using GTA;

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

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
