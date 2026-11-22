// LocModel Downloader v3.0.0 - 并发分片下载配置
// 放在 exe 同目录的纯文本里，改完立即生效；绝不读写 LocModel 自己的任何配置。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LocModelDL
{
    public static class DownloadConfig
    {
        public const int DefaultConnections = 4;
        public const int DefaultMinSegMB = 8;
        public const int MaxConnections = 16;

        // 每条连接负责一个 Range 分片；1 = 关闭分片，退回 v2.4.x 的单连接行为
        public static int Connections = DefaultConnections;

        // 小于该大小的文件不拆分（避免为一堆小文件起多连接）
        public static int MinSegMB = DefaultMinSegMB;

        public static string CfgPath
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;
                return Path.Combine(dir, "LocModel下载器_下载配置.txt");
            }
        }

        public static void Load()
        {
            int c = DefaultConnections, m = DefaultMinSegMB;
            try
            {
                string p = CfgPath;
                if (File.Exists(p))
                {
                    foreach (string line0 in File.ReadAllLines(p))
                    {
                        string line = line0.TrimStart('\uFEFF').Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim().ToLowerInvariant();
                        string v = line.Substring(i + 1).Trim();
                        int n;
                        if (!int.TryParse(v, out n)) continue;
                        if (k == "connections") c = n;
                        else if (k == "minseg_mb") m = n;
                    }
                }
            }
            catch { }
            Connections = Clamp(c);
            MinSegMB = m < 1 ? DefaultMinSegMB : (m > 4096 ? 4096 : m);
        }

        public static void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# LocModel 下载器 v3.0.0 下载配置（改完保存即可，重启下载器生效）");
                sb.AppendLine("# connections = 并发连接数（Range 分片下载）；1 = 单连接");
                sb.AppendLine("# minseg_mb  = 单个分片的最小字节数（MB）；文件小于 2×该值时不拆分");
                sb.AppendLine("connections=" + Connections);
                sb.AppendLine("minseg_mb=" + MinSegMB);
                File.WriteAllText(CfgPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static void SetConnections(int n)
        {
            Connections = Clamp(n);
            Save();
        }

        static int Clamp(int n)
        {
            if (n < 1) return 1;
            if (n > MaxConnections) return MaxConnections;
            return n;
        }

        // 分片阈值（字节）
        public static long MinSegBytes { get { return (long)MinSegMB * 1024L * 1024L; } }

        public static bool SegmentEnabled { get { return Connections > 1; } }
    }
}
