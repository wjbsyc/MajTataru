using System;

namespace MajTataru
{
    public static class TileDecoder
    {
        private static readonly string[] TileNames34 =
        {
            "一万","二万","三万","四万","五万","六万","七万","八万","九万",
            "一筒","二筒","三筒","四筒","五筒","六筒","七筒","八筒","九筒",
            "一索","二索","三索","四索","五索","六索","七索","八索","九索",
            "东","南","西","北","白","發","中"
        };

        private static readonly string[] TileNamesShort =
        {
            "1m","2m","3m","4m","5m","6m","7m","8m","9m",
            "1p","2p","3p","4p","5p","6p","7p","8p","9p",
            "1s","2s","3s","4s","5s","6s","7s","8s","9s",
            "東","南","西","北","白","發","中"
        };

        private static readonly string[] WindNames = { "东", "南", "西", "北" };
        private static readonly string[] RoundWindNames = { "東", "南", "西", "北" };

        /// <summary>
        /// 34牌型编码 → 中文名（含赤宝牌标记）
        /// </summary>
        public static string DecodeTile34(uint value)
        {
            bool isRed = (value & 0x100) != 0;
            uint tileType = value & 0xFF;
            if (tileType > 33) return $"?({value:X})";
            string name = TileNames34[tileType];
            return isRed ? $"赤{name}" : name;
        }

        /// <summary>
        /// 34牌型编码 → 简写（含赤宝牌标记）
        /// </summary>
        public static string DecodeTile34Short(uint value)
        {
            bool isRed = (value & 0x100) != 0;
            uint tileType = value & 0xFF;
            if (tileType > 33) return $"?({value:X})";
            string name = TileNamesShort[tileType];
            return isRed ? $"0{name}" : name;
        }

        /// <summary>
        /// 136牌ID编码 → 中文名。
        /// FFXIV 多玛方城战中赤宝牌为 copy 1 (tileId % 4 == 1)，
        /// 区别于 Tenhou 的 copy 0。
        /// </summary>
        public static string DecodeTile136(uint tileId)
        {
            uint tileType = tileId / 4;
            if (tileType > 33) return $"?({tileId:X})";
            bool isRed = (tileType == 4 || tileType == 13 || tileType == 22) && (tileId % 4 == 1);
            string name = TileNames34[tileType];
            return isRed ? $"赤{name}" : name;
        }

        /// <summary>
        /// 136牌ID编码 → 简写
        /// </summary>
        public static string DecodeTile136Short(uint tileId)
        {
            uint tileType = tileId / 4;
            if (tileType > 33) return $"?({tileId:X})";
            bool isRed = (tileType == 4 || tileType == 13 || tileType == 22) && (tileId % 4 == 1);
            string name = TileNamesShort[tileType];
            return isRed ? $"0{name}" : name;
        }

        /// <summary>
        /// 分数值解码（×100）
        /// </summary>
        public static int DecodeScore(uint value)
        {
            return (int)value * 100;
        }

        /// <summary>
        /// 局数索引 → 场风名
        /// </summary>
        public static string GetRoundName(uint roundIndex, uint honba)
        {
            uint wind = roundIndex / 4;
            uint num = roundIndex % 4 + 1;
            string windStr = wind < 4 ? RoundWindNames[wind] : $"Wind{wind}";
            string result = $"{windStr}{num}局";
            if (honba > 0) result += $" {honba}本場";
            return result;
        }

        /// <summary>
        /// 座位号 → 风位名（相对于庄家）
        /// </summary>
        public static string GetSeatWind(uint seat, uint dealer)
        {
            uint windIndex = (seat - dealer + 4) % 4;
            return WindNames[windIndex];
        }
    }
}
