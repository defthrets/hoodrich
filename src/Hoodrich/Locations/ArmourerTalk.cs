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
    /// browsing a catalogue, you are asking a man what he has. He answers by table -- handguns,
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

        public DialogueNode Root()
        {
            var node = Node("Whatchu need? And don't touch nothing you ain't buying.");

            // Each category wears the art of the first thing on that table, so the rows are
            // told apart by shape before they are read.
            node.Say("Handguns.", () => Table("Handguns", Armourer.Handguns), Count(Armourer.Handguns));
            node.WithWeapon(Armourer.Handguns[0].Weapon);

            node.Say("Something automatic.", () => Table("Automatics", Armourer.Automatics), Count(Armourer.Automatics));
            node.WithWeapon(Armourer.Automatics[0].Weapon);

            node.Say("Shotguns.", () => Table("Shotguns", Armourer.Shotguns), Count(Armourer.Shotguns));
            node.WithWeapon(Armourer.Shotguns[0].Weapon);

            node.Say("Something quiet.", () => Table("Blades and bats", Armourer.Melee), Count(Armourer.Melee));
            node.WithWeapon(Armourer.Melee[0].Weapon);

            node.Say("Something I can throw.", () => Table("Throwables", Armourer.Throwables), Count(Armourer.Throwables));
            node.WithWeapon(Armourer.Throwables[0].Weapon);

            node.Leave("Just looking.");
            return node;
        }

        private static string Count(Piece[] pieces)
        {
            return pieces.Length + " on the table";
        }

        private DialogueNode Table(string title, Piece[] pieces)
        {
            var node = Node(title + ". Cash, and I never saw you.");

            foreach (var piece in pieces)
            {
                var item = piece;
                var owned = Owns(item);
                var canPay = Game.Player.Money >= item.Price;

                // Owning it already is not a refusal -- it is a top-up, and it is cheaper,
                // because you are buying rounds rather than the gun they go in.
                var label = owned ? item.Name + "  (ammo)" : item.Name;
                var cost = owned ? AmmoPrice(item) : item.Price;

                node.SayIf(canPay,
                           "You are $" + (cost - Game.Player.Money).ToString("N0") + " short",
                           label,
                           () => Buy(item, owned, cost),
                           "$" + cost.ToString("N0") + "  ·  " + item.Note);

                node.WithWeapon(item.Weapon);
            }

            node.Say("Show me something else.", Root);
            node.Leave("Not today.");
            return node;
        }

        /// <summary>Rounds cost a quarter of what the gun did, rounded up to something tidy.</summary>
        private static int AmmoPrice(Piece piece)
        {
            return Math.Max(40, (int)Math.Round(piece.Price * 0.25f / 10f) * 10);
        }

        private static bool Owns(Piece piece)
        {
            try
            {
                return Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON,
                                           Game.Player.Character.Handle, piece.Hash, false);
            }
            catch
            {
                return false;
            }
        }

        private DialogueNode Buy(Piece piece, bool topUp, int cost)
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return Node("Not right now.");

            try
            {
                if (topUp)
                {
                    Function.Call(Hash.ADD_AMMO_TO_PED, player.Handle, piece.Hash, Math.Max(12, piece.Ammo));
                }
                else
                {
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, piece.Hash,
                                  piece.Ammo, false, false);
                }

                Game.Player.Money -= cost;
                _state.Touch();

                Notify.Ticker("~y~-$" + cost.ToString("N0") + "~s~  " + piece.Name);
                Log.Info("Bought " + piece.Weapon + " off Grimes for $" + cost + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Could not hand over " + piece.Weapon + ".", ex);
                return Node("Nah, forget that one. Pick something else.");
            }

            var node = Node(topUp
                ? "Rounds for the " + piece.Name + ". Don't waste 'em."
                : "That's yours. You didn't get it from me.");

            node.Say("What else you got.", Root);
            node.Leave("Good looking out.");
            return node;
        }
    }
}
