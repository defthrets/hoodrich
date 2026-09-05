using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;

namespace Hoodrich.Weapons
{
    /// <summary>
    /// What Stretch can bolt to a gun: the components the game says the weapon takes, each
    /// with a name a person would use and a price under the counter.
    ///
    /// THE GAME KNOWS WHAT FITS AND WILL SAY SO, one part at a time. There is no native that
    /// lists a weapon's components, but DOES_WEAPON_TAKE_WEAPON_COMPONENT answers about any
    /// pair, so the same candidate list Attachments scans with is asked of the gun on the
    /// counter and what says yes is the shelf. Default magazines are left off it: a part that
    /// is already on every gun is not a part anybody buys.
    /// </summary>
    internal static class Parts
    {
        internal sealed class Part
        {
            public string Component;
            public uint Hash;
            public string Name;
            public int Price;
        }

        public static List<Part> For(string weapon)
        {
            var parts = new List<Part>();
            if (string.IsNullOrEmpty(weapon)) return parts;

            uint gun;
            try { gun = Function.Call<uint>(Hash.GET_HASH_KEY, weapon); }
            catch { return parts; }
            if (gun == 0) return parts;

            foreach (var name in Attachments.Candidates(weapon))
            {
                if (name.EndsWith("_CLIP_01", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var part = Function.Call<uint>(Hash.GET_HASH_KEY, name);
                    if (part == 0) continue;
                    if (!Function.Call<bool>(Hash.DOES_WEAPON_TAKE_WEAPON_COMPONENT, gun, part)) continue;

                    parts.Add(new Part { Component = name, Hash = part, Name = Named(name), Price = Priced(name) });
                }
                catch
                {
                }
            }

            return parts;
        }

        public static bool Fitted(Ped ped, uint gun, Part part)
        {
            try
            {
                return ped != null && ped.Exists()
                       && Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, ped.Handle, gun, part.Hash);
            }
            catch
            {
                return false;
            }
        }

        public static void Fit(Ped ped, uint gun, Part part, bool on)
        {
            if (ped == null || !ped.Exists()) return;

            if (on) Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED, ped.Handle, gun, part.Hash);
            else Function.Call(Hash.REMOVE_WEAPON_COMPONENT_FROM_PED, ped.Handle, gun, part.Hash);
        }

        // ---- names and prices -----------------------------------------------------

        /// <summary>"COMPONENT_AT_SCOPE_MACRO_MK2" reads as "Macro scope Mk II" on a shelf.</summary>
        public static string Named(string component)
        {
            var s = component.StartsWith("COMPONENT_", StringComparison.OrdinalIgnoreCase)
                ? component.Substring(10)
                : component;

            var mk2 = s.EndsWith("_MK2", StringComparison.OrdinalIgnoreCase);
            if (mk2) s = s.Substring(0, s.Length - 4);

            var second = s.EndsWith("_02") || s.EndsWith("_03");
            var name = Plain(s);

            if (second && !name.EndsWith("rounds") && !name.Contains("magazine") && !name.Contains("clip"))
            {
                name += s.EndsWith("_03") ? " III" : " II";
            }

            return mk2 ? name + " Mk II" : name;
        }

        private static string Plain(string s)
        {
            if (s.Contains("SCOPE_MACRO")) return "Macro scope";
            if (s.Contains("SCOPE_SMALL")) return "Small scope";
            if (s.Contains("SCOPE_MEDIUM")) return "Medium scope";
            if (s.Contains("SCOPE_LARGE")) return "Large scope";
            if (s.Contains("SCOPE_MAX")) return "Advanced scope";
            if (s.Contains("SCOPE_NV")) return "Night vision scope";
            if (s.Contains("SCOPE_THERMAL")) return "Thermal scope";
            if (s.Contains("SIGHTS")) return "Holographic sight";
            if (s.Contains("_SUPP")) return "Suppressor";
            if (s.Contains("MUZZLE")) return "Muzzle brake " + s.Substring(s.Length - 2).TrimStart('0');
            if (s.Contains("AFGRIP")) return "Grip";
            if (s.Contains("FLSH")) return "Flashlight";
            if (s.Contains("RAILCOVER")) return "Rail cover";
            if (s.Contains("CR_LIGHT")) return "Light";
            if (s.Contains("BARREL")) return "Heavy barrel";
            if (s.EndsWith("CLIP_02")) return "Extended clip";
            if (s.EndsWith("CLIP_03")) return "Drum magazine";
            if (s.EndsWith("CLIP_04")) return "Box magazine";
            if (s.EndsWith("CLIP_DRUM")) return "Drum magazine";
            if (s.EndsWith("CLIP_BOX")) return "Box magazine";
            if (s.EndsWith("CLIP_INCENDIARY")) return "Incendiary rounds";
            if (s.EndsWith("CLIP_TRACER")) return "Tracer rounds";
            if (s.EndsWith("CLIP_ARMORPIERCING")) return "Armour piercing rounds";
            if (s.EndsWith("CLIP_FMJ")) return "Full metal jacket rounds";
            if (s.EndsWith("CLIP_HOLLOWPOINT")) return "Hollow point rounds";
            if (s.EndsWith("CLIP_EXPLOSIVE")) return "Explosive rounds";

            // Whatever it is, as words.
            var words = s.ToLowerInvariant().Split('_');
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }
            return string.Join(" ", words);
        }

        private static int Priced(string component)
        {
            var s = component.ToUpperInvariant();

            if (s.Contains("SCOPE_MAX") || s.Contains("SCOPE_NV") || s.Contains("SCOPE_THERMAL")) return 900;
            if (s.Contains("SCOPE_LARGE")) return 650;
            if (s.Contains("SCOPE_MEDIUM")) return 500;
            if (s.Contains("SCOPE_SMALL")) return 400;
            if (s.Contains("SCOPE_MACRO")) return 350;
            if (s.Contains("SIGHTS")) return 250;
            if (s.Contains("_SUPP")) return 450;
            if (s.Contains("MUZZLE")) return 300;
            if (s.Contains("AFGRIP")) return 200;
            if (s.Contains("FLSH") || s.Contains("CR_LIGHT")) return 120;
            if (s.Contains("RAILCOVER")) return 60;
            if (s.Contains("BARREL")) return 400;
            if (s.EndsWith("CLIP_02")) return 180;
            if (s.EndsWith("CLIP_03") || s.EndsWith("CLIP_DRUM")) return 320;
            if (s.EndsWith("CLIP_04") || s.EndsWith("CLIP_BOX")) return 400;
            if (s.Contains("_CLIP_")) return 260;

            return 200;
        }
    }
}
