using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Weapons
{
    /// <summary>One weapon and the art the game already ships for it.</summary>
    internal sealed class WeaponDef
    {
        public string Id = "";
        public uint Hash;
        public string Name = "";

        /// <summary>Which of the eight vanilla wheel slots this belongs in.</summary>
        public string Slot = "";

        /// <summary>
        /// The weapon's MODEL name, e.g. w_pi_pistol. The game stores each weapon's art in a
        /// streamed texture dictionary of that name containing a texture of the same name, so
        /// this single string is both the dict and the texture.
        /// </summary>
        public string Icon = "";

        public override string ToString() => Id;
    }

    /// <summary>
    /// The weapon catalogue, and the bookkeeping for streaming weapon art.
    ///
    /// Hoodrich takes over the weapon-wheel button, so it owes the player a way to change
    /// weapons. This reproduces the vanilla eight-slot layout using the game's own icons rather
    /// than inventing new ones.
    /// </summary>
    internal sealed class WeaponRegistry
    {
        /// <summary>WEAPON_UNARMED. Always available, and never listed in a slot.</summary>
        public const uint UnarmedHash = 2725352035; // WEAPON_UNARMED, 0xA2719263

        /// <summary>The vanilla wheel's slots, in the order it draws them.</summary>
        public static readonly string[] SlotOrder =
        {
            "Melee", "Handguns", "SMGs", "Shotguns", "Rifles", "Sniper", "Heavy", "Thrown"
        };

        private readonly List<WeaponDef> _all = new List<WeaponDef>();
        private readonly Dictionary<uint, WeaponDef> _byHash = new Dictionary<uint, WeaponDef>();

        /// <summary>Dicts we have asked the streamer for, so we only request each once.</summary>
        private readonly HashSet<string> _requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Dicts confirmed missing, so a bad name is not retried every frame forever.</summary>
        private readonly HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _requestedAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How long to wait for a dict before writing it off as absent.</summary>
        private const int StreamTimeoutMs = 4000;

        public IReadOnlyList<WeaponDef> All => _all;

        public WeaponDef Get(uint hash) => _byHash.TryGetValue(hash, out var d) ? d : null;

        public static WeaponRegistry Load()
        {
            var reg = new WeaponRegistry();

            var path = Path.Combine(Paths.Data, "weapons.json");
            var doc = JsonFile.Read(path);
            if (doc == null)
            {
                Log.Warn("No weapons.json found; the weapon wheel will fall back to text labels only.");
                return reg;
            }

            var list = doc.Kind == JsonKind.Array ? doc : doc["weapons"];
            if (list.Kind != JsonKind.Array)
            {
                Log.Warn("weapons.json has no top-level array.");
                return reg;
            }

            foreach (var node in list.Items)
            {
                // The dump stores hashes as signed; reinterpret rather than clamp.
                var hash = unchecked((uint)node["hash"].AsLong(0));
                if (hash == 0) continue;

                var def = new WeaponDef
                {
                    Id = node["id"].AsString(""),
                    Hash = hash,
                    Name = node["name"].AsString(node["id"].AsString("Weapon")),
                    Slot = node["slot"].AsString("Heavy"),
                    Icon = node["icon"].AsString("")
                };

                if (reg._byHash.ContainsKey(hash))
                {
                    Log.Debug("Duplicate weapon hash " + hash + " (" + def.Id + ") ignored.");
                    continue;
                }

                reg._byHash[hash] = def;
                reg._all.Add(def);
            }

            Log.Info("Weapons loaded: " + reg._all.Count + " across " + SlotOrder.Length + " slots.");
            return reg;
        }

        // ---- what the player is carrying ---------------------------------------

        /// <summary>Weapons the player actually has, grouped into the slot they belong to.</summary>
        public List<WeaponDef> CarriedInSlot(string slot)
        {
            var found = new List<WeaponDef>();

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return found;

            foreach (var def in _all)
            {
                if (!string.Equals(def.Slot, slot, StringComparison.OrdinalIgnoreCase)) continue;
                if (!HasWeapon(player, def.Hash)) continue;
                found.Add(def);
            }

            return found;
        }

        /// <summary>Slots the player has at least one weapon in, in vanilla wheel order.</summary>
        public List<string> OccupiedSlots()
        {
            var slots = new List<string>();
            foreach (var slot in SlotOrder)
            {
                if (CarriedInSlot(slot).Count > 0) slots.Add(slot);
            }
            return slots;
        }

        private static bool HasWeapon(Ped ped, uint hash)
        {
            try
            {
                return Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, ped.Handle, hash, false);
            }
            catch
            {
                return false;
            }
        }

        public static uint CurrentWeaponHash()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return 0;

                var outArg = new OutputArgument();
                Function.Call(Hash.GET_CURRENT_PED_WEAPON, player.Handle, outArg, true);
                return outArg.GetResult<uint>();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Ammo in reserve for a weapon the player is carrying.</summary>
        public static int AmmoFor(uint hash)
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return 0;
                return Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, player.Handle, hash);
            }
            catch
            {
                return 0;
            }
        }

        public static void Equip(uint hash)
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                // forceInHand = true so the swap is immediate rather than played as a draw.
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, hash, true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not equip weapon " + hash, ex);
            }
        }

        // ---- icon streaming ----------------------------------------------------

        /// <summary>
        /// True once a weapon's art is resident and can be drawn.
        ///
        /// Requests are fire-and-forget and each dict is given a few seconds to arrive; a name
        /// the game does not recognise is remembered as missing so it is not retried forever.
        /// The caller falls back to a text label, so a wrong name costs an icon, not a menu.
        /// </summary>
        public bool IconReady(WeaponDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Icon)) return false;
            if (_missing.Contains(def.Icon)) return false;

            if (Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, def.Icon)) return true;

            var now = Game.GameTime;
            if (!_requested.Contains(def.Icon))
            {
                _requested.Add(def.Icon);
                _requestedAt[def.Icon] = now;
                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, def.Icon, false);
                return false;
            }

            if (_requestedAt.TryGetValue(def.Icon, out var asked) && now - asked > StreamTimeoutMs)
            {
                _missing.Add(def.Icon);
                Log.Debug("Weapon texture dict '" + def.Icon + "' never streamed; using a text label for " +
                          def.Name + ".");
                return false;
            }

            Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, def.Icon, false);
            return false;
        }

        /// <summary>
        /// Warms every icon for the weapons the player is carrying. Called when the wheel opens
        /// so art is resident by the time a page is drawn rather than popping in.
        /// </summary>
        public void PrewarmCarried()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            foreach (var def in _all)
            {
                if (string.IsNullOrEmpty(def.Icon)) continue;
                if (_missing.Contains(def.Icon) || _requested.Contains(def.Icon)) continue;
                if (!HasWeapon(player, def.Hash)) continue;

                _requested.Add(def.Icon);
                _requestedAt[def.Icon] = Game.GameTime;
                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, def.Icon, false);
            }
        }

        /// <summary>How many icon dicts failed to stream, for the log.</summary>
        public int MissingIconCount => _missing.Count;
    }
}
