using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>Where Tanya is up to.</summary>
    internal enum TowState
    {
        None,

        /// <summary>Phone out, thumbs going.</summary>
        Texting,

        /// <summary>Sent. She has not answered yet.</summary>
        Waiting,

        /// <summary>Answered, and somewhere off the end of the street.</summary>
        Coming,

        /// <summary>Near the wreck, lining up on it.</summary>
        Backing,

        /// <summary>Arm down, car on the hook.</summary>
        Hooking,

        /// <summary>Gone, with your car behind her.</summary>
        Towing
    }

    /// <summary>
    /// The recovery truck, and the woman driving it.
    ///
    /// A CAR YOU BOUGHT DOES NOT STOP BEING YOURS BECAUSE IT CAUGHT FIRE. Hao's cars are
    /// permanent and persistent, which is the whole point of them -- and the one thing that
    /// could happen to one had no answer at all. It burned out, and then it sat there being a
    /// blip on your map forever: a marker pointing at a shell, on a car the mod still insisted
    /// you owned. Neither "it is gone" nor "it is fine" ever happened, so it was both.
    ///
    /// So somebody comes and takes it away. She is not a menu -- you text her, she answers when
    /// she has read it, she drives the whole way, she lines up on the wreck, drops the arm and
    /// hooks it, and she leaves with it. The car comes back on Hao's lot afterwards, straight,
    /// which is what a tow yard is FOR and is a better ending than a car that quietly evaporates
    /// when nobody is looking.
    ///
    /// NOTHING HERE KNOWS WHAT AN OWNED CAR IS. It is handed a wreck and it hands one back;
    /// where the record lives and what happens to it is somebody else's business. Which is why
    /// this file can be read on its own and why the recovery could be pointed at any vehicle at
    /// all tomorrow.
    /// </summary>
    internal sealed class TowTruck
    {
        // ---- shape --------------------------------------------------------------

        /// <summary>Close enough to be looking at it, and on foot.</summary>
        private const float PromptRange = 14f;

        /// <summary>How long he stands there with the phone out.</summary>
        private const int TextingMs = 3600;

        /// <summary>
        /// How long before she answers, and how long after that before she is on the road.
        ///
        /// A REPLY THAT ARRIVES INSTANTLY IS NOT A REPLY, it is a vending machine with a
        /// portrait on it. She is driving something when you text her; she reads it at a light.
        /// </summary>
        private const int ReplyMinMs = 4000;
        private const int ReplyMaxMs = 9000;

        private const int SetOffMinMs = 5000;
        private const int SetOffMaxMs = 11000;

        /// <summary>Far enough that she arrives rather than appears.</summary>
        private const float ComeFromMin = 190f;
        private const float ComeFromMax = 300f;

        /// <summary>Close enough to the wreck to start lining up.</summary>
        private const float NearWreck = 22f;

        /// <summary>And close enough to get the hook on it.</summary>
        private const float HookRange = 9f;

        /// <summary>How long the arm takes to come down, and the pause with it on the hook.</summary>
        private const int ArmMs = 2600;
        private const int SettleMs = 2200;

        /// <summary>
        /// How long she drives with it on before any of it is allowed to disappear.
        ///
        /// A tow truck that evaporates the moment it is out of the shot is a tow truck that
        /// never went anywhere. A full minute is long enough to watch her go, follow her if
        /// you feel like it, and lose interest -- and by the end of it she is most of a
        /// district away, which is where a thing is allowed to stop existing.
        /// </summary>
        private const int TowAwayMs = 60000;

        /// <summary>Backing on: how fast, how straight, and how long she is given to do it.</summary>
        private const float BackSpeed = 2.2f;
        private const float SwingRate = 90f;
        private const int BackCapMs = 14000;

        /// <summary>Close enough behind it to stage the reverse from.</summary>
        private const float StageRange = 13f;

        /// <summary>
        /// Nothing in this waits forever.
        ///
        /// Every phase has a deadline because every phase depends on a driver getting somewhere
        /// through traffic, and a driver can be blocked by a bin lorry for the rest of the
        /// session. A tow that gives up and cleans itself away is a small disappointment; one
        /// that leaves a truck parked across a junction until you reload is a bug report.
        /// </summary>
        private const int PhaseCapMs = 150000;

        /// <summary>What she charges. In the ini, because everybody prices this differently.</summary>
        private const int DefaultFee = 500;

        private static readonly string[] Trucks = { "towtruck", "towtruck2" };

        /// <summary>Her, in order of preference. All three are South Central locals.</summary>
        private static readonly string[] Drivers =
            { "a_f_m_soucent_01", "a_f_m_soucent_02", "a_f_y_genhot_01" };

        // ---- wiring -------------------------------------------------------------

        /// <summary>Set by Main: a wreck of yours near the player, or null.</summary>
        public Func<Vector3, Vehicle> Wreck;

        /// <summary>Set by Main: she has driven off with it. Put it back on the lot.</summary>
        public Action<Vehicle> Recovered;

        /// <summary>Set by Main: take the fee. False when he cannot pay it.</summary>
        public Func<int, bool> Charge;

        /// <summary>Set by Main: off while something louder is happening.</summary>
        public Func<bool> Busy;

        /// <summary>Set by Main: what she charges, out of the ini.</summary>
        public Func<int> Fee;

        public TowState State { get; private set; }

        public bool IsRunning => State != TowState.None;

        private readonly Random _rng = new Random();

        private Vehicle _wreck;
        private Vehicle _truck;
        private Ped _tanya;
        private Blip _blip;

        private Vector3 _where;

        private int _at;
        private int _due;
        private int _phaseFrom;

        private bool _texted;
        private bool _held;

        // ---- per-tick -----------------------------------------------------------

        public void Update(Ped player)
        {
            try
            {
                if (player == null || !player.Exists() || !player.IsAlive)
                {
                    if (IsRunning) Give("");
                    return;
                }

                if (!IsRunning)
                {
                    Offer(player);
                    return;
                }

                var now = Game.GameTime;

                // The whole job is about one car. Lose it and there is nothing to recover.
                if (State != TowState.Towing && (_wreck == null || !_wreck.Exists()))
                {
                    Give("The wreck's gone.");
                    return;
                }

                if (now - _phaseFrom > PhaseCapMs)
                {
                    Log.Info("Tow: gave up in " + State + ".");
                    Give("Tanya couldn't get through. Text her again.");
                    return;
                }

                switch (State)
                {
                    case TowState.Texting: Thumbs(player, now); break;
                    case TowState.Waiting: Answer(now); break;
                    case TowState.Coming: Driving(now); break;
                    case TowState.Backing: LineUp(now); break;
                    case TowState.Hooking: Hook(now); break;
                    case TowState.Towing: Leaving(); break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Tow tripped: " + ex.Message);
                Give("");
            }
        }

        // ---- the offer ----------------------------------------------------------

        private void Offer(Ped player)
        {
            if (Busy != null && Busy()) return;
            if (Wreck == null) return;
            if (player.IsInVehicle()) return;

            Vehicle wreck;

            try { wreck = Wreck(player.Position); }
            catch { return; }

            if (wreck == null || !wreck.Exists()) return;
            if (wreck.Position.DistanceTo(player.Position) > PromptRange) return;

            var fee = Fee == null ? DefaultFee : Fee();

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to text Tanya for a tow.  ~g~$" +
                               fee + "~s~");

            if (!Tapped()) return;

            if (Charge != null && !Charge(fee))
            {
                Notify.Failure("you're short. Tanya don't do favours.");
                return;
            }

            _wreck = wreck;
            _where = wreck.Position;

            Begin(TowState.Texting);

            _at = Game.GameTime;
            _texted = false;

            Play(player);
        }

        /// <summary>The phone comes out and he actually types on it.</summary>
        private void Play(Ped player)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, TextDict);

                // Requested and played on the same frame is the one thing that never works --
                // the dictionary is not there yet and the first play is dropped. So it is
                // asked for here and started on the next tick, in Thumbs.
            }
            catch (Exception ex)
            {
                Log.Debug("Could not ask for the texting animation: " + ex.Message);
            }

            _phone.Show(player);
        }

        private const string TextDict = "cellphone@";
        private const string TextClip = "cellphone_text_read_base";

        /// <summary>The phone he is looking at while he texts her. See Core.Handset.</summary>
        private readonly Handset _phone = new Handset();

        private void Thumbs(Ped player, int now)
        {
            if (!_texted)
            {
                _texted = true;

                try
                {
                    if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, TextDict))
                    {
                        Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, TextDict, TextClip,
                                      8f, -8f, TextingMs, 49, 0f, false, false, false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Texting animation failed: " + ex.Message);
                }

                Notify.Ticker("~y~Texting Tanya...~s~");
            }

            if (now - _at < TextingMs) return;

            _phone.Hide();

            try { Function.Call(Hash.CLEAR_PED_TASKS, player.Handle); }
            catch { /* he puts it away on his own */ }

            Begin(TowState.Waiting);
            _due = now + _rng.Next(ReplyMinMs, ReplyMaxMs);
        }

        // ---- her reply ----------------------------------------------------------

        private void Answer(int now)
        {
            if (now < _due) return;

            Notify.Text(Faces.For("Tanya"), "Tanya", "on my way",
                        "got you. im finishing a drop in strawberry then im straight there. " +
                        "dont let nobody else touch it, i know what a hoodrich plate looks like");

            Begin(TowState.Coming);

            _due = now + _rng.Next(SetOffMinMs, SetOffMaxMs);
        }

        // ---- the drive over -----------------------------------------------------

        private void Driving(int now)
        {
            // Still finishing her drop.
            if (_truck == null && now < _due) return;

            if (_truck == null || !_truck.Exists())
            {
                if (!Send()) { Give("Tanya couldn't get a truck out."); }
                return;
            }

            if (_tanya == null || !_tanya.Exists() || !_tanya.IsAlive)
            {
                Give("Something happened to Tanya.");
                return;
            }

            var gap = _truck.Position.DistanceTo(_where);

            if (gap > NearWreck) return;

            Begin(TowState.Backing);
        }

        /// <summary>Puts her on the road, out of sight, with the truck she works out of.</summary>
        private bool Send()
        {
            try
            {
                var start = Somewhere();
                if (start == Vector3.Zero) return false;

                foreach (var name in Trucks)
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    _truck = World.CreateVehicle(model, start);
                    model.MarkAsNoLongerNeeded();

                    if (_truck != null && _truck.Exists()) break;
                }

                if (_truck == null || !_truck.Exists()) return false;

                _truck.IsPersistent = true;
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _truck.Handle);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _truck.Handle, true, true);

                // By name rather than by enum. The enum's spelling of a given ped varies
                // between SHVDN builds and a wrong member is a build error on somebody else's
                // machine; a wrong NAME is one model that will not load out of a list of three.
                Ped made = null;

                foreach (var name in Drivers)
                {
                    var who = new Model(name);
                    if (!who.IsValid || !who.IsInCdImage || !who.Request(2000)) continue;

                    made = World.CreatePed(who, start);
                    who.MarkAsNoLongerNeeded();

                    if (made != null && made.Exists()) break;
                }

                _tanya = made;

                if (_tanya == null || !_tanya.Exists())
                {
                    return false;
                }

                _tanya.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _tanya.Handle, true, true);
                Function.Call(Hash.SET_PED_INTO_VEHICLE, _tanya.Handle, _truck.Handle, -1);

                // She is a recovery driver, not a getaway driver. Left alone by everybody, and
                // she does not join in with anything.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _tanya.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _tanya.Handle, false);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _tanya.Handle, true);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                              _tanya.Handle, _truck.Handle,
                              _where.X, _where.Y, _where.Z, 17f, 786603, 12f);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, _tanya.Handle, 17f);
                Function.Call(Hash.SET_PED_KEEP_TASK, _tanya.Handle, true);

                Mark();

                Log.Info("Tow: Tanya set off from " + start.DistanceTo(_where).ToString("0") + "m out.");

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send the tow truck: " + ex.Message);
                return false;
            }
        }

        private Vector3 Somewhere()
        {
            for (var tries = 0; tries < 14; tries++)
            {
                try
                {
                    var away = ComeFromMin + (float)_rng.NextDouble() * (ComeFromMax - ComeFromMin);
                    var probe = _where.Around(away);

                    var at = World.GetNextPositionOnStreet(probe, true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(_where) < ComeFromMin) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        // ---- lining up ----------------------------------------------------------

        /// <summary>
        /// The last few metres, which are the ones anybody actually watches.
        ///
        /// She is sent to a point just BEHIND the wreck rather than at it, because a tow truck
        /// that drives into the thing it came to collect is a tow truck nobody would call
        /// twice. The reverse on top of that is a temp action rather than a task: reversing
        /// accurately through traffic is not something the driving AI will do on request, and
        /// two seconds of it in roughly the right place is what the manoeuvre looks like.
        /// </summary>
        private void LineUp(int now)
        {
            if (_truck == null || !_truck.Exists() || _tanya == null || !_tanya.Exists())
            {
                Give("");
                return;
            }

            var gap = _truck.Position.DistanceTo(_wreck.Position);

            if (gap <= HookRange)
            {
                Stop();

                Begin(TowState.Hooking);
                _at = now;

                return;
            }

            // Still driving to the staging spot behind it.
            if (!_reversing)
            {
                if (gap > StageRange)
                {
                    if (_held) return;

                    _held = true;

                    try
                    {
                        // Behind it, along its own back end, which is where a truck would sit.
                        var spot = _wreck.Position - _wreck.ForwardVector * 9f;

                        Function.Call(Hash.CLEAR_PED_TASKS, _tanya.Handle);

                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _tanya.Handle,
                                      _truck.Handle, spot.X, spot.Y, spot.Z, 7f, 0,
                                      _truck.Model.Hash, 262144, 3f, true);

                        Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, _tanya.Handle, 7f);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Could not stage the truck: " + ex.Message);
                    }

                    return;
                }

                // Arrived. The driving task is done with; the rest is done by hand.
                _reversing = true;
                _at = now;

                try { Function.Call(Hash.CLEAR_PED_TASKS, _tanya.Handle); }
                catch { /* she is stopping anyway */ }

                return;
            }

            // SHE REVERSES ONTO IT, and this is driven rather than tasked.
            //
            // There is no native that will ask a driver to back accurately onto a specific
            // object. TASK_VEHICLE_TEMP_ACTION has reverse actions and they go in whatever
            // direction the truck happens to be pointing for whatever length of time you name,
            // which is a manoeuvre you cannot aim. So the last nine metres are done by hand:
            // the back end is swung round to face the wreck and the truck is pushed backwards
            // along it, a little at a time, until the hook is over it.
            //
            // Swinging WHILE reversing rather than turning on the spot and then going. A truck
            // that rotates in place and then drives straight is a turret; one whose back end
            // comes round as it moves is a driver reversing.
            if (now - _at > BackCapMs)
            {
                // Close enough is close enough. The attach snaps it into place from here, and
                // a truck that spends fifteen seconds shuffling has stopped being a tow.
                Stop();

                Begin(TowState.Hooking);
                _at = now;

                return;
            }

            try
            {
                // The heading that puts her TAIL at the wreck: the direction from the wreck
                // out to her.
                var out_ = _truck.Position - _wreck.Position;
                var want = (float)(Math.Atan2(out_.X, -out_.Y) * 180.0 / Math.PI);

                var have = _truck.Heading;
                var turn = ((want - have + 540f) % 360f) - 180f;

                var step = SwingRate * 0.033f;
                if (turn > step) turn = step;
                if (turn < -step) turn = -step;

                Function.Call(Hash.SET_ENTITY_HEADING, _truck.Handle, (have + turn + 360f) % 360f);
                Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, _truck.Handle, -BackSpeed);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not back the truck on: " + ex.Message);

                Begin(TowState.Hooking);
                _at = now;
            }
        }

        /// <summary>Puts the handbrake on, so she is still while the arm comes down.</summary>
        private void Stop()
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, _truck.Handle, 0f);
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _tanya.Handle, _truck.Handle, 1, 2000);
            }
            catch
            {
                // She stops when she stops.
            }
        }

        private bool _reversing;

        // ---- the hook -----------------------------------------------------------

        private void Hook(int now)
        {
            var since = now - _at;

            try
            {
                // Down over the first stretch, and left down. 1 is stowed and 0 is dropped, so
                // this counts backwards from the stowed position it arrived in.
                var t = Math.Min(1f, since / (float)ArmMs);

                Function.Call(Hash.SET_VEHICLE_TOW_TRUCK_ARM_POSITION, _truck.Handle, 1f - t);
            }
            catch
            {
                // A truck whose arm will not move still takes the car away.
            }

            if (since < ArmMs) return;

            if (!Function.Call<bool>(Hash.IS_VEHICLE_ATTACHED_TO_TOW_TRUCK,
                                     _truck.Handle, _wreck.Handle))
            {
                try
                {
                    Function.Call(Hash.ATTACH_VEHICLE_TO_TOW_TRUCK, _truck.Handle, _wreck.Handle,
                                  true, 0f, 0f, 0f);

                    Notify.Ticker("~g~She's got it.~s~");
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not hook the wreck on: " + ex.Message);
                    Give("Tanya couldn't get the hook on it.");
                    return;
                }
            }

            if (since < ArmMs + SettleMs) return;

            // Arm back up with it on, and away.
            try { Function.Call(Hash.SET_VEHICLE_TOW_TRUCK_ARM_POSITION, _truck.Handle, 1f); }
            catch { /* cosmetic */ }

            Away();
        }

        private void Away()
        {
            Begin(TowState.Towing);

            try
            {
                var off = Somewhere();
                if (off == Vector3.Zero) off = _where.Around(320f);

                Function.Call(Hash.CLEAR_PED_TASKS, _tanya.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                              _tanya.Handle, _truck.Handle,
                              off.X, off.Y, off.Z, 16f, 786603, 20f);

                Function.Call(Hash.SET_PED_KEEP_TASK, _tanya.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send the tow away: " + ex.Message);
            }

            Notify.Important("~g~Towed.~s~ It'll be back on Hao's lot.");
        }

        private void Leaving()
        {
            if (_truck == null || !_truck.Exists())
            {
                Done();
                return;
            }

            // A FULL MINUTE, however far she gets in it.
            //
            // Distance was the old test and it is the wrong one: she can be held at a light
            // fifty metres away, and a truck that has your car on it and is not going anywhere
            // is not a thing to delete out from under you. Time is what the player is actually
            // measuring here -- she left, and after a while she is gone.
            if (Game.GameTime - _phaseFrom < TowAwayMs) return;

            Done();
        }

        /// <summary>She is out of sight with it, so the pair of them stop existing.</summary>
        private void Done()
        {
            // BOOKED IN BEFORE IT IS DELETED, which is the whole of this ordering. Recovered
            // has to read the plate off the car to know which of your cars this was, and a
            // deleted entity has no plate -- so cleaning up first would hand it a handle to
            // nothing and quietly lose the car it was called to save.
            try { if (Recovered != null) Recovered(_wreck); }
            catch (Exception ex) { Log.Debug("Could not book the recovery in: " + ex.Message); }

            Clean(true);

            State = TowState.None;

            Log.Info("Tow: recovered and away.");
        }

        // ---- housekeeping -------------------------------------------------------

        private void Begin(TowState state)
        {
            State = state;
            _phaseFrom = Game.GameTime;
            _held = false;

            if (state != TowState.Backing) _reversing = false;
        }

        /// <summary>Called off. Everything put back and nothing recovered.</summary>
        public void Give(string why)
        {
            if (!string.IsNullOrEmpty(why)) Notify.Failure(why);

            Clean(false);
            State = TowState.None;
        }

        private void Clean(bool takeTheCar)
        {
            // FIRST, and outside the try below. Everything after this is about her and her
            // truck; this is about the player's own hand, and a handset left attached to it
            // because a vehicle failed to delete is the worst outcome available here -- it
            // survives the job, the mission and the reload.
            _phone.Hide();

            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
                _blip = null;

                if (_truck != null && _truck.Exists())
                {
                    if (_wreck != null && _wreck.Exists())
                    {
                        try
                        {
                            Function.Call(Hash.DETACH_VEHICLE_FROM_TOW_TRUCK,
                                          _truck.Handle, _wreck.Handle);
                        }
                        catch { /* it comes apart when the truck goes */ }
                    }

                    _truck.Delete();
                }

                if (_tanya != null && _tanya.Exists()) _tanya.Delete();

                // The wreck only goes when she has actually taken it. Called off, it stays
                // exactly where it was standing -- which is the whole difference between a tow
                // and a car that vanished while you were looking at your phone.
                if (takeTheCar && _wreck != null && _wreck.Exists()) _wreck.Delete();
            }
            catch (Exception ex)
            {
                Log.Debug("Tow cleanup: " + ex.Message);
            }

            _truck = null;
            _tanya = null;
            _wreck = null;
        }

        private void Mark()
        {
            try
            {
                if (_truck == null || !_truck.Exists()) return;

                _blip = _truck.AddBlip();
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = BlipSprite.TowTruck;
                _blip.Color = BlipColor.Yellow;
                _blip.Scale = 0.8f;
                _blip.Name = "Tanya";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the tow truck: " + ex.Message);
            }
        }

        private bool _down;

        private bool Tapped()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context);
            }
            catch
            {
            }

            var hit = down && !_down;
            _down = down;

            return hit;
        }

        /// <summary>Teardown, so nothing of hers is left in the world when the mod stops.</summary>
        public void RestoreWorld()
        {
            Clean(false);
            State = TowState.None;
        }
    }
}
