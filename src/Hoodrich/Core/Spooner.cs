using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using GTA;
using GTA.Math;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Menyoo's Object Spooner files, read and written.
    ///
    /// WHY THIS EXISTS. Placing a man on a corner by hand in Menyoo takes a minute; writing
    /// the same man into a C# file, building, deploying and reloading takes ten, and you
    /// cannot see what you are doing while you do it. So the spooner is the level editor and
    /// this is the loader: save the placement in Menyoo and the mod builds it every session,
    /// streamed in as you come near it and taken out again when you leave.
    ///
    /// THE FORMAT IS READ LOOSELY ON PURPOSE. Menyoo has been through several hands and its
    /// tag names differ between versions -- a weapon is CurrentWeapon in one and Weapon in
    /// another, an animation is Anim/AnimDict/Dict depending on who last touched the writer.
    /// A parser that insists on one spelling reads a file somebody made this week and silently
    /// finds nothing in it, which is the worst failure available here because the file looks
    /// fine and the corner is simply empty. So every field is looked up by a LIST of names,
    /// case-insensitively, anywhere under the placement -- and what a thing IS comes from its
    /// model rather than from the Type number, because the model cannot be spelled wrong.
    /// </summary>
    internal static class Spooner
    {
        internal enum Kind
        {
            Prop,
            Ped,
            Vehicle
        }

        /// <summary>One thing somebody put somewhere, and everything about it worth keeping.</summary>
        internal sealed class Placed
        {
            public Kind What = Kind.Prop;

            /// <summary>The model by name where the file gave one, and by hash otherwise. Name wins.</summary>
            public string ModelName = "";
            public int ModelHash;

            public Vector3 At;
            public float Pitch, Roll, Yaw;

            public bool Frozen = true;
            public bool Visible = true;
            public bool Invincible;
            public bool Gravity = true;
            public int Opacity = 255;

            // ---- peds -------------------------------------------------------------
            public string Weapon = "";
            public string Scenario = "";
            public string AnimDict = "";
            public string AnimClip = "";
            public string Relationship = "";
            public int Health = -1;
            public int Armour = -1;

            /// <summary>Clothing, as slot to drawable and texture. Empty means whatever the model wears.</summary>
            public readonly List<int[]> Components = new List<int[]>();
            public readonly List<int[]> Props = new List<int[]>();

            // ---- what is stuck to what ---------------------------------------------

            /// <summary>The handle this placement had in the session it was saved from; how attachments name each other.</summary>
            public int Handle;

            public bool Attached;
            public int AttachedTo;
            public int Bone;
            public Vector3 AttachOffset;
            public Vector3 AttachRotation;

            /// <summary>Which file it came out of, for the log when it will not build.</summary>
            public string From = "";

            public Model Model
            {
                get
                {
                    if (!string.IsNullOrEmpty(ModelName))
                    {
                        var byName = new Model(ModelName);
                        if (byName.IsValid) return byName;
                    }

                    return new Model(ModelHash);
                }
            }

            /// <summary>
            /// The weapon as a hash, whatever the file called it. Menyoo writes 0xfad1f1c9 and
            /// other hands write WEAPON_CARBINERIFLE; both end up here as the same number.
            /// </summary>
            public uint WeaponHash;

            /// <summary>The relationship group as a hash, same deal.</summary>
            public uint GroupHash;

            /// <summary>How the animation is played: 1 loops, and a looping idle is the point.</summary>
            public int AnimFlag = 1;

            public bool Armed => WeaponHash != 0 && WeaponHash != Unarmed;

            /// <summary>WEAPON_UNARMED. Seven of the ten peds in the first real file had a rifle and two had this.</summary>
            public const uint Unarmed = 0xA2719263;
        }

        /// <summary>
        /// A map prop taken OUT of the world and kept out.
        ///
        /// THE SPOONER CANNOT SAY THIS AND IT IS THE HALF THAT WAS MISSING. Menyoo records
        /// what you PUT somewhere -- a placement, with a model and a position -- and there is
        /// no tag anywhere in its format for what you took away. Delete one of Denise's
        /// cupboards to make room for a scale and a set of bags, save the file, and the
        /// cupboard is back next session standing through the middle of everything.
        ///
        /// So this is ours. It is written in the same file, in a &lt;Removals&gt; block that
        /// Menyoo ignores when it loads one -- so a scene with removals in it still opens in
        /// the spooner and still saves out of it, minus this block. That is the whole reason
        /// it is a sibling element rather than something clever hidden in the Note.
        ///
        /// A MODEL AND A PLACE, NOT A HANDLE. The game's own way of doing this is
        /// CREATE_MODEL_HIDE, which takes a sphere and a model and hides every copy of that
        /// model inside it. Handles do not survive a session and a coordinate does; the radius
        /// is small on purpose, so hiding a bin at Denise's does not hide the one outside
        /// Gerald's.
        /// </summary>
        internal sealed class Hidden
        {
            public string ModelName = "";
            public uint ModelHash;
            public Vector3 At;
            public float Radius = DefaultRadius;
            public string From = "";
        }

        /// <summary>
        /// How big a sphere one removal covers.
        ///
        /// A METRE AND A HALF, which is a prop and its immediate air. CREATE_MODEL_HIDE hides
        /// every copy of the model inside the sphere, so this is the number that decides
        /// whether "that bin" means one bin or a street of them. Big enough that a coordinate
        /// read off a prop's origin still covers the prop; small enough that the next one along
        /// is somebody else's.
        /// </summary>
        public const float DefaultRadius = 1.5f;

        // ================================================================= reading

        /// <summary>
        /// Everything in one spooner file. An unreadable file is a log line and an empty list,
        /// never a throw: one bad file in the folder must not take the other nine with it.
        /// </summary>
        public static List<Placed> Read(string path)
        {
            var found = new List<Placed>();

            try
            {
                var doc = new XmlDocument();
                doc.Load(path);

                var name = Path.GetFileNameWithoutExtension(path);
                var nodes = doc.GetElementsByTagName("Placement");

                foreach (XmlNode node in nodes)
                {
                    var one = OnePlacement(node, name);
                    if (one != null) found.Add(one);
                }

                // Some hands write the list as <Placements><Placement/></Placements> and some
                // older ones as <Object>/<Ped> siblings. The tag search above catches the first
                // shape; this catches the second without a second parser.
                if (found.Count == 0)
                {
                    foreach (var tag in new[] { "Object", "Ped", "Vehicle" })
                    {
                        foreach (XmlNode node in doc.GetElementsByTagName(tag))
                        {
                            var one = OnePlacement(node, name);
                            if (one != null) found.Add(one);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the placements in " + Path.GetFileName(path) + ": " + ex.Message);
            }

            return found;
        }

        /// <summary>
        /// Adds one removal to a file, making the file if it is not there yet.
        ///
        /// READ, ADD, REWRITE, because there is no way to append to XML and pretending
        /// otherwise produces a file with two roots that nothing will open. The whole file is
        /// a few dozen lines; this is not the expensive part of anything.
        ///
        /// The same prop twice is one prop. Somebody who cannot remember whether they already
        /// hid that bin should be able to hide it again and get a shrug, not a second row.
        /// </summary>
        public static bool Bury(string path, Hidden one)
        {
            if (one == null || one.ModelHash == 0) return false;

            try
            {
                var have = File.Exists(path) ? Gone(path) : new List<Hidden>();

                foreach (var had in have)
                {
                    if (had.ModelHash == one.ModelHash && had.At.DistanceTo(one.At) < 0.5f) return true;
                }

                have.Add(one);

                var sb = new StringBuilder();

                sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
                sb.Append("<SpoonerPlacements>\n");
                sb.Append("  <Note>Map props hidden by hand. Written by Hoodrich; Menyoo ignores the Removals block.</Note>\n");
                sb.Append("  <Removals>\n");

                foreach (var row in have)
                {
                    sb.Append("    <Remove>\n");
                    sb.Append("      <HashName>").Append(Escape(row.ModelName ?? "")).Append("</HashName>\n");
                    sb.Append("      <ModelHash>0x").Append(row.ModelHash.ToString("x8")).Append("</ModelHash>\n");
                    sb.Append("      <X>").Append(row.At.X.ToString("0.###", CultureInfo.InvariantCulture)).Append("</X>\n");
                    sb.Append("      <Y>").Append(row.At.Y.ToString("0.###", CultureInfo.InvariantCulture)).Append("</Y>\n");
                    sb.Append("      <Z>").Append(row.At.Z.ToString("0.###", CultureInfo.InvariantCulture)).Append("</Z>\n");
                    sb.Append("      <Radius>").Append(row.Radius.ToString("0.##", CultureInfo.InvariantCulture)).Append("</Radius>\n");
                    sb.Append("    </Remove>\n");
                }

                sb.Append("  </Removals>\n");
                sb.Append("</SpoonerPlacements>\n");

                File.WriteAllText(path, sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not write the removal: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The removals in one scene file, or an empty list for the great majority that have
        /// none. Never throws, for the same reason Read never throws.
        /// </summary>
        public static List<Hidden> Gone(string path)
        {
            var found = new List<Hidden>();

            try
            {
                var doc = new XmlDocument();
                doc.Load(path);

                var name = Path.GetFileNameWithoutExtension(path);

                foreach (XmlNode node in doc.GetElementsByTagName("Remove"))
                {
                    try
                    {
                        var one = new Hidden { From = name };

                        one.ModelName = Text(node, "HashName", "ModelName", "Model");

                        one.ModelHash = AsHash(Text(node, "ModelHash"), null);

                        if (one.ModelHash == 0 && one.ModelName.Length > 0)
                        {
                            one.ModelHash = AsHash(one.ModelName, null);
                        }

                        if (one.ModelHash == 0) continue;

                        one.At = new Vector3(Number(node, 0f, "X"),
                                             Number(node, 0f, "Y"),
                                             Number(node, 0f, "Z"));

                        var r = Number(node, 0f, "Radius");
                        if (r > 0.05f) one.Radius = r;

                        found.Add(one);
                    }
                    catch
                    {
                        // One bad row is one bad row.
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the removals in " + Path.GetFileName(path) + ": " + ex.Message);
            }

            return found;
        }

        private static Placed OnePlacement(XmlNode node, string from)
        {
            try
            {
                var p = new Placed { From = from };

                p.ModelName = Text(node, "HashName", "ModelName", "Model");

                // NOT A FLOAT. Menyoo writes the hash as 0xe83b93b7, which a float parse
                // rejects outright -- and even in decimal a float has 24 bits of mantissa and
                // 3,896,612,791 comes back as 3,896,612,864. Every ped in the file would be
                // the wrong model, or more likely no model at all.
                p.ModelHash = Whole(node, 0, "ModelHash", "Hash", "ModelKey");

                if (string.IsNullOrEmpty(p.ModelName) && p.ModelHash == 0) return null;

                // A name that is only digits is a hash somebody stored as a name.
                if (!string.IsNullOrEmpty(p.ModelName) && IsAllDigits(p.ModelName))
                {
                    if (p.ModelHash == 0) p.ModelHash = Int(p.ModelName, 0);
                    p.ModelName = "";
                }

                // HASHNAME IS WHAT MENYOO SHOWS YOU, NOT WHAT THE GAME CALLS IT. The first real
                // file had ten peds named "Families Female", "Families CA Male" and so on --
                // labels off its own list with spaces in them. A name with a space in it is
                // never a model name, so it is kept only for the log and the hash does the work.
                if (p.ModelName.IndexOf(' ') >= 0) p.ModelName = "";

                p.At = new Vector3(Number(node, 0f, "X"), Number(node, 0f, "Y"), Number(node, 0f, "Z"));
                p.Pitch = Number(node, 0f, "Pitch", "RotX");
                p.Roll = Number(node, 0f, "Roll", "RotY");
                p.Yaw = Number(node, 0f, "Yaw", "Heading", "RotZ");

                if (p.At.X == 0f && p.At.Y == 0f && p.At.Z == 0f) return null;

                p.Frozen = Flag(node, true, "FrozenPos", "Frozen", "IsPositionFrozen");
                p.Visible = Flag(node, true, "IsVisible", "Visible");
                p.Invincible = Flag(node, false, "IsInvincible", "Invincible", "God");
                p.Gravity = Flag(node, true, "HasGravity", "Gravity");
                p.Opacity = Whole(node, 255, "OpacityLevel", "Alpha", "Opacity");

                p.Handle = Whole(node, 0, "InitialHandle", "Handle", "Id");

                p.Weapon = Text(node, "CurrentWeapon", "Weapon", "WeaponHash", "PedWeapon");
                p.WeaponHash = AsHash(p.Weapon, "WEAPON_");

                p.Relationship = Text(node, "RelationshipGroup", "Relationship", "RelGroup");
                p.GroupHash = AsHash(p.Relationship, "");

                p.Scenario = Text(node, "Scenario", "ScenarioName", "ActiveScenario");
                p.Health = Whole(node, -1, "Health");
                p.Armour = Whole(node, -1, "Armour", "Armor");

                // A scenario that is off in the file is not a scenario. Menyoo keeps the last
                // one picked in the file with a flag beside it saying whether it is running,
                // and every ped in the first real file had that flag false.
                if (!Flag(node, true, "ScenarioActive", "IsScenarioActive")) p.Scenario = "";

                Anim(node, p);

                Clothes(node, p);
                Attach(node, p);

                // What it is, from the model rather than the Type number -- see the note above.
                var type = (int)Number(node, 0f, "Type", "EntityType");
                var model = p.Model;

                if (model.IsPed) p.What = Kind.Ped;
                else if (model.IsVehicle) p.What = Kind.Vehicle;
                else if (model.IsProp || model.IsValid) p.What = Kind.Prop;
                else p.What = type == 1 ? Kind.Ped : type == 2 ? Kind.Vehicle : Kind.Prop;

                return p;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// What the ped is doing, out of Menyoo's Animations block.
        ///
        /// Read from inside the block rather than by tag name across the placement, because the
        /// tags in there are Dict and NAME -- and Name is the most common tag in the file.
        /// Menyoo saves a list and plays the first; so does this.
        /// </summary>
        private static void Anim(XmlNode node, Placed p)
        {
            var block = Find(node, "Animation");

            if (block == null)
            {
                // Older hands wrote it flat on the placement.
                p.AnimDict = Text(node, "AnimDict", "AnimationDict", "AnimDictionary");
                p.AnimClip = Text(node, "AnimName", "AnimationName", "Clip");

                if (!Flag(node, true, "AnimActive", "IsAnimActive")) { p.AnimDict = ""; p.AnimClip = ""; }
                return;
            }

            p.AnimDict = Text(block, "Dict", "AnimDict", "Dictionary");
            p.AnimClip = Text(block, "Name", "AnimName", "Clip");
            p.AnimFlag = Whole(block, 1, "Flag", "Flags");

            // A one-shot in a scene is a man who does a thing once and then stands empty for
            // the rest of the evening. Anything that is not already looping is made to loop.
            if ((p.AnimFlag & 1) == 0) p.AnimFlag |= 1;

            if (!Flag(node, true, "AnimActive", "IsAnimActive")) { p.AnimDict = ""; p.AnimClip = ""; }
        }

        /// <summary>
        /// What the ped has on.
        ///
        /// TWO SHAPES. Menyoo writes a bag per kind -- PedComps and PedProps -- holding one
        /// element per slot NAMED for the slot, "_4", with the drawable and the texture in it
        /// as "1,0". Other hands write a row per slot with the number on an attribute. Both are
        /// read; anything whose numbers do not make sense is dropped rather than applied.
        /// </summary>
        private static void Clothes(XmlNode node, Placed p)
        {
            foreach (XmlNode bag in node.SelectNodes(".//*"))
            {
                var comps = bag.Name.Equals("PedComps", StringComparison.OrdinalIgnoreCase) ||
                            bag.Name.Equals("Components", StringComparison.OrdinalIgnoreCase) ||
                            bag.Name.Equals("ClothingVariations", StringComparison.OrdinalIgnoreCase);
                var props = bag.Name.Equals("PedProps", StringComparison.OrdinalIgnoreCase) ||
                            bag.Name.Equals("ClothingProps", StringComparison.OrdinalIgnoreCase);

                if (!comps && !props) continue;

                foreach (XmlNode row in bag.ChildNodes)
                {
                    var tag = row.Name;
                    if (tag.Length < 2 || tag[0] != '_') continue;

                    var slot = Int(tag.Substring(1), -1);
                    if (slot < 0 || slot > 11) continue;

                    var parts = (row.InnerText ?? "").Split(',');
                    if (parts.Length < 1) continue;

                    var drawable = Int(parts[0].Trim(), -1);
                    var texture = parts.Length > 1 ? Int(parts[1].Trim(), 0) : 0;

                    if (drawable < -1 || drawable > 511) continue;

                    // -1 on a component means "leave it alone"; on a prop it means bare.
                    if (comps && drawable < 0) continue;

                    (comps ? p.Components : p.Props).Add(new[] { slot, drawable, texture });
                }
            }

            foreach (XmlNode child in node.SelectNodes(".//*"))
            {
                var tag = child.Name;
                var isComp = tag.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             tag.Equals("Comp", StringComparison.OrdinalIgnoreCase);
                var isProp = !isComp && tag.Equals("Prop", StringComparison.OrdinalIgnoreCase);

                if (!isComp && !isProp) continue;

                var slot = (int)Attribute(child, -1f, "id", "slot", "index", "componentId");
                if (slot < 0) slot = Whole(child, -1, "Id", "Slot", "ComponentId");
                if (slot < 0 || slot > 11) continue;

                var drawable = Whole(child, -1, "Index", "Drawable", "DrawableId", "Variation");
                var texture = Whole(child, 0, "Texture", "TextureId", "Textures");

                if (drawable < -1 || drawable > 511) continue;

                (isComp ? p.Components : p.Props).Add(new[] { slot, drawable, texture });
            }
        }

        private static void Attach(XmlNode node, Placed p)
        {
            XmlNode att = null;

            foreach (XmlNode child in node.SelectNodes(".//*"))
            {
                if (child.Name.IndexOf("Attachment", StringComparison.OrdinalIgnoreCase) < 0) continue;
                att = child;
                break;
            }

            if (att == null) return;

            var on = Attribute(att, 0f, "isAttached", "attached") > 0.5f ||
                     Flag(att, false, "IsAttached", "Attached");

            if (!on) return;

            p.AttachedTo = Whole(att, 0, "AttachedTo", "AttachedToHandle", "Parent");
            if (p.AttachedTo == 0) return;

            p.Attached = true;
            p.Bone = Whole(att, -1, "BoneIndex", "Bone");
            p.AttachOffset = new Vector3(Number(att, 0f, "X", "OffsetX"),
                                         Number(att, 0f, "Y", "OffsetY"),
                                         Number(att, 0f, "Z", "OffsetZ"));
            p.AttachRotation = new Vector3(Number(att, 0f, "Pitch", "RotX"),
                                           Number(att, 0f, "Roll", "RotY"),
                                           Number(att, 0f, "Yaw", "RotZ"));
        }

        // ---- tolerant lookups ----------------------------------------------------

        /// <summary>The first descendant with any of these names, case-insensitively, nearest first.</summary>
        private static XmlNode Find(XmlNode node, params string[] names)
        {
            if (node == null) return null;

            foreach (var want in names)
            {
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.Name.Equals(want, StringComparison.OrdinalIgnoreCase)) return child;
                }
            }

            foreach (var want in names)
            {
                var deep = node.SelectNodes(".//*");
                if (deep == null) continue;

                foreach (XmlNode child in deep)
                {
                    if (child.Name.Equals(want, StringComparison.OrdinalIgnoreCase)) return child;
                }
            }

            return null;
        }

        private static string Text(XmlNode node, params string[] names)
        {
            var found = Find(node, names);
            var value = found == null ? "" : (found.InnerText ?? "").Trim();

            // Menyoo writes an empty element rather than leaving the field out.
            return value == "0" && names.Length > 0 && names[0].IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0
                ? ""
                : value;
        }

        private static float Number(XmlNode node, float fallback, params string[] names)
        {
            var found = Find(node, names);
            if (found == null) return fallback;

            float value;
            return float.TryParse((found.InnerText ?? "").Trim(), NumberStyles.Float,
                                  CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static bool Flag(XmlNode node, bool fallback, params string[] names)
        {
            var found = Find(node, names);
            if (found == null) return fallback;

            var text = (found.InnerText ?? "").Trim();

            if (text.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

            float value;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value > 0.5f
                : fallback;
        }

        private static float Attribute(XmlNode node, float fallback, params string[] names)
        {
            if (node == null || node.Attributes == null) return fallback;

            foreach (var want in names)
            {
                foreach (XmlAttribute a in node.Attributes)
                {
                    if (!a.Name.Equals(want, StringComparison.OrdinalIgnoreCase)) continue;

                    var text = (a.Value ?? "").Trim();

                    if (text.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1f;
                    if (text.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0f;

                    float value;
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
                }
            }

            return fallback;
        }

        /// <summary>
        /// A whole number out of the file, in hex or decimal, signed or not.
        ///
        /// This is the one that matters. Every hash in a Menyoo file is written as 0x1234abcd
        /// and every one of them is bigger than a float can count in ones.
        /// </summary>
        private static int Whole(XmlNode node, int fallback, params string[] names)
        {
            var found = Find(node, names);
            return found == null ? fallback : Int((found.InnerText ?? "").Trim(), fallback);
        }

        /// <summary>The same, from a string. 0x-prefixed is hex; everything else is decimal.</summary>
        private static int Int(string text, int fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;

            text = text.Trim();

            var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                      text.StartsWith("&h", StringComparison.OrdinalIgnoreCase);

            uint asUint;
            if (hex && uint.TryParse(text.Substring(2), NumberStyles.HexNumber,
                                     CultureInfo.InvariantCulture, out asUint))
            {
                return unchecked((int)asUint);
            }

            int asInt;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out asInt)) return asInt;

            // A hash above int.MaxValue written in decimal.
            if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out asUint))
            {
                return unchecked((int)asUint);
            }

            // "1.0" where a whole number was meant.
            float asFloat;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out asFloat)
                ? (int)asFloat
                : fallback;
        }

        /// <summary>
        /// A hash out of whatever the file wrote: 0x1234abcd, a plain number, or a name to be
        /// hashed. <paramref name="prefix"/> is put on the front of a bare name -- CARBINERIFLE
        /// becomes WEAPON_CARBINERIFLE -- because some writers save it without.
        /// </summary>
        private static uint AsHash(string text, string prefix)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            text = text.Trim();

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                uint hex;
                return uint.TryParse(text.Substring(2), NumberStyles.HexNumber,
                                     CultureInfo.InvariantCulture, out hex)
                    ? hex
                    : 0;
            }

            if (IsAllDigits(text))
            {
                uint plain;
                if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out plain)) return plain;
            }

            if (!string.IsNullOrEmpty(prefix) &&
                text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) < 0)
            {
                text = prefix + text;
            }

            return Names.Joaat(text);
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;

            for (var i = 0; i < s.Length; i++)
            {
                if (i == 0 && s[i] == '-') continue;
                if (!char.IsDigit(s[i])) return false;
            }

            return true;
        }

        // ================================================================= writing

        /// <summary>
        /// The same format back out, so a capture opens in Menyoo like anything else it saved.
        ///
        /// The point of writing it rather than only reading it: what is stood in the world
        /// right now is the thing somebody spent an hour arranging, and until it is on disk it
        /// is one crash away from never having happened.
        /// </summary>
        public static bool Write(string path, IEnumerable<Placed> items, string note)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<SpoonerPlacements>\n");
                sb.Append("  <Note>").Append(Escape(note)).Append("</Note>\n");
                sb.Append("  <AudioFile />\n  <ClearWorld>0</ClearWorld>\n  <ClearDatabase>false</ClearDatabase>\n");
                sb.Append("  <IPLsToLoad />\n  <InteriorsToEnable />\n");
                sb.Append("  <ReferenceCoords>\n    <X>0</X>\n    <Y>0</Y>\n    <Z>0</Z>\n  </ReferenceCoords>\n");

                var n = 0;

                foreach (var p in items)
                {
                    if (p == null) continue;
                    n++;

                    sb.Append("  <Placement>\n");
                    sb.Append("    <ModelHash>0x").Append(unchecked((uint)p.ModelHash).ToString("x8")).Append("</ModelHash>\n");
                    sb.Append("    <Type>").Append(p.What == Kind.Ped ? 1 : p.What == Kind.Vehicle ? 2 : 3).Append("</Type>\n");
                    sb.Append("    <DynamicC>").Append(p.Frozen ? "false" : "true").Append("</DynamicC>\n");
                    sb.Append("    <FrozenPos>").Append(p.Frozen ? "true" : "false").Append("</FrozenPos>\n");
                    sb.Append("    <HashName>").Append(Escape(p.ModelName)).Append("</HashName>\n");
                    sb.Append("    <InitialHandle>").Append(p.Handle).Append("</InitialHandle>\n");
                    sb.Append("    <OpacityLevel>").Append(p.Opacity).Append("</OpacityLevel>\n");
                    sb.Append("    <IsVisible>").Append(p.Visible ? "true" : "false").Append("</IsVisible>\n");
                    sb.Append("    <HasGravity>").Append(p.Gravity ? "true" : "false").Append("</HasGravity>\n");
                    sb.Append("    <IsInvincible>").Append(p.Invincible ? "true" : "false").Append("</IsInvincible>\n");

                    sb.Append("    <PositionRotation>\n");
                    sb.Append("      <X>").Append(F(p.At.X)).Append("</X>\n");
                    sb.Append("      <Y>").Append(F(p.At.Y)).Append("</Y>\n");
                    sb.Append("      <Z>").Append(F(p.At.Z)).Append("</Z>\n");
                    sb.Append("      <Pitch>").Append(F(p.Pitch)).Append("</Pitch>\n");
                    sb.Append("      <Roll>").Append(F(p.Roll)).Append("</Roll>\n");
                    sb.Append("      <Yaw>").Append(F(p.Yaw)).Append("</Yaw>\n");
                    sb.Append("    </PositionRotation>\n");

                    sb.Append("    <Attachment isAttached=\"false\" />\n");

                    if (p.What == Kind.Ped)
                    {
                        sb.Append("    <PedProperties>\n");
                        sb.Append("      <CurrentWeapon>").Append(Escape(p.Weapon)).Append("</CurrentWeapon>\n");
                        sb.Append("      <ScenarioActive>").Append(string.IsNullOrEmpty(p.Scenario) ? "false" : "true").Append("</ScenarioActive>\n");
                        sb.Append("      <Scenario>").Append(Escape(p.Scenario)).Append("</Scenario>\n");
                        sb.Append("      <RelationshipGroup>").Append(Escape(p.Relationship)).Append("</RelationshipGroup>\n");
                        if (p.Health >= 0) sb.Append("      <Health>").Append(p.Health).Append("</Health>\n");
                        if (p.Armour >= 0) sb.Append("      <Armour>").Append(p.Armour).Append("</Armour>\n");

                        if (p.Components.Count > 0)
                        {
                            sb.Append("      <ClothingVariations>\n");
                            foreach (var c in p.Components)
                            {
                                sb.Append("        <Component id=\"").Append(c[0]).Append("\">\n");
                                sb.Append("          <Index>").Append(c[1]).Append("</Index>\n");
                                sb.Append("          <Texture>").Append(c[2]).Append("</Texture>\n");
                                sb.Append("        </Component>\n");
                            }
                            sb.Append("      </ClothingVariations>\n");
                        }

                        if (p.Props.Count > 0)
                        {
                            sb.Append("      <ClothingProps>\n");
                            foreach (var c in p.Props)
                            {
                                sb.Append("        <Prop id=\"").Append(c[0]).Append("\">\n");
                                sb.Append("          <Index>").Append(c[1]).Append("</Index>\n");
                                sb.Append("          <Texture>").Append(c[2]).Append("</Texture>\n");
                                sb.Append("        </Prop>\n");
                            }
                            sb.Append("      </ClothingProps>\n");
                        }

                        sb.Append("    </PedProperties>\n");
                    }

                    sb.Append("  </Placement>\n");
                }

                sb.Append("</SpoonerPlacements>\n");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Log.Info("Wrote " + n + " placement(s) to " + path);

                return n > 0;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not write the placements: " + ex.Message);
                return false;
            }
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        // ================================================================= capturing

        /// <summary>
        /// Everything somebody put around here, read back off the world.
        ///
        /// ONLY MISSION ENTITIES. The spooner marks what it places as a mission entity so the
        /// game will not clean it up, and so does every other script including this one -- but
        /// the ambient crowd and the map's own furniture are not marked, and they are the
        /// thousand things we must not sweep up. It is not a perfect sieve and it is not
        /// pretending to be: what comes out is written down and logged line by line, so
        /// anything that should not be in there can be deleted before it is loaded.
        /// </summary>
        public static List<Placed> Around(Vector3 centre, float radius)
        {
            var found = new List<Placed>();

            try
            {
                var me = Game.Player.Character;
                var mine = me != null && me.Exists() ? me.Handle : 0;
                var myCar = me != null && me.Exists() && me.IsInVehicle() ? me.CurrentVehicle.Handle : 0;

                foreach (var ped in World.GetNearbyPeds(centre, radius))
                {
                    if (ped == null || !ped.Exists() || ped.Handle == mine) continue;
                    if (!Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, ped.Handle)) continue;

                    var p = Basics(ped, Kind.Ped);

                    try
                    {
                        // BY NUMBER. The game gives back a hash and there is no call that
                        // turns one into WEAPON_PISTOL; the loader takes either, so the number
                        // goes in the file and the name goes in when the bench knows it.
                        var weapon = Function.Call<uint>(Hash.GET_SELECTED_PED_WEAPON, ped.Handle);
                        var named = Names.Of(weapon);
                        p.Weapon = string.IsNullOrEmpty(named) ? weapon.ToString(CultureInfo.InvariantCulture) : named;
                        p.Health = ped.Health;
                        p.Armour = ped.Armor;

                        foreach (var slot in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 })
                        {
                            var d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, slot);
                            var t = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped.Handle, slot);
                            if (d > 0 || t > 0) p.Components.Add(new[] { slot, d, t });
                        }

                        foreach (var slot in new[] { 0, 1, 2, 6, 7 })
                        {
                            var d = Function.Call<int>(Hash.GET_PED_PROP_INDEX, ped.Handle, slot);
                            if (d < 0) continue;
                            var t = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, ped.Handle, slot);
                            p.Props.Add(new[] { slot, d, t });
                        }
                    }
                    catch
                    {
                        // What could be read is enough.
                    }

                    found.Add(p);
                }

                foreach (var prop in World.GetNearbyProps(centre, radius))
                {
                    if (prop == null || !prop.Exists()) continue;
                    if (!Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, prop.Handle)) continue;

                    found.Add(Basics(prop, Kind.Prop));
                }

                foreach (var car in World.GetNearbyVehicles(centre, radius))
                {
                    if (car == null || !car.Exists() || car.Handle == myCar) continue;
                    if (!Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, car.Handle)) continue;

                    found.Add(Basics(car, Kind.Vehicle));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read what is around him: " + ex.Message);
            }

            return found;
        }

        private static Placed Basics(Entity e, Kind what)
        {
            var rot = e.Rotation;

            return new Placed
            {
                What = what,
                ModelName = Names.Of(e.Model),
                ModelHash = e.Model.Hash,
                Handle = e.Handle,
                At = e.Position,
                Pitch = rot.X,
                Roll = rot.Y,
                Yaw = rot.Z,
                Frozen = what != Kind.Ped,
                Visible = e.IsVisible
            };
        }
    }
}
