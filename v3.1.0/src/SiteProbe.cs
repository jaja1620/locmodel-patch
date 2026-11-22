// LocModel Downloader v2.2 - 自定义下载站点：解析 / 持久化 / 测速择优
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace LocModelDL
{
    // 单个站点的测速结果
    public class SiteSpeed
    {
        public string Base;     // 归一化站点，无尾斜杠
        public bool Ok;
        public long Bytes;      // 探测实际读到的字节
        public long Ms;         // 耗时（毫秒）
        public double Bps;      // 字节/秒
        public string Note = ""; // 失败原因或补充说明
        public int Order;       // 用户输入顺序

        public string Human()
        {
            if (!Ok) return Note.Length > 0 ? ("不可用：" + Note) : "不可用";
            if (Ms <= 0) return "0 ms";
            double kb = Bps / 1024.0;
            string sp = kb >= 1024 ? (kb / 1024.0).ToString("0.00") + " MB/s" : kb.ToString("0") + " KB/s";
            string tail = Note.Length > 0 ? "（" + Note + "）" : "";
            return sp + " / " + Ms + " ms" + tail;
        }
    }

    // 站点输入解析与配置持久化
    public static class SiteStore
    {
        public const string DefaultSite = "https://hf-mirror.com";

        public static string CfgPath
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;
                return Path.Combine(dir, "LocModel下载器_站点配置.txt");
            }
        }

        // 把多行/逗号/分号/空白分隔的文本拆成原始站点串
        public static List<string> Parse(string text)
        {
            List<string> raw = new List<string>();
            if (text == null) return raw;
            string t = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = t.Split('\n');
            foreach (string line in lines)
            {
                string s = line;
                int hash = s.IndexOf('#');
                if (hash >= 0) s = s.Substring(0, hash);          // 支持行内注释
                s = s.Replace('，', ',').Replace('；', ';').Replace('、', ',');
                string[] parts = s.Split(new char[] { ',', ';', '\t', ' ', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string p in parts) raw.Add(p);
            }
            return raw;
        }

        // 归一化：补协议、去尾斜杠、校验、去重（保留首次出现）
        public static List<string> Normalize(IEnumerable<string> raw)
        {
            List<string> outp = new List<string>();
            if (raw == null) return outp;
            List<string> seen = new List<string>();
            foreach (string r0 in raw)
            {
                if (r0 == null) continue;
                string r = r0.Trim();
                if (r.Length == 0) continue;
                if (r.StartsWith("//")) continue;                  // 纯注释行
                if (r.IndexOf("://", StringComparison.Ordinal) < 0) r = "https://" + r;
                while (r.EndsWith("/")) r = r.Substring(0, r.Length - 1);
                Uri u;
                if (!Uri.TryCreate(r, UriKind.Absolute, out u)) continue;
                if (u.Scheme != "http" && u.Scheme != "https") continue;
                if (string.IsNullOrEmpty(u.Host)) continue;
                string key = (u.Scheme + "://" + u.Host + ":" + u.Port + u.AbsolutePath).ToLowerInvariant();
                if (seen.Contains(key)) continue;
                seen.Add(key);
                // 协议名统一小写（HTTPS:// 与 https:// 视为同一站点），其余部分保持用户输入原样
                string norm = u.Scheme + r.Substring(r.IndexOf("://", StringComparison.Ordinal));
                outp.Add(norm);
            }
            return outp;
        }

        // 是否 LocModel 自有 CDN（不代理 HuggingFace 路径，不能当 HF 镜像用）
        public static bool IsLocModelCdn(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return false;
            return baseUrl.IndexOf("locmodel.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 读取配置；缺失或无效时回退默认站点
        public static string Load()
        {
            try
            {
                string p = CfgPath;
                if (File.Exists(p))
                {
                    string txt = File.ReadAllText(p, Encoding.UTF8);
                    List<string> ok = Normalize(Parse(txt));
                    if (ok.Count > 0) return string.Join("\r\n", ok.ToArray());
                }
            }
            catch { }
            return DefaultSite;
        }

        // 保存配置（只写归一化后的站点行）；失败静默（如 exe 位于只读目录）
        public static string Save(string text)
        {
            List<string> ok = Normalize(Parse(text));
            string body = ok.Count > 0 ? string.Join("\r\n", ok.ToArray()) : DefaultSite;
            try { File.WriteAllText(CfgPath, body + "\r\n", new UTF8Encoding(false)); } catch { }
            return body;
        }
    }

    // 多站点并发测速 + 择优排序
    public static class SiteProbe
    {
        public const int ProbeBytes = 256 * 1024;   // 每站点探测 256KB
        public const int ProbeTimeoutMs = 8000;

        // 测速前必须支持 TLS 1.2，否则 https 站点会报"未能创建 SSL/TLS 安全通道"
        static SiteProbe()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch { }
        }
        // 未选模型时"测速预览"用的公开探测目标（HF 路径形式，与真实下载同构）
        public const string PreviewProbePath = "unsloth/Qwen3.5-4B-GGUF/resolve/main/imatrix_unsloth.gguf_file";

        // 返回按"可用性 + 速度"排序后的列表；失败站点排在最后并保留原有顺序
        public static List<SiteSpeed> Rank(IList<string> bases, string probePath, int parallel, Action<string> log)
        {
            List<SiteSpeed> res = new List<SiteSpeed>();
            if (bases == null || bases.Count == 0) return res;
            int i = 0;
            foreach (string b in bases)
            {
                SiteSpeed s = new SiteSpeed();
                s.Base = b;
                s.Order = i++;
                res.Add(s);
            }

            string rel = (probePath == null ? "" : probePath).Trim().TrimStart('/');
            if (rel.Length == 0)
            {
                foreach (SiteSpeed s in res) { s.Ok = false; s.Note = "缺少探测文件"; }
                return res;
            }

            int par = parallel <= 0 ? 3 : parallel;
            if (par > 6) par = 6;
            if (par > res.Count) par = res.Count;
            try
            {
                if (ServicePointManager.DefaultConnectionLimit < par + 2)
                    ServicePointManager.DefaultConnectionLimit = par + 2;
            }
            catch { }

            int cursor = -1;
            object gate = new object();
            List<Thread> ts = new List<Thread>();
            for (int w = 0; w < par; w++)
            {
                Thread t = new Thread(delegate()
                {
                    while (true)
                    {
                        int k;
                        lock (gate) { cursor++; k = cursor; }
                        if (k >= res.Count) break;
                        Measure(res[k], rel);
                        if (log != null) log("[测速] " + res[k].Base + "  " + res[k].Human());
                    }
                });
                t.IsBackground = true;
                t.Start();
                ts.Add(t);
            }
            foreach (Thread t in ts) { try { t.Join(); } catch { } }

            res.Sort(delegate(SiteSpeed a, SiteSpeed b)
            {
                int ra = RankKey(a), rb = RankKey(b);
                if (ra != rb) return ra.CompareTo(rb);
                if (ra == 0 && a.Bps != b.Bps) return b.Bps > a.Bps ? 1 : -1;
                return a.Order.CompareTo(b.Order);
            });
            return res;
        }

        static int RankKey(SiteSpeed s)
        {
            if (s.Ok && s.Bytes > 0) return 0;   // 可用，按速度
            if (s.Ok) return 1;                   // 有响应但没读到数据
            return 2;                             // 不可用
        }

        // 单站点测速：Range 读 256KB，按实际读到字节 / 耗时算速度
        static void Measure(SiteSpeed s, string rel)
        {
            string url = s.Base.TrimEnd('/') + "/" + rel;
            long t0 = Environment.TickCount;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = ProbeTimeoutMs;
                req.ReadWriteTimeout = ProbeTimeoutMs;
                req.AllowAutoRedirect = true;
                req.MaximumAutomaticRedirections = 4;
                req.UserAgent = "LocModelDownloader/2.2";
                req.KeepAlive = false;
                req.AddRange(0, ProbeBytes - 1);

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                {
                    int code = (int)resp.StatusCode;
                    bool rangeOk = resp.StatusCode == HttpStatusCode.PartialContent;
                    byte[] buf = new byte[32 * 1024];
                    long read = 0;
                    while (read < ProbeBytes)
                    {
                        int n = st.Read(buf, 0, (int)Math.Min(buf.Length, ProbeBytes - read));
                        if (n <= 0) break;
                        read += n;
                    }
                    long ms = Environment.TickCount - t0;
                    if (ms < 1) ms = 1;
                    s.Bytes = read;
                    s.Ms = ms;
                    s.Bps = read * 1000.0 / ms;
                    s.Ok = (code == 200 || code == 206) && read > 0;
                    if (!s.Ok) s.Note = "HTTP " + code + " 无数据";
                    else if (!rangeOk) s.Note = "不支持 Range，按整包测速";
                }
            }
            catch (WebException we)
            {
                long ms = Environment.TickCount - t0;
                if (ms < 1) ms = 1;
                s.Ms = ms; s.Ok = false;
                HttpWebResponse hr = we.Response as HttpWebResponse;
                if (hr != null) s.Note = "HTTP " + (int)hr.StatusCode;
                else if (we.Status == WebExceptionStatus.Timeout) s.Note = "超时";
                else s.Note = Short(we.Message);
            }
            catch (Exception ex)
            {
                s.Ok = false;
                s.Note = Short(ex.Message);
            }
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "连接失败";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }
    }
}
