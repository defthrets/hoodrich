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
    /// Deliberately a second man rather than another line on Gerald's list. Gerald is supply
    /// and standing -- weight, prices, whether you are in. Lamar is jobs. Keeping them apart
    /// means each is somewhere you go for a reason, instead of one NPC being a menu with
    /// everything bolted to it.
    /// </summary>
    internal sealed class Fixer
    {
        /// <summary>The courtyard on Chamberlain, where he waits.</summary>
        /// <summary>
        /// The lot behind the lab, not the Chamberlain courtyard.
        ///
        /// He is where the party is now. Everything about him follows this one coordinate --
        /// his blip, the walk-up prompt, where a raid defends, where a job hands in -- so the
        /// move is this line and the four spots around it rather than a rewrite.
        /// </summary>
        internal static readonly Vector3 Spot = new Vector3(-199.669f, -1718.827f, 32.664f);
        private const float Heading = 124.548f;

        private const float SpawnRange = 110f;
        private const float DespawnRange = 190f;
        private const float TalkRange = 3.0f;
        private const int UpdateIntervalMs = 700;

        /// <summary>
        /// radar_ped_gang_leader, which is what he actually is now.
        ///
        /// It was radar_lester -- a favour-broker on a phone -- with a comment above it calling
        /// it a skull, which it also was not. He is the one you run with and the one who hands
        /// out the work, and there is a sprite in the game for exactly that.
        /// </summary>
        private const int Sprite = 855;

        private static readonly string[] Models =
        {
            "ig_lamardavis", "csb_lamardavis", "ig_lamardavis_02", "g_m_y_famca_01"
        };

        private readonly Affiliation _crew;

        private Ped _ped;
        private Blip _blip;
        private int _lastUpdate;
        private bool _held;

        /// <summary>
        /// Whether a mission has him.
        ///
        /// Separate from _held, which is the two seconds he spends facing you during a
        /// conversation. This is the whole job: he is off his corner, out of this class's
        /// hands, and nothing in here may spawn him, despawn him or re-task him until he
        /// comes back.
        /// </summary>
        private bool _lent;
        private bool _talkHeld;

        /// <summary>
        /// Whether the job he lent himself to is finished and waiting to be handed in.
        ///
        /// A deadlock lived exactly here. Lend sets _lent, TakeBack clears it, and TakeBack
        /// only runs when you COLLECT -- but collecting means talking to him, and being lent
        /// is what stops you talking to him. So the bike ride ended, he walked back to his own
        /// corner, stood on it, and could not be spoken to by the man stood next to him.
        ///
        /// The lend is still right and still has to outlast the ride: TakeBack puts him back
        /// on his mark or despawns him if he has drifted, and doing either while he is walking
        /// home would undo the walk. This is the narrower question the guard actually wanted to
        /// ask -- not "is a job using him" but "is a job still using him".
        /// </summary>
        public Func<bool> Finished;

        private bool HandInDue
        {
            get
            {
                try { return Finished != null && Finished(); }
                catch { return false; }
            }
        }

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

            // On a job, so hands off entirely.
            //
            // This is not politeness, it is the difference between him coming on the ride and
            // him evaporating halfway down Innocence. Despawn fires at 190 metres from his
            // corner and the courts are further than that, so without this the mission would
            // watch its own passenger get deleted out from under it.
            //
            // The corner blip goes with him, because a marker reading "Lamar" on a corner he
            // is demonstrably not standing on is worse than no marker.
            if (_lent)
            {
                // The blip comes back with him. Everything else stays hands-off -- the mission
                // still owns the ped until it hands him over, so this must not spawn, despawn
                // or re-task him. It only decides whether there is a marker on the corner he is
                // demonstrably standing on.
                if (HandInDue) SyncBlip();
                else DropBlip();

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

            Guard();
        }

        /// <summary>The compact rifle he keeps for the days somebody comes down the block.</summary>
        private const string Rifle = "WEAPON_COMPACTRIFLE";

        /// <summary>How far out he will notice somebody who has come for this block.</summary>
        private const float DangerRange = 34f;

        /// <summary>
        /// How close an armed rival has to be before he is a problem rather than a passer-by.
        ///
        /// Two ranges on purpose. Anybody already SHOOTING at him or at you is a problem from
        /// wherever they are doing it; somebody who merely hates the set and happens to be
        /// carrying is a problem when they are on top of us. Without the second, tighter number
        /// he opens up on every Balla who drives past the end of the road, which is not a man
        /// defending his corner, it is a man with a rifle and no judgement.
        /// </summary>
        private const float ComingRange = 20f;

        /// <summary>Close enough to his mark to stand back on it rather than keep walking.</summary>
        private const float BackRange = 1.6f;

        private bool _fighting;
        private bool _walkingHome;

        /// <summary>
        /// Fights back when it comes to him, and goes back to his corner when it stops.
        ///
        /// He was standing through raids with his phone out. That is not stoicism, it is the
        /// scenario: a ped on a looping scenario with non-temporary events blocked has been
        /// told in as many words to ignore gunfire, and he did exactly that while the yard was
        /// being shot up around him.
        ///
        /// So the block comes OFF when there is somebody to fight and goes back ON when there
        /// is not, which is the whole trick -- blocked is what keeps him on his mark the other
        /// ninety-nine per cent of the time, and permanently unblocking him to fix this would
        /// have him wandering off after ambient events for the rest of the session.
        /// </summary>
        private void Guard()
        {
            if (_lent) return;
            if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return;

            var threat = Threat();

            if (threat != null)
            {
                // Already on him. Re-issuing over a running combat task restarts the aim every
                // time and he never gets a round off.
                if (_fighting &&
                    Function.Call<bool>(Hash.IS_PED_IN_COMBAT, _ped.Handle, threat.Handle)) return;

                _fighting = true;
                _walkingHome = false;

                try
                {
                    // Whatever else happens on this block, it does not happen to him.
                    Protect(true);

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _ped.Handle, false);
                    _ped.BlockPermanentEvents = false;

                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _ped.Handle, 5, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _ped.Handle, 0, true);

                    // 1 is CanUseVehicles, and the answer is no. He is defending a corner, not
                    // starting a pursuit -- a fixer who drives off after a carload is a fixer
                    // who is not on his corner when you come back for work.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _ped.Handle, 1, false);

                    Function.Call(Hash.SET_PED_COMBAT_ABILITY, _ped.Handle, 2);
                    Function.Call(Hash.SET_PED_COMBAT_RANGE, _ped.Handle, 1);
                    Function.Call(Hash.SET_PED_ACCURACY, _ped.Handle, 55);

                    Function.Call(Hash.SET_CURRENT_PED_WEAPON, _ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY, Rifle), true);

                    _ped.Task.ClearAll();
                    Function.Call(Hash.TASK_COMBAT_PED, _ped.Handle, threat.Handle, 0, 16);
                }
                catch (Exception ex)
                {
                    Log.Debug("Lamar could not get into it: " + ex.Message);
                }

                return;
            }

            if (_fighting)
            {
                _fighting = false;
                GoHome();
                return;
            }

            // Still walking back. Put him on the mark once he is on it, so he ends up facing
            // the way he is supposed to rather than whichever way the last shot came from.
            if (_walkingHome && _ped.Position.DistanceTo(Spot) <= BackRange)
            {
                _walkingHome = false;
                Stand();
            }
        }

        /// <summary>
        /// The nearest person who has actually come for somebody, or null.
        ///
        /// Two different tests, because "enemy" and "threat" are not the same word. Somebody
        /// shooting at him, or at you, is a threat at any range this can see. Somebody who
        /// merely belongs to a set that hates ours is a threat when he is armed AND close.
        /// </summary>
        private Ped Threat()
        {
            Ped worst = null;
            var nearest = float.MaxValue;

            try
            {
                var you = Game.Player.Character;

                foreach (var ped in World.GetNearbyPeds(_ped.Position, DangerRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == _ped.Handle) continue;
                    if (you != null && ped.Handle == you.Handle) continue;

                    var atUs = Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, _ped.Handle) ||
                               (you != null &&
                                Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, you.Handle));

                    var gap = _ped.Position.DistanceTo(ped.Position);

                    if (!atUs)
                    {
                        // 4 is dislike and 5 is hate. Anything below that is somebody who lives
                        // here.
                        var feeling = Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_PEDS,
                                                         _ped.Handle, ped.Handle);

                        if (feeling < 4 || feeling > 5) continue;
                        if (gap > ComingRange) continue;

                        // 7 is every weapon type there is.
                        if (!Function.Call<bool>(Hash.IS_PED_ARMED, ped.Handle, 7)) continue;
                    }

                    if (gap >= nearest) continue;

                    nearest = gap;
                    worst = ped;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Lamar could not see who was there: " + ex.Message);
            }

            return worst;
        }

        /// <summary>
        /// Back to the corner once it is quiet, on his feet rather than by teleport.
        /// </summary>
        private void GoHome()
        {
            try
            {
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _ped.Handle, 46, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _ped.Handle, 5, false);

                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _ped.Handle, true);
                _ped.BlockPermanentEvents = true;

                _ped.Task.ClearAll();

                // Gun away. He is about to stand on a corner talking on his phone.
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, _ped.Handle,
                              Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_UNARMED"), true);

                if (_ped.Position.DistanceTo(Spot) > BackRange)
                {
                    _walkingHome = true;

                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, _ped.Handle,
                                  Spot.X, Spot.Y, Spot.Z, 1.2f, 20000, BackRange, 0, Heading);

                    return;
                }

                Stand();
            }
            catch (Exception ex)
            {
                Log.Debug("Lamar could not get back to his corner: " + ex.Message);

                _walkingHome = false;
            }
        }

        /// <summary>Him, on his mark, facing the right way, doing what he was doing.</summary>
        private void Stand()
        {
            try
            {
                _ped.Task.ClearAll();
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle,
                              "WORLD_HUMAN_STAND_MOBILE", 0, true);
                _ped.Heading = Heading;
            }
            catch { /* he is stood there either way */ }
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

            // The probe is BELIEVED ONLY IF IT AGREES, which is the whole of this fix.
            //
            // It used to look down from twenty metres above the mark and take whatever it hit,
            // unconditionally. In the Chamberlain courtyard that was the courtyard. In the lot
            // behind the lab it is the ROOF of the shed he is stood next to, and that is where
            // he appeared -- twenty feet up, looking down at his own blip.
            //
            // The bike ride learnt this exact lesson and wrote it down: every authored position
            // in this mod was read off the HUD while stood on the spot, so the height is
            // already right, and a downward probe finds the first thing it meets rather than
            // the floor somebody measured. A probe that disagrees by more than a storey is
            // wrong about a coordinate a person took by standing on it.
            //
            // So it is kept for the case it was written for -- a mark taken a foot off the
            // deck, ground that has moved under a mod -- and ignored for the case that put him
            // on a roof.
            try
            {
                if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 3f), out var groundZ,
                                          GetGroundHeightMode.Normal) &&
                    groundZ > 0f && Math.Abs(groundZ - spot.Z) <= 2.5f)
                {
                    spot.Z = groundZ;
                }
            }
            catch
            {
                // Use the authored height, which is the one somebody stood on.
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

                    // Under the jacket, not in his hands. He is stood on a corner talking on
                    // his phone; the rifle is what comes out when somebody turns up, and a man
                    // holding one while he offers you work is a different scene entirely.
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, _ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY, Rifle), 250, false, false);

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

        /// <summary>
        /// Wraps him in plot armour for the length of a job, or takes it off again.
        ///
        /// He was shot dead on the bike ride, and the mission did not fail -- it STOPPED.
        /// Every phase tick opens by checking he is alive and returning if he is not, which is
        /// the right guard for one frame and a deadlock for the rest of the job: no phase
        /// advances, no objective changes, and nothing left that can end it.
        ///
        /// This belongs here rather than in any one mission because Lend is the single door
        /// every job takes him through and TakeBack is the only way back out. On his corner he
        /// is an ordinary man and can be shot like one. From the moment a job borrows him he
        /// cannot die, cannot be dropped, and cannot be pulled off his bike.
        /// </summary>
        private void Protect(bool on)
        {
            if (_ped == null || !_ped.Exists()) return;

            try
            {
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _ped.Handle, on);
                Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, _ped.Handle, !on);
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, _ped.Handle, !on);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _ped.Handle, !on);

                // Not dying and not going down are different things, and the second one is what
                // actually breaks the ride. Police shooting at a man on a bike knock him OFF
                // it, and an invincible Lamar face down in the road is no more use to the
                // mission than a dead one. 1 is never; 0 is the default he goes back to.
                Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, _ped.Handle, on ? 1 : 0);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, _ped.Handle, !on);

                // Whatever he took before the armour went on. A job that starts with him on
                // one bar of health is a job that starts with him already knocked about.
                if (on) _ped.Health = _ped.MaxHealth;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not " + (on ? "protect" : "release") + " Lamar: " + ex.Message);
            }
        }

        /// <summary>
        /// Hands him to a mission. Null if he is not around to be handed over.
        /// </summary>
        public Ped Lend()
        {
            if (_ped == null || !_ped.Exists() || !_ped.IsAlive) return null;

            _lent = true;
            Protect(true);

            // The conversation is over -- whoever is borrowing him is about to task him, and a
            // hold left on would have him turning to face you for the length of a bike ride.
            _held = false;

            try { _ped.Task.ClearAll(); }
            catch { /* the mission is about to task him anyway */ }

            return _ped;
        }

        /// <summary>
        /// Takes him back off a mission.
        ///
        /// Near his corner he simply goes back to standing on it. Anywhere else he is let go
        /// entirely, because walking him home across Davis is a man the player would have to
        /// watch, and a fresh one is standing on the corner the next time they come round.
        /// Dead is the same case -- the body is released and the corner refills.
        /// </summary>
        public void TakeBack()
        {
            if (!_lent) return;

            _lent = false;
            _fighting = false;
            _walkingHome = false;
            Protect(false);

            if (_ped == null || !_ped.Exists() || !_ped.IsAlive)
            {
                Despawn();
                return;
            }

            try
            {
                if (_ped.Position.DistanceTo(Spot) > HomeRange)
                {
                    Despawn();
                    return;
                }

                _ped.Task.ClearAll();
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle,
                              "WORLD_HUMAN_STAND_MOBILE", 0, true);
                _ped.Heading = Heading;
            }
            catch
            {
                Despawn();
            }
        }

        /// <summary>Near enough to his corner to just stand back on it.</summary>
        private const float HomeRange = 30f;

        private void DropBlip()
        {
            if (_blip == null) return;

            try { if (_blip.Exists()) _blip.Delete(); }
            catch { /* it goes when the area does */ }

            _blip = null;
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
            _fighting = false;
            _walkingHome = false;
            _held = false;
            _lent = false;
        }

        /// <summary>Offers the conversation and opens it, same button as the leaders.</summary>
        public void UpdatePrompt()
        {
            // Not while a job has him.
            //
            // This never checked, so the "talk to Lamar" prompt stayed live through the whole
            // bike ride -- including stood in the 24/7 with him -- and opening it put his
            // ordinary mission-giver conversation on screen in the middle of a mission.
            //
            // Worse than untidy: the ROBBERY offer refuses to open while another conversation
            // is up, so the one screen the mission actually needed could be blocked by the one
            // that should not have been available. Lend sets this and TakeBack clears it, so
            // he goes back to being a man you can talk to the moment the job is over.
            if (_lent && !HandInDue) return;

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
