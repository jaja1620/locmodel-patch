// LocModel Downloader v2.1 - data model
using System;
using System.Collections.Generic;

namespace LocModelDL
{
    public class QuantFile
    {
        public string Repo;   // owner/name
        public string Path;   // path inside repo
        public QuantFile() { }
        public QuantFile(string repo, string path) { Repo = repo; Path = path; }
        public string BaseName
        {
            get
            {
                if (Path == null) return "";
                int i = Path.LastIndexOf('/');
                return i < 0 ? Path : Path.Substring(i + 1);
            }
        }
    }

    public class Quant
    {
        public string Name;
        public List<QuantFile> Files = new List<QuantFile>();
        public double SizeGB;          // 0 = unknown
        public Quant() { }
        public Quant(string name) { Name = name; }
    }

    public class ModelEntry
    {
        public string Id;
        public string Name;
        public string Org;
        public string Desc;
        public string Release;
        public string Tags;
        public string Repo;            // main repo owner/name
        public string MainFileTemplate; // may contain {q}; may include sub-path
        public string MainFile2Template; // second expert file (wan2.2 i2v)
        public bool IsImageLike;        // true -> image\<slug>\ ; false -> {org}__{repo}\
        public bool Vision;
        public string Category;         // text | image | video
        public List<string> Quants = new List<string>();
        public Dictionary<string, double> SizeMap = new Dictionary<string, double>();
        public List<QuantFile> Common = new List<QuantFile>(); // shared deps -> image\common

        public ModelEntry() { }

        public List<Quant> BuildQuants()
        {
            List<Quant> list = new List<Quant>();
            foreach (string q in Quants)
            {
                Quant qt = new Quant(q);
                double s;
                if (SizeMap.TryGetValue(q, out s)) qt.SizeGB = s;
                if (!string.IsNullOrEmpty(MainFileTemplate))
                    qt.Files.Add(new QuantFile(Repo, Expand(MainFileTemplate, q)));
                if (!string.IsNullOrEmpty(MainFile2Template))
                    qt.Files.Add(new QuantFile(Repo, Expand(MainFile2Template, q)));
                list.Add(qt);
            }
            return list;
        }

        static string Expand(string tpl, string q) { return tpl.Replace("{q}", q); }

        public List<QuantFile> QuantsFiles(string quant)
        {
            foreach (Quant qt in BuildQuants()) if (qt.Name == quant) return qt.Files;
            return new List<QuantFile>();
        }

        // 落盘目录：文本/多模态 -> {models}\{owner}__{repoName}  ;  图像/视频 -> {models}\image\{slug}
        public string TargetDir(string modelsDir)
        {
            if (IsImageLike)
                return System.IO.Path.Combine(modelsDir, "image", Id);
            int i = Repo.IndexOf('/');
            string owner = i < 0 ? Repo : Repo.Substring(0, i);
            string name = i < 0 ? Repo : Repo.Substring(i + 1);
            return System.IO.Path.Combine(modelsDir, owner + "__" + name);
        }

        public bool SameRepo(QuantFile f) { return string.Equals(f.Repo, Repo, StringComparison.OrdinalIgnoreCase); }
    }

    public static class EmbeddedImageCatalog
    {
        // 与 LocModel 3.x 内置图像/视频下载页 20261027 版保持一致（字段来源：LocModel.exe 内嵌 catalog 字符串表 + HF 仓库实测）
        public static List<ModelEntry> Build()
        {
            List<ModelEntry> l = new List<ModelEntry>();

            ModelEntry m;

            m = new ModelEntry();
            m.Id = "qwen-image-2.1"; m.Name = "Qwen-Image 2.1"; m.Org = "阿里 Qwen";
            m.Release = "2026年9月发布";
            m.Desc = "最新版：画质和图里的中英文字都比上一代更好，体积小一半，还能一句话改图、生成透明背景 PNG";
            m.Tags = "图像 · 文生图 · 改图";
            m.Repo = "leejet/Qwen-Image-2.1-GGUF";
            m.MainFileTemplate = "qwen_image_2.1-{q}.gguf";
            m.Category = "image"; m.IsImageLike = true; m.Vision = true;
            m.Quants.AddRange(new string[] { "Q3_K", "Q4_K", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("Qwen/Qwen3-VL-8B-Instruct-GGUF", "Qwen3VL-8B-Instruct-Q4_K_M.gguf"));
            m.Common.Add(new QuantFile("Qwen/Qwen3-VL-8B-Instruct-GGUF", "mmproj-Qwen3VL-8B-Instruct-F16.gguf"));
            m.Common.Add(new QuantFile("Comfy-Org/Qwen-Image-2.1", "vae/qwen_image_2.1_vae_bf16.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "z-image-turbo"; m.Name = "Z-Image Turbo"; m.Org = "阿里通义";
            m.Release = "2025年11月发布";
            m.Desc = "速度快，8 步出图，中文提示词友好，4 GB 显存也能跑";
            m.Tags = "图像 · 文生图";
            m.Repo = "leejet/Z-Image-Turbo-GGUF";
            m.MainFileTemplate = "z_image_turbo-{q}.gguf";
            m.Category = "image"; m.IsImageLike = true;
            m.Quants.AddRange(new string[] { "Q3_K", "Q4_K", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("unsloth/Qwen3-4B-Instruct-2507-GGUF", "Qwen3-4B-Instruct-2507-Q4_K_M.gguf"));
            m.Common.Add(new QuantFile("Comfy-Org/z_image_turbo", "split_files/vae/ae.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "qwen-image"; m.Name = "Qwen-Image"; m.Org = "阿里 Qwen";
            m.Release = "2025年8月发布";
            m.Desc = "画面里的中文、英文文字最准，适合海报、封面、带字的图";
            m.Tags = "图像 · 文生图";
            m.Repo = "QuantStack/Qwen-Image-GGUF";
            m.MainFileTemplate = "Qwen_Image-{q}.gguf";
            m.Category = "image"; m.IsImageLike = true; m.Vision = true;
            m.Quants.AddRange(new string[] { "Q2_K", "Q3_K_M", "Q4_K_M", "Q5_K_M", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("mradermacher/Qwen2.5-VL-7B-Instruct-GGUF", "Qwen2.5-VL-7B-Instruct.Q4_K_M.gguf"));
            m.Common.Add(new QuantFile("mradermacher/Qwen2.5-VL-7B-Instruct-GGUF", "Qwen2.5-VL-7B-Instruct.mmproj-Q8_0.gguf"));
            m.Common.Add(new QuantFile("QuantStack/Qwen-Image-GGUF", "VAE/Qwen_Image-VAE.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "qwen-image-edit"; m.Name = "Qwen-Image-Edit"; m.Org = "阿里 Qwen";
            m.Release = "2025年12月发布（2511 版）";
            m.Desc = "改图：一句话换背景、换衣服、改图里的文字，人物长相基本不变，中文指令最准";
            m.Tags = "图像 · 改图";
            m.Repo = "unsloth/Qwen-Image-Edit-2511-GGUF";
            m.MainFileTemplate = "qwen-image-edit-2511-{q}.gguf";
            m.Category = "image"; m.IsImageLike = true; m.Vision = true;
            m.Quants.AddRange(new string[] { "Q2_K", "Q3_K_M", "Q4_K_M", "Q5_K_M", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("mradermacher/Qwen2.5-VL-7B-Instruct-GGUF", "Qwen2.5-VL-7B-Instruct.Q4_K_M.gguf"));
            m.Common.Add(new QuantFile("mradermacher/Qwen2.5-VL-7B-Instruct-GGUF", "Qwen2.5-VL-7B-Instruct.mmproj-Q8_0.gguf"));
            m.Common.Add(new QuantFile("QuantStack/Qwen-Image-GGUF", "VAE/Qwen_Image-VAE.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "flux1-schnell"; m.Name = "FLUX.1 schnell"; m.Org = "Black Forest Labs";
            m.Release = "2024年8月发布";
            m.Desc = "画质标杆，4 步出图，写实照片和插画都很强（英文提示词效果最好）";
            m.Tags = "图像 · 文生图";
            m.Repo = "leejet/FLUX.1-schnell-gguf";
            m.MainFileTemplate = "flux1-schnell-{q}.gguf";
            m.Category = "image"; m.IsImageLike = true;
            m.Quants.AddRange(new string[] { "q3_k", "q4_k", "q8_0" });
            m.Common.Add(new QuantFile("comfyanonymous/flux_text_encoders", "clip_l.safetensors"));
            m.Common.Add(new QuantFile("comfyanonymous/flux_text_encoders", "t5xxl_fp16.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "flux2-klein-4b"; m.Name = "FLUX.2 klein 4B"; m.Org = "Black Forest Labs";
            m.Release = "2026年1月发布";
            m.Desc = "又小又快，4 步出图；还能按一句话改图：换背景、改颜色、加删东西，8 GB 显存可用";
            m.Tags = "图像 · 文生图 · 改图";
            m.Repo = "leejet/FLUX.2-klein-4B-GGUF";
            m.MainFileTemplate = "flux-2-klein-4b-{q}.gguf";
            m.Category = "image"; m.IsImageLike = true;
            m.Quants.AddRange(new string[] { "Q4_0", "Q8_0" });
            m.Common.Add(new QuantFile("unsloth/Qwen3-4B-GGUF", "Qwen3-4B-Q4_K_M.gguf"));
            m.Common.Add(new QuantFile("Comfy-Org/flux2-klein-4B", "split_files/vae/flux2-vae.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "wan2.1-t2v-1.3b"; m.Name = "Wan2.1 文生视频 1.3B"; m.Org = "阿里通义万相";
            m.Release = "2025年3月发布";
            m.Desc = "最轻量的视频模型，8 GB 显存就能生成 480P 短视频，适合先体验";
            m.Tags = "视频 · 文生视频";
            m.Repo = "Comfy-Org/Wan_2.1_ComfyUI_repackaged";
            m.MainFileTemplate = "split_files/diffusion_models/wan2.1_t2v_1.3B_fp16.safetensors";
            m.Category = "video"; m.IsImageLike = true;
            m.Quants.Add("fp16");
            m.SizeMap["fp16"] = 2.64;
            m.Common.Add(new QuantFile("city96/umt5-xxl-encoder-gguf", "umt5-xxl-encoder-Q8_0.gguf"));
            m.Common.Add(new QuantFile("Comfy-Org/Wan_2.1_ComfyUI_repackaged", "split_files/vae/wan_2.1_vae.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "wan2.2-ti2v-5b"; m.Name = "Wan2.2 视频 5B"; m.Org = "阿里通义万相";
            m.Release = "2025年8月发布";
            m.Desc = "画质明显更好，支持文字生成视频，也能上传一张图片让它动起来";
            m.Tags = "视频 · 文生视频 · 图生视频";
            m.Repo = "QuantStack/Wan2.2-TI2V-5B-GGUF";
            m.MainFileTemplate = "Wan2.2-TI2V-5B-{q}.gguf";
            m.Category = "video"; m.IsImageLike = true;
            m.Quants.AddRange(new string[] { "Q4_K_M", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("city96/umt5-xxl-encoder-gguf", "umt5-xxl-encoder-Q8_0.gguf"));
            m.Common.Add(new QuantFile("Comfy-Org/Wan_2.2_ComfyUI_Repackaged", "split_files/vae/wan2.2_vae.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "wan2.2-i2v-a14b"; m.Name = "Wan2.2 图生视频 A14B"; m.Org = "阿里通义万相";
            m.Release = "2025年8月发布";
            m.Desc = "开源图生视频画质天花板：上传一张图让它动起来，动作幅度大、细节稳定；两个 14B 专家轮流工作，较慢";
            m.Tags = "视频 · 图生视频";
            m.Repo = "QuantStack/Wan2.2-I2V-A14B-GGUF";
            m.MainFileTemplate = "LowNoise/Wan2.2-I2V-A14B-LowNoise-{q}.gguf";
            m.MainFile2Template = "HighNoise/Wan2.2-I2V-A14B-HighNoise-{q}.gguf";
            m.Category = "video"; m.IsImageLike = true;
            m.Quants.AddRange(new string[] { "Q3_K_M", "Q4_K_M", "Q5_K_M", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("city96/umt5-xxl-encoder-gguf", "umt5-xxl-encoder-Q8_0.gguf"));
            m.Common.Add(new QuantFile("Comfy-Org/Wan_2.2_ComfyUI_Repackaged", "split_files/vae/wan2.2_vae.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "ltx-2.3"; m.Name = "LTX-2.3 有声视频"; m.Org = "Lightricks";
            m.Release = "2026年发布";
            m.Desc = "视频和声音一起生成（对白、音效、背景音乐），支持 720P 和图片动起来；蒸馏版 8 步出片，速度快";
            m.Tags = "视频 · 有声视频";
            m.Repo = "unsloth/LTX-2.3-GGUF";
            m.MainFileTemplate = "distilled-1.1/ltx-2.3-22b-distilled-1.1-{q}.gguf";
            m.Category = "video"; m.IsImageLike = true;
            m.Quants.AddRange(new string[] { "Q3_K_M", "Q4_K_M", "Q5_K_M", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("unsloth/gemma-3-12b-it-qat-GGUF", "gemma-3-12b-it-qat-UD-Q4_K_XL.gguf"));
            m.Common.Add(new QuantFile("unsloth/LTX-2.3-GGUF", "text_encoders/ltx-2.3-22b-distilled_embeddings_connectors.safetensors"));
            m.Common.Add(new QuantFile("unsloth/LTX-2.3-GGUF", "vae/ltx-2.3-22b-distilled_video_vae.safetensors"));
            m.Common.Add(new QuantFile("unsloth/LTX-2.3-GGUF", "vae/ltx-2.3-22b-distilled_audio_vae.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "minimax-h3"; m.Name = "MiniMax-H3 有声视频"; m.Org = "MiniMax 海螺";
            m.Release = "2026年发布";
            m.Desc = "画面 + 立体声一起生成，人物动作和镜头运动自然，可以上传图片当第一帧；模型很大，建议 24 GB 显存 + 64 GB 内存";
            m.Tags = "视频 · 有声视频";
            m.Repo = "unsloth/MiniMax-H3-GGUF";
            m.MainFileTemplate = "minimax_h3_fl2va_pruned-{q}.gguf";
            m.Category = "video"; m.IsImageLike = true;
            m.Quants.AddRange(new string[] { "Q2_K", "Q3_K", "Q4_K", "Q5_0", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("unsloth/MiniMax-H3-GGUF", "qwen3vl_32b_minimax_h3-Q4_K_M.gguf"));
            m.Common.Add(new QuantFile("unsloth/MiniMax-H3-GGUF", "vae/minimax_h3_video_vae_fp16.safetensors"));
            m.Common.Add(new QuantFile("unsloth/MiniMax-H3-GGUF", "vae/minimax_h3_audio_vae_fp32.safetensors"));
            l.Add(m);

            m = new ModelEntry();
            m.Id = "minimax-h3-ref"; m.Name = "MiniMax-H3 多图参考视频"; m.Org = "MiniMax 海螺";
            m.Release = "2026年发布";
            m.Desc = "上传 1–4 张参考图（人物、商品、场景），生成的视频里会出现它们，同时生成声音。和「MiniMax-H3 有声视频」共用文本编码器和 VAE，已经下过 H3 只需再下主体";
            m.Tags = "视频 · 参考图生视频";
            m.Repo = "unsloth/MiniMax-H3-GGUF";
            m.MainFileTemplate = "minimax_h3_ref2va_pruned-{q}.gguf";
            m.Category = "video"; m.IsImageLike = true;
            m.Quants.AddRange(new string[] { "Q2_K", "Q3_K", "Q4_K", "Q5_0", "Q6_K", "Q8_0" });
            m.Common.Add(new QuantFile("unsloth/MiniMax-H3-GGUF", "qwen3vl_32b_minimax_h3-Q4_K_M.gguf"));
            m.Common.Add(new QuantFile("unsloth/MiniMax-H3-GGUF", "vae/minimax_h3_video_vae_fp16.safetensors"));
            m.Common.Add(new QuantFile("unsloth/MiniMax-H3-GGUF", "vae/minimax_h3_audio_vae_fp32.safetensors"));
            l.Add(m);

            return l;
        }
    }
}
