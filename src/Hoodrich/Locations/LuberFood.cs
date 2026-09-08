using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>Where an order is up to.</summary>
    internal enum FoodState
    {
        None = 0,

        /// <summary>Paid for, and somebody is riding it over.</summary>
        Coming,

        /// <summary>Parked up, off the moped, walking it to you with the bag in his hand.</summary>
        Walking,

        /// <summary>Stood in front of you, handing it over.</summary>
        Handing,

        /// <summary>Done. Back on the moped and away.</summary>
        Leaving
    }

    /// <summary>
    /// LUber brings you food.
    ///
    /// THE OTHER HALF OF THE APP, and it is the same company: the cars drive themselves and
    /// the food does not, which is the joke the whole thing runs on. You order off the menu,
    /// somebody real gets on a moped somewhere across the map, rides to wherever you are
    /// stood, gets off, walks over with the bag in his hand and puts it in yours.
    ///
    /// IT IS BARE MINIMUM'S FOOD, NOT OURS. The menu, the prices, the names and the pictures
    /// all come over the bridge (Core.Pantry) and what arrives goes into that mod's pockets.
    /// Without it installed there is no menu and the app never offers the row -- which is
    /// right: this mod has no food of its own and inventing some so the button worked would
    /// be two mods disagreeing about what a sandwich is.
    ///
    /// THE RIDE IS THE POINT. Nothing here teleports and nothing appears at your feet: he is
    /// made a hundred metres away or more, on a road, and he has to get to you. If he cannot
    /// -- traffic, a wall, you running off across a field -- the order times out and you get
    /// your money back. A delivery that always arrives is a menu with a delay on it.
    ///
    /// It is deliberately one at a time. A queue of riders converging on one man is a bit,
    /// not a feature.
    /// </summary>
    internal sealed class LuberFood
    {
        /// <summary>What it costs to send somebody, on top of what the food costs.</summary>
        private const int Fee = 30;

        /// <summary>Far enough that he rides to you rather than appearing at the kerb.</summary>
        private const float ComeFromMin = 110f;
        private const float ComeFromMax = 220f;

        /// <summary>Close enough to you to stop the moped and get off it.</summary>
        private const float ParkRange = 16f;

        /// <summary>Close enough on foot to hand it over.</summary>
        private const float HandRange = 1.9f;

        /// <summary>How far he will chase you on foot before he gives up and rides off.</summary>
        private const float LostRange = 70f;

        /// <summary>How long each leg is given before the order is written off.</summary>
        private const int RideCapMs = 240000;
        private const int WalkCapMs = 90000;
        private const int HandMs = 1400;
        private const int LeaveCapMs = 60000;

        /// <summary>Once he is away and this far off, he stops existing.</summary>
        private const float GoneRange = 120f;

        /// <summary>How often he is re-aimed at you while he rides. You move; the route does not.</summary>
        private const int ReaimMs = 4000;

        /// <summary>
        /// How he rides.
        ///
        /// The same eight flags the cars use -- see Luber.Style -- because the reasoning is
        /// identical and a moped that stops dead behind a parked van is the same failure.
        /// </summary>
        private const int Style = 1 | 2 | 4 | 8 | 16 | 32 | 128 | 256;

        private const float RideSpeed = 19f;

        /// <summary>PH_R_Hand: the prop helper, where a hand actually holds a thing. See Core.Handset.</summary>
        private const int RightHandBone = 28422;

        /// <summary>The moped, in the order an install has them.</summary>
        private static readonly string[] Bikes = { "faggio2", "faggio3", "faggio", "esskey" };

        /// <summary>
        /// Who rides it.
        ///
        /// The postal and courier peds, because they are the only people in this game already
        /// dressed for carrying something to a door. Not a gang ped and not a random member of
        /// the public: a man in a uniform is read as working before he has done anything.
        /// </summary>
        private static readonly string[] Riders =
        {
            "s_m_m_postal_02", "s_m_m_postal_01", "s_m_m_ups_01", "s_m_m_ups_02"
        };

        /// <summary>The bag in his hand, in the order an install has them.</summary>
        private static readonly string[] Bags =
        {
            "prop_food_bs_bag_01", "prop_carrier_bag_01", "prop_paper_bag_01"
        };

        private const string GiveDict = "mp_common";
        private const string GiveClip = "givetake1_a";

        /// <summary>Set by Main: take the money. False when he cannot cover it.</summary>
        public Func<int, bool> Charge;

        /// <summary>Set by Main: hand it back when the order does not arrive.</summary>
        public Action<int> Refund;

        /// <summary>Set by Main: off while something louder is happening.</summary>
        public Func<bool> Busy;

        /// <summary>Set by Main: he has just been handed something, for the feed.</summary>
        public Action<string> Arrived;

        public FoodState State { get; private set; }

        public bool IsRunning => State != FoodState.None;

        /// <summary>What is on its way, for the app to say so.</summary>
        public string What { get; private set; } = "";

        private readonly Random _rng = new Random();

        private Vehicle _bike;
        private Ped _rider;
        private Prop _bag;
        private Blip _blip;

        private string _id = "";
        private int _paid;

        private int _phaseFrom;
        private int _reaimAt;
        private int _handFrom;

        // ---- ordering -----------------------------------------------------------

        /// <summary>
        /// What one costs delivered: the counter price plus the fee. Below nought when it is
        /// not something LUber can bring.
        /// </summary>
        public static int Quote(string id)
        {
            if (string.IsNullOrEmpty(id) || !Pantry.Present) return -1;

            var price = Pantry.PriceOf(id);
            if (price <= 0) return -1;

            return price + Fee;
        }

        /// <summary>
        /// Orders one. Returns a refusal to put in front of the player, or null once somebody
        /// is on the way.
        /// </summary>
        public string Order(string id)
        {
            if (IsRunning) return "You've already got one coming.";
            if (!Pantry.Present) return "Nothing to order.";

            if (Busy != null && Busy()) return "Not right now.";

            var price = Quote(id);
            if (price < 0) return "They don't do that.";

            var player = Game.Player.Character;

            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            // ON FOOT. The handover is a man walking up to you and putting a bag in your hand,
            // and there is no version of that through a car window worth writing.
            if (player.IsInVehicle()) return "Get out of the car first.";

            // ASKED BEFORE THE MONEY. A full pocket is the one refusal that would otherwise
            // cost you the fare and the ride to find out.
            if (Pantry.Slots > 0 && Pantry.Total >= Pantry.Slots) return "Your pockets are full.";

            var start = Somewhere(player.Position);
            if (start == Vector3.Zero) return "Nobody free near you.";

            if (Charge != null && !Charge(price)) return "You can't cover that.";

            if (!Make(start))
            {
                if (Refund != null) Refund(price);
                return "Nobody free near you.";
            }

            Luber.Theme();

            _id = id;
            _paid = price;
            What = Pantry.NameOf(id);

            Ride(player.Position);
            Mark();
            Begin(FoodState.Coming);

            Notify.Card(Luber.Face, "LUber", "on the way",
                        What + " is coming. $" + price + ", rider's on his way.");

            Log.Info("LUber: " + What + " ordered for $" + price + ".");

            return null;
        }

        /// <summary>Called off the app, or by anything that has to stop it. Money back.</summary>
        public void Cancel(string why)
        {
            if (!IsRunning) return;

            var back = State == FoodState.Leaving ? 0 : _paid;

            Away();
            Clean();
            State = FoodState.None;
            What = "";

            if (back > 0 && Refund != null) Refund(back);

            if (!string.IsNullOrEmpty(why)) Notify.Failure(why);
        }

        // ---- the tick -----------------------------------------------------------

        public void Update(Ped player)
        {
            if (State == FoodState.None) return;

            try
            {
                var now = Game.GameTime;

                if (player == null || !player.Exists() || !player.IsAlive)
                {
                    Cancel(null);
                    return;
                }

                // DEAD RIDER, DEAD ORDER. Not a refusal he did anything about, so the money
                // goes back without a word about whose fault it was.
                if (_rider == null || !_rider.Exists() || !_rider.IsAlive)
                {
                    Log.Info("LUber: the rider never made it. Refunded.");
                    Cancel("your delivery never turned up. Money back.");
                    return;
                }

                switch (State)
                {
                    case FoodState.Coming: Coming(player, now); break;
                    case FoodState.Walking: Walking(player, now); break;
                    case FoodState.Handing: Handing(player, now); break;
                    case FoodState.Leaving: Leaving(now); break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("LUber food failed: " + ex.Message);
                Cancel(null);
            }
        }

        /// <summary>
        /// Riding it over.
        ///
        /// RE-AIMED WHILE HE RIDES, because you are not stood still. A route handed out once
        /// is a route to where you were when you pressed the button, which on foot is a
        /// street away by the time he arrives and in a car is another postcode.
        /// </summary>
        private void Coming(Ped player, int now)
        {
            if (_bike == null || !_bike.Exists())
            {
                Log.Info("LUber: the moped is gone. Refunded.");
                Cancel("your delivery never turned up. Money back.");
                return;
            }

            if (now - _phaseFrom > RideCapMs)
            {
                Log.Info("LUber: the rider could not reach you in time. Refunded.");
                Cancel("your delivery couldn't get to you. Money back.");
                return;
            }

            var gap = _rider.Position.DistanceTo(player.Position);

            if (gap > ParkRange)
            {
                if (now < _reaimAt) return;

                Ride(player.Position);
                return;
            }

            // Off the moped, bag in hand, and the rest of the way on foot.
            Hold();

            try
            {
                Function.Call(Hash.TASK_LEAVE_VEHICLE, _rider.Handle, _bike.Handle, 0);
                Function.Call(Hash.SET_PED_KEEP_TASK, _rider.Handle, true);
            }
            catch
            {
                // He is walked at you either way; the game gets him off it.
            }

            Carry();
            Begin(FoodState.Walking);
        }

        /// <summary>Walking it over, and following you while he does it.</summary>
        private void Walking(Ped player, int now)
        {
            if (now - _phaseFrom > WalkCapMs)
            {
                Log.Info("LUber: the rider gave up looking for you. Refunded.");
                Cancel("your delivery couldn't find you. Money back.");
                return;
            }

            var gap = _rider.Position.DistanceTo(player.Position);

            // YOU LEFT. He is not chasing a car across the map on foot with a paper bag.
            if (gap > LostRange)
            {
                Log.Info("LUber: you left while the rider was walking over. Refunded.");
                Cancel("you left. Money back.");
                return;
            }

            if (gap > HandRange)
            {
                if (now < _reaimAt) return;

                _reaimAt = now + 1500;

                try
                {
                    Function.Call(Hash.TASK_GO_TO_ENTITY, _rider.Handle, player.Handle,
                                  -1, HandRange * 0.75f, 1.6f, 1073741824f, 0);
                    Function.Call(Hash.SET_PED_KEEP_TASK, _rider.Handle, true);
                }
                catch
                {
                    // Asked again in a second and a half.
                }

                return;
            }

            // There. Stop, face him, and hand it over.
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _rider.Handle);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _rider.Handle, player.Handle, 800);

                Function.Call(Hash.REQUEST_ANIM_DICT, GiveDict);

                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, GiveDict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, _rider.Handle, GiveDict, GiveClip,
                                  4f, -2f, HandMs, 48, 0f, false, false, false);
                }
            }
            catch
            {
                // The bag still changes hands; only the performance is lost.
            }

            _handFrom = now;
            Begin(FoodState.Handing);
        }

        /// <summary>
        /// The moment it changes hands.
        ///
        /// THE FOOD MOVES HALFWAY THROUGH THE ANIMATION, not at the start and not at the end.
        /// At the start it is in your pocket before he has reached out; at the end it is a
        /// beat of him stood there with empty hands. In the middle it lands with the gesture.
        /// </summary>
        private void Handing(Ped player, int now)
        {
            if (now - _handFrom < HandMs / 2) return;

            var name = Pantry.NameOf(_id);

            // THE ONE FAILURE THAT CAN STILL HAPPEN HERE. Pockets were checked when it was
            // ordered and could have filled in the four minutes since, so the answer is asked
            // for rather than assumed -- and the money goes back if it is no.
            var took = Pantry.Give(_id, 1);

            Drop();

            if (!took)
            {
                Log.Info("LUber: " + name + " arrived and would not fit. Refunded.");

                if (Refund != null) Refund(_paid);

                Notify.Card(Luber.Face, "LUber", "no room",
                            "He brought it. You've nowhere to put it. $" + _paid + " back.");
            }
            else
            {
                Log.Info("LUber: " + name + " delivered.");

                Notify.Card(Luber.Face, "LUber", "delivered",
                            name + ", in your pocket. Enjoy.");

                if (Arrived != null) Arrived(name);
            }

            // He does not hang about either way.
            Leave();
            Begin(FoodState.Leaving);
        }

        /// <summary>Back on the moped and gone.</summary>
        private void Leaving(int now)
        {
            var done = now - _phaseFrom > LeaveCapMs;

            if (!done)
            {
                var player = Game.Player.Character;

                done = player != null && player.Exists() &&
                       _rider.Position.DistanceTo(player.Position) > GoneRange;
            }

            if (!done) return;

            Away();
            Clean();
            State = FoodState.None;
            What = "";
        }

        // ---- the people and the things ------------------------------------------

        private bool Make(Vector3 at)
        {
            try
            {
                foreach (var name in Bikes)
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    _bike = World.CreateVehicle(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (_bike == null || !_bike.Exists()) continue;

                    Log.Info("LUber: a " + name + " went out with it.");
                    break;
                }

                if (_bike == null || !_bike.Exists()) return false;

                _bike.IsPersistent = true;

                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _bike.Handle);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _bike.Handle, true, true);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _bike.Handle, true, true, false);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, _bike.Handle, 0f);

                // THE FLEET'S WHITE, the same as the cars. Written as a custom colour rather
                // than a paint index for the reason Luber.Make gives: the index table has
                // several whites in it and one of them is nearly grey.
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, _bike.Handle, 0);
                Function.Call(Hash.SET_VEHICLE_CUSTOM_PRIMARY_COLOUR, _bike.Handle, 255, 255, 255);
                Function.Call(Hash.SET_VEHICLE_CUSTOM_SECONDARY_COLOUR, _bike.Handle, 255, 255, 255);

                return Rider();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a LUber rider out: " + ex.Message);
                return false;
            }
        }

        private bool Rider()
        {
            try
            {
                Ped made = null;

                foreach (var name in Riders)
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    made = World.CreatePed(model, _bike.Position);
                    model.MarkAsNoLongerNeeded();

                    if (made != null && made.Exists()) break;
                    made = null;
                }

                if (made == null) return false;

                _rider = made;
                _rider.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _rider.Handle, true, true);
                Function.Call(Hash.SET_PED_INTO_VEHICLE, _rider.Handle, _bike.Handle, -1);

                // HE IS WORKING, NOT LIVING. He does not flinch at gunfire, does not join a
                // fight, cannot be dragged off the moped and cannot be shot off it by traffic
                // -- because every one of those ends with your dinner on the road and a refund
                // you did not ask for. He is not invincible: a player who runs him over has
                // done that on purpose and can have the consequence.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _rider.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _rider.Handle, false);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _rider.Handle, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, _rider.Handle, 251, true);
                Function.Call(Hash.SET_DRIVER_ABILITY, _rider.Handle, 1.0f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, _rider.Handle, 0.4f);

                // AND A HELMET, because the game gives a moped rider one only sometimes and a
                // uniform with a helmet reads as a job.
                Function.Call(Hash.SET_PED_HELMET, _rider.Handle, true);

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a LUber rider on the moped: " + ex.Message);
                return false;
            }
        }

        /// <summary>The bag, in his right hand, for the walk over.</summary>
        private void Carry()
        {
            try
            {
                foreach (var name in Bags)
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _bag = World.CreateProp(model, _rider.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_bag != null && _bag.Exists()) break;
                    _bag = null;
                }

                if (_bag == null) return;

                _bag.IsPersistent = true;

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, _rider.Handle, RightHandBone);

                // No offset and no rotation: PH_R_Hand already sits where a held thing goes.
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _bag.Handle, _rider.Handle, bone,
                              0f, 0f, 0f, 0f, 0f, 0f, false, false, false, false, 2, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a bag in the rider's hand: " + ex.Message);
            }
        }

        /// <summary>Out of his hand, gone. The food is in your pocket by now.</summary>
        private void Drop()
        {
            try
            {
                if (_bag != null && _bag.Exists())
                {
                    Function.Call(Hash.DETACH_ENTITY, _bag.Handle, true, true);
                    _bag.Delete();
                }
            }
            catch
            {
                // It goes with the rest of it in Clean.
            }

            _bag = null;
        }

        // ---- driving and walking ------------------------------------------------

        private void Ride(Vector3 to)
        {
            _reaimAt = Game.GameTime + ReaimMs;

            try
            {
                var road = OnRoad(to);

                Function.Call(Hash.CLEAR_PED_TASKS, _rider.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                              _rider.Handle, _bike.Handle, road.X, road.Y, road.Z,
                              RideSpeed, Style, 6f);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, _rider.Handle, RideSpeed);
                Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, _rider.Handle, Style);
                Function.Call(Hash.SET_PED_KEEP_TASK, _rider.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send the LUber rider on: " + ex.Message);
            }
        }

        private void Hold()
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _rider.Handle);
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _rider.Handle, _bike.Handle, 1, 1200);
            }
            catch
            {
                // He gets off it either way.
            }
        }

        /// <summary>Back to the moped, on it, and away.</summary>
        private void Leave()
        {
            try
            {
                if (_blip != null && _blip.Exists()) { _blip.Delete(); _blip = null; }

                Function.Call(Hash.CLEAR_PED_TASKS, _rider.Handle);

                if (_bike != null && _bike.Exists())
                {
                    Function.Call(Hash.TASK_ENTER_VEHICLE, _rider.Handle, _bike.Handle,
                                  20000, -1, 2f, 1, 0);
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, _rider.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send the LUber rider home: " + ex.Message);
            }
        }

        /// <summary>
        /// Hands both of them back to the game.
        ///
        /// NOT DELETED WHILE YOU CAN SEE THEM. A rider who blinks out of existence three
        /// metres from you is worse than one who rides off badly, so both stop being ours and
        /// are left to the game's own clean-up, which does it when nobody is looking.
        /// </summary>
        private void Away()
        {
            Drop();

            try
            {
                if (_rider != null && _rider.Exists())
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _rider.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _rider.Handle, true);

                    if (_bike != null && _bike.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, _rider.Handle, _bike.Handle,
                                      RideSpeed, Style);
                    }

                    _rider.MarkAsNoLongerNeeded();
                }

                if (_bike != null && _bike.Exists()) _bike.MarkAsNoLongerNeeded();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not let the LUber rider go: " + ex.Message);
            }
        }

        // ---- housekeeping -------------------------------------------------------

        private void Begin(FoodState state)
        {
            State = state;
            _phaseFrom = Game.GameTime;
            _reaimAt = 0;
        }

        private void Mark()
        {
            try
            {
                if (_bike == null || !_bike.Exists()) return;

                _blip = _bike.AddBlip();
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = BlipSprite.Store;
                _blip.Color = BlipColor.Blue;
                _blip.Scale = 0.7f;
                _blip.Name = "LUber";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip a LUber rider: " + ex.Message);
            }
        }

        private void Clean()
        {
            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch
            {
                // Nothing else to do about a blip.
            }

            _blip = null;
            _rider = null;
            _bike = null;
            _id = "";
            _paid = 0;
        }

        /// <summary>
        /// On the way out, and this one DOES delete.
        ///
        /// The difference from Away is who is watching: this runs when the script is being
        /// unloaded, and a moped left wandering with a mission ped on it belongs to a script
        /// that no longer exists.
        /// </summary>
        public void RestoreWorld()
        {
            try
            {
                Drop();

                if (_blip != null && _blip.Exists()) _blip.Delete();
                if (_rider != null && _rider.Exists()) _rider.Delete();
                if (_bike != null && _bike.Exists()) _bike.Delete();
            }
            catch
            {
                // Teardown.
            }

            _blip = null;
            _rider = null;
            _bike = null;
            State = FoodState.None;
            What = "";
        }

        private static Vector3 OnRoad(Vector3 near)
        {
            try
            {
                var at = World.GetNextPositionOnStreet(near, true);
                return at == Vector3.Zero ? near : at;
            }
            catch
            {
                return near;
            }
        }

        private Vector3 Somewhere(Vector3 near)
        {
            for (var tries = 0; tries < 14; tries++)
            {
                try
                {
                    var away = ComeFromMin + (float)_rng.NextDouble() * (ComeFromMax - ComeFromMin);
                    var probe = near.Around(away);

                    var at = World.GetNextPositionOnStreet(probe, true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(near) < ComeFromMin * 0.6f) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }
    }
}
