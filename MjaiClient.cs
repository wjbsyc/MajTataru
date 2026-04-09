using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace MajTataru
{
    public class MjaiClient
    {
        private readonly AIGameState _gs;
        private readonly List<string> _events = new List<string>();
        private bool _gameStarted;

        public string ServerUrl { get; set; } = "http://127.0.0.1:7331";
        public int TimeoutMs { get; set; } = 5000;
        public string LastError { get; private set; }

        public MjaiClient(AIGameState gs) { _gs = gs; }

        public void Reset()
        {
            _events.Clear();
            _gameStarted = false;
        }

        #region Seat Mapping

        private int WindToPhysical(int windSeat)
        {
            int offset = (_gs.PhysicalSeat - _gs.MySeat + 4) % 4;
            return (windSeat + offset) % 4;
        }

        private int PhysicalToWind(int physSeat)
        {
            int offset = (_gs.PhysicalSeat - _gs.MySeat + 4) % 4;
            return (physSeat - offset + 4) % 4;
        }

        private int PlayerToMjai(int player)
        {
            int windSeat = (_gs.MySeat + player) % 4;
            return WindToPhysical(windSeat);
        }

        #endregion

        #region Tile Conversion

        private static readonly string[] HonorNames = { "", "E", "S", "W", "N", "P", "F", "C" };

        public static string TileToMjai(MahjongTile t)
        {
            if (t.Type == 3)
                return (t.Index >= 1 && t.Index <= 7) ? HonorNames[t.Index] : "?";
            string suit;
            switch (t.Type)
            {
                case 0: suit = "p"; break;
                case 1: suit = "m"; break;
                case 2: suit = "s"; break;
                default: return "?";
            }
            if (t.Dora && t.Index == 5) return "5" + suit + "r";
            return t.Index.ToString() + suit;
        }

        public static MahjongTile MjaiToTile(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "?") return new MahjongTile(-1, 0);
            switch (s)
            {
                case "E": return new MahjongTile(3, 1);
                case "S": return new MahjongTile(3, 2);
                case "W": return new MahjongTile(3, 3);
                case "N": return new MahjongTile(3, 4);
                case "P": return new MahjongTile(3, 5);
                case "F": return new MahjongTile(3, 6);
                case "C": return new MahjongTile(3, 7);
            }
            if (s.Length < 2) return new MahjongTile(-1, 0);
            bool red = s.EndsWith("r");
            char suitChar = red ? s[s.Length - 2] : s[s.Length - 1];
            int type;
            switch (suitChar)
            {
                case 'm': type = 1; break;
                case 'p': type = 0; break;
                case 's': type = 2; break;
                default: return new MahjongTile(-1, 0);
            }
            int idx;
            if (!int.TryParse(s.Substring(0, 1), out idx)) return new MahjongTile(-1, 0);
            return new MahjongTile(type, idx, red);
        }

        public static string MjaiTileDisplayName(string mjaiStr)
        {
            var t = MjaiToTile(mjaiStr);
            return t.IsValid() ? t.Name : mjaiStr;
        }

        #endregion

        #region Event Generation

        public void OnGameInit()
        {
            Reset();
            ResetServer();
            _events.Add("{\"type\":\"start_game\",\"id\":" + _gs.PhysicalSeat + "}");
            _gameStarted = true;
        }

        public void OnRoundStart(int honba, int kyotaku)
        {
            string bakaze = _gs.RoundWind == 1 ? "E" : _gs.RoundWind == 2 ? "S" :
                            _gs.RoundWind == 3 ? "W" : "N";
            int oya = WindToPhysical(0);

            var sb = new StringBuilder(512);
            sb.Append("{\"type\":\"start_kyoku\"");
            sb.Append(",\"bakaze\":\"").Append(bakaze).Append('"');
            sb.Append(",\"dora_marker\":\"");
            if (_gs.DoraIndicators.Count > 0) sb.Append(TileToMjai(_gs.DoraIndicators[0]));
            sb.Append('"');
            sb.Append(",\"kyoku\":").Append(_gs.RoundNumber);
            sb.Append(",\"honba\":").Append(honba);
            sb.Append(",\"kyotaku\":").Append(kyotaku);
            sb.Append(",\"oya\":").Append(oya);

            sb.Append(",\"scores\":[");
            for (int phys = 0; phys < 4; phys++)
            {
                if (phys > 0) sb.Append(',');
                int wind = PhysicalToWind(phys);
                sb.Append(wind >= 0 && wind < 4 ? _gs.Scores[wind] : 25000);
            }
            sb.Append(']');

            sb.Append(",\"tehais\":[");
            for (int phys = 0; phys < 4; phys++)
            {
                if (phys > 0) sb.Append(',');
                if (phys == _gs.PhysicalSeat)
                {
                    sb.Append('[');
                    int count = Math.Min(_gs.OwnHand.Count, 13);
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append(TileToMjai(_gs.OwnHand[i])).Append('"');
                    }
                    sb.Append(']');
                }
                else
                {
                    sb.Append("[\"?\",\"?\",\"?\",\"?\",\"?\",\"?\",\"?\",\"?\",\"?\",\"?\",\"?\",\"?\",\"?\"]");
                }
            }
            sb.Append(']');
            sb.Append('}');
            _events.Add(sb.ToString());
        }

        public void OnDraw(int player, MahjongTile? tile)
        {
            int actor = PlayerToMjai(player);
            string pai = (player == 0 && tile.HasValue) ? TileToMjai(tile.Value) : "?";
            _events.Add("{\"type\":\"tsumo\",\"actor\":" + actor + ",\"pai\":\"" + pai + "\"}");
        }

        public void OnDiscard(int player, MahjongTile tile, bool tsumogiri, bool isRiichi)
        {
            int actor = PlayerToMjai(player);
            string pai = TileToMjai(tile);
            if (isRiichi)
                _events.Add("{\"type\":\"reach\",\"actor\":" + actor + "}");
            _events.Add("{\"type\":\"dahai\",\"actor\":" + actor +
                ",\"pai\":\"" + pai + "\",\"tsumogiri\":" + (tsumogiri ? "true" : "false") + "}");
            if (isRiichi)
                _events.Add("{\"type\":\"reach_accepted\",\"actor\":" + actor + "}");
        }

        public void OnPon(int player, int targetPlayer, MahjongTile calledTile, List<MahjongTile> consumed)
        {
            int actor = PlayerToMjai(player);
            int tgt = PlayerToMjai(targetPlayer);
            _events.Add(BuildCallEvent("pon", actor, tgt, calledTile, consumed));
        }

        public void OnChi(int player, int targetPlayer, MahjongTile calledTile, List<MahjongTile> consumed)
        {
            int actor = PlayerToMjai(player);
            int tgt = PlayerToMjai(targetPlayer);
            _events.Add(BuildCallEvent("chi", actor, tgt, calledTile, consumed));
        }

        public void OnAnkan(int player, List<MahjongTile> consumed)
        {
            int actor = PlayerToMjai(player);
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"ankan\",\"actor\":").Append(actor);
            sb.Append(",\"consumed\":[");
            for (int i = 0; i < consumed.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(TileToMjai(consumed[i])).Append('"');
            }
            sb.Append("]}");
            _events.Add(sb.ToString());
        }

        public void OnKakan(int player, MahjongTile tile, List<MahjongTile> existingPon)
        {
            int actor = PlayerToMjai(player);
            string pai = TileToMjai(tile);
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"kakan\",\"actor\":").Append(actor);
            sb.Append(",\"pai\":\"").Append(pai).Append('"');
            sb.Append(",\"consumed\":[");
            for (int i = 0; i < existingPon.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(TileToMjai(existingPon[i])).Append('"');
            }
            sb.Append("]}");
            _events.Add(sb.ToString());
        }

        public void OnDaiminkan(int player, int targetPlayer, MahjongTile calledTile, List<MahjongTile> consumed)
        {
            int actor = PlayerToMjai(player);
            int tgt = PlayerToMjai(targetPlayer);
            _events.Add(BuildCallEvent("daiminkan", actor, tgt, calledTile, consumed));
        }

        public void OnNewDora(MahjongTile doraMarker)
        {
            _events.Add("{\"type\":\"dora\",\"dora_marker\":\"" + TileToMjai(doraMarker) + "\"}");
        }

        public void OnHora(int player, int targetPlayer)
        {
            int actor = PlayerToMjai(player);
            int tgt = PlayerToMjai(targetPlayer);
            _events.Add("{\"type\":\"hora\",\"actor\":" + actor + ",\"target\":" + tgt + "}");
        }

        public void OnRyukyoku()
        {
            _events.Add("{\"type\":\"ryukyoku\"}");
        }

        public void OnRoundEnd()
        {
            _events.Add("{\"type\":\"end_kyoku\"}");
        }

        public void OnGameEnd()
        {
            _events.Add("{\"type\":\"end_game\"}");
            _gameStarted = false;
        }

        private string BuildCallEvent(string type, int actor, int target, MahjongTile pai, List<MahjongTile> consumed)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"").Append(type).Append('"');
            sb.Append(",\"actor\":").Append(actor);
            sb.Append(",\"target\":").Append(target);
            sb.Append(",\"pai\":\"").Append(TileToMjai(pai)).Append('"');
            sb.Append(",\"consumed\":[");
            for (int i = 0; i < consumed.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(TileToMjai(consumed[i])).Append('"');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        #endregion

        #region HTTP Communication

        public void ResetServer()
        {
            try
            {
                string resetUrl = ServerUrl.TrimEnd('/') + "/reset";
                var request = (HttpWebRequest)WebRequest.Create(resetUrl);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Timeout = 3000;
                byte[] empty = Encoding.UTF8.GetBytes("{}");
                request.ContentLength = empty.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(empty, 0, empty.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                    response.Close();
            }
            catch { }
        }

        public MjaiResponse RequestDecision()
        {
            if (!_gameStarted)
                return new MjaiResponse { Success = false, Error = "游戏未开始" };

            try
            {
                var sb = new StringBuilder(4096);
                sb.Append('[');
                for (int i = 0; i < _events.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(_events[i]);
                }
                sb.Append(']');

                string body = sb.ToString();
                var request = (HttpWebRequest)WebRequest.Create(ServerUrl);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Timeout = TimeoutMs;

                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    LastError = null;
                    return ParseResponse(json);
                }
            }
            catch (WebException wex)
            {
                LastError = wex.Message;
                return new MjaiResponse { Success = false, Error = "MJAI服务器连接失败: " + wex.Message };
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new MjaiResponse { Success = false, Error = "MJAI请求异常: " + ex.Message };
            }
        }

        private static MjaiResponse ParseResponse(string json)
        {
            var r = new MjaiResponse { Success = true, RawJson = json };
            r.Type = ExtractString(json, "type") ?? "none";
            r.Pai = ExtractString(json, "pai");
            r.Tsumogiri = json.Contains("\"tsumogiri\":true");
            r.Actor = ExtractInt(json, "actor", -1);
            r.Target = ExtractInt(json, "target", -1);

            string consumedArr = ExtractArray(json, "consumed");
            if (consumedArr != null)
            {
                r.Consumed = new List<string>();
                int pos = 0;
                while (pos < consumedArr.Length)
                {
                    int q1 = consumedArr.IndexOf('"', pos);
                    if (q1 < 0) break;
                    int q2 = consumedArr.IndexOf('"', q1 + 1);
                    if (q2 < 0) break;
                    r.Consumed.Add(consumedArr.Substring(q1 + 1, q2 - q1 - 1));
                    pos = q2 + 1;
                }
            }
            return r;
        }

        private static string ExtractString(string json, string key)
        {
            string search = "\"" + key + "\":\"";
            int idx = json.IndexOf(search);
            if (idx < 0) return null;
            int start = idx + search.Length;
            int end = json.IndexOf('"', start);
            return end < 0 ? null : json.Substring(start, end - start);
        }

        private static string ExtractArray(string json, string key)
        {
            string search = "\"" + key + "\":[";
            int idx = json.IndexOf(search);
            if (idx < 0) return null;
            int start = idx + search.Length;
            int end = json.IndexOf(']', start);
            return end < 0 ? null : json.Substring(start, end - start);
        }

        private static int ExtractInt(string json, string key, int fallback)
        {
            string search = "\"" + key + "\":";
            int idx = json.IndexOf(search);
            if (idx < 0) return fallback;
            int start = idx + search.Length;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            int val;
            return int.TryParse(json.Substring(start, end - start), out val) ? val : fallback;
        }

        #endregion
    }

    public class MjaiResponse
    {
        public bool Success;
        public string Error;
        public string RawJson;

        public string Type;
        public string Pai;
        public bool Tsumogiri;
        public List<string> Consumed;
        public int Actor;
        public int Target;

        public MahjongTile? GetPaiTile()
        {
            if (string.IsNullOrEmpty(Pai)) return null;
            var t = MjaiClient.MjaiToTile(Pai);
            return t.IsValid() ? t : (MahjongTile?)null;
        }

        public string GetDisplayType()
        {
            switch (Type)
            {
                case "dahai": return "打牌";
                case "reach": return "立直";
                case "pon": return "碰";
                case "chi": return "吃";
                case "ankan": return "暗杠";
                case "kakan": return "加杠";
                case "daiminkan": return "大明杠";
                case "hora": return "和了";
                case "ryukyoku": return "流局";
                case "none": return "跳过";
                default: return Type ?? "未知";
            }
        }
    }
}
