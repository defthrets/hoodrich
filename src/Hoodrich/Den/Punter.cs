using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;

namespace Hoodrich.Den
{
    /// <summary>
    /// A street punk sat at the far end of a table: the chair furthest from the way in, the
    /// casino's own seated idle and its fidgets, the way a regular sits at a table he is not
    /// winning at. Scenery for the table rather than a player at it -- nobody NPC Mind knows,
    /// nobody the scene builds -- and his chair is never offered to you. Michael asked for one
    /// at the roulette and one at the card table on 2026-09-26.
    /// </summary>
    internal sealed class Punter
    {
        private readonly Entity _table;
        private readonly string _model;
        private readonly string _idle;
        private readonly string[] _fidgets;
        private readonly string _what;
        private readonly Random _rng;

        private Ped _ped;
        private Sitter _sitter;

        public Punter(Entity table, string model, string idle, string[] fidgets, string what, Random rng)
        {
            _table = table;
            _model = model;
            _idle = idle;
            _fidgets = fidgets ?? new string[0];
            _what = what;
            _rng = rng;
        }

        public bool Exists => _ped != null && _ped.Exists() && _ped.IsAlive;

        /// <summary>His chair, 1 to 4 by the table's own numbering, so it is not offered to you. 0 while he has none.</summary>
        public int Chair => Exists && _sitter != null ? _sitter.Seat.Number : 0;

        /// <summary>
        /// Sat straight down in the far chair, already in his idle -- the room is put together
        /// behind the door's fade, and a man walking in from nowhere to sit down would be the
        /// one thing in it that moved. Only once the seated clips are in.
        /// </summary>
        public bool Spawn(Vector3 door, GangDef gang)
        {
            if (Exists) return true;
            if (_table == null || !_table.Exists()) return false;
            if (!Scene.Loaded(Scene.SharedPlayer)) return false;
            if (Core.Crowded.Busy) { Core.Crowded.HeldOff("Den"); return false; }

            var seat = Seat.Furthest(_table, door);
            if (seat == null) return false;

            try
            {
                var model = new Model(_model);
                if (!model.IsValid || !model.IsInCdImage || !Models.Ready(model)) return false;

                var ped = World.CreatePed(model, seat.At, seat.Rot.Z);
                model.MarkAsNoLongerNeeded();

                if (ped == null || !ped.Exists()) return false;

                ped.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, false);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, ped.Handle, true);

                // One of the room's own, so the men on the door have no quarrel with him.
                if (gang != null) Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                Core.Helmets.Off(ped);

                _ped = ped;
                _sitter = new Sitter(ped, seat, _idle, _fidgets, _rng);
                _sitter.Idle();

                Log.Info("Den: a punter (" + _model + ") is sat at the " + _what + ", chair " + seat.Number + " -- the far end from the way in.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not sit a punter at the " + _what + ": " + ex.Message);
                return false;
            }
        }

        public void Update()
        {
            if (!Exists || _sitter == null) return;
            _sitter.Update();
        }

        public void Remove()
        {
            try
            {
                if (_ped != null && _ped.Exists()) _ped.Delete();
            }
            catch { }

            _ped = null;
            _sitter = null;
        }
    }
}
