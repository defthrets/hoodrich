using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// Finding the game's own photograph of a gun, once, and then never looking again.
    ///
    /// THE GAME WILL NOT TELL YOU WHERE ITS OWN ART IS. Every weapon has a shop photograph and
    /// it is a texture named after the weapon's model -- w_pi_combatpistol -- living in one of
    /// a few dozen streamed dictionaries that were added an update at a time. There is no call
    /// that says which, and asking for a texture out of a dictionary that has not streamed in
    /// yet is indistinguishable from asking for one that does not exist.
    ///
    /// THE FIRST ATTEMPT AT THIS ASKED FOR ALL FORTY DICTIONARIES EVERY FRAME AND GAVE UP ON
    /// EACH GUN AFTER FIVE SECONDS. Five of twenty guns ever got a picture, and the AP Pistol
    /// -- a base-game weapon whose photo is certainly in there somewhere -- was one of the
    /// failures. Forty simultaneous requests is not a search, it is a queue nothing gets to the
    /// front of, and a five second deadline against a streamer under that load is a coin toss
    /// that then gets remembered as a no for the rest of the session.
    ///
    /// So it works the other way round now. ONE SWEEP, in small batches, each batch given long
    /// enough to actually arrive: which dictionaries exist on this install is a question with
    /// one answer, asked once. Then every gun's photo is looked for only in the dictionaries
    /// that are known to be there and already resident, which is instant.
    ///
    /// AND IT IS WRITTEN DOWN. gunart.json in the mod's own folder keeps what was found, so the
    /// second session and every session after it draws every picture on the first frame. A
    /// player never sees the search at all; only the person who installs a new game update
    /// does, and then only once.
    /// </summary>
    internal static class GunArt
    {
        /// <summary>
        /// The dictionaries the game keeps gun photographs in, one an update since 2013.
        ///
        /// Names it does not have cost one sweep entry and nothing after that, so the list errs
        /// wide -- and the sweep LOGS every one that turns out to be real, which is how this
        /// list gets shorter and truer rather than by guessing harder.
        /// </summary>
        private static readonly string[] Candidates =
        {
            "mpweaponscommon", "mpweaponscommon2", "mpweapons",
            "mpweaponsgang0", "mpweaponsgang1", "mpweaponsgang2", "mpweaponsgang3",
            "mpweaponsbeach", "mpweaponsvalentines", "mpweaponsvalentines2",
            "mpweaponsbusiness", "mpweaponsbusiness2", "mpweaponshipster",
            "mpweaponsindependence", "mpweaponspilot", "mpweaponslts",
            "mpweaponschristmas2", "mpweaponsxmas", "mpweaponsxmas2", "mpweaponsxmas3",
            "mpweaponschristmas2017", "mpweaponschristmas2018", "mpweaponschristmas3",
            "mpweaponsassault", "mpweaponsassault3", "mpweaponsheist", "mpweaponsheist3",
            "mpweaponsheist4", "mpweaponsluxe", "mpweaponsluxe2", "mpweaponsreplay",
            "mpweaponslowrider", "mpweaponslowrider2", "mpweaponshalloween",
            "mpweaponsapartment", "mpweaponsjanuary2016", "mpweaponsexecutive",
            "mpweaponsstunt", "mpweaponsbiker", "mpweaponsbikers", "mpweaponsimportexport",
            "mpweaponsgunrunning", "mpweaponsairraces", "mpweaponssmuggler",
            "mpweaponsbattle", "mpweaponsvinewood", "mpweaponscasino",
            "mpweaponssum20", "mpweaponssum2", "mpweaponssum23", "mpweaponstuner",
            "mpweaponssecurity", "mpweaponsg9ec", "mpweaponsag",
            "mpweaponsm23_1", "mpweaponsm23_2", "mpweaponsm24_1", "mpweaponsm24_2"
        };

        /// <summary>How many are asked for at a time, and how long that batch is given to arrive.</summary>
        private const int Batch = 8;
        private const int BatchMs = 600;

        /// <summary>The dictionaries this install actually has.</summary>
        private static readonly List<string> Real = new List<string>();

        /// <summary>Icon to the dictionary holding it; "" once every real dictionary has been asked and none had it.</summary>
        private static readonly Dictionary<string, string> Where =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static int _at = -1;
        private static int _batchUntil;
        private static bool _done;
        private static bool _read;
        private static bool _dirty;

        /// <summary>Whether the sweep has finished, for the settings screen to say.</summary>
        public static bool Ready => _done;

        public static string Tally()
        {
            if (!_done) return _at < 0 ? "not looked yet" : "looking, " + _at + " of " + Candidates.Length;

            var named = 0;
            foreach (var pair in Where)
            {
                if (pair.Value.Length > 0) named++;
            }

            return Real.Count + " art pack(s) on this install, " + named + " gun(s) found";
        }

        // ---- the sweep -------------------------------------------------------------

        /// <summary>
        /// Called every frame the gun counter is open. Does nothing once the sweep has finished.
        ///
        /// It runs from the DRAW path rather than from a spawner, so it must never wait: a
        /// batch is asked for, the frame ends, and the answer is read a few frames later.
        /// </summary>
        public static void Update()
        {
            if (_done) return;

            if (!_read) Recall();
            if (_done) return;

            var now = Game.GameTime;

            if (_at < 0)
            {
                _at = 0;
                _batchUntil = now + BatchMs;
                Ask();
                return;
            }

            if (now < _batchUntil) return;

            // Whatever arrived in that window is real; whatever did not is not on this install.
            for (var i = _at; i < _at + Batch && i < Candidates.Length; i++)
            {
                var dict = Candidates[i];

                try
                {
                    if (!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict)) continue;
                }
                catch
                {
                    continue;
                }

                Real.Add(dict);
            }

            _at += Batch;

            if (_at < Candidates.Length)
            {
                _batchUntil = now + BatchMs;
                Ask();
                return;
            }

            _done = true;
            _dirty = true;

            Log.Info("Gun art: " + Real.Count + " of " + Candidates.Length +
                     " art packs are on this install -- " + string.Join(", ", Real.ToArray()) + ".");

            Keep();
        }

        private static void Ask()
        {
            for (var i = _at; i < _at + Batch && i < Candidates.Length; i++)
            {
                try { Hud.EnsureTextureDict(Candidates[i]); }
                catch { /* a name the game does not know is simply never loaded */ }
            }
        }

        // ---- drawing ---------------------------------------------------------------

        /// <summary>Draws the gun's photograph if it can be found. False means draw the name instead.</summary>
        public static bool Draw(string icon, float cx, float cy, float w, float h, System.Drawing.Color ink)
        {
            if (string.IsNullOrEmpty(icon)) return false;

            if (!_read) Recall();

            string dict;

            if (Where.TryGetValue(icon, out dict))
            {
                if (dict.Length == 0) return false;

                var cut = dict.IndexOf('/');
                var name = cut < 0 ? icon : dict.Substring(cut + 1);
                if (cut >= 0) dict = dict.Substring(0, cut);

                if (!Hud.EnsureTextureDict(dict)) return false;

                Hud.Sprite(dict, name, cx, cy, w, h, 0f, ink);
                return true;
            }

            // LOOKED FOR IN WHATEVER IS KNOWN SO FAR, rather than waiting for the sweep.
            //
            // This was gated on the sweep finishing, and the sweep takes several seconds --
            // so for those seconds NOTHING had a picture, including the guns whose pack was
            // confirmed in the first batch. Waiting for a complete answer before giving any
            // answer is the wrong trade on a counter somebody is stood at.
            foreach (var name in Spellings(icon))
            {
                foreach (var candidate in Real)
                {
                    if (!Hud.EnsureTextureDict(candidate)) continue;
                    if (!Hud.HasTexture(candidate, name)) continue;

                    // The dictionary AND the name that worked, because the name that worked is
                    // not always the one we asked for.
                    Where[icon] = candidate + "/" + name;
                    _dirty = true;

                    Log.Info("Gun art: " + icon + " is in " + candidate +
                             (name == icon ? "." : " as " + name + "."));

                    Hud.Sprite(candidate, name, cx, cy, w, h, 0f, ink);
                    Keep();

                    return true;
                }
            }

            // Not found yet. While the sweep is still running that is not an answer, only a
            // not-yet -- writing it down here would remember a guess made before the packs
            // it needed had been looked at.
            if (!_done) return false;

            // Every real pack was asked and none of them had it. Said once, then kept.
            Where[icon] = "";
            _dirty = true;

            Log.Info("Gun art: no pack on this install has " + icon + "; the name will do.");
            Keep();

            return false;
        }

        /// <summary>
        /// The names one gun's photograph might be filed under, best first.
        ///
        /// ONLY EVER THE SAME GUN. The temptation is to strip a suffix until something matches,
        /// and that ends with the Compact Rifle showing a photograph of an Assault Rifle --
        /// which is worse than showing its name, because a wrong picture is believed. So the
        /// only substitutions here are ones where the two names are the same weapon: the two
        /// ways the game spells a Mk II, and the lowrider suffix on a melee weapon that was
        /// re-released rather than replaced. A Mk II falling back to its own base gun is
        /// allowed on purpose -- it is that gun, with a kit on it.
        /// </summary>
        private static IEnumerable<string> Spellings(string icon)
        {
            yield return icon;

            var lower = icon.ToLowerInvariant();

            if (lower.EndsWith("mk2", StringComparison.Ordinal))
            {
                if (lower.EndsWith("_mk2", StringComparison.Ordinal))
                {
                    yield return icon.Substring(0, icon.Length - 4) + "mk2";
                    yield return icon.Substring(0, icon.Length - 4);
                }
                else
                {
                    yield return icon.Substring(0, icon.Length - 3) + "_mk2";
                    yield return icon.Substring(0, icon.Length - 3);
                }
            }

            if (lower.EndsWith("_lr", StringComparison.Ordinal)) yield return icon.Substring(0, icon.Length - 3);
        }

        // ---- keeping it --------------------------------------------------------------

        private static string File => Path.Combine(Paths.Writable, "gunart.json");

        /// <summary>
        /// What was found last time. A file from a different game build is no use, so what it
        /// says is only trusted while the packs it names are still there -- checked lazily, by
        /// the draw itself failing to load the dictionary, rather than by a second sweep.
        /// </summary>
        private static void Recall()
        {
            _read = true;

            try
            {
                if (!System.IO.File.Exists(File)) return;

                var doc = JsonFile.Read(File);
                if (doc == null) return;

                Real.Clear();
                foreach (var node in doc["packs"].Items)
                {
                    var name = node.AsString("");
                    if (!string.IsNullOrEmpty(name)) Real.Add(name);
                }

                Where.Clear();
                foreach (var node in doc["guns"].Items)
                {
                    var row = node.AsString("");
                    if (string.IsNullOrEmpty(row)) continue;

                    var cut = row.IndexOf('|');
                    if (cut < 0) continue;

                    Where[row.Substring(0, cut)] = row.Substring(cut + 1);
                }

                if (Real.Count == 0) return;

                _done = true;
                _at = Candidates.Length;

                Log.Info("Gun art: " + Real.Count + " art pack(s) and " + Where.Count +
                         " gun(s) remembered from last time.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read the gun art list: " + ex.Message);
            }
        }

        private static void Keep()
        {
            if (!_dirty) return;
            _dirty = false;

            try
            {
                var packs = Json.Array();
                foreach (var dict in Real) packs.Add(Json.Str(dict));

                var guns = Json.Array();
                foreach (var pair in Where) guns.Add(Json.Str(pair.Key + "|" + pair.Value));

                var doc = Json.Object().Set("packs", packs).Set("guns", guns);

                JsonFile.Write(File, doc);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not write the gun art list: " + ex.Message);
            }
        }

        /// <summary>Forget everything and look again. For the settings screen, after a game update.</summary>
        public static void Again()
        {
            Real.Clear();
            Where.Clear();

            _at = -1;
            _done = false;
            _read = true;
            _dirty = true;

            try
            {
                if (System.IO.File.Exists(File)) System.IO.File.Delete(File);
            }
            catch
            {
                // It is overwritten when the sweep finishes anyway.
            }

            Log.Info("Gun art: looking again.");
        }
    }
}
