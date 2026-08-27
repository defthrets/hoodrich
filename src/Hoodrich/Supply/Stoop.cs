using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;

namespace Hoodrich.Supply
{
    /// <summary>
    /// The three men stood around a dealer's corner.
    ///
    /// A plug alone on a pavement is a shop with one employee and no queue. Three of his own
    /// people near him -- one watching the street, one smoking, one drinking -- is a corner,
    /// and it is the difference between arriving somewhere and arriving at somebody's spot.
    ///
    /// THEY WILL NOT FIGHT FRANKLIN, EVER, and that is not left to the relationship between
    /// his crew and theirs. They get their own relationship group which is neutral to the
    /// player in both directions, so the Ballas' corner works exactly the same whether you are
    /// at war with the Ballas or wearing their colours. A man you can buy from should not have
    /// friends who open fire on the way in.
    ///
    /// They are not statues either. Combat is switched off and fleeing is switched on, so a
    /// gunshot or a car through the fence scatters them like anybody else -- they just scatter
    /// rather than draw. And the guard drifts around his patch on a wander, so the corner does
    /// not read as three mannequins every time you come back.
    /// </summary>
    internal sealed class Stoop
    {
        /// <summary>Standing about, in three different ways.</summary>
        private static readonly string[] Doing =
        {
            // Watching the street. GUARD_STAND is the one that actually faces outward.
            "WORLD_HUMAN_GUARD_STAND",

            // The joint. SMOKING_POT is a different animation to SMOKING and it is the one
            // with the hand near the mouth and the slouch -- SMOKING is a cigarette.
            "WORLD_HUMAN_SMOKING_POT",

            // The beer.
            "WORLD_HUMAN_DRINKING"
        };

        /// <summary>How far off his corner each of them stands.</summary>
        private static readonly float[] Out = { 2.6f, 3.4f, 3.0f };
        private static readonly float[] Round = { 40f, 165f, 275f };

        /// <summary>
        /// How far the one on watch will drift.
        ///
        /// Small on purpose. He is minding a corner, not going for a walk, and a guard who
        /// wanders thirty metres is a man who has left.
        /// </summary>
        private const float DriftRadius = 7f;

        /// <summary>Everybody's models, per set. Three different faces on every corner.</summary>
        private static readonly Dictionary<string, string[]> Faces =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "aztecas",   new[] { "g_m_y_azteca_01", "g_m_y_mexgoon_02", "a_m_y_mexthug_01" } },
                { "ballas",    new[] { "g_m_y_ballasout_01", "g_m_y_ballaeast_01", "g_m_y_ballaorig_01" } },
                { "vagos",     new[] { "g_m_y_mexgoon_01", "g_m_y_mexgoon_03", "g_m_y_mexgang_01" } },
                { "marabunta", new[] { "g_m_y_salvagoon_01", "g_m_y_salvagoon_02", "g_m_y_salvagoon_03" } },
                { "lost",      new[] { "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_lost_03" } },
                { "triads",    new[] { "g_m_m_chigoon_01", "g_m_m_chigoon_02", "g_m_m_chicold_01" } },
                { "armenians", new[] { "g_m_y_armgoon_02", "g_m_m_armlieut_01", "g_m_m_armboss_01" } },
                { "koreans",   new[] { "g_m_y_korean_01", "g_m_y_korean_02", "g_m_y_korlieut_01" } },
            };

        /// <summary>
        /// For the three who answer to nobody.
        ///
        /// Merle, Yusuf and Poncho have no set, so there is no colour for their people to be
        /// wearing. They get whoever is actually stood around in that part of town.
        /// </summary>
        private static readonly string[] NoSet =
        {
            "a_m_y_soucent_01", "a_m_y_soucent_02", "a_m_m_soucent_03"
        };

        private readonly List<Ped> _crew = new List<Ped>();
        private readonly Random _rng = new Random();

        private string _forDealer = "";
        private int _group;
        private bool _groupMade;

        /// <summary>Who is currently stood out, if anybody.</summary>
        public string For { get { return _forDealer; } }

        /// <summary>
        /// A group of their own, neutral to the player both ways.
        ///
        /// Made once and reused. ADD_RELATIONSHIP_GROUP hands back the same hash for the same
        /// name, so calling it twice is harmless -- but the relationships only need setting
        /// once and doing it every spawn would be sixty pointless natives a corner.
        /// </summary>
        private int Group()
        {
            if (_groupMade) return _group;

            try
            {
                var slot = new OutputArgument();
                Function.Call(Hash.ADD_RELATIONSHIP_GROUP, "HOODRICH_STOOP", slot);
                _group = slot.GetResult<int>();

                var player = Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER");

                // 3 is neutral. Both directions, because one of them being neutral while the
                // other hates you is exactly how a man ends up shooting at somebody who is not
                // shooting back.
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 3, _group, player);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 3, player, _group);

                _groupMade = true;
            }
            catch
            {
                _group = 0;
            }

            return _group;
        }

        /// <summary>Puts three of his people round him. Does nothing if they are already there.</summary>
        public void Gather(DealerDef def, Vector3 at, float heading)
        {
            if (def == null) return;
            if (string.Equals(_forDealer, def.Id, StringComparison.OrdinalIgnoreCase) && Alive() > 0) return;

            Scatter();

            var faces = Pool(def);
            if (faces.Length == 0) return;

            for (var i = 0; i < Doing.Length; i++)
            {
                // A different face each, walking the list rather than picking at random --
                // random three from three is the same man twice about half the time.
                var name = faces[i % faces.Length];

                var spot = Beside(at, heading, Out[i], Round[i]);
                var ped = Put(name, spot, heading + 180f);

                if (ped == null) continue;

                Settle(ped, Doing[i], spot, i == 0);
                _crew.Add(ped);
            }

            _forDealer = def.Id;

            Log.Debug("Stoop: " + _crew.Count + " out with " + def.Id + ".");
        }

        /// <summary>Which faces belong on this corner.</summary>
        private static string[] Pool(DealerDef def)
        {
            var gang = def.GangId ?? "";

            string[] faces;
            return Faces.TryGetValue(gang, out faces) ? faces : NoSet;
        }

        /// <summary>A spot beside him, out at an angle from where he is facing.</summary>
        private static Vector3 Beside(Vector3 at, float heading, float outBy, float round)
        {
            var a = (heading + round) * (Math.PI / 180.0);

            return new Vector3(at.X + (float)Math.Sin(a) * -outBy,
                               at.Y + (float)Math.Cos(a) * outBy,
                               at.Z);
        }

        private Ped Put(string name, Vector3 at, float heading)
        {
            try
            {
                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage) return null;
                if (!model.Request(1200)) return null;

                var ped = World.CreatePed(model, at, heading);
                model.MarkAsNoLongerNeeded();

                if (ped == null || !ped.Exists()) return null;

                Function.Call(Hash.SET_PED_AS_ENEMY, ped.Handle, false);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                ped.IsPersistent = true;

                var group = Group();
                if (group != 0) Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, group);

                return ped;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>What he does, and what he will not do.</summary>
        private void Settle(Ped ped, string doing, Vector3 at, bool watches)
        {
            try
            {
                // NEVER STARTS ANYTHING, ALWAYS LEAVES.
                //
                // 5 is BF_AlwaysFight and 17 is BF_AlwaysFlee, read off the native list rather
                // than remembered. 58 is BF_DisableFleeFromCombat, which has to go OFF or the
                // fleeing they are told to do is the fleeing they are forbidden from doing.
                // 46 lets an unarmed man square up to an armed one, which is the exact scene
                // nobody wants outside a shop.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 58, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 17, true);

                // And they can be frightened, which is the other half of not being statues.
                // Blocking non-temporary events would have made them ignore gunfire entirely
                // -- calm, which is worse than hostile.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);

                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, true);

                if (watches)
                {
                    // The one on watch drifts. Small radius: he is minding a corner, not going
                    // for a walk, and the wander is what stops three men standing in exactly
                    // the same three places every single visit.
                    Function.Call(Hash.TASK_WANDER_IN_AREA, ped.Handle,
                                  at.X, at.Y, at.Z, DriftRadius, 4f, 8f);
                    return;
                }

                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, doing, 0, true);
            }
            catch
            {
                // He stands there doing nothing, which is still a man on a corner.
            }
        }

        private int Alive()
        {
            var n = 0;

            for (var i = 0; i < _crew.Count; i++)
            {
                var ped = _crew[i];
                if (ped != null && ped.Exists() && ped.IsAlive) n++;
            }

            return n;
        }

        /// <summary>
        /// Hands them back when the dealer goes.
        ///
        /// Released rather than deleted, the same way everything else in the mod lets a ped go
        /// -- somebody vanishing in front of you is worse than somebody wandering off.
        /// </summary>
        public void Scatter()
        {
            for (var i = 0; i < _crew.Count; i++)
            {
                var ped = _crew[i];
                if (ped == null || !ped.Exists()) continue;

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    ped.IsPersistent = false;
                    ped.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // The streamer gets him.
                }
            }

            _crew.Clear();
            _forDealer = "";
        }
    }
}
