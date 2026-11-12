// LocModel Downloader v2.1 - download engine
// v2.1 全量修正：已存在校验(缺陷1) / 不足长度转半成品续传(缺陷2) / 校验失败清理半成品+限次重试(缺陷3)
//               / 单文件失败不中断整批(缺陷4) / HEAD 预检换版(优化5) / 结束补发最终进度帧(优化6)
//               / 线路健康记忆+缩短超时(优化7) / 磁盘空间预检(优化10) / SHA-256 整包校验(优化11)
//               / 跨镜像优先比 ETag(优化12) / 半成品名并入路径哈希(优化9)
// 保留 v2.1 已加固：续传按 206/ETag/Last-Modified 校验、416 自动恢复
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace LocModelDL
{
    public class DownloadItem
    {
        public string Repo;
        public string Path;       // path inside repo
        public string DestPath;   // final full path
        public long ExpectedSize; // -1 unknown
        public string Label;
        public string Sha256;     // optional expected sha256 (优化11)
        public string UrlPath { get { return Repo + "/resolve/main/" + Path; } }
        public string BaseName
        {
            get { int i = Path.LastIndexOf('/'); return i < 0 ? Path : Path.Substring(i + 1); }
        }
    }

    // 半成品校验记录：与 .part 同目录的 .part.meta，记录远端文件标识，续传前用于判断远端是否已换版
    class PartMeta
    {
        public string Etag = "";
        public string LastMod = "";
        public string Mirror = "";
        public long Size = -1;

        public static PartMeta Load(string part)
        {
            PartMeta m = new PartMeta();
            try
            {
                string p = part + ".meta";
                if (!File.Exists(p)) return m;
                foreach (string line in File.ReadAllLines(p))
                {
                    string cl = line.TrimStart('\uFEFF');   // 兼容带 BOM 的旧 meta
                    int i = cl.IndexOf('=');
                    if (i <= 0) continue;
                    string k = cl.Substring(0, i).Trim().ToLowerInvariant();
                    string v = cl.Substring(i + 1).Trim();
                    if (k == "etag") m.Etag = v;
                    else if (k == "lastmod") m.LastMod = v;
                    else if (k == "mirror") m.Mirror = v;
                    else if (k == "size")
                    {
                        long s;
                        if (long.TryParse(v, out s)) m.Size = s;
                    }
                }
            }
            catch { }
            return m;
        }

        public void Save(string part)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("etag=" + Etag);
                sb.AppendLine("lastmod=" + LastMod);
                sb.AppendLine("mirror=" + Mirror);
                sb.AppendLine("size=" + Size);
                File.WriteAllText(part + ".meta", sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public bool HasValidator { get { return Etag.Length > 0 || LastMod.Length > 0; } }
    }

    public class DownloadEngine
    {
        public event Action<string> Log;
        public event Action<long, long, double> Progress;   // doneBytes, totalBytes, bytesPerSec
        public volatile bool Cancelled;

        // 优化7 / 优化8：慢链（huggingface.co 本机 20s 无响应）放最后；m1/m2 作为快速失败兜底
        // 成功线路在运行时被提升到首位（线路健康记忆）
        public static readonly string[] BuiltinFileMirrors = new string[] {
            "https://hf-mirror.com",
            "https://m1.locmodel.com",
            "https://m2.locmodel.com",
            "https://huggingface.co"
        };

        // v2.2：默认线路顺序；界面测速后会被 SetMirrors 覆盖为用户站点优先的顺序
        public static string[] FileMirrors = (string[])BuiltinFileMirrors.Clone();

        const int StallSeconds = 15;      // 优化7：30s -> 15s
        const int ReqTimeoutMs = 15000;   // 优化7：30s -> 15s
        const int HeadTimeoutMs = 10000;  // 优化5：HEAD 预检
        const int MaxVerifyFail = 2;      // 缺陷3：校验失败最多重试 2 次

        static readonly object MirrorLock = new object();

        void L(string s) { Action<string> h = Log; if (h != null) h(s); }

        void Frame(long doneBase, long totalBytes, long done)
        {
            Action<long, long, double> ph = Progress;
            if (ph != null) ph(doneBase + done, totalBytes > 0 ? totalBytes : 0, 0);
        }

        public bool RunAll(List<DownloadItem> items, string tempRoot)
        {
            long totalBytes = 0;
            foreach (DownloadItem it in items) if (it.ExpectedSize > 0) totalBytes += it.ExpectedSize;

            if (!Directory.Exists(tempRoot)) Directory.CreateDirectory(tempRoot);

            // 优化10：开始前做磁盘剩余空间预检
            if (!PrecheckDisk(items, tempRoot)) return false;

            long doneBase = 0;
            List<DownloadItem> failed = new List<DownloadItem>();

            for (int i = 0; i < items.Count; i++)
            {
                if (Cancelled) { L("已取消"); Summarize(items.Count, failed); return false; }
                DownloadItem it = items[i];
                L(string.Format("[{0}/{1}] {2}", i + 1, items.Count, it.Label));
                bool ok = DownloadOne(it, tempRoot, doneBase, totalBytes);
                if (!ok)
                {
                    // 缺陷4：单文件失败不再中断整批
                    failed.Add(it);
                    L("  本文件失败，跳过并继续后续文件");
                }
                if (it.ExpectedSize > 0) doneBase += it.ExpectedSize;
                else doneBase += 0;
            }

            // 优化6：全部结束时补发一帧收尾进度，避免进度条停在旧值
            if (totalBytes > 0 && failed.Count == 0 && !Cancelled) Frame(0, totalBytes, totalBytes);

            return Summarize(items.Count, failed);
        }

        bool Summarize(int total, List<DownloadItem> failed)
        {
            if (failed.Count == 0)
            {
                L(string.Format("全部完成：{0}/{0} 个文件", total));
                return true;
            }
            int okCount = total - failed.Count;
            L(string.Format("本批结束：成功 {0}/{1}，失败 {2} 个", okCount, total, failed.Count));
            foreach (DownloadItem f in failed) L("  失败 - " + f.Label + " -> " + f.DestPath);
            return false;
        }

        // ---------- 优化10：磁盘剩余空间预检 ----------
        bool PrecheckDisk(List<DownloadItem> items, string tempRoot)
        {
            try
            {
                long need = 0;
                int unknown = 0;
                foreach (DownloadItem it in items)
                {
                    if (it.ExpectedSize <= 0) { unknown++; continue; }
                    long have = 0;
                    try { if (File.Exists(it.DestPath)) have = Math.Max(have, new FileInfo(it.DestPath).Length); } catch { }
                    try { string p = PartPath(tempRoot, it); if (File.Exists(p)) have = Math.Max(have, new FileInfo(p).Length); } catch { }
                    long rem = it.ExpectedSize - have;
                    if (rem > 0) need += rem;
                }

                string root = Path.GetPathRoot(Path.GetFullPath(tempRoot));
                DriveInfo di = new DriveInfo(root);
                long free = di.AvailableFreeSpace;

                string note = string.Format("磁盘空间：还需 {0}，{1} 可用 {2}", Fmt(need), root, Fmt(free));
                if (unknown > 0) note += string.Format("（另有 {0} 个文件大小未知，未计入）", unknown);
                L(note);

                if (need > free)
                {
                    L(string.Format("磁盘剩余空间不足（还需 {0}，可用 {1}），已中止本次下载", Fmt(need - free), Fmt(free)));
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                L("磁盘空间预检跳过: " + Short(ex.Message));
                return true;
            }
        }

        static string Fmt(long bytes)
        {
            double v = bytes;
            string[] u = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return string.Format(i == 0 ? "{0:0} {1}" : "{0:0.0} {1}", v, u[i]);
        }

        bool DownloadOne(DownloadItem it, string tempRoot, long doneBase, long totalBytes)
        {
            // 优化9：半成品名并入 UrlPath 哈希，避免同 repo 不同子目录同名文件互相覆盖
            string part = PartPath(tempRoot, it);

            if (File.Exists(it.DestPath))
            {
                long existing = 0;
                try { existing = new FileInfo(it.DestPath).Length; } catch { }

                // 缺陷1：期望大小未知时先用 HEAD 探测远端真实长度，避免把任意"已存在文件"当合格
                if (it.ExpectedSize <= 0)
                {
                    long probed = ProbeRemoteLength(it);
                    if (probed > 0)
                    {
                        it.ExpectedSize = probed;
                        L("  远端长度探测: " + probed + "B（本地已存在 " + existing + "B）");
                    }
                    else L("  远端长度探测失败，按内容校验判断");
                }

                bool acceptable = false;
                if (it.ExpectedSize <= 0 || existing == it.ExpectedSize)
                {
                    // 缺陷1：大小未知或长度相同也必须校验内容，不做无条件跳过
                    try { Verify(it, it.DestPath); acceptable = true; }
                    catch (Exception ex) { L("  已存在但校验不通过(" + Short(ex.Message) + ")，将重新下载"); }
                }

                if (acceptable)
                {
                    L("  已存在且校验通过，跳过: " + it.DestPath);
                    Frame(doneBase, totalBytes, it.ExpectedSize > 0 ? it.ExpectedSize : existing);
                    return true;
                }

                if (it.ExpectedSize > 0 && existing > it.ExpectedSize)
                {
                    L("  已存在文件(" + existing + "B) 大于期望(" + it.ExpectedSize + "B)，删除重下");
                    try { File.Delete(it.DestPath); } catch { }
                }
                else
                {
                    // 缺陷2：不足完整长度时不整包重下，转为半成品续传
                    MoveDestToPart(it.DestPath, part, existing);
                }
            }

            int attempt = 0;
            int verifyFail = 0;
            int maxAttempt = FileMirrors.Length * 2;
            while (attempt < maxAttempt && verifyFail < MaxVerifyFail)
            {
                if (Cancelled) { L("  已取消"); return false; }
                string baseUrl = FileMirrors[attempt % FileMirrors.Length];
                string url = baseUrl + "/" + it.UrlPath;
                attempt++;
                try
                {
                    L("  线路 " + baseUrl);
                    if (Fetch(url, part, it.ExpectedSize, doneBase, totalBytes))
                    {
                        try { Verify(it, part); }
                        catch (Exception vex)
                        {
                            verifyFail++;
                            // 缺陷3：校验失败立即清理半成品，避免坏数据反复重试
                            L("  校验失败(" + Short(vex.Message) + ")，丢弃半成品（第 " + verifyFail + " 次）");
                            Reset(part);
                            if (verifyFail >= MaxVerifyFail) break;
                            continue;
                        }

                        PromoteMirror(baseUrl);   // 优化7：记住可用线路
                        Directory.CreateDirectory(Path.GetDirectoryName(it.DestPath));
                        if (File.Exists(it.DestPath)) File.Delete(it.DestPath);
                        File.Move(part, it.DestPath);
                        try { string mp = part + ".meta"; if (File.Exists(mp)) File.Delete(mp); } catch { }
                        L("  完成 -> " + it.DestPath);
                        if (it.ExpectedSize <= 0) { try { it.ExpectedSize = new FileInfo(it.DestPath).Length; } catch { } }
                        Frame(doneBase, totalBytes, it.ExpectedSize > 0 ? it.ExpectedSize : 0);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    L("  线路失败(" + baseUrl + "): " + Short(ex.Message));
                }
                if (Cancelled) return false;
                Thread.Sleep(400);
            }
            if (verifyFail >= MaxVerifyFail) L("  连续校验失败 " + verifyFail + " 次，放弃该文件");
            L("  全部线路失败: " + it.BaseName);
            return false;
        }

        // 缺陷2：已存在的不足长度文件转半成品（若已有更完整的半成品则沿用）
        void MoveDestToPart(string dest, string part, long destLen)
        {
            long partLen = 0;
            try { if (File.Exists(part)) partLen = new FileInfo(part).Length; } catch { }

            if (partLen > 0 && partLen >= destLen)
            {
                L("  已有更完整的半成品(" + partLen + "B)，沿用半成品续传");
                try { File.Delete(dest); } catch { }
                return;
            }
            try
            {
                if (File.Exists(part)) Reset(part);
                File.Move(dest, part);
                L("  已存在文件(" + destLen + "B) 不足完整长度，转为半成品续传");
            }
            catch (Exception ex)
            {
                L("  转半成品失败(" + Short(ex.Message) + ")，删除后整包重下");
                try { File.Delete(dest); } catch { }
            }
        }

        bool Fetch(string url, string part, long expected, long doneBase, long totalBytes)
        {
            string mirror = MirrorOf(url);
            long offset = 0;
            if (File.Exists(part)) offset = new FileInfo(part).Length;
            PartMeta meta = PartMeta.Load(part);

            if (expected > 0 && offset == expected) { Frame(doneBase, totalBytes, offset); return true; }
            if (expected > 0 && offset > expected)
            {
                L("  半成品(" + offset + "B) 大于期望(" + expected + "B)，丢弃重下");
                Reset(part); offset = 0; meta = new PartMeta();
            }
            if (meta.Size > 0 && expected > 0 && meta.Size != expected)
            {
                L("  半成品记录总长(" + meta.Size + "B) 与期望(" + expected + "B) 不符，判定远端已换版，丢弃重下");
                Reset(part); offset = 0; meta = new PartMeta();
            }
            if (offset > 0 && !meta.HasValidator)
                L("  半成品无校验记录（v2.0 遗留），按长度续传");

            // 优化5：续传前先用 HEAD 取远端标识，换版则在发数据前就丢弃，省掉一次无效 Range 流量
            if (offset > 0 && meta.HasValidator)
            {
                long remoteLen = -1;
                string hEtag = "", hLastMod = "";
                if (HeadValidators(url, out remoteLen, out hEtag, out hLastMod))
                {
                    if (remoteLen > 0 && expected > 0 && remoteLen != expected)
                    {
                        L("  HEAD 预检：远端长度(" + remoteLen + "B) 与期望(" + expected + "B) 不符，判定换版，整包重下");
                        Reset(part); offset = 0; meta = new PartMeta();
                    }
                    else if (ValidatorChanged(meta, mirror, hEtag, hLastMod))
                    {
                        L("  HEAD 预检：远端文件标识已变化，丢弃半成品整包重下（未产生 Range 流量）");
                        Reset(part); offset = 0; meta = new PartMeta();
                    }
                    else L("  HEAD 预检：远端标识一致，继续续传");
                }
                else L("  HEAD 预检不可用，改按 Range 响应校验");
            }

            for (int pass = 0; pass < 2; pass++)
            {
                if (Cancelled) throw new OperationCanceledException();
                bool useRange = offset > 0;

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "LocModel/0.1";
                req.Timeout = ReqTimeoutMs;
                req.ReadWriteTimeout = StallSeconds * 1000;
                req.AllowAutoRedirect = true;
                req.KeepAlive = true;
                if (useRange) req.AddRange(offset);

                HttpWebResponse resp = null;
                try
                {
                    try { resp = (HttpWebResponse)req.GetResponse(); }
                    catch (WebException wex)
                    {
                        HttpWebResponse er = wex.Response as HttpWebResponse;
                        if (er != null && er.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                        {
                            long remoteTotal = -1;
                            try { remoteTotal = ParseUnsatisfiedTotal(er.Headers["Content-Range"]); } catch { }
                            try { er.Close(); } catch { }
                            if (offset > 0 && remoteTotal > 0 && remoteTotal == offset && (expected <= 0 || expected == offset))
                            {
                                L("  远端总长与半成品完全一致（416，远端 " + remoteTotal + "B），判定已下载完整");
                                Frame(doneBase, totalBytes, offset);
                                return true;
                            }
                            L("  服务器拒绝该区间（416；远端 " + (remoteTotal > 0 ? remoteTotal + "B" : "总长未知") + "，半成品 " + offset + "B），丢弃半成品从 0 重下");
                            Reset(part); offset = 0; meta = new PartMeta();
                            continue;
                        }
                        throw;
                    }

                    bool partial = resp.StatusCode == HttpStatusCode.PartialContent;
                    string respEtag = SafeHeader(resp, "ETag");
                    string respLastMod = SafeHeader(resp, "Last-Modified");
                    bool needNewRequest = false;

                    // 加固点1：续传前校验远端文件是否已换版
                    if (offset > 0 && !partial)
                    {
                        L("  服务器未按区间返回（" + (int)resp.StatusCode + "），丢弃半成品从 0 重下");
                        Reset(part); offset = 0; meta = new PartMeta();
                    }
                    else if (partial)
                    {
                        long crStart = -1, crTotal = -1;
                        ParseContentRange(SafeHeader(resp, "Content-Range"), out crStart, out crTotal);
                        if (crStart >= 0 && crStart != offset)
                        {
                            L("  Content-Range 起点(" + crStart + ") 与半成品长度(" + offset + ") 不符，丢弃半成品并重新请求");
                            Reset(part); offset = 0; meta = new PartMeta(); needNewRequest = true;
                        }
                        if (!needNewRequest && meta.HasValidator && ValidatorChanged(meta, mirror, respEtag, respLastMod))
                        {
                            L("  远端文件标识已变化（ETag/Last-Modified 与半成品记录不符），丢弃半成品并重新请求");
                            Reset(part); offset = 0; meta = new PartMeta(); needNewRequest = true;
                        }
                    }
                    if (needNewRequest) continue;

                    long contentLen = resp.ContentLength;
                    long fileTotal;
                    if (partial)
                    {
                        long st, tt;
                        ParseContentRange(SafeHeader(resp, "Content-Range"), out st, out tt);
                        fileTotal = tt > 0 ? tt : (contentLen > 0 ? offset + contentLen : (expected > 0 ? expected : -1));
                    }
                    else fileTotal = contentLen > 0 ? contentLen : (expected > 0 ? expected : -1);

                    // 先落盘校验记录，再写数据：中途中断也能保留远端标识供下次续传校验
                    PartMeta rec = new PartMeta();
                    rec.Etag = respEtag; rec.LastMod = respLastMod; rec.Mirror = mirror; rec.Size = fileTotal;
                    rec.Save(part);

                    FileStream fs = new FileStream(part, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                    try
                    {
                        using (Stream net = resp.GetResponseStream())
                        {
                            byte[] buf = new byte[1 << 20];
                            long done = offset;
                            DateTime lastTick = DateTime.UtcNow;
                            long lastBytes = done;
                            double speed = 0;
                            int zeroReads = 0;

                            while (true)
                            {
                                if (Cancelled) throw new OperationCanceledException();
                                int n;
                                try { n = net.Read(buf, 0, buf.Length); }
                                catch (Exception ex)
                                {
                                    if (ex is WebException || ex is IOException) { fs.Flush(); throw; }
                                    throw;
                                }
                                if (n <= 0)
                                {
                                    zeroReads++;
                                    if (zeroReads > 3) break;
                                    Thread.Sleep(200);
                                    continue;
                                }
                                zeroReads = 0;
                                fs.Write(buf, 0, n);
                                done += n;

                                DateTime now = DateTime.UtcNow;
                                double dt = (now - lastTick).TotalSeconds;
                                if (dt >= 0.5)
                                {
                                    speed = (done - lastBytes) / dt;
                                    lastTick = now; lastBytes = done;
                                    long tot = fileTotal > 0 ? fileTotal : done;
                                    Action<long, long, double> ph = Progress;
                                    if (ph != null) ph(doneBase + done, totalBytes > 0 ? totalBytes : 0, speed);
                                }
                            }
                            fs.Flush();
                        }
                    }
                    finally { fs.Close(); }

                    long finalLen = new FileInfo(part).Length;
                    if (fileTotal > 0 && finalLen < fileTotal)
                    {
                        L("  连接提前结束 (" + finalLen + "/" + fileTotal + ")，将断点续传");
                        throw new IOException("incomplete");
                    }
                    if (fileTotal <= 0)
                    {
                        rec.Size = finalLen;
                        rec.Save(part);
                    }
                    // 优化6：本次传输结束补发最终进度帧
                    Frame(doneBase, totalBytes, finalLen);
                    return true;
                }
                finally
                {
                    if (resp != null) { try { resp.Close(); } catch { } }
                }
            }
            throw new IOException("区间校验两次均未通过");
        }

        // 缺陷1：未知大小时按线路顺序 HEAD 探测远端真实长度
        long ProbeRemoteLength(DownloadItem it)
        {
            for (int i = 0; i < FileMirrors.Length; i++)
            {
                string baseUrl = FileMirrors[i];
                long len = -1; string et = "", lm = "";
                if (HeadValidators(baseUrl + "/" + it.UrlPath, out len, out et, out lm) && len > 0)
                {
                    PromoteMirror(baseUrl);
                    return len;
                }
            }
            return -1;
        }

        // 优化5：HEAD 取远端长度与标识（失败即视为不可用，不阻塞下载）
        static bool HeadValidators(string url, out long length, out string etag, out string lastmod)
        {
            length = -1; etag = ""; lastmod = "";
            HttpWebResponse resp = null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD";
                req.UserAgent = "LocModel/0.1";
                req.Timeout = HeadTimeoutMs;
                req.ReadWriteTimeout = HeadTimeoutMs;
                req.AllowAutoRedirect = true;
                resp = (HttpWebResponse)req.GetResponse();
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) return false;
                length = resp.ContentLength;
                etag = SafeHeader(resp, "ETag");
                lastmod = SafeHeader(resp, "Last-Modified");
                return etag.Length > 0 || lastmod.Length > 0 || length > 0;
            }
            catch { return false; }
            finally { if (resp != null) { try { resp.Close(); } catch { } } }
        }

        // 优化11：校验 = 最小长度 + HTML 检测 + 魔数/结构 + （目录提供时）整包 SHA-256
        void Verify(DownloadItem it, string part)
        {
            long len = new FileInfo(part).Length;
            if (len < 512) throw new InvalidDataException("文件过小(" + len + "B)，疑似错误响应");

            byte[] head = new byte[Math.Min(4096, (int)len)];
            using (FileStream fs = File.OpenRead(part)) { fs.Read(head, 0, head.Length); }

            // HTML 页面
            string h = Encoding.ASCII.GetString(head, 0, Math.Min(head.Length, 512)).TrimStart();
            if (h.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || h.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("返回的是 HTML 页面，非模型文件");

            string low = it.BaseName.ToLowerInvariant();
            if (low.EndsWith(".gguf"))
            {
                if (!(head[0] == 'G' && head[1] == 'G' && head[2] == 'U' && head[3] == 'F'))
                    throw new InvalidDataException("GGUF 魔数校验失败");
            }
            else if (low.EndsWith(".safetensors"))
            {
                long hdr = BitConverter.ToInt64(head, 0);
                if (hdr <= 0 || hdr > len) throw new InvalidDataException("safetensors 头部长度异常");
            }
            else
            {
                // 纯空白
                bool blank = true;
                foreach (byte b in head) { if (b != 0x20 && b != 0x0a && b != 0x0d && b != 0x09) { blank = false; break; } }
                if (blank) throw new InvalidDataException("文件内容为空");
            }

            // 整包哈希：目录未提供哈希时跳过
            if (!string.IsNullOrEmpty(it.Sha256))
            {
                string want = it.Sha256.Trim();
                string got = Sha256Of(part);
                if (!string.Equals(got, want, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 不一致（本地 " + Prefix(got, 12) + " / 期望 " + Prefix(want, 12) + "）");
            }
        }

        static string Sha256Of(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hb = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hb.Length * 2);
                foreach (byte b in hb) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static string Prefix(string s, int n) { return s == null ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…"); }

        static string MirrorOf(string url)
        {
            try { Uri u = new Uri(url); return u.Scheme + "://" + u.Host; }
            catch { return ""; }
        }

        static string SafeHeader(HttpWebResponse r, string name)
        {
            try { string v = r.Headers[name]; return v == null ? "" : v.Trim(); }
            catch { return ""; }
        }

        // bytes 1000-2999/3000
        static void ParseContentRange(string v, out long start, out long total)
        {
            start = -1; total = -1;
            if (string.IsNullOrEmpty(v)) return;
            v = v.Trim();
            int sp = v.IndexOf(' ');
            if (sp >= 0) v = v.Substring(sp + 1).Trim();
            int dash = v.IndexOf('-');
            int slash = v.IndexOf('/');
            if (dash > 0)
            {
                long s;
                if (long.TryParse(v.Substring(0, dash), out s)) start = s;
            }
            if (slash >= 0)
            {
                long t;
                if (long.TryParse(v.Substring(slash + 1).Trim(), out t)) total = t;
            }
        }

        // 416 响应头里的 bytes */总长
        static long ParseUnsatisfiedTotal(string v)
        {
            if (string.IsNullOrEmpty(v)) return -1;
            int slash = v.IndexOf('/');
            if (slash < 0) return -1;
            long t;
            return long.TryParse(v.Substring(slash + 1).Trim(), out t) ? t : -1;
        }

        // 优化12：优先比 ETag（hf-mirror 不返回 Last-Modified），ETag 缺失时退回比 Last-Modified
        static bool ValidatorChanged(PartMeta meta, string mirror, string etag, string lastmod)
        {
            if (meta.Etag.Length > 0 && etag.Length > 0)
                return !string.Equals(NormTag(meta.Etag), NormTag(etag), StringComparison.Ordinal);
            if (meta.LastMod.Length > 0 && lastmod.Length > 0)
                return !string.Equals(meta.LastMod.Trim(), lastmod.Trim(), StringComparison.Ordinal);
            return false;
        }

        // 不同镜像/边缘节点对 ETag 的引号与 W/ 前缀处理不一致，比较前统一归一化
        static string NormTag(string v)
        {
            if (v == null) return "";
            string s = v.Trim();
            if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2).Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s;
        }

        // v2.2：按界面测速结果重设线路顺序（去重、保序、失败站点垫后由调用方决定）
        public static void SetMirrors(IList<string> ordered)
        {
            if (ordered == null) { lock (MirrorLock) { FileMirrors = (string[])BuiltinFileMirrors.Clone(); } return; }
            lock (MirrorLock)
            {
                List<string> arr = new List<string>();
                foreach (string s in ordered)
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    string v = s.Trim().TrimEnd('/');
                    if (v.Length == 0) continue;
                    if (!arr.Contains(v)) arr.Add(v);
                }
                // 空列表视为「未指定」，回退内置线路，绝不让线路表为空
                if (arr.Count == 0) { FileMirrors = (string[])BuiltinFileMirrors.Clone(); return; }
                FileMirrors = arr.ToArray();
            }
        }

        public static string[] MirrorsSnapshot()
        {
            lock (MirrorLock) { return (string[])FileMirrors.Clone(); }
        }

        // 优化7：把成功线路提升到队首，后续文件优先复用
        static void PromoteMirror(string baseUrl)
        {
            lock (MirrorLock)
            {
                int idx = Array.IndexOf(FileMirrors, baseUrl);
                if (idx <= 0) return;
                string[] arr = (string[])FileMirrors.Clone();
                string hit = arr[idx];
                for (int i = idx; i > 0; i--) arr[i] = arr[i - 1];
                arr[0] = hit;
                FileMirrors = arr;
            }
        }

        static void Reset(string part)
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { }
            try { string m = part + ".meta"; if (File.Exists(m)) File.Delete(m); } catch { }
        }

        // 优化9：半成品路径 = 仓库/文件名 + UrlPath 哈希
        public static string PartPath(string tempRoot, DownloadItem it)
        {
            return Path.Combine(tempRoot, Sanitize(it.Repo + "_" + it.BaseName) + "_" + Hash8(it.UrlPath) + ".part");
        }

        static string Hash8(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        static string Sanitize(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            string r = sb.ToString();
            return r.Length > 110 ? r.Substring(0, 110) : r;
        }

        static string Short(string s) { if (s == null) return ""; return s.Length > 90 ? s.Substring(0, 90) : s; }
    }
}
