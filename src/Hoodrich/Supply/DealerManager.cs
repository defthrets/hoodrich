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
                def.SourceReply = node["sourceReply"].AsString(def.SourceReply);
                def.SourceTooSoon = node["sourceTooSoon"].AsString(def.SourceTooSoon);
                def.Farewell = node["farewell"].AsString(def.Farewell);

                var kind = node["kind"].AsString(def.Kind.ToString());
                try { def.Kind = (DealerKind)Enum.Parse(typeof(DealerKind), kind, true); }
                catch { Log.Warn("Unknown dealer kind '" + kind + "' on " + id + "."); }

                ReplaceList(def.Models, node["models"]);
                ReplaceList(def.Drugs, node["drugs"]);
                ReplaceList(def.Rides, node["rides"]);
                def.RidePaint = node["ridePaint"].AsInt(def.RidePaint);
                def.OpeningText = node["openingText"].AsString(def.OpeningText);
                def.Portrait = node["portrait"].AsString(FaceFor(def.Id));
                def.TextCalled = node["textCalled"].AsString(def.TextCalled);
                def.TextLeaving = node["textLeaving"].AsString(def.TextLeaving);
                def.TextOutside = node["textOutside"].AsString(def.TextOutside);
                def.LotValue = node["lotValue"].AsFloat(def.LotValue);
                def.LotStep = node["lotStep"].AsFloat(def.LotStep);
                def.PriceFloor = (int)node["priceFloor"].AsFloat(def.PriceFloor);
                def.PriceStep = Math.Max(1, (int)node["priceStep"].AsFloat(def.PriceStep));
                def.Drunk = node["drunk"].AsBool(def.Drunk);
                ReplaceList(def.Zones, node["zones"]);

                def.Purity = Math.Max(Economy.Stash.MinPurity,
                                      Math.Min(Economy.Stash.MaxPurity,
                                               node["purity"].AsFloat(def.Purity)));

                if (isNew) _defs.Add(def);
            }
        }

        private static void ReplaceList(List<string> target, Json node)
        {
            if (node.Kind != JsonKind.Array) return;
            target.Clear();
            foreach (var s in node.AsStringList()) target.Add(s);
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
                case "stretch_run": return "CHAR_MP_GERALD";
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

        public string RefusalReason(DealerDef def, PlayerState state, Affiliation crew)
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
            // delivery while you were still driving his quarter kilo across the city, and it
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
            if (def.Kind == DealerKind.Docks && AtHome != null && !AtHome())
            {
                return "Call him from the house";
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
                if (RefusalReason(def, state, crew) != null) continue;

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

            var zone = turf.ZoneCode;
            var wanted = DealerForZone(zone, crew, state);

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
                if (TryPitch(zone, player.Position, out var spot)) SpawnAt(wanted, spot, zone, player, false);
                return;
            }

            if (player.Position.DistanceTo(_livePed.Position) > DespawnRange) Despawn();
        }

        private void SpawnAt(DealerDef def, Vector3 spot, string zone, Ped player, bool route)
        {
            var model = ResolveModel(def);
            if (model == null) return;

            try
            {
                var heading = (float)(_rng.NextDouble() * 360.0);
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

        private void CreateBlip(DealerDef def, bool route)
        {
            try
            {
                _liveBlip = _livePed.AddBlip();
                if (_liveBlip == null || !_liveBlip.Exists()) return;

                _liveBlip.Sprite = BlipSprite.Friend;
                _liveBlip.Color = def.Kind == DealerKind.Docks ? BlipColor.Blue : BlipColor.Green;
                _liveBlip.Name = def.Name;

                // A posted dealer is a local landmark; someone you called out gets a route.
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
            var def = InReach;
            if (def == null || Talk == null || Talk.IsOpen || TalkBuilder == null) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to talk to " + def.Name + ".");

            if (!WantsToTalk()) return;

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
        }
    }
}
