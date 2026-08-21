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
    /// Nothing here opens a screen. A screen is for choosing and you are not choosing
    /// anything -- you are saying hello to somebody you see every day. Press it and one of you
    /// talks, press it again and the other one answers, and whose turn it is is remembered per
    /// person so walking back up to the same man carries on rather than restarting.
    ///
    /// One rule decides what you are looking at, and it comes from the world rather than a
    /// menu: a body outranks a living man. If somebody is down within reach, that is what you
    /// are looking at -- standing over a friend to greet the man behind him is grotesque.
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
        /// Long enough to stop the key repeating, short enough to hold a conversation.
        ///
        /// This was four seconds, when a press was a one-off greeting. It is a back-and-forth
        /// now, so the gap only has to be longer than a keypress -- otherwise leaning on the
        /// button plays both halves at once.
        /// </summary>
        private const int GreetCooldownMs = 700;

        /// <summary>And a body is remarked on once, not every time you walk past it.</summary>
        private const int BodyCooldownMs = 20000;

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

        /// <summary>
        /// Watches the key. Nothing is drawn.
        ///
        /// There was a help prompt here -- "press this to say something", "press this to check
        /// on him" -- and it was the wrong idea. Everything else in the mod that owns this key
        /// owns a PLACE, and a prompt is how you learn a place exists. A man stood next to you
        /// is not a place, and captioning every homie on the block turns walking down your own
        /// street into reading. He just talks when you press it.
        /// </summary>
        public void Draw()
        {
            if (_target == null || !_target.Exists()) return;
            if (_talk != null && _talk.IsOpen) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

            Act(player);
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

            Swap(player, ped);
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
        /// One line, and then it is the other one's go.
        ///
        /// There was a whole dialogue screen here for a homie who was stood still, which is the
        /// wrong shape for this: a screen is for choosing, and you are not choosing anything --
        /// you are saying hello to somebody you see every day. So there is no screen. Press it
        /// and one of you talks; press it again and the other one answers.
        ///
        /// Whose turn it is is remembered per person, so walking up to the same man twice
        /// carries on where you left off rather than restarting the same exchange.
        /// </summary>
        private void Swap(Ped player, Ped ped)
        {
            var yours = !_theirs.Contains(ped.Handle);

            if (yours)
            {
                Say(player, GreetSpeech);
                Dialogue.Say("Franklin", Pick(Greetings));
                _theirs.Add(ped.Handle);
            }
            else
            {
                Say(ped, GreetSpeech);
                Dialogue.Say("", Pick(Answers));
                _theirs.Remove(ped.Handle);
            }

            try
            {
                // Whoever is listening looks at whoever is talking. A line delivered to the
                // middle distance is a man ignoring you.
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, ped.Handle, player.Handle, 4000, 0, 2);
            }
            catch
            {
                // He will keep looking wherever he was looking.
            }

            if (_theirs.Count > 200) _theirs.Clear();
        }

        /// <summary>Handles whose next line is theirs rather than yours.</summary>
        private readonly HashSet<int> _theirs = new HashSet<int>();

        /// <summary>What he says back.</summary>
        private static readonly string[] Answers =
        {
            "Same old. Holdin' it down.",
            "Aye. Ain't nothin'.",
            "We good over here.",
            "Been out here all day, boy.",
            "You know how it go.",
            "Straight. You?",
            "Everything quiet. For now.",
            "Man, don't even ask.",
            "All day, every day.",
            "Cool. Stay up out here.",
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
            Dialogue.Say("Franklin", Pick(ours ? OverOurs : OverTheirs));

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
