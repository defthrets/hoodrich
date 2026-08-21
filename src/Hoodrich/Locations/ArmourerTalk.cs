using System;
using System.Drawing;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Buying off Grimes.
    ///
    /// A conversation rather than a shop screen, for the same reason Stretch is: you are not
    /// browsing a catalogue, you are asking a man what he has. He answers by kind -- handguns,
    /// automatics, shotguns, blades, throwables -- because that is how it would be laid out,
    /// and because five short lists read better than one list of twenty.
    /// </summary>
    internal sealed class ArmourerTalk
    {
        private readonly Armourer _big;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;

        public ArmourerTalk(Armourer big, Affiliation crew, PlayerState state)
        {
            _big = big;
            _crew = crew;
            _state = state;
        }

        private Color Tint => _crew.IsAffiliated ? _crew.Current.Colour : Palette.Text;

        private DialogueNode Node(string line) =>
            new DialogueNode(_big.Name, line) { SpeakerColour = Tint };

        /// <summary>
        /// What he sounds like mid-sentence.
        ///
        /// Warm, or at worst amused. He is pleased to see you -- you are the customer -- and the
        /// shared list the screen uses by default had an insult in it, which had him breaking
        /// off halfway through selling you a pistol to call you something.
        /// </summary>
        public static readonly string[] Voice =
        {
            "GENERIC_HOWS_IT_GOING", "SHOP_GREETING", "GENERIC_YES",
            "CHAT_STATE", "GENERIC_THANKS", "GENERIC_HI"
        };

        /// <summary>
        /// Opens the rack. Set by Main.
        ///
        /// The five racks used to be five dialogue pages you went into and came back out of, so
        /// finding out whether he had a shotgun was a round trip. They are one screen now, and
        /// this is the door to it.
        /// </summary>
        public Action Rack;

        public DialogueNode Root()
        {
            var node = Node("Whatchu need? And don't be handling nothin' you ain't buyin'.");

            node.Say("Show me what you got.", () =>
            {
                Rack?.Invoke();
                return null;
            }, "Everything he's holding");
            node.WithWeapon(Armourer.Handguns[0].Weapon);

            node.Leave("Just looking.");
            return node;
        }

    }
}
