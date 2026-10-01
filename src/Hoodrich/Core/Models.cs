using System.Collections.Generic;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Asking the streamer for a model without stopping the world to wait for it.
    ///
    /// Model.Request(timeout) YIELDS THE SCRIPT. That is the whole reason this exists. It is
    /// not a call that takes a while, it is a call that ENDS THE TICK and resumes on a later
    /// frame -- so everything after it in that tick simply does not happen, including every
    /// rectangle, sprite and line of text this mod draws.
    ///
    /// A rectangle in this game exists only on the frame it is issued, so a tick that yields
    /// for half a second is half a second with no phone, no toasts, no war bar and no takeover
    /// HUD. That is not a flicker in any one panel; it is all of them going out together and
    /// coming back, which is exactly what it looks like from the pavement.
    ///
    /// MEASURED, NOT REASONED ABOUT. Six hundred frames of tick pacing came back at a median of
    /// 20ms -- healthy, and running every frame -- with a MAXIMUM of 967. Nearly a full second
    /// in one tick, on a mod whose ambient spawners ask for models constantly and whose crowd
    /// spawner alone will try six models at nine hundred milliseconds each.
    ///
    /// So: ask, and answer honestly about whether it is here yet. A caller that gets false does
    /// not fail, it comes back next tick -- which for anything that runs on a timer is free,
    /// and is the difference between a spawner that costs a frame and one that costs a second
    /// of everybody's HUD.
    ///
    /// NOT FOR ONE-SHOT WORK. A mission that has one chance to put a car down would rather
    /// block than not happen, and a single stall nobody is looking for is cheaper than a job
    /// that silently does not start. Those keep the blocking form on purpose.
    /// </summary>
    internal static class Models
    {
        /// <summary>
        /// The models asked for while a room is open, so they can all be let go when it shuts.
        ///
        /// A MODEL A SCRIPT ASKS FOR STAYS IN MEMORY UNTIL THE SCRIPT SAYS IT IS DONE WITH IT,
        /// and Ready only ever asked. The gambling den asks for every card, chip, ball and reel
        /// it might deal and never said so, so one visit kept the casino's models resident for
        /// the rest of the session -- on a machine already full of a graphics mod's textures,
        /// that is the street outside going soft (texture loss with NVE, on the mod page). A
        /// model let go of takes nothing off a prop still standing; it only lets the streamer
        /// drop it once nothing is.
        /// </summary>
        private static string _scope;
        private static readonly HashSet<int> _asked = new HashSet<int>();

        /// <summary>From here, what Ready asks for is the named room's, until Out.</summary>
        public static void Into(string scope)
        {
            _scope = scope;
        }

        /// <summary>The room has shut: everything it asked for is let go of.</summary>
        public static void Out(string scope)
        {
            if (_scope != scope) return;

            _scope = null;

            foreach (var hash in _asked)
            {
                try { Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, hash); }
                catch { /* it goes when the game decides */ }
            }

            var n = _asked.Count;
            _asked.Clear();

            if (n > 0) Log.Info("Models: let go of the " + n + " the " + scope + " asked for.");
        }

        /// <summary>
        /// True when the model is loaded and ready to spawn from.
        ///
        /// Request() with no timeout is the non-blocking form: it puts the model on the
        /// streamer's list and returns immediately. Called again next tick it is usually
        /// already there, so the cost of "no" is one frame rather than one second.
        /// </summary>
        public static bool Ready(Model model)
        {
            try
            {
                if (!model.IsValid || !model.IsInCdImage) return false;

                if (model.IsLoaded) return true;

                model.Request();
                if (_scope != null) _asked.Add(model.Hash);

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
