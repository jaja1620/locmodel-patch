# v2.4.2 — 支持 LocModel 3.5（默认），保留 3.3 / 3.1

**发布日期**：2026-11-20
**类型**：版本适配（下载 / 校验 / 续传内核零改动）

> 语言 / Language：中文说明见下方第一段，英文说明折叠在最后一段（点击展开）。
> 中文 by default. English notes are collapsed at the bottom — click to expand.

---

<details open>
<summary><b>中文说明（点击收起 / 展开）</b></summary>

## 一句话

v2.4.2 把版本适配层从「3.3 / 3.1」扩到「**3.5（默认）/ 3.3 / 3.1**」：装 LocModel 3.5 的机器开箱即用，装 3.3 或 3.1 的把下拉切回去，行为与旧版一致。**下载、校验、续传内核与 v2.2.1 / v2.3 / v2.4.1 字节级完全一致，包含那次 >2 GiB 校验死循环的修复。**

## 这一版相对 v2.3 的变化

| 项 | v2.3 | v2.4.2 |
| --- | --- | --- |
| LocModel 版本 | 3.3（默认）/ 3.1 | **3.5（默认）/ 3.3 / 3.1** |
| 模型来源 | 仅 LocModel 内置清单 | 内置清单 **+ 在线搜索任意 Hugging Face 仓库模型并直接下载**（自定义站点、测速择优、断点续传、校验全部复用本机内核） |
| 模型处理 | 只能下载现成文件 | 额外支持 **GGUF 量化转换**（下载源权重 → 本地转出目标量化档） |
| 详情面板 | 名称 / 组织 / 描述 / 仓库 / 量化档 / 大小 | 3.5 档位追加一行「**模型规格：180B（激活 6B）· 上下文 256K · 48 层**」 |

也就是说：**比 v2.3 多了「自定义搜索模型下载」这条完整链路**（在线搜索 → 选仓库与文件 → 走同一套测速/续传/校验落盘），并新增对 LocModel 3.5 的支持。

## LocModel 3.5 变了什么

1. **配置**：`settings.json` 共 36 个字段，新增/启用 `hf_token`、`github_mirror`、`accel_nodes`、`preferred_line`、`preferred_ip`（`hf_mirror` 仍是枚举 `member`，镜像地址放 `custom_mirror`）。
2. **模型清单**：`{m1|m2|get.locmodel.com}/catalog.json` 8033 B，`version=20261027`，16 个语言/推理模型，字段从 10 个扩到 **19 个**：新增 `params_b`、`active_b`、`layers`、`kv_layers`、`kv_heads`、`head_dim`、`experts`、`experts_used`、`ctx_max`。
3. **量化档全面换成 Unsloth Dynamic**：`UD-Q2_K_XL … UD-Q8_K_XL`（`gpt-oss` 另有 `F16`），旧版解析器按 `Q4_K_M` 之类固定档位名匹配会大面积落空。
4. **半成品机制**：主程序改为就地 `<file>.part` + `.part.meta`（内容 `{"size":N,"mismatch":0}`），并做线路一致性校验；不再往 `_locmodel_dl_tmp\lmv23` 里堆中转文件。
5. **图像/视频模型**：仍为 13 条，与 3.3 一致（`minimax-h3-image` 在册）。

## 3.5 档位做了什么适配

| 改动 | 说明 |
| --- | --- |
| 版本差异表 | `LocModelProfile.cs` 重写为 `V31 / V33 / V35` 三档差异表：配置字段解释、catalog 解析器、图像清单、落盘目录规则、半成品扫描开关全部按档位取值，新增版本只加一行 |
| 版本探测 | 探测链：exe 文件版本号（3.5.0 → 3.5）→ `settings.json` 的 3.5 专有字段 → `accel_lines` 有无 → exe 内嵌特征串（`accel_nodes`/`hf_token` → 3.5，`minimax-h3-image` → 3.3，`minimax-h3` → 3.1）→ 兜底默认 3.5 |
| catalog 解析 | 兼容 19 字段；缺体积的量化档只打日志提示，不打断列表；`(params_b, active_b)` 与 `experts/experts_used` 双写映射，兼容不同上游写法 |
| 内置快照为权威清单 | 3.5 下图像/视频依赖关系以内置快照为准（13 条全量、依赖仓库归属与子路径逐条锁定），exe 解析结果只用于「存在性核对 + 新条目提示」，不再让它生成落盘清单 —— 因为旧解析器在 3.5 上会漏 2 条、并把 VAE 记到错误仓库 |
| slug 固定映射 | `flux.1-schnell → flux1-schnell`、`flux.2-klein-4b → flux2-klein-4b`、`qwen-image-edit-2511 → qwen-image-edit`、`wan-2.1-comfyui-repackaged → wan2.1-t2v-1.3b` 等，落盘目录与 LocModel 自身完全对齐 |
| 模型规格展示 | 详情面板新增一行「模型规格：…（激活 …）· 上下文 …K · … 层」，同时用于内部校验；描述区加高 22 px，下方控件整体下移，无重叠 |
| `.part` 半成品识别 | 新增 `HalfPartScan.cs`：扫描模型目录下的 `<file>.part` / `.part.meta`，在计划预览里标注「主程序未完成下载 N MB」，**只提示，不接管、不删除、不续传** |
| `hf_token` 只读支持 | 仅在直连 `huggingface.co` 时附加 `Authorization`，`hf-mirror.com` 与其它站点一律不带；token 只读不写、日志打码（`hf_****c81`），自检用假 token |
| 默认版本 | 3.5（写在 exe 同目录 `LocModel下载器_版本配置.txt`，不碰 LocModel 的 `settings.json`） |

## 内核零改动的验证方式

| 文件 | SHA-256 前 12 位（v2.4.1 → v2.4.2） | 结论 |
| --- | --- | --- |
| `Downloader.cs` | `313DC3B74651` / `313DC3B74651` | **完全一致**（与 v2.2.1、v2.3 亦相同） |
| `SiteProbe.cs` | `1915B5167DD4` / `1915B5167DD4` | **完全一致**（与 v2.2.1、v2.3 亦相同） |
| `HubBrowserForm.cs` / `HubSearch.cs` / `HubUrlMapper.cs` / `RepoQuants.cs` | 逐字节一致 | 未改 |

## 实测记录（本机 LocModel 3.5.0）

| 项 | 结果 |
| --- | --- |
| 版本探测 | `FileVersion=3.5.0` → 识别为 **3.5**，模型目录 `F:\AI-Models` |
| 模型清单 | 3.5：29 项（图像 7 + 视频 6 + 语言/推理 16）；3.3：29 项；3.1：28 项（无 `minimax-h3-image`） |
| 规格字段 | 29 个模型中 16 个带规格，示例 `qwen3.8-flash-next → 模型规格：180B（激活 6B）· 上下文 256K · 48 层` |
| 详情面板布局 | 逐个模型点选 29/29 成功，规格行渲染 16 次，控件重叠 0 处 |
| catalog 拉取 | 三站点（m1 / m2 / get）返回一致：16 模型、19 字段、`version=20261027` |
| 落盘清单核对 | 主文件模板 × 全部量化档 100% 命中上游仓库；依赖 `repo::路径` 180 项全部命中，0 缺失 |
| 图像清单 | 内置快照 13 条 vs exe 解析 11 条：快照内仓库全部存在于 exe，差异均为旧解析器的漏项/串档，不影响落盘 |
| 端到端下载 | `unsloth/Qwen3.5-4B-GGUF` 的 `imatrix_unsloth.gguf_file` 下载完成，落盘 3,626,496 B，与上游 `size` 完全一致，校验通过 |
| 半成品识别 | 扫描到 3 处就地 `.part`（1.11 GB / 337.8 MB / 668 MB），仅提示，原有文件未改动 |
| token 打码 | `hf_****c81`；直连 HF 携带 `Bearer`，`hf-mirror.com` 与其它站点不携带 |

## 已知事项

- **3.5 的就地 `.part` 半成品不接管续传**：按设计只做识别提示，下载新文件不受影响；要用半成品请让 LocModel 自己续传。
- **图像/视频模型没有规格字段**：3.5 的 catalog 只为语言/推理模型提供 `params_b` 等，图像清单来自内置快照，因此规格行只在语言/推理模型上出现。
- **自检入口 `--hubget` 会弹出「完成」对话框**：这是正常的完成提示，命令行场景需手动关闭；GUI 下载不受影响。

## 资产

| 文件 | 说明 |
| --- | --- |
| `LocModel-Downloader-v2.4.2.exe` | 单文件下载器（Windows 10/11 自带 .NET Framework 4.8，无需装运行库） |
| `locmodel-downloader-v2.4.2-src.zip` | 完整源码（13 个 `.cs`，含 `tools/Probe23.cs`） |
| `RELEASE_NOTES_v2.4.2.md` | 本文件 |
| `v2.4.1/`（仓库内目录） | **v2.4.1 保留备份**：源码 12 个 `.cs` + 基线 exe，供回退对照 |

## 升级建议

- 已装 **LocModel 3.5**：直接用默认档位，无需任何设置。
- 仍在 **3.3 / 3.1**：下拉切到对应版本即可，行为与 v2.4.1 一致。
- 想回退：仓库 `v2.4.1/` 目录里的基线 exe 与源码可直接使用。

</details>

---

<details>
<summary><b>English notes (click to expand)</b></summary>

## TL;DR

v2.4.2 extends the version-adaptation layer from "3.3 / 3.1" to "**3.5 (default) / 3.3 / 3.1**". Machines running LocModel 3.5 work out of the box; users on 3.3 or 3.1 just switch the dropdown back. **The download / verify / resume kernel is byte-identical to v2.2.1, v2.3 and v2.4.1, including the previous >2 GiB verification-loop fix.**

## What changed since v2.3

| Item | v2.3 | v2.4.2 |
| --- | --- | --- |
| LocModel version | 3.3 (default) / 3.1 | **3.5 (default) / 3.3 / 3.1** |
| Model source | Built-in LocModel catalog only | Built-in catalog **+ online search over any Hugging Face repo, downloaded through the same local kernel** (custom sites, speed-based line picking, resume, verification) |
| Model handling | Download ready-made files | Plus **GGUF quantization conversion** (fetch source weights, convert locally) |
| Detail panel | name / org / description / repo / quant / size | 3.5 profile adds a line: **Model spec: 180B (active 6B) · context 256K · 48 layers** |

In short: compared with v2.3 this release adds the full **"search any model and download it"** flow, plus LocModel 3.5 support.

## What LocModel 3.5 changed

1. **Settings**: `settings.json` now has 36 fields, adding `hf_token`, `github_mirror`, `accel_nodes`, `preferred_line`, `preferred_ip` (`hf_mirror` stays the enum `member`; mirror URLs live in `custom_mirror`).
2. **Catalog**: `{m1|m2|get.locmodel.com}/catalog.json` (~8 KB, `version=20261027`), 16 text models, **19 fields** (new: `params_b`, `active_b`, `layers`, `kv_layers`, `kv_heads`, `head_dim`, `experts`, `experts_used`, `ctx_max`).
3. **Quantization switched to Unsloth Dynamic**: `UD-Q2_K_XL … UD-Q8_K_XL` (`gpt-oss` also `F16`); the old fixed-name matcher misses most of them.
4. **Partial-download mechanism**: the app now writes `<file>.part` + `.part.meta` (`{"size":N,"mismatch":0}`) in place, with line-consistency checks; no more staging inside `_locmodel_dl_tmp\lmv23`.
5. **Image/video models**: still 13 entries, same as 3.3.

## 3.5 adaptation highlights

| Change | Detail |
| --- | --- |
| Version table | `LocModelProfile.cs` rewritten as a `V31 / V33 / V35` diff table (settings interpretation, catalog parser, image catalog, on-disk rules, part-scan switch) — adding a future version is one entry |
| Detection | exe file version (3.5.0 → 3.5) → 3.5-only settings fields → `accel_lines` presence → embedded marker strings → fallback default 3.5 |
| Catalog parsing | 19 fields supported; quants without size only log a hint; `(params_b, active_b)` and `experts/experts_used` dual mapping |
| Built-in snapshot is authoritative | For 3.5 the embedded snapshot (13 entries, dependency repos and sub-paths pinned) drives the download plan; exe parsing is used only for existence cross-check and "new entry" hints — the old parser drops 2 entries and misattributes VAE repos on 3.5 |
| Slug mapping | `flux.1-schnell → flux1-schnell`, `flux.2-klein-4b → flux2-klein-4b`, `qwen-image-edit-2511 → qwen-image-edit`, `wan-2.1-comfyui-repackaged → wan2.1-t2v-1.3b`, etc., so directories match LocModel's own layout |
| Model spec line | New detail line "Model spec: …" (also used for internal validation); description area grew 22 px and the controls below moved down — zero overlap |
| `.part` detection | New `HalfPartScan.cs` reports in-place `<file>.part` / `.part.meta` in the plan preview ("unfinished download N MB"). **Report only — never takes over, deletes or resumes.** |
| `hf_token` | Read-only, sent **only** when connecting directly to `huggingface.co`; never to `hf-mirror.com` or other sites; masked in logs (`hf_****c81`) |
| Default version | 3.5, persisted in `LocModel下载器_版本配置.txt` next to the exe; LocModel's `settings.json` is never written |

## Kernel-unchanged proof

| File | SHA-256 (first 12 chars, v2.4.1 → v2.4.2) | Result |
| --- | --- | --- |
| `Downloader.cs` | `313DC3B74651` / `313DC3B74651` | identical |
| `SiteProbe.cs` | `1915B5167DD4` / `1915B5167DD4` | identical |
| `HubBrowserForm.cs` / `HubSearch.cs` / `HubUrlMapper.cs` / `RepoQuants.cs` | byte-identical | untouched |

## Test log (LocModel 3.5.0 on this machine)

| Item | Result |
| --- | --- |
| Version detection | `FileVersion=3.5.0` → detected **3.5**, models dir `F:\AI-Models` |
| Model lists | 3.5: 29 (7 image + 6 video + 16 text); 3.3: 29; 3.1: 28 (no `minimax-h3-image`) |
| Spec fields | 16 of 29 models carry specs; e.g. `qwen3.8-flash-next → 180B (active 6B) · 256K ctx · 48 layers` |
| Detail panel layout | all 29 models selectable, spec line rendered 16 times, 0 overlaps |
| Catalog fetch | m1 / m2 / get return identical data: 16 models, 19 fields, `version=20261027` |
| Download plan cross-check | every main-file template × quant present upstream; 180 dependency `repo::path` entries all found, 0 missing |
| Image catalog | snapshot 13 vs exe parse 11; all snapshot repos exist in the exe — differences are old-parser omissions/misattributions and do not affect the download plan |
| End-to-end download | `unsloth/Qwen3.5-4B-GGUF` → `imatrix_unsloth.gguf_file` downloaded, 3,626,496 B on disk, matches upstream `size`, verification passed |
| Part detection | 3 in-place `.part` files reported (1.11 GB / 337.8 MB / 668 MB) — report only, files untouched |
| Token masking | `hf_****c81`; `Bearer` only for huggingface.co |

## Known notes

- In-place `.part` files from LocModel 3.5 are **detected but not resumed**; let LocModel finish them. New downloads are unaffected.
- Image/video models have no spec fields, because the 3.5 catalog only supplies `params_b` etc. for text models.
- The `--hubget` self-test entry shows the normal "completed" dialog; close it manually. GUI downloads are unaffected.

## Assets

| File | Description |
| --- | --- |
| `LocModel-Downloader-v2.4.2.exe` | Single-file downloader (needs only the .NET Framework 4.8 shipped with Windows 10/11) |
| `locmodel-downloader-v2.4.2-src.zip` | Full source (13 `.cs`, includes `tools/Probe23.cs`) |
| `RELEASE_NOTES_v2.4.2.md` | This file |
| `v2.4.1/` (repo folder) | **v2.4.1 kept as backup**: 12 `.cs` + baseline exe for rollback comparison |

## Upgrade advice

- On **LocModel 3.5**: just use the default profile, no configuration needed.
- On **3.3 / 3.1**: switch the dropdown back; behaviour matches v2.4.1.
- To roll back: use the baseline exe and sources in the repo's `v2.4.1/` folder.

</details>

---

*（内容由 AI 生成，仅供参考）*
