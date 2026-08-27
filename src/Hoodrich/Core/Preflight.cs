using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GTA;

namespace Hoodrich.Core
{
    /// <summary>One thing that is wrong, and what to do about it.</summary>
    internal sealed class Fault
    {
        public string What = "";
        public string Fix = "";

        /// <summary>Fatal means the mod cannot work at all, not merely that something is odd.</summary>
        public bool Fatal;
    }

    /// <summary>
    /// Why it is not working, said out loud.
    ///
    /// THERE IS A LIMIT TO THIS AND IT IS WORTH STATING PLAINLY. If ScriptHookV is missing or
    /// is the wrong one for the game, or ScriptHookVDotNet never loads, then nothing in this
    /// file runs either -- we are inside the thing that failed to start. No amount of error
    /// handling can put a message on screen from a script that was never loaded, and any mod
    /// claiming otherwise is telling you about a different failure than the one you have.
    ///
    /// What this DOES cover is everything after SHVDN has managed to load us, which is most of
    /// what people actually hit: the right ScriptHookV for the wrong build of the game, an
    /// SHVDN too old to know what Enhanced is, a data folder that did not get copied, an
    /// install where the scripts folder is somewhere unexpected. Those all reach this code, and
    /// all of them used to fail as silence.
    ///
    /// For the case where nothing loads at all, the answer is not code -- it is the
    /// troubleshooting file in the zip and the two logs the loaders write themselves.
    /// </summary>
    internal static class Preflight
    {
        /// <summary>
        /// The SHVDN that first understood the Enhanced build.
        ///
        /// Enhanced game versions appear in SHVDN's GameVersion enum with a v2_0_ prefix, and
        /// an older build has no entries for them -- so it reports Unknown, half the natives
        /// resolve to nothing, and scripts fail in ways that look like the scripts are broken.
        /// </summary>
        private static readonly Version NeedsForEnhanced = new Version(3, 6, 0, 0);

        /// <summary>Every data file the mod will try to read.</summary>
        private static readonly string[] Needed =
        {
            "drugs.json", "gangs.json", "dealers.json", "leaders.json",
            "missions.json", "zones.json", "weapons.json", "cars.json",
            "socials.json", "tags.json"
        };

        /// <summary>What SHVDN says the game is, or Unknown.</summary>
        public static string GameBuild
        {
            get
            {
                try { return Game.Version.ToString(); }
                catch { return "unreadable"; }
            }
        }

        /// <summary>
        /// Whether this is the Enhanced release.
        ///
        /// Asked two ways because either one alone can be wrong. The running executable is the
        /// honest answer -- Enhanced runs GTA5_Enhanced.exe -- but a launcher or a shim can
        /// muddy it, so the SHVDN build string is checked too: Enhanced versions are the ones
        /// that come through with a v2_0_ prefix.
        /// </summary>
        public static bool IsEnhanced
        {
            get
            {
                try
                {
                    var exe = Process.GetCurrentProcess().MainModule.FileName ?? "";
                    if (exe.IndexOf("enhanced", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                catch
                {
                    // Some setups will not hand over the module. Fall through to the version.
                }

                return GameBuild.StartsWith("v2_0_", StringComparison.OrdinalIgnoreCase) ||
                       GameBuild.StartsWith("V2_0_", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Which ScriptHookVDotNet is actually running us.</summary>
        public static Version Shvdn
        {
            get
            {
                try { return typeof(Script).Assembly.GetName().Version ?? new Version(0, 0); }
                catch { return new Version(0, 0); }
            }
        }

        /// <summary>The ScriptHookV sitting next to the game, by its own file version.</summary>
        public static string ScriptHookV
        {
            get
            {
                try
                {
                    var exe = Process.GetCurrentProcess().MainModule.FileName;
                    var dir = Path.GetDirectoryName(exe);
                    if (string.IsNullOrEmpty(dir)) return "not found";

                    var dll = Path.Combine(dir, "ScriptHookV.dll");
                    if (!File.Exists(dll)) return "not found";

                    return FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "unknown";
                }
                catch
                {
                    return "unreadable";
                }
            }
        }

        /// <summary>Everything wrong, worst first. Empty means there is nothing to say.</summary>
        public static List<Fault> Check()
        {
            var faults = new List<Fault>();

            CheckLoader(faults);
            CheckData(faults);
            CheckWritable(faults);

            return faults;
        }

        private static void CheckLoader(List<Fault> faults)
        {
            var build = GameBuild;

            // SHVDN DOES NOT RECOGNISE THE GAME. This is the big one and it is almost always
            // the answer on Enhanced: the loader was built before this version of the game
            // existed, so it cannot map the natives and everything downstream misbehaves.
            if (string.Equals(build, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                faults.Add(new Fault
                {
                    Fatal = true,
                    What = "ScriptHookVDotNet does not recognise this build of GTA V" +
                           "  (it reports the version as Unknown)",
                    Fix = IsEnhanced
                        ? "Enhanced needs the ENHANCED ScriptHookV, not the Legacy one, and " +
                          "ScriptHookVDotNet v3.6 or newer. Check ScriptHookV.dll sitting next " +
                          "to GTA5_Enhanced.exe -- the Legacy and Enhanced files have the same " +
                          "name and are not interchangeable."
                        : "Update ScriptHookV to the build that matches your game version, " +
                          "then update ScriptHookVDotNet. The game updated and the loader did not."
                });
            }

            if (IsEnhanced && Shvdn < NeedsForEnhanced)
            {
                faults.Add(new Fault
                {
                    Fatal = true,
                    What = "ScriptHookVDotNet " + Shvdn + " is too old for GTA V Enhanced",
                    Fix = "Enhanced support arrived in v" + NeedsForEnhanced.ToString(3) +
                          ". Replace ScriptHookVDotNet.asi and ScriptHookVDotNet3.dll with a " +
                          "current build and keep them together -- a new .asi with an old .dll " +
                          "fails the same way."
                });
            }

            if (string.Equals(ScriptHookV, "not found", StringComparison.OrdinalIgnoreCase))
            {
                faults.Add(new Fault
                {
                    Fatal = false,
                    What = "ScriptHookV.dll was not found next to the game executable",
                    Fix = "The mod is running, so something loaded it -- but if things " +
                          "misbehave, check ScriptHookV is installed in the game folder itself " +
                          "and not in scripts\\."
                });
            }
        }

        private static void CheckData(List<Fault> faults)
        {
            string dir;

            try
            {
                dir = Paths.Data;
            }
            catch (Exception ex)
            {
                faults.Add(new Fault
                {
                    Fatal = true,
                    What = "Could not work out where the mod's data folder is: " + ex.Message,
                    Fix = "Reinstall: the scripts\\Hoodrich\\ folder should sit beside " +
                          "Hoodrich.dll inside scripts\\."
                });
                return;
            }

            if (!Directory.Exists(dir))
            {
                faults.Add(new Fault
                {
                    Fatal = true,
                    What = "The data folder is missing: " + dir,
                    Fix = "The zip contains scripts\\Hoodrich\\ as well as scripts\\Hoodrich.dll. " +
                          "Both go in. Copying only the dll is the most common way to get here."
                });
                return;
            }

            var missing = new List<string>();

            for (var i = 0; i < Needed.Length; i++)
            {
                if (!File.Exists(Path.Combine(dir, Needed[i]))) missing.Add(Needed[i]);
            }

            if (missing.Count == 0) return;

            faults.Add(new Fault
            {
                Fatal = missing.Count > 3,
                What = missing.Count + " data file" + (missing.Count == 1 ? "" : "s") +
                       " missing: " + string.Join(", ", missing.ToArray()),
                Fix = "Copy scripts\\Hoodrich\\ out of the zip again, over the top. Do not " +
                      "delete the folder first or the save goes with it."
            });
        }

        private static void CheckWritable(List<Fault> faults)
        {
            try
            {
                var dir = Paths.Writable;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var probe = Path.Combine(dir, "write.test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                faults.Add(new Fault
                {
                    Fatal = false,
                    What = "The mod cannot write to its own folder: " + ex.Message,
                    Fix = "Your game is somewhere Windows protects, such as Program Files. " +
                          "Nothing will save. Either run the game as administrator or move the " +
                          "install somewhere writable."
                });
            }
        }

        /// <summary>
        /// The block a player can paste when they say it is not working.
        ///
        /// Written every single start, working or not, because the one time it matters is the
        /// time somebody is describing a problem they cannot reproduce on request.
        /// </summary>
        public static void WriteEnvironment()
        {
            try
            {
                Log.Info("---- environment ----");
                Log.Info("  " + Build.Name + " " + Build.Version);
                Log.Info("  game build   " + GameBuild + (IsEnhanced ? "  (Enhanced)" : "  (Legacy)"));
                Log.Info("  ScriptHookV  " + ScriptHookV);
                Log.Info("  SHVDN        " + Shvdn);
                Log.Info("  runtime      " + Environment.Version + "  " +
                         (Environment.Is64BitProcess ? "64-bit" : "32-bit"));
                Log.Info("  scripts      " + Paths.Scripts);
                Log.Info("  data         " + Paths.Data);
                Log.Info("---------------------");
            }
            catch
            {
                // A log line is not worth failing a startup over.
            }
        }
    }
}
