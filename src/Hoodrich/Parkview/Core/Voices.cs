// GENERATED -- DO NOT EDIT. This is Parkview, copied in by tools/sync-parkview.py from
// C:\projects\parkview\src\Parkview\Core\Voices.cs. Change it there; the next build overwrites this.
using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Native;

namespace Hoodrich.Parkview.Core
{
    /// <summary>
    /// What each of the scene's people can say, and in which voice -- so they say all of it.
    ///
    /// WHY. Between them they said eight things. The lines were picked by hand from what all
    /// four Families voices have in common, and the plain set from what everybody else could
    /// manage -- four lines -- so the man on the corner said "hi" and "whatever" all evening
    /// and a local off the street said one of four and nothing else. Michael asked on
    /// 2026-09-24 for every one of them to be talking, and to say all the lines they can.
    ///
    /// HOW. data\voices.txt lists, for every voice in the game, the lines it really has of the
    /// kind somebody stood about talking says (made from menyooStuff\PedSpeechList.txt -- see
    /// the head of the file). Each ped is given one voice for good: the file's own choice when it
    /// names his model (a model with no voice, or one whose voice says too little), else the one
    /// named after his model -- its fuller ones, picked by his mark -- else a man's or a woman's
    /// default. It is set on him, so the game's own
    /// reactions come out in the same voice. What he says next is dealt off a shuffled deck of
    /// everything that voice has, so he gets through all of it before he says anything twice --
    /// and a man stood on a phone has a whole call, from hello to goodbye.
    ///
    /// Without the file, nobody is given a voice and the scene falls back to its own short lists.
    /// </summary>
    internal static class Voices
    {
        public enum Kind
        {
            /// <summary>Anything at all: said to nobody, or to whoever is stood there.</summary>
            Idle,

            /// <summary>Something to start with -- the one who walked over, in a chat.</summary>
            State,

            /// <summary>An answer -- the other one, in a chat.</summary>
            Reply,

            /// <summary>What goes with a sign being thrown.</summary>
            Gang
        }

        // THE SAME LISTS THE FILE WAS MADE WITH. A line the file has that is on none of these
        // is not said; a line on these that a voice does not have is not in the file for it.
        private static readonly HashSet<string> States = new HashSet<string>
        {
            "CHAT_STATE", "CHAT_ACROSS_STREET_STATE", "GENERIC_HOWS_IT_GOING", "HOWS_IT_GOING_GENERIC", "GENERIC_HI",
            "GREET_ACROSS_STREET", "GREET_ACROSS_STREET_FEMALE", "KIFFLOM_GREET", "PED_RANT", "PED_RANT_01", "NICE_CAR",
            "GUN_COOL", "SEE_WEIRDO", "STEPPED_IN_SHIT", "GENERIC_BUY", "LOOKING_AT_PHONE", "GENERIC_CURSE_MED",
            "GENERIC_CURSE_HIGH", "SEE_FRANKLIN"
        };

        private static readonly HashSet<string> Replies = new HashSet<string>
        {
            "CHAT_RESP", "CHAT_ACROSS_STREET_RESP", "AGREE_ACROSS_STREET", "GENERIC_YES", "GENERIC_NO", "GENERIC_WHATEVER",
            "GENERIC_THANKS", "GENERIC_CHEER", "WON_DISPUTE", "APOLOGY_NO_TROUBLE", "GENERIC_BYE", "GOODBYE_ACROSS_STREET",
            "GOODBYE_ACROSS_STREET_FEMALE"
        };

        private static readonly HashSet<string> Gangs = new HashSet<string>
        {
            "SHOUT_THREATEN_GANG", "SHOUT_THREATEN_PED", "SHOUT_INSULT", "CHALLENGE_THREATEN", "PROVOKE_GENERIC",
            "PROVOKE_STARING", "PROVOKE_BAR", "PROVOKE_TRESPASS", "GENERIC_INSULT_MED", "GENERIC_INSULT_HIGH"
        };

        /// <summary>A phone call, in order: PHONE_CONV{n}_ and then these.</summary>
        public static readonly string[] CallParts = { "INTRO", "CHAT1", "CHAT2", "CHAT3", "OUTRO" };

        /// <summary>The scenarios a man is on the phone in.</summary>
        private static readonly string[] Phones =
        {
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_STAND_MOBILE_UPRIGHT", "WORLD_HUMAN_STAND_MOBILE_UPRIGHT_CLUBHOUSE"
        };

        internal static readonly Random Dice = new Random();

        private static bool _read;
        private static readonly Dictionary<string, string[]> _lines = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A model's own voices, by model hash -- off the voices' names, not the file's labels.</summary>
        private static readonly Dictionary<uint, List<string>> _byModel = new Dictionary<uint, List<string>>();

        /// <summary>A voice for a model with none of its own, by model hash.</summary>
        private static readonly Dictionary<uint, string> _given = new Dictionary<uint, string>();

        private static string _man, _woman;

        private static void Read()
        {
            if (_read) return;
            _read = true;

            var path = Path.Combine(Paths.Data, "voices.txt");

            try
            {
                if (!File.Exists(path))
                {
                    Log.Info("Voices: no voices.txt in " + Paths.Data + "; the scene's people use its own short lists.");
                    return;
                }

                var section = "";

                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        continue;
                    }

                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line.Substring(0, eq).Trim();
                    var value = line.Substring(eq + 1).Trim();

                    if (section == "models")
                    {
                        if (key.Equals("male", StringComparison.OrdinalIgnoreCase)) _man = value;
                        else if (key.Equals("female", StringComparison.OrdinalIgnoreCase)) _woman = value;
                        else _given[Names.Joaat(key)] = value;
                        continue;
                    }

                    if (section != "voices") continue;

                    var said = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    for (var i = 0; i < said.Length; i++) said[i] = said[i].Trim();

                    _lines[key] = said;

                    var model = ModelOf(key);
                    if (model == null) continue;

                    List<string> list;
                    var hash = Names.Joaat(model);
                    if (!_byModel.TryGetValue(hash, out list)) _byModel[hash] = list = new List<string>();
                    list.Add(key);
                }

                // THE FULLER VOICES, WHEN A MODEL HAS THEM. Some models carry a "mini" voice with
                // half the lines of their full ones -- a Families man could land on sixteen things
                // to say while the man next to him had twenty-seven. Whoever can say the most
                // should, so a model's thin voices are only used when it has nothing better.
                foreach (var list in _byModel.Values)
                {
                    list.Sort(StringComparer.Ordinal);

                    var rich = new List<string>();
                    foreach (var v in list)
                    {
                        if (Talkable(_lines[v]) >= RichVoice) rich.Add(v);
                    }

                    if (rich.Count > 0 && rich.Count < list.Count)
                    {
                        list.Clear();
                        list.AddRange(rich);
                    }
                }

                Log.Info("Voices: " + _lines.Count + " voices read; everybody in the scenes says everything theirs can.");
            }
            catch (Exception ex)
            {
                _lines.Clear();
                Log.Warn("Voices: could not read voices.txt: " + ex.Message);
            }
        }

        /// <summary>A voice with at least this many things to say, calls aside, counts as a full one.</summary>
        private const int RichVoice = 20;

        /// <summary>How many different things a voice can say, calls aside.</summary>
        private static int Talkable(string[] lines)
        {
            var n = 0;
            foreach (var l in lines)
            {
                if (!l.StartsWith("PHONE_", StringComparison.Ordinal)) n++;
            }

            return n;
        }

        /// <summary>
        /// The model a voice is named after: G_M_Y_FAMCA_01_BLACK_FULL_01 is g_m_y_famca_01's --
        /// everything before the _FULL_ or _MINI_ and the word for who he is in front of it.
        /// Null for a voice that is nobody's in particular.
        /// </summary>
        private static string ModelOf(string voice)
        {
            var at = voice.LastIndexOf("_FULL_", StringComparison.Ordinal);
            if (at < 0) at = voice.LastIndexOf("_MINI_", StringComparison.Ordinal);
            if (at <= 0) return null;

            var head = voice.Substring(0, at);
            var cut = head.LastIndexOf('_');
            if (cut <= 0) return null;

            return head.Substring(0, cut).ToLowerInvariant();
        }

        /// <summary>
        /// The voice this man speaks in, for good, or null when there is no file. The same answer
        /// for the same man every session: <paramref name="steady"/> comes off his mark.
        /// </summary>
        public static string For(Ped ped, bool male, int steady)
        {
            Read();
            if (_lines.Count == 0 || ped == null) return null;

            try
            {
                var hash = unchecked((uint)ped.Model.Hash);

                // THE FILE'S OWN CHOICE FIRST: a model with no voice, or one whose own voice has
                // hardly anything to say -- the barber's has nine lines.
                string given;
                if (_given.TryGetValue(hash, out given) && _lines.ContainsKey(given)) return given;

                List<string> mine;
                if (_byModel.TryGetValue(hash, out mine) && mine.Count > 0) return mine[Math.Abs(steady) % mine.Count];

                var fallback = male ? _man : _woman;
                return fallback != null && _lines.ContainsKey(fallback) ? fallback : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Gives him the voice, so the game's own reactions come out in it as well as ours.</summary>
        public static void Dress(Ped ped, string voice)
        {
            if (ped == null || string.IsNullOrEmpty(voice)) return;

            try { Function.Call(Hash.SET_AMBIENT_VOICE_NAME, ped.Handle, voice); }
            catch { /* he keeps the one the game gave him; ours is named on every line anyway */ }
        }

        /// <summary>A man's own deck of everything his voice has, or null for a voice not in the file.</summary>
        public static Talk Talker(string voice)
        {
            Read();

            string[] lines;
            if (string.IsNullOrEmpty(voice) || !_lines.TryGetValue(voice, out lines)) return null;

            var state = new List<string>();
            var reply = new List<string>();
            var gang = new List<string>();
            var calls = new List<int>();

            foreach (var l in lines)
            {
                if (States.Contains(l)) state.Add(l);
                else if (Replies.Contains(l)) reply.Add(l);
                else if (Gangs.Contains(l)) gang.Add(l);
            }

            // Only whole calls: a call with no goodbye is a man hanging up on nobody.
            var have = new HashSet<string>(lines);

            for (var n = 1; n <= 8; n++)
            {
                var whole = true;
                foreach (var part in CallParts)
                {
                    if (have.Contains("PHONE_CONV" + n + "_" + part)) continue;
                    whole = false;
                    break;
                }

                if (whole) calls.Add(n);
            }

            return new Talk(voice, state, reply, gang, calls);
        }

        /// <summary>Whether he is stood on a phone right now.</summary>
        public static bool OnThePhone(Ped ped)
        {
            try
            {
                foreach (var s in Phones)
                {
                    if (Function.Call<bool>(Hash.IS_PED_USING_SCENARIO, ped.Handle, s)) return true;
                }
            }
            catch
            {
                // Not on one, then.
            }

            return false;
        }
    }

    /// <summary>One man's voice, and how far through everything it can say he has got.</summary>
    internal sealed class Talk
    {
        public readonly string Voice;

        private readonly Deck _idle, _state, _reply, _gang;
        private readonly List<int> _calls;

        /// <summary>The call he is on, and the next part of it; -1 when he is not on one.</summary>
        private int _call;
        private int _part = -1;

        public Talk(string voice, List<string> state, List<string> reply, List<string> gang, List<int> calls)
        {
            Voice = voice;

            var all = new List<string>(state);
            all.AddRange(reply);
            all.AddRange(gang);

            _idle = new Deck(all);
            _state = new Deck(state.Count > 0 ? state : all);
            _reply = new Deck(reply.Count > 0 ? reply : all);
            _gang = new Deck(gang.Count > 0 ? gang : state.Count > 0 ? state : all);
            _calls = calls;
        }

        /// <summary>The whole of it: how many different things he can say, calls aside.</summary>
        public int Count => _idle.Count;

        /// <summary>What he says next, of that kind. On the phone, anything is the next line of the call.</summary>
        public string Next(Voices.Kind kind, Ped ped)
        {
            if (kind == Voices.Kind.Idle && _calls.Count > 0 && Voices.OnThePhone(ped))
            {
                if (_part < 0 || _part >= Voices.CallParts.Length)
                {
                    _call = _calls[Voices.Dice.Next(_calls.Count)];
                    _part = 0;
                }

                return "PHONE_CONV" + _call + "_" + Voices.CallParts[_part++];
            }

            // Off the phone mid-call: that call is over.
            if (kind == Voices.Kind.Idle) _part = -1;

            switch (kind)
            {
                case Voices.Kind.State: return _state.Next();
                case Voices.Kind.Reply: return _reply.Next();
                case Voices.Kind.Gang: return _gang.Next();
                default: return _idle.Next();
            }
        }
    }

    /// <summary>
    /// A shuffled deck: every card once, in an order nobody can hear the pattern of, and then
    /// shuffled again -- never the last card of one pass as the first of the next.
    /// </summary>
    internal sealed class Deck
    {
        private readonly string[] _cards;
        private readonly int[] _order;
        private int _at;

        public Deck(List<string> cards)
        {
            _cards = cards.ToArray();
            _order = new int[_cards.Length];
            for (var i = 0; i < _order.Length; i++) _order[i] = i;

            Shuffle(-1);
        }

        public int Count => _cards.Length;

        public string Next()
        {
            if (_cards.Length == 0) return null;

            if (_at >= _order.Length) Shuffle(_order[_order.Length - 1]);

            return _cards[_order[_at++]];
        }

        private void Shuffle(int last)
        {
            for (var i = _order.Length - 1; i > 0; i--)
            {
                var j = Voices.Dice.Next(i + 1);
                var t = _order[i];
                _order[i] = _order[j];
                _order[j] = t;
            }

            if (_order.Length > 1 && _order[0] == last)
            {
                var t = _order[0];
                _order[0] = _order[_order.Length - 1];
                _order[_order.Length - 1] = t;
            }

            _at = 0;
        }
    }
}
