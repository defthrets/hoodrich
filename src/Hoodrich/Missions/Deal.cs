using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Missions
{
    /// <summary>Where a buy is up to.</summary>
    internal enum DealPhase
    {
        None = 0,

        /// <summary>Driving out to it with somebody else's money.</summary>
        Riding,

        /// <summary>Stood in front of them, and nothing is wrong yet.</summary>
        Meeting,

        /// <summary>It just went wrong. A second and a half of it, and then guns.</summary>
        Turning,

        /// <summary>Guns.</summary>
        Fighting,

        /// <summary>They are down and the thing you came for is on the floor.</summary>
        Collecting,

        /// <summary>Back to the man who sent you.</summary>
        Leaving
    }

    /// <summary>
    /// THE BUY.
    ///
    /// Vernon has a basement, a vocal booth and no supplier worth the name, and six weeks ago
    /// a man walked into his electrical shop and bought every fluorescent tube in it. They
    /// exchanged numbers over the counter the way two people do when each of them has
    /// privately worked out what the other one is. That is the entire due diligence behind
    /// this job, and Vernon is delighted with it.
    ///
    /// NOBODY IN THE BRIEF KNOWS IT IS A ROBBERY, AND THAT IS THE POINT. Read the pitch and
    /// there is no warning in it anywhere -- it is a man explaining an arrangement he is proud
    /// of, at length, with terms. The one thing in it that should worry anybody is the thing
    /// Vernon is proudest of: he asked the Armenians for cocaine, which is not what the
    /// Armenians move -- see gangs.json, where their trade is heroin -- and the answer he got
    /// was "we can get anything". He heard a business that understands demand. What it
    /// actually is, is a man agreeing to sell something he does not have to somebody who has
    /// already paid a deposit.
    ///
    /// SO THE JOB TURNS, AND IT TURNS ON ITS OWN CLOCK. There is no shot to react to and no
    /// choice to get wrong beforehand: you walk up, there is a beat where it is still a
    /// meeting, and then it is not. What you do about it is the mission. Everything before
    /// that is the setup being funny at your expense.
    ///
    ///   THE RIDE. A ring on the map and Vernon's money in the car.
    ///
    ///   THE MEET. Four of them stood round a car, doing nothing, in their own part of town.
    ///   Walk into it and the clock starts. Nothing you can do in those few seconds changes
    ///   what happens next, which is deliberate -- a player who could draw first would never
    ///   see the turn, and the turn is the story.
    ///
    ///   THE FIGHT. They are not the hunt's Ballas: nothing is blocking their events and
    ///   nothing is holding their hands. They fight like the game fights.
    ///
    ///   THE BRICK. It was in the car the whole time, which is the other half of the joke --
    ///   they had it, they just had no intention of selling it. Pick it up off the floor.
    ///
    ///   THE RIDE BACK. Vernon is on his wall where he has always been, and he has questions.
    /// </summary>
    internal sealed class Deal
    {
        // ======================================================================
        // Measures
        // ======================================================================

        /// <summary>How many of them are stood there.</summary>
        private const int Many = 4;

        /// <summary>Near enough to the lot to put them out, and how long that is given.</summary>
        private const float LayFrom = 160f;
        private const int LayPatienceMs = 30000;

        /// <summary>Nobody is put down nearer than this, or where the camera can see it happen.</summary>
        private const float SpawnClear = 45f;

        /// <summary>Walk inside this and the meeting starts.</summary>
        private const float MeetWithin = 9f;

        /// <summary>
        /// How long it is still a meeting.
        ///
        /// LONG ENOUGH TO RELAX AND NOT LONG ENOUGH TO ACT. About three seconds: one line out
        /// of the man doing the talking, a pause, and then it goes. Shorter and the turn reads
        /// as a scripted ambush that was never a meeting; longer and a player who has smelled
        /// it starts shooting first, which robs him of the one beat this job exists for.
        /// </summary>
        private const int MeetMs = 3200;

        /// <summary>And how long the turn itself takes, before anybody pulls anything.</summary>
        private const int TurnMs = 1400;

        /// <summary>Near enough to the brick on the floor to pick it up.</summary>
        private const float GrabWithin = 2.2f;

        /// <summary>Near enough to Vernon's wall to call it back.</summary>
        private const float HomeWithin = 22f;

        /// <summary>Vernon's wall, outside Leroy's. See Locations.Vernon.</summary>
        private static readonly Vector3 Home = new Vector3(51.057f, -1452.677f, 29.312f);

        /// <summary>
        /// Who turns up, worst of them first.
        ///
        /// The boss does the talking and the goons stand behind him, which is the shape of
        /// every meeting like this ever held. Every name is in this install's ped list.
        /// </summary>
        private static readonly string[] Boss = { "g_m_m_armboss_01", "g_m_m_armlieut_01" };
        private static readonly string[] Goons = { "g_m_y_armgoon_02", "g_m_m_armlieut_01" };

        /// <summary>What they came in. See Payback, which gives them the same four.</summary>
        private static readonly string[] Cars = { "schafter2", "oracle", "felon", "cavalcade" };

        /// <summary>What is in the boot, and what it is lying on when they are done.</summary>
        private static readonly string[] Brick = { "prop_coke_block_01", "prop_drug_package_02" };

        /// <summary>What they are doing before you get there.</summary>
        private static readonly string[] Waiting =
        {
            "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_MOBILE",
            "WORLD_HUMAN_GUARD_STAND"
        };

        // ======================================================================
        // The people in it
        // ======================================================================

        private sealed class Man
        {
            public Ped Who;
            public Blip Mark;
            public bool Talks;
        }

        private readonly List<Man> _them = new List<Man>();

        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly Random _rng = new Random();

        private MissionDef _def;
        private Vector3 _lot;
        private Blip _mark;
        private int _markAt = -2;

        private int _layFrom;
        private int _phaseFrom;

        private Vehicle _theirs;
        private Prop _brick;
        private Blip _brickMark;

        public DealPhase Phase { get; private set; }

        public bool IsRunning => Phase != DealPhase.None;

        public bool ReadyToCollect { get; private set; }

        public string Failure { get; private set; }

        public int Standing
        {
            get
            {
                var n = 0;
                foreach (var m in _them) if (m.Who != null && m.Who.Exists() && m.Who.IsAlive) n++;
                return n;
            }
        }

        public float Advance
        {
            get
            {
                switch (Phase)
                {
                    case DealPhase.Riding: return 0.1f;
                    case DealPhase.Meeting:
                    case DealPhase.Turning: return 0.25f;
                    case DealPhase.Fighting: return 0.25f + 0.5f * (1f - Standing / (float)Many);
                    case DealPhase.Collecting: return 0.85f;
                    case DealPhase.Leaving: return 0.95f;
                    default: return 0f;
                }
            }
        }

        public string Objective
        {
            get
            {
                switch (Phase)
                {
                    case DealPhase.Riding: return "Go and meet Vernon's man";
                    case DealPhase.Meeting: return "Do the deal";
                    case DealPhase.Turning: return "Do the deal";
                    case DealPhase.Fighting: return "Kill them  --  " + (Many - Standing) + " of " + Many;
                    case DealPhase.Collecting: return "Take the package";
                    case DealPhase.Leaving: return "Get it back to Vernon";
                    default: return "";
                }
            }
        }

        public Deal(Affiliation crew, GangRegistry gangs)
        {
            _crew = crew;
            _gangs = gangs;
        }

        // ======================================================================
        // Starting
        // ======================================================================

        public string Start(MissionDef def)
        {
            if (def == null) return "nothing to go and buy.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "not right now.";

            Clear();

            _def = def;
            _lot = new Vector3(def.X, def.Y, def.Z);

            if (_lot == Vector3.Zero) return "he never said where.";

            Failure = null;
            ReadyToCollect = false;

            Phase = DealPhase.Riding;
            _phaseFrom = Game.GameTime;
            _layFrom = 0;

            Mark();

            Log.Info("Deal: on, at " + _lot.X.ToString("0") + ", " + _lot.Y.ToString("0") + ".");

            return null;
        }

        // ======================================================================
        // The tick
        // ======================================================================

        public void Update()
        {
            if (!IsRunning) return;

            try
            {
                var player = Game.Player.Character;

                if (player == null || !player.Exists() || !player.IsAlive)
                {
                    Failure = "you didn't make it back.";
                    return;
                }

                var now = Game.GameTime;

                switch (Phase)
                {
                    case DealPhase.Riding: Riding(player, now); break;
                    case DealPhase.Meeting: Meeting(player, now); break;
                    case DealPhase.Turning: Turning(player, now); break;
                    case DealPhase.Fighting: Fighting(player, now); break;
                    case DealPhase.Collecting: Collecting(player, now); break;
                    case DealPhase.Leaving: Leaving(player, now); break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("The deal fell over: " + ex.Message);
                Failure = "it went wrong out there.";
            }
        }

        /// <summary>
        /// Out to the lot, and they are put there once you are near enough and not looking.
        /// </summary>
        private void Riding(Ped player, int now)
        {
            if (player.Position.DistanceTo(_lot) > LayFrom) return;

            if (_layFrom == 0) _layFrom = now;

            Lay(player);

            if (_them.Count < Many && now - _layFrom < LayPatienceMs) return;

            if (_them.Count == 0)
            {
                Failure = "nobody turned up.";
                return;
            }

            Phase = DealPhase.Meeting;
            _phaseFrom = now;

            Log.Info("Deal: " + _them.Count + " of them waiting.");
        }

        /// <summary>
        /// Them, their car, and the thing they were never going to hand over.
        ///
        /// PUT DOWN IN ONE GO rather than a man a tick, because unlike the hunt they are all
        /// stood in the same six metres of car park and there is no version of this where some
        /// of them have arrived and the rest have not.
        /// </summary>
        private void Lay(Ped player)
        {
            if (_them.Count >= Many) return;

            try
            {
                if (Function.Call<bool>(Hash.IS_SPHERE_VISIBLE, _lot.X, _lot.Y, _lot.Z, 6f) &&
                    player.Position.DistanceTo(_lot) < SpawnClear)
                {
                    return;
                }
            }
            catch
            {
                // Then distance will have to do.
            }

            var ground = Ground(_lot);

            if (_theirs == null || !_theirs.Exists()) _theirs = TheirCar(ground);

            for (var i = _them.Count; i < Many; i++)
            {
                // The one who talks stands at the front. The rest fan out behind him.
                var talks = i == 0;

                var about = ground + Vector3.RandomXY() * (talks ? 1.2f : 2.4f + i * 0.6f);
                var at = Ground(about);

                var who = Make(talks ? Boss : Goons, at, Waiting[i % Waiting.Length]);
                if (who == null) return;

                _them.Add(new Man { Who = who, Talks = talks });
            }

            Log.Info("Deal: the meet is set.");
        }

        // ======================================================================
        // The meeting, and the end of it
        // ======================================================================

        /// <summary>
        /// Stood in front of them, before anything has happened.
        ///
        /// The clock does not start until you are actually in it. Drive past at forty and
        /// nothing happens; walk up and you have about three seconds of a normal afternoon.
        /// </summary>
        private void Meeting(Ped player, int now)
        {
            if (Gone(player)) return;

            if (player.Position.DistanceTo(_lot) > MeetWithin)
            {
                _phaseFrom = now;
                return;
            }

            if (now - _phaseFrom < 400) return;

            Face(player);

            if (now - _phaseFrom < MeetMs) return;

            Phase = DealPhase.Turning;
            _phaseFrom = now;

            Say("GENERIC_INSULT_HIGH");

            Log.Info("Deal: it turned.");
        }

        /// <summary>
        /// The second and a half where you know and it has not started yet.
        ///
        /// WORTH HAVING AS ITS OWN PHASE. Going from a conversation straight to four men
        /// shooting reads as a trigger being pulled by the mod; a beat in between -- he stops
        /// talking, everybody turns -- reads as a decision being made by the men in front of
        /// you. It is the same second either way. One of them is a story.
        /// </summary>
        private void Turning(Ped player, int now)
        {
            if (Gone(player)) return;

            if (now - _phaseFrom < TurnMs) return;

            foreach (var m in _them)
            {
                if (m.Who == null || !m.Who.Exists() || !m.Who.IsAlive) continue;

                try
                {
                    // HANDED BACK TO THE GAME, ALL OF IT. They were held still to stand about
                    // looking like a meeting; from here they are just armed men and the game
                    // is better at that than any of this would be.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, m.Who.Handle, false);
                    Function.Call(Hash.SET_PED_KEEP_TASK, m.Who.Handle, false);
                    Function.Call(Hash.CLEAR_PED_TASKS, m.Who.Handle);
                    Function.Call(Hash.SET_PED_ALERTNESS, m.Who.Handle, 3);
                    Function.Call(Hash.SET_PED_COMBAT_ABILITY, m.Who.Handle, 1);
                    Function.Call(Hash.SET_PED_ACCURACY, m.Who.Handle, 28);
                    Function.Call(Hash.TASK_COMBAT_PED, m.Who.Handle, player.Handle, 0, 16);
                }
                catch
                {
                    // He stands there, which is its own kind of unnerving.
                }

                Blip(m);
            }

            Phase = DealPhase.Fighting;
            _phaseFrom = now;
        }

        private void Fighting(Ped player, int now)
        {
            if (Standing > 0) return;

            Phase = DealPhase.Collecting;
            _phaseFrom = now;

            Drop();

            Log.Info("Deal: all of them down. The package is on the floor.");
        }

        // ======================================================================
        // The package
        // ======================================================================

        /// <summary>
        /// What they had in the car all along.
        ///
        /// PUT DOWN WHERE THE TALKER FELL rather than in the boot, because a boot is a prompt
        /// and a container and a camera angle, and this is a thing on the floor you walk over.
        /// If he cannot be found -- shot off a roof, despawned, fell through the world -- it
        /// goes by their car instead, and if there is no car either it goes on the spot the
        /// meet was called at. There is no version of this where the player arrives and there
        /// is nothing to pick up.
        /// </summary>
        private void Drop()
        {
            var at = _lot;

            foreach (var m in _them)
            {
                if (!m.Talks || m.Who == null || !m.Who.Exists()) continue;
                at = m.Who.Position;
                break;
            }

            if (at == _lot && _theirs != null && _theirs.Exists())
            {
                at = _theirs.Position + _theirs.ForwardVector * -3f;
            }

            at = Ground(at);

            foreach (var name in Brick)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    _brick = World.CreateProp(model, at + new Vector3(0f, 0f, 0.05f), false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_brick == null || !_brick.Exists()) continue;

                    _brick.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _brick.Handle, true, true);
                    Function.Call(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, _brick.Handle);

                    break;
                }
                catch
                {
                    // Next name.
                }
            }

            try
            {
                _brickMark = _brick != null && _brick.Exists()
                    ? _brick.AddBlip()
                    : World.CreateBlip(at);

                if (_brickMark != null && _brickMark.Exists())
                {
                    _brickMark.Sprite = (BlipSprite)51;
                    _brickMark.Color = BlipColor.White;
                    _brickMark.Scale = 0.8f;
                    _brickMark.Name = "The package";
                    _brickMark.ShowRoute = true;
                }
            }
            catch
            {
                // The objective still says what to do.
            }
        }

        private void Collecting(Ped player, int now)
        {
            var at = _brick != null && _brick.Exists() ? _brick.Position : _lot;

            if (player.Position.DistanceTo(at) > GrabWithin) return;

            try { if (_brick != null && _brick.Exists()) _brick.Delete(); }
            catch { /* it goes with the job */ }

            _brick = null;

            Unmark(ref _brickMark);

            Phase = DealPhase.Leaving;
            _phaseFrom = now;

            Hud.PlaySound("PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            Mark();

            Log.Info("Deal: package collected. Back to Vernon.");
        }

        private void Leaving(Ped player, int now)
        {
            Mark();

            if (player.Position.DistanceTo(Home) > HomeWithin) return;

            ReadyToCollect = true;
        }

        // ======================================================================
        // The people, and the ground under them
        // ======================================================================

        /// <summary>
        /// One of them, stood about waiting, reacting to nothing until he is told to.
        ///
        /// HELD STILL ON PURPOSE, AND ONLY UNTIL THE TURN. A meeting where one of the four
        /// wanders off to look at a car is not a meeting, and the game's own gang hatred would
        /// have started this fight in the car park before anybody said a word. See Turning,
        /// where all of it comes back off.
        /// </summary>
        private Ped Make(string[] names, Vector3 at, string doing)
        {
            foreach (var name in names)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    var who = World.CreatePed(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (who == null || !who.Exists()) continue;

                    who.IsPersistent = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, who.Handle, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, who.Handle, true);
                    Function.Call(Hash.SET_PED_KEEP_TASK, who.Handle, true);
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, who.Handle, doing, 0, true);

                    // ARMED FROM THE START, AND HOLSTERED. They do not draw until the turn --
                    // four men holding pistols in a car park is not a meeting, it is already
                    // the fight -- but giving them the gun afterwards would mean the first
                    // second of it is four men patting their pockets.
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, who.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"),
                                  120, false, false);

                    if (_gangs != null)
                    {
                        var arm = _gangs.Get("armenians");

                        if (arm != null && arm.GroupHash != 0)
                        {
                            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, who.Handle, arm.GroupHash);
                        }
                    }

                    return who;
                }
                catch
                {
                    // Next model.
                }
            }

            return null;
        }

        private Vehicle TheirCar(Vector3 at)
        {
            foreach (var name in Cars)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    var spot = at + Vector3.RandomXY() * 6f;

                    var car = World.CreateVehicle(model, Ground(spot));
                    model.MarkAsNoLongerNeeded();

                    if (car == null || !car.Exists()) continue;

                    car.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, car.Handle, 2);

                    car.PlaceOnGround();

                    return car;
                }
                catch
                {
                    // Next name.
                }
            }

            return null;
        }

        /// <summary>They look at you while it is still a meeting, because people do.</summary>
        private void Face(Ped player)
        {
            foreach (var m in _them)
            {
                if (m.Who == null || !m.Who.Exists() || !m.Who.IsAlive) continue;

                try
                {
                    if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, m.Who.Handle, 224)) continue;
                    Function.Call(Hash.TASK_LOOK_AT_ENTITY, m.Who.Handle, player.Handle, 4000, 0, 2);
                }
                catch
                {
                    // Then they stare into the middle distance.
                }
            }
        }

        /// <summary>Whoever is doing the talking, saying the last thing anybody says politely.</summary>
        private void Say(string speech)
        {
            foreach (var m in _them)
            {
                if (!m.Talks || m.Who == null || !m.Who.Exists() || !m.Who.IsAlive) continue;

                try
                {
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, m.Who.Handle, speech,
                                  "SPEECH_PARAMS_FORCE_SHOUTED");
                }
                catch
                {
                    // A quiet double-cross.
                }

                return;
            }
        }

        /// <summary>
        /// The player walked out of it before it happened.
        ///
        /// Nothing here forces him back. A man who drives off mid-meeting has not failed
        /// anything yet -- the four of them are still stood in that car park with his money --
        /// so the phase simply resets and waits for him to come back.
        /// </summary>
        private bool Gone(Ped player)
        {
            return player.Position.DistanceTo(_lot) > LayFrom;
        }

        /// <summary>
        /// The pavement under a point.
        ///
        /// The lot is a coordinate somebody read off a HUD, which is a man's pelvis rather
        /// than the ground he is stood on -- see Hunt.Ground, which is the same fix for the
        /// same reason and cost the hunt a session of men loitering in mid-air.
        /// </summary>
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
                // The read height stands.
            }

            return where;
        }

        // ======================================================================
        // The map
        // ======================================================================

        /// <summary>
        /// One ring at a time: the lot, then the package, then Vernon's wall.
        ///
        /// Redrawn only when the answer changes, because a blip deleted and recreated every
        /// frame flickers on the minimap and shows up on nobody's draw budget as the reason.
        /// </summary>
        private void Mark()
        {
            var stage = Phase == DealPhase.Leaving ? 1 : 0;

            if (_mark != null && _mark.Exists() && _markAt == stage) return;

            Unmark(ref _mark);
            _markAt = stage;

            try
            {
                _mark = World.CreateBlip(stage == 1 ? Home : _lot, stage == 1 ? 20f : 35f);
                if (_mark == null || !_mark.Exists()) return;

                _mark.Color = stage == 1 ? BlipColor.Green : BlipColor.Yellow;
                _mark.Alpha = 90;
                _mark.ShowRoute = true;
                _mark.Name = stage == 1 ? "Back to Vernon" : "The meet";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the deal: " + ex.Message);
            }
        }

        private static void Unmark(ref Blip blip)
        {
            try { if (blip != null && blip.Exists()) blip.Delete(); }
            catch { /* it goes with the job */ }

            blip = null;
        }

        private void Blip(Man m)
        {
            try
            {
                m.Mark = m.Who.AddBlip();
                if (m.Mark == null || !m.Mark.Exists()) return;

                m.Mark.Sprite = (BlipSprite)1;
                m.Mark.Color = BlipColor.Red;
                m.Mark.Scale = 0.6f;
                m.Mark.Name = "Armenian";
                m.Mark.IsShortRange = true;
            }
            catch
            {
                // They are in front of you anyway.
            }
        }

        // ======================================================================
        // The card
        // ======================================================================

        public void Draw()
        {
            if (!IsRunning) return;
            if (Phase == DealPhase.Riding || Phase == DealPhase.Leaving) return;

            const float w = 0.176f;
            const float h = 0.050f;

            var left = 0.5f - w * 0.5f;
            var top = 0.800f;

            Theme.Panel(left, top, w, h);

            var x = left + 0.011f;
            var right = left + w - 0.011f;

            Hud.Text("THE BUY", x, top + 0.005f, 0.25f,
                     Palette.Alpha(Palette.TextDim, 200), Hud.FontLabel, centre: false);

            if (Phase == DealPhase.Fighting)
            {
                Hud.TextRight((Many - Standing) + " / " + Many, right, top + 0.003f, 0.34f,
                              Palette.Text, Hud.FontLabel);
            }

            string words;
            var ink = Palette.Alpha(Palette.Text, 235);

            switch (Phase)
            {
                case DealPhase.Meeting:
                    words = "GIVE HIM THE MONEY";
                    ink = Palette.Brand;
                    break;

                case DealPhase.Turning:
                    words = "SOMETHING'S WRONG";
                    ink = Palette.Warn;
                    break;

                case DealPhase.Fighting:
                    words = "KILL THEM";
                    ink = Palette.Danger;
                    break;

                default:
                    words = "TAKE THE PACKAGE";
                    ink = Palette.Brand;
                    break;
            }

            Hud.RectFrom(left + 0.008f, top + h - 0.019f, w - 0.016f, 0.0155f,
                         Color.FromArgb(38, ink.R, ink.G, ink.B));

            Hud.RectFrom(left + 0.008f, top + h - 0.019f, 0.0016f, 0.0155f,
                         Palette.Alpha(ink, 210));

            Hud.Text(words, left + w * 0.5f, top + h - 0.0155f, 0.235f, ink, Hud.FontLabel);
        }

        // ======================================================================
        // The end
        // ======================================================================

        public void Clear()
        {
            Unmark(ref _mark);
            Unmark(ref _brickMark);

            _markAt = -2;

            foreach (var m in _them)
            {
                try
                {
                    Unmark(ref m.Mark);

                    if (m.Who != null && m.Who.Exists())
                    {
                        Function.Call(Hash.SET_PED_KEEP_TASK, m.Who.Handle, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, m.Who.Handle, false);
                        m.Who.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    // The game takes them back.
                }
            }

            _them.Clear();

            try
            {
                if (_theirs != null && _theirs.Exists()) _theirs.MarkAsNoLongerNeeded();
            }
            catch
            {
                // Likewise.
            }

            _theirs = null;

            try { if (_brick != null && _brick.Exists()) _brick.Delete(); }
            catch { /* it streams out */ }

            _brick = null;

            _def = null;
            _layFrom = 0;

            Phase = DealPhase.None;
            ReadyToCollect = false;
            Failure = null;
        }
    }
}
