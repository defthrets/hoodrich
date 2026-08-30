using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using Hoodrich.Core;

namespace Hoodrich.Social
{
    /// <summary>One finished post handed over by another mod.</summary>
    internal sealed class GuestPost
    {
        /// <summary>Which mod wrote it. For the log, and for nothing else.</summary>
        public string From = "";

        public string Handle = "";
        public string Name = "";
        public string Gender = "none";
        public bool Verified;
        public string Text = "";

        public bool Usable => Handle.Length > 0 && Name.Length > 0 && Text.Length > 0;
    }

    /// <summary>
    /// Lets other mods put a post on the timeline.
    ///
    /// WHY A DROP FOLDER AND NOT AN API. Every script here lives in one shared assembly
    /// resolution namespace, so a second mod could in principle reflect its way to this class
    /// and call Add directly -- and would break the first time a field was renamed, silently,
    /// in somebody else's build. A folder of small files is a contract that survives both
    /// mods being rewritten, and it costs one directory scan every few seconds.
    ///
    /// FINISHED TEXT ONLY. A guest hands over the sentence it wants to say, not a template
    /// and not an event name. Hoodrich decides how a post LOOKS -- the avatar, the tint, the
    /// stamp, whether it toasts -- and the guest decides what it SAYS. Neither has to learn
    /// the other's content format, so Bare Minimum can rewrite every joke it owns without
    /// touching a line of this.
    ///
    /// ONE FILE PER POST, read then deleted. An append-only log shared between two scripts on
    /// two threads has a real interleaving problem and a half-written line renders as a
    /// half-written post; a whole file either exists or does not.
    ///
    /// NOTHING HERE ASSUMES ANYBODY IS THERE. No guests folder, no guests: this is a mod that
    /// most people run on its own.
    /// </summary>
    internal sealed class Guests
    {
        /// <summary>How often the folder is looked at. Real milliseconds.</summary>
        private const int ScanGapMs = 4000;

        /// <summary>
        /// How old a dropped file may be before it is binned unread. Real milliseconds.
        ///
        /// A crash between the write and the read leaves a file behind, and without this the
        /// first thing the feed says next session is a hot dog stand reacting to something
        /// that happened yesterday.
        /// </summary>
        private const int StaleMs = 600000;

        /// <summary>At most this many read in one pass, so a pile-up trickles in.</summary>
        private const int PerScan = 4;

        private readonly Queue<GuestPost> _waiting = new Queue<GuestPost>();

        private string _folder;
        private int _nextScan;

        /// <summary>True when at least one guest post is ready to go out.</summary>
        public bool Any => _waiting.Count > 0;

        // ======================================================================

        /// <summary>Takes the next post off the queue, or null.</summary>
        public GuestPost Next()
        {
            return _waiting.Count > 0 ? _waiting.Dequeue() : null;
        }

        /// <summary>Picks up anything dropped since the last look. Cheap when there is nothing.</summary>
        public void Collect()
        {
            var now = Game.GameTime;
            if (now < _nextScan) return;

            _nextScan = now + ScanGapMs;

            var folder = Folder();
            if (folder == null) return;

            try
            {
                var files = Directory.GetFiles(folder, "*.json");
                if (files.Length == 0) return;

                var taken = 0;

                foreach (var file in files)
                {
                    if (taken >= PerScan) break;

                    // READ AND DELETE, in that order, and the delete happens whatever the read
                    // decided. A file that cannot be parsed is a file that will never parse,
                    // and leaving it there means scanning past it for the rest of the session.
                    var post = ReadOne(file);
                    Bin(file);

                    taken++;

                    if (post == null || !post.Usable) continue;

                    _waiting.Enqueue(post);

                    Log.Debug("Guest post from " + (post.From.Length > 0 ? post.From : "somebody") +
                              " as @" + post.Handle + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read the guests folder: " + ex.Message);
            }
        }

        /// <summary>
        /// The drop folder, or null while nothing has ever used it.
        ///
        /// CACHED ONLY ONCE FOUND. Answering "no" is not remembered, so a guest mod that
        /// creates the folder later in the session -- because it only creates it when it
        /// finally has something to say -- is picked up on the next scan rather than needing
        /// a reload. Answering "no" costs one Exists check every four seconds, which is
        /// nothing, and the alternative is a feature that mysteriously needs restarting.
        /// </summary>
        private string Folder()
        {
            if (_folder != null) return _folder;

            try
            {
                var d = Path.Combine(Paths.Data, "guests");

                // NOT CREATED HERE. If nothing has ever dropped a post there is no folder, and
                // making one would put an empty directory in everybody's install to advertise
                // a feature only one other mod uses. The guest creates it when it has something
                // to say.
                if (Directory.Exists(d)) _folder = d;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not find the guests folder: " + ex.Message);
            }

            return _folder;
        }

        private static GuestPost ReadOne(string file)
        {
            try
            {
                if (Stale(file)) return null;

                var doc = JsonFile.Read(file, out var how);
                if (how != ReadResult.Ok || doc == null || doc.IsNull) return null;

                return new GuestPost
                {
                    From = doc["from"].AsString(""),
                    Handle = doc["handle"].AsString("").TrimStart('@'),
                    Name = doc["name"].AsString(""),
                    Gender = doc["gender"].AsString("none"),
                    Verified = doc["verified"].AsBool(false),
                    Text = doc["text"].AsString("")
                };
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read a guest post: " + ex.Message);
                return null;
            }
        }

        private static bool Stale(string file)
        {
            try
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
                return age.TotalMilliseconds > StaleMs;
            }
            catch
            {
                return false;
            }
        }

        private static void Bin(string file)
        {
            try { File.Delete(file); }
            catch (Exception ex) { Log.Debug("Could not clear " + file + ": " + ex.Message); }
        }
    }
}
