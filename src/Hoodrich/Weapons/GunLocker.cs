using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;

namespace Hoodrich.Weapons
{
    /// <summary>
    /// What you bought off Stretch, still yours tomorrow.
    ///
    /// GIVE_WEAPON_TO_PED hands a piece to the ped, and the ped's loadout is whatever
    /// Rockstar's own save last wrote. So the gun survived exactly as long as the game felt
    /// like remembering it: load a save from before you bought it, or hit any of the several
    /// things that reset a character's weapons, and something you paid for in a shop this mod
    /// invented had quietly never happened.
    ///
    /// So the mod keeps its own list and hands them back.
    ///
    /// NOT THE AMMUNITION. Rounds are a consumable and Stretch sells those separately -- a
    /// locker that refilled itself every load would make half his shop pointless. You get the
    /// gun back with what a fresh one comes with and you buy your own bullets.
    ///
    /// And it is emptied when they are properly taken. Being searched, arrested or killed are
    /// all moments where losing everything is the point, and a locker that undid them would be
    /// cheating on the player's behalf.
    /// </summary>
    internal sealed class GunLocker
    {
        /// <summary>
        /// Long enough after a load for the game to have finished writing the loadout.
        ///
        /// Handing weapons to a ped during the load screen is handing them to a ped the game is
        /// about to overwrite. The check is cheap and repeats, so being late costs nothing and
        /// being early costs the whole feature.
        /// </summary>
        private const int SettleMs = 6000;

        /// <summary>How often to look after that. Rare -- this is a safety net, not a system.</summary>
        private const int CheckMs = 20_000;

        private readonly PlayerState _state;
        private readonly WeaponRegistry _guns;

        private int _next;

        /// <summary>
        /// What was last seen bolted to a gun. Read WHILE HE STILL HAS IT -- by the time the
        /// locker notices one is missing there is nothing left to ask.
        /// </summary>
        private List<string> PartsFor(string weapon)
        {
            var want = weapon + "|";

            for (var i = 0; i < _state.GunParts.Count; i++)
            {
                var row = _state.GunParts[i];

                if (row == null || !row.StartsWith(want, StringComparison.OrdinalIgnoreCase)) continue;

                var bits = row.Split('|');
                var parts = new List<string>();

                for (var b = 1; b < bits.Length; b++)
                {
                    if (!string.IsNullOrEmpty(bits[b])) parts.Add(bits[b]);
                }

                return parts;
            }

            return null;
        }

        /// <summary>
        /// Writes down what is on one, replacing whatever was there.
        ///
        /// RECORDED EVEN WHEN IT IS EMPTY, so taking a scope OFF is remembered as well as
        /// putting one on. Without the empty row he would be handed his old scope back on every
        /// reload for the rest of the save.
        /// </summary>
        private void Remember(string weapon, List<string> parts)
        {
            if (_state == null || parts == null) return;

            var want = weapon + "|";

            _state.GunParts.RemoveAll(
                r => r != null && r.StartsWith(want, StringComparison.OrdinalIgnoreCase));

            _state.GunParts.Add(weapon + "|" + string.Join("|", parts.ToArray()));

            _state.Touch();
        }

        public GunLocker(PlayerState state, WeaponRegistry guns)
        {
            _state = state;
            _guns = guns;
            _next = SettleMs;
        }

        /// <summary>He bought one. Written down.</summary>
        public void Bought(string weapon)
        {
            if (_state == null || string.IsNullOrEmpty(weapon)) return;
            if (_state.GunsBought.Contains(weapon)) return;

            _state.GunsBought.Add(weapon);
            _state.Touch();

            Log.Info("Locker: " + weapon + " is his now (" + _state.GunsBought.Count + " held).");
        }

        /// <summary>
        /// Somebody took the lot -- police, or a hospital bill.
        ///
        /// The whole list goes, not the ones he happens to be missing, because this is the
        /// event the exception exists for: everything is gone and it is meant to be.
        /// </summary>
        public void TakenOffHim(string why)
        {
            if (_state == null || _state.GunsBought.Count == 0) return;

            Log.Info("Locker: " + _state.GunsBought.Count + " guns gone -- " + why + ".");

            _state.GunsBought.Clear();
            _state.Touch();
        }

        /// <summary>Hands back anything on the list he is not carrying.</summary>
        public void Update()
        {
            if (_state == null || _state.GunsBought.Count == 0) return;

            var now = Game.GameTime;
            if (now < _next) return;

            _next = now + CheckMs;

            Ped me;

            try
            {
                me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive) return;
            }
            catch
            {
                return;
            }

            // NOT WHILE THE LAW HAS HIM. Between being busted and the screen fading, the game
            // is in the middle of taking his weapons -- handing them back into that is a race
            // we would sometimes win, which is worse than always losing it, because then the
            // seizure works only sometimes and nobody can tell why.
            try
            {
                if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, false)) return;
            }
            catch
            {
                // If it cannot be asked, carry on.
            }

            var back = 0;
            var parts = 0;

            for (var i = 0; i < _state.GunsBought.Count; i++)
            {
                var name = _state.GunsBought[i];
                if (string.IsNullOrEmpty(name)) continue;

                try
                {
                    var hash = Function.Call<uint>(Hash.GET_HASH_KEY, name);
                    if (hash == 0) continue;

                    if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false))
                    {
                        // He has it, so this is the moment to write down what is on it. Once the
                        // gun goes, its components go with it and there is nothing left to read.
                        Remember(name, Attachments.On(me, name));
                        continue;
                    }

                    // Ammunition is his problem. Zero would be a gun he cannot fire and a
                    // trip back to Stretch for rounds he already thought he had, so it comes
                    // with the same handful a new one does.
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, me.Handle, hash, 0, false, false);

                    var def = _guns == null ? null : _guns.Get(hash);
                    if (def != null) ExtendedClips.GiveTo(me, def.Id);

                    // And everything he had bolted to it. The clip above is the one part the
                    // shop sells; this is the scope, grip, suppressor and light he fitted
                    // himself, which came back missing every time until now.
                    parts += Attachments.GiveTo(me, name, PartsFor(name));

                    back++;
                }
                catch
                {
                    // One that will not come back is not worth losing the others over.
                }
            }

            if (back <= 0) return;

            Log.Info("Locker: handed back " + back + " gun" + (back == 1 ? "" : "s") +
                     (parts > 0 ? " with " + parts + " part(s) back on them" : "") + ".");

            UI.Notify.Ticker("~g~Your pieces are still yours.~s~  " + back +
                             (back == 1 ? " gun" : " guns") + " back off the shelf.");
        }
    }
}
