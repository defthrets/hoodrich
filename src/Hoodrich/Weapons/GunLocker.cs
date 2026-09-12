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
    /// AND THE ROUNDS IT HAD. This used to hand the gun back empty, on the reasoning that
    /// rounds are Stretch's to sell -- and the first thing anybody saw after a load was a gun
    /// with nothing in it and a trip to buy what they already had. So the count is written
    /// down with the gun, at every save and every time the locker looks, and comes back with
    /// it. A gun he still has after a load is topped up to the count once, on the first look,
    /// and never again: the game's own save is the truth from then on, and a locker that
    /// refilled a magazine he had just emptied would be a cheat.
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

        /// <summary>The one top-up, done. See the class note.</summary>
        private bool _topped;

        /// <summary>
        /// Writes down what each bought gun has in it. At every save, so a shutdown a second
        /// after buying rounds does not forget them.
        /// </summary>
        public void Snapshot()
        {
            if (_state == null || _state.GunsBought.Count == 0) return;

            Ped me;

            try
            {
                me = Game.Player.Character;
                if (me == null || !me.Exists()) return;
            }
            catch
            {
                return;
            }

            for (var i = 0; i < _state.GunsBought.Count; i++)
            {
                var name = _state.GunsBought[i];
                if (string.IsNullOrEmpty(name)) continue;

                try
                {
                    var hash = Function.Call<uint>(Hash.GET_HASH_KEY, name);
                    if (hash == 0) continue;
                    if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false)) continue;

                    var rounds = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, me.Handle, hash);

                    int had;
                    if (!_state.GunAmmo.TryGetValue(name, out had) || had != rounds)
                    {
                        _state.GunAmmo[name] = rounds;
                        _state.Touch();
                    }
                }
                catch
                {
                    // One that cannot be asked is left as it was.
                }
            }
        }

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
        /// He put one in the boot of his car. Off the list, or the locker hands him another
        /// after the next death and the boot is a gun factory.
        /// </summary>
        public void Stowed(string weapon)
        {
            if (_state == null || string.IsNullOrEmpty(weapon)) return;
            if (!_state.GunsBought.Remove(weapon)) return;

            _state.Touch();
            Log.Info("Locker: " + weapon + " is in a boot (" + _state.GunsBought.Count + " held).");
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

                    int rounds;
                    if (!_state.GunAmmo.TryGetValue(name, out rounds)) rounds = 0;

                    if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false))
                    {
                        // He has it, so this is the moment to write down what is on it. Once the
                        // gun goes, its components go with it and there is nothing left to read.
                        Remember(name, Attachments.On(me, name));

                        // THE ONE TOP-UP, on the first look after a load: a game save older
                        // than the mod's has the gun with fewer rounds than he had. See the
                        // class note for why never again.
                        if (!_topped && rounds > 0)
                        {
                            var has = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, me.Handle, hash);
                            if (has < rounds) Function.Call(Hash.SET_PED_AMMO, me.Handle, hash, rounds);
                        }

                        continue;
                    }

                    // With the rounds it had. See the class note.
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, me.Handle, hash, rounds, false, false);

                    // WHAT HE HAD BOLTED TO IT FIRST, THEN THE MAGAZINE -- and the magazine
                    // only if he had not chosen one himself. Components share slots: a clip
                    // fitted after the saved list replaces whichever clip the list carried, so
                    // the order decides which wins, and the one he picked at the counter
                    // should. The old order put the big magazine on first and then let the
                    // saved list knock it straight back off.
                    var saved = PartsFor(name);
                    parts += Attachments.GiveTo(me, name, saved);

                    var chose = false;
                    if (saved != null)
                    {
                        foreach (var p in saved)
                        {
                            if (p != null && p.IndexOf("_CLIP_", StringComparison.OrdinalIgnoreCase) >= 0) chose = true;
                        }
                    }

                    var def = _guns == null ? null : _guns.Get(hash);
                    if (!chose && def != null) ExtendedClips.GiveTo(me, def.Id);

                    back++;
                }
                catch
                {
                    // One that will not come back is not worth losing the others over.
                }
            }

            _topped = true;

            // What they hold now is what the next save remembers.
            Snapshot();

            if (back <= 0) return;

            Log.Info("Locker: handed back " + back + " gun" + (back == 1 ? "" : "s") +
                     (parts > 0 ? " with " + parts + " part(s) back on them" : "") + ".");

            UI.Notify.Ticker("~g~Your pieces are still yours.~s~  " + back +
                             (back == 1 ? " gun" : " guns") + " back off the shelf.");
        }
    }
}
