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

        /// <summary>Whether the ground has actually been found under it yet. See Ground.</summary>
        private bool _grounded;
        private string _placedAs = "";

        /// <summary>The second pass, a few seconds after the first, for anything stood on something else. See Ground.</summary>
        private int _settleAt;
        private bool _settled;
        private const int SettleMs = 3000;

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

            if (!_grounded) Ground(true);
            else if (!_settled && now >= _settleAt)
            {
                _settled = true;
                Ground(true);
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
                    _placedAs = name;
                    _grounded = false;

                    // Held still from the first frame, and put on the ground by Ground, which
                    // is allowed to fail and be asked again.
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, _prop.Handle, true);
                    Ground(false);

                    Log.Info("Fixture " + name + " placed at " + _where +
                             (_grounded ? "." : " -- no ground loaded under it yet; it goes down when there is."));
                    return;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Debug("No usable model for the fixture at " + _where + ".");
        }

        /// <summary>
        /// Puts it on the ground, and says whether it managed to.
        ///
        /// PLACE_OBJECT_ON_GROUND_PROPERLY finds the ground by asking the collision under the
        /// prop, and in the second after the map has been switched -- which the grow room
        /// door does, on the far side of this same yard -- there is none loaded to ask. The
        /// native answers no and the prop stays exactly where it was asked to be, a metre
        /// up, frozen, for the rest of the session: a whole yard of furniture floating at
        /// knee height, which is what happened. So it is asked again every couple of seconds
        /// until it answers yes, and only then is the furniture offered as somewhere to sit,
        /// because the seats are measured off where it ends up.
        /// </summary>
        private void Ground(bool late)
        {
            if (_prop == null || !_prop.Exists()) return;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, _prop.Handle)) return;

                var was = _prop.Position;

                Function.Call(Hash.FREEZE_ENTITY_POSITION, _prop.Handle, false);
                var down = Function.Call<bool>(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, _prop.Handle);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, _prop.Handle, true);

                if (!down) return;

                // THE SECOND PASS. A bag on a table is put down onto whatever is under it at
                // the time, and if the table has not been put down yet itself the bag lands
                // on a table that is about to drop half a metre -- the same tick put the bag
                // down before the table and left the bag in the air. So everything gets a
                // second pass a few seconds after its first, by which time whatever it is
                // stood on has settled. The seats are only offered again if it moved, or a
                // couch nobody has touched would have everybody on it re-seated.
                if (_grounded)
                {
                    var moved = was.DistanceTo(_prop.Position);
                    if (moved < 0.03f) return;

                    if (Seating.IsSeat(_placedAs)) Seating.Offer(this, _prop, _placedAs);

                    Log.Info("Fixture " + _placedAs + " settled " + (int)(moved * 100f) +
                             "cm once what was under it had, at " + _prop.Position + ".");
                    return;
                }

                _grounded = true;
                _settled = false;
                _settleAt = Game.GameTime + SettleMs;

                if (Seating.IsSeat(_placedAs)) Seating.Offer(this, _prop, _placedAs);

                if (late) Log.Info("Fixture " + _placedAs + " put on the ground late, at " + _prop.Position + ".");
            }
            catch
            {
                // Asked again in two seconds.
            }
        }


        private void Clear()
        {
            // Before the prop goes, so nobody is left holding a seat on a couch that is no
            // longer in the world. Cheap, unconditional, and safe on a fixture that never
            // offered any.
            _grounded = false;
            _settled = false;

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
