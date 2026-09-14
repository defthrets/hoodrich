using GTA;

namespace Hoodrich.Core
{
    /// <summary>
    /// Asking the game for a model WITHOUT STOPPING THE SCRIPT TO WAIT FOR IT.
    ///
    /// THIS IS THE SINGLE BIGGEST SOURCE OF DROPPED FRAMES IN THE MOD, and it does not look
    /// like one in the source. `model.Request(1200)` reads as "ask for this model, give up
    /// after a bit" and what it actually does is YIELD THE SCRIPT -- SHVDN hands the frame
    /// back to the game and does not return into this line until the model has arrived or the
    /// timeout has run out. Every frame in between is a frame with none of this mod's HUD on
    /// it, because the tick that draws it is the tick sat inside this call.
    ///
    /// One fixture on one corner, measured over a nine hour session:
    ///
    ///   19:43:44  yielded 85 frame(s) in 2226 ms     -- two timeouts back to back, no prop
    ///   19:44:13  yielded 41 frame(s) in 1328 ms     -- another one, still no prop
    ///   19:44:43  yielded  3 frame(s) in   86 ms     -- placed
    ///
    /// Three and a half seconds of dark HUD to put a crate down, every time the player walks
    /// back to that corner -- and the third attempt is the tell: once the model is resident it
    /// costs nothing at all. The waiting was never doing the work. The streamer loads on its
    /// own schedule regardless of who is stood over it, and all the timeout buys is the right
    /// to be stood there while it does.
    ///
    /// Across the mod: 3113 frames and 82.7 seconds of yield in one session, nearly three
    /// quarters of it from the three ambient systems that spawn and despawn as you move --
    /// fixtures, crews and walkers.
    ///
    /// SO IT ASKS AND COMES BACK. REQUEST_MODEL with no wait is a message to the streamer and
    /// returns immediately; the caller finds out next tick whether it landed. Every one of
    /// these lives inside a throttled Update that will be along again in a moment anyway, so
    /// "not yet" costs a second or two of the crate not being there -- which is a second the
    /// player spends walking towards it, rather than a second of the mod's HUD switched off.
    ///
    /// WHAT CHANGES FOR A CALLER. A list of candidate models used to be walked in preference
    /// order with each one waited on in turn. Now the first one that is ALREADY LOADED wins.
    /// On the first pass that is usually nobody, and all of them get asked for; by the next
    /// pass the preferred one has almost always arrived and is still first in the list, so
    /// preference survives in every ordinary case. Where it does not survive is a preferred
    /// model that never loads at all -- and a fallback winning is precisely what a fallback
    /// list is for.
    /// </summary>
    internal static class Streamer
    {
        /// <summary>
        /// Whether that model can be used on THIS frame, asking for it if it cannot.
        ///
        /// False is the ordinary answer the first time and is not a failure. Ask again next
        /// tick.
        ///
        /// A model that is not in this build answers false for ever and is never asked for,
        /// which is the same thing the old IsValid/IsInCdImage pair did and is why they are
        /// still here: a request for a model the game has never heard of is a request that is
        /// never satisfied, and a caller looping on it would ask once a tick until the session
        /// ended.
        /// </summary>
        public static bool Here(Model model)
        {
            try
            {
                if (!model.IsValid || !model.IsInCdImage) return false;
                if (model.IsLoaded) return true;

                // No timeout, so nothing yields. The streamer gets the message and this frame
                // carries on.
                model.Request();
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>By name, for the callers that have one rather than a Model.</summary>
        public static bool Here(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            return Here(new Model(name));
        }
    }
}
