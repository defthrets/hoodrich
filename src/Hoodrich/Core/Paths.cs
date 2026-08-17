using System;
using System.Collections.Generic;
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

        /// <summary>
        /// The game's scripts\ folder.
        ///
        /// Assembly.Location is NOT usable here: SHVDN shadow-copies scripts into the .NET
        /// download cache, so it reports somewhere under AppData\Local\assembly\dl3. Trusting
        /// it meant the ini, the drug catalogue and the weapon table were all silently "not
        /// found" while the mod ran happily on built-in defaults.
        ///
        /// So instead of trusting any one API, several candidates are tested against the files
        /// we know we shipped, and the first that actually holds them wins.
        /// </summary>
        public static string Scripts
        {
            get
            {
                if (_scripts != null) return _scripts;

                var candidates = new List<string>();

                // SHVDN builds its script AppDomain with the scripts folder as the base.
                TryAdd(candidates, SafeGet(() => AppDomain.CurrentDomain.BaseDirectory));

                var cwd = SafeGet(Directory.GetCurrentDirectory);
                if (!string.IsNullOrEmpty(cwd))
                {
                    TryAdd(candidates, Path.Combine(cwd, "scripts"));
                    TryAdd(candidates, cwd);
                }

                // Last resort, and only because an unshadowed load would still be correct.
                TryAdd(candidates, SafeGet(() =>
                {
                    var loc = Assembly.GetExecutingAssembly().Location;
                    return string.IsNullOrEmpty(loc) ? null : Path.GetDirectoryName(loc);
                }));

                // Prefer wherever our files actually are.
                foreach (var dir in candidates)
                {
                    if (LooksLikeOurFolder(dir)) { _scripts = dir; return _scripts; }
                }

                _scripts = candidates.Count > 0 ? candidates[0] : cwd ?? ".";
                return _scripts;
            }
        }

        /// <summary>True when this folder holds the files the deploy puts down.</summary>
        private static bool LooksLikeOurFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                if (File.Exists(Path.Combine(dir, "Hoodrich.ini"))) return true;

                var data = Path.Combine(dir, "Hoodrich");
                return Directory.Exists(data) &&
                       (File.Exists(Path.Combine(data, "weapons.json")) ||
                        File.Exists(Path.Combine(data, "gangs.json")));
            }
            catch
            {
                return false;
            }
        }

        private static void TryAdd(List<string> list, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;

            try
            {
                dir = Path.GetFullPath(dir.TrimEnd(Path.DirectorySeparatorChar));
                if (Directory.Exists(dir) && !list.Contains(dir)) list.Add(dir);
            }
            catch
            {
                // Unusable path; skip it.
            }
        }

        private static string SafeGet(Func<string> get)
        {
            try { return get(); }
            catch { return null; }
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
