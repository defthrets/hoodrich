using System;
using System.Drawing;
using GTA;
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
        /// <summary>How many he puts in front of you at a time.</summary>
        private const int Offers = 3;

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

        private DialogueNode Node(string line) =>
            new DialogueNode(_fixer.Name, line) { SpeakerColour = Tint };

        public DialogueNode Root()
        {
            // Job in hand: he does not want to hear about anything else.
            if (_runner.IsRunning)
            {
                return _runner.ReadyToCollect ? HandIn() : StillOn();
            }

            var node = Node("What's happening. You looking for work or you just walking past?");

            var offered = 0;

            foreach (var def in _missions.All)
            {
                if (offered >= Offers) break;

                var locked = _state.Rank < def.MinRank;

                node.SayIf(!locked,
                           "He wants somebody with more of a name",
                           def.Name,
                           () => Brief(def),
                           "$" + def.PayMin.ToString("N0") + "-" + def.PayMax.ToString("N0"));

                offered++;
            }

            if (offered == 0)
            {
                node.Say("...", () => Nothing(), "He has nothing going");
            }

            node.Leave("Not today.");
            return node;
        }

        private DialogueNode Nothing()
        {
            var node = Node("Ain't got nothing right now. Come see me later.");
            node.Leave("Alright.");
            return node;
        }

        private DialogueNode StillOn()
        {
            var node = Node("You already got something on. Go handle that first.");
            node.Say("On it.", () => null, _runner.Objective);
            return node;
        }

        /// <summary>The job explained, with the option to take it or walk.</summary>
        private DialogueNode Brief(MissionDef def)
        {
            var node = Node(def.Brief);

            node.Say("I'll do it.", () => Accept(def),
                     "$" + def.PayMin.ToString("N0") + "-" + def.PayMax.ToString("N0") +
                     "  ·  " + def.Rep.ToString("0") + " rep");

            node.Say("Nah, what else you got?", Root);
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

            var yes = Node("Good. Take the homies, they know where they going.");
            yes.Say("Say less.", () => null, _runner.Objective);
            return yes;
        }

        private DialogueNode HandIn()
        {
            var line = _runner.Collect();

            var node = Node(string.IsNullOrEmpty(line) ? "Good look. Take that." : line);
            node.Say("Anytime.", () => null, "Paid");
            return node;
        }
    }
}
