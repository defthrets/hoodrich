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

        /// <summary>
        /// Whether nobody turns up to defend this one.
        ///
        /// Their paint can be on a wall without their people being round the corner from it.
        /// A tag on Forum Drive is somebody who drove over, wrote on a wall and left -- it is
        /// an insult precisely BECAUSE they could not have stood there afterwards.
        /// </summary>
        public bool Quiet;

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
        /// <summary>
        /// Where the bike is left, and where the homie comes from.
        ///
        /// The lot, with everybody else, rather than the old spot four streets away. A job that
        /// starts by walking somewhere else is a job that starts with a walk.
        /// </summary>
        private static readonly Vector3 BikeSpot = new Vector3(-211.331f, -1720.275f, 31.995f);
        private const float BikeHeading = 109.394f;


        /// <summary>
        /// How close to Lamar counts as back.
        ///
        /// The same figure the bike ride uses. Two jobs that hand in on the same square of
        /// concrete were reading that square as two different sizes, so one of them took the
        /// job off you where you stood and the other had you circling the lot.
        /// </summary>
        private const float HomeRange = 38f;

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
        /// <summary>
        /// How long he stands there painting, start to finish.
        ///
        /// ONE number and everything reads it: the progress bar, the check that says he has
        /// finished, and the length handed to all three of the animation tasks -- so the clip,
        /// the paint jet, the stains going down and the can in his hand all end together
        /// because they were all told the same figure. That is why three seconds more is a
        /// three-second edit here and nowhere else.
        /// </summary>
        private const int SprayMs = 11000;
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
            // What used to sit under it was a guess at which dictionary might contain
            // something wall-shaped, and FIVE OF THE SIX GUESSES WERE NOT REAL DICTIONARIES.
            // Checked against the game's own dump of every dict and clip in the build:
            //
            //   anim@mp_player_intupperspray_can       does not exist
            //   anim@mp_player_intuppersmoke_cig       does not exist
            //   mp_player_int_upper_spray_can          does not exist
            //   anim@mp_tagging / @ / anim@mp_tagging@ does not exist, in any spelling
            //   amb@world_human_window_shop_browse@..  misspelt; the dict has no _browse
            //
            // There is no spray-can interaction-menu animation in GTA V. The only spray
            // dictionaries in the whole game are the poster-tagging pair and champagne. So a
            // list that read as six careful fallbacks was one real animation and five names
            // that fail SILENTLY -- REQUEST_ANIM_DICT on a dictionary that is not there simply
            // never completes, which is why this needed a wait loop to paper over.
            SprayDict,

            // The same animation authored for the other rig. Same 31 clips under different
            // suffixes, so if the male set were ever missing this is genuinely the same thing.
            "anim@scripted@freemode@postertag@graffiti_spray@heeled@",

            // Last resort: anybody working at something directly in front of them. Both of
            // these are real, and the second is the corrected spelling of the old one.
            "amb@world_human_janitor@male@idle_a",
            "amb@world_human_window_shop@male@idle_a"
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

        /// <summary>
        /// Whether the block has already come out at you this run.
        ///
        /// It was a dice roll on every single wall, which meant killing the two who turned up
        /// bought you nothing -- start the next tag and there was a fair chance of two more,
        /// out of the same doorway, for as long as the job lasted. That is not a block
        /// reacting to you, it is a tap somebody left running.
        ///
        /// Once a run. They come out, you deal with them, and the rest of the afternoon is
        /// yours. Reset in Start, so taking the job again is a fresh block with its own two --
        /// which is the bit that keeps it from becoming a place you know is safe.
        /// </summary>
        private bool _troubleSpent;

        private readonly HashSet<string> _done =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set by the runner. Null-checked, so the feed is never load-bearing.</summary>
        public SocialFeed Social;

        /// <summary>
        /// Whose colour goes on the wall.
        ///
        /// Set by the runner and null-checked. The jet was a hardcoded green because the job is
        /// Lamar's and Lamar is Families -- which is true and still leaves the one number that
        /// says WHOSE wall this now is sitting in a source file rather than in gangs.json with
        /// every other gang colour in the mod.
        /// </summary>
        public Affiliation Crew;

        /// <summary>
        /// The set's colour as particle and decal coefficients, 0-1.
        ///
        /// Falls back to the Families green the jet always used, so an unaffiliated player
        /// painting a wall on Lamar's say-so gets exactly what they got before.
        /// </summary>
        /// <summary>
        /// What the can writes. CGF or FAM, whichever comes up.
        ///
        /// Both are the same set and both are what gets written on walls in Davis, so picking
        /// between them per wall means four walls in a run are not four copies of one stencil.
        /// Anybody else gets their own tag out of gangs.json -- and if that tag has a letter
        /// nobody has drawn, CanWrite says so and the old random splatter takes it, which is a
        /// worse tag but is never a blank wall.
        /// </summary>
        private string TagToWrite()
        {
            var gang = Crew == null ? null : Crew.Current;

            var mine = gang == null ? "" : (gang.Tag ?? "");

            if (string.Equals(mine, "FAM", StringComparison.OrdinalIgnoreCase))
            {
                return _rng.Next(2) == 0 ? "CGF" : "FAM";
            }

            return mine;
        }

        /// <summary>
        /// How far apart the dots sit along a letter, in letter heights.
        ///
        /// 0.11, down from 0.17, and the offline render is what got that wrong. On a flat
        /// preview at a few hundred pixels the wider spacing read beautifully -- you could see
        /// the individual passes of the can. On a wall at full size the same figure leaves the
        /// strokes beaded, because a paint splatter decal is not a solid disc: it is mostly
        /// gaps with paint round them, so a mark covers a good deal less than its own width.
        ///
        /// The budget survives it because of how Refresh works rather than by being cheap.
        /// Marks are only put back on the wall within a hundred and ten metres and dropped
        /// past a hundred and fifty, so the tags of a four-wall run are almost never all live
        /// at once -- what has to hold four walls is MarkCap, which is a list in memory, not
        /// the engine's decal pool.
        ///
        /// Settable from the ini because this is exactly the sort of number that wants trying
        /// in the game rather than reasoning about on a screenshot.
        /// </summary>
        public float TagSpacing = 0.11f;

        /// <summary>How wide and how tall the writing is on the wall, in metres.</summary>
        private const float TagWide = 2.60f;
        private const float TagTall = 1.15f;

        /// <summary>Where the bottom of the letters sits relative to the spot the can is at.</summary>
        private const float TagFoot = -0.55f;

        /// <summary>
        /// How big each mark is. Comfortably wider than the gap, so a stroke joins up.
        ///
        /// Bigger than the arithmetic suggests it needs to be, on purpose. The gap at this
        /// spacing is about thirteen centimetres and a mark is half a metre, which sounds like
        /// overkill until you remember the splatter art is holes with paint around them.
        /// </summary>
        public float TagDotSize = 0.50f;

        private void OurColour(out float r, out float g, out float b)
        {
            r = 0.24f; g = 0.86f; b = 0.32f;

            var gang = Crew == null ? null : Crew.Current;
            if (gang == null) return;

            r = gang.Colour.R / 255f;
            g = gang.Colour.G / 255f;
            b = gang.Colour.B / 255f;
        }

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

        /// <summary>
        /// The tag being written on this wall, and how much of it is already paint.
        ///
        /// Laid out once when the wall is found rather than per dot, because the layout is the
        /// same every tick and the only thing that moves is how far through it we are.
        /// </summary>
        private List<float[]> _tagPoints;
        private int _tagPlaced;
        private string _tagText = "";
        private bool _held;

        public TagRun(GangRegistry gangs)
        {
            _gangs = gangs;
        }

        public bool IsRunning { get; private set; }

        public bool ReadyToCollect { get; private set; }

        /// <summary>Walls crossed out over walls to cross out, for the card's bar.</summary>
        public float Advance =>
            _spots.Count == 0 ? 0f : Math.Min(1f, _done.Count / (float)_spots.Count);

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
                    Heading = node["heading"].AsFloat(),
                    Quiet = node["quiet"].AsBool()
                });
            }

            Log.Info("Tag spots loaded: " + spots.Count + ".");
            return spots;
        }

        // ---- starting ----------------------------------------------------------

        /// <summary>
        /// The walls we painted longest ago, with our paint taken back off them.
        ///
        /// ORDER IS THE CLOCK. _marks is appended to as he sprays and trimmed from the front
        /// at MarkCap, so walking it forwards is walking backwards in time -- which means the
        /// oldest work can be found without storing a timestamp on every dot, and without
        /// changing the save format for a list that is already several hundred entries long.
        ///
        /// Their paint comes off as they are picked, because that is the event: it is not that
        /// the wall became available, it is that somebody went over it. Leaving our marks up
        /// would put him in front of a wall that already says CGF and ask him to write CGF.
        /// </summary>
        private List<TagSpot> GoneOverAgain(List<TagSpot> all, int want)
        {
            var picked = new List<TagSpot>();

            for (var i = 0; i < _marks.Count && picked.Count < want; i++)
            {
                var spot = WallAt(_marks[i].At, all);
                if (spot == null || picked.Contains(spot)) continue;
                picked.Add(spot);
            }

            // Backwards, so removing does not shift anything still to be looked at.
            for (var i = _marks.Count - 1; i >= 0; i--)
            {
                var spot = WallAt(_marks[i].At, all);
                if (spot == null || !picked.Contains(spot)) continue;

                Wipe(_marks[i]);
                _marks.RemoveAt(i);
            }

            return picked;
        }

        /// <summary>Which wall a mark belongs to, or null if it is not near one any more.</summary>
        private static TagSpot WallAt(Vector3 at, List<TagSpot> all)
        {
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && at.DistanceTo(all[i].Where) <= PaintedWithin) return all[i];
            }

            return null;
        }

        /// <summary>Returns a player-facing refusal, or null once the run is on.</summary>
        public string Start(MissionDef def, List<TagSpot> all)
        {
            if (all == null || all.Count == 0) return "Nobody could tell you where they're at.";

            _spots.Clear();
            _done.Clear();

            // A new run gets its own trouble. See _troubleSpent.
            _troubleSpent = false;

            // However many the job asks for, drawn at random from the walls that are still
            // somebody else's, so running it again is not the same afternoon twice.
            var pool = new List<TagSpot>();

            for (var i = 0; i < all.Count; i++)
            {
                if (AlreadyOurs(all[i])) continue;
                pool.Add(all[i]);
            }

            // EVERY WALL IS OURS -- so somebody goes back over the oldest ones.
            //
            // This used to be the end of the job forever: twenty walls, four a run, five runs
            // and Lamar had nothing left to offer until the graffiti was wiped from a settings
            // menu. A mission that can only be replayed by using a debug switch is not
            // repeatable, and the fiction already had the answer in Lamar's own line about the
            // busta being out again. He IS out again. He goes over the paint that has been up
            // longest, and those walls come back on the list.
            if (pool.Count == 0)
            {
                pool = GoneOverAgain(all, Math.Max(1, def.Targets));

                if (pool.Count == 0) return "Every wall on that list is already ours.";

                Log.Info("Tag list was dry; " + pool.Count + " of the oldest walls got done over.");
            }

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

            // A new run has not failed yet. Without this a stale reason would fail the next
            // one the instant MissionRunner read it.
            Failure = null;

            _playerBike = SpawnBike(BikeSpot, BikeHeading);
            if (_playerBike == null)
            {
                IsRunning = false;
                return "Ain't no bike out there.";
            }

            Mark(_playerBike, "Your bike");

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
                // NO BIKE, NO JOB -- and it has to SAY so.
                //
                // This used to return, every tick, forever. The objective stayed on "Get on the
                // bike", the run never became collectable, and Fixer would only say "you
                // already got something on" -- so shoving the bike into the water at the lot
                // left a live mission that could be neither finished nor cancelled, with dying
                // or being arrested as the only exits. The bike ride handles exactly this case
                // and says "Somebody took the bike"; this file had no way to say anything at
                // all, because it had no Failure channel and nothing was reading one.
                if (_playerBike == null || !_playerBike.Exists())
                {
                    Failure = "Somebody took the bike.";
                    return;
                }

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

            if (_spraying == null) { _cardAt = 0; _bar = 0f; return; }

            SprayCard();
        }

        // ---- the can panel -----------------------------------------------------

        private const float SprayCardWidth = 0.250f;
        private const float SprayCardTop = 0.782f;
        private const float SprayCardHeight = 0.062f;
        private const float SprayCardPad = 0.008f;
        private const float SprayCardRail = 0.0022f;
        private const float SprayIconSize = 0.030f;
        private const float SprayBarHeight = 0.0050f;

        /// <summary>How long the panel takes to arrive, and how far it rises on the way.</summary>
        private const int SprayEnterMs = 180;
        private const float SprayEnterRise = 0.014f;

        /// <summary>How fast the fill catches the figure, and how fast the light travels it.</summary>
        private const float SprayBarRate = 0.16f;
        private const int SprayBarSweepMs = 1500;

        /// <summary>The corner ticks, which are the frame without drawing a whole box.</summary>
        private const float TickLong = 0.020f;
        private const float TickThick = 0.0022f;

        private static readonly System.Drawing.Color SprayBack =
            System.Drawing.Color.FromArgb(232, 12, 13, 15);

        private int _cardAt;
        private float _bar;

        /// <summary>
        /// The one thing in this job that asks you to stand still, drawn like it matters.
        ///
        /// It was a bare grey trough with the word PAINTING over it -- which told you the
        /// truth and nothing else. This is the same information wearing the panel every other
        /// readout in the mod wears: it rises in, it is framed, it fills smoothly, and it is
        /// painted in YOUR set's colour rather than a hardcoded green, because the whole point
        /// of the eight seconds is whose colour ends up on the wall.
        ///
        /// It also says which half of those eight seconds you are in. The clip opens with him
        /// shaking the can and only starts laying down paint four seconds later, and a bar that
        /// calls all of it "painting" is describing something that has not started yet.
        /// </summary>
        private void SprayCard()
        {
            if (_cardAt == 0) _cardAt = Game.GameTime;

            var age = Game.GameTime - _cardAt;
            var enter = age >= SprayEnterMs ? 1f : age / (float)SprayEnterMs;
            var eased = 1f - (1f - enter) * (1f - enter);

            var top = SprayCardTop + SprayEnterRise * (1f - eased);
            var left = 0.5f - SprayCardWidth * 0.5f;
            var right = left + SprayCardWidth;

            OurColour(out var r, out var g, out var b);

            var ink = Tint(System.Drawing.Color.FromArgb(255, (int)(r * 255f), (int)(g * 255f),
                                                         (int)(b * 255f)), eased);

            // ---- the panel ----
            Hud.RectFrom(left, top, SprayCardWidth, SprayCardHeight, Tint(SprayBack, eased));
            Hud.RectFrom(left, top, SprayCardRail, SprayCardHeight, ink);
            Hud.RectFrom(left, top, SprayCardWidth, 0.0022f, ink);

            Corners(left, top, right, top + SprayCardHeight, ink);

            // ---- the can ----
            var iconLeft = left + SprayCardRail + SprayCardPad;
            var iconWide = Hud.ToX(SprayIconSize);

            Hud.RectFrom(iconLeft, top + (SprayCardHeight - SprayIconSize) * 0.5f - 0.006f,
                         iconWide, SprayIconSize,
                         System.Drawing.Color.FromArgb((int)(20 * eased), 255, 255, 255));

            // The can jitters while he is shaking it and holds still once he is painting,
            // which is the cheapest possible way to say which half you are in.
            var shaking = Game.GameTime - _sprayingSince < PaintDelayMs;
            var jitter = shaking ? (float)Math.Sin(Game.GameTime * 0.045d) * 0.0018f : 0f;

            Hud.File("spray.png", iconLeft + iconWide * 0.5f + jitter,
                     top + SprayCardHeight * 0.5f - 0.006f, SprayIconSize * 0.66f, 0f, ink);

            var x = iconLeft + iconWide + SprayCardPad;

            Hud.Text(shaking ? "SHAKING THE CAN" : "GOING OVER IT",
                     x, top + 0.008f, 0.29f, Tint(Palette.Text, eased),
                     Hud.FontLabel, centre: false);

            var whose = _gangs == null ? null : _gangs.Get(_spraying.Gang);

            Hud.Text(whose == null ? "their tag" : whose.Name.ToUpperInvariant(),
                     x, top + 0.027f, 0.24f, Tint(Palette.TextDim, eased),
                     Hud.FontBody, centre: false);

            Hud.TextRight((_done.Count + 1) + " OF " + _spots.Count,
                          right - SprayCardPad, top + 0.010f, 0.23f, ink, Hud.FontLabel);

            // ---- the bar ----
            var barLeft = x;
            var barWide = right - SprayCardPad - barLeft;
            var barY = top + SprayCardHeight - 0.011f;

            Hud.RectFrom(barLeft, barY, barWide, SprayBarHeight,
                         System.Drawing.Color.FromArgb((int)(46 * eased), 255, 255, 255));

            var done = Math.Min(1f, (Game.GameTime - _sprayingSince) / (float)SprayMs);

            _bar += (done - _bar) * SprayBarRate;
            if (Math.Abs(done - _bar) < 0.002f) _bar = done;

            if (_bar <= 0f) return;

            var lit = barWide * _bar;
            Hud.RectFrom(barLeft, barY, lit, SprayBarHeight, ink);

            // A light travelling up the filled part only. On the empty track it would be the
            // panel promising progress it has not made.
            var t = (Game.GameTime % SprayBarSweepMs) / (float)SprayBarSweepMs;
            var band = Math.Min(lit, barWide * 0.12f);
            var at = barLeft - band + (lit + band) * t;

            var lo = Math.Max(barLeft, at);
            var hi = Math.Min(barLeft + lit, at + band);

            if (hi > lo)
            {
                Hud.RectFrom(lo, barY, hi - lo, SprayBarHeight,
                             System.Drawing.Color.FromArgb((int)(130 * eased), 255, 255, 255));
            }
        }

        /// <summary>
        /// Four corner ticks instead of a box.
        ///
        /// A full outline round a small panel reads as a dialog and fights the backing; the
        /// ticks give it the same framed, deliberate look for a fraction of the ink. Same idea
        /// the wheel and the socials panel use, so it belongs to the same set of screens.
        /// </summary>
        private static void Corners(float left, float top, float right, float bottom,
                                    System.Drawing.Color ink)
        {
            var wide = Hud.ToX(TickThick);
            var run = Hud.ToX(TickLong);

            // top left
            Hud.RectFrom(left, top, run, TickThick, ink);
            Hud.RectFrom(left, top, wide, TickLong, ink);

            // top right
            Hud.RectFrom(right - run, top, run, TickThick, ink);
            Hud.RectFrom(right - wide, top, wide, TickLong, ink);

            // bottom left
            Hud.RectFrom(left, bottom - TickThick, run, TickThick, ink);
            Hud.RectFrom(left, bottom - TickLong, wide, TickLong, ink);

            // bottom right
            Hud.RectFrom(right - run, bottom - TickThick, run, TickThick, ink);
            Hud.RectFrom(right - wide, bottom - TickLong, wide, TickLong, ink);
        }

        private static System.Drawing.Color Tint(System.Drawing.Color c, float by)
        {
            if (by >= 0.999f) return c;
            return System.Drawing.Color.FromArgb((int)(c.A * by), c.R, c.G, c.B);
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
        /// <summary>Metallic dark green, the same index every other car of theirs uses.</summary>
        private const int BikeGreen = 49;

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

                    // The set's green, out of the game's own paint table rather than an RGB --
                    // the flake is in the table and a bicycle in flat green is a toy. Yours and
                    // his match, because they came out of the same yard.
                    try
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD_KIT, bike.Handle, 0);
                        Function.Call(Hash.SET_VEHICLE_LIVERY, bike.Handle, -1);
                        Function.Call(Hash.SET_VEHICLE_MOD, bike.Handle, 48, -1, false);

                        Function.Call(Hash.SET_VEHICLE_COLOURS, bike.Handle, BikeGreen, BikeGreen);
                        Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, bike.Handle, BikeGreen, 0);
                    }
                    catch { /* it rides the same in whatever colour it came in */ }

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
                Dress(name);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the next leg: " + ex.Message);
            }
        }

        /// <summary>
        /// The same, on a thing that moves.
        ///
        /// A blip on a COORDINATE is right for a wall and wrong for a bike. It marked where the
        /// bike was parked, so the moment you rode off, the arrow stayed behind in the lot
        /// pointing at an empty kerb -- and if you dumped the bike somewhere on the way there
        /// was nothing at all telling you where it went.
        /// </summary>
        private void Mark(Entity what, string name)
        {
            ClearMarker();

            if (what == null || !what.Exists()) return;

            try
            {
                _marker = what.AddBlip();
                Dress(name);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the bike: " + ex.Message);
            }
        }

        private void Dress(string name)
        {
            if (_marker == null || !_marker.Exists()) return;

            _marker.Color = BlipColor.Yellow;
            _marker.ShowRoute = true;
            _marker.Name = name;
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

            // A fresh wall gets a fresh tag, and nothing of the last one is owed.
            _tagText = TagToWrite();
            _tagPoints = TagLetters.CanWrite(_tagText)
                ? TagLetters.Layout(_tagText, TagSpacing)
                : null;
            _tagPlaced = 0;

            if (_tagPoints != null)
            {
                Log.Info("Writing " + _tagText + " on this one -- " + _tagPoints.Count +
                         " marks.");
            }
            else
            {
                Log.Info("No letters for '" + _tagText + "'; this one gets splatter.");
            }

            // A different wall, so the last one's surface is forgotten rather than painted on
            // from four streets away.
            ForgetWall();

            // And the panel arrives rather than being already there.
            _cardAt = 0;
            _bar = 0f;

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

            // Their block, and you are stood on it with your back to the road -- unless it is
            // ours, in which case it is just a wall and nobody is coming.
            if (!_troubleSpent && !spot.Quiet && _rng.NextDouble() < TroubleChance)
            {
                SpawnTrouble(player, spot);
            }
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
            Stain(player);

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
        /// The jet coming out of the can.
        ///
        /// This doc used to say the opposite -- that the method was deliberately empty and the
        /// animation was the whole effect. That was true for about a day. The plume came back
        /// once it was aimed off the PED rather than off the hand bone, which is the change
        /// that made it look like paint instead of like fog coming off his wrist, and nobody
        /// came back to the comment above it.
        ///
        /// It starts four seconds in and it is a colourless jet tinted by us -- see the colour
        /// call below.
        /// </summary>
        private void Paint(Ped player)
        {
            if (_paintFx != -1) return;
            if (_spraying == null) return;

            // Four seconds in, not straight away.
            //
            // The clip opens with him shaking the can and reaching up, and paint coming out
            // while his arm is still on the way to the wall reads as a leak. By four seconds he
            // is up against it and moving, which is when a can would actually be laying down
            // colour -- and it leaves the back half of the eight seconds spraying, which is the
            // half anybody is looking at.
            if (Game.GameTime - _sprayingSince < PaintDelayMs) return;

            var patient = Game.GameTime - _sprayingSince < PaintDelayMs + JetGraceMs;

            try
            {
                foreach (var jet in PaintJets)
                {
                    // Nothing to hang it off. The can is optional -- an install without the
                    // prop still gets the job, it just gets it without a can in shot -- so a
                    // jet that needs one steps aside rather than failing.
                    if (jet.OnCan && (_can == null || !_can.Exists())) continue;

                    // A named PTFX asset streams in like an anim dict does: the request
                    // returns immediately and the file lands some frames later, so asking
                    // whether it is loaded on the line after asking for it can only be
                    // answered no.
                    if (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, jet.Asset))
                    {
                        Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, jet.Asset);

                        // Hold the queue rather than letting the standby past. See JetGraceMs.
                        if (patient) return;
                        continue;
                    }

                    // Has to be re-declared before every start, not once at load: the call sets
                    // which asset the NEXT start reads from and the game resets it constantly.
                    Function.Call(Hash.USE_PARTICLE_FX_ASSET, jet.Asset);

                    // On the CAN for Rockstar's, which is where they hang it and what it is
                    // authored around. Otherwise on the PED rather than on the hand bone: a
                    // bone's local axes are its own and point wherever the skeleton happens to
                    // face, so aiming a jet off one is guesswork, and a ped's are not -- +Y is
                    // the way he is looking, which during this clip is the wall.
                    var on = jet.OnCan ? _can.Handle : player.Handle;

                    var fx = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY,
                                                jet.Effect, on,
                                                jet.X, jet.Y, jet.Z,
                                                jet.Pitch, 0f, 0f,
                                                jet.Scale, false, false, false);

                    if (fx == 0 || !Function.Call<bool>(Hash.DOES_PARTICLE_FX_LOOPED_EXIST, fx))
                    {
                        continue;
                    }

                    _paintFx = fx;

                    // The effect itself is a colourless jet -- steam or water -- so the colour
                    // is entirely this call, and without it the can appears to spray nothing at
                    // all. Green for the Families, and whatever the set is if it is ever another.
                    OurColour(out var pr, out var pg, out var pb);
                    Function.Call(Hash.SET_PARTICLE_FX_LOOPED_COLOUR, fx, pr, pg, pb, false);
                    Function.Call(Hash.SET_PARTICLE_FX_LOOPED_ALPHA, fx, 0.85f);

                    Log.Info("Tag paint: " + jet + " started.");
                    return;
                }

                // Still inside the grace with everything either unstreamed or unusable, so
                // there is nothing to report yet -- come back next tick.
                if (patient) return;

                Log.Debug("Tag paint: none of the effects would start; painting stays silent.");

                // Do not try again every frame for the rest of the spray. -2 is "asked and
                // answered", and StopPaint treats anything that is not a live handle as
                // nothing to stop.
                _paintFx = -2;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not start the paint: " + ex.Message);
                _paintFx = -2;
            }
        }

        /// <summary>
        /// How long into the clip the can starts laying down colour.
        ///
        /// It used to be half of SprayMs and is deliberately no longer tied to it. Shaking the
        /// can and reaching up takes as long as it takes -- it did not get slower because the
        /// job got longer -- so this stays at four seconds and the whole of the extra three
        /// goes where it was wanted, on the paint. Held at half it would have bought three more
        /// seconds of a man rattling a can and only a second and a half of colour.
        /// </summary>
        private const int PaintDelayMs = 4000;

        /// <summary>
        /// Where the jet starts, in the player's own space: right hand, arm's length, chest
        /// height. +Y is the way he is facing, which during this clip is the wall.
        /// </summary>
        private const float PaintRight = 0.20f;
        private const float PaintForward = 0.42f;
        private const float PaintUp = 0.48f;

        /// <summary>
        /// How far the jet is tipped over from its own resting direction, in degrees.
        ///
        /// A jet effect points along its own axis and which axis that is varies per effect --
        /// steam rises, a hose sprays out. -90 tips a rising one flat, so it goes the way the
        /// player is facing, which during this clip is the wall. This is the ONE number to
        /// change if it comes out of the can pointing at the floor or the sky.
        /// </summary>
        private const float PaintPitch = -90f;

        private const float PaintScale = 0.55f;

        /// <summary>One way of getting paint out of the can, with where to hang it.</summary>
        private sealed class PaintJet
        {
            public string Asset = "";
            public string Effect = "";

            /// <summary>Attached to the can rather than to the man holding it.</summary>
            public bool OnCan;

            public float X, Y, Z;
            public float Pitch;
            public float Scale = 1f;

            public override string ToString() => Asset + " / " + Effect;
        }

        /// <summary>
        /// Candidates, tried in order, first one that actually starts wins.
        ///
        /// The first is Rockstar's, and it is not a lookalike -- `scr_lamgraff_paint_spray`
        /// out of `scr_playerlamgraff` is the effect from the Lamar graffiti scene, which is
        /// the exact thing this job is. It is authored as paint coming out of a nozzle, and
        /// Rockstar's own script colours it with SET_PARTICLE_FX_LOOPED_COLOUR, so the tint we
        /// have always been applying is one it is known to accept.
        ///
        /// It hangs off the CAN at zero offset and zero rotation, which is how R* hangs it.
        /// That works regardless of which bone our can is on or how it was rotated to get it
        /// sitting right, because a particle effect attached to an entity inherits that
        /// entity's world transform -- so if the can looks correctly held, an effect authored
        /// for that can sprays correctly out of it.
        ///
        /// Everything under it is what the job shipped with: a colourless water or steam jet
        /// out of "core", hung off the PED and tipped flat, tinted green to look like paint. It
        /// is kept because "core" is always resident and cannot fail to stream, which makes it
        /// the thing that is definitely there if the graffiti asset is not.
        ///
        /// A PTFX name that does not exist fails SILENTLY, returning a handle for an effect
        /// that is not there -- so the handle is checked rather than assumed, and the one that
        /// took is logged. That log line is the only way to find out from outside the game
        /// which of these the build actually has.
        /// </summary>
        private static readonly PaintJet[] PaintJets =
        {
            new PaintJet
            {
                Asset = "scr_playerlamgraff", Effect = "scr_lamgraff_paint_spray",
                OnCan = true, Scale = 1f
            },

            new PaintJet
            {
                Asset = PaintAsset, Effect = PaintEffect,
                X = PaintRight, Y = PaintForward, Z = PaintUp,
                Pitch = PaintPitch, Scale = PaintScale
            },

            new PaintJet
            {
                Asset = PaintAsset, Effect = "ent_sht_water",
                X = PaintRight, Y = PaintForward, Z = PaintUp,
                Pitch = PaintPitch, Scale = PaintScale
            },

            new PaintJet
            {
                Asset = PaintAsset, Effect = "ent_sht_extinguisher",
                X = PaintRight, Y = PaintForward, Z = PaintUp,
                Pitch = PaintPitch, Scale = PaintScale
            }
        };

        /// <summary>
        /// How long to hold out for a jet whose asset is still streaming.
        ///
        /// Without this the standby wins every time and the good one never gets a look in:
        /// "core" is always resident, so the moment the graffiti asset is a frame late the
        /// loop falls straight past it to something that is ready now. Under the grace the
        /// loop WAITS at the first thing that has not landed; after it, it takes whatever will
        /// start. Preload asks for the asset from the moment the job begins, so in practice
        /// this has already elapsed by the time anybody is stood at a wall.
        /// </summary>
        private const int JetGraceMs = 1400;

        /// <summary>Turns the paint off. Called from every path out of painting.</summary>
        private void StopPaint()
        {
            // -1 is "never started", -2 is "tried and could not". Neither is a handle, and
            // telling the game to stop one is at best a no-op and at worst a stop aimed at
            // somebody else's effect.
            if (_paintFx < 0)
            {
                _paintFx = -1;
                return;
            }

            try
            {
                Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, _paintFx, false);
                Function.Call(Hash.REMOVE_PARTICLE_FX, _paintFx, false);
            }
            catch { /* it will time out on its own */ }

            _paintFx = -1;
        }

        // ---- and the mark it leaves --------------------------------------------

        /// <summary>
        /// Decal render settings, tried in order, first one the game accepts wins.
        ///
        /// 1030 is splatters_paint and it is the right answer: a paint splatter, authored pale
        /// enough to take a colour, and the one a paint-gun menu reaches for when it wants an
        /// arbitrary RGB on a wall.
        ///
        /// The two behind it are blood, and they are a WORSE fallback than they look. The
        /// colour arguments are MULTIPLIERS over the source texture rather than a replacement
        /// -- Rockstar's own scripts pass 0.196, 0, 0 to splatters_blood2 to darken it -- so
        /// green over red comes out near black. A dark splat on a garage door still reads as
        /// somebody having done something to it, which beats a wall that did not change at
        /// all, but it is not the picture and it says so in the log when it happens.
        ///
        /// ADD_DECAL returns 0 when it will not place, so which of these the install actually
        /// has is a question the game answers rather than one this file assumes.
        /// </summary>
        private static readonly int[] PaintDecals = { 1030, 1110, 1010 };

        /// <summary>Which of them took, so every mark after the first costs no failed calls.</summary>
        private int _decalType;

        /// <summary>
        /// How many times in a row nothing would place before this stops asking.
        ///
        /// Not one. The decal budget is five hundred and twelve for the WHOLE world and every
        /// bullet hole and tyre mark competes for it, so a single refusal in the middle of a
        /// firefight says nothing about whether the type works -- it says the budget was full
        /// for a moment. Three in a row on three different frames is a real answer.
        /// </summary>
        private const int StainGiveUpAfter = 3;

        private int _stainMisses;

        /// <summary>How far in front of him to look for the wall.</summary>
        private const float WallReach = 2.6f;

        /// <summary>Chest height, which is where the can is and where the tag is.</summary>
        private const float WallEye = 1.1f;

        /// <summary>Sat just off the surface, so the projection runs into it rather than past it.</summary>
        private const float WallLift = 0.05f;

        /// <summary>
        /// A mark roughly this often.
        ///
        /// Under the 300ms tick this works out at one per tick, so the four spraying seconds
        /// lay down a dozen or so rather than six. Paint should arrive faster than you can
        /// count it.
        /// </summary>
        private const int StainEveryMs = 280;

        /// <summary>How far a mark strays from the middle, along the wall and up it.</summary>
        private const float StainSpreadSide = 0.90f;
        private const float StainSpreadUp = 0.50f;

        private const float StainMinSize = 0.50f;
        private const float StainMaxSize = 1.20f;

        /// <summary>
        /// How high up the wall the paint goes, measured from the ground he is stood on.
        ///
        /// NOT from the raycast's own hit height, which is where the first attempt put it and
        /// why the paint came out along the roofline instead of over the tag. The ray is fired
        /// from chest height to find the wall, and the height it happens to strike at is a fact
        /// about the ray rather than about where a tag is -- so the hit gives the wall its
        /// PLANE and its horizontal position, and the height is decided here.
        ///
        /// A metre and a third up, spreading half a metre either way, covers roughly what a
        /// person's arm covers standing at a wall, which is where anybody's tag is.
        /// </summary>
        private const float TagHeight = 1.35f;

        /// <summary>Seconds. -1 is no expiry clock -- the game's decal budget is the only limit.</summary>
        private const float StainForever = -1f;

        /// <summary>One mark, and everything needed to put it back exactly where it was.</summary>
        private sealed class PaintMark
        {
            public Vector3 At;
            public Vector3 Into;
            public Vector3 Side;
            public float Size;
            public float R, G, B;

            /// <summary>The last handle the game gave it, so a stale one can be cleaned up.</summary>
            public int Handle;

            /// <summary>True once he has been far enough away for the game to have dropped it.</summary>
            public bool Away;
        }

        /// <summary>
        /// Every mark painted this session, so the wall stays yours.
        ///
        /// Decals do not survive a stream-out. Ride to the next wall and the first one's paint
        /// is quietly gone by the time you come back past it -- which turns "that's ours now"
        /// into something that was true for as long as you were stood in front of it. The mod
        /// that does this for a living re-adds every one of its tags on a timer for exactly
        /// this reason.
        ///
        /// Deliberately NOT cleared by Clear(). The job ending is the point at which the paint
        /// starts mattering, not the point at which it stops.
        /// </summary>
        private readonly List<PaintMark> _marks = new List<PaintMark>();

        /// <summary>
        /// How many are kept. A dozen a wall, four walls, and room to go round again.
        ///
        /// The whole world shares a budget of five hundred and twelve decals with every bullet
        /// hole and tyre mark in it, so this stays well clear of being the thing that fills it.
        /// </summary>
        private const int MarkCap = 640;

        /// <summary>Far enough for the game to have dropped it.</summary>
        private const float MarkGoneRange = 150f;

        /// <summary>And near enough to want it back. Under Gone, so the two do not flap.</summary>
        private const float MarkBackRange = 110f;

        private const int MarkCheckMs = 2000;

        /// <summary>When the last sweep ran, not when the next one is due.</summary>
        private int _lastMarkCheck;

        private bool _wallFound;
        private Vector3 _wallAt;
        private Vector3 _wallInto;
        private Vector3 _wallSide;
        private Vector3 _wallUp;
        private int _nextStain;
        private int _stains;

        /// <summary>Forgets the wall, so the next spot is found fresh rather than painted over.</summary>
        private void ForgetWall()
        {
            _wallFound = false;
            _nextStain = 0;
            _stains = 0;
        }

        /// <summary>
        /// Finds the surface he is stood in front of, once per wall.
        ///
        /// A raycast rather than a coordinate in tags.json, because a decal has to lie FLAT and
        /// nothing in that file knows which way the wall faces -- only which way the player
        /// does. The ray hands back the exact point and the surface normal, which is the whole
        /// of what placing a decal needs, and it is right whether the surface is a garage door,
        /// a billboard or a fence.
        /// </summary>
        private bool FindWall(Ped player)
        {
            if (_wallFound) return true;

            try
            {
                var from = player.Position + Vector3.WorldUp * WallEye;

                var hit = World.Raycast(from, player.ForwardVector, WallReach,
                                        IntersectFlags.Map | IntersectFlags.Objects, player);

                if (!hit.DidHit) return false;

                var n = hit.SurfaceNormal;
                if (n.Length() < 0.5f) return false;

                n = n.Normalized;

                // Two axes lying IN the wall. Both come out of a cross product with the normal,
                // so both are square to it by construction -- which is the one thing that has to
                // be true, because a decal whose side vector is not perpendicular to its
                // direction renders flipped, rotated, or not at all.
                var side = Vector3.Cross(Vector3.WorldUp, n);

                // Unless the surface is a floor or a ceiling, where "up along the wall" means
                // nothing and that cross product collapses to zero.
                if (side.Length() < 0.05f) side = Vector3.Cross(player.RightVector, n);
                if (side.Length() < 0.05f) return false;

                _wallSide = side.Normalized;
                _wallUp = Vector3.Cross(n, _wallSide).Normalized;

                // The projection runs INTO the surface, so it is the normal reversed.
                _wallInto = -n;

                var at = hit.HitPosition + n * WallLift;

                // And the height is taken off the GROUND rather than off the ray. See TagHeight.
                var ground = Ground(player.Position);
                if (ground.Z > 0.01f) at.Z = ground.Z + TagHeight;

                _wallAt = at;

                _wallFound = true;

                Log.Debug("Tag wall found, normal " + n.ToString() + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not find the tag wall: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Puts the paint on the wall, a bit at a time.
        ///
        /// This class used to say a script cannot change a wall, and for ARTWORK that is true:
        /// a picture is a texture asset and this mod is a DLL and some JSON. A decal is not
        /// artwork though. It is a mark the engine projects onto whatever is behind it, in a
        /// colour we hand it -- and going over somebody else's tag is a great deal closer to
        /// leaving a mark than it is to painting a picture.
        ///
        /// Several small ones rather than one big one, laid down as the bar fills. A single
        /// splat appearing the instant the job completes is something that happened TO the wall
        /// after you finished; a patch that grows while the can is hissing is the thing you are
        /// doing. It costs six decals out of a budget of five hundred and twelve.
        /// </summary>
        private void Stain(Ped player)
        {
            if (_spraying == null) return;
            if (_stainMisses >= StainGiveUpAfter) return;

            // The same four seconds the jet waits for. Paint arriving before the can is up
            // against the wall reads as a leak.
            var now = Game.GameTime;
            if (now - _sprayingSince < PaintDelayMs) return;
            if (now < _nextStain) return;

            if (!FindWall(player)) return;

            _nextStain = now + StainEveryMs;

            OurColour(out var r, out var g, out var b);

            // WRITING, if this wall has letters to write. Everything below is the old random
            // splatter, kept whole as the fallback for a tag nobody has drawn.
            if (_tagPoints != null && _tagPoints.Count > 0)
            {
                Write(now, r, g, b);
                return;
            }

            // Somewhere in a patch about the size of a tag, at its own angle and its own size,
            // so six of them read as paint rather than as six copies of one sticker.
            var du = ((float)_rng.NextDouble() * 2f - 1f) * StainSpreadSide;
            var dv = ((float)_rng.NextDouble() * 2f - 1f) * StainSpreadUp;

            var at = _wallAt + _wallSide * du + _wallUp * dv;

            var roll = (float)(_rng.NextDouble() * Math.PI * 2d);
            var side = _wallSide * (float)Math.Cos(roll) + _wallUp * (float)Math.Sin(roll);

            var size = StainMinSize + (float)_rng.NextDouble() * (StainMaxSize - StainMinSize);

            foreach (var type in PaintDecals)
            {
                // Once one of them has taken, it is the only one worth asking again.
                if (_decalType > 0 && type != _decalType) continue;

                int handle;

                try
                {
                    handle = Function.Call<int>(Hash.ADD_DECAL, type,
                                                at.X, at.Y, at.Z,
                                                _wallInto.X, _wallInto.Y, _wallInto.Z,
                                                side.X, side.Y, side.Z,
                                                size, size,
                                                r, g, b, 0.92f,
                                                StainForever, true, false, false);
                }
                catch (Exception ex)
                {
                    Log.Debug("Decal type " + type + " threw: " + ex.Message);
                    continue;
                }

                if (handle == 0) continue;

                if (_decalType != type)
                {
                    _decalType = type;

                    Log.Info("Tag paint lands as decal type " + type +
                             (type == PaintDecals[0]
                                 ? " (splatters_paint -- takes the colour properly)."
                                 : " (a blood splatter -- the tint multiplies, so it reads dark)."));
                }

                _stains++;
                _stainMisses = 0;

                Remember(new PaintMark
                {
                    At = at, Into = _wallInto, Side = side, Size = size,
                    R = r, G = g, B = b, Handle = handle
                });

                return;
            }

            // Nothing placed this time. Saying so is worth a great deal more than a wall that
            // quietly does not change, and the job itself is untouched by it: the animation,
            // the jet and the objective all still work.
            _stainMisses++;

            if (_stainMisses >= StainGiveUpAfter)
            {
                Log.Warn("No decal would place after " + StainGiveUpAfter +
                         " tries; the tag stays theirs on screen.");
            }
        }

        /// <summary>
        /// Set by Main, through MissionRunner: marks the save dirty when the wall changes.
        ///
        /// The save only writes when something says it has changed, and paint going up is a
        /// change. Without this a tag laid after the last sale would be gone on the next load,
        /// which is the whole thing this was for.
        /// </summary>
        public Action Changed;

        private void Remember(PaintMark mark)
        {
            _marks.Add(mark);

            // Oldest out first, and taken off the wall rather than just forgotten -- a handle
            // dropped from this list is one nothing can ever clean up again.
            while (_marks.Count > MarkCap)
            {
                Wipe(_marks[0]);
                _marks.RemoveAt(0);
            }

            if (Changed != null) Changed();
        }

        // ---- the save ---------------------------------------------------------

        /// <summary>
        /// Every mark on every wall, so the block still says what you said last night.
        ///
        /// Decals do not survive a stream-out, which Refresh already handles by putting them
        /// back when you come near. They do not survive a RESTART either, and nothing handled
        /// that -- so an evening spent crossing out the Ballas was gone the next time the game
        /// opened, and the one thing the job is FOR is that the wall stays yours.
        ///
        /// The handle is deliberately not written. It is this session's number for a decal the
        /// game has already forgotten by the time anybody reads this back; what is worth
        /// keeping is where the paint went, which way the wall faces, how big it was and what
        /// colour. Refresh puts it up again from exactly those.
        ///
        /// The decal TYPE is kept with them and that is not decoration. Which of the three
        /// candidates works is discovered by probing on the first tag of a session -- so a save
        /// full of paint loaded on a fresh session has no idea how to draw any of it until you
        /// happen to spray something new. Writing down the answer means it comes back knowing.
        /// </summary>
        public Json ToJson()
        {
            var doc = Json.Object();

            doc.Set("decal", _decalType);

            var arr = Json.Array();

            for (var i = 0; i < _marks.Count; i++)
            {
                var m = _marks[i];

                arr.Add(Json.Object()
                    .Set("x", m.At.X).Set("y", m.At.Y).Set("z", m.At.Z)
                    .Set("ix", m.Into.X).Set("iy", m.Into.Y).Set("iz", m.Into.Z)
                    .Set("sx", m.Side.X).Set("sy", m.Side.Y).Set("sz", m.Side.Z)
                    .Set("size", m.Size)
                    .Set("r", m.R).Set("g", m.G).Set("b", m.B));
            }

            doc.Set("marks", arr);
            return doc;
        }

        public void LoadFrom(Json node)
        {
            if (node == null || node.IsNull) return;

            try
            {
                var type = node["decal"].AsInt(0);
                if (type > 0) _decalType = type;

                foreach (var item in node["marks"].Items)
                {
                    _marks.Add(new PaintMark
                    {
                        At = new Vector3(item["x"].AsFloat(0f), item["y"].AsFloat(0f),
                                         item["z"].AsFloat(0f)),
                        Into = new Vector3(item["ix"].AsFloat(0f), item["iy"].AsFloat(0f),
                                           item["iz"].AsFloat(0f)),
                        Side = new Vector3(item["sx"].AsFloat(0f), item["sy"].AsFloat(0f),
                                           item["sz"].AsFloat(0f)),
                        Size = item["size"].AsFloat(0.5f),
                        R = item["r"].AsFloat(1f),
                        G = item["g"].AsFloat(1f),
                        B = item["b"].AsFloat(1f),

                        // Nothing is on the wall yet. Away is what tells Refresh there is
                        // something owed here, and it is what puts every restored mark back up
                        // the first time you walk past it.
                        Handle = 0,
                        Away = true
                    });
                }

                while (_marks.Count > MarkCap) _marks.RemoveAt(0);

                Log.Info("Tags: " + _marks.Count + " mark(s) restored, decal type " +
                         _decalType + ".");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the tags back: " + ex.Message);
            }
        }

        private static void Wipe(PaintMark mark)
        {
            if (mark == null || mark.Handle == 0) return;

            try { Function.Call(Hash.REMOVE_DECAL, mark.Handle); }
            catch { /* it has already gone */ }

            mark.Handle = 0;
        }

        /// <summary>
        /// Puts the paint back on walls he is coming past again.
        ///
        /// Called every tick whether or not a job is running, because the whole point of the
        /// paint is what the block looks like AFTER the job. Range re-entry rather than a
        /// repeating timer: re-adding one that is still on the wall does not replace it, it
        /// stacks a second one on top, and doing that every twenty seconds would empty the
        /// world's decal budget into one garage door. The two ranges differ so a player stood
        /// on the boundary does not flap across it.
        /// </summary>
        public void Refresh()
        {
            if (_marks.Count == 0) return;
            if (_stainMisses >= StainGiveUpAfter) return;

            var now = Game.GameTime;
            if (now - _lastMarkCheck < MarkCheckMs) return;
            _lastMarkCheck = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            var where = player.Position;

            foreach (var mark in _marks)
            {
                var d = where.DistanceTo(mark.At);

                if (d > MarkGoneRange)
                {
                    // The handle is deliberately KEPT rather than zeroed. Being out of range
                    // is the reason to expect the game has dropped it, not proof that it has --
                    // and if it did survive, the only thing that could ever remove it is this
                    // handle. Throwing it away here is what would put a second mark on top of
                    // the first when he came back.
                    mark.Away = true;
                    continue;
                }

                if (!mark.Away || d > MarkBackRange) continue;

                // Belt and braces: if it somehow did survive, this stops a second going on
                // top of it.
                Wipe(mark);

                try
                {
                    mark.Handle = Function.Call<int>(Hash.ADD_DECAL, _decalType,
                                                     mark.At.X, mark.At.Y, mark.At.Z,
                                                     mark.Into.X, mark.Into.Y, mark.Into.Z,
                                                     mark.Side.X, mark.Side.Y, mark.Side.Z,
                                                     mark.Size, mark.Size,
                                                     mark.R, mark.G, mark.B, 0.92f,
                                                     StainForever, true, false, false);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a tag back: " + ex.Message);
                    continue;
                }

                if (mark.Handle != 0) mark.Away = false;
            }
        }

        /// <summary>
        /// How near a mark has to be to a wall to count as that wall being done.
        ///
        /// The letters are laid out a couple of metres wide from the spot Franklin stands on,
        /// with jitter on top, so a mark can sit a fair way from the spot itself. Generous
        /// enough to catch all of them and still tight enough that two walls a street apart
        /// are never confused -- the closest pair on the list is about eleven metres.
        /// </summary>
        private const float PaintedWithin = 6f;

        /// <summary>
        /// Whether this wall already has our paint on it.
        ///
        /// NOT called Painted -- that is already taken by the property meaning "every wall in
        /// this run is done", which is a different question about a different thing.
        ///
        /// Asked of the MARKS rather than of a list of finished wall ids, and that is the whole
        /// trick: the paint is already saved and restored, so it is the one record that cannot
        /// disagree with what is on the wall. A separate "walls done" flag would be a second
        /// source of truth for the same fact, and the first time somebody wiped the graffiti
        /// from the reset menu the two would part company -- clean walls the run still refused
        /// to offer.
        /// </summary>
        private bool AlreadyOurs(TagSpot spot)
        {
            if (spot == null) return false;

            for (var i = 0; i < _marks.Count; i++)
            {
                if (_marks[i].At.DistanceTo(spot.Where) <= PaintedWithin) return true;
            }

            return false;
        }

        /// <summary>How many marks are on the walls right now.</summary>
        public int PaintCount { get { return _marks.Count; } }

        /// <summary>
        /// Every tag gone, off the walls and out of the save.
        ///
        /// Different from the teardown wipe, which deliberately KEEPS the list so the save can
        /// record it -- this one is somebody asking for the walls back, so the record goes with
        /// the paint. Marked as a change so the empty list is what reaches the disk; without
        /// that the save would quietly put them all back on the next load, which is the exact
        /// opposite of what the button says.
        /// </summary>
        public void ForgetPaint()
        {
            var had = _marks.Count;

            foreach (var mark in _marks) Wipe(mark);

            _marks.Clear();

            if (Changed != null) Changed();

            Log.Info("Paint wiped: " + had + " mark(s) off the walls and out of the save.");
        }

        /// <summary>Takes every mark off the walls. Only for the mod shutting down.</summary>
        private void WipeAllMarks()
        {
            foreach (var mark in _marks) Wipe(mark);

            // THE PAINT COMES OFF THE WALL. THE LIST STAYS.
            //
            // It used to clear the list here too, and that quietly undid the whole point of
            // saving it: OnAborted restores the world and THEN writes the save, so an empty
            // list at this moment is an empty list on disk -- every clean unload wiping the
            // very thing it was about to record.
            //
            // Saving before the restore instead is not the answer either. DroppedBags returns
            // its contents to the stash during that same restore, and that IS a change worth
            // keeping, so moving the save earlier trades one loss for another.
            //
            // Removing the decals and keeping their description costs nothing. This object is
            // being thrown away either way; the only thing that reads the list after this is
            // the save, and the only thing that reads the save is the next session, which
            // wants exactly this.
            foreach (var mark in _marks)
            {
                mark.Handle = 0;
                mark.Away = true;
            }
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

                    // PH_R_Hand (28422), at zero offset and zero rotation.
                    //
                    // It was on SKEL_R_Hand (57005) with a hand-tuned offset and a -90 twist,
                    // and that is the wrong bone for this. SKEL_R_Hand is the WRIST JOINT --
                    // the thing the arm deforms around -- so a prop hung off it sits beside the
                    // hand rather than in the grip, and every clip that changes the grip pose
                    // moves it again. PH_R_Hand is a non-deforming helper bone the animators
                    // put there specifically to hang props on, which is why props attached to
                    // it need no offset at all.
                    //
                    // It also fixes the spray. Rockstar's graffiti jet is authored to come out
                    // of a can sitting on THIS bone at THIS rotation, so with the can twisted
                    // ninety degrees the paint came out sideways. Put the can where the
                    // animation expects it and the jet points at the wall on its own.
                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _can.Handle, player.Handle,
                                  Function.Call<int>(Hash.GET_PED_BONE_INDEX, player.Handle, 28422),
                                  0f, 0f, CanSeat, 0f, 0f, 0f,
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

        /// <summary>
        /// A centimetre up the bone, so it sits IN the grip rather than hanging off the bottom
        /// of it.
        ///
        /// Everything above about PH_R_Hand needing no offset is still true and is why this is
        /// one number rather than a hand-tuned vector with a twist in it: the bone is right,
        /// the rotation is right, the jet comes out of the nozzle at the wall. The can just sat
        /// a little low in the fist, which reads as a prop resting on a hand rather than one
        /// being held.
        ///
        /// Small on purpose. This is a seating tweak by eye, not a correction to the rig -- go
        /// much past a centimetre or two and it stops being held and starts floating above the
        /// hand instead, which is the same fault the other way up.
        ///
        /// Z is up the bone. If it has gone the wrong way it is this sign, and nothing else.
        /// </summary>
        private const float CanSeat = 0.012f;

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

                // And the paint, for exactly the same reason. Asked for while he is still
                // riding, so the graffiti asset is resident by the time he is stood at a wall
                // and the grace period in Paint never has to be spent.
                foreach (var jet in PaintJets) Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, jet.Asset);
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
                // One with a bat, one with his hands. Alternating rather than rolling for it,
                // because with two of them a coin flip lands on two empty-handed men often
                // enough to matter, and one of each is the picture every time.
                var ped = SpawnMember(gang, spot.Where.Around(TroubleSpread), i % 2 == 0);
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

            if (_trouble.Count > 0)
            {
                // Spent whether or not both of them made it out of the model request. Two men
                // is what the block had in it; one that failed to spawn is not a reason to roll
                // again at the next wall.
                _troubleSpent = true;

                Notify.Problem("somebody saw you.");
            }
        }

        /// <summary>
        /// Lays down however much of the tag the can has got through by now.
        ///
        /// PACED OFF THE BAR, not off a dot count. The number of marks in a tag depends on how
        /// many letters it has -- CGF is three and BALL is four -- so a fixed dots-per-tick
        /// would finish some tags early and leave others half written when the spray stops.
        /// Working out how far through the paint we are and catching the letters up to it means
        /// the last dot always lands as the can runs out, whatever is being written.
        ///
        /// Several per tick rather than one. The bar runs for seven seconds and a tag is sixty
        /// odd marks, so one every 280ms would not get halfway.
        /// </summary>
        private void Write(int now, float r, float g, float b)
        {
            var paintFor = SprayMs - PaintDelayMs;
            if (paintFor <= 0) return;

            var done = (now - _sprayingSince - PaintDelayMs) / (float)paintFor;
            if (done < 0f) done = 0f;
            if (done > 1f) done = 1f;

            var want = (int)Math.Round(done * _tagPoints.Count);
            if (want > _tagPoints.Count) want = _tagPoints.Count;

            while (_tagPlaced < want)
            {
                var p = _tagPoints[_tagPlaced];
                _tagPlaced++;

                // A hand is not a plotter. Each mark gets shifted a little off its ideal spot
                // and given its own size, which is the whole difference between spray and a
                // dot-matrix printer.
                var jitter = TagSpacing * 0.30f;

                var du = p[0] * TagWide + ((float)_rng.NextDouble() * 2f - 1f) * jitter;
                var dv = TagFoot + p[1] * TagTall
                       + ((float)_rng.NextDouble() * 2f - 1f) * jitter;

                var at = _wallAt + _wallSide * du + _wallUp * dv;

                var roll = (float)(_rng.NextDouble() * Math.PI * 2d);
                var side = _wallSide * (float)Math.Cos(roll) + _wallUp * (float)Math.Sin(roll);

                var size = TagDotSize * (0.80f + (float)_rng.NextDouble() * 0.45f);

                if (!Put(at, side, size, r, g, b)) return;
            }
        }

        /// <summary>
        /// Puts one mark on the wall and remembers it. False if nothing would take.
        ///
        /// Split out of Stain so the writer and the old splatter share one decal path: the
        /// three-candidate probe, the logging of which type won, the miss counter and the
        /// persisted record are all things both want and neither should own.
        /// </summary>
        private bool Put(Vector3 at, Vector3 side, float size, float r, float g, float b)
        {
            foreach (var type in PaintDecals)
            {
                if (_decalType > 0 && type != _decalType) continue;

                int handle;

                try
                {
                    handle = Function.Call<int>(Hash.ADD_DECAL, type,
                                                at.X, at.Y, at.Z,
                                                _wallInto.X, _wallInto.Y, _wallInto.Z,
                                                side.X, side.Y, side.Z,
                                                size, size,
                                                r, g, b, 0.92f,
                                                StainForever, true, false, false);
                }
                catch (Exception ex)
                {
                    Log.Debug("Decal type " + type + " threw: " + ex.Message);
                    continue;
                }

                if (handle == 0) continue;

                if (_decalType != type)
                {
                    _decalType = type;
                    Log.Info("Tag paint lands as decal type " + type + ".");
                }

                _stains++;
                _stainMisses = 0;

                Remember(new PaintMark
                {
                    At = at, Into = _wallInto, Side = side, Size = size,
                    R = r, G = g, B = b, Handle = handle
                });

                return true;
            }

            _stainMisses++;
            return false;
        }

        private Ped SpawnMember(GangDef gang, Vector3 near, bool bat)
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

                    // A BAT OR HIS HANDS. Never a gun.
                    //
                    // These are men who came out of a house because somebody is painting on
                    // their wall. A pistol turns a scuffle over a fence into a firefight in the
                    // middle of a stealth job -- and it was ninety rounds each, which is a
                    // gunfight nobody asked for at every third wall.
                    //
                    // Combat attribute 46 above is what makes this work rather than making them
                    // useless: it is BF_CanFightArmedPedsWhenNotArmed, so a man with a bat will
                    // still come at Franklin while Franklin is holding a rifle, instead of
                    // deciding the odds are bad and standing there.
                    if (bat)
                    {
                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_BAT"),
                                      1, false, true);
                    }

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

        /// <summary>
        /// Why the run ended badly, or null. Read by MissionRunner, same as the bike ride's.
        /// </summary>
        public string Failure { get; private set; }

        public void Clear()
        {
            Failure = null;

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

        public void RestoreWorld()
        {
            // The marks go too, and ONLY here. A script reload loses the list but not the
            // decals, so leaving them would put a second set on top of the first the next time
            // somebody paints that wall.
            WipeAllMarks();
            Clear();
        }
    }
}
