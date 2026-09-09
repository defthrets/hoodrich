using System;

namespace Hoodrich.Api
{
    /// <summary>
    /// What another mod is allowed to know about what is happening on the block tonight.
    ///
    /// ONE QUESTION, REALLY: is there a takeover on, and where. It exists because a takeover
    /// is the one thing this mod puts in the street that another mod can ruin by not knowing
    /// about it. Five0 Patrol keeps police on the streets around you; a takeover is a junction
    /// full of people that ENDS with police, hours later, as its last beat. Those two are the
    /// same street and opposite instructions, and neither side could see the other -- so a
    /// patrol car came round the corner twenty minutes in and finished a thing that had barely
    /// started.
    ///
    /// IT IS THE SAME SHAPE AS Api/Drugs, deliberately: one pattern on this machine rather
    /// than two. Only BCL types cross -- bool, float, float[] -- because the caller reaches
    /// this by REFLECTION and holds no reference to this assembly, so it cannot name Vector3.
    /// mscorlib is the one assembly both mods are guaranteed to agree about.
    ///
    /// NOTHING THROWN LEAVES THIS FILE. An exception crossing a reflection call arrives at the
    /// other end as a TargetInvocationException wrapping a type the caller does not have.
    ///
    /// IT IS SAFE BEFORE HOODRICH HAS STARTED. SHVDN builds scripts in whatever order it finds
    /// them, so the other mod can call in before Wire has run. Everything answers "no" until
    /// Ready, and the caller is expected to keep asking.
    /// </summary>
    public static class Block
    {
        /// <summary>
        /// The contract version. Bumped when a signature here changes in a way that breaks.
        /// Read by the caller BEFORE anything else.
        /// </summary>
        public static int ApiVersion => 2;

        /// <summary>Hoodrich's own version string, for the other side's log.</summary>
        public static string Version
        {
            get { try { return Core.Build.Version; } catch { return "?"; } }
        }

        private static Locations.Takeover _takeover;
        private static Func<bool> _war;

        /// <summary>
        /// Called by Main once these exist. Not for outside use.
        ///
        /// THE WAR COMES THROUGH AS A QUESTION RATHER THAN AN OBJECT, because Main builds these
        /// in an order this file should not have to know, and a reference taken a moment too
        /// early is a null that never fixes itself.
        /// </summary>
        internal static void Wire(Locations.Takeover takeover, Func<bool> war)
        {
            _takeover = takeover;
            _war = war;
        }

        internal static void Unwire()
        {
            _takeover = null;
            _war = null;
        }

        /// <summary>Whether Hoodrich is here AND has finished starting up.</summary>
        public static bool Ready
        {
            get
            {
                try { return _takeover != null; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Whether a gang war is being fought right now.
        ///
        /// THE OTHER THING ON THIS BLOCK THAT POLICE RUIN. A war is a set-piece fight between
        /// two gangs that the player is in the middle of; a patrol car arriving, a wanted level
        /// climbing off the gunfire, and officers opening up on everybody is three separate
        /// systems answering an event that is not theirs. Five0 Patrol asks this so it can take
        /// its police out of it entirely until the fight is over.
        /// </summary>
        public static bool WarRunning
        {
            get
            {
                try { return _war != null && _war(); }
                catch { return false; }
            }
        }

        /// <summary>
        /// Whether there is a takeover on the street right now that is not yet over.
        ///
        /// RUNNING ONLY, NOT SCATTERING. Scattering IS the ending -- it is what happens when
        /// the blue lights arrive -- so a mod keeping its police away until this goes false is
        /// keeping them away until the takeover is complete, which is the whole request. Once
        /// it is scattering, police turning up is the thing that is supposed to be happening.
        /// </summary>
        public static bool TakeoverRunning
        {
            get
            {
                try
                {
                    return _takeover != null &&
                           _takeover.State == Locations.TakeoverState.Running;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Where it is -- x, y, z. An empty array when there is not one on.</summary>
        public static float[] TakeoverAt
        {
            get
            {
                try
                {
                    if (!TakeoverRunning) return new float[0];

                    var at = _takeover.Where;

                    return new[] { at.X, at.Y, at.Z };
                }
                catch
                {
                    return new float[0];
                }
            }
        }

        /// <summary>How far out the ring goes, in metres. Nought when there is not one on.</summary>
        public static float TakeoverRing
        {
            get
            {
                try { return TakeoverRunning ? _takeover.Reach : 0f; }
                catch { return 0f; }
            }
        }
    }
}
