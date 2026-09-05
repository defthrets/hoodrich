using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GTA;
using Hoodrich.Core;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// Asking the three art packs what is actually in them, one name at a time.
    ///
    /// WHY THIS HAD TO BE BUILT. The game will not list the contents of a texture dictionary.
    /// There is no call for it, so the only way to learn what art exists is to name a texture
    /// and ask whether it has a resolution -- a question that costs nothing and answers
    /// truthfully, but only about the one name you asked.
    ///
    /// Two rounds of guessing pack names found three packs and seven guns, and guessing harder
    /// was not going to find an eighth. So this goes the other way: it takes every weapon model
    /// name in the game, plus the obvious respellings of each, and asks all three packs about
    /// all of them. What comes back is the complete usable inventory -- and, just as usefully,
    /// proof of what is genuinely not in there, so nobody spends another evening looking for a
    /// picture of an AP Pistol that was never shipped as a sprite.
    ///
    /// IT WRITES A FILE. The log would do, but the log is also where forty other things are
    /// talking; gunart-probe.txt is one page that can be read, kept, or sent to somebody.
    /// </summary>
    internal static class GunArtProbe
    {
        /// <summary>
        /// Every weapon model name in the game, as far as one list can be.
        ///
        /// The mod's own weapons.json only names the weapons the counter sells, and the point
        /// here is to find out what ELSE is in the packs -- so this is the whole roster, sniper
        /// rifles and rocket launchers included, even though Stretch has never sold either.
        /// </summary>
        private static readonly string[] Models =
        {
            // Melee
            "w_me_bat", "w_me_battleaxe", "w_me_bottle", "w_me_candy_xm3", "w_me_crowbar",
            "w_me_dagger", "w_me_flashlight", "w_me_gclub", "w_me_hammer", "w_me_hatchet",
            "w_me_knife_01", "w_me_knuckle", "w_me_kunai", "w_me_machette", "w_me_machette_lr",
            "w_me_nightstick", "w_me_poolcue", "w_me_rod_m41", "w_me_stonehatchet",
            "w_me_switchblade", "w_me_wrench",

            // Pistols
            "w_pi_appistol", "w_pi_ceramic_pistol", "w_pi_combatpistol", "w_pi_flaregun",
            "w_pi_heavypistol", "w_pi_pistol", "w_pi_pistol50", "w_pi_pistolmk2",
            "w_pi_pistol_xm3", "w_pi_pistolsmg_m31", "w_pi_raygun", "w_pi_revolver",
            "w_pi_revolvermk2", "w_pi_singleshot", "w_pi_singleshoth4", "w_pi_sns_pistol",
            "w_pi_sns_pistolmk2", "w_pi_stungun", "w_pi_vintage_pistol", "w_pi_wep1_gun",
            "w_pi_wep2_gun",

            // Machine pistols and SMGs
            "w_sb_assaultsmg", "w_sb_compactsmg", "w_sb_gusenberg", "w_sb_microsmg",
            "w_sb_minismg", "w_sb_pdw", "w_sb_smg", "w_sb_smgmk2",

            // Shotguns
            "w_sg_assaultshotgun", "w_sg_bullpupshotgun", "w_sg_doublebarrel",
            "w_sg_heavyshotgun", "w_sg_musket", "w_sg_pumpshotgun", "w_sg_pumpshotgunh4",
            "w_sg_pumpshotgunmk2", "w_sg_sawnoff", "w_sg_sweeper",

            // Rifles
            "w_ar_advancedrifle", "w_ar_assaultrifle", "w_ar_assaultriflemk2",
            "w_ar_assaultrifle_smg", "w_ar_bullpuprifle", "w_ar_bullpupriflemk2",
            "w_ar_bullpuprifleh4", "w_ar_bullpupriflem42", "w_ar_carbinerifle",
            "w_ar_carbineriflemk2", "w_ar_carbinerifle_reh", "w_ar_heavyrifleh", "w_ar_musket",
            "w_ar_railgun", "w_ar_railgun_xm3", "w_ar_specialcarbine", "w_ar_specialcarbinemk2",
            "w_ar_srifle",

            // Machine guns
            "w_mg_combatmg", "w_mg_combatmgmk2", "w_mg_mg", "w_mg_minigun", "w_mg_sminigun",

            // Sniper rifles
            "w_sr_heavysniper", "w_sr_heavysnipermk2", "w_sr_marksmanrifle",
            "w_sr_marksmanriflemk2", "w_sr_precisionrifle_reh", "w_sr_sniperrifle",

            // Launchers
            "w_lr_compactgl", "w_lr_compactml", "w_lr_compactsl_m32", "w_lr_firework",
            "w_lr_grenadelauncher", "w_lr_homing", "w_lr_rpg",

            // Thrown, and the odds and ends
            "w_ex_apmine", "w_ex_birdshat", "w_ex_grenadefrag", "w_ex_grenadesmoke",
            "w_ex_molotov", "w_ex_pe", "w_ex_pipebomb", "w_ex_snowball",
            "w_am_baseball", "w_am_flare", "w_am_jerrycan", "w_am_jerrycan_sf",
            "w_am_papers_xm3", "w_ch_jerrycan", "w_sl_battlerifle_m32"
        };

        /// <summary>
        /// A few shapes that are not model names at all.
        ///
        /// Here to answer a question the model names cannot: whether these packs are keyed on
        /// model names in the first place. If "pistol" or "weapon_pistol" comes back and
        /// "w_pi_pistol" does not, everything above is the wrong question and the answer is one
        /// line in a file rather than another two evenings.
        /// </summary>
        private static readonly string[] Shapes =
        {
            "pistol", "combatpistol", "microsmg", "sawnoffshotgun", "assaultrifle",
            "weapon_pistol", "weapon_combatpistol", "wp_pistol", "gun_pistol",
            "pistol_01", "w_pistol", "w_pi_pistol_01", "w_pi_pistol1"
        };

        private static bool _running;
        private static int _at;
        private static List<string> _names;
        private static List<string> _packs;
        private static readonly List<string> Found = new List<string>();

        /// <summary>How many names are asked per tick. The question is cheap; the frame is not.</summary>
        private const int PerTick = 60;

        public static bool Running => _running;

        /// <summary>Start it. The packs are whatever the sweep found, so it must have run first.</summary>
        public static bool Start(IEnumerable<string> packs)
        {
            _packs = new List<string>(packs);
            if (_packs.Count == 0) return false;

            _names = new List<string>();

            foreach (var model in Models)
            {
                Add(model);

                // The respellings, because two of the seven that already work were found under
                // a name nobody asked for. Same weapon only -- see GunArt.Spellings.
                if (model.EndsWith("mk2", StringComparison.Ordinal))
                {
                    Add(model.Substring(0, model.Length - 3) + "_mk2");
                    Add(model.Substring(0, model.Length - 3));
                }

                if (model.EndsWith("_mk2", StringComparison.Ordinal))
                {
                    Add(model.Substring(0, model.Length - 4) + "mk2");
                    Add(model.Substring(0, model.Length - 4));
                }

                Add(model + "_01");
                if (model.EndsWith("_01", StringComparison.Ordinal)) Add(model.Substring(0, model.Length - 3));
                if (model.IndexOf('_') > 0) Add(model.Replace("_", ""));
            }

            foreach (var shape in Shapes) Add(shape);

            Found.Clear();
            _at = 0;
            _running = true;

            Log.Info("Gun art probe: asking " + _packs.Count + " pack(s) about " + _names.Count + " names.");

            return true;
        }

        private static void Add(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_names.Contains(name)) return;

            _names.Add(name);
        }

        /// <summary>One slice per tick. Called from the same place the sweep is.</summary>
        public static void Update()
        {
            if (!_running) return;

            var stop = Math.Min(_at + PerTick, _names.Count);

            for (; _at < stop; _at++)
            {
                var name = _names[_at];

                foreach (var pack in _packs)
                {
                    try
                    {
                        if (!Hud.HasTexture(pack, name)) continue;
                    }
                    catch
                    {
                        continue;
                    }

                    Found.Add(name + "  in  " + pack);
                    Log.Info("Gun art probe: " + pack + " has " + name + ".");
                    break;
                }
            }

            if (_at < _names.Count) return;

            _running = false;
            Write();
        }

        private static void Write()
        {
            var sb = new StringBuilder();

            sb.Append("WHAT IS ACTUALLY IN THE GUN ART PACKS\r\n");
            sb.Append("=====================================\r\n\r\n");
            sb.Append("Packs on this install: ").Append(string.Join(", ", _packs.ToArray())).Append("\r\n");
            sb.Append("Names asked: ").Append(_names.Count).Append("\r\n");
            sb.Append("Names found: ").Append(Found.Count).Append("\r\n\r\n");

            if (Found.Count == 0)
            {
                sb.Append("Nothing matched. If the packs are loaded and even w_pi_pistol is\r\n");
                sb.Append("missing, these packs are not keyed on weapon model names at all.\r\n");
            }
            else
            {
                foreach (var row in Found) sb.Append(row).Append("\r\n");
            }

            try
            {
                var path = Path.Combine(Paths.Writable, "gunart-probe.txt");
                System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

                Log.Info("Gun art probe: " + Found.Count + " of " + _names.Count +
                         " names exist. Written to " + path + ".");

                Notify.Important(Found.Count + " of " + _names.Count +
                                 " names exist. Written to gunart-probe.txt.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not write the probe file: " + ex.Message);
                Notify.Important(Found.Count + " of " + _names.Count + " names exist. See the log.");
            }
        }
    }
}
