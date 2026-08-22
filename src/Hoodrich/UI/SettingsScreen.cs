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

        public bool IsOpen { get; private set; }

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
            IsOpen = true;

            if (!_rows[_selected].Selectable) Move(1);

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            IsOpen = false;
            _listening = -1;
            _holdingSince = 0;
            _rows.Clear();
            _cfg = null;
        }

        // ---- the list ----------------------------------------------------------

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
                           string note = "")
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
            Tick("Hoodrich on", "General", "Enabled", () => c.Enabled, v => c.Enabled = v,
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

            Head("The wheel");
            Pick("Opens with", "Wheel", "Mode", new[] { "Replace", "Separate" },
                 () => (int)c.WheelMode, v => c.WheelMode = (WheelMode)v,
                 "Replace takes over the weapon wheel; Separate uses its own key");
            Bind("Key", "Wheel", "Key", () => c.WheelKey, v => c.WheelKey = v,
                 "Select, then press the key you want");
            Bind("Modifier", "Wheel", "Modifier", () => c.WheelModifier, v => c.WheelModifier = v,
                 "None for no modifier");
            Tick("Hold to open", "Wheel", "HoldToOpen", () => c.HoldToOpen, v => c.HoldToOpen = v,
                 "Hold for Hoodrich, tap to holster");
            Tick("Sounds", "Wheel", "PlaySounds", () => c.PlaySounds, v => c.PlaySounds = v,
                 "Clicks and confirmations");
            Tick("Blur behind it", "Wheel", "BlurBackground",
                 () => c.BlurBackground, v => c.BlurBackground = v);
            Tick("Blips in the bars", "Wheel", "BlipsInBars",
                 () => c.BlipsInBars, v => c.BlipsInBars = v,
                 "Map art at the ends of the reputation and heat bars");
            Pick("Drawn as", "Wheel", "RenderMode", new[] { "Wedge", "Node", "Auto" },
                 () => (int)c.RenderMode, v => c.RenderMode = (WheelRenderMode)v,
                 "Auto falls back to Node if the wedge texture will not stream");
            Slide("Time slows to", "Wheel", "TimeScale",
                  () => c.WheelTimeScale, v => c.WheelTimeScale = v, 0.05f, 1f, 0.05f, "0.00",
                  note: "1.00 turns the slowdown off");
            Slide("Inner radius", "Wheel", "InnerRadius",
                  () => c.InnerRadius, v => c.InnerRadius = v, 0.02f, 0.34f, 0.005f, "0.000");
            Slide("Outer radius", "Wheel", "OuterRadius",
                  () => c.OuterRadius, v => c.OuterRadius = v, 0.05f, 0.48f, 0.005f, "0.000");
            Slide("Dead zone", "Wheel", "DeadZone",
                  () => c.DeadZone, v => c.DeadZone = v, 0f, 0.9f, 0.05f, "0.00");
            Slide("Mouse sensitivity", "Wheel", "MouseSensitivity",
                  () => c.MouseSensitivity, v => c.MouseSensitivity = v, 0.1f, 5f, 0.1f, "0.0");
            Slide("Vanilla wheel hold", "Wheel", "VanillaWheelSeconds",
                  () => c.VanillaWheelSeconds, v => c.VanillaWheelSeconds = (int)v,
                  1f, 30f, 1f, "0", "s");
            Readout("Blur effect", () => string.IsNullOrEmpty(c.TimecycleModifier)
                        ? "none" : c.TimecycleModifier,
                    "A timecycle name. Ini only -- there is no list to pick from");

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

            Head("The law");
            Tick("Police patrol the blocks", "Police", "PatrolsEnabled",
                 () => c.PatrolsEnabled, v => c.PatrolsEnabled = v,
                 "Cars going round of their own accord -- nothing to do with heat");
            Slide("Patrol cars at once", "Police", "PatrolCars",
                  () => c.PatrolCars, v => c.PatrolCars = (int)v, 0f, 4f, 1f, "0",
                  note: "One is a neighbourhood; three is an occupation");

            Head("Socials");
            Tick("Feed on the right", "Socials", "TweetsOnTheRight",
                 () => c.TweetsOnTheRight, v => c.TweetsOnTheRight = v);

            Head("Posting up");
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

            Head("Money");
            Slide("Bulk discount", "Economy", "BulkPurchaseDiscountPercent",
                  () => c.BulkPurchaseDiscountPercent, v => c.BulkPurchaseDiscountPercent = v,
                  0f, 90f, 5f, "0", "%");
            Slide("Docks unlock at", "Economy", "DocksUnlockGrams",
                  () => c.DocksUnlockGrams, v => c.DocksUnlockGrams = v, 0f, 500f, 10f, "0", "g");
            Slide("Prices move every", "Economy", "MarketDriftIntervalMinutes",
                  () => c.MarketDriftIntervalMinutes, v => c.MarketDriftIntervalMinutes = v,
                  0f, 60f, 1f, "0", "m");
            Slide("Most a price can swing", "Economy", "MarketMaxSwingPercent",
                  () => c.MarketMaxSwingPercent, v => c.MarketMaxSwingPercent = v,
                  0f, 80f, 5f, "0", "%");
            Slide("Grams a leader fronts you", "Map", "LeaderFrontGrams",
                  () => c.LeaderFrontGrams, v => c.LeaderFrontGrams = v, 0f, 200f, 5f, "0", "g");

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

            Head("The bag on his back");
            Slide("Across", "Dealing", "BagX", () => c.BagX, v => c.BagX = v, -1f, 1f, 0.01f, "0.00");
            Slide("Front to back", "Dealing", "BagY", () => c.BagY, v => c.BagY = v,
                  -1f, 1f, 0.01f, "0.00");
            Slide("Up", "Dealing", "BagZ", () => c.BagZ, v => c.BagZ = v, -1f, 1f, 0.01f, "0.00");
            Slide("Pitch", "Dealing", "BagPitch", () => c.BagPitch, v => c.BagPitch = v,
                  -180f, 180f, 5f, "0");
            Slide("Roll", "Dealing", "BagRoll", () => c.BagRoll, v => c.BagRoll = v,
                  -180f, 180f, 5f, "0");
            Slide("Yaw", "Dealing", "BagYaw", () => c.BagYaw, v => c.BagYaw = v,
                  -180f, 180f, 5f, "0");
        }

        // ---- input -------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

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

                    if (Game.GameTime - _holdingSince >= HoldMs)
                    {
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
                    _holdingSince = 0;
                }

                return;
            }

            _holdingSince = 0;

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

            // Headings and readouts are stepped over rather than landed on, so holding down
            // never parks the cursor on something that does nothing.
            for (var i = 0; i < _rows.Count; i++)
            {
                _selected += step;

                if (_selected < 0) _selected = _rows.Count - 1;
                if (_selected >= _rows.Count) _selected = 0;

                if (_rows[_selected].Selectable) break;
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
                    if (now > row.Max) now = row.Max;

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
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.MeleeAttack1);
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

        public void Draw()
        {
            if (!IsOpen || _rows.Count == 0) return;

            var shown = Math.Min(Window, _rows.Count);
            var height = 0.088f + shown * RowHeight + 0.040f;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f;

            Hud.RectFrom(left, top, panelWidth, height, Color.FromArgb(238, 12, 13, 15));
            Hud.RectFrom(left, top, panelWidth, 0.0028f, Palette.Accent);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var y = top + 0.013f;

            Hud.Text("SETTINGS", x, y, 0.34f, Palette.Text, Hud.FontLabel, centre: false);

            Hud.TextRight(_rows.Count + " settings  ·  scripts\\Hoodrich.ini", right, y + 0.003f,
                          0.24f, Palette.TextDim, Hud.FontLabel);

            y += 0.034f;

            Hud.RectFrom(x, y, right - x, 0.0015f, Color.FromArgb(90, 255, 255, 255));
            y += 0.008f;

            var listTop = y;

            for (var i = _top; i < _top + shown && i < _rows.Count; i++)
            {
                DrawRow(_rows[i], i == _selected, x, right, y);
                y += RowHeight;
            }

            // A bar down the right edge saying where in the list you are. Fifty-one settings in
            // a fifteen-line window is four screenfuls, and without this there is nothing to say
            // whether there is more underneath.
            if (_rows.Count > shown)
            {
                var trackH = shown * RowHeight;
                var barH = Math.Max(0.012f, trackH * shown / (float)_rows.Count);
                var barY = listTop + (trackH - barH) * _top / (float)Math.Max(1, _rows.Count - shown);

                Hud.RectFrom(right + 0.004f, listTop, 0.0018f, trackH,
                             Color.FromArgb(50, 255, 255, 255));
                Hud.RectFrom(right + 0.004f, barY, 0.0018f, barH, Palette.Accent);
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
                     _listening >= 0 ? Palette.Accent : Palette.TextDim, Hud.FontLabel,
                     centre: false);
        }

        private void DrawRow(Opt row, bool picked, float x, float right, float y)
        {
            if (row.Kind == OptKind.Heading)
            {
                Hud.Text(row.Label.ToUpperInvariant(), x, y + 0.004f, 0.26f,
                         Palette.Alpha(Palette.Accent, 215), Hud.FontLabel, centre: false);

                Hud.RectFrom(x, y + RowHeight - 0.007f, right - x, 0.0015f,
                             Color.FromArgb(55, 255, 255, 255));
                return;
            }

            if (picked)
            {
                Hud.RectFrom(x - 0.006f, y - 0.003f, right - x + 0.012f, RowHeight,
                             Color.FromArgb(45, 255, 255, 255));
            }

            var live = row.Selectable;
            var tint = !live ? Palette.TextDisabled : picked ? Palette.Text : Palette.TextDim;

            Hud.Text((picked ? "> " : "  ") + row.Label, x, y, 0.28f, tint, Hud.FontBody,
                     centre: false);

            switch (row.Kind)
            {
                case OptKind.Tick:
                    DrawTick(right, y, row.GetBool != null && row.GetBool(), picked);
                    break;

                case OptKind.Slider:
                    DrawSlider(row, right, y, picked);
                    break;

                case OptKind.Choice:
                {
                    var at = row.GetChoice == null ? 0 : row.GetChoice();
                    var text = row.Choices != null && at >= 0 && at < row.Choices.Length
                        ? row.Choices[at]
                        : "?";

                    Hud.TextRight((picked ? "< " : "  ") + text.ToUpperInvariant() +
                                  (picked ? " >" : "  "),
                                  right, y, 0.28f, picked ? Palette.Accent : Palette.TextDim,
                                  Hud.FontBody);
                    break;
                }

                case OptKind.Binding:
                {
                    var listening = _listening >= 0 && _rows[_listening] == row;
                    var key = row.GetKey == null ? Keys.None : row.GetKey();

                    Hud.TextRight(listening ? "PRESS A KEY" : key.ToString().ToUpperInvariant(),
                                  right, y, 0.28f,
                                  listening ? Palette.Accent
                                            : picked ? Palette.Text : Palette.TextDim,
                                  Hud.FontBody);
                    break;
                }

                case OptKind.Readout:
                    Hud.TextRight(row.GetText == null ? "" : row.GetText(), right, y, 0.28f,
                                  Palette.TextDisabled, Hud.FontBody);
                    break;

                case OptKind.Danger:
                    DrawDanger(row, right, y, picked);
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
        private static void DrawTick(float right, float y, bool on, bool picked)
        {
            var size = 0.0155f;
            var w = Hud.ToX(size);
            var bx = right - w;
            var by = y + 0.0015f;

            var edge = on ? Palette.Cash : picked ? Palette.Text : Palette.TextDim;

            if (on)
            {
                Hud.RectFrom(bx, by, w, size, Palette.Alpha(Palette.Cash, 210));
                Hud.File("tick.png", bx + w * 0.5f, by + size * 0.5f, size * 0.78f, 0f,
                         Color.FromArgb(255, 12, 13, 15));
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
        private void DrawSlider(Opt row, float right, float y, bool picked)
        {
            var value = row.GetNum == null ? 0f : row.GetNum();
            var text = row.Prefix + value.ToString(row.Format, CultureInfo.InvariantCulture) +
                       row.Suffix;

            Hud.TextRight(text, right, y, 0.28f, picked ? Palette.Text : Palette.TextDim,
                          Hud.FontBody);

            var numberW = Hud.MeasureText(text, 0.28f, Hud.FontBody);
            var trackW = Hud.ToX(0.13f);
            var trackX = right - numberW - 0.008f - trackW;
            var trackY = y + 0.0105f;

            var span = row.Max - row.Min;
            var f = span <= 0f ? 0f : (value - row.Min) / span;

            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;

            Hud.RectFrom(trackX, trackY, trackW, 0.0030f, Color.FromArgb(60, 255, 255, 255));
            Hud.RectFrom(trackX, trackY, trackW * f, 0.0030f,
                         picked ? Palette.Accent : Palette.Alpha(Palette.Accent, 130));

            // The knob, kept fully on the track at both ends rather than hanging off it.
            var knobW = Hud.ToX(0.004f);
            var knobX = trackX + (trackW - knobW) * f;

            Hud.RectFrom(knobX, trackY - 0.0035f, knobW, 0.0100f,
                         picked ? Palette.Text : Palette.TextDim);
        }

        /// <summary>Something irreversible, and how far through holding it you are.</summary>
        private void DrawDanger(Opt row, float right, float y, bool picked)
        {
            var holding = picked && _holdingSince != 0;
            var f = holding ? Math.Min(1f, (Game.GameTime - _holdingSince) / (float)HoldMs) : 0f;

            var barW = Hud.ToX(0.13f);
            var barX = right - barW;

            Hud.RectFrom(barX, y + 0.0090f, barW, 0.0055f, Color.FromArgb(55, 255, 255, 255));

            if (f > 0f) Hud.RectFrom(barX, y + 0.0090f, barW * f, 0.0055f, Palette.Danger);

            Hud.TextRight(holding ? "HOLD..." : "HOLD ENTER", barX - 0.008f, y, 0.26f,
                          picked ? Palette.Danger : Palette.TextDim, Hud.FontBody);
        }
    }
}
