using System;
using System.Collections.Generic;
using System.Text;

namespace MajTataru
{
    public class MahjongGameState
    {
        public bool InGame;
        public bool InMahjongZone;
        /// <summary>
        /// 客户端当前风位座位号 (0=东,1=南,2=西,3=北)，每轮根据庄家轮转而变化。
        /// 来源: 0x0134 ROUND_START 的 p[6]。
        /// </summary>
        public int MySeat = -1;
        /// <summary>
        /// 客户端的初始物理座位（整局游戏不变）。
        /// 来源: 0x0134 ROUND_START 的 p[5]。
        /// </summary>
        public int InitialSeat = -1;
        public uint RoundIndex;
        public uint Honba;
        public uint RiichiSticks;
        public uint DoraIndicator;
        public List<uint> DoraIndicators = new List<uint>();
        public int[] Scores = new int[4];
        public string[] Hand;
        public int RoundCount;
        public int TurnCount;
        public string LastDiscard;
        public int LastDiscardSeat = -1;
        public int LastDrawSeat = -1;
        public bool[] PlayerRiichi = new bool[4];

        private static readonly string[] WindNames = { "东", "南", "西", "北" };
        private static readonly string[] RelNames = { "自己", "下家", "对家", "上家" };

        public void Reset()
        {
            InGame = false;
            MySeat = -1;
            InitialSeat = -1;
            RoundIndex = 0;
            Honba = 0;
            RiichiSticks = 0;
            DoraIndicator = 0;
            DoraIndicators = new List<uint>();
            Scores = new int[4];
            Hand = null;
            RoundCount = 0;
            TurnCount = 0;
            LastDiscard = null;
            LastDiscardSeat = -1;
            LastDrawSeat = -1;
            PlayerRiichi = new bool[4];
        }

        /// <summary>
        /// 风位座位号 → 相对玩家ID (0=自己, 1=下家, 2=对家, 3=上家)。
        /// 座位号直接代表风位: 0=东, 1=南, 2=西, 3=北。
        /// 摸牌/出牌顺序: 东(0)→南(1)→西(2)→北(3)。
        /// </summary>
        public int SeatToPlayerId(uint seat)
        {
            if (MySeat < 0) return (int)seat;
            return (int)((seat - (uint)MySeat + 4) % 4);
        }

        /// <summary>
        /// 获取座位的显示标签，如 "P0(自己/东)" "P1(下家/南)"
        /// </summary>
        public string GetPlayerTag(uint seat)
        {
            int pid = SeatToPlayerId(seat);
            string rel = pid < 4 ? RelNames[pid] : "?";
            string wind = seat < 4 ? WindNames[seat] : "?";
            return $"P{pid}({rel}/{wind})";
        }

        /// <summary>
        /// 获取座位的座风名（座位号即风位: 0=东,1=南,2=西,3=北）
        /// </summary>
        public string GetWindName(uint seat)
        {
            if (seat >= 4) return "?";
            return WindNames[seat];
        }
    }

    public class ParseResult
    {
        public string Tag;
        public string Summary;
        public bool IsRelevant;

        public ParseResult(string tag, string summary, bool relevant = true)
        {
            Tag = tag;
            Summary = summary;
            IsRelevant = relevant;
        }
    }

    public class MahjongParser
    {
        public MahjongGameState State { get; } = new MahjongGameState();

        public event Action<string> OnMessage;

        private const uint MAHJONG_ZONE_ID = 0x33F;

        /*
         * FFXIV IPC 包在 NetworkReceived 的 byte[] 中的布局：
         *   Offset  0-3:  PacketSize    (uint32 LE)
         *   Offset  4-7:  SourceActor   (uint32 LE)
         *   Offset  8-11: TargetActor   (uint32 LE)
         *   Offset 12-13: SegmentType   (uint16 LE, 0x0014 = IPC)
         *   Offset 14-15: Padding
         *   Offset 16-17: Reserved      (uint16 LE)
         *   Offset 18-19: Opcode        (uint16 LE) ★
         *   Offset 20-21: Padding
         *   Offset 22-23: ServerID      (uint16 LE)
         *   Offset 24-27: Timestamp     (uint32 LE)
         *   Offset 28-31: Padding
         *   Offset 32+:   IPC Payload   ★
         *
         * Payload 中每个字段为 uint32 LE，依次对应文档中的 F0, F1, F2, ...
         */
        private const int HEADER_SIZE = 32;
        private const int OPCODE_OFFSET = 18;

        // Opcodes — 随版本变化，公开以便外部修改
        public ushort OP_GAME_INIT = 0x00D7;
        public ushort OP_ROUND_START = 0x0134;
        public ushort OP_DISCARD = 0x0141;
        public ushort OP_DRAW_EVENT = 0x01DC;
        public ushort OP_TSUMO_RESULT = 0x02DE;
        public ushort OP_RON_RESULT = 0x007E;
        public ushort OP_ROUND_END = 0x00EF;
        public ushort OP_SETTLEMENT = 0x00E0;
        public ushort OP_GAME_RESULT = 0x03DD;
        public ushort OP_BOARD_HEARTBEAT = 0x0096;
        public ushort OP_TIMER_HEARTBEAT = 0x02D8;

        private static readonly Dictionary<uint, string> DiscardActions = new Dictionary<uint, string>
        {
            { 0x0110, "手出" },
            { 0x0111, "立直手出" },
            { 0x0112, "摸切" },
            { 0x0113, "立直摸切" },
            { 0x0210, "杠后手出" },
            { 0x0211, "杠后立直手出" },
            { 0x0212, "杠后摸切" },
            { 0x0213, "杠后立直摸切" },
            { 0x0A10, "鸣牌后打牌" },
        };

        private static bool IsRiichiAction(uint actionType)
        {
            return (actionType & 0x01) != 0 && (actionType & 0xFF00) != 0x0A00;
        }

        private static readonly Dictionary<uint, string> DrawActions = new Dictionary<uint, string>
        {
            { 0x0100, "摸牌" },
            { 0x0130, "暗杠" },
            { 0x0200, "岭上摸牌" },
            { 0x0400, "加杠" },
            { 0x0500, "碰" },
            { 0x0600, "吃" },
        };

        #region 二进制包解析入口（主要数据源 — NetworkReceived）

        /// <summary>
        /// 解析 NetworkReceived 回调中的原始二进制网络包。
        /// 返回非 null 表示这是一个麻将相关包。
        /// </summary>
        public ParseResult ParseBinaryPacket(byte[] message)
        {
            if (message == null || message.Length < HEADER_SIZE)
                return null;

            ushort opcode = BitConverter.ToUInt16(message, OPCODE_OFFSET);

            if (!IsMahjongOpcode(opcode))
                return null;

            if (!State.InMahjongZone)
                State.InMahjongZone = true;

            uint[] payload = ExtractBinaryPayload(message, HEADER_SIZE);
            return DispatchOpcode(opcode, payload);
        }

        /// <summary>
        /// 将 byte[] message 格式化为 type 252 风格的十六进制字符串（用于 debug 日志）
        /// </summary>
        public static string FormatBinaryAsHex(byte[] message)
        {
            if (message == null || message.Length < 4)
                return "(empty)";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < message.Length; i += 4)
            {
                if (i > 0) sb.Append('|');
                if (i + 4 <= message.Length)
                    sb.Append(BitConverter.ToUInt32(message, i).ToString("X8"));
                else
                {
                    for (int j = i; j < message.Length; j++)
                        sb.Append(message[j].ToString("X2"));
                }
            }
            return sb.ToString();
        }

        private uint[] ExtractBinaryPayload(byte[] data, int offset)
        {
            int count = (data.Length - offset) / 4;
            uint[] payload = new uint[count];
            for (int i = 0; i < count; i++)
                payload[i] = BitConverter.ToUInt32(data, offset + i * 4);
            return payload;
        }

        #endregion

        #region ACT 日志行解析入口（辅助数据源 — BeforeLogLineRead）

        /// <summary>
        /// 解析一条 ACT 日志行。用于区域变更/队伍列表检测，
        /// 以及 FFXIV 插件 debug 模式下的 type 252 回退解析。
        /// </summary>
        public ParseResult ParseLogLine(string logLine)
        {
            if (string.IsNullOrEmpty(logLine)) return null;

            string[] parts = logLine.Split('|');
            if (parts.Length < 2) return null;

            int logType;
            if (!int.TryParse(parts[0], out logType)) return null;

            switch (logType)
            {
                case 1:
                    return ParseZoneChange(parts);
                case 11:
                    return ParsePartyList(parts);
                case 252:
                    return ParseNetworkPacketText(parts);
                default:
                    return null;
            }
        }

        private ParseResult ParseZoneChange(string[] parts)
        {
            if (parts.Length < 3) return null;
            uint zoneId;
            if (!TryParseHex(parts[2], out zoneId)) return null;

            if (zoneId == MAHJONG_ZONE_ID)
            {
                State.InMahjongZone = true;
                string zoneName = parts.Length > 3 ? parts[3] : "曼德维尔魔导方城";
                return new ParseResult("ZONE", $"进入麻将区域: {zoneName} (0x{zoneId:X})");
            }
            else if (State.InMahjongZone)
            {
                State.InMahjongZone = false;
                State.Reset();
                return new ParseResult("ZONE", $"离开麻将区域 → 0x{zoneId:X}");
            }
            return null;
        }

        private ParseResult ParsePartyList(string[] parts)
        {
            if (!State.InMahjongZone || parts.Length < 4) return null;

            StringBuilder sb = new StringBuilder("牌桌玩家: ");
            int count;
            if (int.TryParse(parts[2], out count))
            {
                for (int i = 0; i < count && i + 3 < parts.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"[{i}]{parts[i + 3]}");
                }
            }
            return new ParseResult("PARTY", sb.ToString());
        }

        private ParseResult ParseNetworkPacketText(string[] parts)
        {
            if (parts.Length < 11) return null;

            uint opcodeSegment;
            if (!TryParseHex(parts[6], out opcodeSegment)) return null;

            ushort opcode = (ushort)(opcodeSegment >> 16);

            if (!IsMahjongOpcode(opcode)) return null;

            if (!State.InMahjongZone)
                State.InMahjongZone = true;

            int endIndex = parts.Length - 1;
            uint[] payload = ExtractTextPayload(parts, 10, endIndex);

            return DispatchOpcode(opcode, payload);
        }

        private uint[] ExtractTextPayload(string[] parts, int startIndex, int endIndex)
        {
            List<uint> payload = new List<uint>();
            for (int i = startIndex; i < endIndex; i++)
            {
                string field = parts[i].Trim();
                if (string.IsNullOrEmpty(field)) continue;
                uint val;
                if (TryParseHex(field, out val))
                    payload.Add(val);
            }
            return payload.ToArray();
        }

        #endregion

        #region 共用 Opcode 分发

        private bool IsMahjongOpcode(ushort opcode)
        {
            return opcode == OP_GAME_INIT || opcode == OP_ROUND_START ||
                   opcode == OP_DISCARD || opcode == OP_DRAW_EVENT ||
                   opcode == OP_TSUMO_RESULT || opcode == OP_RON_RESULT ||
                   opcode == OP_ROUND_END || opcode == OP_SETTLEMENT ||
                   opcode == OP_GAME_RESULT;
        }

        private ParseResult DispatchOpcode(ushort opcode, uint[] payload)
        {
            if (opcode == OP_GAME_INIT) return ParseGameInit(payload);
            if (opcode == OP_ROUND_START) return ParseRoundStart(payload);
            if (opcode == OP_DISCARD) return ParseDiscard(payload);
            if (opcode == OP_DRAW_EVENT) return ParseDrawEvent(payload);
            if (opcode == OP_TSUMO_RESULT) return ParseTsumoResult(payload);
            if (opcode == OP_RON_RESULT) return ParseRonResult(payload);
            if (opcode == OP_ROUND_END) return ParseRoundEnd(payload);
            if (opcode == OP_SETTLEMENT) return ParseSettlement(payload);
            if (opcode == OP_GAME_RESULT) return ParseGameResult(payload);
            return null;
        }

        #endregion

        #region Opcode Handlers

        private ParseResult ParseGameInit(uint[] p)
        {
            if (p.Length < 12) return null;

            uint gameType = p[0];
            State.MySeat = (int)p[6];
            State.InGame = true;
            State.RoundCount = 0;

            int[] scores = new int[4];
            for (int i = 0; i < 4; i++)
                scores[i] = TileDecoder.DecodeScore(p[7 + i]);
            State.Scores = scores;

            string typeStr = gameType == 2 ? "半庄战" : $"类型{gameType}";
            string scoreStr = string.Join("/", scores);

            RaiseMessage($"=== 游戏开始 === {typeStr} | 分数: {scoreStr}");

            return new ParseResult("GAME_INIT",
                $"游戏开始 | {typeStr} | 初始分数: {scoreStr}");
        }

        private ParseResult ParseRoundStart(uint[] p)
        {
            if (p.Length < 26) return null;

            State.RoundIndex = p[1] * 4 + p[2];
            State.Honba = p[3];
            State.RiichiSticks = p[4];
            State.InitialSeat = (int)p[5];
            State.MySeat = (int)p[6];
            State.DoraIndicator = p[7];
            State.DoraIndicators = new List<uint> { p[7] };
            State.TurnCount = 0;
            State.RoundCount++;
            State.PlayerRiichi = new bool[4];
            State.LastDrawSeat = -1;

            for (int i = 0; i < 4; i++)
                State.Scores[i] = TileDecoder.DecodeScore(p[8 + i]);

            string[] tiles = new string[13];
            for (int i = 0; i < 13; i++)
                tiles[i] = TileDecoder.DecodeTile34(p[12 + i]);
            State.Hand = tiles;

            string roundName = TileDecoder.GetRoundName(State.RoundIndex, State.Honba);
            string doraStr = TileDecoder.DecodeTile34(State.DoraIndicator);
            string handStr = string.Join(" ", tiles);
            string sticksInfo = State.RiichiSticks > 0 ? $" | 供托={State.RiichiSticks}" : "";

            StringBuilder scoreSb = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) scoreSb.Append(" / ");
                scoreSb.Append($"{State.GetPlayerTag((uint)i)}:{State.Scores[i]}");
            }
            string scoreStr = scoreSb.ToString();

            string dealerTag = State.GetPlayerTag(0);
            RaiseMessage($"--- {roundName} ---  庄={dealerTag}  宝牌指示={doraStr}{sticksInfo}");
            RaiseMessage($"    分数: {scoreStr}");
            RaiseMessage($"    手牌: {handStr}");

            return new ParseResult("ROUND_START",
                $"{roundName} | 庄={dealerTag} | 宝牌指示={doraStr}{sticksInfo} | {scoreStr} | 手牌=[{handStr}]");
        }

        private const uint ACTION_KAKAN_DECL = 0x0140;

        private ParseResult ParseDiscard(uint[] p)
        {
            if (p.Length < 8) return null;

            uint seat = p[0];
            uint actionType = p[2];
            uint tileData = p[3];
            string playerTag = State.GetPlayerTag(seat);

            if (actionType == ACTION_KAKAN_DECL)
            {
                uint kakanTileId = p[4] & 0xFFFF;
                string kakanTile = kakanTileId < 136
                    ? TileDecoder.DecodeTile136(kakanTileId) : "?";
                State.LastDrawSeat = -1;
                return new ParseResult("KAKAN", $"{playerTag} 加杠 {kakanTile}");
            }

            uint tileId = tileData & 0xFFFF;
            string tileName = tileId < 136
                ? TileDecoder.DecodeTile136(tileId)
                : $"?(0x{tileData:X8})";

            string actionName;
            if (!DiscardActions.TryGetValue(actionType, out actionName))
                actionName = $"动作0x{actionType:X4}";

            bool isRiichi = IsRiichiAction(actionType);
            if (isRiichi && seat < 4)
            {
                State.PlayerRiichi[seat] = true;
                RaiseMessage($"    ★ {playerTag} 立直宣言！打 {tileName}");
            }

            State.TurnCount++;
            State.LastDiscard = tileName;
            State.LastDiscardSeat = (int)seat;
            State.LastDrawSeat = -1;

            string tag = isRiichi ? "RIICHI" : "DISCARD";

            return new ParseResult(tag,
                $"{playerTag} {actionName}: {tileName} (0x{tileId:X2})");
        }

        private ParseResult ParseDrawEvent(uint[] p)
        {
            if (p.Length < 6) return null;

            uint seat = p[0];
            uint actionType = p[1];
            uint fieldA = p[2];

            string actionName;
            if (!DrawActions.TryGetValue(actionType, out actionName))
                actionName = $"事件0x{actionType:X4}";

            string playerTag = State.GetPlayerTag(seat);

            switch (actionType)
            {
                case 0x0100:
                {
                    State.LastDrawSeat = (int)seat;
                    uint tileId = fieldA & 0xFFFF;
                    bool isSelf = ((int)seat == State.MySeat);
                    bool visible = tileId < 136 && (isSelf || tileId > 0);
                    string tileName = visible ? TileDecoder.DecodeTile136(tileId) : "---";
                    return new ParseResult("DRAW",
                        $"{playerTag} 摸牌: {tileName}" + (visible ? $" (0x{tileId:X2})" : ""));
                }
                case 0x0500:
                {
                    string tileInfo = DecodePairField(p, 3);
                    string fromTag = State.LastDiscardSeat >= 0
                        ? State.GetPlayerTag((uint)State.LastDiscardSeat) : "?";
                    string fromInfo = State.LastDiscardSeat >= 0
                        ? $" ← {fromTag}的{State.LastDiscard}" : "";
                    return new ParseResult("PON", $"{playerTag} 碰{fromInfo} [{tileInfo}]");
                }
                case 0x0600:
                {
                    string tileInfo = DecodePairField(p, 3);
                    string fromTag = State.LastDiscardSeat >= 0
                        ? State.GetPlayerTag((uint)State.LastDiscardSeat) : "?";
                    string fromInfo = State.LastDiscardSeat >= 0
                        ? $" ← {fromTag}的{State.LastDiscard}" : "";
                    return new ParseResult("CHI", $"{playerTag} 吃{fromInfo} [{tileInfo}]");
                }
                case 0x0130:
                {
                    string pair1 = DecodePairField(p, 3);
                    string pair2 = DecodePairField(p, 4);
                    return new ParseResult("ANKAN", $"{playerTag} 暗杠 [{pair1}, {pair2}]");
                }
                case 0x0400:
                {
                    string tileInfo = DecodePairField(p, 3);
                    bool isDaiminkan = (State.LastDrawSeat != (int)seat);
                    if (isDaiminkan)
                    {
                        string fromTag = State.LastDiscardSeat >= 0
                            ? State.GetPlayerTag((uint)State.LastDiscardSeat) : "?";
                        string fromInfo = State.LastDiscardSeat >= 0
                            ? $" ← {fromTag}的{State.LastDiscard}" : "";
                        return new ParseResult("DAIMINKAN", $"{playerTag} 大明杠{fromInfo} [{tileInfo}]");
                    }
                    return new ParseResult("KAKAN", $"{playerTag} 加杠 [{tileInfo}]");
                }
                case 0x0200:
                {
                    uint tileId = fieldA & 0xFFFF;
                    uint newDoraId = (fieldA >> 16) & 0xFFFF;
                    bool isSelf = ((int)seat == State.MySeat);
                    bool visible = tileId < 136 && (isSelf || tileId > 0);
                    string tileName = visible ? TileDecoder.DecodeTile136(tileId) : "---";

                    string doraPart = "";
                    if (newDoraId < 136)
                    {
                        string doraName = TileDecoder.DecodeTile136(newDoraId);
                        State.DoraIndicators.Add(newDoraId);
                        doraPart = $" | 新宝牌指示={doraName}";
                        RaiseMessage($"    ★ 新增宝牌指示牌: {doraName}");
                    }

                    return new ParseResult("RINSHAN",
                        $"{playerTag} 岭上摸牌: {tileName}" + (visible ? $" (0x{tileId:X2})" : "") + doraPart);
                }
                default:
                    return new ParseResult("DRAW_EVENT", $"{playerTag} {actionName} A=0x{fieldA:X8}");
            }
        }

        private ParseResult ParseTsumoResult(uint[] p)
        {
            if (p.Length < 26) return null;

            uint winnerSeat = p[4];
            int points = (int)p[5] * 100;
            int tileCount = (int)p[1];

            for (int i = 0; i < 4 && i + 9 < p.Length; i++)
                State.Scores[i] = (int)p[9 + i] * 100;

            var tiles = new List<string>();
            for (int i = 0; i < tileCount && i + 13 < p.Length; i++)
                tiles.Add(TileDecoder.DecodeTile34(p[13 + i]));

            string winnerTag = State.GetPlayerTag(winnerSeat);
            string tileStr = tiles.Count > 0 ? string.Join(" ", tiles) : "";
            string roundName = TileDecoder.GetRoundName(State.RoundIndex, State.Honba);

            var sb = new StringBuilder();
            sb.Append($"自摸! {winnerTag} {points}点");
            sb.Append($" | 手牌=[{tileStr}]");
            for (int i = 0; i < 4; i++)
                sb.Append($" | {State.GetPlayerTag((uint)i)}:{State.Scores[i]}");

            RaiseMessage($"★ {roundName} {winnerTag} 自摸 {points}点");

            return new ParseResult("TSUMO", sb.ToString());
        }

        private ParseResult ParseRonResult(uint[] p)
        {
            if (p.Length < 26) return null;

            uint winnerSeat = p[4];
            int points = (int)p[5] * 100;
            int tileCount = (int)p[1];

            for (int i = 0; i < 4 && i + 9 < p.Length; i++)
                State.Scores[i] = (int)p[9 + i] * 100;

            var tiles = new List<string>();
            for (int i = 0; i < tileCount && i + 13 < p.Length; i++)
                tiles.Add(TileDecoder.DecodeTile34(p[13 + i]));

            string winnerTag = State.GetPlayerTag(winnerSeat);
            string loserTag = State.LastDiscardSeat >= 0
                ? State.GetPlayerTag((uint)State.LastDiscardSeat) : "?";
            string ronTile = State.LastDiscard ?? "?";
            string tileStr = tiles.Count > 0 ? string.Join(" ", tiles) : "";
            string roundName = TileDecoder.GetRoundName(State.RoundIndex, State.Honba);

            var sb = new StringBuilder();
            sb.Append($"荣和! {winnerTag} {points}点 ← {loserTag}的{ronTile}");
            sb.Append($" | 手牌=[{tileStr}]");
            for (int i = 0; i < 4; i++)
                sb.Append($" | {State.GetPlayerTag((uint)i)}:{State.Scores[i]}");

            RaiseMessage($"★ {roundName} {winnerTag} 荣和 {points}点 ← {loserTag}的{ronTile}");

            return new ParseResult("RON", sb.ToString());
        }

        private ParseResult ParseRoundEnd(uint[] p)
        {
            if (p.Length < 66) return null;

            uint resultType = p[0];
            uint winInfo = p[1];
            bool isDraw = winInfo == 0xFF;

            StringBuilder sb = new StringBuilder();
            sb.Append(isDraw ? "流局" : "和了");
            sb.Append($" (type=0x{resultType:X}, win=0x{winInfo:X})");

            for (int block = 0; block < 4; block++)
            {
                int offset = 2 + block * 16;
                uint score = p[offset];
                int scorePoints = TileDecoder.DecodeScore(score);

                List<string> tiles = new List<string>();
                for (int t = 1; t < 16; t++)
                {
                    uint val = p[offset + t];
                    uint baseVal = val & 0xFF;
                    if (val != 0xFF && val != 0xFFFFFFFF && baseVal <= 0x21)
                        tiles.Add(TileDecoder.DecodeTile34(val));
                }

                string tileStr = tiles.Count > 0 ? string.Join(" ", tiles) : "(无/全鸣)";
                string blockTag = State.GetPlayerTag((uint)block);
                sb.Append($" | {blockTag}: {scorePoints}点 [{tileStr}]");
            }

            string roundName = TileDecoder.GetRoundName(State.RoundIndex, State.Honba);
            RaiseMessage($"    {roundName} 结束 → {(isDraw ? "流局" : "和了")}");

            return new ParseResult("ROUND_END", sb.ToString());
        }

        private ParseResult ParseSettlement(uint[] p)
        {
            if (p.Length < 1) return null;
            return new ParseResult("SETTLE", $"结算通知: 0x{p[0]:X8}");
        }

        private ParseResult ParseGameResult(uint[] p)
        {
            if (p.Length < 8) return null;

            State.InGame = false;
            StringBuilder sb = new StringBuilder("游戏结束");
            for (int i = 0; i < Math.Min(p.Length, 8); i++)
                sb.Append($" F{i}=0x{p[i]:X}");

            RaiseMessage($"=== 游戏结束 === 共 {State.RoundCount} 回合");

            return new ParseResult("GAME_END", sb.ToString());
        }

        #endregion

        #region Helpers

        private string DecodePairField(uint[] p, int index)
        {
            if (index >= p.Length) return "?";
            uint val = p[index];
            uint high = (val >> 16) & 0xFFFF;
            uint low = val & 0xFFFF;

            if (high == 0xFFFF && low == 0xFFFF) return "---";
            if (val == 0) return "(隐藏)";

            List<string> tiles = new List<string>();
            if (high < 136) tiles.Add(TileDecoder.DecodeTile136(high));
            else if (high != 0xFFFF && high != 0) tiles.Add($"?{high:X4}");
            if (low < 136) tiles.Add(TileDecoder.DecodeTile136(low));
            else if (low != 0xFFFF && low != 0) tiles.Add($"?{low:X4}");

            return tiles.Count > 0 ? string.Join("+", tiles) : $"(0x{val:X8})";
        }

        private static bool TryParseHex(string hex, out uint result)
        {
            result = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            hex = hex.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);
            return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out result);
        }

        private void RaiseMessage(string msg)
        {
            OnMessage?.Invoke(msg);
        }

        #endregion
    }
}
