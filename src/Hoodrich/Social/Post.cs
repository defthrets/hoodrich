using System;
using System.Drawing;
using GTA;

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

        /// <summary>
        /// The sentence on its own, without the sign-off.
        ///
        /// What gets remembered for the no-repeats check. The hashtags on the end are picked at
        /// random, so the same line signed two different ways looks like two different posts to
        /// anything comparing the finished text -- and then the feed says the same thing all
        /// evening while believing it never repeated itself.
        /// </summary>
        public string Plain = "";

        /// <summary>Game time it landed, for the "2m" stamp.</summary>
        public int At;

        /// <summary>Where the numbers END UP. What is shown is Grown, below.</summary>
        public int Likes;
        public int Reposts;
        public int Replies;

        /// <summary>
        /// The figures as they stand this second, rather than the ones it will settle at.
        ///
        /// A post used to arrive holding its final engagement: eight seconds old, ninety-four
        /// likes, and never another one for the rest of its life. Which is not what a feed
        /// looks like -- the thing that makes a timeline feel alive is that the numbers on it
        /// are DIFFERENT when you scroll back past them, and none of them ever were.
        ///
        /// Now they climb. The curve is fast at the start and slows, which is the shape real
        /// engagement has: most of what a post is ever going to get, it gets early. A post you
        /// watch for a minute visibly gains, and one you come back to has moved on without you.
        ///
        /// Nothing is stored. It is a function of the post's own age, so it survives a reload,
        /// costs no memory, and cannot drift out of step with anything.
        /// </summary>
        public int LikesNow => Grown(Likes);
        public int RepostsNow => Grown(Reposts);
        public int RepliesNow => Grown(Replies);

        /// <summary>How long a post takes to reach the numbers it was born with.</summary>
        private const int SettleMs = 240000;

        private int Grown(int settled)
        {
            if (settled <= 0) return 0;

            try
            {
                var age = Game.GameTime - At;

                // A post out of a loaded save has an At from a previous session and no sensible
                // age at all. It is old news either way, so it gets its settled figure.
                if (age < 0 || age >= SettleMs) return settled;

                var t = age / (float)SettleMs;
                var curve = 1f - (float)Math.Pow(1f - t, 2.2);

                var n = (int)(settled * curve);

                // Never nought while it has any at all. A post showing 0 replies and then 1 a
                // moment later reads as a bug; showing 1 and then 4 reads as a conversation.
                return n < 1 ? 1 : n;
            }
            catch
            {
                return settled;
            }
        }

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
