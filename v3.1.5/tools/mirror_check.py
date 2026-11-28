# -*- coding: utf-8 -*-
"""对 LipsyncCatalog 清单里的 HF 权重做镜像站可用性核对（hf-mirror.com vs huggingface.co）"""
import concurrent.futures as cf
import urllib.request
import urllib.error
import json
import sys

SITES = ["https://hf-mirror.com", "https://huggingface.co"]

FILES = [
    ("Wan-AI/Wan2.1-I2V-14B-480P", "config.json", 250),
    ("Wan-AI/Wan2.1-I2V-14B-480P", "Wan2.1_VAE.pth", 507609880),
    ("Wan-AI/Wan2.1-I2V-14B-480P", "models_clip_open-clip-xlm-roberta-large-vit-huge-14.pth", 4772359047),
    ("Wan-AI/Wan2.1-I2V-14B-480P", "google/umt5-xxl/special_tokens_map.json", 6623),
    ("Wan-AI/Wan2.1-I2V-14B-480P", "google/umt5-xxl/spiece.model", 4548313),
    ("Wan-AI/Wan2.1-I2V-14B-480P", "google/umt5-xxl/tokenizer.json", 16837417),
    ("Wan-AI/Wan2.1-I2V-14B-480P", "xlm-roberta-large/sentencepiece.bpe.model", 5069051),
    ("Wan-AI/Wan2.1-I2V-14B-480P", "xlm-roberta-large/tokenizer.json", 17082660),
    ("MeiGen-AI/InfiniteTalk", "quant_models/t5_fp8.safetensors", 6733349304),
    ("MeiGen-AI/InfiniteTalk", "quant_models/t5_map_fp8.json", 12506),
    ("MeiGen-AI/InfiniteTalk", "single/infinitetalk.safetensors", 9948708152),
    ("Kijai/WanVideo_comfy", "Lightx2v/lightx2v_I2V_14B_480p_cfg_step_distill_rank64_bf16.safetensors", 738005744),
    ("Kijai/WanVideo_comfy", "Wan2_1-I2V-14B-480P_fp8_e4m3fn.safetensors", 16993877896),
    ("Kijai/WanVideo_comfy_GGUF", "InfiniteTalk/Wan2_1-InfiniteTalk_Single_Q4_K_M.gguf", 1403374240),
    ("TencentGameMate/chinese-wav2vec2-base", "config.json", 1951),
    ("TencentGameMate/chinese-wav2vec2-base", "preprocessor_config.json", 160),
    ("TencentGameMate/chinese-wav2vec2-base", "pytorch_model.bin", 380261837),
    ("city96/Wan2.1-I2V-14B-480P-gguf", "wan2.1-i2v-14b-480p-Q4_K_M.gguf", 11341184384),
]

ABS = [
    "https://github.com/MeiGen-AI/InfiniteTalk/archive/refs/heads/main.zip",
    "https://codeload.github.com/MeiGen-AI/InfiniteTalk/zip/main",
    "https://dl.fbaipublicfiles.com/demucs/hybrid_transformer/955717e8-8726e21a.th",
]


def head(url, timeout=25):
    req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "LocModelDownloader/3.1.5"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            cl = r.headers.get("Content-Length")
            return r.status, int(cl) if cl and cl.isdigit() else -1, r.geturl()[:60]
    except urllib.error.HTTPError as e:
        return e.code, -1, ""
    except Exception as e:
        return -1, -1, str(e)[:60]


def check(site, repo, path, size):
    url = "%s/%s/resolve/main/%s" % (site, repo, path)
    st, cl, note = head(url)
    ok = (st == 200 and (cl == size or cl < 0))
    return dict(site=site, repo=repo, path=path, size=size, status=st, clen=cl, ok=ok, note=note)


def main():
    jobs = [(s, r, p, z) for (r, p, z) in FILES for s in SITES]
    rows = []
    with cf.ThreadPoolExecutor(max_workers=8) as ex:
        futs = [ex.submit(check, *j) for j in jobs]
        for f in cf.as_completed(futs):
            rows.append(f.result())
    rows.sort(key=lambda x: (x["repo"], x["path"], x["site"]))
    bad = [r for r in rows if not r["ok"]]
    print("== 权重核对：共 %d 组（%d 文件 × %d 站点），不通过 %d 组 ==" % (len(rows), len(FILES), len(SITES), len(bad)))
    for r in rows:
        print("%-3s %-24s %-12s %s  catalog=%s" % (
            "OK" if r["ok"] else "BAD", r["site"].split("//")[1], str(r["status"]), r["repo"] + "/" + r["path"][:70], r["size"]))
    print()
    print("== 绝对地址通道 ==")
    for u in ABS:
        st, cl, note = head(u)
        print("%s %s bytes=%s %s" % ("OK" if st in (200, 302, 303) else "BAD", st, cl, u))
    json.dump(rows, open(sys.argv[1], "w", encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
