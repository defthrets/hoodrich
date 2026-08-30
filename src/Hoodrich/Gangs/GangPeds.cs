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
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

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
        /// be somebody having used SKEL_R_Hand, which is the wrist joint and half a hand out
        /// from where the fingers close.
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

        /// <summary>PH_R_Hand, the prop helper.</summary>
        private const int RightHand = 28422;
    }
}
