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
                     "Buy weight, cut it at home, stand somewhere and sell it. Be gone before "
                     + "anybody makes it their business",
                     r => r.ArtFile = "gang_families.png");

            what.Row("It all lives on your phone", "The phone button", null,
                     "Arrows move, Enter opens, Backspace backs out. That is the whole of it",
                     r => r.ArtFile = "phone.png");

            what.Row("Your weapon wheel still works", "Nothing was taken off it", null,
                     "We put all this on the phone instead, so pulling a gun costs you nothing",
                     r => r.ArtFile = "guns.png");

            sections.Add(what);

            // ---- the loop ------------------------------------------------------
            var loop = new InfoSection { Title = "The whole job, in order" };

            loop.Row("1. Get hold of some", "Gangs > your leader", Palette.Cash,
                     "Gerald puts the first bag in your hand for nothing. Later you ring the "
                     + "man at the port and he drives it over",
                     r => r.ArtFile = "crate.png");

            loop.Row("2. Cut it in the kitchen", "Aunt Denise's", null,
                     "BULK WEIGHT CANNOT BE SOLD. It has to be worked first -- this is the step "
                     + "people miss",
                     r => r.ArtFile = "scales.png");

            loop.Row("3. Go and stand somewhere", "Dealing > Post up", null,
                     "You pick the SPOT, not the customer. They come to you",
                     r => r.ArtFile = "footfall.png");

            loop.Row("4. Do not still be there", "Watch your heat", Palette.Warn,
                     "Stand on one corner long enough and somebody makes a phone call",
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
                    "Sell decent stuff for a while and it comes back up. Step on everything and "
                    + "it does not. That trade is the game",
                    r => r.ArtFile = "rank.png");

            sections.Add(cut);

            // ---- standing on a corner ------------------------------------------
            var corner = new InfoSection { Title = "Standing on a corner" };

            corner.Row("You pick the place, not the buyer", "Dealing > Post up", null,
                       "Stand somewhere with people on it and they come to you. An empty street "
                       + "sells nothing, however good the product is",
                       r => r.ArtFile = "footfall.png");

            corner.Row("You can move about", "Forty metres", null,
                       "Cross the road, step round the back -- the pitch holds. Walk off the "
                       + "block and you have stopped working it",
                       r => r.ArtFile = "pin.png");

            corner.Row("They ask, you answer", "A gram, an eighth, an ounce", null,
                       "Different buyers want different amounts, and what a block pays is not "
                       + "what the next one pays",
                       r => r.ArtFile = "deal.png");

            corner.Row("Every sale is noticed", "Heat builds where you stand", Palette.Warn,
                       "It sticks to the GROUND, not to you. Come back to the same corner and "
                       + "you pick up what you left there -- a different block is a clean start",
                       r => r.ArtFile = "fire.png");

            sections.Add(corner);

            // ---- the police ----------------------------------------------------
            var law = new InfoSection { Title = "The patrol" };

            law.Row("They roll the block you work", "Not a wanted level", null,
                    "A car comes past, sits, and moves on. This is separate from stars -- it "
                    + "happens whether or not anybody is looking for you",
                    r => r.ArtFile = "police.png");

            law.Row("Hot corner, more of them", "They come to the heat", Palette.Warn,
                    "Work one spot long enough and they stop rolling past and start stopping",
                    r => r.ArtFile = "warning.png");

            law.Row("Carrying is the risk, not dealing", "They search, they do not arrest", null,
                    "Get searched holding product and they take it. Hold nothing and they fine "
                    + "you instead -- which is why dropping a bag before they reach you works",
                    r => r.ArtFile = "stash.png");

            law.Row("Your guns are yours", "They leave the wheel alone", null,
                    "A search costs you product and money. It has never cost you a weapon",
                    r => r.ArtFile = "guns.png");

            sections.Add(law);

            // ---- the people ----------------------------------------------------
            var who = new InfoSection { Title = "Who everybody is" };

            who.Row("Gerald", "The flats on Grove", Palette.Cash,
                    "START HERE. He fronts you a package, you move all of it, you bring him back "
                    + "the money. Do that twice and he puts you on to where it comes from. He "
                    + "deals PILLS -- bars first, oxys second",
                    r => r.ArtFile = "pills.png");

            who.Row("Lamar", "Forum Drive, once you are in", null,
                    "The work. Rides, hits, drive-bys and tagging, gated on your rank. He runs "
                    + "the weed side of the block, so he is not who you go to for pills",
                    r => r.ArtFile = "car.png");

            who.Row("Tao Cheng", "Elysian Island", null,
                    "The port, and the reason the two packages matter. Bricks only -- and once "
                    + "you know him he drives it to wherever you are standing",
                    r => r.ArtFile = "brick.png");

            who.Row("Hao", "The lot in Little Seoul", null,
                    "Cars. Eleven of them for sale, all on competition suspension, and the one "
                    + "you deliver to him after a job gets new numbers and sold on",
                    r => r.ArtFile = "car.png");

            who.Row("Stretch", "The rack", null,
                    "Guns, and extended mags on everything he sells",
                    r => r.ArtFile = "ammo.png");

            who.Row("Aunt Denise", "The kitchen at hers", null,
                    "Not a person you talk to -- a sink you use. See the step below",
                    r => r.ArtFile = "scales.png");

            who.Row("Eight other sets", "Gangs", null,
                    "Each has a leader standing somewhere, and each is hidden on the map until "
                    + "you have actually met him",
                    r => r.ArtFile = "people.png");

            sections.Add(who);

            // ---- the sink ------------------------------------------------------
            var sink = new InfoSection { Title = "The kitchen, step by step" };

            sink.Row("Bulk is not sellable", "This is the step people miss", Palette.Warn,
                     "Weight bought by the brick cannot be handed to anybody. It has to be "
                     + "worked first",
                     r => r.ArtFile = "brick.png");

            sink.Row("Go to the sink", "Aunt Denise's kitchen", null,
                     "The counter in there is where it happens. Walk up to it and it offers",
                     r => r.ArtFile = "stash.png");

            sink.Row("Choose how hard to step on it", "100% down to 25%", null,
                     "Cut to half and you have twice as much. Cut to a quarter and you have four "
                     + "times as much, and it is obviously rubbish",
                     r => r.ArtFile = "cut_50.png");

            sink.Row("What comes out is bagged", "Ready to sell", Palette.Cash,
                     "Packaged product is what a corner takes. Anything Gerald FRONTS you is "
                     + "already bagged and skips this entirely",
                     r => r.ArtFile = "baggie.png");

            sections.Add(sink);

            // ---- the rest ------------------------------------------------------
            // ---- the rest ------------------------------------------------------
            var more = new InfoSection { Title = "What else is out there" };

            more.Row("Nine sets, and standing with each", "Gangs", null,
                     "Rep, bodies, money and beef are tracked per gang, not as one number",
                     r => r.ArtFile = "people.png");

            more.Row("The block talks about you", "Socials", null,
                     "It reacts to what you actually do. You can post back, and name a set",
                     r => r.ArtFile = "reply.png");

            more.Row("Everything reads somewhere", "The numbers", null,
                     "Prices, heat, what a block pays. No screen here shows you a bare statistic",
                     r => r.ArtFile = "cash.png");

            sections.Add(more);

            // ---- how to get out of it ------------------------------------------
            var last = new InfoSection { Title = "If you take one thing" };

            last.Row("Go and see Gerald", "The flats on Grove", Palette.Cash,
                     "He is the only thing on your map right now and that is on purpose. He "
                     + "fronts you the first package -- you do not need money to start",
                     r => r.ArtFile = "pills.png");

            last.Row("Weight is not product", "Cut it first", Palette.Warn,
                     "Anything you BUY has to go to the kitchen before it will sell. Anything "
                     + "you are FRONTED is already bagged",
                     r => r.ArtFile = "warning.png");

            last.Row("You will not see this again", "It is all on the phone", null,
                     "Backspace closes it. Settings has a switch to start the whole thing over "
                     + "if you ever want this back",
                     r => r.ArtFile = "tick.png");

            sections.Add(last);

            return sections;
        }
    }
}
