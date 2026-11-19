# v2.2.1 — 修复 >2 GiB 模型「校验失败(数组维度超过了支持的范围。)」死循环

## 症状

下载 Qwen_Image-Q6_K.gguf（16.82 GB）、Qwen_Image-Q2_K（7.06 GB）等模型，进度走到 100% 后报：

```
校验失败(数组维度超过了支持的范围。)，丢弃半成品（第 1 次）
校验失败(数组维度超过了支持的范围。)，丢弃半成品（第 2 次）
连续校验失败 2 次，放弃该文件
全部线路失败: Qwen_Image-Q6_K.gguf
```

重新点开始又从 0 字节下载，再次撞同样的报错，永远下不完。

## 根因

校验函数读取文件头时写的是 `new byte[Math.Min(4096, (int)len)]`，`len` 是 `long` 类型的文件字节数。当文件大小对 4 GiB 取模落在 **2 GiB ~ 4 GiB** 区间时，`(int)len` 溢出成负数，`new byte[负数]` 抛出 `OverflowException`，其文案正是「数组维度超过了支持的范围。」。该异常又被外层 `catch (Exception)` 当作数据损坏处理，于是删除 `.part` 重下。

因此表现为「有的模型能下、有的死活不行」：

| 模型 | 大小 | 模 4 GiB | v2.2.0 结果 |
| --- | --- | --- | --- |
| Qwen_Image-Q2_K | 7,062,518,304 B | 2.77 GB | 必败 |
| Qwen_Image-Q6_K | 16,824,990,240 B | 3.94 GB | 必败 |
| Qwen_Image-Q3_K_M | 9,679,567,392 B | 1.09 GB | 正常 |
| Qwen_Image-Q4_K_M | 13,065,746,976 B | 0.18 GB | 正常 |

## 修复

1. 头部长度全程按 `long` 计算（`Math.Min(len, 4096L)`）后再收窄为 `int`，并改为循环读满文件头（顺带修掉 4 GiB 整数倍时 `(int)len` 归零、读不到文件头的同一隐患）。
2. 校验异常分级：
   - `InvalidDataException`（HTML 页面、文件过小、GGUF 魔数错、safetensors 头部非法、SHA-256 不符）→ 判定数据不可信，清理半成品并限次重试（行为不变）；
   - 其他异常（溢出 / IO / 内存）→ **保留半成品**并终止该文件，不再误删已下完的数据。
3. 「已存在文件」校验走同一函数，同样按异常类型分流：程序性异常不再把用户磁盘上的好文件转成 `.part` 重下。

## 验证

| 组 | 内容 | 结果 |
| --- | --- | --- |
| 大文件校验单测 | 2.5 GiB、7.06 GB、16.82 GB、9.68 GB、13.07 GB、4 GiB 边界全部通过；HTML / 过小 / 魔数错 / safetensors 头非法 / SHA 不符仍被正确拒绝 | 16/16 PASS |
| 端到端真实下载 | 本地 HTTP 下载 2.2 GB 文件：落盘长度正确、无「校验失败」、二次运行「已存在且校验通过，跳过」 | 6/6 PASS |
| v2.2 原有回归 | 站点解析归一化、测速排序、择优选线下载、真实 hf-mirror.com 测速 | 27/27 PASS |

## 资产

| 资产 | 说明 |
| --- | --- |
| `LocModel-Downloader-v2.2.1.exe` | 单文件图形下载器（89 KB，.NET Framework 4.8，系统自带无需安装；文件名用 ASCII，下载后可直接改名） |
| `locmodel-downloader-v2.2.1-src.zip` | 完整 C# 源码（`src/`），可用系统自带 `csc.exe` 自行编译 |

## 升级说明

直接替换旧 exe 即可，模型目录、自定义站点配置均沿用。v2.2.0 及更早版本保留在仓库与历史 Release 中，不做删除。

> 已存在的大小正确文件无需重新下载：新版启动后会校验通过并跳过；若之前因该缺陷残留了 `.part`，新版会先校验再决定续传或丢弃，不会误删完好的目标文件。

---

# v2.2.1 — Fix the endless verify loop on model files larger than 2 GiB

Symptom: after reaching 100% on Qwen_Image-Q6_K.gguf (16.82 GB) or Qwen_Image-Q2_K (7.06 GB), the downloader logged `校验失败(数组维度超过了支持的范围。)，丢弃半成品` twice, dropped the finished `.part` file and restarted from zero — forever.

Root cause: the verifier read the file header with `new byte[Math.Min(4096, (int)len)]` where `len` is a `long` file size. For any file whose size mod 4 GiB falls between 2 GiB and 4 GiB, the cast overflows to a negative `int`, and `new byte[negative]` throws `OverflowException` (message: 数组维度超过了支持的范围。). The outer `catch (Exception)` treated it as corrupt data and deleted the `.part`.

Fixes: (1) compute the header length in `long` before narrowing and read the header in a loop; (2) split verify error handling — only `InvalidDataException` clears the `.part`, any other exception keeps it and aborts that file; (3) the existing-file check follows the same split, so a good file on disk is never converted into a `.part`.

Verified: 16/16 large-file verify cases, 6/6 end-to-end real 2.2 GB download cases, 27/27 existing v2.2 site/download regression cases.
