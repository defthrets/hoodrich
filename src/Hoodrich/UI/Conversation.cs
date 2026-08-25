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

        /// <summary>
        /// A small mark after the label, for a fact about the row rather than a picture of it.
        ///
        /// The art on the left says WHAT the row is. This says how strong it is, and they are
        /// different questions -- a bag of weed and a bag of weed somebody has cut in half
        /// share an icon and are not the same thing to buy. It goes after the words because
        /// that is where the stash and the cook screen put it, and one mark should mean one
        /// thing wherever it turns up.
        /// </summary>
        public string MarkFile = "";

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

        /// <summary>
        /// The texture dictionary of the speaker's photograph, or empty to work it out.
        ///
        /// An override rather than the usual way. Almost every node in the mod already says
        /// who is talking, and a name is enough to find a face -- so this exists for the case
        /// where two people share a name, or somebody wants a different picture for one line,
        /// rather than as a field every builder has to remember to fill in.
        /// </summary>
        public string Portrait = "";

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
        /// The same, for a row that is shown but cannot be picked.
        ///
        /// The choice has carried Enabled and DisabledReason since it was written and there was
        /// no way to set them from here, so every list in the mod either offered a thing or hid
        /// it. Greyed out with a reason on it is the third answer, and it is usually the right
        /// one: a locked row you can see is a reason to go and earn something.
        /// </summary>
        public DialogueNode Say(string label, Func<DialogueNode> pick, string detail,
                                bool enabled, string disabledReason = "")
        {
            Choices.Add(new DialogueChoice
            {
                Label = label,
                Detail = detail,
                Pick = pick,
                Enabled = enabled,
                DisabledReason = disabledReason
            });

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

        /// <summary>Puts a mark after the label of the choice just added.</summary>
        public DialogueNode WithMark(string markFile)
        {
            if (Choices.Count == 0 || string.IsNullOrEmpty(markFile)) return this;

            Choices[Choices.Count - 1].MarkFile = markFile;
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

        /// <summary>The speaker's photograph for this node, or empty for none.</summary>
        private string _face = "";

        /// <summary>
        /// A headshot of whoever is actually talking, which beats any name.
        ///
        /// REGISTER_PEDHEADSHOT renders the ped's own head to a texture -- the same machinery
        /// the socials screen uses for you. It is the only way to be certain the picture is the
        /// man: a contact texture is a guess at a string, and one of those guesses put a dog on
        /// Lamar's dialogue for a fortnight.
        ///
        /// It is not instant. The render takes a moment, so the contact picture shows first and
        /// is replaced the frame the real one is ready -- which on a conversation you are
        /// reading is invisible, and on one you skip through costs nothing.
        /// </summary>
        private int _shot;
        private string _shotOf = "";
        private string _shotTxd = "";

        private void Mugshot()
        {
            var who = Speaker;

            if (who == null || !who.Exists() || !who.IsAlive)
            {
                DropMugshot();
                return;
            }

            // A different man means a different photograph. Handles are reused, so the check is
            // against the handle we took THIS one for.
            var id = who.Handle.ToString();

            if (_shotOf != id)
            {
                DropMugshot();
                _shotOf = id;

                try { _shot = Function.Call<int>(Hash.REGISTER_PEDHEADSHOT, who.Handle); }
                catch (Exception ex) { Log.Debug("No headshot for the speaker: " + ex.Message); }
            }

            if (_shot == 0 || !string.IsNullOrEmpty(_shotTxd)) return;

            try
            {
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_READY, _shot)) return;
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_VALID, _shot)) return;

                _shotTxd = Function.Call<string>(Hash.GET_PEDHEADSHOT_TXD_STRING, _shot);
            }
            catch
            {
                // The contact picture stands in.
            }
        }

        /// <summary>
        /// Hands the render back.
        ///
        /// There are a limited number of these and a leaked one is gone for the session, so
        /// every path that stops needing it says so -- a new speaker, a closed conversation,
        /// and the mod shutting down.
        /// </summary>
        private void DropMugshot()
        {
            try { if (_shot != 0) Function.Call(Hash.UNREGISTER_PEDHEADSHOT, _shot); }
            catch { /* teardown */ }

            _shot = 0;
            _shotOf = "";
            _shotTxd = "";
        }

        /// <summary>How tall the photograph is, and how far it pushes the words across.</summary>
        private const float FaceSize = 0.062f;
        private static float TextInset => Hud.ToX(FaceSize) + 0.012f;

        /// <summary>
        /// Whose face goes with a name.
        ///
        /// Looked up from the speaker rather than carried on every node, which is the only way
        /// this reaches all of them: there are dozens of nodes across Lamar, Gerald, Stretch,
        /// Tao and the rest, they are built in half a dozen files, and none of them would ever
        /// be revisited to add a field. A name they all already set is the one thing they have
        /// in common.
        ///
        /// The map itself moved to Faces, because the panel was not the only thing that needed
        /// it -- texts did too, and having their own copy is how they ended up sending Gerald's
        /// under a silhouette. Empty for an unknown name, and the panel lays out without a
        /// picture exactly as it did before.
        /// </summary>
        private static string FaceFor(string speaker) => Faces.For(speaker);

        /// <summary>
        /// Where the highlight actually is, which is not always where the cursor is.
        ///
        /// Kept as a float and eased toward the selected row, so moving down the list slides
        /// the bar instead of teleporting it. That is the whole trick with a list: a bar that
        /// moves shows you WHICH WAY you went, and a bar that jumps makes you re-find it.
        /// </summary>
        private float _slide;

        /// <summary>When this node went up, for the panel's own entrance.</summary>
        private int _nodeAt;

        /// <summary>How fast the highlight catches the cursor. Per frame, eased.</summary>
        private const float SlideRate = 0.30f;

        /// <summary>How long the panel takes to arrive, and how far it rises on the way.</summary>
        private const int EnterMs = 180;
        private const float EnterRise = 0.016f;

        /// <summary>The travelling highlight along the picked row, and along the top bar.</summary>
        private const int SweepMs = 2200;
        private const int BarSweepMs = 3400;

        /// <summary>The caret's breathing, which is the one thing that never stops.</summary>
        private const int CaretMs = 1300;
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
            "GENERIC_YES", "GENERIC_HOWS_IT_GOING", "CHAT_STATE", "GENERIC_HI",
            "GENERIC_THANKS", "GENERIC_SHOCKED_MED"
        };

        /// <summary>
        /// What he says walking up, which is a different noise from agreeing with somebody.
        ///
        /// Kept separate from YourLines because a greeting is the one line in a conversation
        /// whose timing is fixed and obvious -- getting "yeah" as the first thing out of his
        /// mouth before anybody has said anything reads as the audio being on the wrong beat.
        /// </summary>
        private static readonly string[] GreetLines =
        {
            "GENERIC_HI", "GENERIC_HOWS_IT_GOING", "GENERIC_WHATS_UP"
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
            _nodeAt = Game.GameTime;

            // Snapped rather than eased on a change of node. Sliding the bar from where it was
            // on the LAST question to where it starts on this one is a bar travelling across a
            // panel that has just been replaced under it.
            _slide = FirstEnabled(node);
            Subject = subject;
            _selected = FirstEnabled(node);
            _openedAt = Game.GameTime;
            // The picture, and the width the words have left because of it.
            //
            // The contact texture is the FALLBACK now rather than the answer. CHAR_LAMAR draws
            // Chop -- an actual photograph of the dog, on Lamar's dialogue -- and there is no
            // way to tell that from a name, which is the problem with naming a face. So the
            // face is taken off the man who is standing in front of you instead, and the
            // contact picture is only used for somebody the mod is quoting rather than talking
            // to.
            Mugshot();

            _face = string.IsNullOrEmpty(node.Portrait) ? FaceFor(node.Speaker) : node.Portrait;

            _wrapped = Wrap(node.Line, PanelWidth - 0.03f - TextInset, BodyScale);

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            // Only on the FIRST page. After that the answer comes from Commit, so that one
            // pick produces one line rather than yours and theirs on top of each other.
            //
            // BOTH of them now. Walking up to somebody used to be silent on your side until
            // you had picked an answer, which made the opening of every conversation in the
            // mod a man talking at a mute -- so Franklin says something first, the way anybody
            // walking up to a friend does, and the other man answers a beat later.
            if (_openedFresh)
            {
                _openedFresh = false;

                Speak(Game.Player.Character, GreetLines);
                _replyAt = Game.GameTime + ReplyDelayMs;
            }
        }

        public void Close()
        {
            // The button that got you out of here does not also swing at somebody.
            if (IsOpen)
            {
                Core.InputGuard.Swallow();
                _quietUntil = Game.GameTime + QuietAfterCloseMs;
            }
            // Both of them, because a goodbye one man says on his own is not a goodbye.
            Speak(Game.Player.Character, PartingLines);
            Speak(Speaker, PartingLines);

            _node = null;
            _closedAt = Game.GameTime;
            Subject = null;

            // The render goes back the moment the conversation does. There are a limited
            // number of these and one leaked is gone for the session.
            DropMugshot();
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

        /// <summary>Attack stays dead until this, so the closing press cannot land.</summary>
        private static int _quietUntil;

        private const int QuietAfterCloseMs = 350;

        /// <summary>
        /// Every control that swings or fires. There are NINE of them.
        ///
        /// The list here had four, which is why walking out of a conversation threw a fist:
        /// Backspace and pad B land on MeleeAttackLight and MeleeAttackAlternate, and neither
        /// was in it. Exactly the same four were missing from the phone.
        /// </summary>
        private static void Swing()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.MeleeAttack1);
            Game.DisableControlThisFrame(Control.MeleeAttack2);
            Game.DisableControlThisFrame(Control.MeleeAttackLight);
            Game.DisableControlThisFrame(Control.MeleeAttackHeavy);
            Game.DisableControlThisFrame(Control.MeleeAttackAlternate);
            Game.DisableControlThisFrame(Control.MeleeBlock);
            Game.DisableControlThisFrame(Control.VehicleMeleeHold);
        }

        /// <summary>
        /// Held for a moment AFTER the panel has gone.
        ///
        /// Disabling a control lasts exactly the frame you disable it on, and the button that
        /// closed the conversation is still held down on the next one -- the frame we have
        /// already stopped drawing and stopped locking. That gap is one punch wide.
        /// </summary>
        public static void TickQuiet()
        {
            if (Game.GameTime < _quietUntil) Swing();
        }

        private static void LockControls()
        {
            Swing();

            Game.DisableControlThisFrame(Control.Aim);
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
            // The body has to be at least as tall as the photograph, or a one-line answer
            // leaves his face hanging over the first thing you can say back. The name row above
            // it counts toward that, since the picture starts level with the name.
            if (!string.IsNullOrEmpty(_face) || !string.IsNullOrEmpty(_shotTxd))
            {
                var room = FaceSize - 0.034f + 0.004f;
                if (bodyHeight < room) bodyHeight = room;
            }

            var total = 0.075f + bodyHeight + 0.012f + choiceHeight + 0.030f;
            if (!string.IsNullOrEmpty(Title)) total += 0.036f;
            var top = Math.Max(0.06f, 0.5f - total * 0.5f);

            // The entrance. Up and in over about a fifth of a second, eased out so it slows
            // as it lands -- a panel that arrives at constant speed reads as a jump cut.
            var age = Game.GameTime - _nodeAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;

            arrive = 1f - (1f - arrive) * (1f - arrive);

            top += EnterRise * (1f - arrive);

            var fade = (int)(228f * arrive);

            Hud.RectFrom(PanelX, top, PanelWidth, total, Color.FromArgb(fade, 12, 13, 15));
            Hud.RectFrom(PanelX, top, PanelWidth, 0.0035f,
                         Palette.Alpha(_node.SpeakerColour, (int)(255f * arrive)));

            // A light running along the speaker's bar. Slow, and only a sixth of the width, so
            // it reads as the panel being live rather than as something demanding attention.
            var barT = (Game.GameTime % BarSweepMs) / (float)BarSweepMs;
            var barW = PanelWidth * 0.16f;
            var barAt = PanelX - barW + (PanelWidth + barW) * barT;

            var barLeft = Math.Max(PanelX, barAt);
            var barRight = Math.Min(PanelX + PanelWidth, barAt + barW);

            if (barRight > barLeft)
            {
                Hud.RectFrom(barLeft, top, barRight - barLeft, 0.0035f,
                             Color.FromArgb((int)(90f * arrive), 255, 255, 255));
            }

            // Same idea as the info panels: the mod first, quietly, then who is speaking.
            Hud.BrandCentre(0.5f, top + 0.024f, 0.022f, Palette.Alpha(Palette.TextDim, 150));

            var y = top + 0.039f;

            if (!string.IsNullOrEmpty(Title))
            {
                Hud.Text(Title.ToUpperInvariant(), PanelX + 0.014f, y - 0.004f, 0.62f,
                         Palette.Text, Hud.FontCursive, centre: false);
                y += 0.036f;
            }

            var said = PanelX + 0.014f;

            // His photograph, beside what he is saying.
            //
            // Drawn from the top of the NAME rather than centred on the block of text, so a
            // three-line answer and a one-line one both put his face in the same place --
            // a portrait that slides up and down the panel as the sentence changes length
            // reads as part of the sentence rather than as the man saying it.
            // His own head if it has finished rendering, the contact picture until then.
            Mugshot();

            var face = string.IsNullOrEmpty(_shotTxd) ? _face : _shotTxd;

            if (!string.IsNullOrEmpty(face) && Hud.EnsureTextureDict(face))
            {
                var wide = Hud.ToX(FaceSize);

                // A well behind it, the same one the objective card puts its icon in, so the
                // picture has an edge on a panel that is otherwise flat.
                Hud.RectFrom(said, y - 0.002f, wide, FaceSize,
                             Color.FromArgb((int)(30f * arrive), 255, 255, 255));

                Hud.Sprite(face, face, said + wide * 0.5f, y - 0.002f + FaceSize * 0.5f,
                           wide, FaceSize, 0f,
                           Color.FromArgb((int)(255f * arrive), 255, 255, 255));

                // And his own colour down the near edge of it, so the picture belongs to the
                // name above the words rather than floating next to them.
                Hud.RectFrom(said, y - 0.002f, 0.0022f, FaceSize,
                             Palette.Alpha(_node.SpeakerColour, (int)(255f * arrive)));

                said += TextInset;
            }

            Hud.Text(_node.Speaker.ToUpperInvariant(), said, y, 0.36f,
                         _node.SpeakerColour, Hud.FontLabel, centre: false);
            y += 0.034f;

            foreach (var line in _wrapped)
            {
                Hud.Text(line, said, y, BodyScale, Palette.Text, Hud.FontBody, centre: false);
                y += LineHeight;
            }

            y += 0.012f;

            // The highlight is drawn ONCE, where the easing has got to, rather than on the row
            // that happens to be selected. Two rows can be lit at the edges of a slide and that
            // is correct: the bar is between them.
            _slide += (_selected - _slide) * SlideRate;
            if (Math.Abs(_selected - _slide) < 0.002f) _slide = _selected;

            var barY = y - 0.004f + _slide * ChoiceHeight;

            Hud.RectFrom(PanelX, barY, PanelWidth, ChoiceHeight,
                         Color.FromArgb((int)(235f * arrive), 240, 242, 240));

            // And a sweep along it, the same one the stash list uses. It is the only moving
            // thing on the panel once the entrance has finished, and it is always the row you
            // are about to pick.
            var sweepT = (Game.GameTime % SweepMs) / (float)SweepMs;
            var sweepW = PanelWidth * 0.14f;
            var sweepAt = PanelX - sweepW + (PanelWidth + sweepW) * sweepT;

            var sweepLeft = Math.Max(PanelX, sweepAt);
            var sweepRight = Math.Min(PanelX + PanelWidth, sweepAt + sweepW);

            if (sweepRight > sweepLeft)
            {
                Hud.RectFrom(sweepLeft, barY, sweepRight - sweepLeft, ChoiceHeight,
                             Color.FromArgb((int)(26f * arrive), 0, 0, 0));
            }

            // The near edge, in the speaker's own colour, so the bar belongs to whoever is
            // talking rather than being a grey slab.
            Hud.RectFrom(PanelX, barY, 0.0026f, ChoiceHeight,
                         Palette.Alpha(_node.SpeakerColour, (int)(255f * arrive)));

            for (var i = 0; i < _node.Choices.Count; i++)
            {
                var choice = _node.Choices[i];
                var picked = i == _selected;

                // Inked by how much of the BAR is under this row, not by which row is
                // selected.
                //
                // Those are the same thing when nothing is moving and different for the tenth
                // of a second the bar is sliding -- and getting it wrong is visible: the text
                // flips to its on-white colour the instant you press down, which is dark ink on
                // a dark panel until the bar catches up. So the text crosses over exactly as
                // fast as the thing it has to stay readable against.
                var under = 1f - Math.Min(1f, Math.Abs(i - _slide));

                var colour = !choice.Enabled
                    ? Palette.TextDisabled
                    : Blend(Palette.TextDim, Palette.TextOnHover, under);

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

                var labelled = picked ? "> " + choice.Label : "  " + choice.Label;

                Hud.Text(labelled, textX, y, ChoiceScale, colour, Hud.FontBody, centre: false);

                // The caret breathes on the row you are on. Drawn over the top of the one in
                // the label rather than instead of it, so the width of the line never changes
                // and nothing under it shifts as it pulses.
                if (picked && choice.Enabled)
                {
                    var beat = (Game.GameTime % CaretMs) / (float)CaretMs;
                    var lit = 0.5f + 0.5f * (float)Math.Sin(beat * Math.PI * 2.0);

                    Hud.Text(">", textX, y, ChoiceScale,
                             Color.FromArgb((int)(70f + 185f * lit), 16, 18, 20),
                             Hud.FontBody, centre: false);
                }

                // How strong it is, straight after what it is.
                //
                // Measured rather than parked at a fixed offset: "Marijuana." and "Oxycodone."
                // are different widths, and one constant x sits inside the end of one word and
                // out in the middle of nothing after the other.
                if (!string.IsNullOrEmpty(choice.MarkFile))
                {
                    var wide = Hud.MeasureText(labelled, ChoiceScale, Hud.FontBody);

                    Hud.File(choice.MarkFile, textX + wide + 0.005f + Hud.ToX(MarkSize) * 0.5f,
                             y + 0.0115f, MarkSize, 0f, colour);
                }

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

            // The border, last, so nothing paints over it. Cyan like the feed's, with the
            // corner ticks in the SPEAKER'S colour rather than the mod's accent -- on this
            // panel the one thing worth signalling from the frame is who is talking.
            Hud.Frame(PanelX, top, PanelWidth, total,
                      Color.FromArgb((int)(150f * arrive), 90, 215, 235),
                      Palette.Alpha(_node.SpeakerColour, (int)(255f * arrive)), 0.0016f, 0.024f);
        }

        /// <summary>Somewhere between two inks, for text the highlight is sliding under.</summary>
        private static Color Blend(Color from, Color to, float k)
        {
            if (k <= 0f) return from;
            if (k >= 1f) return to;

            return Color.FromArgb(
                (int)(from.A + (to.A - from.A) * k),
                (int)(from.R + (to.R - from.R) * k),
                (int)(from.G + (to.G - from.G) * k),
                (int)(from.B + (to.B - from.B) * k));
        }

        /// <summary>Smaller than the row's own art -- a footnote to the label rather than a
        /// second picture competing with the first.</summary>
        private const float MarkSize = 0.0135f;

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

            // PARAGRAPHS FIRST, and this is not a nicety.
            //
            // The wrap only ever split on spaces, so a newline stayed inside whatever "word"
            // it was attached to and went to DRAW_TEXT intact -- which draws the second half
            // at the same height as the first and lays a whole paragraph over the one above
            // it. Any line with a break in it came out as unreadable overlapping text.
            //
            // Split on the break, wrap each half on its own, and put one empty line between
            // them. An empty line costs a row of height and draws nothing, which is exactly
            // what a paragraph break is.
            if (text.IndexOf('\n') >= 0)
            {
                var paras = text.Split('\n');

                for (var p = 0; p < paras.Length; p++)
                {
                    var para = paras[p].Trim();

                    // A run of breaks is one break. Two newlines is how a paragraph is
                    // written and it should not cost two blank rows.
                    if (para.Length == 0) continue;

                    if (lines.Count > 0) lines.Add("");

                    lines.AddRange(Wrap(para, width, scale));
                }

                return lines;
            }

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
