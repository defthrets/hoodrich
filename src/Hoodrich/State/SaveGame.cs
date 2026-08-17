using System;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.Locations;
using Hoodrich.Territory;

namespace Hoodrich.State
{
    /// <summary>
    /// Reads and writes the single save document.
    ///
    /// Kept separate from the systems it persists so that each of them owns only its own
    /// serialisation, and there is exactly one place that knows the file layout and the
    /// migration rules between versions.
    /// </summary>
    internal static class SaveGame
    {
        public static void Load(PlayerState state, Affiliation affiliation, Market market,
                                TerritoryState territory, GangDen den)
        {
            var doc = JsonFile.Read(Paths.SaveFile);
            if (doc == null)
            {
                Log.Info("No save found; starting fresh at rank 0.");
                return;
            }

            var version = doc["version"].AsString("0.1.0");
            if (version != Build.Version)
            {
                Log.Info("Save was written by " + version + "; migrating to " + Build.Version + ".");
            }

            state.LoadFrom(doc);
            affiliation.LoadFrom(doc["affiliation"]);
            market.LoadFrom(doc["market"]);
            territory.LoadFrom(doc["territory"]);
            den.LoadFrom(doc["dens"]);
            state.MarkSaved();
        }

        public static bool Save(PlayerState state, Affiliation affiliation, Market market,
                                TerritoryState territory, GangDen den, bool force = false)
        {
            if (!state.IsDirty && !force) return false;

            try
            {
                var doc = state.ToJson()
                    .Set("version", Build.Version)
                    .Set("affiliation", affiliation.ToJson())
                    .Set("market", market.ToJson())
                    .Set("territory", territory.ToJson())
                    .Set("dens", den.ToJson());

                if (!JsonFile.Write(Paths.SaveFile, doc)) return false;

                state.MarkSaved();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Save failed.", ex);
                return false;
            }
        }
    }
}
