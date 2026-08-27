using System;
using System.Drawing;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Paint
{
    /// <summary>
    /// The extinguisher itself: getting one, and painting the can.
    ///
    /// THE CAN CANNOT BE AN ARBITRARY COLOUR and it is worth saying why rather than quietly
    /// approximating. A weapon's appearance comes from its model's textures, and the only hook
    /// a script has into that is the tint index -- a fixed table of eight the game ships. There
    /// is no native that takes an RGB, because there is nothing for it to write to; making the
    /// can a true match for the paint means editing w_am_fire_exting's texture dictionary,
    /// which is an OpenIV job and a different kind of mod.
    ///
    /// Eight is still eight. Picking a hot pink and watching the can go pink is most of the
    /// feeling, so the tint is snapped to whichever of the eight is nearest and the picker
    /// shows which one it landed on -- an honest approximation the player can see, rather than
    /// a silent one they find out about.
    /// </summary>
    internal sealed class Can
    {
        public const string Weapon = "WEAPON_FIREEXTINGUISHER";

        /// <summary>
        /// Roughly what the eight tints look like.
        ///
        /// EYEBALLED, and flagged as such. The dump gives their names and their indices and not
        /// their colours -- there is nowhere in the data that says what "Army tint" is in RGB --
        /// so these are close enough to sort a hue into the right bucket and no better. If a
        /// colour keeps landing on the wrong can, these numbers are the thing to nudge.
        /// </summary>
        private static readonly Color[] TintLooks =
        {
            Color.FromArgb(255,  32,  32,  34),   // 0 Black
            Color.FromArgb(255,  62, 132,  66),   // 1 Green
            Color.FromArgb(255, 198, 162,  58),   // 2 Gold
            Color.FromArgb(255, 226, 112, 176),   // 3 Pink
            Color.FromArgb(255,  92,  96,  70),   // 4 Army
            Color.FromArgb(255,  54,  76, 120),   // 5 LSPD
            Color.FromArgb(255, 224, 120,  42),   // 6 Orange
            Color.FromArgb(255, 214, 214, 220)    // 7 Platinum
        };

        private static readonly string[] TintNames =
        {
            "black", "green", "gold", "pink", "army", "LSPD", "orange", "platinum"
        };

        private int _lastTint = -1;

        /// <summary>Which of the eight the current colour rounds to.</summary>
        public static int NearestTint(Color c)
        {
            var best = 0;
            var bestD = double.MaxValue;

            for (var i = 0; i < TintLooks.Length; i++)
            {
                var t = TintLooks[i];

                // Straight RGB distance. A perceptual space would be more correct and it is
                // eight buckets -- the extra maths would be answering a question nobody asked.
                var dr = c.R - t.R;
                var dg = c.G - t.G;
                var db = c.B - t.B;

                var d = (double)dr * dr + (double)dg * dg + (double)db * db;

                if (d >= bestD) continue;

                bestD = d;
                best = i;
            }

            return best;
        }

        public static string TintName(int i)
        {
            return i >= 0 && i < TintNames.Length ? TintNames[i] : "?";
        }

        /// <summary>Whether he is holding one at all.</summary>
        public static bool Has()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return false;

                var hash = Function.Call<uint>(Hash.GET_HASH_KEY, Weapon);
                return Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Whether it is the thing currently in his hands.</summary>
        public static bool Out()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return false;

                var want = Function.Call<uint>(Hash.GET_HASH_KEY, Weapon);
                return Function.Call<uint>(Hash.GET_SELECTED_PED_WEAPON, me.Handle) == want;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Puts one in his hands.
        ///
        /// Vanilla leaves them lying about in fire stations and a handful of interiors, which
        /// is a scavenger hunt before you can use the mod at all. Since the picker IS the mod's
        /// front door, opening it hands you one.
        /// </summary>
        public static void Give(bool equip)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var hash = Function.Call<uint>(Hash.GET_HASH_KEY, Weapon);

                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false))
                {
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, me.Handle, hash, 5000, false, false);
                    Log.Info("Handed over an extinguisher.");
                }

                // Topped up whether it was new or not. Asking for a fresh one and getting the
                // empty one you were already carrying is not what the button says it does.
                var max = new OutputArgument();
                Function.Call(Hash.GET_MAX_AMMO, me.Handle, hash, max);

                var full = max.GetResult<int>();
                if (full > 0) Function.Call(Hash.SET_PED_AMMO, me.Handle, hash, full, false);

                if (equip) Function.Call(Hash.SET_CURRENT_PED_WEAPON, me.Handle, hash, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not hand over an extinguisher: " + ex.Message);
            }
        }

        /// <summary>
        /// Paints the can to whichever tint is nearest, when it changes.
        ///
        /// Only on a change: SET_PED_WEAPON_TINT_INDEX every frame is sixty pointless natives a
        /// second for a value that moves when somebody turns a dial.
        /// </summary>
        public void Match(Color c, bool on)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var want = on ? NearestTint(c) : 0;
                if (want == _lastTint) return;

                var hash = Function.Call<uint>(Hash.GET_HASH_KEY, Weapon);
                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false)) return;

                Function.Call(Hash.SET_PED_WEAPON_TINT_INDEX, me.Handle, hash, want);
                _lastTint = want;
            }
            catch
            {
                // The can stays red. Nothing else depends on it.
            }
        }

        /// <summary>Back to the factory red, for when paint mode goes off.</summary>
        public void Reset()
        {
            _lastTint = -1;
            _lastAmmo = -1;
            _owed = 0f;
        }

        // ---- how long it lasts --------------------------------------------------

        private int _lastAmmo = -1;
        private float _owed;

        /// <summary>
        /// Gives back some of what was just spent.
        ///
        /// BY REFUND RATHER THAN BY FLAG, deliberately. SET_PED_INFINITE_AMMO would be one
        /// call and it would leave a switch flipped on the player's weapon that outlives this
        /// mod being unloaded -- and the one promise this thing makes is that the extinguisher
        /// is never actually modified. Watching what it spends and handing part of it back
        /// stops the instant nothing is calling it.
        ///
        /// Refunding the whole amount is a can, which never runs down. Refunding two thirds is
        /// a tank that lasts three times as long, because only the remaining third is ever
        /// really gone. The fraction falls out of the multiplier rather than being a second
        /// number that has to agree with it.
        /// </summary>
        public void Feed(PaintConfig cfg)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) { _lastAmmo = -1; return; }

                var hash = Function.Call<uint>(Hash.GET_HASH_KEY, Weapon);

                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false))
                {
                    _lastAmmo = -1;
                    _owed = 0f;
                    return;
                }

                var now = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, me.Handle, hash);

                // First look, or he picked some up somewhere. Either way there is nothing owed.
                if (_lastAmmo < 0 || now > _lastAmmo)
                {
                    _lastAmmo = now;
                    return;
                }

                var spent = _lastAmmo - now;

                if (spent <= 0) return;

                // Endless means every unit spent is handed straight back, so the gauge never
                // moves. Otherwise two thirds come back, which is a tank that lasts three
                // times as long -- the fraction falls out of the multiplier rather than being
                // a second number that has to agree with it.
                var endless = cfg.SprayCanLook ? !cfg.CanRunsOut : !cfg.ExtinguisherRunsOut;

                var keep = endless ? 1f : 1f - 1f / Math.Max(1f, cfg.ExtinguisherLasts);

                _owed += spent * keep;

                // Whole units only -- ammo is an integer, and the remainder is carried rather
                // than dropped so the ratio stays exact over a long hold instead of drifting
                // short by up to one unit every tick.
                var give = (int)_owed;

                if (give > 0)
                {
                    Function.Call(Hash.ADD_AMMO_TO_PED, me.Handle, hash, give);
                    _owed -= give;

                    now = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, me.Handle, hash);
                }

                _lastAmmo = now;
            }
            catch
            {
                // It empties at the stock rate. Nothing else depends on this.
            }
        }
    }
}
