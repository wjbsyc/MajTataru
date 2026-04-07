using System;
using System.Collections.Generic;

namespace MajTataru
{
    public struct YakuResult
    {
        public double Open;
        public double Closed;
        public YakuResult(double o, double c) { Open = o; Closed = c; }
    }

    public static class YakuEvaluator
    {
        public static YakuResult GetYaku(
            List<MahjongTile> inputHand,
            List<MahjongTile> inputCalls,
            DecompositionResult triplesAndPairs,
            bool chiitoitsu,
            int seatWind,
            int roundWind,
            bool isConsideringCall = false)
        {
            var callsFiltered = FilterKanFourth(inputCalls);
            var hand = new List<MahjongTile>(inputHand);
            hand.AddRange(callsFiltered);

            DecompositionResult tap;
            if (triplesAndPairs != null)
            {
                tap = new DecompositionResult
                {
                    Triples = new List<MahjongTile>(triplesAndPairs.Triples),
                    Pairs = new List<MahjongTile>(triplesAndPairs.Pairs)
                };
                tap.Triples.AddRange(callsFiltered);
            }
            else
            {
                tap = ShantenCalculator.GetTriplesAndPairs(hand);
            }

            var triplets = ShantenCalculator.GetTripletsAsArray(hand);
            var sequences = GetSequenceTiles(hand, tap, callsFiltered);

            double open = 0, closed = 0;

            if (!chiitoitsu)
            {
                var yh = GetYakuhai(triplets, seatWind, roundWind);
                open += yh; closed += yh;
            }

            var tanyao = GetTanyao(hand, tap, callsFiltered);
            open += tanyao; closed += tanyao;

            if (!chiitoitsu)
            {
                var iip = GetIipeikou(sequences);
                closed += iip;

                if (!isConsideringCall)
                {
                    var san = GetSanankou(inputHand);
                    open += san; closed += san;
                }

                var toi = GetToitoi(triplets);
                open += toi; closed += toi;

                var sdk = GetSanshokuDoukou(triplets);
                open += sdk; closed += sdk;

                var sdj = GetSanshokuDoujun(sequences);
                open += sdj.Open; closed += sdj.Closed;

                var ssg = GetShousangen(hand);
                open += ssg; closed += ssg;

                var dsg = GetDaisangen(hand);
                open += dsg; closed += dsg;
            }

            var chanta = GetChanta(triplets, tap.Pairs, sequences);
            open += chanta.Open; closed += chanta.Closed;

            var honrou = GetHonroutou(triplets);
            open += honrou.Open; closed += honrou.Closed;

            var junchan = GetJunchan(triplets, tap.Pairs, sequences);
            open += junchan.Open; closed += junchan.Closed;

            var ittsuu = GetIttsuu(sequences);
            open += ittsuu.Open; closed += ittsuu.Closed;

            var honitsu = GetHonitsu(hand);
            open += honitsu.Open; closed += honitsu.Closed;

            var chinitsu = GetChinitsu(hand);
            open += chinitsu.Open; closed += chinitsu.Closed;

            return new YakuResult(open, closed);
        }

        private static List<MahjongTile> FilterKanFourth(List<MahjongTile> calls)
        {
            var result = new List<MahjongTile>();
            var counts = new Dictionary<long, int>();
            foreach (var t in calls)
            {
                long key = t.Type * 100 + t.Index;
                int c;
                counts.TryGetValue(key, out c);
                if (c < 3) { result.Add(t); counts[key] = c + 1; }
            }
            return result;
        }

        private static List<MahjongTile> GetSequenceTiles(
            List<MahjongTile> hand, DecompositionResult tap, List<MahjongTile> calls)
        {
            var tripletsAndPairs = new List<MahjongTile>(tap.Triples);
            tripletsAndPairs.AddRange(tap.Pairs);
            var remaining = TileUtils.Remove(hand, ShantenCalculator.GetTripletsAsArray(hand));
            remaining = TileUtils.Remove(remaining, tap.Pairs);
            var seqs = ShantenCalculator.GetBestSequenceCombination(remaining);
            var callSeqs = GetCallSequences(calls);
            seqs.AddRange(callSeqs);
            return seqs;
        }

        private static List<MahjongTile> GetCallSequences(List<MahjongTile> calls)
        {
            var result = new List<MahjongTile>();
            for (int i = 0; i + 2 < calls.Count; i += 3)
            {
                if (calls[i].Type != 3 && !calls[i].IsSame(calls[i + 1]) &&
                    calls[i].Type == calls[i + 1].Type && calls[i].Type == calls[i + 2].Type)
                {
                    result.Add(calls[i]); result.Add(calls[i + 1]); result.Add(calls[i + 2]);
                }
            }
            return result;
        }

        private static double GetYakuhai(List<MahjongTile> triplets, int seatWind, int roundWind)
        {
            double yaku = 0;
            for (int i = 0; i + 2 < triplets.Count; i += 3)
            {
                var t = triplets[i];
                if (t.Type != 3) continue;
                if (t.Index > 4) yaku++;
                if (t.Index == seatWind) yaku++;
                if (t.Index == roundWind && roundWind != seatWind) yaku++;
                if (t.Index == seatWind && seatWind == roundWind) yaku++;
            }
            return yaku;
        }

        private static double GetTanyao(List<MahjongTile> hand, DecompositionResult tap, List<MahjongTile> calls)
        {
            int termCount = 0;
            foreach (var t in hand)
                if (t.IsTerminalOrHonor()) termCount++;
            if (termCount > hand.Count - 14) return 0;

            foreach (var t in calls)
                if (t.IsTerminalOrHonor()) return 0;
            foreach (var t in tap.Pairs)
                if (t.IsTerminalOrHonor()) return 0;
            for (int i = 0; i + 2 < tap.Triples.Count; i += 3)
                if (tap.Triples[i].IsTerminalOrHonor()) return 0;
            return 1;
        }

        private static double GetIipeikou(List<MahjongTile> sequences)
        {
            for (int i = 0; i + 5 < sequences.Count; i += 3)
            {
                var a = sequences[i];
                if (a.Type == 3) continue;
                for (int j = i + 3; j + 2 < sequences.Count; j += 3)
                {
                    var b = sequences[j];
                    if (a.Type == b.Type && a.Index == b.Index &&
                        sequences[i + 1].Index == sequences[j + 1].Index &&
                        sequences[i + 2].Index == sequences[j + 2].Index)
                        return 1;
                }
            }
            return 0;
        }

        private static double GetSanankou(List<MahjongTile> hand)
        {
            var trips = ShantenCalculator.GetTripletsAsArray(hand);
            return trips.Count / 3 >= 3 ? 2 : 0;
        }

        private static double GetToitoi(List<MahjongTile> triplets)
        {
            return triplets.Count / 3 >= 4 ? 2 : 0;
        }

        private static double GetSanshokuDoukou(List<MahjongTile> triplets)
        {
            for (int idx = 1; idx <= 9; idx++)
            {
                int types = 0;
                for (int tp = 0; tp <= 2; tp++)
                {
                    int c = 0;
                    for (int i = 0; i < triplets.Count; i++)
                        if (triplets[i].Index == idx && triplets[i].Type == tp) c++;
                    if (c >= 3) types++;
                }
                if (types >= 3) return 2;
            }
            return 0;
        }

        private static YakuResult GetSanshokuDoujun(List<MahjongTile> sequences)
        {
            for (int idx = 1; idx <= 7; idx++)
            {
                bool[] found = new bool[3];
                for (int i = 0; i + 2 < sequences.Count; i += 3)
                {
                    var s = sequences[i];
                    if (s.Type < 3 && s.Index == idx) found[s.Type] = true;
                }
                if (found[0] && found[1] && found[2]) return new YakuResult(1, 2);
            }
            return new YakuResult(0, 0);
        }

        private static double GetShousangen(List<MahjongTile> hand)
        {
            int total = 0;
            bool allThree = true;
            for (int i = 5; i <= 7; i++)
            {
                int c = TileUtils.Count(hand, i, 3);
                total += c;
                if (c < 2) allThree = false;
            }
            if (total >= 8 && allThree)
            {
                bool notDai = false;
                for (int i = 5; i <= 7; i++)
                    if (TileUtils.Count(hand, i, 3) < 3) notDai = true;
                if (notDai) return 2;
            }
            return 0;
        }

        private static double GetDaisangen(List<MahjongTile> hand)
        {
            for (int i = 5; i <= 7; i++)
                if (TileUtils.Count(hand, i, 3) < 3) return 0;
            return 10;
        }

        private static YakuResult GetChanta(List<MahjongTile> triplets, List<MahjongTile> pairs, List<MahjongTile> sequences)
        {
            if (sequences.Count < 3) return new YakuResult(0, 0);

            int seqTerminal = 0;
            for (int i = 0; i + 2 < sequences.Count; i += 3)
            {
                if (sequences[i].Index == 1 || sequences[i + 2].Index == 9)
                    seqTerminal++;
            }
            if (seqTerminal * 3 != sequences.Count) return new YakuResult(0, 0);

            foreach (var t in pairs)
                if (!t.IsTerminalOrHonor()) return new YakuResult(0, 0);
            for (int i = 0; i + 2 < triplets.Count; i += 3)
                if (!triplets[i].IsTerminalOrHonor()) return new YakuResult(0, 0);

            return new YakuResult(1, 2);
        }

        private static YakuResult GetHonroutou(List<MahjongTile> triplets)
        {
            int term = 0;
            foreach (var t in triplets)
                if (t.IsTerminalOrHonor()) term++;
            return term >= 13 ? new YakuResult(3, 2) : new YakuResult(0, 0);
        }

        private static YakuResult GetJunchan(List<MahjongTile> triplets, List<MahjongTile> pairs, List<MahjongTile> sequences)
        {
            if (sequences.Count < 3) return new YakuResult(0, 0);

            int seqTerminal = 0;
            for (int i = 0; i + 2 < sequences.Count; i += 3)
                if (sequences[i].Index == 1 || sequences[i + 2].Index == 9) seqTerminal++;
            if (seqTerminal * 3 != sequences.Count) return new YakuResult(0, 0);

            foreach (var t in pairs)
                if (t.Type == 3 || (t.Index != 1 && t.Index != 9)) return new YakuResult(0, 0);
            for (int i = 0; i + 2 < triplets.Count; i += 3)
                if (triplets[i].Type == 3 || (triplets[i].Index != 1 && triplets[i].Index != 9))
                    return new YakuResult(0, 0);

            return new YakuResult(1, 1);
        }

        private static YakuResult GetIttsuu(List<MahjongTile> sequences)
        {
            for (int tp = 0; tp <= 2; tp++)
            {
                bool[] found = new bool[10];
                for (int i = 0; i + 2 < sequences.Count; i += 3)
                {
                    if (sequences[i].Type != tp) continue;
                    found[sequences[i].Index] = true;
                    found[sequences[i + 1].Index] = true;
                    found[sequences[i + 2].Index] = true;
                }
                bool all = true;
                for (int j = 1; j <= 9; j++) if (!found[j]) { all = false; break; }
                if (all) return new YakuResult(1, 2);
            }
            return new YakuResult(0, 0);
        }

        private static YakuResult GetHonitsu(List<MahjongTile> hand)
        {
            for (int tp = 0; tp <= 2; tp++)
            {
                int count = 0;
                foreach (var t in hand)
                    if (t.Type == tp || t.Type == 3) count++;
                if (count >= hand.Count) return new YakuResult(2, 3);
            }
            return new YakuResult(0, 0);
        }

        private static YakuResult GetChinitsu(List<MahjongTile> hand)
        {
            for (int tp = 0; tp <= 2; tp++)
            {
                int count = 0;
                foreach (var t in hand)
                    if (t.Type == tp) count++;
                if (count >= hand.Count) return new YakuResult(3, 3);
            }
            return new YakuResult(0, 0);
        }
    }
}
