using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Den
{
    /// <summary>
    /// Blackjack, at the den's table, played the way the Diamond plays it.
    ///
    /// Michael asked on 2026-09-26 for the den's poker table to be blackjack instead, in the same
    /// spot, "the actual gambling feature, same graphics". So: you sit in one of the table's own
    /// chairs; your chips go down with the casino's reach for them; she deals from the shoe in the
    /// table, a card riding in her right hand through her deal clip for your seat and laid on the
    /// felt where the casino lays that card for that seat; her second card goes down face down;
    /// you tap for a card, wave to stand, or push out the double; she turns her card over with
    /// the casino's own turn and draws to seventeen; and she sweeps it all up. She says your
    /// count and hers as she goes, in a Diamond croupier's voice.
    ///
    /// WHERE THE CARDS GO. Every card spot for every seat -- seven deep -- and the turn each one
    /// sits at, the chip spots, the shoe, the cameras and the moments her hand closes and opens
    /// are the casino's own, out of a port of its decompiled blackjack script (DiamondBlackjack).
    /// The one guess is her own row, which that script estimated too.
    ///
    /// THE SEATS RUN BACKWARDS, as they do at the poker: the chair the table calls 4 is her
    /// player 1, and chair 1 her player 4. Her clips and the card spots go by her numbering.
    ///
    /// The house rules: she stands on every seventeen, a natural pays three to two, you can
    /// double on your first two cards. No split, no insurance. Four decks in the shoe.
    /// </summary>
    internal sealed class Blackjack
    {
        private enum Stage { Sitting, Betting, Chipping, Dealing, Deciding, Acting, DealerTurn, Result, Collecting, Standing, Done }

        private const string D = Scene.BlackjackDealerAll;
        private const string P = Scene.BlackjackPlayer;

        /// <summary>Her hand opening on a card: the moment it is let go of on the felt, or the chips leave yours.</summary>
        private const int LetGo = 585557868;

        /// <summary>Her hand closing on one: picked up to be turned, or swept up.</summary>
        private const int TakeHold = -1345695206;

        /// <summary>Her right hand's prop bone, where a card rides while she deals it.</summary>
        private const int RightHand = 28422;

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>The casino's card spots, by her seat number less one, seven deep.</summary>
        private static readonly Vector3[][] SeatCards =
        {
            new[] { V(0.5737f, 0.2376f, 0.948025f), V(0.562975f, 0.2523f, 0.94875f), V(0.553875f, 0.266325f, 0.94955f), V(0.5459f, 0.282075f, 0.9501f), V(0.536125f, 0.29645f, 0.95085f), V(0.524975f, 0.30975f, 0.9516f), V(0.515775f, 0.325325f, 0.95235f) },
            new[] { V(0.2325f, -0.1082f, 0.94805f), V(0.23645f, -0.0918f, 0.949f), V(0.2401f, -0.074475f, 0.950225f), V(0.244625f, -0.057675f, 0.951125f), V(0.249675f, -0.041475f, 0.95205f), V(0.257575f, -0.0256f, 0.9532f), V(0.2601f, -0.008175f, 0.954375f) },
            new[] { V(-0.2359f, -0.1091f, 0.9483f), V(-0.221025f, -0.100675f, 0.949f), V(-0.20625f, -0.092875f, 0.949725f), V(-0.193225f, -0.07985f, 0.950325f), V(-0.1776f, -0.072f, 0.951025f), V(-0.165f, -0.060025f, 0.951825f), V(-0.14895f, -0.05155f, 0.95255f) },
            new[] { V(-0.5765f, 0.2229f, 0.9482f), V(-0.558925f, 0.2197f, 0.949175f), V(-0.5425f, 0.213025f, 0.9499f), V(-0.525925f, 0.21105f, 0.95095f), V(-0.509475f, 0.20535f, 0.9519f), V(-0.491775f, 0.204075f, 0.952825f), V(-0.4752f, 0.197525f, 0.9543f) }
        };

        /// <summary>And the turn each card sits at, on the table's heading.</summary>
        private static readonly float[][] SeatTurns =
        {
            new[] { 69.12f, 67.8f, 66.6f, 70.44f, 70.84f, 67.88f, 69.56f },
            new[] { 22.11f, 22.32f, 20.8f, 19.8f, 19.44f, 26.28f, 22.68f },
            new[] { -21.43f, -20.16f, -16.92f, -23.4f, -21.24f, -23.76f, -19.44f },
            new[] { -67.03f, -69.12f, -64.44f, -67.68f, -63.72f, -68.4f, -64.44f }
        };

        /// <summary>The chip spots: the bet, and the double beside it.</summary>
        private static readonly Vector3[] BetSpot =
        {
            V(0.712625f, 0.170625f, 0.95f), V(0.278125f, -0.2571f, 0.95f), V(-0.30305f, -0.2464f, 0.95f), V(-0.72855f, 0.17345f, 0.95f)
        };

        private static readonly Vector3[] DoubleSpot =
        {
            V(0.6658f, 0.218375f, 0.95f), V(0.280375f, -0.190375f, 0.95f), V(-0.257975f, -0.19715f, 0.95f), V(-0.652825f, 0.177525f, 0.95f)
        };

        /// <summary>The shoe in the table the cards come out of, and how a card lies in it.</summary>
        private static readonly Vector3 Shoe = V(0.526f, 0.571f, 0.963f);

        /// <summary>Her own row, in front of her. Estimated -- the one spot here that is not the casino's own.</summary>
        private static Vector3 HerSpot(int n) => V(-0.061f + 0.1223f * n, 0.30f, 0.9501f);

        /// <summary>The casino's character cards, by our suits and ranks: vw_prop_vw_hrt_char_q_a and the rest.</summary>
        private static readonly string[] SuitModel = { "spd", "hrt", "dia", "club" };
        private static readonly string[] RankModel = { "a_a", "02a", "03a", "04a", "05a", "06a", "07a", "08a", "09a", "10a", "j_a", "q_a", "k_a" };

        private static readonly string[] Ranks = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        private static readonly string[] Suits = { "S", "H", "D", "C" };

        private readonly Entity _table;
        private readonly Dealer _dealer;
        private readonly Ped _me;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;
        private readonly Sitter _sitter;
        private readonly TableCam _cam = new TableCam();

        /// <summary>Your seat as she numbers it, 1 to 4, and as the casino's tables index it, 0 to 3.</summary>
        private readonly int _seatNo;
        private readonly int _i;

        private Stage _stage = Stage.Sitting;
        private int _step;
        private string _action = "";
        private int _at;
        private int _bet;
        private int _stake;
        private bool _doubled;
        private bool _inPlay;
        private bool _natural;
        private bool _focused;
        private int _hits;
        private int _net;
        private int _session;
        private string _said = "";
        private int _saidUntil;

        private readonly List<int> _shoe = new List<int>();
        private readonly List<int> _mine = new List<int>();
        private readonly List<int> _hers = new List<int>();
        private readonly List<Prop> _myCards = new List<Prop>();
        private readonly List<Prop> _herCards = new List<Prop>();

        private bool _revealing;
        private bool _holeHeld;
        private bool _holeUp;

        private Prop _chip;
        private Prop _doubleChip;
        private bool _wantChip;
        private bool _wantDouble;

        /// <summary>A card on its way: out of the shoe, into her hand, and down on the felt.</summary>
        private sealed class Deal
        {
            public bool ToHer;
            public int Card;
            public string Clip;
            public bool Down;
            public int Slot;
            public Prop Prop;
            public int From;
            public bool Shown;
        }

        private readonly Queue<Deal> _deals = new Queue<Deal>();
        private Deal _deal;
        private int _waitFrom;

        private string _sweepClip;
        private bool _sweepHeld;

        private const int ResultMs = 4200;

        public Blackjack(Entity table, Dealer dealer, Ped me, Seat seat, int minBet, int maxBet, Random rng)
        {
            _table = table;
            _dealer = dealer;
            _me = me;
            _rng = rng;
            _minBet = Math.Max(1, minBet);
            _maxBet = Math.Max(_minBet, maxBet);
            _bet = _minBet;

            var chair = seat != null ? seat.Number : 2;
            _seatNo = 5 - chair;
            _i = _seatNo - 1;

            Chips.Ask();

            if (seat != null)
            {
                _sitter = new Sitter(me, seat, "idle_cardgames", me.Gender == Gender.Female ? FemaleFidgets : MaleFidgets, rng);
                _sitter.Enter();
            }
            else
            {
                _stage = Stage.Betting;
            }

            Hud(false);
            _dealer.Say("MINIGAME_DEALER_GREET");

            Log.Info("Den: sat down at the blackjack" + (seat != null ? ", chair " + chair + " (her player " + _seatNo + ")" : ", stood (the table has no chairs)") + ".");
        }

        private static readonly string[] MaleFidgets =
        {
            "idle_cardgames_var_01", "idle_cardgames_var_02", "idle_cardgames_var_03", "idle_cardgames_var_04",
            "idle_cardgames_var_05", "idle_cardgames_var_06", "idle_cardgames_var_07", "idle_cardgames_var_08",
            "idle_cardgames_var_09", "idle_cardgames_var_10", "idle_cardgames_var_11", "idle_cardgames_var_12",
            "idle_cardgames_var_13"
        };

        private static readonly string[] FemaleFidgets =
        {
            "female_idle_cardgames_var_01", "female_idle_cardgames_var_02", "female_idle_cardgames_var_03",
            "female_idle_cardgames_var_04", "female_idle_cardgames_var_05", "female_idle_cardgames_var_06",
            "female_idle_cardgames_var_07", "female_idle_cardgames_var_08"
        };

        public bool Finished => _stage == Stage.Done;

        public void Update()
        {
            if (_table == null || !_table.Exists() || !_dealer.Exists) { Abandon(); return; }

            if (_sitter != null) _sitter.Update();
            _cam.Update();

            Felt();
            Dealing();
            Sweeping();

            switch (_stage)
            {
                case Stage.Sitting:
                    if (_sitter == null || _sitter.Down) Open();
                    break;

                case Stage.Betting:
                    Betting();
                    break;

                case Stage.Chipping:
                    Chipping();
                    break;

                case Stage.Dealing:
                    if (_deal == null && _deals.Count == 0 && !_dealer.Busy) Dealt();
                    break;

                case Stage.Deciding:
                    Deciding();
                    break;

                case Stage.Acting:
                    Acting();
                    break;

                case Stage.DealerTurn:
                    HerTurn();
                    break;

                case Stage.Result:
                    if (Game.GameTime >= _at && !_dealer.Busy && (_sitter == null || !_sitter.Busy)) Collect();
                    break;

                case Stage.Collecting:
                    if (!_dealer.Busy) Reset();
                    break;

                case Stage.Standing:
                    if (_sitter == null || _sitter.Gone) Finish();
                    break;
            }

            if (_stage != Stage.Done) Draw();
        }

        // ---- the bet ------------------------------------------------------------------

        private void Open()
        {
            _stage = Stage.Betting;
            PlayerCam();
            _dealer.Say("MINIGAME_DEALER_PLACE_BET");
            _dealer.Act(D, "female_place_bet_request");
        }

        private void Betting()
        {
            if (Keys.Back) { Stand(); return; }

            var step = Keys.Right || Keys.Raise ? 1 : Keys.Left || Keys.Lower ? -1 : 0;

            if (step != 0)
            {
                _bet = NextStake(_bet, step);
                Sfx.Front(step > 0 ? "DLC_VW_BET_UP" : "DLC_VW_BET_DOWN");
            }

            if (!Keys.Select && !Keys.Space) return;

            if (Game.Player.Money < _bet)
            {
                Sfx.Front("DLC_VW_ERROR_MAX");
                Say("You ain't got it.");
                return;
            }

            Game.Player.Money -= _bet;
            _stake = _bet;
            _doubled = false;
            _natural = false;
            _inPlay = true;
            _hits = 0;
            _said = "";

            _step = 0;
            _stage = Stage.Chipping;
        }

        /// <summary>Your chips down, the way the casino reaches for them.</summary>
        private void Chipping()
        {
            if (_sitter == null)
            {
                _wantChip = true;
                Shuffle();
                return;
            }

            switch (_step)
            {
                case 0:
                    _sitter.Once(P, _bet >= _minBet * 10 ? "place_bet_large" : SmallBet());
                    Sfx.Front("DLC_VW_BET_DOWN");
                    _step = 1;
                    return;

                case 1:
                    if (Mine(LetGo) || _sitter.Phase >= 0.5f) _wantChip = true;
                    if (_sitter.Busy) return;

                    _wantChip = true;
                    Shuffle();
                    return;
            }
        }

        private string SmallBet()
        {
            string[] clips = { "place_bet_small", "place_bet_small_alt1", "place_bet_small_alt2", "place_bet_small_alt3" };
            return clips[_rng.Next(clips.Length)];
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

        // ---- the deal -----------------------------------------------------------------

        /// <summary>Two to you, one up and one down to her, the way the casino deals it.</summary>
        private void Shuffle()
        {
            if (_shoe.Count < 30)
            {
                _shoe.Clear();
                for (var d = 0; d < 4; d++)
                    for (var c = 0; c < 52; c++) _shoe.Add(c);

                for (var i = _shoe.Count - 1; i > 0; i--)
                {
                    var j = _rng.Next(i + 1);
                    var t = _shoe[i]; _shoe[i] = _shoe[j]; _shoe[j] = t;
                }

                Log.Info("Den: the blackjack shoe is shuffled, four decks.");
            }

            var dealTo = "female_deal_card_player_0" + _seatNo;

            Queue(false, Draw1(), dealTo, 0, false);
            Queue(true, Draw1(), "female_deal_card_self", 0, false);
            Queue(false, Draw1(), dealTo, 1, false);
            Queue(true, Draw1(), "female_deal_card_self_second_card", 1, true);

            Sfx.SceneOn("DLC_VW_Casino_Cards_Focus_Hand");
            _stage = Stage.Dealing;
        }

        private void Queue(bool toHer, int card, string clip, int slot, bool down)
        {
            Models.Ready(new Model(CardModel(card)));
            _deals.Enqueue(new Deal { ToHer = toHer, Card = card, Clip = clip, Slot = slot, Down = down });
        }

        /// <summary>
        /// The card in her hand: out of the shoe as her clip starts, shown once her hand has it,
        /// and laid down in its spot the moment her hand opens.
        /// </summary>
        private void Dealing()
        {
            var now = Game.GameTime;

            if (_deal == null)
            {
                if (_deals.Count == 0 || _dealer.Busy) return;

                var next = _deals.Peek();

                if (next.Prop == null) next.Prop = MakeCard(next.Card);

                if (next.Prop == null)
                {
                    // Still streaming. A card that never comes is dealt without its prop.
                    if (_waitFrom == 0) _waitFrom = now;
                    if (now - _waitFrom < 4000) return;
                }

                _waitFrom = 0;
                _deal = _deals.Dequeue();
                Hold(_deal.Prop);
                _dealer.Act(D, _deal.Clip);
                _deal.From = now;
                return;
            }

            if (!_deal.Shown && now - _deal.From >= 250)
            {
                Chips.Show(_deal.Prop, true);
                _deal.Shown = true;
            }

            var on = _dealer.Doing == _deal.Clip;
            var phase = _dealer.Phase;

            var down = _dealer.Fired(LetGo) ||
                       on && phase >= 0.62f && phase <= 1.01f ||
                       !on && now - _deal.From > 600 ||
                       now - _deal.From > 6000;

            if (!down) return;

            Land(_deal);
            _deal = null;
        }

        /// <summary>A card down in its spot, and the count said.</summary>
        private void Land(Deal deal)
        {
            Vector3 at, rot;

            if (deal.ToHer)
            {
                at = _table.GetOffsetPosition(HerSpot(deal.Slot));
                rot = deal.Down ? V(0f, 180f, _table.Heading) : V(0f, 0f, _table.Heading);
                _hers.Add(deal.Card);
                if (deal.Prop != null) _herCards.Add(deal.Prop);
            }
            else
            {
                var s = Math.Min(deal.Slot, 6);
                at = _table.GetOffsetPosition(SeatCards[_i][s]);
                rot = V(0f, 0f, _table.Heading + SeatTurns[_i][s]);
                _mine.Add(deal.Card);
                if (deal.Prop != null) _myCards.Add(deal.Prop);
            }

            Put(deal.Prop, at, rot);

            if (!deal.ToHer)
            {
                var t = Total(_mine);
                if (t <= 21) _dealer.Say("MINIGAME_BJACK_DEALER_" + t);
            }
            else if (!deal.Down && _holeUp || _hers.Count == 1)
            {
                _dealer.Say("MINIGAME_BJACK_DEALER_" + Showing());
            }
        }

        private void Dealt()
        {
            Sfx.SceneOff("DLC_VW_Casino_Cards_Focus_Hand");

            // A natural: she turns hers over and it is settled either way.
            if (Total(_mine) == 21)
            {
                _natural = true;
                Reveal();
                return;
            }

            Decide();
        }

        // ---- your turn ------------------------------------------------------------------

        private void Decide()
        {
            _focused = false;
            _dealer.Act(D, "female_dealer_focus_player_0" + _seatNo + "_idle_intro");
            if (_hits == 0) _dealer.Say("MINIGAME_BJACK_DEALER_ANOTHER_CARD");
            _stage = Stage.Deciding;
        }

        private bool CanDouble => _mine.Count == 2 && !_doubled && Game.Player.Money >= _bet;

        private void Deciding()
        {
            // Her eyes on your seat once she has turned to it, until you make your mind up.
            if (!_focused && !_dealer.Busy)
            {
                _dealer.Hold(D, "female_dealer_focus_player_0" + _seatNo + "_idle");
                _focused = true;
            }

            if (Keys.Select)
            {
                _action = "hit";
                if (_sitter != null) _sitter.Once(P, Pick("request_card", "request_card_alt1", "request_card_alt2"));
                Sfx.Front("DLC_VW_CONTINUE");
            }
            else if (Keys.Back)
            {
                _action = "stand";
                if (_sitter != null) _sitter.Once(P, Pick("decline_card_001", "decline_card_alt1", "decline_card_alt2"));
                Sfx.Front("DLC_VW_CONTINUE");
            }
            else if (Keys.Space && CanDouble)
            {
                _action = "double";
                Game.Player.Money -= _bet;
                _stake += _bet;
                _doubled = true;
                if (_sitter != null) _sitter.Once(P, "place_bet_double_down");
                Sfx.Front("DLC_VW_BET_DOWN");
            }
            else
            {
                return;
            }

            _step = 0;
            _stage = Stage.Acting;
        }

        private void Acting()
        {
            switch (_step)
            {
                case 0:
                    // Halfway through your tap, your wave or your push of the chips, she moves.
                    if (_sitter != null && _sitter.Busy && _sitter.Phase < 0.45f)
                    {
                        if (_action == "double" && Mine(LetGo)) _wantDouble = true;
                        return;
                    }

                    if (_action == "double") _wantDouble = true;

                    _dealer.Act(D, "female_dealer_focus_player_0" + _seatNo + "_idle_outro");

                    if (_action == "stand")
                    {
                        Reveal();
                        return;
                    }

                    _hits++;
                    Queue(false, Draw1(), (_hits == 1 ? "female_hit_card_player_0" : "female_hit_second_card_player_0") + _seatNo, _mine.Count, false);
                    _step = 1;
                    return;

                case 1:
                    if (_deal != null || _deals.Count > 0 || _dealer.Busy) return;

                    var t = Total(_mine);

                    if (t > 21)
                    {
                        _dealer.Say("MINIGAME_BJACK_DEALER_PLAYER_BUST");
                        Settle();
                        return;
                    }

                    if (_action == "double" || t == 21)
                    {
                        Reveal();
                        return;
                    }

                    Decide();
                    return;
            }
        }

        // ---- her turn -----------------------------------------------------------------

        /// <summary>Her hole card turned over with the casino's own turn, and the camera on her.</summary>
        private void Reveal()
        {
            _revealing = true;
            _holeHeld = false;
            _dealer.Act(D, "female_check_and_turn_card");
            DealerCam();
            _stage = Stage.DealerTurn;
        }

        private void HerTurn()
        {
            if (_revealing)
            {
                var hole = _herCards.Count > 1 ? _herCards[1] : null;
                var on = _dealer.Doing == "female_check_and_turn_card";
                var phase = _dealer.Phase;

                if (!_holeHeld && (_dealer.Fired(TakeHold) || on && phase >= 0.3f && phase <= 1.01f))
                {
                    Hold(hole);
                    _holeHeld = true;
                }

                if (_holeHeld && (_dealer.Fired(LetGo) || on && phase >= 0.65f && phase <= 1.01f || !on))
                {
                    Put(hole, _table.GetOffsetPosition(HerSpot(1)), V(0f, 0f, _table.Heading));
                    _holeUp = true;
                    _revealing = false;
                    _dealer.Say("MINIGAME_BJACK_DEALER_" + Total(_hers));
                }

                // A dealer without the clip turns it over at once.
                if (_revealing && !on && !_dealer.Busy)
                {
                    Put(hole, _table.GetOffsetPosition(HerSpot(1)), V(0f, 0f, _table.Heading));
                    _holeUp = true;
                    _revealing = false;
                }

                return;
            }

            if (_deal != null || _deals.Count > 0 || _dealer.Busy) return;

            if (_natural || Total(_mine) > 21)
            {
                Settle();
                return;
            }

            if (Total(_hers) < 17)
            {
                Queue(true, Draw1(), "female_deal_card_self_card_10", _hers.Count, false);
                return;
            }

            Settle();
        }

        // ---- the result -----------------------------------------------------------------

        private void Settle()
        {
            var me = Total(_mine);
            var her = Total(_hers);
            var natural = me == 21 && _mine.Count == 2;
            var herNatural = her == 21 && _hers.Count == 2;

            int back;
            string how;

            if (me > 21) { back = 0; how = "bust"; }
            else if (natural && !herNatural) { back = _stake + _stake * 3 / 2; how = "blackjack"; }
            else if (natural && herNatural) { back = _stake; how = "both blackjack"; }
            else if (her > 21) { back = _stake * 2; how = "dealer busts"; }
            else if (me > her) { back = _stake * 2; how = me + " beats " + her; }
            else if (me == her) { back = _stake; how = "push"; }
            else { back = 0; how = her + " beats " + me; }

            if (back > 0) Game.Player.Money += back;

            _net = back - _stake;
            _session += _net;
            _inPlay = false;

            if (her > 21 && me <= 21) _dealer.Say("MINIGAME_DEALER_BUSTS");
            else if (_net < 0) _dealer.Say("MINIGAME_DEALER_WINS");

            if (_net > 0)
            {
                _said = how + " -- you win $" + _net.ToString("N0");
                Sfx.Front("DLC_VW_WIN_CHIPS");
                _dealer.React(true);
                React(natural || _net >= _stake * 2 ? "great" : "good");
            }
            else if (_net == 0)
            {
                _said = how + " -- stake back";
                React("impartial");
            }
            else
            {
                _said = how + " -- house takes $" + (-_net).ToString("N0");
                _dealer.React(false);
                React(me > 21 ? "terrible" : "bad");
            }

            _saidUntil = Game.GameTime + ResultMs + 4000;

            Log.Info("Den: blackjack " + how + ", you " + Hand(_mine) + " (" + me + ") her " + Hand(_hers) + " (" + her + "); $" +
                     _stake.ToString("N0") + (_doubled ? " doubled" : "") + (_net > 0 ? ", won $" + _net.ToString("N0") : _net == 0 ? ", pushed" : ", lost") + ".");

            PlayerCam();
            _at = Game.GameTime + ResultMs;
            _stage = Stage.Result;
        }

        /// <summary>The chips paid or taken, then your cards and hers swept up.</summary>
        private void Collect()
        {
            Clear();

            _dealer.Act(D, "female_retrieve_cards_player_0" + _seatNo);
            _dealer.Act(D, "female_retrieve_own_cards_and_remove");
            _stage = Stage.Collecting;
        }

        /// <summary>The cards in her hand as she sweeps them, and gone as it opens.</summary>
        private void Sweeping()
        {
            var doing = _dealer.Doing;

            if (doing != _sweepClip)
            {
                _sweepClip = doing;
                _sweepHeld = false;
            }

            if (doing == null) return;

            List<Prop> cards;

            if (doing.StartsWith("female_retrieve_cards_player")) cards = _myCards;
            else if (doing == "female_retrieve_own_cards_and_remove") cards = _herCards;
            else return;

            var phase = _dealer.Phase;

            if (!_sweepHeld && (_dealer.Fired(TakeHold) || phase >= 0.3f && phase <= 1.01f))
            {
                foreach (var c in cards) Hold(c);
                _sweepHeld = true;
            }

            if (_sweepHeld && (_dealer.Fired(LetGo) || phase >= 0.72f && phase <= 1.01f))
            {
                foreach (var c in cards) Chips.Gone(c);
                cards.Clear();
            }
        }

        private void Reset()
        {
            GoneAll();
            _mine.Clear();
            _hers.Clear();
            _holeUp = false;
            _revealing = false;

            _dealer.Say("MINIGAME_DEALER_PLACE_BET");
            _stage = Stage.Betting;
        }

        /// <summary>How you take it, in your chair.</summary>
        private void React(string how)
        {
            if (_sitter == null) return;

            string clip;

            if (_sitter.Female)
            {
                var most = how == "great" || how == "terrible" ? 5 : how == "impartial" ? 7 : 4;
                clip = "female_reaction_" + how + "_var_0" + (1 + _rng.Next(most));
            }
            else
            {
                var most = how == "impartial" ? 8 : 4;
                clip = "reaction_" + how + "_var_0" + (1 + _rng.Next(most));
            }

            _sitter.Once(Scene.SharedPlayer, clip);
        }

        // ---- cards and chips ----------------------------------------------------------------

        private static string CardModel(int c)
        {
            return "vw_prop_vw_" + SuitModel[c / 13] + "_char_" + RankModel[c % 13];
        }

        /// <summary>A card, hidden, lying in the shoe the way the casino has them.</summary>
        private Prop MakeCard(int c)
        {
            var model = new Model(CardModel(c));
            if (!Models.Ready(model)) return null;

            var at = _table.GetOffsetPosition(Shoe);
            var rot = V(0f, 164.52f, _table.Heading + 11.5f);
            return Chips.Make(model, at, rot, false, false);
        }

        /// <summary>A card into her right hand.</summary>
        private void Hold(Prop card)
        {
            if (card == null || !card.Exists() || !_dealer.Exists) return;

            try
            {
                Function.Call(Hash.FREEZE_ENTITY_POSITION, card.Handle, false);
                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, _dealer.Ped.Handle, RightHand);
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, card.Handle, _dealer.Ped.Handle, bone,
                              0f, 0f, 0f, 0f, 0f, 0f, false, false, false, true, 2, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Den: a card would not go in her hand: " + ex.Message);
            }
        }

        /// <summary>A card out of her hand and down, exactly where it goes.</summary>
        private static void Put(Prop card, Vector3 at, Vector3 rot)
        {
            if (card == null || !card.Exists()) return;

            try
            {
                Function.Call(Hash.DETACH_ENTITY, card.Handle, false, true);
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, card.Handle, at.X, at.Y, at.Z, false, false, false);
                Function.Call(Hash.SET_ENTITY_ROTATION, card.Handle, rot.X, rot.Y, rot.Z, 2, true);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, card.Handle, true);
                Chips.Show(card, true);
            }
            catch { }
        }

        /// <summary>The chips wanted on the felt, put down as their models come in.</summary>
        private void Felt()
        {
            var turn = _table.Heading + SeatTurns[_i][0];

            if (_wantChip && (_chip == null || !_chip.Exists())) _chip = Chips.Put(_table, BetSpot[_i], _bet, turn);
            if (_wantDouble && (_doubleChip == null || !_doubleChip.Exists())) _doubleChip = Chips.Put(_table, DoubleSpot[_i], _bet, turn);
        }

        private void Clear()
        {
            _wantChip = _wantDouble = false;
            Chips.Gone(_chip);
            Chips.Gone(_doubleChip);
            _chip = _doubleChip = null;
        }

        private void GoneAll()
        {
            foreach (var c in _myCards) Chips.Gone(c);
            foreach (var c in _herCards) Chips.Gone(c);
            _myCards.Clear();
            _herCards.Clear();

            if (_deal != null) Chips.Gone(_deal.Prop);
            _deal = null;

            foreach (var d in _deals) Chips.Gone(d.Prop);
            _deals.Clear();
        }

        private int Draw1()
        {
            if (_shoe.Count == 0)
            {
                for (var c = 0; c < 52; c++) _shoe.Add(c);
            }

            var i = _rng.Next(_shoe.Count);
            var card = _shoe[i];
            _shoe.RemoveAt(i);
            return card;
        }

        /// <summary>A hand's count, aces as eleven while that does not bust it.</summary>
        private static int Total(List<int> hand)
        {
            var total = 0;
            var aces = 0;

            foreach (var c in hand)
            {
                var r = c % 13;

                if (r == 0) { aces++; total += 11; }
                else if (r >= 9) total += 10;
                else total += r + 1;
            }

            while (total > 21 && aces > 0)
            {
                total -= 10;
                aces--;
            }

            return total;
        }

        /// <summary>What she has showing: her count without the card still face down.</summary>
        private int Showing()
        {
            if (_holeUp || _hers.Count < 2) return Total(_hers);
            return Total(new List<int> { _hers[0] });
        }

        private bool Mine(int cue)
        {
            try { return _me != null && _me.Exists() && Function.Call<bool>(Hash.HAS_ANIM_EVENT_FIRED, _me.Handle, cue); }
            catch { return false; }
        }

        private string Pick(params string[] clips)
        {
            return clips[_rng.Next(clips.Length)];
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

        // ---- the cameras ----------------------------------------------------------------

        /// <summary>
        /// The casino's own camera for a seat: a quarter of a metre out from the chair and just
        /// above your head, looking down sixty degrees at your part of the felt. Your head is
        /// behind it, so it never fills the shot.
        /// </summary>
        private void PlayerCam()
        {
            if (_sitter == null) { DealerCam(); return; }

            var fwd = Forward();
            var from = _sitter.Seat.At + fwd * 0.245f + V(0f, 0f, 1.415f);
            var at = from + fwd * 0.487f - V(0f, 0f, 0.873f);
            _cam.Look(from, at, 55f, 900, 0.12f);
        }

        /// <summary>The casino's camera on her side of the table, over the middle of the felt.</summary>
        private void DealerCam()
        {
            var fwd = _table.ForwardVector;
            fwd.Z = 0f;
            if (fwd.Length() > 0.01f) fwd.Normalize();

            var from = _table.GetOffsetPosition(V(-0.0094f, -0.0611f, 1.5098f));
            var at = from + fwd * 0.513f - V(0f, 0f, 0.858f);
            _cam.Look(from, at, 55f, 800, 0.1f);
        }

        /// <summary>Which way the chair faces: whichever of its bone's axes points at the table.</summary>
        private Vector3 Forward()
        {
            var seat = _sitter.Seat;
            var h = seat.Rot.Z * (float)Math.PI / 180f;
            var x = V((float)Math.Cos(h), (float)Math.Sin(h), 0f);
            var y = V(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
            var best = Vector3.Dot(x, seat.Facing) >= Vector3.Dot(y, seat.Facing) ? x : y;
            return Vector3.Dot(best, seat.Facing) < 0.3f ? seat.Facing : best;
        }

        // ---- out ----------------------------------------------------------------------

        private void Stand()
        {
            Tidy();

            _dealer.Say(_session > 0 ? "MINIGAME_DEALER_LEAVE_GOOD_GAME"
                      : _session < 0 ? "MINIGAME_DEALER_LEAVE_BAD_GAME"
                      : "MINIGAME_DEALER_LEAVE_NEUTRAL_GAME");

            if (_sitter != null) _sitter.Stand();
            _stage = Stage.Standing;
        }

        private void Tidy()
        {
            Clear();
            GoneAll();
            _dealer.Drop();
            if (_dealer.Exists) _dealer.Idle();
            Sfx.SceneOff("DLC_VW_Casino_Cards_Focus_Hand");
            _cam.Stop();
            Hud(true);
        }

        /// <summary>Straight out, whatever state it is in. A hand not yet settled is called off and its stake comes back.</summary>
        public void Abandon()
        {
            if (_stage == Stage.Done) return;

            if (_inPlay)
            {
                Game.Player.Money += _stake;
                _inPlay = false;
            }

            Finish();
        }

        private void Finish()
        {
            if (_stage == Stage.Done) return;

            Tidy();
            if (_sitter != null) _sitter.Let();
            _stage = Stage.Done;
            Log.Info("Den: up from the blackjack.");
        }

        private static void Hud(bool on)
        {
            try { Function.Call(Hash.DISPLAY_RADAR, on); }
            catch { }
        }

        private void Say(string words)
        {
            _said = words;
            _saidUntil = Game.GameTime + 2200;
        }

        // ---- the screen -----------------------------------------------------------------

        private static readonly Color Gold = Color.FromArgb(255, 240, 200, 80);
        private static readonly Color Green = Color.FromArgb(255, 114, 204, 114);
        private static readonly Color Red = Color.FromArgb(255, 224, 50, 50);

        private string Count(List<int> hand)
        {
            if (hand.Count == 0) return "--";

            var t = Total(hand);
            var soft = false;
            var hard = 0;
            var aces = 0;

            foreach (var c in hand)
            {
                var r = c % 13;
                if (r == 0) { aces++; hard += 1; }
                else hard += r >= 9 ? 10 : r + 1;
            }

            soft = aces > 0 && t != hard;
            return soft ? "SOFT " + t : t.ToString();
        }

        private void Draw()
        {
            var cash = "$" + Game.Player.Money.ToString("N0");
            var saying = _said.Length > 0 && Game.GameTime < _saidUntil;
            var hers = _hers.Count == 0 ? "--" : Showing().ToString();

            switch (_stage)
            {
                case Stage.Betting:
                    Help.ShowThisFrame(saying ? _said : "Place your bet. The dealer stands on 17; a blackjack pays 3 to 2.");

                    Bars.Draw("CASH", cash, Color.White,
                              "BET", "$" + _bet.ToString("N0"), Gold);

                    Buttons.Show(Control.PhoneCancel, "Stand up",
                                 Control.PhoneSelect, "Deal",
                                 Control.PhoneRight, "Raise",
                                 Control.PhoneLeft, "Lower");
                    break;

                case Stage.Deciding:
                    Help.ShowThisFrame(saying ? _said : "You have " + Count(_mine) + ". The dealer shows " + hers + ".");

                    Bars.Draw("CASH", cash, Color.White,
                              "DEALER", hers, Color.White,
                              "YOUR HAND", Count(_mine), Gold,
                              "BET", "$" + _stake.ToString("N0"), Color.White);

                    if (CanDouble)
                        Buttons.Show(Control.Jump, "Double", Control.PhoneCancel, "Stand", Control.PhoneSelect, "Hit");
                    else
                        Buttons.Show(Control.PhoneCancel, "Stand", Control.PhoneSelect, "Hit");
                    break;

                case Stage.Result:
                case Stage.Collecting:
                    if (saying) Help.ShowThisFrame(_said);

                    Bars.Draw("CASH", cash, Color.White,
                              "DEALER", Total(_hers).ToString(), Color.White,
                              "YOUR HAND", Count(_mine), Color.White,
                              _net > 0 ? "WON" : _net == 0 ? "PUSH" : "LOST", "$" + Math.Abs(_net).ToString("N0"),
                              _net > 0 ? Green : _net == 0 ? Gold : Red);
                    break;

                case Stage.Sitting:
                case Stage.Standing:
                    break;

                default:
                    Bars.Draw("CASH", cash, Color.White,
                              "DEALER", hers, Color.White,
                              "YOUR HAND", Count(_mine), Gold,
                              "BET", "$" + _stake.ToString("N0"), Color.White);
                    break;
            }
        }
    }
}
