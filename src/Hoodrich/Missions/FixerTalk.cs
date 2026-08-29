using System;
using Color = System.Drawing.Color;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Missions
{
    /// <summary>
    /// What Lamar has to say.
    ///
    /// He only ever offers a few jobs at a time and only one runs at once, so the conversation
    /// is short: here is the work, here is what it pays, are you doing it. Coming back with it
    /// done is its own line, because getting paid should be something he says to you rather
    /// than a number that appears.
    /// </summary>
    internal sealed class FixerTalk
    {
        private readonly Fixer _fixer;
        private readonly MissionBook _missions;
        private readonly MissionRunner _runner;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;

        public FixerTalk(Fixer fixer, MissionBook missions, MissionRunner runner,
                         Affiliation crew, PlayerState state)
        {
            _fixer = fixer;
            _missions = missions;
            _runner = runner;
            _crew = crew;
            _state = state;
        }

        private Color Tint => _crew.IsAffiliated ? _crew.Current.Colour : Palette.Text;

        /// <summary>
        /// How he opens, which depends on whether you have done this one before.
        ///
        /// The first telling is why it matters -- his granddad's street, corners held since
        /// before you could reach the pedals. The fourth telling of that is what turns a
        /// repeatable job into a loop, and he would not say it that way twice anyway. Blank
        /// falls back, so a job with no second version is repetitive rather than broken.
        /// </summary>
        private string Opening(MissionDef def)
        {
            if (def == null) return "";

            var again = _state != null && _state.HasDone(def.Id);

            return again && !string.IsNullOrEmpty(def.BriefAgain) ? def.BriefAgain : def.Brief;
        }

        /// <summary>What he says when you bring it back, same rule.</summary>
        private string Closing(MissionDef def)
        {
            if (def == null) return "";

            var again = _state != null && _state.HasDone(def.Id);

            return again && !string.IsNullOrEmpty(def.DoneAgain) ? def.DoneAgain : def.Done;
        }

        /// <summary>
        /// His slot in the greeted list.
        ///
        /// Prefixed, because that list is keyed by gang id and he is not a gang -- an
        /// unprefixed "Lamar" would be a made-up gang id sat among real ones, and the day
        /// somebody adds a set called that, one of the two silently stops working.
        /// </summary>
        private string GreetKey => "fixer:" + _fixer.Name;

        private DialogueNode Node(string line) =>
            new DialogueNode(_fixer.Name, line) { SpeakerColour = Tint };

        /// <summary>
        /// Set by Main: whether one of ours is being hit right now.
        ///
        /// He will still take a finished job off you -- money is money -- but he is not handing
        /// out new work while there are people on the block. It is also the only way the escape
        /// phase makes any sense: a raid holds the wanted level at zero for its whole length, so
        /// "lose them, then get back to me" would complete the instant it started.
        /// </summary>
        public Func<bool> BlockUnderAttack;

        public DialogueNode Root()
        {
            // Job in hand: he does not want to hear about anything else.
            if (_runner.IsRunning)
            {
                return _runner.ReadyToCollect ? HandIn() : StillOn();
            }

            // He has just paid you and he wants a minute. Said in person as well as texted,
            // because walking back to a man who told you he would call is the obvious thing to
            // do and being shown his ordinary job list would make the text a lie.
            if (_runner.Resting)
            {
                var wait = Node("I told you cuz, I'm still schemin. Gimme like " +
                                _runner.RestLeft + (_runner.RestLeft == 1 ? " minute" : " minutes") +
                                " and I'll hit your line.");

                // The minutes left change every time he says it, so there is nothing stable to
                // hash. Named instead, and the recording keeps the number out of it -- the
                // screen is still showing how long.
                wait.Voiced(Voice.Named(_fixer.Name, "schemin"));

                wait.Leave("Aight, later.");
                return wait;
            }

            if (BlockUnderAttack != null && BlockUnderAttack())
            {
                var busy = Node("Not now, Frank. They ON the block right now. " +
                                "Go handle that and come back when it's quiet.");

                busy.Leave("On it.");
                return busy;
            }

            // THE INTRODUCTION IS SPENT THE FIRST TIME HE GIVES IT.
            //
            // "Bout time you came around to see your old friend Lamar" is a man who has not
            // seen you in a while. It is the wrong sentence the second time you walk up to him
            // -- and on his own job list that is every few minutes, so said forever it stops
            // being a greeting and becomes the noise the menu makes when it opens.
            //
            // After that he is just a man stood at his yard: are you working or walking past.
            //
            // MARKED HERE BECAUSE THIS IS WHERE HE SAYS IT. Everything above this line returns
            // early -- mid-job, resting, block under attack -- and in none of those has he
            // greeted you, so none of them should spend it. Same list the gang leaders use and
            // cleared by the same reset, because it is the same question about a different man.
            var first = _state == null || _state.MarkGreeted(GreetKey);

            var node = Node(first
                ? "What up Franklin! Bout time you came around to see your old friend Lamar, "
                  + "the one you done forgot about... Nah man, it's all love dawg. Got rid "
                  + "of that yee yee ass haircut I see nigga... come here I gots some shit "
                  + "we gotta work on."
                : "What's happening. You looking for work, or you just walking past?");

            // The NEW thing comes first and on its own terms: he works down his list in order,
            // and until you have been through it he tells you what needs doing rather than
            // handing you a board to pick from.
            // Everything up to one past the furthest you have got, which is what "unlocked"
            // means when the list is worked through in order. Offering only the next undone one
            // hid jobs that were plainly available -- finish the third and the fourth is open,
            // whether or not you have gone back and done the second.
            var reached = -1;

            for (var i = 0; i < _missions.All.Count; i++)
            {
                if (_state.HasDone(_missions.All[i].Id)) reached = i;
            }

            var offered = 0;

            // The WHOLE list, every time, with the ones you cannot take yet greyed out and
            // saying why.
            //
            // It used to show only what you could take. Which meant his list grew as you worked
            // through it, and a man with one job on Tuesday and four on Thursday reads as a mod
            // that has just started working rather than as a chain you are getting through --
            // and there was no way at all to find out that the fourth one wants a rank you have
            // not made. A locked row you can see is a reason to go and earn something; a row
            // that is not there is nothing.
            for (var i = 0; i < _missions.All.Count; i++)
            {
                var def = _missions.All[i];

                var pick = def;
                var done = _state.HasDone(def.Id);

                // Further down the chain than you have got. He works his list in order.
                //
                // The rank wall used to sit above this and it is gone. It gated his list on a
                // number that only moves if you stand on corners, which meant somebody who
                // wanted to do the JOBS found the jobs locked behind not doing the jobs -- and
                // it said so four rows at a time, in red, which is a list that mostly tells you
                // no. The chain already says which one is next; that is the whole gate now.
                if (i > reached + 1)
                {
                    node.Say(def.Name, () => null, "locked", false,
                             "He'll get to this one. Finish what's above it first");

                    node.WithIcon(IconFor(def));
                    offered++;
                    continue;
                }

                // Just did one. He is not lining up the next before you have got out of the
                // car, and a row counting down is a great deal friendlier than a row that has
                // vanished.
                if (_state.JobsAreCooling)
                {
                    node.Say(def.Name, () => null, "wait  ·  " + _state.JobWaitWord, false,
                             "Give it a minute, cuz. Come back in " + _state.JobWaitWord);

                    node.WithIcon(IconFor(def));
                    offered++;
                    continue;
                }

                // Shut is shown rather than hidden. A job that quietly vanishes from his list
                // between two and six in the morning reads as the mod having lost it; the same
                // job sat there saying when it opens reads as a shop with hours.
                if (!Open(def))
                {
                    node.Say(def.Name, () => Shut(pick), "shut  ·  " + Hours(def));
                    node.WithIcon(IconFor(def));
                    offered++;
                    continue;
                }

                node.Say(def.Name, () => Brief(pick),
                         (done ? "again  ·  $" : "$") +
                         def.PayMin.ToString("N0") + "-" + def.PayMax.ToString("N0"));

                node.WithIcon(IconFor(def));
                offered++;
            }

            if (offered == 0)
            {
                // Either the list is empty, or everything open to you wants a bigger name than
                // you have. Those are different sentences, so which one he says depends on it.
                return Nothing("Ain't got nothing right now. Come see me later.");
            }

            node.Leave("Not today.");
            return node;
        }

        /// <summary>
        /// Art for a job, by what the job actually is.
        ///
        /// The kind is the only thing that reliably distinguishes one from another at a glance,
        /// and it is the thing you care about when picking: whether this is hands or guns.
        /// </summary>
        private static Icon IconFor(MissionDef def)
        {
            switch (def.Kind)
            {
                case MissionKind.BikeRide: return Icons.Health;
                case MissionKind.DriveBy: return Icons.FromFile("car.png");

                // The fire, now that there is one. It wore the garage icon on the grounds that
                // a torch job is still a job you do from a car -- with a comment admitting the
                // burning was the part it did not show. It shows it.
                case MissionKind.TorchJob: return Icons.FromFile("fire.png");
                case MissionKind.Tags: return Icons.FromFile("spray.png");
                case MissionKind.Hit: return Icons.Guns;
                default: return Icons.Mask;
            }
        }
        /// <summary>Whether the clock is inside this job's window, if it has one.</summary>
        private static bool Open(MissionDef def)
        {
            try
            {
                return def.OpenNow(Function.Call<int>(Hash.GET_CLOCK_HOURS));
            }
            catch
            {
                // No clock, no restriction. Refusing a job because a native did not answer is
                // the worst of both.
                return true;
            }
        }

        /// <summary>The window, said the way he would say it.</summary>
        private static string Hours(MissionDef def)
        {
            return MissionDef.Clock(def.OpensHour) + " to " + MissionDef.Clock(def.ClosesHour);
        }

        /// <summary>Him telling you to come back when the place is open.</summary>
        private DialogueNode Shut(MissionDef def)
        {
            var node = Node("Nah, not right now. The whole thing ends up at that store and " +
                            "they got the shutter down. Come see me between " + Hours(def) +
                            " and we'll go.");

            // Same again: the opening hours come off the job, so this is not fixed text.
            node.Voiced(Voice.Named(_fixer.Name, "shut"));

            node.Say("Alright, later.", Root);
            node.Leave("Cool.");
            return node;
        }

        private DialogueNode Nothing(string line = null)
        {
            var node = Node(string.IsNullOrEmpty(line)
                ? "Ain't got nothing right now. Come see me later."
                : line);

            node.Leave("Alright.");
            return node;
        }

        private DialogueNode StillOn()
        {
            var node = Node("You already got something on. Go handle that first.");
            node.Say("I'm on it.", () => null, _runner.Objective).MovesOn();
            return node;
        }

        /// <summary>The job explained, with the option to take it or walk.</summary>
        /// <summary>
        /// The pitch, however many beats it takes.
        ///
        /// He gets to finish talking before you are asked. A job you can accept from the first
        /// sentence is a job where the rest of what he said was decoration.
        /// </summary>
        private DialogueNode Brief(MissionDef def, int beat = 0)
        {
            if (beat < def.BriefMore.Count)
            {
                var more = Node(beat == 0 ? Opening(def) : def.BriefMore[beat - 1]);

                var next = beat + 1;
                more.Say("Go on.", () => Brief(def, next));
                more.WithIcon(Icons.Tick);

                more.Say("Nah. What else you got?", Root);
                more.Leave("Forget it.");
                return more;
            }

            var node = Node(beat == 0 ? Opening(def) : def.BriefMore[beat - 1]);

            node.Say("I'll do it.", () => Accept(def),
                     "$" + def.PayMin.ToString("N0") + "-" + def.PayMax.ToString("N0") +
                     "  ·  " + def.Rep.ToString("0") + " rep");

            node.WithIcon(Icons.Tick);
            node.MovesOn();

            node.Say("Nah. What else you got?", Root);
            node.Leave("Forget it.");
            return node;
        }

        private DialogueNode Accept(MissionDef def)
        {
            var refusal = _runner.Start(def);

            if (refusal != null)
            {
                var no = Node("Then it ain't happening right now.");
                no.Say("Alright.", () => null, refusal);
                return no;
            }

            // THREE cases, not two, because "no homies" and "on your own" are not the same
            // thing and reading them as one had him telling you to go alone on the one job he
            // personally comes with you on.
            //
            // The tag run really is one man: two dudes on bikes with cans is a whole story for
            // the laws and one is a kid, which is the brief's own reasoning. The bike ride is
            // him and you, no crew -- that is the entire point of it.
            string line;

            if (def.Homies > 0)
            {
                line = "Good. Take the homies, they know where they going.";
            }
            else if (def.Kind == MissionKind.BikeRide)
            {
                line = "Good. Just me and you on this one. No crew, no convoy, none of that.";
            }
            else
            {
                line = "Good. And you go by yourself, remember. Just you.";
            }

            var yes = Node(line);
            yes.Say("Say less.", () => null, _runner.Objective).MovesOn();
            return yes;
        }

        /// <summary>
        /// Handing the job in.
        ///
        /// The payout used to happen while this node was being BUILT, which meant the money
        /// landed the instant the conversation opened -- before his line was on screen, and even
        /// if you walked off without pressing anything. Now the choice does it, so getting paid
        /// is something you do rather than something that happens at you.
        /// </summary>
        private DialogueNode HandIn()
        {
            var def = _runner.Current;

            // He does not say the same thing over a body.
            //
            // The written line is him delighted about a basketball court and a shop that went
            // smoothly, and it reads as the mod not having noticed when the man behind the
            // counter is dead. He is not going to hand the money back over it -- but he is
            // going to say something, and then get on with it, which is the whole character.
            var line = def == null || string.IsNullOrEmpty(Closing(def))
                ? "Good look. Come get this."
                : Closing(def);

            // AND ONLY ON THE JOB THAT HAD A SHOP IN IT.
            //
            // KilledTheClerk is a fact about the bike ride that never expires, and this asked
            // for it on every hand-in -- so once you shot the man behind the counter on the
            // first job, Lamar opened every debrief after it by grieving him. Burn a car on the
            // other side of the city, come back, and he is still asking why you couldn't let
            // the guy open the register.
            if (_runner != null && _runner.ClerkKilled &&
                def != null && def.Kind == MissionKind.BikeRide)
            {
                line = "Damn, dawg. DAMN. You didn't have to do that man, he was openin' it. "
                     + "He was literally openin' it. ...Aight. Aight, it's done, I ain't gon' "
                     + "sit here and cry about a man I never met. Just -- next one, let him "
                     + "hand it over, yeah? Here. Take this before I change my mind about how "
                     + "funny it was.";
            }

            var node = Node(line);

            node.Say("Appreciate it.", () =>
            {
                _runner.Collect();
                return null;
            }, "Take the money");

            node.WithIcon(Icons.Money);
            node.MovesOn();
            return node;
        }
    }
}
