using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>
    /// The smoke he breathes out.
    ///
    /// THE SAME FILE LIVES IN BARE MINIMUM, at Food/Exhale.cs, because that mod does the
    /// cigarettes and this one does the weed and neither can call into the other. It is not
    /// in tools/sync-ledger.py: the other three mods of the set have nothing to smoke, and
    /// sending a file to a mod that does not need it is the mistake Petrol already made.
    ///
    /// A MAN SMOKING WITH NOTHING COMING OUT OF HIM IS A MAN HOLDING A CIGARETTE. The
    /// animation puts it to his mouth and takes it away again and the game adds nothing,
    /// because the ambient smoking scenarios carry their own effect and a script-driven
    /// clip does not. So this does it: one small plume out of his face, once per drag.
    ///
    /// A PARTICLE NAME IS A NAME LIKE ANY OTHER AND THERE IS NO LIST OF THEM ON THIS
    /// MACHINE. The animations and the props are checked against Menyoo's dumps before
    /// they are written down -- Menyoo's PedAnimList and PropList -- and nothing dumps the
    /// particle library, so the usual discipline is not available here.
    ///
    /// WHICH IS WHY IT IS A LADDER AND WHY IT IS VERIFIED. START_PARTICLE_FX_NON_LOOPED_ON_
    /// PED_BONE returns whether it actually started, so a name that is not in the asset
    /// answers false rather than lying -- the one thing the anim dictionaries do not do.
    /// Each rung is tried until one answers true, the rung that took is remembered for the
    /// session, and the log says which. The last rung is the one this set of mods already
    /// sprays out of a paint can, so there is a floor under it that is known to work.
    /// </summary>
    internal static class Exhale
    {
        /// <summary>SKEL_Head. The plume comes out of his face, not his chest.</summary>
        private const int Head = 31086;

        /// <summary>
        /// Asset and effect, in pairs, best first.
        ///
        /// The first three are what a cigarette ought to be called if the library has one;
        /// the fourth is steam, which is white, small and PROVEN -- Overspray's extinguisher
        /// plume is this exact effect. Scaled right down it reads as breath in cold air,
        /// which is near enough to a lungful of smoke at arm's length.
        /// </summary>
        private static readonly string[] Plumes =
        {
            "core", "ent_anim_cig_smoke",
            "core", "ent_amb_smoke_general",
            "core", "exp_grd_bzgas_smoke",
            "core", "ent_sht_steam"
        };

        /// <summary>The rung that answered true, once one has. -1 until then.</summary>
        private static int _rung = -1;

        private static bool _said;
        private static bool _gaveUp;

        /// <summary>
        /// One lungful, out of his mouth, now.
        ///
        /// Called on the beat the animation lowers the cigarette, which is the beat he would
        /// be breathing out on. Costs nothing when it fails and nothing when it is off.
        /// </summary>
        public static void Now(Ped me, float scale)
        {
            if (_gaveUp || me == null || !me.Exists() || me.IsDead) return;

            try
            {
                if (_rung >= 0)
                {
                    Fire(me, Plumes[_rung], Plumes[_rung + 1], scale);
                    return;
                }

                for (var i = 0; i + 1 < Plumes.Length; i += 2)
                {
                    if (!Fire(me, Plumes[i], Plumes[i + 1], scale)) continue;

                    _rung = i;

                    if (!_said)
                    {
                        _said = true;
                        Log.Info("Exhale: " + Plumes[i] + " / " + Plumes[i + 1] + " -- that one plays.");
                    }

                    return;
                }

                // EVERY RUNG REFUSED, WHICH IS AN ANSWER. Asked again every drag would be a
                // handful of failed native calls a second for the rest of the session.
                _gaveUp = true;
                Log.Info("Exhale: none of these particle effects exist on this install, so he " +
                         "smokes without smoke. Tried: " + Names());
            }
            catch (Exception ex)
            {
                _gaveUp = true;
                Log.Debug("The exhale failed and is off for the session: " + ex.Message);
            }
        }

        private static bool Fire(Ped me, string asset, string fx, float scale)
        {
            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, asset);

            // NOT WAITED ON. The asset lands in a frame or two and the next drag is seconds
            // away; blocking the frame for a puff of smoke is the wrong trade.
            if (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, asset)) return false;

            Function.Call(Hash.USE_PARTICLE_FX_ASSET, asset);

            // Grey, and half see-through. The effects behind these names are white steam and
            // yellow gas; smoke is neither, and both take a colour.
            Function.Call(Hash.SET_PARTICLE_FX_NON_LOOPED_COLOUR, 0.72f, 0.72f, 0.74f);
            Function.Call(Hash.SET_PARTICLE_FX_NON_LOOPED_ALPHA, 0.5f);

            var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, Head);

            // A little in front of his mouth and a little below the eyes. The head bone sits
            // inside the skull, so a plume at zero starts behind his own face.
            return Function.Call<bool>(Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE,
                                       fx, me.Handle,
                                       0.0f, 0.11f, 0.02f,
                                       0f, 0f, 0f,
                                       bone, scale, false, false, false);
        }

        private static string Names()
        {
            var sb = new System.Text.StringBuilder();

            for (var i = 0; i + 1 < Plumes.Length; i += 2)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Plumes[i]).Append('/').Append(Plumes[i + 1]);
            }

            return sb.ToString();
        }
    }
}
