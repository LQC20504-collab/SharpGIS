using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpGIS.SelfTest
{
    /// <summary>自检日志：同时写控制台与 selftest.log，并统计 FAIL。</summary>
    internal sealed class TestLog
    {
        private readonly StringBuilder _sb = new StringBuilder();
        public readonly string LogPath;
        public int Failures;
        public int Passes;

        public TestLog(string dir)
        {
            Directory.CreateDirectory(dir);
            LogPath = Path.Combine(dir, "selftest.log");
        }

        public void Info(string msg)
        {
            string line = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, msg);
            _sb.AppendLine(line);
            ConsoleBridge.Write(msg);
        }

        public void Pass(string name, string detail = "")
        {
            Passes++;
            Info("PASS | " + name + (string.IsNullOrEmpty(detail) ? "" : " | " + detail));
        }

        public void Fail(string name, string detail = "")
        {
            Failures++;
            Info("FAIL | " + name + (string.IsNullOrEmpty(detail) ? "" : " | " + detail));
        }

        public void Check(bool condition, string name, string detail = "")
        {
            if (condition) Pass(name, detail);
            else Fail(name, detail);
        }

        public void Save()
        {
            try
            {
                _sb.AppendLine();
                _sb.AppendLine(string.Format("汇总: PASS={0} FAIL={1}", Passes, Failures));
                File.WriteAllText(LogPath, _sb.ToString(), Encoding.UTF8);
                ConsoleBridge.Write("日志已写入: " + LogPath);
            }
            catch { }
        }
    }
}
