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

            /// <summary>
            /// Which hand this recipe's animation actually uses.
            ///
            /// A property of the CLIP, not of the drug -- so it lives beside the pairs it
            /// belongs to. Right unless it says otherwise, because most of them are.
            /// </summary>
            public bool Lefty;

            /// <summary>And how it is turned, in degrees.</summary>
            public Vector3 Turned = new Vector3(0f, 0f, 0f);

            /// <summary>What to fall back on when no dictionary loads. Brings its own prop.</summary>
            public string Scenario = "";

            /// <summary>
            /// A SYNCHRONISED SCENE, for the things that are not held in a hand.
            ///
            /// THIS IS WHY THE BONG WAS GIVEN UP ON, AND IT WAS THE RIGHT CALL AT THE TIME.
            /// Every other recipe in this file is one animation with a prop bolted to a fist,
            /// and a bong is not a thing you hold -- it stands on a table and you lean over
            /// it. A prop welded to a hand with no motion behind it points nowhere, which is
            /// exactly what was tried and exactly what it looked like.
            ///
            /// The game does it another way, and the animation was there all along:
            /// safe@franklin@ig_10 carries THREE tracks cut from one take -- bong_fra is the
            /// man, bong_bong is the bong, bong_lighter is the lighter -- and a synchronised
            /// scene plays them from one origin so they stay in register with each other.
            /// Nothing is attached to anything. He reaches for a bong that is already where
            /// his hand is going.
            ///
            /// Checked against menyooStuff/PedAnimList.txt, which is every dictionary and clip
            /// this install has: safe@franklin@ig_10 and anim@safehouse@bong are both in it,
            /// with their prop tracks. The earlier attempt called the name a guess. It is not.
            /// </summary>
            public string SceneDict = "";

            /// <summary>His track in the scene.</summary>
            public string SceneHim = "";

            /// <summary>The prop's track in the same scene, played on SceneProps.</summary>
            public string ScenePropClip = "";

            /// <summary>The prop that track drives. The first one this install has.</summary>
            public string[] SceneProps = new string[0];

            /// <summary>
            /// Where the scene's origin sits relative to him: right, forward, up, in metres.
            ///
            /// THE ANIMATION WAS CUT IN A ROOM. Its origin is where the furniture was and
            /// everything in the take is placed against it -- so the origin is the one thing
            /// there is to aim, and aiming it is how a scene made for a sofa is made to work
            /// on a pavement. Zero puts it under his feet, facing where he faces.
            /// </summary>
            public Vector3 SceneAt = new Vector3(0f, 0f, 0f);

            /// <summary>
            /// WHETHER HE BREATHES IT OUT. A plume of smoke out of his face every couple of
            /// seconds for as long as this runs, the linger included.
            ///
            /// Only for the things that are actually smoked. A needle does not exhale and a
            /// bump does not either, and a mod that puffs smoke out of a man snorting a line
            /// is a mod that stopped paying attention. See Exhale.
            /// </summary>
            public bool Puffs;

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

            /// <summary>
            /// He goes down at the end of it, as a ragdoll. Nothing sets it now -- the needle
            /// ends on the ground as a second act instead, see Highs.Nodding -- but a recipe
            /// that wants the old fall still can.
            /// </summary>
            public bool Slump = false;

            /// <summary>
            /// A second act that follows when the first lands: the sip after the pill, the
            /// slump after the needle. Its own clip, prop, length and linger, on the same
            /// camera. The effect has landed by the time it starts, so the second act is all
            /// look. Null means one act, which is most of them.
            /// </summary>
            public Recipe Then;

            /// <summary>
            /// The clip on the whole body rather than the upper half. Every other recipe
            /// keeps his legs his own so he can stand on a kerb; a man lying on the ground
            /// needs the legs too.
            /// </summary>
            public bool FullBody;
        }

        private Prop _held;
        private string _dict = "";
        private string _clip = "";
        private bool _scenario;

        /// <summary>Whether what is playing is a synchronised scene. See Scene.</summary>
        private bool _scene;

        /// <summary>When the next lungful is due, and how many have gone. See Recipe.Puffs.</summary>
        private int _puffAt;
        private int _puffs;

        private const int PuffEveryMs = 2200;

        /// <summary>
        /// Whether he has just drawn on somebody, in which case this is over.
        ///
        /// NOT SIMPLY "IS HE ARMED". He can be carrying half an armoury and still want a
        /// smoke; what ends it is a CHANGE of weapon from the one he started with, or an act
        /// -- a shot, a swing, or sights going up. A bare-handed punch is not melee combat
        /// until it lands, so the button is read as well, and only while he is unarmed:
        /// with a gun in his hand that button is the trigger and the shot test has it.
        /// </summary>
        private bool Drawn(Ped me)
        {
            try
            {
                var now = me.Weapons != null && me.Weapons.Current != null
                        ? me.Weapons.Current.Hash : WeaponHash.Unarmed;

                // A CHANGE TO SOMETHING, NOT A CHANGE TO NOTHING. The game takes a weapon
                // off him itself for some of these animations -- a synchronised scene empties
                // his hands -- and reading that as "he drew" would cancel the very clip that
                // caused it on its second frame. Putting one away is not drawing one.
                if (now != _armed && now != WeaponHash.Unarmed) return true;

                if (Function.Call<bool>(Hash.IS_PED_SHOOTING, me.Handle)) return true;
                if (Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, me.Handle)) return true;
                if (Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle)) return true;

                // AND NOT AT THE WHEEL. Attack is a driving control as well, and a meal
                // eaten through a drive-through is eaten sitting on it.
                if (now == WeaponHash.Unarmed && !me.IsInVehicle() &&
                    Game.IsControlJustPressed(Control.Attack)) return true;

                return false;
            }
            catch
            {
                // A test that cannot be made is not an interruption.
                return false;
            }
        }

        /// <summary>What he was holding when this started. See Drawn.</summary>
        private WeaponHash _armed = WeaponHash.Unarmed;

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

        /// <summary>The recipe running now, and whether it is the second act. See Recipe.Then.</summary>
        private Recipe _recipe;
        private Recipe _after;

        /// <summary>Set while a dictionary is still streaming, so it can be asked for again.</summary>
        private Recipe _waiting;
        private Recipe _watching;
        private int _giveUpAt;

        /// <summary>How long a dictionary gets to arrive before the scenario takes over.</summary>
        private const int StreamMs = 1200;

        /// <summary>
        /// Whether the camera goes round the front. Set from the ini by Main.
        ///
        /// A static because a Ritual is created in more than one place and this is one answer
        /// for the whole mod rather than a property of any single one of them.
        /// </summary>
        public static bool Cinematic = true;

        /// <summary>
        /// A multiplier on every ritual's length, from the ini.
        ///
        /// The numbers in Highs are what each act ought to take -- a bump is short and a joint
        /// is not -- and this scales all of them together without flattening that out. Somebody
        /// who wants to watch it gets more of everything; somebody taking one on the way
        /// somewhere can have it out of the way.
        /// </summary>
        public static float Length = 1f;

        /// <summary>How fast counts as walking off, and how long he gets before it is asked.</summary>
        private const float MovedAt = 0.35f;
        private const int MovedAfterMs = 600;

        /// <summary>When to ask whether the scenario actually started, and what to ask about.</summary>
        private int _checkAt;
        private string _checking = "";

        private const int CheckMs = 400;

        /// <summary>
        /// PH_R_Hand and PH_L_Hand -- the non-deforming helpers the animators hang props on.
        ///
        /// WHICH ONE MATTERS AND IT IS NOT A DETAIL. Everything was bolted to the right hand,
        /// and amb@world_human_smoking_pot smokes LEFT-handed -- so the log said the prop was
        /// in his hand and the animation was playing, both true, and the joint hung by his
        /// right thigh while his empty left hand went to his mouth. Reported as smoking
        /// without a joint, which is exactly what it was.
        /// </summary>
        private const int RightHand = 28422;
        private const int LeftHand = 60309;

        /// <summary>Start it. Returns false only if there is nobody to do it.</summary>
        public bool Start(Recipe recipe)
        {
            if (recipe == null) return false;

            var me = Game.Player.Character;
            if (me == null || !me.Exists() || !me.IsAlive) return false;

            Stop();

            Busy = true;
            Landed = false;

            // SCALED ONCE, HERE, AND USED EVERYWHERE BELOW. The effect landing, the
            // animation's own duration and the camera all have to agree about how long this
            // takes, or the man stops moving while the shot is still running.
            _ms = (int)(recipe.Ms * Length);

            if (_ms < 600) _ms = 600;

            _until = Game.GameTime + _ms;
            _linger = recipe.Linger;
            _lingerUntil = 0;

            _rung = 0;
            _tries = 0;
            _watchAt = 0;
            _watching = null;

            _recipe = recipe;
            _after = null;

            // The first lungful is not on the first frame -- he has not had it to his mouth
            // yet. And what he is holding now is what a change is measured against. See Drawn.
            _puffs = 0;
            _puffAt = Game.GameTime + PuffEveryMs;

            try
            {
                _armed = me.Weapons != null && me.Weapons.Current != null
                       ? me.Weapons.Current.Hash : WeaponHash.Unarmed;
            }
            catch
            {
                _armed = WeaponHash.Unarmed;
            }

            Hold(recipe, me);

            // Round the front, for as long as this takes. See RitualCam -- the whole point of
            // choosing the right clip is that somebody can see it, and from behind his own
            // shoulder none of this is visible at all.
            RitualCam.Start(me, _ms + Math.Min(recipe.Linger, 2500), Cinematic);

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

            // A SCENE'S DICTIONARY IS STREAMED LIKE ANY OTHER, and a scene recipe has no
            // ladder to ask on its behalf -- so it is asked for here, beside them. Without
            // this the bong waited for a dictionary nobody had requested, gave up, and went
            // to the scenario with the rest of the ritual's time already spent.
            if (!string.IsNullOrEmpty(recipe.SceneDict))
            {
                try { Function.Call(Hash.REQUEST_ANIM_DICT, recipe.SceneDict); }
                catch { }

                _waiting = recipe;
                _giveUpAt = Game.GameTime + StreamMs;
            }

            if (rungs.Length < 2 && string.IsNullOrEmpty(recipe.SceneDict))
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

            // HANDS ARE FOR ONE THING AT A TIME. Drawing on somebody ends it: the man stops
            // smoking, the prop goes, and the effect lands anyway because he already paid for
            // it and the gram is already gone. Losing a bag to an accidental trigger pull
            // would be a worse bug than the one this fixes. See Drawn.
            if (Drawn(me))
            {
                Log.Info("Ritual cut short: he reached for a weapon.");

                var land = !Landed;
                Landed = true;

                Stop();

                // True is "the effect lands this tick" to whoever is driving this. See
                // Highs.Update, which is the only caller.
                return land;
            }

            // Every frame this runs, which is what a camera move needs. It gives itself back
            // when its own clock runs out, so a ritual that lingers for a minute does not mean
            // a minute of not being able to look where you like.
            RitualCam.Update(me);

            // AND HE BREATHES OUT WHILE HE DOES IT. On its own clock rather than off the
            // animation, because these clips carry no signal for where the drag ends and the
            // whole point is that something comes out of him. See Recipe.Puffs.
            if (_recipe != null && _recipe.Puffs && now >= _puffAt)
            {
                _puffAt = now + PuffEveryMs;

                if (_puffs > 0) Exhale.Now(me, 0.18f);

                _puffs++;
            }

            if (_waiting != null)
            {
                if (Play(_waiting, me)) { _watching = _waiting; _waiting = null; }
                else if (now >= _giveUpAt)
                {
                    // THIS RUNG'S DICTIONARY NEVER CAME. Down one, with a fresh stream clock,
                    // rather than straight to the scenario -- see Play.
                    _rung += 2;
                    _tries = 0;

                    if (_rung + 1 < Rungs(_waiting).Length)
                    {
                        _giveUpAt = now + StreamMs;
                    }
                    else
                    {
                        Scenario(_waiting, me);
                        _waiting = null;
                    }
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

                // THE SECOND ACT, where there is one. The effect is landing this tick and the
                // caller is told so; what follows -- the sip, the slump -- is on the same
                // camera with its own clip and prop, and ends the way a first act would.
                if (_recipe != null && _recipe.Then != null)
                {
                    Second(_recipe.Then, me);
                    return true;
                }

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

            if (_after != null)
            {
                if (now < _until) return false;

                var slump = _slump;
                _after = null;

                if (_linger <= 0)
                {
                    Stop();

                    if (slump) Nod(me);
                    return false;
                }

                _lingerUntil = now + _linger;

                if (slump) Nod(me);
                return false;
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

        /// <summary>
        /// The first act's clip and prop go, the second's come in, on the same clock and the
        /// same camera. See Recipe.Then.
        /// </summary>
        private void Second(Recipe then, Ped me)
        {
            try
            {
                if (!string.IsNullOrEmpty(_dict)) Function.Call(Hash.STOP_ANIM_TASK, me.Handle, _dict, _clip, 3f);
            }
            catch
            {
                // The next clip takes over either way.
            }

            _dict = "";
            _clip = "";
            Drop();

            _after = then;
            _recipe = then;
            _slump = then.Slump;
            _linger = then.Linger;
            _lingerUntil = 0;

            _ms = (int)(then.Ms * Length);
            if (_ms < 600) _ms = 600;
            _until = Game.GameTime + _ms;

            _rung = 0;
            _tries = 0;
            _watchAt = 0;
            _watching = null;

            _held = InHand(me, then.Props, then.Sits, then.Turned, then.Lefty);

            RitualCam.Start(me, _ms + Math.Min(then.Linger, 2500), Cinematic);

            var rungs = Rungs(then);

            if (rungs.Length < 2)
            {
                Scenario(then, me);
                return;
            }

            _waiting = then;
            _giveUpAt = Game.GameTime + StreamMs;

            for (var i = 0; i + 1 < rungs.Length; i += 2)
            {
                try { Function.Call(Hash.REQUEST_ANIM_DICT, rungs[i]); }
                catch { }
            }
        }

        /// <summary>The prop goes in his hand before anything is played, so it is there for frame one.</summary>
        private void Hold(Recipe recipe, Ped me)
        {
            _slump = recipe.Slump;

            _held = InHand(me, recipe.Props, recipe.Sits, recipe.Turned, recipe.Lefty);
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
        public static Prop InHand(Ped who, string[] names, Vector3 sits, Vector3 turned,
                                  bool lefty = false)
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

                    // Plus whatever was settled on for this prop on the settings screen.
                    Give(prop, who, sits + Fit.Offset(name), turned + Fit.Turn(name), lefty);

                    Log.Info("Prop in hand: " + name + ", " +
                             (lefty ? "left" : "right") + ".");
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
        public static void Give(Prop what, Ped who, Vector3 sits, Vector3 turned,
                                bool lefty = false)
        {
            if (what == null || !what.Exists() || who == null || !who.Exists()) return;

            try
            {
                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, who.Handle,
                                              lefty ? LeftHand : RightHand);

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
            // A SCENE IS NOT A RUNG ON THE LADDER. It is one take with the props in it, and
            // either it plays or this was the wrong recipe -- there is no second-choice bong.
            // False walks on to the ladder below, which is how a man with a bong on an install
            // missing the dictionary still gets his joint.
            if (!string.IsNullOrEmpty(recipe.SceneDict) && Scene(recipe, me)) return true;

            var pairs = Rungs(recipe);
            if (_rung + 1 >= pairs.Length) return false;

            var dict = pairs[_rung];
            var clip = pairs[_rung + 1];

            try
            {
                // THIS RUNG OR NOTHING. This used to walk on down the ladder to the first
                // dictionary that happened to be resident, which on a fresh session was never
                // the one at the top: the Trevor switch dictionary streams in over a second
                // while the ambient smoking one is always loaded, so meth played as a
                // cigarette every first time and as Trevor every time after. A rung waits on
                // its own dictionary and is given up only when the stream clock runs out --
                // see Update -- so the ladder is an order of preference, not a race.
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) return false;

                // 49 is upper body, looping, and lets the rest of him keep his footing --
                // a full-body lock on a man stood on a kerb is a man who snaps to attention
                // and then teleports his feet back when it ends.
                // Or the whole of him, for a clip that puts him on the ground. See FullBody.
                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, dict, clip,
                              4f, -2f, _ms, recipe.FullBody ? 1 : 49, 0f, false, false, false);

                _dict = dict;
                _clip = clip;

                // NOT LOGGED AS A SUCCESS YET. See Watch: the call being accepted says
                // nothing at all about whether the clip exists.
                _watchAt = Game.GameTime + WatchMs;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// One take, several tracks, one origin -- see Recipe.SceneDict.
        ///
        /// The scene is built where he stands, facing where he faces, and the prop is made
        /// unattached and handed to the same scene. It is kept in _held, so Stop takes it
        /// away with everything else.
        /// </summary>
        private bool Scene(Recipe recipe, Ped me)
        {
            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, recipe.SceneDict)) return false;

                var at = me.Position
                       + me.RightVector * recipe.SceneAt.X
                       + me.ForwardVector * recipe.SceneAt.Y;

                at.Z += recipe.SceneAt.Z;

                var scene = Function.Call<int>(Hash.CREATE_SYNCHRONIZED_SCENE,
                                               at.X, at.Y, at.Z, 0f, 0f, me.Heading, 2);

                Function.Call(Hash.SET_SYNCHRONIZED_SCENE_LOOPED, scene, false);

                Function.Call(Hash.TASK_SYNCHRONIZED_SCENE, me.Handle, scene,
                              recipe.SceneDict, recipe.SceneHim, 4f, -4f, 0, 0, 1000f, 0);

                _held = Standing(recipe.SceneProps, at);

                if (_held != null && !string.IsNullOrEmpty(recipe.ScenePropClip))
                {
                    Function.Call(Hash.PLAY_SYNCHRONIZED_ENTITY_ANIM, _held.Handle, scene,
                                  recipe.ScenePropClip, recipe.SceneDict, 4f, -4f, 0, 1000f);
                }

                _dict = recipe.SceneDict;
                _clip = recipe.SceneHim;
                _scene = true;

                // Accepted still says nothing about playing, the same as any other clip. The
                // ladder is not walked on a scene, but the log is worth having. See Watch.
                _watchAt = Game.GameTime + WatchMs;

                Log.Info("Ritual scene: " + recipe.SceneDict + " / " + recipe.SceneHim +
                         (_held != null ? ", with the prop" : ", with no prop") + ".");

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The first of these props this install has, standing where it is put rather than
        /// held in a hand. For a scene, which drives it itself -- see Scene.
        /// </summary>
        private static Prop Standing(string[] names, Vector3 at)
        {
            if (names == null) return null;

            foreach (var name in names)
            {
                try
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(600)) continue;

                    var prop = World.CreateProp(model, at, false, false);

                    model.MarkAsNoLongerNeeded();

                    if (prop == null || !prop.Exists()) continue;

                    Log.Info("Scene prop: " + name + ".");
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
                _tries = 0;

                Log.Info("Ritual anim: " + _dict + " / " + _clip + " -- playing.");
                return;
            }

            // ---- A SECOND GO ON THE SAME RUNG BEFORE IT IS WRITTEN OFF ----
            //
            // THE FIRST ATTEMPT IS LOST EVERY TIME AND IT IS NOT THE NAME'S FAULT. The log
            // has four different pairs reported as "accepted and not playing" which are all
            // present in menyooStuff/PedAnimList.txt -- verified, in this install, by name and
            // clip. In every one of those cases the NEXT rung played immediately.
            //
            // That is not a bad name, it is a cancelled task. The drug is taken from the phone,
            // the phone closes, and the game plays its own put-it-away animation over the top
            // of ours a moment later. Ours is issued first and killed; the retry lands after
            // the handset is down and survives.
            //
            // So a rung is only wrong if it fails TWICE. The cost when a name really is absent
            // is one extra check -- four hundred milliseconds -- and the cost of not doing it
            // was every recipe silently running on its second-choice animation. Which is
            // exactly what "he sniffs it instead of smoking it" was.
            if (_tries == 0)
            {
                _tries = 1;

                Log.Debug("Ritual anim: " + _dict + " / " + _clip + " did not take; " +
                          "trying the same one again.");

                _waiting = recipe;
                _giveUpAt = Game.GameTime + StreamMs;
                return;
            }

            Log.Info("Ritual anim: " + _dict + " / " + _clip + " was accepted and is not " +
                     "playing twice over. That clip is not in that dictionary; trying the next.");

            _tries = 0;
            _rung += 2;

            if (_rung + 1 < Rungs(recipe).Length)
            {
                _waiting = recipe;
                _giveUpAt = Game.GameTime + StreamMs;
                return;
            }

            Scenario(recipe, me);
        }

        /// <summary>This ritual's length once Length has been applied.</summary>
        private int _ms;

        /// <summary>How many goes the current rung has had. See Watch.</summary>
        private int _tries;

        /// <summary>Which rung of the ladder is being tried, and when to check it.</summary>
        private int _rung;
        private int _watchAt;

        /// <summary>
        /// How long after issuing an animation before it is fair to ask whether it is playing.
        ///
        /// Six hundred rather than four. The thing most likely to have cancelled it is the
        /// phone being put away, which takes about half a second from the moment the screen
        /// closes -- so asking at four hundred was asking during the one window where the
        /// answer is always no.
        /// </summary>
        private const int WatchMs = 600;

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
            _watching = null;
            _watchAt = 0;
            _recipe = null;
            _after = null;

            // WHATEVER ELSE THIS IS DOING, THE CAMERA COMES BACK. Stop runs on the ritual
            // finishing, on it being cut short, on the player dying and on the mod being
            // switched off, and a script camera left running is a player who cannot see and
            // cannot get it back without reloading.
            RitualCam.Stop();
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
                    // A SCENE GOES THE WAY A SCENARIO DOES. Stopping an anim task by name
                    // does not end a synchronised scene: he stays in it, holding the last
                    // frame, until something takes it off him.
                    if (_scenario || _scene) Function.Call(Hash.CLEAR_PED_TASKS, me.Handle);
                }
            }
            catch
            {
                // Teardown.
            }

            _dict = "";
            _clip = "";
            _scenario = false;
            _scene = false;
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
