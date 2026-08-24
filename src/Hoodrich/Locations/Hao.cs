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

                // Sold is remembered, so his lot does not quietly restock the thing you are
                // currently driving around in.
                if (_state != null && _state.CarsBought.Contains(id)) continue;

                _stock.Add(new CarLot
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
                });
            }

            Log.Info("Hao's lot: " + _stock.Count + " for sale.");
        }

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
                    var blip = car.Live.AddBlip();
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

            _stock.Remove(car);

            if (_state != null)
            {
                _state.CarsBought.Add(car.Id);
                _state.Touch();
            }

            Log.Info("Bought " + car.Id + " off Hao for $" + car.Price + ".");
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

            EnsureBlip();

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
        public void RestoreWorld()
        {
            Despawn();

            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch { /* teardown */ }

            _blip = null;
        }
    }
}
