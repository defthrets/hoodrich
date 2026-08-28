using System;
using System.Collections.Generic;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>One motor on the lot, and what it costs.</summary>
    internal sealed class CarLot
    {
        public string Id = "";
        public string Model = "";
        public string Name = "";
        public string Class = "";
        public string Note = "";
        public int Price;

        public Vector3 Spot;
        public float Heading;

        /// <summary>
        /// What it is painted, as indices into the game's own colour table.
        ///
        /// In the data rather than in the code, for the same reason the gang paints are: it is
        /// a look, and looks get retuned by whoever is stood in the yard finding it too blue.
        /// Minus one leaves the game to choose, which is what every car here used to do -- and
        /// a forecourt of randomly painted cars comes out mostly primary blue and red, because
        /// that is what the random table is mostly made of.
        /// </summary>
        public int Paint = -1;
        public int Paint2 = -1;

        /// <summary>The one standing in the yard right now, if it is streamed in.</summary>
        public Vehicle Live;

        /// <summary>Named ModelHash, not Hash, so it cannot shadow the native enum.</summary>
        public uint ModelHash => Function.Call<uint>(Hash.GET_HASH_KEY, Model);
    }

    /// <summary>
    /// Hao: street racer, mechanic, and the man who sells you a car with somebody else's past.
    ///
    /// He is deliberately NOT another drug contact. Every other person in this mod wants
    /// weight moved; Hao wants METAL, and that is the whole reason he exists -- a second
    /// economy running alongside the first, with its own yard, its own money and eventually
    /// its own jobs. He tunes, he races, and he re-vins. You bring him something and it stops
    /// being what it was.
    ///
    /// The lot is real. Every car on it is a vehicle standing in that yard rather than a row
    /// in a menu, placed where it was parked, and buying one hands you THAT car -- the one you
    /// have been walking past -- rather than spawning a copy of it somewhere.
    /// </summary>
    internal sealed class Hao
    {
        /// <summary>By the shutter at the top of the lot, facing down it.</summary>
        private static readonly Vector3 Spot = new Vector3(-40.439f, -1675.140f, 29.470f);
        private const float Heading = 137.369f;

        private const float SpawnRange = 120f;
        private const float DespawnRange = 200f;
        private const float TalkRange = 3.2f;

        /// <summary>How close you stand to a motor before he will talk about that one.</summary>
        private const float CarRange = 4.0f;

        private const int UpdateIntervalMs = 700;

        /// <summary>radar_gang_vehicle -- a car, which is what he is.</summary>
        private const int Sprite = 225;

        /// <summary>
        /// The Tuners-era Hao first, because that is the man this is.
        ///
        /// ig_hao is the kid from the drag strip in the main story; ig_hao_02 is the one who
        /// runs a garage and sells cars, which is who is standing here. The mechanics behind
        /// them are for an install that has neither.
        /// </summary>
        private static readonly string[] Models =
        {
            "ig_hao_02", "csb_hao_02", "ig_hao", "csb_hao",
            "ig_mechanic_01", "ig_mechanic_02", "ig_mechanic_03",
            "s_m_y_xmech_02", "a_m_y_eastsa_01"
        };

        /// <summary>
        /// Suspension. Type 15, and the index is worked out rather than hardcoded.
        ///
        /// "Competition" is the LAST suspension a vehicle offers, and how many it offers is not
        /// the same on every model -- so a hardcoded 3 fits the ones with four and silently
        /// does nothing on the ones with three. Asking the vehicle how many it has and taking
        /// the top one is right on all of them.
        /// </summary>
        private const int ModSuspension = 15;

        private readonly PlayerState _state;
        private readonly List<CarLot> _stock = new List<CarLot>();

        private Ped _ped;
        private Blip _blip;
        private int _lastUpdate;
        private bool _held;

        public Hao(PlayerState state)
        {
            _state = state;
            Load();
        }

        public string Name => "Hao";
        public Vector3 Position => Spot;
        public Ped Ped => _ped != null && _ped.Exists() ? _ped : null;

        /// <summary>Set by Main.</summary>
        public Conversation Talk;
        public Func<DialogueNode> TalkBuilder;

        /// <summary>Opens the buying screen. Set by Main.</summary>
        public Action Showroom;

        /// <summary>Everything still for sale.</summary>
        public IReadOnlyList<CarLot> Stock => _stock;

        public bool InReach
        {
            get
            {
                if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return false;

                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return false;

                return player.Position.DistanceTo(_ped.Position) <= TalkRange;
            }
        }

        // ---- what he has -------------------------------------------------------

        private void Load()
        {
            var doc = JsonFile.Read(System.IO.Path.Combine(Paths.Data, "cars.json"));
            if (doc == null)
            {
                Log.Warn("No cars.json; Hao has an empty lot.");
                return;
            }

            foreach (var node in doc["cars"].Items)
            {
                var id = node["id"].AsString("");
                if (string.IsNullOrEmpty(id)) continue;

                var lot = new CarLot
                {
                    Id = id,
                    Model = node["model"].AsString(id),
                    Name = node["name"].AsString(id),
                    Class = node["class"].AsString(""),
                    Note = node["note"].AsString(""),
                    Price = Math.Max(1, node["price"].AsInt(10000)),
                    Spot = new Vector3(node["x"].AsFloat(), node["y"].AsFloat(), node["z"].AsFloat()),
                    Heading = node["heading"].AsFloat(),
                    Paint = node["paint"].AsInt(-1),
                    Paint2 = node["paint2"].AsInt(-1)
                };

                // EVERY CAR IS KEPT, not only the ones for sale. The catalogue is what a
                // buy-back is put back into: the spot, the heading and the paint all live in
                // cars.json, and a car that has been bought is gone from _stock -- so without a
                // full list there is nothing left to say where it belongs.
                _all.Add(lot);

                // Sold is remembered, so his lot does not quietly restock the thing you are
                // currently driving around in.
                if (_state != null && _state.CarsBought.Contains(id)) continue;

                _stock.Add(lot);
            }

            Log.Info("Hao's lot: " + _stock.Count + " for sale.");
        }

        /// <summary>
        /// Every car on the books, sold or not. See Load.
        /// </summary>
        private readonly List<CarLot> _all = new List<CarLot>();

        /// <summary>
        /// When each car was bought, by lot id.
        ///
        /// IN MEMORY AND NOT IN THE SAVE, deliberately. It exists for one thing -- the window in
        /// which he gives you your money back rather than half of it -- and that window is about
        /// having just done something you did not mean to do. Carrying it across a reload would
        /// turn "I have just bought the wrong car" into a thing you could bank and come back to.
        ///
        /// So a car bought last session sells for half, which is also what a car is worth once
        /// you have owned it for a while.
        /// </summary>
        private readonly Dictionary<string, int> _boughtAt = new Dictionary<string, int>();

        /// <summary>How long he will pretend the sale never happened.</summary>
        private const int RegretMs = 10 * 60 * 1000;

        /// <summary>The one you are standing next to, if any.</summary>
        public CarLot NearestCar()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return null;

            CarLot best = null;
            var bestAway = CarRange;

            foreach (var car in _stock)
            {
                var away = player.Position.DistanceTo(car.Spot);
                if (away > bestAway) continue;

                bestAway = away;
                best = car;
            }

            return best;
        }

        /// <summary>
        /// Sells one. Returns what to tell the player, or null if it went through.
        ///
        /// The car you have been walking round IS the car you get -- unlocked, yours, and left
        /// exactly where it was parked. Spawning a fresh copy somewhere would be easier and
        /// would undo the entire point of putting them in a yard.
        /// </summary>
        public string Buy(CarLot car)
        {
            if (car == null) return "pick something first.";
            if (Game.Player.Money < car.Price) return "you ain't got it. Come back with it.";

            Game.Player.Money -= car.Price;

            try
            {
                if (car.Live != null && car.Live.Exists())
                {
                    var h = car.Live.Handle;

                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, h, 1);
                    Function.Call(Hash.SET_VEHICLE_HAS_BEEN_OWNED_BY_PLAYER, h, true);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);

                    car.Live.IsPersistent = true;

                    // Yours on the map until you have actually found it once.
                    //
                    // KEPT, so it can be taken down. This used to be a local that was
                    // configured and dropped on the floor -- nothing stored it, nothing deleted
                    // it, and the vehicle it is attached to is persistent, so it was never
                    // cleaned up with the car either. OwnedCars then drew its own marker for
                    // the same vehicle, so a car you had just bought carried two blips: a blue
                    // one that vanished when you got in, and a green one that followed you
                    // around for the rest of the session.
                    var blip = car.Live.AddBlip();
                    if (blip != null && blip.Exists()) _sold.Add(blip);
                    if (blip != null && blip.Exists())
                    {
                        Function.Call(Hash.SET_BLIP_SPRITE, blip.Handle, Sprite);
                        blip.Color = BlipColor.Green;
                        blip.Scale = 0.8f;
                        blip.Name = car.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not hand over " + car.Id + ": " + ex.Message);
            }

            // Written down, so it is still yours next time you start the game.
            //
            // Everything above holds the car against the population manager for as long as this
            // session lasts and not one second longer. See OwnedCars.
            if (Owned != null && car.Live != null && car.Live.Exists())
            {
                Owned.Bought(car.Id, car.Name, car.Live);
            }

            _stock.Remove(car);

            if (_state != null)
            {
                _state.CarsBought.Add(car.Id);
                _state.Touch();
            }

            // Stamped for the buy-back window. See _boughtAt.
            _boughtAt[car.Id] = Game.GameTime;

            Log.Info("Bought " + car.Id + " off Hao for $" + car.Price + ".");
            return null;
        }

        /// <summary>
        /// Which of his cars this is, or null if he never sold it to you.
        ///
        /// BY PLATE. Every car he sells gets one stamped on it out of the lot id, so the plate
        /// is the only thing that survives being driven off, parked, saved and loaded -- the
        /// handle does not and the position certainly does not.
        /// </summary>
        public CarLot His(Vehicle car)
        {
            if (car == null || !car.Exists() || _state == null) return null;

            string plate;

            // The native, not the wrapper property, which this build of SHVDN does not have --
            // and it matches how OwnedCars WRITES the plate in the first place.
            try
            {
                plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle);
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrEmpty(plate)) return null;

            plate = plate.Trim();

            foreach (var id in _state.CarsBought)
            {
                if (!string.Equals(OwnedCars.Plate(id), plate, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var lot in _all)
                {
                    if (string.Equals(lot.Id, id, StringComparison.OrdinalIgnoreCase)) return lot;
                }
            }

            return null;
        }

        /// <summary>What he will give you for it, and whether that is the full price.</summary>
        public int Offer(CarLot car, out bool full)
        {
            full = false;

            if (car == null) return 0;

            int at;

            if (_boughtAt.TryGetValue(car.Id, out at) && Game.GameTime - at < RegretMs)
            {
                full = true;
                return car.Price;
            }

            return Math.Max(1, car.Price / 2);
        }

        /// <summary>
        /// Buys one back. Returns what to tell the player, or null if it went through.
        ///
        /// THE CAR GOES BACK ON THE LOT RATHER THAN BEING DELETED, which is the whole difference
        /// between this and a bin. It is driven into its own space, locked, stripped of every
        /// flag that made it yours, and put back on the board at its old price -- so the yard
        /// after a buy-back looks exactly like the yard before you ever touched it.
        /// </summary>
        public string SellBack(Vehicle car, CarLot lot)
        {
            if (lot == null) return "that ain't one of mine.";
            if (car == null || !car.Exists()) return "bring it here first.";

            bool full;
            var paid = Offer(lot, out full);

            var me = Game.Player.Character;
            var stood = me != null && me.Exists() ? me.Position : lot.Spot;

            try
            {
                // OUT FIRST, AND INSTANTLY. TASK_LEAVE_VEHICLE is a task -- it queues an
                // animation of him opening the door and climbing out, which takes a couple of
                // seconds. The car moves on the next line, so with the ordinary flag he is
                // still sat in it and gets driven across the yard with it.
                //
                // Flag 16 is the warp, and then he is put back where he was standing anyway,
                // because a warp out leaves him beside the car and the car is about to leave.
                if (me != null && me.Exists())
                {
                    Function.Call(Hash.TASK_LEAVE_VEHICLE, me.Handle, car.Handle, 16);
                }

                car.Position = lot.Spot;
                car.Heading = lot.Heading;

                car.Speed = 0f;

                // Back to being stock. Every one of these is something Buy turned on, and a car
                // left persistent and owned is a car the population manager will not touch and
                // OwnedCars will keep standing back up.
                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, car.Handle, 2);
                Function.Call(Hash.SET_VEHICLE_HAS_BEEN_OWNED_BY_PLAYER, car.Handle, false);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                car.IsPersistent = true;
                car.PlaceOnGround();

                // Back on the pavement where he was, not wherever the warp dropped him -- which
                // is beside a car that has just been driven to the other end of the lot.
                if (me != null && me.Exists()) me.Position = stood;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not park " + lot.Id + " back up: " + ex.Message);
            }

            Game.Player.Money += paid;

            // Off the books, in both places that remember it.
            if (Owned != null) Owned.Sold(lot.Id);

            if (_state != null)
            {
                _state.CarsBought.RemoveAll(
                    id => string.Equals(id, lot.Id, StringComparison.OrdinalIgnoreCase));

                _state.Touch();
            }

            ClearSoldBlips();

            // And back on the board, as the very car you drove in.
            lot.Live = car;

            if (!_stock.Contains(lot)) _stock.Add(lot);

            Log.Info("Sold " + lot.Id + " back to Hao for $" + paid +
                     (full ? " (full price, inside the window)." : " (half price)."));

            return null;
        }

        // ---- per frame ---------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            if (IsKnown) EnsureBlip(); else DropBlip();

            var away = player.Position.DistanceTo(Spot);

            if (away > DespawnRange)
            {
                Despawn();
                return;
            }

            if (away <= SpawnRange)
            {
                if (_ped == null || !_ped.Exists()) SpawnHim();
                else if (!_held) Settle();

                StockTheYard();
                ParkHisRide();
            }
        }

        private void SpawnHim()
        {
            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _ped = World.CreatePed(model, Spot, Heading);
                    model.MarkAsNoLongerNeeded();

                    if (_ped == null || !_ped.Exists()) continue;

                    var h = _ped.Handle;

                    _ped.IsPersistent = true;
                    _ped.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL, h, false);
                    Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, h, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);

                    Settle();

                    Log.Info("Hao is on the lot (" + name + ").");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put Hao out: " + ex.Message);
                }
            }
        }

        /// <summary>Standing about looking at his own cars, which is what he would be doing.</summary>
        private void Settle()
        {
            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle,
                              "WORLD_HUMAN_CLIPBOARD", 0, true);
                _held = true;
            }
            catch
            {
                _held = false;
            }
        }

        // ---- the lot -----------------------------------------------------------

        /// <summary>
        /// Puts the cars out, and puts them back exactly where they were.
        ///
        /// Each one is re-checked rather than spawned once: a car left standing in a yard is
        /// something the game will happily tow, blow up or stream out from under you, and a
        /// showroom with holes in it is worse than no showroom.
        /// </summary>
        private void StockTheYard()
        {
            foreach (var car in _stock)
            {
                if (car.Live != null && car.Live.Exists() && car.Live.IsDriveable) continue;

                try
                {
                    if (car.Live != null && car.Live.Exists())
                    {
                        try { car.Live.Delete(); } catch { /* it was a wreck anyway */ }
                    }

                    var model = new Model(car.Model);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200))
                    {
                        Log.Debug("Hao has no " + car.Model + " on this install.");
                        continue;
                    }

                    car.Live = World.CreateVehicle(model, car.Spot, car.Heading);
                    model.MarkAsNoLongerNeeded();

                    if (car.Live == null || !car.Live.Exists()) continue;

                    Dress(car.Live, car);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put out the " + car.Id + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// His own car, and it is not for sale.
        ///
        /// The orange Penumbra he leans on in story mode. It stands apart from the stock on
        /// purpose: a man who deals cars drives one of them, and the one he keeps says more
        /// about him than the eleven he is trying to move.
        /// </summary>
        private const string RideModel = "penumbra";

        private static readonly Vector3 RideSpot = new Vector3(-33.923f, -1680.023f, 29.434f);
        private const float RideHeading = 313.928f;

        private Vehicle _ride;

        /// <summary>Set by Main. The ledger that keeps a bought car between sessions.</summary>
        public OwnedCars Owned;

        /// <summary>
        /// Every mod on the car Rockstar hand him, copied out of their own script.
        ///
        /// Not eyeballed off a screenshot. hao1.c4 builds his Penumbra at line 39721 and this is
        /// that block: primary 38 over secondary 0, pearlescent 91, then nine mod slots and
        /// three toggles. Reading it out of the script is the difference between "orange with a
        /// black bonnet" and the actual car, which also has the spoiler, the splitter, the
        /// skirts and wheel twenty on it.
        ///
        /// Slot and index, in the order they set them. Slot 7 is the bonnet -- index 2 is the
        /// carbon one, which is the black nose in every picture of him.
        /// </summary>
        private static readonly int[,] RideMods =
        {
            { 0, 2 }, { 1, 1 }, { 2, 1 }, { 3, 1 }, { 4, 1 },
            { 6, 0 }, { 7, 2 }, { 10, 0 }, { 23, 20 }
        };

        /// <summary>Turbo, and the two he switches on rather than picks an index for.</summary>
        private static readonly int[] RideToggles = { 18, 17, 22 };

        /// <summary>
        /// Puts his Penumbra where he parks it.
        ///
        /// Re-checked the same way the stock is, and for the same reason -- the game will tow or
        /// stream out a car standing in a yard, and his being missing reads as him being out.
        /// </summary>
        private void ParkHisRide()
        {
            if (_ride != null && _ride.Exists() && _ride.IsDriveable) return;

            try
            {
                if (_ride != null && _ride.Exists())
                {
                    try { _ride.Delete(); } catch { /* it was a wreck anyway */ }
                }

                var model = new Model(RideModel);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(1200))
                {
                    Log.Debug("No " + RideModel + " on this install; Hao is on foot.");
                    return;
                }

                _ride = World.CreateVehicle(model, RideSpot, RideHeading);
                model.MarkAsNoLongerNeeded();

                if (_ride == null || !_ride.Exists()) return;

                var h = _ride.Handle;

                _ride.IsPersistent = true;

                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, h);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, h, 0f);

                Function.Call(Hash.SET_VEHICLE_COLOURS, h, 38, 0);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, h, 91, 0);

                // Before any mod takes, same as the lot cars.
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);

                // Asked for and then set, which is the order their script uses. PRELOAD is what
                // stops a mod arriving a second late and popping onto the car in front of you.
                for (var i = 0; i < RideMods.GetLength(0); i++)
                {
                    Function.Call(Hash.PRELOAD_VEHICLE_MOD, h, RideMods[i, 0], RideMods[i, 1]);
                }

                for (var i = 0; i < RideMods.GetLength(0); i++)
                {
                    Function.Call(Hash.SET_VEHICLE_MOD, h, RideMods[i, 0], RideMods[i, 1], false);
                }

                foreach (var slot in RideToggles)
                {
                    Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, slot, true);
                }

                // 3 is locked to the player and nobody else, which is what they give it -- you
                // can walk round it and you cannot take it, and it does not read as a car that
                // has been abandoned with the doors open.
                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, h, 3);
                Function.Call(Hash.ROLL_DOWN_WINDOW, h, 0);

                Log.Info("Hao's Penumbra is on the lot.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not park Hao's own car: " + ex.Message);
            }
        }

        /// <summary>How a car sits on his lot: locked, clean, and on competition suspension.</summary>
        private static void Dress(Vehicle car, CarLot def)
        {
            var h = car.Handle;

            // Painted first, because a colour set after the mod kit is a colour the kit can
            // overwrite.
            //
            // Both barrels or neither. Setting a primary and leaving the secondary alone keeps
            // whatever the game rolled on the trim, which on a white car is regularly a red
            // stripe -- and the trim is half of why the lot read as red and blue in the first
            // place.
            if (def != null && def.Paint >= 0)
            {
                try
                {
                    Function.Call(Hash.SET_VEHICLE_COLOURS, h, def.Paint,
                                  def.Paint2 >= 0 ? def.Paint2 : def.Paint);
                }
                catch
                {
                    // It sells in whatever the game gave it.
                }
            }

            car.IsPersistent = true;

            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, h);

            // Locked until it is paid for. 2 is locked for everybody.
            Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, h, 2);

            Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, h, 0f);

            // A mod kit HAS to be set before any modification takes, and forgetting it is the
            // usual reason a car comes out visually stock while every call reported success.
            Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);

            try
            {
                var n = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, ModSuspension);
                if (n > 0) Function.Call(Hash.SET_VEHICLE_MOD, h, ModSuspension, n - 1, false);
            }
            catch
            {
                // It sits at stock ride height. Still sells.
            }
        }

        // ---- talking to him ----------------------------------------------------

        private bool _talkHeld;

        /// <summary>
        /// The prompt, and opening the conversation off it.
        ///
        /// Two prompts, not one, because there are two things to press at on this lot. Stood
        /// next to a car he tells you what it is and what it costs before you have opened
        /// anything -- a price you can read from the pavement is worth more than a price you
        /// have to open a menu to find -- and stood next to HIM you get the conversation.
        /// </summary>
        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen) return;

            if (!InReach)
            {
                var car = NearestCar();
                if (car == null) return;

                Help.ShowThisFrame(car.Name + "  ·  ~g~$" + car.Price.ToString("N0") + "~s~  ·  " +
                                   "see Hao at the shutter");
                return;
            }

            // SAT IN ONE OF HIS, the prompt is about that instead. Driving a car you bought
            // up to the man you bought it from is the whole of the interaction -- there is
            // nothing to open and nothing to find in a menu.
            var mine = His(Game.Player.Character == null ? null
                                                        : Game.Player.Character.CurrentVehicle);

            if (mine != null)
            {
                bool full;
                var offer = Offer(mine, out full);

                Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to sell the " + mine.Name +
                                   " back for ~g~$" + offer.ToString("N0") + "~s~" +
                                   (full ? "  ·  he'll pretend it never happened" : ""));

                if (!WantsToTalk()) return;

                var no = SellBack(Game.Player.Character.CurrentVehicle, mine);

                Notify.Important(no == null
                    ? "~g~Sold.~s~  $" + offer.ToString("N0") + " back off Hao."
                    : "~y~Hao:~s~ " + no);

                return;
            }

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to talk to Hao.");

            if (!WantsToTalk()) return;

            var root = TalkBuilder == null ? null : TalkBuilder();
            if (root == null) return;

            HoldForTalk();

            Talk.Speaker = _ped;
            Talk.Open(root, this);
        }

        private bool WantsToTalk()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                // Unreadable control is simply not pressed.
            }

            var pressed = down && !_talkHeld;
            _talkHeld = down;
            return pressed;
        }

        public void HoldForTalk()
        {
            if (_ped == null || !_ped.Exists() || !_held) return;

            _held = false;

            try
            {
                var player = Game.Player.Character;

                _ped.Task.ClearAll();

                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _ped.Handle, player.Handle, -1);
                }
            }
            catch
            {
                // He will still talk.
            }
        }

        public void ReleaseFromTalk()
        {
            if (_held || _ped == null || !_ped.Exists()) return;
            Settle();
        }

        // ---- map ---------------------------------------------------------------

        /// <summary>
        /// Whether he is on the map at all yet.
        ///
        /// Set by Main and null-checked, so a caller that does not care gets the old behaviour.
        /// Same reasoning as Lamar's: a marker on a yard in Little Seoul before anybody has
        /// vouched for you is the map introducing you to a man you have no reason to know.
        /// </summary>
        public Func<bool> Known;

        private bool IsKnown => Known == null || Known();

        /// <summary>Takes the mark off the yard, for when he is not somebody you know yet.</summary>
        private void DropBlip()
        {
            if (_blip == null) return;

            try { if (_blip.Exists()) _blip.Delete(); }
            catch { /* it was already gone */ }

            _blip = null;
        }

        private void EnsureBlip()
        {
            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(Spot);
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, Sprite);
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.8f;
                _blip.IsShortRange = true;
                _blip.Name = "Hao";
            }
            catch (Exception ex)
            {
                Log.Debug("No blip for Hao: " + ex.Message);
            }
        }

        // ---- teardown ----------------------------------------------------------

        private void Despawn()
        {
            _held = false;

            try
            {
                if (_ped != null && _ped.Exists())
                {
                    _ped.IsPersistent = false;
                    _ped.MarkAsNoLongerNeeded();
                    _ped.Delete();
                }
            }
            catch { /* he will be back */ }

            _ped = null;

            try
            {
                if (_ride != null && _ride.Exists()) _ride.Delete();
            }
            catch { /* gone */ }

            _ride = null;

            foreach (var car in _stock)
            {
                try
                {
                    if (car.Live != null && car.Live.Exists()) car.Live.Delete();
                }
                catch { /* gone */ }

                car.Live = null;
            }
        }

        /// <summary>
        /// Everything ours, put back.
        ///
        /// The cars that were SOLD are deliberately left alone -- they belong to the player now
        /// and deleting somebody's car on unload is how a mod loses you a vehicle you paid for.
        /// Only the unsold stock is ours to take away.
        /// </summary>
        /// <summary>Blips put on cars that have been sold, so they can be taken down.</summary>
        private readonly System.Collections.Generic.List<Blip> _sold =
            new System.Collections.Generic.List<Blip>();

        /// <summary>Takes down the sale markers. OwnedCars draws its own from then on.</summary>
        private void ClearSoldBlips()
        {
            foreach (var blip in _sold)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* it is gone */ }
            }

            _sold.Clear();
        }

        public void RestoreWorld()
        {
            Despawn();
            ClearSoldBlips();

            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch { /* teardown */ }

            _blip = null;
        }
    }
}
