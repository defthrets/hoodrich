using System;
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

        /// <summary>Largest loan, before rank scaling.</summary>
        public int MaxLoanAmount = 25000;

        /// <summary>Interest charged per period, as a percent of the principal.</summary>
        public float LoanVigPercent = 15f;

        /// <summary>In-game days between vig payments.</summary>
        public int LoanPeriodDays = 7;

        /// <summary>Missed periods before the crew stops asking nicely.</summary>
        public int LoanDefaultAfterMissed = 3;

        /// <summary>How much the vig grows each time it is missed.</summary>
        public float LoanVigGrowthPercent = 25f;

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

            s.MaxLoanAmount = Math.Max(0, ini.GetInt("Loans", "MaxLoanAmount", s.MaxLoanAmount));
            s.LoanVigPercent = Clamp(ini.GetFloat("Loans", "LoanVigPercent", s.LoanVigPercent), 0f, 100f);
            s.LoanPeriodDays = Math.Max(1, ini.GetInt("Loans", "LoanPeriodDays", s.LoanPeriodDays));
            s.LoanDefaultAfterMissed =
                Math.Max(1, ini.GetInt("Loans", "LoanDefaultAfterMissed", s.LoanDefaultAfterMissed));
            s.LoanVigGrowthPercent =
                Clamp(ini.GetFloat("Loans", "LoanVigGrowthPercent", s.LoanVigGrowthPercent), 0f, 200f);

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

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
