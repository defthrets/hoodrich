using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.State;
using Hoodrich.Supply;
using Hoodrich.Territory;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>The man you have to find before a crew will have anything to do with you.</summary>
    internal sealed class LeaderDef
    {
        public string GangId = "";
        public string Name = "";

        /// <summary>Zone he holds court in. Drives both his map marker and where he stands.</summary>
        public string HomeZone = "";

        public readonly List<string> Models = new List<string>();

        /// <summary>
        /// Exactly where he stands, from leaders.json. Zero means fall back to the pavement
        /// nearest the zone centre, which is how a leader ends up in the middle of the road.
        /// Ground height is probed at runtime so there is no Z to author wrongly.
        /// </summary>
        public float SpotX;
        public float SpotY;

        public float Heading;

        /// <summary>Said when you walk up unaffiliated.</summary>
        public string Greeting = "";

        /// <summary>Said when he takes you on.</summary>
        public string Accept = "";

        /// <summary>Said when you have not earned it yet.</summary>
        public string Refuse = "";

        /// <summary>Said when you already run with him.</summary>
        public string Already = "";
    }

    /// <summary>
    /// Gang leaders: where they are, and how you get in with them.
    ///
    /// Every crew has one, permanently marked on the map so joining is something you go and
    /// DO rather than a wedge you pick. He is also the crew's first dealer -- taking you on
    /// comes with a bag fronted to you, because a crew does not hand a stranger cash, it hands
    /// him work.
    /// </summary>
    internal sealed class GangLeaders
    {
        private const float SpawnRange = 120f;
        private const float DespawnRange = 200f;
        private const float TalkRange = 3.0f;
        private const float MarkerRange = 60f;
        private const int UpdateIntervalMs = 800;

        private readonly List<LeaderDef> _defs = new List<LeaderDef>();
        private readonly Dictionary<string, Blip> _blips = new Dictionary<string, Blip>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> _spots = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly ZoneMap _zones;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;

        private LeaderDef _liveDef;
        private Ped _livePed;
        private int _lastUpdate;

        public GangLeaders(Settings cfg, GangRegistry gangs, ZoneMap zones, Affiliation crew, PlayerState state)
        {
            _cfg = cfg;
            _gangs = gangs;
            _zones = zones;
            _crew = crew;
            _state = state;

            AddDefaults();
            ApplyPlacements();
        }

        /// <summary>
        /// Overlays leaders.json onto the built-in cast: where each man stands and which way he
        /// faces. The lines stay in code because they are written to fit the gang, but a spot is
        /// a coordinate somebody will want to move without rebuilding the mod.
        /// </summary>
        private void ApplyPlacements()
        {
            var doc = JsonFile.Read(System.IO.Path.Combine(Paths.Data, "leaders.json"));
            if (doc == null)
            {
                Log.Warn("No leaders.json; leaders fall back to standing at their zone centre.");
                return;
            }

            var placed = 0;

            foreach (var node in doc["leaders"].Items)
            {
                var def = Get(node["gang"].AsString(""));
                if (def == null) continue;

                var name = node["name"].AsString("");
                if (!string.IsNullOrEmpty(name)) def.Name = name;

                var zone = node["zone"].AsString("");
                if (!string.IsNullOrEmpty(zone)) def.HomeZone = zone;

                def.SpotX = node["x"].AsFloat();
                def.SpotY = node["y"].AsFloat();
                def.Heading = node["heading"].AsFloat();

                var models = node["models"].AsStringList();
                if (models != null && models.Count > 0)
                {
                    def.Models.Clear();
                    def.Models.AddRange(models);
                }

                placed++;
            }

            Log.Info("Leader placements loaded: " + placed + ".");
        }

        public IReadOnlyList<LeaderDef> All => _defs;

        /// <summary>The leader the player is close enough to talk to, if any.</summary>
        public LeaderDef InReach
        {
            get
            {
                if (_liveDef == null || _livePed == null || !_livePed.Exists() || !_livePed.IsAlive) return null;

                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return null;

                return player.Position.DistanceTo(_livePed.Position) <= TalkRange ? _liveDef : null;
            }
        }

        public LeaderDef Get(string gangId)
        {
            return _defs.Find(d => string.Equals(d.GangId, gangId, StringComparison.OrdinalIgnoreCase));
        }

        public Vector3 SpotFor(LeaderDef def)
        {
            if (def == null) return Vector3.Zero;
            if (_spots.TryGetValue(def.GangId, out var v)) return v;

            Vector3 spot;

            if (Math.Abs(def.SpotX) > 0.01f || Math.Abs(def.SpotY) > 0.01f)
            {
                // An authored spot is a specific yard or wall, so it is used as given --
                // snapping it to the nearest pavement would put him back on the kerb.
                spot = new Vector3(def.SpotX, def.SpotY, 0f);

                try
                {
                    if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, 1000f), out var groundZ,
                                              GetGroundHeightMode.Normal))
                    {
                        spot.Z = groundZ;
                    }
                }
                catch
                {
                    // Unstreamed. Spawning resolves the height again once the player is close.
                }
            }
            else
            {
                spot = _zones.GroundedCentre(def.HomeZone);
            }

            _spots[def.GangId] = spot;
            return spot;
        }

        /// <summary>
        /// Ground height only resolves once the terrain around a spot is streamed in, which it
        /// is not when the blip is first placed from across the map. Re-probing just before he
        /// spawns is what stops him standing in mid-air or buried in the pavement.
        /// </summary>
        private Vector3 ResolveSpotNow(LeaderDef def)
        {
            var spot = SpotFor(def);
            if (spot == Vector3.Zero) return spot;

            try
            {
                if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 20f), out var groundZ,
                                          GetGroundHeightMode.Normal) && groundZ > 0f)
                {
                    spot.Z = groundZ;
                    _spots[def.GangId] = spot;
                }
            }
            catch
            {
                // Keep whatever we had.
            }

            return spot;
        }

        // ---- who they are ------------------------------------------------------

        private void AddDefaults()
        {
            Add("families", "Uncle Dee", "CHAMH",
                new[] { "g_m_y_famdnf_01", "g_m_y_famca_01", "a_m_m_soucent_01" },
                "Who sent you? Nah, don't answer. What you want.",
                "Alright. You can hang round the block, run a few things. But you ain't Family " +
                "till the set says you are. Take this, move it, come back when it's gone.",
                "Slow down. You ain't done nothin' for nobody yet. Go put some work in first.",
                "You already ride with us. Go handle your business.");

            Add("ballas", "OG Reese", "RANCHO",
                new[] { "g_m_y_ballaorig_01", "g_m_y_ballaeast_01", "a_m_m_soucent_02" },
                "You a long way from wherever you from.",
                "Purple looks alright on you. You ain't one of us yet though -- you a worker. " +
                "Take this, bring me my money, then we talk.",
                "Nah. You ain't put in nothin'. Come back when your name means somethin'.",
                "You already with us. Go on.");

            Add("vagos", "El Tio", "EBURO",
                new[] { "g_m_y_mexgoon_02", "g_m_y_mexgoon_01", "a_m_y_mexthug_01" },
                "You lost, or you looking for something.",
                "Bueno. You run for us, not with us -- not yet. Take this, move it, don't be " +
                "stupid with it. Then we see.",
                "You are nobody to me yet. Go make a name, then come back.",
                "You are already with us. Go work.");

            Add("marabunta", "Chavo", "CYPRE",
                new[] { "g_m_y_salvagoon_01", "g_m_y_salvaboss_01", "a_m_y_mexthug_01" },
                "You standing in the wrong place to be asking questions.",
                "You want in, you start at the bottom like everyone. Take this, sell it, " +
                "bring back what's ours.",
                "You have not earned a word from me. Go and do something worth hearing about.",
                "You are one of ours already.");

            Add("lost", "Bull", "SLAB",
                new[] { "g_m_y_lost_02", "g_m_y_lost_01", "a_m_m_hillbilly_01" },
                "You ain't wearing a patch, so make it quick.",
                "You're a hangaround. That's it. Hangarounds work. Take this, shift it, " +
                "don't touch it yourself.",
                "Hangarounds earn. You ain't earned. Come back when you have.",
                "You're already riding with us.");

            Add("triads", "Uncle Wei", "KOREAT",
                new[] { "g_m_m_chiboss_01", "g_m_m_chigoon_01", "a_m_y_ktown_01" },
                "You are not expected. Speak.",
                "You may work for us. You are not of us -- that takes years, not a conversation. " +
                "Take this. Return with what it is worth.",
                "You have done nothing. There is nothing to discuss. Go.",
                "You already work for us. Do so.");

            Add("armenians", "Sarkis", "ALTA",
                new[] { "g_m_m_armboss_01", "g_m_m_armgoon_01", "a_m_m_eastsa_02" },
                "You want something. Everybody wants something.",
                "You can carry for us. That is all, for now. Take this, sell it, bring the money. " +
                "Then we find out what you are.",
                "You are nobody. Nobody gets anything. Come back when that changes.",
                "You are already ours.");
        }

        private void Add(string gangId, string name, string zone, string[] models,
                         string greeting, string accept, string refuse, string already)
        {
            var def = new LeaderDef
            {
                GangId = gangId,
                Name = name,
                HomeZone = zone,
                Greeting = greeting,
                Accept = accept,
                Refuse = refuse,
                Already = already
            };
            def.Models.AddRange(models);
            _defs.Add(def);
        }

        // ---- map markers -------------------------------------------------------

        /// <summary>
        /// Every leader is marked permanently, so finding one is navigation rather than luck.
        /// The marker only disappears for the crew you already run with.
        /// </summary>
        private void SyncBlips()
        {
            foreach (var def in _defs)
            {
                var mine = _crew.IsAffiliated &&
                           string.Equals(_crew.Current.Id, def.GangId, StringComparison.OrdinalIgnoreCase);

                if (mine)
                {
                    if (_blips.TryGetValue(def.GangId, out var owned))
                    {
                        try { if (owned != null && owned.Exists()) owned.Delete(); } catch { }
                        _blips.Remove(def.GangId);
                    }
                    continue;
                }

                if (_blips.TryGetValue(def.GangId, out var existing) && existing != null && existing.Exists())
                {
                    continue;
                }

                var spot = SpotFor(def);
                if (spot == Vector3.Zero) continue;

                try
                {
                    var blip = World.CreateBlip(spot);
                    if (blip == null || !blip.Exists()) continue;

                    var gang = _gangs.Get(def.GangId);

                    blip.Sprite = BlipSprite.Enemy;
                    blip.Color = (BlipColor)(gang?.BlipColour ?? 0);
                    blip.Name = def.Name + " -- " + (gang?.Name ?? def.GangId);
                    blip.IsShortRange = false;
                    blip.Scale = 0.8f;

                    _blips[def.GangId] = blip;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not blip leader " + def.GangId + ": " + ex.Message);
                }
            }
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            SyncBlips();

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            // Whichever leader you are closest to, if you are near enough to matter.
            LeaderDef nearest = null;
            var nearestDistance = float.MaxValue;

            foreach (var def in _defs)
            {
                var spot = SpotFor(def);
                if (spot == Vector3.Zero) continue;

                var d = player.Position.DistanceTo(spot);
                if (d >= nearestDistance) continue;

                nearestDistance = d;
                nearest = def;
            }

            if (nearest == null || nearestDistance > DespawnRange)
            {
                Despawn();
                return;
            }

            if (_liveDef != null && _liveDef.GangId != nearest.GangId) Despawn();

            if (_livePed == null && nearestDistance <= SpawnRange) Spawn(nearest);
        }

        private void Spawn(LeaderDef def)
        {
            var spot = ResolveSpotNow(def);
            if (spot == Vector3.Zero) return;

            var model = ResolveModel(def);
            if (model == null) return;

            try
            {
                _livePed = World.CreatePed(model.Value, spot);
                if (_livePed == null || !_livePed.Exists()) return;

                var h = _livePed.Handle;

                if (Math.Abs(def.Heading) > 0.01f) _livePed.Heading = def.Heading;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);

                Wander();

                var gang = _gangs.Get(def.GangId);
                if (gang != null && gang.GroupHash != 0)
                {
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h, gang.GroupHash);
                }

                _livePed.IsPersistent = true;
                _livePed.BlockPermanentEvents = true;

                _liveDef = def;
                Log.Info("Leader " + def.Name + " (" + def.GangId + ") spawned in " + def.HomeZone + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Could not spawn leader " + def.GangId, ex);
            }
            finally
            {
                try { model.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        /// <summary>How far he will drift from his spot while nobody is talking to him.</summary>
        private const float WanderRadius = 35f;

        /// <summary>True while he has been stopped to talk.</summary>
        private bool _held;

        /// <summary>
        /// Sets him wandering his own corner.
        ///
        /// Standing rooted to one tile made him read as a shop counter rather than a man on his
        /// block. He walks his own patch instead -- bounded to the spot so he never wanders off
        /// his gang's streets and out of the story.
        /// </summary>
        private void Wander()
        {
            if (_livePed == null || !_livePed.Exists()) return;

            _held = false;

            try
            {
                var spot = _spots.TryGetValue(_liveDef?.GangId ?? "", out var s) ? s : _livePed.Position;

                _livePed.Task.ClearAll();
                Function.Call(Hash.TASK_WANDER_IN_AREA, _livePed.Handle,
                              spot.X, spot.Y, spot.Z, WanderRadius, 3f, 8f);
            }
            catch (Exception ex)
            {
                Log.Debug("Leader could not be set wandering: " + ex.Message);
            }
        }

        /// <summary>
        /// Stops him and turns him to face you, for as long as the conversation lasts. Called
        /// when the dialogue opens; <see cref="Wander"/> puts him back to work afterwards.
        /// </summary>
        public void HoldForTalk()
        {
            if (_livePed == null || !_livePed.Exists() || _held) return;

            _held = true;

            try
            {
                var player = Game.Player.Character;

                _livePed.Task.ClearAll();
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _livePed.Handle, player.Handle, -1);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Leader could not be held: " + ex.Message);
            }
        }

        /// <summary>Puts him back to wandering once you have finished with him.</summary>
        public void ReleaseFromTalk()
        {
            if (!_held) return;
            Wander();
        }

        /// <summary>
        /// Flat distance to the live leader, or a large number when he is not around. Used to
        /// decide whether the player has walked out of a conversation.
        /// </summary>
        public float DistanceTo(LeaderDef def)
        {
            if (def == null || _liveDef == null || _livePed == null || !_livePed.Exists()) return 9999f;
            if (!string.Equals(def.GangId, _liveDef.GangId, StringComparison.OrdinalIgnoreCase)) return 9999f;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 9999f;

            var dx = player.Position.X - _livePed.Position.X;
            var dy = player.Position.Y - _livePed.Position.Y;

            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static Model? ResolveModel(LeaderDef def)
        {
            foreach (var name in def.Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(1500)) continue;
                    return model;
                }
                catch
                {
                    // Try the next one.
                }
            }
            return null;
        }

        private void Despawn()
        {
            if (_livePed != null && _livePed.Exists())
            {
                try
                {
                    _livePed.MarkAsNoLongerNeeded();
                    _livePed.Delete();
                }
                catch { }
            }

            _livePed = null;
            _liveDef = null;
        }

        /// <summary>
        /// Set by Main. Walking up no longer dumps a subtitle at you -- he waits, and you
        /// choose to start the conversation, which is what makes it a conversation.
        /// </summary>
        public Conversation Talk;

        /// <summary>Builds what he has to say. Set by Main alongside <see cref="Talk"/>.</summary>
        public Func<LeaderDef, DialogueNode> TalkBuilder;

        /// <summary>
        /// True the frame the player asks to talk.
        ///
        /// Read through several inputs rather than one. The cellphone directions are D-pad on a
        /// pad and the arrow keys on a keyboard, but they are also a control other scripts like
        /// to disable, and a prompt you cannot answer is worse than no prompt -- so the enabled
        /// and disabled paths are both checked, and E is accepted as well.
        /// </summary>
        private bool WantsToTalk()
        {
            // Level-read rather than JUST_PRESSED: the cellphone controls report their edge
            // inconsistently depending on whether the phone is considered active, which is why
            // the prompt appeared but the press did nothing. The edge is tracked here instead.
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 2, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                // A control that cannot be read is simply not pressed.
            }

            var pressed = down && !_talkHeld;
            _talkHeld = down;

            return pressed;
        }

        /// <summary>Edge state for <see cref="WantsToTalk"/>.</summary>
        private bool _talkHeld;

        /// <summary>
        /// Offers the conversation and starts it on D-pad right. Runs every frame rather than
        /// on the slow tick, because a prompt that appears 800ms after you arrive feels broken.
        /// </summary>
        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen) return;

            var def = InReach;
            if (def == null) return;

            // ~INPUT_PHONE_RIGHT~ is not a control the game knows, so it substituted nothing and
            // the prompt read "Press  to talk". The D-pad directions are the CELLPHONE inputs.
            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to talk to " + def.Name + ".");

            if (!WantsToTalk()) return;

            if (TalkBuilder == null)
            {
                Log.Warn("Nobody built " + def.Name + " a conversation; nothing to say.");
                return;
            }

            DialogueNode root;
            try
            {
                root = TalkBuilder(def);
            }
            catch (Exception ex)
            {
                Log.Error("Building " + def.Name + "'s conversation threw.", ex);
                return;
            }

            if (root == null)
            {
                Log.Warn(def.Name + " has no opening line; check his gang id in leaders.json.");
                return;
            }

            Log.Info("Talking to " + def.Name + ".");

            HoldForTalk();
            Talk.Open(root, def);
        }

        // ---- joining -----------------------------------------------------------

        /// <summary>
        /// Signing on. Returns a player-facing refusal, or null once you are in.
        ///
        /// Joining is not a handshake -- it is being handed work. The crew fronts you a bag on
        /// the spot, which is both the tutorial and the debt that starts the relationship.
        /// </summary>
        public string Join(LeaderDef def, Drugs catalogue)
        {
            if (def == null) return "Nobody here.";

            var gang = _gangs.Get(def.GangId);
            if (gang == null) return "Nobody here.";

            if (_crew.IsAffiliated && _crew.Current.Id == gang.Id)
            {
                Dialogue.Say(def.Name, def.Already);
                return null;
            }

            if (_state.Respect < gang.JoinRespect)
            {
                Dialogue.Say(def.Name, def.Refuse);
                return "Need " + gang.JoinRespect.ToString("F0") + " respect. You have " +
                       _state.Respect.ToString("F0") + ".";
            }

            var failure = _crew.Join(gang, _state.Respect);
            if (failure != null)
            {
                Dialogue.Say(def.Name, def.Refuse);
                return failure;
            }

            Dialogue.Say(def.Name, def.Accept);
            FrontProduct(gang, catalogue);
            return null;
        }

        /// <summary>Hands over a starter bag of whatever the crew moves.</summary>
        private void FrontProduct(GangDef gang, Drugs catalogue)
        {
            if (catalogue == null || gang.Drugs.Count == 0) return;

            var product = catalogue.Get(gang.Drugs[0]);
            if (product == null) return;

            var grams = Math.Max(1f, _cfg.LeaderFrontGrams);
            var given = _state.Stash.AddPackaged(product.Id, grams, 1f);
            if (given <= 0f)
            {
                Notify.Problem("you cannot carry what they are trying to hand you.");
                return;
            }

            _state.Touch();
            Notify.Important("~g~" + given.ToString("0") + "g of " + product.Name.ToLowerInvariant() +
                             " fronted to you.~s~ Post up and move it.");
            Log.Info("Fronted " + given.ToString("0.#") + "g " + product.Id + " on joining " + gang.Id + ".");
        }

        /// <summary>Ground marker so a leader reads as somewhere to go, not just a blip.</summary>
        public void Draw()
        {
            foreach (var def in _defs)
            {
                var mine = _crew.IsAffiliated &&
                           string.Equals(_crew.Current.Id, def.GangId, StringComparison.OrdinalIgnoreCase);
                if (mine) continue;

                var spot = SpotFor(def);
                if (spot == Vector3.Zero) continue;

                var player = Game.Player.Character;
                if (player == null || !player.Exists()) continue;
                if (player.Position.DistanceTo(spot) > MarkerRange) continue;

                var gang = _gangs.Get(def.GangId);
                var colour = gang?.Colour ?? Color.White;

                try
                {
                    World.DrawMarker(MarkerType.Cylinder, spot, Vector3.Zero, Vector3.Zero,
                                     new Vector3(1.2f, 1.2f, 0.7f),
                                     Color.FromArgb(140, colour.R, colour.G, colour.B),
                                     false, false, false, null, null, false);
                }
                catch
                {
                    // Cosmetic only.
                }
            }
        }

        public void RestoreWorld()
        {
            Despawn();
            foreach (var kv in _blips)
            {
                try { if (kv.Value != null && kv.Value.Exists()) kv.Value.Delete(); } catch { }
            }
            _blips.Clear();
        }
    }
}
