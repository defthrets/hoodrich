using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;

namespace Hoodrich.Core
{
    /// <summary>
    /// Writes down how you actually drive, so it can be given to somebody else later.
    ///
    /// THE POINT IS THE TAKEOVER. Every number the cars at the junction work to is a guess --
    /// how wide a donut is, how fast a car comes in, how long it holds lock. This is the
    /// instrument for replacing those guesses with a measurement of somebody doing it properly.
    ///
    /// WHAT IS RECORDED IS DELIBERATELY MORE THAN ANY ONE USE NEEDS, because it is cheap to
    /// record and expensive to have to go and do it again. There are three things this could
    /// feed and they want different halves of it:
    ///
    ///   - REPLAYING THE INPUTS on an NPC car. Wants steering, throttle, brake and handbrake.
    ///     This is the one worth building: the car stays physically simulated, so it has
    ///     momentum and it hits things like a car instead of passing through them.
    ///
    ///   - REPLAYING THE PATH, by writing position and rotation onto a car every frame. Wants
    ///     the pose. It would be frame-perfect and it is the mistake this mod already made and
    ///     fixed once -- see the note on Takeover.Working. A car moved that way ploughs through
    ///     a crowd, and the junction has sixty people standing round it.
    ///
    ///   - TUNING THE CONSTANTS, which needs neither: just the geometry of where the car went
    ///     and how fast. The cheapest of the three by a distance and probably the best value.
    ///
    /// So it records the pose, the velocity AND the controls, and nothing has to be decided
    /// before the driving happens.
    ///
    /// THIRTY HERTZ, NOT SIXTY. Half the file for a resolution nothing here can tell apart: a
    /// car at twenty metres a second moves two thirds of a metre between samples, and anything
    /// replaying this interpolates anyway. An hour of driving is still an enormous file, so
    /// there is a cap, and it stops rather than wrapping -- a recording that quietly threw away
    /// its first half would be worse than one that ended.
    /// </summary>
    internal sealed class Recorder
    {
        /// <summary>One instant of somebody driving.</summary>
        private struct Frame
        {
            public int T;

            public float X, Y, Z;
            public float Pitch, Roll, Heading;

            public float VX, VY, VZ;
            public float Speed;

            public float Steer;
            public float Throttle;
            public float Brake;

            public bool Hand;
            public int Gear;
            public float Rpm;
            public float Wheel;
            public bool Grounded;
        }

        private readonly List<Frame> _frames = new List<Frame>();

        private int _startedAt;
        private int _nextSample;
        private string _car = "";

        /// <summary>Whether it is taking samples right now.</summary>
        public bool Running { get; private set; }

        /// <summary>How many samples are in hand, for the settings row to show.</summary>
        public int Count { get { return _frames.Count; } }

        /// <summary>
        /// Start, if there is anything to record.
        ///
        /// Refuses rather than starting empty. A recorder that says yes while you are stood on
        /// a pavement is one you come back to twenty minutes later having captured nothing.
        /// </summary>
        public string Start()
        {
            if (Running) return "Already recording.";

            var player = Game.Player.Character;

            if (player == null || !player.Exists()) return "Not now.";

            var car = player.CurrentVehicle;

            if (car == null || !car.Exists()) return "Get in a car first.";

            _frames.Clear();

            _car = SafeModel(car);
            _startedAt = Game.GameTime;
            _nextSample = 0;

            Running = true;

            Log.Info("Recording your driving in a " + _car + ".");

            return "Recording. Drive it how you want them to drive it.";
        }

        /// <summary>Stop and write it out.</summary>
        public string Stop()
        {
            if (!Running) return "Not recording.";

            Running = false;

            if (_frames.Count < 2)
            {
                _frames.Clear();
                return "Nothing worth keeping.";
            }

            var path = Write();

            var seconds = (_frames[_frames.Count - 1].T - _frames[0].T) / 1000f;
            var kept = _frames.Count;

            _frames.Clear();

            if (path == null) return "Could not write the recording.";

            Log.Info("Wrote " + kept + " samples (" + seconds.ToString("0.0") + "s) to " + path);

            return kept + " samples, " + seconds.ToString("0.0") + "s. Saved.";
        }

        /// <summary>
        /// One sample, if one is due.
        ///
        /// Called every frame and mostly does nothing, which is the shape everything else that
        /// runs per frame in this mod has. The early returns are ordered cheapest first.
        ///
        /// STOPS ITSELF WHEN YOU GET OUT. Somebody who has left the car is not driving it, and
        /// a recording that carries on through a firefight and a walk back is a recording with
        /// a hole in the middle that nothing downstream can see.
        /// </summary>
        public void Update()
        {
            if (!Running) return;

            var now = Game.GameTime;

            if (now < _nextSample) return;

            _nextSample = now + SampleMs;

            try
            {
                var player = Game.Player.Character;

                if (player == null || !player.Exists()) { Stop(); return; }

                var car = player.CurrentVehicle;

                if (car == null || !car.Exists()) { Stop(); return; }

                if (_frames.Count >= MostSamples)
                {
                    Log.Info("Recording hit the cap and stopped itself.");
                    Stop();
                    return;
                }

                var at = car.Position;
                var spin = car.Rotation;
                var vel = car.Velocity;

                _frames.Add(new Frame
                {
                    T = now - _startedAt,

                    X = at.X, Y = at.Y, Z = at.Z,

                    // Pitch and roll as well as heading. A car mid-slide is leaned over and
                    // nose-down, and a recording that kept only the heading would describe a
                    // car sliding perfectly flat.
                    Pitch = spin.X, Roll = spin.Y, Heading = car.Heading,

                    VX = vel.X, VY = vel.Y, VZ = vel.Z,
                    Speed = car.Speed,

                    // THE CONTROLS ARE THE HALF THAT MATTERS FOR REPLAY. SteeringAngle is what
                    // the wheels are actually doing rather than what the stick is doing, which
                    // is the thing another car would have to copy.
                    Steer = car.SteeringAngle,
                    Throttle = car.Throttle,
                    Brake = car.BrakePower,
                    // READ OFF THE DRIVER, NOT THE CAR. IsHandbrakeForcedOn is set-only --
                    // it is how you FORCE one on, not how you ask whether one is on -- so the
                    // honest source is the button, which is also the thing another car would
                    // have to copy.
                    Hand = Game.IsControlPressed(Control.VehicleHandbrake),

                    Gear = car.CurrentGear,
                    Rpm = car.CurrentRPM,

                    // Wheel speed against road speed is what a skid IS -- wheels turning faster
                    // than the car is moving. Neither number says it on its own.
                    Wheel = car.WheelSpeed,
                    Grounded = car.IsOnAllWheels
                });
            }
            catch (Exception ex)
            {
                Log.Debug("Recorder: " + ex.Message);
            }
        }

        /// <summary>
        /// Out to data\drives, named for when it was taken.
        ///
        /// ROUNDED ON THE WAY OUT rather than stored rounded, because the rounding is about the
        /// file and not about the data. Three decimals on a position is a millimetre and four on
        /// a steering angle is far finer than any hand -- past that it is digits nobody will
        /// read making a file nobody can open.
        /// </summary>
        private string Write()
        {
            try
            {
                var dir = Path.Combine(Paths.Writable, "drives");

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var name = "drive-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                var path = Path.Combine(dir, name);

                var doc = Json.Object();

                doc.Set("_comment", Json.Array()
                    .Add(Json.Str("Somebody actually driving, sampled at 30Hz."))
                    .Add(Json.Str(""))
                    .Add(Json.Str("t is milliseconds from the start. x/y/z and pitch/roll/heading are the pose;"))
                    .Add(Json.Str("vx/vy/vz and speed are the motion; steer/throttle/brake/hand are what the"))
                    .Add(Json.Str("driver was doing about it. wheel is wheel speed -- wheel against speed is"))
                    .Add(Json.Str("what a skid is, and neither number says it alone.")));

                doc.Set("car", _car);
                doc.Set("hz", 1000 / SampleMs);
                doc.Set("samples", _frames.Count);

                var list = Json.Array();

                foreach (var f in _frames)
                {
                    list.Add(Json.Object()
                        .Set("t", f.T)
                        .Set("x", Math.Round(f.X, 3))
                        .Set("y", Math.Round(f.Y, 3))
                        .Set("z", Math.Round(f.Z, 3))
                        .Set("pitch", Math.Round(f.Pitch, 2))
                        .Set("roll", Math.Round(f.Roll, 2))
                        .Set("heading", Math.Round(f.Heading, 2))
                        .Set("vx", Math.Round(f.VX, 3))
                        .Set("vy", Math.Round(f.VY, 3))
                        .Set("vz", Math.Round(f.VZ, 3))
                        .Set("speed", Math.Round(f.Speed, 3))
                        .Set("steer", Math.Round(f.Steer, 4))
                        .Set("throttle", Math.Round(f.Throttle, 3))
                        .Set("brake", Math.Round(f.Brake, 3))
                        .Set("hand", f.Hand)
                        .Set("gear", f.Gear)
                        .Set("rpm", Math.Round(f.Rpm, 3))
                        .Set("wheel", Math.Round(f.Wheel, 3))
                        .Set("grounded", f.Grounded));
                }

                doc.Set("frames", list);

                return JsonFile.Write(path, doc) ? path : null;
            }
            catch (Exception ex)
            {
                Log.Error("Could not write the recording.", ex);
                return null;
            }
        }

        /// <summary>The model name if it can be had, and something honest if it cannot.</summary>
        private static string SafeModel(Vehicle car)
        {
            try
            {
                var name = car.DisplayName;

                return string.IsNullOrEmpty(name) ? car.Model.Hash.ToString() : name;
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>Thirty a second. See the note on the class.</summary>
        private const int SampleMs = 33;

        /// <summary>
        /// The most samples one recording may hold. Twenty minutes at thirty hertz.
        ///
        /// It STOPS at the cap rather than dropping the oldest. A recording that quietly threw
        /// away its first half would be a file that says it is twenty minutes of driving and is
        /// actually the last twenty of forty, which is the sort of thing nobody notices until
        /// the numbers taken from it are wrong.
        /// </summary>
        private const int MostSamples = 36000;
    }
}
