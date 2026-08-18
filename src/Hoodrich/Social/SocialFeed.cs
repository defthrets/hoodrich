using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Social
{
    /// <summary>Something that happened, which the block might have an opinion about.</summary>
    internal enum SocialEvent
    {
        Ambient,
        MissionTaken,
        MissionDone,
        MissionFailed,
        RivalKilled,
        Sale,
        BigSale,
        Busted,
        Tagged,
        Brawl,
        DriveBy,
        RankUp,
        JoinedGang,
        LeftGang,
        Delivery
    }

    /// <summary>
    /// The block, talking about itself.
    ///
    /// Everything here exists to make the neighbourhood feel occupied by people who are not
    /// waiting for you. Most of what scrolls past has nothing to do with anything you did --
    /// somebody's car got towed, somebody is selling a sofa, somebody thinks the chicken spot
    /// has gone downhill. That is the point: a feed that only ever talks about the player is a
    /// scoreboard with avatars on it, and the moment a post about YOU appears in the middle of
    /// forty posts about nothing, it lands, because it had to compete for the space.
    ///
    /// Posts are built from templates with slots rather than picked from a list of finished
    /// sentences. A few hundred fragments across a few dozen shapes gives tens of thousands of
    /// combinations, which is the only honest way to promise you will rarely read the same post
    /// twice -- a fixed list of five hundred runs out in an evening.
    /// </summary>
    internal sealed class SocialFeed
    {
        /// <summary>How many posts are kept. Older ones fall off the bottom, as they would.</summary>
        private const int Capacity = 80;

        /// <summary>Ambient posting rate, in real seconds, when you are just walking around.</summary>
        private const int AmbientGapMinMs = 26000;
        private const int AmbientGapMaxMs = 75000;

        /// <summary>
        /// Nobody, to begin with.
        ///
        /// Starting at zero is the whole point: the number is the only thing in the mod that
        /// records what you have actually done out there, and handing you sixty for turning the
        /// game on makes it a decoration instead of a record.
        /// </summary>
        private const int StartingFollowers = 0;

        /// <summary>
        /// Contact pictures, assigned per handle so somebody always turns up looking the same.
        ///
        /// These are the game's own phone-contact textures. A real headshot needs a ped that
        /// exists in the world, and the people writing these posts do not -- so the notification
        /// borrows the same art the phone uses for people who are not on screen.
        /// </summary>
        private static readonly string[] ContactPics =
        {
            "CHAR_DEFAULT", "CHAR_BLOCKED", "CHAR_CHAT_CALL", "CHAR_SOCIAL_CLUB",
            "CHAR_LAMAR", "CHAR_FRANKLIN", "CHAR_DENISE", "CHAR_SIMEON",
            "CHAR_MP_MECHANIC", "CHAR_MP_GERALD", "CHAR_MP_STRETCH", "CHAR_MP_MERRYWEATHER"
        };

        private readonly Random _rng = new Random();
        private readonly List<Post> _timeline = new List<Post>();
        private readonly List<Author> _authors = new List<Author>();

        private readonly Dictionary<string, List<string>> _templates =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<string>> _slots =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private int _nextAmbient;

        public SocialFeed()
        {
            Handle = "@franklin_c";
            DisplayName = "Franklin Clinton";
        }

        /// <summary>Newest first, which is the order it is read in.</summary>
        public IReadOnlyList<Post> Timeline => _timeline;

        public string Handle { get; private set; }
        public string DisplayName { get; private set; }

        /// <summary>Set by Main from the save.</summary>
        public int Followers;
        public int Following = 118;

        /// <summary>Raised whenever followers change, so the save knows to write.</summary>
        public Action Changed;

        /// <summary>Filled in by Main so posts can name the block you are actually on.</summary>
        public Func<string> WhereYouAre;
        public Func<string> YourGang;

        // ---- loading -----------------------------------------------------------

        public static SocialFeed Load()
        {
            var feed = new SocialFeed();

            var doc = JsonFile.Read(Path.Combine(Paths.Data, "socials.json"));

            if (doc == null)
            {
                Log.Warn("No socials.json; the feed will be very quiet.");
                feed.AddFallback();
                return feed;
            }

            try
            {
                feed.Handle = doc["you"]["handle"].AsString("@franklin_c");
                feed.DisplayName = doc["you"]["name"].AsString("Franklin Clinton");
                feed.Following = Math.Max(0, doc["you"]["following"].AsInt(118));

                foreach (var node in doc["authors"].Items)
                {
                    var handle = node["handle"].AsString("");
                    if (string.IsNullOrEmpty(handle)) continue;

                    feed._authors.Add(new Author
                    {
                        Handle = handle.StartsWith("@") ? handle : "@" + handle,
                        Name = node["name"].AsString(handle),
                        Gang = node["gang"].AsString(""),
                        Verified = node["verified"].AsBool(false),
                        Tint = TintFor(handle)
                    });
                }

                foreach (var key in doc["posts"].Keys)
                {
                    var list = new List<string>();
                    foreach (var line in doc["posts"][key].Items)
                    {
                        var text = line.AsString("");
                        if (!string.IsNullOrEmpty(text)) list.Add(text);
                    }

                    if (list.Count > 0) feed._templates[key] = list;
                }

                foreach (var key in doc["slots"].Keys)
                {
                    var list = new List<string>();
                    foreach (var line in doc["slots"][key].Items)
                    {
                        var text = line.AsString("");
                        if (!string.IsNullOrEmpty(text)) list.Add(text);
                    }

                    if (list.Count > 0) feed._slots[key] = list;
                }

                Log.Info("Socials loaded: " + feed._authors.Count + " people, " +
                         feed._templates.Count + " post sets, " + feed._slots.Count + " word lists.");
            }
            catch (Exception ex)
            {
                Log.Error("socials.json was unreadable; the feed will be very quiet.", ex);
            }

            if (feed._authors.Count == 0 || feed._templates.Count == 0) feed.AddFallback();

            return feed;
        }

        /// <summary>Enough to not be empty if the file is missing or broken.</summary>
        private void AddFallback()
        {
            if (_authors.Count == 0)
            {
                _authors.Add(new Author { Handle = "@davisdaily", Name = "Davis Daily", Tint = TintFor("davisdaily") });
                _authors.Add(new Author { Handle = "@chamblocktalk", Name = "Block Talk", Tint = TintFor("chamblocktalk") });
            }

            if (!_templates.ContainsKey("Ambient"))
            {
                _templates["Ambient"] = new List<string>
                {
                    "quiet one out here today",
                    "whoever keeps parking across my driveway, we gon have a conversation"
                };
            }
        }

        /// <summary>
        /// Avatar colour from the handle.
        ///
        /// Derived rather than stored so somebody always has the same colour, in every save,
        /// without a colour field on every one of them -- and so a handle you have never seen
        /// still gets one that looks deliberate.
        /// </summary>
        private static Color TintFor(string handle)
        {
            var hash = 17;
            foreach (var c in handle) hash = hash * 31 + c;

            var hue = Math.Abs(hash) % 360;
            return FromHue(hue, 0.42f, 0.52f);
        }

        private static Color FromHue(float h, float s, float v)
        {
            var c = v * s;
            var x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
            var m = v - c;

            float r = 0f, g = 0f, b = 0f;

            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromArgb(255,
                                  (int)((r + m) * 255f), (int)((g + m) * 255f), (int)((b + m) * 255f));
        }

        // ---- the timeline ------------------------------------------------------

        public void Start(int followers)
        {
            Followers = Math.Max(0, followers);
            _nextAmbient = Game.GameTime + 4000;

            // A few already on the timeline, so opening it for the first time is not an empty
            // screen where a neighbourhood should be. These are backdated and never notify --
            // they are what you missed, not what just happened.
            for (var i = 0; i < 9; i++) Ambient(true);
        }

        public void Update()
        {
            if (Game.GameTime < _nextAmbient) return;

            _nextAmbient = Game.GameTime + AmbientGapMinMs + _rng.Next(AmbientGapMaxMs - AmbientGapMinMs);
            Ambient(false);
        }

        private void Ambient(bool backdated)
        {
            var post = Build("Ambient", null);
            if (post == null) return;

            if (backdated)
            {
                // Spread the opening batch back over the last hour or so, so the first look at
                // the feed is a morning's worth of chatter rather than nine posts at once.
                post.At -= _rng.Next(120000, 3600000);
                Add(post);
                return;
            }

            Add(post);
            Notify(post);
        }

        // ---- things that happened ----------------------------------------------

        /// <summary>
        /// Something worth talking about.
        ///
        /// Not everything gets posted. A block that comments on every single sale is a block of
        /// people who are watching you rather than living, so the smaller events roll dice and
        /// most of them come up quiet.
        /// </summary>
        public void On(SocialEvent kind, string subject = "", int amount = 0)
        {
            var chance = ChanceFor(kind);
            if (chance < 1f && _rng.NextDouble() > chance) return;

            var post = Build(kind.ToString(), subject, amount);
            if (post == null) return;

            post.AboutYou = true;
            Add(post);
            Notify(post);

            var gained = FollowersFor(kind, amount);
            if (gained == 0) return;

            Followers = Math.Max(0, Followers + gained);
            if (Changed != null) Changed();

            if (gained > 0 && gained >= 12)
            {
                Hoodrich.UI.Notify.Ticker("~b~+" + gained + " followers~s~  ·  " +
                                          Followers.ToString("N0") + " followers");
            }
        }

        private static float ChanceFor(SocialEvent kind)
        {
            switch (kind)
            {
                // The big ones always get talked about.
                case SocialEvent.MissionDone:
                case SocialEvent.MissionFailed:
                case SocialEvent.RankUp:
                case SocialEvent.JoinedGang:
                case SocialEvent.LeftGang:
                case SocialEvent.Brawl:
                case SocialEvent.DriveBy:
                    return 1f;

                case SocialEvent.RivalKilled: return 0.45f;
                case SocialEvent.Busted: return 0.7f;
                case SocialEvent.Tagged: return 0.5f;
                case SocialEvent.BigSale: return 0.35f;
                case SocialEvent.Delivery: return 0.2f;

                // Somebody serving somebody on a corner is not news.
                case SocialEvent.Sale: return 0.05f;

                default: return 0.5f;
            }
        }

        private int FollowersFor(SocialEvent kind, int amount)
        {
            switch (kind)
            {
                case SocialEvent.MissionDone: return 18 + _rng.Next(26);
                case SocialEvent.MissionFailed: return -(4 + _rng.Next(9));
                case SocialEvent.RankUp: return 60 + _rng.Next(90);
                case SocialEvent.RivalKilled: return 3 + _rng.Next(7);
                case SocialEvent.Brawl: return 6 + _rng.Next(12);
                case SocialEvent.DriveBy: return 10 + _rng.Next(18);
                case SocialEvent.Tagged: return 2 + _rng.Next(5);
                case SocialEvent.JoinedGang: return 25 + _rng.Next(30);
                case SocialEvent.LeftGang: return -(10 + _rng.Next(20));
                case SocialEvent.Busted: return -(2 + _rng.Next(6));
                case SocialEvent.BigSale: return 1 + _rng.Next(4);
                default: return 0;
            }
        }

        // ---- building a post ---------------------------------------------------

        private Post Build(string set, string subject, int amount = 0)
        {
            List<string> templates;
            if (!_templates.TryGetValue(set, out templates) || templates.Count == 0) return null;
            if (_authors.Count == 0) return null;

            var body = Fill(templates[_rng.Next(templates.Count)], subject, amount);
            if (string.IsNullOrEmpty(body)) return null;

            var by = _authors[_rng.Next(_authors.Count)];

            var post = new Post
            {
                By = by,
                Body = body,
                At = Game.GameTime
            };

            // Engagement scales off your following, because a bigger audience is the only thing
            // that changes what a post does. Wide spread so the numbers do not look generated.
            var reach = Math.Max(8, Followers);

            post.Likes = _rng.Next(reach / 40, Math.Max(3, reach / 4));
            post.Reposts = _rng.Next(0, Math.Max(2, post.Likes / 5));
            post.Replies = _rng.Next(0, Math.Max(2, post.Likes / 8));

            return post;
        }

        /// <summary>
        /// Fills the slots in a template.
        ///
        /// {subject} is whatever the caller passed -- a gang name, a product, a mission. {money}
        /// is the amount. Everything else is looked up in the word lists, so a template can say
        /// {hood} or {filler} and get something different every time it is used.
        /// </summary>
        private string Fill(string template, string subject, int amount)
        {
            var text = template;
            var guard = 0;

            while (guard++ < 12)
            {
                var open = text.IndexOf('{');
                if (open < 0) break;

                var close = text.IndexOf('}', open + 1);
                if (close < 0) break;

                var key = text.Substring(open + 1, close - open - 1);
                var value = ValueFor(key, subject, amount);

                text = text.Substring(0, open) + value + text.Substring(close + 1);
            }

            return text;
        }

        private string ValueFor(string key, string subject, int amount)
        {
            if (string.Equals(key, "subject", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(subject) ? "them" : subject;
            }

            if (string.Equals(key, "money", StringComparison.OrdinalIgnoreCase))
            {
                return amount.ToString("N0");
            }

            if (string.Equals(key, "count", StringComparison.OrdinalIgnoreCase))
            {
                return amount.ToString();
            }

            if (string.Equals(key, "here", StringComparison.OrdinalIgnoreCase))
            {
                var where = WhereYouAre == null ? "" : WhereYouAre();
                return string.IsNullOrEmpty(where) ? "the block" : where;
            }

            if (string.Equals(key, "yours", StringComparison.OrdinalIgnoreCase))
            {
                var gang = YourGang == null ? "" : YourGang();
                return string.IsNullOrEmpty(gang) ? "the homies" : gang;
            }

            List<string> list;
            if (_slots.TryGetValue(key, out list) && list.Count > 0) return list[_rng.Next(list.Count)];

            // An unknown slot is a typo in the data file, and printing the braces is how you
            // find it. Silently swallowing it would leave a hole nobody notices.
            return "{" + key + "}";
        }

        /// <summary>
        /// A post arriving, as the phone would deliver it.
        ///
        /// The game's own feed notification, with a contact picture, the display name in the
        /// sender line and the handle under it -- which is exactly the shape of a text message,
        /// because that is what a post arriving on a phone IS. Deliberately silent: a chime for
        /// every one of these would be unbearable within ten minutes, and the point is that the
        /// block is talking whether or not you are listening.
        /// </summary>
        private void Notify(Post post)
        {
            if (post == null || post.By == null) return;

            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, Draw.FormatFor(post.Body));

                const int chunk = 96;
                for (var i = 0; i < post.Body.Length; i += chunk)
                {
                    var len = Math.Min(chunk, post.Body.Length - i);
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                                  post.Body.Substring(i, len));
                }

                var pic = PicFor(post.By.Handle);

                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                              pic, pic, false, 0, post.By.Name, post.By.Handle);

                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not deliver a post: " + ex.Message);
            }
        }

        /// <summary>The same picture for the same person, every time, without storing one.</summary>
        private static string PicFor(string handle)
        {
            var hash = 17;
            foreach (var c in handle) hash = hash * 31 + c;

            return ContactPics[Math.Abs(hash) % ContactPics.Length];
        }

        private void Add(Post post)
        {
            _timeline.Insert(0, post);

            while (_timeline.Count > Capacity) _timeline.RemoveAt(_timeline.Count - 1);
        }

        /// <summary>How long ago, the way a timeline writes it.</summary>
        public static string Ago(int at)
        {
            var ms = Math.Max(0, Game.GameTime - at);
            var seconds = ms / 1000;

            if (seconds < 60) return seconds + "s";
            if (seconds < 3600) return (seconds / 60) + "m";
            if (seconds < 86400) return (seconds / 3600) + "h";

            return (seconds / 86400) + "d";
        }
    }
}
