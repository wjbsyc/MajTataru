namespace MajTataru
{
    public static class MahjongOpcodes
    {
        public const uint MAHJONG_ZONE_ID = 0x33F;

        public const int HEADER_SIZE = 32;
        public const int OPCODE_OFFSET = 18;

        public static ushort OP_GAME_INIT = 0x021B;
        public static ushort OP_ROUND_START = 0x021E;
        public static ushort OP_DISCARD = 0x025E;
        public static ushort OP_DRAW_EVENT = 0x013F;
        public static ushort OP_TSUMO_RESULT = 0x013D;
        public static ushort OP_RON_RESULT = 0x02C4;
        public static ushort OP_ROUND_END = 0x031D;
        public static ushort OP_SETTLEMENT = 0x022E;
        public static ushort OP_GAME_RESULT = 0x02F2;
        public static ushort OP_BOARD_HEARTBEAT = 0x02AD;
        public static ushort OP_TIMER_HEARTBEAT = 0x029C;
    }
}
