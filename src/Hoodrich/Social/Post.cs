using System.Drawing;

namespace Hoodrich.Social
{
    /// <summary>Who a post belongs to.</summary>
    internal sealed class Author
    {
        public string Handle = "";
        public string Name = "";

        /// <summary>Which set they run with, or empty for somebody with no dog in it.</summary>
        public string Gang = "";

        /// <summary>The blue tick. Rare on purpose -- almost nobody on this block has one.</summary>
        public bool Verified;

        /// <summary>Avatar colour, derived once from the handle so it never changes on them.</summary>
        public Color Tint = Color.FromArgb(255, 90, 96, 92);

        /// <summary>The letter in the avatar disc.</summary>
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
    }

    /// <summary>One thing somebody said.</summary>
    internal sealed class Post
    {
        public Author By;
        public string Body = "";

        /// <summary>Game time it landed, for the "2m" stamp.</summary>
        public int At;

        public int Likes;
        public int Reposts;
        public int Replies;

        /// <summary>
        /// True when it is about the player.
        ///
        /// Worth marking rather than inferring: a post about you is the whole point of the
        /// system, and the timeline gives it a rail down the left so you can find it while
        /// scrolling past everything else.
        /// </summary>
        public bool AboutYou;
    }
}
