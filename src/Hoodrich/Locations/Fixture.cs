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

        public void Update()
        {
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
