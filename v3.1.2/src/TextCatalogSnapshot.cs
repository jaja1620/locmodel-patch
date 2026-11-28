// LocModel Downloader v3.1.1 - 语言/推理模型内置快照（第三兜底来源）
// 数据来源：LocModel 服务端 catalog.json（version=20261027，16 个模型），与 LocModel 3.6.1 内嵌清单逐字段一致。
// 用途：LocModel 扫描失败且服务端 catalog 不可达时兜底；正常情况下以 LocModel 扫描为准。
using System.Collections.Generic;

namespace LocModelDL
{
    public static class EmbeddedTextCatalog
    {
        public const string SnapshotVersion = "20261027";

        public static List<ModelEntry> Build()
        {
            List<ModelEntry> l = new List<ModelEntry>();
            ModelEntry m;

            m = new ModelEntry();
            m.Id = "qwen3.8-flash-next"; m.Name = "Qwen3.8 Flash-Next"; m.Org = "阿里 Qwen · unsloth";
            m.Release = "2026-08";
            m.Desc = "Qwen 最新一代 MoE（Qwen4 架构预览），每次只激活 6B，能看图；需要大内存";
            m.Tags = "对话 · 编程 · 看图 · 推理";
            m.Repo = "unsloth/Qwen3.8-Flash-Next-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 180; m.ActiveB = 6; m.Layers = 48; m.KvLayers = 12; m.KvHeads = 2;
            m.HeadDim = 256; m.Experts = 512; m.ExpertsUsed = 10; m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "UD-Q2_K_XL", "UD-Q3_K_XL", "UD-Q4_K_XL" });
            m.SizeMap["UD-Q2_K_XL"] = 52;
            m.SizeMap["UD-Q3_K_XL"] = 66;
            m.SizeMap["UD-Q4_K_XL"] = 82;
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "qwen3.8-27b"; m.Name = "Qwen3.8 27B"; m.Org = "阿里 Qwen · unsloth";
            m.Release = "2026-08";
            m.Desc = "Qwen 最新稠密模型，综合能力强，能看图，24G 显存流畅";
            m.Tags = "对话 · 编程 · 看图 · 推理";
            m.Repo = "unsloth/Qwen3.8-27B-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 27; m.Layers = 64; m.KvLayers = 16; m.KvHeads = 4; m.HeadDim = 256;
            m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "UD-Q2_K_XL", "UD-Q3_K_XL", "UD-Q4_K_XL", "UD-Q5_K_XL", "UD-Q6_K_XL", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "muse-glimmer-30b"; m.Name = "Muse Glimmer 30B"; m.Org = "Meta · unsloth";
            m.Release = "2026-08";
            m.Desc = "Meta 开源（Apache 2.0），擅长工具调用和本地智能体，能看图";
            m.Tags = "对话 · 编程 · 看图";
            m.Repo = "unsloth/Muse-Glimmer-30B-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 30; m.Layers = 64; m.KvHeads = 8; m.HeadDim = 128; m.CtxMax = 131072;
            m.Quants.AddRange(new string[] { "UD-Q2_K_XL", "UD-Q3_K_XL", "UD-Q4_K_XL", "UD-Q6_K_XL", "UD-Q8_K_XL" });
            m.SizeMap["UD-Q2_K_XL"] = 11.5;
            m.SizeMap["UD-Q3_K_XL"] = 13.5;
            m.SizeMap["UD-Q4_K_XL"] = 16.5;
            m.SizeMap["UD-Q6_K_XL"] = 21;
            m.SizeMap["UD-Q8_K_XL"] = 32;
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "qwen3.6-35b-a3b"; m.Name = "Qwen3.6 35B-A3B"; m.Org = "阿里 Qwen · unsloth";
            m.Release = "2026-04";
            m.Desc = "MoE 模型，每次只激活 3B，速度非常快，编程强，能看图";
            m.Tags = "对话 · 编程 · 看图";
            m.Repo = "unsloth/Qwen3.6-35B-A3B-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 35; m.ActiveB = 3; m.Layers = 40; m.KvLayers = 10; m.KvHeads = 2;
            m.HeadDim = 256; m.Experts = 256; m.ExpertsUsed = 8; m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "UD-Q2_K_XL", "UD-Q3_K_XL", "UD-Q4_K_XL", "UD-Q5_K_XL", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "qwen3.6-27b"; m.Name = "Qwen3.6 27B"; m.Org = "阿里 Qwen · unsloth";
            m.Release = "2026-04";
            m.Desc = "稠密模型，长文写作和复杂推理，能看图";
            m.Tags = "对话 · 推理 · 看图";
            m.Repo = "unsloth/Qwen3.6-27B-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 27; m.Layers = 64; m.KvLayers = 16; m.KvHeads = 4; m.HeadDim = 256;
            m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "UD-Q2_K_XL", "UD-Q3_K_XL", "UD-Q4_K_XL", "UD-Q5_K_XL", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "gemma-4-26b-a4b"; m.Name = "Gemma 4 26B-A4B"; m.Org = "Google · unsloth";
            m.Release = "2026-04";
            m.Desc = "Google 的 MoE 模型，每次激活 4B，看图和多语言都不错";
            m.Tags = "对话 · 看图";
            m.Repo = "unsloth/gemma-4-26B-A4B-it-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 26; m.ActiveB = 4; m.Layers = 30; m.KvLayers = 5; m.KvHeads = 8;
            m.HeadDim = 256; m.Experts = 128; m.ExpertsUsed = 8; m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "UD-Q3_K_XL", "UD-Q4_K_XL", "Q4_K_M", "UD-Q5_K_XL", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "gemma-4-31b"; m.Name = "Gemma 4 31B"; m.Org = "Google · unsloth";
            m.Release = "2026-04";
            m.Desc = "Gemma 4 最大号稠密模型，看图 + 长文";
            m.Tags = "对话 · 看图 · 推理";
            m.Repo = "unsloth/gemma-4-31B-it-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 31; m.Layers = 60; m.KvLayers = 10; m.KvHeads = 16; m.HeadDim = 128;
            m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "UD-Q2_K_XL", "UD-Q3_K_XL", "UD-Q4_K_XL", "Q4_K_M", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "gemma-4-12b"; m.Name = "Gemma 4 12B"; m.Org = "Google · unsloth";
            m.Release = "2026-04";
            m.Desc = "中等大小，8–12G 显存的甜点，能看图";
            m.Tags = "对话 · 看图";
            m.Repo = "unsloth/gemma-4-12b-it-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 12; m.Layers = 48; m.KvLayers = 8; m.KvHeads = 8; m.HeadDim = 256;
            m.CtxMax = 131072;
            m.Quants.AddRange(new string[] { "Q4_K_M", "UD-Q4_K_XL", "Q5_K_M", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "gemma-4-e4b"; m.Name = "Gemma 4 E4B"; m.Org = "Google · unsloth";
            m.Release = "2026-04";
            m.Desc = "为笔记本和小显存设计，8G 显存或纯 CPU 也能跑，能看图";
            m.Tags = "对话 · 看图";
            m.Repo = "unsloth/gemma-4-E4B-it-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 8; m.Layers = 42; m.KvLayers = 7; m.KvHeads = 2; m.HeadDim = 256;
            m.CtxMax = 131072;
            m.Quants.AddRange(new string[] { "Q4_K_M", "UD-Q4_K_XL", "Q6_K", "Q8_0" });
            m.SizeMap["Q4_K_M"] = 5;
            m.SizeMap["UD-Q4_K_XL"] = 5.1;
            m.SizeMap["Q6_K"] = 6.4;
            m.SizeMap["Q8_0"] = 8.2;
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "qwen3.5-122b-a10b"; m.Name = "Qwen3.5 122B-A10B"; m.Org = "阿里 Qwen · unsloth";
            m.Release = "2026-02";
            m.Desc = "大号 MoE，64G 以上内存的电脑能跑，专家放内存";
            m.Tags = "对话 · 推理 · 编程";
            m.Repo = "unsloth/Qwen3.5-122B-A10B-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 122; m.ActiveB = 10; m.Layers = 48; m.KvLayers = 12; m.KvHeads = 2;
            m.HeadDim = 256; m.Experts = 256; m.ExpertsUsed = 8; m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "UD-Q2_K_XL", "UD-Q3_K_XL", "UD-Q4_K_XL" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "qwen3.5-9b"; m.Name = "Qwen3.5 9B"; m.Org = "阿里 Qwen · unsloth";
            m.Release = "2026-03";
            m.Desc = "小巧全能，8G 显存流畅，能看图";
            m.Tags = "对话 · 看图";
            m.Repo = "unsloth/Qwen3.5-9B-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 9; m.Layers = 32; m.KvLayers = 8; m.KvHeads = 4; m.HeadDim = 256;
            m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "Q4_K_M", "UD-Q4_K_XL", "Q5_K_M", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "qwen3.5-4b"; m.Name = "Qwen3.5 4B"; m.Org = "阿里 Qwen · unsloth";
            m.Release = "2026-03";
            m.Desc = "低配电脑和纯 CPU 也能流畅对话";
            m.Tags = "对话";
            m.Repo = "unsloth/Qwen3.5-4B-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = true;
            m.ParamsB = 4; m.Layers = 32; m.KvLayers = 8; m.KvHeads = 4; m.HeadDim = 256;
            m.CtxMax = 262144;
            m.Quants.AddRange(new string[] { "Q4_K_M", "Q5_K_M", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "glm-4.7-flash"; m.Name = "GLM-4.7 Flash"; m.Org = "智谱 · unsloth";
            m.Release = "2026-01";
            m.Desc = "智谱轻量 MoE，编程和智能体任务强";
            m.Tags = "编程 · 对话";
            m.Repo = "unsloth/GLM-4.7-Flash-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = false;
            m.ParamsB = 30; m.ActiveB = 3; m.Layers = 47; m.KvHeads = 4; m.HeadDim = 128;
            m.Experts = 64; m.ExpertsUsed = 4; m.CtxMax = 131072;
            m.Quants.AddRange(new string[] { "UD-Q3_K_XL", "UD-Q4_K_XL", "Q4_K_M", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "devstral-small-2-24b"; m.Name = "Devstral Small 2 24B"; m.Org = "Mistral · unsloth";
            m.Release = "2025-12";
            m.Desc = "Mistral 的编程专用模型，写代码、改项目";
            m.Tags = "编程";
            m.Repo = "unsloth/Devstral-Small-2-24B-Instruct-2512-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = false;
            m.ParamsB = 24; m.Layers = 40; m.KvHeads = 8; m.HeadDim = 128; m.CtxMax = 131072;
            m.Quants.AddRange(new string[] { "UD-Q3_K_XL", "UD-Q4_K_XL", "Q4_K_M", "Q6_K", "Q8_0" });
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "gpt-oss-20b"; m.Name = "gpt-oss 20B"; m.Org = "OpenAI · unsloth";
            m.Release = "2025-08";
            m.Desc = "OpenAI 开源 MoE，16G 显存就能流畅跑";
            m.Tags = "对话 · 推理 · 编程";
            m.Repo = "unsloth/gpt-oss-20b-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = false;
            m.ParamsB = 21; m.ActiveB = 3.6; m.Layers = 24; m.KvHeads = 8; m.HeadDim = 64;
            m.Experts = 32; m.ExpertsUsed = 4; m.CtxMax = 131072;
            m.Quants.AddRange(new string[] { "Q4_K_M", "F16" });
            m.SizeMap["Q4_K_M"] = 11.6;
            m.SizeMap["F16"] = 13.8;
            m.MainFileTemplate = "{q}";
            l.Add(m);

            m = new ModelEntry();
            m.Id = "gpt-oss-120b"; m.Name = "gpt-oss 120B"; m.Org = "OpenAI · unsloth";
            m.Release = "2025-08";
            m.Desc = "OpenAI 开源大号 MoE，专家放内存也能跑出不错速度";
            m.Tags = "对话 · 推理 · 编程";
            m.Repo = "unsloth/gpt-oss-120b-GGUF";
            m.Category = "text"; m.IsImageLike = false; m.Vision = false;
            m.ParamsB = 117; m.ActiveB = 5.1; m.Layers = 36; m.KvHeads = 8; m.HeadDim = 64;
            m.Experts = 128; m.ExpertsUsed = 4; m.CtxMax = 131072;
            m.Quants.AddRange(new string[] { "Q4_K_M", "F16" });
            m.SizeMap["Q4_K_M"] = 62.8;
            m.SizeMap["F16"] = 65.4;
            m.MainFileTemplate = "{q}";
            l.Add(m);

            return l;
        }
    }
}