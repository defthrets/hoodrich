using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Saying hello to anybody, and saying more than hello to anybody who is worth it.
    ///
    /// EVERY WORD OF IT IS ROCKSTAR'S. Not one line here is written, recorded or invented --
    /// they are speech names out of the game's own banks, played through the game's own
    /// ambient speech system on the ped's own voice, which means they are lip-synced, they are
    /// in the right accent, and a Vagos and a stockbroker say completely different things
    /// because the game already decided what each of them sounds like. A mod cannot write
    /// dialogue this good and should not try.
    ///
    /// TWICE, AND THE SECOND TIME IS DIFFERENT. Press once and it is a nod in the street: he
    /// says hello, they say hello, nobody stops walking. Press again on the same person and
    /// they stop and have a conversation.
    ///
    /// AND IT IS THEIR CONVERSATION, NOT HIS. CHAT_STATE and CHAT_RESP are the game's own
    /// pavement chat -- the lines one pedestrian says to another when the pair of them stop,
    /// and they are whole SENTENCES rather than noises, different for every voice in the game.
    /// So a tramp tells you a tramp's story and a woman on Rockford Hills tells you hers, and
    /// nothing here had to write either. He says hello, they talk, he says yeah, they talk
    /// some more, and both of them say goodbye.
    ///
    /// NOBODY IS PROVOKED. Franklin's voice also carries a greeting for a Balla, a Vago, a
    /// cop, a tramp, a junkie, a hippy and a hillbilly, and picking the right one for whoever
    /// was stood there was the first version of this. It was a lovely thing that produced the
    /// wrong scene: most of those lines are not greetings, they are what he makes of somebody
    /// he has just clocked, and he delivers them like it. Opening with what you think of a
    /// man's sort is starting something. See Opener.
    ///
    /// That split is the point. A greeting you can only do the long version of is a greeting
    /// you stop using, because most of the time you want to nod at somebody and keep walking.
    /// A greeting that is ONLY a nod has nowhere to go. Two presses costs nothing and gives
    /// both.
    ///
    /// THE LOWEST PRIORITY THING IN THE MOD FOR THE CONTEXT KEY. Forty other things offer it
    /// and every one of them is somewhere specific -- a boot, a counter, a wardrobe. This is
    /// offered everywhere, to anybody, so it is the only one that can be in the way. It asks
    /// Help whether the corner is spoken for and stands down if it is.
    /// </summary>
    internal sealed class Greeting
    {
        // ---- who is close enough, and in front ---------------------------------------

        /// <summary>
        /// How near, and how far round.
        ///
        /// TWO AND A HALF METRES AND A NARROW CONE. A greeting has to be aimed or it is not a
        /// greeting -- stood in a crowd with a wide arc you would be offered whichever of six
        /// people the sort happened to return first, and press it to find out. The cone is
        /// tight enough that the person offered is the one you are looking at.
        /// </summary>
        private const float Reach = 2.6f;
        private const float Cone = 0.62f;

        /// <summary>How often the street is looked at. Not a per-frame job.</summary>
        private const int LookEveryMs = 140;

        /// <summary>
        /// How long the second press stays available on the same person.
        ///
        /// Long enough to be a decision and short enough that walking away ends it. Nodding at
        /// a man, crossing the road and nodding at him again an hour later is two nods.
        /// </summary>
        private const int AgainMs = 14000;

        /// <summary>And how long before he will say hello to the same person from scratch again.</summary>
        private const int RestMs = 25000;

        /// <summary>Set by Main: off while a screen is up or the mod is standing down.</summary>
        public Func<bool> Busy;

        /// <summary>Set by Main: which gang somebody is in, or null. See Affiliation.GangOf.</summary>
        public Func<Ped, string> Gang;

        private int _nextLook;

        private int _who;
        private int _spokeAt;
        private int _restUntil;

        // ---- the long one, while it runs ---------------------------------------------

        private Ped _with;
        private int _stage;
        private int _nextBeat;

        public void Update()
        {
            var now = Game.GameTime;

            if (_with != null) { Running(now); return; }

            if (now < _nextLook) return;
            _nextLook = now + LookEveryMs;

            try
            {
                if (Busy != null && Busy()) return;

                var me = Game.Player.Character;

                if (me == null || !me.Exists() || !me.IsAlive) return;
                if (me.IsInVehicle() || me.IsRagdoll || me.IsInCombat) return;

                // Not while he is holding something at somebody. A greeting offered down a gun
                // barrel is the mod misreading the room.
                if (me.IsShooting || Game.Player.IsAiming) return;

                var them = Facing(me);
                if (them == null) return;

                // LAST IN THE QUEUE FOR THE KEY. See the note on the class, and Help.Taken.
                if (Help.Taken) return;

                var second = them.Handle == _who && now - _spokeAt < AgainMs;

                if (!second && them.Handle == _who && now < _restUntil) return;

                // NO PROMPT. Every other context action in the mod is somewhere -- a boot, a
                // counter, a door -- and a box in the corner is how you find out it is there.
                // This one is everywhere there is a person, which is most of the time you are
                // out of a car, so a prompt for it is a box on the screen permanently. It
                // would stop being a hint about half an hour in and be furniture for the rest
                // of the game.
                //
                // Help.Taken stays, and it is now doing the ONLY job it was doing that
                // mattered: making sure this does not eat the key from under something that
                // does have a prompt up. A shop door you are stood in beats the pedestrian
                // walking past it, and now it does so silently.
                if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

                if (second) Start(me, them, now);
                else Nod(me, them, now);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not say hello: " + ex.Message);
            }
        }

        /// <summary>
        /// The nearest person in front of him, or nobody.
        ///
        /// EVERYTHING THE MOD IS ALREADY HANDLING IS SKIPPED. A man walking over to buy, a
        /// homie following him, a leader with a job -- all of those have their own prompt on
        /// the same key and their own idea of what a conversation is, and offering to say hello
        /// on top of it would put two prompts on one man.
        /// </summary>
        private Ped Facing(Ped me)
        {
            var eye = me.Position;
            var way = me.ForwardVector;

            Ped best = null;
            var near = Reach;

            foreach (var ped in World.GetNearbyPeds(me, Reach))
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (ped.Handle == me.Handle) continue;
                if (ped.IsInVehicle()) continue;
                if (ped.IsRagdoll || ped.IsInCombat || ped.IsFleeing) continue;

                // Somebody else's, and somebody else's prompt.
                if (Dealing.Serving.Is(ped)) continue;

                var gap = ped.Position - eye;
                gap.Z = 0f;

                var d = gap.Length();
                if (d < 0.2f || d > near) continue;

                gap = gap * (1f / d);

                if (Vector3.Dot(way, gap) < Cone) continue;

                near = d;
                best = ped;
            }

            return best;
        }

        // ---- the nod ------------------------------------------------------------------

        /// <summary>
        /// Hello, and hello back. Nobody stops what they are doing.
        ///
        /// A HEAD TURN RATHER THAN A TASK. Looking at somebody as you pass them is what this
        /// is, and TASK_LOOK_AT_ENTITY does exactly that without touching what either of them
        /// was doing -- so a man on his phone stays on his phone and glances up, which is what
        /// happens. Turning him to face you would be a conversation, and the conversation is
        /// the other press.
        /// </summary>
        private void Nod(Ped me, Ped them, int now)
        {
            _who = them.Handle;
            _spokeAt = now;
            _restUntil = now + RestMs;

            try
            {
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, me.Handle, them.Handle, LookMs, 0, 2);
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, them.Handle, me.Handle, LookMs, 0, 2);

                Speak(me, them.Gender == Gender.Female ? "GENERIC_HI_FEMALE" : "GENERIC_HI_MALE");

                // Their answer is the same system on their own voice, so a Vagos, a banker and
                // a tramp all say hello in three different ways for nothing.
                _with = null;
                _reply = them;
                _replyAt = now + ReplyMs;
                _replyWith = "GENERIC_HI";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not nod: " + ex.Message);
            }
        }

        private const int LookMs = 2500;
        private const int ReplyMs = 900;

        private Ped _reply;
        private int _replyAt;
        private string _replyWith = "";

        // ---- the conversation ----------------------------------------------------------

        /// <summary>
        /// The longer one: they stop, they face each other, and he opens with what he actually
        /// makes of somebody like them.
        /// </summary>
        private void Start(Ped me, Ped them, int now)
        {
            _with = them;
            _stage = 0;
            _nextBeat = now;

            _who = them.Handle;
            _spokeAt = now;
            _restUntil = now + RestMs;

            try
            {
                // BOTH OF THEM, and it is the difference between a greeting and a conversation.
                // The nod above only turns heads; this squares them up.
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, them.Handle, me.Handle, TurnMs);
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, them.Handle, me.Handle, TalkMs, 0, 2);
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, me.Handle, them.Handle, TalkMs, 0, 2);

                // Held still for the length of it. Without this the game's own wander pass
                // walks him off mid-sentence, which is the single thing that would make this
                // read as broken rather than as rude.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, them.Handle, true);
            }
            catch
            {
                // He talks at them anyway.
            }
        }

        private const int TurnMs = 900;
        private const int TalkMs = 12000;

        /// <summary>
        /// One beat at a time.
        ///
        /// A CLOCK RATHER THAN A WAIT. Every line here is fired and forgotten -- the speech
        /// system owns how long it takes and there is no way to be told when it finished -- so
        /// the exchange is spaced on a timer picked to be a beat longer than a short line and
        /// a beat shorter than a long one. Two people talking over each other slightly is what
        /// two people talking sounds like; a two second silence between every line is a
        /// cutscene.
        /// </summary>
        private void Running(int now)
        {
            try
            {
                var me = Game.Player.Character;

                var lost = me == null || !me.Exists() || !me.IsAlive
                           || _with == null || !_with.Exists() || !_with.IsAlive
                           || me.IsInVehicle() || _with.IsInCombat
                           || me.Position.DistanceTo(_with.Position) > Walkaway;

                if (lost) { Done(); return; }

                if (now < _nextBeat) return;

                switch (_stage)
                {
                    case 0:
                        // HE OPENS WITH HELLO AND NOTHING ELSE. See Opener.
                        Speak(me, Opener(_with));
                        _nextBeat = now + BeatMs;
                        break;

                    case 1:
                        // AND THEN THEY TALK. CHAT_STATE is the game's own "somebody telling
                        // you a thing" -- it is the line one pedestrian says to another when
                        // the pair of them stop on a pavement, and it is a whole sentence
                        // rather than a noise. Every voice in the game has its own set, so the
                        // story is theirs and not ours.
                        Speak(_with, "CHAT_STATE");
                        Gesture(_with, "gesture_hello");
                        _nextBeat = now + TellMs;
                        break;

                    case 2:
                        // He is listening, which on his voice is a short one.
                        Speak(me, "GENERIC_YES");
                        _nextBeat = now + BeatMs;
                        break;

                    case 3:
                        Speak(_with, "CHAT_RESP");
                        Gesture(_with, "gesture_hand_right");
                        _nextBeat = now + TellMs;
                        break;

                    case 4:
                        Speak(me, "GENERIC_THANKS");
                        _nextBeat = now + BeatMs;
                        break;

                    case 5:
                        // THE SECOND HALF OF THE STORY. Two CHAT_STATEs is how the game's own
                        // street pairs do it -- they go back and forth several times, and one
                        // exchange each is a greeting rather than a conversation.
                        Speak(_with, "CHAT_STATE");
                        Gesture(_with, "gesture_nod_yes_soft");
                        _nextBeat = now + TellMs;
                        break;

                    case 6:
                        Speak(me, "GENERIC_BYE");
                        _nextBeat = now + BeatMs;
                        break;

                    case 7:
                        Speak(_with, "GENERIC_BYE");
                        _nextBeat = now + BeatMs;
                        break;

                    default:
                        Done();
                        return;
                }

                _stage++;
            }
            catch (Exception ex)
            {
                Log.Debug("The conversation fell over: " + ex.Message);
                Done();
            }
        }

        /// <summary>How far he can walk off before it is over, and how long a line is given.</summary>
        private const float Walkaway = 6f;
        private const int BeatMs = 1500;

        /// <summary>
        /// And how long one of THEIR lines gets, which is longer.
        ///
        /// CHAT_STATE IS A SENTENCE AND GENERIC_YES IS A WORD. Giving both the same beat talks
        /// over the half of this that is worth listening to -- their line is the story and his
        /// is somebody going "yeah". Three and a half seconds is long enough for the longest of
        /// them and short enough that a short one does not leave a hole.
        private const int TellMs = 3500;

        /// <summary>Everybody let go of, whichever way it ended.</summary>
        private void Done()
        {
            if (_with != null && _with.Exists())
            {
                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _with.Handle, false);
                    Function.Call(Hash.TASK_CLEAR_LOOK_AT, _with.Handle);
                    _with.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // He goes back to his day either way.
                }
            }

            _with = null;
            _stage = 0;
        }

        // ---- what he makes of them ------------------------------------------------------

        /// <summary>
        /// The line he opens with. Hello, and that is all it is.
        ///
        /// IT USED TO BE A WHOLE CHARACTER READ AND THAT WAS A MISTAKE. Franklin's voice
        /// carries a different greeting for a Balla, a Vago, a cop, a tramp, a junkie, a
        /// hippy, a hipster and a hillbilly -- eleven of them, in character, already recorded
        /// -- and picking the right one for whoever is stood there was a lovely thing that
        /// produced the wrong scene. Most of those lines are not greetings. They are what
        /// Franklin says about somebody he has just clocked, and he says them like it. Walking
        /// up to a man and opening with what you make of his sort is starting something.
        ///
        /// ONE IS KEPT, AND IT IS THE ONLY WARM ONE IN THE SET. GREET_GANG_FAMILIES is him
        /// greeting his OWN people, which is the one case where the line is friendlier than a
        /// plain hello rather than sharper. Everybody else gets hello.
        ///
        /// The rest are still in the game and still work. If they are ever wanted, they belong
        /// somewhere that is about a reaction rather than somewhere that is about starting a
        /// conversation -- and this is the second one.
        /// </summary>
        private string Opener(Ped them)
        {
            var she = them.Gender == Gender.Female;

            var gang = Gang == null ? null : Gang(them);

            // His own set, and nobody else's. See the note above.
            if (gang == "families") return she ? "GREET_GANG_FAMILIES_F" : "GREET_GANG_FAMILIES_M";

            return she ? "GENERIC_HI_FEMALE" : "GENERIC_HI_MALE";
        }

        // ---- the plumbing ---------------------------------------------------------------

        /// <summary>
        /// One line, on their own voice.
        ///
        /// SPEECH_PARAMS_FORCE, because the polite parameters let the game decide it is busy
        /// and drop the line -- which for a greeting the player just pressed a button for is
        /// the feature not working. It is the same parameter every other speaking thing in this
        /// mod uses and for the same reason.
        /// </summary>
        private static void Speak(Ped who, string line)
        {
            if (who == null || !who.Exists() || string.IsNullOrEmpty(line)) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle, line, "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A line that will not play is a quieter conversation, not a broken one.
            }
        }

        /// <summary>
        /// A hand movement to go with it, upper body only.
        ///
        /// SECONDARY AND UPPER BODY: 48 is those two flags together, and they are what let a
        /// man gesture without standing up out of whatever he was doing. The game's own street
        /// conversations look exactly like this -- the hands move and the feet do not.
        /// </summary>
        private static void Gesture(Ped who, string clip)
        {
            if (who == null || !who.Exists()) return;

            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, GestureDict);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, GestureDict)) return;

                Function.Call(Hash.TASK_PLAY_ANIM, who.Handle, GestureDict, clip,
                              4f, -4f, -1, GestureFlags, 0f, false, 0, false);
            }
            catch
            {
                // He talks with his hands in his pockets.
            }
        }

        private const string GestureDict = "gestures@m@standing@casual";
        private const int GestureFlags = 48;

        /// <summary>
        /// The reply to a nod, a beat later.
        ///
        /// Called from the same pump as everything else rather than being its own timer,
        /// because one field and an if is cheaper than another moving part.
        /// </summary>
        public void Tick()
        {
            if (_reply == null) return;

            var now = Game.GameTime;
            if (now < _replyAt) return;

            var them = _reply;
            var line = _replyWith;

            _reply = null;
            _replyWith = "";

            Speak(them, line);
        }

        /// <summary>Everything let go of. For a teardown.</summary>
        public void Clear()
        {
            Done();

            _reply = null;
            _replyWith = "";
            _who = 0;
        }
    }
}
