// GENERATED -- DO NOT EDIT. This is Parkview, copied in by tools/sync-parkview.py from
// C:\projects\parkview\src\Parkview\Main.cs. Change it there; the next build overwrites this.
using System;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using Hoodrich.Parkview.Core;
using Hoodrich.Parkview.UI;

namespace Hoodrich.Parkview
{
    /// <summary>
    /// Parkview: a scene built on the open ground in Chamberlain Hills, and the loader that
    /// builds it -- Object Spooner files stood up as you come near, props with bodies, floors
    /// laid under slabs that have none, and people who live a little: idle with the thing in
    /// their hand, talk, walk about, walk in and walk off, and are not there in the small hours.
    ///
    /// Nothing here talks to any other mod and nothing here is loaded from anywhere but the
    /// BCL and ScriptHookVDotNet.
    /// </summary>
    public sealed class Main : Script
    {
        private readonly Settings _cfg;
        private readonly Scenery _scenes;
        private readonly Rooms _rooms;
        private bool _parked;
        private int _greetAt;
        private bool _greeted;
        private int _flushAt;

        public Main()
        {
            try
            {
                // ONE PARKVIEW AT A TIME. It ships inside Posted Up as well as on its own, and
                // two copies loaded together build every scene twice -- two of everybody, stood
                // inside each other, two of every prop. The first to start claims the name for
                // the session and any other stands down and says where the running one is. The
                // claim lives in the script domain, so a reload (Insert) starts clean.
                var claim = AppDomain.CurrentDomain.GetData(OneCopy) as string;
                var mine = typeof(Main).Assembly.GetName().Name;

                if (claim != null)
                {
                    _parked = true;
                    Log.Info("Parkview is already running inside " + claim + ".dll, so the copy in " + mine +
                             ".dll stands down. Take " + mine + ".dll out of scripts to stop this line.");
                    return;
                }

                AppDomain.CurrentDomain.SetData(OneCopy, mine);

                _cfg = Core.Settings.Load();
                Log.Level = _cfg.LogLevel;

                Log.Info("---- environment ----");
                Log.Info("  " + Build.Name + " " + Build.Version);
                Log.Info("  scripts " + Paths.Scripts);
                Log.Info("  data    " + Paths.Data);
                Log.Info("---------------------");

                _scenes = new Scenery(_cfg);
                _rooms = new Rooms(_cfg);

                Interval = 0;
                Tick += OnTick;
                KeyDown += OnKey;
                Aborted += OnAborted;

                _greetAt = Game.GameTime + 4000;

                Log.Info(Build.Name + " " + Build.Version + " by " + Build.By + " loaded. " +
                         Say(_cfg.CaptureKey, _cfg.CaptureModifier) + " captures everything round you, " +
                         Say(_cfg.ReloadKey, _cfg.ReloadModifier) + " reads the scene files again, " +
                         Say(_cfg.HideKey, _cfg.HideModifier) + " hides the map prop you are looking at, " +
                         Say(_cfg.RoomKey, _cfg.RoomModifier) + " sets the rented room to where you stand, " +
                         Say(_cfg.TakeKey, _cfg.TakeModifier) + " takes over the map prop you are looking at.");
            }
            catch (Exception ex)
            {
                _parked = true;
                Log.Error("Failed to start; disabled for this session.", ex);
            }
        }

        /// <summary>The script-domain key the running copy claims. See the constructor.</summary>
        private const string OneCopy = "Parkview.Running";

        private static string Say(Keys key, Keys mod)
        {
            return (mod == Keys.None ? "" : mod + "+") + key;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_parked || _cfg == null || !_cfg.Enabled) return;

            try
            {
                var now = Game.GameTime;

                if (!_greeted && now >= _greetAt)
                {
                    _greeted = true;
                    Notify.Important("~g~" + Build.Name + " " + Build.Version + "~s~ loaded. " + _scenes.Tally() + ".");
                }

                _scenes.Update();
                _rooms.Update();

                if (now - _flushAt > 2000)
                {
                    _flushAt = now;
                    Log.Flush();

                    // Anybody of ours who is gone comes off NPC Mind's table. See Folk.Prune.
                    Folk.Prune();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Tick threw.", ex);
            }
        }

        private static bool Pressed(KeyEventArgs e, Keys key, Keys mod)
        {
            if (key == Keys.None || e.KeyCode != key) return false;

            var shift = e.Shift; var ctrl = e.Control; var alt = e.Alt;

            if (mod == Keys.Shift) return shift && !ctrl && !alt;
            if (mod == Keys.Control) return ctrl && !shift && !alt;
            if (mod == Keys.Alt) return alt && !shift && !ctrl;
            return !shift && !ctrl && !alt;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (_parked || _cfg == null || !_cfg.Enabled) return;

            try
            {
                if (Pressed(e, _cfg.HideKey, _cfg.HideModifier)) { Hide(); return; }
                if (Pressed(e, _cfg.ReloadKey, _cfg.ReloadModifier)) { Reload(); return; }
                if (Pressed(e, _cfg.CaptureKey, _cfg.CaptureModifier)) { Capture(); return; }
                if (Pressed(e, _cfg.RoomKey, _cfg.RoomModifier)) { _rooms.SetRoomHere(); return; }
                if (Pressed(e, _cfg.TakeKey, _cfg.TakeModifier)) { Take(); return; }
            }
            catch (Exception ex)
            {
                Log.Error("Key threw.", ex);
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            try { _scenes?.RestoreWorld(); } catch { /* teardown */ }
            try { _rooms?.Down(); } catch { /* teardown */ }
            try { Folk.Prune(true); } catch { /* teardown */ }
            Log.Flush();
        }

        // ---- the three tools ------------------------------------------------------------

        /// <summary>Takes down what is standing and builds it from the files as they are now.</summary>
        private void Reload()
        {
            var n = _scenes.Reload();

            Notify.Important(n == 0
                ? "No placements found. Save one in Menyoo's Object Spooner, or put a scene file in scripts\\Parkview\\scenery."
                : n + " placement(s) read. " + _scenes.Tally() + ".");
        }

        /// <summary>
        /// Writes the placed peds, cars and props within 80 m to a scene file, and lists them
        /// in the log. The one that catches unsaved work: an hour in the spooner is one crash
        /// from never having happened, and this writes what is stood round you to a file that
        /// opens in Menyoo like anything else it saved.
        /// </summary>
        private void Capture()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var found = Spooner.Around(me.Position, 80f);

            if (found.Count == 0)
            {
                Notify.Important("Nothing placed within 80 m.");
                return;
            }

            foreach (var one in found)
            {
                Log.Info("Placed: " + (string.IsNullOrEmpty(one.ModelName) ? Names.Say(one.ModelHash) : one.ModelName) +
                         "  " + one.What + "  " + one.At.DistanceTo(me.Position).ToString("0.0") + " m  at " + one.At);
            }

            var name = "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml";
            var path = System.IO.Path.Combine(Paths.Scenery, name);

            Notify.Important(Spooner.Write(path, found, "Captured by " + Build.Name)
                ? found.Count + " placement(s) saved as " + name + "."
                : "Could not write the scene file. See the log.");
        }

        /// <summary>
        /// TAKE OVER THE MAP PROP YOU ARE LOOKING AT: the vanilla one is hidden for good and
        /// a copy of it is put in the same spot, ours to move.
        ///
        /// WHY MOVING A VANILLA BENCH DOES NOT STICK. A bench on the street is part of the
        /// map, not an entity somebody placed. Menyoo will shift the one in front of you and
        /// it looks moved -- but nothing has changed in the map, so the moment the block
        /// streams out and back the bench is at its old spot again. There is nothing this mod
        /// can write in a scene file to stop that: the scene file only ever ADDS things.
        ///
        /// So the map one goes (CREATE_MODEL_HIDE, written into hidden.xml so it stays gone)
        /// and a real prop of the same model takes its place. That one is a thing, not a
        /// picture: move it in Menyoo, press the capture key, and it is in the scene at wherever
        /// you left it. Michael asked on 2026-09-23 after the benches and lights kept going
        /// home.
        ///
        /// DO IT BEFORE MOVING IT, not after. The hide is a sphere round where the thing is
        /// NOW, and a bench you have already dragged ten metres leaves its map twin sat at the
        /// old spot outside that sphere. Put it back first, or hide the twin with the hide key.
        /// </summary>
        private void Take()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var eye = GameplayCamera.Position;
            var to = eye + GameplayCamera.Direction * HideReach;

            var ray = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                                         eye.X, eye.Y, eye.Z, to.X, to.Y, to.Z, 16, me.Handle, 7);

            var hit = new OutputArgument();
            var where = new OutputArgument();
            var normal = new OutputArgument();
            var thing = new OutputArgument();

            Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, ray, hit, where, normal, thing);

            var handle = thing.GetResult<int>();

            if (!hit.GetResult<bool>() || handle == 0)
            {
                Notify.Important("Nothing in front of you. Look straight at it and try again.");
                return;
            }

            var found = Entity.FromHandle(handle);

            if (found == null || !found.Exists() || !(found is Prop))
            {
                Notify.Important("That is not a prop.");
                return;
            }

            if (_scenes.Mine(handle))
            {
                Notify.Important("That one is already ours -- just move it and capture.");
                return;
            }

            var model = found.Model;
            var at = found.Position;
            var rot = found.Rotation;
            var name = Names.Say(model.Hash);
            var hash = unchecked((uint)model.Hash);

            // ANY EARLIER COPY GOES FIRST. Take makes a real prop, and a real prop is
            // not part of any scene until it is captured -- so Mine() says no to it and
            // a second press took over the copy and made another. Michael pressed it
            // eight times on one bench and got eight benches inside each other.
            //
            // Only MISSION entities are touched: that is what a script made, and it is
            // what tells our copy apart from a bench the map put there.
            try
            {
                var already = World.GetNearbyProps(at, 0.6f);

                if (already != null)
                {
                    var stacked = 0;

                    foreach (var other in already)
                    {
                        if (other == null || !other.Exists()) continue;
                        if (other.Handle == found.Handle) continue;
                        if (other.Model.Hash != model.Hash) continue;
                        if (!Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, other.Handle)) continue;

                        try { other.Delete(); stacked++; } catch { }
                    }

                    if (stacked > 0) Log.Info("Take: cleared " + stacked + " copy(s) already stacked there.");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not clear the stack: " + ex.Message);
            }

            // The map one goes first, so the copy is not standing inside it.
            var one = new Spooner.Hidden
            {
                ModelName = name,
                ModelHash = hash,
                At = at,
                Radius = Spooner.DefaultRadius
            };

            var path = System.IO.Path.Combine(Paths.Scenery, "hidden.xml");

            if (!Spooner.Bury(path, one))
            {
                Notify.Important("Could not write it down. See the log.");
                return;
            }

            // Excluding script objects: the copy about to stand here is one, and the plain
            // call would hide it the moment it went up. See Scenery.Vanish.
            Function.Call(Hash.CREATE_MODEL_HIDE_EXCLUDING_SCRIPT_OBJECTS, at.X, at.Y, at.Z, one.Radius,
                          unchecked((int)hash), false);

            // And ours takes its place.
            if (!Models.Ready(model))
            {
                Log.Info("Take: " + name + " is hidden, but the model would not load for the copy.");
                Notify.Problem(name + " is hidden. The copy would not load -- place one in Menyoo.");
                return;
            }

            var made = World.CreateProp(model, at, false, false);

            if (made == null || !made.Exists())
            {
                Log.Info("Take: " + name + " is hidden, but the copy would not stand.");
                Notify.Problem(name + " is hidden. The copy would not stand -- place one in Menyoo.");
                return;
            }

            made.PositionNoOffset = at;
            made.Rotation = rot;
            made.IsPositionFrozen = true;
            Function.Call(Hash.SET_ENTITY_DYNAMIC, made.Handle, false);
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, made.Handle, true, true);

            model.MarkAsNoLongerNeeded();

            Log.Info("Take: " + name + " at " + at + " is hidden and replaced with one of ours.");
            Notify.Important("~g~" + name + "~s~ is yours now. Move it, then " +
                             Say(_cfg.CaptureKey, _cfg.CaptureModifier) + " to keep it there.");
        }
        /// <summary>How far ahead the hide looks for a prop.</summary>
        private const float HideReach = 12f;

        /// <summary>
        /// Takes the map prop you are looking at out for good, and writes it into
        /// scenery\hidden.xml so it stays out. The half of a spooner scene the spooner cannot
        /// do: Menyoo records what you put somewhere and has no tag for what you took away.
        /// </summary>
        private void Hide()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var eye = GameplayCamera.Position;
            var to = eye + GameplayCamera.Direction * HideReach;

            var ray = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                                         eye.X, eye.Y, eye.Z, to.X, to.Y, to.Z, 16, me.Handle, 7);

            var hit = new OutputArgument();
            var where = new OutputArgument();
            var normal = new OutputArgument();
            var thing = new OutputArgument();

            Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, ray, hit, where, normal, thing);

            var handle = thing.GetResult<int>();

            if (!hit.GetResult<bool>() || handle == 0)
            {
                Notify.Important("Nothing in front of you. Look straight at it and try again.");
                return;
            }

            var found = Entity.FromHandle(handle);

            if (found == null || !found.Exists() || !(found is Prop))
            {
                Notify.Important("That is not a prop.");
                return;
            }

            if (_scenes.Mine(handle))
            {
                Notify.Important("That one is ours -- take it out of the scene file instead.");
                return;
            }

            var hash = unchecked((uint)found.Model.Hash);

            var one = new Spooner.Hidden
            {
                ModelName = Names.Say(found.Model.Hash),
                ModelHash = hash,
                At = found.Position,
                Radius = Spooner.DefaultRadius
            };

            var path = System.IO.Path.Combine(Paths.Scenery, "hidden.xml");

            if (!Spooner.Bury(path, one))
            {
                Notify.Important("Could not write it down. See the log.");
                return;
            }

            // Excluding script objects, so hiding the old bench next to one of ours never takes
            // ours with it. See Scenery.Vanish.
            Function.Call(Hash.CREATE_MODEL_HIDE_EXCLUDING_SCRIPT_OBJECTS, one.At.X, one.At.Y, one.At.Z, one.Radius,
                          unchecked((int)hash), false);

            Log.Info("Hidden for good: " + one.ModelName + " at " + one.At + ".");
            Notify.Important("~g~" + one.ModelName + "~s~ is gone, and stays gone.");
        }
    }
}
