// LocModel Downloader v3.1.5 - LocModel 版本适配层（3.6.5 / 3.6 / 3.5 / 3.3 / 3.1）
// 本文件只承载「版本差异」：settings 字段解释、catalog 规格字段能力、图像/视频清单的权威来源、
// 半成品残留的识别方式、落盘目录规则。下载 / 校验 / 续传内核（Downloader.cs、SiteProbe.cs）
// 自 v2.2.1 起字节不变，不感知版本；任何版本差异都在这一层收敛。
// v3.1.0：新增 3.6 档。3.6.0 实测（catalog 8033 B / 19 字段 / 16 文本模型、settings 36 字段、
//         内置图像与视频清单 13 条、image\loras、就地 .part + .part.meta、UD- 量化）与 3.5 完全一致，
//         故 3.6 与 3.5 共用全部能力位，仅默认档位切到 3.6。
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

        // v3.1.5：对口型（lipsync / InfiniteTalk）子系统是否存在。3.6.5 起主程序内置该子系统，
        // 下载器据此决定是否显示「对口型模型」入口；权重清单与落盘规则全部收敛在 LipsyncCatalog.cs。
        public bool HasLipsync = false;

        public string Note = "";

        // 界面下拉顺序：新版在前，默认 3.6.5
        public static readonly string[] Versions = new string[] { "3.6.5", "3.6", "3.5", "3.3", "3.1" };
        public const string DefaultVersion = "3.6.5";

        public static bool IsKnown(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            for (int i = 0; i < Versions.Length; i++)
                if (string.Equals(Versions[i], v.Trim(), StringComparison.Ordinal)) return true;
            return false;
        }

        // 版本号归一化：支持 "3.6" / "3.6.0" / "v3.6" / "3.6.0-beta" 等写法；无法识别时返回默认版本
        public static string Normalize(string v)
        {
            if (string.IsNullOrEmpty(v)) return DefaultVersion;
            string t = v.Trim();
            if (t.Length == 0) return DefaultVersion;
            if (t.StartsWith("LocModel", StringComparison.OrdinalIgnoreCase)) t = t.Substring(8).Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1).Trim();
            // v3.1.5：3.6.x 拆成两档 —— patch >= 5 归 3.6.5（内置对口型子系统），其余 3.6.x 仍归 3.6
            if (t.StartsWith("3.6"))
            {
                Match m = Regex.Match(t, @"^3\.6\.(\d+)");
                if (m.Success)
                {
                    int patch;
                    if (int.TryParse(m.Groups[1].Value, out patch) && patch >= 5) return "3.6.5";
                }
                return "3.6";
            }
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

            // 3.6.5 / 3.6 / 3.5：能力位完全相同，仅提示语与「是否内置对口型子系统」区分
            p.IsV33 = true; p.IsV35 = true;
            p.TempSubDir = Path.Combine("_locmodel_dl_tmp", "lmv23");
            p.HasLoraDir = true; p.RecognizeComfyDir = true;
            p.HasHfToken = true; p.HasAccelNodes = true; p.HasGithubMirror = true;
            p.CatalogHasSpec = true;
            p.ImageAuthority = 0;
            p.ScanInlinePart = true;
            p.HasLipsync = false;
            if (v == "3.6.5")
            {
                p.HasLipsync = true;
                p.Note = "3.6.5：在 3.6 能力位之上内置对口型子系统（InfiniteTalk + Wan2.1-I2V-14B-480P），"
                    + "settings 新增 lipsync_variant 等字段（只读展示，不回写）；对口型权重清单独立在 LipsyncCatalog.cs";
            }
            else if (v == "3.6")
                p.Note = "3.6：能力位与 3.5 一致（catalog 规格字段 / hf_token / accel_nodes / 就地 .part 半成品）；图像与视频清单以内置快照为准（exe 仅核对）";
            else
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
            if (v == "3.6.5") return "LocModel 3.6.5（默认）";
            if (v == "3.6") return "LocModel 3.6";
            if (v == "3.5") return "LocModel 3.5";
            if (v == "3.3") return "LocModel 3.3";
            return "LocModel 3.1";
        }

        public string TempRoot(string modelsDir)
        {
            return Path.Combine(modelsDir, TempSubDir);
        }

        // ================= 版本探测 =================
        // 判定顺序：exe 版本号（最可靠）→ 3.5/3.6 专有 settings 字段 → accel_lines → exe 内嵌特征串 → 默认版本
        // v3.1.0：3.5 与 3.6 的 settings 指纹和 exe 特征串完全一致（无法用指纹区分二者），
        //         因此指纹命中时一律落到默认版本（3.6），保证装了新版 LocModel 就按新版配置工作。
        public static string Detect(string exePath, bool hasAccelLines, out string detail)
        {
            return Detect(exePath, hasAccelLines, false, out detail);
        }

        public static string Detect(string exePath, bool hasAccelLines, bool hasV35Settings, out string detail)
        {
            return Detect(exePath, hasAccelLines, hasV35Settings, false, out detail);
        }

        // v3.1.5：新增 hasV365Settings（settings 含 lipsync_variant 等 3.6.5 专有字段）
        public static string Detect(string exePath, bool hasAccelLines, bool hasV35Settings, bool hasV365Settings, out string detail)
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
                            Match m = Regex.Match(s, @"(\d+)\.(\d+)(?:\.(\d+))?");
                            if (m.Success)
                            {
                                string cand = m.Groups[1].Value + "." + m.Groups[2].Value;
                                if (m.Groups[3].Success) cand += "." + m.Groups[3].Value;
                                string n = Normalize(cand);
                                // 只有真正命中已知档位才采信，避免 3.4 之类被归一化成默认值
                                bool hit = string.Equals(cand, n, StringComparison.Ordinal);
                                if (!hit && n == "3.6.5" && cand.StartsWith("3.6.", StringComparison.Ordinal)) hit = true;
                                if (!hit && n == "3.6" && cand.StartsWith("3.6", StringComparison.Ordinal)) hit = true;
                                if (hit)
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

            // 2) settings 里的 3.6.5 专有字段（lipsync_variant / engine_cn / tps_records …）—— 最可靠
            if (hasV365Settings) { detail = "settings 含 3.6.5 专有字段（lipsync_variant 等）"; return "3.6.5"; }

            // 2b) settings 里的 3.5/3.6 专有字段（hf_token / accel_nodes / github_mirror）
            //    这两个版本指纹完全一致，落到 3.6（3.5 与 3.6 同指纹，取较新的 3.6；
            //    3.6.5 由 2) 的 lipsync_variant 等字段单独识别，故这里不能落默认版本）
            if (hasV35Settings) { detail = "settings 含 3.5/3.6 专有字段（3.5 与 3.6 同指纹），按 3.6"; return "3.6"; }

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
                        // v3.1.5：3.6.5 内置对口型子系统，特征串优先判定
                        if (text.IndexOf("lipsync_variant", StringComparison.Ordinal) >= 0
                            || text.IndexOf("InfiniteTalk", StringComparison.Ordinal) >= 0
                            || text.IndexOf("infinitetalk", StringComparison.Ordinal) >= 0)
                        { detail = "exe 内嵌对口型子系统特征串"; return "3.6.5"; }
                        if (text.IndexOf("accel_nodes", StringComparison.Ordinal) >= 0
                            || text.IndexOf("hf_token", StringComparison.Ordinal) >= 0)
                        { detail = "exe 内嵌 3.5/3.6 特征串，按 3.6"; return "3.6"; }
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
