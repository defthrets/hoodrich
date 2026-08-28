namespace Hoodrich.Paint
{
    /// <summary>
    /// A nozzle for the can.
    ///
    /// WHAT A CAP ACTUALLY CHANGES is the narrowest line the can is capable of, not the widest.
    /// A fat cap's defining property is that you CANNOT do fine work with it however close you
    /// hold it -- the paint leaves the nozzle already wide. The far end is governed by how far
    /// the wall is, and standing back with a thin cap gets you a wide band too.
    ///
    /// So a cap is one number: a multiplier on the floor. The ceiling is shared and stays put.
    /// </summary>
    internal sealed class Cap
    {
        public readonly string Name;

        /// <summary>Multiplies CanMinSize. 1 is the can as it has always been.</summary>
        public readonly float Width;

        public Cap(string name, float width)
        {
            Name = name;
            Width = width;
        }

        /// <summary>
        /// The art for it, in data\icons.
        ///
        /// Built here rather than in each front end, because there are two of those and a
        /// filename assembled by hand in both is a filename that gets renamed in one.
        /// </summary>
        public string Icon { get { return "cap_" + Name + ".png"; } }
    }

    /// <summary>
    /// The three, narrowest first.
    ///
    /// Multipliers rather than absolute sizes, so CanMinSize stays the one place that says how
    /// fine this can gets and the caps stay relative to it. Tune the can and all three move
    /// together, which is the point of tuning the can.
    ///
    /// Doubling each step, which is about right: the gap between a skinny and a stock is the
    /// same kind of gap as between a stock and a fat.
    ///
    /// THE WHOLE LADDER MOVED UP ONE. What used to be the stock cap is the thin one now, what
    /// used to be fat is the stock, and the fat cap is new and twice the old fat again. That
    /// was done by raising CanMinSize rather than by rewriting these three numbers, so the
    /// ratios stay a plain doubling and there is still exactly one place saying how fine this
    /// can gets.
    /// </summary>
    internal static class Caps
    {
        public static readonly Cap[] All =
        {
            new Cap("thin",  1.0f),
            new Cap("stock", 2.0f),
            new Cap("fat",   4.0f)
        };

        /// <summary>Whichever one that index means, with anything out of range treated as thin.</summary>
        public static Cap At(int i)
        {
            return i >= 0 && i < All.Length ? All[i] : All[0];
        }
    }
}
