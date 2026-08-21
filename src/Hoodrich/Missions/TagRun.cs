using System;
using System.Collections.Generic;
using System.IO;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
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
        /// <summary>Where the bike is left, and where the homie comes from -- Lamar's block.</summary>
        private static readonly Vector3 BikeSpot = new Vector3(-97.042f, -1610.761f, 32.313f);
        private const float BikeHeading = 56.429f;


        private const float HomeRange = 25f;

        private static readonly string[] BikeModels = { "bmx", "cruiser", "scorcher", "tribike" };

        /// <summary>
        /// How close is close enough to paint.
        ///
        /// Matched to the ring on the ground, because a prompt that appears three metres before
        /// you reach the marker makes the marker decorative -- you press the button somewhere
        /// in the street and Franklin paints the air.
        /// </summary>
        private const float ArriveRange = 1.1f;

        /// <summary>How wide the ring is drawn, which is what ArriveRange has to agree with.</summary>
        private const float MarkerSize = 2.2f;
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
        /// Animations for painting a wall, tried in order and checked before use.
        ///
        /// The first two are the game's own graffiti sets -- a man stood square on to a wall
        /// with his arm working. The earlier guesses were nightclub and sleeping clips, which
        /// is why he stood there doing nothing recognisable.
        /// </summary>
        private static readonly string[] SprayDicts =
        {
            // The real one, and it is not a clip -- it is a whole scripted sequence built for
            // exactly this, from the Cayo Perico poster tagging. A man steps up to a wall,
            // shakes the can, and paints.
            //
            // Everything under it was a guess at which dictionary might contain something
            // wall-shaped: an interaction-menu upper body, a janitor, a man hammering. They
            // stay as a fallback for an install that does not have this, and for no other
            // reason.
            SprayDict,

            "anim@mp_player_intupperspray_can",
            "anim@mp_player_intuppersmoke_cig",

            // Older name for the same action on some builds.
            "mp_player_int_upper_spray_can",

            // And the graffiti sets, if this install has them.
            "anim@mp_tagging@", "mp_tagging", "anim@mp_tagging",

            // Last resort: anybody working at something directly in front of them.
            "amb@world_human_janitor@male@idle_a",
            "amb@world_human_window_shop_browse@male@base"
        };

        /// <summary>
        /// Lamar's own wall-tagging animation, which is the one this mission is about.
        ///
        /// It lives in switch@franklin@lamar_tagging_wall -- a character switch scene, which is
        /// why it was not in any of the mission sets. The full dictionary is:
        ///
        ///   lamar_tagging_wall_loop_lamar     the man painting
        ///   lamar_tagging_wall_exit_lamar     him stepping back off it
        ///   lamar_tagging_wall_loop_franklin  the man WATCHING him paint
        ///   lamar_tagging_wall_exit_franklin
        ///   ..._cam                           the camera
        ///
        /// The _lamar pair is the right one even though the player is Franklin: in that scene
        /// Lamar is the one tagging and Franklin is stood watching, so the clip named after the
        /// observer would have Franklin watching a wall paint itself.
        /// </summary>
        private static readonly string[] LamarDicts =
        {
            "switch@franklin@lamar_tagging_wall",
        };

        private const string LamarLoop = "lamar_tagging_wall_loop_lamar";
        private const string LamarExit = "lamar_tagging_wall_exit_lamar";

        /// <summary>
        /// The poster-tagging set. Every clip in it is suffixed by who it drives: _male is the
        /// ped, _spraycan is the can in his hand, _cam is the camera.
        /// </summary>
        private const string SprayDict = "anim@scripted@freemode@postertag@graffiti_spray@male@";

        /// <summary>
        /// The sequence, in order. Step up, shake it, paint.
        ///
        /// The painting clip is the long one and the one that loops; the two before it are a
        /// second each and are what makes it read as a person rather than an animation
        /// starting. Nobody walks up to a wall already spraying.
        /// </summary>
        private static readonly string[] SprayIntro = { "intro_male", "shake_can_male" };

        private const string SprayLoop = "spray_can_male";

        private static readonly string[] SprayClips =
        {
            // The spray-can action's own clips first, then the tagging sets, then the rest.
            "idle_a", "mp_player_int_spray_can", "enter",
            "tag_loop", "tag_enter", "base", "idle"
        };

        /// <summary>
        /// The paint itself.
        ///
        /// A can with nothing coming out of it is a man miming, so the effect matters more than
        /// the animation does -- it is the only part of this the wall actually gets.
        /// </summary>
        private const string PaintAsset = "core";
        private const string PaintEffect = "ent_sht_steam";

        /// <summary>
        /// The live paint effect.
        ///
        /// Looped and held by handle rather than a puff per tick: a non-looped effect fired
        /// every frame carries on for its own lifetime after the last one is fired, which is
        /// why the smoke outlived the animation. A handle can simply be told to stop.
        /// </summary>
        private int _paintFx = -1;

        private readonly GangRegistry _gangs;
        private readonly Random _rng = new Random();

        private readonly List<TagSpot> _spots = new List<TagSpot>();
        private readonly List<Blip> _blips = new List<Blip>();
        private readonly List<Ped> _trouble = new List<Ped>();

        private readonly HashSet<string> _done =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set by the runner. Null-checked, so the feed is never load-bearing.</summary>
        public SocialFeed Social;

        private Prop _can;

        // No homie. The brief says go by yourself and it means it -- two men on bikes with
        // spray cans is a story somebody tells the police, one is somebody riding home. The
        // fields for a second rider were left behind by an older version of the job and had
        // nothing filling them in.
        private Vehicle _playerBike;
        private Blip _marker;

        private bool _rolling;

        private int _lastUpdate;
        private int _sprayingSince;
        private TagSpot _spraying;
        private bool _held;

        public TagRun(GangRegistry gangs)
        {
            _gangs = gangs;
        }

        public bool IsRunning { get; private set; }

        public bool ReadyToCollect { get; private set; }

        public string Objective
        {
            get
            {
                if (!IsRunning) return "";
                if (!_rolling) return "Get on the bike";
                if (Painted) return "Ride back to Lamar";

                return "Go over their tags  --  " + _done.Count + " of " + _spots.Count;
            }
        }

        /// <summary>Every wall done. Getting home is still between you and the money.</summary>
        private bool Painted => _spots.Count > 0 && _done.Count >= _spots.Count;

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
            if (all == null || all.Count == 0) return "Nobody could tell you where they're at.";

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
            ReadyToCollect = false;
            _rolling = false;

            _playerBike = SpawnBike(BikeSpot, BikeHeading);
            if (_playerBike == null)
            {
                IsRunning = false;
                return "Ain't no bike out there.";
            }

            Mark(BikeSpot, "Your bike");

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

            // Asked for while he is still riding, so it is resident by the time he is stood at
            // a wall. Streaming takes a moment and the request is free once it has landed.
            Preload();

            // ---- get on the bike -----------------------------------------------
            if (!_rolling)
            {
                if (_playerBike == null || !_playerBike.Exists()) return;
                if (!player.IsInVehicle(_playerBike)) return;

                _rolling = true;
                MarkAll();

                // Alone. Going over somebody's tag is a thing you do quietly and quickly, and
                // a second man on a bicycle behind you is a lookout, which changes what the job
                // is -- the whole tension here is that nobody is watching your back.
                Notify.Ticker("~g~Rolling out.~s~ Their blocks, our set.");
                return;
            }


            if (_spraying != null)
            {
                TickSpraying(player, now);
                return;
            }

            // ---- and home again -------------------------------------------------
            if (Painted)
            {
                if (player.Position.DistanceTo(Fixer.Spot) > HomeRange) return;

                ReadyToCollect = true;
                ClearMarker();
                return;
            }

            var near = Nearest(player);
            if (near == null) return;

            if (player.IsInVehicle())
            {
                Help.ShowThisFrame("Get off the bike to go over their tag.");
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

                World.DrawMarker(MarkerType.Cylinder,
                                 spot.Where - new Vector3(0f, 0f, 0.95f),
                                 Vector3.Zero, Vector3.Zero,
                                 new Vector3(MarkerSize, MarkerSize, 0.7f),
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

        // ---- riding ------------------------------------------------------------
        private Vehicle SpawnBike(Vector3 where, float heading)
        {
            var spot = Ground(where);
            spot.Z += 0.4f;

            foreach (var name in BikeModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var bike = World.CreateVehicle(model, spot, heading);
                    model.MarkAsNoLongerNeeded();

                    if (bike == null || !bike.Exists()) continue;

                    bike.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, bike.Handle, true, true);

                    return bike;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Warn("No push bike model would load for the tag run.");
            return null;
        }

        /// <summary>Only believes a ground probe that agrees with the authored height.</summary>
        private static Vector3 Ground(Vector3 where)
        {
            try
            {
                if (World.GetGroundHeight(new Vector3(where.X, where.Y, where.Z + 1.5f),
                                          out var groundZ, GetGroundHeightMode.Normal) &&
                    groundZ > 0f && Math.Abs(groundZ - where.Z) <= 3f)
                {
                    where.Z = groundZ;
                }
            }
            catch
            {
                // Keep the authored height.
            }

            return where;
        }

        private void Mark(Vector3 where, string name)
        {
            ClearMarker();

            try
            {
                _marker = World.CreateBlip(where);
                if (_marker == null || !_marker.Exists()) return;

                _marker.Color = BlipColor.Yellow;
                _marker.ShowRoute = true;
                _marker.Name = name;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the next leg: " + ex.Message);
            }
        }

        private void ClearMarker()
        {
            try { if (_marker != null && _marker.Exists()) _marker.Delete(); }
            catch { /* teardown */ }

            _marker = null;
        }

        private static void Release(Vehicle car)
        {
            try
            {
                if (car == null || !car.Exists()) return;
                car.MarkAsNoLongerNeeded();
            }
            catch { /* teardown */ }
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

                Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, PaintAsset);

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
            // Walking off, being knocked over, or just pressing the button again abandons it.
            // The wall keeps their tag and you can come back -- nothing is lost but the paint
            // and the time. An animation you cannot get out of is a cutscene, and this is not
            // one: it is eight seconds of standing still that you chose to spend.
            var walked = player.Position.DistanceTo(_spraying.Where) > ArriveRange + 1.2f;
            var cancelled = Tapped() || Moving(player);

            if (walked || cancelled || !player.IsAlive)
            {
                Notify.Problem("you left that one half done.");
                EndSpray(player);
                return;
            }

            Paint(player);

            if (now - _sprayingSince < SprayMs) return;

            _done.Add(_spraying.Id);

            Notify.Ticker("~g~That's ours now.~s~  " + _done.Count + " of " + _spots.Count);
            Log.Info("Tag " + _spraying.Id + " gone over.");

            if (Social != null) Social.On(SocialEvent.Tagged);

            EndSpray(player);
            MarkAll();

            if (Painted)
            {
                Mark(Fixer.Spot, "Lamar");
                Notify.Important("~g~That's all of 'em.~s~ Ride back to Lamar.");
            }
        }

        private void EndSpray(Ped player)
        {
            _spraying = null;
            _sprayingSince = 0;

            StopPaint();

            try
            {
                // CLEAR_PED_TASKS_IMMEDIATELY as well as the managed call, because a looping
                // TASK_PLAY_ANIM does not always let go of a ped on a plain ClearAll -- which
                // is what left Franklin painting an invisible wall after the job was done.
                player.Task.ClearAll();
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, player.Handle);
                Function.Call(Hash.STOP_ANIM_TASK, player.Handle, "", "", 3f);

                TakeCan();
            }
            catch
            {
                // He will stand up on his own.
            }
        }

        /// <summary>
        /// Nothing. The animation is the effect.
        ///
        /// There was a green particle plume on the hand bone here, added back when the
        /// animation was wrong and there was nothing else on screen to say what he was doing.
        /// The real spray-can clip does that job now, and a cloud of green fog coming off his
        /// wrist on top of it is two things saying the same thing, the louder of which is not
        /// the one that looks like paint.
        ///
        /// Kept as an empty call rather than deleted at the call sites: this is the hook to
        /// hang an effect on if one is ever wanted again, and the streaming request and the
        /// stop path either side of it are already correct.
        /// </summary>
        private void Paint(Ped player)
        {
        }

        /// <summary>Turns the paint off. Called from every path out of painting.</summary>
        private void StopPaint()
        {
            if (_paintFx == -1) return;

            try
            {
                Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, _paintFx, false);
                Function.Call(Hash.REMOVE_PARTICLE_FX, _paintFx, false);
            }
            catch { /* it will time out on its own */ }

            _paintFx = -1;
        }

        /// <summary>True when the player is trying to walk away, which cancels it.</summary>
        private static bool Moving(Ped player)
        {
            try
            {
                return Math.Abs(Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)Control.MoveLeftRight)) > 0.35f
                    || Math.Abs(Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)Control.MoveUpDown)) > 0.35f;
            }
            catch
            {
                return false;
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

        /// <summary>
        /// Gets the painting animations into memory before they are needed.
        ///
        /// This is the whole reason the mission used to hammer at the wall. REQUEST_ANIM_DICT
        /// is asynchronous -- it starts a stream and returns immediately -- and every candidate
        /// asked for its dictionary and then tested HAS_ANIM_DICT_LOADED on the next line,
        /// which is false on the frame you ask. So Lamar's set was skipped, the poster set was
        /// skipped, every fallback was skipped, and the last resort in the list is a man
        /// hammering. It was not the wrong clip name; nothing was ever given time to load.
        ///
        /// Asked for every tick from the moment the job starts. A dictionary already in memory
        /// costs nothing to re-request, so this is free after the first second.
        /// </summary>
        private static void Preload()
        {
            try
            {
                foreach (var dict in LamarDicts) Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                Function.Call(Hash.REQUEST_ANIM_DICT, SprayDict);
            }
            catch
            {
                // Nothing to do about a refused request but try again next tick.
            }
        }

        /// <summary>
        /// Waits for a dictionary, rather than glancing at it.
        ///
        /// Bounded, because a dictionary that is not in this install never arrives and the
        /// player would stand at the wall forever. With Preload doing its job this returns on
        /// the first check and the wait never happens.
        /// </summary>
        private static bool Loaded(string dict)
        {
            try
            {
                if (!Function.Call<bool>(Hash.DOES_ANIM_DICT_EXIST, dict)) return false;

                Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                var waited = 0;
                while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                {
                    if (waited >= LoadWaitMs) return false;

                    Script.Wait(LoadStepMs);
                    waited += LoadStepMs;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>How long a cold dictionary is given before it is written off.</summary>
        private const int LoadWaitMs = 1200;

        private const int LoadStepMs = 50;

        /// <summary>
        /// Lamar's wall-tagging clip, from whichever mission dictionary holds it.
        ///
        /// Loop then exit, as a sequence -- two TASK_PLAY_ANIMs issued back to back replace
        /// each other, so the exit would win and the painting would never happen. Returns false
        /// if no candidate dictionary exists, which sends the caller to the fallback.
        /// </summary>
        private static bool PlayLamarsOne(Ped player)
        {
            foreach (var dict in LamarDicts)
            {
                try
                {
                    if (!Loaded(dict)) continue;

                    var seq = new OutputArgument();
                    Function.Call(Hash.OPEN_SEQUENCE_TASK, seq);
                    var handle = seq.GetResult<int>();

                    // Flag 1 loops the painting for the length of the timer; the exit runs once
                    // at the end and puts him back on his feet.
                    Function.Call(Hash.TASK_PLAY_ANIM, 0, dict, LamarLoop,
                                  4f, -4f, SprayMs, 1, 0f, false, false, false);

                    Function.Call(Hash.TASK_PLAY_ANIM, 0, dict, LamarExit,
                                  4f, -4f, -1, 0, 0f, false, false, false);

                    Function.Call(Hash.CLOSE_SEQUENCE_TASK, handle);
                    Function.Call(Hash.TASK_PERFORM_SEQUENCE, player.Handle, handle);
                    Function.Call(Hash.CLEAR_SEQUENCE_TASK, seq);

                    Log.Info("Painting with Lamar's set from " + dict + ".");
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Debug("Lamar's tagging set would not play from " + dict + ": " + ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// The poster-tagging sequence: step up, shake the can, paint.
        ///
        /// A task sequence rather than three calls, because three TASK_PLAY_ANIMs back to back
        /// replace each other -- the third one wins and the first two never play, which is the
        /// same trap the plug's phone fell into. In a sequence they run in order.
        ///
        /// The painting clip loops for the rest of the timer. Flag 1 is the looping flag, and
        /// the intro pair are deliberately not looped: a man who shakes the can forever is
        /// worse than a man who never shakes it at all.
        /// </summary>
        private static bool PlayTheProperOne(Ped player)
        {
            try
            {
                if (!Loaded(SprayDict)) return false;

                var seq = new OutputArgument();
                Function.Call(Hash.OPEN_SEQUENCE_TASK, seq);
                var handle = seq.GetResult<int>();

                foreach (var clip in SprayIntro)
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, 0, SprayDict, clip,
                                  4f, -4f, -1, 0, 0f, false, false, false);
                }

                Function.Call(Hash.TASK_PLAY_ANIM, 0, SprayDict, SprayLoop,
                              4f, -4f, SprayMs, 1, 0f, false, false, false);

                Function.Call(Hash.CLOSE_SEQUENCE_TASK, handle);
                Function.Call(Hash.TASK_PERFORM_SEQUENCE, player.Handle, handle);
                Function.Call(Hash.CLEAR_SEQUENCE_TASK, seq);

                Log.Info("Painting with the poster-tag set.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Poster-tag set would not play: " + ex.Message);
                return false;
            }
        }

        private static void PlaySprayClip(Ped player)
        {
            // The poster-tag set first. Both work now that the dictionaries are given time to
            // load, so this is a choice between two working animations rather than a fallback
            // chain -- and spray_can_male is a man painting a wall, where Lamar's is a man
            // painting a wall in a cutscene, staged for a camera that is not there.
            if (PlayTheProperOne(player)) return;

            // Lamar's own behind it, for an install without the freemode dictionary.
            if (PlayLamarsOne(player)) return;

            foreach (var dict in SprayDicts)
            {
                if (dict == SprayDict) continue;

                try
                {
                    if (!Loaded(dict)) continue;

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

            // Nothing in this install fits, so he at least does something with his hands at a
            // wall rather than standing to attention while paint appears.
            //
            // Logged as a warning, not silently. This branch reading as normal is what let a
            // man hammer a wall through an entire mission without anything saying why.
            Log.Warn("No painting animation would load -- falling back to the hammer. " +
                     "Lamar's set is " + string.Join(", ", LamarDicts) + ".");

            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, player.Handle,
                              "WORLD_HUMAN_HAMMERING", 0, true);
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
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                    // 46 is BF_CanFightArmedPedsWhenNotArmed, NOT BF_AlwaysFight. That is 5.
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

            StopPaint();
            TakeCan();
            ClearBlips();
            ClearMarker();

            // The bike is left where it ended up rather than deleted, same as the ride out.
            Release(_playerBike);
            _playerBike = null;

            _rolling = false;
            ReadyToCollect = false;

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
