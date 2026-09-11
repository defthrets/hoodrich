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
        /// <summary>
        /// What the player reads. English is the code's own; the rest are files in
        /// scripts\Hoodrich\lang, and anything a file does not cover shows in English. See Lang.
        /// </summary>
        public Language Language = Language.English;

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
        public Keys RescueKey = Keys.Divide;
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
        /// Per-speaker level on top of VoiceVolume, keyed by the front of the file name.
        /// [Voice] Level.lamar=0.68 and so on; see the ini for the measured reasons.
        /// </summary>
        public readonly Dictionary<string, float> VoiceLevels =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

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
        /// Whether the feed says anything on screen at all.
        ///
        /// OFF IS GENUINELY OFF, and that is the point of it being separate from the setting
        /// above. TweetsOnTheRight only chooses WHICH side of the screen a post lands on --
        /// asked for by somebody who wanted neither. With this false nothing is drawn and
        /// nothing goes into the game's notification stack, so nothing makes a noise either.
        /// The timeline still fills, so it is all there to read in the phone whenever you want
        /// it; it simply stops narrating itself at you.
        /// </summary>
        public bool FeedOnScreen = true;

        /// <summary>
        /// A key that turns that on and off where you stand, and writes the answer to the ini.
        ///
        /// F4 by default. It used to be NumPad *, under a comment claiming that was "one of the
        /// few keys this game and the mods people run alongside it leave alone" -- which was
        /// not true here. Fumes reads the same key at a pump to cycle the hose rope, so one
        /// press toggled the feed AND rebuilt the hose, and it never showed up in anybody's
        /// keymap because Fumes binds it in code rather than in its ini.
        ///
        /// F4 is beside the phone on F2, which is the other thing that opens a screen, and it
        /// is genuinely free: every key bound by every mod on this install was mapped, and F4,
        /// F7, F8, F9 and NumPad 1-9 were all that was left. No numpad key is safe -- Fumes has
        /// Multiply and NumPad0 and Franklin-RP has NumPad0.
        ///
        /// Set it to None and there is no key -- the tick on the settings screen does the same
        /// job.
        /// </summary>
        public Keys FeedKey = Keys.F4;

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
        /// <summary>
        /// WHAT A MARK IS MADE OF, and why these four are in this mod's ini at all.
        ///
        /// The paint engine is shared verbatim with the standalone, and the standalone has its
        /// own ini with two dozen knobs in it -- but it STANDS DOWN when this mod is installed,
        /// because both of them running means two cans in his hand and two decals for every
        /// one you meant. So on any machine with both, the engine that is actually painting is
        /// this one, and until now nothing in a config file could reach it.
        ///
        /// Only these four, not the two dozen. These are the ones that change what a tag LOOKS
        /// LIKE rather than how it behaves, and they are the ones worth an evening on a wall.
        /// Everything else stays where it is: in PaintConfig, one copy, tuned in code.
        /// </summary>
        public int PaintTexture = 1030;
        public int PaintTextureDark = 1010;
        public int PaintMixTexture;
        public int PaintMixEvery;
        public float PaintColourGain = 1f;
        public float PaintOpacity = 1f;
        public float PaintOpacityFar = 0.6f;

        /// <summary>
        /// Where the bag sits in a buyer's hand, from the [Dealing] section.
        ///
        /// Three floats in metres, in the HAND BONE'S own space rather than the world's, which
        /// is why they are a setting: nobody can work out from a text editor which way is down
        /// on a wrist that is halfway through a handshake. Nudge one, press Insert, look.
        /// </summary>
        public float BaggieX = -0.010f;
        public float BaggieY = 0.010f;
        public float BaggieZ = -0.025f;

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
        public float ColdCallMinutes = 10f;

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
        /// Whether the Luber picker shows you the place while you are choosing it: a slow
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

        /// <summary>
        /// What a mask off the counter at Vespucci costs.
        ///
        /// Once per mask, per body -- see PlayerState.Masks. Cheap on purpose: the point of the
        /// shop is that you can afford to change your face before a job, not that it is another
        /// thing to grind for.
        /// </summary>
        /// <summary>
        /// Whether a body you made gets a headstone on the map, and for how long.
        ///
        /// Bodies are lootable, and a body you have walked away from is a body you will not
        /// find again -- a ped on the ground is invisible from six feet in this game. The mark
        /// expires because the point of it is "that just happened, over there", which stops
        /// being useful about as fast as the body stops being interesting.
        /// </summary>
        /// <summary>
        /// The bench in front of Stretch that a gun is laid on while you look at it.
        ///
        /// A COORDINATE CAN ONLY HONESTLY COME FROM SOMEBODY STANDING ON IT, which is the same
        /// argument the doors make. These are a guess an arm's length in front of him at about
        /// table height: put a table down, stand where the middle of it is, and write what the
        /// settings screen tells you in here.
        ///
        /// The camera is NOT a setting. It is worked out from these -- back along the heading,
        /// up a little, looking down at the top -- because a camera position is the one thing
        /// nobody can judge while standing still. See UI.GunModel.
        /// </summary>
        public float BenchX = -128.147f;
        public float BenchY = -1461.071f;
        public float BenchZ = 33.701f;

        /// <summary>
        /// Which side the camera stands on, NOT which way the bench points.
        ///
        /// A hundred and fifteen degrees is the way Stretch is facing, which is the side a
        /// customer stands on -- so the camera looks at the crate over the gun rather than
        /// through the back of his head.
        /// </summary>
        public float BenchHeading = 115f;

        public bool KillMarks = true;
        public int KillMarkSeconds = 50;

        /// <summary>
        /// How long a body you made is held against the game's own clean-up, so it is still
        /// there when you come back for what is on it. Nought turns it off. See UI.Graves.
        /// </summary>
        public int KeepBodiesMinutes = 10;

        public int MaskPrice = 350;

        public int MaskSlot = 8;
        public bool MaskAsProp;
        public int MaskDrawable = 4;
        public int MaskTexture = 0;

        /// <summary>Percent of product dropped as a recoverable bag when you die.</summary>
        public float LoseOnDeathPercent = 100f;

        /// <summary>Whether that bag is left where you fell to go back for. Off, and it is gone for good.</summary>
        public bool DeathBagRecoverable = true;

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

        /// <summary>The lowriders, out once a night on the Families' blocks. See Gangs.Cruise.</summary>
        public bool CruiseEnabled = true;

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
        public int RollerPearl = 4;

        /// <summary>
        /// Which of the game's ropes a dog lead is made of.
        ///
        /// CLAMPED, AND THE CLAMP IS THE POINT. ADD_ROPE's type is an unvalidated index into
        /// the game's rope table -- hand it one past the end and the engine reads off the end
        /// of the array and the process dies, with no managed exception and nothing in the
        /// log. Eight did exactly that on this machine while the fuel mod was being built.
        /// Nought to seven are inside the table; four is the thinnest of them that reads as a
        /// lead rather than as a tow rope.
        /// </summary>
        public int LeashRope = 4;

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

        /// <summary>
        /// How many of those crews are walking a dog, as a percentage.
        ///
        /// A quarter by default. A dog is a thing you notice, and a thing you notice every
        /// single time is scenery -- three men and a dog is a street, four crews all walking
        /// dogs is a park. Nought turns them off entirely.
        /// </summary>
        public int WalkerDogs = 25;

        /// <summary>
        /// What Franklin's own bike is, in place of the Bagger the game gives him, and which
        /// of its liveries it wears. Blank, or "bagger", leaves the game's own.
        /// </summary>
        /// <summary>
        /// Which dog Trigger is.
        ///
        /// Any of the eleven the game has a petting animation for -- see Trigger.Breeds. The
        /// small ones are a_c_pug, a_c_pug_02, a_c_westy and a_c_poodle; a_c_retriever and
        /// a_c_shepherd are middling; a_c_rottweiler, a_c_rottweiler_02, a_c_chop, a_c_chop_02
        /// and a_c_husky are the big ones. A name that is not on that list, or one this build
        /// has not got, falls back to the list in Trigger.
        /// </summary>
        public string TriggerBreed = "a_c_rottweiler";

        public string FranklinsBike = "sanchez";
        public int FranklinsBikeLivery = 0;

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
            s.Language = ini.GetEnum("General", "Language", s.Language);
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

            // The phone's own keys. There is no wheel any more and no old ini to read one from.
            s.VanillaPhoneSeconds =
                (int)Clamp(ini.GetInt("Phone", "VanillaPhoneSeconds", s.VanillaPhoneSeconds), 1f, 30f);

            s.PhoneKey = ini.GetKey("Phone", "Key", s.PhoneKey);
            s.RescueKey = ini.GetKey("General", "RescueKey", s.RescueKey);
            s.PhoneModifier = ini.GetKey("Phone", "Modifier", s.PhoneModifier);

            s.WheelTimeScale = Clamp(ini.GetFloat("Phone", "TimeScale", s.WheelTimeScale), 0.05f, 1f);
            s.BlurBackground = ini.GetBool("Phone", "BlurBackground", s.BlurBackground);
            s.TimecycleModifier = ini.GetString("Phone", "TimecycleModifier", s.TimecycleModifier);
            s.TweetsOnTheRight = ini.GetBool("Socials", "TweetsOnTheRight", s.TweetsOnTheRight);
            s.FeedOnScreen = ini.GetBool("Socials", "FeedOnScreen", s.FeedOnScreen);
            s.FeedKey = ini.GetKey("Socials", "FeedKey", s.FeedKey);
            s.BlipsInBars = ini.GetBool("PostUp", "BlipsInBars", s.BlipsInBars);
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

            s.BaggieX = Clamp(ini.GetFloat("Dealing", "BaggieX", s.BaggieX), -0.3f, 0.3f);
            s.BaggieY = Clamp(ini.GetFloat("Dealing", "BaggieY", s.BaggieY), -0.3f, 0.3f);
            s.BaggieZ = Clamp(ini.GetFloat("Dealing", "BaggieZ", s.BaggieZ), -0.3f, 0.3f);

            // The paint engine's texture knobs. See PaintTexture.
            s.PaintTexture = ini.GetInt("Paint", "CanDecal", s.PaintTexture);
            s.PaintTextureDark = ini.GetInt("Paint", "CanDecalDark", s.PaintTextureDark);
            s.PaintMixTexture = ini.GetInt("Paint", "MixDecal", s.PaintMixTexture);
            s.PaintMixEvery = (int)Clamp(ini.GetInt("Paint", "MixEvery", s.PaintMixEvery), 0f, 100f);
            s.PaintColourGain = Clamp(ini.GetFloat("Paint", "CanColourGain", s.PaintColourGain), 0.1f, 8f);
            s.PaintOpacity = Clamp(ini.GetFloat("Paint", "Opacity", s.PaintOpacity), 0.1f, 1f);
            s.PaintOpacityFar = Clamp(ini.GetFloat("Paint", "OpacityFar", s.PaintOpacityFar), 0f, 1f);
            s.BagRoll = ini.GetFloat("Dealing", "BagRoll", s.BagRoll);
            s.BagYaw = ini.GetFloat("Dealing", "BagYaw", s.BagYaw);

            // One block per door, all read the same way. Adding a third room is a section in
            // the ini and a line here, not another class.
            // NO DOORS OF ITS OWN ANY MORE.
            //
            // The grow room and the pill press were the two the mod shipped, and both were
            // removed: they sent people into buried DLC interiors, two players reported the
            // game locking up going in or out, and four rounds of work never established
            // which of three things was wrong -- the ipl never streaming, the coordinate
            // being inside a wall, or the room not being in the build at all. A room nobody
            // can prove is there is not worth the crash.
            //
            // The machinery stays, because it was never the problem: any door anybody wants
            // is a section in Hoodrich.ini with two coordinates in it, and the settings screen
            // will write those down for you while you stand on them.

            // ANY NUMBER OF MORE DOORS, named in the ini rather than in here.
            //
            // The two above are the mod's own. Everything else -- the casino, a club, a
            // shop, whatever somebody wants a way into -- is a section in Hoodrich.ini and
            // its name listed under [Doors] More. Nothing about a door is code: it is two
            // coordinates and a name, and the two coordinates can only honestly come from
            // somebody standing on them. See the two rows on the settings screen that write
            // them down.

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

            s.PlaySounds = ini.GetBool("Phone", "PlaySounds", s.PlaySounds);

            s.VoiceEnabled = ini.GetBool("Voice", "Enabled", s.VoiceEnabled);
            s.VoiceVolume = Clamp(ini.GetFloat("Voice", "Volume", s.VoiceVolume), 0f, 1f);
            s.VoiceRepeat = ini.GetBool("Voice", "RepeatLines", s.VoiceRepeat);

            // Level.<prefix>=0..1, read off the whole section rather than by a list of
            // names, so a new speaker is one ini line and no code.
            foreach (var kv in ini.Section("Voice"))
            {
                if (!kv.Key.StartsWith("Level.", StringComparison.OrdinalIgnoreCase)) continue;

                var who = kv.Key.Substring(6).Trim();
                float level;

                if (who.Length == 0 ||
                    !float.TryParse(kv.Value, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out level))
                {
                    continue;
                }

                s.VoiceLevels[who] = Clamp(level, 0f, 1f);
            }

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
            // THE OLD SECTION STILL COUNTS. It was called Knowai until the app was, and
            // an ini already on somebody's disk keeps its heading -- the deploy never
            // overwrites one. Read the new name first and fall back to what they have,
            // so a rename costs nobody their setting.
            s.RidePreview = ini.GetBool("LUber", "Preview",
                                        ini.GetBool("Knowai", "Preview", s.RidePreview));

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

            s.BenchX = ini.GetFloat("Guns", "BenchX", s.BenchX);
            s.BenchY = ini.GetFloat("Guns", "BenchY", s.BenchY);
            s.BenchZ = ini.GetFloat("Guns", "BenchZ", s.BenchZ);
            s.BenchHeading = ini.GetFloat("Guns", "BenchHeading", s.BenchHeading);

            s.KillMarks = ini.GetBool("Map", "KillMarks", s.KillMarks);
            s.KillMarkSeconds = (int)Clamp(ini.GetInt("Map", "KillMarkSeconds", s.KillMarkSeconds), 0f, 600f);
            s.KeepBodiesMinutes = (int)Clamp(ini.GetInt("Map", "KeepBodiesMinutes", s.KeepBodiesMinutes), 0f, 60f);

            s.MaskPrice = (int)Clamp(ini.GetInt("Mask", "Price", s.MaskPrice), 0f, 100000f);
            s.MaskSlot = (int)Clamp(ini.GetInt("Mask", "Slot", s.MaskSlot), 0f, 11f);
            s.MaskAsProp = ini.GetBool("Mask", "AsProp", s.MaskAsProp);
            s.MaskDrawable = (int)Clamp(ini.GetInt("Mask", "Drawable", s.MaskDrawable), 0f, 400f);
            s.MaskTexture = (int)Clamp(ini.GetInt("Mask", "Texture", s.MaskTexture), 0f, 64f);
            s.LoseOnDeathPercent =
                Clamp(ini.GetFloat("Risk", "LoseOnDeathPercent", s.LoseOnDeathPercent), 0f, 100f);
            s.DeathBagRecoverable = ini.GetBool("Risk", "DeathBagRecoverable", s.DeathBagRecoverable);
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
            s.CruiseEnabled = ini.GetBool("Block", "CruiseEnabled", s.CruiseEnabled);
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
            s.WalkerCrews = (int)Clamp(ini.GetInt("Block", "WalkerCrews", s.WalkerCrews), 0f, 8f);
            s.WalkerDogs = (int)Clamp(ini.GetInt("Block", "WalkerDogs", s.WalkerDogs), 0f, 100f);

            s.TriggerBreed = (ini.GetString("Cars", "TriggerBreed", s.TriggerBreed) ?? "").Trim();
            s.FranklinsBike = (ini.GetString("Cars", "FranklinsBike", s.FranklinsBike) ?? "").Trim();
            s.FranklinsBikeLivery = (int)Clamp(ini.GetInt("Cars", "FranklinsBikeLivery", s.FranklinsBikeLivery), 0f, 30f);

            // Never read until now. It was declared, documented and written into the ini, and
            // nothing ever loaded it -- so the number in the file did nothing at all.
            s.RollerPearl = (int)Clamp(ini.GetInt("Block", "RollerPearl", s.RollerPearl), 0f, 160f);
            s.LeashRope = (int)Clamp(ini.GetInt("Block", "LeashRope", s.LeashRope), 0f, 7f);

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

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
