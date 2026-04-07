using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MajTataru
{
    /// <summary>
    /// AI游戏状态，跟踪手牌、弃牌、副露、可见牌等结构化数据。
    /// </summary>
    public class AIGameState
    {
        public List<MahjongTile> OwnHand = new List<MahjongTile>();
        public List<MahjongTile>[] Discards = Init4();
        public List<MahjongTile>[] Calls = Init4();
        public List<MahjongTile> DoraIndicators = new List<MahjongTile>();
        public List<MahjongTile> AvailableTiles = new List<MahjongTile>();
        public List<MahjongTile> VisibleTiles = new List<MahjongTile>();
        public int SeatWind = 1;
        public int RoundWind = 1;
        public int TilesLeft = 70;
        public Strategy CurrentStrategy = Strategy.General;
        public bool StrategyAllowsCalls = true;
        public bool IsClosed = true;
        public bool[] PlayerRiichi = new bool[4];
        public MahjongTile?[] RiichiTiles = new MahjongTile?[4];
        public int[] Scores = new int[4];
        public int MySeat = -1;
        public int TurnCount;
        public bool InGame;
        public int LastDiscardSeat = -1;

        public int GameType = 2;
        public int RoundNumber = 1;
        public int[] PlayerDrawCount = new int[4];
        public List<int[]>[] DiscardDrawSnapshots = InitSnapshots();
        public List<double>[] PlayerDiscardSafetyList = InitSafety();
        public double ParamSakigiri = 1.0;

        private static List<MahjongTile>[] Init4()
        {
            return new[] {
                new List<MahjongTile>(), new List<MahjongTile>(),
                new List<MahjongTile>(), new List<MahjongTile>()
            };
        }

        private static List<int[]>[] InitSnapshots()
        {
            return new[] {
                new List<int[]>(), new List<int[]>(),
                new List<int[]>(), new List<int[]>()
            };
        }

        private static List<double>[] InitSafety()
        {
            return new[] {
                new List<double>(), new List<double>(),
                new List<double>(), new List<double>()
            };
        }

        public void Reset()
        {
            OwnHand.Clear();
            Discards = Init4();
            Calls = Init4();
            DoraIndicators.Clear();
            AvailableTiles.Clear();
            VisibleTiles.Clear();
            SeatWind = 1; RoundWind = 1; TilesLeft = 70;
            CurrentStrategy = Strategy.General;
            StrategyAllowsCalls = true; IsClosed = true;
            PlayerRiichi = new bool[4];
            RiichiTiles = new MahjongTile?[4];
            Scores = new int[4];
            TurnCount = 0;
            LastDiscardSeat = -1;
            GameType = 2;
            RoundNumber = 1;
            PlayerDrawCount = new int[4];
            DiscardDrawSnapshots = InitSnapshots();
            PlayerDiscardSafetyList = InitSafety();
        }

        public void ResetForRound()
        {
            OwnHand.Clear();
            Discards = Init4();
            Calls = Init4();
            AvailableTiles.Clear();
            VisibleTiles.Clear();
            TilesLeft = 70;
            CurrentStrategy = Strategy.General;
            StrategyAllowsCalls = true; IsClosed = true;
            PlayerRiichi = new bool[4];
            RiichiTiles = new MahjongTile?[4];
            TurnCount = 0;
            LastDiscardSeat = -1;
            PlayerDrawCount = new int[4];
            DiscardDrawSnapshots = InitSnapshots();
            PlayerDiscardSafetyList = InitSafety();
        }

        public int SeatToPlayer(int seat)
        {
            if (MySeat < 0) return seat;
            return (seat - MySeat + 4) % 4;
        }

        public bool IsEastRound { get { return GameType != 2; } }

        public bool IsLastGame()
        {
            if (IsEastRound)
                return RoundNumber >= 4 || RoundWind > 1;
            return (RoundNumber >= 4 && RoundWind == 2) || RoundWind > 2;
        }

        public int GetDistanceToFirst()
        {
            return Math.Max(Scores[1], Math.Max(Scores[2], Scores[3])) - Scores[0];
        }

        public int GetDistanceToLast()
        {
            return Math.Min(Scores[1], Math.Min(Scores[2], Scores[3])) - Scores[0];
        }

        public int GetDistanceToPlayer(int player)
        {
            return Scores[player] - Scores[0];
        }

        public int GetSeatWind(int player)
        {
            int seat = (MySeat + player) % 4;
            return seat + 1;
        }

        public int GetNumberOfTilesInHand(int player)
        {
            if (player == 0) return OwnHand.Count;
            int callSets = Calls[player].Count / 3;
            return 13 - callSets * 2;
        }

        public void RecordDiscard(int player, MahjongTile tile)
        {
            Discards[player].Add(tile);
            DiscardDrawSnapshots[player].Add((int[])PlayerDrawCount.Clone());
        }

        public int GetHandChangesSinceDiscard(int targetPlayer, int discardPlayer, int discardIndex)
        {
            if (discardPlayer < DiscardDrawSnapshots.Length &&
                DiscardDrawSnapshots[discardPlayer] != null &&
                discardIndex < DiscardDrawSnapshots[discardPlayer].Count)
            {
                var snapshot = DiscardDrawSnapshots[discardPlayer][discardIndex];
                return PlayerDrawCount[targetPlayer] - snapshot[targetPlayer];
            }
            return 99;
        }

        public void UpdateAvailableTiles()
        {
            VisibleTiles.Clear();
            VisibleTiles.AddRange(DoraIndicators);
            VisibleTiles.AddRange(OwnHand);
            for (int i = 0; i < 4; i++) { VisibleTiles.AddRange(Discards[i]); VisibleTiles.AddRange(Calls[i]); }

            AvailableTiles.Clear();
            for (int tp = 0; tp <= 3; tp++)
            {
                int maxIdx = tp == 3 ? 7 : 9;
                for (int idx = 1; idx <= maxIdx; idx++)
                {
                    int avail = TileUtils.GetNumberOfTilesAvailable(idx, tp, VisibleTiles);
                    for (int k = 0; k < avail; k++)
                    {
                        bool isRed = idx == 5 && tp != 3 &&
                            !VisibleTiles.Exists(t => t.Type == tp && t.Dora) &&
                            !AvailableTiles.Exists(t => t.Type == tp && t.Dora);
                        var tile = new MahjongTile(tp, idx, isRed);
                        tile.DoraValue = TileUtils.GetDoraValue(tile, DoraIndicators);
                        AvailableTiles.Add(tile);
                    }
                }
            }
            TileUtils.UpdateDoraValues(OwnHand, DoraIndicators);
            TileUtils.UpdateDoraValues(VisibleTiles, DoraIndicators);
        }

        public void UpdateIsClosed()
        {
            IsClosed = true;
            foreach (var t in Calls[0])
            {
                if (t.From >= 0 && t.From != MySeat)
                {
                    IsClosed = false;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 麻将AI主控制器。独立处理网络包，维护AI状态，按需输出分析结果。
    /// </summary>
    public class MahjongAI
    {
        public AIGameState State { get; } = new AIGameState();

        /// <summary>
        /// 最近一次分析对应的 Overlay JSON（供悬浮窗消费）。
        /// 每次 RunAnalysis / RunCallAnalysis 执行后更新。
        /// </summary>
        public string LastOverlayJson { get; private set; }

        /// <summary>
        /// 最近一次分析的 TTS 播报文本。供 C# 侧通过 ACT TTS 管线播报，
        /// 使 FoxTTS 等第三方 TTS 插件能够接管语音合成。
        /// </summary>
        public string LastTtsMessage { get; private set; }

        private AIDefense _defense;
        private AIOffense _offense;

        public ushort OP_GAME_INIT = 0x00D7;
        public ushort OP_ROUND_START = 0x0134;
        public ushort OP_DISCARD = 0x0141;
        public ushort OP_DRAW_EVENT = 0x01DC;
        public ushort OP_TSUMO_RESULT = 0x02DE;
        public ushort OP_RON_RESULT = 0x007E;
        public ushort OP_ROUND_END = 0x00EF;
        public ushort OP_GAME_RESULT = 0x03DD;

        private const int HEADER_SIZE = 32;
        private const int OPCODE_OFFSET = 18;

        public MahjongAI()
        {
            _defense = new AIDefense(State);
            _offense = new AIOffense(State, _defense);
        }

        /// <summary>
        /// 处理一个二进制网络包。如果是自家摸牌，返回AI分析字符串；否则返回null。
        /// </summary>
        public string ProcessPacket(byte[] data)
        {
            if (data == null || data.Length < HEADER_SIZE) return null;
            ushort opcode = BitConverter.ToUInt16(data, OPCODE_OFFSET);
            if (!IsMahjongOpcode(opcode)) return null;

            uint[] payload = new uint[(data.Length - HEADER_SIZE) / 4];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = BitConverter.ToUInt32(data, HEADER_SIZE + i * 4);

            return Dispatch(opcode, payload);
        }

        private bool IsMahjongOpcode(ushort op)
        {
            return op == OP_GAME_INIT || op == OP_ROUND_START ||
                   op == OP_DISCARD || op == OP_DRAW_EVENT ||
                   op == OP_TSUMO_RESULT || op == OP_RON_RESULT ||
                   op == OP_ROUND_END || op == OP_GAME_RESULT;
        }

        private string Dispatch(ushort opcode, uint[] p)
        {
            if (opcode == OP_GAME_INIT) return OnGameInit(p);
            if (opcode == OP_ROUND_START) return OnRoundStart(p);
            if (opcode == OP_DISCARD) return OnDiscard(p);
            if (opcode == OP_DRAW_EVENT) return OnDrawEvent(p);
            if (opcode == OP_ROUND_END) { State.ResetForRound(); return null; }
            if (opcode == OP_GAME_RESULT) { State.InGame = false; State.Reset(); return null; }
            return null;
        }

        private string OnGameInit(uint[] p)
        {
            if (p.Length < 12) return null;
            State.Reset();
            State.GameType = (int)p[0];
            State.MySeat = (int)p[6];
            State.InGame = true;
            for (int i = 0; i < 4; i++) State.Scores[i] = (int)p[7 + i] * 100;
            return null;
        }

        private string OnRoundStart(uint[] p)
        {
            if (p.Length < 26) return null;
            State.ResetForRound();
            State.MySeat = (int)p[6];
            State.SeatWind = State.MySeat + 1;
            State.RoundWind = (int)(p[1] + 1);
            State.RoundNumber = (int)(p[2] + 1);

            var dora = MahjongTile.FromType34(p[7]);
            State.DoraIndicators.Clear();
            State.DoraIndicators.Add(dora);

            for (int i = 0; i < 4; i++) State.Scores[i] = (int)p[8 + i] * 100;

            State.OwnHand.Clear();
            for (int i = 0; i < 13; i++)
                State.OwnHand.Add(MahjongTile.FromType34(p[12 + i]));

            State.UpdateAvailableTiles();
            return null;
        }

        private string OnDiscard(uint[] p)
        {
            if (p.Length < 8) return null;

            uint seat = p[0];
            uint actionType = p[2];
            int player = State.SeatToPlayer((int)seat);

            if (actionType == 0x0140)
            {
                // kakan declaration: tiles handled in draw event
                return null;
            }

            uint tileId = p[3] & 0xFFFF;
            if (tileId >= 136) return null;

            var tile = MahjongTile.FromTileId136(tileId);
            tile.DoraValue = TileUtils.GetDoraValue(tile, State.DoraIndicators);
            State.LastDiscardSeat = (int)seat;

            if (player == 0)
            {
                for (int i = 0; i < State.OwnHand.Count; i++)
                {
                    if (State.OwnHand[i].IsSame(tile, true))
                    {
                        State.OwnHand.RemoveAt(i);
                        break;
                    }
                }
                if (State.OwnHand.Count > 13)
                {
                    for (int i = 0; i < State.OwnHand.Count; i++)
                    {
                        if (State.OwnHand[i].IsSame(tile))
                        {
                            State.OwnHand.RemoveAt(i);
                            break;
                        }
                    }
                }
            }

            State.RecordDiscard(player, tile);
            State.PlayerDrawCount[player]++;
            State.TurnCount++;

            bool isRiichi = (actionType & 0x01) != 0 && (actionType & 0xFF00) != 0x0A00;
            if (isRiichi && player >= 0 && player < 4)
            {
                State.PlayerRiichi[player] = true;
                State.RiichiTiles[player] = tile;
            }

            State.UpdateAvailableTiles();

            if (player != 0)
            {
                bool isTsumogiri = (actionType & 0x02) != 0;
                double danger = _defense.GetTileDanger(tile, player);
                if (isTsumogiri && danger < 0.01)
                    danger = 0.05;
                State.PlayerDiscardSafetyList[player].Add(danger);
                return RunCallAnalysis(tile, player);
            }

            return null;
        }

        private string OnDrawEvent(uint[] p)
        {
            if (p.Length < 6) return null;

            uint seat = p[0];
            uint actionType = p[1];
            uint fieldA = p[2];
            int player = State.SeatToPlayer((int)seat);

            switch (actionType)
            {
                case 0x0100: // normal draw
                {
                    State.TilesLeft--;
                    State.PlayerDrawCount[player]++;
                    if (player == 0)
                    {
                        uint tileId = fieldA & 0xFFFF;
                        if (tileId < 136)
                        {
                            var tile = MahjongTile.FromTileId136(tileId);
                            tile.DoraValue = TileUtils.GetDoraValue(tile, State.DoraIndicators);
                            State.OwnHand.Add(tile);
                            State.UpdateAvailableTiles();
                            return RunAnalysis();
                        }
                    }
                    break;
                }
                case 0x0200: // rinshan draw
                {
                    State.TilesLeft--;
                    State.PlayerDrawCount[player]++;
                    uint newDoraId = (fieldA >> 16) & 0xFFFF;
                    if (newDoraId < 136)
                        State.DoraIndicators.Add(MahjongTile.FromTileId136(newDoraId));

                    if (player == 0)
                    {
                        uint tileId = fieldA & 0xFFFF;
                        if (tileId < 136)
                        {
                            var tile = MahjongTile.FromTileId136(tileId);
                            tile.DoraValue = TileUtils.GetDoraValue(tile, State.DoraIndicators);
                            State.OwnHand.Add(tile);
                            State.UpdateAvailableTiles();
                            return RunAnalysis();
                        }
                    }
                    State.UpdateAvailableTiles();
                    break;
                }
                case 0x0500: // pon
                {
                    State.PlayerDrawCount[player]++;
                    AddCallFromPair(player, (int)seat, p, 3, 2);
                    if (player == 0) State.UpdateIsClosed();
                    State.UpdateAvailableTiles();
                    if (player == 0) return RunAnalysis();
                    break;
                }
                case 0x0600: // chi
                {
                    State.PlayerDrawCount[player]++;
                    AddCallFromPair(player, (int)seat, p, 3, 2);
                    if (player == 0) State.UpdateIsClosed();
                    State.UpdateAvailableTiles();
                    if (player == 0) return RunAnalysis();
                    break;
                }
                case 0x0130: // ankan
                {
                    AddCallFromPair(player, (int)seat, p, 3, 2);
                    AddCallFromPair(player, (int)seat, p, 4, 2);
                    if (player == 0) State.UpdateIsClosed();
                    State.UpdateAvailableTiles();
                    break;
                }
                case 0x0400: // kakan or daiminkan
                {
                    AddCallFromPair(player, (int)seat, p, 3, 1);
                    if (player == 0) State.UpdateIsClosed();
                    State.UpdateAvailableTiles();
                    break;
                }
            }
            return null;
        }

        private void AddCallFromPair(int player, int seat, uint[] p, int pairIdx, int expectedCount)
        {
            if (pairIdx >= p.Length) return;
            uint val = p[pairIdx];
            uint hi = (val >> 16) & 0xFFFF;
            uint lo = val & 0xFFFF;

            var tiles = new List<MahjongTile>();
            if (hi < 136) tiles.Add(MahjongTile.FromTileId136(hi));
            if (lo < 136) tiles.Add(MahjongTile.FromTileId136(lo));

            foreach (var tile in tiles)
            {
                var ct = tile;
                ct.From = expectedCount == 1 ? State.LastDiscardSeat : -1;
                State.Calls[player].Add(ct);
                if (player == 0)
                {
                    for (int i = 0; i < State.OwnHand.Count; i++)
                    {
                        if (State.OwnHand[i].IsSame(ct, true))
                        {
                            State.OwnHand.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 执行AI分析，返回格式化的分析字符串。同时更新 LastOverlayJson。
        /// </summary>
        public string RunAnalysis()
        {
            if (State.OwnHand.Count < 2) return null;
            try
            {
                State.CurrentStrategy = _offense.DetermineStrategy();
                var priorities = _offense.GetTilePriorities();
                if (priorities == null || priorities.Count == 0) return null;
                _offense.SortOutUnsafeTiles(priorities);

                bool riichi = false;
                if (priorities[0].Shanten == 0)
                    riichi = _offense.ShouldRiichi(priorities[0]);

                LastOverlayJson = BuildDiscardJson(priorities, riichi);
                return FormatAnalysis(priorities);
            }
            catch (Exception ex)
            {
                LastOverlayJson = null;
                LastTtsMessage = null;
                return $"AI分析异常: {ex.Message}";
            }
        }

        /// <summary>
        /// 对手出牌时评估鸣牌机会（吃/碰/杠/荣和）。同时更新 LastOverlayJson。
        /// </summary>
        public string RunCallAnalysis(MahjongTile discardedTile, int discardingPlayer)
        {
            if (State.OwnHand.Count < 4) return null;
            try
            {
                var advices = _offense.EvaluateCalls(discardedTile, discardingPlayer);
                if (advices == null || advices.Count == 0) return null;
                LastOverlayJson = BuildCallJson(advices, discardedTile, discardingPlayer);
                return FormatCallAnalysis(advices, discardedTile, discardingPlayer);
            }
            catch (Exception ex)
            {
                LastOverlayJson = null;
                LastTtsMessage = null;
                return $"鸣牌分析异常: {ex.Message}";
            }
        }

        private string FormatAnalysis(List<TilePriority> tiles)
        {
            var sb = new StringBuilder();
            string[] stratNames = { "一般型", "七对子", "国士无双", "弃和" };
            string stratName = (int)State.CurrentStrategy < stratNames.Length
                ? stratNames[(int)State.CurrentStrategy] : "?";

            int topShanten = tiles.Count > 0 ? tiles[0].Shanten : -1;
            sb.AppendLine($"策略={stratName} | 向听={topShanten} | 剩余={State.TilesLeft}张 | 自风={GetWindStr(State.SeatWind)}");

            var handSorted = TileUtils.Sort(State.OwnHand);
            sb.Append("手牌: ");
            foreach (var t in handSorted) sb.Append(t.Name + " ");
            sb.AppendLine();

            sb.AppendLine("推荐切牌:");
            int showCount = Math.Min(tiles.Count, 5);
            for (int i = 0; i < showCount; i++)
            {
                var tp = tiles[i];
                string safe = tp.Safe ? "" : "[危]";
                sb.AppendLine($"  #{i + 1} {tp.Tile.Name,-6} 优先={tp.Priority,8:F1} 效率={tp.Efficiency,5:F2} " +
                    $"得分={tp.ScoreOpen:F0}/{tp.ScoreClosed:F0}/{tp.ScoreRiichi:F0} " +
                    $"危险={tp.Danger,5:F1} 向听={tp.Shanten} {safe}");
            }

            if (topShanten == 0 && tiles.Count > 0)
            {
                var best = tiles[0];
                bool riichi = _offense.ShouldRiichi(best);
                string riichiAdvice = riichi ? "建议立直" : "不建议立直";
                if (best.Waits > 0)
                    sb.AppendLine($"听牌分析: 待牌={best.Waits:F1} 形状={best.Shape:F2} → {riichiAdvice}");
            }

            if (State.CurrentStrategy == Strategy.Fold)
                sb.AppendLine("★ 弃和模式: 优先打安全牌");

            return sb.ToString().TrimEnd();
        }

        private string FormatCallAnalysis(List<CallAdvice> advices, MahjongTile discardedTile, int discardingPlayer)
        {
            var sb = new StringBuilder();
            string[] playerNames = { "自家", "下家", "对面", "上家" };
            string playerName = discardingPlayer >= 0 && discardingPlayer < 4
                ? playerNames[discardingPlayer] : "?";
            sb.AppendLine($"▶ {playerName}打出 {discardedTile.Name} — 鸣牌分析:");

            foreach (var a in advices)
            {
                string rec = a.Recommended ? "★推荐" : "  不推荐";
                string usedStr = "";
                if (a.UsedTiles != null && a.UsedTiles.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var t in a.UsedTiles) names.Add(t.ShortName);
                    usedStr = $" [{string.Join("+", names)}]";
                }
                string discardStr = "";
                if (a.BestDiscard.HasValue)
                    discardStr = $" → 打{a.BestDiscard.Value.Name}";

                sb.AppendLine($"  {a.Type}{usedStr}{discardStr}: {rec} | {a.Reason}");

                if (a.ShantenAfter >= 0)
                    sb.AppendLine($"    向听: {a.ShantenBefore}→{a.ShantenAfter} | 预估得分: {a.ScoreEstimate:F0}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetWindStr(int wind)
        {
            string[] w = { "", "东", "南", "西", "北" };
            return wind >= 1 && wind <= 4 ? w[wind] : "?";
        }

        #region Overlay JSON

        private string BuildDiscardJson(List<TilePriority> tiles, bool riichi)
        {
            string[] stratNames = { "一般型", "七对子", "国士无双", "弃和" };
            string strategy = (int)State.CurrentStrategy < stratNames.Length
                ? stratNames[(int)State.CurrentStrategy] : "?";
            int shanten = tiles.Count > 0 ? tiles[0].Shanten : -1;
            var best = tiles[0];
            bool isFold = State.CurrentStrategy == Strategy.Fold;

            string tts;
            if (isFold)
                tts = "弃和模式";
            else if (riichi && shanten == 0)
                tts = $"切{best.Tile.Name} 立直";
            else
                tts = $"切{best.Tile.Name}";
            LastTtsMessage = tts;

            var sb = new StringBuilder(512);
            sb.Append("{\"type\":\"discard\"");
            sb.Append($",\"strategy\":\"{Esc(strategy)}\"");
            sb.Append($",\"shanten\":{shanten}");
            sb.Append($",\"tilesLeft\":{State.TilesLeft}");
            sb.Append($",\"wind\":\"{Esc(GetWindStr(State.SeatWind))}\"");
            sb.Append($",\"bestTile\":\"{Esc(best.Tile.ShortName)}\"");
            sb.Append($",\"bestTileName\":\"{Esc(best.Tile.Name)}\"");
            sb.Append($",\"riichi\":{(riichi ? "true" : "false")}");
            sb.Append($",\"isFold\":{(isFold ? "true" : "false")}");
            sb.Append($",\"tts\":\"{Esc(tts)}\"");

            sb.Append(",\"recommendations\":[");
            int count = Math.Min(tiles.Count, 5);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                var tp = tiles[i];
                sb.Append('{');
                sb.Append($"\"rank\":{i + 1}");
                sb.Append($",\"tile\":\"{Esc(tp.Tile.ShortName)}\"");
                sb.Append($",\"tileName\":\"{Esc(tp.Tile.Name)}\"");
                sb.Append($",\"priority\":{N(tp.Priority, 1)}");
                sb.Append($",\"efficiency\":{N(tp.Efficiency, 2)}");
                sb.Append($",\"danger\":{N(tp.Danger, 1)}");
                sb.Append($",\"shanten\":{tp.Shanten}");
                sb.Append($",\"safe\":{(tp.Safe ? "true" : "false")}");
                sb.Append('}');
            }
            sb.Append(']');

            if (shanten == 0 && best.Waits > 0)
            {
                sb.Append(",\"tenpaiInfo\":{");
                sb.Append($"\"waits\":{N(best.Waits, 1)}");
                sb.Append($",\"shape\":{N(best.Shape, 2)}");
                sb.Append($",\"riichi\":{(riichi ? "true" : "false")}");
                sb.Append('}');
            }

            sb.Append('}');
            return sb.ToString();
        }

        private string BuildCallJson(List<CallAdvice> advices, MahjongTile discardedTile, int discardingPlayer)
        {
            string[] playerNames = { "自家", "下家", "对面", "上家" };
            string playerName = discardingPlayer >= 0 && discardingPlayer < 4
                ? playerNames[discardingPlayer] : "?";

            CallAdvice recommended = null;
            foreach (var a in advices)
                if (a.Recommended) { recommended = a; break; }

            string tts = "";
            if (recommended != null)
            {
                tts = recommended.Type;
                if (recommended.BestDiscard.HasValue)
                    tts += $" 打{recommended.BestDiscard.Value.Name}";
            }
            LastTtsMessage = tts;

            var sb = new StringBuilder(512);
            sb.Append("{\"type\":\"call\"");
            sb.Append($",\"player\":\"{Esc(playerName)}\"");
            sb.Append($",\"discardedTile\":\"{Esc(discardedTile.Name)}\"");
            sb.Append($",\"tts\":\"{Esc(tts)}\"");

            sb.Append(",\"advices\":[");
            for (int i = 0; i < advices.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var a = advices[i];
                sb.Append('{');
                sb.Append($"\"callType\":\"{Esc(a.Type)}\"");
                sb.Append($",\"recommended\":{(a.Recommended ? "true" : "false")}");

                if (a.UsedTiles != null && a.UsedTiles.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var t in a.UsedTiles) names.Add(t.ShortName);
                    sb.Append($",\"tiles\":\"{Esc(string.Join("+", names))}\"");
                }

                if (a.BestDiscard.HasValue)
                    sb.Append($",\"discard\":\"{Esc(a.BestDiscard.Value.Name)}\"");

                if (a.Reason != null)
                    sb.Append($",\"reason\":\"{Esc(a.Reason)}\"");

                if (a.ShantenAfter >= 0)
                {
                    sb.Append($",\"shantenBefore\":{a.ShantenBefore}");
                    sb.Append($",\"shantenAfter\":{a.ShantenAfter}");
                    sb.Append($",\"score\":{N(a.ScoreEstimate, 0)}");
                }

                sb.Append('}');
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");
        }

        private static string N(double v, int d)
        {
            return v.ToString("F" + d, CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
