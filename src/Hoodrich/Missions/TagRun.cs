using System;
using System.Collections.Generic;
using System.IO;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Missions
{
    /// <summary>One wall somebody else has already written on.</summary>
    internal sealed class TagSpot
    {
        public string Id = "";
        public string Zone = "";
        public string Gang = "";
        public Vector3 Where;
        public float Heading;

        public override string ToString() => Id;
    }

    /// <summary>
    /// Going over their tags with yours.
    ///
    /// The thing this is modelled on paints your own artwork onto the wall. That is not
    /// something a script can do -- new art on a surface is a texture asset, installed into the
    /// game files, and this mod is a DLL and some JSON. So the wall does not change.
    ///
    /// What the wall not changing costs is less than it sounds, because none of the tension was
    /// ever in the picture. It is in standing still for eight seconds, with your back to the
    /// street, in a neighbourhood where you are the wrong colour -- and all of that is real.
    /// The marker turns over, the can comes out, the paint goes on, and while you are doing it
    /// the block is deciding whether it has noticed you.
    /// </summary>
    internal sealed class TagRun
    {
        private const float ArriveRange = 3.5f;
        private const int SprayMs = 8000;
        private const int UpdateIntervalMs = 300;

        /// <summary>How far a tag is visible as a marker on the ground.</summary>
        private const float MarkerRange = 60f;

        /// <summary>Chance per tag that the block turns out to have people on it.</summary>
        private const float TroubleChance = 0.45f;
        private const int TroubleCount = 2;
        private const float TroubleSpread = 14f;

        private const int PedTypeCiv = 4;

        /// <summary>
        /// Spray-can props, tried in order. An install without any of them still gets the job,
        /// it just gets it without a can in shot.
        /// </summary>
        private static readonly string[] CanProps =
        {
            "prop_cs_spray_can", "prop_spray_can", "prop_paint_spray01a"
        };

        /// <summary>
        /// Animations for painting a wall. Validated before use, because a scenario or clip
        /// name that is not in this install fails silently and leaves Franklin standing there.
        /// </summary>
        private static readonly string[] SprayDicts =
        {
            "anim@amb@nightclub@peds@", "switch@trevor@trailer_sleeping", "missfbi3_party_d"
        };

        private static readonly string[] SprayClips = { "base", "idle_a", "idle" };

        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly Random _rng = new Random();

        private readonly List<TagSpot> _spots = new List<TagSpot>();
        private readonly List<Blip> _blips = new List<Blip>();
        private readonly List<Ped> _trouble = new List<Ped>();

        private readonly HashSet<string> _done =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private MissionDef _def;
        private Prop _can;

        private int _lastUpdate;
        private int _sprayingSince;
        private TagSpot _spraying;
        private bool _held;

        public TagRun(GangRegistry gangs, Affiliation crew)
        {
            _gangs = gangs;
            _crew = crew;
        }

        public bool IsRunning { get; private set; }

        public bool ReadyToCollect => IsRunning && _done.Count >= _spots.Count && _spots.Count > 0;

        public string Objective
        {
            get
            {
                if (!IsRunning) return "";
                if (ReadyToCollect) return "Go back to Lamar for the money";

                return "Go over their tags  --  " + _done.Count + " of " + _spots.Count;
            }
        }

        // ---- the list ----------------------------------------------------------

        /// <summary>
        /// The walls, from tags.json.
        ///
        /// A data file rather than constants because these are the one thing in the mod that
        /// nobody can get right from a desk: a tag has to be on a wall you can stand in front
        /// of, facing the right way, on a block that belongs to somebody. Every one of them
        /// wants standing on and reading off the HUD.
        /// </summary>
        public static List<TagSpot> Load()
        {
            var spots = new List<TagSpot>();

            var doc = JsonFile.Read(Path.Combine(Paths.Data, "tags.json"));
            if (doc == null)
            {
                Log.Warn("No tags.json; the tag run will have nowhere to go.");
                return spots;
            }

            foreach (var node in doc["tags"].Items)
            {
                var id = node["id"].AsString("");
                if (string.IsNullOrEmpty(id)) continue;

                spots.Add(new TagSpot
                {
                    Id = id,
                    Zone = node["zone"].AsString(""),
                    Gang = node["gang"].AsString(""),
                    Where = new Vector3(node["x"].AsFloat(), node["y"].AsFloat(), node["z"].AsFloat()),
                    Heading = node["heading"].AsFloat()
                });
            }

            Log.Info("Tag spots loaded: " + spots.Count + ".");
            return spots;
        }

        // ---- starting ----------------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once the run is on.</summary>
        public string Start(MissionDef def, List<TagSpot> all)
        {
            if (all == null || all.Count == 0) return "Nobody could tell you where they are.";

            _def = def;
            _spots.Clear();
            _done.Clear();

            // However many the job asks for, drawn from the whole list at random, so running it
            // again is not the same afternoon twice.
            var pool = new List<TagSpot>(all);

            var want = Math.Max(1, Math.Min(def.Targets, pool.Count));

            for (var i = 0; i < want; i++)
            {
                var pick = _rng.Next(pool.Count);
                _spots.Add(pool[pick]);
                pool.RemoveAt(pick);
            }

            IsRunning = true;
            MarkAll();

            Log.Info("Tag run started with " + _spots.Count + " walls.");
            return null;
        }

        private void MarkAll()
        {
            ClearBlips();

            foreach (var spot in _spots)
            {
                if (_done.Contains(spot.Id)) continue;

                try
                {
                    var blip = World.CreateBlip(spot.Where);
                    if (blip == null || !blip.Exists()) continue;

                    var gang = _gangs.Get(spot.Gang);

                    Function.Call(Hash.SET_BLIP_SPRITE, blip.Handle, 464);
                    blip.Color = gang == null ? BlipColor.Red : BlipColor.Purple;
                    blip.Scale = 0.8f;
                    blip.Name = gang == null ? "Tag" : gang.Name + " tag";

                    _blips.Add(blip);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not mark a tag: " + ex.Message);
                }
            }
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (!IsRunning) return;

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            if (_spraying != null)
            {
                TickSpraying(player, now);
                return;
            }

            if (ReadyToCollect) return;

            var near = Nearest(player);
            if (near == null) return;

            if (player.IsInVehicle())
            {
                Help.ShowThisFrame("Get out to go over their tag.");
                return;
            }

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to go over their tag.");

            if (Tapped()) BeginSpray(player, near);
        }

        /// <summary>Draws the ground markers, which is what actually leads you to a wall.</summary>
        public void Draw()
        {
            if (!IsRunning) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            foreach (var spot in _spots)
            {
                if (_done.Contains(spot.Id)) continue;
                if (player.Position.DistanceTo(spot.Where) > MarkerRange) continue;

                var gang = _gangs.Get(spot.Gang);
                var colour = gang == null ? System.Drawing.Color.FromArgb(160, 190, 60, 190) : gang.Colour;

                World.DrawMarker(MarkerType.VerticalCylinder,
                                 spot.Where - new Vector3(0f, 0f, 0.95f),
                                 Vector3.Zero, Vector3.Zero, new Vector3(0.8f, 0.8f, 0.7f),
                                 System.Drawing.Color.FromArgb(120, colour.R, colour.G, colour.B));
            }

            if (_spraying == null) return;

            // A bar for the one thing in this job that asks you to stand still.
            var done = Math.Min(1f, (Game.GameTime - _sprayingSince) / (float)SprayMs);

            const float x = 0.5f;
            const float y = 0.80f;
            const float w = 0.20f;
            const float h = 0.016f;

            Hud.Rect(x, y, w + 0.004f, h + 0.004f, System.Drawing.Color.FromArgb(190, 8, 8, 10));
            Hud.Rect(x, y, w, h, System.Drawing.Color.FromArgb(160, 30, 32, 34));

            var filled = w * done;
            Hud.Rect(x - (w - filled) * 0.5f, y, filled, h, Palette.Cash);

            Hud.Text("PAINTING", x, y - 0.040f, 0.34f, Palette.Cash, Hud.FontLabel);
        }

        private TagSpot Nearest(Ped player)
        {
            foreach (var spot in _spots)
            {
                if (_done.Contains(spot.Id)) continue;
                if (player.Position.DistanceTo(spot.Where) <= ArriveRange) return spot;
            }

            return null;
        }

        // ---- painting ----------------------------------------------------------

        private void BeginSpray(Ped player, TagSpot spot)
        {
            _spraying = spot;
            _sprayingSince = Game.GameTime;

            try
            {
                player.Task.ClearAll();
                player.Heading = spot.Heading;

                GiveCan(player);
                PlaySprayClip(player);

                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Beep_Red",
                              "DLC_HEIST_HACKING_SNAKE_SOUNDS", true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not start painting: " + ex.Message);
            }

            // Their block, and you are stood on it with your back to the road.
            if (_rng.NextDouble() < TroubleChance) SpawnTrouble(player, spot);
        }

        private void TickSpraying(Ped player, int now)
        {
            // Walking off or being knocked over abandons it. The wall keeps their tag, and you
            // can come back -- nothing is lost except the paint and the time.
            if (player.Position.DistanceTo(_spraying.Where) > ArriveRange + 2f || !player.IsAlive)
            {
                Notify.Problem("you left that one half done.");
                EndSpray(player);
                return;
            }

            if (now - _sprayingSince < SprayMs) return;

            _done.Add(_spraying.Id);

            Notify.Ticker("~g~That is ours now.~s~  " + _done.Count + " of " + _spots.Count);
            Log.Info("Tag " + _spraying.Id + " gone over.");

            EndSpray(player);
            MarkAll();

            if (ReadyToCollect) Notify.Important("~g~That is all of them.~s~ Go back to Lamar.");
        }

        private void EndSpray(Ped player)
        {
            _spraying = null;
            _sprayingSince = 0;

            try
            {
                player.Task.ClearAll();
                TakeCan();
            }
            catch
            {
                // He will stand up on his own.
            }
        }

        private void GiveCan(Ped player)
        {
            TakeCan();

            foreach (var name in CanProps)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(800)) continue;

                    _can = World.CreateProp(model, player.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_can == null || !_can.Exists()) continue;

                    // Right hand, turned so the nozzle points at the wall.
                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _can.Handle, player.Handle,
                                  Function.Call<int>(Hash.GET_PED_BONE_INDEX, player.Handle, 57005),
                                  0.10f, 0.02f, -0.02f, -90f, 0f, 0f,
                                  false, false, false, false, 2, true);

                    return;
                }
                catch
                {
                    // Try the next prop.
                }
            }

            Log.Debug("No spray can prop in this install.");
        }

        private void TakeCan()
        {
            try
            {
                if (_can != null && _can.Exists()) _can.Delete();
            }
            catch { /* teardown */ }

            _can = null;
        }

        private static void PlaySprayClip(Ped player)
        {
            foreach (var dict in SprayDicts)
            {
                try
                {
                    if (!Function.Call<bool>(Hash.DOES_ANIM_DICT_EXIST, dict)) continue;

                    Function.Call(Hash.REQUEST_ANIM_DICT, dict);
                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) continue;

                    foreach (var clip in SprayClips)
                    {
                        Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, dict, clip,
                                      4f, -4f, SprayMs, 1, 0f, false, false, false);

                        Log.Info("Painting with " + dict + " / " + clip + ".");
                        return;
                    }
                }
                catch
                {
                    // Try the next dictionary.
                }
            }

            // Nothing in this install fits, so he at least reaches up at the wall rather than
            // standing to attention while paint appears.
            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, player.Handle,
                              "WORLD_HUMAN_STAND_IMPATIENT", 0, true);
            }
            catch { /* he will stand there */ }

            Log.Debug("No spray animation dictionary answered.");
        }

        // ---- the block noticing ------------------------------------------------

        private void SpawnTrouble(Ped player, TagSpot spot)
        {
            var gang = _gangs.Get(spot.Gang);
            if (gang == null) return;

            for (var i = 0; i < TroubleCount; i++)
            {
                var ped = SpawnMember(gang, spot.Where.Around(TroubleSpread));
                if (ped == null) continue;

                _trouble.Add(ped);

                try
                {
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                }
                catch { /* the AI takes over */ }
            }

            if (_trouble.Count > 0) Notify.Problem("somebody saw you.");
        }

        private Ped SpawnMember(GangDef gang, Vector3 near)
        {
            foreach (var name in gang.MemberModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var spot = World.GetNextPositionOnSidewalk(near);
                    if (spot == Vector3.Zero) spot = near;

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                    spot.X, spot.Y, spot.Z, 0f, false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"),
                                  90, false, true);

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        // ---- input -------------------------------------------------------------

        private bool Tapped()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                // Unreadable control is simply not pressed.
            }

            var pressed = down && !_held;
            _held = down;
            return pressed;
        }

        // ---- finishing ---------------------------------------------------------

        public void Clear()
        {
            var player = Game.Player.Character;
            if (_spraying != null && player != null && player.Exists()) EndSpray(player);

            TakeCan();
            ClearBlips();

            foreach (var ped in _trouble)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }

            _trouble.Clear();
            _spots.Clear();
            _done.Clear();

            _def = null;
            _spraying = null;
            IsRunning = false;
        }

        private void ClearBlips()
        {
            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* teardown */ }
            }

            _blips.Clear();
        }

        public void RestoreWorld() => Clear();
    }
}
