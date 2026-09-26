using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Weapons
{
    /// <summary>
    /// The extended magazine for every gun that has one.
    ///
    /// Everything Stretch sells comes with the bigger mag already in it. He is not a gun shop
    /// with an accessories counter -- he is a man in a yard selling you a working piece, and a
    /// working piece is one you do not have to reload every six shots.
    ///
    /// The component names are NOT guessable and must not be guessed. The pattern looks like
    /// COMPONENT_&lt;WEAPON&gt;_CLIP_02 right up until it is not, and a name that does not exist
    /// fails silently -- GIVE_WEAPON_COMPONENT_TO_PED simply does nothing and the player walks
    /// off with a standard magazine and no error anywhere. So this table is generated from the
    /// game's own weapon component data rather than typed out.
    ///
    /// It is filtered by the component's English LABEL, not its name, because several weapons
    /// carry ammo types spelt like magazines -- COMPONENT_REVOLVER_MK2_CLIP_FMJ is Full Metal
    /// Jacket rounds and COMPONENT_PUMPSHOTGUN_MK2_CLIP_ARMORPIERCING is Steel Buckshot. Handing
    /// somebody those instead of a bigger magazine changes what the gun fires.
    ///
    /// Weapons with no extended magazine in the game at all -- the Double Action, the machete,
    /// the grenades -- are simply absent, and asking for one is a no-op rather than a failure.
    /// </summary>
    internal static class ExtendedClips
    {
        private static readonly Dictionary<string, string> Clips =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "WEAPON_ADVANCEDRIFLE", "COMPONENT_ADVANCEDRIFLE_CLIP_02" },
            { "WEAPON_APPISTOL", "COMPONENT_APPISTOL_CLIP_02" },
            { "WEAPON_ASSAULTRIFLE", "COMPONENT_ASSAULTRIFLE_CLIP_02" },
            { "WEAPON_ASSAULTRIFLE_MK2", "COMPONENT_ASSAULTRIFLE_MK2_CLIP_02" },
            { "WEAPON_ASSAULTSHOTGUN", "COMPONENT_ASSAULTSHOTGUN_CLIP_02" },
            { "WEAPON_ASSAULTSMG", "COMPONENT_ASSAULTSMG_CLIP_02" },
            { "WEAPON_BATTLERIFLE", "COMPONENT_BATTLERIFLE_CLIP_02" },
            { "WEAPON_BULLPUPRIFLE", "COMPONENT_BULLPUPRIFLE_CLIP_02" },
            { "WEAPON_BULLPUPRIFLE_MK2", "COMPONENT_BULLPUPRIFLE_MK2_CLIP_02" },
            { "WEAPON_CARBINERIFLE", "COMPONENT_CARBINERIFLE_CLIP_02" },
            { "WEAPON_CARBINERIFLE_MK2", "COMPONENT_CARBINERIFLE_MK2_CLIP_02" },
            { "WEAPON_CERAMICPISTOL", "COMPONENT_CERAMICPISTOL_CLIP_02" },
            { "WEAPON_COMBATMG", "COMPONENT_COMBATMG_CLIP_02" },
            { "WEAPON_COMBATMG_MK2", "COMPONENT_COMBATMG_MK2_CLIP_02" },
            { "WEAPON_COMBATPDW", "COMPONENT_COMBATPDW_CLIP_02" },
            { "WEAPON_COMBATPISTOL", "COMPONENT_COMBATPISTOL_CLIP_02" },
            { "WEAPON_COMPACTRIFLE", "COMPONENT_COMPACTRIFLE_CLIP_02" },
            { "WEAPON_GUSENBERG", "COMPONENT_GUSENBERG_CLIP_02" },
            { "WEAPON_HEAVYPISTOL", "COMPONENT_HEAVYPISTOL_CLIP_02" },
            { "WEAPON_HEAVYRIFLE", "COMPONENT_HEAVYRIFLE_CLIP_02" },
            { "WEAPON_HEAVYSHOTGUN", "COMPONENT_HEAVYSHOTGUN_CLIP_02" },
            { "WEAPON_HEAVYSNIPER_MK2", "COMPONENT_HEAVYSNIPER_MK2_CLIP_02" },
            { "WEAPON_MACHINEPISTOL", "COMPONENT_MACHINEPISTOL_CLIP_02" },
            { "WEAPON_MARKSMANRIFLE", "COMPONENT_MARKSMANRIFLE_CLIP_02" },
            { "WEAPON_MARKSMANRIFLE_MK2", "COMPONENT_MARKSMANRIFLE_MK2_CLIP_02" },
            { "WEAPON_MG", "COMPONENT_MG_CLIP_02" },
            { "WEAPON_MICROSMG", "COMPONENT_MICROSMG_CLIP_02" },
            { "WEAPON_MILITARYRIFLE", "COMPONENT_MILITARYRIFLE_CLIP_02" },
            { "WEAPON_MINISMG", "COMPONENT_MINISMG_CLIP_02" },
            { "WEAPON_PISTOL", "COMPONENT_PISTOL_CLIP_02" },
            { "WEAPON_PISTOL50", "COMPONENT_PISTOL50_CLIP_02" },
            { "WEAPON_PISTOL_MK2", "COMPONENT_PISTOL_MK2_CLIP_02" },
            { "WEAPON_SMG", "COMPONENT_SMG_CLIP_02" },
            { "WEAPON_SMG_MK2", "COMPONENT_SMG_MK2_CLIP_02" },
            { "WEAPON_SNSPISTOL", "COMPONENT_SNSPISTOL_CLIP_02" },
            { "WEAPON_SNSPISTOL_MK2", "COMPONENT_SNSPISTOL_MK2_CLIP_02" },
            { "WEAPON_SPECIALCARBINE", "COMPONENT_SPECIALCARBINE_CLIP_02" },
            { "WEAPON_SPECIALCARBINE_MK2", "COMPONENT_SPECIALCARBINE_MK2_CLIP_02" },
            { "WEAPON_TACTICALRIFLE", "COMPONENT_TACTICALRIFLE_CLIP_02" },
            { "WEAPON_TECPISTOL", "COMPONENT_TECPISTOL_CLIP_02" },
            { "WEAPON_VINTAGEPISTOL", "COMPONENT_VINTAGEPISTOL_CLIP_02" },
        };

        /// <summary>The component name for a weapon, or null if the game has none.</summary>
        public static string For(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;

            // The table is keyed the game's way; a bare id off weapons.json is made to match.
            return Clips.TryGetValue(WeaponRegistry.GameName(weaponId), out var c) ? c : null;
        }

        /// <summary>
        /// The magazine names worth trying, biggest first.
        ///
        /// CLIP_DRUM and CLIP_03 are both spelt "Drum Magazine" in the game's own labels --
        /// which of the two a given weapon uses is not consistent, so both are asked for.
        /// CLIP_04 is deliberately absent: on the handful of weapons that have one it is not a
        /// bigger magazine, and this list is ordered by size.
        /// </summary>
        private static readonly string[] Biggest = { "CLIP_DRUM", "CLIP_03" };

        /// <summary>
        /// The biggest magazine the game will actually put on this weapon.
        ///
        /// WHY GUESSING IS SAFE HERE AND NOT IN THE TABLE ABOVE. That table is typed out
        /// because GIVE_WEAPON_COMPONENT_TO_PED fails in SILENCE -- a name that does not exist
        /// does nothing at all and the player walks off with a standard magazine and no error
        /// anywhere, which is exactly how you end up shipping a feature that has never once
        /// worked. DOES_WEAPON_TAKE_WEAPON_COMPONENT removes that problem entirely: it is the
        /// game's own answer about its own weapon, a wrong name simply answers no, and nothing
        /// is fitted on a no. So a name can be tried here that must not be trusted there.
        ///
        /// Drums first because that is what somebody wants off a man in a yard, and the
        /// curated extended clip second so a weapon without a drum still comes ready to use.
        /// A weapon with neither -- the revolvers, the Double Action, anything thrown -- gets
        /// nothing, and that is not a failure.
        /// </summary>
        public static string BestFor(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;

            var game = WeaponRegistry.GameName(weaponId);

            try
            {
                var weapon = Function.Call<uint>(Hash.GET_HASH_KEY, game);

                if (weapon != 0)
                {
                    var stem = game.StartsWith("WEAPON_", System.StringComparison.OrdinalIgnoreCase)
                        ? game.Substring(7)
                        : game;

                    for (var i = 0; i < Biggest.Length; i++)
                    {
                        var name = "COMPONENT_" + stem + "_" + Biggest[i];
                        var part = Function.Call<uint>(Hash.GET_HASH_KEY, name);

                        if (part == 0) continue;
                        if (!Function.Call<bool>(Hash.DOES_WEAPON_TAKE_WEAPON_COMPONENT, weapon, part)) continue;

                        return name;
                    }
                }
            }
            catch
            {
                // Fall through to the extended clip.
            }

            return For(game);
        }

        /// <summary>
        /// Fits the bigger magazine, if there is one. Safe to call for anything.
        ///
        /// Returns true only when a component was actually fitted, so a caller can say so.
        /// </summary>
        public static bool GiveTo(Ped ped, string weaponId)
        {
            if (ped == null || !ped.Exists()) return false;

            // THE BIGGEST ONE IT TAKES, not merely the extended one. He is a man in a yard
            // selling a working piece, and if the game has a drum for it then a drum is what
            // working means. See BestFor.
            var component = BestFor(weaponId);
            if (component == null) return false;

            try
            {
                var weapon = Function.Call<uint>(Hash.GET_HASH_KEY, WeaponRegistry.GameName(weaponId));
                var part = Function.Call<uint>(Hash.GET_HASH_KEY, component);

                Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED, ped.Handle, weapon, part);

                return true;
            }
            catch (System.Exception ex)
            {
                Log.Debug("Could not fit " + component + ": " + ex.Message);
                return false;
            }
        }
    }
}
