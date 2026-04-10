namespace MajTataru
{
    public static class MahjongOpcodes
    {
        public const uint MAHJONG_ZONE_ID = 0x33F;

        public const int HEADER_SIZE = 32;
        public const int OPCODE_OFFSET = 18;

        public static ushort OP_GAME_INIT = 0x00D7;
        public static ushort OP_ROUND_START = 0x0134;
        public static ushort OP_DISCARD = 0x0141;
        public static ushort OP_DRAW_EVENT = 0x01DC;
        public static ushort OP_TSUMO_RESULT = 0x02DE;
        public static ushort OP_RON_RESULT = 0x007E;
        public static ushort OP_ROUND_END = 0x00EF;
        public static ushort OP_SETTLEMENT = 0x00E0;
        public static ushort OP_GAME_RESULT = 0x03DD;
        public static ushort OP_BOARD_HEARTBEAT = 0x0096;
        public static ushort OP_TIMER_HEARTBEAT = 0x02D8;
    }
}
