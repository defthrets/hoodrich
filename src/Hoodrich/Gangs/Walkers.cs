using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Territory;

namespace Hoodrich.Gangs
{
    /// <summary>One of them, and what he is carrying.</summary>
    internal sealed class Walker
    {
        public Ped Man;
        public Prop Holding;
    }

    /// <summary>Three or four of them, going the same way.</summary>
    internal sealed class Crew
    {
        public readonly List<Walker> Men = new List<Walker>();

        public Ped Lead;

        /// <summary>Where they are headed, and when they were last looked at.</summary>
        public Vector3 Target;
        public Vector3 WasAt;
        public int LookedAt;

        public int Nudges;
        public int BornAt;

        /// <summary>When one of them next says something.</summary>
        public int NextWord;
    }

    /// <summary>
    /// Groups of them walking the back streets, and not going anywhere in particular.
    ///
    /// THE BLOCK HAD PEOPLE STANDING AND PEOPLE DRIVING AND NOBODY WALKING. BlockLife nails a
    /// man to a spot outside his own building, which is right for the yard and wrong for a
    /// neighbourhood -- a street where every pedestrian is stationary reads as a diorama. The
    /// rollers cover the roads. This is the gap between them: three or four of the set walking
    /// up and down together with a bottle and a smoke, because that is most of what actually
    /// happens on a residential street at night.
    ///
    /// THEY WALK AS A GROUP RATHER THAN AS FOUR MEN GOING THE SAME WAY. One leads and the rest
    /// are tasked to him with their own offsets, so they arrive together, wait together and set
    /// off together -- and when the lead stops to talk they are stood around him instead of
    /// filing past. Four independent pathfinds to one coordinate is four men who happen to be
    /// adjacent.
    ///
    /// BACK STREETS ON PURPOSE. The road network marks a node as GPS-allowed when it is a road
    /// the satnav would route you down, so the ones it will NOT route down are the service
    /// roads, the yards and the cut-throughs behind the buildings -- which is where people
    /// stand about, and not where the traffic is.
    ///
    /// Only on our own turf, and only while you are on it. There is no world simulation behind
    /// this and there should not be.
    /// </summary>
    internal sealed class Walkers
    {
        private const int TickMs = 1100;

        /// <summary>Far enough out to be found rather than to appear.</summary>
        private const float SpawnNear = 70f;
        private const float SpawnFar = 130f;

        /// <summary>Past this they are handed back to the game.</summary>
        private const float LetGoRange = 220f;

        private const int GapMinMs = 20000;
        private const int GapMaxMs = 55000;

        /// <summary>How many are in one, and how many crews are out at once.</summary>
        private const int CrewMin = 3;
        private const int CrewMax = 4;

        /// <summary>Close enough to where they were headed to pick somewhere else.</summary>
        private const float ArrivedRange = 7f;

        /// <summary>Nothing lasts forever, so the same four are not pacing all night.</summary>
        private const int LifetimeMs = 420000;

        /// <summary>Stuck: this little movement between two looks, that many times over.</summary>
        private const float StuckMoved = 1.6f;
        private const int StuckLookMs = 11000;
        private const int MaxNudges = 2;

        /// <summary>Walking pace. Not a stroll and definitely not a jog.</summary>
        private const float Pace = 1.0f;

        /// <summary>
        /// How often one of them says something, and how loud.
        ///
        /// FORCE_SHOUTED because the whole point is that you hear them before you see them.
        /// The lines are the game's own ambient speech, so they come out in the voice the model
        /// already has -- a line a given voice has not got simply does not play, which is the
        /// right failure and needs no list of who can say what.
        /// </summary>
        private const int WordMinMs = 7000;
        private const int WordMaxMs = 22000;

        private static readonly string[] Words =
        {
            "GENERIC_HI", "GENERIC_HOWS_IT_GOING", "CHAT_STATE", "GENERIC_INSULT_HIGH",
            "GENERIC_CURSE_MED", "LAUGH", "GENERIC_WHATEVER", "SHOUT"
        };

        /// <summary>What one of them might be holding. Most of them hold nothing.</summary>
        private static readonly string[] Bottles =
            { "prop_beer_bottle", "prop_beer_am", "prop_cs_beer_bot_40oz" };

        /// <summary>
        /// Standing about, once they have stopped somewhere -- for the ones with empty hands.
        ///
        /// WORLD_HUMAN_DRINKING IS NOT IN HERE ANY MORE. A scenario brings its own prop, so a
        /// man already holding one of our bottles who was handed the drinking scenario ended up
        /// with two bottles: ours welded to his hand and the game's in the other. The men with
        /// a drink get an animation instead -- see Drinks below -- which keeps the bottle they
        /// already have.
        /// </summary>
        private static readonly string[] Standing =
        {
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING_POT",
            "WORLD_HUMAN_AA_SMOKE", "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_STAND_IMPATIENT"
        };

        /// <summary>
        /// And the ones with a bottle actually drink out of it.
        ///
        /// The game's own beer idles, played as animations rather than as a scenario so the
        /// bottle in his hand stays the bottle in his hand. Tried in order, and if none of them
        /// load he falls back to standing about like everybody else -- a man holding a beer and
        /// not drinking it is only slightly wrong, and it is what was happening before.
        /// </summary>
        private static readonly string[][] Drinks =
        {
            new[] { "amb@world_human_drinking@beer@male@idle_a", "idle_a" },
            new[] { "amb@world_human_drinking@beer@male@idle_a", "idle_b" },
            new[] { "amb@world_human_drinking@beer@male@idle_a", "idle_c" },
            new[] { "amb@world_human_drinking@beer@male@base", "base" }
        };

        /// <summary>The walk of a man four beers in.</summary>
        private const string DrunkWalk = "move_m@drunk@moderatedrunk";

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly TurfWatch _turf;
        private readonly Random _rng = new Random();

        private readonly List<Crew> _out = new List<Crew>();

        private int _lastTick;
        private int _nextSpawn;

        /// <summary>Off while something louder is happening. Wired by the house script.</summary>
        public Func<bool> Busy;

        /// <summary>How close you have to be for one of them to notice, and how often.</summary>
        private const float GreetRange = 18f;
        private const int GreetGapMs = 22000;

        private bool Enabled => _cfg == null || _cfg.WalkersEnabled;

        private int MaxCrews => _cfg == null ? 2 : _cfg.WalkerCrews;

        public Walkers(Settings cfg, GangRegistry gangs, string gangId, TurfWatch turf)
        {
            _cfg = cfg;
            _gangs = gangs;
            _gangId = gangId;
            _turf = turf;
        }

        // ---- per-tick -----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            try
            {
                Prune(now);
                Greet(now);
                Scrap(now);

                if (!Enabled)
                {
                    for (var i = _out.Count - 1; i >= 0; i--) Release(_out[i]);
                    _out.Clear();
                    return;
                }

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                for (var i = _out.Count - 1; i >= 0; i--)
                {
                    if (!Steer(_out[i], now)) continue;

                    Release(_out[i]);
                    _out.RemoveAt(i);
                }

                if (!OnOurTurf()) return;
                if (Busy != null && Busy()) return;

                if (_nextSpawn == 0) _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);
                if (now < _nextSpawn) return;

                _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);

                if (_out.Count < MaxCrews) Send(player);
            }
            catch (Exception ex)
            {
                Log.Debug("Walkers tripped: " + ex.Message);
            }
        }

        // ---- keeping them moving ------------------------------------------------

        /// <returns>True when this crew has been given up on.</returns>
        private bool Steer(Crew crew, int now)
        {
            if (crew.Lead == null || !crew.Lead.Exists() || !crew.Lead.IsAlive) return true;

            Talk(crew, now);

            var here = crew.Lead.Position;

            if (crew.Target != Vector3.Zero && here.DistanceTo(crew.Target) < ArrivedRange)
            {
                crew.Nudges = 0;
                Aim(crew, now);
                return false;
            }

            if (now - crew.LookedAt < StuckLookMs) return false;

            var moved = crew.LookedAt == 0 ? float.MaxValue : here.DistanceTo(crew.WasAt);

            crew.WasAt = here;
            crew.LookedAt = now;

            if (moved > StuckMoved) return false;

            crew.Nudges++;

            if (crew.Nudges > MaxNudges) return true;

            Aim(crew, now);
            return false;
        }

        /// <summary>
        /// Put a beer idle on him, keeping the bottle he is already holding.
        /// </summary>
        /// <returns>False if none of the clips would load, so the caller can fall back.</returns>
        private bool Sip(Ped man)
        {
            var pick = _rng.Next(Drinks.Length);

            for (var i = 0; i < Drinks.Length; i++)
            {
                var pair = Drinks[(pick + i) % Drinks.Length];

                try
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, pair[0]);

                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pair[0])) continue;

                    // Looped and open-ended: the stop lasts until the crew is aimed somewhere
                    // else, and a drink that runs out after four seconds leaves him stood
                    // holding a bottle doing nothing, which is where this started.
                    Function.Call(Hash.TASK_PLAY_ANIM, man.Handle, pair[0], pair[1],
                                  2f, -2f, -1, 1, 0f, false, false, false);

                    return true;
                }
                catch
                {
                    // Next clip.
                }
            }

            return false;
        }

        /// <summary>
        /// Somebody who is not from round here walks past, and the crew goes for him.
        ///
        /// WHY THIS IS NOT JUST RELATIONSHIP GROUPS. The mod only makes two sets hate each
        /// other while a raid is actually running -- outside one they are neutral, which is
        /// right, because a city where every gang shoots every other gang on sight the whole
        /// time is a city on fire by Tuesday afternoon. So a rival walking down our alley is,
        /// as far as the game is concerned, a stranger, and nothing happens.
        ///
        /// This is the exception, and it is deliberately a small one: OUR crews, on OUR block,
        /// noticing somebody from a set we have a problem with. Rivals come out of gangs.json,
        /// so who counts is data rather than a list in here.
        ///
        /// The whole crew goes, not one man. A greeting is one bloke looking up; this is four
        /// people deciding at once, which is what a group of them does and the reason they walk
        /// around in one.
        /// </summary>
        private void Scrap(int now)
        {
            if (now < _nextScrap) return;

            var mine = _gangs == null ? null : _gangs.Get(_gangId);
            if (mine == null || mine.Rivals.Count == 0) return;

            // The groups we have a problem with, worked out once rather than per ped.
            var hated = new List<int>();

            foreach (var id in mine.Rivals)
            {
                var them = _gangs.Get(id);

                if (them != null && them.GroupHash != 0) hated.Add(them.GroupHash);
            }

            if (hated.Count == 0) return;

            foreach (var crew in _out)
            {
                if (crew.Men.Count == 0) continue;

                Ped lead = null;

                foreach (var w in crew.Men)
                {
                    if (w.Man != null && w.Man.Exists() && w.Man.IsAlive) { lead = w.Man; break; }
                }

                if (lead == null) continue;

                Ped foe = null;
                var nearest = ScrapRange;

                try
                {
                    foreach (var ped in World.GetNearbyPeds(lead.Position, ScrapRange))
                    {
                        if (ped == null || !ped.Exists() || !ped.IsAlive || ped.IsPlayer) continue;

                        var group = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH,
                                                       ped.Handle);

                        if (!hated.Contains(group)) continue;

                        var gap = ped.Position.DistanceTo(lead.Position);
                        if (gap > nearest) continue;

                        nearest = gap;
                        foe = ped;
                    }
                }
                catch
                {
                    continue;
                }

                if (foe == null) continue;

                _nextScrap = now + ScrapGapMs;

                foreach (var w in crew.Men)
                {
                    if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;

                    // Already on him. Re-issuing restarts the approach, which is how a group
                    // ends up jogging on the spot instead of arriving.
                    if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, w.Man.Handle, foe.Handle))
                    {
                        continue;
                    }

                    try
                    {
                        // A bottle in the hand is not a weapon and holding one through a fight
                        // looks daft. It goes.
                        if (w.Holding != null && w.Holding.Exists())
                        {
                            w.Holding.Delete();
                            w.Holding = null;
                        }

                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, false);
                        Function.Call(Hash.TASK_COMBAT_PED, w.Man.Handle, foe.Handle, 0, 16);
                        Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
                    }
                    catch
                    {
                        // Next man.
                    }
                }

                Log.Info("Walkers: a crew went for somebody on the block.");
                return;
            }
        }

        /// <summary>How far they will notice one, and how often a crew starts something.</summary>
        private const float ScrapRange = 28f;
        private const int ScrapGapMs = 15000;

        private int _nextScrap;

        /// <summary>
        /// Somebody in the crew says hello as you go past.
        ///
        /// ONE MAN, NOT THE CREW. Four people turning and waving in unison is a chorus line;
        /// one of them clocking you and the rest carrying on is a group of blokes on a
        /// pavement, which is what they are. The others still get the look, because heads
        /// turning is cheap and is most of what "they noticed you" looks like.
        ///
        /// Foot crews wave OR speak rather than both -- doing both at once reads as somebody
        /// performing rather than somebody acknowledging.
        /// </summary>
        private void Greet(int now)
        {
            if (now < _nextGreet) return;

            Ped you;

            try
            {
                you = Game.Player.Character;
                if (you == null || !you.Exists() || !you.IsAlive) return;
            }
            catch
            {
                return;
            }

            foreach (var crew in _out)
            {
                if (crew.Men.Count == 0) continue;

                var near = -1;

                for (var i = 0; i < crew.Men.Count; i++)
                {
                    var m = crew.Men[i].Man;
                    if (m == null || !m.Exists() || !m.IsAlive) continue;

                    if (m.Position.DistanceTo(you.Position) > GreetRange) continue;

                    near = i;
                    break;
                }

                if (near < 0) continue;

                _nextGreet = now + GreetGapMs;

                foreach (var w in crew.Men) GangPeds.Notice(w.Man, you);

                var him = crew.Men[near].Man;

                // A man holding a bottle waves with the hand that has a bottle in it, which is
                // fine, but he is more likely to just shout.
                if (crew.Men[near].Holding != null || _rng.Next(100) < 40)
                {
                    GangPeds.Hello(him, _rng);
                }
                else if (!GangPeds.Wave(him, _rng))
                {
                    GangPeds.Hello(him, _rng);
                }

                return;
            }
        }

        private int _nextGreet;

        /// <summary>
        /// Points the lead somewhere else and everybody else at the lead.
        ///
        /// A quarter of the time they stop instead, and that is the half of "walking up and
        /// down" that makes it read as people rather than as a patrol route. A group that only
        /// ever walks is a group on its way somewhere.
        /// </summary>
        private void Aim(Crew crew, int now)
        {
            var stop = _rng.Next(100) < 26;

            if (stop)
            {
                foreach (var w in crew.Men)
                {
                    if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                        // A man with a drink drinks it. Everybody else stands about.
                        if (w.Holding == null || !w.Holding.Exists() || !Sip(w.Man))
                        {
                            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, w.Man.Handle,
                                          Standing[_rng.Next(Standing.Length)], 0, true);
                        }
                    }
                    catch
                    {
                        // He stands there either way.
                    }
                }

                // Long enough to be a stop rather than a stumble, and it ends when the stuck
                // check next looks at them -- which is exactly what "they stopped" should be.
                crew.Target = Vector3.Zero;
                crew.LookedAt = now;
                crew.WasAt = crew.Lead.Position;

                return;
            }

            var where = Alley(crew.Lead.Position);
            if (where == Vector3.Zero) where = Pavement(crew.Lead.Position);
            if (where == Vector3.Zero) return;

            crew.Target = where;
            crew.LookedAt = now;
            crew.WasAt = crew.Lead.Position;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, crew.Lead.Handle);

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, crew.Lead.Handle,
                              where.X, where.Y, where.Z, Pace, -1, 2f, true, 0f);

                Function.Call(Hash.SET_PED_KEEP_TASK, crew.Lead.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a crew on: " + ex.Message);
            }

            Gather(crew);
        }

        /// <summary>
        /// Everybody who is not the lead walks at the lead, each with his own place.
        ///
        /// Re-issued every time they are aimed rather than set once. A follow task is dropped
        /// by all sorts of things -- a scenario, a shove, a car door -- and a man whose follow
        /// has lapsed does not rejoin on his own, he just stands in the road behind them.
        /// </summary>
        private void Gather(Crew crew)
        {
            var slot = 0;

            foreach (var w in crew.Men)
            {
                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;
                if (w.Man.Handle == crew.Lead.Handle) continue;

                slot++;

                // Beside and behind, alternating sides, so four of them are a group walking
                // rather than a queue.
                var across = (slot % 2 == 0 ? 1f : -1f) * (0.8f + slot * 0.25f);
                var back = -(0.6f + slot * 0.5f);

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                    Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, w.Man.Handle,
                                  crew.Lead.Handle, across, back, 0f, Pace, -1, 1.6f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
                }
                catch
                {
                    // He catches up next time they are aimed.
                }
            }
        }

        /// <summary>One of them says something, loudly, now and again.</summary>
        private void Talk(Crew crew, int now)
        {
            if (now < crew.NextWord) return;

            crew.NextWord = now + _rng.Next(WordMinMs, WordMaxMs);

            var live = new List<Ped>();

            foreach (var w in crew.Men)
            {
                if (w.Man != null && w.Man.Exists() && w.Man.IsAlive) live.Add(w.Man);
            }

            if (live.Count == 0) return;

            var who = live[_rng.Next(live.Count)];

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle,
                              Words[_rng.Next(Words.Length)], "SPEECH_PARAMS_FORCE_SHOUTED");
            }
            catch
            {
                // A voice without that line simply says nothing.
            }
        }

        // ---- putting one out ----------------------------------------------------

        private void Send(Ped player)
        {
            var gang = _gangs.Get(_gangId);
            if (gang == null || gang.MemberModels.Count == 0) return;

            var at = Somewhere(player);
            if (at == Vector3.Zero) return;

            var crew = new Crew { BornAt = Game.GameTime, NextWord = Game.GameTime + 3000 };

            var want = _rng.Next(CrewMin, CrewMax + 1);

            for (var i = 0; i < want; i++)
            {
                // Spread around the spot rather than stacked on it, so they do not spawn
                // inside each other and spend their first second shoving.
                var spot = Ground(at.Around(0.8f + i * 0.7f));

                var man = GangPeds.OnFoot(gang, Shuffled(gang), spot,
                                          (float)_rng.NextDouble() * 360f);

                if (man == null) continue;

                var w = new Walker { Man = man };

                // Most of them hold nothing. A group where everybody has a bottle is a group
                // at a party, and this is four men on a pavement.
                if (_rng.Next(100) < 45)
                {
                    w.Holding = GangPeds.Hand(man, Bottles[_rng.Next(Bottles.Length)]);

                    // AND HE WALKS LIKE HE HAS HAD A FEW. The bottle was doing all the work of
                    // saying he had been drinking, which is a prop rather than a performance --
                    // he carried a beer down the alley with the gait of a man on his way to
                    // work. The clipset is the whole difference and it costs one call.
                    //
                    // Requested and checked, because a movement clipset applied before it has
                    // streamed in is silently ignored, and the man walks normally for the rest
                    // of the night with a beer in his hand.
                    if (w.Holding != null)
                    {
                        try
                        {
                            Function.Call(Hash.REQUEST_ANIM_SET, DrunkWalk);

                            if (Function.Call<bool>(Hash.HAS_ANIM_SET_LOADED, DrunkWalk))
                            {
                                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET,
                                              man.Handle, DrunkWalk, 1.0f);
                            }
                        }
                        catch
                        {
                            // He walks straight. Not the end of the world.
                        }
                    }
                }

                try
                {
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, man.Handle, false);

                    // AND THEY CAN FIGHT, which none of them could before.
                    //
                    // A ped's willingness to fight is not a default, it is a set of flags, and
                    // these had none of them -- so a crew walking their own block would watch a
                    // rival stroll past and carry on drinking. Not because anybody decided they
                    // should ignore him: because nobody had told them they were allowed not to.
                    //
                    // 46 is fight rather than flee, 5 lets an unarmed man square up to an armed
                    // one, and the flee attributes are cleared so a gunshot does not send the
                    // whole alley running. Movement is offensive: the difference between a man
                    // shooting from where he stands and a man walking at you.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 46, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 5, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 17, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, man.Handle, 0, false);
                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, man.Handle, 2);
                    Function.Call(Hash.SET_PED_COMBAT_ABILITY, man.Handle, 1);
                    Core.Helmets.Off(man);
                }
                catch
                {
                    // Cosmetic either way.
                }

                crew.Men.Add(w);
            }

            if (crew.Men.Count == 0) return;

            crew.Lead = crew.Men[0].Man;

            _out.Add(crew);
            Aim(crew, Game.GameTime);

            Log.Info("Walkers: " + crew.Men.Count + " out on " +
                     (_turf == null ? "the block" : _turf.ZoneName) + ".");
        }

        /// <summary>The set's models in a different order each time, so a crew is not four twins.</summary>
        private IEnumerable<string> Shuffled(GangDef gang)
        {
            var names = new List<string>(gang.MemberModels);

            for (var i = names.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var t = names[i];
                names[i] = names[j];
                names[j] = t;
            }

            return names;
        }

        // ---- where --------------------------------------------------------------

        private Vector3 Somewhere(Ped player)
        {
            for (var tries = 0; tries < 14; tries++)
            {
                try
                {
                    var away = SpawnNear + (float)_rng.NextDouble() * (SpawnFar - SpawnNear);
                    var probe = player.Position.Around(away);

                    var at = World.GetNextPositionOnSidewalk(probe);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(player.Position) < SpawnNear) continue;
                    if (!Ours(at)) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// A point on a back street near them.
        ///
        /// Asked of the road network rather than listed: a node the satnav will not route down
        /// is a service road, a yard or a cut-through, which is the same set of places people
        /// walk about in.
        /// </summary>
        private Vector3 Alley(Vector3 from)
        {
            for (var tries = 0; tries < 10; tries++)
            {
                try
                {
                    var probe = from.Around(18f + (float)_rng.NextDouble() * 45f);

                    var id = Function.Call<int>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_ID,
                                                probe.X, probe.Y, probe.Z,
                                                1 + _rng.Next(6), 1, 3f, 0f);

                    if (!Function.Call<bool>(Hash.IS_VEHICLE_NODE_ID_VALID, id)) continue;

                    var got = new OutputArgument();
                    Function.Call(Hash.GET_VEHICLE_NODE_POSITION, id, got);

                    var at = got.GetResult<Vector3>();

                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    // Held out for over the first six tries, then any of ours will do.
                    var backstreet = !Function.Call<bool>(Hash.GET_VEHICLE_NODE_IS_GPS_ALLOWED, id);
                    if (tries < 6 && !backstreet) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        private Vector3 Pavement(Vector3 from)
        {
            for (var tries = 0; tries < 8; tries++)
            {
                try
                {
                    var probe = from.Around(20f + (float)_rng.NextDouble() * 50f);
                    var at = World.GetNextPositionOnSidewalk(probe);

                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        private static Vector3 Ground(Vector3 at)
        {
            try
            {
                float z;

                if (World.GetGroundHeight(new Vector3(at.X, at.Y, at.Z + 1.5f), out z,
                                          GetGroundHeightMode.Normal))
                {
                    return new Vector3(at.X, at.Y, z);
                }
            }
            catch
            {
                // The spawn point will do.
            }

            return at;
        }

        private bool Ours(Vector3 at)
        {
            try
            {
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, at.X, at.Y, at.Z) ?? "";
                var owner = _gangs.OwnerOfZone(code);

                return owner != null &&
                       string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool OnOurTurf()
        {
            var owner = _turf == null ? null : _turf.Owner;
            return owner != null &&
                   string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
        }

        // ---- housekeeping -------------------------------------------------------

        private void Prune(int now)
        {
            var player = Game.Player.Character;

            for (var i = _out.Count - 1; i >= 0; i--)
            {
                var crew = _out[i];

                var old = now - crew.BornAt > LifetimeMs;

                var far = player != null && player.Exists() && crew.Lead != null &&
                          crew.Lead.Exists() &&
                          crew.Lead.Position.DistanceTo(player.Position) > LetGoRange;

                var gone = crew.Lead == null || !crew.Lead.Exists() || !crew.Lead.IsAlive;

                if (!old && !far && !gone) continue;

                Release(crew);
                _out.RemoveAt(i);
            }
        }

        /// <summary>Handed back to the game rather than deleted out from under you.</summary>
        private void Release(Crew crew)
        {
            foreach (var w in crew.Men)
            {
                try
                {
                    // The bottle goes. It is ours and it is attached to somebody who is about
                    // to stop being ours, and a beer welded to a passer-by outlives everything.
                    if (w.Holding != null && w.Holding.Exists())
                    {
                        Function.Call(Hash.DETACH_ENTITY, w.Holding.Handle, true, true);
                        w.Holding.Delete();
                    }

                    if (w.Man == null || !w.Man.Exists()) continue;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, false);

                    w.Man.IsPersistent = false;
                    w.Man.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Letting go of something already gone.
                }
            }

            crew.Men.Clear();
            crew.Lead = null;
        }

        /// <summary>Teardown.</summary>
        public void RestoreWorld()
        {
            foreach (var crew in _out) Release(crew);
            _out.Clear();
        }
    }
}
