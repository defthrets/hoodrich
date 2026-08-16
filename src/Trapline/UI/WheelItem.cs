using System;
using System.Collections.Generic;
using System.Drawing;

namespace Trapline.UI
{
    /// <summary>One selectable segment of the wheel.</summary>
    internal sealed class WheelItem
    {
        /// <summary>Short label drawn inside the segment.</summary>
        public string Label = "";

        /// <summary>Text glyph drawn above the label. Text symbols only, never emoji.</summary>
        public string Symbol = "";

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
