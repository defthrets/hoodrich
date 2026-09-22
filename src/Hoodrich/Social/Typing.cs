using System;
using System.Collections.Generic;
using System.Text;

namespace Hoodrich.Social
{
    /// <summary>
    /// How one person's thumbs work.
    ///
    /// WHY THIS IS CODE AND NOT DATA. socials.json is a shared pool: one line is handed to any
    /// of seventy-three people, so the line cannot carry a register. It was written with one
    /// anyway -- the file measured 64% of lines opening with a capital, 41% still holding their
    /// apostrophes and 1.7% carrying any abbreviation at all, which is not a street, it is one
    /// careful person typing seventy-three times.
    ///
    /// Orthography belongs to the WRITER, not to the sentence. So it lives here, it is decided
    /// once per account, and it never changes on them: Tanya drops every apostrophe she has ever
    /// owned and Kev keeps his, in every post either of them will ever make, and the same shared
    /// line about the price of chicken reads as two different people having typed it.
    ///
    /// That consistency is the whole point. A corpus that drops 80% of its apostrophes at random
    /// reads as sloppy; eighty per cent of PEOPLE who never use one reads as a phone.
    ///
    /// Nothing in here invents vocabulary. Slang is meaning and belongs in the words somebody
    /// actually wrote. This only ever removes and flattens -- which is what the research found
    /// does the work: strip the apostrophes, strip the full stops, lowercase the opener, and
    /// -ing becomes -in. Four rules, and a corpus is most of the way home before a single word
    /// of it changes.
    /// </summary>
    internal sealed class Typing
    {
        /// <summary>Lowercases the first word, and "I" wherever it stands alone.</summary>
        public bool Lower;

        /// <summary>dont, cant, aint, yall, thats. Never an apostrophe in a contraction.</summary>
        public bool NoApostrophes;

        /// <summary>
        /// No full stop, and -- this is the one that surprises people -- no question mark.
        /// "who up", "wyd tonight", "you still got it" are all questions, written flat. A
        /// question mark reads as either real urgency or as somebody's mother.
        /// </summary>
        public bool NoStops;

        /// <summary>
        /// talkin, goin, nothin. Never with the apostrophe -- "talkin" with one is how a
        /// novelist writes a voice, and nobody holding a phone has ever typed it.
        /// </summary>
        public bool Dropping;

        /// <summary>nahh, bruhh, lmaooo. Length is intensity and it is read that way.</summary>
        public bool Stretches;

        /// <summary>Shouts a short one. Caps carry volume, grief and celebration.</summary>
        public bool Shouts;

        /// <summary>The ellipsis. Reads forty-plus, which is exactly what it is for.</summary>
        public bool Trails;

        /// <summary>
        /// Somebody whose words are their own.
        ///
        /// Every hand-written voice defaults to this, and so does every organisation. Weazel
        /// News does not drop its apostrophes, the LSPD does not shout, and a line written for
        /// one specific mouth has already had this decision made about it by whoever wrote it.
        /// Styling those would not add a register, it would overwrite one.
        /// </summary>
        public static readonly Typing AsWritten = new Typing();

        public bool Idle => !Lower && !NoApostrophes && !NoStops && !Dropping
                            && !Stretches && !Shouts && !Trails;

        // ======================================================================
        // Who types how
        // ======================================================================

        /// <summary>
        /// The bands, by name, as data\socials.json spells them in an author's "types".
        ///
        /// street -- the default for anybody in a set who has no written lines of their own.
        ///           Nearly everything off, orthographically speaking.
        /// plain  -- a phone, but a tidier one. Half of them keep their apostrophes.
        /// proper -- capitals, apostrophes, full stops and the ellipsis. The aunties, the
        ///           teacher, the elders. Not an oversight: the research is explicit that
        ///           punctuation is a characterisation lever, and a block where every single
        ///           person types identically is the same flatness in a different key.
        /// none   -- untouched. See AsWritten.
        /// </summary>
        public static Typing Band(string band, string handle)
        {
            if (string.IsNullOrEmpty(band)) band = "none";

            switch (band.ToLowerInvariant())
            {
                case "street": return Roll(handle, 90, 85, 90, 85, 55, 25, 0);
                case "plain":  return Roll(handle, 70, 55, 72, 45, 25, 12, 8);
                case "proper": return Roll(handle, 0, 0, 0, 0, 0, 0, 55);
                default:       return AsWritten;
            }
        }

        /// <summary>
        /// The habits, decided once from the handle.
        ///
        /// SEEDED RATHER THAN RANDOM so a person is the same person across a save, a session
        /// and a reinstall -- the same reason the avatar colour is derived from the handle
        /// instead of being stored. Each habit gets its own slice of the hash so they do not
        /// move together; rolling one number and comparing it seven times would produce seven
        /// people, not seventy-three.
        /// </summary>
        private static Typing Roll(string handle, int lower, int apos, int stops,
                                   int dropping, int stretch, int shout, int trail)
        {
            return new Typing
            {
                Lower          = Chance(handle, 1, lower),
                NoApostrophes  = Chance(handle, 2, apos),
                NoStops        = Chance(handle, 3, stops),
                Dropping       = Chance(handle, 4, dropping),
                Stretches      = Chance(handle, 5, stretch),
                Shouts         = Chance(handle, 6, shout),
                Trails         = Chance(handle, 7, trail)
            };
        }

        private static bool Chance(string handle, int salt, int percent)
        {
            if (percent <= 0) return false;
            if (percent >= 100) return true;

            return Hash(handle, salt) % 100 < percent;
        }

        /// <summary>A small stable hash. Same shape as the one behind the avatar colours.</summary>
        internal static int Hash(string text, int salt)
        {
            var hash = 17 + salt * 131;

            if (text != null)
            {
                foreach (var c in text) hash = unchecked(hash * 31 + c);
            }

            return hash == int.MinValue ? 0 : Math.Abs(hash);
        }

        // ======================================================================
        // The names
        // ======================================================================

        /// <summary>
        /// Words that keep their capital even when the person typing keeps nothing else.
        ///
        /// LEARNED FROM THE FILE RATHER THAN LISTED, because a list would go stale the first
        /// time somebody adds a line. A word counts as a name if it ever turns up capitalised
        /// in the MIDDLE of a line -- "we from Grove", "not Franklin", "weazel news calling it"
        /// -- since nothing but a name has much reason to be capitalised there.
        ///
        /// The exception it has to carve out is a capital that is only there because a full
        /// stop came before it. "applause. Somebody said" would otherwise teach it that
        /// Somebody is a person, and then every line in the file that opens with the word
        /// keeps a capital it should have lost.
        /// </summary>
        private static readonly HashSet<string> Names =
            new HashSet<string>(StringComparer.Ordinal);

        public static void Learn(IEnumerable<string> lines)
        {
            if (lines == null) return;

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;

                var word = new StringBuilder();
                var first = true;
                var afterStop = false;

                for (var i = 0; i <= line.Length; i++)
                {
                    var c = i < line.Length ? line[i] : ' ';

                    if (char.IsLetter(c) || c == '\'')
                    {
                        word.Append(c);
                        continue;
                    }

                    if (word.Length > 0)
                    {
                        var w = word.ToString();
                        word.Length = 0;

                        // Not the opener, not standing behind a full stop, capitalised, and
                        // not a shout -- BREAKING and OGs are not people.
                        if (!first && !afterStop && char.IsUpper(w[0]) && HasLower(w))
                        {
                            Names.Add(w);
                        }

                        first = false;
                        afterStop = false;
                    }

                    if (c == '.' || c == '!' || c == '?' || c == ':' || c == '/' || c == '"')
                    {
                        afterStop = true;
                    }
                }
            }
        }

        private static bool HasLower(string word)
        {
            for (var i = 1; i < word.Length; i++)
            {
                if (char.IsLower(word[i])) return true;
            }

            return false;
        }

        public static bool IsName(string word)
        {
            return !string.IsNullOrEmpty(word) && Names.Contains(word);
        }

        public static int NamesKnown => Names.Count;

        // ======================================================================
        // The typing
        // ======================================================================

        /// <summary>
        /// Words that get held, and only where a held one would actually be.
        ///
        /// A reaction sits at one end of the line or the other -- "man, ...", "... nah". In
        /// the middle it is not a reaction, it is a noun, and stretching it produces "by a
        /// grown mannnn on a bicycle", which was the first thing this did.
        /// </summary>
        private static readonly string[] Stretchy =
        {
            "nah", "yeah", "bruh", "damn", "cuh", "ayy", "aye", "lmao", "ok", "woah", "man"
        };

        /// <summary>The ones that are only ever an interjection at the front. "man, ..."</summary>
        private static readonly HashSet<string> OpenersOnly =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "man", "ok", "damn" };

        /// <summary>
        /// Words that end in -ing and stay that way.
        ///
        /// Whole-word matching, not the suffix: "something" ends in "thing" and still becomes
        /// somethin, while "thing" itself does not become "thin", which is a different word.
        /// </summary>
        private static readonly HashSet<string> KeepsIng =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "thing", "things", "king", "kings", "ring", "rings", "sing", "bring",
                "brings", "string", "strings", "spring", "swing", "wing", "wings",
                "sting", "cling", "during", "ceiling", "viking", "ping", "ding",

                // The -ing that is not a verb ending. Taken off the file's own inventory of
                // four hundred -ing words rather than guessed at, after a keyring came out of
                // here as a keyrin. Note what is deliberately NOT here: morning, evening,
                // wedding, boxing, nothing, something and everything all become mornin, evenin,
                // weddin, boxin, nothin, somethin and everythin, which is correct.
                "awning", "awnings", "clothing", "ongoing", "keyring", "keyrings",
                "earring", "earrings", "herring", "sibling", "siblings", "darling",
                "darlings", "dumpling", "dumplings", "duckling", "ducklings",
                "lightning", "sapling", "gosling", "inkling", "fledgling", "shilling"
            };

        /// <summary>
        /// The line, as this person would have typed it.
        ///
        /// <paramref name="opens"/> is whether this text is the start of what they wrote. It is
        /// false for a word list dropped into the middle of a template, where there is no
        /// opening word to lowercase because somebody else's sentence is already in front of it.
        /// </summary>
        public string Apply(string text, bool opens)
        {
            if (Idle || string.IsNullOrEmpty(text)) return text;

            var seed = Hash(text, 11);

            // A SLOT NAME IS NOT A WORD ANYBODY TYPED.
            //
            // Found by running this over the raw file: {priceofthing} came out the other side
            // as {priceofthin}, which is not a slot, so it would have printed its own braces on
            // the phone. The letter rules only ever touch what is outside the braces now.
            text = Spelling(text, opens, seed);

            // And these three are about the whole line, so they run once at the end. Shout
            // leaves anything with a slot in it alone regardless -- a place name in capitals
            // reads as a headline, not as somebody raising their voice.
            if (NoStops) text = StripStop(text);
            if (Trails && opens) text = Trail(text, seed);
            if (Shouts && opens) text = Shout(text, seed);

            return text;
        }

        /// <summary>Every stretch of the line that is somebody's own words, and none that is not.</summary>
        private string Spelling(string text, bool opens, int seed)
        {
            if (text.IndexOf('{') < 0) return Letters(text, opens, seed);

            var b = new StringBuilder(text.Length);
            var i = 0;
            var first = true;

            while (i < text.Length)
            {
                var open = text.IndexOf('{', i);

                if (open < 0)
                {
                    b.Append(Letters(text.Substring(i), opens && first, seed));
                    break;
                }

                if (open > i)
                {
                    b.Append(Letters(text.Substring(i, open - i), opens && first, seed));
                    first = false;
                }

                var close = text.IndexOf('}', open);

                if (close < 0)
                {
                    b.Append(text, open, text.Length - open);
                    break;
                }

                b.Append(text, open, close - open + 1);
                first = false;
                i = close + 1;
            }

            return b.ToString();
        }

        private string Letters(string text, bool opens, int seed)
        {
            if (Lower) text = LowerIt(text, opens);
            if (NoApostrophes) text = StripApostrophes(text);
            if (Dropping) text = DropG(text);
            if (Stretches) text = Stretch(text, seed);

            return text;
        }

        /// <summary>The opener, and every "I" that is standing on its own.</summary>
        private static string LowerIt(string text, bool opens)
        {
            var b = new StringBuilder(text);

            // I, I'm, I've, I'll, I'd -- done before the apostrophes go, so the rule only has
            // to know about one shape of the word.
            for (var i = 0; i < b.Length; i++)
            {
                if (b[i] != 'I') continue;
                if (i > 0 && (char.IsLetter(b[i - 1]) || b[i - 1] == '\'')) continue;

                var next = i + 1 < b.Length ? b[i + 1] : ' ';
                if (char.IsLetter(next)) continue;

                b[i] = 'i';
            }

            text = b.ToString();

            if (!opens) return text;

            // And the first word, unless it is somebody's name or a shout.
            var start = 0;
            while (start < text.Length && !char.IsLetter(text[start])) start++;
            if (start >= text.Length || !char.IsUpper(text[start])) return text;

            var end = start;
            while (end < text.Length && (char.IsLetter(text[end]) || text[end] == '\'')) end++;

            var word = text.Substring(start, end - start);
            if (!HasLower(word)) return text;   // BREAKING, LOCAL, FREE
            if (IsName(word)) return text;      // Franklin, Grove, Weazel

            return text.Substring(0, start) + char.ToLowerInvariant(word[0])
                 + word.Substring(1) + text.Substring(end);
        }

        /// <summary>Only the ones inside a word. A quotation mark is not a contraction.</summary>
        private static string StripApostrophes(string text)
        {
            var b = new StringBuilder(text.Length);

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if ((c == '\'' || c == '’')
                    && i > 0 && i + 1 < text.Length
                    && char.IsLetter(text[i - 1]) && char.IsLetter(text[i + 1]))
                {
                    continue;
                }

                b.Append(c);
            }

            return b.ToString();
        }

        private static string DropG(string text)
        {
            var b = new StringBuilder(text.Length);
            var word = new StringBuilder();

            for (var i = 0; i <= text.Length; i++)
            {
                var c = i < text.Length ? text[i] : '\0';

                if (char.IsLetter(c))
                {
                    word.Append(c);
                    continue;
                }

                if (word.Length >= 5)
                {
                    var w = word.ToString();

                    // NOT IF AN APOSTROPHE IS ABOUT TO FOLLOW IT. The file quotes people --
                    // 'green over everything' -- and taking the g off inside a quotation
                    // leaves everythin' , which is the one spelling the research is emphatic
                    // that nobody has ever actually typed. It is how a novelist writes a voice.
                    var quoted = c == '\'' || c == '’';

                    if (!quoted && w.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                        && !KeepsIng.Contains(w))
                    {
                        word.Length -= 1;
                    }
                }

                b.Append(word);
                word.Length = 0;

                if (i < text.Length) b.Append(c);
            }

            return b.ToString();
        }

        /// <summary>
        /// The full stop off the end, and the question mark with it.
        ///
        /// An ellipsis is left where it is. It is somebody older typing, and taking it off
        /// would be removing the very thing it was put there to say.
        /// </summary>
        private static string StripStop(string text)
        {
            var end = text.Length;
            while (end > 0 && text[end - 1] == ' ') end--;
            if (end == 0) return text;

            var c = text[end - 1];
            if (c != '.' && c != '!' && c != '?') return text;

            // ... stays where it is.
            if (c == '.' && end >= 2 && text[end - 2] == '.') return text;

            return text.Substring(0, end - 1) + text.Substring(end);
        }

        private static string Trail(string text, int seed)
        {
            if (seed % 100 >= 30) return text;

            var end = text.Length;
            while (end > 0 && text[end - 1] == ' ') end--;
            if (end == 0) return text;

            if (text[end - 1] != '.') return text;
            if (end >= 2 && text[end - 2] == '.') return text;

            return text.Substring(0, end) + ".." + text.Substring(end);
        }

        /// <summary>One held letter on a reaction word, and only sometimes.</summary>
        private static string Stretch(string text, int seed)
        {
            if (seed % 100 >= 22) return text;

            foreach (var w in Stretchy)
            {
                var at = IndexOfWord(text, w);
                if (at < 0) continue;

                var opensWith = at == 0 || text.Substring(0, at).Trim().Length == 0;
                if (!opensWith)
                {
                    if (OpenersOnly.Contains(w)) continue;

                    // Otherwise the end of the line will do, punctuation and all.
                    var rest = text.Substring(at + w.Length).Trim();
                    if (rest.Length > 0 && rest != "." && rest != "!" && rest != "?") continue;
                }

                var last = text[at + w.Length - 1];
                var held = (seed % 3) + 1;

                return text.Substring(0, at + w.Length)
                     + new string(last, held)
                     + text.Substring(at + w.Length);
            }

            return text;
        }

        /// <summary>A short one, said loud. Long ones are not shouted, they are typed.</summary>
        private static string Shout(string text, int seed)
        {
            if (seed % 100 >= 12) return text;
            if (text.Length > 34 || text.IndexOf('{') >= 0) return text;

            var words = 1;
            foreach (var c in text)
            {
                if (c == ' ') words++;
            }

            if (words > 5) return text;

            return text.ToUpperInvariant();
        }

        private static int IndexOfWord(string text, string word)
        {
            var from = 0;

            while (from < text.Length)
            {
                var at = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return -1;

                var before = at == 0 || !char.IsLetter(text[at - 1]);
                var afterAt = at + word.Length;
                var after = afterAt >= text.Length || !char.IsLetter(text[afterAt]);

                if (before && after) return at;

                from = at + 1;
            }

            return -1;
        }
    }
}
