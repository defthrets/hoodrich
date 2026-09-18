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
                // Game.Version rather than Game.FileVersion, which 3.6 has not got. It is the
                // running loader's enum, so on a build that knows the game it names it
                // (v1_0_3889_0_Steam, v2_0_...) and on one that does not it says Unknown --
                // which is exactly the answer CheckLoader wants to hear about.
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

        /// <summary>
        /// WHICH ScriptHookVDotNet THIS DLL WAS COMPILED AGAINST.
        ///
        /// Not a number anybody types in. The compiler stamps the exact version of every
        /// reference assembly into the output, and this reads it back out of our own metadata
        /// -- so it is right by construction, it moves the day the build machine's loader
        /// moves, and it cannot drift out of step with a readme the way "3.6 or newer" did.
        ///
        /// WHY IT IS WORTH KNOWING. A method that changed shape between the loader we built
        /// against and the loader running us is a MissingMethodException, and the .NET runtime
        /// throws that when it COMPILES the method containing the call -- not when the call
        /// runs. So a try/catch around the call cannot catch it, and a branch that would never
        /// have been taken kills the method anyway: a player on English lost the whole mod to
        /// a line that only ever speaks to somebody running it in Chinese.
        ///
        /// Nothing can be done about that from inside the method. What CAN be done is to
        /// notice the mismatch and say which way round it is, which is the whole of this.
        /// </summary>
        public static Version ShvdnBuiltAgainst
        {
            get
            {
                try
                {
                    var refs = Assembly.GetExecutingAssembly().GetReferencedAssemblies();

                    for (var i = 0; i < refs.Length; i++)
                    {
                        if (refs[i].Name != null &&
                            refs[i].Name.StartsWith("ScriptHookVDotNet", StringComparison.OrdinalIgnoreCase))
                        {
                            return refs[i].Version ?? new Version(0, 0);
                        }
                    }
                }
                catch
                {
                    // Reading our own manifest is not worth failing a startup over.
                }

                return new Version(0, 0);
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

        /// <summary>
        /// What the mod was in the middle of when it fell over.
        ///
        /// A breadcrumb rather than a stack, because a stack means nothing to the person
        /// reading it and the stack is in the log anyway. "while loading dealers.json" turns
        /// an unactionable NullReferenceException into somebody opening one file.
        /// </summary>
        public static string Step = "starting up";

        /// <summary>Everything wrong, worst first. Empty means there is nothing to say.</summary>
        public static List<Fault> Check()
        {
            var faults = new List<Fault>();

            CheckLoader(faults);
            CheckData(faults);
            CheckWritable(faults);

            // AND WHAT IS ACTUALLY IN THE FILES. Everything above this line asks whether things
            // exist; this asks whether they are any good and whether they agree with each other.
            try
            {
                DataCheck.Check(Paths.Data, faults);
            }
            catch (Exception ex)
            {
                faults.Add(new Fault
                {
                    Fatal = false,
                    What = "Could not check the data files: " + ex.Message,
                    Fix = "Not necessarily a problem on its own -- but if the mod is misbehaving, " +
                          "recopy scripts\\Hoodrich\\ from the zip."
                });
            }

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

            // AND THE PLAIN CASE: A LOADER OLDER THAN THE ONE THIS WAS BUILT ON.
            //
            // THIS IS THE COMMONEST REPORT ON THE PAGE AND IT HAD NO ANSWER. It arrives as
            // "MissingMethodException: Method not found: GTA.FeedPost GTA.UI.Notification
            // .PostTicker(String, Boolean, Boolean)" and the mod, having no idea, signed it
            // off as "this is one for the mod author" -- sending somebody to wait on me for a
            // thing they could fix in thirty seconds by updating their loader.
            //
            // The two numbers are read, not assumed: ShvdnBuiltAgainst comes out of our own
            // manifest and Shvdn out of the assembly running us. Older is a mismatch; the same
            // or newer says nothing, which is why a healthy install never sees this.
            var built = ShvdnBuiltAgainst;

            if (built > new Version(0, 0) && Shvdn > new Version(0, 0) && Shvdn < built)
            {
                faults.Add(new Fault
                {
                    Fatal = true,
                    What = "ScriptHookVDotNet " + Shvdn + " is older than the " + built +
                           " this was built against",
                    // ToString(3) THROWS on a version with fewer than three parts, and this
                    // runs inside the handler that reports a startup that already failed. A
                    // metadata version always has four, but "always" is doing work there that
                    // a conditional can do for nothing.
                    Fix = "Install ScriptHookVDotNet v" +
                          (built.Build >= 0 ? built.ToString(3) : built.ToString()) +
                          " or newer from " +
                          "github.com/scripthookvdotnet/scripthookvdotnet/releases. Replace " +
                          "ScriptHookVDotNet.asi AND ScriptHookVDotNet3.dll together -- a new " +
                          ".asi with an old .dll fails exactly the same way. Nightly builds " +
                          "move the API about and are not what this is tested on."
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

        /// <summary>
        /// Where the save actually is, said out loud when it is not where you would look.
        ///
        /// THIS USED TO BE UNREACHABLE. It probed Paths.Writable and reported a fault if the
        /// probe threw -- but Paths.Writable has ALREADY fallen back to somewhere writable by
        /// the time it hands back a path, so the probe passed every time and the warning it
        /// guarded could never fire. A check that cannot fail is not a check.
        ///
        /// What is worth saying is not "this folder is read-only". It is "your save is not
        /// where you think it is", because that is the sentence that stops somebody looking in
        /// scripts\Hoodrich, finding nothing, and concluding the mod does not save.
        /// </summary>
        private static void CheckWritable(List<Fault> faults)
        {
            try
            {
                var dir = Paths.Writable;
                var beside = Path.Combine(Paths.Scripts, "Hoodrich");

                if (string.Equals(dir, beside, StringComparison.OrdinalIgnoreCase)) return;

                faults.Add(new Fault
                {
                    Fatal = false,
                    What = "Your save is in " + dir + ", not beside the dll.",
                    Fix = "Windows will not let the game folder be written to, which is normal " +
                          "under Program Files. Nothing is wrong and nothing is lost -- but do " +
                          "not go looking for save.json in scripts\\Hoodrich, and do not " +
                          "change that folder's permissions, because the save does not move " +
                          "with them."
                });
            }
            catch (Exception ex)
            {
                faults.Add(new Fault
                {
                    Fatal = false,
                    What = "Could not work out where the save goes: " + ex.Message,
                    Fix = "Read Hoodrich.log -- the Paths line names the folder it settled on."
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
                // BOTH NUMBERS, ALWAYS. Somebody pasting this block into a comment should
                // not need me to ask them what their loader is -- the mismatch that breaks the
                // mod is visible on the line itself.
                var built = ShvdnBuiltAgainst;
                Log.Info("  SHVDN        " + Shvdn +
                         (built > new Version(0, 0) ? "  (built against " + built + ")" : "") +
                         (built > new Version(0, 0) && Shvdn > new Version(0, 0) && Shvdn < built
                             ? "  <-- TOO OLD, update ScriptHookVDotNet"
                             : ""));
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
