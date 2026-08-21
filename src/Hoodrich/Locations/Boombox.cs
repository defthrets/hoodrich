using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A prop that plays music.
    ///
    /// The game has no way to make an object emit a radio station. Static emitters are fixed
    /// where Rockstar put them and cannot be moved, and the sound natives play one-shot effects
    /// rather than a stream. The only thing in the engine that plays radio at a point in the
    /// world is a vehicle -- so there is one here, invisible, without collision, sitting inside
    /// the prop with its radio on and turned up.
    ///
    /// That sounds like a bodge and it is, but it is the bodge every mod that has ever put a
    /// boombox in a yard uses, and it buys the real thing: actual stations, actual DJs, actual
    /// falloff as you walk away, and it ducks under gunfire and dialogue the way game audio
    /// should because the game thinks it IS game audio.
    ///
    /// The speaker is declared to the traffic watchdog like any other car of ours, or a
    /// watchdog looking for abandoned vehicles would eventually come and tidy away the music.
    /// </summary>
    internal sealed class Boombox
    {
        /// <summary>Near enough to be worth having it playing.</summary>
        private const float StreamRange = 90f;

        private const int UpdateIntervalMs = 1500;

        /// <summary>
        /// What is on.
        ///
        /// West Coast Classics, because this is a yard in Chamberlain and not a nightclub.
        /// </summary>
        private const string Station = "RADIO_09_HIPHOP_OLD";

        /// <summary>
        /// The speaker.
        ///
        /// Small, so that if the invisibility ever fails there is a moped in the yard rather
        /// than a bus.
        /// </summary>
        private static readonly string[] SpeakerModels = { "faggio", "bmx" };

        private readonly Vector3 _where;
        private readonly float _heading;
        private readonly Fixture _prop;

        private Vehicle _speaker;
        private int _lastUpdate;

        public Boombox(Vector3 where, float heading, params string[] models)
        {
            _where = where;
            _heading = heading;
            _prop = new Fixture(where, heading, models);
        }

        /// <summary>Whether this is our speaker, for the traffic watchdog.</summary>
        public bool Owns(Vehicle car)
        {
            return car != null && _speaker != null && _speaker.Exists() &&
                   car.Handle == _speaker.Handle;
        }

        public void Update()
        {
            _prop.Update();

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var near = player.Position.DistanceTo(_where) <= StreamRange;

            if (_speaker != null && !_speaker.Exists()) _speaker = null;

            // Out of earshot, so it goes. A silent vehicle sitting under a prop on the other
            // side of the map is a vehicle the game is streaming for no reason.
            if (!near)
            {
                Silence();
                return;
            }

            if (_speaker == null) Make();
            else KeepPlaying();
        }

        private void Make()
        {
            foreach (var name in SpeakerModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    // Under the prop rather than in it, so nothing pokes out if the invisibility
                    // does not take on some build.
                    _speaker = World.CreateVehicle(model, _where - new Vector3(0f, 0f, 2.5f), _heading);
                    model.MarkAsNoLongerNeeded();

                    if (_speaker == null || !_speaker.Exists()) continue;

                    _speaker.IsPersistent = true;
                    _speaker.IsVisible = false;
                    _speaker.IsPositionFrozen = true;

                    Function.Call(Hash.SET_ENTITY_COLLISION, _speaker.Handle, false, false);
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _speaker.Handle, true, true, false);

                    KeepPlaying();

                    Log.Info("Boombox playing at " + _where + ".");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not start the boombox: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Holds the station on.
        ///
        /// Re-asserted rather than set once: the game turns a parked vehicle's radio off on its
        /// own when nobody is in it, and it does it quietly, so a boombox that was set up
        /// correctly goes silent after a minute with nothing to say why.
        /// </summary>
        private void KeepPlaying()
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, _speaker.Handle, true);
                Function.Call(Hash.SET_VEH_RADIO_STATION, _speaker.Handle, Station);
                Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, _speaker.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Boombox would not play: " + ex.Message);
            }
        }

        private void Silence()
        {
            if (_speaker == null) return;

            try
            {
                if (_speaker.Exists())
                {
                    _speaker.IsPersistent = false;
                    _speaker.Delete();
                }
            }
            catch { /* it will stream out */ }

            _speaker = null;
        }

        public void RestoreWorld()
        {
            Silence();

            try { _prop.RestoreWorld(); }
            catch { /* teardown */ }
        }
    }
}
