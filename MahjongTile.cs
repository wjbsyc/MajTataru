using System;
using System.Collections.Generic;

namespace MajTataru
{
    /// <summary>
    /// AI用の牌表現。JS AIと同じ形式: Type 0=筒,1=万,2=索,3=字 / Index 1-9(数牌) or 1-7(字牌)
    /// </summary>
    public struct MahjongTile
    {
        public int Type;
        public int Index;
        public bool Dora;
        public int DoraValue;
        public int From; // call tiles: seat of provider, -1 if self

        public MahjongTile(int type, int index, bool dora = false)
        {
            Type = type;
            Index = index;
            Dora = dora;
            DoraValue = 0;
            From = -1;
        }

        public static MahjongTile FromType34(uint value)
        {
            bool isRed = (value & 0x100) != 0;
            uint t = value & 0xFF;
            if (t <= 8) return new MahjongTile(1, (int)t + 1, isRed);
            if (t <= 17) return new MahjongTile(0, (int)t - 8, isRed);
            if (t <= 26) return new MahjongTile(2, (int)t - 17, isRed);
            if (t <= 33) return new MahjongTile(3, (int)t - 26);
            return new MahjongTile(-1, 0);
        }

        public static MahjongTile FromTileId136(uint tileId)
        {
            uint type34 = tileId / 4;
            uint copy = tileId % 4;
            bool isRed = (type34 == 4 || type34 == 13 || type34 == 22) && copy == 1;
            return FromType34(isRed ? (type34 | 0x100) : type34);
        }

        public uint ToType34()
        {
            uint b;
            switch (Type)
            {
                case 1: b = (uint)(Index - 1); break;
                case 0: b = (uint)(Index + 8); break;
                case 2: b = (uint)(Index + 17); break;
                case 3: b = (uint)(Index + 26); break;
                default: return 0xFF;
            }
            return Dora ? (b | 0x100) : b;
        }

        public bool IsSame(MahjongTile o, bool checkDora = false)
        {
            if (checkDora)
                return Index == o.Index && Type == o.Type && Dora == o.Dora;
            return Index == o.Index && Type == o.Type;
        }

        public bool IsTerminalOrHonor()
        {
            if (Type == 3) return true;
            return Index == 1 || Index == 9;
        }

        public bool IsValid()
        {
            if (Type < 0 || Type > 3) return false;
            if (Type == 3) return Index >= 1 && Index <= 7;
            return Index >= 1 && Index <= 9;
        }

        public bool IsValueTile(int seatWind, int roundWind)
        {
            return Type == 3 && (Index > 4 || Index == seatWind || Index == roundWind);
        }

        public string Name
        {
            get { return TileDecoder.DecodeTile34(ToType34()); }
        }

        public string ShortName
        {
            get
            {
                string[] tn = { "p", "m", "s", "z" };
                if (Type < 0 || Type > 3) return "?";
                if (Dora && Type != 3) return "0" + tn[Type];
                return Index.ToString() + tn[Type];
            }
        }

        public override string ToString() { return ShortName; }
    }

    public class TileCombination
    {
        public MahjongTile Tile1;
        public MahjongTile Tile2;
        public MahjongTile Tile3;
        public bool IsTriple;

        public TileCombination(MahjongTile t1, MahjongTile t2)
        {
            Tile1 = t1; Tile2 = t2; Tile3 = default; IsTriple = false;
        }

        public TileCombination(MahjongTile t1, MahjongTile t2, MahjongTile t3)
        {
            Tile1 = t1; Tile2 = t2; Tile3 = t3; IsTriple = true;
        }
    }

    public static class TileUtils
    {
        public static List<MahjongTile> Sort(List<MahjongTile> tiles)
        {
            var s = new List<MahjongTile>(tiles);
            s.Sort((a, b) =>
            {
                if (a.Type != b.Type) return a.Type.CompareTo(b.Type);
                if (a.Index != b.Index) return a.Index.CompareTo(b.Index);
                return b.DoraValue.CompareTo(a.DoraValue);
            });
            return s;
        }

        public static List<MahjongTile> Remove(List<MahjongTile> from, List<MahjongTile> toRemove)
        {
            var r = new List<MahjongTile>(from);
            foreach (var t in toRemove)
            {
                for (int i = 0; i < r.Count; i++)
                {
                    if (t.IsSame(r[i]))
                    {
                        r.RemoveAt(i);
                        break;
                    }
                }
            }
            return r;
        }

        public static List<MahjongTile> GetTiles(List<MahjongTile> tiles, int index, int type)
        {
            var r = new List<MahjongTile>();
            foreach (var t in tiles)
                if (t.Index == index && t.Type == type) r.Add(t);
            return r;
        }

        public static int Count(List<MahjongTile> tiles, int index, int type)
        {
            int c = 0;
            foreach (var t in tiles)
                if (t.Index == index && t.Type == type) c++;
            return c;
        }

        public static int GetHigherIndex(MahjongTile tile)
        {
            if (tile.Type == 3)
            {
                if (tile.Index == 4) return 1;
                return tile.Index == 7 ? 5 : tile.Index + 1;
            }
            return tile.Index == 9 ? 1 : tile.Index + 1;
        }

        public static int GetDoraValue(MahjongTile tile, List<MahjongTile> indicators)
        {
            int dr = 0;
            foreach (var d in indicators)
                if (d.Type == tile.Type && GetHigherIndex(d) == tile.Index) dr++;
            if (tile.Dora) dr++;
            return dr;
        }

        public static void UpdateDoraValues(List<MahjongTile> tiles, List<MahjongTile> indicators)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                t.DoraValue = GetDoraValue(t, indicators);
                tiles[i] = t;
            }
        }

        public static int TotalDora(List<MahjongTile> tiles)
        {
            int dr = 0;
            foreach (var t in tiles) dr += t.DoraValue;
            return dr;
        }

        public static double TotalDora(List<MahjongTile> tiles, List<MahjongTile> indicators)
        {
            double dr = 0;
            foreach (var t in tiles) dr += GetDoraValue(t, indicators);
            return dr;
        }

        public static bool Contains(List<MahjongTile> tiles, int index, int type)
        {
            foreach (var t in tiles)
                if (t.Index == index && t.Type == type) return true;
            return false;
        }

        public static void PushIfNotExists(List<MahjongTile> tiles, int index, int type,
            List<MahjongTile> doraIndicators)
        {
            if (Contains(tiles, index, type)) return;
            var t = new MahjongTile(type, index);
            t.DoraValue = GetDoraValue(t, doraIndicators);
            tiles.Add(t);
        }

        public static List<MahjongTile> GetUsefulTilesForDouble(List<MahjongTile> hand,
            List<MahjongTile> doraIndicators)
        {
            var tiles = new List<MahjongTile>();
            foreach (var tile in hand)
            {
                PushIfNotExists(tiles, tile.Index, tile.Type, doraIndicators);
                if (tile.Type == 3) continue;
                if (tile.Index - 1 >= 1)
                    PushIfNotExists(tiles, tile.Index - 1, tile.Type, doraIndicators);
                if (tile.Index + 1 <= 9)
                    PushIfNotExists(tiles, tile.Index + 1, tile.Type, doraIndicators);
                if (tile.Index - 2 >= 1)
                    PushIfNotExists(tiles, tile.Index - 2, tile.Type, doraIndicators);
                if (tile.Index + 2 <= 9)
                    PushIfNotExists(tiles, tile.Index + 2, tile.Type, doraIndicators);
            }
            return tiles;
        }

        public static List<MahjongTile> GetUsefulTilesForTriple(List<MahjongTile> hand,
            List<MahjongTile> doraIndicators)
        {
            var tiles = new List<MahjongTile>();
            foreach (var tile in hand)
            {
                int amt = Count(hand, tile.Index, tile.Type);
                if (tile.Type == 3 && amt >= 2) { PushIfNotExists(tiles, tile.Index, tile.Type, doraIndicators); continue; }
                if (amt >= 2) PushIfNotExists(tiles, tile.Index, tile.Type, doraIndicators);
                if (tile.Type == 3) continue;
                int al = Count(hand, tile.Index - 1, tile.Type);
                int al2 = Count(hand, tile.Index - 2, tile.Type);
                int au = Count(hand, tile.Index + 1, tile.Type);
                int au2 = Count(hand, tile.Index + 2, tile.Type);
                if (tile.Index > 1 && (amt == al + 1 && (au > 0 || al2 > 0)))
                    PushIfNotExists(tiles, tile.Index - 1, tile.Type, doraIndicators);
                if (tile.Index < 9 && (amt == au + 1 && (al > 0 || au2 > 0)))
                    PushIfNotExists(tiles, tile.Index + 1, tile.Type, doraIndicators);
            }
            return tiles;
        }

        public static List<MahjongTile> GetAllTerminalHonor(List<MahjongTile> hand)
        {
            var r = new List<MahjongTile>();
            foreach (var t in hand)
                if (t.IsTerminalOrHonor()) r.Add(t);
            return r;
        }

        public static int GetNumberOfTilesAvailable(int index, int type, List<MahjongTile> visible)
        {
            if (index < 1 || index > 9 || type < 0 || type > 3 || (type == 3 && index > 7))
                return 0;
            int seen = 0;
            foreach (var t in visible)
                if (t.Index == index && t.Type == type) seen++;
            return 4 - seen;
        }
    }
}
