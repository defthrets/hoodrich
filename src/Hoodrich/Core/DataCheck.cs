using System;
using System.Collections.Generic;
using System.IO;

namespace Hoodrich.Core
{
    /// <summary>
    /// Reads the data folder the way the mod will, and says what is wrong with it.
    ///
    /// EXISTING IS NOT THE SAME AS WORKING, and the preflight only ever checked existing. A
    /// file can be present and truncated, present and empty, or present and full of references
    /// to things that are not there -- a dealer whose gang was renamed, a mission pointing at a
    /// zone code that does not exist, an icon nobody copied across. None of those throw. They
    /// produce a mod that starts and then quietly does not do one particular thing, which is
    /// the single hardest kind of problem for somebody to report, because the honest report is
    /// "some of it doesn't work" and there is nowhere to go from there.
    ///
    /// So this is the other half: parse every file, then check that everything they point at
    /// actually exists, and name the file and the id when it does not.
    ///
    /// It is deliberately READ-ONLY and deliberately its own pass. It does not use the loaded
    /// registries, because those have already applied their own fallbacks -- a gang id that did
    /// not resolve is a null the registry quietly skipped, and asking the registry afterwards
    /// is asking the thing that already forgave it.
    /// </summary>
    internal static class DataCheck
    {
        /// <summary>Each file, and the array the mod expects to find in it.</summary>
        private static readonly string[][] Expect =
        {
            new[] { "drugs.json", "drugs" },
            new[] { "gangs.json", "gangs" },
            new[] { "dealers.json", "dealers" },
            new[] { "leaders.json", "leaders" },
            new[] { "missions.json", "missions" },
            new[] { "zones.json", "zones" },
            new[] { "weapons.json", "weapons" },
            new[] { "cars.json", "cars" },
            new[] { "tags.json", "tags" }
        };

        /// <summary>Everything wrong inside the data itself.</summary>
        public static void Check(string dir, List<Fault> into)
        {
            if (into == null || string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            var docs = new Dictionary<string, Json>(StringComparer.OrdinalIgnoreCase);

            Parse(dir, docs, into);

            // No point cross-referencing against files that would not open.
            Ids(docs, into);
            Icons(dir, docs, into);
            Stamp(dir, into);
        }

        /// <summary>Opens each one and says which failed, rather than that "a" file failed.</summary>
        private static void Parse(string dir, Dictionary<string, Json> docs, List<Fault> into)
        {
            for (var i = 0; i < Expect.Length; i++)
            {
                var name = Expect[i][0];
                var root = Expect[i][1];
                var path = Path.Combine(dir, name);

                if (!File.Exists(path)) continue;   // the preflight already said so

                Core.ReadResult how;
                var doc = JsonFile.Read(path, out how);

                if (doc == null)
                {
                    into.Add(new Fault
                    {
                        Fatal = true,
                        What = name + " is there but could not be read -- it is not valid JSON",
                        Fix = "Copy that one file out of the zip again. A half-finished edit " +
                              "usually means a missing comma or a stray bracket near the end."
                    });

                    continue;
                }

                var list = doc[root];

                if (list.IsNull || list.Count == 0)
                {
                    into.Add(new Fault
                    {
                        // leaders.json is allowed to be empty; nothing else is.
                        Fatal = !string.Equals(name, "leaders.json", StringComparison.OrdinalIgnoreCase),
                        What = name + " has no \"" + root + "\" in it, or the list is empty",
                        Fix = "The file should be an object with a \"" + root + "\" array inside " +
                              "it. If you were editing it, that is where it went."
                    });

                    continue;
                }

                docs[name] = doc;
            }
        }

        /// <summary>Everything one file says about another.</summary>
        private static void Ids(Dictionary<string, Json> docs, List<Fault> into)
        {
            var gangs = Set(docs, "gangs.json", "gangs", "id");
            var drugs = Set(docs, "drugs.json", "drugs", "id");
            var zones = Set(docs, "zones.json", "zones", "code");

            // dealers -> gangs, drugs, zones
            Each(docs, "dealers.json", "dealers", (id, node) =>
            {
                var gang = node["gangId"].AsString("");
                if (gang.Length > 0 && !gangs.Contains(gang))
                {
                    Miss(into, "dealers.json", id, "gangId \"" + gang + "\"", "gangs.json");
                }

                foreach (var d in node["drugs"].Items)
                {
                    var drug = d.AsString("");
                    if (drug.Length > 0 && !drugs.Contains(drug))
                    {
                        Miss(into, "dealers.json", id, "drug \"" + drug + "\"", "drugs.json");
                    }
                }

                foreach (var z in node["zones"].Items)
                {
                    var zone = z.AsString("");
                    if (zone.Length > 0 && !zones.Contains(zone))
                    {
                        Miss(into, "dealers.json", id, "zone \"" + zone + "\"", "zones.json");
                    }
                }
            });

            // missions -> zones, gangs
            Each(docs, "missions.json", "missions", (id, node) =>
            {
                var zone = node["zone"].AsString("");
                if (zone.Length > 0 && !zones.Contains(zone))
                {
                    Miss(into, "missions.json", id, "zone \"" + zone + "\"", "zones.json");
                }

                var gang = node["targetGang"].AsString("");
                if (gang.Length > 0 && !gangs.Contains(gang))
                {
                    Miss(into, "missions.json", id, "targetGang \"" + gang + "\"", "gangs.json");
                }
            });

            // leaders and tags -> gangs
            Each(docs, "leaders.json", "leaders", (id, node) =>
            {
                var gang = node["gang"].AsString("");
                if (gang.Length > 0 && !gangs.Contains(gang))
                {
                    Miss(into, "leaders.json", id, "gang \"" + gang + "\"", "gangs.json");
                }
            });

            Each(docs, "tags.json", "tags", (id, node) =>
            {
                var gang = node["gang"].AsString("");
                if (gang.Length > 0 && !gangs.Contains(gang))
                {
                    Miss(into, "tags.json", id, "gang \"" + gang + "\"", "gangs.json");
                }
            });

            // gangs -> their own rivals
            Each(docs, "gangs.json", "gangs", (id, node) =>
            {
                foreach (var r in node["rivals"].Items)
                {
                    var rival = r.AsString("");
                    if (rival.Length > 0 && !gangs.Contains(rival))
                    {
                        Miss(into, "gangs.json", id, "rival \"" + rival + "\"", "gangs.json");
                    }
                }
            });
        }

        /// <summary>Every .png any data file names, against what is in icons\.</summary>
        private static void Icons(string dir, Dictionary<string, Json> docs, List<Fault> into)
        {
            var icons = Path.Combine(dir, "icons");
            if (!Directory.Exists(icons))
            {
                into.Add(new Fault
                {
                    Fatal = false,
                    What = "The icons folder is missing, so every menu will draw without pictures",
                    Fix = "scripts\\Hoodrich\\icons\\ comes in the zip. It is the folder people " +
                          "most often leave behind."
                });

                return;
            }

            var missing = new List<string>();

            foreach (var kv in docs)
            {
                Walk(kv.Value, s =>
                {
                    if (!s.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
                    if (File.Exists(Path.Combine(icons, s))) return;
                    if (missing.Contains(s)) return;

                    missing.Add(s);
                });
            }

            if (missing.Count == 0) return;

            into.Add(new Fault
            {
                Fatal = false,
                What = missing.Count + " icon" + (missing.Count == 1 ? "" : "s") +
                       " named in the data are not in icons\\: " +
                       string.Join(", ", missing.ToArray()),
                Fix = "Those rows will draw blank. Copy scripts\\Hoodrich\\icons\\ out of the " +
                      "zip again."
            });
        }

        /// <summary>
        /// Whether the data folder came with this dll.
        ///
        /// THE MOST COMMON HALF-BROKEN INSTALL THERE IS. Somebody drops in a new Hoodrich.dll,
        /// keeps the old scripts\Hoodrich\ folder because their save is in it, and gets a build
        /// that reads fields the old files do not have and looks for dealers that were added
        /// after it. Nothing throws. It just behaves like a worse version of itself.
        ///
        /// The deploy stamps the version it shipped; if the file is absent or disagrees, say so.
        /// </summary>
        private static void Stamp(string dir, List<Fault> into)
        {
            var path = Path.Combine(dir, "version.txt");

            string had;

            try
            {
                had = File.Exists(path) ? (File.ReadAllText(path) ?? "").Trim() : "";
            }
            catch
            {
                return;
            }

            if (had == Build.Version) return;

            into.Add(new Fault
            {
                Fatal = false,
                What = had.Length == 0
                    ? "The data folder has no version stamp, so it is older than " + Build.Version
                    : "Data folder is version " + had + " but Hoodrich.dll is " + Build.Version,
                Fix = "Copy scripts\\Hoodrich\\ out of the zip over the top -- do NOT delete the " +
                      "folder first or save.json goes with it. Updating the dll and keeping the " +
                      "old data is the most common way to get a mod that half works."
            });
        }

        // ---- small helpers ------------------------------------------------------

        private static void Miss(List<Fault> into, string file, string id, string what, string shouldBeIn)
        {
            into.Add(new Fault
            {
                Fatal = false,
                What = file + ": \"" + id + "\" points at " + what + ", which is not in " + shouldBeIn,
                Fix = "Either it was renamed in one file and not the other, or the two files " +
                      "came from different versions. Whatever uses it will be skipped."
            });
        }

        private static HashSet<string> Set(Dictionary<string, Json> docs, string file,
                                           string root, string key)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Json doc;
            if (!docs.TryGetValue(file, out doc)) return set;

            foreach (var node in doc[root].Items)
            {
                var id = node[key].AsString("");
                if (id.Length > 0) set.Add(id);
            }

            return set;
        }

        private static void Each(Dictionary<string, Json> docs, string file, string root,
                                 Action<string, Json> each)
        {
            Json doc;
            if (!docs.TryGetValue(file, out doc)) return;

            foreach (var node in doc[root].Items)
            {
                var id = node["id"].AsString("");
                if (id.Length == 0) id = node["name"].AsString("(unnamed)");

                each(id, node);
            }
        }

        /// <summary>Every string anywhere in a document.</summary>
        private static void Walk(Json node, Action<string> onString)
        {
            if (node == null || node.IsNull) return;

            var s = node.AsString("");
            if (s.Length > 0 && node.Count == 0)
            {
                onString(s);
                return;
            }

            foreach (var child in node.Items) Walk(child, onString);

            foreach (var key in node.Keys) Walk(node[key], onString);
        }
    }
}
