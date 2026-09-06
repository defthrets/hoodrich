using System;
using GTA;
using Color = System.Drawing.Color;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A car that belongs somewhere and stays there.
    ///
    /// The same idea as <see cref="Fixture"/>, which streams a prop at an exact spot, except a
    /// vehicle needs three things a prop does not: a paint job, something decided about the
    /// engine, and -- most importantly -- somebody to tell the traffic watchdog that it is
    /// parked on purpose.
    ///
    /// That last one is not optional. TrafficWatch removes empty cars standing in a lane, which
    /// is exactly what this is, and without the exemption the mod would spend its afternoon
    /// deleting its own scenery.
    /// </summary>
    internal sealed class ParkedCar
    {
        /// <summary>Close enough to be worth having it there.</summary>
        private const float StreamRange = 140f;

        private const int UpdateIntervalMs = 1800;

        private readonly Vector3 _where;
        private readonly float _heading;
        private readonly string[] _models;
        /// <summary>A game paint index, or below zero to leave it whatever it came in.</summary>
        private readonly int _paint;

        /// <summary>
        /// Puts back the three things the game turns off behind your back.
        ///
        /// Engine, radio and underglow are all electrics, and the game switches them off on an
        /// unoccupied vehicle -- on a timer, when the area streams out and back, and whenever
        /// anything else in the world decides a parked car should be quiet. Set once at spawn
        /// they were correct for about a minute, which is exactly as long as it takes to walk
        /// over and notice.
        ///
        /// Only while nobody is in it. The moment somebody gets in it stops being scenery and
        /// becomes their car -- fighting a player over his own radio every second and a half is
        /// worse than a van that goes quiet when it is driven away.
        /// </summary>
        /// <summary>Holds the station on for this frame, which is the only way a parked car
        /// keeps playing one.</summary>
        private void ForceRadio()
        {
            if (string.IsNullOrEmpty(Radio) || Quiet) return;
            if (_car == null || !_car.Exists()) return;

            try
            {
                var driver = _car.Driver;
                if (driver != null && driver.Exists()) return;

                Function.Call(Hash.SET_VEH_FORCED_RADIO_THIS_FRAME, _car.Handle);
            }
            catch
            {
                // Silent for a frame.
            }
        }

        private void Keep()
        {
            if (!Running && Neon == null && !Lights && string.IsNullOrEmpty(Radio)) return;

            try
            {
                if (_car == null || !_car.Exists()) return;

                var driver = _car.Driver;
                if (driver != null && driver.Exists()) return;

                // Off hours: the key comes out. All three go together because they are one
                // switch -- somebody turned it off and went inside.
                if (Quiet)
                {
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _car.Handle, false, true, false);
                    Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, _car.Handle, false);

                    // One is forced off. Nought would hand them back to the game, which would
                    // switch them on again the moment it decided it was dark.
                    if (Lights) Function.Call(Hash.SET_VEHICLE_LIGHTS, _car.Handle, 1);

                    if (Neon.HasValue)
                    {
                        for (var side = 0; side < 4; side++)
                        {
                            Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, _car.Handle, side, false);
                        }
                    }

                    return;
                }

                if (Running)
                {
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _car.Handle, true, true, false);
                }

                // Two is forced on. The engine above is what makes them light anything --
                // headlights on a car with the key out are a texture rather than a lamp.
                if (Lights) Function.Call(Hash.SET_VEHICLE_LIGHTS, _car.Handle, 2);

                if (!string.IsNullOrEmpty(Radio))
                {
                    Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, _car.Handle, true);
                    Function.Call(Hash.SET_VEH_RADIO_STATION, _car.Handle, Radio);
                    Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, _car.Handle, true);
                }

                if (Neon.HasValue) Light(Neon.Value);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not keep the parked car going: " + ex.Message);
            }
        }

        /// <summary>
        /// Every performance mod at its top index, the turbo, and the body kit.
        ///
        /// Off by default, because most of these are somebody's parked car and a stock car is
        /// what a parked car looks like. On for the one that is somebody's PROJECT.
        /// </summary>
        public bool Built;

        /// <summary>
        /// Nothing on it but the paint.
        ///
        /// Not the same as leaving Built off. Every car that comes through here gets Benny's
        /// rims, lowered springs and tinted glass whether it is a build or not, because that is
        /// what the cars in this lot are -- Built only adds the engine and the body kit on top
        /// of those. A car somebody drove to a meet and parked is stock: factory wheels,
        /// factory ride height, factory glass, and the only things on it are the paint and
        /// whatever is glowing underneath it.
        ///
        /// Wins over Built where both are set, because stock is a statement about the whole car.
        /// </summary>
        public bool Stock;

        /// <summary>
        /// Mods asked for by what they are called: slot to a word, and the slot gets the
        /// first option whose name has the word in it. The index of "Street Front Bumper"
        /// is different on every car; the word is not.
        /// </summary>
        public System.Collections.Generic.Dictionary<int, string> Wants;

        /// <summary>Mods asked for by index: slot to index, or -1 for the last one the car has.</summary>
        public System.Collections.Generic.Dictionary<int, int> Mods;

        /// <summary>The glass, if it is not to be whatever the build gives it: 1 black, 2 dark smoke, 3 light smoke.</summary>
        public int Tint = -1;

        /// <summary>
        /// Dropped on its springs, and nothing else.
        ///
        /// Separate from both of the others on purpose. Built is the whole shop and Stock is
        /// none of it, and the thing people actually do to a car on this block is neither: they
        /// drop it. It composes with Stock -- stock wheels, stock glass, on the floor.
        /// </summary>
        public bool Lowered;

        /// <summary>
        /// Whether the headlights are on.
        ///
        /// Electrics, like the radio and the underglow, so it needs the engine and it needs
        /// putting back every so often -- an unoccupied car has its lights taken off it along
        /// with the rest. Forced rather than left to the game, which decides by time of day and
        /// decides wrong for a car that has been sat there since before it got dark.
        /// </summary>
        public bool Lights;

        /// <summary>Underglow, if it has any. Null for a car nobody has lit.</summary>
        public Color? Neon;

        /// <summary>Whether the boot is standing open -- a van being unloaded, or worked on.</summary>
        public bool BootOpen;

        /// <summary>
        /// Other doors to stand open, by index, or null for none.
        ///
        /// 0 and 1 are the front, 2 and 3 the back, 4 the bonnet, 5 the boot. BootOpen is
        /// still its own flag because it is the one nearly every car that wants this wants;
        /// this is for the rest.
        /// </summary>
        public int[] Doors;

        /// <summary>What it says on the plate, or null to leave it whatever it came with.</summary>
        public string Plate;

        /// <summary>
        /// Interior and dashboard paint, or null for whatever it came with.
        ///
        /// The natives for this are in the enum as SET_VEHICLE_EXTRA_COLOUR_5 and _6, which is
        /// why they look missing when you go searching for INTERIOR and DASHBOARD. Five is the
        /// interior and six is the dash; checked against the assembly rather than called by a
        /// hash typed from memory, because a wrong native hash is a crash and not an error.
        /// </summary>
        public int? Interior;

        /// <summary>
        /// Whether it sits there with the engine running.
        ///
        /// Not a detail. An unoccupied car with the engine off has no radio you can hear and no
        /// underglow that stays lit -- the game switches both off with the electrics, which is
        /// why the neon came on at spawn and was dark by the time anybody walked over. A van
        /// somebody has parked up to play music out of is a van with the key still in it.
        /// </summary>
        public bool Running;

        /// <summary>The station, if it is playing anything.</summary>
        public string Radio;

        /// <summary>
        /// The hours it goes dark and quiet, or -1 for never.
        ///
        /// The engine, the radio and the underglow all go together, because they are the same
        /// switch: somebody came out, turned the key off and went in. A van still thumping at
        /// four in the morning in a yard everybody has left is the one thing that would give
        /// the whole trick away.
        /// </summary>
        public int QuietFrom = -1;
        public int QuietTo = -1;

        /// <summary>Set by the owner: whether this is off for the whole of tonight. See Core.Nights.</summary>
        public System.Func<bool> OffTonight;

        /// <summary>
        /// Whether the quiet hours take the car away entirely rather than just switching it off.
        ///
        /// Two different ideas sharing one window. The van in the lot is somebody's van and it
        /// is there at four in the morning with the key out -- quiet is a state it is in. A car
        /// that turned up for a meet is not there at four; quiet is the hours it does not exist.
        /// </summary>
        public bool GoneWhenQuiet;

        private bool Quiet
        {
            get
            {
                // Not tonight, whatever the hour. See Core.Nights.
                if (OffTonight != null && OffTonight()) return true;

                if (QuietFrom < 0 || QuietTo < 0 || QuietFrom == QuietTo) return false;

                try
                {
                    var hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);

                    return QuietTo > QuietFrom
                        ? hour >= QuietFrom && hour < QuietTo
                        : hour >= QuietFrom || hour < QuietTo;
                }
                catch
                {
                    return false;
                }
            }
        }

        private Vehicle _car;
        private int _lastUpdate;

        public ParkedCar(Vector3 where, float heading, int paint,
                         params string[] models)
        {
            _where = where;
            _heading = heading;
            _paint = paint;
            _models = models;
        }

        /// <summary>Whether this is our car, for the traffic watchdog.</summary>
        public bool Owns(Vehicle car)
        {
            return car != null && _car != null && _car.Exists() && car.Handle == _car.Handle;
        }

        public void Update()
        {
            // EVERY FRAME, before the throttle, and this is why the van was silent.
            //
            // The decks learnt this and wrote it down: an UNOCCUPIED vehicle does not keep its
            // radio on by being told to once. SET_VEH_FORCED_RADIO_THIS_FRAME is the native for
            // that case and its name is the contract -- one frame, then the game decides again,
            // and for a parked car with nobody in it the game decides off.
            //
            // Everything else in here is on a 1.8 second throttle, so the van had its station
            // set and its radio enabled and loud sixty times a minute and was forced to actually
            // play it never.
            ForceRadio();

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            if (_car != null && !_car.Exists()) _car = null;

            // Once it is out there it is left alone. Not put back on its mark, not re-parked --
            // if somebody has taken it for a drive then it is a car that got taken, which is a
            // better thing to happen on a block than a car that cannot be moved.
            // Off hours for a car that only turns up sometimes: it goes, and stays gone until
            // the hour comes round. Deleted rather than hidden -- an invisible car still has
            // collision, and a meet you can walk into but not see is worse than no meet.
            if (GoneWhenQuiet && Quiet)
            {
                if (_car != null)
                {
                    try { if (_car.Exists()) _car.Delete(); }
                    catch { /* it is already gone */ }

                    _car = null;
                }

                return;
            }

            if (_car != null)
            {
                Keep();
                return;
            }

            if (player.Position.DistanceTo(_where) > StreamRange) return;

            Make();
        }

        private void Make()
        {
            foreach (var name in _models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _car = World.CreateVehicle(model, _where, _heading);
                    model.MarkAsNoLongerNeeded();

                    if (_car == null || !_car.Exists()) continue;

                    _car.IsPersistent = true;
                    _car.Position = _where;
                    _car.Heading = _heading;

                    // A different car is a different question about neon mounts.
                    _neonAsked = false;

                    // Engine off and on the ground properly, so it reads as parked rather than
                    // as something that has just been put there.
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _car.Handle, Running, true, false);
                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _car.Handle);
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, _car.Handle, 1);

                    Paint();

                    Log.Info("Parked a " + name + " at " + _where + ".");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not park a " + name + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Paint and wheels.
        ///
        /// The colour is one of the game's own paint indices rather than an RGB triple. RGB
        /// gives a flat colour with no flake in it, which is how the first attempt came out as
        /// hard poster green -- the paint table has the finish baked in, and a lowrider is
        /// painted, not printed.
        ///
        /// The wheels are Benny's, which is a two-step thing: the wheel TYPE has to be set to
        /// the Benny's family before the rim index means anything, and the mod kit has to be
        /// open before either. Set them in the wrong order and you get stock wheels and no
        /// error to tell you why.
        /// </summary>
        private void Paint()
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, _car.Handle, 0);

                // Whatever pattern the game rolled for it, taken straight back off.
                //
                // This is why an FR36 painted metallic green came out navy with a red stripe
                // down it. Newer cars spawn with a random LIVERY, and a livery is a texture
                // over the panels -- the paint underneath was set correctly and could not be
                // seen. Both natives, because the old cars carry liveries in their own slot
                // and the DLC ones carry them as mod 48, and a car has one or the other.
                Function.Call(Hash.SET_VEHICLE_LIVERY, _car.Handle, -1);
                Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, 48, -1, false);

                // Below zero is not a colour, it is the absence of an instruction.
                //
                // Every car through here used to be painted whether it wanted to be or not,
                // and most of them do -- they belong to a set and the set has a colour. A
                // beat-up camper somebody has lived in does not: its whole look IS the look,
                // and metallic dark green on it would say "the mod painted this" out loud.
                if (_paint >= 0)
                {
                    Function.Call(Hash.SET_VEHICLE_COLOURS, _car.Handle, _paint, _paint);
                }

                // Everything below the paint is car-shaped: lowrider rims, lowered springs and
                // tinted glass mean nothing on two wheels or four small ones. Asked rather than
                // assumed, so a bike parked here later does not quietly get a suspension kit it
                // has no springs for.
                // Stock asks for none of it, and two wheels could not use it anyway.
                var onWheels = !Stock
                               && !Function.Call<bool>(Hash.IS_THIS_MODEL_A_BIKE, _car.Model.Hash)
                               && !Function.Call<bool>(Hash.IS_THIS_MODEL_A_QUADBIKE, _car.Model.Hash);

                if (onWheels)
                {
                    // Benny's Original, and a rim out of that set. Lowered on its springs,
                    // because a lowrider sitting at factory height is a saloon with nice wheels.
                    Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, _car.Handle, BennysWheels);
                    Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, 23, BennysRim, false);
                    Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, 15, 3, false);

                    Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, _car.Handle, 1);
                }

                // Lowered on its own, for a car that is otherwise stock. Somebody dropping
                // his car is not the same as somebody building one, and it is much the more
                // common of the two -- the ride height IS the statement, and the engine and
                // the body kit are a different and more expensive one.
                //
                // Asked for rather than assumed. The shop's suspension list runs stock, then
                // lowered, then lower again, so the last index is the lowest it goes -- and
                // hardcoding 3 fits some cars and quietly does nothing on the rest.
                if (Lowered)
                {
                    try
                    {
                        var drops = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, _car.Handle, 15);
                        if (drops > 0) Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, 15, drops - 1, false);
                    }
                    catch { /* it sits at factory height */ }
                }

                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, _car.Handle, Built || Stock ? 0.4f : 1.5f);

                if (Built && !Stock) BuildIt();

                Wanted();
                Asked();

                // Five is the interior, six is the dash. Both, because a green interior with a
                // black dashboard is half a job you can see from the door.
                if (Interior.HasValue)
                {
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOUR_5, _car.Handle, Interior.Value);
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOUR_6, _car.Handle, Interior.Value);
                }

                Keep();
                if (Neon.HasValue) Light(Neon.Value);

                // Door 5 is the boot. Instantly rather than swung, because the car is being
                // created in front of you and a boot easing itself open on spawn is a car
                // doing something rather than a car that was already like that.
                // Open on the frame it is created, so it is a van that was already like that
                // rather than one that opens its own boot while you watch. Swing holds it from
                // there.
                if (BootOpen) Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, _car.Handle, 5, false, true);

                if (Doors != null)
                {
                    foreach (var d in Doors)
                    {
                        Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, _car.Handle, d, false, true);
                    }
                }

                if (!string.IsNullOrEmpty(Plate))
                {
                    Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, _car.Handle, Plate);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not paint the parked car: " + ex.Message);
            }
        }

        /// <summary>
        /// Everything the shop sells, at the top of each list.
        ///
        /// Asked for rather than assumed: GET_NUM_VEHICLE_MODS says how many a given car
        /// actually has, and the top one is the last index. Hardcoding "4" fits some cars and
        /// silently does nothing on the rest, which is the sort of thing that looks like the
        /// mod not working.
        /// </summary>
        /// <summary>The mods asked for by name. See Wants.</summary>
        private void Wanted()
        {
            if (Wants == null) return;

            foreach (var pair in Wants)
            {
                try
                {
                    var many = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, _car.Handle, pair.Key);
                    if (many <= 0) continue;

                    var pick = many - 1;

                    for (var i = 0; i < many; i++)
                    {
                        if (ModName(pair.Key, i).IndexOf(pair.Value, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                        pick = i;
                        break;
                    }

                    Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, pair.Key, pick, false);
                }
                catch
                {
                    // A slot this car has not got.
                }
            }
        }

        /// <summary>The mods asked for by index, and the glass. See Mods and Tint.</summary>
        private void Asked()
        {
            if (Mods != null)
            {
                foreach (var pair in Mods)
                {
                    try
                    {
                        var many = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, _car.Handle, pair.Key);
                        if (many <= 0) continue;

                        var index = pair.Value < 0 || pair.Value >= many ? many - 1 : pair.Value;
                        Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, pair.Key, index, false);
                    }
                    catch
                    {
                        // A slot this car has not got.
                    }
                }
            }

            if (Tint >= 0)
            {
                try { Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, _car.Handle, Tint); }
                catch { /* the glass stays as it was */ }
            }
        }

        /// <summary>What a mod option is called, the way the shop reads it, or nothing.</summary>
        private string ModName(int slot, int index)
        {
            try
            {
                var label = Function.Call<string>(Hash.GET_MOD_TEXT_LABEL, _car.Handle, slot, index);

                if (!string.IsNullOrEmpty(label) && Function.Call<bool>(Hash.DOES_TEXT_LABEL_EXIST, label))
                {
                    var text = Function.Call<string>(Hash.GET_FILENAME_FOR_AUDIO_CONVERSATION, label);
                    if (!string.IsNullOrEmpty(text) && text != "NULL") return text;
                }
            }
            catch
            {
                // Nameless, then.
            }

            return "";
        }

        private void BuildIt()
        {
            // 11 engine, 12 brakes, 13 transmission, 15 suspension, 16 armour -- and then the
            // body: 0 spoiler through 10 roof, which is what makes it read as somebody's build
            // rather than a stock van with a fast engine nobody can see.
            foreach (var kind in new[] { 11, 12, 13, 15, 16,
                                         0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })
            {
                try
                {
                    var many = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, _car.Handle, kind);
                    if (many > 0) Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, kind, many - 1, false);
                }
                catch { /* a slot this car has not got */ }
            }

            try
            {
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, _car.Handle, 18, true);   // turbo
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, _car.Handle, 22, true);   // xenons
            }
            catch { /* neither is worth a log line */ }
        }

        /// <summary>
        /// <summary>
        /// Hold the doors open.
        ///
        /// SET_VEHICLE_DOOR_OPEN WAS THE WRONG NATIVE AND IT IS WHAT MADE THE BOOT FLAP. It
        /// does not mean "this door is open", it means "open this door" -- an ANIMATION. So the
        /// loop was: the game shuts the boot, we notice a second or two later and play the
        /// opening animation, it shuts it again, we play it again. A boot dropping and lifting
        /// on a timer, which is exactly what it looked like. Re-issuing it faster would only
        /// have made it flap faster.
        ///
        /// SET_VEHICLE_DOOR_CONTROL is the native that means what was wanted: hold this door at
        /// this angle. Called every frame it simply keeps it there -- there is no animation to
        /// restart, so there is nothing to see. Ratio 1 is as far as it goes.
        ///
        /// Unlatched on top of that, which is the other half of it. A latched door is one the
        /// game is entitled to close; unlatching stops it being shut in the first place rather
        /// than fighting it afterwards.
        /// </summary>
        private void Swing()
        {
            if (_car == null || !_car.Exists()) return;

            try
            {
                if (BootOpen) Hold(5);

                if (Doors == null) return;

                foreach (var d in Doors) Hold(d);
            }
            catch
            {
                // Next frame.
            }
        }

        /// <summary>One door, unlatched and held wide.</summary>
        private void Hold(int door)
        {
            Function.Call(Hash.SET_VEHICLE_DOOR_LATCHED, _car.Handle, door, false, false, true);
            Function.Call(Hash.SET_VEHICLE_DOOR_CONTROL, _car.Handle, door, 1f, 1f);
        }

        /// <summary>
        /// Underglow, all four sides, in one colour.
        ///
        /// The natives are SET_VEHICLE_NEON_ENABLED and SET_VEHICLE_NEON_COLOUR in this build
        /// -- not the _LIGHT_ spellings the docs use, which are simply not in the enum. Checked
        /// against the assembly rather than typed from memory.
        /// </summary>
        private void Light(Color c)
        {
            try
            {
                // Colour first, then the switch. Setting the colour of a tube that is not on
                // yet is fine; turning one on and colouring it a frame later is where a green
                // car gets one frame of factory blue every time the throttle comes round.
                Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, _car.Handle, (int)c.R, (int)c.G, (int)c.B);

                // Nothing is holding them off. There is a native whose whole job is to suppress
                // neons on a vehicle, and a parked car nobody is sitting in is exactly the sort
                // of thing something else in the game might have used it on.
                Function.Call(Hash.SUPPRESS_NEONS_ON_VEHICLE, _car.Handle, false);

                for (var side = 0; side < 4; side++)
                {
                    Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, _car.Handle, side, true);
                }

                // Asked back, once, and written down.
                //
                // Underglow is a Los Santos Customs slot and not every model has one. Told to
                // light up, a car without the mounts says nothing and simply does not glow,
                // which from the pavement is indistinguishable from a bug in this file. One
                // line in the log settles which of the two it is without another playthrough.
                if (!_neonAsked)
                {
                    _neonAsked = true;

                    var lit = Function.Call<bool>(Hash.GET_VEHICLE_NEON_ENABLED, _car.Handle, 0);

                    if (!lit)
                    {
                        Log.Info("No underglow on " + _car.Model.Hash.ToString("X") +
                                 " at " + _where + " -- the model has not got the mounts.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not light the parked car: " + ex.Message);
            }
        }

        /// <summary>Whether the neon has been checked once for this car.</summary>
        private bool _neonAsked;

        /// <summary>Wheel type 7 is the Benny's Original family.</summary>
        private const int BennysWheels = 7;

        /// <summary>
        /// Knock-Offs, out of that set.
        ///
        /// Fourth in the shop's list, which is index 2: the list opens with Stock -- index -1,
        /// the absence of a wheel choice -- so the numbered ones start one line below it.
        /// </summary>
        private const int BennysRim = 2;

        public void RestoreWorld()
        {
            try
            {
                if (_car != null && _car.Exists())
                {
                    // Handed back rather than deleted. If the player is sitting in it, deleting
                    // it on unload drops him through the world.
                    _car.IsPersistent = false;
                    _car.MarkAsNoLongerNeeded();
                }
            }
            catch { /* teardown */ }

            _car = null;
        }
    }
}
