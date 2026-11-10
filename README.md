# LocModel 镜像补丁工具 locmodel_patch.exe

> **适用版本**：LocModel 2.1.0 ✅ · LocModel 3.1.0 ✅（两版均已本机实测） · Windows 10 / Windows 11
>
> LocModel 在国内网络下无法直连 `huggingface.co`，导致模型列表拉不到、模型下载失败。
> 本工具把配置文件里的 `custom_mirror` 切换到 `https://hf-mirror.com`，单文件 exe，目标机无需安装任何运行库。
> 使用时，经实测，打开locmodel后点击下载，如果下载过程中报错或开始就报错，不要点重试，
> 一定直接退出locmodel再重新打开程序点下载（因为locmodel在触发下载失败后会自动回写mirro=menber会员），
> 中途断掉的可以续传，有的模型多次报错就关掉程序在重开再试就可成功。镜像连接后的速率一般在25-90MB/s。


把 LocModel 配置文件 `settings.json` 中的 `custom_mirror` 字段改为 `https://hf-mirror.com`，
解决国内网络下 LocModel「网络无法连接」、拉不到模型列表、模型下载失败的问题。

单文件控制台程序，Windows 10/11 自带 .NET Framework 4.x 即可运行，**目标机无需安装任何运行库**。

---

## 一、怎么用（最常见两种情况）

### 情况 1：LocModel 已经装好、正在用（推荐）
直接双击 `locmodel_patch.exe` 即可，或在命令行运行：

```
locmodel_patch.exe
```

程序会自动按顺序找配置文件：
1. 本 exe 所在目录的 `settings.json`（便携版）
2. `%LOCALAPPDATA%\LocModel\settings.json`（**绝大多数情况命中这里**）
3. 注册表卸载项 `InstallLocation` 指向的目录及其子目录

找到后会：
1. 若 LocModel 正在运行，尝试正常关闭（最多等 15 秒；关不掉就中止，**绝不强杀**）
2. 把原文件改名为 `settings.json.bak.yyyyMMdd-HHmmss` 备份
3. 只把 `custom_mirror` 的值改成 `https://hf-mirror.com`，其它内容一个字节都不动
4. 写完后重新读取校验，校验不过就用备份自动回滚
5. 如果原本 LocModel 在运行，自动重新启动它

### 情况 2：便携版 / 想手动指定配置文件
把 `locmodel_patch.exe` 拷到 `settings.json` 所在目录，双击即可；
或用 `--path` 明确指定：

```
locmodel_patch.exe --path "C:\Users\你的用户名\AppData\Local\LocModel\settings.json"
```

也支持传目录：

```
locmodel_patch.exe --path "C:\Users\你的用户名\AppData\Local\LocModel"
```

---

## 二、参数说明

| 参数 | 作用 |
|---|---|
| `--path <路径>` | 指定 `settings.json` 的完整路径，或其所在目录 |
| `--dry-run` | 只打印将要做的改动，**不写入、不备份、不碰进程** |
| `--no-process` | 跳过 LocModel 进程检查与关闭（自动化/脚本场景用） |
| `--no-restart` | 改完不自动重启 LocModel |
| `--silent` | 结束后不等待回车，便于脚本调用 |
| `--help` | 显示帮助 |

**想先看看会改什么，不改动任何东西：**

```
locmodel_patch.exe --dry-run
```

---

## 三、退出码

| 退出码 | 含义 |
|---|---|
| `0` | 成功（包括「已是目标值，无需修改」） |
| `1` | 未知错误 / 参数错误 |
| `2` | 找不到配置文件（会列出已探测的全部路径） |
| `3` | 配置里没有 `custom_mirror` 键（未做任何写入） |
| `4` | 写入或校验失败（已用备份自动回滚） |
| `5` | LocModel 进程未能关闭（未做任何改动） |

---

## 四、备份与回滚

每次实际改写前，原文件会被**改名**（不是复制）为：

```
settings.json.bak.20261105-212910
```

如果同一秒内重复执行，会自动追加序号：`settings.json.bak.20261105-212910-1`、`-2`……

**要回滚，只需两步：**
1. 关闭 LocModel
2. 把 `settings.json` 删掉/改名，再把备份文件改回 `settings.json`

例：

```
cd %LOCALAPPDATA%\LocModel
ren settings.json settings.json.broken
ren settings.json.bak.20261105-212910 settings.json
```

备份文件程序**永不删除**，由你自行决定何时清理。

---

## 五、安全边界（明确承诺）

- 只改 `custom_mirror` **一个值**；缩进、换行符（LF/CRLF）、BOM、键顺序、尾随换行全部原样保留，
  不做「反序列化再序列化」这种会打乱格式的操作。
- 不碰 `settings.json` 之外的任何文件；模型目录、`apis.json`、`images.json` 一律不动。
- 不改注册表（只**读取**注册表用于定位安装目录）。
- 绝不强杀进程：只发正常关闭请求，15 秒不退就中止。
- 当前值已经是 `https://hf-mirror.com` 时直接提示「已是目标值，无需修改」，不产生多余备份。

---

## 六、源码与重新编译

源码 `locmodel_patch.cs` 一并提供，含完整中文注释。用 Windows 自带编译器即可重新编译
（无需 Visual Studio、无需额外运行库）：

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /codepage:65001 /optimize+ /out:locmodel_patch.exe locmodel_patch.cs
```

编译后得到的就是单个 exe，不依赖任何额外 DLL。

---

## 七、本机实测记录（2026-11-05，Windows 11 Build 26200）

用真实配置的原始副本（531 字节、UTF-8 无 BOM、LF 换行、2 空格缩进、23 个 LF、无尾随换行）在沙箱中实测：

| 用例 | 场景 | 实际结果 |
|---|---|---|
| 1 | `custom_mirror` 为空 `""` | 退出码 0；改为 `https://hf-mirror.com`（552 字节）；生成备份；除该行外逐行无差异；换行/BOM/行数/缩进完全一致 |
| 2 | 已改好的文件再跑一次 | 退出码 0；提示「已是目标值，无需修改」；文件哈希不变；未产生新备份 |
| 3 | `--dry-run` | 退出码 0；仅打印改动；文件哈希不变；无备份生成 |
| 4 | 传入不存在的路径 | 退出码 2；列出已探测路径并提示用 `--path` |
| 5 | 删掉 `custom_mirror` 键 | 退出码 3；文件未被改动；无备份生成 |

附加验证：便携模式（exe 同目录放 `settings.json`）自动命中并修改成功；`--help` 正常输出，退出码 0。
全程使用 `--no-process` 与 `--no-restart`，真实配置文件与正在运行的 LocModel 进程均未被触碰。

---

## 八、LocModel 3.1.0 兼容性验证（2026-11-09）

**结论：LocModel 从 2.1.0 升级到 3.1.0 后，本补丁无需任何修改，原程序仍可正常使用。3.1.0 版经测试仍可使用。**

| 核验项 | 2.1.0 | 3.1.0 实测 | 判定 |
|---|---|---|---|
| `custom_mirror` 字段 | 存在 | 仍存在，值 `https://hf-mirror.com` 在程序回写配置后仍被保留 | 有效 |
| 程序内镜像常量 | 内置 `hf-mirror.com` | 仍内置 | 有效 |
| `HF_ENDPOINT` 支持 | 不支持 | 仍不支持 | 无变化 |
| 直连 huggingface.co | 超时 | 仍不通：DNS 污染至 199.59.148.8，curl 12.0s 超时（code=000） | 仍不通 |
| hf-mirror.com | HTTP 200 | HTTP 200 / 1.02s，模型列表与仓库接口均 200，Range 请求 206 | 仍可用 |
| 真实下载链路 | 走镜像 | 抓包见连接 `65.9.168.32/58:443`，证书 `CN=cas-bridge.xethub.hf.co`，SNI `huggingface.co` 被 RST | 有效 |

3.1.0 新增的 `hf_mirror`（默认 `member`）、多线路优选（`accel_lines`、`preferred_line`）、`NEED_ACCEL` 会员限制均不影响本补丁：未开通会员时会员线路接口返回 404，程序自动回落 `custom_mirror` 兜底，补丁继续生效。配置文件位置未变：`%LOCALAPPDATA%\LocModel\settings.json`，用法与参数无需任何调整。
