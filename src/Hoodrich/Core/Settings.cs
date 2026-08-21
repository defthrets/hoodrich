using System;
using Hoodrich.Locations;
using GTA;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Hoodrich.Core
{
    /// <summary>How the Hoodrich wheel is opened.</summary>
    internal enum WheelMode
    {
        /// <summary>Take over the vanilla weapon-wheel control. Holding it opens Hoodrich instead.</summary>
        Replace,

        /// <summary>Leave the vanilla wheel alone; open Hoodrich from its own key.</summary>
        Separate
    }

    /// <summary>
    /// How wheel segments are filled. Wedge is the real weapon-wheel look and needs a
    /// streamed texture dict; Node needs nothing but DRAW_RECT and always works.
    /// </summary>
    internal enum WheelRenderMode
    {
        /// <summary>True arc wedges, built from rotated sprites.</summary>
        Wedge,

        /// <summary>Rectangular cards arranged in a ring. Dependency-free fallback.</summary>
        Node,

        /// <summary>Wedge, falling back to Node if the texture dict will not stream.</summary>
        Auto
    }

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

        // ---- wheel -------------------------------------------------------------
        public WheelMode WheelMode = WheelMode.Replace;
        public Keys WheelKey = Keys.B;
        public Keys WheelModifier = Keys.None;
        public bool HoldToOpen = true;
        public WheelRenderMode RenderMode = WheelRenderMode.Auto;

        /// <summary>Time scale while the wheel is open. 1.0 disables the slowdown.</summary>
        public float WheelTimeScale = 0.25f;

        public bool BlurBackground = true;
        public string TimecycleModifier = "hud_def_blur";

        /// <summary>Ring geometry as a fraction of screen height.</summary>
        public float InnerRadius = 0.085f;
        public float OuterRadius = 0.20f;

        /// <summary>Stick/mouse magnitude below which nothing is highlighted.</summary>
        public float DeadZone = 0.25f;

        public float MouseSensitivity = 1.0f;
        public bool PlaySounds = true;

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
        public bool BlipsInBars = true;

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

        /// <summary>Seconds the game's own weapon wheel is held open after picking Weapons.</summary>
        public int VanillaWheelSeconds = 5;

        // ---- economy -----------------------------------------------------------
        public float BulkPurchaseDiscountPercent = 50f;

        /// <summary>Grams that must be sold before a corner dealer will name his source.</summary>
        public float DocksUnlockGrams = 50f;

        // ---- market ------------------------------------------------------------

        /// <summary>Minutes between street-price drift steps. 0 freezes the market.</summary>
        public float MarketDriftIntervalMinutes = 5f;

        /// <summary>How far either side of the base price a product can wander, as a percent.</summary>
        public float MarketMaxSwingPercent = 45f;

        // ---- risk --------------------------------------------------------------

        /// <summary>Base chance a completed sale draws police attention.</summary>
        public float PoliceBustChancePercent = 12f;

        /// <summary>How long an undercover buyer takes to call it in -- your window to react.</summary>
        public float UndercoverCallSeconds = 6f;

        /// <summary>Get this far from the deal before the call lands and you are clear.</summary>
        public float UndercoverEscapeDistance = 40f;

        public int BustWantedStars = 2;

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
        public float HideoutStashCapacity = 5000f;

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

        // ---- joining -----------------------------------------------------------

        /// <summary>Grams a gang leader fronts you when he takes you on.</summary>
        public float LeaderFrontGrams = 20f;

        public static Settings Load()
        {
            var s = new Settings();
            var ini = IniFile.Load(Paths.Ini);

            s.Enabled = ini.GetBool("General", "Enabled", s.Enabled);
            s.LogLevel = ini.GetEnum("General", "LogLevel", s.LogLevel);
            s.SaveIntervalSeconds = ini.GetInt("General", "SaveIntervalSeconds", s.SaveIntervalSeconds);
            s.PauseDuringMission = ini.GetBool("General", "PauseDuringMission", s.PauseDuringMission);

            s.WheelMode = ini.GetEnum("Wheel", "Mode", s.WheelMode);
            s.WheelKey = ini.GetKey("Wheel", "Key", s.WheelKey);
            s.WheelModifier = ini.GetKey("Wheel", "Modifier", s.WheelModifier);
            s.HoldToOpen = ini.GetBool("Wheel", "HoldToOpen", s.HoldToOpen);
            s.RenderMode = ini.GetEnum("Wheel", "RenderMode", s.RenderMode);
            s.WheelTimeScale = Clamp(ini.GetFloat("Wheel", "TimeScale", s.WheelTimeScale), 0.05f, 1f);
            s.BlurBackground = ini.GetBool("Wheel", "BlurBackground", s.BlurBackground);
            s.TimecycleModifier = ini.GetString("Wheel", "TimecycleModifier", s.TimecycleModifier);
            s.InnerRadius = Clamp(ini.GetFloat("Wheel", "InnerRadius", s.InnerRadius), 0.02f, 0.35f);
            s.OuterRadius = Clamp(ini.GetFloat("Wheel", "OuterRadius", s.OuterRadius), 0.05f, 0.48f);
            s.DeadZone = Clamp(ini.GetFloat("Wheel", "DeadZone", s.DeadZone), 0f, 0.9f);
            s.MouseSensitivity = Clamp(ini.GetFloat("Wheel", "MouseSensitivity", s.MouseSensitivity), 0.1f, 5f);
            s.TweetsOnTheRight = ini.GetBool("Socials", "TweetsOnTheRight", s.TweetsOnTheRight);
            s.BlipsInBars = ini.GetBool("Wheel", "BlipsInBars", s.BlipsInBars);

            // One block per door, all read the same way. Adding a third room is a section in
            // the ini and a line here, not another class.
            s.Doors.Add(ReadDoor(ini, "GrowRoom", "grow room", "bkr_biker_dlc_int_ware02",
                                 BlipSprite.Weed,
                                 -201.384f, -1707.909f, 32.664f, 313.362f,
                                 1039.000f, -3098.000f, -39.000f, 180f));

            // radar_crim_drugs, 51. The generic one, and the right one: the room is the meth
            // lab interior dressed as a crack den, so a cocaine leaf named the wrong product
            // and there is no crack sprite to name the right one.
            s.Doors.Add(ReadDoor(ini, "CrackDen", "crack den", "tr_tuner_methlab_1",
                                 (BlipSprite)51,
                                 -105.053f, -1408.631f, 29.673f, 226.934f,
                                 1000.000f, -3200.000f, -38.000f, 180f));
            s.PlaySounds = ini.GetBool("Wheel", "PlaySounds", s.PlaySounds);
            s.VanillaWheelSeconds = (int)Clamp(ini.GetInt("Wheel", "VanillaWheelSeconds", s.VanillaWheelSeconds), 1f, 30f);

            s.BulkPurchaseDiscountPercent =
                Clamp(ini.GetFloat("Economy", "BulkPurchaseDiscountPercent", s.BulkPurchaseDiscountPercent), 0f, 90f);
            s.DocksUnlockGrams = Math.Max(0f, ini.GetFloat("Economy", "DocksUnlockGrams", s.DocksUnlockGrams));
            s.MarketDriftIntervalMinutes =
                Math.Max(0f, ini.GetFloat("Economy", "MarketDriftIntervalMinutes", s.MarketDriftIntervalMinutes));
            s.MarketMaxSwingPercent =
                Clamp(ini.GetFloat("Economy", "MarketMaxSwingPercent", s.MarketMaxSwingPercent), 0f, 80f);

            s.PoliceBustChancePercent =
                Clamp(ini.GetFloat("Risk", "PoliceBustChancePercent", s.PoliceBustChancePercent), 0f, 100f);
            s.UndercoverCallSeconds =
                Clamp(ini.GetFloat("Risk", "UndercoverCallSeconds", s.UndercoverCallSeconds), 1f, 60f);
            s.UndercoverEscapeDistance =
                Clamp(ini.GetFloat("Risk", "UndercoverEscapeDistance", s.UndercoverEscapeDistance), 5f, 300f);
            s.BustWantedStars = (int)Clamp(ini.GetInt("Risk", "BustWantedStars", s.BustWantedStars), 1f, 5f);
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

            // An inner radius at or past the outer one would render nothing at all.
            if (s.InnerRadius >= s.OuterRadius - 0.02f)
            {
                Log.Warn("Wheel InnerRadius >= OuterRadius; falling back to defaults for both.");
                s.InnerRadius = 0.085f;
                s.OuterRadius = 0.20f;
            }

            Log.Level = s.LogLevel;
            Log.Info("Settings loaded: mode=" + s.WheelMode + " render=" + s.RenderMode +
                     " key=" + s.WheelKey + " timescale=" + s.WheelTimeScale);
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
