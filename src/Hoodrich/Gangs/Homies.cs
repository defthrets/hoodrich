using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Two of yours, on call.
    ///
    /// You spend the first three jobs riding with homies who are handed to you by whoever set
    /// the job, and then every hour after that on your own -- which is the wrong way round for
    /// somebody who has just been asked into a gang. Once you have been in a car with them and
    /// come back, you should be able to ask.
    ///
    /// They are put in the PLAYER'S OWN GROUP rather than tasked to follow, which is a much
    /// bigger difference than it sounds. The group is the machinery the game already uses for
    /// story buddies: they keep formation without being re-tasked, they fight what you fight,
    /// they get into the passenger seats of whatever you get into, and they find their own way
    /// back when they lose you. Anything hand-rolled out of TASK_FOLLOW would do the first of
    /// those and none of the rest.
    /// </summary>
    internal sealed class Homies
    {
        /// <summary>How many turn up. Two, because three is a convoy and one is a shadow.</summary>
        private const int Crew = 2;

        /// <summary>Where they appear, behind you and off to the side.</summary>
        private const float BehindBy = 3.4f;
        private const float SideBy = 1.5f;

        /// <summary>How far they may drift before the group hauls them back.</summary>
        private const float LeashRange = 55f;

        private const int TickMs = 900;

        /// <summary>
        /// What they carry.
        ///
        /// A pistol each and nothing heavier. Two men following you round Los Santos with
        /// rifles is an event rather than an escort, and they draw the kind of attention this
        /// mod spends its time helping you avoid.
        /// </summary>
        private static readonly string[] Guns =
        {
            "WEAPON_PISTOL", "WEAPON_SNSPISTOL", "WEAPON_MICROSMG"
        };

        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;

        private readonly List<Ped> _out = new List<Ped>();
        private readonly Random _rng = new Random();

        private int _next;

        public Homies(GangRegistry gangs, Affiliation crew, PlayerState state)
        {
            _gangs = gangs;
            _crew = crew;
            _state = state;
        }

        /// <summary>
        /// The block, so somebody mentions it when you roll out with people.
        ///
        /// Named Feed rather than Social because a field called Social shadows the namespace
        /// it lives in, and Social.SocialEvent then resolves to the field instead of the type.
        /// </summary>
        public Hoodrich.Social.SocialFeed Feed;

        /// <summary>How many of yours are actually still standing.</summary>
        public int Standing
        {
            get
            {
                var n = 0;

                foreach (var ped in _out)
                {
                    if (ped != null && ped.Exists() && ped.IsAlive) n++;
                }

                return n;
            }
        }

        public bool AnyOut => Standing > 0;

        /// <summary>
        /// Whether you are allowed to ask at all.
        ///
        /// Unlocked by the job you first ride with them on, and gated on still being in the
        /// set -- they are the gang's people, not yours, and somebody who has walked away does
        /// not get to keep the phone number.
        /// </summary>
        public bool Available =>
            _state != null && _state.HomiesUnlocked && _crew != null && _crew.IsAffiliated;

        // ---- calling them ------------------------------------------------------

        /// <summary>Brings them out. Returns what to tell the player.</summary>
        public string Call()
        {
            if (!Available) return "you ain't got nobody to call yet.";
            if (AnyOut) return "they already with you.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "not right now.";

            var gang = _crew.Current;
            if (gang == null || gang.MemberModels.Count == 0) return "nobody picked up.";

            var group = Function.Call<int>(Hash.GET_PLAYER_GROUP, Game.Player.Handle);

            for (var i = 0; i < Crew; i++)
            {
                var side = i % 2 == 0 ? 1f : -1f;

                var at = player.Position
                         - player.ForwardVector * BehindBy
                         + player.RightVector * (SideBy * side);

                var ped = Make(gang, at, player.Heading, group);
                if (ped != null) _out.Add(ped);
            }

            if (_out.Count == 0) return "nobody picked up.";

            // Formation 0 is the loose one that walks abreast rather than in single file,
            // which is what two men walking with you looks like.
            try
            {
                Function.Call(Hash.SET_GROUP_FORMATION, group, 0);
                Function.Call(Hash.SET_GROUP_SEPARATION_RANGE, group, LeashRange);
            }
            catch { /* they will keep up anyway */ }

            Log.Info("Homies out: " + _out.Count + " with the player.");

            if (Feed != null) Feed.On(Hoodrich.Social.SocialEvent.HomiesOut);

            return null;
        }

        /// <summary>Sends them home. They walk off rather than vanishing.</summary>
        public string Dismiss()
        {
            if (!AnyOut) return "ain't nobody with you.";

            foreach (var ped in _out)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);

                    if (ped.IsAlive)
                    {
                        ped.Task.ClearAll();
                        Function.Call(Hash.TASK_WANDER_STANDARD, ped.Handle, 10f, 10);
                    }

                    // Released rather than deleted, for the same reason nothing else in this
                    // mod deletes a man you are looking at: they walk away, and the game takes
                    // them back once nobody is watching.
                    ped.IsPersistent = false;
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* they know the way */ }
            }

            _out.Clear();
            Log.Info("Homies sent home.");

            return null;
        }

        // ---- keeping them -------------------------------------------------------

        public void Update()
        {
            if (_out.Count == 0) return;

            if (Game.GameTime < _next) return;
            _next = Game.GameTime + TickMs;

            // The dead are dropped from the list rather than cleaned up. A body is a thing
            // that happened and it should stay where it fell; what has to stop is this class
            // counting it as somebody who is still with you.
            for (var i = _out.Count - 1; i >= 0; i--)
            {
                var ped = _out[i];

                if (ped == null || !ped.Exists())
                {
                    _out.RemoveAt(i);
                    continue;
                }

                if (!ped.IsAlive)
                {
                    try
                    {
                        ped.IsPersistent = false;
                        ped.MarkAsNoLongerNeeded();
                    }
                    catch { /* he is past caring */ }

                    _out.RemoveAt(i);
                    Log.Info("A homie went down.");
                }
            }

            // Walking away from the set does not leave you with two of their people.
            if (_out.Count > 0 && (_crew == null || !_crew.IsAffiliated)) Dismiss();
        }

        // ---- who they are -------------------------------------------------------

        private Ped Make(GangDef gang, Vector3 at, float heading, int group)
        {
            var name = gang.MemberModels[_rng.Next(gang.MemberModels.Count)];

            try
            {
                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) return null;

                var ped = World.CreatePed(model, at, heading);
                model.MarkAsNoLongerNeeded();

                if (ped == null || !ped.Exists()) return null;

                var h = ped.Handle;

                ped.IsPersistent = true;
                ped.Armor = 40;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h,
                              Game.GenerateHash(gang.RelationshipGroup));

                Function.Call(Hash.GIVE_WEAPON_TO_PED, h,
                              Game.GenerateHash(Guns[_rng.Next(Guns.Length)]), 200, false, true);

                Function.Call(Hash.SET_PED_ACCURACY, h, 35);

                // 0 can use cover, 1 can use vehicles, 2 can do drive-bys, 5 always fight,
                // 46 will fight an armed man while empty-handed. The last one matters: without
                // it a homie who has just been disarmed stands and watches.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 0, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 1, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 2, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, true);

                Function.Call(Hash.SET_PED_COMBAT_ABILITY, h, 2);
                Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, h, 2);

                // Yours, and not shootable by you by accident. Friendly fire from the player
                // turning two bodyguards into two enemies is the worst possible failure here.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED_BY_PLAYER, h, Game.Player.Handle, false);
                Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, h, false, false);

                Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, h, group);
                Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, h, true);

                return ped;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not bring a homie out: " + ex.Message);
                return null;
            }
        }

        public void RestoreWorld()
        {
            foreach (var ped in _out)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);
                    ped.IsPersistent = false;
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }

            _out.Clear();
        }
    }
}
