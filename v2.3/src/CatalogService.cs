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
            }

            // v2.3：识别本机 LocModel 版本（仅用于提示，实际生效版本以界面选择为准）
            info.Version = LocModelProfile.Detect(info.ExePath, info.HasAccelLines, out info.VersionDetail);
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
            }
            catch { }
        }
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

        // 优化8：HF 解析链与自有 CDN 链分离。m1/m2 实测对 HF 路径直接 404，不再参与 HF 解析；
        // huggingface.co 本机常无响应，放最后。成功线路在运行时被提升到首位（健康记忆）。
        public static string[] HfBases = new string[] {
            "https://hf-mirror.com",
            "https://huggingface.co"
        };

        static readonly object HfBaseLock = new object();

        // v2.2：把界面里用户填写的站点并入 HF 解析链（排在内置线路之前，失败自动降级到内置）
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
            lock (HfBaseLock)
            {
                List<string> arr = new List<string>();
                foreach (string v in head) arr.Add(v);
                foreach (string v in HfBases) if (!arr.Contains(v)) arr.Add(v);
                foreach (string v in new string[] { "https://hf-mirror.com", "https://huggingface.co" })
                    if (!arr.Contains(v)) arr.Add(v);
                HfBases = arr.ToArray();
            }
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

        // ---------- 语言/推理模型：服务端 catalog.json（优先），失效降级读取程序内嵌模型库 ----------
        public static List<ModelEntry> LoadTextCatalog(string exePath, out string log)
        {
            StringBuilder sb = new StringBuilder();
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
                sb.AppendLine("服务端 catalog 无有效条目，降级读取程序内嵌模型库");
            }
            else sb.AppendLine("服务端 catalog 全部镜像不可用，降级读取程序内嵌模型库");

            string l2;
            List<ModelEntry> emb = LoadTextCatalogFromExe(exePath, out l2);
            sb.AppendLine(l2);
            log = sb.ToString();
            return emb;
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

            if (!profile.IsV33) { SyncFromExeV31(imageModels, text, out log); return; }

            // ---- 3.3：以「主仓库」为锚点解析 exe 内嵌清单（量化档/年份取 exe），内置快照兜底 ----
            List<ExeImageEntry> parsed = new List<ExeImageEntry>();
            string perr = "";
            try { parsed = ParseExeImageEntries(text); }
            catch (Exception ex) { perr = Short(ex.Message); }

            if (parsed.Count < 6)
            {
                log = "LocModel 3.3 exe 清单解析不足（解析到 " + parsed.Count + " 条"
                    + (perr.Length > 0 ? "：" + perr : "") + "），使用内置快照兜底（" + imageModels.Count + " 个模型）";
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("LocModel 3.3 图像/视频清单（exe 解析 " + parsed.Count + " 条）");
            foreach (ExeImageEntry e in parsed)
                sb.Append("\n  · " + e.Slug + " ← " + e.Repo + "（量化 " + e.Quants.Count + " 档，依赖 " + e.Common.Count + "）");

            int refreshed = 0, added = 0, dropped = 0;
            for (int i = imageModels.Count - 1; i >= 0; i--)
            {
                ModelEntry m = imageModels[i];
                List<ExeImageEntry> hit = new List<ExeImageEntry>();
                foreach (ExeImageEntry e in parsed) if (SameRepo(m.Repo, e.Repo)) hit.Add(e);

                if (hit.Count == 0)
                {
                    // 3.3 exe 里连仓库名都搜不到 → 该模型已被下架；只是解析没识别到仓库的（如 h3 / h3-ref 这类
                    // 主仓库与共享依赖同名的条目）一律保留，避免解析器局限打掉可用清单
                    if (!string.IsNullOrEmpty(m.Repo) && text.IndexOf(m.Repo, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        sb.Append("\n  - 移除 " + m.Id + "（3.3 exe 内已无仓库 " + m.Repo + "）");
                        imageModels.RemoveAt(i);
                        dropped++;
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
                // 量化档变化才刷新；文件名模板始终留给下载期按仓库文件清单解析，不从粘连串里猜
                int sameRepoCount = 0;
                foreach (ModelEntry mm in imageModels) if (SameRepo(mm.Repo, m.Repo)) sameRepoCount++;
                if (sameRepoCount == 1 && e0.Quants.Count > 0 && e0.Quants.Count != m.Quants.Count && string.IsNullOrEmpty(m.MainFile2Template))
                {
                    m.Quants.Clear();
                    m.Quants.AddRange(e0.Quants);
                    ch = true;
                }
                if (ch) refreshed++;
            }

            // exe 里有、内置没有的模型 → 新增条目；模板用 {q}（下载期按仓库文件清单解析真实文件名）
            foreach (ExeImageEntry e in parsed)
            {
                bool exists = false;
                foreach (ModelEntry m in imageModels) if (SameRepo(m.Repo, e.Repo)) { exists = true; break; }
                if (exists || string.IsNullOrEmpty(e.Slug)) continue;

                ModelEntry nm = new ModelEntry();
                nm.Id = e.Slug;
                nm.Name = e.Slug;
                nm.Release = e.Release;
                nm.Desc = "来自 LocModel 3.3 内置下载清单（文件名在下载时按仓库清单解析）";
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

            sb.Append("\n  新增 " + added + " · 刷新 " + refreshed + " · 移除 " + dropped);
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
            return name.ToLowerInvariant().Replace('_', '-');
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
                    e.MainFileTemplate = "{q}";
                    list.Add(e);
                }
                sb.AppendLine("语言/推理模型: " + list.Count + " 个");
            }
            catch (Exception ex) { sb.AppendLine("catalog 解析失败: " + Short(ex.Message)); }
            return list;
        }

        static string S(Dictionary<string, object> m, string k)
        {
            object v; if (m.TryGetValue(k, out v) && v != null) return v.ToString();
            return "";
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

        public static string HttpGetText(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "LocModel/0.1";
            req.Accept = "application/json,text/plain,*/*";
            req.AllowAutoRedirect = true;
            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // ---------- HF 仓库文件清单（等价 LocModel repo_quants） ----------
        static readonly Dictionary<string, string[]> SibCache = new Dictionary<string, string[]>();

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
            lock (SizeCache) SizeCache[repo] = map;
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
