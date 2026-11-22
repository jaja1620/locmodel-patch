// LocModel Downloader v2.4.2 - LocModel 版本适配层（3.5 / 3.3 / 3.1）
// 本文件只承载「版本差异」：settings 字段解释、catalog 规格字段能力、图像/视频清单的权威来源、
// 半成品残留的识别方式、落盘目录规则。下载 / 校验 / 续传内核（Downloader.cs、SiteProbe.cs）
// 在 v2.4.2 中字节不变，不感知版本；任何版本差异都在这一层收敛。
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LocModelDL
{
    public class LocModelProfile
    {
        public string Version = DefaultVersion;

        // 3.3 及以后共用：image\loras 目录、comfy_dir 识别（3.1 没有）
        public bool IsV33 = true;
        // 3.5 专有：catalog 带规格字段（params_b/active_b/...）、settings 带 hf_token/accel_nodes、就地 .part 半成品
        public bool IsV35 = false;

        public string TempSubDir = "_locmodel_dl_tmp";
        public bool HasLoraDir = false;
        public bool RecognizeComfyDir = false;

        // settings.json 字段能力
        public bool HasHfToken = false;        // 3.5 起有 hf_token
        public bool HasAccelNodes = false;     // 3.5 起有 accel_nodes / preferred_line
        public bool HasGithubMirror = false;   // 3.5 起有 github_mirror

        // catalog.json 字段能力
        public bool CatalogHasSpec = false;    // 3.5 起有 params_b/active_b/layers/kv_*/head_dim/experts*/ctx_max

        // 图像/视频清单权威来源：0=内置快照为准（exe 仅核对）  1=exe 解析优先 + 快照兜底  2=快照 + exe 仓库修复
        public int ImageAuthority = 0;

        // 是否扫描 <file>.part（就地半成品）并提示：3.5 主程序改为就地断点续传，2.4.x 内核不认识这种残留
        public bool ScanInlinePart = false;

        public string Note = "";

        // 界面下拉顺序：新版在前，默认 3.5
        public static readonly string[] Versions = new string[] { "3.5", "3.3", "3.1" };
        public const string DefaultVersion = "3.5";

        public static bool IsKnown(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            for (int i = 0; i < Versions.Length; i++)
                if (string.Equals(Versions[i], v.Trim(), StringComparison.Ordinal)) return true;
            return false;
        }

        // 版本号归一化：支持 "3.5" / "3.5.0" / "v3.5" / "3.5.0-beta" 等写法；无法识别时返回默认版本
        public static string Normalize(string v)
        {
            if (string.IsNullOrEmpty(v)) return DefaultVersion;
            string t = v.Trim();
            if (t.Length == 0) return DefaultVersion;
            if (t.StartsWith("LocModel", StringComparison.OrdinalIgnoreCase)) t = t.Substring(8).Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1).Trim();
            if (t.StartsWith("3.5")) return "3.5";
            if (t.StartsWith("3.3")) return "3.3";
            if (t.StartsWith("3.1")) return "3.1";
            return DefaultVersion;
        }

        public static LocModelProfile Get(string version)
        {
            string v = Normalize(version);
            LocModelProfile p = new LocModelProfile();
            p.Version = v;

            if (v == "3.1")
            {
                p.IsV33 = false; p.IsV35 = false;
                p.TempSubDir = "_locmodel_dl_tmp";
                p.HasLoraDir = false; p.RecognizeComfyDir = false;
                p.HasHfToken = false; p.HasAccelNodes = false; p.HasGithubMirror = false;
                p.CatalogHasSpec = false;
                p.ImageAuthority = 2;
                p.ScanInlinePart = false;
                p.Note = "3.1：无加速线路 / 无 loras 目录 / 无 comfy_dir；图像清单以内置快照为准，exe 仅做仓库修复";
                return p;
            }

            if (v == "3.3")
            {
                p.IsV33 = true; p.IsV35 = false;
                p.TempSubDir = Path.Combine("_locmodel_dl_tmp", "lmv23");
                p.HasLoraDir = true; p.RecognizeComfyDir = true;
                p.HasHfToken = false; p.HasAccelNodes = false; p.HasGithubMirror = false;
                p.CatalogHasSpec = false;
                p.ImageAuthority = 1;
                p.ScanInlinePart = false;
                p.Note = "3.3：accel_lines 加速线路并入自有 CDN 链；图像清单以 exe 解析为准、内置快照兜底";
                return p;
            }

            // 3.5（默认）
            p.IsV33 = true; p.IsV35 = true;
            p.TempSubDir = Path.Combine("_locmodel_dl_tmp", "lmv23");
            p.HasLoraDir = true; p.RecognizeComfyDir = true;
            p.HasHfToken = true; p.HasAccelNodes = true; p.HasGithubMirror = true;
            p.CatalogHasSpec = true;
            p.ImageAuthority = 0;
            p.ScanInlinePart = true;
            p.Note = "3.5：catalog 含模型规格字段，settings 含 hf_token/accel_nodes，主程序用就地 .part 半成品；图像清单以内置快照为准（exe 仅核对）";
            return p;
        }

        public string LabelOf()
        {
            return LabelOf(Version);
        }

        public static string LabelOf(string version)
        {
            string v = Normalize(version);
            if (v == "3.5") return "LocModel 3.5（默认）";
            if (v == "3.3") return "LocModel 3.3";
            return "LocModel 3.1";
        }

        public string TempRoot(string modelsDir)
        {
            return Path.Combine(modelsDir, TempSubDir);
        }

        // ================= 版本探测 =================
        // 判定顺序：exe 版本号（最可靠）→ 3.5 专有 settings 字段 → accel_lines → exe 内嵌特征串 → 默认版本
        public static string Detect(string exePath, bool hasAccelLines, out string detail)
        {
            return Detect(exePath, hasAccelLines, false, out detail);
        }

        public static string Detect(string exePath, bool hasAccelLines, bool hasV35Settings, out string detail)
        {
            detail = "";

            // 1) exe 文件版本号
            if (!string.IsNullOrEmpty(exePath))
            {
                try
                {
                    if (File.Exists(exePath))
                    {
                        FileVersionInfo fv = FileVersionInfo.GetVersionInfo(exePath);
                        string s = (fv.ProductVersion != null && fv.ProductVersion.Trim().Length > 0) ? fv.ProductVersion : fv.FileVersion;
                        if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0)
                        {
                            Match m = Regex.Match(s, @"(\d+)\.(\d+)");
                            if (m.Success)
                            {
                                string cand = m.Groups[1].Value + "." + m.Groups[2].Value;
                                string n = Normalize(cand);
                                // 只有真正等于 3.5/3.3/3.1 才采信，避免 3.4 之类被归一化成默认值
                                if (string.Equals(cand, n, StringComparison.Ordinal))
                                {
                                    detail = "exe 版本号 " + s.Trim();
                                    return n;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // 2) settings 里的 3.5 专有字段（hf_token / accel_nodes / github_mirror）
            if (hasV35Settings) { detail = "settings 含 3.5 专有字段"; return "3.5"; }

            // 3) 加速线路：3.3 起才有
            if (hasAccelLines) { detail = "settings 含 accel_lines"; return "3.3"; }

            // 4) exe 内嵌特征串
            if (!string.IsNullOrEmpty(exePath))
            {
                try
                {
                    if (File.Exists(exePath))
                    {
                        string text = Encoding.UTF8.GetString(File.ReadAllBytes(exePath));
                        if (text.IndexOf("accel_nodes", StringComparison.Ordinal) >= 0
                            || text.IndexOf("hf_token", StringComparison.Ordinal) >= 0)
                        { detail = "exe 内嵌 3.5 特征串"; return "3.5"; }
                        if (text.IndexOf("minimax-h3-image", StringComparison.Ordinal) >= 0
                            || text.IndexOf("MiniMax-H3-Image", StringComparison.Ordinal) >= 0)
                        { detail = "exe 内嵌 minimax-h3-image"; return "3.3"; }
                        if (text.IndexOf("minimax-h3", StringComparison.Ordinal) >= 0)
                        { detail = "exe 内嵌 minimax-h3"; return "3.1"; }
                    }
                }
                catch { }
            }

            detail = "未识别，按默认版本";
            return DefaultVersion;
        }
    }

    // 版本选择持久化（与程序同目录，纯文本，失败不影响运行）
    public static class VersionStore
    {
        public static string CfgPath
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                    return Path.Combine(dir, "LocModel下载器_版本配置.txt");
                }
                catch { return "LocModel下载器_版本配置.txt"; }
            }
        }

        public static string Load()
        {
            try
            {
                string p = CfgPath;
                if (File.Exists(p))
                {
                    string s = File.ReadAllText(p, Encoding.UTF8).Trim();
                    string n = LocModelProfile.Normalize(s);
                    if (LocModelProfile.IsKnown(n)) return n;
                }
            }
            catch { }
            return LocModelProfile.DefaultVersion;
        }

        public static void Save(string v)
        {
            try
            {
                string n = LocModelProfile.Normalize(v);
                File.WriteAllText(CfgPath, n + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
