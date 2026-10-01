using System;
using GTA;
using GTA.Native;
using Control = GTA.Control;

namespace Hoodrich.Court
{
    /// <summary>
    /// The row of buttons along the bottom right: the game's own instructional_buttons movie, with
    /// the real key for each control drawn in it, so a pad shows a pad's buttons and a keyboard its
    /// keys. The court's own copy of Posted Up's casino row, so the court needs nothing of its host
    /// to draw one.
    /// </summary>
    internal static class CourtButtons
    {
        private static int _movie;
        private static string _shown = "";

        /// <summary>Pairs of a control and what it does, rightmost first. Drawn this frame.</summary>
        public static void Show(params object[] pairs)
        {
            try
            {
                if (_movie == 0) _movie = Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "instructional_buttons");
                if (_movie == 0 || !Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, _movie)) return;

                // Built again when he changes device, not only when the labels change: picking up a
                // pad left the keyboard's arrows in the row until something else changed it.
                var key = (CourtHost.OnPad() ? "pad|" : "keys|") + string.Join("|", pairs);

                if (key != _shown)
                {
                    _shown = key;

                    Method("CLEAR_ALL");

                    Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "SET_CLEAR_SPACE");
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 200);
                    Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);

                    var slot = 0;

                    for (var i = 0; i + 1 < pairs.Length; i += 2)
                    {
                        var control = (Control)pairs[i];
                        var label = CourtHost.T((string)pairs[i + 1]);
                        var button = Function.Call<string>(Hash.GET_CONTROL_INSTRUCTIONAL_BUTTONS_STRING, 2, (int)control, true);

                        Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "SET_DATA_SLOT");
                        Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, slot++);
                        Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_PLAYER_NAME_STRING, button);
                        Function.Call(Hash.BEGIN_TEXT_COMMAND_SCALEFORM_STRING, "STRING");
                        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, label);
                        Function.Call(Hash.END_TEXT_COMMAND_SCALEFORM_STRING);
                        Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
                    }

                    Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "SET_BACKGROUND_COLOUR");
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 0);
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 0);
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 0);
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 80);
                    Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);

                    Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "DRAW_INSTRUCTIONAL_BUTTONS");
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, -1);
                    Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
                }

                Function.Call(Hash.DRAW_SCALEFORM_MOVIE_FULLSCREEN, _movie, 255, 255, 255, 255, 0);
            }
            catch (Exception ex)
            {
                CourtHost.Debug("Hoops: the button row would not draw: " + ex.Message);
            }
        }

        private static void Method(string name)
        {
            Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, name);
            Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
        }

        /// <summary>Let go of the movie as a game ends.</summary>
        public static void Drop()
        {
            if (_movie == 0) return;

            try
            {
                var handle = new OutputArgument(_movie);
                Function.Call(Hash.SET_SCALEFORM_MOVIE_AS_NO_LONGER_NEEDED, handle);
            }
            catch { }

            _movie = 0;
            _shown = "";
        }
    }
}
