# LocModel 下载器 v3.1.0 发布说明

发布日期：2026-11-22
基线：LocModel 下载器 v3.0.0（源码见仓库 `v3.0.0/`，tag `v3.0.0`）
产物：`LocModel-Downloader-v3.1.0.exe` / `locmodel-downloader-v3.1.0-src.zip`

## 一、本版核心：下载前复核（已下载文件标绿 + 「（已存在）」）

以往「本次将下载的文件」栏只列出本次要拉的文件，用户无法判断其中哪些本地已有（尤其依赖组件被多个模型共用时），容易误估下载量、甚至重复下载。

v3.1.0 在下拉量化档后、以及清单落地后各做一次**只读复核**，把本地已下载完成的文件单独标出来：

- **标注方式**：已存在的行整行文字渲染为绿色（RGB `126,208,148`），并在文件信息后追加「**（已存在）**」。
- **标题统计**：栏标题改为「本次将下载的文件（共 N 项，已存在 M 项）」，N 为本次清单总项数，M 为复核命中数。
- **复核口径**（全部满足才算已存在，任一不满足即按未下载处理）：
  1. 目标文件存在且长度 > 0；
  2. 目标同目录没有 `<文件>.part` / `<文件>.part.meta` 半成品残留（LocModel 3.5/3.6 主程序与下载内核都在就地留这类文件，有残留即视为没下完）；
  3. 声明大小已知时，实际长度 ≥ 声明大小（未知大小则不做长度上限判断，避免误判）。
- **mmproj 特例**：语言/多模态模型的 `mmproj` 是自动配对的，预览阶段拿不到确切文件名，改为按「目录内是否存在非半成品、长度 > 0 的 mmproj 文件」判定。
- **纯只读**：复核只做 `File.Exists` / `Directory.GetFiles` / 文件长度读取，不改动、不创建、不删除任何文件，也不影响下载与续传内核的判断。

## 二、LocModel 3.6 版本适配（默认档位）

- 新增 `3.6` 档位，界面下拉顺序为 `3.6（默认）/ 3.5 / 3.3 / 3.1`。
- 3.6.0 实测：catalog 8033 B / 19 字段 / 16 个文本模型、settings 36 字段、`accel_lines` 与 `hf_token` / `accel_nodes` 均存在——与 3.5 的能力位完全一致，因此 3.6 与 3.5 共用全部能力位，仅把默认档位切到 3.6。
- 版本推断链：exe 文件版本号（最可靠）→ 3.5/3.6 专有 settings 字段 → `accel_lines` → exe 内嵌特征串 → 默认版本。由于 3.5 与 3.6 的 settings 指纹与 exe 特征串无法区分，指纹命中时统一落到默认档 3.6，保证装了新版本 LocModel 就按新版配置工作。
- `Normalize` 支持 `3.6` / `3.6.0` / `v3.6` / `3.6.0-beta` 等写法；无法识别的输入回落到默认档。

## 三、下载内核未改

| 文件 | v3.1.0 SHA-256 前 12 位 | 与 v3.0.0 对比 |
| --- | --- | --- |
| `Downloader.cs` | `1FCBA789C401` | 相同 |
| `DownloadSeg.cs` | `C58569AD2812` | 相同 |
| `SiteProbe.cs` | `1915B5167DD4` | 相同 |
| `CatalogService.cs` / `Models.cs` / `RepoQuants.cs` / `HalfPartScan.cs` / `Hub*.cs` / `ConvertService.cs` | 与 v3.0.0 一致 | 相同 |
| `DepsPrecheck.cs` | `0AED8E5E288C` | 新增 |
| `MainForm.cs` / `LocModelProfile.cs` / `DownloadConfig.cs` | 已变更 | 改动集中在复核标注、3.6 适配与版本注释 |

并发分片、断点续传、目标盘写入能力自适应、测速择优等全部沿用 v3.0.0，未动一行下载逻辑。

## 四、验证记录

编译：`csc`（.NET Framework 4.8 自带，`/target:winexe`）编译通过，产物 206,336 B。

`--selftest` 内置自检 **29 项通过 / 0 项失败**，其中与本次改动相关的输出：

```
-- v3.1.0 下载前复核 --
  IsReady.done(未知大小)=True done(声明2048)=True half(有.part)=False empty=False short(声明4096)=False missing=False null=False
  HasMmproj.dir=True 空目录=False
  Suffix(true)=（已存在） Suffix(false)=[]
  CountText(3,1)=本次将下载的文件（共 3 项，已存在 1 项）
  RowText=主文件  x  →  y（已存在） Exists=True DrawColor=CExists(Green)
  -- 真实目录抽样（F:\AI-Models）--
    z-image-turbo quant=Q3_K 标题=本次将下载的文件（共 4 项，已存在 3 项）
      [绿色] 主文件  leejet/Z-Image-Turbo-GGUF  →  z_image_turbo-Q3_K.gguf（已存在）
      [常规] —— 依赖组件（image\common）——
      [绿色] 依赖    unsloth/Qwen3-4B-Instruct-2507-GGUF  →  Qwen3-4B-Instruct-2507-Q4_K_M.gguf（已存在）
      [绿色] 依赖    Comfy-Org/z_image_turbo  →  ae.safetensors（已存在）
      合计 4 项，绿色 3 项
    qwen-image-2.1 quant=Q3_K 标题=本次将下载的文件（共 5 项，已存在 3 项）
      [常规] 主文件  leejet/Qwen-Image-2.1-GGUF  →  qwen_image_2.1-Q3_K.gguf
      [常规] —— 依赖组件（image\common）——
      [绿色] 依赖    Qwen/Qwen3-VL-8B-Instruct-GGUF  →  Qwen3VL-8B-Instruct-Q4_K_M.gguf（已存在）
      [绿色] 依赖    Qwen/Qwen3-VL-8B-Instruct-GGUF  →  mmproj-Qwen3VL-8B-Instruct-F16.gguf（已存在）
      合计 5 项，绿色 3 项
  绘制验证：已存在行绿色像素=281（>20 视为标绿成功），未存在行绿色像素=1
```

- 真实目录抽样直接读生产模型目录 `F:\AI-Models`：`z-image-turbo` 4 项中 3 项判为已存在，`qwen-image-2.1` 主文件因目录里留有 `.part` 半成品而被正确判为「未下载」，其 mmproj 与另两个依赖判为已存在。
- 绘制验证直接以真实自绘例程（`LstDeps_DrawItem`）画到离屏位图后统计像素：已存在行绿色像素 281、未存在行仅 1，说明绿色只在命中行出现，且文字确实被画成了绿色而非只存了标志位。
- 分片下载回归（本地 HTTP 服务 + 24 MB 文件 + 4 连接）：`result=Done conns=4 bytes=25165824`，`.part` / `.part.seg` 清理干净。

## 五、已知限制

- `--uicheck` 截图分支不加载模型清单，仅用于布局核对；且 WinForms 的 `DrawToBitmap` 抓不到自绘 ListBox 的内容，v3.1.0 在该分支额外用 `PrintWindow(PW_RENDERFULLCONTENT)` 抓了一份真实像素（`*.pw.png`）。判断标注正确性请以 `--selftest` 的复核段与绘制验证为准。
- 复核只看「本地是否已下载完成」，不做内容哈希校验；文件被外部截断但长度仍达标的情况无法识别（这是刻意取舍，避免扫描大文件拖慢界面）。

## 六、自行编译

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.1.0.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.1.0\src\*.cs
```

`v3.1.0/tools/segtest_server.py` 是分片下载回归用的本地 HTTP Range 服务，与 exe 编译无关。

## 七、文件清单

| 文件 | 说明 |
| --- | --- |
| `LocModel-Downloader-v3.1.0.exe` | 主程序（206,336 B，单文件，无外部依赖） |
| `locmodel-downloader-v3.1.0-src.zip` | 完整源码（15 个 `.cs` + 回归工具） |
| `v3.1.0/src/`、`v3.1.0/tools/` | 仓库内源码与工具目录 |
| `RELEASE_NOTES_v3.1.0.md` | 本说明 |

## 八、升级建议

直接从 v3.0.0 覆盖 exe 即可，配置文件 `LocModel下载器_下载配置.txt` 与目录设置保持兼容。若本机 LocModel 已升级到 3.6，下载器会自动识别并按 3.6 档工作，无需手动切换版本。
