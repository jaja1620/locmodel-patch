// LocModel Downloader v3.1.5 - 对口型子系统下载面板（独立成段）
// =====================================================================================
// 本文件只做界面；下载动作在 LipsyncEngine.cs，清单与落盘规则在 LipsyncCatalog.cs。
// 三者不触碰主窗体已有的模型清单/详情区/下载内核；回滚 = 删这三个文件
// + 还原 MainForm.cs 中 lip- 标记块。
//
// 下载通道分工（与 LocModel 3.6.5 实测一致）：
//   ① 权重：全部是 HuggingFace 仓库文件 → 走「下载站点」栏位的站点（默认 hf-mirror.com），
//      复用内核 DownloadEngine（分片/续传/校验/回落语义完全一致，不新增任何线路）。
//   ② 程序包 InfiniteTalk.zip：GitHub / ModelScope 分发，HF 镜像不涵盖 → 用绝对地址直连，
//      下载失败只报错，不换用未设置的站点。
//   ③ 人声分离模型：dl.fbaipublicfiles.com 绝对地址。
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LocModelDL
{
    public class LipsyncForm : Form
    {
        static readonly Color CBg = Color.FromArgb(24, 25, 28);
        static readonly Color CPanel = Color.FromArgb(35, 37, 42);
        static readonly Color CPanel2 = Color.FromArgb(43, 45, 51);
        static readonly Color CText = Color.FromArgb(228, 230, 235);
        static readonly Color CDim = Color.FromArgb(150, 155, 165);
        static readonly Color COk = Color.FromArgb(126, 208, 148);
        static readonly Color CWarn = Color.FromArgb(232, 178, 96);

        readonly List<string> _sites;
        readonly string _modelsDir;
        readonly string _lmVariant;

        TextBox txtRoot;
        ComboBox cboVariant;
        CheckBox chkWeights, chkProgram, chkDemucs;
        Button btnBrowse, btnOpen, btnVerify, btnStart, btnCancel;
        ProgressBar pb;
        Label lblState, lblNote;
        ListBox lstFiles;
        TextBox txtLog;

        LipsyncEngine _job;
        Thread _worker;
        bool _busy;

        public LipsyncForm(List<string> sites, string modelsDir, string locModelVariant)
        {
            _sites = sites == null ? new List<string>() : sites;
            _modelsDir = modelsDir == null ? "" : modelsDir;
            _lmVariant = locModelVariant == null ? "" : locModelVariant;

            Text = "对口型模型（LocModel 3.6.5 子系统）";
            Width = 980; Height = 720; MinimumSize = new Size(880, 620);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = CBg; ForeColor = CText;
            Font = new Font("Microsoft YaHei UI", 9f);

            BuildUI();
            LoadDefaults();
        }

        // ---------------- UI ----------------

        Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text; b.Bounds = new Rectangle(x, y, w, 28);
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(56, 59, 66);
            b.BackColor = CPanel2; b.ForeColor = CText; b.Cursor = Cursors.Hand;
            return b;
        }

        void BuildUI()
        {
            Panel head = new Panel();
            head.Dock = DockStyle.Top; head.Height = 150; head.BackColor = CPanel; head.Padding = new Padding(16, 10, 16, 10);
            Controls.Add(head);

            Label l1 = new Label();
            l1.Text = "对口型目录（权重 + 程序包 + 人声分离模型都落在这里）";
            l1.ForeColor = CDim; l1.AutoSize = true; l1.Location = new Point(16, 8);
            head.Controls.Add(l1);

            txtRoot = new TextBox();
            txtRoot.Bounds = new Rectangle(16, 30, 760, 26);
            txtRoot.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtRoot.BackColor = CPanel2; txtRoot.ForeColor = CText; txtRoot.BorderStyle = BorderStyle.FixedSingle;
            txtRoot.Font = new Font("Consolas", 9.5f);
            head.Controls.Add(txtRoot);

            btnBrowse = MakeButton("浏览…", 784, 29, 72);
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Click += delegate { BrowseRoot(); };
            head.Controls.Add(btnBrowse);

            btnOpen = MakeButton("打开目录", 862, 29, 86);
            btnOpen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnOpen.Click += delegate { OpenRoot(); };
            head.Controls.Add(btnOpen);

            Label l2 = new Label();
            l2.Text = "档位"; l2.ForeColor = CDim; l2.AutoSize = true; l2.Location = new Point(16, 68);
            head.Controls.Add(l2);

            cboVariant = new ComboBox();
            cboVariant.DropDownStyle = ComboBoxStyle.DropDownList;
            cboVariant.FlatStyle = FlatStyle.Flat;
            cboVariant.Bounds = new Rectangle(56, 64, 250, 24);
            cboVariant.BackColor = CPanel2; cboVariant.ForeColor = CText;
            foreach (LipsyncVariant v in LipsyncCatalog.All()) cboVariant.Items.Add(v.Label + "  —  " + LipsyncCatalog.Fmt(v.TotalBytes()));
            cboVariant.SelectedIndex = 0;
            cboVariant.SelectedIndexChanged += delegate { RefreshFileList(); };
            head.Controls.Add(cboVariant);

            chkWeights = MakeCheck("模型权重", 330, 66, 96);
            chkProgram = MakeCheck("程序包（InfiniteTalk.zip，GitHub）", 434, 66, 268);
            chkDemucs = MakeCheck("人声分离模型（fbaipublicfiles）", 710, 66, 240);
            chkWeights.Checked = true;
            chkDemucs.Checked = true;
            head.Controls.Add(chkWeights); head.Controls.Add(chkProgram); head.Controls.Add(chkDemucs);

            lblNote = new Label();
            lblNote.AutoSize = false;
            lblNote.Bounds = new Rectangle(16, 94, 930, 18);
            lblNote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblNote.ForeColor = CWarn; lblNote.Font = new Font("Microsoft YaHei UI", 8f);
            lblNote.Text = LipsyncCatalog.SiteNote();
            head.Controls.Add(lblNote);

            lblState = new Label();
            lblState.AutoSize = false;
            lblState.Bounds = new Rectangle(16, 116, 930, 18);
            lblState.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblState.ForeColor = CDim; lblState.Font = new Font("Microsoft YaHei UI", 8f);
            head.Controls.Add(lblState);

            Panel tools = new Panel();
            tools.Dock = DockStyle.Top; tools.Height = 44; tools.BackColor = CBg; tools.Padding = new Padding(16, 8, 16, 8);
            Controls.Add(tools);

            btnVerify = MakeButton("结构自检", 16, 8, 88);
            btnVerify.Click += delegate { DoVerify(); };
            tools.Controls.Add(btnVerify);

            btnStart = MakeButton("开始下载", 112, 8, 88);
            btnStart.Click += delegate { StartDownload(); };
            tools.Controls.Add(btnStart);

            btnCancel = MakeButton("取消", 208, 8, 72);
            btnCancel.Enabled = false;
            btnCancel.Click += delegate { RequestCancel(); };
            tools.Controls.Add(btnCancel);

            pb = new ProgressBar();
            pb.Bounds = new Rectangle(292, 12, 660, 18);
            pb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tools.Controls.Add(pb);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill; body.BackColor = CBg; body.Padding = new Padding(16, 0, 16, 8);
            Controls.Add(body);

            lstFiles = new ListBox();
            lstFiles.Dock = DockStyle.Top; lstFiles.Height = 240;
            lstFiles.BackColor = CPanel2; lstFiles.ForeColor = CText; lstFiles.BorderStyle = BorderStyle.FixedSingle;
            lstFiles.Font = new Font("Consolas", 9f);
            lstFiles.HorizontalScrollbar = true;
            body.Controls.Add(lstFiles);

            txtLog = new TextBox();
            txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Dock = DockStyle.Fill;
            txtLog.BackColor = CPanel; txtLog.ForeColor = CDim; txtLog.BorderStyle = BorderStyle.FixedSingle;
            txtLog.Font = new Font("Consolas", 8.5f);
            txtLog.ReadOnly = true;
            body.Controls.Add(txtLog);
        }

        CheckBox MakeCheck(string text, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text; c.Bounds = new Rectangle(x, y, w, 22);
            c.ForeColor = CText; c.BackColor = Color.Transparent;
            return c;
        }

        // ---------------- 默认值 ----------------

        void LoadDefaults()
        {
            string root = LipsyncCatalog.DetectExistingRoot(_modelsDir);
            bool existed = root.Length > 0;
            if (!existed) root = LipsyncCatalog.DefaultRoot(_modelsDir);
            txtRoot.Text = root;

            string key = LipsyncCatalog.KeyFromLocModelVariant(_lmVariant);
            cboVariant.SelectedIndex = LipsyncCatalog.Get(key).Key == "low" ? 1 : 0;

            if (existed)
                SetState("已检测到本机对口型目录：" + root, COk);
            else if (_lmVariant.Length > 0)
                SetState("本机未发现已安装的对口型目录；LocModel 记录 lipsync_variant=" + _lmVariant
                    + "，已按该值预选档位。默认落盘 LocModel 程序数据目录 lipsync（只读识别，不改任何配置）。", CDim);
            else
                SetState("本机未发现已安装的对口型目录，默认落盘 LocModel 程序数据目录 lipsync。", CDim);

            RefreshFileList();
        }

        void SetState(string s, Color c)
        {
            lblState.Text = s; lblState.ForeColor = c;
        }

        string CurKey()
        {
            return cboVariant.SelectedIndex == 1 ? "low" : "hq";
        }

        void RefreshFileList()
        {
            lstFiles.Items.Clear();
            string root = (txtRoot.Text == null ? "" : txtRoot.Text.Trim());
            LipsyncVariant v = LipsyncCatalog.Get(CurKey());
            foreach (LipsyncFile f in v.Files)
            {
                string full = "";
                try { full = root.Length > 0 ? Path.Combine(root, f.DestRel) : ""; } catch { }
                string mark = "  ";
                if (full.Length > 0)
                {
                    if (File.Exists(full))
                    {
                        long len = -1;
                        try { len = new FileInfo(full).Length; } catch { }
                        mark = (f.Size > 0 && len == f.Size) ? "[有] " : "[不完整] ";
                    }
                    else mark = "[缺] ";
                }
                lstFiles.Items.Add(string.Format("{0}{1}  {2,10}  ← {3} :: {4}",
                    mark, f.DestRel, LipsyncCatalog.Fmt(f.Size), f.Repo, f.SrcPath));
            }
            if (chkProgram.Checked)
                lstFiles.Items.Add(string.Format("  program\\{0}  {1}  ← github.com/MeiGen-AI/InfiniteTalk", LipsyncCatalog.ProgramZipName, "未知"));
            if (chkDemucs.Checked)
                lstFiles.Items.Add(string.Format("  demucs\\{0}  {1}  ← dl.fbaipublicfiles.com", LipsyncCatalog.DemucsFileName, "未知"));
        }

        // ---------------- 动作 ----------------

        void BrowseRoot()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选择对口型目录（默认应为 LocModel 程序数据目录或 模型目录\\lipsync）";
            if (Directory.Exists(txtRoot.Text)) d.SelectedPath = txtRoot.Text;
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                txtRoot.Text = d.SelectedPath;
                RefreshFileList();
            }
        }

        void OpenRoot()
        {
            try
            {
                string p = txtRoot.Text == null ? "" : txtRoot.Text.Trim();
                if (p.Length == 0) return;
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + p + "\"");
            }
            catch (Exception ex) { LogLine("打开目录失败: " + ex.Message); }
        }

        void LogLine(string s)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s;
            try
            {
                if (txtLog.InvokeRequired) { txtLog.BeginInvoke(new Action<string>(LogLine), new object[] { s }); return; }
                txtLog.AppendText(line + "\r\n");
            }
            catch { }
        }

        void DoVerify()
        {
            RefreshFileList();
            string root = txtRoot.Text == null ? "" : txtRoot.Text.Trim();
            List<string> bad = LipsyncCatalog.VerifyStructure(root, CurKey());
            if (bad.Count == 0) { SetState("结构自检通过：当前档位全部权重就位且大小一致。", COk); LogLine("结构自检通过"); return; }
            SetState("结构自检未通过，缺失/不完整 " + bad.Count + " 项（清单见下方日志）。", CWarn);
            foreach (string b in bad) LogLine("缺失/不完整 - " + b);
        }

        void RequestCancel()
        {
            LipsyncEngine j = _job;
            if (j != null) j.Cancelled = true;
            LogLine("已请求取消，等待当前分片收尾…");
        }

        void StartDownload()
        {
            if (_busy) return;
            string root = txtRoot.Text == null ? "" : txtRoot.Text.Trim();
            if (root.Length == 0) { MessageBox.Show(this, "请先选择对口型目录。", "提示"); return; }
            if (!chkWeights.Checked && !chkProgram.Checked && !chkDemucs.Checked)
            { MessageBox.Show(this, "至少勾选一项下载内容。", "提示"); return; }

            try { if (!Directory.Exists(root)) Directory.CreateDirectory(root); }
            catch (Exception ex) { MessageBox.Show(this, "目录不可写：" + ex.Message, "提示"); return; }

            string key = CurKey();
            LipsyncEngine job = new LipsyncEngine();
            job.Root = root;
            job.Key = key;
            job.DoWeights = chkWeights.Checked;
            job.DoProgram = chkProgram.Checked;
            job.DoDemucs = chkDemucs.Checked;
            job.Sites = new List<string>(_sites);
            job.Log += delegate (string s) { LogLine(s); };
            job.Progress += delegate (long done, long total, double sp) { UiProgress(done, total, sp); };
            _job = job;

            _busy = true;
            btnStart.Enabled = false; btnCancel.Enabled = true; btnVerify.Enabled = false;
            pb.Value = 0;
            txtLog.Clear();
            LogLine("对口型目录：" + root);
            LogLine("档位：" + LipsyncCatalog.Get(key).Label);
            LogLine("下载站点（仅用于权重）：" + (job.Sites.Count == 0 ? "（栏位为空，用默认站点）" : string.Join(" , ", job.Sites.ToArray())));

            Thread t = new Thread(delegate ()
            {
                bool ok = false;
                try { ok = job.Run(); }
                catch (Exception ex) { LogLine("异常终止: " + ex.Message); }
                UiDone(ok);
            });
            t.IsBackground = true;
            _worker = t;
            t.Start();
        }

        void UiDone(bool ok)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action<bool>(delegate (bool ok2)
                {
                    _busy = false; btnStart.Enabled = true; btnCancel.Enabled = false; btnVerify.Enabled = true;
                    _job = null;
                    string root = txtRoot.Text.Trim();
                    List<string> bad = LipsyncCatalog.VerifyStructure(root, CurKey());
                    if (ok && bad.Count == 0) SetState("下载完成，结构自检通过。", COk);
                    else if (ok && bad.Count > 0) SetState("下载流程结束，仍有 " + bad.Count + " 项缺失/不完整，请再点“开始下载”续传或看日志。", CWarn);
                    else SetState("下载未全部成功，请看日志；已完成的文件不会重下。", CWarn);
                    RefreshFileList();
                }), new object[] { ok });
            }
            catch { }
        }

        void UiProgress(long done, long total, double sp)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action<long, long, double>(delegate (long d, long t, double s2)
                {
                    if (t <= 0) return;
                    int v = (int)Math.Min(100, Math.Max(0, d * 100 / t));
                    pb.Value = v;
                    SetState(string.Format("总进度 {0}/{1}（{2}%）{3}", LipsyncCatalog.Fmt(d), LipsyncCatalog.Fmt(t), v,
                        s2 > 0 ? "  " + LipsyncCatalog.Fmt((long)s2) + "/s" : ""), CDim);
                }), new object[] { done, total, sp });
            }
            catch { }
        }
    }
}
