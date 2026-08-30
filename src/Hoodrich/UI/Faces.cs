using System.Collections.Generic;
using GTA.Native;

namespace Hoodrich.UI
{
    /// <summary>
    /// Whose face goes with a name, in the one place that decides it.
    ///
    /// This map existed three times -- once in Conversation for the dialogue panel, once in
    /// GangLeaders for a leader's texts and once in DealerManager for a plug's -- and the
    /// copies had already drifted. Gerald's opening text, the very first thing the mod says to
    /// anybody, went out under a grey silhouette because the call site that sent it did not
    /// know about any of them and passed the default.
    ///
    /// So the sender's NAME is the lookup key, because it is the one thing every one of those
    /// paths already has. A text now gets the right picture by virtue of being from a person
    /// somebody has heard of, rather than by the call site remembering to say so.
    ///
    /// These are the game's own contact pictures. Nothing here ships a texture; CHAR_ dicts are
    /// what the vanilla phone puts on a message from Lamar and they are what this puts on a
    /// message from Lamar.
    /// </summary>
    internal static class Faces
    {
        /// <summary>The grey silhouette. What a stranger's message looks like.</summary>
        public const string Nobody = "CHAR_DEFAULT";

        /// <summary>
        /// The faces this mod ever asks for.
        ///
        /// Held as a list so they can be pulled in ahead of time. They are small 2D dictionaries
        /// -- the phone has all of them resident whenever it is open -- so warming the set costs
        /// nothing worth measuring and buys the guarantee below.
        /// </summary>
        private static readonly string[] Everyone =
        {
            "CHAR_LAMAR", "CHAR_MP_GERALD", "CHAR_MP_STRETCH", "CHAR_CHENG",
            "CHAR_DENISE", "CHAR_FRANKLIN", "CHAR_TANISHA", "CHAR_MICHAEL", "CHAR_TREVOR",

            // The father. Only ever needed once a save, and on the one text where a silhouette
            // would land worst -- the new man at the port introducing himself.
            "CHAR_CHENGSR"
        };

        private static readonly HashSet<string> Resident = new HashSet<string>();

        private static bool _warm;

        /// <summary>
        /// Whose face goes with a name. Empty for somebody nobody has a photo of.
        ///
        /// Deliberately not defaulted to the silhouette here: the dialogue panel wants to know
        /// there is no picture so it can lay out without one, where a text always wants SOME
        /// portrait. Two different right answers, so the caller picks.
        /// </summary>
        public static string For(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            switch (name.Trim().ToUpperInvariant())
            {
                case "LAMAR": return "CHAR_LAMAR";
                case "GERALD": return "CHAR_MP_GERALD";
                case "STRETCH": return "CHAR_MP_STRETCH";
                case "TAO CHENG": return "CHAR_CHENG";
                case "DENISE": return "CHAR_DENISE";
                case "FRANKLIN": return "CHAR_FRANKLIN";
                case "TANISHA": return "CHAR_TANISHA";

                // The recovery driver. There is no Tanya in this game, so she borrows the
                // nearest thing it has -- and which of these a build ships is not something to
                // guess at from here, so the first that loads wins.
                case "TANYA": return FirstReady("CHAR_TOW_TRUCK", "CHAR_MP_MECHANIC",
                                                "CHAR_LS_CUSTOMS", "CHAR_TANISHA");
                case "MICHAEL": return "CHAR_MICHAEL";
                case "TREVOR": return "CHAR_TREVOR";

                default: return "";
            }
        }

        /// <summary>
        /// Pulls the whole set in, so the first text of a session already has a picture.
        ///
        /// REQUEST_STREAMED_TEXTURE_DICT is asynchronous, and a message posted on the same
        /// frame as the request draws before the texture lands. That is not hypothetical --
        /// Gerald's first text fires the moment his package is cleared, which for most players
        /// is the first CHAR_ dict the mod has ever asked for.
        ///
        /// Called off a slow tick and stops asking once everything is in, so it is a handful of
        /// requests early in a session and then nothing.
        /// </summary>
        public static void Warm()
        {
            if (_warm) return;

            var all = true;

            foreach (var dict in Everyone)
            {
                if (Resident.Contains(dict)) continue;

                if (Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict))
                {
                    Resident.Add(dict);
                    continue;
                }

                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, dict, false);
                all = false;
            }

            if (!all) return;

            _warm = true;
            Core.Log.Debug("Contact pictures are resident.");
        }

        /// <summary>
        /// The picture if the game actually has it, and the silhouette if it does not.
        ///
        /// A CHAR_ name that is not in this build draws NO picture at all, and a message with a
        /// hole where the face should be reads as broken in a way the plain silhouette does not.
        /// Checked rather than assumed because the two GTA installs this runs on do not ship
        /// identical contact sets.
        /// </summary>
        /// <summary>
        /// The first of these this build actually has, or the silhouette.
        ///
        /// For a picture that is a BRAND rather than a person. There is nobody to photograph
        /// for a driverless car service, and the game has no picture of one -- so the best
        /// available is somebody else's logo, and which logos a given build ships is not
        /// something to guess at from here. Every name is requested on the way past, so a
        /// call that falls through today succeeds a moment later.
        /// </summary>
        public static string FirstReady(params string[] dicts)
        {
            if (dicts == null || dicts.Length == 0) return Nobody;

            var got = Nobody;

            foreach (var dict in dicts)
            {
                if (string.IsNullOrEmpty(dict)) continue;

                var ready = Ready(dict);

                if (ready != Nobody && got == Nobody) got = ready;
            }

            return got;
        }

        public static string Ready(string dict)
        {
            if (string.IsNullOrEmpty(dict)) return Nobody;
            if (Resident.Contains(dict)) return dict;

            if (Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict))
            {
                Resident.Add(dict);
                return dict;
            }

            // Ask, so the next one has it, and use the silhouette for this one.
            Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, dict, false);
            return Nobody;
        }
    }
}
