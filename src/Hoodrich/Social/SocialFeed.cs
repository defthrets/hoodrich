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

        /// <summary>
        /// How often the block says something.
        ///
        /// Fast, and everything it says comes through. The notification and the timeline are the
        /// same feed seen twice: a post pops as it is written and is still sitting there in the
        /// same order when you open the tab. Holding some back would mean the feed you read and
        /// the feed you were shown are two different feeds, which is worse than either.
        /// </summary>
        private const int AmbientGapMinMs = 10000;
        private const int AmbientGapMaxMs = 20000;

        /// <summary>
        /// A burst is several posts about the same thing, seconds apart.
        ///
        /// One post about a shooting is a notification. Four, arriving on top of each other from
        /// four different people who each saw a different piece of it, is a neighbourhood
        /// reacting -- and that is the difference between a feed that reports at you and a feed
        /// that is happening around you.
        /// </summary>
        private const int BurstGapMs = 2600;
        private const int BurstMax = 4;

        /// <summary>How many recent bodies are remembered, so nobody repeats anybody.</summary>
        private const int RecentMemory = 45;

        /// <summary>How hard to try for something nobody has said yet.</summary>
        private const int UniqueTries = 14;

        /// <summary>
        /// How often a post comes from somebody with a name.
        ///
        /// Higher on events than on ambient, because the cast comment on things that happen and
        /// the neighbourhood comments on everything. Too high either way and the feed becomes a
        /// group chat between eight famous people, which is not what a block sounds like.
        /// </summary>
        private const float VoicedEventChance = 0.55f;
        private const float VoicedAmbientChance = 0.30f;



        /// <summary>
        /// Nobody, to begin with.
        ///
        /// Starting at zero is the whole point: the number is the only thing in the mod that
        /// records what you have actually done out there, and handing you sixty for turning the
        /// game on makes it a decoration instead of a record.
        /// </summary>
        private const int StartingFollowers = 0;

        /// <summary>
        /// Contact pictures, split by who is holding the phone.
        ///
        /// These are the game's own phone-contact textures -- every face it ships that belongs
        /// to a nobody, which is most of them. A real headshot needs a ped that exists in the
        /// world and the people writing these posts do not, so the notification borrows the
        /// same art the phone uses for anybody who is not on screen.
        ///
        /// The story leads are deliberately absent. Franklin, Michael, Trevor, Lamar and
        /// Stretch are people who exist in this mod and can be stood in front of; seeing one of
        /// their faces on a stranger complaining about the price of chicken breaks the whole
        /// illusion in a way no amount of good writing recovers from.
        /// </summary>
        private static readonly string[] MalePics =
        {
            "CHAR_ANDREAS", "CHAR_BARRY", "CHAR_BEVERLY", "CHAR_CASTRO", "CHAR_CHEF",
            "CHAR_CHENG", "CHAR_CHENGSR", "CHAR_CRIS", "CHAR_DAVE", "CHAR_DEVIN",
            "CHAR_DOM", "CHAR_DREYFUSS", "CHAR_DR_FRIEDLANDER", "CHAR_FLOYD", "CHAR_HAO",
            "CHAR_JIMMY", "CHAR_JIMMY_BOSTON", "CHAR_JOE", "CHAR_JOSEF", "CHAR_JOSH",
            "CHAR_LAZLOW", "CHAR_LESTER", "CHAR_MANUEL", "CHAR_MARTIN", "CHAR_MECHANIC",
            "CHAR_NIGEL", "CHAR_OMEGA", "CHAR_ONEIL", "CHAR_ORTEGA", "CHAR_OSCAR",
            "CHAR_RON", "CHAR_SIMEON", "CHAR_SOLOMON", "CHAR_STEVE", "CHAR_WADE",
            "CHAR_MP_BRUCIE", "CHAR_MP_GERALD", "CHAR_MP_JULIO", "CHAR_MP_MECHANIC",
            "CHAR_MP_RAY_LAVOY", "CHAR_MP_ROBERTO", "CHAR_MP_FAM_BOSS", "CHAR_MP_MEX_BOSS",
            "CHAR_MP_MEX_DOCKS", "CHAR_MP_MEX_LT", "CHAR_MP_BIKER_BOSS",
            "CHAR_MP_BIKER_MECHANIC", "CHAR_MP_PROF_BOSS", "CHAR_MP_SNITCH",
            "CHAR_MP_ARMY_CONTACT", "CHAR_MP_FIB_CONTACT"
        };

        private static readonly string[] FemalePics =
        {
            "CHAR_ABIGAIL", "CHAR_AMANDA", "CHAR_ANTONIA", "CHAR_BROKEN_DOWN_GIRL",
            "CHAR_DENISE", "CHAR_HITCHER_GIRL", "CHAR_MARNIE", "CHAR_MARY_ANN",
            "CHAR_MAUDE", "CHAR_MOLLY", "CHAR_PATRICIA", "CHAR_SAEEDA", "CHAR_TANISHA",
            "CHAR_TAXI_LIZ", "CHAR_TENNIS_COACH", "CHAR_TOW_TONYA", "CHAR_TRACEY",
            "CHAR_MP_STRIPCLUB_PR", "CHAR_MP_FM_CONTACT"
        };

        /// <summary>Everything that is not a person: papers, shops, radio stations.</summary>
        private static readonly string[] OrgPics =
        {
            "CHAR_LIFEINVADER", "CHAR_SOCIAL_CLUB", "CHAR_LS_CUSTOMS", "CHAR_BUGSTARS",
            "CHAR_EPSILON", "CHAR_MERRYWEATHER", "CHAR_LS_TOURIST_BOARD", "CHAR_PEGASUS_DELIVERY",
            "CHAR_MP_MORS_MUTUAL", "CHAR_TAXI", "CHAR_CALL911", "CHAR_BLOCKED",
            "CHAR_CHAT_CALL", "CHAR_DIAL_A_SUB", "CHAR_MINOTAUR", "CHAR_SASQUATCH"
        };

        private readonly Random _rng = new Random();
        private readonly List<Post> _timeline = new List<Post>();
        private readonly List<Author> _authors = new List<Author>();

        private readonly Dictionary<string, List<string>> _templates =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<string>> _slots =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>voice -> post set -> that character's own lines.</summary>
        private readonly Dictionary<string, Dictionary<string, List<string>>> _voices =
            new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

        private int _nextAmbient;
        private int _lastNotify;

        /// <summary>
        /// What has been said lately, so nobody says it again.
        ///
        /// Two people posting the same sentence word for word is the single thing that gives the
        /// whole system away, and with a few hundred templates and a post every ten seconds it
        /// happens constantly on its own. A post is rerolled until it is something nobody has
        /// said recently, and only settled for after enough tries that the pool is plainly out.
        /// </summary>
        private readonly Queue<string> _recent = new Queue<string>();
        private readonly HashSet<string> _recentSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>An event still spilling out across several posts.</summary>
        private string _burstSet = "";
        private string _burstSubject = "";
        private int _burstLeft;
        private int _burstNext;

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
                        Gender = node["gender"].AsString("male"),
                        Pic = node["pic"].AsString(""),
                        Voice = node["voice"].AsString(""),
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

                foreach (var voice in doc["voices"].Keys)
                {
                    var sets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var set in doc["voices"][voice].Keys)
                    {
                        var lines = new List<string>();

                        foreach (var line in doc["voices"][voice][set].Items)
                        {
                            var text = line.AsString("");
                            if (!string.IsNullOrEmpty(text)) lines.Add(text);
                        }

                        if (lines.Count > 0) sets[set] = lines;
                    }

                    if (sets.Count > 0) feed._voices[voice] = sets;
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

                Log.Info("Socials loaded: " + feed._authors.Count + " people (" +
                         feed._voices.Count + " with their own voice), " +
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
            for (var i = 0; i < 12; i++) Ambient(true);
        }

        public void Update()
        {
            // A burst outranks the clock. Something just happened and the block is still talking
            // about it, so the ordinary chatter waits its turn.
            if (_burstLeft > 0 && Game.GameTime >= _burstNext)
            {
                _burstLeft--;
                _burstNext = Game.GameTime + BurstGapMs;

                var post = Build(_burstSet, _burstSubject);

                if (post != null)
                {
                    post.AboutYou = true;
                    Add(post);
                    Notify(post);
                }

                _nextAmbient = Game.GameTime + AmbientGapMinMs;
                return;
            }

            if (Game.GameTime < _nextAmbient) return;

            _nextAmbient = Game.GameTime + AmbientGapMinMs + _rng.Next(AmbientGapMaxMs - AmbientGapMinMs);

            var roll = _rng.NextDouble();

            // Businesses post on the same clock as everybody else, just less often -- there are
            // fewer of them and they have less to say. The other sets run their mouths on the
            // same clock too, because a rivalry that only speaks when you poke it is a reaction
            // rather than a rivalry.
            if (roll < 0.14) { Taunt(); return; }

            Ambient(false, roll < 0.34);
        }

        private void Ambient(bool backdated, bool business = false)
        {
            var post = Build(business ? "AmbientOrg" : "Ambient", null);
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

            // Then everybody else's reaction to the same thing, seconds apart.
            React(kind, subject);

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

        /// <summary>
        /// Sets the block talking about what just happened, from more than one mouth.
        ///
        /// The second and third posts are the ones that sell it. A gang losing somebody says so;
        /// the other side says something about it; the shops nearby shut their doors. None of
        /// that is a report -- it is people who were all in the same place at the same time.
        /// </summary>
        private void React(SocialEvent kind, string subject)
        {
            string follow;
            var count = 2;

            switch (kind)
            {
                case SocialEvent.RivalKilled:
                    // Their side grieves, our side gloats, and every so often somebody records.
                    follow = _rng.NextDouble() < 0.30 ? "DissTrack"
                           : _rng.NextDouble() < 0.55 ? "RivalMourns" : "OursGloats";
                    count = 2 + _rng.Next(BurstMax - 1);
                    break;

                case SocialEvent.DriveBy:
                    follow = _rng.NextDouble() < 0.5 ? "RivalMourns" : "OrgEvent";
                    count = 2 + _rng.Next(BurstMax - 1);
                    break;

                case SocialEvent.Brawl:
                    follow = _rng.NextDouble() < 0.5 ? "OursGloats" : "BallasTaunt";
                    break;

                case SocialEvent.MissionDone:
                    follow = _rng.NextDouble() < 0.45 ? "OursGloats"
                           : _rng.NextDouble() < 0.5 ? "BallasTaunt" : "VagosTaunt";
                    break;

                case SocialEvent.MissionFailed:
                    follow = _rng.NextDouble() < 0.5 ? "RivalGloats" : "BallasTaunt";
                    break;

                case SocialEvent.Busted:
                    follow = _rng.NextDouble() < 0.5 ? "RivalGloats" : "OrgEvent";
                    break;

                case SocialEvent.Tagged:
                    follow = "BallasTaunt";
                    break;

                default:
                    return;
            }

            _burstSet = follow;
            _burstSubject = subject;
            _burstLeft = Math.Min(BurstMax, Math.Max(1, count));
            _burstNext = Game.GameTime + BurstGapMs;
        }

        /// <summary>
        /// The other sets, running their mouths when nothing in particular has happened.
        ///
        /// Called on the ambient clock rather than off an event, because a rivalry that only
        /// speaks when you poke it is not a rivalry, it is a reaction. They should be talking
        /// while you are stood in your own kitchen doing nothing at all.
        /// </summary>
        public void Taunt()
        {
            var set = _rng.NextDouble() < 0.5 ? "BallasTaunt" : "VagosTaunt";

            var post = Build(set, null);
            if (post == null) return;

            Add(post);
            Notify(post);
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
            for (var attempt = 0; attempt < UniqueTries; attempt++)
            {
                var post = BuildOnce(set, subject, amount);
                if (post == null) return null;

                if (!_recentSet.Contains(post.Body)) return post;
            }

            // The pool for this set is genuinely exhausted, so a repeat is better than silence.
            return BuildOnce(set, subject, amount);
        }

        private Post BuildOnce(string set, string subject, int amount = 0)
        {
            if (_authors.Count == 0) return null;

            Author by = null;
            List<string> templates = null;

            // Somebody with their own words gets first refusal, weighted by whether this is the
            // kind of thing they would bother commenting on. Michael is more likely to have a
            // view about a job going down than about the price of chicken.
            var voicedChance = string.Equals(set, "Ambient", StringComparison.OrdinalIgnoreCase)
                ? VoicedAmbientChance
                : VoicedEventChance;

            if (_rng.NextDouble() < voicedChance)
            {
                var candidates = new List<Author>();

                foreach (var author in _authors)
                {
                    if (!author.HasVoice) continue;

                    Dictionary<string, List<string>> sets;
                    if (!_voices.TryGetValue(author.Voice, out sets)) continue;

                    List<string> lines;
                    if (!sets.TryGetValue(set, out lines) || lines.Count == 0) continue;

                    candidates.Add(author);
                }

                if (candidates.Count > 0)
                {
                    by = candidates[_rng.Next(candidates.Count)];
                    templates = _voices[by.Voice][set];
                }
            }

            if (by == null)
            {
                // Who is even eligible depends on what is being said. A chicken shop does not
                // post about a shooting the way a neighbour does, and a Balla does not commiserate
                // about one of ours -- so the author pool is chosen first and the words follow.
                var open = new List<Author>();

                var wantOrg = string.Equals(set, "AmbientOrg", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(set, "OrgEvent", StringComparison.OrdinalIgnoreCase);

                var wantGang = GangFor(set);

                foreach (var author in _authors)
                {
                    if (author.HasVoice) continue;

                    var isOrg = string.Equals(author.Gender, "none", StringComparison.OrdinalIgnoreCase);

                    if (wantOrg != isOrg) continue;

                    if (!string.IsNullOrEmpty(wantGang) &&
                        !string.Equals(author.Gang, wantGang, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    open.Add(author);
                }

                if (open.Count == 0) return null;

                if (!_templates.TryGetValue(set, out templates) || templates.Count == 0) return null;

                by = open[_rng.Next(open.Count)];
            }

            var body = Fill(templates[_rng.Next(templates.Count)], subject, amount);
            if (string.IsNullOrEmpty(body)) return null;

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
        /// Which set has to be posting, for the sets that only one side would ever say.
        ///
        /// Empty means anybody. This is what stops a Families member gloating over one of their
        /// own going down, which is the sort of thing that reads as a bug even when nobody can
        /// say why.
        /// </summary>
        private static string GangFor(string set)
        {
            switch (set)
            {
                case "BallasTaunt": return "ballas";
                case "VagosTaunt": return "vagos";
                case "RivalMourns":
                case "RivalGloats": return "ballas";
                case "OursGloats":
                case "OursMourns":
                case "DissTrack": return "families";
                default: return "";
            }
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

            _lastNotify = Game.GameTime;

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

                var pic = PicFor(post.By);

                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                              pic, pic, false, 0, post.By.Name, post.By.Handle);

                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not deliver a post: " + ex.Message);
            }
        }

        /// <summary>
        /// The same face for the same person, every time, without storing one.
        ///
        /// Derived from the handle so it is stable across saves and across sessions, and drawn
        /// from the pool that matches who they are. Seventy people over roughly eighty-five
        /// faces means the rotation is wide enough that two posts in a row rarely wear the
        /// same one.
        /// </summary>
        private static string PicFor(Author by)
        {
            // Anybody the game already has a face for wears their own.
            if (!string.IsNullOrEmpty(by.Pic)) return by.Pic;

            var pool = string.Equals(by.Gender, "female", StringComparison.OrdinalIgnoreCase) ? FemalePics
                     : string.Equals(by.Gender, "none", StringComparison.OrdinalIgnoreCase) ? OrgPics
                     : MalePics;

            var hash = 17;
            foreach (var c in by.Handle) hash = hash * 31 + c;

            return pool[Math.Abs(hash) % pool.Length];
        }

        private void Add(Post post)
        {
            _timeline.Insert(0, post);

            _recent.Enqueue(post.Body);
            _recentSet.Add(post.Body);

            while (_recent.Count > RecentMemory) _recentSet.Remove(_recent.Dequeue());
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
