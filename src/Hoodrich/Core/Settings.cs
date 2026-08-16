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
