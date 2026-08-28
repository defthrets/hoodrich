using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Paint
{
    /// <summary>
    /// Police noticing a man with a can, and what that should be worth.
    ///
    /// ONE STAR IS THE ARREST. That is not a choice this makes, it is how the game already
    /// works: at one star officers pursue and cuff, and only from two do they draw and shoot.
    /// So "arrest him, do not kill him" is not a behaviour to be written -- it is one star,
    /// held at one star, which is the part that does need writing. Left alone a chase escalates
    /// on its own the moment you run, and a tag becomes a firefight.
    ///
    /// SEEN, NOT HEARD, AND NOT MERELY NEARBY. A clear line of sight is the whole test. An
    /// officer the other side of the wall you are painting has not seen anything, and a mod
    /// that books you through a building is a mod nobody trusts the next time.
    ///
    /// THE ENGINE DOES NOT TOUCH THE WANTED LEVEL. It reports what it sees and the host decides
    /// -- because Posted Up has a counted law-hold that gang wars and bike rides already share,
    /// and a second system pushing the same natives behind its back is the exact bug that
    /// LawHold exists to have fixed once.
    /// </summary>
    internal sealed class Law
    {
        /// <summary>How far an officer can be and still be said to have seen it.</summary>
        private const float Eyes = 35f;

        /// <summary>How often to look. This is a patrol, not a tripwire.</summary>
        private const int LookMs = 500;

        /// <summary>
        /// How long a sighting stands after he stops being able to see you.
        ///
        /// Without it, ducking behind a bin for one scan drops the whole thing and the officer
        /// forgets a man he is walking towards. It is also what stops the state flapping when
        /// somebody crosses in front of him.
        /// </summary>
        private const int RemembersMs = 8000;

        /// <summary>6 is COP, 27 SWAT, 29 ARMY. The same three Bystanders leaves alone.</summary>
        private static readonly int[] Badge = { 6, 27, 29 };

        private readonly PaintConfig _cfg;

        private int _nextLook;
        private int _sawAt = int.MinValue / 2;

        public Law(PaintConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Whether an officer has this in his eyeline right now, or just did.</summary>
        public bool Watching { get; private set; }

        /// <summary>
        /// Whether he has produced an actual gun.
        ///
        /// THE EXTINGUISHER IS NOT A GUN, and it is the one the mod puts in his hands -- so the
        /// obvious test, "is he armed", books him for holding the thing he is painting with.
        /// Unarmed and the extinguisher are both fine; anything else is a weapon in front of
        /// police and stops being a vandalism stop.
        /// </summary>
        public bool Drawn { get; private set; }

        public void Update(bool spraying)
        {
            if (!_cfg.CopsCare)
            {
                Watching = false;
                Drawn = false;
                return;
            }

            var now = Game.GameTime;

            Drawn = HasGun();

            if (now < _nextLook)
            {
                Watching = now - _sawAt < RemembersMs;
                return;
            }

            _nextLook = now + LookMs;

            if (spraying && Looking()) _sawAt = now;

            Watching = now - _sawAt < RemembersMs;
        }

        /// <summary>Forgets the sighting -- for a wipe, a reload, or the mod being switched off.</summary>
        public void Forget()
        {
            _sawAt = int.MinValue / 2;
            Watching = false;
        }

        private static bool HasGun()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return false;

                var now = Function.Call<uint>(Hash.GET_SELECTED_PED_WEAPON, me.Handle);

                if (now == 0) return false;

                // Unarmed, and the can he is painting with. Everything else counts.
                if (now == Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_UNARMED")) return false;
                if (now == Function.Call<uint>(Hash.GET_HASH_KEY, Can.Weapon)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Whether any officer nearby has a clear look at him.</summary>
        private static bool Looking()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return false;

                var mine = me.Position;

                foreach (var ped in World.GetNearbyPeds(me, Eyes))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == me.Handle) continue;

                    var kind = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);

                    var law = false;
                    for (var i = 0; i < Badge.Length; i++) if (kind == Badge[i]) law = true;
                    if (!law) continue;

                    // In a car counts. A patrol car rolling past is the classic way to get
                    // caught doing this, and an officer at the wheel can see perfectly well.
                    if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                                             ped.Handle, me.Handle, 17)) continue;

                    // And roughly facing him. A clear line exists out of the back of a head as
                    // easily as out of the front, and being booked by an officer walking away
                    // is the sort of thing that reads as the mod cheating.
                    var to = mine - ped.Position;

                    if (to.LengthSquared() > 0.01f)
                    {
                        to.Normalize();

                        var face = ped.ForwardVector;

                        if (Vector3.Dot(face, to) < 0.25f) continue;
                    }

                    return true;
                }
            }
            catch
            {
                // A frame with no scan is a frame where nobody saw anything.
            }

            return false;
        }
    }
}
