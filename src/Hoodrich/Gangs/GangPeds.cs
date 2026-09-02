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
        /// Something in his hand, held the way a hand holds it.
        ///
        /// PH_R_Hand at zero offset. Every fiddled-in offset in this codebase has turned out to
        /// be somebody having used the wrong bone, so the fix is the bone and never the offset.
        ///
        /// THIS COMMENT WAS TRUE AND THE CODE WAS NOT. It said PH_R_Hand and the constant below
        /// it said 28422, which is IK_R_Hand -- so every bottle in the mod hung off the wrist,
        /// unrotated, while the file explained at length that it did not. Worth remembering the
        /// next time a comment is used as evidence that something works.
        /// </summary>
        public static Prop Hand(Ped who, string propName)
        {
            if (who == null || !who.Exists() || string.IsNullOrEmpty(propName)) return null;

            try
            {
                var model = new Model(propName);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(500)) return null;

                var prop = World.CreateProp(model, who.Position, false, false);
                model.MarkAsNoLongerNeeded();

                if (prop == null || !prop.Exists()) return null;

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, who.Handle, RightHand);

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
        /// The bone a held prop hangs off.
        ///
        /// PH_R_Hand, not IK_R_Hand. This was 28422 -- the IK_R_Hand bone -- which is the
        /// TARGET the animation system aims the hand at, not the hand's grip. A bottle
        /// attached there with no offset sits at the wrist, unrotated, pointing along the
        /// world axes rather than along the fist, which is why they were carrying beer at an
        /// angle nobody has ever held a beer at.
        ///
        /// 60309 is PH_R_Hand -- the prop-holder bone, which exists for exactly this and is
        /// already positioned and rotated in the grip. Attached to it at zero offset the
        /// bottle sits where the game's own scenarios put one, with no magic numbers to tune
        /// per prop.
        /// </summary>
        private const int RightHand = 60309;
    }
}
