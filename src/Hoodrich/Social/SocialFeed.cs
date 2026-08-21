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
        Delivery,

        /// <summary>Police down. The city notices this one more than anything else you do.</summary>
        CopKilled,

        /// <summary>Woke up at Pillbox. Word gets round before you are out of the bed.</summary>
        Hospital,

        /// <summary>
        /// A rival put you down, and they are still talking about it when you get out.
        ///
        /// Separate from Hospital because the two are different rooms: Hospital is the block
        /// wishing you well, this is the set that did it laughing about how. Both go out when
        /// you wake up, which is exactly how it would arrive -- you come round to your people
        /// worrying and theirs celebrating.
        /// </summary>
        WastedBy,

        /// <summary>A car going up somewhere quiet. Somebody always sees the smoke.</summary>
        CarBurned,

        /// <summary>Shots on a street with people living on it.</summary>
        Shots,

        /// <summary>Somebody came through loud and kept going. Not the same as a drive-by.</summary>
        RideThrough,

        WarStarted,
        WarHeld,
        WarLost,

        /// <summary>Took a package off somebody because you had nothing of your own.</summary>
        FrontedWork,

        /// <summary>Moved it and got paid the few hundred for it.</summary>
        FrontedPaid
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
        private const int BurstGapFastMs = 1100;

        /// <summary>
        /// How many popups can be on screen at once.
        ///
        /// The game's own feed stacks forever, so a burst of four during a war put a column of
        /// text down the side of the screen in the middle of a gunfight. Three, and a fourth
        /// pushes the oldest out -- a scrolling feed rather than a growing wall.
        /// </summary>
        private const int OnScreen = 3;

        private const int BurstMax = 4;

        /// <summary>How many recent bodies are remembered, so nobody repeats anybody.</summary>
        /// <summary>
        /// How many posts back the feed remembers, to avoid repeating itself.
        ///
        /// Was 45 against a catalogue of eighteen hundred lines, which is a memory of about
        /// twenty minutes of play -- long enough to notice a repeat and too short to prevent
        /// one. Raised well past the biggest single pool so a set has to be genuinely
        /// exhausted before anything comes round again.
        /// </summary>
        private const int RecentMemory = 220;

        /// <summary>How hard to try for something nobody has said yet.</summary>
        private const int UniqueTries = 24;

        /// <summary>
        /// How often a post comes from somebody with a name.
        ///
        /// Higher on events than on ambient, because the cast comment on things that happen and
        /// the neighbourhood comments on everything. Too high either way and the feed becomes a
        /// group chat between eight famous people, which is not what a block sounds like.
        /// </summary>
        private const float VoicedEventChance = 0.55f;
        private const float VoicedAmbientChance = 0.42f;



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
        private int _burstGap = 2600;

        /// <summary>
        /// Notification handles currently up, oldest first.
        ///
        /// Held so the oldest can be taken down when a fourth arrives. The game will not do it
        /// for us -- its feed grows until it runs out of screen.
        /// </summary>
        private readonly Queue<int> _onScreen = new Queue<int>();

        /// <summary>
        /// Set while a war is on and nobody has fired yet.
        ///
        /// Cars pulling up is not news. The block starts posting when the shooting starts, which
        /// is both truer and stops the whole raid being narrated over the top of itself.
        /// </summary>
        public bool HoldUntilShots;

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

        /// <summary>Set by Main: the road you are standing on, for {street}.</summary>
        public Func<string> StreetYouAre;
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
                        Tint = TintFor(handle, node["gang"].AsString(""))
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

                foreach (var gang in doc["hashtags"].Keys)
                {
                    var tags = new List<string>();

                    foreach (var tag in doc["hashtags"][gang].Items)
                    {
                        var text = tag.AsString("");
                        if (!string.IsNullOrEmpty(text)) tags.Add(text);
                    }

                    if (tags.Count > 0) feed._hashtags[gang] = tags;
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

                // What each set calls itself, so {rival} can never hand a gang its own name.
                foreach (var gang in doc["selfWords"].Keys)
                {
                    var words = new List<string>();
                    foreach (var word in doc["selfWords"][gang].Items)
                    {
                        var text = word.AsString("");
                        if (!string.IsNullOrEmpty(text)) words.Add(text);
                    }

                    if (words.Count > 0) feed._selfWords[gang] = words;
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
        private static Color TintFor(string handle, string gang = "")
        {
            // Anybody who runs with a set wears the set. It is the first thing you would know
            // about them from a glance at a timeline, and it means a screen full of purple is
            // information rather than decoration.
            if (!string.IsNullOrEmpty(gang))
            {
                switch (gang.ToLowerInvariant())
                {
                    case "families": return Color.FromArgb(255, 46, 138, 62);
                    case "ballas":   return Color.FromArgb(255, 118, 62, 158);
                    case "vagos":    return Color.FromArgb(255, 196, 158, 38);
                    case "lost":     return Color.FromArgb(255, 132, 42, 38);
                    case "koreans":  return Color.FromArgb(255, 52, 96, 152);
                    // "armenians", with the s. data/socials.json spells it that way and this did not,
            // so the one Armenian author fell through to a hash hue and wore a random colour.
            // The Azteca had no case at all.
            case "armenians": return Color.FromArgb(255, 92, 84, 76);
            case "aztecas": return Color.FromArgb(255, 176, 96, 40);
                    case "marabunta":return Color.FromArgb(255, 38, 132, 128);
                }
            }

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

        /// <summary>
        /// Back to nobody knowing who you are.
        ///
        /// The timeline goes as well as the number. Followers at zero under a feed still full of
        /// people talking about what you did last week is a contradiction you can read.
        /// </summary>
        public void Wipe()
        {
            // The cards on screen as well. Wiping the feed and leaving three of them sat in the
            // corner is wiping the part nobody was looking at.
            if (Toasts != null) Toasts.Clear();

            _timeline.Clear();
            _recent.Clear();
            _recentSet.Clear();

            _burstLeft = 0;
            _burstSet = "";
            _argueUntil = 0;

            Followers = 0;
            if (Changed != null) Changed();

            // A few pieces of ordinary chatter, so it is a quiet feed rather than a broken one.
            for (var i = 0; i < 6; i++) Ambient(true);

            Log.Info("Socials wiped by the player.");
        }

        public void Start(int followers)
        {
            Followers = Math.Max(0, followers);
            _nextAmbient = Game.GameTime + 4000;

            // A few already on the timeline, so opening it for the first time is not an empty
            // screen where a neighbourhood should be. These are backdated and never notify --
            // they are what you missed, not what just happened.
            for (var i = 0; i < 12; i++) Ambient(true);
        }

        /// <summary>
        /// The two sets going back and forth about a raid, for a while after it is over.
        ///
        /// A fight that produces one post and then silence reads as a scoreboard. A fight that
        /// people are still arguing about four minutes later reads as something that happened
        /// between people who have to keep living near each other -- so whoever lost it keeps
        /// talking, whoever won it keeps answering, and it dies down on its own.
        /// </summary>
        /// <summary>
        /// Set by whoever is running the fight: the id of the gang on the other side.
        ///
        /// Needed because the rival-side sets are written once and used by everybody, so the
        /// words say "we" and "they" without naming anybody -- which is right, and means the
        /// author pool is the only thing that decides whose mouth they come out of.
        /// </summary>
        /// <summary>
        /// How they got you, set by Main from the cause of death.
        ///
        /// Read once when the post is built rather than stored on the post, because it only
        /// has to survive from the moment you go down to the moment you wake up.
        /// </summary>
        public string WastedHow = "shot";

        private string WastedSet()
        {
            switch ((WastedHow ?? "").ToLowerInvariant())
            {
                case "melee": return "WastedMelee";
                case "car": return "WastedCar";
                case "blast": return "WastedBlast";
                default: return "WastedShot";
            }
        }

        public string RivalGang
        {
            set { _rivalGang = value ?? ""; }
        }

        private string _rivalGang = "";

        public void Argue(string rival, bool held, int forMs = 150000)
        {
            _argueUntil = Game.GameTime + forMs;
            _argueNext = Game.GameTime + 6000;
            _argueSubject = rival ?? "";
            _argueHeld = held;
            _argueTheirTurn = !held; // whoever came off worse speaks first
        }

        /// <summary>
        /// Keeps the feed talking while a raid is actually happening.
        ///
        /// There was a burst when it kicked off and an argument once it was over, and in
        /// between -- while people were shooting at each other on somebody's front lawn --
        /// silence. Which is the wrong way round: the loudest the block ever is, is while it is
        /// going on.
        ///
        /// Called every tick by the war rather than once at the start, so it lapses on its own
        /// the moment the war stops calling it. Nothing has to remember to switch it off, and a
        /// war that ends badly -- script error, save load, the player driving off -- does not
        /// leave the feed reporting a fight that finished ten minutes ago.
        /// </summary>
        public void WarRunning(string rival)
        {
            _warRival = rival ?? "";
            _warUntil = Game.GameTime + WarLapseMs;

            if (_warNext == 0) _warNext = Game.GameTime + 4000;
        }

        /// <summary>How long after the last WarRunning call the commentary gives up.</summary>
        private const int WarLapseMs = 3000;

        /// <summary>Faster than the argument afterwards. This is people reacting, not debating.</summary>
        private const int WarGapMinMs = 6500;
        private const int WarGapMaxMs = 15000;

        /// <summary>
        /// The player's own account, built once from what the feed already knows about him.
        ///
        /// Deliberately NOT in the authors list: he must never be picked at random to comment
        /// on his own arrest, and a Franklin post has to be a Franklin post because he chose to
        /// write it.
        /// </summary>
        private Author _me;

        private Author Me
        {
            get
            {
                if (_me != null) return _me;

                _me = new Author
                {
                    Handle = Handle,
                    Name = DisplayName,
                    Gang = "families",
                    Gender = "male",
                    Pic = "CHAR_FRANKLIN",
                    Tint = System.Drawing.Color.FromArgb(255, 60, 180, 75),
                };

                return _me;
            }
        }

        /// <summary>
        /// Posts something in Franklin's own name.
        ///
        /// The set decides the words; the author is always him. Returns what was said so the
        /// caller can put it in a ticker, or null when the set has nothing left that has not
        /// been said recently.
        /// </summary>
        public string PostAsYou(string set, string subject)
        {
            var post = Build(set, subject ?? "");

            // A per-gang set that does not exist falls back to the general one, so a gang added
            // later without its own diss lines still gets a usable post rather than silence.
            if (post == null && set != null && set.StartsWith("YouDiss"))
            {
                post = Build("YouDiss", subject ?? "");
            }

            if (post == null) return null;

            post.By = Me;
            post.AboutYou = true;

            Add(post);
            Notify(post);

            return post.Plain;
        }

        /// <summary>
        /// You named a set in public and they read it.
        ///
        /// Their answers come in over the next minute or so rather than all at once, because
        /// three replies arriving in the same second is a script and three arriving as people
        /// pick up their phones is an argument.
        /// </summary>
        public void Dissed(string gangId, string gangName, int replies)
        {
            _dissGang = gangId ?? "";
            _dissName = gangName ?? "";
            _dissLeft = Math.Max(1, replies);
            _dissNext = Game.GameTime + DissFirstMs;
        }

        /// <summary>The set answering back, empty when nobody is.</summary>
        private string _dissGang = "";
        private string _dissName = "";
        private int _dissLeft;
        private int _dissNext;

        private const int DissFirstMs = 7000;
        private const int DissGapMinMs = 8000;
        private const int DissGapMaxMs = 19000;

        /// <summary>
        /// Forces the author pool to one set, whatever <see cref="GangFor"/> would have said.
        ///
        /// Needed because a reply from the gang you just insulted has to come from THAT gang.
        /// The set name alone cannot say which -- DissedBack is written six times over, once per
        /// set, and the pool has to match the words.
        /// </summary>
        private string _forceGang = "";

        private int _warUntil;
        private int _warNext;
        private string _warRival = "";
        private int _warTurn;

        private int _argueUntil;
        private int _argueNext;
        private string _argueSubject = "";
        private bool _argueHeld;
        private bool _argueTheirTurn;

        /// <summary>Gap between one side and the other having their say.</summary>
        private const int ArgueGapMinMs = 9000;
        private const int ArgueGapMaxMs = 22000;

        public void Update()
        {
            // Somebody answering what you said about them. Ahead of the war chatter because a
            // reply that arrives four minutes after the post it is replying to is not a reply.
            if (_dissLeft > 0 && Game.GameTime >= _dissNext && _burstLeft <= 0)
            {
                _dissLeft--;
                _dissNext = Game.GameTime + DissGapMinMs + _rng.Next(DissGapMaxMs - DissGapMinMs);

                _forceGang = _dissGang;

                var back = Build("DissedBack" + Pretty(_dissGang), _dissName)
                           ?? Build("DissedBack", _dissName);

                _forceGang = "";

                if (back != null)
                {
                    back.AboutYou = true;
                    Add(back);
                    Notify(back);
                }

                _nextAmbient = Game.GameTime + AmbientGapMinMs;
                return;
            }

            // It is happening right now, so this goes first.
            //
            // Two neighbours calling it for every one gang post: most people watching a raid
            // are not in either set, they are the ones on the floor of their own kitchen, and a
            // feed made only of the two gangs taunting each other reads like a chatroom rather
            // than a street.
            if (Game.GameTime < _warUntil && Game.GameTime >= _warNext && _burstLeft <= 0)
            {
                _warNext = Game.GameTime + WarGapMinMs + _rng.Next(WarGapMaxMs - WarGapMinMs);

                string set;
                switch (_warTurn % 4)
                {
                    case 1: set = "WarLiveRival"; break;
                    case 3: set = "WarLiveOurs"; break;
                    default: set = "WarLive"; break;
                }

                _warTurn++;

                var live = Build(set, _warRival);

                if (live != null)
                {
                    live.AboutYou = true;
                    Add(live);
                    Notify(live);
                }

                _nextAmbient = Game.GameTime + AmbientGapMinMs;
                return;
            }

            // Nothing going on. Reset so the next raid opens promptly rather than inheriting a
            // gap left over from the last one.
            if (Game.GameTime >= _warUntil && _warNext != 0) _warNext = 0;

            // Still arguing about the last one. Slower than a burst -- this is a conversation
            // across an afternoon, not a reaction.
            if (Game.GameTime < _argueUntil && Game.GameTime >= _argueNext && _burstLeft <= 0)
            {
                _argueNext = Game.GameTime + ArgueGapMinMs + _rng.Next(ArgueGapMaxMs - ArgueGapMinMs);

                var set = _argueTheirTurn
                    ? (_argueHeld ? "RivalMourns" : "RivalGloats")
                    : (_argueHeld ? "OursGloats" : "OursMourns");

                _argueTheirTurn = !_argueTheirTurn;

                var said = Build(set, _argueSubject);

                if (said != null)
                {
                    said.AboutYou = true;
                    Add(said);
                    Notify(said);
                }

                _nextAmbient = Game.GameTime + AmbientGapMinMs;
                return;
            }

            // A burst outranks the clock. Something just happened and the block is still talking
            // about it, so the ordinary chatter waits its turn.
            if (_burstLeft > 0 && Game.GameTime >= _burstNext)
            {
                _burstLeft--;
                _burstNext = Game.GameTime + _burstGap;

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

        /// <summary>
        /// Set by Main: one gang with a problem, and the name of who they have it with.
        ///
        /// Comes in from outside because this class has no gang registry and should not grow
        /// one -- it knows about authors and words, and the city's politics are somebody else's
        /// business. Returns [gangId, theirName], or null when nobody is feuding.
        /// </summary>
        public Func<string[]> BickerPair;

        /// <summary>
        /// Set by Main: what the block currently reckons of the product, 0..1.
        ///
        /// A function rather than a value because this class does not get to hold a reference
        /// to the player's state -- it knows about authors and words, and what is in the bags
        /// is somebody else's business. Null simply means nobody is talking about it.
        /// </summary>
        public Func<float> ProductRep;

        /// <summary>
        /// How far off neutral before anybody bothers mentioning it.
        ///
        /// Matched to the point where the readout starts saying something -- below this the
        /// block genuinely has no opinion, which is the correct thing for it to say about
        /// somebody who has sold four bags. It also means these never fire on a new save.
        /// </summary>
        private const float WorthSaying = 0.15f;

        /// <summary>
        /// The chance at maximum notoriety. Scaled down by how far off neutral you actually are.
        ///
        /// So one ambient post in twenty when word is only just going round, and better than
        /// one in four once you are known for it either way. It should feel like the talk
        /// builds rather than switching on.
        /// </summary>
        private const double WordChance = 0.35;

        /// <summary>
        /// The block, on your product. True when it took the slot.
        ///
        /// Deliberately competing with ordinary ambient chatter rather than running on its own
        /// timer: the feed has a fixed rate and this is the block choosing to talk about you
        /// instead of about the bins, which is what a reputation IS.
        /// </summary>
        private bool Word(bool backdated)
        {
            if (ProductRep == null) return false;

            var rep = ProductRep();

            // Distance from the middle, as 0..1. The middle is nobody knowing who you are.
            var off = Math.Abs(rep - 0.5f) * 2f;
            if (off < WorthSaying) return false;

            if (_rng.NextDouble() > off * WordChance) return false;

            var post = Build(rep >= 0.5f ? "ProductGood" : "ProductBad", null);
            if (post == null) return false;

            if (backdated)
            {
                post.At -= _rng.Next(120000, 3600000);
                Add(post);
                return true;
            }

            Add(post);
            Notify(post);
            return true;
        }

        /// <summary>How often an ambient post is one gang being rude about another.</summary>
        private const double BickerChance = 0.22;

        private void Ambient(bool backdated, bool business = false)
        {
            // Roughly one ambient post in five is two other gangs going at each other.
            //
            // Everything on this feed used to point at the Families -- every taunt, every
            // gloat, every RIP was somebody talking to us or about us, which makes a city of
            // eight gangs read as one gang and seven audiences. The Vagos and the Marabunta
            // have their own problem and it has nothing to do with Franklin.
            if (!business && _rng.NextDouble() < BickerChance && Bicker(backdated)) return;

            // And some of what is left is the block talking about what you are selling.
            if (!business && Word(backdated)) return;

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

        /// <summary>
        /// One gang, being rude about another, in their own voice.
        ///
        /// The author pool is forced to the gang doing the talking, and the set is theirs --
        /// GangOnGangTriads for Cheng's people, GangOnGangLost for the bikers. A Triad does not
        /// post like a Balla, and if he can then neither line was worth writing. The general
        /// set is the fallback for a gang nobody has written lines for yet.
        /// </summary>
        private bool Bicker(bool backdated)
        {
            if (BickerPair == null) return false;

            string[] pair;
            try { pair = BickerPair(); }
            catch { return false; }

            if (pair == null || pair.Length < 2) return false;
            if (string.IsNullOrEmpty(pair[0]) || string.IsNullOrEmpty(pair[1])) return false;

            _forceGang = pair[0];

            var post = Build("GangOnGang" + Pretty(pair[0]), pair[1])
                       ?? Build("GangOnGang", pair[1]);

            _forceGang = "";

            if (post == null) return false;

            // Not about you. It goes on the timeline and, when it is live, it notifies the same
            // as anything else -- but nothing in it is aimed at Franklin.
            if (backdated)
            {
                post.At -= _rng.Next(120000, 3600000);
                Add(post);
                return true;
            }

            Add(post);
            Notify(post);
            return true;
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
                case SocialEvent.Hospital:
                    // Nobody signs a get-well post with their colours. It is the block, not a set.
                    follow = "Hospital";
                    count = 1 + _rng.Next(2);
                    break;

                case SocialEvent.WastedBy:
                    // The set that did it, and the words say HOW. Being shot and being beaten
                    // with something are not the same story and a set that just dropped you
                    // would not tell them the same way.
                    follow = WastedSet();
                    count = 1 + _rng.Next(3);
                    break;

                case SocialEvent.CarBurned:
                    follow = "CarBurned";
                    count = 1 + _rng.Next(2);
                    break;

                case SocialEvent.RideThrough:
                    follow = "RideThrough";
                    count = 1 + _rng.Next(2);
                    break;

                case SocialEvent.Shots:
                    // Usually one person mentions it. Sometimes two, and the second one is
                    // always somebody arguing about how many they counted.
                    follow = "Shots";
                    count = _rng.NextDouble() < 0.35 ? 1 : 0;
                    break;

                case SocialEvent.RivalKilled:
                    // Their side grieves, our side gloats, and every so often somebody records.
                    follow = _rng.NextDouble() < 0.30 ? "DissTrack"
                           : _rng.NextDouble() < 0.55 ? "RivalMourns" : "OursGloats";
                    count = 2 + _rng.Next(BurstMax - 1);
                    break;

                case SocialEvent.CopKilled:
                    // The one thing the whole city has an opinion about, and it arrives fast.
                    follow = _rng.NextDouble() < 0.55 ? "CopKilled" : "OrgEvent";
                    count = BurstMax;
                    break;

                case SocialEvent.WarStarted:
                    follow = _rng.NextDouble() < 0.5 ? "WarStarted" : "OrgEvent";
                    count = BurstMax;
                    break;

                case SocialEvent.WarHeld:
                    follow = _rng.NextDouble() < 0.6 ? "OursGloats" : "RivalMourns";
                    count = 2 + _rng.Next(BurstMax - 1);
                    break;

                case SocialEvent.WarLost:
                    follow = _rng.NextDouble() < 0.6 ? "RivalGloats" : "BallasTaunt";
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

                case SocialEvent.FrontedWork:
                    // Somebody always notices, and half of them enjoy it.
                    follow = _rng.NextDouble() < 0.5 ? "Ambient" : "BallasTaunt";
                    break;

                case SocialEvent.FrontedPaid:
                    follow = "Ambient";
                    break;

                default:
                    return;
            }

            _burstSet = follow;
            _burstSubject = subject;
            _burstLeft = Math.Min(BurstMax, Math.Max(1, count));

            // Police and a raid on your own block arrive on top of each other. Everything else
            // has time to breathe.
            _burstGap = kind == SocialEvent.CopKilled || kind == SocialEvent.WarStarted
                ? BurstGapFastMs
                : BurstGapMs;

            _burstNext = Game.GameTime + _burstGap;
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

                case SocialEvent.CopKilled: return 1f;
                case SocialEvent.RideThrough: return 1f;
                case SocialEvent.Shots: return 0.9f;
                case SocialEvent.CarBurned: return 0.8f;
                case SocialEvent.Hospital: return 1f;
                case SocialEvent.WastedBy: return 1f;
                case SocialEvent.WarStarted: return 1f;
                case SocialEvent.WarHeld: return 1f;
                case SocialEvent.WarLost: return 1f;

                case SocialEvent.RivalKilled: return 0.45f;
                case SocialEvent.FrontedWork: return 0.55f;
                case SocialEvent.FrontedPaid: return 0.40f;
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
                case SocialEvent.CopKilled: return 20 + _rng.Next(45);
                case SocialEvent.WarHeld: return 45 + _rng.Next(60);
                case SocialEvent.WarLost: return -(25 + _rng.Next(30));
                // Two or three. One person saying it reads as a coincidence; the whole block
                // saying it at once is what actually happens when somebody gets hit.
                case SocialEvent.RideThrough: return 1 + _rng.Next(3);
                case SocialEvent.Shots: return _rng.Next(2);
                case SocialEvent.CarBurned: return _rng.Next(3);
                case SocialEvent.Hospital: return 1 + _rng.Next(2);
                case SocialEvent.WastedBy: return 12 + _rng.Next(30);
                case SocialEvent.WarStarted: return 0;

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

                if (!_recentSet.Contains(post.Plain)) return post;
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

                    // Same rule as the shared pool below. A written account with its own lines
                    // for one of these sets still has to be somebody who would say it.
                    if (OursOnly(set) && !Ours(author)) continue;

                    Dictionary<string, List<string>> sets;
                    if (!_voices.TryGetValue(author.Voice, out sets)) continue;

                    List<string> lines;
                    if (!sets.TryGetValue(set, out lines) || lines.Count == 0) continue;

                    candidates.Add(author);
                }

                if (candidates.Count > 0)
                {
                    // Preferring somebody whose own words are not all in the recent list.
                    //
                    // This is why the same tweet kept coming back. The median voice has FOUR
                    // lines for a set. Picking a voiced author at random and then asking for
                    // an unused line meant a four-line voice failed almost every time, and the
                    // retry re-rolled the author into the same small pools over and over --
                    // fourteen attempts later Build gave up and printed a repeat.
                    //
                    // Now the author is chosen from the ones who still have something new to
                    // say, and only falls back to the whole list if nobody does.
                    var fresh = new List<Author>();

                    foreach (var author in candidates)
                    {
                        foreach (var line in _voices[author.Voice][set])
                        {
                            if (_recentSet.Contains(line)) continue;

                            fresh.Add(author);
                            break;
                        }
                    }

                    var pool = fresh.Count > 0 ? fresh : candidates;

                    by = pool[_rng.Next(pool.Count)];
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
                var oursOnly = OursOnly(set);

                // Anybody with a voice of their own is excluded, with no exception.
                //
                // There used to be one: if no voice-less organisation was left, voiced accounts
                // were allowed to borrow the shared pool. That is how the LSPD ended up posting
                // a shop's opening hours. A written account says its own words or says nothing,
                // and a set with nobody to speak it simply produces no post -- the police having
                // nothing to say about a particular corner is not a hole that needs filling.
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

                    if (oursOnly && !Ours(author)) continue;

                    open.Add(author);
                }

                if (open.Count == 0) return null;

                if (!_templates.TryGetValue(set, out templates) || templates.Count == 0) return null;

                by = open[_rng.Next(open.Count)];
            }

            var body = Fill(templates[_rng.Next(templates.Count)], subject, amount, by);
            if (string.IsNullOrEmpty(body)) return null;

            var plain = body;
            body += TagsFor(by, set);

            var post = new Post
            {
                By = by,
                Body = body,
                Plain = plain,
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
        /// <summary>
        /// Sets that only neighbours and our own people would ever say.
        ///
        /// GangFor forces a pool to ONE gang, which is the wrong shape for these. The block
        /// talking about your product is the neighbours AND the set, both, and never theirs --
        /// a Balla telling you your work is good is not a compliment, and a Balla telling you
        /// it is garbage is not information.
        /// </summary>
        private static bool OursOnly(string set)
        {
            return string.Equals(set, "ProductGood", StringComparison.OrdinalIgnoreCase)
                || string.Equals(set, "ProductBad", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Somebody with no gang at all, or one of ours.</summary>
        private static bool Ours(Author author)
        {
            return string.IsNullOrEmpty(author.Gang)
                || string.Equals(author.Gang, "families", StringComparison.OrdinalIgnoreCase);
        }

        private string GangFor(string set)
        {
            // A reply to a diss has to come from the set that was dissed, and the set name
            // alone cannot say which -- DissedBack is written six times over, once each, and
            // the author pool has to match the words.
            if (!string.IsNullOrEmpty(_forceGang)) return _forceGang;

            switch (set)
            {
                // Whoever is actually on the other side of it.
                //
                // These used to return "ballas" flat, so a war with the Vagos produced Ballas
                // mourning their dead and Balla accounts gloating about a block in Rancho. The
                // set name cannot say who; the war has to.
                case "RivalMourns":
                case "RivalGloats":
                case "WarLiveRival":
                    return string.IsNullOrEmpty(_rivalGang) ? "ballas" : _rivalGang;

                // Whoever actually did it. Set by Main from the source of death.
                case "WastedShot":
                case "WastedMelee":
                case "WastedCar":
                case "WastedBlast":
                    return string.IsNullOrEmpty(_rivalGang) ? "ballas" : _rivalGang;

                case "BallasTaunt": return "ballas";
                case "VagosTaunt": return "vagos";

                case "OursGloats":
                case "OursMourns":
                case "WarLiveOurs":
                case "DissTrack": return "families";

                // CopKilled, WarStarted and WarLive are deliberately absent: everybody in the
                // city has a view on a dead officer, and everybody on the block can see cars
                // pulling up and hear what happens next.
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
        private string Fill(string template, string subject, int amount, Author by)
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
                var value = ValueFor(key, subject, amount, by);

                text = text.Substring(0, open) + value + text.Substring(close + 1);
            }

            return text;
        }

        private string ValueFor(string key, string subject, int amount, Author by = null)
        {
            // Nobody disses their own set.
            //
            // This list is names and colours -- Ballas, the purple, Vagos, the yellow -- and it
            // was drawn from blind, so a Balla account could and did post "we let Ballas eat and
            // they got greedy" under its own hashtags. Rolled again until it lands on somebody
            // else, and if the list is all self-references it says "them boys", which is true
            // of anybody.
            if (string.Equals(key, "rival", StringComparison.OrdinalIgnoreCase) &&
                by != null && !string.IsNullOrEmpty(by.Gang))
            {
                List<string> rivals;
                if (_slots.TryGetValue("rival", out rivals) && rivals.Count > 0)
                {
                    for (var tries = 0; tries < 8; tries++)
                    {
                        var pick = rivals[_rng.Next(rivals.Count)];
                        if (!IsSelf(by.Gang, pick)) return pick;
                    }

                    return "them boys";
                }
            }

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

            if (string.Equals(key, "street", StringComparison.OrdinalIgnoreCase))
            {
                // The road, not the district. "Down Forum Dr" is what somebody types; "in
                // Davis" is what a news report says, and {here} already covers that.
                var road = StreetYouAre == null ? "" : StreetYouAre();
                return string.IsNullOrEmpty(road) ? ValueFor("here", subject, amount) : road;
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
        /// <summary>
        /// What each set calls itself, so it can never be handed its own name as the enemy.
        /// Loaded from socials.json; a gang with no entry simply has nothing excluded.
        /// </summary>
        private readonly Dictionary<string, List<string>> _selfWords =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether a candidate rival is really the speaker's own lot.</summary>
        private bool IsSelf(string gangId, string candidate)
        {
            if (string.IsNullOrEmpty(gangId) || string.IsNullOrEmpty(candidate)) return false;

            List<string> mine;
            if (!_selfWords.TryGetValue(gangId, out mine)) return false;

            foreach (var word in mine)
            {
                if (candidate.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        /// <summary>
        /// Set by Main. When present, tweets are drawn down the right-hand side instead of
        /// being posted into the game's own notification stack on the left.
        /// </summary>
        public UI.TweetToast Toasts;

        private void Notify(Post post)
        {
            if (post == null || post.By == null) return;

            // Nothing at all until the first shot of a raid. The timeline still fills, so it is
            // all there to read afterwards -- it just does not narrate itself at you while you
            // are driving over.
            if (HoldUntilShots) return;

            // The right-hand stack, when it is switched on. Everything else the mod says --
            // busts, deliveries, money, warnings -- carries on going out the native way on the
            // left, which is the entire point of splitting them.
            if (Toasts != null && Toasts.Enabled)
            {
                Toasts.Show(post);
                return;
            }

            try
            {
                // Just the words. The name and the handle are already in the notification's
                // own header, so putting them at the front of the message as well said the same
                // thing twice, in a shape no phone has ever used. Who it is from is carried the
                // way people actually carry it -- on the end, in their set's colour.
                var line = post.Body;

                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, Draw.FormatFor(line));

                const int chunk = 96;
                for (var i = 0; i < line.Length; i += chunk)
                {
                    var len = Math.Min(chunk, line.Length - i);
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                                  line.Substring(i, len));
                }

                // A blank contact rather than a borrowed face. Everybody made up loses the
                // photograph -- a fabricated Balla wearing a stock picture of a middle-aged man
                // from the phone contacts is the most obviously wrong thing the feed can do --
                // but they keep the shape of a message, with the name and the handle in the
                // header where a phone puts them, and the colour and initial on the line below.
                //
                // Dropping to a plain ticker would have taken that shape away from ninety-five
                // posts in a hundred to fix the face on all of them.
                var pic = PicFor(post.By);
                if (string.IsNullOrEmpty(pic)) pic = BlankFace;

                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                              pic, pic, false, 0, post.By.Name, post.By.Handle);

                var handle = Function.Call<int>(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false);

                _onScreen.Enqueue(handle);
                Trim();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not deliver a post: " + ex.Message);
            }
        }

        /// <summary>A fourth pushes the first one off, so the column never grows past three.</summary>
        private void Trim()
        {
            while (_onScreen.Count > OnScreen)
            {
                var oldest = _onScreen.Dequeue();

                try { Function.Call(Hash.THEFEED_REMOVE_ITEM, oldest); }
                catch { /* it will time out on its own */ }
            }
        }

        /// <summary>
        /// What somebody signs off with.
        ///
        /// Anybody who runs with a set puts it on the end -- one to three of them, in the set's
        /// colour, out of whatever that set actually claims. It is how you tell who is talking
        /// without a face on the message, and it is what people do anyway.
        ///
        /// Nobody who is not in a gang gets any. A chicken shop does not sign its posts.
        /// </summary>
        private string TagsFor(Author by, string set)
        {
            if (by == null || string.IsNullOrEmpty(by.Gang)) return "";

            // Gang business only.
            //
            // Somebody signing off a remark about a television programme with their set's
            // colours is not how any of this works. A man tags his set when he is talking about
            // his set -- a taunt, a body, a block, a war. The rest of the time he is a person
            // who lives somewhere and the post is about a dog.
            if (!IsGangBusiness(set)) return "";

            List<string> pool;
            if (!_hashtags.TryGetValue(by.Gang, out pool) || pool.Count == 0) return "";

            var how = pool.Count == 1 ? 1 : 1 + _rng.Next(Math.Min(3, pool.Count));

            var picked = new List<string>();

            for (var attempt = 0; attempt < how * 4 && picked.Count < how; attempt++)
            {
                var tag = pool[_rng.Next(pool.Count)];
                if (!picked.Contains(tag)) picked.Add(tag);
            }

            if (picked.Count == 0) return "";

            var colour = Colour(by);
            var line = "";

            foreach (var tag in picked) line += " " + colour + tag;

            // Back to the normal colour, or everything after it on the same feed line inherits
            // whatever the last set was wearing.
            return line + "~s~";
        }

        /// <summary>
        /// The posts that are actually about the set.
        ///
        /// Everything else -- ambient chatter, shops, weather, the price of chicken -- goes out
        /// unsigned however deep in a gang the person writing it is.
        /// </summary>
        private static bool IsGangBusiness(string set)
        {
            switch (set)
            {
                case "BallasTaunt":
                case "VagosTaunt":
                case "RivalMourns":
                case "RivalGloats":
                case "OursGloats":
                case "OursMourns":
                case "DissTrack":
                case "RivalKilled":
                case "WarStarted":

                // WarLive is deliberately absent. Somebody lying on their kitchen floor while
                // it goes off outside is not repping a set, they are a person who lives there.
                case "WarLiveRival":
                case "WarLiveOurs":

                // One set being rude about another is the most gang thing on here.
                case "GangOnGang":
                case "GangOnGangFamilies":
                case "GangOnGangBallas":
                case "GangOnGangVagos":
                case "GangOnGangAztecas":
                case "GangOnGangMarabunta":
                case "GangOnGangLost":
                case "GangOnGangTriads":
                case "GangOnGangArmenians":
                case "Brawl":
                case "DriveBy":
                case "Tagged":
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// A gang id as the per-gang set names spell it: "ballas" -> "Ballas".
        ///
        /// The sets are written one per gang rather than with a {rival} slot in a single
        /// generic line, because the entire point of a diss is that it lands. "Rancho been ours
        /// since before half of you was born" is a Vagos line; handing it to the Lost MC makes
        /// it a line about nothing.
        /// </summary>
        private static string Pretty(string gangId)
        {
            if (string.IsNullOrEmpty(gangId)) return "";
            return char.ToUpperInvariant(gangId[0]) + gangId.Substring(1).ToLowerInvariant();
        }

        /// <summary>gang id -> what they sign off with.</summary>
        private readonly Dictionary<string, List<string>> _hashtags =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The nearest text colour the game has to somebody's set.
        ///
        /// Notifications only take the handful of named colour codes, so this is as close as a
        /// ticker can get to the disc the timeline draws.
        /// </summary>
        private static string Colour(Author by)
        {
            switch ((by.Gang ?? "").ToLowerInvariant())
            {
                case "families": return "~g~";
                case "ballas":   return "~p~";
                case "vagos":    return "~y~";
                case "lost":     return "~r~";
                case "koreans":  return "~b~";
                case "marabunta":return "~b~";
            }

            return "~s~";
        }

        /// <summary>
        /// The same face for the same person, every time, without storing one.
        ///
        /// Derived from the handle so it is stable across saves and across sessions, and drawn
        /// from the pool that matches who they are. Seventy people over roughly eighty-five
        /// faces means the rotation is wide enough that two posts in a row rarely wear the
        /// same one.
        /// </summary>
        /// <summary>
        /// A photograph, or nothing at all.
        ///
        /// Only people the game actually has a face for get one. Everybody else used to be
        /// handed a stock contact picture from a pool by gender, which meant a Balla with a
        /// name you invented posting under a photograph of somebody who exists in this game
        /// as somebody else entirely -- and one borrowed face gives the lie to all of it.
        ///
        /// An empty string here sends the post out as a ticker with the colour and the initial
        /// instead, which is what the timeline shows for them anyway.
        /// </summary>
        private static string PicFor(Author by)
        {
            return by.Pic ?? "";
        }

        /// <summary>The game's own empty contact picture. No face, same layout.</summary>
        private const string BlankFace = "CHAR_BLANK_ENTRY";

        /// <summary>
        /// Raised for every post that lands, with the poster's gang id or an empty string.
        ///
        /// An event rather than a reference back to Affiliation. The feed already knows who
        /// wrote each post and has no business knowing what anybody wants to do about it, and
        /// the standing tally is exactly the kind of thing that wants to hang off the feed
        /// without the feed having to carry it. Affiliation already hands out RivalDropped the
        /// same way round.
        /// </summary>
        public Action<string> Posted;

        private void Add(Post post)
        {
            _timeline.Insert(0, post);

            _recent.Enqueue(post.Plain);
            _recentSet.Add(post.Plain);

            while (_recent.Count > RecentMemory) _recentSet.Remove(_recent.Dequeue());
            while (_timeline.Count > Capacity) _timeline.RemoveAt(_timeline.Count - 1);

            // After the post is actually on the timeline, and wrapped, because a listener
            // throwing is not a reason for the post not to have happened.
            try { Posted?.Invoke(post.By == null ? "" : post.By.Gang); }
            catch (Exception ex) { Log.Debug("Post hook threw: " + ex.Message); }
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
