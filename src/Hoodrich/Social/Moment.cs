using System;
using GTA;
using GTA.Native;

namespace Hoodrich.Social
{
    /// <summary>
    /// What time it actually is, and what the sky is actually doing.
    ///
    /// WHY THIS EXISTS. {timeofday} is the fourth most-used slot in socials.json -- 231 lines
    /// carry it -- and it was a flat list of ten phrases picked at random. So the feed would
    /// tell you somebody got taken off the corner "at like 3am" while you stood there in the
    /// two o'clock sun, and "last night" at half past eight in the morning. {weathertalk} was
    /// the same: "Rain finally" in a cloudless sky.
    ///
    /// That is the whole of situational awareness in one slot. The feed already knows WHERE
    /// you are -- {here} and {street} are wired to the player through Main -- and it has never
    /// known WHEN. A post that gets the hour wrong is worse than a generic one, because a
    /// generic line is merely bland and a wrong one is visibly a machine.
    ///
    /// THE BUCKETS ARE THE DECISION AND THEY ARE PURE. The two natives are read in Now and
    /// Sky and nowhere else; everything that decides anything takes a plain int or a plain
    /// string, so it can be compiled into a console harness and run against every hour of the
    /// day without the game. See feedback_verify_logic_offline.
    /// </summary>
    internal static class Moment
    {
        // ======================================================================
        // The clock
        // ======================================================================

        /// <summary>
        /// Which part of the day an hour belongs to.
        ///
        /// The names are the keys under "moments" in socials.json, so adding a phrase to a
        /// time of day is a data edit and never a code one.
        /// </summary>
        public static string Part(int hour)
        {
            // Anything outside a day is somebody else's bug, not a reason to crash.
            if (hour < 0 || hour > 23) return "afternoon";

            if (hour < 5) return "latenight";
            if (hour < 8) return "earlymorning";
            if (hour < 12) return "morning";
            if (hour < 14) return "midday";
            if (hour < 18) return "afternoon";
            if (hour < 22) return "evening";

            return "night";
        }

        /// <summary>The hour on the game clock, or -1 if it cannot be had.</summary>
        public static int Hour()
        {
            try
            {
                return Function.Call<int>(Hash.GET_CLOCK_HOURS);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>The part of the day it is right now, or empty if the clock is unreadable.</summary>
        public static string Now()
        {
            var hour = Hour();
            return hour < 0 ? "" : Part(hour);
        }

        // ======================================================================
        // The sky
        // ======================================================================

        /// <summary>
        /// Which kind of weather a game weather name is, for the purposes of somebody
        /// complaining about it.
        ///
        /// THE NAMES ARE READ OFF THE ENUM, NOT REMEMBERED. The sixteen the game has are
        /// ExtraSunny, Clear, Clouds, Smog, Foggy, Overcast, Raining, ThunderStorm, Clearing,
        /// Neutral, Snowing, Blizzard, Snowlight, Christmas, Halloween and Unknown -- taken
        /// out of ScriptHookVDotNet3.dll by reflection rather than typed from memory, because
        /// a name that is nearly right here silently sends every post to the fallback.
        ///
        /// Matched on the string rather than the enum on purpose: a build of SHVDN that adds
        /// or renames one cannot stop this compiling, and an unfamiliar name lands on a clear
        /// day rather than on nothing.
        ///
        /// Clouds and Overcast get their own bucket because in this city they are not
        /// "bad weather", they are the marine layer -- grey until noon and gone by two, which
        /// locals have a name for and complain about in a completely different tone from rain.
        /// </summary>
        public static string SkyFor(string weather)
        {
            if (string.IsNullOrEmpty(weather)) return "clear";

            switch (weather.Trim().ToLowerInvariant())
            {
                case "extrasunny":
                    return "hot";

                case "clouds":
                case "overcast":
                    return "grey";

                case "smog":
                    return "smog";

                case "foggy":
                    return "fog";

                case "raining":
                case "thunderstorm":
                    return "rain";

                case "snowing":
                case "blizzard":
                case "snowlight":
                case "christmas":
                    return "snow";

                case "clear":
                case "clearing":
                case "neutral":
                case "halloween":
                case "unknown":
                    return "clear";

                default:
                    return "clear";
            }
        }

        /// <summary>What the sky is doing right now, or empty if it cannot be had.</summary>
        public static string Sky()
        {
            try
            {
                return SkyFor(World.Weather.ToString());
            }
            catch
            {
                return "";
            }
        }
    }
}
