# LocModel 网络修复工具集

**简体中文** | [English](README_EN.md)

> LocModel 在国内网络下无法直连 `huggingface.co`，表现为模型列表拉不到、模型下载直接失败。
> 本仓库提供两条互补的解决方案：**v1.0 镜像补丁**（改一行配置，让 LocModel 本体走镜像）与 **v2.4.2 独立下载器**（自带 GUI 的完整下载器，不改动 LocModel，多站点自动测速择优，支持 LocModel **3.5 / 3.3 / 3.1** 三版本切换，并可在界面里**在线搜索任意 Hugging Face 仓库的模型直接下载**；**v3.0.0** 在其上加入并发分片下载加速，**v3.1.0** 加**下载前复核与已下载文件标绿**并适配 LocModel 3.6，最新的 **v3.1.1** 取消 `huggingface.co` 隐式兜底（站点只以「下载站点」栏位为准）并把模型清单改为「LocModel 扫描优先、内置快照兜底」，**v3.1.2** 让在线搜索可自选**下载到自定义目录**并把多笔任务改为**串行排队**（上限 8 个），**v3.1.3** 修正「开始下载」按钮被整段禁用等 6 处交互缺陷（本地构建，未发 Release），最新的 **v3.1.5** 适配 LocModel **3.6.5**（默认档位，3.6 / 3.5 / 3.3 / 3.1 仍可切换）、把 **对口型（InfiniteTalk）子系统纳入下载器纳管**，并新增**配置只读查看**（新字段与 token 打码展示、绝不写回 `settings.json`））。

---

## 目录

- [一、版本矩阵（本仓库有什么）](#一版本矩阵本仓库有什么)
- [二、问题是什么](#二问题是什么)
- [三、v1.0 的设计思路：补丁派](#三v10-的设计思路补丁派)
- [四、v2.2 / v2.3 的设计思路：独立器派](#四v22-的设计思路独立器派)
- [五、v2.2 与 v1.0 的设计差别与原因](#五v22-与-v10-的设计差别与原因)
- [六、v2.2 使用方法（详尽）](#六v22-使用方法详尽)
- [七、v1.0 使用方法（保留）](#七v10-使用方法保留)
- [八、兼容性与测试范围（必读）](#八兼容性与测试范围必读)
- [九、验证记录](#九验证记录)
- [十、自行编译](#十自行编译)
- [十一、版本保留与发布策略](#十一版本保留与发布策略)
- [十二、免责声明与许可](#十二免责声明与许可)

---

## 一、版本矩阵（本仓库有什么）

| 版本 | 形态 | 作用 | 是否需要 LocModel 在跑 | 状态 |
| --- | --- | --- | --- | --- |
| **v1.0.0** | 单文件控制台补丁 `locmodel_patch.exe`（18 KB） | 把 `settings.json` 的 `custom_mirror` 改成 `https://hf-mirror.com`，让 LocModel 自己的下载页走镜像 | 改配置时会尝试正常关闭，改完可自动重启 | 已验证 LocModel 2.1.0 ✅ / 3.1.0 ✅ |
| **v2.2.0** | 单文件图形下载器 `LocModel下载器_v2.2.exe`（88 KB） | 独立下载器，覆盖 LocModel 全部 28 个模型；自定义多个下载站点，下载前自动测速择优 | 不需要 LocModel 运行，只读写同一模型目录 | 已验证 LocModel 3.1.0 ✅（**未在 2.1.0 测试**） |
| **v2.2.1** | 单文件图形下载器 `LocModel下载器_v2.2.1.exe`（89 KB） | v2.2.0 的缺陷修复版：修掉 >2 GiB 模型「校验失败(数组维度超过了支持的范围。)」后反复丢弃半成品的死循环 | 同 v2.2.0 | 已验证 LocModel 3.1.0 ✅（**未在 2.1.0 测试**） |
| **v2.3.0** | 单文件图形下载器 `LocModel下载器_v2.3.exe`（101 KB） | v2.2.1 的能力扩展版：新增「LocModel 版本选择」（默认 3.3，可切回 3.1），覆盖 3.3 的 29 个模型（图像/视频 13 + 语言/推理 16），适配 3.3 新增的 `accel_lines` 等配置与 LoRA 目录。**下载/校验/续传内核与 v2.2.1 完全相同，未改一行** | 同 v2.2.0 | 已验证 LocModel 3.3.0 ✅ / 3.1.0 ✅ |
| **v2.4.0 / v2.4.1** | 单文件图形下载器（166 KB） | 新增**在线搜索模型下载**：可搜索任意 Hugging Face 仓库，选择文件后走本机内核下载（自定义站点、测速择优、续传、校验全部复用）；新增 **GGUF 量化转换**（下载源权重 → 本地转出目标量化档）。**内核与 v2.3 字节级一致** | 同 v2.2.0 | 已验证 LocModel 3.3.0 ✅ / 3.1.0 ✅（源码与基线 exe 保留在仓库 `v2.4.1/`） |
| **v2.4.2** | 单文件图形下载器 `LocModel下载器_v2.4.2.exe`（176 KB） | v2.4.1 的版本适配版：新增**对 LocModel 3.5 的支持（默认档位）**，3.3 / 3.1 仍可手动切换；适配 3.5 的 19 字段 catalog、Unsloth Dynamic 量化档、`hf_token` / `accel_nodes` 等新配置；详情面板新增「模型规格：180B（激活 6B）· 上下文 256K · 48 层」；识别 3.5 的就地 `.part` 半成品（只提示不接管）。**下载/校验/续传内核与 v2.2.1 / v2.3 / v2.4.1 字节级完全相同** | 同 v2.2.0 | 已验证 LocModel 3.5.0 ✅ / 3.3.0 ✅ / 3.1.0 ✅（清单自检） |
| **v3.0.0** | 单文件图形下载器 `LocModel下载器_v3.0.0.exe`（195 KB） | v2.4.2 的下载加速版：新增**并发分片下载**（手动跟随 302/307 并重发 Range、按目标盘实测顺序写速度自动压并发、每 2 秒落盘分片进度），界面新增连接数下拉（1–16，默认 4）与分片状态显示；默认站点收敛为 `hf-mirror.com`。分片不适用时（低于最小分片、无 Range、机械盘）自动退回单连接内核 | 同 v2.4.2 | 已验证真实 942 MB 文件 4 连接 13.9 s / 67.77 MB/s，SHA-256 一致；**本版仅打 tag，未建 GitHub Release** |
| **v3.1.0** | 单文件图形下载器 `LocModel下载器_v3.1.0.exe`（201 KB） | v3.0.0 的可用性增强版：新增**下载前复核**——「本次将下载的文件」栏逐项判断本地是否已下载完成（目标存在且长度 > 0、同目录无 `.part` / `.part.meta` 半成品残留、声明大小已知时长度达标），命中的行整行标为**绿色**并在文件信息后追加「**（已存在）**」，栏标题同步显示「共 N 项，已存在 M 项」；新增 **LocModel 3.6 档（默认）**，3.5 / 3.3 / 3.1 仍可切换。复核为纯只读，**下载内核与 v3.0.0 字节级一致** | 同 v3.0.0 | 已验证 --selftest 29 项全过、真实模型目录抽样（z-image-turbo 4 项 3 绿 / qwen-image-2.1 5 项 3 绿、带 `.part` 的主文件正确判为未下载）、自绘例程像素级标绿验证、本地 24 MB 4 连接分片下载回归 |
| **v3.1.1** | 单文件图形下载器 `LocModel下载器_v3.1.1.exe`（211 KB） | v3.1.0 的两点行为修正：**① 取消 `huggingface.co` 隐式兜底**——「下载站点」栏位成为唯一线路来源，内置站点表只剩 `hf-mirror.com`，测速排序不再把内置线路垫后，在线搜索与链接映射的默认站点与栏位同源，栏目里没填的站点一律不进入下载链路，站点全失败就如实报错；**② 模型清单改为「LocModel 扫描优先 + 内置快照兜底」**——语言/推理清单按「扫描 → 服务端 catalog.json → 内置快照」三级取源，图像/视频清单扫描到 ≥6 条即以扫描为准、快照条目只补不减（量化档并集、依赖只补空），LocModel 小版本新增的模型（本次实测 `wan2.1-i2v-14b`）可直接出现。站点策略改动涉及 `Downloader.cs`，但仅限站点表与空回退分支，分片/续传/校验内核逻辑未改，`DownloadSeg.cs` 与 v3.1.0 字节级一致 | 同 v3.0.0 | 已验证 --selftest 30 项全过（新增站点链 3 段 + 清单来源 2 段断言）、单站点真实下载回归（12 MiB 走完整内核，MD5 一致，日志中 `hf-mirror.com` / `huggingface.co` 命中均为 0）、404 场景只重试栏位站点不换线、清单实测 14 + 16 条 |
| **v3.1.2** | 单文件图形下载器 `LocModel下载器_v3.1.2.exe`（227 KB） | v3.1.1 的两项交互增强：**① 在线搜索窗口可选「下载到主界面目录 / 自定义目录」**——单选区 + `浏览…` 目录选择 + 实时落点提示，自定义目录下落盘 `{自定义目录}\{owner}__{仓库名}`，主界面「模型目录」栏位与 LocModel 配置均不被改写；自定义目录为空时点下载会提示并聚焦浏览按钮、任务不提交；选择记在 exe 同目录 `LocModel下载器_在线搜索下载目录.txt`（`mode=` / `dir=`），下次打开自动恢复；**② 下载任务串行排队（A 方案）**——同「仓库 + 量化档 + 目标目录」去重、上限 8 个未开始任务、主界面新增「下载队列」状态行与「清空队列」按钮、「取消下载」只取消当前任务且队列继续、队列清空后统一弹一次汇总，主界面「开始下载」与在线搜索共用同一队列。下载内核（`Downloader.cs` / `DownloadSeg.cs` / `CatalogService.cs`）与 v3.1.1 **字节级一致**，队列只在外层串行交付任务 | 同 v3.0.0 | 已验证 --selftest 30 项全过 + 新增 v3.1.2 段 10 项断言（目录记忆往返、路径归一化、两种落点、队列 FIFO/去重/上限/取消不清队列/清空）、主界面与在线搜索窗口离屏布局自检（无重叠）、`--hubget` 两条真实下载回归（主界面目录 570 B 落盘 / 自定义目录 570 B 落盘且主界面目录零写入）、真实模型目录 `F:\AI-Models` 108 文件前后零改动 |
| **v3.1.3** | 单文件图形下载器 `LocModel-Downloader-v3.1.3.exe`（214 KB） | v3.1.2 的六处交互缺陷修复：**① 主界面「开始下载」不再整段禁用**（拆 `_submitting` 与 `_busy`）；**② `_busy` 与「取消下载」按钮由统一状态方法 `UpdateActionState()` 裁决**；**③ 在线搜索窗口显示队列状态与拒绝提示**（`lblQueueState` 每秒刷新）；**④ 切换仓库后刷新「实际落盘」路径**；**⑤ 单实例互斥**（`Local\LocModelDownloader_SingleInstance_v3`，`--` 开头的命令行模式绕开互斥）；**⑥ 版本号统一**（主窗口标题、在线搜索窗口标题、配置 txt 头注释、各 `.cs` 首行注释）。下载内核与 v3.1.2 字节级一致 | 同 v3.0.0 | 本地已完成编译、自检与打包；**未打 tag、未发 Release**，源码与产物随 v3.1.5 提交入库留档 |
| **v3.1.5** | 单文件图形下载器 `LocModel-Downloader-v3.1.5.exe`（265 KB） | v3.1.3 的能力扩展版：**① 默认档位切到 LocModel 3.6.5**（3.6 / 3.5 / 3.3 / 3.1 保留可切换，`Normalize()` 对 3.6.x 拆档、`Detect()` 增加 3.6.5 专有 settings 判据）；**② 对口型（InfiniteTalk）子系统纳入纳管**——新增 `LipsyncCatalog.cs` / `LipsyncEngine.cs` / `LipsyncForm.cs` 三个独立文件（主界面只加「对口型模型」入口按钮），两档（`hq` fp8 约 37.4 GB / `low` GGUF Q4_K_M 约 24.1 GB）共 16 个文件，默认落盘 `%LOCALAPPDATA%\LocModel\lipsync`（与 LocModel 3.6.5 程序数据目录一致），权重走「下载站点」栏位、程序包走 GitHub 绝对地址、人声分离模型走 `dl.fbaipublicfiles.com`；**③ 站点栏位未改**——实测 `hf-mirror.com` 对 18 个权重文件 18/18 命中且字节一致，因此只在「下载站点」状态行追加说明（「对口型模型用」），不新增站点行；**④ 配置只读查看**——新窗口展示 3.6.5 新增字段与打码后的 `hf_token`，**不含任何写盘代码**；**⑤ 小文件最小长度下限修正**（`ExpectedSize < 512` 时不再误判 160 B / 250 B 的配置小文件为错误页）。除上述修正外，分片 / 续传 / 校验内核与 v3.1.3 字节级一致 | 同 v3.0.0 | 已验证 --selftest **31 项全过 0 FAIL**、`--lipget` 真实下载回归 5 场景（全新 6 文件 MD5 全对、幂等重跑全跳过、1 MiB `.part` 206 续传、站点全错如实失败不换线、栏位两站回落命中次站）、`hf-mirror.com` 18 文件 HEAD 逐条核对、`--uicheck` 离屏渲染无重叠。**F 盘离线，37.4 GB 全量真实下载未做（见发布说明「已知局限」）** |

两个版本**互不冲突**，可以同时存在：

- 只想让 LocModel 本体能正常下载 → 用 v1.0 补丁，30 秒搞定。
- 想要多镜像、自动测速、可控目录、可续传不受本体限制 → 用 v2.2 下载器。
- 两者一起用也没问题：v1.0 只改 `custom_mirror` 一个值，v2.2 一个字节都不碰 LocModel。

历史版本文件均保留在仓库与 Release 中，不做删除（见[第十一节](#十一版本保留与发布策略)）。

---

## 二、问题是什么

LocModel 的模型下载链路写死了 HuggingFace 官方域名：

- 国内网络访问 `https://huggingface.co` 会被 RST / DNS 污染（本机实测解析到 `199.59.148.8`，curl 12 秒超时，返回码 000）。
- 结果就是：模型列表拉空、「网络无法连接」、点下载报错、下载到一半断流。
- `https://hf-mirror.com` 是国内可用镜像：模型列表、仓库信息、Range 请求全部正常（实测 HTTP 200 / 206），走通后下载速率约 25–90 MB/s。

所以两条路线都在解决同一件事：**把「必须直连 huggingface.co」换成「走可用镜像」**。区别只在改在哪一层。

---

## 三、v1.0 的设计思路：补丁派

**核心判断：既然本体已经提供了镜像入口（`custom_mirror` 配置字段），那就只改这个字段，不动程序本体。**

做法：

1. 定位 `settings.json`：exe 同目录 → `%LOCALAPPDATA%\LocModel\settings.json` → 注册表 `InstallLocation` 目录。
2. LocModel 在运行就**尝试正常关闭**（最多等 15 秒，关不掉就中止，绝不强杀）。
3. 备份为 `settings.json.bak.yyyyMMdd-HHmmss`。
4. **只改 `custom_mirror` 一个值**，缩进、换行、BOM、键顺序、尾随换行全部原样保留。
5. 写完回读校验，不过就用备份自动回滚。
6. 原本在运行就自动重启 LocModel。

优点：极小（18 KB）、零依赖、可逆（有备份 + `--dry-run` 预览）、不看模型目录、不管下载逻辑，不会和 LocModel 抢文件。

代价：**能力上限被 LocModel 本体锁死**。镜像只能填一个；没有测速；没有多线路择优；下载行为受本体的重试策略、会员线路机制（3.1.0 起 `hf_mirror` 字段与会员高速线路）影响；本体下载失败时还会把 `custom_mirror` 回写成会员线路，需要重启程序再试。

---

## 四、v2.2 / v2.3 的设计思路：独立器派

**核心判断：本体不提供任何可编程入口，下载逻辑也封在 exe 里改不动，那就绕开本体，自己写一个只做下载的 GUI，直接往同一个模型目录写文件。**

取证依据（决定了这条路而不是监听端口那条路）：

- LocModel 主进程（Tauri + WebView2 打包）**不监听任何 TCP/UDP 端口**：对 exe 内 `axum` / `warp` / `actix` / `TcpListener` / `HttpServer` / `tiny_http` 全 0 命中，`CorsLayer` 0 命中。
- 全部能力是 Tauri command（`get_settings` / `save_settings` / `download_model` / `set_hf_mirror` / `accel_plan` 等 30+ 个），前端经 `window.__TAURI_INTERNALS__.invoke` → WebView2 `postMessage` 通信，origin 为 `tauri://localhost`，外部页面（`file://` 或 `http://`）无法调用。
- 设置里的 `port=8686` 只是拉起的 llama.cpp 推理引擎端口（未启动时不监听）；图像引擎的 `8790` 只是 stable-diffusion.cpp 的 WebUI（仅 `/v1/models`、`/sdapi/v1/*` 等几条路由），都不含下载/设置语义。

结论：**外部端口注入方案不可行**，因此 v2.2 走「独立下载器 + 对齐落盘规范」的路线。

v2.2 做的事：

1. 启动即读 LocModel 的 `settings.json` 拿到真实模型目录（含盘符漂移校验），并解析全部 28 个模型（图像 6 + 视频 6 + 语言/推理 16）、全部量化档位、多文件依赖与 `image\common` 共享组件。
2. 落盘路径与 LocModel 完全一致，所以下完的文件 LocModel 能直接识别：
   - 图像 / 视频 → `{目录}\image\{模型ID}\{上游原文件名}`
   - 共享组件 → `{目录}\image\common\{原文件名}`
   - 语言 / 推理 → `{目录}\{组织}__{仓库名}\{原文件名}`
3. 自定义下载站点（v2.2 新增）：界面顶部输入栏默认预填 `https://hf-mirror.com`，可填多个自有镜像；下载前自动对每个站点测速（并发 3、单站 Range 实读 256 KB、超时 8 秒），按实测速度排序，**用最快的线路下载**，失败自动降级到下一名。
4. 下载链路加固：HEAD 预检换版、`.part` + `.part.meta` 断点续传、416 自愈、临时目录隔离、校验阶梯（长度 → HTML 错误页 → GGUF 魔数 / safetensors 头长 → SHA-256）、成功线路记忆提前。
5. 全程不修改 LocModel：不 patch、不注入、不改注册表，只读写模型目录与自己的配置文件。

### 4.1 v2.3 的改动：LocModel 版本选择（3.3 / 3.1）

LocModel 3.3.0 把模型清单和配置结构都改了一批（新增 `minimax-h3-image`、`settings.json` 多了 `accel_lines` / `account_base` / `comfy_dir` / `images_dir` 等字段、`hf_mirror` 从 URL 变成枚举值 `member`、引擎配置从 `engines.json` 移到 `engines\engine.json`）。v2.3 的做法是**加一层版本适配层，而不是改内核**：

| 变化点 | 3.1 | 3.3 | v2.3 的处理 |
| --- | --- | --- | --- |
| 模型总数 | 28（图像 6 + 视频 6 + 语言/推理 16） | 29（图像/视频 13 + 语言/推理 16） | 版本下拉切换后重载清单，3.3 增加 `minimax-h3-image` |
| 版本识别 | — | — | 读 `LocModel.exe` 文件版本号 + 配置里的 `accel_lines` 双重判断，界面顶部显示探测结果 |
| `hf_mirror` | URL 字符串 | 枚举 `member` / `custom` | 3.3 下不再把 `hf_mirror` 当镜像地址用，改读 `custom_mirror` 与 `accel_lines` |
| 镜像线路 | 内置 4 条 | 内置 4 条 + `accel_lines`（会员线路） | `accel_lines` 注入到 catalog 探测线路，下载线路表不变 |
| LoRA | 无 | 新增 `{目录}\image\loras` | 只识别、显示该目录，**不纳入下载管理**（3.3 的 LoRA 走自己的 `/sdapi/v1/loras` 入口） |
| `comfy_dir` | 无 | ComfyUI 目录（可能为空） | 只读识别，为空时提示，**绝不自动改写用户配置** |
| 临时目录 | 无 | `{目录}\_locmodel_dl_tmp`（3.3 自用） | 下载器改用 `{目录}\_locmodel_dl_tmp\lmv23`，避开本体临时文件 |

**内核零改动的边界**：`Downloader.cs`（HEAD 预检、`.part` + `.part.meta` 续传、416 自愈、镜像轮换、卡死检测、SHA-256 校验、>2 GiB 校验修复）与 `SiteProbe.cs`（站点解析与测速）在两个版本中**字节级完全一致**，v2.3 只在其外围新增 `LocModelProfile.cs` 适配层并改动 UI 与清单来源。

界面顶部新增一行：

```
LocModel 版本：[ 3.3（默认） v ]   探测：D:\...\LocModel.exe · 3.3.0        （切换后自动重载清单与目录规则）
```

版本选择由下载器自己记在 exe 同目录的 `LocModel下载器_版本配置.txt` 里，**不写进 LocModel 的 `settings.json`**，所以切版本不会影响本体行为。

---

### 4.2 v2.4.2 的改动：在线搜索下载 + LocModel 3.5 支持（3.5 / 3.3 / 3.1）

**（1）相比 v2.3 多出来的能力：自定义搜索模型下载。** 内置清单只覆盖 LocModel 自己的 29 个模型，想下别的仓库得手工拼 URL。v2.4.x 在界面里加了「在线搜索」：输入关键词搜 Hugging Face 上的仓库 → 选定仓库 → 列出文件（含 `.gguf` 量化档与 `mmproj` 视觉投影）→ 直接调用**本机下载内核**落盘。也就是说自定义站点、测速择优、断点续传、SHA-256 校验、镜像轮换这些能力，对任意仓库一样生效。配套新增 `HubBrowserForm.cs` / `HubSearch.cs` / `HubUrlMapper.cs`，以及 GGUF 量化转换（`ConvertService.cs` + `RepoQuants.cs`）。

**（2）v2.4.2 的版本适配：LocModel 3.5。** 3.5 又改了一批结构：

| 变化点 | 3.3 | 3.5 | v2.4.2 的处理 |
| --- | --- | --- | --- |
| catalog 字段 | 10 个 | **19 个**（新增 `params_b` / `active_b` / `layers` / `kv_layers` / `kv_heads` / `head_dim` / `experts` / `experts_used` / `ctx_max`） | 解析器补全 19 字段，缺失值宽松降级；规格写入详情面板与内部校验 |
| 量化档命名 | `Q2_K … Q8_0` | **Unsloth Dynamic**：`UD-Q2_K_XL … UD-Q8_K_XL`（`gpt-oss` 另有 `F16`） | 不再按固定档位名硬匹配，改按仓库清单解析实际文件名 |
| `settings.json` | 20 多个字段 | **36 个**，新增 `hf_token` / `github_mirror` / `accel_nodes` / `preferred_line` / `preferred_ip` | 只读识别；`hf_token` 仅在直连 `huggingface.co` 时附加，`hf-mirror.com` 不带，日志打码 |
| 半成品机制 | `_locmodel_dl_tmp\lmv23\*.part` | 就地 `<file>.part` + `.part.meta` | 新增 `HalfPartScan.cs` 扫描并提示「主程序未完成下载 N MB」，**不接管、不删除、不续传** |
| 图像/视频清单 | 13 条 | 13 条（不变） | 以内置快照为权威清单；exe 解析只做存在性核对，避免旧解析器漏 2 条、把 VAE 记到错误仓库 |
| 落盘目录 | `{目录}\{组织}__{仓库}`、`{目录}\image\{slug}`、`{目录}\image\common` | 不变 | 不变；补充 slug 固定映射（`flux.1-schnell → flux1-schnell` 等），与 LocModel 自身目录完全对齐 |

**内核零改动的边界**：`Downloader.cs`（SHA-256 前 12 位 `313DC3B74651`）与 `SiteProbe.cs`（`1915B5167DD4`）在 v2.2.1 / v2.3 / v2.4.1 / v2.4.2 四版中**字节级完全一致**，`HubBrowserForm.cs` / `HubSearch.cs` / `HubUrlMapper.cs` / `RepoQuants.cs` 在 v2.4.1 → v2.4.2 之间亦未改动。改动集中在 `LocModelProfile.cs`（版本差异表）、`CatalogService.cs`（catalog 解析 / slug / 快照权威）、`Models.cs`（规格字段）、`MainForm.cs`（界面）与新增的 `HalfPartScan.cs`。

界面顶部变为：

```
LocModel 版本：[ 3.5（默认） v ]   探测：D:\...\LocModel.exe · 3.5.0        （切换后自动重载清单与目录规则）
```

---

## 五、v2.2 与 v1.0 的设计差别与原因

| 维度 | v1.0 补丁 | v2.2 下载器 | 为什么变成这样 |
| --- | --- | --- | --- |
| 改动对象 | LocModel 的 `settings.json`（一个字段） | 不碰 LocModel，独立进程直接写模型目录 | LocModel 主进程无任何监听端口、能力全走 Tauri IPC，外部无法调用；不改本体就只能自己做下载 |
| 镜像数量 | 只能 1 个（`custom_mirror` 单值） | 任意多个，一行一个，自动归一化去重 | 单镜像一旦抽风就只能干等；多站点需要程序自己择优 |
| 线路选择 | 本体内部逻辑，用户不可干预 | 下载前自动测速，速度最快者优先，失败自动降级 | 用户诉求是「填几个站点，让它自己挑最快的」，本体做不到 |
| 生效范围 | 只对 LocModel 自己的下载页生效 | 覆盖全部 28 个模型，含本体下载页列不全的依赖文件 | 本体下载页不暴露 `image\common` 等依赖细节，独立器可一次备齐 |
| 断点续传控制 | 受本体策略约束，报错后 `custom_mirror` 会被回写成会员线路，需重启再试 | 自己做 `.part` 续传 + HEAD 预检 + ETag 变化识别 + 416 自愈 | 本体在 3.1.0 引入会员高速线路后会把配置回写，用户难以稳定停留在镜像线路上 |
| 目录控制 | 不管目录 | 可自定义模型目录，与默认目录不一致时红字提示 + 下载前确认 | 外接移动硬盘换盘符会导致 LocModel 配置与实际目录漂移（本机 `G:` → `F:` 就是实例） |
| 风险面 | 改配置（有备份、可回滚、`--dry-run`） | 只写模型文件，不改任何配置 | 两种风险性质不同：补丁要小心配置，下载器要小心文件 |
| 体量 | 18 KB 控制台 | 88 KB 图形界面 | 需求从「修一行」升级到「多站点测速 + 完整下载器」 |
| 依赖 | .NET Framework 4.x（系统自带） | 同左，单文件 | 保持「目标机无需装运行库」 |

一句话概括：**v1.0 是「让本体会走镜像」，v2.2 是「自己成为下载器」。** 前者 30 秒见效但能力受限于本体；后者工程量大但把镜像选择、线路择优、续传、目录全部握在自己手里。这也是为什么 v2.2 保留了 v1.0 的全部结论（hf-mirror 可用、huggingface.co 不可直连），却把「镜像」从一个配置值升级成「可测速的站点列表」。

---

## 六、v2.2 使用方法（详尽）

### 6.1 获取与运行

1. 从 [Releases](https://github.com/jaja1620/locmodel-patch/releases) 下载 `LocModel-Downloader-v2.2.exe`（Release 资产用 ASCII 名以避免中文乱码；仓库内源码目录同名文件为 `v2.2/LocModel下载器_v2.2.exe`）。
2. 双击运行，无需安装、无需运行库（Windows 10/11 自带 .NET Framework 4.8）。
3. 建议放在一个固定目录（例如 `D:\Tools\LocModelDownloader\`），因为站点配置就存在 exe 同目录。

### 6.2 界面总览

| 区域 | 内容 |
| --- | --- |
| 顶栏 | 「重新从 LocModel 获取」按钮、模型目录输入框 + 「浏览...」、「开始下载」/「取消」、版本号 v2.2 |
| 站点栏（v2.2 新增） | 「自定义下载站点」多行输入框、「测速预览」、「恢复默认」、状态行（实时显示各站速度） |
| 左侧 | 分类（全部 / 图像模型 / 视频模型 / 语言·推理）+ 模型列表，共 28 项 |
| 右侧 | 模型标题、仓库、简介、量化档位下拉、「本次将下载的文件」清单、进度条、日志区 |

### 6.3 自定义站点与自动测速择优（v2.2 核心）

1. **默认值**：首次启动输入框已填 `https://hf-mirror.com`，不改也能直接下载。
2. **填多个站点**：一行一个，或用逗号、分号、空格分隔，例如：

   ```
   https://hf-mirror.com
   https://my-mirror.example.com
   https://another.example.net/hf
   ```

3. **自动整理**：缺协议自动补 `https://`、去尾斜杠、协议名统一小写、去重（同协议+域名+端口视为同一站）、非法项丢弃并提示。
4. **测速预览**：点「测速预览」，程序对每个站点发起 `Range: bytes=0-262143` 实读 256 KB 的请求，按「字节 ÷ 耗时」算速度，3 站并发、单站超时 8 秒；状态行显示 `测速：hf-mirror.com 3.21 MB/s | 镜像B 260 KB/s | 镜像C 不可用`，日志区给出线路顺序。
5. **下载时自动择优**：每次点「开始下载」，程序先用**本批任务里最大的文件**路径做探测（与真实下载同构），再按实测速度排序，**用第一名下载**；第一名失败自动降到下一名，成功线路自动提前、失败线路自动降位。
6. **排序规则**：可用站点按速度从快到慢 → 有响应但读不到数据 → 连接失败/超时/HTTP 错误（排最后但仍留作兜底）。
7. **恢复默认**：点「恢复默认」回到单个 `https://hf-mirror.com`。
8. **配置落盘**：站点写入 exe 同目录 `LocModel下载器_站点配置.txt`（一行一个），下次启动自动回填；也可直接编辑该文件。
9. **`*.locmodel.com` 会被排除**：`m1` / `m2` / `activation` / `get.locmodel.com` 是 LocModel 自有 CDN，不代理 HuggingFace 路径（实测对 `/{repo}/resolve/main/...` 返回 404），所以即使你误填进站点栏也会自动排除，只留在自有 CDN 链使用，避免白等一次超时。

### 6.4 下载流程

1. 启动后自动读取：模型目录、端口、镜像设置、全部模型参数。
2. 左侧选分类 → 选模型 → 右侧选量化档位（如 `Q4_K_M`、`BF16`）。
3. 查看「本次将下载的文件」清单：主文件、`mmproj`、`image\common` 依赖全部列出，含大小。
4. 点「开始下载」：先自动测速（进度条显示「正在测速 N 个站点…」），随后用最快线路下载，显示总字节、实时速度、百分比。
5. 「取消」保留 `.part`，下次点「开始下载」自动续传。
6. 已存在且校验通过的文件自动跳过；已有但长度不足的文件自动转续传，不重下。
7. 完成后弹窗提示保存位置，到 LocModel 里刷新模型列表即可看到。

### 6.5 保存位置规则

- 默认 = 从 LocModel `settings.json` 读到的模型目录（本机实测为 `F:\AI-Models`，而配置里写的 `G:` 盘已不存在，下载器以 `settings.json` 为准并如实显示）。
- 可手改或「浏览...」指定自定义目录。
- 自定义目录与 LocModel 默认目录不一致时：顶部红字提示，且开始下载前弹确认框：
  「用户自建目录与默认目录不一致，需用户在locmodel内自行更改默认目录或把下载到自定义目录内容复制到默认目录使用」

### 6.6 常见问题

| 现象 | 原因与处理 |
| --- | --- |
| 点开始下载后要等几秒 | 在测速：单站超时 8 秒、并发 3，站点多时最长约 8 秒，属正常 |
| 某站点显示「不可用」 | 该站点对 HF 路径返回非 200/206 或连不上；它会被垫到队尾，不影响其他站点 |
| 下载速度慢 | 看状态行哪个站点最快；也可只保留最快的一两个站点，减少测速等待 |
| 想用公司/自建反代 | 必须是能对 `/{repo}/resolve/main/{文件路径}` 返回文件的 HF 镜像或反向代理；普通网盘、网页下载页不可用 |
| LocModel 里看不到下载好的模型 | 确认下载目录与 LocModel 默认目录一致；若用了自定义目录，参考上面红字提示处理 |
| 模型目录盘符变了 | 外接硬盘换盘符后 LocModel 配置不会同步，需在其设置里改成实际盘符，或在本程序里选对目录 |

---

## 七、v1.0 使用方法（保留）

v1.0 仍完整可用、文件与 Release 资产均保留。完整说明见 [README-v1.0.0.md](README-v1.0.0.md)，速览：

```
locmodel_patch.exe                 # 自动定位并修复
locmodel_patch.exe --dry-run       # 只预览要改什么，不写盘
locmodel_patch.exe --path "..."    # 指定 settings.json 路径或其目录
```

退出码：`0` 成功（含「已是目标值」）｜`1` 未知错误/参数错误｜`2` 找不到配置｜`3` 没有 `custom_mirror` 键｜`4` 写入或校验失败（已回滚）｜`5` LocModel 进程关不掉。

注意：3.1.0 起 LocModel 下载失败会把 `custom_mirror` 回写成会员线路，此时**不要点重试**，直接退出程序重开再点下载即可。

---

## 八、兼容性与测试范围（必读）

| 项目 | 测试结论 |
| --- | --- |
| **v2.2.0 下载器** | **仅在 LocModel 3.1.0 上实测通过；未在 LocModel 2.1.0 上测试。** 2.1.0 环境的目录规范、配置字段是否完全一致未经验证，请自行评估后再用 |
| **v2.3.0 下载器** | **LocModel 3.3.0 ✅ 本机实测**（29 个模型、13 条图像/视频清单与 exe 内嵌 catalog 逐条比对一致）；切到 3.1 档位后与 v2.2.1 的 28 个模型清单一致 |
| **v1.0.0 补丁** | LocModel 2.1.0 ✅ 与 3.1.0 ✅ 均本机实测通过 |
| 操作系统 | Windows 10 / 11（.NET Framework 4.8，系统自带） |
| 网络 | `hf-mirror.com` 可用；`huggingface.co` 在本机 DNS 受限，仅作最后兜底 |

> 说明：v2.2 读取的配置路径与字段沿用 3.1.0 实测结果（`%LOCALAPPDATA%\LocModel\settings.json`）。如果你在 2.1.0 上使用并遇到目录识别异常，请在 Issues 反馈版本号与界面提示。

---

## 九、验证记录

**v3.1.5 升级验证（LocModel 3.6.5 适配 + 对口型子系统纳管）**

| 项 | 内容 | 结果 |
| --- | --- | --- |
| 编译与自检 | `--selftest` 31 项（档位策略 5、3.6.5 检测 2、对口型清单 2 档结构 16、token 打码长短用例、settings 只读渲染、v3.1.3 回归 9、图像/视频清单重叠） | 31/31 全过，`selFail=0`；`Default=3.6.5`、`Versions=[3.6.5,3.6,3.5,3.3,3.1]` |
| 镜像覆盖核对 | `tools/mirror_check.py` 逐条 HEAD 对口型 18 个权重文件（`hf-mirror.com` 与 `huggingface.co` 各 18 次） | hf-mirror **18/18 状态 200** 且 `Content-Length` 与清单字节一致；huggingface.co 18 条全部不可达 → **站点栏位不增改**，仅补说明文案 |
| 绝对地址通道 | 程序包 `github.com/.../main.zip`、`codeload.github.com/.../zip/main`、`dl.fbaipublicfiles.com/demucs/hybrid_transformer/955717e8-8726e21a.th` | 三条均 200，人声分离模型 84,141,911 字节 |
| 对口型下载回归 | `--lipget` 走本地 Range 服务（真实 206 / 分片 / 校验），5 场景：全新下载 6 文件、幂等重跑、1 MiB `.part` 续传、站点全错、栏位两站回落 | 全部符合预期：MD5 与字节全对、`.part` 残留 0、全错场景 `ok=False` 不换线、两站场景命中次站 |
| 界面自检 | `--uicheck` 离屏渲染 | 标题 `v3.1.5`、默认档位 `LocModel 3.6.5（默认）`、「配置只读查看」「对口型模型」按钮就位、无控件重叠 |
| 配置只读 | `SettingsView.cs` 全文件审查（无 `File.Write*` / 无 settings 回写）+ `--selftest` 的 `SettingsReadOnly` 段 | 通过；`hf_token` 展示为 `sk-1********cdef` 形式（长度 ≤ 8 全星号） |
| 未覆盖（如实声明） | 37.4 GB / 24.1 GB 全量真实下载、LocModel 3.6.5 本体联调 | F 盘模型盘离线（`F:\AI-Models` 不可达），本轮只在 C/D 临时目录做替身回归；本机未装对口型子系统，未跑真正的口型推理 |

**v2.3 升级验证（LocModel 3.3.0 适配）**

| 项 | 内容 | 结果 |
| --- | --- | --- |
| 版本探测 | 3.3.0 安装于 `D:\Users\jiasi\AppData\Local\LocModel`，配置在 `%LOCALAPPDATA%\LocModel\settings.json`，模型目录 `F:\AI-Models` | 与 3.1 定位规则一致，无需改定位逻辑 |
| 清单解析 | 从 `LocModel.exe` 内嵌字符串解析图像/视频条目，与内置快照 diff | 解析 11 条命中、刷新 4 条、新增 0、移除 0，最终 13 条与 3.3 实际一致（新增 `minimax-h3-image`） |
| 文件名解析 | 6 个代表模型走 HF siblings 解析真实文件名（含双文件模型 wan2.2-i2v-a14b、多依赖 ltx-2.3） | 6/6 全部命中，含 `minimax_h3_image_pruned-Q3_K_M.gguf` |
| 界面自检 | `--selftest` 在 3.3 档位 / 3.1 档位分别运行 | 3.3：29 项（图像 7 + 视频 6 + 语言 16）；3.1：28 项（图像 6 + 视频 6 + 语言 16） |
| 内核回归 | 用 v2.3 源码重编 `Test24` 跑断点续传与超大文件校验 | 跨进程续传 10/10 PASS、8–100 GiB 校验 5/5 PASS；连接级中断用例（B 组）v2.2.1 与 v2.3 输出**完全一致**，属测试服务器与 .NET 交互的既有行为，非本次改动引入 |
| 端到端 | 真实网络下载 `minimax-h3-image` 的主模型 + 共享组件 | 主模型 8.77 GB、共享组件 18.22 GB 均完整落盘并通过校验，落盘位置符合 `image\{id}\` 与 `image\common\` 规则 |
| 内核一致性 | `Downloader.cs` / `SiteProbe.cs` 与 v2.2.1 比对 | SHA-256 一致，未改一字 |

**v2.2.1 缺陷修复（用户反馈：16.8 GB 模型下到 100% 后报「校验失败(数组维度超过了支持的范围。)」，删掉半成品从头再下，如此反复）**

| 项 | 内容 |
| --- | --- |
| 现象 | 日志 `校验失败(数组维度超过了支持的范围。)，丢弃半成品（第 1 次）` / `（第 2 次）` → `连续校验失败 2 次，放弃该文件` → `全部线路失败`，重新点开始又从 0 开始，永远下不完 |
| 根因 | 校验函数取文件头时写作 `new byte[Math.Min(4096, (int)len)]`，`len` 是 `long` 文件字节数；当文件大小对 4 GiB 取模落在 **2 GiB ~ 4 GiB** 区间时，强转 `int` 溢出成负数，`new byte[负数]` 抛 `OverflowException`（文案正是「数组维度超过了支持的范围。」）。该异常被外层 `catch (Exception)` 当作数据损坏处理，于是删 `.part` 重下 |
| 影响面 | 所有长度命中该区间的模型：Qwen_Image-Q2_K（7.06 GB）、Qwen_Image-Q6_K（16.82 GB）等会必败；Q3_K_M（9.68 GB）、Q4_K_M（13.07 GB）恰好躲过，所以表现为「有的模型能下、有的死活不行」。另外 4 GiB 整数倍时 `(int)len` 归零、读不到文件头，属同一隐患 |
| 修复 1 | 头部长度全程按 `long` 计算（`Math.Min(len, 4096L)`）后再收窄为 `int`，并改为循环读满文件头 |
| 修复 2 | 校验异常分级：只有 `InvalidDataException`（HTML 页面、过小、魔数错、safetensors 头非法、SHA 不符）才判定数据不可信并清理半成品；其余异常（溢出/IO/内存）一律**保留半成品**并终止该文件，不再无意义重下数 GB |
| 验证 | ① 大文件校验单测 16/16 PASS（2.5 GiB、7.06 GB、16.82 GB、9.68 GB、13.07 GB、4 GiB 边界全部通过，256 B HTML / 过小文件 / GGUF 魔数错 / safetensors 头非法 / SHA 不符仍被正确拒绝）；② 端到端真实下载 2.2 GB 文件 6/6 PASS（新下载落盘正确、无「校验失败」、二次运行已存在文件校验通过直接跳过）；③ v2.2 原有 27 用例回归 27/27 PASS（站点解析、测速排序、择优选线、真实 hf-mirror 测速） |

**v2.2 站点功能回归（27 用例全部 PASS）**

| 组 | 覆盖 | 结果 |
| --- | --- | --- |
| A 解析/归一化/链路合并 | 15 项（混合分隔、补协议、去尾斜杠、去重、自有 CDN 排除、HF 链顺序、配置落盘回读等） | 15/15 |
| B 测速排序 | 5 项（死端口垫底、最快站第一 3.21 MB/s / 78 ms、慢站第二 260 KB/s / 984 ms、快慢差 >3×） | 5/5 |
| C 择优后选线下载 | 5 项（31,457,280 字节校验一致、请求只落最优站、慢站零使用、首选失联自动降级） | 5/5 |
| D 真实 hf-mirror.com 测速 | 2 项（161 KB/s / 1593 ms，<10 s 完成） | 2/2 |

**v2.2 下载链路回归（沿用 v2.1，全部 PASS）**：本地 Range 服务端 21 场景（新下载、续传、ETag/Last-Modified 变化、半成品超长/等长、不支持 Range、取消后续传、目标残缺接管、期望大小未知、校验失败上限、单文件失败不中断整批、HEAD 拦截换版、磁盘不足预检、SHA 一致/不符、跨镜像 ETag 判定等）+ 真实链路 4 场景（全新下载、正确 ETag 续传、伪造 ETag 识别换版、半成品超长丢弃重下）。

**v1.0 实测（历史记录）**：LocModel 2.1.0 与 3.1.0 均通过；3.1.0 抓包确认下载经 `hf-mirror.com` 解析后直达 xet-bridge CDN，全程未直连 `huggingface.co`；5690 MB 模型实测 448–567 KB/s、`.part` 续传正常；镜像线路下速率 25–90 MB/s。

---

## 十、自行编译

**v2.2 下载器**（Windows 自带编译器，无需 Visual Studio）：

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v2.2.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  v2.2\src\Downloader.cs v2.2\src\CatalogService.cs v2.2\src\Models.cs v2.2\src\MainForm.cs v2.2\src\SiteProbe.cs
```

**v2.3 下载器**（多一个 `LocModelProfile.cs`）：

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v2.3.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  v2.3\src\Downloader.cs v2.3\src\CatalogService.cs v2.3\src\Models.cs v2.3\src\LocModelProfile.cs v2.3\src\MainForm.cs v2.3\src\SiteProbe.cs
```

**v3.0.0 下载器**（新增 `DownloadSeg.cs`、`DownloadConfig.cs`，多引两个压缩程序集）：

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.0.0.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.0.0\src\*.cs
```

**v3.1.0 下载器**（新增 `DepsPrecheck.cs`，编译命令与 v3.0.0 相同，仅换目录）：

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.1.0.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.1.0\src\*.cs
```

**v3.1.1 下载器**（新增 `TextCatalogSnapshot.cs`，编译命令与 v3.1.0 相同，仅换目录）：

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.1.1.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.1.1\src\*.cs
```

**v3.1.2 下载器**（新增 `HubDestStore.cs`，编译命令与 v3.1.1 相同，仅换目录）：

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.1.2.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.1.2\src\*.cs
```

**v3.1.5 下载器**（新增 `LipsyncCatalog.cs` / `LipsyncEngine.cs` / `LipsyncForm.cs` / `SettingsView.cs`，编译命令与 v3.1.2 相同，仅换目录）：

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.1.5.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.1.5\src\*.cs
```

v3.1.3 / v3.1.5 都不需要额外引用；源码目录中若有取证用的 probe 文件，编译前请先移出 `src\`（仓库内 `v3.1.5/src` 只含 21 个正式 `.cs`）。

**v1.0 补丁**：

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /codepage:65001 /optimize+ /out:locmodel_patch.exe locmodel_patch.cs
```

---

## 十一、版本保留与发布策略

- 仓库中 v1.0 的全部文件（`locmodel_patch.cs`、`locmodel_patch.exe`、`build.cmd`、`README-v1.0.0.md` 原说明书）**原样保留**。
- Release 中 `v1.0.0` **保留不删**，新版本以新 tag 追加发布。
- v2.2 以 `v2.2/` 目录与 `v2.2.0` Release 并存，不覆盖任何旧资产。
- v3.0.0 以 `v3.0.0/` 目录与 tag `v3.0.0` 保留；该版本**只打了 tag，未创建 Release**，可执行文件与源码包直接放在仓库根目录（`LocModel-Downloader-v3.0.0.exe`、`locmodel-downloader-v3.0.0-src.zip`）与 `v3.0.0/` 中下载。

- v3.1.0 以 `v3.1.0/` 目录与 tag `v3.1.0` 保留（`LocModel-Downloader-v3.1.0.exe`、`locmodel-downloader-v3.1.0-src.zip`、`RELEASE_NOTES_v3.1.0.md`）。
- v3.1.1 以 `v3.1.1/` 目录与 tag `v3.1.1` 保留（`LocModel-Downloader-v3.1.1.exe`、`locmodel-downloader-v3.1.1-src.zip`、`RELEASE_NOTES_v3.1.1.md`）。
- v3.1.2 以 `v3.1.2/` 目录、根目录 `LocModel-Downloader-v3.1.2.exe` / `locmodel-downloader-v3.1.2-src.zip` 与 `RELEASE_NOTES_v3.1.2.md` 保留；**v3.1.2 未打 tag、未创建 Release**（其说明文件内早期记录的「Release 作为 Latest」为当时的计划，实际以本行为准）。
- v3.1.3 以 `v3.1.3/` 目录、根目录 `LocModel-Downloader-v3.1.3.exe` / `locmodel-downloader-v3.1.3-src.zip` 与 `RELEASE_NOTES_v3.1.3.md` 留档，**不打 tag、不发 Release**。
- v3.1.5 以 `v3.1.5/` 目录与 tag `v3.1.5` 保留，并创建 GitHub Release 作为 **Latest**（`LocModel-Downloader-v3.1.5.exe`、`locmodel-downloader-v3.1.5-src.zip`、`RELEASE_NOTES_v3.1.5.md`）；v3.1.2 及更早版本的目录与既有 Release 资产一律不删、不覆盖，仅 v3.1.1 失去 Latest 标记。

| Release | 资产 |
| --- | --- |
| `v1.0.0` | `locmodel_patch.exe`、`locmodel_patch.cs`、`README.md`（原文） |
| `v2.2.0` | `LocModel-Downloader-v2.2.exe`、中文使用说明 `README_zh-CN.md`、English README `README_EN.md`、源码 `locmodel-downloader-v2.2-src.zip` |
| `v2.2.1` | `LocModel-Downloader-v2.2.1.exe`、源码 `locmodel-downloader-v2.2.1-src.zip`、说明 `RELEASE_NOTES_v2.2.1.md` |
| `v2.3.0` | `LocModel-Downloader-v2.3.exe`、源码 `locmodel-downloader-v2.3-src.zip`、说明 `RELEASE_NOTES_v2.3.md` |
| `v3.1.0` | `LocModel-Downloader-v3.1.0.exe`、源码 `locmodel-downloader-v3.1.0-src.zip`、说明 `RELEASE_NOTES_v3.1.0.md` |
| `v3.1.1` | `LocModel-Downloader-v3.1.1.exe`、源码 `locmodel-downloader-v3.1.1-src.zip`、说明 `RELEASE_NOTES_v3.1.1.md` |
| `v3.1.5` | `LocModel-Downloader-v3.1.5.exe`、源码 `locmodel-downloader-v3.1.5-src.zip`、说明 `RELEASE_NOTES_v3.1.5.md` |

v3.1.2 与 v3.1.3 **只有仓库目录**（`v3.1.2/`、`v3.1.3/` + 根目录 exe / src.zip / RELEASE_NOTES），未打 tag、未创建 Release。

---

## 十二、免责声明与许可

- 本项目与 LocModel 官方无关，为第三方独立工具；不改动 LocModel 程序本体，不绕过其任何授权与会员机制。
- v1.0 会修改 `settings.json`（自动备份、可回滚，且提供 `--dry-run`）；v2.2 只写模型文件与自身配置。请在了解行为后使用。
- 模型文件的版权与许可遵循其上游仓库（HuggingFace 各仓库）规定。
- 许可：见 [LICENSE](LICENSE)。

*（内容由AI生成，仅供参考）*
