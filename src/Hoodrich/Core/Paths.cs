using System;
using System.IO;
using System.Reflection;

namespace Hoodrich.Core
{
    /// <summary>
    /// Resolves where Hoodrich reads and writes. Everything hangs off the folder the
    /// dll was loaded from (the game's scripts\ folder), never off the working directory,
    /// which SHVDN does not guarantee.
    /// </summary>
    internal static class Paths
    {
        private static string _scripts;

        /// <summary>The game's scripts\ folder.</summary>
        public static string Scripts
        {
            get
            {
                if (_scripts != null) return _scripts;

                string dir = null;
                try
                {
                    var loc = Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(loc)) dir = Path.GetDirectoryName(loc);
                }
                catch
                {
                    // Assembly loaded from bytes; fall through to the CWD probe.
                }

                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    var cwd = Directory.GetCurrentDirectory();
                    var probe = Path.Combine(cwd, "scripts");
                    dir = Directory.Exists(probe) ? probe : cwd;
                }

                _scripts = dir;
                return _scripts;
            }
        }

        /// <summary>scripts\Hoodrich\ — the shipped data files. Read-only as far as we care.</summary>
        public static string Data
        {
            get
            {
                var d = Path.Combine(Scripts, "Hoodrich");
                EnsureDir(d);
                return d;
            }
        }

        private static string _writable;

        /// <summary>
        /// Where the log and the save go.
        ///
        /// The game is normally installed under Program Files, which is NOT writable by an
        /// unelevated process -- and GTA5.exe is unelevated. Reads work fine, so the shipped
        /// data files load, but every write silently fails: no log, and no save. Rather than
        /// demand the player run the game as admin or move their install, fall back to
        /// Documents the moment the game folder proves unwritable.
        /// </summary>
        public static string Writable
        {
            get
            {
                if (_writable != null) return _writable;

                var preferred = Path.Combine(Scripts, "Hoodrich");
                if (IsWritable(preferred))
                {
                    _writable = preferred;
                    return _writable;
                }

                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hoodrich");

                try
                {
                    if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback);
                }
                catch
                {
                    // If even Documents is out, fall back to temp so nothing throws upstream.
                    fallback = Path.Combine(Path.GetTempPath(), "Hoodrich");
                    try { if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback); }
                    catch { /* nothing left to try */ }
                }

                _writable = fallback;
                return _writable;
            }
        }

        /// <summary>True when a real file can actually be created here.</summary>
        private static bool IsWritable(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var probe = Path.Combine(dir, ".hoodrich_write_test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string Gangs
        {
            get
            {
                var d = Path.Combine(Data, "Gangs");
                EnsureDir(d);
                return d;
            }
        }

        public static string Ini => Path.Combine(Scripts, "Hoodrich.ini");

        // Both of these are WRITTEN, so they follow the writability fallback rather than
        // sitting next to the dll.
        public static string LogFile => Path.Combine(Writable, "Hoodrich.log");
        public static string SaveFile => Path.Combine(Writable, "save.json");

        private static void EnsureDir(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                Log.Error("Could not create directory " + path, ex);
            }
        }
    }
}
