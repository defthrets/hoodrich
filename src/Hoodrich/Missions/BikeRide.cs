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

        /// <summary>Done it. Lose them.</summary>
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
        private static readonly Vector3 BikeSpot = new Vector3(-97.042f, -1610.761f, 32.313f);
        private const float BikeHeading = 56.429f;

        /// <summary>The courts in Chamberlain Hills.</summary>
        private static readonly Vector3 Courts = new Vector3(-227.173f, -1541.756f, 31.607f);

        /// <summary>The 24/7 down the road.</summary>
        private static readonly Vector3 Shop = new Vector3(29.028f, -1352.893f, 29.341f);

        /// <summary>
        /// Where Lamar's bike is, and which way it is pointing.
        ///
        /// A named spot rather than two metres from wherever he happens to be stood. He is
        /// borrowed off his corner and put straight onto it, the same way the homies are put
        /// straight onto theirs -- and it lands round the side, out of the courtyard, so three
        /// men and three bikes are not materialising in the space you are stood in.
        /// </summary>
        private static readonly Vector3 LamarBike = new Vector3(-105.510f, -1603.331f, 31.153f);
        private const float LamarBikeHeading = 345.805f;

        /// <summary>
        /// Where the other two are waiting, up the alley from Lamar's bike.
        ///
        /// Four metres from his, so the three of them read as one crew setting off rather than
        /// as people who happened to be in the same postcode.
        /// </summary>
        private static readonly Vector3 HomieSpot = new Vector3(-109.105f, -1598.494f, 31.091f);

        /// <summary>
        /// Where the ride ends.
        ///
        /// The alley behind the block rather than Fixer.Spot. The fixer stands where he stands
        /// for every other job; this one started in the alley and it finishes there, which is
        /// also where the bikes came from.
        /// </summary>
        private static readonly Vector3 RideHome = new Vector3(-107.822f, -1600.699f, 31.074f);

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
        private const float HomeRange = 25f;

        /// <summary>Further behind than this and they are put back on your wheel.</summary>
        private const float CatchUpRange = 90f;

        /// <summary>They are put on the court before you can see it, so nobody drops in.</summary>
        private const float PreSpawnRange = 170f;
        private const float CourtSpread = 5f;

        private const int UpdateIntervalMs = 400;

        /// <summary>Chatter on the ride out, so four men on bikes are not four silent men on bikes.</summary>
        private const int ChatterGapMs = 14000;

        /// <summary>How often the escort task is put back on anybody who has lost it.</summary>
        private const int RetaskGapMs = 2500;

        /// <summary>
        /// What they shout on the way out.
        ///
        /// Wound up rather than rude. They are riding out with him, not at him, so the insults
        /// come off -- whatever is being shouted is aimed at the people on the court, and the
        /// ones aimed at nobody in particular should sound like men enjoying themselves.
        /// </summary>
        private static readonly string[] RideLines =
        {
            "GENERIC_HOWS_IT_GOING", "GENERIC_YES", "CHASE_SUSPECT",
            "GENERIC_WAR_CRY", "CHAT_STATE"
        };

        private static readonly string[] BikeModels = { "bmx", "cruiser", "scorcher", "tribike" };

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
        private bool _talkHeld;
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
                        return _robAccepted
                            ? (_gotCash ? "Get out and get on the bike" : "Aim at the clerk until he empties the till")
                            : "Pull up outside the 24/7";

                    case BikePhase.Escape: return "Lose them";
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
            _shouted = false;
            _robOffered = false;
            _robAccepted = false;
            _gotCash = false;
            _heldAt = 0;
            _clerk = null;
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
            HoldTheLaw(true);
            Mark(BikeSpot, "Your bike", BlipColor.Yellow);

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
            HoldTheLaw(true);

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
            BringLamar(player);

            Phase = BikePhase.Riding;
            _nextChatter = Game.GameTime + ChatterGapMs;

            Mark(Courts, "The courts", BlipColor.Yellow);
            Notify.Ticker(_lamar != null && _lamar.Exists()
                ? "~g~Lamar rolled out with you.~s~"
                : "~g~The homies rolled out with you.~s~");
        }

        private void TickRiding(Ped player)
        {
            Chatter();
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

                _wordsAt = Game.GameTime + 3600;
                _wordsStep = 1;
                return;
            }

            if (Game.GameTime < _wordsAt) return;

            if (_wordsStep == 1)
            {
                Says("~g~FRANKLIN", "Say that again.", 1800);

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
        private void Says(string who, string line, int ms)
        {
            if (string.IsNullOrEmpty(line)) return;

            try { GTA.UI.Screen.ShowSubtitle(who + ":~s~ " + line, ms); }
            catch { /* the objective still says what to do */ }
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

            _wentInside = false;
            _shouted = false;
            _robOffered = false;
            _robAccepted = false;
            _gotCash = false;
            _clerk = null;

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

                GTA.UI.Screen.ShowSubtitle("~y~LAMAR:~s~ AYO! FRANKLIN HOL UP!", 4500);
                _weaselAt = Game.GameTime + 4600;
            }

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
                if (_shouted && range <= ShopRange && Stopped(player)) OfferWhenAsked(player);

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
        private void OfferWhenAsked(Ped player)
        {
            if (Talk == null || Talk.IsOpen || _robOffered) return;
            if (_lamar == null || !_lamar.Exists() || !_lamar.IsAlive) return;

            if (Flat(player.Position, _lamar.Position) > TalkRange) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to see what he wants.");

            if (!Pressed()) return;

            _robOffered = true;
            Talk.Open(TheOffer(), this);
        }

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
                "sittin' on a counter and you the one with the steady hands.")
            {
                SpeakerColour = Palette.Cash
            };

            node.Say("ROB THE CONVENIENCE STORE", () =>
            {
                _robAccepted = true;

                Notify.Important("~r~In you go.~s~ Aim at the man behind the counter and hold " +
                                 "it on him until he empties the till.");

                LamarSays("I'm right here on the bike. Go on.");
                return null;
            }, "Walk in, aim at the clerk, hold it on him -- somebody will call it in");

            node.WithIcon(Icons.FromFile("cash.png"));

            node.Say("Nah. We said hands, we did hands.", () =>
            {
                // Taken as done. The shop is finished with either way, and the ride home is
                // the same ride home -- he just complains the whole way.
                _gotCash = true;
                LamarSays("Man... aight. AIGHT. Then I'm startin' that grow with lint.");
                return null;
            }, "Ride back with what you came for");

            node.WithIcon(Icons.FromFile("car.png"));

            return node;
        }

        /// <summary>
        /// The shop floor.
        ///
        /// Held rather than pressed. Waving a gun once and getting paid is a button; keeping it
        /// on a frightened man while he empties a till is the thing the scene is about.
        /// </summary>
        private void Robbery(Ped player)
        {
            if (!Inside(player))
            {
                _heldAt = 0;
                return;
            }

            if (!_wentInside)
            {
                _wentInside = true;
                Notify.Ticker("~r~Aim at him.~s~ Hold it on him.");
            }

            // And it stays on screen for as long as you are in there.
            //
            // The ticker above is one shot: look at the shelves for ten seconds and the only
            // instruction the robbery ever gave you has gone, leaving a man stood in a shop
            // with no idea what the game wants. This is the standing instruction, and it
            // changes to "keep it on him" once the gun is up.
            if (!_gotCash) Help.ShowThisFrame("Aim at the man behind the counter.");

            if (_clerk == null || !_clerk.Exists() || !_clerk.IsAlive) _clerk = FindClerk(player);

            // Nobody there. The game staffs its own shops and mostly does, but "mostly" is not
            // a thing a mission can be built on: turn up on the wrong night and the robbery is
            // a man stood in an empty shop with an instruction telling him to aim at somebody.
            //
            // Given a moment first, because the shop's own peds stream in a beat behind the
            // room does -- spawning instantly would put two clerks behind one counter.
            if (_clerk == null)
            {
                if (_clerkDue == 0) _clerkDue = Game.GameTime + ClerkGraceMs;
                if (Game.GameTime < _clerkDue) return;

                _clerk = MakeClerk(player);
                _clerkDue = 0;
            }

            if (_clerk == null) return;

            var onHim = Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING_AT_ENTITY,
                                            Game.Player.Handle, _clerk.Handle);

            if (!onHim)
            {
                if (_heldAt != 0)
                {
                    _heldAt = 0;
                    Notify.Problem("keep it on him.");
                }

                return;
            }

            if (_heldAt == 0)
            {
                _heldAt = Game.GameTime;

                try
                {
                    // Hands up, and he stays put. Without the block he runs the moment the
                    // first shot goes off anywhere in the neighbourhood.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _clerk.Handle, true);
                    Function.Call(Hash.TASK_HANDS_UP, _clerk.Handle, HoldMs + 2000, 0, -1, false);
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, _clerk.Handle,
                                  "GENERIC_FRIGHTENED_HIGH", "SPEECH_PARAMS_FORCE");
                }
                catch { /* he can be frightened silently */ }

                return;
            }

            var held = Game.GameTime - _heldAt;
            if (held < HoldMs)
            {
                Help.ShowThisFrame("Keep it on him.");
                return;
            }

            _gotCash = true;

            var take = _rng.Next(TillMin, TillMax);
            Game.Player.Money += take;

            Notify.Important("~g~+$" + take.ToString("N0") + "~s~ out the till.");
            LamarSays("THAT'S what I'm talkin' about! Go, go, go!");

            Social?.On(Hoodrich.Social.SocialEvent.StoreRobbed, "");

            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _clerk.Handle, false);
            }
            catch { /* he is free either way */ }

            // The police come back on FIRST, and this is the whole fix.
            //
            // The job holds the wanted system off for its whole length, for a good reason: a
            // fist fight on a basketball court should not put a helicopter over Chamberlain.
            // But the hold sets the maximum wanted level to zero, so the star this line asks
            // for was being asked for against a ceiling of none -- set, and gone in the same
            // frame. There has been no heat on the ride home since the robbery was written.
            //
            // Robbing a shop is exactly the moment that reasoning stops applying. Somebody
            // presses the button under the counter, and from here it is meant to be a chase.
            HoldTheLaw(false);

            try
            {
                Game.Player.Wanted.SetWantedLevel(RobberyStars, false);
                Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
            }
            catch { /* then it is a very easy escape */ }
        }

        /// <summary>How long the game gets to staff its own shop before we do it for it.</summary>
        private const int ClerkGraceMs = 1500;

        /// <summary>When that grace runs out, or 0 if it has not started.</summary>
        private int _clerkDue;

        /// <summary>Who serves in a shop the game did not staff.</summary>
        private static readonly string[] ClerkModels =
        {
            "mp_m_shopkeep_01", "s_m_y_shop_mask", "a_m_m_indian_01", "a_m_y_business_01"
        };

        /// <summary>
        /// Puts somebody behind the counter.
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
        private Ped MakeClerk(Ped player)
        {
            var where = player.Position;
            var facing = player.Heading;
            var found = false;

            foreach (var till in new[] { "prop_till_01", "prop_till_02", "prop_till_03",
                                         "v_ret_gc_till", "prop_cash_register_01" })
            {
                try
                {
                    var handle = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,
                                                    where.X, where.Y, where.Z, 12f,
                                                    Function.Call<int>(Hash.GET_HASH_KEY, till),
                                                    false, false, false);

                    var prop = Entity.FromHandle(handle);
                    if (prop == null || !prop.Exists()) continue;

                    var at = prop.Position;
                    var away = prop.ForwardVector * 0.85f;

                    var behind = Behind(at + away) ? at + away : at - away;

                    where = behind;
                    facing = (float)(Math.Atan2(at.Y - behind.Y, at.X - behind.X) * 180.0 / Math.PI) - 90f;
                    found = true;
                    break;
                }
                catch
                {
                    // Try the next till model.
                }
            }

            if (!found)
            {
                Log.Warn("No till in the 24/7 to stand a clerk behind; using the doorway instead.");
            }

            foreach (var name in ClerkModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED, 4, model.Hash,
                                                    where.X, where.Y, where.Z, facing, false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var man = Entity.FromHandle(handle) as Ped;
                    if (man == null || !man.Exists()) continue;

                    man.IsPersistent = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, man.Handle, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, man.Handle, false);
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, man.Handle,
                                  "WORLD_HUMAN_STAND_IMPATIENT", 0, true);

                    _madeClerk = man;

                    Log.Info("Staffed the 24/7 with a " + name + " at " + where + ".");
                    return man;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not stand a clerk up: " + ex.Message);
                }
            }

            return null;
        }

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

        /// <summary>
        /// Whoever is behind the counter.
        ///
        /// Nearest ped in the shop who is not you and not one of ours. The 24/7 has exactly one
        /// person working in it, so nearest-inside is the right answer rather than a guess.
        /// </summary>
        private Ped FindClerk(Ped player)
        {
            try
            {
                Ped best = null;
                var bestAt = float.MaxValue;

                foreach (var ped in World.GetNearbyPeds(player, 14f))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;
                    if (_homies.Contains(ped)) continue;

                    var at = player.Position.DistanceTo(ped.Position);
                    if (at >= bestAt) continue;

                    best = ped;
                    bestAt = at;
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Out the door, on the bike, and away from them.</summary>
        private void TickEscape(Ped player)
        {
            RemountHomies(player);

            // Still hot. Getting back to the spot with a helicopter over you is not getting
            // away with it, so the job does not end until they have lost you.
            LamarStaysOut();

            if (Game.Player.Wanted.WantedLevel > 0)
            {
                Help.ShowThisFrame("Lose them, then get back to the spot.");
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
        /// A scenario rather than an animation, which matters here: an anim dictionary is not
        /// loaded in the frame it is asked for, so playing a clip the moment the last man goes
        /// down means playing nothing at all. WORLD_HUMAN_CHEERING is already in the game and
        /// starts on the frame it is given.
        ///
        /// He is not held there. If you get straight on the bike, RemountHomies puts him on
        /// his -- somebody choosing to ride off IS the end of the moment, and freezing the
        /// mission for three seconds to make sure a celebration is seen would be worse than
        /// missing it.
        /// </summary>
        private void Cheer()
        {
            _laughAt = Game.GameTime + 1700;
            _thirstyAt = Game.GameTime + 3400;

            Says("~y~LAMAR", "AYYY! You seen that?! That's what I'm talkin' 'bout!", 3000);

            if (_lamar == null || !_lamar.Exists() || !_lamar.IsAlive) return;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _lamar.Handle);
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _lamar.Handle,
                              "WORLD_HUMAN_CHEERING", 0, true);
            }
            catch
            {
                // He will stand there pleased with himself instead.
            }

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
        }

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

        /// <summary>How long the gun stays on him before he gives it up.</summary>
        private const int HoldMs = 4000;

        private const int TillMin = 900;
        private const int TillMax = 2600;

        /// <summary>Somebody presses the button under the counter. One, and you are on a bike.</summary>
        private const int RobberyStars = 1;

        /// <summary>When his second line is due, so it does not land on top of the shout.</summary>
        private int _weaselAt;

        private bool _shouted;
        private bool _robOffered;
        private bool _robAccepted;
        private bool _gotCash;
        private int _heldAt;
        private Ped _clerk;

        private void TickHome(Ped player)
        {
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
        private static readonly Vector3 LamarHome = new Vector3(-94.579f, -1609.991f, 32.314f);

        /// <summary>
        /// How close he has to be. Wider than his own mark, because he arrives on a bicycle
        /// and parks it wherever the nav mesh lets him rather than on a spot.
        /// </summary>
        private const float LamarHomeRange = 14f;

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

                try
                {
                    ped.SetIntoVehicle(bike, VehicleSeat.Driver);

                    MakeSides();
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle,
                                  _usGroup != 0 ? _usGroup : gang.GroupHash);

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
        /// Puts the man who gave you the job on a bike.
        ///
        /// He is borrowed off the Fixer rather than spawned, so it is the same man you were
        /// stood in front of a moment ago. Spawning a second Lamar while the first one watches
        /// from the kerb is the kind of thing that cannot be unseen.
        ///
        /// He goes into _homies with the rest, which is deliberate: every piece of behaviour
        /// this mission has for a homie -- keeping up, remounting, being called up at the
        /// courts, fighting -- then applies to him for free. What does NOT apply is the
        /// teardown, which is handled where it happens.
        /// </summary>
        private void BringLamar(Ped player)
        {
            if (Boss == null || _lamar != null) return;

            var gang = _crew.Current;
            if (gang == null) return;

            var ped = Boss.Lend();
            if (ped == null || !ped.Exists()) return;

            var bike = SpawnBike(LamarBike, LamarBikeHeading);
            if (bike == null)
            {
                // No bike, so no ride. Give him straight back rather than leaving him stood
                // in the road with his tasks cleared.
                Boss.TakeBack();
                return;
            }

            _lamar = ped;
            _homies.Add(ped);
            _bikes.Add(bike);

            try
            {
                ped.SetIntoVehicle(bike, VehicleSeat.Driver);

                MakeSides();
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle,
                              _usGroup != 0 ? _usGroup : gang.GroupHash);

                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                // 46 is BF_CanFightArmedPedsWhenNotArmed, NOT BF_AlwaysFight. That is 5.

                // He keeps whatever he had. The others are stripped because a random gang
                // member with a rifle on a bicycle reads as a spawn; the man who runs the set
                // being armed reads as the man who runs the set.
                Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);

                // Killable now, where he is normally not. A bodyguard who cannot be shot is
                // not a bodyguard, and the mission already fails properly if he goes down.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                Enlist(ped, player);
                Escort(ped, bike, player);

                var blip = ped.AddBlip();
                if (blip != null && blip.Exists())
                {
                    blip.Color = BlipColor.Green;
                    blip.Scale = 0.8f;
                    blip.Name = "Lamar";
                    _blips.Add(blip);
                }

                Log.Info("Lamar is riding this one.");
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
                    Function.Call(Hash.TASK_VEHICLE_FOLLOW, ped.Handle, bike.Handle, target.Handle,
                                  25f, FollowStyle, 8);
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

            for (var i = 0; i < _homies.Count; i++)
            {
                var ped = _homies[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (i >= _bikes.Count) continue;

                var bike = _bikes[i];
                if (bike == null || !bike.Exists()) continue;

                if (!ped.IsInVehicle(bike))
                {
                    try
                    {
                        Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, bike.Handle,
                                      -1, (int)VehicleSeat.Driver, 2f, 1, 0);
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

                if (bike == null || !bike.Exists())
                {
                    bike = SpawnBike(behind, player.Heading);

                    if (bike != null && bike.Exists())
                    {
                        _bikes.Add(bike);
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
        private const float LamarLeash = 20f;

        /// <summary>How long he gets to close it before he is simply moved.</summary>
        private const int LamarStuckMs = 4000;

        /// <summary>Far enough that he is gone rather than slow, and the clock is not waited on.</summary>
        private const float LamarLost = 120f;

        private int _lamarSlipSince;
        private float _lamarWasAt;

        private void RemountHomies(Ped player)
        {
            if (!player.IsInVehicle()) return;

            for (var i = 0; i < _homies.Count; i++)
            {
                var ped = _homies[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (ped.IsInVehicle()) continue;

                if (i >= _bikes.Count) continue;

                var bike = _bikes[i];
                if (bike == null || !bike.Exists()) continue;

                try
                {
                    Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, bike.Handle,
                                  -1, (int)VehicleSeat.Driver, 2f, 1, 0);
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
            _nextChatter = Game.GameTime + ChatterGapMs;

            // Lamar first. He is the only one out here now, and a silent ride with the man
            // who invited you on it is worse than no chatter at all.
            var speaker = _lamar != null && _lamar.Exists() && _lamar.IsAlive
                ? _lamar
                : _homies.Count == 0 ? null : _homies[_rng.Next(_homies.Count)];

            if (speaker == null || !speaker.Exists() || !speaker.IsAlive) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, speaker.Handle,
                              RideLines[_rng.Next(RideLines.Length)], "SPEECH_PARAMS_FORCE");
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

        private bool Pressed()
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

        public void Clear()
        {
            // First, before anything else can go wrong in teardown. Leaving the player unable
            // to attract police for the rest of the session because a cleanup threw is far
            // worse than any of the litter below.
            if (IsRunning) HoldTheLaw(false);

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

            Release(_playerBike);
            _playerBike = null;

            // The court sides go with the court.
            DropSides();

            _def = null;
            Phase = BikePhase.None;
            ReadyToCollect = false;
            Failure = null;
            _wentInside = false;
            _shouted = false;
            _robOffered = false;
            _robAccepted = false;
            _gotCash = false;
            _heldAt = 0;
            _clerk = null;
            _lamarSlipSince = 0;
            _lamarWasAt = 0f;
            _lamarGoneSince = 0;
            _wordsStep = 0;
            _wordsAt = 0;
            _laughAt = 0;
            _thirstyAt = 0;
            _clerkDue = 0;

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
