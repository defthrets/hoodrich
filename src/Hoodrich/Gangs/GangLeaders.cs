using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.State;
using Hoodrich.Supply;
using Hoodrich.Territory;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>The man you have to find before a crew will have anything to do with you.</summary>
    internal sealed class LeaderDef
    {
        public string GangId = "";
        public string Name = "";

        /// <summary>Zone he holds court in. Drives both his map marker and where he stands.</summary>
        public string HomeZone = "";

        public readonly List<string> Models = new List<string>();

        /// <summary>
        /// His map mark, or 0 for the ordinary gang-leader one.
        ///
        /// In the data rather than in the code because it is a judgement about a person, and
        /// the person is in the data. Editable in leaders.json.
        /// </summary>
        public int Sprite;

        /// <summary>
        /// Exactly where he stands, from leaders.json. Zero means fall back to the pavement
        /// nearest the zone centre, which is how a leader ends up in the middle of the road.
        /// Ground height is probed at runtime so there is no Z to author wrongly.
        /// </summary>
        public float SpotX;
        public float SpotY;

        /// <summary>
        /// Exact height, when the spot is not on the ground.
        ///
        /// Probing for ground height finds the ground -- which is a floor below a man standing
        /// on a raised walkway, and puts him in the alley underneath. Zero means probe.
        /// </summary>
        public float SpotZ;

        public float Heading;

        /// <summary>
        /// Hours he is not on the corner at all, wrapping midnight. Equal values mean always.
        ///
        /// Nobody stands on the same corner twenty-four hours a day, and a man who is reliably
        /// absent before dawn is a man with a life rather than a vending machine.
        /// </summary>
        public int AwayFromHour;
        public int AwayToHour;

        /// <summary>True when the clock says he has gone home.</summary>
        public bool IsAwayAt(int hour)
        {
            if (AwayFromHour == AwayToHour) return false;

            return AwayFromHour < AwayToHour
                ? hour >= AwayFromHour && hour < AwayToHour
                : hour >= AwayFromHour || hour < AwayToHour;
        }

        /// <summary>Said when you walk up unaffiliated.</summary>
        public string Greeting = "";

        /// <summary>Said when he takes you on.</summary>
        public string Accept = "";

        /// <summary>Said when you have not earned it yet.</summary>
        public string Refuse = "";

        /// <summary>Said when you already run with him.</summary>
        public string Already = "";
    }

    /// <summary>
    /// Gang leaders: where they are, and how you get in with them.
    ///
    /// Every crew has one, permanently marked on the map so joining is something you go and
    /// DO rather than a wedge you pick. He is also the crew's first dealer -- taking you on
    /// comes with a bag fronted to you, because a crew does not hand a stranger cash, it hands
    /// him work.
    /// </summary>
    internal sealed class GangLeaders
    {
        /// <summary>
        /// Asked, per gang, whether that leader is off his corner right now.
        ///
        /// Set by Main and answered by whatever has him. Nothing here needs to know why.
        /// </summary>
        public Func<string, bool> StandDown;

        private const float SpawnRange = 120f;
        private const float DespawnRange = 200f;
        private const float TalkRange = 3.0f;
        private const int UpdateIntervalMs = 800;

        private readonly List<LeaderDef> _defs = new List<LeaderDef>();
        private readonly Dictionary<string, Blip> _blips = new Dictionary<string, Blip>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> _spots = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly ZoneMap _zones;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;

        private LeaderDef _liveDef;
        private Ped _livePed;
        private int _lastUpdate;

        public GangLeaders(Settings cfg, GangRegistry gangs, ZoneMap zones, Affiliation crew, PlayerState state)
        {
            _cfg = cfg;
            _gangs = gangs;
            _zones = zones;
            _crew = crew;
            _state = state;

            AddDefaults();
            ApplyPlacements();
        }

        /// <summary>
        /// Overlays leaders.json onto the built-in cast: where each man stands and which way he
        /// faces. The lines stay in code because they are written to fit the gang, but a spot is
        /// a coordinate somebody will want to move without rebuilding the mod.
        /// </summary>
        private void ApplyPlacements()
        {
            var doc = JsonFile.Read(System.IO.Path.Combine(Paths.Data, "leaders.json"));
            if (doc == null)
            {
                Log.Warn("No leaders.json; leaders fall back to standing at their zone centre.");
                return;
            }

            var placed = 0;

            foreach (var node in doc["leaders"].Items)
            {
                var gangId = node["gang"].AsString("");
                if (string.IsNullOrEmpty(gangId)) continue;

                var def = Get(gangId);

                // An unknown gang id used to be skipped in silence, and that is how two
                // leaders came to exist entirely on paper.
                //
                // Chuy of the Aztecas and Mr Kim of the Kkangpae had names, coordinates,
                // headings, models and blip sprites in leaders.json and no entry in
                // AddDefaults, so every one of those fields was read and thrown away by this
                // "continue". They never spawned, never blipped and could never be spoken to,
                // and nothing anywhere said so -- the file looked complete and two ninths of
                // the cast did not exist. It only became obvious when leader markers turned
                // into something the player goes hunting for.
                //
                // DealerManager has always let dealers.json add a dealer it has never heard
                // of. This is the same behaviour, for the same reason: the data file is the
                // cast list, not a set of corrections to a cast list held somewhere else.
                if (def == null)
                {
                    def = new LeaderDef { GangId = gangId };
                    _defs.Add(def);
                    Log.Info("leaders.json introduced a leader the code did not have: " + gangId + ".");
                }

                var name = node["name"].AsString("");
                if (!string.IsNullOrEmpty(name)) def.Name = name;

                // Voice lines are data now. They were only ever in AddDefaults because that is
                // where they were first written, and a man's dialogue is the last thing that
                // should need a compiler to change.
                def.Greeting = node["greeting"].AsString(def.Greeting);
                def.Accept = node["accept"].AsString(def.Accept);
                def.Refuse = node["refuse"].AsString(def.Refuse);
                def.Already = node["already"].AsString(def.Already);

                var zone = node["zone"].AsString("");
                if (!string.IsNullOrEmpty(zone)) def.HomeZone = zone;

                def.SpotX = node["x"].AsFloat();
                def.SpotY = node["y"].AsFloat();
                def.SpotZ = node["z"].AsFloat();
                def.Heading = node["heading"].AsFloat();
                def.Sprite = node["sprite"].AsInt(def.Sprite);
                def.AwayFromHour = node["awayFromHour"].AsInt(0);
                def.AwayToHour = node["awayToHour"].AsInt(0);

                var models = node["models"].AsStringList();
                if (models != null && models.Count > 0)
                {
                    def.Models.Clear();
                    def.Models.AddRange(models);
                }

                placed++;
            }

            Log.Info("Leader placements loaded: " + placed + ".");

            // Said once, loudly, at load, rather than discovered by a player who drove across
            // the city to meet somebody who cannot speak.
            foreach (var def in _defs)
            {
                if (string.IsNullOrEmpty(def.Greeting))
                {
                    Log.Warn(def.Name + " (" + def.GangId + ") has NO greeting and cannot be " +
                             "talked to. Give him greeting/accept/refuse/already in leaders.json.");
                }
            }
        }

        public IReadOnlyList<LeaderDef> All => _defs;

        /// <summary>The leader the player is close enough to talk to, if any.</summary>
        public LeaderDef InReach
        {
            get
            {
                if (_liveDef == null || _livePed == null || !_livePed.Exists() || !_livePed.IsAlive) return null;

                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return null;

                return player.Position.DistanceTo(_livePed.Position) <= TalkRange ? _liveDef : null;
            }
        }

        public LeaderDef Get(string gangId)
        {
            return _defs.Find(d => string.Equals(d.GangId, gangId, StringComparison.OrdinalIgnoreCase));
        }

        public Vector3 SpotFor(LeaderDef def)
        {
            if (def == null) return Vector3.Zero;
            if (_spots.TryGetValue(def.GangId, out var v)) return v;

            Vector3 spot;

            if (Math.Abs(def.SpotX) > 0.01f || Math.Abs(def.SpotY) > 0.01f)
            {
                // An authored spot is a specific yard or wall, so it is used as given --
                // snapping it to the nearest pavement would put him back on the kerb.
                spot = new Vector3(def.SpotX, def.SpotY, def.SpotZ);

                // An authored height is taken as read. Only guess when there is nothing to use.
                if (Math.Abs(def.SpotZ) < 0.01f)
                {
                    try
                    {
                        if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, 1000f), out var groundZ,
                                                  GetGroundHeightMode.Normal))
                        {
                            spot.Z = groundZ;
                        }
                    }
                    catch
                    {
                        // Unstreamed. Spawning resolves the height again once the player is close.
                    }
                }
            }
            else
            {
                // No authored coordinate, so this man has no home -- and until now that meant
                // the nearest pavement to his zone centre, which is a NAVMESH query and comes
                // back differently depending on what was streamed when it was first asked. He
                // stood somewhere new every session and looked stable within one, because the
                // answer is cached.
                //
                // Deterministic now: the same coordinate for the same gang, forever. It is
                // still a guess and it is still worth replacing with a real spot, which is why
                // it says so out loud rather than quietly carrying on.
                spot = _zones.FixedCentre(def.HomeZone);

                Log.Warn(def.Name + " (" + def.GangId + ") has no spot in leaders.json and is " +
                         "standing at the middle of " + def.HomeZone + ". Give him a real one.");
            }

            _spots[def.GangId] = spot;
            return spot;
        }

        /// <summary>
        /// Ground height only resolves once the terrain around a spot is streamed in, which it
        /// is not when the blip is first placed from across the map. Re-probing just before he
        /// spawns is what stops him standing in mid-air or buried in the pavement.
        /// </summary>
        private Vector3 ResolveSpotNow(LeaderDef def)
        {
            var spot = SpotFor(def);
            if (spot == Vector3.Zero) return spot;

            // Probed even when a height was authored. Skipping it meant a spot read a metre
            // high stayed a metre high and the man dropped out of the air every time you saw
            // him -- and the guard against a probe finding a balcony is that it has to AGREE
            // with the authored height, not that it never runs.
            if (Math.Abs(def.SpotZ) > 0.01f)
            {
                try
                {
                    if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 1.5f),
                                              out var settled, GetGroundHeightMode.Normal) &&
                        settled > 0f && Math.Abs(settled - spot.Z) <= 3f)
                    {
                        spot.Z = settled;
                    }
                }
                catch
                {
                    // Keep the authored height.
                }

                return spot;
            }

            try
            {
                if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 20f), out var groundZ,
                                          GetGroundHeightMode.Normal) && groundZ > 0f)
                {
                    spot.Z = groundZ;
                    _spots[def.GangId] = spot;
                }
            }
            catch
            {
                // Keep whatever we had.
            }

            return spot;
        }

        /// <summary>
        /// He notices the moment his package is gone, rather than the next time you see him.
        ///
        /// Selling the last gram of somebody else's work happens on a corner with a customer
        /// in front of you and no menu open, so if he only reacted when you walked back up to
        /// him there would be no moment at all -- you would simply find a new dialogue option
        /// waiting whenever you next happened past. A text is how everybody else in this mod
        /// gets hold of you and it is how he should too.
        ///
        /// Latched, because "the package is gone" stays true until the ledger is cleared and
        /// this runs several times a second.
        /// </summary>
        private void HisWorkIsGone()
        {
            if (_state == null) return;
            if (!_state.FrontedWorkDone || _state.FrontDoneTexted) return;

            _state.FrontDoneTexted = true;
            _state.Touch();

            // Two different messages, because they are two different moments. The first
            // package is an audition; the second is him deciding you are worth introducing to
            // the people he buys from.
            var second = _state.FrontsDone >= 1;

            try
            {
                // Face left to the sender's name. Naming the picture here is exactly the habit
                // that put a silhouette on this message in the first place.
                Notify.Text(null, "Gerald", "Chamberlain Hills",
                            second
                                ? "aight thats all of it gone. come see me, i got somethin " +
                                  "else for you and it aint corner work"
                                : "heard you moved all that already. come see me and we'll " +
                                  "talk about you properly",
                            false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not text about the front: " + ex.Message);
            }

            Log.Info("Gerald's front is cleared (" + (second ? "second" : "first") + ").");
        }

        // ---- who they are ------------------------------------------------------

        private void AddDefaults()
        {
            // Gerald holds the corner and the supply. Stretch is four streets away selling
            // guns out of a courtyard, which is a different man doing a different job -- see
            // Armourer.
            //
            // The model list runs the two names the game might have him under and then the
            // Families heavies. He is a man on a Chamberlain corner either way, and a wrong
            // guess at a model costs a line in the log rather than an empty corner.
            // Gerald. Clipped and transactional, and the whole relationship runs through
            // Lamar having spoken for you -- which is the only reason he is talking to you at
            // all and the thing he measures you against.
            Add("families", "Gerald", "CHAMH",
                new[] { "ig_g", "csb_g", "g_m_y_famdnf_01", "a_m_m_soucent_01" },
                "You the one Lamar keep bringin' up. Aight. Talk.",
                "Far as anybody need to know, you run with us. That's it, that's the whole " +
                "ceremony. Now -- I don't wanna see you again til that bag's empty. Not " +
                "halfway. Empty.",
                "Lamar talk about you like you somebody. I ain't seen it yet. Go make him right.",
                "We already did this part. Go work.");

            // OG Reese. Asks rather than tells, because he is old enough to have watched
            // everybody out here come up and he places you by who your people are.
            Add("ballas", "OG Reese", "RANCHO",
                new[] { "g_m_y_ballaorig_01", "g_m_y_ballaeast_01", "a_m_m_soucent_02" },
                "Hold up. Who your people? 'Cause I know everybody out here, and I don't know you.",
                "Purple look alright on you. Don't get comfortable in it. Somebody gon' be " +
                "watchin' how you move for a while, and it ain't gon' be me -- you won't know " +
                "which one he is. That's the job.",
                "I been out here since before you could walk. You think you the first one come " +
                "up to me askin'? Go get a name.",
                "I know who you are. Go on.");

            // El Tio. Everything is a debt and everything is on his name. He never says work.
            Add("vagos", "El Tio", "EBURO",
                new[] { "g_m_y_mexgoon_02", "g_m_y_mexgoon_01", "a_m_y_mexthug_01" },
                "Ey. You looking for something, or you lost? Around here those are different " +
                "problems.",
                "Bueno. Understand me -- this is not a job. This is a debt. Everything in that " +
                "bag moves on my name, and my name is not free. You break it, I take it back " +
                "out of you.",
                "You want me to put my name on a stranger. No. Bring me somebody who will say " +
                "your name out loud, and then we can owe each other something.",
                "Ya. You're mine. Go.");

            // Chavo. The fewest words of anyone here, and nothing is ceremonial.
            Add("marabunta", "Chavo", "CYPRE",
                new[] { "g_m_y_salvagoon_01", "g_m_y_salvaboss_01", "a_m_y_mexthug_01" },
                "You standing in the wrong place to be asking questions.",
                "You're in. Nobody claps. You start where everybody starts, which is carrying " +
                "what you're given and not asking what's in it.",
                "No. Nobody has said your name to me.",
                "You're one of ours. What else.");

            // Bull. Club vocabulary and nothing else -- hangaround, prospect, church, patch.
            // He never once uses a word that would mean anything to somebody outside it.
            Add("lost", "Bull", "SLAB",
                new[] { "g_m_y_lost_02", "g_m_y_lost_01", "a_m_m_hillbilly_01" },
                "You ain't wearing a patch, so make it quick.",
                "You're a hangaround. Not a prospect, not a member -- a hangaround. Hangarounds " +
                "fetch. Fetch long enough and somebody might put your name up at church. Might.",
                "Hangarounds earn it. You ain't hung around. There's a difference and you'll " +
                "learn it standing over there.",
                "You ride with us already. So go ride.");

            // Uncle Wei. Formal, no contractions anywhere, and he measures in decades.
            Add("triads", "Uncle Wei", "KOREAT",
                new[] { "g_m_m_chiboss_01", "g_m_m_chigoon_01", "a_m_y_ktown_01" },
                "You are not expected. Speak.",
                "You may carry for us. Understand what that is, and what it is not. There are " +
                "men who have worked thirty years and are still outside this family. You are " +
                "not inside it. You are useful. That is a different word.",
                "You have done nothing, and I have no interest in what you intend to do. Go.",
                "You work for us already. Do so.");

            // Sarkis. Money, and specifically the idea that the first number you show him is
            // the number you are stuck with.
            Add("armenians", "Sarkis", "ALTA",
                new[] { "g_m_m_armboss_01", "g_m_m_armgoon_01", "a_m_m_eastsa_02" },
                "You want something. Everybody wants something.",
                "Fine. You carry, I pay, and we find out what you are worth. Be careful what " +
                "you show me first -- whatever I decide you are worth today is what you will " +
                "be worth to me for as long as I know you.",
                "You are nobody, and nobody is worth nothing. Change that and we will talk " +
                "about numbers.",
                "You are already mine. What do you want?");

            // Chuy. Banning, and everything is about who is from here and who is not. He is
            // the only one of the nine whose rules are about SECRECY rather than product.
            Add("aztecas", "Chuy", "BANNING",
                new[] { "g_m_y_azteca_01", "g_m_y_mexgoon_03", "a_m_y_mexthug_01" },
                "This is Banning. Nobody comes to Banning by accident. So which is it?",
                "Alright. You're not from here, so listen -- you don't say our name anywhere " +
                "off this block, you don't bring nobody back here with you, and you never come " +
                "looking for me at my house. Do that and there's always something to carry.",
                "Your face isn't from here. Neither is your family. Round here that's the same " +
                "as not existing. Go and exist somewhere else first.",
                "You're with us. So stop standing out in the open like a tourist.");

            // Mr Kim. Polite, precise, and menacing entirely by implication. What he sells is
            // your business; what people REMEMBER about it is his.
            Add("koreans", "Mr Kim", "KOREAT",
                new[] { "g_m_m_korboss_01", "g_m_y_korlieut_01", "g_m_y_korean_01" },
                "Yes? Be quick. I am polite, not patient.",
                "Very well. Two rules, and they are the same rule twice. Nothing loud. Nothing " +
                "that ends up on somebody's phone. What you sell is your business. What people " +
                "remember about it is mine.",
                "No. There is nothing written next to your name yet, and I do not do business " +
                "with a blank page.",
                "We have an arrangement. Do not spend it standing here.");
        }

        private void Add(string gangId, string name, string zone, string[] models,
                         string greeting, string accept, string refuse, string already)
        {
            var def = new LeaderDef
            {
                GangId = gangId,
                Name = name,
                HomeZone = zone,
                Greeting = greeting,
                Accept = accept,
                Refuse = refuse,
                Already = already
            };
            def.Models.AddRange(models);
            _defs.Add(def);
        }

        // ---- map markers -------------------------------------------------------

        /// <summary>
        /// radar_ped_gang_leader -- the sprite the game keeps for exactly this.
        ///
        /// It was the weed leaf, on the reasoning that what a leader is there for is product
        /// rather than violence. That is true of what you DO with them and wrong about who they
        /// are, and it put the same marker on Stretch as on a grow. This one says gang leader,
        /// which is the whole of it, and the gang's own colour underneath says which gang.
        /// </summary>
        private const int GangIconSprite = 855;

        /// <summary>
        /// radar_dead -- the skull, for a leader who has earned it.
        ///
        /// Per leader rather than for all of them, because it is a thing about the MAN. Stretch
        /// carries it: everything he has you do ends with somebody down, and the mark should say
        /// so before you walk over. Anybody without an entry keeps the leader icon.
        /// </summary>
        private const int SkullSprite = 274;

        /// <summary>
        /// Marks the one leader worth finding, attached to the MAN rather than to a coordinate
        /// so the marker walks with him.
        ///
        /// Only the gang you can actually join is marked. The others are still out there and
        /// still sell to you, but a map peppered with skulls you have no business visiting is
        /// noise, not navigation.
        /// </summary>
        private void SyncBlips()
        {
            foreach (var def in _defs)
            {
                var gang = _gangs.Get(def.GangId);

                // A marker on an empty corner is worse than no marker: you drive across town
                // and find nobody, with nothing telling you why.
                //
                // Everybody's boss is marked now, not only the one who might take you on.
                // Blipping joinable gangs alone meant eight of the nine leaders existed and
                // were invisible -- you could walk past OG Reese on his own block and never
                // know he was a person. A rival boss standing somewhere fixed is a landmark:
                // it says whose side of the street this is, and it gives you a door to knock
                // on when you want to start something.
                //
                // Rivals used to be gated on having joined a set, for the same reason: at the
                // very start Gerald is deliberately the only mark on the map, and nine gang
                // bosses before you have moved a gram undoes the opening.
                //
                // That gate is gone because the one below does the job better. You now cannot
                // see a rival until you have stood in front of him, which means a fresh save
                // still opens with exactly one icon -- but a player who goes looking is
                // rewarded for it instead of being told to come back later.
                var mine = gang != null && gang.Joinable;

                // A boss is not on your map until you have stood in front of him.
                //
                // Everything above is still true -- a rival boss standing somewhere fixed is a
                // landmark, and the map should say whose side of the street this is. What it
                // should NOT do is say it before you have been down that street. Nine markers
                // handed to a player who has never left Strawberry turns the whole city into a
                // list of errands; nine markers that appear one at a time as you find the men
                // is a map you drew yourself.
                //
                // The one exception is your own boss, who is never hidden.
                //
                // Before you join he is the opening -- Gerald texts, you go and see him, you
                // move his package, you get asked in -- and hiding the only icon on a fresh
                // save leaves a new player with a text message and no idea where to go. After
                // you join he is your boss, and a man you take orders from is not somebody you
                // have to keep rediscovering.
                //
                // Stating it as "mine" rather than as "mine and not yet joined" also means a
                // save made before any of this existed keeps its Families marker instead of
                // losing it to an empty list.
                var known = mine || (_state != null && _state.HasMet(def.GangId));

                var worthMarking = gang != null && known && !def.IsAwayAt(Pricing.ClockHour)
                                   && (StandDown == null || !StandDown(def.GangId));

                _blips.TryGetValue(def.GangId, out var existing);

                // The blip is on the ped, so it only exists while he does.
                var live = _liveDef != null &&
                           string.Equals(_liveDef.GangId, def.GangId, StringComparison.OrdinalIgnoreCase) &&
                           _livePed != null && _livePed.Exists();

                if (!worthMarking)
                {
                    if (existing != null)
                    {
                        try { if (existing.Exists()) existing.Delete(); } catch { }
                        _blips.Remove(def.GangId);
                    }
                    continue;
                }

                if (existing != null && existing.Exists())
                {
                    // Swap a coordinate marker for one on the man as soon as he is around.
                    if (!live || existing.Handle == _pedBlipHandle) continue;

                    try { existing.Delete(); } catch { }
                    _blips.Remove(def.GangId);
                }

                try
                {
                    Blip blip;

                    if (live)
                    {
                        var handle = Function.Call<int>(Hash.ADD_BLIP_FOR_ENTITY, _livePed.Handle);
                        if (handle == 0) continue;

                        blip = new Blip(handle);
                        _pedBlipHandle = handle;
                    }
                    else
                    {
                        var spot = SpotFor(def);
                        if (spot == Vector3.Zero) continue;

                        blip = World.CreateBlip(spot);
                        if (blip == null || !blip.Exists()) continue;
                    }

                    Function.Call(Hash.SET_BLIP_SPRITE, blip.Handle,
                                  def.Sprite > 0 ? def.Sprite : GangIconSprite);
                    Function.Call(Hash.SET_BLIP_COLOUR, blip.Handle, gang.BlipColour);

                    blip.Name = def.Name + " -- " + gang.Name;
                    blip.Scale = 0.85f;

                    // Short range for everybody who is not yours.
                    //
                    // Short range does NOT mean the mark disappears -- it stays on the pause
                    // map exactly as before, so you can still find any of them from the
                    // planning screen. What it stops is eight rival bosses sitting on the
                    // minimap at all times from across the city, which turns the corner of the
                    // screen into a list. Near their block, they come back.
                    //
                    // Your own leader stays long range. He is the one you are sent to.
                    blip.IsShortRange = !mine;

                    _blips[def.GangId] = blip;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not blip leader " + def.GangId + ": " + ex.Message);
                }
            }
        }

        /// <summary>Handle of the blip currently attached to a ped, so it is not rebuilt.</summary>
        private int _pedBlipHandle;

        /// <summary>
        /// Writes down that you have met whoever you are stood in front of.
        ///
        /// Said out loud the first time, because a marker quietly appearing behind you on a map
        /// you are not looking at is a thing the player never learns is happening -- and the
        /// whole point of hiding them is that finding one should feel like progress.
        /// </summary>
        private void MarkFound()
        {
            if (_state == null || _liveDef == null) return;
            if (!_state.MarkMet(_liveDef.GangId)) return;

            var gang = _gangs.Get(_liveDef.GangId);

            try
            {
                Notify.Ticker("~y~" + _liveDef.Name + "~s~ is on your map now.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not say who was found: " + ex.Message);
            }

            Log.Info("Met " + _liveDef.Name + " of " +
                     (gang != null ? gang.Name : _liveDef.GangId) + "; his blip is on.");

            // Straight away rather than on the next sweep, so it is on the map by the time the
            // dialogue closes and you look.
            SyncBlips();
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            SyncBlips();

            HisWorkIsGone();

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            // Whichever leader you are closest to, if you are near enough to matter.
            LeaderDef nearest = null;
            var nearestDistance = float.MaxValue;

            foreach (var def in _defs)
            {
                var spot = SpotFor(def);
                if (spot == Vector3.Zero) continue;

                var d = player.Position.DistanceTo(spot);
                if (d >= nearestDistance) continue;

                nearestDistance = d;
                nearest = def;
            }

            if (nearest == null || nearestDistance > DespawnRange)
            {
                Despawn();
                return;
            }

            if (_liveDef != null && _liveDef.GangId != nearest.GangId) Despawn();

            // He keeps hours. Off the corner in the small hours, and if the clock rolls round
            // while you are stood there, he goes.
            if (nearest.IsAwayAt(Pricing.ClockHour))
            {
                if (_liveDef != null && _liveDef.GangId == nearest.GangId) Despawn();
                return;
            }

            // And he is not on his corner while he is stood somewhere else waiting for you.
            //
            // Exactly one leader is ever alive in the world, so a mission that needs one of
            // them somewhere cannot simply spawn its own -- there would be two of the same man
            // two hundred metres apart. This is how the mission says he has gone out: it is
            // not a workaround, it is the truth about where he is.
            if (StandDown != null && StandDown(nearest.GangId))
            {
                if (_liveDef != null && _liveDef.GangId == nearest.GangId) Despawn();
                return;
            }

            if (_livePed == null && nearestDistance <= SpawnRange)
            {
                Spawn(nearest);
                return;
            }

            ReturnIfStrayed();
            SettleIfHome();
        }

        private void Spawn(LeaderDef def)
        {
            var spot = ResolveSpotNow(def);
            if (spot == Vector3.Zero) return;

            var model = ResolveModel(def);
            if (model == null) return;

            try
            {
                _livePed = World.CreatePed(model.Value, spot);
                if (_livePed == null || !_livePed.Exists()) return;

                var h = _livePed.Handle;

                if (Math.Abs(def.Heading) > 0.01f) _livePed.Heading = def.Heading;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);

                // And he cannot die. A leader is a shop, a conversation and a chain of jobs;
                // losing him to a stray round from a fight two streets away takes all three off
                // the map with nothing to say why. Not-targetable already stopped anybody aiming
                // AT him -- this is the rest of it: the stray, the car, the fire.
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, h, true);
                Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, h, false);
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, h, false);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, h, false);

                StandAtSpot();

                var gang = _gangs.Get(def.GangId);
                if (gang != null && gang.GroupHash != 0)
                {
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h, gang.GroupHash);
                }

                _livePed.IsPersistent = true;
                _livePed.BlockPermanentEvents = true;

                _liveDef = def;
                Log.Info("Leader " + def.Name + " (" + def.GangId + ") spawned in " + def.HomeZone + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Could not spawn leader " + def.GangId, ex);
            }
            finally
            {
                try { model.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        /// <summary>How far he can end up from his spot before he walks back to it.</summary>
        private const float DriftAllowance = 6f;

        /// <summary>Idles tried in order, so he is doing something rather than staring ahead.</summary>
        private static readonly string[] StandScenarios =
        {
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_STAND_MOBILE"
        };

        /// <summary>True while he has been stopped to talk.</summary>
        private bool _held;

        /// <summary>
        /// Puts him back on his corner, doing nothing in particular.
        ///
        /// He is a fixture: the corner is where he is, and it is where you go to find him. He
        /// can still be frightened off it -- gunfire, a car through the fence -- because a man
        /// who stands still while being shot at is a prop. He walks back afterwards.
        /// </summary>
        private void StandAtSpot()
        {
            if (_livePed == null || !_livePed.Exists()) return;

            _held = false;

            try
            {
                _livePed.Task.ClearAll();

                foreach (var scenario in StandScenarios)
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _livePed.Handle, scenario, 0, true);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Leader could not be posted up: " + ex.Message);
            }
        }

        /// <summary>
        /// Walks him back to his corner once whatever spooked him is over.
        ///
        /// Nothing happens while he is fighting or fleeing -- being dragged back into gunfire by
        /// a script is worse than being off his mark for a minute.
        /// </summary>
        private void ReturnIfStrayed()
        {
            if (_held || _liveDef == null || _livePed == null || !_livePed.Exists() || !_livePed.IsAlive) return;

            var spot = SpotFor(_liveDef);
            if (spot == Vector3.Zero) return;

            var dx = _livePed.Position.X - spot.X;
            var dy = _livePed.Position.Y - spot.Y;
            if (dx * dx + dy * dy <= DriftAllowance * DriftAllowance) return;

            try
            {
                if (_livePed.IsInCombat || _livePed.IsRagdoll) return;
                if (Function.Call<bool>(Hash.IS_PED_FLEEING, _livePed.Handle)) return;

                // Already on his way back.
                if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, _livePed.Handle, 224)) return;

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, _livePed.Handle,
                              spot.X, spot.Y, spot.Z, 1.2f, 20000, 1.5f, false,
                              Math.Abs(_liveDef.Heading) > 0.01f ? _liveDef.Heading : 0f);
            }
            catch (Exception ex)
            {
                Log.Debug("Leader could not walk back: " + ex.Message);
            }
        }

        /// <summary>Re-posts him once he is home again, so he is idling rather than standing dumb.</summary>
        private void SettleIfHome()
        {
            if (_held || _liveDef == null || _livePed == null || !_livePed.Exists()) return;

            var spot = SpotFor(_liveDef);
            if (spot == Vector3.Zero) return;

            var dx = _livePed.Position.X - spot.X;
            var dy = _livePed.Position.Y - spot.Y;
            if (dx * dx + dy * dy > 2.5f * 2.5f) return;

            try
            {
                if (_livePed.IsInCombat) return;
                if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, _livePed.Handle, 118)) return;

                StandAtSpot();
                if (Math.Abs(_liveDef.Heading) > 0.01f) _livePed.Heading = _liveDef.Heading;
            }
            catch
            {
                // He will settle on the next pass.
            }
        }

        /// <summary>Ambient lines, so walking up and walking off are things you hear.</summary>
        private static readonly string[] HelloLines = { "GENERIC_HOWS_IT_GOING", "GENERIC_HI", "CHAT_STATE" };

        private static readonly string[] ByeLines = { "GENERIC_BYE", "GENERIC_THANKS" };

        /// <summary>
        /// One shared source of randomness.
        ///
        /// This used to be `new Random()` inside the call. Random is seeded from the clock, and
        /// two greetings a few milliseconds apart got the same seed and therefore the same line
        /// -- so a man with three hellos said one of them, and always the same one.
        /// </summary>
        private static readonly Random Rng = new Random();

        private static void Say(Ped ped, string[] lines)
        {
            if (ped == null || !ped.Exists() || lines.Length == 0) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle,
                              lines[Rng.Next(lines.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        /// <summary>
        /// Stops him and turns him to face you, for as long as the conversation lasts. Called
        /// when the dialogue opens; <see cref="StandAtSpot"/> puts him back on his mark after.
        /// </summary>
        public void HoldForTalk()
        {
            if (_livePed == null || !_livePed.Exists() || _held) return;

            _held = true;
            Say(_livePed, HelloLines);

            // Standing in front of him is what puts him on your map, and this is the one place
            // in the file that knows you are doing it. Not proximity -- walking past a man on
            // the far pavement is not meeting him, and a marker that appears because you drove
            // near it is a marker you still have not earned.
            MarkFound();

            try
            {
                var player = Game.Player.Character;

                _livePed.Task.ClearAll();
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _livePed.Handle, player.Handle, -1);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Leader could not be held: " + ex.Message);
            }
        }

        /// <summary>Puts him back on his corner once you have finished with him.</summary>
        public void ReleaseFromTalk()
        {
            if (!_held) return;

            Say(_livePed, ByeLines);
            StandAtSpot();
        }

        /// <summary>
        /// Flat distance to the live leader, or a large number when he is not around. Used to
        /// decide whether the player has walked out of a conversation.
        /// </summary>
        public float DistanceTo(LeaderDef def)
        {
            if (def == null || _liveDef == null || _livePed == null || !_livePed.Exists()) return 9999f;
            if (!string.Equals(def.GangId, _liveDef.GangId, StringComparison.OrdinalIgnoreCase)) return 9999f;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 9999f;

            var dx = player.Position.X - _livePed.Position.X;
            var dy = player.Position.Y - _livePed.Position.Y;

            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static Model? ResolveModel(LeaderDef def)
        {
            foreach (var name in def.Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(1500)) continue;
                    return model;
                }
                catch
                {
                    // Try the next one.
                }
            }
            return null;
        }

        private void Despawn()
        {
            if (_livePed != null && _livePed.Exists())
            {
                try
                {
                    _livePed.MarkAsNoLongerNeeded();
                    _livePed.Delete();
                }
                catch { }
            }

            _livePed = null;
            _liveDef = null;
        }

        /// <summary>
        /// Set by Main. Walking up no longer dumps a subtitle at you -- he waits, and you
        /// choose to start the conversation, which is what makes it a conversation.
        /// </summary>
        public Conversation Talk;

        /// <summary>Builds what he has to say. Set by Main alongside <see cref="Talk"/>.</summary>
        public Func<LeaderDef, DialogueNode> TalkBuilder;

        /// <summary>
        /// True the frame the player asks to talk.
        ///
        /// Read through several inputs rather than one. The cellphone directions are D-pad on a
        /// pad and the arrow keys on a keyboard, but they are also a control other scripts like
        /// to disable, and a prompt you cannot answer is worse than no prompt -- so the enabled
        /// and disabled paths are both checked, and E is accepted as well.
        /// </summary>
        private bool WantsToTalk()
        {
            // Level-read rather than JUST_PRESSED: the cellphone controls report their edge
            // inconsistently depending on whether the phone is considered active, which is why
            // the prompt appeared but the press did nothing. The edge is tracked here instead.
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 2, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                // A control that cannot be read is simply not pressed.
            }

            var pressed = down && !_talkHeld;
            _talkHeld = down;

            return pressed;
        }

        /// <summary>Edge state for <see cref="WantsToTalk"/>.</summary>
        private bool _talkHeld;

        /// <summary>
        /// Offers the conversation and starts it on D-pad right. Runs every frame rather than
        /// on the slow tick, because a prompt that appears 800ms after you arrive feels broken.
        /// </summary>
        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen) return;

            var def = InReach;
            if (def == null) return;

            // ~INPUT_PHONE_RIGHT~ is not a control the game knows, so it substituted nothing and
            // the prompt read "Press  to talk". The D-pad directions are the CELLPHONE inputs.
            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to talk to " + def.Name + ".");

            if (!WantsToTalk()) return;

            if (TalkBuilder == null)
            {
                Log.Warn("Nobody built " + def.Name + " a conversation; nothing to say.");
                return;
            }

            DialogueNode root;
            try
            {
                root = TalkBuilder(def);
            }
            catch (Exception ex)
            {
                Log.Error("Building " + def.Name + "'s conversation threw.", ex);
                return;
            }

            if (root == null)
            {
                Log.Warn(def.Name + " has no opening line; check his gang id in leaders.json.");
                return;
            }

            Log.Info("Talking to " + def.Name + ".");

            HoldForTalk();

            Talk.Speaker = _livePed;
            Talk.Open(root, def);
        }

        // ---- joining -----------------------------------------------------------

        /// <summary>
        /// Signing on. Returns a player-facing refusal, or null once you are in.
        ///
        /// Joining is not a handshake -- it is being handed work. The crew fronts you a bag on
        /// the spot, which is both the tutorial and the debt that starts the relationship.
        /// </summary>
        /// <summary>
        /// The first thing that happens: he hears there is somebody new and sends for them.
        ///
        /// Once ever, and saved, because "have you been told" is a fact about a conversation.
        /// Held back until the mod is actually running rather than fired on load -- a text
        /// arriving over the loading screen is a text nobody reads.
        ///
        /// It is also the only thing on the map at that point, which is deliberate: a first
        /// session that opens with nine icons and no idea which one matters is a mod nobody
        /// finishes the first evening of.
        /// </summary>
        public void SendForThem()
        {
            if (_state == null || _state.SentForYou) return;
            if (_crew != null && _crew.IsAffiliated) return;

            var def = Get("families");
            if (def == null) return;

            _state.SentForYou = true;
            _state.Touch();

            try
            {
                UI.Notify.Text(Portrait(def), def.Name, "Chamberlain Hills",
                               "heard theres somebody new on the block movin little bits. " +
                               "im in the flats on grove, round the back. come see me before " +
                               "you go makin that my problem");
            }
            catch (System.Exception ex)
            {
                Log.Debug("Could not send for the player: " + ex.Message);
            }

            Log.Info(def.Name + " has sent for the player.");
        }

        /// <summary>The contact picture for a leader, or the plain one.</summary>
        private static string Portrait(LeaderDef def)
        {
            var face = UI.Faces.For(def == null ? "" : def.Name);
            return string.IsNullOrEmpty(face) ? UI.Faces.Nobody : face;
        }

        /// <summary>
        /// The man himself, if the one being talked about is the one stood there.
        ///
        /// Only ever one leader is live at a time, so this is a comparison rather than a
        /// search -- and it returns null for a leader who is being quoted rather than spoken
        /// to, which is exactly when a voice should not come from anywhere in particular.
        /// </summary>
        private Ped Standing(LeaderDef def)
        {
            if (def == null || _liveDef == null || _livePed == null) return null;
            if (!_livePed.Exists() || !_livePed.IsAlive) return null;

            return string.Equals(_liveDef.GangId, def.GangId, StringComparison.OrdinalIgnoreCase)
                ? _livePed
                : null;
        }

        public string Join(LeaderDef def, Drugs catalogue)
        {
            if (def == null) return "Nobody here.";

            var gang = _gangs.Get(def.GangId);
            if (gang == null) return "Nobody here.";

            if (_crew.IsAffiliated && _crew.Current.Id == gang.Id)
            {
                Dialogue.Say(def.Name, def.Already, Standing(def));
                return null;
            }

            if (_state.Respect < gang.JoinRespect)
            {
                Dialogue.Say(def.Name, def.Refuse, Standing(def));
                return "Need " + gang.JoinRespect.ToString("F0") + " respect. You have " +
                       _state.Respect.ToString("F0") + ".";
            }

            var failure = _crew.Join(gang, _state.Respect);
            if (failure != null)
            {
                Dialogue.Say(def.Name, def.Refuse, Standing(def));
                return failure;
            }

            Dialogue.Say(def.Name, def.Accept, Standing(def));

            // Only if he has not already put you to work.
            //
            // The order changed: his package now comes BEFORE you are asked in rather than
            // with the handshake, so a leader who has already fronted you one and been squared
            // up with would otherwise hand over a second the moment you signed. The starter bag
            // still exists for any set that takes somebody on without that conversation.
            if (!_state.HasFrontedWork) FrontProduct(gang, catalogue);

            return null;
        }

        /// <summary>
        /// How pure a fronted bag is.
        ///
        /// Three quarters. Stash clamps to 0.2..1.0 so this is well inside, and PurityWord
        /// calls 0.75 "barely stepped on" -- which is what a bag off your own set should be.
        /// </summary>
        private const float FrontedPurity = 0.75f;

        /// <summary>Hands over a starter bag of whatever the crew moves.</summary>
        private void FrontProduct(GangDef gang, Drugs catalogue)
        {
            if (catalogue == null || gang.Drugs.Count == 0) return;

            // The set's STARTER, which is not the same as the first thing on its list.
            //
            // Families lead with weed because that is what the block is known for, and the man
            // who actually hands you your first bag deals pills. Taking Drugs[0] meant signing
            // on with Gerald and being handed marijuana by Gerald, who does not sell it.
            var product = catalogue.Get(gang.Starter.Length > 0 ? gang.Starter : gang.Drugs[0])
                          ?? catalogue.Get(gang.Drugs[0]);

            if (product == null) return;

            // Fronted at three quarters, not untouched.
            //
            // It used to arrive at 1.0, which is better than anything the player can produce
            // in his own kitchen -- so the bag a set hands a newcomer to get him started was
            // the best product in the game, and cutting your own was a step down. A front is
            // somebody else's work off somebody else's re-up, already stepped on once before
            // it got to you. Now it sells for what stepped-on weight sells for, and the
            // kitchen is worth walking into.
            var grams = Math.Max(1f, _cfg.LeaderFrontGrams);
            var given = _state.Stash.AddPackaged(product.Id, grams, FrontedPurity);
            if (given <= 0f)
            {
                Notify.Problem("you can't carry what he's trying to hand you.");
                return;
            }

            _state.Touch();
            // Amount(), not grams. Bars and pills are counted; the catalogue carries a
            // singular for exactly this and every other line in the mod uses it.
            Notify.Important("~g~" + product.Amount(given) +
                             " fronted to you.~s~ Post up and move it.");
            Log.Info("Fronted " + given.ToString("0.#") + "g " + product.Id + " on joining " + gang.Id + ".");
        }

        // There is deliberately no ground marker under a leader. He is a man stood on a corner
        // with a blip on the map -- a glowing ring round his feet is how you mark a pickup, and
        // he is not one. What used to be here was the loop that decided that: it worked out a
        // spot, a distance and a gang colour for all seven of them every frame, and drew
        // nothing with any of it.

        public void RestoreWorld()
        {
            Despawn();
            foreach (var kv in _blips)
            {
                try { if (kv.Value != null && kv.Value.Exists()) kv.Value.Delete(); } catch { }
            }
            _blips.Clear();
        }
    }
}
