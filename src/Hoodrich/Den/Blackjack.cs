using System;
using System.Collections.Generic;
using GTA;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Den
{
    /// <summary>
    /// Blackjack, at the den's table, one seat.
    ///
    /// The house rules of the Diamond, near enough: the dealer stands on every seventeen, a
    /// natural pays three to two, you can double down on your first two cards, and there is no
    /// split and no insurance -- a den has one dealer and one player and neither has time for
    /// side bets. One deck, shuffled every hand.
    ///
    /// THE DEALER'S HANDS ARE THE CASINO'S. Every card she deals, every hit, the turn of her
    /// hole card and the sweep of the table at the end are the clips the casino plays for the
    /// same moments, queued so each waits for the one before. What they are NOT is holding
    /// cards you can read: ScriptHookVDotNet 3.6 has no native for a card's face, so the cards
    /// are on the card at the bottom of the screen and her hands mime the deal. The chips on
    /// the felt are Michael's.
    /// </summary>
    internal sealed class Blackjack
    {
        private enum Stage { Betting, Dealing, Player, DealerTurn, Result, Done }

        private readonly Dealer _dealer;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;

        private Stage _stage = Stage.Betting;
        private int _stake;
        private int _bet;
        private bool _doubled;
        private int _choice;
        private int _at;
        private string _said = "";
        private bool _asked;

        private readonly List<int> _deck = new List<int>();
        private readonly List<int> _mine = new List<int>();
        private readonly List<int> _hers = new List<int>();
        private bool _holeUp;

        /// <summary>What she has been asked to do, in order. Each waits for the one before.</summary>
        private readonly Queue<KeyValuePair<string, string>> _acts = new Queue<KeyValuePair<string, string>>();

        /// <summary>The cards that go with the acts: which hand gets one when that act starts.</summary>
        private readonly Queue<int> _dealTo = new Queue<int>();

        private const int ResultMs = 4500;

        private static readonly string[] Ranks = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        private static readonly string[] Suits = { "S", "H", "D", "C" };

        public Blackjack(Dealer dealer, int minBet, int maxBet, Random rng)
        {
            _dealer = dealer;
            _rng = rng;
            _minBet = Math.Max(1, minBet);
            _maxBet = Math.Max(_minBet, maxBet);
            _stake = _minBet;

            Log.Info("Den: sat down at the blackjack.");
        }

        public bool Finished => _stage == Stage.Done;

        public void Update()
        {
            Acts();

            switch (_stage)
            {
                case Stage.Betting: Betting(); break;
                case Stage.Dealing: if (Idle()) Dealt(); break;
                case Stage.Player: Playing(); break;
                case Stage.DealerTurn: if (Idle()) HerTurn(); break;
                case Stage.Result: if (Game.GameTime >= _at && Idle()) NewHand(); break;
            }

            Draw();
        }

        /// <summary>Nothing queued and the dealer free: the moment the next thing can happen.</summary>
        private bool Idle()
        {
            return _acts.Count == 0 && !_dealer.Busy;
        }

        /// <summary>The next act starts the moment she is free, and its card lands with it.</summary>
        private void Acts()
        {
            if (_acts.Count == 0 || _dealer.Busy) return;

            var act = _acts.Dequeue();

            if (_dealTo.Count > 0)
            {
                var to = _dealTo.Dequeue();

                if (to == 1) _mine.Add(Draw1());
                else if (to == 2) _hers.Add(Draw1());
                else if (to == 3) _holeUp = true;
            }

            _dealer.Act(act.Key, act.Value);
        }

        private void Ask(string clip, int dealTo = 0)
        {
            _acts.Enqueue(new KeyValuePair<string, string>(Scene.BlackjackDealer, clip));
            _dealTo.Enqueue(dealTo);
        }

        // ---- the bet ------------------------------------------------------------------

        private void Betting()
        {
            if (!_asked)
            {
                _asked = true;
                _dealer.Pin(null);
                Ask("place_bet_request");
            }

            if (Keys.Back) { Leave(); return; }

            var step = Keys.Right ? 1 : Keys.Left ? -1 : 0;
            if (step != 0) { _stake = NextStake(_stake, step); Click(); }

            if (!Keys.Select) return;

            if (Game.Player.Money < _stake)
            {
                _said = "You ain't got it.";
                _at = Game.GameTime + 1500;
                UI.Draw.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            Game.Player.Money -= _stake;
            Click();

            _bet = _stake;
            _doubled = false;
            _said = "";
            _mine.Clear();
            _hers.Clear();
            _holeUp = false;
            Shuffle();

            // Two each, hers second face down: the deal as the casino deals it.
            Ask("deal_card_player_01", 1);
            Ask("deal_card_self", 2);
            Ask("deal_card_player_01", 1);
            Ask("deal_card_self", 2);

            _stage = Stage.Dealing;
        }

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

        // ---- the hand -----------------------------------------------------------------

        private void Dealt()
        {
            if (Total(_mine) == 21)
            {
                // A natural. She checks hers and it is settled either way.
                Ask("check_and_turn_card", 3);
                _stage = Stage.DealerTurn;
                return;
            }

            _choice = 0;
            _dealer.Pin("dealer_focus_player_01_idle");
            _stage = Stage.Player;
        }

        private void Playing()
        {
            if (!Idle()) return;

            var options = Options();

            if (Keys.Left) { _choice = (_choice + options.Length - 1) % options.Length; Click(); }
            if (Keys.Right) { _choice = (_choice + 1) % options.Length; Click(); }

            if (!Keys.Select) return;

            Click();

            switch (options[_choice])
            {
                case "Hit":
                    Ask("hit_card_player_01", 1);
                    if (Total(_mine) > 21) { /* judged on the next tick, once dealt */ }
                    break;

                case "Stand":
                    Stand();
                    break;

                case "Double":
                    if (Game.Player.Money < _bet) { _said = "You ain't got it."; _at = Game.GameTime + 1500; return; }
                    Game.Player.Money -= _bet;
                    _bet *= 2;
                    _doubled = true;
                    Ask("hit_card_player_01", 1);
                    Stand();
                    break;
            }
        }

        private string[] Options()
        {
            return _mine.Count == 2 && !_doubled && Game.Player.Money >= _bet
                ? new[] { "Hit", "Stand", "Double" }
                : new[] { "Hit", "Stand" };
        }

        private void Stand()
        {
            _dealer.Pin(null);
            Ask("turn_card", 3);
            _stage = Stage.DealerTurn;
        }

        /// <summary>Her turn, one card an act, until she stands or busts -- or you already have.</summary>
        private void HerTurn()
        {
            if (_mine.Count > 0 && Total(_mine) > 21)
            {
                Settle();
                return;
            }

            if (!_holeUp) { Ask("turn_card", 3); return; }

            if (Total(_mine) == 21 && _mine.Count == 2)
            {
                Settle();
                return;
            }

            if (Total(_hers) < 17)
            {
                Ask("deal_card_self", 2);
                return;
            }

            Settle();
        }

        private void Settle()
        {
            var me = Total(_mine);
            var her = Total(_hers);
            var natural = me == 21 && _mine.Count == 2;
            var herNatural = her == 21 && _hers.Count == 2;

            int back;
            string how;

            if (me > 21) { back = 0; how = "bust"; }
            else if (natural && !herNatural) { back = _bet + _bet * 3 / 2; how = "blackjack"; }
            else if (her > 21) { back = _bet * 2; how = "dealer busts"; }
            else if (me > her) { back = _bet * 2; how = me + " beats " + her; }
            else if (me == her) { back = _bet; how = "push"; }
            else { back = 0; how = her + " beats " + me; }

            if (back > 0) Game.Player.Money += back;

            var net = back - _bet;

            if (net > 0)
            {
                _said = how + " -- you win $" + net.ToString("N0");
                UI.Draw.PlaySound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
                _dealer.React(true);
            }
            else if (net == 0)
            {
                _said = how + " -- stake back";
                _acts.Enqueue(new KeyValuePair<string, string>(Scene.SharedDealer, "female_dealer_reaction_impartial_var01"));
                _dealTo.Enqueue(0);
            }
            else
            {
                _said = how + " -- house takes it";
                UI.Draw.PlaySound("LOSER", "HUD_AWARDS");
                _dealer.React(false);
            }

            Ask("retrieve_all_cards");

            Log.Info("Den: blackjack " + how + ", you " + Hand(_mine) + " (" + me + ") her " + Hand(_hers) + " (" + her + "); " +
                     "$" + _bet.ToString("N0") + (net > 0 ? " won $" + net.ToString("N0") : net == 0 ? " pushed" : " lost") + ".");

            _at = Game.GameTime + ResultMs;
            _stage = Stage.Result;
        }

        private void NewHand()
        {
            _asked = false;
            _stage = Stage.Betting;
        }

        // ---- cards --------------------------------------------------------------------

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

        private static int Total(List<int> hand)
        {
            var total = 0;
            var aces = 0;

            foreach (var c in hand)
            {
                var rank = c % 13;

                if (rank == 0) { aces++; total += 11; }
                else if (rank >= 9) total += 10;
                else total += rank + 1;
            }

            while (total > 21 && aces > 0) { total -= 10; aces--; }

            return total;
        }

        private static string Card(int c)
        {
            return Ranks[c % 13] + Suits[c / 13];
        }

        private string Hand(List<int> hand)
        {
            var parts = new List<string>();
            foreach (var c in hand) parts.Add(Card(c));
            return string.Join(" ", parts);
        }

        private string Hers()
        {
            if (_hers.Count == 0) return "";
            if (_holeUp || _hers.Count < 2) return Hand(_hers);

            // The hole card is down until she turns it.
            return Card(_hers[0]) + " ??";
        }

        // ---- out ----------------------------------------------------------------------

        private void Leave()
        {
            _dealer.Pin(null);
            _stage = Stage.Done;

            Log.Info("Den: up from the blackjack.");
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
                    lines.Add(_said.Length > 0 && Game.GameTime < _at ? _said : "Stake $" + _stake.ToString("N0"));
                    hint = "Left/Right stake   Enter deal   Backspace leave";
                    break;

                case Stage.Player:
                    lines.Add("You  " + Hand(_mine) + "   (" + Total(_mine) + ")");
                    lines.Add("Her  " + Hers());
                    choices = new List<string>(Options());
                    picked = _choice;
                    hint = "Left/Right choose   Enter";
                    break;

                case Stage.Result:
                    lines.Add("You  " + Hand(_mine) + "   (" + Total(_mine) + ")");
                    lines.Add("Her  " + Hand(_hers) + "   (" + Total(_hers) + ")");
                    lines.Add(_said);
                    hint = "";
                    Panel.Banner(_said.Contains("win") ? "WIN" : _said.Contains("push") ? "PUSH" : "HOUSE",
                                 _said.Contains("win") ? Palette.Cash : _said.Contains("push") ? Palette.Warn : Palette.Danger);
                    break;

                default:
                    lines.Add("You  " + Hand(_mine) + (_mine.Count > 0 ? "   (" + Total(_mine) + ")" : ""));
                    lines.Add("Her  " + Hers());
                    hint = "";
                    break;
            }

            Panel.Draw("Blackjack  $" + (_stage == Stage.Betting ? _stake : _bet).ToString("N0"), lines, choices, picked, hint);
        }
    }
}
