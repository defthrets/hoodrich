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
    /// Three Card Poker, at the den's poker table, played the way the Diamond plays it.
    ///
    /// THE DIAMOND'S RULES. An ante, and a Pair Plus beside it if you want one. Three cards
    /// each. You play -- a second bet the size of the ante -- or fold, and lose the ante. The
    /// dealer needs a queen high to qualify: without one the ante pays even money and the play
    /// bet comes back; with one, the better hand takes both at even money and a tie pushes. An
    /// ante bonus on a straight or better whatever she holds, and the Pair Plus pays on your
    /// hand alone, fold or play. Three cards rank their own way: straight flush, three of a
    /// kind, straight, flush, pair, high card -- a straight is harder to make than a flush.
    ///
    /// THE DIAMOND'S TABLE, since 2026-09-26, when Michael asked for "the actual gambling
    /// feature, same graphics, everything". You sit in one of the table's own chairs. Your chips
    /// go down on the felt with the casino's own reach for them. She picks up the deck, shuffles,
    /// and deals real cards across the felt -- three to your seat, three to herself -- on the
    /// clips the casino made for each card, one clip a card, for each of the four seats. You
    /// lift yours and look at them, and play or fold with them in your hand. She turns hers over
    /// and then yours, and sweeps them up. Every one of those moves is the casino's own clip in a
    /// scene on the table or on your chair; the only thing decided here is which card is which.
    ///
    /// Your seat decides which of her clips deals to you: the casino numbers its seats from the
    /// other end, so the chair the table calls 4 is her p01 and chair 1 is her p04.
    /// </summary>
    internal sealed class Poker
    {
        private enum Stage { Sitting, Betting, Chipping, Loading, Dealing, Looking, Deciding, Acting, Revealing, Result, Collecting, Standing, Done }

        private readonly Entity _table;
        private readonly Dealer _dealer;
        private readonly Ped _me;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;
        private readonly Sitter _sitter;
        private readonly TableCam _cam = new TableCam();

        /// <summary>Which chair, 1 to 4, and her name for it in the clips.</summary>
        private readonly int _chair;
        private readonly string _pid;

        private Stage _stage = Stage.Sitting;
        private int _step;
        private int _at;

        /// <summary>Which bet the arrows are on: the ante, or the Pair Plus.</summary>
        private int _field;
        private int _ante;
        private int _plus;

        private int _anteBet;
        private int _plusBet;
        private int _playBet;
        private bool _played;
        private bool _inPlay;
        private int _net;
        private int _session;
        private string _said = "";
        private int _saidUntil;

        private readonly List<int> _deck = new List<int>();
        private readonly List<int> _mine = new List<int>();
        private readonly List<int> _hers = new List<int>();

        private readonly Prop[] _myCards = new Prop[3];
        private readonly Prop[] _herCards = new Prop[3];

        private Prop _pack;
        private bool _packHeld;
        private bool _packSaid;

        private Prop _anteChip;
        private Prop _plusChip;
        private Prop _playChip;
        private bool _wantAnte;
        private bool _wantPlus;
        private bool _wantPlay;

        /// <summary>A card to be shown when its clip is far enough in -- it would flash where it was otherwise.</summary>
        private sealed class Cue
        {
            public Prop Prop;
            public int Scene;
            public float At;
        }

        private readonly List<Cue> _cues = new List<Cue>();

        private string _lastDoing;
        private int _loadFrom;

        private const int ResultMs = 5200;
        private const int LoadMostMs = 6000;

        private const string DeckModel = "vw_prop_casino_cards_01";

        /// <summary>The animation event on her pick-up clip that is her hand closing on the deck.</summary>
        private const int DeckInHand = 1691374422;

        /// <summary>Her left hand's prop bone.</summary>
        private const int LeftHand = 60309;

        private const int HighCard = 0;
        private const int Pair = 1;
        private const int Flush = 2;
        private const int Straight = 3;
        private const int Trips = 4;
        private const int StraightFlush = 5;

        private static readonly string[] HandNames = { "high card", "a pair", "a flush", "a straight", "three of a kind", "a straight flush" };
        private static readonly string[] Ranks = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        private static readonly string[] Suits = { "S", "H", "D", "C" };

        /// <summary>The casino's cards by our suits and ranks: vw_prop_cas_card_hrt_queen and the rest.</summary>
        private static readonly string[] SuitModel = { "spd", "hrt", "dia", "club" };
        private static readonly string[] RankModel = { "ace", "02", "03", "04", "05", "06", "07", "08", "09", "10", "jack", "queen", "king" };

        /// <summary>
        /// Where each chair's chips go on the felt, in the table's own space: the ante circle, the
        /// Pair Plus circle and the play box, chairs 1 to 4. Measured by a script that plays this
        /// table and matched to its chairs.
        /// </summary>
        private static readonly Vector3[] AnteSpot =
        {
            new Vector3(-0.606975f, 0.249675f, 0.95f), new Vector3(-0.2804f, -0.109775f, 0.95f),
            new Vector3(0.247825f, -0.123625f, 0.95f), new Vector3(0.59535f, 0.200875f, 0.95f)
        };

        private static readonly Vector3[] PlusSpot =
        {
            new Vector3(-0.529875f, 0.281425f, 0.95f), new Vector3(-0.2552f, -0.031225f, 0.95f),
            new Vector3(0.2163f, -0.04745f, 0.95f), new Vector3(0.51655f, 0.2268f, 0.95f)
        };

        private static readonly Vector3[] PlaySpot =
        {
            new Vector3(-0.69795f, 0.211525f, 0.954f), new Vector3(-0.30935f, -0.205675f, 0.954f),
            new Vector3(0.2869f, -0.211925f, 0.954f), new Vector3(0.689125f, 0.171575f, 0.954f)
        };

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

        public Poker(Entity table, Dealer dealer, Ped me, Seat seat, int minBet, int maxBet, Random rng)
        {
            _table = table;
            _dealer = dealer;
            _me = me;
            _rng = rng;
            _minBet = Math.Max(1, minBet);
            _maxBet = Math.Max(_minBet, maxBet);
            _ante = _minBet;

            _chair = seat != null ? seat.Number : 2;
            _pid = "p0" + (5 - _chair);

            Chips.Ask();
            Models.Ready(new Model(DeckModel));

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

            Log.Info("Den: sat down at the poker" + (seat != null ? ", chair " + seat.Number + " (her " + _pid + ")" : ", stood (the table has no chairs)") + ".");
        }

        public bool Finished => _stage == Stage.Done;

        public void Update()
        {
            if (_table == null || !_table.Exists()) { Abandon(); return; }

            if (_sitter != null) _sitter.Update();
            _cam.Update();

            Pack();
            Cues();
            Felt();
            Watch();

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

                case Stage.Loading:
                    Loading();
                    break;

                case Stage.Dealing:
                    if (!_dealer.Busy) Look();
                    break;

                case Stage.Looking:
                    if (_sitter == null || !_sitter.Busy) Decide();
                    break;

                case Stage.Deciding:
                    Deciding();
                    break;

                case Stage.Acting:
                    Acting();
                    break;

                case Stage.Revealing:
                    if (!_dealer.Busy) Settle();
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

            _lastDoing = _dealer.Doing;

            if (_stage != Stage.Done) Draw();
        }

        // ---- the bets -----------------------------------------------------------------

        private void Open()
        {
            _stage = Stage.Betting;
            TableCam();
            _dealer.Say("MINIGAME_DEALER_PLACE_BET");
        }

        private void Betting()
        {
            if (Keys.Back) { Stand(); return; }

            if (Keys.Up || Keys.Down) { _field = 1 - _field; Sfx.Front("DLC_VW_BET_HIGHLIGHT"); }

            var step = Keys.Right || Keys.Raise ? 1 : Keys.Left || Keys.Lower ? -1 : 0;

            if (step != 0)
            {
                Sfx.Front(step > 0 ? "DLC_VW_BET_UP" : "DLC_VW_BET_DOWN");
                if (_field == 0) _ante = NextStake(_ante, step);
                else _plus = NextPlus(_plus, step);
            }

            if (!Keys.Select && !Keys.Space) return;

            var total = _ante + _plus;

            if (Game.Player.Money < total)
            {
                Sfx.Front("DLC_VW_ERROR_MAX");
                Say("You ain't got it.");
                return;
            }

            Game.Player.Money -= total;

            _anteBet = _ante;
            _plusBet = _plus;
            _playBet = 0;
            _played = false;
            _inPlay = true;
            _said = "";
            _mine.Clear();
            _hers.Clear();

            _step = 0;
            _stage = Stage.Chipping;
        }

        /// <summary>Your chips down, the way the casino reaches for them: the ante, then the Pair Plus.</summary>
        private void Chipping()
        {
            if (_sitter == null)
            {
                _wantAnte = true;
                _wantPlus = _plusBet > 0;
                Shuffle();
                return;
            }

            switch (_step)
            {
                case 0:
                    _sitter.Once(Scene.PokerPlayer, _anteBet >= 10000 ? "bet_ante_large" : "bet_ante");
                    Sfx.Front("DLC_VW_BET_DOWN");
                    _step = 1;
                    return;

                case 1:
                    if (_sitter.Phase >= 0.45f) _wantAnte = true;
                    if (_sitter.Busy) return;

                    _wantAnte = true;

                    if (_plusBet > 0)
                    {
                        _sitter.Once(Scene.PokerPlayer, _plusBet >= 10000 ? "bet_plus_large" : "bet_plus");
                        _step = 2;
                        return;
                    }

                    Shuffle();
                    return;

                case 2:
                    if (_sitter.Phase >= 0.45f) _wantPlus = true;
                    if (_sitter.Busy) return;

                    _wantPlus = true;
                    Shuffle();
                    return;
            }
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

        // ---- the deal -----------------------------------------------------------------

        /// <summary>Bets closed: the deck shuffled here, the six cards drawn, and their props asked for.</summary>
        private void Shuffle()
        {
            _deck.Clear();
            for (var c = 0; c < 52; c++) _deck.Add(c);

            for (var i = _deck.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var t = _deck[i]; _deck[i] = _deck[j]; _deck[j] = t;
            }

            for (var i = 0; i < 3; i++) _mine.Add(Draw1());
            for (var i = 0; i < 3; i++) _hers.Add(Draw1());

            foreach (var c in _mine) Models.Ready(new Model(CardModel(c)));
            foreach (var c in _hers) Models.Ready(new Model(CardModel(c)));

            _dealer.Say("MINIGAME_DEALER_CLOSED_BETS");

            _loadFrom = Game.GameTime;
            _stage = Stage.Loading;
        }

        private void Loading()
        {
            var ready = true;

            for (var i = 0; i < 3; i++)
            {
                if (_myCards[i] == null) _myCards[i] = MakeCard(_mine[i]);
                if (_herCards[i] == null) _herCards[i] = MakeCard(_hers[i]);
                if (_myCards[i] == null || _herCards[i] == null) ready = false;
            }

            if (!ready && Game.GameTime - _loadFrom < LoadMostMs) return;

            if (!ready) Log.Info("Den: not every card came in; the hand is dealt with what did.");

            Deal();
        }

        /// <summary>A card, hidden under the table until its clip puts it in her hand.</summary>
        private Prop MakeCard(int c)
        {
            var model = new Model(CardModel(c));
            if (!Models.Ready(model)) return null;

            return Chips.Make(model, _table.Position - new Vector3(0f, 0f, 1f), Vector3.Zero, false, false);
        }

        private static string CardModel(int c)
        {
            return "vw_prop_cas_card_" + SuitModel[c / 13] + "_" + RankModel[c % 13];
        }

        /// <summary>The deal as the casino deals it: the deck up, shuffled, three to you, three to her, the deck down.</summary>
        private void Deal()
        {
            const string d = Scene.PokerDealer;

            _dealer.Act(d, "female_deck_pick_up");
            _dealer.Act(d, "female_deck_shuffle", s => Cards(s, _herCards, "deck_shuffle_card_", d, 0.03f));
            _dealer.Act(d, "female_deck_deal_" + _pid, s =>
            {
                Hide(_herCards);
                Cards(s, _myCards, "deck_deal_" + _pid + "_card_", d, 0.05f);
            });
            _dealer.Act(d, "female_deck_deal_self", s => Cards(s, _herCards, "deck_deal_self_card_", d, 0.05f));
            _dealer.Act(d, "female_deck_put_down");

            Sfx.SceneOn("DLC_VW_Casino_Cards_Focus_Hand");
            _stage = Stage.Dealing;
        }

        /// <summary>Three cards into a scene, each on its own clip, shown once the clip is far enough in.</summary>
        private void Cards(int scene, Prop[] cards, string prefix, string dict, float showAt)
        {
            for (var i = 0; i < 3; i++)
            {
                var card = cards[i];
                if (card == null || !card.Exists()) continue;

                Chips.Show(card, false);
                Scene.Prop(scene, card, dict, prefix + (char)('a' + i));
                _cues.Add(new Cue { Prop = card, Scene = scene, At = showAt });
            }
        }

        private static void Hide(Prop[] cards)
        {
            foreach (var c in cards) Chips.Show(c, false);
        }

        private void Cues()
        {
            for (var i = _cues.Count - 1; i >= 0; i--)
            {
                var cue = _cues[i];

                if (cue.Prop == null || !cue.Prop.Exists()) { _cues.RemoveAt(i); continue; }
                if (Scene.Phase(cue.Scene) < cue.At) continue;

                Chips.Show(cue.Prop, true);
                _cues.RemoveAt(i);
            }
        }

        /// <summary>
        /// The deck: on the table where her pick-up clip reaches for it, in her left hand from the
        /// moment it closes on it, and back on the table when she puts it down.
        /// </summary>
        private void Pack()
        {
            if (_pack == null)
            {
                var model = new Model(DeckModel);
                if (!Models.Ready(model)) return;

                Vector3 at, rot;
                Rest(out at, out rot);
                _pack = Chips.Make(model, at, rot, true, true);
                return;
            }

            if (!_pack.Exists() || !_dealer.Exists) return;

            var doing = _dealer.Doing;

            if (!_packHeld && doing == "female_deck_pick_up")
            {
                var fired = false;

                try { fired = Function.Call<bool>(Hash.HAS_ANIM_EVENT_FIRED, _dealer.Ped.Handle, DeckInHand); }
                catch { }

                if (fired || _dealer.Phase >= 0.42f)
                {
                    try
                    {
                        Function.Call(Hash.FREEZE_ENTITY_POSITION, _pack.Handle, false);
                        var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, _dealer.Ped.Handle, LeftHand);
                        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _pack.Handle, _dealer.Ped.Handle, bone,
                                      0f, 0f, 0f, 0f, 0f, 0f, false, false, false, true, 2, true);
                        _packHeld = true;

                        if (!_packSaid)
                        {
                            _packSaid = true;
                            Log.Info("Den: the dealer has the deck in her hand (" + (fired ? "on her clip's cue" : "by the clock") + ").");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Den: the deck would not go in her hand: " + ex.Message);
                    }
                }
            }
            else if (_packHeld && (doing == "female_deck_put_down" && _dealer.Phase >= 0.55f || doing == null))
            {
                Put();
            }
        }

        /// <summary>The deck back where it lives on the table.</summary>
        private void Put()
        {
            if (_pack == null || !_pack.Exists()) return;

            try
            {
                Function.Call(Hash.DETACH_ENTITY, _pack.Handle, true, true);

                Vector3 at, rot;
                Rest(out at, out rot);
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _pack.Handle, at.X, at.Y, at.Z, false, false, false);
                Function.Call(Hash.SET_ENTITY_ROTATION, _pack.Handle, rot.X, rot.Y, rot.Z, 2, true);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, _pack.Handle, true);
            }
            catch { }

            _packHeld = false;
        }

        /// <summary>Where the deck sits between hands: where her pick-up clip starts it.</summary>
        private void Rest(out Vector3 at, out Vector3 rot)
        {
            var p = _table.Position;
            var r = _table.Rotation;

            try
            {
                at = Function.Call<Vector3>(Hash.GET_ANIM_INITIAL_OFFSET_POSITION, Scene.PokerDealer, "deck_pick_up_deck",
                                            p.X, p.Y, p.Z, r.X, r.Y, r.Z, 0.01f, 2);
                rot = Function.Call<Vector3>(Hash.GET_ANIM_INITIAL_OFFSET_ROTATION, Scene.PokerDealer, "deck_pick_up_deck",
                                             p.X, p.Y, p.Z, r.X, r.Y, r.Z, 0.01f, 2);

                if (at.DistanceTo(p) < 3f) return;
            }
            catch { }

            at = _table.GetOffsetPosition(new Vector3(0.1f, 0.25f, 0.95f));
            rot = r;
        }

        /// <summary>The chips wanted on the felt, put down as their models come in.</summary>
        private void Felt()
        {
            var i = Math.Max(0, Math.Min(3, _chair - 1));
            var heading = _me != null && _me.Exists() ? _me.Heading : _table.Heading;

            if (_wantAnte && (_anteChip == null || !_anteChip.Exists())) _anteChip = Chips.Put(_table, AnteSpot[i], _anteBet, heading);
            if (_wantPlus && (_plusChip == null || !_plusChip.Exists())) _plusChip = Chips.Put(_table, PlusSpot[i], _plusBet, heading);
            if (_wantPlay && (_playChip == null || !_playChip.Exists())) _playChip = Chips.Put(_table, PlaySpot[i], _playBet, heading);
        }

        /// <summary>The camera, timed to what she is doing: on her cards as she turns them, then on yours.</summary>
        private void Watch()
        {
            var doing = _dealer.Doing;
            if (doing == _lastDoing || doing == null) return;

            if (doing == "female_reveal_self") DealerCam();
            else if (doing.StartsWith("female_reveal_played") || doing.StartsWith("female_reveal_folded")) SeatCam();
            else if (doing.StartsWith("female_cards_collect")) TableCam();
        }

        // ---- the hand -----------------------------------------------------------------

        /// <summary>Your three picked up off the felt and held up to look at.</summary>
        private void Look()
        {
            Sfx.SceneOff("DLC_VW_Casino_Cards_Focus_Hand");

            if (_sitter == null)
            {
                Decide();
                return;
            }

            var s = _sitter.Once(Scene.PokerPlayer, "cards_pickup");
            Cards(s, _myCards, "cards_pickup_card_", Scene.PokerPlayer, 0f);
            _stage = Stage.Looking;
        }

        /// <summary>Held up in front of you, and the camera over your shoulder on them.</summary>
        private void Decide()
        {
            if (_sitter != null)
            {
                var s = _sitter.Hold(Scene.PokerPlayer, "cards_idle", true);
                Cards(s, _myCards, "cards_idle_card_", Scene.PokerPlayer, 0f);
                HandCam();
            }

            _dealer.Say("MINIGAME_DEALER_COMMENT_SLOW");
            _stage = Stage.Deciding;
        }

        private void Deciding()
        {
            if (Keys.Select || Keys.Space)
            {
                if (Game.Player.Money < _anteBet)
                {
                    Sfx.Front("DLC_VW_ERROR_MAX");
                    Say("You ain't got the play bet. Fold or find it.");
                    return;
                }

                Game.Player.Money -= _anteBet;
                _playBet = _anteBet;
                _played = true;

                if (_sitter != null)
                {
                    var s = _sitter.Once(Scene.PokerPlayer, "cards_play");
                    Cards(s, _myCards, "cards_play_card_", Scene.PokerPlayer, 0f);
                }

                Sfx.Front("DLC_VW_BET_DOWN");
                TableCam();
                _step = 0;
                _stage = Stage.Acting;
                return;
            }

            if (Keys.Back)
            {
                _played = false;

                if (_sitter != null)
                {
                    var s = _sitter.Once(Scene.PokerPlayer, "cards_fold");
                    Cards(s, _myCards, "cards_fold_card_", Scene.PokerPlayer, 0f);
                }

                Sfx.Front("DLC_VW_REMOVE_BET");
                TableCam();
                _step = 0;
                _stage = Stage.Acting;
            }
        }

        /// <summary>Played -- the cards down and the play chips beside them -- or folded, then her turn.</summary>
        private void Acting()
        {
            if (_sitter == null)
            {
                if (_played) _wantPlay = true;
                Reveal();
                return;
            }

            switch (_step)
            {
                case 0:
                    if (_sitter.Busy) return;

                    if (_played)
                    {
                        _sitter.Once(Scene.PokerPlayer, _playBet >= 10000 ? "cards_bet_large" : "cards_bet");
                        _step = 1;
                        return;
                    }

                    Reveal();
                    return;

                case 1:
                    if (_sitter.Phase >= 0.45f) _wantPlay = true;
                    if (_sitter.Busy) return;

                    _wantPlay = true;
                    Reveal();
                    return;
            }
        }

        /// <summary>Hers turned over, then yours, and a nod to your seat.</summary>
        private void Reveal()
        {
            const string d = Scene.PokerDealer;
            var how = _played ? "played" : "folded";

            _dealer.Act(d, "female_reveal_self", s => Cards(s, _herCards, "reveal_self_card_", d, 0f));
            _dealer.Act(d, "female_reveal_" + how + "_" + _pid, s =>
            {
                Hide(_myCards);
                Cards(s, _myCards, "reveal_" + how + "_" + _pid + "_card_", d, 0.025f);
            });
            _dealer.Act(Scene.SharedDealer, "female_acknowledge_" + _pid);

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
            _session += _net;
            _inPlay = false;

            var line = string.Join(", ", how);

            if (_net > 0)
            {
                _said = line + " -- you win $" + _net.ToString("N0");
                Sfx.Front("DLC_VW_WIN_CHIPS");
                _dealer.React(true);
                React(_net >= staked * 3 ? "great" : "good");
            }
            else if (_net == 0)
            {
                _said = line + " -- stake back";
                React("impartial");
            }
            else
            {
                _said = line + " -- house takes $" + (-_net).ToString("N0");
                _dealer.React(false);
                React(_played ? "terrible" : "bad");
            }

            _saidUntil = Game.GameTime + ResultMs + 4000;

            Log.Info("Den: poker, you " + Hand(_mine) + " (" + HandNames[me.Cat] + ") her " + Hand(_hers) + " (" +
                     HandNames[her.Cat] + "); ante $" + _anteBet.ToString("N0") + ", pair plus $" + _plusBet.ToString("N0") +
                     (_played ? ", played" : ", folded") + "; " + (_net >= 0 ? "+" : "-") + "$" + Math.Abs(_net).ToString("N0") + ".");

            _at = Game.GameTime + ResultMs;
            _stage = Stage.Result;
        }

        /// <summary>The chips taken or paid, and the cards swept up: yours, then hers.</summary>
        private void Collect()
        {
            const string d = Scene.PokerDealer;

            Clear();

            _dealer.Act(d, "female_cards_collect_" + _pid, s => Cards(s, _myCards, "cards_collect_" + _pid + "_card_", d, 0f));
            _dealer.Act(d, "female_cards_collect_self", s =>
            {
                Gone(_myCards);
                Cards(s, _herCards, "cards_collect_self_card_", d, 0f);
            });

            _stage = Stage.Collecting;
        }

        private void Reset()
        {
            Gone(_myCards);
            Gone(_herCards);
            _cues.Clear();
            _mine.Clear();
            _hers.Clear();

            TableCam();
            _dealer.Say("MINIGAME_DEALER_ANOTHER_GO");
            _stage = Stage.Betting;
        }

        /// <summary>The chips off the felt.</summary>
        private void Clear()
        {
            _wantAnte = _wantPlus = _wantPlay = false;

            Chips.Gone(_anteChip);
            Chips.Gone(_plusChip);
            Chips.Gone(_playChip);
            _anteChip = _plusChip = _playChip = null;
        }

        private static void Gone(Prop[] cards)
        {
            for (var i = 0; i < cards.Length; i++)
            {
                Chips.Gone(cards[i]);
                cards[i] = null;
            }
        }

        /// <summary>How you take it, in your chair: the casino has a seated reaction for every size of luck.</summary>
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

        private int Draw1()
        {
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

        // ---- the cameras ----------------------------------------------------------------

        /// <summary>
        /// The casino's own camera for a seat: a quarter of a metre out from the chair and just
        /// above your head, looking down sixty degrees at the felt. It was behind and above your
        /// head until 2026-09-26, and your head filled the middle of the shot.
        /// </summary>
        private void TableCam()
        {
            if (_sitter == null)
            {
                var centre = _table.GetOffsetPosition(new Vector3(0f, 0.12f, 0.95f));
                _cam.Look(Behind(centre, 0.3f, 1.7f), centre, 58f, 900, 0.12f);
                return;
            }

            var fwd = SeatForward();
            var from = _sitter.Seat.At + fwd * 0.245f + new Vector3(0f, 0f, 1.415f);
            var at = from + fwd * 0.487f - new Vector3(0f, 0f, 0.873f);
            _cam.Look(from, at, 55f, 900, 0.12f);
        }

        /// <summary>Which way the chair faces: whichever of its bone's axes points at the table.</summary>
        private Vector3 SeatForward()
        {
            var seat = _sitter.Seat;
            var h = seat.Rot.Z * (float)Math.PI / 180f;
            var x = new Vector3((float)Math.Cos(h), (float)Math.Sin(h), 0f);
            var y = new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
            var best = Vector3.Dot(x, seat.Facing) >= Vector3.Dot(y, seat.Facing) ? x : y;
            return Vector3.Dot(best, seat.Facing) < 0.3f ? seat.Facing : best;
        }

        /// <summary>Down on her three, from your side of the felt.</summary>
        private void DealerCam()
        {
            var look = _table.GetOffsetPosition(new Vector3(0f, 0.12f, 0.95f));
            _cam.Look(Toward(look, 0.3f, 0.55f), look, 50f, 700, 0.1f);
        }

        /// <summary>Down on your three as she turns them over at your seat.</summary>
        private void SeatCam()
        {
            var i = Math.Max(0, Math.Min(3, _chair - 1));
            var look = _table.GetOffsetPosition(AnteSpot[i]);
            _cam.Look(Toward(look, 0.22f, 0.5f), look, 50f, 700, 0.1f);
        }

        /// <summary>Over your shoulder, high enough to clear your head, on the cards in your hand.</summary>
        private void HandCam()
        {
            try
            {
                var head = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, _me.Handle, 31086, 0f, 0f, 0f);
                var card = _myCards[1] != null && _myCards[1].Exists() ? _myCards[1].Position : head + Facing() * 0.35f - new Vector3(0f, 0f, 0.3f);
                var from = head + new Vector3(0f, 0f, 0.5f) - Facing() * 0.05f;
                _cam.Look(from, card, 45f, 800, 0.1f);
            }
            catch
            {
                TableCam();
            }
        }

        private Vector3 Facing()
        {
            if (_sitter != null) return _sitter.Seat.Facing;

            var f = _me.ForwardVector;
            f.Z = 0f;
            if (f.Length() > 0.01f) f.Normalize();
            return f;
        }

        /// <summary>A point on your side of something on the table, raised: where a camera looking at it from your seat goes.</summary>
        private Vector3 Toward(Vector3 look, float back, float up)
        {
            var side = _sitter != null ? _sitter.Seat.At : _me.Position;
            var dir = side - look;
            dir.Z = 0f;

            if (dir.Length() < 0.05f) dir = -_table.ForwardVector;
            dir.Normalize();

            return look + dir * back + new Vector3(0f, 0f, up);
        }

        /// <summary>Behind and above your chair, looking over you at a point.</summary>
        private Vector3 Behind(Vector3 look, float back, float up)
        {
            var seat = _sitter != null ? _sitter.Seat.At : _me.Position;
            var dir = seat - look;
            dir.Z = 0f;

            if (dir.Length() < 0.05f) dir = -_table.ForwardVector;
            dir.Normalize();

            var at = seat + dir * back;
            at.Z = _table.Position.Z + up;
            return at;
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

        /// <summary>Everything the game put on the table off it, the deck included, and the cameras back.</summary>
        private void Tidy()
        {
            Clear();
            Gone(_myCards);
            Gone(_herCards);
            _cues.Clear();

            if (_packHeld)
            {
                try { if (_pack != null && _pack.Exists()) Function.Call(Hash.DETACH_ENTITY, _pack.Handle, true, true); }
                catch { }

                _packHeld = false;
            }

            Chips.Gone(_pack);
            _pack = null;

            _dealer.Drop();
            Sfx.SceneOff("DLC_VW_Casino_Cards_Focus_Hand");
            _cam.Stop();
            Hud(true);
        }

        /// <summary>Straight out, whatever state it is in. A hand not yet settled is called off and its stakes come back.</summary>
        public void Abandon()
        {
            if (_stage == Stage.Done) return;

            if (_inPlay)
            {
                Game.Player.Money += _anteBet + _plusBet + _playBet;
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
            Log.Info("Den: up from the poker.");
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

        private void Draw()
        {
            var cash = "$" + Game.Player.Money.ToString("N0");
            var saying = _said.Length > 0 && Game.GameTime < _saidUntil;

            switch (_stage)
            {
                case Stage.Betting:
                    Help.ShowThisFrame(saying ? _said : "Ante up. Pair Plus pays on your hand alone.");

                    Bars.Draw("CASH", cash, Color.White,
                              "PAIR PLUS", _plus > 0 ? "$" + _plus.ToString("N0") : "OFF", _field == 1 ? Gold : Color.White,
                              "ANTE", "$" + _ante.ToString("N0"), _field == 0 ? Gold : Color.White);

                    Buttons.Show(Control.PhoneCancel, "Stand up",
                                 Control.PhoneSelect, "Deal",
                                 Control.PhoneRight, "Raise",
                                 Control.PhoneLeft, "Lower",
                                 Control.PhoneUp, _field == 0 ? "Pair Plus" : "Ante");
                    break;

                case Stage.Deciding:
                    Help.ShowThisFrame(saying ? _said
                                     : "You have " + HandNames[Rank(_mine).Cat] + ": " + Hand(_mine) + ". Play $" + _anteBet.ToString("N0") + ", or fold.");

                    Bars.Draw("CASH", cash, Color.White,
                              "PAIR PLUS", _plusBet > 0 ? "$" + _plusBet.ToString("N0") : "OFF", Color.White,
                              "ANTE", "$" + _anteBet.ToString("N0"), Gold);

                    Buttons.Show(Control.PhoneCancel, "Fold",
                                 Control.PhoneSelect, "Play");
                    break;

                case Stage.Result:
                case Stage.Collecting:
                    if (saying) Help.ShowThisFrame(_said);

                    Bars.Draw("CASH", cash, Color.White,
                              _net > 0 ? "WON" : _net == 0 ? "PUSH" : "LOST", "$" + Math.Abs(_net).ToString("N0"),
                              _net > 0 ? Green : _net == 0 ? Gold : Red);
                    break;

                case Stage.Sitting:
                case Stage.Standing:
                    break;

                default:
                    Bars.Draw("CASH", cash, Color.White,
                              "ON THE TABLE", "$" + (_anteBet + _plusBet + _playBet).ToString("N0"), Gold);
                    break;
            }
        }
    }
}
