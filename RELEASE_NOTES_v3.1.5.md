# LocModel 下载器 v3.1.5 发布说明

- 基线：`v3.1.3`（本地已编译、未发布的版本，源码一并入库）
- 产物：`LocModel-Downloader-v3.1.5.exe`（271,872 字节 / 约 265 KB，单文件，.NET Framework 4.x，无需安装）
- 源码包：`locmodel-downloader-v3.1.5-src.zip`
- 状态：已编译、已自检、已做真实下载回归；tag `v3.1.5` + GitHub Release（Latest）
- 触发动机：LocModel 主程序 3.6.5 引入「对口型（InfiniteTalk）」子系统，下载器需要与之对齐

---

## 一、默认档位切到 3.6.5，其余档位保留可切换

| 项 | v3.1.3 | v3.1.5 |
| --- | --- | --- |
| 档位列表 | 3.6 / 3.5 / 3.3 / 3.1 | **3.6.5 / 3.6 / 3.5 / 3.3 / 3.1** |
| 默认档位 | 3.6 | **3.6.5** |
| 显示文案 | — | `LocModel 3.6.5（默认）` |

- `LocModelProfile.Normalize()` 增加 3.6.x 拆档：`patch >= 5 → 3.6.5`，其余 `3.6.x → 3.6`；版本号正则放宽为 `(\d+)\.(\d+)(?:\.(\d+))?`。
- `Detect()` 增加 4 参数重载（新增 `hasV365Settings` 判据）：exe 版本号 → 3.6.5 / 3.5 专有 settings 字段 → `accel_lines` → exe 内嵌特征串 → 默认版本；旧 3 参数重载保留并转发 `false`，老调用点行为不变。
- 3.6.5 档与 3.6 档共用同一份 13 条图像/视频 + 16 条语言/推理清单，**3.6.5 不引入新模型条目**（实测 catalog 四域 version=20261027 与 3.6 一致）。

---

## 二、对口型子系统纳入下载器纳管

### 2.1 独立成段（便于维护 / 修改 / 回滚）

新增 **3 个整文件**，对口型的全部专属知识收在 `LipsyncCatalog.cs` 一处：

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `src/LipsyncCatalog.cs` | 252 | 权重清单（仓库 / 仓库内路径 / 落盘相对路径 / 期望字节）、目录规则、绝对地址通道、站点覆盖结论 |
| `src/LipsyncEngine.cs` | 236 | 下载执行器（复用既有内核 `Downloader` 的取流/续传/校验/落盘能力，按 `权重 → 程序包 → 人声分离` 顺序执行） |
| `src/LipsyncForm.cs` | 380 | 独立窗口（目录、档位、文件清单、进度、日志、取消） |

主界面只加一个入口按钮「对口型模型」与一处站点状态文案，其余代码用 `lip-` 标记块隔离。

**回滚方式**：删除上述 3 个文件 + 还原 `MainForm.cs` 中 3 处 `lip-` 标记块即可，内核与其它档位完全不受影响。

### 2.2 下载目录结构（实测对齐 LocModel 3.6.5）

LocModel 3.6.5 把对口型程序包与权重都放在**程序数据目录**下，而不是模型目录：

```
%LOCALAPPDATA%\LocModel\lipsync\
├─ InfiniteTalk.zip                   程序包（GitHub 分发）
├─ infinitetalk_single.safetensors    对口型主干（单说话人，hq 档）
├─ base\                              Wan2.1 主干配置 / VAE / CLIP / UMT5 / XLM-R
├─ t5\                                T5 fp8 量化权重与映射表
├─ lora\                               lightx2v 加速 LoRA
├─ dit\                               视频主干（hq: fp8 safetensors / low: GGUF）
├─ gguf\                              省显存档主干（low 档）
├─ wav2vec\                            中文 wav2vec2（口型特征提取）
└─ demucs\                             人声分离模型（fbaipublicfiles 分发）
```

- 窗口默认写盘根目录 = `%LOCALAPPDATA%\LocModel\lipsync`（LocModel 实际读取位置）；取不到该路径才退回 `{模型目录}\lipsync`。
- 已安装检测优先 `%LOCALAPPDATA%\LocModel\lipsync`，其次 `{模型目录}\lipsync`；目录为空不算已安装。
- 下载器**只读识别** `settings.json` 的 `lipsync_variant` 用来预选档位，绝不回写。

### 2.3 两个档位与体积（按清单字节实测）

| 档位 | 主干 | 文件数 | 合计 |
| --- | --- | --- | --- |
| 高画质（`hq`） | fp8 主干 17 GB + InfiniteTalk single 10 GB | 16 | 约 **37.4 GB** |
| 省显存（`low`） | GGUF Q4_K_M 主干 11 GB + InfiniteTalk Q4_K_M 1.4 GB | 16 | 约 **24.1 GB** |

两档共用 14 个文件（VAE / CLIP / UMT5 / XLM-R / T5 fp8 / LoRA / wav2vec2），差异只在视频主干与对口型主干。

### 2.4 下载通道与国内镜像对应关系（重点结论）

| 通道 | 内容 | 地址来源 | 是否经过「下载站点」栏位 |
| --- | --- | --- | --- |
| HuggingFace 权重（18 个文件） | 主干 / LoRA / T5 / wav2vec2 等全部权重 | `{站点}/{repo}/resolve/main/{path}` | **是**，跟随栏位站点 |
| 程序包 `InfiniteTalk.zip` | 程序本体 | `github.com/MeiGen-AI/InfiniteTalk/archive/refs/heads/main.zip` → 失败回退 `codeload.github.com/MeiGen-AI/InfiniteTalk/zip/main` | 否（绝对地址） |
| 人声分离模型 | `955717e8-8726e21a.th`（84,141,911 字节） | `dl.fbaipublicfiles.com/demucs/hybrid_transformer/` | 否（绝对地址） |

**镜像核对结果（2026-11-28 实测，证据见 `v3.1.5/evidence/mirror_check.json`）**：

- `https://hf-mirror.com` 对 18 个权重文件 **18/18 HEAD 200**，`Content-Length` 与清单字节逐条一致 → **hf-mirror 已涵盖对口型全部权重**。
- `https://huggingface.co` 对应 18 条全部不可达（status -1，与既有结论一致）。
- 绝对地址三通道均可达：`github.com/.../main.zip` 200、`codeload.github.com/.../zip/main` 200、`dl.fbaipublicfiles.com` 200。

**因此站点栏位一字未改**：没有新增站点行、没有新增注释行，仅在「下载站点」状态行追加了一段说明文案：

> 对口型模型用站点：https://hf-mirror.com（实测已涵盖对口型全部权重，下载站点栏位无需增改）；程序包走 github.com/MeiGen-AI/InfiniteTalk，人声分离模型走 dl.fbaipublicfiles.com（两者用绝对地址，不经过站点栏位）

### 2.5 顺带修掉的真实缺陷

`Downloader.cs` 原有 `if (len < 512)` 的「过小即判为错误页」硬下限，会误伤对口型清单里 160 B / 250 B 的 `preprocessor_config.json`、`config.json`。改为：`ExpectedSize > 0 且 < 512` 时下限降到 1 字节，精确大小仍由 `ExpectedSize` 兜底校验。其余分片 / 续传 / 校验内核逻辑未动。

---

## 三、配置只读查看：新字段与新 token 只读展示、token 打码、绝不写回

新增 `src/SettingsView.cs`（116 行），主界面按钮「配置只读查看」打开：

- 展示 `settings.json` 识别结果（项目数据目录、模型目录、站点、档位）与常用字段；
- 3.6.5 新增字段（`lipsync_variant`、`engine_cn`、`account_ips`、`account_base`、`tps_cal_v`、`tps_records`、`ui_zoom`、`img_remote` 等）走白名单逐项列出，未知字段以 `fp` 指纹占位，数组最多列 20 项、单值截断 160 字；
- `hf_token` 一律打码：长度 ≤ 8 全星号，否则 `前4 + 星号 + 后4`（例：`sk-1********cdef`）；空值显示「（字段存在，本机未填值）」；
- **整个窗口不含任何写盘代码**，不写 `settings.json`、不写注册表、不改 LocModel 任何配置。

---

## 四、v3.1.3 一并入库（不含 tag / Release）

- `v3.1.3/` 目录、`LocModel-Downloader-v3.1.3.exe`、`locmodel-downloader-v3.1.3-src.zip`、`RELEASE_NOTES_v3.1.3.md` 随本次提交入库留档；
- **不为 v3.1.3 打 tag、不发 Release**，v3.1.5 直接从 v3.1.3 基线升级。

---

## 五、变更文件（SHA-256 前 12 位）

| 文件 | v3.1.3 | v3.1.5 | 行数 | 说明 |
| --- | --- | --- | --- | --- |
| `LipsyncCatalog.cs` | — | `BD382EE968B2` | 252 | 新增 |
| `LipsyncEngine.cs` | — | `2F813C1F1BDD` | 236 | 新增 |
| `LipsyncForm.cs` | — | `261254A97CDE` | 380 | 新增 |
| `SettingsView.cs` | — | `5FE25D3BD0EF` | 116 | 新增 |
| `MainForm.cs` | `22833AF9CD6A` | `3E1B079D6BC2` | 2623 | 入口按钮 + 站点文案 + `--lipget` 模式 |
| `CatalogService.cs` | `2CB04A9168F3` | `D99E3500E97E` | 1609 | 3.6.5 字段解析 + token 打码 |
| `LocModelProfile.cs` | `8C1558DD2DF3` | `440D3DCC89D8` | 272 | 3.6.5 档与检测 |
| `Downloader.cs` | `A94FA91A41AB` | `15B04B10A3D0` | 857 | 小文件最小长度下限 |
| 其余 13 个文件 | — | 与 v3.1.3 字节级相同 | — | 内核与既有功能未改 |

---

## 六、验证记录

### 6.1 编译与自检

- 编译：`csc.exe /nologo /codepage:65001 /target:winexe /out:dist\LocModel下载器_v3.1.5.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll src\*.cs`，exit=0。
- `--selftest`：**31 项全过，0 FAIL**（含档位策略 5 项、3.6.5 检测 2 项、对口型清单 2 档 16 项结构自检、token 打码长短用例、settings 只读渲染、v3.1.3 回归 9 项、图像/视频清单重叠 0）。
- `VerPolicy=OK Versions=[3.6.5,3.6,3.5,3.3,3.1] Default=3.6.5`；`LipRoot=%LOCALAPPDATA%\LocModel\lipsync`；`TokenMask=OK long=sk-1********cdef short=*****`。

### 6.2 对口型真实下载回归（`--lipget`，本地 HTTP 服务 + 真实 Range/206）

F 盘不在身边、模型盘离线，本轮用 C/D 临时目录 + 本地服务替身做端到端回归（清单用同结构的 KB/MB 级假数据，路径与字节校验逻辑与真实清单同一套代码）：

| 场景 | 期望 | 结果 |
| --- | --- | --- |
| A 全新下载 6 个文件（20 MiB / 9 MiB / 4 MiB / 2 MiB / 1600 B / 250 B） | 全部落盘且 MD5 一致 | ✅ `good=6 bad=0`，`.part` 残留 0 |
| B 幂等重跑 | 全部「已存在且校验通过，跳过」 | ✅ `good=6 bad=0`，耗时 0.0 s |
| C 预置 1 MiB `.part` 后重跑 | 服务端 206 续传、最终 MD5 一致 | ✅ `good=6 bad=0` |
| D 站点全错 | 如实报错、不换线到任何未设站点 | ✅ `ok=False good=0 bad=4`，线路逐条报失败 |
| E 栏位两站（首站全错，次站正常） | 命中第二站 | ✅ `good=4 bad=0` |

`verify` 复核：6 个文件字节数与 MD5 全对，残留 `.part` 数量 0。

### 6.3 界面自检

`--uicheck` 离屏渲染截图（`v3.1.5/evidence/uicheck.png`、`uicheck.pw.png`）：标题栏 `LocModel 模型下载器 v3.1.5`、档位下拉默认 `LocModel 3.6.5（默认）`、按钮「配置只读查看」「对口型模型」就位、站点为 `https://hf-mirror.com`、无控件重叠。

---

## 七、已知局限（如实声明）

1. **未做 37.4 GB / 24.1 GB 的真实全量下载**：F 盘模型盘离线，本轮只在临时目录用替身清单验证了下载链路（URL 拼接、Range 续传、字节校验、跳过逻辑），权重字节一致性以 18 个文件的服务端 `Content-Length` 逐条核对为准，未做整档 MB/s 实测。
2. **未在 LocModel 3.6.5 本体上做「下载完直接可用」联调**：本机 `%LOCALAPPDATA%\LocModel\lipsync` 尚为空（未安装对口型子系统），所以只做了目录结构对齐与清单校验，未跑真正的对口型推理。
3. 程序包按 GitHub 直连 + codeload 回退两个绝对地址，国内网络下若 github.com 不可达会失败（未接入 GitHub 镜像，避免引入未验证的第三方通道）；如需要可后续在 `LipsyncCatalog.ProgramSources` 追加条目。

---

## 八、保留策略与资产

- **不删不覆盖**：v3.1.1 及更早版本的 tag、目录、Release 资产一律保留。
- v3.1.2 / v3.1.3 以目录入库留档，**未打 tag、未发 Release**。
- v3.1.5 以 `v3.1.5/` 目录 + tag `v3.1.5` + GitHub Release（Latest）发布，资产：

| 资产 | 说明 |
| --- | --- |
| `LocModel-Downloader-v3.1.5.exe` | 单文件下载器（ASCII 名，跨平台安全） |
| `locmodel-downloader-v3.1.5-src.zip` | 21 个 `.cs` 源码（含 `tools/` 回归脚本的仓库目录内同源版本） |
| `RELEASE_NOTES_v3.1.5.md` | 本文件 |
