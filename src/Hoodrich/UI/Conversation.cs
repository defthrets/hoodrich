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

        /// <summary>
        /// Optional art for the row, resolved per frame like the wheel's.
        ///
        /// A list of gun names is a list of words; the same list with the guns beside them is
        /// something you can read at a glance, which is what a table on a man's floor would be.
        /// </summary>
        public string IconDict = "";
        public string IconTexture = "";
        public float IconAspect = 1f;
        public bool IconTried;

        /// <summary>Set when this row's art is a blip tag rather than a texture.</summary>
        public string IconBlip = "";

        /// <summary>
        /// A PNG of ours, which wins over anything the game ships.
        ///
        /// The wheel has drawn these since the icons were made; this panel could not, so the
        /// same Icon produced our own art on a wedge and the old shop sprite on a dialogue
        /// row. One thing, two pictures, depending where you happened to be looking at it.
        /// </summary>
        public string IconFile = "";

        /// <summary>Texture names to try, for icons that are not named after their dictionary.</summary>
        public string[] Candidates;

        /// <summary>True when the texture shares its dictionary's name, as weapon art does.</summary>
        public bool SelfNamed;

        public bool HasIcon => !string.IsNullOrEmpty(IconDict) && !string.IsNullOrEmpty(IconTexture);
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

        /// <summary>
        /// Gives the choice just added a weapon's own model art.
        ///
        /// Weapon dictionaries are named after the weapon, so the name is enough -- the panel
        /// resolves it the first time it draws the row.
        /// </summary>
        public DialogueNode WithWeapon(string weaponName)
        {
            if (Choices.Count == 0 || string.IsNullOrEmpty(weaponName)) return this;

            var choice = Choices[Choices.Count - 1];
            choice.IconDict = weaponName;
            choice.SelfNamed = true;
            return this;
        }

        /// <summary>Gives the choice just added one of the wheel's icons.</summary>
        public DialogueNode WithIcon(Icon icon)
        {
            if (Choices.Count == 0 || !icon.IsSet) return this;

            var choice = Choices[Choices.Count - 1];

            // Ours first, exactly as the wheel takes it. Nothing to stream and nothing to
            // resolve: a PNG is square, is authored for this size, and is the thing the row is
            // supposed to be showing rather than a shop sprite that means something near it.
            if (icon.HasFile)
            {
                choice.IconFile = icon.File;
                return this;
            }

            // A blip icon has no texture to stream or resolve -- it is drawn as text.
            if (icon.IsBlip)
            {
                choice.IconBlip = icon.Blip;
                return this;
            }

            choice.IconDict = icon.Dict;
            choice.Candidates = icon.Textures;
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

        /// <summary>And keep holding them for a moment after closing, for the same reason.</summary>
        private const int CloseGraceMs = 320;

        private int _closedAt;

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

        /// <summary>
        /// Lines for the middle of a conversation.
        ///
        /// Deliberately NOT thanks. Thanks is what you say when money changes hands, and using
        /// it for every button press made two men stood in a courtyard sound like they were
        /// completing a transaction over and over. These are the noises people make while
        /// talking: agreeing, thinking about it, asking.
        /// </summary>
        private static readonly string[] TheirLines =
        {
            "GENERIC_HOWS_IT_GOING", "GENERIC_YES", "CHAT_STATE",
            "GENERIC_HI", "SHOP_GREETING", "GENERIC_THANKS"
        };

        /// <summary>
        /// The noises somebody makes while you talk to them, for a conversation that is not
        /// friendly.
        ///
        /// Set by whoever opens the screen. The shared list above used to carry an insult in it,
        /// which meant the man selling you a gun and the man fronting you work both stopped
        /// halfway through a sentence to call you something -- and there is no context in this
        /// class to know when that is right, so it is the caller's to say.
        /// </summary>
        public string[] TheirVoice;

        private string[] Theirs => TheirVoice ?? TheirLines;

        private static readonly string[] YourLines =
        {
            "GENERIC_YES", "GENERIC_HOWS_IT_GOING", "CHAT_STATE", "GENERIC_HI"
        };

        /// <summary>Saved for the end, which is the one place it means something.</summary>
        private static readonly string[] PartingLines =
        {
            "GENERIC_BYE", "GENERIC_THANKS"
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

        /// <summary>
        /// A heading over the whole exchange, for a conversation that is somewhere.
        ///
        /// "GRIMES -- THE TABLE" over a list of guns reads as a place you are stood in; the
        /// same list with only a name over it reads as a man talking. Set by whoever opens it
        /// and drawn in the house script, the same face every other screen in the mod titles
        /// itself with.
        /// </summary>
        public string Title = "";

        public void Open(DialogueNode node, object subject = null)
        {
            if (node == null) return;

            _node = node;
            Subject = subject;
            _selected = FirstEnabled(node);
            _openedAt = Game.GameTime;
            _wrapped = Wrap(node.Line, PanelWidth - 0.03f, BodyScale);

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            // Only on the FIRST page. After that the answer comes from Commit, so that one pick
            // produces one line rather than yours and theirs on top of each other.
            if (_openedFresh)
            {
                _openedFresh = false;
                Speak(Speaker, Theirs);
            }
        }

        public void Close()
        {
            // Both of them, because a goodbye one man says on his own is not a goodbye.
            Speak(Game.Player.Character, PartingLines);
            Speak(Speaker, PartingLines);

            _node = null;
            _closedAt = Game.GameTime;
            Subject = null;
            Speaker = null;
            TheirVoice = null;
            Title = "";
            _openedFresh = true;
            _replyAt = 0;
        }

        /// <summary>True until the opening line has been said, so pages do not re-greet you.</summary>
        private bool _openedFresh = true;

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
            if (_node == null)
            {
                // Still holding the controls for a moment after it shuts.
                //
                // LockControls disables per FRAME, and this method returned before reaching it
                // the instant the node went away -- so the very press that closed the screen
                // arrived in the game world on the next frame and Franklin threw a punch at
                // whoever he had just finished talking to.
                if (Game.GameTime - _closedAt < CloseGraceMs) LockControls();
                return;
            }

            LockControls();

            // His answer, once the beat after your line has passed.
            TickReply();

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

        /// <summary>
        /// When he is due to answer, or 0.
        ///
        /// A beat rather than immediately, because two ambient lines started in the same frame
        /// talk over each other and the game keeps the louder one.
        /// </summary>
        private int _replyAt;

        private const int ReplyDelayMs = 700;

        /// <summary>Plays his answer once it is due. Called every frame the panel is up.</summary>
        private void TickReply()
        {
            if (_replyAt == 0 || Game.GameTime < _replyAt) return;

            _replyAt = 0;

            // Only if there is still a page up. Committing the last choice closes the panel,
            // and a man answering a conversation that has ended is a voice from nowhere.
            if (_node == null) return;

            Speak(Speaker, Theirs);
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

            // You speak, and then HE answers -- rather than the two of you taking it in
            // turns to be the only one who makes a noise.
            //
            // Alternating meant every second page was silent on his side: press Go on, hear
            // yourself, press Go on, hear him. But the words on screen are always HIS, so the
            // page where he says nothing is the page that looks broken. He now replies to
            // every page that has any, and the reply is queued rather than played here so it
            // lands after your line instead of on top of it.
            Speak(Game.Player.Character, YourLines);

            _replyAt = Game.GameTime + ReplyDelayMs;

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
            // 0.062 rather than 0.048: the wordmark went in above the speaker's name and the
            // panel has to be that much taller, or the last line of choices runs off the bottom
            // of its own ground.
            var total = 0.068f + bodyHeight + 0.012f + choiceHeight + 0.030f;
            if (!string.IsNullOrEmpty(Title)) total += 0.036f;
            var top = Math.Max(0.06f, 0.5f - total * 0.5f);

            Hud.RectFrom(PanelX, top, PanelWidth, total, Color.FromArgb(228, 12, 13, 15));
            Hud.RectFrom(PanelX, top, PanelWidth, 0.0035f, _node.SpeakerColour);

            // Same idea as the info panels: the mod first, quietly, then who is speaking.
            Hud.BrandCentre(0.5f, top + 0.017f, 0.022f, Palette.Alpha(Palette.TextDim, 150));

            var y = top + 0.032f;

            if (!string.IsNullOrEmpty(Title))
            {
                Hud.Text(Title.ToUpperInvariant(), PanelX + 0.014f, y - 0.004f, 0.62f,
                         Palette.Text, Hud.FontCursive, centre: false);
                y += 0.036f;
            }

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

                var textX = PanelX + 0.014f;

                // Ours, if there is one. Square and authored for this size, so it needs none
                // of the aspect correction the shipped sprites below do.
                if (!string.IsNullOrEmpty(choice.IconFile) &&
                    Hud.File(choice.IconFile, textX + Hud.ToX(0.022f) * 0.5f, y + 0.012f,
                             0.022f, 0f, colour))
                {
                    textX += Hud.ToX(0.022f) + 0.006f;
                }
                // A blip is text, not a sprite -- blip art has no route through DRAW_SPRITE.
                else if (!string.IsNullOrEmpty(choice.IconBlip))
                {
                    Hud.Text(choice.IconBlip, textX, y + 0.001f, 0.34f, colour,
                             Hud.FontChaletLondon, centre: false);

                    textX += 0.020f;
                }
                else if (ResolveIcon(choice))
                {
                    // Height-boxed the same way the wheel does it, so a long silhouette stays
                    // long and a square one stays square.
                    var aspect = choice.IconAspect;
                    if (aspect < 0.25f || aspect > 4f) aspect = 1f;

                    var iw = Math.Min(0.048f, 0.022f * aspect);

                    Hud.Sprite(choice.IconDict, choice.IconTexture,
                               textX + Hud.ToX(iw) * 0.5f, y + 0.012f,
                               Hud.ToX(iw), 0.022f, 0f, colour);

                    textX += Hud.ToX(iw) + 0.006f;
                }

                Hud.Text(picked ? "> " + choice.Label : "  " + choice.Label,
                             textX, y, ChoiceScale, colour, Hud.FontBody, centre: false);

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
        /// Finds the weapon's own model art, once, and remembers the answer either way.
        ///
        /// Weapon art lives in a dictionary named after the weapon, with the texture named the
        /// same -- so a gun that is in this install answers, and one that is not simply has no
        /// picture and keeps its words.
        /// </summary>
        private static bool ResolveIcon(DialogueChoice choice)
        {
            if (string.IsNullOrEmpty(choice.IconDict)) return false;
            if (choice.HasIcon) return true;
            if (choice.IconTried) return false;

            var dict = choice.SelfNamed ? choice.IconDict.ToLowerInvariant() : choice.IconDict;
            if (!Hud.EnsureTextureDict(dict)) return false;

            float aspect;

            // Weapon art shares its dictionary's name. Everything else carries a list, because
            // which of these the install actually has varies -- and a name that is not there
            // draws nothing at all rather than failing, so it has to be checked first.
            if (choice.SelfNamed)
            {
                if (Hud.HasTexture(dict, dict, out aspect))
                {
                    choice.IconDict = dict;
                    choice.IconTexture = dict;
                    choice.IconAspect = aspect;
                    return true;
                }
            }
            else if (choice.Candidates != null)
            {
                foreach (var name in choice.Candidates)
                {
                    if (!Hud.HasTexture(dict, name, out aspect)) continue;

                    choice.IconTexture = name;
                    choice.IconAspect = aspect;
                    return true;
                }
            }

            choice.IconTried = true;
            return false;
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
