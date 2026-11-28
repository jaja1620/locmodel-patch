// LocModel Downloader v3.1.5 - 对口型子系统下载引擎（独立成段，无 WinForms 依赖）
// =====================================================================================
// 【独立成段说明】
//   本文件只做「下载动作」，界面在 LipsyncForm.cs，清单在 LipsyncCatalog.cs。
//   三者互不侵入主窗体的模型清单/详情区，也不改动内核 Downloader / DownloadSeg / 校验逻辑：
//   权重下载直接复用内核 DownloadEngine（分片 / 续传 / 校验 / 站点回落语义与主界面完全一致）。
//   回滚 = 删除本文件 + LipsyncForm.cs + LipsyncCatalog.cs + 还原 MainForm.cs 的三处 lip- 标记块。
//
// 【真实下载回归入口】MainForm 的 --lipget 命令行模式只调用本类的 Run()，与界面点击「开始下载」
//   走的是同一段代码；可用环境变量把清单/地址替换成小型测试数据（见 BuildItemsFromSpec 与
//   TestBaseOverride），从而在无模型盘的机器上做端到端回归。
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace LocModelDL
{
    public class LipsyncEngine
    {
        // 测试钩子（仅回归用，正常运行时为空）：把程序包/人声分离模型地址重定向到本地测试服务
        public const string EnvTestBase = "LOCMODEL_LIP_TEST_BASE";
        // 测试钩子（仅回归用）：用真实清单以外的小文件清单替换权重清单
        public const string EnvTestSpec = "LOCMODEL_LIP_TEST_SPEC";

        public string Root = "";
        public string Key = "hq";
        public bool DoWeights = true;
        public bool DoProgram = false;
        public bool DoDemucs = false;
        public List<string> Sites = new List<string>();

        public volatile bool Cancelled;

        public event Action<string> Log;
        public event Action<long, long, double> Progress;

        DownloadEngine _eng;

        void L(string s) { Action<string> h = Log; if (h != null) try { h(s); } catch { } }
        void P(long done, long total, double sp) { Action<long, long, double> h = Progress; if (h != null) try { h(done, total, sp); } catch { } }

        // ---------------- 清单 → 下载项 ----------------

        // 权重：HF 仓库 + 仓库内路径 → 落盘相对路径（内核按 站点 + "/" + Repo + "/resolve/main/" + Path 拼 URL）
        public static List<DownloadItem> BuildItems(string root, string key)
        {
            List<DownloadItem> items = new List<DownloadItem>();
            foreach (LipsyncFile f in LipsyncCatalog.FilesOf(key))
            {
                DownloadItem it = new DownloadItem();
                it.Repo = f.Repo;
                it.Path = f.SrcPath;
                it.DestPath = Path.Combine(root, f.DestRel);
                it.ExpectedSize = f.Size;
                it.Label = f.DestRel + "  ← " + f.Repo;
                items.Add(it);
            }
            return items;
        }

        // 回归用：specFile 每行 "repo|srcPath|destRel|size"，用于把 GB 级清单换成 KB/MB 级假数据
        public static List<DownloadItem> BuildItemsFromSpec(string root, string specFile)
        {
            List<DownloadItem> items = new List<DownloadItem>();
            if (string.IsNullOrEmpty(specFile) || !File.Exists(specFile)) return items;
            foreach (string raw in File.ReadAllLines(specFile))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split('|');
                if (p.Length < 4) continue;
                long size = 0;
                long.TryParse(p[3].Trim(), out size);
                DownloadItem it = new DownloadItem();
                it.Repo = p[0].Trim();
                it.Path = p[1].Trim();
                it.DestPath = Path.Combine(root, p[2].Trim());
                it.ExpectedSize = size;
                it.Label = p[2].Trim() + "  ← " + p[0].Trim();
                items.Add(it);
            }
            return items;
        }

        static string TestBase()
        {
            try { string s = Environment.GetEnvironmentVariable(EnvTestBase); return s == null ? "" : s.TrimEnd('/'); }
            catch { return ""; }
        }

        public static string[] ProgramUrls()
        {
            string b = TestBase();
            if (b.Length > 0)
                return new string[] { b + "/program/" + LipsyncCatalog.ProgramZipName };
            return LipsyncCatalog.ProgramSources;
        }

        public static string DemucsUrl()
        {
            string b = TestBase();
            if (b.Length > 0)
                return b + "/demucs/" + LipsyncCatalog.DemucsFileName;
            return LipsyncCatalog.DemucsUrlOf();
        }

        public static string WeightsUrlFor(string site, DownloadItem it)
        {
            return site.TrimEnd('/') + "/" + it.Repo + "/resolve/main/" + it.Path;
        }

        // ---------------- 执行 ----------------

        public bool Run()
        {
            bool ok = true;
            try
            {
                if (Root != null && Root.Length > 0 && !Directory.Exists(Root)) Directory.CreateDirectory(Root);
            }
            catch (Exception ex) { L("目录不可写: " + ex.Message); return false; }

            if (DoWeights) ok = RunWeights() && ok;
            if (DoProgram) ok = RunProgram() && ok;
            if (DoDemucs) ok = RunDemucs() && ok;
            return ok;
        }

        bool RunWeights()
        {
            string spec = "";
            try { spec = Environment.GetEnvironmentVariable(EnvTestSpec); } catch { }
            List<DownloadItem> items = (spec != null && spec.Trim().Length > 0 && File.Exists(spec.Trim()))
                ? BuildItemsFromSpec(Root, spec.Trim())
                : BuildItems(Root, Key);

            DownloadEngine eng = new DownloadEngine();
            eng.Log += delegate (string s) { L(s); };
            eng.Progress += delegate (long done, long total, double sp) { P(done, total, sp); };
            _eng = eng;
            DownloadEngine.SetMirrors(Sites);
            L("权重下载开始：共 " + items.Count + " 个文件");
            bool r = eng.RunAll(items, Path.Combine(Root, "_dl_tmp"));
            _eng = null;
            L(r ? "权重下载结束：全部完成" : "权重下载结束：存在失败项");
            return r;
        }

        // 程序包：GitHub / ModelScope 分发，HF 镜像不涵盖 → 绝对地址直连，多源按顺序尝试
        bool RunProgram()
        {
            string dest = Path.Combine(Path.Combine(Root, "program"), LipsyncCatalog.ProgramZipName);
            try { Directory.CreateDirectory(Path.GetDirectoryName(dest)); } catch { }
            string[] urls = ProgramUrls();
            foreach (string url in urls)
            {
                L("程序包源： " + url);
                if (FetchAbsolute(url, dest))
                {
                    L("程序包已保存：" + dest + "（解包由 LocModel 或手动完成，本下载器不解包）");
                    return true;
                }
            }
            L("程序包下载失败：所有来源均不可用。若网络受限，可用 LocModel 自带的对口型安装功能下载程序包。");
            return false;
        }

        bool RunDemucs()
        {
            string dest = Path.Combine(Path.Combine(Root, "demucs"), LipsyncCatalog.DemucsFileName);
            try { Directory.CreateDirectory(Path.GetDirectoryName(dest)); } catch { }
            string url = DemucsUrl();
            L("人声分离模型源： " + url);
            bool ok = FetchAbsolute(url, dest);
            L(ok ? "人声分离模型已保存：" + dest : "人声分离模型下载失败");
            return ok;
        }

        // 单文件绝对地址下载（支持 Range 续传，失败最多重试 2 次；.part 落盘后再改名）
        public bool FetchAbsolute(string url, string dest)
        {
            string part = dest + ".part";
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    long have = 0;
                    if (File.Exists(part)) have = new FileInfo(part).Length;
                    else if (File.Exists(dest)) { L("已存在，跳过：" + dest); return true; }

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.AllowAutoRedirect = true;
                    req.UserAgent = "LocModelDownloader/3.1.5";
                    req.Timeout = 20000;
                    req.ReadWriteTimeout = 30000;
                    if (have > 0) req.AddRange((int)have);

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (have > 0 && resp.StatusCode != HttpStatusCode.PartialContent)
                        {
                            have = 0; // 服务端不支持续传，从头来
                            try { File.Delete(part); } catch { }
                        }
                        long total = resp.ContentLength + have;
                        using (Stream rs = resp.GetResponseStream())
                        using (FileStream fs = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buf = new byte[131072];
                            long done = have;
                            DateTime last = DateTime.UtcNow;
                            while (true)
                            {
                                DownloadEngine e = _eng;
                                if (Cancelled || (e != null && e.Cancelled)) { L("已取消"); return false; }
                                int n = rs.Read(buf, 0, buf.Length);
                                if (n <= 0) break;
                                fs.Write(buf, 0, n);
                                done += n;
                                if ((DateTime.UtcNow - last).TotalMilliseconds > 300)
                                {
                                    last = DateTime.UtcNow;
                                    P(done, total, 0);
                                }
                            }
                            fs.Flush();
                        }
                        L("下载完成：" + url);
                    }

                    if (File.Exists(dest)) { try { File.Delete(dest); } catch { } }
                    File.Move(part, dest);
                    return true;
                }
                catch (Exception ex)
                {
                    L("第 " + attempt + " 次尝试失败：" + ex.Message);
                    if (attempt < 3) Thread.Sleep(1200 * attempt);
                }
            }
            return false;
        }
    }
}
