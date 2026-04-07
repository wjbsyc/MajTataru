using System;
using System.IO;
using System.Text;

namespace MajTataru
{
    public class DebugLogger : IDisposable
    {
        private StreamWriter _writer;
        private readonly object _lock = new object();
        private bool _disposed;

        public string LogFilePath { get; private set; }

        /// <summary>
        /// 在指定目录下创建新的日志文件，文件名含启动时间戳
        /// </summary>
        public void Open(string logDirectory)
        {
            Close();

            if (!Directory.Exists(logDirectory))
                Directory.CreateDirectory(logDirectory);

            string fileName = $"MajTataru_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            LogFilePath = Path.Combine(logDirectory, fileName);

            _writer = new StreamWriter(LogFilePath, false, Encoding.UTF8) { AutoFlush = true };
            WriteHeader();
        }

        public void Close()
        {
            lock (_lock)
            {
                if (_writer != null)
                {
                    try { _writer.Flush(); _writer.Close(); } catch { }
                    _writer = null;
                }
            }
        }

        private void WriteHeader()
        {
            WriteLine("INFO", $"MajTataru Debug Log - Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteLine("INFO", new string('-', 80));
        }

        public void WriteLine(string tag, string message)
        {
            lock (_lock)
            {
                if (_writer == null) return;
                try
                {
                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff}|{tag}|{message}");
                }
                catch { }
            }
        }

        /// <summary>
        /// 写入原始包数据行
        /// </summary>
        public void WriteRaw(string rawLine)
        {
            WriteLine("RAW", rawLine);
        }

        /// <summary>
        /// 写入解析后的结果
        /// </summary>
        public void WriteParsed(string tag, string parsed)
        {
            WriteLine(tag, parsed);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Close();
                _disposed = true;
            }
        }
    }
}
