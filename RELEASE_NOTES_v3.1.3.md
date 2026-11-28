---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 798659b1f59514cb753b383c1f423b09_2ae020dce75e11f1b0c5525400cd780f
    ReservedCode1: MWdOGmD47VT56hZazNkoVvM5z9Ab/bcvIF/nRkH31JNZBudaTfmMNmOTov36yH5bRocAwARxAavIi9Okk9od6CmmKQVzSvHWglM2MV4Ej8XnN9vl6O/ztL+WSdPjAzrKZVGtsny+NSK03ir9N9YqWtlRx/7W2I3r5SHsWprzcSaezjSZ7O3/xgamWtE=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 798659b1f59514cb753b383c1f423b09_2ae020dce75e11f1b0c5525400cd780f
    ReservedCode2: MWdOGmD47VT56hZazNkoVvM5z9Ab/bcvIF/nRkH31JNZBudaTfmMNmOTov36yH5bRocAwARxAavIi9Okk9od6CmmKQVzSvHWglM2MV4Ej8XnN9vl6O/ztL+WSdPjAzrKZVGtsny+NSK03ir9N9YqWtlRx/7W2I3r5SHsWprzcSaezjSZ7O3/xgamWtE=
---

# LocModel 下载器 v3.1.3 发布说明

发布日期：2026-11-24
基线：LocModel 下载器 v3.1.2（源码见仓库 `v3.1.2/`，tag `v3.1.2`）
产物：`LocModel-Downloader-v3.1.3.exe`（214 KB）/ `locmodel-downloader-v3.1.3-src.zip`
状态：本地已完成编译、自检与打包，**尚未推送远端、未打 tag、未创建 Release**

本版只修交互与调度层，**下载 / 续传 / 校验内核零改动**。

## 一、修复 ①：主界面「开始下载」不再整段禁用

v3.1.2 只要一点「开始下载」，按钮就从提交一路灰到下载结束，队列机制形同虚设 —— 想连排第二个任务必须先等第一个跑完。

v3.1.3 拆开「提交窗口」与「执行期间」两个状态：

| 阶段 | v3.1.2 | v3.1.3 |
| --- | --- | --- |
| 清单解析（网络取 HF tree） | `btnDownload.Enabled=false`，直到下载结束才恢复 | 仅置 `_submitting=true` 拦重复点击，解析一结束立刻恢复 |
| 下载执行中 | 保持禁用 | **保持可用**，可继续提交进队列 |
| 转换执行中 | 保持禁用 | **保持可用**，可继续提交进队列 |

`_submitting` 只在清单解析与入队这两个窗口内为 true，覆盖「连续快速点击导致重复解析 / 重复入队」。

## 二、修复 ②：`_busy` 与「取消下载」按钮由统一状态方法裁决

v3.1.2 在 4 处回调里各自无条件写 `_busy` 与 `btnCancel.Enabled`，与队列状态机互相打架：转换完成回调把 `_busy` 置 false 时，队尾任务可能刚好被调度；「取消中」态也会被后续回调覆盖回可用。

v3.1.3 收口为一个裁决入口 `UpdateActionState()`：

```csharp
// v3.1.3：按钮与 busy 状态的唯一裁决入口
void UpdateActionState()
{
    if (btnDownload != null) btnDownload.Enabled = !_submitting;
    if (btnCancel != null)   btnCancel.Enabled   = (_current != null) && !_cancelling;
}
```

| 状态源 | 写入点（唯一） |
| --- | --- |
| `_busy` | 只由 `RunDownload` / `RunConvertJob` 进场写 true，`FinishJob` 统一收口写 false |
| `_cancelling` | 只由 `RequestCancel` 写 true，`FinishJob` 收口写 false |
| `_current` | 只由 `StartNextJob` 置位、`FinishJob` 清空 |
| `btnCancel.Enabled` | 只由 `UpdateActionState()` 依 `_current != null && !_cancelling` 推导 |

被移除的 4 处无条件赋值（行号为 v3.1.2 源码行号）：

| 位置 | 原语句 | v3.1.3 处理 |
| --- | --- | --- |
| L1253 清单解析异常/空清单分支 | `_busy=false; btnDownload.Enabled=true; btnCancel.Enabled=false;` | 改为 `_submitting=false; UpdateActionState();` |
| L1266 入队前落盘分支 | `_busy=false; btnDownload.Enabled=true; btnCancel.Enabled=false;` | 改为 `_submitting=false; UpdateActionState();` |
| L641 `StartConvert` 进场 | `_busy=true; btnDownload.Enabled=false; btnCancel.Enabled=true;` | 改为 `_busy=true; _cancelling=false; UpdateActionState();` |
| L1404 转换完成回调 | `_busy=false; btnDownload.Enabled=true; btnCancel.Enabled=false;` | 删除，改由 `FinishJob` 统一收口 |
| L1351 `RunDownload` 进场 | `_busy=true; btnDownload.Enabled=false;` | 改为 `_busy=true; _cancelling=false; UpdateActionState();` |
| L约1390 `RunDownload` 内 | `btnCancel.Enabled=true;` | 删除，改为 `UpdateActionState();` |

附带修正：`QueueStatusText()` 在「有等待任务但当前任务尚未取走」的瞬间不再显示「空闲」，改为「待调度」，避免与「等待 N 个」自相矛盾。

## 三、修复 ③：在线搜索窗口显示队列状态 + 拒绝提示

v3.1.2 在线搜索窗口提交后只在日志里留一行，队列满 / 重复提交被拒时也只是一行灰字，很容易误以为已开始下载。

v3.1.3 在该窗口新增一行 `lblQueueState`（每秒刷新一次）：

- 常态：`下载队列：正在执行 owner/repo 量化档 · 等待 N 个 / 上限 8`，空闲时 `下载队列：空闲（最多排 8 个）`，待调度时 `下载队列：待调度 · 等待 N 个 / 上限 8`
- 队列已达上限：文字变橙红并追加 `· 队列已满，等完成或到主界面点「清空队列」`
- `EnqueueJob` 返回 false（队列满 / 同仓库同量化档同目标目录重复）：状态行显示红色 `提交失败：<具体原因>`，同时弹提示框说明原因；成功提交后立即刷新状态行
- 窗口关闭时停止并释放刷新定时器

## 四、修复 ④：切换仓库后刷新「实际落盘」路径

v3.1.2 的落点提示只在切换「下载到」单选、载入记忆、点浏览时刷新，**切换仓库不会刷新** —— 选了 A 仓库看到 A 的落盘路径，再切到 B 仓库，标签仍显示 A 的路径。

v3.1.3 在 `OnSelectRepo()` 末尾补 `UpdateDestState()`，仓库一换，落盘路径立即按新仓库名重算（`{基目录}\{owner}__{repo}`）。

## 五、修复 ⑤：单实例互斥

下载队列与下载内核都在进程内，双开会让两个实例同时往同一模型目录落盘、同名 `.part` 互相踩踏。

v3.1.3 在程序入口加命名互斥：

- 互斥名 `Local\LocModelDownloader_SingleInstance_v3`（`Local\` 前缀限定当前登录会话）
- 非首实例：枚举同名进程，取到主窗口句柄后 `ShowWindow(SW_RESTORE)` + `SetForegroundWindow` 激活已有窗口，本进程直接退出
- 取不到窗口句柄（首实例还在初始化）时，弹「LocModel 下载器已在运行中，请从任务栏切换过去」后退出
- 互斥对象创建异常时按首实例放行，不阻塞用户启动
- **`--selftest` / `--segtest` / `--hubcheck` / `--hubget` / `--conv` / `--uicheck` / `--setconn` 等 `--` 开头的命令行模式一律绕开互斥**，回归脚本不会被正在运行的界面实例拦住

## 六、版本号统一

| 位置 | v3.1.2 | v3.1.3 |
| --- | --- | --- |
| 主窗口标题 | `LocModel 模型下载器 v3.1.2` | `LocModel 模型下载器 v3.1.3` |
| 在线搜索窗口标题 | `在线搜索 HuggingFace · LocModel 模型下载器 v3.1.2` | `… v3.1.3` |
| `LocModel下载器_下载配置.txt` 头注释 | `v3.1.2` | `v3.1.3` |
| `LocModel下载器_在线搜索下载目录.txt` 头注释 | `v3.1.2` | `v3.1.3` |
| `DownloadConfig.cs` / `HubDestStore.cs` / `LocModelProfile.cs` / `MainForm.cs` 首行注释 | `v3.1.2` | `v3.1.3` |

新增代码统一以 `v3.1.3：` 前缀注释，历史 `v3.1.2：` 注释原样保留，便于按版本追溯。

## 七、变更文件与内核对比

| 文件 | v3.1.3 SHA-256 前 12 位 | v3.1.2 | 对比 |
| --- | --- | --- | --- |
| `MainForm.cs` | `22833AF9CD6A` | `BAAAE1B1F732` | 变更（① ② ⑤ + v3.1.3 自检段 + 版本号） |
| `HubBrowserForm.cs` | `75276A592D88` | `9DC33AFC9C5B` | 变更（③ 队列状态行与拒绝提示、④ `UpdateDestState()`、标题） |
| `HubDestStore.cs` | `4C58F0AF18FD` | `F37CD7FAB8EA` | 仅版本号注释 |
| `DownloadConfig.cs` | `2EC276B0DE13` | `DD108A6F0AEA` | 仅版本号注释 |
| `LocModelProfile.cs` | `8C1558DD2DF3` | `96D4E1558815` | 仅首行版本号注释 |
| `Downloader.cs` | `A94FA91A41AB` | 同 | **字节级相同** |
| `DownloadSeg.cs` | `C58569AD2812` | 同 | **字节级相同** |
| `CatalogService.cs` | `2CB04A9168F3` | 同 | **字节级相同** |
| `ConvertService.cs` | `B39DBDB40D4F` | 同 | **字节级相同** |
| `SiteProbe.cs` | `1915B5167DD4` | 同 | **字节级相同** |
| `DepsPrecheck.cs` | `0AED8E5E288C` | 同 | **字节级相同** |
| `HalfPartScan.cs` | `A5C85FD8E98E` | 同 | **字节级相同** |
| `Models.cs` | `0B6888F46ECC` | 同 | **字节级相同** |
| `RepoQuants.cs` | `4F62077CBA5E` | 同 | **字节级相同** |
| `HubSearch.cs` | `1FB1A7AD18B5` | 同 | **字节级相同** |
| `HubUrlMapper.cs` | `C8C9C496F730` | 同 | **字节级相同** |
| `TextCatalogSnapshot.cs` | `8A59E9077C9B` | 同 | **字节级相同** |

17 个源文件中 12 个 SHA-256 与 v3.1.2 完全一致，5 个变更文件全部位于 UI 层；分片调度、连接数自适应、`.part` 续传、HEAD 预检、ETag 识别、416 自愈与校验逻辑未触碰。

## 八、验证记录

### 8.1 编译

```
csc.exe /nologo /codepage:65001 /target:winexe /optimize+ /out:LocModel-Downloader-v3.1.3.exe
        /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll
        /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll *.cs
BUILD_EXIT=0
```

首轮编译暴露一处 `Timer` 二义性（`System.Threading.Timer` vs `System.Windows.Forms.Timer`），已改为全限定名后通过。

### 8.2 自检 `--selftest`

v3.1.2 原有断言全部保留并通过，新增 9 项：

```
  ...（v2.2–v3.1.2 历史断言省略）
  队列FIFO=OK count=3 head=o/a/Q4 重复被拒=True note=[队列里已有相同任务（同仓库 + 同量化档 + 同目标目录），已跳过]
  队列上限=OK 再收=5 count=8 超限被拒=True note=[队列已满（上限 8 个未开始任务），请等待完成或点「清空队列」]
  取消不清队列=OK before=8 after=8
  清空队列=OK count=0 label=[下载队列：空闲（最多排 8 个）]
  v312.sum ok=10 fail=0
-- v3.1.3 按钮状态 + 单实例 --
  版本号=OK title=[LocModel 模型下载器 v3.1.3]
  空闲态=OK btnDownload=True btnCancel=False
  解析窗口=OK btnDownload=False btnCancel=False
  执行中=OK btnDownload=True btnCancel=True
  取消中=OK btnDownload=True btnCancel=False
  FinishJob=OK busy=False cancelling=False current=null btnDownload=True btnCancel=False
  队列空闲文本=OK [下载队列：空闲（最多排 8 个）]
  队列忙时文本=OK [下载队列：待调度 · 等待 1 个 / 上限 8]
  单实例互斥=OK first=True second=False name=[Local\LocModelDownloader_SingleInstance_v3]
  v313.sum ok=9 fail=0
RUN_EXIT=0
```

新增 9 项断言覆盖：窗口标题版本号、空闲态按钮组合、解析窗口只灰「开始下载」而不灰「取消」、执行中「开始下载」保持可用、「取消中」禁用取消键、`FinishJob` 收口后状态归零、队列空闲/忙时文案、单实例互斥首实例 true 次实例 false。

自检结束会清零自检期间造出的队列计数，避免延迟回调弹出队列汇总框阻塞无人值守回归。

### 8.3 在线搜索窗口离屏自检 `--hubcheck`

```
HubSize=1060x740
  Layout=OK overlap=0 txtLog.Bottom=617 panelH=627
  MainMode=OK txtDest.Enabled=False browse.Enabled=False state=[实际落盘：F:\AI-Models\Qwen__Qwen2.5-7B-Instruct-GGUF]
  CustomMode=OK txtDest.Enabled=True text=[] state=[请先点「浏览…」选择目录]
  CustomEmpty.state=[请先点「浏览…」选择目录] hint=[2 个文件  4.36 GB（分片）   落盘：(未设置保存位置)]
```

新增队列状态行并入布局自检数组，`overlap=0`，原控件整体下移且底边锚定正确；主界面模式下路径框与浏览按钮置灰；「实际落盘」按当前仓库实时算出（本例 `Qwen__Qwen2.5-7B-Instruct-GGUF`）。

### 8.4 主界面离屏自检 `--uicheck`

截图 `ui313.png`：标题栏显示 `LocModel 模型下载器 v3.1.3`，日志区上方「下载队列：空闲（最多排 8 个）」一行渲染正常，控件无重叠、无文字截断。

### 8.5 单实例真实进程验证

```
A: id=23884 hasExited=False mainWnd=724476 title=[LocModel 模型下载器 v3.1.3]
B: id=19584 hasExited=True  exitCode=0
process count = 1
after close, A.hasExited=True
```

同一 exe 连续启动两次：首实例正常显示主窗口，第二个进程退出码 0 且未创建窗口，系统中始终只有一个实例。

### 8.6 真实模型目录只读影响

自检与界面自检全程未写入 `F:\AI-Models`（`--hubcheck` 只读取目录树用于拼路径，不落盘）。

## 九、升级建议

- 从 v3.1.2 升级：直接替换 exe 即可，无需改动任何配置文件。
- exe 同目录的 `LocModel下载器_下载配置.txt`、`LocModel下载器_站点配置.txt`、`LocModel下载器_在线搜索下载目录.txt` 格式未变，头注释会随程序写入更新为 v3.1.3。
- **行为变化提醒**：① 下载 / 转换执行期间「开始下载」不再置灰，可连续提交多个任务，注意队列上限 8 个；② 程序改为单实例，双击第二次会切回已开的窗口而不是开新窗口；若确需并行，需先关掉正在运行的实例。

## 十、版本保留策略（本版未执行远端操作）

- 本版已生成 `v3.1.3/` 目录、`LocModel-Downloader-v3.1.3.exe`、`locmodel-downloader-v3.1.3-src.zip`、`RELEASE_NOTES_v3.1.3.md`。
- 按要求**未执行** `git push`、**未打** tag `v3.1.3`、**未创建** GitHub Release；`v3.1.2` 及更早版本的 tag、目录、Release 资产保持不动。

*（内容由AI生成，仅供参考）*
*（内容由AI生成，仅供参考）*
