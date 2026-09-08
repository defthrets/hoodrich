using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Keeps Franklin's front doors open, so this mod does not need another one to work.
    ///
    /// The kitchen sink is in Denise's house on Forum Drive, and cutting is not optional --
    /// weight bought by the brick will not sell until it has been through that sink. So a
    /// locked front door there is not a missing bit of scenery, it is the middle of the loop
    /// gone. And the story locks it: once Franklin moves out to Vinewood Hills the house stops
    /// being his and the door is set locked and left that way.
    ///
    /// Which made the honest answer to "why can't I cut anything?" be "install Open All
    /// Interiors" -- and a mod that needs a second mod to do the thing it is for does not work.
    ///
    /// Both houses, because there is no cost to the other one being open and no reliable way to
    /// know from here which one a given save considers home. Neither interior needs an IPL
    /// requesting: both are baked into the base map and were always there. What was never there
    /// was the way in.
    /// </summary>
    internal sealed class HouseDoors
    {
        /// <summary>
        /// One front door, at Rockstar's own coordinate for it.
        ///
        /// These are lifted from the calls in the story scripts that LOCK these doors, rather
        /// than measured by standing next to them and reading off a HUD. Same model, same
        /// point, opposite state -- which is as close to certain as this gets, and it also
        /// means we are pulling the exact lever that was pulled on us.
        /// </summary>
        private sealed class Front
        {
            public string What;
            public string Model;
            public Vector3 At;
        }

        private static readonly Front[] Fronts =
        {
            new Front
            {
                What = "Denise's, on Forum Drive",
                Model = "v_ilev_fa_frontdoor",
                At = new Vector3(-14.8689f, -1441.1821f, 31.1920f)
            },
            new Front
            {
                What = "Franklin's, up in Vinewood Hills",
                Model = "v_ilev_fh_frontdoor",
                At = new Vector3(7.5179f, 539.5260f, 176.1781f)
            }
        };

        /// <summary>Unlocked. The door system's own state 1 is what the story sets.</summary>
        private const int Unlocked = 0;

        /// <summary>
        /// How often they are put back.
        ///
        /// Reasserted rather than set once, and that is the whole design. What locks them is
        /// not a one-off either: the ambient door script holds a table of doors and enforces it
        /// on its own schedule, so a door unlocked at startup is a door locked again by the
        /// time anybody walks up to it. This is a standing instruction, not a fix.
        ///
        /// Four seconds is far below anything a player would notice and far above anything
        /// worth measuring -- it is three native calls per door.
        /// </summary>
        private const int EveryMs = 4000;

        private int _next;
        private bool _said;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _next < EveryMs) return;
            _next = now;

            foreach (var front in Fronts) Open(front);

            if (_said) return;

            _said = true;
            Log.Info("Holding the front doors unlocked: Denise's on Forum Drive, Franklin's in " +
                     "Vinewood Hills. The sink is behind one of them.");
        }

        /// <summary>
        /// Unlocks one door three ways, because there are three things that can be holding it.
        ///
        /// SET_STATE_OF_CLOSEST_DOOR_OF_TYPE is the lever the story scripts themselves use, so
        /// it is the one that answers them directly -- but it only reaches a door the game has
        /// streamed in.
        ///
        /// SET_LOCKED_UNSTREAMED_IN_DOOR_OF_TYPE covers the door before it is in memory, so it
        /// arrives already unlocked rather than arriving locked and being corrected a moment
        /// later while you are stood in front of it.
        ///
        /// And the DOOR SYSTEM is the one that actually matters here, because that is where
        /// these two are registered and it is what the ambient door script enforces from. Its
        /// hash is looked UP rather than hard-coded: the registry is keyed by a number that
        /// appears nowhere a person can read it, and a wrong one silently unlocks somebody
        /// else's door. Found by position and model, which are both things we know.
        /// </summary>
        private static void Open(Front front)
        {
            try
            {
                var model = Function.Call<int>(Hash.GET_HASH_KEY, front.Model);
                var at = front.At;

                Function.Call(Hash.SET_STATE_OF_CLOSEST_DOOR_OF_TYPE, model,
                              at.X, at.Y, at.Z, false, 0f, false);

                Function.Call(Hash.SET_LOCKED_UNSTREAMED_IN_DOOR_OF_TYPE, model,
                              at.X, at.Y, at.Z, false, 0f, 0f, 0f);

                var found = new OutputArgument();

                Function.Call(Hash.DOOR_SYSTEM_FIND_EXISTING_DOOR,
                              at.X, at.Y, at.Z, model, found);

                var door = found.GetResult<int>();

                if (door != 0)
                {
                    Function.Call(Hash.DOOR_SYSTEM_SET_DOOR_STATE, door, Unlocked, false, true);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not unlock " + front.What + ": " + ex.Message);
            }
        }
    }
}
