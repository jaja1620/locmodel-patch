// LocModel Downloader v2.4.2 - 就地半成品识别（只读，不参与下载）
// 背景：LocModel 3.5 主程序改成就地下载 <file>.part + <file>.part.meta（meta 内容 {"size":N,"mismatch":0}），
//       与下载器自己的 _locmodel_dl_tmp 断点目录互不相干。
// 约束（用户确认）：仅识别并提示，不移动、不删除、不接管续传；下载器照常用自己的断点机制下载新文件。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LocModelDL
{
static class HalfPartScan
{
    static readonly Regex MetaSizeRx = new Regex("\"size\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

    // 单个目标的就地半成品检查：返回 0 或 1 行提示
    static string CheckOne(string destPath)
    {
        try
        {
            string part = destPath + ".part";
            if (!File.Exists(part)) return null;
            long len = 0;
            try { len = new FileInfo(part).Length; } catch { }
            if (len <= 0) return null;

            string extra = "";
            string meta = part + ".meta";
            if (File.Exists(meta))
            {
                try
                {
                    string t = File.ReadAllText(meta, Encoding.UTF8);
                    Match m = MetaSizeRx.Match(t);
                    long declared;
                    if (m.Success && long.TryParse(m.Groups[1].Value, out declared) && declared > 0)
                        extra = "（meta 记录 " + MB(declared) + "）";
                }
                catch { }
            }
            return "  · 主程序未完成下载 " + MB(len) + extra + "：" + Path.GetFileName(destPath);
        }
        catch { return null; }
    }

    static string MB(long b)
    {
        double v = b / 1048576.0;
        return v >= 1024 ? (v / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " GB"
                         : v.ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }

    // 对照本次下载清单，找出目标路径旁边残留的 .part
    public static List<string> Scan(string modelsDir, List<DownloadItem> list)
    {
        List<string> rows = new List<string>();
        if (list == null) return rows;
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DownloadItem it in list)
        {
            if (it == null || string.IsNullOrEmpty(it.DestPath)) continue;
            if (!seen.Add(it.DestPath)) continue;
            string r = CheckOne(it.DestPath);
            if (r != null) rows.Add(r);
        }
        if (rows.Count > 0)
        {
            rows.Insert(0, "检测到 LocModel 主程序留下的半成品 " + rows.Count + " 个（下载器使用独立断点目录，不受影响，也不会接管这些 .part）：");
        }
        return rows;
    }

    // 目录树扫描（环境面板用）：最多 3 层、200 条
    public static List<string> ScanTree(string root, int maxDepth, int maxRows)
    {
        List<string> rows = new List<string>();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return rows;
        try
        {
            Walk(root, 0, maxDepth, maxRows, rows);
        }
        catch { }
        return rows;
    }

    static void Walk(string dir, int depth, int maxDepth, int maxRows, List<string> rows)
    {
        if (rows.Count >= maxRows) return;
        if (depth > maxDepth) return;
        try
        {
            foreach (string f in Directory.GetFiles(dir, "*.part"))
            {
                if (rows.Count >= maxRows) return;
                long len = 0;
                try { len = new FileInfo(f).Length; } catch { }
                rows.Add("  · " + f.Substring(Math.Min(f.Length, dir.Length + 1)) + "  " + MB(len));
            }
            foreach (string d in Directory.GetDirectories(dir))
            {
                if (rows.Count >= maxRows) return;
                string nm = Path.GetFileName(d);
                if (nm.StartsWith("_locmodel_dl_tmp", StringComparison.OrdinalIgnoreCase)) continue;
                Walk(d, depth + 1, maxDepth, maxRows, rows);
            }
        }
        catch { }
    }
}
}

