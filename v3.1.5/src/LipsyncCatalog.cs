// LocModel Downloader v3.1.5 - 对口型子系统（lipsync）清单与落盘规则
// =====================================================================================
// 【独立成段说明】
//   本文件是 v3.1.5 唯一「新增整文件」的改动点，对口型子系统的全部专属知识都收在这里：
//     ① 权重清单（仓库 / 仓库内路径 / 落盘相对路径 / 期望字节）
//     ② 落盘根目录规则与目录结构约定
//     ③ 非 HuggingFace 通道（程序包、人声分离模型）的地址与顺序
//     ④ 站点覆盖情况说明（供界面注明「对口型模型用」）
//   回滚方式：删除本文件 + 删除 LipsyncForm.cs + 还原 MainForm.cs 中三处 lip- 标记块，
//   内核（Downloader / DownloadSeg / 校验 / 续传）与其它版本档位完全不受影响。
//
// 【清单来源与验证（2026-11-28）】
//   - 清单取自本机 LocModel 3.6.5 主程序 exe 内嵌数据（版本文件时间 2026-11-26 19:42），
//     逐条解析 (repo, repo内路径, 落盘路径) 三元组，期望字节与 HF 仓库单文件字节一致。
//   - 国内镜像核对：站点 https://hf-mirror.com 对下列仓库的下列路径实测返回 200 且
//     字节数与清单一致 → 权重无需另设站点，站点栏位保持默认 hf-mirror.com 即可。
//   - 例外（不在 HuggingFace，hf-mirror 不涵盖）：程序包（GitHub/ModelScope 分发）、
//     人声分离模型（dl.fbaipublicfiles.com）。二者用绝对地址下载，不经过「下载站点」栏位。
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;

namespace LocModelDL
{
    // 单个对口型文件的下载项（HF 仓库 + 仓库内路径 → 落盘相对路径）
    public class LipsyncFile
    {
        public string Repo;        // 例：MeiGen-AI/InfiniteTalk
        public string SrcPath;     // 仓库内路径，例：quant_models/t5_fp8.safetensors
        public string DestRel;     // 落盘相对路径（相对对口型根目录），例：t5\t5_fp8.safetensors
        public long Size;          // 期望字节；-1 表示未知
        public string Note = "";   // 界面备注

        public LipsyncFile(string repo, string srcPath, string destRel, long size, string note)
        {
            Repo = repo; SrcPath = srcPath; DestRel = destRel; Size = size; Note = note;
        }

        public string Label
        {
            get
            {
                string n = SrcPath;
                int i = n.LastIndexOf('/');
                if (i >= 0) n = n.Substring(i + 1);
                return n;
            }
        }
    }

    // 一个档位（变体）的完整清单
    public class LipsyncVariant
    {
        public string Key;         // hq / low
        public string Label;       // 界面显示
        public string Desc;        // 说明（显存/体积）
        public List<LipsyncFile> Files = new List<LipsyncFile>();

        public LipsyncVariant(string key, string label, string desc)
        {
            Key = key; Label = label; Desc = desc;
        }

        public long TotalBytes()
        {
            long t = 0;
            foreach (LipsyncFile f in Files) if (f.Size > 0) t += f.Size;
            return t;
        }
    }

    public static class LipsyncCatalog
    {
        // 对口型子系统随 LocModel 3.6.5 引入
        public const string MinVersion = "3.6.5";

        // 落盘默认根目录名（放在主界面「模型保存位置」之下）
        public const string RootDirName = "lipsync";

        // 程序包与人声分离模型（HF 之外的通道，绝对地址）
        public const string ProgramZipName = "InfiniteTalk.zip";
        public const string DemucsFileName = "955717e8-8726e21a.th";

        public static readonly string[] ProgramSources = new string[] {
            "https://github.com/MeiGen-AI/InfiniteTalk/archive/refs/heads/main.zip",
            "https://codeload.github.com/MeiGen-AI/InfiniteTalk/zip/main"
        };

        // 站点覆盖结论（供界面注明；文字与实测一致，勿随意改口径）
        public static string SiteNote()
        {
            return "对口型模型用站点：https://hf-mirror.com（实测已涵盖对口型全部权重，下载站点栏位无需增改）；"
                 + "程序包走 github.com/MeiGen-AI/InfiniteTalk，人声分离模型走 dl.fbaipublicfiles.com（两者用绝对地址，不经过站点栏位）";
        }

        // ---------------- 目录规则 ----------------

        // LocModel 自己读取的对口型根目录：%LOCALAPPDATA%\LocModel\lipsync（程序数据目录，不是模型目录）。
        // 实测 LocModel 3.6.5 把程序包与权重都放这里，落到模型目录下 LocModel 认不出来（会报「对口型程序包损坏」）。
        public static string ProgramDataRoot()
        {
            try
            {
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(la)) return "";
                return Path.Combine(Path.Combine(la, "LocModel"), RootDirName);
            }
            catch { return ""; }
        }

        // 默认根目录：优先 LocModel 程序数据目录 lipsync；取不到才退回 {模型保存位置}\lipsync
        public static string DefaultRoot(string modelsDir)
        {
            string pd = ProgramDataRoot();
            if (!string.IsNullOrEmpty(pd)) return pd;
            if (string.IsNullOrEmpty(modelsDir)) return "";
            try { return Path.Combine(modelsDir.TrimEnd('\\', '/'), RootDirName); }
            catch { return ""; }
        }

        // 探测已存在的对口型目录：优先 LocModel 程序数据目录（%LOCALAPPDATA%\LocModel\lipsync），
        // 其次模型目录\ l ip sync。返回 "" 表示本机尚未安装对口型子系统。
        public static string DetectExistingRoot(string modelsDir)
        {
            List<string> cand = new List<string>();
            string pd = ProgramDataRoot();
            if (!string.IsNullOrEmpty(pd)) cand.Add(pd);
            if (!string.IsNullOrEmpty(modelsDir))
            {
                try { cand.Add(Path.Combine(modelsDir.TrimEnd('\\', '/'), RootDirName)); }
                catch { }
            }

            foreach (string c in cand)
            {
                try
                {
                    if (!Directory.Exists(c)) continue;
                    // 至少有一个子目录或文件才算「已安装」，避免空目录误判
                    if (Directory.GetDirectories(c).Length > 0 || Directory.GetFiles(c).Length > 0) return c;
                }
                catch { }
            }
            return "";
        }

        // 结构自检：返回缺失/不完整的相对路径列表（空列表 = 结构完整）
        public static List<string> VerifyStructure(string root, string variantKey)
        {
            List<string> bad = new List<string>();
            if (string.IsNullOrEmpty(root)) { bad.Add("（未选择对口型目录）"); return bad; }
            foreach (LipsyncFile f in FilesOf(variantKey))
            {
                string full = "";
                try { full = Path.Combine(root, f.DestRel); } catch { bad.Add(f.DestRel); continue; }
                if (!File.Exists(full)) { bad.Add(f.DestRel + "（缺失）"); continue; }
                try
                {
                    long len = new FileInfo(full).Length;
                    if (f.Size > 0 && len != f.Size)
                        bad.Add(f.DestRel + "（大小 " + len + "，应为 " + f.Size + "）");
                }
                catch { bad.Add(f.DestRel + "（不可读）"); }
            }
            return bad;
        }

        // ---------------- 清单 ----------------

        static LipsyncVariant[] Build()
        {
            // 公共部分：文本编码器 / VAE / CLIP / T5 量化 / LoRA / 人声特征提取
            List<LipsyncFile> shared = new List<LipsyncFile>();
            string W = "Wan-AI/Wan2.1-I2V-14B-480P";
            shared.Add(new LipsyncFile(W, "config.json", @"base\config.json", 250, "Wan2.1 主干配置"));
            shared.Add(new LipsyncFile(W, "Wan2.1_VAE.pth", @"base\Wan2.1_VAE.pth", 507609880L, "VAE"));
            shared.Add(new LipsyncFile(W, "models_clip_open-clip-xlm-roberta-large-vit-huge-14.pth",
                @"base\models_clip_open-clip-xlm-roberta-large-vit-huge-14.pth", 4772359047L, "CLIP 图像编码器"));
            shared.Add(new LipsyncFile(W, "google/umt5-xxl/special_tokens_map.json",
                @"base\google\umt5-xxl\special_tokens_map.json", 6623L, "UMT5 词表"));
            shared.Add(new LipsyncFile(W, "google/umt5-xxl/spiece.model",
                @"base\google\umt5-xxl\spiece.model", 4548313L, "UMT5 分词模型"));
            shared.Add(new LipsyncFile(W, "google/umt5-xxl/tokenizer.json",
                @"base\google\umt5-xxl\tokenizer.json", 16837417L, "UMT5 分词器"));
            shared.Add(new LipsyncFile(W, "xlm-roberta-large/sentencepiece.bpe.model",
                @"base\xlm-roberta-large\sentencepiece.bpe.model", 5069051L, "XLM-R 分词模型"));
            shared.Add(new LipsyncFile(W, "xlm-roberta-large/tokenizer.json",
                @"base\xlm-roberta-large\tokenizer.json", 17082660L, "XLM-R 分词器"));

            string IT = "MeiGen-AI/InfiniteTalk";
            shared.Add(new LipsyncFile(IT, "quant_models/t5_fp8.safetensors",
                @"t5\t5_fp8.safetensors", 6733349304L, "T5 fp8 量化"));
            shared.Add(new LipsyncFile(IT, "quant_models/t5_map_fp8.json",
                @"t5\t5_map_fp8.json", 12506L, "T5 量化映射表"));

            shared.Add(new LipsyncFile("Kijai/WanVideo_comfy",
                "Lightx2v/lightx2v_I2V_14B_480p_cfg_step_distill_rank64_bf16.safetensors",
                @"lora\lightx2v_I2V_14B_480p_rank64_bf16.safetensors", 738005744L, "加速 LoRA"));

            shared.Add(new LipsyncFile("TencentGameMate/chinese-wav2vec2-base", "config.json",
                @"wav2vec\config.json", 1951L, "中文 wav2vec2 配置"));
            shared.Add(new LipsyncFile("TencentGameMate/chinese-wav2vec2-base", "preprocessor_config.json",
                @"wav2vec\preprocessor_config.json", 160L, "wav2vec2 预处理配置"));
            shared.Add(new LipsyncFile("TencentGameMate/chinese-wav2vec2-base", "pytorch_model.bin",
                @"wav2vec\pytorch_model.bin", 380261837L, "wav2vec2 权重"));

            // 档位 A：高画质（fp8 主干 + InfiniteTalk single 全精度），磁盘约 37.4 GB（按清单字节实测）
            LipsyncVariant hq = new LipsyncVariant("hq", "高画质（fp8 主干）", "fp8 主干 17 GB + InfiniteTalk single 10 GB，合计约 37.4 GB，建议 16 GB 以上显存");
            hq.Files.AddRange(shared);
            hq.Files.Add(new LipsyncFile("Kijai/WanVideo_comfy",
                "Wan2_1-I2V-14B-480P_fp8_e4m3fn.safetensors",
                @"dit\Wan2_1-I2V-14B-480P_fp8_e4m3fn.safetensors", 16993877896L, "视频主干 fp8"));
            hq.Files.Add(new LipsyncFile(IT, "single/infinitetalk.safetensors",
                @"infinitetalk_single.safetensors", 9948708152L, "对口型主干（单说话人）"));

            // 档位 B：省显存（GGUF Q4_K_M），磁盘约 24.1 GB（按清单字节实测）
            LipsyncVariant low = new LipsyncVariant("low", "省显存（GGUF Q4_K_M）", "Q4_K_M 主干 11 GB + InfiniteTalk Q4_K_M 1.4 GB，合计约 24.1 GB，建议 8 GB 以上显存");
            low.Files.AddRange(shared);
            low.Files.Add(new LipsyncFile("city96/Wan2.1-I2V-14B-480P-gguf",
                "wan2.1-i2v-14b-480p-Q4_K_M.gguf",
                @"gguf\wan2.1-i2v-14b-480p-Q4_K_M.gguf", 11341184384L, "视频主干 GGUF Q4_K_M"));
            low.Files.Add(new LipsyncFile("Kijai/WanVideo_comfy_GGUF",
                "InfiniteTalk/Wan2_1-InfiniteTalk_Single_Q4_K_M.gguf",
                @"gguf\Wan2_1-InfiniteTalk_Single_Q4_K_M.gguf", 1403374240L, "对口型主干 GGUF Q4_K_M"));

            return new LipsyncVariant[] { hq, low };
        }

        static LipsyncVariant[] _all;

        public static LipsyncVariant[] All()
        {
            if (_all == null) _all = Build();
            return _all;
        }

        public static LipsyncVariant Get(string key)
        {
            foreach (LipsyncVariant v in All())
                if (string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)) return v;
            return All()[0];
        }

        public static List<LipsyncFile> FilesOf(string key)
        {
            return Get(key).Files;
        }

        // LocModel 的 lipsync_variant 取值映射到本下载器的档位键（只读识别，不改任何配置）
        public static string KeyFromLocModelVariant(string v)
        {
            if (string.IsNullOrEmpty(v)) return "hq";
            string t = v.Trim().ToLowerInvariant();
            if (t.IndexOf("gguf") >= 0 || t.IndexOf("q4") >= 0 || t.IndexOf("low") >= 0 || t.IndexOf("lite") >= 0)
                return "low";
            return "hq";
        }

        public static string DemucsUrlOf()
        {
            return "https://dl.fbaipublicfiles.com/demucs/hybrid_transformer/" + DemucsFileName;
        }

        public static string Fmt(long bytes)
        {
            if (bytes < 0) return "未知";
            double v = bytes;
            string[] u = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.00") + " " + u[i];
        }
    }
}
