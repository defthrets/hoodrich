using System;
using System.Collections.Generic;
using System.Drawing;
using GTA.Native;

namespace Hoodrich.Court
{
    /// <summary>
    /// The court's own drawing: rectangles, text and sprites, straight through the game's natives
    /// and set the way Posted Up's UI.Draw sets them, so the court looks the same in either mod
    /// and needs neither's. The one thing it tells its host is that a rectangle went into the
    /// list -- the count the whole set shares. See CourtHost.Counted.
    /// </summary>
    internal static class CourtDraw
    {
        /// <summary>Chalet London: anything meant to be read.</summary>
        public const int FontBody = 0;

        /// <summary>Chalet Comprime Cologne: condensed, for labels.</summary>
        public const int FontLabel = 4;

        /// <summary>Pricedown, the GTA logo face: the banner's big word and nothing else.</summary>
        public const int FontPricedown = 7;

        /// <summary>
        /// ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME truncates past 99 bytes, so longer text goes
        /// in pieces, under the game's own many-piece format.
        /// </summary>
        private const int ChunkSize = 96;

        private static readonly HashSet<string> Asked = new HashSet<string>();

        /// <summary>A filled rectangle, placed by its centre, as DRAW_RECT places it.</summary>
        public static void Rect(float x, float y, float w, float h, Color c)
        {
            CourtHost.Counted();
            Function.Call(Hash.DRAW_RECT, x, y, w, h, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
        }

        /// <summary>One line of text; scale is the game's text scale.</summary>
        public static void Text(string text, float x, float y, float scale, Color c,
                                int font = FontLabel, bool centre = true,
                                bool shadow = true, bool outline = false)
        {
            if (string.IsNullOrEmpty(text)) return;

            text = CourtHost.T(text);

            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
            Function.Call(Hash.SET_TEXT_CENTRE, centre);

            // Never inherit somebody else's wrap region.
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, centre ? 0 : 1);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, 1f);

            if (shadow) Function.Call(Hash.SET_TEXT_DROP_SHADOW);
            if (outline) Function.Call(Hash.SET_TEXT_OUTLINE);

            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, FormatFor(text));
            AddLongString(text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
        }

        /// <summary>Text ending at a right-hand edge: justified right inside a wrap region that ends there.</summary>
        public static void TextRight(string text, float rightX, float y, float scale, Color c,
                                     int font = FontBody, bool shadow = true)
        {
            if (string.IsNullOrEmpty(text)) return;

            text = CourtHost.T(text);

            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
            Function.Call(Hash.SET_TEXT_CENTRE, false);
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, 2);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, rightX);
            if (shadow) Function.Call(Hash.SET_TEXT_DROP_SHADOW);

            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, FormatFor(text));
            AddLongString(text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0f, y);

            // Both stick across draws: left set, the next left-aligned label in anybody's mod
            // is flowed inside this one's box.
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, 0);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, 1f);
        }

        /// <summary>A sprite from a texture dictionary, rotated in degrees clockwise.</summary>
        public static void Sprite(string dict, string texture, float x, float y, float w, float h,
                                  float rotationDeg, Color c)
        {
            Function.Call(Hash.DRAW_SPRITE, dict, texture, x, y, w, h, rotationDeg,
                          (int)c.R, (int)c.G, (int)c.B, (int)c.A);
        }

        /// <summary>Whether a texture dictionary is in; asked for if it is not.</summary>
        public static bool EnsureTextureDict(string dict)
        {
            if (string.IsNullOrEmpty(dict)) return false;

            try
            {
                if (Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict)) return true;

                Asked.Add(dict);
                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, dict, false);
            }
            catch { }

            return false;
        }

        /// <summary>Every texture dictionary the court asked for, handed back as a game ends.</summary>
        public static void Release()
        {
            foreach (var d in Asked)
            {
                try { Function.Call(Hash.SET_STREAMED_TEXTURE_DICT_AS_NO_LONGER_NEEDED, d); }
                catch { }
            }

            Asked.Clear();
        }

        private static string FormatFor(string text)
        {
            return text != null && text.Length > ChunkSize ? "CELL_EMAIL_BCON" : "STRING";
        }

        private static void AddLongString(string text)
        {
            for (var i = 0; i < text.Length; i += ChunkSize)
            {
                var len = Math.Min(ChunkSize, text.Length - i);
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text.Substring(i, len));
            }
        }
    }
}
