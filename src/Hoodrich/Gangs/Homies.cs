using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Three of yours, on call.
    ///
    /// You spend the first three jobs riding with homies who are handed to you by whoever set
    /// the job, and then every hour after that on your own -- which is the wrong way round for
    /// somebody who has just been asked into a gang. Once you have been in a car with them and
    /// come back, you should be able to ask.
    ///
    /// They are put in the PLAYER'S OWN GROUP rather than tasked to follow, which is a much
    /// bigger difference than it sounds. The group is the machinery the game already uses for
    /// story buddies: they keep formation without being re-tasked, they fight what you fight,
    /// they get into the passenger seats of whatever you get into, and they find their own way
    /// back when they lose you. Anything hand-rolled out of TASK_FOLLOW would do the first of
    /// those and none of the rest.
    ///
    /// The three are deliberately NOT interchangeable. Each carries a different gun, one of
    /// them is permanently drunk and one of them is permanently smoking, so that after a
    /// minute of walking around with them you know which one is which without being told.
    /// Three identical men in a formation are furniture; three you can tell apart are a crew.
    /// </summary>
    internal sealed class Homies
    {
        /// <summary>How many turn up. A cab holds three and three is a car full.</summary>
        private const int Crew = 3;

        /// <summary>
        /// How far off they set out from -- round a corner, not behind a bin.
        ///
        /// Far enough that you cannot see the cab appear and near enough that you are not
        /// stood waiting. It is snapped to a road node from here, so the real distance is
        /// whatever the nearest street happens to be.
        /// </summary>
        private const float ComeFrom = 110f;

        /// <summary>Close enough to you for the cab to stop and let them out.</summary>
        private const float DropRange = 18f;

        /// <summary>And how long it gets to find you before they just get out and walk.</summary>
        private const int InboundGiveUpMs = 60000;

        /// <summary>How long the cab gets to actually stop before they climb out regardless.</summary>
        private const int HaltGiveUpMs = 14000;

        /// <summary>
        /// How long the cab stands still once it has stopped.
        ///
        /// SIX SECONDS, and it used to be one and a half. That was the bug: three men do not
        /// get out of a car in 1600 milliseconds, so the halt expired with somebody still
        /// halfway through the door.
        ///
        /// And a halt is only a pin. The long-range drive task that brought him here was never
        /// cancelled, so the moment the pin let go that task resumed -- with its destination
        /// being where the player is standing, which is where everybody had just got out. He
        /// pulled away through them, arrived at a coordinate he was already on top of, and then
        /// ground against it because there was nowhere left to drive to. Being hit and him
        /// getting stuck were the same fault twice.
        /// </summary>
        private const int CabWaitMs = 6000;

        /// <summary>What they turn up in.</summary>
        private static readonly string[] CabModels = { "taxi", "cavalcade", "premier" };
        private static readonly string[] CabbieModels =
        {
            "s_m_m_hairdress_01", "a_m_m_indian_01", "a_m_m_eastsa_01", "a_m_y_business_01"
        };

        /// <summary>How far they may drift before the group hauls them back.</summary>
        private const float LeashRange = 55f;

        /// <summary>
        /// How close the formation holds them.
        ///
        /// The group's default spacing is built for story buddies crossing open ground and it
        /// leaves them strung out over ten metres, which on a pavement means one of them is
        /// permanently in the road. A metre and a bit puts them at your shoulder.
        /// </summary>
        private const float FormationSpacing = 1.1f;
        private const float FormationMin = 0.4f;
        private const float FormationMax = 2.4f;

        private const int TickMs = 900;

        /// <summary>
        /// What they carry, one each, in this order.
        ///
        /// Not a random draw out of a pool -- three fixed guns, so the man on your left is
        /// always the rifle and you learn to place them by silhouette. A compact rifle, a
        /// micro SMG and a machine pistol is also a believable spread for people who bought
        /// what was going rather than what they wanted.
        /// </summary>
        private static readonly string[] Guns =
        {
            "WEAPON_COMPACTRIFLE", "WEAPON_MICROSMG", "WEAPON_MACHINEPISTOL"
        };

        /// <summary>
        /// The one who is always drunk.
        ///
        /// A movement clipset, not an animation -- it replaces his entire walk cycle, so he
        /// sways the whole time he is following you rather than doing a stumble on a timer.
        /// Verified against the game's own movement clipset list; "moderate" rather than
        /// "very", because verydrunk cannot keep up with a walking player and he would spend
        /// the day being teleported back by the group's leash.
        /// </summary>
        private const string DrunkWalk = "move_m@drunk@moderatedrunk";

        /// <summary>
        /// What each of them is always doing with his hands.
        ///
        /// One drinks, one smokes, one does neither -- and the point is that you can tell
        /// which is which from across the street without anybody saying a word. Every dict,
        /// clip and prop below is out of the game's own data rather than off a wiki; a
        /// misspelt clip does not throw, it silently plays nothing.
        /// </summary>
        private const string BeerDict = "amb@world_human_drinking@beer@male@idle_a";
        private const string BeerProp = "prop_amb_beer_bottle";

        private const string JointDict = "amb@world_human_smoking_pot@male@idle_a";
        private const string JointProp = "p_amb_joint_01";

        /// <summary>The one who is always smoking, and what he smokes.</summary>
        private const string CiggyProp = "prop_cs_ciggy_01";

        /// <summary>PH_R_Hand. The prop helper, so the cigarette sits where a hand holds it.</summary>
        private const int RightHandBone = 28422;

        /// <summary>
        /// Standing-around idles, for the one who is neither drunk nor smoking.
        ///
        /// Played UPPER BODY and SECONDARY (flag 48), which is the whole trick: his legs stay
        /// under the group's control, so he can be mid-gesture when you walk off and simply
        /// walks while finishing it. A full-body idle would win the argument with the
        /// formation and leave him standing in the street doing an animation at you.
        ///
        /// Every dict and clip below is out of the game's own animation dump rather than off
        /// a wiki. A misspelt clip name does not throw -- it silently plays nothing.
        /// </summary>
        private static readonly string[][] LoiterIdles =
        {
            new[] { "amb@world_human_hang_out_street@male_a@idle_a", "idle_a", "idle_b", "idle_c", "idle_d", "idle_e" },
            new[] { "amb@world_human_hang_out_street@male_b@idle_a", "idle_a", "idle_b", "idle_c", "idle_d" },
            new[] { "amb@world_human_hang_out_street@male_c@idle_a", "idle_a", "idle_b", "idle_c" },
            new[] { "amb@world_human_stand_impatient@male@no_sign@idle_a", "idle_a", "idle_b", "idle_c" },
            new[] { "amb@world_human_stand_mobile@male@text@idle_a", "idle_a", "idle_b", "idle_c" },
        };

        /// <summary>His, and only his. Looped, because he never finishes it.</summary>
        private const string SmokeDict = "amb@world_human_smoking@male@male_a@idle_a";
        private static readonly string[] SmokeClips = { "idle_a", "idle_b", "idle_c" };

        /// <summary>The three idle clips every one of these ambient sets is built from.</summary>
        private static readonly string[] Idles = { "idle_a", "idle_b", "idle_c" };

        /// <summary>
        /// What they say, and how often.
        ///
        /// Three men who follow you round Los Santos in total silence are three props on a
        /// lead. These are the game's own ambient speech labels rather than lines of ours --
        /// they come out in the ped's own voice, which is the entire reason to use them, and
        /// a label that does not exist on a given model is simply silent.
        /// </summary>
        private static readonly string[] Chatter =
        {
            "GENERIC_HI", "GENERIC_HOWS_IT_GOING", "GENERIC_YES", "GENERIC_CURSE_MED",
            "GENERIC_INSULT_HIGH", "CHAT_STATE", "GENERIC_WHATEVER", "GENERIC_THANKS"
        };

        /// <summary>Roughly this often, per man, jittered so they do not speak in chorus.</summary>
        private const int SayMinMs = 9000;
        private const int SayMaxMs = 22000;

        /// <summary>How long a gesture holds before another is picked.</summary>
        private const int IdleHoldMin = 6000;
        private const int IdleHoldMax = 13000;

        /// <summary>Below this they count as standing still and start fidgeting.</summary>
        private const float StillSpeed = 0.6f;

        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;

        private readonly List<Homie> _men = new List<Homie>();
        private readonly Random _rng = new Random();

        private int _next;

        public Homies(GangRegistry gangs, Affiliation crew, PlayerState state)
        {
            _gangs = gangs;
            _crew = crew;
            _state = state;
        }

        /// <summary>
        /// One of them, and the things that make him himself.
        ///
        /// The traits live here rather than in a lookup keyed on handle because a handle is
        /// reused the moment a ped is released, and a recycled handle inheriting somebody
        /// else's drunkenness is the kind of bug that takes an evening to find.
        /// </summary>
        private sealed class Homie
        {
            public Ped Ped;

            /// <summary>He sways. Set once, at the kerb.</summary>
            public bool Drunk;

            /// <summary>He smokes whenever he is not walking or shooting.</summary>
            public bool Smoker;

            /// <summary>Whatever is in his hand right now -- a bottle, a cigarette, a joint.</summary>
            public Prop Ciggy;

            /// <summary>Which the smoker is on. He alternates rather than picking at random.</summary>
            public bool OnTheJoint;

            /// <summary>When he next opens his mouth.</summary>
            public int NextWord;

            /// <summary>When the current gesture is allowed to be replaced.</summary>
            public int IdleUntil;

            /// <summary>Whether he is mid-gesture, so it is only cleared once.</summary>
            public bool Idling;

            /// <summary>Which prop model is in his hand, so a swap can be spotted.</summary>
            public string Holding;
        }

        /// <summary>
        /// The block, so somebody mentions it when you roll out with people.
        ///
        /// Named Feed rather than Social because a field called Social shadows the namespace
        /// it lives in, and Social.SocialEvent then resolves to the field instead of the type.
        /// </summary>
        public Hoodrich.Social.SocialFeed Feed;

        /// <summary>How many of yours are actually still standing.</summary>
        public int Standing
        {
            get
            {
                var n = 0;

                foreach (var man in _men)
                {
                    if (man.Ped != null && man.Ped.Exists() && man.Ped.IsAlive) n++;
                }

                return n;
            }
        }

        public bool AnyOut => Standing > 0;

        /// <summary>
        /// Whether you are allowed to ask at all.
        ///
        /// Unlocked by the job you first ride with them on, and gated on still being in the
        /// set -- they are the gang's people, not yours, and somebody who has walked away does
        /// not get to keep the phone number.
        /// </summary>
        /// <summary>
        /// What their thread is called, and the name every text from them is filed under.
        ///
        /// A const rather than a string typed in three places. Threading is by sender name, so
        /// one of those three drifting by a capital letter would quietly split them into two
        /// conversations -- and it would look like a bug in the app rather than a typo here.
        /// </summary>
        public const string ContactName = "Homies";

        /// <summary>Where their texts say they are. Their block, not yours.</summary>
        public const string ContactZone = "Chamberlain Hills";

        public bool Available =>
            _state != null && _state.HomiesUnlocked && _crew != null && _crew.IsAffiliated;

        // ---- calling them ------------------------------------------------------

        /// <summary>
        /// Sends for them. They arrive; they do not appear.
        ///
        /// Three men fading into existence behind your shoulder is the cheapest thing a mod
        /// can do and it reads as exactly what it is. They come in a cab from round the
        /// corner instead: it drives to you, it stops, they get out, it leaves. The whole
        /// thing costs about twenty seconds and it is the difference between summoning
        /// somebody and phoning them.
        ///
        /// Returns what to tell the player, or null if it worked.
        /// </summary>
        public string Call()
        {
            if (!Available) return "you ain't got nobody to call yet.";
            if (AnyOut || _inbound) return "they already on the way.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "not right now.";

            var gang = _crew.Current;
            if (gang == null || gang.MemberModels.Count == 0) return "nobody picked up.";

            // Asked for now so it is resident by the time the cab pulls up. The clipset cannot
            // be applied until it has loaded, and the drive over is free time to load it in.
            try { Function.Call(Hash.REQUEST_ANIM_SET, DrunkWalk); } catch { /* he walks straight */ }

            // A road, out of sight, in whatever direction happens to have one.
            var away = (float)(_rng.NextDouble() * Math.PI * 2d);
            var guess = player.Position
                        + new Vector3((float)Math.Cos(away), (float)Math.Sin(away), 0f) * ComeFrom;

            var start = World.GetNextPositionOnStreet(guess);
            if (start == Vector3.Zero) start = guess;

            if (!MakeCab(start, player.Position)) return "couldn't get nobody out here.";

            for (var i = 0; i < Crew; i++)
            {
                // Seat 0 is beside the driver, then the back. CREATE_PED_INSIDE_VEHICLE puts
                // them straight in rather than walking them to a door that is moving.
                var man = Make(gang, i, i == 0 ? 0 : i);
                if (man != null) _men.Add(man);
            }

            if (_men.Count == 0)
            {
                ScrapCab();
                return "nobody picked up.";
            }

            _inbound = true;
            _dropping = false;
            _calledAt = Game.GameTime;

            Log.Info("Homies inbound: " + _men.Count + " in a cab from " + start.ToString() + ".");

            if (Feed != null) Feed.On(Hoodrich.Social.SocialEvent.HomiesOut);

            return null;
        }

        private bool _inbound;
        private bool _dropping;
        private int _calledAt;
        private int _haltedAt;
        private Vehicle _cab;
        private Ped _cabbie;

        /// <summary>True while the cab is still on its way.</summary>
        public bool Inbound => _inbound;

        private bool MakeCab(Vector3 start, Vector3 to)
        {
            foreach (var name in CabModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _cab = World.CreateVehicle(model, start);
                    model.MarkAsNoLongerNeeded();

                    if (_cab == null || !_cab.Exists()) continue;

                    _cab.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _cab.Handle, true, true);
                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _cab.Handle);

                    _cabbie = MakeCabbie();

                    if (_cabbie == null)
                    {
                        try { _cab.Delete(); } catch { /* gone */ }
                        _cab = null;
                        continue;
                    }

                    // Longrange, because it may be several streets away and the short version
                    // gives up at a junction it does not like.
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                                  _cabbie.Handle, _cab.Handle, to.X, to.Y, to.Z,
                                  20f, 786603, DropRange * 0.5f);

                    return true;
                }
                catch (Exception ex)
                {
                    Log.Debug("No cab for the homies: " + ex.Message);
                }
            }

            return false;
        }

        private Ped MakeCabbie()
        {
            foreach (var name in CabbieModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var h = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                               _cab.Handle, 4, model.Hash, -1, true, false);
                    model.MarkAsNoLongerNeeded();
                    if (h == 0) continue;

                    var ped = Entity.FromHandle(h) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;
                    ped.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);
                    Function.Call(Hash.SET_DRIVER_ABILITY, h, 1f);

                    return ped;
                }
                catch (Exception ex)
                {
                    Log.Debug("No cabbie: " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>When the cab was sent off, so it can be let go once it is actually moving.</summary>
        private int _cabSentAt;

        /// <summary>How long it gets to pull away before we stop holding on to it.</summary>
        private const int CabGoneMs = 9000;

        /// <summary>
        /// Sends the cab away, and gives it somewhere to go.
        ///
        /// It was a straight TASK_VEHICLE_DRIVE_WANDER, and wander has no DESTINATION -- it
        /// picks a direction and negotiates out of wherever it happens to be standing. From a
        /// kerbside stop with the car it just dropped three men beside, that is a taxi rocking
        /// back and forth against the kerb for as long as anybody is looking at it. The
        /// delivery driver had exactly this and was given a road to get to first; so is this.
        ///
        /// Released on a timer rather than immediately, because a car handed back to
        /// population control the same frame it is tasked is a car the game may simply decide
        /// it no longer needs to drive.
        /// </summary>
        private void SendCabOff()
        {
            if (_cabbie == null || !_cabbie.Exists() || !_cabbie.IsAlive ||
                _cab == null || !_cab.Exists())
            {
                ScrapCab();
                return;
            }

            var slot = new OutputArgument();

            try
            {
                // LET GO OF THE CAR FIRST. This is what was still holding it.
                //
                // BRING_VEHICLE_TO_HALT pins a vehicle in place, and a pin is not a timer that
                // tidies up after itself -- the game keeps a halt on that vehicle until
                // somebody says otherwise, which is what STOP_BRINGING_VEHICLE_TO_HALT is for
                // and it was never called. So the drive task went out to a car the engine was
                // still being told to hold still, and the cab sat there revving at a road it
                // had been given perfectly good directions to.
                //
                // Worse, it was a dead heat by design: the halt is set for CabWaitMs and the
                // send fires the instant CabWaitMs has elapsed. Even if the pin did expire on
                // its own, this asks on the exact frame it would be doing it.
                try { Function.Call(Hash.STOP_BRINGING_VEHICLE_TO_HALT, _cab.Handle); }
                catch { /* older builds may not have it; the drive still goes out */ }

                // Somewhere up the road, snapped to an actual street.
                var ahead = _cab.Position + _cab.ForwardVector * 220f;
                var away = World.GetNextPositionOnStreet(ahead);
                if (away == Vector3.Zero) away = ahead;

                // AND NOT A DESTINATION HE IS ALREADY ON. The stop radius below is twelve
                // metres, so a node that snapped in close is a drive task that completes on
                // the frame it is given -- the exact fault the delivery driver had, where he
                // pulled out, went three metres and stopped dead. If there is nothing far
                // enough away, wandering has no destination and cannot be satisfied on the
                // spot, so he simply drives.
                var far = _cab.Position.DistanceTo(away) > 40f;

                _cabbie.Task.ClearAll();

                Function.Call(Hash.OPEN_SEQUENCE_TASK, slot);
                var seq = slot.GetResult<int>();

                if (far)
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, 0, _cab.Handle,
                                  away.X, away.Y, away.Z, 18f, 0, _cab.Model.Hash, 786603, 12f, 0f);
                }

                // And once it is out on a through road, wandering works.
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, 0, _cab.Handle, 18f, 786603);

                Function.Call(Hash.CLOSE_SEQUENCE_TASK, seq);
                Function.Call(Hash.TASK_PERFORM_SEQUENCE, _cabbie.Handle, seq);
            }
            catch (Exception ex)
            {
                Log.Debug("The cab would not pull away: " + ex.Message);
            }
            finally
            {
                try { Function.Call(Hash.CLEAR_SEQUENCE_TASK, slot); } catch { }
            }

            _cabSentAt = Game.GameTime;
        }

        /// <summary>Lets go of the cab once it has had time to actually drive off.</summary>
        private void ReleaseCab()
        {
            if (_cabSentAt == 0 || Game.GameTime - _cabSentAt < CabGoneMs) return;

            try
            {
                if (_cabbie != null && _cabbie.Exists())
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _cabbie.Handle, false);
                    _cabbie.IsPersistent = false;
                    _cabbie.MarkAsNoLongerNeeded();
                }

                if (_cab != null && _cab.Exists())
                {
                    _cab.IsPersistent = false;
                    _cab.MarkAsNoLongerNeeded();
                }
            }
            catch { /* it knows the way */ }

            _cab = null;
            _cabbie = null;
            _cabSentAt = 0;
        }

        /// <summary>And this one takes it away, for a cab nobody ever saw.</summary>
        private void ScrapCab()
        {
            try { if (_cabbie != null && _cabbie.Exists()) _cabbie.Delete(); } catch { }
            try { if (_cab != null && _cab.Exists()) _cab.Delete(); } catch { }

            _cab = null;
            _cabbie = null;
        }

        /// <summary>Sends them home. They walk off rather than vanishing.</summary>
        public string Dismiss()
        {
            if (!AnyOut && !_inbound) return "ain't nobody with you.";

            foreach (var man in _men)
            {
                try
                {
                    PutItOut(man);

                    var ped = man.Ped;
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);

                    if (ped.IsAlive)
                    {
                        // His own walk back, not the one we gave him. A released ped keeps
                        // whatever clipset he was left with, and a permanently drunk pedestrian
                        // wandering Strawberry for the rest of the session is our litter.
                        if (man.Drunk)
                        {
                            try
                            {
                                Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, ped.Handle, 0f);
                                Function.Call(Hash.SET_PED_IS_DRUNK, ped.Handle, false);
                            }
                            catch { /* he sobers up eventually */ }
                        }

                        Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, ped.Handle);

                        ped.Task.ClearAll();
                        WalkOff(ped);
                    }

                    // Released rather than deleted, for the same reason nothing else in this
                    // mod deletes a man you are looking at: they walk away, and the game takes
                    // them back once nobody is watching.
                    ped.IsPersistent = false;
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* they know the way */ }
            }

            _men.Clear();

            // If they never got here, the cab goes with them.
            if (_inbound) ScrapCab();

            _inbound = false;
            _dropping = false;

            Log.Info("Homies sent home.");

            return null;
        }

        /// <summary>
        /// Out of whatever he is sitting in, and then away on foot.
        ///
        /// A SEQUENCE, because a second task does not queue behind the first -- it replaces
        /// it. Tasking him to leave the car and then tasking him to wander on the next line
        /// throws the exit away before it starts, and a wander issued to a man in a passenger
        /// seat does nothing at all, so he simply stays there. Inside a sequence 0 means "the
        /// ped performing this", and each task genuinely waits for the one before it.
        /// </summary>
        private static void WalkOff(Ped ped)
        {
            var slot = new OutputArgument();

            try
            {
                Function.Call(Hash.OPEN_SEQUENCE_TASK, slot);
                var seq = slot.GetResult<int>();

                Function.Call(Hash.TASK_LEAVE_VEHICLE, 0, 0, 0);
                Function.Call(Hash.TASK_WANDER_STANDARD, 0, 10f, 10);

                Function.Call(Hash.CLOSE_SEQUENCE_TASK, seq);
                Function.Call(Hash.TASK_PERFORM_SEQUENCE, ped.Handle, seq);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a homie off: " + ex.Message);
            }
            finally
            {
                try { Function.Call(Hash.CLEAR_SEQUENCE_TASK, slot); } catch { }
            }
        }

        // ---- keeping them -------------------------------------------------------

        public void Update()
        {
            // THE CAB FIRST, because the cab outlives the crew.
            //
            // ReleaseCab sat below this guard, so a carload wiped out inside the nine seconds
            // between the drop-off and the hand-back left the taxi and its driver standing in
            // the road as persistent mission entities the game's own population control is
            // forbidden from touching -- permanently, for the session. That is the exact
            // failure Payback's own comment calls "a permanent roadblock, which is a bug this
            // mod has already been through once".
            ReleaseCab();

            if (_men.Count == 0) return;

            if (Game.GameTime < _next) return;
            _next = Game.GameTime + TickMs;

            if (_inbound)
            {
                Arriving();
                return;
            }

            // The dead are dropped from the list rather than cleaned up. A body is a thing
            // that happened and it should stay where it fell; what has to stop is this class
            // counting it as somebody who is still with you.
            for (var i = _men.Count - 1; i >= 0; i--)
            {
                var man = _men[i];
                var ped = man.Ped;

                if (ped == null || !ped.Exists())
                {
                    PutItOut(man);
                    _men.RemoveAt(i);
                    continue;
                }

                if (!ped.IsAlive)
                {
                    try
                    {
                        // The cigarette goes with him. An unattached prop left hanging in the
                        // air over a body is the sort of thing that outlives the session.
                        PutItOut(man);
                        ped.IsPersistent = false;
                        ped.MarkAsNoLongerNeeded();
                    }
                    catch { /* he is past caring */ }

                    _men.RemoveAt(i);
                    Log.Info("A homie went down.");
                }
            }

            // The cab is let go once it has had time to get down the road.
            ReleaseCab();

            // Walking away from the set does not leave you with three of their people.
            if (_men.Count > 0 && (_crew == null || !_crew.IsAffiliated))
            {
                Dismiss();
                return;
            }

            Loiter();
            Talk();
        }

        /// <summary>
        /// Watches the cab in, gets them out of it, and hands them to the group.
        ///
        /// Two beats, not one, and the split is the whole point. The first stops the cab and
        /// tells them to get out; the second waits until they are actually standing on the
        /// road before adding them to the group and letting the cab drive off. Doing both at
        /// once looked fine right up until the cab was still rolling, at which point three men
        /// dive out of a moving car and the group formation drags them along the tarmac.
        ///
        /// Group membership also deliberately does not happen until they are out. A group
        /// member sitting in a cab three streets away is a man the formation spends the whole
        /// journey hauling toward you, fighting the cab's own driving task the entire way.
        /// </summary>
        private void Arriving()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var late = Game.GameTime - _calledAt > InboundGiveUpMs;
            var gone = _cab == null || !_cab.Exists() || !_cab.IsDriveable;

            if (!_dropping)
            {
                var near = !gone && _cab.Position.DistanceTo(player.Position) <= DropRange;
                if (!near && !late && !gone) return;

                if (late) Log.Info("The cab never made it; the homies got out where they were.");

                // Brought to a stop over eight metres rather than tasked to park, because the
                // driving task is still running and would pull away again the moment it was
                // asked to do anything else.
                if (!gone)
                {
                    try
                    {
                        // THE DRIVE GOES FIRST, then the pin.
                        //
                        // Halting a car does not cancel what it was told to do; it holds it in
                        // place for a while and then lets go, and what it lets go into is the
                        // task that was running all along. Clearing it means there is nothing
                        // underneath to resume, so when the pin expires he is simply a stopped
                        // car -- and SendCabOff hands him a fresh road to leave by.
                        if (_cabbie != null && _cabbie.Exists()) _cabbie.Task.ClearAll();

                        Function.Call(Hash.BRING_VEHICLE_TO_HALT, _cab.Handle, 5f, CabWaitMs, false);
                    }
                    catch { /* it will roll to a stop on its own */ }
                }

                foreach (var man in _men)
                {
                    var ped = man.Ped;
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                    try
                    {
                        // Flag 0: normal exit, closes the door behind him. Checked against the
                        // decompiled-script flag list rather than guessed, because the values
                        // near it do very different things -- 16 teleports him to the kerb and
                        // 4160 is the throw-yourself-out roll, which is precisely the thing
                        // stopping the cab first was meant to avoid.
                        if (ped.IsInVehicle())
                        {
                            Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle,
                                          gone ? 0 : _cab.Handle, 0);
                        }
                    }
                    catch { /* he will find his own way out */ }
                }

                _dropping = true;
                _haltedAt = Game.GameTime;
                return;
            }

            // Second beat: are they actually out yet?
            var stillIn = 0;

            foreach (var man in _men)
            {
                var ped = man.Ped;
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (ped.IsInVehicle()) stillIn++;
            }

            var since = Game.GameTime - _haltedAt;

            // BOTH, not either. Everybody out AND the six seconds up.
            //
            // It used to go the instant the last man's feet touched the road, which is the
            // frame he is closest to the wheels and still has the door open. A driver who
            // waits a beat after the last passenger is out is also just what a cab does.
            var ready = stillIn == 0 && since >= CabWaitMs;

            // The hard stop, for a man who cannot get out at all -- wedged against a wall,
            // knocked down, door blocked. Above the wait, or it would fire first and the wait
            // would never be reached.
            var waited = since > HaltGiveUpMs;

            if (!ready && !waited) return;

            if (stillIn > 0) Log.Info("Gave up waiting; " + stillIn + " still in the cab.");

            var group = Function.Call<int>(Hash.GET_PLAYER_GROUP, Game.Player.Handle);

            foreach (var man in _men)
            {
                var ped = man.Ped;
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, ped.Handle, group);
                    Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, ped.Handle, true);
                }
                catch { /* he will find you */ }

                if (man.Drunk) MakeHimDrunk(ped);
            }

            try
            {
                // Formation 0 is the loose one that walks abreast rather than in single file.
                // The spacing after it is what actually brings them in close -- the formation
                // only decides the shape, not how big it is.
                Function.Call(Hash.SET_GROUP_FORMATION, group, 0);
                Function.Call(Hash.SET_GROUP_FORMATION_SPACING, group,
                              FormationSpacing, FormationMin, FormationMax);
                Function.Call(Hash.SET_GROUP_SEPARATION_RANGE, group, LeashRange);
            }
            catch { /* they will keep up anyway */ }

            SendCabOff();

            _inbound = false;
            _dropping = false;

            // A TEXT, not a ticker, and for the same reason the plugs send one: this is the
            // message you go back and look for. A ticker is gone in eight seconds and tells
            // you something you may well have been looking away for -- a text is in the thread
            // afterwards, next to the line you sent asking them to come.
            Notify.Text(null, ContactName, ContactZone, "we outside");

            Log.Info("Homies arrived: " + Standing + " with the player.");
        }

        // ---- standing around ----------------------------------------------------

        /// <summary>
        /// What they do with their hands when nothing is happening.
        ///
        /// Three men stood in a perfect triangle staring at the middle distance is the tell
        /// that they are spawned props rather than people, and it is the state they are in for
        /// most of the time you have them. So: while everybody is stationary and nobody is
        /// shooting, each one is given something to do with his upper body -- a gesture, a
        /// phone, a cigarette -- re-rolled every several seconds so it never settles into a
        /// loop you can count.
        ///
        /// The moment anyone moves it is all cleared. A secondary task does not stop on its
        /// own; it has to be taken off him, and a man walking down the street still doing a
        /// leaning-against-a-wall gesture looks worse than a man doing nothing at all.
        /// </summary>
        private void Loiter()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            // Nothing while you are driving, fighting, or otherwise busy -- the group has
            // better things for them to be doing and a gesture would sit on top of it.
            var busy = player.IsInVehicle() ||
                       player.IsInCombat ||
                       player.IsShooting ||
                       player.IsRagdoll ||
                       Game.Player.WantedLevel > 0 ||
                       player.Velocity.Length() > StillSpeed;

            foreach (var man in _men)
            {
                var ped = man.Ped;
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                var his = busy ||
                          ped.IsInVehicle() ||
                          ped.IsInCombat ||
                          ped.IsRagdoll ||
                          ped.Velocity.Length() > StillSpeed;

                if (his)
                {
                    StopIdling(man);
                    continue;
                }

                if (man.Idling && Game.GameTime < man.IdleUntil) continue;

                StartIdling(man);
            }
        }

        /// <summary>
        /// They talk. In their own voices, and often enough to notice.
        ///
        /// Ambient speech labels rather than lines of ours, which is the whole point: a line
        /// of written dialogue needs a subtitle and a subtitle needs a name beside it, and
        /// three men muttering captions at you while you are trying to sell something is a
        /// worse screen than a quiet one. These come out in the ped's own voice with no text
        /// at all -- the thing a bystander actually hears.
        ///
        /// Ticked whatever else they are doing, so they carry on talking while walking, and
        /// jittered per man so they never speak in chorus.
        /// </summary>
        private void Talk()
        {
            var now = Game.GameTime;

            foreach (var man in _men)
            {
                var ped = man.Ped;
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                if (man.NextWord == 0)
                {
                    // Staggered from the start rather than all three arming on the same tick.
                    man.NextWord = now + _rng.Next(SayMinMs);
                    continue;
                }

                if (now < man.NextWord) continue;

                man.NextWord = now + SayMinMs + _rng.Next(SayMaxMs - SayMinMs);

                try
                {
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle,
                                  Chatter[_rng.Next(Chatter.Length)], "SPEECH_PARAMS_FORCE");
                }
                catch
                {
                    // A label this model has no line for is simply silence.
                }
            }
        }

        private void StartIdling(Homie man)
        {
            var ped = man.Ped;

            try
            {
                string dict;
                string clip;
                string prop;
                int flag;

                if (man.Drunk)
                {
                    // He is not merely walking drunk, he is still drinking. The bottle is the
                    // whole joke and it has to be in his hand for it to land.
                    dict = BeerDict;
                    clip = Idles[_rng.Next(Idles.Length)];
                    prop = BeerProp;
                    flag = 49;
                }
                else if (man.Smoker)
                {
                    // Alternated rather than rolled, so it reads as one man working through a
                    // cigarette and then a joint rather than as a random prop generator.
                    man.OnTheJoint = !man.OnTheJoint;

                    dict = man.OnTheJoint ? JointDict : SmokeDict;
                    clip = Idles[_rng.Next(Idles.Length)];
                    prop = man.OnTheJoint ? JointProp : CiggyProp;

                    // 49 is loop | upper body | secondary. He is never finished, so unlike the
                    // third man his gesture loops until something takes it off him.
                    flag = 49;
                }
                else
                {
                    var set = LoiterIdles[_rng.Next(LoiterIdles.Length)];
                    dict = set[0];
                    clip = set[1 + _rng.Next(set.Length - 1)];
                    prop = null;

                    // 48 is upper body | secondary, played once. Letting it end and picking
                    // another is what stops him metronoming the same gesture at you.
                    flag = 48;
                }

                // Asked for and then dropped for a tick. A dict that is not resident makes
                // TASK_PLAY_ANIM a silent no-op rather than an error, so the man would simply
                // stand there and nothing would ever say why.
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, dict);
                    return;
                }

                // The prop is renewed whenever the vice changes hands -- the smoker swapping
                // a cigarette for a joint has to actually swap what he is holding.
                if (prop == null) PutItOut(man);
                else if (man.Ciggy == null || man.Holding != prop) LightOne(man, prop);

                // The last three are bPhaseControlled, IkFlags and bAllowOverrideCloneUpdate.
                // They are NOT position locks, whatever their place in the signature suggests,
                // and passing true for them freezes the clip on its first frame.
                Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, dict, clip,
                              4f, -4f, -1, flag, 0f, false, 0, false);

                man.Idling = true;
                man.IdleUntil = Game.GameTime + IdleHoldMin + _rng.Next(IdleHoldMax - IdleHoldMin);
            }
            catch (Exception ex)
            {
                Log.Debug("Idle failed: " + ex.Message);
                man.IdleUntil = Game.GameTime + IdleHoldMax;
            }
        }

        private void StopIdling(Homie man)
        {
            if (!man.Idling) return;

            man.Idling = false;
            man.IdleUntil = 0;

            try
            {
                // A secondary task is not cleared by tasking something else over it, which is
                // exactly why it is useful here and exactly why it has to be taken off by hand.
                Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, man.Ped.Handle);
            }
            catch { /* it will run out */ }

            PutItOut(man);
        }

        /// <summary>Puts the right thing in his hand -- a bottle, a cigarette, a joint.</summary>
        private void LightOne(Homie man, string what)
        {
            PutItOut(man);

            try
            {
                var model = new Model(what);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(500)) return;

                var prop = World.CreateProp(model, man.Ped.Position, false, false);
                model.MarkAsNoLongerNeeded();

                if (prop == null || !prop.Exists()) return;

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, man.Ped.Handle, RightHandBone);

                // PH_R_Hand is a prop helper, so it already sits where a held object goes --
                // no offset, no rotation. Every fiddled-in offset here has historically been a
                // sign of using SKEL_R_Hand instead, which is the wrist joint and half a hand
                // out from where the fingers actually close.
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, prop.Handle, man.Ped.Handle, bone,
                              0f, 0f, 0f, 0f, 0f, 0f, true, true, false, true, 1, true);

                man.Ciggy = prop;
                man.Holding = what;
            }
            catch (Exception ex)
            {
                Log.Debug("Nothing to hold: " + ex.Message);
            }
        }

        /// <summary>And takes it off him again whenever he stops standing about.</summary>
        private static void PutItOut(Homie man)
        {
            if (man.Ciggy == null) return;

            try { if (man.Ciggy.Exists()) man.Ciggy.Delete(); } catch { /* gone already */ }

            man.Ciggy = null;
            man.Holding = null;
        }

        private void MakeHimDrunk(Ped ped)
        {
            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_SET_LOADED, DrunkWalk))
                {
                    Function.Call(Hash.REQUEST_ANIM_SET, DrunkWalk);
                }

                // Applied whether or not it has finished loading. The clipset is asked for the
                // moment you make the call, so by the time the cab has crossed a suburb it is
                // resident; if it somehow is not, this is a no-op and he walks straight, which
                // is a far better failure than blocking the arrival on a load.
                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, ped.Handle, DrunkWalk, 1f);

                // And the balance to go with the walk, so a kerb or a shove puts him down.
                Function.Call(Hash.SET_PED_IS_DRUNK, ped.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not get him drunk: " + ex.Message);
            }
        }

        // ---- who they are -------------------------------------------------------

        private Homie Make(GangDef gang, int index, int seat)
        {
            var name = gang.MemberModels[_rng.Next(gang.MemberModels.Count)];

            try
            {
                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) return null;

                var made = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                              _cab.Handle, 4, model.Hash, seat, true, false);
                model.MarkAsNoLongerNeeded();
                if (made == 0) return null;

                var ped = Entity.FromHandle(made) as Ped;
                if (ped == null || !ped.Exists()) return null;

                var h = ped.Handle;

                ped.IsPersistent = true;
                ped.Armor = 40;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h,
                              Game.GenerateHash(gang.RelationshipGroup));

                // One gun each, by position, so they stay tellable apart.
                Function.Call(Hash.GIVE_WEAPON_TO_PED, h,
                              Game.GenerateHash(Guns[index % Guns.Length]), 200, false, true);

                Function.Call(Hash.SET_PED_ACCURACY, h, 35);

                // 0 can use cover, 1 can use vehicles, 2 can do drive-bys, 5 always fight,
                // 46 will fight an armed man while empty-handed. The last one matters: without
                // it a homie who has just been disarmed stands and watches.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 0, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 1, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 2, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, true);

                Function.Call(Hash.SET_PED_COMBAT_ABILITY, h, 2);
                Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, h, 2);

                // Yours, and not shootable by you by accident. Friendly fire from the player
                // turning three bodyguards into three enemies is the worst possible failure.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED_BY_PLAYER, h, Game.Player.Handle, false);
                Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, h, false, false);

                // They are NOT put in the group here. That happens when they are stood on the
                // pavement next to you -- a group member still sitting in a cab three streets
                // away is a man the formation is dragging toward you through traffic.
                return new Homie
                {
                    Ped = ped,

                    // The rifle drinks and the SMG smokes. Fixed rather than rolled, so the
                    // crew is the same crew every time you call it rather than a lucky dip.
                    Drunk = index == 0,
                    Smoker = index == 1,
                };
            }
            catch (Exception ex)
            {
                Log.Debug("Could not bring a homie out: " + ex.Message);
                return null;
            }
        }

        public void RestoreWorld()
        {
            ScrapCab();

            _inbound = false;
            _dropping = false;

            foreach (var man in _men)
            {
                try
                {
                    PutItOut(man);

                    var ped = man.Ped;
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);
                    ped.IsPersistent = false;
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }

            _men.Clear();
        }
    }
}
