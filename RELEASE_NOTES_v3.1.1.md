# LocModel 下载器 v3.1.1 发布说明

发布日期：2026-11-23
基线：LocModel 下载器 v3.1.0（源码见仓库 `v3.1.0/`，tag `v3.1.0`）
产物：`LocModel-Downloader-v3.1.1.exe` / `locmodel-downloader-v3.1.1-src.zip`

## 一、本版核心 ①：下载站点不再兜底 `huggingface.co`

v3.1.0 及更早版本在几处保留了 `huggingface.co` 的隐式回退：当「下载站点」栏位为空、或某站点下载失败时，内核会悄悄切换到内置站点表里的 `huggingface.co` 继续下载。这导致两个问题：用户明明只填了镜像站点，实际却可能走了直连；失败原因也因「换线成功」而被掩盖。

v3.1.1 的规则改为：**「下载站点」栏位是唯一线路来源，运行期不再向线路表追加任何内置站点。**

| 位置 | v3.1.0 行为 | v3.1.1 行为 |
| --- | --- | --- |
| `Downloader.cs` 内置站点表 | `hf-mirror.com` + `huggingface.co` | 仅 `hf-mirror.com`（`DefaultSites`，`BuiltinFileMirrors` 降为同内容别名） |
| `Downloader.cs` `SetMirrors` 传空 / `null` | 回退内置站点表（含 `huggingface.co`） | 回退 `DefaultSites`（仅 `hf-mirror.com`） |
| `MainForm.cs` 测速排序 `RankToOrder` | 把内置线路追加到末尾「垫底」「兜底」 | 不再追加，栏目里没填的站点一律不出现在下载链路 |
| `CatalogService.cs` `HfBases` / `SetHfBases` | 默认表 + 每次都追加 `hf-mirror.com`、`huggingface.co` | 默认表仅 `hf-mirror.com`；`SetHfBases` 只认栏位站点，栏位为空时用 `SiteStore.DefaultSite` |
| `HubSearch.cs` 在线搜索基址 | 空列表回退硬编码 `https://hf-mirror.com` | 回退 `SiteStore.DefaultSite`（与栏位默认值同源） |
| `HubUrlMapper.cs` 链接映射 | 未指定站点时用 `https://huggingface.co` | 未指定站点时用 `SiteStore.DefaultSite` |

配套行为说明：

- **栏位留空怎么办**：界面在保存站点时会用默认站点 `https://hf-mirror.com` 回填栏位，因此「留空」等于「用默认镜像」，而不是「用未设置的站点」。栏位下方提示同步改为「只使用本栏位里的站点，不再内置其它站点兜底」。
- **站点打不开怎么办**：只对**栏位里已填的站点**按顺序重试，全部失败就如实报错（`全部线路失败`），不再悄悄换到用户没设置的站点。
- **不受影响的部分**：GGUF 量化转换工具链（llama.cpp 转换脚本）仍按原样从 GitHub 源下载，本版未改动其下载源，也不存在回退到 `hf-mirror.com` 的情况。

## 二、本版核心 ②：模型清单以「LocModel 扫描」为准，快照兜底

v3.1.0 的图像/视频清单在 3.5 / 3.6 档走的是「内置快照为权威，扫描只做仓库名核对」，因此 LocModel 小版本升级**新加的模型**不会出现在清单里（例如本机 3.6.1 的 `city96/Wan2.1-I2V-14B-480P-gguf`，v3.1.0 只提示、不纳入）。

v3.1.1 把优先级倒过来：**扫描优先、快照兜底、只补不减。**

### 2.1 语言/推理清单（16 条）

清单来源顺序改为三级：

1. **扫描本机 LocModel exe**（首选，能自动跟进 LocModel 小版本新增的模型）；
2. **服务端 `catalog.json`**（扫描失败时）；
3. **内置快照 `EmbeddedTextCatalog`**（新增文件 `TextCatalogSnapshot.cs`，快照版本 `20261027`，16 条，两条来源都不可用时的最终兜底）。

### 2.2 图像/视频清单（同步后 14 条）

新增 `SyncImageCatalogScanned` 取代旧的分支逻辑，规则如下：

- **扫描到 ≥ 6 条即以扫描为准**；解析不足 6 条说明解析器不适配该版本，退回内置快照（仍可正常使用）。
- **快照条目**：用扫描结果刷新发布年份等字段；量化档只做**并集**（补上扫描新增的档位，快照已有档位一律保留，避免解析器局限砍掉用户可用的档）。
- **扫描独有仓库**：直接新增为新条目（本次实测新增 `wan2.1-i2v-14b`，来自 `city96/Wan2.1-I2V-14B-480P-gguf`，量化 1 档、依赖 1 条）。
- **依赖（encoder / VAE / mmproj 等）**：只在快照条目本就没有依赖时才用扫描结果补，扫描解析出的粘连串不覆盖已有依赖。
- **文件名模板**：新增条目统一用 `{q}` 模板，下载期再按仓库真实文件清单解析确切文件名（含分片），不从粘连串里猜。
- **快照条目在 exe 里搜不到仓库**：只写日志提示，不移除条目（避免解析器局限打掉可用清单）。
- **3.1 档保持原语义**：仍走「快照 + exe 仓库修复」，不受本版改动影响。

## 三、清单文件快照（`TextCatalogSnapshot.cs`）

- 类名 `EmbeddedTextCatalog`，`SnapshotVersion = "20261027"`，`Build()` 返回 16 条语言/推理模型。
- 字段与在线 `catalog.json` 对齐：`params_b` / `active_b` / `layers` / `kv_layers` / `kv_heads` / `head_dim` / `experts` / `experts_used` / `ctx_max` / `quants` / `size_gb`。
- 取值来源：本机 LocModel 3.6.1 内嵌扫描结果（与在线 `catalog.json` 逐字段一致，已核对）。
- 用途：仅在「扫描失败 **且** 服务端 `catalog.json` 不可达」时作为第三来源，不参与正常路径。

## 四、变更文件与内核对比

| 文件 | v3.1.1 SHA-256 前 12 位 | 与 v3.1.0 对比 |
| --- | --- | --- |
| `CatalogService.cs` | `2CB04A9168F3` | 变更（站点表 + 清单来源 + 图像同步规则） |
| `Downloader.cs` | `A94FA91A41AB` | 变更（仅站点表与空回退分支，见下） |
| `HubSearch.cs` | `1FB1A7AD18B5` | 变更（空回退改同源默认站点） |
| `HubUrlMapper.cs` | `C8C9C496F730` | 变更（默认站点改同源默认站点） |
| `MainForm.cs` | `BC67C461834C` | 变更（去掉内置垫底、版本号、站点提示、自检断言、异步回写句柄保护） |
| `LocModelProfile.cs` | `BD7569ACE5E7` | 变更（`ImageAuthority=0` 语义注释改为「扫描优先 + 快照兜底」） |
| `DownloadConfig.cs` | `D02314937C0C` | 变更（配置头版本号） |
| `TextCatalogSnapshot.cs` | `8A59E9077C9B` | **新增**（内置文本模型快照） |
| `DownloadSeg.cs` | `C58569AD2812` | 相同 |
| `SiteProbe.cs` | `1915B5167DD4` | 相同 |
| `ConvertService.cs` | `B39DBDB40D4F` | 相同 |
| `DepsPrecheck.cs` | `0AED8E5E288C` | 相同 |
| `HalfPartScan.cs` | `A5C85FD8E98E` | 相同 |
| `HubBrowserForm.cs` | `A1913F52F13A` | 相同 |
| `Models.cs` | `0B6888F46ECC` | 相同 |
| `RepoQuants.cs` | `4F62077CBA5E` | 相同 |

**关于 `Downloader.cs`**：本版必须改动它，因为取消 `huggingface.co` 兜底就写在这里（`DefaultSites` / `SetMirrors` 的空回退）。改动范围严格限定为**站点表的取值与空回退分支**：分片调度、连接数与阈值自适应、`.part` 续传、HEAD 预检、ETag 变化识别、416 自愈、校验与重试逻辑**一行未改**。分片内核所在 `DownloadSeg.cs` 仍与 v3.1.0 字节级一致。

## 五、验证记录

### 5.1 自检 `--selftest`

```
AllCount=30  image=14  text=16
TextCatalogSource=SCAN  count=16
ImageCatalogSource=SCAN count=14
SitePolicy=OK  DefaultSites=[https://hf-mirror.com]  NoHfCom=True
SiteFollow=OK
SiteEmptyFallback=OK
selOk=30  selFail=0
Mirrors=https://hf-mirror.com
BuiltinMirrors=https://hf-mirror.com  HasM1M2=False
HfBases=https://hf-mirror.com
```

自检新增三段站点链断言（`SitePolicy` / `SiteFollow` / `SiteEmptyFallback`）与两段清单来源断言（`TextCatalogSource` / `ImageCatalogSource`），用于防止后续版本再把 `huggingface.co` 兜底加回去。

### 5.2 单站点真实下载回归（只用栏位里的站点）

用本地 HTTP 服务（仓库内 `tools/segtest_server.py`）模拟站点，栏位只填 `http://127.0.0.1:18088`，12 MiB 文件走完整内核（含 `RunAll`、分片探测、校验、落盘）：

| 场景 | 结果 |
| --- | --- |
| 正常下载 | 成功，12582912 B，MD5 `BB51DBA7-1EAA-7B28-C257-CC6D811D9E43` 与服务端一致；日志中站点命中 1 次，`hf-mirror.com` 命中 0 次、`huggingface.co` 命中 0 次 |
| 文件不存在（404） | 失败并如实报错；仅对栏位站点重试，未切换任何未设置站点，`hf-mirror.com` / `huggingface.co` 命中均为 0 次 |

### 5.3 清单实测

- 本机 LocModel 3.6.1：语言/推理清单 16 条全部来自 exe 扫描（日志「语言/推理清单以 LocModel 扫描为准（16 个模型）」）。
- 图像/视频：内置快照 13 条 → 同步后 14 条（新增 `wan2.1-i2v-14b`）；`flux1-schnell`、`wan2.1-t2v-1.3b` 等扫描量化档为 0 的条目按快照保留，未被砍档。
- `qwen-image-edit`、`wan2.2-i2v-a14b` 等扫描依赖为 0 的条目按快照保留依赖。

## 六、升级建议

- 从 v3.1.0 升级：直接替换 exe 即可。exe 同目录的 `LocModel下载器_下载配置.txt`（连接数 / 最小分片）与 `LocModel下载器_站点配置.txt`（下载站点）格式未变，可继续沿用。
- **行为变化提醒**：本版起「下载站点」栏位没填的站点**不会**被自动使用；如果你的栏位只填了一个镜像且该镜像不可用，下载会直接失败（而不是悄悄走 `huggingface.co`）。需要多线路容错时，请在栏位里把备用站点一并写上（每行一个，或逗号 / 分号分隔）。
- 转换工具链（GGUF 量化转换）仍需访问 GitHub 源，与本版站点策略无关。

## 七、历史版本保留策略

- v3.1.1 以 `v3.1.1/` 目录与 tag `v3.1.1` 保留，并创建 GitHub Release 作为 **Latest**（`LocModel-Downloader-v3.1.1.exe`、`locmodel-downloader-v3.1.1-src.zip`、`RELEASE_NOTES_v3.1.1.md`）。
- v3.1.0 及更早版本的 tag、目录、Release 资产**一律不删、不覆盖**，仅 v3.1.0 失去 Latest 标记。
