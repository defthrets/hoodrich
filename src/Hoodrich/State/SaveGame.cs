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
        /// <summary>
        /// Set when the save on disk could not be read AND neither could the backup.
        ///
        /// While this is true nothing writes. That is the entire safety mechanism, and it is
        /// deliberately blunt: the alternative -- carrying on and autosaving over the top --
        /// is what turns one bad read into a lost playthrough. Write is atomic and leaves a
        /// .bak behind, so a save that has gone unreadable is very often still recoverable by
        /// hand, right up until the mod saves twice more and rolls the good copy out of the
        /// backup slot.
        ///
        /// It is only ever set at load, and never cleared, so a session that started badly
        /// stays read-only until the player has dealt with the file and restarted.
        /// </summary>
        public static bool Blocked { get; private set; }

        /// <summary>What went wrong, for the message the player actually sees.</summary>
        public static string BlockedBecause { get; private set; }

        public static void Load(PlayerState state, Affiliation affiliation, Market market,
                                StashHouse stash, Missions.TagRun tags = null)
        {
            Core.ReadResult how;
            var doc = JsonFile.Read(Paths.SaveFile, out how);

            if (doc == null && how == Core.ReadResult.Missing)
            {
                Log.Info("No save found; starting fresh at rank 0.");
                return;
            }

            if (doc == null)
            {
                // THE SAVE IS THERE AND WE COULD NOT READ IT. Every write leaves the previous
                // document in a .bak beside it, and until now nothing had ever opened that
                // file -- it was written every single save and read by nobody. One save
                // behind is a few minutes of play; a fresh start is everything.
                var bak = JsonFile.BackupOf(Paths.SaveFile);

                Core.ReadResult bakHow;
                doc = JsonFile.Read(bak, out bakHow);

                if (doc != null)
                {
                    Log.Error("save.json unreadable; loaded save.json.bak instead.");
                    UI.Notify.Important(
                        "~r~Save was damaged.~s~ Loaded the backup -- you may have lost a few minutes.");
                }
                else
                {
                    // Nothing left to read. Do NOT start fresh and do NOT write, because the
                    // damaged file is still on disk and is still the best copy in existence.
                    Blocked = true;
                    BlockedBecause = bakHow == Core.ReadResult.Missing
                        ? "save.json could not be read and there is no backup"
                        : "neither save.json nor save.json.bak could be read";

                    Log.Error("SAVING IS OFF: " + BlockedBecause + ". File: " + Paths.SaveFile);

                    UI.Notify.Important(
                        "~r~Could not read your save.~s~ Saving is OFF so nothing overwrites it. " +
                        "Check scripts\\Hoodrich\\save.json.");

                    return;
                }
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
            // NOT EVEN WHEN FORCED. The forced path is the one the sleep spot and the shutdown
            // hook use, and those are exactly the moments a blank state would be written over
            // a real save with the most conviction.
            if (Blocked) return false;

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
