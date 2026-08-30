using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Territory;

namespace Hoodrich.Gangs
{
    /// <summary>What one of them is doing at this moment.</summary>
    internal enum RollPhase
    {
        Rolling,
        Sitting
    }

    /// <summary>One carload of them, or one man on a bike.</summary>
    internal sealed class Roll
    {
        public Vehicle Car;
        public Ped Driver;
        public readonly List<Ped> Crew = new List<Ped>();

        /// <summary>A bike on the pavement rather than a car on the road.</summary>
        public bool OnFoot;

        public RollPhase Phase = RollPhase.Rolling;

        /// <summary>Where they are headed, and whether that spot is worth stopping at.</summary>
        public Vector3 Target;
        public bool StopThere;

        /// <summary>When the sitting ends.</summary>
        public int SitUntil;

        /// <summary>Where they were when last looked at, and when that was.</summary>
        public Vector3 WasAt;
        public int LookedAt;

        /// <summary>How many times they have been unstuck before being written off.</summary>
        public int Nudges;

        public int BornAt;

        /// <summary>
        /// The one this bike is riding with, or null for a man on his own.
        ///
        /// A REFERENCE RATHER THAN AN ID, because the only question ever asked of it is "where
        /// is he going" and the answer is a field on the other Roll. An id would mean a lookup
        /// through the list every time, for a list that is four long.
        ///
        /// The lead can die, crash, or be let go for being stuck while the rest are still out.
        /// Nothing repairs that and nothing needs to: a mate whose lead is gone falls back to
        /// picking his own way, which is what a man whose mate has ridden off does.
        /// </summary>
        public Roll Lead;

        /// <summary>While this is in the future, the front wheel is up.</summary>
        public int WheelieUntil;

        /// <summary>And not another one before this, so it is a moment and not a stance.</summary>
        public int WheelieAfter;

        /// <summary>When they were first noticed off the blocks, or nought while they are on.</summary>
        public int StrayedAt;
    }

    /// <summary>
    /// Their own people, out driving their own blocks.
    ///
    /// Everything in this mod that arrives by car arrives because of you: a delivery, a
    /// payback, a raid. Which means the only Families car you ever see is one that wants
    /// something, and a neighbourhood where every vehicle is plot is a neighbourhood nobody
    /// lives in. These want nothing. Four of them go round the block at thirty, pull into an
    /// alley, sit there a while, and go round again -- and they will still be doing it after
    /// you have walked off, which is the whole point of them.
    ///
    /// Two ways of moving, because a block is not only its roads. The cars use the road
    /// network. The bikes are given pavement to aim at and told to take the short way, which
    /// puts them up on the footpath cutting between the yards -- the way anybody actually rides
    /// round here.
    ///
    /// Only on our own turf, and only while you are stood on it. There is no world simulation
    /// behind this and there should not be: spawning carloads of Families across the map on the
    /// chance you drive past is a memory bill for something nobody sees.
    /// </summary>
    internal sealed class Rollers
    {
        private const int TickMs = 900;

        /// <summary>
        /// Far enough out to arrive rather than appear.
        ///
        /// Nothing is spawned inside seventy-five metres. A car that materialises at the end of
        /// the street is a bug report; the same car coming round the corner is traffic.
        /// </summary>
        private const float SpawnNear = 75f;
        private const float SpawnFar = 145f;

        /// <summary>Past this they are somebody else's problem, so they are handed back.</summary>
        private const float LetGoRange = 260f;

        /// <summary>
        /// How long one gets to find its way back onto the blocks before it is let go.
        ///
        /// Long enough to drive round a corner and short enough that nobody follows a green
        /// Manchez onto the freeway wondering where it thinks it is going.
        /// </summary>
        private const int StrayMs = 20000;

        private const int GapMinMs = 8000;
        private const int GapMaxMs = 24000;

        /// <summary>How long they sit in an alley with the engine running.</summary>
        private const int SitMinMs = 12000;
        private const int SitMaxMs = 45000;

        /// <summary>Chance the next place they are headed is somewhere they will stop.</summary>
        private const int StopChancePercent = 45;

        /// <summary>
        /// And the same for a bike, which used to be nought.
        ///
        /// Bikes never stopped. The stop roll was written !OnFoot, so every rider went round
        /// and round until his eight minutes were up and he was handed back -- which is fine as
        /// traffic and useless as life. Somebody riding round the block is going somewhere;
        /// somebody stood over his bike in the park with two mates IS the block.
        /// </summary>
        private const int BikeStopChancePercent = 55;

        /// <summary>How many ride out together when they ride out together.</summary>
        private const int PackMin = 2;
        private const int PackMax = 3;

        /// <summary>Chance a bike going out brings his crew rather than going alone.</summary>
        private const int PackChancePercent = 45;

        /// <summary>
        /// How far off the lead's own spot the rest of a crew are aimed.
        ///
        /// Not zero, because three bikes given one identical coordinate arrive as three bikes
        /// trying to occupy one square metre, and the game resolves that by shoving them.
        /// </summary>
        private const float PackSpread = 6.5f;

        /// <summary>
        /// Pulling the front up.
        ///
        /// There is no native for this. TASK_VEHICLE_TEMP_ACTION has a list of stunts and none
        /// of them is a wheelie, so it is done the way it is done in the physics: a shove
        /// upwards applied BEHIND the centre of mass, which pitches the nose up and leaves the
        /// back wheel driving. Held frame by frame while it lasts rather than applied once,
        /// because one impulse at 900ms intervals is a bump in the road, not a wheelie.
        ///
        /// Wanting speed is what keeps it honest. A bike doing walking pace that rears up is a
        /// bike being levitated; the same bike doing thirty is a rider showing off.
        /// </summary>
        private const int WheelieChancePercent = 30;
        private const int WheelieMinMs = 1100;
        private const int WheelieMaxMs = 2600;
        private const int WheelieRestMinMs = 7000;
        private const int WheelieRestMaxMs = 22000;
        private const float WheelieNeedsSpeed = 9f;
        private const float WheelieLift = 1.9f;
        private const float WheelieBehind = 1.15f;

        /// <summary>
        /// Where they stand about with the engines off.
        ///
        /// ONE COORDINATE, AND IT IS THE ONE THAT IS KNOWN GOOD. The Chamberlain courts are
        /// already in this mod -- Lamar rides out to them -- so it is a spot that has been
        /// stood on, driven to and filmed rather than a number read off a map. Everything else
        /// is found at runtime by Green() below, which is slower to write and cannot put a bike
        /// inside a wall.
        ///
        /// Add to it freely. A hangout is only ever used when the player is already near it, so
        /// a bad entry costs nothing anywhere else on the map.
        /// </summary>
        private static readonly Vector3[] Hangouts =
        {
            // The basketball courts in Chamberlain Hills.
            new Vector3(-227.173f, -1541.756f, 31.607f)
        };

        /// <summary>Near enough to be worth riding to rather than a trip across town.</summary>
        private const float HangoutRange = 320f;

        /// <summary>No road this close, and it is open ground rather than a kerb.</summary>
        private const float OffRoad = 15f;

        /// <summary>A road this close, and he is on one.</summary>
        private const float OnRoad = 7f;

        /// <summary>
        /// How hard the front comes up, out of the ini.
        ///
        /// In the ini because it is the one number here that cannot be reasoned about from a
        /// desk. It is a force against a mass the game owns, on a model that can be swapped,
        /// and the difference between a front wheel skimming and a bike on its back is a
        /// decimal place. Whoever is looking at it can move it; nobody who is not, cannot.
        /// </summary>
        private float Lift => _cfg == null ? WheelieLift : _cfg.RollerWheelieLift;

        /// <summary>Close enough to call it arrived.</summary>
        private const float ArrivedRange = 16f;

        /// <summary>Nothing lasts forever, so the same four are not circling all afternoon.</summary>
        private const int LifetimeMs = 480000;

        /// <summary>
        /// Stuck: less than this much movement between two looks, that many times over.
        ///
        /// Learnt from the gang war, where two men out of eight would find a fence and stand at
        /// it for the rest of the fight. A thing that cannot reach where it is going has to be
        /// given one more try and then taken off the board, or it stops being scenery and
        /// becomes a car parked across a junction.
        /// </summary>
        private const float StuckMoved = 3f;
        private const int StuckLookMs = 9000;
        private const int MaxNudges = 2;

        /// <summary>Slow. Around thirty for a car, a bit less for a bike.</summary>
        private const float CruiseCar = 8.5f;
        private const float CruiseBike = 6f;

        /// <summary>
        /// Normal road driving for the cars.
        ///
        /// The bikes get the shortest-path bit instead, which is what puts them over the kerb:
        /// aimed at a spot on the pavement and allowed to take the direct way to it, a bicycle
        /// mounts the footpath rather than going round by the road. Peds and objects are still
        /// avoided, so it is a bike weaving past people, not a bike through them.
        /// </summary>
        private const int StyleCar = 786603;
        private const int StyleBike = 262196;

        private const int PedTypeCiv = 4;

        /// <summary>
        /// What they turn up in.
        ///
        /// Read off the spawn menu's own hash rather than guessed at, so the name here is the
        /// model that was actually looked at. Half of them are 1.69 and later, which the Legacy
        /// build has never had -- hence the spares below and the check before spawning. A
        /// missing car is a different car here, not an exception in the log.
        /// </summary>
        private static readonly string[] Cars =
        {
            // The Sentinel GTS, three times, so it turns up often enough to be the car the set
            // is known for rather than one you see once an evening. Three names because the
            // GTS is a late addition and installs disagree about what it is called -- the list
            // is tried in order and the log says which one actually loaded, so a build without
            // it quietly gets an ordinary Sentinel instead of nothing.
            "sentinelgts", "sentinel4", "sentinel3",

            "minimus", "woodlander", "hardy", "driftdominator10", "driftgauntlet4",
            "driftchavosv6", "s95", "vorschlaghammer", "asterope2", "dorado",
            "driftfr36", "kanjosj", "iwagen", "toros"
        };

        /// <summary>Always present, for the install that has none of the above.</summary>
        private static readonly string[] SpareCars = { "buccaneer2", "voodoo", "manana", "primo2" };

        /// <summary>
        /// What they ride round the back streets: Street Blazers, Sanchezes, push bikes.
        ///
        /// Cut back to those three families, which is what was asked for twice. The list had
        /// grown a scooter, two cheap sports bikes, a chopper and a couple of customs -- and a
        /// man on a Bagger is somebody riding through the neighbourhood rather than somebody
        /// from it. These three are what is actually parked in these yards.
        /// </summary>
        private static readonly string[] Bikes =
        {
            // The Manchez first and twice, because it is what was asked for and because a
            // crew that turns up on three different bikes is three men who happen to be
            // riding, where three on the same one is a crew.
            "manchez", "manchez",

            // The Street Blazer, then the plain one behind it.
            "blazer4", "blazer",

            // Dirt bikes, which is the same answer at a different price.
            "sanchez", "sanchez2",

            // And what you pedal. The Inductor and the Stryder sit here rather than with the
            // engines: both are bicycle-shaped, both were asked for by name, and on a footpath
            // they read as push bikes with a motor rather than as motorbikes.
            "inductor", "inductor2", "stryder",
            "bmx", "cruiser", "scorcher", "fixter", "tribike"
        };

        private static readonly string[] SpareBikes = { "bmx", "scorcher" };

        /// <summary>Dark green, out of the game's own paint table.</summary>
        private const int DarkGreen = 49;

        /// <summary>
        /// The flake over the top of it. A brighter green, so it lifts rather than flattens.
        ///
        /// Overridable in the ini and deliberately so -- see the note in Paint. If this one is
        /// not the green it should be it is one line to change and nothing is rebuilt.
        /// </summary>
        private const int DefaultPearl = 53;

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly TurfWatch _turf;
        private readonly Random _rng = new Random();

        private readonly List<Roll> _out = new List<Roll>();

        private int _lastTick;
        private int _nextSpawn;

        /// <summary>Off while something louder is happening. Wired by the house script.</summary>
        public Func<bool> Busy;

        /// <summary>
        /// The settings are read every tick rather than copied at startup.
        ///
        /// Because the wheel can change them. A value copied into a field here would leave the
        /// switch on the settings page doing nothing until the next reload, which is a switch
        /// that appears broken -- and the whole reason that page exists is that changes take.
        /// </summary>
        private bool Enabled => _cfg == null || _cfg.RollersEnabled;

        private int MaxCars => _cfg == null ? 2 : _cfg.RollerCars;
        private int MaxBikes => _cfg == null ? 2 : _cfg.RollerBikes;

        public Rollers(Settings cfg, GangRegistry gangs, string gangId, TurfWatch turf)
        {
            _cfg = cfg;
            _gangs = gangs;
            _gangId = gangId;
            _turf = turf;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;

            // ABOVE THE THROTTLE ON PURPOSE. A wheelie is held by pushing on the bike every
            // frame it lasts; the same push at nine-hundred-millisecond intervals is a pothole.
            // Everything below here is a decision and can wait its turn, but this is physics
            // and has to run at the rate the physics does.
            Wheelies(now);

            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            try
            {
                Prune(now);

                if (!Enabled)
                {
                    // Turned off mid-session. Handed back rather than left circling, so the
                    // switch does something you can see out of the window.
                    if (_out.Count > 0)
                    {
                        foreach (var roll in _out) Release(roll);
                        _out.Clear();
                    }

                    return;
                }

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                // Backwards, because a carload that has given up is dropped where it stands and
                // a foreach over a list something is being removed from throws.
                for (var i = _out.Count - 1; i >= 0; i--)
                {
                    if (!Steer(_out[i], now)) continue;

                    Release(_out[i]);
                    _out.RemoveAt(i);
                }

                // Existing ones are left to finish whatever they were doing when you walked off
                // our blocks; new ones only start on them.
                if (!OnOurTurf()) return;
                if (Busy != null && Busy()) return;

                if (_nextSpawn == 0) _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);
                if (now < _nextSpawn) return;

                _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);

                // A bike is cheaper and reads better on a quiet street, so it wins the coin
                // toss more often than not.
                var wantBike = _rng.Next(100) < 72;

                if (wantBike && Count(true) < MaxBikes)
                {
                    var lead = Send(player, true, null);

                    // The rest of his crew, spawned around him and pointed wherever he is
                    // pointed. Held to the same ceiling as everybody else, so a pack is not a
                    // way round RollerBikes -- turning that down to two gets you pairs, and
                    // turning it to one gets you the mod as it was.
                    if (lead != null && _rng.Next(100) < PackChancePercent)
                    {
                        var mates = _rng.Next(PackMin, PackMax + 1) - 1;

                        for (var i = 0; i < mates && Count(true) < MaxBikes; i++)
                        {
                            Send(player, true, lead);
                        }
                    }
                }
                else if (Count(false) < MaxCars) Send(player, false, null);
                else if (Count(true) < MaxBikes) Send(player, true, null);
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers tripped: " + ex.Message);
            }
        }

        private int Count(bool bikes)
        {
            var n = 0;
            foreach (var roll in _out)
            {
                if (roll.OnFoot == bikes) n++;
            }
            return n;
        }

        // ---- keeping them moving -----------------------------------------------

        /// <summary>
        /// One carload, one tick.
        ///
        /// Arriving is the only event here. Everything else -- sitting, moving off, being
        /// stuck -- hangs off whether they got where they were going, because the drive task
        /// stops the car at its destination and holds it there. That is why nothing needs to
        /// brake or park: a car told to drive to a point in an alley IS a car sat in an alley
        /// once it gets there, and all this has to do is not give it a new instruction for a
        /// while.
        /// </summary>
        /// <returns>True when this one has been given up on and should be let go.</returns>
        private bool Steer(Roll roll, int now)
        {
            if (roll.Car == null || !roll.Car.Exists()) return false;

            if (roll.Phase == RollPhase.Sitting)
            {
                if (now < roll.SitUntil) return false;

                roll.Phase = RollPhase.Rolling;
                roll.Nudges = 0;
                Aim(roll, now);
                return false;
            }

            var here = roll.Car.Position;

            // OFF THE BLOCKS.
            //
            // Everything about these people is that they are FROM somewhere -- they spawn on
            // Chamberlain Hills and Strawberry, they are painted the set's green, and a green
            // Manchez three districts away is not the set, it is a stray car with a story
            // nobody wrote. Every destination they are given is already checked against the
            // turf, so the only way out is drift: a wander that had nowhere better to go, or a
            // driver taking a wide line round a junction on the boundary.
            //
            // Noticed once, pointed home once, and handed back if it cannot get there. Not
            // teleported and not deleted -- a car that vanishes while you are looking at it is
            // worse than one that drives off.
            if (Ours(here))
            {
                roll.StrayedAt = 0;
            }
            else
            {
                if (roll.StrayedAt == 0)
                {
                    roll.StrayedAt = now;
                    Aim(roll, now);
                    return false;
                }

                if (now - roll.StrayedAt > StrayMs)
                {
                    Log.Debug("Rollers: one wandered off the blocks and was handed back.");
                    return true;
                }
            }

            if (roll.Target != Vector3.Zero && here.DistanceTo(roll.Target) < ArrivedRange)
            {
                if (roll.StopThere)
                {
                    roll.Phase = RollPhase.Sitting;
                    roll.SitUntil = now + _rng.Next(SitMinMs, SitMaxMs);
                    return false;
                }

                roll.Nudges = 0;
                Aim(roll, now);
                return false;
            }

            // Not there yet. Two looks with nothing between them is a wall.
            if (now - roll.LookedAt < StuckLookMs) return false;

            var moved = roll.LookedAt == 0 ? float.MaxValue : here.DistanceTo(roll.WasAt);

            roll.WasAt = here;
            roll.LookedAt = now;

            if (moved > StuckMoved) return false;

            roll.Nudges++;

            // Given up on. Handed back rather than deleted, because deleting a car the player
            // might be looking at is worse than one that drives off oddly.
            if (roll.Nudges > MaxNudges) return true;

            Aim(roll, now);
            return false;
        }

        /// <summary>Points them at somewhere else on the block and lets them go.</summary>
        private void Aim(Roll roll, int now)
        {
            if (roll.Car == null || !roll.Car.Exists()) return;
            if (roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive) return;

            // A mate goes where the lead goes and stops when the lead stops. He does not get
            // his own opinion about it, which is the entire difference between a crew and three
            // men who happened to set off at the same time.
            //
            // A lead who has crashed, died or been handed back for being stuck leaves his mates
            // with nothing to follow, and they quietly go back to riding on their own -- which
            // is what somebody whose mate has ridden off does.
            var lead = roll.Lead;

            // STILL ONE OF OURS, checked here rather than everywhere a roll is dropped.
            //
            // Release does not null the vehicle -- it hands it back to the game, which leaves
            // Car.Exists() perfectly true on a bike that is now ordinary traffic riding off
            // with a Target it was given twenty minutes ago. Asking whether the lead is still
            // on the books catches that, and catches it in the one place that reads Lead at
            // all, so no future removal site can forget to do it.
            if (lead != null && !_out.Contains(lead))
            {
                roll.Lead = null;
                lead = null;
            }

            var following = roll.OnFoot && lead != null &&
                            lead.Car != null && lead.Car.Exists() &&
                            lead.Target != Vector3.Zero;

            var stop = following
                ? lead.StopThere
                : _rng.Next(100) < (roll.OnFoot ? BikeStopChancePercent : StopChancePercent);

            // A rider goes down the back streets about as often as he goes along the pavement.
            //
            // Pavement alone put every one of them out on the main road frontage, which is the
            // one place a bike is least interesting -- the service roads and the cut-throughs
            // behind the buildings are where anybody round here actually rides, and the road
            // network already knows which ones those are.
            // WHERE THE PROBES START, which is not always where the vehicle is.
            //
            // Every one of them looks for somewhere within a hundred metres or so and throws
            // out anything off our turf. That works perfectly while they are ON it and fails
            // completely the moment they are not: a car sat one street outside the zone probes
            // around itself, every probe lands outside as well, nothing is found, and the only
            // thing left is a wander -- which takes it further out, so the next tick fails
            // harder. It cannot look its way home from where it is standing.
            //
            // So a stray probes around the PLAYER instead. He is the one position known to be
            // on our blocks, because nothing is put out at all unless he is stood on them.
            var from = Anchor(roll);

            Vector3 where;

            if (following)
            {
                where = Beside(lead.Target);
            }
            else if (roll.OnFoot && stop)
            {
                // Somewhere to stand about, rather than somewhere to ride to.
                where = Hangout(from);
            }
            else if (roll.OnFoot)
            {
                where = _rng.Next(100) < 55 ? Node(from, true) : Pavement(from);
            }
            else
            {
                where = Node(from, stop);
            }

            // Whichever it asked for, take the other rather than stand still.
            if (where == Vector3.Zero && roll.OnFoot) where = Pavement(from);

            // Still nothing, and already off the blocks. Anywhere on them will do -- getting
            // back is the whole job at this point and which street it is does not matter.
            if (where == Vector3.Zero && !Ours(roll.Car.Position)) where = Homeward();

            // Nowhere to send them this tick -- the probes all landed off our turf, or off the
            // road network entirely. They wander instead of standing still, because a car
            // stopped in a live lane is the exact thing the traffic watchdog exists to remove
            // and this one has a driver in it, so nothing would remove it.
            // Only ever reached ON our turf now, or with no player to steer back to. That
            // matters: wander has no idea where Chamberlain Hills is, and handing it to a car
            // that has already drifted out is an instruction to keep going.
            if (where == Vector3.Zero)
            {
                try
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, roll.Driver.Handle, roll.Car.Handle,
                                  roll.OnFoot ? CruiseBike : CruiseCar,
                                  roll.OnFoot ? StyleBike : StyleCar);

                    // No destination any more, which matters: leaving the old one in place
                    // would read as "arrived" again next tick and restart the wander every
                    // 900ms, and a car handed a fresh instruction nine times a second does not
                    // go anywhere at all.
                    roll.Target = Vector3.Zero;
                    roll.StopThere = false;
                    roll.LookedAt = now;
                    roll.WasAt = roll.Car.Position;
                }
                catch
                {
                    // They will be looked at again in nine seconds either way.
                }

                return;
            }

            roll.Target = where;
            roll.StopThere = stop;
            roll.LookedAt = now;
            roll.WasAt = roll.Car.Position;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, roll.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, roll.Driver.Handle, roll.Car.Handle,
                              where.X, where.Y, where.Z,
                              roll.OnFoot ? CruiseBike : CruiseCar,
                              0, roll.Car.Model.Hash,
                              roll.OnFoot ? StyleBike : StyleCar,
                              stop ? 4f : 15f, true);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle,
                              roll.OnFoot ? CruiseBike : CruiseCar);

                Function.Call(Hash.SET_PED_KEEP_TASK, roll.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a roller on: " + ex.Message);
            }
        }

        // ---- where they go -----------------------------------------------------

        /// <summary>
        /// A point on the road network near them, on our blocks.
        ///
        /// Back alleys are picked out by asking the map, not by listing coordinates: the game
        /// marks a node as GPS-allowed when it is a road the satnav would route you down, so the
        /// ones it will NOT route down are the service roads, the yards and the cut-throughs
        /// behind the buildings. That is close enough to the same set of places somebody would
        /// pull into, which is a happy accident rather than a design, but it holds up.
        ///
        /// A few tries and then it takes whatever it got. Insisting on an alley near a block
        /// that has none means never moving.
        /// </summary>
        /// <summary>
        /// Somewhere to stand about with the engine off.
        ///
        /// A known spot first, and only if he is near enough that riding to it is a thing he
        /// might have been doing anyway -- a bike sent across two districts to reach a car park
        /// is not hanging about, it is commuting.
        ///
        /// Failing that, found rather than listed. See Green.
        /// </summary>
        private Vector3 Hangout(Vector3 from)
        {
            var best = Vector3.Zero;
            var nearest = HangoutRange;

            foreach (var spot in Hangouts)
            {
                try
                {
                    var d = spot.DistanceTo(from);

                    if (d >= nearest) continue;
                    if (!Ours(spot)) continue;

                    best = spot;
                    nearest = d;
                }
                catch
                {
                    // Next one.
                }
            }

            // Scattered around it, because three bikes handed one coordinate arrive as three
            // bikes trying to stand in the same square metre and the game settles that by
            // shoving them into each other.
            if (best != Vector3.Zero) return Beside(best);

            var green = Green(from);

            // Nothing open nearby. The pavement is still better than standing in the road.
            return green != Vector3.Zero ? green : Pavement(from);
        }

        /// <summary>
        /// Open ground on our turf, found by asking the map instead of by listing coordinates.
        ///
        /// A pavement spot with a road running past it is a pavement. The same spot with no
        /// road within fifteen metres is a green, a yard, a court or the back of a car park --
        /// which is the same set of places people actually stand about in, arrived at without
        /// anybody typing a single coordinate that could turn out to be inside a wall.
        ///
        /// That is the whole reason it is done this way round. A hand-written list of parks is
        /// better scenery and worse code: every entry is a number somebody read off a map, and
        /// the ones that are wrong put a bike through a fence on somebody else's machine.
        /// </summary>
        private Vector3 Green(Vector3 from)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var probe = from.Around(30f + (float)_rng.NextDouble() * 95f);
                    var at = World.GetNextPositionOnSidewalk(probe);

                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    var street = World.GetNextPositionOnStreet(at);
                    if (street != Vector3.Zero && street.DistanceTo(at) < OffRoad) continue;

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
        /// Where to look from: themselves while they are on the blocks, the player once they
        /// are not. See the note in Aim for why looking from where they are stood cannot work.
        /// </summary>
        private Vector3 Anchor(Roll roll)
        {
            try
            {
                var here = roll.Car.Position;
                if (Ours(here)) return here;

                var player = Game.Player.Character;
                if (player != null && player.Exists() && Ours(player.Position)) return player.Position;

                return here;
            }
            catch
            {
                return roll.Car.Position;
            }
        }

        /// <summary>Anywhere at all back on our blocks, for one that has drifted off them.</summary>
        private Vector3 Homeward()
        {
            try
            {
                var player = Game.Player.Character;

                if (player == null || !player.Exists()) return Vector3.Zero;
                if (!Ours(player.Position)) return Vector3.Zero;

                var back = Node(player.Position, false);

                return back != Vector3.Zero ? back : player.Position;
            }
            catch
            {
                return Vector3.Zero;
            }
        }

        /// <summary>Near a spot rather than on it.</summary>
        private Vector3 Beside(Vector3 spot)
        {
            try
            {
                var turn = _rng.NextDouble() * Math.PI * 2d;
                var reach = PackSpread * (0.35f + (float)_rng.NextDouble());

                return new Vector3(spot.X + (float)Math.Cos(turn) * reach,
                                   spot.Y + (float)Math.Sin(turn) * reach,
                                   spot.Z);
            }
            catch
            {
                return spot;
            }
        }

        // ---- the front wheel ---------------------------------------------------

        /// <summary>
        /// Whoever is up on the back wheel, held there.
        ///
        /// RUN EVERY FRAME, from above the tick throttle. The lift is a push applied behind the
        /// centre of mass, and a push is only a wheelie if it keeps coming -- delivered once
        /// every nine hundred milliseconds it is a kerb being hit.
        ///
        /// Started rarely and only on a road at speed. A bike doing walking pace that rears up
        /// on the pavement is being levitated; the same bike doing thirty down Carson is a
        /// rider showing off, which is the thing worth seeing.
        /// </summary>
        private void Wheelies(int now)
        {
            if (!Enabled || _out.Count == 0) return;
            if (_cfg != null && !_cfg.RollerWheelies) return;

            for (var i = 0; i < _out.Count; i++)
            {
                var roll = _out[i];

                if (!roll.OnFoot || roll.Phase == RollPhase.Sitting) continue;

                var bike = roll.Car;
                if (bike == null || !bike.Exists()) continue;

                var rider = roll.Driver;
                if (rider == null || !rider.Exists() || !rider.IsAlive) continue;

                float speed;

                try { speed = bike.Speed; }
                catch { continue; }

                if (now < roll.WheelieUntil)
                {
                    // Comes down on its own when he runs out of road or slows for a junction,
                    // rather than being carried nose-up at walking pace to the end of a timer.
                    if (speed < WheelieNeedsSpeed * 0.6f)
                    {
                        roll.WheelieUntil = 0;
                        continue;
                    }

                    try
                    {
                        Function.Call(Hash.APPLY_FORCE_TO_ENTITY, bike.Handle, 1,
                                      0f, 0f, Lift,
                                      0f, -WheelieBehind, 0f,
                                      0, true, true, true, false, true);
                    }
                    catch
                    {
                        // Next frame.
                    }

                    continue;
                }

                if (now < roll.WheelieAfter || speed < WheelieNeedsSpeed) continue;

                // Whether he goes for one or not, he is not asked again for a while. Rolling
                // the dice every frame at speed would make it a certainty within about two.
                roll.WheelieAfter = now + _rng.Next(WheelieRestMinMs, WheelieRestMaxMs);

                if (_rng.Next(100) >= WheelieChancePercent) continue;

                // On a road, checked only here. It is a native call and this is the one branch
                // that is rare enough to afford it.
                try
                {
                    var street = World.GetNextPositionOnStreet(bike.Position);
                    if (street == Vector3.Zero || street.DistanceTo(bike.Position) > OnRoad) continue;
                }
                catch
                {
                    continue;
                }

                roll.WheelieUntil = now + _rng.Next(WheelieMinMs, WheelieMaxMs);
            }
        }

        private Vector3 Node(Vector3 from, bool wantAlley)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                var probe = from.Around(40f + (float)_rng.NextDouble() * 110f);

                try
                {
                    var id = Function.Call<int>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_ID,
                                                probe.X, probe.Y, probe.Z,
                                                1 + _rng.Next(8), 1, 3f, 0f);

                    if (!Function.Call<bool>(Hash.IS_VEHICLE_NODE_ID_VALID, id)) continue;

                    var got = new OutputArgument();
                    Function.Call(Hash.GET_VEHICLE_NODE_POSITION, id, got);

                    var at = got.GetResult<Vector3>();
                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    var backstreet = !Function.Call<bool>(Hash.GET_VEHICLE_NODE_IS_GPS_ALLOWED, id);

                    // Held out for over the first eight tries, then anything on our turf will do.
                    if (tries < 8 && backstreet != wantAlley) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>Somewhere to aim a bike: a stretch of pavement on our blocks.</summary>
        private Vector3 Pavement(Vector3 from)
        {
            for (var tries = 0; tries < 10; tries++)
            {
                try
                {
                    var probe = from.Around(35f + (float)_rng.NextDouble() * 85f);
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

        private bool OnOurTurf()
        {
            var owner = _turf == null ? null : _turf.Owner;
            return owner != null && string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
        }

        private bool Ours(Vector3 at)
        {
            try
            {
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, at.X, at.Y, at.Z) ?? "";
                var owner = _gangs.OwnerOfZone(code);

                return owner != null && string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ---- putting one out ---------------------------------------------------

        /// <summary>
        /// One of them out on the block, optionally alongside somebody already out there.
        /// </summary>
        /// <returns>The one that went out, so a caller can send his mates after him.</returns>
        private Roll Send(Ped player, bool bike, Roll lead)
        {
            var gang = _gangs.Get(_gangId);
            if (gang == null || gang.MemberModels.Count == 0) return null;

            // A mate comes from where his lead is, not from the far end of the block. The
            // near-spawn rule is relaxed for him on purpose: the thing it protects against is
            // a vehicle appearing in front of you, and one appearing beside a bike you are
            // already watching is worse, so he is put behind the lead and slightly off him.
            var spawn = lead != null && lead.Car != null && lead.Car.Exists()
                ? Beside(lead.Car.Position - lead.Car.ForwardVector * 6f)
                : Somewhere(player, bike);

            if (spawn == Vector3.Zero) return null;

            var roll = new Roll { OnFoot = bike, BornAt = Game.GameTime, Lead = lead };

            try
            {
                roll.Car = Make(bike, spawn, lead == null ? null : lead.Car);
                if (roll.Car == null) return null;

                // Two or four up. A bike is one man, and a two-seater is a man and his mate --
                // asking for four in a coupe gets you two and a warning nobody reads.
                var room = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, roll.Car.Handle);
                // Two up where there is a pillion to sit on. Bicycles have none, so the clamp
                // below quietly puts those back to one man -- which is why this asks for it
                // rather than checking what he is riding.
                var want = bike
                    ? (_rng.Next(100) < 40 ? 1 : 0)
                    : (_rng.Next(2) == 0 ? 1 : 3);
                if (want > room) want = room;

                for (var seat = -1; seat < want; seat++)
                {
                    var man = Fill(gang, roll.Car, seat);
                    if (man == null) continue;

                    if (seat == -1) roll.Driver = man;
                    roll.Crew.Add(man);
                }

                if (roll.Driver == null)
                {
                    Scrap(roll);
                    return null;
                }

                Paint(roll.Car, bike);

                _out.Add(roll);
                Aim(roll, Game.GameTime);

                Log.Info("Rollers: " +
                         (bike ? (lead == null ? "a bike" : "a bike alongside another")
                               : "a car with " + roll.Crew.Count + " up") +
                         " came out on " + (_turf == null ? "the block" : _turf.ZoneName) + ".");

                return roll;
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers could not put one out: " + ex.Message);
                Scrap(roll);
                _out.Remove(roll);
            }

            return null;
        }

        /// <summary>
        /// A spot to come from: on the road, on our turf, and not in your lap.
        ///
        /// The near limit is the whole trick. Everything else about this is ordinary spawning.
        /// </summary>
        private Vector3 Somewhere(Ped player, bool bike)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var away = SpawnNear + (float)_rng.NextDouble() * (SpawnFar - SpawnNear);
                    var probe = player.Position.Around(away);

                    var at = bike
                        ? World.GetNextPositionOnSidewalk(probe)
                        : World.GetNextPositionOnStreet(probe, true);

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
        /// The vehicle itself.
        ///
        /// Every model is checked against this copy of the game before it is asked for, and a
        /// miss simply moves on to the next one. Half of the list is content the Legacy build
        /// never received, so on that install this quietly becomes a shorter list rather than a
        /// stream of failures.
        /// </summary>
        private Vehicle Make(bool bike, Vector3 at, Vehicle matching)
        {
            var wanted = bike ? Bikes : Cars;
            var spares = bike ? SpareBikes : SpareCars;

            var order = new List<string>();

            // THE SAME BIKE AS THE MAN HE IS RIDING WITH, tried first. Three riders on three
            // different frames read as three strangers who happen to be on the same street;
            // three on the same frame read as people who came together, which is the whole
            // thing being built here. It is only a preference -- if that model will not load
            // the ordinary list is right behind it.
            if (matching != null && matching.Exists())
            {
                try
                {
                    var name = matching.Model.Hash;
                    if (Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, name)) order.Add(null);
                }
                catch
                {
                    // The list below is a complete answer on its own.
                }
            }

            for (var i = 0; i < 8; i++) order.Add(wanted[_rng.Next(wanted.Length)]);
            order.AddRange(spares);

            foreach (var name in order)
            {
                try
                {
                    // The null placeholder is the lead's own model, carried by reference rather
                    // than by name because a hash is what we have and a hash is what Model takes.
                    var model = name == null ? new Model(matching.Model.Hash) : new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var car = World.CreateVehicle(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (car == null || !car.Exists()) continue;

                    car.IsPersistent = true;

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                    return car;
                }
                catch (Exception ex)
                {
                    Log.Debug("Rollers could not make a " + name + ": " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Dark green, the whole set of them.
        ///
        /// The colour is a paint index rather than an RGB triple, for the same reason the cars
        /// parked on the block are: the table has the metal flake in it and a raw green comes
        /// out flat. The radio is on and loud in the cars because a car going past with nothing
        /// coming out of it is a car nobody is in.
        /// </summary>
        private void Paint(Vehicle car, bool bike)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);

                // Nothing over the top of it.
                Function.Call(Hash.SET_VEHICLE_LIVERY, car.Handle, -1);
                Function.Call(Hash.SET_VEHICLE_MOD, car.Handle, 48, -1, false);

                // The set's own index rather than a second copy of the number. This file and
                // gangs.json both said 49 and only one of them could be the place to change it.
                var gang = _gangs == null ? null : _gangs.Get(_gangId);
                var paint = gang != null && gang.Paint >= 0 ? gang.Paint : DarkGreen;

                // PRIMARY DARK, PEARL BRIGHT, and the gap between them is the whole effect.
                //
                // It used to set the pearl to the same index as the body, which is the same as
                // having no pearl at all -- a flake that matches the paint it is suspended in
                // does not catch anything. A lighter green over the dark one is what makes it
                // shift as the car turns under a street light, which is what anybody means by
                // a pearlescent green.
                //
                // The pearl is in the ini because the colour table is not documented anywhere
                // we can check and the difference between "green" and the wrong green is one
                // number. The body is not: that is the set's own colour and it belongs to the
                // set, not to a taste.
                var pearl = _cfg == null ? DefaultPearl : _cfg.RollerPearl;

                Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, paint, paint);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, car.Handle, pearl, 0);

                if (bike) return;

                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, 1);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, car.Handle, 1f);

                Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, car.Handle, true);
                Function.Call(Hash.SET_VEH_RADIO_STATION, car.Handle, Core.Radio.Blonded);
                Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, car.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not paint a roller: " + ex.Message);
            }
        }

        /// <summary>One of them, in one seat.</summary>
        private Ped Fill(GangDef gang, Vehicle car, int seat)
        {
            try
            {
                var name = gang.MemberModels[_rng.Next(gang.MemberModels.Count)];

                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) return null;

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, car.Handle,
                                                PedTypeCiv, model.Hash, seat, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return null;

                var man = Entity.FromHandle(handle) as Ped;
                if (man == null || !man.Exists()) return null;

                man.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, man.Handle, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, man.Handle, gang.GroupHash);

                // Not told to ignore the world. They are ours and they are on our blocks, so a
                // set that drove past a fight without looking at it would be worse than one that
                // gets out of the car -- and the stuck watchdog covers the mess either way.
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, man.Handle, false);

                // Not on a bike, and not on the bikes they are about to be put on. Set at the
                // seat rather than after, because CREATE_PED_INSIDE_VEHICLE has already sat him
                // down by the time this line runs and the lid goes on with the seat.
                Core.Helmets.Off(man);

                return man;
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers could not fill a seat: " + ex.Message);
                return null;
            }
        }

        // ---- clearing up -------------------------------------------------------

        private void Prune(int now)
        {
            var player = Game.Player.Character;
            var mine = player != null && player.Exists() && player.CurrentVehicle != null
                       && player.CurrentVehicle.Exists()
                ? player.CurrentVehicle.Handle
                : 0;

            for (var i = _out.Count - 1; i >= 0; i--)
            {
                var roll = _out[i];

                var gone = roll.Car == null || !roll.Car.Exists();
                var noDriver = roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive;

                if (gone || noDriver)
                {
                    Release(roll);
                    _out.RemoveAt(i);
                    continue;
                }

                // Taken off us: the player got in it, or somebody else is behind the wheel. It
                // stops being ours the moment either happens.
                var driver = roll.Car.Driver;
                var taken = driver == null || !driver.Exists()
                            || driver.Handle != roll.Driver.Handle
                            || (mine != 0 && roll.Car.Handle == mine);

                var old = now - roll.BornAt > LifetimeMs;

                var far = player != null && player.Exists()
                          && roll.Car.Position.DistanceTo(player.Position) > LetGoRange;

                if (taken || old || far)
                {
                    Release(roll);
                    _out.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Handed back to the game rather than deleted.
        ///
        /// The game clears up its own traffic when nobody is looking, and it is better at
        /// choosing the moment than a distance check is. Deleting outright is for teardown,
        /// where there is no later moment to wait for.
        /// </summary>
        private void Release(Roll roll)
        {
            try
            {
                foreach (var man in roll.Crew)
                {
                    if (man == null || !man.Exists()) continue;

                    man.IsPersistent = false;
                    man.MarkAsNoLongerNeeded();
                }

                if (roll.Car != null && roll.Car.Exists())
                {
                    roll.Car.IsPersistent = false;
                    roll.Car.MarkAsNoLongerNeeded();
                }
            }
            catch
            {
                // Letting go of something already gone.
            }
        }

        private void Scrap(Roll roll)
        {
            try
            {
                foreach (var man in roll.Crew)
                {
                    if (man != null && man.Exists()) man.Delete();
                }

                if (roll.Car != null && roll.Car.Exists()) roll.Car.Delete();
            }
            catch
            {
                // Nothing left to scrap.
            }
        }

        /// <summary>Everything off the street, for a reload.</summary>
        public void RestoreWorld()
        {
            foreach (var roll in _out) Scrap(roll);
            _out.Clear();
        }
    }
}
