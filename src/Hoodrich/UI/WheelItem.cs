using System;
using Hoodrich.Core;
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
        /// A PNG of ours, which wins over anything the game ships.
        ///
        /// Set once and never re-resolved. A file is either there or it is not, and unlike a
        /// streamed dictionary the answer cannot change between one frame and the next.
        /// </summary>
        public string IconFile = "";

        /// <summary>
        /// Width over height of the source art, so it is drawn in its own proportions.
        ///
        /// Weapon art is a long letterbox and the menu and inventory sprites are square. Fitting
        /// both into one fixed box stretched the square ones sideways, which is why they looked
        /// squashed.
        /// </summary>
        public float IconAspect = 1f;

        /// <summary>Set when this item's art is a blip tag rather than a texture.</summary>
        public string IconBlip = "";

        /// <summary>
        /// How many times a texture has been looked for and not found.
        ///
        /// A count rather than a flag, because "not resolved yet" and "will never resolve" look
        /// identical from the outside and only time tells them apart. Thirty frames is long
        /// enough for a dictionary that is merely slow, short enough that a missing one stops
        /// asking -- one wheel entry once wrote 6,768 log lines about the same absent texture.
        /// </summary>
        public int IconTries;

        public const int IconAttempts = 30;

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

        /// <summary>
        /// A PNG drawn immediately AFTER the label, hard against the last letter of it.
        ///
        /// Different job from ArtFile below, which sits in the gutter and says what the row is
        /// about. This one modifies the name it follows -- how cut the bag is, in the place you
        /// would write it if you were writing it: after the word.
        /// </summary>
        public string MarkFile;

        /// <summary>Overrides the value colour; null uses the default.</summary>
        public Color? Tint;

        /// <summary>
        /// A PNG in data\icons, drawn in the gutter to the left of the label.
        ///
        /// The side panel was the last surface in the mod with no art at all -- every other
        /// screen had been given icons and this one kept printing seventy-odd rows of bare
        /// words beside them. Optional, so a row that has nothing worth a picture stays
        /// flush left rather than sitting in an empty column.
        /// </summary>
        public string ArtFile;
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

        public WheelPage Row(string label, string value, Color? tint = null, string art = null,
                             string mark = null)
        {
            Panel.Add(new PanelRow
            {
                Label = label,
                Value = value,
                Tint = tint,
                ArtFile = art ?? "",
                MarkFile = mark ?? ""
            });
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

            // A blip icon skips the whole texture path -- there is nothing to stream, nothing
            // to resolve, and it is drawn as text rather than as a sprite.
            if (icon.IsBlip)
            {
                item.IconBlip = icon.Blip;
                return this;
            }

            // Ours is settled here and now, and that is a fix rather than a shortcut.
            //
            // A file needs nothing streamed and nothing resolved, so it was being answered
            // inside the readiness lambda below -- which is only ever CALLED from HasIcon, and
            // HasIcon opens with `!string.IsNullOrEmpty(IconDict)`. A FromFile icon has no
            // dictionary, so HasIcon was false, so the lambda never ran, so IconFile was never
            // set and every one of them silently fell back to its text glyph. That is why
            // "Text the plug" was drawing an equals sign.
            if (icon.HasFile)
            {
                item.IconFile = icon.File;
                item.IconAspect = 1f;
                item.IconTries = WheelItem.IconAttempts;
                return this;
            }

            item.IconDict = icon.Dict;

            // Resolved per frame rather than here: the dictionary is usually still streaming
            // when the page is built, and an unloaded dictionary answers "no" to every name.
            item.IconReady = () =>
            {
                // Ours needs no dictionary and no streaming, so it is answered before the
                // texture machinery is asked anything at all.
                if (icon.HasFile)
                {
                    item.IconFile = icon.File;
                    return true;
                }

                if (!Draw.EnsureTextureDict(icon.Dict)) return false;

                // Retried while it keeps failing rather than cached: a dictionary can report
                // itself resident a frame before its textures answer to being measured, and
                // locking in that first answer left the icon missing for good.
                if (string.IsNullOrEmpty(item.IconTexture) && item.IconTries < WheelItem.IconAttempts)
                {
                    item.IconTries++;

                    // Resolved once and kept. The aspect comes from the texture that actually
                    // won, not from a guess -- without it every sprite was drawn square, which
                    // cost wide art half its width and made a couple of wedges read as empty.
                    float aspect;
                    item.IconTexture = icon.Resolve(out aspect);
                    item.IconAspect = aspect;

                    // Logged once per icon. What the game reports for these dictionaries is the
                    // only thing that decides the shape, and it is not something you can read off
                    // a screenshot -- so when a wedge looks wrong this is the line that says why.
                    //
                    // "Once" was the intention and not what happened. The guard above is on the
                    // texture being empty, and a texture that never resolves stays empty -- so
                    // an icon the install does not have was re-resolved and re-logged on every
                    // frame the wheel was open. One session came to 6,768 lines of it for a
                    // single entry, all of them saying the same thing about the same missing
                    // texture. It gives up now, and says so once.
                    if (!string.IsNullOrEmpty(item.IconTexture))
                    {
                        Log.Info("Icon " + item.Label + ": " + icon.Dict + "/" + item.IconTexture +
                                 " aspect " + aspect.ToString("0.00"));
                    }

                    // Only complain on the LAST attempt. The line above it is right about
                    // giving up, and my first version of it gave up on the first frame -- when
                    // a dictionary can report itself resident before its textures will answer
                    // to being measured, which is the exact failure the comment above warns
                    // about. So a texture that is simply slow gets thirty frames, and one that
                    // is genuinely absent still stops asking.
                    if (string.IsNullOrEmpty(item.IconTexture) && item.IconTries >= WheelItem.IconAttempts)
                    {
                        Log.Info("Icon " + item.Label + ": nothing in " + icon.Dict + " matched.");
                    }
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
