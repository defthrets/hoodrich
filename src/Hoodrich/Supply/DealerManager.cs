using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.Territory;
using Hoodrich.UI;

namespace Hoodrich.Supply
{
    /// <summary>
    /// Puts dealers in the world and keeps track of who is standing where.
    ///
    /// At most one dealer is live at a time: whoever belongs in the zone the player is
    /// currently in. Their spot is chosen once per zone and remembered for the session, so a
    /// dealer stays on the same corner while you are working that block rather than teleporting
    /// around it.
    /// </summary>
    internal sealed class DealerManager
    {
        private const float SpawnMinDistance = 35f;
        private const float SpawnMaxDistance = 85f;
        private const float DespawnRange = 160f;
        private const float TalkRange = 3.2f;
        private const int UpdateIntervalMs = 750;

        /// <summary>
        /// Which relationship group a gang id belongs to, asked of whoever knows.
        ///
        /// A hook rather than a reference to the registry, because this class has never needed
        /// to know what a gang IS and should not start now -- it needs one number about one of
        /// them, once, at spawn.
        /// </summary>
        public Func<string, int> GroupFor;

        private readonly List<DealerDef> _defs = new List<DealerDef>();
        private readonly Random _rng = new Random();
        private Settings _cfg;

        /// <summary>Chosen corner per zone, so a dealer keeps his pitch for the session.</summary>
        private readonly Dictionary<string, Vector3> _pitches =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        private const float MeetMinDistance = 100f;
        private const float MeetMaxDistance = 190f;
        private const int MeetTimeoutMs = 15 * 60 * 1000;

        private DealerDef _liveDef;
        private Ped _livePed;
        private Blip _liveBlip;
        private string _liveZone = "";
        private int _lastUpdate;
        private bool _greeted;

        /// <summary>A phoned-in rendezvous. Overrides whoever would otherwise be posted here.</summary>
        private DealerDef _meetDef;
        private Vector3 _meetSpot;
        private int _meetStartedAt;

        public DealerDef MeetDealer => _meetDef;

        public bool HasMeet => _meetDef != null;

        public float MeetDistance
        {
            get
            {
                if (_meetDef == null) return 0f;
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return 0f;
                return player.Position.DistanceTo(_meetSpot);
            }
        }

        public IReadOnlyList<DealerDef> All => _defs;

        public float LiveDistance
        {
            get
            {
                if (_livePed == null || !_livePed.Exists()) return float.MaxValue;
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return float.MaxValue;
                return player.Position.DistanceTo(_livePed.Position);
            }
        }

        /// <summary>The dealer if the player is close enough to talk to them.</summary>
        public DealerDef InReach => LiveDistance <= TalkRange ? _liveDef : null;

        // ---- loading -----------------------------------------------------------

        /// <summary>
        /// Loads the contacts.
        ///
        /// dealers.json is AUTHORITATIVE, not a patch. The built-ins used to be seeded first and
        /// then merely overridden, which meant deleting somebody from the file did nothing at
        /// all -- Lil' Marcus and five other corner dealers kept turning up weeks after they
        /// were removed, because nothing had actually removed them. The defaults are now only a
        /// fallback for when there is no usable file at all.
        /// </summary>
        public static DealerManager Load(Settings cfg)
        {
            var mgr = new DealerManager { _cfg = cfg };

            var path = Path.Combine(Paths.Data, "dealers.json");
            var doc = JsonFile.Read(path);

            var list = doc == null ? null : (doc.Kind == JsonKind.Array ? doc : doc["dealers"]);
            var hasFile = list != null && list.Kind == JsonKind.Array;

            if (hasFile) mgr.ApplyOverrides(doc);
            else
            {
                Log.Warn("No usable dealers.json; falling back to the built-in contacts.");
                mgr.AddDefaults();
            }

            Log.Info("Dealers loaded: " + mgr._defs.Count + ".");
            return mgr;
        }

        private void ApplyOverrides(Json doc)
        {
            var list = doc.Kind == JsonKind.Array ? doc : doc["dealers"];
            if (list.Kind != JsonKind.Array) return;

            foreach (var node in list.Items)
            {
                var id = node["id"].AsString(null);
                if (string.IsNullOrEmpty(id)) continue;

                var def = _defs.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
                var isNew = def == null;
                if (isNew) def = new DealerDef { Id = id };

                Apply(def, node, id);

                // Kept, not applied. See Handover.
                var after = node["afterCheng"];
                if (after.Kind == JsonKind.Object) _afterCheng[def.Id] = after;

                if (isNew) _defs.Add(def);
            }
        }

        /// <summary>Every field a dealer reads out of one json object.</summary>
        private void Apply(DealerDef def, Json node, string id)
        {
            def.Name = node["name"].AsString(def.Name.Length > 0 ? def.Name : id);
            def.Tag = node["tag"].AsString(def.Tag);
            def.GangId = node["gangId"].AsString(def.GangId);
            def.PriceMultiplier = Math.Max(0.1f, node["priceMultiplier"].AsFloat(def.PriceMultiplier));
            def.MinRank = Math.Max(0, node["minRank"].AsInt(def.MinRank));
            def.MaxOrderGrams = Math.Max(1f, node["maxOrderGrams"].AsFloat(def.MaxOrderGrams));
            def.OpenHour = node["openHour"].AsInt(def.OpenHour);
            def.CloseHour = node["closeHour"].AsInt(def.CloseHour);

            def.Greeting = node["greeting"].AsString(def.Greeting);
            def.BuyLine = node["buyLine"].AsString(def.BuyLine);
            def.ShopLine = node["shopLine"].AsString(def.ShopLine);
            def.ShopCutLine = node["shopCutLine"].AsString(def.ShopCutLine);
            def.NoRoomLine = node["noRoomLine"].AsString(def.NoRoomLine);
            def.NoSpaceLine = node["noSpaceLine"].AsString(def.NoSpaceLine);
            def.HandOverLine = node["handOverLine"].AsString(def.HandOverLine);
            def.WalkItInLine = node["walkItInLine"].AsString(def.WalkItInLine);
            def.SourceReply = node["sourceReply"].AsString(def.SourceReply);
            def.SourceTooSoon = node["sourceTooSoon"].AsString(def.SourceTooSoon);
            def.Farewell = node["farewell"].AsString(def.Farewell);

            var kind = node["kind"].AsString(def.Kind.ToString());
            try { def.Kind = (DealerKind)Enum.Parse(typeof(DealerKind), kind, true); }
            catch { Log.Warn("Unknown dealer kind '" + kind + "' on " + id + "."); }

            def.DeliveryOnly = node["deliveryOnly"].AsBool(def.DeliveryOnly);

            ReplaceList(def.Models, node["models"]);
            ReplaceList(def.Drugs, node["drugs"]);
            ReplaceList(def.Rides, node["rides"]);
            def.RidePaint = node["ridePaint"].AsInt(def.RidePaint);
            def.RideOwn = node["rideOwn"].AsBool(def.RideOwn);
            def.RideSecondary = node["rideSecondary"].AsInt(def.RideSecondary);
            def.RidePearl = node["ridePearl"].AsInt(def.RidePearl);

            ReplaceInts(def.RideMods, node["rideMods"]);
            ReplaceInts(def.RideToggles, node["rideToggles"]);
            ReplaceInts(def.RideNeon, node["rideNeon"]);
            def.Plate = node["plate"].AsString(def.Plate);
            def.OpeningText = node["openingText"].AsString(def.OpeningText);
            def.Portrait = node["portrait"].AsString(FaceFor(def.Id));
            // A string or a list of them, so an old file keeps working unchanged.
            ReadLines(node["textCalled"], def.CalledLines, ref def.TextCalled);
            ReadLines(node["textLeaving"], def.LeavingLines, ref def.TextLeaving);
            ReadLines(node["textOutside"], def.OutsideLines, ref def.TextOutside);
            def.LotValue = node["lotValue"].AsFloat(def.LotValue);
            def.LotStep = node["lotStep"].AsFloat(def.LotStep);
            def.PriceFloor = (int)node["priceFloor"].AsFloat(def.PriceFloor);
            def.PriceStep = Math.Max(1, (int)node["priceStep"].AsFloat(def.PriceStep));
            def.Drunk = node["drunk"].AsBool(def.Drunk);
            ReplaceList(def.Zones, node["zones"]);

            def.Purity = Math.Max(Economy.Stash.MinPurity,
                                  Math.Min(Economy.Stash.MaxPurity,
                                           node["purity"].AsFloat(def.Purity)));

            def.PurePurity = Math.Max(Economy.Stash.MinPurity,
                                      Math.Min(Economy.Stash.MaxPurity,
                                               node["purePurity"].AsFloat(def.PurePurity)));
            def.PureMultiplier = Math.Max(0.2f, node["pureMultiplier"].AsFloat(def.PureMultiplier));
            def.PureChancePercent =
                Math.Max(0f, Math.Min(100f, node["pureChancePercent"].AsFloat(def.PureChancePercent)));
            def.UncutFromLots = Math.Max(0.01f, node["uncutFromLots"].AsFloat(def.UncutFromLots));

            def.SpotX = node["x"].AsFloat(def.SpotX);
            def.SpotY = node["y"].AsFloat(def.SpotY);
            def.SpotZ = node["z"].AsFloat(def.SpotZ);
            def.SpotHeading = node["heading"].AsFloat(def.SpotHeading);
            def.NumberLine = node["numberLine"].AsString(def.NumberLine);
            def.JoinAsk = node["joinAsk"].AsString(def.JoinAsk);
            def.JoinAccept = node["joinAccept"].AsString(def.JoinAccept);
            def.JoinRefuse = node["joinRefuse"].AsString(def.JoinRefuse);
            def.JoinAlready = node["joinAlready"].AsString(def.JoinAlready);

            ReplaceList(def.ArrivalLines, node["arrivalLines"]);
            ReplaceList(def.CarryLines, node["carryLines"]);
            ReplaceList(def.DropLines, node["dropLines"]);
            ReplaceList(def.PartingLines, node["partingLines"]);
        }

        /// <summary>
        /// A second set of fields for the same dealer, applied when the world changes under him.
        ///
        /// The port does not stop working because Tao stopped standing on it -- the boats still
        /// tie up, the containers still come off, and the family that owns the paperwork is
        /// perfectly capable of putting somebody sober on the dock. So the DEALER survives and
        /// his identity is replaced: name, face, models, voice, every line and the price.
        ///
        /// Done by re-reading him out of a second block in dealers.json through exactly the
        /// same code that read the first one, rather than by branching every accessor on a
        /// flag. One path, one set of rules, and the man who ends up standing there is as real
        /// as the man who was there before him.
        /// </summary>
        private readonly Dictionary<string, Json> _afterCheng =
            new Dictionary<string, Json>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Hands a dealer's pitch to whoever comes next, and clears the man who was on it.
        ///
        /// The live ped goes with him. Leaving him standing there would be the old face on the
        /// new name, and the first thing the player does after telling the old man is drive
        /// down to the port to see whether it was true.
        /// </summary>
        public void Handover(string id, PlayerState state = null)
        {
            Json after;
            if (!_afterCheng.TryGetValue(id ?? "", out after)) return;

            var def = _defs.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (def == null) return;

            Apply(def, after, def.Id);

            // Only if HE is the one stood there. Despawn clears whichever dealer is live, and
            // taking away a different man because this one changed his name is not the job.
            try
            {
                if (_liveDef != null &&
                    string.Equals(_liveDef.Id, def.Id, StringComparison.OrdinalIgnoreCase))
                {
                    Despawn();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not clear the old plug: " + ex.Message);
            }

            // He introduces himself, because his number is not the old one and nobody else is
            // going to tell you that. The offer is un-marked rather than sent by hand, so the
            // new man's text arrives through exactly the path the old man's did.
            if (state != null) state.ForgetOffered("plug:" + def.Id);

            Log.Info("The port changed hands: " + id + " is now " + def.Name + ".");
        }

        private static void ReplaceList(List<string> target, Json node)
        {
            if (node.Kind != JsonKind.Array) return;
            target.Clear();
            foreach (var s in node.AsStringList()) target.Add(s);
        }

        /// <summary>The same, for the mod slots on somebody's own car. See DealerDef.RideOwn.</summary>
        private static void ReplaceInts(List<int> target, Json node)
        {
            if (node.Kind != JsonKind.Array) return;
            target.Clear();
            for (var i = 0; i < node.Count; i++) target.Add(node[i].AsInt(-1));
        }

        /// <summary>
        /// Built-in dealers: one corner dealer per gang, plus the dock worker.
        ///
        /// Gang dealers carry only what their crew moves, and stand on that crew's turf. The
        /// dock worker carries everything (empty drug list) but does not exist for the player
        /// until a corner dealer tells them he does.
        /// </summary>
        private void AddDefaults()
        {
            // The gang corner dealers used to live here -- one per set, placed at a random
            // pitch near the player whenever you stood on your own crew's turf.
            //
            // Gone, because they were the same idea twice. The gang LEADERS already do this
            // job and do it better: they stand in one place you can learn, they have names and
            // scripts and a history with you, and finding one is an event. A second man who
            // does the same thing but turns up at a different spot every time you walk down
            // the road turns that into weather -- and it put a blue blip on the map wherever
            // you happened to be standing, which is the opposite of somewhere to go.

            var docks = new DealerDef
            {
                Id = "docks",
                Name = "Tao Cheng",
                Tag = "DOCK",
                Kind = DealerKind.Docks,
                PriceMultiplier = 0.75f,
                MinRank = 0,
                MaxOrderGrams = 500f,
                OpenHour = 5,
                CloseHour = 21,
                Greeting = "Marcus's people, right? Keep it quiet and keep it quick. " +
                           "Whatever's in the box, I can get it off the box.",
                BuyLine = "Anything you want, in weight. Say what and say how much.",
                SourceReply = "Where do I get it? Man, I AM where you get it.",
                SourceTooSoon = "",
                Farewell = "Off you go. Don't come back in daylight if you're carrying."
            };
            // One model, not four. He is somebody you ring up and meet by name, and turning
            // up with a different face every delivery is what made him read as a spawn rather
            // than a contact.
            docks.Models.AddRange(new[] { "ig_taocheng", "u_m_y_ushi" });
            // Drugs deliberately empty: the docks carry the whole catalogue.
            docks.Zones.AddRange(new[] { "ELYSIAN", "ZP_ORT", "TERMINA", "BANNING" });
            _defs.Add(docks);
        }

        // ---- what they actually have on them -----------------------------------

        /// <summary>Per dealer, per product, grams on hand. Depletes as you buy.</summary>
        private readonly Dictionary<string, Dictionary<string, float>> _stock =
            new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Dealers who simply have nothing this visit.</summary>
        private readonly HashSet<string> _dry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>When somebody is next allowed to ring out of the blue.</summary>
        private int _nextCall;

        /// <summary>When every dealer next re-decides whether tonight is an uncut night.</summary>
        private int _nextPure;

        /// <summary>
        /// Who has got hold of something clean, re-rolled on the restock clock.
        ///
        /// On the same timer as everything else about a dealer's day rather than its own, so a
        /// player who has learned when the corner restocks has also learned when to check for
        /// this. One clock is a thing you can plan around; three is weather.
        /// </summary>
        private void RollPure()
        {
            var now = Game.GameTime;
            var everyMs = (int)(Math.Max(1f, _cfg.DealerRestockMinutes) * 60_000f);

            if (_nextPure != 0 && now - _nextPure < everyMs) return;
            _nextPure = now;

            for (var i = 0; i < _defs.Count; i++)
            {
                var d = _defs[i];
                if (d == null) continue;

                d.PureTonight = d.PureChancePercent > 0f &&
                                _rng.NextDouble() * 100.0 < d.PureChancePercent;
            }
        }

        /// <summary>
        /// A plug rings round when he wants something gone.
        ///
        /// Gated behind having actually met people -- an unsolicited discount from a man whose
        /// name you do not know is not a favour, it is a menu popping up. It also only ever
        /// fires for somebody you could reach: the offer is a race against a clock, and a race
        /// you cannot enter is just a message telling you what you are missing.
        /// </summary>
        private void ColdCalls(PlayerState state, Affiliation crew)
        {
            if (!_cfg.ColdCallsEnabled || state == null) return;

            var now = Game.GameTime;

            if (_nextCall == 0)
            {
                // Not on the first tick of a session. Being texted a deal before the loading
                // screen has finished reads as a mod announcing itself.
                _nextCall = now + (int)(_cfg.ColdCallEveryMinutes * 60_000f);
                return;
            }

            if (now < _nextCall) return;
            _nextCall = now + (int)(Math.Max(1f, _cfg.ColdCallEveryMinutes) * 60_000f);

            if (ColdCall.Standing) return;
            if (_rng.NextDouble() * 100.0 >= _cfg.ColdCallChancePercent) return;

            // SOMEBODY HE HAS ACTUALLY STOOD IN FRONT OF.
            //
            // The comment here used to say exactly that and the code did not do it -- the pool
            // was every dealer in the file, so men the player had never met were texting him
            // time-limited discounts on product they had never been introduced over. Which is
            // the same bug the introduction text had, arriving through a different door.
            //
            // Asked through RefusalReason rather than HaveMet alone, because everything else
            // that stops him answering the phone should stop him ringing it too: the wrong
            // rank, the wrong hour, a run of Gerald's still in progress. A man who would refuse
            // the call has no business making it.
            var pool = new List<DealerDef>();

            for (var i = 0; i < _defs.Count; i++)
            {
                var d = _defs[i];
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                if (_dry.Contains(d.Id)) continue;
                if (RefusalReason(d, state, crew, needHome: false) != null) continue;

                pool.Add(d);
            }

            if (pool.Count == 0) return;

            var who = pool[_rng.Next(pool.Count)];
            var drug = who.Drugs.Count > 0 ? who.Drugs[_rng.Next(who.Drugs.Count)] : "";

            var off = 0.15f + (float)_rng.NextDouble() * 0.25f;
            var mins = Math.Max(2f, _cfg.ColdCallMinutes);

            ColdCall.Open(who.Id, drug, off, (int)(mins * 60_000f));

            var pct = (int)Math.Round(off * 100f);

            UI.Notify.Text(UI.Faces.For(who.Name), who.Name, "you around tonight",
                           Line(who, drug, pct, (int)mins), true);

            Log.Info("Cold call: " + who.Name + " at -" + pct + "% for " + (int)mins + " min.");
        }

        /// <summary>What he says. Different men, different reasons to be in a hurry.</summary>
        private string Line(DealerDef who, string drugId, int pct, int mins)
        {
            var what = string.IsNullOrEmpty(drugId) ? "what I got" : drugId;

            switch (_rng.Next(5))
            {
                case 0:
                    return "sittin' on more " + what + " than I got room for. " + pct +
                           " off if you come get it in the next " + mins + " or so. after that " +
                           "it's gone to somebody else, no hard feelings";
                case 1:
                    return "need this " + what + " out my hands tonight. " + pct + " off. " +
                           "don't ask me why and don't take all night about it";
                case 2:
                    return "aye. " + pct + " off the " + what + ", next " + mins +
                           " minutes only.\n\nI'm doin' you a favour so don't have me standin' here";
                case 3:
                    return "got a man comin' for this " + what + " at the end of the night and I " +
                           "would rather it went to you. " + pct + " off. your call";
                default:
                    return "movin' house, basically. " + what + " at " + pct +
                           " off while I'm packin'.\n\nclock's tickin' though";
            }
        }

        private int _lastRestock;

        private Dictionary<string, float> StockFor(string dealerId)
        {
            if (!_stock.TryGetValue(dealerId, out var s))
            {
                s = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                _stock[dealerId] = s;
            }
            return s;
        }

        /// <summary>Grams of a product this dealer can sell right now.</summary>
        public float StockOf(DealerDef def, string drugId)
        {
            if (def == null || string.IsNullOrEmpty(drugId)) return 0f;
            if (_dry.Contains(def.Id)) return 0f;

            var s = StockFor(def.Id);
            if (!s.TryGetValue(drugId, out var grams))
            {
                // First time we have been asked: he is holding a full load.
                grams = _cfg.DealerMaxStockGrams;
                s[drugId] = grams;
            }
            return grams;
        }

        public bool IsDry(DealerDef def) => def != null && _dry.Contains(def.Id);

        /// <summary>Deducts what was bought. Returns how much he could actually supply.</summary>
        /// <summary>
        /// Puts back weight that was taken off him and then not sold.
        ///
        /// TakeStock happens before the stash is asked whether it has room, because he cannot
        /// sell what he is not holding and that has to be settled first. When the stash then
        /// takes less than was handed over -- or none of it, because it is full -- the
        /// difference had nowhere to go and simply stopped existing. The player was charged
        /// correctly either way, so nobody was robbed; the weight just left the world. A
        /// dealer could be emptied by a man with a full stash walking up and failing to buy.
        ///
        /// Capped at what he is allowed to hold, so this can never be used to stack him past
        /// a full load.
        /// </summary>
        public void GiveStock(DealerDef def, string drugId, float grams)
        {
            if (def == null || string.IsNullOrEmpty(drugId) || grams <= 0f) return;

            var s = StockFor(def.Id);
            var have = s.TryGetValue(drugId, out var now) ? now : 0f;

            s[drugId] = Math.Min(_cfg.DealerMaxStockGrams, have + grams);
        }

        public float TakeStock(DealerDef def, string drugId, float grams)
        {
            if (def == null || grams <= 0f) return 0f;

            var have = StockOf(def, drugId);
            var taken = Math.Min(have, grams);
            if (taken <= 0f) return 0f;

            StockFor(def.Id)[drugId] = have - taken;
            return taken;
        }

        /// <summary>Tops every dealer back up over time, so a cleaned-out corner recovers.</summary>
        private void RestockTick()
        {
            if (_cfg.DealerRestockMinutes <= 0f) return;

            var now = Game.GameTime;
            var intervalMs = (int)(_cfg.DealerRestockMinutes * 60_000f);
            if (_lastRestock != 0 && now - _lastRestock < intervalMs) return;

            _lastRestock = now;

            foreach (var kv in _stock)
            {
                var s = kv.Value;
                var keys = new List<string>(s.Keys);
                foreach (var drug in keys)
                {
                    // A third of a full load per interval.
                    s[drug] = Math.Min(_cfg.DealerMaxStockGrams, s[drug] + _cfg.DealerMaxStockGrams / 3f);
                }
            }

            // A dry dealer gets another roll next time he is seen.
            _dry.Clear();
            Log.Debug("Dealers restocked.");
        }

        /// <summary>
        /// Whose face goes on his messages, when the data does not say.
        ///
        /// A fallback rather than the source of truth -- dealers.json can name a portrait for
        /// anybody -- but the two the mod ships with should not have to.
        /// </summary>
        private static string FaceFor(string id)
        {
            switch (id)
            {
                case "docks": return "CHAR_CHENG";
                case GeraldId: return "CHAR_MP_GERALD";
                default: return "CHAR_DEFAULT";
            }
        }

        public DealerDef Get(string id) =>
            _defs.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        public DealerDef ForGang(string gangId)
        {
            if (string.IsNullOrEmpty(gangId)) return null;
            return _defs.Find(d => d.IsGangDealer &&
                                   string.Equals(d.GangId, gangId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The man at the port, by NAME rather than by kind.
        ///
        /// This used to be the first dealer of kind Docks, which was unambiguous while there
        /// was one of them. Gerald delivers now and is the same kind -- he phones, he drives
        /// over, he hands a bag across, which is the same machinery -- so "the first one that
        /// looks like this" would have started answering with whoever happened to sort first
        /// in the file. The port is the port.
        /// </summary>
        public DealerDef Docks() => Find("docks");

        /// <summary>One dealer by id, or null.</summary>
        public DealerDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            return _defs.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        // ---- placement ---------------------------------------------------------

        /// <summary>
        /// Which dealer, if any, belongs in the zone the player is standing in.
        ///
        /// Your own crew's dealer is on your crew's turf. The dock worker is at the port, but
        /// only once someone has told you he exists.
        /// </summary>
        /// <summary>
        /// How near his corner you have to be for him to be standing on it.
        ///
        /// Generously inside DespawnRange so he is already there by the time you can see the
        /// spot, and short enough that two pinned dealers a few streets apart never argue.
        /// </summary>
        private const float SpotRange = 120f;

        /// <summary>
        /// Sends his number the moment the conversation shuts.
        ///
        /// As a TEXT rather than another line of dialogue, because that is the thing that
        /// happened: you now have his number, and the place a number lives is the phone. It
        /// lands in Messages under his name with his face on it, which is also the first thing
        /// in that app that is there because you went and got it.
        /// </summary>
        private void HandOverTheNumber()
        {
            var open = Talk != null && Talk.IsOpen;

            var closed = _talkWasOpen && !open;
            _talkWasOpen = open;

            if (!closed || _owesNumber == null) return;

            var def = _owesNumber;
            _owesNumber = null;

            if (string.IsNullOrEmpty(def.NumberLine)) return;

            try
            {
                UI.Notify.Text(def.Portrait, def.Name, "here's my number", def.NumberLine, true);
                Log.Info("Handed over " + def.Id + "'s number on the way out.");
            }
            catch
            {
                // He said it out loud in the menu either way.
            }
        }

        /// <summary>Whichever pinned dealer is closest, if you are near enough to any of them.</summary>
        private DealerDef NearestPinned(Vector3 from)
        {
            DealerDef best = null;
            var bestAt = SpotRange;

            for (var i = 0; i < _defs.Count; i++)
            {
                var def = _defs[i];
                if (def == null || !def.HasSpot) continue;

                var d = from.DistanceTo(new Vector3(def.SpotX, def.SpotY, def.SpotZ));
                if (d > bestAt) continue;

                best = def;
                bestAt = d;
            }

            return best;
        }

        public DealerDef DealerForZone(string zoneCode, Affiliation crew, PlayerState state)
        {
            if (string.IsNullOrEmpty(zoneCode)) return null;

            var docks = Docks();
            if (docks != null && state.DocksUnlocked && ZoneMatches(docks.Zones, zoneCode)) return docks;

            // Independents stand in their own patch and answer to nobody, so they are found by
            // zone alone -- no affiliation, no unlock.
            foreach (var def in _defs)
            {
                if (def.Kind != DealerKind.Independent) continue;
                if (ZoneMatches(def.Zones, zoneCode)) return def;
            }

            // And that is the lot. Nothing is placed on a corner because you are affiliated.
            //
            // This used to fall through to ForGang(crew), which looks a dealer up by GANG and
            // not by kind -- so with the corner dealers gone it found Gerald's delivery
            // contact, who is a families dealer, and stood him on a random pavement with a
            // blip on him. A man you ring up and who drives to you is not a man who is also
            // permanently loitering on your block; being on somebody's turf is a reason for
            // THEM to be somewhere, not a reason for a stranger to appear next to you.
            return null;
        }

        /// <summary>
        /// Reads a message field that may be one line or several.
        ///
        /// A bare string fills the singular field and nothing else, which is exactly what a
        /// dealer with one thing to say should do. An array fills the pool, and the first entry
        /// also lands in the singular field so anything still reading that gets something
        /// sensible rather than an empty string.
        /// </summary>
        private static void ReadLines(Json node, List<string> pool, ref string single)
        {
            if (node == null) return;

            if (node.Kind == JsonKind.Array)
            {
                pool.Clear();

                foreach (var item in node.Items)
                {
                    var line = item.AsString("");
                    if (!string.IsNullOrEmpty(line)) pool.Add(line);
                }

                if (pool.Count > 0) single = pool[0];
                return;
            }

            single = node.AsString(single);
        }

        private static bool ZoneMatches(List<string> zones, string zoneCode)
        {
            foreach (var z in zones)
            {
                if (string.Equals(z, zoneCode, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ---- phoning ahead -----------------------------------------------------

        /// <summary>
        /// Calls a dealer out to meet you. Returns a player-facing refusal, or null.
        ///
        /// Phoning does not skip the trip -- it picks a rendezvous and blips it, and you still
        /// have to drive there and stand in front of them. The point is that you do not have to
        /// be on their block to reach them, not that you can buy from the map screen.
        /// </summary>
        public string ArrangeMeet(DealerDef def, PlayerState state, Affiliation crew)
        {
            if (def == null) return "No such contact.";
            if (_meetDef != null) return "You already have " + _meetDef.Name + " coming out.";

            var refusal = RefusalReason(def, state, crew);
            if (refusal != null) return def.Name + " won't come out: " + refusal + ".";

            // Standing in front of them already.
            if (InReach != null && _liveDef != null && _liveDef.Id == def.Id)
            {
                return "They are right in front of you.";
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return "Not right now.";

            if (!TryMeetSpot(player.Position, out _meetSpot))
            {
                return "Nowhere good to meet round here. Try somewhere less remote.";
            }

            // A meet takes over the world slot; whoever was posted here stands down.
            Despawn();

            _meetDef = def;
            _meetStartedAt = Game.GameTime;

            MarkMeet(def);

            Bark(AgreeLines);
            Notify.Important("~y~" + def.Name + "~s~ is on their way. Marked on your map.");
            Log.Info("Meet arranged with " + def.Id + " at " + _meetSpot + ".");
            return null;
        }

        /// <summary>
        /// Puts the rendezvous on the map the moment it is arranged.
        ///
        /// The notification says "marked on your map" and, until now, nothing was: the only
        /// blip came off the DEALER, and he does not exist until you are already within a
        /// hundred and sixty metres of the spot -- which is most of the way there. So you were
        /// told to go somewhere and given no way to find it.
        /// </summary>
        private void MarkMeet(DealerDef def)
        {
            ClearMeetBlip();

            try
            {
                _meetBlip = World.CreateBlip(_meetSpot);
                if (_meetBlip == null || !_meetBlip.Exists()) return;

                _meetBlip.Sprite = BlipSprite.Friend;
                _meetBlip.Color = BlipColor.Yellow;
                _meetBlip.Name = def.Name;
                _meetBlip.ShowRoute = true;
                _meetBlip.Scale = 0.9f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the meet: " + ex.Message);
            }
        }

        private void ClearMeetBlip()
        {
            try { if (_meetBlip != null && _meetBlip.Exists()) _meetBlip.Delete(); }
            catch { /* teardown */ }

            _meetBlip = null;
        }

        private Blip _meetBlip;

        public void CancelMeet(string reason)
        {
            if (_meetDef == null) return;

            var name = _meetDef.Name;
            _meetDef = null;

            ClearMeetBlip();
            Despawn();

            if (!string.IsNullOrEmpty(reason)) Notify.Ticker("~o~Meet with " + name + " is off.~s~ " + reason);
        }

        private bool TryMeetSpot(Vector3 origin, out Vector3 spot)
        {
            spot = Vector3.Zero;

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var angle = _rng.NextDouble() * Math.PI * 2.0;
                var distance = MeetMinDistance + (float)_rng.NextDouble() * (MeetMaxDistance - MeetMinDistance);

                var candidate = origin + new Vector3(
                    (float)Math.Cos(angle) * distance, (float)Math.Sin(angle) * distance, 0f);

                Vector3 onFoot;
                try { onFoot = World.GetNextPositionOnSidewalk(candidate); }
                catch { continue; }

                if (onFoot == Vector3.Zero) continue;
                if (onFoot.DistanceTo(origin) < MeetMinDistance * 0.5f) continue;

                try
                {
                    if (World.GetGroundHeight(onFoot, out var groundZ, GetGroundHeightMode.Normal))
                    {
                        onFoot.Z = groundZ;
                    }
                }
                catch
                {
                    // Sidewalk Z is usually fine already.
                }

                spot = onFoot;
                return true;
            }

            return false;
        }

        /// <summary>Set by Main: whether the player is at the stash house right now.</summary>
        public Func<bool> AtHome;

        /// <param name="needHome">
        /// Whether standing at the house is required. True everywhere the player is trying to
        /// reach HIM -- he brings a box to a door and there has to be a door. False when the
        /// question is whether HE would ring YOU, because an offer arriving on a phone is not a
        /// delivery and waiting until somebody happens to be stood in their aunt's front room
        /// before telling them about it is not a cold call, it is a coincidence.
        /// </param>
        public string RefusalReason(DealerDef def, PlayerState state, Affiliation crew,
                                    bool needHome = true)
        {
            if (def == null) return "No such contact.";

            // The PORT's lock, and only the port's.
            //
            // Both entries are Docks-kind because both drive a box to your door -- that is what
            // the kind means here, a delivery rather than a corner. So this was telling you that
            // you do not know anybody at the port while you were looking at the man from your own
            // block, who has never been near it. He is a gang dealer; the port is not.
            if (def.Kind == DealerKind.Docks && !def.IsGangDealer && !state.DocksUnlocked)
            {
                return "You don't know nobody at the port";
            }

            // And not while you are in the middle of his errand.
            //
            // DocksUnlocked goes true the moment he HANDS you the run, which is right for the
            // run and wrong for everything else: it made him a contact you could ring for a
            // delivery while you were still driving his load across the city, and it
            // fired his introduction text at you two minutes into the job he had just given
            // you. He is somebody you know once you have finished, not once you have agreed.
            if (def.Kind == DealerKind.Docks && !def.IsGangDealer
                && state.PortRunStage != Missions.PortRun.StageNone)
            {
                return "Finish his run first";
            }

            // He brings a box to a door, so there has to be a door. Checked here as well as in
            // Delivery itself, so the wheel greys the option out and says why rather than
            // letting you press it and be told no.
            // ANY of them, not just the port.
            //
            // They all bring it to the door now -- there is no rendezvous option left, so the
            // rule that used to apply only to the two Docks contacts applies to all fourteen.
            // The alternative was a phone menu where half the numbers worked anywhere and half
            // did not, with nothing on screen to say which was which.
            if (needHome && AtHome != null && !AtHome())
            {
                return "Call him from the house";
            }

            // NOT A CONTACT UNTIL YOU HAVE MET HIM.
            //
            // Every plug used to text you his introduction the moment you cleared his rank
            // requirement -- a stranger you had never seen, on a corner you had never been to,
            // announcing himself and going straight into your contacts. That is a menu
            // unlocking, not a person you know.
            //
            // Checked BEFORE rank so the reason reads as the real one. A man you have not met
            // is not somebody you need a promotion to ring; he is somebody you have not met.
            //
            // This gates all three phone-side doors at once, because they all come through
            // here: the introduction text, the Contacts row and the delivery. It does NOT gate
            // him spawning on his own block, which is the whole point -- he is out there to be
            // found, and finding him is what hands you the number.
            if (!HaveMet(def, state))
            {
                return "You ain't met him";
            }

            if (state.Rank < def.MinRank)
            {
                return "Need rank " +
                       PlayerState.RankNames[Math.Min(def.MinRank, PlayerState.RankNames.Length - 1)];
            }

            if (!def.IsOpenAt(Pricing.ClockHour))
            {
                return "Works " + def.OpenHour + ":00-" + def.CloseHour + ":00";
            }

            if (!def.IsGangDealer) return null;

            if (!crew.IsAffiliated) return "You don't run with nobody";

            if (!string.Equals(def.GangId, crew.Current.Id, StringComparison.OrdinalIgnoreCase))
            {
                return "Not the gang you run with";
            }

            return null;
        }

        // ---- per-tick ----------------------------------------------------------

        /// <summary>
        /// A text from a plug the first time he is actually reachable.
        ///
        /// Lamar tells you when there is work. Nobody told you when there was GEAR -- a plug
        /// went from refusing you to serving you on a rank you happened to cross while doing
        /// something else, and the only way to find out was to open the wheel and read a menu
        /// that had stopped saying no.
        ///
        /// Once per plug per save, tracked by id on PlayerState alongside the jobs, because
        /// "he is open to you now" is a thing that happens once and should survive a reload.
        /// </summary>
        private void TextIfNewlyOpen(PlayerState state, Affiliation crew)
        {
            if (state == null) return;
            if (Game.GameTime < _nextOpenCheck) return;
            _nextOpenCheck = Game.GameTime + OpenCheckMs;

            foreach (var def in All)
            {
                if (def == null) continue;

                var key = "plug:" + def.Id;
                if (state.HasBeenOffered(key)) continue;
                // needHome: false for the same reason the cold calls use it. This is him
                // texting to say he is reachable, which is a thing that happens wherever you
                // are standing -- gating it on being at the house meant an introduction that
                // waited for you to go home, and half the time you already had his number by
                // then because you had walked up to him again.
                if (RefusalReason(def, state, crew, needHome: false) != null) continue;

                state.MarkOffered(key);
                state.Touch();

                var carries = def.Drugs.Count == 0
                    ? "whatever you need"
                    : string.Join(", ", def.Drugs.ToArray());

                Notify.Text(def.Portrait, def.Name, "Los Santos",
                            def.OpeningText.Length > 0
                                ? def.OpeningText
                                : "im good for " + carries + " when you are. hit me",
                            false);

                Log.Info("Plug " + def.Id + " texted that he is open.");
                return;
            }
        }

        private int _nextOpenCheck;

        /// <summary>Rank does not move fast. Every few seconds is more than enough.</summary>
        private const int OpenCheckMs = 6000;

        public void Update(TurfWatch turf, Affiliation crew, PlayerState state)
        {
            ColdCalls(state, crew);

            TextIfNewlyOpen(state, crew);

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            RestockTick();

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            // A phoned meet takes priority over whoever is posted on this block.
            if (_meetDef != null)
            {
                if (now - _meetStartedAt > MeetTimeoutMs)
                {
                    CancelMeet("They got tired of waiting.");
                    return;
                }

                if (_livePed != null && _livePed.Exists() && !_livePed.IsAlive)
                {
                    CancelMeet("Your contact is dead.");
                    return;
                }

                var toMeet = player.Position.DistanceTo(_meetSpot);

                if (_livePed == null && toMeet <= DespawnRange)
                {
                    SpawnAt(_meetDef, _meetSpot, "", player, route: true);

                    // He is here, so the marker on the spot gives way to the one on the man.
                    ClearMeetBlip();
                }
                else if (_livePed != null && toMeet > DespawnRange)
                {
                    Despawn();
                }

                return;
            }

            RollPure();
            MarkTheOnesHeKnows();

            var zone = turf.ZoneCode;

            // AN ADDRESS BEATS A ZONE. Whoever is pinned nearest wins, and only then does the
            // old zone lottery get a say -- so a man with a spot is at his spot whatever the
            // game thinks the neighbourhood is called there. Several of them stand on the far
            // side of a boundary from the zone they belong to, which is exactly the sort of
            // thing that is true of real corners and hopeless to express as a zone list.
            var wanted = NearestPinned(player.Position) ?? DealerForZone(zone, crew, state);

            // Dealer keeps shop hours.
            if (wanted != null && !wanted.IsOpenAt(Pricing.ClockHour)) wanted = null;

            if (_liveDef != null && (wanted == null || wanted.Id != _liveDef.Id || zone != _liveZone))
            {
                Despawn();
            }

            if (wanted == null) return;

            // Dead dealer stays dead for this visit.
            if (_livePed != null && _livePed.Exists() && !_livePed.IsAlive) return;

            if (_livePed == null)
            {
                if (wanted.HasSpot)
                {
                    var pin = new Vector3(wanted.SpotX, wanted.SpotY, wanted.SpotZ);

                    SpawnAt(wanted, pin, zone, player, false);

                    // Only the pinned ones get people. A dealer the game puts on a different
                    // pavement every visit has no corner for anybody to be stood on.
                    _stoop.Gather(wanted, pin, wanted.SpotHeading);
                    return;
                }

                if (TryPitch(zone, player.Position, out var spot)) SpawnAt(wanted, spot, zone, player, false);
                return;
            }

            if (player.Position.DistanceTo(_livePed.Position) > DespawnRange) Despawn();

            HideHimUntilYouAreClose(player);
        }

        /// <summary>
        /// An unknown dealer has no marker until you are nearly on top of him.
        ///
        /// IsShortRange was not enough on its own. It only stops a blip drawing on the big map
        /// -- the minimap still showed it from most of the way down a street, so every corner
        /// in the city announced itself as soon as it streamed in and there was nothing left to
        /// find. The blip has to not EXIST, which means making and unmaking it on distance.
        ///
        /// Only for men he has not met. Once somebody is a contact the permanent mark is on the
        /// map anyway and hiding the live one would be hiding a fact he already owns.
        /// </summary>
        private void HideHimUntilYouAreClose(Ped player)
        {
            if (_livePed == null || !_livePed.Exists() || _liveDef == null) return;
            if (HaveMet(_liveDef, State)) return;

            // A meet he asked for keeps its route: he is coming BECAUSE you called him.
            if (_liveBlip != null && _liveBlip.Exists() && _liveBlip.ShowRoute) return;

            var near = player.Position.DistanceTo(_livePed.Position) <= ShowMarkerWithin;
            var have = _liveBlip != null && _liveBlip.Exists();

            if (near == have) return;

            try
            {
                if (near)
                {
                    CreateBlip(_liveDef, false);
                    return;
                }

                _liveBlip.Delete();
                _liveBlip = null;
            }
            catch
            {
                // A marker that will not appear is a search that is slightly harder.
            }
        }

        private void SpawnAt(DealerDef def, Vector3 spot, string zone, Ped player, bool route)
        {
            var model = ResolveModel(def);
            if (model == null) return;

            try
            {
                // THE HEADING HE WAS GIVEN, if he was given one.
                //
                // Every pinned spot was stood on and read off the coordinate HUD facing
                // something -- a wall, a road, a shop door -- and that facing is half of why
                // the spot was chosen. Spawning him on the right pavement pointing a random
                // way round is a man who happens to be there rather than a man posted there.
                //
                // Random stays for anybody unpinned, where there is nothing to face.
                var heading = def.HasSpot
                    ? def.SpotHeading
                    : (float)(_rng.NextDouble() * 360.0);

                _livePed = World.CreatePed(model.Value, spot, heading);
                if (_livePed == null || !_livePed.Exists())
                {
                    Log.Warn("CreatePed returned nothing for dealer " + def.Id + ".");
                    return;
                }

                var h = _livePed.Handle;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);

                // A man selling for a set is IN that set, and this never said so.
                //
                // The same fault as the one that had Lamar and his own men shooting each other
                // in his yard: a ped with no relationship group is in nobody's, so when a raid
                // sets the Families to hate whoever turned up, the Families man stood on the
                // corner is not covered by it. Gerald's corner is one of the three places a
                // raid musters, so this one was waiting to happen in the same way.
                //
                // Only gang dealers. The man at the port sells to everybody and belongs to
                // none of them, which is the whole reason you can buy off him.
                if (def.IsGangDealer && GroupFor != null)
                {
                    try
                    {
                        var group = GroupFor(def.GangId);

                        if (group != 0)
                        {
                            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h, group);
                            Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, h, false, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Could not put " + def.Id + " in his set: " + ex.Message);
                    }
                }
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, h, "WORLD_HUMAN_STAND_IMPATIENT", 0, true);

                _livePed.IsPersistent = true;
                _livePed.BlockPermanentEvents = true;

                _liveDef = def;
                _liveZone = zone;
                _greeted = false;

                // Some days he just has nothing. Rolled once, the first time he posts up.
                if (!_stock.ContainsKey(def.Id) &&
                    _rng.NextDouble() * 100.0 < _cfg.DealerDryChancePercent)
                {
                    _dry.Add(def.Id);
                    Log.Debug("Dealer " + def.Id + " is dry today.");
                }

                CreateBlip(def, route);
                Log.Info("Dealer " + def.Id + (route ? " arrived at the meet." : " posted up in " + zone + "."));
            }
            catch (Exception ex)
            {
                Log.Error("Could not spawn dealer " + def.Id, ex);
            }
            finally
            {
                try { model.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        /// <summary>
        /// Finds this zone's corner. Chosen once and cached, so the dealer is in the same place
        /// every time you come back to that block during a session.
        /// </summary>
        private bool TryPitch(string zone, Vector3 origin, out Vector3 spot)
        {
            if (_pitches.TryGetValue(zone, out spot))
            {
                // Only reuse it if it is close enough to actually stream in.
                if (spot.DistanceTo(origin) <= DespawnRange) return true;
            }

            for (var attempt = 0; attempt < 10; attempt++)
            {
                var angle = _rng.NextDouble() * Math.PI * 2.0;
                var distance = SpawnMinDistance + (float)_rng.NextDouble() * (SpawnMaxDistance - SpawnMinDistance);

                var candidate = origin + new Vector3(
                    (float)Math.Cos(angle) * distance, (float)Math.Sin(angle) * distance, 0f);

                Vector3 onFoot;
                try { onFoot = World.GetNextPositionOnSidewalk(candidate); }
                catch { continue; }

                if (onFoot == Vector3.Zero) continue;

                // It has to actually be in the zone we think it is.
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, onFoot.X, onFoot.Y, onFoot.Z);
                if (!string.Equals(code, zone, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    if (World.GetGroundHeight(onFoot, out var groundZ, GetGroundHeightMode.Normal))
                    {
                        onFoot.Z = groundZ;
                    }
                }
                catch
                {
                    // Sidewalk Z is usually fine already.
                }

                _pitches[zone] = onFoot;
                spot = onFoot;
                return true;
            }

            return false;
        }

        /// <summary>Gerald. The one dealer whose mark stays on the radar from anywhere.</summary>
        private const string GeraldId = "stretch_run";

        private void CreateBlip(DealerDef def, bool route)
        {
            // ONCE YOU HAVE MET HIM THERE IS NOTHING LEFT FOR THIS BLIP TO SAY.
            //
            // It was the thing that made finding the first one a search, and that job is over
            // the moment you have found him: his corner gets a permanent mark, and the mark is
            // the answer to "where does he work". A second blip on the man himself is the same
            // information twice, sat on top of itself on the radar.
            //
            // A ROUTE STILL GETS ONE, because that is a different question. You called him out
            // to meet somewhere, and where he is right now is the whole point of asking.
            if (!route && HaveMet(def, State))
            {
                _liveBlip = null;
                return;
            }

            try
            {
                _liveBlip = _livePed.AddBlip();
                if (_liveBlip == null || !_liveBlip.Exists()) return;

                _liveBlip.Sprite = BlipSprite.Friend;
                _liveBlip.Name = def.Name;

                var gang = def.IsGangDealer && GangById != null ? GangById(def.GangId) : null;

                if (gang != null && gang.BlipColour > 0)
                {
                    Function.Call(Hash.SET_BLIP_COLOUR, _liveBlip.Handle, gang.BlipColour);
                }
                else
                {
                    _liveBlip.Color = def.Kind == DealerKind.Docks ? BlipColor.Blue : BlipColor.White;
                }

                // SHORT RANGE, WHICH IS WHAT MAKES THE FIRST ONE A SEARCH. It used to be
                // visible at map range, so every dealer in the city announced himself the
                // moment he loaded and there was nothing to find.
                //
                // Anything reaching this line is either somebody you have not met or somebody
                // you called out; the one you called out gets the range, because you are trying
                // to get to him.
                _liveBlip.IsShortRange = !route;
                _liveBlip.ShowRoute = route;
                _liveBlip.Scale = 0.85f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip dealer: " + ex.Message);
            }
        }

        private void Despawn()
        {
            // His people go when he does.
            _stoop.Scatter();

            try
            {
                if (_liveBlip != null && _liveBlip.Exists()) _liveBlip.Delete();
            }
            catch { }

            try
            {
                if (_livePed != null && _livePed.Exists())
                {
                    _livePed.MarkAsNoLongerNeeded();
                    _livePed.Delete();
                }
            }
            catch { }

            _liveBlip = null;
            _livePed = null;
            _liveDef = null;
            _liveZone = "";
            _greeted = false;
        }

        private static Model? ResolveModel(DealerDef def)
        {
            foreach (var name in def.Models)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(1500)) continue;
                    return model;
                }
                catch (Exception ex)
                {
                    Log.Debug("Dealer model '" + name + "' failed: " + ex.Message);
                }
            }

            Log.Warn("No usable model for dealer " + def.Id + ".");
            return null;
        }

        // ---- the conversation that opens the game up ----------------------------

        /// <summary>
        /// Starts the run to the port. Set by Main, so this file never has to know how.
        /// </summary>
        public Func<bool> SendToThePort;

        /// <summary>
        /// The player asks their crew's dealer where the product actually comes from.
        ///
        /// This is the one progression gate in the supply chain: until somebody names the
        /// port, the docks are not a place the player can go. He will only say it once you
        /// have moved enough weight to be worth telling -- and even then the answer is a drive
        /// rather than a phone number, which is PortRun's job.
        /// </summary>
        /// <summary>
        /// Asking a plug where the weight comes from.
        ///
        /// Gated on Gerald's PACKAGES rather than on a lifetime grams figure, and it has to be
        /// the same gate he uses or this becomes a way round him: he wants two fronts taken
        /// and cleared before he introduces anybody to the port, and a plug on the same block
        /// answering the identical question off a different number makes that sequence
        /// optional. The parameter is kept so the call sites do not all have to change; it is
        /// simply no longer what decides the answer.
        /// </summary>
        public void AskSource(DealerDef def, PlayerState state, float requiredGrams)
        {
            if (def == null) return;

            if (state.PortRunStage != 0)
            {
                Bark(NoLines);
                Notify.Ticker("~y~He already sent you. Go and do it.~s~");
                return;
            }

            if (state.DocksUnlocked)
            {
                Bark(NoLines);
                Notify.Ticker("~y~He already told you. The port.~s~");
                return;
            }

            if (PackagesUntilSource(state) > 0)
            {
                Bark(NoLines);

                // The ticker rather than a subtitle. This one is a REFUSAL with a reason, and a
                // grunt on its own leaves you standing there not knowing why nothing happened.
                Notify.Ticker("~o~" + (string.IsNullOrEmpty(def.SourceTooSoon)
                    ? "That ain't his to tell you. Square up with Gerald first."
                    : def.SourceTooSoon) + "~s~");
                return;
            }

            if (SendToThePort == null || !SendToThePort()) return;

            state.AddRespect(5f);
            state.Touch();

            Bark(AgreeLines);
        }

        /// <summary>How much more the player has to move before the question will be answered.</summary>
        public static float GramsUntilSource(PlayerState state, float requiredGrams)
        {
            return Math.Max(0f, requiredGrams - state.GramsSold);
        }

        /// <summary>How many of Gerald's packages are still owed before anybody will say.</summary>
        public static int PackagesUntilSource(PlayerState state)
        {
            return state == null ? 2 : Math.Max(0, 2 - state.FrontsDone);
        }

        /// <summary>The conversation panel, and how to build the page for a given dealer.</summary>
        public Conversation Talk;
        public Func<DealerDef, DialogueNode> TalkBuilder;

        /// <summary>
        /// Set by Main. Needed because meeting somebody is a thing that gets remembered, and
        /// UpdatePrompt is the one path into a conversation that is not handed the save.
        /// </summary>
        public PlayerState State;

        /// <summary>
        /// The permanent map marks, one per pinned dealer you have actually met.
        ///
        /// TWO DIFFERENT BLIPS DOING TWO DIFFERENT JOBS. The one on the ped is him being
        /// visible while he is loaded, and it is short range now -- you have to be on the
        /// street before it appears, which is what makes finding somebody the first time an
        /// actual search rather than a marker appearing across the city.
        ///
        /// This one is the address, and it is the reward for the search. It is not on the ped
        /// at all: it sits on the coordinate whether he is spawned or not, whether it is his
        /// opening hours or not, forever. Learning where a man stands should not be something
        /// you have to do twice.
        /// </summary>
        /// <summary>
        /// Whose number is owed, and whether the conversation was open last tick.
        ///
        /// THE TEXT ARRIVES AS YOU WALK AWAY, which is when it actually happens. He says the
        /// line to your face inside the menu and then the number is in your phone -- and the
        /// second half was missing, so the one moment the whole search pays off was a sentence
        /// that scrolled past inside a screen you were already closing.
        ///
        /// Watched rather than called back, because Conversation has no close hook. Open last
        /// tick and shut this one is the same fact from the outside.
        /// </summary>
        private DealerDef _owesNumber;
        private bool _talkWasOpen;

        /// <summary>The three stood round whoever is currently out.</summary>
        private readonly Stoop _stoop = new Stoop();

        private readonly Dictionary<string, Blip> _marks =
            new Dictionary<string, Blip>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Puts a permanent mark on every pinned dealer he has met, and only those.</summary>
        private void MarkTheOnesHeKnows()
        {
            if (State == null) return;

            for (var i = 0; i < _defs.Count; i++)
            {
                var def = _defs[i];
                if (def == null || !def.HasSpot) continue;

                var known = HaveMet(def, State);

                Blip mark;
                var have = _marks.TryGetValue(def.Id, out mark) && mark != null && mark.Exists();

                if (!known)
                {
                    // Never met, or the paint got wiped -- no mark, and no mark left over.
                    if (have) { try { mark.Delete(); } catch { } }
                    if (_marks.ContainsKey(def.Id)) _marks.Remove(def.Id);
                    continue;
                }

                if (have) continue;

                try
                {
                    var at = new Vector3(def.SpotX, def.SpotY, def.SpotZ);

                    mark = World.CreateBlip(at);
                    if (mark == null || !mark.Exists()) continue;

                    // THE CROWN, which is what the leaders had and what the leaders were for.
                    //
                    // 855 is radar_ped_gang_leader -- the game keeps a sprite for exactly this
                    // and it now sits on the men who actually matter, which is these. A dealer
                    // you have found IS the set as far as the player is concerned: he sells
                    // their product, he stands on their corner, and he is the one who puts you
                    // on.
                    Function.Call(Hash.SET_BLIP_SPRITE, mark.Handle, KnownSprite);

                    // And his set's own colour underneath, out of gangs.json rather than a
                    // second palette to keep in step. The independents answer to nobody, so
                    // they keep the plain green.
                    var gang = def.IsGangDealer && GangById != null ? GangById(def.GangId) : null;

                    if (gang != null && gang.BlipColour > 0)
                    {
                        Function.Call(Hash.SET_BLIP_COLOUR, mark.Handle, gang.BlipColour);
                    }
                    else
                    {
                        mark.Color = def.Kind == DealerKind.Docks ? BlipColor.Blue : BlipColor.White;
                    }

                    mark.Name = def.Name + (gang == null ? "" : " -- " + gang.Name);
                    mark.Scale = 0.85f;

                    // SHORT RANGE, SO THE MINIMAP IS NOT A ROW OF CROWNS.
                    //
                    // A long-range blip that is off the edge of the minimap does not disappear,
                    // it CLAMPS -- it slides to the border and sits there pointing at itself.
                    // Thirteen dealers found is thirteen crowns lined up along the top of the
                    // radar the whole time, and none of them tells you anything you can act on:
                    // the one you are near is somewhere in that row with the rest.
                    //
                    // It costs nothing that mattered. Short range is a MINIMAP rule only, so
                    // every pin is still on the pause map exactly as it was -- the thing you
                    // earned by finding him is still there, on the map you open to plan with,
                    // and it now also means something when it shows up on the radar.
                    //
                    // GERALD IS THE EXCEPTION. He is the one you are sent to rather than the
                    // one you find, and a contact you are meant to be able to get to is not a
                    // thing to make you hunt for twice.
                    mark.IsShortRange = def.Id != GeraldId;

                    _marks[def.Id] = mark;

                    Log.Info("Pinned " + def.Id + " on the map for good.");
                }
                catch
                {
                    // A blip that will not create is not worth losing the tick over.
                }
            }
        }

        /// <summary>radar_ped_gang_leader. The crown the leaders used to carry.</summary>
        private const int KnownSprite = 855;

        /// <summary>
        /// How close you have to be before an unknown dealer shows up at all.
        ///
        /// Ten to fifteen metres, which is close enough to be looking straight at him. Short
        /// range was not nearly enough on its own -- it only stops a blip drawing on the big
        /// map, and it still puts a marker on the minimap from most of the way down a street,
        /// so every corner in the city announced itself the moment it streamed in.
        ///
        /// The point of finding somebody is that you have to find them.
        /// </summary>
        private const float ShowMarkerWithin = 13f;

        /// <summary>Set by Main, so a dealer's mark can be his set's colour.</summary>
        public Func<string, Gangs.GangDef> GangById;

        /// <summary>The save key for having stood in front of somebody.</summary>
        public static string MetKey(DealerDef def)
        {
            return def == null ? "" : "met:" + def.Id;
        }

        /// <summary>
        /// Whether you have actually met him, rather than merely qualified for him.
        ///
        /// THE OLD MARKER COUNTS. Before this rule existed, "plug:&lt;id&gt;" was set the moment a
        /// dealer texted his introduction -- which happened on rank alone, but it also happened
        /// to everybody a player has ever actually dealt with. Reading it as proof of a meeting
        /// is generous by exactly the people it is generous to: somebody mid-save who has been
        /// buying off Gerald for hours would otherwise open his phone to find Gerald gone.
        ///
        /// Losing a contact you have a conversation thread with is a far worse bug than an old
        /// save keeping one introduction it did not strictly earn.
        /// </summary>
        public static bool HaveMet(DealerDef def, PlayerState state)
        {
            if (def == null || state == null) return false;

            // THE TWO WHO COME TO YOU ARE NOT MEN YOU FIND.
            //
            // Gerald and the port are Docks kind, which is the mod's word for "drives a box to
            // your door". Neither of them ever stands on a corner, so neither can ever be
            // walked up to -- and the marker is only ever written by walking up to somebody.
            // On a fresh save that made both of them permanently untextable: Gerald, who is the
            // first contact in the mod and introduces himself by text, and Tao, who you meet in
            // the middle of a scripted run at his own yard.
            //
            // They were never the point of the rule. The rule is for the eleven who stand on a
            // corner waiting to be found, and both of these already have gates of their own --
            // the port stays shut until DocksUnlocked, Gerald until you have moved enough for
            // him to bother.
            // AND ANYBODY ELSE WHO ONLY EVER COMES TO YOU. See DealerDef.DeliveryOnly.
            //
            // Hao is the first of these: a car dealer to your face, a phone number for the
            // pills. The moment he stopped standing somewhere you could walk up to, the only
            // thing that could ever write his "met" marker went with it -- and the other way
            // in, the introduction text, is itself gated on having met him. A cycle with no
            // entry, which on a new save means he can never be reached at all.
            //
            // Invisible on any existing save, because the marker was written back when there
            // was a ped there to walk up to. That is the whole reason it is worth stating in
            // the rule rather than leaving to be noticed.
            if (def.Kind == DealerKind.Docks || def.DeliveryOnly) return true;

            return state.HasBeenOffered(MetKey(def)) ||
                   state.HasBeenOffered("plug:" + def.Id);
        }

        /// <summary>
        /// Offers the trade to somebody you have walked up to.
        ///
        /// This is how you buy from a dealer you arranged to meet, and for a while there was no
        /// way at all: the only route in was a wheel page reached from Re-up, and when Re-up
        /// went the page went with it and nothing replaced it. A man stood at his meet spot
        /// with a marker over his head and no way to do business.
        ///
        /// It opens the SAME screen the delivery uses, which is the point -- one dealer should
        /// not quote two sets of prices depending on whether he drove or you walked.
        /// </summary>
        public void UpdatePrompt()
        {
            HandOverTheNumber();

            // His people shift about while he is out.
            if (_liveDef != null && _liveDef.HasSpot)
            {
                _stoop.Wander(new Vector3(_liveDef.SpotX, _liveDef.SpotY, _liveDef.SpotZ),
                              _liveDef.SpotHeading);
            }

            _stoop.PickItBackUp();

            var def = InReach;
            if (def == null || Talk == null || Talk.IsOpen || TalkBuilder == null) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to talk to " + def.Name + ".");

            if (!WantsToTalk()) return;

            // THIS IS WHERE A CONTACT COMES FROM. He is a man on a corner until you have
            // stood in front of him; after that he is a number.
            //
            // Marked here rather than in Main's builder lambda, because this is the one place
            // a conversation with a dealer can begin and a rule about meeting people should
            // not depend on a caller remembering to say so.
            if (State != null && !HaveMet(def, State))
            {
                State.MarkOffered(MetKey(def));
                State.Touch();

                // The greeting picks this up and puts it down again.
                def.JustMet = true;

                // And the number follows him out of the conversation.
                _owesNumber = def;

                Log.Info("Met " + def.Id + " in person; he is a contact now.");
            }

            var root = TalkBuilder(def);
            if (root == null) return;

            Talk.Speaker = _livePed;
            Talk.Title = def.Name;
            Talk.Open(root, this);
        }

        /// <summary>
        /// The talk button, read the same way Lamar's corner reads it.
        ///
        /// Several controls and two raw keys, because the one the prompt names is not always
        /// the one that arrives -- and edge-detected, so holding it down opens the screen once
        /// rather than reopening it every frame you stand there.
        /// </summary>
        private bool WantsToTalk()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                // Unreadable control is simply not pressed.
            }

            var pressed = down && !_talkHeld;
            _talkHeld = down;
            return pressed;
        }

        private bool _talkHeld;

        /// <summary>Plays the dealer's greeting once per approach, as a subtitle.</summary>
        public void GreetIfNeeded()
        {
            if (_liveDef == null || _greeted) return;
            if (InReach == null) return;

            _greeted = true;
            Bark(HelloLines);
        }

        /// <summary>
        /// One ambient line out of him, over whatever he was already saying.
        ///
        /// This is all he does now. GENERIC_* is not a voice so much as the sound of a man
        /// making one, but it is the sound of THIS man rather than a line of text at the bottom
        /// of the screen, and it is enough to say that somebody spoke.
        /// </summary>
        private void Bark(string[] lines)
        {
            if (_livePed == null || !_livePed.Exists() || !_livePed.IsAlive) return;
            if (lines == null || lines.Length == 0) return;

            try
            {
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, _livePed.Handle);
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, _livePed.Handle,
                              lines[_rng.Next(lines.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        private static readonly string[] HelloLines = { "GENERIC_HOWS_IT_GOING", "GENERIC_HI" };
        private static readonly string[] AgreeLines = { "GENERIC_YES", "GENERIC_THANKS" };
        private static readonly string[] NoLines = { "GENERIC_NO", "GENERIC_CURSE_MED" };
        private static readonly string[] ByeLines = { "GENERIC_BYE", "GENERIC_THANKS" };

        /// <summary>What he says as you walk off. Called from the wheel's Leave wedge.</summary>
        public void SayBye()
        {
            Bark(ByeLines);
        }

        public void RestoreWorld()
        {
            ClearMeetBlip();
            Despawn();

            // AND THE PERMANENT MARKS. They are the one thing here that is not attached to a
            // ped or a run, so nothing else was ever going to take them off -- a reload would
            // have left a green blip on every corner he knows and then drawn a second set on
            // top of them.
            foreach (var kv in _marks)
            {
                try { if (kv.Value != null && kv.Value.Exists()) kv.Value.Delete(); }
                catch { /* teardown */ }
            }

            _marks.Clear();
        }
    }
}
