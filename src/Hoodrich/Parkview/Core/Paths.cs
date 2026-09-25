// GENERATED -- DO NOT EDIT. This is Parkview, copied in by tools/sync-parkview.py from
// C:\projects\parkview\src\Parkview\Core\Paths.cs. Change it there; the next build overwrites this.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Hoodrich.Parkview.Core
{
    /// <summary>
    /// Where Parkview reads and writes. Everything hangs off the folder the dll was loaded
    /// from (the game's scripts\ folder), never off the working directory, which SHVDN does
    /// not guarantee -- and Assembly.Location is not usable either, because SHVDN shadow-copies
    /// scripts into the .NET download cache. Several candidates are tested against the files
    /// we know we shipped, and the first that holds them wins.
    /// </summary>
    internal static class Paths
    {
        private static string _scripts;

        public static string Scripts
        {
            get
            {
                if (_scripts != null) return _scripts;

                var candidates = new List<string>();

                TryAdd(candidates, SafeGet(() => AppDomain.CurrentDomain.BaseDirectory));

                var cwd = SafeGet(Directory.GetCurrentDirectory);
                if (!string.IsNullOrEmpty(cwd))
                {
                    TryAdd(candidates, Path.Combine(cwd, "scripts"));
                    TryAdd(candidates, cwd);
                }

                TryAdd(candidates, SafeGet(() =>
                {
                    var loc = Assembly.GetExecutingAssembly().Location;
                    return string.IsNullOrEmpty(loc) ? null : Path.GetDirectoryName(loc);
                }));

                foreach (var dir in candidates)
                {
                    if (LooksLikeOurFolder(dir)) { _scripts = dir; return _scripts; }
                }

                _scripts = candidates.Count > 0 ? candidates[0] : cwd ?? ".";
                return _scripts;
            }
        }

        private static bool LooksLikeOurFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                if (File.Exists(Path.Combine(dir, "Parkview.ini"))) return true;
                return Directory.Exists(Path.Combine(dir, "Parkview", "scenery"));
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

        /// <summary>scripts\Parkview\ -- the shipped data, and where the log and captures go.</summary>
        public static string Data
        {
            get
            {
                var d = Path.Combine(Scripts, "Parkview");
                EnsureDir(d);
                return d;
            }
        }

        private static string _writable;

        /// <summary>
        /// Where the log goes. The game is usually under Program Files, which an unelevated
        /// process cannot write to; the data folder is tried first and Documents\Parkview is
        /// the fallback, so there is always a log somewhere.
        /// </summary>
        public static string Writable
        {
            get
            {
                if (_writable != null) return _writable;

                var preferred = Data;

                if (IsWritable(preferred))
                {
                    _writable = preferred;
                    return _writable;
                }

                var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Parkview");

                try { if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback); }
                catch
                {
                    fallback = Path.Combine(Path.GetTempPath(), "Parkview");
                    try { if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback); }
                    catch { /* nothing left to try */ }
                }

                _writable = fallback;
                return _writable;
            }
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                EnsureDir(dir);
                var probe = Path.Combine(dir, ".write-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The scenes: scripts\Parkview\scenery\.</summary>
        public static string Scenery
        {
            get
            {
                var d = Path.Combine(Data, "scenery");
                EnsureDir(d);
                return d;
            }
        }

        public static string Ini => Path.Combine(Scripts, "Parkview.ini");
        public static string LogFile => Path.Combine(Writable, "Parkview.log");

        private static void EnsureDir(string path)
        {
            try { if (!Directory.Exists(path)) Directory.CreateDirectory(path); }
            catch { /* the caller finds out when it writes */ }
        }
    }
}
