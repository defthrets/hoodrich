using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Trigger.
    ///
    /// He started as scenery -- somebody's dog, off the lead, wandering the yard, doing nothing
    /// for the player and deliberately not interactive, on the reasoning that a dog which
    /// turned out to be a mission would ruin the party he was standing in.
    ///
    /// That reasoning was right about missions and wrong about dogs. He is not a mission and
    /// there is nothing to complete: you pet him, and after that he comes with you. Which is
    /// the difference between a party with a dog in it and a dog.
    ///
    /// THE GROUP DOES THE WORK. Putting him in the player's ped group is what makes him follow,
    /// keep up, get left behind and catch up, and turn on whoever turns on you -- all of it,
    /// for two native calls. Writing a follow behaviour by hand would be several hundred lines
    /// that behave worse, and the homies already ride on the same mechanism.
    ///
    /// HE DOES NOT DIE. That is a reversal: he used to be mortal on the reasoning that a
    /// bodyguard you cannot lose is a turret, which is a good argument about a bodyguard and
    /// the wrong argument about this. He is a dog you petted at a party, he walks into the same
    /// gunfights you do because he follows you into them, and losing him permanently to a
    /// stray round from something you did not start is not a consequence -- it is an accident
    /// with a save file attached.
    ///
    /// So he takes damage, he limps, he goes down and he gets back up, and his health is held
    /// above a floor rather than switched off: shooting him still does something, it just does
    /// not do that.
    /// </summary>
    internal sealed class Trigger
    {
        public const string Name = "Trigger";

        private const float SpawnRange = 80f;
        private const float DespawnRange = 150f;
        private const int UpdateIntervalMs = 900;

        /// <summary>Close enough to put a hand on him.</summary>
        private const float PetRange = 2.6f;

        /// <summary>How long the pet lasts, and how long before he can be petted again.</summary>
        private const int PetMs = 3400;
        private const int PetAgainMs = 6000;

        /// <summary>How long he settles before moving again, and the shortest walk he takes.</summary>
        private const float ShortestWalk = 1.5f;
        private const float PauseBetween = 4f;

        /// <summary>
        /// A Rottweiler, because that is what was asked for.
        ///
        /// Chop is the fallback and not the first choice. He is the same breed and the right
        /// shape, but he is Franklin's dog and a character in the story -- so he is what
        /// Trigger looks like on a build that has not got the plain one, rather than what
        /// Trigger IS.
        /// </summary>
        private static readonly string[] Dogs = { "a_c_rottweiler", "a_c_chop", "a_c_shepherd" };

        /// <summary>
        /// The synchronised pair the story uses for petting a Rottweiler.
        ///
        /// Two clips out of one dictionary, authored to line up -- the man's half and the dog's
        /// half. Playing either on its own is somebody miming, so both go or neither does.
        /// </summary>
        private const string PetDict = "creatures@rottweiler@tricks@";
        private const string PetMan = "petting_franklin";
        private const string PetDog = "petting_chop";

        private readonly Vector3 _where;
        private readonly float _radius;
        private readonly Random _rng = new Random();
        private readonly PlayerState _state;

        private Ped _dog;
        private int _lastUpdate;

        private int _petUntil;
        private int _petAgainAt;
        private bool _down;

        private bool _inGroup;

        public Trigger(Vector3 where, float radius, PlayerState state)
        {
            _where = where;
            _radius = radius;
            _state = state;
        }

        /// <summary>Set by Main: the yard only exists once the block is yours.</summary>
        public Func<bool> Known;

        /// <summary>Whether he has been petted and is coming with you.</summary>
        private bool Yours
        {
            get { return _state != null && _state.TriggerIsYours; }
        }

        // ---- per-tick -----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                // Only actually gone if the entity itself has gone -- streamed out, deleted
                // by another script, cleaned up by the game. Being hurt is handled below and
                // is not this.
                if (_dog != null && !_dog.Exists())
                {
                    Release();
                    return;
                }

                if (Yours) { Mine(player, now); return; }

                if (Known != null && !Known())
                {
                    Release();
                    return;
                }

                Yard(player);
            }
            catch (Exception ex)
            {
                Log.Debug("Trigger tripped: " + ex.Message);
            }
        }

        /// <summary>Before he is yours: he is in the yard, and you can put a hand on him.</summary>
        private void Yard(Ped player)
        {
            var away = player.Position.DistanceTo(_where);

            if (_dog == null)
            {
                if (away <= SpawnRange) Make(_where);
                return;
            }

            if (away > DespawnRange)
            {
                Release();
                return;
            }

            Offer(player);
        }

        /// <summary>After: he is wherever you are.</summary>
        private void Mine(Ped player, int now)
        {
            if (_dog == null || !_dog.Exists())
            {
                // Behind you rather than in front, so he arrives at your shoulder instead of
                // appearing in the shot you are looking at.
                Make(player.Position - player.ForwardVector * 3f);

                if (_dog == null) return;
            }

            Alive();
            Once(now);
            Join();
            Bite(player);
            Offer(player);
            Ride(player);
            Nose(player, now);
            Idle(player, now);
            Mark();
        }

        /// <summary>
        /// In the player's group, which is the whole of following and defending.
        ///
        /// Re-asserted rather than set once. The group membership is dropped by a good number
        /// of things -- a cutscene, a mission, dying -- and a dog that has quietly left the
        /// group stands in the road looking at you.
        /// </summary>
        private void Join()
        {
            try
            {
                var group = Function.Call<int>(Hash.GET_PLAYER_GROUP, Game.Player.Handle);

                if (!Function.Call<bool>(Hash.IS_PED_GROUP_MEMBER, _dog.Handle, group))
                {
                    Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, _dog.Handle, group);
                    Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, _dog.Handle, true);

                    _inGroup = true;
                }
            }
            catch
            {
                // He catches up on the next tick.
            }
        }

        /// <summary>
        /// He gets hurt. He does not die.
        ///
        /// Three things, because one of them on its own is not enough. The flags stop the game
        /// deciding he is finished -- an injured ped normally dies where it lies, and a
        /// critical hit skips the injury and goes straight to dead. The floor is the backstop
        /// under both: whatever took the health down, it stops going down here.
        ///
        /// And if something got through anyway -- an explosion, a script that killed everything
        /// nearby, a fall -- he is stood back up rather than mourned. RESURRECT_PED leaves a
        /// ped out of its group and with none of its flags, so all of that is put back on;
        /// resurrecting without re-arming him is a dog that follows nobody and fights nothing.
        /// </summary>
        private void Alive()
        {
            if (_dog == null || !_dog.Exists()) return;

            try
            {
                var h = _dog.Handle;

                var max = Function.Call<int>(Hash.GET_PED_MAX_HEALTH, h);
                if (max <= 0) max = 200;

                var floor = Math.Max(25, max / 3);

                if (!_dog.IsAlive)
                {
                    Function.Call(Hash.RESURRECT_PED, h);
                    Function.Call(Hash.SET_ENTITY_HEALTH, h, floor);

                    Arm(h);
                    _inGroup = false;

                    Notify.Failure(Name + "'s hurt bad.");
                    Log.Info("Trigger went down and got back up.");
                    return;
                }

                if (Function.Call<int>(Hash.GET_ENTITY_HEALTH, h) < floor)
                {
                    Function.Call(Hash.SET_ENTITY_HEALTH, h, floor);
                }
            }
            catch
            {
                // Next tick.
            }
        }

        /// <summary>
        /// The flags that make him yours, in one place.
        ///
        /// Called from two sides -- when he is made, and when he is stood back up -- because a
        /// resurrected ped is a blank one. Two copies of this list would drift apart, and the
        /// copy that drifted would be the one nobody tested.
        /// </summary>
        private static void Arm(int h)
        {
            try
            {
                Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, h, false);
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, h, false);
                Function.Call(Hash.SET_PED_DIES_IN_WATER, h, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, h, true);

                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 17, false);

                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h, Game.GenerateHash("PLAYER"));
            }
            catch
            {
                // He is still a dog.
            }
        }

        /// <summary>
        /// Whoever is on you is on him.
        ///
        /// THE GROUP ALONE DOES NOT DO THIS. Group members defend, but defending is a
        /// disposition rather than an order -- it decides for itself whether a threat is worth
        /// crossing the street for, and an animal weighs that differently to a man with a gun.
        /// The result was a dog that watched a shootout he was stood in the middle of.
        ///
        /// So he is TOLD. Anybody already in combat with you, or who has put a round into you
        /// in the last few seconds, gets pointed out by name -- nearest first, since a dog can
        /// only bite one person at a time and the near one is the one about to be a problem.
        ///
        /// Not while he is in a car, for the obvious reason.
        /// </summary>
        private void Bite(Ped player)
        {
            if (_dog == null || !_dog.Exists() || !_dog.IsAlive) return;
            if (_dog.IsInVehicle()) return;

            try
            {
                Ped worst = null;
                var nearest = BiteRange;

                foreach (var other in World.GetNearbyPeds(player.Position, BiteRange))
                {
                    if (other == null || !other.Exists() || !other.IsAlive) continue;
                    if (other.Handle == _dog.Handle || other.Handle == player.Handle) continue;
                    if (other.IsPlayer) continue;

                    var onYou =
                        Function.Call<bool>(Hash.IS_PED_IN_COMBAT, other.Handle, player.Handle)
                        || Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY,
                                               player.Handle, other.Handle, true);

                    if (!onYou) continue;

                    var gap = other.Position.DistanceTo(_dog.Position);
                    if (gap > nearest) continue;

                    nearest = gap;
                    worst = other;
                }

                if (worst == null)
                {
                    // Nothing on you, so the record of who hit you is cleared -- otherwise the
                    // damage flag above stays set for the rest of the session and he keeps
                    // hunting somebody who stopped ten minutes ago.
                    if (_biting != 0)
                    {
                        _biting = 0;

                        try { Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, player.Handle); }
                        catch { }
                    }

                    return;
                }

                // Already on this one. Re-issuing the task every tick restarts the approach and
                // he trots at the same man for ever without arriving.
                if (_biting == worst.Handle
                    && Function.Call<bool>(Hash.IS_PED_IN_COMBAT, _dog.Handle, worst.Handle))
                {
                    return;
                }

                _biting = worst.Handle;

                Function.Call(Hash.CLEAR_PED_TASKS, _dog.Handle);
                Function.Call(Hash.TASK_COMBAT_PED, _dog.Handle, worst.Handle, 0, 16);
                Function.Call(Hash.SET_PED_KEEP_TASK, _dog.Handle, true);
            }
            catch
            {
                // He barks about it instead.
            }
        }

        /// <summary>How far out he will go for somebody who is on you.</summary>
        private const float BiteRange = 35f;

        /// <summary>
        /// Dog things, when nothing else is going on.
        ///
        /// ON FOOT ONLY, and that is not a rule about animation -- it is a rule about what
        /// these clips are. They are authored for a dog stood on the ground, so playing one on
        /// a dog in a passenger seat is a dog barking through the roof of the car. He already
        /// has an in-vehicle animation and it is the seated pose; that is the car-specific one,
        /// and it is the whole of what belongs in there.
        ///
        /// AND ONLY WHEN YOU HAVE STOPPED. An animation is a task, and a task interrupts the
        /// follow -- so a dog that decides to have a scratch while you are walking away is a
        /// dog you leave behind, which is worse than a dog that does nothing. He does it when
        /// you are stood still, near him, and nobody is shooting.
        ///
        /// The task is not cleared afterwards. It runs its length and ends, and the group picks
        /// him straight back up -- clearing it would fight the same group on the frame it was
        /// already handing him back.
        /// </summary>
        private void Idle(Ped player, int now)
        {
            if (_dog == null || !_dog.Exists() || !_dog.IsAlive) return;

            // Everything that is more important than a dog scratching itself.
            if (_dog.IsInVehicle() || player.IsInVehicle()) return;
            if (_biting != 0 || _jumpingAt != 0 || _outAt != 0) return;
            if (now < _petUntil) return;

            if (now < _idleAt)
            {
                return;
            }

            // First time through, the clock has never been set -- give him one gap rather than
            // having him perform the instant he is created.
            if (_idleAt == 0)
            {
                _idleAt = now + _rng.Next(IdleMinMs, IdleMaxMs);
                return;
            }

            if (player.Position.DistanceTo(_dog.Position) > IdleNear) return;

            try
            {
                if (player.Velocity.Length() > 0.6f) return;
            }
            catch
            {
                return;
            }

            _idleAt = now + _rng.Next(IdleMinMs, IdleMaxMs);

            var start = _rng.Next(Idles.Length);

            for (var i = 0; i < Idles.Length; i++)
            {
                var pair = Idles[(start + i) % Idles.Length];

                try
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, pair[0]);

                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pair[0])) continue;

                    Function.Call(Hash.TASK_PLAY_ANIM, _dog.Handle, pair[0], pair[1],
                                  2f, -2f, _rng.Next(IdleShortMs, IdleLongMs), 0, 0f,
                                  false, false, false);

                    // The animation has just replaced whatever he was doing, and if that was
                    // the wander then the wander is gone -- so it is marked as gone and set
                    // again next time round. He works the area, stops to bark at something,
                    // and carries on, which is what a dog does anyway.
                    _wandering = false;

                    return;
                }
                catch
                {
                    // Next one.
                }
            }
        }

        /// <summary>
        /// The ambient dog set, tried in a random order.
        ///
        /// Random rather than in order, because a fixed list plus a fallback chain is a dog
        /// that only ever does the first thing on it -- which is the same fault the takeover
        /// cars had, where the list was read as a preference and behaved as a single choice.
        /// </summary>
        private static readonly string[][] Idles =
        {
            new[] { "creatures@rottweiler@amb@world_dog_barking@idle_a", "idle_a" },
            new[] { "creatures@rottweiler@amb@world_dog_barking@idle_a", "idle_b" },
            new[] { "creatures@rottweiler@amb@world_dog_barking@idle_a", "idle_c" },
            new[] { "creatures@rottweiler@amb@world_dog_barking@base", "base" },
            new[] { "creatures@rottweiler@amb@world_dog_sitting@idle_a", "idle_a" },
            new[] { "creatures@rottweiler@amb@world_dog_sitting@base", "base" },
            new[] { "creatures@rottweiler@amb@world_dog_sitting@enter", "enter" }
        };

        /// <summary>
        /// Stood about long enough that he goes and has a look round.
        ///
        /// A dog at heel is right for thirty seconds and wrong for five minutes. If you have
        /// parked yourself somewhere -- a shop door, a corner, reading the map -- he stops
        /// waiting at your knee and works the area instead, and comes straight back the moment
        /// you move off.
        ///
        /// THE WANDER IS A TASK AND THE FOLLOW IS A TASK, and only one of them can be on. That
        /// is the whole reason this is switched rather than layered: the group hands him a
        /// follow, this hands him a wander, and the one issued last is the one he does. Which
        /// makes the clear on the way out the important half -- without it, moving off leaves
        /// him wandering a patch of pavement you have walked away from, and the group does not
        /// take him back until something else clears it.
        ///
        /// The trigger is your speed rather than your position. Standing still turning on the
        /// spot is still standing still, and a position check would call that walking.
        /// </summary>
        private void Nose(Ped player, int now)
        {
            if (_dog == null || !_dog.Exists() || !_dog.IsAlive) return;

            // Anything that matters more than a sniff around.
            if (_dog.IsInVehicle() || player.IsInVehicle() || _biting != 0
                || _jumpingAt != 0 || _outAt != 0 || now < _petUntil)
            {
                Heel();
                return;
            }

            float speed;

            try { speed = player.Velocity.Length(); }
            catch { return; }

            // MOVING AGAIN. Back to your side, and the clock starts from nothing rather than
            // from where it left off -- a step to the left should not leave him one second
            // away from wandering off again.
            if (speed > MovingAt)
            {
                Heel();
                _stillSince = 0;
                return;
            }

            if (_stillSince == 0)
            {
                _stillSince = now;
                return;
            }

            if (now - _stillSince < StoodAboutMs) return;
            if (_wandering) return;

            try
            {
                var at = player.Position;

                Function.Call(Hash.TASK_WANDER_IN_AREA, _dog.Handle,
                              at.X, at.Y, at.Z, NoseRange, ShortestWalk, PauseBetween);

                Function.Call(Hash.SET_PED_KEEP_TASK, _dog.Handle, true);

                _wandering = true;
            }
            catch
            {
                // He stays at your knee.
            }
        }

        /// <summary>
        /// Sweep now and then, not every tick.
        ///
        /// GetNearbyPeds walks the ped pool and this does not need doing several times a
        /// second -- a duplicate arrives at a reload or a dismiss, both of which are rare and
        /// neither of which is urgent. Six seconds is quick enough that you never see the
        /// second dog for long, and slow enough to cost nothing.
        /// </summary>
        private void Once(int now)
        {
            if (now < _sweepAt) return;

            _sweepAt = now + SweepMs;

            Only();
        }

        private const int SweepMs = 6000;
        private int _sweepAt;

        /// <summary>Back to your side, if he had wandered off.</summary>
        private void Heel()
        {
            if (!_wandering) return;

            _wandering = false;

            // NOT WHILE HE IS SAT IN A CAR, and this was breaking the seated pose on the way
            // in. The order of a tick is Ride() then Nose(): Ride puts him on the seat and
            // starts the sit, then Nose sees you are in a vehicle and calls this to bring him
            // back to heel -- and the clear it does to end a wander ended the animation that
            // had just started, one line later, every single time you got in a car after
            // standing still long enough for him to have a look round.
            //
            // The flag still drops, because he is plainly not wandering any more. It is only
            // the clear that is wrong here: there is no wander task left to cancel, and the
            // task it actually cancelled was the one we wanted.
            if (_dog != null && _dog.Exists() && _dog.IsInVehicle()) return;

            try { Function.Call(Hash.CLEAR_PED_TASKS, _dog.Handle); }
            catch { }

            // And the group is re-asserted on the next tick by Join(), which is what actually
            // brings him back -- clearing only makes room for it.
            _inGroup = false;
        }

        /// <summary>How long you have to stand there, how far he goes, and what counts as moving.</summary>
        private const int StoodAboutMs = 16000;
        private const float NoseRange = 9f;
        private const float MovingAt = 0.9f;

        private int _stillSince;
        private bool _wandering;

        /// <summary>How often, how close, and how long each one runs.</summary>
        private const int IdleMinMs = 14000;
        private const int IdleMaxMs = 34000;
        private const float IdleNear = 7f;
        private const int IdleShortMs = 2600;
        private const int IdleLongMs = 5200;

        private int _idleAt;

        /// <summary>Who he is currently going for.</summary>
        private int _biting;

        // ---- petting ------------------------------------------------------------

        private void Offer(Ped player)
        {
            if (_dog == null || !_dog.Exists()) return;
            if (player.IsInVehicle()) return;

            var now = Game.GameTime;

            if (now < _petUntil) return;
            if (_dog.Position.DistanceTo(player.Position) > PetRange) return;

            Help.ShowThisFrame(Yours
                ? "Hold ~INPUT_CELLPHONE_RIGHT~ to pet " + Name + ", or ~INPUT_CELLPHONE_LEFT~ "
                  + "to send him back to the yard."
                : "Hold ~INPUT_CELLPHONE_RIGHT~ to pet him.");

            // SENDING HIM HOME IS A TAP, not a hold. It is the opposite of the pet in every
            // way that matters and it wants to feel like it: one is a thing you lean on him
            // to do, the other is a thing you wave him off with.
            if (Yours && Away())
            {
                Dismiss();
                return;
            }

            if (!Held()) return;
            if (now < _petAgainAt) return;

            Pet(player, now);
        }

        /// <summary>The wave-off, on its own edge so holding it does not fire it twice.</summary>
        private bool Away()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneLeft)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneLeft);
            }
            catch
            {
            }

            var hit = down && !_awayDown;
            _awayDown = down;

            return hit;
        }

        private bool _awayDown;

        /// <summary>
        /// Off you go, then.
        ///
        /// He is put back to being the yard dog rather than deleted: out of the group, off the
        /// save, and handed to the game where he stands. He is at the party again next time you
        /// go there, because that is where the yard puts him -- walking him across the map in
        /// real time would be a dog trotting through traffic for ten minutes to arrive at a
        /// place you are not.
        /// </summary>
        private void Dismiss()
        {
            try
            {
                if (_state != null)
                {
                    _state.TriggerIsYours = false;
                    _state.Touch();
                }

                if (_dog != null && _dog.Exists())
                {
                    var h = _dog.Handle;

                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, h);
                    Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, h, false);
                    Function.Call(Hash.CLEAR_PED_TASKS, h);

                    Function.Call(Hash.TASK_WANDER_STANDARD, h, 10f, 10);
                    Function.Call(Hash.SET_PED_KEEP_TASK, h, true);
                }

                _inGroup = false;
                _sitting = false;
                _wandering = false;

                // HE IS NOT LET GO OF HERE, and that is a fix rather than an oversight.
                // Releasing him marks him as no longer needed, which CLEARS the mission flag --
                // and the mission flag is the only thing that says this dog is ours. A released
                // dog is therefore invisible to the sweep and still stood there, so walking
                // back to the yard built a second one right next to him.
                //
                // Kept instead, and handed to the yard logic: it offers him to be petted again,
                // and lets him go properly once you are far enough away to not see it happen.
                Notify.Important("~o~" + Name + " heads back to the yard.~s~");
                Log.Info("Trigger was sent home.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send Trigger home: " + ex.Message);
            }
        }

        /// <summary>
        /// Start him over: forget he was ever yours and put him back in the yard.
        ///
        /// For the settings screen. The difference from a dismiss is that this one works from
        /// anywhere and does not need him to be stood next to you -- it is the row you press
        /// when he is lost, stuck inside a wall, or a save has him marked as yours with no dog
        /// anywhere on the map.
        /// </summary>
        public void Reset()
        {
            try
            {
                if (_state != null)
                {
                    _state.TriggerIsYours = false;
                    _state.Touch();
                }

                Unmark();

                if (_dog != null && _dog.Exists())
                {
                    try
                    {
                        Function.Call(Hash.REMOVE_PED_FROM_GROUP, _dog.Handle);
                        _dog.Delete();
                    }
                    catch
                    {
                        Release();
                    }
                }

                _dog = null;
                _inGroup = false;
                _sitting = false;
                _jumpingAt = 0;
                _outAt = 0;
                _doorOn = -1;

                Notify.Important("~o~" + Name + "'s back in the yard.~s~");
                Log.Info("Trigger was reset.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not reset Trigger: " + ex.Message);
            }
        }

        private void Pet(Ped player, int now)
        {
            _petUntil = now + PetMs;
            _petAgainAt = now + PetMs + PetAgainMs;

            try
            {
                // Facing each other first. The two clips are authored as a pair and line up
                // only if the pair does; a dog petted over its own shoulder is worse than a
                // dog not petted at all.
                var toDog = _dog.Position - player.Position;

                // Minus on the X. GTA headings run anticlockwise from north, so the
                // version without it is mirrored east to west -- which had Franklin turning
                // away from the dog to pet it whenever the dog was to his left or right.
                player.Heading =
                    (float)((Math.Atan2(-toDog.X, toDog.Y) * 180.0 / Math.PI + 360.0) % 360.0);
                _dog.Heading = (player.Heading + 180f) % 360f;

                Function.Call(Hash.REQUEST_ANIM_DICT, PetDict);

                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, PetDict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, PetDict, PetMan,
                                  4f, -4f, PetMs, 0, 0f, false, false, false);

                    Function.Call(Hash.TASK_PLAY_ANIM, _dog.Handle, PetDict, PetDog,
                                  4f, -4f, PetMs, 0, 0f, false, false, false);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not play the petting: " + ex.Message);
            }

            if (Yours) return;

            // THE FIRST ONE IS THE WHOLE TRANSACTION. Nothing is accepted and nothing is
            // confirmed -- you put a hand on a dog and after that the dog comes with you,
            // which is how it works with dogs.
            _state.TriggerIsYours = true;
            _state.Touch();

            Notify.Important("~g~" + Name + ".~s~ He's coming with you.");
            Log.Info("Trigger has been adopted.");
        }

        // ---- the car ------------------------------------------------------------

        /// <summary>
        /// Shotgun, like Chop.
        ///
        /// The group will not put him in a car on its own -- it follows on foot and stands
        /// there while you drive off. So when you are in something and he is not, he is told
        /// to get in, and the seat is chosen rather than left to the game: front passenger if
        /// it is free, the back if it is not, and nothing at all if the car is full.
        /// </summary>
        private void Ride(Ped player)
        {
            try
            {
                var car = player.CurrentVehicle;

                if (car == null || !car.Exists())
                {
                    _jumpingAt = 0;

                    // STILL SAT IN A CAR YOU HAVE GOT OUT OF. He gets out the way he got in,
                    // through a door that opens for him, rather than being deleted off the
                    // seat and re-materialised on the pavement.
                    if (_dog.IsInVehicle())
                    {
                        Hop();
                        return;
                    }

                    // The seated pose is a looped animation, and a loop with no end time
                    // outlives the reason it was started -- so a dog left holding it sits in an
                    // abandoned car while you walk away. Cleared once, on the frame the car
                    // goes, and the group brings him after you.
                    if (_sitting)
                    {
                        _sitting = false;

                        try { Function.Call(Hash.CLEAR_PED_TASKS, _dog.Handle); }
                        catch { }
                    }

                    Afoot();

                    return;
                }

                _outAt = 0;

                if (_dog.IsInVehicle(car))
                {
                    _jumpingAt = 0;

                    // IN AND SEATED, so the door he came through goes back. Held as a door
                    // index rather than a flag, because shutting "the passenger door" is wrong
                    // on the trip where he took a back one.
                    Shut(car);
                    Sit();
                    return;
                }

                var seat = Free(car);
                if (seat == int.MinValue) return;

                // AND THE DOOR OPENS FOR HIM WHEN HE IS ACTUALLY COMING.
                //
                // It used to open the moment you got in, which meant it opened for a dog who
                // was two streets back and stayed open the whole time he was catching up --
                // and if he never caught up, it stayed open for good. A door standing open
                // beside you with nothing arriving is worse than a dog that opens his own,
                // because at least the second one is explicable.
                //
                // Twelve metres, which is roughly three seconds of dog. Far enough ahead that
                // it is already open when he gets there and he never plays the door-nudging
                // animation; near enough that it is plainly for him.
                var comingIn = _dog.Position.DistanceTo(car.Position);

                if (comingIn <= OpenFrom) Open(car, seat);
                else if (comingIn > OpenFrom + 8f) Shut(car);

                // MID-JUMP. The seat is taken at the END of the animation, so this branch is
                // the one that does nothing -- and it has to come before the task check below
                // or he is re-tasked halfway through his own jump.
                if (_jumpingAt != 0)
                {
                    if (Game.GameTime - _jumpingAt < JumpMs) return;

                    _jumpingAt = 0;

                    Function.Call(Hash.SET_PED_INTO_VEHICLE, _dog.Handle, car.Handle, seat);
                    return;
                }

                // Close enough to jump rather than walk. Further out he is still catching up
                // and the group is already bringing him.
                if (_dog.Position.DistanceTo(car.Position) < JumpFrom && Jump(car)) return;

                // Already on his way. Asking again every tick restarts the approach and he
                // never reaches the door.
                if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, _dog.Handle, 160)) return;

                Function.Call(Hash.TASK_ENTER_VEHICLE, _dog.Handle, car.Handle,
                              -1, seat, 2f, 1, 0);
            }
            catch
            {
                // He runs alongside, which is not the worst outcome.
            }
        }

        /// <summary>
        /// The jump in, which is the one Chop does.
        ///
        /// A DOG CANNOT OPEN A DOOR, so the ordinary enter task is a dog miming a person: he
        /// walks to the handle, reaches for it with a paw, and slides in sideways. The game has
        /// an animation for this exact thing because the story needed it, and it lives in the
        /// Rottweiler's own set.
        ///
        /// Which dictionary and clip cannot be checked from here, so it is a short list tried
        /// in order, and if none of them load he falls back to the ordinary task -- which is
        /// ugly and works. He is turned to face the car first, or the jump goes off at whatever
        /// angle he happened to be stood at.
        /// </summary>
        private bool Jump(Vehicle car)
        {
            foreach (var pair in Jumps)
            {
                try
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, pair[0]);

                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pair[0])) continue;

                    Function.Call(Hash.CLEAR_PED_TASKS, _dog.Handle);

                    var toCar = car.Position - _dog.Position;

                    _dog.Heading =
                        (float)((Math.Atan2(-toCar.X, toCar.Y) * 180.0 / Math.PI + 360.0) % 360.0);

                    Function.Call(Hash.TASK_PLAY_ANIM, _dog.Handle, pair[0], pair[1],
                                  4f, -4f, JumpMs, 0, 0f, false, false, false);

                    _jumpingAt = Game.GameTime;
                    return true;
                }
                catch
                {
                    // Next pair.
                }
            }

            return false;
        }

        /// <summary>
        /// Sat up on the seat, the way Chop sits.
        ///
        /// A DOG PUT IN A SEAT WITH NOTHING TO PLAY GETS THE HUMAN SEATED POSE mapped onto a
        /// dog skeleton -- which is the sideways, half-through-the-door, floating look. The
        /// seated clip is what makes him a passenger instead of cargo.
        ///
        /// Looped, and re-asserted rather than set once: the pose is dropped by a good number
        /// of things -- a hard landing, a shunt, being shot at, the group re-tasking him -- and
        /// he spends the rest of the drive lying through the upholstery. Checked on a clock
        /// rather than every frame, because asking four clips whether they are playing several
        /// times a second is a lot of asking about a dog sitting still.
        /// </summary>
        private void Sit()
        {
            Seated();

            try
            {
                foreach (var pair in Sits)
                {
                    if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM,
                                            _dog.Handle, pair[0], pair[1], 3))
                    {
                        _sitting = true;
                        return;
                    }
                }

                if (Game.GameTime < _sitAgainAt) return;
                _sitAgainAt = Game.GameTime + SitCheckMs;

                foreach (var pair in Sits)
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, pair[0]);

                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pair[0])) continue;

                    // -1 and the loop flag, so it holds for the whole drive. Eased in slowly,
                    // because a pose that snaps on every time it is re-asserted reads as a
                    // twitch every couple of seconds.
                    Function.Call(Hash.TASK_PLAY_ANIM, _dog.Handle, pair[0], pair[1],
                                  2f, -2f, -1, 1, 0f, false, false, false);

                    _sitting = true;
                    return;
                }
            }
            catch
            {
                // He rides badly, which is still riding.
            }
        }

        /// <summary>
        /// A passenger does not flinch.
        ///
        /// THIS IS WHY HE CAME APART UNDER FIRE. He is deliberately reactive -- that is what
        /// makes him a bodyguard rather than a prop, and it is switched on the moment he is
        /// yours. But a reaction is a TASK, and every task replaces the seated pose, so a
        /// firefight beside the car was a stream of flinch-cower-brace events each of which
        /// cancelled the animation and left him lying through the seat until the next re-assert
        /// put it back -- which the next round then cancelled again. That flicker is the glitch.
        ///
        /// So the reactions go off while he is a passenger and come back on when he gets out.
        /// He can do nothing useful about a gunfight from inside a car anyway: he cannot bite
        /// anybody through a door, and Bite() already sits out the whole time he is in one. All
        /// the reactions were producing was the flicker.
        ///
        /// Ragdoll goes too. A dog knocked into a ragdoll on a seat has no pose at all until he
        /// settles, and he settles wherever the physics leaves him -- usually halfway through
        /// the dashboard.
        ///
        /// He is still shot at and still hurt. The health floor is what keeps him, not this.
        /// </summary>
        private void Seated()
        {
            if (_riding) return;
            _riding = true;

            try
            {
                var h = _dog.Handle;

                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, h, false);
                Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, h, 1);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);
            }
            catch
            {
                // He rides badly, which is still riding.
            }
        }

        /// <summary>And gets them all back the moment his feet are on the ground.</summary>
        private void Afoot()
        {
            if (!_riding) return;
            _riding = false;

            try
            {
                if (_dog == null || !_dog.Exists()) return;

                Function.Call(Hash.SET_PED_CAN_RAGDOLL, _dog.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, _dog.Handle, 0);

                // Only if he is yours -- the yard dog is deliberately deaf to everything, and
                // handing him the bodyguard settings on the way out of a car would be a party
                // dog that suddenly starts picking fights.
                if (Yours) Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS,
                                         _dog.Handle, false);
            }
            catch
            {
                // Next tick.
            }
        }

        private bool _riding;

        /// <summary>Dictionary and clip for the jump in, in order of preference.</summary>
        private static readonly string[][] Jumps =
        {
            new[] { "creatures@rottweiler@in_vehicle@std_car", "get_in" },
            new[] { "creatures@rottweiler@in_vehicle@std_car", "getin" },
            new[] { "creatures@rottweiler@in_vehicle@van", "get_in" }
        };

        /// <summary>
        /// And for sitting on the seat. The last one is the ambient sitting idle, which is
        /// everywhere in the game -- which is why this list ends in something certain.
        /// </summary>
        private static readonly string[][] Sits =
        {
            new[] { "creatures@rottweiler@in_vehicle@std_car", "sit" },
            new[] { "creatures@rottweiler@in_vehicle@std_car", "idle" },
            new[] { "creatures@rottweiler@in_vehicle@van", "sit" },
            new[] { "creatures@rottweiler@amb@world_dog_sitting@base", "base" }
        };

        /// <summary>How long the jump takes, how close he has to be to try it, and how often
        /// the seated pose is checked.</summary>
        private const int JumpMs = 1100;
        private const float JumpFrom = 4.5f;

        /// <summary>
        /// And how close before the door swings.
        ///
        /// Well outside the jump distance on purpose. The whole reason to open it from script
        /// is so that the door is ALREADY open when he arrives -- if it opened at the same
        /// range he jumps from, he would reach a shut door and the game would give him the
        /// business of opening it, which is the thing this exists to avoid.
        /// </summary>
        private const float OpenFrom = 12f;
        /// <summary>
        /// Five hundred, down from fifteen hundred.
        ///
        /// The gate on how often the pose is re-asserted, which is also how long he can be seen
        /// without one. The tick above it only runs every nine hundred milliseconds, so the old
        /// number meant a broken pose could stand for the better part of two and a half seconds
        /// -- long enough to read as a bug rather than a hiccup. At five hundred the tick is the
        /// limit rather than this, and anything that does get through is put back on the next
        /// one. The cost is asking four clips whether they are playing about once a second.
        /// </summary>
        private const int SitCheckMs = 500;

        private int _jumpingAt;
        private int _sitAgainAt;
        private bool _sitting;

        /// <summary>
        /// Take up the one that is already out there, if there is one.
        ///
        /// Only ours -- a mission entity of one of our models. An ambient dog wandering past is
        /// somebody else's and is left alone.
        ///
        /// He is re-armed on the way in when he is yours, because an adopted dog is one whose
        /// flags were set by an instance that is gone: after a reload he is a rottweiler stood
        /// in the road with none of the things that make him Trigger.
        /// </summary>
        private bool Adopt(Vector3 at)
        {
            try
            {
                Ped found = null;
                var nearest = AdoptRange;

                foreach (var ped in World.GetNearbyPeds(at, AdoptRange))
                {
                    if (!Ours(ped)) continue;

                    var gap = ped.Position.DistanceTo(at);
                    if (gap > nearest) continue;

                    nearest = gap;
                    found = ped;
                }

                if (found == null) return false;

                _dog = found;
                _dog.IsPersistent = true;

                _inGroup = false;
                _sitting = false;
                _jumpingAt = 0;
                _outAt = 0;
                _doorOn = -1;
                _wandering = false;

                if (Yours) Arm(_dog.Handle);

                Only();

                Log.Info("Trigger was already out here. Took that one rather than making another.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not adopt a dog: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Anything else of ours goes.
        ///
        /// The backstop under the adoption above: adopting picks the nearest, and this removes
        /// the rest, so a save that already collected three of them settles back to one on the
        /// first tick rather than staying at three for ever.
        /// </summary>
        private void Only()
        {
            if (_dog == null || !_dog.Exists()) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(_dog.Position, CullRange))
                {
                    if (ped.Handle == _dog.Handle) continue;
                    if (!Ours(ped)) continue;

                    Log.Info("Found a second Trigger. Removed it.");

                    try { ped.Delete(); }
                    catch { }
                }
            }
            catch
            {
                // One extra dog is survivable; the sweep runs again.
            }
        }

        /// <summary>
        /// Is that one of ours.
        ///
        /// Two tests, and both are needed. The model says it is the right kind of animal and
        /// the mission flag says this mod put it there -- the game spawns rottweilers of its
        /// own accord, and those are not ours to delete.
        /// </summary>
        private static bool Ours(Ped ped)
        {
            try
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) return false;
                if (ped.IsPlayer) return false;

                var hash = ped.Model.Hash;
                var mine = false;

                foreach (var name in Dogs)
                {
                    if (hash != Game.GenerateHash(name)) continue;

                    mine = true;
                    break;
                }

                if (!mine) return false;

                return Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, ped.Handle);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>How far to look for one already out there, and how far to sweep.</summary>
        private const float AdoptRange = 60f;
        private const float CullRange = 90f;

        /// <summary>
        /// Swing the right door for the seat he is heading to.
        ///
        /// Seat to door is an offset of one -- seat 0 is the front passenger and that is door
        /// 1, because door 0 is the one you are sat in. Remembered so the same one is shut
        /// again, and only opened once, since re-opening an open door every tick stops it
        /// closing at all.
        /// </summary>
        private void Open(Vehicle car, int seat)
        {
            var door = seat + 1;
            if (_doorOn == door) return;

            try
            {
                Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, car.Handle, door, false, false);
                _doorOn = door;
            }
            catch
            {
                // He gets in through it anyway.
            }
        }

        /// <summary>And shut it behind him.</summary>
        private void Shut(Vehicle car)
        {
            if (_doorOn < 0) return;

            try { Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, car.Handle, _doorOn, false); }
            catch { }

            _doorOn = -1;
        }

        /// <summary>
        /// Out of the car, through the door, the way Chop gets out.
        ///
        /// Two beats with the animation between them: the door swings, he plays the jump-out,
        /// and only then does he actually leave the seat -- because a ped taken out of a
        /// vehicle first has nothing left to animate and simply appears stood on the road.
        ///
        /// CLEAR_PED_TASKS_IMMEDIATELY is what takes him off the seat; a leave-vehicle task
        /// would put him back through the door-handle mime this whole thing exists to avoid.
        /// He is then set down on the side the door is actually on -- doors 1 and 3 are the
        /// right-hand side of the car and door 2 is the left, so a fixed offset would drop him
        /// through the car on half the trips.
        /// </summary>
        private void Hop()
        {
            var car = _dog.CurrentVehicle;

            if (car == null || !car.Exists())
            {
                _outAt = 0;
                return;
            }

            var seat = SeatOf(car);
            var door = seat + 1;

            if (_outAt == 0)
            {
                _sitting = false;
                _outAt = Game.GameTime;

                try { Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, car.Handle, door, false, false); }
                catch { }

                _doorOn = door;

                foreach (var pair in Outs)
                {
                    try
                    {
                        Function.Call(Hash.REQUEST_ANIM_DICT, pair[0]);

                        if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pair[0])) continue;

                        Function.Call(Hash.TASK_PLAY_ANIM, _dog.Handle, pair[0], pair[1],
                                      4f, -4f, JumpMs, 0, 0f, false, false, false);
                        break;
                    }
                    catch
                    {
                        // Next pair.
                    }
                }

                return;
            }

            if (Game.GameTime - _outAt < JumpMs) return;

            _outAt = 0;
            Afoot();

            try
            {
                var side = car.RightVector * (door == 2 ? -2.2f : 2.2f);
                var spot = car.Position + side;

                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, _dog.Handle);

                Function.Call(Hash.SET_ENTITY_COORDS, _dog.Handle,
                              spot.X, spot.Y, spot.Z, false, false, false, true);

                Shut(car);
            }
            catch
            {
                // He is out either way.
            }
        }

        /// <summary>
        /// Which seat he is actually in.
        ///
        /// Asked rather than remembered. The seat he was put in and the seat he is in can
        /// differ -- the game moves passengers about when somebody else gets in, and a
        /// remembered number would open the wrong door on the way out.
        /// </summary>
        private int SeatOf(Vehicle car)
        {
            try
            {
                var seats = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, car.Handle);

                for (var seat = 0; seat < seats; seat++)
                {
                    if (Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, car.Handle, seat)
                        == _dog.Handle)
                    {
                        return seat;
                    }
                }
            }
            catch
            {
                // Fall through to the passenger door.
            }

            return 0;
        }

        /// <summary>The jump out, tried in order like the others.</summary>
        private static readonly string[][] Outs =
        {
            new[] { "creatures@rottweiler@in_vehicle@std_car", "get_out" },
            new[] { "creatures@rottweiler@in_vehicle@std_car", "getout" },
            new[] { "creatures@rottweiler@in_vehicle@van", "get_out" }
        };

        /// <summary>Which door is hanging open for him, or -1.</summary>
        private int _doorOn = -1;

        /// <summary>When the jump out started.</summary>
        private int _outAt;

        /// <summary>The best empty seat, or MinValue when there is not one.</summary>
        private static int Free(Vehicle car)
        {
            try
            {
                var seats = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, car.Handle);

                for (var seat = 0; seat < seats; seat++)
                {
                    if (Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, car.Handle, seat)) return seat;
                }
            }
            catch
            {
                // Fall through.
            }

            return int.MinValue;
        }

        // ---- making him ---------------------------------------------------------

        /// <summary>
        /// The dog, and only ever one dog.
        ///
        /// THE OLD ONE IS LOOKED FOR BEFORE A NEW ONE IS MADE, because "I do not have a
        /// reference to him" and "he is not out there" are different things and this class
        /// kept confusing them. Three ways they came apart:
        ///
        /// A SCRIPT RELOAD. Pressing Insert builds a fresh instance with no dog in hand while
        /// the dog from before is still stood in the world as a mission entity -- nothing
        /// cleans him up, because the object that owned him no longer exists. Every reload
        /// added another one, and this mod has been reloaded a great many times in one sitting.
        ///
        /// A DISMISS. Sending him home hands him back to the game where he stands, which is the
        /// point -- but he does not vanish, so walking back to the yard found no reference and
        /// built a second dog next to the first.
        ///
        /// AND STREAMING. Walking out of range releases him and walking back makes one, and if
        /// the release did not actually take he is now twice.
        ///
        /// So: adopt what is already there, then sweep. The sweep only takes MISSION entities,
        /// which is the line between a dog this mod made and a dog the game put in the street
        /// -- deleting every rottweiler near the player would be a mod that shoots other
        /// people's dogs.
        /// </summary>
        private void Make(Vector3 at)
        {
            if (Adopt(at)) return;

            foreach (var name in Dogs)
            {
                try
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage) continue;

                    // Not loaded yet is a reason to wait, not a reason to bring a different
                    // dog. Update comes back in under a second.
                    if (!model.Request(1000)) return;

                    var spot = at;

                    float ground;
                    if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 2f),
                                              out ground, GetGroundHeightMode.Normal))
                    {
                        spot = new Vector3(spot.X, spot.Y, ground);
                    }

                    _dog = World.CreatePed(model, spot, (float)(_rng.NextDouble() * 360d));
                    model.MarkAsNoLongerNeeded();

                    if (_dog == null || !_dog.Exists()) continue;

                    var h = _dog.Handle;

                    _dog.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);

                    // The dark coat. Rottweilers only have a couple of variations and which
                    // index is which is not written down anywhere, so this asks for the first
                    // and takes what it gets -- it is the breed that does the work here.
                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, h, 0, 0, 0, 0);

                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);

                    // He does not die, in the yard or out of it. A party dog that can be shot
                    // dead by a passing argument is the same problem in a quieter place.
                    Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, h, false);
                    Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, h, false);
                    Function.Call(Hash.SET_PED_DIES_IN_WATER, h, false);

                    if (Yours)
                    {
                        // HE FIGHTS NOW. The yard dog was told to ignore everything, which is
                        // right for scenery and useless in a bodyguard -- so a dog that is
                        // yours gets the opposite treatment: he reacts, he does not run, and
                        // he does not need to be told who to bite because the group tells him.
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, false);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 17, false);

                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h,
                                      Game.GenerateHash("PLAYER"));
                    }
                    else
                    {
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, false);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);

                        Function.Call(Hash.TASK_WANDER_IN_AREA, h,
                                      spot.X, spot.Y, spot.Z, _radius, ShortestWalk, PauseBetween);

                        Function.Call(Hash.SET_PED_KEEP_TASK, h, true);
                    }

                    Only();

                    Log.Info((Yours ? "Trigger is with you: " : "A dog is out in the yard: ") + name + ".");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a dog out: " + ex.Message);
                    return;
                }
            }
        }

        /// <summary>
        /// Held rather than tapped, because the prompt says held.
        ///
        /// A PROMPT THAT SAYS HOLD AND FIRES ON A TAP IS A PROMPT THAT LIES, and this is the
        /// same key that talks to everybody else in the mod -- so a tap near the dog was also
        /// a tap near whoever was stood behind him. A hold is the difference between putting a
        /// hand on him deliberately and walking past with a thumb down.
        ///
        /// The clock resets on release, so a run of taps never adds up to a hold, and the hold
        /// is spent when it fires, so one long press is one pet rather than one per frame for
        /// as long as it is down.
        /// </summary>
        private bool Held()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context);
            }
            catch
            {
            }

            if (!down)
            {
                _downSince = 0;
                _down = false;
                return false;
            }

            if (_downSince == 0) _downSince = Game.GameTime;

            if (Game.GameTime - _downSince < HoldMs) return false;

            _downSince = 0;
            _down = true;

            return true;
        }

        /// <summary>How long a hold is.</summary>
        private const int HoldMs = 550;

        private int _downSince;

        /// <summary>Handed back to the game rather than deleted, so he is not removed in view.</summary>
        /// <summary>
        /// The dog on the map.
        ///
        /// Chop's own blip, which is sprite 442 -- the game has a dog icon because the story
        /// needed one, and drawing a generic dot for a dog when a dog exists would be worse for
        /// no reason. Set by number rather than through the sprite enum: which names that enum
        /// carries varies between builds of the scripting library, and a number that is wrong
        /// is a blip that looks odd, while a name that is missing is a mod that will not build.
        ///
        /// Only ever while he is yours, because this is only called from Mine(). Small, because
        /// he is a dog and not a mission.
        /// </summary>
        private void Mark()
        {
            try
            {
                if (_dog == null || !_dog.Exists() || !_dog.IsAlive)
                {
                    Unmark();
                    return;
                }

                if (_blip != null && _blip.Exists()) return;

                _blip = _dog.AddBlip();
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, 442);
                Function.Call(Hash.SET_BLIP_SCALE, _blip.Handle, 0.7f);
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, _blip.Handle, true);
                Function.Call(Hash.SET_BLIP_COLOUR, _blip.Handle, 5);

                Function.Call(Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, Name);
                Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, _blip.Handle);
            }
            catch
            {
                // No blip is not a broken dog.
            }
        }

        /// <summary>Take it off the map.</summary>
        private void Unmark()
        {
            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch
            {
            }

            _blip = null;
        }

        private Blip _blip;

        private void Release()
        {
            Unmark();

            try
            {
                if (_dog != null && _dog.Exists())
                {
                    _dog.IsPersistent = false;
                    _dog.MarkAsNoLongerNeeded();
                }
            }
            catch { /* he is the game's now either way */ }

            _dog = null;
            _inGroup = false;
        }

        public void RestoreWorld()
        {
            Release();
        }
    }
}
