using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;
using Control = GTA.Control;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Somebody at work in a room: who, what they are doing, and exactly where.
    ///
    /// The coordinates come from a player stood on the spot, the animation from the game's
    /// own list, and the model from the game's own list of business staff -- the same
    /// people the online game puts in these rooms.
    /// </summary>
    internal sealed class Post
    {
        public string Model = "";
        public string Dict = "";
        public string Clip = "";
        public float X, Y, Z, Heading;
    }

    /// <summary>
    /// One door and the room behind it, read out of the ini.
    ///
    /// Section is carried so the "that room is not there" message can name the exact block of
    /// settings to correct, rather than telling somebody a coordinate is wrong and leaving them
    /// to work out which of them.
    /// </summary>
    internal sealed class DoorSpec
    {
        public string Section = "";
        public string Name = "room";
        public string Ipl = "";

        /// <summary>
        /// IPL names the mod asks for whatever the ini says, semicolons between them.
        ///
        /// THE INI MAY ADD, IT MAY NOT TAKE AWAY. Ipl above is the player's, and an ini written
        /// before a name was corrected keeps the old one for ever -- which is exactly what
        /// happened here: the grow room's ini carried the ARCHETYPE name, the correction went
        /// into the code's default, and the default is the one thing an existing ini overrides.
        /// The room went on not loading and the file looked untouched.
        ///
        /// Asking for a name the game does not have costs nothing, so both are always asked.
        /// </summary>
        public string Extra = "";

        /// <summary>
        /// The stock and the equipment: interior ENTITY SETS, switched on by name once the
        /// room is loaded. Groups separated by semicolons; within a group, alternatives
        /// separated by bars, of which the first the game accepts wins. See Dress.
        /// </summary>
        public string Sets = "";

        public bool Blip = true;
        public BlipSprite Sprite = BlipSprite.Standard;

        public float DoorX, DoorY, DoorZ, DoorHeading;
        public float InsideX, InsideY, InsideZ, InsideHeading;

        /// <summary>
        /// Other places the room might be, tried in order when the one above turns out to
        /// have nothing in it.
        ///
        /// AN INTERIOR COORDINATE IS A GUESS UNTIL SOMEBODY HAS STOOD IN IT. It cannot be
        /// looked up: there is no call that turns an interior's name into where it is, and
        /// the rooms these doors use sit forty metres under the map where nobody wanders
        /// past them by accident. One wrong number and the door says the room is not there.
        ///
        /// So the door carries the addresses of the whole terrace rather than one house, and
        /// asks the game which of them has anything in it. That question is free -- it needs
        /// no warp and no wait -- and it turns a number somebody has to get right into a
        /// number somebody only has to get NEAR.
        /// </summary>
        public readonly List<Vector3> Elsewhere = new List<Vector3>();

        /// <summary>
        /// Who is in there, by model.
        ///
        /// The game does not staff these rooms. Online they are full of people because the
        /// online script puts them there, and a room with a full crop and nobody in it reads
        /// as a place that has been abandoned mid-harvest.
        ///
        /// NO JOBS ATTACHED. They had one animation each and it looked like three people
        /// frozen mid-task, which is what a work animation on a loop is -- the same gesture
        /// for as long as you watch. What is in there is people, and people move about.
        /// </summary>
        public readonly List<string> Crew = new List<string>();

        /// <summary>
        /// The people at work, each at their own station with their own job, on top of the
        /// crew above who wander. Crew is company; posts are the business running.
        /// </summary>
        public readonly List<Post> Posts = new List<Post>();
    }

    /// <summary>
    /// A door that puts you inside one of the game's own interiors.
    ///
    /// One thing about interiors that is worth stating plainly, because it shapes all of this.
    /// An MLO is not a prop and cannot be placed. It is baked into the map at one fixed
    /// coordinate, and REQUEST_IPL only decides whether that coordinate has anything in it -- it
    /// cannot decide WHERE. So "put an interior over there" is really "put a DOOR over there",
    /// and the door warps you across the map to wherever the interior actually lives. That is
    /// how every mod that uses a base-game interior does it, and from inside it is
    /// indistinguishable, because there are no windows.
    ///
    /// Every coordinate lives in Hoodrich.ini rather than in here. The door needs moving to
    /// wherever it should really stand, and the interior coordinate is a guess at where that
    /// particular room sits -- neither is worth a rebuild.
    ///
    /// One class rather than one per room. A second copy of this file with different constants
    /// in it is how a mod ends up with two of everything and a bug fixed in only one of them.
    ///
    /// The guess has a safety net. After the warp, the game is asked whether there is actually
    /// an interior where we just put you; if there is not, you come straight back out and get
    /// told the coordinate is wrong, rather than being left standing in a black void under the
    /// map wondering whether the mod has crashed.
    /// </summary>
    internal sealed class InteriorDoor
    {
        /// <summary>How close to the door before it offers to let you in.</summary>
        private const float DoorRange = 1.8f;

        /// <summary>How close to the inside mark before it offers to let you out.</summary>
        private const float ExitRange = 2.4f;

        /// <summary>Long enough for the fade, short enough not to feel like a loading screen.</summary>
        private const int FadeMs = 700;

        /// <summary>How long the interior gets to stream before we judge whether it is there.</summary>
        /// <summary>
        /// How long a room is given to arrive, and how often it is asked.
        ///
        /// A ceiling rather than a duration. The old flat wait was a guess -- too long on a
        /// machine that had the interior cached, and not long enough on the run that dropped
        /// somebody through the floor of the grow room.
        /// </summary>
        private const int StreamCeilingMs = 8000;
        private const int StreamStepMs = 100;

        /// <summary>
        /// How far above the recorded height the floor is looked for, and how far off that
        /// height the answer is still believed.
        ///
        /// The probe starts above him because it looks downward. The band is what stops it
        /// following a hit on the wrong surface: these rooms sit forty metres under the map,
        /// so a few metres either side of the recorded height is the floor of the room and
        /// anything further away is something else entirely.
        /// </summary>
        private const float FloorProbeUp = 3f;
        private const float FloorProbeBand = 4f;

        /// <summary>
        /// How far the room's own origin may be from the recorded coordinate before it is
        /// treated as a different room entirely.
        ///
        /// GET_INTERIOR_AT_COORDS will answer for an interior the point is merely NEAR, so an
        /// origin a hundred metres away means the ini is pointing at the wrong building rather
        /// than at the wrong corner of the right one -- and warping to it would be worse than
        /// failing honestly.
        /// </summary>
        private const float OriginTrust = 60f;

        /// <summary>
        /// Close enough to the entry mark to be in the doorway rather than out of the room.
        ///
        /// Only used by the watchdog below, which needs to tell "stepped through the frame for
        /// a second" apart from "walked out of the building".
        /// </summary>
        private const float DoorwaySlack = 8f;

        /// <summary>Long enough after the warp for the room to have decided he is in it.</summary>
        private const int SettleGraceMs = 2500;

        /// <summary>
        /// How far the nothing around one of these shells reaches.
        ///
        /// Inside this, he is a man who walked out of the wrong side of the building and needs
        /// putting back on the street. Outside it, he is a man who got arrested.
        /// </summary>
        private const float ShellYard = 500f;

        private readonly DoorSpec _spec;

        private Blip _blip;
        private bool _inside;

        /// <summary>One of the crew, and when they will next change their mind.</summary>
        private sealed class Hand
        {
            public Ped Who;

            /// <summary>At a station with a job, rather than wandering. Kept at it. See Work.</summary>
            public bool Posted;
            public string Dict;
            public string Clip;
            public int Until;
            public bool Walking;
        }

        /// <summary>The people in there while he is. Made on the way in, gone on the way out.</summary>
        private readonly List<Hand> _staff = new List<Hand>();

        /// <summary>How far they will wander from where they started.</summary>
        private const float Leash = 7f;

        /// <summary>How long a spell of walking lasts, and how long a spell of standing.</summary>
        private const int WalkMinMs = 9000;
        private const int WalkMaxMs = 20000;
        private const int StandMinMs = 8000;
        private const int StandMaxMs = 18000;

        /// <summary>
        /// Standing about: hanging out, on a phone, and two of them talking.
        ///
        /// Every pair checked against the game's own animation list. The chat one is the same
        /// clip the spooner scenes use for a group talking, which is what a back room full of
        /// people who are not currently carrying anything actually looks like.
        /// </summary>
        private static readonly string[][] Standing =
        {
            new[] { "anim@heists@narcotics@funding@gang_chat", "gang_chatting_combined" },
            new[] { "amb@world_human_hang_out_street@female_hold_arm@idle_a", "idle_a" },
            new[] { "amb@world_human_hang_out_street@female_arms_crossed@idle_a", "idle_a" },
            new[] { "amb@world_human_stand_mobile@female@text@idle_a", "idle_a" },
            new[] { "amb@world_human_hang_out_street@male_a@idle_a", "idle_b" },
            new[] { "amb@world_human_stand_mobile@male@standing@call@idle_a", "idle_a" }
        };

        private static readonly Random Dice = new Random();
        private bool _busy;

        /// <summary>
        /// Where he was stood when he pressed the key, and which way he was facing.
        ///
        /// The way out is the way in, rather than the coordinate in the ini. Those are usually
        /// the same spot to within a stride, and the ini value is still the fallback for a
        /// reload -- but "put him back where he was" is the thing that is actually meant, and
        /// saying it outright means the exit cannot drift away from the entrance.
        /// </summary>
        private Vector3 _cameFrom;
        private float _cameFacing;

        /// <summary>
        /// Where he was actually PUT inside, which is not always where the ini says.
        ///
        /// This is the grow room bug. The recorded coordinate for that room is twenty-one
        /// metres outside its own volume, so entering corrects it to the room's own origin and
        /// stands him there -- and then the way out was measured against the ini coordinate he
        /// had just been moved off. The prompt to leave was therefore twenty-one metres away,
        /// through a wall, at the one spot in the area that is not inside the room. Walk to it
        /// and you are stood outside the building at its real place on the map, which is
        /// exactly what it looked like from the outside: a door that dumps you at the docks.
        /// </summary>
        private Vector3 _standing;

        /// <summary>When the warp finished, so the watchdog does not fire during streaming.</summary>
        private int _enteredAt;

        public InteriorDoor(DoorSpec spec)
        {
            _spec = spec;
        }

        private Vector3 Door => new Vector3(_spec.DoorX, _spec.DoorY, _spec.DoorZ);
        private Vector3 Inside => new Vector3(_spec.InsideX, _spec.InsideY, _spec.InsideZ);

        /// <summary>The mark to leave from: where he was actually stood, or the ini's guess.</summary>
        private Vector3 Mark => _standing == Vector3.Zero ? Inside : _standing;

        /// <summary>The way back out: the doorway he used, or the ini's door after a reload.</summary>
        /// <summary>
        /// Where leaving puts you: the doorway you came in by, and the ini's door when nothing
        /// remembered one.
        ///
        /// AND THE DOOR WINS WHEN THE TWO DISAGREE. What is remembered is read off the player
        /// as he steps in, so a reload, a mission that moved him, or a session that ended
        /// inside the room can leave a coordinate from somewhere else entirely -- and coming
        /// out of the grow room into the middle of Vinewood is worse than coming out a stride
        /// from where the ini says the door is. Anything further than the doorway slack is
        /// treated as a stale reading rather than a way home.
        /// </summary>
        private Vector3 Back
        {
            get
            {
                if (_cameFrom == Vector3.Zero) return Door;
                return _cameFrom.DistanceTo(Door) > DoorwaySlack ? Door : _cameFrom;
            }
        }

        private float BackFacing => _cameFrom == Vector3.Zero ? _spec.DoorHeading : _cameFacing;

        public bool IsInside => _inside;

        /// <summary>
        /// Whether this door is yours yet.
        ///
        /// Set by Main. The grow room and the pill press are the set's places, not public
        /// ones, and a fresh save that opens with both of them marked tells somebody who has
        /// not met anybody yet that they own two buildings.
        /// </summary>
        public Func<bool> Known;

        public void Update()
        {
            if (_busy) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            NoticeHesInThere(player);

            if (Known != null && !Known())
            {
                try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
                catch { /* it is gone */ }

                _blip = null;
                return;
            }

            EnsureBlip();

            if (_inside)
            {
                Mill(Game.GameTime);

                if (WanderedOut(player)) return;

                Ring(Mark, player);

                if (player.Position.DistanceTo(Mark) > ExitRange) return;

                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to leave the " + _spec.Name + ".");

                if (Game.IsControlJustPressed(Control.Context)) Leave(player);
                return;
            }

            // On foot. Driving a car into a warehouse you reached by teleport leaves the car
            // where it was and you inside without it, which reads as a bug even when it is not.
            if (player.IsInVehicle()) return;
            Ring(Door, player);

            if (player.Position.DistanceTo(Door) > DoorRange) return;

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to go into the " + _spec.Name + ".");

            if (Game.IsControlJustPressed(Control.Context)) Enter(player);
        }

        /// <summary>
        /// Which of the coordinates this room is actually at.
        ///
        /// Asked of the game rather than trusted from the file. GET_INTERIOR_AT_COORDS answers
        /// for any point on the loaded map whether or not anybody is near it, so the whole list
        /// can be tried in one frame for nothing -- and the first one with an interior in it is
        /// the room. The one from the ini goes first, so a coordinate somebody has actually
        /// measured always wins over the fallbacks.
        ///
        /// When none of them has anything, the ini's own coordinate is handed back anyway and
        /// the checks after the warp do what they always did: bounce him out and say so. A
        /// silent wrong answer is the one outcome worth ruling out.
        /// </summary>
        private Vector3 Somewhere()
        {
            var first = Inside;

            try
            {
                if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, first.X, first.Y, first.Z) != 0) return first;

                foreach (var maybe in _spec.Elsewhere)
                {
                    if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, maybe.X, maybe.Y, maybe.Z) == 0) continue;

                    Log.Info("The " + _spec.Name + " is not at " + first + " -- using " + maybe +
                             ", which has one. Put that in [" + _spec.Section + "] Inside to keep it.");

                    return maybe;
                }
            }
            catch
            {
                // The ini's own coordinate, and the checks below will judge it.
            }

            return first;
        }

        /// <summary>
        /// In. Fade, load, warp, check, fade back.
        ///
        /// The check is the important part. The interior coordinate is a guess until somebody
        /// stands in there and reads it off the HUD, and a wrong guess without a check is the
        /// player under the map in the dark with no way out but a reload.
        /// </summary>
        private void Enter(Ped player)
        {
            _busy = true;

            // Taken before anything moves him, because this is what "back outside" means. He
            // is within a stride of the door -- the prompt does not appear otherwise -- so this
            // is the doorway, read off him rather than typed into a file.
            _cameFrom = player.Position;
            _cameFacing = player.Heading;

            try
            {
                Fade(false);

                // EVERY NAME, not one. Interior IPLs are named for where the game PLACES
                // them rather than for the room -- the weed farm's archetype is called
                // bkr_biker_dlc_int_ware02 and its placement is a forty-character string with
                // an index in the middle -- and asking for the archetype does nothing at all
                // while looking exactly like asking for the right thing. Semicolons in the ini
                // separate them; asking for a name the game does not have costs nothing.
                // THE MULTIPLAYER MAP, FIRST, and this is the thing four rounds of guessing
                // missed. These shells are ONLINE content. In a story game they are not merely
                // unloaded, they are not in the map at all, so REQUEST_IPL has nothing to find
                // and every coordinate under the sea reads as empty -- which is exactly what
                // the sweep came back with: thirty-four thousand points, not one interior.
                //
                // ON_ENTER_MP swaps the map over to the online one. It is what the interior
                // loader on this machine was doing all along, under a setting called
                // "load mp maps on refresh", and it is why the door worked while that mod ran
                // and stopped the day it did not. Nothing about this mod ever changed.
                Mp(true);

                foreach (var name in (_spec.Ipl + ";" + _spec.Extra).Split(';'))
                {
                    var one = name.Trim();
                    if (one.Length == 0) continue;

                    Function.Call(Hash.REQUEST_IPL, one);

                    // Said out loud, because "the room is not there" and "the name is wrong"
                    // are the same sentence from the pavement and different jobs to fix.
                    Log.Info("Asked for ipl '" + one + "' for the " + _spec.Name + ".");
                }

                var to = Somewhere();

                // Asked for BEFORE the warp, so the streamer has the whole fade to work in
                // rather than being told about the room only once somebody is standing in it.
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, to.X, to.Y, to.Z);
                Function.Call(Hash.NEW_LOAD_SCENE_START_SPHERE, to.X, to.Y, to.Z, 40f, 0);

                // Frozen across the warp, and this is the fix.
                //
                // A warp puts him at a coordinate whether or not anything is loaded there, and
                // an unfrozen ped in empty space starts falling on the very next frame -- so by
                // the time the room streams in he is already below it, which is the falling
                // through the sky. Frozen, he waits where he was put.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, true);

                player.Position = to;
                player.Heading = _spec.InsideHeading;

                // Pin the interior so the game does not decide the room is not worth streaming
                // while we are stood in the middle of it.
                var interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, to.X, to.Y, to.Z);

                if (interior != 0)
                {
                    Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
                    Function.Call(Hash.SET_INTERIOR_ACTIVE, interior, true);
                    Dress(interior);
                    Function.Call(Hash.REFRESH_INTERIOR, interior);

                    // Where the room ACTUALLY is, asked of the game rather than read off a
                    // coordinate somebody typed into an ini.
                    //
                    // This is what the grow room needed. The log said it plainly:
                    //
                    //   ipl bkr_biker_dlc_int_ware02 active=False,
                    //   interior=235521, he is in interior=0
                    //
                    // An interior IS registered at the recorded coordinate -- that is the
                    // 235521 -- but standing on that exact point put him OUTSIDE its volume,
                    // with no collision under him and no room around him. Near enough to find
                    // the room, not near enough to be in it, which is the one failure a
                    // hand-taken reading produces and the one a person cannot debug by looking.
                    //
                    // The interior knows its own origin. Offset zero from it is the middle of
                    // the room, which is somewhere a man can stand.
                    var origin = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_INTERIOR_IN_WORLD_COORDS,
                                                        interior, 0f, 0f, 0f);

                    if (origin != Vector3.Zero && origin.DistanceTo(to) < OriginTrust)
                    {
                        Log.Info("The " + _spec.Name + " is really at " + origin +
                                 ", not " + to + " -- using the room's own origin.");
                        to = origin;
                    }
                }

                // Waited ON rather than waited OUT.
                //
                // This was a flat two and a half seconds, which is a guess at how long a room
                // takes to arrive -- too long on a machine that had it cached and, on the run
                // that produced this bug, not long enough. Now it asks: is there collision
                // round him yet, and is the interior itself ready. It gives up at a ceiling
                // rather than hanging, because a fade that never lifts is worse than a room
                // that never arrives.
                var waited = 0;
                var iplOn = false;
                var inRoom = 0;

                while (waited < StreamCeilingMs)
                {
                    Wait(StreamStepMs);
                    waited += StreamStepMs;

                    if (interior == 0)
                    {
                        interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, to.X, to.Y, to.Z);

                        if (interior != 0)
                        {
                            Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
                            Function.Call(Hash.SET_INTERIOR_ACTIVE, interior, true);
                            Dress(interior);
                        }
                    }

                    // Re-asked rather than asked once. REQUEST_IPL is a request and not a
                    // load -- it returns immediately whether or not anything happened, and the
                    // old code fired it once and warped on the very next line. If the IPL is
                    // still not active after all this waiting it gets asked again, because a
                    // dropped request looks exactly like a slow one from in here.
                    // A ROOM WITH NO IPL IS NOT A ROOM THAT FAILED. Most of the mission
                    // interiors are in the story map already and simply have no way in; they
                    // need a door and nothing else. Asking IS_IPL_ACTIVE about an empty name
                    // answers no forever, and the failure message would then blame a missing
                    // name for a room that was never waiting on one.
                    iplOn = _spec.Ipl.Trim().Length == 0 ||
                            Function.Call<bool>(Hash.IS_IPL_ACTIVE, _spec.Ipl);

                    if (!iplOn && waited % 1000 == 0)
                    {
                        Function.Call(Hash.REQUEST_IPL, _spec.Ipl);
                    }

                    var solid = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY,
                                                    player.Handle);

                    var ready = interior == 0 ||
                                Function.Call<bool>(Hash.IS_INTERIOR_READY, interior);

                    var scene = Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_LOADED);

                    // Where he IS, not what is at the coordinate. The two disagree when the
                    // room exists but he has not landed inside its volume, which is the
                    // difference between a room that failed to load and a coordinate that
                    // points at the wrong side of one of its walls.
                    inRoom = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle);

                    if (solid && ready && scene && inRoom != 0) break;
                }

                Function.Call(Hash.NEW_LOAD_SCENE_STOP);

                // Written every time, not only on failure. This is the one thing in the mod
                // that cannot be worked out from the outside: "I fell through the floor" is the
                // same sentence whether the IPL never loaded, the interior is not at that
                // coordinate, or the coordinate is inside a wall. These four numbers tell those
                // three apart, and without them the next attempt is another guess.
                Log.Info("Entering " + _spec.Name + ": ipl " + _spec.Ipl + " active=" + iplOn +
                         ", interior=" + interior + ", he is in interior=" + inRoom +
                         ", waited " + waited + "ms");

                if (waited >= StreamCeilingMs)
                {
                    Log.Warn("The " + _spec.Name + " did not settle within " + StreamCeilingMs +
                             "ms (ipl active=" + iplOn + ", in interior=" + inRoom + ").");
                }

                if (interior == 0)
                {
                    Log.Warn("No interior at " + to + " for " + _spec.Ipl +
                             "; the coordinate in Hoodrich.ini is wrong.");

                    player.Position = Back;
                    player.Heading = BackFacing;
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false);

                    Sack();

                    // Bounced out because the room was not there: the map goes back too,
                    // or a failed attempt leaves the whole city on the online variant for
                    // nothing at all.
                    Mp(false);

                    Wait(400);
                    Fade(true);

                    // WHICH OF THE THREE, because they have different fixes and the log
                    // already knows the answer. An IPL that never went active is a wrong or
                    // dead IPL NAME; an IPL that loaded with nothing at the coordinate is a
                    // wrong Inside. Telling somebody to check the coordinate when the name is
                    // what is broken sends them to re-measure a number that was always right.
                    Notify.Problem(iplOn
                        ? "that room ain't there. check [" + _spec.Section + "] Inside in the ini."
                        : "ipl '" + _spec.Ipl + "' never loaded. check [" + _spec.Section + "] Ipl in the ini.");
                    return;
                }

                // The floor the GAME reports, not the one somebody typed into the ini.
                //
                // A hand-entered Z is a reading taken once by standing in the room, and it is
                // the single number here that cannot be checked from outside the game. Now the
                // geometry has streamed, the game will say where the floor really is -- so ask
                // it, and stand him on that. Believed only within a sane band of the recorded
                // height: a probe that comes back with the street thirty metres up has found
                // the wrong surface entirely, and following it would put him on the pavement.
                float floorZ;
                var floorFound = World.GetGroundHeight(new Vector3(to.X, to.Y, to.Z + FloorProbeUp),
                                                       out floorZ, GetGroundHeightMode.Normal);

                if (floorFound && Math.Abs(floorZ - to.Z) <= FloorProbeBand)
                {
                    player.Position = new Vector3(to.X, to.Y, floorZ + 0.05f);
                }
                else
                {
                    Log.Warn("No floor under " + to + " in the " + _spec.Name +
                             " (probe found=" + floorFound + ", z=" + floorZ.ToString("0.00") +
                             "); standing him on the ini height instead.");
                }

                // Nothing under him and not in a room: that is open air, and letting go of him
                // here IS the falling through the sky. Better to come back out of the door he
                // went in by and be told why than to be dropped into the void under the map.
                if (!floorFound && inRoom == 0)
                {
                    Log.Warn("The " + _spec.Name + " is not there: no floor and no interior at " +
                             to + ". Check [" + _spec.Section + "] Inside in the ini.");

                    player.Position = Back;
                    player.Heading = BackFacing;
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false);

                    Sack();

                    // Bounced out because the room was not there: the map goes back too,
                    // or a failed attempt leaves the whole city on the online variant for
                    // nothing at all.
                    Mp(false);

                    Wait(400);
                    Fade(true);

                    Notify.Problem("that room ain" + "'" + "t loading. check [" + _spec.Section +
                                   "] Inside in the ini.");
                    return;
                }

                _inside = true;

                // Where he ended up, not where the ini said to put him. Everything about
                // getting out again is measured from here.
                _standing = player.Position;
                _enteredAt = Game.GameTime;

                // Let go last, with the floor under him and the screen still black.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false);

                Wait(200);
                Fade(true);

                // He is in and the room is real: staff it.
                Hire(to);

                Notify.Ticker("~g~" + Capital(_spec.Name) + ".~s~");
            }
            catch (Exception ex)
            {
                Log.Error("Could not enter the " + _spec.Name, ex);

                // Unfrozen FIRST. Anything thrown after the freeze would otherwise leave
                // him unable to move for the rest of the save, with the fade lifting onto a
                // man who cannot walk -- worse than the failure that caused it.
                try { Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false); }
                catch { /* nothing else to try */ }

                try { player.Position = Back; } catch { /* nothing else to try */ }
                Fade(true);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Realises he is standing in the room even though nothing here put him there.
        ///
        /// THIS IS THE DESERT. Being inside is a bool in memory, and memory is exactly what a
        /// script reload and a loaded save both throw away -- so a man who walked into the pill
        /// press and then reloaded scripts, or saved and came back, was stood in the room with
        /// the mod certain he was on the street. No prompt, no way out but the room's own
        /// opening, and these rooms are shells parked in the empty quarter of the map with
        /// nothing around them. Walk out of one and you are in the desert, miles from Strawberry
        /// and with no idea how you got there.
        ///
        /// So the room is asked rather than remembered. If the game says he is standing in our
        /// interior, he is inside, and the door works again. The way back out is the ini's
        /// street coordinate, because the doorway he actually used went with the same memory --
        /// but the ini's door is a real place on a real street, which is the whole point.
        /// </summary>
        private void NoticeHesInThere(Ped player)
        {
            if (_inside) return;
            if (_busy) return;

            // Cheap first: nothing to work out unless he is somewhere no street is.
            if (player.Position.DistanceTo(Inside) > OriginTrust) return;

            int room;

            try
            {
                room = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle);
            }
            catch
            {
                return;
            }

            if (room == 0) return;

            // THE ROOM HAS TO BE THIS ROOM. The two rooms the mod ships are sixty-seven
            // metres apart under the same patch of ground, and by distance alone the far end
            // of one is within reach of the other's mark: walk to the back of the pill press
            // and the grow room adopted you, put up its own leave prompt beside the pill
            // press's, and its way out is Lamar's roller door. The game knows which interior
            // he is stood in; the only question worth asking is whether it is ours.
            int mine;

            try
            {
                mine = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, Inside.X, Inside.Y, Inside.Z);
            }
            catch
            {
                return;
            }

            if (mine == 0 || mine != room) return;

            _inside = true;
            _standing = player.Position;
            _enteredAt = Game.GameTime;

            // The furniture too, in case whatever put him here did not bring it.
            Dress(room);

            // Deliberately NOT cleared. If a reload happened while he was inside, the doorway
            // he came in by is already gone -- but if this fires for any other reason and we
            // still have it, it is better than the ini.
            Log.Info("Found him already in the " + _spec.Name +
                     "; the door works again, out to " + Back + ".");
        }

        /// <summary>
        /// Whether he has got out of the room some way other than the door he came in by.
        ///
        /// These interiors are real places on the map, sitting forty metres under it, and their
        /// own openings lead into nothing. Walking out of one leaves a man stood in the dark at
        /// the far end of the map with the mod still believing he is in the grow room -- which
        /// reads as the teleport having gone wrong rather than as him having walked somewhere.
        ///
        /// Out of the interior but still where the interior is means exactly that, and he is
        /// put back at the shutter. Out of it and nowhere near it means something else moved
        /// him -- Pillbox, a cell, another script -- and then the right thing is to forget he
        /// was ever in there rather than to drag him across the map.
        /// </summary>
        private bool WanderedOut(Ped player)
        {
            if (_enteredAt != 0 && Game.GameTime - _enteredAt < SettleGraceMs) return false;

            int room;

            try
            {
                room = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle);
            }
            catch
            {
                return false;
            }

            if (room != 0) return false;

            var away = player.Position.DistanceTo(Mark);

            // A doorway reads as no interior for a step or two. Not acted on until he is
            // properly clear of it.
            if (away < DoorwaySlack) return false;

            // Out of the room but still in the empty quarter it is parked in.
            //
            // The old rule was that anything past sixty metres was somebody else having moved
            // him, and it let go. That is right for Pillbox and a cell, and completely wrong
            // for the case that actually happens: these shells sit in unused map space with no
            // roads and no ground worth standing on, so walking out of one puts you a couple of
            // hundred metres into nothing. Letting go there abandons him in the desert instead
            // of taking him home, which is the bug reported as "it puts me in the desert".
            //
            // A real place he could have been moved TO is a long way further off than that.
            if (away > ShellYard)
            {
                Log.Info("Out of the " + _spec.Name + " and " + (int)away +
                         "m from it; something else moved him, so it is forgotten.");

                _inside = false;
                _standing = Vector3.Zero;
                return true;
            }

            if (away > OriginTrust)
            {
                Log.Info("Wandered " + (int)away + "m out of the " + _spec.Name +
                         " into the empty ground round it; taking him back to the street.");

                Leave(player);
                return true;
            }

            Log.Info("Walked out of the " + _spec.Name + " itself; putting him back at the door.");

            Leave(player);
            return true;
        }

        /// <summary>
        /// The stock and the equipment, switched on.
        ///
        /// NOT IPLS. A business interior is a shell with its furniture stored inside it as
        /// named ENTITY SETS -- the plants, the lights, the press, the tables -- and a set is
        /// turned on with ACTIVATE_INTERIOR_ENTITY_SET on the interior, not asked for with
        /// REQUEST_IPL. The first attempt asked for every one of these as an IPL, which is a
        /// request the game answers by doing nothing, and both rooms came up as bare shells
        /// with three people standing in them.
        ///
        /// Groups of alternatives, because the names came from two mods' string tables and
        /// the pattern between them. Within a group the first name the game accepts wins and
        /// the rest are left alone, so a wrong guess costs nothing and two versions of the
        /// same plant are never both drawn. What took and what did not is logged by name, so
        /// the list can be corrected against what the game actually has rather than argued
        /// about.
        /// </summary>
        private void Dress(int interior)
        {
            if (interior == 0 || string.IsNullOrEmpty(_spec.Sets)) return;

            var on = new List<string>();
            var off = new List<string>();

            foreach (var group in _spec.Sets.Split(';'))
            {
                var took = false;

                foreach (var raw in group.Split('|'))
                {
                    if (took) break;

                    var name = raw.Trim();
                    if (name.Length == 0) continue;

                    try
                    {
                        Function.Call(Hash.ACTIVATE_INTERIOR_ENTITY_SET, interior, name);

                        if (Function.Call<bool>(Hash.IS_INTERIOR_ENTITY_SET_ACTIVE, interior, name))
                        {
                            on.Add(name);
                            took = true;
                        }
                        else
                        {
                            off.Add(name);
                        }
                    }
                    catch
                    {
                        off.Add(name);
                    }
                }
            }

            try
            {
                Function.Call(Hash.REFRESH_INTERIOR, interior);
            }
            catch
            {
                // The sets are on either way; the refresh only hurries the props along.
            }

            Log.Info("Dressed the " + _spec.Name + " (interior " + interior + "): " + on.Count +
                     " set" + (on.Count == 1 ? "" : "s") + " on" +
                     (on.Count > 0 ? " -- " + string.Join(", ", on) : "") +
                     (off.Count > 0 ? "; not in this room: " + string.Join(", ", off) : "") + ".");
        }

        /// <summary>
        /// Out, to the doorway he came in by.
        ///
        /// Not to the coordinate in the ini. They are the same place in the ordinary case, and
        /// the ini is still what a reload falls back on -- but the room does not decide where
        /// the street is, and putting him back exactly where he was standing is the only
        /// version of this that cannot come out somewhere else.
        /// </summary>
        private void Leave(Ped player)
        {
            _busy = true;

            try
            {
                Fade(false);

                player.Position = Back;
                player.Heading = _cameFrom != Vector3.Zero && _cameFrom.DistanceTo(Door) <= DoorwaySlack
                    ? BackFacing
                    : _spec.DoorHeading;

                // He is outside again: the crew knock off and the city goes back to the
                // map the story happens in.
                Sack();
                Mp(false);

                Log.Info("Out of the " + _spec.Name + " to " + Back +
                         (_cameFrom == Vector3.Zero
                              ? " (the ini's door -- nothing remembered the way in)"
                              : _cameFrom.DistanceTo(Door) > DoorwaySlack
                                  ? " (the ini's door -- what was remembered was " +
                                    (int)_cameFrom.DistanceTo(Door) + "m from it)"
                                  : " (the way he came in)"));

                _inside = false;
                _standing = Vector3.Zero;

                Wait(400);
                Fade(true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not leave the " + _spec.Name, ex);
                Fade(true);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Put somebody to work, around wherever he came in.
        ///
        /// AROUND THE ARRIVAL MARK RATHER THAN AT AUTHORED SPOTS. Nobody has walked these
        /// rooms with a notebook, and a coordinate typed for the inside of a room nobody has
        /// stood in is the mistake this whole feature has already made twice. The mark is
        /// known-good floor -- he is standing on it -- so the crew go in a small arc off it
        /// and the worst case is somebody working a little close to a shelf.
        /// </summary>
        private void Hire(Vector3 at)
        {
            if (_spec.Crew.Count == 0) return;

            // Far enough not to be stood in his face, near enough to be the same room.
            var spread = new[]
            {
                new Vector3(2.6f, 1.4f, 0f),
                new Vector3(-2.2f, 2.6f, 0f),
                new Vector3(0.6f, -2.8f, 0f)
            };

            for (var i = 0; i < _spec.Crew.Count; i++)
            {
                var who = _spec.Crew[i];
                if (string.IsNullOrEmpty(who)) continue;

                try
                {
                    var model = new Model(who);
                    if (!model.IsValid || !model.IsInCdImage) continue;

                    model.Request(2000);
                    if (!model.IsLoaded) continue;

                    var spot = at + spread[i % spread.Length];

                    var worker = World.CreatePed(model, spot);
                    model.MarkAsNoLongerNeeded();

                    if (worker == null || !worker.Exists()) continue;

                    worker.IsPersistent = true;
                    worker.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, worker.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, worker.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, worker.Handle, false);

                    // There is one Families female model in the game and two of the three are
                    // her, so the game's own clothing shuffle does the work a second model
                    // would have done. Without it the room has twins in it.
                    Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, worker.Handle, 0);

                    // Facing the middle, so three people are working AT something rather than
                    // stood in a row facing a wall.
                    worker.Heading = Toward(spot, at);

                    var hand = new Hand { Who = worker };
                    _staff.Add(hand);

                    // Half of them set off, half of them stand. Otherwise three people arrive
                    // and all three start walking on the same frame, which is a shift change
                    // rather than a room somebody has been in for an hour.
                    if (Dice.Next(2) == 0) Walk(hand, at);
                    else Stand(hand);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put somebody to work in the " + _spec.Name + ": " + ex.Message);
                }
            }

            // ---- and the ones at work ----
            //
            // Each on the spot that was measured for them, facing the way the reading said,
            // doing the job the animation says. They do not mill: a cook stands at the
            // cooker for as long as you are in the room, which is what a cook does.
            foreach (var post in _spec.Posts)
            {
                if (post == null || string.IsNullOrEmpty(post.Model)) continue;

                try
                {
                    var model = new Model(post.Model);
                    if (!model.IsValid || !model.IsInCdImage) continue;

                    model.Request(2000);
                    if (!model.IsLoaded) continue;

                    var spot = new Vector3(post.X, post.Y, post.Z);
                    var worker = World.CreatePed(model, spot);
                    model.MarkAsNoLongerNeeded();

                    if (worker == null || !worker.Exists()) continue;

                    worker.IsPersistent = true;
                    worker.BlockPermanentEvents = true;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, worker.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, worker.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, worker.Handle, false);
                    Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, worker.Handle, 0);
                    Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, worker.Handle,
                                  spot.X, spot.Y, spot.Z, false, false, false);
                    worker.Heading = post.Heading;

                    var hand = new Hand { Who = worker, Posted = true, Dict = post.Dict, Clip = post.Clip };
                    _staff.Add(hand);

                    Function.Call(Hash.REQUEST_ANIM_DICT, post.Dict);

                    for (var n = 0; n < 40 && !Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, post.Dict); n++)
                    {
                        Script.Yield();
                    }

                    Work(hand);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not post somebody in the " + _spec.Name + ": " + ex.Message);
                }
            }

            if (_staff.Count > 0) Log.Info(_staff.Count + " working in the " + _spec.Name + ".");
        }

        /// <summary>Off round the room, on a leash so nobody wanders into the map.</summary>
        private static void Walk(Hand hand, Vector3 around)
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, hand.Who.Handle);
                Function.Call(Hash.TASK_WANDER_IN_AREA, hand.Who.Handle,
                              around.X, around.Y, around.Z, Leash, 3f, 8f);
                Function.Call(Hash.SET_PED_KEEP_TASK, hand.Who.Handle, true);
            }
            catch
            {
                // He stays where he is, which is the other half of what this does anyway.
            }

            hand.Walking = true;
            hand.Until = Game.GameTime + Dice.Next(WalkMinMs, WalkMaxMs);
        }

        /// <summary>At their station, doing their job, unless they already are.</summary>
        private static void Work(Hand hand)
        {
            if (string.IsNullOrEmpty(hand.Dict) || string.IsNullOrEmpty(hand.Clip)) return;

            try
            {
                if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, hand.Who.Handle, hand.Dict, hand.Clip, 3))
                {
                    return;
                }

                Function.Call(Hash.REQUEST_ANIM_DICT, hand.Dict);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, hand.Dict)) return;

                Function.Call(Hash.TASK_PLAY_ANIM, hand.Who.Handle, hand.Dict, hand.Clip,
                              8f, -8f, -1, 1, 0f, false, false, false);
            }
            catch
            {
                // Stood at the station is still stood at the station.
            }
        }

        /// <summary>Stopped, doing something with their hands.</summary>
        private static void Stand(Hand hand)
        {
            var pick = Standing[Dice.Next(Standing.Length)];

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, hand.Who.Handle);
                Function.Call(Hash.REQUEST_ANIM_DICT, pick[0]);

                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pick[0]))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, hand.Who.Handle, pick[0], pick[1],
                                  8f, -8f, -1, 1, 0f, false, false, false);
                }
            }
            catch
            {
                // Standing there is still standing there.
            }

            hand.Walking = false;
            hand.Until = Game.GameTime + Dice.Next(StandMinMs, StandMaxMs);
        }

        /// <summary>
        /// Each of them changes their mind now and then, on their own clock.
        ///
        /// Separate timers on purpose. One timer for all three is three people who stop and
        /// start together, which is choreography -- and choreography is the thing that says
        /// these are not people.
        /// </summary>
        private void Mill(int now)
        {
            for (var i = _staff.Count - 1; i >= 0; i--)
            {
                var hand = _staff[i];

                if (hand.Who == null || !hand.Who.Exists()) { _staff.RemoveAt(i); continue; }
                if (now < hand.Until) continue;

                // Somebody at a station keeps their job. Looked at every few seconds and
                // only started again if something knocked them out of it.
                if (hand.Posted)
                {
                    hand.Until = now + 4000;
                    Work(hand);
                    continue;
                }

                if (hand.Walking) Stand(hand);
                else Walk(hand, hand.Who.Position);
            }
        }

        /// <summary>Everybody out. Called on every way out of the room, including the failures.</summary>
        private void Sack()
        {
            foreach (var hand in _staff)
            {
                try
                {
                    if (hand.Who != null && hand.Who.Exists()) hand.Who.Delete();
                }
                catch
                {
                    // Already gone.
                }
            }

            _staff.Clear();
        }

        /// <summary>The heading from one point to another, in degrees.</summary>
        private static float Toward(Vector3 from, Vector3 to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;

            return (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI);
        }

        /// <summary>
        /// The online map on or off.
        ///
        /// PUT BACK ON THE WAY OUT. The online map is not only these shells: it changes doors,
        /// props and whole buildings around the city, and leaving a story game running on it
        /// because somebody looked in a grow room is not a trade anybody agreed to. It stays on
        /// while he is inside -- taking it away then would delete the room out from under him.
        /// </summary>
        /// <summary>
        /// A ring on the floor where a door is, the way the online game marks a way in.
        ///
        /// The prompt only appears within two paces, and a way in that is invisible until
        /// you are stood on it is a way in nobody finds twice. The ring is drawn from thirty
        /// metres, at the feet rather than at the coordinate, which is where the player's
        /// middle was when it was read.
        /// </summary>
        private static void Ring(Vector3 at, Ped player)
        {
            if (player.Position.DistanceTo(at) > RingRange) return;

            try
            {
                Function.Call(Hash.DRAW_MARKER, 1, at.X, at.Y, at.Z - 1.0f,
                              0f, 0f, 0f, 0f, 0f, 0f,
                              1.1f, 1.1f, 0.35f,
                              126, 232, 122, 105,
                              false, false, 2, false, 0, 0, false);
            }
            catch
            {
                // A door without its ring is still a door.
            }
        }

        private const float RingRange = 30f;

        private static void Mp(bool on)
        {
            // NOT BACK TO THE STORY MAP while the block is being held on the online one. Both
            // doors are on the block, so a door switching the map off on its way out would
            // turn LD Organics back into a garage every time you left a room.
            if (!on && HomeMap.On) return;

            try
            {
                Function.Call(on ? Hash.ON_ENTER_MP : Hash.ON_ENTER_SP);
                Log.Info("Map switched to the " + (on ? "online" : "story") + " one.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not switch the map: " + ex.Message);
            }
        }

        private static void Fade(bool inwards)
        {
            try
            {
                if (inwards) Function.Call(Hash.DO_SCREEN_FADE_IN, FadeMs);
                else
                {
                    Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs);
                    Wait(FadeMs);
                }
            }
            catch
            {
                // A hard cut is survivable. Being stuck on black is not, so a failed fade OUT
                // never stops the warp, and the fade IN is attempted regardless.
            }
        }

        private static void Wait(int ms)
        {
            var until = Game.GameTime + ms;
            while (Game.GameTime < until) Script.Yield();
        }

        private void EnsureBlip()
        {
            if (!_spec.Blip)
            {
                if (_blip != null && _blip.Exists()) { _blip.Delete(); _blip = null; }
                return;
            }

            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(Door);
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = _spec.Sprite;
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.8f;
                _blip.IsShortRange = true;

                // Big map only. SET_BLIP_DISPLAY 2 keeps it out of the minimap while leaving
                // it on the pause map -- a door you have to know about is not the same as a
                // door the corner of your screen keeps pointing at.
                Function.Call(Hash.SET_BLIP_DISPLAY, _blip.Handle, 2);
                _blip.Name = Capital(_spec.Name);
            }
            catch
            {
                // A blip is a nicety.
            }
        }

        /// <summary>Sentence case, for a blip name and a ticker.</summary>
        private static string Capital(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        public void RestoreWorld()
        {
            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* teardown */ }

            _blip = null;

            // The IPL is left loaded on purpose. Unloading an interior the player might be
            // standing in is a far worse ending than a warehouse nobody is looking at.
        }
    }
}
