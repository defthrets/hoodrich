using System.Collections.Generic;

namespace Hoodrich.UI
{
    /// <summary>
    /// What the mod is, the first time it runs.
    ///
    /// Written for somebody who has never seen any of it, and ordered by when they need to
    /// know each thing rather than by how the mod is built. That is the whole difference
    /// between this version and the one before it: the old one opened by telling you which
    /// gang you were in before you had joined one, sent you to a kitchen belonging to a woman
    /// it introduced four screens later, and said "this is the step people miss" -- a sentence
    /// that only makes sense to somebody who has already missed it.
    ///
    /// So it opens with the one thing to actually go and do, gets the reader through a first
    /// sale, and only then becomes reference. Nothing is named before it is explained.
    ///
    /// What earns a row is what a player CANNOT work out by pressing things. Weight bought by
    /// the brick will not sell until it has been cut. Heat sticks to the ground rather than to
    /// you. A corner sells at a rate set by how busy the pavement is. None of that is
    /// guessable, and a player who does not know it buys a kilo, walks to a corner, and
    /// concludes the mod is broken.
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

            // ---- the first five minutes ----------------------------------------
            var start = new InfoSection { Title = "Start here" };

            start.Row("Go and see Gerald", "The flats on Grove", Palette.Cash,
                      "He is the only marker on your map, and that is deliberate. He puts the "
                      + "first lot of product in your hand for nothing, so you do not need "
                      + "money to get going",
                      r => r.ArtFile = "pills.png");

            start.Row("Open the phone", "The phone button", null,
                      "Everything in here lives on it. Arrows move, Enter opens, Backspace goes "
                      + "back a screen. There is nothing else to learn about the controls",
                      r => r.ArtFile = "phone.png");

            start.Row("Sell what he gave you", "Dealing > Post up", null,
                      "Walk somewhere with people about, open the phone and post up. Buyers come "
                      + "to you. When it is all gone he will text you to come back",
                      r => r.ArtFile = "footfall.png");

            sections.Add(start);

            // ---- the loop ------------------------------------------------------
            var loop = new InfoSection { Title = "The job, in order" };

            loop.Row("1. Get hold of some", "Gangs > your leader", Palette.Cash,
                     "Gerald hands you the first two lots free. After that you buy it -- from "
                     + "him, or from the man at the port once you have met him",
                     r => r.ArtFile = "crate.png");

            loop.Row("2. Bag it up", "The kitchen at Denise's", Palette.Warn,
                     "Anything you BUY comes as weight and will not sell as it is. It has to go "
                     + "to the sink first. Anything you are GIVEN is already bagged",
                     r => r.ArtFile = "scales.png");

            loop.Row("3. Stand somewhere", "Dealing > Post up", null,
                     "You choose the SPOT, not who to sell to. A busy pavement sells fast and an "
                     + "empty street sells nothing, however good the gear is",
                     r => r.ArtFile = "pin.png");

            loop.Row("4. Do not still be there", "Watch your heat", Palette.Warn,
                     "Every sale gets you noticed a bit more. Stay on one corner long enough and "
                     + "somebody makes a phone call",
                     r => r.ArtFile = "fire.png");

            sections.Add(loop);

            // ---- the sink ------------------------------------------------------
            var sink = new InfoSection { Title = "The kitchen" };

            sink.Row("Weight will not sell", "Bricks and bags are not product", Palette.Warn,
                     "You cannot hand somebody a brick on a street corner. It has to be worked "
                     + "and bagged before anybody will take it",
                     r => r.ArtFile = "brick.png");

            sink.Row("Use the sink at Denise's", "Walk up to the counter", null,
                     "It is in the kitchen at her place. Standing at it offers you the job",
                     r => r.ArtFile = "stash.png");

            sink.Row("Decide how far to stretch it", "100% down to 25%", null,
                     "Cut to half and you come out with twice as much. Cut to a quarter and you "
                     + "have four times as much, and it is obviously rubbish",
                     r => r.ArtFile = "cut_50.png");

            sink.Row("What comes out is ready", "Bagged and sellable", Palette.Cash,
                     "That is what a corner takes. It is the only thing a corner takes",
                     r => r.ArtFile = "baggie.png");

            sections.Add(sink);

            // ---- purity --------------------------------------------------------
            var cut = new InfoSection { Title = "Why cutting is the whole game" };

            cut.Row("Cutting makes more of it", "100g at 50% = 200g", Palette.Cash,
                    "Same money in, twice as much to sell. There is a reason everybody does it",
                    r => r.ArtFile = "cut_50.png");

            cut.Row("People can tell", "Weak gear gets refused", Palette.Danger,
                    "A refusal is not just one lost sale. They remember it, they tell people, "
                    + "and some of them want to do something about it",
                    r => r.ArtFile = "warning.png");

            cut.Row("Your name is a number", "It moves on every sale", null,
                    "Sell decent stuff for a while and it climbs. Step on everything and it does "
                    + "not. That trade is the game",
                    r => r.ArtFile = "rank.png");

            sections.Add(cut);

            // ---- standing on a corner ------------------------------------------
            var corner = new InfoSection { Title = "On the corner" };

            corner.Row("You can wander", "About forty metres", null,
                       "Cross the road, step round the back of the block -- the pitch holds. Go "
                       + "further than that and you have packed up",
                       r => r.ArtFile = "pin.png");

            corner.Row("They ask for an amount", "A gram, an eighth, an ounce", null,
                       "Different people want different amounts, and what one block pays is not "
                       + "what the next one pays",
                       r => r.ArtFile = "deal.png");

            corner.Row("Heat sticks to the place", "Not to you", Palette.Warn,
                       "Come back to the same corner and you pick up where you left it. A "
                       + "different block is a clean start, and a quiet corner cools off",
                       r => r.ArtFile = "fire.png");

            corner.Row("The block hears about it", "Socials", null,
                       "Posting up puts the word out, in the sort of language that does not say "
                       + "anything outright. That is how anybody knows to come",
                       r => r.ArtFile = "socials.png");

            sections.Add(corner);

            // ---- the police ----------------------------------------------------
            var law = new InfoSection { Title = "The police" };

            law.Row("They drive your block anyway", "This is not a wanted level", null,
                    "A car comes past, has a look, moves on. It happens whether or not anybody "
                    + "is after you",
                    r => r.ArtFile = "police.png");

            law.Row("A hot corner draws them", "They come to the noise", Palette.Warn,
                    "Work one spot long enough and they stop driving past and start stopping",
                    r => r.ArtFile = "warning.png");

            law.Row("It is what you are carrying", "They search, they do not arrest", null,
                    "Get searched holding product and they take it. Hold nothing and it is only "
                    + "a fine -- which is why getting rid of a bag before they reach you works",
                    r => r.ArtFile = "stash.png");

            law.Row("You can tell them what you think", "Right on the d-pad", null,
                    "Stood near a patrol car with no wanted level, put a finger up at them. "
                    + "Nothing they can do you for, and they know it",
                    r => r.ArtFile = "megaphone.png");

            sections.Add(law);

            // ---- the people ----------------------------------------------------
            var who = new InfoSection { Title = "Who is who" };

            who.Row("Gerald", "The flats on Grove", Palette.Cash,
                    "Where you begin. He gives you a package, you move all of it, you bring him "
                    + "the money. Do that twice and he tells you where it comes from. He deals "
                    + "PILLS -- bars first, then rolls",
                    r => r.ArtFile = "pills.png");

            who.Row("Lamar", "Forum Drive, once you are in", null,
                    "The work: rides, hits, drive-bys, tagging. He opens jobs up as your name "
                    + "gets bigger, and he runs the weed side rather than pills",
                    r => r.ArtFile = "car.png");

            who.Row("Tao Cheng", "Elysian Island", null,
                    "The port, and the reason Gerald's two packages matter. He sells by the "
                    + "brick, and once you know him he drives it to wherever you are standing",
                    r => r.ArtFile = "brick.png");

            who.Row("Hao", "The lot in Little Seoul", null,
                    "Cars, all of them on competition suspension. Anything you buy off him is "
                    + "yours and stays where you leave it",
                    r => r.ArtFile = "car.png");

            who.Row("Stretch", "The rack", null,
                    "Guns, with extended mags on everything he sells",
                    r => r.ArtFile = "ammo.png");

            who.Row("Everybody else", "Gangs", null,
                    "Eight more sets, each with a leader standing somewhere, each hidden on your "
                    + "map until you have actually met him. Standing is tracked per gang",
                    r => r.ArtFile = "people.png");

            sections.Add(who);

            // ---- the rest of the phone ------------------------------------------
            var more = new InfoSection { Title = "The rest of the phone" };

            more.Row("Dealing", "Buy, bag, post up", null,
                     "The product side of it, and where the corner is started and stopped",
                     r => r.ArtFile = "baggie.png");

            more.Row("Gangs", "Who you run with", null,
                     "Your leader, your standing, and the eight sets you do not run with",
                     r => r.ArtFile = "people.png");

            more.Row("Socials", "What the block is saying", null,
                     "It reacts to what you actually do, and you can say something back",
                     r => r.ArtFile = "reply.png");

            more.Row("Settings", "Everything is adjustable", null,
                     "Difficulty, prices, what shows on screen -- and a switch to start the "
                     + "whole thing over if you want a clean run",
                     r => r.ArtFile = "scales.png");

            sections.Add(more);

            // ---- the short version ----------------------------------------------
            var last = new InfoSection { Title = "If you remember three things" };

            last.Row("Go to Gerald first", "The flats on Grove", Palette.Cash,
                     "He is on your map now. Everything else opens up behind him",
                     r => r.ArtFile = "pills.png");

            last.Row("Bought weight has to be bagged", "Before it will sell", Palette.Warn,
                     "The sink at Denise's. This is the one that catches people out",
                     r => r.ArtFile = "warning.png");

            last.Row("Do not get comfortable", "Move corners", null,
                     "The money is in selling out and being gone before anybody makes it their "
                     + "business",
                     r => r.ArtFile = "tick.png");

            sections.Add(last);

            return sections;
        }
    }
}
