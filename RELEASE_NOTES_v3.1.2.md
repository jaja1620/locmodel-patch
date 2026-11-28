---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 798659b1f59514cb753b383c1f423b09_a5afa8fae74d11f1b0c5525400cd780f
    ReservedCode1: 057vcn3f4kld/c37ruBWUWdzuJ7+R/8pmmJr3ZtaXBJ25m2vWFsVBx9tfSEKenhUbiFYPGesRCK6MXP73DGrh76ePDNituURDFRdfdmmhSjLuASY1NAZObBKuXmTp5j3dhg/Ke468DDH7RaFF8hSd/Z7uzR99Hdz8ffJoTX/5TclruKwUQaodKaTWMc=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 798659b1f59514cb753b383c1f423b09_a5afa8fae74d11f1b0c5525400cd780f
    ReservedCode2: 057vcn3f4kld/c37ruBWUWdzuJ7+R/8pmmJr3ZtaXBJ25m2vWFsVBx9tfSEKenhUbiFYPGesRCK6MXP73DGrh76ePDNituURDFRdfdmmhSjLuASY1NAZObBKuXmTp5j3dhg/Ke468DDH7RaFF8hSd/Z7uzR99Hdz8ffJoTX/5TclruKwUQaodKaTWMc=
---

# LocModel 下载器 v3.1.2 发布说明

发布日期：2026-11-23
基线：LocModel 下载器 v3.1.1（源码见仓库 `v3.1.1/`，tag `v3.1.1`）
产物：`LocModel-Downloader-v3.1.2.exe`（227 KB）/ `locmodel-downloader-v3.1.2-src.zip`

## 一、本版核心 ①：在线搜索可自行选择「下载到」

v3.1.1 及更早版本，在线搜索窗口下载的落点固定为「主界面模型目录 `{模型目录}\{owner}__{仓库名}`」，想换盘只能先回主界面改模型目录 —— 而模型目录同时决定了 LocModel 读取模型的位置，改它会影响本体。

v3.1.2 在在线搜索窗口新增「下载到」单选区：

| 选项 | 落点 | 是否影响主界面 |
| --- | --- | --- |
| 下载到主界面目录（默认，与旧版行为一致） | `{主界面模型目录}\{owner}__{仓库名}` | 否 |
| 下载到自定义目录 | `{自定义目录}\{owner}__{仓库名}` | 否，主界面「模型目录」栏位与 LocModel 配置均不被改写 |

配套细节：

- **浏览按钮**：`浏览…` 弹目录选择框，选中后写入路径框；路径框也可直接粘贴/输入。
- **实时落点提示**：单选区下方状态标签随选择实时刷新，主界面模式显示「实际落盘：…」，自定义模式显示「实际落盘：…」或「目录不存在，开始时将自动创建」。
- **自定义目录为空时拦截**：选择自定义但目录为空，点「开始下载」会提示「选择了「自定义目录」但目录为空，请先点「浏览…」选择，或改选「主界面目录」」，焦点落到浏览按钮，**任务不提交**，窗口不关闭。
- **路径归一化**：粘贴带引号的路径、带尾部分隔符的路径、含非法字符的路径统一清洗后再使用。
- **记忆上次选择**：选择写在 exe 同目录的 `LocModel下载器_在线搜索下载目录.txt`（`mode=0/1` + `dir=`），下次打开窗口自动恢复；该文件缺失或损坏时回落「下载到主界面目录」，不报错、不阻塞。
- **按钮文案统一**：主按钮文案固定为「开始下载」（原先会随所选条目名称变化），避免与新增的单选区混淆。

## 二、本版核心 ②：下载任务串行排队（A 方案）

旧版靠一个 `_busy` 标志拦第二笔任务，弹「已有任务在执行」，模型多的时候只能一个一个点。

v3.1.2 改为**队列串行执行**：任务入队后按 FIFO 逐个交给下载内核，内核侧仍是一次只跑一个任务（分片/续传/校验逻辑完全不变）。

| 规则 | 行为 |
| --- | --- |
| 队列上限 | 8 个未开始任务；已满时拒绝入队并提示「队列已满（上限 8 个未开始任务），请等待完成或点「清空队列」」 |
| 重复判定 | 同「仓库 + 量化档 + 目标目录」视为重复，跳过并提示；同仓库但目标目录不同**不算**重复 |
| 入队提示 | 「已加入队列，等待 N 个任务（含本次）」，同时写入运行日志 |
| 状态显示 | 主界面日志区上方新增「下载队列：…」一行，空闲时显示「空闲（最多排 8 个）」，忙时显示「正在下载：owner/repo 量化档 · 等待 N 个 / 上限 8」 |
| 清空队列 | 新增「清空队列」按钮，只移除**尚未开始**的任务，正在执行的那个不受影响 |
| 取消下载 | 「取消下载」只取消**当前**任务，队列继续往下跑；不再像旧版那样一并清空 |
| 完成汇总 | 队列全部跑完后弹一次汇总（成功 / 失败 / 取消计数），明细写运行日志；旧版是每笔任务各弹一次 |
| 覆盖范围 | 在线搜索窗口的下载、主界面「开始下载」（含「下载并转换」）共用同一个队列 |

同时放开了「下载中不能打开在线搜索窗口」的限制：队列执行期间可以继续搜索并往队尾加任务。

## 三、版本号与展示修正

- 主界面窗口标题：`LocModel 模型下载器 v3.1.0` → `v3.1.2`（v3.1.1 遗留未更新）。
- 在线搜索窗口标题：`在线搜索 HuggingFace · LocModel 模型下载器 v2.4` → `v3.1.2`（自 v2.4 遗留）。
- `LocModel下载器_下载配置.txt` 头部注释版本号同步为 v3.1.2。

## 四、变更文件与内核对比

| 文件 | v3.1.2 SHA-256 前 12 位 | 与 v3.1.1 对比 |
| --- | --- | --- |
| `MainForm.cs` | `BAAAE1B1F732` | 变更（下载队列、`BuildHubItems`、`DownloadFromHub`/`DownloadAndConvert` 增 destDir 重载、队列行 UI、`--hubget` 第 4 参数） |
| `HubBrowserForm.cs` | `9DC33AFC9C5B` | 变更（「下载到」单选区 + 浏览 + 状态标签 + 记忆读写、落盘基目录、标题与按钮文案） |
| `HubDestStore.cs` | `F37CD7FAB8EA` | **新增**（下载目录记忆：Load / Save / Normalize / IsReady / EffDir / ModelDirOf） |
| `LocModelProfile.cs` | `96D4E1558815` | 仅首行版本号注释 |
| `DownloadConfig.cs` | `DD108A6F0AEA` | 仅版本号注释（首行与配置头） |
| `Downloader.cs` | `A94FA91A41AB` | 相同 |
| `DownloadSeg.cs` | `C58569AD2812` | 相同 |
| `CatalogService.cs` | `2CB04A9168F3` | 相同 |
| `SiteProbe.cs` | `1915B5167DD4` | 相同 |
| `ConvertService.cs` | `B39DBDB40D4F` | 相同 |
| `DepsPrecheck.cs` | `0AED8E5E288C` | 相同 |
| `HalfPartScan.cs` | `A5C85FD8E98E` | 相同 |
| `Models.cs` | `0B6888F46ECC` | 相同 |
| `RepoQuants.cs` | `4F62077CBA5E` | 相同 |
| `HubSearch.cs` | `1FB1A7AD18B5` | 相同 |
| `HubUrlMapper.cs` | `C8C9C496F730` | 相同 |
| `TextCatalogSnapshot.cs` | `8A59E9077C9B` | 相同 |

**内核零改动**：`Downloader.cs`、`DownloadSeg.cs`、`CatalogService.cs` 三个承载分片调度、连接数自适应、`.part` 续传、HEAD 预检、ETag 识别、416 自愈与校验的文件哈希与 v3.1.1 **完全一致**；队列只在外层做「一次交一个任务」的调度，不改动下载内核的任何判断。

## 五、验证记录

### 5.1 自检 `--selftest`

```
selOk=30  selFail=0  specRows=16  overlap=0
-- v3.1.2 在线搜索下载目录 + 队列 --
  Load.无文件=OK   mode=0  dir=[]
  RoundTrip=OK     mode=1  dir=[…\自定义 目录]
  IsReady=OK       自定义目录=False  主目录空=True  mode=1 dir=[]
  Normalize=OK     quoted=[…]  empty=[]  bad=[C:\abc]
  Land=OK          destDir=null→[E:\LocModels\owner__repo]  custom→[…\自定义 目录\owner__repo]
  BuildHubItems=OK mainDir=…\owner__repo  size=[4096,512]
  队列FIFO=OK      count=3  head=o/a/Q4  重复被拒=True
  队列上限=OK      入队=5  count=8  饱满被拒=True
  取消不清队列=OK  before=8  after=8
  清空队列=OK      count=0
  v312.sum ok=10  fail=0
```

新增 10 项断言覆盖：无配置文件默认主界面模式、mode/dir 保存加载往返、自定义空目录判定未就绪、路径归一化（去引号/转绝对/滤非法字符）、`destDir=null` 与自定义目录两种落点、在线搜索清单落点、队列 FIFO 与去重、上限 8 拒绝入队、取消不清队列、清空队列。

### 5.2 界面离屏自检

主界面 `--uicheck`：新增「下载队列」行与「清空队列」按钮渲染正常，无重叠、无截断，标题显示 v3.1.2。

在线搜索窗口 `--hubcheck`：

```
HubSize=1060x740
  Layout=OK overlap=0 txtLog.Bottom=617 panelH=627
  MainMode=OK txtDest.Enabled=False browse.Enabled=False state=[实际落盘：F:\AI-Models\audio-cpp__audio.cpp-gguf]
  CustomMode=OK txtDest.Enabled=True text=[] state=[请先点「浏览…」选择目录]
```

新增控件与原控件的纵坐标排布无重叠，原 `chkMmproj` / `btnDownload` / 提示行 / 日志框整体下移且底边锚定正确；主界面模式下路径框与浏览按钮置灰。

### 5.3 真实下载回归（`--hubget` 端到端，走 `DownloadFromHub` 全链路）

| 场景 | 参数 | 结果 |
| --- | --- | --- |
| 主界面目录 | `--hubget bert-base-uncased config.json <rt>\main1` | exit=0，落盘 `main1\bert-base-uncased\config.json` 570 B，`hubDest=[]`（回落主界面目录） |
| 自定义目录 | `--hubget bert-base-uncased config.json <rt>\main2 <rt>\custom` | exit=0，落盘 `custom\bert-base-uncased\config.json` 570 B，`main2` 下**零文件**（未污染主界面目录） |

两例控制台均输出 `start=True note=[已加入队列，等待 1 个任务（含本次）]`，证明任务确实经队列链路提交并完成。

### 5.4 真实模型目录只读影响核验

自检与界面自检前后对 `F:\AI-Models` 做快照比对：`108 个文件 / 623,151,583,546 字节` 前后完全一致，本版行为未对真实模型目录产生任何写入或改动。

## 六、升级建议

- 从 v3.1.1 升级：直接替换 exe 即可。exe 同目录的 `LocModel下载器_下载配置.txt`（连接数 / 最小分片）、`LocModel下载器_站点配置.txt`（下载站点）格式未变，继续沿用。
- 新增的 `LocModel下载器_在线搜索下载目录.txt` 首次运行自动生成，无需手工创建；也可手工编辑 `mode=`（0 主界面目录 / 1 自定义）与 `dir=`（绝对路径）。
- **行为变化提醒**：旧版「已有任务在执行」的拦截提示改为排队；若你在脚本或习惯上依赖「第二笔任务被拒绝」，请注意现在会自动入队执行。

## 七、历史版本保留策略

- v3.1.2 以 `v3.1.2/` 目录与 tag `v3.1.2` 保留，并创建 GitHub Release 作为 **Latest**（`LocModel-Downloader-v3.1.2.exe`、`locmodel-downloader-v3.1.2-src.zip`、`RELEASE_NOTES_v3.1.2.md`）。
- v3.1.1 及更早版本的 tag、目录、Release 资产**一律不删、不覆盖**，仅 v3.1.1 失去 Latest 标记。
*（内容由AI生成，仅供参考）*
