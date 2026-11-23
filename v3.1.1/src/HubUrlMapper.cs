// LocModel Downloader v2.4 - HuggingFace 链接 / 仓库标识解析（P1 URL 映射层）
// 目标：把用户粘进来的任何形态（网页链接 / resolve 直链 / tree 视图 / 裸 repo id / 搜索词）
//       归一成「仓库 + 修订 + 仓库内文件路径」，供 v2.4 搜索页与下载页直接使用。
// 说明：v2.4 不改下载内核，DownloadItem.UrlPath 仍按 /resolve/main/ 拼装，
//       因此本层只负责识别与提示，不做 revision 透传。
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LocModelDL
{
    public class HubRef
    {
        public string Raw = "";
        public string Host = "";
        public string Repo = "";        // owner/name
        public string Revision = "main";
        public string FilePath = "";    // 仓库内相对路径，可空
        public string SubPath = "";     // tree 视图里的子目录
        public string Query = "";       // 非仓库形态时当搜索关键词
        public string Kind = "";        // repo / file / search / dir
        public bool IsSearch = false;
        public bool RevisionUnsupported = false;
        public string Note = "";

        public bool HasFile { get { return FilePath != null && FilePath.Length > 0; } }

        public string Describe()
        {
            if (IsSearch) return "搜索：" + Query;
            string s = Repo;
            if (HasFile) s += " / " + FilePath;
            if (SubPath.Length > 0) s += " / " + SubPath;
            return s;
        }
    }

    public static class HubUrlMapper
    {
        // HF 系站点主机（含官方短域名与国内镜像）；其余域名按「带路径的 URL」宽容处理
        static readonly string[] HostHints = new string[] {
            "huggingface.co", "hf-mirror.com", "hf.co", "hf.dev", "modelscope"
        };

        static readonly Regex RevRx = new Regex(@"^(main|master|refs/[A-Za-z0-9_\-/\.]+|[0-9a-f]{7,40}|[vV]?\d+(\.\d+)*[A-Za-z0-9\.\-\+_]*)$", RegexOptions.Compiled);

        static bool HostKnown(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string h = host.ToLowerInvariant();
            foreach (string k in HostHints) if (h.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static string StripQueryAnchor(string s)
        {
            int i = s.IndexOf('#');
            if (i >= 0) s = s.Substring(0, i);
            i = s.IndexOf('?');
            if (i >= 0) s = s.Substring(0, i);
            return s;
        }

        // 从 /models?search=xxx 这类浏览链接里捞出关键词
        static string ExtractSearchParam(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            int i = raw.IndexOf("?search=", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            string tail = raw.Substring(i + 8);
            int amp = tail.IndexOf('&');
            if (amp >= 0) tail = tail.Substring(0, amp);
            if (tail.Length == 0) return "";
            try { return Uri.UnescapeDataString(tail.Replace("+", " ")).Trim(); }
            catch { return tail.Trim(); }
        }

        static List<string> Segs(string path)
        {
            List<string> l = new List<string>();
            string[] raw = path.Split('/');
            foreach (string p in raw)
            {
                string t = p.Trim();
                if (t.Length == 0) continue;
                l.Add(t);
            }
            return l;
        }

        static string Join(List<string> segs, int from)
        {
            string s = "";
            for (int i = from; i < segs.Count; i++) s += (i > from ? "/" : "") + segs[i];
            return s;
        }

        static bool LooksLikeUrl(string s)
        {
            if (s.IndexOf("://", StringComparison.Ordinal) >= 0) return true;
            if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return true;
            int slash = s.IndexOf('/');
            if (slash <= 0) return false;
            string head = s.Substring(0, slash);
            // 主机形态：含点且不是纯 owner 名（owner 名不含点）
            return head.IndexOf('.') > 0;
        }

        // 主入口：解析任意输入
        public static HubRef Parse(string input)
        {
            HubRef r = new HubRef();
            r.Raw = input == null ? "" : input.Trim();
            string searchParam = ExtractSearchParam(r.Raw);
            string s = StripQueryAnchor(r.Raw);
            if (s.Length == 0) { r.IsSearch = true; r.Kind = "search"; r.Query = ""; return r; }

            string work = s;
            int scheme = work.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) work = work.Substring(scheme + 3);

            string host = "";
            string path = work;
            if (LooksLikeUrl(work))
            {
                int slash = work.IndexOf('/');
                host = slash < 0 ? work : work.Substring(0, slash);
                path = slash < 0 ? "" : work.Substring(slash + 1);
                r.Host = host;
                if (!HostKnown(host)) r.Note = "非 HuggingFace 域名（" + host + "），已按 HF 路径规则解析";
            }

            List<string> segs = Segs(path);
            if (segs.Count == 0)
            {
                r.IsSearch = true; r.Kind = "search"; r.Query = searchParam;
                if (host.Length > 0 && searchParam.Length == 0) r.Note = "链接只有域名，已转为空搜索（浏览热门）";
                return r;
            }

            // 兼容 /models/owner/name、/datasets/... 这类带类型前缀的链接
            int st = 0;
            if (segs[0] == "models") st = 1;
            else if (segs[0] == "datasets") { st = 1; r.Note = AppendNote(r.Note, "数据集仓库，仍按模型仓库接口解析"); }
            else if (segs[0] == "spaces") { st = 1; r.Note = AppendNote(r.Note, "Space 仓库，通常不含 GGUF"); }

            if (segs.Count - st < 2)
            {
                // 只有一段：当搜索词
                r.IsSearch = true; r.Kind = "search";
                r.Query = searchParam.Length > 0 ? searchParam : Join(segs, st);
                if (host.Length > 0) r.Note = AppendNote(r.Note, "链接路径不足 owner/name，已转为搜索");
                return r;
            }

            string owner = segs[st];
            string name = segs[st + 1];
            if (owner.Length == 0 || name.Length == 0) { r.IsSearch = true; r.Kind = "search"; r.Query = Join(segs, st); return r; }
            r.Repo = owner + "/" + name;
            r.Kind = "repo";

            int rest = st + 2;
            if (rest >= segs.Count) return r;

            string verb = segs[rest];
            if (verb == "resolve" || verb == "blob" || verb == "raw")
            {
                if (rest + 2 < segs.Count)
                {
                    r.Revision = segs[rest + 1];
                    r.FilePath = Join(segs, rest + 2);
                    r.Kind = "file";
                }
                else if (rest + 2 == segs.Count)
                {
                    r.Revision = segs[rest + 1];
                    r.Kind = "repo";
                }
                else { r.Note = AppendNote(r.Note, verb + " 链接缺少文件路径"); }
            }
            else if (verb == "tree" || verb == "commit")
            {
                if (rest + 1 < segs.Count)
                {
                    r.Revision = segs[rest + 1];
                    r.SubPath = Join(segs, rest + 2);
                    r.Kind = r.SubPath.Length > 0 ? "dir" : "repo";
                }
            }
            else if (RevRx.IsMatch(verb) && (rest + 1 < segs.Count))
            {
                // 裸形态：/{owner}/{repo}/{revision}/{path}
                r.Revision = verb;
                r.FilePath = Join(segs, rest + 1);
                r.Kind = "file";
            }
            else
            {
                // 其余当仓库内子目录
                r.SubPath = Join(segs, rest);
                r.Kind = "dir";
            }

            if (r.Revision == null || r.Revision.Length == 0) r.Revision = "main";
            if (!string.Equals(r.Revision, "main", StringComparison.OrdinalIgnoreCase))
            {
                r.RevisionUnsupported = true;
                r.Note = AppendNote(r.Note, "该链接指向非 main 分支（" + r.Revision + "），v2.4 暂按 main 处理");
            }
            return r;
        }

        static string AppendNote(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            return a + "；" + b;
        }

        // 由仓库 + 文件 + （可选）修订 反推标准 HF 链接，便于界面展示与复制
        // v3.1.1：未指定站点时用「下载站点」默认站点，不再硬编码 huggingface.co
        public static string ToResolveUrl(string repo, string filePath, string siteBase)
        {
            string b = string.IsNullOrEmpty(siteBase) ? SiteStore.DefaultSite : siteBase.TrimEnd('/');
            return b + "/" + repo + "/resolve/main/" + (filePath == null ? "" : filePath);
        }

        public static string ToRepoUrl(string repo, string siteBase)
        {
            string b = string.IsNullOrEmpty(siteBase) ? SiteStore.DefaultSite : siteBase.TrimEnd('/');
            return b + "/" + repo;
        }

        // 校验 repo id 形态（owner/name），用于搜索框直接输入时的兜底判断
        public static bool IsRepoId(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.IndexOf(' ') >= 0) return false;
            string[] p = s.Split('/');
            if (p.Length != 2) return false;
            if (p[0].Length < 1 || p[1].Length < 1) return false;
            return Regex.IsMatch(s, @"^[A-Za-z0-9][A-Za-z0-9_\-\.]{0,95}/[A-Za-z0-9][A-Za-z0-9_\-\.]{0,95}$");
        }
    }
}
