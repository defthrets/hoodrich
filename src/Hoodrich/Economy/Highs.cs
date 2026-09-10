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
    ///   e         colour that moves, and the legs to run about in it all night.
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

            /// <summary>What one dose counts for toward an overdose. Weed is half of one.</summary>
            public float Load = 1f;

            /// <summary>A screen effect to run underneath it, or "".</summary>
            public string Fx = "";

            /// <summary>
            /// Whether the look SWELLS rather than sitting at one strength.
            ///
            /// A timecycle held at a fixed strength is something you stop seeing after about a
            /// minute -- your eye writes it off as the colour the game is today, which is the
            /// exact opposite of what a trip should be doing to you. Six of the seven in here
            /// last a couple of minutes and that is fine for them. Acid lasts seven, and for
            /// seven minutes a fixed filter is wallpaper.
            ///
            /// So the strength rides on TWO periods that do not divide into each other -- see
            /// Swelling. The point of two is that the pattern never repeats and there is never
            /// a beat to it. A trip that pulsed to a rhythm would be a strobe, which is a
            /// different thing and a much worse one to do to somebody.
            /// </summary>
            public bool Swell;

            /// <summary>
            /// Whether the screen effect lands on the FIRST one rather than waiting.
            ///
            /// THIS IS WHY ONE TAB DID NOTHING. Recombine holds the postfx back until the dose
            /// reaches full -- deliberately, and right for six of the seven, because a screen
            /// effect cannot be done by halves and handing somebody the whole thing for one
            /// bump would leave the second bump with nowhere to go.
            ///
            /// Acid is the exception and it is the exception for a plain reason: the postfx IS
            /// the drug. Everything else here has a walk or a speed or a damage number to
            /// carry the first dose while the picture waits. This has a colour and nothing
            /// else, so with it held back the first tab was a green tint and a man wondering
            /// whether he had taken anything.
            /// </summary>
            public bool FxAtOnce;

            /// <summary>
            /// The second one's effect, and the colours it starts throwing.
            ///
            /// From the second dose the look stops being a filter and starts MOVING: the
            /// timecycle is transitioned between these every few seconds instead of being set
            /// once, so the world washes from one colour into the next and never settles. Fx2
            /// goes over the top of it.
            ///
            /// TRANSITIONED, NOT SET. SET_TIMECYCLE_MODIFIER swaps instantly and a hard swap
            /// every six seconds is a slideshow; SET_TRANSITION_TIMECYCLE_MODIFIER blends over
            /// a few seconds, which is what makes it read as the room changing colour rather
            /// than as the game changing setting.
            /// </summary>
            public string[] Riot = new string[0];
            public string Fx2 = "";

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
            // THE POT DICTIONARY LEADS, AND IT IS NOT THE ONE THIS ASKED FOR.
            //
            // It wanted amb@world_human_smoking_pot@male@male_a@base. The real one has no
            // male_a in it -- amb@world_human_smoking_pot@male@base -- and one wrong segment
            // is as absent as a name nobody has ever typed. It sat second in the ladder doing
            // nothing while the generic smoker underneath took the job.
            //
            // Checked against menyooStuff/PedAnimList.txt, which is every dictionary and clip
            // in the game as this install has it. No name in this file is a guess any more.
            Pairs = new[]
            {
                "amb@world_human_smoking_pot@male@base",    "base",
                "amb@world_human_smoking_pot@male@idle_a",  "idle_a",
                "amb@world_human_aa_smoke@male@idle_a",     "idle_a",
                "amb@world_human_smoking@male@male_a@base", "base"
            },
            // THE SCENARIO'S OWN JOINT FIRST. p_amb_joint_01 is the one the pot animation
            // was built around, so it sits in the hand the way the hand expects; the cutscene
            // joints have their own origins and want a fit (see Economy.Fit).
            Props = new[]
            {
                "p_amb_joint_01", "p_cs_joint_01", "p_cs_joint_02", "prop_sh_joint_01"
            },

            // THE POT ANIMATION SMOKES LEFT-HANDED. See Ritual.Recipe.Lefty -- the joint was
            // on the right hand and hanging by his thigh while his empty left hand went to
            // his mouth, which is what "smoking but not holding the joint" was.
            Lefty = true,
            Sits = new Vector3(0.02f, 0.01f, 0.0f),

            // Still named, because a fallback that never runs costs nothing and an install
            // missing all three dictionaries is better off in a scenario than stood still.
            Scenario = "WORLD_HUMAN_SMOKING_POT",

            // The first drag is what does it; the rest is standing there smoking. See Linger.
            Ms = 7400,
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
                "amb@world_human_smoking_pot@male@base",    "base",
                "amb@world_human_smoking_pot@male@idle_a",  "idle_a",
                "amb@world_human_aa_smoke@male@idle_a",     "idle_a",
                "amb@world_human_smoking@male@male_a@base", "base"
            },
            // A JOINT, NOT A CIGAR. The cigar props read as a cigar at arm's length -- a
            // fat brown thing with a gold band -- which is not what anybody means by weed.
            Props = new[] { "p_amb_joint_01", "p_cs_joint_01", "p_cs_joint_02" },

            // Same clips as the joint, so the same hand.
            Lefty = true,
            Sits = new Vector3(0.02f, 0.01f, 0.0f),
            Scenario = "WORLD_HUMAN_SMOKING_POT",
            Ms = 7800,
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
            // The dictionary holds exactly three clips -- _loop, _exit and _exit_cam -- and
            // the guessed _idle, _enter and bare name are gone. _loop is the one that plays
            // and the one the log confirmed.
            Pairs = new[]
            {
                "switch@trevor@trev_smoking_meth",          "trev_smoking_meth_loop",
                "switch@trevor@trev_smoking_meth",          "trev_smoking_meth_exit",
                "amb@world_human_aa_smoke@male@idle_a",     "idle_a",
                "amb@world_human_smoking@male@male_a@base", "base"
            },
            // PROP_CS_CRACKPIPE. One word, and every attempt so far spelled it with an
            // underscore -- prop_cs_crack_pipe -- which is a name the game has never had. Six
            // guesses across three sessions, all of them missing by a character or by a whole
            // word, and the log dutifully reported each one absent. RampageFiles/Lists/
            // ObjectList.txt has had the answer in it the entire time.
            // A METH PIPE. There is one -- prop_cs_meth_pipe, from the same cutscenes as the
            // crack pipe -- and the crack pipe is the fallback, not a cigarette.
            Props = new[] { "prop_cs_meth_pipe", "prop_cs_crackpipe" },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),
            Scenario = "WORLD_HUMAN_SMOKING",
            Ms = 7200
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
            // HAND TO THE NOSE, THEN THE RUB AFTER IT. This led with the meth pipe clip,
            // which is a man drawing on a pipe -- fine for meth and wrong for a bump.
            //
            // There is no snorting animation in this game and there never was. What the list
            // does have, once you look for the MOTION rather than the drug, is a deliberate
            // hand-to-nose emote and an ambient nose rub -- and a bump is those two things in
            // that order. The wrap in his hand does the rest of the explaining.
            // THE RAISE-TO-THE-FACE CLIPS, WHICH TURNED OUT TO BE THE COKE ONE ALL ALONG.
            //
            // These were written for the pills, on the reasoning that a hand going up to the
            // mouth and coming down is a swallow. It is -- and with a baggie pinched in the
            // fingers rather than a bottle it is also a man bumping something off the back of
            // his hand. Same motion, better home: it reads as an act, where the nose-pick emote
            // it replaces reads as a gesture.
            //
            // The nose rub stays under it, because the sniff afterwards is the half that sells
            // it, and the emote goes.
            Pairs = new[]
            {
                "amb@code_human_wander_drinking@male@idle_a",  "idle_a",
                "amb@code_human_wander_drinking@male@idle_a",  "idle_b",
                "amb@code_human_wander_idles@male@idle_a",     "idle_b_rubnose",
                "amb@world_human_drug_dealer_hard@male@base",  "base"
            },
            // THE SAME BAG HE HANDS PEOPLE ON THE CORNER. prop_meth_bag_01 is what PostUp
            // puts in his hand for a deal -- see BaggieProps there -- and it is sized for a
            // hand rather than for a shelf. Everything that has led this list so far was
            // stock: bkr_prop_coke_block_01a is a full kilo package, and the smallbags are
            // still bags rather than pinches.
            //
            // The offsets match the deal for the same reason. One object, one place in the
            // hand, whichever end of the transaction he happens to be on.
            Props = new[]
            {
                "prop_meth_bag_01", "bkr_prop_meth_smallbag_01a",
                "prop_drug_package_02", "h4_prop_h4_pouch_01a"
            },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),

            // Same reasoning as the pills. WORLD_HUMAN_DRUG_DEALER is a man loitering with
            // something in his hands, which is close enough to be tempting and is still a
            // scenario -- so it would drop the baggie, and the baggie is the only thing on
            // screen that says what he is doing.
            Ms = 5000
        };

        private static readonly Ritual.Recipe Pipe = new Ritual.Recipe
        {
            Pairs = new[]
            {
                "switch@trevor@trev_smoking_meth",          "trev_smoking_meth_loop",
                "amb@world_human_aa_smoke@male@idle_a",     "idle_a",
                "amb@world_human_smoking@male@male_a@base", "base"
            },
            Props = new[] { "prop_cs_crackpipe", "prop_cs_ciggy_01" },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),
            Scenario = "WORLD_HUMAN_SMOKING",
            Ms = 6600
        };

        /// <summary>The needle, and going over at the end of it.</summary>
        private static readonly Ritual.Recipe Needle = new Ritual.Recipe
        {
            // THE GAME HAS NO INJECTION ANIMATION FOR A STANDING PED. Everything with a
            // needle in it -- bfinjection, cs_fib3_syringe -- is cutscene body data, whole
            // scenes with their own camera, and none of it can be laid over a man on a corner.
            //
            // So it is the dealer's hands. drug_dealer_hard is a man working at something held
            // close to his own chest and forearm, which under a syringe is the shape of the
            // act, and the idle variants keep him moving through it rather than freezing. The
            // nod afterwards -- Slump, below -- is the half that actually reads, and needs no
            // dictionary at all.
            Pairs = new[]
            {
                "amb@world_human_drug_dealer_hard@male@base",   "base",
                "amb@world_human_drug_dealer_hard@male@idle_a", "idle_b",
                "amb@world_human_drug_dealer_hard@male@idle_a", "idle_a",
                "amb@world_human_drug_dealer_hard@male@idle_b", "idle_d"
            },
            Props = new[] { "prop_syringe_01", "p_syringe_01_s" },
            Sits = new Vector3(0.02f, 0.0f, 0.0f),
            Turned = new Vector3(0f, 0f, 90f),
            Scenario = "WORLD_HUMAN_DRUG_DEALER_HARD",

            // The nod. He goes down at the end of it, which is the half of this that nobody
            // needs an animation dictionary to read.
            Slump = true,
            Ms = 7000
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
            // amb@world_human_drug_dealer@ does not exist in any form. Only the _hard one
            // does, which is why that rung was dead.
            // HAND TO MOUTH IS THE WHOLE MOTION AND THE GAME HAS NO SWALLOW. There is no
            // pill clip anywhere in the list and no drinking dictionary either -- the drinking
            // SCENARIO is built from something not addressable by name -- so the smoking idle
            // does the work: it brings a hand up to the mouth and holds it there, and what is
            // in the hand is a bottle of pills rather than a cigarette.
            //
            // The drinking scenario stays underneath as the fallback, where the head-tip is
            // better than anything here if the dictionaries ever fail to load.
            // A SWALLOW, WHICH DOES EXIST -- IT IS JUST NOT FILED UNDER PILLS.
            //
            // There is no pill clip in the game and no drinking dictionary either, so the
            // smoking idle was standing in: hand to mouth and hold. It is the holding that is
            // wrong, because a cigarette goes up and STAYS up and a pill goes up and goes in.
            //
            // amb@code_human_wander_eating_donut is a man eating: hand to the mouth, the thing
            // disappears into it, and he chews. Filed under eating because it was written for
            // a donut, and it is the swallow this wanted all along. The static clip is the
            // standing-still one, which is what somebody stopped in the street is doing.
            // THE IDLES ARE THE ACT. BASE AND STATIC ARE JUST CARRYING IT.
            //
            // "He is holding his arm out" is precisely what base and static are: the pose a
            // wandering ped holds while it walks along with a thing in its hand. The raising,
            // the tip and the swallow are in the idle_* clips of the same family -- those are
            // what the ped plays when it stops and actually uses what it is holding, and they
            // are the reason this dictionary is worth having at all.
            //
            // And drinking rather than eating. Both raise a hand to the mouth, but eating ends
            // with a bite and a chew, and drinking ends with the head going back and the hand
            // coming down -- which is a man tipping pills in. The donut stays underneath in
            // case an install is missing the drinker.
            // THE EATING IDLES, NOW THE DRINKING ONE HAS GONE TO THE COKE.
            //
            // Hand up, the thing goes IN, and he works his jaw -- which for a bottle of pills
            // is tipping two out and getting them down, and is the one thing the drinking clip
            // does not do. Drinking ends with a tip and a lower; this ends with something
            // having been consumed, and that is the difference between a swallow and a sip.
            Pairs = new[]
            {
                "amb@code_human_wander_eating_donut@male@idle_a",  "idle_a",
                "amb@code_human_wander_eating_donut@male@idle_a",  "idle_b",
                "amb@code_human_wander_eating_donut@male@idle_a",  "idle_c",
                "amb@code_human_wander_eating_donut@male@base",    "base"
            },
            Props = new[]
            {
                "prop_cs_pills", "xm3_prop_xm3_bottle_pills_01a",
                "hei_prop_pill_bag_01", "prop_cs_script_bottle"
            },
            Sits = new Vector3(0.02f, 0.01f, 0.0f),

            // NO SCENARIO, AND THAT IS THE FIX. WORLD_HUMAN_DRINKING was the fallback here on
            // the reasoning that a head tipped back is most of a swallow. What it actually is,
            // when it fires, is a man producing a CUP OF COFFEE -- reported in exactly those
            // words. A scenario brings its own prop, and Scenario drops ours to make room, so
            // the fallback threw away the bottle of pills that was the entire explanation and
            // replaced it with a hot drink.
            //
            // Standing still holding a bottle of pills is a worse animation and a better
            // picture, and the ladder above has four real rungs before it could ever come to
            // this.
            Ms = 5200
        };
        // ---- what each one is ---------------------------------------------------

        private static readonly Recipe[] Book =
        {
            // WEED. Nothing happens quickly. The drift is a slight drunk gait rather than a
            // stagger -- somebody stoned does not fall over, they arrive late.
            new Recipe
            {
                Drug = "weed",
                Load = 0.5f,
                Cycles = new[] { "drug_wobbly", "stoned", "drug_flying_base", "spectator5" },
                Strength = 0.85f,
                Clipset = "move_m@drunk@slightlydrunk",
                Shake = 0.10f,
                Time = 0.80f,
                Ms = 100000,
                Line = "everything's fine. everything's real slow and fine"
            },


            // ECSTASY. COLOUR AND ENERGY, which is the one thing none of the other six do.
            //
            // It spent a while as a painkiller -- half damage, healing as you walk, a drunk
            // gait and a soft sway -- and every one of those numbers was about NOT feeling
            // things. A roll is the opposite of that. It is the drug you take to go all night.
            //
            // THE PICTURE MOVES, which is what separates this from the two other bright ones.
            // Coke is over-lit and SHARP; the bars are a sunny day. Here the colour warps and
            // slides, at six tenths so the world underneath stays readable -- slightly
            // psychedelic rather than a trip, because the trip is heroin and this one has to
            // be able to run about in.
            //
            // AND METH'S LEGS. 1.45 is between the wired one and coke, with time left at
            // normal on purpose: every other fast drug in here also drags the world, which
            // makes YOU feel quick. Nothing drags here, so the speed is genuinely yours.
            //
            // The comedown is long because a roll's is.
            new Recipe
            {
                Drug = "ecstasy",
                Doing = Pop,

                // Colour that moves. drug_gas_huffin warps, dont_tazeme_bro saturates, and the
                // flying pair are the pale bright ones behind them. All four are in the game's
                // own timecycle list on this machine; the ladder is for installs that differ.
                Cycles = new[] { "drug_gas_huffin", "dont_tazeme_bro", "drug_flying_02", "drug_wobbly" },
                Strength = 0.6f,

                Fx = "DrugsMichaelAliensFightIn",

                // Hurrying, not staggering. The drunk clipset it used to wear was the single
                // loudest thing about the old version and it said the exact wrong word.
                Clipset = "move_m@hurry@a",

                // Enough to feel, well short of the sway that reads as drunk.
                Shake = 0.22f,

                Time = 1f,
                Run = 1.45f,
                Hits = 1f,
                Takes = 0.7f,
                Heals = 1.6f,
                Ms = 180000,
                DownMs = 45000,
                Line = "everyone here is beautiful and i love this song"
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

            // LSD. THE LONG ONE, AND THE ONLY ONE THAT DOES NOTHING TO YOUR BODY.
            //
            // Every other drug on this list is a set of numbers about what you can do -- hit
            // harder, run faster, take less, walk worse. This one has none of them. No run
            // multiplier, no damage either way, and NO CLIPSET: he walks exactly as he always
            // walks. What changes is the picture, and only the picture, for a very long time.
            //
            // That is the whole character and it is why it is worth having alongside the other
            // seven. Weed and the bars already do "you are worse at things"; the stimulants
            // already do "you are better at things". Acid does neither, which leaves it free
            // to be the one that is genuinely about looking at something.
            //
            // SEVEN MINUTES, the longest here by two. A trip that is over in the time it takes
            // to drive across Davis is not a trip, and the low heat and the cheap tab are the
            // trade for having to live in it -- see the notes on the drugs.json entry.
            //
            // NO COMEDOWN. Every other long one hands you a minute of being no use to
            // anybody. The way off acid is a long flat glide, and the injured walk and the
            // grey screen would say something quite wrong about it. DownMs stays at nought,
            // the same as weed's.
            //
            // The flying family, all four verified in the machine's own timecycle list:
            // flying_01 is the strongest of them, and the ladder underneath is for an install
            // that is missing it. Held at nine tenths, which is further than anything else
            // here goes -- the point of this one is that the world is not right.
            new Recipe
            {
                Drug = "lsd",
                Doing = Pop,
                Cycles = new[] { "drug_flying_01", "drug_flying_02", "drug_flying_base", "drug_wobbly" },

                // OVER ONE ON PURPOSE, WHICH NOTHING ELSE IN HERE IS.
                //
                // Every effect is scaled by the dose -- half on the first, the recipe's own on
                // the second (see Live.Power) -- so a recipe written at 0.9 spends its whole
                // first dose at 0.45, which for a drug whose entire character is the picture
                // means the first tab did very little and looked like it had failed. At 1.7 the
                // first one is properly out there and the second is at the ceiling, which is
                // exactly the shape asked for: strong, then double.
                //
                // The game clamps a timecycle at one, so the number above it is not wasted --
                // it is what makes the swell stay near the top instead of dipping out of the
                // trip every few seconds.
                Strength = 1.7f,
                Swell = true,

                // The sustained clown one rather than crack's blend-in. Both are the same
                // family; this is the variant meant to be left running, which is the whole
                // difference over seven minutes.
                // THE FIRST TAB GETS THIS ONE, not the second. See Recipe.FxAtOnce.
                Fx = "DrugsTrevorClownsFight",
                FxAtOnce = true,

                // AND THE SECOND TAB STARTS THROWING COLOUR. Every name below is in the
                // machine's own timecycle list. The glasses_ family are full-screen colour
                // washes -- they are what the game tints the world with when somebody puts
                // coloured lenses on -- and blended one into the next every few seconds they
                // are the moving colour a trip is made of. gas_huffin warps, flying_01 is the
                // peyote green, and Barry1_Stoned is the one the game itself uses for being
                // off your head, so the rotation keeps coming back to something that reads as
                // a drug rather than as a disco.
                Riot = new[]
                {
                    "glasses_pink", "DRUG_gas_huffin", "glasses_purple", "drug_flying_01",
                    "glasses_orange", "Barry1_Stoned", "glasses_blue", "glasses_green",
                    "glasses_yellow", "drug_flying_02"
                },

                Fx2 = "DrugsMichaelAliensFight",

                // A drift, not a sway. Anything heavier reads as drunk and there is already a
                // drunk drug.
                Shake = 0.16f,

                // The world slightly behind you. Not heroin's half speed -- you can still
                // drive on this, you just should not.
                Time = 0.90f,
                Run = 1f,

                Ms = 420000,
                Line = "the road is breathing. give it a minute"
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

        /// <summary>
        /// What the come-down looks like once the grey has had its twenty seconds.
        ///
        /// drug_deadman is the drained, colourless look the game uses for a man who has just
        /// been scraped off the floor, and it is exactly right for the moment you come round
        /// in a car park with no idea how you got there. It is not right for the forty-five
        /// seconds after that, because a filter that strong stops being a state you are in and
        /// starts being a thing on your screen -- you go from feeling wrecked to waiting for
        /// your game back.
        ///
        /// So the grey is the arrival and this is the hangover. Same wobble, same ruined
        /// damage numbers, same injured walk -- the colour just comes back.
        /// </summary>
        private static readonly string[] DownAfterGrey = { "drug_wobbly", "drug_flying_base" };

        /// <summary>How long the colourless look lasts before it steps down.</summary>
        private const int GreyMs = 20000;

        private const string DownClipset = "move_m@injured";
        private const float DownShake = 0.18f;
        private const float DownTakes = 1.6f;

        // ---- where it is up to --------------------------------------------------

        /// <summary>The props the settings screen can fit, each with the ritual it is held in. See Economy.Fit.</summary>
        static Highs()
        {
            // The package is the plug's, not his: no ritual, left hand, no pose. It is here so
            // the same six sliders can put it in Gerald's hand properly.
            Fit.Wire(new[] { Joint, MethPipe, Pipe, Needle, Bump, Pop,
                             new Ritual.Recipe { Props = new[] { "prop_drug_package" }, Lefty = true } });
        }

        private readonly Ritual _ritual = new Ritual();
        private readonly Random _rng = new Random();

        /// <summary>Taken, being taken, and not landed yet. See Update.</summary>
        private Recipe _taking;

        /// <summary>One thing currently in him, and when it lets go.</summary>
        private sealed class Live
        {
            public Recipe What;
            public int Until;

            /// <summary>How many times it has been taken this go. See Power.</summary>
            public int Doses = 1;

            /// <summary>
            /// How hard it is hitting, nought upwards: half on the first one, the recipe's
            /// own on the second, more after, and a ceiling. Every effect a recipe has is
            /// scaled by this in Recombine, so the first joint is a mild one and the second
            /// is the one you notice.
            /// </summary>
            public float Power => Math.Min(MostPower, HalfDose * Doses);

            /// <summary>What it counts for toward an overdose: the recipe's weight, per dose.</summary>
            public float Load => What.Load * Doses;

            private const float HalfDose = 0.5f;
            private const float MostPower = 1.6f;
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

        /// <summary>When the colourless look gives way to the ordinary one, or 0.</summary>
        private int _greyUntil;

        /// <summary>Whether this instance has cleared up after whatever came before it.</summary>
        private bool _swept;

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

            // MIXING IS ALLOWED, AND SO IS ANOTHER OF THE SAME. A second one of the same
            // thing is a bigger dose of it -- see Live.Power -- up to a ceiling, past which
            // he has had enough of that one for now.
            foreach (var one in _live)
            {
                if (one.What.Drug == drugId && one.Doses >= MostDoses) return "You've had enough of that";
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
            var again = _live.Find(one => one.What == recipe);

            if (again != null)
            {
                // ANOTHER OF THE SAME: a bigger dose, and the clock starts over.
                again.Doses++;
                again.Until = Math.Max(again.Until, Game.GameTime + recipe.Ms);

                Log.Info("High: " + recipe.Drug + " again, dose " + again.Doses + ".");
            }
            else
            {
                _live.Add(new Live { What = recipe, Until = Game.GameTime + recipe.Ms });

                Log.Info("High: " + recipe.Drug + " for " + (recipe.Ms / 1000) + "s. " +
                         _live.Count + " in him.");
            }

            // TOO MUCH IS MEASURED IN DOSES NOW, NOT IN DRUGS. Four of anything hard, one
            // kind or four, is the same night; weed counts half.
            var load = 0f;
            foreach (var one in _live) load += one.Load;

            if (load > TooMany)
            {
                Overdo();
                return;
            }

            Recombine();

            // THE THIRD TAB IS NOT A TICKER. Everything else in here announces itself in the
            // corner because a drug you cannot feel yet needs to say it landed -- but the
            // third one announces itself by putting him nine hundred metres up, and a line of
            // text over the top of that is the mod explaining a joke it is still telling.
            if (Sky(recipe, again)) return;

            Notify.Ticker("~g~" + what + "~s~ -- " + (again != null ? "again. " : "") + recipe.Line);
        }

        /// <summary>
        /// Three tabs and the floor goes.
        ///
        /// ONCE A TRIP, not once per dose past three. It is cleared when the acid finally
        /// leaves him -- see Off -- so a session of taking one every ten minutes gets it once
        /// each time he starts again, and a fourth tab on the way down does not do it twice.
        /// </summary>
        private bool Sky(Recipe recipe, Live again)
        {
            if (recipe == null || recipe.Drug != "lsd") return false;
            if (again == null || again.Doses < SkyAt) return false;
            if (_fellThisTrip) return false;

            _fellThisTrip = true;

            Fall();
            return true;
        }

        /// <summary>How many tabs it takes.</summary>
        private const int SkyAt = 3;

        /// <summary>Whether it has already happened on this trip. See Sky.</summary>
        private bool _fellThisTrip;

        /// <summary>How many things he can have in him before he goes over.</summary>
        private const int TooMany = 3;

        /// <summary>Doses of one thing before it refuses. See Live.Power for what each does.</summary>
        private const int MostDoses = 4;

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
                var p = one.Power;

                // HALF ON THE FIRST ONE. Each effect's distance from "nothing" is scaled by
                // the dose: half the slow-down, half the shake, half the colour on the first,
                // the recipe's own on the second, more on the third. The walk and the screen
                // effect cannot be halved, so they wait for the second one -- which is what
                // makes the second one the one you notice.
                var slow = 1f - (1f - r.Time) * p;
                if (slow < time) time = slow;

                var quick = 1f + (r.Run - 1f) * p;
                if (quick > run) run = quick;

                hits *= 1f + (r.Hits - 1f) * p;
                takes *= 1f + (r.Takes - 1f) * p;
                heals *= 1f + (r.Heals - 1f) * p;
                shake += r.Shake * p;
                sunny |= r.Sunny;
                rage |= r.Rage;

                // THE SECOND ONE'S EFFECT IF HE HAS HAD TWO, otherwise the first one's --
                // and the first one's arrives immediately for anything that says so. See
                // Recipe.FxAtOnce and Recipe.Fx2.
                if (p >= 1f && !string.IsNullOrEmpty(r.Fx2)) fx = r.Fx2;
                else if ((p >= 1f || r.FxAtOnce) && !string.IsNullOrEmpty(r.Fx)) fx = r.Fx;

                if (p >= 1f && !string.IsNullOrEmpty(r.Clipset)) clipset = r.Clipset;
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

                if (_cycleOwner != _live[i].What || _cyclePower != _live[i].Power)
                {
                    _cycleOwner = _live[i].What;
                    _cyclePower = _live[i].Power;
                    Cycle(_live[i].What.Cycles, _live[i].What.Strength * _cyclePower);
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

        /// <summary>
        /// Every screen effect this run has asked for, so all of them can be stopped.
        ///
        /// A set rather than one string, because more than one can be on him at once and the
        /// single string only ever remembered the last.
        /// </summary>
        private readonly System.Collections.Generic.HashSet<string> _played =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>What the combine last worked out, for the per-frame half to hold.</summary>
        private float _time = 1f;
        private float _shake;
        private bool _sunny;
        private string _clipset = "";

        /// <summary>Whose look is on the screen, so it is not reapplied every recombine.</summary>
        private Recipe _cycleOwner;
        private float _cyclePower;

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

            // NOTHING OF OURS IS RUNNING, SO NOTHING OF OURS SHOULD BE ON THE SCREEN.
            //
            // Reported as being stuck in black and white, and the log said the mod had just
            // been reloaded with nothing high and nothing coming down. That is the whole bug:
            // a timecycle is a global the game holds until somebody clears it, and pressing
            // Insert throws away the object that knew it had set one. The new one comes up,
            // finds no highs and no comedown, and returns on the line below -- for ever, while
            // drug_deadman sits over everything. Teardown is supposed to catch this and
            // usually does; a script being aborted is not a promise that it ran.
            //
            // So the first tick of a new instance clears what the last one may have left. Only
            // once, and only when there is genuinely nothing of ours live -- which at startup
            // is always true, and after that this never runs again.
            if (!_swept)
            {
                _swept = true;

                if (_live.Count == 0 && !_coming && _blackFrom == 0) Sweep();
            }

            if (_falling != 0) { Falling(); return; }


            if (_blackFrom != 0) { Blackout(); return; }

            Swelling();

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

                        // AND THE SKY IS AVAILABLE AGAIN. See Sky -- once a trip, not once a
                        // session. He can do the whole thing again this evening; he just
                        // cannot do it twice off the same three tabs.
                        if (gone.Drug == "lsd") _fellThisTrip = false;

                        if (_live.Count == 0) { Crash(gone); return; }
                    }

                    if (went) Recombine();

                    // Drunk while the drunk walk is on him, and not otherwise. Lurching turns
                    // the gait on and off through a high; the flag goes with it.
                    var gait = Lurching(now) ? _clipset : "";

                    Hold(me, gait, _shake, _sunny, Sozzled(gait));

                    Trip(me, now);
                    return;
                }

                if (now >= _downUntil) { Off(); return; }

                // The grey steps down to the wobble, once. See DownAfterGrey.
                if (_greyUntil != 0 && now >= _greyUntil)
                {
                    _greyUntil = 0;
                    Cycle(DownAfterGrey, 1f);
                }

                // NOT DRUNK. The comedown is exhaustion, not drink -- move_m@injured is a man
                // who has been up all night, and he should not be falling over on it.
                Hold(me, DownClipset, DownShake, false, false);
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
            // LOADED: a second bar or a second shot, and his legs are not his. He goes over
            // a lot when he runs and now and then when he walks, whatever else is in him.
            // Otherwise it is the old rule -- three things at once before he trips at all.
            var loaded = false;
            foreach (var one in _live)
            {
                if ((one.What.Drug == "xanax" || one.What.Drug == "heroin") && one.Doses >= 2) loaded = true;
            }

            // ONE BAR IS ENOUGH TO GO OVER. The old rule was three things at once before his
            // legs were a problem at all, so a single xanax -- which puts him on the game's own
            // very-drunk walk and wobbles the camera -- never once put him on the floor. A man
            // walking like that who never falls is a man wearing an animation.
            //
            // Rare, though. It is asked every two and a half seconds and the odds below work
            // out at about one fall a minute while he is moving, which is often enough to be
            // his legs and seldom enough not to be a mechanic.
            var bars = false;
            foreach (var one in _live)
            {
                if (one.What.Drug != "xanax") continue;
                bars = true;
                break;
            }

            if (!loaded && !bars && _live.Count < TripsFrom) return;
            if (now < _nextTrip) return;
            _nextTrip = now + TripEveryMs;

            try
            {
                if (me.IsInVehicle()) return;

                if (loaded)
                {
                    if (me.Speed < WalkAbove) return;
                    var running = me.Speed >= TripAbove;
                    if (_rng.Next(100) >= (running ? LoadedRunTrip : LoadedWalkTrip)) return;
                    Function.Call(Hash.SET_PED_TO_RAGDOLL, me.Handle, TripDownMs, TripDownMs + 600, 0, true, true, false);
                    Log.Debug("Went over, loaded, " + (running ? "running." : "walking."));
                    return;
                }

                // ON BARS HE CAN GO OVER WALKING. Everything else needs him running: a man
                // strolling on a joint who face-plants is a bug, and a man on the very-drunk
                // walk who does exactly that is the point of the very-drunk walk.
                if (me.Speed < (bars ? WalkAbove : TripAbove)) return;

                if (bars)
                {
                    if (_rng.Next(100) >= (me.Speed >= TripAbove ? BarsRunTrip : BarsWalkTrip)) return;
                }
                else if (_rng.Next(100) >= TripChance)
                {
                    return;
                }

                Function.Call(Hash.SET_PED_TO_RAGDOLL, me.Handle, TripDownMs, TripDownMs + 600,
                              0, true, true, false);
                Log.Debug("Tripped over on " + _live.Count + (bars ? " with bars in." : "."));
            }
            catch
            {
            }
        }

        private int _nextTrip;

        /// <summary>How many in him before his legs are a problem.</summary>
        private const int TripsFrom = 3;

        /// <summary>How often it is even considered, and how fast he has to be going.</summary>
        private const int TripEveryMs = 2500;
        private const float TripAbove = 2.6f;

        /// <summary>The odds each time it is asked, out of a hundred, on anything but bars.</summary>
        private const int TripChance = 22;

        /// <summary>
        /// ONE BAR, running and walking. Asked every two and a half seconds, so twenty-four
        /// looks in a minute of moving: seven per cent running is about one fall a minute, and
        /// three walking is about one every two. Rare enough to be his legs rather than a rule.
        ///
        /// A second bar is a different man entirely -- see LoadedRunTrip, which is sixty.
        /// </summary>
        private const int BarsRunTrip = 7;
        private const int BarsWalkTrip = 3;

        /// <summary>Loaded on bars or a shot: the odds of going over per look, running and walking, and what counts as walking.</summary>
        private const int LoadedRunTrip = 60;
        private const int LoadedWalkTrip = 14;
        private const float WalkAbove = 0.8f;

        /// <summary>How long he is on the floor.</summary>
        private const int TripDownMs = 1400;

        /// <summary>
        /// Whether the drunk walk is on him this instant.
        ///
        /// ONE BAR IS A WOBBLE, NOT A CONDITION. A single xanax used to put the game's own
        /// very-drunk walk on him and leave it there for the whole two and a half minutes,
        /// which is not one bar -- it is a man who cannot stand up, permanently, off the
        /// smallest dose in the mod. It reads as the drug having one setting.
        ///
        /// So on a single bar it comes and goes: two seconds of it every thirty, and ordinary
        /// walking in between. He is fine, and then for a moment he is not.
        ///
        /// EVERYTHING ELSE IS UNCHANGED and holds its gait the whole time -- a second bar, a
        /// shot, anything mixed with anything. Those are conditions.
        /// </summary>
        private bool Lurching(int now)
        {
            if (!OneBar()) return true;

            if (now < _lurchUntil) return true;

            if (now >= _lurchNext)
            {
                _lurchUntil = now + LurchForMs;
                _lurchNext = now + LurchEveryMs;

                return true;
            }

            return false;
        }

        /// <summary>One bar and nothing else in him.</summary>
        private bool OneBar()
        {
            if (_live.Count != 1) return false;

            var one = _live[0];

            return one.What != null && one.What.Drug == "xanax" && one.Doses < 2;
        }

        private int _lurchUntil;
        private int _lurchNext;

        /// <summary>How long a wobble lasts and how often it comes round.</summary>
        private const int LurchForMs = 2000;
        private const int LurchEveryMs = 30000;

        /// <summary>The per-frame half: the gait, the sway, and the sky.</summary>
        private void Hold(Ped me, string clipset, float shake, bool sunny, bool drunk)
        {
            // ASKED FOR NOTHING MEANS TAKE IT OFF. Every other caller passes a real clipset
            // and this branch never runs for them; the single bar passes empty between its
            // lurches, and without this he would put the drunk walk on once and keep it.
            if (string.IsNullOrEmpty(clipset))
            {
                if (!string.IsNullOrEmpty(_clip))
                {
                    Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, me.Handle, 0.4f);
                    _clip = "";
                }
            }
            else if (_clip != clipset && Streamed(clipset))
            {
                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, me.Handle, clipset, 0.5f);
                _clip = clipset;
            }

            // THE STUMBLE FOLLOWS THE GAIT, NOT THE CAMERA.
            //
            // IT USED TO RIDE ON THE SHAKE and that is why he kept tripping after the bars had
            // worn off. SET_PED_IS_DRUNK is the game's loose-balance flag -- it is what makes
            // him catch his foot and go over -- and it was switched on with the camera shake
            // and never switched off again until the whole thing tore down. The comedown shakes
            // too, so for the fifteen seconds AFTER the log said "xanax wore off" he was still
            // flagged drunk, still stumbling, on a screen that had told him it was over.
            //
            // Tied to the walk instead: he stumbles while he is walking like a drunk and stops
            // when he is walking like a man who has been up all night, which is what the
            // comedown's gait actually is. Written only on a change, because it is a state flag
            // on the ped and another mod on this machine writes the same one for its own
            // reasons -- see Bare Minimum's Effects.Unsteady, which sets it when you are
            // exhausted. Two writers on one flag is survivable; two writers both hammering it
            // every frame is not.
            if (drunk != _drunk)
            {
                Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, drunk);
                _drunk = drunk;
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

        /// <summary>Whether that gait is one of the game's drunk ones. See Hold.</summary>
        private static bool Sozzled(string clipset)
        {
            return !string.IsNullOrEmpty(clipset) &&
                   clipset.IndexOf("drunk", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Whether we are the ones holding the loose-balance flag on. See Hold.</summary>
        private bool _drunk;

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
        /// <summary>
        /// The look, breathing, for anything whose recipe asks for it. See Recipe.Swell.
        ///
        /// TWO PERIODS THAT DO NOT DIVIDE INTO EACH OTHER, seventeen seconds and six point
        /// seven. Added together they never repeat inside any run of this, so it never settles
        /// into a pattern and there is never a beat you could tap along to. That last part is
        /// not decoration: something pulsing on a countable rhythm is a strobe.
        ///
        /// It rides on top of whatever Recombine last worked out, so a second dose still
        /// doubles it -- the swell is a shape, not a level.
        /// </summary>
        private void Swelling()
        {
            if (_cycleOwner == null || !_cycleOwner.Swell) return;
            if (string.IsNullOrEmpty(_cycle)) return;

            Rioting();

            var clock = Game.GameTime;

            var slow = Math.Sin(clock / SwellSlowMs * Math.PI * 2.0);
            var fast = Math.Sin(clock / SwellFastMs * Math.PI * 2.0);

            // Two thirds the slow one and a third the fast, so the long breath is the shape of
            // it and the short one only ever unsettles it.
            var ride = 1f + SwellDepth * (float)(slow * 0.66 + fast * 0.34);

            var want = _cycleOwner.Strength * _cyclePower * ride;

            // NOT WHILE HE IS IN THE SKY. On the ground the look is over a solid world and it
            // can be pushed as far as it likes -- there is a street under it whatever colour
            // it goes. Nine hundred metres up there is nothing but sky, and a full-strength
            // wash over nothing is not a trip, it is fog: the picture goes white, he vanishes
            // into it, and the one thing worth looking at is the thing you cannot see.
            //
            // So it is held back on the way down and the COLOUR does the work instead.
            if (_falling != 0 && want > FallLook) want = FallLook;

            if (want < 0f) want = 0f;

            try { Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, want); }
            catch { /* it stays where Recombine left it */ }
        }

        /// <summary>
        /// The colours, from the second one on.
        ///
        /// A LIST WALKED IN ORDER RATHER THAN PICKED AT RANDOM. Random would put two greens
        /// next to each other about as often as not, and two greens in a row is where a trip
        /// stops moving. Written in the order they should arrive and stepped through, so pink
        /// is always followed by the warp and never by another pink.
        ///
        /// The transition time is most of the gap between changes, so one is still arriving
        /// when the next is asked for and the world is never sat on a single colour.
        ///
        /// AND ONLY FROM THE SECOND. One tab is the postfx and the green; two is when the
        /// room starts changing colour round him. That difference is the whole point of a
        /// second one and it has to be visible from across the street.
        /// </summary>
        private void Rioting()
        {
            var r = _cycleOwner;

            if (r.Riot.Length == 0) return;
            if (_cyclePower < 1f) return;

            var now = Game.GameTime;

            // Quicker on the way down. Forty seconds of falling wants more than seven
            // changes in it, and there is nothing else happening to look at.
            var gap = _falling != 0 ? RiotFallMs : RiotEveryMs;

            if (_riotAt != 0 && now - _riotAt < gap) return;

            _riotAt = now;
            _riotStep = (_riotStep + 1) % r.Riot.Length;

            var name = r.Riot[_riotStep];

            try
            {
                Function.Call(Hash.SET_TRANSITION_TIMECYCLE_MODIFIER, name,
                              (_falling != 0 ? RiotFallBlendMs : RiotBlendMs) / 1000f);

                // The strength call in Swelling acts on whatever is current, so the name has
                // to be remembered here or the breath would go on riding the one before it.
                _cycle = name;
            }
            catch
            {
                // It stays on the one it was on, which is still a trip.
            }
        }

        private int _riotAt;
        private int _riotStep;

        /// <summary>How often the colour changes, and how long it takes to get there.</summary>
        private const int RiotEveryMs = 5200;
        private const float RiotBlendMs = 4200f;

        /// <summary>The same, on the way down, where there is nothing else to watch.</summary>
        private const int RiotFallMs = 2600;
        private const float RiotFallBlendMs = 2200f;

        /// <summary>
        /// The most the look may be worth while he is in the air. See Swelling.
        ///
        /// Well under one, because there is no world up there for it to tint -- past about
        /// two thirds a sky-only picture stops being a colour and becomes a white-out.
        /// </summary>
        private const float FallLook = 0.62f;

        /// <summary>The two breaths, and how far either of them moves it.</summary>
        private const double SwellSlowMs = 17000.0;
        private const double SwellFastMs = 6700.0;
        private const float SwellDepth = 0.45f;

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
                // THE ONE BEFORE IT STOPS FIRST, WHICH IS THE WHOLE OF THE BUG.
                //
                // This played the new effect and overwrote _fx, and ANIMPOSTFX_PLAY here is
                // asked to LOOP -- so the previous one carried on running with nothing left
                // pointing at it. Clear stops _fx, which by then names the newest, and every
                // effect before it was orphaned on the screen for the rest of the session.
                //
                // Reported as effects lingering and stuck after a minute, and it needs two
                // drugs to happen: one on its own is started and stopped by the same name.
                // The overdose mechanic is three or four.
                if (!string.IsNullOrEmpty(_fx) && _fx != name)
                {
                    try { Function.Call(Hash.ANIMPOSTFX_STOP, _fx); }
                    catch { }
                }

                Function.Call(Hash.ANIMPOSTFX_PLAY, name, 0, true);

                // Remembered whether or not it took. A name that fails ANIMPOSTFX_IS_RUNNING
                // here may still be running by the time anybody checks -- the flag is not
                // instant -- and a set of names we have asked for is the only honest record
                // of what might need stopping later.
                _played.Add(name);

                if (Function.Call<bool>(Hash.ANIMPOSTFX_IS_RUNNING, name))
                {
                    _fx = name;
                    return;
                }

                Log.Debug("Screen effect " + name + " would not run.");

                // AND A RECIPE THAT ASKED FOR ONE ASKED FOR A REASON. A name the game does not
                // have leaves nothing on the screen at all -- which is the same failure as the
                // effect being switched off, and indistinguishable from it. So it falls back
                // to one this mod has watched run rather than to nothing.
                //
                // Once, guarded by the name check: the fallback failing means the install has
                // no screen effects worth the name and there is nothing further to try.
                if (name != Proven) Fx(Proven);
            }
            catch
            {
                // The timecycle is doing most of the work anyway.
            }
        }

        /// <summary>
        /// A screen effect this mod has seen run, for when a recipe names one it has not.
        ///
        /// ANIMPOSTFX names cannot be checked against anything on disk the way animation and
        /// timecycle names can -- there is no list of them anywhere in the install -- so the
        /// only proof a name is real is having watched it work. This one has.
        /// </summary>
        private const string Proven = "DrugsTrevorClownsFightIn";

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
            _greyUntil = Game.GameTime + GreyMs;

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
        // ---- the third tab -------------------------------------------------------

        /// <summary>
        /// He is a very long way up and he was not a moment ago.
        ///
        /// FOUR MOMENTS, and they are the same four the blackout uses because they are the
        /// only four a teleport can safely have: dark, moved, shown, and put back. The move
        /// happens under a black screen because a teleport is a streaming request, and
        /// arriving before the world does is how you come round inside a cloud.
        ///
        /// HE FLIES DOWN, HE DOES NOT TUMBLE. The first version ragdolled him and it was
        /// wrong -- a man falling limp reads as a corpse, and what this is meant to be is the
        /// long floating descent with his arms out. TASK_SKY_DIVE is the game's own free-fall,
        /// the same one you are in after stepping out of a plane: he steers it, the camera
        /// does the right thing on its own, and none of it is animation this mod had to build.
        ///
        /// AND HE CANNOT DIE OF IT. He has no parachute, so left alone this ends one way. The
        /// screen goes before the ground arrives and he is put back exactly where he was
        /// standing -- which is also the only ending that makes sense, because none of it
        /// happened.
        /// </summary>
        private void Fall()
        {
            _falling = Game.GameTime;
            _fallStage = 0;
            _fellFrom = Vector3.Zero;

            try { Function.Call(Hash.DO_SCREEN_FADE_OUT, FallFadeMs); }
            catch { /* the rest still runs, it is just abrupt */ }

            Log.Info("Three tabs. Taking him up.");
        }

        private void Falling()
        {
            var now = Game.GameTime;
            var since = now - _falling;

            // THE TRIP DOES NOT STOP BECAUSE HE IS IN THE AIR, and it used to. This method
            // returns out of the top of Update, so the whole per-tick upkeep of the look --
            // the breath, the colours, the lot -- was skipped for the entire fall and what was
            // left was one timecycle at full strength, held still, for forty seconds. Which is
            // exactly what it looked like: a white fog with a man in it.
            //
            // It runs here as well, and harder. See FallLook.
            Swelling();
            Bare();

            // THE SAME HARD FLOOR THE BLACKOUT HAS, and for the same reason: the worst thing
            // this file can do to somebody is leave them stuck. Past the point where the
            // sequence could still be running, he goes back on the ground with his screen and
            // his skin whatever any of it thought it was doing.
            if (since > FallGiveUpMs)
            {
                Log.Warn("The fall ran long. Putting him back.");
                Ground();
                return;
            }

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) { Ground(); return; }

            switch (_fallStage)
            {
                case 0:
                    if (since < FallFadeMs) return;

                    Up(me);
                    _fallStage = 1;
                    return;

                case 1:
                    // A beat under the black for the sky to stream, then the lights.
                    if (now - _fallAt < FallHoldMs) return;

                    try { Function.Call(Hash.DO_SCREEN_FADE_IN, FallFadeMs); }
                    catch { }

                    _fallStage = 2;
                    return;

                case 2:
                    Easing(me);

                    // Down until the ground is close, and then the screen goes first.
                    if (me.Position.Z - _fellFrom.Z > FallCutAt &&
                        now - _fallAt < FallMostMs) return;

                    // SLOWLY, AND THE SONG PLAYS ALL THE WAY THROUGH IT. Three seconds rather
                    // than the eight hundred milliseconds the way up got, and the radio is not
                    // touched until the picture has already gone -- see Ground.
                    //
                    // That is also the only honest way to fade a radio out. There is no call
                    // that ramps its volume: it is on or it is off. What CAN be ramped is the
                    // picture, and a song still going while the world dims and then stopping
                    // at black is what a fade sounds like from the inside.
                    try { Function.Call(Hash.DO_SCREEN_FADE_OUT, FallOutMs); }
                    catch { }

                    _fallAt = now;
                    _fallStage = 3;
                    return;

                default:
                    if (now - _fallAt < FallOutMs + FallHoldMs) return;

                    Ground();
                    return;
            }
        }

        /// <summary>
        /// The last part of the way down, slowed.
        ///
        /// A REAL FALL FROM NINE HUNDRED METRES IS TWO SECONDS OF SPECTACLE AND FIFTEEN OF
        /// WAITING, and then it is over before you have looked at anything. He arrives at
        /// terminal velocity, which for a man is about fifty metres a second, and at fifty
        /// metres a second the last three hundred are gone in six.
        ///
        /// So the top of it is a fall and the bottom of it is a float. Nothing happens for the
        /// first six hundred metres -- he drops the way anybody drops -- and then the ceiling
        /// on how fast he may descend comes down with him, until at the point the screen takes
        /// him he is drifting at walking pace. That is where the colours are, that is where
        /// the city is close enough to be a city rather than a map, and it is the part worth
        /// spending the time on.
        ///
        /// ONLY THE DOWNWARD HALF IS TOUCHED. Whatever he is doing sideways is him steering,
        /// and taking that off him would turn the one interactive thing about this into a
        /// cutscene.
        /// </summary>
        private void Easing(Ped me)
        {
            try
            {
                var high = me.Position.Z - _fellFrom.Z;

                if (high > FallEaseFrom) return;

                var t = high / FallEaseFrom;
                if (t < 0f) t = 0f;

                var most = FallDrift + (FallTerminal - FallDrift) * t;

                var v = me.Velocity;

                if (v.Z >= -most) return;

                me.Velocity = new Vector3(v.X, v.Y, -most);
            }
            catch
            {
                // He falls at whatever the game gives him.
            }
        }

        /// <summary>Where the float starts, and the two speeds it runs between.</summary>
        private const float FallEaseFrom = 320f;
        private const float FallTerminal = 52f;
        private const float FallDrift = 7.5f;

        /// <summary>Straight up, arms out.</summary>
        private void Up(Ped me)
        {
            _fellFrom = me.Position;
            _fallAt = Game.GameTime;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, me.Handle);

                Bare();

                Function.Call(Hash.SET_ENTITY_INVINCIBLE, me.Handle, true);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, me.Handle, false);

                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle,
                              _fellFrom.X, _fellFrom.Y, _fellFrom.Z + FallHeight,
                              false, false, false);

                // The game's own free-fall. He steers it and the camera follows him properly,
                // which is the whole reason for using it rather than throwing him.
                Function.Call(Hash.TASK_SKY_DIVE, me.Handle, true);

                Music();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put him in the sky: " + ex.Message);
                Ground();
            }
        }

        /// <summary>
        /// Nothing on his back.
        ///
        /// TWO THINGS, AND TAKING THE WEAPON AWAY IS ONLY ONE OF THEM. The parachute is a
        /// weapon and it is also a BAG -- component slot five, the same slot a rucksack goes
        /// in -- and the two are set separately. Removing the weapon leaves the pack sitting
        /// there on his shoulders, which is what the screenshot showed: a man plainly wearing
        /// a parachute and falling to his death anyway.
        ///
        /// EVERY TICK OF THE FALL, not once at the top. TASK_SKY_DIVE is the game's own
        /// skydive and the game's own skydive believes people have parachutes; it puts one
        /// back. Asking every tick is two natives against a thing that costs the whole shot if
        /// it wins once.
        /// </summary>
        private static void Bare()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                Function.Call(Hash.REMOVE_WEAPON_FROM_PED, me.Handle,
                              Function.Call<uint>(Hash.GET_HASH_KEY, "GADGET_PARACHUTE"));

                Function.Call(Hash.SET_PLAYER_HAS_RESERVE_PARACHUTE, Game.Player.Handle, false);

                // Slot 5 is the bag. Nought, nought is nothing on it.
                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, 5, 0, 0, 0);
            }
            catch
            {
                // He falls either way.
            }
        }

        /// <summary>Back where he was standing, with everything given back.</summary>
        private void Ground()
        {
            var me = Game.Player.Character;

            try
            {
                if (me != null && me.Exists())
                {
                    Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, me.Handle);

                    if (_fellFrom != Vector3.Zero)
                    {
                        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle,
                                      _fellFrom.X, _fellFrom.Y, _fellFrom.Z,
                                      false, false, false);
                    }

                    Function.Call(Hash.SET_ENTITY_INVINCIBLE, me.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL, me.Handle, true);
                    Function.Call(Hash.SET_ENTITY_HEALTH, me.Handle,
                                  Function.Call<int>(Hash.GET_ENTITY_MAX_HEALTH, me.Handle));
                }
            }
            catch
            {
                // He is back either way.
            }

            Quiet();

            try { Function.Call(Hash.DO_SCREEN_FADE_IN, FallFadeMs); }
            catch { }

            _falling = 0;
            _fallStage = 0;
            _fallAt = 0;

            Log.Info("Back on the pavement.");
        }

        /// <summary>
        /// Radio Mirror Park, out loud, while he comes down.
        ///
        /// THE RADIO IS SILENT ON FOOT and that is the whole problem to solve. A station set
        /// while somebody is walking about plays to nobody -- the game only pipes it through a
        /// car. The mobile radio is the switch that makes it audible anywhere, and it is the
        /// same one the player has on their own phone.
        ///
        /// RADIO_16_SILVERLAKE is Mirror Park. That name is not a guess: the block already
        /// runs on RADIO_09_HIPHOP_OLD elsewhere in this mod and the whole family is spelt the
        /// same way.
        ///
        /// THE PARTICULAR SONG IS NOT SOMETHING THIS CAN PROMISE. A station can be named; a
        /// TRACK is named in the audio metadata, which lives inside the archives, and there is
        /// no list on this machine to check one against. So the station is switched for
        /// certain and the track is left to the station -- and if somebody turns up with the
        /// real internal name for it, [Highs] FallTrack plays it without a rebuild.
        ///
        /// What was here before this was a ladder of guessed MICHAEL3 music-event names. It
        /// went because a named station that definitely plays beats nine names that might.
        /// </summary>
        private void Music()
        {
            _radioWas = "";
            _phoneRadioWas = false;

            try
            {
                var station = Settings.Read("Highs", "FallStation", MirrorPark).Trim();
                if (station.Length == 0) return;

                _radioWas = Function.Call<string>(Hash.GET_PLAYER_RADIO_STATION_NAME) ?? "";
                _phoneRadioWas = Function.Call<bool>(Hash.IS_MOBILE_PHONE_RADIO_ACTIVE);

                // Heard while walking. Both of these, because the flag is what lets it play at
                // all outside a car and the state is what turns it on.
                Function.Call(Hash.SET_AUDIO_FLAG, "MobileRadioInGame", true);
                Function.Call(Hash.SET_MOBILE_PHONE_RADIO_STATE, true);
                Function.Call(Hash.SET_AUDIO_FLAG, "AllowRadioDuringSwitch", true);

                Function.Call(Hash.SET_RADIO_TO_STATION_NAME, station);

                // Only if somebody has supplied one. An invented track name is a call that
                // does nothing and reports nothing, which is the worst kind.
                var track = Settings.Read("Highs", "FallTrack", "").Trim();

                if (track.Length > 0)
                {
                    Function.Call(Hash.SET_RADIO_TRACK, station, track);
                    Log.Info("Fall music: " + station + ", asked for " + track + ".");
                }
                else
                {
                    Log.Info("Fall music: " + station + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put the radio on: " + ex.Message);
            }
        }

        /// <summary>
        /// The radio back the way he had it.
        ///
        /// BOTH HALVES, and the station last. Leaving the mobile radio switched on would mean
        /// music followed him round the street for the rest of the session -- a thing the mod
        /// did that nobody could work out how to stop.
        /// </summary>
        private void Quiet()
        {
            try
            {
                if (!_phoneRadioWas)
                {
                    Function.Call(Hash.SET_MOBILE_PHONE_RADIO_STATE, false);
                    Function.Call(Hash.SET_AUDIO_FLAG, "MobileRadioInGame", false);
                }

                Function.Call(Hash.SET_AUDIO_FLAG, "AllowRadioDuringSwitch", false);

                if (!string.IsNullOrEmpty(_radioWas))
                {
                    Function.Call(Hash.SET_RADIO_TO_STATION_NAME, _radioWas);
                }
            }
            catch
            {
                // It goes off with the next car he gets in either way.
            }

            _radioWas = "";
            _phoneRadioWas = false;
        }

        /// <summary>Radio Mirror Park. See Music for why this name can be trusted.</summary>
        private const string MirrorPark = "RADIO_16_SILVERLAKE";

        private string _radioWas = "";
        private bool _phoneRadioWas;

        /// <summary>How far up, and the clock the whole thing runs on.</summary>
        private const float FallHeight = 900f;
        private const float FallCutAt = 55f;

        private const int FallFadeMs = 800;

        /// <summary>The way back down. Long, because the song is going out with it.</summary>
        private const int FallOutMs = 3000;
        private const int FallHoldMs = 900;
        private const int FallMostMs = 45000;
        private const int FallGiveUpMs = 80000;

        private int _falling;
        private int _fallStage;
        private int _fallAt;
        private Vector3 _fellFrom;

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
            _greyUntil = Game.GameTime + GreyMs;

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
            _greyUntil = 0;

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
        /// Put back everything a previous instance of this class may have left set.
        ///
        /// CLEAR IS NO USE HERE AND THAT IS THE WHOLE POINT. It only clears the timecycle when
        /// it can see one in its own _cycle field -- which is right, because it is the thing
        /// that runs between one high and the next and must not stamp on somebody else's
        /// screen. A brand new instance has an empty _cycle and a stuck grey screen, and those
        /// two facts together are exactly the bug: the object that knew it had set a modifier
        /// went away when Insert was pressed, and the object that could clear it has no idea
        /// there is anything to clear.
        ///
        /// So this asks for nothing and clears unconditionally. It runs once, on the first
        /// tick, with nothing of ours live -- which is the one moment where "there should be
        /// no drug effects on this screen" is certainly true.
        /// </summary>
        private void Sweep()
        {
            try
            {
                Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                Function.Call(Hash.ANIMPOSTFX_STOP_ALL);
                Function.Call(Hash.SET_TIME_SCALE, 1f);
                Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);

                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, false);
                    Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, me.Handle, 0f);
                }

                _clip = "";
                _drunk = false;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not sweep up after the last run: " + ex.Message);
            }
        }

        /// <summary>
        /// Set by Main: put his chosen walk back on, because Clear has just taken it off.
        ///
        /// A HOOK RATHER THAN A REFERENCE. This file knows about drugs; it has no business
        /// knowing that a wardrobe exists, and the one thing it needs from it -- "his gait, as
        /// he last chose it" -- is a single call somebody else already owns.
        /// </summary>
        public Action Walk;

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
            _cyclePower = 0f;

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

                // EVERY EFFECT THIS RUN HAS EVER STARTED, not just the newest. See Fx.
                // Named rather than ANIMPOSTFX_STOP_ALL, which would also switch off whatever
                // another mod is running -- this stops exactly what we started and nothing
                // else.
                foreach (var name in _played)
                {
                    try { Function.Call(Hash.ANIMPOSTFX_STOP, name); }
                    catch { }
                }

                _played.Clear();
                _fx = "";

                // TIME GOES BACK WHATEVER HAPPENED. It is a global the whole game reads, and a
                // mod that unloads while it is at 0.55 leaves the player in slow motion with
                // nothing left running that could put it right.
                Function.Call(Hash.SET_TIME_SCALE, 1f);

                // ASKED FOR UNCONDITIONALLY, WHICH IS THE FIX AND THE WHOLE OF IT.
                //
                // HE KEPT TRIPPING AFTER THE BARS HAD WORN OFF, and the reason is that this
                // put his legs back only where its own bookkeeping said it had taken them. The
                // stumble is SET_PED_IS_DRUNK, which Hold turns on with the camera shake -- and
                // turning that flag off does not by itself put a walk back, because the game
                // fitted its own drunk gait underneath it. The reset that would have was
                // guarded by _clip.
                //
                // _clip IS OUR RECORD, NOT THE GAME'S. Hold only writes it when the clipset
                // actually streamed in: ask for one that has not loaded yet and the field stays
                // empty while SET_PED_IS_DRUNK has already changed how he walks. So the one
                // case where he most needs his legs back is the one case that skipped it.
                //
                // Sweep, at the top of this file, already worked this out for the startup case
                // and says so: it asks for nothing and clears unconditionally. This is the same
                // reasoning applied to the end of every high rather than only to the first tick
                // of a session. None of these calls costs anything on a player who is already
                // upright.
                Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
                _shaking = false;

                if (_weather)
                {
                    Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
                    _weather = false;
                }

                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, false);
                    Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, me.Handle, 0.5f);
                }

                _clip = "";
                _drunk = false;

                // AND HIS OWN WALK BACK, because the line above just took it off him too.
                //
                // The wardrobe's walk is a movement clipset like any other and RESET takes
                // whatever is on him, ours or his. Main sets his once and latches it, so
                // without this the first high of a session ended with his chosen gait gone for
                // the rest of it -- which nobody would connect to having taken a bar an hour
                // earlier. See Walk.
                if (Walk != null) Walk();
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
