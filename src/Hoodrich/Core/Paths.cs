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

                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hoodrich");

                if (IsWritable(preferred))
                {
                    // A SAVE THAT ALREADY EXISTS WINS, WHEREVER IT IS.
                    //
                    // Choosing purely on "can I write here" makes the save location a function
                    // of the folder's permissions, and permissions change: the game goes into
                    // Program Files, the first run cannot write there and saves to Documents,
                    // and then the player runs as administrator once, or takes ownership, or
                    // an installer resets the ACL -- and the next run CAN write to the game
                    // folder, finds no save in it, and starts them at rank zero. Their
                    // playthrough is still sitting in Documents, untouched, and nothing ever
                    // looks at it again.
                    //
                    // Reported as "all the progress i have made via your mod is reset", and
                    // fixed by the player granting full control -- which is not the fix, it is
                    // simply the thing that stopped the location moving about.
                    //
                    // So the question asked first is not where CAN we write, it is where IS
                    // the save. Only when neither place has one does writability decide.
                    if (!HasSave(preferred) && HasSave(fallback))
                    {
                        // ASSIGNED BEFORE IT IS LOGGED, and that order is not stylistic.
                        // Log.Info writes to Paths.LogFile, which asks for Paths.Writable --
                        // so logging first re-enters this getter with _writable still null,
                        // takes this same branch, and logs again. That is not an exception
                        // anything can catch; it is a stack overflow, and it would land on
                        // precisely the installs this branch exists to rescue.
                        _writable = fallback;

                        Log.Info("Save found in " + fallback + " rather than beside the dll. " +
                                 "Using it, so the permissions on the game folder cannot move " +
                                 "a playthrough.");

                        return _writable;
                    }

                    _writable = preferred;
                    return _writable;
                }

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

        /// <summary>
        /// Whether a playthrough lives here.
        ///
        /// The backup counts. A save that is mid-write is a save, and the whole point of the
        /// .bak is that it is the one still readable when the other one is not -- so a folder
        /// holding nothing but a backup is still the folder with the playthrough in it, and
        /// walking away from it would be the exact loss this check exists to prevent.
        /// </summary>
        private static bool HasSave(string dir)
        {
            try
            {
                var save = Path.Combine(dir, "save.json");

                return File.Exists(save) || File.Exists(save + ".bak");
            }
            catch
            {
                return false;
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

        /// <summary>
        /// Free-hand paint, kept apart from the save.
        ///
        /// ITS OWN FILE ON PURPOSE. The tag run's marks already ride inside save.json, and
        /// folding thousands of loose splatters in beside them would make every ordinary save
        /// carry a wall's worth of decals -- and would put the two at risk of each other, so a
        /// corrupt paint list could cost somebody their money and their product.
        /// </summary>
        public static string PaintFile => Path.Combine(Writable, "paint.json");

        /// <summary>
        /// Recorded dialogue, next to the data rather than in the writable folder.
        ///
        /// It is CONTENT and it ships with the mod, so it belongs beside drugs.json and the
        /// icons. The writable fallback exists for files the game writes, and nothing writes
        /// here -- a missing folder just means nobody is talking yet.
        /// </summary>
        public static string Voice
        {
            get
            {
                var d = Path.Combine(Data, "voice");
                EnsureDir(d);
                return d;
            }
        }

        /// <summary>
        /// Object Spooner scenes that ship with the mod. Menyoo's own folder is read as well
        /// -- see Locations.Scenery -- so this one is for scenes taken into the mod to keep.
        /// </summary>
        public static string Scenery
        {
            get
            {
                var d = Path.Combine(Data, "scenery");
                EnsureDir(d);
                return d;
            }
        }

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
    
        /// <summary>
        /// The HUD art, beside the data rather than loose in scripts\.
        ///
        /// Read-only as far as this mod is concerned, so it hangs off Data and not Writable --
        /// a player whose game folder is unwritable still has the icons, because the deploy put
        /// them there.
        ///
        /// Named here because every other mod in the set names it, and UI.Splash is the same
        /// file in all six -- it can only ask for the folder by one name.
        /// </summary>
        public static string Icons => Path.Combine(Data, "icons");
}
}
