using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GTA;

namespace Hoodrich.Core
{
    /// <summary>
    /// A model hash turned back into the name somebody typed to spawn it.
    ///
    /// THE GAME ONLY GOES ONE WAY. A name is hashed into a number the moment it is used and
    /// the number is all any script can read back, so "what is this thing called" -- the first
    /// question you ask about anything you want to build round -- has no answer in the API at
    /// all. The usual workaround is to guess names and watch for one that spawns, which costs
    /// a build per guess and has been the single most expensive habit in this project.
    ///
    /// So: hash the LISTS instead. Menyoo and Rampage both ship complete name lists as plain
    /// text, twenty-one thousand objects and every ped model, and the hash is a nine-line
    /// function. Run it over the lists once, keep the map, and every hash in the game that
    /// came from one of those names can be read out loud.
    ///
    /// NOT SHIPPED WITH THE MOD AND NOT REQUIRED BY IT. The lists belong to other tools and
    /// live in the game folder; without them this answers "" and everything that uses it falls
    /// back to printing the number. It is a workshop tool, and workshops are allowed to depend
    /// on what is on the bench.
    /// </summary>
    internal static class Names
    {
        private static Dictionary<uint, string> _byHash;
        private static bool _tried;

        /// <summary>Where the lists live, relative to the game folder. First one found of each kind is used.</summary>
        private static readonly string[] Lists =
        {
            @"RampageFiles\Lists\ObjectList.txt",
            @"RampageFiles\Lists\PedList.txt",
            @"RampageFiles\Lists\VehicleList.txt",
            @"RampageFiles\Lists\WeaponList.txt",
            @"menyooStuff\PropList.txt",
            @"menyooStuff\PedList.xml",
            @"menyooStuff\VehicleList.txt"
        };

        /// <summary>How many names are known. Zero means the lists were not there.</summary>
        public static int Count
        {
            get
            {
                Ready();
                return _byHash == null ? 0 : _byHash.Count;
            }
        }

        public static string Of(Model model) => Of(model.Hash);

        /// <summary>The name, or "" when nothing on the bench knows it.</summary>
        public static string Of(int hash) => Of(unchecked((uint)hash));

        public static string Of(uint hash)
        {
            Ready();

            string name;
            return _byHash != null && _byHash.TryGetValue(hash, out name) ? name : "";
        }

        /// <summary>The name if it is known, and the hash in hex if it is not. For logs.</summary>
        public static string Say(int hash)
        {
            var name = Of(hash);
            return string.IsNullOrEmpty(name) ? "0x" + unchecked((uint)hash).ToString("X8") : name;
        }

        private static void Ready()
        {
            if (_tried) return;
            _tried = true;

            try
            {
                var root = GameRoot();
                if (string.IsNullOrEmpty(root)) return;

                _byHash = new Dictionary<uint, string>();

                foreach (var rel in Lists)
                {
                    var path = Path.Combine(root, rel);
                    if (!File.Exists(path)) continue;

                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0) continue;

                        // The xml lists carry the name in an attribute; pull anything that
                        // looks like a model name out of the line rather than parsing it.
                        if (line[0] == '<')
                        {
                            foreach (var word in Words(line))
                            {
                                Add(word);
                            }

                            continue;
                        }

                        // Some lists are "name,0x1234" or "name = 123".
                        var cut = line.IndexOfAny(new[] { ',', '=', '\t', ' ' });
                        if (cut > 0) line = line.Substring(0, cut).Trim();

                        Add(line);
                    }
                }

                Log.Info("Model names on the bench: " + _byHash.Count + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read the name lists: " + ex.Message);
            }
        }

        private static void Add(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3) return;

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return;
            }

            var hash = Joaat(name);
            if (!_byHash.ContainsKey(hash)) _byHash[hash] = name;
        }

        /// <summary>The name-shaped runs in a line of xml, without parsing it as xml.</summary>
        private static IEnumerable<string> Words(string line)
        {
            var sb = new StringBuilder();

            foreach (var c in line)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                    continue;
                }

                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Length = 0;
                }
            }

            if (sb.Length > 0) yield return sb.ToString();
        }

        /// <summary>
        /// Rockstar's string hash: the one the game itself uses, on the lower-cased name.
        /// Jenkins one-at-a-time, and every model, anim dict, scenario and audio name in the
        /// game is one of these.
        /// </summary>
        public static uint Joaat(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            uint hash = 0;

            foreach (var raw in text)
            {
                var c = raw;
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);

                hash += c;
                hash += hash << 10;
                hash ^= hash >> 6;
            }

            hash += hash << 3;
            hash ^= hash >> 11;
            hash += hash << 15;

            return hash;
        }

        /// <summary>The folder the game runs from: the one above scripts\.</summary>
        public static string GameRoot()
        {
            try
            {
                var scripts = Paths.Scripts;
                if (string.IsNullOrEmpty(scripts)) return "";

                var up = Directory.GetParent(scripts);
                return up == null ? "" : up.FullName;
            }
            catch
            {
                return "";
            }
        }
    }
}
