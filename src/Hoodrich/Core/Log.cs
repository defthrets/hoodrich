using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hoodrich.Core
{
    internal enum LogLevel
    {
        Error = 0,
        Warn = 1,
        Info = 2,
        Debug = 3
    }

    /// <summary>
    /// File logger for scripts\Hoodrich.log.
    ///
    /// Every method swallows its own exceptions. A logger that can throw will take the
    /// whole script down inside a Tick handler, which is exactly when you most need the log.
    /// </summary>
    internal static class Log
    {
        private const long MaxBytes = 2 * 1024 * 1024;

        private static readonly object Gate = new object();
        private static bool _started;

        public static LogLevel Level = LogLevel.Info;

        public static void Error(string message, Exception ex = null) => Write(LogLevel.Error, message, ex);
        public static void Warn(string message) => Write(LogLevel.Warn, message, null);
        public static void Info(string message) => Write(LogLevel.Info, message, null);
        public static void Debug(string message) => Write(LogLevel.Debug, message, null);

        private static void Write(LogLevel level, string message, Exception ex)
        {
            if (level > Level) return;

            try
            {
                lock (Gate)
                {
                    var path = Paths.LogFile;
                    if (!_started)
                    {
                        RollIfLarge(path);
                        _started = true;
                        AppendLine(path, "");
                        AppendLine(path, "=== " + Build.Name + " " + Build.Version + " by " + Build.By + " started " +
                                         DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ===");
                    }

                    var sb = new StringBuilder();
                    sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("] ");
                    sb.Append(level.ToString().ToUpperInvariant().PadRight(5)).Append(' ');
                    sb.Append(message);
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.Append("    ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                        if (!string.IsNullOrEmpty(ex.StackTrace))
                        {
                            sb.AppendLine();
                            sb.Append(ex.StackTrace);
                        }
                        if (ex.InnerException != null)
                        {
                            sb.AppendLine();
                            sb.Append("    inner: ").Append(ex.InnerException.GetType().Name)
                              .Append(": ").Append(ex.InnerException.Message);
                        }
                    }

                    AppendLine(path, sb.ToString());

                    if (Due(level)) Drain();
                }
            }
            catch
            {
                // Logging must never be the reason a script dies.
            }
        }

        /// <summary>
        /// Lines waiting to be written, and when the last lot went.
        ///
        /// A LINE USED TO BE A FILE OPENED, WRITTEN AND CLOSED, on the game's thread, inside
        /// the lock. Ten thousand of them in a session, each one a round trip through the
        /// filesystem with whatever the player's antivirus does to a file that keeps being
        /// reopened bolted on the side. None of it is slow enough to see on its own and all
        /// of it is on the frame.
        ///
        /// So they queue and go out together. Every half second, or when there are enough to
        /// be worth a trip, or the moment anything at WARN or worse arrives -- because the
        /// one log that matters is the one written just before a crash, and a buffer that
        /// loses the last five lines to save five milliseconds has thrown away the only
        /// evidence there was.
        /// </summary>
        private static readonly List<string> Waiting = new List<string>();
        private static int _flushedAt;

        private const int FlushEveryMs = 500;
        private const int FlushAt = 24;

        private static void AppendLine(string path, string line)
        {
            Waiting.Add(line);
        }

        /// <summary>Writes what is waiting. Called on the clock, on a warning, and on the way out.</summary>
        public static void Flush()
        {
            lock (Gate)
            {
                Drain();
            }
        }

        private static void Drain()
        {
            if (Waiting.Count == 0) return;

            try
            {
                var sb = new StringBuilder();

                for (var i = 0; i < Waiting.Count; i++)
                {
                    sb.Append(Waiting[i]).Append(Environment.NewLine);
                }

                File.AppendAllText(Paths.LogFile, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // A log that cannot be written is not a reason to stop playing. The lines go
                // either way, or they would queue for ever on a locked file.
            }

            Waiting.Clear();

            try { _flushedAt = GTA.Game.GameTime; }
            catch { _flushedAt = 0; }
        }

        /// <summary>Whether what is waiting should go now. See Waiting.</summary>
        private static bool Due(LogLevel level)
        {
            if (level <= LogLevel.Warn) return true;
            if (Waiting.Count >= FlushAt) return true;

            try { return GTA.Game.GameTime - _flushedAt >= FlushEveryMs; }
            catch { return true; }
        }

        private static void RollIfLarge(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < MaxBytes) return;

                var old = path + ".1";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }
            catch
            {
                // A locked or unrollable log is not worth failing over.
            }
        }
    }

    /// <summary>
    /// What this thing is called, in the one place anything is allowed to ask.
    ///
    /// Name was already here and nothing used it, so every line that wanted it typed the word
    /// out instead -- and they all typed the OLD word. That is how a rename ends up half done:
    /// the screens say Posted Up, the log says Hoodrich, and somebody reading a log to work out
    /// what is wrong has to know those are the same thing.
    ///
    /// The file names are a separate matter and stay as they are. Hoodrich.dll, Hoodrich.ini,
    /// Hoodrich.log and the folder beside them are paths -- renaming those breaks every
    /// installation that exists for the sake of a word nobody reads. This is the word people
    /// read.
    /// </summary>
    internal static class Build
    {
        public const string Version = "0.8.2";
        public const string Name = "Posted Up";

        /// <summary>
        /// Whose it is, next to the version wherever the version appears.
        ///
        /// One constant rather than the word typed into each line that shows it. There are
        /// three of those already -- the ticker, the log header and the load line -- and a
        /// name spelled out in three places is a name that gets changed in two.
        /// </summary>
        public const string By = "spitmux";

    }
}
