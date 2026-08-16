using System;
using System.IO;
using System.Text;

namespace Hoodrich.Core
{
    /// <summary>Load/save helpers for Json documents on disk.</summary>
    internal static class JsonFile
    {
        /// <summary>Reads a Json document. A missing or malformed file yields null, never an exception.</summary>
        public static Json Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path, Encoding.UTF8);
                if (!Json.TryParse(text, out var doc))
                {
                    Log.Error("Malformed JSON in " + Path.GetFileName(path) + " - ignoring it.");
                    return null;
                }
                return doc;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read " + path, ex);
                return null;
            }
        }

        /// <summary>
        /// Writes atomically: full write to a .tmp sibling, then replace. A crash mid-save
        /// (which for a game mod means an alt-F4 during an autosave) leaves the previous
        /// save intact instead of a truncated one.
        /// </summary>
        public static bool Write(string path, Json doc)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var tmp = path + ".tmp";
                File.WriteAllText(tmp, doc.ToJsonString(true), new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    var bak = path + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Replace(tmp, path, bak);
                }
                else
                {
                    File.Move(tmp, path);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not write " + path, ex);
                return false;
            }
        }
    }
}
