using System;
using Color = System.Drawing.Color;
using GTA;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Supply
{
    /// <summary>One thing Juan will sell you, in the quantity he sells it in.</summary>
    internal sealed class Brick
    {
        public readonly string DrugId;
        public readonly float Grams;
        public readonly string Label;

        public Brick(string drugId, float grams, string label)
        {
            DrugId = drugId;
            Grams = grams;
            Label = label;
        }
    }

    /// <summary>
    /// Buying weight off Juan.
    ///
    /// He does not do grams and he does not do ounces. Everything on this list is a brick or
    /// several, and nothing on it comes to less than fifty thousand dollars -- that is the whole
    /// point of him. Stretch sells you enough to work a corner tonight; Juan sells you enough
    /// that the corner stops being the interesting part.
    ///
    /// It all goes to the house, not your pockets. You cannot walk around with twelve kilos of
    /// anything, and pretending otherwise would make the stash house decorative.
    /// </summary>
    internal sealed class DealerTalk
    {
        /// <summary>Nothing he sells comes in under this.</summary>
        public const int Floor = 50000;

        /// <summary>
        /// What he breaks it down into.
        ///
        /// Sized per product so every one of them clears the floor on its own -- twelve kilos of
        /// weed and three of heroin both come to fifty-odd thousand, which is what makes them
        /// the same rung on the same ladder rather than two unrelated numbers.
        ///
        /// These have to be re-checked whenever a price moves. Heroin dropping from $200 to $80
        /// a gram took its kilo down to nineteen thousand, and the floor would then have quietly
        /// charged you fifty for it -- $50 a gram wholesale on something that sells at $80.
        /// A floor that rounds UP is only safe while every brick is genuinely above it.
        /// </summary>
        private static readonly Brick[] Bricks =
        {
            new Brick("weed",    12000f, "Twelve kilos of weed"),
            new Brick("ecstasy",  9000f, "Nine kilos of pills"),
            new Brick("meth",     4000f, "Four kilos of meth"),
            new Brick("crack",    2500f, "Two and a half of crack"),
            new Brick("coke",     1500f, "A kilo and a half of coke"),
            new Brick("heroin",   3000f, "Three kilos of heroin"),
        };

        /// <summary>How many of one thing you can take at once.</summary>
        private static readonly int[] Lots = { 1, 2, 4 };

        private readonly Delivery _delivery;
        private readonly Drugs _drugs;
        private readonly Pricing _pricing;
        private readonly PlayerState _state;
        private readonly Affiliation _crew;

        /// <summary>Set by Main. Everything he sells lands here.</summary>
        public Stash House;

        public DealerTalk(Delivery delivery, Drugs drugs, Pricing pricing,
                          PlayerState state, Affiliation crew)
        {
            _delivery = delivery;
            _drugs = drugs;
            _pricing = pricing;
            _state = state;
            _crew = crew;
        }

        private string Name => _delivery.Def == null ? "Juan" : _delivery.Def.Name;

        private float Multiplier => _delivery.Def == null ? 0.75f : _delivery.Def.PriceMultiplier;

        private DialogueNode Node(string line) =>
            new DialogueNode(Name, line) { SpeakerColour = Palette.Cash };

        public DialogueNode Root()
        {
            if (House == null) return Node("Come back when you got somewhere to put it.");

            var node = Node("Ain't got time to stand here. What you taking?");

            foreach (var brick in Bricks)
            {
                var product = _drugs.Get(brick.DrugId);
                if (product == null) continue;

                var pick = brick;
                var cost = Cost(product, brick.Grams, 1);

                node.Say(product.Name + ".", () => Amounts(pick, product),
                         "from $" + cost.ToString("N0"));

                node.WithIcon(Icons.ForDrug(product.Id));
            }

            node.Leave("Not today.");
            return node;
        }

        /// <summary>How many bricks, once you have said what.</summary>
        private DialogueNode Amounts(Brick brick, DrugDef product)
        {
            var node = Node("How many? And don't say one if you mean four.");

            foreach (var lot in Lots)
            {
                var count = lot;
                var grams = brick.Grams * lot;
                var cost = Cost(product, brick.Grams, lot);

                var canPay = Game.Player.Money >= cost;
                var fits = House.FreeSpace >= grams - 0.5f;

                var blocked = !canPay ? "You're $" + (cost - Game.Player.Money).ToString("N0") + " short"
                            : !fits ? "The house won't hold that"
                            : "";

                node.SayIf(blocked.Length == 0, blocked,
                           Weight(brick, count),
                           () => Buy(product, grams, cost),
                           "$" + cost.ToString("N0"));

                node.WithIcon(Icons.ForDrug(product.Id));
            }

            node.Say("Something else.", Root);
            node.Leave("Forget it.");
            return node;
        }

        private static string Weight(Brick brick, int lot)
        {
            if (lot == 1) return brick.Label;

            var kilos = brick.Grams * lot / 1000f;
            return lot + "x  --  " + kilos.ToString("0.#") + " kilos";
        }

        /// <summary>
        /// What it costs, rounded to something a man would actually say out loud.
        ///
        /// Never below the floor: if the maths came out at forty-eight thousand he would round
        /// it up rather than break his own rule, and so does this.
        /// </summary>
        private int Cost(DrugDef product, float gramsPerBrick, int lot)
        {
            var raw = _pricing.WholesalePrice(product, Multiplier) * gramsPerBrick * lot;

            var rounded = (int)(Math.Round(raw / 500.0) * 500);
            return Math.Max(Floor * lot, rounded);
        }

        private DialogueNode Buy(DrugDef product, float grams, int cost)
        {
            var taken = House.AddBulk(product.Id, grams);

            if (taken <= 0.5f)
            {
                return Node("You got nowhere to put it. Sort that out first.");
            }

            var charged = (int)Math.Round(cost * (taken / grams));

            Game.Player.Money -= charged;
            _state.Touch();

            if (_crew != null) _crew.CreditPurchase();

            Notify.Important("~y~-$" + charged.ToString("N0") + "~s~  " +
                             (taken / 1000f).ToString("0.#") + " kilos of " +
                             product.Name.ToLowerInvariant() + " at the house");

            Log.Info("Bought " + taken.ToString("0") + "g " + product.Id + " off " + Name +
                     " for $" + charged + ".");

            var node = Node("It's at the house. Don't call me again this week.");

            node.Say("Anything else.", Root);
            node.Leave("We good.");
            return node;
        }
    }
}
