using System;
using GTA;

namespace Hoodrich.Core
{
    /// <summary>
    /// How full the world already is, asked cheaply.
    ///
    /// THE GAME HAS POOLS AND IT DOES NOT ASK POLITELY WHEN THEY RUN OUT. Peds, vehicles and
    /// blips each come out of a fixed table sized by the install's gameconfig, and the thing
    /// that happens when one fills is not an exception a script can catch -- it is the process
    /// going away, with nothing in any log, because the crash is in the game and not in the
    /// managed side at all. Every report of it reads the same: "crashed to desktop, no error,
    /// nothing in ScriptHookVDotNet.log".
    ///
    /// A mod cannot know the ceiling: it is whatever the player's gameconfig says, and that is
    /// a file we neither ship nor read. What it CAN do is count what is out there, say so in
    /// the log while things are busy, and stop adding to it when the number is already high --
    /// which is the difference between a crash nobody can explain and a war that is a few men
    /// short.
    ///
    /// COUNTED ON A CLOCK, NOT ON DEMAND. Walking the ped pool is not free and nothing here
    /// needs a fresh answer: the count moves by ones and the decisions it feeds are about
    /// whether to spawn the next man. Twice a second is more than enough.
    /// </summary>
    internal static class Crowded
    {
        private const int EveryMs = 500;

        /// <summary>
        /// Where "the world is already full" starts, in peds.
        ///
        /// A GUESS, AND DELIBERATELY A HIGH ONE. The stock pool is 256 and every gameconfig
        /// mod raises it, so a number chosen to be safe on a stock install would hold our
        /// spawners off on a machine with room for three times as many. This is set where a
        /// stock install is genuinely in trouble and a raised one is nowhere near, and the
        /// count goes in the log either way -- so a crash report from somebody running out at
        /// a lower number arrives with the evidence in it.
        /// </summary>
        public const int PedsBusy = 190;

        private static int _at;
        private static int _peds;
        private static int _cars;

        public static int Peds { get { Look(); return _peds; } }
        public static int Vehicles { get { Look(); return _cars; } }

        /// <summary>True when the world is full enough that ours should stop adding to it.</summary>
        public static bool Busy => Peds >= PedsBusy;

        private static void Look()
        {
            int now;

            try { now = Game.GameTime; }
            catch { return; }

            if (now - _at < EveryMs && _at != 0) return;
            _at = now;

            try
            {
                _peds = World.GetAllPeds().Length;
                _cars = World.GetAllVehicles().Length;
            }
            catch
            {
                // The last answer stands. A count that cannot be taken is not a reason to
                // refuse to spawn anything ever again.
            }
        }

        /// <summary>What is out there, for a log line. See GangWar's heartbeat.</summary>
        public static string Line()
        {
            return Peds + " ped(s) and " + Vehicles + " vehicle(s) in the world" +
                   (Busy ? " -- FULL, ours are holding off" : "");
        }

        private static int _censusAt;
        private const int CensusEveryMs = 60000;

        /// <summary>
        /// Everything in the world, once a minute, at INFO.
        ///
        /// FOR THE QUESTION NOBODY CAN ANSWER FROM A LOG OF WHAT WE MEANT TO DO. "It gets
        /// laggy about ten minutes in" and "it crashed to desktop with nothing in any log"
        /// are the same report twice, and the thing that would settle both is a number that
        /// either climbs or does not. Peds, vehicles, props and blips are the four pools a
        /// mod can fill, and none of them was ever written down.
        ///
        /// A minute apart, so it is four numbers an hour rather than a wall, and cheap even
        /// so: four pool walks a minute against sixty frames a second.
        /// </summary>
        public static void Census()
        {
            int now;

            try { now = Game.GameTime; }
            catch { return; }

            if (now - _censusAt < CensusEveryMs && _censusAt != 0) return;
            _censusAt = now;

            try
            {
                var props = World.GetAllProps().Length;
                var blips = World.GetAllBlips().Length;

                _peds = World.GetAllPeds().Length;
                _cars = World.GetAllVehicles().Length;
                _at = now;

                Log.Info("World: " + _peds + " ped(s), " + _cars + " vehicle(s), " + props +
                         " prop(s), " + blips + " blip(s)." +
                         (Busy ? " Ours are holding off -- see Crowded." : ""));
            }
            catch
            {
                // A census that cannot be taken is not worth a frame.
            }
        }
    }
}
