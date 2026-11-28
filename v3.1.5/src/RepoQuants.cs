// LocModel Downloader v2.4 - 仓库量化档解析（P2：等价 LocModel repo_quants 的增强版）
// 输入 owner/name，输出可下载的量化档列表（含分片合并、mmproj 单列、真实字节数）。
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LocModelDL
{
    public class QuantGroup
    {
        public string Name = "";       // Q4_K_M / mmproj / 未知名
        public string Kind = "quant";  // quant | mmproj | other
        public List<string> Files = new List<string>();
        public List<long> Sizes = new List<long>();
        public long Bytes = 0;
        public bool Sharded = false;
        public string Note = "";
        // v2.4.1：仓库没有 GGUF 时给出的"需转换"档（源文件 = safetensors + 配置）
        public bool Convert = false;
        public string OutType = "";    // convert 档使用的 llama.cpp 输出类型：f16 / bf16 / q8_0 / auto
        public string QuantName = "";  // 转换后再量化的目标类型：Q4_K_M / Q5_K_M / Q6_K / Q8_0 / BF16
        public long EstBytes = 0;      // 转换产物体积估算（用于空间预检）

        public static string Human(long bytes)
        {
            if (bytes <= 0) return "大小未知";
            double v = bytes;
            string[] u = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(v >= 100 ? "0" : "0.00") + " " + u[i];
        }

        public string Line()
        {
            string s = Name;
            if (Kind == "mmproj") s += "（视觉投影）";
            if (Convert) s += "  需转换";
            s += "  " + Human(Bytes);
            if (Convert && EstBytes > 0) s += "  → 约 " + Human(EstBytes);
            if (Sharded) s += "  " + Files.Count + " 片";
            if (Note.Length > 0) s += "  " + Note;
            return s;
        }
    }

    public static class RepoQuants
    {
        // 量化名：Q4_K_M / IQ3_XS / Q8_0 / BF16 / F16 / F32 / MXFP4 / TQ1_0 …
        static readonly Regex QuantRx = new Regex(
            @"(?:^|[-_.])((?:IQ|TQ|Q)\d+(?:_[A-Za-z0-9]+)*|BF16|FP16|F16|F32|MXFP4|W8A16)\.gguf$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex ShardRx = new Regex(@"-(\d{5})-of-(\d{5})\.gguf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 推荐优先级：质量/体积平衡档优先，未命中的排后
        static readonly string[] Prefer = new string[] {
            "Q4_K_M", "Q4_K_S", "Q5_K_M", "Q5_K_S", "Q3_K_M", "Q6_K", "Q8_0",
            "IQ4_XS", "IQ4_NL", "IQ3_M", "IQ3_XS", "IQ2_M", "Q2_K", "BF16", "F16"
        };

        public static List<QuantGroup> Parse(string repo, out string err)
        {
            err = "";
            List<QuantGroup> outl = new List<QuantGroup>();
            if (string.IsNullOrEmpty(repo)) { err = "EMPTY|仓库为空"; return outl; }

            // v2.4.1：走带真实字节数与错误分类的清单接口（不再依赖 /tree/main）
            CatalogService.RepoFiles rf = CatalogService.GetRepoFiles(repo);
            if (!rf.Ok)
            {
                err = ErrCode(rf);
                return outl;
            }

            string[] sibs = rf.Paths.ToArray();
            Dictionary<string, long> sizes = rf.Sizes.Count > 0 ? rf.Sizes : CatalogService.GetFileSizes(repo);

            Dictionary<string, QuantGroup> map = new Dictionary<string, QuantGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in sibs)
            {
                string bn = BaseName(f);
                if (!bn.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;

                bool isMmproj = bn.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase);
                string nameForMatch = bn;
                Match sh = ShardRx.Match(bn);
                if (sh.Success) nameForMatch = bn.Substring(0, bn.Length - sh.Length) + ".gguf";

                string key;
                if (isMmproj) key = "mmproj";
                else
                {
                    Match qm = QuantRx.Match(nameForMatch);
                    key = qm.Success ? qm.Groups[1].Value.ToUpperInvariant() : StripExt(nameForMatch);
                }

                QuantGroup g;
                if (!map.TryGetValue(key, out g))
                {
                    g = new QuantGroup();
                    g.Name = key;
                    g.Kind = isMmproj ? "mmproj" : "quant";
                    map[key] = g;
                }
                g.Files.Add(f);
                long sz = 0;
                if (sizes != null) sizes.TryGetValue(f, out sz);
                g.Sizes.Add(sz);
                g.Bytes += sz;
                if (sh.Success) g.Sharded = true;
                if (g.Kind == "quant" && !qm_hit(nameForMatch) && g.Note.Length == 0) g.Note = "未识别量化名";
            }

            foreach (KeyValuePair<string, QuantGroup> kv in map)
            {
                QuantGroup g = kv.Value;
                // 分片按序号排序，保证下载顺序稳定
                SortShards(g);
                if (g.Bytes <= 0) g.Bytes = 0;
                outl.Add(g);
            }

            outl.Sort(delegate(QuantGroup a, QuantGroup b)
            {
                int ra = Rank(a), rb = Rank(b);
                if (ra != rb) return ra.CompareTo(rb);
                return b.Bytes.CompareTo(a.Bytes);
            });

            // 有 GGUF → 直接给量化档；没有 GGUF 但有 safetensors → 给"需转换"档；两者都没有 → 报 NO_GGUF
            bool hasQuant = false;
            foreach (QuantGroup g in outl) if (g.Kind == "quant") { hasQuant = true; break; }
            if (!hasQuant)
            {
                List<QuantGroup> conv = ConvertPlans(rf);
                if (conv.Count > 0) return conv;
                outl.Clear();
                err = "NO_GGUF|" + repo + " 既没有 GGUF 也没有 safetensors 权重，没有可转换的东西";
            }
            return outl;
        }

        // ---- 错误码：界面据此给出可执行的提示 ----
        static string ErrCode(CatalogService.RepoFiles rf)
        {
            string repo = rf.Repo;
            switch (rf.ErrKind)
            {
                case "notfound": return "NOTFOUND|" + repo + " 不存在，检查 owner/name 是否拼错";
                case "auth": return "AUTH|" + repo + " 是私有或受限仓库，需要授权才能读文件清单";
                case "empty": return "EMPTY|" + repo + " 是空仓库，里面没有任何文件";
            }
            return "NET|读取 " + repo + " 的文件清单失败：" + (rf.Err.Length > 0 ? rf.Err : "网络不可用");
        }

        // ---- 转换档（A 路线）：convert_hf_to_gguf.py 转出 → llama-quantize 量化 ----
        // 名称 / 目标量化类型 / 每参数字节估算
        static readonly string[][] ConvertSpecs = new string[][] {
            new string[] { "Q4_K_M", "q4_k_m", "0.60" },
            new string[] { "Q5_K_M", "q5_k_m", "0.72" },
            new string[] { "Q6_K",   "q6_k",   "0.84" },
            new string[] { "Q8_0",   "q8_0",   "1.06" },
            new string[] { "BF16",   "bf16",   "2.00" }
        };

        public static List<QuantGroup> ConvertPlans(CatalogService.RepoFiles rf)
        {
            List<QuantGroup> outl = new List<QuantGroup>();
            if (rf == null || !rf.Ok) return outl;
            List<string> src = SourceFiles(rf);
            if (src.Count == 0) return outl;
            long srcBytes = rf.Sum(src);
            double paramsB = srcBytes > 0 ? srcBytes / 2.0 : 0;   // 源权重按 fp16/bf16 处理

            foreach (string[] spec in ConvertSpecs)
            {
                QuantGroup g = new QuantGroup();
                g.Name = spec[0];
                g.Kind = "quant";
                g.Convert = true;
                g.OutType = spec[0] == "BF16" ? "bf16" : (spec[0] == "Q8_0" ? "q8_0" : "f16");
                g.QuantName = spec[0];
                g.Files = new List<string>(src);
                g.Sizes = new List<long>();
                foreach (string f in src) { long v = 0; rf.Sizes.TryGetValue(f, out v); g.Sizes.Add(v); }
                g.Bytes = srcBytes;
                double bpp = 0;
                try { bpp = double.Parse(spec[2], System.Globalization.CultureInfo.InvariantCulture); }
                catch { }
                g.EstBytes = paramsB > 0 ? (long)(paramsB * bpp) : 0;
                g.Note = "下载后本地转换";
                outl.Add(g);
            }
            return outl;
        }

        // 转换所需源文件：safetensors 权重 + tokenizer/config 等元数据
        static readonly string[] MetaNames = new string[] {
            "config.json", "generation_config.json", "tokenizer.json", "tokenizer_config.json",
            "special_tokens_map.json", "vocab.json", "merges.txt", "tokenizer.model",
            "added_tokens.json", "chat_template.jinja", "chat_template.json",
            "preprocessor_config.json", "processor_config.json",
            "model.safetensors.index.json", "pytorch_model.bin.index.json"
        };

        static bool IsMeta(string p)
        {
            string bn = BaseName(p);
            foreach (string m in MetaNames) if (string.Equals(bn, m, StringComparison.OrdinalIgnoreCase)) return true;
            if (bn.StartsWith("configuration_", StringComparison.OrdinalIgnoreCase) && bn.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return true;
            if (bn.StartsWith("image_processor", StringComparison.OrdinalIgnoreCase)) return true;
            if (bn.StartsWith("tokenization_", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<string> SourceFiles(CatalogService.RepoFiles rf)
        {
            List<string> l = new List<string>();
            if (rf == null) return l;
            foreach (string p in rf.Paths)
            {
                string bn = BaseName(p);
                if (bn.StartsWith(".", StringComparison.Ordinal)) continue;
                if (bn.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)) { l.Add(p); continue; }
                if (bn.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                    && bn.IndexOf("pytorch_model", StringComparison.OrdinalIgnoreCase) >= 0) { l.Add(p); continue; }
                if (IsMeta(p)) l.Add(p);
            }
            return l;
        }

        static bool qm_hit(string s) { return QuantRx.IsMatch(s); }

        static int Rank(QuantGroup g)
        {
            if (g.Kind == "mmproj") return 900;
            for (int i = 0; i < Prefer.Length; i++)
                if (string.Equals(g.Name, Prefer[i], StringComparison.OrdinalIgnoreCase)) return i;
            return 500;
        }

        static void SortShards(QuantGroup g)
        {
            if (g.Files.Count < 2) return;
            List<KeyValuePair<string, long>> pairs = new List<KeyValuePair<string, long>>();
            for (int i = 0; i < g.Files.Count; i++) pairs.Add(new KeyValuePair<string, long>(g.Files[i], g.Sizes[i]));
            pairs.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                int ia = ShardIndex(a.Key), ib = ShardIndex(b.Key);
                if (ia != ib) return ia.CompareTo(ib);
                return string.CompareOrdinal(a.Key, b.Key);
            });
            g.Files.Clear(); g.Sizes.Clear();
            foreach (KeyValuePair<string, long> p in pairs) { g.Files.Add(p.Key); g.Sizes.Add(p.Value); }
        }

        static int ShardIndex(string path)
        {
            Match m = ShardRx.Match(BaseName(path));
            if (!m.Success) return 0;
            int v;
            return int.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        // 首选量化：列表已按偏好排序，第一个 quant 类即首选
        public static QuantGroup Recommend(List<QuantGroup> list)
        {
            if (list == null) return null;
            foreach (QuantGroup g in list) if (g.Kind == "quant") return g;
            return null;
        }

        // 直接给 DownloadItem 列表需要的仓库内路径
        public static List<string> FilesOf(QuantGroup g)
        {
            if (g == null) return new List<string>();
            return new List<string>(g.Files);
        }

        // 目标文件名（分片保留原名，单片取原名）
        public static string DestFileName(QuantGroup g, int index)
        {
            if (g == null || index < 0 || index >= g.Files.Count) return "";
            return BaseName(g.Files[index]);
        }

        static string StripExt(string s)
        {
            int i = s.LastIndexOf('.');
            return i > 0 ? s.Substring(0, i) : s;
        }

        static string BaseName(string p) { int i = p.LastIndexOf('/'); return i < 0 ? p : p.Substring(i + 1); }

        static string Short(string s) { if (s == null) return ""; return s.Length > 100 ? s.Substring(0, 100) : s; }
    }
}
