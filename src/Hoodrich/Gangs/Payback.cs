using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// What happens after you name a set in public.
    ///
    /// The replies on the feed are free. This is the part that is not: somewhere between one
    /// and five minutes after the post, a car full of them turns up wherever you happen to be
    /// standing. Not at your house, not at a marker -- where you are, because that is the whole
    /// threat and the reason a diss is worth thinking about before you send it.
    ///
    /// Three ways it comes, picked at random, because a single scripted answer stops being a
    /// consequence the second time you see it:
    ///
    ///   Drive-by   -- they do not stop. The car comes past, the windows come down, and it
    ///                 keeps going. Over in six seconds and you never get near them.
    ///   Hands      -- they pull up, get out with bats, and it is a fight rather than a
    ///                 shooting. Somebody who called you out on the internet wants to be seen
    ///                 doing it, not to catch a body.
    ///   Guns       -- they pull up, get out, and it is not a fight.
    ///
    /// The attackers go in their own relationship group rather than their gang's. Making the
    /// whole of the Ballas hate the player because he typed something would be a permanent
    /// change to the world made by a menu, and it would still be there three hours later when
    /// he had forgotten he ever posted it.
    /// </summary>
    internal sealed class Payback
    {
        /// <summary>The window. Long enough that you stop watching for it.</summary>
        private const int SoonestMs = 60000;
        private const int LatestMs = 300000;

        /// <summary>How far out they spawn, so they arrive rather than appear.</summary>
        private const float SpawnRange = 95f;

        /// <summary>Close enough to the player for a roll-up crew to get out.</summary>
        private const float DismountRange = 22f;

        /// <summary>A drive-by that has not happened by now is a car stuck in traffic.</summary>
        private const int DriveByPatienceMs = 45000;

        /// <summary>Once they are past you, this long before the whole thing packs up.</summary>
        private const int LeaveMs = 7000;

        /// <summary>Everything is released this long after it started, whatever happened.</summary>
        private const int LifetimeMs = 150000;

        private const int TickMs = 900;

        /// <summary>Cars a set would actually turn up in.</summary>
        private static readonly string[] Cars =
        {
            "baller", "buccaneer", "manana", "peyton", "primo", "tornado", "voodoo",
        };

        /// <summary>
        /// What a particular set turns up in, where they have their own answer.
        ///
        /// Everybody used to arrive in the same seven lowriders, which is fine for a Los Santos
        /// set and reads as nonsense for a biker gang: naming the Lost brought a Voodoo full of
        /// bearded men. The Lost ride, the Triads and the Armenians drive something with money
        /// in it, and everybody without an entry falls back to the list above.
        /// </summary>
        private static readonly Dictionary<string, string[]> Rides =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "lost", new[] { "daemon", "hexer", "sovereign", "zombiea", "gburrito" } },
                { "triads", new[] { "kuruma", "schafter2", "cavalcade", "landstalker" } },
                { "armenians", new[] { "schafter2", "oracle", "felon", "cavalcade" } },
                { "koreans", new[] { "kuruma", "sultan", "penumbra", "fugitive" } },
                { "vagos", new[] { "tornado", "buccaneer", "chino", "voodoo" } },
                { "marabunta", new[] { "tornado", "manana", "voodoo", "moonbeam" } },
            };

        /// <summary>
        /// Who a set is made of when nothing has said.
        ///
        /// Gangs added in gangs.json rather than in code arrive with an EMPTY model list, and
        /// an empty list means every seat fails to fill, which means no driver, which means the
        /// visit is quietly binned. Naming the Koreans did nothing at all for exactly this
        /// reason, and from the street it was indistinguishable from the feature being broken.
        /// </summary>
        private static readonly string[] AnyGoons =
        {
            "g_m_y_lost_01", "g_m_y_mexgoon_01", "g_m_y_salvagoon_01", "a_m_m_soucent_01"
        };

        private enum Flavour { DriveBy, Hands, Guns }

        private readonly GangRegistry _gangs;
        private readonly Random _rng = new Random();

        private readonly List<Ped> _crew = new List<Ped>();
        private readonly List<Blip> _blips = new List<Blip>();

        private GangDef _who;
        private Flavour _how;

        private Vehicle _car;
        private Ped _driver;

        private int _dueAt;
        private int _startedAt;
        private int _lastTick;
        private int _passedAt;

        private int _groupHash;
        private bool _groupSet;

        /// <summary>Set by Main: true while something else owns the player's attention.</summary>
        public Func<bool> Busy;

        public Payback(GangRegistry gangs)
        {
            _gangs = gangs;
        }

        public bool IsRunning => _startedAt != 0;

        /// <summary>Somebody is on their way, or already here.</summary>
        public bool IsOwed => _dueAt != 0 || IsRunning;

        /// <summary>Whether this car belongs to us, for the traffic watchdog.</summary>
        public bool Owns(Vehicle car)
        {
            return car != null && _car != null && _car.Exists() && car.Handle == _car.Handle;
        }

        /// <summary>
        /// You said it, so it is coming.
        ///
        /// A second diss while one is already on the way does not stack another car on top --
        /// it pulls the one that is coming forward. Two carloads converging because you pressed
        /// the button twice is a bug that looks like a feature until it happens.
        /// </summary>
        /// <summary>
        /// Puts the debt back on the clock after an attempt that could not start.
        ///
        /// Sooner than a fresh one, because it is not a new grudge -- it is the same one, still
        /// owed, having failed to find a road.
        /// </summary>
        private void Rearm()
        {
            if (_who == null) return;

            _dueAt = Game.GameTime + 20000 + _rng.Next(25000);
            Log.Info("Payback to " + _who.Id + " could not start; trying again in " +
                     ((_dueAt - Game.GameTime) / 1000) + "s.");
        }

        public void Owed(string gangId)
        {
            var gang = _gangs == null ? null : _gangs.Get(gangId);
            if (gang == null) return;

            _who = gang;

            var when = Game.GameTime + SoonestMs + _rng.Next(LatestMs - SoonestMs);
            if (_dueAt == 0 || when < _dueAt) _dueAt = when;

            Log.Info("Payback owed to " + gang.Id + " in " +
                     ((_dueAt - Game.GameTime) / 1000) + "s.");
        }

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            if (IsRunning)
            {
                Run(now);
                return;
            }

            if (_dueAt == 0 || now < _dueAt) return;

            // Not in the middle of something else. It keeps waiting rather than being dropped:
            // the debt does not expire because you happened to be on a job when it came due.
            if (Busy != null && Busy()) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            _dueAt = 0;
            Begin(player);
        }

        private void Begin(Ped player)
        {
            if (_who == null) return;

            _how = (Flavour)_rng.Next(3);

            try
            {
                // Behind him and on a road, so it comes round a corner instead of appearing.
                var back = player.Position - player.ForwardVector * SpawnRange;
                var spawn = World.GetNextPositionOnStreet(back);

                if (spawn == Vector3.Zero)
                {
                    // No road behind you -- indoors, up an alley, out in a field. The debt is
                    // put BACK rather than swallowed, which is the bug this whole method had:
                    // _dueAt is cleared before we get here, so one failed attempt was the end
                    // of it and nobody ever came. From the street that is the feature not
                    // working, and it fails most often exactly where you would post from.
                    Rearm();
                    return;
                }

                var list = _who != null && Rides.TryGetValue(_who.Id, out var theirs)
                    ? theirs
                    : Cars;

                var model = new Model(list[_rng.Next(list.Length)]);
                if (!model.IsValid || !model.Request(2000))
                {
                    // Their own ride is not in this copy of the game. Anybody can drive a Voodoo.
                    model = new Model(Cars[_rng.Next(Cars.Length)]);
                    if (!model.IsValid || !model.Request(2000)) { Rearm(); return; }
                }

                _car = World.CreateVehicle(model, spawn);
                model.MarkAsNoLongerNeeded();

                if (_car == null || !_car.Exists()) return;

                _car.IsPersistent = true;

                MakeGroup();

                // Four of them for a fight, three for a drive-by -- the fourth has nowhere to
                // lean out of.
                var seats = _how == Flavour.DriveBy ? 2 : 3;

                for (var seat = -1; seat <= seats; seat++)
                {
                    var man = Spawn(_car, seat);
                    if (man == null) continue;

                    if (seat == -1) _driver = man;
                    else if (_how == Flavour.DriveBy) ArmForDriveBy(man, player);
                    else Arm(man);
                }

                if (_driver == null)
                {
                    Pack();
                    Rearm();
                    return;
                }

                Send(player);

                _startedAt = Game.GameTime;
                _passedAt = 0;

                Notify.Failure(Warning());
                Log.Info("Payback: " + _who.Id + " came for the post, " + _how + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Payback failed to start: " + ex.Message);
                Pack();
            }
        }

        /// <summary>What you get told, which is never what is about to happen.</summary>
        private string Warning()
        {
            switch (_how)
            {
                case Flavour.DriveBy: return "that car's been past twice.";
                case Flavour.Hands: return "somebody read your post.";
                default: return _who.Name.ToLowerInvariant() + " found you.";
            }
        }

        /// <summary>
        /// Tells the driver what kind of visit this is.
        ///
        /// A drive-by aims at a point on the far side of the player so the car passes THROUGH
        /// rather than arriving: driving to where somebody is standing means stopping where
        /// somebody is standing, which is a delivery, not a drive-by.
        /// </summary>
        private void Send(Ped player)
        {
            if (_how == Flavour.DriveBy)
            {
                var through = player.Position + (player.Position - _car.Position).Normalized * 90f;
                var past = World.GetNextPositionOnStreet(through);
                if (past == Vector3.Zero) past = through;

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _driver.Handle, _car.Handle,
                              past.X, past.Y, past.Z, 28f, 0, _car.Model.Hash, 786606, 4f, true);
                return;
            }

            var where = player.Position;

            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _driver.Handle, _car.Handle,
                          where.X, where.Y, where.Z, 24f, 0, _car.Model.Hash, 786606, 6f, true);
        }

        private void Run(int now)
        {
            var player = Game.Player.Character;

            if (player == null || !player.Exists() || now - _startedAt > LifetimeMs)
            {
                Pack();
                return;
            }

            Cull();

            if (_how == Flavour.DriveBy)
            {
                RunDriveBy(player, now);
                return;
            }

            // Everybody down, and there is nothing left to be attacked by.
            if (_crew.Count == 0)
            {
                Notify.Ticker("~g~That's them dealt with.~s~");
                Pack();
                return;
            }

            var here = _car != null && _car.Exists() &&
                       _car.Position.DistanceTo(player.Position) <= DismountRange;

            foreach (var man in _crew)
            {
                if (!man.IsInVehicle())
                {
                    Attack(man, player);
                    continue;
                }

                if (!here) continue;

                man.Task.LeaveVehicle();
                Attack(man, player);
            }
        }

        /// <summary>
        /// They pass, they shoot, they keep going.
        ///
        /// The leaving is the whole shape of it. A drive-by crew that hangs about afterwards is
        /// four men stood in the road with guns, which is a different thing entirely and one
        /// you can win.
        /// </summary>
        private void RunDriveBy(Ped player, int now)
        {
            var gone = _car == null || !_car.Exists();
            var far = !gone && _car.Position.DistanceTo(player.Position) > 55f;

            if (_passedAt == 0)
            {
                var close = !gone && _car.Position.DistanceTo(player.Position) < 35f;

                // Close, then far again, is a pass. Or the patience ran out, which means the
                // car is stuck somewhere and this is never going to look like anything.
                if (close) _passedAt = -1;
                else if (_passedAt == 0 && now - _startedAt > DriveByPatienceMs) { Pack(); return; }
            }

            if (_passedAt == -1 && (far || gone)) _passedAt = now;

            if (_passedAt <= 0) return;

            // Off they go, and everything is handed back so the car does not become a monument
            // in the middle of the road.
            if (_driver != null && _driver.Exists() && _driver.IsAlive && _car != null && _car.Exists())
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _driver.Handle);
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, _driver.Handle, _car.Handle, 30f, 786606);
            }

            if (now - _passedAt < LeaveMs) return;

            Notify.Ticker("~s~They kept driving.");
            Pack();
        }

        private void Attack(Ped man, Ped player)
        {
            try
            {
                if (Function.Call<int>(Hash.GET_PED_TARGET_FROM_COMBAT_PED, man.Handle, 0) != 0) return;

                Function.Call(Hash.TASK_COMBAT_PED, man.Handle, player.Handle, 0, 16);
            }
            catch
            {
                // He will find his own way to the fight.
            }
        }

        private Ped Spawn(Vehicle car, int seat)
        {
            try
            {
                var name = _who.MemberModels.Count > 0
                    ? _who.MemberModels[_rng.Next(_who.MemberModels.Count)]
                    : AnyGoons[_rng.Next(AnyGoons.Length)];
                var model = new Model(name);
                if (!model.IsValid || !model.Request(2000)) return null;

                var man = car.CreatePedOnSeat((VehicleSeat)seat, model);
                model.MarkAsNoLongerNeeded();

                if (man == null || !man.Exists()) return null;

                man.IsPersistent = true;
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, true);

                if (_groupSet)
                {
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, man.Handle, _groupHash);
                }

                _crew.Add(man);
                Mark(man);

                return man;
            }
            catch (Exception ex)
            {
                Log.Debug("Payback could not fill a seat: " + ex.Message);
                return null;
            }
        }

        private void Arm(Ped man)
        {
            try
            {
                // Hands means hands. Somebody who called you out on the internet wants to be
                // seen doing something about it; he is not necessarily trying to catch a body.
                var weapon = _how == Flavour.Hands
                    ? "WEAPON_BAT"
                    : (_rng.NextDouble() < 0.5 ? "WEAPON_PISTOL" : "WEAPON_MICROSMG");

                var hash = Function.Call<uint>(Hash.GET_HASH_KEY, weapon);

                Function.Call(Hash.GIVE_WEAPON_TO_PED, man.Handle, hash, 120, false, true);
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, man.Handle, hash, true);

                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 0, true);   // uses cover
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 5, true);   // takes on armed
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 46, true);  // always fight
                Function.Call(Hash.SET_PED_ACCURACY, man.Handle, _how == Flavour.Hands ? 8 : 22);
            }
            catch (Exception ex)
            {
                Log.Debug("Payback could not arm somebody: " + ex.Message);
            }
        }

        /// <summary>
        /// TASK_DRIVE_BY does nothing at all unless the ped is holding something he is allowed
        /// to fire out of a window, which is how you get three men driving past waving.
        /// </summary>
        private void ArmForDriveBy(Ped man, Ped player)
        {
            try
            {
                var hash = Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_MICROSMG");

                Function.Call(Hash.GIVE_WEAPON_TO_PED, man.Handle, hash, 250, false, true);
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, man.Handle, hash, true);

                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 5, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 46, true);
                Function.Call(Hash.SET_PED_ACCURACY, man.Handle, 18);

                Function.Call(Hash.TASK_DRIVE_BY, man.Handle, player.Handle, 0,
                              0f, 0f, 0f, 45f, 100, true, hash);
            }
            catch (Exception ex)
            {
                Log.Debug("Payback could not arm a shooter: " + ex.Message);
            }
        }

        /// <summary>
        /// A relationship group of their own.
        ///
        /// Putting them in the Ballas group and making the Ballas hate the player would be a
        /// permanent change to the world made by pressing a button on a menu, still in force
        /// three hours later when he has forgotten he ever posted anything. These four hate
        /// him. Their set carries on as it was.
        /// </summary>
        private void MakeGroup()
        {
            if (_groupSet) return;

            try
            {
                var made = new OutputArgument();
                Function.Call(Hash.ADD_RELATIONSHIP_GROUP, "HOODRICH_PAYBACK", made);

                _groupHash = made.GetResult<int>();
                if (_groupHash == 0)
                {
                    _groupHash = Function.Call<int>(Hash.GET_HASH_KEY, "HOODRICH_PAYBACK");
                }

                var player = Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER");

                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, _groupHash, player);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, player, _groupHash);

                _groupSet = true;
            }
            catch (Exception ex)
            {
                Log.Debug("Payback could not make its relationship group: " + ex.Message);
            }
        }

        private void Mark(Ped man)
        {
            try
            {
                var blip = man.AddBlip();
                if (blip == null || !blip.Exists()) return;

                blip.Sprite = BlipSprite.Standard;
                blip.Color = BlipColor.Red;
                blip.Scale = 0.75f;
                blip.Name = _who.Name;

                _blips.Add(blip);
            }
            catch
            {
                // A blip is a nicety.
            }
        }

        /// <summary>Drops anybody who is down, so the "all dealt with" check can be true.</summary>
        private void Cull()
        {
            for (var i = _crew.Count - 1; i >= 0; i--)
            {
                var man = _crew[i];
                if (man != null && man.Exists() && man.IsAlive) continue;

                if (man != null && man.Exists()) man.MarkAsNoLongerNeeded();
                _crew.RemoveAt(i);
            }
        }

        /// <summary>
        /// Everything handed back.
        ///
        /// The car particularly: a persistent vehicle abandoned in a lane is invisible to the
        /// game's own population control and becomes a permanent roadblock, which is a bug this
        /// mod has already been through once.
        /// </summary>
        private void Pack()
        {
            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* teardown */ }
            }

            _blips.Clear();

            foreach (var man in _crew)
            {
                try
                {
                    if (man == null || !man.Exists()) continue;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, false);
                    man.IsPersistent = false;
                    man.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }

            _crew.Clear();

            try
            {
                if (_car != null && _car.Exists())
                {
                    _car.IsPersistent = false;
                    _car.MarkAsNoLongerNeeded();
                }
            }
            catch { /* teardown */ }

            _car = null;
            _driver = null;
            _who = null;
            _startedAt = 0;
            _passedAt = 0;
        }

        public void RestoreWorld()
        {
            Pack();
            _dueAt = 0;
        }
    }
}
