using System;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// A balaclava, on or off.
    ///
    /// COMPONENT 1 IS THE WHOLE PROBLEM. On a freemode ped it is the mask slot. On a story ped
    /// -- and Franklin is one -- it is the BEARD slot, with the shop masks somewhere in the
    /// same list and later DLC beards after those, and nothing in the game says which index is
    /// which. BikeRide had a MaskUp that took "the last valid drawable in the slot" and it put
    /// a beard on him every time; the note on its grave is still in that file.
    ///
    /// So this does not guess. The index comes from the ini, it is checked with
    /// IS_PED_COMPONENT_VARIATION_VALID before it goes anywhere near his head, and the Settings
    /// app has a slider that re-applies it live so the right number is found by LOOKING -- the
    /// only way it can be. The count of what is in the slot goes to the log so anybody
    /// reading it knows the range.
    ///
    /// WHAT WAS UNDER IT IS REMEMBERED. Taking the mask off sets the slot back to what it was
    /// before, not to zero, because zero is clean-shaven and a man who had a goatee before he
    /// masked up should have one after. Across a reload the memory is gone; then it is zero
    /// and the log says so.
    ///
    /// It also does not fight anybody. A cutscene, a mission outfit or another mod that
    /// changes the slot wins: Update notices the slot no longer holds the mask and simply
    /// stops claiming it is on.
    /// </summary>
    internal static class Mask
    {
        /// <summary>"berd". Beards, masks, and on this character both.</summary>
        public const int Slot = 1;

        /// <summary>What the slot held before the mask went on, or -1 for not known.</summary>
        private static int _wore = -1;
        private static int _woreTexture = -1;

        private static bool _on;
        private static int _nextLook;

        private const int LookEveryMs = 500;

        /// <summary>Whether the balaclava is on right now.</summary>
        public static bool Wearing => _on;

        /// <summary>
        /// On if it is off, off if it is on. Returns why it could not, or empty for done.
        /// </summary>
        public static string Toggle(Settings cfg)
        {
            return _on ? Off() : On(cfg);
        }

        /// <summary>How many drawables the slot has on the current ped, or 0 if nobody is there.</summary>
        public static int Count()
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return 0;

                return Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, me.Handle, Slot);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>How many textures one drawable has, or 0.</summary>
        public static int Textures(int drawable)
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return 0;

                return Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS,
                                          me.Handle, Slot, drawable);
            }
            catch
            {
                return 0;
            }
        }

        public static string On(Settings cfg)
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return "Nobody to mask.";

                var many = Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, me.Handle, Slot);

                var drawable = cfg.MaskDrawable;
                var texture = cfg.MaskTexture;

                if (drawable < 1 || drawable >= many)
                {
                    Log.Warn("Mask: drawable " + drawable + " is not in the slot -- component " +
                             Slot + " has " + many + " on this character. Settings > Mask.");
                    return "That mask isn't in the slot. Settings > Mask.";
                }

                var textures = Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS,
                                                  me.Handle, Slot, drawable);

                if (texture < 0 || texture >= Math.Max(1, textures)) texture = 0;

                if (!Function.Call<bool>(Hash.IS_PED_COMPONENT_VARIATION_VALID,
                                         me.Handle, Slot, drawable, texture))
                {
                    Log.Warn("Mask: drawable " + drawable + " texture " + texture +
                             " is not valid on this character.");
                    return "That mask isn't valid on him. Settings > Mask.";
                }

                // Remembered ONCE, on the way from bare to masked. Refresh comes through here
                // too, and remembering again then would remember the mask as what was under it.
                if (!_on)
                {
                    _wore = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, Slot);
                    _woreTexture = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, Slot);
                }

                var palette = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, me.Handle, Slot);

                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, Slot,
                              drawable, texture, palette);

                _on = true;

                Log.Info("Mask on: component " + Slot + " drawable " + drawable + " texture " +
                         texture + " (slot has " + many + ", this one has " + textures +
                         " texture(s)); was " + _wore + "/" + _woreTexture + ".");

                return "";
            }
            catch (Exception ex)
            {
                Log.Debug("Mask: could not put it on: " + ex.Message);
                return "Couldn't get it on.";
            }
        }

        public static string Off()
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return "Nobody to unmask.";

                // Not known is a reload: the mask survived it and the memory did not. Zero is
                // the honest fallback and the log says it happened.
                var back = _wore < 0 ? 0 : _wore;
                var backTexture = _woreTexture < 0 ? 0 : _woreTexture;

                if (_wore < 0) Log.Info("Mask off: what was under it is not known, using 0.");

                var palette = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, me.Handle, Slot);

                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, Slot,
                              back, backTexture, palette);

                _on = false;
                _wore = -1;
                _woreTexture = -1;

                return "";
            }
            catch (Exception ex)
            {
                Log.Debug("Mask: could not take it off: " + ex.Message);
                return "Couldn't get it off.";
            }
        }

        /// <summary>
        /// Re-applies the configured mask if one is on. This is what the Settings slider calls
        /// as it moves, so the number is found by watching his head change.
        /// </summary>
        public static void Refresh(Settings cfg)
        {
            if (!_on) return;

            var why = On(cfg);

            // A bad number mid-scroll is not a reason to leave him bare: the last good one is
            // still on his head, and the next notch may be fine.
            if (!string.IsNullOrEmpty(why)) Log.Debug("Mask: refresh skipped -- " + why);
        }

        /// <summary>
        /// Keeps Wearing honest against whatever else touches his head. Cheap, on a clock.
        /// </summary>
        public static void Update(Settings cfg)
        {
            var now = Game.GameTime;
            if (now < _nextLook) return;
            _nextLook = now + LookEveryMs;

            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return;

                var has = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, Slot);

                if (_on && has != cfg.MaskDrawable)
                {
                    // Somebody else changed it -- a cutscene, an outfit, another mod. They win,
                    // and the memory of what was under it is no longer true either.
                    _on = false;
                    _wore = -1;
                    _woreTexture = -1;

                    Log.Info("Mask: the slot changed under it; no longer counted as on.");
                }
                else if (!_on && cfg.MaskDrawable >= 1 && has == cfg.MaskDrawable)
                {
                    // On his head without this having put it there -- a reload, most likely.
                    // Counted as on so the tile says the truth; what was under it is not known.
                    _on = true;
                }
            }
            catch
            {
                // Next look.
            }
        }

        /// <summary>For a teardown. Off if it is on; quiet if it is not.</summary>
        public static void RestoreWorld()
        {
            if (_on) Off();
        }
    }
}
