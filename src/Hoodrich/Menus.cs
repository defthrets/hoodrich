using System;

namespace Hoodrich
{
    /// <summary>
    /// Which other script has a menu up right now, or null.
    ///
    /// NOT A CONTROL, WHICH IS THE WHOLE POINT. A control-based gate cannot work here: this mod
    /// disables the very controls it would test, and which script ticks first in a frame is not
    /// something either side chooses. A variable has no frame order. Every SHVDN script lives in
    /// the one AppDomain, so its data slots are shared: a script that opens a menu sets "MenuOpen"
    /// to its own name while the menu is up, and a script with a hotkey looks here before it
    /// listens. Vehicle Tweaks sets it. The phone and the boot both read it.
    /// </summary>
    internal static class Menus
    {
        public static string Owner()
        {
            try { return AppDomain.CurrentDomain.GetData("MenuOpen") as string; }
            catch { return null; }
        }
    }
}
