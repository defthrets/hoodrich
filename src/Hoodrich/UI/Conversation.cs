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

        /// <summary>
        /// Whether picking this one is what carries the story forward.
        ///
        /// A leader's list is eight rows of things you can say and one of them is the job. The
        /// other seven are prices, stock, how you are doing, a front, a way out -- all real,
        /// all worth having, and all indistinguishable from the one the game is waiting on. So
        /// somebody stood in front of Gerald with an objective reading "ask about the port"
        /// reads down a menu with no port on it and has to guess which of "You got anything for
        /// me?" and "How am I doing?" is the door.
        ///
        /// Marked rather than reworded, because the words are his and should stay his. The
        /// panel does the telling.
        /// </summary>
        public bool MovesOn;

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

        /// <summary>
        /// The recording to play for this node, when its words cannot name one themselves.
        ///
        /// NAMING A FILE BY WHAT IS SAID ONLY WORKS IF WHAT IS SAID IS FIXED. Most lines are,
        /// including many that look otherwise -- a turf name or a product word pasted in from
        /// the data comes out identical every time, so the hash lands on it. But a handful
        /// genuinely differ per utterance: how much of his work you are still carrying, what
        /// he just paid you. Those hash to a new name every single time they are spoken and no
        /// file could ever match them.
        ///
        /// So the line names its own file instead, and the recording is written to suit -- say
        /// the sentence without the number in it, and the number stays on screen where it is
        /// accurate. Which is the honest split anyway: the screen carries the arithmetic and
        /// the voice carries the man.
        ///
        /// Empty means the usual thing, and the usual thing is what nearly everything uses.
        /// </summary>
        public string VoiceKey = "";

        /// <summary>
        /// The recording the screen plays on its way out, whichever way out is taken: an
        /// option that ends it, or backing out of it. A cue name -- see Voice.Cue -- so it
        /// plays every time rather than once. Set on the nodes of a man who has a goodbye.
        /// </summary>
        public string Farewell = "";
        public Color SpeakerColour = Palette.Text;

        /// <summary>
        /// Where this line goes on its own, or null for a line that waits to be answered.
        ///
        /// A PERFORMANCE IS NOT A CONVERSATION. Everything in this mod up to now has been a man
        /// asking you something, so every node has carried a list of things to say back -- and
        /// that is precisely wrong for four bars of a verse, where the only honest option
        /// between one line and the next is nothing at all.
        ///
        /// So a beat has no choices, plays, and hands over to the next one. The last line of a
        /// verse is a normal node again, which is where your answers come back -- so the shape
        /// on screen is: he starts, he goes, and you get to react to how it ended.
        ///
        /// Only honoured on a node with NO choices. A node that has both is a mistake, and the
        /// choices are the half worth keeping.
        /// </summary>
        public Func<DialogueNode> Runs;

        /// <summary>
        /// Play this line every time rather than once ever.
        ///
        /// Say spends a recording the first time it is heard and leaves the words on screen
        /// after that, which is right for a man answering the same question twice and wrong for
        /// a verse. "Run that back" is a row on this screen; a verse that only ever plays once
        /// per save would make it a lie, and the whole point of somebody recording a rapping
        /// voice is that it gets performed.
        /// </summary>
        public bool Encore;

        /// <summary>
        /// How long this beat stays up when nothing plays, or 0 to work it out from the words.
        ///
        /// Only reached when the recording is missing. With audio the line lasts exactly as
        /// long as the audio does.
        /// </summary>
        public int HoldMs { get; set; }

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

        /// <summary>
        /// Marks the choice just added as the one that takes the story somewhere.
        ///
        /// Deliberately a claim the caller makes rather than something worked out here. Only
        /// the code that built the row knows whether THIS row, on THIS node, at this point in
        /// the save, is the next beat -- "You got anything for me?" is the job when there is a
        /// job and small talk when there is not.
        /// </summary>
        public DialogueNode MovesOn(bool yes = true)
        {
            if (Choices.Count == 0) return this;

            Choices[Choices.Count - 1].MovesOn = yes;
            return this;
        }

        /// <summary>Gives the choice just added one of the wheel's icons.</summary>
        /// <summary>Name the recording for this line. See VoiceKey.</summary>
        public DialogueNode Voiced(string key)
        {
            VoiceKey = key ?? "";
            return this;
        }

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

        /// <summary>
        /// Makes this one beat of a performance: it plays out loud and moves on by itself.
        ///
        /// Encore comes with it, because the only thing built this way is a verse and a verse
        /// that plays once per save is not worth recording twice over.
        /// </summary>
        public DialogueNode Beat(Func<DialogueNode> next)
        {
            Runs = next;
            Encore = true;
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

        /// <summary>Where the foot of the panel sits, as a fraction of the screen's height.</summary>
        private const float PanelFoot = 0.95f;

        /// <summary>
        /// Sized in HEIGHT fractions and converted, so the card keeps its shape.
        ///
        /// It was 0.42 of the screen's width, which on a widescreen monitor is a card and on
        /// an ultrawide is a letterbox strip with a sentence a metre long across it. The
        /// stash screen made the same move for the same reason.
        /// </summary>
        private const float PanelWidthH = 0.72f;
        private static float PanelWidth => Hud.ToX(PanelWidthH);
        private const float LineHeight = 0.031f;
        private const float ChoiceHeight = 0.034f;
        private const float BodyScale = 0.36f;
        private const float ChoiceScale = 0.34f;

        // The panel's own measures. Heights are fractions of the screen's height, the x
        // insets are fractions of its width, the way every Hud call takes them. See Draw.
        private const float Pad = 0.014f;
        private const float PadTop = 0.020f;
        private const float PadBottom = 0.014f;
        private const float NameRow = 0.034f;
        private const float RuleGap = 0.011f;
        private const float KeysGap = 0.010f;
        private const float KeysRow = KeysGap + 0.0175f;
        private const float Rail = 0.0030f;
        private const float RowInset = 0.010f;
        private const float RowFill = 26f;
        private const float NameScale = 0.36f;
        private const float PlaceScale = 0.27f;

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
        private int _shotRetryAt;
        private bool _shotMoaned;

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

            // ASKED AGAIN IF THE GAME SAID NO. It keeps a fixed number of these and hands
            // back nothing when they are all taken -- the feed's faces, the phone's own,
            // whatever else is running -- and this used to ask once, on the frame the talk
            // opened, and take the answer for the rest of the conversation. Hao stood there
            // with no face for exactly that reason. A slot comes free within seconds; it
            // is asked for again every couple.
            if (_shotOf != id || (_shot == 0 && Game.GameTime >= _shotRetryAt))
            {
                if (_shotOf != id) DropMugshot();
                _shotOf = id;
                _shotRetryAt = Game.GameTime + 1500;

                try { _shot = Function.Call<int>(Hash.REGISTER_PEDHEADSHOT, who.Handle); }
                catch (Exception ex) { Log.Debug("No headshot for the speaker: " + ex.Message); }

                if (_shot == 0 && !_shotMoaned)
                {
                    _shotMoaned = true;
                    Log.Info("No headshot slot for " + (_node == null ? "the speaker" : _node.Speaker) +
                             "; the contact picture stands in until one frees up.");
                }
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

        /// <summary>The choice the cursor was on before this one, and when it moved. See Theme.Lit.</summary>
        private int _lastSelected = -1;
        private int _pickedAt;

        /// <summary>The cursor frame that glides between choices. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

        /// <summary>When this node went up, for the panel's own entrance.</summary>
        private int _nodeAt;

        /// <summary>How long the panel takes to arrive, and how far it rises on the way.</summary>
        private const int EnterMs = 180;
        private const float EnterRise = 0.016f;

        /// <summary>
        /// How the waiting row breathes: the length of one breath, and how far it goes.
        ///
        /// Slower than the caret on purpose. The caret is telling you where you are, which
        /// wants to feel responsive; this is telling you where to go, which wants to feel
        /// patient. Low numbers throughout -- at twenty-six over near-black it is a shade
        /// rather than a colour, and it is the movement that does the work, not the brightness.
        /// </summary>
        private const int MovesOnMs = 2200;
        private const float MovesOnFloor = 10f;
        private const float MovesOnSwing = 16f;

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
        /// Whoever is stood there, told each time a page goes up, so the man can act the line.
        ///
        /// SET AND CLEARED BY WHOEVER OPENED THE SCREEN. The one screen is shared by everybody
        /// in the mod, so a hook left behind after a close would be told about the next man's
        /// lines. Vernon is the one who uses it: a gesture on a spoken line, a dance on a
        /// verse, the set thrown up when the verse ends. The screen knows nothing about
        /// anybody's body; it says which page is up and leaves the rest to him.
        /// </summary>
        public Action<DialogueNode> Staged;

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

        /// <summary>
        /// A moving picture at the far end of the name row, the size of his face: the base
        /// name of a flipbook in the icons folder, how many frames, and the delay. See
        /// Draw.Animated. Empty means none, which is every conversation but the one that
        /// set it; Close clears it so nobody inherits it.
        /// </summary>
        public string Badge = "";
        public int BadgeFrames;
        public int BadgeMs = 100;

        private float BadgeInset => string.IsNullOrEmpty(Badge) ? 0f : TextInset;

        public void Open(DialogueNode node, object subject = null)
        {
            if (node == null) return;

            _node = node;
            _nodeAt = Game.GameTime;

            // Landed rather than glided to on a change of node. A frame travelling from where
            // it was on the LAST question to where it starts on this one is a frame crossing a
            // panel that has just been replaced under it.
            Subject = subject;
            _selected = FirstEnabled(node);
            _lastSelected = -1;
            _pickedAt = Game.GameTime;
            _glide.Reset();
            _openedAt = Game.GameTime;

            // A fresh conversation starts with no goodbye; a node that has one sets it.
            if (_openedFresh) _farewell = null;
            if (!string.IsNullOrEmpty(node.Farewell)) _farewell = node.Farewell;

            // And say it out loud, if somebody recorded this one. Say() stops whatever was
            // talking before it, so arrowing down a list does not stack voices on top of each
            // other -- every node either replaces the last line or leaves the screen quiet.
            //
            // A PERFORMANCE GOES THROUGH CUE INSTEAD. See DialogueNode.Encore: Say spends a
            // line the first time it is heard, which is right for an answer and wrong for a
            // verse the screen is currently offering to run back for you. Cue is the same
            // channel with the once-only rule left off, and the key is the one Say would have
            // worked out, so the file is named exactly as every other line in the pack is.
            if (node.Encore)
            {
                Core.Voice.Cue(string.IsNullOrEmpty(node.VoiceKey)
                                   ? Core.Voice.Key(node.Speaker, node.Line)
                                   : node.VoiceKey);

                // And he does not grunt over himself. The reply was queued by whichever
                // choice started the performance, and it would land on the first bar.
                _replyAt = 0;
            }
            else
            {
                Core.Voice.Say(node.Speaker, node.Line, node.VoiceKey);
            }

            // A fresh beat has not heard anything yet. See TickBeat.
            _beatHeardAt = 0;

            // AND ACT IT. After the voice has been cued, so a hook that asks whether the line
            // is playing yet gets the honest answer (not yet; it is opening). See Staged.
            if (Staged != null)
            {
                try
                {
                    Staged(node);
                }
                catch
                {
                    // A man who cannot act the line still says it.
                }
            }

            // THE CAMERA. Over his shoulder on the first line of a talk with somebody who is
            // stood there (a phone call has no Speaker), and the other shoulder on each line
            // after -- see TalkCam.
            if (_openedFresh) TalkCam.Start(Game.Player.Character, Speaker);
            else TalkCam.Cut();
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

            _wrapped = Wrap(node.Line, PanelWidth - 0.03f - TextInset - BadgeInset, BodyScale);

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
            Badge = "";
            BadgeFrames = 0;

            TalkCam.Stop();

            // The button that got you out of here does not also swing at somebody.
            // Nothing carries on talking to an empty screen, and nothing carries on
            // mouthing at one either.
            Core.Voice.Hush();
            Core.Lips.Rest();

            // HIS GOODBYE, OUT LOUD. On every way out -- an option that ends the talk, or
            // backing out of it -- the man says his recorded farewell. Cued rather than said,
            // because a goodbye that only plays the first time is a man who has stopped
            // talking to you.
            if (!string.IsNullOrEmpty(_farewell))
            {
                var bye = _farewell;
                _farewell = null;
                Core.Voice.Cue(bye);
            }

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

        /// <summary>The goodbye the open conversation will play when it closes, if any.</summary>
        private string _farewell;

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
            TalkCam.Update();

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

            // And his mouth, for as long as the recording lasts. Every screen already sets
            // Speaker so the mugshot and the parting lines work, so there is nothing to wire
            // up per conversation -- if the line has audio the face moves, and if it does not
            // this costs one comparison.
            Core.Lips.Update(Speaker);

            // His answer, once the beat after your line has passed.
            TickReply();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            // A beat carries itself. See TickBeat.
            if (IsBeat) { TickBeat(); return; }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (Pressed(Control.PhoneCancel)) { Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET"); Close(); }
            else if (Pressed(Control.PhoneSelect)) Commit();
        }

        /// <summary>
        /// Whether the page up is one that turns itself.
        ///
        /// BOTH CONDITIONS, and the choices are the one that wins. A node with somewhere to go
        /// AND something to ask is a mistake somebody made building it, and running it as a
        /// beat would eat the question -- so it is run as a question and the beat is what gets
        /// dropped.
        /// </summary>
        private bool IsBeat => _node != null && _node.Runs != null && _node.Choices.Count == 0;

        /// <summary>When this beat last heard its recording still going. See TickBeat.</summary>
        private int _beatHeardAt;

        /// <summary>
        /// Long enough for the sound device to have opened the file before it is asked whether
        /// it is playing. MCI answers "not playing" for a moment while it gets going, and
        /// without this every bar would be skipped in the frame it went up.
        /// </summary>
        private const int BeatOpenMs = 350;

        /// <summary>The breath after a line lands, before the next one starts.</summary>
        private const int BeatTailMs = 220;

        /// <summary>
        /// How long an unrecorded beat stays up: a floor, a bit per word, and a ceiling.
        ///
        /// Only ever reached when the file is missing, so this is what the verse looks like
        /// while somebody is still recording it -- readable, rather than four lines flashing
        /// past in half a second.
        /// </summary>
        private const int BeatFloorMs = 900;
        private const int BeatPerWordMs = 260;
        private const int BeatCeilingMs = 6000;

        /// <summary>
        /// One beat, which moves on when its line has finished rather than when you press
        /// something.
        ///
        /// THE RECORDING IS THE CLOCK. A bar lasts exactly as long as the bar does, so a verse
        /// comes out at the pace it was performed at rather than at a pace guessed here -- and
        /// when there is no recording yet it falls back to how long the words take to read.
        ///
        /// Two ways out by hand, because being trapped inside somebody else's verse is funny
        /// once. Select pushes past a line; back leaves the whole conversation.
        /// </summary>
        private void TickBeat()
        {
            if (Pressed(Control.PhoneCancel))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            var on = Game.GameTime - _nodeAt;
            if (on < BeatOpenMs) return;

            if (Pressed(Control.PhoneSelect)) { Step(); return; }

            if (Core.Voice.Talking)
            {
                _beatHeardAt = Game.GameTime;
                return;
            }

            // It was said out loud, so the beat was as long as the saying of it.
            if (_beatHeardAt > 0)
            {
                if (Game.GameTime - _beatHeardAt < BeatTailMs) return;

                Step();
                return;
            }

            if (on < Reading(_node)) return;

            Step();
        }

        /// <summary>How long a line with no recording is left on screen.</summary>
        private static int Reading(DialogueNode node)
        {
            if (node.HoldMs > 0) return node.HoldMs;

            var words = 1;
            foreach (var c in node.Line) if (c == ' ') words++;

            var read = BeatFloorMs + words * BeatPerWordMs;
            return read > BeatCeilingMs ? BeatCeilingMs : read;
        }

        /// <summary>On to whatever this beat hands over to, or out if it hands over nothing.</summary>
        private void Step()
        {
            DialogueNode to = null;

            try
            {
                if (_node.Runs != null) to = _node.Runs();
            }
            catch (Exception ex)
            {
                Log.Error("A beat threw on its way to the next one; ending the conversation.", ex);
            }

            if (to == null) { Close(); return; }

            Open(to, Subject);
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

            var before = _selected;

            // Skip past anything he will not let you say, wrapping round.
            for (var i = 0; i < _node.Choices.Count; i++)
            {
                _selected += step;
                if (_selected < 0) _selected = _node.Choices.Count - 1;
                if (_selected >= _node.Choices.Count) _selected = 0;

                if (_node.Choices[_selected].Enabled) break;
            }

            if (_selected != before)
            {
                _lastSelected = before;
                _pickedAt = Game.GameTime;
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
        /// Backspace and pad B land on MeleeAttackLight and MeleeAttackAlternate, which is the
        /// whole reason backing out of anything threw a punch. That was found here and fixed
        /// here, and the nine screens with their own hand-typed lists never heard about it --
        /// so the list now lives in Core.Fists and this is a name for calling it.
        /// </summary>
        private static void Swing()
        {
            Core.Fists.Off();
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

            // The photograph runs from the name row down into the words, so a one-line
            // answer still has to be as tall as his face, or the face hangs over the first
            // thing you can say back.
            var pictured = !string.IsNullOrEmpty(_face) || !string.IsNullOrEmpty(_shotTxd);
            if (pictured)
            {
                var room = FaceSize - NameRow + 0.004f;
                if (bodyHeight < room) bodyHeight = room;
            }

            // ONE HEADER ROW, THE WORDS, A RULE, THE CHOICES, THE KEYS. Nothing else. The
            // wordmark across the top and the place name in cursive the size of a headline
            // both went: a conversation is the one screen where the man talking is the whole
            // point, and everything above his name was competing with him for it.
            var total = PadTop + NameRow + bodyHeight + RuleGap * 2f + choiceHeight + KeysRow + PadBottom;

            // STOOD ON THE BOTTOM OF THE SCREEN, under the face the camera has put at the
            // top -- see TalkCam. Only a long list of choices climbs back up into it.
            var top = Math.Max(0.06f, PanelFoot - total);

            // The entrance. Up and in over about a fifth of a second, eased out so it slows
            // as it lands -- a panel that arrives at constant speed reads as a jump cut.
            var age = Game.GameTime - _nodeAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;

            arrive = 1f - (1f - arrive) * (1f - arrive);

            top += EnterRise * (1f - arrive);

            Theme.Panel(PanelX, top, PanelWidth, total, arrive);

            var left = PanelX + Pad;
            var right = PanelX + PanelWidth - Pad;
            var inner = right - left;
            var ink = (int)(255f * arrive);

            var y = top + PadTop;
            var said = left;

            // His photograph, level with his name, with his colour down the near edge --
            // the one mark on the panel of whose it is. No well behind it: the picture has
            // its own edge and the panel is flat everywhere else.
            Mugshot();

            var face = string.IsNullOrEmpty(_shotTxd) ? _face : _shotTxd;

            if (!string.IsNullOrEmpty(face) && Hud.EnsureTextureDict(face))
            {
                var wide = Hud.ToX(FaceSize);

                Hud.Sprite(face, face, said + wide * 0.5f, y + FaceSize * 0.5f,
                           wide, FaceSize, 0f, Color.FromArgb(ink, 255, 255, 255));

                Hud.RectFrom(said, y, Hud.ToX(Rail), FaceSize,
                             Palette.Alpha(_node.SpeakerColour, ink));

                said += TextInset;
            }

            // The name in his colour; the place small and dim at the far end of the row,
            // where a label goes, rather than over everything as a title.
            Hud.Text(_node.Speaker.ToUpperInvariant(), said, y, NameScale,
                     Palette.Alpha(_node.SpeakerColour, ink), Hud.FontLabel, centre: false);

            // The badge, level with the face at the other end: the same size, the same
            // top, so the row reads as a pair -- his picture on the left, his thing on the
            // right -- and the place label steps in to make room for it.
            var labelRight = right;

            if (!string.IsNullOrEmpty(Badge) && BadgeFrames > 0)
            {
                var wide = Hud.ToX(FaceSize);
                Hud.Animated(Badge, BadgeFrames, BadgeMs, right - wide, y, wide, FaceSize,
                             Color.FromArgb(ink, 255, 255, 255));
                labelRight = right - wide - 0.008f;
            }

            if (!string.IsNullOrEmpty(Title))
            {
                Hud.TextRight(Title.ToUpperInvariant(), labelRight, y + 0.004f, PlaceScale,
                              Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontLabel);
            }

            y += NameRow;

            var bodyTop = y;

            foreach (var line in _wrapped)
            {
                Hud.Text(line, said, y, BodyScale, Palette.Alpha(Palette.Text, ink),
                         Hud.FontBody, centre: false);
                y += LineHeight;
            }

            // One hairline between what he said and what you can say back, tinted his
            // colour at the near end the way every rule in the mod is.
            y = bodyTop + bodyHeight + RuleGap;
            Theme.Rule(left, y, inner, arrive, _node.SpeakerColour);
            y += RuleGap;

            // THE LIGHT COMES UP UNDER THE CHOICE rather than a frame sliding to it: a soft
            // fill and a rail rise under the new line over a sixth of a second while the
            // ones under the old line sink, and the ink brightens by the same measure. No
            // plate, no sheen, no frame -- a light behind the words, not a box round them.
            var grown = Theme.Grown(_pickedAt);

            for (var i = 0; i < _node.Choices.Count; i++)
            {
                var choice = _node.Choices[i];
                var picked = i == _selected;

                // Inked by how far the light has come up under this row, not by which row
                // is selected -- the two differ for the sixth of a second it is rising.
                var under = Theme.Lit(i, _selected, _lastSelected, grown) * arrive;
                var rowTop = y - 0.002f;

                if (under > 0.01f)
                {
                    Hud.RectFrom(left, rowTop, inner, ChoiceHeight,
                                 Color.FromArgb((int)(RowFill * under), 255, 255, 255));
                    Hud.RectFrom(left, rowTop, Hud.ToX(Rail), ChoiceHeight,
                                 Palette.Alpha(Palette.Brand, (int)(255f * under)));
                }

                var colour = !choice.Enabled
                    ? Theme.Ink(Palette.TextDisabled, under)
                    : Theme.Ink(picked ? Palette.Text : Palette.TextDim, under);

                colour = Palette.Alpha(colour, (int)(colour.A * arrive));

                // The one the game is waiting on, breathing -- on the rail only. A slow
                // sine: anything that snaps back to its start reads as a warning, and this
                // is waiting. It hands over to the light as the light arrives.
                if (choice.MovesOn && choice.Enabled && under < 0.99f)
                {
                    var breath = (Game.GameTime % MovesOnMs) / (float)MovesOnMs;
                    var swell = 0.5f + 0.5f * (float)Math.Sin(breath * Math.PI * 2.0);
                    var strength = (1f - under) * arrive;

                    Hud.RectFrom(left, rowTop, Hud.ToX(Rail), ChoiceHeight,
                                 Palette.Alpha(Palette.Brand, (int)((90f + 130f * swell) * strength)));
                }

                var textX = left + RowInset;

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

                // FITTED TO WHAT IS LEFT. The detail on the right -- a price, a reason it is
                // locked -- is measured first and the words give way to it, so the two never
                // meet in the middle of the row.
                var note = !choice.Enabled ? choice.DisabledReason : picked ? choice.Detail : "";
                var noteW = string.IsNullOrEmpty(note) ? 0f : Hud.MeasureText(note, 0.26f, Hud.FontLabel) + 0.012f;
                var markW = string.IsNullOrEmpty(choice.MarkFile) ? 0f : Hud.ToX(MarkSize) + 0.008f;

                var labelled = Hud.Fit(choice.Label, right - noteW - markW - textX,
                                       ChoiceScale, Hud.FontBody);

                Hud.Text(labelled, textX, y, ChoiceScale, colour, Hud.FontBody, centre: false);

                // How strong it is, straight after what it is. Measured, because
                // "Marijuana." and "Ecstasy." are different widths.
                if (!string.IsNullOrEmpty(choice.MarkFile))
                {
                    var wide = Hud.MeasureText(labelled, ChoiceScale, Hud.FontBody);

                    Hud.File(choice.MarkFile, textX + wide + 0.005f + Hud.ToX(MarkSize) * 0.5f,
                             y + 0.0115f, MarkSize, 0f, colour);
                }

                if (!string.IsNullOrEmpty(note))
                {
                    var noteInk = Theme.Ink(!choice.Enabled ? Palette.Danger : Palette.TextDim, under);

                    Hud.TextRight(note, right, y + 0.004f, 0.26f,
                                  Palette.Alpha(noteInk, (int)(noteInk.A * arrive)), Hud.FontLabel);
                }

                y += ChoiceHeight;
            }

            // The keys, drawn as keys, the way every other panel does it, under a little air.
            var ky = y + KeysGap;

            UiKit.KeyRight(right, ky, UiKit.Back, "WALK OFF", arrive);

            var kx = UiKit.Key(left, ky, null, "arrow_updown.png", "CHOOSE", arrive);
            UiKit.Key(kx, ky, UiKit.Confirm, null, "SAY IT", arrive);
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
