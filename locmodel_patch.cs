// ============================================================================
// locmodel_patch.exe  ——  LocModel 镜像补丁工具（单文件控制台程序）
// ----------------------------------------------------------------------------
// 用途：把 LocModel 配置文件 settings.json 中的 custom_mirror 字段改为
//       https://hf-mirror.com，使国内网络环境下能正常拉取模型列表与下载模型。
//
// 设计原则（安全第一）：
//   1. 只改 custom_mirror 一个值，其余内容逐字节零损伤（不反序列化再序列化）；
//   2. 写入前把原文件【改名】备份为 settings.json.bak.yyyyMMdd-HHmmss；
//   3. 绝不删除用户文件、绝不动 settings.json 之外的任何文件、绝不改注册表；
//   4. 绝不强杀进程：正常关闭失败就中止，不会带进程改配置。
//
// 编译方式（Windows 10/11 自带 .NET Framework 4.x，目标机无需装任何运行库）：
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//       /nologo /target:exe /codepage:65001 /optimize+
//       /out:locmodel_patch.exe locmodel_patch.cs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace LocModelPatch
{
    internal static class Program
    {
        // ---------------- 常量定义 ----------------
        private const string TARGET_VALUE = "https://hf-mirror.com"; // 要写入的镜像地址
        private const string PROC_NAME    = "LocModel";              // 进程名（不带 .exe）
        private const string CFG_NAME     = "settings.json";         // 配置文件名
        private const string KEY_NAME     = "custom_mirror";         // 要修改的字段名

        // 退出码（按需求约定）
        private const int EXIT_OK         = 0; // 成功（含“已是目标值无需修改”）
        private const int EXIT_UNKNOWN    = 1; // 未知错误 / 参数错误
        private const int EXIT_NOT_FOUND  = 2; // 找不到配置文件
        private const int EXIT_NO_KEY     = 3; // 配置里没有 custom_mirror 键
        private const int EXIT_WRITE_FAIL = 4; // 写入或校验失败（已回滚）
        private const int EXIT_PROC_FAIL  = 5; // 进程未能关闭

        // --------------------------------------------------------------------
        // 核心正则：只匹配 custom_mirror 这个键值对
        //   分组1 = "custom_mirror" 加上冒号及两侧空白（原样保留，不动）
        //   值部分 = 支持 JSON 转义的字符串，整体被替换
        // --------------------------------------------------------------------
        private static readonly Regex MirrorRegex = new Regex(
            "(\"" + KEY_NAME + "\"\\s*:\\s*)\"(?:[^\"\\\\]|\\\\.)*\"",
            RegexOptions.CultureInvariant);

        // ---------------- 命令行参数 ----------------
        private static string optPath     = null;  // --path 指定路径
        private static bool   optNoProc   = false; // --no-process 跳过进程处理
        private static bool   optNoRestart= false; // --no-restart 改完不重启
        private static bool   optSilent   = false; // --silent 结束不等回车
        private static bool   optDryRun   = false; // --dry-run 只打印不落盘
        private static bool   optHelp     = false; // --help

        // ====================================================================
        // 程序入口
        // ====================================================================
        private static int Main(string[] args)
        {
            // 控制台输出强制 UTF-8，避免中文提示乱码
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch { /* 某些重定向环境下设置会失败，忽略即可 */ }

            // 1) 解析命令行
            if (!ParseArgs(args)) { PrintUsage(); return Done(EXIT_UNKNOWN); }
            if (optHelp) { PrintUsage(); return Done(EXIT_OK); }

            PrintHeader();

            // 2) 定位目标配置文件
            List<string> probed = new List<string>();
            string target = ResolveTargetPath(probed);
            if (target == null)
            {
                Console.WriteLine("[失败] 未能找到 LocModel 的 settings.json。");
                Console.WriteLine("       已探测以下位置：");
                foreach (string p in probed) { Console.WriteLine("         - " + p); }
                Console.WriteLine("       建议：用 --path 手动指定配置文件路径。");
                return Done(EXIT_NOT_FOUND);
            }
            Console.WriteLine("[目标] " + target);

            // 3) 进程处理（dry-run 模式不碰进程，只提示）
            bool wasRunning = false;
            string procExe = null;
            if (optDryRun)
            {
                Console.WriteLine("[进程] --dry-run 模式，跳过进程处理（不会关闭任何程序）。");
            }
            else if (optNoProc)
            {
                Console.WriteLine("[进程] --no-process 已指定，跳过进程处理。");
            }
            else
            {
                int procResult = HandleProcess(out wasRunning, out procExe);
                if (procResult != EXIT_OK) return Done(procResult);
            }

            // 4) 读取原始字节，检测 BOM
            byte[] raw;
            try { raw = File.ReadAllBytes(target); }
            catch (Exception ex)
            {
                Console.WriteLine("[失败] 读取配置文件出错：" + ex.Message);
                return Done(EXIT_UNKNOWN);
            }

            bool hasBom = (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF);
            int offset = hasBom ? 3 : 0;
            byte[] body = new byte[raw.Length - offset];
            Array.Copy(raw, offset, body, 0, body.Length);

            string text;
            try { text = new UTF8Encoding(false).GetString(body); }
            catch (Exception ex)
            {
                Console.WriteLine("[失败] 配置内容不是合法 UTF-8 文本：" + ex.Message);
                return Done(EXIT_UNKNOWN);
            }

            int crlfCount = CountOccurrences(text, "\r\n");
            int lfCount   = CountOccurrences(text, "\n");
            bool trailingNewline = (text.EndsWith("\n") || text.EndsWith("\r"));
            Console.WriteLine("[现状] 文件 " + raw.Length + " 字节 | BOM=" + (hasBom ? "有" : "无")
                + " | CRLF=" + crlfCount + " | LF=" + lfCount
                + " | 尾随换行=" + (trailingNewline ? "有" : "无"));
            // 5) 查找 custom_mirror 键
            MatchCollection matches = MirrorRegex.Matches(text);
            if (matches.Count == 0)
            {
                Console.WriteLine("[失败] 配置文件中不存在 " + KEY_NAME + " 键，已中止，未做任何写入。");
                return Done(EXIT_NO_KEY);
            }
            if (matches.Count > 1)
            {
                Console.WriteLine("[失败] 配置文件中出现 " + matches.Count + " 个 " + KEY_NAME
                    + " 键，为避免误改已中止，未做任何写入。");
                return Done(EXIT_NO_KEY);
            }

            string oldValue = ExtractValue(matches[0].Value);
            Console.WriteLine("[字段] " + KEY_NAME + " 当前值 = \"" + oldValue + "\"");

            // 6) 已是目标值 -> 直接成功退出，不产生备份
            if (oldValue == TARGET_VALUE)
            {
                Console.WriteLine("[完成] 已是目标值，无需修改。");
                Console.WriteLine("[结果] 未写入文件、未产生备份。");
                return Done(EXIT_OK);
            }

            // 7) 生成新文本：只替换值部分，其它字符一个不动
            string newText = MirrorRegex.Replace(text, new MatchEvaluator(ReplaceEvaluator), 1);
            Console.WriteLine("[字段] " + KEY_NAME + " 目标值 = \"" + TARGET_VALUE + "\"");

            // 8) 预校验：把新旧文本的镜像值都归一化成占位符后必须完全相等
            //    这一步在落盘前就证明“除 custom_mirror 外其余内容逐字节一致”
            string normOld = NormalizeMirror(text);
            string normNew = NormalizeMirror(newText);
            if (normOld != normNew)
            {
                Console.WriteLine("[失败] 内部一致性检查未通过（改动超出了目标字段），已中止，未做任何写入。");
                return Done(EXIT_WRITE_FAIL);
            }
            Console.WriteLine("[预检] 除目标字段外的内容与原文件完全一致。");

            if (optDryRun)
            {
                Console.WriteLine();
                Console.WriteLine("---- dry-run：以下改动【未】写入磁盘 ----");
                Console.WriteLine("  文件  : " + target);
                Console.WriteLine("  改动前: \"" + oldValue + "\"");
                Console.WriteLine("  改动后: \"" + TARGET_VALUE + "\"");
                Console.WriteLine("  预计备份: " + target + ".bak.yyyyMMdd-HHmmss");
                Console.WriteLine("  预计新大小: " + (Encoding.UTF8.GetByteCount(newText) + (hasBom ? 3 : 0)) + " 字节");
                Console.WriteLine("---------------------------------------");
                return Done(EXIT_OK);
            }

            // 9) 生成备份文件名（同名则追加 -1、-2 ...）
            string backupPath = BuildBackupPath(target);

            // 10) 先把原文件改名为备份（不是复制，避免残留半成品）
            try
            {
                File.Move(target, backupPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[失败] 备份（重命名原文件）失败：" + ex.Message);
                Console.WriteLine("       已中止，原文件未改动。");
                return Done(EXIT_WRITE_FAIL);
            }
            Console.WriteLine("[备份] 原文件已更名为：" + backupPath);

            // 11) 写入新内容（BOM 与原文一致，换行随原文本，不加尾随换行）
            byte[] newBody = new UTF8Encoding(false).GetBytes(newText);
            byte[] outBytes;
            if (hasBom)
            {
                outBytes = new byte[3 + newBody.Length];
                outBytes[0] = 0xEF; outBytes[1] = 0xBB; outBytes[2] = 0xBF;
                Array.Copy(newBody, 0, outBytes, 3, newBody.Length);
            }
            else
            {
                outBytes = newBody;
            }

            try
            {
                File.WriteAllBytes(target, outBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[失败] 写入新文件失败：" + ex.Message);
                Rollback(target, backupPath);
                return Done(EXIT_WRITE_FAIL);
            }

            // 12) 写后复查：新值正确 + 其余内容逐字节一致 + BOM 状态一致
            string verifyError = Verify(target, backupPath, oldValue, hasBom);
            if (verifyError != null)
            {
                Console.WriteLine("[失败] 写入后校验未通过：" + verifyError);
                Rollback(target, backupPath);
                return Done(EXIT_WRITE_FAIL);
            }
            Console.WriteLine("[校验] 通过：新值正确，除目标字段外内容与原文件逐字节一致，BOM 状态一致。");
            Console.WriteLine("[完成] 修改成功。");
            Console.WriteLine("[路径] 配置文件：" + target);
            Console.WriteLine("[路径] 备份文件：" + backupPath);

            // 13) 原先在运行则自动重启
            if (wasRunning && !optNoRestart)
            {
                RestartProgram(procExe);
            }
            else if (wasRunning && optNoRestart)
            {
                Console.WriteLine("[进程] --no-restart 已指定，未重新启动 LocModel。");
            }

            return Done(EXIT_OK);
        }
        // ====================================================================
        // 正则替换回调：保留分组1（键名+冒号+空白），只换值
        // ====================================================================
        private static string ReplaceEvaluator(Match m)
        {
            return m.Groups[1].Value + "\"" + TARGET_VALUE + "\"";
        }

        // ====================================================================
        // 归一化：把 custom_mirror 的值统一替换成占位符，用于逐字节一致性比对
        // ====================================================================
        private static string NormalizeMirror(string text)
        {
            return MirrorRegex.Replace(text, delegate(Match m)
            {
                return m.Groups[1].Value + "\"<MIRROR_PLACEHOLDER>\"";
            });
        }

        // ====================================================================
        // 从匹配文本中取出值（去掉两侧引号）
        // ====================================================================
        private static string ExtractValue(string matchText)
        {
            int colon = matchText.IndexOf(':');
            if (colon < 0) return matchText;
            string after = matchText.Substring(colon + 1).Trim();
            if (after.Length >= 2 && after[0] == '\"' && after[after.Length - 1] == '\"')
            {
                return after.Substring(1, after.Length - 2);
            }
            return after;
        }

        // ====================================================================
        // 写后校验，返回 null 表示通过，否则返回错误说明
        // ====================================================================
        private static string Verify(string target, string backupPath, string oldValue, bool oldHasBom)
        {
            byte[] nowRaw;
            byte[] bakRaw;
            try
            {
                nowRaw = File.ReadAllBytes(target);
                bakRaw = File.ReadAllBytes(backupPath);
            }
            catch (Exception ex) { return "重新读取文件失败：" + ex.Message; }

            // BOM 状态必须与原文一致
            bool nowHasBom = (nowRaw.Length >= 3 && nowRaw[0] == 0xEF && nowRaw[1] == 0xBB && nowRaw[2] == 0xBF);
            if (nowHasBom != oldHasBom) return "BOM 状态与原文件不一致。";

            int nOff = nowHasBom ? 3 : 0;
            int bOff = oldHasBom ? 3 : 0;
            string nowText = new UTF8Encoding(false).GetString(nowRaw, nOff, nowRaw.Length - nOff);
            string bakText = new UTF8Encoding(false).GetString(bakRaw, bOff, bakRaw.Length - bOff);

            // 新值必须正确
            MatchCollection mc = MirrorRegex.Matches(nowText);
            if (mc.Count != 1) return "写入后 " + KEY_NAME + " 键数量异常（" + mc.Count + " 个）。";
            string nowValue = ExtractValue(mc[0].Value);
            if (nowValue != TARGET_VALUE) return "写入后值为 \"" + nowValue + "\"，不是目标值。";

            // 除目标字段外，其余内容必须与备份逐字节一致
            if (NormalizeMirror(nowText) != NormalizeMirror(bakText))
            {
                return "除目标字段外，其它内容与原文件不一致。";
            }
            return null;
        }

        // ====================================================================
        // 回滚：删掉刚写入的新文件，把备份改回原名
        // ====================================================================
        private static void Rollback(string target, string backupPath)
        {
            try
            {
                if (File.Exists(target)) File.Delete(target); // 仅删除本工具刚生成的文件
            }
            catch (Exception ex) { Console.WriteLine("[警告] 回滚时删除新文件失败：" + ex.Message); }

            try
            {
                if (File.Exists(backupPath)) File.Move(backupPath, target);
                Console.WriteLine("[回滚] 已用备份恢复原文件。");
            }
            catch (Exception ex) { Console.WriteLine("[严重] 回滚失败，请手工把备份改回原名：" + ex.Message); }
        }

        // ====================================================================
        // 生成备份路径：settings.json.bak.yyyyMMdd-HHmmss，重名则追加 -1、-2
        // ====================================================================
        private static string BuildBackupPath(string target)
        {
            string baseName = target + ".bak." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (!File.Exists(baseName)) return baseName;
            for (int i = 1; i < 1000; i++)
            {
                string candidate = baseName + "-" + i;
                if (!File.Exists(candidate)) return candidate;
            }
            return baseName + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
        // ====================================================================
        // 进程处理：正常关闭 LocModel，最多等 15 秒，绝不强杀
        // ====================================================================
        private static int HandleProcess(out bool wasRunning, out string procExe)
        {
            wasRunning = false;
            procExe = null;

            Process[] ps;
            try { ps = GetLocModelProcesses(); }
            catch (Exception ex)
            {
                Console.WriteLine("[失败] 查询进程状态出错：" + ex.Message);
                return EXIT_UNKNOWN;
            }

            if (ps.Length == 0)
            {
                Console.WriteLine("[进程] LocModel 未运行。");
                return EXIT_OK;
            }

            wasRunning = true;
            StringBuilder ids = new StringBuilder();
            foreach (Process p in ps)
            {
                ids.Append(p.Id).Append(" ");
                // 记录可执行文件路径，用于改完后重启
                try
                {
                    string f = p.MainModule.FileName;
                    if (procExe == null && !string.IsNullOrEmpty(f)) procExe = f;
                }
                catch { /* 权限不足时忽略，后面用兜底方式推断 */ }
            }
            Console.WriteLine("[进程] LocModel 正在运行（PID: " + ids.ToString().Trim() + "），尝试正常关闭…");

            if (procExe == null) procExe = GuessExePath();

            foreach (Process p in ps)
            {
                try { p.CloseMainWindow(); }
                catch { /* 无窗口进程会失败，交给后面的超时判断 */ }
            }

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 15000)
            {
                Thread.Sleep(500);
                try { if (GetLocModelProcesses().Length == 0) break; }
                catch { break; }
            }

            Process[] remain;
            try { remain = GetLocModelProcesses(); }
            catch { remain = new Process[0]; }

            if (remain.Length > 0)
            {
                StringBuilder r = new StringBuilder();
                foreach (Process p in remain) r.Append(p.Id).Append(" ");
                Console.WriteLine("[失败] 等待 15 秒后进程仍未退出（PID: " + r.ToString().Trim() + "）。");
                Console.WriteLine("       本工具不会强杀进程，已中止，配置未做任何改动。");
                Console.WriteLine("       请手动退出 LocModel 后重新运行本工具。");
                return EXIT_PROC_FAIL;
            }

            Console.WriteLine("[进程] 已正常关闭。");
            return EXIT_OK;
        }

        // 取当前所有名为 LocModel 的进程（排除自身）
        private static Process[] GetLocModelProcesses()
        {
            Process[] all = Process.GetProcessesByName(PROC_NAME);
            List<Process> list = new List<Process>();
            int selfId = Process.GetCurrentProcess().Id;
            foreach (Process p in all)
            {
                if (p.Id != selfId) list.Add(p);
            }
            return list.ToArray();
        }

        // 兜底推断 LocModel.exe 路径（常见目录 / 注册表安装位置）
        private static string GuessExePath()
        {
            string[] roots = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PROC_NAME),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), PROC_NAME),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), PROC_NAME)
            };
            foreach (string r in roots)
            {
                try
                {
                    string exe = Path.Combine(r, PROC_NAME + ".exe");
                    if (File.Exists(exe)) return exe;
                }
                catch { }
            }
            try
            {
                string loc = FindInstallLocationFromRegistry();
                if (loc != null)
                {
                    string exe = Path.Combine(loc, PROC_NAME + ".exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }
            return null;
        }

        // 自动重启原程序
        private static void RestartProgram(string procExe)
        {
            if (procExe == null || !File.Exists(procExe))
            {
                Console.WriteLine("[提示] 未能确定 LocModel.exe 路径，请手动启动 LocModel。");
                return;
            }
            try
            {
                Process.Start(procExe);
                Console.WriteLine("[进程] 已重新启动：" + procExe);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[提示] 自动重启失败（" + ex.Message + "），请手动启动 LocModel。");
            }
        }
        // ====================================================================
        // 目标文件定位（按优先级 a -> d）
        // ====================================================================
        private static string ResolveTargetPath(List<string> probed)
        {
            // a) 命令行 --path 指定的路径
            if (!string.IsNullOrEmpty(optPath))
            {
                probed.Add("--path 指定：" + optPath);
                try
                {
                    string full = Path.GetFullPath(optPath);
                    if (File.Exists(full)) return full;
                    if (Directory.Exists(full))
                    {
                        string inDir = Path.Combine(full, CFG_NAME);
                        probed.Add("--path 目录下的 " + CFG_NAME + "：" + inDir);
                        if (File.Exists(inDir)) return inDir;
                    }
                }
                catch { }
                // --path 明确指定却找不到，直接失败，不再猜测
                return null;
            }

            // b) 本 exe 所在目录下的 settings.json（便携安装场景）
            try
            {
                string exeDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string p = Path.Combine(exeDir, CFG_NAME);
                    probed.Add("程序所在目录：" + p);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }

            // c) %LOCALAPPDATA%\LocModel\settings.json
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string p = Path.Combine(Path.Combine(localAppData, PROC_NAME), CFG_NAME);
                probed.Add("用户本地数据目录：" + p);
                if (File.Exists(p)) return p;
            }
            catch { }

            // d) 注册表 Uninstall 项的 InstallLocation
            try
            {
                string loc = FindInstallLocationFromRegistry();
                if (!string.IsNullOrEmpty(loc))
                {
                    string p1 = Path.Combine(loc, CFG_NAME);
                    probed.Add("注册表安装位置：" + p1);
                    if (File.Exists(p1)) return p1;

                    try
                    {
                        foreach (string sub in Directory.GetDirectories(loc))
                        {
                            string p2 = Path.Combine(sub, CFG_NAME);
                            probed.Add("注册表安装位置子目录：" + p2);
                            if (File.Exists(p2)) return p2;
                        }
                    }
                    catch { }
                }
                else
                {
                    probed.Add("注册表：未找到 DisplayName 含 LocModel 的卸载项");
                }
            }
            catch { }

            return null;
        }

        // 从注册表卸载项里找 LocModel 的 InstallLocation
        private static string FindInstallLocationFromRegistry()
        {
            string[] roots = new string[]
            {
                "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
            };
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };

            foreach (RegistryHive hive in hives)
            {
                foreach (string root in roots)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                        using (RegistryKey uninst = baseKey.OpenSubKey(root))
                        {
                            if (uninst == null) continue;
                            foreach (string subName in uninst.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey sub = uninst.OpenSubKey(subName))
                                    {
                                        if (sub == null) continue;
                                        object dn = sub.GetValue("DisplayName");
                                        if (dn == null) continue;
                                        if (dn.ToString().IndexOf(PROC_NAME, StringComparison.OrdinalIgnoreCase) < 0) continue;

                                        object il = sub.GetValue("InstallLocation");
                                        if (il != null)
                                        {
                                            string loc = il.ToString().Trim().Trim('\"');
                                            if (loc.Length > 0) return loc;
                                        }
                                        object us = sub.GetValue("UninstallString");
                                        if (us != null)
                                        {
                                            string s = us.ToString().Trim().Trim('\"');
                                            int idx = s.LastIndexOf('\\');
                                            if (idx > 0) return s.Substring(0, idx);
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        // ====================================================================
        // 命令行解析
        // ====================================================================
        private static bool ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? string.Empty;
                string lower = a.ToLowerInvariant();

                if (lower == "--help" || lower == "-h" || lower == "/?") { optHelp = true; }
                else if (lower == "--no-process") { optNoProc = true; }
                else if (lower == "--no-restart") { optNoRestart = true; }
                else if (lower == "--silent") { optSilent = true; }
                else if (lower == "--dry-run") { optDryRun = true; }
                else if (lower == "--path")
                {
                    if (i + 1 >= args.Length) { Console.WriteLine("[错误] --path 后面缺少路径参数。"); return false; }
                    optPath = args[++i];
                }
                else if (lower.StartsWith("--path="))
                {
                    optPath = a.Substring("--path=".Length);
                }
                else
                {
                    Console.WriteLine("[错误] 无法识别的参数：" + a);
                    return false;
                }
            }
            return true;
        }

        // ====================================================================
        // 输出辅助
        // ====================================================================
        private static void PrintHeader()
        {
            Console.WriteLine("==============================================");
            Console.WriteLine(" LocModel 镜像补丁工具  locmodel_patch");
            Console.WriteLine(" 作用：把 custom_mirror 改为 " + TARGET_VALUE);
            Console.WriteLine("==============================================");
        }

        private static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("用法：locmodel_patch.exe [选项]");
            Console.WriteLine();
            Console.WriteLine("选项：");
            Console.WriteLine("  --path <路径>     指定 settings.json 的完整路径，或其所在目录");
            Console.WriteLine("  --dry-run         只打印将要做的改动，不写入任何文件");
            Console.WriteLine("  --no-process      跳过 LocModel 进程检查与关闭");
            Console.WriteLine("  --no-restart      修改完成后不自动重启 LocModel");
            Console.WriteLine("  --silent          结束后不等待回车（便于脚本调用）");
            Console.WriteLine("  --help            显示本帮助");
            Console.WriteLine();
            Console.WriteLine("默认查找顺序（不带 --path 时）：");
            Console.WriteLine("  1) 本 exe 所在目录的 settings.json");
            Console.WriteLine("  2) %LOCALAPPDATA%\\LocModel\\settings.json");
            Console.WriteLine("  3) 注册表卸载项 InstallLocation 指向目录及其子目录");
            Console.WriteLine();
            Console.WriteLine("退出码：");
            Console.WriteLine("  0 成功（含“已是目标值，无需修改”）");
            Console.WriteLine("  1 未知错误 / 参数错误");
            Console.WriteLine("  2 找不到配置文件");
            Console.WriteLine("  3 配置里没有 custom_mirror 键");
            Console.WriteLine("  4 写入或校验失败（已自动回滚）");
            Console.WriteLine("  5 进程未能关闭（未做任何改动）");
            Console.WriteLine();
            Console.WriteLine("示例：");
            Console.WriteLine("  locmodel_patch.exe");
            Console.WriteLine("  locmodel_patch.exe --dry-run");
            Console.WriteLine("  locmodel_patch.exe --path \"D:\\Users\\me\\AppData\\Local\\LocModel\\settings.json\"");
            Console.WriteLine();
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += token.Length;
            }
            return count;
        }

        private static void Finish()
        {
            if (optSilent) return;
            Console.WriteLine();
            Console.Write("按回车键退出...");
            try { Console.ReadLine(); } catch { }
        }

        private static int Done(int code)
        {
            Finish();
            return code;
        }
    }
}