using System;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Vernon. Calls himself OG Vee. Nobody else does.
    ///
    /// He is stood against the wall by his dad's old shop door on Strawberry Ave, and the whole
    /// of him is one joke told straight: a man who inherited an electrical business, put a
    /// cocaine operation in the basement of it, and thinks the interesting half of that
    /// sentence is that he raps.
    ///
    /// HE IS NOT A DEALER AND HE IS NOT A FIXER. The mod already has both, several times over,
    /// and a fourth man selling weight would be a fourth menu. Vernon wants an AUDIENCE. The
    /// work is what he offers you so you will stand there long enough to hear the second verse.
    ///
    /// HE IS ALSO THE LOCK ON THE BASEMENT. The door beside him goes into the grow, and it does
    /// not open until his job is done -- so he is not decoration on a door that already worked,
    /// he is the reason the door does anything. See Main, where his mission id is handed to the
    /// InteriorDoor as its gate.
    ///
    /// Modelled on the old San Andreas rapper who could not rap, deliberately and without ever
    /// naming him. The joke only works if it is played completely straight: he is not winking
    /// at you, he genuinely thinks the bars are good, and everything he says about them has to
    /// be said like a man who believes it.
    /// </summary>
    internal sealed class Vernon
    {
        /// <summary>
        /// Against the wall by the shop door, facing out at the street.
        ///
        /// Stood on and read off the screen, like both ends of the door beside him. His heading
        /// is very nearly the door's own reversed, which is what "back to the wall" means -- so
        /// he is leaning on the shutter he grew up behind rather than stood in the middle of
        /// the dirt with his arms folded.
        /// </summary>
        private static readonly Vector3 Spot = new Vector3(51.057f, -1452.677f, 29.312f);
        private const float Heading = 51.272f;

        private const float SpawnRange = 90f;
        private const float DespawnRange = 160f;
        private const float TalkRange = 3.0f;

        /// <summary>
        /// How far his greeting carries, which is further than a conversation does.
        ///
        /// TALKING TO A MAN IS THREE METRES. SHOUTING AT ONE IS NOT. The first time he sees
        /// you he is not waiting to be spoken to, he is spotting a face he knows across the
        /// front of the shop and going off like a firework about it -- and a man you have to
        /// walk up to and stand on the toes of before he notices you is not that man.
        /// </summary>
        private const float ShoutRange = 8.0f;

        /// <summary>
        /// Where he stands in the basement, and which way he faces while he does it.
        ///
        /// HE SAYS "I'M RIGHT BEHIND YOU" AND THEN HE WAS NOT. Everything about this man was
        /// one coordinate on Strawberry -- spawned at it, settled back onto it, despawned at a
        /// hundred and sixty metres from it -- so walking down his stairs put you in an empty
        /// warehouse, with the whole tour, the whole pitch and the whole job waiting on a
        /// conversation with somebody who was still outside leaning on a wall eight hundred
        /// metres away.
        ///
        /// PLACED BY EYE, LIKE THE ARMOURER'S CRATES. The floor height is the room's own origin
        /// -- the game reported it while the door was arguing about where to put people -- and
        /// the facing looks back at the bottom of the stairs, so he is watching you come down.
        /// Send a HUD readout from where he should actually stand and he moves.
        /// </summary>
        private static readonly Vector3 DownSpot = new Vector3(147.397f, -2201.349f, 3.602f);
        private const float DownHeading = 101.1f;

        /// <summary>
        /// Where he is standing when he comes out of the shop.
        ///
        /// STOOD ON AND READ OFF A HUD, like everything else out here, and it is the pavement
        /// outside the door rather than a point worked out from it -- the door's own coordinate
        /// is the frame, and a man made in a doorway is a man made inside whoever just used it.
        /// </summary>
        private static readonly Vector3 OutSpot = new Vector3(46.763f, -1451.599f, 29.314f);
        private const float OutHeading = 292.388f;

        /// <summary>Near enough to his post to stop walking and start standing.</summary>
        private const float PostedRange = 1.4f;

        /// <summary>How long before a walk order that has fallen off is given again.</summary>
        private const int WalkAgainMs = 4000;

        /// <summary>Standing in his own basement, which is not a wall to lean on.</summary>
        private static readonly string[] Posted =
            { "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_STAND_MOBILE" };

        private const int UpdateIntervalMs = 700;

        /// <summary>
        /// The model, and the cutscene copy under it.
        ///
        /// ig_vernon is the man himself; csb_vernon is the same face built for a cutscene and is
        /// there for an install that is missing the first. Both checked against the machine's
        /// own ped list rather than remembered.
        /// </summary>
        private static readonly string[] Models = { "ig_vernon", "csb_vernon" };

        /// <summary>
        /// Leaning on the wall having a cigarette.
        ///
        /// WORLD_HUMAN_AA_SMOKE is the one scenario in the game of somebody with their back
        /// against a wall and a smoke in their hand -- it is what the alleys off Vespucci are
        /// full of -- and it is the picture asked for. WORLD_HUMAN_SMOKING under it stands the
        /// same man upright in the same place, which is the honest fallback: a lean needs a
        /// wall behind the ped and there is no way to check for one.
        ///
        /// Both names off the machine's own scenario list. Neither is suffixed _UPRIGHT, which
        /// is the suffix that means "do this one WITHOUT leaning" -- so the un-suffixed name is
        /// the one that leans, which is the way round it always catches people out.
        /// </summary>
        private static readonly string[] Leaning = { "WORLD_HUMAN_AA_SMOKE", "WORLD_HUMAN_SMOKING" };

        private readonly PlayerState _state;

        /// <summary>
        /// 497 -- radar_production_crack, the razor blade over lines of powder.
        ///
        /// NOT 51. That is radar_crim_drugs, and it draws as a capsule -- the reference and the
        /// ini both called it "the razor and the line" and both were wrong, which is how he
        /// stood there wearing a pill. 497 is the cocaine-lockup mark from the Bikers business
        /// and it is the one picture the game has of what is actually in his basement.
        ///
        /// THE ONLY MARK ON THAT CORNER. The door beside him used to carry one too, on the
        /// same spot; the man you walk up to is the thing worth pointing at, so the door's is
        /// off and this is on the minimap as well as the big map.
        /// </summary>
        private const int Sprite = 497;

        private Ped _ped;

        /// <summary>Out on a job, so the wall does not despawn him and nothing here tasks him.</summary>
        private bool _lent;

        /// <summary>Downstairs rather than on the wall. See Basement.</summary>
        private bool _down;
        private int _walkedAt;

        /// <summary>Out of the door and on his way back to the wall. See OutAfterYou.</summary>
        private bool _returning;
        private Blip _blip;
        private int _lastUpdate;
        private bool _held;
        private bool _talkHeld;
        private int _leaning;

        /// <summary>
        /// True from HoldForTalk to ReleaseFromTalk. Update re-settles him on the wall
        /// whenever he is not held, and a conversation is the one time he is off the wall on
        /// purpose -- without this the 700ms tick would have him leaning again mid-sentence.
        /// </summary>
        private bool _talking;

        public Vernon(PlayerState state)
        {
            _state = state;
        }

        public string Name => "Vernon";
        public Vector3 Position => Spot;
        public Ped Ped => _ped != null && _ped.Exists() ? _ped : null;

        /// <summary>Set by Main.</summary>
        public Conversation Talk;
        public Func<DialogueNode> TalkBuilder;

        /// <summary>
        /// Set by Main: true while something else has the context button.
        ///
        /// He stands a stride and a half from the door into the basement, and the door uses the
        /// same button he does. Without this, walking up to the shop offers you a conversation
        /// and a doorway at once and the button picks one of them.
        /// </summary>
        public Func<bool> Suppressed;

        /// <summary>
        /// Set by Main: whether the map is allowed to point at him yet.
        ///
        /// The opening of this mod is one man and one icon, and it was two icons and a razor
        /// blade: his mark went up the moment the script loaded, next to Hao's lot, before
        /// Gerald had put anything in your hand. He is still on the wall -- the man is there
        /// whether or not the map says so -- it is only the mark that waits.
        /// </summary>
        public Func<bool> Known;

        /// <summary>Set by Main: whether the player is down in the basement. See Basement.</summary>
        public Func<bool> Inside;

        /// <summary>Set by Main: where the stairs put you, so he can arrive the same way.</summary>
        public Func<Vector3> Landing;

        /// <summary>Set by Main: whether his job is on, so he gets in rather than leaning.</summary>
        public Func<bool> OnTheJob;

        /// <summary>Set by Main: his Dorado, for the passenger seat. See Board.</summary>
        public Func<Vehicle> Ride;

        /// <summary>Set by Main: the job is done and he is waiting to be handed it. See UpdatePrompt.</summary>
        public Func<bool> HandingIn;

        public bool InReach => Within(TalkRange);

        /// <summary>Near enough to be shouted at. See ShoutRange.</summary>
        private bool InEarshot => Within(ShoutRange);

        private bool Within(float metres)
        {
            if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return false;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return false;

            return player.Position.DistanceTo(_ped.Position) <= metres;
        }

        // ---- per frame ---------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            if (Known == null || Known()) EnsureBlip(); else DropBlip();

            Keyed();

            // OUT ON THE JOB, SO THE WALL LETS GO OF HIM. Everything below is about a man
            // stood in one place in Strawberry -- it despawns him at a hundred and sixty
            // metres and settles him back into his scenario whenever he drifts -- and all of
            // it is wrong for a man riding to La Puerta in the passenger seat. See Lend.
            // ---- DOWNSTAIRS IS A PLACE HE CAN BE ----
            //
            // Checked before the wall, because the wall's rules would despawn him for being
            // eight hundred metres from Strawberry -- which, stood in a warehouse in Banning
            // with the player beside him, he is.
            var below = Inside != null && Inside();

            if (below && !_lent) { Basement(); return; }

            // ---- BACK UP THE STAIRS, AND THIS RUNS EVEN WHEN THE JOB HAS HIM ----
            //
            // THE JOB TAKES HIM WHILE HE IS STILL DOWN THERE. You accept in the basement, the
            // mission starts on the next tick and asks for him -- see Lend -- and from that
            // moment the wall's rules are skipped entirely. So the man the job was holding was
            // a man stood in a warehouse in Banning, and the first thing the job does is tell
            // him to walk to your car: eight hundred metres away, up a staircase that does not
            // exist from where he is standing.
            //
            // MOVED, NOT REMADE, for the same reason: the job is holding THAT ped. Despawning
            // it and making a new one out here leaves the mission pointing at a dead handle.
            if (_down && !below)
            {
                _down = false;
                Out();
                return;
            }

            // ---- STILL TRYING TO GET IN, AND THIS IS AHEAD OF THE LENT RETURN ----
            //
            // THE CAR IS NOT THERE ON THE FRAME HE WALKS OUT. It is a ParkedCar and you have
            // just been eight hundred metres away in Banning, so it was despawned and its own
            // tick is on a one-and-a-half second throttle -- which means asking it for a
            // Vehicle at the top of the stairs answers null, Board gives up, and he goes and
            // leans on his wall while the job waits for a passenger who is never coming.
            //
            // So it is not one attempt. He keeps asking until the car exists and he is in it.
            if (_boarding) { Boarding(); return; }

            // ---- AND ARMED AGAIN WHEN YOU GET TO THE CAR ----
            //
            // The walk out of the shop is not the only time he has to get in. He can be talked
            // to on the way, give up waiting, be left on the wall while you go and find a
            // different car and come back -- and in all of those the job is still on and he is
            // still meant to be in the passenger seat. So walking up to the Dorado with the job
            // running arms it again, wherever he happens to be standing.
            if (Wanted(player)) { Boarding(); return; }

            // OUT ON THE JOB, SO THE WALL LETS GO OF HIM. Everything below is about a man
            // stood in one place in Strawberry -- it despawns him at a hundred and sixty
            // metres and settles him back into his scenario whenever he drifts -- and all of
            // it is wrong for a man riding to La Puerta in the passenger seat. See Lend.
            if (_lent) return;

            var away = player.Position.DistanceTo(Spot);

            if (away > DespawnRange)
            {
                Despawn();
                return;
            }

            if (away > SpawnRange) return;

            if (_ped == null || !_ped.Exists())
            {
                if (_returning) OutAfterYou();
                else Spawn();
            }
            else if (_returning) Returning();
            else if (!_held && !_talking) Settle();
        }

        private void Spawn()
        {
            if (!Make(Spot, Heading, "on the wall at Leroy's")) return;

            Settle();
        }

        /// <summary>
        /// Him, at a coordinate, with everything about him switched on.
        ///
        /// Split out of Spawn because there are two places he stands now and only one of them
        /// is a wall. Every flag here is the same either way -- he is a fixture, not a
        /// pedestrian, and a fixture that can be shot at, panicked, knocked over or wandered
        /// off is not one.
        /// </summary>
        private bool Make(Vector3 at, float facing, string where)
        {
            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _ped = World.CreatePed(model, at, facing);
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

                    Log.Info("Vernon is " + where + " (" + name + ") at " + at + ".");
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put Vernon out: " + ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// In the basement with you: down the stairs behind you and over to his post.
        ///
        /// HE ARRIVES WHERE YOU ARRIVED. The landing is the one coordinate down here anybody
        /// has stood on and proved walkable -- the door puts the player on it every time -- so
        /// he is made a stride into the room from it and walks the rest himself. Made at his
        /// post instead he would simply BE there, which is a man who was always in the room
        /// rather than a man who followed you into it.
        ///
        /// AND HE WALKS RATHER THAN BEING PLACED because the walk is the line. "Go down them
        /// stairs, I'm right behind you" is either true or it is a man lying on a pavement.
        /// </summary>
        private void Basement()
        {
            if (_ped == null || !_ped.Exists() || !_ped.IsAlive || !_down)
            {
                // Whatever the street was holding lets go of him first: one Vernon.
                Despawn();

                var from = DownSpot;

                if (Landing != null)
                {
                    try
                    {
                        var top = Landing();

                        // A stride into the room off the landing, so he is not stood inside
                        // the player who just used it.
                        if (top != Vector3.Zero) from = top + Forward(DownHeading) * -1.2f;
                    }
                    catch { /* the post itself, then */ }
                }

                if (!Make(from, DownHeading, "down in the basement")) return;

                _down = true;
                _held = false;

                WalkTo(DownSpot, DownHeading);
                return;
            }

            if (_talking || _held) return;

            if (_ped.Position.DistanceTo(DownSpot) <= PostedRange) { Post(); return; }

            // Still on his way. A task that has fallen off him -- and they do -- looks exactly
            // like a man standing still halfway across his own basement.
            if (Game.GameTime - _walkedAt > WalkAgainMs) WalkTo(DownSpot, DownHeading);
        }

        /// <summary>
        /// Out of the shop door and back to his wall.
        ///
        /// MADE AT THE DOORWAY, which is the one coordinate out here somebody has certainly
        /// just stood on -- the door puts the player on it coming out. It is a stride and a
        /// half from the wall, so this is a short walk and it is meant to be: the point is that
        /// he is coming OUT, not that he is going far.
        /// </summary>
        private void OutAfterYou()
        {
            if (!Make(OutSpot, OutHeading, "out of the door after you"))
            {
                _returning = false;
                return;
            }

            _held = false;

            WalkTo(Spot, Heading);

            Keys();
        }

        /// <summary>
        /// What he says coming out of the door behind you: the car is round the side.
        ///
        /// SAID ON THE WAY OUT, NOT IN A PANEL. A full-screen conversation to deliver one
        /// sentence while both of you are walking is a screen that stops the walk it is
        /// describing -- and this line exists to point at something, which is a thing a man does
        /// over his shoulder.
        ///
        /// ONLY WHILE HE STILL OWES YOU THE JOB. Afterwards there is nothing to drive anywhere
        /// for, and a man announcing his car every time you come up from listening to his tape
        /// is a man you would stop visiting.
        /// </summary>
        private void Keys()
        {
            if (_state != null && _state.HasDone(Locations.VernonTalk.JobId)) return;

            _keysAt = Game.GameTime + KeysAfterMs;
        }

        /// <summary>The one line. See Keys.</summary>
        private const string RoundTheSide =
            "Aight -- we takin' my Dorado, she round the side. And Franklin? Mind them rims.";

        /// <summary>A breath after the door, so it is not said over the fade. See Keys.</summary>
        private const int KeysAfterMs = 1200;

        private int _keysAt;

        /// <summary>Said once the pause is up, if he is still out here to say it. See Keys.</summary>
        private void Keyed()
        {
            if (_keysAt == 0 || Game.GameTime < _keysAt) return;

            _keysAt = 0;

            if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return;

            try { Core.Voice.Say("vernon", RoundTheSide, null, true); }
            catch { /* the subtitle still carries it */ }

            try { GTA.UI.Screen.ShowSubtitle(Core.Lang.T("~y~VERNON:~s~ " + RoundTheSide), 5000); }
            catch { /* the audio still carries it */ }
        }

        /// <summary>
        /// Out of the shop, and then one of two things.
        ///
        /// FOR THE JOB HE GETS IN; OTHERWISE HE GOES BACK TO HIS WALL. They are the only two
        /// reasons he has ever been through that door, and which one it is is a question the
        /// mission can answer -- see OnTheJob.
        /// </summary>
        private void Out()
        {
            if (_ped == null || !_ped.Exists() || !_ped.IsAlive)
            {
                // Nothing to move. The wall makes one out here in a moment. See OutAfterYou.
                _returning = true;
                return;
            }

            try
            {
                _ped.Task.ClearAll();
                _ped.Position = OutSpot;
                _ped.Heading = OutHeading;
            }
            catch
            {
                // Wherever he is, then, which is at least the right side of the door.
            }

            _held = false;

            Log.Info("Vernon is out of the shop at " + OutSpot + ".");

            Keys();

            // ARMED RATHER THAN DECIDED. Whether the car is there yet is not a question to
            // answer once, on the worst frame to ask it. See Boarding.
            if (OnTheJob != null && OnTheJob())
            {
                _boarding = true;
                _boardingFrom = Game.GameTime;

                Board();
                return;
            }

            _returning = true;
            WalkTo(Spot, Heading);
        }

        /// <summary>
        /// Waiting for his own car to exist, and getting in it when it does.
        ///
        /// GIVES UP EVENTUALLY, because a man stood on a pavement reaching for a door handle
        /// that is not there is worse than a man who walked back to his wall. The job can still
        /// pick him up off the wall afterwards -- Deal.Vee puts him in whatever YOU get into --
        /// so this failing is a lost bit of staging rather than a broken mission.
        /// </summary>
        private void Boarding()
        {
            if (_ped == null || !_ped.Exists() || !_ped.IsAlive) { _boarding = false; return; }

            // In, and that is that. The job has him from here.
            if (_ped.IsInVehicle())
            {
                _boarding = false;
                _held = true;
                return;
            }

            // The job ended while he was walking to it.
            if (OnTheJob == null || !OnTheJob())
            {
                _boarding = false;
                _returning = true;

                WalkTo(Spot, Heading);
                return;
            }

            if (Game.GameTime - _boardingFrom > BoardingMs)
            {
                _boarding = false;
                _returning = true;

                Log.Info("Vernon gave up getting in the Dorado; going back to the wall.");

                WalkTo(Spot, Heading);
                return;
            }

            Board();
        }

        /// <summary>
        /// Whether he ought to be getting in right now: the job is on, he is out here, he is
        /// not already in something, and you are stood at the car.
        /// </summary>
        private bool Wanted(Ped player)
        {
            if (OnTheJob == null || Ride == null) return false;
            if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return false;
            if (_talking) return false;

            try
            {
                if (!OnTheJob()) return false;
                if (_ped.IsInVehicle()) return false;

                var car = Ride();
                if (car == null || !car.Exists()) return false;

                if (player.Position.DistanceTo(car.Position) > AtTheCar) return false;

                _boarding = true;
                _boardingFrom = Game.GameTime;
                _boardedAt = 0;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Near enough to the car that you are plainly about to get in it.</summary>
        private const float AtTheCar = 14f;

        /// <summary>How long he keeps reaching for a door before he goes back to the wall.</summary>
        private const int BoardingMs = 40000;

        private bool _boarding;
        private int _boardingFrom;

        /// <summary>The car he has already been told to get into, so he is not told twice.</summary>
        private int _boardedAt;

        /// <summary>
        /// Into the passenger seat of his own car, if that is what this is.
        ///
        /// SEAT NOUGHT IS THE ONE BESIDE THE DRIVER. He is not driving -- he says so himself --
        /// and he is not sitting in the back of his own Dorado like a man being taken somewhere.
        /// The job puts him into whatever car you get into after this, see Deal.Vee; this is
        /// only about which one he walks to on the way out of the door, and the answer is his,
        /// because he has just finished telling you that is what you are taking.
        /// </summary>
        private bool Board()
        {
            if (OnTheJob == null || Ride == null) return false;

            try
            {
                if (!OnTheJob()) return false;

                var car = Ride();

                // Not made yet. He asks again on the next pass -- see Boarding.
                if (car == null || !car.Exists()) return false;

                // ALREADY ASKED, AND ASKING AGAIN RESTARTS THE WALK. A task given every pass is
                // a man who steps towards the door, is told to approach the door, and steps
                // towards it again for ever.
                if (_boardedAt == car.Handle && Game.GameTime - _boardingFrom < BoardingMs) return true;

                Function.Call(Hash.TASK_ENTER_VEHICLE, _ped.Handle, car.Handle, 20000, 0, 2f, 1, 0);

                _boardedAt = car.Handle;
                _returning = false;

                Log.Info("Vernon is getting in the Dorado for the job.");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>On his way to the wall. Gets there, or is talked to on the way.</summary>
        private void Returning()
        {
            if (_talking) { _returning = false; return; }

            if (_ped.Position.DistanceTo(Spot) <= PostedRange)
            {
                _returning = false;
                Settle();
                return;
            }

            if (Game.GameTime - _walkedAt > WalkAgainMs) WalkTo(Spot, Heading);
        }

        /// <summary>The way a heading points, so a spot can be measured off one.</summary>
        private static Vector3 Forward(float heading)
        {
            var r = heading * (float)(Math.PI / 180.0);

            return new Vector3(-(float)Math.Sin(r), (float)Math.Cos(r), 0f);
        }

        private void WalkTo(Vector3 where, float facing)
        {
            _walkedAt = Game.GameTime;

            try
            {
                _ped.Task.ClearAll();

                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, _ped.Handle,
                              where.X, where.Y, where.Z, 1.0f, -1, facing, 0.3f);
            }
            catch { /* he stands where he is, which is still somewhere */ }
        }

        /// <summary>Arrived: turned the right way and stood in it like he owns it.</summary>
        private void Post()
        {
            try
            {
                _ped.Task.ClearAll();
                _ped.Heading = DownHeading;

                foreach (var name in Posted)
                {
                    try
                    {
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle, name, 0, true);
                        break;
                    }
                    catch
                    {
                        // The next way of standing there.
                    }
                }

                _held = true;
            }
            catch { /* stood still is stood still */ }
        }

        /// <summary>
        /// Back on the wall.
        ///
        /// The scenario is asked for by NAME and the first one that takes wins, remembered so a
        /// re-settle after a conversation does not walk the list again -- an install where the
        /// lean is missing would otherwise pay for the failed call every time you walked away
        /// from him.
        /// </summary>
        private void Settle()
        {
            for (var i = _leaning; i < Leaning.Length; i++)
            {
                try
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle, Leaning[i], 0, true);

                    _leaning = i;
                    _held = true;
                    return;
                }
                catch
                {
                    // Try the next way of standing there.
                }
            }

            _held = false;
        }

        // ---- talking to him ----------------------------------------------------

        /// <summary>
        /// The prompt, and opening the conversation off it.
        ///
        /// He gets the button only when nothing else nearer wants it. See Suppressed: the
        /// basement door is a stride behind his shoulder.
        /// </summary>
        public void UpdatePrompt()
        {
            if (Talk == null) return;

            // The screen is up: his body follows it. See TickAct. And if it has gone down on
            // its own -- a choice that ends the talk closes the screen from inside -- this is
            // where he finds out, because nothing else tells him.
            if (Talk.IsOpen) { TickAct(); return; }
            if (_talking) ReleaseFromTalk();

            // ---- NOT WHILE HE IS ON THE JOB ----
            //
            // He sits a foot from your shoulder for the whole drive to La Puerta, which means
            // InReach is true for twenty minutes and the prompt was up for every one of them --
            // so a man riding to a deal he set up could be asked, at eighty miles an hour, if
            // he had any work going. And answer "still on the wall", from the passenger seat.
            //
            // Lend is the tell rather than the mission running: it is the moment the job takes
            // him off the wall, and it is already what every other rule in this file uses to
            // mean "he is not scenery at the moment".
            //
            // EXCEPT WHEN THE JOB IS WAITING ON THIS CONVERSATION, which is what I broke by
            // adding the line above. He is still lent when you get back to Strawberry -- the
            // job does not let go of him until it is handed in -- so suppressing the prompt for
            // the whole of the lend suppressed the one talk the job cannot finish without. You
            // stood in front of him holding his kilo with nothing to press.
            if (_lent && !Owed) return;

            if (Suppressed != null && Suppressed()) return;

            // ---- THE FIRST TIME, HE STARTS IT ----
            //
            // A stranger on a wall with a button floating over him is a quest marker. Vernon
            // shouting "Ayy -- FRANKLIN!" at you the second you get within three metres is a
            // man who has been waiting all day for somebody he knows to walk past, which is
            // exactly who he is and exactly what the first line says.
            //
            // ONCE. After he has introduced himself he is somebody you know, and somebody you
            // know does not restart his own conversation every time you walk past his shop --
            // that is a man you would begin to avoid. So from the second time on it is the
            // prompt and the button like everybody else in the mod.
            //
            // Knows is MetVernon, which Root sets as it builds the meeting -- so the handover
            // between the two happens on its own, in one place, and cannot disagree with the
            // greeting he actually gives.
            var greeting = !Knows && InEarshot && WillingToBeGreeted();

            if (!greeting)
            {
                // The button is still three metres, and it should be: a prompt you can read
                // from across the street is a prompt on a man you are walking past.
                if (!InReach) return;

                Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to talk to " +
                                   (Knows ? "Vernon" : "the man on the wall") + ".");

                if (!WantsToTalk()) return;
            }

            var root = TalkBuilder == null ? null : TalkBuilder();
            if (root == null) return;

            HoldForTalk();

            Talk.Speaker = _ped;

            // His thing, shaking on the panel while he talks: sixteen frames of a baggie,
            // split out of a GIF by tools/gif2frames.py. The first flipbook on the HUD, and
            // the test of the mechanism.
            Talk.Badge = "baggie";
            Talk.BadgeFrames = 16;
            Talk.BadgeMs = 130;

            Talk.Open(root, this);
        }

        /// <summary>Whether you have been introduced, which is the only thing the prompt knows.</summary>
        private bool Knows => _state != null && _state.MetVernon;

        /// <summary>
        /// Whether a conversation that nobody asked for would be welcome right now.
        ///
        /// EVERY GUARD HERE EXISTS BECAUSE THE BUTTON IS GONE. Walking up and pressing E is its
        /// own proof that a man wants to talk and has the hands free to do it; three metres is
        /// not. Three metres is also a car going past the shop, a man face down on the pavement
        /// after a ragdoll, and a firefight in the street outside -- and a full-screen
        /// conversation opening in the middle of any of those is the mod taking the controller
        /// off you.
        ///
        /// And not while he is out on the job: riding to La Puerta he sits a foot from your
        /// shoulder for the whole drive. See Lend.
        /// </summary>
        /// <summary>Whether he is stood there waiting to be handed the thing he paid for.</summary>
        private bool Owed
        {
            get
            {
                if (HandingIn == null) return false;

                try { return HandingIn(); }
                catch { return false; }
            }
        }

        private bool WillingToBeGreeted()
        {
            if (_lent) return false;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return false;

            if (player.IsInVehicle() || player.IsRagdoll || player.IsInAir) return false;
            if (player.IsInCombat || player.IsShooting || Game.Player.IsAiming) return false;

            return Game.Player.Wanted.WantedLevel <= 0;
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

        /// <summary>
        /// Off the wall and turned round to you.
        ///
        /// He is the one man in the mod where this costs something: the lean IS the character,
        /// and standing him up to talk loses it. It is still right -- a man delivering his own
        /// verses at a wall with his back to you is a different joke -- and Settle puts him
        /// straight back on it the moment the screen closes.
        /// </summary>
        public void HoldForTalk()
        {
            if (_ped == null || !_ped.Exists()) return;

            _held = false;
            _talking = true;
            _firstLine = true;
            _act = Act.None;
            _pending = null;

            if (Talk != null) Talk.Staged = OnNode;
            Warm();

            try
            {
                _ped.Task.ClearAll();
                Face();
            }
            catch
            {
                // He will still talk.
            }
        }

        public void ReleaseFromTalk()
        {
            if (!_talking && (_held || _ped == null || !_ped.Exists())) return;

            _talking = false;
            if (Talk != null && Talk.Staged == (Action<DialogueNode>)OnNode) Talk.Staged = null;

            Rest();

            if (_ped == null || !_ped.Exists()) return;
            Settle();
        }

        // ---- acting the line ---------------------------------------------------

        /// <summary>
        /// He does not stand still while he talks, and he does not stand still while he raps.
        ///
        /// THREE WAYS OF BEING ON. A spoken line gets a hand off the game's own street
        /// conversation set, picked off the words -- a question gets the open palms, a "you"
        /// gets the point at you, a "nah" gets the head shake -- and a line that keeps going
        /// gets another every few seconds while the recording runs. A verse gets a whole
        /// dance, full body, off the nightclub floor, for exactly as long as OG Vee is on the
        /// mic. And the moment the verse ends and the screen hands you the choices, the dance
        /// stops and he throws up the set and holds it, which is a man waiting to hear what
        /// you thought of it.
        ///
        /// Told which page is up by Conversation.Staged, so the words-to-movement lives here
        /// and the screen knows nothing about anybody's body. Every name below is in
        /// RampageFiles\Lists\PedAnimList.txt on this install; none of them is guessed.
        /// </summary>
        private enum Act { None, Talk, Rap, Flex }

        private const string GestureDict = "gestures@m@standing@casual";

        /// <summary>Upper body and secondary: the hands move and the feet do not. See Greeting.</summary>
        private const int GestureFlags = 48;

        /// <summary>Looping and full body. A dance is not a thing you do from the waist up.</summary>
        private const int DanceFlags = 1;

        /// <summary>Looping, upper body, secondary: the set held up over a man standing still.</summary>
        private const int SignFlags = 49;

        private const string SignDict = "mp_player_int_uppergang_sign_a";
        private const string SignClip = "mp_player_int_gang_sign_a";

        /// <summary>
        /// The nightclub's solo dances, one per verse so three verses are not one dance three
        /// times over. var_a and var_b are two different routines; med and high is how hard
        /// he goes. Which verse gets which is fixed off the words, so running one back gets
        /// the same moves.
        /// </summary>
        private static readonly string[][] Dances =
        {
            new[] { "anim@amb@nightclub@mini@dance@dance_solo@male@var_a@", "med_center" },
            new[] { "anim@amb@nightclub@mini@dance@dance_solo@male@var_b@", "med_center" },
            new[] { "anim@amb@nightclub@mini@dance@dance_solo@male@var_a@", "high_center" },
        };

        /// <summary>What his hands do when the words do not say. hello is kept for the first line.</summary>
        private static readonly string[] Hands =
        {
            "gesture_hand_left", "gesture_hand_right", "gesture_easy_now", "gesture_pleased",
            "gesture_shrug_soft", "gesture_bring_it_on", "gesture_point", "gesture_me"
        };

        /// <summary>The gap between hands on a line that keeps going, and the jitter on it.</summary>
        private const int HandGapMs = 2600;
        private const int HandJitterMs = 1400;

        /// <summary>
        /// A verse is not over in the frames before its recording has started. The sound
        /// device says "not playing" while it opens the file -- see Conversation.BeatOpenMs.
        /// </summary>
        private const int RapOpenMs = 600;

        /// <summary>How long a clip whose dictionary is still loading is retried for.</summary>
        private const int PendingMs = 2000;

        private Act _act;
        private int _actAt;
        private bool _heard;
        private bool _firstLine;
        private int _nextHandAt;
        private string _lastHand;
        private string[] _dance;
        private bool _signing;
        private string[] _pending;
        private int _pendingFlags;
        private int _pendingAt;
        private readonly Random _dice = new Random();

        /// <summary>
        /// Every dictionary he will need, asked for as the screen opens.
        ///
        /// REQUEST_ANIM_DICT is asynchronous and the first line goes up in the same frame as
        /// this, so the first gesture still usually misses -- Play says so and the miss is
        /// retried by TickAct for a moment. By the first verse everything is in.
        /// </summary>
        private void Warm()
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, GestureDict);
                Function.Call(Hash.REQUEST_ANIM_DICT, SignDict);
                foreach (var d in Dances) Function.Call(Hash.REQUEST_ANIM_DICT, d[0]);
            }
            catch
            {
                // He will act it stiffer.
            }
        }

        /// <summary>A page went up. See Conversation.Staged.</summary>
        private void OnNode(DialogueNode node)
        {
            if (node == null || _ped == null || !_ped.Exists()) return;
            if (Talk == null || !ReferenceEquals(Talk.Subject, this)) return;

            if (node.Speaker == VernonTalk.Stage) Rap(node.Line);
            else Say(node.Line);
        }

        private void Say(string line)
        {
            _pending = null;
            StopDance();
            StopSign();
            Face();

            _act = Act.Talk;
            _actAt = Game.GameTime;

            var hand = _firstLine ? "gesture_hello" : HandFor(line);
            _firstLine = false;

            Gesture(hand);
            _nextHandAt = Game.GameTime + HandGapMs + _dice.Next(HandJitterMs);
        }

        private void Rap(string verse)
        {
            _pending = null;
            StopSign();

            _act = Act.Rap;
            _actAt = Game.GameTime;
            _heard = false;

            var sum = 0;
            foreach (var c in verse) sum += c;
            var dance = Dances[sum % Dances.Length];

            if (_dance != null && (_dance[0] != dance[0] || _dance[1] != dance[1])) StopDance();

            if (Play(dance[0], dance[1], DanceFlags)) _dance = dance;
            else Later(dance, DanceFlags);
        }

        private void Flex()
        {
            _pending = null;
            StopDance();
            Face();

            _act = Act.Flex;
            _actAt = Game.GameTime;

            if (Play(SignDict, SignClip, SignFlags)) _signing = true;
            else Later(new[] { SignDict, SignClip }, SignFlags);
        }

        /// <summary>Every frame the screen is up. See UpdatePrompt.</summary>
        private void TickAct()
        {
            if (_act == Act.None || _ped == null || !_ped.Exists()) return;

            var now = Game.GameTime;

            if (_pending != null)
            {
                if (Play(_pending[0], _pending[1], _pendingFlags))
                {
                    if (_pendingFlags == DanceFlags) _dance = _pending;
                    else if (_pendingFlags == SignFlags) _signing = true;
                    _pending = null;
                }
                else if (now - _pendingAt > PendingMs)
                {
                    Log.Debug("Vernon's " + _pending[1] + " never loaded; acting it without.");
                    _pending = null;
                }
            }

            var talking = Voice.Talking;

            switch (_act)
            {
                case Act.Rap:
                    // For as long as the take runs. A verse with no recording yet keeps him
                    // dancing until you pick something, which is the right look for it.
                    if (talking) _heard = true;
                    else if (_heard && now - _actAt > RapOpenMs) Flex();
                    break;

                case Act.Talk:
                    if (now < _nextHandAt) break;

                    if (talking)
                    {
                        Gesture(HandFor(null));
                        _nextHandAt = now + HandGapMs + _dice.Next(HandJitterMs);
                    }
                    else
                    {
                        _nextHandAt = now + 500;
                    }
                    break;
            }
        }

        /// <summary>
        /// The hand for a line, off its words; or off the dice when the words do not say.
        ///
        /// The FRONT of the line decides, because that is the part being said when the hand
        /// goes up; the point at the basement is the one exception and reads the whole line,
        /// because a man mentions the basement wherever in the sentence it falls and points
        /// at it either way.
        /// </summary>
        private string HandFor(string line)
        {
            if (!string.IsNullOrEmpty(line))
            {
                var l = line.ToLowerInvariant();
                var head = l.Length > 40 ? l.Substring(0, 40) : l;

                if (l.TrimEnd().EndsWith("?")) return _dice.Next(2) == 0 ? "gesture_what_soft" : "gesture_why";
                if (l.Contains("basement") || l.Contains("downstairs") || l.Contains("down there")) return "gesture_point";
                if (head.Contains("nah") || head.Contains("no,") || head.Contains("don't") || head.Contains("never")) return "gesture_nod_no_soft";
                if (head.StartsWith("you ") || head.Contains(" you ")) return "gesture_you_soft";
                if (head.Contains("damn") || head.Contains("shit") || head.Contains("man,")) return "gesture_damn";
                if (head.Contains("yeah") || head.Contains("aight") || head.Contains("okay")) return "gesture_nod_yes_soft";
                if (head.Contains("i'm ") || head.Contains(" my ") || head.Contains(" me ")) return "gesture_me";
            }

            string pick;
            do pick = Hands[_dice.Next(Hands.Length)]; while (pick == _lastHand);
            return pick;
        }

        private void Gesture(string clip)
        {
            _lastHand = clip;
            if (!Play(GestureDict, clip, GestureFlags)) Later(new[] { GestureDict, clip }, GestureFlags);
        }

        /// <summary>
        /// One clip, now. False means the dictionary is not in yet and the caller should try
        /// again; see Later and TickAct.
        /// </summary>
        private bool Play(string dict, string clip, int flags)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dict);
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) return false;

                Function.Call(Hash.TASK_PLAY_ANIM, _ped.Handle, dict, clip,
                              4f, -4f, -1, flags, 0f, false, false, false);
                return true;
            }
            catch
            {
                // A call that throws will throw again; there is nothing to wait for.
                return true;
            }
        }

        private void Later(string[] pair, int flags)
        {
            _pending = pair;
            _pendingFlags = flags;
            _pendingAt = Game.GameTime;
        }

        private void StopDance()
        {
            if (_dance == null) return;

            try
            {
                if (_ped != null && _ped.Exists())
                {
                    Function.Call(Hash.STOP_ANIM_TASK, _ped.Handle, _dance[0], _dance[1], -4f);
                }
            }
            catch
            {
                // The next task replaces it anyway.
            }

            _dance = null;
        }

        /// <summary>
        /// The set down. A secondary loop is the one thing a scenario does NOT replace, so
        /// this has to happen before Settle or he leans on the wall still holding it up.
        /// </summary>
        private void StopSign()
        {
            if (!_signing) return;

            try
            {
                if (_ped != null && _ped.Exists())
                {
                    Function.Call(Hash.STOP_ANIM_TASK, _ped.Handle, SignDict, SignClip, -4f);
                }
            }
            catch
            {
                // See StopDance.
            }

            _signing = false;
        }

        private void Face()
        {
            try
            {
                var player = Game.Player.Character;
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _ped.Handle, player.Handle, -1);
                }
            }
            catch
            {
                // He talks to your shoulder.
            }
        }

        /// <summary>Everything off, for the wall or for a teardown.</summary>
        private void Rest()
        {
            _pending = null;
            _act = Act.None;

            if (_ped == null || !_ped.Exists())
            {
                _dance = null;
                _signing = false;
                return;
            }

            StopDance();
            StopSign();
        }

        // ---- map ---------------------------------------------------------------

        /// <summary>
        /// His mark on the wall, put up once and left alone.
        ///
        /// NAMED FOR WHAT YOU KNOW. Before you have met him it is the shop, because that is
        /// all it is from the street; after, it is him, because by then the shop is the least
        /// interesting thing about it.
        /// </summary>
        private void EnsureBlip()
        {
            if (_blip != null && _blip.Exists())
            {
                var should = Knows ? "Vernon" : "Leroy's Electrical";
                if (_blip.Name != should) _blip.Name = should;
                return;
            }

            try
            {
                _blip = World.CreateBlip(Spot);
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, Sprite);
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.8f;
                _blip.IsShortRange = true;
                _blip.Name = Knows ? "Vernon" : "Leroy's Electrical";
            }
            catch (Exception ex)
            {
                Log.Debug("No blip for Vernon: " + ex.Message);
            }
        }

        /// <summary>Takes the mark off the wall, for when you are not anybody yet.</summary>
        private void DropBlip()
        {
            if (_blip == null) return;

            try { if (_blip.Exists()) _blip.Delete(); }
            catch { /* it goes with the session */ }

            _blip = null;
        }

        // ---- teardown ----------------------------------------------------------

        /// <summary>
        /// Hands him to a job. Null if he is not stood there to be handed over.
        ///
        /// THE SAME LOAN LAMAR IS, and for the same reason -- see Fixer.Lend. He has one job
        /// in the mod and he goes to it, which is a decision the brief makes funny: a man who
        /// has told you he cannot leave the store, in the passenger seat, on the way to a drug
        /// deal he arranged on the shop phone.
        ///
        /// Nothing here follows him. The mission that borrowed him owns him until it gives him
        /// back, and Update stands down for the whole of it.
        /// </summary>
        public Ped Lend()
        {
            if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return null;

            _lent = true;
            _held = false;
            _talking = false;

            try
            {
                _ped.Task.ClearAll();

                // HE CAN BE SHOT AT NOW, because the whole point of him being there is that
                // three men turn up with rifles and go for both of you. On the wall he is
                // untargetable so a stray round on Strawberry Avenue cannot kill the only man
                // who knows where the basement key is.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _ped.Handle, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _ped.Handle, false);
            }
            catch
            {
                // The job is about to task him anyway.
            }

            return _ped;
        }

        /// <summary>
        /// Takes him back off a job.
        ///
        /// Near the shop he goes back to leaning on his wall. Anywhere else he is let go --
        /// walking him home across the city is a man the player would have to watch, and a
        /// fresh one is on that wall the next time they come round. Dead is the same case.
        /// </summary>
        public void TakeBack()
        {
            if (!_lent) return;

            _lent = false;

            if (_ped == null || !_ped.Exists() || !_ped.IsAlive)
            {
                Despawn();
                return;
            }

            try
            {
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _ped.Handle, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _ped.Handle, true);

                if (_ped.Position.DistanceTo(Spot) > DespawnRange)
                {
                    Despawn();
                    return;
                }

                _ped.Task.ClearAll();
                _ped.Position = Spot;
                _ped.Heading = Heading;
            }
            catch
            {
                Despawn();
            }
        }

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
        }

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
