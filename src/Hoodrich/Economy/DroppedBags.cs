using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>One bag on the floor, and what is in it.</summary>
    internal sealed class Bag
    {
        public Prop Thing;
        public Blip Mark;

        public string DrugId = "";
        public float Grams;
        public float Purity = 1f;

        /// <summary>True for street-ready units, false for uncut weight -- it goes back the
        /// way it came out.</summary>
        public bool Bagged;

        public int DroppedAt;
        public Vector3 Where;
    }

    /// <summary>
    /// Product you have put down, and can pick back up.
    ///
    /// The one thing you can do to your own pockets that is not selling, and it exists because
    /// of the search: a man who can see a patrol coming has, at the moment, exactly one option,
    /// which is to run. Putting the bag in a hedge and walking back for it afterwards is the
    /// other one, and it is the one everybody would actually try.
    ///
    /// Deliberately not a stash. It is a prop on the pavement with a blip on it and half an
    /// hour before the street takes it -- no capacity, no menu, no interest. Anything more and
    /// it stops being somewhere to put a bag in a hurry and becomes a second inventory.
    /// </summary>
    internal sealed class DroppedBags
    {
        /// <summary>
        /// How long a bag lasts. Thirty minutes of real time, as asked for.
        ///
        /// GameTime rather than the clock in the sky, because that one runs at thirty times
        /// real speed and would take the bag in a minute.
        /// </summary>
        private const int LifetimeMs = 30 * 60 * 1000;

        /// <summary>Close enough to pick it up.</summary>
        private const float ReachRange = 1.6f;

        /// <summary>And far enough that you are not picking up what you just put down.</summary>
        private const int SettleMs = 1500;

        private const int CheckMs = 400;

        /// <summary>
        /// What a bag looks like on the floor. Tried in order; the first this install has wins.
        ///
        /// A weed bag for weight, a package for anything bagged up, and a plain holdall behind
        /// both -- the point is that there is an object where you left the thing, not that the
        /// object is the correct shape of parcel.
        /// </summary>
        private static readonly string[] Props =
        {
            "prop_weed_bottle", "hei_prop_heist_weed_block", "prop_drug_package_02",
            "prop_drug_package", "prop_michael_backpack", "prop_cs_heist_bag_01"
        };

        private readonly List<Bag> _bags = new List<Bag>();

        private int _next;

        /// <summary>Set by Main. Where a picked-up bag goes back to.</summary>
        public Stash Pockets;

        public int Count => _bags.Count;

        /// <summary>
        /// Puts it on the floor in front of you.
        ///
        /// The stash is asked to hand the product over FIRST and the bag carries whatever it
        /// actually got. Making the prop and then taking the product is how you end up with a
        /// bag of nothing on the pavement when a rounding error says the pocket was emptier
        /// than the screen thought.
        /// </summary>
        public string Drop(string drugId, float grams, bool bagged)
        {
            if (Pockets == null || string.IsNullOrEmpty(drugId) || grams <= 0.005f) return null;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return null;

            var purity = bagged ? Pockets.PurityOf(drugId) : Pockets.BulkPurityOf(drugId);

            var got = bagged
                ? Pockets.RemovePackaged(drugId, grams)
                : Pockets.RemoveBulk(drugId, grams);

            if (got <= 0.005f) return null;

            var at = player.Position + player.ForwardVector * 1.0f;

            var bag = new Bag
            {
                DrugId = drugId,
                Grams = got,
                Purity = purity,
                Bagged = bagged,
                DroppedAt = Game.GameTime,
                Where = at
            };

            Make(bag, at);

            _bags.Add(bag);

            Log.Info("Dropped " + got.ToString("0.#") + "g of " + drugId +
                     (bagged ? " (bagged)" : " (weight)") + " at " + at + ".");

            return null;
        }

        private void Make(Bag bag, Vector3 at)
        {
            foreach (var name in Props)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1000)) continue;

                    bag.Thing = World.CreateProp(model, at, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (bag.Thing == null || !bag.Thing.Exists()) continue;

                    bag.Thing.IsPersistent = true;

                    // On the floor rather than floating at hip height, and left alone after
                    // that. PLACE_OBJECT_ON_GROUND_PROPERLY drops it the last few inches from
                    // wherever it was made, which is what a thrown bag does anyway.
                    Function.Call(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, bag.Thing.Handle);

                    bag.Where = bag.Thing.Position;
                    break;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not make a bag out of " + name + ": " + ex.Message);
                }
            }

            // The blip goes on whether the prop loaded or not. A bag you cannot see but can
            // walk back to is recoverable; one with nothing marking it is gone.
            try
            {
                bag.Mark = bag.Thing != null && bag.Thing.Exists()
                    ? bag.Thing.AddBlip()
                    : World.CreateBlip(at);

                if (bag.Mark != null && bag.Mark.Exists())
                {
                    // 514 is radar_drugs_package -- a parcel, which is what this is.
                    Function.Call(Hash.SET_BLIP_SPRITE, bag.Mark.Handle, 514);
                    Function.Call(Hash.SET_BLIP_COLOUR, bag.Mark.Handle, 2);

                    bag.Mark.Scale = 0.7f;
                    bag.Mark.IsShortRange = true;
                    bag.Mark.Name = "Dropped bag";
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip a dropped bag: " + ex.Message);
            }
        }

        public void Update()
        {
            if (_bags.Count == 0) return;
            if (Game.GameTime < _next) return;
            _next = Game.GameTime + CheckMs;

            var player = Game.Player.Character;
            var at = player != null && player.Exists() ? player.Position : Vector3.Zero;

            for (var i = _bags.Count - 1; i >= 0; i--)
            {
                var bag = _bags[i];
                var age = Game.GameTime - bag.DroppedAt;

                // Somebody had it away. Nothing is announced -- half an hour is long enough
                // that a message about a bag you forgot about is not news.
                if (age > LifetimeMs)
                {
                    Bin(bag);
                    _bags.RemoveAt(i);
                    continue;
                }

                if (at == Vector3.Zero || age < SettleMs) continue;

                var where = bag.Thing != null && bag.Thing.Exists() ? bag.Thing.Position : bag.Where;
                if (at.DistanceTo(where) > ReachRange) continue;

                Take(bag);
                _bags.RemoveAt(i);
            }
        }

        /// <summary>
        /// Back in your pockets, or as much of it as will fit.
        ///
        /// What will not fit stays on the floor, which is the only honest answer: a bag that
        /// vanishes into a full pocket has destroyed product, and one that refuses to be picked
        /// up at all leaves you stood on top of it pressing nothing.
        /// </summary>
        private void Take(Bag bag)
        {
            var back = bag.Bagged
                ? Pockets.AddPackaged(bag.DrugId, bag.Grams, bag.Purity)
                : Pockets.AddBulk(bag.DrugId, bag.Grams, bag.Purity);

            var left = bag.Grams - back;

            if (back > 0.005f)
            {
                UI.Notify.Ticker("~g~Picked it back up.~s~  " + back.ToString("0.#") + "g" +
                                 (left > 0.005f ? "  ~o~(" + left.ToString("0.#") +
                                                  "g left -- no room)" : ""));
            }
            else
            {
                UI.Notify.Problem("no room for that.");
            }

            if (left > 0.005f)
            {
                // Still somebody's bag. The clock is not restarted: it has been on that
                // pavement the whole time and being briefly picked at does not change that.
                bag.Grams = left;
                _bags.Add(bag);
                return;
            }

            Bin(bag);
        }

        private static void Bin(Bag bag)
        {
            try { if (bag.Mark != null && bag.Mark.Exists()) bag.Mark.Delete(); }
            catch { /* it is gone */ }

            try
            {
                if (bag.Thing != null && bag.Thing.Exists())
                {
                    bag.Thing.IsPersistent = false;
                    bag.Thing.Delete();
                }
            }
            catch { /* it is gone */ }

            bag.Mark = null;
            bag.Thing = null;
        }

        /// <summary>
        /// Everything on the floor goes when the mod does.
        ///
        /// Not handed back. A persistent prop with a blip on it left behind on somebody's save
        /// is litter nothing in the game knows how to clear up.
        /// </summary>
        public void RestoreWorld()
        {
            foreach (var bag in _bags) Bin(bag);
            _bags.Clear();
        }
    }
}
