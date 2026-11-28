// LocModel Downloader v2.1 - catalog & settings acquisition
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace LocModelDL
{
    public class LocModelInfo
    {
        public string ExePath = "";
        public string SettingsPath = "";
        public string ModelsDir = "";
        public int Port = 0;
        public string HfMirror = "";
        public string CustomMirror = "";
        public string Source = "";

        // v2.3：LocModel 版本与 3.3 专有字段
        public string Version = "";
        public string VersionDetail = "";
        public List<string> AccelLines = new List<string>();
        public string ComfyDir = "";
        public string ImagesDir = "";
        public bool HasAccelLines = false;
        public bool HasAccountBase = false;

        // v2.4.2：3.5 专有字段（全部只读识别，任何情况下都不回写 LocModel 配置）
        public string HfToken = "";                       // hf_token 原值，仅在内存中使用，落日志必须脱敏
        public bool HasHfToken = false;
        public string GithubMirror = "";
        public List<string> AccelNodes = new List<string>();
        public string PreferredLine = "";
        public string PreferredIp = "";
        public bool HasV35Settings = false;               // 是否出现 3.5 专有字段（用于版本探测）

        // ===== v3.1.5 新增（3.6.5 只读识别，独立成段，便于维护与回滚）=====
        public bool HasV365Settings = false;              // 是否出现 3.6.5 专有字段（lipsync_variant 等）
        public string LipsyncVariant = "";                // lipsync_variant 原值（只读）
        public string HfTokenDisplay = "";                // hf_token 打码后的展示值（永远不含原文）
        // 其余未纳入语义解释的新字段：键 + 已打码/截断的展示值，仅用于界面只读展示
        public List<V365Field> ExtraSettings = new List<V365Field>();
        // ===== v3.1.5 新增结束 =====
    }

    // v3.1.5：3.6.5 新增字段的只读展示项
    public class V365Field
    {
        public string Name = "";
        public string Display = "";
        public bool Masked = false;

        public V365Field() { }
        public V365Field(string name, string display, bool masked)
        {
            Name = name; Display = display; Masked = masked;
        }
    }

    // v3.1.5：敏感值打码（界面上永不见原文；本类不参与任何写盘）
    public static class SecretMask
    {
        public static string Mask(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Trim();
            if (t.Length == 0) return "";
            if (t.Length <= 8) return new string('*', t.Length);
            return t.Substring(0, 4) + new string('*', Math.Min(8, t.Length - 8)) + t.Substring(t.Length - 4);
        }

        public static bool LooksSecret(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string k = key.ToLowerInvariant();
            return k.IndexOf("token") >= 0 || k.IndexOf("secret") >= 0 || k.IndexOf("key") >= 0
                || k.IndexOf("password") >= 0 || k.IndexOf("passwd") >= 0 || k.IndexOf("cookie") >= 0;
        }
    }

    public static class LocModelProbe
    {
        static readonly List<string> Cats = new List<string>();

        public static LocModelInfo Probe()
        {
            LocModelInfo info = new LocModelInfo();
            List<string> dirs = CandidateDirs();

            // 1) locmodel.exe
            foreach (string d in dirs)
            {
                try
                {
                    string exe = Path.Combine(d, "LocModel.exe");
                    if (File.Exists(exe)) { info.ExePath = exe; break; }
                }
                catch { }
            }

            // 2) settings.json（多份时取最近修改）
            string best = "";
            DateTime bestT = DateTime.MinValue;
            List<string> sCats = new List<string>();
            foreach (string d in dirs)
            {
                try
                {
                    string sp = Path.Combine(d, "settings.json");
                    if (File.Exists(sp))
                    {
                        sCats.Add(sp);
                        DateTime t = File.GetLastWriteTime(sp);
                        if (t > bestT) { bestT = t; best = sp; }
                    }
                }
                catch { }
            }
            // 常见用户目录兜底
            foreach (string sp in OtherSettingsCandidates())
            {
                if (File.Exists(sp))
                {
                    sCats.Add(sp);
                    DateTime t = File.GetLastWriteTime(sp);
                    if (t > bestT) { bestT = t; best = sp; }
                }
            }

            if (best != "")
            {
                info.SettingsPath = best;
                try { ParseSettings(File.ReadAllText(best, Encoding.UTF8), info); } catch { }
                // v3.1.5：hf_token 只以打码形式进入界面对象，原文仅留在 info.HfToken（内存）用于请求头
                info.HfTokenDisplay = SecretMask.Mask(info.HfToken);
            }

            // v2.3：识别本机 LocModel 版本（仅用于提示，实际生效版本以界面选择为准）
            // v2.4.2：探测链补一条「settings 是否带 3.5 专有字段」，避免 exe 版本号缺失时误判
            // v3.1.5：再补一条「settings 是否带 3.6.5 专有字段（lipsync_variant 等）」
            info.Version = LocModelProfile.Detect(info.ExePath, info.HasAccelLines, info.HasV35Settings, info.HasV365Settings, out info.VersionDetail);
            return info;
        }

        static IEnumerable<string> OtherSettingsCandidates()
        {
            string user = Environment.UserName;
            List<string> l = new List<string>();
            try
            {
                foreach (DriveInfo di in DriveInfo.GetDrives())
                {
                    if (di.DriveType != DriveType.Fixed && di.DriveType != DriveType.Removable) continue;
                    try
                    {
                        l.Add(Path.Combine(di.RootDirectory.FullName, "Users", user, "AppData", "Local", "LocModel", "settings.json"));
                    }
                    catch { }
                }
            }
            catch { }
            return l;
        }

        public static List<string> CandidateDirs()
        {
            List<string> dirs = new List<string>();
            // 注册表
            try
            {
                string[] roots = new string[] {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };
                RegistryKey[] hives = new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser };
                foreach (RegistryKey hive in hives)
                {
                    foreach (string r in roots)
                    {
                        try
                        {
                            using (RegistryKey k = hive.OpenSubKey(r))
                            {
                                if (k == null) continue;
                                foreach (string sub in k.GetSubKeyNames())
                                {
                                    try
                                    {
                                        using (RegistryKey sk = k.OpenSubKey(sub))
                                        {
                                            if (sk == null) continue;
                                            string dn = sk.GetValue("DisplayName") as string;
                                            if (dn == null || dn.IndexOf("LocModel", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                            string il = sk.GetValue("InstallLocation") as string;
                                            if (!string.IsNullOrEmpty(il) && Directory.Exists(il)) dirs.Add(il.TrimEnd('\\'));
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocModel")); } catch { }
            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LocModel")); } catch { }

            string user = Environment.UserName;
            try
            {
                foreach (DriveInfo di in DriveInfo.GetDrives())
                {
                    if (di.DriveType != DriveType.Fixed && di.DriveType != DriveType.Removable) continue;
                    try { dirs.Add(Path.Combine(di.RootDirectory.FullName, "Users", user, "AppData", "Local", "LocModel")); } catch { }
                }
            }
            catch { }

            List<string> outl = new List<string>();
            foreach (string d in dirs) { if (!string.IsNullOrEmpty(d) && !outl.Contains(d)) outl.Add(d); }
            return outl;
        }

        static void ParseSettings(string json, LocModelInfo info)
        {
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> d = js.DeserializeObject(json) as Dictionary<string, object>;
                if (d == null) return;
                object v;
                if (d.TryGetValue("models_dir", out v) && v != null) info.ModelsDir = v.ToString();
                if (d.TryGetValue("port", out v) && v != null) { int p; int.TryParse(v.ToString(), out p); info.Port = p; }
                if (d.TryGetValue("hf_mirror", out v) && v != null) info.HfMirror = v.ToString();
                if (d.TryGetValue("custom_mirror", out v) && v != null) info.CustomMirror = v.ToString();

                // v2.3：3.3 专有字段（只读识别，任何情况下都不回写 LocModel 配置）
                if (d.TryGetValue("accel_lines", out v) && v != null)
                {
                    info.HasAccelLines = true;
                    object[] arr = v as object[];
                    if (arr == null)
                    {
                        string one = v.ToString().Trim();
                        if (one.Length > 0) arr = new object[] { one };
                    }
                    if (arr != null)
                    {
                        foreach (object o in arr)
                        {
                            if (o == null) continue;
                            string u = o.ToString().Trim();
                            if (u.Length == 0) continue;
                            if (!info.AccelLines.Contains(u)) info.AccelLines.Add(u);
                        }
                    }
                }
                if (d.TryGetValue("account_base", out v) && v != null) info.HasAccountBase = true;
                if (d.TryGetValue("comfy_dir", out v) && v != null) info.ComfyDir = v.ToString();
                if (d.TryGetValue("images_dir", out v) && v != null) info.ImagesDir = v.ToString();

                // v2.4.2：3.5 专有字段（只读识别；hf_token 只在内存里用，绝不写日志、绝不回写）
                if (d.TryGetValue("hf_token", out v))
                {
                    info.HasV35Settings = true;
                    info.HasHfToken = true;
                    info.HfToken = (v == null) ? "" : v.ToString().Trim();
                }
                if (d.TryGetValue("github_mirror", out v))
                {
                    info.HasV35Settings = true;
                    if (v != null) info.GithubMirror = v.ToString();
                }
                if (d.TryGetValue("accel_nodes", out v))
                {
                    info.HasV35Settings = true;
                    object[] arr = v as object[];
                    if (arr != null)
                        foreach (object o in arr)
                        {
                            if (o == null) continue;
                            string u = o.ToString().Trim();
                            if (u.Length == 0) continue;
                            if (!info.AccelNodes.Contains(u)) info.AccelNodes.Add(u);
                        }
                }
                if (d.TryGetValue("preferred_line", out v))
                {
                    info.HasV35Settings = true;
                    if (v != null) info.PreferredLine = v.ToString();
                }
                if (d.TryGetValue("preferred_ip", out v) && v != null) info.PreferredIp = v.ToString();

                // ===== v3.1.5 新增：3.6.5 新字段（只读识别 + 打码，绝不回写）=====
                ParseV365Fields(d, info);
                // ===== v3.1.5 新增结束 =====
            }
            catch { }
        }

        // ===== v3.1.5 新增：3.6.5 专有字段的只读解析（独立成段，回滚只需删掉本方法与其调用）=====
        // 原则：① 只读展示，任何字段都不写回 settings.json；② 键名含 token/key/secret/password/cookie 的一律打码；
        //       ③ 值统一转字符串并截断到 160 字，避免界面被超长数组撑爆。
        static readonly string[] V365Known = new string[] {
            "lipsync_variant", "engine_cn", "account_ips", "account_base", "tps_cal_v", "tps_records",
            "ui_zoom", "img_remote", "hf_token", "github_mirror", "accel_nodes", "preferred_line",
            "preferred_ip", "models_dir", "port", "hf_mirror", "custom_mirror", "accel_lines",
            "comfy_dir", "images_dir"
        };

        static void ParseV365Fields(Dictionary<string, object> d, LocModelInfo info)
        {
            try
            {
                object v;
                // 3.6.5 指纹字段：任一项存在即认定 settings 为 3.6.5 结构
                string[] fp = new string[] { "lipsync_variant", "engine_cn", "tps_records", "tps_cal_v", "account_ips", "img_remote", "ui_zoom" };
                for (int i = 0; i < fp.Length; i++)
                    if (d.ContainsKey(fp[i])) { info.HasV365Settings = true; break; }

                if (d.TryGetValue("lipsync_variant", out v) && v != null) info.LipsyncVariant = v.ToString().Trim();

                // 未在语义层解释过的新字段，逐条收集成只读展示项
                foreach (KeyValuePair<string, object> kv in d)
                {
                    string key = kv.Key;
                    if (string.IsNullOrEmpty(key)) continue;
                    bool known = false;
                    for (int i = 0; i < V365Known.Length; i++)
                        if (string.Equals(V365Known[i], key, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                    if (known) continue;

                    string disp = ValueToText(kv.Value);
                    bool masked = SecretMask.LooksSecret(key);
                    if (masked) disp = SecretMask.Mask(disp);
                    if (disp.Length > 160) disp = disp.Substring(0, 160) + "…";
                    info.ExtraSettings.Add(new V365Field(key, disp, masked));
                }
                info.ExtraSettings.Sort(delegate (V365Field a, V365Field b) { return string.CompareOrdinal(a.Name, b.Name); });
            }
            catch { }
        }

        static string ValueToText(object o)
        {
            if (o == null) return "";
            object[] arr = o as object[];
            if (arr != null)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < arr.Length && i < 20; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(arr[i] == null ? "" : arr[i].ToString());
                }
                if (arr.Length > 20) sb.Append(", …共 " + arr.Length + " 项");
                return sb.ToString();
            }
            return o.ToString();
        }
        // ===== v3.1.5 新增结束 =====
    }

    public static class CatalogService
    {
        static CatalogService()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                ServicePointManager.DefaultConnectionLimit = 16;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
        }

        // 自有 CDN 链：仅用于 LocModel 自有资源（catalog.json 等）
        // v2.3：改为可注入（3.3 的 settings.accel_lines 优先），默认值保持 v2.2.1 原样
        public static string[] CatalogBases = new string[] {
            "https://m1.locmodel.com",
            "https://m2.locmodel.com",
            "https://activation.locmodel.com",
            "https://get.locmodel.com"
        };

        static readonly object CatalogBaseLock = new object();

        // 把 LocModel 3.3 settings 里的加速线路并入自有 CDN 链，排在默认线路之前（仅内存生效，不改配置）
        public static void SetCatalogBases(IEnumerable<string> accel)
        {
            if (accel == null) return;
            List<string> head = new List<string>();
            foreach (string s in accel)
            {
                if (string.IsNullOrEmpty(s)) continue;
                string v = s.Trim().TrimEnd('/');
                if (v.Length < 10) continue;
                if (v.IndexOf("://", StringComparison.Ordinal) < 0) continue;
                if (!head.Contains(v)) head.Add(v);
            }
            if (head.Count == 0) return;
            lock (CatalogBaseLock)
            {
                List<string> arr = new List<string>();
                foreach (string v in head) arr.Add(v);
                foreach (string v in CatalogBases) if (!arr.Contains(v)) arr.Add(v);
                CatalogBases = arr.ToArray();
            }
        }

        public static string[] CatalogBasesSnapshot()
        {
            lock (CatalogBaseLock) { return (string[])CatalogBases.Clone(); }
        }

        // 优化8：HF 解析链与自有 CDN 链分离。m1/m2 实测对 HF 路径直接 404，不再参与 HF 解析。
        // v3.1.1：HF 解析链同样只认「下载站点」栏位，huggingface.co 兜底已移除；
        // 本字段仅在栏位为空时作为默认站点使用。
        public static string[] HfBases = new string[] {
            "https://hf-mirror.com"
        };

        static readonly object HfBaseLock = new object();

        // v2.2：把界面里用户填写的站点并入 HF 解析链
        // v3.1.1：栏位是唯一来源 —— 只认栏位里填的站点，绝不再追加 hf-mirror.com / huggingface.co 兜底；
        // 栏位为空时用默认站点（hf-mirror.com）填位，保证解析链不为空。
        // LocModel 自有 CDN 不代理 huggingface.co 路径（实测 404），一律排除
        public static void SetHfBases(IEnumerable<string> userSites)
        {
            List<string> head = new List<string>();
            if (userSites != null)
            {
                foreach (string s in userSites)
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    string v = s.Trim().TrimEnd('/');
                    if (v.Length == 0) continue;
                    if (SiteStore.IsLocModelCdn(v)) continue;
                    if (!head.Contains(v)) head.Add(v);
                }
            }
            if (head.Count == 0) head.Add(SiteStore.DefaultSite);
            lock (HfBaseLock) { HfBases = head.ToArray(); }
        }

        public static string[] HfBasesSnapshot()
        {
            lock (HfBaseLock) { return (string[])HfBases.Clone(); }
        }

        // 优化7/优化8：记住可用的 HF 线路，后续解析优先复用
        static void PromoteHfBase(string b)
        {
            lock (HfBaseLock)
            {
                int idx = Array.IndexOf(HfBases, b);
                if (idx <= 0) return;
                string[] arr = (string[])HfBases.Clone();
                string hit = arr[idx];
                for (int i = idx; i > 0; i--) arr[i] = arr[i - 1];
                arr[0] = hit;
                HfBases = arr;
            }
        }

        // ---------- 语言/推理模型：以 LocModel 扫描为准（v3.1.1）----------
        // 顺序：① 扫描本机 LocModel.exe 内嵌模型库 → ② 服务端 catalog.json → ③ 内置快照（EmbeddedTextCatalog）
        // 依据：LocModel 小升级（如 3.6.0 → 3.6.1）常有新增模型，扫描能立即反映；服务端 catalog 与内置快照只作兜底。
        public static List<ModelEntry> LoadTextCatalog(string exePath, out string log)
        {
            StringBuilder sb = new StringBuilder();

            // ① 扫描本机 LocModel：唯一权威来源
            string l1;
            List<ModelEntry> scanned = LoadTextCatalogFromExe(exePath, out l1);
            sb.AppendLine(l1);
            if (scanned.Count >= 6)
            {
                sb.AppendLine("语言/推理清单以 LocModel 扫描为准（" + scanned.Count + " 个模型）");
                log = sb.ToString();
                return scanned;
            }
            sb.AppendLine("LocModel 扫描结果不足（" + scanned.Count + " 条），改用服务端 catalog.json");

            // ② 服务端 catalog.json
            string body = null;
            foreach (string b in CatalogBasesSnapshot())
            {
                try
                {
                    body = HttpGetText(b + "/catalog.json", 15000);
                    if (!string.IsNullOrEmpty(body) && body.TrimStart().StartsWith("{"))
                    {
                        sb.AppendLine("模型库 catalog.json <- " + b + " (" + body.Length + " 字节)");
                        break;
                    }
                    body = null;
                }
                catch (Exception ex) { sb.AppendLine("catalog.json " + b + " 失败: " + Short(ex.Message)); }
            }

            if (body != null)
            {
                List<ModelEntry> online = ParseCatalog(body, sb);
                if (online.Count > 0) { log = sb.ToString(); return online; }
                sb.AppendLine("服务端 catalog 无有效条目，降级为内置快照");
            }
            else sb.AppendLine("服务端 catalog 全部线路不可用，降级为内置快照");

            // ③ 内置快照（离线可用，永不失效）
            List<ModelEntry> snap = EmbeddedTextCatalog.Build();
            sb.AppendLine("内置快照：语言/推理模型 " + snap.Count + " 个（catalog 版本 " + EmbeddedTextCatalog.SnapshotVersion + "）");
            log = sb.ToString();
            return snap;
        }

        // ---------- 图像/视频模型：按 LocModel 版本同步（v2.3 新增版本分支）----------
        public static void SyncImageCatalogFromExe(List<ModelEntry> imageModels, string exePath, out string log)
        {
            SyncImageCatalogFromExe(imageModels, exePath, LocModelProfile.Get(LocModelProfile.DefaultVersion), out log);
        }

        public static void SyncImageCatalogFromExe(List<ModelEntry> imageModels, string exePath, LocModelProfile profile, out string log)
        {
            log = "";
            if (profile == null) profile = LocModelProfile.Get(LocModelProfile.DefaultVersion);
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                log = "未找到 LocModel.exe，使用内置快照（" + imageModels.Count + " 个模型）";
                return;
            }
            string text;
            try { text = Encoding.UTF8.GetString(File.ReadAllBytes(exePath)); }
            catch (Exception ex) { log = "读取 exe 失败: " + Short(ex.Message); return; }

            // v3.1.1：图像/视频模型一律「LocModel 扫描为准 + 内置快照兜底」——只增不删、字段只补不减，
            // 这样 LocModel 小升级新增的图像/视频模型能自动进入清单；3.1 仍保留旧的「内置优先，exe 仅做仓库修复」。
            if (profile.ImageAuthority == 2) { SyncFromExeV31(imageModels, text, out log); return; }
            SyncImageCatalogScanned(imageModels, text, profile, out log);
        }

        // ---------- v3.1.1：图像/视频清单「LocModel 扫描优先 + 内置快照兜底」 ----------
        // 规则：
        //   ① 扫描能解析出 ≥6 条即以扫描为准：快照条目刷新年份、扫描独有的仓库直接新增；
        //   ② 字段只补不减：量化档只做并集（补新档、不砍旧档），依赖只在快照为空时补，
        //      文件名模板始终留给下载期按仓库文件清单解析，不从粘连串里猜；
        //   ③ 快照条目在 exe 里搜不到仓库时只提示、不移除，避免解析器局限打掉可用清单。
        static void SyncImageCatalogScanned(List<ModelEntry> imageModels, string text, LocModelProfile profile, out string log)
        {
            List<ExeImageEntry> parsed = new List<ExeImageEntry>();
            string perr = "";
            try { parsed = ParseExeImageEntries(text); }
            catch (Exception ex) { perr = Short(ex.Message); }

            if (parsed.Count < 6)
            {
                log = "LocModel " + profile.Version + " 图像/视频清单扫描不足（解析到 " + parsed.Count + " 条"
                    + (perr.Length > 0 ? "：" + perr : "") + "），以内置快照为准（" + imageModels.Count + " 个模型）";
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("LocModel " + profile.Version + " 图像/视频清单：以扫描为准（扫描 " + parsed.Count + " 条 / 快照 " + imageModels.Count + " 条）");
            foreach (ExeImageEntry e in parsed)
                sb.Append("\n  · " + e.Slug + " ← " + e.Repo + "（量化 " + e.Quants.Count + " 档，依赖 " + e.Common.Count + "）");

            int refreshed = 0, added = 0, missing = 0, quantsAdd = 0;
            foreach (ModelEntry m in imageModels)
            {
                List<ExeImageEntry> hit = new List<ExeImageEntry>();
                foreach (ExeImageEntry e in parsed) if (SameRepo(m.Repo, e.Repo)) hit.Add(e);

                if (hit.Count == 0)
                {
                    // 扫描不到该仓库：只提示，不移除
                    if (!string.IsNullOrEmpty(m.Repo) && text.IndexOf(m.Repo, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        missing++;
                        sb.Append("\n  ! " + m.Id + "：LocModel 内未出现仓库 " + m.Repo + "（按快照保留）");
                    }
                    continue;
                }

                // 同仓库多条（minimax-h3 / minimax-h3-ref）：按量化档贴合度挑参考条目
                ExeImageEntry e0 = hit[0];
                int best = -1;
                foreach (ExeImageEntry e in hit)
                {
                    int sc = 0;
                    foreach (string q in m.Quants)
                        foreach (string q2 in e.Quants)
                            if (string.Equals(q, q2, StringComparison.OrdinalIgnoreCase)) sc++;
                    if (sc > best) { best = sc; e0 = e; }
                }

                bool ch = false;
                if (!string.IsNullOrEmpty(e0.Release) && !string.Equals(e0.Release, m.Release, StringComparison.Ordinal))
                {
                    m.Release = e0.Release;
                    ch = true;
                }

                // 量化档只做并集：补上扫描新增的档位，快照已有档位一律保留
                int addQ = 0;
                foreach (string q in e0.Quants)
                {
                    bool has = false;
                    foreach (string q0 in m.Quants) if (string.Equals(q0, q, StringComparison.OrdinalIgnoreCase)) { has = true; break; }
                    if (!has) { m.Quants.Add(q); addQ++; }
                }
                if (addQ > 0)
                {
                    quantsAdd++;
                    ch = true;
                    sb.Append("\n  + " + m.Id + " 补量化档 " + addQ + " 个");
                }

                // 依赖只补空：粘连串解析的依赖归属易失真，快照已有依赖时不动
                if (m.Common.Count == 0 && e0.Common.Count > 0)
                {
                    foreach (QuantFile qf in e0.Common) m.Common.Add(qf);
                    ch = true;
                }
                if (ch) refreshed++;
            }

            // 扫描里有、快照里没有的模型 → 新增条目；模板用 {q}（下载期按仓库文件清单解析真实文件名）
            foreach (ExeImageEntry e in parsed)
            {
                bool exists = false;
                foreach (ModelEntry m in imageModels) if (SameRepo(m.Repo, e.Repo)) { exists = true; break; }
                if (exists || string.IsNullOrEmpty(e.Slug)) continue;

                ModelEntry nm = new ModelEntry();
                nm.Id = e.Slug;
                nm.Name = e.Slug;
                nm.Release = e.Release;
                nm.Desc = "来自 LocModel " + profile.Version + " 内置下载清单（文件名在下载时按仓库清单解析）";
                nm.Tags = "图像 · 视频";
                nm.Repo = e.Repo;
                nm.MainFileTemplate = "{q}";
                nm.Category = e.Category;
                nm.IsImageLike = true;
                foreach (string q in e.Quants) if (!nm.Quants.Contains(q)) nm.Quants.Add(q);
                foreach (QuantFile qf in e.Common) nm.Common.Add(qf);
                imageModels.Add(nm);
                added++;
                sb.Append("\n  + 新增 " + e.Slug + "（" + e.Repo + "，量化 " + e.Quants.Count + " 档）");
            }

            sb.Append("\n  新增 " + added + " · 补档 " + quantsAdd + " · 快照保留提示 " + missing);
            log = sb.ToString();
        }

        // ---------- v2.4.2（LocModel 3.5）：内置快照为权威，exe 只做存在性核对 ----------
        // 依据：3.5 exe 内嵌图像/视频清单 13 条与 3.3 完全一致，主文件模板 × 每档量化、依赖 repo::路径
        // 20 项已逐条比对 HF 实际文件 0 偏差；而 v2.4.1 的粘连串解析在 3.5 上只出 11 条，且 slug / 依赖归属 /
        // 子路径大面积失真（ae.safetensors 记到 unsloth/Qwen3-4B、qwen_image_2.1_vae_bf16 丢 vae/ 前缀等）。
        // 因此这里不再让解析结果改动落盘清单，只输出核对结论，异常条目以提示形式告知用户。
        static void SyncFromSnapshotAuthoritative(List<ModelEntry> imageModels, string text, LocModelProfile profile, out string log)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("LocModel " + profile.Version + "：以内置快照为权威清单（" + imageModels.Count + " 个模型）");

            int noRepo = 0;
            foreach (ModelEntry m in imageModels)
            {
                if (string.IsNullOrEmpty(m.Repo)) continue;
                if (text.IndexOf(m.Repo, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    noRepo++;
                    sb.Append("\n  ! " + m.Id + "：exe 内未出现仓库 " + m.Repo + "（快照保留，下载时按 HF 实际清单校验）");
                }
            }

            List<ExeImageEntry> parsed = new List<ExeImageEntry>();
            try { parsed = ParseExeImageEntries(text); }
            catch { }

            int extra = 0;
            foreach (ExeImageEntry e in parsed)
            {
                if (string.IsNullOrEmpty(e.Repo)) continue;
                bool known = false;
                foreach (ModelEntry m in imageModels)
                    if (SameRepo(m.Repo, e.Repo)) { known = true; break; }
                if (known) continue;
                extra++;
                sb.Append("\n  + exe 内还出现 " + e.Repo + "（本版未纳入内置快照，可在“自定义搜索模型下载”里手动获取）");
            }

            sb.Append("\n  依赖核对：exe 解析 " + parsed.Count + " 条 / 快照 " + imageModels.Count + " 条")
              .Append(" · 快照内仓库全部命中 exe " + (noRepo == 0 ? "是" : "否（" + noRepo + " 条未命中）"))
              .Append(extra > 0 ? " · exe 额外条目 " + extra : "");
            log = sb.ToString();
        }

        // ---------- v2.2.1 原逻辑（LocModel 3.1 用）：内置值优先，exe 片段仅做仓库修复 ----------
        static void SyncFromExeV31(List<ModelEntry> imageModels, string text, out string log)
        {
            int changed = 0;
            List<string> notes = new List<string>();
            Regex repoRx = new Regex(@"[A-Za-z0-9_.\-]{2,}/[A-Za-z0-9_.\-]{2,}", RegexOptions.Compiled);

            foreach (ModelEntry m in imageModels)
            {
                int idx = text.IndexOf(m.Id, StringComparison.Ordinal);
                if (idx < 0) { notes.Add(m.Id + ": exe 内未找到条目"); continue; }
                int len = Math.Min(900, text.Length - idx);
                string seg = text.Substring(idx, len);

                // 仓库：内置值优先（能在 HF 验证即保持不变），否则用 exe 段落候选做修复
                if (!RepoExists(m.Repo))
                {
                    string fixedRepo = null;
                    foreach (Match cand in repoRx.Matches(seg))
                    {
                        string s = cand.Value;
                        if (s.StartsWith("http") || s.Contains("..")) continue;
                        bool prefixOk = s.StartsWith("Comfy-Org/") || s.StartsWith("comfyanonymous/") || s.StartsWith("city96/")
                            || s.StartsWith("mradermacher/") || s.StartsWith("QuantStack/") || s.StartsWith("leejet/")
                            || s.StartsWith("unsloth/") || s.StartsWith("Qwen/");
                        if (!prefixOk && s.IndexOf("-GGUF", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        fixedRepo = VerifyRepo(s);
                        if (fixedRepo != null) break;
                    }
                    if (fixedRepo != null && !string.Equals(fixedRepo, m.Repo, StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add(m.Id + ": 仓库 " + m.Repo + " -> " + fixedRepo);
                        m.Repo = fixedRepo;
                        changed++;
                    }
                }

                // 文件名模板不在 exe 字符串表里猜测（字符串相互粘连易串味），下载时按仓库文件清单解析
            }

            log = changed > 0
                ? ("已从 LocModel.exe 同步 " + changed + " 处差异\n" + string.Join("\n", notes.ToArray()))
                : ("图像/视频 catalog 与 LocModel.exe 一致（" + imageModels.Count + " 个模型）" + (notes.Count > 0 ? "\n" + string.Join("\n", notes.ToArray()) : ""));
        }

        // ================= v2.3：LocModel 3.3 图像/视频内嵌清单解析 =================
        // 3.3 exe 字符串表里相邻条目无分隔符（如 "...eulerminimax-h3-imageMiniMax-H3-Image..."），
        // 因此不从粘连串里猜 slug / 文件名（v2.0 踩过的坑），改为以「主仓库 + 落点结构」为锚点：
        //   描述/年份之后出现的第一个仓库 = 该条目的主仓库（开新条目），其后到下一个仓库之间的仓库 = 共享依赖；
        //   量化档只从主仓库窗口内的 "-Qx_K.gguf" 尾缀提取；文件名模板交给下载期按仓库文件清单匹配。
        class ExeImageEntry
        {
            public string Slug = "";
            public string Release = "";
            public string Repo = "";
            public string Category = "image";
            public List<string> Quants = new List<string>();
            public List<QuantFile> Common = new List<QuantFile>();
        }

        enum TokKind { Repo, Release, Cjk }

        class Tok
        {
            public int Pos;
            public int End;
            public TokKind Kind;
            public string Val = "";
        }

        // 仓库锚点：exe 里仓库名与后面的文件名/描述无分隔符粘连（"...-GGUFminimax_h3_image_pruned-Q3_K_M.gguf"），
        // 因此用「owner/name + 已知仓库名尾缀」的最短匹配把仓库名从粘连串里切出来
        static readonly Regex TokRepoRx = new Regex(@"[A-Za-z][A-Za-z0-9_\-]{1,23}/[A-Za-z0-9][A-Za-z0-9_\.\-]{2,70}?(?:GGUF|gguf|Repackaged|repackaged|repack)", RegexOptions.Compiled);
        static readonly Regex TokReleaseRx = new Regex(@"\d{4}年(\d{1,2}月)?(发布|推出)", RegexOptions.Compiled);
        static readonly Regex TokCjkRx = new Regex(@"[\u4e00-\u9fff]{2,}", RegexOptions.Compiled);
        static readonly Regex TokQuantRx = new Regex(@"[-_](Q\d(?:_[A-Za-z0-9]+)*)\.gguf", RegexOptions.Compiled);
        static readonly Regex TokFileRx = new Regex(@"[A-Za-z0-9_\.\-]+\.(?:gguf|safetensors)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static bool SameRepo(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // v2.4.2：仓库名 → 落盘 slug 的固定映射表
        // 3.5 实测：从粘连串推导出的 slug 与 LocModel 真实落盘目录名不一致（flux.1-schnell / flux.2-klein-4b /
        // qwen-image-edit-2511 / wan-2.1-comfyui-repackaged），会导致 image\<slug>\ 建错目录。
        // 这里按「去掉 . _ - 后的小写形式」做键，命中即用 LocModel 的真实目录名，避免歧义。
        static readonly Dictionary<string, string> SlugFix = BuildSlugFix();

        static Dictionary<string, string> BuildSlugFix()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d["flux1schnell"] = "flux1-schnell";
            d["flux2klein4b"] = "flux2-klein-4b";
            d["qwenimageedit2511"] = "qwen-image-edit";
            d["qwenimageedit"] = "qwen-image-edit";
            d["wan21comfyuirepackaged"] = "wan2.1-t2v-1.3b";
            d["wan21t2v13b"] = "wan2.1-t2v-1.3b";
            d["wan22i2va14b"] = "wan2.2-i2v-a14b";
            d["wan22ti2v5b"] = "wan2.2-ti2v-5b";
            d["minimaxh3"] = "minimax-h3";
            d["minimaxh3image"] = "minimax-h3-image";
            d["minimaxh3ref"] = "minimax-h3-ref";
            d["qwenimage"] = "qwen-image";
            d["qwenimage21"] = "qwen-image-2.1";
            d["zimageturbo"] = "z-image-turbo";
            d["ltx23"] = "ltx-2.3";
            return d;
        }

        static string SlugKey(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
                if (c != '.' && c != '_' && c != '-' && c != ' ') sb.Append(c);
            return sb.ToString();
        }

        static string SlugFromRepo(string repo)
        {
            int i = repo.LastIndexOf('/');
            string name = i < 0 ? repo : repo.Substring(i + 1);
            string[] tails = new string[] { "-GGUF", "-gguf", "-Gguf" };
            foreach (string t in tails)
                if (name.EndsWith(t, StringComparison.Ordinal) && name.Length > t.Length)
                {
                    name = name.Substring(0, name.Length - t.Length);
                    break;
                }
            string slug = name.ToLowerInvariant().Replace('_', '-');

            // 固定映射优先（含同仓库双条目 minimax-h3 / -image / -ref）
            string k = SlugKey(slug);
            string fixedSlug;
            if (SlugFix.TryGetValue(k, out fixedSlug)) return fixedSlug;
            if (k.IndexOf("minimaxh3ref") >= 0) return "minimax-h3-ref";
            if (k.IndexOf("minimaxh3image") >= 0) return "minimax-h3-image";
            return slug;
        }

        static string CategoryOfSlug(string slug)
        {
            string s = slug ?? "";
            if (s.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0) return "image";
            if (s.IndexOf("t2v", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("i2v", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("video", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("wan", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("ltx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("h3", StringComparison.OrdinalIgnoreCase) >= 0) return "video";
            return "image";
        }

        // 仓库可信度：过滤 exe 里大量 "HTTP/1.1"、"text/css"、"images/xxx" 之类的路径噪声
        static bool IsPlausibleRepo(string repo)
        {
            if (string.IsNullOrEmpty(repo) || repo.Length < 10 || repo.Length > 90) return false;
            int sl = repo.IndexOf('/');
            if (sl <= 1 || sl >= repo.Length - 3) return false;
            string owner = repo.Substring(0, sl);
            string name = repo.Substring(sl + 1);
            if (owner.IndexOf('.') >= 0) return false;
            if (owner.Length < 2 || owner.Length > 24) return false;
            if (name.IndexOf('.') == 0) return false;
            if (!char.IsLetter(name[0])) return false;
            return true;
        }

        static List<Tok> ScanTokens(string text)
        {
            List<Tok> toks = new List<Tok>();
            foreach (Match m in TokRepoRx.Matches(text))
            {
                string v = m.Value;
                if (v.IndexOf("://", StringComparison.Ordinal) >= 0) continue;
                if (v.IndexOf("..", StringComparison.Ordinal) >= 0) continue;
                if (v.IndexOf("AppData", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (v.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ||
                    v.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)) continue;   // 目录/文件名，非仓库
                int sl = v.IndexOf('/');
                if (sl <= 0 || sl >= v.Length - 1) continue;
                toks.Add(new Tok { Pos = m.Index, End = m.Index + m.Length, Kind = TokKind.Repo, Val = v });
            }
            foreach (Match m in TokReleaseRx.Matches(text))
                toks.Add(new Tok { Pos = m.Index, End = m.Index + m.Length, Kind = TokKind.Release, Val = m.Value });
            foreach (Match m in TokCjkRx.Matches(text))
                toks.Add(new Tok { Pos = m.Index, End = m.Index + m.Length, Kind = TokKind.Cjk, Val = m.Value });
            toks.Sort(delegate(Tok a, Tok b) { return a.Pos.CompareTo(b.Pos); });
            return toks;
        }

        static string WindowBetween(string text, int start, List<Tok> toks, int idx)
        {
            int end = text.Length;
            for (int j = idx + 1; j < toks.Count; j++)
            {
                if (toks[j].Pos < start) continue;
                end = toks[j].Pos;
                break;
            }
            if (end <= start) return "";
            if (end > text.Length) end = text.Length;
            return text.Substring(start, end - start);
        }

        static string LastReleaseBefore(List<Tok> toks, int idx)
        {
            for (int j = idx - 1; j >= 0; j--)
            {
                if (toks[j].Kind == TokKind.Repo) break;
                if (toks[j].Kind == TokKind.Release) return toks[j].Val;
            }
            return "";
        }

        static List<string> FileTokens(string win)
        {
            List<string> list = new List<string>();
            foreach (Match m in TokFileRx.Matches(win))
            {
                string v = m.Value;
                int i = v.LastIndexOf('/');
                if (i >= 0) v = v.Substring(i + 1);
                if (v.Length < 5) continue;
                if (!list.Contains(v)) list.Add(v);
            }
            return list;
        }

        static List<ExeImageEntry> ParseExeImageEntries(string text)
        {
            List<ExeImageEntry> result = new List<ExeImageEntry>();
            List<Tok> toks = ScanTokens(text);

            ExeImageEntry cur = null;
            bool pendingMain = false;

            for (int i = 0; i < toks.Count; i++)
            {
                Tok t = toks[i];
                if (t.Kind == TokKind.Release || t.Kind == TokKind.Cjk)
                {
                    if (cur != null) pendingMain = true;
                    continue;
                }

                string win = WindowBetween(text, t.End, toks, i);
                bool hasFile = TokFileRx.IsMatch(win);
                bool okRepo = IsPlausibleRepo(t.Val);
                if (!okRepo || !hasFile) continue;   // 不可信仓库 / 后面没有模型文件 → 视为噪声
                bool isMain = (cur == null) || pendingMain;

                if (isMain)
                {
                    cur = new ExeImageEntry();
                    cur.Repo = t.Val;
                    cur.Slug = SlugFromRepo(t.Val);
                    cur.Category = CategoryOfSlug(cur.Slug);
                    cur.Release = LastReleaseBefore(toks, i);
                    result.Add(cur);
                    pendingMain = false;
                    foreach (Match m in TokQuantRx.Matches(win))
                    {
                        string q = m.Groups[1].Value;
                        if (!cur.Quants.Contains(q)) cur.Quants.Add(q);
                    }
                }
                else if (cur != null)
                {
                    List<string> files = FileTokens(win);
                    foreach (string f in files) cur.Common.Add(new QuantFile(t.Val, f));
                }
            }

            // 去重依赖 + 丢弃无文件依赖
            foreach (ExeImageEntry e in result)
            {
                List<QuantFile> keep = new List<QuantFile>();
                List<string> seen = new List<string>();
                foreach (QuantFile qf in e.Common)
                {
                    if (string.IsNullOrEmpty(qf.Path)) continue;
                    string key = (qf.Repo + "|" + qf.Path).ToLowerInvariant();
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    keep.Add(qf);
                }
                e.Common = keep;
            }
            return result;
        }

        // 从 LocModel.exe 内嵌字符串中提取语言/推理 catalog（服务端失效时兜底，永不失效）
        public static List<ModelEntry> LoadTextCatalogFromExe(string exePath, out string log)
        {
            log = "";
            List<ModelEntry> empty = new List<ModelEntry>();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) { log = "未找到 LocModel.exe，无法内嵌兜底"; return empty; }
            string text;
            try { text = Encoding.UTF8.GetString(File.ReadAllBytes(exePath)); }
            catch (Exception ex) { log = "读取 exe 失败: " + Short(ex.Message); return empty; }

            int pos = 0, guard = 0;
            while (guard++ < 60)
            {
                int idx = text.IndexOf("\"models\"", pos, StringComparison.Ordinal);
                if (idx < 0) break;
                pos = idx + 8;
                int start = text.LastIndexOf('{', idx);
                if (start < 0) continue;
                string json = ExtractBalanced(text, start);
                if (json == null) continue;
                StringBuilder sb = new StringBuilder();
                List<ModelEntry> list = ParseCatalog(json, sb);
                if (list.Count > 0)
                {
                    log = "内嵌模型库(LocModel.exe): " + list.Count + " 个语言/推理模型";
                    return list;
                }
            }
            log = "exe 内未找到可解析的内嵌模型库";
            return empty;
        }

        // 花括号配平提取（跳过字符串内容与转义字符）
        static string ExtractBalanced(string s, int start)
        {
            int depth = 0; bool inStr = false, esc = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return s.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        static List<ModelEntry> ParseCatalog(string body, StringBuilder sb)
        {
            List<ModelEntry> list = new List<ModelEntry>();
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 64 * 1024 * 1024;
                Dictionary<string, object> root = js.DeserializeObject(body) as Dictionary<string, object>;
                if (root == null) { sb.AppendLine("catalog 解析为空"); return list; }
                object ver; if (root.TryGetValue("version", out ver)) sb.AppendLine("catalog 版本: " + ver);
                object mo;
                if (!root.TryGetValue("models", out mo)) { sb.AppendLine("catalog 无 models 字段"); return list; }
                object[] arr = mo as object[];
                if (arr == null) { sb.AppendLine("catalog models 不是数组"); return list; }
                foreach (object o in arr)
                {
                    Dictionary<string, object> m = o as Dictionary<string, object>;
                    if (m == null) continue;
                    ModelEntry e = new ModelEntry();
                    e.Id = S(m, "id");
                    e.Name = S(m, "name");
                    e.Org = S(m, "org");
                    e.Desc = S(m, "desc");
                    e.Release = S(m, "release");
                    e.Repo = S(m, "repo");
                    e.Category = "text";
                    e.IsImageLike = false;
                    object vis; e.Vision = m.TryGetValue("vision", out vis) && vis is bool && (bool)vis;
                    object tags;
                    if (m.TryGetValue("tags", out tags) && tags is object[])
                    {
                        List<string> tl = new List<string>();
                        foreach (object tg in (object[])tags) if (tg != null) tl.Add(tg.ToString());
                        e.Tags = string.Join(" · ", tl.ToArray());
                    }
                    object qs;
                    if (m.TryGetValue("quants", out qs) && qs is object[])
                        foreach (object q in (object[])qs) if (q != null) e.Quants.Add(q.ToString());
                    object sz;
                    if (m.TryGetValue("size_gb", out sz) && sz is Dictionary<string, object>)
                        foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)sz)
                        {
                            double dv;
                            if (kv.Value != null && double.TryParse(kv.Value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dv))
                                e.SizeMap[kv.Key] = dv;
                        }
                    if (string.IsNullOrEmpty(e.Name)) e.Name = e.Id;
                    if (string.IsNullOrEmpty(e.Repo)) continue;

                    // v2.4.2：LocModel 3.5 catalog 新增的模型规格字段（3.3/3.1 没有，取不到就保持 0）
                    e.ParamsB = D(m, "params_b");
                    e.ActiveB = D(m, "active_b");
                    e.Layers = I(m, "layers");
                    e.KvLayers = I(m, "kv_layers");
                    e.KvHeads = I(m, "kv_heads");
                    e.HeadDim = I(m, "head_dim");
                    e.Experts = I(m, "experts");
                    e.ExpertsUsed = I(m, "experts_used");
                    e.CtxMax = L(m, "ctx_max");

                    // 内部校验：量化档在 size_gb 里缺失时补日志（只提示，不阻断下载）
                    if (e.SizeMap.Count > 0)
                    {
                        List<string> miss = new List<string>();
                        foreach (string q in e.Quants) if (!e.SizeMap.ContainsKey(q)) miss.Add(q);
                        if (miss.Count > 0 && sb != null)
                            sb.AppendLine("  ! " + e.Id + " 有 " + miss.Count + " 档量化缺体积数据：" + string.Join(",", miss.ToArray()));
                    }

                    e.MainFileTemplate = "{q}";
                    list.Add(e);
                }
                int specCnt = 0;
                foreach (ModelEntry e0 in list) if (e0.HasSpec) specCnt++;
                sb.AppendLine("语言/推理模型: " + list.Count + " 个" + (specCnt > 0 ? "（含规格字段 " + specCnt + " 个）" : ""));
            }
            catch (Exception ex) { sb.AppendLine("catalog 解析失败: " + Short(ex.Message)); }
            return list;
        }

        static string S(Dictionary<string, object> m, string k)
        {
            object v; if (m.TryGetValue(k, out v) && v != null) return v.ToString();
            return "";
        }

        // v2.4.2：catalog 数值字段的宽松解析（3.5 的 params_b 可能给 180 / "180" / 180.0）
        static double D(Dictionary<string, object> m, string k)
        {
            object v; if (!m.TryGetValue(k, out v) || v == null) return 0;
            double d;
            if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float
                , System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }

        static int I(Dictionary<string, object> m, string k)
        {
            object v; if (!m.TryGetValue(k, out v) || v == null) return 0;
            int i;
            if (int.TryParse(v.ToString(), System.Globalization.NumberStyles.Float
                , System.Globalization.CultureInfo.InvariantCulture, out i)) return i;
            double d = D(m, k);
            return (int)Math.Round(d);
        }

        static long L(Dictionary<string, object> m, string k)
        {
            object v; if (!m.TryGetValue(k, out v) || v == null) return 0;
            long l;
            if (long.TryParse(v.ToString(), System.Globalization.NumberStyles.Float
                , System.Globalization.CultureInfo.InvariantCulture, out l)) return l;
            double d = D(m, k);
            return (long)Math.Round(d);
        }

        static readonly Dictionary<string, int> RepoCheck = new Dictionary<string, int>();

        static bool RepoExists(string repo)
        {
            if (string.IsNullOrEmpty(repo)) return false;
            lock (RepoCheck) { int c; if (RepoCheck.TryGetValue(repo, out c)) return c > 0; }
            bool ok = false;
            try { string e; string[] s = GetSiblings(repo, out e); ok = s != null && s.Length > 0; }
            catch { }
            lock (RepoCheck) { RepoCheck[repo] = ok ? 1 : 0; }
            return ok;
        }

        // exe 内字符串相互粘连，对候选仓库做逐级截断 + HF 存在性验证
        static string VerifyRepo(string glued)
        {
            if (string.IsNullOrEmpty(glued)) return null;
            int slash = glued.IndexOf('/');
            if (slash <= 0) return null;
            List<string> cands = new List<string>();
            cands.Add(glued.TrimEnd('-', '_', '.'));
            for (int i = glued.Length - 1; i > slash + 1 && cands.Count < 6; i--)
            {
                if (!char.IsUpper(glued[i])) continue;
                string c = glued.Substring(0, i).TrimEnd('-', '_', '.');
                if (c.Length > slash + 1 && !cands.Contains(c)) cands.Add(c);
            }
            foreach (string c in cands)
                if (c.IndexOf(' ') < 0 && RepoExists(c)) return c;
            return null;
        }

        static string Short(string s) { if (s == null) return ""; return s.Length > 80 ? s.Substring(0, 80) : s; }

        // ---------- v2.4.1：极小元数据文件单独抓取 ----------
        // 内核 DownloadEngine 的 Verify 把 <512B 的文件判为「疑似错误响应」并丢弃（Downloader.cs 已冻结不改），
        // 但 HF 仓库里 special_tokens_map.json(90B)、tokenizer_config.json(236B) 都是合法文件，
        // 交给引擎会导致整批下载失败，这里用轻量 GET 直接落盘。
        public static bool FetchSmallFile(string repo, string path, string destPath, out string err)
        {
            err = "";
            string last = "";
            foreach (string b in HfBases)
            {
                string url = b + "/" + repo + "/resolve/main/" + path;
                try
                {
                    string body = HttpGetText(url, 20000);
                    string dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(destPath, body, new UTF8Encoding(false));
                    return true;
                }
                catch (Exception ex) { last = Short(ex.Message); }
            }
            err = string.IsNullOrEmpty(last) ? "所有站点均无法访问" : last;
            return false;
        }

        // ---------- v2.4.2：hf_token（LocModel 3.5 settings.hf_token，只读使用，绝不回写）----------
        // 策略（用户确认方案 A）：仅在直连 huggingface.co 时携带 token；hf-mirror.com 及用户自定义站点
        // 一律不带，避免 token 经过第三方镜像。token 只保存在内存里，任何日志输出都走 MaskToken。
        static string HfToken = "";

        public static void SetHfToken(string token)
        {
            HfToken = (token == null) ? "" : token.Trim();
        }

        public static bool HasHfToken { get { return HfToken.Length > 0; } }

        // 日志用：hf_****c81 形态（保留前缀与前 3 位尾号，其余打码）
        public static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return "(空)";
            string t = token.Trim();
            if (t.Length <= 8) return "****";
            int cut = t.IndexOf('_');
            string head = (cut > 0 && cut <= 6) ? t.Substring(0, cut + 1) : "";
            string tail = t.Substring(t.Length - Math.Min(3, t.Length));
            return head + "****" + tail;
        }

        // 只有 huggingface.co 直连才注入 Authorization
        static void ApplyAuth(HttpWebRequest req, string url)
        {
            if (HfToken.Length == 0) return;
            try
            {
                Uri u = new Uri(url);
                if (!string.Equals(u.Host, "huggingface.co", StringComparison.OrdinalIgnoreCase)) return;
                req.Headers["Authorization"] = "Bearer " + HfToken;
            }
            catch { }
        }

        // 自检用：给定 URL 会带上的 Authorization（打码），用于断言"只在 huggingface.co 直连携带"
        public static string DebugAuthHeader(string url)
        {
            if (HfToken.Length == 0) return "(未配置 token)";
            try
            {
                Uri u = new Uri(url);
                if (!string.Equals(u.Host, "huggingface.co", StringComparison.OrdinalIgnoreCase)) return "(不携带)";
                return "Bearer " + MaskToken(HfToken);
            }
            catch { return "(URL 无效)"; }
        }

        public static string HttpGetText(string url, int timeoutMs)
        {
            // v2.4：.NET 4.0 不把 308 当重定向，hf-mirror 现在整站 308 跳到 huggingface.co，
            // 这里手动跟随（301/302/303/307 仍交给 AllowAutoRedirect），最多 5 跳。
            string cur = url;
            for (int hop = 0; hop < 5; hop++)
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(cur);
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "LocModel/0.1";
                req.Accept = "application/json,text/plain,*/*";
                req.AllowAutoRedirect = true;
                // 308 跳到 huggingface.co 时按跳转后的最终地址判断是否带 token
                ApplyAuth(req, cur);
                try
                {
                    using (WebResponse resp = req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                        return sr.ReadToEnd();
                }
                catch (WebException we)
                {
                    HttpWebResponse hr = we.Response as HttpWebResponse;
                    if (hr == null) throw;
                    int code = (int)hr.StatusCode;
                    string loc = hr.Headers["Location"];
                    try { hr.Close(); } catch { }
                    if ((code == 308 || code == 301 || code == 302 || code == 303 || code == 307) && !string.IsNullOrEmpty(loc))
                    {
                        cur = loc.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? loc : new Uri(new Uri(cur), loc).ToString();
                        continue;
                    }
                    throw;
                }
            }
            throw new Exception("重定向次数过多: " + url);
        }

        // ---------- HF 仓库文件清单（等价 LocModel repo_quants） ----------
        static readonly Dictionary<string, string[]> SibCache = new Dictionary<string, string[]>();

        // ---------- v2.4.1：仓库文件清单（同时取真实字节数，并对失败原因分类） ----------
        // 依据：HF api/models/{repo}?blobs=true 的 siblings[] 已带 size / lfs.size，
        //       不再依赖 /tree/main（hf-mirror 对该路径返回 403，导致体积一直显示"大小未知"）。
        public class RepoFiles
        {
            public string Repo = "";
            public List<string> Paths = new List<string>();
            public Dictionary<string, long> Sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            public string Err = "";
            public string ErrKind = "";   // "" | notfound | auth | empty | net
            public string Base = "";

            public bool Ok { get { return Paths.Count > 0; } }

            public List<string> Ext(string ext)
            {
                List<string> l = new List<string>();
                foreach (string p in Paths)
                    if (p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) l.Add(p);
                return l;
            }

            public long Sum(IList<string> files)
            {
                long t = 0;
                if (files == null) return 0;
                foreach (string f in files) { long v; if (Sizes.TryGetValue(f, out v)) t += v; }
                return t;
            }
        }

        static readonly Dictionary<string, RepoFiles> RepoFilesCache = new Dictionary<string, RepoFiles>();

        public static void DropRepoFilesCache(string repo)
        {
            lock (RepoFilesCache) { if (!string.IsNullOrEmpty(repo)) RepoFilesCache.Remove(repo); }
        }

        public static RepoFiles GetRepoFiles(string repo)
        {
            RepoFiles rf = new RepoFiles();
            rf.Repo = repo;
            if (string.IsNullOrEmpty(repo)) { rf.ErrKind = "empty"; rf.Err = "仓库为空"; return rf; }
            lock (RepoFilesCache) { RepoFiles c; if (RepoFilesCache.TryGetValue(repo, out c)) return c; }

            bool sawNotFound = false, sawAuth = false;
            string lastNet = "";
            foreach (string b in HfBases)
            {
                string url = b + "/api/models/" + repo + "?blobs=true";
                try
                {
                    string body = HttpGetText(url, 20000);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    js.MaxJsonLength = 64 * 1024 * 1024;
                    Dictionary<string, object> root = js.DeserializeObject(body) as Dictionary<string, object>;
                    object sib;
                    if (root != null && root.TryGetValue("siblings", out sib) && sib is object[])
                    {
                        RepoFiles got = new RepoFiles();
                        got.Repo = repo; got.Base = b;
                        foreach (object o in (object[])sib)
                        {
                            Dictionary<string, object> d = o as Dictionary<string, object>;
                            if (d == null) continue;
                            string p = S(d, "rfilename");
                            if (p.Length == 0) continue;
                            got.Paths.Add(p);
                            long sz = ToLong(d, "size");
                            if (sz <= 0 && d.ContainsKey("lfs") && d["lfs"] is Dictionary<string, object>)
                                sz = ToLong((Dictionary<string, object>)d["lfs"], "size");
                            if (sz > 0) got.Sizes[p] = sz;
                        }
                        if (got.Paths.Count == 0) { got.ErrKind = "empty"; got.Err = repo + " 是空仓库（没有任何文件）"; }
                        lock (RepoFilesCache) { RepoFilesCache[repo] = got; }
                        lock (SibCache) { SibCache[repo] = got.Paths.ToArray(); }
                        if (got.Sizes.Count > 0) lock (SizeCache) { SizeCache[repo] = got.Sizes; }
                        if (got.Ok) PromoteHfBase(b);
                        return got;
                    }
                    lastNet = "返回体里没有 siblings 字段";
                }
                catch (WebException we)
                {
                    HttpWebResponse hr = we.Response as HttpWebResponse;
                    int code = hr == null ? 0 : (int)hr.StatusCode;
                    try { if (hr != null) hr.Close(); } catch { }
                    if (code == 404) sawNotFound = true;
                    else if (code == 401 || code == 403) { sawAuth = true; lastNet = "HTTP " + code; }
                    else lastNet = Short(we.Message);
                }
                catch (Exception ex) { lastNet = Short(ex.Message); }
            }

            if (sawNotFound) { rf.ErrKind = "notfound"; rf.Err = repo + " 不存在（HTTP 404，检查 owner/name 拼写）"; }
            else if (sawAuth) { rf.ErrKind = "auth"; rf.Err = repo + " 是私有或受限仓库（HTTP 401/403）"; }
            else { rf.ErrKind = "net"; rf.Err = string.IsNullOrEmpty(lastNet) ? "所有站点均无法访问" : lastNet; }
            return rf;
        }

        public static string[] GetSiblings(string repo, out string err)
        {
            err = "";
            lock (SibCache) { string[] c; if (SibCache.TryGetValue(repo, out c)) return c; }
            foreach (string b in HfBases)
            {
                try
                {
                    string body = HttpGetText(b + "/api/models/" + repo, 20000);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    js.MaxJsonLength = 64 * 1024 * 1024;
                    Dictionary<string, object> root = js.DeserializeObject(body) as Dictionary<string, object>;
                    object sib; if (root != null && root.TryGetValue("siblings", out sib) && sib is object[])
                    {
                        List<string> l = new List<string>();
                        foreach (object o in (object[])sib)
                        {
                            Dictionary<string, object> d = o as Dictionary<string, object>;
                            if (d == null) continue;
                            object n; if (d.TryGetValue("rfilename", out n) && n != null) l.Add(n.ToString());
                        }
                        string[] arr = l.ToArray();
                        lock (SibCache) { SibCache[repo] = arr; }
                        PromoteHfBase(b);
                        return arr;
                    }
                }
                catch (Exception ex) { err = b + ": " + Short(ex.Message); }
            }
            return new string[0];
        }

        // 把某个量化解析为"仓库内文件路径列表"
        // 量化文件名解析：等价 LocModel 的 repo_quants（按仓库文件清单匹配）
        // ---------- 仓库文件真实字节数（HF tree API，用于容量预检与进度） ----------
        static readonly Dictionary<string, Dictionary<string, long>> SizeCache = new Dictionary<string, Dictionary<string, long>>();

        public static Dictionary<string, long> GetFileSizes(string repo)
        {
            if (string.IsNullOrEmpty(repo)) return new Dictionary<string, long>();
            lock (SizeCache) { Dictionary<string, long> c0; if (SizeCache.TryGetValue(repo, out c0)) return c0; }
            Dictionary<string, long> map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 32 * 1024 * 1024;
            foreach (string b in HfBases)
            {
                try
                {
                    string txt = HttpGetText(b + "/api/models/" + repo + "/tree/main?recursive=true", 20000);
                    object root = js.DeserializeObject(txt);
                    IEnumerable arr = root as IEnumerable;
                    if (arr == null || root is string) continue;
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        string path = S(d, "path");
                        if (path.Length == 0) continue;
                        long sz = ToLong(d, "size");
                        if (sz <= 0 && d.ContainsKey("lfs") && d["lfs"] is Dictionary<string, object>)
                            sz = ToLong((Dictionary<string, object>)d["lfs"], "size");
                        if (sz > 0) map[path] = sz;
                    }
                    if (map.Count > 0) { PromoteHfBase(b); break; }
                }
                catch { }
            }
            // v2.4.1：空结果不写缓存，避免把一次网络失败钉死成"仓库无体积信息"
            if (map.Count > 0) lock (SizeCache) SizeCache[repo] = map;
            return map;
        }

        static long ToLong(Dictionary<string, object> m, string k)
        {
            object v; if (!m.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToInt64(v); } catch { return 0; }
        }

        public static List<string> ResolveQuantFiles(string repo, string quant, string template, out string err)
        {
            err = "";
            List<string> outl = new List<string>();
            if (string.IsNullOrEmpty(template)) { err = "模板为空"; return outl; }
            string expand = template.Replace("{q}", quant);
            bool hasPath = template.IndexOf('/') >= 0;

            string[] sibs = null;
            try { sibs = GetSiblings(repo, out err); }
            catch (Exception ex) { err = Short(ex.Message); }
            if (sibs == null || sibs == null || sibs.Length == 0)
            {
                if (hasPath) { outl.Add(expand); err = ""; }
                else if (string.IsNullOrEmpty(err)) err = "无法获取仓库 " + repo + " 的文件清单";
                return outl;
            }
            if (hasPath)
                foreach (string f in sibs)
                    if (string.Equals(f, expand, StringComparison.OrdinalIgnoreCase)) { outl.Add(expand); return outl; }

            // 分片:-00001-of-0000N.gguf / _00001-of_0000N.gguf：必须取回全部分片
            List<string> shards = new List<string>();
            int shardTotal = 0;
            foreach (string f in sibs)
            {
                string bn = BaseName(f);
                if (!bn.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;
                if (bn.IndexOf(quant, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Match sm = Regex.Match(bn, @"-0*1-of-0*(\d+)\.gguf$", RegexOptions.IgnoreCase);
                if (sm.Success) shardTotal = int.Parse(sm.Groups[1].Value);
            }
            if (shardTotal > 0)
            {
                for (int idx = 1; idx <= shardTotal; idx++)
                {
                    string pat = @"-0*" + idx + @"-of-0*" + shardTotal + @"\.gguf$";
                    foreach (string f in sibs)
                    {
                        string bn = BaseName(f);
                        if (bn.IndexOf(quant, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (Regex.IsMatch(bn, pat, RegexOptions.IgnoreCase)) { shards.Add(f); break; }
                    }
                }
                if (shards.Count != shardTotal)
                    err = "分片不全：应有 " + shardTotal + " 片，实得 " + shards.Count + " 片";
                shards.Sort(StringComparer.Ordinal);
                outl.AddRange(shards);
                return outl;
            }

            // 单片:-{q}.gguf / _{q}.gguf
            List<string> hit = new List<string>();
            foreach (string f in sibs)
            {
                string bn = BaseName(f);
                if (bn.EndsWith("-" + quant + ".gguf", StringComparison.OrdinalIgnoreCase) ||
                    bn.EndsWith("_" + quant + ".gguf", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(bn, quant + ".gguf", StringComparison.OrdinalIgnoreCase))
                    hit.Add(f);
            }
            if (hit.Count == 0)
                foreach (string f in sibs)
                {
                    string bn = BaseName(f);
                    if (bn.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) &&
                        bn.IndexOf(quant, StringComparison.OrdinalIgnoreCase) >= 0) hit.Add(f);
                }
            if (hit.Count > 1)
            {
                hit.Sort(delegate(string a, string b) { return BaseName(a).Length - BaseName(b).Length; });
                string pick = hit[0];
                hit.Clear();
                hit.Add(pick);
            }
            if (hit.Count == 0) err = "仓库 " + repo + " 内未找到 " + quant + " 对应文件";
            outl.AddRange(hit);
            return outl;
        }

        static string BaseName(string p) { int i = p.LastIndexOf('/'); return i < 0 ? p : p.Substring(i + 1); }

        public static string PickMmproj(string repo)
        {
            string err;
            string[] sibs = GetSiblings(repo, out err);
            string best = null;
            int bestRank = 99;
            foreach (string f in sibs)
            {
                int bi = f.LastIndexOf('/');
                string bn = bi < 0 ? f : f.Substring(bi + 1);
                if (!bn.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)) continue;
                if (!bn.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;
                int rank = 50;
                if (bn.IndexOf("BF16", StringComparison.OrdinalIgnoreCase) >= 0) rank = 0;
                else if (bn.IndexOf("F16", StringComparison.OrdinalIgnoreCase) >= 0) rank = 1;
                else if (bn.IndexOf("F32", StringComparison.OrdinalIgnoreCase) >= 0) rank = 2;
                else if (bn.IndexOf("Q8", StringComparison.OrdinalIgnoreCase) >= 0) rank = 3;
                if (rank < bestRank) { bestRank = rank; best = f; }
            }
            return best;
        }
    }
}
