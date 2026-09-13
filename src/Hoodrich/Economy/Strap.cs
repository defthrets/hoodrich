using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Economy
{
    /// <summary>
    /// The bag as an object in the world: worn, put down, picked back up.
    ///
    /// WHAT IS IN IT IS THE SATCHEL'S BUSINESS AND WHERE IT IS IS THIS ONE'S. Two files because
    /// they answer to two different things -- the contents are saved state that has to survive
    /// a restart, and the strap on his chest and the holdall on the pavement are a ped
    /// component and a prop, neither of which survives anything. What connects them is a
    /// coordinate, and that is all that gets written down.
    ///
    /// THE STRAP IS A VEST DRAWABLE. Component nine, drawable seven -- on Franklin that is the
    /// bag strap across the chest, which is why you can see whether you are carrying it without
    /// opening anything. It overwrites whatever the wardrobe had in that slot, so what was
    /// there is written down first and handed back when the bag comes off. See Satchel.WasVest.
    ///
    /// DOWN IS A REAL PLACE. Dropping it puts a holdall on the floor where he is stood and a
    /// blip on the map, and that survives the mod being reloaded and the game being restarted
    /// -- the prop does not, but the coordinate does, and the prop is made again the next time
    /// anybody goes near it. Which is the whole reason a player would ever risk leaving it
    /// somewhere: it is still going to be there.
    /// </summary>
    internal sealed class Strap
    {
        /// <summary>The vest slot, and the drawable that is the strap.</summary>
        private const int VestSlot = 9;
        private const int BagDrawable = 7;

        /// <summary>Close enough to pick it up, and how long the button is held to do it.</summary>
        private const float Reach = 2.0f;
        private const int HoldMs = 550;

        /// <summary>How near he has to be before the bag on the floor is a real object again.</summary>
        private const float MakeWithin = 120f;

        private const int EveryMs = 200;

        /// <summary>
        /// What it looks like on the pavement. Tried in order; the first this install has wins.
        ///
        /// A holdall rather than a parcel, because the thing on the floor has to read as YOUR
        /// bag and not as a bag of product -- the dropped-product bags are a different system
        /// with different rules and the two must not look alike. See DroppedBags.
        /// </summary>
        private static readonly string[] Props =
        {
            "prop_cs_heist_bag_01", "prop_michael_backpack", "prop_ld_case_01",
            "prop_cs_heist_bag_02", "prop_drug_package_02"
        };

        private readonly PlayerState _state;

        private Prop _thing;
        private Blip _mark;

        private int _at;
        private int _holdFrom;

        /// <summary>Said once. A body with fewer drawables is a fact, not an event.</summary>
        private bool _moaned;

        /// <summary>Set by Main: whether some screen is up and the buttons belong to it.</summary>
        public Func<bool> Busy;

        public Strap(PlayerState state)
        {
            _state = state;
        }

        private Satchel Bag => _state == null ? null : _state.Bag;

        /// <summary>Whether he is stood near enough to the bag on the floor to take it.</summary>
        public bool InReach { get; private set; }

        public void Update()
        {
            if (_state == null) return;

            int now;

            try { now = Game.GameTime; }
            catch { return; }

            if (now - _at < EveryMs) return;
            _at = now;

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

            var bag = Bag;
            if (bag == null) return;

            Room(bag);

            if (bag.Worn)
            {
                Clear();
                Wear(me);
                return;
            }

            Down(me, now);
        }

        /// <summary>
        /// Carrying the bag means carrying more, and putting it down means carrying less.
        ///
        /// THE POCKETS ARE THE POCKETS AND THE BAG IS ON TOP. Four hundred grams is what a
        /// jacket has always held in this mod; the bag adds its twenty slots to that, so the
        /// readout everywhere -- the phone, the wheel, every capacity check in the economy --
        /// grows by exactly what is on his back and shrinks the moment it is not.
        ///
        /// SET RATHER THAN ADDED TO, on purpose. Adding would compound every pass and a player
        /// who walked round for ten minutes would be carrying a warehouse. The answer is
        /// computed from scratch from one fact -- is it on him -- so there is no way for it to
        /// drift, and no state to get out of step with the bag.
        /// </summary>
        private void Room(Satchel bag)
        {
            if (_state.Stash == null) return;

            var want = Satchel.Pockets + (bag.Worn ? Satchel.Grams : 0f);

            if (Math.Abs(_state.Stash.Capacity - want) < 0.5f) return;

            _state.Stash.Capacity = want;
        }

        // ======================================================================
        // On him
        // ======================================================================

        /// <summary>
        /// Puts the strap back on, if something has taken it off.
        ///
        /// RE-ASSERTED RATHER THAN SET ONCE, because half the mod changes clothes: the wardrobe
        /// writes every component, a mask goes on and off, and the game itself resets the lot
        /// on a respawn. A bag that is only put on once is a bag that is invisible for the rest
        /// of the session after the first time anything else touched him.
        /// </summary>
        private void Wear(Ped me)
        {
            try
            {
                // WHICH STRAP THIS BODY ACTUALLY HAS.
                //
                // Drawable seven is the strap on the outfit it was found on, and the number of
                // drawables in a component CHANGES WITH THE TORSO -- a different top and there
                // may be five, in which case asking for seven is asking for nothing and the
                // game quietly leaves him as he was. Which is exactly what "he doesn't wear it"
                // looks like: no error, no strap, a bag that is definitely on him according to
                // every other part of the mod.
                //
                // So the wanted one if it exists, and the last one this body has if it does
                // not. A strap that is not quite the right strap beats no strap at all, and
                // either way there is something on his chest that says he is carrying it.
                var most = Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS,
                                              me.Handle, VestSlot);

                var want = BagDrawable;

                if (most > 0 && want > most - 1)
                {
                    want = most - 1;

                    if (!_moaned)
                    {
                        _moaned = true;
                        Log.Info("Bag: this body has " + most + " vest drawable(s), so the strap is " +
                                 want + " rather than " + BagDrawable + ".");
                    }
                }

                if (want < 0) return;

                var on = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, VestSlot);

                if (on == want) return;

                // WHAT WAS THERE, WRITTEN DOWN ONCE. Only the first time it takes the slot
                // over -- re-asserting after a wardrobe change would record the bag as the
                // thing to go back to.
                if (Bag.WasVest < 0)
                {
                    Bag.WasVest = on;
                    Bag.WasVestTexture =
                        Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, VestSlot);
                }

                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle,
                              VestSlot, want, 0, 0);
            }
            catch
            {
                // He carries it invisibly, which is wrong but is not lost.
            }
        }

        /// <summary>Hands the vest slot back to whatever the wardrobe had in it.</summary>
        private void Unwear(Ped me)
        {
            try
            {
                var was = Bag.WasVest < 0 ? 0 : Bag.WasVest;

                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle,
                              VestSlot, was, Bag.WasVestTexture, 0);

                Bag.WasVest = -1;
                Bag.WasVestTexture = 0;
            }
            catch
            {
                // Then he is wearing a strap with no bag on the end of it.
            }
        }

        // ======================================================================
        // On the floor
        // ======================================================================

        private void Down(Ped me, int now)
        {
            var where = new Vector3(Bag.DownX, Bag.DownY, Bag.DownZ);
            var gap = me.Position.DistanceTo(where);

            Mark(where);

            if (gap > MakeWithin)
            {
                Unmake();
                InReach = false;
                return;
            }

            Make(where);

            InReach = gap <= Reach && !me.IsInVehicle();
        }

        private void Make(Vector3 where)
        {
            if (_thing != null && _thing.Exists()) return;

            foreach (var name in Props)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _thing = World.CreateProp(model, where, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_thing == null || !_thing.Exists()) continue;

                    _thing.IsPersistent = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _thing.Handle, true, true);

                    // FLAT FIRST, THEN DOWN. Created with no rotation the holdall takes
                    // whatever the model's own axes give it and ends up corner-down on the
                    // carpet like something thrown -- and PLACE_OBJECT_ON_GROUND_PROPERLY only
                    // settles the height, it does not level anything. Pitch and roll are set to
                    // nothing and only the heading is kept, so it lies the way a bag lies.
                    _thing.Rotation = new Vector3(0f, 0f, Bag.DownHeading);

                    Function.Call(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, _thing.Handle);

                    // AND LEVELLED AGAIN AFTERWARDS. The settle can tip it to follow a slope,
                    // which is right for a crate on a hill and wrong for this on a rug.
                    _thing.Rotation = new Vector3(0f, 0f, Bag.DownHeading);

                    Function.Call(Hash.FREEZE_ENTITY_POSITION, _thing.Handle, true);

                    return;
                }
                catch
                {
                    // Next name.
                }
            }
        }

        private void Unmake()
        {
            try { if (_thing != null && _thing.Exists()) _thing.Delete(); }
            catch { /* it streams out */ }

            _thing = null;
        }

        private void Mark(Vector3 where)
        {
            if (_mark != null && _mark.Exists()) return;

            try
            {
                _mark = World.CreateBlip(where);
                if (_mark == null || !_mark.Exists()) return;

                // 175 IS BODY ARMOUR, which is what it was and is not what this is. The
                // plug's drop already wears Package -- see DeadDrop -- and the two must not
                // look alike on a minimap, so the bag takes the drugs-package sprite and a
                // colour of its own. See BLIPS.md.
                _mark.Sprite = (BlipSprite)514;
                _mark.Color = BlipColor.Yellow;
                _mark.Scale = 0.85f;
                _mark.Name = "Your bag";
                _mark.IsShortRange = false;
            }
            catch
            {
                // Then he has to remember where he put it, which is fair.
            }
        }

        private void Clear()
        {
            Unmake();

            try { if (_mark != null && _mark.Exists()) _mark.Delete(); }
            catch { /* it goes */ }

            _mark = null;
            InReach = false;
            _holdFrom = 0;
        }

        // ======================================================================
        // The buttons
        // ======================================================================

        /// <summary>
        /// B puts it down, and holding the context button picks it back up.
        ///
        /// A TAP TO DROP AND A HOLD TO TAKE. Dropping wants to be instant, because the whole
        /// reason to do it is that somebody is coming; picking up wants a hold, because the
        /// context button is also every door, car and conversation in the game and a bag lying
        /// next to a car should not be a coin toss.
        /// </summary>
        public void Keys()
        {
            if (_state == null || Bag == null) return;

            if (Busy != null)
            {
                try { if (Busy()) return; }
                catch { /* then the buttons are ours */ }
            }

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

            if (Bag.Worn)
            {
                if (!Tapped()) return;
                if (me.IsInVehicle()) return;

                Drop(me);
                return;
            }

            if (!InReach)
            {
                _holdFrom = 0;
                return;
            }

            Hud.Hint(null, (Hud.OnPad ? "HOLD DPAD LEFT / E" : "HOLD E") + "   PICK UP THE BAG",
                     0.5f - 0.09f, 0.84f, 0.26f, Palette.Alpha(Palette.Text, 225));

            if (!Holding())
            {
                _holdFrom = 0;
                return;
            }

            var now = Game.GameTime;

            if (_holdFrom == 0)
            {
                _holdFrom = now;
                return;
            }

            if (now - _holdFrom < HoldMs) return;

            _holdFrom = 0;
            Take(me);
        }

        /// <summary>
        /// B, and it does not care what the mod thinks you are holding.
        ///
        /// OnPad IS A GUESS ABOUT THE LAST THING YOU TOUCHED, and it was gating this -- so a
        /// player who had nudged a stick could not drop the bag with the key at all, silently,
        /// with no way to tell that from the feature being broken. The key is read directly and
        /// always; a keyboard B is a keyboard B whatever the wheel last saw.
        /// </summary>
        private static bool Tapped()
        {
            try
            {
                return Game.IsKeyPressed(System.Windows.Forms.Keys.B) &&
                       !Game.IsKeyPressed(System.Windows.Forms.Keys.ShiftKey) &&
                       !Game.IsKeyPressed(System.Windows.Forms.Keys.ControlKey);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Either hand, always, and that is the fix.
        ///
        /// This asked OnPad which control to watch and then watched ONLY that one -- so the
        /// prompt said HOLD DPAD LEFT, the player held E, and nothing happened. Both are
        /// checked now. There is no reading of the situation in which somebody holding either
        /// of these, stood over their own bag, wanted something else to happen.
        /// </summary>
        private static bool Holding()
        {
            try
            {
                return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context) ||
                       Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.ScriptPadLeft) ||
                       Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                return false;
            }
        }

        // ======================================================================
        // Down and up
        // ======================================================================

        public void Drop(Ped me)
        {
            var bag = Bag;
            if (bag == null || !bag.Worn) return;

            var where = me.Position;

            // A STRIDE IN FRONT OF HIM, not inside him. Dropped at his own coordinate the prop
            // lands between his feet and the pickup prompt fights his own collision.
            try { where = me.Position + me.ForwardVector * 0.7f; }
            catch { /* where he is stood will do */ }

            bag.Worn = false;
            bag.DownX = where.X;
            bag.DownY = where.Y;
            bag.DownZ = where.Z;

            // Lying across his path rather than pointing away down it, which is how a bag put
            // down in a hurry actually ends up.
            try { bag.DownHeading = me.Heading + 90f; }
            catch { /* whatever it was lying at before */ }

            Unwear(me);

            try { _state.Touch(); }
            catch { /* the save picks it up on its own clock */ }

            Hud.PlaySound("PUT_AWAY", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Notify.Ticker(bag.IsEmpty
                ? "Bag down."
                : "Bag down -- " + bag.Used + " of " + Satchel.Slots + " still in it.");

            Log.Info("Bag: put down at " + where.X.ToString("0") + ", " + where.Y.ToString("0") +
                     " with " + bag.Used + " slot(s) in it.");
        }

        public void Take(Ped me)
        {
            var bag = Bag;
            if (bag == null || bag.Worn) return;

            bag.Worn = true;

            Clear();
            Wear(me);

            try { _state.Touch(); }
            catch { /* the save picks it up on its own clock */ }

            Hud.PlaySound("PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Notify.Ticker(bag.IsEmpty
                ? "Bag."
                : "Bag -- " + bag.Used + " of " + Satchel.Slots + ".");

            Log.Info("Bag: picked back up with " + bag.Used + " slot(s) in it.");
        }

        public void RestoreWorld()
        {
            Clear();
        }
    }
}
