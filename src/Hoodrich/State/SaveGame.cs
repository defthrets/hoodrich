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
                                StashHouse stash, Missions.TagRun tags = null)
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
            stash.LoadFrom(doc["stashHouse"]);

            // Not passed in like the other four, because the inbox is static -- see the note
            // on that class. It is still the save document's business, so it is still read and
            // written here rather than quietly loading itself from somewhere else.
            Social.Inbox.LoadFrom(doc["inbox"]);

            // The paint. Optional so a caller that has not got hold of it yet still loads
            // everything else rather than throwing.
            if (tags != null) tags.LoadFrom(doc["tags"]);

            state.MarkSaved();
        }

        /// <summary>
        /// Reads just the paint back, for a caller that could not have it at Load time.
        ///
        /// Main loads the save before it builds the mission runner, and the tags live inside
        /// that runner -- so at the moment everything else is read there is nothing to hand
        /// them to. Rather than reorder a startup sequence that other systems depend on, the
        /// document is opened a second time for this one field. It is a few kilobytes, once,
        /// at load.
        /// </summary>
        public static void LoadTags(Missions.TagRun tags)
        {
            if (tags == null) return;

            var doc = JsonFile.Read(Paths.SaveFile);
            if (doc == null) return;

            tags.LoadFrom(doc["tags"]);
        }

        public static bool Save(PlayerState state, Affiliation affiliation, Market market,
                                StashHouse stash, bool force = false,
                                Missions.TagRun tags = null)
        {
            if (!state.IsDirty && !force) return false;

            try
            {
                var doc = state.ToJson()
                    .Set("version", Build.Version)
                    .Set("affiliation", affiliation.ToJson())
                    .Set("market", market.ToJson())
                    .Set("stashHouse", stash.ToJson())
                    .Set("inbox", Social.Inbox.ToJson());

                if (tags != null) doc.Set("tags", tags.ToJson());

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
