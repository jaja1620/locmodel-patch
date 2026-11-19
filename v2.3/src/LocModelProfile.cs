// LocModel Downloader v2.3 - LocModel 版本适配层（3.1 / 3.3）
// 只负责“版本差异”：catalog 来源与解析策略、落盘目录规则、settings 字段解释。
// 下载 / 校验 / 续传内核（Downloader.cs）一律复用 v2.2.1，不感知版本。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LocModelDL
{
    public class LocModelProfile
    {
        public string Version = DefaultVersion;
        public bool IsV33 = true;
        public string TempSubDir = "_locmodel_dl_tmp";
        public bool HasLoraDir = false;
        public bool RecognizeComfyDir = false;
        public string Note = "";
        public string CatalogNote = "";

        public static readonly string[] Versions = new string[] { "3.3", "3.1" };
        public const string DefaultVersion = "3.3";

        public static string Normalize(string v)
        {
            if (string.IsNullOrEmpty(v)) return DefaultVersion;
            string t = v.Trim();
            if (t.StartsWith("3.1")) return "3.1";
            if (t.StartsWith("3.3")) return "3.3";
            return DefaultVersion;
        }

        public static LocModelProfile Get(string version)
        {
            string v = Normalize(version);
            LocModelProfile p = new LocModelProfile();
            if (v == "3.1")
            {
                p.Version = "3.1";
                p.IsV33 = false;
                p.TempSubDir = "_locmodel_dl_tmp";
                p.HasLoraDir = false;
                p.RecognizeComfyDir = false;
                p.Note = "3.1 适配：图像/视频清单取内置快照（无 minimax-h3-image），临时目录沿用 3.1 习惯位置";
                p.CatalogNote = "图像/视频 catalog：内置快照 + exe 存在性裁剪";
            }
            else
            {
                p.Version = "3.3";
                p.IsV33 = true;
                p.TempSubDir = Path.Combine("_locmodel_dl_tmp", "lmv23");   // 3.3 自带 _locmodel_dl_tmp，子目录避让
                p.HasLoraDir = true;
                p.RecognizeComfyDir = true;
                p.Note = "3.3 适配：图像/视频清单 exe 解析优先 + 内置快照兜底（含 minimax-h3-image），识别 image\\loras 与 comfy_dir";
                p.CatalogNote = "图像/视频 catalog：exe 解析优先 + 内置快照兜底";
            }
            return p;
        }

        // 界面下拉显示文案
        public static string LabelOf(string version)
        {
            string v = Normalize(version);
            return v == DefaultVersion ? "LocModel " + v + "（默认）" : "LocModel " + v;
        }

        public List<ModelEntry> ImageCatalog()
        {
            return EmbeddedImageCatalog.Build(Version);
        }

        // 断点续传 .part 存放目录（3.3 需要避让 LocModel 自己的临时目录）
        public string TempRoot(string modelsDir)
        {
            return Path.Combine(modelsDir, TempSubDir);
        }

        // 探测本机 LocModel 版本：exe 版本号 -> settings 特征 -> exe 内嵌清单特征 -> 默认 3.3
        public static string Detect(string exePath, bool hasAccelLines, out string detail)
        {
            detail = "";
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    FileVersionInfo fv = FileVersionInfo.GetVersionInfo(exePath);
                    string s = fv.ProductVersion;
                    if (string.IsNullOrEmpty(s)) s = fv.FileVersion;
                    if (!string.IsNullOrEmpty(s))
                    {
                        Match m = Regex.Match(s, @"(\d+)\.(\d+)");
                        if (m.Success)
                        {
                            string v = m.Groups[1].Value + "." + m.Groups[2].Value;
                            if (v == "3.1" || v == "3.3")
                            {
                                detail = "exe 版本号 " + s.Trim();
                                return v;
                            }
                        }
                    }
                }
                catch { }
            }
            if (hasAccelLines)
            {
                detail = "settings.json 含 3.3 专有字段 accel_lines";
                return "3.3";
            }
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    string t = Encoding.UTF8.GetString(File.ReadAllBytes(exePath));
                    if (t.IndexOf("minimax-h3-image", StringComparison.Ordinal) >= 0)
                    {
                        detail = "exe 内嵌清单含 minimax-h3-image";
                        return "3.3";
                    }
                    if (t.IndexOf("minimax-h3", StringComparison.Ordinal) >= 0)
                    {
                        detail = "exe 内嵌清单含 minimax-h3（无 h3-image），按 3.1 处理";
                        return "3.1";
                    }
                }
                catch { }
            }
            detail = "未识别到明确版本特征，按默认 3.3 处理";
            return DefaultVersion;
        }
    }

    // 下载器自身的版本选择持久化（与站点配置同目录，不改动 LocModel 的任何配置文件）
    public static class VersionStore
    {
        public static string CfgPath
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;
                return Path.Combine(dir, "LocModel下载器_版本配置.txt");
            }
        }

        public static string Load()
        {
            try
            {
                string p = CfgPath;
                if (File.Exists(p))
                {
                    string t = File.ReadAllText(p, Encoding.UTF8).Trim();
                    if (t.Length > 0) return LocModelProfile.Normalize(t);
                }
            }
            catch { }
            return LocModelProfile.DefaultVersion;
        }

        public static void Save(string version)
        {
            try { File.WriteAllText(CfgPath, LocModelProfile.Normalize(version) + "\r\n", new UTF8Encoding(false)); } catch { }
        }
    }
}
