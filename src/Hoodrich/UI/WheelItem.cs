using System;
using System.Collections.Generic;
using System.Drawing;

namespace Hoodrich.UI
{
    /// <summary>One selectable segment of the wheel.</summary>
    internal sealed class WheelItem
    {
        /// <summary>Short label drawn inside the segment.</summary>
        public string Label = "";

        /// <summary>Text glyph drawn above the label. Text symbols only, never emoji.</summary>
        public string Symbol = "";

        /// <summary>
        /// Streamed texture dictionary and texture for a real game sprite drawn in place of
        /// <see cref="Symbol"/> -- this is how weapons show their own art. Both are the weapon's
        /// model name.
        /// </summary>
        public string IconDict = "";
        public string IconTexture = "";

        /// <summary>
        /// Width over height of the source art, so it is drawn in its own proportions.
        ///
        /// Weapon art is a long letterbox and the menu and inventory sprites are square. Fitting
        /// both into one fixed box stretched the square ones sideways, which is why they looked
        /// squashed.
        /// </summary>
        public float IconAspect = 1f;

        /// <summary>
        /// Whether the icon is resident right now. Evaluated per frame rather than at page-build
        /// time, because the dict often arrives a frame or two after the wheel opens.
        /// </summary>
        public Func<bool> IconReady;

        public bool HasIcon => !string.IsNullOrEmpty(IconDict) && IconReady != null && IconReady();

        /// <summary>Detail line shown in the hub while this item is hovered.</summary>
        public string Detail = "";

        /// <summary>Right-hand value shown in the hub, e.g. a price or a count.</summary>
        public string Value = "";

        /// <summary>Overrides the hover colour for this segment when set. Used by gang/turf pages.</summary>
        public Color? Tint = null;

        /// <summary>A disabled item still draws (so the wheel layout stays stable) but cannot be picked.</summary>
        public bool Enabled = true;

        /// <summary>Why the item is disabled; surfaced in the hub instead of Detail.</summary>
        public string DisabledReason = "";

        /// <summary>Invoked on commit. Null when the item only opens a submenu.</summary>
        public Action OnSelect;

        /// <summary>Builds the page this item drills into, or null for a leaf.</summary>
        public Func<WheelPage> Submenu;

        public bool IsSubmenu => Submenu != null;
    }

    /// <summary>One label/value line in a page's side panel.</summary>
    internal struct PanelRow
    {
        public string Label;
        public string Value;

        /// <summary>Overrides the value colour; null uses the default.</summary>
        public Color? Tint;
    }

    /// <summary>A ring of items plus the header shown while it is open.</summary>
    internal sealed class WheelPage
    {
        public string Title = "";
        public string Subtitle = "";
        public readonly List<WheelItem> Items = new List<WheelItem>();

        /// <summary>
        /// Optional stat block drawn beside the wheel. The hub is far too small for a dossier,
        /// so pages that are mostly about reading numbers (Gang, Turf) put them here.
        /// </summary>
        public string PanelTitle = "";
        public readonly List<PanelRow> Panel = new List<PanelRow>();

        public WheelPage(string title, string subtitle = "")
        {
            Title = title;
            Subtitle = subtitle;
        }

        public WheelPage Row(string label, string value, Color? tint = null)
        {
            Panel.Add(new PanelRow { Label = label, Value = value, Tint = tint });
            return this;
        }

        public WheelPage Add(WheelItem item)
        {
            if (item != null) Items.Add(item);
            return this;
        }

        /// <summary>
        /// Gives the item just added a real game sprite instead of its text glyph.
        ///
        /// Readiness is checked per frame rather than here, because a texture dictionary
        /// usually arrives a frame or two after the wheel opens -- evaluating it at build time
        /// would leave every icon permanently missing on the first open.
        /// </summary>
        public WheelPage WithIcon(Icon icon)
        {
            if (Items.Count == 0 || !icon.IsSet) return this;

            var item = Items[Items.Count - 1];
            item.IconDict = icon.Dict;

            // Resolved per frame rather than here: the dictionary is usually still streaming
            // when the page is built, and an unloaded dictionary answers "no" to every name.
            item.IconReady = () =>
            {
                if (!Draw.EnsureTextureDict(icon.Dict)) return false;

                if (string.IsNullOrEmpty(item.IconTexture))
                {
                    // Resolved once and kept. The aspect comes from the texture that actually
                    // won, not from a guess -- without it every sprite was drawn square, which
                    // cost wide art half its width and made a couple of wedges read as empty.
                    float aspect;
                    item.IconTexture = icon.Resolve(out aspect);
                    item.IconAspect = aspect;
                }

                return !string.IsNullOrEmpty(item.IconTexture);
            };

            return this;
        }

        public WheelPage Add(string label, string symbol, Action onSelect, string detail = "",
                             string value = "", bool enabled = true, string disabledReason = "")
        {
            return Add(new WheelItem
            {
                Label = label,
                Symbol = symbol,
                Detail = detail,
                Value = value,
                Enabled = enabled,
                DisabledReason = disabledReason,
                OnSelect = onSelect
            });
        }

        public WheelPage AddSub(string label, string symbol, Func<WheelPage> submenu, string detail = "",
                                string value = "", bool enabled = true, string disabledReason = "")
        {
            return Add(new WheelItem
            {
                Label = label,
                Symbol = symbol,
                Detail = detail,
                Value = value,
                Enabled = enabled,
                DisabledReason = disabledReason,
                Submenu = submenu
            });
        }
    }
}
