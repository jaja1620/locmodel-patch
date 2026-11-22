---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 798659b1f59514cb753b383c1f423b09_996340c0e4f911f19f9d525400cd780f
    ReservedCode1: 9jBoZ2Ts/PmY3ex0QlA8FBeDer0JGueNgyxP9S6jYbbuAUFuHS7DFh6xPm3GBT2K3p+TfRjnulV6x7XaQpalH90VMVV/lNuPV7qNnWMatHeANhuKulO5qmZyF+aelwbJTPRpdgKuGIbbYKXc06y6d1eRpLnFEXRqFZQ614HXIUwMt7HNBNKEEdYI4jo=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 798659b1f59514cb753b383c1f423b09_996340c0e4f911f19f9d525400cd780f
    ReservedCode2: 9jBoZ2Ts/PmY3ex0QlA8FBeDer0JGueNgyxP9S6jYbbuAUFuHS7DFh6xPm3GBT2K3p+TfRjnulV6x7XaQpalH90VMVV/lNuPV7qNnWMatHeANhuKulO5qmZyF+aelwbJTPRpdgKuGIbbYKXc06y6d1eRpLnFEXRqFZQ614HXIUwMt7HNBNKEEdYI4jo=
---

# LocModel 下载器 v3.0.0 发布说明

发布日期：2026-11-20
基线：LocModel 下载器 v2.4.2（完整备份见 `LocModel-Downloader-v2.4.2-backup.zip`）
产物：`LocModel-Downloader-v3.0.0.exe` / `locmodel-downloader-v3.0.0-src.zip`

## 一、本版核心：并发分片下载（HTTP Range 多连接）

- 单文件按 Range 切分为 N 个独立区间，多连接并行拉取，落盘前先预分配半成品，各连接只写自己的偏移区间。
- 连接数可选 1 / 2 / 3 / 4 / 6 / 8（默认 4，上限 16），最小分片门槛默认 8 MB，随 exe 同目录 `LocModel下载器_下载配置.txt` 持久化（`connections=` / `minseg_mb=`）。
- 302 / 307 手动跟随（最多 5 跳，`AllowAutoRedirect=false`），每跳重新携带 Range；解决 hf-mirror 跳转 xethub 后 Range 头丢失导致的失败。
- 显式启用 TLS 1.2 / 1.1，连接池 `DefaultConnectionLimit=64`，关闭 `Expect100Continue`。
- 断点续传：`.part.seg` 记录每片已完成字节，**每 2 秒落盘一次**（此前仅在下片/异常时写，进程被强杀会丢掉全部进度）。实测强杀后续传 330.1 MB / 942.3 MB（35.0%）直接续跑。
- 我先前的描述里"落盘后删 .seg"仍然保留，避免成品旁残留孤儿状态文件。

## 二、目标盘写入能力自适应（本版新增的必需保护）

并发分片在慢盘上会因多流随机写互相打断而**变慢**，因此按落盘盘实测顺序写速度压制并发：

| 目标盘实测顺序写 | 并发上限 | 说明 |
|---|---|---|
| < 60 MB/s | 1（退回单连接） | 外置 USB 机械盘（如 WD Elements）属此档 |
| 60–120 MB/s | 2 | |
| 120–300 MB/s | 4 | |
| ≥ 300 MB/s | 配置值 | NVMe 走满配置 |

探测方式：向落盘目录顺序写 128 MB 后删除（按卷缓存，单进程只探一次）。样本低于 128 MB 会被首次分配开销污染（F: 32 MB 样本仅 5 MB/s，128 MB 样本 29 MB/s），故取 128 MB 且只计写入循环耗时。

本机实测（988,097,824 字节 `Qwen/Qwen2.5-0.5B-Instruct/model.safetensors`）：

| 落盘位置 | 介质 | 判定 | 耗时 | 平均速度 |
|---|---|---|---|---|
| C:（NVMe SSD） | KIOXIA BG4 | 4 连接分片 | 13.9 s（含 35% 续传） | 67.8 MB/s |
| C:（NVMe SSD） | KIOXIA BG4 | 4 连接分片 | 19.7 s | 47.7 MB/s |
| F:（外置 USB 机械盘） | WD Elements 25A1 | 自动退回单连接 | 无分片 | 受盘限约 25–30 MB/s |

对照：同线路 curl 单连接 80 MB / 2.9 s ≈ 28.8 MB/s，4 连接并行 4×30 MB 合计 46.8 MB/s。分片收益在 SSD 落盘 + 高带宽线路成立；机械盘/外置盘上并发无意义，因此自动回退。

## 三、下载站点收敛

- 内置线路去掉 `m1.locmodel.com` / `m2.locmodel.com`，只保留 `https://hf-mirror.com`（备用 `https://huggingface.co`）。实测 m1/m2 对 HF 文件路径返回 404、会员路径返回 403（本机 `member_token` 为空），保留只会白耗重试。
- 站点配置默认值 `https://hf-mirror.com`；`LocModel下载器_站点配置.txt` 现内容即该单条。
- 界面站点提示追加"官方 m1/m2 已从内置线路移除"。

## 四、兼容与兜底

- 单连接内核（`Downloader.Fetch`）行为保持 v2.4.2 不变，作为所有异常场景的兜底：分片返回 Unsupported / 一个字节未落地 / 半成品损坏时，自动清理 `.part` / `.part.seg` 后走单连接重下。
- 服务器不支持 Range（Range 请求返回 200）、hf 缓存层小文件（307 → `/api/resolve-cache` 返回 200 且无 `Content-Length`）、文件小于最小分片门槛——以上均判为 Unsupported 并静默回退，不报错、不中断下载。
- 沿用 v2.4.2 的半成品复用逻辑（连续前缀可被首片继承）。

## 五、命令行入口（便于排查）

```
LocModel下载器_v3.0.0.exe --selftest                      # 界面/配置/分片规划自检
LocModel下载器_v3.0.0.exe --setconn 6                     # 设置默认并发连接数（1~16）
LocModel下载器_v3.0.0.exe --segtest <url> <dest> [conns] [minMB]   # 单文件分片下载实测
LocModel下载器_v3.0.0.exe --uicheck <png>                 # 主界面截图
```

## 六、验证记录

| 用例 | 期望 | 结果 |
|---|---|---|
| 本地 24 MB / 4 连接 / minSeg 4 MB | Done | Done，SHA-256 一致 |
| 本地 5 MB 奇数长度 / 4 连接 / minSeg 1 MB | Done | Done，SHA-256 一致 |
| 本地 2 MB / minSeg 8 MB | 回退单连接 | Unsupported |
| 服务端不支持 Range | 回退单连接 | Unsupported |
| hf 缓存层小文件 tokenizer.json | 回退单连接 | Unsupported |
| 真实 942 MB 大文件（NVMe） | Done | Done，4 连接 |
| 真实 942 MB 大文件（外置机械盘） | 回退单连接 | Unsupported（日志给出盘速） |
| 下载中强杀进程后续传 | 从 35% 续跑 | 通过 |
| `--selftest` 全项 | 通过 | 通过（含 >2 GiB、1 TiB 分片规划） |

## 七、已知限制

- 分片并发受目标盘随机写能力约束，机械盘/外置盘收益为零，已做自适应回退；NAS/SMB 网络盘同样建议 1–2 连接。
- 单文件分片下载期间若远端内容变更（etag 变化），会丢弃半成品重下，不做跨版本续传。
- exe 未写 FileVersion（与 v2.4.2 一致），避免影响 `LocModelProfile.Detect` 的版本推断链（exe 版本号 → settings 专有字段 → accel_lines → 内置特征串 → 默认 3.5）。
- 真实大文件仅校验长度与落盘完整性，未逐字节比对远端 SHA-256（官方未在同一响应给出校验值）。
*（内容由AI生成，仅供参考）*
