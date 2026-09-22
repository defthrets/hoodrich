using System;
using System.Collections.Generic;

namespace Hoodrich.Api
{
    /// <summary>
    /// What another mod is allowed to know about the product in your pockets, and nothing else.
    ///
    /// THIS EXISTS BECAUSE THERE WAS NO WAY IN. Hoodrich keeps the stash on PlayerState, which
    /// is an instance held by Main -- so a mod trying to read it by reflection can find the
    /// TYPE and never the object. Bare Minimum wants to show what you are carrying in its own
    /// pocket and let you take it from there, and could not, because nothing static pointed at
    /// the live stash. This is that pointer and only that.
    ///
    /// IT IS THE MIRROR OF Core/Larder, which is how Hoodrich already reads Bare Minimum's
    /// food. Same shape on purpose: one pattern on this machine rather than two.
    ///
    /// ONLY BCL TYPES CROSS -- string, float, bool and arrays of those. The caller reaches
    /// this by REFLECTION and holds no reference to this assembly, so it cannot name DrugDef
    /// or Stash; hand one back and the other side gets an object it can only poke at with
    /// more reflection. mscorlib is the one assembly both mods are guaranteed to agree about.
    ///
    /// NOTHING THROWN LEAVES THIS FILE. An exception crossing a reflection call arrives at the
    /// other end as a TargetInvocationException wrapping a type the caller does not have --
    /// which is a crash in a mod whose author cannot read the stack trace.
    ///
    /// IT IS SAFE BEFORE HOODRICH HAS STARTED. SHVDN builds scripts in whatever order it finds
    /// them, so the other mod can call in before Wire has run. Everything answers "nothing"
    /// until Ready, and the caller is expected to keep asking.
    /// </summary>
    public static class Drugs
    {
        /// <summary>
        /// The contract version. Bumped when a signature here changes in a way that breaks.
        ///
        /// Read by the caller BEFORE anything else: an old client talking to a new Hoodrich
        /// checks this, does not like it, and quietly shows nothing rather than half-calling
        /// an API that has moved.
        /// </summary>
        public static int ApiVersion => 1;

        /// <summary>Hoodrich's own version string, for the other side's log.</summary>
        public static string Version
        {
            get { try { return Core.Build.Version; } catch { return "?"; } }
        }

        private static State.PlayerState _state;
        private static Economy.Drugs _catalogue;
        private static Economy.Highs _highs;

        /// <summary>Called by Main once the state and the catalogue exist. Not for outside use.</summary>
        internal static void Wire(State.PlayerState state, Economy.Drugs catalogue, Economy.Highs highs)
        {
            _state = state;
            _catalogue = catalogue;
            _highs = highs;
        }

        internal static void Unwire()
        {
            _state = null;
            _catalogue = null;
            _highs = null;
            _pricing = null;
        }

        /// <summary>Whether Hoodrich is here AND has finished starting up.</summary>
        public static bool Ready
        {
            get
            {
                try { return _state != null && _state.Stash != null && _catalogue != null && _highs != null; }
                catch { return false; }
            }
        }

        /// <summary>
        /// The ids of everything STREET-READY in your pockets. Bulk weight is not included.
        ///
        /// Deliberate: a brick that has not been cut is not something you take, it is stock.
        /// Bare Minimum is asking what you could put in your mouth, and the answer is the
        /// bagged product.
        /// </summary>
        public static string[] Ids()
        {
            try
            {
                if (!Ready) return new string[0];

                var found = new List<string>();
                foreach (var d in _catalogue.All)
                {
                    if (_state.Stash.PackagedOf(d.Id) > 0.005f) found.Add(d.Id);
                }

                return found.ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>Grams of street-ready product on hand. Zero for anything unknown.</summary>
        public static float GramsOf(string id)
        {
            try { return Ready ? _state.Stash.PackagedOf(id) : 0f; }
            catch { return 0f; }
        }

        /// <summary>What it is called on a menu. The id back if the catalogue has never heard of it.</summary>
        public static string NameOf(string id)
        {
            try
            {
                if (!Ready) return id ?? "";
                var def = _catalogue.Get(id);
                return def == null || string.IsNullOrEmpty(def.Name) ? (id ?? "") : def.Name;
            }
            catch { return id ?? ""; }
        }

        /// <summary>
        /// Counted rather than weighed -- pills against powder.
        ///
        /// The other side needs it to write a number on a tile. Three pills is "3"; three
        /// grams of anything is "3g", and a pill measured in grams reads like a mistake.
        /// </summary>
        public static bool CountedOf(string id)
        {
            try
            {
                if (!Ready) return false;
                var def = _catalogue.Get(id);
                return def != null && def.Counted;
            }
            catch { return false; }
        }

        /// <summary>That much of it, spelled the way this drug counts itself. "" if unknown.</summary>
        public static string AmountOf(string id, float quantity)
        {
            try
            {
                if (!Ready) return "";
                var def = _catalogue.Get(id);
                return def == null ? "" : def.Amount(quantity);
            }
            catch { return ""; }
        }

        /// <summary>
        /// The full path of that drug's picture, or "" when there is not one.
        /// </summary>
        ///
        /// <remarks>
        /// A PATH, NOT A NAME, which is the same choice Api/Pantry made coming the other way.
        /// The caller's icons live in the caller's folder and it has no reason to know where
        /// ours are -- and both mods build an icon by Path.Combine against their own folder,
        /// which hands a rooted path straight back. So an absolute path from here draws over
        /// there with no change to anything over there.
        ///
        /// The files are named for what they are pictures of rather than for the drug, which
        /// is why this is a list and not id + ".png": weed's picture is a bong and crack's is
        /// a crystal. Kept beside UI/Icons.ForDrug, which makes the same choices for our own
        /// screens; if one grows a drug the other should too.
        /// </remarks>
        public static string IconOf(string id)
        {
            try
            {
                string file;

                switch ((id ?? "").ToLowerInvariant())
                {
                    case "weed": file = "i_weed.png"; break;
                    case "coke":
                    case "cocaine": file = "i_coke.png"; break;
                    case "crack": file = "i_crack.png"; break;
                    case "meth": file = "i_meth.png"; break;
                    case "heroin": file = "i_heroin.png"; break;
                    case "ecstasy":
                    case "pills": file = "i_ecstasy.png"; break;
                    case "xanax": file = "i_xanax.png"; break;

                    // THE ART WAS ALREADY DRAWN. acid.png is a perforated sheet of blotter with
                    // the squares marked out, which has been in the set since before there was
                    // anything to put it on -- so LSD arrived with its own tile and nothing had
                    // to be drawn for it. Both names answer to it: "acid" is what anybody would
                    // type, and the id is "lsd".
                    case "lsd":
                    case "acid": file = "i_lsd.png"; break;

                    // A drug added to drugs.json without art still gets a tile rather than a
                    // hole. Our own screens fall back to a text glyph, which another mod's
                    // grid has no way to draw.
                    default: file = "baggie.png"; break;
                }

                var path = System.IO.Path.Combine(
                    System.IO.Path.Combine(Core.Paths.Data, "icons"), file);

                return System.IO.File.Exists(path) ? path : "";
            }
            catch { return ""; }
        }

        /// <summary>One of whatever it counts itself in -- a gram, or a pill.</summary>
        public static float Unit => UseUnit;

        /// <summary>Everything on you against what you can carry, in grams.</summary>
        public static float Carried
        {
            get { try { return Ready ? _state.Stash.Total : 0f; } catch { return 0f; } }
        }

        public static float Capacity
        {
            get { try { return Ready ? _state.Stash.Capacity : 0f; } catch { return 0f; } }
        }

        /// <summary>How pure it is, 0 to 1. One when holding none, which is what Stash says.</summary>
        public static float PurityOf(string id)
        {
            try { return Ready ? _state.Stash.PurityOf(id) : 1f; }
            catch { return 1f; }
        }

        /// <summary>One of whatever it counts itself in. The same unit Hoodrich's own pocket uses.</summary>
        private const float UseUnit = 1f;

        /// <summary>
        /// Actually takes it: the refusal, the weight, the ritual and the high.
        /// </summary>
        ///
        /// <remarks>
        /// THE WHOLE ACT, not a piece of it. The first version of this only removed weight and
        /// left the effect to the caller, and that is wrong in the one way a player would
        /// notice -- a bag taken from Bare Minimum's pocket would move a hunger bar and do
        /// nothing else, while the same bag taken from Hoodrich's own pocket stopped him,
        /// played the ritual and changed the walk. Two doors into one act, and one of them a
        /// worse room. So this is the same act, reached from somewhere else.
        ///
        /// THE ORDER IS UI/PocketScreen'S ORDER because that order is the part that was
        /// thought about. Refuse before charging, so a man who has had enough is not charged
        /// for being told so. Remove before landing, so a rounding error cannot leave him high
        /// on a gram he still has. And put it back if the landing fails, which is the one thing
        /// PocketScreen does not do -- there the refusal was checked a line earlier so nothing
        /// could change in between, whereas here the caller is another mod and the gap is real.
        ///
        /// WHAT COMES BACK IS FOR A HUMAN. Null means it happened; anything else is Hoodrich's
        /// own sentence for why it did not, already written for a screen -- "You've had enough
        /// of that" -- so the other mod can show it without inventing wording of its own.
        /// </remarks>
        public static string Use(string id)
        {
            try
            {
                if (!Ready) return "Not ready";
                if (string.IsNullOrEmpty(id)) return "Nothing to take";

                var def = _catalogue.Get(id);
                if (def == null) return "Never heard of it";

                var no = _highs.Refusal(id);
                if (no != null) return no;

                var got = _state.Stash.RemovePackaged(id, UseUnit);
                if (got <= 0.001f) return "You have none of that";

                var late = _highs.Take(id, def.Amount(got));

                if (late != null)
                {
                    // It refused after being charged. Put it back at the purity it left at,
                    // which is what PurityOf still reports for the rest of the bag.
                    try { _state.Stash.AddPackaged(id, got, _state.Stash.PurityOf(id), true); }
                    catch { /* better to lose a gram than to double it */ }

                    return late;
                }

                return null;
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Api.Drugs.Use failed: " + ex.Message);
                return "Could not";
            }
        }

        // ---- the catalogue, and putting product back in -------------------------

        /// <summary>
        /// Every drug that exists, whether or not any is on you.
        ///
        /// NOT Ids(), WHICH IS A DIFFERENT QUESTION. Ids answers "what am I carrying" and is
        /// what a pocket screen wants. A caller that has to invent what somebody ELSE was
        /// carrying - what was in a dead man's jacket - needs to name a drug it has never
        /// seen, and Ids can only ever name the ones you already hold.
        ///
        /// Mirrors Bare Minimum's Api.Pantry.Menu, which draws the same line for the same
        /// reason.
        /// </summary>
        public static string[] Catalogue()
        {
            try
            {
                if (!Ready) return new string[0];

                var found = new List<string>();
                foreach (var d in _catalogue.All)
                {
                    if (d != null && !string.IsNullOrEmpty(d.Id)) found.Add(d.Id);
                }
                return found.ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>
        /// Puts street-ready product INTO his pockets. The grams that actually fit.
        /// </summary>
        ///
        /// <remarks>
        /// THE OPPOSITE DIRECTION FROM EVERYTHING ELSE HERE, and the one the surface was
        /// missing. Use takes product away, Sell takes it away, ToBag and ToPocket move it
        /// between two containers that both already belong to this mod - so a caller that
        /// FINDS product somewhere Hoodrich has never heard of, in a dead man's jacket or a
        /// searched glovebox, had no way to hand it over.
        ///
        /// AT THE PURITY IT WAS FOUND AT, not at full strength. Product picked up off a body
        /// is whatever that person was carrying, and letting an outside caller mint pure
        /// product would make looting strictly better than buying.
        ///
        /// CAPACITY IS RESPECTED - regardless is false - so this refuses rather than
        /// overfilling, and the caller is expected to notice it got less than it offered and
        /// leave the remainder where it found it.
        /// </remarks>
        public static float Loot(string id, float grams, float purity)
        {
            try
            {
                if (!Ready || string.IsNullOrEmpty(id) || grams <= 0.005f) return 0f;

                var def = _catalogue.Get(id);
                if (def == null) return 0f;

                if (purity < 0.05f) purity = 0.05f;
                if (purity > 1f) purity = 1f;

                var added = _state.Stash.AddPackaged(id, grams, purity, false);

                if (added > 0.005f)
                {
                    _state.Touch();
                    Core.Log.Info("Api.Drugs: took in " + added.ToString("0.#") + "g of " + NameOf(id) +
                                  " at " + (purity * 100f).ToString("0") + "% from another mod.");
                }

                return added;
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Api.Drugs.Loot failed: " + ex.Message);
                return 0f;
            }
        }

        // ---- selling it to somebody standing in front of you -------------------

        /// <summary>
        /// Set by Main once Pricing exists, which is AFTER Wire runs.
        ///
        /// A second call rather than a parameter on Wire, because Wire is made the moment the
        /// save has filled the stash and the ladder is not built until forty lines later.
        /// Moving Wire down would delay every other answer on this surface to buy nothing.
        /// </summary>
        private static Economy.Pricing _pricing;

        internal static void WireTrade(Economy.Pricing pricing)
        {
            _pricing = pricing;
        }

        /// <summary>Whether the selling half of this surface is up. Ready alone is not enough.</summary>
        public static bool CanTrade
        {
            get
            {
                try { return Ready && _pricing != null; }
                catch { return false; }
            }
        }

        /// <summary>
        /// What that much would fetch right now, before anything moves.
        ///
        /// Off the same ladder a corner sale uses, so a caller quoting a price and then
        /// calling Sell gets the number it showed. Nought when it cannot be priced.
        /// </summary>
        public static int Quote(string id, float grams)
        {
            try
            {
                if (!CanTrade || string.IsNullOrEmpty(id) || grams <= 0.005f) return 0;

                var def = _catalogue.Get(id);
                if (def == null) return 0;

                var have = _state.Stash.PackagedOf(id);
                var take = Math.Min(grams, have);
                if (take <= 0.005f) return 0;

                return _pricing.SaleValue(def, take, _state.Stash.PurityOf(id));
            }
            catch { return 0; }
        }

        /// <summary>
        /// How likely a buyer is to knock this back for being stepped on, 0 to 1.
        ///
        /// THE ROLL IS THE CALLER'S, not ours. A corner customer's refusal is decided by
        /// PostUp because PostUp owns that encounter; a mod running its own conversation owns
        /// its own, and it may well want to fold this into whatever else that person thinks
        /// of you. So the chance is published and the decision is left alone.
        /// </summary>
        public static float RefusalChance(string id)
        {
            try
            {
                if (!CanTrade || string.IsNullOrEmpty(id)) return 0f;
                return Economy.Pricing.BadCutChance(_state.Stash.PurityOf(id));
            }
            catch { return 0f; }
        }

        /// <summary>
        /// Sells product to somebody in front of you. The dollars actually paid, or nought.
        /// </summary>
        ///
        /// <remarks>
        /// THE WHOLE ACT, for the same reason Use is the whole act. A method that only took
        /// the weight off would leave the caller to invent a price, and then dealing through
        /// another mod would be a SECOND economy: money that never touched the ladder, weight
        /// that never moved the block, sales that never earned respect and never cost
        /// notoriety. One of those two economies would be wrong and nobody could say which.
        ///
        /// So this is PostUp's sale with the encounter taken out: the ladder prices what was
        /// actually handed over, the money is given, and every number a sale on the corner
        /// moves is moved here too - respect, grams sold, deals made, lifetime earnings, the
        /// last transfer the bank card shows, and the block's own saturation so somewhere
        /// sold dry stays sold dry.
        ///
        /// WHAT IT DOES NOT DO is roll for a refusal or apply heat. The caller decided this
        /// person was buying before it got here; see RefusalChance and Refused.
        ///
        /// Nought or less asks for everything of that kind in your pockets.
        /// </remarks>
        public static int Sell(string id, float grams)
        {
            try
            {
                if (!CanTrade || string.IsNullOrEmpty(id)) return 0;

                var def = _catalogue.Get(id);
                if (def == null) return 0;

                var have = _state.Stash.PackagedOf(id);
                var want = grams <= 0.005f ? have : Math.Min(grams, have);
                if (want <= 0.005f) return 0;

                // Purity is read BEFORE the weight leaves, because removing the last of a
                // batch resets what the stash reports and the price would follow it.
                var purity = _state.Stash.PurityOf(id);

                var sold = _state.Stash.RemovePackaged(id, want);
                if (sold <= 0.005f) return 0;

                var payout = _pricing.SaleValue(def, sold, purity);

                UI.Cash.Give(payout);

                _state.SoldAt(purity);
                _state.AddRespect(1f + def.Tier * 0.4f);
                _state.GramsSold += sold;
                _state.TotalDealsMade++;
                _state.TotalEarned += payout;
                _state.LastDeal = payout;
                _state.Touch();

                _pricing.SoldHere(sold);

                Core.Log.Info("Api.Drugs: sold " + sold.ToString("0.#") + "g of " + NameOf(id) +
                              " for $" + payout + " through another mod's screen.");

                return payout;
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Api.Drugs.Sell failed: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// A buyer turned the product down for being cut.
        ///
        /// The other half of the purity system, which is worth nothing if a refusal only ever
        /// costs one sale: stretching product has to have a price. Same two marks a refusal on
        /// the corner leaves - a point of notoriety, and the block's memory of what you were
        /// selling.
        /// </summary>
        public static void Refused(string id)
        {
            try
            {
                if (!CanTrade || string.IsNullOrEmpty(id)) return;

                _state.AddNotoriety(1f);
                _state.RefusedAt(_state.Stash.PurityOf(id));
                _state.Touch();
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Api.Drugs.Refused failed: " + ex.Message);
            }
        }

        // ---- the bag -----------------------------------------------------------

        /// <summary>
        /// Whether the bag is on his back. False without one, and false before Ready.
        /// </summary>
        ///
        /// <remarks>
        /// THE BAG'S SIDE OF THE SAME SURFACE, for the other mod's bag screen. That screen
        /// stands in for the pocket the whole time a bag is on his back, and it could see the
        /// pocket's product and not the bag's -- so a man wearing a bag had two ways into his
        /// inventory and neither showed a gram of what he had just looted into it. Everything
        /// from here down is the bag's product, read and moved: the same Stash the phone's own
        /// pocket screen carries between, through the same Stash.Carry, so nothing is minted
        /// or lost between two containers that both belong to this mod.
        ///
        /// ADDED WITHOUT BUMPING ApiVersion, the way Bare Minimum added its bag shelf: a
        /// caller that asks for these is newer than the surface, and an older caller never
        /// asks.
        /// </remarks>
        public static bool BagWorn
        {
            get
            {
                try { return Ready && _state.Bag != null && _state.Bag.Worn; }
                catch { return false; }
            }
        }

        /// <summary>The ids of everything street-ready in the bag, on his back or not.</summary>
        public static string[] BagIds()
        {
            try
            {
                if (!Ready || _state.Bag == null) return new string[0];

                var found = new List<string>();
                foreach (var d in _catalogue.All)
                {
                    if (_state.Bag.Stash.PackagedOf(d.Id) > 0.005f) found.Add(d.Id);
                }

                return found.ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>Grams of street-ready product of that kind in the bag. Zero for anything unknown.</summary>
        public static float BagGramsOf(string id)
        {
            try { return Ready && _state.Bag != null ? _state.Bag.Stash.PackagedOf(id) : 0f; }
            catch { return 0f; }
        }

        /// <summary>Grams of product in the bag, bulk and bagged together.</summary>
        public static float BagCarried
        {
            get
            {
                try { return Ready && _state.Bag != null ? _state.Bag.Stash.Total : 0f; }
                catch { return 0f; }
            }
        }

        /// <summary>
        /// Moves street-ready product from his pockets into the bag. How much went, which is
        /// nought when none of it fit or the bag is not on him.
        ///
        /// NOUGHT OR LESS MEANS THE LOT. The other side moves a tile, and a tile is however
        /// much of that drug there is.
        ///
        /// ONLY INTO A BAG HE IS WEARING, for the reason Api.Pantry.Give gives coming the
        /// other way: putting product into a bag lying on a pavement two streets off is worse
        /// than refusing it.
        ///
        /// AS MUCH AS FITS BY SLOTS, not by grams. The bag is twenty slots between two mods --
        /// a slot per hundred grams of product, one per item of food -- and Satchel.GramsFree
        /// is what is left once their food shelf has been counted.
        /// </summary>
        public static float ToBag(string id, float grams)
        {
            try
            {
                if (!Ready || string.IsNullOrEmpty(id)) return 0f;

                var bag = _state.Bag;
                if (bag == null || !bag.Worn) return 0f;

                var have = _state.Stash.PackagedOf(id);
                var want = grams <= 0.005f ? have : Math.Min(grams, have);

                want = Math.Min(want, bag.GramsFree);
                if (want <= 0.005f) return 0f;

                var moved = Economy.Stash.Carry(_state.Stash, bag.Stash, id, want, true);

                if (moved > 0.005f)
                {
                    Core.Log.Info("Bag: " + moved.ToString("0.#") + "g of " + NameOf(id) +
                                  " into the bag off the screen next door -- bag " +
                                  bag.Used + " of " + State.Satchel.Slots + " slots.");
                }

                return moved;
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Api.Drugs.ToBag failed: " + ex.Message);
                return 0f;
            }
        }

        /// <summary>
        /// Moves street-ready product out of the bag into his pockets. How much went, which
        /// is nought when the pockets are full or the bag is not on him. Nought or less asks
        /// for the lot.
        /// </summary>
        public static float ToPocket(string id, float grams)
        {
            try
            {
                if (!Ready || string.IsNullOrEmpty(id)) return 0f;

                var bag = _state.Bag;
                if (bag == null || !bag.Worn) return 0f;

                var have = bag.Stash.PackagedOf(id);
                var want = grams <= 0.005f ? have : Math.Min(grams, have);
                if (want <= 0.005f) return 0f;

                var moved = Economy.Stash.Carry(bag.Stash, _state.Stash, id, want, true);

                if (moved > 0.005f)
                {
                    Core.Log.Info("Bag: " + moved.ToString("0.#") + "g of " + NameOf(id) +
                                  " out of the bag into his pockets off the screen next door -- pockets " +
                                  _state.Stash.Total.ToString("0.#") + "g of " +
                                  _state.Stash.Capacity.ToString("0") + "g.");
                }

                return moved;
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Api.Drugs.ToPocket failed: " + ex.Message);
                return 0f;
            }
        }

        /// <summary>
        /// Takes one out of the bag: into his pockets first, then the same act as Use.
        ///
        /// THROUGH THE POCKETS RATHER THAN A SECOND Use, so there is one act and one set of
        /// refusals. Refused before anything moves; and if the landing refuses after the unit
        /// has crossed, it goes back in the bag, so a refusal leaves the bag exactly as it
        /// found it. Null when it happened, a sentence for a screen when it did not.
        /// </summary>
        public static string UseFromBag(string id)
        {
            try
            {
                if (!Ready) return "Not ready";
                if (string.IsNullOrEmpty(id)) return "Nothing to take";

                var bag = _state.Bag;
                if (bag == null || !bag.Worn) return "The bag is not on you";

                var def = _catalogue.Get(id);
                if (def == null) return "Never heard of it";

                if (bag.Stash.PackagedOf(id) < UseUnit - 0.001f) return "You have none of that in the bag";

                var no = _highs.Refusal(id);
                if (no != null) return no;

                var crossed = Economy.Stash.Carry(bag.Stash, _state.Stash, id, UseUnit, true);

                if (crossed < UseUnit - 0.001f)
                {
                    // Not even one would fit in his pockets. Whatever did cross goes back.
                    if (crossed > 0.005f) Economy.Stash.Carry(_state.Stash, bag.Stash, id, crossed, true);
                    return "No room in your pockets";
                }

                var late = Use(id);

                if (late != null)
                {
                    // Refused after crossing. Back in the bag, so the bag is as it was.
                    Economy.Stash.Carry(_state.Stash, bag.Stash, id, UseUnit, true);
                }

                return late;
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Api.Drugs.UseFromBag failed: " + ex.Message);
                return "Could not";
            }
        }
    }
}
