using System;
using System.Collections.Generic;
using System.IO;
using Hoodrich.Core;

namespace Hoodrich.Social
{
    /// <summary>
    /// What you can text back, and what they might say to it.
    ///
    /// YOU COULD NOT ANSWER ANYBODY. Twelve systems across nine files send you messages -- a
    /// man telling you his spot is being shot at, somebody asking where you are for the fourth
    /// time, a garage saying your car is straight -- and the inbox was a wall you read. The
    /// only key that did anything on a thread was the one that closed it.
    ///
    /// MATCHED ON WORDS RATHER THAN ON A TOPIC. None of those systems files a category with
    /// the text; there is a sender, a subject and a body and that is all there has ever been.
    /// So a reply set claims the words it answers, and the first set with one of its words in
    /// the last thing they said is the set that answers it. Order in the file is the priority:
    /// "Ballas outside right now, get down here" is both a fight and a summons, and the fight
    /// is the half worth answering.
    ///
    /// TONE IS PER SET AND THAT IS THE POINT OF DOING IT THIS WAY. Somebody whose block is
    /// being raided does not want a joke; somebody asking where you are for the fourth time
    /// has earned one. The sets near the top of the file are straight and the ones under them
    /// are not, which is a thing you can only do when you know what is being talked about.
    ///
    /// IN DATA, because it is dialogue. Every other line of writing in this mod that turned out
    /// to want changing -- the leaders, the socials, the dealers -- ended up in a JSON file for
    /// the same reason, and a reply nobody can edit without a compiler is a reply that stays
    /// wrong.
    /// </summary>
    internal static class Replies
    {
        private sealed class Set
        {
            public string[] When = new string[0];
            public string[] Lines = new string[0];
            public string[] Back = new string[0];
        }

        private static readonly List<Set> Sets = new List<Set>();

        private static string[] _fallback = { "aight" };
        private static string[] _fallbackBack = { "" };

        private static bool _loaded;
        private static readonly Random Rng = new Random();

        /// <summary>Read once, on the first ask. Nothing needs it before then.</summary>
        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                var doc = JsonFile.Read(Path.Combine(Paths.Data, "replies.json"));

                if (doc == null)
                {
                    Log.Warn("No replies.json; you can answer everybody with 'aight'.");
                    return;
                }

                foreach (var node in doc["sets"].Items)
                {
                    var set = new Set
                    {
                        When = Words(node["when"]),
                        Lines = Words(node["lines"]),
                        Back = Words(node["back"])
                    };

                    if (set.When.Length == 0 || set.Lines.Length == 0) continue;

                    Sets.Add(set);
                }

                var fall = Words(doc["default"]);
                if (fall.Length > 0) _fallback = fall;

                var fallBack = Words(doc["defaultBack"]);
                if (fallBack.Length > 0) _fallbackBack = fallBack;

                Log.Info("Replies loaded: " + Sets.Count + " sets.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read replies.json: " + ex.Message);
            }
        }

        private static string[] Words(Json node)
        {
            var list = node == null ? null : node.AsStringList();

            return list == null ? new string[0] : list.ToArray();
        }

        /// <summary>
        /// Something to say to the last thing they said.
        ///
        /// The subject AND the body are searched, because the two carry different halves of it
        /// -- "GET HERE" is the whole meaning of a message whose body is a complaint about
        /// traffic, and a raid is named in the body of one whose subject is somebody's name.
        /// </summary>
        public static string To(string subject, string body, out string[] back)
        {
            Load();

            back = _fallbackBack;

            var hay = ((subject ?? "") + " " + (body ?? "")).ToLowerInvariant();

            foreach (var set in Sets)
            {
                foreach (var word in set.When)
                {
                    if (string.IsNullOrEmpty(word)) continue;
                    if (hay.IndexOf(word, StringComparison.Ordinal) < 0) continue;

                    back = set.Back;
                    return set.Lines[Rng.Next(set.Lines.Length)];
                }
            }

            return _fallback[Rng.Next(_fallback.Length)];
        }

        /// <summary>
        /// What they say to that, or "" for nothing.
        ///
        /// AN EMPTY STRING IS A REAL ANSWER AND THE FILE USES IT DELIBERATELY. Not every text
        /// gets a reply back, and a conversation where the other person always has the last
        /// word is a conversation with a machine in it. Empties are left in the lists so the
        /// chance of silence is authored per set rather than being one number in here.
        /// </summary>
        public static string Back(string[] from)
        {
            if (from == null || from.Length == 0) return "";

            return from[Rng.Next(from.Length)] ?? "";
        }
    }
}
