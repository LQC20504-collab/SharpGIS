using System;
using System.Collections.Generic;
using System.IO;

namespace SharpGIS.Services
{
    /// <summary>最近打开文件列表（%APPDATA%\SharpGIS\recent.txt）。</summary>
    public static class RecentFiles
    {
        private const int MaxItems = 10;

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SharpGIS");
                return Path.Combine(dir, "recent.txt");
            }
        }

        /// <summary>读取最近打开列表（最多 10 条）。</summary>
        /// <returns>按时间倒序的路径列表。</returns>
        public static List<string> Load()
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var s = line.Trim();
                    if (s.Length == 0 || list.Contains(s)) continue;
                    list.Add(s);
                    if (list.Count >= MaxItems) break;
                }
            }
            catch { }
            return list;
        }

        /// <summary>把路径加入最近打开列表首位。</summary>
        /// <param name="path">影像文件路径。</param>
        public static void Add(string path)
        {
            try
            {
                var list = Load();
                list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, path);
                while (list.Count > MaxItems) list.RemoveAt(list.Count - 1);

                string dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(FilePath, list);
            }
            catch { }
        }

        /// <summary>清空最近打开列表。</summary>
        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }
    }
}
