using System;
using System.Collections.Generic;

namespace MajTataru
{
    /// <summary>
    /// 防守评估引擎。
    /// </summary>
    public class AIDefense
    {
        private readonly AIGameState _gs;

        private int _cachedWaitsTurn = -1;
        private double[] _cachedTotalWaits = new double[4];

        public AIDefense(AIGameState gs) { _gs = gs; }

        #region Main Danger: getTileDanger

        public double GetTileDanger(MahjongTile tile, int perspective = 0)
        {
            double total = 0;
            for (int player = 0; player < 4; player++)
            {
                if (player == perspective) continue;
                double chance = GetDealInChance(player, tile, perspective);
                if (perspective == 0)
                    chance *= GetExpectedDealInValue(player);
                total += chance;
            }
            double level = GetCurrentDangerLevel();
            if (level < 2500)
                total *= 1.0 - (2500.0 - level) / 2500.0;
            return total;
        }

        public double GetDealInChance(int player, MahjongTile tile, int perspective = 0)
        {
            double total;
            if (perspective == 0)
            {
                if (_cachedWaitsTurn != _gs.TilesLeft)
                {
                    _cachedWaitsTurn = _gs.TilesLeft;
                    for (int pl = 1; pl < 4; pl++)
                        _cachedTotalWaits[pl] = GetTotalPossibleWaits(pl);
                }
                total = _cachedTotalWaits[player];
            }
            else
            {
                total = GetTotalPossibleWaits(player);
            }
            if (total <= 0) return 0;
            return GetDangerForPlayer(tile, player, perspective) / total;
        }

        public double GetTotalPossibleWaits(int player)
        {
            double total = 0;
            for (int idx = 1; idx <= 9; idx++)
                for (int tp = 0; tp <= 3; tp++)
                {
                    if (tp == 3 && idx >= 8) break;
                    total += GetDangerForPlayer(new MahjongTile(tp, idx), player, 0);
                }
            return total;
        }

        #endregion

        #region Per-Player Danger: getTileDangerForPlayer

        public double GetDangerForPlayer(MahjongTile tile, int player, int perspective = 0)
        {
            if (GetLastTileInDiscard(player, tile)) return 0;

            double danger = GetWaitScore(player, tile, true, perspective == 0);
            if (danger <= 0) return 0;

            if (tile.Type == 3) danger *= 1.3;

            int doraVal = TileUtils.GetDoraValue(tile, _gs.DoraIndicators);
            danger *= 1.0 + doraVal / 10.0;

            if (IsTileCloseToDora(tile)) danger *= 1.05;

            double honitsuChance = IsDoingHonitsu(player, tile.Type);
            double otherHonitsu = Math.Max(Math.Max(
                IsDoingHonitsu(player, 0), IsDoingHonitsu(player, 1)), IsDoingHonitsu(player, 2));
            if (honitsuChance > 0)
                danger *= 1 + honitsuChance;
            else if (otherHonitsu > 0)
            {
                if (tile.Type == 3)
                    danger *= 1 + otherHonitsu;
                else
                    danger *= 1 - otherHonitsu;
            }

            double tanyaoChance = IsDoingTanyao(player);
            if (tile.Type != 3 && tile.Index < 9 && tile.Index > 1)
                danger *= 1 + tanyaoChance / 10.0;
            else
                danger /= 1 + tanyaoChance / 10.0;

            if (!HasYaku(player))
            {
                int pw = _gs.GetSeatWind(player);
                if (tile.Type == 3 && (tile.Index > 4 || tile.Index == pw || tile.Index == _gs.RoundWind) &&
                    TileUtils.GetNumberOfTilesAvailable(tile.Index, tile.Type, _gs.VisibleTiles) > 2)
                    danger *= 1.1;
            }

            if (_gs.PlayerRiichi[player])
            {
                var rt = _gs.RiichiTiles[player];
                if (rt.HasValue && IsTileCloseToOtherTile(tile, rt.Value))
                    danger *= 1.1;
            }

            int earlyCount = Math.Min(6, _gs.Discards[player].Count);
            for (int i = 0; i < earlyCount; i++)
                if (IsTileCloseToOtherTile(tile, _gs.Discards[player][i]))
                    danger *= 0.9;

            return danger < 5 ? 5 : danger;
        }

        #endregion

        #region Wait Score: getWaitScoreForTileAndPlayer

        public double GetWaitScore(int player, MahjongTile tile, bool includeOthers = true, bool useKnowledgeOfOwnHand = true)
        {
            int tile0 = TileUtils.GetNumberOfTilesAvailable(tile.Index, tile.Type, _gs.VisibleTiles);
            int tile0Public = tile0 + TileUtils.Count(_gs.OwnHand, tile.Index, tile.Type);
            if (!useKnowledgeOfOwnHand) tile0 = tile0Public;

            double furitenFactor = GetFuritenValue(player, tile, includeOthers);
            if (furitenFactor == 0) return 0;

            double toitoiFactor = 1 - IsDoingToiToi(player) / 3.0;

            double score = tile0 * tile0Public * furitenFactor * 2 * (2 - toitoiFactor);

            if (_gs.GetNumberOfTilesInHand(player) == 1 || tile.Type == 3) return score;

            int tileL3Pub = TileUtils.GetNumberOfTilesAvailable(tile.Index - 3, tile.Type, _gs.VisibleTiles)
                          + TileUtils.Count(_gs.OwnHand, tile.Index - 3, tile.Type);
            int tileU3Pub = TileUtils.GetNumberOfTilesAvailable(tile.Index + 3, tile.Type, _gs.VisibleTiles)
                          + TileUtils.Count(_gs.OwnHand, tile.Index + 3, tile.Type);

            int tileL2 = TileUtils.GetNumberOfTilesAvailable(tile.Index - 2, tile.Type, _gs.VisibleTiles);
            int tileL1 = TileUtils.GetNumberOfTilesAvailable(tile.Index - 1, tile.Type, _gs.VisibleTiles);
            int tileU1 = TileUtils.GetNumberOfTilesAvailable(tile.Index + 1, tile.Type, _gs.VisibleTiles);
            int tileU2 = TileUtils.GetNumberOfTilesAvailable(tile.Index + 2, tile.Type, _gs.VisibleTiles);

            if (!useKnowledgeOfOwnHand)
            {
                tileL2 += TileUtils.Count(_gs.OwnHand, tile.Index - 2, tile.Type);
                tileL1 += TileUtils.Count(_gs.OwnHand, tile.Index - 1, tile.Type);
                tileU1 += TileUtils.Count(_gs.OwnHand, tile.Index + 1, tile.Type);
                tileU2 += TileUtils.Count(_gs.OwnHand, tile.Index + 2, tile.Type);
            }

            double furitenL = GetFuritenValue(player, new MahjongTile(tile.Type, tile.Index - 3), includeOthers);
            double furitenU = GetFuritenValue(player, new MahjongTile(tile.Type, tile.Index + 3), includeOthers);

            score += tileL1 * tileL2 * (tile0Public + tileL3Pub) * furitenL * toitoiFactor;
            score += tileU1 * tileU2 * (tile0Public + tileU3Pub) * furitenU * toitoiFactor;
            score += tileL1 * tileU1 * tile0Public * furitenFactor * toitoiFactor;

            return score;
        }

        #endregion

        #region Furiten: getFuritenValue / getMostRecentDiscardDanger

        public double GetFuritenValue(int player, MahjongTile tile, bool includeOthers)
        {
            int danger = GetMostRecentDiscardDanger(tile, player, includeOthers);
            if (danger == 0) return 0;
            if (danger == 1) return _gs.Calls[player].Count > 0 ? 0.5 : 0.95;
            if (danger == 2) return _gs.Calls[player].Count > 0 ? 0.8 : 1.0;
            return 1.0;
        }

        private int GetMostRecentDiscardDanger(MahjongTile tile, int player, bool includeOthers)
        {
            if (GetLastTileInDiscard(player, tile)) return 0;

            if (!includeOthers || player == 0) return 99;

            int minDanger = 99;
            for (int i = 0; i < 4; i++)
            {
                if (i == player) continue;
                for (int j = _gs.Discards[i].Count - 1; j >= 0; j--)
                {
                    if (_gs.Discards[i][j].IsSame(tile))
                    {
                        int hc = _gs.GetHandChangesSinceDiscard(player, i, j);
                        if (hc < minDanger) minDanger = hc;
                        break;
                    }
                }
            }
            return minDanger;
        }

        private bool GetLastTileInDiscard(int player, MahjongTile tile)
        {
            for (int i = _gs.Discards[player].Count - 1; i >= 0; i--)
                if (_gs.Discards[player][i].IsSame(tile)) return true;
            return WasTileCalledFromPlayer(player, tile);
        }

        private bool WasTileCalledFromPlayer(int player, MahjongTile tile)
        {
            int playerSeat = (_gs.MySeat + player) % 4;
            for (int i = 0; i < 4; i++)
            {
                if (i == player) continue;
                foreach (var t in _gs.Calls[i])
                    if (t.From == playerSeat && tile.IsSame(t)) return true;
            }
            return false;
        }

        #endregion

        #region Tenpai / Expected Value: isPlayerTenpai / getExpectedHandValue

        public double GetExpectedDealInValue(int player)
        {
            return IsPlayerTenpai(player) * GetExpectedHandValue(player);
        }

        public double IsPlayerTenpai(int player)
        {
            int callSets = _gs.Calls[player].Count / 3;
            if (_gs.PlayerRiichi[player] || callSets >= 4) return 1.0;

            double[][] table = {
                new double[] {0,0.1,0.2,0.5,1,1.8,2.8,4.2,5.8,7.6,9.5,11.5,13.5,15.5,17.5,19.5,21.7,23.9,25,27,29,31,33,35,37},
                new double[] {0.2,0.9,2.3,4.7,8.3,12.7,17.9,23.5,29.2,34.7,39.7,43.9,47.4,50.3,52.9,55.2,57.1,59,61,63,65,67,69},
                new double[] {0,5.1,10.5,17.2,24.7,32.3,39.5,46.1,52,57.2,61.5,65.1,67.9,69.9,71.4,72.4,73.3,74.2,75,76,77,78,79},
                new double[] {0,0,41.9,54.1,63.7,70.9,76,79.9,83,85.1,86.7,87.9,88.7,89.2,89.5,89.4,89.3,89.2,89.2,89.2,90,90,90}
            };

            int numDiscards = _gs.Discards[player].Count;
            int playerSeat = (_gs.MySeat + player) % 4;
            for (int i = 0; i < 4; i++)
            {
                if (i == player) continue;
                foreach (var t in _gs.Calls[i])
                    if (t.From == playerSeat) numDiscards++;
            }
            if (numDiscards > 20) numDiscards = 20;

            double tenpaiChance;
            int row = Math.Min(callSets, 3);
            if (row < 0 || numDiscards < 0 || numDiscards >= table[row].Length)
                tenpaiChance = 0.5;
            else
                tenpaiChance = table[row][numDiscards] / 100.0;

            tenpaiChance *= 1 + IsPlayerPushing(player) / 5.0;

            for (int tp = 0; tp < 3; tp++)
            {
                double h = IsDoingHonitsu(player, tp);
                if (h > 0)
                {
                    var disc = _gs.Discards[player];
                    for (int di = 10; di < disc.Count; di++)
                    {
                        if (disc[di].Type == tp) { tenpaiChance *= 1 + h / 1.5; break; }
                    }
                }
            }

            if (tenpaiChance > 1) tenpaiChance = 1;
            if (tenpaiChance < 0) tenpaiChance = 0;
            return tenpaiChance;
        }

        public double GetExpectedHandValue(int player)
        {
            double doraValue = TileUtils.TotalDora(_gs.Calls[player], _gs.DoraIndicators);
            doraValue += GetExpectedDoraInHand(player);

            double hanValue = 0;
            if (_gs.PlayerRiichi[player]) hanValue += 1;

            hanValue += Math.Max(Math.Max(IsDoingHonitsu(player, 0) * 2, IsDoingHonitsu(player, 1) * 2), IsDoingHonitsu(player, 2) * 2)
                      + IsDoingToiToi(player) * 2
                      + IsDoingTanyao(player) * 1
                      + IsDoingYakuhai(player) * 1;

            if (_gs.Calls[player].Count == 0)
                hanValue += 1.3;
            else
                hanValue += _gs.GetNumberOfTilesInHand(player) / 15.0;

            if (hanValue < 1) hanValue = 1;

            bool isDealer = _gs.GetSeatWind(player) == 1;
            return ScoreCalculator.CalculateScore(isDealer, hanValue + doraValue);
        }

        private double GetExpectedDoraInHand(int player)
        {
            double uradora = 0;
            if (_gs.PlayerRiichi[player])
                uradora = _gs.DoraIndicators.Count * 0.4;

            int tilesInHand = _gs.GetNumberOfTilesInHand(player);
            int discardsCount = _gs.Discards[player].Count;
            int availCount = Math.Max(1, _gs.AvailableTiles.Count);
            double dorasInPool = TileUtils.TotalDora(_gs.AvailableTiles, _gs.DoraIndicators);

            return ((tilesInHand + discardsCount / 2.0) / availCount) * dorasInPool + uradora;
        }

        #endregion

        #region Yaku Prediction

        public double IsDoingHonitsu(int player, int type)
        {
            var calls = _gs.Calls[player];
            if (calls.Count == 0) return 0;
            foreach (var t in calls)
                if (t.Type != type && t.Type != 3) return 0;
            if (calls.Count / 3 >= 4) return 1;

            var disc = _gs.Discards[player];
            int earlyCount = Math.Min(10, disc.Count);
            if (earlyCount == 0) return 0;
            int sameTypeCount = 0;
            for (int i = 0; i < earlyCount; i++)
                if (disc[i].Type == type) sameTypeCount++;
            double pct = (double)sameTypeCount / earlyCount;
            if (pct > 0.2) return 0;

            double confidence = Math.Pow(calls.Count / 3, 2) / 10.0 - pct + 0.1;
            return confidence > 1 ? 1 : Math.Max(0, confidence);
        }

        public double IsDoingToiToi(int player)
        {
            var calls = _gs.Calls[player];
            if (calls.Count == 0) return 0;
            if (ShantenCalculator.GetSequences(calls).Count > 0) return 0;
            return Math.Max(0, GetConfidenceInYakuPrediction(player) - 0.1);
        }

        public double IsDoingTanyao(int player)
        {
            var calls = _gs.Calls[player];
            if (calls.Count == 0) return 0;
            foreach (var t in calls)
                if (t.Type == 3 || t.Index == 1 || t.Index == 9) return 0;

            var disc = _gs.Discards[player];
            int earlyCount = Math.Min(5, disc.Count);
            if (earlyCount == 0) return 0;
            int termCount = 0;
            for (int i = 0; i < earlyCount; i++)
                if (disc[i].Type == 3 || disc[i].Index == 1 || disc[i].Index == 9) termCount++;
            if ((double)termCount / earlyCount < 0.6) return 0;

            return GetConfidenceInYakuPrediction(player);
        }

        public double IsDoingYakuhai(int player)
        {
            var calls = _gs.Calls[player];
            if (calls.Count == 0) return 0;
            int pw = _gs.GetSeatWind(player);
            int yakuhai = 0;
            for (int i = 0; i + 2 < calls.Count; i += 3)
            {
                var t = calls[i];
                if (t.Type == 3 && (t.Index > 4 || t.Index == pw || t.Index == _gs.RoundWind))
                    yakuhai++;
                if (t.Type == 3 && t.Index == pw && pw == _gs.RoundWind)
                    yakuhai++;
            }
            return yakuhai;
        }

        public bool HasYaku(int player)
        {
            return IsDoingHonitsu(player, 0) > 0 || IsDoingHonitsu(player, 1) > 0 ||
                   IsDoingHonitsu(player, 2) > 0 || IsDoingToiToi(player) > 0 ||
                   IsDoingTanyao(player) > 0 || IsDoingYakuhai(player) > 0;
        }

        private double GetConfidenceInYakuPrediction(int player)
        {
            double c = Math.Pow(_gs.Calls[player].Count / 3, 2) / 10.0;
            return c > 1 ? 1 : c;
        }

        private double IsPlayerPushing(int player)
        {
            var safetyList = _gs.PlayerDiscardSafetyList[player];
            if (safetyList.Count < 3) return 0;

            int start = Math.Max(0, safetyList.Count - 3);
            var last3 = new List<double>();
            for (int i = start; i < safetyList.Count; i++)
            {
                if (safetyList[i] >= 0) last3.Add(safetyList[i]);
            }
            if (last3.Count == 0) return 0;

            double sum = 0;
            foreach (double v in last3) sum += v * 20;
            double pushValue = -1 + sum / last3.Count;
            return pushValue > 1 ? 1 : pushValue;
        }

        #endregion

        #region Danger Level: getCurrentDangerLevel

        public double GetCurrentDangerLevel(int forPlayer = 0)
        {
            int i = 1, j = 2, k = 3;
            if (forPlayer == 1) i = 0;
            if (forPlayer == 2) j = 0;
            if (forPlayer == 3) k = 0;
            double ei = GetExpectedDealInValue(i);
            double ej = GetExpectedDealInValue(j);
            double ek = GetExpectedDealInValue(k);
            return (ei + ej + ek + Math.Max(ei, Math.Max(ej, ek))) / 4.0;
        }

        #endregion

        #region Safe Tiles & Sakigiri

        public bool IsSafeTile(int player, MahjongTile tile)
        {
            if (GetWaitScore(player, tile, false) < 20) return true;
            if (tile.Type == 3)
            {
                int count = 0;
                foreach (var t in _gs.AvailableTiles)
                    if (t.IsSame(tile)) count++;
                if (count <= 2) return true;
            }
            return false;
        }

        public double GetSakigiriValue(List<MahjongTile> hand, MahjongTile tile)
        {
            double sakigiri = 0;
            for (int player = 1; player < 4; player++)
            {
                if (_gs.Discards[player].Count < 3) continue;
                if (GetExpectedDealInValue(player) > 150) continue;
                if (IsSafeTile(player, tile)) continue;

                int safeTiles = 0;
                foreach (var t in hand)
                    if (IsSafeTile(player, t)) safeTiles++;

                double saki = (3 - safeTiles) * (_gs.ParamSakigiri * 4);
                if (saki <= 0) continue;

                if (_gs.GetSeatWind(player) == 1) saki *= 1.5;
                sakigiri += saki;
            }
            return sakigiri;
        }

        public double GetWaitQuality(MahjongTile tile)
        {
            double q = 1.3 - GetDealInChance(0, tile, 1) * 5;
            return q < 0.7 ? 0.7 : q;
        }

        #endregion

        #region Utility

        private bool IsTileCloseToOtherTile(MahjongTile tile, MahjongTile other)
        {
            if (tile.Type == 3 || tile.Type != other.Type) return false;
            return tile.Index >= other.Index - 3 && tile.Index <= other.Index + 3;
        }

        private bool IsTileCloseToDora(MahjongTile tile)
        {
            foreach (var d in _gs.DoraIndicators)
            {
                int doraIndex = TileUtils.GetHigherIndex(d);
                if (tile.Type == 3 && d.Type == 3 && tile.Index == doraIndex) return true;
                if (tile.Type != 3 && tile.Type == d.Type &&
                    tile.Index >= doraIndex - 2 && tile.Index <= doraIndex + 2) return true;
            }
            return false;
        }

        #endregion
    }
}
