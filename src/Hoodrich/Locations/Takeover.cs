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

        /// <summary>The junction being used tonight. Never null -- see Junctions and Plan.</summary>
        private Junction _here = Junctions[0];

        /// <summary>Whose turn it was last. Minus one so the first takeover of a session is the first junction.</summary>
        private int _turn = -1;

        /// <summary>The junction, read off the screen while stood in the middle of it.</summary>
        private Vector3 Middle => _here.Middle;

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
        private Vector3 Circle => _here.Circle;

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
        private Vector3[] Corners => _here.Corners;

        /// <summary>How many of them stand on a corner, and how far a corner spreads.</summary>
        private const int OnCorners = 80;
        private const float CornerSpread = 4.5f;

        /// <summary>How far out the ring stands. Measured on the ground at each junction.</summary>
        private float RingAt => _here.Ring;

        /// <summary>One place a car waits its turn, and which way it points while it waits.</summary>
        private sealed class Spot
        {
            public Vector3 At;
            public float Face;
        }

        /// <summary>
        /// The spots round the junction, walked and written down.
        ///
        /// NOT A CIRCLE ANY MORE, and that is the point. The ring used to be generated -- an
        /// angle, a radius, a jitter -- which put cars in the middle of the road, half on the
        /// pavement, and nose-in at whatever angle the maths produced. A generated ring can
        /// only ever be a circle, and the junction is not a circle: it is four streets meeting
        /// at an angle, with kerbs, a corner shop and a verge.
        ///
        /// These are real kerbside places, each with the heading of a car actually parked in
        /// it. Measured rather than claimed: they sit between 19.5 and 36.6 metres from the
        /// mark -- a wider, looser ring than the first walked set, out along the streets that
        /// feed the junction rather than tight around it.
        ///
        /// THE HEADINGS ARE NOT COMPUTED AND MUST NOT BE. A car parked on a street points down
        /// the street, canted in towards whatever it has stopped to watch -- which is close to
        /// facing the middle but never exactly it, and the difference between those two is the
        /// difference between cars parked up and cars arranged. Turning them to face the mark
        /// exactly would throw away the whole reason for walking them.
        ///
        /// EVERY ONE OF THEM IS FILLED -- one car per spot, no spares, so the length of this
        /// array is the number of cars that turn up. Adding a spot adds a car.
        /// </summary>
        /// <summary>
        /// Where a car waits for its turn in the pit.
        ///
        /// THE MISSING STEP BETWEEN THE KERB AND THE MIDDLE. A car used to go straight from
        /// its parking space into the circle, which meant every turn began with a car
        /// appearing out of the wall at speed -- there was no moment where you could see whose
        /// go was next. These are the four places they sit and wait, on the edge of the
        /// circle, engine running, in front of the crowd.
        ///
        /// Walked like the kerbs and pointing the way they were walked. They sit between 17
        /// and 23 metres from the mark, which is right on the crowd's own ring -- close enough
        /// that a car sitting there is plainly part of it and not parked up.
        /// </summary>
        private Spot[] Stages => _here.Stages;

        /// <summary>
        /// How close counts as being on a marker.
        ///
        /// FIVE, DOWN FROM EIGHT. Eight metres is most of a car length either side of a
        /// walked mark, and with two of them spinning that is a show happening near the
        /// junction rather than on the places that were walked for it. Nothing is placed to
        /// achieve it -- the car still drives the whole way under its own power and is left
        /// where it stops -- it is simply not called arrived until it is actually there.
        /// </summary>
        private const float StageArrived = 5f;

        /// <summary>
        /// How far a performer may travel off its place before it is sent back.
        ///
        /// THE ONLY THING THAT INTERRUPTS A SHOW. It used to be interrupted for reaching the
        /// people as well, and before that for drifting a few metres, and each time the
        /// result was the same: a performer braking, gathering itself and re-driving its
        /// own mark, so the show was mostly cars stopping. A donut that travels is a donut,
        /// and a donut that reaches the crowd is the crowd's problem.
        ///
        /// Twelve metres is a car that has slid clean off its place rather than one working
        /// a wide loop round it -- and on tyres with two fifths of their grip the loops are
        /// wide. It still has to be wider than StageArrived or a car would be sent back to
        /// a marker it is already at, for ever.
        /// </summary>
        private const float StageLeash = 12f;

        /// <summary>
        /// How long a performer tries to reach its marker before waiting where it is.
        ///
        /// Twenty-five seconds. Long enough that a car merely held up by the crowd gets there
        /// properly, short enough that one which cannot is available for its turn rather than
        /// grinding at a kerb for the whole night while the middle stands empty.
        /// </summary>
        private const int StageGiveUpMs = 40000;

        private Spot[] Spots => _here.Spots;

        /// <summary>
        /// A junction a takeover can happen at.
        ///
        /// IT USED TO BE ONE CROSSROADS AND EVERY MARK WAS A CONSTANT. That was right while
        /// there was one, and the moment there are two it is the whole file: fifty-eight reads
        /// of Middle, twenty-one of Circle, and every one of them meaning "the one junction".
        /// So the marks moved into this and the names stayed exactly as they were -- the code
        /// that works still says Middle and Stages and Spots, and what those mean is now the
        /// junction being used tonight rather than the only junction there is.
        ///
        /// Everything in here was WALKED. Nothing is computed from anything else, for the
        /// reason the kerb headings have always given: a heading worked out from a coordinate
        /// is a car arranged, and a heading read off the ground is a car parked.
        /// </summary>
        private sealed class Junction
        {
            /// <summary>What it is called in the log and on the feed.</summary>
            public string Name = "";

            /// <summary>The centre of the event: the crowd ring, the cordon, the police.</summary>
            public Vector3 Middle;

            /// <summary>The patch of road the tyre marks are on, which is not always the same spot.</summary>
            public Vector3 Circle;

            /// <summary>Where a crowd rushing in ends up.</summary>
            public Vector3 Rush;

            /// <summary>How far out the ring of people stands here.</summary>
            public float Ring = 19f;

            /// <summary>The pavement corners people actually stand on.</summary>
            public Vector3[] Corners = new Vector3[0];

            /// <summary>The places the performers perform on. One car each, no more.</summary>
            public Spot[] Stages = new Spot[0];

            /// <summary>The kerbs the crowd's cars park on. One car per spot, so this is how many turn up.</summary>
            public Spot[] Spots = new Spot[0];

            /// <summary>A junction with nowhere to park is one that has been walked but not finished.</summary>
            public bool Ready => Spots.Length > 0 && Stages.Length > 0 && Corners.Length > 0;
        }

        private static readonly Junction[] Junctions =
        {
            new Junction
            {
                Name = "Carson",

                Middle = new Vector3(-126.840f, -1737.201f, 30.135f),

                // THREE METRES FROM THE MIDDLE, AND THE THREE METRES MATTER. The middle is
                // the centre of the event; this is the centre of the CIRCLE, which on this
                // junction is not the same spot as the geometric middle of the crossroads.
                Circle = new Vector3(-129.151f, -1735.830f, 29.531f),

                Rush = new Vector3(-128.603f, -1738.153f, 30.137f),
                Ring = 19f,

                // Seven, and they are not evenly spaced because the junction is not -- they
                // sit between thirteen and twenty-seven metres out at bearings of roughly 20,
                // 65, 148, 192, 246, 305 and 335 degrees from the circle, which is every side
                // of it. That unevenness is the point: it is the shape of the actual
                // crossroads rather than the shape of a circle drawn around it.
                Corners = new[]
                {
                    new Vector3(-137.831f, -1715.518f, 30.033f),
                    new Vector3(-120.845f, -1721.802f, 30.028f),
                    new Vector3(-111.836f, -1727.592f, 29.907f),
                    new Vector3(-108.003f, -1748.946f, 29.955f),
                    new Vector3(-123.605f, -1766.196f, 29.796f),
                    new Vector3(-136.306f, -1750.899f, 30.233f),
                    new Vector3(-149.461f, -1729.984f, 30.026f)
                },

                // THE FOUR PLACES THE PERFORMERS PERFORM ON, walked and written down. A car
                // drives to one, is set on it, and does its thing there until the police come:
                // a standing burnout or a spinning one, decided on arrival.
                Stages = new[]
                {
                    new Spot { At = new Vector3(-134.609f, -1730.354f, 29.474f), Face = 309.695f },
                    new Spot { At = new Vector3(-121.989f, -1735.803f, 29.498f), Face = 194.406f },
                    new Spot { At = new Vector3(-121.690f, -1748.387f, 29.531f), Face = 127.029f },
                    new Spot { At = new Vector3(-130.422f, -1738.134f, 29.471f), Face =  29.370f }
                },

                // WALKED AGAIN FOR 0.6.0, screenshot by screenshot, kerb by kerb: the only
                // places a car may stand at this junction. The first set had a handful on the
                // corner itself, which read as parked from the pavement and as a roadblock
                // from a windscreen.
                Spots = new[]
                {
                    new Spot { At = new Vector3(-152.228f, -1722.200f, 29.328f), Face = 229.442f },
                    new Spot { At = new Vector3(-157.573f, -1717.774f, 29.531f), Face = 229.866f },
                    new Spot { At = new Vector3(-147.068f, -1711.992f, 29.474f), Face = 235.449f },
                    new Spot { At = new Vector3(-141.470f, -1716.230f, 29.338f), Face = 245.605f },
                    new Spot { At = new Vector3(-128.397f, -1708.204f, 29.000f), Face = 138.998f },
                    new Spot { At = new Vector3(-157.613f, -1744.205f, 29.328f), Face = 322.409f },
                    new Spot { At = new Vector3(-153.508f, -1739.049f, 29.355f), Face = 323.045f },
                    new Spot { At = new Vector3(-139.477f, -1755.944f, 29.451f), Face = 303.891f },
                    new Spot { At = new Vector3(-135.275f, -1767.179f, 29.114f), Face = 298.065f },
                    new Spot { At = new Vector3(-116.476f, -1764.240f, 29.046f), Face = 253.356f },
                    new Spot { At = new Vector3(-107.876f, -1752.781f, 29.072f), Face =  91.146f },
                    new Spot { At = new Vector3(-101.038f, -1726.310f, 28.813f), Face = 111.986f },
                    new Spot { At = new Vector3(-117.682f, -1713.554f, 29.057f), Face = 145.261f },
                    new Spot { At = new Vector3(-115.624f, -1719.251f, 29.220f), Face = 139.591f }
                }
            },

            new Junction
            {
                Name = "Davis",

                // THE SECOND JUNCTION, walked the same way as the first. Two performers on
                // this one rather than four -- it is a wider crossroads with more road in the
                // middle of it and two cars working that much space read as a takeover, where
                // four read as a car park.
                //
                // The middle and the circle are the same spot here, which they were not on
                // Carson: the two marks sit either side of the centre and the patch of road
                // between them is both the middle of the junction and where the tyre marks
                // go. Its height is the pavement's; the circle's is the road's.
                Middle = new Vector3(61.823f, -1504.024f, 29.270f),
                Circle = new Vector3(61.823f, -1504.024f, 28.668f),
                Rush = new Vector3(61.823f, -1504.024f, 29.270f),

                // Five, between fourteen and twenty-two metres out at bearings of roughly
                // 111, 177, 235, 294 and 352 from the circle -- every side of it again.
                Ring = 17f,

                Corners = new[]
                {
                    new Vector3(56.913f, -1491.023f, 29.239f),
                    new Vector3(41.134f, -1502.947f, 29.272f),
                    new Vector3(53.950f, -1515.248f, 29.390f),
                    new Vector3(68.574f, -1519.440f, 29.117f),
                    new Vector3(83.993f, -1507.033f, 29.293f)
                },

                Stages = new[]
                {
                    new Spot { At = new Vector3(68.282f, -1508.017f, 28.667f), Face = 245.159f },
                    new Spot { At = new Vector3(55.365f, -1500.030f, 28.669f), Face = 226.818f }
                },

                // FOURTEEN KERBS, walked one at a time and pointing the way they were walked.
                // They sit between 13.6 and 32.2 metres out, all the way round: down both
                // sides of the four streets that feed the crossroads rather than tight against
                // it, which is where a car actually stops when it has come to watch.
                Spots = new[]
                {
                    new Spot { At = new Vector3(75.612f, -1522.826f, 28.522f), Face =  50.290f },
                    new Spot { At = new Vector3(62.959f, -1522.862f, 28.495f), Face = 319.713f },
                    new Spot { At = new Vector3(58.468f, -1528.215f, 28.494f), Face = 320.122f },
                    new Spot { At = new Vector3(54.197f, -1517.653f, 28.673f), Face = 316.971f },
                    new Spot { At = new Vector3(48.614f, -1516.245f, 28.671f), Face = 114.899f },
                    new Spot { At = new Vector3(39.048f, -1510.165f, 28.515f), Face = 147.094f },
                    new Spot { At = new Vector3(38.957f, -1492.803f, 28.553f), Face = 218.368f },
                    new Spot { At = new Vector3(44.853f, -1488.899f, 28.497f), Face =  51.709f },
                    new Spot { At = new Vector3(50.752f, -1488.642f, 28.478f), Face =  48.829f },
                    new Spot { At = new Vector3(62.414f, -1484.630f, 28.536f), Face = 173.108f },
                    new Spot { At = new Vector3(67.492f, -1491.680f, 28.630f), Face = 144.475f },
                    new Spot { At = new Vector3(84.608f, -1498.469f, 28.526f), Face = 336.745f },
                    new Spot { At = new Vector3(84.308f, -1512.855f, 28.564f), Face =  20.286f },
                    new Spot { At = new Vector3(89.233f, -1521.002f, 28.581f), Face =  43.308f }
                }
            }
        };

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
        /// <summary>
        /// The spread of circle sizes, and it is a MEASURED one now.
        ///
        /// Half a metre either side was a guess and it was far too tight -- every car drove the
        /// same circle, which is a formation rather than a takeover. Three minutes of somebody
        /// actually doing donuts on this junction came out at a median radius of 8.3 metres
        /// with a tenth-to-ninetieth spread of 2.3 to 13.5: tight ones, wide ones, and most of
        /// them in the middle. Minus three to plus four reproduces that around the setting.
        /// </summary>
        private float DriftMin { get { return SpinRadius - 3f; } }
        private float DriftMax { get { return SpinRadius + 4f; } }

        /// <summary>The ini figure, because this number has been changed by eye four times.</summary>
        private float SpinRadius
        {
            get { return _cfg == null ? 8f : _cfg.TakeoverSpinRadius; }
        }



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
        /// <summary>
        /// How far out they are put down to walk in from. Pulled in from 55-130.
        ///
        /// A hundred and thirty metres at a run is the better part of a minute, and the ring
        /// is not full until the LAST of them has done it -- which is why the crowd was taking
        /// two and a half minutes to gather. Forty to ninety is still somewhere out of sight
        /// round a corner and it halves the far end, which is the end that sets the clock.
        /// </summary>
        private const float WalkFromMin = 40f;
        private const float WalkFromMax = 90f;

        /// <summary>
        /// A block over, and that is the floor rather than a suggestion.
        ///
        /// It was ninety, which on these streets is the far side of one junction -- close
        /// enough that a car for the takeover could appear in the same shot as the takeover.
        /// The whole reason everything drives in is so that nothing is seen arriving out of
        /// nowhere, and a spawn radius that fits inside the draw distance gives that away.
        /// </summary>
        /// <summary>Where the parked cars come from. Nearer than the performers.</summary>
        private const float ParkFromMin = 90f;
        private const float ParkFromMax = 170f;

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
        /// <summary>
        /// How many are put down at once. Seven to ten.
        ///
        /// Sixty-eight people at seven a wave is ten waves, and at 2.6 seconds a wave the last
        /// of them had not been CREATED for half a minute before he even started walking.
        /// </summary>
        private const int PerWave = 10;

        /// <summary>How long the cars get before anybody sets off on foot.</summary>
        private const int CrowdLeadMs = 14000;
        private const int WaveGapMs = 1600;

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
            // NOBODY CLAPS. Cheering was a third of the ring, and fifty people applauding a
            // car is a crowd at a display rather than people on a street -- an audience, and
            // the whole point of this is that they are not one. It is gone entirely.
            //
            // FILMING IT is what actually happens, and it is the most of anything here. Half
            // the value of one of these is that everybody has their phone up.
            "WORLD_HUMAN_MOBILE_FILM_SHOCKING", "WORLD_HUMAN_MOBILE_FILM_SHOCKING",
            "WORLD_HUMAN_MOBILE_FILM_SHOCKING", "WORLD_HUMAN_MOBILE_FILM_SHOCKING",
            "WORLD_HUMAN_MOBILE_FILM_SHOCKING", "WORLD_HUMAN_MOBILE_FILM_SHOCKING",

            // Talking to whoever they came with. HANG_OUT_STREET is the loose-limbed gesturing
            // idle the game uses for somebody mid-conversation -- it reads as two people
            // talking when two of them happen to be stood together, which on a ring this
            // tightly packed is most of them.
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_HANG_OUT_STREET",

            // On a beer.
            "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_DRINKING",

            // On a cigarette, and a couple just on their phones rather than filming.
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_STAND_MOBILE_UPRIGHT", "WORLD_HUMAN_STAND_MOBILE"
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
        /// <summary>
        /// One of the cars that turns up to every takeover, whatever else does.
        ///
        /// THE FIELD USED TO BE ENTIRELY A DICE ROLL, and a field with no faces in it is a
        /// field nobody recognises. These three are the regulars: you know whose they are
        /// before they have stopped, and everything else that arrives is arriving to a scene
        /// that already has somebody in it.
        ///
        /// Each carries its own fallbacks, because a name an install has not got is not worth
        /// losing a regular over -- the drift variants are the same cars with the kit on.
        /// </summary>
        private sealed class Headliner
        {
            public string[] Models = new string[0];

            /// <summary>Its paint, or below zero for whatever Dress rolls.</summary>
            public int Paint = -1;
        }

        private static readonly Headliner[] Headliners =
        {
            // The Vectre in the set's own green, which is the one car at the junction that is
            // plainly somebody's rather than just a fast car that turned up.
            new Headliner { Models = new[] { "vectre" }, Paint = SetGreen },

            new Headliner { Models = new[] { "fr36", "driftfr36" } },
            new Headliner { Models = new[] { "gauntlet4", "driftgauntlet4" } }
        };

        /// <summary>Metallic dark green: the set's colour with flake in it. See Main.</summary>
        private const int SetGreen = 49;

        /// <summary>How many of the regulars have turned up to this one. Reset with the takeover.</summary>
        private int _headed;

        private static readonly string[] Drifters =
        {
            "driftdominator10", "driftgauntlet4", "driftchavosv6", "driftfr36", "driftremus",
            "gauntlet3", "gauntlet4", "gauntlet5",
            "dominator3", "dominator7", "dominator8",
            "dominator", "buffalo3", "sultan", "futo"
        };

        /// <summary>
        /// The cars round the ring: imports and Ubermachts.
        ///
        /// JDM AND BMW, WHICH IN THIS GAME MEANS KARIN, DINKA, ANNIS, MAIBATSU AND UBERMACHT.
        /// That is the crowd a street takeover actually draws -- a row of Skylines, Silvias
        /// and E36s on the kerb, not the muscle and the saloons that were in here before. The
        /// Ubermachts are every BMW this game has: the Sentinel is an E36, the Sentinel
        /// Classic an E30, the Cypher an M2, the Oracle and the Zion the big coupes.
        ///
        /// Deep on purpose. Nineteen or so of these are spawned at once and Make skips a name
        /// somebody is already driving on its first pass, so a short list would come out as
        /// the same four cars repeated down the street. Twenty-eight names is enough that it
        /// does not have to.
        ///
        /// Ordered best-first, and Make walks the whole list past anything an install has not
        /// got. So the Tuners cars lead where they exist and the base-game ones behind them
        /// catch a build without that DLC -- Sultan, Kuruma, Futo, Jester, Penumbra, Elegy,
        /// Warrener, Sentinel, Zion and Oracle ship with every copy of the game, which is
        /// more than enough on its own to fill twenty-three kerbs.
        /// </summary>
        private static readonly string[] Parked =
        {
            // The set's own, checked against the hashes off the vehicle list rather than
            // typed from memory -- the same names the rollers drive, so the cars on the block
            // and the cars at the meet are recognisably one set's cars.
            "sentinel5", "cavalcade3", "fq2", "rebla", "fr36", "dominator3", "dominator9",
            "gauntlet4", "ruiner4", "vigero2", "outlaw",

            // Imports.
            "zr350", "euros", "remus", "previon", "calico", "futo2", "penumbra2",
            "jester3", "rt3000", "kanjo", "kanjosj", "warrener2", "s95", "vectre",
            "sultan2", "sultanrs", "elegy2", "kuruma", "sultan", "futo", "jester",
            "penumbra", "elegy", "warrener", "asterope2",

            // Ubermacht.
            "cypher", "sentinel3", "sentinel2", "sentinel", "zion2", "zion",
            "oracle2", "oracle"
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

        /// <summary>
        /// A few. Two or three of the twenty-three, which is what "a few" means on a street
        /// this size -- they are the cars people come to look at, and a kerb full of them is
        /// a show rather than a takeover.
        /// </summary>
        private const int DonksMin = 2;
        private const int DonksMax = 3;

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
        /// <summary>
        /// And a few on juice. Two or three, the same as the donks.
        ///
        /// It was four to seven, which with three to five donks put nine of the twenty-odd
        /// cars on big wheels or hydraulics -- getting on for half a street that is supposed
        /// to be imports. Four to six of twenty-three between the two of them leaves the rest
        /// of the kerb to the Skylines.
        /// </summary>
        private const int LowsMin = 2;
        private const int LowsMax = 3;

        // ---- what is out there --------------------------------------------------

        private sealed class Watcher
        {
            public Ped Man;
            public Vector3 Slot;
            public bool There;

            /// <summary>Until when they are cheering or filming the cars, nought when they are not.</summary>
            public int Hype;

            /// <summary>Until when they are getting out of a car's way, and until when they are on a driver.</summary>
            public int Dodge;
            public int Angry;

            /// <summary>The stranger's car they went for; until when they are on the car itself; the next hit; whether from the roof.</summary>
            public Vehicle Wrecking;
            public int Wreck;
            public int NextHit;
            public bool OnRoof;

            /// <summary>Until when they are in the middle, their spot there, and how far in they have got.</summary>
            public int Rush;
            public Vector3 RushAt;
            public int RushStage;
            public int RushMove;

            /// <summary>When they were first noticed away from their spot, or nought.</summary>
            public int Away;

            /// <summary>When he stepped out of a car's way, or nought. See Step.</summary>
            public int Stepped;

            /// <summary>When his fright wears off, where it came from, and whether he has moved.</summary>
            public int Spooked;
            public Vector3 From;
            public bool Ran;

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

            /// <summary>The walked heading for this spot. Never recomputed -- see Spots.</summary>
            public float Face;

            /// <summary>What he was last told to steer for. See Toward.</summary>
            public Vector3 Aimed;

            /// <summary>When he was sent, so one that cannot get there can be put there.</summary>
            public int Sent;

            /// <summary>When he stopped on the way in, or nought while he is rolling.</summary>
            public int Stuck;

            /// <summary>When the driver may get out. Set on arrival, so he sits a beat first.</summary>
            public int OutAt;

            /// <summary>The station this car is playing, so it can be put back on when the game turns it off.</summary>
            public string Station;

            /// <summary>He is out of the car and stood with the rest of them.</summary>
            public bool Outside;

            /// <summary>What he does while he stands there. Picked once. See Mingle.</summary>
            public string Doing;

            /// <summary>He has been sent back to the car and is not in it yet. See Bail.</summary>
            public bool Bailing;

            /// <summary>The earliest he pulls out, so thirty cars do not leave on one frame.</summary>
            public int OffAt;

            public bool There;

            /// <summary>Whether the game's parking task has the wheel, and since when.</summary>
            public bool Parking;
            public int ParkedAt;

            /// <summary>How many times he has been handed a different kerb. See Settle.</summary>
            public int Moved;

            /// <summary>He gave up and drove off. Taken off the list next tick. See Settle.</summary>
            public bool Gone;

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

            public int Way;

            public int Until;

            public bool Circling;

            public bool Leaving;

            /// <summary>
            /// Which marker is HIS, for as long as he is at this takeover.
            ///
            /// Held the whole time rather than given up when he is called in, which is the
            /// change that lets him come back to it. Nobody else can be sent to a marker
            /// somebody is still using, so his place is still there when his go is over --
            /// the same reasoning the kerbs use, applied to the queue.
            /// </summary>
            public int Stage;

            /// <summary>He has been called in, so he is not waiting on his marker any more.</summary>
            public bool Called;

            /// <summary>He has reached it and is sat on it waiting.</summary>
            public bool AtStage;

            /// <summary>When he got there, so the longest wait goes first.</summary>
            public int Waited;

            /// <summary>When he stopped on the way home, or nought if he is still moving.</summary>
            public int Stuck;

            /// <summary>When the lock swaps to the other side. See Working.</summary>
            public int SwapAt;

            /// <summary>Driving back to the middle after sliding wide.</summary>
            public bool Returning;
        }

        private readonly Settings _cfg;
        private readonly Random _rng = new Random();

        private readonly List<Watcher> _crowd = new List<Watcher>();
        private readonly List<Parkee> _parked = new List<Parkee>();
        private readonly List<Runner> _running = new List<Runner>();



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

            /// <summary>How far in it has got, when that leg began, and where it is heading.</summary>
            public int Stage;
            public int Sent;
            public Vector3 Stop;

            /// <summary>When the order was last given again, and since when it has been stood still.</summary>
            public int Poked;
            public int Still;
        }

        private readonly List<Law> _law = new List<Law>();

        /// <summary>How many turn up, and how far out they start.</summary>
        private const int Units = 3;
        private const float LawFrom = 150f;

        /// <summary>Close enough for the junction to notice them.</summary>
        private const float LawSeen = 70f;

        /// <summary>
        /// How far out they stop first, how fast they come, and how far in they go after.
        ///
        /// TWO LEGS. Thirty metres is the outside of the ring: close enough that the lights
        /// are all over the junction, far enough that they are not driving through the
        /// parked cars to get there. Fourteen metres a second is fifty rather than seventy
        /// -- fast enough to read as a response, slow enough that the avoidance in the
        /// driving style has room to work. And once everybody is running, each unit creeps
        /// on to ten metres from the middle at a walking-pace seven, on the style that
        /// stops before people and cars, and the officer gets out. See Raid.
        /// </summary>
        private const float LawHold = 30f;
        private const float LawSpeed = 14f;
        private const float LawClose = 10f;
        private const float LawCreep = 9f;

        /// <summary>Near enough to a stop to count as there, and the most a leg is given.</summary>
        private const float LawThere = 6f;
        private const int LawLegMs = 25000;

        /// <summary>How long stood still counts as stuck, and how often the order is given again.</summary>
        private const int LawStuckMs = 8000;
        private const int LawPokeMs = 4000;

        /// <summary>
        /// The style for the way in: steer round cars moving and parked, round people, round
        /// objects, and stop only for a person straight in front. NOT the care style: that
        /// stops for cars as well, and the way into a junction that thirty cars are leaving
        /// is a car in front every second, so the unit sat at the edge until it gave up.
        /// </summary>
        private const int RaidStyle = 2 | 4 | 8 | 16 | 32;

        public Func<bool> Busy;

        /// <summary>Set by Main: the feed, so the block can talk about it.</summary>
        public SocialFeed Social;

        public TakeoverState State { get; private set; }

        private int _plannedFor = -1;

        /// <summary>
        /// How many nights have passed since the last one was put in the diary.
        ///
        /// Counted rather than worked out from the date. The day of the month wraps at the end
        /// of it, so "three days since the twenty-ninth" is arithmetic with a special case in
        /// it, and the special case is the bit that would be wrong. This counts the nights it
        /// actually sees instead -- and starts full, so the first night of a session is one.
        /// </summary>
        private int _nightsSince = int.MaxValue;
        private int _startsAt = -1;
        private int _endsAt;

        private int _lastTick;
        private int _lastDrive;

        private bool _scattered;
        private int _toCome;
        private int _nextWave;
        private int _nextWord;

        private const int TickMs = 700;
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

        /// <summary>
        /// Start one now, whatever the clock says.
        ///
        /// Arms a flag rather than calling Begin directly, so the takeover still starts down
        /// its ordinary path on the next tick with every check that path makes. A second
        /// entrance into a state machine is a second set of assumptions to keep in step with
        /// the first, and this one has phases, a diary and a teardown hanging off it.
        ///
        /// Returns the line to show the player. Every way this can decline is a thing they can
        /// do something about, so each one says which it was -- a button that silently does
        /// nothing is indistinguishable from a button that is broken.
        /// </summary>
        public string Force(int which)
        {
            if (!Enabled) return "Takeovers are switched off. Turn them on above.";

            if (State != TakeoverState.None) return "There is one on already.";

            if (Busy != null && Busy()) return "Not while something else is running.";

            var player = Game.Player.Character;

            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            // THE JUNCTION ASKED FOR, before the distance is measured against it -- otherwise
            // the check is against whichever one the rotation happened to leave loaded and a
            // man stood in the middle of Davis is told he is two hundred metres from Carson.
            // It also holds: what is started by hand stays where it was started, and the
            // rotation carries on from it next time.
            if (which < 0 || which >= Junctions.Length) return "No such junction.";

            if (!Junctions[which].Ready)
            {
                return Junctions[which].Name + " has not been walked all the way yet.";
            }

            _here = Junctions[which];

            var near = player.Position.DistanceTo(Middle);

            if (near > NearEnough)
            {
                return "Too far from " + _here.Name + " -- you are " + (int)near +
                       "m away and it starts within " + (int)NearEnough + "m.";
            }

            _forced = true;

            Log.Info("Takeover: started by hand on " + _here.Name + ".");

            return "Starting one on " + _here.Name + " now.";
        }

        /// <summary>
        /// What the junctions are called, in the order Force takes them.
        ///
        /// For the settings screen, which draws one button per junction rather than one
        /// button that guesses: there is no sensible way to work out which crossroads
        /// somebody meant, so it asks.
        /// </summary>
        public static string[] Places()
        {
            var names = new string[Junctions.Length];

            for (var i = 0; i < Junctions.Length; i++) names[i] = Junctions[i].Name;

            return names;
        }

        /// <summary>Somebody asked for one. Cleared the moment it begins. See Force.</summary>
        private bool _forced;

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

                // And the beam, which is a draw call and therefore only exists on the frame it
                // is made -- a spot light drawn every nine hundred milliseconds is a spot light
                // that is off for eight hundred and ninety of them.
                Beam();

                // AND THE TAP GETS TURNED DOWN. See Quieter.
                Quieter();
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
                        // ASKED FOR, OR THE CLOCK. The two checks that get skipped are the two
                        // that are about WHEN -- somebody who has just held the button down
                        // has answered "is tonight one of the nights" himself.
                        if (!_forced)
                        {
                            if (Busy != null && Busy()) return;
                            if (!Tonight()) return;
                        }

                        // The distance is NOT skipped, and Force checks it too. This one is
                        // not about when: the junction is a fixed place, and starting one
                        // three miles away is thirty-five cars and sixty people spawning
                        // somewhere nobody is stood to see them.
                        if (near > NearEnough) return;

                        _forced = false;

                        Begin(now);
                        break;

                    case TakeoverState.Running:
                        if (near > LetGo) { Pack(); return; }
                        if (OwnedCars.NowMinutes() >= _endsAt) { Blues(); return; }

                        Calm();
                        Fright(now);
                        Arriving(now);
                        TopUp(now);
                        Filling(now);
                        Wave(now);
                        Walking();
                        Parking(now);
                        Sweep(now);
                        Keep(now);
                        Working(now);
                        Chatter(now);
                        Racket(now);
                        Chopper(now);
                        Flares(now);
                        Unarm(now);
                        Firework(now);
                        Hype(now);
                        Blasting(now);
                        Crowding(now);
                        Rushes(now);
                        Riders(now);
                        break;

                    case TakeoverState.Scattering:
                        // Still calling for them. See Blues -- the first ask can come back
                        // empty-handed and that must not be the end of it.
                        MoreLaw(now);

                        // The police are driving in. Nothing runs until one of them is close
                        // enough to be worth running from.
                        if (!_scattered && Closing())
                        {
                            _scattered = true;
                            Scatter();
                        }

                        Bail(now);
                        Raid(now);
                        RidersOff(now);

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

            // A NIGHT HAS TURNED OVER. Whether there is one tonight is decided here and
            // nowhere else: _startsAt of -1 is the whole of "not tonight", and Tonight()
            // already reads it that way.
            var every = _cfg == null ? 3 : Math.Max(1, _cfg.TakeoverEveryNights);

            if (_nightsSince < int.MaxValue) _nightsSince++;

            if (_nightsSince < every)
            {
                _startsAt = -1;

                Log.Info("Takeover: none tonight -- " + _nightsSince + " of " + every +
                         " night(s) since the last.");
                return;
            }

            _nightsSince = 0;

            // AND WHICH JUNCTION: THE OTHER ONE FROM LAST TIME, decided with the night and
            // not before.
            //
            // TURNS, NOT A ROLL. A coin can come up the same three takeovers running, and
            // three in a row on one crossroads is a takeover that reads as being in one place
            // -- which is the whole thing having a second one is for. So it steps to the next
            // junction and wraps, and with two that is a swap every time.
            //
            // Only the ones walked all the way -- corners, stages and kerbs -- are in the
            // rotation, so a junction half written down is not used rather than used badly.
            var ready = new List<Junction>();
            foreach (var j in Junctions) { if (j.Ready) ready.Add(j); }

            if (ready.Count > 0)
            {
                _turn = (_turn + 1) % ready.Count;
                _here = ready[_turn];
            }

            var span = (24 - FromHour) + ToHour;
            _startsAt = (FromHour + _rng.Next(span)) % 24;

            Log.Info("Takeover: tonight's is on " + _here.Name + " at " + _startsAt +
                     ":00. One night in " + every + ". " + ready.Count + " of " +
                     Junctions.Length + " junction(s) walked.");
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

            // Nothing goes in until the kerbs are full, and this is the flag that says they
            // are not yet. Cleared HERE rather than at teardown: a takeover that ended badly
            // must not leave the next one thinking its street is already parked.
            _ringed = false;

            _topUps = 0;
            _saidTopUp = false;
            _nextTopUp = 0;

            // The regulars turn up to every one of these, so the count starts again with it.
            _headed = 0;

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
            // THE CROWD DOES NOT SET OFF YET. Wave is gated on the cars being in, and
            // Filling is what opens it -- so this is only the earliest it could ever be, not
            // when it will be. See Filling.
            _nextWave = now;
            _carsIn = false;

            // AND A NOTE OF WHAT WAS ALREADY PARKED HERE. See Sweep.
            _nextWord = now + _rng.Next(20000, 45000);


            Theme();

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
            // NOBODY WALKS IN UNTIL THE CARS ARE PARKED. The street is the thing they came to
            // stand round; a crowd that arrives at an empty junction and waits for the cars is
            // a queue, and it was the wrong way round.
            if (!_carsIn) return;

            if (_toCome <= 0 || now < _nextWave) return;

            _nextWave = now + WaveGapMs;

            var want = Math.Min(PerWave, _toCome);

            // COUNTED DOWN ONLY WHEN SOMEBODY ACTUALLY TURNED UP.
            //
            // It used to count down either way, on the reasoning that a failure is a person who
            // did not come and retrying for ever would hammer the pavement finder all night.
            // That was right while asking for a model WAITED for it -- failures were rare and
            // meant something was wrong. Asking does not wait any more, so the first wave of an
            // evening would mostly answer "not yet" and forty of the crowd would simply never
            // have existed.
            //
            // Retrying is safe because the wave is on a clock: a wave that spawns nobody costs
            // 1.6 seconds and tries again, and by then the streamer has what it was asked for.
            // The hammering the old comment worried about is bounded by that clock.
            for (var i = 0; i < want; i++)
            {
                if (Somebody()) _toCome--;
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

                    got = model.IsValid && model.IsInCdImage && Core.Models.Ready(model);
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

                // Somebody getting out of a car's way, on a driver, or in the middle, is left to it.
                if (w.Dodge > now || w.Angry > now || w.Rush != 0 || w.Wreck != 0) continue;

                // AND SO IS SOMEBODY BUYING SOMETHING OFF YOU. He walked out of the ring on
                // purpose; dragging him back to his spot mid-deal is what stopped anybody at a
                // takeover ever getting to you. He rejoins on the next pass after the deal,
                // which is this same code doing what it always does. See Dealing.Serving.
                if (Dealing.Serving.Is(w.Man)) continue;

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

                        // DEAF AGAIN NOW HE IS BACK. Somebody who has been spooked had this
                        // turned OFF so he could hear the thing that spooked him, and a man who
                        // never gets it back reacts to every bang for the rest of the night --
                        // which is a ring that empties itself one firework at a time.
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS,
                                      w.Man.Handle, true);

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

                // OUT OF THE WAY OF A CAR, AND THEN BACK. See Step.
                if (Step(w, now)) continue;

                // AND OUT OF THE WAY OF WHATEVER JUST HAPPENED. See Spook.
                if (Startled(w, now)) continue;

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

                    // A CAR LEFT IN THE CIRCLE with nobody alive at the wheel -- the driver
                    // pulled out of it by the crowd, or dead -- is in the way of the show and
                    // nobody is coming back for it. The same window as a driven one, then it
                    // goes.
                    if ((driver == null || !driver.Exists() || !driver.IsAlive)
                        && !Ours(car) && !LawCar(car) && car.Position.DistanceTo(Middle) < EatAt)
                    {
                        Eat(car, null);
                        continue;
                    }

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

                    // GOT THROUGH. It is not going to be persuaded now -- but it is not eaten
                    // on the spot either. THE CROWD GETS ITS TURN FIRST: Crowding puts the
                    // nearest few on the driver the moment a stranger's car is inside the
                    // ring, and a car deleted on the tick it crossed the line was a beating
                    // nobody saw -- the log had the two lines a second apart. See Eat.
                    if (in_ < EatAt)
                    {
                        Eat(car, driver);
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
        /// <summary>
        /// Clear every stranger out of a hundred metres, every few seconds.
        ///
        /// The cordon turns traffic round at the edge and eats what gets inside nineteen
        /// metres, and that was never going to be enough on its own: it only ever looks at
        /// cars with drivers heading in, so anything already parked on the street, anybody
        /// stood on the pavement, and every pedestrian the population manager quietly puts
        /// back stays exactly where it is. The junction ends up with our sixty people and a
        /// hundred of the city's.
        ///
        /// WHAT IS SAFE FROM IT is the important half, and one flag does nearly all of it:
        /// mission entities are skipped. Everything this mod spawns is one -- the dog, the
        /// homies, the Knowai, the dealers, the cars in this very event -- and so is anything
        /// another script owns and anything the game has a story reason to keep. What is left
        /// is ambient population, which is what this is for.
        ///
        /// On top of that, by name: the player, whatever he is sitting in, and anything with a
        /// badge. Deleting a police car mid-response is a wanted level that never resolves,
        /// and the flag would not have caught them.
        /// </summary>

        private void Sweep(int now)
        {
            if (now < _nextSweep) return;

            _nextSweep = now + SweepEveryMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var riding = player.CurrentVehicle;

            try
            {
                // NO CAR IS DELETED ANY MORE. THEY ARE TURNED ROUND.
                //
                // The delete was catching our own. Every guard on it was a guess about which
                // cars were ours -- persistent, on one of our lists, not police -- and a guess
                // that is wrong once is a spectator that vanishes on its way to a kerb it will
                // now never fill. There is no version of "delete every car near here" that is
                // safe while thirty-five of ours are driving to the same junction, so the
                // whole idea goes rather than another guard being added to it.
                //
                // A stranger's car was never the problem anyway. A car driving PAST is a
                // street. A car driving INTO the crowd is the problem, and the answer to that
                // is the one the cordon has always used at forty metres: send it back the way
                // it came. This is the same thing at a hundred, so a driver is turned before he
                // is committed to the junction rather than in the middle of it.
                foreach (var car in World.GetNearbyVehicles(Middle, SweepRange))
                {
                    if (car == null || !car.Exists()) continue;
                    if (riding != null && riding.Exists() && car.Handle == riding.Handle) continue;
                    if (Badged(car)) continue;
                    if (Ours(car)) continue;

                    // An empty car is the street. There is nobody in it to turn round, and one
                    // somebody left here is scenery, which is now simply true of every parked
                    // car rather than a list we had to keep.
                    var driver = car.Driver;

                    if (driver == null || !driver.Exists() || !driver.IsAlive) continue;
                    if (driver.Handle == player.Handle) continue;
                    if (Ours(driver)) continue;

                    Turn(car, driver, now);
                }

                // NOBODY ON FOOT IS TOUCHED, AND THAT IS A REVERSAL.
                //
                // This deleted every stranger within a hundred metres of the junction, on the
                // reasoning that the crowd should be the crowd we built rather than whoever
                // happened to be walking past. Which was true when the crowd was a dozen and
                // is not now it is sixty: a passer-by is one more person at a street meet, and
                // a street meet nobody has wandered into is a set.
                //
                // It was also the last thing in this file that deleted anything. The cars
                // stopped being deleted months ago -- there is no version of "remove every
                // stranger near here" that is safe while our own are driving in, and every
                // guard on it is another guess about which is which. A driver gets turned
                // round and walks away from it; a pedestrian was simply erased in front of
                // whoever was looking at him.
                //
                // The cordon still holds the ROAD, which is the half that mattered. Traffic is
                // turned at a hundred metres and cannot get into the ring. People can.
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover sweep: " + ex.Message);
            }
        }

        /// <summary>Anybody with a badge, who is never ours to delete.</summary>
        private static bool Badged(Ped who)
        {
            try
            {
                var type = Function.Call<int>(Hash.GET_PED_TYPE, who.Handle);

                // 6 cop, 27 swat, 29 army. The three the game hands a uniform.
                return type == 6 || type == 27 || type == 29;
            }
            catch
            {
                return true;
            }
        }

        private static bool Badged(Vehicle car)
        {
            try
            {
                var cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, car.Handle);

                // 18 emergency, 19 military. Also anything with somebody in uniform at the
                // wheel, because an unmarked car with a detective in it is still a response.
                if (cls == 18 || cls == 19) return true;

                var driver = car.Driver;

                return driver != null && driver.Exists() && Badged(driver);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Point a stranger's car back the way it came, and leave it alone for a while.
        ///
        /// AWAY FROM THE MARK RATHER THAN TO AN ADDRESS. The direction is the one he is already
        /// on -- straight out from the junction through where he is now -- so he carries on
        /// down the street he was already using instead of performing a U-turn in front of
        /// sixty people to reach a coordinate on the far side of town.
        ///
        /// On the cordon's own list, shared on purpose. It turns cars at forty metres and this
        /// turns them at a hundred, and two systems handing the same driver two destinations in
        /// the same second is a driver who sits there deciding.
        /// </summary>
        private void Turn(Vehicle car, Ped driver, int now)
        {
            int when;

            if (_turned.TryGetValue(car.Handle, out when) && now - when < TurnAgainMs) return;

            _turned[car.Handle] = now;

            try
            {
                var out_ = car.Position - Middle;
                var len = out_.Length();

                out_ = len < 0.5f ? car.ForwardVector : out_ * (1f / len);

                var to = car.Position + out_ * SendAway;

                var road = World.GetNextPositionOnStreet(to, true);
                if (road != Vector3.Zero) to = road;

                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, driver.Handle, true);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, 0.2f);

                Function.Call(Hash.CLEAR_PED_TASKS, driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              to.X, to.Y, to.Z, 14f, 0, car.Model.Hash, CareStyle, 20f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, driver.Handle, true);
            }
            catch
            {
                // He drives wherever he was going, and the cordon has another go at forty.
            }
        }

        /// <summary>How far he is sent, and how long before anything may tell him again.</summary>
        private const float SendAway = 160f;
        private const int TurnAgainMs = 20000;

        /// <summary>How far out strangers are turned round, and how often.</summary>
        private const float SweepRange = 100f;
        private const int SweepEveryMs = 4000;

        private int _nextSweep;

        /// <summary>One of ours, asked of a car rather than a driver.</summary>
        private bool Ours(Vehicle car)
        {
            foreach (var p in _parked)
            {
                if (p.Car != null && p.Car.Exists() && p.Car.Handle == car.Handle) return true;
            }

            foreach (var r in _running)
            {
                if (r.Car != null && r.Car.Exists() && r.Car.Handle == car.Handle) return true;
            }

            foreach (var g in _ghosts)
            {
                if (g.Car != null && g.Car.Exists() && g.Car.Handle == car.Handle) return true;
            }

            foreach (var r in _riders)
            {
                if (r.Bike != null && r.Bike.Exists() && r.Bike.Handle == car.Handle) return true;
            }

            if (_heli != null && _heli.Exists() && _heli.Handle == car.Handle) return true;

            return false;
        }

        /// <summary>
        /// A stranger's car inside the ring goes -- after the crowd has had its turn on it.
        /// The first sight of it starts a clock; for the length of the crowd's temper and a
        /// moment more it is left alone, and whatever is still in the circle after that --
        /// the car and its driver, or the car on its own -- is removed.
        /// </summary>
        private void Eat(Vehicle car, Ped driver)
        {
            // NOT ONE THE CROWD HAS BEEN ON. A car they went for stays where it ended up,
            // however wrecked, for the rest of the night: it is part of what happened.
            if (_beaten.Contains(car.Handle)) return;

            var now = Game.GameTime;
            int since;

            if (!_letIn.TryGetValue(car.Handle, out since))
            {
                _letIn[car.Handle] = now;
                return;
            }

            if (now - since < LetInMs) return;

            try
            {
                if (driver != null && driver.Exists()) driver.Delete();
                car.Delete();

                Log.Info("Takeover: something drove into it. Removed, after the crowd had its say.");
            }
            catch
            {
                // Next tick.
            }

            _letIn.Remove(car.Handle);
        }

        // ==================================================================
        // The car, after the driver
        // ==================================================================

        /// <summary>The cars the crowd has been on. Never removed by the sweep.</summary>
        private readonly HashSet<int> _beaten = new HashSet<int>();

        private const int WreckMs = 15000;
        private const int HitMinMs = 900;
        private const int HitSpanMs = 700;
        private const string MeleeDict = "melee@unarmed@streamed_core";
        private static readonly string[] SideHits = { "vehicle_kick", "vehicle_kick", "heavy_punch_a", "heavy_punch_b", "short_0_punch" };
        private static readonly string[] RoofHits = { "kick_close_a", "kick_close_b" };

        /// <summary>
        /// Onto the car. The first one up goes on the roof; the rest work the sides. The
        /// alarm goes off the moment the first of them lands a foot on it.
        /// </summary>
        private void StartWreck(Watcher w, Vehicle car, int now)
        {
            w.Wreck = now + WreckMs;
            w.NextHit = now + 400;
            w.Wrecking = car;

            try { Function.Call(Hash.REQUEST_ANIM_DICT, MeleeDict); }
            catch { /* then they shove it about instead */ }

            var roofTaken = false;
            foreach (var other in _crowd)
            {
                if (other != w && other.OnRoof && other.Wrecking != null && other.Wrecking.Exists()
                    && other.Wrecking.Handle == car.Handle)
                {
                    roofTaken = true;
                    break;
                }
            }

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                if (!roofTaken)
                {
                    // Up on it. The roof is the model's own top, over its middle.
                    var lo = new OutputArgument();
                    var hi = new OutputArgument();
                    Function.Call(Hash.GET_MODEL_DIMENSIONS, car.Model.Hash, lo, hi);

                    var top = hi.GetResult<Vector3>().Z;
                    var at = car.Position + new Vector3(0f, 0f, top + 0.05f);

                    Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, w.Man.Handle, at.X, at.Y, at.Z, false, false, false);
                    Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, car.Heading + 90f);
                    w.OnRoof = true;

                    try { Function.Call(Hash.START_VEHICLE_ALARM, car.Handle); }
                    catch { /* a quiet car */ }
                }
                else
                {
                    Function.Call(Hash.TASK_GO_TO_ENTITY, w.Man.Handle, car.Handle, -1, 1.8f, 2f, 1073741824, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover: could not get onto the car: " + ex.Message);
            }
        }

        /// <summary>One hit: a kick or a punch at the car, a dent where it lands, now and then a window.</summary>
        private void Hit(Watcher w, int now)
        {
            if (now < w.NextHit) return;
            w.NextHit = now + HitMinMs + _rng.Next(HitSpanMs);

            var car = w.Wrecking;
            if (car == null || !car.Exists() || w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) return;

            try
            {
                if (!w.OnRoof && w.Man.Position.DistanceTo(car.Position) > 3.2f)
                {
                    Function.Call(Hash.TASK_GO_TO_ENTITY, w.Man.Handle, car.Handle, -1, 1.8f, 2f, 1073741824, 0);
                    return;
                }

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, MeleeDict)) return;

                var clip = w.OnRoof ? RoofHits[_rng.Next(RoofHits.Length)] : SideHits[_rng.Next(SideHits.Length)];

                if (!w.OnRoof) Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, w.Man.Handle, car.Handle, 300);

                Function.Call(Hash.TASK_PLAY_ANIM, w.Man.Handle, MeleeDict, clip, 8f, -8f, 1100, 0, 0f, false, false, false);

                // The dent, where he is stood, and now and then a window with it.
                var rel = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_GIVEN_WORLD_COORDS, car.Handle,
                                                 w.Man.Position.X, w.Man.Position.Y, w.Man.Position.Z);

                Function.Call(Hash.SET_VEHICLE_DAMAGE, car.Handle, rel.X * 0.8f, rel.Y * 0.8f, w.OnRoof ? 0.6f : 0.3f,
                              w.OnRoof ? 90f : 60f, 140f, true);

                if (_rng.Next(100) < 22) Function.Call(Hash.SMASH_VEHICLE_WINDOW, car.Handle, _rng.Next(4));
            }
            catch
            {
                // Next hit.
            }
        }

        /// <summary>Whether a car is one of the law's, which Calm has no business eating.</summary>
        private bool LawCar(Vehicle car)
        {
            foreach (var l in _law)
            {
                if (l.Car != null && l.Car.Exists() && l.Car.Handle == car.Handle) return true;
            }

            return false;
        }

        /// <summary>When each stranger's car was first seen inside the ring.</summary>
        private readonly Dictionary<int, int> _letIn = new Dictionary<int, int>();

        /// <summary>How long a stranger's car is left to the crowd before it is removed.</summary>
        private const int LetInMs = AngryMs + 3000;

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

            foreach (var r in _riders)
            {
                if (r.Man != null && r.Man.Exists() && r.Man.Handle == who.Handle) return true;
            }

            // AND WHOEVER IS DRIVING A CAR OUT. Loose hands the driver back to the game, which
            // makes him non-persistent -- and the sweep deletes non-persistent strangers within
            // a hundred metres. So the man steering a car away from the junction was being
            // taken out of it halfway down the street, leaving a driverless car rolling to a
            // stop in front of everybody. He is still ours until his car is off the list.
            foreach (var g in _ghosts)
            {
                if (g.Driver != null && g.Driver.Exists() && g.Driver.Handle == who.Handle) return true;
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
            if (_pilot != null && _pilot.Exists() && _pilot.Handle == who.Handle) return true;

            return false;
        }

        // ---- the cars that came to watch ----------------------------------------

        private void Cars()
        {
            // EVERY KERB, EVERY TIME. Fourteen to twenty into twenty-three places left gaps
            // in the wall, and a gap in a row of parked cars is a hole you can see the far
            // pavement through -- which is the thing the ring exists to stop.
            var want = Spots.Length;
            var lows = _rng.Next(LowsMin, LowsMax + 1);
            var donks = _rng.Next(DonksMin, DonksMax + 1);

            // Lowriders first, then donks, then everything else -- counted out of the same
            // total rather than added on top, so turning any of them up does not quietly grow
            // the number of cars ringing the junction.
            // Shuffled, so the lowriders and the donks are on a different corner every time
            // rather than always the same three kerbs.
            var order = new List<int>();
            for (var i = 0; i < Spots.Length; i++) order.Add(i);

            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var t = order[i];
                order[i] = order[j];
                order[j] = t;
            }

            // THE MINIMUM-GAP CHECK THAT USED TO BE HERE WAS WRONG AND IS GONE.
            //
            // It skipped any spot within five metres of one already taken, on the grounds that
            // the closest walked pair is 3.4m apart and a car is longer than that. The distance
            // was right and the conclusion was not: it measured centre to centre and ignored
            // which way the cars point. Every close pair on this junction is SIDE BY SIDE along
            // a kerb -- 3.4m apart across the cars and about 0.3m along them -- so they are two
            // cars in adjacent bays, not one parked inside another. Measured across all 23:
            // seventeen pairs within seven metres, and not one of them would clip.
            //
            // So it was throwing away a perfectly good kerb to solve a problem that was not
            // there, which is exactly the gap it was asked to close.
            // NOT ALL AT ONCE. THIS QUEUES THEM.
            //
            // Thirty-five cars released on the same frame is thirty-five drivers all pathing to
            // the same junction through the same four streets, arriving in a block, and then
            // shunting each other trying to reach kerbs that are three metres apart. They are
            // not bad drivers -- they are thirty-five cars in a space that holds thirty-five
            // cars, all trying to be there at the same moment.
            //
            // Spread over a minute they arrive the way people arrive: one, then another, then
            // two together. Each has an empty kerb to aim at because the one next to it has
            // already stopped, and the street fills up rather than appearing.
            var made = 0;

            foreach (var idx in order)
            {
                var spot = Spots[idx];

                // SOMEBODY ELSE IS ALREADY PARKED ON IT, so it is not a free kerb tonight.
                //
                // This used to be handled by deleting them, and deleting is what was catching
                // our own cars -- so the kerb is simply given up instead. One car per spot is
                // the rule that matters; WHICH thirty-odd kerbs get used is not, and a takeover
                // with thirty-four cars at it and no two of them inside each other is better
                // than thirty-five with a pair sharing a parking space.
                if (Taken(spot.At)) continue;

                var kind = made < lows ? Kind.Low
                         : made < lows + donks ? Kind.Donk
                         : Kind.Plain;

                _coming.Add(new Pending { Where = spot, What = kind });

                made++;
            }

            // The order they were dealt is already shuffled, so this is the order they turn up
            // in as well -- the lowriders are not the first three to arrive every night.
            _nextCar = Game.GameTime;
        }

        /// <summary>
        /// Whether somebody who is not ours is parked on this spot.
        ///
        /// Asked of the world rather than of our own lists, because the whole point is the cars
        /// we did not put there. The player's own is included on purpose: he leaves it at the
        /// kerb, walks off to watch, and a spectator lands on top of it.
        /// </summary>
        private bool Taken(Vector3 at)
        {
            try
            {
                foreach (var car in World.GetNearbyVehicles(at, OnTheSpot))
                {
                    if (car == null || !car.Exists()) continue;
                    if (Ours(car)) continue;

                    return true;
                }
            }
            catch
            {
                // If it cannot be asked, the kerb is free.
            }

            return false;
        }

        /// <summary>How close another car has to be to count as being on a spot.</summary>
        private const float OnTheSpot = 4f;

        /// <summary>A car that has a kerb but has not been sent for yet. See Arriving.</summary>
        private sealed class Pending
        {
            public Spot Where;
            public Kind What;
        }

        private readonly List<Pending> _coming = new List<Pending>();
        private int _nextCar;

        /// <summary>
        /// Send for the next one, when it is due.
        ///
        /// The gap is worked out from the count rather than fixed, so the spread is a MINUTE
        /// whether there are thirty-five kerbs or five. Adding spots to the list lengthens the
        /// queue, not the night.
        /// </summary>
        private void Arriving(int now)
        {
            if (_coming.Count == 0 || now < _nextCar) return;

            var one = _coming[0];

            // SOMEBODY HAS PARKED ON IT SINCE. THE KERB GOES, NOT THE NIGHT.
            //
            // Cars() checks every kerb is clear when it deals them out, and then the last one
            // is not sent for until a minute later -- which is a minute in which an ambient car
            // can stop on one of them. Sending a spectator to a kerb that now has somebody
            // else's car on it is sending him somewhere he cannot go: he stops short, in a
            // lane, and everything behind him stops with him.
            //
            // Given up rather than swapped, exactly as Cars() gives one up. WHICH thirty-odd
            // kerbs get used has never mattered; one car per kerb is the rule that does.
            if (Taken(one.Where.At))
            {
                _coming.RemoveAt(0);
                _nextCar = now + RetrySoonMs;
                return;
            }

            // PUT BACK IF THE MODEL IS NOT HERE YET, and this is the half that stops the
            // non-blocking loader losing cars.
            //
            // Asking for a model used to WAIT for it, so a spawn only failed when something was
            // really wrong. It does not wait any more -- it says "not yet" and comes back --
            // which means the very first attempt at a car nobody has spawned this session will
            // usually say no. Dropping his kerb for that would leave a hole in the ring for the
            // rest of the night because the streamer was half a second behind.
            //
            // So the queue entry stays where it is and he is tried again shortly. The streamer
            // has been asked by now, so the second attempt nearly always takes.
            if (!Spectator(one.What, one.Where))
            {
                _nextCar = now + RetrySoonMs;
                return;
            }

            _coming.RemoveAt(0);

            var gap = SpreadMs / Math.Max(1, Spots.Length);

            _nextCar = now + Math.Max(500, gap);
        }

        /// <summary>
        /// Keep sending cars until the street is properly full.
        ///
        /// THE RING LEAKED CARS AT FOUR DIFFERENT POINTS AND NOTHING EVER REPLACED THEM. Cars
        /// deals out one per kerb, once, at the start of the night -- and between that moment
        /// and the ring being full a spot can be lost to a stranger already parked on it, to a
        /// stranger arriving on it before ours does, to a model that was not resident, or to a
        /// driver who could not reach it and went home rather than blocking the road. Each of
        /// those is handled correctly on its own and every one of them is a gap in the wall
        /// for the rest of the night.
        ///
        /// So the deal is not the end of it. Anything short of a full street gets another car
        /// sent for, on a slow clock, for as long as the takeover runs.
        ///
        /// COUNTED AS CLAIMS, NOT AS ARRIVALS. A car still driving in has its kerb and must not
        /// be sent a second one -- counting only what has parked would send thirty more cars
        /// during the minute the first thirty are on their way, and the junction would end up
        /// with sixty.
        ///
        /// AND A KERB THAT IS OCCUPIED IS SKIPPED RATHER THAN RETRIED FOREVER. If somebody's
        /// van is parked on it, it is not a free space this evening -- but it is asked again
        /// each pass, because vans drive away.
        /// </summary>
        private void TopUp(int now)
        {
            if (now < _nextTopUp) return;
            _nextTopUp = now + TopUpEveryMs;

            var have = _coming.Count;

            foreach (var p in _parked)
            {
                if (p.Gone) continue;
                if (p.Car == null || !p.Car.Exists()) continue;

                have++;
            }

            if (have >= Fewest) return;

            // A BUDGET, BECAUSE THIS CAN OTHERWISE RUN ALL NIGHT AND IT DID.
            //
            // The log from the night it crashed shows "1 more sent for -- 30 of 30 wanted"
            // every five seconds for minutes on end: it reached thirty, a car failed to park
            // and went home, it dropped to twenty-nine, and it sent another. Forever.
            //
            // That is a vehicle and a driver created every five seconds for three game hours,
            // on top of the performers, the police and sixty people -- and the game has a
            // finite number of each. An unbounded spawner is a crash with a delay on it.
            //
            // Fifteen replacements is more than a bad night needs and far short of what a
            // pathological one would take. Past that the ring is however full it is.
            if (_topUps >= MostTopUps)
            {
                if (!_saidTopUp)
                {
                    _saidTopUp = true;

                    Log.Info("Takeover: stopped topping the ring up after " + MostTopUps +
                             " replacements. It is as full as this junction will get tonight.");
                }

                return;
            }

            var sent = 0;

            foreach (var spot in Spots)
            {
                if (have >= Fewest) break;

                if (Claimed(spot.At, null)) continue;
                if (Taken(spot.At)) continue;

                _coming.Add(new Pending { Where = spot, What = Kind.Plain });

                have++;
                sent++;

                _topUps++;

                if (_topUps >= MostTopUps) break;
            }

            if (sent == 0) return;

            // Sent for now rather than on the original minute-long spread: the street is
            // already full of people and the point of a top-up is to close a hole in the wall
            // while it still matters.
            if (_nextCar > now) _nextCar = now;

            Log.Info("Takeover: " + sent + " more sent for -- " + have + " of " +
                     Fewest + " wanted.");
        }

        /// <summary>
        /// The fewest kerbs that should ever have a car on them.
        ///
        /// Thirty out of the walked list. Not all of them: a couple are usually under somebody
        /// else's parked car on any given night, and holding the night to a number the street
        /// cannot always give would have the top-up trying forever.
        /// </summary>
        private const int Fewest = 30;

        private int _nextTopUp;

        /// <summary>How many replacements have been sent, and the ceiling on it.</summary>
        private int _topUps;
        private bool _saidTopUp;

        private const int MostTopUps = 15;

        /// <summary>How often the street is counted. It is not a per-frame job.</summary>
        private const int TopUpEveryMs = 5000;

        /// <summary>How soon to try again for a car whose model was not loaded yet.</summary>
        private const int RetrySoonMs = 400;

        /// <summary>How long the whole street takes to fill, in milliseconds.</summary>
        private const int SpreadMs = 60000;

        /// <summary>
        /// Put the whole evening's models on the streamer's list, and wait for none of them.
        ///
        /// Models.Ready asks and returns; it is called here purely for the asking. Anything
        /// already resident answers true and costs nothing, and anything missing is being
        /// fetched by the time the first spawner wants it.
        /// </summary>
        private void Theme()
        {
            try
            {
                foreach (var set in new[] { Faces, Parked, Lows, Donks, Drifters, Badges, Sirens })
                {
                    foreach (var name in set) Core.Models.Ready(new Model(name));
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not warm its models: " + ex.Message);
            }
        }

        /// <summary>What sort of car came to watch.</summary>
        private enum Kind
        {
            Plain,
            Low,
            Donk
        }

        private bool Spectator(Kind kind, Spot spot)
        {
            try
            {
                var slot = spot.At;

                // CLOSER IN THAN A PERFORMER. Thirty-five of them have to be parked before
                // anything else can happen, so the distance they set off from is the length of
                // the whole build-up. Ninety to a hundred and seventy metres is far enough to
                // be arriving from somewhere and near enough that the last one is not the
                // reason the night starts a minute late.
                var from = OnRoad(ParkFromMin + (float)_rng.NextDouble() * (ParkFromMax - ParkFromMin));
                if (from == Vector3.Zero) return false;

                var car = Make(kind == Kind.Low ? Lows
                             : kind == Kind.Donk ? Donks
                             : Parked, from);

                // The model was not resident yet. Arriving keeps the kerb and tries again.
                if (car == null) return false;

                var driver = Behind(car);

                if (driver == null)
                {
                    car.Delete();
                    return false;
                }

                // ROUND THE OUTSIDE IF THE DIRECT LINE GOES THROUGH THE MARK. See Toward.
                var aim = Toward(from, slot);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              aim.X, aim.Y, aim.Z, ComeToKerb, 0, car.Model.Hash,
                              CareStyle, 4f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, driver.Handle, true);

                if (kind == Kind.Low) Function.Call(Hash.SET_CAN_USE_HYDRAULICS, car.Handle, true);

                _parked.Add(new Parkee
                {
                    Car = car,
                    Driver = driver,
                    Slot = slot,
                    Face = spot.Face,
                    Aimed = aim,
                    Sent = Game.GameTime,
                    Low = kind == Kind.Low,
                    Hop = _rng.NextDouble() * Math.PI * 2d,
                    Rate = 2.2 + _rng.NextDouble() * 2.6
                });

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send a spectator: " + ex.Message);

                // Not a "try again": something went wrong rather than something not being
                // ready. The kerb is given up so a broken one cannot hold the queue for ever.
                return true;
            }
        }

        private void Parking(int now)
        {
            // The ones that gave up and drove off, taken off the list a tick after they went.
            // Deliberately not removed inside Settle -- that runs from inside this loop.
            for (var i = _parked.Count - 1; i >= 0; i--)
            {
                if (_parked[i].Gone) _parked.RemoveAt(i);
            }

            foreach (var p in _parked)
            {
                // OUT TAKING HIS TURN. His slot is held and he is coming back to it, but he is
                // not a car failing to arrive at it -- without this he would be re-faced towards
                // the middle and settled in place the moment he came within range of his own
                // gap on his way past it.
                // ALREADY IN. Still worth a look, because a heading set once is a heading set
                // once: a car gets nudged by the next one arriving, shoved by somebody's donut
                // going wide, or simply settles a few degrees as the suspension takes it. None
                // of those move it far enough to notice, and every one of them leaves it parked
                // crooked for the rest of the night.
                if (p.There)
                {
                    // KNOCKED OFF ITS KERB AND INTO THE ROAD.
                    //
                    // A parked car is not a fixed object -- a donut that goes wide shoves it,
                    // and the next one shoves it again, so over a night it walks. Three of them
                    // ended up sat in the middle of the junction with their drivers still stood
                    // at the kerb they had been shunted away from.
                    //
                    // Aim already puts a nudged car back on its heading and never once looked
                    // at where it actually was, which is how something twenty metres out of
                    // place was still counted as parked and pointing the right way.
                    //
                    // Put back, not re-driven. The driver is stood on the pavement by now, so
                    // there is nobody in it to drive it -- and it is going to the same spot it
                    // is supposed to be on already.
                    // SHOVED OFF ITS KERB. Its kerb moves with it rather than the car being
                    // dragged back -- same reasoning as Settle. A car nudged three metres by a
                    // donut is still a parked car; teleporting it back is the thing that reads
                    // as broken, not the three metres.
                    if (p.Car.Exists() && p.Car.Speed < 0.5f
                        && p.Car.Position.DistanceTo(p.Slot) > StrayedFar)
                    {
                        p.Slot = p.Car.Position;
                    }

                    Mingle(p, now);
                    continue;
                }

                if (p.Car == null || !p.Car.Exists()) continue;

                // HE GETS FORTY SECONDS TO DRIVE THERE AND THEN HE IS PUT THERE.
                //
                // Every spot filled is the whole point of the walked list -- a gap in the wall
                // is a hole you can see the far pavement through, and one car that cannot find
                // its kerb should not cost the ring a space for the entire night. There are
                // plenty of ways for that to happen and none of them are worth chasing: the
                // spot is a kerbside place a car can sit but not always a place the road nodes
                // will route to, so the game drives to the nearest bit of road it knows and
                // stops, which can be metres short.
                //
                // Deliberately BEFORE the crowd arrives, which is the whole reason the order
                // of the night was changed. Placing a car is a car appearing where it was not,
                // and doing it to an empty street is very different from doing it in front of
                // sixty people.
                // THE GAME'S OWN PARKING TASK HAS THE WHEEL, and nothing below -- the nudge,
                // the re-aim, the give-up -- is allowed to take it back. See Park.
                if (p.Parking)
                {
                    Landed(p, now);
                    continue;
                }

                // STALLED ON THE WAY IN, so he is asked again. THIS is what was making them
                // teleport.
                //
                // Nothing re-tasked a spectator once he had been sent: the only re-task was
                // when Toward's answer changed, which happens once, and a car that stopped for
                // a bin lorry or lost its route simply sat there until the give-up timer fired
                // and put it on its kerb. From the pavement that is a car vanishing and
                // reappearing parked. Rolling is fine; stopped for four seconds means the route
                // ran out, and the answer to that is another route.
                Nudge(p, now);

                // HIS KERB HAS GONE AND HE IS STOOD IN THE ROAD WAITING FOR IT.
                //
                // Somebody else parked on it while he was driving in -- a stranger, or one of
                // ours that stopped short and took it. Nothing noticed until the ninety-second
                // give-up fired, and for that whole minute and a half he sat in a lane with
                // the rest of the arrivals queueing behind him.
                //
                // Asked once he has actually stopped and stayed stopped, so a car merely
                // waiting at a junction on the way in is not sent somewhere else.
                if (p.Stuck != 0 && now - p.Stuck > BlockedMs && Taken(p.Slot))
                {
                    if (Respot(p, now)) continue;
                }

                if (now - p.Sent > ParkGiveUpMs) Settle(p, now);

                // ONCE HE IS ROUND, HE COMES IN. Toward answers "the waypoint" while the
                // straight line to his kerb would cross the mark, and "the kerb itself" once it
                // would not -- so the answer changes exactly once per car, and that change is
                // the moment to re-task him.
                //
                // Gated on the answer having MOVED rather than run on a timer. Re-issuing a
                // drive order every couple of seconds is how a car ends up doing nothing at
                // all: each new task throws away the routing the last one was part way through,
                // and it never gets far enough to finish any of them.
                var want = Toward(p.Car.Position, p.Slot);

                if (want.DistanceTo(p.Aimed) > 8f)
                {
                    p.Aimed = want;

                    try
                    {
                        if (p.Driver != null && p.Driver.Exists())
                        {
                            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, p.Driver.Handle,
                                          p.Car.Handle, want.X, want.Y, want.Z, Closing(p), 0,
                                          p.Car.Model.Hash, CareStyle, 4f, true);

                            Function.Call(Hash.SET_PED_KEEP_TASK, p.Driver.Handle, true);
                        }
                    }
                    catch
                    {
                        // He will be asked again when the answer next moves.
                    }
                }

                // PARKED BY DRIVING, NOT BY BEING PUT THERE. Near enough, the driver is
                // given the game's own parking task for the walked spot and heading, once,
                // and Landed above decides when it is parked: stopped on or near the spot,
                // or out of time, wherever that is. A car set down on its mark by hand was
                // the thing that read as broken, not the half-metre it was out by.
                if (p.Car.Position.DistanceTo(p.Slot) > ParkFromRange) continue;

                Park(p, now);
            }
        }

        /// <summary>
        /// He has parked. Now he gets out and watches, like everybody else did.
        ///
        /// NOBODY DRIVES TO A STREET TAKEOVER AND THEN SITS IN THE CAR. They park it, they get
        /// out, and they stand by it with everyone else -- and thirty-five men sat behind glass
        /// in a ring of parked cars was the single most obviously wrong thing about the street.
        ///
        /// He stands WHERE HE IS, at his own door, rather than walking in to join the ring. It
        /// is what people actually do -- your car is the thing you came in and the thing you
        /// stand next to -- and it means thirty-five more men do not all set off walking across
        /// the junction at the same moment.
        ///
        /// Turned to face the circle, twice, for the same reason the crowd is: a scenario picks
        /// its own facing when it starts, so a heading set only beforehand is thrown away.
        ///
        /// The leave-vehicle task is re-issued rather than assumed. Getting out can be refused
        /// -- a door against a wall, a ped shoved mid-animation -- and a man who silently never
        /// got out is a car with somebody in it for the rest of the night.
        /// </summary>
        private void Mingle(Parkee p, int now)
        {
            if (p.Outside || p.Bailing) return;
            if (p.OutAt == 0 || now < p.OutAt) return;
            if (p.Car == null || !p.Car.Exists()) return;
            if (p.Driver == null || !p.Driver.Exists() || !p.Driver.IsAlive) return;

            try
            {
                var inside = Function.Call<bool>(Hash.IS_PED_IN_VEHICLE,
                                                 p.Driver.Handle, p.Car.Handle, false);

                if (inside)
                {
                    // Asked again every tick until it takes. Cheap, and the alternative is a
                    // man who is stuck in his seat because one attempt was refused.
                    Function.Call(Hash.TASK_LEAVE_VEHICLE, p.Driver.Handle, p.Car.Handle, 0);
                    return;
                }

                p.Outside = true;

                if (string.IsNullOrEmpty(p.Doing)) p.Doing = Watching[_rng.Next(Watching.Length)];

                var h = p.Driver.Handle;

                Function.Call(Hash.CLEAR_PED_TASKS, h);
                Function.Call(Hash.SET_ENTITY_HEADING, h, Facing(p.Driver.Position));

                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, h, p.Doing, 0, true);

                Function.Call(Hash.SET_ENTITY_HEADING, h, Facing(p.Driver.Position));

                // Deaf to the world while he stands there, the same as the ring is. Without it
                // the first burnout sends every one of them home.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
            }
            catch
            {
                // He gets out next tick, or he does not and he sits there.
            }
        }

        /// <summary>How long he sits in the parked car before opening the door.</summary>
        private const int SitAMomentMs = 1800;

        /// <summary>
        /// He could not get to his kerb, so wherever he stopped becomes his kerb.
        ///
        /// NOTHING IS MOVED AND NOTHING VANISHES. Placing him on the mark was a car appearing
        /// where it was not, which is the single thing that gives the whole event away as
        /// scenery being assembled -- and gating it on nobody looking only meant it happened
        /// behind you instead of in front of you. It is still a car teleporting.
        ///
        /// Close enough IS close enough. A spectator four metres off his mark, parked on the
        /// same street facing the same way, is indistinguishable from one that made it -- and
        /// it is a car that DROVE there, which the placed one never was.
        ///
        /// His slot moves to where he actually is, so everything downstream keeps working:
        /// the stray check measures against here now rather than dragging him back to a mark
        /// he could not reach, and Aim turns him to face the circle from where he stands
        /// instead of holding a walked heading that belongs to a different piece of road.
        /// </summary>
        /// <summary>
        /// The parking order: the game's own task, for the walked spot and heading. Mode 1
        /// is nose in, which is how every one of those spots was walked; the radius is how
        /// far away it will start the manoeuvre from.
        /// </summary>
        private void Park(Parkee p, int now)
        {
            p.Parking = true;
            p.ParkedAt = now;

            try
            {
                if (p.Driver == null || !p.Driver.Exists()) return;

                Function.Call(Hash.CLEAR_PED_TASKS, p.Driver.Handle);
                Function.Call(Hash.TASK_VEHICLE_PARK, p.Driver.Handle, p.Car.Handle,
                              p.Slot.X, p.Slot.Y, p.Slot.Z, p.Face, ParkMode, ParkRadius, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, p.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover: could not give a parking order: " + ex.Message);
            }
        }

        /// <summary>
        /// Parked, when the parking task has it stopped on or near the spot -- or when the
        /// task has had its time, wherever that leaves it. Then the brake, and the driver
        /// gets out after a moment, as before.
        /// </summary>
        private void Landed(Parkee p, int now)
        {
            var gap = p.Car.Position.DistanceTo(p.Slot);
            var still = p.Car.Speed < 0.4f;

            if (!(still && gap <= ParkedWithin) && now - p.ParkedAt < ParkTaskMs) return;

            // ON A WALKED SPOT OR NOT AT ALL.
            //
            // The parking task was allowed to run out and whatever it had managed became the
            // space. That is a car parked in a traffic lane, across a junction, or on the
            // wrong side of the road -- and the line below said so, every time, and then left
            // it there anyway. A log line describing a thing you have decided to accept is
            // not a diagnostic, it is an apology.
            //
            // The walked spots are the only places a spectator may stand. A car that finished
            // its manoeuvre somewhere else is offered another free kerb, exactly as the
            // give-up path does; if there is not one it goes home rather than becoming
            // permanent scenery in a lane. Nothing is moved to make this work.
            if (gap > ParkedWithin)
            {
                if (Respot(p, now)) return;

                Loose(p.Car, p.Driver);
                p.Gone = true;

                Log.Info("Takeover: a car finished parking " + gap.ToString("0") +
                         " m off its spot with no free kerb to move to, and left.");
                return;
            }

            p.There = true;

            try
            {
                if (p.Driver != null && p.Driver.Exists())
                {
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, p.Driver.Handle, p.Car.Handle, 1, 4000);
                }
            }
            catch
            {
                // It stops where it stops.
            }

            p.OutAt = now + SitAMomentMs;
            Blast(p);
        }

        private const float ParkFromRange = 16f;
        private const float ParkedWithin = 3.5f;
        private const int ParkTaskMs = 22000;
        private const int ParkMode = 1;
        private const float ParkRadius = 20f;

        private void Settle(Parkee p, int now)
        {
            try
            {
                // Still rolling. He may yet make it, and a car settled mid-manoeuvre is a car
                // parked across a lane.
                if (p.Car.Speed > 0.8f) return;

                // NEAR ENOUGH TO HIS KERB, AND NOT ON TOP OF SOMEBODY ELSE'S.
                //
                // A walked kerb is a kerbside place a car can sit and not always a place the
                // road nodes will route to, so the game drives to the nearest bit of road it
                // knows and stops a few metres short. That is still that kerb, and he is set
                // on it (see Snap) -- the kerb used to move to him, which made wherever the
                // game stopped him, in the road, his official place for the night.
                var gap = p.Car.Position.DistanceTo(p.Slot);

                if (gap <= SettleWithin && !Claimed(p.Car.Position, p))
                {
                    // Where it stopped is where it is parked. Not moved.
                    p.There = true;

                    p.OutAt = now + SitAMomentMs;
                    Blast(p);
                    return;
                }

                // AND THIS IS WHERE THE JAM CAME FROM.
                //
                // It used to end here: wherever he had stopped became his space, full stop.
                // But a car only reaches this at all if it is FURTHER than CarArrivedRange
                // from its kerb -- the arrival test upstairs has already said no -- so every
                // single car that settled was, by definition, not at a kerb. It was stopped in
                // a traffic lane because the kerb ahead was blocked, and it then became a
                // permanent parked car in that lane. The one behind it stopped, settled, and
                // became another. Ninety seconds apart, the whole street welded shut.
                //
                // So there are only two endings now, and parking in the road is not one of
                // them. He gets a different kerb if there is a free one near him, and if there
                // is not, he goes home. A takeover with thirty cars at it looks like a takeover.
                // One with thirty-five and a queue backed up to the boulevard does not.
                if (Respot(p, now)) return;

                // Nowhere to put him. Loose hands the driver back to the game and follows the
                // car out, so it is not left standing empty in the middle of the junction --
                // which is the other way this used to end up looking like a scrapyard.
                Loose(p.Car, p.Driver);

                p.Gone = true;

                Log.Info("Takeover: a car with nowhere to park left instead of blocking the road.");
            }
            catch
            {
                // Asked again next tick.
            }
        }

        /// <summary>
        /// Is this ground already somebody's?
        ///
        /// ASKED OF THE CLAIM, NOT OF THE CAR. A spectator on his way in owns his kerb from
        /// the moment he is sent for -- that is the whole point of walking them and handing
        /// them out one each -- so a car that stops on it has taken a space that is spoken
        /// for, whether or not its owner has arrived yet.
        ///
        /// Two and a half metres, because the closest walked pair on this junction is three
        /// and a half apart and those two are legitimate neighbours along one kerb. Anything
        /// closer than that is not a neighbour, it is the same parking space twice.
        /// </summary>
        private bool Claimed(Vector3 at, Parkee not)
        {
            foreach (var p in _parked)
            {
                if (p == not || p.Gone) continue;
                if (p.Car == null || !p.Car.Exists()) continue;

                if (p.Slot.DistanceTo(at) < KerbApart) return true;
            }

            // The ones still queued have kerbs too. They have not been built yet, so nothing
            // else in the world knows about them.
            foreach (var c in _coming)
            {
                if (c.Where != null && c.Where.At.DistanceTo(at) < KerbApart) return true;
            }

            return false;
        }

        /// <summary>
        /// The nearest walked kerb to a stuck car that nobody has and nothing is standing on.
        /// </summary>
        private Spot FreeKerb(Vector3 near, Parkee not)
        {
            Spot best = null;
            var bestGap = ReSpotRange;

            foreach (var spot in Spots)
            {
                var gap = spot.At.DistanceTo(near);

                if (gap > bestGap) continue;
                if (Claimed(spot.At, not)) continue;
                if (Taken(spot.At)) continue;

                best = spot;
                bestGap = gap;
            }

            return best;
        }

        /// <summary>
        /// Send him looking for another kerb, if there is one and he has not used up his goes.
        ///
        /// LIFTED OUT OF Settle SO IT CAN BE ASKED FOR EARLY. It was the give-up path and
        /// nothing else could reach it -- so a car whose kerb had been taken by somebody else
        /// stood in the road for the full ninety seconds of the give-up timer before anybody
        /// asked whether there was anywhere else to go. Ninety seconds is most of the arrival
        /// window, and the cars behind it were queueing the whole time. That is the jam.
        /// </summary>
        private bool Respot(Parkee p, int now)
        {
            if (p.Moved >= MoveOnTimes) return false;

            var other = FreeKerb(p.Car.Position, p);

            if (other == null) return false;

            p.Moved++;

            p.Slot = other.At;
            p.Face = other.Face;
            p.Parking = false;

            p.Sent = now;
            p.Stuck = 0;

            var want = Toward(p.Car.Position, p.Slot);

            p.Aimed = want;

            try
            {
                if (p.Driver != null && p.Driver.Exists())
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, p.Driver.Handle);

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, p.Driver.Handle,
                                  p.Car.Handle, want.X, want.Y, want.Z, Closing(p), 0,
                                  p.Car.Model.Hash, CareStyle, 4f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, p.Driver.Handle, true);
                }
            }
            catch
            {
                // He is asked again by the give-up timer.
            }

            Log.Debug("Takeover: a car was sent to a different kerb.");
            return true;
        }

        /// <summary>
        /// How far short of his kerb still counts as being at it.
        ///
        /// FIVE, DOWN FROM ELEVEN, AND THIS IS THE ONE THAT WAS PUTTING CARS IN THE ROAD.
        /// Eleven metres was chosen as slack for the road nodes not routing exactly to a
        /// kerbside coordinate, and eleven metres from a kerb is not slack -- it is the far
        /// side of a traffic lane. Anything that stopped that far out was accepted as parked
        /// and stayed there for the night, which is a queue of cars down the middle of the
        /// junction with the mod insisting they are all in spaces.
        ///
        /// Five is close enough to a walked kerb that a car sitting there is at it, and short
        /// enough that anything else goes and finds a real one.
        /// </summary>
        private const float SettleWithin = 5f;

        /// <summary>Closer than this to somebody else's kerb is the same space twice.</summary>
        private const float KerbApart = 2.5f;

        /// <summary>How far a stuck car will look for a different kerb, and how often it may.</summary>
        private const float ReSpotRange = 35f;
        /// <summary>
        /// How many times a car may be sent to a different kerb before it goes home.
        ///
        /// Four rather than two. The cheap outcome is a car driving another twenty metres; the
        /// expensive one is a gap in the ring for the rest of the night, and now that the
        /// re-spot can be asked for the moment a kerb is lost rather than ninety seconds later
        /// there is time in the night to use them.
        /// </summary>
        private const int MoveOnTimes = 4;

        /// <summary>
        /// Ask a stopped spectator for its route again.
        ///
        /// The same patience the performers get on their way to a marker, and for the same
        /// reason: a car in traffic near a junction full of people is stopped most of the time,
        /// so being stopped is not a fault -- being stopped for SECONDS is.
        /// </summary>
        private void Nudge(Parkee p, int now)
        {
            try
            {
                if (p.Car.Speed > 0.6f)
                {
                    p.Stuck = 0;
                    return;
                }

                if (p.Stuck == 0)
                {
                    p.Stuck = now;
                    return;
                }

                if (now - p.Stuck < BlockedMs) return;

                p.Stuck = now;

                if (p.Driver == null || !p.Driver.Exists()) return;

                var want = Toward(p.Car.Position, p.Slot);

                p.Aimed = want;

                Function.Call(Hash.CLEAR_PED_TASKS, p.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, p.Driver.Handle, p.Car.Handle,
                              want.X, want.Y, want.Z, Closing(p), 0, p.Car.Model.Hash,
                              CareStyle, 4f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, p.Driver.Handle, true);
            }
            catch
            {
                // He is asked again in a few seconds.
            }
        }

        /// <summary>
        /// How fast he should be going, given how close he is to his kerb.
        ///
        /// FOURTEEN METRES A SECOND IS FIFTY AN HOUR, and it was the speed for the whole run --
        /// including the last twenty metres, which are through a junction lined with parked
        /// cars and people, towards a gap the width of a car. Most of the crashing is that: a
        /// car arriving at its space at the speed it left the main road.
        ///
        /// So the approach has two halves. Out on the road he moves at an ordinary speed, and
        /// inside the last stretch he is down to a crawl -- which is also what makes the
        /// avoidance work at all, because steering round something is a manoeuvre that needs
        /// room and time and he has neither at fifty.
        /// </summary>
        private static float Closing(Parkee p)
        {
            try
            {
                var gap = p.Car.Position.DistanceTo(p.Slot);

                if (gap > SlowFrom) return ComeToKerb;
                if (gap < CrawlWithin) return KerbCrawl;

                var t = (gap - CrawlWithin) / (SlowFrom - CrawlWithin);

                return KerbCrawl + (ComeToKerb - KerbCrawl) * t;
            }
            catch
            {
                return KerbCrawl;
            }
        }

        /// <summary>The speed out on the road, and the speed in among the parked cars.</summary>
        private const float ComeToKerb = 9f;
        private const float KerbCrawl = 3.5f;

        /// <summary>Where he starts easing off, and where he is fully down to walking pace.</summary>
        private const float SlowFrom = 35f;
        private const float CrawlWithin = 8f;

        /// <summary>How far a parked car may be shoved off its kerb before the kerb moves.</summary>
        private const float StrayedFar = 6f;

        /// <summary>How long a spectator drives at its kerb before it settles for near enough.</summary>
        private const int ParkGiveUpMs = 90000;


        /// <summary>
        /// Somebody stood where a car is trying to get past.
        ///
        /// THE CARS ALREADY LIFT OFF FOR HIM AND THAT IS ONLY HALF OF IT. A driver easing out
        /// of a donut because somebody is in the arc stops the man being run over; it does not
        /// stop him standing in the road. Two things that both give way is a stand-off -- one
        /// of them has to move, and the one with legs is the obvious candidate.
        ///
        /// THREE AND A HALF METRES, STRAIGHT AWAY FROM THE CAR, at a run. Not fleeing: a flee
        /// task sends him off down the street and he is gone for the night. This is a man
        /// taking three steps back and then standing there again, which is what people at
        /// these actually do.
        ///
        /// He goes back the moment nothing is near, through the same path a knock uses -- his
        /// spot, his own scenario, his own facing. Nothing new had to be written for the
        /// returning half; it was already there for men who had been run over, and this is the
        /// same man in a better mood.
        ///
        /// Returns true when it has taken charge of him this tick, so the checks below leave
        /// him alone -- a man deliberately stood off his mark must not also be read as a man
        /// who has strayed off it.
        /// </summary>
        private bool Step(Watcher w, int now)
        {
            var car = Coming(w.Man.Position);

            if (car != null)
            {
                // Already moving out of the way. Let him finish.
                if (w.Stepped != 0) return true;

                w.Stepped = now;

                try
                {
                    var away = w.Man.Position - car.Position;
                    var len = away.Length();

                    // Dead level with it: any direction that is not into the car will do.
                    away = len < 0.5f ? w.Man.ForwardVector : away * (1f / len);

                    var to = w.Man.Position + away * StepBack;

                    Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                    Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, w.Man.Handle,
                                  to.X, to.Y, to.Z, 2.0f, 3000, 0f, 0.2f);
                }
                catch
                {
                    w.Stepped = 0;
                }

                return true;
            }

            if (w.Stepped == 0) return false;

            // NOTHING NEAR HIM NOW, but not the instant it passes. Turning round while the
            // back end is still going by is how he ends up under it.
            if (now - w.Stepped < StepBackMs) return true;

            w.Stepped = 0;
            w.There = false;
            w.Away = 0;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                              w.Slot.X, w.Slot.Y, w.Slot.Z, 1.6f, -1, 1.0f, true, 0f);

                Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
            }
            catch
            {
                // He finds his own way back, or the stray check has another go at him.
            }

            return true;
        }

        /// <summary>
        /// One of our cars, moving, close enough to be worth stepping away from.
        ///
        /// Ours only. The cordon deals with strangers and the sweep deletes them, so a car
        /// that is not on one of these two lists is not going to be there long enough to be
        /// worth reacting to.
        ///
        /// MOVING, which is the whole test. Thirty-five of them are parked within a few metres
        /// of somebody, and a crowd that backs away from parked cars is a crowd stood in the
        /// middle of the road.
        /// </summary>
        private Vehicle Coming(Vector3 at)
        {
            foreach (var r in _running)
            {
                if (r.Car == null || !r.Car.Exists()) continue;
                if (r.Car.Speed < MovingAt) continue;
                if (r.Car.Position.DistanceTo(at) < StepAt) return r.Car;
            }

            // And the spectators still driving to their kerbs, which is most of the driving
            // anybody does through this crowd all night.
            foreach (var p in _parked)
            {
                if (p.There) continue;
                if (p.Car == null || !p.Car.Exists()) continue;
                if (p.Car.Speed < MovingAt) continue;
                if (p.Car.Position.DistanceTo(at) < StepAt) return p.Car;
            }

            return null;
        }

        /// <summary>How close a moving car gets before somebody moves, and how far they move.</summary>
        private const float StepAt = 7f;
        private const float StepBack = 3.5f;
        private const float MovingAt = 2.5f;

        /// <summary>How long after the car has gone before he walks back to his spot.</summary>
        private const int StepBackMs = 2000;

        /// <summary>
        /// Something happened, and everybody near it moves.
        ///
        /// TWO THINGS SET IT OFF and they are the two you actually see at one of these: a shot,
        /// and somebody going under a car. Both are read from the world rather than from our own
        /// bookkeeping -- IS_BULLET_IN_AREA does not care whose gun it was, and a man on the
        /// floor is a man on the floor whoever put him there.
        ///
        /// ONE SCARE AT A TIME, held with a position. Everybody near enough reacts to the same
        /// event from the same place, which is what makes a crowd move as a crowd -- fifty
        /// people each deciding separately produces fifty people milling, which is what a crowd
        /// never does.
        /// </summary>
        private void Fright(int now)
        {
            if (_crowd.Count == 0) return;
            if (now < _nextFright) return;

            _nextFright = now + FrightGapMs;

            try
            {
                // SOMEBODY ON THE FLOOR, and it is checked first because it is the bigger of
                // the two. A car has just been through where people were standing.
                foreach (var w in _crowd)
                {
                    if (w.Man == null || !w.Man.Exists()) continue;
                    if (w.Man.IsAlive && !Function.Call<bool>(Hash.IS_PED_RAGDOLL, w.Man.Handle)) continue;

                    Scare(w.Man.Position, now, RunOverRange, RunOverMs);
                    return;
                }

                // OR A SHOT. Radius rather than a source, because from the pavement a gunshot
                // is a noise and a direction and nothing else.
                var shot = Function.Call<bool>(Hash.IS_BULLET_IN_AREA,
                                               Middle.X, Middle.Y, Middle.Z, ShotHeard, true);

                if (shot) Scare(Middle, now, ShotHeard, ShotMs);
            }
            catch
            {
                // Nobody jumps this time.
            }
        }

        /// <summary>Mark everybody near a thing as having seen it.</summary>
        private void Scare(Vector3 at, int now, float range, int hold)
        {
            foreach (var w in _crowd)
            {
                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;
                if (w.Spooked != 0) continue;
                if (w.Man.Position.DistanceTo(at) > range) continue;

                w.Spooked = now + hold;
                w.From = at;
            }
        }

        /// <summary>
        /// One man reacting, and then going back to what he was doing.
        ///
        /// HE GOES BACK, and that is the half that makes this usable. A crowd that scatters at
        /// the first bang is a takeover that ends itself thirty seconds in -- the whole ring is
        /// deaf to the world on purpose for exactly that reason. This turns that off for a few
        /// seconds, lets him have his reaction, and turns it back on when he returns.
        ///
        /// AWAY FROM IT rather than a flee task. A flee sends him off down the street and he is
        /// gone for the night; this is a man taking several quick steps back and stopping,
        /// which is what people at one of these actually do -- they move, and then they turn
        /// round and look at it.
        ///
        /// Returned through the same path a knock already uses: There is cleared, so he walks
        /// to his spot, faces the circle and starts his own scenario again. Nothing new had to
        /// be written for the coming-back half.
        /// </summary>
        private bool Startled(Watcher w, int now)
        {
            if (w.Spooked == 0) return false;

            if (now < w.Spooked)
            {
                if (w.Ran) return true;

                w.Ran = true;

                try
                {
                    var away = w.Man.Position - w.From;
                    var len = away.Length();

                    away = len < 0.5f ? w.Man.ForwardVector : away * (1f / len);

                    var to = w.Man.Position + away * BackAway;

                    // Let him hear the world for as long as this lasts, or the game will not
                    // let him break out of his scenario to move at all.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, false);

                    Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                    Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, w.Man.Handle,
                                  to.X, to.Y, to.Z, 2.0f, 4000, 0f, 0.3f);
                }
                catch
                {
                    w.Spooked = 0;
                    w.Ran = false;
                }

                return true;
            }

            w.Spooked = 0;
            w.Ran = false;
            w.There = false;
            w.Away = 0;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                              w.Slot.X, w.Slot.Y, w.Slot.Z, 1.8f, -1, 1.0f, true, 0f);

                Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
            }
            catch
            {
                // The stray check has another go at him.
            }

            return true;
        }

        /// <summary>How far a fright carries, and how long people stay off their mark for it.</summary>
        private const float ShotHeard = 30f;
        private const int ShotMs = 4500;

        private const float RunOverRange = 14f;
        private const int RunOverMs = 6000;

        /// <summary>How far back they go, and how often the world is checked for a fright.</summary>
        private const float BackAway = 5f;
        private const int FrightGapMs = 1200;

        private int _nextFright;

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

                    // PER WHEEL. The native wants (vehicle, wheel, factor) and was being given
                    // (vehicle, factor) -- a wheel index made of a float's bits, and no hop.
                    for (var wheel = 0; wheel < 4; wheel++)
                    {
                        Function.Call(Hash.SET_HYDRAULIC_SUSPENSION_RAISE_FACTOR,
                                      p.Car.Handle, wheel, (float)(s * s));
                    }
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

                // HIS GO IS OVER AND HE IS GOING BACK TO HIS MARKER.
                //
                // Not off the map any more. He parks up where he was waiting, somebody else is
                // called in, and he is in the queue again -- which is what taking turns looks
                // like from the pavement, and it means the ring of waiting cars stays full
                // instead of emptying one car per turn.
                if (r.Leaving)
                {
                    if (r.Stage < 0)
                    {
                        // Spawned straight into the pit with no marker of his own. Off the map
                        // as before -- there is nowhere to send him.
                        if (r.Car.Position.DistanceTo(Middle) < Ring + 14f) continue;

                        Out(r);
                        _running.RemoveAt(i);
                        continue;
                    }

                    var bay = Stages[r.Stage];

                    if (r.Car.Position.DistanceTo(bay.At) > StageArrived)
                    {
                        Hold(r, now, bay.At);
                        continue;
                    }

                    // Back on his mark, and back in the queue. Leaving and Called both clear,
                    // AtStage false so the waiting branch settles him again from the top --
                    // brake, face the walked heading, hold it until he is called.
                    r.Leaving = false;
                    r.Called = false;
                    r.AtStage = false;
                    r.Circling = false;
                    r.Stuck = 0;

                    continue;
                }

                // WAITING HIS TURN. He is not in the pit and is not trying to be.
                if (r.Stage >= 0)
                {
                    var bay = Stages[r.Stage];

                    if (!r.AtStage)
                    {
                        if (r.Car.Position.DistanceTo(bay.At) > StageArrived)
                        {
                            // HE WAITS WHERE HE IS RATHER THAN NOT AT ALL, and this is why the
                            // middle stayed empty all night.
                            //
                            // Turn only ever calls in a car that has ARRIVED at a marker, and
                            // Queued -- the thing that decides whether to fetch more -- only
                            // asks whether a car has been GIVEN one. So four performers with
                            // markers, none of whom could reach one, read as a full queue to
                            // the fetcher and as nobody at all to the caller. The log said it
                            // exactly: "4 performers, 3 waiting at markers", and an empty pit
                            // between them.
                            //
                            // Six metres from a walked marker on a junction with thirty parked
                            // cars round it is not always reachable, and the marker was never
                            // the point. It is somewhere to sit until called, and a man sat
                            // fifteen metres from it is just as available.
                            if (now - r.Sent < StageGiveUpMs)
                            {
                                // Same patience as a car going home: he is driving round the
                                // edge of a crowd, so being stopped is normal and asking again
                                // is the answer rather than forcing through.
                                Hold(r, now, bay.At);
                                continue;
                            }

                            Log.Info("Takeover: a performer could not reach its marker and " +
                                     "waits where it stopped.");
                        }

                        // IT DRIVES IN. It used to be teleported onto the mark and turned to
                        // face the walked heading, which put it exactly where it belonged and
                        // looked exactly like what it was: a car that was somewhere else on
                        // the previous frame. It has driven the whole way here under its own
                        // power, so it is left where it stopped and starts from there.
                        //
                        // Nothing is lost by that. The mark is where the show HAPPENS, not a
                        // parking bay, and a car going round on the spot is going round on the
                        // spot wherever within a few metres it came to rest. The leash below
                        // is what keeps it near the mark from then on.
                        r.AtStage = true;
                        r.Waited = now;

                        try
                        {
                            Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                            // THE SHOW. Every one of them spins; the only thing decided here
                            // is which way round. It used to be a coin flip against a standing
                            // burnout, and with four places that meant two cars sat still --
                            // and on a bad flip, three. A takeover is the cars going round.
                            // Kept up in bursts below for as long as the car is on its place.
                            r.Way = _rng.Next(2) == 0 ? 1 : -1;
                            Reckless(r, true);
                            Show(r, now);
                        }
                        catch
                        {
                            // He waits where he stopped.
                        }
                    }
                    else if (r.Car.Position.DistanceTo(bay.At) > StageLeash)
                    {
                        // IT HAS SLID OFF ITS MARK, and only that. It used to be interrupted
                        // for reaching the people too -- brake, gather itself, sit until the
                        // hold noticed it had stopped, drive back, start again -- and on a
                        // ring of fifty that was a show made of stops. Nothing stops it now.
                        // Anybody in the way is hit; getting out of it is the crowd's job.
                        // This is the one case left, a car that has travelled clean off its
                        // place.
                        //
                        // STRAIGHT BACK, STILL SIDEWAYS. No brake first, and the route is
                        // issued here rather than left for the hold, which only asks after
                        // a car has sat still for four seconds. On the tyres it has: this is
                        // a correction inside his go, not the end of it. AtStage is dropped
                        // and the clock restarted, which hands it to the arrival branch
                        // above -- onto the mark, and the show again.
                        try
                        {
                            Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);
                            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Driver.Handle,
                                          r.Car.Handle, bay.At.X, bay.At.Y, bay.At.Z,
                                          12f, 0, r.Car.Model.Hash, RushStyle, 3f, true);
                        }
                        catch
                        {
                            // The hold asks again in a few seconds.
                        }

                        r.AtStage = false;
                        r.Sent = now;
                        r.Stuck = 0;
                        r.NextAction = 0;
                    }
                    else if (now >= r.NextAction)
                    {
                        // A temp action expires; the show is topped up before it does.
                        Show(r, now);
                    }

                    continue;
                }

                // On the way in. Close enough to its circle and it takes over by hand.
                if (!r.Circling)
                {
                    var wants = r.Radius + 6f;
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

                    r.Until = now + _rng.Next(24000, 52000);

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
                    r.SwapAt = now + SettleMs + SwapMs;
                    r.Returning = false;

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
                        Slick(r.Car, true);
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

            // NEARLY ALL OF THEM GO ROUND, AND NOW AND THEN SOMEBODY SITS STILL.
            //
            // It used to be the other way about: there was always exactly one car on the mark
            // holding a stationary burnout, and it was the first thing sent for. Which made the
            // static one the centrepiece of every takeover, when it should be the thing that
            // happens occasionally in among a junction full of cars going round.
            //
            // So the mark is now a roll rather than a rule, taken on the same clock as the
            // count so it holds for a while instead of flickering. When nobody has it, every
            // car working is doing circles -- which is what a takeover mostly is.
            //
            // Counted separately from the round-the-outside cars all the same: the middle is a
            // PLACE, and letting the two come out of one pool means the mark stands empty
            // whenever the circle happens to be busy.
            // NOBODY SKIDS UNTIL THE STREET IS PARKED. See Ringed.
            if (!Ringed()) return;

            var round = 0;

            foreach (var r in _running)
            {
                if (r.Leaving) continue;

                // Waiting his turn is not working. Counting the queue as though it were in
                // the pit is how the pit ends up empty with four cars sat watching it.
                if (r.Stage >= 0) continue;

                round++;
            }

            // KEEP THE MARKERS OCCUPIED. Somebody should always be sat ready, so the next turn
            // starts with a car that is already there rather than one that has to be fetched
            // from a kerb first -- which was the old gap between one car finishing and the
            // next arriving.
            while (Queued() < Stages.Length)
            {
                if (!In()) break;
            }

            // AN EMPTY PIT IS THE ONE FAILURE THIS FILE CANNOT SEE.
            //
            // Everything between a takeover starting and a car going sideways in the middle of
            // it is a chain of quiet falses: In returns false with no stage free, no road
            // point, no model or no driver; Turn returns false with nobody sat at a marker.
            // Not one of them says anything, so a night where the middle stayed empty produced
            // a log identical to one where it did not -- which is exactly the night that
            // happened, twice.
            //
            // So it says where the chain broke, once every fifteen seconds and only while
            // there is genuinely nothing happening in the middle. Three numbers is enough to
            // name the link: no runners at all is In failing, runners but none waiting is them
            // not reaching a marker, and runners waiting with nothing in the pit is Turn.

            // Only if this stretch of the night is having one. An existing static burnout is
            // left to finish rather than pulled off the mark the moment the roll changes --
            // his go is his go, and a car that vanishes mid-burnout is worse than one that
            // stays a minute longer than the dice wanted.

            // TWO TO FOUR WORKING AT ONCE, and never fewer than two.
            //
            // The count is re-rolled on a clock rather than every tick. Rolling it every tick
            // meant the target flickered between three and four several times a second, so a
            // car was constantly being sent for and then not needed -- which is how you get
            // four cars queueing to enter a circle that wants three.
            if (now >= _reroll)
            {
                _reroll = now + RerollMs;

                // ONE CAR IN THE MIDDLE. Not two to four.
                //
                // Three cars sliding round the same circle is three cars fighting for the same
                // twenty metres -- they clip each other, they break each other's line, and no
                // single one of them is ever the thing you are watching. A takeover is people
                // taking turns: somebody goes in, everybody watches HIM, he comes out, somebody
                // else goes in. The waiting is the format, not dead time.
                _want = 1;

                _wantMark = _rng.Next(100) < MarkChance;
            }

            // WHATEVER IS NOT ON THE MARK IS GOING ROUND, AND WITH ONE CAR THAT IS EITHER OR.
            //
            // The clamp that used to be here forced at least one car into the circle, which was
            // right while the count was two to four and is exactly wrong now: on a night the
            // roll wants a static burnout, it would have put a car on the mark AND one round
            // it, which is two cars in a middle that is supposed to hold one.
            var want = _wantMark ? _want - 1 : _want;

            while (round < want)
            {
                if (!Turn(false, now)) break;
                round++;
            }
        }

        /// <summary>
        /// The cars are in, or near enough. Lets the crowd set off.
        ///
        /// NINE IN TEN RATHER THAN ALL OF THEM, because the last one is always the one stuck
        /// behind a bin lorry and holding the whole night on it is holding it on the worst
        /// case. The stragglers keep coming while the crowd walks in, and Parking puts any
        /// that genuinely cannot make it onto their spot itself.
        /// </summary>
        private void Filling(int now)
        {
            if (_carsIn) return;

            var there = 0;

            foreach (var p in _parked)
            {
                if (p.There) there++;
            }

            // MEASURED AGAINST THE NUMBER OF KERBS, NOT AGAINST HOW MANY HAVE BEEN SENT.
            //
            // That distinction is the whole reason this can overlap safely. _parked GROWS as
            // cars are sent for, so a fraction of _parked.Count is meaningless while they are
            // still coming -- three sent and three parked is a hundred per cent, and the crowd
            // would set off to a street with three cars on it. Spots.Length is known from the
            // start and does not move, so half of it means half of it whenever it is asked.
            //
            // HALF, so the crowd walks in WHILE the back half of the cars are still arriving
            // rather than after them. The two phases took a minute each end to end; overlapped
            // they take about a minute together, and the street filling up with cars and people
            // at the same time is what one of these actually looks like anyway.
            var enough = there >= (int)Math.Ceiling(Spots.Length * CrowdAfter);

            var late = _startedAt != 0 && now - _startedAt > FillGiveUpMs;

            if (!enough && !late) return;

            _carsIn = true;

            Log.Info("Takeover: " + there + " of " + Spots.Length +
                     " kerbs taken. The crowd sets off while the rest come in.");
        }

        /// <summary>Half the kerbs taken, and the crowd starts walking while the rest arrive.</summary>
        private const float CrowdAfter = 0.5f;

        /// <summary>The cars have their spots, so the crowd may come. See Filling.</summary>
        private bool _carsIn;

        /// <summary>
        /// Whether every car that turned up has reached its kerb, so the skidding can start.
        ///
        /// THE CARS THAT GO IN ARE THE CARS OFF THE KERB. That is what makes this worth
        /// waiting for rather than a nicety: a takeover that begins before the ring is full
        /// starts with half a wall AND pulls its performers out of the half that exists, so
        /// the street thins out at the exact moment it should be at its fullest. Waiting means
        /// the first donut happens in front of a finished street -- every spot taken, and then
        /// somebody pulls out of one.
        ///
        /// LATCHED ONCE IT PASSES. After that the ring is permanently short by whoever is out
        /// taking their turn, so asking again would stop the takeover dead the moment it
        /// started. This answers "has it filled yet", once, and never again.
        ///
        /// WITH A DEADLINE, because "all of them" is a promise about thirty-five cars and any
        /// one of them can be wedged behind a bin lorry on the way. Ninety seconds and it goes
        /// with whoever made it: a takeover that never starts is worse than one that starts
        /// with thirty-three cars parked. The log says which happened, so a build where they
        /// routinely do not arrive says so rather than just feeling slow.
        ///
        /// A car that has been destroyed on the way does not count as still coming. Nothing
        /// else in here would ever let go of it.
        /// </summary>
        /// <summary>How many spectators have actually reached their kerb.</summary>
        private int OnKerbs()
        {
            var n = 0;

            foreach (var p in _parked)
            {
                if (p.There) n++;
            }

            return n;
        }

        private bool Ringed()
        {
            if (_ringed) return true;

            // HALF THE KERBS, AND THAT IS THE WHOLE TEST NOW.
            //
            // This used to wait for three more things on top of it: every car SENT for, every
            // spectator spawned, and three in five of them actually stood at the junction. All
            // three were defensible on their own and together they meant the first donut went
            // in a minute and a half after the first car did -- and that minute and a half is
            // a car park. You stand in the middle of thirty-five parked cars watching nothing
            // happen, which is the one thing a takeover must never look like.
            //
            // The order asked for is: the cars fill up, and halfway through that somebody
            // starts skidding. So the pit opens on exactly the moment the crowd is already
            // sent for -- see Filling, which sets this at half the kerbs -- and everything
            // else arrives INTO a running takeover instead of queueing to make one.
            //
            // Nobody is performing to an empty street either way: seventeen-odd cars are
            // parked round the circle by the time this comes true, and the walk-in crowd is on
            // its way while the first car works. A takeover you arrive at is meant to be
            // already going.
            if (!_carsIn) return false;

            _ringed = true;

            Log.Info("Takeover: " + OnKerbs() + " of " + Spots.Length +
                     " kerbs taken. First car in, the rest arrive around it.");

            return true;
        }

        /// <summary>
        /// How long the ring gets to fill before it starts without the stragglers.
        ///
        /// Raised from ninety seconds to two and a half minutes when the arrivals were spread
        /// over a minute. The last car is now SENT for at sixty seconds and still has to drive
        /// in, so ninety would have fired as a matter of course rather than as the fallback it
        /// is -- and a fallback that trips every single night is not a fallback, it is the
        /// behaviour.
        /// </summary>
        private const int FillGiveUpMs = 150000;

        /// <summary>Set once the ring has filled. Never asked again -- see Ringed.</summary>
        private bool _ringed;

        /// <summary>How many are actually working the circle right now.</summary>
        private int Spinning()
        {
            var n = 0;

            foreach (var r in _running)
            {
                if (r.Leaving) continue;
                if (r.Car == null || !r.Car.Exists()) continue;

                // A PERFORMER AT ITS MARK IS SPINNING. Circling is the old arrival flag, set
                // only for a car that drove into the pit with no marker of its own, and no
                // car has done that since the markers went in -- so this counted nought all
                // night with four cars going round, and the flares, the cheering and the
                // filming all wait on this count. The log said it exactly: "0 on the circle"
                // with forty-odd handy to throw.
                if (!r.Circling && !(r.Stage >= 0 && r.AtStage)) continue;

                n++;
            }

            return n;
        }

        /// <summary>How many should be out there, and when that was last decided.</summary>
        private int _want = 3;


        private const int EmptyEveryMs = 15000;
        private bool _wantMark;
        private int _reroll;
        private const int RerollMs = 45000;

        /// <summary>
        /// How often a stretch of the night has somebody parked on the mark.
        ///
        /// A quarter. Often enough that it happens and is worth looking at when it does; rare
        /// enough that it reads as somebody deciding to, rather than as a fixture of the event.
        /// </summary>
        private const int MarkChance = 25;



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
                if (!model.IsValid || !model.IsInCdImage || !Core.Models.Ready(model)) return;

                _heli = World.CreateVehicle(model, from);
                model.MarkAsNoLongerNeeded();

                if (_heli == null || !_heli.Exists()) return;

                _heli.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _heli.Handle, true, true);
                Function.Call(Hash.SET_HELI_BLADES_FULL_SPEED, _heli.Handle);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _heli.Handle, true, true, false);

                var cop = new Model("s_m_y_cop_01");

                if (!cop.IsValid || !Core.Models.Ready(cop))
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
        /// <summary>And round the middle, slower again, because it is a circle.</summary>
        private const float LoopSpeed = 6f;
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
        /// <summary>
        /// NOTHING COMING TO THIS STOPS AT A RED LIGHT, and 128 is the flag that made them.
        ///
        /// The style was the careful one, lights and all, which is right for a taxi and wrong
        /// for thirty-five people driving to a street takeover at one in the morning. Half of
        /// them sat at the junction two streets away waiting for a green while the thing they
        /// were coming to was already going, and a car that queues politely at an empty
        /// crossing on its way to an illegal meet is the one detail that says none of this is
        /// really happening.
        ///
        /// EVERYTHING THAT STOPS THEM HITTING SOMETHING STAYS. 1 and 2 are stopping before
        /// vehicles and people, 4 through 32 are the avoidance, and all of it is kept -- they
        /// are meant to be lawless, not blind, and this street has sixty people stood in it.
        /// The one thing that goes is the obedience.
        /// </summary>
        private const int CareStyle = 1 | 2 | 4 | 8 | 16 | 32 | 256;

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
        /// <summary>
        /// How fast he crosses the junction. Seven, down from twenty-four.
        ///
        /// Twenty-four metres a second is eighty-six an hour and it was written for a Manchez.
        /// A cruise order a bike cannot obey is not a fast bike, it is a man pedalling flat out
        /// and never arriving where he was sent -- and the pass is timed off arriving.
        /// </summary>
        private const float PassSpeed = 7f;
        private const int PassGiveUpMs = 40000;

        /// <summary>How long a car gets to find its way back to its own spot.</summary>
        /// <summary>
        /// How fast he comes back, and it is a walking pace for a car on purpose.
        ///
        /// Eight rather than twelve. He is driving into a ring of people, and the difference
        /// between those two numbers is whether a man who steps back into his path gets
        /// stopped for or gets hit.
        /// </summary>
        private const float BackSpeed = 8f;

        private const int BikeGapMs = 9000;



        /// <summary>
        /// Somebody in the ring pulls out and takes their turn.
        ///
        /// THE CARS THAT RING THE JUNCTION ARE THE CARS THAT GO IN, which is both how a real
        /// one works and the answer to a question this never had a good reply to: where do the
        /// drift cars come from? They used to be spawned a block away and driven in, and when
        /// their go was over they drove off the map and were deleted -- so the ring was
        /// scenery and the circle was a conveyor belt, with no relationship between them.
        ///
        /// Now it is one set of cars. Somebody pulls out of the line, does his bit, and backs
        /// into the same gap he left. The ring thins by one while he is out, which is exactly
        /// what it should do, and there is no spawning or deleting during the whole night.
        ///
        /// His slot is held for him the entire time. That is why the car stays on the parked
        /// list with a flag rather than being moved onto another one.
        ///
        /// Not the lowriders. They are sat on their hydraulics being looked at, which is its
        /// own act, and a car bouncing on the spot is not one about to go and do donuts.
        /// </summary>
        /// <summary>A marker nobody has claimed, or -1 when they are all taken.</summary>
        private int Free()
        {
            for (var i = 0; i < Stages.Length; i++)
            {
                var taken = false;

                foreach (var r in _running)
                {
                    if (r.Stage != i) continue;

                    taken = true;
                    break;
                }

                if (!taken) return i;
            }

            return -1;
        }

        /// <summary>How many are sat on markers or on their way to one.</summary>
        private int Queued()
        {
            var n = 0;

            foreach (var r in _running)
            {
                // Called ones still HOLD a marker -- that is the point -- but they are not
                // waiting on it, so they do not count towards keeping the queue full.
                if (r.Stage >= 0 && !r.Called) n++;
            }

            return n;
        }

        /// <summary>
        /// Somebody's turn. The car that has been waiting longest goes in.
        ///
        /// LONGEST WAIT FIRST, which is the whole reason the markers have a clock on them. Any
        /// other order and the same car can be picked twice while somebody sits on a marker
        /// all night, which from the pavement is not a queue, it is favouritism.
        ///
        /// Whether he is the one on the mark or one going round is decided HERE rather than
        /// when he was fetched. A car waiting its turn has not been promised a particular job,
        /// so the roll that wants a static burnout can take whoever is next rather than
        /// waiting for the one car that happened to be labelled for it.
        /// </summary>
        private bool Turn(bool middle, int now)
        {
            // NOBODY IS SENT OFF A MARKER ANY MORE. The four markers are the show: a car
            // gets to its place and performs there, all night, and the middle is what the
            // crowd looks at them across. The line it used to be sent to drive is gone.
            return false;
        }


        /// <summary>Somebody drives in for their go, on the mark or round the outside.</summary>
        /// <summary>
        /// Fetch a performer: spawn one out on the road and send it to a free marker.
        ///
        /// SPAWNED RATHER THAN TAKEN OFF A KERB, which is the reversal of what this did
        /// before. The parked cars are SPECTATORS -- they drive in, they stop, and that is the
        /// last thing they do all night. Borrowing one for a turn meant the wall the ring
        /// exists to be lost a car every time somebody went in, and the whole apparatus of
        /// holding its space and steering it home existed to paper over that.
        ///
        /// So performers are their own cars now, from their own list, and when their go is
        /// over they leave the way they came instead of parking. The kerbs stay full.
        /// </summary>
        private bool In()
        {
            var stage = Free();

            if (stage < 0) return false;

            try
            {
                var from = OnRoad(DriveFromMin + (float)_rng.NextDouble() * (DriveFromMax - DriveFromMin));
                if (from == Vector3.Zero) return false;

                // NOT A SHOW CAR. It keeps the paint, the rims, the bodywork and the neon;
                // it loses the coloured tyre smoke and the coloured headlights. See Dress.
                var car = Contender(from);
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

                    // Which job he does is decided when he is CALLED off the marker, not now.
                    // See Turn.
                    Stage = stage,
                    Radius = DriftMin + (float)_rng.NextDouble() * (DriftMax - DriftMin),
                    Way = _rng.Next(2) == 0 ? 1 : -1
                };

                // TO THE MARKER. He waits his turn there like everybody else -- the pit
                // is entered from a marker and from nowhere else, so a spawned car and one
                // that was already here arrive in it the same way.
                var to = Toward(car.Position, Stages[stage].At);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              to.X, to.Y, to.Z, 12f, 0, car.Model.Hash,
                              CareStyle, 3f, true);

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
                Slick(r.Car, false);
                Function.Call(Hash.SET_DRIFT_TYRES, r.Car.Handle, false);
                Reckless(r, false);

                // HE GOES BACK TO HIS SPOT, not off the map. The gap he left has been held for
                // him the whole time, so this is a car rejoining a line rather than one leaving
                // and another arriving to replace it.
                //
                // Carefully rather than in a hurry: he is driving back INTO the ring of people
                // he has just been performing in front of, which is the one moment on this
                // whole junction where stopping for somebody is the right behaviour.
                // HIS OWN MARKER IF HE HAS ONE, and off the map only if he has not.
                var back = r.Stage >= 0
                    ? Toward(r.Car.Position, Stages[r.Stage].At)
                    : OnRoad(150f + (float)_rng.NextDouble() * 110f);

                if (back == Vector3.Zero) back = Middle.Around(190f);

                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Driver.Handle, r.Car.Handle,
                              back.X, back.Y, back.Z, BackSpeed, 0, r.Car.Model.Hash,
                              CareStyle, 10f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send one back: " + ex.Message);
            }
        }


        /// <summary>
        /// Patience on the way TO a marker, which is the same problem as patience on the way
        /// home and is answered the same way: rolling is fine, stopped for a few seconds means
        /// the route ran out or ran into somebody, and then he asks for it again.
        /// </summary>
        private void Hold(Runner r, int now, Vector3 at)
        {
            try
            {
                if (r.Car.Speed > 0.6f)
                {
                    r.Stuck = 0;
                    return;
                }

                if (r.Stuck == 0)
                {
                    r.Stuck = now;
                    return;
                }

                if (now - r.Stuck < BlockedMs) return;

                r.Stuck = now;

                var to = Toward(r.Car.Position, at);

                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Driver.Handle, r.Car.Handle,
                              to.X, to.Y, to.Z, 12f, 0, r.Car.Model.Hash,
                              CareStyle, 3f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, true);
            }
            catch
            {
                // He tries again in a few seconds.
            }
        }

        /// <summary>How long a car going home may sit still before it asks for the route again.</summary>
        private const int BlockedMs = 4000;



        private void Working(int now)
        {
            foreach (var r in _running)
            {
                if (!r.Circling) continue;
                if (r.Car == null || !r.Car.Exists()) continue;
                if (r.Driver == null || !r.Driver.Exists() || !r.Driver.IsAlive) continue;

                // THE LEASH, and it is a real drive rather than a shove. Five metres off his
                // ring and he gets an ordinary route back to the middle; the lock goes straight
                // back on the moment he is inside it again, not on the next clock.
                var gap = r.Car.Position.DistanceTo(Circle);

                if (gap > r.Radius + Wander)
                {
                    if (!r.Returning)
                    {
                        r.Returning = true;
                        r.NextAction = 0;
                    }

                    if (now < r.NextAction) continue;

                    r.NextAction = now + 3000;

                    try
                    {
                        // BACK TO THE MIDDLE, AND STILL SIDEWAYS. Aimed at the centre rather
                        // than at the nearest point on his ring: a car that slid wide hauling
                        // itself back towards the middle is what losing it and catching it
                        // looks like. The tyres stay on -- this is a correction inside his
                        // go, not the end of it.
                        Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, false);
                        Slick(r.Car, true);
                        Function.Call(Hash.SET_DRIFT_TYRES, r.Car.Handle, true);

                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Driver.Handle,
                                      r.Car.Handle, Circle.X, Circle.Y, Circle.Z,
                                      12f, 0, r.Car.Model.Hash, RushStyle, 4f, true);
                    }
                    catch
                    {
                        // Next time round.
                    }

                    continue;
                }

                if (r.Returning)
                {
                    r.Returning = false;
                    r.NextAction = 0;
                }

                // EVERY THIRTY SECONDS, THE OTHER WAY.
                if (r.SwapAt == 0) r.SwapAt = now + SwapMs;

                if (now >= r.SwapAt)
                {
                    r.Way = r.Way > 0 ? -1 : 1;
                    r.SwapAt = now + SwapMs;
                    r.NextAction = 0;
                }

                // NOTHING IN THE WAY STOPS HIM. A temp action is a driver input, not a route
                // -- there is no avoidance in it at all -- and there is no stall handling
                // round it either, any more. There was: a car not moving was called
                // blocked, cleared, held in a stationary burnout and given the lock again a
                // few seconds later, and on a junction full of people that was a show made
                // of stops. Lifting off for anybody in the arc went before it, for the same
                // reason. He holds the lock. Anybody in the way is hit, and getting out of
                // it is the crowd's job -- see Crowding.
                if (now < r.NextAction) continue;

                Lock(r, now);
            }
        }

        /// <summary>
        /// Full lock and the throttle, his way, asked for long enough that the next one is
        /// issued before this one runs out. Re-issuing an action a car is already performing
        /// simply continues it, so the overlap costs nothing and the gap it prevents was a
        /// car stopping dead every few seconds.
        /// </summary>
        private void Lock(Runner r, int now)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, false);
                Slick(r.Car, true);
                Function.Call(Hash.SET_DRIFT_TYRES, r.Car.Handle, true);
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Driver.Handle, r.Car.Handle, Spin(r.Way), LockMs);

                r.NextAction = now + LockMs - TopUpLead;
            }
            catch
            {
                r.NextAction = now + 1000;
            }
        }

        /// <summary>
        /// How long the lock is asked for at a time, and how often it swaps sides.
        ///
        /// TWELVE SECONDS, UP FROM SIX, AND THE SHOW ON A MARK USES IT TOO. Re-issuing a
        /// temp action a car is already performing is meant to simply continue it, and
        /// every top-up is one more chance for that to be a hair less than true. Fewer of
        /// them is fewer chances.
        /// </summary>
        private const int LockMs = 12000;
        private const int SwapMs = 30000;

        /// <summary>
        /// The show on a marker: full lock and the throttle, re-issued before it runs out.
        ///
        /// BURNOUT MODE OFF, GRIP DOWN. Those two are the whole trick. Burnout mode holds a car
        /// on the spot with its rears going, which is the opposite of what is wanted here, so
        /// it is turned off explicitly rather than left to whatever the car was doing on the
        /// way in. Reduced grip and drift tyres are what let the back come round instead of the
        /// car simply steering in a tight circle.
        /// </summary>
        private void Show(Runner r, int now)
        {
            r.NextAction = now + LockMs - TopUpLead;

            try
            {
                Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, false);
                Slick(r.Car, true);
                Function.Call(Hash.SET_DRIFT_TYRES, r.Car.Handle, true);
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Driver.Handle, r.Car.Handle, Spin(r.Way), LockMs);
            }
            catch
            {
                r.NextAction = now + 1000;
            }
        }

        /// <summary>
        /// A performer, performing, does not stop for anybody. The driver's reactions are
        /// blocked -- a ped clipped, a car nudged, a gunshot -- and the car is made
        /// collision-proof and strong, so it takes the knocks without losing its show. Only
        /// the performers, and only while they perform: taken off again the moment one leaves.
        /// </summary>
        private static void Reckless(Runner r, bool on)
        {
            try
            {
                if (r.Driver != null && r.Driver.Exists())
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, r.Driver.Handle, on);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, r.Driver.Handle, !on);
                }

                if (r.Car != null && r.Car.Exists())
                {
                    Function.Call(Hash.SET_ENTITY_PROOFS, r.Car.Handle, false, false, false, on, false, false, false, false);
                    Function.Call(Hash.SET_VEHICLE_STRONG, r.Car.Handle, on);
                    Function.Call(Hash.SET_VEHICLE_CAN_BE_VISIBLY_DAMAGED, r.Car.Handle, !on);

                    // AND THE TYRES DO NOT POP. Spinning on reduced grip for a whole takeover
                    // is minutes of wheelspin against kerbs, and a performer that blows a rear
                    // stops being a performer -- it grinds round on a rim and the show has a
                    // broken car in the middle of it. Rims stay on too, for the same reason.
                    Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, r.Car.Handle, !on);
                    Function.Call(Hash.SET_VEHICLE_WHEELS_CAN_BREAK, r.Car.Handle, !on);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Full lock and the throttle, one way or the other: with the grip reduced that is a
        /// car going round on the spot. 30 and 31 -- handbrake turns -- were what these
        /// were, and on a car that is already stopped a handbrake turn is a car that sits
        /// there, which is why every performer looked like the standing kind.
        /// </summary>
        private int Spin(int way)
        {
            return way > 0 ? 7 : 8;
        }

        /// <summary>
        /// How far ahead of a burst ending the next one is asked for.
        ///
        /// IT WAS FOUR HUNDRED MILLISECONDS AND THE TICK IS SEVEN HUNDRED, WHICH IS THE
        /// WHOLE BUG. The top-up only happens on a tick, so a lead shorter than one tick
        /// cannot be relied on to land before the old action expires -- it arrived up to
        /// three hundred milliseconds late, every burst, and a car doing donuts stopped dead
        /// for a third of a second every three seconds all night. It read as the driver
        /// pausing for breath, which is not a thing anybody does mid-burnout.
        ///
        /// A tick and a bit. The overlap costs nothing -- re-issuing a temp action a car is
        /// already performing simply replaces it -- and it cannot now be late.
        /// </summary>
        private const int TopUpLead = TickMs + 300;

        /// <summary>How fast they come in, and how long they sit before they start.</summary>
        private const float ComeInSpeed = 11f;
        private const int SettleMs = 1500;
        private const float Wander = 5f;

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
        /// Flares, thrown into the middle.
        ///
        /// Hand flares rather than anything fired. A ring of people who have just heard a shot
        /// is a ring of sixty people leaving; a thrown flare lands, burns, lights the smoke
        /// orange, and nobody flinches. It is also what people actually throw at these.
        ///
        /// ONLY WHILE THERE IS SOMETHING TO LIGHT. A flare goes out because a car is sideways
        /// in front of you, not on a timer -- so this does nothing at all unless somebody is
        /// working the circle, which also means it stops on its own when the police arrive.
        ///
        /// VOLLEYS, NOT ONE MAN. It was one thrower every six to eighteen seconds and more
        /// than half of those went straight up, so the middle of a takeover had one flare in
        /// it at a time and the crowd looked like it was watching. Now two to four throw
        /// within a second of each other every few seconds, and four in five go low across
        /// the middle where the cars are. The high ones stay, fewer, because they are what
        /// you see from a street away and come to look at.
        ///
        /// Each is given one flare and it is taken back afterwards, because a man stood in a
        /// crowd holding one for three hours will eventually be seen holding it.
        /// </summary>
        private void Flares(int now)
        {
            // The volley in flight. Each throws on their own moment, so four arms do not go
            // up on one frame.
            for (var i = 0; i < _throws.Count; i++)
            {
                var t = _throws[i];
                if (t.Loosed || now < t.ThrowAt) continue;

                t.Loosed = true;

                try
                {
                    Throw(t);
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover: no flare: " + ex.Message);
                }
            }

            if (now < _nextFlare || _crowd.Count == 0) return;

            var spinning = Spinning();

            if (spinning < 1)
            {
                Stuck("no car on the circle", spinning);
                return;
            }

            _nextFlare = now + FlareMinMs + _rng.Next(FlareMaxMs - FlareMinMs);

            var many = FlareThrowersMin + _rng.Next(FlareThrowersMax - FlareThrowersMin + 1);
            var added = 0;

            for (var tries = 0; many > 0 && tries < 14; tries++)
            {
                var w = _crowd[_rng.Next(_crowd.Count)];

                if (!Handy(w)) continue;

                Vector3 at;

                if (_rng.Next(100) < FlareUpShare)
                {
                    at = new Vector3(Circle.X + (float)(_rng.NextDouble() * 8.0 - 4.0),
                                     Circle.Y + (float)(_rng.NextDouble() * 8.0 - 4.0),
                                     Circle.Z + FlareUpHigh);
                }
                else
                {
                    at = new Vector3(Circle.X + (float)(_rng.NextDouble() * 7.0 - 3.5),
                                     Circle.Y + (float)(_rng.NextDouble() * 7.0 - 3.5),
                                     Circle.Z + 1.0f);
                }

                _throws.Add(new Thrown
                {
                    W = w,
                    At = at,
                    ThrowAt = now + _rng.Next(FlareStaggerMs),
                    Back = now + FlareStaggerMs + FlareHoldMs
                });

                many--;
                _volleyed++;
                added++;
            }

            // Said out loud when a volley found nobody, because that is the difference
            // between flares that are thrown and dropped and flares that are never thrown.
            if (added == 0) Stuck("nobody handy to throw", spinning);

            if (_volleyed > 0 && _volleyed != _volleyLogged)
            {
                _volleyLogged = _volleyed;
                Log.Info("Takeover: " + _volleyed + " flares thrown so far this takeover.");
            }
        }

        /// <summary>How many flares this takeover has had thrown, for the log.</summary>
        private int _volleyed;
        private int _volleyLogged;

        /// <summary>Whether this one already has a flare or a firework on the go.</summary>
        private bool Occupied(Watcher w)
        {
            for (var i = 0; i < _throws.Count; i++)
            {
                if (ReferenceEquals(_throws[i].W, w)) return true;
            }

            for (var i = 0; i < _rockets.Count; i++)
            {
                if (ReferenceEquals(_rockets[i].Man, w)) return true;
            }

            return false;
        }

        /// <summary>One throw: armed, turned to face the middle, and let go.</summary>
        private void Throw(Thrown t)
        {
            var w = t.W;
            if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) return;

            var h = w.Man.Handle;
            var flare = Function.Call<int>(Hash.GET_HASH_KEY, "weapon_flare");

            // OUT OF WHATEVER THEY WERE DOING FIRST. A throw handed to somebody stood in a
            // scenario -- cheering, filming, drinking -- is a throw the game quietly drops,
            // and since the crowd started cheering that was most of them. They go back to
            // their idle through Unarm afterwards, and a cheer cut short by a flare is
            // exactly what a cheer at one of these looks like.
            w.Hype = 0;
            Function.Call(Hash.CLEAR_PED_TASKS, h);

            Function.Call(Hash.GIVE_WEAPON_TO_PED, h, flare, 1, false, true);
            Function.Call(Hash.SET_CURRENT_PED_WEAPON, h, flare, true);
            Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);

            var to = t.At - w.Man.Position;

            w.Man.Heading = (float)((Math.Atan2(-to.X, to.Y) * 180.0 / Math.PI + 360.0) % 360.0);

            Function.Call(Hash.TASK_THROW_PROJECTILE, h, t.At.X, t.At.Y, t.At.Z);
        }

        /// <summary>The flares taken back once thrown.</summary>
        private void Unarm(int now)
        {
            for (var i = _throws.Count - 1; i >= 0; i--)
            {
                var t = _throws[i];
                if (now < t.Back) continue;

                _throws.RemoveAt(i);

                try
                {
                    var w = t.W;
                    if (w == null || w.Man == null || !w.Man.Exists()) continue;

                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, w.Man.Handle, true);
                    Home(w);
                }
                catch
                {
                    // He keeps it, then. It is a flare.
                }
            }
        }

        /// <summary>
        /// Fireworks, from a box set down on the ring round the middle.
        ///
        /// Somebody walks out of the crowd to a spot a few car lengths from the circle,
        /// crouches over a box for a few seconds, and it goes up. Doing it from a box a
        /// person walked to is what makes it a firework rather than an explosion.
        ///
        /// MORE OF THEM. It was one every twenty-five to sixty seconds, one man at a time,
        /// which over a takeover is a handful. Now one every nine to twenty-four seconds,
        /// two boxes can be on the go at once, and each puts up four bursts spread a little
        /// rather than three in a column.
        /// </summary>
        private void Firework(int now)
        {
            // The boxes being set up, lit when their moment comes.
            for (var i = _rockets.Count - 1; i >= 0; i--)
            {
                var r = _rockets[i];
                if (now < r.At) continue;

                _rockets.RemoveAt(i);

                Bang(r.Spot);

                if (r.Man != null && r.Man.Man != null && r.Man.Man.Exists())
                {
                    try { Function.Call(Hash.CLEAR_PED_TASKS, r.Man.Man.Handle); }
                    catch { /* he is stood up already */ }

                    r.Man.There = false;
                }

                try { if (r.Prop != null && r.Prop.Exists()) r.Prop.Delete(); }
                catch { /* it is gone */ }
            }

            if (now < _nextFire || _crowd.Count == 0) return;
            if (_rockets.Count >= FireAtOnce) return;

            _nextFire = now + FireMinMs + _rng.Next(FireMaxMs - FireMinMs);

            try
            {
                var w = _crowd[_rng.Next(_crowd.Count)];

                if (!Handy(w)) return;

                var a = _rng.NextDouble() * Math.PI * 2d;
                var dist = FireRingMin + (float)(_rng.NextDouble() * (FireRingMax - FireRingMin));

                var spot = Ground(new Vector3(Circle.X + (float)Math.Cos(a) * dist,
                                              Circle.Y + (float)Math.Sin(a) * dist, Circle.Z));

                Prop prop = null;

                try
                {
                    var model = new Model("ind_prop_firework_01");

                    if (model.IsValid && model.IsInCdImage && Core.Models.Ready(model))
                    {
                        prop = World.CreateProp(model, spot, false, false);
                        model.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    prop = null;
                }

                w.There = false;

                Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                              spot.X, spot.Y, spot.Z, 2.5f, -1, 1f, true, 0f);
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, w.Man.Handle,
                              "WORLD_HUMAN_CROUCH_INSPECT", 0, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);

                _rockets.Add(new Rocket { At = now + FireSetUpMs, Spot = spot, Man = w, Prop = prop });
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover: no firework: " + ex.Message);
            }
        }

        /// <summary>A box going off: bursts stacked up over it, spread a little, and the bang.</summary>
        private void Bang(Vector3 at)
        {
            try
            {
                Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, FireAsset);

                if (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, FireAsset)) return;

                for (var i = 0; i < FireBurstsPerBox; i++)
                {
                    var up = at + new Vector3((float)(_rng.NextDouble() * 5.0 - 2.5),
                                              (float)(_rng.NextDouble() * 5.0 - 2.5),
                                              6f + i * 5f);

                    Function.Call(Hash.USE_PARTICLE_FX_ASSET, FireAsset);
                    Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD,
                                  FireBursts[_rng.Next(FireBursts.Length)],
                                  up.X, up.Y, up.Z, 0f, 0f, 0f,
                                  1.3f + (float)_rng.NextDouble() * 0.5f, false, false, false);
                }

                Function.Call(Hash.PLAY_SOUND_FROM_COORD, -1, "Explosion",
                              at.X, at.Y, at.Z, "DLC_HEIST_FLEECA_SOUNDSET", false, 0, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover: the firework did not go off: " + ex.Message);
            }
        }

        /// <summary>
        /// The crowd cheering and filming while there is something to cheer.
        ///
        /// Standing about with a phone out is what the crowd does between things -- see
        /// Watching. This is what it does when a car is sideways in front of it: turns to
        /// the middle and either cheers or holds the phone up to film it, for a few seconds
        /// at a time, one or two more of them every second or so, until the cars stop and
        /// the last of them wind down. Nobody is told to stop; each one's turn simply ends
        /// and they go back to what they were doing.
        ///
        /// Not everybody at once. A crowd where every arm goes up on one frame is a crowd
        /// on a cue, and never more than about two thirds of them at a time, because the
        /// people who are not reacting are what makes the ones who are look like a
        /// reaction.
        /// </summary>
        private void Hype(int now)
        {
            var spinning = Spinning() >= 1;
            var hyped = 0;

            // Turns that are over, and turns that should be because there is nothing left
            // to cheer. Handed back through There: the walk-up code sees somebody stood on
            // their own slot, and gives them their own idle again facing the middle.
            foreach (var w in _crowd)
            {
                if (w.Hype == 0) continue;

                if (now < w.Hype && spinning) { hyped++; continue; }
                if (now < w.Hype && now < w.Hype - HypeWindDownMs) { hyped++; continue; }

                w.Hype = 0;
                Home(w);
            }

            if (!spinning) return;
            if (now < _nextHype || _crowd.Count == 0) return;

            _nextHype = now + HypeEveryMinMs + _rng.Next(HypeEveryMaxMs - HypeEveryMinMs);

            if (hyped >= (int)(_crowd.Count * HypeShare)) return;

            var many = 1 + _rng.Next(2);

            for (var tries = 0; many > 0 && tries < 10; tries++)
            {
                var w = _crowd[_rng.Next(_crowd.Count)];

                if (!Handy(w) || w.Hype != 0) continue;

                try
                {
                    var doing = _rng.Next(100) < HypeCheerShare
                        ? "WORLD_HUMAN_CHEERING"
                        : "WORLD_HUMAN_MOBILE_FILM_SHOCKING";

                    Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                    Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, Facing(w.Man.Position));
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, w.Man.Handle, doing, 0, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, true);

                    w.Hype = now + HypeMinMs + _rng.Next(HypeMaxMs - HypeMinMs);
                    many--;
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover: nobody cheered: " + ex.Message);
                }
            }
        }

        /// <summary>How long a turn lasts, how often somebody starts one, and how many can be at it.</summary>
        private const int HypeMinMs = 6000;
        private const int HypeMaxMs = 15000;
        private const int HypeEveryMinMs = 700;
        private const int HypeEveryMaxMs = 1600;
        private const int HypeCheerShare = 45;
        private const float HypeShare = 0.66f;

        /// <summary>Once the cars stop, a turn is cut short to this much of what was left, so the last cheer trails the last car by a moment and not by a quarter of a minute.</summary>
        private const int HypeWindDownMs = 3000;

        private int _nextHype;

        // ==================================================================
        // Who can be given something to do
        // ==================================================================

        /// <summary>
        /// Whether this one can be handed a flare, a firework or a cheer: alive, stood
        /// somewhere near their own spot, not already busy with one of those, and not on
        /// the ground or in a fight.
        ///
        /// BY WHERE THEY ARE, NOT BY THE FLAG. There was set on arriving at the slot and
        /// cleared by a cheer ending, and only set again by arriving within a pace of the
        /// slot -- and a cheer moves people a pace. So after a few cheers most of the crowd
        /// was flagged as elsewhere while stood exactly where they had been, and a volley
        /// of flares found nobody to throw, fourteen picks running.
        /// </summary>
        private bool Handy(Watcher w)
        {
            if (w == null || w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) return false;
            if (Occupied(w)) return false;
            if (w.Dodge != 0 || w.Angry != 0 || w.Rush != 0 || w.Wreck != 0) return false;
            if (w.Man.Position.DistanceTo(w.Slot) > HandyRange) return false;

            try
            {
                if (Function.Call<bool>(Hash.IS_PED_RAGDOLL, w.Man.Handle)) return false;
                if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, w.Man.Handle, 0)) return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        private const float HandyRange = 6f;
        private int _nobodyAt;

        /// <summary>
        /// Why there was no volley, in the log, at most once every twenty seconds. The
        /// difference between flares that are thrown and dropped and flares that are never
        /// thrown is a line in the log, and there was no line at all.
        /// </summary>
        private void Stuck(string why, int spinning)
        {
            var now = Game.GameTime;
            if (now - _nobodyAt < 20000) return;
            _nobodyAt = now;

            var handy = 0;
            var there = 0;

            foreach (var w in _crowd)
            {
                if (Handy(w)) handy++;
                if (w.There) there++;
            }

            Log.Info("Takeover: no flare volley -- " + why + ": " + _crowd.Count + " in the crowd, " +
                     there + " at their spots, " + handy + " handy, " + spinning + " on the circle.");
        }

        /// <summary>
        /// Sends somebody back to their own spot the way the crowd loop does for anybody
        /// knocked off it: a walk to the slot, and the loop gives them their idle again when
        /// they arrive. Used for everything that ends -- a throw, a cheer, a dodge, a fight.
        /// </summary>
        private static void Home(Watcher w)
        {
            Home(w, false);
        }

        /// <summary>Home, at a run: the way a group comes back out of the middle.</summary>
        private static void Home(Watcher w, bool run)
        {
            if (w == null || w.Man == null || !w.Man.Exists()) return;

            w.There = false;
            w.Away = 0;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                              w.Slot.X, w.Slot.Y, w.Slot.Z, run ? 3.0f : 1.0f, -1, 1.0f, true, 0f);
                Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
            }
            catch
            {
                // The loop will notice.
            }
        }

        // ==================================================================
        // The riders
        // ==================================================================

        /// <summary>One of the set on a dirt bike: where he stops, how far along he is, what he is doing.</summary>
        private sealed class Rider
        {
            public Vehicle Bike;
            public Ped Man;
            public Vector3 Park;
            public Vector3 Outer;
            public int Sent;

            /// <summary>0 riding to the outer point, 5 the straight run in, 1 sat there watching.</summary>
            public int Stage;

            /// <summary>0 sat, 1 filming, 2 lighting the back tyre up; until when; and when the next thing is.</summary>
            public int Doing;
            public int DoUntil;
            public int NextDo;
            public int Held;
            public Prop Phone;
        }

        private readonly List<Rider> _riders = new List<Rider>();
        private bool _ridersSent;
        private int _ridersAt;

        /// <summary>Sanchezes and Street Blazers: what the set rides round the back streets.</summary>
        private static readonly string[] DirtBikes = { "sanchez", "sanchez2", "blazer" };

        /// <summary>The filming, from the game's own scenario, played on the top half only so it works in the saddle.</summary>
        private const string FilmDict = "amb@world_human_mobile_film_shocking@male@base";
        private const string FilmClip = "base";
        private const int FilmFlags = 1 | 16 | 32;
        private const string PhoneProp = "prop_npc_phone_02";
        private const int PropHandBone = 60309;

        private const int RidersMin = 2;
        private const int RidersMax = 4;
        private const int RidersAfterMs = 8000;
        private const float RideIn = 16f;
        /// <summary>
        /// Where the bike stops -- on the line with the crowd -- and where it comes in
        /// from: a point further out on the same bearing, so the last leg is a straight run
        /// inward and he arrives facing the middle without being turned by hand.
        /// </summary>
        private const float BikeOut = 1.2f;
        private const float StageOut = 13f;
        private const float BikeThere = 2.5f;
        private const float OuterThere = 6f;
        private const float RideStraight = 30f;
        private const float CreepIn = 6f;
        private const int StraightGiveUpMs = 14000;
        private const int RideGiveUpMs = 45000;
        private const int HoldStillEveryMs = 6000;

        private const int FilmChance = 50;
        private const int BurnChance = 28;
        private const int FilmMinMs = 9000;
        private const int FilmMaxMs = 18000;
        private const int TyreMinMs = 2500;
        private const int TyreMaxMs = 4500;
        private const int RestMinMs = 8000;
        private const int RestMaxMs = 22000;
        private const int BurnAction = 23;

        /// <summary>
        /// A few of the set on dirt bikes, as spectators.
        ///
        /// Once the crowd is forming, two to four of them come in on Sanchezes and Street
        /// Blazers, pull up on the line with the crowd facing the middle, and stay in the
        /// saddle watching. They do not get off. Now and then one
        /// puts his phone up and films, and now and then one lights the back tyre up
        /// without going anywhere. When the police come they turn round and ride off, which
        /// is the one thing a man on a Sanchez has over a man on foot.
        /// </summary>
        private void Riders(int now)
        {
            if (!_ridersSent)
            {
                if (_crowd.Count < 6) return;

                if (_ridersAt == 0)
                {
                    _ridersAt = now + RidersAfterMs;
                    return;
                }

                if (now < _ridersAt) return;

                _ridersSent = true;

                try { Function.Call(Hash.REQUEST_ANIM_DICT, FilmDict); }
                catch { /* then nobody films */ }

                var many = RidersMin + _rng.Next(RidersMax - RidersMin + 1);
                var sent = 0;

                for (var i = 0; i < many; i++)
                {
                    if (SendRider(now)) sent++;
                }

                if (sent > 0) Log.Info("Takeover: " + sent + " of the set on dirt bikes on the way.");
            }

            for (var i = _riders.Count - 1; i >= 0; i--)
            {
                var r = _riders[i];

                if (r.Bike == null || !r.Bike.Exists() || r.Man == null || !r.Man.Exists() || !r.Man.IsAlive)
                {
                    Stop(r);

                    try { Loose(r.Bike, r.Man); }
                    catch { /* gone */ }

                    _riders.RemoveAt(i);
                    continue;
                }

                try
                {
                    if (r.Stage == 0)
                    {
                        // Riding in, to the outer point. There when near it, or when it has
                        // taken too long -- then the straight run in from wherever he is.
                        if (r.Bike.Position.DistanceTo(r.Outer) > OuterThere && now - r.Sent < RideGiveUpMs) continue;

                        Function.Call(Hash.CLEAR_PED_TASKS, r.Man.Handle);
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Man.Handle, r.Bike.Handle,
                                      r.Park.X, r.Park.Y, r.Park.Z, CreepIn, 0, r.Bike.Model.Hash,
                                      CareStyle, 1.5f, RideStraight);
                        Function.Call(Hash.SET_PED_KEEP_TASK, r.Man.Handle, true);

                        r.Stage = 5;
                        r.Sent = now;
                        continue;
                    }

                    if (r.Stage == 5)
                    {
                        // The straight run in. There when on the spot, or when that has had
                        // its time.
                        if (r.Bike.Position.DistanceTo(r.Park) > BikeThere && now - r.Sent < StraightGiveUpMs) continue;

                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Man.Handle, r.Bike.Handle, 1, 2000);
                        r.Stage = 1;
                        r.Held = now;
                        r.NextDo = now + 5000 + _rng.Next(8000);

                        Log.Info("Takeover: a rider is in, " + r.Bike.Position.DistanceTo(r.Park).ToString("0.0") + " m from his spot.");
                        continue;
                    }

                    // SAT THERE. Whatever he was doing ends on its clock, and the next thing
                    // is decided after a rest: half the time the phone comes up, a quarter of
                    // the time the back tyre does, the rest of the time he just watches.
                    if (r.Doing != 0 && now >= r.DoUntil)
                    {
                        Stop(r);
                        r.NextDo = now + RestMinMs + _rng.Next(RestMaxMs - RestMinMs);
                    }

                    if (r.Doing == 0 && now >= r.NextDo)
                    {
                        var roll = _rng.Next(100);

                        if (roll < FilmChance && Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, FilmDict))
                        {
                            Function.Call(Hash.TASK_PLAY_ANIM, r.Man.Handle, FilmDict, FilmClip,
                                          4f, -4f, -1, FilmFlags, 0f, false, false, false);
                            r.Phone = Phone(r.Man);
                            r.Doing = 1;
                            r.DoUntil = now + FilmMinMs + _rng.Next(FilmMaxMs - FilmMinMs);
                        }
                        else if (roll < FilmChance + BurnChance)
                        {
                            // A still one: the temp action holds the front and spins the back.
                            var ms = TyreMinMs + _rng.Next(TyreMaxMs - TyreMinMs);

                            Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Bike.Handle, true);
                            Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Man.Handle, r.Bike.Handle, BurnAction, ms);
                            r.Doing = 2;
                            r.DoUntil = now + ms;
                        }
                        else
                        {
                            r.NextDo = now + RestMinMs + _rng.Next(RestMaxMs - RestMinMs);
                        }
                    }

                    // The brake, put on again every few seconds, so a bike nudged by somebody
                    // walking past does not roll off into the road. Not during a burnout,
                    // which is its own action and would be cancelled by it.
                    if (r.Doing != 2 && now - r.Held >= HoldStillEveryMs)
                    {
                        r.Held = now;
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Man.Handle, r.Bike.Handle, 1, HoldStillEveryMs + 500);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover: a rider lost the plot: " + ex.Message);
                    r.Stage = 1;
                }
            }
        }

        /// <summary>Whatever he was doing, over: the phone away, the tyre stopped, the brake back on.</summary>
        private void Stop(Rider r)
        {
            try
            {
                if (r.Doing == 1 && r.Man != null && r.Man.Exists())
                {
                    Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, r.Man.Handle);
                }

                if (r.Doing == 2 && r.Bike != null && r.Bike.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Bike.Handle, false);

                    if (r.Man != null && r.Man.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, r.Man.Handle, r.Bike.Handle, 1, 1500);
                    }
                }

                if (r.Phone != null && r.Phone.Exists()) r.Phone.Delete();
            }
            catch
            {
                // It is over either way.
            }

            r.Phone = null;
            r.Doing = 0;
        }

        /// <summary>A phone in his hand, on the prop-holder bone, which is already in the grip.</summary>
        private static Prop Phone(Ped who)
        {
            try
            {
                var model = new Model(PhoneProp);
                if (!model.IsValid || !model.Request(1000)) return null;

                var prop = World.CreateProp(model, who.Position, false, false);
                model.MarkAsNoLongerNeeded();

                if (prop == null || !prop.Exists()) return null;

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, who.Handle, PropHandBone);

                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, prop.Handle, who.Handle, bone,
                              0f, 0f, 0f, 0f, 0f, 0f, false, false, false, false, 2, true);

                return prop;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>One rider, sent for: a bike from down the road, a spot behind the ring.</summary>
        private bool SendRider(int now)
        {
            try
            {
                var from = OnRoad(ParkFromMin + (float)_rng.NextDouble() * (ParkFromMax - ParkFromMin));

                if (from == Vector3.Zero)
                {
                    Log.Info("Takeover: no road to send a rider from.");
                    return false;
                }

                Vector3 park, outer;

                if (!RiderSpot(out park, out outer))
                {
                    Log.Info("Takeover: no gap on the line for a rider.");
                    return false;
                }

                var bike = Make(DirtBikes, from, false, false);

                if (bike == null)
                {
                    Log.Info("Takeover: no bike would load for a rider.");
                    return false;
                }

                // Ours, in the set's green.
                try
                {
                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, bike.Handle, 0);
                    Function.Call(Hash.SET_VEHICLE_COLOURS, bike.Handle, 49, 49);
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, bike.Handle, 53, 0);
                }
                catch
                {
                    // Whatever colour it came in.
                }

                var man = Behind(bike);

                if (man == null)
                {
                    bike.Delete();
                    return false;
                }

                Helmets.Off(man);

                // He stays on it: no getting off for a fight, none to run, and deaf to the
                // street the way the crowd is, so a bang does not have him off the bike.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, man.Handle, 3, false);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, man.Handle, 0, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, true);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, man.Handle, bike.Handle,
                              outer.X, outer.Y, outer.Z, RideIn, 0, bike.Model.Hash, CareStyle, 4f, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, true);

                _riders.Add(new Rider { Bike = bike, Man = man, Park = park, Outer = outer, Sent = now });
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send a rider: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Where a bike stops and where it comes in from: a bearing round the ring with a
        /// gap in the crowd at it, the bike on the line in that gap, clear of the other
        /// bikes and of the kerb cars, and the outer point further out on the same line.
        /// </summary>
        private bool RiderSpot(out Vector3 park, out Vector3 outer)
        {
            park = Vector3.Zero;
            outer = Vector3.Zero;

            for (var tries = 0; tries < 24; tries++)
            {
                var a = _rng.NextDouble() * Math.PI * 2d;
                var dir = new Vector3((float)Math.Cos(a), (float)Math.Sin(a), 0f);
                var at = Middle + dir * (Ring + BikeOut);
                var clear = true;

                foreach (var w in _crowd)
                {
                    if (w.Slot.DistanceTo(at) < 1.6f)
                    {
                        clear = false;
                        break;
                    }
                }

                if (clear)
                {
                    foreach (var r in _riders)
                    {
                        if (r.Park.DistanceTo(at) < 3f)
                        {
                            clear = false;
                            break;
                        }
                    }
                }

                if (clear)
                {
                    foreach (var p in _parked)
                    {
                        if (p.Slot.DistanceTo(at) < 4.5f)
                        {
                            clear = false;
                            break;
                        }
                    }
                }

                if (!clear) continue;

                park = at;
                outer = Middle + dir * (Ring + StageOut);
                return true;
            }

            return false;
        }

        /// <summary>The police are here: round, and away, whether he was sat there or still coming.</summary>
        private void RidersOff(int now)
        {
            for (var i = _riders.Count - 1; i >= 0; i--)
            {
                var r = _riders[i];

                Stop(r);

                try
                {
                    if (r.Bike != null && r.Bike.Exists() && r.Man != null && r.Man.Exists() && r.Man.IsAlive)
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, r.Man.Handle);
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, r.Man.Handle, r.Bike.Handle, LeaveSpeed, CareStyle);
                        Function.Call(Hash.SET_PED_KEEP_TASK, r.Man.Handle, true);
                    }

                    Loose(r.Bike, r.Man);
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover: a rider could not get away: " + ex.Message);
                }

                _riders.RemoveAt(i);
            }
        }

        // ==================================================================
        // The middle
        // ==================================================================

        /// <summary>
        /// Now and then, while the cars are going round, a handful of the crowd run into the
        /// middle of it.
        ///
        /// Four to seven of them, from wherever they are stood, at a sprint, to a spot each
        /// in a loose knot where Michael stood in the road. They cheer and film from among
        /// the cars for half a minute, and the same dodge that keeps the ring out of a car's
        /// way keeps them out of it -- only somebody knocked out of the middle goes back
        /// into the middle rather than home. Then they run back out, and the next group
        /// goes in a minute or two later.
        /// </summary>
        private void Rushes(int now)
        {
            // ---- the group in the middle ----
            foreach (var w in _crowd)
            {
                if (w.Rush == 0) continue;

                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive)
                {
                    w.Rush = 0;
                    continue;
                }

                if (now >= w.Rush)
                {
                    // Half a minute is enough. Out, at a run, and home.
                    w.Rush = 0;
                    w.RushStage = 0;
                    Home(w, true);
                    continue;
                }

                // Getting out of a car's way, or on a driver: left to it.
                if (w.Dodge != 0 || w.Angry != 0 || w.Wreck != 0) continue;

                var at = w.Man.Position;

                if (w.RushStage == 0)
                {
                    // On the way in. There when they are there, or when the run has had long
                    // enough -- a man stopped a stride short by a passing car is in the middle
                    // as far as anybody watching is concerned.
                    if (at.DistanceTo(w.RushAt) > 1.6f && now < w.RushMove) continue;

                    w.RushStage = 1;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                        Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, Facing(at));
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, w.Man.Handle,
                                      _rng.Next(100) < 60 ? "WORLD_HUMAN_CHEERING" : "WORLD_HUMAN_MOBILE_FILM_SHOCKING",
                                      0, true);
                    }
                    catch
                    {
                        // Stood there is stood there.
                    }

                    continue;
                }

                // There, and knocked well off it -- a car, a shove. Back to the spot.
                if (at.DistanceTo(w.RushAt) > 2.5f)
                {
                    w.RushStage = 0;
                    Dash(w, now);
                }
            }

            // ---- the next group ----
            if (now < _nextRush || _crowd.Count == 0) return;
            if (Spinning() < 1) return;

            _nextRush = now + RushHoldMs + RushMinMs + _rng.Next(RushMaxMs - RushMinMs);

            var many = RushMin + _rng.Next(RushMax - RushMin + 1);
            var sent = 0;

            for (var tries = 0; sent < many && tries < 40; tries++)
            {
                var w = _crowd[_rng.Next(_crowd.Count)];
                if (!Handy(w) || w.Hype != 0) continue;

                var a = _rng.NextDouble() * Math.PI * 2d;
                var d = RushKnotMin + _rng.NextDouble() * (RushKnotMax - RushKnotMin);

                w.RushAt = RushSpot + new Vector3((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d), 0f);
                w.Rush = now + RushHoldMs + _rng.Next(4000);
                w.RushStage = 0;
                w.Hype = 0;

                Dash(w, now);
                sent++;
            }

            if (sent > 0) Log.Info("Takeover: " + sent + " of the crowd have run into the middle.");
        }

        /// <summary>A sprint to their spot in the middle, by the nav mesh so it goes round what it can.</summary>
        private static void Dash(Watcher w, int now)
        {
            w.RushMove = now + RushRunMs;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                              w.RushAt.X, w.RushAt.Y, w.RushAt.Z, 3.0f, RushRunMs, 0.5f, true, 0f);
                Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
            }
            catch
            {
                // They will be counted as there when the run has had its time.
            }
        }

        private int _nextRush;

        /// <summary>Where Michael stood in the road, and how loose the knot round it is.</summary>
        private Vector3 RushSpot => _here.Rush;
        private const double RushKnotMin = 0.8;
        private const double RushKnotMax = 2.6;

        private const int RushMin = 4;
        private const int RushMax = 7;
        private const int RushHoldMs = 30000;
        private const int RushRunMs = 7000;

        /// <summary>The gap between one group running out and the next running in.</summary>
        private const int RushMinMs = 45000;
        private const int RushMaxMs = 100000;

        // ==================================================================
        // The tyres
        // ==================================================================

        /// <summary>
        /// A little slippery, not a lot.
        ///
        /// SET_VEHICLE_REDUCE_GRIP is ice: it is what made the cars drift, and it is what
        /// sent them off the circle every third lap. It is a switch with no half setting,
        /// so the half setting is the car's own handling: the tyres are given a fraction of
        /// their grip while the car is working the circle, and it all back when it stops.
        ///
        /// HANDLING IS THE MODEL'S, NOT THE CAR'S: every car of that model in the game --
        /// the traffic, the player's -- shares it. So the original is written down once per
        /// model, each car on the circle is counted against it, and the model gets its grip
        /// back when the last of its cars has finished, and again, whatever was counted,
        /// when the takeover packs up. Nothing is left slippery for the rest of the night.
        /// </summary>
        private void Slick(Vehicle car, bool on)
        {
            if (car == null || !car.Exists()) return;

            try
            {
                Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, car.Handle, false);

                var key = car.Model.Hash;

                if (on)
                {
                    if (!_slick.Add(car.Handle)) return;

                    var h = car.HandlingData;
                    if (h == null || !h.IsValid) return;

                    Grip g;

                    if (!_grip.TryGetValue(key, out g))
                    {
                        g = new Grip { Max = h.TractionCurveMax, Min = h.TractionCurveMin };
                        _grip[key] = g;
                    }

                    g.Count++;

                    h.TractionCurveMax = g.Max * SlickTraction;
                    h.TractionCurveMin = g.Min * SlickTraction;
                }
                else
                {
                    if (!_slick.Remove(car.Handle)) return;

                    Grip g;
                    if (!_grip.TryGetValue(key, out g)) return;

                    g.Count--;

                    if (g.Count <= 0)
                    {
                        var h = car.HandlingData;

                        if (h != null && h.IsValid)
                        {
                            h.TractionCurveMax = g.Max;
                            h.TractionCurveMin = g.Min;
                        }

                        _grip.Remove(key);
                    }
                }
            }
            catch (Exception ex)
            {
                // The old switch, if the handling cannot be reached on this build.
                Log.Debug("Takeover: could not touch the handling: " + ex.Message);
                try { Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, car.Handle, on); } catch { }
            }
        }

        /// <summary>
        /// Every model its grip back, whatever was counted, for the end of the night. By
        /// model rather than by car, because by the time the takeover packs up the cars
        /// may already be gone and the handling would stay as they left it.
        /// </summary>
        private void Regrip()
        {
            foreach (var pair in _grip)
            {
                try
                {
                    var h = HandlingData.GetByVehicleModel(new Model(pair.Key));
                    if (h == null || !h.IsValid) continue;

                    h.TractionCurveMax = pair.Value.Max;
                    h.TractionCurveMin = pair.Value.Min;
                }
                catch
                {
                    // Not on this build.
                }
            }

            _grip.Clear();
            _slick.Clear();
        }

        private sealed class Grip
        {
            public float Max;
            public float Min;
            public int Count;
        }

        private readonly Dictionary<int, Grip> _grip = new Dictionary<int, Grip>();
        private readonly HashSet<int> _slick = new HashSet<int>();

        /// <summary>
        /// What share of its grip a car keeps on the circle. Nought is ice; one is a road car.
        ///
        /// Two fifths, down from three. The donut comes from the tyres and nowhere else now
        /// -- the engine is the car's own -- so this is the figure that decides whether full
        /// lock and the throttle is a loop of wheelspin or a car pulling itself round.
        /// </summary>
        private const float SlickTraction = 0.42f;

        // ==================================================================
        // The kerb
        // ==================================================================

        /// <summary>
        /// The stations the parked cars play, taken in turn so neighbours differ. Every
        /// name is one the game has had since it came out or since the update that added
        /// the station.
        /// </summary>
        private static readonly string[] Stations =
        {
            "RADIO_03_HIPHOP_NEW",
            "RADIO_09_HIPHOP_OLD",
            "RADIO_20_THELAB",
            "RADIO_08_MEXICAN",
            "RADIO_14_DANCE_02",
            "RADIO_12_REGGAE",
            "RADIO_17_FUNK",
            "RADIO_15_MOTOWN"
        };

        private int _tuned;
        private int _blastedAt;

        /// <summary>A car that has parked turns its music up, on a station of its own.</summary>
        private void Blast(Parkee p)
        {
            if (p == null || p.Car == null || !p.Car.Exists()) return;

            p.Station = Stations[_tuned++ % Stations.Length];

            Tune(p);
        }

        /// <summary>
        /// Every few seconds, every parked car is put back on its station with the engine
        /// on. The driver getting out turns the engine and the radio off, and a car with no
        /// engine has no radio, so both are held on for as long as it is on the kerb.
        /// </summary>
        private void Blasting(int now)
        {
            if (now - _blastedAt < 5000) return;
            _blastedAt = now;

            foreach (var p in _parked)
            {
                if (p == null || p.Car == null || !p.Car.Exists()) continue;
                if (string.IsNullOrEmpty(p.Station)) continue;

                Tune(p);
            }
        }

        private static void Tune(Parkee p)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, p.Car.Handle, true, true, false);
                Function.Call(Hash.SET_VEHICLE_KEEP_ENGINE_ON_WHEN_ABANDONED, p.Car.Handle, true);
                Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, p.Car.Handle, true);
                Function.Call(Hash.SET_VEH_RADIO_STATION, p.Car.Handle, p.Station);
                Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, p.Car.Handle, true);
            }
            catch
            {
                // Quiet, then.
            }
        }

        // ==================================================================
        // The crowd and the cars
        // ==================================================================

        /// <summary>
        /// Two things the crowd does about cars.
        ///
        /// A CAR THAT IS NOT OURS IN THE CIRCLE gets the nearest few of the crowd on it:
        /// out of their idle, onto the driver, unarmed, for a while, and then home. Not the
        /// police, who are their own problem, and not the player.
        ///
        /// A CAR THAT IS OURS coming at somebody gets them out of its way: whoever is in
        /// front of a running car, or where it is about to be, steps sharply sideways off
        /// its line and walks back afterwards. The crowd used to stand there and be hit,
        /// because a crowd told to ignore everything ignores that too.
        /// </summary>
        private void Crowding(int now)
        {
            if (now - _crowdedAt < 250) return;
            _crowdedAt = now;

            // ---- out of the way ----
            foreach (var r in _running)
            {
                if (r.Car == null || !r.Car.Exists()) continue;

                var v = r.Car.Velocity;
                var speed = v.Length();
                if (speed < 4f) continue;

                var ahead = r.Car.Position + v * 0.6f;

                foreach (var w in _crowd)
                {
                    if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;
                    if (w.Dodge != 0 || w.Angry != 0 || w.Wreck != 0) continue;

                    var at = w.Man.Position;

                    if (at.DistanceTo(ahead) > DodgeRange && at.DistanceTo(r.Car.Position) > DodgeRange) continue;

                    // Sideways off the car's line, to whichever side they are already on.
                    var dir = Vector3.Normalize(new Vector3(v.X, v.Y, 0f));
                    var side = new Vector3(-dir.Y, dir.X, 0f);

                    if (Vector3.Dot(at - r.Car.Position, side) < 0f) side = side * -1f;

                    var to = at + side * DodgeStep;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                        Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, w.Man.Handle,
                                      to.X, to.Y, to.Z, 3.0f, DodgeMs, w.Man.Heading, 0.5f);
                    }
                    catch
                    {
                        // Then they are hit, as they were.
                    }

                    w.Dodge = now + DodgeMs;
                    w.Hype = 0;
                }
            }

            foreach (var w in _crowd)
            {
                if (w.Dodge != 0 && now >= w.Dodge)
                {
                    w.Dodge = 0;

                    // Somebody in the middle goes back to their spot in the middle, not home.
                    if (w.Rush != 0)
                    {
                        w.RushStage = 0;
                        Dash(w, now);
                    }
                    else
                    {
                        Home(w);
                    }
                }

                // ON THE DRIVER, and then ON THE CAR. Once the driver is dealt with -- dead,
                // out of it, gone -- or the temper's time is up, whoever was on him turns on
                // the car itself, if it is still there and has stopped: fifteen seconds of
                // kicks and punches from the sides and one of them up on the roof stomping.
                // A car that drove off is let go.
                if (w.Angry != 0)
                {
                    var car = w.Wrecking;
                    var here = car != null && car.Exists();
                    var driver = here ? car.Driver : null;
                    var dealt = !here || driver == null || !driver.Exists() || !driver.IsAlive;
                    var stopped = here && car.Speed < 1f;

                    if ((now >= w.Angry || dealt) && here && stopped)
                    {
                        w.Angry = 0;
                        StartWreck(w, car, now);
                    }
                    else if (now >= w.Angry || !here)
                    {
                        w.Angry = 0;
                        w.Wrecking = null;
                        Home(w);
                    }
                }

                if (w.Wreck != 0)
                {
                    if (now >= w.Wreck || w.Wrecking == null || !w.Wrecking.Exists())
                    {
                        w.Wreck = 0;
                        w.OnRoof = false;
                        w.Wrecking = null;

                        try { Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle); }
                        catch { /* home either way */ }

                        Home(w);
                    }
                    else
                    {
                        Hit(w, now);
                    }
                }
            }

            // ---- a car that is not ours ----
            Vehicle[] near;

            try
            {
                near = World.GetNearbyVehicles(Middle, Ring + 4f);
            }
            catch
            {
                return;
            }

            foreach (var car in near)
            {
                if (car == null || !car.Exists() || Ours(car)) continue;
                if (car.Position.DistanceTo(Middle) > Ring) continue;

                var driver = car.Driver;
                if (driver == null || !driver.Exists() || !driver.IsAlive) continue;
                if (driver.Handle == Game.Player.Character.Handle) continue;

                int kind;
                try { kind = Function.Call<int>(Hash.GET_PED_TYPE, driver.Handle); }
                catch { continue; }

                // Cops, SWAT and the army are their own problem.
                if (kind == 6 || kind == 27 || kind == 29) continue;

                int last;
                if (_angryAt.TryGetValue(car.Handle, out last) && now - last < AngryAgainMs) continue;
                _angryAt[car.Handle] = now;

                // The nearest few.
                var picked = new List<Watcher>();

                foreach (var w in _crowd)
                {
                    if (!Handy(w)) continue;
                    if (w.Man.Position.DistanceTo(car.Position) > AngryReach) continue;

                    picked.Add(w);
                }

                picked.Sort((x, y) => x.Man.Position.DistanceTo(car.Position)
                                       .CompareTo(y.Man.Position.DistanceTo(car.Position)));

                var sent = 0;

                foreach (var w in picked)
                {
                    if (sent >= AngryMany) break;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, w.Man.Handle, 5, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, w.Man.Handle, 46, true);
                        Function.Call(Hash.TASK_COMBAT_PED, w.Man.Handle, driver.Handle, 0, 16);
                    }
                    catch
                    {
                        continue;
                    }

                    w.Angry = now + AngryMs;
                    w.Wrecking = car;
                    w.Hype = 0;
                    sent++;
                }

                if (sent > 0)
                {
                    _beaten.Add(car.Handle);
                    Log.Info("Takeover: a car came into the circle; " + sent + " of the crowd are on the driver.");
                }
            }
        }

        private int _crowdedAt;
        private readonly Dictionary<int, int> _angryAt = new Dictionary<int, int>();

        private const float DodgeRange = 3.2f;
        private const float DodgeStep = 3.0f;
        private const int DodgeMs = 1500;

        private const float AngryReach = 30f;
        private const int AngryMany = 4;
        private const int AngryMs = 12000;
        private const int AngryAgainMs = 25000;

        private const string FireAsset = "scr_indep_fireworks";

        private static readonly string[] FireBursts =
        {
            "scr_indep_firework_starburst",
            "scr_indep_firework_trailburst",
            "scr_indep_firework_shotburst"
        };

        /// <summary>How often a volley goes, how many are in it, how they stagger, and how long they hold the flare.</summary>
        private const int FlareMinMs = 14000;
        private const int FlareMaxMs = 30000;
        private const int FlareThrowersMin = 1;
        private const int FlareThrowersMax = 2;
        private const int FlareStaggerMs = 900;
        private const int FlareHoldMs = 3200;

        /// <summary>One in five goes high; the rest go low across the middle.</summary>
        private const int FlareUpShare = 20;
        private const float FlareUpHigh = 70f;

        /// <summary>How often a box goes out, how long it takes to set, how many at once, and how big a box is.</summary>
        private const int FireMinMs = 9000;
        private const int FireMaxMs = 24000;
        private const int FireSetUpMs = 5200;
        private const int FireAtOnce = 2;
        private const int FireBurstsPerBox = 4;
        private const float FireRingMin = 12f;
        private const float FireRingMax = 16f;

        /// <summary>One flare on its way: who, where at, when they let go, and when it is taken back.</summary>
        private sealed class Thrown
        {
            public Watcher W;
            public Vector3 At;
            public int ThrowAt;
            public int Back;
            public bool Loosed;
        }

        /// <summary>One box being set: where, who is crouched over it, and when it goes.</summary>
        private sealed class Rocket
        {
            public int At;
            public Vector3 Spot;
            public Watcher Man;
            public Prop Prop;
        }

        private int _nextFlare;
        private readonly List<Thrown> _throws = new List<Thrown>();

        private int _nextFire;
        private readonly List<Rocket> _rockets = new List<Rocket>();

        // ---- the feed -----------------------------------------------------------

        /// <summary>The block says something about it while it is on.</summary>
        private void Chatter(int now)
        {
            if (Social == null || now < _nextWord) return;

            // HALFWAY IN, ON THE CLOCK THE NIGHT ACTUALLY ENDS ON.
            //
            // This waited fifteen REAL minutes, described in its own comment as "half of the
            // three hours it runs". Those three hours are GAME hours: at the game's own rate
            // that is about six real minutes end to end, so the gate was more than twice the
            // length of the whole event and the block has never once said a word about a
            // takeover. Every takeover post ever seen was the call-out.
            //
            // Asked of _endsAt instead -- the same clock the police are called on -- so
            // halfway is halfway whatever the clock is doing, including when somebody has
            // sped it up or slowed it down.
            //
            // The reason for waiting at all is unchanged: the block posting in the first
            // minute is the block reporting something it cannot have noticed yet, and it gave
            // the whole thing away before there was anything at the junction to see.
            if (_endsAt == 0) return;
            if (_endsAt - OwnedCars.NowMinutes() > (int)(LastsHours * 30f)) return;

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
        /// <summary>
        /// Call them, and KEEP calling them until they come.
        ///
        /// THIS RAN ONCE AND THAT IS WHY NO POLICE EVER ARRIVED. The log said it in four
        /// words -- "Takeover: 0 units on the way" -- and the cause is the non-blocking model
        /// loader meeting a one-shot.
        ///
        /// Asking for a model does not wait any more; it says "not yet" and fetches. Every
        /// other spawner in this file was taught to come back for that -- a kerb is put back on
        /// the queue rather than losing its car, a crowd wave that spawns nobody tries again
        /// on the next tick. Blues was not. It asked for police3 at the one moment it would
        /// ever ask, on a night where no police car had been anywhere near the junction for
        /// three hours, got "not yet" three times, wrote zero in the log and stood down.
        ///
        /// So it is two things now. The squad cars are warmed with the rest of the cast at the
        /// start of the night, which is the fix -- and this keeps asking for a minute either
        /// way, which is the thing that stops one bad moment ending the night quietly.
        /// </summary>
        private void Blues()
        {
            State = TakeoverState.Scattering;
            _lastDrive = Game.GameTime + 60000;

            _lawUntil = Game.GameTime + LawKeepAskingMs;
            _nextLaw = 0;

            MoreLaw(Game.GameTime);
        }

        /// <summary>
        /// Top the response up to strength, on a clock. Called every tick while they scatter.
        /// </summary>
        private void MoreLaw(int now)
        {
            if (_law.Count >= Units || now > _lawUntil) return;
            if (now < _nextLaw) return;

            _nextLaw = now + LawAgainMs;

            var had = _law.Count;

            for (var i = _law.Count; i < Units; i++)
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
                    var car = Make(Sirens, at, false);
                    if (car == null) continue;

                    var cop = Officer(car);

                    if (cop == null)
                    {
                        car.Delete();
                        continue;
                    }

                    Function.Call(Hash.SET_VEHICLE_SIREN, car.Handle, true);

                    // THEY PULL UP AT THE EDGE OF IT, NOT INTO THE MIDDLE OF IT.
                    //
                    // They were driven at the centre of the junction at twenty metres a second
                    // on the avoidance-only style -- no stopping before vehicles, no stopping
                    // before people -- and the centre of the junction is thirty parked cars
                    // and sixty people. So three squad cars came in at fifty and ploughed
                    // through the lot, which is a pile of wreckage rather than a raid.
                    //
                    // Police block a street. They come up it, stop across it, and get out --
                    // the scattering is caused by the lights and the noise, not by being run
                    // over. So the destination is a point out on the ring rather than the mark
                    // in the middle, worked out along the bearing they are already coming from
                    // so each one holds the street it arrived on.
                    var back = at - Middle;

                    var len = back.Length();

                    var stop = len < 1f ? at
                        : Middle + back * (LawHold / len);

                    var road = World.GetNextPositionOnStreet(stop, true);

                    if (road != Vector3.Zero) stop = road;

                    // CareStyle, which stops before vehicles and before people. It is the same
                    // style everything else at this junction drives on, minus the traffic
                    // lights, and there is no version of a police response worth watching that
                    // begins by killing eleven spectators.
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, cop.Handle, car.Handle,
                                  stop.X, stop.Y, stop.Z, LawSpeed, 0,
                                  car.Model.Hash, CareStyle, 5f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, cop.Handle, true);

                    _law.Add(new Law { Car = car, Cop = cop, Sent = now, Stop = stop });
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover: no police car: " + ex.Message);
                }
            }

            // Only when it changes, or a minute of retries is sixty identical lines.
            if (_law.Count == had) return;

            Log.Info("Takeover: " + _law.Count + " of " + Units + " units on the way.");
        }

        /// <summary>Until when the response is topped up, and how often it is tried.</summary>
        private int _lawUntil;
        private int _nextLaw;

        private const int LawKeepAskingMs = 60000;
        private const int LawAgainMs = 900;

        /// <summary>
        /// What turns up at the end of it.
        ///
        /// A FIELD RATHER THAN AN ARRAY BUILT AT THE CALL, and that is not tidiness. Warm asks
        /// the streamer for everything the night will need before any of it is wanted, and it
        /// can only ask for lists it can see -- these were written inline inside Blues, so
        /// they were the one cast member nobody warmed. See Blues for what that cost.
        /// </summary>
        private static readonly string[] Sirens = { "police3", "police", "police2" };

        /// <summary>
        /// Each unit, the rest of the way in.
        ///
        /// They used to stop at the edge and sit there with the lights going, which broke
        /// the takeover up from thirty-five metres away. A raid comes into the junction. So
        /// the moment the crowd is running -- the running is what clears the road ahead --
        /// every unit is sent on to ten metres from the middle, on a style that steers round
        /// cars, people and objects and stops only for a person straight in front of it. The
        /// order is given again every few seconds, because a drive task dropped by a swerve
        /// is a car sat in the road. There, or stood still behind something for long enough,
        /// or out of time, it brakes and the officer gets out and stands by the car. Every
        /// step is in the log with the distance from the middle, so "far away" is a number.
        /// </summary>
        private void Raid(int now)
        {
            var n = 0;

            foreach (var l in _law)
            {
                n++;

                if (l.Car == null || !l.Car.Exists()) continue;
                if (l.Cop == null || !l.Cop.Exists() || !l.Cop.IsAlive) continue;

                try
                {
                    var from = l.Car.Position.DistanceTo(Middle);

                    if (l.Stage == 0)
                    {
                        // Sent on the moment everybody is running. Not before: a squad car
                        // driven into a standing crowd on a style that stops for people is a
                        // squad car parked in a crowd.
                        if (!_scattered) continue;

                        var back = l.Car.Position - Middle;
                        var len = back.Length();

                        l.Stop = len <= LawClose + 1f ? l.Car.Position : Middle + back * (LawClose / len);
                        l.Stage = 1;
                        l.Sent = now;
                        l.Poked = 0;
                        l.Still = 0;

                        Log.Info("Takeover: unit " + n + " sent into the junction from " + from.ToString("0") + " m out.");
                    }

                    if (l.Stage == 1)
                    {
                        var gap = l.Car.Position.DistanceTo(l.Stop);
                        var still = Function.Call<bool>(Hash.IS_VEHICLE_STOPPED, l.Car.Handle);

                        if (!still) l.Still = 0;
                        else if (l.Still == 0) l.Still = now;

                        var there = gap <= LawThere;
                        var stuck = l.Still != 0 && now - l.Still > LawStuckMs;
                        var late = now - l.Sent >= LawLegMs;

                        if (!there && !stuck && !late)
                        {
                            // The order, given again every few seconds. Quick until it is near
                            // the junction, a creep from there.
                            if (now - l.Poked >= LawPokeMs)
                            {
                                l.Poked = now;

                                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, l.Cop.Handle, l.Car.Handle,
                                              l.Stop.X, l.Stop.Y, l.Stop.Z, from > 40f ? LawSpeed : LawCreep, 0,
                                              l.Car.Model.Hash, RaidStyle, 3f, true);
                                Function.Call(Hash.SET_PED_KEEP_TASK, l.Cop.Handle, true);
                            }

                            continue;
                        }

                        // Temp action 1 is the brake.
                        Function.Call(Hash.CLEAR_PED_TASKS, l.Cop.Handle);
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, l.Cop.Handle, l.Car.Handle, 1, 2000);

                        Log.Info("Takeover: unit " + n + " stops " + from.ToString("0") + " m from the middle -- " +
                                 (there ? "there." : stuck ? "stood behind something." : "out of time."));

                        l.Stage = 2;
                        l.Sent = now;
                        continue;
                    }

                    if (l.Stage == 2)
                    {
                        if (now - l.Sent < 2200) continue;

                        Function.Call(Hash.TASK_LEAVE_VEHICLE, l.Cop.Handle, l.Car.Handle, 0);

                        l.Stage = 3;
                        l.Sent = now;
                        continue;
                    }

                    if (l.Stage == 3)
                    {
                        if (now - l.Sent < 3500) continue;

                        // Still in it after that long is a door that will not open; he stays put.
                        if (Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, l.Cop.Handle, false))
                        {
                            if (now - l.Sent < 9000) continue;

                            l.Stage = 4;
                            continue;
                        }

                        Function.Call(Hash.SET_ENTITY_HEADING, l.Cop.Handle, Facing(l.Cop.Position));
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, l.Cop.Handle,
                                      "WORLD_HUMAN_COP_IDLES", 0, true);

                        l.Stage = 4;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover: a unit lost its way in: " + ex.Message);
                    l.Stage = 4;
                }
            }
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
        /// <summary>
        /// Drivers running back to their cars, and driven off the moment they are in one.
        ///
        /// THE SECOND HALF OF SCATTER, and it has to be a tick rather than a second task queued
        /// behind the first. Getting into a car takes as long as it takes -- the run, the door,
        /// the animation -- and a drive order issued in the same breath cancels the getting in
        /// that had not happened yet, which leaves a man stood at an open door.
        ///
        /// Given up on after a while. A driver who cannot reach his car because it is on its
        /// roof or somebody is in it should not stand in the road for the rest of the night;
        /// he legs it with everybody else.
        /// </summary>
        private void Bail(int now)
        {
            foreach (var p in _parked)
            {
                if (!p.Bailing) continue;
                if (p.Car == null || !p.Car.Exists()) { p.Bailing = false; continue; }
                if (p.Driver == null || !p.Driver.Exists() || !p.Driver.IsAlive)
                {
                    p.Bailing = false;
                    continue;
                }

                try
                {
                    var inside = Function.Call<bool>(Hash.IS_PED_IN_VEHICLE,
                                                     p.Driver.Handle, p.Car.Handle, false);

                    if (!inside)
                    {
                        if (_scatteredAt != 0 && now - _scatteredAt > GetInGiveUpMs)
                        {
                            p.Bailing = false;

                            Function.Call(Hash.CLEAR_PED_TASKS, p.Driver.Handle);
                            Function.Call(Hash.TASK_SMART_FLEE_COORD, p.Driver.Handle,
                                          Middle.X, Middle.Y, Middle.Z, 240f, -1, false, false);
                        }

                        continue;
                    }

                    // In and waiting his turn to pull out. See OffAt.
                    if (p.OffAt != 0 && now < p.OffAt) continue;

                    p.Bailing = false;
                    p.Outside = false;

                    var off = OnRoad(200f + (float)_rng.NextDouble() * 150f);
                    if (off == Vector3.Zero) off = Middle.Around(250f);

                    Function.Call(Hash.CLEAR_PED_TASKS, p.Driver.Handle);

                    // MAXIMUM AGGRESSION WITH DEFAULT ABILITY IS A BAD DRIVER IN A HURRY, and
                    // that is precisely what this was: aggressiveness pinned at one, ability
                    // never set at all, twenty-eight metres a second, on the style that does
                    // not stop for cars or people. Thirty of those in one junction is not
                    // people leaving, it is a demolition derby with a reason.
                    //
                    // The two knobs go the other way round. Ability high, so they can actually
                    // place a car; aggression a bit over half, so they are hurrying rather than
                    // suicidal. A frightened driver who can drive gets out of a tight street
                    // faster than a reckless one who cannot.
                    Function.Call(Hash.SET_DRIVER_ABILITY, p.Driver.Handle, 1.0f);
                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, p.Driver.Handle, LeaveNerve);

                    // And on the style that stops before cars and before people. They are
                    // running from the police, not through the crowd they were stood in.
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, p.Driver.Handle, p.Car.Handle,
                                  off.X, off.Y, off.Z, LeaveSpeed, 0, p.Car.Model.Hash,
                                  CareStyle, 15f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, p.Driver.Handle, true);
                }
                catch
                {
                    // Next tick.
                }
            }
        }

        /// <summary>How long a driver gets to reach his car before he runs for it instead.</summary>
        private const int GetInGiveUpMs = 20000;

        /// <summary>
        /// How they leave: over eight seconds, at sixty-five, hurrying rather than raging.
        ///
        /// Eighteen metres a second is still quick out of a side street and it is a long way
        /// short of the hundred they were doing. The speed was never the thing that made it
        /// look urgent -- the sirens are -- and it was the thing that made every one of them
        /// arrive at the same corner at once with no time to do anything about it.
        /// </summary>
        private const int LeaveSpreadMs = 8000;
        private const float LeaveSpeed = 18f;
        private const float LeaveNerve = 0.55f;

        /// <summary>When the police turned up, for Bail's patience.</summary>
        private int _scatteredAt;

        private void Scatter()
        {
            _scatteredAt = Game.GameTime;

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
                    for (var wheel = 0; wheel < 4; wheel++)
                    {
                        Function.Call(Hash.SET_HYDRAULIC_SUSPENSION_RAISE_FACTOR, p.Car.Handle, wheel, 0f);
                    }

                    if (p.Driver == null || !p.Driver.Exists()) continue;

                    // HE IS STOOD NEXT TO IT, so first he has to get back in.
                    //
                    // Two tasks rather than one. Handing a drive order to a man on the pavement
                    // is asking the game to work out the whole of getting in on its own, and
                    // what it does with that varies by how far away he is, which door is clear
                    // and what he was doing -- often nothing at all. Told to get in, and then
                    // told to drive once he is in, by Bail.
                    if (p.Outside)
                    {
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p.Driver.Handle, false);
                        Function.Call(Hash.CLEAR_PED_TASKS, p.Driver.Handle);

                        // Seat -1 is the driver's, and 2.0 is a run. Nobody walks to their car
                        // when the lights come round the corner.
                        Function.Call(Hash.TASK_ENTER_VEHICLE, p.Driver.Handle, p.Car.Handle,
                                      20000, -1, 2.0f, 1, 0);

                        // AND THEY DO NOT ALL PULL OUT AT ONCE.
                        //
                        // Getting in takes as long as the run to the car takes, which staggers
                        // them a little and not nearly enough -- thirty cars parked round one
                        // junction are all within a few seconds of each other, so thirty
                        // drivers finished getting in inside about two seconds and thirty cars
                        // pulled out into the same road. That is the heap.
                        //
                        // Eight seconds of spread on top. Everybody is still leaving in a
                        // hurry; they are leaving in a hurry one after another, which is what
                        // a car park emptying looks like.
                        p.OffAt = Game.GameTime + _rng.Next(LeaveSpreadMs);

                        p.Bailing = true;
                        continue;
                    }

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
        /// <summary>
        /// A competitor's car: one of the regulars while there are any left, then the field.
        ///
        /// Then the two things that are true of every car that competes and of nothing else at
        /// the junction: competition springs under it, and no livery on it.
        /// </summary>
        private Vehicle Contender(Vector3 from)
        {
            Vehicle car = null;

            if (_headed < Headliners.Length)
            {
                var one = Headliners[_headed];
                _headed++;

                car = Make(one.Models, from, true, false);

                if (car != null && one.Paint >= 0)
                {
                    try { Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, one.Paint, one.Paint); }
                    catch { /* it keeps whatever Dress gave it */ }
                }
            }

            if (car == null) car = Make(Drifters, from, true, false);
            if (car == null) return null;

            Competing(car);

            return car;
        }

        /// <summary>
        /// What is true of a car that competes and of nothing else here.
        ///
        /// COMPETITION SPRINGS, ASKED FOR BY NAME. The suspension slot was being set to index
        /// three, which is Competition on some cars, Race on others and nothing at all on the
        /// ones with a shorter list -- an index is a guess about a menu that differs per model.
        /// The shop's own label for the part is the only thing that actually says which is
        /// which, so it is read, and the lowest the car has is the fallback.
        ///
        /// AND NO LIVERY, AFTER EVERYTHING. Dress takes them off already, but this runs after
        /// the paint has been forced on a regular and after every body part is on -- and on
        /// the models that keep a livery in a mod slot, fitting a part is what hands one back.
        /// Both spellings and the roof one, because a car has whichever it has.
        /// </summary>
        private void Competing(Vehicle car)
        {
            if (car == null || !car.Exists()) return;

            var h = car.Handle;

            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);

                var many = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, 15);

                if (many > 0)
                {
                    var picked = -1;

                    for (var i = 0; i < many; i++)
                    {
                        var label = Function.Call<string>(Hash.GET_MOD_TEXT_LABEL, h, 15, i);
                        if (string.IsNullOrEmpty(label)) continue;
                        if (!Function.Call<bool>(Hash.DOES_TEXT_LABEL_EXIST, label)) continue;

                        var text = Function.Call<string>(Hash.GET_FILENAME_FOR_AUDIO_CONVERSATION, label);
                        if (string.IsNullOrEmpty(text)) continue;

                        if (text.IndexOf("Competition", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        picked = i;
                        break;
                    }

                    Function.Call(Hash.SET_VEHICLE_MOD, h, 15, picked >= 0 ? picked : many - 1, false);
                }
            }
            catch
            {
                // It rides on whatever it came with.
            }

            try
            {
                Function.Call(Hash.SET_VEHICLE_LIVERY, h, -1);
                Function.Call(Hash.SET_VEHICLE_LIVERY2, h, -1);
                Function.Call(Hash.SET_VEHICLE_MOD, h, 48, -1, false);
            }
            catch
            {
                // Nothing this game has to say about liveries applies to this model.
            }
        }

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

                        if (!Core.Models.Ready(model)) continue;

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

                // NO SPOILER. A big wing is a track car; these are street cars thrown
                // sideways at a junction on a Tuesday. Taken off rather than not put on,
                // because a few models ship with one fitted by default.
                Function.Call(Hash.SET_VEHICLE_MOD, h, 0, -1, false);

                // And the rest of the bodywork, from what this model owns.
                Fit(h, 1, false);    // front bumper
                Fit(h, 2, false);    // rear bumper
                Fit(h, 3, false);    // skirts
                Fit(h, 4, false);    // exhaust
                Fit(h, 6, false);    // grille
                Fit(h, 7, false);    // bonnet
                Fit(h, 10, false);   // roof

                // NO COLOURED SMOKE AND NO COLOURED HEADLIGHTS. On any of them.
                //
                // Both are things you only see when a car is working, which is exactly where
                // they are wrong: purple smoke off a car mid-donut turns a street takeover
                // into a light show, and blue headlights sweeping the crowd every time it
                // comes round does the same job in the same way. That reasoning was applied
                // to the drift cars and then not followed through -- a spectator burning out
                // as it pulls away, a donk lighting up as it leaves, and the row of parked
                // cars pointing pink and green at the crowd all night are the same wrong
                // picture in smaller frames.
                //
                // WHITE XENONS STAY, and they are a different thing from coloured ones. The
                // bulb upgrade is a part somebody bought; the colour index is a novelty on
                // top of it. Dropping the index rather than setting it to a white value
                // matters for the same reason the smoke toggle is left off rather than set
                // to white: no part at all is a car that never had one.
                if (showy)
                {
                    Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, 22, true);
                }

                // NEON ON A FEW OF THEM, NOT ON ALL OF THEM.
                //
                // Everybody used to get it, on the reasoning that underglow belongs at one of
                // these. It does -- and that is exactly why every car having it is wrong: a
                // thing everybody has is not a statement, it is lighting. Thirty-five glowing
                // cars round a junction stop reading as somebody's build and start reading as
                // a fairground, and the two or three that would actually have spent the money
                // are lost in it.
                //
                // A quarter. Enough that there is always some of it on the street and enough
                // that a car with it stands out from the ones without.
                if (_rng.Next(100) < NeonChance)
                {
                    var neon = Neons[_rng.Next(Neons.Length)];

                    for (var side = 0; side < 4; side++)
                    {
                        Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, h, side, true);
                    }

                    Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, h, neon[0], neon[1], neon[2]);
                }

                // THE SET'S, MOSTLY. Two in three wear the Families' plates, because this is
                // their junction and their cars; the rest keep the street's own -- SIDEWYS,
                // NO GRIP -- which is what the builds that came from elsewhere would wear.
                var setPlate = _rng.Next(3) < 2 ? Gangs.Plates.Pick("families", _rng) : null;

                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, h,
                              setPlate ?? StreetPlates[_rng.Next(StreetPlates.Length)]);

                // AND NO LIVERY, LAST OF ALL.
                //
                // A livery is a paint scheme somebody ordered from a catalogue, which is not
                // what any of these are. It was being cleared before the bodywork went on,
                // and on the models that carry a livery in a mod slot rather than the old
                // livery slot, fitting a body part re-reads the kit and can hand one back --
                // so the cars this was written for were the ones still wearing them.
                //
                // Both spellings, because the older models keep liveries in their own slot and
                // the newer ones keep them as mod 48, and a car has one or the other.
                Function.Call(Hash.SET_VEHICLE_LIVERY, h, -1);
                Function.Call(Hash.SET_VEHICLE_MOD, h, 48, -1, false);
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

        /// <summary>How many of them have underglow, out of a hundred. See Dress.</summary>
        private const int NeonChance = 25;

        /// <summary>Paints, wheel sets, tints, and plates.</summary>
        private static readonly int[] Paints =
        {
            0, 1, 2, 3, 4, 12, 27, 28, 38, 49, 52, 55, 64, 70, 73, 88, 89, 92,
            111, 112, 117, 120, 125, 132, 134, 141, 142, 145, 150
        };

        private static readonly int[] Wheels = { 0, 1, 2, 5, 7, 11 };
        private static readonly int[] Tints = { 1, 2, 3, 5 };
        private const int Rims = 156;

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

        private static readonly string[] StreetPlates =
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
        /// <summary>
        /// Somebody with a badge, for a car with a light bar on it.
        ///
        /// THE SQUAD CARS WERE BEING DRIVEN BY THE CROWD. Behind picks its model out of Faces,
        /// which is where the spectators come from -- gang members and people off the block --
        /// so the police arriving to end the takeover were three men in vests and jerseys
        /// sitting in marked cars. None of them was a police officer in any sense the game
        /// understands either: wrong model, wrong ped type, wrong relationship group, no
        /// sidearm.
        ///
        /// Made as a COP rather than dressed as one. The ped type is what makes the rest of the
        /// game treat him as police -- dispatch, relationship groups, and what everybody at the
        /// junction thinks is about to happen. A uniform on a civilian ped is a costume.
        /// </summary>
        private Ped Officer(Vehicle car)
        {
            foreach (var name in Badges)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !Core.Models.Ready(model)) continue;

                    // Ped type 6 is COP.
                    var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, car.Handle,
                                                    6, model.Hash, -1, false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;

                    var h = ped.Handle;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                    Function.Call(Hash.SET_PED_AS_COP, h, true);

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, h,
                                  Function.Call<int>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"), 60, false, true);

                    Function.Call(Hash.SET_PED_ACCURACY, h, 35);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);

                    Function.Call(Hash.SET_DRIVER_ABILITY, h, 1.0f);
                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, h, 0.4f);

                    // And somebody riding with him. One man in a patrol car is a driver; two is
                    // a unit, which is what turns up to one of these.
                    Mate(car, name);

                    return ped;
                }
                catch
                {
                    // Try the next uniform.
                }
            }

            return null;
        }

        /// <summary>His partner in the passenger seat. Missing one is not worth failing over.</summary>
        private void Mate(Vehicle car, string name)
        {
            try
            {
                var model = new Model(name);
                if (!model.IsValid || !Core.Models.Ready(model)) return;

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, car.Handle,
                                                6, model.Hash, 0, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return;

                var mate = Entity.FromHandle(handle) as Ped;
                if (mate == null || !mate.Exists()) return;

                mate.IsPersistent = true;

                var h = mate.Handle;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_PED_AS_COP, h, true);

                Function.Call(Hash.GIVE_WEAPON_TO_PED, h,
                              Function.Call<int>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"), 60, false, true);

                Function.Call(Hash.SET_PED_ACCURACY, h, 35);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);

                _extras.Add(mate);
            }
            catch
            {
                // He rides alone.
            }
        }

        /// <summary>The uniforms, best first. An install without one quietly gets the next.</summary>
        private static readonly string[] Badges =
        {
            "s_m_y_cop_01", "s_f_y_cop_01", "s_m_y_sheriff_01", "s_f_y_sheriff_01",
            "s_m_y_hwaycop_01"
        };

        /// <summary>Passengers, held so they are cleared up with everything else.</summary>
        private readonly List<Ped> _extras = new List<Ped>();

        private Ped Behind(Vehicle car)
        {
            try
            {
                var name = Faces[_rng.Next(Faces.Length)];

                var model = new Model(name);
                if (!model.IsValid || !Core.Models.Ready(model)) return null;

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
        /// <summary>
        /// Where a car heading for a kerb should actually steer for, so it does not cut
        /// across the middle to get there.
        ///
        /// THE JUNCTION IS THE SHORTEST ROUTE BETWEEN ANY TWO KERBS ON IT, which is the whole
        /// problem. A car sent straight to a spot on the far side takes the road nodes through
        /// the centre -- through the donut, past the burnout, and out the other side. It is
        /// the fastest way there and it is the one place it must not go.
        ///
        /// SET_ROADS_IN_AREA is not the answer and was tried: switching the junction's nodes
        /// off breaks OUR pathing too, and the cars that are supposed to arrive then cannot.
        ///
        /// So it is a waypoint. If the straight line to the spot passes inside the no-go
        /// circle, steer for a point radially OUTSIDE the spot instead -- up its own street,
        /// away from the junction. That sends the car round the outside, and once it is round,
        /// the line from where it now is to its spot no longer crosses the middle, so this
        /// returns the spot itself and it comes in off the kerb.
        ///
        /// One test per car rather than a route: the answer changes exactly once, when it has
        /// got round, which is what the re-task in Parking watches for.
        /// </summary>
        /// <summary>
        /// The car put exactly on its walked place, pointing exactly the way it was walked.
        ///
        /// THE PLACES ARE THE RULE. A driver stops short of a kerb, or a metre into the
        /// road, or across a corner, and a ring of thirty cars stopping "near enough" is a
        /// street with cars in it. The eighteen places were walked one screenshot at a time
        /// and the instruction was nowhere else -- so a car that has got to within a few
        /// metres of its place is set on it, still, the way a car parked there sits. The
        /// snap is a shuffle of a metre or two on a car that has already stopped, which is
        /// a smaller wrong thing than a car in the road all night.
        /// </summary>
        private static void Snap(Vehicle car, Vector3 at, float face)
        {
            if (car == null || !car.Exists()) return;

            try
            {
                Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, car.Handle, 0f);
                Function.Call(Hash.SET_ENTITY_COORDS, car.Handle, at.X, at.Y, at.Z, false, false, false, true);
                Function.Call(Hash.SET_ENTITY_HEADING, car.Handle, face);
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
            }
            catch
            {
            }
        }

        private Vector3 Toward(Vector3 from, Vector3 spot)
        {
            // A TARGET INSIDE THE ZONE HAS NOTHING TO ROUTE AROUND. The staging marks sit on
            // the edge of the circle on purpose, and one of them is inside the no-go radius --
            // without this, the answer for it would be "go round" from every position
            // including the waypoint, and the car would circle the junction for ever.
            if ((spot - Middle).Length() < NoGo + 1f) return spot;

            if (!Crosses(from, spot)) return spot;

            var out_ = spot - Middle;
            var len = out_.Length();

            if (len < 0.5f) return spot;

            out_ = out_ * (1f / len);

            return Middle + out_ * (len + SwingOut);
        }

        /// <summary>
        /// Whether driving straight from one point to the other would go through the middle.
        ///
        /// Point-to-SEGMENT, not point-to-line. A car parked well off to one side is not
        /// "about to drive through the junction" merely because the infinite line through it
        /// and its spot happens to pass near the mark -- the bit of that line it will actually
        /// drive is what matters, and clamping t to 0..1 is the difference.
        /// </summary>
        private bool Crosses(Vector3 from, Vector3 to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;

            var len2 = dx * dx + dy * dy;
            if (len2 < 0.01f) return false;

            var t = ((Middle.X - from.X) * dx + (Middle.Y - from.Y) * dy) / len2;

            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            var nx = from.X + dx * t - Middle.X;
            var ny = from.Y + dy * t - Middle.Y;

            return nx * nx + ny * ny < NoGo * NoGo;
        }

        /// <summary>
        /// How far around the mark nobody drives on their way to a kerb, in metres.
        ///
        /// Seventeen, which sits just inside the ring the crowd stands on at nineteen and
        /// comfortably inside the nearest kerb at 18.8 -- so a spot is never itself inside the
        /// zone that would send a car round to reach it.
        /// </summary>
        private const float NoGo = 17f;

        /// <summary>How far past a kerb the go-round waypoint sits, up its own street.</summary>
        private const float SwingOut = 20f;

        private float Facing(Vector3 from)
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
                    Slick(r.Car, false);
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
        /// Turn the city's own traffic down while the junction is full of ours.
        ///
        /// THE LOG COUNTED A HUNDRED AND THIRTEEN CARS DRIVEN INTO IT AND DELETED. That is the
        /// game doing exactly what it is supposed to -- there is a junction there, the road
        /// network says cars go through it, so it keeps making cars and sending them in, and
        /// Sweep keeps taking them out again. Every one of those is a vehicle and a driver
        /// created and destroyed, on top of the sixty this thing already put there, for as long
        /// as the takeover lasts.
        ///
        /// SET_ROADS_IN_AREA IS STILL NOT THE ANSWER, and Roads below explains why: switching
        /// the nodes off breaks OUR pathing too and the cars that are meant to arrive then
        /// cannot. That was tried and it is why Roads is only ever called with true.
        ///
        /// The density multipliers are the other end of the same problem. They do not touch the
        /// road network at all -- every route still exists and our cars still drive it -- they
        /// only tell the population system to stop MAKING new ones. Traffic already on the road
        /// carries on and is swept as before; what stops is the queue behind it.
        ///
        /// PER FRAME, BECAUSE THAT IS THE ONLY WAY THEY EXIST. They are THIS_FRAME natives:
        /// set once, they last one frame and the tap opens again. And they are global rather
        /// than area-scoped, which is why this is fenced by distance -- turning the whole
        /// city's traffic off because something is happening on Carson is a fix worse than the
        /// fault. Near enough to see it, and no further.
        /// </summary>
        private void Quieter()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                if (me.Position.DistanceToSquared(Middle) > QuietWithin * QuietWithin) return;

                // Not zero. Zero is an empty city, which reads as a bug the moment you look up
                // the street -- and the ones already driving still have to come from somewhere
                // or the block outside the cordon dies too.
                Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, QuietTraffic);
                Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, QuietTraffic);
                Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, QuietParked);

                // People are left nearly alone. A street takeover with nobody watching it is
                // the thing this whole file exists to avoid, and a pedestrian costs a fraction
                // of what a car and its driver cost.
                Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, QuietPeople);
            }
            catch
            {
                // The tap stays open. It is a frame.
            }
        }

        /// <summary>How near the junction the damper applies, and how far down it goes.</summary>
        private const float QuietWithin = 180f;
        private const float QuietTraffic = 0.15f;
        private const float QuietParked = 0.3f;
        private const float QuietPeople = 0.8f;

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
            Regrip();

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

            // The bikes, and whoever is still on one. A rider who got off is in the crowd
            // and goes with it.
            foreach (var r in _riders)
            {
                Stop(r);

                try { Loose(r.Bike, r.Man); }
                catch { /* Already gone. */ }
            }

            _riders.Clear();
            _ridersSent = false;
            _ridersAt = 0;

            foreach (var p in _parked)
            {
                try { Loose(p.Car, p.Driver); }
                catch { /* Already gone. */ }
            }

            _parked.Clear();
            _turned.Clear();

            // The helicopter is deleted rather than released. A police maverick handed back to
            // the game with its searchlight on circles the neighbourhood for the rest of the
            // night, and there is nothing in the world that would ever turn it off.
            Away();
            _heliDone = false;

            // And the firework box, which is ours and would otherwise sit in the road for the
            // rest of the session with nobody left who knows what it is.
            foreach (var r in _rockets)
            {
                try { if (r.Prop != null && r.Prop.Exists()) r.Prop.Delete(); }
                catch { /* it is gone */ }
            }

            _rockets.Clear();
            _throws.Clear();
            _nextHype = 0;
            _volleyed = 0;
            _volleyLogged = 0;
            _nobodyAt = 0;
            _angryAt.Clear();
            _letIn.Clear();
            _beaten.Clear();
            _nextRush = 0;
            _tuned = 0;

            // Where they ended up, for the log, before they are handed back.
            if (_law.Count > 0)
            {
                var ended = new List<string>();

                foreach (var l in _law)
                {
                    try
                    {
                        if (l.Car != null && l.Car.Exists()) ended.Add(l.Car.Position.DistanceTo(Middle).ToString("0") + " m");
                    }
                    catch
                    {
                        // Gone.
                    }
                }

                if (ended.Count > 0) Log.Info("Takeover: the units ended " + string.Join(", ", ended.ToArray()) + " from the middle.");
            }

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

            // The partners riding with them go too. They are not on the Law list -- that
            // pairs one car with one driver -- so without this a squad car's passenger outlives
            // the takeover that sent him.
            foreach (var e in _extras)
            {
                try { if (e != null && e.Exists()) e.Delete(); }
                catch { /* already gone */ }
            }

            _extras.Clear();

            _law.Clear();

            _coming.Clear();

            _scattered = false;
            _toCome = 0;

            State = TakeoverState.None;
        }

        public void RestoreWorld()
        {
            Regrip();

            try
            {
                foreach (var w in _crowd) { if (w.Man != null && w.Man.Exists()) w.Man.Delete(); }

                foreach (var p in _parked)
                {
                    if (p.Driver != null && p.Driver.Exists()) p.Driver.Delete();
                    if (p.Car != null && p.Car.Exists()) p.Car.Delete();
                }

                foreach (var r in _riders)
                {
                    if (r.Phone != null && r.Phone.Exists()) r.Phone.Delete();
                    if (r.Man != null && r.Man.Exists()) r.Man.Delete();
                    if (r.Bike != null && r.Bike.Exists()) r.Bike.Delete();
                }

                foreach (var r in _running)
                {
                    if (r.Driver != null && r.Driver.Exists()) r.Driver.Delete();
                    if (r.Car != null && r.Car.Exists()) r.Car.Delete();
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
            // The partners riding with them go too. They are not on the Law list -- that
            // pairs one car with one driver -- so without this a squad car's passenger outlives
            // the takeover that sent him.
            foreach (var e in _extras)
            {
                try { if (e != null && e.Exists()) e.Delete(); }
                catch { /* already gone */ }
            }

            _extras.Clear();

            _law.Clear();
            _ghosts.Clear();

            _scattered = false;

            State = TakeoverState.None;
        }
    }
}
