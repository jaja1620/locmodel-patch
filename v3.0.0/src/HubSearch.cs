// LocModel Downloader v2.4 - HuggingFace 在线搜索（P2 搜索层）
// 等价 LocModel 的 search_hf 命令：api/models?search={q}&filter=gguf&sort=downloads&direction=-1&limit=N
// 站点来源沿用 CatalogService 的 HF 解析链（界面里填的站点在前，内置镜像兜底），失败自动降级。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;

namespace LocModelDL
{
    public class HubModel
    {
        public string Repo = "";
        public string Author = "";
        public long Downloads;
        public long Likes;
        public string PipelineTag = "";
        public string Updated = "";
        public List<string> Tags = new List<string>();
        public bool HasGguf = false;

        public string Line()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Repo);
            sb.Append("  ↓").Append(FmtCount(Downloads));
            sb.Append("  ♥").Append(FmtCount(Likes));
            if (Updated.Length >= 10) sb.Append("  ").Append(Updated.Substring(0, 10));
            return sb.ToString();
        }

        static string FmtCount(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.0") + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0.0") + "k";
            return n.ToString();
        }
    }

    public static class HubSearch
    {
        const int TimeoutMs = 20000;
        static readonly object CacheLock = new object();
        static readonly Dictionary<string, List<HubModel>> Cache = new Dictionary<string, List<HubModel>>();
        static string LastGoodBase = "";

        public static string BuildSearchUrl(string baseUrl, string query, int limit)
        {
            if (limit <= 0) limit = 40;
            if (limit > 100) limit = 100;
            string q = query == null ? "" : query.Trim();
            return baseUrl.TrimEnd('/') + "/api/models?search=" + Uri.EscapeDataString(q)
                + "&filter=gguf&sort=downloads&direction=-1&limit=" + limit;
        }

        public static List<string> Bases()
        {
            List<string> l = new List<string>();
            try
            {
                string[] arr = CatalogService.HfBasesSnapshot();
                if (arr != null) foreach (string b in arr) if (!string.IsNullOrEmpty(b)) l.Add(b.TrimEnd('/'));
            }
            catch { }
            if (l.Count == 0) l.Add("https://hf-mirror.com");
            // 上次成功的站点提到最前面
            if (LastGoodBase.Length > 0)
            {
                int i = l.IndexOf(LastGoodBase);
                if (i > 0) { l.RemoveAt(i); l.Insert(0, LastGoodBase); }
            }
            return l;
        }

        // 搜索 GGUF 仓库；query 可为空（空则按下载量浏览热门）
        public static List<HubModel> Search(string query, int limit, out string log)
        {
            StringBuilder sb = new StringBuilder();
            string key = (query == null ? "" : query.Trim()).ToLowerInvariant() + "|" + limit;
            lock (CacheLock)
            {
                List<HubModel> c;
                if (Cache.TryGetValue(key, out c)) { log = "缓存命中：" + key; return new List<HubModel>(c); }
            }

            List<string> bases = Bases();
            foreach (string b in bases)
            {
                string url = BuildSearchUrl(b, query, limit);
                sb.AppendLine("搜索: " + url);
                try
                {
                    string body = CatalogService.HttpGetText(url, TimeoutMs);
                    List<HubModel> list = Parse(body);
                    if (list != null)
                    {
                        LastGoodBase = b;
                        lock (CacheLock) { Cache[key] = list; }
                        sb.AppendLine("命中 " + list.Count + " 个仓库（来源 " + b + "）");
                        log = sb.ToString();
                        return new List<HubModel>(list);
                    }
                }
                catch (Exception ex) { sb.AppendLine("失败(" + b + "): " + Short(ex.Message)); }
            }
            log = sb.ToString();
            return new List<HubModel>();
        }

        // 解析 api/models 返回的数组
        public static List<HubModel> Parse(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            string t = body.TrimStart();
            if (t.Length == 0) return null;
            if (t[0] != '[')
            {
                // 错误体：{"error":"..."}
                return null;
            }
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            object root = js.DeserializeObject(body);
            object[] arr = root as object[];
            if (arr == null) return null;

            List<HubModel> list = new List<HubModel>();
            foreach (object o in arr)
            {
                Dictionary<string, object> m = o as Dictionary<string, object>;
                if (m == null) continue;
                HubModel h = new HubModel();
                h.Repo = S(m, "id");
                if (h.Repo.Length == 0) h.Repo = S(m, "modelId");
                if (h.Repo.Length == 0) continue;
                h.Author = S(m, "author");
                if (h.Author.Length == 0)
                {
                    int i = h.Repo.IndexOf('/');
                    if (i > 0) h.Author = h.Repo.Substring(0, i);
                }
                h.Downloads = L(m, "downloads");
                h.Likes = L(m, "likes");
                h.PipelineTag = S(m, "pipeline_tag");
                h.Updated = S(m, "lastModified");
                if (h.Updated.Length == 0) h.Updated = S(m, "createdAt");
                object tags;
                if (m.TryGetValue("tags", out tags) && tags is object[])
                {
                    foreach (object tg in (object[])tags)
                    {
                        if (tg == null) continue;
                        string ts = tg.ToString();
                        h.Tags.Add(ts);
                        if (ts.IndexOf("gguf", StringComparison.OrdinalIgnoreCase) >= 0) h.HasGguf = true;
                    }
                }
                if (h.Repo.IndexOf("gguf", StringComparison.OrdinalIgnoreCase) >= 0) h.HasGguf = true;
                list.Add(h);
            }
            return list;
        }

        static string S(Dictionary<string, object> m, string k)
        {
            object v; if (m.TryGetValue(k, out v) && v != null) return v.ToString();
            return "";
        }

        static long L(Dictionary<string, object> m, string k)
        {
            object v; if (!m.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToInt64(v); } catch { return 0; }
        }

        static string Short(string s) { if (s == null) return ""; return s.Length > 100 ? s.Substring(0, 100) : s; }

        public static void ClearCache()
        {
            lock (CacheLock) { Cache.Clear(); }
        }
    }
}
