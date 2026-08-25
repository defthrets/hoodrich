using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hoodrich.Core;

namespace Hoodrich.Voice
{
    /// <summary>
    /// Which file says which line.
    ///
    /// NO HASH, AND NO CONTRACT BETWEEN TWO LANGUAGES. The plan this came from keyed the
    /// lookup on a SHA1 of the speaker and the line, computed identically by a Python
    /// extractor and by the mod, and warned that the two implementations had to agree
    /// byte-for-byte forever. That is a real cost: two normalisers that must never drift, in
    /// two languages, neither of which can see the other.
    ///
    /// So the manifest carries the SPEAKER AND THE TEXT, in the clear, and the mod normalises
    /// both sides itself with the one piece of code below. The generator does not need to
    /// know how matching works -- it only has to write down what it recorded. There is one
    /// normaliser, in one language, and it is impossible for the two ends to disagree because
    /// there is only one end.
    ///
    /// It also makes the files readable. gerald_0007.wav instead of a1b2c3d4e5f6.wav is the
    /// difference between finding a bad take in a second and grepping a manifest for it.
    ///
    /// And the casing trap goes with it. Delivery uppercases its speaker before the subtitle,
    /// so the key built at runtime was "TAO CHENG" while the catalogue said "Tao Cheng";
    /// matching case-insensitively means nobody has to remember that.
    /// </summary>
    internal sealed class VoiceIndex
    {
        private readonly Dictionary<string, string> _byLine =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Lines the manifest names that are not actually on disk.</summary>
        public int Missing { get; private set; }

        /// <summary>How many lines can be spoken.</summary>
        public int Count => _byLine.Count;

        /// <summary>
        /// Reads the manifest and keeps only the lines whose audio is really there.
        ///
        /// Checked at load rather than at play time, because a missing file discovered mid
        /// conversation is a silent line and a puzzled player, while one discovered here is a
        /// number in the log before anybody has spoken.
        /// </summary>
        public bool Load(string manifestPath, string audioDir)
        {
            _byLine.Clear();
            Missing = 0;

            try
            {
                if (!File.Exists(manifestPath))
                {
                    Log.Info("Voice: no manifest at " + manifestPath + "; dialogue stays silent.");
                    return false;
                }

                var doc = JsonFile.Read(manifestPath);
                var list = doc["lines"];

                if (list.Kind != JsonKind.Array)
                {
                    Log.Warn("Voice: manifest has no 'lines' array; dialogue stays silent.");
                    return false;
                }

                foreach (var node in list.Items)
                {
                    var speaker = node["speaker"].AsString("");
                    var text = node["text"].AsString("");
                    var file = node["file"].AsString("");

                    if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(file)) continue;

                    var full = Path.Combine(audioDir, file);

                    if (!File.Exists(full)) { Missing++; continue; }

                    // A BLANK SPEAKER MEANS ANYBODY.
                    //
                    // Thirty-nine of this mod's lines live in LeaderTalk, which is written
                    // once and served by whichever of the nine leaders you happen to be stood
                    // in front of -- the speaker is not knowable until runtime. Recording them
                    // per leader is nine times the work for lines most players hear from one
                    // man, so a manifest entry with no speaker is matched on the words alone
                    // and will do for all of them.
                    //
                    // Named entries still win. Record a line for Gerald specifically and
                    // Gerald gets that take; everybody else falls through to the general one.
                    var key = Key(speaker, text);
                    if (key.Length == 0) continue;

                    // First one wins. A duplicate line in the manifest is a mistake in the
                    // generator, not a decision the mod should be making at runtime.
                    if (!_byLine.ContainsKey(key)) _byLine[key] = full;
                }

                Log.Info("Voice: " + _byLine.Count + " lines ready" +
                         (Missing > 0 ? ", " + Missing + " named but not on disk" : "") + ".");

                return _byLine.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error("Voice: manifest unreadable; dialogue stays silent.", ex);
                return false;
            }
        }

        /// <summary>
        /// The file for this line, or null.
        ///
        /// His own take first, then anybody's. See the note in Load about blank speakers --
        /// a line written once and served by nine different men can be recorded once.
        /// </summary>
        public string Find(string speaker, string text)
        {
            if (_byLine.Count == 0) return null;

            string path;

            if (!string.IsNullOrEmpty(speaker))
            {
                var mine = Key(speaker, text);
                if (mine.Length > 0 && _byLine.TryGetValue(mine, out path)) return path;
            }

            var anyone = Key("", text);
            return anyone.Length > 0 && _byLine.TryGetValue(anyone, out path) ? path : null;
        }

        /// <summary>
        /// What a line looks like once everything that is not the words is taken off it.
        ///
        /// The one place matching is decided, used for the manifest at load and for the live
        /// line at play, so the two cannot possibly disagree.
        ///
        /// Colour codes go, because the same sentence is written with and without them
        /// depending on the screen. Whitespace collapses, because a line broken across three
        /// source lines has whatever indentation the file had. Case goes, because one caller
        /// shouts the speaker's name. What is left is the words, and the words are the thing
        /// somebody read into a microphone.
        /// </summary>
        public static string Key(string speaker, string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var s = new StringBuilder(text.Length + 24);

            if (!string.IsNullOrEmpty(speaker))
            {
                Clean(speaker, s);
                s.Append(Separator);
            }

            Clean(text, s);
            return s.ToString();
        }

        /// <summary>
        /// Between the speaker and the words, and it is a character no line can contain.
        ///
        /// A plain colon or a space would let "Gerald: come see me" collide with a speaker
        /// called "Gerald" saying "come see me" -- unlikely, and unlikely is not a reason to
        /// leave a hole in a key. Unit separator, which nobody has ever typed into dialogue.
        /// </summary>
        private const char Separator = '\u001f';

        private static void Clean(string src, StringBuilder into)
        {
            var space = false;
            var start = into.Length;

            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];

                // ~y~, ~s~, ~HUD_COLOUR_...~ and friends. Everything between a pair of tildes
                // is an instruction to the renderer and was never spoken.
                if (c == '~')
                {
                    var close = src.IndexOf('~', i + 1);
                    if (close > i && close - i <= 24) { i = close; continue; }
                }

                if (char.IsWhiteSpace(c)) { space = into.Length > start; continue; }

                if (space) { into.Append(' '); space = false; }

                into.Append(char.ToLowerInvariant(c));
            }
        }
    }
}
