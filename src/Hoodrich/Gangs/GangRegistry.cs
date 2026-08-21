using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Loads gang definitions and indexes them for the two lookups that matter at runtime:
    /// by relationship-group hash (which gang is this ped in?) and by zone code (whose turf
    /// am I standing on?).
    /// </summary>
    internal sealed class GangRegistry
    {
        private readonly List<GangDef> _ordered = new List<GangDef>();
        private readonly Dictionary<string, GangDef> _byId = new Dictionary<string, GangDef>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, GangDef> _byGroupHash = new Dictionary<int, GangDef>();
        private readonly Dictionary<string, GangDef> _byZone = new Dictionary<string, GangDef>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<GangDef> All => _ordered;

        public GangDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var g) ? g : null;
        }

        /// <summary>Which gang, if any, a relationship-group hash belongs to.</summary>
        public GangDef ByGroupHash(int hash)
        {
            return _byGroupHash.TryGetValue(hash, out var g) ? g : null;
        }

        /// <summary>Which gang claims a GET_NAME_OF_ZONE code, or null for neutral ground.</summary>
        public GangDef OwnerOfZone(string zoneCode)
        {
            if (string.IsNullOrEmpty(zoneCode)) return null;
            return _byZone.TryGetValue(zoneCode, out var g) ? g : null;
        }

        public static GangRegistry Load()
        {
            var reg = new GangRegistry();
            reg.AddDefaults();

            var path = Path.Combine(Paths.Data, "gangs.json");
            var doc = JsonFile.Read(path);
            if (doc != null) reg.ApplyOverrides(doc);

            reg.ResolveGroups();
            reg.BuildIndexes();

            Log.Info("Gangs loaded: " + reg._ordered.Count + " gangs, " +
                     reg._byZone.Count + " claimed zones.");
            return reg;
        }

        private void ApplyOverrides(Json doc)
        {
            var list = doc.Kind == JsonKind.Array ? doc : doc["gangs"];
            if (list.Kind != JsonKind.Array)
            {
                Log.Warn("gangs.json has no top-level array; keeping built-in gangs.");
                return;
            }

            foreach (var node in list.Items)
            {
                var id = node["id"].AsString(null);
                if (string.IsNullOrEmpty(id))
                {
                    Log.Warn("gangs.json entry with no 'id' skipped.");
                    continue;
                }

                var existing = Get(id);
                var g = existing ?? new GangDef { Id = id };

                g.Name = node["name"].AsString(g.Name.Length > 0 ? g.Name : id);
                g.Tag = node["tag"].AsString(g.Tag.Length > 0 ? g.Tag : id.Substring(0, Math.Min(4, id.Length)).ToUpperInvariant());
                g.RelationshipGroup = node["relationshipGroup"].AsString(g.RelationshipGroup);
                g.BlipColour = node["blipColour"].AsInt(g.BlipColour);
                g.TurfHint = node["turfHint"].AsString(g.TurfHint);
                g.JoinRespect = Math.Max(0f, node["joinRespect"].AsFloat(g.JoinRespect));
                g.Joinable = node["joinable"].AsBool(g.Joinable);

                var col = node["colour"];
                if (col.Kind == JsonKind.Array && col.Count >= 3)
                {
                    g.Colour = Color.FromArgb(255,
                        Clamp255(col[0].AsInt(128)), Clamp255(col[1].AsInt(128)), Clamp255(col[2].AsInt(128)));
                }

                ReplaceList(g.Drugs, node["drugs"]);
                ReplaceList(g.Rivals, node["rivals"]);
                ReplaceList(g.Turf, node["turf"]);

                if (existing == null) Register(g);
            }
        }

        private static void ReplaceList(List<string> target, Json node)
        {
            if (node.Kind != JsonKind.Array) return;
            target.Clear();
            foreach (var s in node.AsStringList()) target.Add(s);
        }

        private static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        /// <summary>
        /// Turns each gang's relationship-group NAME into a hash, creating the group if the
        /// game does not already have it. Never trust a hardcoded group name to exist.
        /// </summary>
        private void ResolveGroups()
        {
            foreach (var g in _ordered)
            {
                if (string.IsNullOrEmpty(g.RelationshipGroup))
                {
                    g.RelationshipGroup = "Hoodrich_" + g.Id.ToUpperInvariant();
                }

                try
                {
                    g.GroupHash = Function.Call<int>(Hash.GET_HASH_KEY, g.RelationshipGroup);

                    // This native takes a HASH. Handing it the name marshals a pointer, which
                    // never matches, so every vanilla gang group looked missing.
                    if (!Function.Call<bool>(Hash.DOES_RELATIONSHIP_GROUP_EXIST, g.GroupHash))
                    {
                        // ADD_RELATIONSHIP_GROUP writes the new hash through a pointer arg.
                        var created = new OutputArgument();
                        Function.Call(Hash.ADD_RELATIONSHIP_GROUP, g.RelationshipGroup, created);
                        Log.Info("Created missing relationship group '" + g.RelationshipGroup + "' for " + g.Name + ".");
                    }

                }
                catch (Exception ex)
                {
                    Log.Error("Could not resolve relationship group for " + g.Id, ex);
                    g.GroupHash = 0;
                }
            }
        }

        private void BuildIndexes()
        {
            _byGroupHash.Clear();
            _byZone.Clear();

            foreach (var g in _ordered)
            {
                if (g.GroupHash != 0 && !_byGroupHash.ContainsKey(g.GroupHash)) _byGroupHash[g.GroupHash] = g;

                foreach (var zone in g.Turf)
                {
                    if (string.IsNullOrEmpty(zone)) continue;
                    if (_byZone.TryGetValue(zone, out var other))
                    {
                        Log.Warn("Zone '" + zone + "' claimed by both " + other.Id + " and " + g.Id +
                                 "; keeping " + other.Id + ".");
                        continue;
                    }
                    _byZone[zone] = g;
                }
            }
        }

        private void Register(GangDef g)
        {
            if (_byId.ContainsKey(g.Id))
            {
                Log.Warn("Duplicate gang id '" + g.Id + "' ignored.");
                return;
            }
            _byId[g.Id] = g;
            _ordered.Add(g);
        }

        /// <summary>
        /// Built-in gangs and their default turf.
        ///
        /// Zone codes are real GET_NAME_OF_ZONE values taken from the game's own popzone table,
        /// so every one of them matches something in the world. Territory is assigned along
        /// canonical GTA lines and kept non-overlapping. An unrecognised code would simply never
        /// match, quietly costing a gang turf -- which is why these are checked rather than guessed.
        /// Use Turf &gt; "Log zone" in game to confirm any code before adding it.
        /// </summary>
        private void AddDefaults()
        {
            // Varrios Los Aztecas.
            //
            // A couple of streets in Rancho, wedged between Ballas turf to the west and the
            // Vagos strip to the south and east -- the smallest named thing on the map, which
            // is the point of them. Going at the Aztecas is not the same kind of job as going
            // at the Ballas.
            //
            // No vanilla relationship group: the game ships the ped but has no ambient Azteca
            // gang, and the Latino groups belong to the Vagos and the Marabunta. Leaving the
            // name blank makes ResolveGroups build "Hoodrich_AZTECAS" and register it, which is
            // the honest way round -- a made-up group we own rather than borrowing somebody
            // else's and making every Vago in the city an Azteca.
            // The Kkangpae. Ginger Street in Little Seoul, in front of the apartment block by
            // the petrol station -- which is where the game itself puts them, and which is NOT
            // where the Triads are. The Triads are the Raven Slaughterhouse in Cypress Flats.
            // Two outfits, two places; the mod had the Triads standing in the Kkangpae's spot.
            //
            // They already had an author on the feed and a hashtag set of their own. They were
            // simply never a gang.
            Register(Make("koreans", "Kkangpae", "KKP", "AMBIENT_GANG_KOREAN",
                Color.FromArgb(150, 40, 55), 1,
                new[] { "meth", "coke" },
                new[] { "triads", "armenians" },
                new[] { "KOREAT" },
                "Ginger Street, Little Seoul",
                new[] { "g_m_y_korean_01", "g_m_y_korean_02", "g_m_y_korlieut_01", "g_m_m_korboss_01" }));

            Register(Make("aztecas", "Varrios Los Aztecas", "VLA", "",
                Color.FromArgb(0, 190, 185), 3,
                new[] { "weed", "coke" },
                new[] { "vagos", "ballas", "families" },
                new[] { "RANCHO" },
                "a couple of streets in Rancho",
                new[] { "g_m_y_azteca_01" }));

            Register(Make("families", "The Families", "FAM", "AMBIENT_GANG_FAMILY",
                Color.FromArgb(60, 180, 75), 2,
                new[] { "weed", "crack" },
                new[] { "ballas", "vagos", "lost", "aztecas" },
                new[] { "CHAMH", "STRAW", "DAVIS" },
                "Chamberlain Hills, Strawberry, Davis",
                // The three Families models and nothing else. A generic South Central civilian
                // used to sit on the end of this list for variety, and it is how a man in a
                // pink shirt and a blazer ended up guarding Lamar's yard with a rifle.
                // Variety is randomised clothing on these three, not a fourth ped who is not
                // in the set.
                new[] { "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01" }));

            Register(Make("ballas", "Ballas", "BALL", "AMBIENT_GANG_BALLAS",
                Color.FromArgb(145, 70, 190), 27,
                new[] { "crack", "weed" },
                new[] { "families", "vagos", "marabunta", "aztecas" },
                new[] { "DAVIS", "CHAMH" },
                "Rancho, Murrieta Heights, Davis Quartz",
                new[] { "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01", "a_m_m_soucent_02" }));

            Register(Make("vagos", "Los Santos Vagos", "VAGO", "AMBIENT_GANG_MEXICAN",
                Color.FromArgb(235, 195, 40), 5,
                new[] { "coke", "meth" },
                new[] { "families", "ballas", "marabunta", "aztecas" },
                new[] { "RANCHO", "LMESA" },
                "El Burro Heights, La Mesa, Tataviam Mountains",
                new[] { "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03", "a_m_y_mexthug_01" }));

            Register(Make("marabunta", "Marabunta Grande", "MARA", "AMBIENT_GANG_MARABUNTE",
                Color.FromArgb(70, 200, 200), 3,
                new[] { "meth", "coke" },
                new[] { "vagos", "ballas", "lost" },
                new[] { "EBURO", "VESP" },
                "Cypress Flats, Textile City, Elysian Island",
                new[] { "g_m_y_salvaboss_01", "g_m_y_salvagoon_01", "g_m_y_salvagoon_02", "a_m_y_mexthug_01" }));

            Register(Make("lost", "The Lost MC", "LOST", "AMBIENT_GANG_LOST",
                Color.FromArgb(200, 200, 200), 40,
                new[] { "meth", "heroin" },
                new[] { "families", "marabunta", "triads" },
                new[] { "SLAB", "GRAPES" },
                "Stab City, Sandy Shores, Grapeseed",
                new[] { "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_lost_03", "a_m_m_hillbilly_01" }));

            Register(Make("triads", "Wei Cheng Triads", "TRI", "AMBIENT_GANG_WEICHENG",
                Color.FromArgb(220, 60, 60), 1,
                new[] { "ecstasy", "heroin" },
                new[] { "lost", "armenians", "koreans" },
                new[] { "CYPRE" },
                "Little Seoul, Mirror Park, Hawick",
                new[] { "g_m_m_chiboss_01", "g_m_m_chigoon_01", "g_m_m_chigoon_02", "a_m_y_ktown_01" }));

            Register(Make("armenians", "Armenian Mob", "ARM", "AMBIENT_GANG_ARMENIAN",
                Color.FromArgb(150, 40, 60), 76,
                new[] { "heroin" },
                new[] { "triads", "lost", "koreans" },
                new[] { "LOSPUER" },
                "Alta, Burton, Pillbox Hill",
                new[] { "g_m_m_armboss_01", "g_m_m_armgoon_01", "g_m_y_armgoon_02", "a_m_m_eastsa_02" }));
        }

        private static GangDef Make(string id, string name, string tag, string relGroup, Color colour,
                                    int blipColour, string[] drugs, string[] rivals, string[] turf,
                                    string turfHint, string[] memberModels)
        {
            var g = new GangDef
            {
                Id = id,
                Name = name,
                Tag = tag,
                RelationshipGroup = relGroup,
                Colour = colour,
                BlipColour = blipColour,
                TurfHint = turfHint
            };
            g.Drugs.AddRange(drugs);
            g.Rivals.AddRange(rivals);
            g.Turf.AddRange(turf);
            g.MemberModels.AddRange(memberModels);
            return g;
        }
    }
}
