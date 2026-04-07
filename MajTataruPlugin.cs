using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Advanced_Combat_Tracker;

[assembly: AssemblyTitle("MajTataru")]
[assembly: AssemblyDescription("塔塔露麻将")]
[assembly: AssemblyVersion("0.1.0.0")]

namespace MajTataru
{
    public class MajTataruPlugin : UserControl, IActPluginV1
    {
        #region UI Controls

        private CheckBox chkEnabled;
        private CheckBox chkDebug;
        private Label lblGameStatus;
        private RichTextBox txtOutput;
        private Button btnClear;
        private Button btnOpenLogFolder;
        private Button btnTestOverlay;
        private Label lblLogPath;
        private GroupBox grpSettings;
        private GroupBox grpOutput;

        #endregion

        private Label _statusLabel;
        private string _settingsFile;
        private SettingsSerializer _xmlSettings;

        private MahjongParser _parser;
        private MahjongAI _ai;
        private DebugLogger _debugLogger;
        private string _pluginDirectory;

        private int _outputLineCount;
        private const int MAX_OUTPUT_LINES = 2000;

        // FFXIV 插件反射挂载相关
        private object _ffxivDataSubscription;
        private Delegate _networkReceivedDelegate;
        private Delegate _networkSentDelegate;
        private EventInfo _networkReceivedEvent;
        private EventInfo _networkSentEvent;
        private bool _ffxivHooked;

        public MajTataruPlugin()
        {
            InitializeUI();
        }

        #region IActPluginV1

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            _statusLabel = pluginStatusText;
            _pluginDirectory = GetPluginDirectory();
            _settingsFile = Path.Combine(
                ActGlobals.oFormActMain.AppDataFolder.FullName,
                "Config\\MajTataru.config.xml");

            pluginScreenSpace.Controls.Add(this);
            pluginScreenSpace.Text = "MajTataru";
            Dock = DockStyle.Fill;

            _parser = new MahjongParser();
            _parser.OnMessage += OnParserMessage;
            _ai = new MahjongAI();
            _debugLogger = new DebugLogger();

            _xmlSettings = new SettingsSerializer(this);
            LoadSettings();

            // BeforeLogLineRead 用于捕获 zone change (type 01) 和 party list (type 11)
            ActGlobals.oFormActMain.BeforeLogLineRead += OnBeforeLogLineRead;

            if (chkEnabled.Checked)
                OnEnabled();

            _statusLabel.Text = "MajTataru 已加载";
            AppendOutput("[系统] MajTataru 塔塔露麻将已启动", Color.Gray);
            AppendOutput($"[系统] 插件目录: {_pluginDirectory}", Color.Gray);
        }

        public void DeInitPlugin()
        {
            ActGlobals.oFormActMain.BeforeLogLineRead -= OnBeforeLogLineRead;
            UnhookFFXIVPlugin();

            if (_debugLogger != null)
            {
                _debugLogger.Dispose();
                _debugLogger = null;
            }

            SaveSettings();
            _statusLabel.Text = "MajTataru 已卸载";
        }

        #endregion

        #region FFXIV Plugin 反射挂载

        private bool TryHookFFXIVPlugin()
        {
            if (_ffxivHooked) return true;

            try
            {
                object ffxivPlugin = null;

                foreach (var plugin in ActGlobals.oFormActMain.ActPlugins)
                {
                    if (plugin.pluginFile == null || plugin.pluginObj == null)
                        continue;

                    if (plugin.pluginFile.Name.Equals("FFXIV_ACT_Plugin.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        ffxivPlugin = plugin.pluginObj;
                        break;
                    }
                }

                if (ffxivPlugin == null)
                {
                    AppendOutput("[警告] 未找到 FFXIV_ACT_Plugin，无法获取网络数据", Color.Red);
                    AppendOutput("[提示] 请确保 FFXIV 解析插件已加载", Color.Yellow);
                    return false;
                }

                // 获取 DataSubscription 属性
                PropertyInfo dsProp = ffxivPlugin.GetType().GetProperty("DataSubscription",
                    BindingFlags.Public | BindingFlags.Instance);
                if (dsProp == null)
                {
                    AppendOutput("[警告] FFXIV 插件版本不兼容 (无 DataSubscription)", Color.Red);
                    return false;
                }

                _ffxivDataSubscription = dsProp.GetValue(ffxivPlugin, null);
                if (_ffxivDataSubscription == null)
                {
                    AppendOutput("[警告] DataSubscription 尚未就绪", Color.Orange);
                    return false;
                }

                Type dsType = _ffxivDataSubscription.GetType();

                // 挂载 NetworkReceived
                _networkReceivedEvent = dsType.GetEvent("NetworkReceived");
                if (_networkReceivedEvent != null)
                {
                    MethodInfo handler = GetType().GetMethod(nameof(OnNetworkReceived),
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _networkReceivedDelegate = Delegate.CreateDelegate(
                        _networkReceivedEvent.EventHandlerType, this, handler);
                    _networkReceivedEvent.AddEventHandler(_ffxivDataSubscription, _networkReceivedDelegate);
                }

                // 挂载 NetworkSent
                _networkSentEvent = dsType.GetEvent("NetworkSent");
                if (_networkSentEvent != null)
                {
                    MethodInfo handler = GetType().GetMethod(nameof(OnNetworkSent),
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _networkSentDelegate = Delegate.CreateDelegate(
                        _networkSentEvent.EventHandlerType, this, handler);
                    _networkSentEvent.AddEventHandler(_ffxivDataSubscription, _networkSentDelegate);
                }

                _ffxivHooked = true;
                AppendOutput("[系统] 已挂载 FFXIV 插件 NetworkReceived/NetworkSent 事件", Color.LimeGreen);
                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 挂载 FFXIV 插件失败: {ex.Message}", Color.Red);
                if (chkDebug.Checked && _debugLogger != null)
                    _debugLogger.WriteLine("ERROR", $"Hook FFXIV plugin failed: {ex}");
                return false;
            }
        }

        private void UnhookFFXIVPlugin()
        {
            try
            {
                if (_ffxivDataSubscription != null)
                {
                    if (_networkReceivedEvent != null && _networkReceivedDelegate != null)
                        _networkReceivedEvent.RemoveEventHandler(_ffxivDataSubscription, _networkReceivedDelegate);
                    if (_networkSentEvent != null && _networkSentDelegate != null)
                        _networkSentEvent.RemoveEventHandler(_ffxivDataSubscription, _networkSentDelegate);
                }
            }
            catch { }

            _ffxivDataSubscription = null;
            _networkReceivedDelegate = null;
            _networkSentDelegate = null;
            _networkReceivedEvent = null;
            _networkSentEvent = null;
            _ffxivHooked = false;
        }

        /// <summary>
        /// 签名必须匹配 NetworkReceivedDelegate(string connection, long epoch, byte[] message)
        /// </summary>
        private void OnNetworkReceived(string connection, long epoch, byte[] message)
        {
            if (!chkEnabled.Checked) return;
            ProcessNetworkData(message, "RECV");
        }

        /// <summary>
        /// 签名必须匹配 NetworkSentDelegate(string connection, long epoch, byte[] message)
        /// </summary>
        private void OnNetworkSent(string connection, long epoch, byte[] message)
        {
            if (!chkEnabled.Checked) return;
            ProcessNetworkData(message, "SENT");
        }

        private void ProcessNetworkData(byte[] message, string direction)
        {
            try
            {
                ParseResult result = _parser.ParseBinaryPacket(message);

                bool isDebug = chkDebug.Checked;
                bool inGame = _parser.State.InGame;

                if (isDebug && inGame && _debugLogger != null)
                {
                    string hex = MahjongParser.FormatBinaryAsHex(message);
                    _debugLogger.WriteRaw($"{direction}|{hex}");

                    if (result != null && result.IsRelevant)
                        _debugLogger.WriteParsed(result.Tag, result.Summary);
                }

                string aiAnalysis = null;
                if (_ai != null)
                {
                    try
                    {
                        aiAnalysis = _ai.ProcessPacket(message);
                    }
                    catch (Exception aiEx)
                    {
                        if (isDebug && _debugLogger != null)
                            _debugLogger.WriteLine("AI_ERROR", aiEx.ToString());
                    }
                }

                if (aiAnalysis != null && isDebug && _debugLogger != null)
                    _debugLogger.WriteParsed("AI_ANALYSIS", aiAnalysis);

                if (aiAnalysis != null)
                    EmitOverlayData(_ai.LastOverlayJson);

                if (result == null || !result.IsRelevant)
                {
                    if (aiAnalysis != null)
                        ShowAIAnalysis(aiAnalysis);
                    return;
                }

                Color color = GetTagColor(result.Tag);
                string display = $"[{result.Tag}] {result.Summary}";

                if (InvokeRequired)
                    BeginInvoke(new Action(() =>
                    {
                        AppendOutput(display, color);
                        if (aiAnalysis != null)
                            ShowAIOutput(aiAnalysis);
                        UpdateGameStatus();
                    }));
                else
                {
                    AppendOutput(display, color);
                    if (aiAnalysis != null)
                        ShowAIOutput(aiAnalysis);
                    UpdateGameStatus();
                }
            }
            catch (Exception ex)
            {
                if (chkDebug.Checked && _debugLogger != null)
                    _debugLogger.WriteLine("ERROR", ex.ToString());
            }
        }

        private void ShowAIAnalysis(string analysis)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ShowAIOutput(analysis)));
            else
                ShowAIOutput(analysis);
        }

        private void ShowAIOutput(string analysis)
        {
            foreach (var line in analysis.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                if (string.IsNullOrEmpty(trimmed)) continue;
                Color c = Color.FromArgb(100, 200, 255);
                if (trimmed.Contains("和了!")) c = Color.FromArgb(255, 80, 80);
                else if (trimmed.Contains("★推荐")) c = Color.FromArgb(50, 255, 150);
                else if (trimmed.Contains("不推荐")) c = Color.FromArgb(180, 180, 180);
                else if (trimmed.Contains("鸣牌分析")) c = Color.FromArgb(255, 200, 50);
                else if (trimmed.Contains("#1 ")) c = Color.FromArgb(50, 255, 150);
                else if (trimmed.Contains("弃和")) c = Color.FromArgb(255, 150, 100);
                else if (trimmed.Contains("立直")) c = Color.FromArgb(255, 255, 100);
                AppendOutput($"  [AI] {trimmed}", c);
            }
        }

        #endregion

        #region Overlay Data Emission

        /// <summary>
        /// 将 AI 分析 JSON 作为自定义 ACT 日志行写入，OverlayPlugin 会将其转发给悬浮窗。
        /// 格式: 00|timestamp|0048|MajTataru|{json}|
        /// </summary>
        private void EmitOverlayData(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                string escaped = json.Replace("|", "\u2502");
                string line = $"00|{DateTime.Now:O}|0048|MajTataru|{escaped}|";
                ActGlobals.oFormActMain.ParseRawLogLine(false, DateTime.Now, line);
            }
            catch { }
        }

        private void BtnTestOverlay_Click(object sender, EventArgs e)
        {
            string json =
                "{\"type\":\"discard\"" +
                ",\"strategy\":\"一般型\"" +
                ",\"shanten\":1" +
                ",\"tilesLeft\":52" +
                ",\"wind\":\"南\"" +
                ",\"bestTile\":\"3s\"" +
                ",\"bestTileName\":\"三索\"" +
                ",\"riichi\":false" +
                ",\"isFold\":false" +
                ",\"tts\":\"切三索\"" +
                ",\"recommendations\":[" +
                  "{\"rank\":1,\"tile\":\"3s\",\"tileName\":\"三索\",\"priority\":285.3,\"efficiency\":6.72,\"danger\":8.5,\"shanten\":1,\"safe\":true}," +
                  "{\"rank\":2,\"tile\":\"9m\",\"tileName\":\"九万\",\"priority\":210.1,\"efficiency\":4.30,\"danger\":15.2,\"shanten\":1,\"safe\":true}," +
                  "{\"rank\":3,\"tile\":\"1p\",\"tileName\":\"一筒\",\"priority\":185.7,\"efficiency\":3.85,\"danger\":22.0,\"shanten\":1,\"safe\":false}," +
                  "{\"rank\":4,\"tile\":\"東\",\"tileName\":\"东\",\"priority\":120.4,\"efficiency\":2.10,\"danger\":45.6,\"shanten\":2,\"safe\":false}" +
                "]}";

            EmitOverlayData(json);
            AppendOutput("[测试] 已发送测试消息到悬浮窗", Color.Cyan);
        }

        #endregion

        #region ACT Log Line Event (区域/队伍检测 + type 252 回退)

        private void OnBeforeLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (!chkEnabled.Checked) return;
            if (isImport) return;

            string line = logInfo.logLine;
            if (string.IsNullOrEmpty(line)) return;

            char first = line[0];
            if (first != '0' && first != '1' && first != '2') return;

            try
            {
                bool isDebug = chkDebug.Checked;
                bool inGame = _parser.State.InGame;

                // 对局期间 debug 模式下，所有 type 252 包都记录到日志
                if (isDebug && inGame && !_ffxivHooked && _debugLogger != null
                    && line.StartsWith("252|"))
                {
                    _debugLogger.WriteRaw(line.TrimEnd());
                }

                ParseResult result = _parser.ParseLogLine(line);

                // 已解析的结果追加到 debug 日志
                if (isDebug && _debugLogger != null && result != null && result.IsRelevant)
                {
                    // type 252 的原始行已在上面记录，这里只补解析结果
                    if (!line.StartsWith("252|") || !inGame)
                        _debugLogger.WriteRaw(line.TrimEnd());
                    _debugLogger.WriteParsed(result.Tag, result.Summary);
                }

                if (result == null || !result.IsRelevant) return;

                // type 252 结果仅在未挂载 FFXIV 插件时显示（避免重复）
                if (result.Tag != "ZONE" && result.Tag != "PARTY" && _ffxivHooked)
                    return;

                Color color = GetTagColor(result.Tag);
                string display = $"[{result.Tag}] {result.Summary}";

                if (InvokeRequired)
                    BeginInvoke(new Action(() =>
                    {
                        AppendOutput(display, color);
                        UpdateGameStatus();
                    }));
                else
                {
                    AppendOutput(display, color);
                    UpdateGameStatus();
                }
            }
            catch (Exception ex)
            {
                if (chkDebug.Checked && _debugLogger != null)
                    _debugLogger.WriteLine("ERROR", ex.ToString());
            }
        }

        #endregion

        #region Enable/Debug Toggle

        private void OnEnabled()
        {
            if (chkDebug.Checked)
                StartDebugLog();

            bool hooked = TryHookFFXIVPlugin();
            if (!hooked)
            {
                AppendOutput("[回退] 将使用 BeforeLogLineRead（需 FFXIV 插件开启 debug 网络日志）", Color.Yellow);
            }

            _statusLabel.Text = "MajTataru 已启用 - " + (hooked ? "NetworkReceived" : "LogLine回退");
            AppendOutput("[系统] 解析已启用，等待进入麻将区域...", Color.Green);
        }

        private void OnDisabled()
        {
            UnhookFFXIVPlugin();
            StopDebugLog();
            _parser.State.Reset();
            _parser.State.InMahjongZone = false;
            _statusLabel.Text = "MajTataru 已暂停";
            AppendOutput("[系统] 解析已停止", Color.Orange);
            UpdateGameStatus();
        }

        private void StartDebugLog()
        {
            string logDir = Path.Combine(_pluginDirectory, "logs");
            _debugLogger.Open(logDir);
            lblLogPath.Text = $"日志: {_debugLogger.LogFilePath}";
            AppendOutput($"[Debug] 日志已开启: {_debugLogger.LogFilePath}", Color.Cyan);
        }

        private void StopDebugLog()
        {
            if (_debugLogger != null)
            {
                _debugLogger.Close();
                lblLogPath.Text = "日志: (未启用)";
            }
        }

        private void ChkEnabled_CheckedChanged(object sender, EventArgs e)
        {
            if (chkEnabled.Checked)
                OnEnabled();
            else
                OnDisabled();
        }

        private void ChkDebug_CheckedChanged(object sender, EventArgs e)
        {
            if (chkEnabled.Checked && chkDebug.Checked)
                StartDebugLog();
            else
                StopDebugLog();
        }

        #endregion

        #region Parser Message Handler

        private void OnParserMessage(string msg)
        {
            Color color = Color.White;
            if (msg.StartsWith("===")) color = Color.Gold;
            else if (msg.StartsWith("---")) color = Color.LightSkyBlue;
            else if (msg.Contains("手牌:")) color = Color.LightGreen;

            if (InvokeRequired)
                BeginInvoke(new Action(() => AppendOutput(msg, color)));
            else
                AppendOutput(msg, color);
        }

        #endregion

        #region UI Helpers

        private void AppendOutput(string text, Color color)
        {
            if (txtOutput.IsDisposed) return;

            _outputLineCount++;
            if (_outputLineCount > MAX_OUTPUT_LINES)
            {
                txtOutput.Clear();
                _outputLineCount = 1;
                txtOutput.SelectionColor = Color.Gray;
                txtOutput.AppendText("[系统] 输出已清空（超过行数限制）\n");
            }

            txtOutput.SelectionStart = txtOutput.TextLength;
            txtOutput.SelectionLength = 0;
            txtOutput.SelectionColor = color;
            txtOutput.AppendText(text + "\n");
            txtOutput.ScrollToCaret();
        }

        private void UpdateGameStatus()
        {
            string status;
            var s = _parser.State;

            if (!s.InMahjongZone)
                status = "状态: 未在麻将区域";
            else if (!s.InGame)
                status = "状态: 在麻将区域 - 等待开始";
            else
            {
                string round = TileDecoder.GetRoundName(s.RoundIndex, s.Honba);
                string dealerTag = s.GetPlayerTag(0);
                string myWind = s.MySeat >= 0 ? s.GetWindName((uint)s.MySeat) : "?";
                status = $"状态: {round} | 自风={myWind} | 庄={dealerTag} | 巡目#{s.TurnCount}";
            }

            if (InvokeRequired)
                BeginInvoke(new Action(() => lblGameStatus.Text = status));
            else
                lblGameStatus.Text = status;
        }

        private Color GetTagColor(string tag)
        {
            switch (tag)
            {
                case "ZONE": return Color.Cyan;
                case "PARTY": return Color.LightBlue;
                case "GAME_INIT": return Color.Gold;
                case "GAME_END": return Color.Gold;
                case "ROUND_START": return Color.LightGreen;
                case "ROUND_END": return Color.Salmon;
                case "TSUMO": return Color.Lime;
                case "RON": return Color.Red;
                case "RIICHI": return Color.Red;
                case "DISCARD": return Color.White;
                case "DRAW": return Color.LightGray;
                case "CHI": return Color.Orange;
                case "PON": return Color.Orange;
                case "ANKAN": return Color.Magenta;
                case "KAKAN": return Color.Magenta;
                case "DAIMINKAN": return Color.Magenta;
                case "RINSHAN": return Color.Violet;
                case "SETTLE": return Color.Yellow;
                case "AI_ANALYSIS": return Color.FromArgb(100, 200, 255);
                default: return Color.Silver;
            }
        }

        #endregion

        #region Settings

        private void LoadSettings()
        {
            _xmlSettings.AddControlSetting(chkEnabled.Name, chkEnabled);
            _xmlSettings.AddControlSetting(chkDebug.Name, chkDebug);

            if (!File.Exists(_settingsFile)) return;

            FileStream fs = new FileStream(_settingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            XmlTextReader xReader = new XmlTextReader(fs);
            try
            {
                while (xReader.Read())
                {
                    if (xReader.NodeType == XmlNodeType.Element && xReader.LocalName == "SettingsSerializer")
                        _xmlSettings.ImportFromXml(xReader);
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "设置加载失败: " + ex.Message;
            }
            xReader.Close();
        }

        private void SaveSettings()
        {
            FileStream fs = new FileStream(_settingsFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            XmlTextWriter xWriter = new XmlTextWriter(fs, Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                Indentation = 1,
                IndentChar = '\t'
            };
            xWriter.WriteStartDocument(true);
            xWriter.WriteStartElement("Config");
            xWriter.WriteStartElement("SettingsSerializer");
            _xmlSettings.ExportToXml(xWriter);
            xWriter.WriteEndElement();
            xWriter.WriteEndElement();
            xWriter.WriteEndDocument();
            xWriter.Flush();
            xWriter.Close();
        }

        #endregion

        #region Plugin Directory

        private string GetPluginDirectory()
        {
            string asmPath = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(asmPath))
                return Path.GetDirectoryName(asmPath);

            foreach (var p in ActGlobals.oFormActMain.ActPlugins)
            {
                if (p.pluginObj == this)
                    return Path.GetDirectoryName(p.pluginFile.FullName);
            }

            return ActGlobals.oFormActMain.AppDataFolder.FullName;
        }

        #endregion

        #region UI Layout

        private void InitializeUI()
        {
            SuspendLayout();

            grpSettings = new GroupBox
            {
                Text = "设置",
                Dock = DockStyle.Top,
                Height = 90,
                Padding = new Padding(8)
            };

            chkEnabled = new CheckBox
            {
                Text = "启用解析",
                Name = "chkEnabled",
                Location = new Point(15, 22),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5f)
            };
            chkEnabled.CheckedChanged += ChkEnabled_CheckedChanged;

            chkDebug = new CheckBox
            {
                Text = "Debug 日志",
                Name = "chkDebug",
                Location = new Point(130, 22),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5f)
            };
            chkDebug.CheckedChanged += ChkDebug_CheckedChanged;

            lblLogPath = new Label
            {
                Text = "日志: (未启用)",
                Location = new Point(15, 48),
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };

            lblGameStatus = new Label
            {
                Text = "状态: 未启用",
                Location = new Point(15, 68),
                AutoSize = true,
                ForeColor = Color.DarkCyan,
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
            };

            grpSettings.Controls.AddRange(new Control[] { chkEnabled, chkDebug, lblLogPath, lblGameStatus });

            var toolPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32
            };

            btnClear = new Button
            {
                Text = "清空输出",
                Location = new Point(8, 4),
                Size = new Size(80, 24),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            btnClear.Click += (s, e) => { txtOutput.Clear(); _outputLineCount = 0; };

            btnOpenLogFolder = new Button
            {
                Text = "日志目录",
                Location = new Point(96, 4),
                Size = new Size(100, 24),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            btnOpenLogFolder.Click += (s, e) =>
            {
                string dir = Path.Combine(_pluginDirectory ?? ".", "logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", dir);
            };

            btnTestOverlay = new Button
            {
                Text = "测试悬浮窗",
                Location = new Point(204, 4),
                Size = new Size(100, 24),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            btnTestOverlay.Click += BtnTestOverlay_Click;

            toolPanel.Controls.AddRange(new Control[] { btnClear, btnOpenLogFolder, btnTestOverlay });

            grpOutput = new GroupBox
            {
                Text = "解析输出",
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };

            txtOutput = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f),
                WordWrap = true,
                BorderStyle = BorderStyle.None
            };

            grpOutput.Controls.Add(txtOutput);

            Controls.Add(grpOutput);
            Controls.Add(toolPanel);
            Controls.Add(grpSettings);

            Size = new Size(750, 500);
            ResumeLayout(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _debugLogger?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
