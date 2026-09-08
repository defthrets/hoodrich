using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The muffler shop on the corner of Hao's yard: a low-end Los Santos Customs.
    ///
    /// Drive up to the roller door and it asks; say yes and the screen goes down, and it
    /// comes back up on the car in the bay with the shop's menu over it (see ModShopScreen)
    /// and a camera walking slowly round. Everything the menu does goes onto the car as you
    /// look at it, because the car is the preview. Drive out and, if it is one of yours, the
    /// whole kit is read off it and kept with the owned record, so it is stood up with its
    /// rims on (see Kit, OwnedCars).
    ///
    /// NO INTERIOR. The building is a shell with a door that does not open, so the car never
    /// goes through it: the screen goes black on the forecourt and comes back on the bay.
    /// The fade is what says "inside", the way the game's own shops say it.
    ///
    /// TICKED OUTSIDE THE STAND-DOWN. The mod stands down while the screen is faded or the
    /// player has no control, which is exactly the state this thing lives in for a second at
    /// each end -- the first cut of it took the player's control away, the mod stood down,
    /// and the shop sat in its camera for two minutes until he got out. So Main ticks this
    /// every frame regardless, and only the asking waits for a playable game. Nothing here
    /// touches player control; the controls are simply disabled a frame at a time while the
    /// screen is down.
    /// </summary>
    internal sealed class Garage
    {
        private enum Stage { None, Fading, Inside, Leaving }

        /// <summary>Set by Main: off while something louder is happening -- a job with a car to drop here, a war.</summary>
        public Func<bool> Busy;

        /// <summary>Set by Main: the owned record for a car, or null when it is not one of yours.</summary>
        public Func<Vehicle, OwnedCar> OwnedOf;

        /// <summary>Set by Main: writes the save.</summary>
        public Action Save;

        /// <summary>Set by Main: something was bought this visit.</summary>
        public Action Tuned;

        private readonly ModShopScreen _shop;

        /// <summary>The bay in front of the door, and the way a car in it faces. Walked.</summary>
        private static readonly Vector3 Bay = new Vector3(-21.752f, -1677.030f, 28.818f);
        private const float BayHeading = 301.826f;

        /// <summary>How close a driven car has to be to ask.</summary>
        private const float AskRange = 11f;

        private const int FadeMs = 450;
        private const int FadeMostMs = 1400;
        private const float CamFov = 45f;
        private const float Around = 6.5f;

        /// <summary>
        /// How far to one side the camera looks, which is how far the car moves off centre.
        ///
        /// NEGATIVE, so the car sits to the RIGHT of the picture: the menu is down the left
        /// now, and the car belongs in the half of the screen the menu is not in.
        /// </summary>
        private const float Shoulder = -2.1f;
        private const float Up = 1.7f;
        private const float SpinPerSec = 4f;
        private const int Sprite = 72;

        private Stage _stage;
        private Vehicle _car;
        private OwnedCar _owned;
        private int _since;
        private int _cam;
        private float _spin;
        private int _lastTick;
        private bool _bought;
        private Blip _blip;

        public Garage(ModShopScreen shop)
        {
            _shop = shop;
            _shop.Bought += () => _bought = true;
        }

        public bool IsRunning => _stage != Stage.None;

        /// <summary>The spanner on the map, once.</summary>
        public void Mark()
        {
            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(Bay);
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, Sprite);
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.85f;
                _blip.Name = "Hao's Muffler Shop";
                _blip.IsShortRange = true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the muffler shop: " + ex.Message);
            }
        }

        // ---- per tick, every tick -------------------------------------------------

        /// <summary>Every frame. Only the asking needs a playable game; the rest runs while the screen is down.</summary>
        public void Update(bool playable)
        {
            var now = Game.GameTime;

            switch (_stage)
            {
                case Stage.None: if (playable) Asking(); break;
                case Stage.Fading: Fading(now); break;
                case Stage.Inside: Inside(now); break;
                case Stage.Leaving: Leaving(now); break;
            }
        }

        /// <summary>When the pull-in prompt first showed, and the last frame it was drawn.</summary>
        private int _promptSince, _promptLast;

        private void Asking()
        {
            try
            {
                if (Busy != null && Busy()) return;

                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive || !me.IsInVehicle()) return;

                var car = me.CurrentVehicle;
                if (car == null || !car.Exists()) return;
                if (car.Driver != me) return;
                if (car.Position.DistanceTo(Bay) > AskRange) return;
                if (car.Speed > 9f) return;

                // Our own bar rather than the game's yellow box. The stamp restarts whenever
                // the prompt has been away for a moment, so it fades in each time he rolls up.
                var now = Game.GameTime;
                if (now - _promptLast > 250) _promptSince = now;
                _promptLast = now;

                UI.UiKit.Prompt("garage.png", "HAO'S MUFFLER SHOP",
                                (UI.Draw.OnPad ? "D-PAD RIGHT" : "E") + "   PULL IN",
                                UI.UiKit.PromptFade(ref _promptSince, now));

                if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

                Enter(me, car);
            }
            catch (Exception ex)
            {
                Log.Debug("The muffler shop could not ask: " + ex.Message);
            }
        }

        private void Enter(Ped me, Vehicle car)
        {
            _car = car;
            _owned = OwnedOf != null ? OwnedOf(car) : null;
            _bought = false;
            _since = Game.GameTime;

            try
            {
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, me.Handle, car.Handle, 1, 1200);
                Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs);
            }
            catch (Exception ex)
            {
                Log.Debug("The muffler shop could not take the car: " + ex.Message);
            }

            _stage = Stage.Fading;
            Log.Info("Muffler shop: pulling in.");
        }

        private void Fading(int now)
        {
            Still();

            if (!Sane()) { Abort(); return; }
            if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) && now - _since < FadeMostMs) return;

            try
            {
                var h = _car.Handle;

                // Squared up in the bay, still, engine off: a car in a shop.
                Function.Call(Hash.CLEAR_PED_TASKS, Game.Player.Character.Handle);
                Function.Call(Hash.SET_ENTITY_COORDS, h, Bay.X, Bay.Y, Bay.Z, false, false, false, true);
                Function.Call(Hash.SET_ENTITY_HEADING, h, BayHeading);
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, h);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, h, false, true, true);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, h, true);

                // The camera that walks round it, up before the screen comes back.
                _cam = Function.Call<int>(Hash.CREATE_CAM, "DEFAULT_SCRIPTED_CAMERA", true);
                if (_cam != 0)
                {
                    Function.Call(Hash.SET_CAM_FOV, _cam, CamFov);
                    _spin = 40f;
                    _lastTick = now;
                    Place();
                    Function.Call(Hash.SET_CAM_ACTIVE, _cam, true);
                    Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false);
                }

                _shop.Open(_car, _owned != null ? _owned.Name : "");

                Function.Call(Hash.DO_SCREEN_FADE_IN, FadeMs);
            }
            catch (Exception ex)
            {
                Log.Debug("The muffler shop could not open: " + ex.Message);
                Abort();
                return;
            }

            _stage = Stage.Inside;
        }

        private void Inside(int now)
        {
            if (!Sane()) { Abort(); return; }

            var dt = Math.Min(100, now - _lastTick) / 1000f;
            _lastTick = now;
            _spin += SpinPerSec * dt;
            if (_spin >= 360f) _spin -= 360f;

            try { Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME); }
            catch { }

            Place();

            if (_shop.IsOpen) return;

            // The menu closed: out the way it came, behind a fade.
            try { Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs); }
            catch { }

            _since = now;
            _stage = Stage.Leaving;
        }

        private void Leaving(int now)
        {
            Still();

            if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) && now - _since < FadeMostMs) return;

            try
            {
                if (_car != null && _car.Exists())
                {
                    var h = _car.Handle;

                    // Turned round and a few metres back from the door, facing the way out.
                    var rad = BayHeading * (float)Math.PI / 180f;
                    var forward = new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f);
                    var out_ = Bay - forward * 6f;

                    Function.Call(Hash.FREEZE_ENTITY_POSITION, h, false);
                    Function.Call(Hash.SET_ENTITY_COORDS, h, out_.X, out_.Y, out_.Z, false, false, false, true);
                    Function.Call(Hash.SET_ENTITY_HEADING, h, BayHeading + 180f);
                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, h);
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, h, true, true, false);

                    // What it wears now, kept with the owned record.
                    if (_owned != null)
                    {
                        _owned.Kit = Kit.Capture(_car);
                        _owned.PlateStyle = _owned.Kit.PlateStyle;
                        _owned.Paint = _owned.Kit.Paint;
                        _owned.Paint2 = _owned.Kit.Paint2;

                        try { Save?.Invoke(); }
                        catch (Exception ex) { Log.Debug("Could not save the kit: " + ex.Message); }

                        Log.Info("Muffler shop: kit kept on " + _owned.Id + ".");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("The muffler shop could not let the car out: " + ex.Message);
            }

            HandBack();

            if (_bought)
            {
                try { Tuned?.Invoke(); }
                catch { }
            }

            _stage = Stage.None;
        }

        // ---- the camera inside ---------------------------------------------------

        private void Place()
        {
            if (_cam == 0 || _car == null || !_car.Exists()) return;

            var rad = _spin * (float)Math.PI / 180f;
            var at = _car.Position;
            var eye = new Vector3(at.X + (float)Math.Sin(rad) * Around,
                                  at.Y + (float)Math.Cos(rad) * Around,
                                  at.Z + Up);

            Function.Call(Hash.SET_CAM_COORD, _cam, eye.X, eye.Y, eye.Z);

            // AIMED PAST THE CAR, so the car sits in the empty half of the screen rather than
            // behind the menu. The camera looks at a point out to its own right; everything it
            // is actually pointed at therefore lands to the LEFT of centre, which is where the
            // panel is not. Cheaper and steadier than moving the camera sideways, which would
            // only re-centre the car from a different angle.
            var side = new Vector3((float)Math.Cos(rad), -(float)Math.Sin(rad), 0f) * Shoulder;

            Function.Call(Hash.POINT_CAM_AT_COORD, _cam,
                          at.X + side.X, at.Y + side.Y, at.Z + 0.3f);
        }

        /// <summary>While the screen is down: nothing he presses reaches the car.</summary>
        private static void Still()
        {
            try { Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0); }
            catch { }
        }

        // ---- letting go ------------------------------------------------------------

        private bool Sane()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive) return false;
                if (_car == null || !_car.Exists()) return false;
                return me.IsInVehicle(_car);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Something went wrong mid-way: everything handed back, no fade left down.</summary>
        private void Abort()
        {
            Log.Info("Muffler shop: let go early.");

            if (_shop.IsOpen) _shop.Close();

            try
            {
                if (_car != null && _car.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, _car.Handle, false);
            }
            catch
            {
            }

            HandBack();
            _stage = Stage.None;
        }

        private void HandBack()
        {
            try
            {
                if (_cam != 0)
                {
                    Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false);
                    Function.Call(Hash.SET_CAM_ACTIVE, _cam, false);
                    Function.Call(Hash.DESTROY_CAM, _cam, false);
                }

                if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) || Function.Call<bool>(Hash.IS_SCREEN_FADING_OUT))
                {
                    Function.Call(Hash.DO_SCREEN_FADE_IN, FadeMs);
                }
            }
            catch
            {
            }

            _cam = 0;
            _car = null;
            _owned = null;
        }

        /// <summary>Teardown: the same as an abort, plus the blip.</summary>
        public void RestoreWorld()
        {
            if (_stage != Stage.None) Abort();

            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { }

            _blip = null;
        }
    }
}
