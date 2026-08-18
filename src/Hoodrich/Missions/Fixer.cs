using System;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.UI;

namespace Hoodrich.Missions
{
    /// <summary>
    /// Lamar, who has work.
    ///
    /// Deliberately a second man rather than another line on Stretch's list. Stretch is supply
    /// and standing -- weight, prices, whether you are in. Lamar is jobs. Keeping them apart
    /// means each is somewhere you go for a reason, instead of one NPC being a menu with
    /// everything bolted to it.
    /// </summary>
    internal sealed class Fixer
    {
        /// <summary>The courtyard on Chamberlain, where he waits.</summary>
        internal static readonly Vector3 Spot = new Vector3(-84.972f, -1610.382f, 31.485f);
        private const float Heading = 206f;

        private const float SpawnRange = 110f;
        private const float DespawnRange = 190f;
        private const float TalkRange = 3.0f;
        private const int UpdateIntervalMs = 700;

        /// <summary>Skull: he is the one who sends you at people.</summary>
        private const int Sprite = 84;

        private static readonly string[] Models =
        {
            "ig_lamardavis", "csb_lamardavis", "ig_lamardavis_02", "g_m_y_famca_01"
        };

        private readonly Affiliation _crew;

        private Ped _ped;
        private Blip _blip;
        private int _lastUpdate;
        private bool _held;
        private bool _talkHeld;

        public Fixer(Affiliation crew)
        {
            _crew = crew;
        }

        public string Name => "Lamar";

        public Vector3 Position => Spot;

        /// <summary>Set by Main: the conversation screen and what he has to say.</summary>
        public Conversation Talk;
        public Func<DialogueNode> TalkBuilder;

        /// <summary>True when you are close enough to speak to him.</summary>
        public bool InReach
        {
            get
            {
                if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return false;

                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return false;

                return player.Position.DistanceTo(_ped.Position) <= TalkRange;
            }
        }

        public float DistanceTo()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 9999f;

            var from = _ped != null && _ped.Exists() ? _ped.Position : Spot;
            return player.Position.DistanceTo(from);
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            // He is your gang's fixer, so he only exists once you are in with them.
            if (_crew == null || !_crew.IsAffiliated)
            {
                Despawn();
                return;
            }

            SyncBlip();

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            var distance = player.Position.DistanceTo(Spot);

            if (distance > DespawnRange)
            {
                Despawn();
                return;
            }

            if (_ped == null && distance <= SpawnRange) Spawn();
        }

        private void SyncBlip()
        {
            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(Spot);
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, Sprite);
                Function.Call(Hash.SET_BLIP_COLOUR, _blip.Handle, 2);

                _blip.Name = "Lamar -- The Families";
                _blip.IsShortRange = false;
                _blip.Scale = 0.85f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip Lamar: " + ex.Message);
            }
        }

        private void Spawn()
        {
            var spot = Spot;

            try
            {
                if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 20f), out var groundZ,
                                          GetGroundHeightMode.Normal) && groundZ > 0f)
                {
                    spot.Z = groundZ;
                }
            }
            catch
            {
                // Use the authored height.
            }

            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _ped = World.CreatePed(model, spot, Heading);
                    model.MarkAsNoLongerNeeded();

                    if (_ped == null || !_ped.Exists()) continue;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _ped.Handle, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _ped.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _ped.Handle, false);
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle,
                                  "WORLD_HUMAN_STAND_MOBILE", 0, true);

                    _ped.IsPersistent = true;
                    _ped.BlockPermanentEvents = true;

                    GiveVoice(_ped);

                    Log.Info("Lamar is on his corner.");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Lamar model '" + name + "' failed: " + ex.Message);
                }
            }
        }

        private void Despawn()
        {
            if (_ped != null && _ped.Exists())
            {
                try
                {
                    _ped.MarkAsNoLongerNeeded();
                    _ped.Delete();
                }
                catch { /* teardown */ }
            }

            _ped = null;
            _held = false;
        }

        /// <summary>Offers the conversation and opens it, same button as the leaders.</summary>
        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen || !InReach) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to talk to Lamar.");

            if (!WantsToTalk()) return;

            var root = TalkBuilder?.Invoke();
            if (root == null) return;

            HoldForTalk();

            Talk.Speaker = _ped;
            Talk.Open(root, this);
        }

        private bool WantsToTalk()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                // Unreadable control is simply not pressed.
            }

            var pressed = down && !_talkHeld;
            _talkHeld = down;
            return pressed;
        }

        /// <summary>Ambient lines, so walking up to him and leaving are things you hear.</summary>
        private static readonly string[] HelloLines = { "GENERIC_HOWS_IT_GOING", "GENERIC_HI" };
        private static readonly string[] ByeLines = { "GENERIC_BYE", "GENERIC_THANKS" };

        private static readonly Random Rng = new Random();

        /// <summary>
        /// Lamar's own voice, reasserted every time he spawns.
        ///
        /// The stash house used to blank the ambient voice of every ped within 22 m, which is
        /// permanent -- so anybody who walked past Denise's before coming here found him mute
        /// for the rest of the session and no amount of speech calls brought him back. The
        /// sweep is gone, but a save made while it was live can still be carrying the damage,
        /// so his voice is set rather than assumed.
        /// </summary>
        private const string Voice = "LAMAR";

        private static void GiveVoice(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;

            try { Function.Call(Hash.SET_AMBIENT_VOICE_NAME, ped.Handle, Voice); }
            catch { /* he keeps whatever the model came with */ }
        }

        private static void Say(Ped ped, string[] lines)
        {
            if (ped == null || !ped.Exists() || lines.Length == 0) return;

            try
            {
                // Anything already coming out of him is in the way of the line we want.
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, ped.Handle);

                var line = lines[Rng.Next(lines.Length)];
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle, line, "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        public void HoldForTalk()
        {
            if (_ped == null || !_ped.Exists() || _held) return;

            _held = true;
            Say(_ped, HelloLines);

            try
            {
                var player = Game.Player.Character;
                _ped.Task.ClearAll();

                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _ped.Handle, player.Handle, -1);
                }
            }
            catch { /* he will still talk */ }
        }

        public void ReleaseFromTalk()
        {
            if (!_held || _ped == null || !_ped.Exists()) return;

            _held = false;

            Say(_ped, ByeLines);
            try
            {
                _ped.Task.ClearAll();
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle,
                              "WORLD_HUMAN_STAND_MOBILE", 0, true);
                _ped.Heading = Heading;
            }
            catch { /* he will settle */ }
        }

        public void RestoreWorld()
        {
            Despawn();

            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* teardown */ }

            _blip = null;
        }
    }
}
