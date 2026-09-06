using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Locations;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// The two men stood near somebody who matters.
    ///
    /// Nobody who runs anything stands on a corner by himself. A leader alone in an empty
    /// courtyard reads as a shopkeeper waiting for custom; the same man with two of his own
    /// people loitering nearby reads as somebody it would be a mistake to rob. They do nothing
    /// and they are not part of any system -- they are furniture that happens to be armed, and
    /// that is the entire job.
    ///
    /// Shared rather than written twice, because Lamar and Gerald want exactly the same thing
    /// and a second copy of this would be a second place to fix it.
    /// </summary>
    internal sealed class Entourage
    {
        /// <summary>How far out they stand, and how far apart from each other.</summary>
        private const float StandOff = 3.2f;

        private const float SpawnRange = 100f;
        private const float DespawnRange = 180f;
        private const int UpdateIntervalMs = 1100;

        /// <summary>How far they may drift before they are put back.</summary>
        private const float DriftRange = 5f;

        /// <summary>
        /// A wanderer's own numbers, taken from YardDog, which has been doing this in the same
        /// yard since the dog went in.
        ///
        /// Short hops with short pauses. A man who crosses the lot in one go and then stands
        /// still for a minute is a man on a schedule; this is somebody at a party.
        /// </summary>
        private const float WanderShortestWalk = 1.5f;
        private const float WanderPause = 4f;

        /// <summary>How long a wander order is left alone before it may be given again.</summary>
        private const int WanderHoldMs = 30000;

        private const int PedTypeCiv = 4;


        /// <summary>
        /// Idles that keep a weapon in hand. A scenario that puts the gun away defeats the
        /// whole thing, so these are the standing-about ones rather than smoking or drinking.
        /// </summary>
        private static readonly string[] Scenarios =
        {
            "WORLD_HUMAN_GUARD_STAND", "WORLD_HUMAN_GUARD_PATROL", "WORLD_HUMAN_STAND_IMPATIENT"
        };

        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly Vector3 _spot;
        private readonly float _heading;
        private readonly string _who;

        /// <summary>
        /// Exactly where they stand and what each of them is doing.
        ///
        /// Positions given outright rather than worked out from the leader's heading, because
        /// "a step back and to the right" is arithmetic and "on that corner, facing the stairs"
        /// is a decision somebody made standing there. The scenario is per-man too: two people
        /// doing the identical thing beside each other reads as one man copied.
        /// </summary>
        private readonly List<Vector3> _stations = new List<Vector3>();
        private readonly List<float> _facings = new List<float>();
        private readonly List<string> _doing = new List<string>();

        /// <summary>
        /// Per station: a model of its own, or null for whoever the set is made of.
        ///
        /// A woman working a courtyard is not a Families member, and a man holding a beer is not
        /// holding a rifle. Both used to be, because everybody here came out of one list and was
        /// handed one weapon.
        /// </summary>
        private readonly List<string[]> _models = new List<string[]>();
        private readonly List<bool> _armed = new List<bool>();

        /// <summary>
        /// Per station: what he is carrying, or null for the set's usual.
        ///
        /// Everybody armed used to get the same rifle. A man stood at a party turning a pistol
        /// over in his hands is a different picture to a man on a shutter with a choppa, and
        /// the difference is the weapon.
        /// </summary>
        private readonly List<string> _weapons = new List<string>();

        /// <summary>
        /// Per station: whether the height given is furniture rather than floor.
        /// </summary>
        private readonly List<bool> _onProp = new List<bool>();

        /// <summary>
        /// Per station: whether he is still there at four in the morning.
        ///
        /// Almost nobody is. A yard that is full at four is not a yard people live near, it is
        /// a set dressing that happens to be lit -- and the difference between the two is that
        /// one of them empties out. The handful who stay are the ones with a reason to.
        /// </summary>
        private readonly List<bool> _allNight = new List<bool>();

        /// <summary>
        /// The hours everybody without a reason goes home, or -1 for never.
        ///
        /// A window rather than a range, because it wraps past midnight -- two to six is four
        /// hours on the far side of the day boundary and reading it as "from 2 to 6" the way
        /// you would read a number line gets you the twenty hours nobody asked for.
        /// </summary>
        public int QuietFrom = -1;
        public int QuietTo = -1;

        /// <summary>Set by the owner: whether this is off for the whole of tonight. See Core.Nights.</summary>
        public System.Func<bool> OffTonight;

        /// <summary>
        /// The hours the party is on, or -1 for a place that is the same all day.
        ///
        /// A second window rather than a reuse of the quiet one, because they answer different
        /// questions and only overlap by accident. Quiet is "has everybody gone to bed"; this
        /// is "is there a party on", and between them they describe three states rather than
        /// two: a yard with a couple of people in it during the day, a party at night, and an
        /// empty lot at four in the morning.
        /// </summary>
        public int PartyFrom = -1;
        public int PartyTo = -1;

        /// <summary>Whether the party is on right now.</summary>
        private bool PartyOn
        {
            get
            {
                if (PartyFrom < 0 || PartyTo < 0 || PartyFrom == PartyTo) return true;

                try
                {
                    var hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);

                    return PartyTo > PartyFrom
                        ? hour >= PartyFrom && hour < PartyTo
                        : hour >= PartyFrom || hour < PartyTo;
                }
                catch
                {
                    // If the clock cannot be read, the party is on. A yard that is empty
                    // because a native failed is worse than one that is busy at noon.
                    return true;
                }
            }
        }

        /// <summary>
        /// Per station: whether he is only there for the party.
        ///
        /// The DJ, the women and the extra bodies. Nobody sets up decks in an empty lot at
        /// eleven in the morning, and a yard with fifteen people dancing in it in broad
        /// daylight is the same set-dressing problem the quiet hours were added to solve, just
        /// at the other end of the clock.
        /// </summary>
        private readonly List<bool> _partyOnly = new List<bool>();

        /// <summary>Where the ones who turn up for it come FROM. Set by Main.</summary>
        public Vector3 ArriveAt;

        /// <summary>
        /// Nobody exists here until the player is actually INSIDE the room.
        ///
        /// THIS IS WHY THE GROW ROOM AND THE PRESS STOPPED OPENING, and it is worth writing
        /// down properly because the failure looked nothing like the cause.
        ///
        /// Both rooms are entered by warping the player to a coordinate out in the interior
        /// limbo and waiting for the geometry to stream. When that wait fails, InteriorDoor
        /// leaves him stood at that coordinate for eight seconds before giving up -- and those
        /// coordinates are 64 and 88 metres from these two crews' spots, which is inside the
        /// hundred-metre spawn range. So the moment a room was slow, five peds started being
        /// created into an interior that had not loaded, while the load scene was still
        /// running. The workers were fighting the load they needed.
        ///
        /// Distance is the wrong question indoors anyway. The right one is whether the player
        /// is in the room, which the game will answer directly, and which is false for every
        /// one of those eight seconds -- the log says "he is in interior=0" on each failure.
        /// So with this set they cannot spawn during a failed entry at all, which is exactly
        /// the behaviour that was wanted.
        /// </summary>
        public bool IndoorsOnly;

        /// <summary>How near the mark he still has to be, once he is in the right room.</summary>
        private const float IndoorRange = 30f;

        /// <summary>Whether the party hours are running. Read by the music.</summary>
        public bool IsPartyOn { get { return PartyOn; } }

        /// <summary>Whether the next spawn is people turning up, rather than people being there.</summary>
        private bool _arriveOnFoot;

        private bool _wasParty = true;

        /// <summary>Per station: still walking in, so not yet doing what he came to do.</summary>
        private readonly List<bool> _walkingIn = new List<bool>();
        private readonly List<int> _walkDue = new List<int>();

        /// <summary>Close enough to his mark to stop walking and start the evening.</summary>
        private const float ArriveWithin = 1.6f;

        /// <summary>
        /// How long he is given to get there before he is simply put to work where he stands.
        ///
        /// The road is sixty-seven metres from the yard in a straight line and the nav route
        /// is longer than that, because it goes round the fence and in through the gate --
        /// call it a minute and a half at a walk. Forty-five seconds was the first number here
        /// and it was under half the journey, so the timeout would have fired on every single
        /// person every single night.
        /// </summary>
        private const int WalkInMs = 95000;

        /// <summary>Whether it is those hours now.</summary>
        private bool Quiet
        {
            get
            {
                // Not tonight, whatever the hour. See Core.Nights.
                if (OffTonight != null && OffTonight()) return true;

                if (QuietFrom < 0 || QuietTo < 0 || QuietFrom == QuietTo) return false;

                try
                {
                    var hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);

                    return QuietTo > QuietFrom
                        ? hour >= QuietFrom && hour < QuietTo
                        : hour >= QuietFrom || hour < QuietTo;
                }
                catch
                {
                    // No clock, no curfew.
                    return false;
                }
            }
        }

        /// <summary>What the clock said last time it was looked at, so the flip can be caught.</summary>
        private bool _wasQuiet;

        /// <summary>
        /// Per station: a looping animation instead of a scenario, as dict then clip.
        ///
        /// Scenarios cover standing, smoking, drinking and guarding, and they cover nothing
        /// that looks like a party. There is no dancing scenario and no DJ scenario, so the two
        /// people who make a yard read as a party have to be animated rather than scripted.
        ///
        /// Candidates in pairs, first that plays wins, and a station whose clips are all
        /// missing falls back to its scenario -- so an install without the club DLC gets
        /// somebody stood there rather than somebody T-posing.
        /// </summary>
        private readonly List<string[]> _anims = new List<string[]>();

        /// <summary>
        /// Which pair of <see cref="_anims"/> is on him, per station, or -1 for none yet --
        /// and the time after which it is fair to ask whether it took.
        ///
        /// Both exist because the two interesting questions cannot be answered when they were
        /// being asked. A streaming request is not readable in the frame it is made, and a task
        /// is not readable in the frame it is issued.
        /// </summary>
        private readonly List<int> _animPick = new List<int>();
        private readonly List<int> _animDue = new List<int>();

        /// <summary>
        /// How far station i is allowed to roam, or 0 for somebody on a mark.
        ///
        /// A yard where every single person is welded to a spot reads as a diorama. Most of
        /// them SHOULD be still -- a man is smoking, a woman is on the decks, and those are
        /// things you do standing in one place -- but the ones whose whole station is "holding
        /// a drink" have no reason not to move, and two of them drifting is the difference
        /// between a party and a set dressing.
        /// </summary>
        private readonly List<float> _wander = new List<float>();

        /// <summary>When a wanderer may be told again, so he is not re-tasked every pass.</summary>
        private readonly List<int> _wanderDue = new List<int>();

        /// <summary>How long a clip gets to visibly start before it is written off.</summary>
        private const int AnimGraceMs = 900;

        /// <summary>
        /// What people say to each other when nobody is saying anything in particular.
        ///
        /// Every one of these is an AMBIENT SPEECH LABEL, not a line -- the game picks the
        /// actual words from the ped's own voice, so a Families man, a woman off the block and
        /// somebody's uncle all say different things and all say them in their own voices. A
        /// yard where fifteen people share one voice is worse than a silent one.
        ///
        /// Conversational labels first, because that is what a party mostly is. The louder
        /// ones are in there too but they are outnumbered, so the yard sounds like people
        /// talking with the odd shout across it rather than fifteen arguments at once.
        /// </summary>
        /// <summary>
        /// What they say to each other. NOTHING AIMED AT FRANKLIN.
        ///
        /// GENERIC_CURSE_MED and GENERIC_INSULT_MED used to be in here, and they are the two
        /// that produced a man at your own party calling you a punk as you walked in. They
        /// were picked as texture -- a yard where everybody is polite does not sound like a
        /// yard -- and that reasoning was wrong for a simple reason: this list is spoken AT the
        /// player, because he is the one stood in earshot. There is no version of an insult
        /// label that reads as two mates slagging each other off rather than somebody starting
        /// on you.
        ///
        /// GENERIC_CHEER and GENERIC_AGREE take their slots. Both are labels Rockstar use
        /// themselves, which is the only reason to trust either of them exists.
        ///
        /// This list is shared by every Entourage in the mod -- the party, the lab, the
        /// leaders, the den -- so nobody anywhere starts on him now.
        /// </summary>
        private static readonly string[] Talk =
        {
            "GENERIC_HI", "GENERIC_HOWS_IT_GOING", "CHAT_STATE", "GENERIC_YES",
            "GENERIC_THANKS", "GENERIC_WHATEVER", "GENERIC_NO", "CHAT_RESP",
            "GENERIC_HOWS_IT_GOING", "GENERIC_BYE", "CHAT_STATE", "GENERIC_YES",
            "GENERIC_CHEER", "CHAT_RESP", "GENERIC_HI", "GENERIC_AGREE"
        };

        /// <summary>
        /// Where each person is up to in that list, and when they are next due to speak.
        ///
        /// Walked in ORDER rather than picked at random, one step per person per turn. Random
        /// picking from sixteen labels repeats inside a minute and the repeat is the thing you
        /// hear -- the same man saying the same word twice is what makes a crowd sound like a
        /// tape loop. Everybody starts at a different point in the list, so at any moment the
        /// yard is spread across the whole vocabulary rather than working through it together.
        /// </summary>
        private readonly List<int> _sayAt = new List<int>();
        private readonly List<int> _sayDue = new List<int>();

        /// <summary>
        /// How often one person speaks. Per person, jittered.
        ///
        /// Fifteen people on this spread is a line landing somewhere in the yard roughly every
        /// half second, which is the sound of a party. Tightening it further does not make it
        /// busier, it makes it a queue.
        /// </summary>
        private const int SayMinMs = 4500;
        private const int SayMaxMs = 9000;

        /// <summary>Nobody who is not there to be heard. Speech carries about this far.</summary>
        private const float EarShot = 32f;

        private readonly Random _mouth = new Random();

        private readonly List<Ped> _crew = new List<Ped>();
        private readonly List<Vector3> _marks = new List<Vector3>();

        private int _lastUpdate;

        public Entourage(GangRegistry gangs, string gangId, Vector3 spot, float heading, string who)
        {
            _gangs = gangs;
            _gangId = gangId;
            _spot = spot;
            _heading = heading;
            _who = who;
        }

        /// <summary>Adds one of them, on his own mark, doing his own thing.</summary>
        public Entourage Stand(Vector3 where, float facing, string scenario,
                               string[] models = null, bool armed = true, bool onProp = false,
                               string[] anim = null, string weapon = null, bool nights = false,
                               float wander = 0f, bool party = false, bool sit = false,
                               bool onSpot = false)
        {
            _sits.Add(sit);
            _onSpot.Add(onSpot);
            _allNight.Add(nights);
            _stations.Add(where);
            _facings.Add(facing);
            _doing.Add(scenario);
            _models.Add(models);
            _armed.Add(armed);
            _onProp.Add(onProp);
            _anims.Add(anim);
            _animPick.Add(-1);
            _animDue.Add(0);
            _wander.Add(wander);
            _wanderDue.Add(0);
            _partyOnly.Add(party);
            _walkingIn.Add(false);
            _walkDue.Add(0);

            // Staggered at the door rather than at spawn: a fixed offset per station means the
            // yard never starts everybody's clock on the same frame, whatever order they got
            // created in.
            _sayAt.Add(_stations.Count * 3);
            _sayDue.Add(0);
            _weapons.Add(weapon);
            return this;
        }

        /// <summary>The clip pairs for station i, or null for a plain scenario.</summary>
        private string[] AnimAt(int index)
        {
            return index < _anims.Count ? _anims[index] : null;
        }

        /// <summary>
        /// Starts one of these clips on him, and says WHICH one -- or -1 if none can start yet.
        ///
        /// This asked two questions that could not yet have answers, and the dancer and the DJ
        /// stood still for the entire life of the mod because of it.
        ///
        /// REQUEST_ANIM_DICT is a request, not a load: it returns immediately and the file
        /// arrives some frames later, so HAS_ANIM_DICT_LOADED on the very next line read false
        /// essentially always. Every candidate was skipped and every animated station fell
        /// straight through to its scenario. The request is now made for all of them on the way
        /// past, and residency is asked of a LATER call.
        ///
        /// TASK_PLAY_ANIM likewise has not started by the time the next line runs, so
        /// IS_ENTITY_PLAYING_ANIM read false even on success. That did more than fail: it
        /// stacked the second and third candidates on top of the first, reported failure, and
        /// the caller then started the scenario -- which cancels the animation. Several times a
        /// second, forever. Verification still happens, in Update, a tick later, where the
        /// answer means something.
        /// </summary>
        /// <param name="from">Pair offset to start at, so a clip proved missing is not retried.</param>
        private static int PlayAnim(Ped ped, string[] pairs, int from)
        {
            if (pairs == null || pairs.Length < 2) return -1;

            // Ask for all of them, including ones already passed over. The list is short, the
            // call is cheap, and a dict already resident costs nothing to request again.
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                try { Function.Call(Hash.REQUEST_ANIM_DICT, pairs[i]); }
                catch { /* a name this install has never heard of */ }
            }

            for (var i = from < 0 ? 0 : from; i + 1 < pairs.Length; i += 2)
            {
                try
                {
                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pairs[i])) continue;

                    Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, pairs[i], pairs[i + 1],
                                  4f, -4f, -1, LoopingAnim, 0f, false, false, false);
                    return i;
                }
                catch
                {
                    // Try the next pair.
                }
            }

            return -1;
        }

        /// <summary>Looping, full body -- a dancer who stops dancing is worse than no dancer.</summary>
        private const int LoopingAnim = 1;

        /// <summary>
        /// Where station <paramref name="index"/> actually is.
        ///
        /// Most marks are a spot on the concrete read off a HUD, so they are snapped to whatever
        /// the ground turns out to be -- an authored Z a few centimetres out otherwise leaves a
        /// man hovering or shin-deep in it.
        ///
        /// A man on the couch is the exception, and it is why he was in the air. His height IS
        /// the seat, and the probe does not reliably see furniture: it answers with the floor on
        /// one pass and the cushion on the next, so the authored seat height was thrown away and
        /// replaced by whichever the probe happened to find -- on spawn, and then again on every
        /// Settle. Marks flagged onProp keep exactly the height they were given.
        /// </summary>
        private Vector3 MarkAt(int index)
        {
            if (index >= _marks.Count) return _spot;

            return index < _onProp.Count && _onProp[index]
                       ? _marks[index]
                       : Ground(_marks[index]);
        }

        private string[] ModelsFor(int index, GangDef gang)
        {
            if (index < _models.Count && _models[index] != null && _models[index].Length > 0)
            {
                return _models[index];
            }

            var own = new string[gang.MemberModels.Count];
            for (var i = 0; i < own.Length; i++) own[i] = gang.MemberModels[i];
            return own;
        }

        private bool ArmedAt(int index) => index >= _armed.Count || _armed[index];

        private string WeaponAt(int index) => index < _weapons.Count ? _weapons[index] : null;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            var away = player.Position.DistanceTo(_spot);

            // Indoors, the room decides, not the tape measure. See IndoorsOnly.
            if (IndoorsOnly)
            {
                var room = 0;

                try { room = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle); }
                catch { room = 0; }

                if (room == 0 || away > IndoorRange)
                {
                    if (_crew.Count > 0) Despawn();
                    return;
                }
            }

            // Two o'clock, or six. Everybody goes at once rather than drifting off one at a
            // time, which is a compromise and worth naming: people leaving a yard individually
            // over twenty minutes would be better and there is nowhere for them to walk TO.
            // What there is instead is a yard that is full at one and quiet at three, which is
            // the thing you actually notice.
            var quiet = Quiet;

            if (quiet != _wasQuiet)
            {
                _wasQuiet = quiet;

                if (_crew.Count > 0) Despawn();
            }

            // The party starting is its own event, and the one place people ARRIVE rather than
            // simply being there.
            //
            // Only on the way in. When it ends they are cleared like everybody else at closing
            // time -- walking fifteen people back up the road one at a time would be the better
            // scene and it is a lot of pathfinding for something you are almost never watching,
            // because the party ends at two in the morning.
            var party = PartyOn;

            if (party != _wasParty)
            {
                _wasParty = party;

                if (_crew.Count > 0) Despawn();

                _arriveOnFoot = party;
            }

            // Standing rather than Count, now the list keeps a slot for everybody. A crew of
            // fifteen nulls is not a crew.
            if (Standing() > 0)
            {
                if (away > DespawnRange) Despawn();
                else { Settle(); Talking(player); }

                return;
            }

            if (_crew.Count > 0) Despawn();
            if (away <= SpawnRange) Spawn();
        }

        private void Spawn()
        {
            var gang = _gangs.Get(_gangId);
            if (gang == null) return;

            _marks.Clear();

            var quiet = Quiet;

            // Read here as well as in Update. Spawn is reached from more than one place and the
            // hour is the same question wherever it is asked from.
            var party = PartyOn;

            // Every station, always, whatever the hour.
            //
            // Filtering the MARKS was the first attempt and it is a trap: everything about a
            // man -- his facing, his models, his weapon, his scenario, his animation and its two
            // timers -- is a per-station list read by the same index, so a shortened mark list
            // hands the second man the fourteenth man's face and the fourteenth man's gun. Who
            // is actually stood up is decided in the placement loop below, which keeps a slot
            // for everybody and puts a null in the ones nobody is at.
            if (_stations.Count > 0)
            {
                _marks.AddRange(_stations);
            }
            else
            {
                // No marks given, so either side of him and a step back.
                var rad = _heading * (float)(Math.PI / 180.0);
                var right = new Vector3((float)Math.Cos(rad), -(float)Math.Sin(rad), 0f);
                var back = new Vector3(-(float)Math.Sin(rad), -(float)Math.Cos(rad), 0f);

                _marks.Add(_spot + right * StandOff + back * 1.2f);
                _marks.Add(_spot - right * StandOff + back * 1.6f);
            }

            // One entry per MARK, in mark order, null for anybody not stood up.
            //
            // The alignment is the whole thing. Facing, models, weapon, scenario, animation and
            // the two animation timers are all per-station lists read by the same index, so a
            // crew list that skips an entry hands man number four the fifth man's face, the
            // sixth man's gun and somebody else's dance. Quiet hours skip a lot of entries, so
            // this had to be right before they could exist at all.
            for (var i = 0; i < _marks.Count; i++)
            {
                // Quiet hours: nobody without a reason to be out here is even made. An
                // invisible ped is still a ped the game is paying for, and there is nothing to
                // see either way.
                if (quiet && !AllNight(i))
                {
                    _crew.Add(null);
                    continue;
                }

                // And the other end of the clock. Same trick, same reason: a slot is kept for
                // him so every per-station list still lines up, and nobody is made.
                if (!party && PartyOnly(i))
                {
                    _crew.Add(null);
                    continue;
                }

                // WHERE HE STARTS. On his mark if he was always here, and out on the road if
                // he is turning up -- which is only ever the frame the party starts, so
                // arriving to a party already in progress does not send everybody back out to
                // the kerb to walk in again while you watch.
                var arriving = _arriveOnFoot && PartyOnly(i) && ArriveAt != Vector3.Zero;
                var from = arriving ? ArrivalSpot(i) : MarkAt(i);

                var ped = SpawnMember(gang, from, Facing(i), ModelsFor(i, gang), ArmedAt(i),
                                      WeaponAt(i));

                _crew.Add(ped);
                if (ped == null) continue;

                if (arriving)
                {
                    WalkIn(i, ped);
                    continue;
                }

                Idle(i, ped, Doing(i), Facing(i), MarkAt(i), Seated(i), AnimAt(i));
            }

            // Spent. The next spawn is people being here, not people turning up.
            _arriveOnFoot = false;

            var up = Standing();
            if (up > 0) Log.Info(up + " of " + gang.Name + " stood with " + _who + ".");
        }

        /// <summary>
        /// A spot on the road for the man at station i, fanned out so they are not one pile.
        ///
        /// Fifteen people created on the same square metre spend their first second shoving
        /// each other apart, which is the one thing more obviously wrong than them appearing on
        /// their marks would have been. Spread along a line and staggered back from it, off the
        /// index so it is the same every time rather than a random scatter that occasionally
        /// puts somebody in the road.
        /// </summary>
        private Vector3 ArrivalSpot(int index)
        {
            var across = ((index % 4) - 1.5f) * 1.1f;
            var back = (index / 4) * 1.3f;

            return Ground(new Vector3(ArriveAt.X + across, ArriveAt.Y + back, ArriveAt.Z));
        }

        /// <summary>
        /// Sends him up the road to his mark, to be put to work when he gets there.
        ///
        /// FOLLOW_NAV_MESH rather than a straight line, for the reason it always is: there is a
        /// fence and a gate between the road and the yard, and a man walked straight at his
        /// mark stands in the wire until the timeout.
        /// </summary>
        private void WalkIn(int index, Ped ped)
        {
            try
            {
                var mark = MarkAt(index);

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                              mark.X, mark.Y, mark.Z, 1.0f, WalkInMs, ArriveWithin, 0, 0f);

                if (index < _walkingIn.Count)
                {
                    _walkingIn[index] = true;
                    _walkDue[index] = Game.GameTime + WalkInMs;
                }
            }
            catch
            {
                // He could not be sent, so he is already where he is going as far as we care.
                if (index < _walkingIn.Count) _walkingIn[index] = false;

                Idle(index, ped, Doing(index), Facing(index), MarkAt(index), Seated(index),
                     AnimAt(index));
            }
        }

        /// <summary>Whether he is still on his way in.</summary>
        private bool WalkingIn(int index)
        {
            return index < _walkingIn.Count && _walkingIn[index];
        }

        /// <summary>How far station i roams, or 0 if he is on a mark.</summary>
        private float WanderAt(int index)
        {
            return index >= 0 && index < _wander.Count ? _wander[index] : 0f;
        }

        /// <summary>
        /// How far from his mark he is allowed to get before he is walked back.
        ///
        /// A wanderer needs a longer leash than his own radius or the walk-back fights the
        /// wander -- he reaches the edge of where he was told to roam, gets marched home, and
        /// the two tasks argue for the rest of the evening.
        /// </summary>
        private float LeashAt(int index)
        {
            var roam = WanderAt(index);
            return roam > 0f ? roam + 2f : DriftRange;
        }

        /// <summary>Whether the man at that station is one of the ones who stays.</summary>
        private bool AllNight(int index)
        {
            return index < _allNight.Count && _allNight[index];
        }

        /// <summary>Whether he only turns out for the party.</summary>
        private bool PartyOnly(int index)
        {
            return index < _partyOnly.Count && _partyOnly[index];
        }

        /// <summary>How many of them are actually stood there, nulls and bodies aside.</summary>
        private int Standing()
        {
            var n = 0;

            foreach (var ped in _crew)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) n++;
            }

            return n;
        }

        private float Facing(int index)
        {
            return index < _facings.Count ? _facings[index] : _heading;
        }

        private string Doing(int index)
        {
            if (index < _doing.Count && !string.IsNullOrEmpty(_doing[index])) return _doing[index];

            return Scenarios[index % Scenarios.Length];
        }

        private Ped SpawnMember(GangDef gang, Vector3 mark, float facing, string[] models, bool armed,
                                string carrying)
        {
            // Started at a different place in the list for each of them, and wrapped.
            //
            // Walking the list from the top and taking the first that loads means the first
            // entry wins every single time, so every man on the block was the same man. The
            // list is short, so an offset is enough -- and it is stable per station rather than
            // random, so somebody does not change face every time you walk back up the street.
            var order = new List<string>();
            var from = Math.Abs(_crew.Count + _stations.Count * 3) % Math.Max(1, models.Length);

            for (var i = 0; i < models.Length; i++) order.Add(models[(from + i) % models.Length]);

            foreach (var name in order)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                    mark.X, mark.Y, mark.Z, facing, false, false);

                    // A different outfit on every one of them.
                    //
                    // The rotation above stops neighbours sharing a MODEL, which was enough
                    // while every list had three or four in it. The game ships exactly one
                    // Families female, so three stations asking for her got the same woman
                    // three times over, stood in one yard. The native only ever picks a
                    // combination the model actually ships, so there is nothing to get wrong.
                    Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, handle, 0);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;

                    // They can be startled. Blocking non-temporary events -- which is what this
                    // used to do -- makes somebody incapable of reacting to gunfire, a car on
                    // the pavement or a fight in front of them, which is a mannequin rather than
                    // a neighbour. They scatter like anybody would, and Settle walks them back.
                    ped.BlockPermanentEvents = false;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);

                    // OUTSIDE the armed check, which is where it should always have been.
                    //
                    // Whose side a man is on has nothing to do with whether he is holding a
                    // gun. Four of the seven men in Gerald's courtyard are unarmed, and they
                    // were being left in the CIVMALE group the ped was created with -- so in a
                    // raid on that yard the attackers did not consider them targets, the
                    // lookout bonus did not count them, turf checks did not see them as allies,
                    // and shooting one cost no respect because nothing could work out which set
                    // he belonged to. They stood in a firefight being nobody.
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);
                    Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, ped.Handle, false, false);

                    if (armed)
                    {
                        // One of the guard guns, by where he stands, unless the station said
                        // what he carries. See Arms.
                        var gun = string.IsNullOrEmpty(carrying) ? Arms.GuardAt(mark) : carrying;

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, gun), 120, true, true);

                        // In the hands, not on the back.
                        Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, gun), true);

                        Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);
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

        /// <summary>
        /// Puts somebody into their idle.
        ///
        /// Two different natives, because a chair is not a pavement. IN_PLACE starts a scenario
        /// where the ped happens to be standing and has no way to say "this one is a seat" --
        /// so a sitting scenario handed to it either does nothing or plays the sit standing up,
        /// and the man on the couch stayed on his feet next to it. AT_POSITION takes the spot,
        /// the heading, and a flag that means exactly that.
        ///
        /// Worse, a scenario that never takes leaves task 118 inactive, and Settle re-issues on
        /// exactly that condition -- so the failure was not a man standing still, it was a man
        /// being re-tasked to sit down forever.
        /// </summary>
        private void Idle(int index, Ped ped, string scenario, float facing, Vector3 at,
                          bool seated, string[] anim = null)
        {
            try
            {
                // A WANDERER, before anything else. He has no clip and no scenario -- the whole
                // point of him is that he is not doing either.
                //
                // Issued on a timer rather than re-asserted every pass. TASK_WANDER_IN_AREA is
                // a task he stays in, and telling a man to start wandering while he is already
                // wandering restarts him on the spot -- which looks exactly like somebody
                // stuck. The same reason the scenario above is only re-issued when task 118
                // has actually gone.
                var roam = WanderAt(index);

                if (roam > 0f)
                {
                    if (index >= 0 && index < _wanderDue.Count)
                    {
                        if (Game.GameTime < _wanderDue[index]) return;
                        _wanderDue[index] = Game.GameTime + WanderHoldMs;
                    }

                    Function.Call(Hash.TASK_WANDER_IN_AREA, ped.Handle,
                                  at.X, at.Y, at.Z, roam, WanderShortestWalk, WanderPause);

                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
                    return;
                }

                // An animated station tries its clips first and only falls back to the scenario
                // if none of them are in this install.
                if (anim != null)
                {
                    ped.Heading = facing;

                    var from = index >= 0 && index < _animPick.Count ? _animPick[index] : 0;
                    var picked = PlayAnim(ped, anim, from);

                    if (picked >= 0)
                    {
                        if (index >= 0 && index < _animPick.Count)
                        {
                            _animPick[index] = picked;
                            _animDue[index] = Game.GameTime + AnimGraceMs;
                        }

                        return;
                    }

                    // Nothing resident yet. The scenario below is what he does in the meantime,
                    // and the next pass tries again -- by which time the dict has usually landed.
                }

                // ---- SOMEWHERE REAL TO SIT, IF THERE IS ANY NEAR HIM ----
                //
                // The couches were always there. Fixture drags one into each courtyard and its
                // own comment says the point of it is that it does nothing, which was fine
                // while it was scenery and stopped being fine the moment there were six people
                // stood round it holding drinks. A couch that everybody at the party ignores
                // is worse than no couch.
                //
                // ASKED FOR RATHER THAN AUTHORED, because a seat is not a property of the man:
                // it is a property of what happens to be in the yard, and what is in the yard
                // depends on which couch model this install has and where it settled when it
                // was dropped. Seating measures the prop that actually spawned and hands out
                // the cushions on it, one to a person.
                //
                // It falls straight through when there is nothing -- no couch, all of them
                // taken, or a station too far from one -- and he does what he did before.
                if (!seated && Sits(index))
                {
                    var cushion = Seating.Take(ped, at, SitRange);

                    if (cushion != null)
                    {
                        at = cushion.At;
                        facing = cushion.Facing;
                        seated = true;

                        // His own scenario is a standing one -- drinking, smoking, guarding --
                        // and handing a standing scenario to the seated native gets a man
                        // standing at the coordinates of a cushion. The seat replaces it.
                        scenario = SeatDoing[_seatPick % SeatDoing.Length];
                    }
                }

                // ON THE SPOT. See _onSpot: the scenario is started at the mark, which is how
                // a seat scenario is made to work without its chair.
                if (!seated && OnSpot(index)) seated = true;

                if (seated)
                {
                    Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, ped.Handle, scenario,
                                  at.X, at.Y, at.Z, facing, 0, true, true);

                    // WHETHER IT TOOK, ASKED LATER RATHER THAN NOW. A scenario reports nothing
                    // on the frame it is issued, so testing here would fail every time and
                    // walk the ladder to its end on the first pass. Settle re-enters this
                    // method whenever task 118 has gone, which is exactly the condition that
                    // means the last one did not take -- so stepping the pick there costs
                    // nothing and gets a working name within a couple of seconds.
                    if (Sits(index) && !Function.Call<bool>(Hash.IS_PED_USING_SCENARIO,
                                                            ped.Handle, scenario))
                    {
                        _seatTried++;

                        if (_seatTried > SeatGiveUp)
                        {
                            _seatTried = 0;
                            _seatPick++;

                            Log.Info("entourage: " + _who + " -- nobody will sit with " +
                                     scenario + ", trying the next.");
                        }
                    }

                    return;
                }

                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true);
                ped.Heading = facing;
            }
            catch
            {
                // He will stand there regardless.
            }
        }

        /// <summary>Whether station i is somebody sat on something.</summary>
        private bool Seated(int index)
        {
            return index < _onProp.Count && _onProp[index];
        }

        /// <summary>Whether station i would rather be sat on real furniture.</summary>
        private bool Sits(int index)
        {
            return index < _sits.Count && _sits[index];
        }

        private bool OnSpot(int index)
        {
            return index < _onSpot.Count && _onSpot[index];
        }

        /// <summary>Which stations would take a seat if one were going.</summary>
        private readonly List<bool> _sits = new List<bool>();

        /// <summary>
        /// Stands whose scenario is started AT the mark rather than in place. For the seat
        /// scenarios that want a chair: started at a point on a step, the chair-and-beer
        /// one sits a man on the landing with his feet on the steps and a bottle in his
        /// hand, and no chair anywhere.
        /// </summary>
        private readonly List<bool> _onSpot = new List<bool>();

        /// <summary>Which seat scenario is being tried, and how many passes it has had.</summary>
        private int _seatPick;
        private int _seatTried;

        private const int SeatGiveUp = 8;

        /// <summary>
        /// How far a man will go for somewhere to sit.
        ///
        /// UNDER THE LEASH ON PURPOSE. Settle walks anybody more than DriftRange from his mark
        /// back to it, so a cushion further away than that is a man being pulled off the couch
        /// by one part of this file and sat back down by another, forever. Four metres is well
        /// inside five and still reaches the couch from anywhere anybody is stood round it.
        /// </summary>
        private const float SitRange = 4f;

        /// <summary>
        /// The scenario for somebody sat on something that has no scenario of its own.
        ///
        /// A ladder, because a name that is not in this install is accepted in silence and
        /// plays nothing -- the same trap the anim dictionaries had. CHAIR is the one that
        /// should take; the other two exist so a bad install degrades to a man sat down rather
        /// than a man stood in a couch.
        /// </summary>
        private static readonly string[] SeatDoing =
        {
            "PROP_HUMAN_SEAT_CHAIR",
            "PROP_HUMAN_SEAT_BENCH",
            "PROP_HUMAN_SEAT_CHAIR_MP_PLAYER",
        };

        /// <summary>Puts anybody who has wandered back on their mark.</summary>
        /// <summary>
        /// Pins a stationed man to his own spot for the length of a fight.
        ///
        /// Once each, not per tick. The defensive area is ped state and it survives the combat
        /// task laid over it, so re-asserting it every pass would have no visible effect and
        /// cost a handful of native calls a frame for nothing.
        ///
        /// The radius is generous next to the gang war's, because these are people stood about
        /// in a yard rather than holding a line, and a man who cannot step behind the couch he
        /// is already next to reads as broken rather than as disciplined.
        /// </summary>
        private void Hold(Ped ped, Vector3 station)
        {
            if (_held.Contains(ped.Handle)) return;
            _held.Add(ped.Handle);

            try
            {
                Function.Call(Hash.REMOVE_PED_DEFENSIVE_AREA, ped.Handle, false);
                Function.Call(Hash.REMOVE_PED_DEFENSIVE_AREA, ped.Handle, true);

                Function.Call(Hash.SET_PED_SPHERE_DEFENSIVE_AREA, ped.Handle,
                              station.X, station.Y, station.Z, HoldRadius, false, false);

                // 1 is CM_Defensive, which is the mode that hugs cover. 0 is CR_Near, which
                // stops him drifting off looking for a longer firing angle.
                Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 1);
                Function.Call(Hash.SET_PED_COMBAT_RANGE, ped.Handle, 0);

                foreach (var on in HoldOn)
                {
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, on, true);
                }

                foreach (var off in HoldOff)
                {
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, off, false);
                }
            }
            catch
            {
                // He fights the way he always did.
            }
        }

        /// <summary>Handles already dug in, so it is said once each.</summary>
        private readonly HashSet<int> _held = new HashSet<int>();

        /// <summary>Far enough to reach the wall behind him, not far enough to follow anybody.</summary>
        private const float HoldRadius = 18f;

        /// <summary>Cover, stand and fight, and get to the post before looking for a wall.</summary>
        private static readonly int[] HoldOn = { 0, 5, 12, 29, 44, 58 };

        /// <summary>
        /// And the ones that would undo all of it.
        ///
        /// 13 and 43 are both "charge" in disguise. 37, 45, 51 and 62 DELETE the defensive area
        /// the moment he reaches it, and 51 switches him to advance while doing it -- so he
        /// would arrive at his post and immediately leave. 71 lets him charge past the edge.
        /// </summary>
        private static readonly int[] HoldOff = { 13, 37, 43, 45, 47, 51, 62, 71 };

        /// <summary>
        /// The yard talking.
        ///
        /// Everybody is on their own clock, so this is not a round-robin -- it is fifteen
        /// people each due to say something every few seconds, landing wherever they land. The
        /// result is overlapping, which is correct; people at a party do not take turns.
        ///
        /// SPEECH_PARAMS_FORCE rather than the default, because the ambient system will
        /// happily decide a man has spoken recently enough and drop the line -- and a yard
        /// where two thirds of what is asked for is silently discarded is the quiet yard this
        /// is meant to fix.
        /// </summary>
        private void Talking(Ped player)
        {
            var now = Game.GameTime;

            for (var i = 0; i < _crew.Count; i++)
            {
                var ped = _crew[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                if (i >= _sayDue.Count) continue;

                if (_sayDue[i] == 0)
                {
                    _sayDue[i] = now + _mouth.Next(0, SayMaxMs);
                    continue;
                }

                if (now < _sayDue[i]) continue;

                _sayDue[i] = now + _mouth.Next(SayMinMs, SayMaxMs);

                // Out of earshot is not worth the call. They carry on being due, so walking
                // back into the yard does not walk into a silence while everybody re-clocks.
                if (ped.Position.DistanceTo(player.Position) > EarShot) continue;

                var at = _sayAt[i] % Talk.Length;
                _sayAt[i] = at + 1;

                try
                {
                    // THE GAG COMES OFF FOR OUR OWN LINE, and its absence is why the yard went
                    // silent -- the party AND the two who hold it during the day.
                    //
                    // Affiliation.CalmHome blocks all speech on every home-set ped near the
                    // player, which is what stopped the Families challenging Franklin on his
                    // own block. Everybody at that party is a home-set ped near the player, so
                    // it blocked them too. BlockTalk and BlockLife already lift it the instant
                    // before they speak and say so in their comments; this is the third caller
                    // that needed to and did not.
                    //
                    // CalmHome puts it back on its next pass, by which time the line has
                    // started, and a line already playing is not cut by the block.
                    Function.Call(Hash.BLOCK_ALL_SPEECH_FROM_PED, ped.Handle, false, false);

                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle,
                                  Talk[at], "SPEECH_PARAMS_FORCE");
                }
                catch
                {
                    // A label this voice has no line for is simply somebody not talking.
                }
            }
        }

        private void Settle()
        {
            for (var i = _crew.Count - 1; i >= 0; i--)
            {
                var ped = _crew[i];

                if (ped == null || !ped.Exists() || !ped.IsAlive)
                {
                    // Dead or gone is left alone. Replacing a man who was just shot in front of
                    // you is worse than being two men down.
                    //
                    // Nulled rather than REMOVED. Removing him shortened the list under
                    // everybody after him, and every per-station lookup is by index -- so one
                    // body handed the rest of the yard each other's faces, guns and animations,
                    // and it looked like nothing because they were all plausible men.
                    _crew[i] = null;
                    continue;
                }

                if (i >= _marks.Count) continue;

                var mark = MarkAt(i);
                var away = ped.Position.DistanceTo(mark);

                // ON HIS WAY IN. Everything below this is about a man who is where he is meant
                // to be and has stopped doing what he was told -- which describes somebody
                // walking up the road perfectly, and would march him back to a mark he has not
                // reached yet and then re-task him into a dance in the middle of the street.
                if (WalkingIn(i))
                {
                    if (away > ArriveWithin && Game.GameTime < _walkDue[i]) continue;

                    _walkingIn[i] = false;

                    // OUT OF TIME MEANS PUT HIM THERE, not start him where he stands.
                    //
                    // The timeout is for the man who has got himself wedged on a bin or shut
                    // behind a gate, and the whole point of it is that the party is not one
                    // person short all night. But "start the evening here" for somebody still
                    // halfway up the road is a woman doing the deejay animation on the
                    // pavement -- so if he has not made it, he is placed. It is the one moment
                    // somebody appears rather than arrives, and it only happens when the
                    // alternative is visibly broken.
                    if (away > ArriveWithin)
                    {
                        try
                        {
                            ped.Position = mark;
                            ped.Heading = Facing(i);
                        }
                        catch { /* he starts where he is, which is the old behaviour */ }
                    }

                    // Here, or placed. Either way this is where the evening starts: his
                    // heading, his scenario and his clip, exactly as if he had been stood here
                    // all along.
                    Idle(i, ped, Doing(i), Facing(i), mark, Seated(i), AnimAt(i));
                    continue;
                }

                if (away <= LeashAt(i))
                {
                    // Home, but knocked out of what he was doing -- put him back to it once,
                    // not every pass, or he restarts the scenario forever.
                    //
                    // An animated station is asked a different question. Task 118 is the
                    // SCENARIO task and an animation never sets it, so testing for it would
                    // re-issue the dance several times a second and it would never get past
                    // the first frame.
                    var clips = AnimAt(i);
                    if (clips != null)
                    {
                        var pick = i < _animPick.Count ? _animPick[i] : -1;
                        if (pick >= 0 && pick + 1 < clips.Length)
                        {
                            // Issued, but not long enough ago to have started. Asking now and
                            // acting on the answer is the bug this whole path had.
                            if (Game.GameTime < _animDue[i]) continue;

                            if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ped.Handle,
                                                    clips[pick], clips[pick + 1], 3))
                            {
                                continue;
                            }

                            // Resident, issued, given its grace, and still not playing: this
                            // install has the dict but not that CLIP. Step past it so the next
                            // attempt tries the one below, which is what a candidate list is
                            // supposed to do.
                            Log.Debug(string.Format(
                                "entourage: {0} station {1} has no {2} / {3}, trying the next",
                                _who, i, clips[pick], clips[pick + 1]));

                            _animPick[i] = pick + 2 < clips.Length ? pick + 2 : -1;
                        }
                    }
                    else if (WanderAt(i) > 0f)
                    {
                        // Left alone. Idle holds its own timer for him, and there is no task id
                        // to test here that would not be a guess -- 118 is the SCENARIO task and
                        // a wanderer never sets it, so testing for it would re-task him several
                        // times a second and he would never take a step.
                    }
                    else if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 118)) continue;

                    // In it, and standing where he belongs: give him this ground and leave him.
                    //
                    // Settle already refuses to re-task a man who is fighting, which is right.
                    // But it also meant nobody ever told him NOT to follow the fight down the
                    // road -- so he went, and Settle walked him back afterwards as though
                    // nothing had happened. A sphere on his own station is what was missing.
                    if (ped.IsInCombat || ped.IsRagdoll)
                    {
                        Hold(ped, MarkAt(i));
                        continue;
                    }

                    Idle(i, ped, Doing(i), Facing(i), MarkAt(i), Seated(i), AnimAt(i));
                    continue;
                }

                try
                {
                    // Whatever spooked them has to be over first. Walking a man back into the
                    // thing he ran from is worse than leaving him where he stopped.
                    //
                    // He still gets his station as a defensive area though, even from out
                    // here. It does not drag him back -- the fight decides that -- but it is
                    // the edge he will not go past, so he stops drifting further with every
                    // man he chases.
                    if (ped.IsInCombat || ped.IsRagdoll)
                    {
                        Hold(ped, MarkAt(i));
                        continue;
                    }
                    if (Function.Call<bool>(Hash.IS_PED_FLEEING, ped.Handle)) continue;

                    // A long way off and nobody looking: put him back. Anything closer he walks,
                    // because being teleported in front of you is the one thing that gives it
                    // away as a script.
                    if (away > 60f && !ped.IsOnScreen)
                    {
                        ped.Position = mark;
                        ped.Task.ClearAll();
                        Idle(i, ped, Doing(i), Facing(i), MarkAt(i), Seated(i), AnimAt(i));
                        continue;
                    }

                    // Already walking back.
                    if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 224)) continue;

                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                                  mark.X, mark.Y, mark.Z, 1.2f, 20000, 1.0f, 0, Facing(i));
                }
                catch
                {
                    // He will settle.
                }
            }
        }

        /// <summary>
        /// Only trusts a ground probe that agrees with the authored height, for the same reason
        /// everything else here does: a probe from above a courtyard finds the walkway over it.
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
                // Keep the authored height.
            }

            return where;
        }

        private void Despawn()
        {
            foreach (var ped in _crew)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    ped.MarkAsNoLongerNeeded();
                    ped.Delete();
                }
                catch { /* teardown */ }
            }

            _crew.Clear();
        }

        public void RestoreWorld() => Despawn();
    }
}
