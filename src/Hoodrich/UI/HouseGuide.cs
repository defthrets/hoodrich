using System.Collections.Generic;

namespace Hoodrich.UI
{
    /// <summary>
    /// The house, explained once: what the three things in it are for.
    ///
    /// Put up by the readout the first time you walk through Denise's door -- see
    /// StashHouse.Entered -- because the kitchen, the bed and the closet are the three
    /// things in this mod that nobody works out on their own. The kitchen especially: weight
    /// that has not been through it will not sell, and nothing in the house says so except a
    /// ticker that has been and gone by the time anybody reads it. Same shape as Welcome, so
    /// it reads as the same book.
    /// </summary>
    internal static class HouseGuide
    {
        public static List<InfoSection> Pages()
        {
            var sections = new List<InfoSection>();

            // ---- the kitchen ----------------------------------------------------
            var kitchen = new InfoSection { Title = "The kitchen" };

            kitchen.Row("Cut it and bag it", "Stand at the sink", Palette.Warn,
                        "Weight you BUY will not sell as it is. The counter in the kitchen offers "
                        + "you the work. How hard you step on it is the whole decision: 100g at "
                        + "half strength is 200g to sell, and the block can tell",
                        r => r.ArtFile = "scales.png");

            kitchen.Row("Given is already bagged", "Straight to the corner", null,
                        "Anything Gerald or a plug hands you is ready to go. Only weight -- bricks "
                        + "and bags -- comes through here",
                        r => r.ArtFile = "brick.png");

            sections.Add(kitchen);

            // ---- the bed --------------------------------------------------------
            var bed = new InfoSection { Title = "The bed" };

            bed.Row("Sleep, and it saves", "Franklin's old room", Palette.Cash,
                    "Six hours pass and everything is written down: product, cash, cars, who you "
                    + "have met. Plugs keep hours and a burned corner cools off on the clock, so "
                    + "the bed is how you move the clock as much as how you save",
                    r => r.ArtFile = "bed.png");

            sections.Add(bed);

            // ---- the closet -----------------------------------------------------
            var closet = new InfoSection { Title = "The closet" };

            closet.Row("Change at the wardrobe", "Same room, by the door", null,
                       "Six outfits, plus his build, how he walks and how he holds a gun. What "
                       + "you settle on goes in the save and is put back on him when the game "
                       + "loads",
                       r => r.ArtFile = "cap.png");

            sections.Add(closet);

            // ---- the stash ------------------------------------------------------
            var stash = new InfoSection { Title = "The stash" };

            stash.Row("Product lives here", "Phone > Inventory", null,
                      "Move work in and out from the inventory when you are stood in the house, "
                      + "or text a plug from your contacts and have it brought here. Hold too much "
                      + "and somebody comes for it",
                      r => r.ArtFile = "phone.png");

            sections.Add(stash);

            return sections;
        }
    }
}
