using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Locations
{
    /// <summary>Which trial is being run. The order is the order they are offered in.</summary>
    internal enum Trial
    {
        /// <summary>All of them down, as fast as you can. Misses cost nothing but time.</summary>
        Rapid,

        /// <summary>All of them down and not one shot wasted. The first miss ends it.</summary>
        Clean,

        /// <summary>All of them down from a long way off. A close hit does not count.</summary>
        Long
    }

    /// <summary>
    /// A shooting range wherever somebody puts one.
    ///
    /// THERE IS NO RANGE IN THIS FILE. No coordinate, no marker, no ini section, nothing that
    /// has to be measured or corrected -- and that is the whole design, because every fixed
    /// coordinate this mod has ever typed has been wrong at least once and the two doors it
    /// shipped with were deleted over it.
    ///
    /// Instead: a range is THREE OR MORE TARGETS STANDING TOGETHER. Put them up in a spooner
    /// scene, in an alley, in a car park, on a roof, and there is a range there. Take them
    /// away and there is not. The mod is not told where anything is; it looks, and what it
    /// finds is the answer. Move the whole thing fifty metres and nothing needs editing.
    ///
    /// WHAT COUNTS AS A TARGET is a list of model names, every one of them read out of the
    /// object list on this machine rather than remembered -- the paper ones out of the gun
    /// shops, the metal plates off the bunker range, the stunt boards, and bottles and cans
    /// for a range somebody has built out of what was lying about.
    ///
    /// HOW A HIT IS SCORED, and this is the part worth reading. Not by asking the props
    /// whether they have been damaged: a prop placed by a scene is frozen and half of them
    /// are not damageable at all, so that question is a coin toss dressed up as a rule. The
    /// game will tell you where the player's last shot LANDED -- one native, no shape tests,
    /// no per-frame raycasts -- and a shot that landed within arm's reach of a target is a
    /// hit and anything else is a miss. That is also what a range actually is: it does not
    /// care what you were aiming at, it cares where the hole is.
    /// </summary>
    internal sealed class Range
    {
        /// <summary>
        /// Everything the mod will treat as something to shoot at.
        ///
        /// Every name checked against RampageFiles\Lists\ObjectList.txt. A name the install
        /// does not have simply never matches anything, which costs nothing.
        /// </summary>
        private static readonly string[] TargetModels =
        {
            // The gun shops' own paper targets.
            "prop_range_target_01", "prop_range_target_02", "prop_range_target_03",

            // The bunker range: metal plates on stands, in four sizes and two states.
            "gr_prop_gr_target_01a", "gr_prop_gr_target_01b",
            "gr_prop_gr_target_02a", "gr_prop_gr_target_02b",
            "gr_prop_gr_target_03a", "gr_prop_gr_target_03b",
            "gr_prop_gr_target_04a", "gr_prop_gr_target_04b",
            "gr_prop_gr_target_04c", "gr_prop_gr_target_04d",
            "gr_prop_gr_target_05a", "gr_prop_gr_target_05b",

            // The clubhouse ones, and the stunt boards.
            "bkr_prop_biker_target", "bkr_prop_biker_target_small",
            "as_prop_as_target_big", "as_prop_as_target_medium",
            "as_prop_as_target_small", "as_prop_as_target_small_02",
            "as_prop_as_target_grid",

            // The competition boards.
            "prop_target_comp_metal", "prop_target_comp_wood",
            "prop_target_blue", "prop_target_bull", "prop_target_bull_b",

            // AND WHAT IS LYING ABOUT. A row of bottles on a wall is a shooting range and
            // has been for as long as there have been bottles and walls.
            "prop_beer_bottle", "prop_ld_can_01", "prop_ld_can_01b", "prop_orang_can_01"
        };

        /// <summary>
        /// How near targets have to be to each other to be one range, and how far off you can
        /// be and still be offered it.
        ///
        /// Notice is generous on purpose: the whole point of a range is that you stand well
        /// back from it, and a prompt that only appears once you are stood among the targets
        /// is a prompt for a range you cannot shoot.
        /// </summary>
        private const float Notice = 90f;
        private const int Fewest = 3;

        /// <summary>How near a shot has to land. An arm's length -- see the note on the class.</summary>
        private const float HitRadius = 0.75f;

        /// <summary>Two impacts closer together than this are the same impact read twice.</summary>
        private const float SameShot = 0.05f;

        /// <summary>How far back the long trial makes you stand.</summary>
        private const float LongShot = 25f;

        /// <summary>Three, two, one.</summary>
        private const int CountIn = 3;
        private const int CountStepMs = 1000;

        /// <summary>Seconds a target is worth, for each medal. Multiplied by how many there are.</summary>
        private const float GoldEach = 1.15f;
        private const float SilverEach = 1.90f;
        private const float BronzeEach = 3.00f;

        /// <summary>How long the result stays up before the range is ready again.</summary>
        private const int ResultMs = 9000;

        /// <summary>How often the world is asked what is standing about. Not every frame.</summary>
        private const int LookEveryMs = 1500;

        private sealed class Mark
        {
            public Prop Thing;
            public Vector3 At;
            public bool Down;
        }

        private readonly List<Mark> _marks = new List<Mark>();

        private Vector3 _middle;
        private Blip _blip;

        private bool _running;
        private Trial _trial;
        private int _startedAt;
        private int _countFrom;
        private int _hits;
        private int _misses;
        private bool _failed;
        private string _why = "";

        private int _endedAt;
        private float _took;
        private int _lookedAt;

        private Vector3 _lastImpact;
        private Vector3 _stood;

        /// <summary>Set by Main. Where the best times live.</summary>
        public PlayerState State;

        public bool IsRunning => _running;

        /// <summary>True when there is a range near enough to be worth drawing a card for.</summary>
        public bool Found => _marks.Count >= Fewest;

        // ---- the tick ------------------------------------------------------------------

        public void Update()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) { Stop(false); return; }

                var now = Game.GameTime;

                Look(player, now);

                if (!Found) { Stop(false); return; }

                if (_running) { Shooting(player, now); return; }

                if (_endedAt != 0 && now - _endedAt < ResultMs) return;

                Offer(player);
            }
            catch (Exception ex)
            {
                Log.Debug("The range fell over: " + ex.Message);
                Stop(false);
            }
        }

        /// <summary>
        /// What is standing out there.
        ///
        /// Re-asked rather than remembered, because a scene can be edited between two runs and
        /// a target the player has since deleted is a target the trial waits forever for. The
        /// cost is one world query a second and a half.
        ///
        /// A RUN IS NOT INTERRUPTED BY THIS. Mid-trial the list is left exactly as it was --
        /// a prop streaming out for a moment at the far end of a car park would otherwise
        /// finish the trial early and call it a win.
        /// </summary>
        private void Look(Ped player, int now)
        {
            if (_running) return;
            if (now - _lookedAt < LookEveryMs) return;

            _lookedAt = now;

            _marks.Clear();

            try
            {
                var near = World.GetNearbyProps(player.Position, Notice);

                foreach (var prop in near)
                {
                    if (prop == null || !prop.Exists()) continue;
                    if (!IsTarget(prop)) continue;

                    _marks.Add(new Mark { Thing = prop, At = prop.Position });
                }
            }
            catch
            {
                _marks.Clear();
            }

            if (_marks.Count < Fewest) { Unblip(); return; }

            var sum = Vector3.Zero;
            foreach (var m in _marks) sum += m.At;

            _middle = sum / _marks.Count;

            Blip();
        }

        private static bool IsTarget(Prop prop)
        {
            try
            {
                var hash = prop.Model.Hash;

                foreach (var name in TargetModels)
                {
                    if (hash == Function.Call<int>(Hash.GET_HASH_KEY, name)) return true;
                }
            }
            catch
            {
                // Unreadable model, so not a target.
            }

            return false;
        }

        // ---- being offered it -----------------------------------------------------------

        private void Offer(Ped player)
        {
            if (player.IsInVehicle()) return;

            // WHAT IS IN HIS HANDS, not IS_PED_ARMED with a flag. That native's flags are
            // documented three different ways and the wrong one either offers a trial to a man
            // holding a bat or refuses one to a man holding a rifle. This question has one
            // answer.
            var armed = false;

            try { armed = player.Weapons.Current != null && player.Weapons.Current.Hash != WeaponHash.Unarmed; }
            catch { armed = true; }

            if (!armed) return;

            Help.ShowThisFrame("~INPUT_CONTEXT~ to start the " + Name(_trial) +
                               " -- " + Blurb(_trial) + ".  Arrows to change it.");

            // LEFT AND RIGHT, because that is how every other shelf in this mod is walked --
            // the gun counter, the racks, the parts. A key invented for this one screen is a
            // key somebody has to learn, and it is also a key that might already belong to
            // another mod on the machine. The arrows do nothing on foot in a vanilla game.
            var by = Game.IsControlJustPressed(Control.PhoneRight) ? 1
                   : Game.IsControlJustPressed(Control.PhoneLeft) ? -1
                   : 0;

            if (by != 0)
            {
                _trial = (Trial)((((int)_trial + by) % 3 + 3) % 3);
                Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (Game.IsControlJustPressed(Control.Context)) Begin(player);
        }

        private void Begin(Ped player)
        {
            _running = true;
            _countFrom = Game.GameTime;
            _startedAt = 0;
            _hits = 0;
            _misses = 0;
            _failed = false;
            _why = "";
            _took = 0f;
            _endedAt = 0;
            _stood = player.Position;
            _lastImpact = Vector3.Zero;

            foreach (var m in _marks) m.Down = false;

            // WHATEVER HE HAS ALREADY SHOT AT is not part of this. The impact coordinate the
            // game hands back is the last one whenever it happened, so without this the first
            // read of the run is a shot from before it started -- and on a range that shot is
            // very often already sat on a target.
            try
            {
                var junk = new OutputArgument();
                Function.Call<bool>(Hash.GET_PED_LAST_WEAPON_IMPACT_COORD, player.Handle, junk);
                _lastImpact = junk.GetResult<Vector3>();
            }
            catch
            {
                // Then the first shot is read fresh anyway.
            }

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            Log.Info("Range: " + Name(_trial) + " started on " + _marks.Count + " target(s).");
        }

        // ---- running it -----------------------------------------------------------------

        private void Shooting(Ped player, int now)
        {
            if (player.IsInVehicle()) { Give("you got in a car."); return; }

            // The count in. Nothing is scored until it lands.
            if (_startedAt == 0)
            {
                if (now - _countFrom < CountIn * CountStepMs) return;

                _startedAt = now;
                Hud.PlaySound("TIMER_STOP", "HUD_MINI_GAME_SOUNDSET");
                return;
            }

            var shot = Impact(player);

            if (shot != Vector3.Zero) Landed(player, shot);

            if (_failed) return;

            var left = 0;
            foreach (var m in _marks) if (!m.Down) left++;

            if (left == 0) Won(now);
        }

        /// <summary>
        /// Where his last shot landed, or nothing if it is one already counted.
        ///
        /// The native answers with the LAST impact whenever it happened rather than with a new
        /// one, so the only way to tell a fresh shot from the same shot read again is that the
        /// hole has moved. Two rounds into the same five centimetres count once, which on a
        /// target does not matter -- it is down after the first.
        /// </summary>
        private Vector3 Impact(Ped player)
        {
            try
            {
                var where = new OutputArgument();

                if (!Function.Call<bool>(Hash.GET_PED_LAST_WEAPON_IMPACT_COORD, player.Handle, where))
                {
                    return Vector3.Zero;
                }

                var at = where.GetResult<Vector3>();

                if (at == Vector3.Zero) return Vector3.Zero;
                if (at.DistanceTo(_lastImpact) < SameShot) return Vector3.Zero;

                _lastImpact = at;
                return at;
            }
            catch
            {
                return Vector3.Zero;
            }
        }

        private void Landed(Ped player, Vector3 at)
        {
            Mark hit = null;
            var best = HitRadius;

            foreach (var m in _marks)
            {
                if (m.Down) continue;

                var d = m.At.DistanceTo(at);
                if (d >= best) continue;

                best = d;
                hit = m;
            }

            if (hit == null) { Missed(); return; }

            // THE LONG ONE MEASURES THE SHOT, NOT THE MAN. Where he was stood when it landed,
            // against the target it landed on -- so walking up to the close ones after
            // starting from the back does not get counted as a long shot.
            if (_trial == Trial.Long && player.Position.DistanceTo(hit.At) < LongShot)
            {
                Missed("that one was too close.");
                return;
            }

            hit.Down = true;
            _hits++;

            Down(hit);

            Hud.PlaySound("HIT", "HUD_MINI_GAME_SOUNDSET");
        }

        private void Missed(string why = null)
        {
            _misses++;

            if (_trial != Trial.Clean)
            {
                if (why != null) Notify.Problem(why);
                return;
            }

            Give(why ?? "that's a miss. clean means clean.");
        }

        /// <summary>
        /// A target that has been hit, taken out of the run.
        ///
        /// FADED RATHER THAN DELETED, and it goes back at the end. These are somebody's own
        /// props out of their own scene -- deleting one to score a point would take a target
        /// off the range permanently and the range would wear away a run at a time.
        /// </summary>
        private static void Down(Mark m)
        {
            try
            {
                if (m.Thing == null || !m.Thing.Exists()) return;

                Function.Call(Hash.SET_ENTITY_ALPHA, m.Thing.Handle, 90, false);
            }
            catch
            {
                // It still counts.
            }
        }

        private void Standing()
        {
            foreach (var m in _marks)
            {
                try
                {
                    if (m.Thing != null && m.Thing.Exists())
                    {
                        Function.Call(Hash.RESET_ENTITY_ALPHA, m.Thing.Handle);
                    }
                }
                catch
                {
                    // The scene will rebuild it either way.
                }

                m.Down = false;
            }
        }

        // ---- how it ends ------------------------------------------------------------------

        private void Won(int now)
        {
            _took = (now - _startedAt) / 1000f;
            _running = false;
            _endedAt = now;
            _failed = false;

            var medal = Medal(_took, _marks.Count);
            var beat = Record(_trial, _took);

            Standing();

            Hud.PlaySound(medal == "GOLD" ? "RANK_UP" : "CHECKPOINT_PERFECT",
                          medal == "GOLD" ? "HUD_AWARDS" : "HUD_MINI_GAME_SOUNDSET");

            Log.Info("Range: " + Name(_trial) + " done in " + _took.ToString("0.00") +
                     "s, " + _hits + " hit, " + _misses + " missed, " + medal +
                     (beat ? ", a new best." : "."));
        }

        private void Give(string why)
        {
            _failed = true;
            _running = false;
            _endedAt = Game.GameTime;
            _why = why;

            Standing();

            Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            Log.Info("Range: " + Name(_trial) + " ended -- " + why);
        }

        private void Stop(bool quietly)
        {
            if (_running)
            {
                _running = false;
                Standing();

                if (!quietly) Log.Info("Range: walked away from it.");
            }

            _marks.Clear();
            Unblip();
        }

        /// <summary>What it earns, against how many there were to hit.</summary>
        private static string Medal(float took, int many)
        {
            if (many <= 0) return "";

            if (took <= GoldEach * many) return "GOLD";
            if (took <= SilverEach * many) return "SILVER";
            if (took <= BronzeEach * many) return "BRONZE";

            return "";
        }

        /// <summary>
        /// The best so far, kept per trial and per HOW MANY TARGETS.
        ///
        /// A time is meaningless on its own here. Six targets is not the same job as
        /// twenty, and the range is whatever somebody put up this afternoon -- so a best
        /// carried across two different set-ups would be a record nobody could ever beat and
        /// a record nobody could ever lose, depending which way round they built it.
        /// </summary>
        private bool Record(Trial trial, float took)
        {
            if (State == null) return false;

            var key = "range:" + trial.ToString().ToLowerInvariant() + ":" + _marks.Count;

            var had = State.BestFor(key);

            if (had > 0.01f && had <= took) return false;

            State.SetBest(key, took);
            return true;
        }

        private float Best()
        {
            if (State == null) return 0f;

            return State.BestFor("range:" + _trial.ToString().ToLowerInvariant() + ":" + _marks.Count);
        }

        private static string Name(Trial t)
        {
            switch (t)
            {
                case Trial.Clean: return "CLEAN RUN";
                case Trial.Long: return "LONG SHOTS";
                default: return "RAPID";
            }
        }

        private static string Blurb(Trial t)
        {
            switch (t)
            {
                case Trial.Clean: return "every one down, and not one shot wasted";
                case Trial.Long: return "every one down from " + ((int)LongShot) + " metres back";
                default: return "every one down, as fast as you can";
            }
        }

        // ---- the blip -----------------------------------------------------------------------

        private void Blip()
        {
            try
            {
                if (_blip != null && _blip.Exists())
                {
                    _blip.Position = _middle;
                    return;
                }

                _blip = World.CreateBlip(_middle);
                if (_blip == null || !_blip.Exists()) return;

                // 110, radar_gun_shop, out of BLIPS.md. Every sprite number in this mod comes
                // off that list rather than out of somebody's head, and a number the game does
                // not have draws a plain dot -- which looks like a working blip for something
                // else and is the worst of the three outcomes.
                _blip.Sprite = (BlipSprite)110;
                _blip.Color = BlipColor.White;
                _blip.Scale = 0.8f;
                _blip.IsShortRange = true;
                _blip.Name = "Shooting range";
            }
            catch
            {
                _blip = null;
            }
        }

        private void Unblip()
        {
            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* it goes with the session */ }

            _blip = null;
        }

        // ---- the card -------------------------------------------------------------------------

        public void Draw()
        {
            if (!Found) return;

            var now = Game.GameTime;

            if (!_running && _endedAt != 0 && now - _endedAt < ResultMs) { Result(); return; }
            if (!_running) return;

            const float w = 0.150f;
            const float h = 0.062f;

            var left = 0.5f - w * 0.5f;
            var top = 0.795f;

            Theme.Panel(left, top, w, h);

            var x = left + 0.010f;

            Hud.Text(Name(_trial), x, top + 0.006f, 0.26f,
                     Palette.Alpha(Palette.TextDim, 210), Hud.FontLabel, centre: false);

            // The count in, then the clock.
            if (_startedAt == 0)
            {
                var step = (now - _countFrom) / CountStepMs;
                var say = step >= CountIn ? "GO" : (CountIn - step).ToString();

                Hud.Text(say, left + w * 0.5f, top + 0.020f, 0.90f, Palette.Brand, Hud.FontLabel);
                return;
            }

            var run = (now - _startedAt) / 1000f;

            Hud.TextRight(run.ToString("0.00") + "s", left + w - 0.010f, top + 0.004f, 0.34f,
                          Palette.Text, Hud.FontLabel);

            var down = 0;
            foreach (var m in _marks) if (m.Down) down++;

            Hud.Text(down + " / " + _marks.Count, x, top + 0.030f, 0.30f,
                     Palette.Text, Hud.FontLabel, centre: false);

            if (_misses > 0)
            {
                Hud.TextRight(_misses + " missed", left + w - 0.010f, top + 0.032f, 0.24f,
                              _trial == Trial.Clean ? Palette.Danger : Palette.Warn, Hud.FontLabel);
            }

            var best = Best();

            if (best > 0.01f)
            {
                Hud.Text("best " + best.ToString("0.00") + "s", x, top + 0.046f, 0.22f,
                         Palette.Alpha(Palette.TextDim, 190), Hud.FontLabel, centre: false);
            }
        }

        private void Result()
        {
            const float w = 0.190f;
            const float h = 0.070f;

            var left = 0.5f - w * 0.5f;
            var top = 0.780f;

            Theme.Panel(left, top, w, h);

            var x = left + 0.012f;

            if (_failed)
            {
                Hud.Text(Name(_trial) + "  --  NO GOOD", x, top + 0.008f, 0.28f,
                         Palette.Danger, Hud.FontLabel, centre: false);

                Hud.Text(_why, x, top + 0.032f, 0.26f,
                         Palette.Alpha(Palette.TextDim, 210), Hud.FontBody, centre: false);
                return;
            }

            var medal = Medal(_took, _marks.Count);

            Hud.Text(Name(_trial), x, top + 0.008f, 0.28f,
                     Palette.Alpha(Palette.TextDim, 210), Hud.FontLabel, centre: false);

            Hud.TextRight(_took.ToString("0.00") + "s", left + w - 0.012f, top + 0.006f, 0.40f,
                          Palette.Text, Hud.FontLabel);

            Hud.Text(_hits + " hit" + (_misses > 0 ? ",  " + _misses + " missed" : ",  clean"),
                     x, top + 0.034f, 0.24f,
                     Palette.Alpha(Palette.TextDim, 200), Hud.FontLabel, centre: false);

            if (medal.Length > 0)
            {
                var ink = medal == "GOLD" ? Palette.Cash
                        : medal == "SILVER" ? Palette.Text
                        : Palette.Warn;

                Hud.TextRight(medal, left + w - 0.012f, top + 0.036f, 0.28f, ink, Hud.FontLabel);
            }

            var best = Best();

            if (best > 0.01f && Math.Abs(best - _took) < 0.005f)
            {
                Hud.Text("a new best", x, top + 0.050f, 0.24f, Palette.Cash, Hud.FontLabel, centre: false);
            }
        }
    }
}
