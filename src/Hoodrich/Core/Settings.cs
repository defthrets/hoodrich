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

        /// <summary>Texture used to fill wedges. Overridable so a missing texture is a config fix, not a rebuild.</summary>
        public string WheelTextureDict = "commonmenu";
        public string WheelTexture = "gradient_bgd";

        // ---- economy -----------------------------------------------------------
        public int StartingRespect = 0;
        public float BulkPurchaseDiscountPercent = 50f;
        public float BulkSaleDiscountPercent = 25f;

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

        // ---- turf wars ---------------------------------------------------------

        /// <summary>Ceiling on how developed a zone can get. Drives payout and defender numbers.</summary>
        public int MaxTurfValue = 10;

        /// <summary>Minutes between value-creep passes over every owned zone.</summary>
        public float TurfUpgradeMinutes = 12f;

        /// <summary>Reinforcements both sides get before any bonuses.</summary>
        public int BaseKillsBeforeWarVictory = 10;

        /// <summary>Extra reinforcements per point of zone value.</summary>
        public float ExtraKillsPerTurfValue = 1.5f;

        public int BaseCostToTakeTurf = 2000;

        /// <summary>Added on top at maximum intensity; the curve between is quadratic.</summary>
        public int MaxExtraCostToTakeTurf = 30000;

        public int RewardForTakingTurf = 5000;

        /// <summary>How many fighters each side fields at once.</summary>
        public int WarMaxConcurrentPerSide = 6;

        public int WarMemberHealth = 200;
        public int WarMemberArmor = 50;
        public int WarMemberAccuracy = 35;

        /// <summary>Weapons war spawns are armed with, picked at random.</summary>
        public readonly List<string> WarWeapons = new List<string>
        {
            "WEAPON_PISTOL", "WEAPON_COMBATPISTOL", "WEAPON_MICROSMG",
            "WEAPON_PUMPSHOTGUN", "WEAPON_ASSAULTRIFLE"
        };

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
            s.WheelTextureDict = ini.GetString("Wheel", "TextureDict", s.WheelTextureDict);
            s.WheelTexture = ini.GetString("Wheel", "Texture", s.WheelTexture);

            s.StartingRespect = ini.GetInt("Economy", "StartingRespect", s.StartingRespect);
            s.BulkPurchaseDiscountPercent =
                Clamp(ini.GetFloat("Economy", "BulkPurchaseDiscountPercent", s.BulkPurchaseDiscountPercent), 0f, 90f);
            s.BulkSaleDiscountPercent =
                Clamp(ini.GetFloat("Economy", "BulkSaleDiscountPercent", s.BulkSaleDiscountPercent), 0f, 90f);
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

            s.MaxTurfValue = Math.Max(1, ini.GetInt("TurfWars", "MaxTurfValue", s.MaxTurfValue));
            s.TurfUpgradeMinutes = Math.Max(0f, ini.GetFloat("TurfWars", "TurfUpgradeMinutes", s.TurfUpgradeMinutes));
            s.BaseKillsBeforeWarVictory =
                Math.Max(1, ini.GetInt("TurfWars", "BaseKillsBeforeWarVictory", s.BaseKillsBeforeWarVictory));
            s.ExtraKillsPerTurfValue =
                Math.Max(0f, ini.GetFloat("TurfWars", "ExtraKillsPerTurfValue", s.ExtraKillsPerTurfValue));
            s.BaseCostToTakeTurf = Math.Max(0, ini.GetInt("TurfWars", "BaseCostToTakeTurf", s.BaseCostToTakeTurf));
            s.MaxExtraCostToTakeTurf =
                Math.Max(0, ini.GetInt("TurfWars", "MaxExtraCostToTakeTurf", s.MaxExtraCostToTakeTurf));
            s.RewardForTakingTurf = Math.Max(0, ini.GetInt("TurfWars", "RewardForTakingTurf", s.RewardForTakingTurf));
            s.WarMaxConcurrentPerSide =
                (int)Clamp(ini.GetInt("TurfWars", "WarMaxConcurrentPerSide", s.WarMaxConcurrentPerSide), 1f, 20f);
            s.WarMemberHealth = Math.Max(50, ini.GetInt("TurfWars", "WarMemberHealth", s.WarMemberHealth));
            s.WarMemberArmor = Math.Max(0, ini.GetInt("TurfWars", "WarMemberArmor", s.WarMemberArmor));
            s.WarMemberAccuracy =
                (int)Clamp(ini.GetInt("TurfWars", "WarMemberAccuracy", s.WarMemberAccuracy), 1f, 100f);

            s.MaxLoanAmount = Math.Max(0, ini.GetInt("Loans", "MaxLoanAmount", s.MaxLoanAmount));
            s.LoanVigPercent = Clamp(ini.GetFloat("Loans", "LoanVigPercent", s.LoanVigPercent), 0f, 100f);
            s.LoanPeriodDays = Math.Max(1, ini.GetInt("Loans", "LoanPeriodDays", s.LoanPeriodDays));
            s.LoanDefaultAfterMissed =
                Math.Max(1, ini.GetInt("Loans", "LoanDefaultAfterMissed", s.LoanDefaultAfterMissed));
            s.LoanVigGrowthPercent =
                Clamp(ini.GetFloat("Loans", "LoanVigGrowthPercent", s.LoanVigGrowthPercent), 0f, 200f);

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
