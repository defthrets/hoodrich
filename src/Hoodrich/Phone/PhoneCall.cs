using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Phone
{
    /// <summary>
    /// Somebody ringing you, and you deciding whether to pick up.
    ///
    /// A CALL IS A CONVERSATION WITH NOBODY IN FRONT OF YOU, which is why this is small. The
    /// panel already draws a portrait, a name and a line, and it already plays a recording for
    /// whatever it is showing -- so answering is just opening one with no ped attached. All
    /// that is genuinely new is the ringing: a noise, a prompt, and a decision.
    ///
    /// THE LINE IS NAMED RATHER THAN HASHED. A call is authored as a one-off, so it gets an
    /// explicit key and the subtitle can be corrected afterwards without the audio going quiet
    /// -- which matters here more than anywhere, because the words on screen and the words in
    /// the recording were written at different times by different hands.
    ///
    /// AND IT CAN BE MISSED. A phone that will not stop until you answer it is not a phone, it
    /// is a modal dialog with a ringtone. Let it ring out and he texts instead, which loses the
    /// performance but keeps the information -- and is a truer thing for the character to do
    /// than stand there redialling.
    /// </summary>
    internal sealed class PhoneCall
    {
        /// <summary>
        /// The ring, held by id rather than fired and forgotten.
        ///
        /// Remote_Ring LOOPS. Played through the fire-and-forget form there is no handle to
        /// stop it with, so it carries on through the answer, through the conversation, and
        /// out the other side of hanging up -- which is exactly what it did. A looping sound
        /// has to be owned: an id, a stop, and a release.
        /// </summary>
        private int _sound = -1;

        /// <summary>How long he lets it ring before giving up on you.</summary>
        private const int GivesUpMs = 25000;

        /// <summary>Set by Main: brings the handset out ringing, and puts it away.</summary>
        public Action<string, string> ShowCall;
        public Action HideCall;

        /// <summary>Set by Main: the panel a picked-up call opens in.</summary>
        public Conversation Talk;

        /// <summary>Set by Main: what to do when it rings out. Usually a text instead.</summary>
        public Action Missed;

        /// <summary>Set by Main: called once it is over either way, so it never comes twice.</summary>
        public Action Done;

        private string _who = "";
        private string _portrait = "";
        private string _key = "";
        private string _text = "";

        private int _dueAt;
        private int _startedAt;

        private bool _armed;
        private bool _held;

        public bool Ringing { get; private set; }

        /// <summary>
        /// Line one up to come in after a delay.
        ///
        /// A DELAY RATHER THAN NOW, because a phone that rings in the same breath as the thing
        /// that caused it reads as a script firing rather than as somebody hearing your news
        /// and picking up their phone about it.
        /// </summary>
        public void Arm(string who, string portrait, string key, string text, int delayMs)
        {
            if (_armed || Ringing) return;

            _who = who ?? "";
            _portrait = portrait ?? "";
            _key = key ?? "";
            _text = text ?? "";

            _dueAt = Game.GameTime + Math.Max(0, delayMs);
            _armed = true;

            Log.Info("Call from " + _who + " armed for " + (delayMs / 1000) + "s.");
        }

        public void Update(Ped player)
        {
            try
            {
                if (!_armed && !Ringing) return;

                // Not while he is dead, mid-cutscene, or already talking to somebody. A call
                // arriving over the top of a conversation would fight it for the same panel.
                if (player == null || !player.Exists() || !player.IsAlive) return;
                if (Talk != null && Talk.IsOpen) return;
                if (Function.Call<bool>(Hash.IS_CUTSCENE_PLAYING)) return;

                var now = Game.GameTime;

                if (_armed && !Ringing)
                {
                    if (now < _dueAt) return;

                    _armed = false;
                    Ringing = true;
                    _startedAt = now;

                    StartRinging();
                }

                if (!Ringing) return;

                // The handset shows who it is and the two buttons; this only says which
                // keys press them.
                Help.ShowThisFrame("~INPUT_CELLPHONE_RIGHT~  answer          " +
                                   "~INPUT_CELLPHONE_CANCEL~  decline");

                if (Answered())
                {
                    Answer();
                    return;
                }

                // Turning him down is a choice, and it lands where letting it ring out lands:
                // he texts. Refusing to pick up and hearing nothing at all would just read as
                // the mod having lost the call.
                if (Declined())
                {
                    Miss();
                    return;
                }

                if (now - _startedAt >= GivesUpMs) Miss();
            }
            catch (Exception ex)
            {
                Log.Debug("Call went wrong: " + ex.Message);

                Ringing = false;
                _armed = false;

                StopRinging();
            }
        }

        /// <summary>Drop it without answering -- for a wipe, or the mod being switched off.</summary>
        public void Cancel()
        {
            _armed = false;
            Ringing = false;

            StopRinging();
        }

        private void StartRinging()
        {
            try
            {
                _sound = Function.Call<int>(Hash.GET_SOUND_ID);
                Function.Call(Hash.PLAY_SOUND_FRONTEND, _sound,
                              "Remote_Ring", RingSet(), false);

                if (ShowCall != null) ShowCall(_who, _portrait);
            }
            catch
            {
                // A silent phone still shows its prompt.
                _sound = -1;
            }
        }

        /// <summary>
        /// Whose phone is ringing, so it is his own ringtone.
        ///
        /// Each protagonist has his own phone soundset and the game uses it for their calls --
        /// Phone_SoundSet_Default is the generic bleep nobody in the story ever hears. Since
        /// this is Franklin's phone being rung by Franklin's friend, it should sound like it.
        ///
        /// Picked off the model rather than hardcoded, so the one player who swapped character
        /// before answering does not get somebody else's ringtone. Anything that is not one of
        /// the three falls back to the generic, which is right for a phone that is not theirs.
        /// </summary>
        private static string RingSet()
        {
            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    // Model.Hash is signed and these hashes are not, so the casts have to
                    // be unchecked or they will not compile.
                    var model = me.Model.Hash;

                    if (model == unchecked((int)PedHash.Michael)) return "Phone_SoundSet_Michael";
                    if (model == unchecked((int)PedHash.Franklin)) return "Phone_SoundSet_Franklin";
                    if (model == unchecked((int)PedHash.Trevor)) return "Phone_SoundSet_Trevor";
                }
            }
            catch
            {
            }

            return "Phone_SoundSet_Default";
        }

        private void StopRinging()
        {
            if (_sound < 0) return;

            try
            {
                Function.Call(Hash.STOP_SOUND, _sound);
                Function.Call(Hash.RELEASE_SOUND_ID, _sound);
            }
            catch
            {
                // Teardown.
            }

            _sound = -1;

            if (HideCall != null) HideCall();
        }

        private void Answer()
        {
            Ringing = false;
            StopRinging();

            var node = new DialogueNode(_who, _text) { Portrait = _portrait };

            // The panel plays whatever it is showing, so naming the recording here is the whole
            // of "and then the audio plays".
            node.Voiced(_key);
            node.Leave("Hang up.");

            if (Talk != null)
            {
                // NOBODY IS STOOD THERE. Speaker drives the mugshot and the ambient grunts of a
                // real man in front of you; on a call both are wrong, and the portrait carries
                // it instead.
                Talk.Speaker = null;
                Talk.Title = "Call";
                Talk.Open(node);
            }

            Log.Info("Answered " + _who + ".");

            if (Done != null) Done();
        }

        private void Miss()
        {
            Ringing = false;
            StopRinging();

            Log.Info("Missed a call from " + _who + ".");

            if (Missed != null) Missed();
            if (Done != null) Done();
        }

        /// <summary>
        /// The same press that talks to anybody else.
        ///
        /// Held rather than tapped would answer it the moment you walked up to somebody with
        /// the button down, so it wants the edge -- and it reads the disabled control too,
        /// because the phone menu turns that one off while it is open.
        /// </summary>
        private bool Declined()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneCancel)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneCancel)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Back);
            }
            catch
            {
            }

            var pressed = down && !_declineHeld;
            _declineHeld = down;

            return pressed;
        }

        private bool _declineHeld;

        private bool Answered()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
            }

            var pressed = down && !_held;
            _held = down;

            return pressed;
        }
    }
}
