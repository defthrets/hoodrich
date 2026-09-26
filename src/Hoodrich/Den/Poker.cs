using System;
using System.Collections.Generic;
using GTA;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Den
{
    /// <summary>
    /// Three Card Poker, at the den's poker table, one seat -- played the way the Diamond plays it.
    ///
    /// THE DIAMOND'S RULES. An ante, and a Pair Plus beside it if you want one. Three cards
    /// each. You play -- a second bet the size of the ante -- or fold, and lose the ante. The
    /// dealer needs a queen high to qualify: without one the ante pays even money and the play
    /// bet comes back; with one, the better hand takes both at even money and a tie pushes. An
    /// ante bonus on a straight or better whatever she holds, and the Pair Plus pays on your
    /// hand alone, fold or play. Three cards rank their own way: straight flush, three of a
    /// kind, straight, flush, pair, high card -- a straight is harder to make than a flush.
    ///
    /// Her hands are the casino's own poker clips -- the shuffle, the deal to seat one and to
    /// herself, the reveals, the sweep -- queued so each waits for the one before. The cards
    /// are on the card at the bottom of the screen: ScriptHookVDotNet 3.6 has no native for a
    /// card's face. Michael put a poker table in the den on 2026-09-26 and asked for the
    /// gambling to be "just like vanilla".
    /// </summary>
    internal sealed class Poker
    {
        private enum Stage { Betting, Dealing, Deciding, Revealing, Result, Done }

        private readonly Dealer _dealer;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;

        private Stage _stage = Stage.Betting;

        /// <summary>Which bet the arrows are on: the ante, or the Pair Plus.</summary>
        private int _field;
        private int _ante;
        private int _plus;

        private int _anteBet;
        private int _plusBet;
        private int _playBet;
        private bool _played;
        private int _choice;
        private int _at;
        private string _said = "";
        private bool _hersUp;
        private int _net;

        private readonly List<int> _deck = new List<int>();
        private readonly List<int> _mine = new List<int>();
        private readonly List<int> _hers = new List<int>();

        /// <summary>What she has been asked to do, in order, and whose cards come with it: 1 yours, 2 hers, 3 hers face up.</summary>
        private readonly Queue<string> _acts = new Queue<string>();
        private readonly Queue<int> _dealTo = new Queue<int>();

        private const int ResultMs = 5000;

        private const int HighCard = 0;
        private const int Pair = 1;
        private const int Flush = 2;
        private const int Straight = 3;
        private const int Trips = 4;
        private const int StraightFlush = 5;

        private static readonly string[] HandNames = { "high card", "a pair", "a flush", "a straight", "three of a kind", "a straight flush" };
        private static readonly string[] Ranks = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        private static readonly string[] Suits = { "S", "H", "D", "C" };

        public Poker(Dealer dealer, int minBet, int maxBet, Random rng)
        {
            _dealer = dealer;
            _rng = rng;
            _minBet = Math.Max(1, minBet);
            _maxBet = Math.Max(_minBet, maxBet);
            _ante = _minBet;
            Log.Info("Den: sat down at the poker.");
        }

        public bool Finished => _stage == Stage.Done;

        public void Update()
        {
            Acts();

            switch (_stage)
            {
                case Stage.Betting: Betting(); break;
                case Stage.Dealing: if (Idle()) { _choice = 0; _stage = Stage.Deciding; } break;
                case Stage.Deciding: Deciding(); break;
                case Stage.Revealing: if (Idle()) Settle(); break;
                case Stage.Result: if (Game.GameTime >= _at && Idle()) _stage = Stage.Betting; break;
            }

            Draw();
        }

        /// <summary>Nothing queued and the dealer free: the moment the next thing can happen.</summary>
        private bool Idle()
        {
            return _acts.Count == 0 && !_dealer.Busy;
        }

        /// <summary>The next act starts the moment she is free, and its cards land with it.</summary>
        private void Acts()
        {
            if (_acts.Count == 0 || _dealer.Busy) return;

            var clip = _acts.Dequeue();
            var to = _dealTo.Count > 0 ? _dealTo.Dequeue() : 0;

            if (to == 1) for (var i = 0; i < 3; i++) _mine.Add(Draw1());
            else if (to == 2) for (var i = 0; i < 3; i++) _hers.Add(Draw1());
            else if (to == 3) _hersUp = true;

            _dealer.Act(Scene.PokerDealer, clip);
        }

        private void Ask(string clip, int dealTo = 0)
        {
            _acts.Enqueue(clip);
            _dealTo.Enqueue(dealTo);
        }

        // ---- the bets -----------------------------------------------------------------

        private void Betting()
        {
            if (Keys.Back) { Leave(); return; }

            if (Keys.Up || Keys.Down) { _field = 1 - _field; Click(); }

            var step = Keys.Right ? 1 : Keys.Left ? -1 : 0;

            if (step != 0)
            {
                Click();
                if (_field == 0) _ante = NextStake(_ante, step);
                else _plus = NextPlus(_plus, step);
            }

            if (!Keys.Select) return;

            var total = _ante + _plus;

            if (Game.Player.Money < total)
            {
                Say("You ain't got it.");
                return;
            }

            Game.Player.Money -= total;
            Click();

            _anteBet = _ante;
            _plusBet = _plus;
            _playBet = 0;
            _played = false;
            _said = "";
            _mine.Clear();
            _hers.Clear();
            _hersUp = false;

            Shuffle();

            // Shuffled, three to you, three to her -- the deal as the casino deals it.
            Ask("female_deck_shuffle");
            Ask("female_deck_deal_p01", 1);
            Ask("female_deck_deal_self", 2);

            _stage = Stage.Dealing;
        }

        /// <summary>The stakes step by the chips a den would have: the minimum, then multiples of it.</summary>
        private int NextStake(int stake, int step)
        {
            int[] mult = { 1, 2, 5, 10, 25, 50, 100 };
            var i = 0;

            for (var k = 0; k < mult.Length; k++)
            {
                if (_minBet * mult[k] <= stake) i = k;
            }

            i += step;
            if (i < 0) i = 0;
            if (i >= mult.Length) i = mult.Length - 1;

            return Math.Min(_maxBet, _minBet * mult[i]);
        }

        /// <summary>The Pair Plus: off, then the same chips as the ante.</summary>
        private int NextPlus(int plus, int step)
        {
            if (plus == 0) return step > 0 ? _minBet : 0;
            if (step < 0 && plus <= _minBet) return 0;
            return NextStake(plus, step);
        }

        // ---- the hand -----------------------------------------------------------------

        private void Deciding()
        {
            if (!Idle()) return;

            if (Keys.Left || Keys.Right) { _choice = 1 - _choice; Click(); }
            if (!Keys.Select) return;

            Click();

            if (_choice == 0)
            {
                if (Game.Player.Money < _anteBet)
                {
                    Say("You ain't got the play bet. Fold or find it.");
                    return;
                }

                Game.Player.Money -= _anteBet;
                _playBet = _anteBet;
                _played = true;

                // Hers over first, then yours: the reveal as the casino does it.
                Ask("female_reveal_self", 3);
                Ask("female_reveal_played_p01");
            }
            else
            {
                _played = false;
                Ask("female_reveal_folded_p01");
                Ask("female_reveal_self", 3);
            }

            _stage = Stage.Revealing;
        }

        private void Settle()
        {
            var me = Rank(_mine);
            var her = Rank(_hers);
            var back = 0;
            var how = new List<string>();

            // The Pair Plus is on your hand alone, played or folded.
            if (_plusBet > 0)
            {
                var pays = PlusPays(me.Cat);

                if (pays > 0)
                {
                    back += _plusBet * (pays + 1);
                    how.Add("pair plus " + pays + " to 1");
                }
                else
                {
                    how.Add("pair plus down");
                }
            }

            if (!_played)
            {
                how.Insert(0, "folded");
            }
            else
            {
                // The ante bonus, on a straight or better, whatever she holds.
                var bonus = AnteBonus(me.Cat);

                if (bonus > 0)
                {
                    back += _anteBet * bonus;
                    how.Add("ante bonus " + bonus + " to 1");
                }

                var qualifies = her.Cat >= Pair || her.Top >= 12;

                if (!qualifies)
                {
                    back += _anteBet * 2 + _playBet;
                    how.Insert(0, "she don't qualify");
                }
                else if (me.Score > her.Score)
                {
                    back += _anteBet * 2 + _playBet * 2;
                    how.Insert(0, HandNames[me.Cat] + " beats " + HandNames[her.Cat]);
                }
                else if (me.Score == her.Score)
                {
                    back += _anteBet + _playBet;
                    how.Insert(0, "push");
                }
                else
                {
                    how.Insert(0, HandNames[her.Cat] + " beats " + HandNames[me.Cat]);
                }
            }

            if (back > 0) Game.Player.Money += back;

            var staked = _anteBet + _plusBet + _playBet;
            _net = back - staked;

            var line = string.Join(", ", how);

            if (_net > 0)
            {
                _said = line + " -- you win $" + _net.ToString("N0");
                UI.Draw.PlaySound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
                _dealer.React(true);
            }
            else if (_net == 0)
            {
                _said = line + " -- stake back";
            }
            else
            {
                _said = line + " -- house takes $" + (-_net).ToString("N0");
                UI.Draw.PlaySound("LOSER", "HUD_AWARDS");
                _dealer.React(false);
            }

            Ask("female_cards_collect_p01");
            Ask("female_cards_collect_self");

            Log.Info("Den: poker, you " + Hand(_mine) + " (" + HandNames[me.Cat] + ") her " + Hand(_hers) + " (" +
                     HandNames[her.Cat] + "); ante $" + _anteBet.ToString("N0") + ", pair plus $" + _plusBet.ToString("N0") +
                     (_played ? ", played" : ", folded") + "; " + (_net >= 0 ? "+" : "-") + "$" + Math.Abs(_net).ToString("N0") + ".");

            _at = Game.GameTime + ResultMs;
            _stage = Stage.Result;
        }

        /// <summary>The Pair Plus table, to one.</summary>
        private static int PlusPays(int cat)
        {
            switch (cat)
            {
                case StraightFlush: return 40;
                case Trips: return 30;
                case Straight: return 6;
                case Flush: return 4;
                case Pair: return 1;
                default: return 0;
            }
        }

        /// <summary>The ante bonus table, to one.</summary>
        private static int AnteBonus(int cat)
        {
            switch (cat)
            {
                case StraightFlush: return 5;
                case Trips: return 4;
                case Straight: return 1;
                default: return 0;
            }
        }

        // ---- cards --------------------------------------------------------------------

        private struct Ranked
        {
            public int Cat;
            public int Score;
            public int Top;
        }

        /// <summary>
        /// A three-card hand, ranked. Aces high, and low only in A-2-3, which is the lowest
        /// straight. The score is the category and then the cards that break a tie, highest
        /// first, so two hands compare as two numbers.
        /// </summary>
        private static Ranked Rank(List<int> cards)
        {
            var v = new List<int>();
            var suits = new HashSet<int>();

            foreach (var c in cards)
            {
                var r = c % 13;
                v.Add(r == 0 ? 14 : r + 1);
                suits.Add(c / 13);
            }

            v.Sort();
            v.Reverse();

            var flush = suits.Count == 1;
            var wheel = v[0] == 14 && v[1] == 3 && v[2] == 2;
            var straight = wheel || (v[0] - v[1] == 1 && v[1] - v[2] == 1);
            var trips = v[0] == v[1] && v[1] == v[2];
            var pair = !trips && (v[0] == v[1] || v[1] == v[2]);

            int cat;
            int[] key;

            if (straight && flush) { cat = StraightFlush; key = new[] { wheel ? 3 : v[0] }; }
            else if (trips) { cat = Trips; key = new[] { v[0] }; }
            else if (straight) { cat = Straight; key = new[] { wheel ? 3 : v[0] }; }
            else if (flush) { cat = Flush; key = v.ToArray(); }
            else if (pair) { cat = Pair; key = new[] { v[1], v[0] == v[1] ? v[2] : v[0] }; }
            else { cat = HighCard; key = v.ToArray(); }

            var score = cat;
            for (var i = 0; i < 3; i++) score = score * 15 + (i < key.Length ? key[i] : 0);

            return new Ranked { Cat = cat, Score = score, Top = v[0] };
        }

        private void Shuffle()
        {
            _deck.Clear();
            for (var c = 0; c < 52; c++) _deck.Add(c);

            for (var i = _deck.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var t = _deck[i]; _deck[i] = _deck[j]; _deck[j] = t;
            }
        }

        private int Draw1()
        {
            if (_deck.Count == 0) Shuffle();

            var c = _deck[_deck.Count - 1];
            _deck.RemoveAt(_deck.Count - 1);
            return c;
        }

        private static string Card(int c)
        {
            return Ranks[c % 13] + Suits[c / 13];
        }

        private static string Hand(List<int> hand)
        {
            var parts = new List<string>();
            foreach (var c in hand) parts.Add(Card(c));
            return string.Join(" ", parts);
        }

        private string Hers()
        {
            if (_hers.Count == 0) return "";
            return _hersUp ? Hand(_hers) + "   (" + HandNames[Rank(_hers).Cat] + ")" : "?? ?? ??";
        }

        private string Mine()
        {
            if (_mine.Count == 0) return "";
            return Hand(_mine) + "   (" + HandNames[Rank(_mine).Cat] + ")";
        }

        // ---- out ----------------------------------------------------------------------

        private void Leave()
        {
            _stage = Stage.Done;
            Log.Info("Den: up from the poker.");
        }

        private void Say(string words)
        {
            _said = words;
            _at = Game.GameTime + 1800;
            UI.Draw.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private static void Click()
        {
            UI.Draw.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- the card -----------------------------------------------------------------

        private void Draw()
        {
            var lines = new List<string>();
            List<string> choices = null;
            var picked = -1;
            string hint;

            switch (_stage)
            {
                case Stage.Betting:
                    lines.Add(_said.Length > 0 && Game.GameTime < _at ? _said : "Ante up. Pair Plus pays on your hand alone.");
                    choices = new List<string>
                    {
                        "Ante $" + _ante.ToString("N0"),
                        _plus > 0 ? "Pair Plus $" + _plus.ToString("N0") : "Pair Plus off"
                    };
                    picked = _field;
                    hint = "Up/Down bet   Left/Right stake   Enter deal   Backspace leave";
                    break;

                case Stage.Deciding:
                    lines.Add("You  " + Mine());
                    lines.Add("Her  " + Hers());
                    if (_said.Length > 0 && Game.GameTime < _at) lines.Add(_said);
                    choices = new List<string> { "Play $" + _anteBet.ToString("N0"), "Fold" };
                    picked = _choice;
                    hint = "Left/Right choose   Enter";
                    break;

                case Stage.Result:
                    lines.Add("You  " + Mine());
                    lines.Add("Her  " + Hers());
                    lines.Add(_said);
                    hint = "";
                    Panel.Banner(_net > 0 ? "WIN" : _net == 0 ? "PUSH" : "HOUSE",
                                 _net > 0 ? Palette.Cash : _net == 0 ? Palette.Warn : Palette.Danger);
                    break;

                default:
                    lines.Add("You  " + Mine());
                    lines.Add("Her  " + Hers());
                    hint = "";
                    break;
            }

            var stake = _stage == Stage.Betting ? _ante + _plus : _anteBet + _plusBet + _playBet;
            Panel.Draw("Three Card Poker  $" + stake.ToString("N0"), lines, choices, picked, hint);
        }
    }
}
