using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// One of theirs, stood on the ground.
    ///
    /// THREE FILES WERE ABOUT TO DO THIS. The yard makes them, the rollers make them in a car
    /// seat, and the walking crews needed them on a pavement -- and the common part of all
    /// three is the same ten lines: pick a model off the set's list, request it, create the
    /// ped, and mark it as ours in every sense the game has (persistent, mission entity, the
    /// set's relationship group).
    ///
    /// The differences are all AFTER that, and they stay where they are. What a man does once
    /// he exists is the business of whoever asked for him.
    /// </summary>
    internal static class GangPeds
    {
        /// <summary>Civilian. The ped type every one of ours is created as.</summary>
        private const int PedTypeCiv = 4;

        /// <summary>
        /// Makes one on foot, or returns null.
        ///
        /// The models are tried in the order given and the first that loads wins, because a
        /// model missing from a given install is a reason to use the next name rather than a
        /// reason to have nobody on the street.
        /// </summary>
        public static Ped OnFoot(GangDef gang, IEnumerable<string> models, Vector3 at, float heading)
        {
            if (gang == null || models == null) return null;

            foreach (var name in models)
            {
                try
                {
                    if (string.IsNullOrEmpty(name)) continue;

                    var model = new Model(name);
                    // Non-blocking. Request(timeout) yields the whole script and blanks every
                    // panel the mod draws for as long as it waits -- see Core.Models. A caller
                    // that gets nothing here asks again on its next tick.
                    if (!Core.Models.Ready(model)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                    at.X, at.Y, at.Z, heading, false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                    return ped;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put one of theirs on the ground: " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Something in his hand, held the way a hand holds it: on the hand's prop bone, at
        /// zero offset. Every fiddled-in offset in this codebase has turned out to be somebody
        /// having used the wrong bone, so the fix is the bone and never the offset -- and the
        /// bone has been got wrong in this file in both directions by reading bone tables, so
        /// the constants below say what was actually seen.
        /// </summary>
        public static Prop Hand(Ped who, string propName, bool left = false)
        {
            if (who == null || !who.Exists() || string.IsNullOrEmpty(propName)) return null;

            try
            {
                var model = new Model(propName);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(500)) return null;

                var prop = World.CreateProp(model, who.Position, false, false);
                model.MarkAsNoLongerNeeded();

                if (prop == null || !prop.Exists()) return null;

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, who.Handle, left ? LeftHand : RightHand);

                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, prop.Handle, who.Handle, bone,
                              0f, 0f, 0f, 0f, 0f, 0f, true, true, false, true, 1, true);

                return prop;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put something in his hand: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// One of yours notices you go past.
        ///
        /// THE SET NEVER ACKNOWLEDGED THE PLAYER AT ALL, which is a strange thing about a mod
        /// whose whole subject is belonging to it. You could drive the length of your own
        /// block, past your own people, and not one of them would look up -- they were set
        /// dressing that happened to be wearing your colours.
        ///
        /// He looks at you first, always. The look is most of the effect: a man who turns his
        /// head as you pass has noticed you whether or not the rest of it lands, and it is one
        /// call that cannot fail.
        /// </summary>
        public static void Notice(Ped man, Ped you, int lookMs = 2500)
        {
            if (man == null || !man.Exists() || !man.IsAlive) return;
            if (you == null || !you.Exists()) return;

            try
            {
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, man.Handle, you.Handle, lookMs, 0, 2);
            }
            catch
            {
                // He does not look. Nothing else here depends on it.
            }
        }

        /// <summary>
        /// And says something.
        ///
        /// Forced, because these are ambient lines and the game will otherwise decide a man
        /// stood on a pavement has nothing worth saying. A context this build has not got is
        /// silence rather than an error -- speech names are safe to try in a way that native
        /// hashes are not.
        /// </summary>
        public static void Hello(Ped man, Random rng)
        {
            if (man == null || !man.Exists() || !man.IsAlive) return;

            try
            {
                // His own set near Franklin has all speech blocked, to stop the game's
                // wrong-neighbourhood lines; a greeting lifts it for the one line, the way
                // the block's own chatter does, or it comes out as a quiet nod every time.
                Function.Call(Hash.BLOCK_ALL_SPEECH_FROM_PED, man.Handle, false, false);
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, man.Handle,
                              Greetings[rng.Next(Greetings.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // Quiet nod, then.
            }
        }

        /// <summary>
        /// A wave, for the ones on foot.
        ///
        /// The MP celebration wave, tried in order, because it is the one animation in the game
        /// that is unambiguously a person waving at another person. Upper-body only and not
        /// looping, so it plays over whatever he is already doing and he goes back to it --
        /// a man who stops walking to wave has made an event of it.
        /// </summary>
        public static bool Wave(Ped man, Random rng)
        {
            if (man == null || !man.Exists() || !man.IsAlive) return false;

            var start = rng.Next(Waves.Length);

            for (var i = 0; i < Waves.Length; i++)
            {
                var pair = Waves[(start + i) % Waves.Length];

                try
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, pair[0]);

                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pair[0])) continue;

                    // Flag 48 is upper-body plus secondary: it runs alongside the walk instead
                    // of replacing it, which is what makes it a wave rather than a stop.
                    Function.Call(Hash.TASK_PLAY_ANIM, man.Handle, pair[0], pair[1],
                                  4f, -4f, 2200, 48, 0f, false, false, false);

                    return true;
                }
                catch
                {
                    // Next one.
                }
            }

            return false;
        }

        /// <summary>Two taps on the horn, which is how a car says hello.</summary>
        public static void Beep(Vehicle car)
        {
            if (car == null || !car.Exists()) return;

            try
            {
                // Short, and only ever one from here -- the second tap is the caller's, a beat
                // later, because a horn held for four hundred milliseconds is somebody leaning
                // on it and that means something else entirely.
                Function.Call(Hash.START_VEHICLE_HORN, car.Handle, 180, 0, false);
            }
            catch
            {
                // No horn, no hello.
            }
        }

        private static readonly string[] Greetings =
        {
            "GENERIC_HI", "GENERIC_HOWS_IT_GOING", "GENERIC_HI",
            "GENERIC_WHATS_UP", "GENERIC_HOWS_IT_GOING"
        };

        private static readonly string[][] Waves =
        {
            new[] { "anim@mp_player_intcelebrationmale@wave", "wave" },
            new[] { "friends@frj@ig_1", "wave_a" },
            new[] { "gestures@m@standing@casual", "gesture_hello" }
        };

        /// <summary>
        /// The bone a held prop hangs off: PH_R_Hand, 28422, the right hand's prop-holder.
        ///
        /// It is the bone the game's own scenario props -- the beer, the cigarette, the phone
        /// -- are authored to sit on at zero offset, and the hand the drinking, smoking and
        /// texting clips use. It was 60309 for a while on the strength of a bone table, and
        /// 60309 is PH_L_Hand, the same bone on the OTHER hand: a beer put there was in the
        /// left hand of a man whose clip drinks with his right, mirrored, which is why the
        /// walkers were carrying theirs at an angle nobody has ever held a beer at. A can put
        /// there for the tagger came out in the wrong hand too, which is how this was found.
        /// </summary>
        private const int RightHand = 28422;

        /// <summary>
        /// What a caller asking for the left hand gets: 58868, the root of the right hand's
        /// middle finger. The tagger's can was moved here to get it into the hand his clip
        /// sprays with, it looked right on screen, and a thing that looks right on screen is
        /// not moved to satisfy a table. PH_L_Hand itself is 60309, for a clip that really
        /// does hold something on that side.
        /// </summary>
        private const int LeftHand = 58868;
    }
}
