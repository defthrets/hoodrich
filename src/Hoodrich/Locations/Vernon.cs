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

        /// <summary>
        /// True from HoldForTalk to ReleaseFromTalk. Update re-settles him on the wall
        /// whenever he is not held, and a conversation is the one time he is off the wall on
        /// purpose -- without this the 700ms tick would have him leaning again mid-sentence.
        /// </summary>
        private bool _talking;

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
            else if (!_held && !_talking) Settle();
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
            if (Talk == null) return;

            // The screen is up: his body follows it. See TickAct. And if it has gone down on
            // its own -- a choice that ends the talk closes the screen from inside -- this is
            // where he finds out, because nothing else tells him.
            if (Talk.IsOpen) { TickAct(); return; }
            if (_talking) ReleaseFromTalk();

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
            if (_ped == null || !_ped.Exists()) return;

            _held = false;
            _talking = true;
            _firstLine = true;
            _act = Act.None;
            _pending = null;

            if (Talk != null) Talk.Staged = OnNode;
            Warm();

            try
            {
                _ped.Task.ClearAll();
                Face();
            }
            catch
            {
                // He will still talk.
            }
        }

        public void ReleaseFromTalk()
        {
            if (!_talking && (_held || _ped == null || !_ped.Exists())) return;

            _talking = false;
            if (Talk != null && Talk.Staged == (Action<DialogueNode>)OnNode) Talk.Staged = null;

            Rest();

            if (_ped == null || !_ped.Exists()) return;
            Settle();
        }

        // ---- acting the line ---------------------------------------------------

        /// <summary>
        /// He does not stand still while he talks, and he does not stand still while he raps.
        ///
        /// THREE WAYS OF BEING ON. A spoken line gets a hand off the game's own street
        /// conversation set, picked off the words -- a question gets the open palms, a "you"
        /// gets the point at you, a "nah" gets the head shake -- and a line that keeps going
        /// gets another every few seconds while the recording runs. A verse gets a whole
        /// dance, full body, off the nightclub floor, for exactly as long as OG Vee is on the
        /// mic. And the moment the verse ends and the screen hands you the choices, the dance
        /// stops and he throws up the set and holds it, which is a man waiting to hear what
        /// you thought of it.
        ///
        /// Told which page is up by Conversation.Staged, so the words-to-movement lives here
        /// and the screen knows nothing about anybody's body. Every name below is in
        /// RampageFiles\Lists\PedAnimList.txt on this install; none of them is guessed.
        /// </summary>
        private enum Act { None, Talk, Rap, Flex }

        private const string GestureDict = "gestures@m@standing@casual";

        /// <summary>Upper body and secondary: the hands move and the feet do not. See Greeting.</summary>
        private const int GestureFlags = 48;

        /// <summary>Looping and full body. A dance is not a thing you do from the waist up.</summary>
        private const int DanceFlags = 1;

        /// <summary>Looping, upper body, secondary: the set held up over a man standing still.</summary>
        private const int SignFlags = 49;

        private const string SignDict = "mp_player_int_uppergang_sign_a";
        private const string SignClip = "mp_player_int_gang_sign_a";

        /// <summary>
        /// The nightclub's solo dances, one per verse so three verses are not one dance three
        /// times over. var_a and var_b are two different routines; med and high is how hard
        /// he goes. Which verse gets which is fixed off the words, so running one back gets
        /// the same moves.
        /// </summary>
        private static readonly string[][] Dances =
        {
            new[] { "anim@amb@nightclub@mini@dance@dance_solo@male@var_a@", "med_center" },
            new[] { "anim@amb@nightclub@mini@dance@dance_solo@male@var_b@", "med_center" },
            new[] { "anim@amb@nightclub@mini@dance@dance_solo@male@var_a@", "high_center" },
        };

        /// <summary>What his hands do when the words do not say. hello is kept for the first line.</summary>
        private static readonly string[] Hands =
        {
            "gesture_hand_left", "gesture_hand_right", "gesture_easy_now", "gesture_pleased",
            "gesture_shrug_soft", "gesture_bring_it_on", "gesture_point", "gesture_me"
        };

        /// <summary>The gap between hands on a line that keeps going, and the jitter on it.</summary>
        private const int HandGapMs = 2600;
        private const int HandJitterMs = 1400;

        /// <summary>
        /// A verse is not over in the frames before its recording has started. The sound
        /// device says "not playing" while it opens the file -- see Conversation.BeatOpenMs.
        /// </summary>
        private const int RapOpenMs = 600;

        /// <summary>How long a clip whose dictionary is still loading is retried for.</summary>
        private const int PendingMs = 2000;

        private Act _act;
        private int _actAt;
        private bool _heard;
        private bool _firstLine;
        private int _nextHandAt;
        private string _lastHand;
        private string[] _dance;
        private bool _signing;
        private string[] _pending;
        private int _pendingFlags;
        private int _pendingAt;
        private readonly Random _dice = new Random();

        /// <summary>
        /// Every dictionary he will need, asked for as the screen opens.
        ///
        /// REQUEST_ANIM_DICT is asynchronous and the first line goes up in the same frame as
        /// this, so the first gesture still usually misses -- Play says so and the miss is
        /// retried by TickAct for a moment. By the first verse everything is in.
        /// </summary>
        private void Warm()
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, GestureDict);
                Function.Call(Hash.REQUEST_ANIM_DICT, SignDict);
                foreach (var d in Dances) Function.Call(Hash.REQUEST_ANIM_DICT, d[0]);
            }
            catch
            {
                // He will act it stiffer.
            }
        }

        /// <summary>A page went up. See Conversation.Staged.</summary>
        private void OnNode(DialogueNode node)
        {
            if (node == null || _ped == null || !_ped.Exists()) return;
            if (Talk == null || !ReferenceEquals(Talk.Subject, this)) return;

            if (node.Speaker == VernonTalk.Stage) Rap(node.Line);
            else Say(node.Line);
        }

        private void Say(string line)
        {
            _pending = null;
            StopDance();
            StopSign();
            Face();

            _act = Act.Talk;
            _actAt = Game.GameTime;

            var hand = _firstLine ? "gesture_hello" : HandFor(line);
            _firstLine = false;

            Gesture(hand);
            _nextHandAt = Game.GameTime + HandGapMs + _dice.Next(HandJitterMs);
        }

        private void Rap(string verse)
        {
            _pending = null;
            StopSign();

            _act = Act.Rap;
            _actAt = Game.GameTime;
            _heard = false;

            var sum = 0;
            foreach (var c in verse) sum += c;
            var dance = Dances[sum % Dances.Length];

            if (_dance != null && (_dance[0] != dance[0] || _dance[1] != dance[1])) StopDance();

            if (Play(dance[0], dance[1], DanceFlags)) _dance = dance;
            else Later(dance, DanceFlags);
        }

        private void Flex()
        {
            _pending = null;
            StopDance();
            Face();

            _act = Act.Flex;
            _actAt = Game.GameTime;

            if (Play(SignDict, SignClip, SignFlags)) _signing = true;
            else Later(new[] { SignDict, SignClip }, SignFlags);
        }

        /// <summary>Every frame the screen is up. See UpdatePrompt.</summary>
        private void TickAct()
        {
            if (_act == Act.None || _ped == null || !_ped.Exists()) return;

            var now = Game.GameTime;

            if (_pending != null)
            {
                if (Play(_pending[0], _pending[1], _pendingFlags))
                {
                    if (_pendingFlags == DanceFlags) _dance = _pending;
                    else if (_pendingFlags == SignFlags) _signing = true;
                    _pending = null;
                }
                else if (now - _pendingAt > PendingMs)
                {
                    Log.Debug("Vernon's " + _pending[1] + " never loaded; acting it without.");
                    _pending = null;
                }
            }

            var talking = Voice.Talking;

            switch (_act)
            {
                case Act.Rap:
                    // For as long as the take runs. A verse with no recording yet keeps him
                    // dancing until you pick something, which is the right look for it.
                    if (talking) _heard = true;
                    else if (_heard && now - _actAt > RapOpenMs) Flex();
                    break;

                case Act.Talk:
                    if (now < _nextHandAt) break;

                    if (talking)
                    {
                        Gesture(HandFor(null));
                        _nextHandAt = now + HandGapMs + _dice.Next(HandJitterMs);
                    }
                    else
                    {
                        _nextHandAt = now + 500;
                    }
                    break;
            }
        }

        /// <summary>
        /// The hand for a line, off its words; or off the dice when the words do not say.
        ///
        /// The FRONT of the line decides, because that is the part being said when the hand
        /// goes up; the point at the basement is the one exception and reads the whole line,
        /// because a man mentions the basement wherever in the sentence it falls and points
        /// at it either way.
        /// </summary>
        private string HandFor(string line)
        {
            if (!string.IsNullOrEmpty(line))
            {
                var l = line.ToLowerInvariant();
                var head = l.Length > 40 ? l.Substring(0, 40) : l;

                if (l.TrimEnd().EndsWith("?")) return _dice.Next(2) == 0 ? "gesture_what_soft" : "gesture_why";
                if (l.Contains("basement") || l.Contains("downstairs") || l.Contains("down there")) return "gesture_point";
                if (head.Contains("nah") || head.Contains("no,") || head.Contains("don't") || head.Contains("never")) return "gesture_nod_no_soft";
                if (head.StartsWith("you ") || head.Contains(" you ")) return "gesture_you_soft";
                if (head.Contains("damn") || head.Contains("shit") || head.Contains("man,")) return "gesture_damn";
                if (head.Contains("yeah") || head.Contains("aight") || head.Contains("okay")) return "gesture_nod_yes_soft";
                if (head.Contains("i'm ") || head.Contains(" my ") || head.Contains(" me ")) return "gesture_me";
            }

            string pick;
            do pick = Hands[_dice.Next(Hands.Length)]; while (pick == _lastHand);
            return pick;
        }

        private void Gesture(string clip)
        {
            _lastHand = clip;
            if (!Play(GestureDict, clip, GestureFlags)) Later(new[] { GestureDict, clip }, GestureFlags);
        }

        /// <summary>
        /// One clip, now. False means the dictionary is not in yet and the caller should try
        /// again; see Later and TickAct.
        /// </summary>
        private bool Play(string dict, string clip, int flags)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dict);
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) return false;

                Function.Call(Hash.TASK_PLAY_ANIM, _ped.Handle, dict, clip,
                              4f, -4f, -1, flags, 0f, false, false, false);
                return true;
            }
            catch
            {
                // A call that throws will throw again; there is nothing to wait for.
                return true;
            }
        }

        private void Later(string[] pair, int flags)
        {
            _pending = pair;
            _pendingFlags = flags;
            _pendingAt = Game.GameTime;
        }

        private void StopDance()
        {
            if (_dance == null) return;

            try
            {
                if (_ped != null && _ped.Exists())
                {
                    Function.Call(Hash.STOP_ANIM_TASK, _ped.Handle, _dance[0], _dance[1], -4f);
                }
            }
            catch
            {
                // The next task replaces it anyway.
            }

            _dance = null;
        }

        /// <summary>
        /// The set down. A secondary loop is the one thing a scenario does NOT replace, so
        /// this has to happen before Settle or he leans on the wall still holding it up.
        /// </summary>
        private void StopSign()
        {
            if (!_signing) return;

            try
            {
                if (_ped != null && _ped.Exists())
                {
                    Function.Call(Hash.STOP_ANIM_TASK, _ped.Handle, SignDict, SignClip, -4f);
                }
            }
            catch
            {
                // See StopDance.
            }

            _signing = false;
        }

        private void Face()
        {
            try
            {
                var player = Game.Player.Character;
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _ped.Handle, player.Handle, -1);
                }
            }
            catch
            {
                // He talks to your shoulder.
            }
        }

        /// <summary>Everything off, for the wall or for a teardown.</summary>
        private void Rest()
        {
            _pending = null;
            _act = Act.None;

            if (_ped == null || !_ped.Exists())
            {
                _dance = null;
                _signing = false;
                return;
            }

            StopDance();
            StopSign();
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
