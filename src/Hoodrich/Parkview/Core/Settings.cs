// GENERATED -- DO NOT EDIT. This is Parkview, copied in by tools/sync-parkview.py from
// C:\projects\parkview\src\Parkview\Core\Settings.cs. Change it there; the next build overwrites this.
using System.Windows.Forms;

namespace Hoodrich.Parkview.Core
{
    /// <summary>
    /// Everything Parkview.ini says. Every value has a default here, so a missing ini is a
    /// working install rather than a broken one.
    /// </summary>
    internal sealed class Settings
    {
        public bool Enabled = true;
        public LogLevel LogLevel = LogLevel.Info;

        /// <summary>Whether the scenes are built at all.</summary>
        public bool Scenery = true;

        /// <summary>Whether Menyoo's own Spooner folder is read as well as the mod's. For making scenes.</summary>
        public bool SceneryFromMenyoo = false;

        /// <summary>How near a scene has to be before it is stood up, in metres.</summary>
        public float SceneryRange = 220f;

        /// <summary>Whether the people live a little. See Scenery, "Living a little".</summary>
        public bool SceneryLife = true;

        /// <summary>
        /// A name over everybody near, all the time. OFF: Michael found it wrong on 2026-09-24 --
        /// names up over people he could not talk to. NPC Mind shows the name over the one man
        /// you are looking at, with the prompt to talk to him. See Scenery.Tags.
        /// </summary>
        public bool SceneryNames = false;

        /// <summary>The hours, on the game's clock, that nobody is stood about a scene. The same number in both means never.</summary>
        public int SceneryQuietFrom = 3;
        public int SceneryQuietTo = 7;

        /// <summary>Whether the doors on the block can be rented. See Rooms.</summary>
        public bool Rooms = true;

        /// <summary>What a room costs for a week on the game's clock.</summary>
        public int RoomRent = 250;

        // ---- keys ------------------------------------------------------------------------
        public Keys CaptureKey = Keys.F9;
        public Keys CaptureModifier = Keys.None;
        public Keys ReloadKey = Keys.F9;
        public Keys ReloadModifier = Keys.Shift;
        public Keys HideKey = Keys.F9;
        public Keys HideModifier = Keys.Control;
        public Keys RoomKey = Keys.F9;
        public Keys RoomModifier = Keys.Alt;

        /// <summary>
        /// Take over the map prop you are looking at. NUMPAD * because the F9 family is
        /// full -- F9, Shift, Control and Alt are all spoken for -- and F10 is Five0
        /// Patrol's, F11 and F12 are Bare Minimum's, F7 is Rampage's. Multiply is one of
        /// the four keys the hotkey map lists as bound to nothing at all.
        /// </summary>
        public Keys TakeKey = Keys.Multiply;
        public Keys TakeModifier = Keys.None;

        public static Settings Load()
        {
            var s = new Settings();
            var ini = IniFile.Load(Paths.Ini);

            s.Enabled = ini.GetBool("General", "Enabled", s.Enabled);
            s.LogLevel = ini.GetEnum("General", "LogLevel", s.LogLevel);

            s.Scenery = ini.GetBool("Scenery", "Enabled", s.Scenery);
            s.SceneryFromMenyoo = ini.GetBool("Scenery", "FromMenyoo", s.SceneryFromMenyoo);
            s.SceneryRange = Clamp(ini.GetFloat("Scenery", "Range", s.SceneryRange), 40f, 600f);
            s.SceneryLife = ini.GetBool("Scenery", "PedsLive", s.SceneryLife);
            s.SceneryNames = ini.GetBool("Scenery", "Names", s.SceneryNames);
            s.SceneryQuietFrom = (int)Clamp(ini.GetInt("Scenery", "QuietFrom", s.SceneryQuietFrom), 0f, 23f);
            s.SceneryQuietTo = (int)Clamp(ini.GetInt("Scenery", "QuietTo", s.SceneryQuietTo), 0f, 23f);

            s.Rooms = ini.GetBool("Rooms", "Enabled", s.Rooms);
            s.RoomRent = (int)Clamp(ini.GetInt("Rooms", "RentPerWeek", s.RoomRent), 0f, 100000f);

            s.CaptureKey = ini.GetKey("Keys", "Capture", s.CaptureKey);
            s.CaptureModifier = ini.GetKey("Keys", "CaptureModifier", s.CaptureModifier);
            s.ReloadKey = ini.GetKey("Keys", "Reload", s.ReloadKey);
            s.ReloadModifier = ini.GetKey("Keys", "ReloadModifier", s.ReloadModifier);
            s.HideKey = ini.GetKey("Keys", "Hide", s.HideKey);
            s.HideModifier = ini.GetKey("Keys", "HideModifier", s.HideModifier);
            s.RoomKey = ini.GetKey("Keys", "SetRoom", s.RoomKey);
            s.RoomModifier = ini.GetKey("Keys", "SetRoomModifier", s.RoomModifier);
            s.TakeKey = ini.GetKey("Keys", "Take", s.TakeKey);
            s.TakeModifier = ini.GetKey("Keys", "TakeModifier", s.TakeModifier);

            return s;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
