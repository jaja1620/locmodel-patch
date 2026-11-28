// LocModel Downloader v3.1.2 - 在线搜索窗口的「下载到」记忆
// 只记本窗口自己的选择（mode + dir），不碰主界面模型目录，也不碰 LocModel 任何配置。
using System;
using System.IO;
using System.Text;

namespace LocModelDL
{
    public static class HubDestStore
    {
        // 0 = 下载到主界面目录（默认，等价 v3.1.1 行为）；1 = 下载到自定义目录
        public const int ModeMain = 0;
        public const int ModeCustom = 1;
        public const string FileName = "LocModel下载器_在线搜索下载目录.txt";

        // 自检用：指定配置文件路径，null 表示用 exe 同目录
        public static string OverridePath;

        public static string CfgPath
        {
            get
            {
                if (!string.IsNullOrEmpty(OverridePath)) return OverridePath;
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;
                return Path.Combine(dir, FileName);
            }
        }

        public static void Load(out int mode, out string dir)
        {
            mode = ModeMain;
            dir = "";
            try
            {
                string p = CfgPath;
                if (!File.Exists(p)) return;
                foreach (string line0 in File.ReadAllLines(p))
                {
                    string line = line0.TrimStart('\uFEFF').Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    if (k == "mode")
                    {
                        int n;
                        if (int.TryParse(v, out n)) mode = n == ModeCustom ? ModeCustom : ModeMain;
                    }
                    else if (k == "dir")
                    {
                        dir = Normalize(v);
                    }
                }
            }
            catch { }
            // 记忆里是「自定义」但目录已失效：保留模式，交给界面提示未就绪
        }

        public static void Save(int mode, string dir)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# LocModel 下载器 v3.1.2 在线搜索窗口的下载目录（改完保存即可，下次打开该窗口生效）");
                sb.AppendLine("# mode = 0 下载到主界面目录（默认）；1 下载到下面的自定义目录");
                sb.AppendLine("# dir  = 自定义目录的绝对路径；mode=0 时本行仅作记忆，不影响下载");
                sb.AppendLine("mode=" + (mode == ModeCustom ? ModeCustom : ModeMain));
                sb.AppendLine("dir=" + (dir == null ? "" : dir.Trim()));
                File.WriteAllText(CfgPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // 目录文本归一化：去引号、展开 ~、转绝对路径、去尾分隔符、滤掉文件名非法字符
        public static string Normalize(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            string s = dir.Trim().Trim('"').Trim();
            if (s.Length == 0) return "";
            if (s.StartsWith("~"))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home)) s = home + s.Substring(1);
            }
            char[] bad = Path.GetInvalidPathChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (Array.IndexOf(bad, c) >= 0) continue;
                if (c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|') continue;
                sb.Append(c);
            }
            s = sb.ToString().Trim();
            if (s.Length == 0) return "";
            try { s = Path.GetFullPath(s); }
            catch { return ""; }
            string root = Path.GetPathRoot(s);
            if (!string.IsNullOrEmpty(root) && s.Length > root.Length) s = s.TrimEnd('\\', '/');
            return s;
        }

        // 自定义目录是否可用（模式为自定义时 dir 不能为空）
        public static bool IsReady(int mode, string dir)
        {
            if (mode != ModeCustom) return true;
            return !string.IsNullOrEmpty(Normalize(dir));
        }

        // 生效落盘基目录：destDir 为空 → 主界面模型目录
        public static string EffDir(string modelsDir, string destDir)
        {
            string d = Normalize(destDir);
            if (d.Length > 0) return d;
            return Normalize(modelsDir);
        }

        // 落盘子目录：{基目录}\{owner}__{repo}（规则与 LocModel 语言模型一致）
        public static string ModelDirOf(string baseDir, string repo)
        {
            if (string.IsNullOrEmpty(baseDir)) return "";
            string seg = repo == null ? "" : repo;
            int sl = seg.IndexOf('/');
            if (sl > 0) seg = seg.Substring(0, sl) + "__" + seg.Substring(sl + 1);
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in seg) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string safe = sb.ToString().Trim().TrimEnd('.');
            if (safe.Length == 0) safe = "model";
            return Path.Combine(baseDir, safe);
        }
    }
}
