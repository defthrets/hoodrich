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

        public Brick(string drugId, float grams)
        {
            DrugId = drugId;
            Grams = grams;
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
            var def = _delivery == null ? null : _delivery.Def;
            var list = new List<Brick>();

            // What HE carries, which is a thing the dealer data has always said and this menu
            // has never once read. Every dealer was offered the whole catalogue -- so the man
            // whose own buy line is "I ain't the port, don't ask me for no bricks" was stood
            // there selling twelve kilos of weed, cocaine and heroin.
            //
            // An empty list still means everything, which is how the port is described.
            foreach (var d in _drugs.All)
            {
                if (d.MadeOnly) continue;
                if (def != null && def.Drugs.Count > 0 && !Sells(def, d.Id)) continue;

                list.Add(new Brick(d.Id, LotOf(def, d)));
            }

            return list.ToArray();
        }

        private static bool Sells(DealerDef def, string drugId)
        {
            for (var i = 0; i < def.Drugs.Count; i++)
            {
                if (string.Equals(def.Drugs[i], drugId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One lot of this off this man, in grams.
        ///
        /// Sized from what he deals in rather than from a constant, and rounded to his own
        /// step -- half a kilo for the port, five grams for somebody on a pushbike -- so the
        /// number is always one a person would actually say out loud.
        /// </summary>
        private static float LotOf(DealerDef def, DrugDef drug)
        {
            var value = def == null ? BrickValue : def.LotValue;
            var step = def == null ? 500f : def.LotStep;
            if (step < 1f) step = 1f;

            var grams = (float)Math.Round(value / Math.Max(1f, drug.BulkPrice) / step) * step;
            return grams < step ? step : grams;
        }

        /// <summary>Roughly what one load off him is worth, before his own multiplier.</summary>
        private const float BrickValue = 60000f;

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

                // The product's own words for the amount -- "40 pills", "112g" -- rather than
                // a kilo count. Kilos are true of the port and nonsense off a bicycle, where
                // the same routine was rendering an ounce as "0.1 of a kilo".
                node.Say(product.Name + ".", () => Amounts(pick, product),
                         product.Amount(brick.Grams) + " from $" + cost.ToString("N0"));

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
                           Weight(product, brick, count),
                           () => Buy(product, grams, cost),
                           "$" + cost.ToString("N0"));

                node.WithIcon(Icons.ForDrug(product.Id));
            }

            node.Say("Something else.", Root);
            node.Leave("Forget it.");
            return node;
        }

        /// <summary>
        /// One line of the how-many list, in the product's own units.
        ///
        /// This used to read a hand-written phrase off the brick for a single lot and print
        /// KILOS for anything more -- both of which assume the man you are stood in front of
        /// deals in bricks. Off a pushbike that rendered sixty grams of weed as "0.1 kilos",
        /// and the single-lot line as nothing at all once the phrases went.
        /// </summary>
        private static string Weight(DrugDef product, Brick brick, int lot)
        {
            var amount = product.Amount(brick.Grams * lot);
            return lot == 1 ? amount : lot + "x  --  " + amount;
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
