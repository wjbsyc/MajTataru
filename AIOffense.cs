using System;
using System.Collections.Generic;

namespace MajTataru
{
    public enum Strategy { General, Chiitoitsu, ThirteenOrphans, Fold }

    public class TilePriority
    {
        public MahjongTile Tile;
        public double Priority;
        public double RiichiPriority;
        public int Shanten;
        public double Efficiency;
        public double ScoreOpen;
        public double ScoreClosed;
        public double ScoreRiichi;
        public double DoraVal;
        public double YakuOpen;
        public double YakuClosed;
        public double Waits;
        public double Shape;
        public double Danger;
        public double Fu;
        public bool Safe;
    }

    public class CallAdvice
    {
        public string Type;
        public bool Available;
        public bool Recommended;
        public MahjongTile CalledTile;
        public List<MahjongTile> UsedTiles;
        public MahjongTile? BestDiscard;
        public string Reason;
        public double ScoreEstimate;
        public int ShantenBefore;
        public int ShantenAfter;
    }

    /// <summary>
    /// 进攻AI。移植自 ai_offense.js 的 getHandValues / getTilePriorities / discard 等核心决策逻辑。
    /// </summary>
    public class AIOffense
    {
        private readonly AIGameState _gs;
        private readonly AIDefense _defense;

        public double ParamEfficiency = 1.0;
        public double ParamSafety = 1.0;
        public double ParamRiichi = 1.0;
        public int ParamChiitoitsu = 5;
        public int ParamThirteenOrphans = 10;
        public double ParamCallPonChi = 1.0;
        public double ParamCallKan = 1.0;

        public AIOffense(AIGameState gs, AIDefense defense)
        {
            _gs = gs;
            _defense = defense;
        }

        public Strategy DetermineStrategy()
        {
            if (_gs.CurrentStrategy == Strategy.Fold) return Strategy.Fold;

            var handWithCalls = new List<MahjongTile>(_gs.OwnHand);
            handWithCalls.AddRange(_gs.Calls[0]);

            int handTriples = ShantenCalculator.GetTriplesOnly(handWithCalls).Count / 3;
            int pairs = ShantenCalculator.GetPairsAsArray(_gs.OwnHand).Count / 2;

            if ((pairs == 6 || (pairs >= ParamChiitoitsu && handTriples < 2)) && _gs.IsClosed)
            {
                _gs.StrategyAllowsCalls = false;
                return Strategy.Chiitoitsu;
            }

            if (CanDoThirteenOrphans())
            {
                _gs.StrategyAllowsCalls = false;
                return Strategy.ThirteenOrphans;
            }

            _gs.StrategyAllowsCalls = true;
            return Strategy.General;
        }

        public List<TilePriority> GetTilePriorities()
        {
            switch (_gs.CurrentStrategy)
            {
                case Strategy.Chiitoitsu:
                    return ChiitoitsuPriorities();
                case Strategy.ThirteenOrphans:
                    return ThirteenOrphansPriorities();
                default:
                    return GeneralPriorities();
            }
        }

        #region General priorities (two-turn simulation)

        private List<TilePriority> GeneralPriorities()
        {
            var hand = _gs.OwnHand;
            var results = new List<TilePriority>();
            var seen = new HashSet<long>();

            for (int i = 0; i < hand.Count; i++)
            {
                long key = hand[i].Type * 100 + hand[i].Index + (hand[i].Dora ? 10000 : 0);
                if (seen.Contains(key)) continue;
                seen.Add(key);

                var newHand = new List<MahjongTile>(hand);
                newHand.RemoveAt(i);
                results.Add(GetHandValues(newHand, hand[i]));
            }

            results.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return results;
        }

        private TilePriority GetHandValues(List<MahjongTile> hand, MahjongTile? discardedTile = null)
        {
            int callTriples = ShantenCalculator.GetTriplesOnly(_gs.Calls[0]).Count / 3;

            var tap = ShantenCalculator.GetTriplesAndPairs(hand);
            var triples = tap.Triples;
            var pairs = tap.Pairs;
            var doubles = ShantenCalculator.GetDoubles(TileUtils.Remove(hand, new List<MahjongTile>(triples).Also(pairs)));
            int baseShanten = ShantenCalculator.Calculate(triples.Count / 3 + callTriples, pairs.Count / 2, doubles.Count / 2);

            int originalShanten = baseShanten;
            if (discardedTile.HasValue)
            {
                var hand14 = new List<MahjongTile>(hand) { discardedTile.Value };
                var origTap = ShantenCalculator.GetTriplesAndPairs(hand14);
                var origDoubles = ShantenCalculator.GetDoubles(TileUtils.Remove(hand14,
                    new List<MahjongTile>(origTap.Triples).Also(origTap.Pairs)));
                originalShanten = ShantenCalculator.Calculate(origTap.Triples.Count / 3 + callTriples, origTap.Pairs.Count / 2, origDoubles.Count / 2);
            }

            double shanten = 8;
            double expOpen = 0, expClosed = 0, expRiichi = 0;
            double yakuOpen = 0, yakuClosed = 0;
            double doraVal = 0, waits = 0, shape = 0, fu = 0;
            int totalCombs = 0, totalWaitCombs = 0;

            var vis = _gs.VisibleTiles;
            var avail = _gs.AvailableTiles;
            int availCount = avail.Count;
            if (availCount < 2) availCount = 2;
            bool isChii = _gs.CurrentStrategy == Strategy.Chiitoitsu;
            bool isClosed = _gs.IsClosed;
            bool isDealer = _gs.SeatWind == 1;

            var newTiles1 = TileUtils.GetUsefulTilesForDouble(hand, _gs.DoraIndicators);

            var tileCombinations = new List<TileCombData>();
            foreach (var nt in newTiles1)
            {
                int n1 = TileUtils.GetNumberOfTilesAvailable(nt.Index, nt.Type, vis);
                if (n1 <= 0) continue;

                var h2 = new List<MahjongTile>(hand) { nt };
                var nt2list = TileUtils.GetUsefulTilesForDouble(h2, _gs.DoraIndicators);

                var t2objs = new List<Tile2Data>();
                foreach (var t2 in nt2list)
                {
                    if (TileUtils.GetNumberOfTilesAvailable(t2.Index, t2.Type, vis) <= 0) continue;
                    bool skip = false;
                    foreach (var tc in tileCombinations)
                    {
                        if (!tc.Tile1.IsSame(t2)) continue;
                        foreach (var td in tc.Tiles2)
                        {
                            if (td.Tile.IsSame(nt)) { td.Duplicate = true; skip = true; break; }
                        }
                        if (skip) break;
                    }
                    t2objs.Add(new Tile2Data { Tile = t2, Skip = skip });
                }
                tileCombinations.Add(new TileCombData { Tile1 = nt, Tiles2 = t2objs });
            }

            // Step 2: check winning for tile1 draws
            var waitTiles = new List<MahjongTile>();
            foreach (var tc in tileCombinations)
            {
                var h2 = new List<MahjongTile>(hand) { tc.Tile1 };
                var tap2 = ShantenCalculator.GetTriplesAndPairs(h2);
                bool win = ShantenCalculator.IsWinning(tap2.Triples.Count / 3 + callTriples, tap2.Pairs.Count / 2, isChii);
                if (win)
                {
                    waitTiles.Add(tc.Tile1);
                    foreach (var oc in tileCombinations)
                        foreach (var td in oc.Tiles2)
                            if (tc.Tile1.IsSame(td.Tile)) { td.Duplicate = false; td.Skip = false; }
                }
                bool furi = win && IsTileFuriten(tc.Tile1, discardedTile);
                tc.Winning = win;
                tc.Furiten = furi;
                tc.TAP = tap2;
            }

            bool tile1Furiten = false;
            foreach (var tc in tileCombinations) if (tc.Furiten) { tile1Furiten = true; break; }

            // Step 2b: check tile2 winning
            foreach (var tc in tileCombinations)
            {
                var h2 = new List<MahjongTile>(hand) { tc.Tile1 };
                foreach (var t2d in tc.Tiles2)
                {
                    if (t2d.Skip || (tc.Winning && !tile1Furiten)) continue;
                    var h3 = new List<MahjongTile>(h2) { t2d.Tile };
                    var tap3 = ShantenCalculator.GetTriplesAndPairs(h3);
                    bool w2 = ShantenCalculator.IsWinning(tap3.Triples.Count / 3 + callTriples, tap3.Pairs.Count / 2, isChii);
                    bool f2 = w2 && IsTileFuriten(t2d.Tile, discardedTile);
                    t2d.Winning = w2;
                    t2d.Furiten = f2;
                    t2d.TAP = tap3;
                }
            }

            // Step 3: calculate values
            foreach (var tc in tileCombinations)
            {
                int n1 = TileUtils.GetNumberOfTilesAvailable(tc.Tile1.Index, tc.Tile1.Type, vis);
                var h2 = new List<MahjongTile>(hand) { tc.Tile1 };
                var tap2 = tc.TAP;
                var tr2 = tap2.Triples;
                var pa2 = tap2.Pairs;

                if (!isClosed && !tc.Winning && TileUtils.Count(tr2, tc.Tile1.Index, tc.Tile1.Type) == 3)
                    n1 *= 2;

                double factor;
                double thisShanten;

                if (tc.Winning && !tile1Furiten)
                {
                    int usefulSum = 0;
                    foreach (var oc in tileCombinations)
                        usefulSum += TileUtils.GetNumberOfTilesAvailable(oc.Tile1.Index, oc.Tile1.Type, vis);
                    factor = n1 * (availCount - 1) + (availCount - usefulSum) * n1;
                    thisShanten = -1 - baseShanten;
                }
                else
                {
                    var d2 = ShantenCalculator.GetDoubles(TileUtils.Remove(h2, new List<MahjongTile>(tr2).Also(pa2)));
                    int usefulT2 = 0;
                    foreach (var t2d in tc.Tiles2)
                    {
                        int av = TileUtils.GetNumberOfTilesAvailable(t2d.Tile.Index, t2d.Tile.Type, vis);
                        if (tc.Tile1.IsSame(t2d.Tile)) av = Math.Max(0, av - 1);
                        usefulT2 += av;
                    }
                    factor = n1 * ((availCount - 1) - usefulT2);
                    thisShanten = tile1Furiten ? (0 - baseShanten) :
                        ShantenCalculator.Calculate(tr2.Count / 3 + callTriples, pa2.Count / 2, d2.Count / 2) - baseShanten;
                }

                shanten += thisShanten * factor;

                if (tc.Winning)
                {
                    double thisDora = TileUtils.TotalDora(new List<MahjongTile>(tr2).Also(pa2).Also(_gs.Calls[0]));
                    var thisYaku = YakuEvaluator.GetYaku(h2, _gs.Calls[0], tap2, isChii, _gs.SeatWind, _gs.RoundWind);
                    double thisWait = n1 * _defense.GetWaitQuality(tc.Tile1);
                    var waitRemaining = TileUtils.Remove(h2, new List<MahjongTile>(triples).Also(pairs).Also(new List<MahjongTile> { tc.Tile1 }));
                    double thisFu = ScoreCalculator.CalculateFu(tr2, _gs.Calls[0], pa2, waitRemaining.Count > 0 ? waitRemaining : null, tc.Tile1, isClosed, _gs.SeatWind, _gs.RoundWind);

                    if (isClosed || thisYaku.Open >= 1 || _gs.TilesLeft <= 4)
                    {
                        if (tile1Furiten && _gs.TilesLeft > 4) thisWait = n1 / 6.0;
                        waits += thisWait;
                        fu += thisFu * thisWait * factor;
                        if (thisFu == 30 && isClosed) thisYaku = new YakuResult(thisYaku.Open, thisYaku.Closed + 1);
                        doraVal += thisDora * factor;
                        yakuOpen += thisYaku.Open * factor;
                        yakuClosed += thisYaku.Closed * factor;
                        expOpen += ScoreCalculator.CalculateScore(isDealer, thisYaku.Open + thisDora, thisFu) * factor;
                        expClosed += ScoreCalculator.CalculateScore(isDealer, thisYaku.Closed + thisDora, thisFu) * factor;
                        totalCombs += (int)factor;
                    }
                    double uraChance = _gs.DoraIndicators.Count * 0.4;
                    expRiichi += ScoreCalculator.CalculateScore(isDealer, thisYaku.Closed + thisDora + 1 + 0.2 + uraChance, thisFu) * thisWait * factor;
                    totalWaitCombs += (int)(factor * thisWait);

                    if (!tile1Furiten) continue;
                }

                bool t2Furiten = false;
                foreach (var t2d in tc.Tiles2) if (t2d.Furiten) { t2Furiten = true; break; }

                foreach (var t2d in tc.Tiles2)
                {
                    if (t2d.Skip) continue;
                    int n2 = TileUtils.GetNumberOfTilesAvailable(t2d.Tile.Index, t2d.Tile.Type, vis);
                    if (tc.Tile1.IsSame(t2d.Tile)) { if (n2 <= 1) continue; n2--; }

                    double combFactor = n1 * n2;
                    if (t2d.Duplicate) combFactor *= 2;

                    var h3 = new List<MahjongTile>(h2) { t2d.Tile };
                    var tap3 = t2d.TAP ?? ShantenCalculator.GetTriplesAndPairs(h3);
                    var tr3 = tap3.Triples;
                    var pa3 = tap3.Pairs;

                    if (!isClosed && (!t2d.Winning || t2Furiten) && TileUtils.Count(tr3, t2d.Tile.Index, t2d.Tile.Type) == 3)
                        combFactor *= 2;

                    double ts;
                    var ty3 = YakuEvaluator.GetYaku(h3, _gs.Calls[0], tap3, isChii, _gs.SeatWind, _gs.RoundWind);
                    double td3 = TileUtils.TotalDora(new List<MahjongTile>(tr3).Also(pa3).Also(_gs.Calls[0]));

                    if (t2d.Winning && !t2Furiten)
                    {
                        ts = -1 - baseShanten;
                        bool alreadyWait = false;
                        foreach (var wt in waitTiles) if (wt.IsSame(t2d.Tile)) { alreadyWait = true; break; }
                        if (!alreadyWait)
                        {
                            double ns = n2 * _defense.GetWaitQuality(t2d.Tile) * ((double)n1 / availCount);
                            if (t2d.Duplicate)
                                ns += n1 * _defense.GetWaitQuality(tc.Tile1) * ((double)n2 / availCount);
                            shape += ns;
                        }
                        if (isClosed)
                        {
                            var rem = TileUtils.Remove(h3, new List<MahjongTile>(tr3).Also(pa3));
                            var secondDiscard = rem.Count > 0 ? rem[0] : t2d.Tile;
                            if (!t2d.Duplicate)
                            {
                                var waitR = TileUtils.Remove(h3, new List<MahjongTile>(triples).Also(pairs).Also(new List<MahjongTile> { t2d.Tile, secondDiscard }));
                                double nf = ScoreCalculator.CalculateFu(tr3, _gs.Calls[0], pa3, waitR.Count > 0 ? waitR : null, t2d.Tile, isClosed, _gs.SeatWind, _gs.RoundWind);
                                if (nf == 30) ty3 = new YakuResult(ty3.Open, ty3.Closed + 1);
                            }
                            else
                            {
                                var waitR1 = TileUtils.Remove(h3, new List<MahjongTile>(triples).Also(pairs).Also(new List<MahjongTile> { t2d.Tile, secondDiscard }));
                                double nf1 = ScoreCalculator.CalculateFu(tr3, _gs.Calls[0], pa3, waitR1.Count > 0 ? waitR1 : null, t2d.Tile, isClosed, _gs.SeatWind, _gs.RoundWind);
                                var waitR2 = TileUtils.Remove(h3, new List<MahjongTile>(triples).Also(pairs).Also(new List<MahjongTile> { tc.Tile1, secondDiscard }));
                                double nf2 = ScoreCalculator.CalculateFu(tr3, _gs.Calls[0], pa3, waitR2.Count > 0 ? waitR2 : null, tc.Tile1, isClosed, _gs.SeatWind, _gs.RoundWind);
                                double pinfu = 0;
                                if (nf1 == 30) pinfu += 0.5;
                                if (nf2 == 30) pinfu += 0.5;
                                if (pinfu > 0) ty3 = new YakuResult(ty3.Open, ty3.Closed + pinfu);
                            }
                        }
                    }
                    else if (t2d.Winning && (t2Furiten || (!isClosed && ty3.Open < 1)))
                    {
                        ts = 0 - baseShanten;
                    }
                    else
                    {
                        var d3 = ShantenCalculator.GetDoubles(TileUtils.Remove(h3, new List<MahjongTile>(tr3).Also(pa3)));
                        ts = ShantenCalculator.Calculate(tr3.Count / 3 + callTriples, pa3.Count / 2, d3.Count / 2) - baseShanten;
                        if (ts == -1) ts = -0.5;
                    }

                    shanten += ts * combFactor;

                    if (t2d.Winning || ts < 0)
                    {
                        doraVal += td3 * combFactor;
                        yakuOpen += ty3.Open * combFactor;
                        yakuClosed += ty3.Closed * combFactor;
                        expOpen += ScoreCalculator.CalculateScore(isDealer, ty3.Open + td3) * combFactor;
                        expClosed += ScoreCalculator.CalculateScore(isDealer, ty3.Closed + td3) * combFactor;
                        totalCombs += (int)combFactor;
                    }
                }
            }

            double allCombs = availCount * (availCount - 1);
            if (allCombs < 1) allCombs = 1;
            shanten /= allCombs;

            if (totalCombs > 0) { expOpen /= totalCombs; expClosed /= totalCombs; doraVal /= totalCombs; yakuOpen /= totalCombs; yakuClosed /= totalCombs; }
            if (totalWaitCombs > 0) { expRiichi /= totalWaitCombs; fu /= totalWaitCombs; }
            if (waitTiles.Count > 0) waits *= waitTiles.Count * 0.15 + 0.75;
            fu = fu <= 30 ? 30 : fu;
            if (fu > 110) fu = 30;

            double efficiency = (shanten + (baseShanten - originalShanten)) * -1;
            if (originalShanten == 0)
                efficiency = baseShanten == 0 ? (waits + shape) / 10.0 : (shanten / 1.7) * -1;

            if (baseShanten > 0)
            {
                double ura = _gs.DoraIndicators.Count * 0.4;
                expRiichi = ScoreCalculator.CalculateScore(isDealer, yakuClosed + doraVal + 1 + 0.2 + ura);
            }

            double danger = 0;
            double sakigiri = 0;
            if (discardedTile.HasValue)
            {
                danger = _defense.GetTileDanger(discardedTile.Value);
                sakigiri = _defense.GetSakigiriValue(hand, discardedTile.Value);
            }
            double priority = CalculatePriority(efficiency, expOpen, expClosed, danger - sakigiri);
            double riichiPri = 0;
            if (originalShanten == 0)
            {
                double re = waits / 10.0;
                riichiPri = CalculatePriority(re, expOpen, expClosed, danger - sakigiri);
            }

            return new TilePriority
            {
                Tile = discardedTile ?? new MahjongTile(-1, 0), Priority = priority, RiichiPriority = riichiPri,
                Shanten = baseShanten, Efficiency = efficiency,
                ScoreOpen = expOpen, ScoreClosed = expClosed, ScoreRiichi = expRiichi,
                DoraVal = doraVal, YakuOpen = yakuOpen, YakuClosed = yakuClosed,
                Waits = waits, Shape = shape, Danger = danger, Fu = fu
            };
        }

        #endregion

        #region Chiitoitsu priorities

        private List<TilePriority> ChiitoitsuPriorities()
        {
            var hand = _gs.OwnHand;
            var origPairs = ShantenCalculator.GetPairsAsArray(hand);
            int origShanten = 6 - origPairs.Count / 2;
            bool isDealer = _gs.SeatWind == 1;
            var vis = _gs.VisibleTiles;
            var results = new List<TilePriority>();
            var seen = new HashSet<long>();

            for (int i = 0; i < hand.Count; i++)
            {
                long key = hand[i].Type * 100 + hand[i].Index + (hand[i].Dora ? 10000 : 0);
                if (seen.Contains(key)) continue; seen.Add(key);

                var nh = new List<MahjongTile>(hand);
                nh.RemoveAt(i);
                var pa = ShantenCalculator.GetPairsAsArray(nh);
                int pv = pa.Count / 2;
                var hwp = TileUtils.Remove(nh, pa);
                double baseDora = TileUtils.TotalDora(pa);
                int bs = 6 - pv;
                double w = 0, sh = 0, shape2 = 0;
                double yO = 0, yC = 0;
                var baseYaku = YakuEvaluator.GetYaku(nh, _gs.Calls[0], null, true, _gs.SeatWind, _gs.RoundWind);

                foreach (var tile in hwp)
                {
                    var ch = new List<MahjongTile>(hwp) { tile };
                    int nt = GetNonFuritenAvail(tile);
                    double chance = (nt + _defense.GetWaitQuality(tile) / 10.0) / Math.Max(_gs.AvailableTiles.Count, 1);
                    var p2 = ShantenCalculator.GetPairsAsArray(ch);
                    if (p2.Count > 0)
                    {
                        sh += ((6 - (pv + p2.Count / 2)) - bs) * chance;
                        baseDora += TileUtils.TotalDora(p2) * chance;
                        var y2 = YakuEvaluator.GetYaku(new List<MahjongTile>(ch).Also(pa), _gs.Calls[0], null, true, _gs.SeatWind, _gs.RoundWind);
                        yO += (y2.Open - baseYaku.Open) * chance;
                        yC += (y2.Closed - baseYaku.Closed) * chance;
                        if (pv + p2.Count / 2 == 7)
                        {
                            w = nt * _defense.GetWaitQuality(tile);
                            baseDora = TileUtils.TotalDora(p2);
                            bool isTanyao = true;
                            foreach (var ct in ch)
                                if (ct.Type == 3 || ct.Index == 1 || ct.Index == 9) { isTanyao = false; break; }
                            if (tile.Index < 3 || tile.Index > 7 || tile.DoraValue > 0 ||
                                _defense.GetWaitQuality(tile) > 1.1 || isTanyao)
                                shape2 = 1;
                        }
                    }
                }
                yO += baseYaku.Open; yC += baseYaku.Closed + 2;

                double sc = ScoreCalculator.CalculateScore(isDealer, yC + baseDora, 25);
                double sr = ScoreCalculator.CalculateScore(isDealer, yC + baseDora + 1 + 0.2 + _gs.DoraIndicators.Count * 0.4, 25);
                double eff = (sh + (bs - origShanten)) * -1;
                if (origShanten == 0) eff = w / 10.0;
                double dng = _defense.GetTileDanger(hand[i]);
                double sak = _defense.GetSakigiriValue(nh, hand[i]);
                double pri = CalculatePriority(eff, 1000, sc, dng - sak);

                results.Add(new TilePriority
                {
                    Tile = hand[i], Priority = pri, RiichiPriority = pri,
                    Shanten = bs, Efficiency = eff,
                    ScoreOpen = 1000, ScoreClosed = sc, ScoreRiichi = sr,
                    DoraVal = baseDora, YakuOpen = yO, YakuClosed = yC,
                    Waits = w, Shape = shape2, Danger = dng, Fu = 25
                });
            }
            results.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return results;
        }

        #endregion

        #region Thirteen Orphans priorities

        private List<TilePriority> ThirteenOrphansPriorities()
        {
            var hand = _gs.OwnHand;
            var origTH = GetUniqueTerminals(hand);
            var allTH = TileUtils.GetAllTerminalHonor(hand);
            int origShanten = 13 - origTH.Count;
            if (allTH.Count > origTH.Count) origShanten--;
            bool isDealer = _gs.SeatWind == 1;

            var results = new List<TilePriority>();
            for (int i = 0; i < hand.Count; i++)
            {
                var nh = new List<MahjongTile>(hand); nh.RemoveAt(i);
                var uth = GetUniqueTerminals(nh);
                var ath = TileUtils.GetAllTerminalHonor(nh);
                int s = 13 - uth.Count;
                if (ath.Count > uth.Count) s--;
                double w = 0;
                if (s == 0)
                {
                    var missing = GetMissingOrphans(uth);
                    if (missing.Count > 0) w = GetNonFuritenAvail(missing[0]);
                }
                double eff = s == origShanten ? 1 : 0;
                double dng = _defense.GetTileDanger(hand[i]);
                double sak = _defense.GetSakigiriValue(nh, hand[i]);
                double yakuman = ScoreCalculator.CalculateScore(isDealer, 13);
                double pri = CalculatePriority(eff, 0, yakuman, dng - sak);

                results.Add(new TilePriority
                {
                    Tile = hand[i], Priority = pri, RiichiPriority = pri,
                    Shanten = s, Efficiency = eff,
                    ScoreOpen = 0, ScoreClosed = yakuman, ScoreRiichi = yakuman,
                    DoraVal = TileUtils.TotalDora(nh), YakuOpen = 13, YakuClosed = 13,
                    Waits = w, Shape = 0, Danger = dng, Fu = 30
                });
            }
            results.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return results;
        }

        private bool CanDoThirteenOrphans()
        {
            if (!_gs.IsClosed) return false;
            var uth = GetUniqueTerminals(_gs.OwnHand);
            if (uth.Count < ParamThirteenOrphans) return false;
            var missing = GetMissingOrphans(uth);
            int maxMissing = missing.Count == 1 ? 3 : 2;
            foreach (var m in missing)
                if (4 - GetNonFuritenAvail(m) > maxMissing) return false;
            return true;
        }

        #endregion

        #region Safety / Riichi / Fold

        public void SortOutUnsafeTiles(List<TilePriority> tiles)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                bool highP = (i == 0);
                tiles[i].Safe = !ShouldFold(tiles[i], highP);
            }
            tiles.Sort((a, b) => b.Safe.CompareTo(a.Safe));
        }

        public bool ShouldFold(TilePriority tp, bool highestPrio = false)
        {
            if (tp.Shanten * 4 > _gs.TilesLeft)
            {
                if (highestPrio) { _gs.CurrentStrategy = Strategy.Fold; _gs.StrategyAllowsCalls = false; }
                return true;
            }
            double threshold = GetFoldThreshold(tp);
            if (tp.Danger > threshold)
            {
                if (highestPrio) _gs.StrategyAllowsCalls = false;
                return true;
            }
            return false;
        }

        public double GetFoldThreshold(TilePriority tp)
        {
            double handScore = _gs.IsClosed ? tp.ScoreRiichi : tp.ScoreOpen * 1.3;
            double foldValue;

            if (tp.Shanten == 0)
            {
                foldValue = (tp.Waits + tp.Shape) * handScore / 38;
                if (_gs.TilesLeft < 8) foldValue += 200 - (_gs.TilesLeft / 4 * 100);
            }
            else if (tp.Shanten == 1 && _gs.CurrentStrategy == Strategy.General)
            {
                double s = tp.Shape < 0.4 ? 0.4 : tp.Shape;
                s = s > 2 ? 2 : s;
                foldValue = s * handScore / 45;
            }
            else
            {
                if (_defense.GetCurrentDangerLevel() > 3000 && _gs.CurrentStrategy == Strategy.General)
                    return 0;
                foldValue = (((6 - (tp.Shanten - tp.Efficiency)) * 2000) + handScore) / 500;
            }

            if (_gs.IsLastGame())
            {
                if (_gs.GetDistanceToLast() > 0)
                    foldValue *= 1.3;
                else if (_gs.GetDistanceToFirst() < 0)
                {
                    double dist = _gs.GetDistanceToFirst() / 30000.0;
                    if (dist < -0.5) dist = -0.5;
                    foldValue *= 1 + dist;
                }
            }

            double wallSize = 70;
            foldValue *= 1.0 - ((wallSize / 2 - _gs.TilesLeft) / (wallSize * 2));
            if (_gs.SeatWind == 1) foldValue *= 1.2;

            int safeTiles = 0;
            foreach (var t in _gs.OwnHand)
                if (_defense.GetTileDanger(t) < 20 && ++safeTiles == 2) break;
            foldValue *= 1 + (0.5 - safeTiles / 4.0);
            foldValue *= 2.0 - _gs.OwnHand.Count / 14.0;
            foldValue /= ParamSafety;

            return foldValue < 0 ? 0 : foldValue;
        }

        public bool ShouldRiichi(TilePriority tp)
        {
            bool badWait = tp.Waits < 5 - ParamRiichi;
            bool lotsDora = _gs.DoraIndicators.Count >= 3;

            if (_gs.CurrentStrategy == Strategy.Chiitoitsu)
            {
                if (tp.Shape == 0) return false;
                badWait = tp.Waits < 3 - ParamRiichi;
            }
            if (_gs.CurrentStrategy == Strategy.ThirteenOrphans) return false;
            if (_gs.TilesLeft <= 7 - ParamRiichi) return false;
            if (tp.Waits < 1) return false;

            if (_gs.IsLastGame() && _gs.GetDistanceToLast() > 0 &&
                _gs.GetDistanceToLast() < tp.ScoreRiichi)
                return true;

            if (_gs.IsLastGame() && (_gs.GetDistanceToFirst() < -10000 ||
                (tp.YakuClosed >= 1 && _gs.GetDistanceToFirst() < 0)))
                return false;

            bool isDealer = _gs.SeatWind == 1;
            if (!isDealer && badWait && tp.ScoreRiichi < 4000 - ParamRiichi * 1000 && !lotsDora && tp.Shape > 0.4)
                return false;

            if (tp.ScoreRiichi < (_defense.GetCurrentDangerLevel() - ParamRiichi * 1000) * (1 + (badWait ? 1 : 0)))
                return false;

            if (tp.YakuClosed >= 1 && tp.ScoreClosed / (isDealer ? 1.5 : 1) > 4000 + ParamRiichi * 1000 + tp.Waits * 500)
                return false;

            if (tp.YakuClosed < 0.9 && tp.ScoreRiichi > 5000 - ParamRiichi * 1000)
                return true;

            if (lotsDora) return true;

            if (_gs.IsLastGame() && badWait &&
                ((_gs.GetDistanceToPlayer(1) >= -1000 && _gs.GetDistanceToPlayer(1) <= 0) ||
                 (_gs.GetDistanceToPlayer(2) >= -1000 && _gs.GetDistanceToPlayer(2) <= 0) ||
                 (_gs.GetDistanceToPlayer(3) >= -1000 && _gs.GetDistanceToPlayer(3) <= 0)))
                return false;

            return true;
        }

        #endregion

        #region Call Evaluation

        private struct HandMetrics
        {
            public int Shanten;
            public double ScoreOpen;
            public double ScoreClosed;
            public double YakuOpen;
            public double YakuClosed;
            public double Dora;
            public double Waits;
            public int Pairs;
            public int HonorPairs;
            public int ValueHonorPairsWithAvail;
        }

        public List<CallAdvice> EvaluateCalls(MahjongTile discardedTile, int discardingPlayer)
        {
            var advices = new List<CallAdvice>();
            if (_gs.OwnHand.Count < 4) return advices;

            var ron = CheckRon(discardedTile);
            if (ron != null)
            {
                advices.Add(ron);
                return advices;
            }

            int sameCount = TileUtils.Count(_gs.OwnHand, discardedTile.Index, discardedTile.Type);
            if (sameCount >= 2)
            {
                var tiles = TileUtils.GetTiles(_gs.OwnHand, discardedTile.Index, discardedTile.Type);
                advices.Add(SimulateCallTriple(discardedTile, new List<MahjongTile> { tiles[0], tiles[1] }, "碰"));
            }

            if (discardingPlayer == 3 && discardedTile.Type != 3)
            {
                var combos = FindChiCombinations(discardedTile);
                foreach (var combo in combos)
                    advices.Add(SimulateCallTriple(discardedTile, combo, "吃"));
            }

            if (sameCount >= 3)
                advices.Add(SimulateDaiminkan(discardedTile));

            return advices;
        }

        private CallAdvice CheckRon(MahjongTile discardedTile)
        {
            var hand14 = new List<MahjongTile>(_gs.OwnHand) { discardedTile };
            int callTriples = ShantenCalculator.GetTriplesOnly(_gs.Calls[0]).Count / 3;

            var tap = ShantenCalculator.GetTriplesAndPairs(hand14);
            bool winning = ShantenCalculator.IsWinning(tap.Triples.Count / 3 + callTriples, tap.Pairs.Count / 2);

            bool isChii = false;
            if (!winning && _gs.IsClosed)
            {
                var pairs = ShantenCalculator.GetPairsAsArray(hand14);
                if (pairs.Count / 2 >= 7) { winning = true; isChii = true; }
            }

            if (!winning) return null;
            if (IsInFuriten()) return null;

            var yaku = YakuEvaluator.GetYaku(hand14, _gs.Calls[0], tap, isChii, _gs.SeatWind, _gs.RoundWind);
            double yakuVal = _gs.IsClosed ? yaku.Closed : yaku.Open;
            if (isChii) yakuVal += 2;

            double dora = 0;
            foreach (var t in hand14) dora += TileUtils.GetDoraValue(t, _gs.DoraIndicators);
            foreach (var t in _gs.Calls[0]) dora += TileUtils.GetDoraValue(t, _gs.DoraIndicators);
            double totalHan = yakuVal + dora;
            if (totalHan < 1) return null;

            double fu = 30;
            if (!isChii)
            {
                var waitTiles = TileUtils.Remove(hand14, new List<MahjongTile>(tap.Triples).Also(tap.Pairs));
                fu = ScoreCalculator.CalculateFu(tap.Triples, _gs.Calls[0], tap.Pairs,
                    waitTiles.Count > 0 ? waitTiles : null, discardedTile,
                    _gs.IsClosed, _gs.SeatWind, _gs.RoundWind);
            }
            else
            {
                fu = 25;
            }

            double score = ScoreCalculator.CalculateScore(_gs.SeatWind == 1, totalHan, fu);
            return new CallAdvice
            {
                Type = "荣",
                Available = true,
                Recommended = true,
                CalledTile = discardedTile,
                UsedTiles = new List<MahjongTile>(),
                Reason = $"和了! {totalHan:F0}番{fu:F0}符 约{score:F0}点",
                ScoreEstimate = score,
                ShantenBefore = 0,
                ShantenAfter = -1,
            };
        }

        private CallAdvice SimulateCallTriple(MahjongTile discardedTile, List<MahjongTile> callTilesFromHand, string callType)
        {
            var advice = new CallAdvice
            {
                Type = callType,
                Available = true,
                CalledTile = discardedTile,
                UsedTiles = new List<MahjongTile>(callTilesFromHand),
            };

            var beforeHandValue = GetHandValues(new List<MahjongTile>(_gs.OwnHand));
            advice.ShantenBefore = beforeHandValue.Shanten;

            if (!_gs.StrategyAllowsCalls && (_gs.TilesLeft > 4 || beforeHandValue.Shanten > 1))
            {
                advice.Recommended = false;
                advice.Reason = "策略不允许鸣牌";
                return advice;
            }
            if (_gs.CurrentStrategy == Strategy.Fold)
            {
                advice.Recommended = false;
                advice.Reason = "弃和中";
                return advice;
            }

            var savedHand = new List<MahjongTile>(_gs.OwnHand);
            var savedCalls = new List<MahjongTile>(_gs.Calls[0]);
            bool savedClosed = _gs.IsClosed;
            bool savedAllowsCalls = _gs.StrategyAllowsCalls;
            var savedStrategy = _gs.CurrentStrategy;

            TilePriority bestDiscardPrio = null;
            int afterPairs = 0, afterHonorPairs = 0, afterValuePairsWithAvail = 0;
            bool hasSafeTiles = false;
            List<TilePriority> tilePrios = null;

            try
            {
                foreach (var t in callTilesFromHand)
                {
                    _gs.Calls[0].Add(t);
                    for (int i = 0; i < _gs.OwnHand.Count; i++)
                    {
                        if (_gs.OwnHand[i].IsSame(t, true)) { _gs.OwnHand.RemoveAt(i); break; }
                    }
                }
                _gs.Calls[0].Add(discardedTile);
                _gs.IsClosed = false;
                _gs.UpdateAvailableTiles();

                _gs.CurrentStrategy = DetermineStrategy();
                tilePrios = GetTilePriorities();
                if (tilePrios != null && tilePrios.Count > 0)
                    SortOutUnsafeTiles(tilePrios);

                bestDiscardPrio = (tilePrios != null && tilePrios.Count > 0) ? tilePrios[0] : null;

                if (bestDiscardPrio != null)
                {
                    var afterHand = new List<MahjongTile>(_gs.OwnHand);
                    for (int i = 0; i < afterHand.Count; i++)
                    {
                        if (afterHand[i].IsSame(bestDiscardPrio.Tile, true)) { afterHand.RemoveAt(i); break; }
                    }
                    var afterTap = ShantenCalculator.GetTriplesAndPairs(afterHand);
                    afterPairs = afterTap.Pairs.Count / 2;
                    for (int i = 0; i + 1 < afterTap.Pairs.Count; i += 2)
                    {
                        if (afterTap.Pairs[i].Type == 3) afterHonorPairs++;
                        if (afterTap.Pairs[i].IsValueTile(_gs.SeatWind, _gs.RoundWind) &&
                            TileUtils.GetNumberOfTilesAvailable(afterTap.Pairs[i].Index, afterTap.Pairs[i].Type, _gs.VisibleTiles) >= 1)
                            afterValuePairsWithAvail++;
                    }
                    hasSafeTiles = tilePrios.Exists(t => t.Safe);
                }
            }
            finally
            {
                _gs.OwnHand.Clear();
                _gs.OwnHand.AddRange(savedHand);
                _gs.Calls[0].Clear();
                _gs.Calls[0].AddRange(savedCalls);
                _gs.IsClosed = savedClosed;
                _gs.StrategyAllowsCalls = savedAllowsCalls;
                _gs.CurrentStrategy = savedStrategy;
                _gs.UpdateAvailableTiles();
            }

            if (bestDiscardPrio == null)
            {
                advice.Recommended = false;
                advice.Reason = "无法评估";
                return advice;
            }

            int afterShanten = bestDiscardPrio.Shanten;
            double afterScoreOpen = bestDiscardPrio.ScoreOpen;
            double afterYakuOpen = bestDiscardPrio.YakuOpen;
            double afterWaits = bestDiscardPrio.Waits;
            double afterPriority = bestDiscardPrio.Priority;

            advice.ShantenAfter = afterShanten;
            advice.ScoreEstimate = afterScoreOpen;
            advice.BestDiscard = bestDiscardPrio.Tile;

            if (bestDiscardPrio.Tile.IsSame(discardedTile))
            {
                advice.Recommended = false;
                advice.Reason = "最优弃牌即为所鸣之牌";
                return advice;
            }

            if (callType == "吃" && callTilesFromHand.Count == 2)
            {
                if ((callTilesFromHand[0].Index == discardedTile.Index - 2 &&
                     bestDiscardPrio.Tile.IsSame(new MahjongTile(callTilesFromHand[0].Type, callTilesFromHand[0].Index - 1))) ||
                    (callTilesFromHand[1].Index == discardedTile.Index + 2 &&
                     bestDiscardPrio.Tile.IsSame(new MahjongTile(callTilesFromHand[1].Type, callTilesFromHand[1].Index + 1))))
                {
                    advice.Recommended = false;
                    advice.Reason = "弃牌破坏鸣牌效果";
                    return advice;
                }
            }

            if (!hasSafeTiles)
            {
                advice.Recommended = false;
                advice.Reason = "鸣牌后无安全牌";
                return advice;
            }

            if (_gs.TilesLeft <= 4 && beforeHandValue.Shanten == 1 && afterShanten == 0)
            {
                advice.Recommended = true;
                advice.Reason = "终盘鸣牌听牌";
                return advice;
            }

            if (afterYakuOpen < 0.15 && afterValuePairsWithAvail < 2)
            {
                advice.Recommended = false;
                advice.Reason = $"役不足({afterYakuOpen:F2})";
                return advice;
            }

            if (beforeHandValue.Waits > 0 && afterWaits < beforeHandValue.Waits + 1)
            {
                advice.Recommended = false;
                advice.Reason = "待牌减少";
                return advice;
            }

            if (savedClosed && afterScoreOpen < 1500 - ParamCallPonChi * 200 &&
                afterShanten >= (int)(2 + ParamCallPonChi) && _gs.SeatWind != 1 &&
                !(afterHonorPairs >= 1 && afterPairs >= 2))
            {
                advice.Recommended = false;
                advice.Reason = "便宜且慢";
                return advice;
            }

            double bScoreClosed = beforeHandValue.ScoreClosed;
            double bScoreOpen = beforeHandValue.ScoreOpen;
            double nScoreOpen = afterScoreOpen;
            if (_gs.SeatWind == 1) { bScoreClosed /= 1.5; bScoreOpen /= 1.5; nScoreOpen /= 1.5; }

            if (afterShanten > beforeHandValue.Shanten)
            {
                advice.Recommended = false;
                advice.Reason = "向听数变差";
                return advice;
            }

            if (afterShanten == beforeHandValue.Shanten)
            {
                if (!savedClosed)
                {
                    double beforePri = beforeHandValue.Priority;
                    if (afterPriority > beforePri * 1.5)
                    {
                        advice.Recommended = true;
                        advice.Reason = "已副露，鸣牌改善手牌";
                        return advice;
                    }
                }
                advice.Recommended = false;
                advice.Reason = "向听无改善";
                return advice;
            }

            bool isBadWait = callTilesFromHand[0].IsSame(callTilesFromHand[1]);
            if (!isBadWait && callTilesFromHand.Count == 2)
            {
                int diff = Math.Abs(callTilesFromHand[0].Index - callTilesFromHand[1].Index);
                if (diff == 2) isBadWait = true;
                if ((callTilesFromHand[0].Index >= 8 && callTilesFromHand[1].Index >= 8) ||
                    (callTilesFromHand[0].Index <= 2 && callTilesFromHand[1].Index <= 2))
                    isBadWait = true;
            }

            if (beforeHandValue.Shanten >= (int)(5 - ParamCallPonChi) && _gs.SeatWind == 1)
            {
                advice.Recommended = true;
                advice.Reason = "慢手+亲家加速";
                return advice;
            }
            if (!savedClosed && nScoreOpen > bScoreOpen * 0.9)
            {
                advice.Recommended = true;
                advice.Reason = "已副露，向听改善";
                return advice;
            }
            if (nScoreOpen >= 4500 - ParamCallPonChi * 500 && nScoreOpen > bScoreClosed * 0.7)
            {
                advice.Recommended = true;
                advice.Reason = "高打点快攻";
                return advice;
            }
            if (nScoreOpen >= bScoreClosed * 1.75 &&
                (nScoreOpen >= 2000 - ParamCallPonChi * 200 - (3 - afterShanten) * 200 || afterHonorPairs >= 1))
            {
                advice.Recommended = true;
                advice.Reason = "鸣牌大幅提升牌力";
                return advice;
            }
            if (nScoreOpen > bScoreOpen * 0.9 && nScoreOpen > bScoreClosed * 0.7)
            {
                bool priceOk = isBadWait
                    ? nScoreOpen >= 1000 - ParamCallPonChi * 100 - (3 - afterShanten) * 100
                    : nScoreOpen >= 2000 - ParamCallPonChi * 200 - (3 - afterShanten) * 200;
                if ((priceOk || afterHonorPairs >= 2) &&
                    afterValuePairsWithAvail >= 2 && (afterPairs >= 2 || afterShanten > 1))
                {
                    advice.Recommended = true;
                    advice.Reason = "向听改善且牌力充足";
                    return advice;
                }
            }
            if (afterShanten == 0 && nScoreOpen > bScoreClosed * 0.9 && afterWaits > 2 && isBadWait)
            {
                advice.Recommended = true;
                advice.Reason = "消除愚形听牌";
                return advice;
            }

            double wallSize = 70;
            double multiScore =
                (0.5 - (double)_gs.TilesLeft / wallSize) +
                (0.25 - afterShanten / 4.0) +
                (afterShanten > 0 ? (afterPairs - afterShanten - 0.5) / 2.0 : 0) +
                (nScoreOpen / 3000.0 - 0.5) +
                (bScoreClosed > 0 ? (nScoreOpen / bScoreClosed * 0.75 - 0.75) : 0) +
                ((isBadWait ? 1.0 : 0) / 2.0 - 0.25);
            if (multiScore >= 1 - ParamCallPonChi / 2)
            {
                advice.Recommended = true;
                advice.Reason = "综合评估有利";
                return advice;
            }

            advice.Recommended = false;
            advice.Reason = "鸣牌收益不足";
            return advice;
        }

        private CallAdvice SimulateDaiminkan(MahjongTile discardedTile)
        {
            var tiles = TileUtils.GetTiles(_gs.OwnHand, discardedTile.Index, discardedTile.Type);
            var advice = new CallAdvice
            {
                Type = "大明杠",
                Available = true,
                CalledTile = discardedTile,
                UsedTiles = new List<MahjongTile>(tiles),
            };

            if (_gs.IsClosed)
            {
                advice.Recommended = false;
                advice.Reason = "门清不宜大明杠";
                return advice;
            }

            var before = GetHandValues(new List<MahjongTile>(_gs.OwnHand));
            var handWithout = new List<MahjongTile>(_gs.OwnHand);
            for (int i = 0; i < handWithout.Count; i++)
            {
                if (handWithout[i].IsSame(discardedTile)) { handWithout.RemoveAt(i); break; }
            }
            var after = GetHandValues(handWithout);

            advice.ShantenBefore = before.Shanten;
            advice.ShantenAfter = after.Shanten;
            advice.ScoreEstimate = after.ScoreOpen;

            bool effOk = before.Efficiency * 0.9 <= after.Efficiency;
            if (_gs.PlayerRiichi[0] ||
                (_gs.StrategyAllowsCalls &&
                before.Shanten <= _gs.TilesLeft / 35.0 + ParamCallKan &&
                _defense.GetCurrentDangerLevel() < 1000 + ParamCallKan * 500 &&
                before.Shanten >= after.Shanten && effOk))
            {
                advice.Recommended = true;
                advice.Reason = "安全开杠";
            }
            else
            {
                advice.Recommended = false;
                if (before.Shanten < after.Shanten) advice.Reason = "杠后向听变差";
                else if (!effOk) advice.Reason = "杠后效率下降";
                else advice.Reason = "场况不利";
            }
            return advice;
        }

        private List<List<MahjongTile>> FindChiCombinations(MahjongTile discardedTile)
        {
            var results = new List<List<MahjongTile>>();
            if (discardedTile.Type == 3) return results;
            int idx = discardedTile.Index;
            int tp = discardedTile.Type;

            if (idx + 2 <= 9)
            {
                var t1 = FindTileInHand(tp, idx + 1);
                var t2 = FindTileInHand(tp, idx + 2);
                if (t1.HasValue && t2.HasValue)
                    results.Add(new List<MahjongTile> { t1.Value, t2.Value });
            }
            if (idx - 1 >= 1 && idx + 1 <= 9)
            {
                var t1 = FindTileInHand(tp, idx - 1);
                var t2 = FindTileInHand(tp, idx + 1);
                if (t1.HasValue && t2.HasValue)
                    results.Add(new List<MahjongTile> { t1.Value, t2.Value });
            }
            if (idx - 2 >= 1)
            {
                var t1 = FindTileInHand(tp, idx - 2);
                var t2 = FindTileInHand(tp, idx - 1);
                if (t1.HasValue && t2.HasValue)
                    results.Add(new List<MahjongTile> { t1.Value, t2.Value });
            }
            return results;
        }

        private MahjongTile? FindTileInHand(int type, int index)
        {
            foreach (var t in _gs.OwnHand)
                if (t.Type == type && t.Index == index) return t;
            return null;
        }

        private HandMetrics EvaluateHandMetrics(List<MahjongTile> hand)
        {
            int callTriples = ShantenCalculator.GetTriplesOnly(_gs.Calls[0]).Count / 3;
            var tap = ShantenCalculator.GetTriplesAndPairs(hand);
            var doubles = ShantenCalculator.GetDoubles(TileUtils.Remove(hand,
                new List<MahjongTile>(tap.Triples).Also(tap.Pairs)));
            int shanten = ShantenCalculator.Calculate(tap.Triples.Count / 3 + callTriples,
                tap.Pairs.Count / 2, doubles.Count / 2);

            var yaku = YakuEvaluator.GetYaku(hand, _gs.Calls[0], tap, false,
                _gs.SeatWind, _gs.RoundWind, true);
            double dora = 0;
            foreach (var t in tap.Triples) dora += TileUtils.GetDoraValue(t, _gs.DoraIndicators);
            foreach (var t in tap.Pairs) dora += TileUtils.GetDoraValue(t, _gs.DoraIndicators);
            foreach (var t in _gs.Calls[0]) dora += TileUtils.GetDoraValue(t, _gs.DoraIndicators);
            bool isDealer = _gs.SeatWind == 1;

            double waits = 0;
            if (shanten == 0)
            {
                for (int tp = 0; tp <= 3; tp++)
                {
                    int maxIdx = tp == 3 ? 7 : 9;
                    for (int idx = 1; idx <= maxIdx; idx++)
                    {
                        var tt = new MahjongTile(tp, idx);
                        var h14 = new List<MahjongTile>(hand) { tt };
                        var tap14 = ShantenCalculator.GetTriplesAndPairs(h14);
                        if (ShantenCalculator.IsWinning(tap14.Triples.Count / 3 + callTriples, tap14.Pairs.Count / 2))
                            waits += TileUtils.GetNumberOfTilesAvailable(idx, tp, _gs.VisibleTiles);
                    }
                }
            }

            int pairs = tap.Pairs.Count / 2;
            int honorPairs = 0, valuePairs = 0;
            for (int i = 0; i + 1 < tap.Pairs.Count; i += 2)
            {
                if (tap.Pairs[i].Type == 3) honorPairs++;
                if (tap.Pairs[i].IsValueTile(_gs.SeatWind, _gs.RoundWind) &&
                    TileUtils.GetNumberOfTilesAvailable(tap.Pairs[i].Index, tap.Pairs[i].Type, _gs.VisibleTiles) >= 2)
                    valuePairs++;
            }

            return new HandMetrics
            {
                Shanten = shanten,
                ScoreOpen = ScoreCalculator.CalculateScore(isDealer, yaku.Open + dora),
                ScoreClosed = ScoreCalculator.CalculateScore(isDealer, yaku.Closed + dora),
                YakuOpen = yaku.Open, YakuClosed = yaku.Closed,
                Dora = dora, Waits = waits,
                Pairs = pairs, HonorPairs = honorPairs,
                ValueHonorPairsWithAvail = valuePairs
            };
        }

        private bool IsInFuriten()
        {
            if (_gs.Discards[0].Count == 0) return false;
            int callTriples = ShantenCalculator.GetTriplesOnly(_gs.Calls[0]).Count / 3;

            for (int tp = 0; tp <= 3; tp++)
            {
                int maxIdx = tp == 3 ? 7 : 9;
                for (int idx = 1; idx <= maxIdx; idx++)
                {
                    var testTile = new MahjongTile(tp, idx);
                    var hand14 = new List<MahjongTile>(_gs.OwnHand) { testTile };
                    var tap = ShantenCalculator.GetTriplesAndPairs(hand14);
                    if (ShantenCalculator.IsWinning(tap.Triples.Count / 3 + callTriples, tap.Pairs.Count / 2))
                    {
                        foreach (var d in _gs.Discards[0])
                            if (d.IsSame(testTile)) return true;
                    }
                }
            }
            return false;
        }

        #endregion

        #region Helpers

        private double CalculatePriority(double efficiency, double scoreOpen, double scoreClosed, double danger)
        {
            double score = _gs.IsClosed ? scoreClosed : scoreOpen;

            double placementFactor = 1;
            if (_gs.IsLastGame() && _gs.GetDistanceToFirst() < 0)
                placementFactor = 1.5;

            double we = Math.Pow(Math.Abs(efficiency), 0.3 + ParamEfficiency * placementFactor);
            if (efficiency < 0) we = -we;
            score -= danger * 2 * ParamSafety;
            if (we < 0) score = 50000 - score;
            return we * score;
        }

        private bool IsTileFuriten(MahjongTile tile, MahjongTile? discardedTile = null)
        {
            if (discardedTile.HasValue && tile.IsSame(discardedTile.Value)) return true;
            foreach (var d in _gs.Discards[0])
                if (d.IsSame(tile)) return true;
            for (int p = 1; p < 4; p++)
                foreach (var c in _gs.Calls[p])
                    if (c.IsSame(tile) && c.From == _gs.MySeat) return true;
            return false;
        }

        private int GetNonFuritenAvail(MahjongTile tile)
        {
            if (IsTileFuriten(tile, new MahjongTile(-1, 0))) return 0;
            return TileUtils.GetNumberOfTilesAvailable(tile.Index, tile.Type, _gs.VisibleTiles);
        }

        private List<MahjongTile> GetUniqueTerminals(List<MahjongTile> hand)
        {
            var all = TileUtils.GetAllTerminalHonor(hand);
            var unique = new List<MahjongTile>();
            foreach (var t in all)
            {
                bool exists = false;
                foreach (var u in unique) if (t.IsSame(u)) { exists = true; break; }
                if (!exists) unique.Add(t);
            }
            return unique;
        }

        private static readonly MahjongTile[] ThirteenOrphansSet =
        {
            new MahjongTile(1,1), new MahjongTile(1,9),
            new MahjongTile(0,1), new MahjongTile(0,9),
            new MahjongTile(2,1), new MahjongTile(2,9),
            new MahjongTile(3,1), new MahjongTile(3,2), new MahjongTile(3,3), new MahjongTile(3,4),
            new MahjongTile(3,5), new MahjongTile(3,6), new MahjongTile(3,7)
        };

        private List<MahjongTile> GetMissingOrphans(List<MahjongTile> uniqueTerminals)
        {
            var missing = new List<MahjongTile>();
            foreach (var o in ThirteenOrphansSet)
            {
                bool found = false;
                foreach (var u in uniqueTerminals) if (o.IsSame(u)) { found = true; break; }
                if (!found) missing.Add(o);
            }
            return missing;
        }

        #endregion
    }

    internal static class ListExt
    {
        public static List<MahjongTile> Also(this List<MahjongTile> list, List<MahjongTile> other)
        {
            list.AddRange(other); return list;
        }
    }

    internal class TileCombData
    {
        public MahjongTile Tile1;
        public List<Tile2Data> Tiles2;
        public bool Winning;
        public bool Furiten;
        public DecompositionResult TAP;
    }

    internal class Tile2Data
    {
        public MahjongTile Tile;
        public bool Winning;
        public bool Furiten;
        public bool Duplicate;
        public bool Skip;
        public DecompositionResult TAP;
    }
}
