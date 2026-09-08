using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.State;
using Hoodrich.UI;
using Hoodrich.Weapons;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The boot of a car you own.
    ///
    /// TWO WAYS IN. Stood at the back of one of yours, the key pops the lid, he turns to the
    /// car and goes in head first -- the game's own bin-rummage, which is a man bent double
    /// over something at waist height and is exactly the shape of somebody going through a
    /// boot -- and the inventory comes up while he is in there. Sat in the driver's seat with
    /// the car stopped, the same key brings up the same screen and nothing else: the lid
    /// stays shut and nobody bends, because you cannot get into a boot from the front seat
    /// and a lid that lifts itself behind you is a worse feature for pretending you could.
    ///
    /// WHAT COUNTS AS YOURS is what OwnedCars says: the plate. Not "a car you are near" and
    /// not "the car you drove here" -- a car off Hao's lot with your record on it. So there is
    /// one boot per owned car, kept in the save under the car's id, and it is the same boot
    /// after the car has been stood back up, towed, resprayed or driven through a wall.
    ///
    /// Walking away or driving off shuts it. The screen never outlives the car it is about.
    /// </summary>
    internal sealed class Boot
    {
        /// <summary>How close to the boot point he has to stand. The point is a little behind the bumper.</summary>
        private const float ReachM = 2.1f;

        /// <summary>Behind the model's rear face, where the lid actually is.</summary>
        private const float BehindM = 0.55f;

        /// <summary>How far he can drift from the boot before the lid shuts on its own.</summary>
        private const float LeaveM = 3.2f;

        /// <summary>Stopped, for the purpose of doing this from the driver's seat.</summary>
        private const float StoppedMps = 0.5f;

        /// <summary>The scan for a car behind him is not a per-frame job.</summary>
        private const int ScanMs = 200;

        /// <summary>How long the going-in clip runs before the rummage takes over, and the coming-out clip.</summary>
        private const int EnterMs = 1150;
        private const int ExitMs = 900;

        private const int BootDoor = 5;

        private const string EnterDict = "amb@prop_human_bum_bin@enter";
        private const string IdleDict = "amb@prop_human_bum_bin@idle_a";
        private const string ExitDict = "amb@prop_human_bum_bin@exit";
        private const string EnterClip = "enter";
        private const string IdleClip = "idle_a";
        private const string ExitClip = "exit";

        private enum Phase { Idle, GoingIn, In, ComingOut }

        private readonly PlayerState _state;
        private readonly OwnedCars _owned;
        private readonly Drugs _drugs;
        private readonly WeaponRegistry _guns;
        private readonly GunLocker _locker;
        private readonly TrunkScreen _screen = new TrunkScreen();

        /// <summary>Set by Main: writes the save out now. The boot is somewhere product lives, so it is saved like the house is.</summary>
        public Action Save;

        private Phase _phase;
        private int _phaseAt;
        private bool _sat;
        private bool _changed;

        /// <summary>Whether this class lifted the lid, so it only ever shuts what it opened.</summary>
        private bool _lidUp;

        private Vehicle _car;
        private OwnedCar _record;

        private int _nextScan;
        private Vehicle _near;
        private OwnedCar _nearRecord;
        private bool _nearSat;
        private int _promptSince;

        public Boot(PlayerState state, OwnedCars owned, Drugs drugs, WeaponRegistry guns, GunLocker locker)
        {
            _state = state;
            _owned = owned;
            _drugs = drugs;
            _guns = guns;
            _locker = locker;
        }

        /// <summary>The lid is up, or on its way up or down. While this is true the boot owns the frame.</summary>
        public bool IsOpen => _phase != Phase.Idle || _screen.IsOpen;

        public void Update()
        {
            try
            {
                if (IsOpen) Tend();
                else Watch();
            }
            catch (Exception ex)
            {
                Log.Debug("Boot: " + ex.Message);
                try { Close(); } catch { /* let go of what we can */ }
            }
        }

        public void Draw()
        {
            if (_screen.IsOpen) _screen.Draw();
        }

        // ---- looking for one of yours ------------------------------------------------------

        private void Watch()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists() || !me.IsAlive) { _near = null; return; }

            var now = Game.GameTime;

            if (now >= _nextScan)
            {
                _nextScan = now + ScanMs;
                Scan(me);
            }

            if (_near == null || !_near.Exists())
            {
                _promptSince = 0;
                return;
            }

            // In the seat the same key is the horn. It is not the horn while the prompt is up.
            if (_nearSat) Game.DisableControlThisFrame(Control.VehicleHorn);

            Prompt(now);

            if (!Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)Control.Context) &&
                !Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

            Open(me, _near, _nearRecord, _nearSat);
        }

        private void Scan(Ped me)
        {
            _near = null;
            _nearRecord = null;
            _nearSat = false;

            if (_state == null || _owned == null || _state.Owned.Count == 0) return;

            if (me.IsInVehicle())
            {
                var car = me.CurrentVehicle;
                if (car == null || !car.Exists() || car.Speed > StoppedMps) return;
                if (me.SeatIndex != VehicleSeat.Driver) return;

                var record = _owned.Which(car);
                if (record == null || !HasLid(car)) return;

                _near = car;
                _nearRecord = record;
                _nearSat = true;
                return;
            }

            if (me.IsRagdoll || me.IsInAir) return;

            Vehicle best = null;
            OwnedCar bestRecord = null;
            var nearest = ReachM;

            foreach (var car in World.GetNearbyVehicles(me.Position, 8f))
            {
                if (car == null || !car.Exists()) continue;

                var record = _owned.Which(car);
                if (record == null || !HasLid(car)) continue;

                var d = me.Position.DistanceTo(LidPoint(car));
                if (d >= nearest) continue;

                nearest = d;
                best = car;
                bestRecord = record;
            }

            _near = best;
            _nearRecord = bestRecord;
        }

        private static bool HasLid(Vehicle car)
        {
            try { return Function.Call<bool>(Hash.GET_IS_DOOR_VALID, car.Handle, BootDoor); }
            catch { return false; }
        }

        /// <summary>A little behind the rear face of the model, on the centre line, at ground height.</summary>
        private static Vector3 LidPoint(Vehicle car)
        {
            var back = -2.2f;

            try
            {
                var min = new OutputArgument();
                var max = new OutputArgument();
                Function.Call(Hash.GET_MODEL_DIMENSIONS, car.Model.Hash, min, max);
                back = min.GetResult<Vector3>().Y;
            }
            catch
            {
                // A saloon, which most of them are.
            }

            return Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, car.Handle,
                                          0f, back - BehindM, 0f);
        }

        // ---- the prompt ---------------------------------------------------------------------

        /// <summary>The same bottom bar the dog uses: the car's name, then what the key does.</summary>
        private void Prompt(int now)
        {
            var fade = UiKit.PromptFade(ref _promptSince, now);

            var who = (_nearRecord == null || string.IsNullOrEmpty(_nearRecord.Name) ? "YOUR CAR" : _nearRecord.Name)
                .ToUpperInvariant();
            var cap = Hud.OnPad ? "D-PAD RIGHT" : "E";

            UiKit.Prompt("car.png", who, cap + "   " + (_nearSat ? "CHECK THE BOOT" : "POP THE BOOT"), fade);
        }

        // ---- open ---------------------------------------------------------------------------

        private void Open(Ped me, Vehicle car, OwnedCar record, bool sat)
        {
            _car = car;
            _record = record;
            _sat = sat;
            _changed = false;
            _promptSince = 0;
            _near = null;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            if (sat)
            {
                // Nothing to bend over from the seat, and no lid: it stays shut behind him.
                // Straight to the screen.
                _phase = Phase.In;
                _phaseAt = Game.GameTime;
                Show();
                return;
            }

            try
            {
                Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, car.Handle, BootDoor, false, false);
                _lidUp = true;
            }
            catch { /* a lid that will not lift is still a boot */ }

            // Turn him to the car, then the going-in clip. Set rather than tasked, the way the
            // dog does it: a turn task takes a second he spends stood looking the wrong way.
            var to = car.Position - me.Position;
            me.Heading = (float)((Math.Atan2(-to.X, to.Y) * 180.0 / Math.PI + 360.0) % 360.0);

            _phase = Phase.GoingIn;
            _phaseAt = Game.GameTime;

            if (!Play(me, EnterDict, EnterClip, EnterMs + 300, 2))
            {
                // No clip; no wait either.
                _phaseAt -= EnterMs;
            }
        }

        private void Show()
        {
            if (_state == null || _record == null) return;

            var trunk = _state.TrunkOf(_record.Id);

            _screen.Open(trunk, _state.Stash, _drugs, _guns, _locker, _record.Name, () =>
            {
                _changed = true;
                _state.Touch();
            });
        }

        // ---- while it is up -----------------------------------------------------------------

        private void Tend()
        {
            var me = Game.Player.Character;
            var now = Game.GameTime;

            if (me == null || !me.Exists() || !me.IsAlive || _car == null || !_car.Exists() || _car.IsDead)
            {
                Close();
                return;
            }

            switch (_phase)
            {
                case Phase.GoingIn:
                    if (now - _phaseAt < EnterMs) return;

                    _phase = Phase.In;
                    _phaseAt = now;
                    Play(me, IdleDict, IdleClip, -1, 1);
                    Show();
                    return;

                case Phase.In:
                    if (!_screen.IsOpen)
                    {
                        // Shut from the screen. Out he comes.
                        Leave(me);
                        return;
                    }

                    // Drifted off, or drove off. The lid shuts behind him.
                    if (_sat)
                    {
                        if (!me.IsInVehicle(_car) || _car.Speed > StoppedMps * 3f) { _screen.Close(); Leave(me); return; }
                    }
                    else
                    {
                        if (me.IsInVehicle() || me.IsRagdoll || me.Position.DistanceTo(LidPoint(_car)) > LeaveM)
                        {
                            _screen.Close();
                            Leave(me);
                            return;
                        }

                        // The rummage is a loop, but a knock or a shove ends it. Put it back.
                        if (now - _phaseAt > 800 &&
                            !Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, me.Handle, IdleDict, IdleClip, 3))
                        {
                            Play(me, IdleDict, IdleClip, -1, 1);
                        }
                    }

                    _screen.Update();
                    return;

                case Phase.ComingOut:
                    if (now - _phaseAt < ExitMs) return;
                    Close();
                    return;
            }
        }

        private void Leave(Ped me)
        {
            if (_sat)
            {
                Close();
                return;
            }

            _phase = Phase.ComingOut;
            _phaseAt = Game.GameTime;

            if (!Play(me, ExitDict, ExitClip, ExitMs + 200, 0))
            {
                try { me.Task.ClearAll(); } catch { /* he is stood there either way */ }
                _phaseAt -= ExitMs;
            }
        }

        /// <summary>Everything down: the screen, the clip, the lid. Safe to call twice.</summary>
        public void Close()
        {
            if (_screen.IsOpen) _screen.Close();

            var me = Game.Player.Character;

            if (_phase == Phase.In || _phase == Phase.GoingIn)
            {
                try
                {
                    if (me != null && me.Exists() && !_sat) Function.Call(Hash.STOP_ANIM_TASK, me.Handle, IdleDict, IdleClip, -4f);
                }
                catch { /* nothing to stop */ }
            }

            try
            {
                if (_lidUp && _car != null && _car.Exists()) Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, _car.Handle, BootDoor, false);
            }
            catch { /* the car has gone, and the lid with it */ }

            _lidUp = false;

            if (_changed)
            {
                _changed = false;
                try { Save?.Invoke(); } catch (Exception ex) { Log.Debug("Boot: could not save: " + ex.Message); }
            }

            foreach (var dict in new[] { EnterDict, IdleDict, ExitDict })
            {
                try { Function.Call(Hash.REMOVE_ANIM_DICT, dict); } catch { /* not loaded */ }
            }

            _phase = Phase.Idle;
            _car = null;
            _record = null;
            _sat = false;
            _promptSince = 0;
            _nextScan = Game.GameTime + 600;
        }

        public void RestoreWorld()
        {
            Close();
        }

        // ---- clips ----------------------------------------------------------------------------

        /// <summary>Requests and plays, or says it could not. A dictionary that is not in yet is asked for and tried again next tick by the caller.</summary>
        private static bool Play(Ped me, string dict, string clip, int forMs, int flag)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                var until = Environment.TickCount + 250;
                while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                {
                    if (Environment.TickCount - until > 0) return false;
                    Script.Wait(0);
                }

                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, dict, clip, 4f, -4f, forMs, flag, 0f, false, false, false);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Boot: could not play " + dict + " " + clip + ": " + ex.Message);
                return false;
            }
        }
    }
}
