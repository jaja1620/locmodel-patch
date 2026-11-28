// LocModel Downloader v3.0.0 - Range 多连接分片下载内核
// 设计要点（与 v2.4.x 单连接内核并存，互不破坏）：
//   1) 先用 HEAD 取远端总长与 ETag/Last-Modified，再用 Range: bytes=0-0 探测 206，探测不过直接回退单连接；
//   2) 半成品仍是同一个 <part> 文件（预分配为远端总长），每条连接只往自己的区间里 seek 写，
//      因此校验、改名、整包 SHA-256 全部沿用 v2.4.x 的既成流程；
//   3) 每个分片的完成进度写进 <part>.seg，中断后可精确续传（不会出现"长度到了但中间有洞"被误判完成）；
//   4) 任何一条连接连续失败超过上限 → 整体判 Failed 并保留进度，由上层换线路重试；上层若要回退单连接，
//      必须先删掉分片半成品（DownloadEngine 里已做），否则长度=总长会被单连接内核误判为已完成。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace LocModelDL
{
    public enum SegResult
    {
        Done,          // 完整下载到 <part>
        Unsupported,   // 不可用分片（长度未知 / 不支持 Range / 文件太小），应回退单连接
        Failed         // 分片尝试失败（进度已保存，可续传或换线路）
    }

    // 分片进度记录：<part>.seg
    class SegState
    {
        public long Size = -1;
        public string Etag = "";
        public string LastMod = "";
        public string Mirror = "";
        public long[] Done = new long[0];

        public static SegState Load(string part)
        {
            SegState st = null;
            try
            {
                string p = part + ".seg";
                if (!File.Exists(p)) return null;
                st = new SegState();
                List<long> done = new List<long>();
                foreach (string line0 in File.ReadAllLines(p))
                {
                    string line = line0.TrimStart('\uFEFF').Trim();
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    if (k == "size") { long s; if (long.TryParse(v, out s)) st.Size = s; }
                    else if (k == "etag") st.Etag = v;
                    else if (k == "lastmod") st.LastMod = v;
                    else if (k == "mirror") st.Mirror = v;
                    else if (k.StartsWith("seg"))
                    {
                        int idx;
                        if (!int.TryParse(k.Substring(3), out idx)) continue;
                        long d;
                        if (!long.TryParse(v, out d)) d = 0;
                        while (done.Count <= idx) done.Add(0);
                        done[idx] = d < 0 ? 0 : d;
                    }
                }
                st.Done = done.ToArray();
                if (st.Size <= 0) return null;
                return st;
            }
            catch { return null; }
        }

        public long TotalDone()
        {
            long s = 0;
            for (int i = 0; i < Done.Length; i++) s += Done[i];
            return s;
        }

        public void Save(string part)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("size=" + Size);
                sb.AppendLine("etag=" + Etag);
                sb.AppendLine("lastmod=" + LastMod);
                sb.AppendLine("mirror=" + Mirror);
                for (int i = 0; i < Done.Length; i++) sb.AppendLine("seg" + i + "=" + Done[i]);
                File.WriteAllText(part + ".seg", sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    public class SegmentedFetcher
    {
        public event Action<string> Log;

        const int SegRetry = 3;              // 单条分片的最大重试次数
        const int SegTimeoutMs = 20000;      // 连接/请求超时
        const int SegStallMs = 30000;        // 读停超时（分片并发时给宽一点，避免误杀）
        const int BufSize = 1 << 20;

        void L(string s) { Action<string> h = Log; if (h != null) h(s); }

        // 与主下载器一致：显式开启 TLS1.2/1.1，避免 .NET 默认协议握手失败
        static SegmentedFetcher()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                ServicePointManager.DefaultConnectionLimit = 64;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
        }

        // 已有分片半成品的真实完成字节（供磁盘预检使用）
        public static long ProgressOf(string part)
        {
            SegState st = SegState.Load(part);
            return st == null ? 0 : st.TotalDone();
        }

        public static bool IsSegmentedPart(string part)
        {
            try { return File.Exists(part + ".seg"); } catch { return false; }
        }

        // ---------- 目标盘写入能力探测：并发分片在多偏移随机写时会放大寻道开销 ----------
        // 慢盘（外置 USB 机械盘等）顺序写上限本身就低于线路带宽，此时并发不但无收益，还会因
        // 多流随机写互相打断而更慢。按实测写入速度给并发数设上限，探测结果按盘符（卷）缓存。
        static readonly object DiskLock = new object();
        static readonly Dictionary<string, int> DiskCap = new Dictionary<string, int>();
        static readonly Dictionary<string, double> DiskMbps = new Dictionary<string, double>();

        public static int ConnectionsFor(string dir, out double mbps)
        {
            string key = VolumeKeyOf(dir);
            lock (DiskLock)
            {
                int cap;
                if (DiskCap.TryGetValue(key, out cap))
                {
                    DiskMbps.TryGetValue(key, out mbps);
                    if (mbps <= 0) mbps = -1;
                    return cap;
                }
                mbps = ProbeWriteMBps(dir);
                if (mbps < 60) cap = 1;              // 慢盘（外置 USB 机械盘 ~30MB/s）：并发无收益，退回单连接顺序写
                else if (mbps < 120) cap = 2;
                else if (mbps < 300) cap = 4;
                else cap = 16;
                DiskCap[key] = cap;
                DiskMbps[key] = mbps;
                return cap;
            }
        }

        static string VolumeKeyOf(string dir)
        {
            try
            {
                string full = Path.GetFullPath(dir);
                string root = Path.GetPathRoot(full);
                return string.IsNullOrEmpty(root) ? full : root.ToUpperInvariant();
            }
            catch { return dir == null ? "" : dir; }
        }

        // 顺序写 128MB 实测落盘速度（MB/s）；失败返回 -1。
        // 样本必须够大：外置 USB 盘的前 32MB 会被首次分配开销拖到 5MB/s 级别，误判为极慢盘。
        public static double ProbeWriteMBps(string dir)
        {
            string d = dir;
            try
            {
                while (!string.IsNullOrEmpty(d) && !Directory.Exists(d))
                {
                    DirectoryInfo up = Directory.GetParent(d);
                    if (up == null) break;
                    d = up.FullName;
                }
            }
            catch { }
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) return -1;
            string probe = Path.Combine(d, ".lm_probe_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            long bytes = 0;
            DateTime t0 = DateTime.UtcNow;
            try
            {
                byte[] buf = new byte[1 << 20];
                using (FileStream fs = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    for (int i = 0; i < 128; i++) { fs.Write(buf, 0, buf.Length); bytes += buf.Length; }
                }
            }
            catch { bytes = 0; }
            finally { try { if (File.Exists(probe)) File.Delete(probe); } catch { } }
            if (bytes <= 0) return -1;
            double sec = (DateTime.UtcNow - t0).TotalSeconds;
            return sec <= 0 ? -1 : bytes / 1048576.0 / sec;
        }

        // 分片区间划分（纯函数，供自检用）：首尾闭合、无重叠、无空洞，最后一片兜住余数
        public static void Plan(long total, int n, out long[] starts, out long[] lens)
        {
            if (n < 1) n = 1;
            starts = new long[n];
            lens = new long[n];
            for (int i = 0; i < n; i++)
            {
                starts[i] = total * i / n;
                long end = (i == n - 1) ? total : total * (i + 1) / n;
                lens[i] = end - starts[i];
            }
        }

        // 主入口：把 url 指向的远端文件并发分片下载到 part
        public SegResult Run(string url, string part, long expected, int wantConns, long minSeg,
                             Action<long, double> report, Func<bool> cancelled)
        {
            if (wantConns < 2) return SegResult.Unsupported;

            // 目标盘慢于线路时，多流随机写反而更慢：按实测写入速度压并发
            if (minSeg < 1024 * 1024) minSeg = 1024 * 1024;
            if (cancelled == null) cancelled = delegate { return false; };

            long total = -1;
            string etag = "", lastmod = "";
            // v3.0.0：手动跟随 302/307 拿到真实下载地址。.NET 的自动跳转会丢掉 Range 头，
            // 导致“重定向层不认 Range”的误判（hf-mirror 的 /resolve/ 就属于这类）。
            bool hardUnsupported;
            string finalUrl = Resolve(url, out total, out etag, out lastmod, out hardUnsupported);
            if (hardUnsupported) return SegResult.Unsupported;
            if (finalUrl == null || total <= 0)
            {
                if (!DownloadEngine.HeadValidators(url, out total, out etag, out lastmod) || total <= 0)
                {
                    L("  分片：未取到远端总长（Range 探测与 HEAD 均失败），改用单连接");
                    return SegResult.Unsupported;
                }
                finalUrl = url;
            }
            if (expected > 0 && expected != total)
            {
                L("  分片：远端长度(" + total + "B) 与期望(" + expected + "B) 不符，改为单连接走换版判定");
                return SegResult.Unsupported;
            }
            if (total < minSeg * 2)
            {
                L("  分片：文件仅 " + Fmt(total) + "，小于两片阈值，改用单连接");
                return SegResult.Unsupported;
            }

            int n = (int)(total / minSeg);
            if (n > wantConns) n = wantConns;
            if (n < 2) return SegResult.Unsupported;

            double diskMBps;
            int diskCap = ConnectionsFor(Path.GetDirectoryName(Path.GetFullPath(part)), out diskMBps);
            if (n > diskCap) n = diskCap;
            if (n < 2)
            {
                L("  分片：目标盘实测写入 " + (diskMBps > 0 ? diskMBps.ToString("0") : "?") + " MB/s，低于线路带宽，改用单连接顺序写");
                return SegResult.Unsupported;
            }
            if (diskCap < 16) L("  分片：目标盘写入约 " + diskMBps.ToString("0") + " MB/s，并发上限压到 " + diskCap);

            // ---------- 进度状态：能续则续，不能续则重建 ----------
            SegState st = SegState.Load(part);
            bool reuse = st != null && st.Size == total
                          && SameTag(st.Etag, etag) && SameTag(st.LastMod, lastmod)
                          && File.Exists(part);
            if (!reuse)
            {
                long legacy = 0;
                try { if (File.Exists(part)) legacy = new FileInfo(part).Length; } catch { }
                if (legacy >= total) legacy = 0;   // 长度已满但无分片记录 → 不可信，重下
                KillFiles(part);

                st = new SegState();
                st.Size = total;
                st.Etag = etag;
                st.LastMod = lastmod;
                st.Mirror = MirrorOf(url);
                st.Done = new long[n];
                if (legacy > 0)
                {
                    st.Done[0] = legacy;
                    L("  分片：沿用原单连接半成品的连续前缀 " + Fmt(legacy));
                }
                try
                {
                    using (FileStream fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        fs.SetLength(total);    // 预分配，各连接只写自己的区间
                }
                catch (Exception ex)
                {
                    L("  分片：预分配半成品失败(" + ex.GetType().Name + ")，改用单连接");
                    KillFiles(part);
                    return SegResult.Unsupported;
                }
                st.Save(part);
                L(string.Format("  分片：新建 {0} 片，总长 {1}，每片约 {2}", n, Fmt(total), Fmt(total / n)));
            }
            else
            {
                n = st.Done.Length;             // 沿用上次的分片布局
                if (n < 2)
                {
                    KillFiles(part);
                    return SegResult.Unsupported;
                }
                L(string.Format("  分片：续传，已完成 {0} / {1}（{2:0.0}%），{3} 片",
                    Fmt(st.TotalDone()), Fmt(total), st.TotalDone() * 100.0 / total, n));
            }

            // ---------- 区间划分 ----------
            long[] starts, lens;
            Plan(total, n, out starts, out lens);

            object lk = new object();
            bool[] failed = new bool[n];
            double[] speedBox = new double[1];
            DateTime lastTick = DateTime.UtcNow;
            long lastDone = st.TotalDone();

            Thread[] ts = new Thread[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                Thread t = new Thread(delegate()
                {
                    Worker(idx, starts[idx], lens[idx], finalUrl, part, st, lk, failed, cancelled, report,
                           speedBox, delegate() { return lastTick; }, delegate(DateTime d) { lastTick = d; },
                           delegate() { return lastDone; }, delegate(long v) { lastDone = v; });
                });
                t.IsBackground = true;
                ts[i] = t;
                t.Start();
            }
            foreach (Thread t in ts) { try { t.Join(); } catch { } }

            st.Save(part);

            if (cancelled())
            {
                L("  分片：已取消，进度已保存");
                return SegResult.Failed;
            }

            bool anyFail = false;
            for (int i = 0; i < n; i++) if (failed[i]) anyFail = true;

            if (anyFail)
            {
                L("  分片：存在失败的分片，进度已保存（" + Fmt(st.TotalDone()) + " / " + Fmt(total) + "）");
                return SegResult.Failed;
            }

            long fin = 0;
            try { fin = new FileInfo(part).Length; } catch { }
            if (fin != total)
            {
                L("  分片：落盘长度(" + fin + "B) 与远端总长(" + total + "B) 不符");
                return SegResult.Failed;
            }
            L("  分片：全部完成 " + Fmt(total) + "（" + n + " 片）");
            if (report != null) { try { report(total, 0); } catch { } }
            return SegResult.Done;
        }

        void Worker(int idx, long start, long len, string url, string part, SegState st, object lk,
                    bool[] failed, Func<bool> cancelled, Action<long, double> report,
                    double[] speedBox, Func<DateTime> getTick, Action<DateTime> setTick,
                    Func<long> getLastDone, Action<long> setLastDone)
        {
            long segEnd = start + len;
            int tries = 0;
            DateTime lastSave = DateTime.UtcNow;
            FileStream fs = null;
            try
            {
                fs = new FileStream(part, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1 << 16);
                while (true)
                {
                    if (cancelled()) return;
                    long doneSeg;
                    lock (lk) doneSeg = st.Done[idx];
                    long from = start + doneSeg;
                    if (from >= segEnd) return;

                    HttpWebResponse resp = null;
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                        req.UserAgent = "LocModelDL/3.0.0";
                        req.Timeout = SegTimeoutMs;
                        req.ReadWriteTimeout = SegStallMs;
                        req.AllowAutoRedirect = true;
                        req.KeepAlive = true;
                        req.AddRange(from, segEnd - 1);

                        resp = (HttpWebResponse)req.GetResponse();
                        if (resp.StatusCode != HttpStatusCode.PartialContent)
                        {
                            L("  分片" + (idx + 1) + "：服务器返回 " + (int)resp.StatusCode + "（非 206），放弃分片");
                            failed[idx] = true;
                            return;
                        }
                        long crStart = -1, crTotal = -1;
                        DownloadEngine.ParseContentRange(DownloadEngine.SafeHeader(resp, "Content-Range"), out crStart, out crTotal);
                        if (crStart >= 0 && crStart != from)
                        {
                            L("  分片" + (idx + 1) + "：Content-Range 起点(" + crStart + ") 与请求(" + from + ") 不符，放弃分片");
                            failed[idx] = true;
                            return;
                        }

                        byte[] buf = new byte[BufSize];
                        using (Stream net = resp.GetResponseStream())
                        {
                            while (from < segEnd)
                            {
                                if (cancelled()) return;
                                int want = (int)Math.Min((long)buf.Length, segEnd - from);
                                int k = net.Read(buf, 0, want);
                                if (k <= 0) break;
                                fs.Seek(from, SeekOrigin.Begin);
                                fs.Write(buf, 0, k);
                                from += k;
                                lock (lk) st.Done[idx] += k;
                                // 进度必须定期落盘：只在下片/异常时保存的话，进程被强杀或断电会丢掉全部已下字节
                                DateTime nowTick = DateTime.UtcNow;
                                if ((nowTick - lastSave).TotalSeconds >= 2)
                                {
                                    lastSave = nowTick;
                                    lock (lk) st.Save(part);
                                }
                                Report(report, st, lk, speedBox, getTick, setTick, getLastDone, setLastDone);
                            }
                        }
                        if (from < segEnd) throw new IOException("连接提前结束");
                        lock (lk) st.Save(part);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (cancelled()) return;
                        tries++;
                        long nowDone;
                        lock (lk) nowDone = st.Done[idx];
                        if (tries > SegRetry)
                        {
                            L("  分片" + (idx + 1) + "：连续失败 " + tries + " 次，放弃该片（" + Short(ex.Message) + "）");
                            failed[idx] = true;
                            return;
                        }
                        L("  分片" + (idx + 1) + "：中断(" + Short(ex.Message) + ")，已存 " + Fmt(nowDone) + "，从断点续传（第 " + tries + " 次）");
                        try { fs.Flush(); } catch { }
                        lock (lk) st.Save(part);
                        Thread.Sleep(400 * tries);
                    }
                    finally
                    {
                        if (resp != null) { try { resp.Close(); } catch { } }
                    }
                }
            }
            catch (Exception ex)
            {
                L("  分片" + (idx + 1) + "：无法打开半成品写入(" + Short(ex.Message) + ")");
                failed[idx] = true;
            }
            finally
            {
                if (fs != null) { try { fs.Flush(); fs.Close(); } catch { } }
            }
        }

        static void Report(Action<long, double> report, SegState st, object lk,
                           double[] speedBox, Func<DateTime> getTick, Action<DateTime> setTick,
                           Func<long> getLastDone, Action<long> setLastDone)
        {
            if (report == null) return;
            long total, last;
            DateTime tick;
            double spd = 0;
            bool fire = false;
            lock (lk)
            {
                total = st.TotalDone();
                last = getLastDone();
                tick = getTick();
                DateTime now = DateTime.UtcNow;
                double dt = (now - tick).TotalSeconds;
                if (dt >= 0.5)
                {
                    if (dt > 0) spd = (total - last) / dt;
                    speedBox[0] = spd;
                    setTick(now);
                    setLastDone(total);
                    fire = true;
                }
            }
            if (fire)
            {
                try { report(total, spd); } catch { }
            }
        }

        // 手动跟随跳转（最多 5 跳），同时用 bytes=0-0 拿到总长与标识。
        // .NET 的 AllowAutoRedirect 会在 302/307 上丢掉 Range 头，必须自己跟。
        // 返回：可用的最终地址（且服务器确认支持 Range）或 null（不支持/取不到长度）
        // hardUnsupported：服务器明确用 200/其它状态回了 Range 请求 —— 这是确定不支持，
        // 不能再退回 HEAD 兜底（HEAD 常常拿得到长度，会让人误以为可以分片）。
        string Resolve(string url, out long total, out string etag, out string lastmod, out bool hardUnsupported)
        {
            total = -1; etag = ""; lastmod = ""; hardUnsupported = false;
            string cur = url;
            for (int hop = 0; hop < 5; hop++)
            {
                HttpWebResponse resp = null;
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(cur);
                    req.Method = "GET";
                    req.UserAgent = "LocModelDL/3.0.0";
                    req.Timeout = 15000;
                    req.ReadWriteTimeout = 15000;
                    req.AllowAutoRedirect = false;
                    req.AddRange(0, 0);
                    resp = (HttpWebResponse)req.GetResponse();
                    int code = (int)resp.StatusCode;
                    string loc = DownloadEngine.SafeHeader(resp, "Location");
                    if (code >= 300 && code < 400 && loc.Length > 0)
                    {
                        string next;
                        try { next = new Uri(new Uri(cur), loc).ToString(); }
                        catch { next = loc; }
                        L("  分片：跟随跳转 " + code + " → " + MirrorOf(next) + "/…");
                        resp.Close();
                        cur = next;
                        continue;
                    }
                    if (code != 206 && resp.StatusCode != HttpStatusCode.PartialContent)
                    {
                        L("  分片：服务器对 Range 返回 " + code + "（非 206），不支持分片");
                        hardUnsupported = true;
                        return null;
                    }
                    long crStart;
                    DownloadEngine.ParseContentRange(DownloadEngine.SafeHeader(resp, "Content-Range"), out crStart, out total);
                    etag = DownloadEngine.SafeHeader(resp, "ETag");
                    if (etag.Length == 0) etag = DownloadEngine.SafeHeader(resp, "X-Linked-Etag");
                    lastmod = DownloadEngine.SafeHeader(resp, "Last-Modified");
                    if (total <= 0 && resp.ContentLength > 0) total = resp.ContentLength;
                    return total > 0 ? cur : null;
                }
                catch (Exception ex)
                {
                    L("  分片：探测（第 " + (hop + 1) + " 跳）失败：" + Short(ex.Message));
                    return null;
                }
                finally { if (resp != null) { try { resp.Close(); } catch { } } }
            }
            L("  分片：跳转次数过多，放弃分片");
            return null;
        }

        static bool SameTag(string a, string b)
        {
            string x = DownloadEngine.NormTagPublic(a);
            string y = DownloadEngine.NormTagPublic(b);
            if (x.Length == 0 || y.Length == 0) return true;   // 一侧缺失时不据此判换版
            return string.Equals(x, y, StringComparison.Ordinal);
        }

        static string MirrorOf(string url)
        {
            try { Uri u = new Uri(url); return u.Scheme + "://" + u.Host; }
            catch { return ""; }
        }

        static void KillFiles(string part)
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { }
            try { if (File.Exists(part + ".seg")) File.Delete(part + ".seg"); } catch { }
            try { if (File.Exists(part + ".meta")) File.Delete(part + ".meta"); } catch { }
        }

        static string Fmt(long bytes)
        {
            double v = bytes;
            string[] u = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return string.Format(i == 0 ? "{0:0} {1}" : "{0:0.0} {1}", v, u[i]);
        }

        static string Short(string s)
        {
            if (s == null) return "";
            return s.Length > 90 ? s.Substring(0, 90) : s;
        }
    }
}
