using System;
using System.Collections.Generic;
using System.IO;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>A product line the player can buy, hold and sell.</summary>
    internal sealed class DrugDef
    {
        /// <summary>Stable key used in saves and gang data. Never localise this.</summary>
        public string Id = "";

        public string Name = "";

        /// <summary>Three-or-four letter tag for the wheel, where space is tight.</summary>
        public string Tag = "";

        /// <summary>Street price for one unit (one gram) before any modifiers.</summary>
        public float BasePrice = 10f;

        /// <summary>
        /// Wholesale price per gram. Zero falls back to the flat percentage discount.
        ///
        /// Per product rather than a single percentage off the street price, because the gap
        /// between wholesale and street is nothing like the same on weed as it is on powder.
        /// One percentage across the catalogue made the cheap end unbuyable and the expensive
        /// end free money.
        /// </summary>
        public float BulkPrice;

        /// <summary>1 = low-risk street weight, 3 = the stuff that brings heat.</summary>
        public int Tier = 1;

        /// <summary>Relative contribution to police/rival attention per sale.</summary>
        public float HeatFactor = 1f;

        /// <summary>
        /// What the street actually calls breaking this down into sellable amounts. Weed gets
        /// bagged up, powder gets cut, rock gets rocked up, pills get counted out -- calling
        /// all of it "cutting" was wrong for most of the catalogue.
        /// </summary>
        public string SplitVerb = "Cut";

        /// <summary>What one sellable unit is called, for the wheel.</summary>
        public string UnitName = "grams";

        public override string ToString() => Id;
    }

    /// <summary>
    /// The product catalogue. Ships with defaults in code so the mod is playable with no data
    /// files at all, and overlays scripts\Hoodrich\drugs.json when present.
    /// </summary>
    internal sealed class Drugs
    {
        private readonly List<DrugDef> _ordered = new List<DrugDef>();
        private readonly Dictionary<string, DrugDef> _byId = new Dictionary<string, DrugDef>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<DrugDef> All => _ordered;

        public DrugDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var d) ? d : null;
        }

        public static Drugs Load()
        {
            var drugs = new Drugs();
            drugs.AddDefaults();

            var path = Path.Combine(Paths.Data, "drugs.json");
            var doc = JsonFile.Read(path);
            if (doc == null)
            {
                Log.Info("Using built-in drug catalogue (" + drugs._ordered.Count + " products).");
                return drugs;
            }

            var list = doc.Kind == JsonKind.Array ? doc : doc["drugs"];
            if (list.Kind != JsonKind.Array)
            {
                Log.Warn("drugs.json has no top-level array; keeping built-in catalogue.");
                return drugs;
            }

            var replaced = 0;
            var added = 0;
            foreach (var node in list.Items)
            {
                var id = node["id"].AsString(null);
                if (string.IsNullOrEmpty(id))
                {
                    Log.Warn("drugs.json entry with no 'id' skipped.");
                    continue;
                }

                var existing = drugs.Get(id);
                var def = existing ?? new DrugDef { Id = id };

                def.Name = node["name"].AsString(def.Name.Length > 0 ? def.Name : id);
                def.Tag = node["tag"].AsString(def.Tag.Length > 0 ? def.Tag : id.Substring(0, Math.Min(4, id.Length)));
                def.BasePrice = Math.Max(0.01f, node["basePrice"].AsFloat(def.BasePrice));
                def.BulkPrice = Math.Max(0f, node["bulkPrice"].AsFloat(def.BulkPrice));
                def.Tier = Math.Max(1, node["tier"].AsInt(def.Tier));
                def.HeatFactor = Math.Max(0f, node["heatFactor"].AsFloat(def.HeatFactor));
                def.SplitVerb = node["splitVerb"].AsString(def.SplitVerb);
                def.UnitName = node["unitName"].AsString(def.UnitName);

                if (existing == null)
                {
                    drugs.Register(def);
                    added++;
                }
                else
                {
                    replaced++;
                }
            }

            Log.Info("drugs.json applied: " + replaced + " overridden, " + added + " added.");
            return drugs;
        }

        private void Register(DrugDef def)
        {
            if (_byId.ContainsKey(def.Id))
            {
                Log.Warn("Duplicate drug id '" + def.Id + "' ignored.");
                return;
            }
            _byId[def.Id] = def;
            _ordered.Add(def);
        }

        private void AddDefaults()
        {
            Register(new DrugDef { Id = "weed", Name = "Marijuana", Tag = "WEED", BasePrice = 10f, Tier = 1, HeatFactor = 0.5f, SplitVerb = "Bag up", UnitName = "baggies" });
            Register(new DrugDef { Id = "crack", Name = "Crack", Tag = "CRK", BasePrice = 25f, Tier = 2, HeatFactor = 1.0f, SplitVerb = "Rock up", UnitName = "rocks" });
            Register(new DrugDef { Id = "ecstasy", Name = "Ecstasy", Tag = "PILL", BasePrice = 35f, Tier = 2, HeatFactor = 0.8f, SplitVerb = "Count out", UnitName = "pills" });
            Register(new DrugDef { Id = "meth", Name = "Meth", Tag = "METH", BasePrice = 28f, Tier = 2, HeatFactor = 1.1f, SplitVerb = "Break down", UnitName = "shards" });
            Register(new DrugDef { Id = "heroin", Name = "Heroin", Tag = "H", BasePrice = 45f, Tier = 3, HeatFactor = 1.4f, SplitVerb = "Cut", UnitName = "bags" });
            Register(new DrugDef { Id = "coke", Name = "Cocaine", Tag = "COKE", BasePrice = 100f, Tier = 3, HeatFactor = 1.6f, SplitVerb = "Cut", UnitName = "grams" });
        }

    }
}
