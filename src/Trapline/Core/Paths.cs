using System;
using System.IO;
using System.Reflection;

namespace Trapline.Core
{
    /// <summary>
    /// Resolves where Trapline reads and writes. Everything hangs off the folder the
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

        /// <summary>scripts\Trapline\ — data, saves and gang definitions.</summary>
        public static string Data
        {
            get
            {
                var d = Path.Combine(Scripts, "Trapline");
                EnsureDir(d);
                return d;
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

        public static string Ini => Path.Combine(Scripts, "Trapline.ini");
        public static string LogFile => Path.Combine(Scripts, "Trapline.log");
        public static string SaveFile => Path.Combine(Data, "save.json");

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
