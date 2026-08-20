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

        public DialogueNode Root()
        {
            var node = Node("Whatchu need? And don't be handling nothin' you ain't buyin'.");

            // Each category wears the art of the first thing in it, so the rows are told
            // apart by shape before they are read.
            node.Say("Handguns.", () => Rack("Handguns", Armourer.Handguns), Count(Armourer.Handguns));
            node.WithWeapon(Armourer.Handguns[0].Weapon);

            node.Say("Something automatic.", () => Rack("Automatics", Armourer.Automatics), Count(Armourer.Automatics));
            node.WithWeapon(Armourer.Automatics[0].Weapon);

            node.Say("Shotguns.", () => Rack("Shotguns", Armourer.Shotguns), Count(Armourer.Shotguns));
            node.WithWeapon(Armourer.Shotguns[0].Weapon);

            node.Say("Something quiet.", () => Rack("Blades and bats", Armourer.Melee), Count(Armourer.Melee));
            node.WithWeapon(Armourer.Melee[0].Weapon);

            node.Say("Something I can throw.", () => Rack("Throwables", Armourer.Throwables), Count(Armourer.Throwables));
            node.WithWeapon(Armourer.Throwables[0].Weapon);

            node.Leave("Just looking.");
            return node;
        }

        private static string Count(Piece[] pieces)
        {
            return pieces.Length + (pieces.Length == 1 ? " of em" : " of em");
        }

        private DialogueNode Rack(string title, Piece[] pieces)
        {
            var node = Node(title + ". Cash only, and I never saw you.");

            foreach (var piece in pieces)
            {
                var item = piece;
                var owned = Owns(item);
                var canPay = Game.Player.Money >= item.Price;

                // Owning it already is not a refusal -- it is a top-up, and it is cheaper,
                // because you are buying rounds rather than the gun they go in.
                var cost = owned ? AmmoPrice(item) : item.Price;
                var label = owned ? item.Name + "  --  rounds" : item.Name;

                // What is in it right now, so buying rounds is a decision rather than a guess.
                var detail = owned
                    ? "You're carrying " + Held(item) + ".  " + item.AmmoBox + " more"
                    : item.Note + "  ·  comes with " + item.StarterAmmo;

                node.SayIf(canPay,
                           "You're $" + (cost - Game.Player.Money).ToString("N0") + " short",
                           label,
                           () => Buy(item, owned, cost),
                           "$" + cost.ToString("N0") + "  ·  " + detail);

                node.WithWeapon(item.Weapon);
            }

            node.Say("Show me something else.", Root);
            node.Leave("Not today.");
            return node;
        }

        /// <summary>Rounds cost a fifth of what the gun did, rounded to something tidy.</summary>
        private static int AmmoPrice(Piece piece)
        {
            return Math.Max(40, (int)Math.Round(piece.Price * 0.2f / 10f) * 10);
        }

        /// <summary>How many rounds the player has for this, right now.</summary>
        private static int Held(Piece piece)
        {
            try
            {
                return Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON,
                                          Game.Player.Character.Handle, piece.Hash);
            }
            catch
            {
                return 0;
            }
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
                    Function.Call(Hash.ADD_AMMO_TO_PED, player.Handle, piece.Hash, piece.AmmoBox);
                }
                else
                {
                    // A couple of clips in the bag, not a full load. The rest is a separate
                    // conversation with the same man, which is the point of him.
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, piece.Hash,
                                  piece.StarterAmmo, false, false);
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
                ? "Rounds for the " + piece.Name + ". Don't waste 'em, they ain't free."
                : "That's yours. Enough in it to be goin' on with -- come see me for the rest. " +
                  "And you ain't get it from me.");

            node.Say("What else you got.", Root);
            node.Leave("Good looking out.");
            return node;
        }
    }
}
