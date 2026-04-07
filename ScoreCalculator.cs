using System;
using System.Collections.Generic;

namespace MajTataru
{
    public static class ScoreCalculator
    {
        public static double CalculateScore(bool isDealer, double han, double fu = 30)
        {
            double score = fu * Math.Pow(2, 2 + han) * 4;
            if (han > 4) score = 8000;
            if (han > 5) score = 8000 + ((han - 5) * 4000);
            if (han > 6) score = 12000 + ((han - 6) * 2000);
            if (han > 8) score = 16000 + ((han - 8) * 2666);
            if (han > 11) score = 24000 + ((han - 11) * 4000);
            if (han >= 13) score = 32000;
            if (isDealer) score *= 1.5;
            return score;
        }

        public static double CalculateFu(
            List<MahjongTile> triples,
            List<MahjongTile> openTiles,
            List<MahjongTile> pair,
            List<MahjongTile> waitTiles,
            MahjongTile winningTile,
            bool isClosed,
            int seatWind,
            int roundWind,
            bool ron = true)
        {
            double fu = 20;

            var sequences = ShantenCalculator.GetSequences(triples);
            var closedTriplets = ShantenCalculator.GetTripletCombos(triples);
            var openTriplets = ShantenCalculator.GetTripletCombos(openTiles);
            var openTripleFlat = ShantenCalculator.GetTripletsAsArray(openTiles);
            var kans = TileUtils.Remove(openTiles, openTripleFlat);

            foreach (var t in closedTriplets)
            {
                if (t.Tile1.IsTerminalOrHonor())
                    fu += t.Tile1.IsSame(winningTile) ? 4 : 8;
                else
                    fu += t.Tile1.IsSame(winningTile) ? 2 : 4;
            }

            foreach (var t in openTriplets)
                fu += t.Tile1.IsTerminalOrHonor() ? 4 : 2;

            foreach (var tile in kans)
            {
                bool isOpen = false;
                foreach (var ot in openTiles)
                    if (ot.IsSame(tile) && ot.From >= 0) { isOpen = true; break; }
                if (isOpen)
                    fu += tile.IsTerminalOrHonor() ? 12 : 6;
                else
                    fu += tile.IsTerminalOrHonor() ? 28 : 14;
            }

            if (pair.Count > 0 && pair[0].IsValueTile(seatWind, roundWind))
            {
                fu += 2;
                if (pair[0].Type == 3 && pair[0].Index == seatWind && seatWind == roundWind)
                    fu += 2;
            }

            bool hasRyanmen = false;
            foreach (var seq in sequences)
            {
                if ((seq.Tile1.IsSame(winningTile) && seq.Tile3.Index < 9) ||
                    (seq.Tile3.IsSame(winningTile) && seq.Tile1.Index > 1))
                {
                    hasRyanmen = true;
                    break;
                }
            }

            if (fu == 20 && hasRyanmen)
            {
                // pinfu-like: no additional fu
            }
            else if (waitTiles == null || waitTiles.Count != 2 ||
                     waitTiles[0].Type != waitTiles[1].Type ||
                     Math.Abs(waitTiles[0].Index - waitTiles[1].Index) != 1)
            {
                bool isShanpon = false;
                foreach (var t in closedTriplets)
                    if (t.Tile1.IsSame(winningTile)) { isShanpon = true; break; }
                if (!isShanpon)
                    fu += 2;
            }

            if (ron && isClosed) fu += 10;

            return Math.Ceiling(fu / 10) * 10;
        }
    }
}
