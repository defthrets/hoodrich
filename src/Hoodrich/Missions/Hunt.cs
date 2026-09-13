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
    /// <summary>Where a hunt is up to.</summary>
    internal enum HuntPhase
    {
        None = 0,

        /// <summary>Driving out to the ground with Lamar in the passenger seat.</summary>
        Riding,

        /// <summary>On foot, following tracks, looking for the next one.</summary>
        Tracking,

        /// <summary>Three down. Back to the car.</summary>
        Leaving
    }

    /// <summary>
    /// THE HUNT.
    ///
    /// FOUR MEN ON FOUR CORNERS AND A KNIFE. Franklin and Lamar work a list: four Ballas who
    /// stand about on four fixed corners of Davis and Rancho, taken in order, ending on Grove
    /// Street. You need THREE of them. The fourth is the spare, and what the spare is for is
    /// the one you get wrong.
    ///
    ///   THE CORNERS ARE REAL PLACES. See Stands. They were rolled at random inside one
    ///   fifty-five metre field until now, which produced three men in a car park -- no route,
    ///   no reason to be anywhere, nothing to learn between one hunt and the next. Fixed spots
    ///   mean the drive between them is part of the job and the second playthrough is faster
    ///   than the first.
    ///
    ///   ONE AT A TIME, AND YOU ARE TOLD WHICH. Only the next man is ringed on the map and
    ///   only his street is named on the card. He is put out when you get near his corner and
    ///   not before, so nothing appears in front of you.
    ///
    ///   HE LOITERS. Walk thirty metres, stand about scrolling his phone or smoking or
    ///   drinking for a while, walk somewhere else. A man rooted to one scenario is a puzzle
    ///   with one answer; a man who keeps turning round is a stalk. Every step of it is a
    ///   fresh print on the ground.
    ///
    ///   THE TRACKS. He has a trail of prints from where he walked in to where he is now,
    ///   oldest faintest, laid only when you are near enough to be reading the ground. Stand
    ///   on it and you have picked him up: the ring comes off the map and a mark goes on the
    ///   man.
    ///
    ///   THE STEALTH. Crouched is quiet, walking is not much worse, sprinting is a man
    ///   arriving, and a clear line of sight is worse than all of it. One number per man.
    ///
    ///   THE KILL IS A KNIFE. Get behind one who has not noticed you and the card offers a
    ///   takedown: a hand over the mouth, the blade in, and NOBODY ELSE HEARS IT -- the dead
    ///   body events are switched off on these four, so a clean one costs you nothing at all.
    ///   The rifle is in the bag and it is the wrong answer: a shot is a shot and the street
    ///   is a street.
    ///
    ///   FILL HIS NUMBER AND HE TURNS ROUND. He does not run any more and it does not fail
    ///   the job any more -- he comes at you and tries to kill you, and he stops counting.
    ///   That is the entire cost of blowing one, and with three needed out of four you can
    ///   afford it exactly once.
    ///
    ///   AND GROVE STREET IS LAST. It is the only one of the four that is somebody's block
    ///   rather than somebody's corner. Settle him and whoever is stood on that street works
    ///   it out, and the job ends with a run back to Lamar's yard rather than a fourth quiet
    ///   exit. Nobody is spawned to make that happen -- if the block is empty at four in the
    ///   morning then it is empty and you walk home.
    ///
    /// NOBODY IN THIS JOB IS EVER TASKED TWICE ON ONE FRAME. The first version of it sent
    /// Lamar a fresh follow task every frame, which is a man who never finishes starting to
    /// walk. Every task here is issued once, on a change, or on a clock.
    ///
    /// The four have their permanent events blocked, which is what stops the game's own gang
    /// hatred turning a stalk into a shootout the moment one of them glances at a Families
    /// man. Only this job moves them -- until one of them notices you, and then the block
    /// comes off and the game can have him back.
        /// </summary>
    internal sealed class Hunt
    {
        // ======================================================================
        // Measures
        // ======================================================================

        /// <summary>
        /// The four of them, where they stand, and how many you have to get.
        ///
        /// FIXED SPOTS IN A FIXED ORDER, which is the whole shape of the job now. They used to
        /// be rolled into one fifty-five metre field on whatever pavement the dice found, and
        /// what that produced was three men in a car park: no route, no reason to be anywhere,
        /// and nothing to learn between one hunt and the next. These are four corners of
        /// Davis and Rancho that somebody stood on and wrote down, and you work them in order.
        ///
        /// GROVE STREET IS LAST AND THAT IS NOT ARBITRARY. It is the only one of the four
        /// that is somebody's block rather than somebody's corner, so it is the only one where
        /// killing a man in the street has anybody to answer to -- see Grove. Doing it last
        /// means the escape is the end of the job instead of the middle of it.
        ///
        /// THREE OF FOUR. One blown stalk is survivable and two are not, which is the
        /// difference between a job you can play and a job you can only replay.
        /// </summary>
        private static readonly Stand[] Stands =
        {
            new Stand(233.930f, -1741.552f, 29.175f, "BROUGE AVE"),
            new Stand(309.951f, -1713.605f, 29.287f, "RANCHO"),
            new Stand(225.839f, -1847.848f, 26.986f, "CARSON AVE"),
            new Stand(89.797f, -1962.165f, 20.747f, "GROVE ST"),
        };

        private static int Many => Stands.Length;
        private const int Need = 3;

        /// <summary>The last one is the one with a block attached to it. See Grove.</summary>
        private static int Last => Stands.Length - 1;

        /// <summary>How far he wanders off his spot. Thirty metres, which is what was asked for.</summary>
        private const float Beat = 30f;

        /// <summary>How far out the ground itself is, from where he takes the job.</summary>
        private const float FieldRadius = 40f;

        /// <summary>Near enough to the ground to put the men down, and how long that is given.</summary>
        private const float LayFrom = 150f;
        private const int LayPatienceMs = 25000;

        /// <summary>
        /// No man is put down nearer to you than this, and never where the camera can see.
        ///
        /// The ground used to be laid the moment you crossed the outer ring, wherever the
        /// dice landed, which on an open block was a man appearing forty metres in front of
        /// you. They are put down out of sight or not yet.
        /// </summary>
        private const float SpawnClear = 50f;

        /// <summary>How far off he can be and still be tracked at all.</summary>
        private const float TrackRange = 180f;

        /// <summary>
        /// Prints are only drawn when you are near enough to be reading the ground.
        ///
        /// A TRAIL HAS TO LEAD SOMEWHERE YOU CAN SEE. At thirty metres the line of prints
        /// ended about as far ahead as you could make one out, so what you were following was
        /// always a short piece of ground with nothing beyond it -- which is a breadcrumb,
        /// not a track. A little further and the trail runs off toward him instead of
        /// stopping in front of you.
        /// </summary>
        private const float PrintRange = 38f;

        /// <summary>How long his first trail is, how far apart the prints are, and how many are kept.</summary>
        private const int PrintCount = 22;
        private const float PrintStride = 1.5f;

        /// <summary>
        /// How many of his prints are remembered, and how many go down in one pass.
        ///
        /// A HUNDRED AND THIRTY BECAUSE HE ACTUALLY WALKS NOW. Seventy was a man who moved
        /// twice in a job: his trail was the stretch he walked in on and almost nothing after
        /// it, so there was one line to find and then nothing left to read. He wanders his
        /// patch constantly now, and the trail has to be long enough to be a trail.
        ///
        /// THE PER-PASS CAP IS THE DECAL INTAKE. The game takes about thirty-two new decals
        /// in a frame whoever asks -- see the paint engine -- and walking into range of a
        /// long trail would otherwise ask for a hundred at once and lose most of them
        /// silently. Twelve a pass, four hundred milliseconds apart, lays the same trail over
        /// a couple of seconds and keeps every one of them.
        /// </summary>
        private const int TrailMost = 130;
        private const int LayPerPass = 12;

        /// <summary>Stood this close to one of his prints and you have picked up his trail.</summary>
        private const float FoundWithin = 6f;

        /// <summary>
        /// Close enough to put the knife in him.
        ///
        /// A ARM'S LENGTH AND A BIT. The takedown clips carry both men a short way as they
        /// play, so the grab does not have to start touching him -- but it does have to start
        /// close enough that the snap into the animation's own spacing is not a man sliding
        /// half a metre sideways. This is about where the game's own stealth kill offers
        /// itself.
        /// </summary>
        private const float StickRange = 1.5f;

        /// <summary>
        /// How far round the back of him counts as behind him.
        ///
        /// The dot of HIS forward against the direction from him to you. Straight behind is
        /// minus one, level with his shoulders is nought, in his face is plus one -- so this
        /// is a hair past his shoulder each way: a hundred and ten degrees of back, which is
        /// what you can walk into without crossing his eyeline.
        /// </summary>
        private const float BehindDot = -0.34f;

        /// <summary>
        /// Above this he is alert enough that grabbing him goes wrong.
        ///
        /// Not "has he seen you" -- that ends the job on its own. This is the middle ground:
        /// something has him half-turned and listening, and a man in that state gets a hand
        /// up. See Botched.
        /// </summary>
        private const float AlertAt = 0.5f;

        /// <summary>
        /// The takedown clips, and the one dictionary they all live in.
        ///
        /// CHECKED AGAINST THE INSTALL'S OWN LIST, not remembered. melee@knife@streamed_core
        /// carries a matched pair for each of these -- a plyr_ and a victim_ cut against each
        /// other frame for frame -- which is what makes the whole thing possible without a
        /// synchronised scene: play both at ONE origin with TASK_PLAY_ANIM_ADVANCED and they
        /// line up exactly as they were animated to.
        ///
        /// stealth_kill is the execution: a hand over the mouth and the blade in under the
        /// ribs, and it is the one you get for doing it properly. front_takedown is for the
        /// rare case of a man who has not noticed you standing in front of him. And
        /// failed_takedown is what a man who HAS noticed does about it.
        /// </summary>
        private const string BladeDict = "melee@knife@streamed_core";

        private const string StealthPlayer = "plyr_knife_stealth_kill";
        private const string StealthVictim = "victim_knife_stealth_kill";

        private const string FrontPlayer = "plyr_knife_front_takedown";
        private const string FrontVictim = "victim_knife_front_takedown";

        private const string BotchPlayer = "plyr_knife_failed_takedown_rear";
        private const string BotchVictim = "victim_knife_failed_takedown_rear";

        /// <summary>How far through the clip he stops being a man who is being stabbed.</summary>
        private const float DiesAt = 0.86f;

        /// <summary>A takedown that somehow never ends is still over by now.</summary>
        private const int StickMostMs = 9000;

        /// <summary>
        /// How near the player has to be for a wound to read as the knife rather than a shot.
        ///
        /// A SHOT SENDS HIM RUNNING AND A STAB MUST NOT. Hurt is the minigame's wounded elk
        /// and it is right for a rifle -- you winged him, follow the blood -- but a man you
        /// are stood on top of with a blade does not get to become a blood trail after the
        /// first swing. Slightly wider than the reach, because he moves while you swing.
        /// </summary>
        private const float BladeWound = 3.2f;

        /// <summary>The ring on the map for each of them: this big, and slipped off him by up to this.</summary>
        private const float AreaRadius = Beat + 6f;

        /// <summary>How often one of them walks between his two spots, and how far apart they are.</summary>
        /// <summary>
        /// How long he stands before moving on, and how far the next spot is.
        ///
        /// THIRTY-FIVE TO SEVENTY SECONDS WAS A MAN STANDING STILL. A hunt lasts a few
        /// minutes, so at that rate each of them moved perhaps twice in the whole job, and
        /// between moves there was nothing on the ground to read and nothing to see from
        /// cover. Seven to fifteen is a man who is never where you last looked.
        ///
        /// AND HE STAYS IN HIS PATCH. Every spot is measured from his HOME rather than from
        /// wherever he has got to, so he orbits the ground his ring covers instead of
        /// drifting off the block -- the ring is a forty-two metre promise and a man who
        /// walked out of it would make the map a liar.
        /// </summary>
        private const int WanderMinMs = 7000;
        private const int WanderVaryMs = 8000;
        private const float BeatNear = 8f;
        private const float BeatFar = Beat;

        /// <summary>And how far he must actually travel for it to be worth walking.</summary>
        private const float BeatWorth = 9f;

        /// <summary>How far one of them can see you.</summary>
        private const float SeeRange = 42f;

        /// <summary>What fills his suspicion per second, at the worst of it.</summary>
        private const float SeenPerSecond = 0.55f;
        private const float SprintPerSecond = 0.9f;
        private const float ShotSpike = 0.75f;

        /// <summary>And what he forgets per second when nothing is happening.</summary>
        private const float CalmPerSecond = 0.22f;

        /// <summary>Crouched, he hears half of it.</summary>
        private const float CrouchQuiet = 0.45f;

        /// <summary>How often a line of sight is actually asked for. Six raycasts a frame was the old figure.</summary>
        private const int LosEveryMs = 150;

        /// <summary>Lamar, on foot: how far behind, and how often he is re-tasked if he is lagging.</summary>
        private const float LamarBehind = 4.5f;
        private const float LamarRunFrom = 12f;
        private const float LamarWalkFrom = 6f;
        private const int LamarRetaskMs = 4000;

        /// <summary>
        /// The blade, in the order an install has them. THIS is the job now -- see Blade.
        ///
        /// A knife first because a knife is what the takedown animations are cut for: the
        /// clips live in melee@knife@streamed_core and the model in his hand during them is
        /// whatever he is holding, so a machete reads fine and a hammer would not. Every one
        /// of these is a stock weapon on every install; the ladder is there for the one that
        /// is not rather than because any of them is likely to be missing.
        /// </summary>
        private static readonly string[] Blades =
        {
            "WEAPON_KNIFE", "WEAPON_SWITCHBLADE", "WEAPON_DAGGER", "WEAPON_MACHETE"
        };

        /// <summary>The rifle he is lent, in the order an install has them.</summary>
        private static readonly string[] Rifles =
        {
            "WEAPON_SNIPERRIFLE", "WEAPON_MARKSMANRIFLE", "WEAPON_HEAVYSNIPER"
        };

        private const int Rounds = 20;

        /// <summary>Who is out there.</summary>
        private static readonly string[] Models =
        {
            "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01"
        };

        /// <summary>
        /// What a man on a corner does with an afternoon.
        ///
        /// WALK, STAND ABOUT DOING SOMETHING, WALK AGAIN. Each of these is the tail of a
        /// sequence -- see Send -- so he strolls thirty metres, gets his phone out or lights
        /// something for ten or twenty seconds, and then goes somewhere else. The point is
        /// that his BACK is a moving target: a man rooted to one scenario is a puzzle with one
        /// answer, and a man who keeps turning round is a stalk.
        ///
        /// A FRESH ONE EVERY TIME HE SETS OFF rather than one picked when he spawned. Six
        /// clips on four men is not much variety if each man only ever has the one.
        ///
        /// NOTHING IN HERE NEEDS SOMETHING TO LEAN ON. WORLD_HUMAN_LEANING was on this list
        /// and is off it: it is authored against a wall, and the spots these men walk to are
        /// whatever the pavement offers -- so most of the time he leant back onto nothing and
        /// stood at an angle in open air, which reads as broken rather than as casual.
        ///
        /// Every name is in Scenarios.txt on this machine.
        /// </summary>
        private static readonly string[] QuarryDoing =
        {
            "WORLD_HUMAN_STAND_MOBILE",          // scrolling
            "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",  // on a call
            "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_SMOKING_POT",
            "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_DRUG_DEALER"
        };

        // ======================================================================
        // The people in it
        // ======================================================================

        /// <summary>One of the four corners, and what the street it is on is called.</summary>
        private sealed class Stand
        {
            public readonly Vector3 At;
            public readonly string Street;

            public Stand(float x, float y, float z, string street)
            {
                At = new Vector3(x, y, z);
                Street = street;
            }
        }

        private sealed class Quarry
        {
            /// <summary>Which of the four he is. See Stands.</summary>
            public int Which;

            public Ped Man;

            /// <summary>The ring on the map before you have his trail, and the mark on him after.</summary>
            public Blip Area;
            public Blip Mark;

            /// <summary>
            /// The middle of his patch, where he is headed now, and what he does when he
            /// gets there.
            ///
            /// A FRESH SPOT EVERY TIME, NOT TWO OF THEM. He used to shuttle between his home
            /// and one other place, which is a man pacing a line -- and when the other place
            /// could not be found the two were the same point and he never moved again for
            /// the rest of the job. Now it is rolled each time he sets off, so the trail
            /// meanders the way a man's does and a failed roll costs one wait rather than
            /// the whole hunt. See Roam.
            /// </summary>
            public Vector3 Home;
            public Vector3 Going;
            public string Doing = "";

            /// <summary>When he next walks, whether he is walking now, and where his last print went.</summary>
            public int WanderAt;
            public bool Walking;
            public int WalkFrom;
            public Vector3 LastStep;

            /// <summary>Nought to one. At one he is gone. See Watch.</summary>
            public float Suspicion;

            /// <summary>The last line-of-sight answer, and when it was asked.</summary>
            public bool Seen;
            public int LosAt;

            public bool Down;

            /// <summary>
            /// He noticed, and he is coming. Not a kill, and not a failure either.
            ///
            /// HE USED TO RUN AND IT USED TO END THE WHOLE JOB. One man spotting you failed
            /// the hunt outright, which meant the only way to play it was perfectly or not at
            /// all. Now he turns round and tries to kill you, he stops counting toward the
            /// three you need, and the job carries on -- so a blown stalk costs you the one
            /// man you blew it on and the noise of the fight, which is plenty.
            /// </summary>
            public bool Blown;

            /// <summary>You stood on his trail. See Prints.</summary>
            public bool Found;

            /// <summary>
            /// Every print he has left, and which of them are on the ground yet.
            ///
            /// A LIST, NOT AN ARRAY, because he keeps walking. The first stretch is laid when
            /// he is -- from where he walked in to where he stands -- and every walk between
            /// his spots after that adds to the end of it. Each one goes down the first time
            /// YOU are near enough to be reading that piece of ground, and they do not move.
            /// </summary>
            public readonly List<Vector3> Trail = new List<Vector3>();
            public readonly List<bool> Laid = new List<bool>();

            /// <summary>When the trail was last looked over, so it is not walked every frame.</summary>
            public int PrintedAt;

            /// <summary>Stuck, and the clip is still playing. See Blade.</summary>
            public bool Sticking;

            /// <summary>He went down to the blade rather than to the rifle. See Dropped.</summary>
            public bool Knifed;

            /// <summary>Lamar has already told you how to take this one. See Nudges.</summary>
            public bool Nudged;
        }

        /// <summary>What Lamar is doing, so he is only re-tasked when it changes.</summary>
        private enum Walk { None, Follow, Run, Still, Boarding, Riding, Leaving }

        private readonly List<Quarry> _out = new List<Quarry>();

        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly Random _rng = new Random();

        private MissionDef _def;
        private Blip _fieldMark;
        private int _layFrom;

        private Ped _lamar;
        private Walk _lamarDoing;
        private int _lamarAt;
        private bool _lamarStealth;

        private int _phaseFrom;
        private int _shotAt;

        /// <summary>Who is in reach right now, and whether it is a clean chance. See Blade.</summary>
        private Quarry _within;
        private bool _clean;
        private bool _armed;

        /// <summary>Who is being stuck, when it started, how long the clip runs, and whether it took.</summary>
        private Quarry _sticking;
        private int _stickFrom;
        private int _stickMs;
        private bool _stuck;
        private bool _killed;

        /// <summary>What he was lent, so it can be taken back. Nought for a thing he already owned.</summary>
        private uint _lent;
        private uint _lentBlade;

        /// <summary>The blade he is meant to be using, whether it was lent or his own.</summary>
        private uint _blade;

        public HuntPhase Phase { get; private set; }

        public bool IsRunning => Phase != HuntPhase.None;

        public bool ReadyToCollect { get; private set; }

        public string Failure { get; private set; }

        /// <summary>Set by MissionRunner: Lamar, so the hunt does not have to make its own.</summary>
        public Func<Ped> Fixer;

        /// <summary>
        /// Set by MissionRunner: hands Lamar back to his corner when the job is over.
        ///
        /// THE HUNT NEVER GAVE HIM BACK. The bike ride calls Fixer.TakeBack at its end and
        /// this did not, so after a hunt he stayed lent for the rest of the session -- which
        /// is a man you cannot talk to, stood on his own corner.
        /// </summary>
        public Action GiveBack;

        public int Down
        {
            get
            {
                var n = 0;
                foreach (var q in _out) if (q.Down) n++;
                return n;
            }
        }

        private int Lost
        {
            get
            {
                var n = 0;
                foreach (var q in _out) if (q.Blown) n++;
                return n;
            }
        }

        public float Advance => Down / (float)Need;

        /// <summary>
        /// Which of the four is next, or -1 when they have all been settled one way or another.
        ///
        /// IN ORDER, AND ONE AT A TIME. Only this one is ringed and only this one is named on
        /// the card, because "four men are out there somewhere" is a map screen and "the one
        /// on Carson Ave" is a plan.
        /// </summary>
        private int Next
        {
            get
            {
                for (var i = 0; i < Stands.Length; i++)
                {
                    var q = Of(i);
                    if (q == null) return i;
                    if (!q.Down && !q.Blown) return i;
                }

                return -1;
            }
        }

        /// <summary>The man standing at one of the four, if he has been put out yet.</summary>
        private Quarry Of(int which)
        {
            foreach (var q in _out) if (q.Which == which) return q;
            return null;
        }

        /// <summary>Whether all four have been settled, however they went.</summary>
        private bool Settled => Next < 0;

        public string Objective
        {
            get
            {
                switch (Phase)
                {
                    case HuntPhase.Riding: return "Get out to the block with Lamar";
                    case HuntPhase.Leaving: return "Get back to Lamar's";

                    default:
                    {
                        var i = Next;

                        return i < 0
                            ? "Take them out quiet  --  " + Down + " of " + Need
                            : "Take out the one on " + Stands[i].Street +
                              "  --  " + Down + " of " + Need;
                    }
                }
            }
        }

        public Hunt(Affiliation crew, GangRegistry gangs)
        {
            _crew = crew;
            _gangs = gangs;
        }

        // ======================================================================
        // Starting
        // ======================================================================

        public string Start(MissionDef def)
        {
            if (def == null) return "nothing to hunt.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "not right now.";

            Clear();

            // THE BRIEF'S COORDINATE IS NOT USED AND THAT IS DELIBERATE. The hunt has four
            // fixed corners of its own -- see Stands -- and a mission definition that could
            // move the job somewhere else would move it away from the only four places it
            // makes sense. The definition is still the thing that decides whether the job is
            // offered, what it is called and what it pays.
            _def = def;

            Failure = null;
            ReadyToCollect = false;

            Kit(player);

            Phase = HuntPhase.Riding;
            _phaseFrom = Game.GameTime;
            _layFrom = 0;
            _lamarDoing = Walk.None;
            _lamarAt = 0;
            _lamarStealth = false;

            MarkField();

            Word("get us out there. and go quiet when we're close");

            Log.Info("Hunt: on. " + Many + " corners, " + Need + " needed, " +
                     Stands[Last].Street + " last.");

            return null;
        }

        /// <summary>
        /// The ground, on the map, for the ride out.
        ///
        /// THERE WAS NO MARKER. Every other job marks its site; the hunt handed you a rifle
        /// and an objective that said "get out to the block" and nothing that said which
        /// block. The same yellow ring the other jobs use, with the route, until the ground
        /// is laid and the rings for the three take over.
        /// </summary>
        private void MarkField()
        {
            var which = Next;

            // The one you are walking at, or Lamar's yard once they are all settled. See Home.
            var at = which < 0 ? Home : Stands[which].At;
            var name = which < 0 ? "Back to Lamar's" : "The one on " + Stands[which].Street;

            if (_fieldMark != null && _fieldMark.Exists() && _fieldAt == which)
            {
                return;
            }

            UnmarkField();
            _fieldAt = which;

            try
            {
                _fieldMark = World.CreateBlip(at, which < 0 ? FieldRadius : AreaRadius);
                if (_fieldMark == null || !_fieldMark.Exists()) return;

                _fieldMark.Color = which < 0 ? BlipColor.Green : BlipColor.Yellow;
                _fieldMark.Alpha = 90;
                _fieldMark.ShowRoute = true;
                _fieldMark.Name = name;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the hunt: " + ex.Message);
            }
        }

        /// <summary>Which stand the ring is currently drawn round, so it is not redrawn every frame.</summary>
        private int _fieldAt = -2;

        /// <summary>
        /// Lamar's yard, which is where the job starts and where it has to end.
        ///
        /// Written here rather than read off Main, because the hunt is handed a mission
        /// definition and a fixer and nothing else -- and the one place in the city this crew
        /// runs back to is not a thing that varies.
        /// </summary>
        private static readonly Vector3 Home = new Vector3(-202.5f, -1730.0f, 32.664f);

        private void UnmarkField()
        {
            try { if (_fieldMark != null && _fieldMark.Exists()) _fieldMark.Delete(); }
            catch { /* it goes with the job */ }

            _fieldMark = null;
        }

        /// <summary>
        /// What he is lent for the job: a blade to do it with, and a rifle for when it goes
        /// wrong.
        ///
        /// THE BLADE IS THE JOB AND IT GOES IN HIS HAND. A rifle in a stalk is the loud
        /// option, and it stays in the bag so it is a decision rather than the default -- a
        /// shot brings every corner on the block round, which is a rule this job has always
        /// had and never gave you a reason to care about.
        ///
        /// Both are written down so they can be taken back at the end. A job that hands out a
        /// sniper rifle and forgets about it is a job that has changed the rest of the save.
        /// </summary>
        private void Kit(Ped player)
        {
            _blade = Lend(player, Blades, 1, out _lentBlade);

            Lend(player, Rifles, Rounds, out _lent);

            // The blade last, because the blade is what he is meant to be holding.
            if (_blade == 0) return;

            try { Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, _blade, true); }
            catch { /* he can pick it himself */ }
        }

        /// <summary>
        /// Gives him one of these with rounds in it, and says which one he ended up holding.
        ///
        /// AMMO IS NOT SO MUCH A PARAMETER OF GIVE_WEAPON_TO_PED AS A SUGGESTION TO IT.
        /// Handing a ped a weapon he ALREADY OWNS adds nothing whatever -- not the weapon and
        /// not the rounds -- so a player who happened to own a sniper rifle with an empty
        /// magazine was sent out on a hunt with an empty magazine, and the old code took that
        /// branch deliberately and called it "he has his own". Reported as exactly that: no
        /// ammo with the rifle.
        ///
        /// SET_PED_AMMO is the call that is actually obeyed, so it goes in either way, and
        /// the magazine is filled from it so the first shot is not a reload. The count is
        /// only ever raised: a man who turned up with sixty rounds keeps sixty.
        ///
        /// WHAT IS LENT IS ONLY WHAT HE DID NOT ALREADY HAVE. A job lends things, it does not
        /// confiscate a gun somebody brought with him -- and the rounds are his to keep
        /// whichever it was, because taking ammo back off a man's own rifle at the end of an
        /// errand would be worse than giving him none.
        /// </summary>
        private static uint Lend(Ped player, string[] names, int rounds, out uint lent)
        {
            lent = 0;

            foreach (var name in names)
            {
                try
                {
                    var hash = Function.Call<uint>(Hash.GET_HASH_KEY, name);
                    if (hash == 0) continue;

                    var had = Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, hash, false);

                    if (!had)
                    {
                        Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, hash, rounds, false, false);

                        // A name the install does not carry is accepted in silence, like every
                        // other name in this game. Asking again is the only way to tell.
                        if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, hash, false)) continue;

                        lent = hash;
                    }

                    Fill(player, hash, rounds);

                    Log.Info("Hunt: " + (had ? "he has his own " : "lent him a ") + name + ", " +
                             Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, player.Handle, hash) +
                             " round(s).");

                    return hash;
                }
                catch
                {
                    // Next name.
                }
            }

            return 0;
        }

        /// <summary>Tops a weapon up to at least this many rounds, magazine included.</summary>
        private static void Fill(Ped player, uint hash, int rounds)
        {
            if (rounds <= 1) return;

            try
            {
                var have = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, player.Handle, hash);

                if (have < rounds) Function.Call(Hash.SET_PED_AMMO, player.Handle, hash, rounds);

                // AND INTO THE MAGAZINE. Total ammo and the rounds actually in the gun are two
                // different numbers, and setting only the first is a full pouch behind an empty
                // chamber -- which on a sniper rifle is a reload at the exact moment you have
                // finally got somebody in the glass.
                var clip = Function.Call<int>(Hash.GET_MAX_AMMO_IN_CLIP, player.Handle, hash, true);

                if (clip > 0) Function.Call(Hash.SET_AMMO_IN_CLIP, player.Handle, hash, clip);
            }
            catch
            {
                // Then he has whatever the give left him.
            }
        }

        private void HandItBack()
        {
            try
            {
                var player = Game.Player.Character;

                if (player != null && player.Exists())
                {
                    if (_lent != 0) Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, _lent);
                    if (_lentBlade != 0) Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, _lentBlade);
                }
            }
            catch
            {
                // He keeps them, then.
            }

            _lent = 0;
            _lentBlade = 0;
            _blade = 0;
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

                Lamar(player, now);

                // A MAN WHOSE VOICE COMES OUT OF NOWHERE. Lips runs the chatter facial for as
                // long as a recording is playing, which is what the conversation screens use --
                // and there is no screen open out here, so nothing else was going to do it.
                try { Core.Lips.Update(_lamar); }
                catch { /* his face stays still, which is the old behaviour */ }

                // A SHOT IS NOTICED HERE rather than reported from outside. Everything the
                // hunt needs to know about is either the player firing or a target losing
                // blood, and both can be read off the world every tick -- so nothing else in
                // the mod has to know this job exists.
                try
                {
                    if (Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle)) _shotAt = now;
                }
                catch
                {
                    // Then the only thing that gives him away is being seen.
                }

                // BEFORE THE PHASES, AND WHATEVER THEY ARE. See Sticking.
                if (_sticking != null)
                {
                    Sticking(player, now);
                    return;
                }

                switch (Phase)
                {
                    case HuntPhase.Riding: Riding(player, now); break;
                    case HuntPhase.Tracking: Tracking(player, now); break;
                    case HuntPhase.Leaving: Leaving(player, now); break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("The hunt fell over: " + ex.Message);
                Failure = "it went wrong out there.";
            }
        }

        /// <summary>
        /// Out to the block. The ground is laid once he is near enough, a man at a time as
        /// somewhere out of sight turns up for each, and the hunt starts when they are all
        /// down or the patience has run out with at least one.
        /// </summary>
        private void Riding(Ped player, int now)
        {
            if (player.Position.DistanceTo(Stands[0].At) > LayFrom) return;

            if (_layFrom == 0) _layFrom = now;

            Lay(player, now);

            // THE JOB STARTS WHEN THE FIRST ONE IS OUT, not when all of them are. They are
            // laid a corner at a time as you reach them -- see Lay -- so waiting for four
            // would be waiting for the end of the job before letting it begin.
            if (_out.Count == 0)
            {
                if (now - _layFrom < LayPatienceMs) return;

                Failure = "there was nobody out there.";
                return;
            }

            Phase = HuntPhase.Tracking;
            _phaseFrom = now;

            Word("they're out here somewhere. get close and do em quiet");
        }

        /// <summary>
        /// The hunt itself: everybody watching, everybody being watched, and the tracks under
        /// your feet.
        /// </summary>
        private void Tracking(Ped player, int now)
        {
            var crouched = Crouching(player);
            var sprinting = player.IsSprinting;

            foreach (var q in _out)
            {
                if (q.Down || q.Blown) continue;

                if (q.Man == null || !q.Man.Exists())
                {
                    Vanished(q);
                    continue;
                }

                if (!q.Man.IsAlive)
                {
                    Dropped(q, now);
                    continue;
                }

                // HIT AND STILL UP, AND HE KNOWS WHO DID IT. He used to run bleeding and
                // you followed the blood; he turns round now. A wound taken from outside
                // arm's reach can only have come from the rifle, and the rifle is a decision.
                if (q.Man.Health < q.Man.MaxHealth - 20 &&
                    q.Man.Position.DistanceTo(player.Position) > BladeWound)
                {
                    Blown(q);
                    continue;
                }

                // A MAN IN THE MIDDLE OF BEING STABBED IS NOT TAKING ORDERS. Everything
                // below re-tasks him, and one TASK_TURN_TO_FACE in the middle of a takedown is
                // two men doing different animations a foot apart.
                if (q == _sticking) continue;

                Wander(q, now);
                Watch(q, player, crouched, sprinting, now);
                Prints(q, player, now);
            }

            Blade(player, now);

            Nudge(player);

            Lay(player, now);

            // The ring moves to whichever one is next the moment this one is settled.
            MarkField();

            if (!Settled) return;

            // ALL FOUR ARE SETTLED. Three of them down is the job; fewer is not, and the
            // difference is one sentence rather than a rule that could hang -- which is what
            // the old "nothing left to hunt" backstop was papering over.
            if (Down < Need)
            {
                Failure = "we only got " + Down + " of them.";
                return;
            }

            Grove(player);

            Phase = HuntPhase.Leaving;
            _phaseFrom = now;

            MarkField();
        }

        /// <summary>
        /// Lamar, on the one you are walking at, once you are near enough for it to be a plan.
        ///
        /// THE ONE THAT IS NEXT, not the nearest. You work the corners in order and the card
        /// names the street; a line about whichever Balla happens to be closest would be about
        /// a man you are not going to touch for another ten minutes.
        /// </summary>
        private void Nudge(Ped player)
        {
            var which = Next;
            if (which < 0) return;

            var q = Of(which);
            if (q == null || q.Nudged) return;
            if (q.Man == null || !q.Man.Exists() || !q.Man.IsAlive) return;

            if (q.Man.Position.DistanceTo(player.Position) > NudgeRange) return;

            q.Nudged = true;

            Word(Nudges[_rng.Next(Nudges.Length)], SpeechSeen);
        }

        /// <summary>
        /// One man, deciding whether he has noticed you.
        ///
        /// THREE THINGS FEED IT: how close you are, whether he has a clear line to you, and
        /// how much noise you are making. There was a fourth and it was the wind, and it has
        /// gone -- see the note on the class. The lookouts are what replaced it, and they are
        /// their own thing rather than a modifier on this one, because a man being TOLD you are
        /// out here is not the same as a man noticing you himself and should not be maths on
        /// the same number.
        ///
        /// The line of sight is asked for a few times a second, not every frame: the answer
        /// does not change faster than that and the raycast is the dearest thing in here.
        /// </summary>
        private void Watch(Quarry q, Ped player, bool crouched, bool sprinting, int now)
        {
            var gap = q.Man.Position.DistanceTo(player.Position);

            if (gap > TrackRange)
            {
                q.Suspicion = 0f;
                return;
            }

            var dt = Game.LastFrameTime;
            if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

            var rising = 0f;

            if (gap < SeeRange)
            {
                if (now - q.LosAt >= LosEveryMs)
                {
                    q.LosAt = now;

                    try
                    {
                        q.Seen = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, q.Man.Handle,
                                                     player.Handle, 17);
                    }
                    catch
                    {
                        q.Seen = gap < SeeRange * 0.5f;
                    }
                }

                var near = 1f - gap / SeeRange;

                if (q.Seen) rising += SeenPerSecond * near;
                if (sprinting) rising += SprintPerSecond * near;

                if (crouched) rising *= CrouchQuiet;
            }
            else
            {
                q.Seen = false;
            }

            // A SHOT IS A SHOT. Everybody within earshot looks up, and no amount of being
            // careful up to that point counts for anything.
            if (_shotAt != 0 && now - _shotAt < 400) rising += ShotSpike;

            q.Suspicion += (rising - CalmPerSecond) * dt;

            if (q.Suspicion < 0f) q.Suspicion = 0f;

            if (q.Suspicion < 1f) return;

            Blown(q);
        }

        // ======================================================================
        // The ground
        // ======================================================================

        /// <summary>
        /// Puts them out, well apart, each with somewhere he walked in from, a second spot to
        /// walk to, and a ring on the map that says roughly where.
        ///
        /// A MAN A TICK, until there are three. Somewhere out of your sight is not always
        /// available on the first ask, and the old version gave up on the spot and ran the
        /// hunt with whoever it had managed. This keeps asking for as long as the ride is
        /// patient, and only settles for fewer when that runs out.
        /// </summary>
        private void Lay(Ped player, int now)
        {
            // ONE AT A TIME AND ONLY THE ONE YOU ARE WALKING AT. Four men three hundred metres
            // apart cannot all be put out at the start -- a ped created on ground the streamer
            // has not loaded falls through the world -- and there is no reason to: you work
            // the corners in order, so the only one that has to exist is the next one.
            var which = Next;
            if (which < 0) return;
            if (Of(which) != null) return;

            var stand = Stands[which];

            if (player.Position.DistanceTo(stand.At) > LayFrom) return;

            // NOT WHILE YOU ARE LOOKING AT THE SPOT. A man appearing on a corner you can see
            // is the end of it, and the corner is a fixed place now, so this is the only thing
            // standing between the job and that.
            try
            {
                if (Function.Call<bool>(Hash.IS_SPHERE_VISIBLE, stand.At.X, stand.At.Y, stand.At.Z, 4f) &&
                    player.Position.DistanceTo(stand.At) < SpawnClear)
                {
                    return;
                }
            }
            catch
            {
                // Then distance will have to do.
            }

            // Put down on the pavement rather than on the coordinate, which was read off a man
            // stood in a road and is a foot or two out either way.
            // The pavement near the corner, or the corner itself -- and either way at the
            // height of the concrete rather than of the man who read the coordinate. See Ground.
            var at = World.GetNextPositionOnSidewalk(stand.At);
            if (at == Vector3.Zero || at.DistanceTo(stand.At) > 12f) at = stand.At;

            at = Ground(at);

            var doing = QuarryDoing[_rng.Next(QuarryDoing.Length)];

            var man = Make(at, doing, SeeRange);
            if (man == null) return;

            // WHERE HE WALKED IN FROM, ON THE PAVEMENT. A straight line out of a random
            // compass bearing runs through walls as often as not, and a trail of prints laid
            // inside a house is a trail nobody can follow. The far end is snapped to a
            // walkable spot; every print along the way is put on the floor as it is laid --
            // see Spot -- so the line between them can cross a kerb without disappearing
            // into it.
            var from = at + (Vector3.RandomXY() * (PrintCount * PrintStride));

            var walkable = World.GetNextPositionOnSidewalk(from);
            if (walkable != Vector3.Zero && walkable.DistanceTo(at) > PrintStride * 4f) from = walkable;

            from = Ground(from);

            var q = new Quarry
            {
                Which = which,
                Man = man,
                Home = at,
                Going = at,
                Doing = doing,
                LastStep = at,

                // A SHORT FIRST WAIT. The full one is for a man who has just arrived
                // somewhere; this one has been stood there since before you turned up.
                WanderAt = now + 2000 + _rng.Next(WanderVaryMs)
            };

            Walked(q, from, at);
            Ring(q);

            _out.Add(q);

            Log.Info("Hunt: number " + (which + 1) + " is out on " + stand.Street + ".");
        }

        /// <summary>
        /// Somewhere else in his patch: a pavement spot round his home, and far enough from
        /// where he is stood to be worth the walk.
        ///
        /// MEASURED FROM HOME, WALKED FROM HERE. Both distances matter and they are
        /// different ones -- the first keeps him inside the ring on the map, the second
        /// stops him shuffling two metres and calling it a patrol.
        /// </summary>
        private Vector3 Roam(Vector3 home, Vector3 from)
        {
            for (var tries = 0; tries < 8; tries++)
            {
                var far = BeatNear + (float)_rng.NextDouble() * (BeatFar - BeatNear);
                var probe = home + (Vector3.RandomXY() * far);

                var at = World.GetNextPositionOnSidewalk(probe);
                if (at == Vector3.Zero) continue;
                if (at.DistanceTo(home) > BeatFar + 6f) continue;
                if (at.DistanceTo(from) < BeatWorth) continue;

                // He walks there on the nav mesh, which settles the height on its own -- but
                // the SCENARIO at the far end is placed at this coordinate exactly, so a spot
                // that came back high is a man standing in the air when he arrives.
                return Ground(at);
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// The ring on the map for one of them.
        ///
        /// SLIPPED OFF HIM. A ring centred on the man is a marker on the man with extra
        /// steps; centred somewhere within a few strides of him it says "in here" and no
        /// more, which is what a search area is. It comes off the moment you have his
        /// trail, and a mark on the man goes on in its place -- see Prints.
        /// </summary>
        private void Ring(Quarry q)
        {
            try
            {
                // ON THE CORNER, not slipped off it. The spot is a real place now and you
                // were told which street it is on, so pretending not to know where it is would
                // be a lie the card already contradicts. The ring is the size of his BEAT: he
                // is somewhere inside it, and inside it is thirty metres of pavement.
                q.Area = World.CreateBlip(Stands[q.Which].At, AreaRadius);
                if (q.Area == null || !q.Area.Exists()) return;

                q.Area.Color = BlipColor.Purple;
                q.Area.Alpha = 70;
            }
            catch
            {
                // The prints still lead to him.
            }
        }

        private static void Unring(Quarry q)
        {
            try { if (q.Area != null && q.Area.Exists()) q.Area.Delete(); }
            catch { /* it goes with the job */ }

            try { if (q.Mark != null && q.Mark.Exists()) q.Mark.Delete(); }
            catch { /* likewise */ }

            q.Area = null;
            q.Mark = null;
        }

        /// <summary>
        /// Where he walked, as a list of places a foot went.
        ///
        /// LEFT AND RIGHT, NOT A LINE OF DOTS. Each print steps a hand's width off the middle
        /// of the path and the side alternates, which is the difference between a trail and a
        /// dotted line -- and it is the thing that makes a print readable as a print at all
        /// once it is only a few inches across on the ground.
        /// </summary>
        private static void Walked(Quarry q, Vector3 from, Vector3 to)
        {
            var run = to - from;
            var far = run.Length();

            if (far < 1f) return;

            var step = new Vector3(run.X / far, run.Y / far, run.Z / far);
            var side = new Vector3(-step.Y, step.X, 0f);

            var many = (int)(far / PrintStride);
            if (many > PrintCount * 2) many = PrintCount * 2;
            if (many < 2) many = 2;

            for (var i = 0; i < many; i++)
            {
                var t = i / (float)(many - 1);

                q.Trail.Add(from + run * t + side * (i % 2 == 0 ? PrintSide : -PrintSide));
                q.Laid.Add(false);
            }
        }

        /// <summary>How far a foot lands off the middle of the path. See Walked.</summary>
        private const float PrintSide = 0.16f;

        /// <summary>
        /// One Balla, stood somewhere doing something, who reacts to nothing but this job.
        ///
        /// PERMANENT EVENTS BLOCKED. Without that the game's own gang hatred is in charge:
        /// a Ballas ped who sees a Families man pulls a pistol, which turned a stalk into a
        /// gunfight the moment a target or a lookout happened to glance your way. Now the only
        /// things that move him are the numbers in here.
        /// </summary>
        /// <summary>
        /// The pavement under a point.
        ///
        /// A COORDINATE READ OFF A HUD IS A MAN'S PELVIS. GET_ENTITY_COORDS on a ped does not
        /// return the ground he is standing on, it returns his middle -- so every one of the
        /// four corners in Stands, which were read by standing on them and writing down what
        /// the screen said, is about a metre HIGHER than the concrete it names.
        ///
        /// Spawn a man there and he is created a metre up, and the scenario he is handed on
        /// the same frame pins him there before gravity gets a word in: a Balla loitering in
        /// mid-air, smoking, a foot above his own shadow. Reported as exactly that.
        ///
        /// FIXED HERE RATHER THAN IN THE NUMBERS, on purpose. The numbers are what that HUD
        /// prints, and the next corner anybody adds will be read the same way off the same
        /// screen -- so the correction belongs where every spot goes through it, not in four
        /// hand-adjusted constants that only work until somebody writes down a fifth.
        ///
        /// Probed from a bit above and only believed within a few metres, so a bad read
        /// cannot drop a man through a roof into the room below.
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
                // Then the read height stands, which is wrong by a little rather than a storey.
            }

            return where;
        }

        private Ped Make(Vector3 at, string doing, float sees)
        {
            at = Ground(at);

            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    var man = World.CreatePed(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (man == null || !man.Exists()) continue;

                    // AND AGAIN, MEASURED OFF THE MAN HIMSELF. CREATE_PED does not always put
                    // him where it was asked to -- and the scenario below freezes whatever
                    // height he ended up at -- so the one reading worth trusting is the one
                    // taken after he exists. Only when it is worth doing: snapping a man who
                    // is already standing correctly is a visible twitch for nothing.
                    try
                    {
                        var stood = man.Position;
                        var floor = Ground(stood);

                        if (Math.Abs(stood.Z - floor.Z) > 0.25f)
                        {
                            Function.Call(Hash.SET_ENTITY_COORDS, man.Handle,
                                          floor.X, floor.Y, floor.Z, false, false, false, true);
                        }
                    }
                    catch
                    {
                        // He stands where he landed.
                    }

                    man.IsPersistent = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, man.Handle, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, true);

                    // AND NOBODY FINDS THE BODY. A corpse throws a shocking event every few
                    // seconds for as long as it is lying there, and every Balla in earshot of
                    // it reacts -- so doing the first one perfectly quietly still turned the
                    // block on you about twenty seconds later, from a corner you had already
                    // left. Switched off here, which leaves exactly one thing that can raise
                    // the street: a gunshot, which makes its own noise and is nothing to do
                    // with this flag. That is the rule as asked for -- the knife costs you
                    // nothing, the rifle costs you the block.
                    //
                    // It goes back ON the moment he notices you. See Blown.
                    Function.Call(Hash.SET_PED_GENERATES_DEAD_BODY_EVENTS, man.Handle, false);

                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, man.Handle, doing, 0, true);

                    Function.Call(Hash.SET_PED_ALERTNESS, man.Handle, 0);
                    Function.Call(Hash.SET_PED_SEEING_RANGE, man.Handle, sees);
                    Function.Call(Hash.SET_PED_HEARING_RANGE, man.Handle, sees * 0.7f);
                    Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, true);

                    if (_gangs != null)
                    {
                        var ballas = _gangs.Get("ballas");

                        if (ballas != null && ballas.GroupHash != 0)
                        {
                            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, man.Handle, ballas.GroupHash);
                        }
                    }

                    return man;
                }
                catch
                {
                    // Next model.
                }
            }

            return null;
        }

        /// <summary>
        /// Walk there, then do that, as one sequence -- the same shape the car meet uses.
        /// Two tasks issued back to back are one task; in a sequence they are two.
        /// </summary>
        private static bool Send(Ped man, Vector3 to, string doing, float pace)
        {
            var slot = new OutputArgument();

            try
            {
                var d = to - man.Position;
                var face = (float)(Math.Atan2(d.Y, d.X) * 180.0 / Math.PI) - 90f;

                Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, false);

                Function.Call(Hash.OPEN_SEQUENCE_TASK, slot);
                var seq = slot.GetResult<int>();

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, 0,
                              to.X, to.Y, to.Z, pace, 40000, 0.8f, 0, face);

                Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, 0, doing,
                              to.X, to.Y, to.Z, face, -1, false, false);

                Function.Call(Hash.CLOSE_SEQUENCE_TASK, seq);
                Function.Call(Hash.TASK_PERFORM_SEQUENCE, man.Handle, seq);

                Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Hunt: could not send a man across the block: " + ex.Message);
                return false;
            }
            finally
            {
                try { Function.Call(Hash.CLEAR_SEQUENCE_TASK, slot); } catch { }
            }
        }

        /// <summary>
        /// Between his two spots, now and then, leaving prints as he goes.
        ///
        /// A MAN WHO NEVER MOVES LEAVES ONE TRAIL, and once you have read it there is nothing
        /// left to read. Every so often he walks to his other spot and back, and every step
        /// of it goes on the end of his trail -- so the ground keeps saying where he went,
        /// which is what tracking is. Not while he is suspicious, and not while Lamar has
        /// him coming over.
        /// </summary>
        private void Wander(Quarry q, int now)
        {
            if (q.Walking)
            {
                // Prints behind him as he goes.
                if (q.Man.Position.DistanceTo(q.LastStep) >= PrintStride)
                {
                    var side = Vector3.Cross(q.Man.ForwardVector, Vector3.WorldUp);
                    side = new Vector3(side.X, side.Y, 0f);

                    q.Trail.Add(q.Man.Position + side * (q.Trail.Count % 2 == 0 ? PrintSide : -PrintSide));
                    q.Laid.Add(false);
                    q.LastStep = q.Man.Position;

                    while (q.Trail.Count > TrailMost)
                    {
                        q.Trail.RemoveAt(0);
                        q.Laid.RemoveAt(0);
                    }
                }

                var arrived = false;
                try { arrived = Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, q.Man.Handle); }
                catch { arrived = true; }

                if (arrived || now - q.WalkFrom > 40000)
                {
                    q.Walking = false;
                    q.WanderAt = now + WanderMinMs + _rng.Next(WanderVaryMs);
                }

                return;
            }

            if (now < q.WanderAt) return;
            if (q.Suspicion > 0.2f) return;

            // A ROLL THAT FAILS IS WORTH ANOTHER GO IN A MOMENT, and that is the whole of
            // the standing-still bug: the old pair was worked out once, and a man whose
            // second spot came back as his first stood there for the rest of the job.
            // SOMETHING ELSE TO DO WHEN HE GETS THERE. See QuarryDoing.
            q.Doing = QuarryDoing[_rng.Next(QuarryDoing.Length)];

            var to = Roam(q.Home, q.Man.Position);

            if (to == Vector3.Zero)
            {
                q.WanderAt = now + 4000;
                return;
            }

            if (!Send(q.Man, to, q.Doing, 1.0f))
            {
                q.WanderAt = now + 6000;
                return;
            }

            q.Going = to;
            q.Walking = true;
            q.WalkFrom = now;
            q.LastStep = q.Man.Position;
        }

        /// <summary>
        /// His tracks, laid on the ground between where he came in and where he is.
        ///
        /// ONLY WHEN YOU ARE NEAR ENOUGH TO BE READING THEM. Decals are a budget shared with
        /// every other script on the machine -- see the blood mod -- so a trail per man laid
        /// across the whole block would be spent on ground nobody is looking at.
        ///
        /// AND STAND ON IT AND YOU HAVE HIM. Within a few strides of any print of his that
        /// is down, the ring comes off the map and a mark goes on the man: you have picked
        /// up the trail, and from here it is a stalk rather than a search.
        /// </summary>
        private void Prints(Quarry q, Ped player, int now)
        {
            if (q.Trail.Count == 0) return;
            if (now - q.PrintedAt < 400) return;

            q.PrintedAt = now;

            var me = player.Position;
            var stood = false;
            var laid = 0;

            for (var i = 0; i < q.Trail.Count; i++)
            {
                var at = q.Trail[i];
                var gap = me.DistanceTo(at);

                if (q.Laid[i])
                {
                    if (gap < FoundWithin) stood = true;
                    continue;
                }

                // NEAR THE PRINT, not near the man. You read the ground where you are stood,
                // and the far end of a trail is a piece of ground like any other.
                if (gap > PrintRange) continue;

                // And no more than a handful this pass. See LayPerPass.
                if (laid >= LayPerPass) continue;

                laid++;

                // OLDEST NEARLY GONE, FRESHEST ALMOST WHITE, and that ramp is the whole of
                // tracking: the bright end of a trail is the end pointing at him, and you
                // read which way to walk off the brightness rather than off a marker.
                //
                // THEY USED TO BE INVISIBLE AND THAT IS NOT AN EXAGGERATION. The tint topped
                // out at a 46 percent grey-brown and the oldest were at 16 percent -- a dark
                // smudge, on asphalt, in a city that is mostly asphalt. Reported as the
                // targets not leaving any tracks at all, which is exactly what a print you
                // cannot see is. The mission everybody remembers this from has tracks you can
                // follow at a walk without stopping, and that is the bar.
                var fresh = i / (float)Math.Max(1, q.Trail.Count - 1);
                var lit = 0.34f + 0.66f * fresh;

                var step = i + 1 < q.Trail.Count
                    ? q.Trail[i + 1] - at
                    : at - q.Trail[Math.Max(0, i - 1)];

                // Warm rather than white. A neutral print reads as paint; a print with the
                // dust of the street in it reads as a foot.
                //
                // AND IT IS ONLY WRITTEN OFF ONCE IT ACTUALLY WENT DOWN. It used to be marked
                // laid BEFORE the attempt, so a decal the engine refused -- because its pool
                // happened to be full that frame, which with the blood mod running is most
                // frames -- was never tried again. Two passes of that on arrival and the whole
                // trail was permanently spent without a single print existing. Marked after,
                // it simply comes back round in four hundred milliseconds.
                if (!Spot(at, step, lit, lit * 0.96f, lit * 0.86f, PrintSize, i % 2 == 0,
                          0.45f + 0.50f * fresh))
                {
                    _refused++;
                    continue;
                }

                q.Laid[i] = true;

                if (gap < FoundWithin) stood = true;
            }

            if (stood && !q.Found) Found(q);

            Moan(now);
        }

        /// <summary>How many prints the engine would not take, and when that was last said.</summary>
        private int _refused;
        private int _moanedAt;

        private const int MoanEveryMs = 20000;

        /// <summary>
        /// Says when the tracks are not landing, because otherwise nothing does.
        ///
        /// A refused decal is the one failure in this file with no symptom other than the
        /// feature quietly not happening, and it is not our bug when it happens -- the pool
        /// belongs to the whole machine. A line every twenty seconds with a count in it is
        /// the difference between "the mod does not lay tracks" and "your decal pool is full,
        /// and here is by how much".
        /// </summary>
        private void Moan(int now)
        {
            if (_refused <= 0) return;
            if (now - _moanedAt < MoanEveryMs && _moanedAt != 0) return;

            _moanedAt = now;

            Log.Warn("Hunt: the game refused " + _refused + " footprint(s) -- its decal pool is " +
                     "full. Other mods share it; see the blood mod's budget.");

            _refused = 0;
        }

        /// <summary>The trail is picked up: the ring off, a mark on him.</summary>
        private void Found(Quarry q)
        {
            q.Found = true;

            try { if (q.Area != null && q.Area.Exists()) q.Area.Delete(); }
            catch { /* it goes with the job */ }

            q.Area = null;

            try
            {
                q.Mark = q.Man.AddBlip();

                if (q.Mark != null && q.Mark.Exists())
                {
                    q.Mark.Sprite = (BlipSprite)1;
                    q.Mark.Color = BlipColor.Red;
                    q.Mark.Scale = 0.55f;
                    q.Mark.Name = "Ballas";
                    q.Mark.IsShortRange = true;
                }
            }
            catch
            {
                // Then the prints are all there is, which is still a trail.
            }

            Log.Info("Hunt: picked up a trail.");
        }

        /// <summary>
        /// One print on the ground, pointing the way he was walking.
        ///
        /// TYPE 2040, WHICH IS A FOOTPRINT: one of the two BLOOD TRANSFER soles off the
        /// fxdecal_footprints sheet, where the game's own bloody footprints come from; 2140 is
        /// the other tread, and alternating them stops a trail being one stamp repeated. The
        /// colour is ours -- the art is a greyscale mask and takes whatever tint it is
        /// handed -- and it is a good deal lighter than it was, because a print tinted
        /// thirty percent grey on wet asphalt was a print nobody could see. The side vector
        /// turns it across the direction of travel, so the toe points the way he went.
        /// </summary>
        private static bool Spot(Vector3 at, Vector3 step, float r, float g, float b,
                                 float size, bool left, float alpha = 0.9f)
        {
            var flat = new Vector3(step.X, step.Y, 0f);

            if (flat.Length() < 0.001f) flat = new Vector3(1f, 0f, 0f);
            else flat.Normalize();

            var side = new Vector3(-flat.Y, flat.X, 0f);

            // ON THE PAVEMENT, WHEREVER THE POINT CAME FROM.
            //
            // THIS IS WHY THERE WERE NO TRACKS. A trail point is either a ped's Position --
            // which is his PELVIS, a metre up -- or a straight line interpolated between two
            // of them across ground that is not flat. So the prints were being asked for a
            // metre in the air and a decal projects a short way, not a long one: most of them
            // hit nothing at all and the rest landed on a kerb or inside it.
            //
            // Snapped here rather than at the point they are recorded, because a trail is
            // recorded once and drawn once, and doing it here means every caller gets it --
            // the blood from a takedown included.
            at = Ground(at);

            try
            {
                // THE HANDLE IS THE ANSWER TO "WHY IS NOTHING APPEARING". ADD_DECAL returns
                // nought when the ENGINE's decal pool is full, and that pool is shared with
                // every other script on the machine -- the blood mod and the paint engine
                // both live in it. Thrown away, as it was, a mod competing for a full pool
                // and a mod calling the native wrongly look exactly the same from the outside:
                // no tracks, no error, nothing in any log.
                var handle = Function.Call<int>(
                    Hash.ADD_DECAL, left ? PrintDecal : PrintDecalAlt,
                    at.X, at.Y, at.Z + 0.06f,
                    0f, 0f, -1f,
                    side.X, side.Y, 0f,
                    size, size * 1.55f,
                    r, g, b, alpha,
                    600000f, false, false, false);

                return handle != 0;
            }
            catch
            {
                // No tracks, then. The rings still say where to look.
                return false;
            }
        }

        /// <summary>The two soles, and how big a print is. Taller than it is wide, because a foot is.</summary>
        private const int PrintDecal = 2040;
        private const int PrintDecalAlt = 2140;
        private const float PrintSize = 0.26f;

        // ======================================================================
        // The blade
        // ======================================================================

        /// <summary>
        /// Getting behind a man and putting a knife in him.
        ///
        /// THIS IS THE JOB NOW, AND THE RIFLE IS THE MISTAKE. Everything the hunt already had
        /// -- the tracks, the crouch, the line of sight, the lookouts on the corners -- was
        /// built to answer one question, "has he noticed you", and then the answer did not
        /// matter, because four hundred metres away through a scope he could not have done
        /// anything about it either way. Getting close enough to touch him is what makes all
        /// of that cost something.
        ///
        /// IT IS OFFERED, NOT TAKEN. The prompt only appears when it would actually work, and
        /// pressing it when it would not is a decision the card has warned you about in
        /// words. Nothing here fires off a swing you did not ask for: the ordinary knife is
        /// still under the attack button and it still kills people, slower and messier and
        /// without the rest of the block staying asleep.
        ///
        /// ONE ORIGIN, TWO ANIMATIONS, NO SYNCHRONISED SCENE. The clips come in matched pairs
        /// cut against each other, so playing both through TASK_PLAY_ANIM_ADVANCED at the SAME
        /// world position and heading puts each man exactly where the animator put him,
        /// relative to the other. It is the player's own position and heading, so he does not
        /// move and the target snaps the short distance into place. A synchronised scene would
        /// do the same thing with a handle to leak.
        /// </summary>
        private void Blade(Ped player, int now)
        {
            _within = null;
            _clean = false;
            _armed = false;

            if (_sticking != null) return;
            if (Phase != HuntPhase.Tracking) return;

            // STREAMED BEFORE IT IS WANTED. An animation dictionary takes a moment to arrive
            // and the moment it is wanted is the one frame a man has his back to you, so it is
            // asked for on every pass of the hunt rather than at the press.
            var ready = Loaded();

            Quarry near = null;
            var best = StickRange;

            foreach (var q in _out)
            {
                if (q.Down || q.Blown) continue;
                if (q.Man == null || !q.Man.Exists() || !q.Man.IsAlive) continue;

                var gap = q.Man.Position.DistanceTo(player.Position);
                if (gap > best) continue;

                near = q;
                best = gap;
            }

            if (near == null) return;

            _within = near;
            _armed = Armed(player);
            _clean = Unaware(near) && _armed && ready;

            if (!Pressed()) return;

            if (!ready)
            {
                Log.Debug("Hunt: the takedown clips were not streamed in yet.");
                return;
            }

            Stick(player, near, now);
        }

        /// <summary>Whether he has no idea you are there. Being SEEN ends the job on its own.</summary>
        private static bool Unaware(Quarry q)
        {
            return !q.Seen && q.Suspicion < AlertAt;
        }

        /// <summary>Whether the blade is the thing in his hand, rather than in the bag.</summary>
        private bool Armed(Ped player)
        {
            if (_blade == 0) return true;

            try { return Function.Call<uint>(Hash.GET_SELECTED_PED_WEAPON, player.Handle) == _blade; }
            catch { return true; }
        }

        /// <summary>
        /// Whether you are round the back of him.
        ///
        /// Only picks which animation plays -- there is a front takedown for the rare man who
        /// has not noticed somebody standing in front of him. Staying out of his eyeline is
        /// enforced by the suspicion, not by this. See BehindDot.
        /// </summary>
        private static bool Behind(Ped man, Ped player)
        {
            try
            {
                var fwd = man.ForwardVector;
                var to = player.Position - man.Position;

                var fl = (float)Math.Sqrt(fwd.X * fwd.X + fwd.Y * fwd.Y);
                var tl = (float)Math.Sqrt(to.X * to.X + to.Y * to.Y);

                if (fl < 0.001f || tl < 0.001f) return false;

                return (fwd.X * to.X + fwd.Y * to.Y) / (fl * tl) < BehindDot;
            }
            catch
            {
                return false;
            }
        }

        private static bool Pressed()
        {
            try { return Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context); }
            catch { return false; }
        }

        private static bool Loaded()
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, BladeDict);
                return Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, BladeDict);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Starts it: both men, one origin, and the clock that finishes it.</summary>
        private void Stick(Ped player, Quarry q, int now)
        {
            var clean = Unaware(q) && Armed(player);
            var behind = Behind(q.Man, player);

            var mine = !clean ? BotchPlayer : behind ? StealthPlayer : FrontPlayer;
            var his = !clean ? BotchVictim : behind ? StealthVictim : FrontVictim;

            var origin = player.Position;
            var heading = player.Heading;

            try
            {
                // NOTHING ELSE IS IN CHARGE OF EITHER OF THEM FOR THE NEXT COUPLE OF SECONDS.
                // A ragdoll halfway through is two men on the floor in the wrong poses, and a
                // leftover task is the target trying to walk to his next spot with a knife in
                // his neck.
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, q.Man.Handle);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, q.Man.Handle, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, player.Handle, false);

                Lay(player, mine, origin, heading);
                Lay(q.Man, his, origin, heading);
            }
            catch (Exception ex)
            {
                Log.Debug("Hunt: the takedown would not start: " + ex.Message);
                return;
            }

            _sticking = q;
            q.Sticking = true;

            _stickFrom = now;
            _stickMs = Length(mine);
            _stuck = clean;
            _killed = false;

            Log.Info("Hunt: " + (clean ? "a clean one" : "a botched grab") + " -- " + mine + ".");
        }

        /// <summary>One half of the pair, played at the scene's origin rather than the ped's.</summary>
        private static void Lay(Ped who, string clip, Vector3 at, float heading)
        {
            Function.Call(Hash.TASK_PLAY_ANIM_ADVANCED, who.Handle, BladeDict, clip,
                          at.X, at.Y, at.Z,
                          0f, 0f, heading,
                          4f, -4f, -1, 0, 0f, 2, 0);
        }

        /// <summary>How long the clip runs, or a sensible couple of seconds if it will not say.</summary>
        private static int Length(string clip)
        {
            try
            {
                var ms = (int)(Function.Call<float>(Hash.GET_ANIM_DURATION, BladeDict, clip) * 1000f);

                return ms > 200 && ms < StickMostMs ? ms : 2600;
            }
            catch
            {
                return 2600;
            }
        }

        /// <summary>
        /// The takedown, frame by frame, until it is over.
        ///
        /// RUN FROM THE TICK ITSELF rather than from the tracking phase. A man being stuck can
        /// be the third man, and the third man going down moves the hunt to Leaving on the
        /// same frame -- so a version of this that lived inside Tracking would drop the clip
        /// halfway through and leave the player permanently unable to ragdoll.
        /// </summary>
        private void Sticking(Ped player, int now)
        {
            var q = _sticking;
            var gone = now - _stickFrom;

            Hold();

            if (q.Man == null || !q.Man.Exists())
            {
                Let(player);
                Vanished(q);
                return;
            }

            // HE DIES NEAR THE END OF IT, NOT AT THE START. Killing him on the first frame
            // ragdolls him out of the animation and the two of them finish the scene apart;
            // killing him at the end lets the clip put him on the floor and then makes it
            // true. See DiesAt.
            if (_stuck && !_killed && gone >= (int)(_stickMs * DiesAt))
            {
                _killed = true;
                q.Knifed = true;

                // What is left on the pavement. The same mark the blood trail uses.
                Spot(q.Man.Position, q.Man.ForwardVector, 0.55f, 0.06f, 0.05f, PrintSize * 1.8f, true);

                try { Function.Call(Hash.SET_ENTITY_HEALTH, q.Man.Handle, 0); }
                catch { /* the clip still played */ }
            }

            if (gone < _stickMs && gone < StickMostMs) return;

            var clean = _stuck;

            Let(player);

            if (clean)
            {
                // Dropped comes off !IsAlive on the next pass of the hunt, which is where the
                // count and the line live. Nothing to do here.
                return;
            }

            Botched(q);
        }

        /// <summary>
        /// Nothing the player presses gets him out of it.
        ///
        /// The animation is tasked, so walking away would not actually cancel it -- but a
        /// player mashing the stick during a two-second scene with the movement still live is
        /// a man skating across the pavement in a takedown pose.
        /// </summary>
        private static void Hold()
        {
            try
            {
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.MoveLeftRight, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.MoveUpDown, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.Attack, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.Attack2, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.MeleeAttack1, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.MeleeAttack2, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.Aim, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.Jump, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.Duck, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.Cover, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.Enter, true);
            }
            catch
            {
                // Then he can wander off mid-stab, which looks daft and breaks nothing.
            }
        }

        /// <summary>Hands both of them back to themselves.</summary>
        private void Let(Ped player)
        {
            try
            {
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, player.Handle, true);
                Function.Call(Hash.CLEAR_PED_TASKS, player.Handle);

                if (_sticking != null && _sticking.Man != null && _sticking.Man.Exists())
                {
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL, _sticking.Man.Handle, true);
                }
            }
            catch
            {
                // They come back on their own.
            }

            if (_sticking != null) _sticking.Sticking = false;

            _sticking = null;
            _stuck = false;
            _killed = false;
            _stickMs = 0;
        }

        /// <summary>
        /// You grabbed at a man who was already listening, and he got a hand up.
        ///
        /// It is the same outcome as being seen, because it IS being seen -- with your hands
        /// on him. The card says he is onto you before you press it; this is what pressing it
        /// anyway costs.
        /// </summary>
        private void Botched(Quarry q)
        {
            Blown(q);
        }

        // ======================================================================
        // Losing them, and dropping them
        // ======================================================================

        /// <summary>
        /// He noticed you, and now he is coming.
        ///
        /// HE USED TO RUN AND IT USED TO END THE JOB. One man spotting you failed the hunt
        /// outright -- which meant the only way to play it was perfectly, and a stalk you had
        /// half blown was a reload rather than a decision. He turns round now: the events
        /// block comes off, he is handed back to the game's own gang hatred, and he tries to
        /// kill you. He stops counting toward the three, and that is the whole of the cost.
        ///
        /// AND HE IS LOUD, which is the rest of the cost. A fight in the street is a fight in
        /// the street: everybody nearby hears it, so blowing the first one makes the second
        /// one harder without a single rule being written to say so.
        ///
        /// THE MARK STAYS ON HIM. You are allowed to finish what you started -- it will not
        /// count, but leaving an armed man behind you on the way to the next corner is worse.
        /// </summary>
        private void Blown(Quarry q)
        {
            if (q.Down || q.Blown) return;

            q.Blown = true;

            try
            {
                // HANDED BACK TO THE GAME. Make blocks his permanent events so the engine's
                // own gang hatred cannot turn a stalk into a shootout; a man who has seen you
                // is exactly the case where that hatred is what you want.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, q.Man.Handle, false);
                Function.Call(Hash.SET_PED_GENERATES_DEAD_BODY_EVENTS, q.Man.Handle, true);
                Function.Call(Hash.SET_PED_ALERTNESS, q.Man.Handle, 3);
                Function.Call(Hash.SET_PED_KEEP_TASK, q.Man.Handle, true);
                Function.Call(Hash.CLEAR_PED_TASKS, q.Man.Handle);
                Function.Call(Hash.TASK_COMBAT_PED, q.Man.Handle,
                              Game.Player.Character.Handle, 0, 16);
            }
            catch
            {
                // Then he stands there looking at you, which is its own warning.
            }

            try { if (q.Area != null && q.Area.Exists()) q.Area.Delete(); }
            catch { /* it goes with the job */ }

            q.Area = null;

            Word("he's seen us. do him or leave him", SpeechWorst);

            Log.Info("Hunt: number " + (q.Which + 1) + " is onto us. " + Down + " down, " +
                     Lost + " written off, " + Need + " needed.");
        }

        /// <summary>
        /// The man himself has stopped existing, which is the engine and not you.
        ///
        /// Written off the same as a blown one, and for the same reason: the count has to be
        /// able to finish. It does not say you were seen, because you were not.
        /// </summary>
        private void Vanished(Quarry q)
        {
            q.Blown = true;

            Unring(q);

            Log.Warn("Hunt: number " + (q.Which + 1) + " stopped existing. Written off.");
        }

        /// <summary>
        /// Down, and how he went down.
        ///
        /// HE DIED WITHOUT NOTICING OR HE DIED KNOWING. That is the only distinction left
        /// -- a man who spots you stops counting before he can be dropped, so everything that
        /// reaches here is a kill that worked -- and it is worth a different sentence, because
        /// the whole point of the blade is that the quiet one is the one to aim for.
        /// </summary>
        private void Dropped(Quarry q, int now)
        {
            q.Down = true;

            Unring(q);

            // COUNTED AGAINST THREE, which is what passes the job. A fourth is a bonus and
            // ought not to be announced as though it were the finish line.
            var line = Down >= Need
                ? "that's three. we out"
                : Down == 1
                    ? (q.Knifed ? "one down. he never even turned round" : "one down. two more")
                    : (q.Knifed ? "two. one more, do him the same way" : "two. one more");

            Word(line, Down >= Need ? SpeechBest : SpeechGood);

            Log.Info("Hunt: number " + (q.Which + 1) + " down" + (q.Knifed ? ", quietly" : "") +
                     ". " + Down + " of " + Need + " needed.");
        }

        // ======================================================================
        // Grove Street, and getting out
        // ======================================================================

        /// <summary>How far from the last corner a Balla has to be to have noticed.</summary>
        private const float GroveHears = 70f;

        /// <summary>Near enough to Lamar's to call it getting back.</summary>
        private const float HomeWithin = 28f;

        /// <summary>Every Balla model the block might be wearing. See Grove.</summary>
        private static readonly string[] Ballas =
        {
            "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01",
            "g_f_y_ballas_01", "csb_ballasog", "g_m_y_ballablazer_01"
        };

        /// <summary>
        /// The last one is on somebody's block, and the block works it out.
        ///
        /// THE OTHER THREE ARE CORNERS AND THIS ONE IS A STREET. Three men stood on their own
        /// outside a liquor store can be taken quietly and nobody is any the wiser -- which is
        /// the whole point of the knife and is enforced everywhere else by switching the dead
        /// body events off. Grove Street is the exception on purpose: it is the only one of
        /// the four with people on it who would notice, so the job ends with a street turning
        /// round rather than with a fourth quiet exit.
        ///
        /// WHOEVER IS ACTUALLY THERE, which is the honest version of it. Nobody is spawned to
        /// make this happen. If the block is empty at four in the morning then the block is
        /// empty and you walk home, and that is a better answer than a scripted ambush that
        /// arrives whatever the street looks like.
        /// </summary>
        private void Grove(Ped player)
        {
            var turned = 0;

            try
            {
                var at = Stands[Last].At;

                foreach (var man in World.GetNearbyPeds(at, GroveHears))
                {
                    if (man == null || !man.Exists() || !man.IsAlive) continue;
                    if (man.Handle == player.Handle) continue;
                    if (_lamar != null && _lamar.Exists() && man.Handle == _lamar.Handle) continue;
                    if (Mine(man)) continue;
                    if (!Balla(man)) continue;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, false);
                    Function.Call(Hash.SET_PED_ALERTNESS, man.Handle, 3);
                    Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, true);
                    Function.Call(Hash.CLEAR_PED_TASKS, man.Handle);
                    Function.Call(Hash.TASK_COMBAT_PED, man.Handle, player.Handle, 0, 16);

                    turned++;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Hunt: could not turn the block: " + ex.Message);
            }

            Log.Info("Hunt: Grove Street -- " + turned + " of them turned. " +
                     Down + " of " + Many + " down, " + Need + " needed.");

            if (turned > 0)
            {
                Word("grove seen us. get back to lamar's", SpeechWorst);
            }
            else
            {
                Word("that's the last one. we out", SpeechBest);
            }
        }

        /// <summary>One of ours, in this job. Not to be turned on us by the last kill.</summary>
        private bool Mine(Ped man)
        {
            foreach (var q in _out)
            {
                if (q.Man != null && q.Man.Exists() && q.Man.Handle == man.Handle) return true;
            }

            return false;
        }

        private static bool Balla(Ped man)
        {
            try
            {
                var hash = man.Model.Hash;

                foreach (var name in Ballas)
                {
                    if (hash == Function.Call<int>(Hash.GET_HASH_KEY, name)) return true;
                }
            }
            catch
            {
                // Then he is somebody else's problem.
            }

            return false;
        }

        // ======================================================================
        // Lamar
        // ======================================================================

        /// <summary>
        /// He comes with you, in the car or on foot, and he does not get in the way of a rifle.
        ///
        /// IN WHATEVER YOU ARE IN. The ride out said "with Lamar" and nothing put him in the
        /// car: he was left on the corner and turned up on the block, if he turned up at all,
        /// by the fixer's own despawn rules. Now he gets in whatever you get in, is warped
        /// into it if you have driven off without him and nobody can see him, and gets out
        /// when you do.
        ///
        /// ON FOOT, BEHIND YOU, AT YOUR PACE. Walking when you walk, running when you are
        /// away from him, crouched when you are crouched -- the same stealth movement you are
        /// using -- and STOOD STILL when you are looking down the glass with him near, because
        /// a man walking across a scope is the end of a hunt. Tasked when that changes, and
        /// otherwise left alone: the old version re-issued his follow task every frame, which
        /// is a man who never finishes starting to walk.
        /// </summary>
        private void Lamar(Ped player, int now)
        {
            if (_lamar == null || !_lamar.Exists())
            {
                if (Fixer != null) _lamar = Fixer();
                if (_lamar == null || !_lamar.Exists()) return;

                try
                {
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _lamar.Handle, true, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _lamar.Handle, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _lamar.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _lamar.Handle, false);
                }
                catch
                {
                    // He follows either way.
                }

                _lamarDoing = Walk.None;
                _lamarAt = 0;
            }

            if (!_lamar.IsAlive)
            {
                Failure = "lamar's down. that's the job.";
                return;
            }

            // ---- in the car ----
            if (player.IsInVehicle())
            {
                var car = player.CurrentVehicle;
                if (car == null || !car.Exists()) return;

                if (_lamar.IsInVehicle(car))
                {
                    _lamarDoing = Walk.Riding;
                    return;
                }

                var gap = _lamar.Position.DistanceTo(car.Position);

                // Driven off without him and nobody looking: he is simply in the car. A man
                // sprinting after a car for three streets is not a passenger, it is a chase.
                if (gap > 60f && !_lamar.IsOnScreen)
                {
                    var seat = FreeSeat(car);

                    if (seat != NoSeat)
                    {
                        try
                        {
                            Function.Call(Hash.SET_PED_INTO_VEHICLE, _lamar.Handle, car.Handle, seat);
                            _lamarDoing = Walk.Riding;
                        }
                        catch
                        {
                            // Next pass.
                        }
                    }

                    return;
                }

                if (_lamarDoing == Walk.Boarding && now - _lamarAt < LamarRetaskMs) return;

                _lamarDoing = Walk.Boarding;
                _lamarAt = now;

                try
                {
                    Stealth(false);
                    Function.Call(Hash.CLEAR_PED_TASKS, _lamar.Handle);
                    Function.Call(Hash.TASK_ENTER_VEHICLE, _lamar.Handle, car.Handle, -1, -2, 2f, 1, 0);
                }
                catch
                {
                    // He will find his own way in.
                }

                return;
            }

            // ---- you are out and he is not ----
            if (_lamar.IsInVehicle())
            {
                if (_lamarDoing == Walk.Leaving && now - _lamarAt < LamarRetaskMs) return;

                _lamarDoing = Walk.Leaving;
                _lamarAt = now;

                try
                {
                    var ride = _lamar.CurrentVehicle;

                    Function.Call(Hash.TASK_LEAVE_VEHICLE, _lamar.Handle,
                                  ride == null || !ride.Exists() ? 0 : ride.Handle, 0);
                }
                catch
                {
                    // He gets out on the next pass.
                }

                return;
            }

            // ---- on foot ----
            var away = _lamar.Position.DistanceTo(player.Position);

            // Left a long way behind and out of sight: put behind you rather than made to run
            // three blocks. Same rule as the car.
            if (away > 90f && !_lamar.IsOnScreen)
            {
                try
                {
                    var behind = player.Position - player.ForwardVector * 5f;
                    var on = World.GetNextPositionOnSidewalk(behind);
                    if (on != Vector3.Zero && on.DistanceTo(behind) < 10f) behind = on;

                    Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _lamar.Handle,
                                  behind.X, behind.Y, behind.Z, false, false, false);
                }
                catch
                {
                    // Then he runs.
                }

                _lamarDoing = Walk.None;
            }

            var crouched = Crouching(player);
            var aiming = Game.Player.IsAiming;

            Stealth(crouched);

            Walk want;

            if (aiming && away < LamarRunFrom) want = Walk.Still;
            else if (player.IsSprinting || away > LamarRunFrom) want = Walk.Run;
            else if (_lamarDoing == Walk.Run && away > LamarWalkFrom) want = Walk.Run;
            else want = Walk.Follow;

            // Re-issued on a change, and every few seconds while he is lagging, in case the
            // task he had fell off. Never every frame.
            var lagging = away > LamarRunFrom && now - _lamarAt > LamarRetaskMs;

            if (want == _lamarDoing && !lagging) return;

            _lamarDoing = want;
            _lamarAt = now;

            try
            {
                if (want == Walk.Still)
                {
                    Function.Call(Hash.TASK_STAND_STILL, _lamar.Handle, -1);
                }
                else
                {
                    Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, _lamar.Handle, player.Handle,
                                  -0.9f, -LamarBehind, 0f, want == Walk.Run ? 2f : 1f, -1, 2.5f, true);
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, _lamar.Handle, true);
            }
            catch
            {
                // He catches up on his own.
            }
        }

        /// <summary>His crouch follows yours. Set only on a change; the native is not free.</summary>
        private void Stealth(bool on)
        {
            if (on == _lamarStealth) return;
            _lamarStealth = on;

            try
            {
                Function.Call(Hash.SET_PED_STEALTH_MOVEMENT, _lamar.Handle, on, "DEFAULT_ACTION");
            }
            catch
            {
                // He walks upright, then.
            }
        }

        private const int NoSeat = int.MinValue;

        /// <summary>A passenger seat with nobody in it, or NoSeat. Same as the mission runner's.</summary>
        private static int FreeSeat(Vehicle ride)
        {
            try
            {
                var seats = Function.Call<int>(Hash.GET_VEHICLE_MODEL_NUMBER_OF_SEATS, ride.Model.Hash);

                for (var seat = 0; seat <= seats - 2; seat++)
                {
                    if (Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, ride.Handle, seat, false))
                    {
                        return seat;
                    }
                }
            }
            catch
            {
                // No seat, then.
            }

            return NoSeat;
        }

        /// <summary>
        /// Lamar says something, out loud.
        ///
        /// HE USED TO TEXT YOU. Every beat of this job -- get out there, read the ground, one
        /// down, follow the blood -- arrived as a PHONE MESSAGE from a man sitting in your
        /// passenger seat, and that is the wrong medium for all of it. You are crouched behind
        /// a fence watching a man's back; the thing that tells you it worked should not be a
        /// notification sliding in over the corner of the screen, and it certainly should not
        /// be a text from somebody four feet away.
        ///
        /// SO IT IS AUDIO, AND THE WORDS ARE HALF A FILENAME. Voice.Say hashes the exact
        /// sentence into the name of a recording, so the lines written here ARE the to-record
        /// list -- the log prints the name it wanted, you record it under that name, and he
        /// says it. See tools/voice_lines.py.
        ///
        /// AND THE GAME'S OWN BANKS WHEN THERE IS NO RECORDING, so a fresh install is not a
        /// silent job. Only for the beats that are GOOD or BAD -- a man down, a man winged, a
        /// man spooked, a phone coming out. The quiet in between is the point of a stalk and
        /// filling it with barks would undo the whole thing.
        ///
        /// THE VOICE IS NAMED RATHER THAN INHERITED. PLAY_PED_AMBIENT_SPEECH_NATIVE uses the
        /// ped's OWN voice and fails in silence when that voice has no such line -- which is
        /// exactly what was happening to the call across the block: it asked for
        /// GENERIC_INSULT_HIGH, which LAMAR_1_NORMAL does not carry, so the loudest moment in
        /// the job made no sound at all. Naming the voice means the name can be checked
        /// against the install's own speech list, and every name in this file has been.
        ///
        /// AGAIN EVERY TIME. These are reactions, not performances -- see Voice.Say's `again`.
        /// A hunt whose second outing is silent because the first used the lines up would be
        /// worse than one with no audio at all.
        ///
        /// WHAT TO DO NEXT IS NOT IN HERE. That is the card's job and it is a different kind
        /// of sentence -- see Step. This is a man talking; that is an instruction.
        /// </summary>
        private void Word(string words, string speech = null)
        {
            var spoke = false;

            if (!string.IsNullOrEmpty(words))
            {
                try { spoke = Core.Voice.Say("lamar", words, null, true); }
                catch { /* then the banks below, or nothing */ }
            }

            if (spoke || string.IsNullOrEmpty(speech)) return;

            try
            {
                // HIM IF HE IS THERE, the player's own ped if he is not. The line is a sound
                // coming from somewhere nearby either way, and the alternative is the start of
                // the job being silent because Lamar has not been fetched yet.
                var who = _lamar != null && _lamar.Exists() && _lamar.IsAlive
                    ? _lamar
                    : Game.Player.Character;

                if (who == null || !who.Exists()) return;

                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_WITH_VOICE_NATIVE, who.Handle,
                              speech, LamarVoice, "SPEECH_PARAMS_FORCE", 0);
            }
            catch
            {
                // A quiet job is still a job.
            }
        }

        /// <summary>
        /// The voice bank his lines come out of, and every speech name in this file is in it.
        ///
        /// Checked against menyooStuff\PedSpeechList.txt on this machine: LAMAR_1_NORMAL and
        /// LAMAR_2_NORMAL carry DIFFERENT sets, which is how the old call across the block
        /// ended up asking for a line that only the other one has.
        /// </summary>
        private const string LamarVoice = "LAMAR_1_NORMAL";

        /// <summary>Good: a man down, the job finished.</summary>
        private const string SpeechGood = "GAME_GOOD_SELF";
        private const string SpeechBest = "GAME_WIN_SELF";

        /// <summary>
        /// What he says when the next one is close enough to start working out.
        ///
        /// SIXTY METRES IS THE RANGE AT WHICH IT IS STILL A PLAN. Closer and he is telling you
        /// something you have already done; further and it is a man narrating a map. This is
        /// the beat between arriving on the street and picking your approach, and it is the
        /// only one in the job that was silent.
        ///
        /// ONCE PER MAN, NOT ON A CLOCK. He says it when you first get near this one and then
        /// leaves you alone, because a friend who repeats the plan every twenty seconds while
        /// you are crouched behind a fence is not a friend.
        ///
        /// Four of them, picked at random. See tools/voice_lines.py for the file names.
        /// </summary>
        private static readonly string[] Nudges =
        {
            "there he go. let's go round the back of him and jump him",
            "alright, let's sneak round and get this fool",
            "that's him. get behind him and don't let him turn round",
            "easy now. round the back of him, quick and quiet"
        };

        /// <summary>How near the next one has to be before he says one. See Nudges.</summary>
        private const float NudgeRange = 60f;

        /// <summary>Bad: winged him, lost him, somebody is on the phone.</summary>
        private const string SpeechBad = "GENERIC_CURSE_MED";
        private const string SpeechWorst = "GENERIC_CURSE_HIGH";
        private const string SpeechSeen = "ENEMY_SPOTTED";

        // ======================================================================
        // Leaving
        // ======================================================================

        /// <summary>
        /// Back to Lamar's.
        ///
        /// IT USED TO BE "GET FORTY METRES AWAY FROM WHERE YOU WERE STOOD", which is not
        /// leaving, it is stepping back. The job started in that yard and it ends there, and
        /// with the last block behind you it is a run rather than a formality.
        /// </summary>
        private void Leaving(Ped player, int now)
        {
            MarkField();

            if (player.Position.DistanceTo(Home) > HomeWithin) return;

            ReadyToCollect = true;
        }

        // ======================================================================
        // The card
        // ======================================================================

        /// <summary>
        /// The hunting card: how many are left, whether anybody is looking at you, and how
        /// close the nearest one is to hearing you.
        ///
        /// THE LOOKOUT READOUT IS WHAT THE WIND ARROW WAS. Both exist for the same reason -- a
        /// rule you cannot see the state of is a rule you cannot play around -- and this one
        /// has the advantage of being about something that is actually on the map. The word
        /// goes amber while somebody has a look going and red once the phone is out, which is
        /// the only warning there is that you have about four seconds to do something.
        /// </summary>
        public void Draw()
        {
            if (!IsRunning) return;
            if (Phase == HuntPhase.Riding) return;

            const float w = 0.206f;
            const float h = 0.108f;

            var left = 0.5f - w * 0.5f;
            var top = 0.770f;

            Theme.Panel(left, top, w, h);

            var x = left + 0.011f;
            var right = left + w - 0.011f;

            Hud.Text("THE HUNT", x, top + 0.005f, 0.25f,
                     Palette.Alpha(Palette.TextDim, 200), Hud.FontLabel, centre: false);

            // AGAINST THREE, NOT AGAINST FOUR. Three is what passes the job -- see Need --
            // and a counter that reads "2 / 4" while you are one man from finishing is a
            // counter lying about the only number anybody cares about. The fourth is the
            // spare, and losing him is what it is for.
            var count = Down + " / " + Need;
            var countInk = Down >= Need ? Palette.Brand : Palette.Text;

            Hud.TextRight(count, right, top + 0.003f, 0.34f, countInk, Hud.FontLabel);

            Compass(x, top + 0.030f, w - 0.022f);

            // ---- which corner is next, and who is written off ----
            var which = Next;
            var lost = Lost;

            var where = Phase == HuntPhase.Leaving
                ? "LAMAR'S"
                : which < 0 ? "" : Stands[which].Street;

            if (!string.IsNullOrEmpty(where))
            {
                Hud.Text(where, x, top + 0.058f, 0.22f,
                         Palette.Alpha(Phase == HuntPhase.Leaving ? Palette.Brand : Palette.Warn, 235),
                         Hud.FontLabel, centre: false);
            }

            if (lost > 0)
            {
                Hud.TextRight(lost + " BLOWN", right, top + 0.058f, 0.20f,
                              Palette.Alpha(Palette.Danger, 200), Hud.FontLabel);
            }

            // ---- how close the nearest one is to hearing you ----
            var worst = 0f;

            foreach (var q in _out)
            {
                if (q.Down || q.Blown) continue;
                if (q.Suspicion > worst) worst = q.Suspicion;
            }

            var barX = x;
            var barW = right - barX;

            Hud.RectFrom(barX, top + 0.0705f, barW, 0.0055f,
                         Color.FromArgb(90, 255, 255, 255));

            if (worst > 0.01f)
            {
                var ink = worst > 0.66f ? Palette.Danger : worst > 0.33f ? Palette.Warn : Palette.Brand;

                Hud.RectFrom(barX, top + 0.0705f, barW * Math.Min(1f, worst), 0.0055f, ink);
            }

            // ---- and what to do about all of it ----
            Step(left, top + h - 0.020f, w);
        }

        /// <summary>
        /// One line, at the bottom of the card, saying what to do next.
        ///
        /// THIS IS WHAT THE TEXT MESSAGES WERE FOR AND THEY WERE NEVER ANY GOOD AT IT. Lamar
        /// used to phone the instructions through -- "read the ground", "follow the blood" --
        /// which meant the one thing you needed to know arrived once, slid off the corner of
        /// the screen, and was gone by the time you had finished turning round. An instruction
        /// is not news. It should be on the screen for exactly as long as it is true and then
        /// be replaced by the next one, which is what a line on the card is and what a
        /// notification can never be. See Word for what he does instead.
        ///
        /// IT ANSWERS THE STATE, NOT THE SCRIPT. Nothing sets this; it is read off the same
        /// things the rest of the card is drawn from, so it cannot get out of step with them.
        /// The order is the order the trouble comes in: a phone being dialled outranks a man
        /// within reach, and a man within reach outranks the tracks.
        /// </summary>
        private void Step(float left, float top, float w)
        {
            string words;
            var ink = Palette.Alpha(Palette.Text, 235);

            if (_sticking != null)
            {
                return;
            }
            else if (Phase == HuntPhase.Leaving)
            {
                words = "GET BACK TO LAMAR'S";
                ink = Palette.Brand;
            }
            else if (Fighting)
            {
                words = "HE'S ON YOU -- HE WON'T COUNT NOW";
                ink = Palette.Danger;
            }
            else if (_within != null && !_armed)
            {
                words = "PULL THE BLADE OUT";
                ink = Palette.Warn;
            }
            else if (_within != null && _clean)
            {
                words = Key + "   STICK HIM";
                ink = Palette.Brand;
            }
            else if (_within != null)
            {
                words = "HE'S ONTO YOU -- BACK OFF";
                ink = Palette.Danger;
            }
            else if (Marked)
            {
                words = "STAY LOW AND GET ROUND THE BACK OF HIM";
            }
            else if (Next >= 0 && Of(Next) == null)
            {
                words = "GET DOWN TO " + Stands[Next].Street;
            }
            else
            {
                words = "READ THE GROUND -- FIND HIS TRACKS";
            }

            // A PLATE UNDER IT, because this line changes while everything above it stays put,
            // and a line that changes needs to look like the part of the card that changes.
            Hud.RectFrom(left + 0.008f, top - 0.0035f, w - 0.016f, 0.0155f,
                         Color.FromArgb(38, ink.R, ink.G, ink.B));

            Hud.RectFrom(left + 0.008f, top - 0.0035f, 0.0016f, 0.0155f,
                         Palette.Alpha(ink, 210));

            Hud.Text(words, left + w * 0.5f, top, 0.235f, ink, Hud.FontLabel);
        }

        /// <summary>What to press, in the words of whatever he is holding it with.</summary>
        private static string Key => Hud.OnPad ? "[RB]" : "[E]";

        /// <summary>One of them has noticed and is coming, and he is still on his feet.</summary>
        private bool Fighting
        {
            get
            {
                foreach (var q in _out)
                {
                    if (!q.Blown) continue;
                    if (q.Man == null || !q.Man.Exists() || !q.Man.IsAlive) continue;

                    return true;
                }

                return false;
            }
        }

        /// <summary>You have somebody's trail, so there is a man to get behind rather than find.</summary>
        private bool Marked
        {
            get
            {
                foreach (var q in _out)
                {
                    if (q.Down || q.Blown) continue;
                    if (q.Found) return true;
                }

                return false;
            }
        }

        // ======================================================================
        // The compass
        // ======================================================================

        /// <summary>How wide a slice of the world the strip covers, each side of straight ahead.</summary>
        private const float CompassHalfFov = 90f;

        /// <summary>The strip, the marks on it, and the room the caret needs above and below.</summary>
        private const float CompassH = 0.0155f;
        private const float CompassLip = 0.0062f;
        private const float PipW = 0.0026f;
        private const float VaguePipW = 0.0090f;

        /// <summary>
        /// Where the ends start dimming, as a fraction of the half width.
        ///
        /// A MARK THAT VANISHES IS A MARK THAT POPPED. The strip used to end in a hard edge,
        /// so a man walking round you crossed it as a full-strength pip that simply stopped
        /// existing, and the eye read that as the mod losing him rather than as him leaving
        /// the slice you can see. Fading the last fifth of each end costs nothing and turns
        /// the same event into something going out of view.
        /// </summary>
        private const float CompassFade = 0.78f;

        /// <summary>
        /// Which way they are, from where you are looking.
        ///
        /// A HUNT ON FOOT WITH A RING ON THE MAP IS A HUNT PLAYED IN THE PAUSE MENU. The ring
        /// says where he is to within forty metres and the trail says where he went, and
        /// neither is any use at all while you are walking with your head up -- so the state
        /// everybody was actually reading was the map screen, and the field was a place you
        /// crossed between map checks. This is the same knowledge, in front of you.
        ///
        /// WHAT IT KNOWS IS WHAT YOU KNOW, and that is the point. Before you have his trail
        /// the pip is wide, dim and sits on the RING'S centre -- which is slipped off him by
        /// up to sixteen metres, so it is a direction to search in and not a solution. Once
        /// you have stood on his prints the pip goes narrow and green and follows the man
        /// himself. The compass sharpens as you learn, the way the map already did.
        ///
        /// AND IT IS THE CAMERA'S HEADING, NOT HIS. You look around far more than you turn,
        /// and a compass that answered to his feet would sit still while you searched.
        ///
        /// WHAT CHANGED WHEN IT WAS MADE TO LOOK LIKE SOMETHING. It was a black bar with
        /// coloured rectangles on it, which is a debug readout rather than an instrument:
        /// nothing on it said how far round anything was, the letters were four dim pixels
        /// each, and a pip at the end of the strip was indistinguishable from a pip anywhere
        /// else. It now has DEGREES on it -- a tick every fifteen, a longer one on the
        /// diagonals, a letter on the quarters -- so the marks move against something and
        /// turning ninety degrees looks like turning ninety degrees. The pips grow up from the
        /// floor of the strip rather than floating in the middle of it, the ends fade, there
        /// is a caret over the centre saying which way is forward, and the nearest man you
        /// actually have a fix on gets a second caret UNDER the strip pointing at him. All of
        /// it is DRAW_RECT, so it costs about thirty rectangles and nothing streamed.
        /// </summary>
        private void Compass(float x, float top, float w)
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var here = me.Position;

            // ASKED ONCE A FRAME. Every pip and every letter is measured against the same
            // heading, and a native call per mark is a native call per mark for an answer
            // that cannot change between them.
            float look;

            try { look = Heading(GameplayCamera.Direction); }
            catch { return; }

            var mid = x + w * 0.5f;
            var half = w * 0.5f;

            // ---- the strip ----
            Hud.RectFrom(x, top, w, CompassH, Color.FromArgb(118, 0, 0, 0));
            Hud.RectFrom(x, top + CompassH, w, 0.0012f, Palette.Alpha(Palette.TextDim, 48));

            // ---- the degrees ----
            //
            // Walked in world headings rather than in screen positions, so every mark is
            // measured the same way the men are and nothing can drift out of step with them.
            for (var deg = 0; deg < 360; deg += 15)
            {
                var off = Wrap(look - deg);
                if (Math.Abs(off) > CompassHalfFov) continue;

                var at = mid + off / CompassHalfFov * half;
                var dim = Edge(off);

                if (deg % 90 == 0)
                {
                    Hud.Text(Letter(deg), at, top + 0.0018f, 0.165f,
                             Palette.Alpha(Palette.TextDim, (int)(180f * dim)), Hud.FontLabel);

                    continue;
                }

                var major = deg % 45 == 0;

                Hud.RectFrom(at - 0.0006f, top, 0.0012f, CompassH * (major ? 0.42f : 0.24f),
                             Palette.Alpha(Palette.TextDim, (int)((major ? 130f : 74f) * dim)));
            }

            // ---- straight ahead, so a mark under the caret means walk forward ----
            Caret(mid, top - CompassLip, 0.0036f, CompassLip - 0.0012f,
                  Palette.Alpha(Palette.Text, 200), true);

            // ---- the men ----
            Quarry fix = null;
            var fixOff = 0f;
            var fixGap = float.MaxValue;

            foreach (var q in _out)
            {
                // A BLOWN ONE STILL GETS A PIP, IN RED. He does not count any more, but he is
                // walking at you with a bat and he is the single most important thing on this
                // strip while he does it.
                var fighting = q.Blown && q.Man != null && q.Man.Exists() && q.Man.IsAlive;

                if (q.Down || (q.Blown && !fighting)) continue;

                var known = fighting ||
                            (q.Found && q.Man != null && q.Man.Exists() && q.Man.IsAlive);

                Vector3 at;

                if (known)
                {
                    at = q.Man.Position;
                }
                else if (q.Area != null && q.Area.Exists())
                {
                    // The ring, slipped off him when it was placed. See Ring.
                    at = q.Area.Position;
                }
                else
                {
                    continue;
                }

                // NEARNESS IS THE PIP'S HEIGHT, and there is no number anywhere on this card
                // for it. A range readout turns a hunt into a walk down a decreasing number;
                // a pip that grows as you close says warmer without saying where.
                var far = here.DistanceTo(at);
                var near = far <= 25f ? 1f : far >= 200f ? 0.30f : 1f - (far - 25f) / 175f * 0.70f;

                var off = Off(look, here, at);

                // GROWN UP OFF THE FLOOR OF THE STRIP rather than floating in the middle of
                // it. A row of marks standing on one line reads as a chart; the same marks
                // centred read as scattered.
                var tall = CompassH * near;

                Pip(x, top + CompassH - tall, w, mid, off,
                    known ? PipW : VaguePipW, tall,
                    fighting ? Palette.Danger
                             : known ? Palette.Brand : Palette.Alpha(Palette.TextDim, 150));

                if (fighting || !known || far >= fixGap) continue;

                fix = q;
                fixOff = off;
                fixGap = far;
            }

            // ---- and a second caret under the nearest one you actually have a fix on ----
            //
            // Under, not over, so it cannot be confused with the forward mark. This is the
            // only thing on the strip that says WHICH of them to walk at.
            if (fix != null && Math.Abs(fixOff) <= CompassHalfFov)
            {
                var at = mid + fixOff / CompassHalfFov * half;

                Caret(at, top + CompassH + 0.0022f, 0.0030f, CompassLip - 0.0020f,
                      Palette.Alpha(Palette.Brand, (int)(225f * Edge(fixOff))), false);
            }

        }

        /// <summary>How much of its colour a mark keeps this near the end of the strip.</summary>
        private static float Edge(float off)
        {
            var t = Math.Abs(off) / CompassHalfFov;

            if (t <= CompassFade) return 1f;
            if (t >= 1f) return 0f;

            return 1f - (t - CompassFade) / (1f - CompassFade);
        }

        /// <summary>The quarter letters, laid out the way the map is.</summary>
        private static string Letter(int deg)
        {
            switch (deg)
            {
                case 90: return "N";
                case 0: return "E";
                case 270: return "S";
                default: return "W";
            }
        }

        /// <summary>
        /// A little triangle, pointing down at the strip or up at it.
        ///
        /// Stacked rectangles rather than a sprite, for the same reason the wedges in the
        /// wheel are: DRAW_RECT has no texture to stream and no rotation to get wrong, and at
        /// this size five rows is already smoother than the pixels it lands on.
        /// </summary>
        private static void Caret(float cx, float top, float halfWide, float tall, Color ink,
                                  bool down)
        {
            if (tall <= 0f || halfWide <= 0f || ink.A <= 0) return;

            const int rows = 5;

            var rowH = tall / rows;

            for (var i = 0; i < rows; i++)
            {
                var t = down ? i / (float)rows : 1f - (i + 1) / (float)rows;
                var wide = halfWide * (1f - t);

                if (wide <= 0.0002f) continue;

                Hud.RectFrom(cx - wide, top + i * rowH, wide * 2f, rowH + 0.0004f, ink);
            }
        }

        /// <summary>One mark on the strip, clamped to the ends when it is behind you.</summary>
        private static void Pip(float x, float top, float w, float mid, float off,
                                float wide, float tall, Color ink)
        {
            var at = mid + off / CompassHalfFov * (w * 0.5f);

            // BEHIND YOU IS STILL A DIRECTION. A pip that vanished past ninety degrees would
            // leave the strip empty exactly when you have lost him, so it holds at the end it
            // went off and dims -- which reads as "keep turning this way".
            var edge = Math.Abs(off) > CompassHalfFov;

            if (edge)
            {
                at = off > 0f ? x + w - wide : x;
                ink = Color.FromArgb(ink.A / 3, ink.R, ink.G, ink.B);
            }
            else
            {
                // And the last stretch before the end fades rather than stopping dead.
                var dim = Edge(off);
                if (dim < 1f) ink = Color.FromArgb((int)(ink.A * (0.25f + 0.75f * dim)), ink.R, ink.G, ink.B);

                at -= wide * 0.5f;

                if (at < x) at = x;
                if (at + wide > x + w) at = x + w - wide;
            }

            Hud.RectFrom(at, top, wide, tall, ink);
        }

        /// <summary>
        /// How far round you would have to turn to face it: negative left, positive right.
        /// </summary>
        private static float Off(float look, Vector3 from, Vector3 to)
        {
            return Wrap(look - Heading(to - from));
        }

        /// <summary>A direction on the ground as an angle, counter-clockwise from east.</summary>
        private static float Heading(Vector3 run)
        {
            return (float)(Math.Atan2(run.Y, run.X) * 180.0 / Math.PI);
        }

        /// <summary>An angle brought back inside half a turn either way.</summary>
        private static float Wrap(float deg)
        {
            while (deg > 180f) deg -= 360f;
            while (deg < -180f) deg += 360f;

            return deg;
        }

        private static bool Crouching(Ped player)
        {
            try { return Function.Call<bool>(Hash.GET_PED_STEALTH_MOVEMENT, player.Handle); }
            catch { return false; }
        }

        // ======================================================================
        // The end
        // ======================================================================

        public void Clear()
        {
            HandItBack();
            UnmarkField();

            foreach (var q in _out)
            {
                try
                {
                    Unring(q);

                    if (q.Man != null && q.Man.Exists())
                    {
                        Function.Call(Hash.SET_PED_KEEP_TASK, q.Man.Handle, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, q.Man.Handle, false);
                        q.Man.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    // The game takes them back.
                }
            }

            _out.Clear();


            try
            {
                if (_lamar != null && _lamar.Exists())
                {
                    Stealth(false);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _lamar.Handle, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _lamar.Handle, false);
                }
            }
            catch
            {
                // He was borrowed.
            }

            // And handed back to his corner. See GiveBack.
            if (_lamar != null && GiveBack != null)
            {
                try { GiveBack(); }
                catch { /* the fixer takes him back on its own clock */ }
            }

            // A JOB THAT ENDED MID-TAKEDOWN STILL GIVES HIM HIS LEGS BACK. Clear is called
            // on a failure and on a hand-in as well as on the ordinary end, and a player left
            // with ragdolling switched off is a bug that outlives the mission by the rest of
            // the session.
            if (_sticking != null)
            {
                try
                {
                    var me = Game.Player.Character;
                    if (me != null && me.Exists()) Let(me);
                }
                catch
                {
                    _sticking = null;
                }
            }

            // AND THE CLIPS GO BACK. Blade asks for the dictionary on every pass of the hunt
            // so it is resident the frame it is wanted; nothing asks for it once the job is
            // over, and a dictionary nobody has released stays in memory for the session.
            try { Function.Call(Hash.REMOVE_ANIM_DICT, BladeDict); }
            catch { /* the streamer lets go on its own eventually */ }

            _within = null;
            _clean = false;
            _armed = false;

            _lamar = null;
            _lamarDoing = Walk.None;
            _lamarStealth = false;
            _def = null;
            _shotAt = 0;
            _layFrom = 0;
            _fieldAt = -2;

            Phase = HuntPhase.None;
            ReadyToCollect = false;
            Failure = null;
        }
    }
}
