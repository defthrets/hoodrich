using System;
using System.Collections.Generic;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.UI;

namespace Hoodrich.Missions
{
    /// <summary>Where the bike job has got to.</summary>
    internal enum BikePhase
    {
        None,

        /// <summary>A bike is waiting outside. Get on it.</summary>
        ToBike,

        /// <summary>Riding out to the courts with the homies.</summary>
        Riding,

        /// <summary>At the courts. They are stood there waiting to be spoken to.</summary>
        Words,

        /// <summary>Hands.</summary>
        Fight,

        /// <summary>Thirsty. The shop is down the road.</summary>
        /// <summary>Outside the 24/7, deciding whether to do it.</summary>
        Rob,

        /// <summary>Done it. Lose the cops.</summary>
        Escape,

        /// <summary>Drink got. Ride back to the spot.</summary>
        Home
    }

    /// <summary>
    /// The push-bike job.
    ///
    /// Scripted end to end rather than assembled from a site and a target count, because the
    /// shape of it is the point: four of you ride out on bikes, four of them are already stood
    /// on the court, somebody says something, and it goes off. No guns on either side -- pulling
    /// one is what ends it, not what wins it. Then you are thirsty, so you ride to the shop,
    /// and then you ride home. It is a straightener and an afternoon, not a hit.
    ///
    /// Every leg is ridden. Nothing here teleports and nothing fast-forwards, which is what
    /// makes it read as a day out rather than a checklist.
    /// </summary>
    internal sealed class BikeRide
    {
        // ---- the route ---------------------------------------------------------

        /// <summary>Where the bike is left for you, round the corner from Lamar.</summary>
        private static readonly Vector3 BikeSpot = new Vector3(-210.409f, -1720.489f, 32.664f);
        private const float BikeHeading = 102.085f;

        /// <summary>The courts in Chamberlain Hills.</summary>
        private static readonly Vector3 Courts = new Vector3(-227.173f, -1541.756f, 31.607f);

        /// <summary>The 24/7 down the road.</summary>
        private static readonly Vector3 Shop = new Vector3(29.028f, -1352.893f, 29.341f);

        /// <summary>
        /// Where Lamar's bike is, and which way it is pointing.
        ///
        /// A named spot rather than two metres from wherever he happens to be stood. He is
        /// borrowed off his corner and put straight onto it, the same way the homies are put
        /// straight onto theirs -- and it lands up the far end of the lot, so three men and
        /// three bikes are not materialising in the space you are stood in.
        /// </summary>
        /// Three and a bit metres to the right of yours and seven for the homies, worked out
        /// off the one mark that was given rather than read separately: three bikes at the same
        /// coordinate is one bike and two the game has shoved somewhere.
        /// <summary>
        /// Where his bike is stood, which is on the lot beside yours rather than off round the
        /// corner. He walks to it -- see MountLamar -- so the player watches him do it.
        /// </summary>
        private static readonly Vector3 LamarBike = new Vector3(-209.918f, -1729.695f, 32.614f);
        private const float LamarBikeHeading = 43.674f;

        /// <summary>
        /// Where the other two are waiting, up the alley from Lamar's bike.
        ///
        /// Four metres from his, so the three of them read as one crew setting off rather than
        /// as people who happened to be in the same postcode.
        /// </summary>
        private static readonly Vector3 HomieSpot = new Vector3(-219.017f, -1717.120f, 32.629f);

        /// <summary>
        /// Where the ride ends.
        ///
        /// The far corner of the lot rather than Fixer.Spot. He stands where he stands for
        /// every other job; this one is handed back on its own mark, by the skip, a few strides
        /// off him.
        /// </summary>
        private static readonly Vector3 RideHome = new Vector3(-205.515f, -1719.239f, 32.664f);

        /// <summary>
        /// How he drives while following you: everything except the parts that make him wait.
        ///
        /// 786603 with StopBeforeVehicles, StopBeforePeds and StopAtTrafficLights cleared and
        /// AllowGoingWrongWay set. AvoidObjects and AvoidEmptyVehicles stay, so he goes round
        /// a parked car rather than into it.
        /// </summary>
        private const int FollowStyle = 786984;
        private const float HomieHeading = 320.830f;

        // ---- ranges and timings ------------------------------------------------

        private const float ArriveRange = 45f;
        private const float TalkRange = 6f;
        private const float ShopRange = 30f;
        /// <summary>
        /// How close to the spot counts as home, for you and for him.
        ///
        /// Both were tighter and both were the wrong kind of tight. Yours was 25 and his was
        /// 14, and HIS is the one that decides -- so you would pull up on the mark, stop, and
        /// wait while a man on a bicycle picked his way round the last fence. A finishing line
        /// you have to help somebody find is not a finishing line.
        /// </summary>
        private const float HomeRange = 38f;

        /// <summary>Further behind than this and they are put back on your wheel.</summary>
        private const float CatchUpRange = 90f;

        /// <summary>They are put on the court before you can see it, so nobody drops in.</summary>
        private const float PreSpawnRange = 170f;
        private const float CourtSpread = 5f;

        private const int UpdateIntervalMs = 400;

        /// <summary>Chatter on the ride out, so four men on bikes are not four silent men on bikes.</summary>
        /// <summary>
        /// How often somebody says something.
        ///
        /// Down from fourteen seconds, and it now runs for the WHOLE job rather than only the
        /// ride out. Chatter was wired into the riding tick alone, so the walk to the bike, the
        /// business at the courts, the shop and the ride home were all silent -- which is most
        /// of the mission, and it made the one leg that did talk feel like a different scene.
        /// </summary>
        /// <summary>The floor between lines, with ChatterSpreadMs of slop on top.</summary>
        private const int ChatterGapMs = 11000;
        private const int ChatterSpreadMs = 9000;

        /// <summary>How often the escort task is put back on anybody who has lost it.</summary>
        private const int RetaskGapMs = 2500;

        /// <summary>
        /// What they shout on the way out.
        ///
        /// Wound up rather than rude. They are riding out with him, not at him, so the insults
        /// come off -- whatever is being shouted is aimed at the people on the court, and the
        /// ones aimed at nobody in particular should sound like men enjoying themselves.
        /// </summary>
        /// <summary>
        /// What he sounds like on each leg.
        ///
        /// IT WAS ONE LIST FOR THE WHOLE JOB, and it had CHASE_SUSPECT and GENERIC_WAR_CRY in
        /// it -- so the ride out to a basketball court, which is two men going somewhere on
        /// bicycles, had Lamar occasionally screaming a battle cry at nobody. The same five
        /// lines played coming back from an armed robbery.
        ///
        /// Every name here is one Rockstar's own scripts use, taken out of the decompiled set
        /// rather than guessed -- a speech name a voice has not got plays as silence, which is
        /// indistinguishable from the feature not working.
        ///
        /// No subtitles anywhere in here on purpose. These are not lines with words worth
        /// reading; they are the noise of somebody being there.
        /// </summary>
        private static readonly string[] RideLines =
        {
            "CHAT_STATE", "CHAT_RESP", "GENERIC_HOWS_IT_GOING", "GENERIC_YES"
        };

        /// <summary>Squaring up at the courts.</summary>
        private static readonly string[] EdgyLines =
        {
            "CHALLENGE_THREATEN", "GENERIC_INSULT_HIGH", "PROVOKE_STARING", "GENERIC_CURSE_HIGH"
        };

        /// <summary>Rolling up on the shop with a plan he has just told you about.</summary>
        private static readonly string[] StoreLines =
        {
            "GUN_COOL", "CHAT_STATE", "GENERIC_YES", "CHAT_RESP"
        };

        /// <summary>Leaving it, at speed.</summary>
        private static readonly string[] AwayLines =
        {
            "GENERIC_CURSE_HIGH", "GENERIC_WAR_CRY", "CHAT_RESP", "GENERIC_INSULT_HIGH"
        };

        /// <summary>The ride home, when it is done and funny.</summary>
        private static readonly string[] HomeLines =
        {
            "GENERIC_CHEER", "CHAT_STATE", "GENERIC_AGREE", "CHAT_RESP"
        };

        /// <summary>Whichever set belongs to where the job has got to.</summary>
        private string[] LinesForNow()
        {
            switch (Phase)
            {
                case BikePhase.Words:
                case BikePhase.Fight:
                    return EdgyLines;

                case BikePhase.Rob:
                    return StoreLines;

                case BikePhase.Escape:
                    return AwayLines;

                case BikePhase.Home:
                    return HomeLines;

                default:
                    return RideLines;
            }
        }

        /// <summary>
        /// A BMX. This was a ladder of four with the BMX at the top, so on a good day it
        /// was already a BMX and on a bad one it was a road bike -- and a road bike under
        /// one of the set is the wrong picture on any day. The ladder code stays for the
        /// model that will not load; the list is one long.
        /// </summary>
        private static readonly string[] BikeModels = { "bmx" };

        private const int PedTypeCiv = 4;

        // ---- state -------------------------------------------------------------

        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly Random _rng = new Random();

        private readonly List<Ped> _homies = new List<Ped>();
        private readonly List<Vehicle> _bikes = new List<Vehicle>();
        private readonly List<Ped> _rivals = new List<Ped>();
        private readonly List<Blip> _blips = new List<Blip>();

        private MissionDef _def;
        private Vehicle _playerBike;
        private Blip _marker;

        /// <summary>
        /// The man who gave you the job, riding it with you.
        ///
        /// Borrowed rather than spawned, so this is the SAME Lamar you were just talking to
        /// rather than a second one who looks like him. Held separately as well as in _homies
        /// because he must not be released at the end the way the others are -- he is not ours
        /// to let go of.
        /// </summary>
        private Ped _lamar;

        /// <summary>
        /// When he stopped being usable, or 0 while he is fine. See the check in Update.
        /// </summary>
        private int _lamarGoneSince;

        /// <summary>How long he can be missing before the job gives up on him.</summary>
        private const int LamarGoneMs = 6000;

        /// <summary>Set by MissionRunner. Who to borrow him from, and give him back to.</summary>
        public Fixer Boss;

        private int _lastUpdate;
        private int _nextChatter;
        private int _nextRetask;
        private bool _wentInside;
        /// <summary>
        /// How far through the exchange at the courts we are, and when the next line is due.
        ///
        /// It runs itself. It used to be a prompt and a dialogue panel -- ride across two
        /// neighbourhoods, then stand there and press a button to be shown a menu with one
        /// option on it. Four men walk up to you on a court; you do not get asked whether you
        /// would like the scene to begin.
        /// </summary>
        private int _wordsStep;
        private int _wordsAt;

        /// <summary>When Lamar laughs, and when he gets round to why he wants to stop.</summary>
        private int _laughAt;
        private int _thirstyAt;

        public BikeRide(Affiliation crew, GangRegistry gangs)
        {
            _crew = crew;
            _gangs = gangs;
        }

        /// <summary>Set by Main: the dialogue screen the courtyard exchange opens in.</summary>
        public Conversation Talk;

        /// <summary>
        /// Set by the runner. The block posts about the brawl WHILE it is happening.
        ///
        /// A fight that gets written up after the fact is a result. A fight that people are
        /// posting about while you are still swinging is an event, and it is the same content
        /// either way -- the only difference is when it arrives.
        /// </summary>
        public Hoodrich.Social.SocialFeed Social;

        private int _nextFightPost;

        public BikePhase Phase { get; private set; } = BikePhase.None;

        public bool IsRunning => Phase != BikePhase.None;

        public bool ReadyToCollect { get; private set; }

        /// <summary>
        /// How far through the ride you are, 0 to 1, for the bar on the objective card.
        ///
        /// Reported from HERE rather than worked out by the runner, because everything it
        /// depends on lives here -- where the bike is, how many of them are still up, whether
        /// the till is open. The runner has the card; this has the facts.
        /// </summary>
        public float Advance
        {
            get
            {
                try
                {
                    var player = Game.Player.Character;
                    if (player == null || !player.Exists()) return 0f;

                    switch (Phase)
                    {
                        case BikePhase.ToBike:
                            return Bar(player.Position.DistanceTo(BikeSpot), 40f);

                        case BikePhase.Riding:
                            return Bar(player.Position.DistanceTo(Courts), 900f);

                        case BikePhase.Words:
                            return _wordsStep / 3f;

                        case BikePhase.Fight:
                        {
                            var up = 0;
                            foreach (var ped in _rivals)
                            {
                                if (ped != null && ped.Exists() && ped.IsAlive) up++;
                            }

                            var all = Math.Max(1, _rivals.Count);
                            return (all - up) / (float)all;
                        }

                        case BikePhase.Rob:
                            return _gotCash ? 1f : _robAccepted ? 0.6f : 0.3f;

                        case BikePhase.Escape:
                            return 1f - Game.Player.Wanted.WantedLevel / 5f;

                        case BikePhase.Home:
                            return Bar(player.Position.DistanceTo(RideHome), 700f);
                    }
                }
                catch
                {
                    // An empty bar is better than a thrown one.
                }

                return 0f;
            }
        }

        /// <summary>Distance turned into a bar that fills as you close on something.</summary>
        private static float Bar(float away, float from)
        {
            if (from <= 0f) return 0f;

            var f = 1f - away / from;
            return f < 0f ? 0f : f > 1f ? 1f : f;
        }

        /// <summary>Set when the player pulls a gun. Read and cleared by the runner.</summary>
        public string Failure { get; private set; }

        public string Objective
        {
            get
            {
                switch (Phase)
                {
                    case BikePhase.ToBike: return "Get on the bike";
                    case BikePhase.Riding: return "Ride to the courts in Chamberlain Hills";
                    case BikePhase.Words: return "Go say something to them";
                    case BikePhase.Fight: return "Hands only -- pull a gun and it's over";
                    case BikePhase.Rob:
                        // Three different sentences, because there are three different things
                        // in front of you. It used to say "aim at the clerk until he empties
                        // the till" for the whole leg -- which stopped being true the moment
                        // the robbery became the game's, and was actively wrong stood over a
                        // body with the register still shut.
                        if (!_robAccepted) return "Pull up outside the 24/7";
                        if (_gotCash) return "Get out and get on the bike";

                        return _clerkDown ? "Clean out the register" : "Rob the store";

                    case BikePhase.Escape: return "Lose the cops";
                    case BikePhase.Home: return "Ride back to the spot";
                    default: return "";
                }
            }
        }

        // ---- starting ----------------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once the bike is out there.</summary>
        public string Start(MissionDef def)
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            _def = def;
            _wentInside = false;
            _clerkDown = false;

            // A new ride has not killed anybody yet. Without this the flag survives from the
            // last attempt, and Lamar debriefs a body that is not there this time.
            KilledTheClerk = false;

            _shouted = false;
            _robOffered = false;
            _robAccepted = false;
            _gotCash = false;
            _lamarSlipSince = 0;
            _lamarWasAt = 0f;
            _lamarGoneSince = 0;
            _wordsStep = 0;
            _wordsAt = 0;
            _laughAt = 0;
            _thirstyAt = 0;
            ReadyToCollect = false;
            Failure = null;

            _playerBike = SpawnBike(BikeSpot, BikeHeading);
            if (_playerBike == null) return "Ain't no bike out there.";

            Phase = BikePhase.ToBike;

            // BEFORE anything spawns, and that is the fix for our own people wading in.
            //
            // The sides used to be made when Lamar was brought, which is after the rivals are
            // placed on some runs -- and a rival placed before the group exists falls back to
            // AMBIENT_GANG_BALLAS, which the Families on that block genuinely do hate. Half the
            // neighbourhood then arrived at a fight that is supposed to be four men.
            MakeSides();

            HoldTheLaw(true);
            Mark(BikeSpot, "Your bike", BlipColor.Yellow);

            // ON FOOT, from here. He used to appear on a bicycle at the moment you got on
            // yours, which made the walk to the bike a walk on your own -- the man who just
            // asked you to come with him standing where you left him until the scene needed
            // him. He comes off his corner when the job starts and follows you across the lot.
            LendLamar(player);

            Notify.Important("~g~Job on.~s~ " + Objective + ".");
            Log.Info("BikeRide started; bike at " + BikeSpot + ".");
            return null;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (!IsRunning) return;

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            // Re-asserted, the same as the raid does. It was taken once when the job started
            // and the game hands the police back on its own -- a cutscene, an area reload, a
            // mission ending. The straightener on the courts is not a police matter and should
            // not become one halfway through.
            //
            // UNLESS THE TILL HAS BEEN EMPTIED, and this is why there was never a star after
            // the shop. Releasing the hold at the robbery was correct and this line quietly
            // undid it a quarter of a second later: Hold does not merely cap the wanted level,
            // it CLEARS it, so the star was set and then wiped on the very next tick, every
            // time. The re-assert has to stop when the reason for it stops.
            // Held off entirely until the shop, and capped at one star afterwards.
            //
            // Setting the star was never the hard part. Keeping it AT one is: from the game's
            // point of view an armed robbery with a man riding away from it is worth three or
            // four, and it will keep adding them while anybody can see you. One star is what
            // this job is meant to be worth, so one star is the ceiling until it is over.
            if (!_lawLoose) HoldTheLaw(true);
            else LawHold.Cap(RobberyStars);

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                Failure = "You went down out there.";
                return;
            }

            // He is protected from the moment he is lent out, so this should not fire. It
            // exists because the failure it catches is unrecoverable: every phase tick guards
            // on him and returns, so losing him does not fail the job, it freezes it -- with
            // the objective still on screen and nothing left that can clear it. Ending gives
            // the job back, which beats a save that has to be reloaded.
            //
            // Given a few seconds first: he is legitimately absent for a moment while being
            // adopted, and between a bike being spawned and him being put on it.
            if (Phase > BikePhase.ToBike)
            {
                if (_lamar == null || !_lamar.Exists() || !_lamar.IsAlive)
                {
                    if (_lamarGoneSince == 0) _lamarGoneSince = Game.GameTime;

                    if (Game.GameTime - _lamarGoneSince > LamarGoneMs)
                    {
                        Log.Warn("Lamar is gone mid-ride; ending rather than hanging.");
                        Failure = "Lost Lamar out there.";
                        return;
                    }
                }
                else
                {
                    _lamarGoneSince = 0;
                }
            }

            // Every phase, not three of them.
            //
            // This was called from the robbery, the escape and the ride home -- which are
            // exactly the parts where he was already beside you, and not the two-neighbourhood
            // ride where he actually gets lost. Up here it covers the whole job.
            KeepLamarClose(player);

            // Hands only, all the way through, not just during the fight. Pulling a gun on a
            // straightener is the whole thing you were told not to do, and it should not stop
            // being true the moment the last one goes down.
            if (Phase >= BikePhase.Words && Phase <= BikePhase.Fight && PlayerFired(player))
            {
                Failure = "You pulled a gun. That wasn't the job.";
                return;
            }

            // Every leg, not just the ride. See ChatterGapMs.
            Chatter();

            switch (Phase)
            {
                case BikePhase.ToBike: TickToBike(player); return;
                case BikePhase.Riding: TickRiding(player); return;
                case BikePhase.Words: TickWords(player); return;
                case BikePhase.Fight: TickFight(); return;
                case BikePhase.Rob: TickRob(player); return;
                case BikePhase.Escape: TickEscape(player); return;
                case BikePhase.Home: TickHome(player); return;
            }
        }

        private void TickToBike(Ped player)
        {
            if (_playerBike == null || !_playerBike.Exists())
            {
                Failure = "Somebody took the bike.";
                return;
            }

            if (!player.IsInVehicle(_playerBike)) return;

            // They turn up when you get on, not when you take the job. Three men standing about
            // in a courtyard while you decide whether to bother is not the same picture.
            SpawnHomiesOnBikes(player);
            MountLamar(player);

            Phase = BikePhase.Riding;
            _nextChatter = Game.GameTime + ChatterGapMs;

            Mark(Courts, "The courts", BlipColor.Yellow);
            Notify.Ticker(_lamar != null && _lamar.Exists()
                ? "~g~Lamar rolled out with you.~s~"
                : "~g~The homies rolled out with you.~s~");
        }

        private void TickRiding(Ped player)
        {
            KeepUp(player);
            SicThemOn(player);

            if (player.Position.DistanceTo(Courts) <= PreSpawnRange && _rivals.Count == 0)
            {
                SpawnRivals();
            }

            if (player.Position.DistanceTo(Courts) > ArriveRange) return;

            if (_rivals.Count == 0) SpawnRivals();

            if (_rivals.Count == 0)
            {
                Failure = "Wasn't nobody at the courts.";
                return;
            }

            Phase = BikePhase.Words;
            CallThemUp();
            GetDown(player);
            ClearMarker();

            Notify.Important("~r~They're already here.~s~ " + Objective + ".");
        }

        /// <summary>
        /// What gets said before it goes off, on its own, in subtitles.
        ///
        /// Nothing to press. There was a prompt and a one-option dialogue panel here, which is
        /// the wrong shape for this beat twice over: you had already ridden across two
        /// neighbourhoods to have this conversation, so being asked whether you would like to
        /// have it is a question with one answer -- and four men walking up to you on a court
        /// is not something you opt into. Get near enough for it to be them talking to you
        /// rather than a caption from across the court, and it plays.
        ///
        /// Timed off the clock rather than waited on, because everything in this file runs
        /// inside one tick and a wait would stop the whole mod mid-sentence.
        /// </summary>
        private void TickWords(Ped player)
        {
            if (Talk != null && Talk.IsOpen) return;

            // Whichever comes first: the last line, or you not waiting for it.
            //
            // The exchange is written to be walked into and it plays on a clock, so a player
            // who has already decided how this goes and swings at somebody was, until now,
            // punching a man who stood there taking it while the subtitles finished. Now the
            // punch IS the answer -- everybody piles in on the frame you throw it.
            if (ThrownHands(player))
            {
                ItGoesOff();
                return;
            }

            var nearest = Nearest(_rivals, player);
            if (nearest == null)
            {
                Phase = BikePhase.Fight;
                return;
            }

            if (_wordsStep == 0)
            {
                if (Flat(player.Position, nearest.Position) > TalkRange) return;

                RivalSays("You been talking a lot of smack online. We here right now, boy. " +
                          "Whatchu wanna do?", 4200);

                // Out loud as well as on screen. A subtitle with nothing behind it is a caption
                // on a silent film -- you can see two men arguing and hear a basketball court.
                Mouth(nearest, Angry[_rng.Next(Angry.Length)]);

                _wordsAt = Game.GameTime + 3600;
                _wordsStep = 1;
                return;
            }

            if (Game.GameTime < _wordsAt) return;

            if (_wordsStep == 1)
            {
                Says("~g~FRANKLIN", "Say that again.", 1800);

                // And Franklin answers in his own voice rather than only in text.
                Mouth(player, Angry[_rng.Next(Angry.Length)]);

                _wordsAt = Game.GameTime + 1400;
                _wordsStep = 2;
                return;
            }

            ItGoesOff();
        }

        /// <summary>Whoever you came to see, in their own colour, saying it out loud.</summary>
        private void RivalSays(string line, int ms)
        {
            var gang = _gangs.Get(_def.TargetGang);
            var who = gang == null ? "Ballas" : gang.Name;

            Says("~p~" + who.ToUpperInvariant(), line, ms);
        }

        /// <summary>A named line on screen. The colour is the tag, not the whole sentence.</summary>
        /// <summary>
        /// The subtitle, said out loud, if somebody recorded it.
        ///
        /// CUE RATHER THAN SAY, and the difference is the point. Say honours the once-only
        /// rule, which is right for a conversation -- hearing a man's explanation of the
        /// business a fourth time is a speech shouted at somebody re-reading a menu. These
        /// are not that. They are barks on a job you can run again, and a shout that only
        /// ever happens on your first ride is a bug rather than restraint.
        ///
        /// Same key, worked out the same way, handed to Cue -- which skips the heard list.
        /// </summary>
        private static void Aloud(string who, string line)
        {
            try
            {
                Core.Voice.Cue(Core.Voice.Key(who, line));
            }
            catch
            {
                // The subtitle already said it.
            }
        }

        /// <summary>"~y~LAMAR" -> "LAMAR". Colour tags are layout, not who is speaking.</summary>
        private static string Plain(string tagged)
        {
            if (string.IsNullOrEmpty(tagged)) return "";

            var sb = new System.Text.StringBuilder(tagged.Length);
            var skip = false;

            foreach (var c in tagged)
            {
                if (c == '~') { skip = !skip; continue; }
                if (!skip) sb.Append(c);
            }

            return sb.ToString().Trim();
        }

        private void Says(string who, string line, int ms)
        {
            if (string.IsNullOrEmpty(line)) return;

            try { GTA.UI.Screen.ShowSubtitle(who + ":~s~ " + line, ms); }
            catch { /* the objective still says what to do */ }

            Aloud(Plain(who), line);
        }

        private void TickFight()
        {
            // Somebody posts about it every few seconds while it is still going on.
            if (Social != null && Game.GameTime >= _nextFightPost)
            {
                _nextFightPost = Game.GameTime + 4500 + _rng.Next(4000);
                Social.On(Hoodrich.Social.SocialEvent.Brawl);
            }

            var standing = 0;

            foreach (var ped in _rivals)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) standing++;
            }

            if (standing > 0) return;

            Phase = BikePhase.Rob;

            // Straight away, rather than waiting for TickRob's first pass -- the beat right
            // after a fight is the one where four men standing still reads worst.
            RemountHomies(Game.Player.Character);

            _wentInside = false;
            _clerkDown = false;
            _shouted = false;
            _robOffered = false;
            _robAccepted = false;
            _gotCash = false;
            _lawLoose = false;
            _doorAt = 0;

            Cheer();

            Mark(Shop, "24/7", BlipColor.Yellow);
            Notify.Important("~g~That's that.~s~ Lamar wants to stop at the 24/7. " + Objective + ".");
        }

        /// <summary>
        /// Outside the 24/7, and then inside it.
        ///
        /// Told in the order it happens: he shouts before you have stopped, the offer comes
        /// when you have, and the shop itself only starts once you have said yes. Nothing here
        /// forces you -- riding off without taking it is a way to finish the job, it is just
        /// a worse-paid one.
        /// </summary>
        private void TickRob(Ped player)
        {
            // AND AFTER THE FIGHT TOO.
            //
            // KeepUp was called from TickRiding and nowhere else, so the moment the hands went
            // up it stopped being called for the rest of the job -- Rob, Escape and the ride
            // home. Lamar would get back on his bike and then sit on it, because nothing was
            // telling him to go anywhere, and the ride home was a ride home on your own.
            //
            // It is safe in any phase: it re-tasks anybody off his bike and follows with
            // anybody on one, and it rate-limits itself.
            KeepUp(player);

            // Keep trying the door for as long as the robbery is on.
            //
            // Once rather than repeatedly would be the obvious way and would never work: the
            // phase starts at the courts, half a mile from the shop, and a door that has not
            // streamed in yet cannot be told anything. So it is asked again every second and a
            // half until the till is empty, which costs a props-within-nine-metres sweep and
            // catches the door the moment the block loads in.
            if (Game.GameTime >= _doorAt)
            {
                _doorAt = Game.GameTime + DoorCheckMs;
                OpenTheShop();

                // On the same timer and for the same reason: the shop is half a mile away when
                // this phase starts, so neither the door nor the counter has streamed in yet
                // and both have to be asked again until they have.
                StandUpClerk();
            }

            // The two lines that follow the fight, spaced so they do not stack. The laugh is
            // audible and the thirst is written, which is on purpose: one of them is a noise a
            // man makes and the other is a sentence he needs you to hear.
            if (_laughAt != 0 && Game.GameTime >= _laughAt)
            {
                _laughAt = 0;
                Mouth(_lamar, "LAUGH");
            }

            if (_thirstyAt != 0 && Game.GameTime >= _thirstyAt)
            {
                _thirstyAt = 0;

                // Which he will deny at the shutter in about a minute. He is not thirsty and
                // he was never thirsty -- he wants you parked outside that shop, and the drink
                // is the reason he came up with on the way. Saying it out loud here is what
                // makes "nah, I ain't thirsty, just pull in" land as him dropping the act.
                LamarSays("Ay, hold up, I'm thirsty as hell, dawg. Slide on that 24/7, " +
                          "let's get us somethin' to drink.");
            }

            RemountHomies(player);

            var range = player.Position.DistanceTo(Shop);

            // He calls it before you get there. Far enough out that you can still choose to
            // pull in rather than being told after you already have.
            if (!_shouted && range <= ShoutRange)
            {
                _shouted = true;

                LamarSays("AYO! FRANKLIN HOL UP!");
                _weaselAt = Game.GameTime + 4600;
            }

            // THE ONLY WAY PAST THIS LINE IS _robAccepted, so nothing may set _gotCash
            // without it. A branch that skipped ahead used to exist and it stranded the job
            // here permanently. See TheOffer.
            if (!_robAccepted)
            {
                // The second line lands after the shout has cleared, so they do not stack.
                if (_weaselAt != 0 && Game.GameTime >= _weaselAt)
                {
                    _weaselAt = 0;
                    LamarSays("Pull in right here. Nah, I ain't thirsty. Just pull in.");
                }

                // He does not open a menu at you. He tells you to pull in, and then you go
                // and ask him what for -- which is the same thing every other conversation in
                // the mod asks of you, and it stops a screen appearing over the handlebars
                // while you are still rolling.
                if (_shouted && range <= ShopRange) OfferAtTheSpot(player);

                return;
            }

            LamarStaysOut();

            if (!_gotCash)
            {
                Robbery(player);
                return;
            }

            // Cash in hand. Out, on the bike, and gone.
            if (Inside(player)) return;

            Phase = BikePhase.Escape;

            Mark(RideHome, "The spot", BlipColor.Yellow);
            Notify.Important("~r~That's the till.~s~ " + Objective + ".");
        }

        /// <summary>
        /// Walk up to him and he will tell you what he stopped for.
        ///
        /// The same shape as the words at the courts: get near, a prompt appears, you press,
        /// and the screen opens. He is the one who has something to say, so he is the one you
        /// go to -- and it means a menu never lands on top of you unasked.
        /// </summary>
        private void OfferAtTheSpot(Ped player)
        {
            if (Talk == null || Talk.IsOpen || _robOffered) return;

            // Walk onto the mark and he says it. No prompt.
            //
            // It used to be "get near Lamar, press the button" -- the same shape as every other
            // conversation in the mod, which is right for a man stood on a corner you might
            // walk past and wrong for this. He has already shouted at you to pull in; making
            // you then find him and ask is asking you to start a conversation he started.
            //
            // A place rather than a person, because the place is the point: round the back, out
            // of the doorway, where two men talking about a shop are not stood in front of it.
            if (player.Position.DistanceTo(RobSpot) > RobSpotRange) return;

            _robOffered = true;

            // Who is talking, which this never said.
            //
            // The panel speaks through Talk.Speaker, and the mission opened the conversation
            // without setting it -- so every exchange on this job was silent on his side while
            // the corner version of the same man, three streets away, talked. Same class, same
            // panel, one missing assignment.
            Talk.Speaker = _lamar;

            Talk.Open(TheOffer(), this);
        }

        /// <summary>
        /// Round the back of the 24/7, where he says what he actually stopped for.
        ///
        /// Read off the HUD stood on it. Far enough round the corner that the conversation is
        /// not happening in the shop doorway, close enough that walking there is one decision
        /// rather than a second objective.
        /// </summary>
        private static readonly Vector3 RobSpot = new Vector3(12.583f, -1352.833f, 29.330f);

        private const float RobSpotRange = 3.2f;

        /// <summary>
        /// The ring on the ground at that mark.
        ///
        /// Drawn rather than blipped: a blip says "somewhere over there" and this is a spot you
        /// have to actually stand on, which is a thing the world has to show you rather than the
        /// map. It stops the moment he has said his piece.
        /// </summary>
        public void Draw()
        {
            if (!IsRunning || Phase != BikePhase.Rob || _robOffered) return;
            if (!_shouted) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;
            if (player.Position.DistanceTo(RobSpot) > 60f) return;

            try
            {
                World.DrawMarker(MarkerType.Cylinder,
                                 RobSpot - new Vector3(0f, 0f, 0.95f),
                                 Vector3.Zero, Vector3.Zero,
                                 new Vector3(1.4f, 1.4f, 0.8f),
                                 System.Drawing.Color.FromArgb(120, 232, 177, 44));
            }
            catch
            {
                // No marker this frame; the shout still told you to pull in.
            }
        }

        /// <summary>
        /// The piece Lamar hands over for the store.
        ///
        /// A combat pistol with the long magazine, which is what he says it is -- the line and
        /// the gun have to agree or the line is a lie the player catches in about four seconds.
        ///
        /// Through the locker, so it survives a reload like anything else bought in this mod.
        /// He says he wants it back and never asks, which is the most Lamar thing about it.
        /// </summary>
        private void GiveTheGun()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var hash = Function.Call<uint>(Hash.GET_HASH_KEY, RobberyGun);

                // Not if he already has one -- topping somebody up to a full clip for free is a
                // different favour to lending him a gun he did not have.
                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false))
                {
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, me.Handle, hash, RobberyRounds, false, true);
                }

                Weapons.ExtendedClips.GiveTo(me, RobberyGun);

                if (Locker != null) Locker.Bought(RobberyGun);

                Notify.Ticker("~g~Lamar gave you a combat pistol.~s~  Long mag.");
                Log.Info("Lamar handed over " + RobberyGun + " for the store.");
            }
            catch (Exception ex)
            {
                // He goes in empty handed, which is survivable. Being unable to rob the shop is
                // not worth throwing the whole job over.
                Log.Debug("Could not hand over the robbery gun: " + ex.Message);
            }
        }

        /// <summary>What he lends you, and how loaded.</summary>
        private const string RobberyGun = "WEAPON_COMBATPISTOL";
        private const int RobberyRounds = 60;

        /// <summary>Set by Main, so the gun he lends you is one you keep.</summary>
        public Weapons.GunLocker Locker;

        /// <summary>
        /// The offer, as a screen rather than a prompt.
        ///
        /// A robbery is not something to walk into by accident, so it is asked properly and it
        /// explains itself: what to do once you are in there is not obvious, and finding out by
        /// standing in a shop wondering why nothing is happening is not a puzzle worth having.
        /// </summary>
        private DialogueNode TheOffer()
        {
            // He did not mention this on the ride out, and that is the point.
            //
            // The brief was a basketball court and hands only. This is Lamar arriving at the
            // shop with a second plan he has been sitting on, presenting it as an idea he is
            // having right now, and hanging it off something Franklin actually wants -- the
            // grow room needs paying for, and Lamar knows it does.
            var node = new DialogueNode("Lamar",
                "Aight so. Hear me out. That grow I been settin' up? Lights, fans, all that " +
                "-- that's real money, dawg, and I ain't got it. One man in there, one till, " +
                "and them cameras been dead since we was in school. That's my seed money " +
                "sittin' on a counter and you the one with the steady hands.\n\n" +
                "Here -- take this. Long mag, one in the chamber, and I want it back. You " +
                "ain't gotta USE it, cuz, you just gotta be holdin' it. Man behind that " +
                "counter got a whole life to get back to, he ain't dyin' over forty dollars " +
                "and a scratchie.")
            {
                SpeakerColour = Palette.Cash
            };

            node.Say("ROB THE CONVENIENCE STORE", () =>
            {
                _robAccepted = true;

                // AND HE ACTUALLY HANDS IT OVER.
                //
                // The offer used to send you into a robbery with whatever you happened to be
                // carrying, which on this job is nothing: the brief was hands only, the ride
                // out disarms nobody but nobody brings a gun to a basketball game, and a store
                // robbery with empty hands is a man asking politely.
                //
                // Given on ACCEPTING rather than when the screen opens, so walking away from
                // the offer does not leave you holding a pistol Lamar never gave you.
                GiveTheGun();

                // The law comes back on the moment you agree to this.
                //
                // The job holds the wanted system off for its whole length, which is right for
                // a fist fight on a basketball court and exactly wrong here: the game's own
                // robbery raises a star and sets the alarm off, and a hold with a ceiling of
                // zero eats both in the frame they are asked for.
                // _lawLoose is read at the top of every tick: while it is false the job pins
                // the ceiling at nothing, and once it is true it caps at RobberyStars instead.
                // Setting it here rather than at the payout is the point -- the alarm goes off
                // while you are still in there, and a ceiling of zero swallowed it.
                _lawLoose = true;
                HoldTheLaw(false);

                _moneyIn = Game.Player.Money;

                Notify.Important("~r~In you go.~s~ Take the till.");

                LamarSays("I'm right here on the bike. Go on.");
                return null;
            }, "Rob it however it goes -- somebody will call it in");

            node.WithIcon(Icons.FromFile("cash.png"));

            // THERE IS NO SECOND ROW, and that is deliberate twice over.
            //
            // It froze the job. The decline set _gotCash and never set _robAccepted, and
            // TickRob's first gate is `if (!_robAccepted) return` -- so the tick turned round
            // at that line forever while _robOffered stopped the offer ever coming back. The
            // objective sat on "Pull up outside the 24/7" with no way to finish and no way to
            // cancel, exactly like the tag run without a bike.
            //
            // And it should not have been a choice anyway. He has already shouted you off the
            // road, admitted he was never thirsty, walked you round the back and told you what
            // the money is for. Turning round at that point is not a decision the ride is
            // built to absorb -- the whole leg exists because he had a second plan the brief
            // did not mention. You are robbing the store.
            return node;
        }
        /// <summary>
        /// The shop floor, WATCHED rather than run.
        ///
        /// This used to be a robbery of our own: find or stand up a clerk, wait for the player
        /// to free-aim at that particular man, task him with his hands up, count a hold timer,
        /// pay out. It kept finding new ways to stop. The last one was the worst -- shoot the
        /// clerk and the job deadlocked, still telling you to aim at a corpse, with the money
        /// sat in a till nothing would ever open.
        ///
        /// The game ships a shop robbery that handles all of it, including that. Aim and he
        /// empties it; shoot him and you take it out of the register yourself; the alarm and
        /// the star are its business. So ours is gone and this only notices the outcome.
        ///
        /// Money is the signal because money is the point, and it is the one thing that is
        /// true however the scene played out. And there is a way out that does not depend on
        /// it: walk back out of the shop and the job carries on regardless, because a player
        /// who changed their mind in there should still get to ride home.
        /// </summary>
        private void Robbery(Ped player)
        {
            var inside = Inside(player);

            if (inside)
            {
                _wentInside = true;

                if (Game.Player.Money - _moneyIn >= TookEnough) { Took(); return; }

                // Shot him. The game leaves the register openable in that case, and standing
                // over a body waiting to be told what to do is the one moment the leg needs a
                // sentence of its own.
                //
                // Latched rather than re-tested for the objective, because the card asks for
                // that string every frame and this is a ped scan.
                if (!_clerkDown && SomebodyDown(player))
                {
                    _clerkDown = true;
                    KilledTheClerk = true;
                    Notify.Ticker("~o~He's not opening it now.~s~ Clean the register out yourself.");
                }

                if (_clerkDown) Help.ShowThisFrame("Take the money out of the register.");
                return;
            }

            // Out again. Whatever happened in there, happened.
            if (_wentInside) Took();
        }

        /// <summary>Whether the man behind the counter is on the floor.</summary>
        private bool _clerkDown;

        /// <summary>
        /// The same fact, for whoever asks afterwards.
        ///
        /// Kept separate from _clerkDown because that one resets with the leg and this has to
        /// survive to the hand-in, which happens after the ride is over and the shop is four
        /// streets away. Lamar has an opinion about it and he cannot have it if the only record
        /// was cleared on the way out of the door.
        /// </summary>
        public bool KilledTheClerk;

        /// <summary>How far into the shop a body counts as the one behind the counter.</summary>
        private const float BodyRange = 12f;

        /// <summary>
        /// Whether anybody in the shop has been put down.
        ///
        /// Deliberately not "the clerk" -- that is the mistake the old robbery was built on. It
        /// picked one man, waited on him, and deadlocked when he turned out to be somebody
        /// else or turned out to be dead. Anybody on the floor in a shop you came in to rob
        /// means the same thing whichever of them it is.
        /// </summary>
        private bool SomebodyDown(Ped player)
        {
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, BodyRange))
                {
                    if (ped == null || !ped.Exists()) continue;
                    if (ped.Handle == player.Handle) continue;
                    if (ped.IsAlive) continue;

                    return true;
                }
            }
            catch
            {
                // A scan that failed is not a body.
            }

            return false;
        }

        /// <summary>The till is a few hundred, so anything at all off the counter counts.</summary>
        private const int TookEnough = 25;

        /// <summary>What the player was carrying when they walked in.</summary>
        private int _moneyIn;

        /// <summary>Books the robbery as done, however it went.</summary>
        private void Took()
        {
            if (_gotCash) return;

            _gotCash = true;

            var take = Game.Player.Money - _moneyIn;

            if (take >= TookEnough)
            {
                Notify.Important("~g~+$" + take.ToString("N0") + "~s~ out of there.");
                LamarSays("THAT'S what I'm talkin' about! Go, go, go!");
                Social?.On(Hoodrich.Social.SocialEvent.StoreRobbed, "");
            }
            else
            {
                LamarSays("You walked in and walked back out. Man, get on the bike.");
            }

            Log.Info("Bike ride: shop done, $" + take + " taken.");
        }

        /// <summary>Who serves in a shop the game did not staff.</summary>
        private static readonly string[] ClerkModels =
        {
            "s_m_y_shop_mask", "a_m_m_indian_01", "a_m_y_business_01", "s_m_m_ammucountry"
        };

        /// <summary>
        /// Puts somebody behind the counter.
        ///
        /// THIS COMMENT WAS AN ORPHAN. The method it describes was deleted when the home-grown
        /// robbery was dropped in favour of the game's own, and the comment, ClerkModels,
        /// _madeClerk and Behind() were all left behind pointing at nothing.
        ///
        /// It has to come back, because the game does not bring its own man back. Kill the
        /// shopkeeper and GTA shuts that store -- "24-7 is closed, please come back later" --
        /// and there is no native anywhere that reopens it or respawns him. So retrying the
        /// ride put you in a shop with nobody in it and nothing to rob. The leg still ended,
        /// because walking in and out is what finishes it, but it ended hollow.
        ///
        /// Only when the game has nobody there. When the real shopkeeper is present this does
        /// nothing at all and the game's own robbery runs exactly as before -- with the alarm,
        /// the star and the till it opens itself. This is the understudy, not the replacement.
        ///
        /// Found rather than placed. The till is a prop with a known model and it is by
        /// definition on the counter, so the game is asked where the nearest one is and the
        /// clerk goes a step behind it -- which beats a coordinate typed into a file, because
        /// it is right in whichever shop you happen to be stood in.
        ///
        /// Which side is "behind" is worked out by trying both and keeping the one with floor
        /// under it. A till faces the customer, but whether that is the prop's forward or its
        /// back depends on the prop, and guessing wrong puts the man on your side of the
        /// counter looking at the crisps.
        /// </summary>
        private void StandUpClerk()
        {
            if (_madeClerk != null && _madeClerk.Exists() && _madeClerk.IsAlive) return;
            _madeClerk = null;

            try
            {
                var till = NearestTill();
                if (till == null) return;

                // SOMEBODY AT THE TILL, not somebody in the shop.
                //
                // Asking whether any ped is near the SHOP is the wrong question -- a 24/7 has
                // customers, and one man buying crisps by the door would have suppressed this
                // every time and left the counter empty anyway. The question is whether anyone
                // is stood where a shopkeeper stands, which is arm's reach of the till.
                foreach (var ped in World.GetNearbyPeds(till.Position, ClerkAtTill))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped == Game.Player.Character) continue;
                    if (ped == _lamar) continue;

                    return;
                }

                // A till faces its customer. Which of the prop's two faces that is depends on
                // the prop, so both are tried and the one standing on floor wins.
                var back = till.Position - till.ForwardVector * ClerkStep;
                var front = till.Position + till.ForwardVector * ClerkStep;

                var spot = Behind(back) ? back : Behind(front) ? front : back;

                foreach (var name in ClerkModels)
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(1500)) return;

                    var ped = World.CreatePed(model, spot, till.Heading + 180f);
                    model.MarkAsNoLongerNeeded();

                    if (ped == null || !ped.Exists()) continue;

                    _madeClerk = ped;

                    ped.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);

                    // A REAL MAN, not a statue. He can be shot, he can be scared, and the leg
                    // already knows what to do about either -- SomebodyDown watches for him
                    // going over and the objective changes to say so.
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    // He does not fight you for it. He is a man on minimum wage behind a till,
                    // and the whole beat is that he hands it over or he does not get the
                    // chance to.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);

                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle,
                                  "WORLD_HUMAN_STAND_IMPATIENT", 0, true);

                    Log.Info("Stood a clerk back up behind the till: " + name + ".");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not stand a clerk up: " + ex.Message);
            }
        }

        /// <summary>The nearest till prop, which is by definition on the counter.</summary>
        private static Prop NearestTill()
        {
            Prop best = null;
            var nearest = float.MaxValue;

            try
            {
                foreach (var prop in World.GetNearbyProps(Shop, ClerkLook))
                {
                    if (prop == null || !prop.Exists()) continue;

                    var name = prop.Model.Hash;
                    var isTill = false;

                    foreach (var till in TillModels)
                    {
                        if (name != new Model(till).Hash) continue;
                        isTill = true;
                        break;
                    }

                    if (!isTill) continue;

                    var away = prop.Position.DistanceTo(Shop);
                    if (away >= nearest) continue;

                    nearest = away;
                    best = prop;
                }
            }
            catch
            {
                // No till found is no clerk stood up, which is the old behaviour.
            }

            return best;
        }

        /// <summary>Every till the game ships, all three verified against its object list.</summary>
        private static readonly string[] TillModels =
        {
            "prop_till_01", "prop_till_02", "prop_till_03"
        };

        /// <summary>How far out of the shop's middle to look for a counter or a person.</summary>
        private const float ClerkLook = 12f;

        /// <summary>How far behind the till he stands.</summary>
        private const float ClerkStep = 0.7f;

        /// <summary>Close enough to the till to BE the man serving, rather than a customer.</summary>
        private const float ClerkAtTill = 2.2f;
        /// <summary>
        /// Unlocks whatever is standing in that doorway, whatever it happens to be called.
        ///
        /// The job used to only exist while the shop was open. It exists at four in the morning
        /// now, which means it can put a man on a bike outside a door that is bolted -- and a
        /// robbery you cannot walk into is worse than one that was greyed out in the menu.
        ///
        /// A door in this game is a model hash and a position, and there is no native that
        /// asks a room what its door is called. So it asks the world what is standing near the
        /// doorway and tells every one of them to be unlocked. Anything in that list that is
        /// not a door is told nothing at all -- the native looks for a door of that model near
        /// that point and finds none, which is a no-op rather than a mistake.
        ///
        /// Both natives, because they answer different questions: one is for a door that is
        /// loaded and one is for a door that is not, and the whole reason this runs on a timer
        /// is that it starts out being the second.
        /// </summary>
        private void OpenTheShop()
        {
            try
            {
                foreach (var prop in World.GetNearbyProps(Shop, DoorReach))
                {
                    if (prop == null || !prop.Exists()) continue;

                    var hash = prop.Model.Hash;
                    var at = prop.Position;

                    Function.Call(Hash.SET_STATE_OF_CLOSEST_DOOR_OF_TYPE, hash,
                                  at.X, at.Y, at.Z, false, 0f, false);

                    Function.Call(Hash.SET_LOCKED_UNSTREAMED_IN_DOOR_OF_TYPE, hash,
                                  at.X, at.Y, at.Z, false, 0f, 0f, 0f);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not unlock the shop: " + ex.Message);
            }
        }

        /// <summary>How far out of the shop's middle a door could be standing.</summary>
        private const float DoorReach = 9f;

        /// <summary>How often the door is tried while the robbery is on.</summary>
        private const int DoorCheckMs = 1500;

        private int _doorAt;
        /// <summary>Whether there is floor at a spot, which is how the right side of a counter
        /// is told from the wrong one.</summary>
        private static bool Behind(Vector3 at)
        {
            try
            {
                return World.GetGroundHeight(new Vector3(at.X, at.Y, at.Z + 1f), out var z,
                                             GetGroundHeightMode.Normal)
                       && Math.Abs(z - at.Z) < 1.5f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The one we stood up, so he can be cleared away with everything else.</summary>
        private Ped _madeClerk;
        /// <summary>Out the door, on the bike, and away from them.</summary>
        private void TickEscape(Ped player)
        {
            // AND AFTER THE FIGHT TOO.
            //
            // KeepUp was called from TickRiding and nowhere else, so the moment the hands went
            // up it stopped being called for the rest of the job -- Rob, Escape and the ride
            // home. Lamar would get back on his bike and then sit on it, because nothing was
            // telling him to go anywhere, and the ride home was a ride home on your own.
            //
            // It is safe in any phase: it re-tasks anybody off his bike and follows with
            // anybody on one, and it rate-limits itself.
            KeepUp(player);

            RemountHomies(player);

            // Still hot. Getting back to the spot with a helicopter over you is not getting
            // away with it, so the job does not end until they have lost you.
            LamarStaysOut();

            if (Game.Player.Wanted.WantedLevel > 0)
            {
                Help.ShowThisFrame("Lose the cops, then get back to the spot.");
                return;
            }

            Phase = BikePhase.Home;
            Notify.Important("~g~They lost you.~s~ " + Objective + ".");
        }

        /// <summary>
        /// Lamar keeps his seat and keeps out of the police's way.
        ///
        /// He is a bodyguard for the courts and that is what the group task makes him -- which
        /// is exactly wrong from the moment there is a wanted level. A man who fights whoever
        /// you are fighting, sat on a bike outside a shop you have just robbed, gets off it and
        /// opens up on the first patrol car. That is Lamar dead or Lamar arrested over a till.
        ///
        /// So for the robbery and the ride out he loses the two attributes that make him do
        /// that -- 3 is BF_CanLeaveVehicle and 5 is BF_AlwaysFight -- and the events that
        /// would provoke it are blocked outright. He still rides, because the group task is a
        /// task rather than a threat response.
        ///
        /// Idempotent and cheap; it runs every tick of two phases and sets the same flags.
        /// </summary>
        private void LamarStaysOut()
        {
            if (_lamar == null || !_lamar.Exists() || !_lamar.IsAlive) return;

            try
            {
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _lamar.Handle, 3, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _lamar.Handle, 5, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _lamar.Handle, 46, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _lamar.Handle, 2, false);

                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _lamar.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _lamar.Handle, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not keep Lamar on his bike: " + ex.Message);
            }
        }

        /// <summary>
        /// Off the bike, and back to the corner he was standing on.
        ///
        /// The job used to end with him sitting on a bicycle in an alley until the handback
        /// either put him back on his mark or, if he had drifted too far, deleted him outright
        /// -- so the man who runs the set finished every ride by vanishing.
        ///
        /// He gets out and walks it instead. The group is dropped first, because a man still
        /// in your crew will turn round and follow you again the moment you move, and the
        /// walk is a sequence so the exit finishes before the walk starts rather than being
        /// replaced by it.
        /// </summary>
        private void SendLamarHome()
        {
            if (_lamar == null || !_lamar.Exists() || !_lamar.IsAlive) return;

            try
            {
                Function.Call(Hash.REMOVE_PED_FROM_GROUP, _lamar.Handle);
                Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, _lamar.Handle, false);

                // Back to the man who hands out jobs: not a target, and not looking for a
                // fight on his own corner.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _lamar.Handle, 3, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _lamar.Handle, 5, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _lamar.Handle, false);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _lamar.Handle, false);

                _lamar.Task.ClearAll();

                var seq = new OutputArgument();
                Function.Call(Hash.OPEN_SEQUENCE_TASK, seq);
                var handle = seq.GetResult<int>();

                Function.Call(Hash.TASK_LEAVE_ANY_VEHICLE, 0, 0, 0);
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, 0,
                              Fixer.Spot.X, Fixer.Spot.Y, Fixer.Spot.Z, 1.0f, -1, 1.5f, 0, 0f);
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, 0,
                              "WORLD_HUMAN_STAND_MOBILE", 0, true);

                Function.Call(Hash.CLOSE_SEQUENCE_TASK, handle);
                Function.Call(Hash.TASK_PERFORM_SEQUENCE, _lamar.Handle, handle);
                Function.Call(Hash.CLEAR_SEQUENCE_TASK, seq);

                Log.Info("Lamar is walking back to his corner.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send Lamar home: " + ex.Message);
            }
        }

        /// <summary>
        /// Lamar, stood over it, enjoying himself.
        ///
        /// A LINE AND A NOISE, and no animation at all any more.
        ///
        /// WORLD_HUMAN_CHEERING was the obvious way to show it and it is a trap: the scenario
        /// loops, nothing in the phase that follows clears it, and re-tasking him to ride only
        /// works if he is asked -- so he stood on that court applauding until something else
        /// happened to him. A man clapping on a loop is worse than a man who simply said the
        /// thing and got back on his bike.
        ///
        /// He is not held here either way. If you ride off immediately, that IS the end of the
        /// moment -- freezing the mission to make sure a celebration is seen would be worse
        /// than missing it.
        /// </summary>
        private void Cheer()
        {
            _laughAt = Game.GameTime + 1700;
            _thirstyAt = Game.GameTime + 3400;

            Says("~y~LAMAR", "AYYY! You seen that?! That's what I'm talkin' 'bout!", 3000);

            Mouth(_lamar, "GENERIC_INSULT_HIGH");
        }

        /// <summary>Something out loud, which is not the same as something on screen.</summary>
        private void Mouth(Ped who, string speech)
        {
            if (who == null || !who.Exists() || !who.IsAlive) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle, speech,
                              "SPEECH_PARAMS_FORCE_SHOUTED");
            }
            catch
            {
                // A line his voice has not got costs nothing.
            }
        }

        /// <summary>A line out of Lamar's mouth, as a subtitle, because these are written.</summary>
        private void LamarSays(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            try { GTA.UI.Screen.ShowSubtitle("~y~LAMAR:~s~ " + line, 3500); }
            catch { /* the objective still says what to do */ }

            Aloud("Lamar", line);

            // And a noise out of him at the same time. The words are written and the sound is
            // not -- there is no recording of these lines and there never will be -- but a man
            // whose mouth moves while his subtitle is up is a man talking, and one whose does
            // not is a caption floating over a silent bloke.
            Mouth(_lamar, Talking[_rng.Next(Talking.Length)]);
        }

        /// <summary>
        /// Angry noises, for the two men squaring up at the courts.
        ///
        /// Names rather than recordings of the written lines, which do not exist. Every one is
        /// wrapped and none is checked: a speech a particular voice has not got simply does not
        /// play, and one silent exchange is not worth code to prevent.
        /// </summary>
        private static readonly string[] Angry =
        {
            "GENERIC_INSULT_HIGH", "CHALLENGE_THREATEN", "GENERIC_CURSE_HIGH",
            "PROVOKE_GENERIC", "GENERIC_INSULT_MED"
        };

        /// <summary>And the ordinary sort, for Lamar going on about something.</summary>
        private static readonly string[] Talking =
        {
            "CHAT_STATE", "GENERIC_HOWS_IT_GOING", "GENERIC_YES", "CHAT_RESP"
        };

        /// <summary>Stopped enough to be pulling in rather than riding past.</summary>
        private static bool Stopped(Ped player)
        {
            try
            {
                var car = player.CurrentVehicle;
                return car == null || !car.Exists() || car.Speed < 2.5f;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>He calls it out here, before you have stopped.</summary>
        private const float ShoutRange = 70f;

        /// <summary>Somebody presses the button under the counter. One, and you are on a bike.</summary>
        private const int RobberyStars = 1;

        /// <summary>True once the shop has been done and the police are somebody's problem.</summary>
        private bool _lawLoose;

        /// <summary>When his second line is due, so it does not land on top of the shout.</summary>
        private int _weaselAt;

        private bool _shouted;
        private bool _robOffered;
        private bool _robAccepted;
        private bool _gotCash;

        private void TickHome(Ped player)
        {
            // AND AFTER THE FIGHT TOO.
            //
            // KeepUp was called from TickRiding and nowhere else, so the moment the hands went
            // up it stopped being called for the rest of the job -- Rob, Escape and the ride
            // home. Lamar would get back on his bike and then sit on it, because nothing was
            // telling him to go anywhere, and the ride home was a ride home on your own.
            //
            // It is safe in any phase: it re-tasks anybody off his bike and follows with
            // anybody on one, and it rate-limits itself.
            KeepUp(player);

            RemountHomies(player);

            // LAMAR has to get back, not just you.
            //
            // It used to end on the player's distance alone, so riding home ahead of him
            // finished the job while he was still two streets away -- and the last thing the
            // mission did was send a man home who had not arrived. The ride out was with him;
            // the ride back is too.
            //
            // Your own arrival still counts, and has to: if he is gone -- dead, deleted,
            // dropped by the watchdog -- waiting for him would be waiting forever.
            var youHome = player.Position.DistanceTo(RideHome) <= HomeRange;
            if (!youHome) return;

            var lamar = _lamar != null && _lamar.Exists() && _lamar.IsAlive;
            if (lamar && _lamar.Position.DistanceTo(LamarHome) > LamarHomeRange) return;

            SendLamarHome();

            ReadyToCollect = true;
            ClearMarker();
        }

        /// <summary>
        /// Where Lamar has to get to before the job is done, read off the HUD stood on it.
        ///
        /// Under the walkway outside his own door rather than out on the street with you, which
        /// is where he ends up anyway once SendLamarHome walks him back.
        /// </summary>
        private static readonly Vector3 LamarHome = new Vector3(-199.669f, -1718.827f, 32.664f);

        /// <summary>
        /// How close he has to be. Wider than his own mark, because he arrives on a bicycle
        /// and parks it wherever the nav mesh lets him rather than on a spot.
        /// </summary>
        private const float LamarHomeRange = 32f;

        // ---- the people --------------------------------------------------------

        /// <summary>
        /// Puts a man in your crew, properly.
        ///
        /// Riding alongside is a TASK -- it says where to be and nothing about whose side he is
        /// on, so a homie escorting your bike would sit on it while somebody knocked you off
        /// yours. The group is what makes him yours: group members defend the leader, break off
        /// to take on whoever the leader is fighting, and come back afterwards without being
        /// told to.
        ///
        /// NeverLeavesGroup on top of it, because the default is that a man who gets far enough
        /// behind, or frightened enough, stops being in your group and never rejoins.
        /// </summary>
        private static void Enlist(Ped ped, Ped player)
        {
            if (ped == null || !ped.Exists() || player == null || !player.Exists()) return;

            try
            {
                var group = Function.Call<int>(Hash.GET_PED_GROUP_INDEX, player.Handle);

                Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, ped.Handle, group);
                Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, ped.Handle, true);

                // Loose, and spread out. A tight formation on bicycles is four men riding into
                // each other's back wheel.
                Function.Call(Hash.SET_GROUP_FORMATION, group, 1);
                Function.Call(Hash.SET_GROUP_FORMATION_SPACING, group, 3.0f, 2.0f, 6.0f);
                Function.Call(Hash.SET_GROUP_SEPARATION_RANGE, group, 250f);

                // 5 is BF_AlwaysFight and 46 is BF_CanFightArmedPedsWhenNotArmed -- which is
                // the one that matters on a job with no guns on it, because without it an
                // unarmed man will not start on somebody who is holding something.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);

                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 58, true);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);

                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
            }
            catch
            {
                // He rides along without being in the group, which is where he was before.
            }
        }

        /// <summary>
        /// Whoever you are swinging at, they swing at.
        ///
        /// Group membership makes them defend YOU, which is only half of a bodyguard -- it does
        /// not make them start on somebody you have decided to start on. This reads the man
        /// under your reticle and hands him to anybody who is not already busy.
        ///
        /// Only on somebody the crew is allowed to hit: the check is the game's own
        /// relationship test, so on the courts they pile onto a Balla and on the way there they
        /// ignore the pedestrian you happen to be pointing at.
        /// </summary>
        private void SicThemOn(Ped player)
        {
            if (Game.GameTime < _nextSic) return;
            _nextSic = Game.GameTime + SicIntervalMs;

            Ped foe = null;

            try
            {
                var found = new OutputArgument();

                if (Function.Call<bool>(Hash.GET_PLAYER_TARGET_ENTITY, Game.Player, found))
                {
                    foe = Entity.FromHandle(found.GetResult<int>()) as Ped;
                }

                if (foe == null && Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, player.Handle))
                {
                    foe = Function.Call<Ped>(Hash.GET_MELEE_TARGET_FOR_PED, player.Handle);
                }
            }
            catch { /* nobody, then */ }

            if (foe == null || !foe.Exists() || !foe.IsAlive || foe == player) return;

            foreach (var mate in _homies)
            {
                if (mate == null || !mate.Exists() || !mate.IsAlive) continue;

                try
                {
                    if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, mate.Handle, foe.Handle)) continue;

                    // Hostile to HIM, not to you -- a man who is only YOUR enemy is your own
                    // problem. There is no are-these-two-hostile native in this build, so the
                    // relationship is read directly: 4 is dislike and 5 is hate, and anything
                    // milder is somebody they have no reason to hit.
                    var feeling = Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_PEDS,
                                                     mate.Handle, foe.Handle);

                    if (feeling == RelDislike || feeling == RelHate)
                    {
                        Function.Call(Hash.TASK_COMBAT_PED, mate.Handle, foe.Handle, 0, 16);
                    }
                }
                catch { /* he stays on whatever he was doing */ }
            }
        }

        /// <summary>
        /// Whose bike is whose, by ped handle.
        ///
        /// THIS IS WHY HE DID NOT FOLLOW. _homies and _bikes were two lists read as though the
        /// same index meant the same man, and they are not filled in the same order: Lamar is
        /// lent at the start of the job and goes into _homies FIRST, but he is put on a bike
        /// LAST, after the other three -- so every man was paired with somebody else's bicycle,
        /// off by one, all the way down.
        ///
        /// What that looks like in play is exactly what was reported. KeepUp checks whether a
        /// man is on "his" bike, decides Lamar is not, and tells him to get on one of the
        /// others -- every two seconds, for the entire ride. He climbs off the bike he is
        /// riding perfectly well, walks to a bicycle with somebody already on it, fails, and
        /// gets told again. Four men doing that to each other is a ride nobody arrives at.
        ///
        /// A handle is the thing that actually identifies a man, so the pairing is written down
        /// against it and the index is never asked again.
        /// </summary>
        private readonly Dictionary<int, Vehicle> _ride = new Dictionary<int, Vehicle>();

        /// <summary>The bike this man rode in on, or whatever he is sat on now.</summary>
        private Vehicle BikeFor(Ped ped)
        {
            if (ped == null || !ped.Exists()) return null;

            Vehicle bike;

            if (_ride.TryGetValue(ped.Handle, out bike) && bike != null && bike.Exists())
            {
                return bike;
            }

            // He is on something we did not write down -- knocked off and back on, or given a
            // replacement. That is his bike now.
            var now = ped.CurrentVehicle;

            if (now != null && now.Exists())
            {
                _ride[ped.Handle] = now;
                return now;
            }

            return null;
        }

        /// <summary>
        /// Whether Franklin has started it himself.
        ///
        /// Two questions, because one of them is not enough on its own. Being in melee combat
        /// catches a swing that connected or was blocked; the damage check catches the case
        /// where the game has not decided you are "in melee" yet but somebody on that court has
        /// just been hit by you. Either is Franklin having made his position clear.
        /// </summary>
        private bool ThrownHands(Ped player)
        {
            if (player == null || !player.Exists()) return false;

            try
            {
                if (Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, player.Handle)) return true;

                foreach (var ped in _rivals)
                {
                    if (ped == null || !ped.Exists()) continue;

                    if (Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY,
                                            ped.Handle, player.Handle, true))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Then the written exchange runs its course, which is the old behaviour.
            }

            return false;
        }

        /// <summary>
        /// Off the bikes, onto the court, and stood with you.
        ///
        /// Nobody did this before, and it showed on the one beat the whole ride builds to: four
        /// men rode up to a basketball court and then had the argument, and the fight, sitting
        /// on bicycles. Lamar included -- the man who talked you into coming, watching it from
        /// the saddle.
        ///
        /// A SEQUENCE, for the reason this file has already been bitten by twice: a second task
        /// does not queue behind the first, it replaces it. Telling a man to get off a bike and
        /// then telling him to walk over throws the dismount away before it starts, and he sits
        /// there. Inside a sequence 0 is the ped performing it and each task waits its turn.
        ///
        /// They follow YOU rather than a coordinate, because the court is wherever you have
        /// walked to on it, and the offsets fan them out so four men do not arrive on the same
        /// paving slab.
        /// </summary>
        private void GetDown(Ped player)
        {
            if (player == null || !player.Exists()) return;

            var spread = 0;

            foreach (var ped in _homies)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                // NOT THE ONES STILL RIDING IN.
                //
                // CallThemUp runs on the line above this and gives anybody more than forty
                // metres out a drive-to-the-courts order. This then walked the whole list and
                // replaced that order with a dismount -- microseconds later, before the first
                // one could turn a wheel. Ride ahead of Lamar and he would get off his bike in
                // the middle of Chamberlain and jog the last sixty metres, leaving the bike
                // where it stood.
                //
                // Same radius CallThemUp uses, so the two agree on who is still on the road.
                if (Flat(ped.Position, Courts) > 40f) continue;

                var side = spread % 2 == 0 ? 1f : -1f;
                var back = 1.6f + spread * 0.5f;
                var across = 1.4f + spread * 0.6f;

                spread++;

                try
                {
                    if (!ped.IsInVehicle())
                    {
                        Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, ped.Handle,
                                      player.Handle, across * side, -back, 0f,
                                      3f, -1, 1.5f, true);
                        continue;
                    }

                    var slot = new OutputArgument();

                    Function.Call(Hash.OPEN_SEQUENCE_TASK, slot);
                    var seq = slot.GetResult<int>();

                    // Flag 0 is the ordinary dismount rather than a dive off it.
                    Function.Call(Hash.TASK_LEAVE_VEHICLE, 0, 0, 0);

                    Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, 0, player.Handle,
                                  across * side, -back, 0f, 3f, -1, 1.5f, true);

                    Function.Call(Hash.CLOSE_SEQUENCE_TASK, seq);
                    Function.Call(Hash.TASK_PERFORM_SEQUENCE, ped.Handle, seq);
                    Function.Call(Hash.CLEAR_SEQUENCE_TASK, slot);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not get a homie off his bike: " + ex.Message);
                }
            }

            Log.Info("BikeRide: everybody off the bikes at the courts.");
        }

        private int _nextSic;

        /// <summary>Twice a second. Often enough to feel immediate, rare enough to be free.</summary>
        private const int SicIntervalMs = 500;

        private void SpawnHomiesOnBikes(Ped player)
        {
            var gang = _crew.Current;
            if (gang == null) return;

            // Zero means zero.
            //
            // This was Max(1, ...), so a job written with no homies got one anyway. The bike
            // ride is Lamar and you -- he is brought separately, by BringLamar -- and two
            // extra men on bicycles turned a conversation between two people into a convoy.
            var count = Math.Max(0, _def.Homies);
            if (count == 0) return;

            for (var i = 0; i < count; i++)
            {
                // Out of the alley round the back, not conjured at your elbow. Three men
                // appearing beside you is a spawn; three men coming up the alley is an arrival.
                var spot = Ground(HomieSpot).Around(2.5f + i * 1.6f);

                var bike = SpawnBike(spot, HomieHeading);
                if (bike == null) continue;

                var ped = SpawnGangMember(gang, spot);
                if (ped == null)
                {
                    Release(bike);
                    continue;
                }

                _homies.Add(ped);
                _bikes.Add(bike);
                _ride[ped.Handle] = bike;

                try
                {
                    ped.SetIntoVehicle(bike, VehicleSeat.Driver);

                    MakeSides();
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle,
                                  _usGroup != 0 ? _usGroup : gang.GroupHash);
                    Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, ped.Handle, false, false);

                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                    // 46 is BF_CanFightArmedPedsWhenNotArmed, NOT BF_AlwaysFight. That is 5.
                    Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);

                    Enlist(ped, player);
                    Escort(ped, bike, player);

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Green;
                        blip.Scale = 0.6f;
                        blip.Name = "Homie";
                        _blips.Add(blip);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a homie on a bike: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Takes him off his corner and puts him behind you, on foot.
        ///
        /// He used to arrive already sat on a bicycle at the moment you got on yours, which
        /// meant the walk across the lot to your own bike was a walk on your own -- the man who
        /// had just asked you to come with him standing exactly where you left him until the
        /// scene needed him. He comes now when the job starts and follows you to it.
        ///
        /// Everything that makes him one of the crew happens HERE rather than at the bike:
        /// the group, the relationship, the bat, being killable. Only the riding is left for
        /// later, because only the riding needs a bike.
        ///
        /// He goes into _homies with the rest, which is deliberate: every piece of behaviour
        /// this mission has for a homie -- keeping up, remounting, being called up at the
        /// courts, fighting -- then applies to him for free. What does NOT apply is the
        /// teardown, which is handled where it happens.
        /// </summary>
        private void LendLamar(Ped player)
        {
            if (Boss == null || _lamar != null) return;

            var gang = _crew.Current;
            if (gang == null) return;

            var ped = Boss.Lend();
            if (ped == null || !ped.Exists()) return;

            _lamar = ped;
            _homies.Add(ped);

            try
            {
                MakeSides();
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle,
                              _usGroup != 0 ? _usGroup : gang.GroupHash);
                Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, ped.Handle, false, false);

                // BOTH OFF, which is the change. 46 is BF_CanFightArmedPedsWhenNotArmed and 5
                // is BF_AlwaysFight, and with the pair of them on he opened up on every rival
                // he could see from the saddle -- which turns a ride across two neighbourhoods
                // into a running battle and gets the fight at the courts started four streets
                // early. He fights when the job says so and not before.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, false);

                // A bat and nothing else.
                //
                // Whatever he was carrying goes first, because "keep what he had" meant a man
                // who had been handed a rifle at some point in the session still had it. The
                // bat is the whole point of the trip: this is a message being delivered by
                // hand, and a message delivered with a rifle is a different message.
                try
                {
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_BAT"),
                                  1, false, true);
                }
                catch { /* he goes with his hands */ }

                Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);

                // Killable now, where he is normally not. A bodyguard who cannot be shot is
                // not a bodyguard, and the mission already fails properly if he goes down.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, true);

                // AND HE DOES NOT REACT TO BEING WALKED INTO.
                //
                // This was false, which left him answering every event that happened near him
                // -- including the player's shoulder. Bump him on the way to the bikes and he
                // squares up to the man he is about to ride across two neighbourhoods with.
                //
                // Blocking events does not make him unkillable; CAN_BE_TARGETTED on the line
                // above is what decides that, and it stays. What this stops is him deciding
                // things for himself, which is exactly what the combat attributes two lines up
                // already say: he fights when the job says so and not before. It comes off
                // again in ItGoesOff, at the courts, when the job says so.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);

                Enlist(ped, player);
                OnFoot(ped, player);

                var blip = ped.AddBlip();
                if (blip != null && blip.Exists())
                {
                    blip.Color = BlipColor.Green;
                    blip.Scale = 0.8f;
                    blip.Name = "Lamar";
                    _blips.Add(blip);
                }

                Log.Info("Lamar is walking out with you.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not bring Lamar along: " + ex.Message);
            }
        }

        /// <summary>
        /// Right behind you, rather than somewhere in the general area.
        ///
        /// A group member left to the formation wanders within its spacing, which is right for
        /// four men on bicycles crossing a city and much too loose for one man walking twenty
        /// feet with you. Following an OFFSET is the tighter instruction: a metre back and a
        /// little to the side, held, so he arrives at the bike when you do.
        /// </summary>
        private static void OnFoot(Ped ped, Ped player)
        {
            if (ped == null || !ped.Exists() || player == null || !player.Exists()) return;

            try
            {
                Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, ped.Handle, player.Handle,
                              FootOffsetX, FootOffsetY, 0f, FootSpeed, -1, FootStop, true);
            }
            catch
            {
                // The group formation still has him in the right postcode.
            }
        }

        /// <summary>A stride behind and off your shoulder, and he does not stop short.</summary>
        private const float FootOffsetX = 0.8f;
        private const float FootOffsetY = -1.2f;
        private const float FootSpeed = 2.0f;
        private const float FootStop = 1.0f;

        /// <summary>
        /// Puts him on a bike, once you are on yours.
        ///
        /// Under him rather than at a mark up the lot.
        ///
        /// He used to be borrowed off his corner and put on a bike parked twenty metres away,
        /// which is a man teleporting to a bicycle. The bike is made a stride in front of where
        /// he is actually stood and he is put straight on it, so the whole thing reads as him
        /// getting on it beside you -- and it needs no second coordinate to be kept in step
        /// with wherever he has walked to.
        /// </summary>
        private void MountLamar(Ped player)
        {
            // Not lent yet, so lend him now. Belt and braces: if the start-of-job call did not
            // take -- he was missing, the boss had him -- this is still a ride with Lamar on it
            // rather than a ride he is quietly absent from.
            if (_lamar == null) LendLamar(player);

            var ped = _lamar;
            if (ped == null || !ped.Exists() || !ped.IsAlive) return;
            if (ped.IsInVehicle()) return;

            // HIS BIKE IS WHERE HIS BIKE IS, and he walks to it.
            //
            // It used to be made a stride in front of wherever he happened to be standing and
            // he was put straight onto it -- a bicycle appearing under a man, which is the one
            // thing the whole walk-out was written to avoid. He is stood on the same lot as the
            // bike, so there is nothing to teleport: he walks the few strides and gets on it
            // like anybody would, and you can watch him do it while you get on yours.
            var bike = SpawnBike(LamarBike, LamarBikeHeading);
            if (bike == null) return;

            _bikes.Add(bike);
            _ride[ped.Handle] = bike;

            try
            {
                // -1 is "no timeout", seat -1 is the driver's, 2f is a walk. He is ten metres
                // away at most; making him run to it would look like he was late.
                Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, bike.Handle,
                              -1, (int)VehicleSeat.Driver, 2f, 1, 0);

                // NOT escorted yet. There is nothing to follow until he is on it, and KeepUp
                // picks him up the moment he is -- issuing the follow now would replace the
                // walk he has just been given, which is the mistake this file keeps making.
                Log.Info("Lamar is walking to his bike.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put Lamar on a bike: " + ex.Message);
            }
        }

        /// <summary>
        /// Ride alongside rather than follow on foot.
        ///
        /// A group member on a bicycle gets off it to keep up with you, which is not a ride out,
        /// it is three men jogging. Escorting the player's own bike keeps them mounted.
        /// </summary>
        private static void Escort(Ped ped, Vehicle bike, Ped player)
        {
            try
            {
                var target = player.CurrentVehicle;

                if (target != null && target.Exists())
                {
                    // FOLLOW, not ESCORT.
                    //
                    // An escort has its own idea of where it should be relative to you and will
                    // happily carry on to that position when you stop, which is why they rode
                    // off. Following a vehicle means what it says: they go where you went, at
                    // your speed, and they stop when you stop.
                    // 786984, not 786603.
                    //
                    // The default is the law-abiding set: StopBeforeVehicles, StopBeforePeds
                    // and StopAtTrafficLights all on. A man following you across two
                    // neighbourhoods on that set stops at every red you rode through and is a
                    // block behind by the second junction -- which is most of what "he doesn't
                    // keep up" actually was. Those three come off and AllowGoingWrongWay goes
                    // on; he keeps AvoidObjects and AvoidEmptyVehicles so he still goes ROUND
                    // things rather than through them.
                    // FOUR METRES, NOT EIGHT. The last argument is minDistance -- how far back
                    // he is content to sit -- and eight is most of a car length past what reads
                    // as riding WITH somebody. At four he is at your shoulder, which is the
                    // whole picture of the two of them going to the courts together.
                    Function.Call(Hash.TASK_VEHICLE_FOLLOW, ped.Handle, bike.Handle, target.Handle,
                                  25f, FollowStyle, 4);

                    // AND SAID AGAIN, TO THE TASK ITSELF.
                    //
                    // The style handed to TASK_VEHICLE_FOLLOW is a starting condition; this is
                    // the native that sets it on the drive task that is actually running, and
                    // it is what makes the off-road bits stick. FollowStyle already carries
                    // TakeShortestPath and the ignore-roads flag -- what it did not have was
                    // anything guaranteeing they survived the task being set up.
                    //
                    // Which is the difference between a man who follows you across a lot and
                    // down a drainage channel, and one who turns round at the kerb looking for
                    // a road to do it on.
                    Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, ped.Handle, FollowStyle);
                    return;
                }

                // You are not on a bike yet, so there is nothing to follow -- and this used
                // to send him to the COURTS.
                //
                // That is the whole of "Lamar rode off the other way". Escort is called the
                // moment he is put on his bike, which is while you are still walking to yours,
                // so the fallback fired every single time and he set off across Chamberlain on
                // his own while you were getting on.
                //
                // He waits instead. The group follow takes over the second you are moving, and
                // KeepLamarClose picks him up if it does not.
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, ped.Handle, bike.Handle, 1, 4000);
            }
            catch
            {
                // The game own driving takes over.
            }
        }

        /// <summary>
        /// Reissued while riding, because one escort task does not survive the trip.
        ///
        /// A bicycle AI that clips a kerb, gets knocked off, or loses its task ends up standing
        /// in an alley two streets back for the rest of the job. Re-tasking anyone who has
        /// fallen behind or come off their bike costs nothing, and it is the difference between
        /// riding out with the homies and riding out on your own.
        /// </summary>
        private void KeepUp(Ped player)
        {
            if (Game.GameTime < _nextRetask) return;
            _nextRetask = Game.GameTime + RetaskGapMs;

            // THE MOMENT YOU GET ON IS THE MOMENT THERE IS SOMETHING TO FOLLOW.
            //
            // Escort is issued once, as each man mounts -- which is while you are still walking
            // to your own bike. With nothing to follow yet it takes its no-vehicle branch and
            // parks him for four seconds, and after that nobody tells him again: the retask
            // below only fires for somebody already TrailingRange behind, and a man stood at
            // your shoulder never is. So he sat out his four seconds and then rode wherever the
            // game felt like, which is the whole of "Lamar doesn't follow at the start".
            //
            // Watching the vehicle HANDLE rather than a flag, so swapping bikes mid-ride issues
            // it again, and stepping off resets it for when you get back on.
            var riding = player.CurrentVehicle;
            var seat = riding != null && riding.Exists() ? riding.Handle : 0;

            if (seat == 0)
            {
                _followingFor = 0;
            }
            else if (seat != _followingFor)
            {
                _followingFor = seat;

                for (var i = 0; i < _homies.Count; i++)
                {
                    var man = _homies[i];
                    if (man == null || !man.Exists() || !man.IsAlive) continue;

                    var his = BikeFor(man);
                    if (his == null || !his.Exists() || !man.IsInVehicle(his)) continue;

                    Escort(man, his, player);
                }
            }

            for (var i = 0; i < _homies.Count; i++)
            {
                var ped = _homies[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                var bike = BikeFor(ped);
                if (bike == null || !bike.Exists()) continue;

                if (!ped.IsInVehicle(bike))
                {
                    try
                    {
                        // Already on his way? Then leave him alone.
                        //
                        // This runs on a timer, and re-issuing an enter-vehicle task restarts
                        // the walk from the beginning -- so a man twelve strides from his bike
                        // was sent back to step one every couple of seconds and never arrived.
                        // The same shape as the follow task this file already throttles, and
                        // the reason he could be left jogging on the spot beside a bicycle.
                        if (!Function.Call<bool>(Hash.IS_PED_GETTING_INTO_A_VEHICLE, ped.Handle))
                        {
                            Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, bike.Handle,
                                          -1, (int)VehicleSeat.Driver, 2f, 1, 0);
                        }
                    }
                    catch { /* they will walk it */ }

                    continue;
                }

                // Anybody who has drifted a long way behind is brought back rather than left to
                // ride the whole route on their own. Four streets back is not following.
                // Sent after you rather than moved to you. A bike that jumps to your back wheel
                // is a homie who was never really riding with you, and the ride out IS the job.
                if (ped.Position.DistanceTo(player.Position) > CatchUpRange)
                {
                    try
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, ped.Handle, bike.Handle,
                                      player.Position.X, player.Position.Y, player.Position.Z,
                                      24f, 0, bike.Model.Hash, 786603, 8f, true);
                    }
                    catch { /* they will catch up or they will not */ }

                    continue;
                }

                // Only when they have actually fallen behind. Re-issuing a follow task every
                // couple of seconds restarts it, so a homie riding perfectly well beside you
                // was being told to start following you again, over and over, all the way
                // across two neighbourhoods.
                if (ped.Position.DistanceTo(player.Position) > TrailingRange)
                {
                    Escort(ped, bike, player);
                }
            }
        }

        /// <summary>Far enough back to be worth telling again. Anything closer is riding with you.</summary>
        /// <summary>The bike they were last told to follow, so it is issued once per mount.</summary>
        private int _followingFor;

        private const float TrailingRange = 28f;

        /// <summary>
        /// Tells anybody still on the road where it is kicking off.
        ///
        /// They used to be teleported onto the court, so a homie who came off two streets back
        /// simply appeared beside you -- and the moment that can happen, none of the riding
        /// matters. Now they are told and they ride there. If one of them misses the fight
        /// because he clipped a kerb at a junction, that is a thing that happened.
        /// </summary>
        private void CallThemUp()
        {
            foreach (var ped in _homies)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (Flat(ped.Position, Courts) <= 40f) continue;

                try
                {
                    var bike = ped.CurrentVehicle;

                    if (bike != null && bike.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, ped.Handle, bike.Handle,
                                      Courts.X, Courts.Y, Courts.Z, 24f, 0, bike.Model.Hash,
                                      786603, 8f, true);
                    }
                    else
                    {
                        Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                                      Courts.X, Courts.Y, Courts.Z, 2f, 60000, 5f, 0, 0f);
                    }

                    Log.Info("BikeRide: told a straggler where it is.");
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not call a homie up: " + ex.Message);
                }
            }
        }

        /// <summary>Puts them back on their bikes once you are back on yours.</summary>
        /// <summary>
        /// Keeps Lamar on your wheel, and picks him up when he is not.
        ///
        /// The group task handles following. It does not handle being wedged on a kerb, boxed
        /// in by traffic, or knocked off and left half a neighbourhood back -- and on a job
        /// that is two people, losing one of them is losing half the job.
        ///
        /// STUCK, not merely far. A plain distance check fires constantly on a bike: ten metres
        /// is a second of riding, and warping him every second is worse than losing him. So the
        /// distance only opens the question and the CLOSING of it answers: if he has not got
        /// any nearer in four seconds, he is not coming, and he goes behind you instead.
        ///
        /// Behind and slightly to the side, on the ground, facing the way you are facing --
        /// so he arrives where a man who had been following would be, rather than appearing
        /// in front of you.
        /// </summary>
        private void KeepLamarClose(Ped player)
        {
            if (_lamar == null || !_lamar.Exists() || !_lamar.IsAlive) return;
            if (player == null || !player.Exists()) return;

            var gap = player.Position.DistanceTo(_lamar.Position);
            var now = Game.GameTime;

            if (gap <= LamarLeash)
            {
                _lamarSlipSince = 0;
                _lamarWasAt = gap;
                return;
            }

            if (_lamarSlipSince == 0)
            {
                _lamarSlipSince = now;
                _lamarWasAt = gap;
                return;
            }

            // Closing the gap counts as coming. Only a man who is no closer than he was is
            // actually stuck.
            if (gap < _lamarWasAt - 2f)
            {
                _lamarSlipSince = now;
                _lamarWasAt = gap;
                return;
            }

            if (now - _lamarSlipSince < LamarStuckMs && gap < LamarLost) return;

            try
            {
                var behind = player.Position - player.ForwardVector * 6f + player.RightVector * 1.5f;

                float groundZ;
                if (World.GetGroundHeight(new Vector3(behind.X, behind.Y, behind.Z + 2f),
                                          out groundZ, GetGroundHeightMode.Normal))
                {
                    behind = new Vector3(behind.X, behind.Y, groundZ + 0.2f);
                }

                // The bike he is on -- and if he has not got one, he gets one.
                //
                // He loses his somewhere on most runs: knocked off it, or it is left behind
                // when the engine cleans up a street he is no longer on. Teleporting him
                // without one puts a man on foot behind a bicycle and he is dropped again
                // within seconds, which is most of what "he keeps getting stuck" actually was.
                var bike = _lamar.CurrentVehicle;

                // NOT AT THE COURTS. This exists for the ride, where a man without a bicycle
                // is a man who cannot keep up -- and at the court it would hand him one in the
                // middle of the fight and sit him on it. On foot beats mounted there, and the
                // catch-up is what he needs put right rather than the transport.
                var riding = Phase != BikePhase.Words && Phase != BikePhase.Fight;

                if (riding && (bike == null || !bike.Exists()))
                {
                    bike = SpawnBike(behind, player.Heading);

                    if (bike != null && bike.Exists())
                    {
                        _bikes.Add(bike);
                        _ride[_lamar.Handle] = bike;
                        _lamar.SetIntoVehicle(bike, VehicleSeat.Driver);
                    }
                }

                if (bike != null && bike.Exists())
                {
                    bike.Position = behind;
                    bike.Heading = player.Heading;
                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, bike.Handle);

                    // Back on it if he is beside it rather than on it.
                    if (!_lamar.IsInVehicle(bike)) _lamar.SetIntoVehicle(bike, VehicleSeat.Driver);
                }
                else
                {
                    _lamar.Position = behind;
                    _lamar.Heading = player.Heading;
                }

                Escort(_lamar, bike, player);
                Log.Info("Lamar was " + gap.ToString("0") + "m back and not closing; brought him up.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not bring Lamar up: " + ex.Message);
            }

            _lamarSlipSince = 0;
            _lamarWasAt = 0f;
        }

        /// <summary>Past this he is behind rather than beside you, and the clock starts.</summary>
        private const float LamarLeash = 30f;

        /// <summary>How long he gets to close it before he is simply moved.</summary>
        private const int LamarStuckMs = 4000;

        /// <summary>Far enough that he is gone rather than slow, and the clock is not waited on.</summary>
        private const float LamarLost = 120f;

        private int _lamarSlipSince;
        private float _lamarWasAt;

        /// <summary>
        /// Back on the bikes after the court, and back on your wheel once you are riding.
        ///
        /// It used to do nothing at all until YOU were mounted, which is the wrong way round
        /// on the one beat that needed it: the fight ends, you walk to your bike, and behind
        /// you three men and Lamar are stood in the middle of a basketball court watching. By
        /// the time you are on yours they start walking to theirs, so you ride off alone and
        /// they catch up somewhere near the shop, if at all.
        ///
        /// They get on their bikes when the fighting stops. The escort is the part that waits
        /// for you, because there is nothing to follow until there is.
        /// </summary>
        private void RemountHomies(Ped player)
        {
            var yours = player == null ? null : player.CurrentVehicle;
            var riding = yours != null && yours.Exists();

            foreach (var ped in _homies)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                var bike = BikeFor(ped);
                if (bike == null || !bike.Exists()) continue;

                try
                {
                    if (!ped.IsInVehicle(bike))
                    {
                        Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, bike.Handle,
                                      -1, (int)VehicleSeat.Driver, 2f, 1, 0);
                        continue;
                    }

                    // On it, and you are moving. Give him something to follow rather than
                    // leaving him sat on a bicycle at the kerb.
                    if (riding && ped.Position.DistanceTo(player.Position) > TrailingRange)
                    {
                        Escort(ped, bike, player);
                    }
                }
                catch
                {
                    // They will walk it.
                }
            }
        }

        /// <summary>
        /// Two relationship groups that exist only while this job does.
        ///
        /// The rivals used to be put straight into the real Ballas group, which is correct in
        /// every way except the one that matters: it makes this a Ballas-versus-Families
        /// problem rather than OUR problem. Every ambient Families ped within earshot piled in,
        /// every passing Balla piled in on the other side, and a straightener between four men
        /// on a basketball court turned into whoever happened to be walking past.
        ///
        /// So both sides get a private group each. They hate each other and nobody else, and
        /// nobody else has any opinion about them, because a group nobody has set a
        /// relationship with is neutral by default. The rest of Chamberlain watches.
        /// </summary>
        private int _usGroup;
        private int _themGroup;

        private const string UsGroupName = "HOODRICH_COURTS_US";
        private const string ThemGroupName = "HOODRICH_COURTS_THEM";

        /// <summary>The SET_RELATIONSHIP_BETWEEN_GROUPS scale, as used elsewhere in the mod.</summary>
        private const int RelRespect = 1;
        private const int RelDislike = 4;
        private const int RelHate = 5;

        /// <summary>Nought is companion, one respect, three neutral, five hate.</summary>
        private const int RelNeutral = 3;

        /// <summary>
        /// Makes the two groups, once per run.
        ///
        /// ADD_RELATIONSHIP_GROUP writes the new hash through a pointer argument rather than
        /// returning it, and handing DOES_RELATIONSHIP_GROUP_EXIST a name instead of a hash
        /// marshals a pointer that never matches -- both already learned the hard way when the
        /// vanilla gang groups all looked missing.
        /// </summary>
        private void MakeSides()
        {
            if (_usGroup != 0 && _themGroup != 0) return;

            try
            {
                _usGroup = Function.Call<int>(Hash.GET_HASH_KEY, UsGroupName);
                _themGroup = Function.Call<int>(Hash.GET_HASH_KEY, ThemGroupName);

                if (!Function.Call<bool>(Hash.DOES_RELATIONSHIP_GROUP_EXIST, _usGroup))
                {
                    var made = new OutputArgument();
                    Function.Call(Hash.ADD_RELATIONSHIP_GROUP, UsGroupName, made);
                }

                if (!Function.Call<bool>(Hash.DOES_RELATIONSHIP_GROUP_EXIST, _themGroup))
                {
                    var made = new OutputArgument();
                    Function.Call(Hash.ADD_RELATIONSHIP_GROUP, ThemGroupName, made);
                }

                var you = Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER");

                // Both ways round, because the native sets one direction at a time and a
                // one-sided hatred is a man being punched who will not punch back.
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelHate, _themGroup, _usGroup);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelHate, _usGroup, _themGroup);

                // They want you. Your own people do not, which has to be said out loud -- they
                // are no longer in the Families group that the game already knows you are in
                // with, so without this they have no view on you at all.
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelHate, _themGroup, you);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelRespect, _usGroup, you);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelRespect, you, _usGroup);

                // And everybody else on this block stays out of it, both ways round.
                //
                // Two separate problems with one cause. Lamar is in a group of his own for the
                // length of this job, so to the Families standing on the corner he is not
                // Families -- he is a stranger, and one of them takes a swing at him. And the
                // Ballas at the courts are in a group of their own too, which the ambient
                // Families DO hate on sight, so half the neighbourhood was arriving to a fight
                // that is supposed to be four men and a bat.
                //
                // Named the group rather than found it: AMBIENT_GANG_FAMILY is what the game
                // calls them and what every Families ped in the world is in.
                var theirs = Function.Call<int>(Hash.GET_HASH_KEY, "AMBIENT_GANG_FAMILY");

                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelRespect, _usGroup, theirs);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelRespect, theirs, _usGroup);

                // Neutral rather than friendly toward the other side. Our own people are not
                // fond of Ballas and should not have to pretend otherwise -- they simply have
                // no reason to cross the road about these particular ones.
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelNeutral, theirs, _themGroup);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, RelNeutral, _themGroup, theirs);
            }
            catch (Exception ex)
            {
                // Without the groups everybody falls back to their real gang, which is the old
                // behaviour -- a bigger fight than intended, rather than no fight.
                Log.Debug("Could not make the court sides: " + ex.Message);
                _usGroup = 0;
                _themGroup = 0;
            }
        }

        private void DropSides()
        {
            if (_usGroup == 0 && _themGroup == 0) return;

            try
            {
                var you = Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER");

                Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelHate, _themGroup, _usGroup);
                Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelHate, _usGroup, _themGroup);
                Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelHate, _themGroup, you);
                Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelRespect, _usGroup, you);
                Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelRespect, you, _usGroup);
            }
            catch { /* teardown */ }

            _usGroup = 0;
            _themGroup = 0;
        }

        private void SpawnRivals()
        {
            var gang = _gangs.Get(_def.TargetGang);
            if (gang == null) return;

            MakeSides();

            var count = Math.Max(1, _def.Targets);

            for (var i = 0; i < count; i++)
            {
                var ped = SpawnGangMember(gang, Courts.Around(CourtSpread));
                if (ped == null) continue;

                _rivals.Add(ped);

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);

                    // Their own side, not the Ballas. They look like Ballas and they are here
                    // as Ballas -- they simply do not carry the whole gang's quarrels with
                    // them onto this court.
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle,
                                  _themGroup != 0 ? _themGroup : gang.GroupHash);
                    Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, ped.Handle, false, false);

                    // Hands, and nothing they can change their mind about halfway through.
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                    // 46 is BF_CanFightArmedPedsWhenNotArmed, NOT BF_AlwaysFight. That is 5.

                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle,
                                  "WORLD_HUMAN_STAND_IMPATIENT", 0, true);

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Red;
                        blip.Scale = 0.7f;
                        blip.Name = gang.Name;
                        _blips.Add(blip);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not set up a rival: " + ex.Message);
                }
            }

            Log.Info("BikeRide: " + _rivals.Count + " waiting at the courts.");
        }

        /// <summary>The exchange is over and somebody has to go first.</summary>
        private void ItGoesOff()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            Phase = BikePhase.Fight;
            _nextFightPost = Game.GameTime + 2000;

            foreach (var ped in _rivals)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);
                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                }
                catch
                {
                    // The game's AI takes over.
                }
            }

            // Everybody, not just whoever you happened to be facing. One at a time queuing up
            // for a turn is a training dummy, not four men on a court.
            foreach (var ped in _homies)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    // Off the leash, here and nowhere earlier. Everything on our side rode out
                    // deaf to events on purpose so nobody started anything on the way; this is
                    // the moment the job says otherwise.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);

                    // Hated targets rather than a named man. Everybody our side hates is on
                    // that court and nobody else in the city is, so this picks a rival each
                    // and spreads them out -- where naming one would have all three of them
                    // taking turns on the same person while the other two stood watching.
                    Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, ped.Handle, 40f, 0);
                }
                catch { /* they will use their hands anyway */ }
            }

            Notify.Important("~r~It's on.~s~ " + Objective + ".");
        }

        // ---- helpers -----------------------------------------------------------

        /// <summary>
        /// Switches the wanted system off for the length of the job, and back on afterwards.
        ///
        /// This is a straightener on a basketball court, not a crime. Four men having a fight
        /// in a park should not put a helicopter over Chamberlain, and a mission that fails
        /// because a passing patrol saw a fist fight it was never meant to notice is a mission
        /// nobody can finish. Turned back on the moment the job ends, however it ends.
        /// </summary>
        private void HoldTheLaw(bool held)
        {
            // Through the shared switch. A gang war can start while this job is running -- they
            // roll on separate clocks and nothing stops them overlapping -- and whichever of the
            // two ended first used to turn the police back on for the other. Either a fist
            // fight on a basketball court brought a helicopter, or a raid on your own block did.
            //
            // The old SET_EVERYONE_IGNORE_PLAYER call that used to live here passed false on
            // both sides of the branch, so it never did anything in either direction.
            if (held) LawHold.Hold(this);
            else LawHold.Release(this);
        }

        private void Chatter()
        {
            if (Game.GameTime < _nextChatter) return;

            // EVERY NOW AND THEN, not metronomically. A fixed seven seconds is a man with a
            // timer in him; the spread is what makes it read as somebody occasionally saying
            // something.
            _nextChatter = Game.GameTime + ChatterGapMs + _rng.Next(ChatterSpreadMs);

            // Lamar first. He is the only one out here now, and a silent ride with the man
            // who invited you on it is worse than no chatter at all.
            var speaker = _lamar != null && _lamar.Exists() && _lamar.IsAlive
                ? _lamar
                : _homies.Count == 0 ? null : _homies[_rng.Next(_homies.Count)];

            if (speaker == null || !speaker.Exists() || !speaker.IsAlive) return;

            try
            {
                var lines = LinesForNow();

                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, speaker.Handle,
                              lines[_rng.Next(lines.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        /// <summary>True the frame the player actually fires, not merely holds something.</summary>
        private static bool PlayerFired(Ped player)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle);
            }
            catch
            {
                return false;
            }
        }

        private static bool Inside(Ped player)
        {
            try
            {
                return Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle) != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Settles a coordinate onto the ground, but only if the ground is roughly where the
        /// coordinate already said it was.
        ///
        /// Every authored position in this job was read off the player HUD while stood on the
        /// spot, so the height is already right. Probing downward from fifteen metres up in a
        /// courtyard finds the first thing it hits, which is a balcony -- and that is how the
        /// bike ended up on a roof. A probe that disagrees by more than a storey is wrong about
        /// a coordinate somebody measured by standing on it.
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

        private static float Flat(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static Ped Nearest(List<Ped> peds, Ped player)
        {
            Ped best = null;
            var bestRange = float.MaxValue;

            foreach (var ped in peds)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                var range = Flat(player.Position, ped.Position);
                if (range >= bestRange) continue;

                bestRange = range;
                best = ped;
            }

            return best;
        }

        /// <summary>Metallic dark green, the same index every other car of theirs uses.</summary>
        private const int BikeGreen = 49;

        private Vehicle SpawnBike(Vector3 where, float heading)
        {
            var spot = Ground(where);
            spot.Z += 0.4f;

            foreach (var name in BikeModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var bike = World.CreateVehicle(model, spot, heading);
                    model.MarkAsNoLongerNeeded();

                    if (bike == null || !bike.Exists()) continue;

                    bike.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, bike.Handle, true, true);

                    // The set's green, out of the game's own paint table rather than an RGB --
                    // the flake is in the table and a bicycle in flat green is a toy. Yours and
                    // his match, because they came out of the same yard.
                    try
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD_KIT, bike.Handle, 0);
                        Function.Call(Hash.SET_VEHICLE_LIVERY, bike.Handle, -1);
                        Function.Call(Hash.SET_VEHICLE_MOD, bike.Handle, 48, -1, false);

                        Function.Call(Hash.SET_VEHICLE_COLOURS, bike.Handle, BikeGreen, BikeGreen);
                        Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, bike.Handle, BikeGreen, 0);
                    }
                    catch { /* it rides the same in whatever colour it came in */ }

                    return bike;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Warn("No push bike model would load.");
            return null;
        }

        private Ped SpawnGangMember(GangDef gang, Vector3 near)
        {
            foreach (var name in gang.MemberModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var spot = World.GetNextPositionOnSidewalk(near);
                    if (spot == Vector3.Zero) spot = near;

                    // A ped created above the ground falls to it, which is the drop from the sky.
                    try
                    {
                        if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 15f),
                                                  out var groundZ, GetGroundHeightMode.Normal) && groundZ > 0f)
                        {
                            spot.Z = groundZ;
                        }
                    }
                    catch
                    {
                        // Keep what the pavement gave us.
                    }

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                    spot.X, spot.Y, spot.Z, 0f, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        private void Mark(Vector3 where, string name, BlipColor colour)
        {
            ClearMarker();

            try
            {
                _marker = World.CreateBlip(where);
                if (_marker == null || !_marker.Exists()) return;

                _marker.Color = colour;
                _marker.ShowRoute = true;
                _marker.Name = name;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the next leg: " + ex.Message);
            }
        }

        private void ClearMarker()
        {
            try { if (_marker != null && _marker.Exists()) _marker.Delete(); }
            catch { /* teardown */ }

            _marker = null;
        }

        private static void Release(Vehicle car)
        {
            try
            {
                if (car == null || !car.Exists()) return;
                car.MarkAsNoLongerNeeded();
            }
            catch { /* teardown */ }
        }

        // ---- finishing ---------------------------------------------------------
        // MaskUp lived here, and it only ever put a BEARD on him.
        //
        // Component 1 is the mask slot on a freemode ped and the beard slot on a story one,
        // and Franklin is a story ped -- so taking "the last valid drawable in the slot" took
        // the last beard. It could not have produced a mask on him whatever it asked for. The
        // game's own shop robberies do not involve one, so it is gone rather than patched.
        public void Clear()
        {
            // Whatever else happens in teardown, he does not keep the balaclava. This runs on
            // the job finishing, on failing it, and on the mod being switched off.

            // First, before anything else can go wrong in teardown. Leaving the player unable
            // to attract police for the rest of the session because a cleanup threw is far
            // worse than any of the litter below.
            if (IsRunning) HoldTheLaw(false);

            // And the ceiling goes back up, whoever ended the job and however. A cap left on is
            // a player who can never be wanted for anything again this session, which is a far
            // worse thing to leave behind than any amount of litter.
            try { LawHold.Uncap(); }
            catch { /* teardown */ }

            ClearMarker();

            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* teardown */ }
            }
            _blips.Clear();

            foreach (var ped in _rivals)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _rivals.Clear();

            foreach (var ped in _homies)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);

                    // Everybody else is handed back to the population and forgotten. Lamar is
                    // not ours to hand back -- releasing him would let the game clear him away
                    // and the corner he is supposed to be standing on would be empty until the
                    // area reloaded.
                    if (ped == _lamar) continue;

                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _homies.Clear();

            // And the man himself, back on his corner.
            // He is walking home under his own sequence by now, so the handback must not
            // clear it out from under him. Fixer.TakeBack puts him back on his mark or
            // despawns him if he has drifted -- neither is wanted while he is on his way
            // there, and both are fine once he arrives.
            if (_lamar != null)
            {
                _lamar = null;
                if (Boss != null) Boss.TakeBack();
            }

            // The bikes are left where they are rather than deleted. Four bikes vanishing off a
            // street the moment you get paid is the mod tidying up in front of you.
            foreach (var bike in _bikes) Release(bike);
            _bikes.Clear();
            _ride.Clear();

            Release(_playerBike);
            _playerBike = null;

            // The court sides go with the court.
            DropSides();

            _def = null;
            Phase = BikePhase.None;
            ReadyToCollect = false;
            Failure = null;
            _wentInside = false;
            _clerkDown = false;
            _shouted = false;
            _robOffered = false;
            _robAccepted = false;
            _gotCash = false;
            _lamarSlipSince = 0;
            _lamarWasAt = 0f;
            _lamarGoneSince = 0;
            _wordsStep = 0;
            _wordsAt = 0;
            _laughAt = 0;
            _thirstyAt = 0;

            // A clerk the game provided is the game's to keep. One WE stood up is ours, and
            // leaving him behind the counter after the job would put two men there next time.
            if (_madeClerk != null)
            {
                try { if (_madeClerk.Exists()) _madeClerk.Delete(); }
                catch { /* teardown */ }

                _madeClerk = null;
            }
        }
    }
}
