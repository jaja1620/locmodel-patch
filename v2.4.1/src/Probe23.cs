// v2.3 离线校验工具：不联网打印版本探测结果与两套图像/视频清单（含 exe 解析结果 diff）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LocModelDL
{
    public static class VersionProbe
    {
        public static void Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            Console.Write(Dump());
        }

        static string Fmt(ModelEntry m)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(m.Id);
            sb.Append(" | " + m.Category);
            sb.Append(" | repo=" + m.Repo);
            sb.Append(" | tpl=" + m.MainFileTemplate + (string.IsNullOrEmpty(m.MainFile2Template) ? "" : " + " + m.MainFile2Template));
            sb.Append(" | quants=" + string.Join(",", m.Quants.ToArray()));
            sb.Append(" | release=" + m.Release);
            sb.Append(" | deps=" + m.Common.Count);
            foreach (QuantFile qf in m.Common) sb.Append(" [" + qf.Repo + " :: " + qf.Path + "]");
            return sb.ToString();
        }

        public static string Dump()
        {
            StringBuilder sb = new StringBuilder();
            LocModelInfo info = LocModelProbe.Probe();
            sb.AppendLine("=== 探测 ===");
            sb.AppendLine("ExePath=" + info.ExePath);
            sb.AppendLine("SettingsPath=" + info.SettingsPath);
            sb.AppendLine("ModelsDir=" + info.ModelsDir);
            sb.AppendLine("HfMirror=" + info.HfMirror + " CustomMirror=" + info.CustomMirror);
            sb.AppendLine("DetectedVersion=" + info.Version + " detail=" + info.VersionDetail);
            sb.AppendLine("HasAccelLines=" + info.HasAccelLines + " accel=" + string.Join(",", info.AccelLines.ToArray()));
            sb.AppendLine("ComfyDir=" + info.ComfyDir + " ImagesDir=" + info.ImagesDir);
            sb.AppendLine("CatalogBases(before)=" + string.Join(",", CatalogService.CatalogBasesSnapshot()));
            CatalogService.SetCatalogBases(info.AccelLines);
            sb.AppendLine("CatalogBases(after)=" + string.Join(",", CatalogService.CatalogBasesSnapshot()));
            sb.AppendLine();

            string[] vers = new string[] { "3.3", "3.1" };
            Dictionary<string, List<ModelEntry>> all = new Dictionary<string, List<ModelEntry>>();
            foreach (string v in vers)
            {
                LocModelProfile pf = LocModelProfile.Get(v);
                List<ModelEntry> cat = pf.ImageCatalog();
                string log;
                CatalogService.SyncImageCatalogFromExe(cat, info.ExePath, pf, out log);
                all[v] = cat;
                sb.AppendLine("=== 版本 " + v + " ===");
                sb.AppendLine("profile: " + pf.Note);
                sb.AppendLine("tempRoot(models)=" + pf.TempRoot(@"X:\models") + "  HasLoraDir=" + pf.HasLoraDir + " RecognizeComfyDir=" + pf.RecognizeComfyDir);
                sb.AppendLine("catalogNote: " + pf.CatalogNote);
                sb.AppendLine("syncLog: " + log);
                sb.AppendLine("count=" + cat.Count);
                foreach (ModelEntry m in cat) sb.AppendLine("  " + Fmt(m));
                sb.AppendLine();
            }

            // 两版本差异（应仅 minimax-h3-image）
            HashSet<string> a = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelEntry m in all["3.3"]) a.Add(m.Id);
            HashSet<string> b = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelEntry m in all["3.1"]) b.Add(m.Id);
            List<string> only33 = new List<string>();
            foreach (string s in a) if (!b.Contains(s)) only33.Add(s);
            List<string> only31 = new List<string>();
            foreach (string s in b) if (!a.Contains(s)) only31.Add(s);
            sb.AppendLine("=== 差异 ===");
            sb.AppendLine("仅 3.3: " + string.Join(",", only33.ToArray()));
            sb.AppendLine("仅 3.1: " + string.Join(",", only31.ToArray()));
            return sb.ToString();
        }
    }
}
