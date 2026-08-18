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

        /// <summary>
        /// male, female, or none for an organisation.
        ///
        /// Only used to pick which of the game's phone-contact pictures turns up on the
        /// notification. A woman's post arriving with a man's face on it is the kind of small
        /// wrongness that makes a whole system read as generated.
        /// </summary>
        public string Gender = "male";

        /// <summary>
        /// An explicit contact picture, for people the game already has a face for.
        ///
        /// Everybody else draws from the pool by gender. The named cast do not, because Trevor
        /// turning up wearing somebody else's face is worse than any amount of variety is worth.
        /// </summary>
        public string Pic = "";

        /// <summary>
        /// Key into the voice table, for characters who write their own lines.
        ///
        /// This is the whole difference between a name on a post and a character. A generic
        /// template can be handed to any of seventy people; a Trevor line cannot be handed to
        /// Lester, and if it can then it was never really a Trevor line.
        /// </summary>
        public string Voice = "";

        public bool HasVoice => !string.IsNullOrEmpty(Voice);

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
