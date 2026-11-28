// LocModel Downloader v3.1.5 - LocModel 配置只读查看（独立成段）
// =====================================================================================
// 目的：把 LocModel 3.6.5 settings.json 里的新字段（lipsync_variant / engine_cn / account_ips /
//       tps_cal_v / tps_records / ui_zoom / img_remote 等）和 hf_token 以「只读 + 打码」方式展示。
// 红线（与 v2.4.2 起的既有约束一致）：
//   * 本文件不含任何写盘代码，settings.json 只以 File.ReadAllText 打开；
//   * token 一律显示 CatalogService.ParseSettings 产出的 HfTokenDisplay（已打码），原文不出本进程内存；
//   * 敏感键名（含 token/secret/key/password/cookie）即使漏网也在这里二次打码。
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LocModelDL
{
    public class SettingsView : Form
    {
        static readonly Color CBg = Color.FromArgb(24, 25, 28);
        static readonly Color CPanel2 = Color.FromArgb(43, 45, 51);
        static readonly Color CText = Color.FromArgb(228, 230, 235);
        static readonly Color CDim = Color.FromArgb(150, 155, 165);

        public SettingsView(LocModelInfo info)
        {
            Text = "LocModel 配置（只读，不回写）";
            Width = 820; Height = 620; MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = CBg; ForeColor = CText;
            Font = new Font("Microsoft YaHei UI", 9f);

            TextBox txt = new TextBox();
            txt.Multiline = true; txt.ScrollBars = ScrollBars.Both; txt.WordWrap = false;
            txt.ReadOnly = true; txt.Dock = DockStyle.Fill;
            txt.BackColor = CPanel2; txt.ForeColor = CText; txt.BorderStyle = BorderStyle.FixedSingle;
            txt.Font = new Font("Consolas", 9f);
            txt.Text = Build(info);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom; bar.Height = 46; bar.BackColor = CBg;

            Button btnCopy = new Button();
            btnCopy.Text = "复制全部"; btnCopy.Bounds = new Rectangle(12, 10, 96, 28);
            btnCopy.FlatStyle = FlatStyle.Flat; btnCopy.BackColor = CPanel2; btnCopy.ForeColor = CText;
            btnCopy.Click += delegate { try { Clipboard.SetText(txt.Text); } catch { } };
            bar.Controls.Add(btnCopy);

            Label l = new Label();
            l.Text = "只读窗口：值为内存中的识别结果，任何操作都不会写回 settings.json。";
            l.ForeColor = CDim; l.AutoSize = true; l.Location = new Point(120, 16);
            bar.Controls.Add(l);

            Controls.Add(txt);
            Controls.Add(bar);
        }

        static string Build(LocModelInfo info)
        {
            StringBuilder sb = new StringBuilder();
            if (info == null) { sb.AppendLine("（未探测到 LocModel）"); return sb.ToString(); }

            sb.AppendLine("== 识别结果 ==");
            Row(sb, "LocModel 版本", info.Version + (info.VersionDetail == null || info.VersionDetail.Length == 0 ? "" : "（" + info.VersionDetail + "）"));
            Row(sb, "exe 路径", info.ExePath);
            Row(sb, "settings.json", info.SettingsPath);

            sb.AppendLine();
            sb.AppendLine("== 常用字段（只读）==");
            Row(sb, "models_dir", info.ModelsDir);
            Row(sb, "port", info.Port > 0 ? info.Port.ToString() : "");
            Row(sb, "hf_mirror", info.HfMirror);
            Row(sb, "custom_mirror", info.CustomMirror);
            Row(sb, "comfy_dir", info.ComfyDir);
            Row(sb, "images_dir", info.ImagesDir);
            Row(sb, "accel_lines", Join(info.AccelLines));
            Row(sb, "accel_nodes", Join(info.AccelNodes));
            Row(sb, "preferred_line", info.PreferredLine);
            Row(sb, "preferred_ip", info.PreferredIp);
            Row(sb, "github_mirror", info.GithubMirror);

            sb.AppendLine();
            sb.AppendLine("== 凭据（打码展示，永不回写）==");
            Row(sb, "hf_token", info.HfTokenDisplay.Length > 0 ? info.HfTokenDisplay + "  ←已打码" : (info.HasHfToken ? "（字段存在，本机未填值）" : "（未设置）"));

            sb.AppendLine();
            sb.AppendLine("== 3.6.5 新增字段（只读）==");
            if (info.HasV365Settings)
            {
                if (info.LipsyncVariant != null && info.LipsyncVariant.Length > 0)
                    Row(sb, "lipsync_variant", info.LipsyncVariant + "  → 对应下载档位：" + LipsyncCatalog.Get(LipsyncCatalog.KeyFromLocModelVariant(info.LipsyncVariant)).Label);
                else
                    Row(sb, "lipsync_variant", "（未设置）");
                foreach (V365Field f in info.ExtraSettings)
                    Row(sb, f.Name, f.Display + (f.Masked ? "  ←已打码" : ""));
            }
            else
            {
                sb.AppendLine("（本机 settings.json 未出现 3.6.5 专有字段）");
            }

            sb.AppendLine();
            sb.AppendLine("== 说明 ==");
            sb.AppendLine("对口型权重站点：" + LipsyncCatalog.SiteNote());
            return sb.ToString();
        }

        static void Row(StringBuilder sb, string k, string v)
        {
            if (v == null) v = "";
            sb.Append("  ").Append(k.PadRight(20)).Append(" = ").AppendLine(v);
        }

        static string Join(List<string> l)
        {
            if (l == null || l.Count == 0) return "";
            return string.Join(" | ", l.ToArray());
        }
    }
}
