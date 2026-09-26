using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;
using Control = GTA.Control;

namespace Hoodrich.Locations
{
    /// <summary>
    /// THE BONG AND THE TV in a place he rents -- Apartment E2 and the room at Parkview -- the
    /// way the online apartments have them. Michael asked on 2026-09-26 to smoke the bong and
    /// watch the TV "like the real apartment".
    ///
    /// NOTHING IS BROUGHT IN. The bong and the TV are the room's own: the low-end apartment ships
    /// with both, and they are ordinary props the game hands a script, found by their models the
    /// way Single Player Apartment finds them. The bong is the online apartment's own hit --
    /// anim@safehouse@bong's bong_stage3, a bong in his left hand, eight seconds and then the
    /// high -- the same clip, hold and timing as qb-bong, which is played with the very same
    /// prop_bong_01. The room's bong is hidden while he holds one of his own, so it never leaves
    /// the table, and nothing about the map is moved. The TV is the game's own, the way its ob_tv
    /// script runs one: the screen's render target linked to the set's model, the sound hung on
    /// the set, channel 0 or 1, and the picture drawn into it every frame it is on.
    /// </summary>
    internal sealed class Lounge
    {
        /// <summary>He is in a room of his: the one Parkview rents, or behind a door he rents.</summary>
        public Func<bool> Home;

        /// <summary>Something else has the screen or the button.</summary>
        public Func<bool> Busy;

        /// <summary>The hit, landed: the mod's own weed high. Returns why not, or null.</summary>
        public Func<string, string, string> Smoke;

        private const string BongModel = "prop_bong_01";

        /// <summary>Every TV set the online apartments have, from the low end up. See Single Player Apartment's list.</summary>
        private static readonly string[] Sets =
        {
            "prop_tv_flat_01", "prop_tv_03", "hei_heist_str_avunitl_03", "apa_mp_h_str_avunitm_03",
            "apa_mp_h_str_avunits_01", "apa_mp_h_str_avunits_04", "apa_mp_h_str_avunitm_01",
            "apa_mp_h_str_avunitl_01_b", "apa_mp_h_str_avunitl_04", "ex_prop_ex_tv_flat_01"
        };

        private const string BongDict = "anim@safehouse@bong";
        private const string BongClip = "bong_stage3";

        /// <summary>SKEL_L_Hand, and the bong's place in it: qb-bong's numbers, for this very model.</summary>
        private const int LeftHand = 18905;

        private const float BongReach = 1.2f;
        private const float TvReach = 2.2f;

        /// <summary>The high lands this long into the hit, and the bong goes back this long in.</summary>
        private const int HighAt = 8000;
        private const int DoneAt = 10000;

        private Prop _roomBong;
        private Prop _tv;
        private int _lookAt;

        private int _step;
        private int _stepAt;
        private Prop _held;

        private bool _on;
        private int _channel;
        private int _screen;

        public void Update()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var home = false;
            try { home = Home != null && Home() && me.IsAlive && !me.IsInVehicle(); }
            catch { }

            if (!home)
            {
                Leave();
                return;
            }

            var now = Game.GameTime;

            // The room's own bong and set, looked for twice a second rather than every frame.
            if (now >= _lookAt)
            {
                _lookAt = now + 500;
                Look(me);
            }

            if (_step != 0)
            {
                Hit(me, now);
                return;
            }

            Picture();

            var busy = false;
            try { busy = Busy != null && Busy(); }
            catch { }

            if (busy) return;

            if (Near(me, _roomBong, BongReach))
            {
                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to smoke the bong.");
                if (Pressed(Control.Context)) StartHit(me, now);
                return;
            }

            if (Near(me, _tv, TvReach)) Remote();
        }

        // ---- the bong ------------------------------------------------------------------------

        private void StartHit(Ped me, int now)
        {
            InputGuard.Swallow();

            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, BongDict);
                new Model(BongModel).Request();
            }
            catch { }

            // Facing the table, the way he would pick it up.
            try
            {
                var d = _roomBong.Position - me.Position;
                Function.Call(Hash.SET_ENTITY_HEADING, me.Handle, (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI));
            }
            catch { }

            _step = 1;
            _stepAt = now;
        }

        private void Hit(Ped me, int now)
        {
            // His feet stay where they are for it.
            try
            {
                Game.DisableControlThisFrame(Control.MoveLeftRight);
                Game.DisableControlThisFrame(Control.MoveUpDown);
                Game.DisableControlThisFrame(Control.Sprint);
                Game.DisableControlThisFrame(Control.Jump);
                Game.DisableControlThisFrame(Control.Attack);
                Game.DisableControlThisFrame(Control.Aim);
            }
            catch { }

            if (me.IsRagdoll || !me.IsAlive)
            {
                EndHit(me);
                return;
            }

            var age = now - _stepAt;

            switch (_step)
            {
                case 1:
                    // The clip and the bong both in, or nothing happens yet. Two seconds and it gives up.
                    var ready = false;
                    try { ready = Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, BongDict) && new Model(BongModel).IsLoaded; }
                    catch { }

                    if (!ready)
                    {
                        if (age > 2000)
                        {
                            Log.Info("Lounge: the bong's clip or model would not load.");
                            EndHit(me);
                        }

                        return;
                    }

                    try
                    {
                        var at = me.Position;
                        _held = World.CreateProp(new Model(BongModel), at + Vector3.WorldUp * 0.2f, false, false);

                        if (_held != null && _held.Exists())
                        {
                            Function.Call(Hash.SET_ENTITY_COLLISION, _held.Handle, false, false);
                            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _held.Handle, me.Handle,
                                          Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, LeftHand),
                                          0.10f, -0.25f, 0f, 95f, 190f, 180f,
                                          true, true, false, true, 1, true);
                        }

                        // The one on the table goes while his is in his hand.
                        if (_roomBong != null && _roomBong.Exists())
                            Function.Call(Hash.SET_ENTITY_VISIBLE, _roomBong.Handle, false, false);

                        // Upper body, held on its last frame, over his standing legs: 2 + 16 + 32.
                        Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, BongDict, BongClip, 8f, -8f, -1, 50, 0f, false, false, false);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Lounge: the bong would not start: " + ex.Message);
                        EndHit(me);
                        return;
                    }

                    _step = 2;
                    _stepAt = now;
                    return;

                case 2:
                    if (age < HighAt) return;

                    try
                    {
                        var no = Smoke == null ? null : Smoke("weed", "a hit off the bong");
                        if (!string.IsNullOrEmpty(no)) Notify.Ticker(no + ".");
                    }
                    catch { }

                    _step = 3;
                    return;

                case 3:
                    if (age < DoneAt) return;
                    EndHit(me);
                    return;
            }
        }

        /// <summary>His bong gone, the room's back on the table, his arms his own.</summary>
        private void EndHit(Ped me)
        {
            try
            {
                if (_held != null && _held.Exists()) _held.Delete();
            }
            catch { }

            _held = null;

            try
            {
                if (_roomBong != null && _roomBong.Exists())
                    Function.Call(Hash.SET_ENTITY_VISIBLE, _roomBong.Handle, true, false);
            }
            catch { }

            try
            {
                if (me != null && me.Exists()) Function.Call(Hash.STOP_ANIM_TASK, me.Handle, BongDict, BongClip, 1f);
                Function.Call(Hash.REMOVE_ANIM_DICT, BongDict);
                new Model(BongModel).MarkAsNoLongerNeeded();
            }
            catch { }

            _step = 0;
        }

        // ---- the TV --------------------------------------------------------------------------

        private void Remote()
        {
            if (!_on)
            {
                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to turn the TV on.");
                if (Pressed(Control.Context)) TvOn();
                return;
            }

            // The channel is on the jump button while he is at the set, so the jump is not.
            try { Game.DisableControlThisFrame(Control.Jump); }
            catch { }

            Help.ShowThisFrame("~INPUT_JUMP~ changes the channel. ~INPUT_CONTEXT~ turns the TV off.");

            if (Pressed(Control.Context))
            {
                TvOff();
                return;
            }

            if (Pressed(Control.Jump))
            {
                _channel = _channel == 0 ? 1 : 0;

                try { Function.Call(Hash.SET_TV_CHANNEL, _channel); }
                catch { }
            }
        }

        private void TvOn()
        {
            InputGuard.Swallow();

            if (_tv == null || !_tv.Exists()) return;

            try
            {
                var model = _tv.Model.Hash;

                Function.Call(Hash.SET_TV_AUDIO_FRONTEND, false);
                Function.Call(Hash.ATTACH_TV_AUDIO_TO_ENTITY, _tv.Handle);

                if (!Function.Call<bool>(Hash.IS_NAMED_RENDERTARGET_REGISTERED, "tvscreen"))
                    Function.Call(Hash.REGISTER_NAMED_RENDERTARGET, "tvscreen", false);

                if (!Function.Call<bool>(Hash.IS_NAMED_RENDERTARGET_LINKED, model))
                    Function.Call(Hash.LINK_NAMED_RENDERTARGET, model);

                _screen = Function.Call<int>(Hash.GET_NAMED_RENDERTARGET_RENDER_ID, "tvscreen");

                // Whichever of the two the game's own set would come on at.
                _channel = new Random().Next(2);
                Function.Call(Hash.SET_TV_CHANNEL, _channel);
                Function.Call(Hash.SET_TV_VOLUME, 0f);

                _on = true;
                Log.Info("Lounge: the TV on, " + _tv.Model.Hash.ToString("X8") + ", channel " + _channel + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Lounge: the TV would not come on: " + ex.Message);
                TvOff();
            }
        }

        /// <summary>The picture, drawn into the set's screen. Every frame it is on, or it is black.</summary>
        private void Picture()
        {
            if (!_on) return;

            try
            {
                Function.Call(Hash.SET_TEXT_RENDER_ID, _screen);
                Function.Call(Hash.SET_SCRIPT_GFX_DRAW_ORDER, 4);
                Function.Call(Hash.SET_SCRIPT_GFX_DRAW_BEHIND_PAUSEMENU, true);
                Function.Call(Hash.DRAW_TV_CHANNEL, 0.5f, 0.5f, 1f, 1f, 0f, 255, 255, 255, 255);
                Function.Call(Hash.SET_TEXT_RENDER_ID, Function.Call<int>(Hash.GET_DEFAULT_SCRIPT_RENDERTARGET_RENDER_ID));
            }
            catch { }
        }

        private void TvOff()
        {
            try
            {
                Function.Call(Hash.SET_TV_CHANNEL, -1);

                if (Function.Call<bool>(Hash.IS_NAMED_RENDERTARGET_REGISTERED, "tvscreen"))
                    Function.Call(Hash.RELEASE_NAMED_RENDERTARGET, "tvscreen");
            }
            catch { }

            _on = false;
        }

        // ---- finding them, and leaving -----------------------------------------------------------

        private void Look(Ped me)
        {
            try
            {
                Prop bong = null, tv = null;
                float bongAt = float.MaxValue, tvAt = float.MaxValue;
                var bongHash = new Model(BongModel).Hash;

                foreach (var p in World.GetNearbyProps(me.Position, 6f))
                {
                    if (p == null || !p.Exists() || p == _held) continue;

                    var h = p.Model.Hash;
                    var d = p.Position.DistanceTo(me.Position);

                    if (h == bongHash && d < bongAt)
                    {
                        bong = p;
                        bongAt = d;
                        continue;
                    }

                    foreach (var name in Sets)
                    {
                        if (h != new Model(name).Hash || d >= tvAt) continue;
                        tv = p;
                        tvAt = d;
                    }
                }

                if (_step == 0) _roomBong = bong;

                // A set he has on stays his until he walks off.
                if (!_on) _tv = tv;
            }
            catch { }
        }

        /// <summary>Out of the room: the set off, the hit put away.</summary>
        public void Leave()
        {
            if (_on) TvOff();

            if (_step != 0)
            {
                try { EndHit(Game.Player.Character); }
                catch { _step = 0; }
            }
        }

        private static bool Near(Ped me, Prop p, float reach)
        {
            try { return p != null && p.Exists() && p.Position.DistanceTo(me.Position) <= reach; }
            catch { return false; }
        }

        private static bool Pressed(Control c)
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)c); }
            catch { return false; }
        }
    }
}
