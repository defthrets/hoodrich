using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Economy
{
    /// <summary>
    /// Taking your own product, and what it does to you.
    ///
    /// THE ONE THING YOU COULD NEVER DO WITH IT WAS USE IT. Every gram in this mod exists to be
    /// bought, cut, bagged, carried and sold -- a supply chain with a player standing outside
    /// it holding a bag he is not allowed to open. Seven products with seven prices and seven
    /// customers, and no answer at all to "what is this stuff actually like".
    ///
    /// SEVEN DIFFERENT ANSWERS, NOT SEVEN DURATIONS OF ONE. A high that is the same wobble for
    /// a different number of seconds is a stat, and nobody would ever choose one over another.
    /// Each of these has ONE thing it does that none of the others do, and the numbers around
    /// it are there to support that one thing:
    ///
    ///   weed      time slows and the world drifts. Nothing is urgent.
    ///   oxy       everything goes soft. You stop taking damage properly and heal as you walk.
    ///   bars      a bright shiny day, and you cannot walk in a straight line.
    ///   crack     rage. Time drags, you hit like a truck, and it is over in forty seconds.
    ///   coke      speed. You run faster than the game normally lets you, and you cannot stand still.
    ///   meth      the long one. Wired for five minutes and wrecked for one afterwards.
    ///   heroin    the trip. Half speed, trails, and you are not going anywhere.
    ///
    /// AND THE HARD ONES COST YOU AFTERWARDS. A comedown is the only thing that makes taking
    /// your own supply a decision rather than a free buff -- and it is also the honest version.
    ///
    /// EVERY EFFECT NAME IN HERE IS A GUESS UNTIL THE GAME ACCEPTS IT. A timecycle that does
    /// not exist is not an error: SET_TIMECYCLE_MODIFIER takes the string, does nothing, and
    /// says nothing. So they are LADDERS -- a list per drug, tried in order, and the first one
    /// the game admits to is kept and written to the log. That is the same arrangement the
    /// paint decals use and for exactly the same reason.
    /// </summary>
    internal sealed class Highs
    {
        /// <summary>What one product does, and for how long.</summary>
        private sealed class Recipe
        {
            public string Drug = "";

            /// <summary>Timecycle names to try, in order. The first that takes is used.</summary>
            public string[] Cycles = new string[0];

            /// <summary>How hard the timecycle is laid on, nought to one.</summary>
            public float Strength = 1f;

            /// <summary>A screen effect to run underneath it, or "".</summary>
            public string Fx = "";

            /// <summary>How he walks. "" leaves his own gait alone.</summary>
            public string Clipset = "";

            /// <summary>Camera sway, nought for a steady one.</summary>
            public float Shake;

            /// <summary>Where time runs. One is normal; nothing above one is possible.</summary>
            public float Time = 1f;

            /// <summary>Sprint multiplier, one to one and a half. The game refuses more.</summary>
            public float Run = 1f;

            /// <summary>What he dishes out and what he soaks up. One is normal.</summary>
            public float Hits = 1f;
            public float Takes = 1f;

            /// <summary>How fast he heals while it lasts.</summary>
            public float Heals = 1f;

            /// <summary>A bright day, held for as long as it lasts.</summary>
            public bool Sunny;

            /// <summary>The special meter, filled the moment it lands.</summary>
            public bool Rage;

            public int Ms = 60000;

            /// <summary>How long the wreckage afterwards lasts. Nought for none.</summary>
            public int DownMs;

            public string Line = "";

            /// <summary>How he takes it, and what is in his hand while he does.</summary>
            public Ritual.Recipe Doing;
        }

        // ---- how each one is taken ----------------------------------------------
        //
        // ROLLED RATHER THAN CHOSEN, where there is more than one way to do it. Weed is a
        // joint and weed is a bong, and a man who smokes the identical joint every single time
        // is a man performing a routine rather than getting high.

        /// <summary>A joint, with the game's own lit joint in his hand. See Ritual.</summary>
        private static readonly Ritual.Recipe Joint = new Ritual.Recipe
        {
            // THE SCENARIO WAS THE PROBLEM AND IT LOOKED LIKE THE ANSWER.
            //
            // "No dictionary at all, straight to WORLD_HUMAN_SMOKING_POT" is what this said
            // for weeks, on the reasoning that the game's own scenario hands a ped a lit joint
            // and does it better than anything we could assemble. That is true of a PED. The
            // player is not one: IS_PED_USING_SCENARIO answers yes -- the check in Rituals has
            // never once complained about this one -- and nothing appears in his hand and
            // nothing plays. He is in a scenario that is not being performed.
            //
            // Which is why the diagnostic said the opposite of what the eye said, twice.
            //
            // So weed does what everything else in this file already does, and what the log
            // shows working: a real dictionary, played directly, with a real prop bolted to
            // his hand. amb@world_human_aa_smoke is the roll-up idle; the plain smoking base
            // underneath it is proven -- it is the one the coke and meth rituals land on.
            // NAMED IN PAIRS, because the clip is not called the same thing in all three.
            // aa_smoke's clips are named after the idle, not "base" -- which is why the log
            // said this was playing and Franklin stood there with a joint in his fist.
            Pairs = new[]
            {
                "amb@world_human_aa_smoke@male@idle_a",           "idle_a",
                "amb@world_human_smoking_pot@male@male_a@base",   "base",
                "amb@world_human_smoking@male@male_a@base",       "base",
                "amb@world_human_smoking@male@male_a@idle_a",     "idle_a"
            },
            Props = new[] { "p_cs_joint_01", "prop_cs_ciggy_01", "prop_cigar_01" },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),

            // Still named, because a fallback that never runs costs nothing and an install
            // missing all three dictionaries is better off in a scenario than stood still.
            Scenario = "WORLD_HUMAN_SMOKING_POT",

            // The first drag is what does it; the rest is standing there smoking. See Linger.
            Ms = 4200,
            Linger = SmokeMs
        };

        /// <summary>
        /// A blunt. The same scenario as the joint, and that is the honest version.
        ///
        /// THE BONG IS GONE AND IT WAS NEVER GOING TO WORK. anim@safehouse@bong was a guess,
        /// and a prop bolted to a hand with no animation behind it does exactly what you would
        /// expect: it sits in his fist at whatever angle the attachment happened to give it,
        /// pointing nowhere, while he stands there. A bong is not a thing you hold, it is a
        /// thing you lean over and pull on with both hands -- there is no motion in the base
        /// game that reads as that, and half a bong is worse than no bong.
        ///
        /// So weed is a joint or a blunt, both of which ARE things you hold to your mouth, and
        /// both of which the smoking-pot scenario already does properly with its own prop.
        ///
        /// The two are the same animation and only the words differ, which is stated here
        /// rather than dressed up: rolling between them is variety in what the ticker says
        /// while you do the same thing, and that is worth exactly what it costs, which is
        /// nothing.
        /// </summary>
        private static readonly Ritual.Recipe Blunt = new Ritual.Recipe
        {
            // Same motion as the joint and a fatter thing in his hand, which is the only
            // difference between the two words that survives at arm's length. See Joint for
            // why neither of them is a scenario any more.
            Pairs = new[]
            {
                "amb@world_human_aa_smoke@male@idle_a",           "idle_a",
                "amb@world_human_smoking_pot@male@male_a@base",   "base",
                "amb@world_human_smoking@male@male_a@base",       "base",
                "amb@world_human_smoking@male@male_a@idle_a",     "idle_a"
            },
            Props = new[] { "prop_cigar_01", "prop_cigar_02", "p_cs_joint_01", "prop_cs_ciggy_01" },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),
            Scenario = "WORLD_HUMAN_SMOKING_POT",
            Ms = 4600,
            Linger = SmokeMs
        };

        /// <summary>
        /// How long he stands there smoking, if nothing interrupts him.
        ///
        /// A minute, and walking off ends it sooner -- which is how it will actually end
        /// almost every time. The number is the outside limit for somebody who genuinely
        /// stands still and finishes it.
        /// </summary>
        private const int SmokeMs = 60000;

        /// <summary>
        /// The pipe. ONE OF THEM, used by both crack and meth.
        ///
        /// They were two recipes with the same motion and a different prop name, which was a
        /// distinction on paper and nothing at all on the screen -- and the difference in prop
        /// was between two names that both turned out not to exist. Rocks in a glass pipe is
        /// rocks in a glass pipe.
        ///
        /// THE FIRST THREE NAMES ARE GONE BECAUSE THE GAME SAID SO. prop_cs_crack_pipe,
        /// p_crack_pipe_01 and prop_meth_pipe were all guesses and the log answered all three
        /// at once: none of them are in this install. There may be no crack pipe in GTA V at
        /// all, so the ladder now ends in things that certainly do exist -- a cigarette and a
        /// joint. At arm's length a small white object held to the mouth is the shape of the
        /// action, and the shape of the action is what was missing.
        /// </summary>
        /// <summary>
        /// The meth pipe. Trevor's own, which is the one animation in the game that is
        /// literally this.
        ///
        /// switch@trevor@trev_smoking_meth is a cutscene switch clip -- it is what the game
        /// plays when you drop in on Trevor mid-binge -- and it is a man holding a pipe up and
        /// drawing on it. The log has it landing, so it is first here rather than one rung
        /// down a ladder shared with crack.
        ///
        /// NO METH PIPE PROP EXISTS ON THIS INSTALL and the log said so plainly:
        /// "None of these props exist: prop_meth_pipe, prop_cs_crack_pipe, p_crack_pipe_01."
        /// Three guesses, three misses. The bottle fallback is not a pipe, but a small pale
        /// object held to the mouth under a pipe animation is the shape of the act, and the
        /// shape is what reads at arm's length.
        /// </summary>
        private static readonly Ritual.Recipe MethPipe = new Ritual.Recipe
        {
            // The switch clip is named after its own dictionary, which is the convention for
            // the whole switch@ family and is nothing like the amb@ one below it.
            // THE WHOLE SWITCH FAMILY, BECAUSE ONE REAL NAME GIVES YOU THE REST.
            //
            // trev_smoking_meth_exit_cam is a genuine clip in this dictionary -- and a _cam
            // clip is the CAMERA track that runs beside a switch, not the man. Playing it on a
            // ped moves nothing. What it proves is the naming: a switch dictionary holds
            // <name>_enter, <name>_exit, <name>_loop and a _cam beside each, so the existence
            // of the camera half is evidence for the ped half sitting next to it.
            //
            // So the ped names go first, longest-running first, and the camera one goes last
            // where it can do no harm. Guessing six names into a ladder cost nothing the
            // moment the ladder started checking whether each one actually plays.
            Pairs = new[]
            {
                "switch@trevor@trev_smoking_meth",  "trev_smoking_meth_loop",
                "switch@trevor@trev_smoking_meth",  "trev_smoking_meth_idle",
                "switch@trevor@trev_smoking_meth",  "trev_smoking_meth_exit",
                "switch@trevor@trev_smoking_meth",  "trev_smoking_meth_enter",
                "switch@trevor@trev_smoking_meth",  "trev_smoking_meth",
                "switch@trevor@trev_smoking_meth",  "trev_smoking_meth_exit_cam",

                // And the ambient smoker underneath, which is proven to exist.
                "amb@world_human_aa_smoke@male@idle_a",     "idle_a",
                "amb@world_human_smoking@male@male_a@base", "base"
            },
            Props = new[]
            {
                "prop_meth_pipe", "prop_glass_pipe", "prop_cs_pipe_01",
                "prop_test_boss_pipe", "prop_cs_ciggy_01"
            },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),
            Scenario = "WORLD_HUMAN_SMOKING",
            Ms = 4000
        };

        /// <summary>
        /// A bump off a key. Hand to the nose, twice, and a sniff.
        ///
        /// THERE IS NO SNORTING ANIMATION IN THIS GAME. Nobody in Los Santos does a line on
        /// camera outside a cutscene, and a cutscene clip is not addressable from here. What
        /// there is, is a family of hand-to-face idles, and a bump IS a hand-to-face idle with
        /// a small thing pinched in it -- which is exactly what the meth switch clip does, so
        /// it leads. The wrap in his other hand does the rest of the explaining.
        ///
        /// Short, because a bump is short. Two and a half seconds and he is upright again.
        /// </summary>
        private static readonly Ritual.Recipe Bump = new Ritual.Recipe
        {
            Pairs = new[]
            {
                "switch@trevor@trev_smoking_meth",             "trev_smoking_meth",
                "amb@world_human_drug_dealer_hard@male@base",  "base",
                "amb@world_human_smoking@male@male_a@base",    "base",
                "amb@world_human_aa_smoke@male@idle_a",        "idle_a"
            },
            Props = new[]
            {
                "prop_coke_block", "prop_meth_bag_01", "prop_cs_package_01",
                "prop_drug_package_02", "prop_drug_package"
            },
            Sits = new Vector3(0.015f, 0.005f, 0.0f),
            Scenario = "WORLD_HUMAN_DRUG_DEALER",
            Ms = 2500
        };

        private static readonly Ritual.Recipe Pipe = new Ritual.Recipe
        {
            Dicts = new[]
            {
                "switch@trevor@trev_smoking_meth",
                "amb@world_human_smoking@male@male_a@base",
                "amb@world_human_aa_smoke@male@idle_a"
            },
            Clip = "base",
            Props = new[]
            {
                // Still worth asking for, in case an install has one from somewhere.
                "prop_cs_pipe_01", "prop_glass_pipe", "prop_pipe_01",

                // And these are the fallback that will actually land.
                "prop_cs_ciggy_01", "p_cs_joint_01", "prop_cigar_01"
            },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),
            Scenario = "WORLD_HUMAN_SMOKING",
            Ms = 3400
        };

        /// <summary>The needle, and going over at the end of it.</summary>
        private static readonly Ritual.Recipe Needle = new Ritual.Recipe
        {
            Dicts = new[]
            {
                "amb@world_human_drug_dealer_hard@male@base",
                "amb@world_human_drug_dealer@male@base",
                "amb@world_human_smoking@male@male_a@base"
            },
            Clip = "base",
            Props = new[] { "prop_syringe_01", "prop_cs_syringe", "v_med_syringe" },
            Sits = new Vector3(0.02f, 0.0f, 0.0f),
            Turned = new Vector3(0f, 0f, 90f),
            Scenario = "WORLD_HUMAN_DRUG_DEALER_HARD",

            // The nod. He goes down at the end of it, which is the half of this that nobody
            // needs an animation dictionary to read.
            Slump = true,
            Ms = 3800
        };

        /// <summary>A line off the back of a hand. Nothing to hold but the wrap.</summary>
        private static readonly Ritual.Recipe Line_ = new Ritual.Recipe
        {
            Dicts = new[]
            {
                "amb@world_human_drug_dealer@male@base",
                "amb@world_human_smoking@male@male_a@base"
            },
            Clip = "base",
            Props = new[] { "prop_meth_bag_01", "prop_drug_package_02", "prop_drug_package" },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),
            Scenario = "WORLD_HUMAN_DRUG_DEALER",
            Ms = 2800
        };

        /// <summary>
        /// Pills go down with a drink, which is the one ritual the game already has perfectly.
        ///
        /// WORLD_HUMAN_DRINKING is a bottle to the mouth and a head tipped back, prop included.
        /// Nothing about it says beer rather than washing two bars down, and it is a great deal
        /// better than a man swallowing air.
        /// </summary>
        /// <summary>
        /// Bars and percs: shaken into a hand and tipped back.
        ///
        /// The bottle was doing the work before -- WORLD_HUMAN_DRINKING is a genuinely good
        /// swallow, head back and all -- but it is a MAN DRINKING A BEER, and washing two
        /// bars down is not that. The motion wanted is hand to mouth, then head back, and the
        /// smoking idles are hand-to-mouth: with a pill bottle in the hand instead of a
        /// cigarette that reads as tipping something in.
        ///
        /// Drinking stays as the fallback underneath, because it is still the better half of
        /// the act and an install missing every dictionary should get the swallow rather than
        /// a man standing still.
        /// </summary>
        private static readonly Ritual.Recipe Pop = new Ritual.Recipe
        {
            Pairs = new[]
            {
                "amb@world_human_smoking@male@male_a@base",  "base",
                "amb@world_human_drug_dealer@male@base",     "base",
                "amb@world_human_aa_smoke@male@idle_a",      "idle_a"
            },
            Props = new[]
            {
                "prop_cs_pills", "prop_pills_01", "prop_cs_script_bottle",
                "prop_pill_bottle_01", "prop_cs_bottle_01"
            },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),
            Scenario = "WORLD_HUMAN_DRINKING",
            Ms = 2600
        };

        private static readonly Ritual.Recipe Swallow = new Ritual.Recipe
        {
            Scenario = "WORLD_HUMAN_DRINKING",
            Ms = 3000
        };

        // ---- what each one is ---------------------------------------------------

        private static readonly Recipe[] Book =
        {
            // WEED. Nothing happens quickly. The drift is a slight drunk gait rather than a
            // stagger -- somebody stoned does not fall over, they arrive late.
            new Recipe
            {
                Drug = "weed",
                Cycles = new[] { "drug_wobbly", "stoned", "drug_flying_base", "spectator5" },
                Strength = 0.85f,
                Clipset = "move_m@drunk@slightlydrunk",
                Shake = 0.10f,
                Time = 0.80f,
                Ms = 100000,
                Line = "everything's fine. everything's real slow and fine"
            },


            // OXYCODONE. The painkiller one, so it is about not feeling things: half damage,
            // healing as you walk, and a soft sway. No speed, no strength, nothing sharp.
            new Recipe
            {
                Drug = "ecstasy",
                Doing = Pop,
                Cycles = new[] { "drug_flying_01", "drug_flying_base", "drug_wobbly" },
                Strength = 0.7f,
                Clipset = "move_m@drunk@moderatedrunk",
                Shake = 0.14f,
                Time = 0.9f,
                Takes = 0.45f,
                Heals = 4f,
                Ms = 120000,
                DownMs = 12000,
                Line = "cant feel a thing. not one thing"
            },

            // BARS. A BRIGHT SHINY DAY AND A DRUNK WALK, which is the whole brief.
            //
            // The weather is overridden rather than a timecycle being asked to look sunny --
            // it is the one way to be certain, it works at three in the morning in the rain,
            // and coming out of a blackout into blazing sunshine is exactly the joke.
            new Recipe
            {
                Drug = "xanax",
                Doing = Pop,
                Cycles = new[] { "drug_flying_base", "drug_wobbly" },
                Strength = 0.6f,
                Clipset = "move_m@drunk@verydrunk",
                Shake = 0.45f,
                Time = 0.92f,
                Takes = 0.7f,
                Sunny = true,
                Ms = 150000,
                DownMs = 15000,
                Line = "beautiful day. beautiful. wheres my car"
            },

            // CRACK. Rage: time drags, everything you hit goes down, and it is over almost
            // before you have used it. The comedown is as long as the high.
            new Recipe
            {
                Drug = "crack",
                Doing = Pipe,
                Cycles = new[] { "REDMIST", "drug_drivingfast", "spectator9" },
                Fx = "DrugsTrevorClownsFightIn",
                Shake = 0.35f,
                Time = 0.65f,
                Run = 1.35f,
                Hits = 2.2f,
                Takes = 0.5f,
                Rage = true,
                Ms = 40000,
                DownMs = 40000,
                Line = "MOVE"
            },

            // COKE. Not strength -- SPEED. Full sprint multiplier, a twitchy camera and a
            // sharp bright picture, and it is gone in a minute.
            new Recipe
            {
                Drug = "coke",
                Doing = Bump,

                // SPEEDY LIKE THE OTHERS AND BRIGHT LIKE THE BARS, which is a combination none
                // of the rest have and is the whole character of this one.
                //
                // It was wearing crack's clothes -- REDMIST and a hard drivingfast smear at
                // nine tenths strength -- so the fastest drug in here LOOKED like the angriest
                // one, and the two of them were telling you the same thing in the same red.
                //
                // The day goes bright the way the bars do, and the timecycle drops to under
                // half strength so the picture stays SHARP under it. That is the difference
                // between clear and merely pale: everything crisp and over-lit and slightly
                // too much, rather than a filter smeared over the top of it.
                Cycles = new[] { "drug_flying_base", "spectator10", "drug_drivingfast" },
                Strength = 0.45f,
                Sunny = true,

                Fx = "RaceTurbo",

                // A jitter rather than a sway. Anything heavier reads as drunk, which is the
                // one thing this is not.
                Shake = 0.20f,

                Run = 1.49f,
                Hits = 1.3f,
                Rage = true,
                Ms = 60000,
                DownMs = 30000,
                Line = "yeah. yeah yeah yeah. what we doing"
            },

            // METH. The long one. Five minutes wired, and then a minute of being no use to
            // anybody -- which is the point of it being the long one.
            new Recipe
            {
                Drug = "meth",
                Doing = MethPipe,
                Cycles = new[] { "spectator10", "drug_drivingfast", "drug_wobbly" },
                Strength = 0.8f,

                // NOTHING HAPPENED, AND THAT WAS FAIR. This was the only one of the seven with
                // no time change and no gait: a run multiplier you only feel while sprinting, a
                // damage number you only feel in a fight, and a camera sway. Stood still on a
                // pavement it was a slightly different colour and nothing else, for five
                // minutes.
                //
                // The wired one should read as wired. A screen effect, a proper sway, and time
                // pushed just off normal -- not crack's heavy drag, which is a different drug,
                // but far enough that the world is not quite right for as long as it lasts.
                Fx = "RaceTurbo",
                Clipset = "move_m@drunk@slightlydrunk",
                Shake = 0.42f,
                Time = 0.88f,
                Run = 1.40f,
                Hits = 1.2f,
                Takes = 0.85f,
                Ms = 300000,
                DownMs = 60000,
                Line = "not even tired. could go all night"
            },

            // HEROIN. The trip. Half speed, trails, and you are not walking anywhere at any
            // pace -- the run multiplier goes DOWN, which nothing else here does.
            new Recipe
            {
                Drug = "heroin",
                Doing = Needle,
                Cycles = new[] { "DRUG_2_TRAILS", "drug_flying_02", "drug_flying_base", "drug_wobbly" },
                Fx = "PeyoteEndOut",
                Clipset = "move_m@drunk@verydrunk",
                Shake = 0.20f,
                Time = 0.55f,
                Run = 1f,
                Takes = 0.6f,
                Ms = 180000,
                DownMs = 45000,
                Line = "..."
            }
        };

        // ---- the comedown -------------------------------------------------------

        /// <summary>
        /// What is left of you afterwards. One recipe for all of them, because being wrecked
        /// is being wrecked whatever did it -- what differs is how LONG, and that is per drug.
        /// </summary>
        private static readonly string[] DownCycles = { "drug_deadman", "CAMERA_BW", "drug_wobbly" };

        private const string DownClipset = "move_m@injured";
        private const float DownShake = 0.18f;
        private const float DownTakes = 1.6f;

        // ---- where it is up to --------------------------------------------------

        private readonly Ritual _ritual = new Ritual();
        private readonly Random _rng = new Random();

        /// <summary>Taken, being taken, and not landed yet. See Update.</summary>
        private Recipe _taking;

        /// <summary>One thing currently in him, and when it lets go.</summary>
        private sealed class Live
        {
            public Recipe What;
            public int Until;
        }

        /// <summary>
        /// EVERYTHING HE IS ON, not the one thing he is on.
        ///
        /// It was a single recipe and a single expiry, which made "one at a time" a rule the
        /// data structure enforced rather than a decision anybody made. Mixing is the whole
        /// interesting part of having seven of them -- and it is also the thing that should be
        /// able to go badly wrong, which a single slot can never express.
        ///
        /// The numbers are RECOMBINED whenever the list changes rather than applied on top of
        /// each other. Applying is not commutative: two drugs each halving the time scale is a
        /// quarter, and coming off one of them would have to know what the other had set. One
        /// pass over the list, one set of values, no history to unwind.
        /// </summary>
        private readonly List<Live> _live = new List<Live>();

        private bool _coming;
        private int _downUntil;

        private string _cycle = "";
        private string _fx = "";
        private string _clip = "";
        private bool _shaking;
        private bool _weather;

        /// <summary>Where the blackout is up to. See Overdo.</summary>
        private int _blackFrom;
        private int _wokeAt;
        private int _fadeAt;

        public bool IsHigh => _live.Count > 0;
        public bool IsRough => _coming;
        public bool IsOut => _blackFrom != 0;

        /// <summary>How many things are in him at once.</summary>
        public int Stacked { get { return _live.Count; } }

        /// <summary>What is running, for a readout. "" when nothing is.</summary>
        public string Current
        {
            get { return _live.Count == 0 ? "" : _live[_live.Count - 1].What.Drug; }
        }

        // ---- taking one ---------------------------------------------------------

        /// <summary>
        /// Why he cannot take this one, or null if he can.
        ///
        /// Asked by the pocket screen so a row that will not work is greyed with a reason on
        /// it, rather than pressable and silent.
        /// </summary>
        public string Refusal(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return "Not that";
            if (Find(drugId) == null) return "Nothing to do with this one";
            // Mid-needle is nonsense; mid-joint is Tuesday.
            if (_ritual.Busy && !_ritual.Landed) return "You're doing it";
            if (_blackFrom != 0) return "You're gone";
            if (_coming) return "Give it a minute";

            // MIXING IS ALLOWED AND DOUBLING IS NOT. Two of the same is a bigger dose of one
            // thing, which is a different feature and would just be a longer timer; two
            // different things is the point.
            foreach (var one in _live)
            {
                if (one.What.Drug == drugId) return "You're already on that one";
            }

            return null;
        }

        /// <summary>Take it. Returns why not, or null once it has landed.</summary>
        public string Take(string drugId, string what)
        {
            var no = Refusal(drugId);
            if (no != null) return no;

            var recipe = Find(drugId);

            _said = what;

            // A joint or a blunt. Same motion, different word -- see Blunt.
            var rolled = recipe.Drug == "weed" && _rng.Next(2) == 0;

            var doing = recipe.Drug != "weed" ? recipe.Doing : (rolled ? Joint : Blunt);

            // THE EFFECT DOES NOT LAND UNTIL HE HAS TAKEN IT. He stops, he does it, and THEN
            // the world changes -- which is the entire difference between a habit and a cheat
            // code. Update carries it the rest of the way.
            if (doing != null && _ritual.Start(doing))
            {
                _taking = recipe;

                Log.Info("Taking " + drugId + ".");

                return null;
            }

            Land(recipe, what);

            return null;
        }

        /// <summary>The moment it actually hits.</summary>
        private void Land(Recipe recipe, string what)
        {
            _live.Add(new Live { What = recipe, Until = Game.GameTime + recipe.Ms });

            Log.Info("High: " + recipe.Drug + " for " + (recipe.Ms / 1000) + "s. " +
                     _live.Count + " in him.");

            // THE FOURTH ONE PUTS HIM OUT. Three at once is a night; four is not a decision
            // anybody makes twice, and the game should agree with that rather than stacking
            // another timecycle on top and carrying on.
            if (_live.Count > TooMany)
            {
                Overdo();
                return;
            }

            Recombine();

            Notify.Ticker("~g~" + what + "~s~ -- " + recipe.Line);
        }

        /// <summary>How many things he can have in him before he goes over.</summary>
        private const int TooMany = 3;

        /// <summary>What the ticker says once it lands, kept from the press that started it.</summary>
        private string _said = "";

        private static Recipe Find(string drugId)
        {
            foreach (var r in Book)
            {
                if (string.Equals(r.Drug, drugId, StringComparison.OrdinalIgnoreCase)) return r;
            }

            return null;
        }

        /// <summary>
        /// One pass over everything in him, and one set of numbers out of it.
        ///
        /// HOW EACH ONE COMBINES IS A DECISION PER NUMBER, not one rule for all of them:
        ///
        ///   time      the SLOWEST wins rather than multiplying. Weed at four fifths and
        ///             heroin at a half multiply to two fifths, which is a slideshow -- and
        ///             two depressants do not make the world twice as slow, they make it as
        ///             slow as the worse of them.
        ///   run       the FASTEST wins, same reasoning the other way up.
        ///   hits      multiplied, because they genuinely do add up and being hard to hurt is
        ///             the reward for a bad idea.
        ///   takes     multiplied and floored, so a stack cannot make him untouchable.
        ///   shake     SUMMED and capped. This is the one that should get worse with every
        ///             thing you put in him -- it is the readout that says "you have had too
        ///             much" before the screen does.
        ///   look      the NEWEST wins. There is one timecycle slot, one screen effect and one
        ///             movement clipset on a ped, so these cannot combine at all -- and the
        ///             last thing you took is the one you are currently feeling arrive.
        /// </summary>
        private void Recombine()
        {
            var time = 1f;
            var run = 1f;
            var hits = 1f;
            var takes = 1f;
            var heals = 1f;
            var shake = 0f;
            var sunny = false;
            var rage = false;

            string fx = null;
            string clipset = null;

            foreach (var one in _live)
            {
                var r = one.What;

                if (r.Time < time) time = r.Time;
                if (r.Run > run) run = r.Run;

                hits *= r.Hits;
                takes *= r.Takes;
                heals *= r.Heals;

                shake += r.Shake;

                sunny |= r.Sunny;
                rage |= r.Rage;

                if (!string.IsNullOrEmpty(r.Fx)) fx = r.Fx;
                if (!string.IsNullOrEmpty(r.Clipset)) clipset = r.Clipset;
            }

            if (takes < 0.3f) takes = 0.3f;
            if (shake > 0.9f) shake = 0.9f;

            _time = time;
            _shake = shake;
            _sunny = sunny;
            _clipset = clipset ?? "";

            // The newest one that HAS a look is the look. Walked backwards so the last thing
            // taken wins without having to remember which it was on the way through.
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                if (_live[i].What.Cycles.Length == 0) continue;

                if (_cycleOwner != _live[i].What)
                {
                    _cycleOwner = _live[i].What;
                    Cycle(_live[i].What.Cycles, _live[i].What.Strength);
                }

                break;
            }

            if (!string.IsNullOrEmpty(fx) && fx != _fx) Fx(fx);

            try
            {
                var player = Game.Player;

                Function.Call(Hash.SET_TIME_SCALE, time);
                Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, player.Handle, run);
                Function.Call(Hash.SET_PLAYER_WEAPON_DAMAGE_MODIFIER, player.Handle, hits);
                Function.Call(Hash.SET_PLAYER_WEAPON_DEFENSE_MODIFIER, player.Handle, takes);
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, player.Handle, heals);

                if (rage) Function.Call(Hash.SPECIAL_ABILITY_FILL_METER, player.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not combine a high: " + ex.Message);
            }
        }

        /// <summary>What the combine last worked out, for the per-frame half to hold.</summary>
        private float _time = 1f;
        private float _shake;
        private bool _sunny;
        private string _clipset = "";

        /// <summary>Whose look is on the screen, so it is not reapplied every recombine.</summary>
        private Recipe _cycleOwner;

        // ---- keeping it up ------------------------------------------------------

        /// <summary>
        /// Called every tick. Mostly nothing, like everything else that runs per frame here.
        ///
        /// The clipset is re-attempted rather than applied once, because SET_PED_MOVEMENT_CLIPSET
        /// on a set that is not in memory does nothing at all -- no error and no log line, and
        /// he simply walks normally. It has to be streamed first, which takes a few frames.
        /// </summary>
        public void Update()
        {
            // THE RITUAL NO LONGER OWNS THE TICK. It used to return out of here for as long
            // as it ran, which was fine while every ritual was over in three seconds and is
            // not now the joint lasts a minute: a high that had already landed would have sat
            // frozen underneath a man finishing his smoke.
            //
            // Update says ONCE, on the tick the effect is due, and after that the ritual is
            // just something he happens to be doing.
            if (_ritual.Busy && _ritual.Update() && _taking != null)
            {
                var recipe = _taking;
                _taking = null;

                Land(recipe, _said);
            }

            if (_blackFrom != 0) { Blackout(); return; }

            if (_live.Count == 0 && !_coming) return;

            var now = Game.GameTime;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) { Off(); return; }

                // Dying sobers you up, which is the one mercy in here.
                if (!me.IsAlive) { Off(); return; }

                if (_live.Count > 0)
                {
                    // ONE AT A TIME, OLDEST FIRST. They came on separately and they go off
                    // separately, so a stack thins out rather than ending all at once -- and
                    // the comedown is the LAST one's, because the last one to let go is the
                    // one you are left holding.
                    var went = false;

                    for (var i = _live.Count - 1; i >= 0; i--)
                    {
                        if (now < _live[i].Until) continue;

                        var gone = _live[i].What;
                        _live.RemoveAt(i);

                        went = true;

                        Log.Info("High: " + gone.Drug + " wore off. " + _live.Count + " left.");

                        if (_live.Count == 0) { Crash(gone); return; }
                    }

                    if (went) Recombine();

                    Hold(me, _clipset, _shake, _sunny);

                    Trip(me, now);
                    return;
                }

                if (now >= _downUntil) { Off(); return; }

                Hold(me, DownClipset, DownShake, false);
            }
            catch (Exception ex)
            {
                Log.Debug("A high tripped: " + ex.Message);
                Off();
            }
        }

        /// <summary>
        /// Three things in him and running, and his legs stop agreeing with him.
        ///
        /// THE STACK NEEDS A CONSEQUENCE BETWEEN "FINE" AND "UNCONSCIOUS". Three was the limit
        /// and the fourth was the blackout, so up to three was a straight pile of buffs with a
        /// wobbly camera on top -- nothing about being on three at once actually cost you
        /// anything until you took a fourth.
        ///
        /// ONLY WHILE HE IS MOVING FAST, because that is when legs matter. Standing on a corner
        /// on three things is a man having a night; sprinting across four lanes on them is a
        /// man who is about to find out.
        ///
        /// AND BARS DOUBLE IT. Alprazolam is the one in the list whose whole character is not
        /// being able to walk in a straight line -- it already comes with the heaviest gait and
        /// the widest sway of the seven -- so a mix with bars in it goes over far more often
        /// than one without. That is the drug being itself rather than a special case.
        /// </summary>
        private void Trip(Ped me, int now)
        {
            if (_live.Count < TripsFrom) return;
            if (now < _nextTrip) return;

            _nextTrip = now + TripEveryMs;

            try
            {
                if (me.IsInVehicle()) return;
                if (me.Speed < TripAbove) return;

                var bars = false;

                foreach (var one in _live)
                {
                    if (one.What.Drug != "xanax") continue;
                    bars = true;
                    break;
                }

                if (_rng.Next(100) >= (bars ? TripWithBars : TripChance)) return;

                Function.Call(Hash.SET_PED_TO_RAGDOLL, me.Handle, TripDownMs, TripDownMs + 600,
                              0, true, true, false);

                Log.Debug("Tripped over on " + _live.Count + (bars ? " with bars in." : "."));
            }
            catch
            {
                // He stays up, which is the smaller problem.
            }
        }

        private int _nextTrip;

        /// <summary>How many in him before his legs are a problem.</summary>
        private const int TripsFrom = 3;

        /// <summary>How often it is even considered, and how fast he has to be going.</summary>
        private const int TripEveryMs = 2500;
        private const float TripAbove = 2.6f;

        /// <summary>The odds each time it is asked, out of a hundred, and with bars in him.</summary>
        private const int TripChance = 22;
        private const int TripWithBars = 45;

        /// <summary>How long he is on the floor.</summary>
        private const int TripDownMs = 1400;

        /// <summary>The per-frame half: the gait, the sway, and the sky.</summary>
        private void Hold(Ped me, string clipset, float shake, bool sunny)
        {
            if (!string.IsNullOrEmpty(clipset) && _clip != clipset && Streamed(clipset))
            {
                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, me.Handle, clipset, 0.5f);
                _clip = clipset;
            }

            if (shake > 0.001f)
            {
                // STARTED ONCE AND THEN MODULATED. Calling SHAKE_GAMEPLAY_CAM again restarts
                // the shake from the beginning of its curve, so a per-frame call is a shake
                // that never gets anywhere and reads as a judder.
                if (!_shaking)
                {
                    Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "DRUNK_SHAKE", shake);
                    _shaking = true;

                    Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, true);
                }
                else
                {
                    Function.Call(Hash.SET_GAMEPLAY_CAM_SHAKE_AMPLITUDE, shake);
                }
            }

            if (sunny && !_weather)
            {
                Function.Call(Hash.SET_OVERRIDE_WEATHER, "EXTRASUNNY");
                _weather = true;
            }
        }

        private static bool Streamed(string set)
        {
            try
            {
                if (Function.Call<bool>(Hash.HAS_CLIP_SET_LOADED, set)) return true;

                Function.Call(Hash.REQUEST_CLIP_SET, set);
                return false;
            }
            catch
            {
                return false;
            }
        }

        // ---- putting it on and taking it off -------------------------------------

        private void Apply(Recipe r)
        {
            var player = Game.Player;

            Cycle(r.Cycles, r.Strength);

            if (!string.IsNullOrEmpty(r.Fx)) Fx(r.Fx);

            try
            {
                if (r.Time < 0.999f) Function.Call(Hash.SET_TIME_SCALE, r.Time);

                Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, player.Handle, r.Run);
                Function.Call(Hash.SET_PLAYER_WEAPON_DAMAGE_MODIFIER, player.Handle, r.Hits);
                Function.Call(Hash.SET_PLAYER_WEAPON_DEFENSE_MODIFIER, player.Handle, r.Takes);
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, player.Handle, r.Heals);

                if (r.Rage) Function.Call(Hash.SPECIAL_ABILITY_FILL_METER, player.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not apply a high: " + ex.Message);
            }
        }

        /// <summary>
        /// The timecycle ladder.
        ///
        /// SET_TIMECYCLE_MODIFIER takes any string at all and says nothing about it, so the
        /// only way to know a name is real is to set it and then ASK -- the index comes back
        /// negative when the game has no such modifier. Each drug names several and the first
        /// that answers is the one it keeps, which is written to the log so a build where a
        /// name has been renamed says so rather than looking merely uneventful.
        /// </summary>
        private void Cycle(string[] names, float strength)
        {
            foreach (var name in names)
            {
                try
                {
                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER, name);

                    if (Function.Call<int>(Hash.GET_TIMECYCLE_MODIFIER_INDEX) < 0) continue;

                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, strength);

                    _cycle = name;

                    Log.Debug("High look: " + name + ".");
                    return;
                }
                catch
                {
                    // Next one.
                }
            }

            try { Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER); }
            catch { }

            Log.Info("None of the timecycles for this one exist on this install: " +
                     string.Join(", ", names));
        }

        private void Fx(string name)
        {
            try
            {
                Function.Call(Hash.ANIMPOSTFX_PLAY, name, 0, true);

                if (Function.Call<bool>(Hash.ANIMPOSTFX_IS_RUNNING, name))
                {
                    _fx = name;
                    return;
                }

                Log.Debug("Screen effect " + name + " would not run.");
            }
            catch
            {
                // The timecycle is doing most of the work anyway.
            }
        }

        /// <summary>The high ends and the wreckage starts. Or it just ends.</summary>
        private void Crash(Recipe last)
        {
            var down = last == null ? 0 : last.DownMs;
            var line = last == null ? "" : last.Drug;

            Clear();

            if (down <= 0)
            {
                Log.Info("High: " + line + " wore off.");
                return;
            }

            _coming = true;
            _downUntil = Game.GameTime + down;

            Cycle(DownCycles, 1f);

            try
            {
                var player = Game.Player;

                Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DAMAGE_MODIFIER, player.Handle, 0.8f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DEFENSE_MODIFIER, player.Handle, DownTakes);
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, player.Handle, 0f);
            }
            catch
            {
                // He is merely fine, which is a smaller problem than a crash.
            }

            Log.Info("High: " + line + " wore off. Comedown for " + (down / 1000) + "s.");

            Notify.Problem("that's it gone. you feel terrible.");
        }

        // ---- going over ---------------------------------------------------------

        /// <summary>
        /// Four things in him at once, and the lights go out.
        ///
        /// THIS IS THE STORY MODE TREVOR BLACKOUT AND IT IS DELIBERATELY THAT. You do not get
        /// a warning, you do not get a fight, and you do not get to finish what you were
        /// doing: the screen goes, and you come round somewhere else with hours missing. It is
        /// the only consequence in this whole system that costs you something you cannot get
        /// back, which is what makes three a limit rather than a suggestion.
        ///
        /// A STATE MACHINE RATHER THAN A WAIT. Everything here runs on one tick of a script
        /// that must not block -- so the fade, the move and the coming round are three moments
        /// on a clock, driven by Blackout below.
        /// </summary>
        private void Overdo()
        {
            Log.Info("Overdose: " + _live.Count + " at once. Out cold.");

            _live.Clear();

            Clear();

            _blackFrom = Game.GameTime;
            _wokeAt = 0;
            _fadeAt = _blackFrom + DropMs;

            // HE GOES DOWN FIRST AND THE SCREEN FOLLOWS HIM. The fade started on the same
            // frame as the overdose, so the one thing you never saw was the thing that
            // happened -- the picture was already going before his knees did, and what it read
            // as was the mod cutting away rather than a man hitting the pavement.
            //
            // A couple of seconds of him falling over, and THEN the lights. The ragdoll is set
            // to outlast the fade so he is still limp underneath it when it covers him.
            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    Function.Call(Hash.SET_PED_TO_RAGDOLL, me.Handle,
                                  DropMs + FadeMs, DropMs + FadeMs + 1000, 0, true, true, false);
                }
            }
            catch
            {
                // He stays on his feet and the fade still comes.
            }
        }

        /// <summary>
        /// The three moments of it: dark, moved, awake.
        ///
        /// The move happens while the screen is black and a beat is left afterwards before the
        /// fade in, because a teleport is a streaming request and arriving before the world
        /// does is how you come round inside the floor.
        /// </summary>
        private void Blackout()
        {
            var now = Game.GameTime;

            // A HARD FLOOR UNDER THE WHOLE THING, because the failure mode is the worst one
            // this mod can produce: a black screen with no way out of it, on somebody's save.
            //
            // Every step below can fail on its own -- a native refusing, a teleport into
            // ground that has not streamed, a state left half set by something else entirely.
            // None of those should ever end with the picture gone for good, so past the point
            // where the sequence could possibly still be running, the screen comes back and
            // the state is thrown away whatever it thought it was doing.
            if (now - _blackFrom > GiveUpMs)
            {
                Log.Warn("The blackout ran long. Putting the screen back.");

                _blackFrom = 0;
                _wokeAt = 0;
                _fadeAt = 0;

                try { Function.Call(Hash.DO_SCREEN_FADE_IN, 800); }
                catch { }

                return;
            }

            // The lights, once he has had time to hit the ground.
            if (_fadeAt != 0 && now >= _fadeAt)
            {
                _fadeAt = 0;

                try
                {
                    // The fuzz first and the fade over the top of it, so the picture breaks up
                    // before it goes rather than simply dimming.
                    Fx("DeathFailOut");

                    Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs);
                }
                catch
                {
                    // Then it is an abrupt cut, which still reads as passing out.
                }
            }

            if (_wokeAt == 0)
            {
                if (now - _blackFrom < DropMs + FadeMs + DarkMs) return;

                Wake();

                _wokeAt = now;
                return;
            }

            if (now - _wokeAt < SettleMs) return;

            try { Function.Call(Hash.DO_SCREEN_FADE_IN, FadeMs); }
            catch { }

            _blackFrom = 0;
            _wokeAt = 0;

            // He comes round in the state anybody comes round in.
            _coming = true;
            _downUntil = Game.GameTime + WokeRoughMs;

            Cycle(DownCycles, 1f);

            try
            {
                var player = Game.Player;

                Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DAMAGE_MODIFIER, player.Handle, 0.8f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DEFENSE_MODIFIER, player.Handle, DownTakes);
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, player.Handle, 0f);
            }
            catch
            {
            }

            Notify.Problem("you don't remember getting here.");
        }

        /// <summary>Somewhere else, some hours later, face down.</summary>
        private void Wake()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var spot = Elsewhere[_rng.Next(Elsewhere.Length)];

                // Out of any car first. Coming round in the driver's seat of something doing
                // sixty is a different mod.
                if (me.IsInVehicle())
                {
                    try { Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, me.Handle); }
                    catch { }
                }

                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, spot.X, spot.Y, spot.Z);

                me.Position = spot;
                me.Heading = (float)(_rng.NextDouble() * 360.0);

                // HOURS ARE MISSING, which is most of what makes it a blackout rather than a
                // teleport. Between four and nine, so you can go under in the afternoon and
                // come round in the dark.
                var hours = 4 + _rng.Next(6);

                // AND THEY WERE NOT RESTFUL, which has to be said before the clock moves
                // rather than after.
                //
                // Bare Minimum watches the clock and reads a jump it did not cause as somebody
                // having slept -- fair, because the things that move time in hours are beds.
                // So passing out on four drugs was handing back a full night's rest, and its
                // log said so in as many words: "Slept 6h (quality 1.00). Sleep 100%." The
                // most efficient way to sleep in this game was to overdose in the street.
                //
                // Optional on the other end: an install without the call gets the old
                // behaviour, and one without Bare Minimum at all never notices.
                Core.Larder.NotSleep();

                Function.Call(Hash.ADD_TO_CLOCK_TIME, hours, 0, 0);

                // AND HE COMES ROUND WRECKED, if there is anything installed that tracks it.
                //
                // Not resting was only half of it. The hours passing at the ordinary rate takes
                // seven per cent off an eighty-hour sleep meter, which is not what six hours
                // face down in a gutter does to somebody -- it is what an afternoon does. So
                // the blackout takes a real bite out of both: he wakes up starving and he wakes
                // up shattered, because that is the state the whole thing is describing.
                //
                // Sleep harder than hunger, deliberately. An empty hunger meter costs health
                // over there, and a blackout that can kill you an hour later is a consequence
                // nobody could connect back to its cause.
                Core.Larder.Drain(WokeHungry, WokeTired);

                // And he is on the floor when the picture comes back, because nobody who has
                // just come round is stood up straight.
                Function.Call(Hash.SET_PED_TO_RAGDOLL, me.Handle, 4000, 5000, 0, true, true, false);

                Log.Info("Overdose: woke at " + spot + ", " + hours + " hours gone.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not move him: " + ex.Message);
            }
        }

        /// <summary>
        /// Where you turn up. Miles from the block, every one of them.
        ///
        /// That is the whole joke of the Trevor version: not a random street corner, but the
        /// far side of the map with no explanation and no car. Walking back IS the punishment.
        /// </summary>
        private static readonly Vector3[] Elsewhere =
        {
            new Vector3(-1600.0f, -1080.0f, 12.5f),    // Vespucci beach
            new Vector3(-1041.0f, -1396.0f, 5.0f),     // the sand at Del Perro
            new Vector3(1975.0f, 3815.0f, 32.4f),      // Sandy Shores, outside the Yellow Jack
            new Vector3(-434.0f, 6165.0f, 31.5f),      // Paleto Bay
            new Vector3(501.0f, 5604.0f, 797.9f),      // most of the way up Chiliad
            new Vector3(1105.0f, -3130.0f, 5.9f),      // the docks
            new Vector3(722.0f, 4180.0f, 40.7f),       // the Grapeseed fields
            new Vector3(-3020.0f, 100.0f, 11.6f),      // the Chumash coast road
            new Vector3(2570.0f, 4680.0f, 34.1f),      // Grapeseed, by the barn
            new Vector3(-1130.0f, 4940.0f, 220.9f)     // up in the Chiliad woods
        };

        /// <summary>
        /// How long you watch him go over before the screen starts to go.
        ///
        /// Two and a bit seconds, which is about how long a ragdoll takes to finish arguing
        /// with the pavement. Shorter and the fade eats the fall; much longer and it stops
        /// being a blackout and starts being a man having a lie down.
        /// </summary>
        private const int DropMs = 2200;

        /// <summary>How long the fade takes, how long it stays black, and the beat after.</summary>
        private const int FadeMs = 1200;
        private const int DarkMs = 2600;
        private const int SettleMs = 900;

        /// <summary>And how rough he is when he comes round.</summary>
        private const int WokeRoughMs = 45000;

        /// <summary>
        /// What it takes out of him, as fractions of each meter.
        ///
        /// Sleep the harder of the two. Both are subtracted rather than set, so somebody who
        /// went under already rough comes round worse -- but hunger is kept the lighter hit on
        /// purpose, because an empty hunger meter costs health in the other mod and a blackout
        /// that quietly kills you an hour later is a consequence nobody can trace back.
        /// </summary>
        private const float WokeHungry = 0.30f;
        private const float WokeTired = 0.55f;

        /// <summary>
        /// Past this, the sequence is broken and the screen comes back regardless.
        ///
        /// Fifteen seconds against a sequence whose longest honest path is about six. Wide
        /// enough that a slow teleport never trips it and short enough that nobody sits
        /// looking at a black screen wondering whether the game has gone.
        /// </summary>
        private const int GiveUpMs = 15000;

        /// <summary>Everything back to normal. Safe to call at any time, twice.</summary>
        public void Off()
        {
            _ritual.Stop();
            _taking = null;

            _live.Clear();

            Clear();

            _coming = false;
            _downUntil = 0;

            // A teardown in the middle of a blackout must not leave the screen black -- there
            // would be nothing left running to bring it back.
            if (_blackFrom != 0)
            {
                _blackFrom = 0;
                _wokeAt = 0;
                _fadeAt = 0;

                try { Function.Call(Hash.DO_SCREEN_FADE_IN, 500); }
                catch { }
            }

            try
            {
                var player = Game.Player;

                Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DAMAGE_MODIFIER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DEFENSE_MODIFIER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, player.Handle, 1f);
            }
            catch
            {
                // Teardown.
            }
        }

        /// <summary>
        /// The look, the sway, the gait and the sky, put back.
        ///
        /// SEPARATE FROM Off BECAUSE THE COMEDOWN NEEDS IT. Going from high to wrecked has to
        /// take the high off without handing the player back a normal camera and a normal
        /// walk for one frame in between, which is what calling Off would do.
        /// </summary>
        private void Clear()
        {
            _cycleOwner = null;

            _time = 1f;
            _shake = 0f;
            _sunny = false;
            _clipset = "";

            try
            {
                if (!string.IsNullOrEmpty(_cycle))
                {
                    Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                    _cycle = "";
                }

                if (!string.IsNullOrEmpty(_fx))
                {
                    Function.Call(Hash.ANIMPOSTFX_STOP, _fx);
                    _fx = "";
                }

                // TIME GOES BACK WHATEVER HAPPENED. It is a global the whole game reads, and a
                // mod that unloads while it is at 0.55 leaves the player in slow motion with
                // nothing left running that could put it right.
                Function.Call(Hash.SET_TIME_SCALE, 1f);

                if (_shaking)
                {
                    Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
                    _shaking = false;
                }

                if (_weather)
                {
                    Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
                    _weather = false;
                }

                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, false);

                    if (!string.IsNullOrEmpty(_clip))
                    {
                        Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, me.Handle, 0.5f);
                        _clip = "";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not clear a high: " + ex.Message);
            }
        }

        public void RestoreWorld()
        {
            Off();
        }
    }
}
