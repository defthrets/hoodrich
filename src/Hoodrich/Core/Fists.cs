using Control = GTA.Control;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Every input that can throw a punch, pull a trigger or raise a weapon, in one place.
    ///
    /// THIS EXISTS BECAUSE THE LIST WAS WRITTEN OUT NINE TIMES. Every full screen in the mod
    /// held the player still by naming the controls it wanted stopped, each one typed out by
    /// hand at the top of its own lock method, and nine hand-copied lists drift. They drifted
    /// the same way: Attack, Attack2 and Aim in all of them, MeleeAttack1 in most, and in not
    /// one of them MELEE_ATTACK_LIGHT or MELEE_ATTACK_HEAVY -- which are the two inputs that
    /// actually swing a fist on foot.
    ///
    /// So every screen stopped the gun and left the hands free, and backing out of one threw a
    /// punch. It was reported, and the round of melee controls added in response was the wrong
    /// three: 142, 263 and 264, all named like they were the melee inputs, none of them the
    /// one doing it. The list looked more complete and behaved exactly the same.
    ///
    /// The two screens that never had the bug are the two that never wrote a list --
    /// GunScreen and CarScreen call DISABLE_ALL_CONTROL_ACTIONS and hand back the handful they
    /// need, so there was nothing for them to leave out. That is the lesson this class is:
    /// name the family once, and let the nine callers ask for the family.
    ///
    /// Indices are from the ScriptHookVDotNet 3 Control enum, not from memory:
    ///   24 Attack     25 Aim       50 AccurateAim  140 MeleeAttackLight  141 MeleeAttackHeavy
    ///  142 Alternate 143 Block    257 Attack2      263 MeleeAttack1      264 MeleeAttack2
    /// </summary>
    internal static class Fists
    {
        /// <summary>
        /// The whole family, including the ones that are harmless on their own.
        ///
        /// MeleeBlock and AccurateAim do not hurt anybody by themselves, but a partial list is
        /// the thing that caused this, and there is no cost to a control being disabled during
        /// a menu that the player cannot act in anyway. The vehicle pair are here because a
        /// screen can be opened at the wheel, and a drive-by started by closing a menu is the
        /// same bug wearing a seatbelt.
        /// </summary>
        private static readonly Control[] Everything =
        {
            Control.Attack,
            Control.Attack2,
            Control.Aim,
            Control.AccurateAim,
            Control.MeleeAttackLight,
            Control.MeleeAttackHeavy,
            Control.MeleeAttackAlternate,
            Control.MeleeBlock,
            Control.MeleeAttack1,
            Control.MeleeAttack2,
            Control.VehicleAttack,
            Control.VehicleAttack2,

            // Swing() carried VehicleMeleeHold and this list did not, which would have made
            // folding the two together a quiet downgrade. Its neighbours come along for the
            // same reason everything else here does.
            Control.VehicleMeleeHold,
            Control.VehicleMeleeLeft,
            Control.VehicleMeleeRight
        };

        /// <summary>
        /// Hold the lot for this frame. Called from the top of every screen's lock method.
        /// </summary>
        public static void Off()
        {
            for (var i = 0; i < Everything.Length; i++)
            {
                try
                {
                    Game.DisableControlThisFrame(Everything[i]);
                }
                catch
                {
                    // One control the running SHVDN does not know is not worth losing the rest.
                }
            }
        }

        /// <summary>
        /// Whether any of them is held right now, asked the way that still works after they
        /// have been disabled -- which by the time anything asks, they have been.
        /// </summary>
        public static bool AnyDown()
        {
            for (var i = 0; i < Everything.Length; i++)
            {
                try
                {
                    if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Everything[i]))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Same again: a control that cannot be read is not a control that is down.
                }
            }

            return false;
        }
    }
}
