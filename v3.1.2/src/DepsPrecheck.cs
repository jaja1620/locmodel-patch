// LocModel Downloader v3.1.0 - 下载前文件复核（只读判断，绝不改动、不创建、不删除任何文件）
// 目的：在「本次将下载的文件」栏里标注哪些文件此前已下载完成，避免用户误判重复下载量。
// 复核口径（全部满足才算已存在）：
//   1) 目标文件存在且长度 > 0
//   2) 目标同目录没有 <file>.part / <file>.part.meta 半成品残留（有则视为未下完）
//   3) 声明大小已知时，实际长度 >= 声明大小
using System;
using System.Collections.Generic;
using System.IO;

namespace LocModelDL
{
    // 「本次将下载的文件」列表行：Text 已含「（已存在）」后缀，Exists 供渲染绿色
    public class DepsRow
    {
        public string Text = "";
        public bool Exists = false;
        public string DestPath = "";
        public DepsRow() { }
        public DepsRow(string text, bool exists, string dest) { Text = text; Exists = exists; DestPath = dest; }
        public override string ToString() { return Text; }
    }

    public static class DepsPrecheck
    {
        public const string ExistsTag = "（已存在）";

        // 复核单个目标文件：已下载完成 → true
        public static bool IsReady(string destPath, long expectedSize)
        {
            try
            {
                if (string.IsNullOrEmpty(destPath)) return false;
                if (!File.Exists(destPath)) return false;

                // 就地半成品残留 → 未下完（LocModel 3.5/3.6 主程序与内核都在就地留 .part / .part.meta）
                string part = destPath + ".part";
                if (File.Exists(part) || File.Exists(part + ".meta")) return false;

                FileInfo fi = new FileInfo(destPath);
                if (fi.Length <= 0) return false;

                // 声明大小已知时才做长度校验，未知（-1/0）不做上限判断，避免误判
                if (expectedSize > 0 && fi.Length < expectedSize) return false;
                return true;
            }
            catch { return false; }
        }

        // 语言/多模态模型的 mmproj 是「自动配对」的，预览阶段不知道确切文件名，
        // 因此按目录里是否存在非半成品的 mmproj 文件来判定
        public static bool HasMmproj(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string n = Path.GetFileName(f);
                    if (n.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                    if (n.EndsWith(".part.meta", StringComparison.OrdinalIgnoreCase)) continue;
                    if (n.IndexOf("mmproj", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { if (new FileInfo(f).Length > 0) return true; } catch { }
                    }
                }
            }
            catch { }
            return false;
        }

        public static string Suffix(bool exists) { return exists ? ExistsTag : ""; }

        // 标题文案：共 N 项，已存在 M 项
        public static string CountText(int total, int exist)
        {
            return "本次将下载的文件（共 " + total + " 项，已存在 " + exist + " 项）";
        }
    }
}
