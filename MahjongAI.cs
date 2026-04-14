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
        public int PhysicalSeat = -1;
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
            PhysicalSeat = -1;
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

        public bool IsEastRound { get { return GameType < 2; } }

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
        private MjaiClient _mjaiClient;
        private uint[] _pendingRoundStart;

        public bool UseMjaiModel { get; set; }
        public string MjaiServerUrl
        {
            get { return _mjaiClient != null ? _mjaiClient.ServerUrl : "http://127.0.0.1:7331"; }
            set { if (_mjaiClient != null) _mjaiClient.ServerUrl = value; }
        }

        public void ResetMjaiServer()
        {
            if (_mjaiClient != null) _mjaiClient.ResetServer();
        }

        public MahjongAI()
        {
            _defense = new AIDefense(State);
            _offense = new AIOffense(State, _defense);
            _mjaiClient = new MjaiClient(State);
        }

        /// <summary>
        /// 处理一个二进制网络包。如果是自家摸牌，返回AI分析字符串；否则返回null。
        /// </summary>
        public string ProcessPacket(byte[] data)
        {
            if (data == null || data.Length < MahjongOpcodes.HEADER_SIZE) return null;
            ushort opcode = BitConverter.ToUInt16(data, MahjongOpcodes.OPCODE_OFFSET);
            if (!IsMahjongOpcode(opcode)) return null;

            uint[] payload = new uint[(data.Length - MahjongOpcodes.HEADER_SIZE) / 4];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = BitConverter.ToUInt32(data, MahjongOpcodes.HEADER_SIZE + i * 4);

            return Dispatch(opcode, payload);
        }

        private bool IsMahjongOpcode(ushort op)
        {
            return op == MahjongOpcodes.OP_GAME_INIT || op == MahjongOpcodes.OP_ROUND_START ||
                   op == MahjongOpcodes.OP_DISCARD || op == MahjongOpcodes.OP_DRAW_EVENT ||
                   op == MahjongOpcodes.OP_TSUMO_RESULT || op == MahjongOpcodes.OP_RON_RESULT ||
                   op == MahjongOpcodes.OP_ROUND_END || op == MahjongOpcodes.OP_GAME_RESULT;
        }

        private string Dispatch(ushort opcode, uint[] p)
        {
            if (opcode == MahjongOpcodes.OP_GAME_INIT) return OnGameInit(p);
            if (opcode == MahjongOpcodes.OP_ROUND_START) return OnRoundStart(p);
            if (opcode == MahjongOpcodes.OP_DISCARD) return OnDiscard(p);
            if (opcode == MahjongOpcodes.OP_DRAW_EVENT) return OnDrawEvent(p);
            if (opcode == MahjongOpcodes.OP_ROUND_END) { if (UseMjaiModel) _mjaiClient.OnRoundEnd(); State.ResetForRound(); return null; }
            if (opcode == MahjongOpcodes.OP_GAME_RESULT) { if (UseMjaiModel) _mjaiClient.OnGameEnd(); _pendingRoundStart = null; State.InGame = false; State.Reset(); return null; }
            return null;
        }

        private string OnGameInit(uint[] p)
        {
            if (p.Length < 12) return null;
            var savedRound = _pendingRoundStart;
            _pendingRoundStart = null;
            State.Reset();
            _mjaiClient.Reset();
            State.GameType = (int)p[0];
            State.MySeat = (int)p[6];
            State.PhysicalSeat = (int)p[6];
            State.InGame = true;
            for (int i = 0; i < 4; i++) State.Scores[i] = (int)p[7 + i] * 100;
            if (UseMjaiModel) _mjaiClient.OnGameInit();
            if (savedRound != null)
                OnRoundStart(savedRound);
            return null;
        }

        private string OnRoundStart(uint[] p)
        {
            if (p.Length < 26) return null;
            if (!State.InGame)
            {
                _pendingRoundStart = (uint[])p.Clone();
                return null;
            }
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

            if (UseMjaiModel)
            {
                int honba = (int)p[3];
                int kyotaku = (int)p[4];
                _mjaiClient.OnRoundStart(honba, kyotaku);
            }
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
                uint kakanTileId = p.Length > 4 ? (p[4] & 0xFFFF) : 0xFFFF;
                if (kakanTileId < 136)
                {
                    var kt = MahjongTile.FromTileId136(kakanTileId);
                    kt.DoraValue = TileUtils.GetDoraValue(kt, State.DoraIndicators);

                    if (UseMjaiModel)
                    {
                        var existingPon = new List<MahjongTile>();
                        foreach (var ct in State.Calls[player])
                            if (ct.Index == kt.Index && ct.Type == kt.Type && existingPon.Count < 3)
                                existingPon.Add(ct);
                        _mjaiClient.OnKakan(player, kt, existingPon);
                    }

                    State.Calls[player].Add(kt);
                    if (player == 0)
                    {
                        for (int i = 0; i < State.OwnHand.Count; i++)
                        {
                            if (State.OwnHand[i].IsSame(kt, true))
                            {
                                State.OwnHand.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
                State.UpdateAvailableTiles();
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

            bool tsumogiri = (actionType & 0x02) != 0;
            if (UseMjaiModel) _mjaiClient.OnDiscard(player, tile, tsumogiri, isRiichi);

            if (player != 0)
            {
                double danger = _defense.GetTileDanger(tile, player);
                if (tsumogiri && danger < 0.01)
                    danger = 0.05;
                State.PlayerDiscardSafetyList[player].Add(danger);
                return UseMjaiModel ? RunMjaiCallAnalysis(tile, player) : RunCallAnalysis(tile, player);
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
                            if (UseMjaiModel) _mjaiClient.OnDraw(0, tile);
                            return UseMjaiModel ? RunMjaiAnalysis() : RunAnalysis();
                        }
                    }
                    else
                    {
                        if (UseMjaiModel) _mjaiClient.OnDraw(player, null);
                    }
                    break;
                }
                case 0x0200: // rinshan draw
                {
                    State.TilesLeft--;
                    State.PlayerDrawCount[player]++;
                    uint newDoraId = (fieldA >> 16) & 0xFFFF;
                    MahjongTile? newDora = null;
                    if (newDoraId < 136)
                    {
                        newDora = MahjongTile.FromTileId136(newDoraId);
                        State.DoraIndicators.Add(newDora.Value);
                        if (UseMjaiModel) _mjaiClient.OnNewDora(newDora.Value);
                    }

                    if (player == 0)
                    {
                        uint tileId = fieldA & 0xFFFF;
                        if (tileId < 136)
                        {
                            var tile = MahjongTile.FromTileId136(tileId);
                            tile.DoraValue = TileUtils.GetDoraValue(tile, State.DoraIndicators);
                            State.OwnHand.Add(tile);
                            State.UpdateAvailableTiles();
                            if (UseMjaiModel) _mjaiClient.OnDraw(0, tile);
                            return UseMjaiModel ? RunMjaiAnalysis() : RunAnalysis();
                        }
                    }
                    else
                    {
                        if (UseMjaiModel) _mjaiClient.OnDraw(player, null);
                    }
                    State.UpdateAvailableTiles();
                    break;
                }
                case 0x0500: // pon
                {
                    int targetPlayer = State.LastDiscardSeat >= 0 ? State.SeatToPlayer(State.LastDiscardSeat) : -1;
                    var consumed = ExtractPairTiles(p, 3);
                    MahjongTile? calledTile = GetLastDiscardTile(targetPlayer);

                    State.PlayerDrawCount[player]++;
                    AddCallFromPair(player, (int)seat, p, 3, 2);
                    AddLastDiscardToCall(player);
                    if (player == 0) State.UpdateIsClosed();
                    State.UpdateAvailableTiles();

                    if (UseMjaiModel && calledTile.HasValue)
                        _mjaiClient.OnPon(player, targetPlayer, calledTile.Value, consumed);

                    if (player == 0)
                        return UseMjaiModel ? RunMjaiAnalysis() : RunAnalysis();
                    break;
                }
                case 0x0600: // chi
                {
                    int targetPlayer = State.LastDiscardSeat >= 0 ? State.SeatToPlayer(State.LastDiscardSeat) : -1;
                    var consumed = ExtractPairTiles(p, 3);
                    MahjongTile? calledTile = GetLastDiscardTile(targetPlayer);

                    State.PlayerDrawCount[player]++;
                    AddCallFromPair(player, (int)seat, p, 3, 2);
                    AddLastDiscardToCall(player);
                    if (player == 0) State.UpdateIsClosed();
                    State.UpdateAvailableTiles();

                    if (UseMjaiModel && calledTile.HasValue)
                        _mjaiClient.OnChi(player, targetPlayer, calledTile.Value, consumed);

                    if (player == 0)
                        return UseMjaiModel ? RunMjaiAnalysis() : RunAnalysis();
                    break;
                }
                case 0x0130: // ankan
                {
                    var consumed = ExtractPairTiles(p, 3, 4);

                    AddCallFromPair(player, (int)seat, p, 3, 2);
                    AddCallFromPair(player, (int)seat, p, 4, 2);
                    if (player == 0) State.UpdateIsClosed();
                    State.UpdateAvailableTiles();

                    if (UseMjaiModel && consumed.Count > 0)
                        _mjaiClient.OnAnkan(player, consumed);
                    break;
                }
                case 0x0400: // daiminkan
                {
                    int targetPlayer = State.LastDiscardSeat >= 0 ? State.SeatToPlayer(State.LastDiscardSeat) : -1;
                    var consumed = ExtractPairTiles(p, 3, 4);
                    MahjongTile? calledTile = GetLastDiscardTile(targetPlayer);

                    AddCallFromPair(player, (int)seat, p, 3, 2);
                    AddCallFromPair(player, (int)seat, p, 4, 2);
                    AddLastDiscardToCall(player);
                    if (player == 0) State.UpdateIsClosed();
                    State.UpdateAvailableTiles();

                    if (UseMjaiModel && calledTile.HasValue)
                        _mjaiClient.OnDaiminkan(player, targetPlayer, calledTile.Value, consumed);
                    break;
                }
            }
            return null;
        }

        private List<MahjongTile> ExtractPairTiles(uint[] p, params int[] pairIndices)
        {
            var tiles = new List<MahjongTile>();
            foreach (int idx in pairIndices)
            {
                if (idx >= p.Length) continue;
                uint val = p[idx];
                uint hi = (val >> 16) & 0xFFFF;
                uint lo = val & 0xFFFF;
                if (hi < 136) tiles.Add(MahjongTile.FromTileId136(hi));
                if (lo < 136) tiles.Add(MahjongTile.FromTileId136(lo));
            }
            return tiles;
        }

        private MahjongTile? GetLastDiscardTile(int targetPlayer)
        {
            if (targetPlayer < 0 || targetPlayer >= 4) return null;
            var discs = State.Discards[targetPlayer];
            return discs.Count > 0 ? discs[discs.Count - 1] : (MahjongTile?)null;
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

        private void AddLastDiscardToCall(int player)
        {
            if (State.LastDiscardSeat < 0) return;
            int discardPlayer = State.SeatToPlayer(State.LastDiscardSeat);
            if (discardPlayer < 0 || discardPlayer >= 4) return;
            var discs = State.Discards[discardPlayer];
            if (discs.Count == 0) return;

            var tile = discs[discs.Count - 1];
            tile.From = State.LastDiscardSeat;
            State.Calls[player].Add(tile);

            discs.RemoveAt(discs.Count - 1);
            var snaps = State.DiscardDrawSnapshots[discardPlayer];
            if (snaps.Count > 0) snaps.RemoveAt(snaps.Count - 1);
            if (discardPlayer != 0)
            {
                var safety = State.PlayerDiscardSafetyList[discardPlayer];
                if (safety.Count > 0) safety.RemoveAt(safety.Count - 1);
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
                if (CheckTsumo())
                    return FormatTsumo();

                if (State.PlayerRiichi[0])
                {
                    LastTtsMessage = null;
                    LastOverlayJson = null;
                    return null;
                }

                bool isFirstDraw = State.PlayerDrawCount[0] == 1;
                int uniqueTH = isFirstDraw ? CountUniqueTerminalHonors() : 0;
                bool kyuushuEligible = isFirstDraw && uniqueTH >= 9;

                State.CurrentStrategy = _offense.DetermineStrategy();
                var priorities = _offense.GetTilePriorities();
                if (priorities == null || priorities.Count == 0) return null;

                if (kyuushuEligible && State.CurrentStrategy != Strategy.ThirteenOrphans
                    && priorities[0].Shanten >= 4)
                {
                    LastTtsMessage = "九种九牌流局";
                    LastOverlayJson = BuildKyuushuJson(uniqueTH);
                    return FormatKyuushu(uniqueTH);
                }

                _offense.SortOutUnsafeTiles(priorities);

                bool riichi = false;
                if (priorities[0].Shanten == 0)
                    riichi = _offense.ShouldRiichi(priorities[0]);

                bool announceKokushi = kyuushuEligible
                    && State.CurrentStrategy == Strategy.ThirteenOrphans;

                LastOverlayJson = BuildDiscardJson(priorities, riichi, announceKokushi);
                return FormatAnalysis(priorities, announceKokushi);
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
                if (State.PlayerRiichi[0])
                {
                    var ronOnly = _offense.CheckRonOnly(discardedTile);
                    if (ronOnly == null) return null;
                    var ronList = new List<CallAdvice> { ronOnly };
                    LastOverlayJson = BuildCallJson(ronList, discardedTile, discardingPlayer);
                    return FormatCallAnalysis(ronList, discardedTile, discardingPlayer);
                }

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

        #region MJAI Analysis

        private string RunMjaiAnalysis()
        {
            if (State.OwnHand.Count < 2) return null;
            try
            {
                var resp = _mjaiClient.RequestDecision();
                if (!resp.Success)
                {
                    LastOverlayJson = null;
                    LastTtsMessage = null;
                    return "[MJAI] " + resp.Error;
                }
                LastOverlayJson = BuildMjaiOverlayJson(resp, "analysis");
                return FormatMjaiResponse(resp, "摸牌");
            }
            catch (Exception ex)
            {
                LastOverlayJson = null;
                LastTtsMessage = null;
                return $"[MJAI] 分析异常: {ex.Message}";
            }
        }

        private bool HasPotentialCalls(MahjongTile discardedTile, int discardingPlayer)
        {
            if (State.OwnHand.Count < 4) return false;

            int sameCount = TileUtils.Count(State.OwnHand, discardedTile.Index, discardedTile.Type);
            if (sameCount >= 2) return true;

            if (discardingPlayer == 3 && discardedTile.Type != 3)
            {
                int idx = discardedTile.Index;
                int tp = discardedTile.Type;
                bool hasLow2 = idx >= 3 && TileUtils.Count(State.OwnHand, idx - 2, tp) > 0
                                         && TileUtils.Count(State.OwnHand, idx - 1, tp) > 0;
                bool hasMid = idx >= 2 && idx <= 8 && TileUtils.Count(State.OwnHand, idx - 1, tp) > 0
                                                    && TileUtils.Count(State.OwnHand, idx + 1, tp) > 0;
                bool hasHigh2 = idx <= 7 && TileUtils.Count(State.OwnHand, idx + 1, tp) > 0
                                          && TileUtils.Count(State.OwnHand, idx + 2, tp) > 0;
                if (hasLow2 || hasMid || hasHigh2) return true;
            }

            int callTriples = ShantenCalculator.GetTriplesOnly(State.Calls[0]).Count / 3;
            var tap = ShantenCalculator.GetTriplesAndPairs(
                new List<MahjongTile>(State.OwnHand) { discardedTile });
            if (ShantenCalculator.IsWinning(tap.Triples.Count / 3 + callTriples, tap.Pairs.Count / 2))
                return true;
            if (State.IsClosed)
            {
                var pairs = ShantenCalculator.GetPairsAsArray(
                    new List<MahjongTile>(State.OwnHand) { discardedTile });
                if (pairs.Count / 2 >= 7) return true;
            }

            return false;
        }

        private string RunMjaiCallAnalysis(MahjongTile discardedTile, int discardingPlayer)
        {
            if (State.OwnHand.Count < 4) return null;

            bool canCall = HasPotentialCalls(discardedTile, discardingPlayer);
            if (!canCall) return null;

            try
            {
                var resp = _mjaiClient.RequestDecision();
                if (!resp.Success)
                {
                    LastOverlayJson = null;
                    LastTtsMessage = null;
                    return "[MJAI] " + resp.Error;
                }
                if (resp.Type == "none")
                {
                    string[] skipNames = { "自家", "下家", "对面", "上家" };
                    string skipSrc = discardingPlayer >= 0 && discardingPlayer < 4
                        ? skipNames[discardingPlayer] : "?";
                    LastTtsMessage = "跳过";
                    LastOverlayJson = BuildMjaiCallOverlayJson(resp, discardedTile, skipSrc);
                    return $"▶ {skipSrc}打出 {discardedTile.Name} — [MJAI] 跳过（不鸣牌）";
                }
                string[] playerNames = { "自家", "下家", "对面", "上家" };
                string src = discardingPlayer >= 0 && discardingPlayer < 4
                    ? playerNames[discardingPlayer] : "?";
                LastOverlayJson = BuildMjaiCallOverlayJson(resp, discardedTile, src);
                return FormatMjaiCallResponse(resp, discardedTile, src);
            }
            catch (Exception ex)
            {
                LastOverlayJson = null;
                LastTtsMessage = null;
                return $"[MJAI] 鸣牌分析异常: {ex.Message}";
            }
        }

        private string FormatMjaiResponse(MjaiResponse resp, string context)
        {
            var sb = new StringBuilder();
            string displayType = resp.GetDisplayType();
            string tileName = resp.Pai != null ? MjaiClient.MjaiTileDisplayName(resp.Pai) : "";

            switch (resp.Type)
            {
                case "dahai":
                    sb.AppendLine($"[MJAI] 推荐: 切{tileName} ({resp.Pai}){(resp.Tsumogiri ? " [摸切]" : "")}");
                    LastTtsMessage = $"切{tileName}";
                    break;
                case "reach":
                    sb.AppendLine($"[MJAI] 推荐: 立直 切{tileName} ({resp.Pai})");
                    LastTtsMessage = $"切{tileName} 立直";
                    break;
                case "hora":
                    sb.AppendLine($"[MJAI] 推荐: 自摸和了!");
                    LastTtsMessage = "自摸";
                    break;
                case "ankan":
                    string ankanTiles = resp.Consumed != null ? string.Join(" ", resp.Consumed) : "";
                    sb.AppendLine($"[MJAI] 推荐: 暗杠 [{ankanTiles}]");
                    LastTtsMessage = "暗杠";
                    break;
                case "kakan":
                    sb.AppendLine($"[MJAI] 推荐: 加杠 {tileName}");
                    LastTtsMessage = "加杠";
                    break;
                case "ryukyoku":
                    sb.AppendLine($"[MJAI] 推荐: 九种九牌流局");
                    LastTtsMessage = "九种九牌流局";
                    break;
                default:
                    sb.AppendLine($"[MJAI] 响应: {displayType}");
                    LastTtsMessage = displayType;
                    break;
            }

            var handSorted = TileUtils.Sort(State.OwnHand);
            sb.Append("手牌: ");
            foreach (var t in handSorted) sb.Append(t.Name + " ");

            return sb.ToString().TrimEnd();
        }

        private string FormatMjaiCallResponse(MjaiResponse resp, MahjongTile discardedTile, string playerName)
        {
            var sb = new StringBuilder();
            string displayType = resp.GetDisplayType();
            string tileName = resp.Pai != null ? MjaiClient.MjaiTileDisplayName(resp.Pai) : "";
            string consumed = resp.Consumed != null
                ? string.Join("+", resp.Consumed) : "";

            sb.AppendLine($"▶ {playerName}打出 {discardedTile.Name} — MJAI推荐:");

            switch (resp.Type)
            {
                case "pon":
                    sb.AppendLine($"  ★推荐 碰 [{consumed}]");
                    LastTtsMessage = "碰";
                    break;
                case "chi":
                    sb.AppendLine($"  ★推荐 吃 [{consumed}]");
                    LastTtsMessage = "吃";
                    break;
                case "daiminkan":
                    sb.AppendLine($"  ★推荐 大明杠 [{consumed}]");
                    LastTtsMessage = "大明杠";
                    break;
                case "hora":
                    sb.AppendLine($"  ★推荐 荣和! ロン!");
                    LastTtsMessage = "荣和";
                    break;
                default:
                    sb.AppendLine($"  {displayType}");
                    LastTtsMessage = displayType;
                    break;
            }

            return sb.ToString().TrimEnd();
        }

        private string BuildMjaiOverlayJson(MjaiResponse resp, string context)
        {
            string tts = LastTtsMessage ?? resp.GetDisplayType();
            string tilePai = resp.Pai ?? "";
            string tileName = !string.IsNullOrEmpty(tilePai) ? MjaiClient.MjaiTileDisplayName(tilePai) : "";
            bool riichi = resp.Type == "reach";

            var sb = new StringBuilder(512);
            sb.Append("{\"type\":\"discard\"");
            sb.Append(",\"strategy\":\"MJAI\"");
            sb.Append(",\"shanten\":0");
            sb.Append(",\"tilesLeft\":0");
            sb.Append(",\"wind\":\"\"");
            sb.Append(",\"bestTile\":\"").Append(Esc(tilePai)).Append('"');
            sb.Append(",\"bestTileName\":\"").Append(Esc(tileName)).Append('"');
            sb.Append(",\"riichi\":").Append(riichi ? "true" : "false");
            sb.Append(",\"isFold\":false");
            sb.Append(",\"kokushi\":false");
            sb.Append(",\"tts\":\"").Append(Esc(tts)).Append('"');
            sb.Append(",\"recommendations\":[");
            if (!string.IsNullOrEmpty(tilePai))
            {
                sb.Append("{\"rank\":1");
                sb.Append(",\"tile\":\"").Append(Esc(tilePai)).Append('"');
                sb.Append(",\"tileName\":\"").Append(Esc(tileName)).Append('"');
                sb.Append(",\"priority\":999.0,\"efficiency\":0,\"danger\":0,\"shanten\":0,\"safe\":true}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string BuildMjaiCallOverlayJson(MjaiResponse resp, MahjongTile discardedTile, string playerName)
        {
            string tts = LastTtsMessage ?? resp.GetDisplayType();
            string callType = resp.GetDisplayType();

            var sb = new StringBuilder(512);
            sb.Append("{\"type\":\"call\"");
            sb.Append(",\"player\":\"").Append(Esc(playerName)).Append('"');
            sb.Append(",\"discardedTile\":\"").Append(Esc(discardedTile.Name)).Append('"');
            sb.Append(",\"tts\":\"").Append(Esc(tts)).Append('"');
            sb.Append(",\"advices\":[{");
            sb.Append("\"callType\":\"").Append(Esc(callType)).Append('"');
            sb.Append(",\"recommended\":").Append(resp.Type != "none" ? "true" : "false");
            if (resp.Consumed != null && resp.Consumed.Count > 0)
                sb.Append(",\"tiles\":\"").Append(Esc(string.Join("+", resp.Consumed))).Append('"');
            if (!string.IsNullOrEmpty(tts))
                sb.Append(",\"reason\":\"MJAI\"");
            sb.Append("}]}");
            return sb.ToString();
        }

        #endregion

        private int CountUniqueTerminalHonors()
        {
            var all = TileUtils.GetAllTerminalHonor(State.OwnHand);
            var unique = new List<MahjongTile>();
            foreach (var t in all)
            {
                bool exists = false;
                foreach (var u in unique) if (t.IsSame(u)) { exists = true; break; }
                if (!exists) unique.Add(t);
            }
            return unique.Count;
        }

        private string FormatKyuushu(int uniqueTH)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"★ 九種九牌流局 ({uniqueTH}种幺九牌) ★");
            var handSorted = TileUtils.Sort(State.OwnHand);
            sb.Append("手牌: ");
            foreach (var t in handSorted) sb.Append(t.Name + " ");
            return sb.ToString().TrimEnd();
        }

        private string BuildKyuushuJson(int uniqueTH)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"type\":\"kyuushu\"");
            sb.Append($",\"uniqueTH\":{uniqueTH}");
            sb.Append(",\"tts\":\"九种九牌流局\"");

            sb.Append(",\"hand\":[");
            var handSorted = TileUtils.Sort(State.OwnHand);
            for (int i = 0; i < handSorted.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"\"{Esc(handSorted[i].Name)}\"");
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// 检查当前手牌（摸牌后）是否已经和了。
        /// </summary>
        private bool CheckTsumo()
        {
            int callTriples = State.Calls[0].Count / 3;
            var decomp = ShantenCalculator.GetTriplesAndPairs(State.OwnHand);
            int handTriples = decomp.Triples.Count / 3;
            int handPairs = decomp.Pairs.Count / 2;

            bool normalWin = handTriples + callTriples >= 4 && handPairs >= 1;
            bool chiitoitsuWin = State.IsClosed && handPairs >= 7;

            if (normalWin || chiitoitsuWin)
            {
                if (!State.IsClosed)
                {
                    var fullHand = new List<MahjongTile>(State.OwnHand);
                    fullHand.AddRange(State.Calls[0]);
                    var yaku = YakuEvaluator.GetYaku(fullHand, State.Calls[0], decomp,
                        chiitoitsuWin, State.SeatWind, State.RoundWind);
                    if (yaku.Open < 1) return false;
                }
                LastTtsMessage = "自摸";
                LastOverlayJson = BuildTsumoJson();
                return true;
            }
            return false;
        }

        private string FormatTsumo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("★★★ 自摸和了! ツモ! ★★★");
            var handSorted = TileUtils.Sort(State.OwnHand);
            sb.Append("手牌: ");
            foreach (var t in handSorted) sb.Append(t.Name + " ");
            return sb.ToString().TrimEnd();
        }

        private string BuildTsumoJson()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"type\":\"tsumo\"");
            sb.Append(",\"tts\":\"自摸\"");

            sb.Append(",\"hand\":[");
            var handSorted = TileUtils.Sort(State.OwnHand);
            for (int i = 0; i < handSorted.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"\"{Esc(handSorted[i].Name)}\"");
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        private string FormatAnalysis(List<TilePriority> tiles, bool announceKokushi = false)
        {
            var sb = new StringBuilder();
            string[] stratNames = { "一般型", "七对子", "国士无双", "弃和" };
            string stratName = (int)State.CurrentStrategy < stratNames.Length
                ? stratNames[(int)State.CurrentStrategy] : "?";

            int topShanten = tiles.Count > 0 ? tiles[0].Shanten : -1;
            if (announceKokushi)
                sb.AppendLine("★ 九種九牌 → 国士无双 ★");
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
                if (best.Waits > 0)
                {
                    if (State.IsClosed)
                    {
                        bool riichi = _offense.ShouldRiichi(best);
                        string riichiAdvice = riichi ? "建议立直" : "不建议立直";
                        sb.AppendLine($"听牌分析: 待牌={best.Waits:F1} 形状={best.Shape:F2} → {riichiAdvice}");
                    }
                    else
                    {
                        sb.AppendLine($"听牌分析: 待牌={best.Waits:F1} 形状={best.Shape:F2} (副露中)");
                    }
                }
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

        private string BuildDiscardJson(List<TilePriority> tiles, bool riichi,
            bool announceKokushi = false)
        {
            string[] stratNames = { "一般型", "七对子", "国士无双", "弃和" };
            string strategy = (int)State.CurrentStrategy < stratNames.Length
                ? stratNames[(int)State.CurrentStrategy] : "?";
            int shanten = tiles.Count > 0 ? tiles[0].Shanten : -1;
            var best = tiles[0];
            bool isFold = State.CurrentStrategy == Strategy.Fold;

            string tts;
            if (isFold)
                tts = $"弃和 切{best.Tile.Name}";
            else if (riichi && shanten == 0)
                tts = $"切{best.Tile.Name} 立直";
            else
                tts = $"切{best.Tile.Name}";
            if (announceKokushi)
                tts = $"国士无双 {tts}";
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
            sb.Append($",\"kokushi\":{(announceKokushi ? "true" : "false")}");
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

            string tts = "跳过";
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
