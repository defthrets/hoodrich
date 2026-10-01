using System;
using System.Collections.Generic;
using System.IO;

namespace Hoodrich.Core
{
    /// <summary>
    /// The languages the settings screen can offer. English is the code's own; the rest are
    /// files in scripts\Hoodrich\lang.
    ///
    /// Hindi is Latin-script Hindi -- the way Indian players type it -- because the game's
    /// fonts have no Devanagari in any game language, and a line nobody can draw is worse
    /// than no line. See GameCanDraw for Chinese, which is the other font problem.
    /// </summary>
    internal enum Language
    {
        English,
        EnglishUS,
        PortugueseBR,
        Spanish,
        French,
        German,
        Russian,
        Polish,
        ChineseSimplified,
        Hindi
    }

    /// <summary>
    /// What the player reads, in the language they asked for.
    ///
    /// THE ENGLISH IS THE KEY. Every string in the code stays exactly as written and is
    /// looked up, at the moment it is drawn, in a table loaded from
    /// scripts\Hoodrich\lang\&lt;code&gt;.json. A string the table does not have comes back as
    /// itself, so a translation that covers half the mod shows English for the other half
    /// rather than blanks or keys -- and a new setting added in English is simply English
    /// until somebody translates it. Nothing is ever missing; some of it is just not yet
    /// translated.
    ///
    /// That is also why the table is a data file and not code: a translator edits a json
    /// with the English on the left and their language on the right, and never needs the
    /// source, the compiler or a new dll. Same design as Fumes, same file shape, so one
    /// translator can do the whole set.
    ///
    /// LOOKED UP AT DRAW TIME, not when a screen is built, so switching the language on the
    /// settings screen changes the screen you are looking at, including the row you switched
    /// it on.
    ///
    /// GLUED STRINGS ARE HANDLED, CAREFULLY. A lot of the mod's notices are built by
    /// concatenation -- "Paid " + money + " to Gerald" -- so a whole-string match misses
    /// them. When the whole string is not in the table, keys that are GLUE -- ones that
    /// start or end with a space or a colon, which only a fragment does, or on an edge only a
    /// fragment has, such as a "$" with the money still to come (see IsGlue) -- are swapped out
    /// wherever they occur on a word boundary, longest first, and everything else is copied
    /// through. Keys that are whole words are NOT scanned for inside other strings: this mod
    /// is mostly dialogue, dialogue is not translated, and a title like "You" or "Cash"
    /// firing inside "You owe me cash" would turn every conversation into two languages at
    /// once. Fumes scans every key; its text is short enough to get away with it.
    /// </summary>
    internal static class Lang
    {
        private static Language _language = Language.English;
        private static Dictionary<string, string> _table;

        /// <summary>The glue keys, longest first, for the scan in Resolve.</summary>
        private static List<string> _glue;

        /// <summary>
        /// Glue too ordinary to trust on its own -- " now.", " a week.", " short." -- because
        /// the same words turn up inside dialogue, which is never translated. A tail is only
        /// swapped inside a string where some glue that is NOT a tail has matched too, which
        /// is to say inside a notice the mod built and never inside a line somebody says.
        /// Without them every such notice came out in two languages: "c'est l'embrouille avec
        /// Ballas now." make_langs.py lists them, from every line of dialogue in the mod.
        /// </summary>
        private static HashSet<string> _tails;

        /// <summary>Whole strings already resolved once, so a label drawn every frame costs a lookup, not a scan.</summary>
        private static readonly Dictionary<string, string> _memo = new Dictionary<string, string>();

        private const int MemoCap = 2000;

        public static Language Current => _language;

        /// <summary>Every language, in enum order, as the settings row shows them.</summary>
        public static string[] Names
        {
            get
            {
                var all = (Language[])Enum.GetValues(typeof(Language));
                var names = new string[all.Length];
                for (var i = 0; i < all.Length; i++) names[i] = NameOf(all[i]);
                return names;
            }
        }

        /// <summary>Every language, in enum order, as the ini spells them.</summary>
        public static string[] Codes
        {
            get
            {
                var all = (Language[])Enum.GetValues(typeof(Language));
                var codes = new string[all.Length];
                for (var i = 0; i < all.Length; i++) codes[i] = all[i].ToString();
                return codes;
            }
        }

        /// <summary>The file a language reads from, or null for English, which needs none.</summary>
        public static string FileFor(Language language)
        {
            switch (language)
            {
                case Language.EnglishUS: return "en-US.json";
                case Language.PortugueseBR: return "pt-BR.json";
                case Language.Spanish: return "es.json";
                case Language.French: return "fr.json";
                case Language.German: return "de.json";
                case Language.Russian: return "ru.json";
                case Language.Polish: return "pl.json";
                case Language.ChineseSimplified: return "zh-CN.json";
                case Language.Hindi: return "hi.json";
                default: return null;
            }
        }

        /// <summary>
        /// How the language names itself on the settings row.
        ///
        /// Chinese names itself in Chinese only when the game can draw Chinese. Otherwise the
        /// row would read as a run of boxes -- the one row that has to stay legible, because
        /// it is the way back out.
        /// </summary>
        public static string NameOf(Language language)
        {
            switch (language)
            {
                case Language.EnglishUS: return "ENGLISH (US)";
                case Language.PortugueseBR: return "PORTUGUÊS (BR)";
                case Language.Spanish: return "ESPAÑOL";
                case Language.French: return "FRANÇAIS";
                case Language.German: return "DEUTSCH";
                case Language.Russian: return "РУССКИЙ";
                case Language.Polish: return "POLSKI";
                case Language.ChineseSimplified: return GameCanDraw(language) ? "中文（简体）" : "CHINESE (SIMPLIFIED)";
                case Language.Hindi: return "HINDI (LATIN)";
                default: return "ENGLISH (UK)";
            }
        }

        /// <summary>
        /// Whether the game's own font can draw this language at all.
        ///
        /// GTA V loads Latin and Cyrillic glyphs whatever language it is set to, and loads
        /// CJK glyphs ONLY when it is set to a CJK language: on an English game every Chinese
        /// character draws as a box, including the settings row you would use to switch back.
        /// So Chinese is only offered for real when the game itself is in Chinese; otherwise
        /// the setting is kept, English is shown, and the reason is said once in a language
        /// that renders.
        /// </summary>
        public static bool GameCanDraw(Language language)
        {
            if (language != Language.ChineseSimplified) return true;

            try
            {
                var game = GTA.Game.Language;
                return game == GTA.Language.Chinese || game == GTA.Language.ChineseSimplified;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Switches language. English drops the table; anything else loads its file, and
        /// a file that is missing or malformed leaves the mod in English and says so once.
        /// </summary>
        public static void Use(Language language)
        {
            _language = language;
            _table = null;
            _glue = null;
            _tails = null;
            _memo.Clear();

            var file = FileFor(language);
            if (file == null) return;

            if (!GameCanDraw(language))
            {
                // The setting stays as chosen -- it is written to the ini and works the day
                // the game is switched -- but nothing is loaded, so every string on screen is
                // the English the font can draw.
                Log.Warn(language + " needs the game itself set to that language; its glyphs are not in the " +
                         "font otherwise. Showing English.");
                // THROUGH NOTIFY, NOT THE WRAPPER. A method reference the running loader
                // does not have fails when THIS method is compiled, whichever branch it sits
                // in -- so this one line, which only ever speaks to somebody playing in
                // Chinese, took the whole of Lang.Use down for a player on English with an
                // older ScriptHookVDotNet. Notify posts through natives now. See Notify.Feed.
                try
                {
                    UI.Notify.Ticker("~y~Chinese needs GTA V itself set to Chinese~s~ - " +
                                     "its characters are not in the font otherwise. Showing English.");
                }
                catch { /* the log has it */ }
                return;
            }

            try
            {
                var path = Path.Combine(Paths.Lang, file);
                var doc = JsonFile.Read(path);

                if (doc == null)
                {
                    Log.Warn("No usable " + file + " in " + Paths.Lang + " - staying in English.");
                    _language = Language.English;
                    return;
                }

                var strings = doc["strings"];
                var table = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var key in strings.Keys)
                {
                    var value = strings[key].AsString(null);
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value) || key == value) continue;
                    table[key] = value;
                }

                // The glue: keys that begin or end with a space or a colon -- or on one of the
                // other edges IsGlue knows -- are fragments by construction, and only fragments
                // are looked for inside other strings. Nothing
                // under three characters: a two-letter key is a coincidence waiting to happen.
                var glue = new List<string>();
                foreach (var key in table.Keys)
                {
                    if (key.Length < 3) continue;
                    if (IsGlue(key)) glue.Add(key);
                }
                glue.Sort((a, b) => b.Length.CompareTo(a.Length));

                var tails = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in doc["tails"].AsStringList())
                {
                    if (table.ContainsKey(key)) tails.Add(key);
                }

                _table = table;
                _glue = glue;
                _tails = tails;

                Log.Info(doc["language"].AsString(language.ToString()) + " loaded from " + file + ": " +
                         table.Count + " string(s), " + glue.Count + " of them glue, " + tails.Count + " of those tails" +
                         (doc.Has("by") ? ", by " + doc["by"].AsString("") : "") + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Could not load " + file + " - staying in English.", ex);
                _table = null;
                _glue = null;
                _tails = null;
                _language = Language.English;
            }
        }

        private static bool IsGlue(string key)
        {
            // A fragment starts or ends where a word does not: ". Sit tight." and "Paid "
            // are glue; "Paid" and "Sit tight." are whole strings and stay that way.
            //
            // SOME EDGES ONLY EVER HAVE SOMETHING GLUED ON. A "$" is always followed by the
            // money, an opening colour ("Rent on ~g~") by the name it colours, and "~s~" or
            // "'s " only ever close a coloured name. Without these, "you're $" + cost +
            // " short." and "~y~" + name + "~s~ is on your map now." were translated on paper
            // and English in every language in the game.
            var first = key[0];
            var last = key[key.Length - 1];
            if (last == ' ' || last == ':' || last == '$' || last == '~') return true;
            if (key.StartsWith("~s~", StringComparison.Ordinal) || key.StartsWith("'s ", StringComparison.Ordinal)) return true;
            return !char.IsLetterOrDigit(first) && first != '~' && first != '$' && first != '(' && first != '"' && first != '\'';
        }

        /// <summary>The string in the current language, or itself when there is no translation.</summary>
        public static string T(string english)
        {
            if (_table == null || string.IsNullOrEmpty(english)) return english;

            string hit;
            if (_memo.TryGetValue(english, out hit)) return hit;

            hit = Resolve(english);

            if (_memo.Count >= MemoCap) _memo.Clear();
            _memo[english] = hit;
            return hit;
        }

        private static string Resolve(string english)
        {
            string whole;
            if (_table.TryGetValue(english, out whole)) return whole;

            var bare = Untagged(english);
            if (bare != null) return bare;

            if (_glue == null || _glue.Count == 0) return english;

            // NOT A WHOLE STRING, SO IT WAS GLUED TOGETHER. Walk it; at each position take the
            // longest GLUE key that starts there, on a word boundary, and copy anything nothing
            // matches -- the names, the numbers, the money -- through untouched. The result
            // only counts if something other than a tail matched, or the string is a bare
            // readout: see _tails and IsReadout.
            var sb = new System.Text.StringBuilder(english.Length + 32);
            var loose = new System.Text.StringBuilder();
            var i = 0;
            var anchored = false;
            var tailed = false;

            while (i < english.Length)
            {
                string hit = null;

                var atBoundary = i == 0 || !char.IsLetterOrDigit(english[i - 1]) || !char.IsLetterOrDigit(english[i]);

                if (atBoundary)
                {
                    foreach (var key in _glue)          // longest first
                    {
                        if (key.Length > english.Length - i) continue;
                        if (string.CompareOrdinal(english, i, key, 0, key.Length) != 0) continue;

                        var end = i + key.Length;
                        if (end < english.Length && char.IsLetterOrDigit(english[end]) &&
                            char.IsLetterOrDigit(key[key.Length - 1])) continue;

                        hit = key;
                        break;
                    }
                }

                if (hit != null)
                {
                    sb.Append(_table[hit]);
                    i += hit.Length;
                    if (_tails == null || !_tails.Contains(hit)) anchored = true;
                    else tailed = true;
                    loose.Append(' ');
                }
                else
                {
                    sb.Append(english[i]);
                    loose.Append(english[i]);
                    i++;
                }
            }

            return anchored || tailed && IsReadout(loose) ? sb.ToString() : english;
        }

        /// <summary>
        /// Whether what no key matched is only numbers, signs and colour codes -- the "12" of
        /// "12 down", the "3" and "5" of "3 of 5", the "20g" of "20g off you". A counter on the
        /// HUD is built like that and a line of dialogue never is, so its tails may be swapped
        /// with nothing else to vouch for them. A unit letter on a number is allowed; two
        /// letters together are a word.
        /// </summary>
        private static bool IsReadout(System.Text.StringBuilder loose)
        {
            var run = 0;
            var inCode = false;

            for (var i = 0; i < loose.Length; i++)
            {
                var c = loose[i];
                if (c == '~')
                {
                    inCode = !inCode;
                    run = 0;
                    continue;
                }
                if (inCode) continue;

                run = char.IsLetter(c) ? run + 1 : 0;
                if (run >= 2) return false;
            }
            return true;
        }

        /// <summary>
        /// The whole-string translation of a string with its opening colour codes set aside,
        /// or null.
        ///
        /// Colour codes are not part of a string's meaning. "~y~Paid~s~" is "Paid~s~" to a
        /// translator, and the game's own tags are the same in every language -- so a string
        /// that starts with one is looked up without it, and gets it back.
        ///
        /// WHOLE STRINGS ONLY. This used to run the glue scan on what was left as well, and
        /// return the moment that changed anything -- so a coloured opener such as
        /// "~g~Posted up.~s~ Moving " never got its turn whenever a later piece of the same
        /// notice was glue. The scan in Resolve covers the whole string, colours and all.
        /// </summary>
        private static string Untagged(string english)
        {
            if (english.Length <= 3 || english[0] != '~') return null;

            var close = english.IndexOf('~', 1);
            if (close <= 0 || close >= 6) return null;

            var tag = english.Substring(0, close + 1);
            var rest = english.Substring(close + 1);

            string whole;
            if (_table.TryGetValue(rest, out whole)) return tag + whole;

            var inner = Untagged(rest);
            return inner == null ? null : tag + inner;
        }
    }
}
