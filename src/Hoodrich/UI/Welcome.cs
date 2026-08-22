using System.Collections.Generic;

namespace Hoodrich.UI
{
    /// <summary>
    /// What the mod is, the first time it runs.
    ///
    /// There is a lot in here that is not discoverable by pressing things. Weight cannot be
    /// sold until it has been cut, purity is the thing the whole economy turns on, the wheel
    /// is on a button that already does something else, and a corner sells at a rate set by
    /// how busy the pavement is. None of that is guessable, and a player who does not know it
    /// buys a kilo, walks to a corner, and concludes the mod is broken.
    ///
    /// Built out of the same InfoPanel every readout in the mod uses, so it scrolls, it takes
    /// the icons, and there is no second screen to maintain. Written as label / value / note:
    /// the thing, the short answer, and then the sentence that explains it.
    /// </summary>
    internal static class Welcome
    {
        public static List<InfoSection> Pages()
        {
            var sections = new List<InfoSection>();

            // ---- what it is ----------------------------------------------------
            var what = new InfoSection { Title = "What this is" };

            what.Row("You run with the Families", "Chamberlain Hills", Palette.Cash,
                     "Buy weight, cut it at home, sell it on a corner, try to be gone first",
                     r => r.ArtFile = "gang_families.png");

            what.Row("Everything is on one button", "The weapon wheel", null,
                     "HOLD it for Hoodrich. TAP it and you still holster, same as always",
                     r => r.ArtFile = "mask.png");

            what.Row("Weapons hands it back", "Hold, pick Weapons", null,
                     "You get the real weapon wheel for a few seconds, selection and all",
                     r => r.ArtFile = "guns.png");

            sections.Add(what);

            // ---- the loop ------------------------------------------------------
            var loop = new InfoSection { Title = "The whole job, in order" };

            loop.Row("1. Buy weight", "Gangs > your leader", Palette.Cash,
                     "Stretch fronts you a bag to start. Later, text the plug at the port",
                     r => r.ArtFile = "crate.png");

            loop.Row("2. Cut it in the kitchen", "Aunt Denise's", null,
                     "BULK WEIGHT CANNOT BE SOLD. It has to be worked first -- this is the step "
                     + "people miss",
                     r => r.ArtFile = "scales.png");

            loop.Row("3. Post up on a corner", "Dealing > Post up", null,
                     "You pick a SPOT, not a customer. Buyers come to you",
                     r => r.ArtFile = "footfall.png");

            loop.Row("4. Leave before they notice", "Watch your heat", Palette.Warn,
                     "Stand too long and you get clocked, and then it is a police matter",
                     r => r.ArtFile = "police.png");

            sections.Add(loop);

            // ---- the bit that is not obvious -----------------------------------
            var cut = new InfoSection { Title = "Purity is the whole economy" };

            cut.Row("Cutting multiplies it", "100g at 50% = 200g", Palette.Cash,
                    "Step on it and you have twice as much to sell",
                    r => r.ArtFile = "coke.png");

            cut.Row("And people notice", "Weak product gets refused", Palette.Danger,
                    "A refusal is not one lost sale -- the block remembers, and starts saying so",
                    r => r.ArtFile = "megaphone.png");

            cut.Row("Your name is a number", "It moves every sale", null,
                    "Good work earns it back. That is the whole trade-off, and it is yours to make",
                    r => r.ArtFile = "rank.png");

            sections.Add(cut);

            // ---- the rest ------------------------------------------------------
            var more = new InfoSection { Title = "What else is out there" };

            more.Row("Nine sets, and standing with each", "Gangs", null,
                     "Rep, bodies, money and beef are tracked per gang, not as one number",
                     r => r.ArtFile = "people.png");

            more.Row("Work from Lamar", "Find him on Forum Drive", null,
                     "Six jobs, gated on rank. He will tell you what he wants",
                     r => r.ArtFile = "car.png");

            more.Row("The block talks about you", "Socials", null,
                     "It reacts to what you actually do. You can post back, and name a set",
                     r => r.ArtFile = "reply.png");

            more.Row("Everything reads somewhere", "The numbers", null,
                     "Prices, heat, what a block pays. The wheel never shows you a statistic",
                     r => r.ArtFile = "cash.png");

            sections.Add(more);

            // ---- how to get out of it ------------------------------------------
            var last = new InfoSection { Title = "If you take one thing" };

            last.Row("Weight is not product", "Cut it first", Palette.Warn,
                     "Buy it, take it to the kitchen, then go and stand somewhere",
                     r => r.ArtFile = "warning.png");

            last.Row("This screen will not come back", "Everything is in the wheel", null,
                     "Backspace or the back button closes it",
                     r => r.ArtFile = "tick.png");

            sections.Add(last);

            return sections;
        }
    }
}
