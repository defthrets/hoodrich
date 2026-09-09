using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>Where an order is up to.</summary>
    internal enum FoodState
    {
        None = 0,

        /// <summary>Paid for, and somebody is riding it over.</summary>
        Coming,

        /// <summary>Parked up, off the moped, walking it to you with the bag in his hand.</summary>
        Walking,

        /// <summary>Stood in front of you, handing it over.</summary>
        Handing,

        /// <summary>Done. Back on the moped and away.</summary>
        Leaving
    }

    /// <summary>
    /// LUber brings you food.
    ///
    /// THE OTHER HALF OF THE APP, and it is the same company: the cars drive themselves and
    /// the food does not, which is the joke the whole thing runs on. You order off the menu,
    /// somebody real gets on a moped somewhere across the map, rides to wherever you are
    /// stood, gets off, walks over with the bag in his hand and puts it in yours.
    ///
    /// IT IS BARE MINIMUM'S FOOD, NOT OURS. The menu, the prices, the names and the pictures
    /// all come over the bridge (Core.Pantry) and what arrives goes into that mod's pockets.
    /// Without it installed there is no menu and the app never offers the row -- which is
    /// right: this mod has no food of its own and inventing some so the button worked would
    /// be two mods disagreeing about what a sandwich is.
    ///
    /// THE RIDE IS THE POINT. Nothing here teleports and nothing appears at your feet: he is
    /// made a hundred metres away or more, on a road, and he has to get to you. If he cannot
    /// -- traffic, a wall, you running off across a field -- the order times out and you get
    /// your money back. A delivery that always arrives is a menu with a delay on it.
    ///
    /// It is deliberately one at a time. A queue of riders converging on one man is a bit,
    /// not a feature.
    /// </summary>
    internal sealed class LuberFood
    {
        /// <summary>What it costs to send somebody, on top of what the food costs.</summary>
        private const int Fee = 30;

        /// <summary>Far enough that he rides to you rather than appearing at the kerb.</summary>
        private const float ComeFromMin = 110f;
        private const float ComeFromMax = 220f;

        /// <summary>Close enough to you to stop the moped and get off it.</summary>
        private const float ParkRange = 16f;

        /// <summary>Close enough on foot to hand it over.</summary>
        private const float HandRange = 1.9f;

        /// <summary>How far he will chase you on foot before he gives up and rides off.</summary>
        private const float LostRange = 70f;

        /// <summary>How long each leg is given before the order is written off.</summary>
        private const int RideCapMs = 240000;
        private const int WalkCapMs = 90000;
        private const int HandMs = 1400;
        private const int LeaveCapMs = 60000;

        /// <summary>Once he is away and this far off, he stops existing.</summary>
        private const float GoneRange = 120f;

        /// <summary>How often he is re-aimed at you while he rides. You move; the route does not.</summary>
        private const int ReaimMs = 4000;

        /// <summary>
        /// How he rides.
        ///
        /// The same eight flags the cars use -- see Luber.Style -- because the reasoning is
        /// identical and a moped that stops dead behind a parked van is the same failure.
        /// </summary>
        private const int Style = 1 | 2 | 4 | 8 | 16 | 32 | 128 | 256;

        private const float RideSpeed = 19f;

        /// <summary>PH_R_Hand: the prop helper, where a hand actually holds a thing. See Core.Handset.</summary>
        private const int RightHandBone = 28422;

        /// <summary>The moped, in the order an install has them.</summary>
        /// <summary>
        /// What he turns up in.
        ///
        /// NOT ALWAYS A MOPED ANY MORE. Everybody doing this job on the same scooter is a
        /// FLEET, and a fleet is something a company buys -- these are people using their own
        /// vehicles, which is the whole business model, and it is why the same order twice
        /// should not look the same twice.
        ///
        /// The Chavos is painted white and the white is not decoration: a plain white sedan
        /// with nothing on it is the most anonymous car on the road and is exactly what this
        /// job gets done in. The Asterope GZ and the Asbo are left completely stock -- one is
        /// the dullest saloon on the road and the other is the smallest car in the game, and
        /// somebody is delivering your dinner in it. The Pizza Boy already has the box on the
        /// back, so it wants nothing doing to it at all.
        ///
        /// ONE ROLL, NOT A LIST WALKED IN ORDER. Walking it top-down would mean every delivery
        /// on an install that has the Chavos came in the Chavos. One is picked at random and
        /// the rest of the list is only what it falls back to when that model is not in the
        /// build -- which matters, because the first is an add-on and the last two are the old
        /// mopeds that every install has.
        /// </summary>
        private static readonly Wheels[] Rides =
        {
            new Wheels { Model = "chavosv6", White = true },
            new Wheels { Model = "asterope2" },
            new Wheels { Model = "asbo" },
            new Wheels { Model = "pizzaboy", Bike = true },
            new Wheels { Model = "faggio2", Bike = true },
            new Wheels { Model = "esskey", Bike = true }
        };

        /// <summary>One of those: what it is, whether it gets painted, and whether he wears a lid.</summary>
        private sealed class Wheels
        {
            public string Model = "";
            public bool White;
            public bool Bike;
        }

        /// <summary>
        /// Who rides it.
        ///
        /// THE UNIFORM WAS TRADED FOR A PERSON. This was the postal and courier peds, on the
        /// reasoning that a man in a uniform reads as working before he has done anything --
        /// which is true, and which stopped mattering the moment he started texting you by
        /// name. A courier ped is a costume with nobody in it; the delivery is now somebody
        /// you have had a conversation with, and he should look like the man whose name is at
        /// the top of the thread.
        ///
        /// The helmet stays, which is what actually says moped-and-a-job at a glance.
        ///
        /// The couriers are still on the end of the list. They are what an install without the
        /// first two falls back to, and a delivery from a postman beats no delivery.
        /// </summary>
        private static readonly string[] Riders =
        {
            "a_m_y_indian_01", "a_m_m_indian_01",
            "s_m_m_postal_02", "s_m_m_postal_01", "s_m_m_ups_01", "s_m_m_ups_02"
        };

        /// <summary>
        /// What he is called.
        ///
        /// A NAME PER ORDER, from the two halves. Every delivery in this city is a different
        /// man and the thread in your phone should say so -- the same name twice in a night
        /// reads as one driver on a loop, which is worse than no name at all.
        ///
        /// Both lists lean Punjabi and Gujarati because that is who does this job, in this
        /// city, in the year this game is set in. It is a delivery driver, not a joke.
        /// </summary>
        private static readonly string[] Firsts =
        {
            "Ravi", "Sunil", "Amit", "Deepak", "Vikram", "Arjun", "Rajesh", "Manoj",
            "Naveen", "Rohit", "Anil", "Dinesh", "Ajay", "Kiran", "Prakash", "Sanjay",
            "Harpreet", "Gurpreet", "Jaspreet", "Baldev", "Satnam", "Sukhwinder",
            "Nikhil", "Pranav", "Imran", "Karan", "Vijay", "Suresh", "Rakesh", "Yusuf"
        };

        private static readonly string[] Lasts =
        {
            "Patel", "Sharma", "Singh", "Kumar", "Reddy", "Gupta", "Desai", "Iyer",
            "Nair", "Menon", "Rao", "Joshi", "Mehta", "Malhotra", "Kapoor", "Sethi",
            "Dhillon", "Grewal", "Sandhu", "Bains", "Chaudhry", "Bhatt", "Shah",
            "Verma", "Trivedi", "Pillai", "Khatri", "Sodhi", "Ahluwalia", "Chopra"
        };

        /// <summary>The bag in his hand, in the order an install has them.</summary>
        private static readonly string[] Bags =
        {
            "prop_food_bs_bag_01", "prop_carrier_bag_01", "prop_paper_bag_01"
        };

        /// <summary>
        /// What he texts you, in his own words.
        ///
        /// {0} is what you ordered and {1} is what it cost. He is not a support desk and he
        /// does not write like one -- the lines are short, lower case and slightly harried,
        /// because he is on a moped in traffic and has four more of these after yours.
        /// </summary>
        private static readonly string[] SaidComing =
        {
            "on my way with your {0}. give me a few minutes",
            "got your {0}. traffic's bad but i'm coming",
            "picked up your {0}, ${1} paid. see you shortly",
            "hi it's your LUber driver. {0} is with me, on my way",
            "your {0} is in the box. ten minutes maybe less",
            "just left with the {0}. stay where you are if you can",
            "on the road with your {0}. don't move about too much please"
        };

        /// <summary>
        /// The running commentary.
        ///
        /// A DELIVERY IS A CONVERSATION, NOT A RECEIPT. One message at the order and one at the
        /// door is a tracking page with a face on it; what makes it a person is the two minutes
        /// in the middle where he is having a bad time in traffic and telling you about it.
        ///
        /// A FEW OF THEM AT MOST, and sometimes none at all. Every order coming with the same
        /// three updates is a script; a driver who says nothing on one run and four things on
        /// the next is a driver. See ChatMost.
        /// </summary>
        private static readonly string[] SaidEnRoute =
        {
            "sir i am lost. help",
            "sir the map is telling me to drive into the sea",
            "sir i have spilt your drink. only a little bit",
            "sir there is a man doing donuts in the road. i am waiting",
            "sir which house is yours. they all look the same",
            "sir i went the wrong way at the lights. one moment",
            "sir do not worry the bag is fine",
            "sir the traffic here is unbelievable",
            "sir a seagull has taken some of it. i chased him off",
            "sir there is a police roadblock. going around",
            "sir do you have a dog. i am asking for a reason",
            "sir i have your order. i also have a flat tyre",
            "sir five minutes. maybe six",
            "sir your food is safe. i am not",
            "sir somebody is shooting. i will wait here a moment",
            "sir the box fell off. i caught it",
            "sir please stay still. you keep moving",
            "sir i can see the beach. is that near you",
            "sir a man has asked me for a lift. i said no sir",
            "sir the moped only does 30. i am sorry",
            "sir i am behind a bus. i am always behind a bus",
            "sir this hill is too much for the moped. walking a bit",
            "sir there is a helicopter following me. is that for you",
            "sir do not read the reviews of this restaurant",
            "sir i have taken a shortcut. it was not a shortcut"
        };

        /// <summary>And the ask, once he is gone.</summary>
        private static readonly string[] SaidTip =
        {
            "sir tip please",
            "sir a small tip would be nice. no pressure sir",
            "sir my rating is 3.1. you can fix this",
            "sir five stars and a tip and i am a happy man",
            "sir i drove very fast for you",
            "sir the app takes most of it. just so you know",
            "sir tip please. i will not ask again. probably",
            "sir if the drink was short that was the wind",
            "sir please do not mention the seagull in the review",
            "sir it was a pleasure. tip"
        };

        private static readonly string[] SaidHere =
        {
            "ok sir i am here",
            "i'm outside. can you see me",
            "here now. the moped with the box",
            "sir i am outside. i can see a bin and a wall",
            "here sir. please come out",
            "i'm here boss. come out when you're ready",
            "pulled up. i've got it in my hand",
            "outside. i can't stop long",
            "i'm at you. wave if you see me"
        };

        private static readonly string[] SaidDone =
        {
            "there you go. enjoy it",
            "that's you. five stars if you don't mind",
            "done. tell your friends about LUber",
            "all yours. i'm off to the next one",
            "enjoy boss. have a good night",
            "that's yours. any problem, don't message me, message the app"
        };

        private static readonly string[] SaidNoRoom =
        {
            "you've got nowhere to put it. i've sent the ${1} back",
            "your pockets are full mate. money's going back, sorry",
            "can't hand it over, you're carrying too much. ${1} refunded"
        };

        private const string GiveDict = "mp_common";
        private const string GiveClip = "givetake1_a";

        /// <summary>Set by Main: take the money. False when he cannot cover it.</summary>
        public Func<int, bool> Charge;

        /// <summary>Set by Main: hand it back when the order does not arrive.</summary>
        public Action<int> Refund;

        /// <summary>Set by Main: off while something louder is happening.</summary>
        public Func<bool> Busy;

        /// <summary>Set by Main: he has just been handed something, for the feed.</summary>
        public Action<string> Arrived;

        public FoodState State { get; private set; }

        public bool IsRunning => State != FoodState.None;

        /// <summary>What is on its way, for the app to say so.</summary>
        public string What { get; private set; } = "";

        private readonly Random _rng = new Random();

        private Vehicle _bike;
        private Ped _rider;
        private Prop _bag;

        /// <summary>What he came in, which decides the paint and the helmet.</summary>
        private Wheels _ride;

        /// <summary>When he next has something to say on the way, and how many he has left.</summary>
        private int _chatAt;
        private int _chatLeft;

        /// <summary>When he asks for his tip, and who to send it as once the order is cleaned up.</summary>
        private int _tipAt;
        private string _tipWho = "";
        private string _tipFace = "";

        /// <summary>His name this order, the key his photograph is filed under, and when he texts.</summary>
        private string _driver = "";
        private string _faceKey = "";
        private int _saysAt;

        /// <summary>
        /// His photograph, or nothing yet.
        ///
        /// FILED PER MODEL RATHER THAN PER ORDER. The headshot factory caches by key and never
        /// re-photographs one it already has, so a fresh key every delivery would fill the
        /// table with pictures of the same two men. The NAME changes every order; the face
        /// only has to match the ped that turned up, and there are two of those.
        /// </summary>
        private string Face
        {
            get
            {
                if (string.IsNullOrEmpty(_faceKey)) return null;

                UI.Headshots.WantAs(_faceKey, _faceKey.Substring(6));

                return UI.Headshots.Txd(_faceKey);
            }
        }
        private Blip _blip;

        private string _id = "";
        private int _paid;

        private int _phaseFrom;
        private int _reaimAt;
        private int _handFrom;

        // ---- ordering -----------------------------------------------------------

        /// <summary>
        /// What one costs delivered: the counter price plus the fee. Below nought when it is
        /// not something LUber can bring.
        /// </summary>
        public static int Quote(string id)
        {
            if (string.IsNullOrEmpty(id) || !Pantry.Present) return -1;

            var price = Pantry.PriceOf(id);
            if (price <= 0) return -1;

            return price + Fee;
        }

        /// <summary>
        /// Orders one. Returns a refusal to put in front of the player, or null once somebody
        /// is on the way.
        /// </summary>
        public string Order(string id)
        {
            if (IsRunning) return "You've already got one coming.";
            if (!Pantry.Present) return "Nothing to order.";

            if (Busy != null && Busy()) return "Not right now.";

            var price = Quote(id);
            if (price < 0) return "They don't do that.";

            var player = Game.Player.Character;

            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            // ON FOOT. The handover is a man walking up to you and putting a bag in your hand,
            // and there is no version of that through a car window worth writing.
            if (player.IsInVehicle()) return "Get out of the car first.";

            // ASKED BEFORE THE MONEY. A full pocket is the one refusal that would otherwise
            // cost you the fare and the ride to find out.
            if (Pantry.Slots > 0 && Pantry.Total >= Pantry.Slots) return "Your pockets are full.";

            var start = Somewhere(player.Position);
            if (start == Vector3.Zero) return "Nobody free near you.";

            if (Charge != null && !Charge(price)) return "You can't cover that.";

            if (!Make(start))
            {
                if (Refund != null) Refund(price);
                return "Nobody free near you.";
            }

            Luber.Theme();

            _id = id;
            _paid = price;
            What = Pantry.NameOf(id);

            // BEFORE ANYTHING CAN TEXT YOU. Says refuses to send from a man with no name, so
            // this has to happen above the first line rather than beside it.
            Named();

            Ride(player.Position);
            Mark();
            Begin(FoodState.Coming);

            // A BEAT BEFORE HE TEXTS, and it is doing two jobs. A driver who has this second
            // to read the order and answer is a driver; one who answers on the same frame you
            // pressed the button is a vending machine. And his photograph is rendered by the
            // headshot factory a frame or two after it is asked for -- so a message sent on
            // this frame would go out wearing the default silhouette, which is the LS Customs
            // badge on this build, which is how a taxi firm ended up signing for burgers.
            _saysAt = Game.GameTime + SayGapMs;

            // NONE, ONE, TWO OR THREE. Rolled per order, so some deliveries arrive in silence.
            _chatAt = 0;
            _chatLeft = _rng.Next(ChatMost + 1);

            Log.Info("LUber: " + What + " ordered for $" + price + ", driving it: " + _driver + ".");

            return null;
        }

        /// <summary>Called off the app, or by anything that has to stop it. Money back.</summary>
        public void Cancel(string why)
        {
            if (!IsRunning) return;

            var back = State == FoodState.Leaving ? 0 : _paid;

            Away();
            Clean();
            State = FoodState.None;
            What = "";

            if (back > 0 && Refund != null) Refund(back);

            if (!string.IsNullOrEmpty(why)) Notify.Failure(why);
        }

        // ---- the tick -----------------------------------------------------------

        public void Update(Ped player)
        {
            // HE TEXTS AFTER HE HAS GONE, which is why this is above the line that gives up on
            // an order that is over. Everything about the delivery has been cleaned up by then --
            // his name and his photograph included -- so the tip carries its own copy of both.
            if (_tipAt != 0 && Game.GameTime >= _tipAt)
            {
                _tipAt = 0;
                Tip();
            }

            if (State == FoodState.None) return;

            try
            {
                var now = Game.GameTime;

                // He has read the order and got back to you. See SayGapMs.
                //
                // AND NOT UNTIL HIS PHOTOGRAPH EXISTS, up to a point. The factory renders one
                // of these in its own time and the game's feed can only be handed a picture
                // that is ready THIS frame -- so a line sent too early goes out under the
                // default silhouette for ever, since the card is drawn once and never redrawn.
                // Waiting a few seconds costs nothing. Waiting for ever would cost the
                // message, so it goes either way in the end.
                // Asked of the GAME rather than of the table: Face answers with a name the
                // moment one has been rendered, and a name is not a picture -- the factory can
                // have let it go again. Ready is the question the feed actually cares about.
                var got = !string.IsNullOrEmpty(Face) && UI.Headshots.Ready(_faceKey);

                if (_saysAt != 0 && now >= _saysAt && (got || now - _saysAt > FaceWaitMs))
                {
                    _saysAt = 0;
                    Says(SaidComing);

                    // The first update is measured from the moment he answered rather than from
                    // the order, so a slow one does not stack two texts on top of each other.
                    _chatAt = now + ChatMinMs + _rng.Next(ChatSpanMs);
                }

                // AND THEN HE KEEPS TALKING, up to a point. Only while he is still on the road:
                // once he is off the moped and walking at you the next thing he says is that he
                // is outside, and an "i am lost" arriving after that reads as a bug.
                if (_chatAt != 0 && now >= _chatAt && State == FoodState.Coming)
                {
                    _chatAt = now + ChatMinMs + _rng.Next(ChatSpanMs);

                    if (_chatLeft > 0)
                    {
                        _chatLeft--;
                        Says(SaidEnRoute);
                    }
                }

                if (player == null || !player.Exists() || !player.IsAlive)
                {
                    Cancel(null);
                    return;
                }

                // DEAD RIDER, DEAD ORDER. Not a refusal he did anything about, so the money
                // goes back without a word about whose fault it was.
                if (_rider == null || !_rider.Exists() || !_rider.IsAlive)
                {
                    Log.Info("LUber: the rider never made it. Refunded.");
                    Cancel("your delivery never turned up. Money back.");
                    return;
                }

                switch (State)
                {
                    case FoodState.Coming: Coming(player, now); break;
                    case FoodState.Walking: Walking(player, now); break;
                    case FoodState.Handing: Handing(player, now); break;
                    case FoodState.Leaving: Leaving(now); break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("LUber food failed: " + ex.Message);
                Cancel(null);
            }
        }

        /// <summary>
        /// Riding it over.
        ///
        /// RE-AIMED WHILE HE RIDES, because you are not stood still. A route handed out once
        /// is a route to where you were when you pressed the button, which on foot is a
        /// street away by the time he arrives and in a car is another postcode.
        /// </summary>
        private void Coming(Ped player, int now)
        {
            if (_bike == null || !_bike.Exists())
            {
                Log.Info("LUber: the moped is gone. Refunded.");
                Cancel("your delivery never turned up. Money back.");
                return;
            }

            if (now - _phaseFrom > RideCapMs)
            {
                Log.Info("LUber: the rider could not reach you in time. Refunded.");
                Cancel("your delivery couldn't get to you. Money back.");
                return;
            }

            var gap = _rider.Position.DistanceTo(player.Position);

            if (gap > ParkRange)
            {
                if (now < _reaimAt) return;

                Ride(player.Position);
                return;
            }

            // Off the moped, bag in hand, and the rest of the way on foot.
            Hold();

            try
            {
                Function.Call(Hash.TASK_LEAVE_VEHICLE, _rider.Handle, _bike.Handle, 0);
                Function.Call(Hash.SET_PED_KEEP_TASK, _rider.Handle, true);
            }
            catch
            {
                // He is walked at you either way; the game gets him off it.
            }

            Carry();
            Begin(FoodState.Walking);

            Says(SaidHere);
            Aloud(Greets[_rng.Next(Greets.Length)]);
        }

        /// <summary>Walking it over, and following you while he does it.</summary>
        private void Walking(Ped player, int now)
        {
            if (now - _phaseFrom > WalkCapMs)
            {
                Log.Info("LUber: the rider gave up looking for you. Refunded.");
                Cancel("your delivery couldn't find you. Money back.");
                return;
            }

            var gap = _rider.Position.DistanceTo(player.Position);

            // YOU LEFT. He is not chasing a car across the map on foot with a paper bag.
            if (gap > LostRange)
            {
                Log.Info("LUber: you left while the rider was walking over. Refunded.");
                Cancel("you left. Money back.");
                return;
            }

            if (gap > HandRange)
            {
                if (now < _reaimAt) return;

                _reaimAt = now + 1500;

                try
                {
                    Function.Call(Hash.TASK_GO_TO_ENTITY, _rider.Handle, player.Handle,
                                  -1, HandRange * 0.75f, 1.6f, 1073741824f, 0);
                    Function.Call(Hash.SET_PED_KEEP_TASK, _rider.Handle, true);
                }
                catch
                {
                    // Asked again in a second and a half.
                }

                return;
            }

            // There. Stop, face him, and hand it over.
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _rider.Handle);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _rider.Handle, player.Handle, 800);

                Function.Call(Hash.REQUEST_ANIM_DICT, GiveDict);

                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, GiveDict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, _rider.Handle, GiveDict, GiveClip,
                                  4f, -2f, HandMs, 48, 0f, false, false, false);
                }
            }
            catch
            {
                // The bag still changes hands; only the performance is lost.
            }

            _handFrom = now;
            Begin(FoodState.Handing);
        }

        /// <summary>
        /// The moment it changes hands.
        ///
        /// THE FOOD MOVES HALFWAY THROUGH THE ANIMATION, not at the start and not at the end.
        /// At the start it is in your pocket before he has reached out; at the end it is a
        /// beat of him stood there with empty hands. In the middle it lands with the gesture.
        /// </summary>
        private void Handing(Ped player, int now)
        {
            if (now - _handFrom < HandMs / 2) return;

            var name = Pantry.NameOf(_id);

            // THE ONE FAILURE THAT CAN STILL HAPPEN HERE. Pockets were checked when it was
            // ordered and could have filled in the four minutes since, so the answer is asked
            // for rather than assumed -- and the money goes back if it is no.
            var took = Pantry.Give(_id, 1);

            Drop();

            if (!took)
            {
                Log.Info("LUber: " + name + " arrived and would not fit. Refunded.");

                if (Refund != null) Refund(_paid);

                Says(SaidNoRoom);
            }
            else
            {
                Log.Info("LUber: " + name + " delivered.");

                Says(SaidDone);

                if (Arrived != null) Arrived(name);
            }

            Aloud(Byes[_rng.Next(Byes.Length)]);

            // NOT EVERY TIME. A man who asks for a tip on every single order is a running gag
            // that stops being funny on the fourth delivery; one who asks most of the time is a
            // man who needs the money.
            if (_rng.Next(100) < TipChance)
            {
                _tipWho = _driver;
                _tipFace = _faceKey;
                _tipAt = Game.GameTime + TipMinMs + _rng.Next(TipSpanMs);
            }

            // He does not hang about either way.
            Leave();
            Begin(FoodState.Leaving);
        }

        /// <summary>Back on the moped and gone.</summary>
        private void Leaving(int now)
        {
            var done = now - _phaseFrom > LeaveCapMs;

            if (!done)
            {
                var player = Game.Player.Character;

                done = player != null && player.Exists() &&
                       _rider.Position.DistanceTo(player.Position) > GoneRange;
            }

            if (!done) return;

            Away();
            Clean();
            State = FoodState.None;
            What = "";
        }

        // ---- the people and the things ------------------------------------------

        private bool Make(Vector3 at)
        {
            try
            {
                // ONE PICKED AT RANDOM, then the rest of the list in order behind it as the
                // fallback for an install that has not got it. See Rides.
                var first = _rng.Next(Rides.Length);

                for (var i = 0; i < Rides.Length; i++)
                {
                    var pick = Rides[(first + i) % Rides.Length];

                    var model = new Model(pick.Model);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    _bike = World.CreateVehicle(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (_bike == null || !_bike.Exists()) continue;

                    _ride = pick;

                    Log.Info("LUber: a " + pick.Model + " went out with it.");
                    break;
                }

                if (_bike == null || !_bike.Exists()) return false;

                _bike.IsPersistent = true;

                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _bike.Handle);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _bike.Handle, true, true);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _bike.Handle, true, true, false);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, _bike.Handle, 0f);

                // ONLY THE ONE THAT IS MEANT TO BE WHITE. Everything used to be painted, on
                // the reasoning that a delivery fleet is a fleet -- which is exactly the idea
                // that has gone. A stock Asbo painted white is not a stock Asbo, and a Pizza
                // Boy painted white is a Pizza Boy with its own livery taken off it.
                //
                // Written as a custom colour rather than a paint index for the reason
                // Luber.Make gives: the index table has several whites in it and one of them
                // is nearly grey.
                if (_ride != null && _ride.White)
                {
                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, _bike.Handle, 0);
                    Function.Call(Hash.SET_VEHICLE_CUSTOM_PRIMARY_COLOUR, _bike.Handle, 255, 255, 255);
                    Function.Call(Hash.SET_VEHICLE_CUSTOM_SECONDARY_COLOUR, _bike.Handle, 255, 255, 255);
                }

                return Rider();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a LUber rider out: " + ex.Message);
                return false;
            }
        }

        private bool Rider()
        {
            try
            {
                Ped made = null;

                foreach (var name in Riders)
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    made = World.CreatePed(model, _bike.Position);
                    model.MarkAsNoLongerNeeded();

                    if (made != null && made.Exists())
                    {
                        // WHICHEVER ONE THE INSTALL ACTUALLY HAD. The photograph is of the man
                        // who turned up, not of the man at the top of the list -- on a build
                        // missing the first two that is a postman, and the thread should show
                        // a postman rather than somebody who is not there.
                        _faceKey = "luber_" + name;

                        UI.Headshots.WantAs(_faceKey, name);
                        break;
                    }

                    made = null;
                }

                if (made == null) return false;

                _rider = made;
                _rider.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _rider.Handle, true, true);
                Function.Call(Hash.SET_PED_INTO_VEHICLE, _rider.Handle, _bike.Handle, -1);

                // HE IS WORKING, NOT LIVING. He does not flinch at gunfire, does not join a
                // fight, cannot be dragged off the moped and cannot be shot off it by traffic
                // -- because every one of those ends with your dinner on the road and a refund
                // you did not ask for. He is not invincible: a player who runs him over has
                // done that on purpose and can have the consequence.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _rider.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _rider.Handle, false);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _rider.Handle, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, _rider.Handle, 251, true);
                Function.Call(Hash.SET_DRIVER_ABILITY, _rider.Handle, 1.0f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, _rider.Handle, 0.4f);

                // AND A HELMET IF HE IS ON TWO WHEELS, because the game gives a moped rider
                // one only sometimes and a man in a helmet reads as working. A man wearing one
                // in the driver's seat of a saloon reads as something else entirely.
                Function.Call(Hash.SET_PED_HELMET, _rider.Handle, _ride != null && _ride.Bike);

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a LUber rider on the moped: " + ex.Message);
                return false;
            }
        }

        /// <summary>The bag, in his right hand, for the walk over.</summary>
        private void Carry()
        {
            try
            {
                foreach (var name in Bags)
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _bag = World.CreateProp(model, _rider.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_bag != null && _bag.Exists()) break;
                    _bag = null;
                }

                if (_bag == null) return;

                _bag.IsPersistent = true;

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, _rider.Handle, RightHandBone);

                // No offset and no rotation: PH_R_Hand already sits where a held thing goes.
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _bag.Handle, _rider.Handle, bone,
                              0f, 0f, 0f, 0f, 0f, 0f, false, false, false, false, 2, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a bag in the rider's hand: " + ex.Message);
            }
        }

        /// <summary>Out of his hand, gone. The food is in your pocket by now.</summary>
        private void Drop()
        {
            try
            {
                if (_bag != null && _bag.Exists())
                {
                    Function.Call(Hash.DETACH_ENTITY, _bag.Handle, true, true);
                    _bag.Delete();
                }
            }
            catch
            {
                // It goes with the rest of it in Clean.
            }

            _bag = null;
        }

        // ---- driving and walking ------------------------------------------------

        private void Ride(Vector3 to)
        {
            _reaimAt = Game.GameTime + ReaimMs;

            try
            {
                var road = OnRoad(to);

                Function.Call(Hash.CLEAR_PED_TASKS, _rider.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                              _rider.Handle, _bike.Handle, road.X, road.Y, road.Z,
                              RideSpeed, Style, 6f);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, _rider.Handle, RideSpeed);
                Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, _rider.Handle, Style);
                Function.Call(Hash.SET_PED_KEEP_TASK, _rider.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send the LUber rider on: " + ex.Message);
            }
        }

        private void Hold()
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _rider.Handle);
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _rider.Handle, _bike.Handle, 1, 1200);
            }
            catch
            {
                // He gets off it either way.
            }
        }

        /// <summary>Back to the moped, on it, and away.</summary>
        private void Leave()
        {
            try
            {
                if (_blip != null && _blip.Exists()) { _blip.Delete(); _blip = null; }

                Function.Call(Hash.CLEAR_PED_TASKS, _rider.Handle);

                if (_bike != null && _bike.Exists())
                {
                    Function.Call(Hash.TASK_ENTER_VEHICLE, _rider.Handle, _bike.Handle,
                                  20000, -1, 2f, 1, 0);
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, _rider.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send the LUber rider home: " + ex.Message);
            }
        }

        /// <summary>
        /// Hands both of them back to the game.
        ///
        /// NOT DELETED WHILE YOU CAN SEE THEM. A rider who blinks out of existence three
        /// metres from you is worse than one who rides off badly, so both stop being ours and
        /// are left to the game's own clean-up, which does it when nobody is looking.
        /// </summary>
        private void Away()
        {
            Drop();

            try
            {
                if (_rider != null && _rider.Exists())
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _rider.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _rider.Handle, true);

                    if (_bike != null && _bike.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, _rider.Handle, _bike.Handle,
                                      RideSpeed, Style);
                    }

                    _rider.MarkAsNoLongerNeeded();
                }

                if (_bike != null && _bike.Exists()) _bike.MarkAsNoLongerNeeded();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not let the LUber rider go: " + ex.Message);
            }
        }

        // ---- housekeeping -------------------------------------------------------

        private void Begin(FoodState state)
        {
            State = state;
            _phaseFrom = Game.GameTime;
            _reaimAt = 0;
        }

        /// <summary>How long after the order he gets round to answering.</summary>
        private const int SayGapMs = 2200;

        /// <summary>
        /// The brand, for where his own face cannot go.
        ///
        /// A DIFFERENT MAN EVERY ORDER MEANS NO CONTACT TO HANG A PICTURE ON. The messages app
        /// asks the contact book for a thread's picture and there is nothing in it called Ravi
        /// Patel -- so the message carries one. It is the firm's, because that is the only
        /// thing about him that is the same twice.
        ///
        /// Only the phone can use it. The game's own feed takes a texture dictionary and
        /// nothing else; his headshot is what goes there, and it works now. See Notify.Text.
        /// </summary>
        private const string Badge = "luber_dp.png";

        /// <summary>How long the first line waits for his photograph before going without it.</summary>
        private const int FaceWaitMs = 6000;

        /// <summary>How many updates one delivery can carry, and how far apart they fall.</summary>
        private const int ChatMost = 3;
        private const int ChatMinMs = 14000;
        private const int ChatSpanMs = 16000;

        /// <summary>
        /// What the dot looks like: 226 radar_gang_vehicle_bikers, 225 radar_gang_vehicle.
        ///
        /// Raw numbers rather than the enum because these two read as a motorbike and a car
        /// outline whatever the FiveM list calls them, and the enum's names for them are about
        /// a personal vehicle, which this is not. See BLIPS.md.
        /// </summary>
        private const int BikeBlip = 226;
        private const int CarBlip = 225;

        /// <summary>How long after he has gone before he asks, and how often he bothers.</summary>
        private const int TipMinMs = 7000;
        private const int TipSpanMs = 6000;
        private const int TipChance = 70;

        /// <summary>
        /// One of his lines, as a text from him.
        ///
        /// A TEXT AND NOT A CARD, which is the whole change. The card said LUBER at the top
        /// with a company badge beside it and went nowhere -- it appeared, it faded, and there
        /// was no record of it. This goes in the phone under his name, so the delivery is a
        /// thread you can go back and read like every other conversation in the game, and the
        /// next order from the same man carries on down the same one.
        ///
        /// His picture is asked for rather than assumed. If the factory has not finished
        /// rendering it, Notify falls back to the silhouette for that one message and the next
        /// has it -- which is why the first line waits a couple of seconds. See SayGapMs.
        /// </summary>
        private void Says(string[] lines)
        {
            if (lines == null || lines.Length == 0) return;
            if (string.IsNullOrEmpty(_driver)) return;

            var line = lines[_rng.Next(lines.Length)];

            var body = line.Replace("{0}", string.IsNullOrEmpty(What) ? "food" : What)
                           .Replace("{1}", _paid.ToString());

            Notify.Text(Face, _driver, "LUber", body, false, Badge);
        }

        /// <summary>
        /// Out loud, in the voice his model came with.
        ///
        /// NOT A VOICE NAME OF OURS. PLAY_PED_AMBIENT_SPEECH_NATIVE uses the ped's OWN voice,
        /// and the ped is one of the game's two Indian models -- so the accent is the one
        /// Rockstar recorded for him and there is no table of voice names to get wrong. Force
        /// a voice by name and you are guessing at a string that may not be in the build; ask
        /// the ped and he answers in whatever he has.
        ///
        /// The line is stopped first. He is a street ped with a full ambient set and he will
        /// happily be halfway through complaining about traffic when he pulls up.
        /// </summary>
        private void Aloud(string line)
        {
            if (_rider == null || !_rider.Exists() || !_rider.IsAlive) return;

            try
            {
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, _rider.Handle);
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, _rider.Handle, line,
                              "SPEECH_PARAMS_FORCE");
            }
            catch (Exception ex)
            {
                Log.Debug("LUber rider had nothing to say: " + ex.Message);
            }
        }

        /// <summary>What he says walking up, and what he says going.</summary>
        private static readonly string[] Greets =
        {
            "GENERIC_HI", "GENERIC_HOWS_IT_GOING", "GENERIC_WHATS_UP"
        };

        private static readonly string[] Byes =
        {
            "GENERIC_BYE", "GENERIC_THANKS"
        };

        /// <summary>
        /// The ask, sent after everything about the order has been thrown away.
        ///
        /// It carries its own name and its own picture because by the time it lands there is no
        /// order left to read either off -- Clean has been through and _driver is empty. Filed
        /// under HIS name, so it lands at the bottom of the thread you have just been having
        /// with him rather than opening a new one.
        /// </summary>
        private void Tip()
        {
            if (string.IsNullOrEmpty(_tipWho)) return;

            var line = SaidTip[_rng.Next(SaidTip.Length)];

            Notify.Text(UI.Headshots.Txd(_tipFace), _tipWho, "LUber", line, false, Badge);

            _tipWho = "";
            _tipFace = "";
        }

        /// <summary>A different man every order. See Firsts.</summary>
        private void Named()
        {
            _driver = Firsts[_rng.Next(Firsts.Length)] + " " + Lasts[_rng.Next(Lasts.Length)];
        }

        private void Mark()
        {
            try
            {
                if (_bike == null || !_bike.Exists()) return;

                _blip = _bike.AddBlip();
                if (_blip == null || !_blip.Exists()) return;

                // THE SHAPE OF WHAT IS COMING. It was BlipSprite.Store, which draws a HOUSE --
                // so the map showed a shop moving down the freeway towards you. What a player
                // wants off this dot is what to look for when it pulls up, and that is not one
                // thing any more: the drivers use their own vehicles, so it is a scooter or it
                // is a saloon. See Rides, and BLIPS.md for the numbers.
                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle,
                              _ride != null && _ride.Bike ? BikeBlip : CarBlip);

                _blip.Color = BlipColor.Blue;
                _blip.Scale = 0.7f;
                // HIS NAME, NOT THE FIRM'S. The thread in the phone is from a man, and the
                // dot on the map is that same man on his way -- "LUber" on both would be the
                // company texting you and the company driving.
                _blip.Name = string.IsNullOrEmpty(_driver) ? "LUber" : _driver;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip a LUber rider: " + ex.Message);
            }
        }

        private void Clean()
        {
            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch
            {
                // Nothing else to do about a blip.
            }

            _blip = null;
            _rider = null;
            _bike = null;
            _id = "";
            _paid = 0;

            // HIS NAME GOES, HIS PHOTOGRAPH STAYS. The thread in the phone keeps whatever he
            // already said and keeps his face on it -- the picture is filed by ped model and
            // is worth having for the next one. What must not survive is the pending text: an
            // order cancelled in the two seconds before he answers must not have him cheerily
            // announce it is on its way.
            _driver = "";
            _faceKey = "";
            _saysAt = 0;
            _ride = null;

            // The updates stop with the order. The TIP does not -- it is scheduled at handover
            // and carries its own copy of who is asking, which is the whole point of it.
            _chatAt = 0;
            _chatLeft = 0;
        }

        /// <summary>
        /// On the way out, and this one DOES delete.
        ///
        /// The difference from Away is who is watching: this runs when the script is being
        /// unloaded, and a moped left wandering with a mission ped on it belongs to a script
        /// that no longer exists.
        /// </summary>
        public void RestoreWorld()
        {
            try
            {
                Drop();

                if (_blip != null && _blip.Exists()) _blip.Delete();
                if (_rider != null && _rider.Exists()) _rider.Delete();
                if (_bike != null && _bike.Exists()) _bike.Delete();
            }
            catch
            {
                // Teardown.
            }

            _blip = null;
            _rider = null;
            _bike = null;
            State = FoodState.None;
            What = "";
        }

        private static Vector3 OnRoad(Vector3 near)
        {
            try
            {
                var at = World.GetNextPositionOnStreet(near, true);
                return at == Vector3.Zero ? near : at;
            }
            catch
            {
                return near;
            }
        }

        private Vector3 Somewhere(Vector3 near)
        {
            for (var tries = 0; tries < 14; tries++)
            {
                try
                {
                    var away = ComeFromMin + (float)_rng.NextDouble() * (ComeFromMax - ComeFromMin);
                    var probe = near.Around(away);

                    var at = World.GetNextPositionOnStreet(probe, true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(near) < ComeFromMin * 0.6f) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }
    }
}
