using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Den
{
    /// <summary>
    /// The casino's animations, played the way the casino plays them: synchronised scenes
    /// anchored on the table.
    ///
    /// EVERY CASINO CLIP IS AUTHORED AGAINST THE TABLE. The dealer's idle, the wheel's spin, the
    /// ball's drop into pocket nineteen, the player pulling the lever -- none of them carry a
    /// position of their own. They are all offsets from one origin, and that origin is the
    /// table or the machine. Play a dealer's idle on a ped stood wherever, and she stands a
    /// metre off the felt with her hands over nothing. Put the same clip in a scene whose
    /// origin is the table's position and rotation, and she is exactly where the animators
    /// put her. So there is no dealer coordinate anywhere in this folder, and no offset
    /// anybody had to guess at: the anchor is the prop, and the clip does the rest.
    ///
    /// SCENES END THEMSELVES. ScriptHookVDotNet 3.6 has no DISPOSE_SYNCHRONIZED_SCENE, so a
    /// scene has to finish on its own, which one does once nothing is playing in it: a
    /// non-looped clip runs to its end and lets go, a looped one is let go of by tasking its
    /// entities into the next scene. Nothing here holds a scene it is not using.
    /// </summary>
    internal static class Scene
    {
        /// <summary>The dictionaries, by the job they do. Requested together on the way in.</summary>
        public const string RouletteDealer = "anim_casino_b@amb@casino@games@roulette@dealer_female";
        public const string RouletteTable = "anim_casino_b@amb@casino@games@roulette@table";
        public const string BlackjackDealer = "anim_casino_b@amb@casino@games@blackjack@dealer_female";
        public const string SharedDealer = "anim_casino_b@amb@casino@games@shared@dealer@";
        public const string PokerDealer = "anim_casino_b@amb@casino@games@threecardpoker@dealer";
        public const string SlotsMale = "anim_casino_a@amb@casino@games@slots@male";
        public const string SlotsFemale = "anim_casino_a@amb@casino@games@slots@female";

        /// <summary>The player in a casino chair: sitting down, the seated idles, the reactions, getting up.</summary>
        public const string SharedPlayer = "anim_casino_b@amb@casino@games@shared@player@";

        /// <summary>The player's side of three card poker: the chips down, the cards picked up, played or folded.</summary>
        public const string PokerPlayer = "anim_casino_b@amb@casino@games@threecardpoker@player";

        /// <summary>
        /// The blackjack dealer's full set: the woman's clips are the "female_" ones in here, and
        /// only this set has the hole card (female_deal_card_self_second_card), which the
        /// dealer_female set does not. Played where she stands -- see Dealer.
        /// </summary>
        public const string BlackjackDealerAll = "anim_casino_b@amb@casino@games@blackjack@dealer";

        /// <summary>The player's side of blackjack: the chips down, a tap for a card, a wave to stand, the double.</summary>
        public const string BlackjackPlayer = "anim_casino_b@amb@casino@games@blackjack@player";

        public static readonly string[] All =
        {
            RouletteDealer, RouletteTable, BlackjackDealer, SharedDealer, PokerDealer, SlotsMale, SlotsFemale,
            SharedPlayer, PokerPlayer, BlackjackDealerAll, BlackjackPlayer
        };

        private static readonly HashSet<string> Asked = new HashSet<string>();

        /// <summary>Asks for a dictionary, once. Whether it has arrived is Loaded's question.</summary>
        public static void Request(string dict)
        {
            if (string.IsNullOrEmpty(dict) || !Asked.Add(dict)) return;

            try { Function.Call(Hash.REQUEST_ANIM_DICT, dict); }
            catch (Exception ex) { Log.Debug("Den: could not ask for " + dict + ": " + ex.Message); }
        }

        public static bool Loaded(string dict)
        {
            Request(dict);

            try { return Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict); }
            catch { return false; }
        }

        /// <summary>Every dictionary the den uses, in. Asked for on the way in; true once they are all here.</summary>
        public static bool AllLoaded()
        {
            var ready = true;

            foreach (var d in All)
            {
                if (!Loaded(d)) ready = false;
            }

            return ready;
        }

        /// <summary>Let go of them on the way out, so the memory is not held all night.</summary>
        public static void Forget()
        {
            foreach (var d in All)
            {
                try { Function.Call(Hash.REMOVE_ANIM_DICT, d); }
                catch { }
            }

            Asked.Clear();
        }

        /// <summary>
        /// A scene on an entity: its position and its rotation, rotation order 2, which is the
        /// order the rotation was read in. -1 if the game would not make one.
        /// </summary>
        public static int At(Entity anchor)
        {
            if (anchor == null || !anchor.Exists()) return -1;

            try
            {
                var p = anchor.Position;
                var r = anchor.Rotation;

                return Function.Call<int>(Hash.CREATE_SYNCHRONIZED_SCENE, p.X, p.Y, p.Z, r.X, r.Y, r.Z, 2);
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not make a scene: " + ex.Message);
                return -1;
            }
        }

        /// <summary>
        /// A scene on a position and a full rotation -- a chair's bone, which is where every clip
        /// of a player at a casino table is authored from.
        /// </summary>
        public static int At(Vector3 at, Vector3 rot)
        {
            try
            {
                return Function.Call<int>(Hash.CREATE_SYNCHRONIZED_SCENE, at.X, at.Y, at.Z, rot.X, rot.Y, rot.Z, 2);
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not make a scene: " + ex.Message);
                return -1;
            }
        }

        /// <summary>The same, on a position and a heading rather than a thing.</summary>
        public static int At(Vector3 at, float heading)
        {
            try
            {
                return Function.Call<int>(Hash.CREATE_SYNCHRONIZED_SCENE, at.X, at.Y, at.Z, 0f, 0f, heading, 2);
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not make a scene: " + ex.Message);
                return -1;
            }
        }

        /// <summary>A ped into a scene with a clip. Looped or run once; held on the last frame if asked.</summary>
        public static bool Ped(int scene, GTA.Ped ped, string dict, string clip, bool loop, bool holdLast = false)
        {
            if (scene < 0 || ped == null || !ped.Exists()) return false;

            try
            {
                Function.Call(Hash.SET_SYNCHRONIZED_SCENE_LOOPED, scene, loop);
                Function.Call(Hash.SET_SYNCHRONIZED_SCENE_HOLD_LAST_FRAME, scene, holdLast);

                // Blend in fast, blend out fast, no ragdoll-on-collision, no IK flags. The
                // casino's own scripts use the same numbers for the same reason: a dealer who
                // eases into her idle over half a second is a dealer who was doing something
                // else, and there is nothing else.
                Function.Call(Hash.TASK_SYNCHRONIZED_SCENE, ped.Handle, scene, dict, clip,
                              1000f, -1000f, 0, 0, 1000f, 0);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not task " + clip + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The player into a scene. Not the dealer's numbers: he blends in and out over a
        /// quarter of a second, because a man sitting down in one frame is a man teleported,
        /// and his flags are the casino's own for a seated player -- physics on, nothing
        /// interrupts it, and the scene stops if he is pulled out of it.
        /// </summary>
        public static bool Player(int scene, GTA.Ped ped, string dict, string clip, bool loop, bool holdLast = false)
        {
            if (scene < 0 || ped == null || !ped.Exists()) return false;

            try
            {
                Function.Call(Hash.SET_SYNCHRONIZED_SCENE_LOOPED, scene, loop);
                Function.Call(Hash.SET_SYNCHRONIZED_SCENE_HOLD_LAST_FRAME, scene, holdLast);
                Function.Call(Hash.TASK_SYNCHRONIZED_SCENE, ped.Handle, scene, dict, clip,
                              4f, -4f, 13, 16, 1000f, 0);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not sit the player in " + clip + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// A prop playing a clip on its own, not in anybody's scene: the roulette wheel turning
        /// and the ball going round it, the way the casino drives them. Held on the last frame,
        /// so the ball stays in its pocket until it is taken away.
        /// </summary>
        public static bool Entity(Entity e, string dict, string clip, bool loop)
        {
            if (e == null || !e.Exists()) return false;

            try
            {
                Function.Call(Hash.PLAY_ENTITY_ANIM, e.Handle, clip, dict, 1000f, loop, true, true, 0f, 136704);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not play " + clip + " on a prop: " + ex.Message);
                return false;
            }
        }

        /// <summary>How far through a clip a prop is, 0 to 1. Past 1 when it is not playing it at all.</summary>
        public static float EntityPhase(Entity e, string dict, string clip)
        {
            if (e == null || !e.Exists()) return 2f;

            try
            {
                if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, e.Handle, dict, clip, 3)) return 2f;
                return Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, e.Handle, dict, clip);
            }
            catch
            {
                return 2f;
            }
        }

        /// <summary>A prop's own clip stopped, and it back at rest.</summary>
        public static void StopEntity(Entity e, string dict, string clip)
        {
            if (e == null || !e.Exists()) return;

            try { Function.Call(Hash.STOP_ENTITY_ANIM, e.Handle, clip, dict, -1000f); }
            catch { }
        }

        /// <summary>Whether a scene loops, and whether it holds its last frame when it ends.</summary>
        public static void Set(int scene, bool loop, bool holdLast)
        {
            if (scene < 0) return;

            try
            {
                Function.Call(Hash.SET_SYNCHRONIZED_SCENE_LOOPED, scene, loop);
                Function.Call(Hash.SET_SYNCHRONIZED_SCENE_HOLD_LAST_FRAME, scene, holdLast);
            }
            catch { }
        }

        /// <summary>A prop into a scene with a clip: the wheel, the ball, the lever.</summary>
        public static bool Prop(int scene, Entity prop, string dict, string clip)
        {
            if (scene < 0 || prop == null || !prop.Exists()) return false;

            try
            {
                Function.Call(Hash.PLAY_SYNCHRONIZED_ENTITY_ANIM, prop.Handle, scene, clip, dict,
                              1000f, -1000f, 0, 1000f);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not play " + clip + " on a prop: " + ex.Message);
                return false;
            }
        }

        /// <summary>Whether a scene is still going. A finished non-looped scene answers no.</summary>
        public static bool Running(int scene)
        {
            if (scene < 0) return false;

            try { return Function.Call<bool>(Hash.IS_SYNCHRONIZED_SCENE_RUNNING, scene); }
            catch { return false; }
        }

        /// <summary>Where a scene has got to, 0 to 1. Past 1 for one that has finished.</summary>
        public static float Phase(int scene)
        {
            if (scene < 0) return 2f;

            try { return Function.Call<float>(Hash.GET_SYNCHRONIZED_SCENE_PHASE, scene); }
            catch { return 2f; }
        }

        /// <summary>A scene that has run its course: not running any more, or run to the end.</summary>
        public static bool Done(int scene)
        {
            if (scene < 0) return true;
            if (!Running(scene)) return true;
            return Phase(scene) >= 0.999f;
        }

        /// <summary>Takes a prop out of whatever scene it is in.</summary>
        public static void Stop(Entity prop)
        {
            if (prop == null || !prop.Exists()) return;

            try { Function.Call(Hash.STOP_SYNCHRONIZED_ENTITY_ANIM, prop.Handle, 1000f, true); }
            catch { }
        }
    }

    /// <summary>
    /// The buttons, read the way the phone reads them: as disabled controls.
    ///
    /// While a game is up every game control is disabled -- so the phone does not open on the
    /// same key that places a bet, and so a wrong press does not throw a punch at the dealer --
    /// and a disabled control still answers IS_DISABLED_CONTROL_JUST_PRESSED. The arrows are
    /// the phone's arrows, Enter is the phone's select, Backspace the phone's back.
    /// </summary>
    internal static class Keys
    {
        public static bool Up => JustPressed(Control.PhoneUp);
        public static bool Down => JustPressed(Control.PhoneDown);
        public static bool Left => JustPressed(Control.PhoneLeft);
        public static bool Right => JustPressed(Control.PhoneRight);
        public static bool Select => JustPressed(Control.PhoneSelect);
        public static bool Back => JustPressed(Control.PhoneCancel);

        /// <summary>Space: the spin, the deal. INPUT_JUMP read disabled, so it is Space on a keyboard and A on a pad.</summary>
        public static bool Space => JustPressed(Control.Jump);

        /// <summary>Q and E, the frontend shoulders, or the mouse wheel: a chip down or up.</summary>
        public static bool Lower => JustPressed(Control.FrontendLb) || JustPressed(Control.WeaponWheelPrev);
        public static bool Raise => JustPressed(Control.FrontendRb) || JustPressed(Control.WeaponWheelNext);

        /// <summary>The mouse buttons, for the roulette felt: a chip down, a chip back.</summary>
        public static bool Click => JustPressed(Control.Attack);
        public static bool RightClick => JustPressed(Control.Aim);

        /// <summary>An arrow held down, for a cursor that keeps going while the key does.</summary>
        public static bool HeldUp => Held(Control.PhoneUp);
        public static bool HeldDown => Held(Control.PhoneDown);
        public static bool HeldLeft => Held(Control.PhoneLeft);
        public static bool HeldRight => Held(Control.PhoneRight);

        /// <summary>How far the mouse moved this frame, as the game measures it: the look axes.</summary>
        public static float MouseX => Normal(Control.LookLeftRight);
        public static float MouseY => Normal(Control.LookUpDown);

        private static bool Held(Control c)
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)c); }
            catch { return false; }
        }

        private static float Normal(Control c)
        {
            try { return Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)c); }
            catch { return 0f; }
        }

        private static bool JustPressed(Control c)
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)c); }
            catch { return false; }
        }

        /// <summary>
        /// Everything off but the camera, this frame. The scene holds him; this holds the
        /// rest of the game's hands off the keyboard.
        /// </summary>
        public static void HoldTheGame()
        {
            try
            {
                Game.DisableAllControlsThisFrame();
                Game.EnableControlThisFrame(Control.LookLeftRight);
                Game.EnableControlThisFrame(Control.LookUpDown);
                Game.EnableControlThisFrame(Control.NextCamera);
            }
            catch { }
        }
    }
}
