using System;
using System.Collections.Generic;
using System.IO;
using GTA.Math;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The line somebody actually drove, turned into something a car can be told to follow.
    ///
    /// THE RECORDER WAS BUILT FOR THIS AND THEN NOTHING USED IT. It captured a hundred and
    /// twelve seconds of real driving round the junction at thirty hertz -- pose, velocity and
    /// controls -- and all that ever came out of it was a handful of tuned constants: how wide
    /// a donut is, how fast a car comes in. The line itself, which is the whole reason it
    /// exists, was never driven by anybody.
    ///
    /// WAYPOINTS RATHER THAN PLAYBACK, and that decision is already written down in the
    /// recorder's own notes. Writing the recorded pose onto a car every frame would be
    /// frame-perfect and is the mistake this mod made once and fixed: a car moved that way has
    /// no momentum and hits nothing, and there are sixty people stood round this junction.
    /// Handing the game a series of coordinates keeps the car simulated -- it has weight, it
    /// slides, it can be shoved, and it goes round somebody rather than through them.
    ///
    /// SO THE SHAPE IS THE PLAYER'S AND THE PHYSICS ARE THE GAME'S, which is the right split.
    /// What comes out is not a recording being replayed; it is a car being driven round the
    /// same path, badly, at speed, on drift tyres.
    ///
    /// THE STANDING START IS THROWN AWAY. Every recording begins with somebody sitting in a
    /// parked car working out that it is running, and a waypoint list that starts with forty
    /// samples of the same coordinate is a car being told to drive to where it already is.
    /// </summary>
    internal sealed class DriftLine
    {
        private readonly List<Vector3> _points = new List<Vector3>();

        private bool _looked;

        /// <summary>How far apart the waypoints are, in metres.</summary>
        private const float Apart = 4.5f;

        /// <summary>Below this the car was not moving, so the sample says nothing about a line.</summary>
        private const float Moving = 1.2f;

        /// <summary>Fewer than this and it is not a lap, it is somebody pulling out of a drive.</summary>
        private const int Fewest = 8;

        public bool Ready { get { return _points.Count >= Fewest; } }

        public int Count { get { return _points.Count; } }

        public Vector3 At(int i)
        {
            if (_points.Count == 0) return Vector3.Zero;

            if (i < 0) i = 0;

            return _points[i % _points.Count];
        }

        /// <summary>The next one along, wrapping. The line is a lap, so it has no end.</summary>
        public int Next(int i)
        {
            return _points.Count == 0 ? 0 : (i + 1) % _points.Count;
        }

        /// <summary>Whichever point is nearest, so a car can join the line where it stands.</summary>
        public int Nearest(Vector3 to)
        {
            var best = 0;
            var bestGap = float.MaxValue;

            for (var i = 0; i < _points.Count; i++)
            {
                var gap = _points[i].DistanceToSquared(to);

                if (gap >= bestGap) continue;

                bestGap = gap;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Read the most recent recording, once.
        ///
        /// THE NEWEST, because a recording is somebody saying "drive it like this" and the last
        /// thing they said is the thing they meant. Older ones are left where they are rather
        /// than deleted -- they cost nothing and going back to one is a matter of moving a file
        /// rather than driving the laps again.
        ///
        /// Read from the writable folder rather than from the mod's data, because that is where
        /// the recorder writes and a recording is the player's, not the mod's.
        /// </summary>
        public void Load()
        {
            if (_looked) return;
            _looked = true;

            try
            {
                var dir = Path.Combine(Paths.Writable, "drives");

                if (!Directory.Exists(dir))
                {
                    Log.Info("No drives folder, so the middle of the takeover is a burnout.");
                    return;
                }

                var files = Directory.GetFiles(dir, "drive-*.json");

                if (files.Length == 0)
                {
                    Log.Info("No recording to drive, so the middle of the takeover is a burnout.");
                    return;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                var newest = files[files.Length - 1];

                var doc = JsonFile.Read(newest);

                if (doc == null)
                {
                    Log.Warn("Could not read " + Path.GetFileName(newest) + ".");
                    return;
                }

                Take(doc["frames"]);

                Log.Info("Drift line: " + _points.Count + " waypoints out of " +
                         Path.GetFileName(newest) + ".");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not build the drift line: " + ex.Message);
            }
        }

        /// <summary>
        /// Thin thirty hertz down to something a driving task can be given.
        ///
        /// A WAYPOINT EVERY FOUR AND A HALF METRES. Every sample would be a coordinate every
        /// fifteen centimetres, which is not a route, it is a request to stop three thousand
        /// times. Four and a half is close enough that the corners survive -- a donut at four
        /// metres of radius still comes out as a circle rather than a triangle -- and far
        /// enough apart that each one is a real instruction.
        /// </summary>
        private void Take(Json frames)
        {
            if (frames == null) return;

            var started = false;
            var last = Vector3.Zero;

            foreach (var f in frames.Items)
            {
                var speed = f["speed"].AsFloat();

                // The standing start, and any long sit in the middle of a lap. Neither is a
                // shape -- they are the same coordinate over and over.
                if (!started && speed < Moving) continue;

                started = true;

                var at = new Vector3(f["x"].AsFloat(), f["y"].AsFloat(), f["z"].AsFloat());

                if (_points.Count > 0 && at.DistanceTo(last) < Apart) continue;

                _points.Add(at);
                last = at;
            }

            // A LAP THAT DOES NOT CLOSE IS A LINE WITH A SEAM IN IT. Wrapping from the last
            // point back to the first is fine when they are near each other and a diagonal
            // across the junction when they are not, so the tail is trimmed back to wherever
            // it last came near the start.
            if (_points.Count < Fewest) return;

            var home = _points[0];

            for (var i = _points.Count - 1; i > Fewest; i--)
            {
                if (_points[i].DistanceTo(home) > CloseTheLoop) continue;

                if (i < _points.Count - 1)
                {
                    Log.Info("Drift line: trimmed " + (_points.Count - 1 - i) +
                             " waypoint(s) off the end to close the lap.");

                    _points.RemoveRange(i + 1, _points.Count - 1 - i);
                }

                return;
            }
        }

        /// <summary>How near the end has to come to the start for the lap to count as closed.</summary>
        private const float CloseTheLoop = 12f;
    }
}
