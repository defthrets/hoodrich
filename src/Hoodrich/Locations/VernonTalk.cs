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
        /// <summary>The id of his job, which is also the id his door used to be locked behind.</summary>
        public const string JobId = "vernon_intro";

        /// <summary>
        /// The toll he actually charges, and what the basement door is locked behind now.
        ///
        /// HE NAMES IT HIMSELF AND HE NAMES IT FIRST: you don't walk in a man's booth before
        /// you've heard his single, that's disrespect. So the tape is the price of the stairs
        /// -- which is both funnier and the only arrangement that works, because the job is
        /// briefed DOWN there now and a door locked behind the job would be a door locked
        /// behind itself.
        /// </summary>
        public const string HeardIt = "vernon_tape";

        /// <summary>He has walked you round the operation once. See Tour.</summary>
        public const string SawIt = "vernon_setup";

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
        /// Set by Main: the beats of his pitch, in order, out of the job's own definition.
        ///
        /// WRITTEN IN THE DATA AND NOT IN THIS FILE, which is where every other brief in the
        /// mod lives -- and nearly a bug, because the data had his whole pitch in it and this
        /// screen had no idea. Lamar's jobs reach their brief through FixerTalk; Vernon is not
        /// Lamar and has his own screen, so the words would have sat in missions.json being
        /// read by nobody while he said "I got a situation" and sent you out knowing nothing.
        ///
        /// Null until Main wires it, and TheAsk handles that: the short version is still a
        /// complete sentence, so a job with no beats on it is a terse man rather than a hole.
        /// </summary>
        public Func<string[]> Brief { get; set; }

        /// <summary>Set by Main: whether the pair of you are down in the basement. See TheAsk.</summary>
        public Func<bool> Below { get; set; }

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
        internal const string Stage = "OG Vee";

        /// <summary>
        /// A whole verse, in one go, under his other name.
        ///
        /// ONE TAKE. The bars were four nodes and four files for a while, and four recordings
        /// played back to back is not a verse -- the sound device closes and opens between
        /// each one, and those gaps land in the middle of the flow. A rap is performed in one
        /// breath or it is four sentences.
        ///
        /// The split that MATTERS is still here, and it is the voice rather than the bars: the
        /// run-up is Vernon and this is OG Vee, so the two are two files under two names and
        /// the folder still says which take belongs to which.
        ///
        /// Encore with no Runs: it plays every time, like anything anybody would ask to hear
        /// again, and then it waits -- the end of a verse is the one moment in it where you
        /// have something to say.
        ///
        /// The line breaks are for the panel, not the recording. Voice.Tidy flattens them
        /// before it hashes, so the file is named off the words alone and the screen still
        /// lays them out as four lines of a verse.
        /// </summary>
        private static DialogueNode Verse(string lines)
        {
            return new DialogueNode(Stage, lines) { Encore = true };
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
            // The run-up is him, not the record. Same voice as the rest of the talking, and
            // it carries straight into the verse rather than waiting to be answered -- nobody
            // presses a button between "check it" and the verse itself.
            return Node("Aight. Aight aight. Hold up, hold -- okay. Check it. Check it:")
                       .Beat(() => VerseOne());
        }

        private DialogueNode VerseOne()
        {
            var node = Verse(
                "It's OG Vee and I'm here to say,\n" +
                "I move that white in a major way.\n" +
                "My daddy sold lamps -- but I sell the light,\n" +
                "ceilin' fan money, but the fan is white.");

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

        /// <summary>He has played you the whole thing, so the stairs are open. See HeardIt.</summary>
        private void Heard()
        {
            try { if (_state != null) _state.MarkDone(HeardIt); }
            catch { /* then he plays it again, which he would rather do anyway */ }
        }

        private DialogueNode VerseTwo()
        {
            Heard();

            var node = Verse(
                "V-E-E, that's the letter I be,\n" +
                "Leroy's Electrical, that's my legacy.\n" +
                "I got amps, I got volts, I got watts, I got stock --\n" +
                "you need a fluorescent? ...I got a fluorescent. In stock.");

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

        /// <summary>
        /// The operation, shown to you by the man who built it, one proud beat at a time.
        ///
        /// HE IS NOT WRONG ABOUT ANY OF IT, HE IS JUST WRONG ABOUT ALL OF IT. Every single
        /// thing he points at is real and correct and belongs to a different trade. The
        /// lighting is genuinely excellent -- three circuits, daylight tubes, zoned -- because
        /// lighting is the one subject in this room he has forty years of family knowledge
        /// about. Everything else is retail logic applied to a business that has none: a cut
        /// station that is a card table, extraction that is a fan pointed up the stairs, a
        /// vault that is a filing cabinet, forty-one dollars of school chemistry glassware he
        /// has never once touched, and a hundred-and-ninety-dollar microphone held up by a tin
        /// of beans.
        ///
        /// THE TWO THINGS HE SPENT REAL MONEY ON ARE THE MICROPHONE AND THE LIGHTS, which is
        /// the whole man in one sentence. One of them is for the rapping and one of them is
        /// his father's trade, and neither has anything to do with the business he thinks he
        /// is running.
        ///
        /// AND THE JOKE ONLY WORKS IF HE NEVER WINKS, which is the same rule his verses run
        /// on. There is no line here where he admits any of it. He is showing a man round a
        /// facility. The shelf at the end is the only thing he concedes, and he concedes it as
        /// a supply problem rather than as an indictment -- which is exactly how he gets to
        /// the job.
        ///
        /// ONCE, AND THEN IT IS A CHOICE. Nobody wants the tour twice on the way to work; it
        /// stays on the menu for anybody who does.
        /// </summary>
        private DialogueNode Tour(int beat)
        {
            switch (beat)
            {
                case 0:
                    return Step(beat,
                        "AY -- I said watch that third step, I got a man comin' for it. ... Nah. " +
                        "Nah, don't say nothin' yet. Just look at it. LOOK, " +
                        "Frank. Six months of a grown man's own money, right there.");

                case 1:
                    return Step(beat,
                        "First thing. Look UP. Four-foot daylight tubes, five thousand kelvin, " +
                        "and they wired on three separate circuits so I can kill the front two " +
                        "and keep the booth lit. You ever been in a basement that got ZONES? " +
                        "You ain't. 'Cause don't nobody do it. I did it.");

                case 2:
                    return Step(beat,
                        "Cut station. Now that's a card table, but it's a GOOD one, it locks. " +
                        "Scales still boxed 'cause I'm waitin' on a stand for 'em. Extraction " +
                        "-- that fan right there. Points straight up them stairs, pulls the " +
                        "whole room out into the shop. Health and safety, Franklin.");

                case 3:
                    return Step(beat,
                        "And check the DESK. That's laboratory glass, that. Borosilicate. Got " +
                        "the flasks, got the beakers, got a hotplate and one of them digital " +
                        "thermometers -- forty-one dollars, whole lot, off a school supply " +
                        "catalogue. ... Nah, I ain't used none of it yet. I'm still readin' " +
                        "about it. But when I DO need it, Franklin? It's there.");

                case 4:
                    return Step(beat,
                        "And this is the booth. Duvet on that wall, egg boxes on that one -- " +
                        "that's treatment, that's what treatment IS. And that right there is a " +
                        "hundred and ninety dollar condenser microphone. Hundred and ninety, " +
                        "Frank. Come with the arm and everything. Arm droop a little bit, I got " +
                        "a can of beans holdin' it up, but that's temporary. You stand where I'm " +
                        "stood right now and it sound EXPENSIVE.");

                case 5:
                    return Step(beat,
                        "And that's the vault. ... It's a filin' cabinet, but it's a four-drawer " +
                        "and it got a KEY, and I'm the only one got the key. Two of them drawers " +
                        "is just paperwork for the shop. Ain't nobody lookin' in there for " +
                        "nothin'. That's called hidin' in plain sight, that's a technique.");

                default:
                    return Shelf();
            }
        }

        /// <summary>One stop on the tour, with a way out for anybody who has seen it.</summary>
        private DialogueNode Step(int beat, string line)
        {
            var node = Node(line);

            node.Say("Go on.", () => Tour(beat + 1)).WithIcon(Icons.Tick);
            node.Say("Vee. The job.", () => Shelf(), "Skip to it");
            node.Leave("Later, Vee.");

            return node;
        }

        /// <summary>
        /// The one empty thing in the room, and the reason you are stood in it.
        ///
        /// The turn from proud to asking, and he does it without changing register -- the
        /// shelf is not a failure, it is a supply-side issue. That is the sentence that gets
        /// him from the tour into the job, and it is also the offer: he is not hiring you, he
        /// is cutting you in, because a partner is cheaper than a wage and he has read that
        /// somewhere.
        /// </summary>
        private DialogueNode Shelf()
        {
            try { if (_state != null) _state.MarkDone(SawIt); }
            catch { /* he shows you again next time, happily */ }

            var node = Node(
                "And that's the shelf. ... Yeah. I know what's on it. That's exactly why we " +
                "talkin'. See I got the room, I got the lights, I got a SYSTEM -- every gram " +
                "come through here is in that notebook with a date on it and who took it -- and " +
                "I ain't got nothin' to put on the shelf. That's the only thing wrong with this " +
                "whole operation, Franklin. One thing.");

            node.Say("So fix it.", () => Offer(), "Hear him out").MovesOn().WithIcon(Icons.Tick);
            node.Say("Not today.", () => Decline());
            node.Leave("Not today.");

            return node;
        }

        private DialogueNode Offer()
        {
            var node = Node(
                "And listen -- you do this with me, you ain't payin' street no more. Not never " +
                "again. You in on the ground floor, so you get it at what I get it at, and I'm " +
                "'bout to be gettin' it at a number that ain't decent. Franklin, I will do you a " +
                "price that make you emotional. You gon' be angry at how good it is.");

            node.Say("I'm listening.", () => Pitch(0), "The job").MovesOn().WithIcon(Icons.Tick);
            node.Say("Not today.", () => Decline());
            node.Leave("Not today.");

            return node;
        }

        /// <summary>
        /// The ask -- upstairs it is an invitation, downstairs it is the pitch.
        ///
        /// HE WILL NOT DO BUSINESS ON THE PAVEMENT. Every hole in this arrangement is a thing
        /// he has to SHOW you: the empty shelf, the scales still in the box, the square
        /// footage he keeps saying the word about. Stood on Strawberry Avenue he can only
        /// describe it, and a man describing a basement is a man reading you a list. So on the
        /// street he sends you down the stairs, and the whole of it happens in the room it is
        /// about. See Downstairs, which is where Below comes from.
        /// </summary>
        private DialogueNode TheAsk()
        {
            if (Below == null || !Below())
            {
                var up = Node(
                    "Ho -- nah, not out here. Not on the pavement with the whole of Strawberry " +
                    "walkin' past. Go down them stairs, I'm right behind you. I gotta SHOW " +
                    "you somethin' anyway.");

                up.Say("Aight.", () => null, "Head downstairs").WithIcon(Icons.Tick);
                up.Leave("Later, Vee.");

                return up;
            }

            // THE TOUR FIRST, AND ONLY THE FIRST TIME. See Tour -- the job comes out of the
            // shelf being empty, so being shown the shelf IS the setup for it.
            if (_state == null || !_state.HasDone(SawIt)) return Tour(0);

            var node = Node(
                "Aight, real talk though. You busy? 'Cause I got a situation, and you look like " +
                "a man who handle situations. It ain't nothin' crazy. It's just somethin' I " +
                "can't do myself, and I can't ask nobody 'round here to do it neither.");

            node.Say("I'm listening.", () => Pitch(0), "Take the job").MovesOn();
            node.Say("Walk me round it again.", () => Tour(0), "The tour");
            node.Say("Not today.", () => Decline());
            node.Leave("Not today.");

            return node;
        }

        /// <summary>
        /// The pitch, however many beats it takes, and he finishes before you are asked.
        ///
        /// THE SAME SHAPE AS LAMAR'S, ON PURPOSE. A job you can accept from the first sentence
        /// is a job where the rest of what he said was decoration -- and in this one the rest
        /// of what he said is the entire joke, because every hole in the arrangement is in a
        /// beat you would have skipped. See FixerTalk.Brief, which does this for the other man.
        /// </summary>
        private DialogueNode Pitch(int beat)
        {
            string[] beats = null;

            try { beats = Brief == null ? null : Brief(); }
            catch { /* then he is a man of few words */ }

            if (beats == null || beat >= beats.Length) return Accept();

            var node = Node(beats[beat]);

            var next = beat + 1;

            if (next < beats.Length)
            {
                node.Say("Go on.", () => Pitch(next)).WithIcon(Icons.Tick);
            }
            else
            {
                node.Say("Aight.", () => Accept(), "Take it").MovesOn().WithIcon(Icons.Tick);
            }

            node.Say("Nah, Vee.", () => Decline());
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
                "Franklin! The man himself. Nah, don't go down on your own -- I said I'd walk " +
                "you down, so I'm walkin' you down. Mind that third step, it's a whole " +
                "situation. And the tape's already on, so don't talk over the second half.");

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
            var node = Verse(
                "They said Vee can't rap -- now Vee got a plug,\n" +
                "basement full of powder underneath a light bulb.\n" +
                "I ain't sayin' no names 'cause my name on the deed --\n" +
                "Leroy's Electrical: we supply the need.");

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
