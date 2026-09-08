using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Locations;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The muffler shop's menu: what it can do to the car in the bay, with prices.
    ///
    /// TWO LEVELS. The first is what kind of thing -- respray, wheels, engine, spoiler -- and
    /// the second is the choices for it, and moving the cursor over a choice puts it on the
    /// car, because the car is the preview. Enter pays for it; Backspace out of a list puts
    /// back whatever was on the car when you came in, so looking costs nothing.
    ///
    /// LOW END. The prices are a fraction of the shop over the river's, and every name is
    /// the game's own (GET_MOD_TEXT_LABEL) so a spoiler is called what it is called.
    /// </summary>
    internal sealed class ModShopScreen
    {
        /// <summary>Set by Main: take the money. False when he cannot cover it.</summary>
        public Func<int, bool> Pay;

        /// <summary>Something was paid for.</summary>
        public event Action Bought;

        private readonly Curtain _curtain = new Curtain();

        private Vehicle _car;
        private string _name = "";
        private readonly List<Cat> _cats = new List<Cat>();
        private int _cat;
        private int _lastCat = -1;
        private bool _inside;
        private int _pick;
        private int _lastPick = -1;
        private int _wasOn;
        private int _pickedAt;
        private int _openedAt;
        private int _top;

        private const float PanelWidthH = 0.62f;
        private const float RowHeight = 0.034f;
        private const float PadH = 0.024f;

        /// <summary>How far the panel sits off the left edge, and where its bottom rests.</summary>
        private const float LeftMarginH = 0.030f;

        /// <summary>
        /// The bottom of the panel, in screen height. Above the minimap rather than beside it:
        /// the minimap owns the bottom-left corner and a menu overlapping it is a menu you
        /// cannot read and a map you cannot use.
        /// </summary>
        private const float FootY = 0.74f;
        private const int Shown = 11;
        private const int OpenGraceMs = 220;

        /// <summary>The entrance: up and in over a sixth of a second, like every other panel.</summary>
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

        /// <summary>The 45 percent of the game's own prices that a shop with this many bricks on the floor charges.</summary>
        private const float LowEnd = 0.45f;

        public bool IsOpen => _curtain.Showing;

        // ---- what a category is -------------------------------------------------

        private sealed class Cat
        {
            public string Name;
            public Func<int> Count;
            public Func<int> Current;
            public Action<int> Put;
            public Func<int, string> Label;
            public Func<int, int> Price;
        }

        private static readonly string[] WheelTypes =
        {
            "Sport", "Muscle", "Lowrider", "SUV", "Offroad", "Tuner", "Bike", "High End",
            "Benny's Originals", "Benny's Bespoke", "Open Wheel", "Street", "Track"
        };

        private static readonly string[] Tints =
        {
            "None", "Pure Black", "Dark Smoke", "Light Smoke", "Stock", "Limo", "Green"
        };

        private static readonly string[] PlateStyles =
        {
            "Blue on white", "Yellow on black", "Yellow on blue", "Blue on white 2",
            "Blue on white 3", "North Yankton", "eCola", "Las Venturas", "Liberty City",
            "LS Car Meet", "LS Panic", "LS Pounders", "Sprunk"
        };

        /// <summary>The neon colours the shop over the river sells, by name.</summary>
        private static readonly object[,] Neons =
        {
            { "Off", 0, 0, 0 }, { "White", 255, 255, 255 }, { "Blue", 0, 0, 255 },
            { "Electric Blue", 0, 150, 255 }, { "Mint Green", 50, 255, 155 }, { "Lime Green", 0, 255, 0 },
            { "Yellow", 255, 255, 0 }, { "Golden Shower", 255, 150, 0 }, { "Orange", 255, 62, 0 },
            { "Red", 255, 0, 0 }, { "Pony Pink", 255, 50, 100 }, { "Hot Pink", 255, 0, 255 },
            { "Purple", 153, 0, 255 }, { "Blacklight", 15, 3, 255 }
        };

        private static readonly int[] Performance = { 11, 12, 13, 15, 16 };

        // ---- open and close -----------------------------------------------------

        public void Open(Vehicle car, string name)
        {
            if (car == null || !car.Exists()) return;

            _car = car;
            _name = string.IsNullOrEmpty(name) ? car.LocalizedName : name;
            _cats.Clear();
            _cat = 0;
            _lastCat = -1;
            _inside = false;
            _top = 0;
            _pickedAt = _openedAt = Game.GameTime;
            _glide.Reset();

            try { Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0); }
            catch { }

            Build();

            _curtain.Open();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (!IsOpen) return;

            if (_inside) Back(false);

            InputGuard.Swallow();
            _curtain.Close();
            Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- the categories, for this car ----------------------------------------

        private void Build()
        {
            var h = _car.Handle;

            Colour("Respray", () => Colours(true), v => SetColours(v, Colours(false)));
            Colour("Respray, the second colour", () => Colours(false), v => SetColours(Colours(true), v));
            Colour("Pearlescent", () => Extras(true), v => SetExtras(v, Extras(false)));

            _cats.Add(new Cat
            {
                Name = "Wheel type",
                Count = () => WheelTypes.Length,
                Current = () => Function.Call<int>(Hash.GET_VEHICLE_WHEEL_TYPE, h),
                Put = v =>
                {
                    Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, h, v);
                    Function.Call(Hash.SET_VEHICLE_MOD, h, 23, 0, false);
                },
                Label = v => WheelTypes[v],
                Price = v => 0
            });

            Slot("Wheels", 23);
            Colour("Wheel colour", () => Extras(false), v => SetExtras(Extras(true), v));

            Slot("Engine", 11);
            Slot("Brakes", 12);
            Slot("Transmission", 13);
            Slot("Suspension", 15);
            Slot("Armour", 16);
            Toggle("Turbo", Kit.TurboSlot, 2500);

            Slot("Spoiler", 0);
            Slot("Front bumper", 1);
            Slot("Rear bumper", 2);
            Slot("Side skirts", 3);
            Slot("Exhaust", 4);
            Slot("Roll cage", 5);
            Slot("Grille", 6);
            Slot("Bonnet", 7);
            Slot("Left wing", 8);
            Slot("Right wing", 9);
            Slot("Roof", 10);
            Slot("Horn", 14);
            Slot("Plate holder", 25);
            Slot("Trim", 27);
            Slot("Ornaments", 28);
            Slot("Dials", 30);
            Slot("Steering wheel", 33);
            Slot("Shifter", 34);
            Slot("Plaques", 35);
            Slot("Hydraulics", 38);

            _cats.Add(new Cat
            {
                Name = "Window tint",
                Count = () => Tints.Length,
                Current = () => Function.Call<int>(Hash.GET_VEHICLE_WINDOW_TINT, h),
                Put = v => Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, h, v),
                Label = v => Tints[v],
                Price = v => v == 0 ? 0 : Low(500)
            });

            Toggle("Xenon lights", Kit.XenonSlot, 1500);
            Toggle("Tyre smoke", Kit.SmokeSlot, 900);

            _cats.Add(new Cat
            {
                Name = "Neon",
                Count = () => Neons.GetLength(0),
                Current = NeonNow,
                Put = v =>
                {
                    var on = v > 0;
                    for (var i = 0; i < 4; i++) Kit.NeonOn(_car, i, on);
                    if (on) Kit.NeonColour(_car, (int)Neons[v, 1], (int)Neons[v, 2], (int)Neons[v, 3]);
                },
                Label = v => (string)Neons[v, 0],
                Price = v => v == 0 ? 0 : Low(3000)
            });

            var liveries = 0;
            try { liveries = Function.Call<int>(Hash.GET_VEHICLE_LIVERY_COUNT, h); }
            catch { }

            if (liveries > 0)
            {
                _cats.Add(new Cat
                {
                    Name = "Livery",
                    Count = () => liveries + 1,
                    Current = () => Function.Call<int>(Hash.GET_VEHICLE_LIVERY, h) + 1,
                    Put = v => Function.Call(Hash.SET_VEHICLE_LIVERY, h, v - 1),
                    Label = v => v == 0 ? "None" : "Livery " + v,
                    Price = v => v == 0 ? 0 : Low(1200)
                });
            }

            _cats.Add(new Cat
            {
                Name = "Plate style",
                Count = () =>
                {
                    var n = 0;
                    try { n = Function.Call<int>(Hash.GET_NUMBER_OF_VEHICLE_NUMBER_PLATES); }
                    catch { }
                    return n >= 6 ? n : PlateStyles.Length;
                },
                Current = () => Function.Call<int>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, h),
                Put = v => Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, h, v),
                Label = v => v < PlateStyles.Length ? PlateStyles[v] : "Style " + (v + 1),
                Price = v => Low(200)
            });

            // Only what this car has. A category with one choice is a category with nothing in it.
            _cats.RemoveAll(c => c.Count() <= 1);
        }

        /// <summary>A mod slot: "Stock" first, then everything the game lists for it, by its own name.</summary>
        private void Slot(string name, int slot)
        {
            var h = _car.Handle;
            var n = 0;

            try { n = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, slot); }
            catch { }

            if (n <= 0) return;

            var performance = Array.IndexOf(Performance, slot) >= 0;

            _cats.Add(new Cat
            {
                Name = name,
                Count = () => n + 1,
                Current = () => Function.Call<int>(Hash.GET_VEHICLE_MOD, h, slot) + 1,
                Put = v =>
                {
                    if (v == 0) Function.Call(Hash.REMOVE_VEHICLE_MOD, h, slot);
                    else Function.Call(Hash.SET_VEHICLE_MOD, h, slot, v - 1, false);
                },
                Label = v => v == 0 ? "Stock" : ModName(slot, v - 1),
                Price = v => v == 0 ? 0 : performance ? Low(1500 * v * v) : Low(700 + 400 * v)
            });
        }

        private void Toggle(string name, int slot, int price)
        {
            var h = _car.Handle;

            _cats.Add(new Cat
            {
                Name = name,
                Count = () => 2,
                Current = () => Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, h, slot) ? 1 : 0,
                Put = v => Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, slot, v == 1),
                Label = v => v == 0 ? "Off" : "On",
                Price = v => v == 0 ? 0 : Low(price)
            });
        }

        private void Colour(string name, Func<int> now, Action<int> put)
        {
            _cats.Add(new Cat
            {
                Name = name,
                Count = () => Paints.Names.Length,
                Current = now,
                Put = put,
                Label = Paints.Name,
                Price = v => Low(900)
            });
        }

        private static int Low(int full)
        {
            return Math.Max(50, (int)(full * LowEnd / 10) * 10);
        }

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
            }

            return "Type " + (index + 1);
        }

        private int Colours(bool primary)
        {
            var a = new OutputArgument();
            var b = new OutputArgument();
            Function.Call(Hash.GET_VEHICLE_COLOURS, _car.Handle, a, b);
            return primary ? a.GetResult<int>() : b.GetResult<int>();
        }

        private void SetColours(int a, int b)
        {
            Function.Call(Hash.SET_VEHICLE_COLOURS, _car.Handle, a, b);
        }

        private int Extras(bool pearl)
        {
            var p = new OutputArgument();
            var w = new OutputArgument();
            Function.Call(Hash.GET_VEHICLE_EXTRA_COLOURS, _car.Handle, p, w);
            return pearl ? p.GetResult<int>() : w.GetResult<int>();
        }

        private void SetExtras(int pearl, int wheel)
        {
            Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, _car.Handle, pearl, wheel);
        }

        private int NeonNow()
        {
            if (!Kit.NeonIsOn(_car)) return 0;

            int r, g, b;
            Kit.NeonRead(_car, out r, out g, out b);

            for (var i = 1; i < Neons.GetLength(0); i++)
            {
                if ((int)Neons[i, 1] == r && (int)Neons[i, 2] == g && (int)Neons[i, 3] == b) return i;
            }

            return 1;
        }

        // ---- input --------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;
            if (_car == null || !_car.Exists()) { Close(); return; }

            if (Pressed(Control.PhoneCancel))
            {
                if (_inside) Back(false);
                else Close();
                return;
            }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (_inside && Pressed(Control.PhoneLeft)) Move(-1);
            else if (_inside && Pressed(Control.PhoneRight)) Move(1);
            else if (Pressed(Control.PhoneSelect) || Pressed(Control.Context)) Choose();
        }

        private void Move(int step)
        {
            if (!_inside)
            {
                if (_cats.Count == 0) return;
                _lastCat = _cat;
                _cat = (_cat + step + _cats.Count) % _cats.Count;
                Scroll(_cat, _cats.Count);
                _pickedAt = Game.GameTime;
                Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            var cat = _cats[_cat];
            var n = cat.Count();
            if (n <= 0) return;

            _lastPick = _pick;
            _pick = (_pick + step + n) % n;
            Scroll(_pick, n);
            _pickedAt = Game.GameTime;

            // On the car as the cursor lands on it.
            try { cat.Put(_pick); }
            catch (Exception ex) { Log.Debug("Could not try a mod on: " + ex.Message); }

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Scroll(int at, int n)
        {
            if (at < _top) _top = at;
            if (at >= _top + Shown) _top = at - Shown + 1;
            if (_top > Math.Max(0, n - Shown)) _top = Math.Max(0, n - Shown);
            if (_top < 0) _top = 0;
        }

        private void Choose()
        {
            if (!_inside)
            {
                if (_cats.Count == 0) return;

                var cat = _cats[_cat];
                _wasOn = Math.Max(0, Math.Min(cat.Count() - 1, cat.Current()));
                _pick = _wasOn;
                _lastPick = -1;
                _top = 0;
                Scroll(_pick, cat.Count());
                _inside = true;
                _pickedAt = Game.GameTime;
                _glide.Reset();
                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            var chosen = _cats[_cat];
            var price = chosen.Price(_pick);

            if (_pick == _wasOn)
            {
                Notify.Problem("that's already on it.");
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (price > 0 && (Pay == null || !Pay(price)))
            {
                Notify.Problem("that's more than you've got.");
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            // Bought: it is on the car already, so what was on it before is now this.
            _wasOn = _pick;
            Hud.PlaySound("WEAPON_PURCHASE", "HUD_AMMO_SHOP_SOUNDSET");

            Notify.Important((price > 0 ? "~y~-$" + price.ToString("N0") + "~s~  " : "") +
                             chosen.Name + ": " + chosen.Label(_pick) + ".");

            try { Bought?.Invoke(); }
            catch { }
        }

        /// <summary>Out of a list. Nothing bought means what was on the car goes back on it.</summary>
        private void Back(bool sound)
        {
            if (_inside)
            {
                var cat = _cats[_cat];

                if (_pick != _wasOn)
                {
                    try { cat.Put(_wasOn); }
                    catch { }
                }
            }

            _inside = false;
            _top = 0;
            Scroll(_cat, _cats.Count);
            _pickedAt = Game.GameTime;
            _glide.Reset();

            if (sound) Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[]
                     {
                         Control.PhoneUp, Control.PhoneDown, Control.PhoneLeft, Control.PhoneRight,
                         Control.PhoneSelect, Control.PhoneCancel
                     })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }

        // ---- drawing --------------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var width = Hud.ToX(PanelWidthH);

            // DOWN THE LEFT, ABOVE THE MINIMAP. This is a menu about the look of a car, and
            // in the middle of the screen it sat on top of the car -- every respray and every
            // set of rims changed something you could not see while you chose it.
            var left = Hud.ToX(LeftMarginH);
            var pad = Hud.ToX(PadH);
            var n = _inside ? _cats[_cat].Count() : _cats.Count;
            var rows = Math.Max(1, Math.Min(Shown, n));
            var height = UiKit.HeadH + 0.026f + rows * RowHeight + 0.010f + UiKit.FootH;

            // Hung from its bottom rather than centred, so a long list grows upwards into
            // empty screen instead of downwards into the map.
            var top = FootY - height + _curtain.Lift;

            var age = Game.GameTime - _openedAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);
            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, width, height, arrive);

            var x = left + pad;
            var right = left + width - pad;
            var wide = right - x;
            var caps = Palette.Alpha(Palette.TextDim, (int)(190f * arrive));

            // ---- the name over the door, the car in the bay, what is in your pocket ----
            var y = UiKit.Head(left, top, width, pad, "garage.png", "HAO'S MUFFLER SHOP",
                             _name.ToLowerInvariant(), "$" + Game.Player.Money.ToString("N0"), arrive);

            // What you are looking at, and what is on the car right now.
            var head = _inside ? _cats[_cat].Name.ToUpperInvariant() : "WHAT IT CAN DO";
            var side = _inside
                ? "ON IT NOW: " + _cats[_cat].Label(_wasOn).ToUpperInvariant()
                : _cats.Count + (_cats.Count == 1 ? " THING" : " THINGS");

            Hud.Text(head, x, y + 0.002f, 0.22f, caps, Hud.FontLabel, centre: false);
            Hud.TextRight(side, right, y + 0.002f, 0.22f, caps, Hud.FontLabel);

            y += 0.026f;

            if (n == 0)
            {
                Hud.Text("Nothing to be done to this one.", x, y + 0.006f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(255f * arrive)), Hud.FontBody, centre: false);
                Keys(x, right, top + height - UiKit.FootH + 0.006f, arrive);
                return;
            }

            var grown = Theme.Grown(_pickedAt);
            var barWide = wide + pad * 0.7f;
            var selected = _inside ? _pick : _cat;
            var last = _inside ? _lastPick : _lastCat;

            _glide.Begin();

            for (var i = _top; i < Math.Min(n, _top + Shown); i++)
            {
                var here = i == selected;
                var lit = Theme.Lit(i, selected, last, grown);

                Theme.Plate(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit * arrive);
                Theme.Sheen(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit * arrive);

                if (here) _glide.Target(x - pad * 0.35f, y - 0.004f, barWide, RowHeight);

                var ink = Theme.Ink(Palette.Alpha(here ? Palette.Text : Palette.TextDim, (int)(255f * arrive)), lit);

                if (_inside)
                {
                    var cat = _cats[_cat];
                    var price = cat.Price(i);
                    var fitted = i == _wasOn;

                    Hud.Text(cat.Label(i), x, y + 0.006f, 0.30f, ink, Hud.FontBody, centre: false);
                    Hud.TextRight(fitted ? "FITTED" : price > 0 ? "$" + price.ToString("N0") : "FREE",
                                  right, y + 0.007f, 0.28f,
                                  fitted ? Palette.Alpha(Palette.Brand, (int)(255f * arrive)) : ink, Hud.FontLabel);
                }
                else
                {
                    var cat = _cats[i];
                    Hud.Text(cat.Name, x, y + 0.006f, 0.30f, ink, Hud.FontBody, centre: false);

                    string now;
                    try { now = cat.Label(Math.Max(0, Math.Min(cat.Count() - 1, cat.Current()))); }
                    catch { now = ""; }

                    Hud.TextRight(now.ToUpperInvariant(), right, y + 0.008f, 0.24f, caps, Hud.FontLabel);
                }

                y += RowHeight;
            }

            if (n > Shown)
            {
                Hud.TextRight((selected + 1) + " / " + n, right, y + 0.002f, 0.22f, caps, Hud.FontLabel);
            }

            Keys(x, right, top + height - UiKit.FootH + 0.006f, arrive);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw(arrive);
        }

        /// <summary>The keys, drawn as keys. Inside a list the way out puts things back; outside it drives out.</summary>
        private void Keys(float x, float right, float footY, float arrive)
        {
            Theme.Rule(x, footY, right - x, arrive);

            var ky = footY + 0.011f;

            if (_inside)
            {
                UiKit.KeyRight(right, ky, UiKit.Back, "PUT IT BACK", arrive);

                var kx = UiKit.Key(x, ky, null, "arrow_updown.png", "TRY IT ON", arrive);
                UiKit.Key(kx, ky, UiKit.Confirm, null, "BUY IT", arrive);
                return;
            }

            UiKit.KeyRight(right, ky, UiKit.Back, "DRIVE OUT", arrive);

            var mx = UiKit.Key(x, ky, null, "arrow_updown.png", "MOVE", arrive);
            UiKit.Key(mx, ky, UiKit.Confirm, null, "OPEN", arrive);
        }
    }
}
