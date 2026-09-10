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
    /// FRANKLIN HAS A WHOLE GREETING VOCABULARY NOBODY EVER HEARS. His voice carries
    /// GREET_GANG_FAMILIES_M, GREET_GANG_BALLAS_F, GREET_COP, GREET_BUM, GREET_JUNKIE,
    /// GREET_HIPPY_M, GREET_HIPSTER_F, GREET_HILLBILLY_M, GREET_ATTRACTIVE_F, GREET_STRONG_M
    /// and more -- lines the story only ever used in a handful of scripted moments. They are
    /// sat in the audio the player already has, and the whole of the work here is picking the
    /// right one for the person actually stood in front of you.
    ///
    /// TWICE, AND THE SECOND TIME IS DIFFERENT. Press once and it is a nod in the street: he
    /// says hello, they say hello, nobody stops walking. Press again on the same person and it
    /// becomes a conversation -- they turn to face each other, he opens with whatever he
    /// actually thinks of somebody like them, they answer back, and it runs four exchanges
    /// before both of them say goodbye.
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

                Help.ShowThisFrame(second
                    ? "Press ~INPUT_CONTEXT~ to talk to them."
                    : "Press ~INPUT_CONTEXT~ to say something.");

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
                        // HIS OPENER, and the one line in this that is chosen rather than
                        // generic. See Opener.
                        Speak(me, Opener(_with));
                        _nextBeat = now + BeatMs;
                        break;

                    case 1:
                        Speak(_with, "CHAT_RESP");
                        Gesture(_with, "gesture_hello");
                        _nextBeat = now + BeatMs;
                        break;

                    case 2:
                        Speak(me, "GENERIC_YES");
                        _nextBeat = now + BeatMs;
                        break;

                    case 3:
                        Speak(_with, "CHAT_STATE");
                        Gesture(_with, "gesture_hand_right");
                        _nextBeat = now + BeatMs + 400;
                        break;

                    case 4:
                        Speak(me, "GENERIC_BYE");
                        _nextBeat = now + BeatMs;
                        break;

                    case 5:
                        Speak(_with, "GENERIC_BYE");
                        Gesture(_with, "gesture_nod_yes_soft");
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
        private const int BeatMs = 2100;

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
        /// The line he opens with, chosen for who they actually are.
        ///
        /// THIS IS THE WHOLE FEATURE. Rockstar wrote Franklin a different greeting for a
        /// Families member, a Balla, a Vago, a cop, a tramp, a junkie, a hippy, a hipster, a
        /// hillbilly, a big lad and a good-looking woman -- eleven ways of saying hello, in
        /// character, already recorded and already on the player's disk. All that was missing
        /// was somebody asking who is stood there.
        ///
        /// GANG FIRST, BECAUSE IT OUTRANKS EVERYTHING. A Balla in a hipster's shirt is a Balla.
        /// The relationship group is the game's own answer to that question and the mod already
        /// reads it everywhere else.
        ///
        /// Then the job, then the look, and a plain hello for everybody the game has nothing
        /// specific to say about -- which is most people, and is fine. A stranger on the street
        /// getting "hey" is not a missing feature, it is what happens.
        /// </summary>
        private string Opener(Ped them)
        {
            var she = them.Gender == Gender.Female;

            var gang = Gang == null ? null : Gang(them);

            if (!string.IsNullOrEmpty(gang))
            {
                switch (gang)
                {
                    case "families": return she ? "GREET_GANG_FAMILIES_F" : "GREET_GANG_FAMILIES_M";
                    case "ballas": return she ? "GREET_GANG_BALLAS_F" : "GREET_GANG_BALLAS_M";
                    case "vagos": return she ? "GREET_GANG_VAGOS_F" : "GREET_GANG_VAGOS_M";
                }
            }

            var model = (Names.Of(them.Model.Hash) ?? "").ToLowerInvariant();

            if (Cop(them, model)) return "GREET_COP";

            if (Has(model, "tramp", "hobo", "bevhills_bum", "vagrant")) return "GREET_BUM";
            if (Has(model, "methhead", "acult", "dopey", "junkie")) return "GREET_JUNKIE";
            if (Has(model, "hippy", "hippie")) return she ? "GREET_HIPPY_F" : "GREET_HIPPY_M";
            if (Has(model, "hipster", "downtown", "vinewood")) return she ? "GREET_HIPSTER_F" : "GREET_HIPSTER_M";
            if (Has(model, "hillbilly", "rurmeth", "paparazzi", "trucker")) return "GREET_HILLBILLY_M";
            if (Has(model, "muscl", "bouncer", "chemwork", "armoured")) return "GREET_STRONG_M";

            // The good-looking one is only offered to the models the game itself dresses that
            // way, rather than to every woman in the city, because Franklin saying it to a
            // pensioner is a joke the mod would be making on his behalf.
            if (she && Has(model, "beach", "bev", "vinewood", "fitness", "hotposh", "bikini")) return "GREET_ATTRACTIVE_F";

            return she ? "GENERIC_HI_FEMALE" : "GENERIC_HI_MALE";
        }

        private static bool Cop(Ped them, string model)
        {
            if (Has(model, "cop", "sheriff", "ranger", "swat", "prisguard", "fib", "hwaycop")) return true;

            try
            {
                // The game's own answer, for anything the names miss: 6 is police, 27 army.
                var kind = Function.Call<int>(Hash.GET_PED_TYPE, them.Handle);
                return kind == 6 || kind == 27;
            }
            catch
            {
                return false;
            }
        }

        private static bool Has(string model, params string[] bits)
        {
            if (string.IsNullOrEmpty(model)) return false;

            for (var i = 0; i < bits.Length; i++)
            {
                if (model.IndexOf(bits[i], StringComparison.Ordinal) >= 0) return true;
            }

            return false;
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
