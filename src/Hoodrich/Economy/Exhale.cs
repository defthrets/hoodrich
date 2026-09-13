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
    /// clip does not. So this does it: one lungful out of his face, once per drag.
    ///
    /// THE NAMES ARE THE GAME'S OWN AND THEY ARE NOT GUESSES ANY MORE. The first version of
    /// this file said no list of particles existed on this machine and wrote a ladder of
    /// plausible names instead. There is a list: Menyoo.asi carries the whole library as
    /// plain strings, a pretty label followed by the real name, assets and effects together
    /// -- the same trick that gives us PedAnimList and PropList, in a binary rather than a
    /// text file. ent_anim_cig_exhale_mth is in it, and so is the asset it lives in.
    ///
    /// AND AN EFFECT BELONGS TO AN ASSET, which is the thing that version got wrong. It
    /// asked for every name out of "core" -- three of them were real and none of them were
    /// core's, so all three were refused and the ladder fell through to a fire extinguisher.
    /// That is what the spray was, and the hiss with it, because a ptfx carries its own
    /// audio. They are named in pairs now.
    ///
    /// AND AN ASSET THAT IS NOT IN MEMORY YET IS NOT AN ASSET THAT IS MISSING. The same
    /// version judged a rung on the frame it asked for it, so whichever asset happened to be
    /// resident already won every time -- which is the whole reason a cigarette ended up
    /// spraying foam. A rung now keeps its place while its asset streams, and is only given
    /// up when the game has it and still refuses the effect.
    /// </summary>
    internal static class Exhale
    {
        /// <summary>SKEL_Head. The plume comes out of his face, not his chest.</summary>
        private const int Head = 31086;

        /// <summary>Puffs a rung waits for its asset before it is written off.</summary>
        private const int WaitPuffs = 4;

        /// <summary>
        /// Asset and effect, in pairs, best first. Every one of these is in Menyoo's list.
        ///
        /// scr_mp_cig is the cigarette asset and it holds all three of the game's own
        /// smoking effects; ent_anim_cig_exhale_mth is the one that comes out of a mouth,
        /// which is exactly what this is for. The linger underneath it is the ambient one
        /// that hangs in the air after somebody has smoked.
        ///
        /// NOTHING LOUD AND NOTHING SPRAYED. There is deliberately no fallback to a steam
        /// or gas effect: those are jets, they are noisy, and a man exhaling a fire
        /// extinguisher is worse than a man exhaling nothing.
        /// </summary>
        private static readonly string[] Plumes =
        {
            "scr_mp_cig", "ent_anim_cig_exhale_mth",
            "scr_mp_cig", "ent_anim_cig_smoke",
            "core",       "ent_amb_cig_smoke_linger"
        };

        /// <summary>Which pair is being tried, or is known to work once _settled.</summary>
        private static int _rung;
        private static int _waited;

        private static bool _settled;
        private static bool _gaveUp;

        /// <summary>
        /// One lungful, out of his mouth, now.
        ///
        /// Called on the beat the animation lowers the cigarette, which is the beat he would
        /// be breathing out on. Costs nothing when it fails and nothing when it is off.
        /// </summary>
        public static void Now(Ped me)
        {
            if (_gaveUp || me == null || !me.Exists() || me.IsDead) return;

            try
            {
                var asset = Plumes[_rung];
                var fx = Plumes[_rung + 1];

                Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, asset);

                if (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, asset))
                {
                    // STREAMING IS NOT FAILING. It lands in a frame or two; this puff goes
                    // without. Only after several drags is the asset itself in doubt.
                    if (++_waited < WaitPuffs) return;

                    Log.Info("Exhale: " + asset + " never arrived, so " + fx + " cannot be tried.");
                    Next();
                    return;
                }

                if (Fire(me, asset, fx))
                {
                    if (!_settled)
                    {
                        _settled = true;
                        Log.Info("Exhale: " + asset + " / " + fx + " -- that one plays.");
                    }

                    return;
                }

                // THE GAME HAS THE ASSET AND WILL NOT PLAY THE EFFECT, which is the only
                // answer that means the name is wrong.
                Log.Info("Exhale: " + asset + " is loaded and " + fx + " does not play out of it.");
                Next();
            }
            catch (Exception ex)
            {
                _gaveUp = true;
                Log.Debug("The exhale failed and is off for the session: " + ex.Message);
            }
        }

        private static void Next()
        {
            _waited = 0;
            _rung += 2;

            if (_rung + 1 < Plumes.Length) return;

            _gaveUp = true;
            Log.Info("Exhale: none of the game's smoking effects would play on this install, " +
                     "so he smokes without smoke. Nothing else is tried on purpose -- the " +
                     "alternatives are jets with a hiss on them.");
        }

        private static bool Fire(Ped me, string asset, string fx)
        {
            Function.Call(Hash.USE_PARTICLE_FX_ASSET, asset);

            var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, Head);

            // FORWARD AND A LITTLE DOWN, which is where his mouth is. The head bone sits
            // inside the skull, so a plume at zero starts behind his own face.
            //
            // Scale is one. These are the game's own cigarette effects, cut for a cigarette
            // at a mouth -- the version that scaled to a sixth was sizing down an
            // extinguisher, which is not a thing that needs doing to this.
            return Function.Call<bool>(Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE,
                                       fx, me.Handle,
                                       0.0f, 0.09f, -0.02f,
                                       0f, 0f, 0f,
                                       bone, 1.0f, false, false, false);
        }
    }
}
