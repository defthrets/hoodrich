using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>
    /// Actually taking it: the animation, and the thing in his hand while he does.
    ///
    /// A HIGH THAT ARRIVES OUT OF NOWHERE IS A CHEAT CODE. The screen went green and the man
    /// carried on walking -- nothing happened to HIM, something happened to the picture. The
    /// difference between a buff and a habit is that you watch yourself do it, and you cannot
    /// do anything else while you are.
    ///
    /// SO THE EFFECT DOES NOT LAND UNTIL THE RITUAL FINISHES. He stops, he takes it, and THEN
    /// the world changes. Two or three seconds, which is the same beat the game gives its own
    /// smoking and drinking scenarios.
    ///
    /// TWO WAYS TO PLAY ONE, AND THE ORDER MATTERS.
    ///
    ///   1. An ANIM DICT with a prop we attach ourselves. Right prop, right motion, and the
    ///      only way to get a syringe or a pipe into his hand, because no scenario has one.
    ///
    ///   2. A SCENARIO, which brings its own prop and cannot be told to hold anything else.
    ///      WORLD_HUMAN_SMOKING_POT hands him a lit joint for free and it is exactly right for
    ///      weed and nothing else.
    ///
    /// EVERY NAME IN HERE IS A GUESS UNTIL THE GAME ACCEPTS IT, which is the same problem the
    /// timecycles have and it gets the same answer. REQUEST_ANIM_DICT on a dictionary that does
    /// not exist never loads and never complains; TASK_START_SCENARIO_IN_PLACE with a bad name
    /// does nothing at all. So both are LADDERS, both are checked afterwards -- HAS_ANIM_DICT_
    /// LOADED for one and IS_PED_USING_SCENARIO for the other -- and whichever rung took is
    /// written to the log. A wrong guess costs a line in a file rather than a feature that
    /// quietly does nothing.
    /// </summary>
    internal sealed class Ritual
    {
        /// <summary>What he is doing, and with what.</summary>
        internal sealed class Recipe
        {
            /// <summary>Anim dictionaries to try, in order.</summary>
            public string[] Dicts = new string[0];

            /// <summary>The clip inside whichever dictionary loaded.</summary>
            public string Clip = "";

            /// <summary>
            /// Dictionary and clip together, in pairs, tried in order.
            ///
            /// ONE CLIP NAME FOR EVERY DICTIONARY IN A LADDER IS A LADDER THAT CANNOT WORK,
            /// and that is what Dicts plus Clip was. amb@world_human_aa_smoke@male@idle_a does
            /// not contain a clip called "base" -- its clips are named after the idle -- so
            /// asking for one got exactly what asking the game for a name it does not have
            /// always gets: silence, and nothing playing. The dictionary loaded, the call was
            /// accepted, the log said "Ritual anim: ... / base", and the man stood there.
            ///
            /// Pairs are how Entourage has always done this and it is the reason its stations
            /// animate. Same shape: dict, clip, dict, clip.
            /// </summary>
            public string[] Pairs = new string[0];

            /// <summary>Prop models to try, in order. The first the install has is used.</summary>
            public string[] Props = new string[0];

            /// <summary>Where the prop sits in his hand, in metres.</summary>
            public Vector3 Sits = new Vector3(0.0f, 0.0f, 0.0f);

            /// <summary>And how it is turned, in degrees.</summary>
            public Vector3 Turned = new Vector3(0f, 0f, 0f);

            /// <summary>What to fall back on when no dictionary loads. Brings its own prop.</summary>
            public string Scenario = "";

            /// <summary>How long it takes before the effect lands.</summary>
            public int Ms = 2600;

            /// <summary>
            /// And how long he carries on doing it afterwards. Nought stops at Ms.
            ///
            /// A CIGARETTE IS NOT A BUTTON PRESS. Everything else in here is a single act with
            /// a beginning and an end -- a needle, a line, a pill -- but smoking is something
            /// you stand there and DO, and four seconds of it is a man taking one drag and
            /// marching off. So the joint keeps going after the high has landed, and it ends
            /// the way it would: when you walk away from it, or when it is finished.
            /// </summary>
            public int Linger;

            /// <summary>He goes down at the end of it. The junkie nod.</summary>
            public bool Slump;
        }

        private Prop _held;
        private string _dict = "";
        private string _clip = "";
        private bool _scenario;

        public bool Busy { get; private set; }

        /// <summary>
        /// True once the effect has landed and he is only still doing it for the look.
        ///
        /// The difference matters to everything upstairs: taking another one while he is
        /// mid-needle is nonsense, and taking another one while he is finishing a joint is
        /// Tuesday.
        /// </summary>
        public bool Landed { get; private set; }

        private int _until;
        private int _lingerUntil;
        private int _linger;

        /// <summary>Set while a dictionary is still streaming, so it can be asked for again.</summary>
        private Recipe _waiting;
        private Recipe _watching;
        private int _giveUpAt;

        /// <summary>How long a dictionary gets to arrive before the scenario takes over.</summary>
        private const int StreamMs = 1200;

        /// <summary>How fast counts as walking off, and how long he gets before it is asked.</summary>
        private const float MovedAt = 0.35f;
        private const int MovedAfterMs = 600;

        /// <summary>When to ask whether the scenario actually started, and what to ask about.</summary>
        private int _checkAt;
        private string _checking = "";

        private const int CheckMs = 400;

        /// <summary>PH_R_Hand -- the non-deforming helper the animators hang props on.</summary>
        private const int RightHand = 28422;

        /// <summary>Start it. Returns false only if there is nobody to do it.</summary>
        public bool Start(Recipe recipe)
        {
            if (recipe == null) return false;

            var me = Game.Player.Character;
            if (me == null || !me.Exists() || !me.IsAlive) return false;

            Stop();

            Busy = true;
            Landed = false;

            _until = Game.GameTime + recipe.Ms;
            _linger = recipe.Linger;
            _lingerUntil = 0;

            _rung = 0;
            _watchAt = 0;
            _watching = null;

            Hold(recipe, me);

            // NOTHING TO STREAM MEANS NOTHING TO WAIT FOR, and this is why the joint looked
            // like it was not happening.
            //
            // The wait exists because an anim dictionary is not in memory on the frame you ask
            // for it. A recipe with NO dictionaries has nothing to wait for -- and the joint is
            // exactly that, deliberately: it is a scenario and only a scenario, because the
            // game's own smoking-pot scenario is better than anything we could assemble. So it
            // sat doing nothing for the first one-point-two seconds of a four-second ritual,
            // and then started smoking with under three left, most of which is the blend in.
            //
            // Straight to it when there is no ladder to climb.
            // THE LADDER, WHICHEVER WAY IT WAS WRITTEN. This asked recipe.Dicts, which is
            // empty on every recipe that names Pairs instead -- so the moment the joint and
            // the meth pipe were given real clip names they stopped having any dictionaries at
            // all as far as this line was concerned, and went straight to the scenario on the
            // same millisecond they started. The log said "Ritual scenario" where it had said
            // "Ritual anim" the build before, which is the tell, and both drugs went back to
            // being a man standing still holding something.
            var rungs = Rungs(recipe);

            if (rungs.Length < 2)
            {
                Scenario(recipe, me);
                return true;
            }

            _waiting = recipe;
            _giveUpAt = Game.GameTime + StreamMs;

            // Asked for now and played when it arrives. A dictionary requested and played on
            // the same frame is the one thing that never works -- it is not in memory yet and
            // the first play is silently dropped.
            for (var i = 0; i + 1 < rungs.Length; i += 2)
            {
                try { Function.Call(Hash.REQUEST_ANIM_DICT, rungs[i]); }
                catch { }
            }

            return true;
        }

        /// <summary>
        /// Called every tick while it runs. Returns true once it is done.
        ///
        /// The dictionary is re-attempted rather than assumed, and the scenario comes in when
        /// the wait runs out -- so an install missing every dictionary in the ladder still gets
        /// a man who stops and does something, rather than a man who keeps walking.
        /// </summary>
        public bool Update()
        {
            if (!Busy) return false;

            var now = Game.GameTime;

            var me = Game.Player.Character;

            if (me == null || !me.Exists() || !me.IsAlive)
            {
                Stop();
                return true;
            }

            if (_waiting != null)
            {
                if (Play(_waiting, me)) { _watching = _waiting; _waiting = null; }
                else if (now >= _giveUpAt)
                {
                    Scenario(_waiting, me);
                    _waiting = null;
                }
            }

            // A moment after it was asked for, which is the only time the answer means
            // anything. See Watch.
            if (_watchAt != 0 && now >= _watchAt && _watching != null) Watch(_watching, me);

            if (_checkAt != 0 && now >= _checkAt)
            {
                _checkAt = 0;

                try
                {
                    if (!Function.Call<bool>(Hash.IS_PED_USING_SCENARIO, me.Handle, _checking))
                    {
                        Log.Warn("The scenario " + _checking + " was accepted and is not " +
                                 "running. Either the game has no such scenario or something " +
                                 "took the task straight back off him.");
                    }
                }
                catch
                {
                    // The check is a diagnostic. It never breaks the ritual.
                }

                _checking = "";
            }

            if (!Landed)
            {
                if (now < _until) return false;

                Landed = true;

                var slump = _slump;

                if (_linger <= 0)
                {
                    Stop();

                    if (slump) Nod(me);
                    return true;
                }

                // He carries on. The effect is landing THIS tick either way -- the caller is
                // told once and the rest of it is scenery.
                _lingerUntil = now + _linger;

                if (slump) Nod(me);
                return true;
            }

            // ---- still at it ----
            //
            // ENDED BY WALKING AWAY, which is the natural way to stop smoking and needs no
            // key of its own. The scenario holds him still, so any speed at all is him having
            // pushed the stick -- the game breaks its own task the moment he does, and this
            // only has to notice and tidy up behind it.
            //
            // A moment's grace first, because the ragdoll from a needle is movement too.
            if (now - _until > MovedAfterMs)
            {
                try
                {
                    if (me.Speed > MovedAt) { Stop(); return false; }
                }
                catch
                {
                    // Then it ends on the clock like everything else.
                }
            }

            if (now >= _lingerUntil) Stop();

            return false;
        }

        private bool _slump;

        /// <summary>The prop goes in his hand before anything is played, so it is there for frame one.</summary>
        private void Hold(Recipe recipe, Ped me)
        {
            _slump = recipe.Slump;

            _held = InHand(me, recipe.Props, recipe.Sits, recipe.Turned);
        }

        /// <summary>
        /// The first of these models the install actually has, bolted to a hand.
        ///
        /// PUBLIC AND STATIC BECAUSE THE CORNER WANTS IT TOO. Selling somebody a bag is the
        /// same problem as smoking one: a ladder of names that may or may not exist, a hand
        /// bone, and a line in the log saying which rung took. Two copies of that would drift,
        /// and the one that drifted would be the one nobody was looking at.
        ///
        /// Blocking on the model, deliberately: this is one prop, once, at a moment somebody
        /// caused -- not a spawner running every frame. The non-blocking rule exists because a
        /// per-frame wait is a stutter, and this is neither per-frame nor a surprise.
        /// </summary>
        public static Prop InHand(Ped who, string[] names, Vector3 sits, Vector3 turned)
        {
            if (who == null || !who.Exists() || names == null) return null;

            foreach (var name in names)
            {
                try
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(600)) continue;

                    var prop = World.CreateProp(model, who.Position, false, false);

                    model.MarkAsNoLongerNeeded();

                    if (prop == null || !prop.Exists()) continue;

                    Give(prop, who, sits, turned);

                    Log.Info("Prop in hand: " + name + ".");
                    return prop;
                }
                catch
                {
                    // Next one.
                }
            }

            if (names.Length > 0)
            {
                Log.Info("None of these props exist on this install: " + string.Join(", ", names));
            }

            return null;
        }

        /// <summary>
        /// Move a prop into somebody's hand. Re-attaching is how it changes hands.
        ///
        /// ATTACH_ENTITY_TO_ENTITY on something already attached moves it rather than refusing,
        /// so a bag passing from one man to another is one call and no detach in between --
        /// which matters, because a frame with it attached to nobody is a frame with it falling.
        /// </summary>
        public static void Give(Prop what, Ped who, Vector3 sits, Vector3 turned)
        {
            if (what == null || !what.Exists() || who == null || !who.Exists()) return;

            try
            {
                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, who.Handle, RightHand);

                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, what.Handle, who.Handle, bone,
                              sits.X, sits.Y, sits.Z, turned.X, turned.Y, turned.Z,
                              false, false, false, false, 2, true);
            }
            catch
            {
                // It stays where it was, which is somebody's hand either way.
            }
        }

        /// <summary>
        /// Try each dictionary. True once one is playing.
        ///
        /// THE MISSES ARE WORTH A LINE TOO, and finding that out cost a playthrough. Three
        /// pipe props were named, all three were guesses, all three were wrong -- and the only
        /// thing the log said was that none of them existed, at INFO, while every SUCCESS was
        /// at DEBUG and therefore invisible at the level anybody actually runs. So a working
        /// ritual and a broken one produced the same silence.
        /// </summary>
        private bool Play(Recipe recipe, Ped me)
        {
            var pairs = Rungs(recipe);

            for (var i = _rung; i + 1 < pairs.Length; i += 2)
            {
                var dict = pairs[i];
                var clip = pairs[i + 1];

                try
                {
                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) continue;

                    // 49 is upper body, looping, and lets the rest of him keep his footing --
                    // a full-body lock on a man stood on a kerb is a man who snaps to attention
                    // and then teleports his feet back when it ends.
                    Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, dict, clip,
                                  4f, -2f, recipe.Ms, 49, 0f, false, false, false);

                    _dict = dict;
                    _clip = clip;

                    // NOT LOGGED AS A SUCCESS YET. See Watch: the call being accepted says
                    // nothing at all about whether the clip exists.
                    _rung = i;
                    _watchAt = Game.GameTime + WatchMs;

                    return true;
                }
                catch
                {
                    // Next one.
                }
            }

            return false;
        }

        /// <summary>
        /// The recipe's ladder as dict/clip pairs, whichever way it was written.
        ///
        /// Older recipes name a list of dictionaries and one clip for all of them, which works
        /// only where every dictionary in the list happens to use the same clip name. Kept
        /// working rather than rewritten, because for the amb@world_human_*@base family that
        /// really is the case and those recipes are correct as they stand.
        /// </summary>
        private static string[] Rungs(Recipe recipe)
        {
            if (recipe.Pairs.Length >= 2) return recipe.Pairs;

            var out_ = new string[recipe.Dicts.Length * 2];

            for (var i = 0; i < recipe.Dicts.Length; i++)
            {
                out_[i * 2] = recipe.Dicts[i];
                out_[i * 2 + 1] = recipe.Clip;
            }

            return out_;
        }

        /// <summary>
        /// Whether what we asked for is actually playing, asked late enough to be fair.
        ///
        /// THE WHOLE FILE'S DOC SAID IT VERIFIED AND ONLY THE SCENARIO PATH EVER DID. An anim
        /// dictionary that loads proves the dictionary exists; it proves nothing whatsoever
        /// about the clip name inside it, and TASK_PLAY_ANIM with a clip the dictionary has
        /// not got is accepted in silence and plays nothing. Three drugs were reported as
        /// having no animation while the log said, for each of them, that one had started.
        ///
        /// So it is asked -- not on the same frame, because the task is queued and the answer
        /// is always no -- and a rung that did not take steps to the next one. When the ladder
        /// runs out the scenario has it, which is where this always meant to end up.
        /// </summary>
        private void Watch(Recipe recipe, Ped me)
        {
            _watchAt = 0;

            var playing = false;

            try
            {
                playing = Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM,
                                              me.Handle, _dict, _clip, 3);
            }
            catch
            {
                // Treated as not playing, which walks the ladder rather than trusting it.
            }

            if (playing)
            {
                Log.Info("Ritual anim: " + _dict + " / " + _clip + " -- playing.");
                return;
            }

            Log.Info("Ritual anim: " + _dict + " / " + _clip + " was accepted and is not " +
                     "playing. That clip is not in that dictionary; trying the next.");

            _rung += 2;

            if (_rung + 1 < Rungs(recipe).Length)
            {
                _waiting = recipe;
                _giveUpAt = Game.GameTime + StreamMs;
                return;
            }

            Scenario(recipe, me);
        }

        /// <summary>Which rung of the ladder is being tried, and when to check it.</summary>
        private int _rung;
        private int _watchAt;

        private const int WatchMs = 400;

        /// <summary>
        /// The fallback, which brings its own prop and therefore fights ours.
        ///
        /// Whatever we attached comes off first. A scenario that hands him a lit joint while a
        /// syringe is still bolted to the same bone is two things in one fist.
        /// </summary>
        private void Scenario(Recipe recipe, Ped me)
        {
            Drop();

            if (string.IsNullOrEmpty(recipe.Scenario))
            {
                Log.Info("No animation for this one loaded and it has no scenario to fall back on.");
                return;
            }

            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, me.Handle, recipe.Scenario, 0, true);

                _scenario = true;

                // AND ASKED WHETHER IT TOOK, which the comment at the top of this file has
                // claimed all along and the code was not doing. A scenario name the game does
                // not know is accepted in silence, exactly like a timecycle -- so the one
                // place that could tell us was writing "Ritual scenario: X" whether or not
                // anything was happening.
                //
                // Not on the same frame. The task is queued and the ped is not in it yet, so
                // asking now always answers no; Update asks a moment later instead.
                _checkAt = Game.GameTime + CheckMs;
                _checking = recipe.Scenario;

                Log.Info("Ritual scenario: " + recipe.Scenario + ".");
            }
            catch
            {
                // He takes it standing still, which is not the worst thing in here.
            }
        }

        /// <summary>The heroin ending. He sits down where he is.</summary>
        private static void Nod(Ped me)
        {
            try
            {
                // Type 0, and a short one. This is a man going over, not a man being shot --
                // it wants to read as him losing the argument with his own legs.
                Function.Call(Hash.SET_PED_TO_RAGDOLL, me.Handle, 2500, 3000, 0, true, true, false);
            }
            catch
            {
                // He stays up. It still worked.
            }
        }

        /// <summary>Everything put away. Safe twice.</summary>
        public void Stop()
        {
            Busy = false;
            Landed = false;

            _waiting = null;
            _until = 0;
            _lingerUntil = 0;
            _linger = 0;

            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    if (!string.IsNullOrEmpty(_dict))
                    {
                        Function.Call(Hash.STOP_ANIM_TASK, me.Handle, _dict, _clip, 3f);
                    }

                    // ONLY OURS. ClearPedTasks on a man who has walked away and started doing
                    // something else takes THAT off him too, and from the pavement a mod that
                    // interrupts you four seconds after you did something is a bug.
                    if (_scenario) Function.Call(Hash.CLEAR_PED_TASKS, me.Handle);
                }
            }
            catch
            {
                // Teardown.
            }

            _dict = "";
            _clip = "";
            _scenario = false;
            _checkAt = 0;
            _checking = "";

            Drop();
        }

        private void Drop()
        {
            if (_held == null) return;

            try
            {
                if (_held.Exists()) _held.Delete();
            }
            catch
            {
                // The streamer gets it.
            }

            _held = null;
        }
    }
}
