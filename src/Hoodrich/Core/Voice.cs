using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Hoodrich.Core
{
    /// <summary>
    /// Spoken dialogue over the conversation screen.
    ///
    /// MCI RATHER THAN SoundPlayer, and that decision picks the file format rather than the
    /// other way round. System.Media.SoundPlayer ships with the framework and costs nothing to
    /// use, but it plays WAV only and gives you no volume and no stop -- so a line could not be
    /// turned down, could not be ducked, and would carry on talking over an empty screen after
    /// the player walked away. winmm's MCI is one DllImport, needs no assembly reference, and
    /// gives all three back. It also plays MP3, which is what makes the whole thing shippable:
    /// the mod's dialogue is about five hours of speech, which is a gigabyte and a half as WAV
    /// and two hundred megabytes as MP3.
    ///
    /// NAMED BY WHAT IS SAID, not by an index. A file is speaker plus a hash of the exact line,
    /// so nothing has to be kept in step by hand and there is no numbering to renumber. It also
    /// fails in the right direction: rewrite a line and its old audio simply stops matching, so
    /// the screen goes quiet rather than playing the previous wording over the new text. Silence
    /// is a missing recording; the wrong words would be a bug nobody could explain.
    ///
    /// LINES BUILT AT RUNTIME CANNOT MATCH, and that is expected. Anything with a price, a name
    /// or a count in it hashes differently every time it is spoken, so it stays text-only. The
    /// set pieces -- the tryout, the docks, the mission briefs -- are fixed text and those are
    /// the ones worth recording anyway.
    ///
    /// NOTHING IN HERE MAY THROW. It is decoration on top of a screen that has to keep working
    /// on a machine with no sound card, no codec and no files, so every path swallows and goes
    /// quiet.
    /// </summary>
    internal static class Voice
    {
        /// <summary>Whether to speak at all. Off is the same as having no files.</summary>
        public static bool Enabled = true;

        /// <summary>0..1. MCI's own scale is 0..1000 and is set per alias, after opening.</summary>
        public static float Volume = 0.9f;

        /// <summary>
        /// Whether a line you have already heard plays again.
        ///
        /// Off, because a recording is a performance rather than a sound effect: the first time
        /// he explains the business it is worth hearing, and the fourth time you open that
        /// branch to check one sentence it is a speech being shouted at somebody who is
        /// skim-reading.
        ///
        /// ON IS FOR RECORDING. Replacing a take does not change the line, so it does not change
        /// the key, so a save that has already heard the old take will never play the new one.
        /// Turn this on while working and every line speaks every time.
        /// </summary>
        public static bool Repeat;

        /// <summary>
        /// Set by Main: whether this save has heard a key, and a note that it now has.
        ///
        /// Delegates rather than a reference to the save, because this class is one step above
        /// winmm and has no business knowing what a PlayerState is. Both are null-checked, so
        /// with nothing wired up every line simply plays -- which is the right answer for a
        /// screen opened before the save has loaded.
        /// </summary>
        public static Func<string, bool> Heard;
        public static Action<string> Remember;

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
        private static extern int mciSendString(string command, StringBuilder ret,
                                                int retLength, IntPtr callback);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
        private static extern bool mciGetErrorString(int error, StringBuilder ret, int retLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW",
                   SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, int flags);

        /// <summary>
        /// What MCI actually objected to, in words.
        ///
        /// Worth the extra import. "It would not open" is the same message whether the file is
        /// missing, the path is mangled, the device is not registered on this Windows, or the
        /// encoding is one the decoder will not take -- and those want four different fixes.
        /// </summary>
        private static string Why(int code)
        {
            try
            {
                var buf = new StringBuilder(256);
                return mciGetErrorString(code, buf, buf.Capacity)
                    ? buf.ToString()
                    : "MCI error " + code;
            }
            catch
            {
                return "MCI error " + code;
            }
        }

        private static bool _primed;

        /// <summary>
        /// Everything the MPEG driver needs that a game process does not necessarily give it.
        ///
        /// THE DRIVER IS NOT MISSING. mciqtz32.dll is present, MPEGVideo is registered, and the
        /// very same open on the very same file succeeds from an ordinary sixty-four bit
        /// process. It fails only inside GTA -- so this is about the process, not the machine,
        /// and there are two things a host process can take away that MCI quietly needs.
        ///
        /// COM, because MPEGVideo is a DirectShow wrapper and DirectShow cannot build a graph on
        /// a thread with no apartment. WaveAudio needs none of that, which is exactly why WAV
        /// would work where MP3 does not. Asked for apartment-threaded first and multi-threaded
        /// if the thread is already committed the other way, because refusing to change an
        /// existing apartment is a normal answer and not a failure.
        ///
        /// And the DLL by ABSOLUTE PATH, because MCI loads its driver with a bare name. A host
        /// that has narrowed the library search path -- which games do, deliberately -- turns
        /// that bare name into nothing, while the identical call from a normal process resolves
        /// it from System32. Loading it once by full path puts the module in the process, and
        /// MCI's own by-name load then finds it already there.
        ///
        /// Both are logged. One game cycle should say which of them it was, and if it was
        /// neither, the WAV fallback below says that instead.
        /// </summary>
        private static void Prime()
        {
            if (_primed) return;
            _primed = true;

            try
            {
                // 2 = apartment threaded. 0x80010106 is RPC_E_CHANGED_MODE: already MTA.
                var hr = CoInitializeEx(IntPtr.Zero, 2);
                if (hr == unchecked((int)0x80010106)) hr = CoInitializeEx(IntPtr.Zero, 0);

                Log.Info("Voice: COM apartment 0x" + hr.ToString("X8"));
            }
            catch (Exception ex)
            {
                Log.Info("Voice: could not set a COM apartment: " + ex.Message);
            }

            try
            {
                var dll = Path.Combine(Environment.SystemDirectory, "mciqtz32.dll");
                var h = LoadLibrary(dll);

                Log.Info("Voice: " + (h == IntPtr.Zero
                    ? "mciqtz32.dll would NOT load from " + dll
                    : "mciqtz32.dll preloaded"));
            }
            catch (Exception ex)
            {
                Log.Info("Voice: could not preload the MPEG driver: " + ex.Message);
            }
        }

        /// <summary>
        /// The alias currently open, or null.
        ///
        /// ONE AT A TIME, AND ALWAYS CLOSED. Every open must be matched by a close or MCI keeps
        /// the device and the handle -- and after a few hundred lines it quietly stops opening
        /// new ones, which presents as "the voices stopped working after a while" and is close
        /// to impossible to diagnose from the outside.
        /// </summary>
        private static string _alias;

        /// <summary>
        /// Counted up so an alias is never reused.
        ///
        /// Closing is not instantaneous. Reopening the same name while the last one is still
        /// letting go gets an error rather than a device, which on a fast-clicked menu means
        /// the second line of dialogue is silent for no visible reason.
        /// </summary>
        private static int _next;

        /// <summary>Where the recordings live: scripts\Hoodrich\voice.</summary>
        public static string Folder => Paths.Voice;

        /// <summary>
        /// Say a line, if there is a recording of it.
        ///
        /// Returns whether anything is playing, which callers are free to ignore -- the screen
        /// draws its text either way and nothing about the conversation waits on audio.
        /// </summary>
        public static bool Say(string speaker, string line, string named = null)
        {
            Hush();

            if (!Enabled || string.IsNullOrEmpty(line)) return false;

            try
            {
                // A name the line chose beats one worked out from its words. See
                // DialogueNode.VoiceKey -- it is how a sentence with a live number in it can
                // still have a voice.
                var key = string.IsNullOrEmpty(named) ? Key(speaker, line) : named;

                // Once. The text stays on screen either way; only the performance is spent.
                if (!Repeat && Heard != null && Heard(key)) return false;

                var path = Find(key);

                // A NAMED LINE FALLS BACK TO ITS HASHED NAME. The two greetings on Lamar's
                // corner asked for lamar_hello and lamar_work, and the recordings of those
                // exact words were on disk under the hashed names the rest of the pack uses --
                // so both were silent with their audio sat a folder away. A name is a
                // convenience for the recorder, not a second pack; when it is not there, the
                // words themselves are looked up the way every other line is.
                if (path == null && !string.IsNullOrEmpty(named))
                {
                    var hashed = Key(speaker, line);
                    path = Find(hashed);
                    if (path != null) key = hashed;
                }

                if (path == null)
                {
                    // Named, so the log says what to call the file rather than making somebody
                    // work it out. This is the line to grep for when a recording does nothing.
                    // The to-record list writes itself. Play through with logging on and the
                    // log holds every line that wanted audio and did not have it, already
                    // named -- which beats trying to work the list out by reading the source,
                    // because it only ever lists lines that actually reached the screen.
                    Log.Debug("Voice: nothing for " + key + " -- " + Snip(line));
                    return false;
                }

                // AT INFO, NOT DEBUG. A recording that exists is rare -- one line per
                // conversation at most -- so this costs nothing, and it is the only way to tell
                // "the name was wrong" apart from "the codec refused it" without asking somebody
                // to go and change their log level first. Silence is this thing's only failure
                // mode; it has to be able to explain itself on the log people already have.
                Log.Info("Voice: playing " + Path.GetFileName(path));

                if (Start(path)) { Kept(key); return true; }

                // THE SAME LINE IN THE OTHER FORMAT, if the pack happens to carry it.
                //
                // MP3 goes through a DirectShow driver and WAV does not, so a machine or a host
                // process that cannot manage the first can very often still manage the second.
                // Shipping both for every line would be daft, but honouring one when it is there
                // costs nothing and turns a dead subsystem into a working one.
                var other = Other(path);

                if (other != null)
                {
                    Log.Info("Voice: trying " + Path.GetFileName(other) + " instead.");

                    if (Start(other)) { Kept(key); return true; }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Debug("Voice: could not speak: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Written down as heard, but only once it actually made a sound.
        ///
        /// A line whose file is missing is NOT heard -- otherwise recording it later would find
        /// the save already convinced you had listened to it, and it would never play at all.
        /// </summary>
        private static void Kept(string key)
        {
            try
            {
                if (Remember != null) Remember(key);
            }
            catch
            {
                // Not being able to write it down is not a reason to stop talking.
            }
        }

        /// <summary>
        /// Play a named sound outright, for something that HAPPENS rather than something said.
        ///
        /// None of Say's machinery applies here. There is no text to hash, because the caller
        /// already knows what the file is called -- and the once-only rule is deliberately
        /// skipped, because a dispatch call going out over the radio is an event, and an event
        /// that only ever happens the first time is a bug rather than a performance.
        ///
        /// Shares the one channel with dialogue, which is right: you are not stood in a
        /// conversation while a patrol car is deciding about you.
        /// </summary>
        public static bool Cue(string key)
        {
            Hush();

            if (!Enabled || string.IsNullOrEmpty(key)) return false;

            try
            {
                var path = Find(key);

                if (path == null)
                {
                    Log.Debug("Voice: no cue named " + key);
                    return false;
                }

                Log.Info("Voice: cue " + Path.GetFileName(path));
                return Start(path);
            }
            catch (Exception ex)
            {
                Log.Debug("Voice: cue failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Stop whatever is talking. Safe to call when nothing is.</summary>
        public static void Hush()
        {
            if (_alias == null) return;

            try
            {
                mciSendString("stop " + _alias, null, 0, IntPtr.Zero);
                mciSendString("close " + _alias, null, 0, IntPtr.Zero);
            }
            catch
            {
                // A device that will not close is not worth taking the screen down over.
            }

            _alias = null;
        }

        /// <summary>Whether a line is still playing right now.</summary>
        public static bool Talking
        {
            get
            {
                if (_alias == null) return false;

                try
                {
                    var buf = new StringBuilder(64);
                    mciSendString("status " + _alias + " mode", buf, buf.Capacity, IntPtr.Zero);
                    return buf.ToString().IndexOf("playing", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static bool Start(string path)
        {
            Prime();

            var alias = "hrvox" + (++_next).ToString(CultureInfo.InvariantCulture);

            // TWO WAYS IN, because neither is reliable on its own.
            //
            // Naming the device is the documented way and does not depend on the file-type
            // registry, which a stripped or locked-down Windows may not have. But MPEGVIDEO is
            // a legacy driver and is NOT registered on every Windows 11 -- where naming it is
            // the thing that fails, and letting MCI work it out from the extension is what
            // succeeds. So: name it, and if that is refused, ask again without.
            //
            // Quoted either way, because the path runs through Program Files (x86) on nearly
            // every install and an unquoted space ends the argument early.
            var file = "\"" + path + "\"";

            var kind = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                ? "waveaudio"
                : "mpegvideo";

            var err = mciSendString("open " + file + " type " + kind + " alias " + alias,
                                    null, 0, IntPtr.Zero);

            if (err != 0)
            {
                Log.Info("Voice: MCI refused " + Path.GetFileName(path) + " as " + kind +
                         " -- " + Why(err) + ". Trying it without a device.");

                err = mciSendString("open " + file + " alias " + alias, null, 0, IntPtr.Zero);
            }

            if (err != 0)
            {
                Log.Info("Voice: MCI would not open " + Path.GetFileName(path) +
                         " -- " + Why(err));
                return false;
            }

            _alias = alias;

            var vol = (int)Math.Round(Clamp(Volume, 0f, 1f) * 1000f);
            mciSendString("setaudio " + alias + " volume to " +
                          vol.ToString(CultureInfo.InvariantCulture), null, 0, IntPtr.Zero);

            // No "wait" -- that would block the script thread for the length of the line and
            // freeze the game while somebody talks.
            var played = mciSendString("play " + alias, null, 0, IntPtr.Zero);

            if (played != 0)
            {
                Log.Info("Voice: opened but would not play " + Path.GetFileName(path) +
                         " -- " + Why(played));
                Hush();
                return false;
            }

            return true;
        }

        /// <summary>The same recording in the other format, if the pack ships one.</summary>
        private static string Other(string path)
        {
            try
            {
                var swap = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(path, ".wav")
                    : Path.ChangeExtension(path, ".mp3");

                return File.Exists(swap) ? swap : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>An .mp3 if there is one, an .wav if there is not, otherwise null.</summary>
        private static string Find(string key)
        {
            var dir = Folder;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            var mp3 = Path.Combine(dir, key + ".mp3");
            if (File.Exists(mp3)) return mp3;

            var wav = Path.Combine(dir, key + ".wav");
            return File.Exists(wav) ? wav : null;
        }

        /// <summary>
        /// The filename for a line, without extension: "gerald_1f3c9a20".
        ///
        /// FNV-1a over the UTF-8 bytes, and deliberately not string.GetHashCode, which is
        /// randomised per process on modern runtimes and would name the same line differently
        /// every time the game started. This one is eight lines of arithmetic that any tool in
        /// any language can reproduce exactly, which is the whole point -- the script that
        /// generates the recording list has to agree with this to the character.
        /// </summary>
        /// <summary>
        /// A name a line picks for itself, scoped to whoever says it.
        ///
        /// For the lines a hash can never reach -- the ones carrying a price, a count, or how
        /// much room is left at the house. The words differ every telling, so there is nothing
        /// stable to hash, but the LINE is still the same line and the man saying it is still
        /// the same man.
        ///
        /// Scoped by speaker because the screens that need this are shared: every dealer says
        /// the same sentence with their own numbers in it, so "shop" alone would have them all
        /// fighting over one recording. tao_cheng_shop and merle_shop are different men saying
        /// the same words, which is exactly right.
        /// </summary>
        public static string Named(string speaker, string tag)
        {
            return Slug(speaker) + "_" + tag;
        }

        public static string Key(string speaker, string line)
        {
            return Slug(speaker) + "_" + Hash(Tidy(line)).ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The text as it is spoken, with the things that are not words taken out.
        ///
        /// Colour tags and control-button placeholders are layout, not speech, and leaving them
        /// in would make the same sentence hash differently depending on whether it happened to
        /// be drawn green that time.
        /// </summary>
        private static string Tidy(string line)
        {
            var sb = new StringBuilder(line.Length);
            var skipping = false;

            foreach (var c in line)
            {
                if (c == '~') { skipping = !skipping; continue; }
                if (skipping) continue;

                sb.Append(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
            }

            // Collapse runs of spaces so a reflowed line still matches its recording.
            var text = sb.ToString();
            var outp = new StringBuilder(text.Length);
            var space = false;

            foreach (var c in text)
            {
                if (c == ' ')
                {
                    if (!space) outp.Append(c);
                    space = true;
                }
                else
                {
                    outp.Append(c);
                    space = false;
                }
            }

            return outp.ToString().Trim();
        }

        private static uint Hash(string text)
        {
            unchecked
            {
                var h = 2166136261u;

                foreach (var b in Encoding.UTF8.GetBytes(text))
                {
                    h ^= b;
                    h *= 16777619u;
                }

                return h;
            }
        }

        private static string Slug(string name)
        {
            if (string.IsNullOrEmpty(name)) return "someone";

            var sb = new StringBuilder(name.Length);

            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }

            return sb.ToString().Trim('_');
        }

        private static string Snip(string line)
        {
            var one = Tidy(line);
            return one.Length <= 60 ? one : one.Substring(0, 57) + "...";
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
