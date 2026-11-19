# v2.3.0 — 新增 LocModel 版本选择（3.3 / 3.1）

**发布日期**：2026-11-19
**类型**：能力扩展（下载/校验/续传内核零改动）

---

## 一句话

v2.3 在 v2.2.1 外面加了一层「LocModel 版本」适配层：界面顶部可选 **3.3（默认）** 或 **3.1**，切换后自动重载模型清单与目录规则。**下载、校验、续传内核与 v2.2.1 字节级完全一致，包括那次 >2 GiB 校验死循环的修复。**

## 为什么需要它

LocModel 3.3.0 改了两件事，v2.2.1 直接跑会读错：

1. **清单变了**：图像/视频模型从 12 个变 13 个（新增 `minimax-h3-image`），语言/推理仍是 16 个，总计 28 → 29。
2. **配置变了**：`settings.json` 新增 `accel_lines`（会员加速线路）、`account_base`、`comfy_dir`、`images_dir`、`imported`、`perf_*` 等 20 多项；`hf_mirror` 从 URL 变成枚举值 `member`；引擎配置从 `engines.json` 移到 `engines\engine.json`；模型目录下新增 `image\loras` 与 `_locmodel_dl_tmp`。

如果沿用旧逻辑，最坏情况是把 `HF` 镜像地址写成字符串 `member` 去请求 —— 直接全站失败。

## 新增内容

| 能力 | 说明 |
| --- | --- |
| 版本下拉 | 顶部新增「LocModel 版本：3.3（默认）/ 3.1」，旁边显示探测结果（exe 路径 + 版本号） |
| 双清单 | 3.3 档位 29 个模型（图像 7 + 视频 6 + 语言/推理 16）；3.1 档位 28 个模型（图像 6 + 视频 6 + 语言/推理 16） |
| 配置适配 | 3.3 下按枚举语义解释 `hf_mirror`，改用 `custom_mirror` + `accel_lines`；`accel_lines` 注入 catalog 探测线路 |
| 目录避让 | 临时目录改用 `{模型目录}\_locmodel_dl_tmp\lmv23`，不与 3.3 自己用的 `_locmodel_dl_tmp` 抢文件 |
| LoRA / ComfyUI | `image\loras` 与 `comfy_dir` **只读识别并显示**，不纳入下载管理、不自动改写用户配置 |
| 记忆选择 | 选择结果写入 exe 同目录 `LocModel下载器_版本配置.txt`，不碰 LocModel 的 `settings.json` |

## 内核零改动的验证方式

- `Downloader.cs`、`SiteProbe.cs` 与 v2.2.1 的 SHA-256 前 12 位比对：`313DC3B74651` / `1915B5167DD4`，**完全一致**。
- 用 v2.3 源码重编 `Test24` 跑 v2.2.1 的断点续传与超大文件用例：跨进程续传 10/10 PASS、8–100 GiB 校验 5/5 PASS。

## 实测记录

| 项 | 结果 |
| --- | --- |
| 版本探测（本机 3.3.0，安装在 `D:\Users\jiasi\AppData\Local\LocModel`，配置在 `%LOCALAPPDATA%\LocModel`） | 正确识别为 3.3.0，模型目录 `F:\AI-Models` |
| exe 内嵌图像/视频清单解析 | 13 条与 3.3 实际一致（新增 `minimax-h3-image`），无噪声条目 |
| 文件名解析（6 个代表模型，含双文件与多依赖） | 6/6 命中，含 `minimax_h3_image_pruned-Q3_K_M.gguf` |
| 界面自检 | 3.3 档位 29 项、3.1 档位 28 项 |
| 端到端真实下载 | `minimax-h3-image` 主模型 8.77 GB + 共享组件 18.22 GB 完整落盘并通过校验，目录规则正确 |

## 已知事项

- 连接级中断自动续传用例（测试服务器每次连接主动断开）在 v2.2.1 与 v2.3 上输出完全相同，均走「线路失败」而非「连接提前结束」分支；这是测试服务器 `close_connection` 与 .NET 读取异常的既有行为，非本次改动引入，真实 `hf-mirror.com` 断点续传不受影响。
- v2.3 未在 LocModel 2.1.0 上测试。

## 资产

| 文件 | 说明 |
| --- | --- |
| `LocModel-Downloader-v2.3.exe` | 单文件下载器（无需运行库，Windows 10/11 自带 .NET Framework 4.8） |
| `locmodel-downloader-v2.3-src.zip` | 完整源码（6 个 .cs） |
| `RELEASE_NOTES_v2.3.md` | 本文件 |

## 升级建议

- 用 LocModel 3.3 的：直接用默认 3.3 档位。
- 仍停留在 3.1 的：把顶部版本切到 3.1，行为与 v2.2.1 完全一致。
- 已用 v2.2.1 且不打算升级 LocModel 的：可以不换，v2.2.1 仍然有效并保留在 Release 中。
