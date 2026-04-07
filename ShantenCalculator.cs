using System;
using System.Collections.Generic;
using System.Linq;

namespace MajTataru
{
    public class DecompositionResult
    {
        public List<MahjongTile> Triples = new List<MahjongTile>();
        public List<MahjongTile> Pairs = new List<MahjongTile>();
        public int Shanten = 8;
    }

    public static class ShantenCalculator
    {
        private static MahjongTile PushTileAndCheckDora(List<MahjongTile> chosenTiles, List<MahjongTile> arrayToPush, MahjongTile tile)
        {
            var stored = tile;
            if (tile.Dora)
            {
                foreach (var t in chosenTiles)
                {
                    if (t.Type == tile.Type && t.Dora)
                    {
                        stored.Dora = false;
                        if (stored.DoraValue > 0) stored.DoraValue--;
                        break;
                    }
                }
            }
            arrayToPush.Add(stored);
            return tile;
        }

        public static int Calculate(int triples, int pairs, int doubles, bool chiitoitsu = false)
        {
            if (chiitoitsu ? (pairs == 7) : (triples == 4 && pairs == 1))
                return -1;

            if ((triples * 3) + (pairs * 2) + (doubles * 2) > 14)
                doubles = (13 - ((triples * 3) + (pairs * 2))) / 2;

            int s = 8 - (2 * triples) - (pairs + doubles);
            if (triples + pairs + doubles >= 5 && pairs == 0) s++;
            if (triples + pairs + doubles >= 6) s += triples + pairs + doubles - 5;
            return s < 0 ? 0 : s;
        }

        public static bool IsWinning(int triples, int pairs, bool chiitoitsu = false)
        {
            return chiitoitsu ? (pairs == 7) : (triples == 4 && pairs == 1);
        }

        public static DecompositionResult GetTriplesAndPairs(List<MahjongTile> tiles)
        {
            var combos = new List<TileCombination>();
            combos.AddRange(GetSequences(tiles));
            combos.AddRange(GetTripletCombos(tiles));
            combos.AddRange(GetPairCombos(tiles));
            return BestCombination(tiles, combos, 0, new DecompositionResult());
        }

        public static List<MahjongTile> GetDoubles(List<MahjongTile> tiles)
        {
            var sorted = TileUtils.Sort(tiles);
            var doubles = new List<MahjongTile>();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (sorted[i].Type == sorted[i + 1].Type &&
                    (sorted[i].Index == sorted[i + 1].Index ||
                     (sorted[i].Type != 3 && sorted[i].Index + 2 >= sorted[i + 1].Index)))
                {
                    doubles.Add(sorted[i]);
                    doubles.Add(sorted[i + 1]);
                    i++;
                }
            }
            return doubles;
        }

        public static List<TileCombination> GetSequences(List<MahjongTile> tiles)
        {
            var sorted = TileUtils.Sort(tiles);
            var seqs = new List<TileCombination>();
            for (int idx = 1; idx <= 7; idx++)
            {
                for (int tp = 0; tp <= 2; tp++)
                {
                    var t1 = TileUtils.GetTiles(sorted, idx, tp);
                    var t2 = TileUtils.GetTiles(sorted, idx + 1, tp);
                    var t3 = TileUtils.GetTiles(sorted, idx + 2, tp);
                    int n = Math.Min(t1.Count, Math.Min(t2.Count, t3.Count));
                    for (int i = 0; i < n; i++)
                        seqs.Add(new TileCombination(t1[i], t2[i], t3[i]));
                }
            }
            return seqs;
        }

        public static List<TileCombination> GetTripletCombos(List<MahjongTile> tiles)
        {
            var sorted = TileUtils.Sort(tiles);
            var trips = new List<TileCombination>();
            int oi = 0, ot = -1;
            foreach (var tile in sorted)
            {
                if (tile.Index != oi || tile.Type != ot)
                {
                    var ts = TileUtils.GetTiles(sorted, tile.Index, tile.Type);
                    if (ts.Count >= 3)
                        trips.Add(new TileCombination(ts[0], ts[1], ts[2]));
                    oi = tile.Index; ot = tile.Type;
                }
            }
            return trips;
        }

        public static List<TileCombination> GetPairCombos(List<MahjongTile> tiles)
        {
            var sorted = TileUtils.Sort(tiles);
            var pairs = new List<TileCombination>();
            int oi = 0, ot = -1;
            foreach (var tile in sorted)
            {
                if (tile.Index != oi || tile.Type != ot)
                {
                    var ts = TileUtils.GetTiles(sorted, tile.Index, tile.Type);
                    if (ts.Count >= 2)
                        pairs.Add(new TileCombination(ts[0], ts[1]));
                    oi = tile.Index; ot = tile.Type;
                }
            }
            return pairs;
        }

        public static List<MahjongTile> GetTripletsAsArray(List<MahjongTile> tiles)
        {
            var combos = GetTripletCombos(tiles);
            var r = new List<MahjongTile>();
            foreach (var c in combos) { r.Add(c.Tile1); r.Add(c.Tile2); r.Add(c.Tile3); }
            return r;
        }

        public static List<MahjongTile> GetBestSequenceCombination(List<MahjongTile> tiles)
        {
            var seqs = GetSequences(tiles);
            return BestCombination(tiles, seqs, 0, new DecompositionResult()).Triples;
        }

        public static List<MahjongTile> GetTriplesOnly(List<MahjongTile> tiles)
        {
            var combos = new List<TileCombination>();
            combos.AddRange(GetSequences(tiles));
            combos.AddRange(GetTripletCombos(tiles));
            return BestCombination(tiles, combos, 0, new DecompositionResult()).Triples;
        }

        public static List<MahjongTile> GetPairsAsArray(List<MahjongTile> tiles)
        {
            var combos = GetPairCombos(tiles);
            var r = new List<MahjongTile>();
            foreach (var c in combos) { r.Add(c.Tile1); r.Add(c.Tile2); }
            return r;
        }

        private static DecompositionResult BestCombination(
            List<MahjongTile> inputTiles,
            List<TileCombination> combos,
            int startIdx,
            DecompositionResult chosen)
        {
            var best = chosen;
            var origTriples = new List<MahjongTile>(chosen.Triples);
            var origPairs = new List<MahjongTile>(chosen.Pairs);

            for (int i = startIdx; i < combos.Count; i++)
            {
                var combo = combos[i];
                var hand = new List<MahjongTile>(inputTiles);

                if (combo.IsTriple)
                {
                    if (TileUtils.Count(hand, combo.Tile1.Index, combo.Tile1.Type) == 0 ||
                        TileUtils.Count(hand, combo.Tile2.Index, combo.Tile2.Type) == 0 ||
                        TileUtils.Count(hand, combo.Tile3.Index, combo.Tile3.Type) == 0)
                        continue;
                    if (combo.Tile1.IsSame(combo.Tile2) &&
                        TileUtils.Count(hand, combo.Tile1.Index, combo.Tile1.Type) < 3)
                        continue;
                }
                else
                {
                    if (combo.Tile1.IsSame(combo.Tile2) &&
                        TileUtils.Count(hand, combo.Tile1.Index, combo.Tile1.Type) < 2)
                        continue;
                }

                var cs = new DecompositionResult
                {
                    Triples = new List<MahjongTile>(origTriples),
                    Pairs = new List<MahjongTile>(origPairs),
                    Shanten = chosen.Shanten
                };

                if (combo.IsTriple)
                {
                    var rt = PushTileAndCheckDora(cs.Pairs.Concat(cs.Triples).ToList(), cs.Triples, combo.Tile1);
                    hand = TileUtils.Remove(hand, new List<MahjongTile> { rt });
                    rt = PushTileAndCheckDora(cs.Pairs.Concat(cs.Triples).ToList(), cs.Triples, combo.Tile2);
                    hand = TileUtils.Remove(hand, new List<MahjongTile> { rt });
                    rt = PushTileAndCheckDora(cs.Pairs.Concat(cs.Triples).ToList(), cs.Triples, combo.Tile3);
                    hand = TileUtils.Remove(hand, new List<MahjongTile> { rt });
                }
                else
                {
                    var rt = PushTileAndCheckDora(cs.Pairs.Concat(cs.Triples).ToList(), cs.Pairs, combo.Tile1);
                    hand = TileUtils.Remove(hand, new List<MahjongTile> { rt });
                    rt = PushTileAndCheckDora(cs.Pairs.Concat(cs.Triples).ToList(), cs.Pairs, combo.Tile2);
                    hand = TileUtils.Remove(hand, new List<MahjongTile> { rt });
                }

                var result = BestCombination(hand, combos, i + 1, cs);

                bool isBetter = result.Triples.Count > best.Triples.Count ||
                    (result.Triples.Count == best.Triples.Count && result.Pairs.Count > best.Pairs.Count) ||
                    (result.Triples.Count == best.Triples.Count && result.Pairs.Count == best.Pairs.Count &&
                     TileUtils.TotalDora(result.Triples) + TileUtils.TotalDora(result.Pairs) >
                     TileUtils.TotalDora(best.Triples) + TileUtils.TotalDora(best.Pairs));
                if (isBetter)
                {
                    best = result;
                }
            }
            return best;
        }
    }
}
