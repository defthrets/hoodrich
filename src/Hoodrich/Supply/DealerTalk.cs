using System;
using System.Collections.Generic;
using Color = System.Drawing.Color;
using GTA;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Supply
{
    /// <summary>One thing Tao Cheng will sell you, in the quantity he sells it in.</summary>
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
    /// Buying weight off Tao Cheng.
    ///
    /// He does not do grams and he does not do ounces. Everything on this list is a brick or
    /// several, and nothing on it comes to less than fifty thousand dollars -- that is the whole
    /// point of him. Stretch sells you enough to work a corner tonight; Tao Cheng sells you enough
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
        /// <summary>
        /// The six that have a load written for them by hand.
        ///
        /// Anything else in the catalogue gets one worked out below rather than being left
        /// off the menu. This list used to BE the menu, so a product added to drugs.json
        /// simply could not be bought off him -- Alprazolam went in and he carried on offering
        /// the same six he was written with.
        /// </summary>
        private static readonly Brick[] WrittenBricks =
        {
            new Brick("weed",    12000f, "Twelve kilos of weed"),
            new Brick("ecstasy",  9000f, "Nine kilos of pills"),
            new Brick("meth",     4000f, "Four kilos of meth"),
            new Brick("crack",    2500f, "Two and a half of crack"),
            new Brick("coke",     1500f, "A kilo and a half of coke"),
            new Brick("heroin",   3000f, "Three kilos of heroin"),
        };

        /// <summary>
        /// What he is holding today: the written six, then everything else in the catalogue.
        ///
        /// A load is sized by what it is WORTH rather than by weight, because the six that
        /// were written by hand already work that way -- they come to somewhere between forty
        /// and eighty thousand a brick whatever the product is, which is what makes them read
        /// as one man's van rather than as six unrelated numbers. Rounded to the nearest half
        /// kilo so the figure still sounds like something a person would say.
        ///
        /// Made-only products are skipped. Nobody buys rolled joints off a container.
        /// </summary>
        private Brick[] StockToday()
        {
            var list = new List<Brick>();

            foreach (var b in WrittenBricks)
            {
                if (_drugs.Get(b.DrugId) != null) list.Add(b);
            }

            foreach (var d in _drugs.All)
            {
                if (d.MadeOnly) continue;
                if (list.Exists(b => string.Equals(b.DrugId, d.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var grams = (float)Math.Round(BrickValue / Math.Max(1f, d.BulkPrice) / 500f) * 500f;
                if (grams < 500f) grams = 500f;

                list.Add(new Brick(d.Id, grams, Kilos(grams) + " of " + d.Name.ToLowerInvariant()));
            }

            return list.ToArray();
        }

        /// <summary>Roughly what one load off him is worth, before his own multiplier.</summary>
        private const float BrickValue = 60000f;

        /// <summary>A weight said the way he would say it, not printed to one decimal place.</summary>
        private static string Kilos(float grams)
        {
            var k = grams / 1000f;

            if (k < 1f) return (grams / 1000f).ToString("0.#") + " of a kilo";
            if (Math.Abs(k - Math.Round(k)) < 0.01f)
            {
                var whole = (int)Math.Round(k);
                return whole == 1 ? "A kilo" : whole + " kilos";
            }

            return k.ToString("0.#") + " kilos";
        }

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

        private string Name => _delivery.Def == null ? "Tao Cheng" : _delivery.Def.Name;

        private float Multiplier => _delivery.Def == null ? 0.75f : _delivery.Def.PriceMultiplier;

        private DialogueNode Node(string line) =>
            new DialogueNode(Name, line) { SpeakerColour = Palette.Cash };

        public DialogueNode Root()
        {
            if (House == null) return Node("Come back when you got somewhere to put it.");

            var node = Node("Ain't got time to stand here. What you taking?");

            foreach (var brick in StockToday())
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

            // Sending him off, which there was no way to do.
            //
            // "Not today" only closes the conversation -- he stays parked outside until
            // something else moves him. This is the other half: he gets in and drives away,
            // the same exit he takes once a delivery has landed.
            node.Say("That's you done. Go on.", () =>
            {
                _delivery?.Finish();
                return null;
            }, "He gets in and goes");

            node.WithIcon(Icons.FromFile("car.png"));
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

        /// <summary>
        /// Paying for it. Nothing arrives yet.
        ///
        /// The money leaves now and the goods land when the box is on the floor inside, because
        /// the whole reason he walks it in is that it has not been delivered until he has. It
        /// also means the only way to lose an order is to make it impossible for him to reach
        /// the door -- which is a thing you did, not a thing that happened to you.
        /// </summary>
        private DialogueNode Buy(DrugDef product, float grams, int cost)
        {
            if (House.FreeSpace < grams - 0.5f)
            {
                return Node("You got nowhere to put it. Sort that out first.");
            }

            Game.Player.Money -= cost;
            _state.Touch();

            if (_crew != null) _crew.CreditPurchase();

            _delivery.Deliver(product.Id, grams);

            Notify.Important("~y~-$" + cost.ToString("N0") + "~s~  " +
                             (grams / 1000f).ToString("0.#") + " kilos of " +
                             product.Name.ToLowerInvariant());

            Log.Info("Bought " + grams.ToString("0") + "g " + product.Id + " off " + Name +
                     " for $" + cost + "; he is walking it in.");

            var node = Node("Stand aside. I'll put it inside for you.");
            node.Leave("Go on then.");
            return node;
        }
    }
}
