using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>One thing you can say back.</summary>
    internal sealed class DialogueChoice
    {
        public string Label = "";

        /// <summary>Shown under the highlighted choice; the consequence, not a restatement.</summary>
        public string Detail = "";

        public bool Enabled = true;
        public string DisabledReason = "";

        /// <summary>
        /// What happens when it is picked. Returning a node continues the conversation;
        /// returning null ends it.
        /// </summary>
        public Func<DialogueNode> Pick;
    }

    /// <summary>A line of theirs plus everything you can say to it.</summary>
    internal sealed class DialogueNode
    {
        public string Speaker = "";
        public string Line = "";
        public Color SpeakerColour = Palette.Text;

        public readonly List<DialogueChoice> Choices = new List<DialogueChoice>();

        public DialogueNode(string speaker, string line)
        {
            Speaker = speaker;
            Line = line;
        }

        public DialogueNode Say(string label, Func<DialogueNode> pick, string detail = "")
        {
            Choices.Add(new DialogueChoice { Label = label, Detail = detail, Pick = pick });
            return this;
        }

        public DialogueNode SayIf(bool enabled, string blocked, string label,
                                  Func<DialogueNode> pick, string detail = "")
        {
            Choices.Add(new DialogueChoice
            {
                Label = label,
                Detail = detail,
                Pick = pick,
                Enabled = enabled,
                DisabledReason = blocked
            });
            return this;
        }

        /// <summary>Ends the conversation. Every node needs one or the player is trapped.</summary>
        public DialogueNode Leave(string label = "Later.")
        {
            Choices.Add(new DialogueChoice { Label = label, Pick = () => null });
            return this;
        }
    }

    /// <summary>
    /// Talking to somebody, properly.
    ///
    /// The wheel is a gateway and nothing more, so a conversation does not belong on it: you
    /// walk up to a man, he says something, and you pick what to say back. This is that screen
    /// -- a node of theirs, a list of yours, D-pad or arrows to move, Enter to answer.
    ///
    /// It owns the player's controls while open, in the same way the wheel does, so answering
    /// somebody cannot also fire a gun.
    /// </summary>
    internal sealed class Conversation
    {
        /// <summary>Centred: a conversation is a screen you are in, not a corner notification.</summary>
        private static float PanelX => 0.5f - PanelWidth * 0.5f;
        private const float PanelWidth = 0.42f;
        private const float LineHeight = 0.030f;
        private const float ChoiceHeight = 0.032f;
        private const float BodyScale = 0.36f;
        private const float ChoiceScale = 0.36f;

        /// <summary>Ignore input for a moment after opening, or the key that opened it selects.</summary>
        private const int OpenGraceMs = 220;

        private DialogueNode _node;
        private int _selected;
        private int _openedAt;
        private List<string> _wrapped = new List<string>();

        public bool IsOpen => _node != null;

        /// <summary>Who we are talking to, so the caller can end it when they walk away.</summary>
        public object Subject { get; private set; }

        /// <summary>
        /// The two people in the room, so an exchange sounds like one.
        ///
        /// Set by whoever opened the conversation. Reading a wall of text in silence is a menu;
        /// hearing them answer and hearing yourself answer back is a conversation, and it costs
        /// two ambient lines.
        /// </summary>
        public Ped Speaker;

        private static readonly string[] TheirLines =
        {
            "GENERIC_HOWS_IT_GOING", "GENERIC_YES", "CHAT_STATE", "GENERIC_THANKS"
        };

        private static readonly string[] YourLines =
        {
            "GENERIC_YES", "GENERIC_HOWS_IT_GOING", "GENERIC_THANKS"
        };

        private static readonly Random Rng = new Random();

        /// <summary>One ambient line, cutting off whatever they were already saying.</summary>
        private static void Speak(Ped ped, string[] lines)
        {
            if (ped == null || !ped.Exists() || !ped.IsAlive) return;

            try
            {
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, ped.Handle);
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle,
                              lines[Rng.Next(lines.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        public void Open(DialogueNode node, object subject = null)
        {
            if (node == null) return;

            _node = node;
            _subjectSet(subject);
            _selected = FirstEnabled(node);
            _openedAt = Game.GameTime;
            _wrapped = Wrap(node.Line, PanelWidth - 0.03f, BodyScale);

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            // Whoever is talking says something as the page comes up.
            Speak(Speaker, TheirLines);
        }

        private void _subjectSet(object subject) => Subject = subject;

        public void Close()
        {
            _node = null;
            Subject = null;
            Speaker = null;
        }

        private static int FirstEnabled(DialogueNode node)
        {
            for (var i = 0; i < node.Choices.Count; i++)
            {
                if (node.Choices[i].Enabled) return i;
            }
            return 0;
        }

        public void Update()
        {
            if (_node == null) return;

            LockControls();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (Pressed(Control.PhoneCancel)) { Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET"); Close(); }
            else if (Pressed(Control.PhoneSelect)) Commit();
        }

        /// <summary>
        /// Read through the disabled-control path: the controls are locked every frame while
        /// the conversation is up, and a locked control still reports its state.
        /// </summary>
        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private void Move(int step)
        {
            if (_node.Choices.Count == 0) return;

            // Skip past anything he will not let you say, wrapping round.
            for (var i = 0; i < _node.Choices.Count; i++)
            {
                _selected += step;
                if (_selected < 0) _selected = _node.Choices.Count - 1;
                if (_selected >= _node.Choices.Count) _selected = 0;

                if (_node.Choices[_selected].Enabled) break;
            }

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Commit()
        {
            if (_selected < 0 || _selected >= _node.Choices.Count) return;

            var choice = _node.Choices[_selected];
            if (!choice.Enabled)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            // You answer, then they answer back when the next page opens.
            Speak(Game.Player.Character, YourLines);

            DialogueNode next = null;
            try
            {
                if (choice.Pick != null) next = choice.Pick();
            }
            catch (Exception ex)
            {
                Log.Error("Dialogue choice threw; ending the conversation.", ex);
            }

            if (next == null)
            {
                Close();
                return;
            }

            var subject = Subject;
            Open(next, subject);
        }

        private static void LockControls()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.MeleeAttack1);
            Game.DisableControlThisFrame(Control.MeleeAttack2);
            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Enter);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.Sprint);

            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneLeft);
            Game.DisableControlThisFrame(Control.PhoneRight);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (_node == null) return;

            var bodyHeight = _wrapped.Count * LineHeight;
            var choiceHeight = _node.Choices.Count * ChoiceHeight;

            // Grown from the content and centred, like every other panel in the mod. It used to
            // be pinned near the bottom of the screen, which put a conversation you are reading
            // down in the subtitle band and out of step with the readouts and the transfer
            // screen -- so where a panel appears depended on which one it was.
            var total = 0.048f + bodyHeight + 0.012f + choiceHeight + 0.030f;
            var top = Math.Max(0.06f, 0.5f - total * 0.5f);

            Hud.RectFrom(PanelX, top, PanelWidth, total, Color.FromArgb(228, 12, 13, 15));
            Hud.RectFrom(PanelX, top, PanelWidth, 0.0035f, _node.SpeakerColour);

            var y = top + 0.012f;

            Hud.Text(_node.Speaker.ToUpperInvariant(), PanelX + 0.014f, y, 0.36f,
                         _node.SpeakerColour, Hud.FontLabel, centre: false);
            y += 0.034f;

            foreach (var line in _wrapped)
            {
                Hud.Text(line, PanelX + 0.014f, y, BodyScale, Palette.Text, Hud.FontBody, centre: false);
                y += LineHeight;
            }

            y += 0.012f;

            for (var i = 0; i < _node.Choices.Count; i++)
            {
                var choice = _node.Choices[i];
                var picked = i == _selected;

                if (picked)
                {
                    Hud.RectFrom(PanelX, y - 0.004f, PanelWidth, ChoiceHeight, Color.FromArgb(235, 240, 242, 240));
                }

                var colour = !choice.Enabled ? Palette.TextDisabled
                           : picked ? Palette.TextOnHover
                           : Palette.TextDim;

                Hud.Text(picked ? "> " + choice.Label : "  " + choice.Label,
                             PanelX + 0.014f, y, ChoiceScale, colour, Hud.FontBody, centre: false);

                var note = !choice.Enabled ? choice.DisabledReason : picked ? choice.Detail : "";
                if (!string.IsNullOrEmpty(note))
                {
                    Hud.TextRight(note, PanelX + PanelWidth - 0.014f, y, 0.30f,
                                      !choice.Enabled ? Palette.Danger
                                      : picked ? Palette.TextOnHover : Palette.TextDim,
                                      Hud.FontBody);
                }

                y += ChoiceHeight;
            }

            Hud.Text("D-PAD / ARROWS  CHOOSE      ENTER  SAY IT      BACKSPACE  WALK OFF",
                         PanelX + 0.014f, y + 0.004f, 0.28f, Palette.TextDim, Hud.FontLabel, centre: false);
        }

        /// <summary>
        /// Greedy word wrap against the game's own text measurement, so a long line breaks at
        /// the panel edge instead of running off it.
        /// </summary>
        private static List<string> Wrap(string text, float width, float scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            var words = text.Split(' ');
            var current = "";

            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;

                float measured;
                try { measured = Hud.MeasureText(candidate, scale, Hud.FontBody); }
                catch { measured = candidate.Length * scale * 0.011f; }

                if (measured <= width || current.Length == 0)
                {
                    current = candidate;
                    continue;
                }

                lines.Add(current);
                current = word;
            }

            if (current.Length > 0) lines.Add(current);
            return lines;
        }
    }
}
