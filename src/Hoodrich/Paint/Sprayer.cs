using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Control = GTA.Control;

namespace Hoodrich.Paint
{
    /// <summary>
    /// Holding the trigger on a fire extinguisher and getting paint.
    ///
    /// The whole mod is this: while the extinguisher is out and the trigger is down, probe
    /// forward, and put a splatter on whatever the probe found. Everything else -- the pool,
    /// the picker, the plume -- exists to make that read as spraying rather than as decals
    /// appearing.
    ///
    /// GATED ON THE CONTROL, NOT ON IS_PED_SHOOTING. The extinguisher is a weapon by the game's
    /// reckoning but a strange one, and whether it counts as "shooting" is not something to
    /// find out from a mod that has already shipped. Reading the attack control is true
    /// whatever the answer, and it is also true on the frame the trigger goes down rather than
    /// once the plume has spun up.
    /// </summary>
    internal sealed class Sprayer
    {
        private const string Extinguisher = "WEAPON_FIREEXTINGUISHER";

        /// <summary>
        /// The plume, tinted.
        ///
        /// scr_lamgraff_paint_spray IS ROCKSTAR OWN PAINT SPRAY. It is the effect their
        /// graffiti scene uses -- player_scene_f_lamgraff starts it looped and then calls
        /// SET_PARTICLE_FX_LOOPED_COLOUR on it, which is exactly what this mod wants to do and
        /// is proof it takes an arbitrary colour rather than merely tolerating one.
        ///
        /// Found by pulling every ptfx name out of the decompiled script set rather than
        /// guessed, which matters more here than usual: a particle name a build does not have
        /// plays as silence, and silence is indistinguishable from the feature not working.
        ///
        /// THE FIRE TRUCK WATER JET IS NOT IN THAT LIST. Nothing in the entire decompiled set
        /// names a water-cannon effect, which is what you would expect of something driven by a
        /// vehicle weapon rather than by a script -- there is no name for a script to ask for.
        /// A paint spray is the better answer regardless: right shape, already built to be told
        /// what colour to be, and it is what the game itself reaches for when somebody paints
        /// a wall.
        ///
        /// A SECOND EFFECT RIDES ALONGSIDE IT ON THE EXTINGUISHER: a tinted cloud, because an
        /// extinguisher discharges a volume and not a thin line, and the jet on its own reads
        /// as a laser pointer. The can does not get one -- a spray can does not billow, and
        /// giving both the same plume was what made the two looks feel like one tool in a hat.
        /// </summary>
        private sealed class Plume
        {
            /// <summary>The named asset holding it, or null for core, which takes no name.</summary>
            public readonly string Asset;

            public readonly string Name;
            public readonly float Size;

            /// <summary>
            /// Whether to call USE_PARTICLE_FX_ASSET before starting it.
            ///
            /// Rockstar's own graffiti scene does NOT call it for the core asset -- it just
            /// requests and starts. Mods habitually call USE("core") and it usually works,
            /// but "usually" is doing real work in that sentence, so both orders are in the
            /// ladder and whichever starts is the one that gets remembered.
            /// </summary>
            public readonly bool Select;

            public Plume(string asset, string name, float size, bool select)
            {
                Asset = asset;
                Name = name;
                Size = size;
                Select = select;
            }
        }

        /// <summary>The thin line, which is what tells you where the paint is about to go.</summary>
        private static readonly Plume[] Jets =
        {
            // scr_playerlamgraff IS THE ASSET, and neither of the two names I tried before
            // was it. A ptfx asked for out of the wrong asset does not throw and does not log,
            // it plays nothing -- which is why a whole session went by with no jet and no
            // complaint. Confirmed from a working implementation rather than guessed again.
            new Plume("scr_playerlamgraff", "scr_lamgraff_paint_spray", 1.1f, true),

            // Real core effects, all three of which exist. ent_sht_extinguisher is literally
            // the extinguisher discharge, which is the right shape whichever tool is in hand.
            new Plume("core", "ent_sht_extinguisher", 0.8f, true),
            new Plume("core", "ent_sht_water", 0.7f, true),
            new Plume("core", "ent_sht_steam", 0.6f, true)
        };

        /// <summary>
        /// The cloud, on the extinguisher only.
        ///
        /// EVERY NAME HERE IS ONE THE GAME OWN SCRIPTS ACTUALLY USE. What sat here before was
        /// ent_amb_smoke_foundry, which appears nowhere in the decompiled set because I made it
        /// up -- and an invented particle name does not throw or log, it plays as nothing,
        /// which from the outside is identical to the feature being switched off.
        ///
        /// Ordered by how near the shape is to a discharging extinguisher: a steam cone first,
        /// an exhaled plume behind it, then thick building smoke that has to stream in.
        /// </summary>
        private static readonly Plume[] Clouds =
        {
            // The same three confirmed core effects, bigger and softer than the jet so they
            // read as a cloud around it rather than a second line beside it. My earlier list
            // here paired real effect names with GUESSED assets, which fails exactly as
            // silently as an invented name does.
            new Plume("core", "ent_sht_steam", 1.8f, true),
            new Plume("core", "ent_sht_water", 1.6f, true),
            new Plume("core", "ent_sht_extinguisher", 1.6f, true)
        };

        private readonly PaintConfig _cfg;
        private readonly Marks _marks;

        private int _nextDab;
        private int _fx = -1;
        private int _cloud = -1;
        private bool _wasSpraying;

        public Sprayer(PaintConfig cfg, Marks marks)
        {
            _cfg = cfg;
            _marks = marks;
        }

        /// <summary>What the picker last set.</summary>
        public Color Colour = Color.FromArgb(255, 40, 200, 90);

        /// <summary>
        /// The picker's dial, as a MULTIPLIER rather than a width.
        ///
        /// The width itself comes from how far away the wall is -- see Dab -- so a fixed
        /// number here would fight the thing that makes it read as spray. This is nozzle
        /// pressure: same plume, more or less of it.
        /// </summary>
        public float Scale = 1f;

        /// <summary>
        /// The visible thing the plume comes out of, set from outside each tick.
        ///
        /// 0 means there is not one, and the hand is used instead.
        /// </summary>
        public int Nozzle;

        /// <summary>True while paint is actually coming out, for anything that wants to know.</summary>
        public bool Spraying { get; private set; }

        public void Update()
        {
            var spraying = Holding();

            if (spraying != _wasSpraying)
            {
                _wasSpraying = spraying;

                if (spraying) StartPlume();
                else StopPlume();
            }

            Spraying = spraying;

            if (!spraying) return;

            var now = Game.GameTime;
            if (now < _nextDab) return;

            // Rate rather than every frame. A dab per frame at 60fps empties the decal pool in
            // about seven seconds and puts three hundred splatters inside one square metre.
            _nextDab = now + (int)(1000f / Math.Max(1f, _cfg.Rate));

            Dab();
        }

        /// <summary>Extinguisher out, trigger down.</summary>
        private bool Holding()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive) return false;

                var want = Function.Call<uint>(Hash.GET_HASH_KEY, Extinguisher);
                var got = Function.Call<uint>(Hash.GET_SELECTED_PED_WEAPON, me.Handle);

                if (got != want) return false;
                if (!_cfg.PaintEnabled) return false;

                return Game.IsControlPressed(Control.Attack) ||
                       Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.Attack);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>One splatter, wherever the spray is pointing.</summary>
        private void Dab()
        {
            var hit = Surface.InFront(_cfg.LiveRange);
            if (!hit.Landed) return;

            // OFF THE SURFACE BY A HAIR. A decal placed exactly on the geometry fights it for
            // the same pixels and flickers -- z-fighting, and at spray rates it flickers a
            // hundred times a second across a whole wall.
            var at = hit.At + hit.Normal * 0.02f;

            // Into the wall, which is the opposite of the way it faces.
            var into = -hit.Normal;
            // A DIFFERENT WAY UP EVERY TIME. The full circle, not a nudge -- a splatter has
            // no correct orientation, so there is nothing to stay near, and anything less than
            // the whole turn leaves the repeat visible.
            var side = Surface.Along(hit.Normal, (float)(_rng.NextDouble() * Math.PI * 2.0));

            // THE FURTHER THE WALL, THE WIDER THE SPLATTER, and this is what turns a stream of
            // decals into a spray. Up close it is a tight dot you can write with; at arm's
            // reach and beyond it blooms into something you cover a garage door with.
            //
            // Quadratic rather than a cone. A cone is what a spray geometrically is and it is
            // not what one looks like -- the plume holds together while it has pressure and
            // opens out as it loses it, so the far half widens faster than the near half.
            var away = hit.Away;

            var size = _cfg.LiveSizeAtOneMetre *
                       (float)Math.Pow(Math.Max(0.2f, away), _cfg.LiveSpreadPower) *
                       Scale;

            // A little variation, or a held trigger paints one splatter repeatedly in place and
            // reads as a decal rather than as spray.
            size *= 0.85f + (float)_rng.NextDouble() * 0.3f;

            if (size < _cfg.LiveMinSize) size = _cfg.LiveMinSize;
            if (size > _cfg.LiveMaxSize) size = _cfg.LiveMaxSize;

            _marks.Put(at, into, side, size,
                       Colour.R / 255f, Colour.G / 255f, Colour.B / 255f);
        }

        private readonly Random _rng = new Random();

        // ---- the plume ---------------------------------------------------------

        /// <summary>Which candidate took, so every press after the first costs one call.</summary>
        private int _jetPick = -1;
        private int _cloudPick = -1;

        private void StartPlume()
        {
            if (!_cfg.ColourTheSmoke) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var r = Colour.R / 255f;
                var g = Colour.G / 255f;
                var b = Colour.B / 255f;

                // ON THE WEAPON, NOT THE WRIST.
                //
                // The whole point of a visible jet is that the paint lands where you watched it
                // land, so the effect has to leave the nozzle and point where the nozzle points.
                // Hung off the hand bone it comes out of his forearm at whatever angle the
                // animation has, and then the plume and the paint disagree by a few degrees --
                // which at five metres is most of a wall.
                //
                // Falls back to the hand when the weapon object cannot be had, which is the
                // case for a frame or two around drawing it.
                // WHATEVER IS ACTUALLY VISIBLE.
                //
                // Particles attached to a hidden entity are hidden along with it, and the
                // spray can look hides the weapon on purpose. Hanging the plume off the
                // weapon there is asking for an invisible effect, and that is exactly what
                // it got: a whole session's log without a single Plume line, and nothing
                // coming out of the can.
                //
                // The can when there is a can, the weapon when the weapon is the thing you
                // can see. Light falls back to the hand bone when neither is available,
                // which is the case for a frame or two around drawing it -- and the hand is
                // visible in both looks, so that fallback is never the invisible one.
                var gun = _cfg.SprayCanLook
                    ? Nozzle
                    : Function.Call<int>(Hash.GET_CURRENT_PED_WEAPON_ENTITY_INDEX, me.Handle, 0);

                _fx = Light(Jets, ref _jetPick, gun, me.Handle, r, g, b, 0.90f);

                // The cloud comes off the extinguisher and nothing else, and it goes on
                // thinner than the jet on purpose -- you still have to be able to see what you
                // are painting through your own smoke.
                _cloud = _cfg.SprayCanLook
                    ? -1
                    : Light(Clouds, ref _cloudPick, gun, me.Handle, r, g, b, 0.45f);

                Moan();
            }
            catch (Exception ex)
            {
                _fx = -1;
                _cloud = -1;
                Log.Debug("No coloured plume: " + ex.Message);
            }
        }

        private int _dry;
        private bool _moaned;

        /// <summary>
        /// Says so, once, when the plume simply will not start.
        ///
        /// SILENCE WAS THE ACTUAL BUG HERE. The ladder failing looked identical to the
        /// feature being switched off, and a whole session went by with nothing coming out
        /// of the can and not one line in the log to say why. A ladder that can fail has to
        /// be able to report that it failed.
        /// </summary>
        private void Moan()
        {
            if (_fx != -1)
            {
                _dry = 0;
                _moaned = false;
                return;
            }

            // Several goes, not one. The first few legitimately fail while the asset streams
            // in, and complaining about those would be noise that trains you to ignore it.
            if (++_dry < 8 || _moaned) return;

            _moaned = true;

            var tried = new System.Text.StringBuilder();

            foreach (var p in Jets)
            {
                tried.Append(p.Name).Append(" out of ").Append(p.Asset ?? "core")
                     .Append(p.Select ? " (selected)" : " (unselected)").Append("; ");
            }

            Log.Warn("No plume will start after " + _dry + " goes. Tried: " + tried +
                     "Paint still lands -- this is the visible spray only. Nozzle entity was " +
                     Nozzle + ".");
        }

        /// <summary>
        /// Whether an effect's asset is in memory, asking for it if it is not.
        ///
        /// THE CORE ASSET TAKES NO NAME. REQUEST_PTFX_ASSET and HAS_PTFX_ASSET_LOADED are
        /// argument-less natives, and they are exactly what Rockstar's own graffiti scene
        /// calls before starting scr_lamgraff_paint_spray. Asking for core through the NAMED
        /// pair instead -- HAS_NAMED_PTFX_ASSET_LOADED("core") -- is the version of this that
        /// never reports ready, so the effect never starts and nothing anywhere says why.
        /// That is what was here before.
        /// </summary>
        private static bool Ready(Plume p)
        {
            if (p.Asset == null)
            {
                if (Function.Call<bool>(Hash.HAS_PTFX_ASSET_LOADED)) return true;

                Function.Call(Hash.REQUEST_PTFX_ASSET);
                return false;
            }

            if (Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, p.Asset)) return true;

            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, p.Asset);
            return false;
        }

        /// <summary>
        /// Starts the first effect in a ladder that will actually start, and remembers which.
        ///
        /// The remembered index is what keeps this cheap: once one has worked, every later
        /// press tries that one alone rather than walking the ladder to be refused again.
        /// </summary>
        private static int Light(Plume[] ladder, ref int remembered, int gun, int ped,
                                 float r, float g, float b, float alpha)
        {
            for (var i = 0; i < ladder.Length; i++)
            {
                if (remembered >= 0 && i != remembered) continue;

                var p = ladder[i];

                if (!Ready(p)) continue;   // next press, once it has streamed in

                // Named assets have to be selected, and core has to be selected BACK -- or
                // whichever named asset was used last is still current and a core effect gets
                // looked up in the wrong place.
                if (p.Select) Function.Call(Hash.USE_PARTICLE_FX_ASSET, p.Asset ?? "core");
                Function.Call(Hash.SET_PARTICLE_FX_NON_LOOPED_COLOUR, r, g, b);

                int fx;

                if (gun != 0)
                {
                    fx = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY,
                                            p.Name, gun,
                                            0f, 0.15f, 0f,
                                            0f, 0f, 0f,
                                            p.Size, false, false, false);
                }
                else
                {
                    fx = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY_BONE,
                                            p.Name, ped,
                                            0.3f, 0.1f, 0f,
                                            0f, 0f, 0f,
                                            28422, p.Size, false, false, false);
                }

                // A HANDLE IS NOT AN EFFECT. A ptfx name the build does not have returns a
                // handle for an effect that does not exist, so the handle alone proves nothing
                // and the ladder would stop at the first name that merely failed politely.
                if (fx == 0 || !Function.Call<bool>(Hash.DOES_PARTICLE_FX_LOOPED_EXIST, fx))
                {
                    continue;
                }

                Function.Call(Hash.SET_PARTICLE_FX_LOOPED_COLOUR, fx, r, g, b, false);
                Function.Call(Hash.SET_PARTICLE_FX_LOOPED_ALPHA, fx, alpha);

                if (remembered != i)
                {
                    remembered = i;
                    Log.Info("Plume: " + p.Name + " out of " + (p.Asset ?? "core") + ".");
                }

                return fx;
            }

            return -1;
        }

        private void StopPlume()
        {
            Douse(ref _fx);
            Douse(ref _cloud);
        }

        private static void Douse(ref int fx)
        {
            if (fx == -1) return;

            try { Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, fx, false); }
            catch { /* it stops when the asset unloads */ }

            fx = -1;
        }

        /// <summary>Everything off, for a reload.</summary>
        public void Stop()
        {
            StopPlume();
            Spraying = false;
            _wasSpraying = false;
        }
    }
}
