using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;

namespace Hoodrich.Locations
{
    /// <summary>Where the night is up to.</summary>
    internal enum TakeoverState
    {
        None,
        Running,
        Scattering
    }

    /// <summary>
    /// The takeover.
    ///
    /// One intersection, one night, some time between nine and four. Cars and people come in
    /// from the surrounding streets, a ring forms facing inwards, and one or two cars work the
    /// inside of it sideways. It runs for hours and ends with blue lights.
    ///
    /// IT IS NOT A MISSION. No call, no blip, nothing asked of you -- it happens whether you go
    /// or not, and the whole value of it is driving past at two in the morning and finding it.
    ///
    /// NOBODY APPEARS. Every person walks in and every car drives in, from out of sight, to a
    /// place picked for them before they existed. That is the most expensive decision in this
    /// file and the one that makes it read as people turning up rather than a set being
    /// dressed: a crowd that pops into being at a junction is a crowd nobody believes, however
    /// many of them there are.
    ///
    /// THE CIRCLE IS DRIVEN, NOT TASKED. No native asks a driver to hold a donut at a radius
    /// around a point -- the closest are the burnout actions, which go wherever the car is
    /// pointing for however long you name and cannot be aimed. So a car that has ARRIVED is
    /// taken off its task and pushed round by hand: an angle that advances, a heading chasing
    /// the tangent, forward speed, grip turned down underneath. Coming in and going out it is
    /// an ordinary driver on an ordinary route, which is how it gets through the gap in the
    /// ring in the first place.
    ///
    /// AND THE DRIVERS DO NOT PANIC. Flinching at a gunshot, fleeing a collision, treating a
    /// crowd as something to escape -- every one of those is correct for a driver in traffic
    /// and catastrophic here, and the visible symptom of leaving them on is a car abandoning
    /// its own donut and tearing off through the spectators.
    /// </summary>
    internal sealed class Takeover
    {
        // ---- where and when -----------------------------------------------------

        /// <summary>The junction, read off the screen while stood in the middle of it.</summary>
        private static readonly Vector3 Middle = new Vector3(-126.840f, -1737.201f, 30.135f);

        /// <summary>
        /// The mark the cars actually work around, read off the ground in game.
        ///
        /// THREE METRES FROM THE MIDDLE, AND THE THREE METRES MATTER. Middle is the centre of
        /// the event -- where the ring of people is measured from, where the cordon is centred,
        /// where the police are sent. This is the centre of the CIRCLE, which is a different
        /// thing: it is the patch of road the tyre marks are on, and on this junction that is
        /// not the same spot as the geometric middle of the crossroads.
        ///
        /// Kept as its own point rather than nudging Middle, because moving Middle would drag
        /// the crowd ring, the cordon and the police approach along with it -- and the ring is
        /// the part that already works.
        /// </summary>
        private static readonly Vector3 Circle = new Vector3(-129.056f, -1739.410f, 29.530f);

        /// <summary>
        /// The pavement corners people actually stand on, read off the ground in game.
        ///
        /// Seven, and they are not evenly spaced because the junction is not -- they sit
        /// between thirteen and twenty-seven metres out at bearings of roughly 20, 65, 148,
        /// 192, 246, 305 and 335 degrees from the circle, which is every side of it. That
        /// unevenness is the point: it is the shape of the actual crossroads rather than the
        /// shape of a circle drawn around it.
        ///
        /// The cars are deliberately NOT moved onto these. They still park on the ring, which
        /// is where they block the roads in, and the roads in are the thing they are for.
        /// </summary>
        private static readonly Vector3[] Corners =
        {
            new Vector3(-137.831f, -1715.518f, 30.033f),
            new Vector3(-120.845f, -1721.802f, 30.028f),
            new Vector3(-111.836f, -1727.592f, 29.907f),
            new Vector3(-108.003f, -1748.946f, 29.955f),
            new Vector3(-123.605f, -1766.196f, 29.796f),
            new Vector3(-136.306f, -1750.899f, 30.233f),
            new Vector3(-149.461f, -1729.984f, 30.026f)
        };

        /// <summary>How many of them stand on a corner, and how far a corner spreads.</summary>
        private const int OnCorners = 80;
        private const float CornerSpread = 4.5f;

        /// <summary>How far out the ring stands. Measured on the ground: 19.1 metres.</summary>
        private const float RingAt = 19f;

        /// <summary>
        /// And how far in the cars work. Ten, tightened from fifteen.
        ///
        /// Nine metres of clearance to the ring rather than four, which is the number that
        /// matters: the back end of a car on reduced grip steps a long way wide of the line the
        /// nose is taking, and at four the front row was inside that. A tighter circle is also
        /// a faster-looking one -- the same speed round a smaller radius is more lock, more
        /// angle and more smoke in one place.
        /// </summary>
        /// <summary>
        /// How wide the loops are, in metres, either side of the ini figure.
        ///
        /// FIVE, DOWN FROM TEN. A ten metre loop on this junction is a car driving round a
        /// roundabout; five is a car being thrown at a circle it keeps missing, which is the
        /// thing this is supposed to look like. They will slide well outside it and that is
        /// the point -- the radius is what they are AIMING at, not a track they are on, and
        /// the leash further down is what brings a wide one back rather than anything holding
        /// them to a line.
        /// </summary>
        private float DriftMin { get { return SpinRadius - 0.5f; } }
        private float DriftMax { get { return SpinRadius + 0.5f; } }

        /// <summary>The ini figure, because this number has been changed by eye four times.</summary>
        private float SpinRadius
        {
            get { return _cfg == null ? 5f : _cfg.TakeoverSpinRadius; }
        }

        /// <summary>How long the one on the mark gets before somebody else has a go.</summary>
        private const int BurnMinMs = 26000;
        private const int BurnMaxMs = 36000;

        /// <summary>
        /// Close enough to the mark to stop and start smoking.
        ///
        /// SEVEN AND A HALF, RAISED FROM FOUR AND A HALF, and this was why nothing ever burned
        /// out. The drive-in was issued with a stopping range of five metres, so the car parked
        /// itself five metres from the mark and the arrival test wanted four and a half -- it
        /// never passed, so the car never started, never got its tyres, and never timed out
        /// either, because the clock only starts when the work does. One car sat by the mark
        /// doing nothing for the whole takeover and the count said the mark was occupied, so no
        /// replacement was ever sent.
        ///
        /// The stopping range is down to two as well. Both numbers, or the same trap reopens
        /// the first time a kerb stops somebody a metre early.
        /// </summary>
        private const float OnTheMark = 7.5f;

        /// <summary>
        /// How long anybody gets to arrive before the circle stops waiting for them.
        ///
        /// The backstop for the fault above, and for every other version of it: a blocked road,
        /// a driver who has taken a wrong turn, a car wedged on a bollard. Without it a runner
        /// that cannot reach its spot holds that spot for ever.
        /// </summary>
        private const int ComeOnMs = 40000;

        /// <summary>Near enough that a late arrival is started where it stands rather than binned.</summary>
        private const float CloseEnough = 26f;

        /// <summary>How fast he turns on the spot while he does it.</summary>
        private const float SpinRate = 62f;

        private const int FromHour = 21;
        private const int ToHour = 4;
        private const float LastsHours = 3f;

        private const float NearEnough = 200f;
        private const float LetGo = 300f;

        /// <summary>Where people and cars come FROM, which is never the junction itself.</summary>
        private const float WalkFromMin = 55f;
        private const float WalkFromMax = 130f;

        /// <summary>
        /// A block over, and that is the floor rather than a suggestion.
        ///
        /// It was ninety, which on these streets is the far side of one junction -- close
        /// enough that a car for the takeover could appear in the same shot as the takeover.
        /// The whole reason everything drives in is so that nothing is seen arriving out of
        /// nowhere, and a spawn radius that fits inside the draw distance gives that away.
        /// </summary>
        private const float DriveFromMin = 125f;
        private const float DriveFromMax = 230f;

        /// <summary>
        /// How far out ordinary traffic is talked down.
        ///
        /// A junction full of people is a junction the driving AI has no idea what to do with:
        /// a ped in the road is an obstacle, a car sideways in front of it is a threat, and the
        /// response to both is to get out of there -- which at speed, through a crowd, is the
        /// worst thing that can happen at one of these. So anybody driving near it is made
        /// patient for as long as they are near it, and given themselves back when they leave.
        /// </summary>
        private const float CalmRange = 50f;

        /// <summary>
        /// And how close anybody who is not part of it may get.
        ///
        /// Two metres outside the ring, so the turn happens where the crowd starts rather than
        /// on top of them. Anything of ours is exempt: the drifters live inside it, the
        /// spectators park on the line, and the police are supposed to come straight through.
        /// </summary>
        private const float BlockAt = 40f;

        /// <summary>
        /// And how close a stranger gets before it simply stops existing.
        ///
        /// THE TURN-ROUND IS A REQUEST AND THIS IS NOT. Forty metres out a car is asked to go
        /// back the way it came, which works on a driver who is listening -- most are. The ones
        /// that are not, because they are mid-manoeuvre or wedged or being shoved by something
        /// else, used to carry on into a junction with sixty people stood in it.
        ///
        /// At the ring they are removed. Nineteen metres is where the crowd starts, so it is
        /// the last moment it can happen without being a car vanishing in front of somebody --
        /// and a car that has got that far through a closed junction was never going to be
        /// talked out of it.
        ///
        /// ONLY TRAFFIC. Something with a driver in it, that is not ours and is not yours. A
        /// parked car is left alone whoever it belongs to, because the one thing worse than a
        /// stranger driving through the takeover is your own car disappearing off the kerb you
        /// left it on.
        /// </summary>
        private const float EatAt = 19f;

        /// <summary>How often one car may be turned round, so it is not re-tasked every tick.</summary>
        /// <summary>
        /// How often one car may be turned round, so it is not re-tasked every tick.
        ///
        /// Down from four seconds now that the cordon is doing this alone. Four was chosen when
        /// the road nodes were expected to do most of the work and this was the backstop; as
        /// the only thing standing between traffic and the middle it has to catch a car sooner
        /// than that, and re-issuing a route every two and a half seconds is still far enough
        /// apart that a driver gets somewhere between them.
        /// </summary>
        private const int TurnGapMs = 2500;

        /// <summary>Close enough to their place to stop and turn round.</summary>
        private const float ArrivedRange = 3.5f;
        private const float CarArrivedRange = 7f;

        // ---- the crowd ----------------------------------------------------------

        private const int CrowdMin = 48;
        private const int CrowdMax = 68;

        /// <summary>How many set off at once, so it fills up rather than materialising.</summary>
        private const int PerWave = 7;

        /// <summary>How long the cars get before anybody sets off on foot.</summary>
        private const int CrowdLeadMs = 14000;
        private const int WaveGapMs = 2600;

        /// <summary>
        /// What the ring is doing while it watches.
        ///
        /// Weighted by repetition rather than by a table of numbers, which is the cheapest way
        /// to say "mostly cheering and drinking, some smoking, a few filming it on a phone".
        /// The mobile ones earn their place -- half a real crowd is holding a phone up -- but
        /// they were a third of this list and it read as a bus queue.
        /// </summary>
        private static readonly string[] Watching =
        {
            "WORLD_HUMAN_CHEERING", "WORLD_HUMAN_CHEERING", "WORLD_HUMAN_CHEERING",
            "WORLD_HUMAN_CHEERING", "WORLD_HUMAN_CHEERING",
            "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_PARTYING", "WORLD_HUMAN_PARTYING",
            "WORLD_HUMAN_STAND_MOBILE_UPRIGHT", "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_STAND_IMPATIENT"
        };

        /// <summary>
        /// Who turns up, weighted by repetition.
        ///
        /// THIS IS CHAMBERLAIN HILLS AT TWO IN THE MORNING, and the old list was a coach party:
        /// a hipster, a Korean, a man from Downtown and somebody off the beach in Vespucci,
        /// all in equal measure, on a block none of them live on. It read as the game's
        /// ambient population teleported to a junction, which is exactly what it was.
        ///
        /// So it is the neighbourhood instead. Mostly Families and mostly the people who live
        /// on those streets whether they claim a set or not; a few Ballas and a few Vagos,
        /// because a takeover is the one night nobody is counting colours and everybody wants
        /// to see the cars; a couple of dealers, who go where a crowd goes; and women
        /// throughout, including women in both sets, because a ring of fifty men is its own
        /// kind of wrong.
        ///
        /// Weighted by how many times a name appears rather than by a table of numbers -- it
        /// is the same trick as the crowd's idle animations and it keeps the weighting where
        /// anybody can see it.
        ///
        /// A name this install has not got is skipped rather than fatal; see Somebody.
        /// </summary>
        private static readonly string[] Faces =
        {
            // The set, and the block. The bulk of it.
            "g_m_y_famca_01", "g_m_y_famca_01", "g_m_y_famca_01",
            "g_m_y_famdnf_01", "g_m_y_famdnf_01", "g_m_y_famdnf_01",
            "g_m_y_famfor_01", "g_m_y_famfor_01", "g_m_y_famfor_01",
            "a_m_y_soucent_01", "a_m_y_soucent_01", "a_m_y_soucent_02", "a_m_y_soucent_02",
            "a_m_y_soucent_03", "a_m_y_soucent_03", "a_m_y_soucent_04", "a_m_y_soucent_04",
            "a_m_m_soucent_01", "a_m_m_soucent_02", "a_m_m_soucent_03",
            "a_m_o_soucent_01", "a_m_o_soucent_02",

            // Women, from the same streets and from the sets.
            "a_f_y_soucent_01", "a_f_y_soucent_01", "a_f_y_soucent_02", "a_f_y_soucent_02",
            "a_f_m_soucent_01", "a_f_m_soucent_02",
            "g_f_y_families_01", "g_f_y_families_01",
            "g_f_y_ballas_01", "g_f_y_vagos_01",

            // Purple, in ones and twos. Nobody is counting tonight.
            "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01",

            // And yellow, same again.
            "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03",

            // People who go where a crowd goes.
            "s_m_y_dealer_01", "s_m_y_dealer_01",
            "a_m_y_stwhi_01", "a_f_y_genhot_01", "a_m_y_ktown_01", "a_f_y_hipster_02"
        };

        /// <summary>
        /// What gets put sideways.
        ///
        /// The Hellcats and the Mustangs are the Gauntlets and the Dominators -- those are the
        /// cars this game has for those cars, and the numbered variants are the hotted-up ones.
        /// The drift* models lead where an install has them and everything after is a car every
        /// install has, so nobody ends up with an empty circle.
        /// </summary>
        private static readonly string[] Drifters =
        {
            "driftdominator10", "driftgauntlet4", "driftchavosv6", "driftfr36", "driftremus",
            "gauntlet3", "gauntlet4", "gauntlet5",
            "dominator3", "dominator7", "dominator8",
            "dominator", "buffalo3", "sultan", "futo"
        };

        /// <summary>Cars that came to watch, parked outside the ring.</summary>
        private static readonly string[] Parked =
        {
            "asterope2", "dorado", "kanjosj", "s95", "vorschlaghammer", "sultan2",
            "warrener", "faction", "primo2", "gauntlet", "dominator", "buffalo"
        };

        /// <summary>
        /// Donks, parked up with the rest of them.
        ///
        /// Their own list rather than more names in Parked, because they are the cars people
        /// come to LOOK at -- a handful is a feature and a car park full of them is a show,
        /// which this is not. Three or four of the fourteen-odd spectators.
        ///
        /// The Faction Custom Donk is the one the game actually has; everything after it is a
        /// Benny's build on big wheels, which is the same corner of the same car park even if
        /// a purist would argue. The list is tried in order and a build missing the first name
        /// quietly gets the next, so nobody ends up with an empty space.
        /// </summary>
        private static readonly string[] Donks =
        {
            "faction3", "faction2", "voodoo", "chino2", "buccaneer2",
            "sabregt2", "tornado5", "virgo2"
        };

        private const int DonksMin = 3;
        private const int DonksMax = 5;

        /// <summary>
        /// And the ones on juice.
        ///
        /// Hydraulics are a real system in this game and these are the cars that have them. A
        /// bouncing Asterope would be a bouncing bug -- SET_CAN_USE_HYDRAULICS on something
        /// without them does nothing, and the raise factor would be driven all night for free.
        /// </summary>
        private static readonly string[] Lows =
        {
            "voodoo", "buccaneer2", "chino2", "faction2", "moonbeam2",
            "slamvan3", "sabregt2", "virgo2", "tornado5", "minivan2"
        };

        /// <summary>
        /// And the cars that came to watch, ringed round the outside.
        ///
        /// Nearly double, because they are the wall. The cordon turns strangers round and the
        /// road nodes stop them being sent, but a line of parked cars is the thing you can
        /// actually see holding the junction -- and it is what a real one looks like from
        /// above. They park on a ring OUTSIDE the drift circle and the gaps between them are
        /// what the drift cars come in through, so more of them closes the junction without
        /// ever sealing it.
        /// </summary>
        private const int ParkedMin = 14;
        private const int ParkedMax = 20;
        private const int LowsMin = 4;
        private const int LowsMax = 7;

        // ---- what is out there --------------------------------------------------

        private sealed class Watcher
        {
            public Ped Man;
            public Vector3 Slot;
            public bool There;

            /// <summary>When they were first noticed away from their spot, or nought.</summary>
            public int Away;

            /// <summary>
            /// What he was doing, so that after a knock he goes back to doing THAT.
            ///
            /// Picked once, when he first arrives, and kept for the rest of the night. Rolling
            /// a fresh one every time he is put back would have a man who was drinking come
            /// back from being run over as a man on his phone, which reads as a different
            /// person standing in the same place.
            /// </summary>
            public string Doing;
        }

        private sealed class Parkee
        {
            public Vehicle Car;
            public Ped Driver;
            public Vector3 Slot;
            public bool There;

            public bool Low;
            public double Hop;
            public double Rate;
        }

        private sealed class Runner
        {
            public Vehicle Car;
            public Ped Driver;

            public float Radius;

            /// <summary>When this one is given its next go of lock.</summary>
            public int NextAction;

            /// <summary>When it set off, so a car that never arrives can be given up on.</summary>
            public int Sent;
            public float Speed;
            public int Way;
            public int Until;

            public bool Circling;
            public bool Leaving;

            /// <summary>
            /// This one is on the mark rather than going round it.
            ///
            /// The two are the same object because they have the same life -- drive in, do the
            /// thing, drive out -- and the only difference is what "the thing" is. Splitting
            /// them into two classes would duplicate the arrival, the timeout and every line
            /// of the cleanup to change one method.
            /// </summary>
            public bool Middle;
        }

        private readonly Settings _cfg;
        private readonly Random _rng = new Random();

        private readonly List<Watcher> _crowd = new List<Watcher>();
        private readonly List<Parkee> _parked = new List<Parkee>();
        private readonly List<Runner> _running = new List<Runner>();

        /// <summary>
        /// Somebody from the set riding straight through the middle of it with the front
        /// wheel up.
        /// </summary>
        private sealed class Rider
        {
            public Vehicle Bike;
            public Ped Man;

            /// <summary>Which of the runs he is on, and which end he is heading for.</summary>
            public int Run;
            public bool ToFar;

            /// <summary>
            /// Or he is not on a run at all, and is going round the middle instead.
            ///
            /// One of them, and only one. A line rider crosses and leaves; this one never
            /// leaves, so he is the thing in the middle that is always moving -- which is
            /// what makes the junction look busy between passes rather than only during them.
            /// </summary>
            public bool Looping;

            /// <summary>Where round the circle he is aiming, in radians, and which way round.</summary>
            public double Angle;
            public int Round;

            /// <summary>Where that puts him, and when he set off for it.</summary>
            public Vector3 Aim;
            public int Sent;

            /// <summary>How long he is holding the front wheel up for.</summary>
            public int WheelieUntil;
            public int WheelieAfter;
        }

        private readonly List<Rider> _riders = new List<Rider>();

        /// <summary>
        /// When each outsider was last sent back, by vehicle handle.
        ///
        /// Without it a car sat on the line is re-tasked every tick, and a driver handed a
        /// fresh route several times a second never gets anywhere at all -- which would leave
        /// it exactly where the cordon is trying to move it from.
        /// </summary>
        private readonly Dictionary<int, int> _turned = new Dictionary<int, int>();

        private sealed class Law
        {
            public Vehicle Car;
            public Ped Cop;
        }

        private readonly List<Law> _law = new List<Law>();

        /// <summary>How many turn up, and how far out they start.</summary>
        private const int Units = 3;
        private const float LawFrom = 150f;

        /// <summary>Close enough for the junction to notice them.</summary>
        private const float LawSeen = 70f;

        public Func<bool> Busy;

        /// <summary>Set by Main: the feed, so the block can talk about it.</summary>
        public SocialFeed Social;

        public TakeoverState State { get; private set; }

        private int _plannedFor = -1;
        private int _startsAt = -1;
        private int _endsAt;

        private int _lastTick;
        private int _lastDrive;

        private bool _scattered;
        private int _toCome;
        private int _nextWave;
        private int _nextWord;

        private const int TickMs = 700;
        /// <summary>
        /// How long the block says nothing about it.
        ///
        /// Half of the three hours it runs, in real milliseconds rather than game minutes --
        /// the posts are paced by the real clock like everything else on the feed, so the
        /// threshold has to be on the same clock as the gap between them.
        /// </summary>
        private const int QuietForMs = 900000;

        private int _startedAt;

        private const int WordMinMs = 55000;
        private const int WordMaxMs = 130000;

        private bool Enabled => _cfg == null || _cfg.TakeoverEnabled;

        private float Ring => _cfg == null || _cfg.TakeoverRadius <= 1f ? RingAt : _cfg.TakeoverRadius;

        public Takeover(Settings cfg)
        {
            _cfg = cfg;
        }

        // ---- per-tick -----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;

            // Per frame: the circle and the hydraulics. Both are physics driven by hand and
            // both read as a stutter at anything less.
            // The hydraulics stay per frame -- that is a value being driven, not a car
            // being moved. The cars are on tasks now and are looked at on the tick.
            if (State == TakeoverState.Running)
            {
                Bounce();

                // Per frame with the hydraulics and for the same reason: a wheelie is held by
                // pushing on the bike every frame it lasts, and the same push at tick intervals
                // is a pothole.
                Wheelie(now);

                // And the beam, which is a draw call and therefore only exists on the frame it
                // is made -- a spot light drawn every nine hundred milliseconds is a spot light
                // that is off for eight hundred and ninety of them.
                Beam();
            }

            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            // BEFORE ANYTHING ELSE, AND OUTSIDE EVERY EARLY RETURN BELOW. Cars on their way
            // out outlive the takeover that sent them -- that is the whole point of the list --
            // so a sweep that only ran while one was on would leave the last batch of every
            // night sitting where it stopped. It is also cheap: it does nothing at all unless
            // there is something on the list.
            try { Ghosts(now); }
            catch { /* next tick */ }

            try
            {
                if (!Enabled)
                {
                    if (State != TakeoverState.None) Pack();
                    return;
                }

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                Plan();

                var near = player.Position.DistanceTo(Middle);

                switch (State)
                {
                    case TakeoverState.None:
                        if (Busy != null && Busy()) return;
                        if (!Tonight()) return;
                        if (near > NearEnough) return;

                        Begin(now);
                        break;

                    case TakeoverState.Running:
                        if (near > LetGo) { Pack(); return; }
                        if (OwnedCars.NowMinutes() >= _endsAt) { Blues(); return; }

                        Calm();
                        Wave(now);
                        Walking();
                        Parking();
                        Keep(now);
                        Working(now);
                        Chatter(now);
                        Racket(now);
                        Bikes(now);
                        Chopper(now);
                        Flares(now);
                        Unarm(now);
                        Firework(now);
                        break;

                    case TakeoverState.Scattering:
                        // The police are driving in. Nothing runs until one of them is close
                        // enough to be worth running from.
                        if (!_scattered && Closing())
                        {
                            _scattered = true;
                            Scatter();
                        }

                        if (near > LetGo || now > _lastDrive) Pack();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover tripped: " + ex.Message);
                Pack();
            }
        }

        // ---- the diary ----------------------------------------------------------

        private void Plan()
        {
            int day;

            try { day = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH); }
            catch { return; }

            if (day == _plannedFor) return;

            _plannedFor = day;

            var span = (24 - FromHour) + ToHour;
            _startsAt = (FromHour + _rng.Next(span)) % 24;

            Log.Info("Takeover: tonight's is at " + _startsAt + ":00.");
        }

        private bool Tonight()
        {
            if (_startsAt < 0) return false;

            try
            {
                var h = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                var since = (h - _startsAt + 24) % 24;

                return since < (int)Math.Ceiling(LastsHours);
            }
            catch
            {
                return false;
            }
        }

        // ---- starting -----------------------------------------------------------

        private void Begin(int now)
        {
            State = TakeoverState.Running;

            _endsAt = OwnedCars.NowMinutes() + (int)(LastsHours * 60f);
            _startedAt = Game.GameTime;

            // THE ROADS STAY ON, AND THAT IS A REVERSAL OF SOMETHING TRIED AND MEASURED.
            //
            // Switching the road nodes off in a thirty metre box round the junction is the
            // obvious way to stop traffic being routed through a takeover, and the reasoning
            // written here was that ours would be unaffected because ours are not on the
            // traffic generator -- a drift car is handed a coordinate and drives to it.
            //
            // THAT WAS WRONG, and the log said so in one line repeated thirty times: "a car
            // never made it in". TASK_VEHICLE_DRIVE_TO_COORD still ROUTES on the nodes even
            // though the destination is a coordinate, so with none inside the box every car
            // sent for planned as far as the edge of it and stopped -- about thirty metres out,
            // which is just past the distance at which one is given up on. Every drift car,
            // every night, for the whole night. The junction filled with people and spectators
            // and had nothing in the middle of it.
            //
            // So it is the cordon's job again, and the cordon is a better fit for it than it
            // looks: it turns strangers round at thirty metres, and the fourteen to twenty
            // parked cars ringing the outside are a wall you can see. Restored here as well as
            // in the teardowns, because a session that crashed with them off would otherwise
            // leave a permanent hole in the city's traffic.
            Roads(true);
            _toCome = _rng.Next(CrowdMin, CrowdMax + 1);
            // Two or three, and the first of them is always the one going round the middle.
            _wantBikes = _rng.Next(2, 4);
            _nextBike = now + 8000;

            // THE CARS GET THERE FIRST, BUT ONLY JUST.
            //
            // Everything used to be released on the same frame, and the running order after
            // that was an accident of geometry: the crowd spawns fifty to a hundred and thirty
            // metres out and runs, the drift cars spawn a hundred and twenty-five to two
            // hundred and thirty out and drive, and which arrived first depended on what the
            // spawner happened to roll.
            //
            // It should not be an accident. A takeover that people are still arriving at, with
            // a car already sideways in the middle of it, is a thing that started without you;
            // one where a crowd stands in a circle waiting for a car to turn up is a queue.
            // The head start is small on purpose -- long enough for one car to be down and
            // working, not long enough that the first arrivals have got bored.
            _nextWave = now + CrowdLeadMs;
            _nextWord = now + _rng.Next(20000, 45000);

            Cars();

            // THE WORD GOES OUT, AND ONLY THE WORD.
            //
            // One post naming the junction, so somebody reading the feed can decide to come --
            // which is the only kind of takeover post that is worth anything before there is
            // anything to see. The general chatter is a different thing and is still held for
            // the first half hour by Chatter(): people talking about how loud it is only means
            // something once it has been loud for a while.
            if (Social != null) Social.On(SocialEvent.TakeoverOn);

            Log.Info("Takeover: on. " + _toCome + " on their way.");
        }

        /// <summary>People set off in small lots rather than all at once.</summary>
        private void Wave(int now)
        {
            if (_toCome <= 0 || now < _nextWave) return;

            _nextWave = now + WaveGapMs;

            var want = Math.Min(PerWave, _toCome);

            // Counted down whether or not the spawn succeeded. A failure is a person who did
            // not come, and retrying forever would have the mod hammering the pavement finder
            // for the rest of the night on a junction where it cannot find one.
            for (var i = 0; i < want; i++)
            {
                Somebody();
                _toCome--;
            }
        }

        /// <summary>
        /// One person, put down out of sight and told to walk to their place in the ring.
        ///
        /// The slot is picked FIRST and the spawn point is chosen to be away from it, which is
        /// the right way round: everybody has somewhere to be before they exist, so the ring
        /// fills evenly instead of clumping wherever the spawner happened to succeed.
        /// </summary>
        private bool Somebody()
        {
            try
            {
                // MOSTLY ON THE CORNERS, AND THAT IS WHAT A CROWD DOES.
                //
                // A circle drawn round the middle is the obvious way to place a ring and it
                // was quietly wrong on this junction: an even ring puts a quarter of the
                // crowd in the middle of Carson with nothing behind them, stood in a live
                // traffic lane on a bit of road that is not a place anybody would choose to
                // stand. People gather where there is something to stand ON and something to
                // stand BEHIND, which at a crossroads is the pavement corners.
                //
                // Seven of them, read off the ground in game, at bearings that cover every
                // side of the junction. Each takes a scatter so a corner is a knot of people
                // rather than seven neat stacks.
                //
                // A fifth still go on the old ring, because a takeover does spill: somebody is
                // always stood somewhere daft, and a crowd that respects the kerb perfectly is
                // as unconvincing as one that ignores it.
                Vector3 slot;

                if (_rng.Next(100) < OnCorners)
                {
                    var c = Corners[_rng.Next(Corners.Length)];
                    var ca = _rng.NextDouble() * Math.PI * 2d;
                    var cr = (float)(_rng.NextDouble() * CornerSpread);

                    slot = Ground(new Vector3(c.X + (float)Math.Cos(ca) * cr,
                                              c.Y + (float)Math.Sin(ca) * cr, c.Z));
                }
                else
                {
                    var a = _rng.NextDouble() * Math.PI * 2d;
                    var r = Ring + (float)(_rng.NextDouble() * 3.5 - 1.2);

                    slot = Ground(new Vector3(Middle.X + (float)Math.Cos(a) * r,
                                              Middle.Y + (float)Math.Sin(a) * r, Middle.Z));
                }

                var from = OnFoot(slot);
                if (from == Vector3.Zero) return false;

                // A FEW GOES AT A MODEL RATHER THAN ONE.
                //
                // This used to take a single name and give up on the whole person if it did
                // not load, which was survivable when every name was a stock ambient ped. The
                // list now leans on gang models, and a build without one of them would have
                // quietly lost a slice of the crowd -- thinning the ring in a way that would
                // look like the spawner failing rather than like a model being absent.
                Model model = default(Model);
                var got = false;

                for (var tries = 0; tries < 6 && !got; tries++)
                {
                    model = new Model(Faces[_rng.Next(Faces.Length)]);

                    got = model.IsValid && model.IsInCdImage && model.Request(900);
                }

                if (!got) return false;

                var handle = Function.Call<int>(Hash.CREATE_PED, 4, model.Hash,
                                                from.X, from.Y, from.Z, 0f, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return false;

                var ped = Entity.FromHandle(handle) as Ped;
                if (ped == null || !ped.Exists()) return false;

                ped.IsPersistent = true;

                var h = ped.Handle;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);

                // THEY REACT NOW, and that is a reversal of what was here before.
                //
                // Being deaf to the world kept the ring perfectly still, which is also what
                // made it a diorama -- a car sliding four metres from your feet and nobody so
                // much as turning their head is the one thing at a takeover that could not
                // happen. So they flinch, and they duck, and some of them run.
                //
                // What stops that emptying the junction is not the ped -- it is Walking()
                // below, which notices anybody who has left their spot and walks them back.
                // They are allowed to bolt; they are not allowed to keep going.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, false);

                // THEY RUN. 1.2 is a walk, and a walk from a hundred metres out is two
                // minutes of somebody strolling towards a thing that has already started.
                // Nobody walks to a takeover. 3.0 is a run, and the ring fills in seconds
                // rather than in the time it takes to lose interest.
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, h,
                              slot.X, slot.Y, slot.Z, 3.0f, -1, 1.5f, true, 0f);

                Function.Call(Hash.SET_PED_KEEP_TASK, h, true);

                _crowd.Add(new Watcher { Man = ped, Slot = slot });
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send somebody: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Anybody who has reached their place stops and turns to face the middle.
        ///
        /// AND IS TURNED BACK. A scenario picks its own heading and several of these rotate a
        /// ped as they play, so a ring faced inwards once is a ring facing every which way a
        /// minute later. One comparison and one call per person per tick, and it is the
        /// difference between a crowd watching something and a crowd standing near it.
        /// </summary>
        /// <summary>How far he may drift before he is walked back, and how long he gets first.</summary>
        private const float StrayRange = 9f;
        private const int LetHimRunMs = 4000;

        private void Walking()
        {
            var now = Game.GameTime;

            foreach (var w in _crowd)
            {
                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;

                if (!w.There)
                {
                    if (w.Man.Position.DistanceTo(w.Slot) > ArrivedRange) continue;

                    w.There = true;
                    w.Away = 0;

                    if (string.IsNullOrEmpty(w.Doing)) w.Doing = Watching[_rng.Next(Watching.Length)];

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                        Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, Facing(w.Man.Position));

                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, w.Man.Handle,
                                      w.Doing, 0, true);

                        // AND AGAIN AFTER THE SCENARIO, not only before it. A scenario picks
                        // its own facing when it starts, so a heading set first is a heading
                        // thrown away -- which is why the ring kept coming out pointing every
                        // which way however carefully each man was placed.
                        Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, Facing(w.Man.Position));
                    }
                    catch
                    {
                        // He stands there either way.
                    }

                    continue;
                }

                // KNOCKED OVER, SHOVED, OR IN A FIGHT.
                //
                // Distance alone does not catch these. A man clipped by a drift car ends up on
                // the floor roughly where he was standing, so he never strays far enough to be
                // noticed -- he just lies there for the rest of the night, or gets up and
                // stands facing the wrong way with no scenario running, because the thing that
                // hit him cancelled it. Same for anybody who has squared up to somebody: he is
                // stood in the right place doing entirely the wrong thing.
                //
                // All three are the same fault -- he is no longer doing what he came to do --
                // and all three get the same answer, which is the one below: he is no longer
                // "there", so he walks back to his spot and starts his own scenario again.
                var knocked = false;

                try
                {
                    knocked = Function.Call<bool>(Hash.IS_PED_RAGDOLL, w.Man.Handle)
                              || Function.Call<bool>(Hash.IS_PED_IN_COMBAT, w.Man.Handle, 0)
                              || Function.Call<bool>(Hash.IS_PED_FLEEING, w.Man.Handle)
                              || Function.Call<bool>(Hash.IS_PED_BEING_STUNNED, w.Man.Handle, 0);
                }
                catch
                {
                    // Treated as fine. A false alarm here would reset the whole ring.
                }

                if (knocked)
                {
                    // The clock still applies. A man who has just been hit should be allowed to
                    // be a man who has just been hit for a few seconds -- yanking him upright
                    // on the frame he lands is worse than the thing being fixed.
                    if (w.Away == 0) w.Away = now;

                    if (now - w.Away > LetHimRunMs)
                    {
                        w.There = false;
                        w.Away = 0;

                        try
                        {
                            Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                            Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                                          w.Slot.X, w.Slot.Y, w.Slot.Z, 3.0f, -1, 1.5f, true, 0f);

                            Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
                        }
                        catch
                        {
                            // He finds his own way back or he does not.
                        }
                    }

                    continue;
                }

                // SPOOKED, AND THEN BACK. He is allowed to jump out of the way of something --
                // that is the whole reason he can hear the world now -- but a takeover where
                // one loud noise empties the pavement is a takeover that ends itself.
                //
                // Given a few seconds to have his reaction before anybody interferes with it.
                // Pulling him back the instant he moves would cancel the flinch mid-animation
                // and read as a man being dragged, which is worse than not flinching at all.
                var strayed = w.Man.Position.DistanceTo(w.Slot);

                if (strayed > StrayRange)
                {
                    if (w.Away == 0)
                    {
                        w.Away = now;
                        continue;
                    }

                    if (now - w.Away < LetHimRunMs) continue;

                    w.There = false;
                    w.Away = 0;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                        Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                                      w.Slot.X, w.Slot.Y, w.Slot.Z, 3.0f, -1, 1.5f, true, 0f);

                        Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
                    }
                    catch
                    {
                        // He wanders back or he does not.
                    }

                    continue;
                }

                w.Away = 0;

                try
                {
                    // TIGHTER THAN IT WAS. Twenty-five degrees of slack is a third of the
                    // way to standing side-on, and across thirty people that reads as a crowd
                    // milling rather than a crowd watching. Ten is close enough to inwards that
                    // the whole ring points at the same thing.
                    var want = Facing(w.Man.Position);
                    var have = w.Man.Heading;
                    var off = Math.Abs(((want - have + 540f) % 360f) - 180f);

                    if (off > 10f) Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, want);
                }
                catch
                {
                    // Next tick.
                }
            }
        }

        /// <summary>
        /// Ordinary traffic near the junction, talked down.
        ///
        /// Everybody in a car within the radius who is not the player and not one of ours: no
        /// aggression, no panic, and a speed that suits a road with forty people stood in it.
        /// Re-applied every tick rather than once, because these are cars the game is streaming
        /// in and out constantly -- the one that just arrived is exactly the one that has not
        /// been told yet.
        ///
        /// THE PLAYER IS NOT TOUCHED. Taking the aggression off the person driving would be
        /// the mod deciding how they get to drive, which is not its business.
        /// </summary>
        private void Calm()
        {
            try
            {
                var player = Game.Player.Character;
                var mine = player != null && player.Exists() && player.CurrentVehicle != null
                           && player.CurrentVehicle.Exists()
                    ? player.CurrentVehicle.Handle
                    : 0;

                foreach (var car in World.GetNearbyVehicles(Middle, CalmRange))
                {
                    if (car == null || !car.Exists()) continue;
                    if (mine != 0 && car.Handle == mine) continue;

                    var driver = car.Driver;

                    if (driver == null || !driver.Exists() || !driver.IsAlive) continue;
                    if (driver.IsPlayer) continue;

                    // One of ours is already calm and already has a job. Telling it to slow
                    // down would take the circle apart.
                    if (Ours(driver)) continue;

                    var h = driver.Handle;

                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, h, 0.0f);
                    Function.Call(Hash.SET_DRIVER_ABILITY, h, 1.0f);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);

                    // Crawling pace. Not a stop -- a road that nobody can drive down at all
                    // backs traffic up for half a district and that is its own kind of wrong.
                    Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, h, 6f);

                    // AND NOBODY GETS INSIDE THE RING.
                    //
                    // Turned round rather than stopped. A car braked on the line is a road
                    // closure that never clears -- it sits there, the one behind it stops, and
                    // within a minute the junction is a car park with a takeover in the middle
                    // of it. Sent back the way it came, the road empties itself.
                    //
                    // NOT by switching the road nodes off in the area, which is the other way
                    // to do this: our own cars path in and out on those same nodes, and taking
                    // them away would stop the drifters reaching the circle at all.
                    var in_ = car.Position.DistanceTo(Middle);

                    // GOT THROUGH. It is not going to be persuaded now.
                    if (in_ < EatAt)
                    {
                        try
                        {
                            driver.Delete();
                            car.Delete();

                            Log.Info("Takeover: something drove into it. Removed.");
                        }
                        catch
                        {
                            // It will be tried again next tick.
                        }

                        continue;
                    }

                    if (in_ > BlockAt) continue;

                    int turned;

                    if (_turned.TryGetValue(car.Handle, out turned)
                        && Game.GameTime - turned < TurnGapMs)
                    {
                        continue;
                    }

                    _turned[car.Handle] = Game.GameTime;

                    // Back out along the line it came in on, and then some -- so the point it
                    // is given is behind it rather than across the junction.
                    var out_ = car.Position - Middle;
                    var len = out_.Length();

                    if (len < 0.5f) out_ = car.ForwardVector * -1f;
                    else out_ = out_ * (1f / len);

                    var back = Middle + out_ * 90f;
                    var road = World.GetNextPositionOnStreet(back, true);

                    if (road == Vector3.Zero) road = back;

                    Function.Call(Hash.CLEAR_PED_TASKS, h);

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, h, car.Handle,
                                  road.X, road.Y, road.Z, 12f, 0, car.Model.Hash,
                                  CareStyle, 8f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, h, true);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not calm the traffic: " + ex.Message);
            }
        }

        /// <summary>Whether this driver is one this file put there.</summary>
        private bool Ours(Ped who)
        {
            foreach (var r in _running)
            {
                if (r.Driver != null && r.Driver.Exists() && r.Driver.Handle == who.Handle) return true;
            }

            foreach (var p in _parked)
            {
                if (p.Driver != null && p.Driver.Exists() && p.Driver.Handle == who.Handle) return true;
            }

            foreach (var l in _law)
            {
                if (l.Cop != null && l.Cop.Exists() && l.Cop.Handle == who.Handle) return true;
            }

            // THE BIKES AND THE HELICOPTER, WHICH WERE BOTH MISSING FROM THIS LIST.
            //
            // Everything that reads this asks one question -- is this one of ours -- and every
            // answer of "no" about somebody who IS ours is a thing done TO our own event: the
            // cordon was turning our own riders round in the middle of their runs, and the
            // moment anything starts deleting outsiders this becomes the difference between a
            // clear junction and a junction with no bikes in it.
            //
            // A list that has to be added to every time something new is spawned is a list that
            // will be forgotten again, so this is worth saying plainly: ANYTHING SPAWNED FOR
            // THE TAKEOVER GOES IN HERE.
            foreach (var r in _riders)
            {
                if (r.Man != null && r.Man.Exists() && r.Man.Handle == who.Handle) return true;
            }

            if (_pilot != null && _pilot.Exists() && _pilot.Handle == who.Handle) return true;

            return false;
        }

        // ---- the cars that came to watch ----------------------------------------

        private void Cars()
        {
            var want = _rng.Next(ParkedMin, ParkedMax + 1);
            var lows = _rng.Next(LowsMin, LowsMax + 1);
            var donks = _rng.Next(DonksMin, DonksMax + 1);

            // Lowriders first, then donks, then everything else -- counted out of the same
            // total rather than added on top, so turning any of them up does not quietly grow
            // the number of cars ringing the junction.
            for (var i = 0; i < want; i++)
            {
                if (i < lows) Spectator(Kind.Low);
                else if (i < lows + donks) Spectator(Kind.Donk);
                else Spectator(Kind.Plain);
            }
        }

        /// <summary>What sort of car came to watch.</summary>
        private enum Kind
        {
            Plain,
            Low,
            Donk
        }

        private void Spectator(Kind kind)
        {
            try
            {
                var a = _rng.NextDouble() * Math.PI * 2d;
                var r = Ring + 5f + (float)(_rng.NextDouble() * 9.0);

                var slot = new Vector3(Middle.X + (float)Math.Cos(a) * r,
                                       Middle.Y + (float)Math.Sin(a) * r, Middle.Z);

                var from = OnRoad(DriveFromMin + (float)_rng.NextDouble() * (DriveFromMax - DriveFromMin));
                if (from == Vector3.Zero) return;

                var car = Make(kind == Kind.Low ? Lows
                             : kind == Kind.Donk ? Donks
                             : Parked, from);
                if (car == null) return;

                var driver = Behind(car);

                if (driver == null)
                {
                    car.Delete();
                    return;
                }

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              slot.X, slot.Y, slot.Z, 14f, 0, car.Model.Hash, CareStyle, 4f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, driver.Handle, true);

                if (kind == Kind.Low) Function.Call(Hash.SET_CAN_USE_HYDRAULICS, car.Handle, true);

                _parked.Add(new Parkee
                {
                    Car = car,
                    Driver = driver,
                    Slot = slot,
                    Low = kind == Kind.Low,
                    Hop = _rng.NextDouble() * Math.PI * 2d,
                    Rate = 2.2 + _rng.NextDouble() * 2.6
                });
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send a spectator: " + ex.Message);
            }
        }

        private void Parking()
        {
            foreach (var p in _parked)
            {
                if (p.There) continue;
                if (p.Car == null || !p.Car.Exists()) continue;
                if (p.Car.Position.DistanceTo(p.Slot) > CarArrivedRange) continue;

                p.There = true;

                try
                {
                    if (p.Driver != null && p.Driver.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, p.Driver.Handle,
                                      p.Car.Handle, 1, 4000);
                    }

                    Function.Call(Hash.SET_ENTITY_HEADING, p.Car.Handle, Facing(p.Car.Position));
                }
                catch
                {
                    // It stops where it stops.
                }
            }
        }

        /// <summary>The juice. Driven per frame, or it is a car changing height rather than hopping.</summary>
        private void Bounce()
        {
            foreach (var p in _parked)
            {
                if (!p.Low || !p.There) continue;
                if (p.Car == null || !p.Car.Exists()) continue;

                try
                {
                    p.Hop += p.Rate * 0.016;

                    // Squared, so it sits low most of the way round and snaps up. A plain sine
                    // is a car floating, which is not what a hydraulic does.
                    var s = 0.5 + 0.5 * Math.Sin(p.Hop);

                    Function.Call(Hash.SET_HYDRAULIC_SUSPENSION_RAISE_FACTOR,
                                  p.Car.Handle, (float)(s * s));
                }
                catch
                {
                    // Next frame.
                }
            }
        }

        // ---- the circle ---------------------------------------------------------

        private void Keep(int now)
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                var r = _running[i];

                var dead = r.Car == null || !r.Car.Exists()
                           || r.Driver == null || !r.Driver.Exists() || !r.Driver.IsAlive;

                if (dead)
                {
                    Out(r);
                    _running.RemoveAt(i);
                    continue;
                }

                // On the way out. Once clear of the ring it belongs to the world again.
                if (r.Leaving)
                {
                    if (r.Car.Position.DistanceTo(Middle) < Ring + 14f) continue;

                    Out(r);
                    _running.RemoveAt(i);
                    continue;
                }

                // On the way in. Close enough to its circle and it takes over by hand.
                if (!r.Circling)
                {
                    var wants = r.Middle ? OnTheMark : r.Radius + 6f;
                    var gap = r.Car.Position.DistanceTo(Circle);

                    if (gap > wants)
                    {
                        // STILL COMING, up to a point. Past that it is not coming.
                        if (r.Sent != 0 && now - r.Sent < ComeOnMs) continue;

                        // Close but stopped short -- start it where it is. Miles away or stuck,
                        // let it go and the top-up below sends somebody who can get here.
                        if (gap > CloseEnough)
                        {
                            Log.Info("Takeover: a car never made it in. Sending another.");
                            Leave(r);
                            continue;
                        }

                        Log.Info("Takeover: starting one short of the mark at "
                                 + gap.ToString("0.0") + "m.");
                    }

                    r.Circling = true;

                    r.Until = now + (r.Middle
                        ? _rng.Next(BurnMinMs, BurnMaxMs)
                        : _rng.Next(24000, 52000));

                    // AND THEY PULL UP FIRST.
                    //
                    // The donut used to begin on the frame the car arrived, which meant
                    // whatever speed it came in at went straight into the first burst of lock
                    // -- so it did not start a donut, it started a slide, from outside the
                    // circle, across it. That is the "run up" and it is why they looked like
                    // they were driving through rather than working.
                    //
                    // A real one stops. He rolls up, he sits there a second with the crowd
                    // round him, and THEN he stands on it from nothing. So the first thing
                    // after arriving is a brake, and the lock does not start until it is done.
                    r.NextAction = now + SettleMs;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                        // Temp action 1 is the brake. Held for the whole settle, so he is
                        // stopped rather than coasting to a halt across the middle of it.
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Driver.Handle,
                                      r.Car.Handle, 1, SettleMs);

                        // Both, because they are different things: drift tyres are the real
                        // ones off the tuning menu, and reduced grip is the blunt instrument
                        // behind them for a build that has not got the first.
                        Function.Call(Hash.SET_DRIFT_TYRES, r.Car.Handle, true);
                        Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, r.Car.Handle, true);
                    }
                    catch
                    {
                        // It still goes round.
                    }

                    continue;
                }

                if (now < r.Until) continue;

                // HIS GO IS UP, BUT NOT IF HE IS THE SHOW.
                //
                // The burnouts run for the whole night until the police arrive, and that means
                // there is never a moment with nothing happening in the middle. A car leaving
                // opens a gap of thirty seconds -- the time it takes the next one to drive in
                // from a block away -- and if the one leaving was the last one working, that
                // gap is the entire takeover being empty while somebody drives to it.
                //
                // So nobody stands down until somebody else is already going round. The
                // replacement is sent by the top-up below and this one carries on until it
                // arrives, which is a driver having a longer turn rather than a driver stuck.
                if (Spinning() <= 1) continue;

                Leave(r);
            }

            // ONE ON THE MARK AND ONE OR TWO ROUND THE OUTSIDE, counted separately -- the
            // middle is a place rather than a share of the traffic, and letting them come out
            // of the same pool means the mark stands empty whenever the circle is busy.
            var mark = 0;
            var round = 0;

            foreach (var r in _running)
            {
                if (r.Leaving) continue;

                if (r.Middle) mark++;
                else round++;
            }

            if (mark < 1) In(true);

            // TWO TO FOUR WORKING AT ONCE, and never fewer than two.
            //
            // The count is re-rolled on a clock rather than every tick. Rolling it every tick
            // meant the target flickered between three and four several times a second, so a
            // car was constantly being sent for and then not needed -- which is how you get
            // four cars queueing to enter a circle that wants three.
            if (now >= _reroll)
            {
                _reroll = now + RerollMs;
                _want = 1 + _rng.Next(1, 4);
            }

            var want = _want - 1;
            if (want < 1) want = 1;

            while (round < want)
            {
                if (!In(false)) break;
                round++;
            }
        }

        /// <summary>How many are actually working the circle right now.</summary>
        private int Spinning()
        {
            var n = 0;

            foreach (var r in _running)
            {
                if (r.Leaving || !r.Circling) continue;
                if (r.Car == null || !r.Car.Exists()) continue;

                n++;
            }

            return n;
        }

        /// <summary>How many should be out there, and when that was last decided.</summary>
        private int _want = 3;
        private int _reroll;
        private const int RerollMs = 45000;

        /// <summary>
        /// Two or three of the set, on dirt bikes, riding through it.
        ///
        /// THIS IS A DIFFERENT JOB TO THE DRIFT CARS AND IT IS DELIBERATELY NOT ONE OF THEM.
        /// A car in the circle is working a five metre patch and never leaves it; a bike is
        /// crossing the junction, at speed, in a straight line, and then coming back the other
        /// way. It is the thing that gives a takeover its depth -- something moving THROUGH
        /// while everything else moves around.
        ///
        /// Each rider holds a bearing and flips it by a half turn every time he reaches the
        /// end, so his route is always a line through the middle rather than a random pair of
        /// points that might not cross it at all. The ends are snapped to a road a hundred odd
        /// metres out, so he genuinely rides off up the street and genuinely comes back.
        ///
        /// HE IS TRYING NOT TO HIT ANYBODY, and that is not a wish -- it is the driving style.
        /// Steer-around-peds and steer-around-vehicles are both in it, which is the difference
        /// between a bike that threads through a crowd and a bike that mows one down. Stopping
        /// flags are left OUT: a rider who stops dead in the middle of a takeover for a man
        /// crossing the road has ended his own run, and the whole point is that he does not
        /// stop.
        /// </summary>
        private void Bikes(int now)
        {
            for (var i = _riders.Count - 1; i >= 0; i--)
            {
                var r = _riders[i];

                var dead = r.Bike == null || !r.Bike.Exists()
                           || r.Man == null || !r.Man.Exists() || !r.Man.IsAlive
                           || !r.Man.IsInVehicle(r.Bike);

                if (dead)
                {
                    Loose(r.Bike, r.Man);
                    _riders.RemoveAt(i);
                    continue;
                }

                // Arrived at his mark, or has been at it long enough that he plainly is not
                // going to. Either way, on to the next one.
                var there = r.Bike.Position.DistanceTo(r.Aim) < (r.Looping ? LoopDone : PassDone);
                var late = now - r.Sent > PassGiveUpMs;

                if (there || late) Send(r, now, true);
            }

            if (_riders.Count >= _wantBikes) return;
            if (now < _nextBike) return;

            _nextBike = now + BikeGapMs;

            Bike();
        }

        /// <summary>Turns a rider round, or moves him on to the next point of his circle.</summary>
        private void Send(Rider r, int now, bool turn)
        {
            if (r.Looping)
            {
                // ROUND IN STEPS, NOT ROUND IN ONE GO. There is no native for "drive a circle",
                // so the circle is a run of points on it -- he is always aimed a fifth of the
                // way further round, arrives, and is aimed a fifth further again. Small enough
                // that the path between two of them is a curve rather than a chord, and large
                // enough that he is never braking for a waypoint he is already on top of.
                if (turn) r.Angle += LoopStep * r.Round;

                var at = new Vector3(
                    Circle.X + (float)Math.Cos(r.Angle) * LoopRadius,
                    Circle.Y + (float)Math.Sin(r.Angle) * LoopRadius,
                    Circle.Z);

                r.Aim = at;
                r.Sent = now;

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, r.Man.Handle);

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Man.Handle, r.Bike.Handle,
                                  at.X, at.Y, at.Z, LoopSpeed, 0, r.Bike.Model.Hash,
                                  BikeStyle, 3f, true);

                    Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, r.Man.Handle, LoopSpeed);
                    Function.Call(Hash.SET_PED_KEEP_TASK, r.Man.Handle, true);
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover could not send the bike round: " + ex.Message);
                }

                return;
            }

            if (turn) r.ToFar = !r.ToFar;

            var run = Runs[r.Run];

            r.Aim = r.ToFar ? run.Far : run.Near;
            r.Sent = now;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, r.Man.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Man.Handle, r.Bike.Handle,
                              r.Aim.X, r.Aim.Y, r.Aim.Z, PassSpeed, 0, r.Bike.Model.Hash,
                              BikeStyle, 6f, true);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, r.Man.Handle, PassSpeed);
                Function.Call(Hash.SET_PED_KEEP_TASK, r.Man.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send a bike through: " + ex.Message);
            }
        }

        /// <summary>
        /// Halfway through, a police helicopter turns up and starts circling with its light on.
        ///
        /// AND IT DOES NOT DO ANYTHING ELSE, WHICH IS THE POINT. It does not call units, it
        /// does not end the takeover, and nobody scatters -- the cars on the ground are still
        /// what stops it, later. This is the half hour of everybody carrying on with a light
        /// sweeping over them, which is the part of a real one that nobody films because they
        /// are all in it.
        ///
        /// It comes from a long way out and flies in, so the first you know is the noise from
        /// somewhere over Davis, then the light on the buildings, then it overhead. Spawning it
        /// already circling would be a helicopter that was always there.
        /// </summary>
        private void Chopper(int now)
        {
            if (_heli != null)
            {
                // Gone -- shot down, despawned, streamed out with the pilot. Let it go rather
                // than trying to nurse it back; another one is not worth the code.
                if (!_heli.Exists() || _pilot == null || !_pilot.Exists() || !_pilot.IsAlive)
                {
                    Away();
                }

                return;
            }

            if (_heliDone) return;
            if (_startedAt == 0 || now - _startedAt < HeliAfterMs) return;

            _heliDone = true;

            try
            {
                // A long way out and well up. GetNextPositionOnStreet is deliberately not used:
                // it is a helicopter and the one thing it does not need is a road.
                var bearing = _rng.NextDouble() * Math.PI * 2d;

                var from = new Vector3(
                    Middle.X + (float)Math.Cos(bearing) * HeliFrom,
                    Middle.Y + (float)Math.Sin(bearing) * HeliFrom,
                    Middle.Z + HeliHigh);

                var model = new Model("polmav");
                if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) return;

                _heli = World.CreateVehicle(model, from);
                model.MarkAsNoLongerNeeded();

                if (_heli == null || !_heli.Exists()) return;

                _heli.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _heli.Handle, true, true);
                Function.Call(Hash.SET_HELI_BLADES_FULL_SPEED, _heli.Handle);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _heli.Handle, true, true, false);

                var cop = new Model("s_m_y_cop_01");

                if (!cop.IsValid || !cop.Request(2000))
                {
                    _heli.Delete();
                    _heli = null;
                    return;
                }

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, _heli.Handle,
                                                6, cop.Hash, -1, false, false);

                cop.MarkAsNoLongerNeeded();

                _pilot = Entity.FromHandle(handle) as Ped;

                if (_pilot == null || !_pilot.Exists())
                {
                    _heli.Delete();
                    _heli = null;
                    return;
                }

                _pilot.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _pilot.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _pilot.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _pilot.Handle, false);

                // The game's own searchlight, handed to the AI so it points it wherever it is
                // looking. Whether a given build actually swings it is not something that can
                // be checked from here -- so the beam below is drawn by hand as well, and that
                // is the one that is guaranteed to be over the junction.
                Function.Call(Hash.SET_VEHICLE_SEARCHLIGHT, _heli.Handle, true, true);

                // Mission 4 is "circle the coordinate". Numbers after it: speed, the radius it
                // holds, no fixed heading, and a ceiling and floor it stays between.
                Function.Call(Hash.TASK_HELI_MISSION, _pilot.Handle, _heli.Handle, 0, 0,
                              Circle.X, Circle.Y, Circle.Z + HeliHigh,
                              4, HeliSpeed, HeliRing, -1f,
                              (int)(HeliHigh + 20f), (int)(HeliHigh - 15f), -1f, 0);

                Function.Call(Hash.SET_PED_KEEP_TASK, _pilot.Handle, true);

                Log.Info("Takeover: a helicopter is on its way.");
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not put a helicopter up: " + ex.Message);

                Away();
            }
        }

        /// <summary>
        /// The beam, drawn by hand, every frame.
        ///
        /// DRAWN RATHER THAN ASKED FOR. SET_VEHICLE_SEARCHLIGHT gives the helicopter its light
        /// and hands the aiming to the AI, which points it at whatever the AI is interested in
        /// -- and what it is interested in during a circling mission is not reliably the thing
        /// on the ground we care about. A drawn spot light is a draw call: it goes exactly
        /// where it is told, every frame, and it cannot decide to look somewhere else.
        ///
        /// It wanders rather than sits. A beam nailed to one point is a lamp; one that drifts
        /// around the middle at walking pace is somebody up there looking at things, which is
        /// what a police helicopter over a takeover is doing.
        /// </summary>
        private void Beam()
        {
            if (_heli == null || !_heli.Exists()) return;

            try
            {
                _sweep += SweepRate;

                // A slow wander over the junction rather than a lock on the centre mark.
                var at = new Vector3(
                    Circle.X + (float)Math.Cos(_sweep) * SweepWide,
                    Circle.Y + (float)Math.Sin(_sweep * 0.7) * SweepWide,
                    Circle.Z);

                var from = _heli.Position;
                var dir = at - from;

                var len = dir.Length();
                if (len < 1f) return;

                dir = dir * (1f / len);

                Function.Call(Hash.DRAW_SPOT_LIGHT,
                              from.X, from.Y, from.Z,
                              dir.X, dir.Y, dir.Z,
                              255, 245, 220,
                              len + 25f, 12f, 0f, 13f, 0f);
            }
            catch
            {
                // Next frame.
            }
        }

        /// <summary>It goes home, and takes the pilot with it.</summary>
        private void Away()
        {
            try
            {
                if (_pilot != null && _pilot.Exists()) _pilot.Delete();
                if (_heli != null && _heli.Exists()) _heli.Delete();
            }
            catch
            {
                // Teardown.
            }

            _pilot = null;
            _heli = null;
        }

        private Vehicle _heli;
        private Ped _pilot;
        private bool _heliDone;
        private double _sweep;

        /// <summary>
        /// When it turns up, where from, and how it flies.
        ///
        /// The same half hour the feed waits, so the two things that mark the middle of the
        /// night happen together: people start talking about it and something starts circling
        /// over it.
        /// </summary>
        private const int HeliAfterMs = 900000;
        private const float HeliFrom = 420f;
        private const float HeliHigh = 55f;
        private const float HeliSpeed = 22f;
        private const float HeliRing = 60f;

        private const double SweepRate = 0.006;
        private const float SweepWide = 16f;

        /// <summary>One more of them, from a block out.</summary>
        private void Bike()
        {
            try
            {
                var from = OnRoad(DriveFromMin + (float)_rng.NextDouble() * (DriveFromMax - DriveFromMin));
                if (from == Vector3.Zero) return;

                var bike = Make(Bikers, from, true, false);
                if (bike == null) return;

                var man = Behind(bike);

                if (man == null)
                {
                    bike.Delete();
                    return;
                }

                // Helmetless, because everybody else at this junction is. A dirt bike through a
                // street takeover with full safety gear is a man who has come from a track day.
                try { Core.Helmets.Off(man); }
                catch { /* he keeps the lid on */ }

                // THE EMPTIER RUN. Two riders both bouncing along the same line would meet
                // head-on in the middle of it every single pass, which is one collision per
                // crossing rather than two bikes crossing a junction.
                // ONE OF THEM GOES ROUND THE MIDDLE and the rest run the lines. The one in
                // the middle comes first, because he is the one you always want there -- the
                // lines look after themselves between passes and he does not.
                var looping = true;

                foreach (var other in _riders)
                {
                    if (!other.Looping) continue;

                    looping = false;
                    break;
                }

                Rider r;

                if (looping)
                {
                    r = new Rider
                    {
                        Bike = bike,
                        Man = man,
                        Looping = true,

                        // Started from where he actually is, so his first move is onto the
                        // circle from wherever he spawned rather than across it.
                        Angle = Math.Atan2(bike.Position.Y - Circle.Y, bike.Position.X - Circle.X),

                        // And he goes round whichever way he feels like, which is the other
                        // half of it being multi-directional: a bike that always goes anti-
                        // clockwise round a junction is a roundabout.
                        Round = _rng.Next(2) == 0 ? 1 : -1
                    };
                }
                else
                {
                    // THE EMPTIER RUN. Two riders both bouncing along the same line would meet
                    // head-on in the middle of it every single pass, which is one collision per
                    // crossing rather than two bikes crossing a junction.
                    var which = 0;
                    var fewest = int.MaxValue;

                    for (var i = 0; i < Runs.Length; i++)
                    {
                        var n = 0;
                        foreach (var other in _riders) if (other.Run == i && !other.Looping) n++;

                        if (n >= fewest) continue;

                        fewest = n;
                        which = i;
                    }

                    r = new Rider
                    {
                        Bike = bike,
                        Man = man,
                        Run = which,

                        // WHICHEVER END HE FEELS LIKE. It used to be "the one he is furthest
                        // from", which is tidier and makes both runs start the same way every
                        // night -- and the point of two lines is that they are not in step.
                        ToFar = _rng.Next(2) == 0
                    };
                }

                _riders.Add(r);

                Send(r, Game.GameTime, false);

                Log.Info("Takeover: a bike came through.");
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not put a bike out: " + ex.Message);
            }
        }

        /// <summary>
        /// The front wheel, held up by hand, every frame.
        ///
        /// The same force the rollers use -- upward, applied behind the centre of mass, which
        /// is what a wheelie is. It has to be per frame: the same push at tick intervals is a
        /// pothole rather than a wheelie.
        ///
        /// It comes down on its own when he slows, rather than being carried nose-up at walking
        /// pace to the end of a timer, and he only goes for one when he is near the middle --
        /// the whole point is that it happens where people can see it.
        /// </summary>
        private void Wheelie(int now)
        {
            foreach (var r in _riders)
            {
                if (r.Bike == null || !r.Bike.Exists()) continue;
                if (r.Man == null || !r.Man.Exists() || !r.Man.IsAlive) continue;

                float speed;

                try { speed = r.Bike.Speed; }
                catch { continue; }

                // NO TIMER. THE MARKS ARE THE TIMER.
                //
                // It used to hold for a fixed few seconds and then have to decide whether to go
                // again, with a rest in between -- so a crossing that took longer than the
                // number got a wheel that dropped in the middle of the junction and came back
                // up on the far side, for no reason anybody watching could see. The marks
                // already say exactly where it should be up. Nothing else needs to.
                //
                // So: between the marks and moving, the wheel is up. Outside them, or too slow
                // to hold it, it is down. That is the whole rule, it is evaluated fresh every
                // frame, and there is no state to get out of step.
                //
                // The one going round the middle has no marks and no outside -- he is at the
                // junction by definition, so he is at it the whole time.
                var up = r.Looping || Runs[r.Run].Between(r.Bike.Position);

                if (!up || speed < WheelieNeeds) continue;

                try
                {
                    Function.Call(Hash.APPLY_FORCE_TO_ENTITY, r.Bike.Handle, 1,
                                  0f, 0f, WheelieLift,
                                  0f, -WheelieBehind, 0f,
                                  0, true, true, true, false, true);
                }
                catch
                {
                    // Next frame.
                }
            }
        }

        /// <summary>The bikes, and how they ride.</summary>
        private static readonly string[] Bikers = { "manchez", "manchez2", "manchez3", "sanchez" };

        /// <summary>
        /// One line across the junction, with the two marks the front wheel is up between.
        ///
        /// THE MARKS ARE THE WHEELIE, NOT THE RIDE, and the difference is the whole reason this
        /// class exists rather than a pair of coordinates. A bike starting AT the first mark
        /// would not be doing anything at it: twenty-odd metres from a standstill is a bike
        /// still accelerating when it reaches the middle, so it would cross the junction at
        /// walking pace with the wheel down and pop the wheelie on the way OUT -- backwards
        /// from what is wanted, and the marks would be the two places nothing happened.
        ///
        /// So each run extends well past both marks. He arrives at the first already at speed,
        /// the wheel goes up there, it comes down at the second, and there is road past both to
        /// slow, turn, and line up again out of everybody's way.
        ///
        /// The ends are worked out from the marks rather than written down, so moving a mark
        /// moves the run with it and there is only ever one pair of numbers that can be wrong.
        /// </summary>
        private sealed class Run
        {
            public readonly Vector3 From;
            public readonly Vector3 To;

            /// <summary>Unit vector from the first mark to the second, flat.</summary>
            public readonly Vector3 Way;

            /// <summary>How long the wheelie stretch is.</summary>
            public readonly float Length;

            /// <summary>Where he actually rides between, past both marks.</summary>
            public readonly Vector3 Near;
            public readonly Vector3 Far;

            public Run(Vector3 from, Vector3 to)
            {
                From = from;
                To = to;

                var d = new Vector3(to.X - from.X, to.Y - from.Y, 0f);
                var len = d.Length();

                Length = len;
                Way = len < 0.01f ? new Vector3(1f, 0f, 0f) : d * (1f / len);

                Near = from - Way * RunUp;
                Far = to + Way * RunUp;
            }

            /// <summary>
            /// Whether a bike is between the two marks.
            ///
            /// Measured ALONG the line rather than as a distance from either mark, because a
            /// bike a couple of metres wide of it is still between them as far as anybody
            /// watching is concerned -- and a plain radius check would drop the wheel every
            /// time he wandered out of a circle drawn round a point.
            /// </summary>
            public bool Between(Vector3 at)
            {
                var t = (at.X - From.X) * Way.X + (at.Y - From.Y) * Way.Y;

                return t >= -2f && t <= Length + 2f;
            }
        }

        /// <summary>
        /// The two runs, read off the ground in game.
        ///
        /// TWO DIRECTIONS, CROSSING NEAR THE MIDDLE. The first is forty-eight metres of
        /// diagonal whose midpoint is one and a bit metres off the centre of the circle; the
        /// second is forty-one metres of the main road, three metres off it. So they cross each
        /// other roughly where the cars are working, which is what makes two bikes read as a
        /// junction full of them rather than as one bike going back and forth twice.
        /// </summary>
        private static readonly Run[] Runs =
        {
            new Run(new Vector3(-146.195f, -1720.760f, 30.131f),
                    new Vector3(-114.458f, -1757.620f, 29.808f)),

            new Run(new Vector3(-106.582f, -1739.599f, 30.225f),
                    new Vector3(-147.131f, -1743.940f, 30.132f))
        };

        /// <summary>How much road he gets each side of the marks to get up to speed and turn.</summary>
        private const float RunUp = 60f;

        /// <summary>
        /// The circle the one in the middle rides, and how he rides it.
        ///
        /// TEN METRES, WHICH IS ITS OWN LANE. The drift cars work a five metre circle and the
        /// crowd stands on corners thirteen metres out and further, so ten is the gap between
        /// them -- wide enough that he is not riding through a donut, tight enough that he is
        /// plainly part of it rather than doing laps of the block.
        ///
        /// A fifth of a turn per waypoint, and slower than a line pass: he is holding a wheelie
        /// through a bend, which is a thing you do at a speed you can hold rather than as fast
        /// as the bike will go.
        /// </summary>
        private const float LoopRadius = 10f;
        private const float LoopSpeed = 13f;
        private const float LoopDone = 4.5f;
        private const double LoopStep = Math.PI * 2d / 5d;

        /// <summary>
        /// Steer around everything, stop for nothing.
        ///
        /// 4, 8, 16 and 32 -- around vehicles, around parked vehicles, around people, around
        /// objects. The STOPPING flags are deliberately absent: a rider who stops dead in the
        /// middle of a takeover for somebody crossing has ended his own run, and not stopping
        /// is the entire behaviour. He avoids them by going round, which is the thing a bike
        /// can do and a car in a circle cannot.
        /// </summary>
        private const int BikeStyle = 4 | 8 | 16 | 32;

        /// <summary>
        /// How everything arriving at the takeover drives, and this is a fix.
        ///
        /// EVERY DRIVE TASK IN THIS FILE WAS 786603, the game's ordinary traffic style. Read as
        /// flags that is stop-before-vehicles, stop-before-peds, avoid-EMPTY-vehicles,
        /// avoid-objects and stop-at-lights -- and the two bits it does NOT contain are steer
        /// around a vehicle with somebody in it, and steer around a person.
        ///
        /// Which is exactly the wrong pair to be missing here. A takeover is a junction full of
        /// occupied cars and people stood in the road: the one place in the city where "stops
        /// for things but never goes round them" turns into a car nosing into the back of a
        /// donut because the donut is in its lane and it has no instruction to do anything but
        /// wait. Same fault the Knowai had, in seven more places.
        ///
        ///     1   stop before vehicles          16   steer around peds
        ///     2   stop before peds              32   steer around objects
        ///     4   steer around vehicles        128   stop at lights
        ///     8   steer around empty vehicles  256   indicate
        ///
        /// All eight for anything ARRIVING -- it still stops for people, it just also goes
        /// round them.
        /// </summary>
        private const int CareStyle = 1 | 2 | 4 | 8 | 16 | 32 | 128 | 256;

        /// <summary>
        /// And for anything LEAVING, the police coming in, and the drift cars ARRIVING.
        ///
        /// The same steering, none of the stopping. A car scattering from a police raid that
        /// stops at a red light is not scattering, and a squad car that gives way on the
        /// approach is not a raid. They still go round people, which is the part that matters.
        ///
        /// THE DRIFT CARS USE IT COMING IN, and that is the other half of why none of them were
        /// reaching the middle. CareStyle contains stop-before-peds -- correct for a car
        /// parking, and fatal for a car whose destination is the centre of a ring of sixty
        /// people. It drove to the edge of the crowd, did exactly as it was told, and stopped.
        /// Somebody arriving to work the circle is IN A HURRY: he goes round people rather than
        /// queueing behind them, which is the only way through a junction that is full by
        /// design.
        /// </summary>
        private const int RushStyle = 4 | 8 | 16 | 32;

        private const float PassDone = 14f;
        private const float PassSpeed = 24f;
        private const int PassGiveUpMs = 40000;

        private const int BikeGapMs = 9000;
        private const float WheelieNeeds = 8f;
        private const float WheelieLift = 1.9f;
        private const float WheelieBehind = 1.15f;
        private const int WheelieMaxMs = 4200;

        private int _wantBikes = 2;
        private int _nextBike;

        /// <summary>Somebody drives in for their go, on the mark or round the outside.</summary>
        private bool In(bool middle)
        {
            try
            {
                var from = OnRoad(DriveFromMin + (float)_rng.NextDouble() * (DriveFromMax - DriveFromMin));
                if (from == Vector3.Zero) return false;

                // NOT A SHOW CAR. It keeps the paint, the rims, the bodywork and the neon;
                // it loses the coloured tyre smoke and the coloured headlights. See Dress.
                var car = Make(Drifters, from, true, false);
                if (car == null) return false;

                var driver = Behind(car);

                if (driver == null)
                {
                    car.Delete();
                    return false;
                }

                var r = new Runner
                {
                    Car = car,
                    Driver = driver,
                    Middle = middle,
                    Radius = DriftMin + (float)_rng.NextDouble() * (DriftMax - DriftMin),
                    Speed = 9f + (float)_rng.NextDouble() * 5f,
                    Way = _rng.Next(2) == 0 ? 1 : -1
                };

                // WHERE HE IS AIMING, AND IT IS NOT THE MIDDLE.
                //
                // Everybody was sent to the centre mark, which meant every car that joined
                // drove straight across the circle -- through whoever was already in it, past
                // the burnout on the mark, and out the other side before the leash pulled it
                // back. It read as traffic cutting through a takeover rather than as somebody
                // arriving at one.
                //
                // The one doing the static burnout genuinely is going to the middle. Everybody
                // else is aimed at the point on their own circle NEAREST THE WAY THEY CAME IN,
                // so they arrive on the ring tangentially, already where they are meant to be
                // going round, and start their donut from there.
                var aim = Circle;

                if (!middle)
                {
                    var inFrom = from - Circle;
                    var len = inFrom.Length();

                    if (len > 0.5f)
                    {
                        inFrom = inFrom * (1f / len);
                        aim = Circle + inFrom * r.Radius;
                    }
                }

                // ELEVEN, DOWN FROM SIXTEEN. Sixteen metres a second is about fifty-eight
                // kilometres an hour, which is a fine speed to cross a district at and far too
                // much to be doing on the last twenty metres into a circle full of people. The
                // approach is not the interesting part and does not need to be quick.
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              aim.X, aim.Y, aim.Z, ComeInSpeed, 0, car.Model.Hash,
                              RushStyle, 2f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, driver.Handle, true);

                r.Sent = Game.GameTime;

                _running.Add(r);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send one in: " + ex.Message);
                return false;
            }
        }

        /// <summary>Their go is over. Grip back, smoke off, and out the way they came.</summary>
        private void Leave(Runner r)
        {
            r.Leaving = true;
            r.Circling = false;

            try
            {
                Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, false);
                Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, r.Car.Handle, false);
                Function.Call(Hash.SET_DRIFT_TYRES, r.Car.Handle, false);

                var away = OnRoad(150f + (float)_rng.NextDouble() * 110f);
                if (away == Vector3.Zero) away = Middle.Around(190f);

                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Driver.Handle, r.Car.Handle,
                              away.X, away.Y, away.Z, 15f, 0, r.Car.Model.Hash, RushStyle, 10f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send one out: " + ex.Message);
            }
        }

        /// <summary>
        /// Keeping the ones on the floor doing what they came to do.
        ///
        /// NOTHING HERE MOVES A CAR ANY MORE, and that is the fix. The first version advanced
        /// an angle and then wrote the heading and the forward speed straight onto the vehicle
        /// every frame -- which is not driving, it is teleporting sixty times a second. A car
        /// moved that way has no momentum, takes no notice of what it hits, and goes through a
        /// crowd like a plough. It also could not be steered by anything, which is why they
        /// ended up off course: they were never on a course, they were being dragged round a
        /// circle drawn in the script.
        ///
        /// So the game drives them. The stunt actions on TASK_VEHICLE_TEMP_ACTION are a real
        /// driver putting real lock on with real throttle, so the physics, the collision and
        /// the tyre smoke all happen for the ordinary reasons -- and a car about to hit
        /// somebody behaves like a car about to hit somebody.
        ///
        /// The action is re-issued rather than held. A temp action has a duration and expires,
        /// and a driver whose action has run out coasts to a stop -- so each is topped up
        /// slightly before it ends, which is what makes it continuous.
        /// </summary>
        private void Working(int now)
        {
            foreach (var r in _running)
            {
                if (!r.Circling) continue;
                if (r.Car == null || !r.Car.Exists()) continue;
                if (r.Driver == null || !r.Driver.Exists() || !r.Driver.IsAlive) continue;

                // THE LEASH, and it is a real drive rather than a shove. A donut wanders --
                // that is what a donut does -- so anybody who has drifted out of the area gets
                // an ordinary route back into it and picks up again when it arrives.
                var gap = r.Car.Position.DistanceTo(Circle);

                if (gap > r.Radius + Wander)
                {
                    if (now < r.NextAction) continue;

                    r.NextAction = now + 3000;

                    try
                    {
                        // Back to his own circle rather than to the centre, for the same reason
                        // he was not sent to the centre in the first place -- a car recovering
                        // from a wide slide should rejoin the ring, not drive across it.
                        var back = Circle;

                        if (!r.Middle)
                        {
                            var out_ = r.Car.Position - Circle;
                            var len = out_.Length();

                            if (len > 0.5f)
                            {
                                out_ = out_ * (1f / len);
                                back = Circle + out_ * r.Radius;
                            }
                        }

                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Driver.Handle,
                                      r.Car.Handle, back.X, back.Y, back.Z,
                                      12f, 0, r.Car.Model.Hash, RushStyle, 4f, true);
                    }
                    catch
                    {
                        // Next time round.
                    }

                    continue;
                }

                if (now < r.NextAction) continue;

                try
                {
                    // THE ONE ON THE MARK STANDS STILL. It used to be told to hold a burnout
                    // AND to drive a donut in the same breath, which are two different things
                    // to do with the same wheels -- so it did the donut, because a temp action
                    // is a driver input and beats a flag. That is why nothing ever sat there
                    // smoking: there was a burnout car and it was driving in circles.
                    if (r.Middle)
                    {
                        Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, true);

                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Driver.Handle,
                                      r.Car.Handle, Burn(), BurstMs);
                    }
                    else
                    {
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Driver.Handle,
                                      r.Car.Handle, Spin(r.Way), BurstMs);
                    }

                    r.NextAction = now + BurstMs - 400;
                }
                catch
                {
                    r.NextAction = now + BurstMs;
                }
            }
        }

        /// <summary>
        /// The stunt action for a donut, one way or the other.
        ///
        /// THESE TWO NUMBERS ARE THE ONE THING IN HERE THAT CANNOT BE CHECKED FROM A DESK. The
        /// temp action list is not documented by Rockstar and the community numbering is the
        /// only source there is; 30 and 31 are what everybody uses for a spinning donut. If
        /// they are something else on a given build they are in the ini, so finding the right
        /// pair is a matter of trying two numbers rather than rebuilding anything.
        /// </summary>
        private int Spin(int way)
        {
            if (_cfg == null) return way > 0 ? 30 : 31;

            return way > 0 ? _cfg.TakeoverSpinLeft : _cfg.TakeoverSpinRight;
        }

        /// <summary>
        /// And the one for standing on the spot with the back wheels going.
        ///
        /// Same caveat as the donut pair: the temp action list is community numbering and 23 is
        /// what everybody uses for a burnout. It is in the ini for the same reason -- if this
        /// build numbers them differently it is a number to change, not a rebuild.
        /// </summary>
        private int Burn()
        {
            return _cfg == null ? 23 : _cfg.TakeoverBurnAction;
        }

        /// <summary>How long one burst of lock lasts, and how far they may wander.</summary>
        private const int BurstMs = 3200;

        /// <summary>How fast they come in, and how long they sit before they start.</summary>
        private const float ComeInSpeed = 11f;
        private const int SettleMs = 1500;
        private const float Wander = 7f;

        /// <summary>
        /// The crowd, out loud.
        ///
        /// A ring of fifty people cheering silently is the uncanny part of every crowd anybody
        /// has ever built out of scenarios: the animations are right, the place sounds empty,
        /// and it reads as a screenshot rather than as a night out. This is the difference
        /// between watching a takeover and being at one.
        ///
        /// A FEW OF THEM, NOT ALL OF THEM. Fifty peds shouting on the same frame is a wall of
        /// noise with no shape to it -- three at a time, a second or so apart, is a crowd.
        /// They are picked at random each time so it moves around the ring rather than coming
        /// from the same three men all night.
        ///
        /// The speech names are tried and not checked, which is safe here in a way that native
        /// hashes are not: an ambient speech that does not exist on this build is silence, and
        /// silence is what we already had.
        /// </summary>
        private void Racket(int now)
        {
            if (now < _nextNoise || _crowd.Count == 0) return;

            _nextNoise = now + NoiseMinMs + _rng.Next(NoiseMaxMs - NoiseMinMs);

            for (var i = 0; i < NoisyAtOnce; i++)
            {
                try
                {
                    var w = _crowd[_rng.Next(_crowd.Count)];

                    if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive || !w.There) continue;

                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, w.Man.Handle,
                                  Shouts[_rng.Next(Shouts.Length)], "SPEECH_PARAMS_FORCE_SHOUTED");
                }
                catch
                {
                    // Next one.
                }
            }
        }

        /// <summary>What a crowd shouts at a car going sideways.</summary>
        private static readonly string[] Shouts =
        {
            "GENERIC_CURSE_HIGH", "GENERIC_CURSE_MED", "GENERIC_SHOCKED_HIGH",
            "GENERIC_SHOCKED_MED", "GENERIC_WAR_CRY", "CHEER", "GENERIC_INSULT_HIGH",
            "GENERIC_HOWS_IT_GOING", "GENERIC_FRIGHTENED_HIGH", "GENERIC_WHOA"
        };

        /// <summary>How many shout at once, and how often.</summary>
        private const int NoisyAtOnce = 3;
        private const int NoiseMinMs = 1400;
        private const int NoiseMaxMs = 3800;

        private int _nextNoise;

        /// <summary>
        /// Somebody in the crowd throws a flare.
        ///
        /// THROWN, NOT FIRED, and the difference is bigger than it sounds. A flare gun is a
        /// GUN: the ped raises it, the game treats the report as gunfire, and a ring of sixty
        /// people who have just heard a shot is a ring of sixty people leaving. A hand flare is
        /// a thrown object, so it lands, burns, lights the smoke orange, and nobody flinches.
        /// It is also what people actually throw at these.
        ///
        /// ONLY WHILE THERE IS SOMETHING TO LIGHT. A flare goes out because a car is sideways
        /// in front of you, not on a timer -- so this does nothing at all unless somebody is
        /// working the circle, which also means it stops on its own when the police arrive.
        ///
        /// He is given one flare and it is taken back afterwards, because a man stood in a
        /// crowd holding one for three hours will eventually be seen holding it.
        ///
        /// Half go high, arcing over the middle, and half are lobbed low across it. The low
        /// ones are what actually light the cars; the high ones are what you see from a street
        /// away and come to look at.
        /// </summary>
        private void Flares(int now)
        {
            if (now < _nextFlare || _crowd.Count == 0) return;
            if (Spinning() < 1) return;

            _nextFlare = now + FlareMinMs + _rng.Next(FlareMaxMs - FlareMinMs);

            try
            {
                var w = _crowd[_rng.Next(_crowd.Count)];

                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive || !w.There) return;

                var h = w.Man.Handle;
                var flare = Game.GenerateHash("weapon_flare");

                Function.Call(Hash.GIVE_WEAPON_TO_PED, h, flare, 1, false, true);
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, h, flare, true);

                // Nobody takes him for a threat, and he does not take anybody else for one.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);

                Vector3 at;

                if (_rng.Next(100) < FlareUpShare)
                {
                    // High over the middle, so it arcs up and comes down through the smoke.
                    at = new Vector3(Circle.X + (float)(_rng.NextDouble() * 8.0 - 4.0),
                                     Circle.Y + (float)(_rng.NextDouble() * 8.0 - 4.0),
                                     Circle.Z + FlareUpHigh);
                }
                else
                {
                    // Lobbed low across it, which is the one that lights the cars.
                    at = new Vector3(Circle.X + (float)(_rng.NextDouble() * 6.0 - 3.0),
                                     Circle.Y + (float)(_rng.NextDouble() * 6.0 - 3.0),
                                     Circle.Z + 1.0f);
                }

                // Turned to face it first. A thrown object goes where the ped is pointed as
                // much as where it is aimed, and a flare lobbed over his own shoulder is a
                // flare in the crowd behind him.
                var to = at - w.Man.Position;

                w.Man.Heading =
                    (float)((Math.Atan2(-to.X, to.Y) * 180.0 / Math.PI + 360.0) % 360.0);

                Function.Call(Hash.TASK_THROW_PROJECTILE, h, at.X, at.Y, at.Z);

                _flareFrom = w;
                _flareBack = now + FlareHoldMs;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover: no flare: " + ex.Message);
            }
        }

        /// <summary>And the flare goes off his hands once he has thrown it.</summary>
        private void Unarm(int now)
        {
            if (_flareBack == 0 || now < _flareBack) return;

            _flareBack = 0;

            try
            {
                var w = _flareFrom;
                _flareFrom = null;

                if (w == null || w.Man == null || !w.Man.Exists()) return;

                Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, w.Man.Handle, true);

                // Back to whatever he was doing before somebody handed him a flare.
                w.There = false;
            }
            catch
            {
                // He keeps it. Not the end of the world.
            }
        }

        /// <summary>
        /// And somebody sets a firework up in the road.
        ///
        /// THE SETTING UP IS THE BIT WORTH HAVING. A firework that simply goes off is a
        /// particle effect; a man crouched over something in the road for five seconds, and
        /// THEN a firework, is somebody who brought one. So it is three beats -- he walks out
        /// of the ring to a spot, he crouches over it, it goes off -- and the middle beat is
        /// the longest.
        ///
        /// Set down outside the circle the cars work and inside the ring people stand on, so
        /// it is in the open without being in the way of a car that is about to slide.
        /// </summary>
        private void Firework(int now)
        {
            // Going off, or being packed away.
            if (_fireAt != 0)
            {
                if (now < _fireAt) return;

                _fireAt = 0;
                Bang(_fireSpot);

                if (_fireMan != null && _fireMan.Man != null && _fireMan.Man.Exists())
                {
                    // Up, out of the way, and back to the ring.
                    try { Function.Call(Hash.CLEAR_PED_TASKS, _fireMan.Man.Handle); }
                    catch { }

                    _fireMan.There = false;
                }

                _fireMan = null;

                try { if (_fireProp != null && _fireProp.Exists()) _fireProp.Delete(); }
                catch { }

                _fireProp = null;
                return;
            }

            if (now < _nextFire || _crowd.Count == 0) return;

            _nextFire = now + FireMinMs + _rng.Next(FireMaxMs - FireMinMs);

            try
            {
                var w = _crowd[_rng.Next(_crowd.Count)];

                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive || !w.There) return;

                var a = _rng.NextDouble() * Math.PI * 2d;
                var r = FireRingMin + (float)(_rng.NextDouble() * (FireRingMax - FireRingMin));

                _fireSpot = Ground(new Vector3(Circle.X + (float)Math.Cos(a) * r,
                                               Circle.Y + (float)Math.Sin(a) * r, Circle.Z));

                // The box he is crouched over. Cosmetic -- if it will not load he is still a
                // man crouched over something, and something still goes off.
                try
                {
                    var model = new Model("ind_prop_firework_01");

                    if (model.IsValid && model.IsInCdImage && model.Request(600))
                    {
                        _fireProp = World.CreateProp(model, _fireSpot, false, false);
                        model.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    _fireProp = null;
                }

                _fireMan = w;
                w.There = false;

                Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                // Out to it, then crouched over it. The scenario is the game's own "somebody
                // bent over inspecting a thing on the ground", which is exactly the shape of a
                // man setting a firework up.
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                              _fireSpot.X, _fireSpot.Y, _fireSpot.Z, 2.5f, -1, 1f, true, 0f);

                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, w.Man.Handle,
                              "WORLD_HUMAN_CROUCH_INSPECT", 0, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);

                _fireAt = now + FireSetUpMs;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover: no firework: " + ex.Message);

                _fireAt = 0;
                _fireMan = null;
            }
        }

        /// <summary>
        /// It goes off.
        ///
        /// The game's own Independence Day firework effects, which are a named asset that has
        /// to be requested and then selected before anything will draw. Missing that second
        /// call is the usual reason a particle effect does nothing and reports no error.
        ///
        /// Three of them together, up the height of a house, because one burst at ground level
        /// is a firework that did not work.
        /// </summary>
        private void Bang(Vector3 at)
        {
            try
            {
                Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, FireAsset);

                if (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, FireAsset)) return;

                for (var i = 0; i < 3; i++)
                {
                    var up = at + new Vector3(0f, 0f, 6f + i * 5f);

                    Function.Call(Hash.USE_PARTICLE_FX_ASSET, FireAsset);

                    Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD,
                                  FireBursts[_rng.Next(FireBursts.Length)],
                                  up.X, up.Y, up.Z, 0f, 0f, 0f,
                                  1.4f, false, false, false);
                }

                Function.Call(Hash.PLAY_SOUND_FROM_COORD, -1, "Explosion",
                              at.X, at.Y, at.Z, "DLC_HEIST_FLEECA_SOUNDSET", false, 0, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover: the firework did not go off: " + ex.Message);
            }
        }

        private const string FireAsset = "scr_indep_fireworks";

        private static readonly string[] FireBursts =
        {
            "scr_indep_firework_starburst",
            "scr_indep_firework_trailburst",
            "scr_indep_firework_shotburst"
        };

        /// <summary>How often a flare goes up, and how it is aimed.</summary>
        private const int FlareMinMs = 12000;
        private const int FlareMaxMs = 34000;
        private const int FlareHoldMs = 3200;
        private const int FlareUpShare = 55;
        private const float FlareUpHigh = 70f;

        /// <summary>How often somebody sets one up, how long it takes, and where it goes.</summary>
        private const int FireMinMs = 70000;
        private const int FireMaxMs = 160000;
        private const int FireSetUpMs = 5200;
        private const float FireRingMin = 12f;
        private const float FireRingMax = 16f;

        private int _nextFlare;
        private int _flareBack;
        private Watcher _flareFrom;

        private int _nextFire;
        private int _fireAt;
        private Vector3 _fireSpot;
        private Watcher _fireMan;
        private Prop _fireProp;

        // ---- the feed -----------------------------------------------------------

        /// <summary>The block says something about it while it is on.</summary>
        private void Chatter(int now)
        {
            if (Social == null || now < _nextWord) return;

            // NOT UNTIL IT HAS BEEN GOING A WHILE. The block posting about a takeover in the
            // first minute is the block reporting something it cannot have noticed yet -- and
            // it gave the whole thing away before there was anything at the junction to see.
            // Half the night in, it is a thing people have walked past and are talking about.
            if (_startedAt == 0 || Game.GameTime - _startedAt < QuietForMs) return;

            _nextWord = now + _rng.Next(WordMinMs, WordMaxMs);

            try { Social.On(SocialEvent.Takeover); }
            catch (Exception ex) { Log.Debug("Takeover could not post: " + ex.Message); }
        }

        // ---- the law ------------------------------------------------------------

        /// <summary>
        /// Three of them, from three directions, a block out.
        ///
        /// THEY ARRIVE BEFORE ANYTHING SCATTERS. One car parked at the end of the street is a
        /// prop; three sets of lights closing from three bearings is the thing that actually
        /// empties a junction, and the gap between hearing them and seeing them is most of
        /// what makes it work. So this only sends them -- the running is in Closing(), when
        /// somebody is near enough to be worth running from.
        /// </summary>
        private void Blues()
        {
            State = TakeoverState.Scattering;
            _lastDrive = Game.GameTime + 60000;

            for (var i = 0; i < Units; i++)
            {
                try
                {
                    // Spread round the compass rather than random, so three cars cannot all
                    // come up the same street.
                    var bearing = (i / (double)Units) * Math.PI * 2d + _rng.NextDouble() * 0.6;

                    var probe = new Vector3(Middle.X + (float)Math.Cos(bearing) * LawFrom,
                                            Middle.Y + (float)Math.Sin(bearing) * LawFrom,
                                            Middle.Z);

                    var at = World.GetNextPositionOnStreet(probe, true);
                    if (at == Vector3.Zero) continue;

                    // NOT DRESSED. Everything else that turns up here is somebody's own car
                    // and gets neon, rims and a plate; a squad car with underglow and a set of
                    // deep dish is the joke landing in the wrong scene entirely.
                    var car = Make(new[] { "police3", "police", "police2" }, at, false);
                    if (car == null) continue;

                    var cop = Behind(car);

                    if (cop == null)
                    {
                        car.Delete();
                        continue;
                    }

                    Function.Call(Hash.SET_VEHICLE_SIREN, car.Handle, true);

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, cop.Handle, car.Handle,
                                  Middle.X, Middle.Y, Middle.Z, 20f, 0,
                                  car.Model.Hash, RushStyle, 6f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, cop.Handle, true);

                    _law.Add(new Law { Car = car, Cop = cop });
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover: no police car: " + ex.Message);
                }
            }

            Log.Info("Takeover: " + _law.Count + " units on the way.");
        }

        /// <summary>Whether any of them is close enough to be worth running from.</summary>
        private bool Closing()
        {
            foreach (var l in _law)
            {
                if (l.Car == null || !l.Car.Exists()) continue;
                if (l.Car.Position.DistanceTo(Middle) <= LawSeen) return true;
            }

            return false;
        }

        /// <summary>The bit everybody has been waiting for.</summary>
        private void Scatter()
        {
            foreach (var w in _crowd)
            {
                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;

                try
                {
                    // Off, so they can react to the world again. Being deaf to it all night is
                    // what kept the ring standing; turning it back on IS scattering.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, w.Man.Handle, 0, true);

                    Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                    Function.Call(Hash.TASK_SMART_FLEE_COORD, w.Man.Handle,
                                  Middle.X, Middle.Y, Middle.Z, 240f, -1, false, false);
                }
                catch
                {
                    // He runs or he does not.
                }
            }

            foreach (var p in _parked)
            {
                if (p.Car == null || !p.Car.Exists()) continue;

                try
                {
                    Function.Call(Hash.SET_HYDRAULIC_SUSPENSION_RAISE_FACTOR, p.Car.Handle, 0f);

                    if (p.Driver == null || !p.Driver.Exists()) continue;

                    // NOT A WANDER. Wander is a car pottering off at the speed limit, which is
                    // not what anybody does when the lights come round the corner. They are
                    // given somewhere to be and told to get there.
                    var off = OnRoad(200f + (float)_rng.NextDouble() * 150f);
                    if (off == Vector3.Zero) off = Middle.Around(250f);

                    Function.Call(Hash.CLEAR_PED_TASKS, p.Driver.Handle);

                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, p.Driver.Handle, 1.0f);

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, p.Driver.Handle, p.Car.Handle,
                                  off.X, off.Y, off.Z, 28f, 0, p.Car.Model.Hash, RushStyle, 15f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, p.Driver.Handle, true);
                }
                catch
                {
                    // It goes when it goes.
                }
            }

            foreach (var r in _running)
            {
                if (r.Car != null && r.Car.Exists() && r.Driver != null && r.Driver.Exists())
                {
                    Leave(r);
                }
            }

            Log.Info("Takeover: police. Everybody gone.");
        }

        // ---- making things ------------------------------------------------------

        /// <summary>
        /// One car, and not the same car as the last one.
        ///
        /// THIS WALKED THE LIST IN ORDER AND TOOK THE FIRST MODEL THAT LOADED, which meant the
        /// list was a fallback chain rather than a choice -- so every drift car at every
        /// takeover was the same model, three identical Dominators going round one junction.
        /// The list is read from a random point now, and anything already out there is skipped
        /// on the first pass, so a repeat only happens once the whole list is in use.
        /// </summary>
        private Vehicle Make(string[] names, Vector3 at, bool dress = true, bool showy = true)
        {
            var start = _rng.Next(names.Length);

            Taken();

            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < names.Length; i++)
                {
                    var name = names[(start + i) % names.Length];

                    try
                    {
                        var model = new Model(name);
                        if (!model.IsValid || !model.IsInCdImage) continue;

                        // First time round, only what nobody out there is already driving.
                        if (pass == 0 && _taken.Contains(model.Hash)) continue;

                        if (!model.Request(1200)) continue;

                        var car = World.CreateVehicle(model, at);
                        model.MarkAsNoLongerNeeded();

                        if (car == null || !car.Exists()) continue;

                        car.IsPersistent = true;

                        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);
                        Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                        Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car.Handle, true, true, false);

                        if (dress) Dress(car, showy);

                        return car;
                    }
                    catch
                    {
                        // Next name.
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Which models are out there, so the next one is a different car.
        ///
        /// Rebuilt from the live cars rather than added to and removed from. A set that is
        /// maintained by hand drifts out of step the first time something is cleaned up on a
        /// path that forgot to update it, and then the list of cars that "exist" only grows --
        /// until every model is spoken for and the whole thing quietly stops working.
        /// </summary>
        private readonly HashSet<int> _taken = new HashSet<int>();

        private void Taken()
        {
            _taken.Clear();

            try
            {
                foreach (var r in _running)
                {
                    if (r.Car != null && r.Car.Exists()) _taken.Add(r.Car.Model.Hash);
                }

                foreach (var p in _parked)
                {
                    if (p.Car != null && p.Car.Exists()) _taken.Add(p.Car.Model.Hash);
                }
            }
            catch
            {
                // A repeat is not the end of the world.
            }
        }

        /// <summary>
        /// Nobody brings a stock car to one of these.
        ///
        /// Every one of these is rolled per car, so no two arrive looking the same -- and the
        /// parts are picked out of what the MODEL actually has rather than off a fixed list of
        /// indexes: GET_NUM_VEHICLE_MODS is asked how many wheels or spoilers this particular
        /// car owns, and one of those is chosen. A hard-coded index is a part on a Dominator
        /// and nothing at all on a Futo.
        ///
        /// The mod kit goes on first. Without it every SET_VEHICLE_MOD below is a call that
        /// returns quietly having done nothing, which is the usual reason a car dressed in
        /// script comes out stock.
        ///
        /// EVERY NATIVE HERE IS NAMED, NOT NUMBERED, AND THAT IS NOT A STYLE PREFERENCE.
        /// The first version of this addressed them by raw hash, on the reasoning that a name
        /// the scripting library does not carry is a mod that will not compile while a wrong
        /// number is merely a thing that does not work. That reasoning was exactly backwards.
        /// Script Hook V does not shrug at a hash it cannot resolve -- it puts up SCRIPT HOOK V
        /// CRITICAL ERROR, FATAL: Can't find native, and takes the game with it. The try/catch
        /// around all of this cannot help, because the process is gone before any exception
        /// exists to catch. 0x487EB21CC7341E0C was the one that did it.
        ///
        /// A name that the library does not have is a build that fails on this machine, in
        /// seconds, in front of me. A number that this build does not have is somebody else's
        /// game closing mid-session. The compile error is the good failure and it was there to
        /// be had the whole time.
        /// </summary>
        private void Dress(Vehicle car, bool showy = true)
        {
            try
            {
                var h = car.Handle;

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);   // SET_VEHICLE_MOD_KIT

                // Paint. Pearl over a base, which is where the depth in a show car comes from.
                var main = Paints[_rng.Next(Paints.Length)];
                var pearl = Paints[_rng.Next(Paints.Length)];

                Function.Call(Hash.SET_VEHICLE_COLOURS, h, main, main);       // COLOURS
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, h, pearl, Rims);      // EXTRA_COLOURS
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, h, 0f);               // DIRT_LEVEL
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, h, Tints[_rng.Next(Tints.Length)]);

                // The mechanical ones, which are fixed maximums rather than a choice.
                Function.Call(Hash.SET_VEHICLE_MOD, h, 11, 3, false);  // engine
                Function.Call(Hash.SET_VEHICLE_MOD, h, 12, 2, false);  // brakes
                Function.Call(Hash.SET_VEHICLE_MOD, h, 13, 2, false);  // box
                Function.Call(Hash.SET_VEHICLE_MOD, h, 15, 3, false);  // suspension
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, 18, true);      // turbo

                // Wheels, from a random set, and whatever that set has for this car.
                Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, h, Wheels[_rng.Next(Wheels.Length)]);
                Fit(h, 23, true);

                // NO SPOILER AND NO LIVERY. Both were in here and both are the wrong kind of
                // decoration for this: a livery is a paint scheme somebody ordered from a
                // catalogue, and a big wing is a track car. These are street cars that get
                // thrown sideways at a junction on a Tuesday -- the money goes into the paint,
                // the rims and the noise, not into looking like a race entry.
                //
                // Taken OFF rather than simply not put on, because a few models ship with one
                // fitted by default and skipping the call would leave those wearing it.
                Function.Call(Hash.SET_VEHICLE_LIVERY, h, -1);
                Function.Call(Hash.SET_VEHICLE_MOD, h, 48, -1, false);
                Function.Call(Hash.SET_VEHICLE_MOD, h, 0, -1, false);

                // And the rest of the bodywork, from what this model owns.
                Fit(h, 1, false);    // front bumper
                Fit(h, 2, false);    // rear bumper
                Fit(h, 3, false);    // skirts
                Fit(h, 4, false);    // exhaust
                Fit(h, 6, false);    // grille
                Fit(h, 7, false);    // bonnet
                Fit(h, 10, false);   // roof

                // XENONS AND COLOURED SMOKE, BUT NOT ON THE ONES ACTUALLY DRIFTING.
                //
                // Both are things you only see when a car is working, which is exactly the
                // case where they are wrong here. Purple smoke off the back of a car mid-donut
                // turns a street takeover into a light show, and blue headlights sweeping the
                // crowd every time it comes round does the same job. A real one of these is
                // white smoke, standard lights and a lot of noise.
                //
                // The cars that came to WATCH keep both. They are parked, so the smoke never
                // shows anyway, and a row of xenons along the kerb at night is the look.
                if (showy)
                {
                    Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, 22, true);
                    Function.Call(Hash.SET_VEHICLE_XENON_LIGHT_COLOR_INDEX, h, _rng.Next(0, 13));

                    var smoke = Glow[_rng.Next(Glow.Length)];

                    Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, 20, true);
                    Function.Call(Hash.SET_VEHICLE_TYRE_SMOKE_COLOR, h, smoke[0], smoke[1], smoke[2]);
                }

                // NEON, on all four sides, and everybody gets it. It is underglow on a parked
                // or spinning car either way, and it is the one bit of colour that belongs.
                var neon = Neons[_rng.Next(Neons.Length)];

                for (var side = 0; side < 4; side++)
                {
                    Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, h, side, true);
                }

                Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, h, neon[0], neon[1], neon[2]);

                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, h, Plates[_rng.Next(Plates.Length)]);
            }
            catch (Exception ex)
            {
                // A stock car still does donuts.
                Log.Debug("Takeover could not dress one: " + ex.Message);
            }
        }

        /// <summary>
        /// Fit a random one of whatever this model has of that part.
        ///
        /// Asked rather than assumed. A count of zero means this car has no spoilers, and the
        /// right answer there is to leave it alone rather than to set index 0 of nothing.
        /// </summary>
        private void Fit(int car, int slot, bool custom)
        {
            try
            {
                var count = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car, slot);
                if (count <= 0) return;

                Function.Call(Hash.SET_VEHICLE_MOD, car, slot, _rng.Next(count), custom);
            }
            catch
            {
                // It goes without.
            }
        }

        /// <summary>Paints, wheel sets, tints, plates, and the colours that glow.</summary>
        private static readonly int[] Paints =
        {
            0, 1, 2, 3, 4, 12, 27, 28, 38, 49, 52, 55, 64, 70, 73, 88, 89, 92,
            111, 112, 117, 120, 125, 132, 134, 141, 142, 145, 150
        };

        private static readonly int[] Wheels = { 0, 1, 2, 5, 7, 11 };
        private static readonly int[] Tints = { 1, 2, 3, 5 };
        private const int Rims = 156;

        private static readonly int[][] Glow =
        {
            new[] { 255, 0, 60 },     new[] { 0, 200, 255 },   new[] { 140, 0, 255 },
            new[] { 0, 255, 90 },     new[] { 255, 120, 0 },   new[] { 255, 0, 200 },
            new[] { 255, 240, 0 },    new[] { 0, 90, 255 },    new[] { 255, 255, 255 }
        };

        /// <summary>
        /// And the underglow, which leans green.
        ///
        /// Its own list rather than the one above, because the two are answering different
        /// questions. The glow list is "a colour"; this one is "a colour at a takeover in
        /// Chamberlain Hills", and about a third of it is green -- four entries out of twelve,
        /// in four different greens so they do not read as the same car four times. Everything
        /// else is still in there, because a car park of nothing but green underglow is a
        /// gang meet rather than a street takeover.
        /// </summary>
        private static readonly int[][] Neons =
        {
            new[] { 0, 255, 90 },     new[] { 40, 255, 0 },    new[] { 0, 200, 60 },
            new[] { 120, 255, 40 },
            new[] { 255, 0, 60 },     new[] { 0, 200, 255 },   new[] { 140, 0, 255 },
            new[] { 255, 120, 0 },    new[] { 255, 0, 200 },   new[] { 255, 240, 0 },
            new[] { 0, 90, 255 },     new[] { 255, 255, 255 }
        };

        private static readonly string[] Plates =
        {
            "SIDEWYS", "NOGRIP", "8OS ONLY", "1 MORE", "SKIDZ", "LS 4EVA",
            "SMOKIN", "3RD GEAR", "NO TYRES", "SPIN IT", "DRIFTA", "LOUD 1"
        };

        /// <summary>
        /// Somebody at the wheel who will not panic.
        ///
        /// Every line here switches off a reaction that is correct for a driver in traffic and
        /// catastrophic at a takeover. A car that flinches at a gunshot, treats a crowd as an
        /// obstacle to escape, or takes a knock personally is a car that abandons its own donut
        /// and drives through the spectators -- which is the exact failure this exists to stop.
        /// </summary>
        private Ped Behind(Vehicle car)
        {
            try
            {
                var name = Faces[_rng.Next(Faces.Length)];

                var model = new Model(name);
                if (!model.IsValid || !model.Request(1200)) return null;

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, car.Handle,
                                                4, model.Hash, -1, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return null;

                var ped = Entity.FromHandle(handle) as Ped;
                if (ped == null || !ped.Exists()) return null;

                ped.IsPersistent = true;

                var h = ped.Handle;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, false);

                // Good enough to hold a line, calm enough not to race anybody out of it.
                Function.Call(Hash.SET_DRIVER_ABILITY, h, 1.0f);

                // Not zero. A driver on nothing does not drive calmly, it drives TIMIDLY --
                // it will not commit to a gap and waits for a completely clear road, which at a
                // junction with forty people and fifteen cars in it is a car that never moves.
                // A fifth is careful without being stuck.
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, h, 0.2f);

                // Said as ped behaviour as well as in the task, because the style belongs to
                // the TASK and these belong to the DRIVER -- a re-task that forgot the style
                // would still have somebody who goes round things rather than into them.
                Function.Call(Hash.SET_PED_STEERS_AROUND_VEHICLES, h, true);
                Function.Call(Hash.SET_PED_STEERS_AROUND_PEDS, h, true);
                Function.Call(Hash.SET_PED_STEERS_AROUND_OBJECTS, h, true);

                return ped;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not find a driver: " + ex.Message);
                return null;
            }
        }

        /// <summary>A pavement to walk in from, well away from where they are going.</summary>
        private Vector3 OnFoot(Vector3 slot)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var away = WalkFromMin + (float)_rng.NextDouble() * (WalkFromMax - WalkFromMin);
                    var at = World.GetNextPositionOnSidewalk(Middle.Around(away));

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(slot) < WalkFromMin * 0.7f) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>A road to drive in from.</summary>
        private Vector3 OnRoad(float away)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var at = World.GetNextPositionOnStreet(Middle.Around(away), true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(Middle) < DriveFromMin * 0.6f) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// The heading that points something at the middle.
        ///
        /// THE MINUS ON DX IS THE WHOLE FUNCTION. GTA headings run anticlockwise from north --
        /// 0 is north, 90 is WEST, 270 is east -- and atan2(dx, dy) runs the other way, so the
        /// version without it returned every angle mirrored about the north-south axis.
        ///
        /// That is why the ring never faced the middle however many times the heading was set.
        /// It was being set correctly, to the wrong number: anybody due north or south of the
        /// mark happened to look right, and everybody east or west of it was turned exactly the
        /// wrong way. Three attempts went into re-applying a value that was never going to be
        /// correct, which is what looking at the wrong end of a problem costs.
        ///
        /// Checked rather than reasoned about: north 0, west 90, south 180, east 270.
        /// </summary>
        private static float Facing(Vector3 from)
        {
            // Turned to face the CIRCLE rather than the middle of the junction, because the
            // circle is where the cars are and looking at the cars is the entire reason
            // anybody is stood here.
            var dx = Circle.X - from.X;
            var dy = Circle.Y - from.Y;

            return (float)((Math.Atan2(-dx, dy) * 180.0 / Math.PI + 360.0) % 360.0);
        }

        private static Vector3 Ground(Vector3 at)
        {
            try
            {
                float z;

                if (World.GetGroundHeight(new Vector3(at.X, at.Y, at.Z + 2f), out z,
                                          GetGroundHeightMode.Normal))
                {
                    return new Vector3(at.X, at.Y, z);
                }
            }
            catch
            {
                // The given height came off the road in the first place.
            }

            return at;
        }

        // ---- putting it away ----------------------------------------------------

        private void Out(Runner r)
        {
            try
            {
                if (r.Car != null && r.Car.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, false);
                    Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, r.Car.Handle, false);
                }

                Loose(r.Car, r.Driver);
            }
            catch
            {
                // It leaves either way.
            }
        }

        /// <summary>
        /// Let a car and its driver go, without leaving the car behind.
        ///
        /// THIS IS WHY THE JUNCTION FILLED UP WITH EMPTY CARS. Everything used to be handed
        /// back to the game in pairs -- IsPersistent off, MarkAsNoLongerNeeded on both -- on
        /// the reasonable assumption that a pair released together goes away together. It does
        /// not. The population manager treats a loose PED as disposable and clears it out
        /// quickly, while a loose CAR is parked scenery and sits there for as long as the
        /// player is anywhere near. So the driver went, the car stayed, and the car it stayed
        /// as was one of the ones we had just fitted with neon and a personalised plate.
        ///
        /// So the DRIVER is released and drives off, and the CAR is kept -- ours, on a list --
        /// until there is nobody in it or it is far enough away that deleting it is not
        /// something anybody sees. Whichever comes first, it goes.
        /// </summary>
        private void Loose(Vehicle car, Ped driver)
        {
            if (driver != null && driver.Exists())
            {
                try
                {
                    driver.IsPersistent = false;
                    driver.MarkAsNoLongerNeeded();
                }
                catch
                {
                }
            }

            if (car == null || !car.Exists()) return;

            _ghosts.Add(new Ghost { Car = car, Driver = driver, Since = Game.GameTime });
        }

        /// <summary>A car on its way out, and whoever was driving it.</summary>
        private sealed class Ghost
        {
            public Vehicle Car;
            public Ped Driver;
            public int Since;
        }

        private readonly List<Ghost> _ghosts = new List<Ghost>();

        /// <summary>
        /// Follow them out and tidy up behind them.
        ///
        /// Three ways off the list, and the first one is the fault this exists for: the driver
        /// has been cleaned up by the game and the car is now an ornament, so it goes at once.
        /// Otherwise it goes when it is far enough away to not be seen going, and failing both
        /// of those it goes on a timer -- because a car wedged against a wall two streets away
        /// with a driver who cannot free it would otherwise be kept for the rest of the session.
        /// </summary>
        private void Ghosts(int now)
        {
            if (_ghosts.Count == 0) return;

            Vector3 you;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                you = player.Position;
            }
            catch
            {
                return;
            }

            for (var i = _ghosts.Count - 1; i >= 0; i--)
            {
                var g = _ghosts[i];

                try
                {
                    if (g.Car == null || !g.Car.Exists())
                    {
                        _ghosts.RemoveAt(i);
                        continue;
                    }

                    var empty = g.Driver == null || !g.Driver.Exists() || !g.Driver.IsAlive
                                || !g.Driver.IsInVehicle(g.Car);

                    var away = g.Car.Position.DistanceTo(you) > GoneRange;
                    var old = now - g.Since > GhostMs;

                    if (!empty && !away && !old) continue;

                    // An abandoned car is deleted where it stands even if you are looking at
                    // it. It is a car with nobody in it that was not there ten minutes ago --
                    // there is no version of leaving it that looks better.
                    if (g.Driver != null && g.Driver.Exists() && away) g.Driver.Delete();

                    g.Car.Delete();
                    _ghosts.RemoveAt(i);
                }
                catch
                {
                    _ghosts.RemoveAt(i);
                }
            }
        }

        /// <summary>How far is far enough to go, and how long before one goes anyway.</summary>
        private const float GoneRange = 130f;
        private const int GhostMs = 180000;

        /// <summary>
        /// Turn the roads through the junction off, or put them back.
        ///
        /// A box rather than a radius, because that is the shape the native takes. Sized off
        /// the cordon so the two agree -- a car turned round at thirty metres and a road that
        /// stops existing at twenty would leave a ten metre band where traffic is routed in
        /// specifically to be sent back out.
        /// </summary>
        private void Roads(bool on)
        {
            try
            {
                var r = BlockAt;

                if (on)
                {
                    Function.Call(Hash.SET_ROADS_BACK_TO_ORIGINAL,
                                  Middle.X - r, Middle.Y - r, Middle.Z - 20f,
                                  Middle.X + r, Middle.Y + r, Middle.Z + 20f);
                }
                else
                {
                    Function.Call(Hash.SET_ROADS_IN_AREA,
                                  Middle.X - r, Middle.Y - r, Middle.Z - 20f,
                                  Middle.X + r, Middle.Y + r, Middle.Z + 20f,
                                  false, true);
                }

                Log.Info("Takeover: roads through the junction " + (on ? "restored." : "switched off."));
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not set the roads: " + ex.Message);
            }
        }

        private void Pack()
        {
            Roads(true);

            foreach (var r in _running) Out(r);
            _running.Clear();

            foreach (var w in _crowd)
            {
                try
                {
                    if (w.Man == null || !w.Man.Exists()) continue;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, false);

                    w.Man.IsPersistent = false;
                    w.Man.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Already gone.
                }
            }

            _crowd.Clear();

            foreach (var p in _parked)
            {
                try { Loose(p.Car, p.Driver); }
                catch { /* Already gone. */ }
            }

            _parked.Clear();
            _turned.Clear();

            foreach (var r in _riders)
            {
                try { Loose(r.Bike, r.Man); }
                catch { /* Already gone. */ }
            }

            _riders.Clear();

            // The helicopter is deleted rather than released. A police maverick handed back to
            // the game with its searchlight on circles the neighbourhood for the rest of the
            // night, and there is nothing in the world that would ever turn it off.
            Away();
            _heliDone = false;

            // And the firework box, which is ours and would otherwise sit in the road for the
            // rest of the session with nobody left who knows what it is.
            try { if (_fireProp != null && _fireProp.Exists()) _fireProp.Delete(); }
            catch { }

            _fireProp = null;
            _fireMan = null;
            _fireAt = 0;
            _flareFrom = null;
            _flareBack = 0;

            foreach (var l in _law)
            {
                try
                {
                    Loose(l.Car, l.Cop);
                }
                catch
                {
                    // Already gone.
                }
            }

            _law.Clear();

            _scattered = false;
            _toCome = 0;

            State = TakeoverState.None;
        }

        public void RestoreWorld()
        {
            try
            {
                foreach (var w in _crowd) { if (w.Man != null && w.Man.Exists()) w.Man.Delete(); }

                foreach (var p in _parked)
                {
                    if (p.Driver != null && p.Driver.Exists()) p.Driver.Delete();
                    if (p.Car != null && p.Car.Exists()) p.Car.Delete();
                }

                foreach (var r in _running)
                {
                    if (r.Driver != null && r.Driver.Exists()) r.Driver.Delete();
                    if (r.Car != null && r.Car.Exists()) r.Car.Delete();
                }

                foreach (var r in _riders)
                {
                    if (r.Man != null && r.Man.Exists()) r.Man.Delete();
                    if (r.Bike != null && r.Bike.Exists()) r.Bike.Delete();
                }

                if (_pilot != null && _pilot.Exists()) _pilot.Delete();
                if (_heli != null && _heli.Exists()) _heli.Delete();

                _pilot = null;
                _heli = null;
                _heliDone = false;

                foreach (var l in _law)
                {
                    if (l.Cop != null && l.Cop.Exists()) l.Cop.Delete();
                    if (l.Car != null && l.Car.Exists()) l.Car.Delete();
                }

                // Anything already on its way out goes with the rest of it. RestoreWorld is
                // the hard teardown -- a save being loaded, the mod being switched off -- and
                // leaving a list of cars we had promised to delete would be leaving exactly
                // the mess this whole thing is about.
                foreach (var g in _ghosts)
                {
                    if (g.Driver != null && g.Driver.Exists()) g.Driver.Delete();
                    if (g.Car != null && g.Car.Exists()) g.Car.Delete();
                }
            }
            catch
            {
                // Teardown.
            }

            Roads(true);

            _crowd.Clear();
            _parked.Clear();
            _turned.Clear();
            _running.Clear();
            _riders.Clear();
            _law.Clear();
            _ghosts.Clear();

            _scattered = false;

            State = TakeoverState.None;
        }
    }
}
