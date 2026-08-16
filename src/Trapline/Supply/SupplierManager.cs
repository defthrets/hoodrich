using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using Trapline.Core;
using Trapline.Economy;
using Trapline.State;
using Trapline.UI;

namespace Trapline.Supply
{
    /// <summary>
    /// Arranges and runs supply meets.
    ///
    /// Only one meet can be live at a time. The blip is created immediately so the player has
    /// somewhere to drive; the ped itself is not spawned until they are close, which keeps a
    /// pending meet from burning a ped slot across half the map.
    /// </summary>
    internal sealed class SupplierManager
    {
        private const float MeetMinDistance = 90f;
        private const float MeetMaxDistance = 170f;
        private const float SpawnRange = 90f;
        private const float DespawnRange = 240f;
        private const float TradeRange = 4.0f;
        private const int MeetTimeoutMs = 15 * 60 * 1000;
        private const int UpdateIntervalMs = 500;

        private readonly List<SupplierDef> _defs = new List<SupplierDef>();
        private readonly Random _rng = new Random();

        private SupplierDef _meetDef;
        private Vector3 _meetPosition;
        private Ped _meetPed;
        private Blip _meetBlip;
        private int _meetStartedAt;
        private int _lastUpdate;

        public IReadOnlyList<SupplierDef> All => _defs;

        public SupplierDef ActiveMeet => _meetDef;

        public bool HasMeet => _meetDef != null;

        /// <summary>The meet ped, if it is spawned and the player is close enough to trade.</summary>
        public Ped TradablePed
        {
            get
            {
                if (_meetPed == null || !_meetPed.Exists() || !_meetPed.IsAlive) return null;

                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return null;

                return player.Position.DistanceTo(_meetPed.Position) <= TradeRange ? _meetPed : null;
            }
        }

        public float MeetDistance
        {
            get
            {
                if (_meetDef == null) return 0f;
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return 0f;
                return player.Position.DistanceTo(_meetPosition);
            }
        }

        // ---- loading -----------------------------------------------------------

        public static SupplierManager Load()
        {
            var mgr = new SupplierManager();
            mgr.AddDefaults();

            var path = Path.Combine(Paths.Data, "suppliers.json");
            var doc = JsonFile.Read(path);
            if (doc != null) mgr.ApplyOverrides(doc);

            Log.Info("Suppliers loaded: " + mgr._defs.Count + " contacts.");
            return mgr;
        }

        private void ApplyOverrides(Json doc)
        {
            var list = doc.Kind == JsonKind.Array ? doc : doc["suppliers"];
            if (list.Kind != JsonKind.Array) return;

            foreach (var node in list.Items)
            {
                var id = node["id"].AsString(null);
                if (string.IsNullOrEmpty(id)) continue;

                var def = _defs.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
                var isNew = def == null;
                if (isNew) def = new SupplierDef { Id = id };

                def.Name = node["name"].AsString(def.Name.Length > 0 ? def.Name : id);
                def.Tag = node["tag"].AsString(def.Tag);
                def.Blurb = node["blurb"].AsString(def.Blurb);
                def.PriceMultiplier = Math.Max(0.1f, node["priceMultiplier"].AsFloat(def.PriceMultiplier));
                def.MinRank = Math.Max(0, node["minRank"].AsInt(def.MinRank));
                def.MaxOrderGrams = Math.Max(1f, node["maxOrderGrams"].AsFloat(def.MaxOrderGrams));
                def.OpenHour = node["openHour"].AsInt(def.OpenHour);
                def.CloseHour = node["closeHour"].AsInt(def.CloseHour);

                var kind = node["kind"].AsString(def.Kind.ToString());
                try { def.Kind = (SupplierKind)Enum.Parse(typeof(SupplierKind), kind, true); }
                catch { Log.Warn("Unknown supplier kind '" + kind + "' on " + id + "."); }

                if (node["models"].Kind == JsonKind.Array)
                {
                    def.Models.Clear();
                    def.Models.AddRange(node["models"].AsStringList());
                }

                if (node["drugs"].Kind == JsonKind.Array)
                {
                    def.Drugs.Clear();
                    def.Drugs.AddRange(node["drugs"].AsStringList());
                }

                if (isNew) _defs.Add(def);
            }
        }

        private void AddDefaults()
        {
            _defs.Add(new SupplierDef
            {
                Id = "docks",
                Name = "Dock Foreman",
                Tag = "DOCK",
                Kind = SupplierKind.Docks,
                PriceMultiplier = 0.85f,
                MinRank = 0,
                MaxOrderGrams = 120f,
                OpenHour = 6,
                CloseHour = 20,
                Blurb = "Skims containers. Cheap, daylight only.",
                Models = { "s_m_y_dockwork_01", "s_m_m_dockwork_01", "s_m_y_construct_01", "a_m_m_business_01" },
                Drugs = { "weed", "coke" }
            });

            _defs.Add(new SupplierDef
            {
                Id = "mob",
                Name = "The Mob",
                Tag = "MOB",
                Kind = SupplierKind.Mob,
                PriceMultiplier = 1.15f,
                MinRank = 2,
                MaxOrderGrams = 250f,
                OpenHour = 20,
                CloseHour = 5,
                Blurb = "Weight, no questions. Costs you.",
                Models = { "g_m_m_armboss_01", "g_m_m_armgoon_01", "a_m_m_eastsa_02", "a_m_y_business_01" },
                Drugs = { "coke", "heroin", "meth" }
            });

            _defs.Add(new SupplierDef
            {
                Id = "gangsters",
                Name = "Out-of-town Crew",
                Tag = "CREW",
                Kind = SupplierKind.Gang,
                PriceMultiplier = 1f,
                MinRank = 1,
                MaxOrderGrams = 150f,
                OpenHour = 0,
                CloseHour = 24,
                Blurb = "Mid price. They ask who you run with.",
                Models = { "g_m_y_mexgoon_01", "g_m_y_lost_01", "a_m_y_stwhi_01", "a_m_m_soucent_01" },
                Drugs = { "crack", "meth", "weed" }
            });

            _defs.Add(new SupplierDef
            {
                Id = "street",
                Name = "Corner Connect",
                Tag = "CNCT",
                Kind = SupplierKind.Street,
                PriceMultiplier = 1.25f,
                MinRank = 0,
                MaxOrderGrams = 40f,
                OpenHour = 0,
                CloseHour = 24,
                Blurb = "Small weight, always picks up.",
                Models = { "a_m_y_soucent_01", "a_m_m_soucent_02", "a_m_y_downtown_01" },
                Drugs = { "weed", "crack" }
            });
        }

        public SupplierDef Get(string id)
        {
            return _defs.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        // ---- meets -------------------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null if the meet was arranged.</summary>
        public string ArrangeMeet(SupplierDef def, PlayerState state)
        {
            if (def == null) return "No such contact.";
            if (HasMeet) return "You already have a meet on -- " + _meetDef.Name + ".";
            if (state.Rank < def.MinRank)
            {
                return "They will not take your call. Need rank " +
                       PlayerState.RankNames[Math.Min(def.MinRank, PlayerState.RankNames.Length - 1)] + ".";
            }

            var hour = Pricing.ClockHour;
            if (!def.IsOpenAt(hour))
            {
                return def.Name + " works " + def.OpenHour + ":00 to " + def.CloseHour + ":00.";
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return "Not right now.";

            if (!TryPickMeetSpot(player.Position, out _meetPosition))
            {
                return "Nowhere good to meet around here. Try somewhere less remote.";
            }

            _meetDef = def;
            _meetStartedAt = Game.GameTime;
            CreateBlip(def);

            Notify.Important("~y~" + def.Name + "~s~ will meet you. Marked on your map.");
            Log.Info("Meet arranged with " + def.Id + " at " + _meetPosition + ".");
            return null;
        }

        /// <summary>
        /// Picks a spot on a pavement a short drive away. Tries several bearings before giving
        /// up, so being next to water or a cliff does not deadlock the meet.
        /// </summary>
        private bool TryPickMeetSpot(Vector3 origin, out Vector3 spot)
        {
            spot = Vector3.Zero;

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var angle = _rng.NextDouble() * Math.PI * 2.0;
                var distance = MeetMinDistance + (float)_rng.NextDouble() * (MeetMaxDistance - MeetMinDistance);

                var candidate = origin + new Vector3(
                    (float)Math.Cos(angle) * distance,
                    (float)Math.Sin(angle) * distance,
                    0f);

                Vector3 onFoot;
                try
                {
                    onFoot = World.GetNextPositionOnSidewalk(candidate);
                }
                catch
                {
                    continue;
                }

                if (onFoot == Vector3.Zero) continue;
                if (onFoot.DistanceTo(origin) < MeetMinDistance * 0.5f) continue;

                try
                {
                    if (World.GetGroundHeight(onFoot, out var groundZ, GetGroundHeightMode.Normal))
                    {
                        onFoot.Z = groundZ;
                    }
                }
                catch
                {
                    // Keep the sidewalk Z; it is usually right already.
                }

                spot = onFoot;
                return true;
            }

            return false;
        }

        private void CreateBlip(SupplierDef def)
        {
            try
            {
                _meetBlip = World.CreateBlip(_meetPosition);
                if (_meetBlip == null || !_meetBlip.Exists()) return;

                _meetBlip.Sprite = BlipSprite.Friend;
                _meetBlip.Color = BlipColor.Yellow;
                _meetBlip.Name = "Meet: " + def.Name;
                _meetBlip.IsShortRange = false;
                _meetBlip.ShowRoute = true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not create meet blip: " + ex.Message);
            }
        }

        public void CancelMeet(string reason)
        {
            if (_meetDef == null) return;

            var name = _meetDef.Name;
            Cleanup();

            if (!string.IsNullOrEmpty(reason)) Notify.Ticker("~o~Meet with " + name + " is off.~s~ " + reason);
        }

        private void Cleanup()
        {
            try
            {
                if (_meetPed != null && _meetPed.Exists())
                {
                    _meetPed.MarkAsNoLongerNeeded();
                    _meetPed.Delete();
                }
            }
            catch
            {
                // Ped may already be gone.
            }

            try
            {
                if (_meetBlip != null && _meetBlip.Exists()) _meetBlip.Delete();
            }
            catch
            {
                // Blip may already be gone.
            }

            _meetPed = null;
            _meetBlip = null;
            _meetDef = null;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (_meetDef == null) return;

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            if (now - _meetStartedAt > MeetTimeoutMs)
            {
                CancelMeet("They got tired of waiting.");
                return;
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var distance = player.Position.DistanceTo(_meetPosition);

            // The contact dying ends the arrangement.
            if (_meetPed != null && _meetPed.Exists() && !_meetPed.IsAlive)
            {
                CancelMeet("Your contact is dead.");
                return;
            }

            if (_meetPed == null && distance <= SpawnRange)
            {
                SpawnContact();
            }
            else if (_meetPed != null && distance > DespawnRange)
            {
                // Too far to matter; free the ped but keep the meet alive.
                try
                {
                    if (_meetPed.Exists())
                    {
                        _meetPed.MarkAsNoLongerNeeded();
                        _meetPed.Delete();
                    }
                }
                catch
                {
                    // Nothing to do.
                }
                _meetPed = null;
            }
        }

        private void SpawnContact()
        {
            var model = ResolveModel(_meetDef);
            if (model == null)
            {
                CancelMeet("Could not find your contact.");
                return;
            }

            try
            {
                var heading = (float)(_rng.NextDouble() * 360.0);
                _meetPed = World.CreatePed(model.Value, _meetPosition, heading);

                if (_meetPed == null || !_meetPed.Exists())
                {
                    Log.Warn("CreatePed returned nothing for supplier " + _meetDef.Id + ".");
                    return;
                }

                var h = _meetPed.Handle;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, h, "WORLD_HUMAN_STAND_MOBILE", 0, true);

                _meetPed.IsPersistent = true;
                _meetPed.BlockPermanentEvents = true;

                if (_meetBlip != null && _meetBlip.Exists()) _meetBlip.ShowRoute = false;

                Log.Info("Spawned supplier " + _meetDef.Id + " as " + model.Value.Hash + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Could not spawn supplier " + _meetDef.Id, ex);
            }
            finally
            {
                try { model.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        /// <summary>
        /// Walks the model fallback chain and returns the first that actually loads. A model
        /// name that is wrong, renamed, or DLC-only therefore costs flavour, not the feature.
        /// </summary>
        private static Model? ResolveModel(SupplierDef def)
        {
            foreach (var name in def.Models)
            {
                if (string.IsNullOrEmpty(name)) continue;

                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage)
                    {
                        Log.Debug("Supplier model '" + name + "' is not in this install; trying next.");
                        continue;
                    }

                    if (!model.Request(1500))
                    {
                        Log.Debug("Supplier model '" + name + "' would not stream in; trying next.");
                        continue;
                    }

                    return model;
                }
                catch (Exception ex)
                {
                    Log.Debug("Supplier model '" + name + "' failed: " + ex.Message);
                }
            }

            Log.Warn("No usable model for supplier " + def.Id + " (" + def.Models.Count + " tried).");
            return null;
        }

        /// <summary>Removes the meet ped and blip. Called on script unload.</summary>
        public void RestoreWorld() => Cleanup();
    }
}
