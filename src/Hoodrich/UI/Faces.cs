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
            "CHAR_HAO",

            // HIS FALLBACKS, WARMED WITH THE REST. FirstReady only hands back a dictionary it
            // has actually seen load, so a fallback nobody ever asks for is not a fallback --
            // and on a build with no CHAR_HAO in it Hao's picture would come out grey for ever
            // with two perfectly good shop logos sat there unrequested. See For.
            "CHAR_LS_CUSTOMS", "CHAR_MP_MECHANIC",

            // The father. Only ever needed once a save, and on the one text where a silhouette
            // would land worst -- the new man at the port introducing himself.
            "CHAR_CHENGSR"
        };

        private static readonly HashSet<string> Resident = new HashSet<string>();

        /// <summary>What his photograph is filed under. See For and Theme.</summary>
        private const string VernonFace = "vernon";

        /// <summary>When the pictures are next checked over. See Theme.</summary>
        private static int _nextCheck;

        /// <summary>How often. Cheap enough that it may as well be often.</summary>
        private const int CheckEveryMs = 15000;

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
                // The man on the lot. His own contact picture where the game ships one --
                // it does, for the street races -- and the shop's otherwise.
                case "HAO": return FirstReady("CHAR_HAO", "CHAR_LS_CUSTOMS", "CHAR_MP_MECHANIC");

                // THE MAN ON THE WALL, AND THE GAME HAS NO PICTURE OF HIM AT ALL.
                //
                // Vernon is a story ped. Rockstar never gave him a phone card, so there is no
                // CHAR_ to ask for and every message he has ever sent -- the one after you die
                // on his job, the one with his number in it -- arrived over the grey
                // silhouette, from a man whose face is in the dialogue panel two feet above it.
                //
                // So his is PHOTOGRAPHED instead. Headshots renders a ped model into a texture
                // the message layer draws exactly like a contact picture -- Notify checks
                // Headshots.Holds before it checks the streamed dictionaries, for this reason
                // -- and the LUber driver has had his face this way for weeks.
                //
                // Empty until the factory has been round, which is the right answer rather
                // than a wrong picture: Notify falls back to the silhouette on an empty, the
                // way it does for anybody it has never heard of. See Theme, which asks.
                case "VERNON": return Headshots.Txd(VernonFace) ?? "";

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
        public static void Theme()
        {
            // RE-CHECKED FOR EVER, RATHER THAN ONCE. This used to latch: the moment every
            // picture had loaded it set a flag and never looked again.
            //
            // A request is not a lease. REQUEST_STREAMED_TEXTURE_DICT asks the streamer for
            // something and the streamer is entitled to take it back -- and it does, under
            // memory pressure, which on this mod means a takeover with thirty-five cars and
            // sixty people in it. Once that happened the picture was gone for the rest of the
            // session and nothing would ever ask for it again, so every text after it came out
            // as the grey silhouette. That is exactly what Stretch's did.
            //
            // Fifteen seconds and about fourteen native calls, which is nothing, and an evicted
            // face is back before the next message needs it.
            var now = GTA.Game.GameTime;

            if (_nextCheck != 0 && now < _nextCheck) return;

            _nextCheck = now + CheckEveryMs;

            // AND VERNON'S PHOTOGRAPH, WHICH IS NOT A STREAMED DICTIONARY AT ALL.
            //
            // Asked for here because here is the thing that already runs on a slow clock and
            // already exists to make sure a face is ready before the message that needs it.
            // WantAs does nothing once the picture has been taken, and backs off on its own
            // after a failure, so this is one dictionary lookup every fifteen seconds for the
            // rest of the session.
            //
            // ig_vernon first and csb_vernon behind it -- the same pair his own spawner uses,
            // in the same order, so the photograph is of the man who is actually stood on the
            // wall rather than of a cutscene variant of him.
            try
            {
                // ASKED FOR EVEN ONCE IT EXISTS, because asking is what keeps it. Eviction
                // takes whichever face has gone longest without being wanted -- and the social
                // feed photographs a new stranger every few seconds, thirty of them an hour.
                // A contact whose picture is only wanted on the rare frame he texts you is a
                // contact whose picture has been thrown away by the time he does.
                Headshots.Txd(VernonFace);

                Headshots.WantAs(VernonFace, "ig_vernon", "csb_vernon");
            }
            catch { /* he keeps the silhouette, which is where this started */ }

            var lost = 0;

            foreach (var dict in Everyone)
            {
                if (Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict))
                {
                    Resident.Add(dict);
                    continue;
                }

                // Dropped out of Resident as well, or FirstReady goes on handing back the name
                // of a picture that is no longer there.
                if (Resident.Remove(dict)) lost++;

                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, dict, false);
            }

            if (lost > 0)
            {
                Core.Log.Info("Contact pictures: " + lost +
                              " had been unloaded and were asked for again.");
            }

            Roll();
        }

        /// <summary>
        /// Says once which of these the build actually has.
        ///
        /// A CHAR_ NAME IS A GUESS UNTIL THE STREAMER ANSWERS. Not every contact picture is in
        /// every build -- that is the whole reason FirstReady exists -- and up to now the only
        /// way to find out which ones were missing was to look at a phone and see a grey
        /// square. One line, after the set has had a few passes to load, and then never again.
        ///
        /// Not on the first pass: a dictionary asked for on frame one has not arrived by frame
        /// two, and a roll call taken then would name every picture in the mod as missing.
        /// </summary>
        private static void Roll()
        {
            if (_rolled) return;

            _passes++;

            if (_passes < 3) return;

            _rolled = true;

            var missing = "";

            foreach (var dict in Everyone)
            {
                if (Resident.Contains(dict)) continue;

                missing += (missing.Length == 0 ? "" : ", ") + dict;
            }

            Core.Log.Info("Contact pictures: " + Resident.Count + " of " + Everyone.Length +
                          " are in this build" +
                          (missing.Length == 0 ? "." : ". Not here: " + missing));
        }

        private static bool _rolled;
        private static int _passes;

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
