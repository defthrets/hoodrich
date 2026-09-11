using System;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Vernon. Calls himself OG Vee. Nobody else does.
    ///
    /// He is stood against the wall by his dad's old shop door on Strawberry Ave, and the whole
    /// of him is one joke told straight: a man who inherited an electrical business, put a
    /// cocaine operation in the basement of it, and thinks the interesting half of that
    /// sentence is that he raps.
    ///
    /// HE IS NOT A DEALER AND HE IS NOT A FIXER. The mod already has both, several times over,
    /// and a fourth man selling weight would be a fourth menu. Vernon wants an AUDIENCE. The
    /// work is what he offers you so you will stand there long enough to hear the second verse.
    ///
    /// HE IS ALSO THE LOCK ON THE BASEMENT. The door beside him goes into the grow, and it does
    /// not open until his job is done -- so he is not decoration on a door that already worked,
    /// he is the reason the door does anything. See Main, where his mission id is handed to the
    /// InteriorDoor as its gate.
    ///
    /// Modelled on the old San Andreas rapper who could not rap, deliberately and without ever
    /// naming him. The joke only works if it is played completely straight: he is not winking
    /// at you, he genuinely thinks the bars are good, and everything he says about them has to
    /// be said like a man who believes it.
    /// </summary>
    internal sealed class Vernon
    {
        /// <summary>
        /// Against the wall by the shop door, facing out at the street.
        ///
        /// Stood on and read off the screen, like both ends of the door beside him. His heading
        /// is very nearly the door's own reversed, which is what "back to the wall" means -- so
        /// he is leaning on the shutter he grew up behind rather than stood in the middle of
        /// the dirt with his arms folded.
        /// </summary>
        private static readonly Vector3 Spot = new Vector3(51.057f, -1452.677f, 29.312f);
        private const float Heading = 51.272f;

        private const float SpawnRange = 90f;
        private const float DespawnRange = 160f;
        private const float TalkRange = 3.0f;

        private const int UpdateIntervalMs = 700;

        /// <summary>
        /// The model, and the cutscene copy under it.
        ///
        /// ig_vernon is the man himself; csb_vernon is the same face built for a cutscene and is
        /// there for an install that is missing the first. Both checked against the machine's
        /// own ped list rather than remembered.
        /// </summary>
        private static readonly string[] Models = { "ig_vernon", "csb_vernon" };

        /// <summary>
        /// Leaning on the wall having a cigarette.
        ///
        /// WORLD_HUMAN_AA_SMOKE is the one scenario in the game of somebody with their back
        /// against a wall and a smoke in their hand -- it is what the alleys off Vespucci are
        /// full of -- and it is the picture asked for. WORLD_HUMAN_SMOKING under it stands the
        /// same man upright in the same place, which is the honest fallback: a lean needs a
        /// wall behind the ped and there is no way to check for one.
        ///
        /// Both names off the machine's own scenario list. Neither is suffixed _UPRIGHT, which
        /// is the suffix that means "do this one WITHOUT leaning" -- so the un-suffixed name is
        /// the one that leans, which is the way round it always catches people out.
        /// </summary>
        private static readonly string[] Leaning = { "WORLD_HUMAN_AA_SMOKE", "WORLD_HUMAN_SMOKING" };

        private readonly PlayerState _state;

        /// <summary>
        /// 497 -- radar_production_crack, the razor blade over lines of powder.
        ///
        /// NOT 51. That is radar_crim_drugs, and it draws as a capsule -- the reference and the
        /// ini both called it "the razor and the line" and both were wrong, which is how he
        /// stood there wearing a pill. 497 is the cocaine-lockup mark from the Bikers business
        /// and it is the one picture the game has of what is actually in his basement.
        ///
        /// THE ONLY MARK ON THAT CORNER. The door beside him used to carry one too, on the
        /// same spot; the man you walk up to is the thing worth pointing at, so the door's is
        /// off and this is on the minimap as well as the big map.
        /// </summary>
        private const int Sprite = 497;

        private Ped _ped;
        private Blip _blip;
        private int _lastUpdate;
        private bool _held;
        private bool _talkHeld;
        private int _leaning;

        public Vernon(PlayerState state)
        {
            _state = state;
        }

        public string Name => "Vernon";
        public Vector3 Position => Spot;
        public Ped Ped => _ped != null && _ped.Exists() ? _ped : null;

        /// <summary>Set by Main.</summary>
        public Conversation Talk;
        public Func<DialogueNode> TalkBuilder;

        /// <summary>
        /// Set by Main: true while something else has the context button.
        ///
        /// He stands a stride and a half from the door into the basement, and the door uses the
        /// same button he does. Without this, walking up to the shop offers you a conversation
        /// and a doorway at once and the button picks one of them.
        /// </summary>
        public Func<bool> Suppressed;

        public bool InReach
        {
            get
            {
                if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return false;

                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return false;

                return player.Position.DistanceTo(_ped.Position) <= TalkRange;
            }
        }

        // ---- per frame ---------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            EnsureBlip();

            var away = player.Position.DistanceTo(Spot);

            if (away > DespawnRange)
            {
                Despawn();
                return;
            }

            if (away > SpawnRange) return;

            if (_ped == null || !_ped.Exists()) Spawn();
            else if (!_held) Settle();
        }

        private void Spawn()
        {
            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _ped = World.CreatePed(model, Spot, Heading);
                    model.MarkAsNoLongerNeeded();

                    if (_ped == null || !_ped.Exists()) continue;

                    var h = _ped.Handle;

                    _ped.IsPersistent = true;
                    _ped.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL, h, false);
                    Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, h, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);

                    Settle();

                    Log.Info("Vernon is on the wall at Leroy's (" + name + ").");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put Vernon out: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Back on the wall.
        ///
        /// The scenario is asked for by NAME and the first one that takes wins, remembered so a
        /// re-settle after a conversation does not walk the list again -- an install where the
        /// lean is missing would otherwise pay for the failed call every time you walked away
        /// from him.
        /// </summary>
        private void Settle()
        {
            for (var i = _leaning; i < Leaning.Length; i++)
            {
                try
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle, Leaning[i], 0, true);

                    _leaning = i;
                    _held = true;
                    return;
                }
                catch
                {
                    // Try the next way of standing there.
                }
            }

            _held = false;
        }

        // ---- talking to him ----------------------------------------------------

        /// <summary>
        /// The prompt, and opening the conversation off it.
        ///
        /// He gets the button only when nothing else nearer wants it. See Suppressed: the
        /// basement door is a stride behind his shoulder.
        /// </summary>
        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen) return;
            if (!InReach) return;
            if (Suppressed != null && Suppressed()) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to talk to " +
                               (Knows ? "OG Vee" : "the man on the wall") + ".");

            if (!WantsToTalk()) return;

            var root = TalkBuilder == null ? null : TalkBuilder();
            if (root == null) return;

            HoldForTalk();

            Talk.Speaker = _ped;
            Talk.Open(root, this);
        }

        /// <summary>Whether you have been introduced, which is the only thing the prompt knows.</summary>
        private bool Knows => _state != null && _state.MetVernon;

        private bool WantsToTalk()
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

        /// <summary>
        /// Off the wall and turned round to you.
        ///
        /// He is the one man in the mod where this costs something: the lean IS the character,
        /// and standing him up to talk loses it. It is still right -- a man delivering his own
        /// verses at a wall with his back to you is a different joke -- and Settle puts him
        /// straight back on it the moment the screen closes.
        /// </summary>
        public void HoldForTalk()
        {
            if (_ped == null || !_ped.Exists() || !_held) return;

            _held = false;

            try
            {
                var player = Game.Player.Character;

                _ped.Task.ClearAll();

                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _ped.Handle, player.Handle, -1);
                }
            }
            catch
            {
                // He will still talk.
            }
        }

        public void ReleaseFromTalk()
        {
            if (_held || _ped == null || !_ped.Exists()) return;
            Settle();
        }

        // ---- map ---------------------------------------------------------------

        /// <summary>
        /// His mark on the wall, put up once and left alone.
        ///
        /// NAMED FOR WHAT YOU KNOW. Before you have met him it is the shop, because that is
        /// all it is from the street; after, it is him, because by then the shop is the least
        /// interesting thing about it.
        /// </summary>
        private void EnsureBlip()
        {
            if (_blip != null && _blip.Exists())
            {
                var should = Knows ? "OG Vee" : "Leroy's Electrical";
                if (_blip.Name != should) _blip.Name = should;
                return;
            }

            try
            {
                _blip = World.CreateBlip(Spot);
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, Sprite);
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.8f;
                _blip.IsShortRange = true;
                _blip.Name = Knows ? "OG Vee" : "Leroy's Electrical";
            }
            catch (Exception ex)
            {
                Log.Debug("No blip for Vernon: " + ex.Message);
            }
        }

        // ---- teardown ----------------------------------------------------------

        private void Despawn()
        {
            _held = false;

            try
            {
                if (_ped != null && _ped.Exists())
                {
                    _ped.IsPersistent = false;
                    _ped.MarkAsNoLongerNeeded();
                    _ped.Delete();
                }
            }
            catch { /* he will be back */ }

            _ped = null;
        }

        public void RestoreWorld()
        {
            Despawn();

            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch { /* teardown */ }

            _blip = null;
        }
    }
}
