using System;
using Color = System.Drawing.Color;
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

            // The NEW thing comes first and on its own terms: he works down his list in order,
            // and until you have been through it he tells you what needs doing rather than
            // handing you a board to pick from.
            var next = NextUndone();
            var offered = 0;

            if (next != null && _state.Rank >= next.MinRank)
            {
                node.Say(next.Name, () => Brief(next),
                         "$" + next.PayMin.ToString("N0") + "-" + next.PayMax.ToString("N0"));

                node.WithIcon(IconFor(next));
                offered++;
            }

            // Then anything already behind you, because a job you liked should be a job you can
            // go and do again. It pays the same and it counts the same; what it does not do is
            // hold up the next one.
            foreach (var def in _missions.All)
            {
                if (!_state.HasDone(def.Id)) continue;
                if (_state.Rank < def.MinRank) continue;

                var again = def;

                node.Say(def.Name, () => Brief(again),
                         "again  ·  $" + def.PayMin.ToString("N0") + "-" + def.PayMax.ToString("N0"));

                node.WithIcon(IconFor(def));
                offered++;
            }

            if (offered == 0)
            {
                return Nothing(next == null
                    ? "Ain't got nothing right now. Come see me later."
                    : "Got something for you, but not yet. Make more of a name first.");
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
                case MissionKind.DriveBy: return Icons.Garage;
                case MissionKind.Tags: return Icons.Weed;
                case MissionKind.Hit: return Icons.Guns;
                default: return Icons.Mask;
            }
        }

        /// <summary>The first job on his list you have not finished, or null once they are all behind you.</summary>
        private MissionDef NextUndone()
        {
            foreach (var def in _missions.All)
            {
                if (!_state.HasDone(def.Id)) return def;
            }

            return null;
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

            node.WithIcon(Icons.Tick);

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
