using System;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Supply
{
    /// <summary>Where a delivery has got to.</summary>
    internal enum DeliveryState
    {
        None,

        /// <summary>Player has the phone to their ear.</summary>
        Calling,

        /// <summary>He is driving over.</summary>
        Driving,

        /// <summary>He has pulled up and is waiting on you.</summary>
        Waiting
    }

    /// <summary>
    /// The dock worker bringing it to you.
    ///
    /// Everything else in the supply chain makes you travel, and that is the point of it -- but
    /// the docks are the reward for having moved real weight, so the docks come to you. You
    /// phone him, he sets off from somewhere out of sight, and he drives the whole way: no
    /// teleport, no popping into existence at the kerb. What you are buying with that phone
    /// call is the drive, which is why the car has to be real and has to actually arrive.
    /// </summary>
    internal sealed class Delivery
    {
        /// <summary>How long the player holds the phone before he sets off.</summary>
        private const int CallMs = 4200;

        /// <summary>Spawned this far out, so the car is never seen appearing.</summary>
        private const float SpawnMinDistance = 220f;
        private const float SpawnMaxDistance = 340f;

        /// <summary>He parks about here and waits.</summary>
        private const float ArriveDistance = 18f;

        /// <summary>Give up if the drive takes longer than this -- traffic, cliffs, the ocean.</summary>
        private const int DriveTimeoutMs = 5 * 60 * 1000;

        /// <summary>Wander off if you never come to the car.</summary>
        private const int WaitTimeoutMs = 3 * 60 * 1000;

        /// <summary>How often the route is re-aimed at a player who keeps moving.</summary>
        private const int RetaskIntervalMs = 4000;

        /// <summary>Only re-aim once you have actually gone somewhere.</summary>
        private const float RetaskMoveDistance = 35f;

        /// <summary>Far enough that he stops bothering to follow you.</summary>
        private const float AbandonDistance = 500f;

        /// <summary>
        /// He turns up in the same car every time, and it is not a work van.
        ///
        /// The Astron is a DLC model, so an install without it falls through to something else
        /// black and expensive rather than failing the delivery -- but on any current copy it
        /// is always the Astron, which is the point: you learn to recognise it coming.
        /// </summary>
        private static readonly string[] CarModels =
        {
            "astron", "baller3", "baller4", "baller2", "granger"
        };

        /// <summary>Metallic black, inside and out.</summary>
        private const int BlackPaint = 0;

        /// <summary>Window tint 5 is the limo one.</summary>
        private const int LimoTint = 5;

        private readonly Random _rng = new Random();

        private DealerDef _def;
        private Ped _driver;
        private Vehicle _car;
        private Blip _blip;

        private int _stateSince;
        private int _lastRetask;
        private Vector3 _target;

        public DeliveryState State { get; private set; } = DeliveryState.None;

        public bool IsActive => State != DeliveryState.None;

        /// <summary>Who is on the way, or null.</summary>
        public DealerDef Def => _def;

        /// <summary>The driver, once he is in the world -- the man you actually trade with.</summary>
        public Ped Driver => _driver != null && _driver.Exists() ? _driver : null;

        /// <summary>Set by Main: the conversation screen, and what he has to say.</summary>
        public Conversation Talk;
        public Func<DialogueNode> TalkBuilder;

        /// <summary>
        /// How he drives.
        ///
        /// Normal road driving with StopForVehicles dropped and SwerveAroundAllCars added, so a
        /// van double-parked on Innocence does not end the delivery -- he goes round it. He sat
        /// behind traffic indefinitely before, which reads as a broken errand rather than a
        /// careful driver.
        /// </summary>
        private const int DriveStyle = 786606;

        /// <summary>Close enough to do business over the roof of the car.</summary>
        private const float TalkRange = 4.5f;

        private bool _talkHeld;

        /// <summary>
        /// Walking up on him once he has parked.
        ///
        /// He used to be bought from through the wheel, which meant standing next to a man and
        /// opening a menu about him -- and if the range check the wheel used disagreed with
        /// where you were standing, there was nothing to press at all. Everybody else in this
        /// mod is talked to; so is he.
        /// </summary>
        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen) return;
            if (State != DeliveryState.Waiting) return;

            var driver = Driver;
            if (driver == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;
            if (player.Position.DistanceTo(driver.Position) > TalkRange) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to do business with " + Def.Name + ".");

            if (!Pressed()) return;

            var root = TalkBuilder == null ? null : TalkBuilder();
            if (root == null) return;

            Talk.Speaker = driver;
            Talk.Open(root, this);
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

        public float Distance
        {
            get
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return 9999f;

                if (_car != null && _car.Exists()) return player.Position.DistanceTo(_car.Position);
                if (_driver != null && _driver.Exists()) return player.Position.DistanceTo(_driver.Position);

                return 9999f;
            }
        }

        /// <summary>One line for the wheel.</summary>
        public string Status
        {
            get
            {
                switch (State)
                {
                    case DeliveryState.Calling: return "On the phone to " + _def.Name;
                    case DeliveryState.Driving: return _def.Name + " is driving over -- " +
                                                       Distance.ToString("0") + "m";
                    case DeliveryState.Waiting: return _def.Name + " is waiting on you -- " +
                                                       Distance.ToString("0") + "m";
                    default: return "";
                }
            }
        }

        // ---- placing the call --------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once the call is placed.</summary>
        public string Call(DealerDef def)
        {
            if (def == null) return "No such contact.";
            if (IsActive) return _def.Name + " is already on his way.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";
            if (player.IsInVehicle()) return "Get out of the car to make a call.";

            _def = def;
            State = DeliveryState.Calling;
            _stateSince = Game.GameTime;

            PlayPhoneAnimation(player);

            Notify.Ticker("~y~Calling " + def.Name + "...~s~");
            Log.Info("Called " + def.Id + " out for a delivery.");
            return null;
        }

        /// <summary>
        /// Puts the phone to the player's ear for the length of the call.
        ///
        /// The timed mobile task is the game's own: it draws the phone, the hand, and the whole
        /// idle, and it ends by itself -- so there is no prop to attach and nothing left stuck
        /// in the player's hand if the script is interrupted mid-call.
        /// </summary>
        private static void PlayPhoneAnimation(Ped player)
        {
            try
            {
                Function.Call(Hash.TASK_USE_MOBILE_PHONE_TIMED, player.Handle, CallMs);
            }
            catch (Exception ex)
            {
                Log.Debug("Phone animation failed: " + ex.Message);
            }
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (!IsActive) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                Cancel(null);
                return;
            }

            switch (State)
            {
                case DeliveryState.Calling:
                    if (Game.GameTime - _stateSince >= CallMs) Dispatch(player);
                    return;

                case DeliveryState.Driving:
                    TickDriving(player);
                    return;

                case DeliveryState.Waiting:
                    TickWaiting(player);
                    return;
            }
        }

        /// <summary>Puts him and the car on a road far enough out to be off screen, and sends him.</summary>
        private void Dispatch(Ped player)
        {
            if (!TryStartPoint(player.Position, out var start))
            {
                Cancel("He could not get to you from where you are.");
                return;
            }

            Model? carModel = null;
            foreach (var name in CarModels)
            {
                var m = new Model(name);
                if (!m.IsValid || !m.IsInCdImage || !m.Request(2000)) continue;
                carModel = m;
                break;
            }

            if (carModel == null)
            {
                Cancel("He could not get a car out.");
                return;
            }

            try
            {
                _car = World.CreateVehicle(carModel.Value, start);
                if (_car == null || !_car.Exists())
                {
                    Cancel("He could not get a car out.");
                    return;
                }

                _car.IsPersistent = true;
                BlackOut(_car);

                var pedModel = ResolveDriverModel();
                if (pedModel == null)
                {
                    Cancel("Nobody could make the run.");
                    return;
                }

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                                _car.Handle, 4, pedModel.Value.Hash, -1, true, false);
                try { pedModel.Value.MarkAsNoLongerNeeded(); } catch { }

                if (handle == 0)
                {
                    Cancel("Nobody could make the run.");
                    return;
                }

                _driver = (Ped)Entity.FromHandle(handle);
                if (_driver == null || !_driver.Exists())
                {
                    Cancel("Nobody could make the run.");
                    return;
                }

                _driver.IsPersistent = true;
                _driver.BlockPermanentEvents = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _driver.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _driver.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _driver.Handle, false);

                DriveTo(player.Position);

                CreateBlip();

                State = DeliveryState.Driving;
                _stateSince = Game.GameTime;

                Notify.Important("~y~" + _def.Name + "~s~ is on his way to you.");
                Log.Info("Delivery dispatched from " + start + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Could not dispatch the delivery.", ex);
                Cancel("Something went wrong with the run.");
            }
            finally
            {
                try { carModel.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        /// <summary>
        /// Blacked out and tinted, every panel, every time.
        ///
        /// A mod kit has to be set before any modification takes, which is the usual reason
        /// wheels and trim stay factory while the paint changes.
        /// </summary>
        private static void BlackOut(Vehicle car)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);

                Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, BlackPaint, BlackPaint);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, car.Handle, BlackPaint, BlackPaint);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, LimoTint);

                // Black wheels and trim, so nothing on it catches the light.
                Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, car.Handle, 7);
                Function.Call(Hash.SET_VEHICLE_MOD_COLOR_1, car.Handle, 0, 0, 0);
                Function.Call(Hash.SET_VEHICLE_MOD_COLOR_2, car.Handle, 0, 0);

                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, car.Handle, 0f);
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle, "HOODRCH");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not black out the delivery car: " + ex.Message);
            }
        }

        /// <summary>
        /// Points the car at somewhere and lets the game drive it there.
        ///
        /// Drive mode DriveStyle is the normal "obey the road" set: stops at lights, avoids traffic,
        /// takes junctions properly. He is delivering, not fleeing.
        /// </summary>
        private void DriveTo(Vector3 where)
        {
            if (_driver == null || !_driver.Exists() || _car == null || !_car.Exists()) return;

            _target = where;

            try
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _driver.Handle, _car.Handle,
                              where.X, where.Y, where.Z,
                              20f, 0, _car.Model.Hash, DriveStyle, ArriveDistance * 0.5f, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not route the delivery: " + ex.Message);
            }
        }

        private Model? ResolveDriverModel()
        {
            foreach (var name in _def.Models)
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
                    // Try the next.
                }
            }
            return null;
        }

        private void TickDriving(Ped player)
        {
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive ||
                _car == null || !_car.Exists())
            {
                Cancel("Your delivery never turned up.");
                return;
            }

            if (Game.GameTime - _stateSince > DriveTimeoutMs)
            {
                Cancel("He gave up trying to reach you.");
                return;
            }

            if (Distance > AbandonDistance)
            {
                Cancel("You went too far. He turned round.");
                return;
            }

            if (Distance > ArriveDistance)
            {
                // The drive task aims at a fixed coordinate, so a player who keeps walking is a
                // player he never reaches. Re-aimed whenever you have moved far enough to matter.
                if (Game.GameTime - _lastRetask > RetaskIntervalMs &&
                    player.Position.DistanceTo(_target) > RetaskMoveDistance)
                {
                    _lastRetask = Game.GameTime;
                    DriveTo(player.Position);
                }

                return;
            }

            State = DeliveryState.Waiting;
            _stateSince = Game.GameTime;

            try
            {
                // Pulls up and gets out, so the trade happens face to face at the car.
                Function.Call(Hash.TASK_LEAVE_VEHICLE, _driver.Handle, _car.Handle, 0);
                _car.IsEngineRunning = true;
            }
            catch (Exception ex)
            {
                Log.Debug("Delivery driver could not get out: " + ex.Message);
            }

            Notify.Important("~g~" + _def.Name + " has pulled up.~s~ Go and see him.");
        }

        private void TickWaiting(Ped player)
        {
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive)
            {
                Cancel("Your delivery is gone.");
                return;
            }

            if (Game.GameTime - _stateSince > WaitTimeoutMs)
            {
                Cancel("He was not waiting around all day.");
                return;
            }

            if (Distance > AbandonDistance) Cancel("You left him standing there.");
        }

        // ---- cleanup -----------------------------------------------------------

        /// <summary>Ends the run and lets the world have the driver and the van back.</summary>
        public void Cancel(string reason)
        {
            var name = _def == null ? "Your contact" : _def.Name;

            try
            {
                if (_driver != null && _driver.Exists())
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _driver.Handle, false);
                    _driver.MarkAsNoLongerNeeded();
                }
            }
            catch { /* teardown */ }

            try
            {
                if (_car != null && _car.Exists()) _car.MarkAsNoLongerNeeded();
            }
            catch { /* teardown */ }

            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch { /* teardown */ }

            _driver = null;
            _car = null;
            _blip = null;
            _def = null;
            State = DeliveryState.None;

            if (!string.IsNullOrEmpty(reason)) Notify.Ticker("~o~" + name + ": " + reason + "~s~");
        }

        /// <summary>Called once the trade is done, so he drives off rather than standing there.</summary>
        public void Finish()
        {
            try
            {
                if (_driver != null && _driver.Exists() && _car != null && _car.Exists())
                {
                    Function.Call(Hash.TASK_ENTER_VEHICLE, _driver.Handle, _car.Handle, 10000, -1, 2f, 1, 0);
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, _driver.Handle, _car.Handle, 20f, DriveStyle);
                }
            }
            catch { /* he can walk if he likes */ }

            Cancel(null);
        }

        private void CreateBlip()
        {
            try
            {
                if (_car == null || !_car.Exists()) return;

                _blip = _car.AddBlip();
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = BlipSprite.Truck;
                _blip.Color = BlipColor.Blue;
                _blip.Name = _def.Name;
                _blip.IsShortRange = false;
                _blip.Scale = 0.8f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the delivery: " + ex.Message);
            }
        }

        /// <summary>
        /// A road far enough out that the spawn is never witnessed, preferring somewhere behind
        /// the camera so even a long sightline down a street does not catch it.
        /// </summary>
        private bool TryStartPoint(Vector3 origin, out Vector3 spot)
        {
            spot = Vector3.Zero;

            var behind = -Vector3.Zero;
            try { behind = GameplayCamera.Direction; }
            catch { /* fall back to any direction */ }

            for (var attempt = 0; attempt < 14; attempt++)
            {
                double angle;

                if (attempt < 8 && behind != Vector3.Zero)
                {
                    // Behind the camera, give or take a quarter turn.
                    var facing = Math.Atan2(behind.Y, behind.X);
                    angle = facing + Math.PI + (_rng.NextDouble() - 0.5) * (Math.PI * 0.5);
                }
                else
                {
                    angle = _rng.NextDouble() * Math.PI * 2.0;
                }

                var distance = SpawnMinDistance +
                               (float)_rng.NextDouble() * (SpawnMaxDistance - SpawnMinDistance);

                var candidate = origin + new Vector3(
                    (float)Math.Cos(angle) * distance, (float)Math.Sin(angle) * distance, 0f);

                Vector3 onRoad;
                try { onRoad = World.GetNextPositionOnStreet(candidate); }
                catch { continue; }

                if (onRoad == Vector3.Zero) continue;
                if (onRoad.DistanceTo(origin) < SpawnMinDistance * 0.6f) continue;

                spot = onRoad;
                return true;
            }

            return false;
        }

        public void RestoreWorld() => Cancel(null);
    }
}
