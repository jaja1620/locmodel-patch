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
        public static readonly string[] CatalogBases = new string[] {
            "https://m1.locmodel.com",
            "https://m2.locmodel.com",
            "https://activation.locmodel.com",
            "https://get.locmodel.com"
        };

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
            foreach (string b in CatalogBases)
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

        // ---------- 图像/视频模型：从 LocModel.exe 自动同步仓库与文件模板 ----------
        public static void SyncImageCatalogFromExe(List<ModelEntry> imageModels, string exePath, out string log)
        {
            log = "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) { log = "未找到 LocModel.exe，使用内置快照"; return; }
            byte[] data;
            try { data = File.ReadAllBytes(exePath); }
            catch (Exception ex) { log = "读取 exe 失败: " + Short(ex.Message); return; }

            string text = Encoding.UTF8.GetString(data);
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
