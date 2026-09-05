using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Weapons
{
    /// <summary>
    /// What is bolted to a gun, remembered and handed back with it.
    ///
    /// THE LOCKER GAVE BACK BARE GUNS. A rifle you had put a scope, a grip and a suppressor on
    /// came back as the thing it was in the shop, and every one of those is something the player
    /// went and fitted on purpose.
    ///
    /// IT HAS TO BE A SNAPSHOT, taken while he still HAS the gun. By the time the locker notices
    /// one is missing there is nothing left to ask -- the weapon is gone and its components went
    /// with it. So this is read off the guns he is carrying, on the same slow timer the locker
    /// already runs on, and written into the save; the restore then puts back what the last look
    /// saw.
    ///
    /// AND IT HAS TO BE A CANDIDATE LIST, because the game will not enumerate. There is no
    /// native that says "what is on this weapon" -- only HAS_PED_GOT_WEAPON_COMPONENT, which
    /// answers about one component you already knew the name of. So the only way to find out
    /// what is fitted is to ask about everything it could be.
    ///
    /// WHICH IS WHY THIS COVERS FUNCTION AND NOT DECORATION. Scopes, suppressors, grips,
    /// flashlights, muzzles, sights and magazines are the components that change how a gun
    /// plays, and they are a list short enough to ask about. Camouflage and liveries are dozens
    /// per weapon, change nothing about how it shoots, and would triple the cost of every scan
    /// to restore a paint job. If somebody misses those, this is where they go.
    /// </summary>
    internal static class Attachments
    {
        /// <summary>
        /// The components shared across weapons, which is most of what anybody fits.
        ///
        /// The AT_ prefix is Rockstar's own for "attachment", and these names are reused by every
        /// weapon that can take them -- so one list covers the whole armoury rather than a table
        /// per gun. A name a given weapon cannot take simply answers no, which costs nothing.
        /// </summary>
        private static readonly string[] Common =
        {
            // Sights and scopes
            "COMPONENT_AT_SIGHTS",
            "COMPONENT_AT_SIGHTS_SMG",
            "COMPONENT_AT_SCOPE_MACRO",
            "COMPONENT_AT_SCOPE_MACRO_02",
            "COMPONENT_AT_SCOPE_MACRO_MK2",
            "COMPONENT_AT_SCOPE_SMALL",
            "COMPONENT_AT_SCOPE_SMALL_02",
            "COMPONENT_AT_SCOPE_SMALL_MK2",
            "COMPONENT_AT_SCOPE_MEDIUM",
            "COMPONENT_AT_SCOPE_MEDIUM_MK2",
            "COMPONENT_AT_SCOPE_LARGE",
            "COMPONENT_AT_SCOPE_LARGE_MK2",
            "COMPONENT_AT_SCOPE_MAX",
            "COMPONENT_AT_SCOPE_NV",
            "COMPONENT_AT_SCOPE_THERMAL",

            // Suppressors and muzzles
            "COMPONENT_AT_PI_SUPP",
            "COMPONENT_AT_PI_SUPP_02",
            "COMPONENT_AT_AR_SUPP",
            "COMPONENT_AT_AR_SUPP_02",
            "COMPONENT_AT_SR_SUPP",
            "COMPONENT_AT_SR_SUPP_03",
            "COMPONENT_AT_SB_SUPP",
            "COMPONENT_AT_MUZZLE_01",
            "COMPONENT_AT_MUZZLE_02",
            "COMPONENT_AT_MUZZLE_03",
            "COMPONENT_AT_MUZZLE_04",
            "COMPONENT_AT_MUZZLE_05",
            "COMPONENT_AT_MUZZLE_06",
            "COMPONENT_AT_MUZZLE_07",
            "COMPONENT_AT_MUZZLE_08",
            "COMPONENT_AT_MUZZLE_09",

            // Grips, lamps, rails
            "COMPONENT_AT_AR_AFGRIP",
            "COMPONENT_AT_AR_AFGRIP_02",
            "COMPONENT_AT_PI_FLSH",
            "COMPONENT_AT_PI_FLSH_02",
            "COMPONENT_AT_PI_FLSH_03",
            "COMPONENT_AT_AR_FLSH",
            "COMPONENT_AT_RAILCOVER_01",
            "COMPONENT_AT_CR_LIGHT_01",
            "COMPONENT_AT_MRFL_BARREL_02",
            "COMPONENT_AT_BP_BARREL_02",
            "COMPONENT_AT_SC_BARREL_02"
        };

        /// <summary>
        /// The magazine names, which are per weapon rather than shared.
        ///
        /// Built off the weapon's own name because that is the pattern Rockstar used --
        /// COMPONENT_&lt;WEAPON&gt;_CLIP_02 and so on. It is only a pattern and not a rule, which is
        /// exactly the trap ExtendedClips exists to document; but a name that does not exist
        /// answers no here rather than failing loudly, so guessing costs nothing.
        /// </summary>
        private static readonly string[] Clips =
        {
            "CLIP_01", "CLIP_02", "CLIP_03", "CLIP_04",
            "CLIP_DRUM", "CLIP_BOX",
            "CLIP_INCENDIARY", "CLIP_TRACER", "CLIP_ARMORPIERCING",
            "CLIP_FMJ", "CLIP_HOLLOWPOINT", "CLIP_EXPLOSIVE"
        };

        /// <summary>Every component name worth asking a given weapon about.</summary>
        internal static IEnumerable<string> Candidates(string weapon)
        {
            for (var i = 0; i < Common.Length; i++) yield return Common[i];

            if (string.IsNullOrEmpty(weapon)) yield break;

            // WEAPON_CARBINERIFLE -> COMPONENT_CARBINERIFLE_CLIP_02
            var stem = weapon.StartsWith("WEAPON_", StringComparison.OrdinalIgnoreCase)
                ? weapon.Substring(7)
                : weapon;

            for (var i = 0; i < Clips.Length; i++) yield return "COMPONENT_" + stem + "_" + Clips[i];
        }

        /// <summary>
        /// What is fitted to the weapon he is carrying, as component names.
        ///
        /// Returns null rather than an empty list when he does not have the gun at all, so the
        /// caller can tell "nothing on it" from "nothing to look at" -- overwriting a good record
        /// with an empty one because the gun was momentarily gone is the way this feature would
        /// quietly delete itself.
        /// </summary>
        public static List<string> On(Ped ped, string weapon)
        {
            if (ped == null || !ped.Exists() || string.IsNullOrEmpty(weapon)) return null;

            uint hash;

            try
            {
                hash = Function.Call<uint>(Hash.GET_HASH_KEY, weapon);
                if (hash == 0) return null;

                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, ped.Handle, hash, false)) return null;
            }
            catch
            {
                return null;
            }

            var on = new List<string>();

            foreach (var name in Candidates(weapon))
            {
                try
                {
                    var part = Function.Call<uint>(Hash.GET_HASH_KEY, name);
                    if (part == 0) continue;

                    if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, ped.Handle, hash, part))
                    {
                        on.Add(name);
                    }
                }
                catch
                {
                    // One component that cannot be asked about is not worth the rest.
                }
            }

            return on;
        }

        /// <summary>Puts them back on. Anything the weapon cannot take is ignored by the game.</summary>
        public static int GiveTo(Ped ped, string weapon, List<string> parts)
        {
            if (ped == null || !ped.Exists() || parts == null || parts.Count == 0) return 0;

            uint hash;

            try
            {
                hash = Function.Call<uint>(Hash.GET_HASH_KEY, weapon);
                if (hash == 0) return 0;
            }
            catch
            {
                return 0;
            }

            var back = 0;

            for (var i = 0; i < parts.Count; i++)
            {
                try
                {
                    var part = Function.Call<uint>(Hash.GET_HASH_KEY, parts[i]);
                    if (part == 0) continue;

                    Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED, ped.Handle, hash, part);
                    back++;
                }
                catch
                {
                    // A component that will not go back on is not worth losing the others over.
                }
            }

            if (back > 0) Log.Debug("Put " + back + " part(s) back on " + weapon + ".");

            return back;
        }
    }
}
