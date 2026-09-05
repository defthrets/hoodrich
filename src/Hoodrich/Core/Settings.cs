using System;
using Hoodrich.Locations;
using GTA;
using GTA.Math;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Hoodrich.Core
{
    /// <summary>
    /// Typed view over Hoodrich.ini. Every value has a working code default, so the mod
    /// runs correctly with no ini present at all.
    /// </summary>
    internal sealed class Settings
    {
        // ---- general -----------------------------------------------------------
        public bool Enabled = true;
        public LogLevel LogLevel = LogLevel.Info;
        public int SaveIntervalSeconds = 120;
        public bool PauseDuringMission = true;

        // ---- phone -------------------------------------------------------------

        /// <summary>
        /// An extra key that opens the phone, for anybody who would rather keep the real one.
        ///
        /// None by default, because the phone button IS the phone button -- the mod takes it
        /// over the way it used to take over the weapon wheel, except this time it is giving
        /// something back rather than only taking.
        /// </summary>
        /// <summary>
        /// A key that opens the phone, as well as the game's phone button.
        ///
        /// F2 by default now rather than None. It also happens to be the rescue key below, and
        /// that is deliberate -- one key that always does the obvious thing beats two.
        /// </summary>
        public Keys PhoneKey = Keys.F2;

        /// <summary>
        /// The way back in when the mod has been switched off.
        ///
        /// THE SETTINGS SCREEN COULD LOCK YOU OUT OF ITSELF. Turning "Posted Up on" off makes
        /// the tick return before anything reads a button, including the phone -- so the
        /// screen holding the switch became unreachable, and the only way back was editing a
        /// file. Somebody who flicks a toggle to see what it does should not need a text
        /// editor to undo it.
        ///
        /// So this is read BEFORE the enabled check, it turns the mod back on, and it writes
        /// that to the ini so it stays on.
        /// </summary>
        public Keys RescueKey = Keys.F2;
        public Keys PhoneModifier = Keys.None;

        /// <summary>
        /// Seconds the phone button is handed back after picking the real phone.
        ///
        /// There is no native that opens the vanilla phone, so the only way to show it is to
        /// stop suppressing the button and give the player a window to press it in.
        /// </summary>
        public int VanillaPhoneSeconds = 6;

        /// <summary>Time scale while the phone is open. 1.0 disables the slowdown.</summary>
        public float WheelTimeScale = 0.25f;

        public bool BlurBackground = true;
        public string TimecycleModifier = "hud_def_blur";

        public bool PlaySounds = true;

        /// <summary>
        /// Whether recorded dialogue plays over the conversation screen.
        ///
        /// Off behaves exactly as having no recordings does, which is the point -- the screen
        /// has never needed the audio and still does not.
        /// </summary>
        public bool VoiceEnabled = true;

        /// <summary>
        /// How loud, 0 to 1.
        ///
        /// IT NEEDS ITS OWN, because this audio does not go through the game. MCI plays
        /// straight out to the default device, so GTA's own sliders do not touch it and a
        /// player who has turned the game down would otherwise get shouted at.
        /// </summary>
        public float VoiceVolume = 0.9f;

        /// <summary>
        /// Whether an already-heard line speaks again. See Voice.Repeat -- on is for recording.
        /// </summary>
        public bool VoiceRepeat;

        /// <summary>
        /// Tweets drawn down the right-hand side instead of posted to the game's feed.
        ///
        /// The native notification stack is anchored top-left and cannot be moved, so the feed
        /// shared a column with busts, deliveries and warnings -- two unrelated kinds of message
        /// in one place, with the important one buried under chatter. Drawn on the right they
        /// also get the author's own coloured avatar, which the native feed could never do.
        ///
        /// Set false and everything goes back through the notification system exactly as it was.
        /// </summary>
        public bool TweetsOnTheRight = true;

        /// <summary>
        /// Draw the blip art inside the posted-up status bars instead of their names.
        ///
        /// The ~BLIP_~ tag is documented for help messages and "other supported contexts",
        /// which is not a promise about a plain DRAW_TEXT -- and an unsupported tag renders as
        /// literal text. Turn this off and the bars say HEAT and REPUTATION instead.
        /// </summary>
        public bool BlipsInBars = false;

        /// <summary>
        /// Whether the corner readout is drawn at all while you are posted up.
        ///
        /// The wordmark, the heat and reputation bars, the sentence under them and the line of
        /// figures. Off leaves the corner working exactly as it did -- sales, heat, patrols,
        /// reputation, the lot -- with nothing on the screen about it, for anybody who would
        /// rather watch the street than a readout. The notifications stay either way, because
        /// with the panel gone they are the only thing that says a sale happened.
        /// </summary>
        public bool ShowDealHud = true;

        /// <summary>
        /// Where the bag sits on Franklin's back while he is dealing, and which way up.
        ///
        /// In the ini because they can only be judged by looking at them. X is across his
        /// back, Y is front to back (negative is behind him), Z is up.
        /// </summary>
        public float BagX = 0f;
        public float BagY = -0.16f;
        public float BagZ = 0f;
        public float BagPitch = 0f;

        /// <summary>
        /// How dense the sprayed letters are, and how big each mark is.
        ///
        /// Both in the ini because they are look-and-feel numbers that want trying on a real
        /// wall at real size rather than being reasoned about. Spacing is in letter heights,
        /// so it does not change meaning if the tag is drawn bigger; size is in metres.
        ///
        /// Smaller spacing is more solid and costs more marks. It cannot go below a floor,
        /// because somebody typing 0.001 in here would ask the engine for tens of thousands of
        /// decals on one wall and take the game with it.
        /// </summary>
        public float TagDotSpacing = 0.11f;
        public float TagDotSize = 0.50f;
        public float BagRoll = 0f;
        public float BagYaw = 0f;

        /// <summary>
        /// Doors into the game's own interiors, read straight out of the ini.
        ///
        /// Every value is here rather than in the code because every value needs correcting
        /// from inside the game. An MLO cannot be moved -- it is baked into the map at one
        /// fixed coordinate -- so Inside is WHERE THAT ROOM ACTUALLY IS, and it is a guess
        /// until somebody stands in it and reads the HUD. Door is where the way in should be,
        /// which is a matter of taste and a metre either way.
        /// </summary>
        public readonly List<DoorSpec> Doors = new List<DoorSpec>();

        /// <summary>
        /// Map blips for the places you have not turned into doors yet.
        ///
        /// On, because a list of a hundred and thirty-five places is not much use as a list.
        /// Off for anybody who wants their map back.
        /// </summary>
        public bool ShowPlaces = true;

        /// <summary>
        /// The online city all the time: the casino, the shop fronts, the offices, from the
        /// street and not only once you are through a door. See Locations.OnlineMap.
        /// </summary>
        public bool OnlineMap = true;

        // ---- economy -----------------------------------------------------------
        public float BulkPurchaseDiscountPercent = 50f;

        /// <summary>Grams that must be sold before a corner dealer will name his source.</summary>
        public float DocksUnlockGrams = 50f;

        // ---- market ------------------------------------------------------------

        /// <summary>Minutes between street-price drift steps. 0 freezes the market.</summary>
        public float MarketDriftIntervalMinutes = 5f;

        /// <summary>How far either side of the base price a product can wander, as a percent.</summary>
        public float MarketMaxSwingPercent = 45f;

        /// <summary>Whether a worked block goes quiet at all. Off restores the old behaviour.</summary>
        public bool BlockSaturationEnabled = true;

        /// <summary>
        /// Grams on one block that halve how often somebody walks up there.
        ///
        /// Stated as the half-way point because that is the number a player can feel. Two
        /// hundred and fifty grams is a solid evening on one corner, after which it is worth
        /// walking two streets over.
        /// </summary>
        public float BlockSaturationGrams = 250f;

        /// <summary>Real minutes for a half-worked block to come all the way back.</summary>
        public float BlockRecoveryMinutes = 20f;

        /// <summary>How quiet the deadest block can get. Never zero -- there is always somebody.</summary>
        public float BlockDemandFloor = 0.25f;

        /// <summary>Whether the house can be taken from you at all.</summary>
        public bool StashRaidsEnabled = true;

        /// <summary>
        /// Chance per check, at full heat and a fat house, that somebody comes.
        ///
        /// Checked twice a minute, so this is small on purpose: 2% at full heat works out at
        /// roughly one raid an hour of hard, loud dealing, and none at all for somebody who
        /// keeps their head down or keeps the house light.
        /// </summary>
        public float StashRaidChancePercent = 2f;

        /// <summary>How much of the house goes when they get in.</summary>
        public float StashRaidTakePercent = 40f;

        /// <summary>Real minutes between the text and the door.</summary>
        public float StashRaidWarningMinutes = 3f;

        /// <summary>Whether plugs ring you with one-off deals.</summary>
        public bool ColdCallsEnabled = true;

        /// <summary>How often the game even considers making one.</summary>
        public float ColdCallEveryMinutes = 12f;

        /// <summary>Chance it actually happens when it looks.</summary>
        public float ColdCallChancePercent = 35f;

        /// <summary>How long you have got to get there.</summary>
        public float ColdCallMinutes = 8f;

        /// <summary>Whether the feed ever tells you somewhere is busy.</summary>
        public bool BlockTipsEnabled = true;

        public float BlockTipEveryMinutes = 15f;
        public float BlockTipChancePercent = 40f;

        /// <summary>How long the named block stays busy.</summary>
        public float BlockTipMinutes = 12f;

        /// <summary>How much busier. 1.6 is noticeably worth the drive without being silly.</summary>
        public float BlockTipBoost = 1.6f;

        // ---- risk --------------------------------------------------------------

        /// <summary>Base chance a completed sale draws police attention.</summary>
        public float PoliceBustChancePercent = 12f;

        /// <summary>How long an undercover buyer takes to call it in -- your window to react.</summary>
        public float UndercoverCallSeconds = 6f;

        /// <summary>Get this far from the deal before the call lands and you are clear.</summary>
        public float UndercoverEscapeDistance = 40f;

        public int BustWantedStars = 2;

        /// <summary>
        /// Where the face covering lives, and which one it is.
        ///
        /// COMPONENT 8, DRAWABLE 4. Franklin's balaclava, measured rather than guessed, and
        /// it took three wrong answers to get here. Component 1 is the mask slot on a freemode
        /// ped and the BEARD slot on a story one -- his holds five things and all five are
        /// beards, which is why the first build put a goatee on him. Component 8 is "accs",
        /// the undershirt slot, which is not where anybody would look for a balaclava and is
        /// exactly where Rockstar put it.
        ///
        /// STILL SETTINGS RATHER THAN CONSTANTS, because a clothing pack or a different game
        /// build moves these, and because nothing in the game NAMES any of it -- there is no
        /// native that says "drawable 4 is a mask". Settings > Mask walks every slot live, and
        /// Core.Mask.Probe writes each one's size to the log so there is somewhere to start.
        /// Saved the moment they change.
        /// </summary>
        /// <summary>
        /// Whether the camera swings round the front while he takes something.
        ///
        /// On, because the animation is on his hands and his face and the gameplay camera is
        /// behind his head. Off for anybody who would rather keep their own -- it is taken for
        /// a few seconds and handed straight back either way.
        /// </summary>
        public bool DrugCamera = true;

        /// <summary>
        /// Whether the Knowai picker shows you the place while you are choosing it: a slow
        /// circle of the destination, live, behind the sheet. He is held where he stands
        /// until it hands back. Off, the picker sits over the street you are on.
        /// </summary>
        public bool RidePreview = true;

        /// <summary>
        /// Whether Object Spooner scenes are built. See Locations.Scenery: files in
        /// scripts\Hoodrich\scenery and, unless the next one is off, in menyooStuff\Spooner.
        /// </summary>
        public bool Scenery = true;

        /// <summary>Whether Menyoo's own Spooner folder is read as well as the mod's.</summary>
        public bool SceneryFromMenyoo = true;

        /// <summary>How near a scene has to be before it is stood up, in metres.</summary>
        public float SceneryRange = 220f;

        /// <summary>How much longer or shorter every drug-taking animation runs.</summary>
        public float DrugAnimLength = 1f;

        public int MaskSlot = 8;
        public bool MaskAsProp;
        public int MaskDrawable = 4;
        public int MaskTexture = 0;

        /// <summary>Percent of product dropped as a recoverable bag when you die.</summary>
        public float LoseOnDeathPercent = 100f;

        /// <summary>Percent of product the police keep when you are arrested. Not recoverable.</summary>
        public float LoseOnArrestPercent = 100f;

        /// <summary>Minutes a dropped bag survives before someone else takes it. 0 = forever.</summary>
        public float DeadDropDespawnMinutes = 10f;

        // ---- dealer stock ------------------------------------------------------

        /// <summary>Grams of each product a dealer holds when fully stocked.</summary>
        public float DealerMaxStockGrams = 120f;

        /// <summary>Minutes between restock steps; each tops a dealer up by a third of a load.</summary>
        public float DealerRestockMinutes = 10f;

        /// <summary>Chance a dealer simply has nothing when he posts up.</summary>
        public float DealerDryChancePercent = 20f;

        // ---- gang loans --------------------------------------------------------

        // ---- hideouts ----------------------------------------------------------

        /// <summary>Grams each hideout's stash holds.</summary>
        // MATCHES THE SHIPPED INI, which is the whole point of a default.
        //
        // It was 5000 here and 300000 in Hoodrich.ini -- a sixtyfold difference decided by
        // whether the player happened to have the ini, which is not a balance decision, it is
        // two people disagreeing in different files. Aligned to what actually ships so that
        // deleting the ini changes nothing.
        //
        // Three hundred kilos is almost certainly too generous now that the house can be
        // raided; that is a balance question, and it is one number in one file.
        public float HideoutStashCapacity = 300000f;

        // ---- posting up --------------------------------------------------------

        /// <summary>Chance EACH passer-by decides to buy. Busy pavements compound this.</summary>
        public float PostUpApproachChance = 20f;

        /// <summary>Grams moved in one street sale.</summary>
        public float PostUpDealGrams = 1.5f;

        /// <summary>Extra heat per sale for every person who can see it happen.</summary>
        public float PostUpHeatPerWitness = 0.15f;

        /// <summary>Corner heat that brings a patrol over to ask questions.</summary>
        public float PostUpHeatBeforePolice = 12f;

        /// <summary>Seconds from a cop reaching you to being searched. Your window to walk.</summary>
        public float PostUpSearchSeconds = 6f;

        /// <summary>Fine when a search finds product on you.</summary>
        public int PostUpFine = 2500;

        // ---- the law -----------------------------------------------------------

        // ---- Lamar's list ------------------------------------------------------

        /// <summary>
        /// Minutes he wants to himself after a job before he has another one.
        ///
        /// Not difficulty, pacing. Finishing one job and immediately being handed the next
        /// makes him a vending machine; a gap in which nothing is on and he is somewhere
        /// thinking makes the next one arrive rather than queue. 0 turns it off.
        /// </summary>
        public float LamarRestMinutes = 10f;

        // ---- the block ---------------------------------------------------------

        /// <summary>Whether the set drives its own blocks while you are stood on them.</summary>
        public bool RollersEnabled = true;

        /// <summary>Carloads out at once. Two is a neighbourhood; six is a convoy.</summary>
        public int RollerCars = 2;

        /// <summary>Riders on the footpaths at once.</summary>
        public int RollerBikes = 4;

        /// <summary>What Tanya charges to come out and lift a wreck of yours.</summary>
        public int TowFee = 500;

        /// <summary>
        /// How long the yard keeps a recovered car, in IN-GAME hours.
        ///
        /// Thirty-six is a day and a half, which at the game's own clock is a bit over an hour
        /// of playing. Long enough that you notice it is gone and get on with something else,
        /// which is the entire point of it not being instant.
        /// </summary>
        public float TowYardHours = 36f;

        /// <summary>
        /// Where a recovered car is left, or all zeroes to use the car's own spot on the lot.
        ///
        /// Zeroes by default ON PURPOSE. The forecourt spot outside the roller door is a real
        /// coordinate somebody has to stand on and read off, and a made-up one puts a car
        /// through a wall on every install at once. Until it is filled in, each car goes back
        /// to the space it was bought from, which is a spot the mod already knows is good.
        /// </summary>
        public float TowReturnX;
        public float TowReturnY;
        public float TowReturnZ;
        public float TowReturnH;

        /// <summary>
        /// The pearl flake on the set's cars, as an index into the game's colour table.
        ///
        /// The body is always the set's own colour. This is what is suspended in it, and it is
        /// a setting because the colour table is not written down anywhere that can be checked
        /// -- the difference between the green somebody wants and the one beside it is a single
        /// number, and finding it should not cost a rebuild.
        /// </summary>
        public int RollerPearl = 53;

        /// <summary>Whether the junction gets taken over at night. See Locations.Takeover.</summary>
        public bool TakeoverEnabled = true;

        /// <summary>
        /// How many nights apart the takeovers are. 1 is every night, 3 is one night in three.
        ///
        /// It ran every single night, which is what made it a fixture rather than an event:
        /// a thing that is always on is scenery, and the whole point of this one is turning
        /// up to it.
        /// </summary>
        public int TakeoverEveryNights = 3;

        /// <summary>How far out the ring of watchers stands. 0 uses the measured 19 metres.</summary>
        public float TakeoverRadius = 19f;


        /// <summary>
        /// How wide the loops the cars drive are, in metres.
        ///
        /// In the ini because it is a number that can only be judged by standing at the
        /// junction and watching, and it has been judged by eye more than once. The cars aim
        /// at a circle this wide and slide well outside it, which is what it should look like.
        /// </summary>
        public float TakeoverSpinRadius = 8f;

        /// <summary>Whether groups of the set walk the back streets on foot.</summary>
        /// <summary>
        public bool WalkersEnabled = true;

        /// <summary>How many of those groups are out at once. Each is three or four men.</summary>
        public int WalkerCrews = 2;

        /// <summary>Whether riders pull the front wheel up on the straights.</summary>
        public bool RollerWheelies = true;

        /// <summary>How hard. See Rollers.Lift for why this is a setting and not a constant.</summary>
        public float RollerWheelieLift = 1.9f;

        // ---- joining -----------------------------------------------------------

        /// <summary>Grams a gang leader fronts you when he takes you on.</summary>
        public float LeaderFrontGrams = 20f;

        /// <summary>
        /// Changes one setting and writes it to the ini, so it survives a reload.
        ///
        /// The in-memory value and the file are set together on purpose. A screen that changed
        /// only the object would work until you quit; one that changed only the file would not
        /// work until you quit. Both, or it is a setting in name only.
        /// </summary>
        public static bool Put(string section, string key, string value)
        {
            return IniFile.SetValue(Paths.Ini, section, key, value);
        }

        /// <summary>
        /// One value straight out of the ini on disk, without loading the whole thing.
        ///
        /// For the few things that are written back at runtime and have to be read back the
        /// same way -- the list of doors somebody is in the middle of adding, whose next name
        /// depends on what is already in the file rather than on what was loaded at startup.
        /// </summary>
        public static string Read(string section, string key, string fallback)
        {
            try
            {
                return IniFile.Load(Paths.Ini).GetString(section, key, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            var ini = IniFile.Load(Paths.Ini);

            s.Enabled = ini.GetBool("General", "Enabled", s.Enabled);
            s.LogLevel = ini.GetEnum("General", "LogLevel", s.LogLevel);
            // CLAMPED, because Main multiplies this by a thousand into an int.
            //
            // It was the only setting in this whole method with neither a Clamp nor a Max on
            // it. Anything above 2,147,483 wraps the multiply negative, the "is it time to
            // save yet" test becomes permanently true, and the mod writes the entire save
            // document to disk once a second forever -- which is the exact opposite of what
            // somebody typing a huge number is asking for. A floor of ten stops the other end
            // doing the same thing more slowly.
            s.SaveIntervalSeconds =
                (int)Clamp(ini.GetInt("General", "SaveIntervalSeconds", s.SaveIntervalSeconds),
                           0f, 86400f);

            if (s.SaveIntervalSeconds > 0 && s.SaveIntervalSeconds < 10) s.SaveIntervalSeconds = 10;
            s.PauseDuringMission = ini.GetBool("General", "PauseDuringMission", s.PauseDuringMission);

            // [Phone] first, then the old [Wheel] key as a fallback, so an existing
            // Hoodrich.ini keeps working rather than silently reverting to defaults.
            s.VanillaPhoneSeconds =
                (int)Clamp(ini.GetInt("Phone", "VanillaPhoneSeconds", s.VanillaPhoneSeconds), 1f, 30f);

            s.PhoneKey = ini.GetKey("Phone", "Key", ini.GetKey("Wheel", "Key", s.PhoneKey));
            s.RescueKey = ini.GetKey("General", "RescueKey", s.RescueKey);
            s.PhoneModifier = ini.GetKey("Phone", "Modifier",
                                         ini.GetKey("Wheel", "Modifier", s.PhoneModifier));

            s.WheelTimeScale = Clamp(ini.GetFloat("Phone", "TimeScale",
                                     ini.GetFloat("Wheel", "TimeScale", s.WheelTimeScale)), 0.05f, 1f);
            s.BlurBackground = ini.GetBool("Phone", "BlurBackground",
                                           ini.GetBool("Wheel", "BlurBackground", s.BlurBackground));
            s.TimecycleModifier = ini.GetString("Phone", "TimecycleModifier",
                                                ini.GetString("Wheel", "TimecycleModifier", s.TimecycleModifier));
            s.TweetsOnTheRight = ini.GetBool("Socials", "TweetsOnTheRight", s.TweetsOnTheRight);
            s.BlipsInBars = ini.GetBool("Wheel", "BlipsInBars", s.BlipsInBars);
            s.ShowDealHud = ini.GetBool("PostUp", "ShowDealHud", s.ShowDealHud);

            s.BagX = ini.GetFloat("Dealing", "BagX", s.BagX);
            s.BagY = ini.GetFloat("Dealing", "BagY", s.BagY);
            s.BagZ = ini.GetFloat("Dealing", "BagZ", s.BagZ);
            s.BagPitch = ini.GetFloat("Dealing", "BagPitch", s.BagPitch);

            // Floored hard. See the note on the fields: a tiny spacing is not a bad look, it
            // is a hang.
            s.TagDotSpacing = Clamp(ini.GetFloat("Tags", "DotSpacing", s.TagDotSpacing),
                                    0.05f, 0.40f);
            s.TagDotSize = Clamp(ini.GetFloat("Tags", "DotSize", s.TagDotSize), 0.10f, 1.50f);
            s.BagRoll = ini.GetFloat("Dealing", "BagRoll", s.BagRoll);
            s.BagYaw = ini.GetFloat("Dealing", "BagYaw", s.BagYaw);

            // One block per door, all read the same way. Adding a third room is a section in
            // the ini and a line here, not another class.
            // THE IPL IS THE PLACEMENT, NOT THE ARCHETYPE. bkr_biker_dlc_int_ware02 is what
            // the room is called; what the game will actually load is the placement it sits
            // in, whose name carries an index. Taken from the interior loader on this machine
            // rather than guessed: its own list runs interior_0 to interior_5 over the two
            // clubhouses and the warehouses, so ware02 is index 3. Both names are asked for,
            // because the archetype costs nothing and the day one of them is wrong the other
            // may not be.
            //
            // The coordinates are the biker warehouse terrace. They all sit within a hundred
            // metres of each other forty metres under the airport, and which of the five is
            // the weed farm is exactly the sort of thing that cannot be checked from here --
            // so the door tries them in turn and keeps the one the game says has a room in it.
            // THE TWO ROOMS WERE THE WRONG WAY ROUND. The door at Lamar's opened the meth
            // lab and the one by the house opened the weed farm, which is only findable by
            // standing in both -- so the coordinates are swapped here rather than the names:
            // the roller door at Lamar's is the grow room because that is where the grow is.
            s.Doors.Add(Worked(Dressed(Named(Also(ReadDoor(ini, "GrowRoom", "grow room",
                                 "bkr_biker_dlc_int_ware02",
                                 BlipSprite.Weed,
                                 -201.384f, -1707.909f, 32.664f, 313.362f,
                                 1062.130f, -3183.587f, -39.164f, 159.785f),
                             1039.000f, -3098.000f, -39.000f,
                             1063.000f, -3195.000f, -39.000f,
                             1093.000f, -3195.000f, -39.000f,
                             1137.000f, -3197.000f, -39.000f,
                             1165.000f, -3196.000f, -39.000f,
                             997.000f, -3200.000f, -37.000f),
                             // THE NAMES THAT ACTUALLY LOAD IT, read out of the interior loader
                             // sat in this game's scripts folder rather than guessed. An IPL is
                             // named for the PLACEMENT, not the room: the archetype is called
                             // bkr_biker_dlc_int_ware02 and asking for that does nothing while
                             // looking exactly like asking for the right thing.
                             //
                             // ALL FOUR WAREHOUSES, not one. That loader's own list runs
                             // interior_0 to interior_5 across the two clubhouses and the
                             // warehouses and is MISSING index 3 -- the one this room needs.
                             // So index 3 is still a guess, and the other three are here so
                             // that a guess being wrong means landing in the wrong warehouse
                             // rather than in nothing at all. They are empty shells buried
                             // under the sea; loading four costs a few megabytes and no frames.
                             "bkr_biker_interior_placement_interior_3_biker_dlc_int_ware02_milo;"
                             + "bkr_biker_interior_placement_interior_2_biker_dlc_int_ware01_milo;"
                             + "bkr_biker_interior_placement_interior_4_biker_dlc_int_ware03_milo;"
                             + "bkr_biker_interior_placement_interior_5_biker_dlc_int_ware04_milo;"
                             // THE STOCK IS NOT IN THIS LIST. It was -- stash names, dryer
                             // names, upgrade names, all asked for as IPLs -- and none of it
                             // is an IPL. They are entity sets inside the room, switched on
                             // by Dressed below once the room exists. See InteriorDoor.Dress.
                             + "tr_int_placement_tr_interior_2_tuner_methlab_1_milo_;"
                             + "tr_int_placement_tr_interior_3_tuner_methlab_1_milo_;"
                             + "tr_int_placement_tr_interior_4_tuner_methlab_1_milo_;"
                             + "tr_int_placement_tr_interior_5_tuner_methlab_1_milo_"),
                             WeedSets),
                             // THE SET'S OWN, AND MOSTLY WOMEN. There is one Families female
                             // model in the game, so two of the three are her -- given random
                             // clothing on the way in, or the room has twins in it.
                             "g_f_y_families_01", "g_f_y_families_01", "g_m_y_famdnf_01"));

            // radar_production_crack, 497 -- the razor blade.
            //
            // 51 was here and has gone to the set's leader, who sells the pills this room
            // is the better half of that swap as well: the room is where product gets cut and
            // pressed, and a blade is what that is done with. There is no pill-press sprite in
            // the game, so the two closest pictures go to the two things they actually match.
            //
            // The ini section keeps its old name so an existing Hoodrich.ini with a [CrackDen]
            // block in it still overrides the right door. What it is CALLED on screen comes
            // from the default below, which the ini can override on its own.
            s.Doors.Add(Worked(Staffed(Dressed(Named(Also(ReadDoor(ini, "CrackDen", "pill press garage",
                                 "tr_tuner_methlab_1",
                                 (BlipSprite)497,
                                 -105.053f, -1408.631f, 29.673f, 226.934f,
                                 996.914f, -3200.434f, -36.394f, 101.076f),
                             1000.000f, -3200.000f, -38.000f,
                             1009.000f, -3196.000f, -38.000f,
                             1064.000f, -3183.000f, -39.000f),
                             // The tuner meth lab has FOUR placements rather than one, and
                             // tr_tuner_methlab_1 -- which is what the ini has always asked
                             // for -- is the archetype they all share. Same lesson twice.
                             "tr_int_placement_tr_interior_2_tuner_methlab_1_milo_;"
                             + "tr_int_placement_tr_interior_3_tuner_methlab_1_milo_;"
                             + "tr_int_placement_tr_interior_4_tuner_methlab_1_milo_;"
                             + "tr_int_placement_tr_interior_5_tuner_methlab_1_milo_;"
                             + "bkr_biker_interior_placement_interior_2_biker_dlc_int_ware01_milo;"
                             + "bkr_biker_interior_placement_interior_3_biker_dlc_int_ware02_milo;"
                             + "bkr_biker_interior_placement_interior_4_biker_dlc_int_ware03_milo;"
                             + "bkr_biker_interior_placement_interior_5_biker_dlc_int_ware04_milo"),
                             MethSets),
                             // THE GAME'S OWN LAB STAFF, at four stations read off a player
                             // stood on each: the platform at the cooker, the control panel,
                             // the floor by the tanks, and the far side by the furnace. The
                             // models are the ones the online game staffs these rooms with,
                             // and every clip is in the game's own animation list.
                             Cooking("mp_m_meth_01", "base_idle_tank_cooker",
                                     1005.827f, -3200.352f, -38.519f, 178.137f),
                             Cooking("mp_f_meth_01", "base_idle_tank_clipboard",
                                     1002.761f, -3199.873f, -38.993f, 121.662f),
                             Cooking("mp_m_meth_01", "base_idle_tank_ammonia",
                                     1002.984f, -3194.868f, -38.993f, 358.821f),
                             Cooking("mp_f_meth_01", "base_idle_tank_sacid",
                                     1014.616f, -3198.087f, -38.993f, 105.327f)),
                             // And the set's own three, who wander and talk as before.
                             "g_f_y_families_01", "g_f_y_families_01", "g_m_y_famdnf_01"));
            // ANY NUMBER OF MORE DOORS, named in the ini rather than in here.
            //
            // The two above are the mod's own. Everything else -- the casino, a club, a
            // shop, whatever somebody wants a way into -- is a section in Hoodrich.ini and
            // its name listed under [Doors] More. Nothing about a door is code: it is two
            // coordinates and a name, and the two coordinates can only honestly come from
            // somebody standing on them. See the two rows on the settings screen that write
            // them down.
            s.ShowPlaces = ini.GetBool("Doors", "ShowPlaces", s.ShowPlaces);
            s.OnlineMap = ini.GetBool("Doors", "OnlineMap", s.OnlineMap);

            foreach (var name in ini.GetString("Doors", "More", "").Split(','))
            {
                var section = name.Trim();
                if (section.Length == 0) continue;

                var door = ReadDoor(ini, section, section, "", (BlipSprite)40,
                                    0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

                // The rest of what the picker wrote. Extra is the second and later interior
                // names; a door with more than one has to ask for all of them or the room
                // arrives with its walls missing.
                door.Extra = ini.GetString(section, "Extra", "");
                door.Sprite = (BlipSprite)ini.GetInt(section, "Sprite", 40);

                // A door with nothing on either end of it is a section somebody started and
                // did not finish. Better ignored than put at the middle of the map.
                if (Math.Abs(door.DoorX) < 0.01f && Math.Abs(door.DoorY) < 0.01f) continue;
                if (Math.Abs(door.InsideX) < 0.01f && Math.Abs(door.InsideY) < 0.01f) continue;

                s.Doors.Add(door);
            }

            s.PlaySounds = ini.GetBool("Phone", "PlaySounds",
                                        ini.GetBool("Wheel", "PlaySounds", s.PlaySounds));

            s.VoiceEnabled = ini.GetBool("Voice", "Enabled", s.VoiceEnabled);
            s.VoiceVolume = Clamp(ini.GetFloat("Voice", "Volume", s.VoiceVolume), 0f, 1f);
            s.VoiceRepeat = ini.GetBool("Voice", "RepeatLines", s.VoiceRepeat);

            s.BulkPurchaseDiscountPercent =
                Clamp(ini.GetFloat("Economy", "BulkPurchaseDiscountPercent", s.BulkPurchaseDiscountPercent), 0f, 90f);
            s.DocksUnlockGrams = Math.Max(0f, ini.GetFloat("Economy", "DocksUnlockGrams", s.DocksUnlockGrams));
            s.MarketDriftIntervalMinutes =
                Math.Max(0f, ini.GetFloat("Economy", "MarketDriftIntervalMinutes", s.MarketDriftIntervalMinutes));
            s.MarketMaxSwingPercent =
                Clamp(ini.GetFloat("Economy", "MarketMaxSwingPercent", s.MarketMaxSwingPercent), 0f, 80f);
            s.BlockSaturationEnabled =
                ini.GetBool("Economy", "BlockSaturationEnabled", s.BlockSaturationEnabled);
            s.BlockSaturationGrams =
                Math.Max(10f, ini.GetFloat("Economy", "BlockSaturationGrams", s.BlockSaturationGrams));
            s.BlockRecoveryMinutes =
                Math.Max(1f, ini.GetFloat("Economy", "BlockRecoveryMinutes", s.BlockRecoveryMinutes));
            s.BlockDemandFloor =
                Clamp(ini.GetFloat("Economy", "BlockDemandFloor", s.BlockDemandFloor), 0.05f, 1f);
            s.StashRaidsEnabled =
                ini.GetBool("Hideouts", "StashRaidsEnabled", s.StashRaidsEnabled);
            s.StashRaidChancePercent =
                Clamp(ini.GetFloat("Hideouts", "StashRaidChancePercent", s.StashRaidChancePercent), 0f, 50f);
            s.StashRaidTakePercent =
                Clamp(ini.GetFloat("Hideouts", "StashRaidTakePercent", s.StashRaidTakePercent), 5f, 95f);
            s.StashRaidWarningMinutes =
                Math.Max(0.5f, ini.GetFloat("Hideouts", "StashRaidWarningMinutes", s.StashRaidWarningMinutes));
            s.ColdCallsEnabled =
                ini.GetBool("Supply", "ColdCallsEnabled", s.ColdCallsEnabled);
            s.ColdCallEveryMinutes =
                Math.Max(1f, ini.GetFloat("Supply", "ColdCallEveryMinutes", s.ColdCallEveryMinutes));
            s.ColdCallChancePercent =
                Clamp(ini.GetFloat("Supply", "ColdCallChancePercent", s.ColdCallChancePercent), 0f, 100f);
            s.ColdCallMinutes =
                Math.Max(2f, ini.GetFloat("Supply", "ColdCallMinutes", s.ColdCallMinutes));
            s.BlockTipsEnabled =
                ini.GetBool("Socials", "BlockTipsEnabled", s.BlockTipsEnabled);
            s.BlockTipEveryMinutes =
                Math.Max(1f, ini.GetFloat("Socials", "BlockTipEveryMinutes", s.BlockTipEveryMinutes));
            s.BlockTipChancePercent =
                Clamp(ini.GetFloat("Socials", "BlockTipChancePercent", s.BlockTipChancePercent), 0f, 100f);
            s.BlockTipMinutes =
                Math.Max(2f, ini.GetFloat("Socials", "BlockTipMinutes", s.BlockTipMinutes));
            s.BlockTipBoost =
                Clamp(ini.GetFloat("Socials", "BlockTipBoost", s.BlockTipBoost), 1f, 2.5f);

            s.PoliceBustChancePercent =
                Clamp(ini.GetFloat("Risk", "PoliceBustChancePercent", s.PoliceBustChancePercent), 0f, 100f);
            s.UndercoverCallSeconds =
                Clamp(ini.GetFloat("Risk", "UndercoverCallSeconds", s.UndercoverCallSeconds), 1f, 60f);
            s.UndercoverEscapeDistance =
                Clamp(ini.GetFloat("Risk", "UndercoverEscapeDistance", s.UndercoverEscapeDistance), 5f, 300f);
            s.BustWantedStars = (int)Clamp(ini.GetInt("Risk", "BustWantedStars", s.BustWantedStars), 1f, 5f);

            s.DrugCamera = ini.GetBool("Highs", "DrugCamera", s.DrugCamera);
            s.RidePreview = ini.GetBool("Knowai", "Preview", s.RidePreview);

            s.Scenery = ini.GetBool("Scenery", "Enabled", s.Scenery);
            s.SceneryFromMenyoo = ini.GetBool("Scenery", "FromMenyoo", s.SceneryFromMenyoo);
            s.SceneryRange = Clamp(ini.GetFloat("Scenery", "Range", s.SceneryRange), 40f, 600f);

            // Where each held thing sits in his hand, as set on the settings screen. See Economy.Fit.
            foreach (var prop in Economy.Fit.Names)
            {
                var packed = ini.GetString("HandFit", prop, "");
                if (!string.IsNullOrEmpty(packed)) Economy.Fit.Set(prop, Economy.Fit.Unpack(packed), persist: false);
            }
            s.DrugAnimLength = Clamp(ini.GetFloat("Highs", "AnimLength", s.DrugAnimLength), 0.5f, 3f);

            s.MaskSlot = (int)Clamp(ini.GetInt("Mask", "Slot", s.MaskSlot), 0f, 11f);
            s.MaskAsProp = ini.GetBool("Mask", "AsProp", s.MaskAsProp);
            s.MaskDrawable = (int)Clamp(ini.GetInt("Mask", "Drawable", s.MaskDrawable), 0f, 400f);
            s.MaskTexture = (int)Clamp(ini.GetInt("Mask", "Texture", s.MaskTexture), 0f, 64f);
            s.LoseOnDeathPercent =
                Clamp(ini.GetFloat("Risk", "LoseOnDeathPercent", s.LoseOnDeathPercent), 0f, 100f);
            s.LoseOnArrestPercent =
                Clamp(ini.GetFloat("Risk", "LoseOnArrestPercent", s.LoseOnArrestPercent), 0f, 100f);
            s.DeadDropDespawnMinutes =
                Math.Max(0f, ini.GetFloat("Risk", "DeadDropDespawnMinutes", s.DeadDropDespawnMinutes));

            s.DealerMaxStockGrams =
                Math.Max(1f, ini.GetFloat("Supply", "DealerMaxStockGrams", s.DealerMaxStockGrams));
            s.DealerRestockMinutes =
                Math.Max(0f, ini.GetFloat("Supply", "DealerRestockMinutes", s.DealerRestockMinutes));
            s.DealerDryChancePercent =
                Clamp(ini.GetFloat("Supply", "DealerDryChancePercent", s.DealerDryChancePercent), 0f, 100f);

            s.HideoutStashCapacity =
                Math.Max(1f, ini.GetFloat("Hideouts", "HideoutStashCapacity", s.HideoutStashCapacity));

            s.PostUpApproachChance =
                Clamp(ini.GetFloat("PostUp", "PostUpApproachChance", s.PostUpApproachChance), 0f, 100f);
            s.PostUpDealGrams = Math.Max(0.1f, ini.GetFloat("PostUp", "PostUpDealGrams", s.PostUpDealGrams));
            s.PostUpHeatPerWitness =
                Math.Max(0f, ini.GetFloat("PostUp", "PostUpHeatPerWitness", s.PostUpHeatPerWitness));
            s.PostUpHeatBeforePolice =
                Math.Max(1f, ini.GetFloat("PostUp", "PostUpHeatBeforePolice", s.PostUpHeatBeforePolice));
            s.PostUpSearchSeconds =
                Clamp(ini.GetFloat("PostUp", "PostUpSearchSeconds", s.PostUpSearchSeconds), 1f, 60f);
            s.PostUpFine = Math.Max(0, ini.GetInt("PostUp", "PostUpFine", s.PostUpFine));

            s.LeaderFrontGrams = Math.Max(0f, ini.GetFloat("Map", "LeaderFrontGrams", s.LeaderFrontGrams));


            s.LamarRestMinutes =
                Math.Max(0f, ini.GetFloat("Jobs", "LamarRestMinutes", s.LamarRestMinutes));

            s.RollersEnabled = ini.GetBool("Block", "RollersEnabled", s.RollersEnabled);
            s.RollerCars = (int)Clamp(ini.GetInt("Block", "RollerCars", s.RollerCars), 0f, 6f);
            s.RollerBikes = (int)Clamp(ini.GetInt("Block", "RollerBikes", s.RollerBikes), 0f, 6f);
            s.TowFee = (int)Clamp(ini.GetInt("Cars", "TowFee", s.TowFee), 0f, 100000f);
            s.TowYardHours = Clamp(ini.GetFloat("Cars", "TowYardHours", s.TowYardHours), 0f, 336f);

            s.TowReturnX = ini.GetFloat("Cars", "TowReturnX", s.TowReturnX);
            s.TowReturnY = ini.GetFloat("Cars", "TowReturnY", s.TowReturnY);
            s.TowReturnZ = ini.GetFloat("Cars", "TowReturnZ", s.TowReturnZ);
            s.TowReturnH = ini.GetFloat("Cars", "TowReturnH", s.TowReturnH);

            s.TakeoverEnabled = ini.GetBool("Block", "TakeoverEnabled", s.TakeoverEnabled);
            s.TakeoverEveryNights =
                (int)Clamp(ini.GetInt("Block", "TakeoverEveryNights", s.TakeoverEveryNights), 1f, 14f);
            s.TakeoverSpinRadius = Clamp(ini.GetFloat("Block", "TakeoverSpinRadius", s.TakeoverSpinRadius), 2f, 18f);
            s.TakeoverRadius = Clamp(ini.GetFloat("Block", "TakeoverRadius", s.TakeoverRadius), 0f, 60f);

            s.WalkersEnabled = ini.GetBool("Block", "WalkersEnabled", s.WalkersEnabled);
            s.WalkerCrews = (int)Clamp(ini.GetInt("Block", "WalkerCrews", s.WalkerCrews), 0f, 5f);

            // Never read until now. It was declared, documented and written into the ini, and
            // nothing ever loaded it -- so the number in the file did nothing at all.
            s.RollerPearl = (int)Clamp(ini.GetInt("Block", "RollerPearl", s.RollerPearl), 0f, 160f);

            s.RollerWheelies = ini.GetBool("Block", "RollerWheelies", s.RollerWheelies);
            s.RollerWheelieLift = Clamp(ini.GetFloat("Block", "RollerWheelieLift",
                                                     s.RollerWheelieLift), 0f, 8f);

            Log.Level = s.LogLevel;
            Log.Info("Settings loaded: phone key=" + s.PhoneKey +
                     " timescale=" + s.WheelTimeScale + " blur=" + s.BlurBackground);
            return s;
        }

        /// <summary>
        /// One door out of one ini section, falling back to the seeded coordinates.
        ///
        /// The defaults are what ships; the ini is what wins. Both interior coordinates below
        /// are guesses and are meant to be corrected -- InteriorDoor bounces the player back
        /// out with a message naming the section when the room turns out not to be there.
        /// </summary>
        private static DoorSpec ReadDoor(IniFile ini, string section, string name, string ipl,
                                         BlipSprite sprite,
                                         float dx, float dy, float dz, float dh,
                                         float ix, float iy, float iz, float ih)
        {
            return new DoorSpec
            {
                Section = section,
                Name = ini.GetString(section, "Name", name),
                Ipl = ini.GetString(section, "Ipl", ipl),
                Blip = ini.GetBool(section, "Blip", true),
                Sprite = sprite,

                DoorX = ini.GetFloat(section, "DoorX", dx),
                DoorY = ini.GetFloat(section, "DoorY", dy),
                DoorZ = ini.GetFloat(section, "DoorZ", dz),
                DoorHeading = ini.GetFloat(section, "DoorHeading", dh),

                InsideX = ini.GetFloat(section, "InsideX", ix),
                InsideY = ini.GetFloat(section, "InsideY", iy),
                InsideZ = ini.GetFloat(section, "InsideZ", iz),
                InsideHeading = ini.GetFloat(section, "InsideHeading", ih),
            };
        }

        /// <summary>
        /// Who works in a room: model, animation dictionary, clip, three at a time.
        ///
        /// The game does not staff these. Online they are full of people because the online
        /// script puts them there, and a room with a full crop and nobody in it reads as a
        /// place somebody left in a hurry. Every animation here is checked against the game's
        /// own list; a clip it does not have plays nothing and looks like a broken worker.
        /// </summary>
        private static DoorSpec Worked(DoorSpec door, params string[] crew)
        {
            door.Crew.AddRange(crew);
            return door;
        }

        /// <summary>
        /// The grow room at its full stage: the upgraded equipment and security, all nine
        /// stations at stage three under their lights, the chairs, the drying racks.
        ///
        /// ENTITY SETS, NOT IPLS -- see InteriorDoor.Dress. The equipment, security and
        /// light names are literal in Menyoo's own binary on this machine; the plant names
        /// follow the lights' pattern and each is offered upgrade-then-standard so the first
        /// the game accepts is the one drawn. What the game turns down is logged by name.
        /// </summary>
        private const string WeedSets =
            "weed_upgrade_equip|weed_standard_equip;" +
            "weed_security_upgrade|weed_low_security;" +
            "weed_growtha_stage3_upgrade|weed_growtha_stage3_standard;" +
            "weed_growthb_stage3_upgrade|weed_growthb_stage3_standard;" +
            "weed_growthc_stage3_upgrade|weed_growthc_stage3_standard;" +
            "weed_growthd_stage3_upgrade|weed_growthd_stage3_standard;" +
            "weed_growthe_stage3_upgrade|weed_growthe_stage3_standard;" +
            "weed_growthf_stage3_upgrade|weed_growthf_stage3_standard;" +
            "weed_growthg_stage3_upgrade|weed_growthg_stage3_standard;" +
            "weed_growthh_stage3_upgrade|weed_growthh_stage3_standard;" +
            "weed_growthi_stage3_upgrade|weed_growthi_stage3_standard;" +
            "light_growtha_stage23_upgrade|light_growtha_stage23_standard;" +
            "light_growthb_stage23_upgrade|light_growthb_stage23_standard;" +
            "light_growthc_stage23_upgrade|light_growthc_stage23_standard;" +
            "light_growthd_stage23_upgrade|light_growthd_stage23_standard;" +
            "light_growthe_stage23_upgrade|light_growthe_stage23_standard;" +
            "light_growthf_stage23_upgrade|light_growthf_stage23_standard;" +
            "light_growthg_stage23_upgrade|light_growthg_stage23_standard;" +
            "light_growthh_stage23_upgrade|light_growthh_stage23_standard;" +
            "light_growthi_stage23_upgrade|light_growthi_stage23_standard;" +
            "weed_chairs;" +
            "weed_drying;" +
            "weed_hosea;" +
            "weed_hoseb;" +
            "weed_hosec;" +
            "weed_hosed;" +
            "weed_hosee;" +
            "weed_hosef;" +
            "weed_hoseg;" +
            "weed_hoseh;" +
            "weed_hosei";

        /// <summary>
        /// The meth lab at its full stage: the upgraded lab, the security, set up and in
        /// production. Basic, upgrade and security are literal in Menyoo; setup and production
        /// are literal in the interior loader.
        /// </summary>
        private const string MethSets =
            "meth_lab_upgrade|meth_lab_basic;" +
            "meth_lab_security_high;" +
            "meth_lab_setup;" +
            "meth_lab_production";

        /// <summary>The people at work in a room, each at their own station. See DoorSpec.Posts.</summary>
        private static DoorSpec Staffed(DoorSpec door, params Post[] posts)
        {
            door.Posts.AddRange(posts);
            return door;
        }

        /// <summary>One person at one station in the lab, doing one of the cook's jobs.</summary>
        private static Post Cooking(string model, string clip, float x, float y, float z, float heading)
        {
            return new Post
            {
                Model = model,
                Dict = "anim@amb@business@meth@meth_monitoring_cooking@cooking@",
                Clip = clip,
                X = x, Y = y, Z = z, Heading = heading
            };
        }

        /// <summary>The furniture a door switches on in its room. See DoorSpec.Sets.</summary>
        private static DoorSpec Dressed(DoorSpec door, string sets)
        {
            door.Sets = sets;
            return door;
        }

        /// <summary>The IPL names a door asks for whatever the ini says. See DoorSpec.Extra.</summary>
        private static DoorSpec Named(DoorSpec door, string extra)
        {
            door.Extra = extra;
            return door;
        }

        /// <summary>
        /// The rest of the terrace: the other coordinates a door will try when the one it was
        /// given has nothing in it. See InteriorDoor.Somewhere.
        /// </summary>
        private static DoorSpec Also(DoorSpec door, params float[] xyz)
        {
            for (var i = 0; i + 2 < xyz.Length; i += 3)
            {
                door.Elsewhere.Add(new Vector3(xyz[i], xyz[i + 1], xyz[i + 2]));
            }

            return door;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
