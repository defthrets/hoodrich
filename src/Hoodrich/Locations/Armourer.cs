using System;
using System.Collections.Generic;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>One thing Stretch will sell you.</summary>
    internal sealed class Piece
    {
        public readonly string Name;
        public readonly string Weapon;
        public readonly int Price;
        public readonly int Ammo;
        public readonly string Note;

        /// <summary>
        /// How much armour it puts on him, or nought for a gun.
        ///
        /// A VEST IS NOT A WEAPON AND THE COUNTER HAS TO KNOW IT. It has no weapon hash, so
        /// every question the shelf asks about a gun -- do you own one, what does it hold, what
        /// bolts onto it -- has to have a different answer rather than a wrong one. This field
        /// is what the answers key off, and it is the only thing that separates the two kinds
        /// of thing on his shelf.
        /// </summary>
        public readonly int Armour;

        /// <summary>The prop shown on the crate for something that has no weapon model.</summary>
        public readonly string Prop;

        public bool IsVest => Armour > 0;

        public Piece(string name, string weapon, int price, int ammo, string note)
        {
            Name = name;
            Weapon = weapon;
            Price = price;
            Ammo = ammo;
            Note = note;
        }

        /// <summary>A vest: no weapon, no rounds, no parts -- a number and a prop.</summary>
        public Piece(string name, int armour, int price, string note, string prop)
        {
            Name = name;
            Weapon = "";
            Price = price;
            Ammo = 0;
            Note = note;
            Armour = armour;
            Prop = prop;
        }

        /// <summary>
        /// What comes in the bag with the gun.
        ///
        /// A third of what it holds, near enough two or three clips. Buying a piece off a man in
        /// a yard and walking away with two hundred rounds for it is a vending machine; you get
        /// enough to be going on with and you come back for the rest, which is the entire reason
        /// he has a handful of things rather than a menu.
        /// </summary>
        public int StarterAmmo => Ammo <= 0 ? Ammo : Math.Max(1, (int)Math.Round(Ammo / 3.0));

        /// <summary>What a box of rounds off him gives you.</summary>
        public int AmmoBox => Ammo <= 0 ? 0 : Math.Max(1, (int)Math.Round(Ammo / 2.0));

        public uint Hash => Function.Call<uint>(GTA.Native.Hash.GET_HASH_KEY, Weapon);
    }

    /// <summary>
    /// Stretch, who sells guns out of the courtyard on Forum Drive.
    ///
    /// Deliberately not a shop. Ammu-Nation exists, it is on the map, and it wants your licence
    /// and your name -- so a man in a courtyard who does not is a different thing to have, not a
    /// cheaper version of the same thing. He is one of yours, he is always there, and what he
    /// has is what he has.
    ///
    /// Kept apart from Stretch and Lamar for the same reason those two are kept apart: three
    /// people you go to for three different things reads as a neighbourhood, and one NPC with
    /// everything bolted to him reads as a menu.
    /// </summary>
    internal sealed class Armourer
    {
        /// <summary>The courtyard, up the path from Denise's.</summary>
        private static readonly Vector3 Spot = new Vector3(-129.187f, -1461.375f, 33.823f);
        private const float Heading = 294.841f;

        private const float SpawnRange = 110f;
        private const float DespawnRange = 190f;
        private const float TalkRange = 3.0f;
        private const int UpdateIntervalMs = 700;

        /// <summary>Pistol blip, which is what he is.</summary>
        /// <summary>
        /// 556 radar_supplies.
        ///
        /// It was 110, the gun shop, which is Ammu-Nation's own blip -- so the map said there
        /// was a licensed firearms retailer on Forum Drive. There is not. There is a man with a
        /// holdall, and supplies is what that is.
        /// </summary>
        private const int Sprite = 556;

        /// <summary>
        /// Big by name. Tried in order, first one this install has wins -- the heavy Families
        /// models are the point of the name, so the slim ones are last resorts rather than
        /// equals.
        /// </summary>
        private static readonly string[] Models =
        {
            "ig_stretch", "cs_stretch",

            // And the heavies behind him, for an install without the story model. He is a
            // Families man in a Families courtyard either way.
            // NO ONLINE MODELS. mp_m_famdd_01 was in here as a Families body and it is an
            // Online one -- see the note on Five0 Patrol's MpFaces. They do not suit the
            // street, and a fallback nobody looks at is exactly where one hides.
            "g_m_y_famca_01", "g_m_y_famfor_01",
            "a_m_m_soucent_01", "a_m_m_soucent_02", "g_m_y_famdnf_01"
        };

        /// <summary>
        /// What he has. Priced under Ammu-Nation, because none of it came with paperwork.
        ///
        /// Grouped the way he would lay it out rather than by the game's own
        /// categories: handguns, then things that hold more, then rifles, then what you carry
        /// when you are not carrying, then what you throw.
        /// </summary>
        /// <summary>
        /// EVERYTHING HE CAN GET HOLD OF, which is nearly everything the game has.
        ///
        /// It was twenty-one pieces across five racks, hand-picked -- and that was the right
        /// size while the counter was a list you scrolled. It is a gun on a crate now, one at a
        /// time, so a long shelf costs nothing to look at and the only argument left for a
        /// short one is that a man in a yard would not have it. He would. That is the business.
        ///
        /// WHAT IS LEFT OFF, and it is a short list: the joke weapons (a snowball, a newspaper,
        /// a ball), the three the game itself names "Invalid", the vehicle-only rockets, the
        /// jerry cans, and the two ray guns. Nothing in that list is a gun somebody would buy.
        ///
        /// Every id below was checked against data/weapons.json when this was written, which is
        /// the file the rest of the mod reads. The one exception is the knuckle duster, which
        /// that file does not list and the game has anyway.
        ///
        /// Priced under Ammu-Nation, because none of it came with paperwork.
        /// </summary>
        public static readonly Piece[] Handguns =
        {
            new Piece("Pistol",               "WEAPON_PISTOL",             450,   60, "Does the job"),
            new Piece("Combat Pistol",        "WEAPON_COMBATPISTOL",       500,   60, "Holds more"),
            new Piece("SNS Pistol",           "WEAPON_SNSPISTOL",          300,   40, "Fits anywhere"),
            new Piece("Vintage Pistol",       "WEAPON_VINTAGEPISTOL",      650,   40, "Somebody's grandad's"),
            new Piece("Double Action",        "WEAPON_DOUBLEACTION",       900,   36, "Slow and mean"),
            new Piece("Navy Revolver",        "WEAPON_NAVYREVOLVER",      1100,   36, "Older than the block"),
            new Piece("Heavy Revolver",       "WEAPON_REVOLVER",          1400,   36, "Takes a hand off"),
            new Piece("Pistol .50",           "WEAPON_PISTOL50",          1500,   54, "Loud and proud"),
            new Piece("Heavy Pistol",         "WEAPON_HEAVYPISTOL",       1300,   54, "Weighs something"),
            new Piece("AP Pistol",            "WEAPON_APPISTOL",          1600,  120, "Goes through things"),
            new Piece("Ceramic Pistol",       "WEAPON_CERAMICPISTOL",     1800,   48, "Walks through a door"),
            new Piece("Marksman Pistol",      "WEAPON_MARKSMANPISTOL",     700,   10, "One shot, then run"),
            new Piece("Perico Pistol",        "WEAPON_GADGETPISTOL",      1900,   54, "Somebody's holiday"),
            new Piece("WM 29 Pistol",         "WEAPON_PISTOLXM3",         1700,   48, "New money"),
            new Piece("Stun Gun",             "WEAPON_STUNGUN",            600,    0, "Nobody dies"),
            new Piece("Flare Gun",            "WEAPON_FLAREGUN",           400,   20, "For being seen"),
            new Piece("Pistol Mk II",         "WEAPON_PISTOL_MK2",        2400,   72, "Done up"),
            new Piece("SNS Pistol Mk II",     "WEAPON_SNSPISTOL_MK2",     2200,   48, "Done up"),
            new Piece("Heavy Revolver Mk II", "WEAPON_REVOLVER_MK2",      3200,   36, "Done up"),
        };

        public static readonly Piece[] Smgs =
        {
            new Piece("Micro SMG",      "WEAPON_MICROSMG",          2200,  200, "Loud in a car"),
            new Piece("Machine Pistol", "WEAPON_MACHINEPISTOL",     1800,  180, "Fits under a coat"),
            new Piece("Tactical SMG",   "WEAPON_TECPISTOL",         2600,  200, "Steadier than it looks"),
            new Piece("Mini SMG",       "WEAPON_MINISMG",           2400,  200, "Quick hands"),
            new Piece("SMG",            "WEAPON_SMG",               3200,  250, "The workhorse"),
            new Piece("Assault SMG",    "WEAPON_ASSAULTSMG",        4200,  250, "Halfway to a rifle"),
            new Piece("Combat PDW",     "WEAPON_COMBATPDW",         4600,  250, "Somebody's contract job"),
            new Piece("SMG Mk II",      "WEAPON_SMG_MK2",           6500,  300, "Done up"),
        };

        public static readonly Piece[] Shotguns =
        {
            new Piece("Sawn-Off Shotgun",   "WEAPON_SAWNOFFSHOTGUN",    1900,   40, "Close work"),
            new Piece("Double Barrel",      "WEAPON_DBSHOTGUN",         2400,   24, "Two and done"),
            new Piece("Pump Shotgun",       "WEAPON_PUMPSHOTGUN",       2800,   40, "What everybody pictures"),
            new Piece("Bullpup Shotgun",    "WEAPON_BULLPUPSHOTGUN",    3400,   40, "Short in the hands"),
            new Piece("Sweeper Shotgun",    "WEAPON_AUTOSHOTGUN",       3800,   40, "Clears a hallway"),
            new Piece("Assault Shotgun",    "WEAPON_ASSAULTSHOTGUN",    4400,   48, "Eight, fast"),
            new Piece("Heavy Shotgun",      "WEAPON_HEAVYSHOTGUN",      5200,   48, "Overkill, indoors"),
            new Piece("Combat Shotgun",     "WEAPON_COMBATSHOTGUN",     5600,   48, "Newer than the rest"),
            new Piece("Pump Shotgun Mk II", "WEAPON_PUMPSHOTGUN_MK2",   6200,   48, "Done up"),
        };

        public static readonly Piece[] Rifles =
        {
            new Piece("Compact Rifle",         "WEAPON_COMPACTRIFLE",      4500,  180, "The choppa"),
            new Piece("Advanced Rifle",        "WEAPON_ADVANCEDRIFLE",     5200,  250, "Light for what it is"),
            new Piece("Assault Rifle",         "WEAPON_ASSAULTRIFLE",      5600,  250, "You know this one"),
            new Piece("Carbine Rifle",         "WEAPON_CARBINERIFLE",      6400,  250, "Reaches further"),
            new Piece("Special Carbine",       "WEAPON_SPECIALCARBINE",    6800,  250, "Steady at range"),
            new Piece("Bullpup Rifle",         "WEAPON_BULLPUPRIFLE",      6200,  250, "Short and awkward"),
            new Piece("Service Carbine",       "WEAPON_TACTICALRIFLE",     7200,  250, "Army surplus"),
            new Piece("Military Rifle",        "WEAPON_MILITARYRIFLE",     7600,  250, "Not from a shop"),
            new Piece("Heavy Rifle",           "WEAPON_HEAVYRIFLE",        7800,  250, "Hits like a truck"),
            new Piece("Battle Rifle",          "WEAPON_BATTLERIFLE",       8200,  250, "Big rounds"),
            new Piece("El Strickler",          "WEAPON_STRICKLER",         7400,  250, "Somebody's pride"),
            new Piece("Gusenberg Sweeper",     "WEAPON_GUSENBERG",         5800,  250, "Museum piece, still works"),
            new Piece("MG",                    "WEAPON_MG",                7000,  300, "Belt fed"),
            new Piece("Combat MG",             "WEAPON_COMBATMG",          8600,  300, "Belt fed, angrier"),
            new Piece("Assault Rifle Mk II",   "WEAPON_ASSAULTRIFLE_MK2",  8500,  250, "Serious money"),
            new Piece("Carbine Rifle Mk II",   "WEAPON_CARBINERIFLE_MK2",  9500,  250, "Serious money"),
            new Piece("Special Carbine Mk II", "WEAPON_SPECIALCARBINE_MK2",  9800,  250, "Done up"),
            new Piece("Bullpup Rifle Mk II",   "WEAPON_BULLPUPRIFLE_MK2",  9200,  250, "Done up"),
            new Piece("Combat MG Mk II",       "WEAPON_COMBATMG_MK2",     12000,  300, "Done up"),
        };

        public static readonly Piece[] Snipers =
        {
            new Piece("Musket",               "WEAPON_MUSKET",            1200,    8, "One shot. Genuinely one"),
            new Piece("Sniper Rifle",         "WEAPON_SNIPERRIFLE",       7500,   30, "Patience required"),
            new Piece("Marksman Rifle",       "WEAPON_MARKSMANRIFLE",     6800,   30, "Halfway between"),
            new Piece("Precision Rifle",      "WEAPON_PRECISIONRIFLE",    9000,   20, "Somebody's tool"),
            new Piece("Heavy Sniper",         "WEAPON_HEAVYSNIPER",      11000,   30, "Through an engine block"),
            new Piece("Marksman Rifle Mk II", "WEAPON_MARKSMANRIFLE_MK2",  9500,   30, "Done up"),
            new Piece("Heavy Sniper Mk II",   "WEAPON_HEAVYSNIPER_MK2",  14000,   30, "Done up"),
        };

        public static readonly Piece[] Melee =
        {
            new Piece("Switchblade",    "WEAPON_SWITCHBLADE",        150,    0, "Quiet"),
            new Piece("Knuckle Duster", "WEAPON_KNUCKLE",            120,    0, "Quieter"),
            new Piece("Knife",          "WEAPON_KNIFE",              130,    0, "Kitchen drawer"),
            new Piece("Cavalry Dagger", "WEAPON_DAGGER",             400,    0, "Off somebody's wall"),
            new Piece("Machete",        "WEAPON_MACHETE",            250,    0, "Not subtle"),
            new Piece("Hatchet",        "WEAPON_HATCHET",            280,    0, "Not subtle either"),
            new Piece("Battle Axe",     "WEAPON_BATTLEAXE",          600,    0, "Absurd, and it works"),
            new Piece("Baseball Bat",   "WEAPON_BAT",                100,    0, "Sporting equipment"),
            new Piece("Crowbar",        "WEAPON_CROWBAR",            100,    0, "A tool, officer"),
            new Piece("Golf Club",      "WEAPON_GOLFCLUB",           110,    0, "Also sporting equipment"),
            new Piece("Hammer",         "WEAPON_HAMMER",              90,    0, "From the shed"),
            new Piece("Pipe Wrench",    "WEAPON_WRENCH",             120,    0, "From the same shed"),
            new Piece("Pool Cue",       "WEAPON_POOLCUE",            100,    0, "Ended a night once"),
            new Piece("Broken Bottle",  "WEAPON_BOTTLE",              40,    0, "Free with a drink"),
            new Piece("Nightstick",     "WEAPON_NIGHTSTICK",         200,    0, "Do not ask"),
            new Piece("Flashlight",     "WEAPON_FLASHLIGHT",          60,    0, "Two jobs"),
            new Piece("Stone Hatchet",  "WEAPON_STONE_HATCHET",      700,    0, "Older than the city"),
        };

        public static readonly Piece[] Throwables =
        {
            new Piece("Flare",          "WEAPON_FLARE",               80,   10, "For seeing"),
            new Piece("Molotov",        "WEAPON_MOLOTOV",            350,    5, "Bottle and a rag"),
            new Piece("Pipe Bomb",      "WEAPON_PIPEBOMB",           900,    5, "Homemade"),
            new Piece("Grenade",        "WEAPON_GRENADE",           1400,    5, "Not homemade"),
            new Piece("Tear Gas",       "WEAPON_SMOKEGRENADE",       800,    5, "Clears a room"),
            new Piece("BZ Gas",         "WEAPON_BZGAS",             1100,    5, "Clears it differently"),
            new Piece("Sticky Bomb",    "WEAPON_STICKYBOMB",        2500,    5, "For doors"),
            new Piece("Proximity Mine", "WEAPON_PROXMINE",          2800,    5, "For whoever follows"),
        };

        public static readonly Piece[] Heavy =
        {
            new Piece("Compact Launcher",  "WEAPON_COMPACTLAUNCHER",   9000,   10, "Fits in a car"),
            new Piece("Grenade Launcher",  "WEAPON_GRENADELAUNCHER",  14000,   20, "For a vehicle"),
            new Piece("Tear Gas Launcher", "WEAPON_GRENADELAUNCHER_SMOKE",  8000,   20, "For a crowd"),
            new Piece("Firework Launcher", "WEAPON_FIREWORK",          6000,   10, "For a party"),
            new Piece("RPG",               "WEAPON_RPG",              22000,   10, "Ends the conversation"),
            new Piece("Homing Launcher",   "WEAPON_HOMINGLAUNCHER",   28000,   10, "For the helicopter"),
            new Piece("Minigun",           "WEAPON_MINIGUN",          35000,  500, "If you can carry it"),
            new Piece("Railgun",           "WEAPON_RAILGUN",          45000,   20, "Do not ask him where"),
        };

        /// <summary>
        /// And vests, which are the other half of what a man in a yard sells.
        ///
        /// AMMU-NATION SELLS THESE AND THAT IS THE POINT RATHER THAN THE OBJECTION. Everything
        /// else on this shelf is here because the shop wants your licence and he does not, and
        /// a vest is the one thing on their wall you least want your name against. He has them
        /// for the same reason he has the guns.
        ///
        /// FIVE TIERS, THE GAME'S OWN NUMBERS. Armour in this engine is nought to a hundred and
        /// the shop sells it in fifths, so these are fifths -- there is no sense inventing a
        /// scale when the health bar already has one.
        ///
        /// PRICED WELL UNDER THE SHOP, like the rest of him. He is not cheaper because it is
        /// worse; he is cheaper because there is no paperwork and no shopfront to pay for.
        ///
        /// prop_bodyarmour_02 is the vest the game itself puts on the ground as a pickup, so
        /// the thing on the crate is the thing you are buying rather than a stand-in.
        /// </summary>
        public static readonly Piece[] Vests =
        {
            new Piece("Light Vest",      20,   150, "Takes the first one",          "prop_bodyarmour_02"),
            new Piece("Standard Vest",   40,   350, "What most of them wear",       "prop_bodyarmour_02"),
            new Piece("Heavy Vest",      60,   700, "Front and back plates",        "prop_bodyarmour_03"),
            new Piece("Super Heavy",     80,  1400, "Heavy, and you feel it",       "prop_bodyarmour_04"),
            new Piece("Full Plate",     100,  3000, "Everything he can get on you", "prop_bodyarmour_05"),
        };

        private readonly GangRegistry _gangs;

        /// <summary>
        /// Set by Main. Whether the player is in with a set yet.
        ///
        /// A predicate rather than a reference to the crew, because this file has never needed
        /// to know what a gang IS and should not start now -- it needs one bit, and one bit is
        /// what it is handed.
        /// </summary>
        public System.Func<bool> Working;

        private Ped _ped;
        private Blip _blip;
        private int _lastUpdate;
        private bool _held;
        private bool _talkHeld;

        public Armourer(GangRegistry gangs)
        {
            _gangs = gangs;
        }

        public string Name => "Stretch";

        public Vector3 Position => Spot;

        public Ped Ped => _ped != null && _ped.Exists() ? _ped : null;

        /// <summary>Set by Main: the conversation screen and what he has to say.</summary>
        public Conversation Talk;
        public Func<DialogueNode> TalkBuilder;

        public bool InReach
        {
            get
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists() || _ped == null || !_ped.Exists()) return false;

                var a = player.Position;
                var b = _ped.Position;
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;

                return (float)Math.Sqrt(dx * dx + dy * dy) <= TalkRange;
            }
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            EnsureBlip();

            var away = player.Position.DistanceTo(Spot);

            if (_ped != null && _ped.Exists())
            {
                if (away > DespawnRange) Despawn();
                else if (!_held) Settle();

                return;
            }

            if (away <= SpawnRange) Spawn();
        }

        private void Spawn()
        {
            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    // Probed from just above the authored height and only believed if it agrees.
                    // The courtyard has a first-floor walkway over it, and a probe from high up
                    // finds that instead of the path.
                    //
                    // WITHIN A FOOT, NOT WITHIN THREE METRES. His yard is a raised concrete
                    // slab with the estate's own terrain a metre underneath it, and a probe
                    // that has not got the slab's collision yet answers with the terrain --
                    // which is a metre down and well inside the three metres this used to
                    // allow. So he was built a metre below his own floor, standing in the dirt
                    // under the yard with the concrete over his head, and the only thing you
                    // could see of him was the prompt: InReach measures the flat distance and
                    // does not care how far down he is. Reported as him falling through the
                    // floor after a sale, which is what it looks like from up here.
                    //
                    // The authored height is a surveyed one. A probe that disagrees with it by
                    // more than a step is not correcting it, it is finding something else.
                    var spot = Spot;

                    try
                    {
                        if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 1.5f),
                                                  out var groundZ, GetGroundHeightMode.Normal) &&
                            groundZ > 0f && Math.Abs(groundZ - spot.Z) <= ProbeTrust)
                        {
                            spot.Z = groundZ;
                        }
                    }
                    catch
                    {
                        // Keep the authored height.
                    }

                    _ped = World.CreatePed(model, spot, Heading);
                    model.MarkAsNoLongerNeeded();

                    if (_ped == null || !_ped.Exists()) continue;

                    _ped.IsPersistent = true;
                    _ped.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _ped.Handle, true, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _ped.Handle, false);

                    // And he cannot die either. He is a shop that stands on a corner in the
                    // middle of a war -- his own corner is one of the three a raid comes for --
                    // so "shot by somebody who was not aiming at him" is not an edge case here,
                    // it is a Tuesday.
                    Function.Call(Hash.SET_ENTITY_INVINCIBLE, _ped.Handle, true);
                    Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, _ped.Handle, false);
                    Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, _ped.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _ped.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL, _ped.Handle, false);
                    Function.Call(Hash.SET_AMBIENT_VOICE_NAME, _ped.Handle, "SHOP_CLOTHES_LS");

                    var gang = _gangs.Get("families");
                    if (gang != null)
                    {
                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, _ped.Handle, gang.GroupHash);
                    }

                    Settle();

                    Log.Info("Stretch is out at " + spot + ".");
                    return;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Warn("No model would load for the armourer.");
        }

        /// <summary>
        /// How far off the surveyed height a ground probe may be and still be believed, and
        /// how far below his own floor he may end up before he is put back on it.
        ///
        /// A step is about 0.2 of a metre and a kerb rather less. Half of one is generous for
        /// a spot somebody stood in and wrote down, and it is nowhere near the metre that the
        /// slab-versus-terrain answer differs by.
        /// </summary>
        private const float ProbeTrust = 0.5f;
        private const float SunkBy = 0.6f;

        /// <summary>Puts him back on his spot, facing the right way.</summary>
        private void Settle()
        {
            try
            {
                // FLAT DISTANCE MISSED THE ONE THAT MATTERED. This asked how far he was from
                // his spot in three dimensions and moved him past two and a half metres of it,
                // which is right for a man who has wandered and useless for a man who is
                // exactly where he should be and a metre down. Under his own floor is a metre,
                // and a metre never reached the threshold -- so nothing ever put him back and
                // he stayed under there for the session.
                if (_ped.Position.DistanceTo(Spot) > 2.5f || Spot.Z - _ped.Position.Z > SunkBy)
                {
                    _ped.Position = Spot;
                }

                if (!Function.Call<bool>(Hash.IS_PED_USING_SCENARIO, _ped.Handle, "WORLD_HUMAN_SMOKING"))
                {
                    _ped.Task.ClearAll();
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle,
                                  "WORLD_HUMAN_SMOKING", 0, true);
                    _ped.Heading = Heading;
                }
            }
            catch
            {
                // He will settle on his own.
            }
        }

        private void EnsureBlip()
        {
            // Not until you are one of them.
            //
            // He is a Families man selling out of a Families courtyard, and to somebody who has
            // not met the set yet he is not a shop -- he is a stranger with a table. Lamar is
            // already behind this same door; this puts the man with the guns behind it too, so
            // the first session opens with one marker on the map and one thing to do.
            if (Working != null && !Working())
            {
                try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
                catch { /* gone either way */ }

                _blip = null;
                return;
            }

            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(Spot);
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, Sprite);
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.8f;
                _blip.IsShortRange = true;
                // Named the way the leaders are: who he is, then who he runs with. He was the
                // odd one out reading "the armourer -- guns" on a map where everybody else
                // says The Families, which made him look like a shop rather than one of ours
                // who happens to sell.
                var gang = _gangs == null ? null : _gangs.Get("families");

                _blip.Name = Name + " -- " + (gang == null ? "Chamberlain Gangster Families" : gang.Name);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the armourer: " + ex.Message);
            }
        }

        // ---- talking -----------------------------------------------------------

        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen || !InReach) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to see what Stretch is holding.");

            if (!WantsToTalk()) return;

            var root = TalkBuilder == null ? null : TalkBuilder();
            if (root == null) return;

            HoldForTalk();

            Talk.Speaker = _ped;
            Talk.Open(root, this);
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

        public void HoldForTalk()
        {
            if (_ped == null || !_ped.Exists() || _held) return;

            _held = true;

            try
            {
                var player = Game.Player.Character;

                _ped.Task.ClearAll();

                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _ped.Handle, player.Handle, -1);
                }
            }
            catch
            {
                // He will still talk.
            }
        }

        public void ReleaseFromTalk()
        {
            if (!_held || _ped == null || !_ped.Exists()) return;

            _held = false;
            Settle();
        }

        private void Despawn()
        {
            try
            {
                if (_ped != null && _ped.Exists())
                {
                    _ped.MarkAsNoLongerNeeded();
                    _ped.Delete();
                }
            }
            catch { /* teardown */ }

            _ped = null;
            _held = false;
        }

        public void RestoreWorld()
        {
            Despawn();

            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* teardown */ }

            _blip = null;
        }
    }
}
