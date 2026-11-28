# -*- coding: utf-8 -*-
"""生成对口型子系统真实下载回归用的假站点与清单（KB/MB 级），并校验落盘结果。

子命令：
  setup   <workdir>      生成 <workdir>\served 假站点数据 + <workdir>\spec.txt + <workdir>\expect.json
  verify  <workdir> <root>  校验下载落盘结果（字节 + MD5 + .part 残留）
"""
import hashlib
import json
import os
import sys

WEIGHTS = [
    # repo, repo内路径, 落盘相对路径, 字节（其中第 1、3 个 > 2×MinSegMB(8MB) → 走分片）
    ("Test/RepoBig", "big.bin", r"dit\big.bin", 20 * 1024 * 1024),
    ("Test/RepoBig", "t5/small.txt", r"t5\small.txt", 1600),
    ("Test/RepoMid", "sub/mid.bin", r"base\mid.bin", 9 * 1024 * 1024),
    ("Test/RepoTiny", "tiny.json", r"wav2vec\tiny.json", 250),
]
PROGRAM = ("program", "InfiniteTalk.zip", 4 * 1024 * 1024)
DEMUCS = ("demucs", "955717e8-8726e21a.th", 2 * 1024 * 1024)


def gen_bytes(n, seed):
    h = hashlib.sha256(seed.encode()).digest()
    out = bytearray()
    blk = h * 2048
    while len(out) < n:
        out += blk
    return bytes(out[:n])


def setup(work):
    served = os.path.join(work, "served")
    base = os.path.join(served, "test")
    expect = {"weights": [], "abs": []}
    for repo, src, rel, size in WEIGHTS:
        full = os.path.join(base, repo, "resolve", "main", *src.split("/"))
        os.makedirs(os.path.dirname(full), exist_ok=True)
        data = gen_bytes(size, repo + src)
        open(full, "wb").write(data)
        expect["weights"].append({"rel": rel, "size": size, "md5": hashlib.md5(data).hexdigest()})
    for d, name, size in (PROGRAM, DEMUCS):
        full = os.path.join(base, d, name)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        data = gen_bytes(size, d + name)
        open(full, "wb").write(data)
        expect["abs"].append({"rel": os.path.join(d, name), "size": size, "md5": hashlib.md5(data).hexdigest()})

    spec = os.path.join(work, "spec.txt")
    with open(spec, "w", encoding="utf-8") as f:
        f.write("# repo|srcPath|destRel|size  —— 回归用小文件清单，替换真实 GB 级清单\n")
        for repo, src, rel, size in WEIGHTS:
            f.write("%s|%s|%s|%d\n" % (repo, src, rel, size))
    json.dump(expect, open(os.path.join(work, "expect.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("served=%s" % base)
    print("spec=%s" % spec)
    for e in expect["weights"] + expect["abs"]:
        print("  expect %-28s %10d %s" % (e["rel"], e["size"], e["md5"]))


def verify(work, root):
    expect = json.load(open(os.path.join(work, "expect.json"), encoding="utf-8"))
    bad = 0
    for e in expect["weights"] + expect["abs"]:
        full = os.path.join(root, e["rel"])
        if not os.path.isfile(full):
            print("BAD missing %s" % e["rel"])
            bad += 1
            continue
        data = open(full, "rb").read()
        md5 = hashlib.md5(data).hexdigest()
        ok = len(data) == e["size"] and md5 == e["md5"]
        print("%s %-28s len=%d/%d md5=%s" % ("OK " if ok else "BAD", e["rel"], len(data), e["size"], md5 == e["md5"]))
        if not ok:
            bad += 1
    parts = []
    for dirpath, _, names in os.walk(root):
        for n in names:
            if n.endswith(".part"):
                parts.append(os.path.join(dirpath, n))
    print("残留 .part 数量 = %d %s" % (len(parts), parts if parts else ""))
    if parts:
        bad += len(parts)
    print("VERIFY bad=%d" % bad)
    return 1 if bad else 0


if __name__ == "__main__":
    if sys.argv[1] == "setup":
        setup(sys.argv[2])
    elif sys.argv[1] == "verify":
        sys.exit(verify(sys.argv[2], sys.argv[3]))
