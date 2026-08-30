using System;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// A phone in somebody's hand, for the length of time they are looking at it.
    ///
    /// The texting animation is a man holding a phone and reading it. Without the phone it is a
    /// man staring into his own cupped palm, which is a stranger thing to watch than no
    /// animation at all -- the clip does its job perfectly and the object it is built around is
    /// missing.
    ///
    /// ONE COPY OF THIS, because there were two. The delivery had a working one and the tow
    /// got a second written from scratch a fortnight later, and they had already drifted:
    /// different model lists, and only one of them detached the prop before deleting it. Two
    /// implementations of "put a phone in his hand" is two places to fix the next thing that
    /// turns out to be wrong with it, and a coin toss over which one gets fixed.
    /// </summary>
    internal sealed class Handset
    {
        /// <summary>
        /// The handset, in the order they exist.
        ///
        /// More than one because a model that is not in a given build is not an error worth
        /// failing over -- it is a reason to try the next name. The third is the one the story
        /// missions use and is the surest of the three.
        /// </summary>
        private static readonly string[] Props =
            { "prop_npc_phone_02", "prop_npc_phone", "prop_phone_ing" };

        /// <summary>PH_R_Hand. The prop helper, so it sits where a hand actually holds a thing.</summary>
        private const int RightHandBone = 28422;

        private Prop _prop;

        /// <summary>True while there is one in his hand.</summary>
        public bool Out => _prop != null && _prop.Exists();

        /// <summary>
        /// Puts one in his hand. Does nothing if he is already holding it.
        ///
        /// A failure here is not worth reporting to the player and not worth stopping for. The
        /// animation still plays, and a man miming a text message is a smaller wrongness than
        /// a phone call that refused to happen.
        /// </summary>
        public void Show(Ped who)
        {
            if (who == null || !who.Exists()) return;
            if (Out) return;

            foreach (var name in Props)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(500)) continue;

                    _prop = World.CreateProp(model, who.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_prop == null || !_prop.Exists()) continue;

                    var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, who.Handle,
                                                  RightHandBone);

                    // No offset and no rotation. PH_R_Hand is a prop helper and already sits
                    // where a held object goes; every fiddled-in offset in this codebase has
                    // turned out to be somebody having used SKEL_R_Hand, which is the wrist
                    // and half a hand out from where the fingers close.
                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _prop.Handle, who.Handle,
                                  bone, 0f, 0f, 0f, 0f, 0f, 0f,
                                  true, true, false, true, 1, true);

                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a phone in his hand: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Takes it back. Safe to call when there was never one.
        ///
        /// DETACHED BEFORE IT IS DELETED. Deleting an entity that is still attached to a ped
        /// is the sort of thing that works right up until it does not, and the cost of getting
        /// it wrong is a phone welded to the player's hand for the rest of the session -- it
        /// survives the job, the mission and a reload of the area.
        /// </summary>
        public void Hide()
        {
            try
            {
                if (_prop != null && _prop.Exists())
                {
                    Function.Call(Hash.DETACH_ENTITY, _prop.Handle, true, true);
                    _prop.Delete();
                }
            }
            catch
            {
                // Nothing useful to do about a prop that will not go.
            }

            _prop = null;
        }
    }
}
