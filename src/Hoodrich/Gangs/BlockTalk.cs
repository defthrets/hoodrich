using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Supply;
using Hoodrich.UI;
using Control = GTA.Control;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// The context button, pointed at people rather than at places.
    ///
    /// Everything else that answers this key is somewhere -- a counter, a doorway, a man who
    /// sells guns. This is the rest of the block: whoever you happen to be stood next to. Walk
    /// up to one of your own and you nod at him; walk up to one of your own on the pavement and
    /// you say the only thing there is to say.
    ///
    /// Two rules decide which of the four things happens, and both of them come from what is
    /// actually in front of you rather than from a menu:
    ///
    ///   * A body outranks a living man. If somebody is down within reach, that is what you are
    ///     looking at, and standing over a friend to greet the man behind him is grotesque.
    ///   * A homie who is POSTED UP -- smoking, drinking, leaning on a wall -- has time to
    ///     talk, so you get a conversation. A homie walking somewhere gets a nod, because he is
    ///     going somewhere and so are you.
    ///
    /// Nothing here spawns, owns or persists anything. It reads the world, shows a prompt, and
    /// says a line.
    /// </summary>
    internal sealed class BlockTalk
    {
        /// <summary>Close enough to speak to without shouting.</summary>
        private const float GreetRange = 2.6f;

        /// <summary>A body you are standing over, which is nearer than conversation range.</summary>
        private const float BodyRange = 2.2f;

        /// <summary>How wide a net to cast when looking for somebody. Cheap, and thrown away.</summary>
        private const float ScanRange = 6f;

        private const int ScanIntervalMs = 200;

        /// <summary>
        /// The same man does not get greeted twice in a row without a gap.
        ///
        /// Without this, holding the key turns a nod into a stutter -- and the prompt is on
        /// screen the whole time you are stood next to him, so it is very easy to do.
        /// </summary>
        private const int GreetCooldownMs = 4000;

        /// <summary>And a body is remarked on once, not every time you walk past it.</summary>
        private const int BodyCooldownMs = 20000;

        /// <summary>Task 118 is the scenario task -- posted up rather than passing through.</summary>
        private const int ScenarioTask = 118;

        private readonly Affiliation _crew;
        private readonly Conversation _talk;

        private readonly Dictionary<int, int> _spokenTo = new Dictionary<int, int>();

        private int _lastScan;
        private Ped _target;
        private bool _targetIsBody;

        public BlockTalk(Affiliation crew, Conversation talk)
        {
            _crew = crew;
            _talk = talk;
        }

        /// <summary>
        /// Set by Main: true when something else already owns the context key.
        ///
        /// The block is full of people who are ALSO somebody -- Lamar has two men stood with
        /// him, Stretch has two more -- so without this, walking up to Lamar offers you a nod
        /// at his hanger-on instead of the job he is holding.
        /// </summary>
        public Func<bool> Suppressed;

        /// <summary>Set by Main: true while a mission or a menu is running.</summary>
        public Func<bool> Busy;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastScan < ScanIntervalMs) return;
            _lastScan = now;

            _target = null;

            if (_talk != null && _talk.IsOpen) return;
            if (Busy != null && Busy()) return;
            if (Suppressed != null && Suppressed()) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;
            if (player.IsInVehicle() || player.IsInCombat) return;

            Find(player, now);
        }

        /// <summary>
        /// Picks what you are looking at. Bodies first, then whoever is nearest.
        /// </summary>
        private void Find(Ped player, int now)
        {
            var bestBody = (Ped)null;
            var bestBodyAway = float.MaxValue;

            var bestLive = (Ped)null;
            var bestLiveAway = float.MaxValue;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, ScanRange))
                {
                    if (ped == null || !ped.Exists()) continue;
                    if (ped.Handle == player.Handle) continue;

                    var away = player.Position.DistanceTo(ped.Position);

                    if (!ped.IsAlive)
                    {
                        if (away > BodyRange || away >= bestBodyAway) continue;
                        if (Recent(ped, now, BodyCooldownMs)) continue;

                        bestBody = ped;
                        bestBodyAway = away;
                        continue;
                    }

                    // Only your own. Walking up to a Balla and pressing a button is not a
                    // greeting, it is the start of something this class does not handle.
                    if (away > GreetRange || away >= bestLiveAway) continue;
                    if (!IsOneOfOurs(ped)) continue;
                    if (Recent(ped, now, GreetCooldownMs)) continue;

                    bestLive = ped;
                    bestLiveAway = away;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Block scan failed: " + ex.Message);
                return;
            }

            _target = bestBody ?? bestLive;
            _targetIsBody = bestBody != null;
        }

        /// <summary>Whether we have already said something to this one recently.</summary>
        private bool Recent(Ped ped, int now, int cooldownMs)
        {
            if (!_spokenTo.TryGetValue(ped.Handle, out var last)) return false;
            return now - last < cooldownMs;
        }

        private bool IsOneOfOurs(Ped ped)
        {
            var mine = _crew == null ? null : _crew.Current;
            if (mine == null) return false;

            var theirs = _crew.GangOf(ped);
            return theirs != null &&
                   string.Equals(theirs.Id, mine.Id, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The prompt, and the key. Drawn from the main tick so it lands every frame.</summary>
        public void Draw()
        {
            if (_target == null || !_target.Exists()) return;
            if (_talk != null && _talk.IsOpen) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to " + PromptFor());

            if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

            Act(player);
        }

        private string PromptFor()
        {
            if (_targetIsBody) return IsOneOfOurs(_target) ? "check on him" : "look at him";

            return PostedUp(_target) ? "talk to him" : "say something";
        }

        /// <summary>
        /// Whether he is stopped and doing something, rather than on his way somewhere.
        ///
        /// The scenario task covers all of it -- smoking, drinking, leaning, standing guard --
        /// so there is no list of scenario names to keep in step with the ones the block
        /// actually uses. A man doing any of them has stopped, and a man who has stopped can
        /// hold a conversation.
        /// </summary>
        private static bool PostedUp(Ped ped)
        {
            try
            {
                if (ped == null || !ped.Exists()) return false;
                if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, ScenarioTask)) return true;

                // Sitting counts too, and sitting is not a scenario.
                return ped.Velocity.Length() < 0.2f &&
                       Function.Call<bool>(Hash.IS_PED_SITTING_IN_ANY_VEHICLE, ped.Handle) == false &&
                       Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, ped.Handle);
            }
            catch
            {
                return false;
            }
        }

        private void Act(Ped player)
        {
            var ped = _target;
            if (ped == null || !ped.Exists()) return;

            _spokenTo[ped.Handle] = Game.GameTime;
            if (_spokenTo.Count > 200) _spokenTo.Clear();

            if (_targetIsBody)
            {
                OverTheBody(player, ped);
                return;
            }

            if (PostedUp(ped)) StartTalking(ped);
            else Nod(player, ped);

            _target = null;
        }

        // ---- the living --------------------------------------------------------

        /// <summary>What you say in passing, and what he says back.</summary>
        private static readonly string[] Greetings =
        {
            "What's happening.",
            "Aye.",
            "You good?",
            "Hold it down out here.",
            "Everything straight?",
            "Cool, cool.",
            "Stay up.",
        };

        private static readonly string[] GreetSpeech = { "GENERIC_HI", "GENERIC_HOWS_IT_GOING" };

        private static readonly Random Rng = new Random();

        /// <summary>
        /// A nod. Both of them speak, because one man greeting nobody is worse than silence.
        /// </summary>
        private void Nod(Ped player, Ped ped)
        {
            Say(player, GreetSpeech);
            Dialogue.Say("", Pick(Greetings));

            // Him a moment later, so it reads as an answer rather than a chorus.
            Say(ped, GreetSpeech);

            try
            {
                // He looks at you while he says it. A greeting delivered to the middle
                // distance is a man ignoring you.
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, ped.Handle, player.Handle, 3000, 0, 2);
            }
            catch
            {
                // He will keep looking wherever he was looking.
            }
        }

        /// <summary>
        /// He is posted up, so there is time. Opens the proper screen.
        /// </summary>
        private void StartTalking(Ped ped)
        {
            if (_talk == null) return;

            var mine = _crew == null ? null : _crew.Current;
            var name = mine == null ? "Homie" : mine.Name;

            var node = new DialogueNode(name, Pick(Openers))
            {
                SpeakerColour = mine == null ? Palette.Text : mine.Colour
            };

            node.Say("What's the word out here?", () => Reply(name, mine, Word));
            node.Say("Everything straight?", () => Reply(name, mine, Straight));
            node.Leave("Stay up.");

            _talk.Speaker = ped;
            _talk.TheirVoice = GreetSpeech;
            _talk.Title = "";
            _talk.Open(node, ped);
        }

        private DialogueNode Reply(string name, GangDef mine, string[] lines)
        {
            var node = new DialogueNode(name, Pick(lines))
            {
                SpeakerColour = mine == null ? Palette.Text : mine.Colour
            };

            node.Leave("Alright then.");
            return node;
        }

        private static readonly string[] Openers =
        {
            "Aye. You out here early.",
            "What's happening, boy.",
            "Man, I been out here all day. All day.",
            "You seen anybody come through here?",
            "Everything quiet. For now.",
            "Boy, where you been at?",
        };

        private static readonly string[] Word =
        {
            "Same as always. Corner's moving, block's quiet, everybody eating a little.",
            "Ain't nothing. Couple cars come through slow last night and kept going.",
            "They been posting on the timeline all week and ain't nobody slid. That's the word.",
            "Quiet. And quiet round here means somebody's planning something.",
            "Somebody said they seen a car parked up on the corner twice. Might be nothing.",
        };

        private static readonly string[] Straight =
        {
            "Yeah, we good. Just holding it down.",
            "All good. Keep your head on a swivel out here though.",
            "I'm solid. Been out here since this morning, ain't seen nothing.",
            "We straight. You the one been running around all day.",
            "Long as the block's quiet, I'm quiet.",
        };

        // ---- the dead ----------------------------------------------------------

        /// <summary>One of ours, on the pavement. There is only one thing to say.</summary>
        private static readonly string[] OverOurs =
        {
            "Fam down! We got fam down out here!",
            "Fam down. Somebody call somebody.",
            "Nah. Nah, not him. Fam down.",
            "Fam down! Get somebody over here!",
        };

        /// <summary>Anybody else.</summary>
        private static readonly string[] OverTheirs =
        {
            "That's one less.",
            "Should've stayed on his own block.",
            "Somebody's gonna come looking for him.",
            "That's how it goes out here.",
            "He knew what this was.",
            "One for the block.",
            "Leave him. Somebody'll come.",
        };

        private static readonly string[] GriefSpeech =
        {
            "GENERIC_SHOCKED_HIGH", "GENERIC_SHOCKED_MED", "GENERIC_CURSE_HIGH"
        };

        private static readonly string[] ColdSpeech =
        {
            "GENERIC_INSULT_HIGH", "GENERIC_CURSE_MED", "GENERIC_YES"
        };

        private void OverTheBody(Ped player, Ped ped)
        {
            var ours = IsOneOfOurs(ped);

            Say(player, ours ? GriefSpeech : ColdSpeech);
            Dialogue.Say("", Pick(ours ? OverOurs : OverTheirs));

            try
            {
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, player.Handle, ped.Handle, 2500, 0, 2);
            }
            catch
            {
                // He says it either way.
            }

            _target = null;
        }

        // ---- plumbing ----------------------------------------------------------

        private static string Pick(string[] lines)
        {
            return lines == null || lines.Length == 0 ? "" : lines[Rng.Next(lines.Length)];
        }

        private static void Say(Ped ped, string[] speech)
        {
            if (ped == null || !ped.Exists() || !ped.IsAlive) return;

            try
            {
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, ped.Handle);
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle,
                              Pick(speech), "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        /// <summary>Nothing is owned, so there is nothing to put back.</summary>
        public void RestoreWorld()
        {
            _spokenTo.Clear();
            _target = null;
        }
    }
}
