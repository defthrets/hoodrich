using System;
using System.Collections.Generic;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.UI;

namespace Hoodrich.Missions
{
    /// <summary>Where the bike job has got to.</summary>
    internal enum BikePhase
    {
        None,

        /// <summary>A bike is waiting outside. Get on it.</summary>
        ToBike,

        /// <summary>Riding out to the courts with the homies.</summary>
        Riding,

        /// <summary>At the courts. They are stood there waiting to be spoken to.</summary>
        Words,

        /// <summary>Hands.</summary>
        Fight,

        /// <summary>Thirsty. The shop is down the road.</summary>
        Drink,

        /// <summary>Drink got. Ride back to Lamar.</summary>
        Home
    }

    /// <summary>
    /// The push-bike job.
    ///
    /// Scripted end to end rather than assembled from a site and a target count, because the
    /// shape of it is the point: four of you ride out on bikes, four of them are already stood
    /// on the court, somebody says something, and it goes off. No guns on either side -- pulling
    /// one is what ends it, not what wins it. Then you are thirsty, so you ride to the shop,
    /// and then you ride home. It is a straightener and an afternoon, not a hit.
    ///
    /// Every leg is ridden. Nothing here teleports and nothing fast-forwards, which is what
    /// makes it read as a day out rather than a checklist.
    /// </summary>
    internal sealed class BikeRide
    {
        // ---- the route ---------------------------------------------------------

        /// <summary>Where the bike is left for you, round the corner from Lamar.</summary>
        private static readonly Vector3 BikeSpot = new Vector3(-97.042f, -1610.761f, 32.313f);
        private const float BikeHeading = 56.429f;

        /// <summary>The courts in Chamberlain Hills.</summary>
        private static readonly Vector3 Courts = new Vector3(-227.173f, -1541.756f, 31.607f);

        /// <summary>The 24/7 down the road.</summary>
        private static readonly Vector3 Shop = new Vector3(29.028f, -1352.893f, 29.341f);

        /// <summary>The alley the homies come out of, round the back from Lamar.</summary>
        private static readonly Vector3 HomieSpot = new Vector3(-115.933f, -1609.875f, 31.249f);

        // ---- ranges and timings ------------------------------------------------

        private const float ArriveRange = 45f;
        private const float TalkRange = 6f;
        private const float ShopRange = 30f;
        private const float HomeRange = 25f;

        /// <summary>Further behind than this and they are put back on your wheel.</summary>
        private const float CatchUpRange = 90f;

        /// <summary>They are put on the court before you can see it, so nobody drops in.</summary>
        private const float PreSpawnRange = 170f;
        private const float CourtSpread = 5f;

        private const int UpdateIntervalMs = 400;

        /// <summary>Chatter on the ride out, so four men on bikes are not four silent men on bikes.</summary>
        private const int ChatterGapMs = 14000;

        /// <summary>How often the escort task is put back on anybody who has lost it.</summary>
        private const int RetaskGapMs = 2500;

        private static readonly string[] RideLines =
        {
            "GENERIC_HOWS_IT_GOING", "GENERIC_INSULT_MED", "CHASE_SUSPECT",
            "GENERIC_CURSE_MED", "GENERIC_WAR_CRY"
        };

        private static readonly string[] BikeModels = { "bmx", "cruiser", "scorcher", "tribike" };

        private const int PedTypeCiv = 4;

        // ---- state -------------------------------------------------------------

        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly Random _rng = new Random();

        private readonly List<Ped> _homies = new List<Ped>();
        private readonly List<Vehicle> _bikes = new List<Vehicle>();
        private readonly List<Ped> _rivals = new List<Ped>();
        private readonly List<Blip> _blips = new List<Blip>();

        private MissionDef _def;
        private Vehicle _playerBike;
        private Blip _marker;

        private int _lastUpdate;
        private int _nextChatter;
        private int _nextRetask;
        private bool _wentInside;
        private bool _talkHeld;
        private bool _wordsSaid;

        public BikeRide(Affiliation crew, GangRegistry gangs)
        {
            _crew = crew;
            _gangs = gangs;
        }

        /// <summary>Set by Main: the dialogue screen the courtyard exchange opens in.</summary>
        public Conversation Talk;

        /// <summary>
        /// Set by the runner. The block posts about the brawl WHILE it is happening.
        ///
        /// A fight that gets written up after the fact is a result. A fight that people are
        /// posting about while you are still swinging is an event, and it is the same content
        /// either way -- the only difference is when it arrives.
        /// </summary>
        public Hoodrich.Social.SocialFeed Social;

        private int _nextFightPost;

        public BikePhase Phase { get; private set; } = BikePhase.None;

        public bool IsRunning => Phase != BikePhase.None;

        public bool ReadyToCollect { get; private set; }

        /// <summary>Set when the player pulls a gun. Read and cleared by the runner.</summary>
        public string Failure { get; private set; }

        public string Objective
        {
            get
            {
                switch (Phase)
                {
                    case BikePhase.ToBike: return "Get on the bike";
                    case BikePhase.Riding: return "Ride to the courts in Chamberlain Hills";
                    case BikePhase.Words: return "Go say something to them";
                    case BikePhase.Fight: return "Hands only -- pull a gun and it's over";
                    case BikePhase.Drink: return "Go get a drink from the 24/7";
                    case BikePhase.Home: return "Ride back to Lamar";
                    default: return "";
                }
            }
        }

        // ---- starting ----------------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once the bike is out there.</summary>
        public string Start(MissionDef def)
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            _def = def;
            _wentInside = false;
            _wordsSaid = false;
            ReadyToCollect = false;
            Failure = null;

            _playerBike = SpawnBike(BikeSpot, BikeHeading);
            if (_playerBike == null) return "Ain't no bike out there.";

            Phase = BikePhase.ToBike;
            HoldTheLaw(true);
            Mark(BikeSpot, "Your bike", BlipColor.Yellow);

            Notify.Important("~g~Job on.~s~ " + Objective + ".");
            Log.Info("BikeRide started; bike at " + BikeSpot + ".");
            return null;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (!IsRunning) return;

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                Failure = "You went down out there.";
                return;
            }

            // Hands only, all the way through, not just during the fight. Pulling a gun on a
            // straightener is the whole thing you were told not to do, and it should not stop
            // being true the moment the last one goes down.
            if (Phase >= BikePhase.Words && Phase <= BikePhase.Fight && PlayerFired(player))
            {
                Failure = "You pulled a gun. That wasn't the job.";
                return;
            }

            switch (Phase)
            {
                case BikePhase.ToBike: TickToBike(player); return;
                case BikePhase.Riding: TickRiding(player); return;
                case BikePhase.Words: TickWords(player); return;
                case BikePhase.Fight: TickFight(); return;
                case BikePhase.Drink: TickDrink(player); return;
                case BikePhase.Home: TickHome(player); return;
            }
        }

        private void TickToBike(Ped player)
        {
            if (_playerBike == null || !_playerBike.Exists())
            {
                Failure = "Somebody took the bike.";
                return;
            }

            if (!player.IsInVehicle(_playerBike)) return;

            // They turn up when you get on, not when you take the job. Three men standing about
            // in a courtyard while you decide whether to bother is not the same picture.
            SpawnHomiesOnBikes(player);

            Phase = BikePhase.Riding;
            _nextChatter = Game.GameTime + ChatterGapMs;

            Mark(Courts, "The courts", BlipColor.Yellow);
            Notify.Ticker("~g~The homies rolled out with you.~s~");
        }

        private void TickRiding(Ped player)
        {
            Chatter();
            KeepUp(player);

            if (player.Position.DistanceTo(Courts) <= PreSpawnRange && _rivals.Count == 0)
            {
                SpawnRivals();
            }

            if (player.Position.DistanceTo(Courts) > ArriveRange) return;

            if (_rivals.Count == 0) SpawnRivals();

            if (_rivals.Count == 0)
            {
                Failure = "Wasn't nobody at the courts.";
                return;
            }

            Phase = BikePhase.Words;
            CallThemUp();
            ClearMarker();

            Notify.Important("~r~They're already here.~s~ " + Objective + ".");
        }

        private void TickWords(Ped player)
        {
            if (Talk == null || Talk.IsOpen) return;

            // The panel is gone and the line landed, so it goes off. Watched here rather than
            // hooked into the conversation screen, which does not know or care what it was for.
            if (_wordsSaid)
            {
                OnTalkClosed();
                return;
            }

            var nearest = Nearest(_rivals, player);
            if (nearest == null)
            {
                Phase = BikePhase.Fight;
                return;
            }

            if (Flat(player.Position, nearest.Position) > TalkRange) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to say something.");

            if (!Pressed()) return;

            _wordsSaid = false;
            Talk.Open(Words(), this);
        }

        /// <summary>
        /// One page and one way out.
        ///
        /// No branches on purpose: you rode across two neighbourhoods to say this, and being
        /// offered a polite exit at the last moment would make the ride pointless.
        /// </summary>
        private DialogueNode Words()
        {
            var gang = _gangs.Get(_def.TargetGang);
            var who = gang == null ? "Ballas" : gang.Name;

            var node = new DialogueNode(who,
                "You been talking a lot of smack online. We here right now, boy. " +
                "Whatchu wanna do?")
            {
                SpeakerColour = gang == null ? Palette.Danger : gang.Colour
            };

            node.Say("Say that again.", () =>
            {
                _wordsSaid = true;
                return null;
            }, "It goes off");

            return node;
        }

        private void TickFight()
        {
            // Somebody posts about it every few seconds while it is still going on.
            if (Social != null && Game.GameTime >= _nextFightPost)
            {
                _nextFightPost = Game.GameTime + 4500 + _rng.Next(4000);
                Social.On(Hoodrich.Social.SocialEvent.Brawl);
            }

            var standing = 0;

            foreach (var ped in _rivals)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) standing++;
            }

            if (standing > 0) return;

            Phase = BikePhase.Drink;
            _wentInside = false;

            Mark(Shop, "24/7", BlipColor.Yellow);
            Notify.Important("~g~That's that.~s~ Everybody's thirsty now. " + Objective + ".");
        }

        private void TickDrink(Ped player)
        {
            RemountHomies(player);

            var near = player.Position.DistanceTo(Shop) <= ShopRange;

            if (!_wentInside)
            {
                // Inside is the interior, not a radius. A radius around a shop front counts the
                // pavement outside it, and standing on the pavement is not going in.
                if (near && Inside(player))
                {
                    _wentInside = true;
                    Notify.Ticker("~g~Grab something and get out.~s~");
                }

                return;
            }

            if (Inside(player)) return;

            Phase = BikePhase.Home;

            Mark(Fixer.Spot, "Lamar", BlipColor.Yellow);
            Notify.Important("~g~Drink got.~s~ " + Objective + ".");
        }

        private void TickHome(Ped player)
        {
            RemountHomies(player);

            if (player.Position.DistanceTo(Fixer.Spot) > HomeRange) return;

            ReadyToCollect = true;
            ClearMarker();
        }

        // ---- the people --------------------------------------------------------

        private void SpawnHomiesOnBikes(Ped player)
        {
            var gang = _crew.Current;
            if (gang == null) return;

            var count = Math.Max(1, _def.Homies);

            for (var i = 0; i < count; i++)
            {
                // Out of the alley round the back, not conjured at your elbow. Three men
                // appearing beside you is a spawn; three men coming up the alley is an arrival.
                var spot = Ground(HomieSpot).Around(2.5f + i * 1.6f);

                var bike = SpawnBike(spot, player.Heading);
                if (bike == null) continue;

                var ped = SpawnGangMember(gang, spot);
                if (ped == null)
                {
                    Release(bike);
                    continue;
                }

                _homies.Add(ped);
                _bikes.Add(bike);

                try
                {
                    ped.SetIntoVehicle(bike, VehicleSeat.Driver);

                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);

                    Escort(ped, bike, player);

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Green;
                        blip.Scale = 0.6f;
                        blip.Name = "Homie";
                        _blips.Add(blip);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a homie on a bike: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Ride alongside rather than follow on foot.
        ///
        /// A group member on a bicycle gets off it to keep up with you, which is not a ride out,
        /// it is three men jogging. Escorting the player's own bike keeps them mounted.
        /// </summary>
        private static void Escort(Ped ped, Vehicle bike, Ped player)
        {
            try
            {
                var target = player.CurrentVehicle;

                if (target != null && target.Exists())
                {
                    // FOLLOW, not ESCORT.
                    //
                    // An escort has its own idea of where it should be relative to you and will
                    // happily carry on to that position when you stop, which is why they rode
                    // off. Following a vehicle means what it says: they go where you went, at
                    // your speed, and they stop when you stop.
                    Function.Call(Hash.TASK_VEHICLE_FOLLOW, ped.Handle, bike.Handle, target.Handle,
                                  25f, 786603, 8);
                    return;
                }

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, ped.Handle, bike.Handle,
                              Courts.X, Courts.Y, Courts.Z, 16f, 0, bike.Model.Hash, 786603, 6f, true);
            }
            catch
            {
                // The game own driving takes over.
            }
        }

        /// <summary>
        /// Reissued while riding, because one escort task does not survive the trip.
        ///
        /// A bicycle AI that clips a kerb, gets knocked off, or loses its task ends up standing
        /// in an alley two streets back for the rest of the job. Re-tasking anyone who has
        /// fallen behind or come off their bike costs nothing, and it is the difference between
        /// riding out with the homies and riding out on your own.
        /// </summary>
        private void KeepUp(Ped player)
        {
            if (Game.GameTime < _nextRetask) return;
            _nextRetask = Game.GameTime + RetaskGapMs;

            for (var i = 0; i < _homies.Count; i++)
            {
                var ped = _homies[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (i >= _bikes.Count) continue;

                var bike = _bikes[i];
                if (bike == null || !bike.Exists()) continue;

                if (!ped.IsInVehicle(bike))
                {
                    try
                    {
                        Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, bike.Handle,
                                      -1, (int)VehicleSeat.Driver, 2f, 1, 0);
                    }
                    catch { /* they will walk it */ }

                    continue;
                }

                // Anybody who has drifted a long way behind is brought back rather than left to
                // ride the whole route on their own. Four streets back is not following.
                // Sent after you rather than moved to you. A bike that jumps to your back wheel
                // is a homie who was never really riding with you, and the ride out IS the job.
                if (ped.Position.DistanceTo(player.Position) > CatchUpRange)
                {
                    try
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, ped.Handle, bike.Handle,
                                      player.Position.X, player.Position.Y, player.Position.Z,
                                      24f, 0, bike.Model.Hash, 786603, 8f, true);
                    }
                    catch { /* they will catch up or they will not */ }

                    continue;
                }

                Escort(ped, bike, player);
            }
        }

        /// <summary>
        /// Tells anybody still on the road where it is kicking off.
        ///
        /// They used to be teleported onto the court, so a homie who came off two streets back
        /// simply appeared beside you -- and the moment that can happen, none of the riding
        /// matters. Now they are told and they ride there. If one of them misses the fight
        /// because he clipped a kerb at a junction, that is a thing that happened.
        /// </summary>
        private void CallThemUp()
        {
            foreach (var ped in _homies)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (Flat(ped.Position, Courts) <= 40f) continue;

                try
                {
                    var bike = ped.CurrentVehicle;

                    if (bike != null && bike.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, ped.Handle, bike.Handle,
                                      Courts.X, Courts.Y, Courts.Z, 24f, 0, bike.Model.Hash,
                                      786603, 8f, true);
                    }
                    else
                    {
                        Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                                      Courts.X, Courts.Y, Courts.Z, 2f, 60000, 5f, 0, 0f);
                    }

                    Log.Info("BikeRide: told a straggler where it is.");
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not call a homie up: " + ex.Message);
                }
            }
        }

        /// <summary>Puts them back on their bikes once you are back on yours.</summary>
        private void RemountHomies(Ped player)
        {
            if (!player.IsInVehicle()) return;

            for (var i = 0; i < _homies.Count; i++)
            {
                var ped = _homies[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (ped.IsInVehicle()) continue;

                if (i >= _bikes.Count) continue;

                var bike = _bikes[i];
                if (bike == null || !bike.Exists()) continue;

                try
                {
                    Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, bike.Handle,
                                  -1, (int)VehicleSeat.Driver, 2f, 1, 0);
                }
                catch
                {
                    // They will walk it.
                }
            }
        }

        private void SpawnRivals()
        {
            var gang = _gangs.Get(_def.TargetGang);
            if (gang == null) return;

            var count = Math.Max(1, _def.Targets);

            for (var i = 0; i < count; i++)
            {
                var ped = SpawnGangMember(gang, Courts.Around(CourtSpread));
                if (ped == null) continue;

                _rivals.Add(ped);

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                    // Hands, and nothing they can change their mind about halfway through.
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);

                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle,
                                  "WORLD_HUMAN_STAND_IMPATIENT", 0, true);

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Red;
                        blip.Scale = 0.7f;
                        blip.Name = gang.Name;
                        _blips.Add(blip);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not set up a rival: " + ex.Message);
                }
            }

            Log.Info("BikeRide: " + _rivals.Count + " waiting at the courts.");
        }

        /// <summary>The exchange is over and somebody has to go first.</summary>
        private void OnTalkClosed()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            Phase = BikePhase.Fight;
            _nextFightPost = Game.GameTime + 2000;

            foreach (var ped in _rivals)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);
                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                }
                catch
                {
                    // The game's AI takes over.
                }
            }

            // Everybody, not just whoever you happened to be facing. One at a time queuing up
            // for a turn is a training dummy, not four men on a court.
            foreach (var ped in _homies)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try { Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true); }
                catch { /* they will use their hands anyway */ }
            }

            Notify.Important("~r~It's on.~s~ " + Objective + ".");
        }

        // ---- helpers -----------------------------------------------------------

        /// <summary>
        /// Switches the wanted system off for the length of the job, and back on afterwards.
        ///
        /// This is a straightener on a basketball court, not a crime. Four men having a fight
        /// in a park should not put a helicopter over Chamberlain, and a mission that fails
        /// because a passing patrol saw a fist fight it was never meant to notice is a mission
        /// nobody can finish. Turned back on the moment the job ends, however it ends.
        /// </summary>
        private void HoldTheLaw(bool held)
        {
            // Through the shared switch. A gang war can start while this job is running -- they
            // roll on separate clocks and nothing stops them overlapping -- and whichever of the
            // two ended first used to turn the police back on for the other. Either a fist
            // fight on a basketball court brought a helicopter, or a raid on your own block did.
            //
            // The old SET_EVERYONE_IGNORE_PLAYER call that used to live here passed false on
            // both sides of the branch, so it never did anything in either direction.
            if (held) LawHold.Hold(this);
            else LawHold.Release(this);
        }

        private void Chatter()
        {
            if (Game.GameTime < _nextChatter) return;
            _nextChatter = Game.GameTime + ChatterGapMs;

            var speaker = _homies.Count == 0 ? null : _homies[_rng.Next(_homies.Count)];
            if (speaker == null || !speaker.Exists() || !speaker.IsAlive) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, speaker.Handle,
                              RideLines[_rng.Next(RideLines.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        /// <summary>True the frame the player actually fires, not merely holds something.</summary>
        private static bool PlayerFired(Ped player)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle);
            }
            catch
            {
                return false;
            }
        }

        private static bool Inside(Ped player)
        {
            try
            {
                return Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle) != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Settles a coordinate onto the ground, but only if the ground is roughly where the
        /// coordinate already said it was.
        ///
        /// Every authored position in this job was read off the player HUD while stood on the
        /// spot, so the height is already right. Probing downward from fifteen metres up in a
        /// courtyard finds the first thing it hits, which is a balcony -- and that is how the
        /// bike ended up on a roof. A probe that disagrees by more than a storey is wrong about
        /// a coordinate somebody measured by standing on it.
        /// </summary>
        private static Vector3 Ground(Vector3 where)
        {
            try
            {
                if (World.GetGroundHeight(new Vector3(where.X, where.Y, where.Z + 1.5f),
                                          out var groundZ, GetGroundHeightMode.Normal) &&
                    groundZ > 0f && Math.Abs(groundZ - where.Z) <= 3f)
                {
                    where.Z = groundZ;
                }
            }
            catch
            {
                // Keep the authored height.
            }

            return where;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static Ped Nearest(List<Ped> peds, Ped player)
        {
            Ped best = null;
            var bestRange = float.MaxValue;

            foreach (var ped in peds)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                var range = Flat(player.Position, ped.Position);
                if (range >= bestRange) continue;

                bestRange = range;
                best = ped;
            }

            return best;
        }

        private bool Pressed()
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

        private Vehicle SpawnBike(Vector3 where, float heading)
        {
            var spot = Ground(where);
            spot.Z += 0.4f;

            foreach (var name in BikeModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var bike = World.CreateVehicle(model, spot, heading);
                    model.MarkAsNoLongerNeeded();

                    if (bike == null || !bike.Exists()) continue;

                    bike.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, bike.Handle, true, true);

                    return bike;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Warn("No push bike model would load.");
            return null;
        }

        private Ped SpawnGangMember(GangDef gang, Vector3 near)
        {
            foreach (var name in gang.MemberModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var spot = World.GetNextPositionOnSidewalk(near);
                    if (spot == Vector3.Zero) spot = near;

                    // A ped created above the ground falls to it, which is the drop from the sky.
                    try
                    {
                        if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 15f),
                                                  out var groundZ, GetGroundHeightMode.Normal) && groundZ > 0f)
                        {
                            spot.Z = groundZ;
                        }
                    }
                    catch
                    {
                        // Keep what the pavement gave us.
                    }

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                    spot.X, spot.Y, spot.Z, 0f, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        private void Mark(Vector3 where, string name, BlipColor colour)
        {
            ClearMarker();

            try
            {
                _marker = World.CreateBlip(where);
                if (_marker == null || !_marker.Exists()) return;

                _marker.Color = colour;
                _marker.ShowRoute = true;
                _marker.Name = name;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the next leg: " + ex.Message);
            }
        }

        private void ClearMarker()
        {
            try { if (_marker != null && _marker.Exists()) _marker.Delete(); }
            catch { /* teardown */ }

            _marker = null;
        }

        private static void Release(Vehicle car)
        {
            try
            {
                if (car == null || !car.Exists()) return;
                car.MarkAsNoLongerNeeded();
            }
            catch { /* teardown */ }
        }

        // ---- finishing ---------------------------------------------------------

        public void Clear()
        {
            // First, before anything else can go wrong in teardown. Leaving the player unable
            // to attract police for the rest of the session because a cleanup threw is far
            // worse than any of the litter below.
            if (IsRunning) HoldTheLaw(false);

            ClearMarker();

            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* teardown */ }
            }
            _blips.Clear();

            foreach (var ped in _rivals)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _rivals.Clear();

            foreach (var ped in _homies)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _homies.Clear();

            // The bikes are left where they are rather than deleted. Four bikes vanishing off a
            // street the moment you get paid is the mod tidying up in front of you.
            foreach (var bike in _bikes) Release(bike);
            _bikes.Clear();

            Release(_playerBike);
            _playerBike = null;

            _def = null;
            Phase = BikePhase.None;
            ReadyToCollect = false;
            Failure = null;
            _wentInside = false;
            _wordsSaid = false;
        }
    }
}
