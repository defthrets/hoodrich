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
        /// Everything asked for and not yet let go of: when it was last asked for, and which
        /// room asked, if one did.
        ///
        /// A MODEL A SCRIPT ASKS FOR STAYS IN MEMORY UNTIL THE SCRIPT SAYS IT IS DONE WITH IT,
        /// and Ready and Streamer.Here only ever asked. Every spawner in the mod walks a list
        /// of candidates and spawns from the first one that is in -- and the rest of the list,
        /// asked for along the way, stayed pinned for the session. The takeover's Theme asked
        /// for its whole wardrobe on the way in, a hundred and thirty-odd models, and nothing
        /// ever told the streamer it could have them back: from the first takeover of a night
        /// the street went soft and stayed soft, which is the texture loss on the mod page
        /// (audit, 2026-10-04). The gambling den was the one place that let go, and only of
        /// its own.
        ///
        /// So everything asked for is written down, and Settle lets go of whatever nobody has
        /// asked for in a while. That costs nothing a standing prop or a ped is using -- the
        /// entity holds its model, and SET_MODEL_AS_NO_LONGER_NEEDED only lets the streamer
        /// drop it once nothing does -- and a spawner that still wants one simply asks again,
        /// which is what it does every pass anyway. A room (Into/Out) keeps what it asked for
        /// until it shuts: the den's cards are wanted for the whole hand.
        /// </summary>
        private sealed class Ask
        {
            public int At;
            public string Scope;
        }

        private static readonly Dictionary<int, Ask> _asked = new Dictionary<int, Ask>();
        private static string _scope;

        /// <summary>How long an ask is honoured after the last time anybody made it.</summary>
        private const int HoldMs = 20000;

        private const int SettleEveryMs = 5000;
        private static int _nextSettle;

        /// <summary>Reused between sweeps: what to let go of this time.</summary>
        private static readonly List<int> _letGo = new List<int>();

        /// <summary>From here, what is asked for is the named room's, until Out.</summary>
        public static void Into(string scope)
        {
            _scope = scope;
        }

        /// <summary>The room has shut: everything it asked for is let go of.</summary>
        public static void Out(string scope)
        {
            if (_scope != scope) return;

            _scope = null;

            _letGo.Clear();
            foreach (var pair in _asked)
            {
                if (pair.Value.Scope == scope) _letGo.Add(pair.Key);
            }
            foreach (var hash in _letGo) LetGo(hash);

            if (_letGo.Count > 0) Log.Info("Models: let go of the " + _letGo.Count + " the " + scope + " asked for.");
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
                Asked(model.Hash);

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Written down as asked for, by Ready and by Streamer.Here. Asked for again, the
        /// clock restarts; asked for inside a room, it is the room's until the room shuts.
        /// </summary>
        public static void Asked(int hash)
        {
            Ask ask;
            if (_asked.TryGetValue(hash, out ask))
            {
                ask.At = Game.GameTime;
                if (_scope != null) ask.Scope = _scope;
                return;
            }

            _asked[hash] = new Ask { At = Game.GameTime, Scope = _scope };
        }

        /// <summary>
        /// Every few seconds, from the main tick: whatever nobody has asked for in HoldMs is
        /// let go of, unless the room that asked for it is still open.
        /// </summary>
        public static void Settle()
        {
            var now = Game.GameTime;
            if (now < _nextSettle) return;
            _nextSettle = now + SettleEveryMs;

            if (_asked.Count == 0) return;

            _letGo.Clear();
            foreach (var pair in _asked)
            {
                if (pair.Value.Scope != null && pair.Value.Scope == _scope) continue;
                if (now - pair.Value.At < HoldMs) continue;
                _letGo.Add(pair.Key);
            }
            foreach (var hash in _letGo) LetGo(hash);

            if (_letGo.Count > 0)
            {
                Log.Debug("Models: let go of " + _letGo.Count + " nobody has asked for in " + (HoldMs / 1000) +
                          " s; " + _asked.Count + " still asked for.");
            }
        }

        private static void LetGo(int hash)
        {
            _asked.Remove(hash);

            try { Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, hash); }
            catch { /* it goes when the game decides */ }
        }
    }
}
