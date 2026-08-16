using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Hoodrich.Core
{
    /// <summary>
    /// Small INI reader. Tolerates both ';' and '//' comments, trailing inline comments,
    /// and keys outside any section (bucketed under ""). Reading a missing file yields an
    /// empty instance rather than throwing, so a deleted ini falls back to code defaults.
    /// </summary>
    internal sealed class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static IniFile Load(string path)
        {
            var ini = new IniFile();
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warn("No ini at " + path + " - using built-in defaults.");
                    return ini;
                }

                var current = ini.SectionFor("");
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line[0] == ';' || line[0] == '#') continue;
                    if (line.StartsWith("//", StringComparison.Ordinal)) continue;

                    if (line[0] == '[')
                    {
                        var close = line.IndexOf(']');
                        if (close > 1)
                        {
                            current = ini.SectionFor(line.Substring(1, close - 1).Trim());
                            continue;
                        }
                    }

                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line.Substring(0, eq).Trim();
                    var value = StripInlineComment(line.Substring(eq + 1)).Trim();
                    current[key] = value;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed reading ini " + path, ex);
            }

            return ini;
        }

        /// <summary>
        /// Strips a trailing '//' or ';' comment, but only when it is preceded by whitespace,
        /// so values that legitimately contain those characters survive.
        /// </summary>
        private static string StripInlineComment(string value)
        {
            for (var i = 1; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i - 1])) continue;
                if (value[i] == ';') return value.Substring(0, i);
                if (value[i] == '/' && i + 1 < value.Length && value[i + 1] == '/') return value.Substring(0, i);
            }
            return value;
        }

        private Dictionary<string, string> SectionFor(string name)
        {
            if (!_sections.TryGetValue(name, out var s))
            {
                s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections[name] = s;
            }
            return s;
        }

        private bool TryGet(string section, string key, out string value)
        {
            value = null;
            return _sections.TryGetValue(section, out var s) && s.TryGetValue(key, out value);
        }

        public IEnumerable<KeyValuePair<string, string>> Section(string section)
        {
            if (_sections.TryGetValue(section, out var s)) return s;
            return new Dictionary<string, string>();
        }

        public string GetString(string section, string key, string fallback)
        {
            return TryGet(section, key, out var v) && v.Length > 0 ? v : fallback;
        }

        public int GetInt(string section, string key, int fallback)
        {
            if (!TryGet(section, key, out var v)) return fallback;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n : Complain(section, key, v, fallback);
        }

        public float GetFloat(string section, string key, float fallback)
        {
            if (!TryGet(section, key, out var v)) return fallback;
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                ? n : Complain(section, key, v, fallback);
        }

        public bool GetBool(string section, string key, bool fallback)
        {
            if (!TryGet(section, key, out var v)) return fallback;
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1") return true;
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0") return false;
            return Complain(section, key, v, fallback);
        }

        /// <summary>Parses a Keys name. "None" and unparseable values both yield Keys.None.</summary>
        public Keys GetKey(string section, string key, Keys fallback)
        {
            if (!TryGet(section, key, out var v)) return fallback;
            if (v.Equals("None", StringComparison.OrdinalIgnoreCase)) return Keys.None;

            try
            {
                return (Keys)Enum.Parse(typeof(Keys), v, true);
            }
            catch
            {
                Log.Warn("[" + section + "] " + key + "=" + v + " is not a key name; using " + fallback + ".");
                return fallback;
            }
        }

        public T GetEnum<T>(string section, string key, T fallback) where T : struct
        {
            if (!TryGet(section, key, out var v)) return fallback;
            try
            {
                return (T)Enum.Parse(typeof(T), v, true);
            }
            catch
            {
                Log.Warn("[" + section + "] " + key + "=" + v + " is not a valid " + typeof(T).Name +
                         "; using " + fallback + ".");
                return fallback;
            }
        }

        private static T Complain<T>(string section, string key, string raw, T fallback)
        {
            Log.Warn("[" + section + "] " + key + "=" + raw + " could not be parsed; using " + fallback + ".");
            return fallback;
        }
    }
}
