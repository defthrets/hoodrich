using System;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// What Vernon says, which is mostly his own lyrics.
    ///
    /// THE BARS ARE THE CHARACTER AND THEY HAVE TO BE GENUINELY BAD. Not surreal, not
    /// nonsense -- bad in the specific way an amateur is bad: the rhymes land, the meter is
    /// nearly right, and then the line ends somewhere embarrassing. "You need a fluorescent? I
    /// got a fluorescent. In stock." is the whole joke, and it only works because the four
    /// lines before it are competent enough that you were starting to think he could do it.
    ///
    /// AND HE NEVER ACKNOWLEDGES IT. There is not one line in this file where he winks. He
    /// wrote them in a van in twenty minutes and he is proud of that, and when you tell him the
    /// last part needs work he explains that everybody is a critic. A man who knows he is bad
    /// at this is a sad man; a man who does not is funny.
    ///
    /// HE ALSO ALREADY KNOWS YOU, which is the first thing he says and the thing that puts him
    /// in Franklin's world rather than beside it. He is not a contact you were introduced to --
    /// he is a man from the block who has been waiting on that wall for somebody worth playing
    /// the tape to.
    ///
    /// The job he asks for is not written yet and this file does not pretend otherwise. See
    /// Job: accepting calls out to whatever Main hands over, and when nothing is handed over he
    /// says he is lining it up, which is true.
    /// </summary>
    internal sealed class VernonTalk
    {
        /// <summary>The id the basement door is locked behind. See Main.</summary>
        public const string JobId = "vernon_intro";

        private readonly PlayerState _state;

        public VernonTalk(PlayerState state)
        {
            _state = state;
        }

        /// <summary>
        /// Set by Main: starts his job.
        ///
        /// Null until the job exists, and the accept row handles that rather than hiding --
        /// hiding it would mean the whole conversation ends in small talk and nobody could tell
        /// whether that was the writing or a bug.
        /// </summary>
        public Action Job { get; set; }

        /// <summary>
        /// What he sounds like between sentences.
        ///
        /// Up, always. He is pleased you stopped -- being stopped for is the entire event of
        /// his day -- so nothing in here is flat and nothing in here is an insult.
        /// </summary>
        public static readonly string[] Voice =
        {
            "GENERIC_HI", "GENERIC_HOWS_IT_GOING", "CHAT_STATE",
            "GENERIC_YES", "GENERIC_THANKS", "SHOP_GREETING"
        };

        private static DialogueNode Node(string line)
        {
            return new DialogueNode("Vernon", line);
        }

        /// <summary>
        /// The name he uses when he is rapping, which is the point of it being a second name.
        ///
        /// IT IS ALSO WHERE THE RECORDINGS SPLIT. Every file in this pack is named
        /// <c>slug(speaker)_hash(line)</c>, so a bar said as OG Vee is og_vee_*.wav and a line
        /// said as himself is vernon_*.wav -- which means the folder itself says which voice a
        /// file wants, rather than somebody having to keep a list beside them while recording.
        ///
        /// The panel changes name with him, which is the joke landing rather than a side
        /// effect: the man stops being Vernon for exactly as long as the verse lasts. His face
        /// does not change, because the photograph is taken off the ped stood in front of you
        /// rather than looked up from the name.
        /// </summary>
        private const string Stage = "OG Vee";

        /// <summary>
        /// One bar, which plays and goes to the next one on its own. See DialogueNode.Beat.
        ///
        /// A BAR IS A FILE. He is recorded in a different voice from the talking by somebody
        /// who cannot cut audio, so a verse cannot be one recording with four lines in it --
        /// it has to be four recordings, and four recordings means four nodes.
        /// </summary>
        private static DialogueNode Bar(string line, Func<DialogueNode> next)
        {
            return new DialogueNode(Stage, line).Beat(next);
        }

        /// <summary>
        /// The last bar of a verse, which is where your answers come back.
        ///
        /// Encore without a Runs: it plays every time like the rest of the verse, and then it
        /// waits, because the end of a verse is the one moment in it where you have something
        /// to say.
        /// </summary>
        private static DialogueNode Land(string line)
        {
            return new DialogueNode(Stage, line) { Encore = true };
        }

        public DialogueNode Root()
        {
            var met = _state != null && _state.MetVernon;
            var done = _state != null && _state.HasDone(JobId);

            if (!met)
            {
                if (_state != null)
                {
                    _state.MetVernon = true;
                    _state.Touch();
                }

                return Meeting();
            }

            return done ? Since() : Owed();
        }

        // ---- the first time ----------------------------------------------------

        private DialogueNode Meeting()
        {
            var node = Node(
                "Ayy. Ayy ayy ayy -- Franklin! Nah, don't do that face, homie, everybody know " +
                "you. It's Vernon. Leroy's boy. I sold you a extension cord out this exact " +
                "door when you was about yea high.");

            node.Say("Leroy's boy.", () => WhoHeIs(), "Let him get to it");
            node.Say("You still out here?", () => WhoHeIs());
            node.Leave("I'm movin', man.");

            return node;
        }

        private DialogueNode WhoHeIs()
        {
            var node = Node(
                "It's OG Vee now. Vee. Like the letter, but with weight on it. Vernon's what my " +
                "momma call me when the light bill late. OG Vee is what they gon' put on the " +
                "buildin'.");

            node.Say("OG Vee.", () => TheShop());
            node.Say("What happened to the shop?", () => TheShop(), "His dad's place");
            node.Say("You rap now?", () => Bars(), "He has been waiting for this");
            node.Leave();

            return node;
        }

        private DialogueNode TheShop()
        {
            var node = Node(
                "Pops wired half of Strawberry out this store. Ceiling fans, breakers, them long " +
                "orange cords -- forty years, Franklin. Then he passed and don't nobody 'round " +
                "here know where a light switch come from no more. So I stood in that basement " +
                "and I thought: that ain't a basement. That's square footage.");

            node.Say("Square footage for what?", () => Downstairs());
            node.Say("So you rap now.", () => Bars(), "He has been waiting for this");
            node.Leave();

            return node;
        }

        private DialogueNode Downstairs()
        {
            var node = Node(
                "I got a little operation goin' on down there. Clean, quiet, all mine -- and a " +
                "booth in the corner, 'cause a man gotta have somewhere to lay his vocals. " +
                "Can't nobody complain about the noise 'cause ain't nobody down there but me.");

            node.Say("Let me see it.", () => NotYet(), "Ask to go down");
            node.Say("Lay your what?", () => Bars(), "He has been waiting for this");
            node.Leave();

            return node;
        }

        private DialogueNode NotYet()
        {
            var node = Node(
                "Ho -- nah. Nah nah nah. You don't walk in a man's booth 'fore you heard his " +
                "single, that's disrespect. Do one thing for me first and I'll walk you down " +
                "there myself. Hit the lights and everything.");

            node.Say("What thing?", () => TheAsk(), "The work").MovesOn();
            node.Say("Let me hear it, then.", () => Bars());
            node.Leave();

            return node;
        }

        // ---- the tape ----------------------------------------------------------

        /// <summary>
        /// Verse one, and it is the hook.
        ///
        /// He announces it three times before he starts, which is what somebody does when they
        /// have been holding a verse all week waiting for somebody to stand still.
        /// </summary>
        private DialogueNode Bars()
        {
            // The run-up is him, not the record. Same voice as the rest of the talking, and it
            // carries straight into the first bar rather than waiting to be answered -- nobody
            // presses a button between "check it" and the verse.
            return Node("Aight. Aight aight. Hold up, hold -- okay. Check it. Check it:")
                       .Beat(() => VerseOne());
        }

        private DialogueNode VerseOne()
        {
            return Bar("It's OG Vee and I'm here to say,",
                   () => Bar("I move that white in a major way.",
                   () => Bar("My daddy sold lamps -- but I sell the light,",
                   () => LandOne())));
        }

        private DialogueNode LandOne()
        {
            var node = Land("ceilin' fan money, but the fan is white.");

            node.Say("...That's it?", () => Bars2());
            node.Say("Run that back.", () => Bars2(), "There is more");
            node.Leave("Yeah. Aight.");

            return node;
        }

        private DialogueNode Bars2()
        {
            return Node("That's the HOOK. That ain't even the verse, the verse is crazy. Listen:")
                       .Beat(() => VerseTwo());
        }

        private DialogueNode VerseTwo()
        {
            return Bar("V-E-E, that's the letter I be,",
                   () => Bar("Leroy's Electrical, that's my legacy.",
                   () => Bar("I got amps, I got volts, I got watts, I got stock --",
                   () => LandTwo())));
        }

        private DialogueNode LandTwo()
        {
            var node = Land("you need a fluorescent? ...I got a fluorescent. In stock.");

            node.Say("That last part needs work.", () => Critics());
            node.Say("That's cold, actually.", () => Proud());
            node.Leave("I gotta go.");

            return node;
        }

        private DialogueNode Critics()
        {
            var node = Node(
                "See, that's the industry talkin' right there. Everybody a critic 'til the song " +
                "on in they car. Nas ain't have no hook first album neither. I'ma sit with it.");

            node.Say("So what you need from me?", () => TheAsk(), "The work").MovesOn();
            node.Leave();

            return node;
        }

        private DialogueNode Proud()
        {
            var node = Node(
                "I KNOW. I know. And I wrote that in the van outside the beauty supply -- twenty " +
                "minutes, Franklin. Twenty minutes and a receipt.");

            node.Say("So what you need from me?", () => TheAsk(), "The work").MovesOn();
            node.Leave();

            return node;
        }

        // ---- the job -----------------------------------------------------------

        private DialogueNode TheAsk()
        {
            var node = Node(
                "Aight, real talk though. You busy? 'Cause I got a situation, and you look like " +
                "a man who handle situations. It ain't nothin' crazy. It's just somethin' I " +
                "can't do myself, and I can't ask nobody 'round here to do it neither.");

            node.Say("I'm listening.", () => Accept(), "Take the job").MovesOn();
            node.Say("Not today.", () => Decline());
            node.Leave("Not today.");

            return node;
        }

        /// <summary>
        /// Yes.
        ///
        /// Two answers, because the job may not exist yet. Both are him being pleased -- what
        /// changes is whether he sends you somewhere or tells you to give him a minute, and the
        /// second is the truth about where this is.
        /// </summary>
        private DialogueNode Accept()
        {
            if (Job == null)
            {
                var wait = Node(
                    "That's what I'm talkin' about. Aight -- gimme a minute to line it up " +
                    "proper, I ain't sendin' you out on somethin' half-done. Stay 'round the " +
                    "block. I'll be right here on this wall.");

                wait.Leave("Aight.");
                return wait;
            }

            var node = Node(
                "THAT'S what I'm talkin' about. Aight, come on, I'll put you on it. And " +
                "Franklin -- after? You comin' down them stairs and you hearin' the whole tape. " +
                "All of it. Front to back.");

            node.Say("Let's go.", () =>
            {
                try { Job(); }
                catch (Exception ex) { Core.Log.Debug("Vernon's job would not start: " + ex.Message); }

                return null;
            }, "Start it").MovesOn();

            node.Leave("Gimme a minute.");
            return node;
        }

        private DialogueNode Decline()
        {
            var node = Node(
                "Cool. Cool cool cool. I ain't beggin' nobody. I'll be right here on this wall " +
                "when you change your mind -- ain't like I got somewhere else to be, if I'm " +
                "bein' honest with you.");

            node.Leave();
            return node;
        }

        // ---- after the first time ----------------------------------------------

        private DialogueNode Owed()
        {
            var node = Node(
                "Ayy, my guy. Still on the wall. That thing I mentioned? Still standin', still " +
                "need doin'. And that door behind me still ain't openin' for nobody who ain't " +
                "handled it. That's just policy.");

            node.Say("Tell me again.", () => TheAsk(), "The work").MovesOn();
            node.Say("Spit that verse again.", () => Bars());
            node.Leave();

            return node;
        }

        private DialogueNode Since()
        {
            var node = Node(
                "Franklin! The man himself. Door's yours -- go on down, mind the third step, " +
                "it's a whole situation. Tape's on the desk down there. Don't skip nothin', I " +
                "sequenced it.");

            node.Say("Let me hear the new one.", () => NewBars(), "He has a new one");
            node.Leave();

            return node;
        }

        private DialogueNode NewBars()
        {
            return Node("Oh, you ready? Aight:").Beat(() => VerseThree());
        }

        private DialogueNode VerseThree()
        {
            return Bar("They said Vee can't rap -- now Vee got a plug,",
                   () => Bar("basement full of powder underneath a light bulb.",
                   () => Bar("I ain't sayin' no names 'cause my name on the deed --",
                   () => LandThree())));
        }

        private DialogueNode LandThree()
        {
            var node = Land("Leroy's Electrical: we supply the need.");

            node.Say("That's an album.", () => Album());
            node.Leave("Aight, Vee.");

            return node;
        }

        private DialogueNode Album()
        {
            var node = Node(
                "That's what I been sayin'! Three weeks I sat on that. Three weeks. And they " +
                "gon' act surprised.");

            node.Leave("Aight, Vee.");
            return node;
        }
    }
}
