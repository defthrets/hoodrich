using System;
using System.Collections.Generic;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The closet in his room at Denise's: stand at it and change.
    ///
    /// THE GAME'S OWN WARDROBE STOPPED WORKING HERE the day the story moved him out, and the
    /// mod moved him back in. So this is one of ours: a prompt at the closet door, a menu
    /// of the slots a person actually dresses in (see WardrobeScreen), and what he settles
    /// on written into the save and put back on him when the game loads -- otherwise he
    /// wakes up in whatever the story last dressed him in.
    /// </summary>
    internal sealed class Wardrobe
    {
        private static readonly Vector3 Closet = new Vector3(-18.438f, -1438.564f, 31.102f);
        private const float UseRange = 1.6f;

        private readonly WardrobeScreen _screen;

        public Wardrobe(WardrobeScreen screen)
        {
            _screen = screen;
        }

        public void Update()
        {
            if (_screen.IsOpen) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive || me.IsInVehicle()) return;
                if (me.Position.DistanceTo(Closet) > UseRange) return;

                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to change.");

                if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

                _screen.Open();
            }
            catch (Exception ex)
            {
                Log.Debug("The wardrobe could not ask: " + ex.Message);
            }
        }

        // ---- what he wears, kept ----------------------------------------------------

        /// <summary>Every component and prop slot the game has. See WardrobeScreen for what each is.</summary>
        public static readonly int[] Components = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        public static readonly int[] Props = { 0, 1, 2, 6, 7 };

        /// <summary>
        /// Whether there is a body in front of the camera worth writing down.
        ///
        /// Anything the player can be. It used to be Franklin and only Franklin, which was
        /// right while he was the only body the closet could dress -- now that the rail can put
        /// him in the online models, refusing to record those would mean an hour of choosing
        /// that survives until the next load and no longer.
        /// </summary>
        private static bool Him(Ped me)
        {
            return me != null && me.Exists() && me.Model.IsPed;
        }

        /// <summary>Reads what he has on into the record, as "c:slot:drawable:texture" and "p:slot:drawable:texture".</summary>
        public static void Remember(PlayerState state)
        {
            if (state == null) return;

            var me = Game.Player.Character;
            if (!Him(me)) return;

            // FILED UNDER THE BODY IT WAS WORN ON, because a drawable number is an index into
            // one model's wardrobe and means something else entirely on another. Putting the
            // online man's forty-first jacket on Franklin gets whatever is forty-first on him,
            // or nothing. So the rows carry the model, only rows for the body he is in are
            // ever put back on, and choosing a look for each body keeps each of them.
            var body = unchecked((uint)me.Model.Hash).ToString("X8");

            state.Outfit.RemoveAll(row => row.StartsWith(body + "|", StringComparison.Ordinal));

            try
            {
                foreach (var slot in Components)
                {
                    var d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, slot);
                    var t = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, slot);
                    state.Outfit.Add(body + "|c:" + slot + ":" + d + ":" + t);
                }

                foreach (var slot in Props)
                {
                    var d = Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, slot);
                    var t = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, slot);
                    state.Outfit.Add(body + "|p:" + slot + ":" + d + ":" + t);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read what he has on: " + ex.Message);
            }

            state.Touch();
        }

        // ---- how he carries himself --------------------------------------------------

        /// <summary>
        /// The walks, his own first.
        ///
        /// EVERY ONE OF THESE IS IN THE GAME'S OWN ANIMATION LIST, checked against it rather
        /// than remembered -- a movement clipset that does not exist is not an error, it is a
        /// man who carries on walking exactly as he did, which is indistinguishable from the
        /// setting not working.
        ///
        /// An empty name is his own, and it is first because it is the one he came with.
        /// </summary>
        public static readonly string[] Walks =
        {
            "", "move_m@gangster@a", "move_m@gangster@ng", "move_m@gangster@generic",
            "move_m@casual@a", "move_m@casual@d", "move_m@hurry@a"
        };

        public static readonly string[] WalkNames =
        {
            "HIS OWN", "GANGSTER", "GANGSTER 2", "GANGSTER 3", "CASUAL", "CASUAL 2", "IN A HURRY"
        };

        /// <summary>
        /// How he holds a gun.
        ///
        /// THESE ARE THE GAME'S OWN NAMES OUT OF weaponanimations.meta, and that file is
        /// inside the archives, so unlike every animation and scenario in this mod they cannot
        /// be checked against a list on this machine. What CAN be relied on is the failure:
        /// SET_WEAPON_ANIMATION_OVERRIDE takes a hash, an unknown hash is not an error, and a
        /// name the install does not have simply leaves him shooting the way the game shoots.
        /// So a name that turns out not to exist costs a menu row that does nothing, and never
        /// a broken animation.
        ///
        /// SIDEWAYS IS "Ganged". "Gang" was already here and it is the gangster stance --
        /// shoulders, stride, where he holds it -- but the pistol stays upright in it, which is
        /// not what anybody means when they say they want to hold it like the men on the
        /// corner. Ganged is the turned wrist.
        ///
        /// And Ganged2h is the same idea for anything he needs both hands for, which is a
        /// different animation set and therefore a different row rather than a modifier on
        /// this one -- picking "sideways" and having it do nothing because you were holding a
        /// rifle is the sort of thing nobody reports, they just decide the setting is broken.
        /// </summary>
        public static readonly string[] Shoots = { "", "Gang", "Ganged", "Ganged2h" };

        public static readonly string[] ShootNames =
        {
            "HIS OWN", "GANGSTER", "SIDEWAYS", "SIDEWAYS, TWO HANDS"
        };

        /// <summary>
        /// Puts the walk and the gun hold on him.
        ///
        /// ASKED FOR AND CHECKED, like every other clipset in this mod: a movement clipset
        /// applied before it has streamed in is silently ignored and he walks normally for the
        /// rest of the session. Requested here and put on when it has landed -- the caller
        /// runs this on a timer, so "not yet" is answered by asking again.
        /// </summary>
        /// <summary>The last gun hold that went on, so only a change is logged.</summary>
        private static string _carrying = "";

        /// <returns>False while it is still streaming, so the caller knows to come back.</returns>
        public static bool Carry(PlayerState state)
        {
            if (state == null) return true;

            var me = Game.Player.Character;
            if (!Him(me)) return false;

            var ready = true;

            try
            {
                var walk = state.Walk >= 0 && state.Walk < Walks.Length ? Walks[state.Walk] : "";

                if (walk.Length == 0)
                {
                    Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, me.Handle, 0.35f);
                }
                else
                {
                    Function.Call(Hash.REQUEST_ANIM_SET, walk);

                    if (Function.Call<bool>(Hash.HAS_ANIM_SET_LOADED, walk))
                    {
                        Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, me.Handle, walk, 0.35f);
                    }
                    else
                    {
                        ready = false;
                    }
                }

                var shoot = state.Shoot >= 0 && state.Shoot < Shoots.Length ? Shoots[state.Shoot] : "";

                var style = shoot.Length == 0 ? "Default" : shoot;

                Function.Call(Hash.SET_WEAPON_ANIMATION_OVERRIDE, me.Handle,
                              Function.Call<int>(Hash.GET_HASH_KEY, style));

                // ON THE EDGE, so this is a string compare a tick and not a log file.
                //
                // Worth a line at all because there is no way to ASK the game whether it took
                // -- see the note on Shoots. If one of these ever turns out not to exist on an
                // install, "he picked SIDEWAYS and nothing happened" and "he never picked
                // anything" look identical from the outside, and one line saying which name
                // went on is the difference between a guess and a report.
                if (style != _carrying)
                {
                    Log.Info("Carrying a gun " + style + " from now on.");
                    _carrying = style;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not change how he carries himself: " + ex.Message);
            }

            return ready;
        }

        // ---- the six he has hung up -------------------------------------------------

        /// <summary>How many pegs there are. Six, which is what the game's own wardrobe gives him.</summary>
        public const int Pegs = 6;

        /// <summary>The body he is in, as the key the rows are filed under.</summary>
        private static string BodyKey(Ped me)
        {
            return unchecked((uint)me.Model.Hash).ToString("X8");
        }

        /// <summary>
        /// What is on peg n for the body he is in, or null.
        ///
        /// Returned as the whole row so the caller can read the name off it without going back
        /// to the list; Wearing and Naming both split it the same way.
        /// </summary>
        private static string Row(PlayerState state, Ped me, int peg)
        {
            if (state == null || !Him(me) || peg < 0 || peg >= Pegs) return null;

            var head = peg.ToString(CultureInfo.InvariantCulture) + "|";
            var body = BodyKey(me);

            foreach (var row in state.Outfits)
            {
                if (!row.StartsWith(head, StringComparison.Ordinal)) continue;

                var bits = row.Split('|');
                if (bits.Length != 4) continue;
                if (!string.Equals(bits[2], body, StringComparison.OrdinalIgnoreCase)) continue;

                return row;
            }

            return null;
        }

        /// <summary>What peg n is called, or empty for a bare peg.</summary>
        public static string NameOn(PlayerState state, int peg)
        {
            var row = Row(state, Game.Player.Character, peg);
            if (row == null) return "";

            var bits = row.Split('|');
            return bits.Length == 4 ? bits[1] : "";
        }

        /// <summary>Whether there is anything on peg n.</summary>
        public static bool Used(PlayerState state, int peg)
        {
            return Row(state, Game.Player.Character, peg) != null;
        }

        /// <summary>
        /// Hangs what he has on now on peg n, under a name.
        ///
        /// Read off the ped rather than out of the Outfit record, because the record is only
        /// written when the closet SHUTS -- so hanging one up mid-session would otherwise save
        /// whatever he was wearing when he walked in rather than what he is looking at.
        /// </summary>
        public static bool Hang(PlayerState state, int peg, string name)
        {
            var me = Game.Player.Character;
            if (state == null || !Him(me) || peg < 0 || peg >= Pegs) return false;

            var worn = new List<string>();

            try
            {
                foreach (var slot in Components)
                {
                    var d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, slot);
                    var t = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, slot);
                    worn.Add("c:" + slot + ":" + d + ":" + t);
                }

                foreach (var slot in Props)
                {
                    var d = Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, slot);
                    var t = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, slot);
                    worn.Add("p:" + slot + ":" + d + ":" + t);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read what to hang up: " + ex.Message);
                return false;
            }

            // The name is the player's, so it is cleaned rather than trusted: the separators
            // are the record's own and a name carrying one would split the row in two.
            var clean = (name ?? "").Replace("|", " ").Replace(",", " ").Replace(":", " ").Trim();
            if (clean.Length == 0) clean = "Outfit " + (peg + 1);
            if (clean.Length > 18) clean = clean.Substring(0, 18);

            Strip(state, peg, me);

            state.Outfits.Add(peg.ToString(CultureInfo.InvariantCulture) + "|" + clean + "|" +
                              BodyKey(me) + "|" + string.Join(",", worn.ToArray()));

            state.Touch();

            Log.Info("Hung \"" + clean + "\" on peg " + (peg + 1) + ".");
            return true;
        }

        /// <summary>Renames peg n, leaving what is on it alone. False if the peg is bare.</summary>
        public static bool Rename(PlayerState state, int peg, string name)
        {
            var me = Game.Player.Character;
            var row = Row(state, me, peg);
            if (row == null) return false;

            var bits = row.Split('|');

            var clean = (name ?? "").Replace("|", " ").Replace(",", " ").Replace(":", " ").Trim();
            if (clean.Length == 0) return false;
            if (clean.Length > 18) clean = clean.Substring(0, 18);

            state.Outfits.Remove(row);
            state.Outfits.Add(bits[0] + "|" + clean + "|" + bits[2] + "|" + bits[3]);
            state.Touch();

            return true;
        }

        /// <summary>Takes peg n bare. False if it already was.</summary>
        public static bool Strip(PlayerState state, int peg, Ped me = null)
        {
            me = me ?? Game.Player.Character;

            var row = Row(state, me, peg);
            if (row == null) return false;

            state.Outfits.Remove(row);
            state.Touch();
            return true;
        }

        /// <summary>
        /// Puts what is on peg n back on him.
        ///
        /// Every slot the record carries, in the order it was written, so a hat that needs the
        /// hair under it lands after the hair. Slots the row does not carry are left as they
        /// are rather than blanked -- an outfit saved before a slot was on the rail should not
        /// strip him of it.
        /// </summary>
        public static bool WearPeg(PlayerState state, int peg)
        {
            var me = Game.Player.Character;
            var row = Row(state, me, peg);
            if (row == null) return false;

            var bits = row.Split('|');
            if (bits.Length != 4) return false;

            var put = 0;

            foreach (var one in bits[3].Split(','))
            {
                try
                {
                    var f = one.Split(':');
                    if (f.Length != 4) continue;

                    var slot = int.Parse(f[1], CultureInfo.InvariantCulture);
                    var drawable = int.Parse(f[2], CultureInfo.InvariantCulture);
                    var texture = int.Parse(f[3], CultureInfo.InvariantCulture);

                    if (f[0] == "c")
                    {
                        if (Array.IndexOf(Components, slot) < 0) continue;
                        Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, slot, drawable, texture, 0);
                        put++;
                    }
                    else if (f[0] == "p")
                    {
                        if (Array.IndexOf(Props, slot) < 0) continue;
                        if (drawable < 0) Function.Call(Hash.CLEAR_PED_PROP, me.Handle, slot);
                        else Function.Call(Hash.SET_PED_PROP_INDEX, me.Handle, slot, drawable, texture, true);
                        put++;
                    }
                }
                catch
                {
                    // That slot stays as it is.
                }
            }

            if (put > 0) Log.Info("Put on \"" + bits[1] + "\": " + put + " slot(s).");
            return put > 0;
        }

        /// <summary>
        /// Puts the record back on him. Nothing recorded means he stays as the game dressed
        /// him. Says whether it is done with -- false while the player is somebody else, so
        /// the caller asks again later rather than never.
        /// </summary>
        public static bool Apply(PlayerState state)
        {
            if (state == null || state.Outfit.Count == 0) return true;

            var me = Game.Player.Character;
            if (!Him(me)) return false;

            var put = 0;
            var body = unchecked((uint)me.Model.Hash).ToString("X8");

            foreach (var row in state.Outfit)
            {
                try
                {
                    var entry = row;

                    // Rows from before this was written down carry no body. They were all
                    // Franklin's, because he was the only one the closet could dress.
                    var bar = entry.IndexOf('|');

                    if (bar >= 0)
                    {
                        if (string.Compare(entry.Substring(0, bar), body, StringComparison.OrdinalIgnoreCase) != 0) continue;
                        entry = entry.Substring(bar + 1);
                    }
                    else if ((uint)me.Model.Hash != (uint)PedHash.Franklin)
                    {
                        continue;
                    }

                    var bits = entry.Split(':');
                    if (bits.Length != 4) continue;

                    var slot = int.Parse(bits[1], CultureInfo.InvariantCulture);
                    var drawable = int.Parse(bits[2], CultureInfo.InvariantCulture);
                    var texture = int.Parse(bits[3], CultureInfo.InvariantCulture);

                    if (bits[0] == "c")
                    {
                        if (Array.IndexOf(Components, slot) < 0) continue;
                        Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, slot, drawable, texture, 0);
                        put++;
                    }
                    else if (bits[0] == "p")
                    {
                        if (Array.IndexOf(Props, slot) < 0) continue;
                        if (drawable < 0) Function.Call(Hash.CLEAR_PED_PROP, me.Handle, slot);
                        else Function.Call(Hash.SET_PED_PROP_INDEX, me.Handle, slot, drawable, texture, true);
                        put++;
                    }
                }
                catch
                {
                }
            }

            if (put > 0) Log.Info("Dressed him from the save: " + put + " slot(s).");
            return true;
        }
    }
}
