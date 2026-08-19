using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
using Hoodrich.State;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Gangs
{
    /// <summary>Somewhere of ours worth attacking, and the man who stands there.</summary>
    internal sealed class WarTarget
    {
        public string Who = "";
        public Vector3 Where;
    }

    /// <summary>
    /// The other sets coming to us.
    ///
    /// Every fight in the mod so far has been one you went and started. This is the one that
    /// comes to you, at a place you care about, whether or not you were doing anything -- and
    /// the only decision it asks for is whether you turn up.
    ///
    /// Deliberately NOT a wave arena. Carloads arrive over five minutes at irregular intervals,
    /// one or two at a time, so it reads as people driving over rather than a spawner emptying
    /// itself. Ours are there too, in matched numbers, because a block that needs one man to
    /// save it is a block with nobody living on it. You are the difference at the margin, not
    /// the entire defence -- which is the honest shape of it, and it also means turning up late
    /// still matters.
    /// </summary>
    internal sealed class GangWar
    {
        // ---- shape ------------------------------------------------------------

        /// <summary>
        /// How long it runs before the last of them break off, by how bad it is between you.
        ///
        /// A set with a small grievance sends a car and is gone in two minutes. A set at war
        /// sends two and stays five. A set that hates you sends three and is still coming after
        /// eight, which is long enough to run out of ammunition on your own block.
        /// </summary>
        private const int BeefMs = 120000;
        private const int WarMs = 300000;
        private const int HatredMs = 480000;

        /// <summary>Where one becomes the other.</summary>
        private const float WarHeat = 0.35f;
        private const float HatredHeat = 0.70f;

        /// <summary>Gap between carloads. Irregular on purpose.</summary>
        /// <summary>
        /// Gap between carloads.
        /// 
        /// Short enough that the next lot is on the block before the last is finished, which is
        /// what keeps it a fight rather than four separate skirmishes with quiet in between.
        /// Still irregular, so it never sounds like a metronome.
        /// </summary>
        private const int WaveGapMinMs = 22000;
        private const int WaveGapMaxMs = 42000;

        /// <summary>
        /// Two turn up together at the start; after that it is usually one.
        ///
        /// That is the floor, for a set that barely knows you. Everything below scales up from
        /// here with how badly they want you -- see Heat.
        /// </summary>
        private const int OpeningCars = 2;
        private const float DoubleCarChance = 0.3f;

        /// <summary>Extra cars in the opening wave when they hate you outright.</summary>
        private const int OpeningCarsAtWorst = 2;

        /// <summary>Added to the odds of two arriving together, at worst.</summary>
        private const float DoubleCarChanceAtWorst = 0.5f;

        /// <summary>
        /// Where ours stand when a given man is the one being hit.
        ///
        /// Exact coordinates, all three axes, used verbatim -- two of these are up on walkways
        /// and a ground probe would drop everybody standing on them into the courtyard below.
        /// Read off the HUD standing on each spot.
        /// </summary>
        private static readonly Dictionary<string, Vector3> Musters =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
            {
                { "Grimes",  new Vector3(-134.105f, -1472.521f, 36.192f) },
                { "Stretch", new Vector3(-161.651f, -1637.566f, 37.246f) },
                { "Lamar",   new Vector3(-99.960f,  -1632.762f, 32.106f) },
            };

        private const int PerCar = 4;

        /// <summary>Ours, spawned to match. Four a car, same as theirs.</summary>
        private const int DefendersPerCar = 4;

        /// <summary>Where they come in from, and where they aim for.</summary>
        private const float ApproachDistance = 150f;
        private const float DropRange = 32f;

        /// <summary>Close enough to count as having turned up.</summary>
        private const float DefendRange = 70f;

        /// <summary>
        /// How often the war is even considered, and how likely it is when it is.
        ///
        /// Tuned for about one an hour of play. A raid on your own block is only an event while
        /// it is rare -- three of them in twenty minutes and it stops being a raid and becomes
        /// the weather.
        /// </summary>
        private const int RollIntervalMs = 600000;
        private const float WarChance = 0.08f;

        /// <summary>
        /// Added to the odds when the worst of them hates you outright.
        ///
        /// Somebody you have not crossed is a once-in-a-session event. Somebody whose people you
        /// have been putting down all week is coming, and coming soon, and the difference
        /// between those two is the entire point of keeping standings at all.
        /// </summary>
        private const float WarChanceAtWorst = 0.42f;

        /// <summary>Nothing starts within this of the last one ending.</summary>
        private const int CalmMs = 1800000;

        /// <summary>How little quiet you get when they hate you. Half an hour becomes eight minutes.</summary>
        private const int CalmAtWorstMs = 480000;

        /// <summary>
        /// How far standing has to fall before it counts as all the way bad.
        ///
        /// Rep with a rival starts at zero and drops five every time you drop one of theirs, so
        /// this is roughly a dozen of their people. It bottoms out at -100 either way.
        /// </summary>
        private const float WorstRep = -60f;

        /// <summary>Stars handed over once it is finished, and not a moment before.</summary>
        private const int StarsAfter = 2;

        private const int PedTypeCiv = 4;
        private const int UpdateIntervalMs = 700;

        /// <summary>
        /// What both sides bring.
        ///
        /// The same list for everybody, because this is one fight and a side that turns up with
        /// worse guns is a side that was always going to lose -- which makes turning up
        /// pointless rather than decisive.
        /// </summary>
        private static readonly string[] WarWeapons =
        {
            "WEAPON_COMPACTRIFLE", "WEAPON_APPISTOL", "WEAPON_MACHINEPISTOL",
            "WEAPON_MICROSMG", "WEAPON_MINISMG", "WEAPON_ASSAULTSMG"
        };

        // ---- state ------------------------------------------------------------

        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;
        private readonly Random _rng = new Random();

        private readonly List<WarTarget> _targets = new List<WarTarget>();

        private readonly List<Ped> _rivals = new List<Ped>();
        private readonly List<Ped> _defenders = new List<Ped>();
        private readonly List<Vehicle> _cars = new List<Vehicle>();
        private readonly List<Blip> _blips = new List<Blip>();

        private GangDef _attacker;
        private WarTarget _target;
        private Blip _marker;

        private int _lastUpdate;
        private int _nextRoll;
        private int _startedAt;
        private int _nextWave;
        private int _kills;
        private bool _showedUp;

        public GangWar(GangRegistry gangs, Affiliation crew, PlayerState state)
        {
            _gangs = gangs;
            _crew = crew;
            _state = state;
        }

        /// <summary>Set by Main. Null-checked, so the feed is never load-bearing.</summary>
        public SocialFeed Social;

        public bool IsRunning { get; private set; }

        public GangWar Defend(string who, Vector3 where)
        {
            _targets.Add(new WarTarget { Who = who, Where = where });
            return this;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                if (IsRunning) End(false, "You went down.");
                return;
            }

            if (IsRunning) { Tick(player, now); return; }

            if (now < _nextRoll) return;
            _nextRoll = now + RollIntervalMs;

            // Only somebody who runs with a set has a set worth attacking.
            if (!_crew.IsAffiliated) return;
            if (_targets.Count == 0) return;

            // How likely this is at all is set by whoever currently hates you most.
            if (_rng.NextDouble() > WarChance + WorstHeat() * WarChanceAtWorst) return;

            Begin();
        }

        private void Begin()
        {
            _attacker = PickAttacker();
            if (_attacker == null) return;

            _target = _targets[_rng.Next(_targets.Count)];

            _heat = Heat(_attacker);

            // One number decides the whole shape of it.
            if (_heat >= HatredHeat)      { _cars0 = 3; _warMs = HatredMs; }
            else if (_heat >= WarHeat)    { _cars0 = 2; _warMs = WarMs; }
            else                          { _cars0 = 1; _warMs = BeefMs; }

            _startedAt = Game.GameTime;
            _nextWave = 0;
            _kills = 0;
            _showedUp = false;
            IsRunning = true;

            Mark();
            HoldTheLaw(true);

            // Before a single ped exists. A ped works out who it hates when it is created and
            // tasked, so setting this afterwards leaves the opening wave standing about.
            SetWarRelationships(true);

            SpawnDefenders(DefendersPerCar * _cars0);

            Notify.Important("~r~" + _attacker.Name + " rolling up on " + _target.Who + ".~s~ " +
                             (_heat > 0.6f ? "Deep this time. Get over there."
                                           : "Get over there."));

            Log.Info("Gang war: " + _attacker.Id + " attacking " + _target.Who +
                     " (heat " + _heat.ToString("0.00") + ", " + _cars0 + " cars opening, " +
                     (_warMs / 1000) + "s).");

            // Held until somebody actually fires. Cars pulling up is not news, and narrating
            // the drive over defeats the arrival.
            if (Social != null)
            {
                Social.HoldUntilShots = true;
                Social.On(SocialEvent.WarStarted, _attacker.Name);
            }

            SendWave(_cars0);
        }

        /// <summary>How badly this particular lot want you, worked out when it kicks off.</summary>
        private float _heat;

        /// <summary>How many turned up in the opening wave.</summary>
        private int _cars0 = OpeningCars;

        /// <summary>How long this one runs for.</summary>
        private int _warMs = WarMs;

        /// <summary>
        /// Switches the police off for the length of the raid, and back on when it ends.
        ///
        /// Two sets shooting at each other on a residential street would normally bring every
        /// unit in the division inside a minute, and then the fight you were asked to turn up
        /// for is a five-star chase you cannot win. The block settles this one itself; the law
        /// arrives afterwards, the way it always does.
        ///
        /// Restored FIRST in teardown, because leaving the player permanently un-arrestable is
        /// far worse than any amount of litter.
        /// </summary>
        private void HoldTheLaw(bool held)
        {
            try
            {
                if (held)
                {
                    Game.Player.Wanted.SetWantedLevel(0, false);
                    Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
                }

                Function.Call(Hash.SET_MAX_WANTED_LEVEL, held ? 0 : 5);
                Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, held);
                Function.Call(Hash.SET_CREATE_RANDOM_COPS, !held);

                Log.Info(held ? "Gang war: the law is off until it is over." : "Gang war: the law is back on.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not change the wanted rules: " + ex.Message);
            }
        }

        /// <summary>
        /// Makes the two sets hate each other for the length of the raid.
        ///
        /// This was the whole problem. Every ped was given the combat attributes, the alertness
        /// and the standing order to fight hated targets around them -- and then found that
        /// nobody nearby was hated, because the ambient gang relationship groups do not hate
        /// each other by default. Thirty men stood in a street holding rifles, technically at
        /// war, waiting for an enemy the game would not let them see.
        ///
        /// The previous relationship is read back first and restored on the way out, so a raid
        /// does not permanently rewrite how those two sets treat each other everywhere else in
        /// the game.
        /// </summary>
        private void SetWarRelationships(bool on)
        {
            var mine = _crew.Current;

            if (mine == null || _attacker == null) return;
            if (mine.GroupHash == 0 || _attacker.GroupHash == 0) return;

            try
            {
                if (on)
                {
                    _wasTheirs = Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_GROUPS,
                                                    _attacker.GroupHash, mine.GroupHash);
                    _wasOurs = Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_GROUPS,
                                                  mine.GroupHash, _attacker.GroupHash);
                    _wasOnUs = Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_GROUPS,
                                                  _attacker.GroupHash, PlayerGroup);

                    Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelHate,
                                  _attacker.GroupHash, mine.GroupHash);
                    Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelHate,
                                  mine.GroupHash, _attacker.GroupHash);

                    // They came for the set you run with, so they came for you as well.
                    Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelHate,
                                  _attacker.GroupHash, PlayerGroup);

                    Log.Info("Gang war: " + _attacker.Id + " and " + mine.Id + " now hate each other.");
                    return;
                }

                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, _wasTheirs,
                              _attacker.GroupHash, mine.GroupHash);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, _wasOurs,
                              mine.GroupHash, _attacker.GroupHash);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, _wasOnUs,
                              _attacker.GroupHash, PlayerGroup);

                Log.Info("Gang war: relationships put back the way they were.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not set the war relationships: " + ex.Message);
            }
        }

        /// <summary>SET_RELATIONSHIP_BETWEEN_GROUPS intensity for hate.</summary>
        private const int RelHate = 5;

        private static int PlayerGroup
        {
            get { return Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER"); }
        }

        private int _wasTheirs = 4;
        private int _wasOurs = 4;
        private int _wasOnUs = 3;

        /// <summary>
        /// How badly one set wants you, from nothing at all to all the way.
        ///
        /// Standing with a rival is a single number that only ever goes down -- five off every
        /// time you put one of their people on the floor. This turns it into the shape of what
        /// comes back: how often, how many, and how little peace you get in between.
        /// </summary>
        private float Heat(GangDef gang)
        {
            if (gang == null) return 0f;

            var rep = _crew.StandingFor(gang.Id).Rep;
            if (rep >= 0f) return 0f;

            var heat = rep / WorstRep;
            return heat > 1f ? 1f : heat;
        }

        /// <summary>The worst of them. What the odds of anything happening at all are set by.</summary>
        private float WorstHeat()
        {
            var worst = 0f;

            foreach (var gang in Rivals())
            {
                var heat = Heat(gang);
                if (heat > worst) worst = heat;
            }

            return worst;
        }

        /// <summary>Everybody your set is at odds with who has people to send.</summary>
        private IEnumerable<GangDef> Rivals()
        {
            var mine = _crew.Current;
            if (mine == null) yield break;

            foreach (var gang in _gangs.All)
            {
                if (gang.Id == mine.Id) continue;
                if (!mine.IsRivalOf(gang.Id) && !gang.IsRivalOf(mine.Id)) continue;
                if (gang.MemberModels.Count == 0) continue;

                yield return gang;
            }
        }

        /// <summary>
        /// Whose turn it is.
        ///
        /// Weighted, not drawn from a hat. Everybody your set is at odds with is in with a
        /// chance, but the one you have taken the most from is several times more likely to be
        /// the one at the end of the street -- which is how it should read, because they are the
        /// ones with something to come back for.
        /// </summary>
        private GangDef PickAttacker()
        {
            var options = new List<GangDef>();
            var weights = new List<float>();
            var total = 0f;

            foreach (var gang in Rivals())
            {
                // A baseline so somebody you have never touched can still decide today is the
                // day, and heat on top so somebody you have been at war with usually is.
                var weight = 0.35f + Heat(gang) * 3f;

                options.Add(gang);
                weights.Add(weight);
                total += weight;
            }

            if (options.Count == 0) return null;

            var roll = (float)_rng.NextDouble() * total;

            for (var i = 0; i < options.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f) return options[i];
            }

            return options[options.Count - 1];
        }

        private void Tick(Ped player, int now)
        {
            var elapsed = now - _startedAt;
            var here = player.Position.DistanceTo(_target.Where) <= DefendRange;

            if (here && !_showedUp)
            {
                _showedUp = true;
                Notify.Ticker("~g~You showed up.~s~ Hold the block.");
            }

            CountKills();
            ListenForShots(player);
            PushIn(now);

            // More of them, until the clock runs out.
            if (elapsed < _warMs - WaveGapMinMs && now >= _nextWave)
            {
                var twoUp = _rng.NextDouble() < DoubleCarChance + _heat * DoubleCarChanceAtWorst;
                SendWave(twoUp ? 2 : 1);
            }

            if (elapsed < _warMs) return;

            // Time. Anybody still standing decides they have made their point.
            End(_showedUp && _kills > 0, null);
        }

        private void SendWave(int cars)
        {
            // Bad blood means less breathing room between carloads as well as more of them.
            var gap = WaveGapMinMs + _rng.Next(WaveGapMaxMs - WaveGapMinMs);
            gap = (int)(gap * (1f - _heat * 0.45f));

            _nextWave = Game.GameTime + gap;

            for (var i = 0; i < cars; i++) SendCar();

            // Ours turn out again every time theirs do, so the fight stays even for the whole
            // five minutes rather than being decided by the first thirty seconds.
            SpawnDefenders(DefendersPerCar * cars);
        }

        /// <summary>
        /// One carload, driven in from a few streets out.
        ///
        /// Their own car, in their own colour, because a Balla raid arriving in a taxi is not a
        /// raid. Started well away and driven in for the same reason the police are: a car that
        /// simply exists beside you reads as a spawn, and one that comes round the corner reads
        /// as people who decided to come.
        /// </summary>
        private void SendCar()
        {
            if (_attacker == null || _target == null) return;

            var model = PickCar(_attacker);
            if (model == null) return;

            try
            {
                var angle = _rng.NextDouble() * Math.PI * 2.0;

                var far = _target.Where + new Vector3(
                    (float)Math.Cos(angle) * ApproachDistance,
                    (float)Math.Sin(angle) * ApproachDistance, 0f);

                var start = World.GetNextPositionOnStreet(far);
                if (start == Vector3.Zero) return;

                var car = World.CreateVehicle(model.Value, start);
                model.Value.MarkAsNoLongerNeeded();

                if (car == null || !car.Exists()) return;

                car.IsPersistent = true;
                car.IsEngineRunning = true;

                Paint(car, _attacker);
                _cars.Add(car);

                Ped driver = null;

                for (var seat = -1; seat < PerCar - 1; seat++)
                {
                    var ped = SpawnRival(car, seat);
                    if (ped == null) continue;

                    if (seat == -1) driver = ped;
                    _rivals.Add(ped);
                }

                if (driver != null)
                {
                    var drop = World.GetNextPositionOnStreet(_target.Where.Around(DropRange));
                    if (drop == Vector3.Zero) drop = _target.Where;

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                                  drop.X, drop.Y, drop.Z, 22f, 0, car.Model.Hash, 786603, 8f, true);
                }

                Log.Info("Gang war: a carload of " + _attacker.Id + " on the way in.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a carload: " + ex.Message);
            }
        }

        /// <summary>
        /// A car they have not turned up in yet.
        ///
        /// Five identical Ballers arriving one after another reads as a spawner rather than as
        /// people. The colour stays the same -- that is the set -- but the cars do not, and the
        /// list only resets once they have been through all of it.
        /// </summary>
        private Model? PickCar(GangDef gang)
        {
            var all = new List<string>(CarsFor(gang.Id));

            var fresh = new List<string>();
            foreach (var name in all)
            {
                if (!_usedCars.Contains(name)) fresh.Add(name);
            }

            if (fresh.Count == 0)
            {
                _usedCars.Clear();
                fresh = all;
            }

            // Shuffled, so the order is not the order they were written in.
            for (var i = fresh.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var tmp = fresh[i]; fresh[i] = fresh[j]; fresh[j] = tmp;
            }

            foreach (var name in fresh)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _usedCars.Add(name);
                    return model;
                }
                catch
                {
                    // Try the next.
                }
            }

            return null;
        }

        private readonly HashSet<string> _usedCars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The cars each set actually drives.</summary>
        private static IEnumerable<string> CarsFor(string gangId)
        {
            switch (gangId)
            {
                case "ballas":
                    yield return "baller"; yield return "buccaneer2"; yield return "peyote";
                    yield return "manana"; yield return "primo2"; yield return "tornado";
                    yield return "voodoo"; yield return "faction"; yield return "emperor";
                    break;

                case "vagos":
                    yield return "tornado"; yield return "chino2"; yield return "voodoo";
                    yield return "buccaneer"; yield return "primo"; yield return "faction2";
                    yield return "virgo"; yield return "manana"; yield return "peyote";
                    break;

                case "marabunta":
                    yield return "virgo3"; yield return "tornado4"; yield return "primo";
                    break;

                default:
                    yield return "buccaneer"; yield return "manana"; yield return "primo";
                    break;
            }
        }

        /// <summary>Their colour, so you know who it is before anybody gets out.</summary>
        private static void Paint(Vehicle car, GangDef gang)
        {
            try
            {
                var c = gang.Colour;

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);
                Function.Call(Hash.SET_VEHICLE_CUSTOM_PRIMARY_COLOUR, car.Handle, (int)c.R, (int)c.G, (int)c.B);
                Function.Call(Hash.SET_VEHICLE_CUSTOM_SECONDARY_COLOUR, car.Handle, (int)c.R, (int)c.G, (int)c.B);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, 1);
            }
            catch
            {
                // A car in the wrong colour is still a car.
            }
        }

        private Ped SpawnRival(Vehicle car, int seat)
        {
            foreach (var name in _attacker.MemberModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                                    car.Handle, PedTypeCiv, model.Hash, seat, true, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;

                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, _attacker.GroupHash);
                    Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 22);

                    // The combat attributes that make somebody actually fight rather than stand
                    // in a street holding a rifle. 46 is "always fight", 5 is "leave the car to
                    // do it", 3 is "chase on foot", 1 is "use cover". Without these they arrive,
                    // get out, and look at each other -- which is exactly what happened.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 3, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 1, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 2, true);

                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2);
                    Function.Call(Hash.SET_PED_COMBAT_RANGE, ped.Handle, 2);
                    Function.Call(Hash.SET_PED_ALERTNESS, ped.Handle, 3);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    // They came for the block, so that is where they go, and they fight anybody
                    // in the way on their own initiative rather than waiting to be told.
                    Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, ped.Handle, 120f, 0);

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY,
                                                      WarWeapons[_rng.Next(WarWeapons.Length)]),
                                  200, false, true);

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Red;
                        blip.Scale = 0.65f;
                        blip.Name = _attacker.Name;
                        _blips.Add(blip);
                    }

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        /// <summary>
        /// Ours, already there.
        ///
        /// Matched to what is coming, because the block defending itself is the point -- you
        /// are the difference at the margin rather than the entire defence. It also means
        /// turning up two minutes late still matters, which a one-man last stand would not.
        /// </summary>
        private void SpawnDefenders(int count)
        {
            var mine = _crew.Current;
            if (mine == null) return;

            // Everybody comes out of the same doorway -- the muster point for whoever is being
            // hit -- and then wanders, so they spread across the block and find the fight
            // themselves rather than being placed in a firing line.
            var muster = _target != null && Musters.ContainsKey(_target.Who)
                ? Musters[_target.Who]
                : _target.Where;

            for (var i = 0; i < count; i++)
            {
                foreach (var name in mine.MemberModels)
                {
                    try
                    {
                        var model = new Model(name);
                        if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                        // On the mark itself, height and all. These spots are on walkways and
                        // stairs, so the authored Z is the whole point of them.
                        var at = muster;

                        var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                        at.X, at.Y, at.Z, 0f, false, false);

                        model.MarkAsNoLongerNeeded();
                        if (handle == 0) continue;

                        var ped = Entity.FromHandle(handle) as Ped;
                        if (ped == null || !ped.Exists()) continue;

                        ped.IsPersistent = true;

                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, mine.GroupHash);
                        Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 25);

                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 3, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 1, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 2, true);

                        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2);
                        Function.Call(Hash.SET_PED_ALERTNESS, ped.Handle, 3);
                        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                        // Wander, and nothing else. The combat task used to be issued on the
                        // very next line, which immediately replaced the wander and then ended
                        // by itself a moment later with nothing hated in range -- so instead of
                        // walking the block they stood exactly where they were put, forever.
                        //
                        // They are given somebody to fight by Order(), the moment one of theirs
                        // comes within sight of them. Until then this is a block, not a firing
                        // line, and it should look like one.
                        Function.Call(Hash.TASK_WANDER_IN_AREA, ped.Handle,
                                      muster.X, muster.Y, muster.Z, 45f, 3f, 10f);

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY,
                                                          WarWeapons[_rng.Next(WarWeapons.Length)]),
                                      200, false, true);

                        var blip = ped.AddBlip();
                        if (blip != null && blip.Exists())
                        {
                            blip.Color = BlipColor.Green;
                            blip.Scale = 0.55f;
                            blip.Name = mine.Name;
                            _blips.Add(blip);
                        }

                        _defenders.Add(ped);
                        break;
                    }
                    catch
                    {
                        // Try the next model.
                    }
                }
            }

            Log.Info("Gang war: " + _defenders.Count + " of ours holding " + _target.Who + ".");
        }

        /// <summary>
        /// Gives everybody on both sides something to be doing, and names who.
        ///
        /// Three separate things were stopping the raid from being a raid. Everybody was told to
        /// get out of the car on the first tick, a hundred and fifty metres out, so a carload
        /// arrived on foot as stragglers. Everybody was re-issued a nav task every six seconds,
        /// which restarts the path from scratch, so nobody ever finished walking anywhere. And
        /// nobody was ever given a NAME to fight -- "engage hated targets around you" is a
        /// one-shot task that ends the moment there is nothing hated in range, which, before the
        /// relationship fix above, there never was.
        ///
        /// So: they ride in until the car is on the block, then they get out. On foot, if there
        /// is somebody to fight within sight they are told about that specific person, by
        /// handle, and left alone until that person goes down. Only somebody with nobody to
        /// fight gets walked toward the block, and only every ten seconds.
        /// </summary>
        private void PushIn(int now)
        {
            if (now < _nextPush) return;
            _nextPush = now + PushIntervalMs;

            var player = Game.Player.Character;

            for (var i = 0; i < _rivals.Count; i++) Order(_rivals[i], true, player, now);
            for (var i = 0; i < _defenders.Count; i++) Order(_defenders[i], false, player, now);
        }

        /// <summary>One person, one order.</summary>
        private void Order(Ped ped, bool theirs, Ped player, int now)
        {
            if (ped == null || !ped.Exists() || !ped.IsAlive) return;
            if (_target == null) return;

            WarOrder order;
            if (!_orders.TryGetValue(ped.Handle, out order))
            {
                order = new WarOrder();
                _orders[ped.Handle] = order;
            }

            if (now < order.NextThink) return;
            order.NextThink = now + ThinkMs;

            try
            {
                // Still riding in. The driver is left to drive: they get out when the car is on
                // the block, not the moment they spawn three streets away.
                if (ped.IsInVehicle())
                {
                    var car = ped.CurrentVehicle;
                    var arrived = car == null || !car.Exists() ||
                                  car.Position.DistanceTo(_target.Where) <= DismountRange;

                    if (!arrived) return;

                    Function.Call(Hash.TASK_LEAVE_ANY_VEHICLE, ped.Handle, 0, 0);
                    order.Target = 0;
                    return;
                }

                var foe = NearestFoe(ped, theirs, player);

                if (foe != null)
                {
                    // Already on this one and it is still standing, so leave them to it.
                    if (order.Target == foe.Handle) return;

                    order.Target = foe.Handle;
                    order.NextWalk = 0;
                    order.Wandering = false;

                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, foe.Handle, 0, 16);
                    return;
                }

                var wasFighting = order.Target != 0;
                order.Target = 0;

                // Ours, with nobody left in front of them, go back to walking their own block
                // rather than marching on the middle of it. Issued once, on the way out of a
                // fight, and then left alone -- a wander task re-sent every few seconds is a
                // man taking one step and starting again.
                if (!theirs)
                {
                    var muster = _target != null && Musters.ContainsKey(_target.Who)
                        ? Musters[_target.Who]
                        : _target.Where;

                    if (!wasFighting && order.Wandering) return;
                    if (now < order.NextWalk) return;

                    order.NextWalk = now + WalkIntervalMs;
                    order.Wandering = true;

                    Function.Call(Hash.TASK_WANDER_IN_AREA, ped.Handle,
                                  muster.X, muster.Y, muster.Z, 45f, 3f, 10f);
                    return;
                }

                // Theirs came for a specific place, so with nobody in front of them they walk
                // in at it on foot. The car got them to the street; the last stretch is theirs.
                if (now < order.NextWalk) return;
                order.NextWalk = now + WalkIntervalMs;

                if (ped.Position.DistanceTo(_target.Where) <= 10f) return;

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                              _target.Where.X, _target.Where.Y, _target.Where.Z,
                              2.0f, 30000, 4f, 0, 0f);
            }
            catch
            {
                // They will find it or they will not.
            }
        }

        /// <summary>
        /// The closest person this one came here to fight.
        ///
        /// Theirs look for ours and for you; ours look for theirs. Handed over by handle, so the
        /// combat task gets a name rather than a hope.
        /// </summary>
        private Ped NearestFoe(Ped ped, bool theirs, Ped player)
        {
            Ped best = null;
            var bestDist = EngageRange;

            var pool = theirs ? _defenders : _rivals;

            for (var i = 0; i < pool.Count; i++)
            {
                var other = pool[i];
                if (other == null || !other.Exists() || !other.IsAlive) continue;

                var d = ped.Position.DistanceTo(other.Position);
                if (d >= bestDist) continue;

                bestDist = d;
                best = other;
            }

            // As far as they are concerned you are one of ours.
            if (theirs && player != null && player.Exists() && player.IsAlive)
            {
                var d = ped.Position.DistanceTo(player.Position);
                if (d < bestDist) best = player;
            }

            return best;
        }

        private sealed class WarOrder
        {
            public int NextThink;
            public int NextWalk;
            public int Target;

            /// <summary>Ours, currently walking their own block rather than fighting.</summary>
            public bool Wandering;
        }

        private readonly Dictionary<int, WarOrder> _orders = new Dictionary<int, WarOrder>();

        private int _nextPush;

        /// <summary>How often the whole street is looked at.</summary>
        private const int PushIntervalMs = 1500;

        /// <summary>How often any one person is given a new order.</summary>
        private const int ThinkMs = 2500;

        /// <summary>How often somebody with nobody to fight is re-pointed at the block.</summary>
        private const int WalkIntervalMs = 10000;

        /// <summary>How far off somebody will be picked as a target.</summary>
        private const float EngageRange = 90f;

        /// <summary>How close the car gets to the block before they get out of it.</summary>
        private const float DismountRange = 55f;

        /// <summary>
        /// Lets the feed start the moment the first round goes off.
        ///
        /// Checked on everybody, not just the player: the first shot is as likely to be one of
        /// theirs, and the post that matters is "shots on the block" rather than "the player has
        /// opened fire".
        /// </summary>
        private void ListenForShots(Ped player)
        {
            if (Social == null || !Social.HoldUntilShots) return;

            try
            {
                if (Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle))
                {
                    Social.HoldUntilShots = false;
                    return;
                }

                foreach (var ped in _rivals)
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (!Function.Call<bool>(Hash.IS_PED_SHOOTING, ped.Handle)) continue;

                    Social.HoldUntilShots = false;
                    return;
                }

                foreach (var ped in _defenders)
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (!Function.Call<bool>(Hash.IS_PED_SHOOTING, ped.Handle)) continue;

                    Social.HoldUntilShots = false;
                    return;
                }
            }
            catch
            {
                // If we cannot tell, let it talk.
                Social.HoldUntilShots = false;
            }
        }

        private void CountKills()
        {
            for (var i = _rivals.Count - 1; i >= 0; i--)
            {
                var ped = _rivals[i];
                if (ped != null && ped.Exists() && ped.IsAlive) continue;

                var byYou = ped != null && ped.Exists() &&
                            Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY,
                                                ped.Handle, Game.Player.Character.Handle, true);

                if (byYou) _kills++;

                try { if (ped != null && ped.Exists()) ped.MarkAsNoLongerNeeded(); }
                catch { /* teardown */ }

                _rivals.RemoveAt(i);
            }
        }

        private void Mark()
        {
            ClearMarker();

            try
            {
                _marker = World.CreateBlip(_target.Where, DefendRange);
                if (_marker == null || !_marker.Exists()) return;

                _marker.Color = BlipColor.Red;
                _marker.Alpha = 110;
                _marker.ShowRoute = true;
                _marker.Name = _target.Who + " under attack";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the war: " + ex.Message);
            }
        }

        private void ClearMarker()
        {
            try { if (_marker != null && _marker.Exists()) _marker.Delete(); }
            catch { /* teardown */ }

            _marker = null;
        }

        // ---- finishing ---------------------------------------------------------

        private void End(bool held, string reason)
        {
            var attacker = _attacker;
            var kills = _kills;
            var showed = _showedUp;

            // Before anything else. A cleanup that throws must not leave the player unable to
            // attract police for the rest of the session.
            HoldTheLaw(false);
            SetWarRelationships(false);

            if (Social != null) Social.HoldUntilShots = false;

            // Whoever is left decides they have made their point and goes home.
            Scatter();

            IsRunning = false;

            // The worse it is between you, the sooner the next one. Somebody who has just lost
            // four men on your block does not wait half an hour to come back.
            _nextRoll = Game.GameTime + (int)(CalmMs - (CalmMs - CalmAtWorstMs) * _heat);

            ClearMarker();
            Clear();

            if (!string.IsNullOrEmpty(reason))
            {
                Notify.Failure(reason);
                return;
            }

            // Two stars, now it is over. Nobody in that street called it in while it was
            // happening -- they called it in once the shooting stopped, which is both how it
            // actually goes and the reason the fight itself is allowed to be a fight.
            if (showed)
            {
                try
                {
                    Game.Player.Wanted.SetWantedLevel(StarsAfter, false);
                    Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
                }
                catch { /* the law will find him eventually */ }
            }

            if (held)
            {
                var rep = 25f + kills * 4f;

                _crew.AddRep(rep, "for holding the block");
                _state.AddRespect(rep * 0.6f);
                _state.Touch();

                Notify.Important("~g~You held it.~s~ " + kills + " of theirs down.");

                if (Social != null)
                {
                    Social.On(SocialEvent.WarHeld, attacker == null ? "" : attacker.Name);

                    // And then they argue about it for a few minutes, the way people do.
                    Social.Argue(attacker == null ? "" : attacker.Name, true);
                }

                return;
            }

            if (!showed)
            {
                // Not turning up is the only real failure here. Everybody saw that you did not.
                _crew.AddRep(-30f, "for leaving the block");
                _state.Touch();

                Notify.Failure("they hit " + (_target == null ? "the block" : _target.Who) +
                               " and you were nowhere.");

                if (Social != null)
                {
                    Social.On(SocialEvent.WarLost, attacker == null ? "" : attacker.Name);
                    Social.Argue(attacker == null ? "" : attacker.Name, false);
                }

                return;
            }

            Notify.Ticker("~o~They pulled off.~s~ Nobody's calling that a win.");

            // Nobody won it, so both sides claim they did.
            if (Social != null) Social.Argue(attacker == null ? "" : attacker.Name, kills > 0);
        }

        /// <summary>Sends the survivors home rather than deleting them out from under you.</summary>
        private void Scatter()
        {
            var player = Game.Player.Character;

            foreach (var ped in _rivals)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    if (player != null && player.Exists())
                    {
                        Function.Call(Hash.TASK_SMART_FLEE_PED, ped.Handle, player.Handle, 300f, -1, false, false);
                    }

                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* they will find their own way */ }
            }

            _rivals.Clear();
        }

        private void Clear()
        {
            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* teardown */ }
            }
            _blips.Clear();

            foreach (var ped in _defenders)
            {
                try { if (ped != null && ped.Exists()) ped.MarkAsNoLongerNeeded(); }
                catch { /* teardown */ }
            }
            _defenders.Clear();

            // Let go, never deleted -- the same rule as everything else that ends up in a street.
            foreach (var car in _cars)
            {
                try { if (car != null && car.Exists()) car.MarkAsNoLongerNeeded(); }
                catch { /* teardown */ }
            }
            _cars.Clear();

            _attacker = null;
            _target = null;
        }

        public void RestoreWorld()
        {
            if (IsRunning) HoldTheLaw(false);

            Scatter();
            ClearMarker();
            Clear();
            IsRunning = false;
        }

        // ---- hud ---------------------------------------------------------------

        /// <summary>Who is on the block, and how it is going. No clock.</summary>
        public void Draw()
        {
            if (!IsRunning || _target == null) return;

            // A countdown is deliberately not here. A draining clock makes a raid into a timed
            // objective -- you watch the bar instead of the street, and the moment it empties
            // you know it is over before it is over. Nobody standing in that yard knows how long
            // this lasts.
            //
            // The bar is still worth having, so it shows something you could actually see from
            // where you are standing: how many of them are still up. It fills as you put them
            // down, so it reads as progress without ever telling you how long is left -- and a
            // fresh carload pulling in pushes it back, which a clock could never do.
            const float x = 0.5f;
            const float y = 0.115f;
            const float w = 0.22f;
            const float h = 0.014f;

            Hud.Text(_target.Who.ToUpperInvariant() + " UNDER ATTACK", x, y - 0.052f, 0.60f,
                     Palette.Danger, Hud.FontCursive);

            Hud.Text(_attacker == null ? "" : _attacker.Name.ToUpperInvariant(),
                     x, y - 0.018f, 0.28f, Palette.TextDim, Hud.FontLabel);

            var standing = 0;

            for (var i = 0; i < _rivals.Count; i++)
            {
                var ped = _rivals[i];
                if (ped != null && ped.Exists() && ped.IsAlive) standing++;
            }

            var sent = standing + _kills;
            var done = sent <= 0 ? 0f : _kills / (float)sent;

            Hud.Rect(x, y, w + 0.004f, h + 0.004f, System.Drawing.Color.FromArgb(190, 8, 8, 10));
            Hud.Rect(x, y, w, h, System.Drawing.Color.FromArgb(160, 30, 32, 34));

            var filled = w * done;
            if (filled > 0f) Hud.Rect(x - (w - filled) * 0.5f, y, filled, h, Palette.Danger);

            Hud.Text(standing + " still up   ·   " + _kills + " down", x, y + 0.016f, 0.26f,
                     Palette.TextDim, Hud.FontBody);
        }
    }
}
