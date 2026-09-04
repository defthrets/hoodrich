using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Something somebody dragged out and left there.
    ///
    /// A couch in a courtyard, on the exact spot and facing the exact way it was read off the
    /// HUD standing next to it. It does nothing, which is the point -- a block with furniture in
    /// it is a block people live on, and the difference between that and bare concrete costs one
    /// prop and no systems at all.
    ///
    /// Streamed with the block rather than left loaded forever: it appears when you are near
    /// enough to see it and is let go when you are not.
    /// </summary>
    internal sealed class Fixture
    {
        private const float SpawnRange = 90f;
        private const float DespawnRange = 160f;
        private const int UpdateIntervalMs = 2000;

        private readonly string[] _models;
        private readonly Vector3 _where;
        private readonly float _heading;

        private Prop _prop;
        private int _lastUpdate;

        public Fixture(Vector3 where, float heading, params string[] models)
        {
            _where = where;
            _heading = heading;
            _models = models;
        }

        /// <summary>
        /// A light thrown out of it, or null for a prop that is only a shape.
        ///
        /// A WORKLIGHT PROP DOES NOT LIGHT ANYTHING. The model has a lens on it and the lens
        /// is painted bright, and that is the whole of it -- stand a floodlight in a dark yard
        /// and the yard stays dark. Light in this game is drawn, per frame, by the thing that
        /// wants it, so anything that is supposed to be lighting a place has to say so.
        /// </summary>
        public System.Drawing.Color? Beam;

        /// <summary>How far it throws, how hard, and where the lamp sits on the prop.</summary>
        public float BeamRange = 32f;
        public float BeamPower = 14f;
        public Vector3 BeamAt = new Vector3(0f, 0.2f, 1.8f);

        /// <summary>How far below level it points. A worklight is aimed at the ground.</summary>
        public float BeamDrop = 0.35f;

        public void Update()
        {
            // EVERY FRAME, ABOVE THE THROTTLE, because a light is not a state -- it exists on
            // the frame it is drawn and on no other. The same lesson the van's radio taught:
            // a thing set once every two seconds is a thing that is off for the other 119
            // frames, and a floodlight flickering at half a hertz is worse than no floodlight.
            Shine();

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var away = player.Position.DistanceTo(_where);

            if (_prop != null && !_prop.Exists()) _prop = null;

            if (_prop == null)
            {
                if (away <= SpawnRange) Place();
                return;
            }

            if (away > DespawnRange) Clear();
        }

        /// <summary>
        /// The beam, drawn from the lamp along the way the prop is pointing.
        ///
        /// Two lights rather than one: a spot for the cone that lands on the ground, and a
        /// small round one at the lamp itself so the head glows rather than being a dark shape
        /// with a bright floor in front of it.
        ///
        /// Aimed slightly DOWN. A worklight on a tripod is pointed at the work, not at the
        /// horizon, and a beam dead level lights the far wall and nothing anybody is standing
        /// on.
        /// </summary>
        private void Shine()
        {
            if (Beam == null || _prop == null || !_prop.Exists()) return;

            try
            {
                var c = Beam.Value;

                var from = _prop.GetOffsetPosition(BeamAt);

                var dir = _prop.ForwardVector;
                dir = new Vector3(dir.X, dir.Y, dir.Z - BeamDrop);

                var len = dir.Length();
                if (len < 0.01f) return;

                dir = dir * (1f / len);

                Function.Call(Hash.DRAW_SPOT_LIGHT,
                              from.X, from.Y, from.Z,
                              dir.X, dir.Y, dir.Z,
                              (int)c.R, (int)c.G, (int)c.B,
                              BeamRange, BeamPower, 0.5f, 13f, 26f);

                Function.Call(Hash.DRAW_LIGHT_WITH_RANGE,
                              from.X, from.Y, from.Z,
                              (int)c.R, (int)c.G, (int)c.B, 3.5f, 4f);
            }
            catch
            {
                // No light this frame.
            }
        }

        private void Place()
        {
            foreach (var name in _models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    _prop = World.CreateProp(model, _where, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_prop == null || !_prop.Exists()) continue;

                    _prop.Heading = _heading;
                    _prop.IsPersistent = true;

                    // Sat on the ground and not to be shoved across the courtyard by anybody
                    // who walks into it. A couch that slides is a couch nobody put there.
                    Function.Call(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, _prop.Handle);
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, _prop.Handle, true);

                    // MEASURED AFTER IT IS SETTLED, NOT BEFORE. Both calls above move it --
                    // one drops it onto the ground and the other pins it there -- and a seat
                    // worked out from where the prop was asked to go rather than where it
                    // ended up is a seat hanging in the air above a couch that sank.
                    if (Seating.IsSeat(name)) Seating.Offer(this, _prop, name);

                    Log.Info("Fixture " + name + " placed at " + _where + ".");
                    return;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Debug("No usable model for the fixture at " + _where + ".");
        }

        private void Clear()
        {
            // Before the prop goes, so nobody is left holding a seat on a couch that is no
            // longer in the world. Cheap, unconditional, and safe on a fixture that never
            // offered any.
            Seating.Withdraw(this);

            try
            {
                if (_prop != null && _prop.Exists())
                {
                    _prop.MarkAsNoLongerNeeded();
                    _prop.Delete();
                }
            }
            catch { /* teardown */ }

            _prop = null;
        }

        public void RestoreWorld() => Clear();
    }
}
