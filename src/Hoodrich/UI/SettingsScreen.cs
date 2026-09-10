using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>What a line on the settings screen is, which decides how it draws and what
    /// left and right do to it.</summary>
    internal enum OptKind
    {
        /// <summary>A section title. Not selectable, no control.</summary>
        Heading,

        /// <summary>On or off, as a box you tick.</summary>
        Tick,

        /// <summary>A number, as a track you slide.</summary>
        Slider,

        /// <summary>One of a short list, cycled.</summary>
        Choice,

        /// <summary>A keyboard binding, rebound by pressing the key you want.</summary>
        Binding,

        /// <summary>Something to read, not something to change. The ini owns it.</summary>
        Readout,

        /// <summary>
        /// Something that only looks: a tool that reads the world and writes a file or a log
        /// line, and changes nothing. Pressed, not held.
        ///
        /// THESE USED TO BE DANGER ROWS and it cost two goes at using one. The hold is there so
        /// that starting the mod over cannot be walked into, which is right -- but a row that
        /// writes a list of prop names to a text file has nothing to protect anybody from, and
        /// making it feel like it does means somebody presses it, sees nothing happen, and
        /// concludes the feature is broken. Which is what happened, twice.
        /// </summary>
        Action,

        /// <summary>Something that cannot be undone, so it is held rather than pressed.</summary>
        Danger
    }

    /// <summary>
    /// One line of the settings screen.
    ///
    /// Every value is reached through a pair of delegates rather than by reflecting over the
    /// settings object. Fifty-one lambdas is more typing than one loop over the fields, and it
    /// is worth it: this way a setting that is renamed stops COMPILING instead of silently
    /// disappearing off the screen, and each line can carry the range and the wording that only
    /// make sense for it.
    /// </summary>
    internal sealed class Opt
    {
        public OptKind Kind = OptKind.Tick;

        /// <summary>
        /// A ceiling that has to be asked for rather than remembered.
        ///
        /// ROWS ARE BUILT ON OPEN, WHICH IS FINE UNTIL ONE ROW'S RANGE DEPENDS ON ANOTHER ONE.
        /// The mask picker is exactly that: how many things there are to scroll through is a
        /// property of the SLOT, and the slot is the row above. Franklin's component 1 holds
        /// five beards and his prop slot 0 holds twenty-two hats, so switching between them
        /// with a remembered ceiling leaves you able to reach item four of twenty-two and no
        /// further until you close the screen and open it again.
        ///
        /// Null everywhere else, where a fixed Max is the honest answer and cheaper.
        /// </summary>
        public Func<float> MaxOf;

        /// <summary>How high this row actually goes, right now.</summary>
        public float Ceiling => MaxOf != null ? MaxOf() : Max;

        public string Label = "";
        public string Note = "";

        /// <summary>Where it lives in the ini, so a change can be written back.</summary>
        public string Section = "";
        public string Key = "";

        public Func<bool> GetBool;
        public Action<bool> SetBool;

        public Func<float> GetNum;
        public Action<float> SetNum;

        public float Min, Max, Step = 1f;
        public string Format = "0.##";
        public string Prefix = "";
        public string Suffix = "";

        public string[] Choices;
        public Func<int> GetChoice;
        public Action<int> SetChoice;

        public Func<Keys> GetKey;
        public Action<Keys> SetKey;

        public Func<string> GetText;

        public Action Do;
        public Func<bool> Enabled;

        public bool Selectable =>
            Kind != OptKind.Heading && Kind != OptKind.Readout &&
            (Enabled == null || Enabled());
    }

    /// <summary>
    /// Every setting the mod has, on one screen, changed where you are looking at it.
    ///
    /// This replaces a wheel page with five toggles on it. Five was never the number -- there
    /// are fifty-one settings in the ini and the other forty-six were reachable only by
    /// alt-tabbing out of the game, opening a text file and restarting. A wheel wedge is the
    /// wrong shape for that: it is a ring of eight things you pick between, and a settings list
    /// is a column of things you adjust.
    ///
    /// Everything writes through to BOTH the live settings object and the file. One without the
    /// other is a setting in name only -- change just the object and it lasts until you quit;
    /// change just the file and it does nothing until you do.
    ///
    /// No cursor anywhere. Up and down pick a line, left and right change it, and that is the
    /// whole input language -- the same one the stash screen uses, and the same one that works
    /// identically on a pad and on a keyboard.
    /// </summary>
    internal sealed class SettingsScreen
    {
        /// <summary>
        /// Sized in HEIGHT fractions and converted, so the card keeps its shape.
        ///
        /// Width in screen fractions makes a panel as wide as the monitor is, which on an
        /// ultrawide is a letterbox strip. The stash screen learnt this the hard way.
        /// </summary>
        private const float PanelWidthH = 0.78f;

        private const float RowHeight = 0.0265f;
        private const float PadH = 0.024f;

        /// <summary>How many lines are on screen at once. The rest scrolls.</summary>
        private const int Window = 15;

        /// <summary>Ignore input briefly, or the button that opened this acts on it.</summary>
        private const int OpenGraceMs = 220;

        /// <summary>Held left or right repeats at this rate, so a slider can be dragged.</summary>
        private const int RepeatMs = 90;

        /// <summary>How long a dangerous line has to be held before it happens.</summary>
        private const int HoldMs = 900;

        private readonly List<Opt> _rows = new List<Opt>();

        private Settings _cfg;
        private int _selected;
        private int _top;
        private int _openedAt;
        private int _nextRepeat;

        /// <summary>Which line is waiting for a key, or -1. Set by pressing select on a binding.</summary>
        private int _listening = -1;

        /// <summary>When the current dangerous line started being held down, or 0.</summary>
        private int _holdingSince;

        /// <summary>True once a held Danger row has fired, until the key comes back up.</summary>
        private bool _holdSpent;

        /// <summary>How this panel arrives and how it leaves. See UI.Curtain.</summary>
        private readonly Curtain _curtain = new Curtain();

        public bool IsOpen => _curtain.Showing;

        /// <summary>Called after anything changes, so the rest of the mod can react.</summary>
        public Action Changed;

        public void Open(Settings cfg, IEnumerable<Opt> extras = null)
        {
            if (cfg == null) return;

            _cfg = cfg;
            _rows.Clear();

            Build();

            if (extras != null) _rows.AddRange(extras);

            _selected = 0;
            _top = 0;
            _listening = -1;
            _holdingSince = 0;
            _openedAt = Game.GameTime;
            _curtain.Open();
            _shownAt = Game.GameTime;

            if (!_rows[_selected].Selectable) Move(1);

            // Landed, not arrived at: nothing fades out on open and the frame drops straight
            // onto the first row rather than gliding in from wherever it was last time.
            _lastSelected = -1;
            _pickedAt = Game.GameTime;
            _glide.Reset();

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            // The button that got you out of here does not also swing at somebody.
            if (IsOpen) Core.InputGuard.Swallow();
            _curtain.Close();
            Economy.Fit.Hide();
            _listening = -1;
            _holdingSince = 0;
            _holdSpent = false;

            // THE ROWS STAY UNTIL THE PANEL HAS FINISHED LEAVING.
            //
            // Clearing them here emptied the list while the curtain was still lifting out, and
            // Draw bails on an empty list -- so the exit animation was dead code and the panel
            // vanished on a single frame instead. Worse, Main keeps returning early for as long
            // as the screen reports itself open, so that tenth of a second had no panel AND
            // none of the mod's own HUD either, with the controls still held.
            //
            // They are dropped in Update once the curtain has actually gone. InfoPanel holds
            // its own rows through the exit for exactly this reason.
        }

        // ---- the list ----------------------------------------------------------



        /// <summary>
        /// Start a takeover now. Set by Main; returns the line to show the player.
        ///
        /// A hook rather than a reference to the takeover itself, for the same reason every
        /// other one here is: this screen draws rows and reads settings, and giving it the
        /// event runner would make it a thing that can also start events.
        /// </summary>
        public Func<int, string> StartTakeover;

        /// <summary>What the junctions are called, in the order StartTakeover takes them. Set by Main.</summary>
        public Func<string[]> TakeoverPlaces;

        /// <summary>Which group of places the door rows are pointed at, and which place in it.</summary>
        private int _group;
        private int _place;

        /// <summary>The place row itself, so its list can be rebuilt when the group changes.</summary>
        /// <summary>
        /// Set by Main. Starts a car meet and hands back why not, or null.
        ///
        /// A FUNC RATHER THAN THE OBJECT, so this screen knows nothing about how a meet is
        /// held -- the same way it knows nothing about how a door is written. A screen that
        /// reaches into the thing it is a screen for is a screen that has to change every
        /// time that thing does.
        /// </summary>
        public Func<string> Meet;

        private Opt _placeRow;

        /// <summary>The place the two door rows will write to.</summary>
        private Locations.Place Picked()
        {
            var list = Locations.Places.In(_group);
            if (list.Length == 0) return null;

            var at = _place < 0 ? 0 : _place >= list.Length ? list.Length - 1 : _place;
            return list[at];
        }

        /// <summary>The spooner scenes, so the rows can reload them. Set by Main.</summary>
        public static Locations.Scenery Scenes;

        /// <summary>
        /// The map prop the camera is pointed at, taken out of the world for good.
        ///
        /// WHAT IT WILL AND WILL NOT TAKE. Only an object -- never a ped, never a vehicle,
        /// never the player -- and never one of ours. A scene's own props are asked about
        /// through Scenery.Mine, because hiding the crate a scene just built is a scene that
        /// rebuilds it next session and hides it again, forever, and the file it wrote would
        /// have to be edited by hand to undo.
        ///
        /// A LONG RAY AND A NARROW ONE. Twenty metres from the camera along the way it looks,
        /// so a bin across the yard is as reachable as the one at your feet, and the flag is
        /// objects only so the ray goes through people rather than stopping at them.
        ///
        /// The position written down is the OBJECT'S, not the point the ray hit -- a hit is a
        /// spot on a surface and the sphere wants the middle of the thing.
        /// </summary>
        private static void Bury()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var eye = GameplayCamera.Position;
                var to = eye + GameplayCamera.Direction * BuryReach;

                // 16 is objects, and objects alone: the ray passes through anybody stood in
                // front of the thing you mean.
                var ray = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                                             eye.X, eye.Y, eye.Z, to.X, to.Y, to.Z,
                                             16, me.Handle, 7);

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

                if (Scenes != null && Scenes.Mine(handle))
                {
                    Notify.Important("That one is ours -- take it out of the scene file instead.");
                    return;
                }

                var hash = unchecked((uint)found.Model.Hash);

                var one = new Core.Spooner.Hidden
                {
                    ModelName = Core.Names.Say(found.Model.Hash),
                    ModelHash = hash,
                    At = found.Position,
                    Radius = Core.Spooner.DefaultRadius
                };

                var path = System.IO.Path.Combine(Core.Paths.Scenery, "hidden.xml");

                if (!Core.Spooner.Bury(path, one))
                {
                    Notify.Important("Could not write it down. See the log.");
                    return;
                }

                // GONE NOW AS WELL AS NEXT TIME. Writing it to a file that is read when a scene
                // comes into range would leave it standing there for the rest of the session,
                // which reads exactly like the button not working.
                Function.Call(Hash.CREATE_MODEL_HIDE, one.At.X, one.At.Y, one.At.Z,
                              one.Radius, unchecked((int)hash), false);

                Log.Info("Hidden for good: " + one.ModelName + " at " + one.At + ".");
                Notify.Important("~g~" + one.ModelName + "~s~ is gone, and stays gone.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not hide that: " + ex.Message);
                Notify.Important("Could not hide that one.");
            }
        }

        /// <summary>How far down the camera's line a prop can be and still be the one you mean.</summary>
        private const float BuryReach = 20f;

        private void Head(string title)
        {
            _rows.Add(new Opt { Kind = OptKind.Heading, Label = title });
        }

        private void Tick(string label, string section, string key, Func<bool> get,
                          Action<bool> set, string note = "")
        {
            _rows.Add(new Opt
            {
                Kind = OptKind.Tick,
                Label = label,
                Note = note,
                Section = section,
                Key = key,
                GetBool = get,
                SetBool = set
            });
        }

        private void Slide(string label, string section, string key, Func<float> get,
                           Action<float> set, float min, float max, float step,
                           string format = "0.##", string suffix = "", string prefix = "",
                           string note = "", Func<float> maxOf = null)
        {
            _rows.Add(new Opt
            {
                Kind = OptKind.Slider,
                Label = label,
                Note = note,
                Section = section,
                Key = key,
                GetNum = get,
                SetNum = set,
                Min = min,
                Max = max,
                MaxOf = maxOf,
                Step = step,
                Format = format,
                Suffix = suffix,
                Prefix = prefix
            });
        }

        private void Pick(string label, string section, string key, string[] choices,
                          Func<int> get, Action<int> set, string note = "")
        {
            _rows.Add(new Opt
            {
                Kind = OptKind.Choice,
                Label = label,
                Note = note,
                Section = section,
                Key = key,
                Choices = choices,
                GetChoice = get,
                SetChoice = set
            });
        }

        private void Bind(string label, string section, string key, Func<Keys> get,
                          Action<Keys> set, string note = "")
        {
            _rows.Add(new Opt
            {
                Kind = OptKind.Binding,
                Label = label,
                Note = note,
                Section = section,
                Key = key,
                GetKey = get,
                SetKey = set
            });
        }

        private void Readout(string label, Func<string> text, string note = "")
        {
            _rows.Add(new Opt
            {
                Kind = OptKind.Readout,
                Label = label,
                Note = note,
                GetText = text
            });
        }

        /// <summary>
        /// Every setting, in the order somebody would look for them.
        ///
        /// Grouped the way the ini is grouped rather than by type, because that is how they are
        /// talked about -- "the wheel settings" is a real category and "the float settings" is
        /// not.
        /// </summary>
        private void Build()
        {
            var c = _cfg;

            Head("The mod");
            Tick("Posted Up on", "General", "Enabled", () => c.Enabled, v => c.Enabled = v,
                 "Off leaves the game exactly as it was");
            Tick("Pause during story missions", "General", "PauseDuringMission",
                 () => c.PauseDuringMission, v => c.PauseDuringMission = v,
                 "Holds everything while a Rockstar mission is running");
            Slide("Save every", "General", "SaveIntervalSeconds",
                  () => c.SaveIntervalSeconds, v => c.SaveIntervalSeconds = (int)v,
                  15f, 600f, 15f, "0", "s");
            Pick("Log detail", "General", "LogLevel",
                 new[] { "Error", "Warn", "Info", "Debug" },
                 () => (int)c.LogLevel, v => c.LogLevel = (LogLevel)v,
                 "Debug writes a lot. Useful when something is wrong");

            Head("The phone");
            Readout("Opens with", () => "the phone button",
                    Core.Build.Name + " takes the phone. The weapon wheel is the game's own again");
            Bind("Extra key", "Phone", "Key", () => c.PhoneKey, v => c.PhoneKey = v,
                 "Optional. Select, then press the key you want");
            Bind("Modifier", "Phone", "Modifier", () => c.PhoneModifier, v => c.PhoneModifier = v,
                 "None for no modifier");
            Bind("Turn the mod back on", "General", "RescueKey",
                 () => c.RescueKey, v => c.RescueKey = v,
                 "The way back in after switching Posted Up off up there");
            Tick("Sounds", "Phone", "PlaySounds", () => c.PlaySounds, v => c.PlaySounds = v,
                 "Clicks and confirmations");
            Tick("Blur behind it", "Phone", "BlurBackground",
                 () => c.BlurBackground, v => c.BlurBackground = v);
            Tick("Blips in the bars", "PostUp", "BlipsInBars",
                 () => c.BlipsInBars, v => c.BlipsInBars = v,
                 "Map art at the ends of the reputation and heat bars");
            Slide("Real phone hold", "Phone", "VanillaPhoneSeconds",
                  () => c.VanillaPhoneSeconds, v => c.VanillaPhoneSeconds = (int)v,
                  1f, 30f, 1f, "0", "s",
                  note: "How long the button is yours after picking Phone");
            Slide("Time slows to", "Phone", "TimeScale",
                  () => c.WheelTimeScale, v => c.WheelTimeScale = v, 0.05f, 1f, 0.05f, "0.00",
                  note: "1.00 turns the slowdown off");
            Readout("Blur effect", () => string.IsNullOrEmpty(c.TimecycleModifier)
                        ? "none" : c.TimecycleModifier,
                    "A timecycle name. Ini only -- there is no list to pick from");

            Head("Voices");
            Tick("Recorded dialogue", "Voice", "VoiceEnabled",
                 () => c.VoiceEnabled, v => c.VoiceEnabled = v,
                 "Gerald, Lamar, Hao and Tao, in their own voices. Off leaves the text");
            Slide("How loud", "Voice", "VoiceVolume",
                  () => c.VoiceVolume, v => c.VoiceVolume = v, 0f, 1f, 0.05f, "0.00",
                  note: "Against the game's own dialogue, not against the music");

            Head("Lamar's list");
            Slide("He rests between jobs", "Jobs", "LamarRestMinutes",
                  () => c.LamarRestMinutes, v => c.LamarRestMinutes = v, 0f, 60f, 1f, "0", "m",
                  note: "0 hands you the next one the moment he pays for the last");

            Head("The block");
            Tick("The set rides the block", "Block", "RollersEnabled",
                 () => c.RollersEnabled, v => c.RollersEnabled = v,
                 "Ours out driving and riding while you are on our turf");
            Slide("Cars at once", "Block", "RollerCars",
                  () => c.RollerCars, v => c.RollerCars = (int)v, 0f, 6f, 1f, "0");
            Slide("Riders at once", "Block", "RollerBikes",
                  () => c.RollerBikes, v => c.RollerBikes = (int)v, 0f, 6f, 1f, "0");
            Tick("The lowriders come out", "Block", "CruiseEnabled",
                 () => c.CruiseEnabled, v => c.CruiseEnabled = v,
                 "Lowriders one night, donks the next: three in a line, slow, after ten");
            Tick("Riders pull wheelies", "Block", "RollerWheelies",
                 () => c.RollerWheelies, v => c.RollerWheelies = v,
                 "On the straights, when they have the room for it");
            Tick("Crews on foot", "Block", "WalkersEnabled",
                 () => c.WalkersEnabled, v => c.WalkersEnabled = v,
                 "Groups of ours walking the back streets, drinking and smoking");
            Slide("Crews at once", "Block", "WalkerCrews",
                  () => c.WalkerCrews, v => c.WalkerCrews = (int)v, 0f, 5f, 1f, "0");

            Head("The takeover");
            Tick("It happens", "Block", "TakeoverEnabled",
                 () => c.TakeoverEnabled, v => c.TakeoverEnabled = v,
                 "The junction on Carson, once a night between nine and four. Nothing is asked of you");
            Slide("The ring of people", "Block", "TakeoverRadius",
                  () => c.TakeoverRadius, v => c.TakeoverRadius = v, 8f, 40f, 1f, "0", "m",
                  note: "How far out the crowd stands from the middle");

            // HOLD TO START, HOLD AGAIN TO STOP. Held for the same reason as the row below
            // rather than because it is dangerous -- it is a thing you mean to do, and a
            // recording started by accident is one you find out about when you go looking for
            // the file you actually wanted.
            //
            // The label says which it will do, because a toggle whose text never changes is a
            // button you press to find out what state you were in.

            // HELD RATHER THAN PRESSED, and not because it is destructive. It is thirty-five
            // cars, thirty-five drivers and sixty-odd people arriving at once -- not something
            // to walk into while scrolling past it looking for something else.
            // ONE ROW PER JUNCTION, because there is more than one now and a single button
            // would have to guess which crossroads somebody meant. Built from the list rather
            // than typed out, so a third junction is a third button and nothing here changes.
            var places = TakeoverPlaces == null ? new string[0] : TakeoverPlaces();

            for (var i = 0; i < places.Length; i++)
            {
                // Copied out of the loop. A lambda closes over the VARIABLE, not its value, so
                // every button would otherwise start one at whichever junction the loop
                // finished on -- which is the last one, every time, silently.
                var which = i;
                var name = places[i];

                _rows.Add(new Opt
                {
                    Kind = OptKind.Danger,
                    Label = "Start one on " + name,
                    Note = "Hold to start a takeover on " + name + " without waiting for the " +
                           "clock. You have to be near that junction for it to have anywhere " +
                           "to happen",
                    Enabled = () => StartTakeover != null,
                    Do = () =>
                    {
                        var said = StartTakeover == null ? null : StartTakeover(which);

                        if (!string.IsNullOrEmpty(said)) Notify.Important(said);
                    }
                });
            }

            Head("Socials");
            Tick("Feed on screen", "Socials", "FeedOnScreen",
                 () => c.FeedOnScreen, v => c.FeedOnScreen = v,
                 "Off says nothing on screen and makes no sound. The timeline still fills");
            Tick("Feed on the right", "Socials", "TweetsOnTheRight",
                 () => c.TweetsOnTheRight, v => c.TweetsOnTheRight = v);
            Tick("The feed tips you off", "Socials", "BlockTipsEnabled",
                 () => c.BlockTipsEnabled, v => c.BlockTipsEnabled = v,
                 "Somebody posts that a block is busy, and it genuinely is");
            Slide("They notice every", "Socials", "BlockTipEveryMinutes",
                  () => c.BlockTipEveryMinutes, v => c.BlockTipEveryMinutes = v,
                  1f, 120f, 1f, "0", "m");
            Slide("Chance somebody posts", "Socials", "BlockTipChancePercent",
                  () => c.BlockTipChancePercent, v => c.BlockTipChancePercent = v,
                  0f, 100f, 5f, "0", "%");
            Slide("A tip is worth", "Socials", "BlockTipBoost",
                  () => c.BlockTipBoost, v => c.BlockTipBoost = v, 1f, 4f, 0.1f, "0.0", "x",
                  note: "How much more often somebody buys on a block that got named");
            Slide("A tip lasts", "Socials", "BlockTipMinutes",
                  () => c.BlockTipMinutes, v => c.BlockTipMinutes = v, 1f, 60f, 1f, "0", "m");

            // THE ONE SETTING IN THE MOD THAT CAN ONLY BE FOUND BY LOOKING. Component 1 on
            // Franklin is beards and masks in one unnamed list, so the slider re-applies as
            // it moves and you watch his head until the balaclava is on it. Only visible
            // work when the mask is on -- with it off the number changes and nothing shows,
            // which the note says.
            // THE ONLY SETTINGS IN THE MOD THAT CAN ONLY BE FOUND BY LOOKING, and there
            // are four of them because nothing in the game names a single one. Every row
            // re-applies as it moves, so you put the mask on from the phone and then scroll
            // here watching his head. The two that change WHERE it lives go through MoveTo,
            // which takes it off the old slot first -- otherwise the old slot keeps wearing
            // it for ever, since Off only knows about whatever is configured now.
            Head("Getting high");
            Tick("Cinematic camera", "Highs", "DrugCamera",
                 () => c.DrugCamera,
                 v => { c.DrugCamera = v; Economy.Ritual.Cinematic = v; },
                 "Swings round the front while you take something, then hands the camera back");
            Slide("How long it takes", "Highs", "AnimLength",
                  () => c.DrugAnimLength,
                  v => { c.DrugAnimLength = v; Economy.Ritual.Length = v; },
                  0.5f, 3f, 0.1f, "0.0", "x",
                  note: "Scales every drug animation. The camera follows it");

            // WHAT SOMEBODY BUILT IN THE SPOONER. Menyoo saves a placement file; this builds
            // it, from the mod's folder and from Menyoo's own, streamed in by distance.
            Head("Spooner scenes");
            Tick("Build saved scenes", "Scenery", "Enabled",
                 () => c.Scenery,
                 v => { c.Scenery = v; if (Scenes != null && !v) Scenes.Clear(); },
                 "Object Spooner files are stood up as you come near them");
            Tick("Read Menyoo's folder", "Scenery", "FromMenyoo",
                 () => c.SceneryFromMenyoo,
                 v => c.SceneryFromMenyoo = v,
                 "menyooStuff\\Spooner as well as the mod's own scenery folder. Save in Menyoo and it is in");
            Slide("How near to build", "Scenery", "Range",
                  () => c.SceneryRange,
                  v => c.SceneryRange = v,
                  40f, 600f, 10f, "0", " m",
                  note: "A scene is built once you are this close to it and taken out well past it");

            _rows.Add(new Opt
            {
                Kind = OptKind.Danger,
                Label = "Read the scene files again",
                Note = "Takes down what is standing and builds it from the files as they are now",
                Do = () =>
                {
                    if (Scenes == null) return;

                    var n = Scenes.Reload();
                    Notify.Important(n == 0
                        ? "No placements found. Save one in Menyoo's Object Spooner first."
                        : n + " placement(s) read. " + Scenes.Tally() + ".");
                }
            });

            // THE ONE THAT CATCHES UNSAVED WORK. An hour in the spooner is one crash from
            // never having happened; this writes what is stood round him to a scene file that
            // opens in Menyoo like anything else it saved.
            _rows.Add(new Opt
            {
                Kind = OptKind.Danger,
                Label = "Save everything around me",
                Note = "Writes the placed peds, cars and props within 80 m to a scene file, and lists them in the log",
                Do = () =>
                {
                    var me = Game.Player.Character;
                    if (me == null || !me.Exists()) return;

                    var found = Core.Spooner.Around(me.Position, 80f);

                    if (found.Count == 0)
                    {
                        Notify.Important("Nothing placed within 80 m.");
                        return;
                    }

                    foreach (var one in found)
                    {
                        Log.Info("Placed: " + (string.IsNullOrEmpty(one.ModelName)
                                     ? Core.Names.Say(one.ModelHash)
                                     : one.ModelName) +
                                 "  " + one.What +
                                 "  " + one.At.DistanceTo(me.Position).ToString("0.0") + " m  at " + one.At);
                    }

                    var name = "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml";
                    var path = System.IO.Path.Combine(Core.Paths.Scenery, name);

                    Notify.Important(Core.Spooner.Write(path, found, "Captured by Hoodrich")
                        ? found.Count + " placement(s) saved as " + name + "."
                        : "Could not write the scene file. See the log.");
                }
            });

            // THE OTHER HALF OF A SPOONER SCENE, and the spooner cannot do it.
            //
            // Menyoo records what you PUT somewhere. There is no tag anywhere in its format for
            // what you TOOK AWAY -- so a cupboard deleted to make room for a scale is back next
            // session, standing through the middle of everything you arranged around it. This
            // is that half: look at a map prop, press it, and it stays gone.
            _rows.Add(new Opt
            {
                Kind = OptKind.Danger,
                Label = "Hide the prop I am looking at",
                Note = "Takes a map object out for good and writes it into scenery\\hidden.xml",
                Do = () => Bury()
            });

            Head("LUber");
            Tick("Show the place", "LUber", "Preview",
                 () => c.RidePreview,
                 v => { c.RidePreview = v; RideScreen.Preview = v; },
                 "Looks at where you're going while you're choosing it, live. Off keeps the street");

            // WHERE A THING SITS IN HIS HAND, BY EYE. Every prop has its own origin and no
            // animation agrees with it; these put the thing in his hand with the pose it is
            // used in and let you move it while you look. Written to the ini as you go.
            Head("In his hand");
            Pick("Which one", "", "", Economy.Fit.Labels,
                 () => Economy.Fit.Picked,
                 i => Economy.Fit.Pick(i),
                 "Puts it in his hand with the pose it's used in, so you can see it while you move it");
            Slide("Forward", "", "", () => Economy.Fit.Value(0), v => Economy.Fit.Value(0, v),
                  -10f, 10f, 0.25f, "0.00", " cm", note: "Along his fingers");
            Slide("Right", "", "", () => Economy.Fit.Value(1), v => Economy.Fit.Value(1, v),
                  -10f, 10f, 0.25f, "0.00", " cm");
            Slide("Up", "", "", () => Economy.Fit.Value(2), v => Economy.Fit.Value(2, v),
                  -10f, 10f, 0.25f, "0.00", " cm");
            Slide("Pitch", "", "", () => Economy.Fit.Value(3), v => Economy.Fit.Value(3, v),
                  -180f, 180f, 5f, "0", " deg", note: "End over end. 180 points it the other way");
            Slide("Roll", "", "", () => Economy.Fit.Value(4), v => Economy.Fit.Value(4, v),
                  -180f, 180f, 5f, "0", " deg");
            Slide("Yaw", "", "", () => Economy.Fit.Value(5), v => Economy.Fit.Value(5, v),
                  -180f, 180f, 5f, "0", " deg",
                  note: "Saved to the ini as you go. Change which one to put it in his hand; leaving takes it out");

            // FOR BUILDING ROUND THINGS THE GAME ALREADY PUT THERE. A shrine on a pavement, a
            // memorial on a wall: the game does not say what it called them, and the object
            // list has twenty-one thousand names. Stand at the thing, press this, and the log
            // has the hash and the distance of everything within four metres.
            // A DOOR IS TWO COORDINATES AND A NAME, and the coordinates can only honestly
            // come from somebody standing on them. Four rounds of guessing where a room was
            // is what these two rows exist to stop happening again.
            Head("The block");

            // ONE PRESS, BECAUSE A MEET IS A THING THAT HAPPENS RATHER THAN A SETTING. There
            // is nothing to configure here -- where the cars park is twelve coordinates in
            // data\carmeet.json, which is the file to edit if a space is in the wrong place,
            // and what turns up is the set's own cars. All this row does is say "now".
            _rows.Add(new Opt
            {
                Kind = OptKind.Action,
                Label = "Call a car meet",
                Note = "The set brings their cars down to Carson Ave and parks up",
                Do = () =>
                {
                    if (Meet == null) return;

                    var no = Meet();

                    if (no != null) Notify.Problem(no);
                    else Notify.Ticker("~g~word's out.~s~  they're on their way.");
                }
            });

            Head("Doors");

            // THE LIST IS NAMES, THE PRESSES ARE NUMBERS. Everywhere in the game with an
            // inside is in Places.cs, every name of it read out of the interior loader's own
            // string table rather than typed. What is missing from each one is the two
            // coordinates, and those come from a player standing on them. Pick the place,
            // stand at its door, press; stand inside, press again.
            _rows.Add(new Opt
            {
                Kind = OptKind.Choice,
                Label = "Part of town",
                Note = "Which set of places the two rows below are pointed at",
                Choices = Locations.Places.Groups,
                GetChoice = () => _group,
                SetChoice = at =>
                {
                    _group = at;
                    _place = 0;

                    if (_placeRow != null) _placeRow.Choices = Locations.Places.Labels(_group);
                }
            });

            _placeRow = new Opt
            {
                Kind = OptKind.Choice,
                Label = "Place",
                Note = "The one you are standing at. Ones already written down say so",
                Choices = Locations.Places.Labels(0),
                GetChoice = () => _place,
                SetChoice = at => _place = at
            };

            _rows.Add(_placeRow);

            _rows.Add(new Opt
            {
                Kind = OptKind.Action,
                Label = "The door is here",
                Note = "Writes where you stand as the way in, with the name of what has to load",
                Do = () =>
                {
                    var me = Game.Player.Character;
                    if (me == null || !me.Exists()) return;

                    var place = Picked();
                    if (place == null) return;

                    var ok = Locations.DoorMaker.Outside(place, me.Position, me.Heading);

                    if (_placeRow != null) _placeRow.Choices = Locations.Places.Labels(_group);

                    Notify.Important(ok
                        ? place.Name + ": door marked. Now go inside and mark where you come out."
                        : "Could not write to Hoodrich.ini.");
                }
            });

            _rows.Add(new Opt
            {
                Kind = OptKind.Action,
                Label = "...and I come out here",
                Note = "Writes where you stand as the inside. Press Insert after and walk through it",
                Do = () =>
                {
                    var me = Game.Player.Character;
                    if (me == null || !me.Exists()) return;

                    var place = Picked();
                    if (place == null) return;

                    var started = Locations.DoorMaker.Started(place);
                    var ok = Locations.DoorMaker.Inside(place, me.Position, me.Heading);

                    if (_placeRow != null) _placeRow.Choices = Locations.Places.Labels(_group);

                    Notify.Important(!ok
                        ? "Could not write to Hoodrich.ini."
                        : started
                            ? place.Name + ": done. Press Insert and the door is there."
                            : place.Name + ": inside written. Mark its door too if you have not.");
                }
            });

            Readout("Places written down", () => Locations.Places.Tally(),
                    "Out of everywhere in the game that has an inside");

            Head("Finding things");

            _rows.Add(new Opt
            {
                Kind = OptKind.Action,
                Label = "Put him back in Franklin's body",
                Note = "For anybody left as somebody else. Does nothing when he is already himself",
                Do = () =>
                {
                    var me = Game.Player.Character;

                    if (me != null && me.Exists() && (uint)me.Model.Hash == (uint)PedHash.Franklin)
                    {
                        Notify.Important("He is already himself.");
                        return;
                    }

                    var model = new GTA.Model(PedHash.Franklin);
                    model.Request(3000);

                    if (!model.IsLoaded)
                    {
                        Notify.Problem("Franklin would not load. Try again in a moment.");
                        return;
                    }

                    Function.Call(Hash.SET_PLAYER_MODEL, Game.Player.Handle, model.Hash);
                    model.MarkAsNoLongerNeeded();

                    Log.Info("Put the player back in Franklin's body from the settings screen.");
                    Notify.Important("Back in his own body.");
                }
            });

            _rows.Add(new Opt
            {
                Kind = OptKind.Action,
                Label = "Find the rooms under the map",
                Note = "Sweeps the buried business shells and writes every interior it finds, and where, to rooms.txt",
                Do = () =>
                {
                    if (Core.Rooms.Running)
                    {
                        Notify.Important("Already sweeping.");
                        return;
                    }

                    // THE DOORS' OWN NAMES AND MARKS, so the sweep asks for exactly what
                    // this install is configured to open and reports on the coordinates it
                    // would actually put somebody at.
                    var ipls = new System.Collections.Generic.List<string>();
                    var marks = new System.Collections.Generic.List<
                        System.Collections.Generic.KeyValuePair<string, GTA.Math.Vector3>>();

                    if (c.Doors != null)
                    {
                        foreach (var door in c.Doors)
                        {
                            if (door == null) continue;

                            if (!string.IsNullOrEmpty(door.Ipl)) ipls.Add(door.Ipl);

                            // Extra is the second and later names, comma separated.
                            if (!string.IsNullOrEmpty(door.Extra))
                            {
                                foreach (var one in door.Extra.Split(','))
                                {
                                    var name = one.Trim();
                                    if (name.Length > 0) ipls.Add(name);
                                }
                            }

                            marks.Add(new System.Collections.Generic.KeyValuePair<string, GTA.Math.Vector3>(
                                door.Name + "  (ipl " + (string.IsNullOrEmpty(door.Ipl) ? "none" : door.Ipl) + ")",
                                new GTA.Math.Vector3(door.InsideX, door.InsideY, door.InsideZ)));
                        }
                    }

                    Core.Rooms.Start(ipls, marks);
                    Notify.Important("Sweeping. Back out and it writes rooms.txt in a moment.");
                }
            });

            _rows.Add(new Opt
            {
                Kind = OptKind.Action,
                Label = "Ask the art packs what they have",
                Note = "Asks every pack about every weapon name in the game and writes what exists to gunart-probe.txt",
                Do = () =>
                {
                    if (GunArtProbe.Running)
                    {
                        Notify.Important("Already asking.");
                        return;
                    }

                    if (!GunArt.Ready)
                    {
                        Notify.Important("Open Stretch's counter first so the packs are found.");
                        return;
                    }

                    Notify.Important(GunArtProbe.Start(GunArt.Packs)
                        ? "Asking. It writes gunart-probe.txt when it is done."
                        : "No art packs were found on this install.");
                }
            });

            _rows.Add(new Opt
            {
                Kind = OptKind.Action,
                Label = "Find the gun pictures again",
                Note = "Forgets where the game's gun photographs are and looks again. For after a game update",
                Do = () =>
                {
                    GunArt.Again();
                    Notify.Important("Open Stretch's counter and it will look again.");
                }
            });

            _rows.Add(new Opt
            {
                Kind = OptKind.Action,
                Label = "Name the props around me",
                Note = "Writes the model of every prop within four metres to Hoodrich.log, with how far off it is",
                Do = () =>
                {
                    var me = Game.Player.Character;
                    if (me == null || !me.Exists()) return;

                    var n = 0;
                    foreach (var prop in World.GetNearbyProps(me.Position, 4f))
                    {
                        if (prop == null || !prop.Exists()) continue;
                        n++;
                        Log.Info("Prop near him: 0x" + ((uint)prop.Model.Hash).ToString("X8") +
                                 "  " + prop.Position.DistanceTo(me.Position).ToString("0.0") + " m  at " + prop.Position);
                    }

                    Notify.Important(n + (n == 1 ? " prop" : " props") + " written to the log.");
                }
            });

            Head("Mask");
            Slide("Slot", "Mask", "Slot",
                  () => c.MaskSlot,
                  v => Core.Mask.MoveTo(c, () => c.MaskSlot = (int)Math.Round(v)),
                  0f, Core.Mask.Components - 1, 1f, "0", "",
                  note: "1 is beards on Franklin. His bandana is in another one");
            Tick("It's a prop, not clothing", "Mask", "AsProp",
                 () => c.MaskAsProp,
                 v => Core.Mask.MoveTo(c, () => c.MaskAsProp = v),
                 "Hats, glasses and the like are a separate set of eight slots. 0 is hats");
            Slide("Which one", "Mask", "Drawable",
                  () => c.MaskDrawable,
                  v => { c.MaskDrawable = (int)Math.Round(v); Core.Mask.Refresh(c); },
                  0f, Math.Max(1f, Core.Mask.Count(c) - 1), 1f, "0", "",
                  note: "Put it on from the phone first, then scroll and watch his head",
                  maxOf: () => Math.Max(1f, Core.Mask.Count(c) - 1));
            Slide("Colour", "Mask", "Texture",
                  () => c.MaskTexture,
                  v => { c.MaskTexture = (int)Math.Round(v); Core.Mask.Refresh(c); },
                  0f, Math.Max(1f, Core.Mask.Textures(c) - 1), 1f, "0", "",
                  note: "Some come in more than one",
                  maxOf: () => Math.Max(1f, Core.Mask.Textures(c) - 1));

            // AND THE WAY YOU ACTUALLY DO IT, which is not to touch any of the four above.
            //
            // The game knows where his mask is -- it is in his wardrobe, you can walk in and
            // put it on. So put it on, and let the mod look at him and write down what moved.
            // The first snapshot takes itself shortly after load, so most of the time only the
            // second button is needed.
            _rows.Add(new Opt
            {
                Kind = OptKind.Danger,
                Label = "Remember how he looks",
                Note = "Only needed if he was already wearing the mask when the mod loaded. " +
                       "Do it bare-faced, before you go to the wardrobe",
                Do = () =>
                {
                    var said = Core.Mask.Baseline();

                    Notify.Important(string.IsNullOrEmpty(said)
                        ? "Got it. Now put the mask on in his wardrobe."
                        : "~r~" + said);
                }
            });

            _rows.Add(new Opt
            {
                Kind = OptKind.Danger,
                Label = "Find the mask he's wearing",
                Note = "Put it on in his wardrobe first. This works out which slot it went " +
                       "into and fills in all four settings above",
                Do = () =>
                {
                    var said = Core.Mask.Learn(c);

                    if (!string.IsNullOrEmpty(said))
                    {
                        Notify.Important("~r~" + said);
                        return;
                    }

                    // WRITTEN TO THE INI HERE, because Learn changes the settings OBJECT and
                    // nothing else was going to. Every other row on this screen saves through
                    // Write when you nudge it; this one sets four values at once without any
                    // of them being touched, so without this the mask is found, works for the
                    // session, and is gone the next time the mod loads -- which is the most
                    // annoying way for a thing to be broken.
                    Settings.Put("Mask", "Slot", c.MaskSlot.ToString(CultureInfo.InvariantCulture));
                    Settings.Put("Mask", "AsProp", c.MaskAsProp ? "true" : "false");
                    Settings.Put("Mask", "Drawable", c.MaskDrawable.ToString(CultureInfo.InvariantCulture));
                    Settings.Put("Mask", "Texture", c.MaskTexture.ToString(CultureInfo.InvariantCulture));

                    Notify.Important("~g~Found it.~s~  " + Core.Mask.Where(c) +
                                     ", number " + c.MaskDrawable + ". Saved.");
                }
            });

            Head("Posting up");
            Tick("Show the corner readout", "PostUp", "ShowDealHud",
                 () => c.ShowDealHud, v => c.ShowDealHud = v,
                 "Off hides the wordmark and the bars. The corner still works");
            Slide("Chance each passer-by buys", "PostUp", "PostUpApproachChance",
                  () => c.PostUpApproachChance, v => c.PostUpApproachChance = v,
                  0f, 100f, 1f, "0", "%", note: "A busy pavement compounds this");
            Slide("Grams a street sale moves", "PostUp", "PostUpDealGrams",
                  () => c.PostUpDealGrams, v => c.PostUpDealGrams = v, 0.1f, 20f, 0.1f, "0.0", "g");
            Slide("Heat per witness", "PostUp", "PostUpHeatPerWitness",
                  () => c.PostUpHeatPerWitness, v => c.PostUpHeatPerWitness = v,
                  0f, 2f, 0.05f, "0.00");
            Slide("Heat before the law comes", "PostUp", "PostUpHeatBeforePolice",
                  () => c.PostUpHeatBeforePolice, v => c.PostUpHeatBeforePolice = v,
                  1f, 100f, 1f, "0");
            Slide("Seconds before a search", "PostUp", "PostUpSearchSeconds",
                  () => c.PostUpSearchSeconds, v => c.PostUpSearchSeconds = v, 1f, 60f, 1f, "0", "s",
                  note: "Your window to walk away");
            Slide("Fine when they find it", "PostUp", "PostUpFine",
                  () => c.PostUpFine, v => c.PostUpFine = (int)v, 0f, 50000f, 250f, "N0", "", "$");

            Tick("Blocks get worked out", "PostUp", "BlockSaturationEnabled",
                 () => c.BlockSaturationEnabled, v => c.BlockSaturationEnabled = v,
                 "Selling on one corner makes the next customer there slower to turn up");
            Slide("Grams to dry a block", "PostUp", "BlockSaturationGrams",
                  () => c.BlockSaturationGrams, v => c.BlockSaturationGrams = v,
                  10f, 2000f, 10f, "0", "g");
            Slide("It recovers over", "PostUp", "BlockRecoveryMinutes",
                  () => c.BlockRecoveryMinutes, v => c.BlockRecoveryMinutes = v,
                  1f, 240f, 1f, "0", "m", note: "While you are somewhere else");
            Slide("A dead block still does", "PostUp", "BlockDemandFloor",
                  () => c.BlockDemandFloor, v => c.BlockDemandFloor = v, 0f, 1f, 0.05f, "0.00", "x",
                  note: "It never goes to nothing. 0.25 is a quarter of normal");

            Tick("Buyers ring you", "PostUp", "ColdCallsEnabled",
                 () => c.ColdCallsEnabled, v => c.ColdCallsEnabled = v,
                 "Somebody calls wanting something, somewhere, for a while");
            Slide("They try every", "PostUp", "ColdCallEveryMinutes",
                  () => c.ColdCallEveryMinutes, v => c.ColdCallEveryMinutes = v,
                  1f, 120f, 1f, "0", "m");
            Slide("Chance they call", "PostUp", "ColdCallChancePercent",
                  () => c.ColdCallChancePercent, v => c.ColdCallChancePercent = v,
                  0f, 100f, 5f, "0", "%");
            Slide("You have", "PostUp", "ColdCallMinutes",
                  () => c.ColdCallMinutes, v => c.ColdCallMinutes = v, 1f, 60f, 1f, "0", "m",
                  note: "Before they go elsewhere");

            Head("Risk");
            Slide("Police bust chance", "Risk", "PoliceBustChancePercent",
                  () => c.PoliceBustChancePercent, v => c.PoliceBustChancePercent = v,
                  0f, 100f, 1f, "0", "%");
            Slide("Undercover call", "Risk", "UndercoverCallSeconds",
                  () => c.UndercoverCallSeconds, v => c.UndercoverCallSeconds = v,
                  1f, 60f, 1f, "0", "s");
            Slide("Escape distance", "Risk", "UndercoverEscapeDistance",
                  () => c.UndercoverEscapeDistance, v => c.UndercoverEscapeDistance = v,
                  5f, 300f, 5f, "0", "m");
            Slide("Stars on a bust", "Risk", "BustWantedStars",
                  () => c.BustWantedStars, v => c.BustWantedStars = (int)v, 1f, 5f, 1f, "0");
            Slide("Lost when you die", "Risk", "LoseOnDeathPercent",
                  () => c.LoseOnDeathPercent, v => c.LoseOnDeathPercent = v, 0f, 100f, 5f, "0", "%");
            Slide("Lost when you are nicked", "Risk", "LoseOnArrestPercent",
                  () => c.LoseOnArrestPercent, v => c.LoseOnArrestPercent = v, 0f, 100f, 5f, "0", "%");
            Slide("A dropped bag lasts", "Risk", "DeadDropDespawnMinutes",
                  () => c.DeadDropDespawnMinutes, v => c.DeadDropDespawnMinutes = v,
                  0f, 60f, 1f, "0", "m", note: "0 leaves it there forever");

            Tick("The house gets turned over", "Hideouts", "StashRaidsEnabled",
                 () => c.StashRaidsEnabled, v => c.StashRaidsEnabled = v,
                 "Hold enough, get loud enough, and somebody comes while you are out");
            Slide("Chance of a raid", "Hideouts", "StashRaidChancePercent",
                  () => c.StashRaidChancePercent, v => c.StashRaidChancePercent = v,
                  0f, 100f, 5f, "0", "%");
            Slide("They take", "Hideouts", "StashRaidTakePercent",
                  () => c.StashRaidTakePercent, v => c.StashRaidTakePercent = v,
                  0f, 100f, 5f, "0", "%");
            Slide("You are warned", "Hideouts", "StashRaidWarningMinutes",
                  () => c.StashRaidWarningMinutes, v => c.StashRaidWarningMinutes = v,
                  0f, 15f, 0.5f, "0.0", "m", note: "Be at the house when they come and it is off");

            Head("Money");
            Slide("Bulk discount", "Economy", "BulkPurchaseDiscountPercent",
                  () => c.BulkPurchaseDiscountPercent, v => c.BulkPurchaseDiscountPercent = v,
                  0f, 90f, 5f, "0", "%");
            Readout("Docks unlock", () => "after Gerald's two packages",
                    "Not a number any more -- take a package off him and clear it, twice");
            Slide("Prices move every", "Economy", "MarketDriftIntervalMinutes",
                  () => c.MarketDriftIntervalMinutes, v => c.MarketDriftIntervalMinutes = v,
                  0f, 60f, 1f, "0", "m");
            Slide("Most a price can swing", "Economy", "MarketMaxSwingPercent",
                  () => c.MarketMaxSwingPercent, v => c.MarketMaxSwingPercent = v,
                  0f, 80f, 5f, "0", "%");
            Slide("Grams a leader fronts you", "Map", "LeaderFrontGrams",
                  () => c.LeaderFrontGrams, v => c.LeaderFrontGrams = v, 0f, 200f, 5f, "0", "g");
            Slide("Tanya's tow", "Cars", "TowFee",
                  () => c.TowFee, v => c.TowFee = (int)v, 0f, 5000f, 50f, "N0", "", "$",
                  note: "What she charges to recover a wreck");
            Slide("The yard keeps it", "Cars", "TowYardHours",
                  () => c.TowYardHours, v => c.TowYardHours = v, 1f, 168f, 1f, "0", "h",
                  note: "In-game hours before it turns up back at Hao's");

            Head("Supply");
            Slide("A dealer holds", "Supply", "DealerMaxStockGrams",
                  () => c.DealerMaxStockGrams, v => c.DealerMaxStockGrams = v,
                  1f, 1000f, 10f, "0", "g");
            Slide("Restocks every", "Supply", "DealerRestockMinutes",
                  () => c.DealerRestockMinutes, v => c.DealerRestockMinutes = v,
                  0f, 120f, 1f, "0", "m");
            Slide("Chance he is dry", "Supply", "DealerDryChancePercent",
                  () => c.DealerDryChancePercent, v => c.DealerDryChancePercent = v,
                  0f, 100f, 5f, "0", "%");
            Slide("The house holds", "Hideouts", "HideoutStashCapacity",
                  () => c.HideoutStashCapacity, v => c.HideoutStashCapacity = v,
                  100f, 20000f, 100f, "N0", "g");

            // "The bag on his back" lived here: six sliders for the X, Y and Z offset and the
            // pitch, roll and yaw of the holdall prop on Franklin's shoulder.
            //
            // That is a calibration jig, not a setting. It exists so somebody can nudge a prop
            // until it sits right ONCE, and then never again -- and it was sat in the player's
            // options menu costing six rows of a list he has to scroll, next to genuine
            // questions like how much heat a witness adds.
            //
            // The ini keys stay exactly as they were, so anybody who did calibrate it keeps
            // their numbers and anybody porting the bag to a different prop can still get at
            // them. They are simply no longer a thing the game asks the player about.
        }

        // ---- input -------------------------------------------------------------

        public void Update()
        {
            Economy.Fit.Tick();

            if (!IsOpen)
            {
                // Gone for good now, so the list and the settings reference can go with it.
                // See Close: they are kept alive through the lift-out so there is something
                // to draw while it happens.
                if (_rows.Count > 0)
                {
                    _rows.Clear();
                    _cfg = null;
                }

                return;
            }

            LockControls();

            // On its way out it still draws and still holds the controls, but it has stopped
            // listening -- otherwise the panel you just closed spends its last tenth of a
            // second acting on whatever you press next.
            if (!_curtain.Taking) return;

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (_listening >= 0)
            {
                Listen();
                return;
            }

            if (Pressed(Control.PhoneCancel))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            if (_rows.Count == 0) return;

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);

            var row = Current;
            if (row == null) return;

            // Dangerous things are HELD, not pressed. A confirmation page for each of them was
            // the old answer and it is three screens deep for something you either meant or did
            // not; a second of holding says the same thing and cannot be walked into.
            if (row.Kind == OptKind.Danger)
            {
                if (Held(Control.PhoneSelect))
                {
                    if (_holdingSince == 0) _holdingSince = Game.GameTime;

                    if (!_holdSpent && Game.GameTime - _holdingSince >= HoldMs)
                    {
                        // SPENT UNTIL THE KEY COMES BACK UP.
                        //
                        // Zeroing the clock was not enough: the key is still down, so the next
                        // frame stamped it again and the row fired again a second later, and
                        // again, for as long as you held it. On rows like "Start the mod over"
                        // and "Unlock everything" that is a destructive action on a repeat
                        // timer. Hold Enter on the front-finisher for three seconds and it blew
                        // through both of Gerald's packages and opened the port in one press.
                        //
                        // SocialScreen has carried exactly this latch for the same reason.
                        _holdSpent = true;
                        _holdingSince = 0;

                        try { row.Do?.Invoke(); }
                        catch (Exception ex) { Log.Debug("Settings action failed: " + ex.Message); }

                        Hud.PlaySound("DELETE", "HUD_DEATHMATCH_SOUNDSET");
                        Notify.Ticker("~y~" + row.Label + " -- done.");
                        Changed?.Invoke();
                    }
                }
                else
                {
                    // Released: the next hold is a new one.
                    _holdingSince = 0;
                    _holdSpent = false;
                }

                return;
            }

            _holdingSince = 0;
            _holdSpent = false;

            if (row.Kind == OptKind.Action && Pressed(Control.PhoneSelect))
            {
                try { row.Do?.Invoke(); }
                catch (Exception ex) { Log.Debug("Settings action failed: " + ex.Message); }

                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Changed?.Invoke();
                return;
            }

            if (row.Kind == OptKind.Binding && Pressed(Control.PhoneSelect))
            {
                _listening = _selected;
                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (row.Kind == OptKind.Tick && Pressed(Control.PhoneSelect))
            {
                Change(row, 1);
                return;
            }

            if (Game.GameTime < _nextRepeat) return;

            // Held rather than pressed, so a slider can be dragged rather than tapped
            // fifty times. Sprint moves it ten steps at once.
            var fast = Held(Control.Sprint) || Held(Control.Jump);

            if (Held(Control.PhoneRight)) Change(row, fast ? 10 : 1);
            else if (Held(Control.PhoneLeft)) Change(row, fast ? -10 : -1);
        }

        /// <summary>
        /// Waiting for a key to bind.
        ///
        /// Every key is scanned rather than a handful, because the point of rebinding is that
        /// somebody wanted the one you did not think of. Escape cancels and Backspace clears it
        /// to None, so there is a way out and a way to have no modifier at all.
        /// </summary>
        private void Listen()
        {
            var row = _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;

            if (row == null || row.Kind != OptKind.Binding)
            {
                _listening = -1;
                return;
            }

            // A WAY OUT THAT IS NOT A KEYBOARD KEY.
            //
            // Listen runs above the cancel check in Update and only ever read keyboard input,
            // so "PRESS A KEY" on a controller was a room with no door: the screen holds every
            // control for as long as it is up, PhoneCancel was never reached, and nothing else
            // closes it. The only ways out were to reach for the keyboard or to die.
            //
            // Deliberately first, so it beats the catch-all below that takes any key as the
            // new binding.
            if (Pressed(Control.PhoneCancel))
            {
                _listening = -1;
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (Game.IsKeyPressed(Keys.Escape))
            {
                _listening = -1;
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (Game.IsKeyPressed(Keys.Back))
            {
                _listening = -1;
                Set(row, Keys.None);
                return;
            }

            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                if (key == Keys.None || key == Keys.Escape || key == Keys.Back) continue;
                if (!Game.IsKeyPressed(key)) continue;

                _listening = -1;
                Set(row, key);
                return;
            }
        }

        private void Set(Opt row, Keys key)
        {
            row.SetKey?.Invoke(key);
            Write(row, key.ToString());

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private Opt Current =>
            _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;

        private void Move(int step)
        {
            if (_rows.Count == 0) return;

            var before = _selected;

            // Headings and readouts are stepped over rather than landed on, so holding down
            // never parks the cursor on something that does nothing.
            for (var i = 0; i < _rows.Count; i++)
            {
                _selected += step;

                if (_selected < 0) _selected = _rows.Count - 1;
                if (_selected >= _rows.Count) _selected = 0;

                if (_rows[_selected].Selectable) break;
            }

            if (_selected != before)
            {
                _lastSelected = before;
                _pickedAt = Game.GameTime;
            }

            // Keep the picked line inside the window, and keep a heading visible above it where
            // there is one -- a row on its own tells you what it is, not what it belongs to.
            if (_selected < _top + 1) _top = Math.Max(0, _selected - 1);
            if (_selected > _top + Window - 2) _top = Math.Min(_rows.Count - Window, _selected - Window + 2);
            if (_top < 0) _top = 0;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>Moves a value by that many steps, and writes it wherever it lives.</summary>
        private void Change(Opt row, int steps)
        {
            _nextRepeat = Game.GameTime + RepeatMs;

            switch (row.Kind)
            {
                case OptKind.Tick:
                {
                    var now = !(row.GetBool != null && row.GetBool());

                    row.SetBool?.Invoke(now);
                    Write(row, now ? "true" : "false");
                    break;
                }

                case OptKind.Slider:
                {
                    var was = row.GetNum == null ? 0f : row.GetNum();
                    var now = was + row.Step * steps;

                    if (now < row.Min) now = row.Min;
                    if (now > row.Ceiling) now = row.Ceiling;

                    // Rounded onto the step, or a slider dragged left and right ends up on a
                    // number nobody chose -- 0.15000001 in a file people read.
                    now = (float)Math.Round(now / row.Step) * row.Step;

                    if (Math.Abs(now - was) < 0.0000001f)
                    {
                        _nextRepeat = Game.GameTime + RepeatMs * 3;
                        Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                        return;
                    }

                    row.SetNum?.Invoke(now);
                    Write(row, now.ToString("0.####", CultureInfo.InvariantCulture));
                    break;
                }

                case OptKind.Choice:
                {
                    if (row.Choices == null || row.Choices.Length == 0) return;

                    var was = row.GetChoice == null ? 0 : row.GetChoice();
                    var now = was + Math.Sign(steps);

                    if (now < 0) now = row.Choices.Length - 1;
                    if (now >= row.Choices.Length) now = 0;

                    row.SetChoice?.Invoke(now);
                    Write(row, row.Choices[now]);
                    break;
                }

                default:
                    return;
            }

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            Changed?.Invoke();
        }

        /// <summary>
        /// The file half of a change.
        ///
        /// The object has already been set by the time this runs. If the write fails the value
        /// still applies for this session and you are told once -- which is better than
        /// refusing the change, and much better than saying nothing and having it revert the
        /// next time the game starts.
        /// </summary>
        private void Write(Opt row, string value)
        {
            if (string.IsNullOrEmpty(row.Section) || string.IsNullOrEmpty(row.Key)) return;

            if (!Settings.Put(row.Section, row.Key, value))
            {
                Notify.Problem("could not write that to the ini.");
            }
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static bool Held(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Core.Fists.Off();
            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Sprint);
            Game.DisableControlThisFrame(Control.Enter);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.MoveLeftRight);
            Game.DisableControlThisFrame(Control.MoveUpDown);

            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneLeft);
            Game.DisableControlThisFrame(Control.PhoneRight);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);
        }

        // ---- drawing -----------------------------------------------------------

        /// <summary>The row the cursor was on before this one, so its plate can go down as the new one comes up.</summary>
        private int _lastSelected = -1;
        private int _pickedAt;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

        /// <summary>When the screen went up, for its entrance.</summary>
        private int _shownAt;

        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;

        /// <summary>
        /// A picture for each section, by the words in its heading.
        ///
        /// Looked up rather than stored on the row, because the headings are written where the
        /// settings are declared and threading an icon through every one of them would put art
        /// decisions in the middle of a list of ini keys. Anything not named here simply has no
        /// icon, which is the correct behaviour for a section added later by somebody who has
        /// not read this.
        /// </summary>
        private static string IconFor(string heading)
        {
            switch ((heading ?? "").ToLowerInvariant())
            {
                case "the mod": return "logo.png";
                case "the wheel": return "wheel_hub.png";
                case "lamar's list": return "phone.png";
                case "the block": return "pin.png";
                case "the law": return "police.png";
                case "socials": return "mobile.png";
                case "posting up": return "deal.png";
                case "risk": return "warning.png";
                case "money": return "cash.png";
                case "supply": return "crate.png";
                case "the bag on his back": return "box.png";
                default: return "";
            }
        }

        public void Draw()
        {
            if (!IsOpen || _rows.Count == 0) return;

            var shown = Math.Min(Window, _rows.Count);
            var height = 0.088f + shown * RowHeight + 0.040f;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            // Up and in, eased out so it slows as it lands.
            var age = Game.GameTime - _shownAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);

            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, panelWidth, height, arrive);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var y = top + 0.013f;

            Hud.Text("SETTINGS", x, y, 0.34f, Palette.Text, Hud.FontLabel, centre: false);

            // The file name is gone rather than renamed. It is the last place the old name
            // was showing, and what it was telling you -- that there is a file and where it
            // lives -- is worth less than not seeing that word on the settings screen.
            Hud.TextRight(_rows.Count + " settings  ·  saved as you change them",
                          right, y + 0.003f, 0.24f, Palette.TextDim, Hud.FontLabel);

            y += 0.034f;

            Theme.Rule(x, y, right - x, arrive);
            y += 0.008f;

            var listTop = y;

            // THE PLATE COMES UP UNDER THE ROW rather than sliding to it: the one under the
            // new row rises over a sixth of a second while the one under the old row sinks,
            // and the frame -- see Glide -- travels between them. Same as every other screen.
            var grown = Theme.Grown(_pickedAt);

            _glide.Begin();

            for (var i = _top; i < _top + shown && i < _rows.Count; i++)
            {
                var lit = Theme.Lit(i, _selected, _lastSelected, grown) * arrive;

                var plateY = y - 0.003f;

                Theme.Plate(x - 0.006f, plateY, right - x + 0.012f, RowHeight, lit);
                Theme.Sheen(x - 0.006f, plateY, right - x + 0.012f, RowHeight, lit);

                if (i == _selected) _glide.Target(x - 0.006f, plateY, right - x + 0.012f, RowHeight);

                DrawRow(_rows[i], i == _selected, lit, x, right, y);
                y += RowHeight;
            }

            // A bar down the right edge saying where in the list you are. Fifty-one settings in
            // a fifteen-line window is four screenfuls, and without this there is nothing to say
            // whether there is more underneath.
            if (_rows.Count > shown)
            {
                var trackH = shown * RowHeight;
                var barH = Math.Max(0.012f, trackH * shown / (float)_rows.Count);
                var thumbY = listTop + (trackH - barH) * _top / (float)Math.Max(1, _rows.Count - shown);

                Hud.RectFrom(right + 0.004f, listTop, 0.0018f, trackH,
                             Color.FromArgb(50, 255, 255, 255));
                Hud.RectFrom(right + 0.004f, thumbY, 0.0018f, barH, Palette.Brand);
            }

            var note = Current == null ? "" : Current.Note;

            if (!string.IsNullOrEmpty(note))
            {
                Hud.Text(Hud.Fit(note, right - x, 0.25f, Hud.FontBody),
                         x, top + height - 0.036f, 0.25f, Palette.TextDim, Hud.FontBody,
                         centre: false);
            }

            var hint = _listening >= 0
                ? "PRESS THE KEY YOU WANT     BACKSPACE  NONE     ESC  CANCEL"
                : "UP / DOWN  PICK     LEFT / RIGHT  CHANGE     ENTER  TOGGLE     SPRINT  x10     BACKSPACE  DONE";

            Hud.Text(hint, x, top + height - 0.019f, 0.24f,
                     _listening >= 0 ? Palette.Text : Palette.TextDim, Hud.FontLabel,
                     centre: false);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw(arrive);
        }

        /// <summary>The picture beside a section's name.</summary>
        private const float HeadIcon = 0.015f;

        /// <summary>
        /// One row. Lit is how far the plate under it has come up, nought to one, and every
        /// ink on the row follows it -- light on the dark, near-black on the plate, and the
        /// shades between while the plate is on its way.
        /// </summary>
        private void DrawRow(Opt row, bool picked, float lit, float x, float right, float y)
        {
            if (row.Kind == OptKind.Heading)
            {
                var tx = x;
                var art = IconFor(row.Label);

                if (!string.IsNullOrEmpty(art) &&
                    Hud.File(art, x + Hud.ToX(HeadIcon) * 0.5f, y + 0.011f, HeadIcon, 0f,
                             Palette.Alpha(Palette.Text, 230)))
                {
                    tx = x + Hud.ToX(HeadIcon) + 0.006f;
                }

                Hud.Text(row.Label.ToUpperInvariant(), tx, y + 0.004f, 0.26f,
                         Palette.Alpha(Palette.Text, 230), Hud.FontLabel, centre: false);

                Theme.Rule(x, y + RowHeight - 0.007f, right - x);
                return;
            }

            var live = row.Selectable;
            var tint = Theme.Ink(!live ? Palette.TextDisabled : picked ? Palette.Text : Palette.TextDim, lit);

            // No chevron. The plate and the frame say which row this is, and a mark that
            // appears in front of the words shifts them sideways every time the cursor moves.
            Hud.Text(row.Label, x, y, 0.28f, tint, Hud.FontBody, centre: false);

            switch (row.Kind)
            {
                case OptKind.Tick:
                    DrawTick(right, y, row.GetBool != null && row.GetBool(), picked, lit);
                    break;

                case OptKind.Slider:
                    DrawSlider(row, right, y, picked, lit);
                    break;

                case OptKind.Choice:
                {
                    var at = row.GetChoice == null ? 0 : row.GetChoice();
                    var text = row.Choices != null && at >= 0 && at < row.Choices.Length
                        ? row.Choices[at]
                        : "?";

                    Hud.TextRight((picked ? "< " : "  ") + text.ToUpperInvariant() +
                                  (picked ? " >" : "  "),
                                  right, y, 0.28f, Theme.Ink(picked ? Palette.Text : Palette.TextDim, lit),
                                  Hud.FontBody);
                    break;
                }

                case OptKind.Binding:
                {
                    var listening = _listening >= 0 && _rows[_listening] == row;
                    var key = row.GetKey == null ? Keys.None : row.GetKey();

                    Hud.TextRight(listening ? "PRESS A KEY" : key.ToString().ToUpperInvariant(),
                                  right, y, 0.28f,
                                  Theme.Ink(listening ? Palette.Text
                                                     : picked ? Palette.Text : Palette.TextDim, lit),
                                  Hud.FontBody);
                    break;
                }

                case OptKind.Readout:
                    Hud.TextRight(row.GetText == null ? "" : row.GetText(), right, y, 0.28f,
                                  Theme.Ink(Palette.TextDisabled, lit), Hud.FontBody);
                    break;

                case OptKind.Action:
                    Hud.TextRight(Hud.OnPad ? "A" : "ENTER", right, y, 0.26f,
                                  Theme.Ink(picked ? Palette.Text : Palette.TextDim, lit), Hud.FontBody);
                    break;

                case OptKind.Danger:
                    DrawDanger(row, right, y, picked, lit);
                    break;
            }
        }

        /// <summary>
        /// A box you tick.
        ///
        /// Drawn from rectangles rather than loaded as two PNGs, because it is four lines of a
        /// square and a tick already exists as art. An icon file per state would be two more
        /// things to keep in step with the palette for no gain.
        /// </summary>
        private static void DrawTick(float right, float y, bool on, bool picked, float lit)
        {
            var size = 0.0155f;
            var w = Hud.ToX(size);
            var bx = right - w;
            var by = y + 0.0015f;

            var edge = Theme.Ink(on ? Palette.Cash : picked ? Palette.Text : Palette.TextDim, lit);

            if (on)
            {
                // Green box on the dark; on the plate the box goes dark and the tick goes gold,
                // which is the only pairing that reads on both grounds.
                Hud.RectFrom(bx, by, w, size, Theme.Ink(Palette.Alpha(Palette.Cash, 210), lit));
                Hud.File("tick.png", bx + w * 0.5f, by + size * 0.5f, size * 0.78f, 0f,
                         Theme.Lerp(Color.FromArgb(255, 12, 13, 15), Palette.Text, lit));
            }
            else
            {
                var t = 0.0016f;

                Hud.RectFrom(bx, by, w, t, edge);
                Hud.RectFrom(bx, by + size - t, w, t, edge);
                Hud.RectFrom(bx, by, Hud.ToX(t), size, edge);
                Hud.RectFrom(bx + w - Hud.ToX(t), by, Hud.ToX(t), size, edge);
            }
        }

        /// <summary>A track, how far along it the value sits, and the number itself.</summary>
        private void DrawSlider(Opt row, float right, float y, bool picked, float lit)
        {
            var value = row.GetNum == null ? 0f : row.GetNum();
            var text = row.Prefix + value.ToString(row.Format, CultureInfo.InvariantCulture) +
                       row.Suffix;

            Hud.TextRight(text, right, y, 0.28f, Theme.Ink(picked ? Palette.Text : Palette.TextDim, lit),
                          Hud.FontBody);

            var numberW = Hud.MeasureText(text, 0.28f, Hud.FontBody);
            var trackW = Hud.ToX(0.13f);
            var trackX = right - numberW - 0.008f - trackW;
            var trackY = y + 0.0105f;

            var span = row.Ceiling - row.Min;
            var f = span <= 0f ? 0f : (value - row.Min) / span;

            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;

            Hud.RectFrom(trackX, trackY, trackW, 0.0030f,
                         Theme.Lerp(Color.FromArgb(60, 255, 255, 255), Color.FromArgb(70, 20, 18, 14), lit));
            Hud.RectFrom(trackX, trackY, trackW * f, 0.0030f,
                         Theme.Ink(picked ? Palette.Brand : Palette.Alpha(Palette.Brand, 150), lit));

            // The knob, kept fully on the track at both ends rather than hanging off it.
            var knobW = Hud.ToX(0.004f);
            var knobX = trackX + (trackW - knobW) * f;

            Hud.RectFrom(knobX, trackY - 0.0035f, knobW, 0.0100f,
                         Theme.Ink(picked ? Palette.Text : Palette.TextDim, lit));
        }

        /// <summary>Something irreversible, and how far through holding it you are.</summary>
        private void DrawDanger(Opt row, float right, float y, bool picked, float lit)
        {
            var holding = picked && _holdingSince != 0;
            var f = holding ? Math.Min(1f, (Game.GameTime - _holdingSince) / (float)HoldMs) : 0f;

            var barW = Hud.ToX(0.13f);
            var barX = right - barW;

            Hud.RectFrom(barX, y + 0.0090f, barW, 0.0055f,
                         Theme.Lerp(Color.FromArgb(55, 255, 255, 255), Color.FromArgb(70, 20, 18, 14), lit));

            if (f > 0f) Hud.RectFrom(barX, y + 0.0090f, barW * f, 0.0055f, Palette.Danger);

            Hud.TextRight(holding ? "HOLD..." : "HOLD ENTER", barX - 0.008f, y, 0.26f,
                          Theme.Ink(picked ? Palette.Danger : Palette.TextDim, lit), Hud.FontBody);
        }
    }
}
