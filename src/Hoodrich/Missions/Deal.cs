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

        /// <summary>
        /// Rogers Scrap, under the La Puerta freeway, as stood on and read off.
        ///
        /// FOUR PLACES AND A HEADING EACH, because this is a set and not a spawn radius. You
        /// leave the car on the dirt, you walk to a man stood by the units, and when it turns
        /// the three who were always coming come from three directions -- one across the yard,
        /// one from the back gate, and one from above, under the bridge on the roof.
        ///
        /// THE HEIGHTS ARE NOT NEGOTIABLE AND ONE OF THEM PROVES IT. The rifle on the roof is
        /// four metres over the yard floor, so anything that "helpfully" drops a spawn to the
        /// ground under it puts him in the dirt with the others and the whole shape of the
        /// ambush goes flat. See Stood, which is why the fix is arithmetic rather than a probe.
        /// </summary>
        private static readonly Vector3 Park = new Vector3(-532.112f, -1715.173f, 18.534f);
        private static readonly Vector3 Stall = new Vector3(-541.955f, -1713.213f, 19.137f);
        private const float StallFace = 259.859f;

        private static readonly Vector3[] Ambush =
        {
            new Vector3(-537.338f, -1701.869f, 19.617f),   // across the yard
            new Vector3(-548.794f, -1689.069f, 19.498f),   // the back gate
            new Vector3(-559.733f, -1727.837f, 23.417f),   // under the bridge, up on the roof
        };

        private static readonly float[] AmbushFace = { 152.210f, 198.905f, 312.144f };

        /// <summary>How many turn up with rifles once the dealer pulls. See Ambush.</summary>
        private static readonly int Many = Ambush.Length + 1;

        /// <summary>Near enough to the lot to put them out, and how long that is given.</summary>
        private const float LayFrom = 160f;
        private const int LayPatienceMs = 30000;

        /// <summary>Nobody is put down nearer than this, or where the camera can see it happen.</summary>
        private const float SpawnClear = 45f;

        /// <summary>Near enough to the dirt to call the car parked, and to the man to talk.</summary>
        private const float ParkWithin = 12f;
        private const float MeetWithin = 6f;

        /// <summary>
        /// How long it is still a meeting.
        ///
        /// LONG ENOUGH TO RELAX AND NOT LONG ENOUGH TO ACT. About three seconds: one line out
        /// of the man doing the talking, a pause, and then it goes. Shorter and the turn reads
        /// as a scripted ambush that was never a meeting; longer and a player who has smelled
        /// it starts shooting first, which robs him of the one beat this job exists for.
        /// </summary>
        private const int MeetMs = 7000;

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

        /// <summary>
        /// What the three bring, in the order an install has them.
        ///
        /// RIFLES, WHICH IS THE TELL. The man you have been talking to has a pistol under his
        /// coat like anybody at a meeting; the three who come out of the yard were kitted for
        /// this before either of you left Strawberry. Nobody brings a carbine to a sale.
        /// </summary>
        private static readonly string[] Rifles =
        {
            "WEAPON_ASSAULTRIFLE", "WEAPON_CARBINERIFLE", "WEAPON_SMG"
        };

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

        /// <summary>Vernon, who said he could not leave the store. See Locations.Vernon.Lend.</summary>
        private Ped _vee;
        private bool _veeRiding;
        private int _veeAt;

        /// <summary>The car is off the road and you are on the dirt.</summary>
        private bool _parked;

        /// <summary>Which line of the meeting he is on. See Meeting.</summary>
        private int _beat;

        /// <summary>Set by Main: Vernon, and putting him back on his wall afterwards.</summary>
        public Func<Ped> Fixer;
        public Action GiveBack;

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
                    case DealPhase.Riding:
                        return _parked ? "Talk to the man" : "Take Vernon to Rogers Scrap";

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

            // THE YARD IS NOT IN THE DATA AND WILL NOT BE. It is four measured places and a
            // heading each -- see Park and Ambush -- and a definition that could move the job
            // somewhere else would move it away from the only place its shape makes sense.
            _lot = Stall;

            Failure = null;
            ReadyToCollect = false;

            Phase = DealPhase.Riding;
            _phaseFrom = Game.GameTime;
            _layFrom = 0;
            _parked = false;
            _beat = 0;
            _veeAt = 0;
            _veeRiding = false;

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

                Vee(player, now);

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
        /// Out to the yard, park on the dirt, and walk to the man.
        ///
        /// TWO STEPS, AND THE CAR IS THE FIRST. The brief has Vernon in the passenger seat
        /// with a briefcase, so arriving matters: you pull onto the dirt where everybody
        /// arriving here pulls on, you get out, and you walk the last twenty metres. Rolling
        /// up to a drug deal through the gate at fifty and stopping on the man's feet is a
        /// different scene and it is not the one the brief sold you.
        /// </summary>
        private void Riding(Ped player, int now)
        {
            if (player.Position.DistanceTo(Stall) > LayFrom) return;

            if (_layFrom == 0) _layFrom = now;

            Lay(player);

            if (_them.Count == 0)
            {
                if (now - _layFrom < LayPatienceMs) return;

                Failure = "nobody turned up.";
                return;
            }

            // ON THE DIRT AND OUT OF THE CAR. Either one alone is half of arriving.
            if (!_parked)
            {
                if (player.Position.DistanceTo(Park) > ParkWithin) return;
                if (player.IsInVehicle()) return;

                _parked = true;
                Mark();

                return;
            }

            if (player.Position.DistanceTo(Stall) > MeetWithin) return;

            Phase = DealPhase.Meeting;
            _phaseFrom = now;
            _beat = 0;

            Log.Info("Deal: stood in front of him.");
        }

        /// <summary>
        /// The man you came to see, stood where he said he would be.
        ///
        /// ONE OF HIM, AND THAT IS THE WHOLE TRICK. Four men waiting in a scrap yard is an
        /// ambush you can read from the gate, and a player who reads it never has the meeting.
        /// One man on his own by the units is exactly what Vernon described, right up until it
        /// is not -- see Turning, where the other three arrive from three directions at once.
        /// </summary>
        private void Lay(Ped player)
        {
            if (_them.Count > 0) return;

            try
            {
                if (Function.Call<bool>(Hash.IS_SPHERE_VISIBLE, Stall.X, Stall.Y, Stall.Z, 6f) &&
                    player.Position.DistanceTo(Stall) < SpawnClear)
                {
                    return;
                }
            }
            catch
            {
                // Then distance will have to do.
            }

            var who = Make(Boss, Stood(Stall), Waiting[0], StallFace);
            if (who == null) return;

            _them.Add(new Man { Who = who, Talks = true });

            if (_theirs == null || !_theirs.Exists()) _theirs = TheirCar(Stood(Stall));

            Log.Info("Deal: he is out there waiting.");
        }

        // ======================================================================
        // The meeting, and the end of it
        // ======================================================================

        /// <summary>
        /// The two of them talking, and it is a real conversation right up until it is not.
        ///
        /// SEVEN SECONDS, IN BEATS. Vernon does most of it, because Vernon has been waiting six
        /// weeks to do this in person and cannot help himself, and the man answers him in the
        /// short flat way of somebody running a clock. The lines are in Talk; what matters
        /// mechanically is that the player is stood still in the middle of it with nothing to
        /// press, which is the only way the turn can land.
        /// </summary>
        private void Meeting(Ped player, int now)
        {
            if (Gone(player)) return;

            Face(player);

            var gone = now - _phaseFrom;
            var beat = gone / (MeetMs / Math.Max(1, Talk.Length));

            if (beat > _beat && _beat < Talk.Length)
            {
                Word(Talk[_beat]);
                _beat++;
            }

            if (gone < MeetMs) return;

            Phase = DealPhase.Turning;
            _phaseFrom = now;

            Pull(player);

            Log.Info("Deal: it turned.");
        }

        /// <summary>
        /// The dealer takes a gun out, and everybody else arrives.
        ///
        /// THE THREE COME FROM THREE DIRECTIONS AND ONE OF THEM IS ABOVE YOU. Across the yard,
        /// the back gate, and the roof under the bridge -- see Ambush. They are put down at
        /// the moment it turns rather than standing there from the start, because three men
        /// with rifles loitering in a scrap yard is a scene that answers its own question, and
        /// the whole job is built on you not having asked it.
        ///
        /// AND THEY WANT BOTH OF YOU. Vernon is stood next to you holding a briefcase. A
        /// set-up that only ever shoots at the player is a set-up the player can walk out of
        /// by leaving him there.
        /// </summary>
        private void Pull(Ped player)
        {
            foreach (var m in _them)
            {
                if (m.Who == null || !m.Who.Exists() || !m.Who.IsAlive) continue;

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, m.Who.Handle);
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, m.Who.Handle,
                                  "GENERIC_INSULT_HIGH", "SPEECH_PARAMS_FORCE_SHOUTED");

                    // The gun comes out, and it is a full second before he uses it. See
                    // Turning: the beat is the story.
                    Function.Call(Hash.SET_CURRENT_PED_WEAPON, m.Who.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"), true);
                }
                catch
                {
                    // Then it is a quiet double-cross.
                }
            }

            for (var i = 0; i < Ambush.Length; i++)
            {
                var who = Make(Goons, Stood(Ambush[i]), null, AmbushFace[i]);
                if (who == null) continue;

                try
                {
                    var gun = 0u;

                    foreach (var name in Rifles)
                    {
                        gun = Function.Call<uint>(Hash.GET_HASH_KEY, name);
                        if (gun != 0) break;
                    }

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, who.Handle, gun, 250, false, true);
                    Function.Call(Hash.SET_CURRENT_PED_WEAPON, who.Handle, gun, true);
                }
                catch
                {
                    // He arrives with his hands, which is still three more of them.
                }

                _them.Add(new Man { Who = who });
            }

            Log.Info("Deal: " + Ambush.Length + " more of them, and one of them is up top.");
        }

        /// <summary>
        /// The second where you know and it has not started yet.
        ///
        /// WORTH HAVING AS ITS OWN PHASE. Going from a conversation straight to four men
        /// shooting reads as a trigger being pulled by the mod; a beat in between -- he stops
        /// talking, a gun comes out, men appear on the roof -- reads as a decision being made
        /// by the people in front of you. It is the same second either way. One of them is a
        /// story.
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
                    // looking like a meeting; from here they are just armed men, and the game
                    // is better at that than any of this would be.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, m.Who.Handle, false);
                    Function.Call(Hash.SET_PED_KEEP_TASK, m.Who.Handle, false);
                    Function.Call(Hash.CLEAR_PED_TASKS, m.Who.Handle);
                    Function.Call(Hash.SET_PED_ALERTNESS, m.Who.Handle, 3);
                    Function.Call(Hash.SET_PED_COMBAT_ABILITY, m.Who.Handle, 1);
                    Function.Call(Hash.SET_PED_ACCURACY, m.Who.Handle, 24);

                    // BOTH OF YOU, and the nearer of you first. Told to fight the player and
                    // nothing else, they walk past Vernon to get to you, which looks exactly
                    // as stupid as it is.
                    Function.Call(Hash.TASK_COMBAT_PED, m.Who.Handle, player.Handle, 0, 16);

                    if (_vee != null && _vee.Exists() && _vee.IsAlive)
                    {
                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, m.Who.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, "HATES_PLAYER"));
                    }
                }
                catch
                {
                    // He stands there, which is its own kind of unnerving.
                }

                Blip(m);
            }

            Guard();

            Phase = DealPhase.Fighting;
            _phaseFrom = now;
        }

        /// <summary>
        /// Vernon gets to fight back, badly.
        ///
        /// He is an electrical wholesaler with a briefcase and the three men on the yard want
        /// him dead, so standing him there unarmed is a cutscene where your friend is shot. He
        /// gets a pistol and the game's worst aim, which is both survivable and true to him.
        /// </summary>
        private void Guard()
        {
            if (_vee == null || !_vee.Exists() || !_vee.IsAlive) return;

            try
            {
                Function.Call(Hash.GIVE_WEAPON_TO_PED, _vee.Handle,
                              Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"),
                              120, false, true);

                Function.Call(Hash.SET_PED_ACCURACY, _vee.Handle, 12);
                Function.Call(Hash.SET_PED_COMBAT_ABILITY, _vee.Handle, 0);
                Function.Call(Hash.SET_PED_KEEP_TASK, _vee.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS, _vee.Handle);

                foreach (var m in _them)
                {
                    if (m.Who == null || !m.Who.Exists() || !m.Who.IsAlive) continue;

                    Function.Call(Hash.TASK_COMBAT_PED, _vee.Handle, m.Who.Handle, 0, 16);
                    break;
                }
            }
            catch
            {
                // Then he stands behind you, which is also in character.
            }
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

            at = Stood(at);

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
        private Ped Make(string[] names, Vector3 at, string doing, float face)
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
                    who.Heading = face;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, who.Handle, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, who.Handle, true);
                    Function.Call(Hash.SET_PED_KEEP_TASK, who.Handle, true);

                    // The three who arrive when it turns get no scenario at all -- they are
                    // tasked to fight on the same frame and a scenario would only have to be
                    // cleared again.
                    if (!string.IsNullOrEmpty(doing))
                    {
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, who.Handle, doing, 0, true);
                    }

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

                    var car = World.CreateVehicle(model, Stood(spot));
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

        /// <summary>
        /// Vernon, out of the shop and into whatever you are in.
        ///
        /// HE SAID HE COULD NOT LEAVE THE STORE. He is in the passenger seat with his father's
        /// briefcase, which is the joke and also the reason the ambush has two targets instead
        /// of one -- see Pull. Borrowed from the wall rather than made here, so there is never
        /// a second Vernon standing in Strawberry while this one is in La Puerta.
        ///
        /// RE-TASKED ON A CHANGE AND ON A CLOCK, never every frame. A man handed a fresh
        /// follow task sixty times a second is a man who never finishes starting to walk --
        /// which is the same note the hunt's Lamar carries, for the same reason.
        /// </summary>
        private void Vee(Ped player, int now)
        {
            if (_vee == null || !_vee.Exists())
            {
                if (Fixer != null) _vee = Fixer();
                if (_vee == null || !_vee.Exists()) return;

                _veeAt = 0;
            }

            if (!_vee.IsAlive) return;

            // Fighting is the game's business once it starts. See Guard.
            if (Phase == DealPhase.Fighting || Phase == DealPhase.Turning) return;

            if (now - _veeAt < 2500) return;
            _veeAt = now;

            try
            {
                var car = player.CurrentVehicle;

                if (car != null && car.Exists())
                {
                    if (_veeRiding && _vee.IsInVehicle(car)) return;

                    var seat = Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, car.Handle, 0)
                        ? 0
                        : 1;

                    Function.Call(Hash.TASK_ENTER_VEHICLE, _vee.Handle, car.Handle, 12000,
                                  seat, 2f, 1, 0);

                    _veeRiding = true;
                    return;
                }

                if (_veeRiding)
                {
                    Function.Call(Hash.TASK_LEAVE_VEHICLE, _vee.Handle,
                                  _vee.CurrentVehicle == null ? 0 : _vee.CurrentVehicle.Handle, 0);

                    _veeRiding = false;
                    return;
                }

                // On foot, a stride behind, and stood still once the talking starts.
                if (Phase == DealPhase.Meeting)
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _vee.Handle,
                                  player.Handle, 1500);
                    return;
                }

                Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, _vee.Handle, player.Handle,
                              -0.8f, -1.4f, 0f, 2f, -1, 2.5f, true);
            }
            catch
            {
                // He makes his own way, which for Vernon is standing very still.
            }
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

        /// <summary>
        /// The meeting, in the order it is said.
        ///
        /// VERNON CANNOT HELP HIMSELF AND THAT IS THE CLOCK. Every one of his lines is a man
        /// who has waited six weeks to do this in person; every one of the answers is a man
        /// running a stopwatch and giving away nothing. The last one is the only honest thing
        /// said in the yard and neither of them hears it.
        /// </summary>
        private static readonly string[] Talk =
        {
            "vee:ay -- Ardo? It's Vee. Vernon. From the lights.",
            "arm:You brought somebody.",
            "vee:That's my head of security. He with me. So -- we good? You got it?",
            "arm:Put the case down.",
            "vee:...I mean I will, but that's four hundred dollars of -- Franklin, is that a"
        };

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
        /// One line of the meeting, out of whichever mouth it belongs to.
        ///
        /// PREFIXED RATHER THAN PAIRED OFF, because the alternative is two arrays that have to
        /// be kept in step by hand and a bug the first time somebody adds a line to one of
        /// them. "vee:" is Vernon and anything else is the man he is buying from.
        /// </summary>
        private void Word(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            var mine = line.StartsWith("vee:", StringComparison.OrdinalIgnoreCase);
            var words = line.Substring(line.IndexOf(':') + 1);

            Ped who = null;

            if (mine)
            {
                who = _vee;
            }
            else
            {
                foreach (var m in _them)
                {
                    if (!m.Talks || m.Who == null || !m.Who.Exists()) continue;
                    who = m.Who;
                    break;
                }
            }

            if (who == null || !who.Exists() || !who.IsAlive) return;

            try { Core.Voice.Say(mine ? "vernon" : "armenian", words, null, true); }
            catch { /* the banks below, or nothing */ }

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle,
                              mine ? "GENERIC_HOWS_IT_GOING" : "GENERIC_FUCK_OFF",
                              "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A quiet meeting.
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

        /// <summary>How far a man's middle is above the floor he is stood on. See Stood.</summary>
        private const float PelvisUp = 1.0f;

        /// <summary>
        /// The floor under a place that was read off a HUD.
        ///
        /// A HUD COORDINATE IS A MAN'S PELVIS, about a metre over the concrete -- the hunt
        /// lost a session to that, with Ballas loitering in mid-air because the number that
        /// named their corner was never the ground. So a metre comes off, always.
        ///
        /// AND THE PROBE ONLY GETS TO AGREE, NEVER TO OVERRULE. One of these three is stood on
        /// a ROOF under the freeway with four metres of open air beneath him, and
        /// GET_GROUND_Z_FOR_3D_COORD on a spot like that is as likely to find the yard floor
        /// as the roof -- which would take the man off the roof, put him in the dirt with the
        /// other two, and flatten the one bit of the ambush that comes from above. So the
        /// arithmetic decides and the probe is only allowed to nudge it: agree within about a
        /// metre and it wins, disagree and it is ignored as having found the wrong surface.
        /// </summary>
        private static Vector3 Stood(Vector3 read)
        {
            var floor = read.Z - PelvisUp;

            try
            {
                if (World.GetGroundHeight(new Vector3(read.X, read.Y, read.Z + 1f),
                                          out var probe, GetGroundHeightMode.Normal) &&
                    probe > 0f && Math.Abs(probe - floor) <= 1.2f)
                {
                    floor = probe;
                }
            }
            catch
            {
                // The arithmetic stands, which is the answer it was going to give anyway.
            }

            return new Vector3(read.X, read.Y, floor);
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

            if (_vee != null && GiveBack != null)
            {
                try { GiveBack(); }
                catch { /* the wall refills on its own */ }
            }

            _vee = null;
            _veeRiding = false;
            _veeAt = 0;

            _def = null;
            _layFrom = 0;
            _parked = false;
            _beat = 0;

            Phase = DealPhase.None;
            ReadyToCollect = false;
            Failure = null;
        }
    }
}
