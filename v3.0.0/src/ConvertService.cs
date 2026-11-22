// LocModel Downloader v2.4.2 - 本地转换引擎（A 路线：uv + PyTorch CPU + llama.cpp）
// 设计约束：不碰下载/校验内核；依赖装在 exe 同目录 convert\ 下，首次下载后复用。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace LocModelDL
{
    public class ConvertRequest
    {
        public string Repo = "";
        public string QuantName = "Q4_K_M";
        public string OutType = "f16";                          // convert 的 --outtype：f16 / bf16 / q8_0
        public string OutDir = "";                              // GGUF 落盘目录
        public List<string> SourceFiles = new List<string>();   // 本地已下载的源文件绝对路径
        public long EstBytes = 0;
    }

    public class ConvertOutcome
    {
        public bool Ok = false;
        public string GgufPath = "";
        public string Err = "";
    }

    public static class ConvertService
    {
        public delegate void Proc(string msg);
        public static volatile bool Cancelled = false;

        public const string PyVersion = "3.12";

        static string ExeDir
        {
            get { try { return Path.GetDirectoryName(typeof(ConvertService).Assembly.Location); } catch { return "."; } }
        }
        // 依赖根目录：默认 exe 同目录 convert\；但 Windows 单路径上限 260 字符，
        // 而 site-packages 内最深路径（transformers/models/audio_spectrogram_transformer/…py）约 100 字符，
        // exe 路径过长会让 pip 解压丢文件（实测 transformers 缺 models 子模块导致转换脚本 import 失败），
        // 所以路径过长时改落 %LOCALAPPDATA%\LocModelConvert。
        public static string Root
        {
            get
            {
                string cand = Path.Combine(ExeDir, "convert");
                if (cand.Length + 110 <= 200) return cand;
                string local = "";
                try { local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } catch { }
                if (string.IsNullOrEmpty(local)) return cand;
                return Path.Combine(local, "LocModelConvert");
            }
        }

        // ==================== 对外入口 ====================
        public static ConvertOutcome Run(ConvertRequest req, Proc log)
        {
            ConvertOutcome oc = new ConvertOutcome();
            Cancelled = false;
            try
            {
                if (req == null || req.SourceFiles == null || req.SourceFiles.Count == 0)
                { oc.Err = "没有源文件，无法转换"; return oc; }
                foreach (string f in req.SourceFiles)
                    if (!File.Exists(f)) { oc.Err = "源文件缺失：" + f; return oc; }
                if (string.IsNullOrEmpty(req.OutDir)) { oc.Err = "没有指定输出目录"; return oc; }
                MKDir(req.OutDir);
                MKDir(Root);
                log("转换工作目录：" + Root);

                string uv = EnsureUv(log);
                if (Cancelled) { oc.Err = "已取消"; return oc; }
                string py = EnsurePython(uv, log);
                if (Cancelled) { oc.Err = "已取消"; return oc; }
                EnsurePyDeps(uv, py, log);
                if (Cancelled) { oc.Err = "已取消"; return oc; }
                string kit = EnsureKit(log);
                if (Cancelled) { oc.Err = "已取消"; return oc; }
                string quantize = "";
                if (!IsDirectOuttype(req.OutType)) { quantize = EnsureQuantizer(log); }
                if (Cancelled) { oc.Err = "已取消"; return oc; }

                PatchTokenizer(req, log);

                // 1) convert_hf_to_gguf.py：先出 f16（目标为 Q4/Q5/Q6 时的中间产物）
                string intermediate = Path.Combine(Root, "tmp", Safe(req.Repo) + "-" + Safe(req.QuantName) + "-" + (IsDirectOuttype(req.OutType) ? req.OutType : "f16") + ".gguf");
                MKDir(Path.GetDirectoryName(intermediate));
                if (File.Exists(intermediate)) TryDel(intermediate);

                string srcDir = Path.GetDirectoryName(req.SourceFiles[0]);
                StringBuilder args = new StringBuilder();
                args.Append('"').Append(Path.Combine(kit, "convert_hf_to_gguf.py")).Append("\" ");
                args.Append('"').Append(srcDir).Append("\" ");
                args.Append("--outfile \"").Append(IsDirectOuttype(req.OutType) ? Path.Combine(intermediate) : intermediate).Append("\" ");
                args.Append("--outtype ").Append(IsDirectOuttype(req.OutType) ? req.OutType : "f16");

                log("转换中：convert_hf_to_gguf.py --outtype " + (IsDirectOuttype(req.OutType) ? req.OutType : "f16"));
                Dictionary<string, string> env = PyEnv(kit);
                int rc = RunProc(py, args.ToString(), kit, env, log, 0);
                if (Cancelled) { oc.Err = "已取消"; return oc; }
                if (rc != 0 || !File.Exists(intermediate))
                {
                    oc.Err = "转换脚本失败（退出码 " + rc + "）；如果这是新架构模型，llama.cpp 可能还不支持";
                    return oc;
                }
                log("转换产物：" + intermediate + "   " + Human(new FileInfo(intermediate).Length));

                // 2) 需要 Q4_K_M / Q5_K_M / Q6_K 时，用 llama-quantize 量化
                string final = Path.Combine(req.OutDir, OutName(req));
                if (File.Exists(final)) TryDel(final);
                if (IsDirectOuttype(req.OutType))
                {
                    MoveTo(intermediate, final);
                }
                else
                {
                    log("量化中：" + req.OutType + " → " + req.QuantName);
                    string qargs = "\"" + intermediate + "\" \"" + final + "\" " + req.QuantName;
                    int qrc = RunProc(quantize, qargs, Path.GetDirectoryName(quantize), null, log, 0);
                    if (Cancelled) { oc.Err = "已取消"; return oc; }
                    if (qrc != 0 || !File.Exists(final))
                    {
                        oc.Err = "量化失败（退出码 " + qrc + "）；中间 f16 产物保留在 " + intermediate;
                        return oc;
                    }
                    TryDel(intermediate);
                }

                if (!File.Exists(final)) { oc.Err = "转换结束但没有生成 GGUF：" + final; return oc; }
                log("完成：" + final + "   " + Human(new FileInfo(final).Length));
                oc.Ok = true; oc.GgufPath = final;
                return oc;
            }
            catch (Exception ex)
            {
                oc.Err = "转换异常：" + ex.Message;
                return oc;
            }
        }

        static bool IsDirectOuttype(string ot)
        {
            return string.Equals(ot, "bf16", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ot, "q8_0", StringComparison.OrdinalIgnoreCase);
        }

        public static string OutName(ConvertRequest req)
        {
            return Safe(req.Repo.Replace('/', '-')) + "-" + Safe(req.QuantName) + ".gguf";
        }

        // ==================== uv ====================
        static string EnsureUv(Proc log)
        {
            string exe = Path.Combine(Root, "uv", "uv.exe");
            if (File.Exists(exe)) return exe;
            MKDir(Path.GetDirectoryName(exe));
            string zip = Path.Combine(Root, "uv.zip");
            string[] urls = new string[] {
                "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip",
                "https://ghfast.top/https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip",
                "https://mirror.ghproxy.com/https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip"
            };
            log("首次使用：下载 uv（约 15 MB）");
            if (!DownloadAny(urls, zip, log)) throw new Exception("uv 下载失败，检查网络后重试");
            string dir = Path.Combine(Root, "uv");
            Unzip(zip, dir, log);
            TryDel(zip);
            if (!File.Exists(exe))
            {
                string found = FindFile(dir, "uv.exe");
                if (found != null) exe = found;
            }
            if (!File.Exists(exe)) throw new Exception("uv 解压后没有找到 uv.exe");
            log("uv 就绪：" + exe);
            return exe;
        }

        // ==================== Python ====================
        static string EnsurePython(string uv, Proc log)
        {
            string py = Path.Combine(Root, "py", "Scripts", "python.exe");
            if (File.Exists(py)) return py;
            string venv = Path.Combine(Root, "py");
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["UV_PYTHON_INSTALL_MIRROR"] = "https://registry.npmmirror.com/-/binary/python-build-standalone";
            env["UV_NO_CONFIG"] = "1";
            env["UV_HTTP_TIMEOUT"] = "300";
            log("首次使用：准备便携 Python " + PyVersion + "（约 30 MB）");
            RunProc(uv, "python install " + PyVersion, Root, env, log, 1200);
            int rc = RunProc(uv, "venv \"" + venv + "\" --python " + PyVersion, Root, env, log, 600);
            if (!File.Exists(py)) throw new Exception("便携 Python 创建失败（uv 退出码 " + rc + "）");
            log("Python 就绪：" + py);
            return py;
        }

        // ==================== 依赖 ====================
        static readonly string[] PyRequirements = new string[] {
            "numpy", "sentencepiece", "transformers>=4.45.0", "safetensors", "pyyaml", "tqdm", "protobuf"
        };

        static void EnsurePyDeps(string uv, string py, Proc log)
        {
            string marker = Path.Combine(Root, "py", ".deps_ok");
            if (File.Exists(marker)) return;

            Dictionary<string, string> env = new Dictionary<string, string>();
            env["UV_NO_CONFIG"] = "1"; env["UV_HTTP_TIMEOUT"] = "300";

            bool torchOk = Directory.Exists(Path.Combine(Root, "py", "Lib", "site-packages", "torch"));
            if (!torchOk)
            {
                log("首次使用：安装 PyTorch CPU（约 250 MB，仅首次）");
                string[][] idx = new string[][] {
                    new string[] { "--index-url", "https://download.pytorch.org/whl/cpu" },
                    new string[] { "--index-url", "https://pypi.tuna.tsinghua.edu.cn/simple" },
                    new string[] { "--index-url", "https://pypi.org/simple" }
                };
                foreach (string[] src in idx)
                {
                    if (Cancelled) return;
                    log("  torch 源：" + src[1]);
                    int rc = RunProc(uv, "pip install --python \"" + py + "\" " + src[0] + " " + src[1] + " torch", Root, env, log, 3600);
                    torchOk = Directory.Exists(Path.Combine(Root, "py", "Lib", "site-packages", "torch"));
                    if (torchOk) break;
                }
                if (!torchOk) log("警告：PyTorch 安装未成功，转换会在 import torch 处失败");
            }

            string reqFile = Path.Combine(Root, "requirements-convert.txt");
            File.WriteAllText(reqFile, string.Join("\r\n", PyRequirements), new UTF8Encoding(false));
            log("安装转换脚本依赖：" + string.Join(", ", PyRequirements));
            int rc2 = RunProc(uv, "pip install --python \"" + py + "\" -r \"" + reqFile + "\"", Root, env, log, 3600);
            if (rc2 != 0) log("警告：部分依赖安装失败（退出码 " + rc2 + "）");
            File.WriteAllText(marker, DateTime.Now.ToString("s"), new UTF8Encoding(false));
        }

        // ==================== llama.cpp 转换脚本 ====================
        static string EnsureKit(Proc log)
        {
            string kit = Path.Combine(Root, "kit");
            string script = Path.Combine(kit, "convert_hf_to_gguf.py");
            if (File.Exists(script) && Directory.Exists(Path.Combine(kit, "gguf-py"))) return kit;
            MKDir(kit);

            string[] urls = new string[] {
                "https://github.com/ggml-org/llama.cpp/archive/refs/heads/master.tar.gz",
                "https://ghfast.top/https://github.com/ggml-org/llama.cpp/archive/refs/heads/master.tar.gz",
                "https://mirror.ghproxy.com/https://github.com/ggml-org/llama.cpp/archive/refs/heads/master.tar.gz",
                "https://hf-mirror.com/ggml-org/llama.cpp/resolve/main/convert_hf_to_gguf.py"
            };
            string tgz = Path.Combine(Root, "llama.cpp.tar.gz");
            log("首次使用：下载 llama.cpp 转换脚本（约 20 MB）");
            bool ok = false;
            for (int i = 0; i < 3 && !ok; i++)
            {
                if (Cancelled) throw new Exception("已取消");
                if (!Download(urls[i], tgz, log)) continue;
                try { TarGzExtract(tgz, kit, log); ok = File.Exists(script); }
                catch (Exception ex) { log("解包失败：" + ex.Message); }
            }
            TryDel(tgz);

            if (!File.Exists(script))
            {
                // 兜底：单文件取 convert_hf_to_gguf.py（gguf-py 缺失时会给出明确提示）
                if (!Download(urls[3], script, log))
                    throw new Exception("llama.cpp 转换脚本下载失败，检查网络后重试");
            }
            if (!Directory.Exists(Path.Combine(kit, "gguf-py")))
                log("警告：没有 gguf-py 目录，脚本可能报 ModuleNotFoundError: gguf");
            log("转换脚本就绪：" + script);
            return kit;
        }

        // release tag 候选：releases/latest 给出的是别名 tag（如 v1.0.0），
        // 真正的构建 tag 写在它的 nightly-tag.txt 里（如 b12359），资产名形如 llama-b12359-bin-win-cpu-x64.zip
        static List<string> CandidateTags(Proc log)
        {
            List<string> tags = new List<string>();
            string alias = LatestTag(log);
            if (alias.Length > 0)
            {
                try
                {
                    string nightly = HttpGet("https://github.com/ggml-org/llama.cpp/releases/download/" + alias + "/nightly-tag.txt", 15000).Trim();
                    if (nightly.Length > 0 && nightly.Length < 64 && nightly.IndexOf('\n') < 0)
                    {
                        log("llama.cpp 构建 tag：" + nightly + "（别名 " + alias + "）");
                        tags.Add(nightly);
                    }
                }
                catch (Exception ex) { log("读取 nightly-tag 失败：" + ex.Message); }
                if (!tags.Contains(alias)) tags.Add(alias);
            }
            return tags;
        }

        // 取 llama.cpp 最新 release tag：靠 releases/latest 的 302 跳转地址，不依赖 GitHub API
        static string LatestTag(Proc log)
        {
            string[] probes = new string[] {
                "https://github.com/ggml-org/llama.cpp/releases/latest",
                "https://ghfast.top/https://github.com/ggml-org/llama.cpp/releases/latest"
            };
            foreach (string u in probes)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(u);
                    req.UserAgent = "LocModelDL/2.4.2";
                    req.Timeout = 15000;
                    req.AllowAutoRedirect = false;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        string loc = resp.Headers["Location"];
                        if (!string.IsNullOrEmpty(loc))
                        {
                            int i = loc.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
                            if (i > 0)
                            {
                                string t = loc.Substring(i + 5).Trim('/');
                                int q = t.IndexOfAny(new char[] { '?', '#' });
                                if (q > 0) t = t.Substring(0, q);
                                if (t.Length > 0) { log("llama.cpp 最新版本：" + t); return t; }
                            }
                        }
                    }
                }
                catch (WebException wex)
                {
                    try
                    {
                        HttpWebResponse r2 = wex.Response as HttpWebResponse;
                        if (r2 != null)
                        {
                            string loc = r2.Headers["Location"];
                            if (!string.IsNullOrEmpty(loc))
                            {
                                int i = loc.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
                                if (i > 0) { string t = loc.Substring(i + 5).Trim('/'); if (t.Length > 0) { log("llama.cpp 最新版本：" + t); return t; } }
                            }
                        }
                    }
                    catch { }
                    log("查询 llama.cpp 版本失败：" + wex.Message);
                }
                catch (Exception ex) { log("查询 llama.cpp 版本失败：" + ex.Message); }
            }
            return "";
        }

        // ==================== llama-quantize ====================
        static string EnsureQuantizer(Proc log)
        {
            string bin = Path.Combine(Root, "bin");
            MKDir(bin);
            string exe = Path.Combine(bin, "llama-quantize.exe");
            if (File.Exists(exe)) return exe;

            List<string> tags = CandidateTags(log);
            List<string> urls = new List<string>();
            foreach (string t in tags)
            {
                string n = "llama-" + t + "-bin-win-cpu-x64.zip";
                urls.Add("https://github.com/ggml-org/llama.cpp/releases/download/" + t + "/" + n);
                urls.Add("https://ghfast.top/https://github.com/ggml-org/llama.cpp/releases/download/" + t + "/" + n);
            }

            log("首次使用：下载 llama-quantize（约 40 MB）");
            string zip = Path.Combine(Root, "llama-bin.zip");
            bool got = false;
            foreach (string u in urls)
            {
                if (Cancelled) throw new Exception("已取消");
                if (Download(u, zip, log)) { got = true; break; }
            }
            if (!got) throw new Exception("llama-quantize 下载失败；可以先选 BF16 或 Q8_0（无需量化器）");
            Unzip(zip, bin, log);
            TryDel(zip);
            if (!File.Exists(exe))
            {
                string found = FindFile(bin, "llama-quantize.exe");
                if (found != null) exe = found;
            }
            if (!File.Exists(exe)) throw new Exception("压缩包里没有 llama-quantize.exe");
            log("量化器就绪：" + exe);
            return exe;
        }

        // ==================== tokenizer / config 兼容修补 ====================
        static void PatchTokenizer(ConvertRequest req, Proc log)
        {
            string dir = Path.GetDirectoryName(req.SourceFiles[0]);
            string cfg = Path.Combine(dir, "tokenizer_config.json");
            if (File.Exists(cfg) && !File.Exists(cfg + ".orig"))
            {
                try
                {
                    string txt = File.ReadAllText(cfg, Encoding.UTF8);
                    string patched = txt;
                    if (patched.IndexOf("\"auto_map\"", StringComparison.Ordinal) >= 0)
                    {
                        // 自定义 tokenizer 类会让 transformers 走 remote code，转换时通常失败
                        int a = patched.IndexOf("\"auto_map\"", StringComparison.Ordinal);
                        int depth = 0, end = a;
                        for (int i = a; i < patched.Length; i++)
                        {
                            if (patched[i] == '{') depth++;
                            else if (patched[i] == '}') { depth--; if (depth == 0) { end = i + 1; break; } }
                        }
                        if (end > a) patched = patched.Remove(a, end - a).Replace(",}", "}");
                    }
                    if (patched.IndexOf("\"tokenizer_class\"", StringComparison.Ordinal) < 0)
                    {
                        int lb = patched.LastIndexOf('}');
                        if (lb > 0) patched = patched.Insert(lb, ",\r\n  \"tokenizer_class\": \"PreTrainedTokenizerFast\"\r\n");
                    }
                    if (!string.Equals(patched, txt, StringComparison.Ordinal))
                    {
                        File.Copy(cfg, cfg + ".orig", true);
                        File.WriteAllText(cfg, patched, new UTF8Encoding(false));
                        log("已修补 tokenizer_config.json（原文件备份为 .orig）");
                    }
                }
                catch (Exception ex) { log("tokenizer_config.json 修补失败：" + ex.Message); }
            }

            string cj = Path.Combine(dir, "config.json");
            if (File.Exists(cj))
            {
                try
                {
                    string txt = File.ReadAllText(cj, Encoding.UTF8);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    Dictionary<string, object> obj = js.Deserialize<Dictionary<string, object>>(txt);
                    string arch = "";

                    if (obj.ContainsKey("architectures") && obj["architectures"] is object[] && ((object[])obj["architectures"]).Length > 0)
                        arch = Convert.ToString(((object[])obj["architectures"])[0]);
                    bool vision = obj.ContainsKey("vision_config");
                    log("模型架构：" + (arch.Length > 0 ? arch : "(未标注)") + (vision ? "  含视觉塔" : ""));
                    if (vision)
                        log("提示：这是视觉模型，转换只会导出语言部分；视觉投影（mmproj）暂不支持");

                    // v2.4.1：不少 HF 模型把 pad/bos/eos 等 id 写成 -1，gguf-py 的 SpecialVocab 会直接抛
                    // "invalid value for special token type pad: -1"，转换前把这些非法值删掉，脚本会自动探测。
                    string[] idKeys = { "pad_token_id", "bos_token_id", "eos_token_id", "sep_token_id", "cls_token_id", "mask_token_id" };
                    bool dirty = false;
                    foreach (string k in idKeys)
                    {
                        if (!obj.ContainsKey(k) || obj[k] == null) continue;
                        int v;
                        if (int.TryParse(Convert.ToString(obj[k]), out v) && v < 0) { obj.Remove(k); dirty = true; }
                    }
                    if (dirty)
                    {
                        if (!File.Exists(cj + ".orig")) File.Copy(cj, cj + ".orig", true);
                        File.WriteAllText(cj, js.Serialize(obj), new UTF8Encoding(false));
                        log("已清理 config.json 里非法的特殊 token id（原文件备份为 .orig）");
                    }
                }
                catch { }
            }
        }

        // ==================== 进程 ====================
        static Dictionary<string, string> PyEnv(string kit)
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["HF_HUB_OFFLINE"] = "1";
            env["TRANSFORMERS_OFFLINE"] = "1";
            env["HF_HUB_DISABLE_TELEMETRY"] = "1";
            env["NO_LOCAL_GGUF"] = "";
            env["PYTHONUTF8"] = "1";
            env["PYTHONIOENCODING"] = "utf-8";
            env["PYTHONUNBUFFERED"] = "1";
            env["PYTHONPATH"] = Path.Combine(kit, "gguf-py");
            return env;
        }

        static int RunProc(string exe, string args, string workDir, Dictionary<string, string> env, Proc log, int timeoutSec)
        {
            Process p = new Process();
            p.StartInfo.FileName = exe;
            p.StartInfo.Arguments = args;
            p.StartInfo.WorkingDirectory = string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir) ? Root : workDir;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            if (env != null) foreach (KeyValuePair<string, string> kv in env) { try { p.StartInfo.EnvironmentVariables[kv.Key] = kv.Value; } catch { } }
            try { p.Start(); }
            catch (Exception ex) { log("启动失败：" + ex.Message); return -1; }

            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) log(e.Data); };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) log(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            Stopwatch sw = Stopwatch.StartNew();
            while (!p.HasExited)
            {
                if (Cancelled) { try { p.Kill(); } catch { } log("已终止进程"); return -2; }
                if (timeoutSec > 0 && sw.Elapsed.TotalSeconds > timeoutSec)
                {
                    try { p.Kill(); } catch { }
                    log("进程超时（" + timeoutSec + " 秒），已终止");
                    return -3;
                }
                Thread.Sleep(200);
            }
            p.WaitForExit(2000);
            return p.ExitCode;
        }

        // ==================== HTTP / 解压 ====================
        static bool DownloadAny(string[] urls, string dest, Proc log)
        {
            foreach (string u in urls)
            {
                if (Cancelled) return false;
                if (Download(u, dest, log)) return true;
            }
            return false;
        }

        static bool Download(string url, string dest, Proc log)
        {
            try
            {
                if (File.Exists(dest)) TryDel(dest);
                MKDir(Path.GetDirectoryName(dest));
                log("下载 " + url);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "LocModelDL/2.4.2";
                req.Timeout = 30000;
                req.ReadWriteTimeout = 120000;
                req.AllowAutoRedirect = true;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buf = new byte[262144];
                    long total = resp.ContentLength, done = 0;
                    Stopwatch sw = Stopwatch.StartNew();
                    long lastLog = 0;
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (Cancelled) { return false; }
                        fs.Write(buf, 0, n);
                        done += n;
                        if (sw.ElapsedMilliseconds - lastLog > 2000)
                        {
                            lastLog = sw.ElapsedMilliseconds;
                            double sp = sw.Elapsed.TotalSeconds > 0 ? done / sw.Elapsed.TotalSeconds : 0;
                            log("  " + Human(done) + (total > 0 ? " / " + Human(total) : "") + "   " + Spd(sp));
                        }
                    }
                }
                long len = new FileInfo(dest).Length;
                if (len < 1024) { log("下载内容过小，视为失败（" + len + " B）"); return false; }
                return true;
            }
            catch (Exception ex)
            {
                log("下载失败：" + ex.Message + "  ← " + url);
                return false;
            }
        }

        static string HttpGet(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "LocModelDL/2.4.2";
            req.Timeout = timeoutMs;
            req.AllowAutoRedirect = true;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        static void Unzip(string zip, string destDir, Proc log)
        {
            MKDir(destDir);
            using (ZipArchive za = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    string target = Path.Combine(destDir, e.FullName.Replace('/', Path.DirectorySeparatorChar));
                    MKDir(Path.GetDirectoryName(target));
                    try { e.ExtractToFile(target, true); }
                    catch (Exception ex) { log("解压跳过 " + e.FullName + "：" + ex.Message); }
                }
            }
        }

        // 极简 USTAR 解包（只取普通文件，保留相对路径）
        static void TarGzExtract(string tgz, string destDir, Proc log)
        {
            MKDir(destDir);
            using (FileStream fs = new FileStream(tgz, FileMode.Open, FileAccess.Read))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                byte[] hdr = new byte[512];
                while (true)
                {
                    if (!ReadFull(gz, hdr, 512)) break;
                    bool empty = true;
                    for (int i = 0; i < 512; i++) if (hdr[i] != 0) { empty = false; break; }
                    if (empty) break;

                    string name = CStr(hdr, 0, 100);
                    string prefix = CStr(hdr, 345, 155);
                    long size = Oct(hdr, 124, 12);
                    char type = (char)hdr[156];
                    if (prefix.Length > 0) name = prefix + "/" + name;

                    // 去掉顶层目录 llama.cpp-master/
                    int sl = name.IndexOf('/');
                    string rel = sl > 0 ? name.Substring(sl + 1) : name;

                    bool keep = rel.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
                        || rel.StartsWith("gguf-py/", StringComparison.Ordinal) && !rel.EndsWith("/", StringComparison.Ordinal);
                    if ((type == '0' || type == 0) && size > 0 && keep && rel.Length > 0)
                    {
                        string target = Path.Combine(destDir, rel.Replace('/', Path.DirectorySeparatorChar));
                        MKDir(Path.GetDirectoryName(target));
                        using (FileStream of = new FileStream(target, FileMode.Create, FileAccess.Write))
                        {
                            long left = size;
                            byte[] buf = new byte[65536];
                            while (left > 0)
                            {
                                int want = (int)Math.Min(buf.Length, left);
                                int got = gz.Read(buf, 0, want);
                                if (got <= 0) break;
                                of.Write(buf, 0, got);
                                left -= got;
                            }
                        }
                    }
                    else
                    {
                        long left = size;
                        byte[] buf = new byte[65536];
                        while (left > 0)
                        {
                            int want = (int)Math.Min(buf.Length, left);
                            int got = gz.Read(buf, 0, want);
                            if (got <= 0) break;
                            left -= got;
                        }
                    }
                    // 512 对齐填充
                    long pad = (512 - (size % 512)) % 512;
                    long skip = pad;
                    byte[] junk = new byte[512];
                    while (skip > 0)
                    {
                        int want = (int)Math.Min(junk.Length, skip);
                        if (!ReadFull(gz, junk, want)) break;
                        skip -= want;
                    }
                }
            }
            log("已解包 llama.cpp 转换脚本");
        }

        static bool ReadFull(Stream s, byte[] buf, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n = s.Read(buf, off, count - off);
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }

        static string CStr(byte[] b, int off, int len)
        {
            int end = off;
            while (end < off + len && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, off, end - off).Trim();
        }

        static long Oct(byte[] b, int off, int len)
        {
            long v = 0; bool any = false;
            for (int i = off; i < off + len; i++)
            {
                if (b[i] == 0) { if (any) break; else continue; }
                if (b[i] < '0' || b[i] > '7') break;
                v = v * 8 + (b[i] - '0'); any = true;
            }
            return v;
        }

        // ==================== 小工具 ====================
        static void MKDir(string d) { try { if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d); } catch { } }

        static void TryDel(string f)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        static void MoveTo(string src, string dst)
        {
            try
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
            catch
            {
                File.Copy(src, dst, true);
                TryDel(src);
            }
        }

        static string FindFile(string dir, string name)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, name, SearchOption.AllDirectories)) return f;
            }
            catch { }
            return null;
        }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "model";
            StringBuilder sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in s) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string r = sb.ToString().Trim().TrimEnd('.');
            return r.Length == 0 ? "model" : r;
        }

        public static string Human(long b)
        {
            if (b < 0) return "未知";
            double v = b;
            string[] u = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + u[i];
        }

        static string Spd(double bps)
        {
            if (bps <= 0) return "";
            return Human((long)bps) + "/s";
        }
    }
}
