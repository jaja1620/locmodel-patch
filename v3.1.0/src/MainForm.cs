// LocModel Downloader v3.1.0 - UI
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LocModelDL
{
    public class MainForm : Form
    {
        // theme
        static readonly Color CBg = Color.FromArgb(24, 25, 28);
        static readonly Color CPanel = Color.FromArgb(35, 37, 42);
        static readonly Color CPanel2 = Color.FromArgb(43, 45, 51);
        static readonly Color CText = Color.FromArgb(228, 230, 235);
        static readonly Color CDim = Color.FromArgb(150, 155, 165);
        static readonly Color CAccent = Color.FromArgb(88, 132, 245);
        static readonly Color CLine = Color.FromArgb(56, 59, 66);
        // v3.1.0：复核通过（文件此前已下载完成）的行用绿色
        static readonly Color CExists = Color.FromArgb(126, 208, 148);

        LocModelInfo _info = new LocModelInfo();
        List<ModelEntry> _all = new List<ModelEntry>();
        List<ModelEntry> _view = new List<ModelEntry>();
        ModelEntry _sel;
        string _category = "image";
        bool _mismatchWarned = false;
        volatile bool _busy = false;
        DownloadEngine _eng;
        // v2.4.1：下载完成后的后续动作（源权重下载 → 本地转换）
        System.Action<bool> _onFinished;

        TextBox txtDir;
        TextBox txtSites;
        Label lblDirState;
        Label lblSiteState;
        // v3.0.0：并发分片下载
        ComboBox cboSeg;
        Label lblSegState;
        static readonly int[] SegValues = new int[] { 1, 2, 3, 4, 6, 8 };
        static readonly string[] SegItems = new string[] { "1（单连接）", "2 条连接", "3 条连接", "4 条连接（推荐）", "6 条连接", "8 条连接" };
        ListBox lstModels;
        Panel pnlDetail;
        Label lblTitle, lblOrg, lblDesc, lblRepo, lblSize, lblDepTitle, lblProgress;
        ComboBox cboQuant;
        ListBox lstDeps;
        Button btnDownload, btnCancel, btnOpen, btnRefresh, btnBrowse;
        ProgressBar pb;
        TextBox txtLog;
        Label lblStatus;
        ComboBox cboVer;
        Label lblVerState;
        bool _verEventOn = false;
        string _ver = VersionStore.Load();
        LocModelProfile _profile = LocModelProfile.Get(VersionStore.Load());

        public MainForm()
        {
            Text = "LocModel 模型下载器 v3.1.0";
            Width = 1320; Height = 900;
            MinimumSize = new Size(1120, 760);
            BackColor = CBg; ForeColor = CText;
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            DownloadConfig.Load();          // v3.0.0：并发分片下载配置（exe 同目录纯文本）
            BuildUI();
            txtSites.Text = SiteStore.Load();
            UpdateSegState();
            FormClosing += delegate { try { SiteStore.Save(txtSites.Text); } catch { } };
            Shown += delegate { ReloadAll(true); };
        }

        void BuildUI()
        {
            // ===== 顶部：目录栏 =====
            Panel top = new Panel();
            top.Dock = DockStyle.Top; top.Height = 116; top.BackColor = CPanel; top.Padding = new Padding(16, 10, 16, 10);
            Controls.Add(top);

            Label l1 = new Label();
            l1.Text = "模型保存位置"; l1.ForeColor = CDim; l1.AutoSize = true; l1.Location = new Point(16, 12);
            top.Controls.Add(l1);

            txtDir = new TextBox();
            txtDir.Location = new Point(16, 34); txtDir.Width = 720; txtDir.Height = 26;
            txtDir.BackColor = CPanel2; txtDir.ForeColor = CText; txtDir.BorderStyle = BorderStyle.FixedSingle;
            txtDir.Font = new Font("Consolas", 9.5f);
            txtDir.TextChanged += delegate { OnDirChanged(); };
            top.Controls.Add(txtDir);

            btnBrowse = MakeButton("浏览…", 744, 33, 74);
            btnBrowse.Click += delegate { BrowseDir(); };
            top.Controls.Add(btnBrowse);

            btnRefresh = MakeButton("重新从 LocModel 获取", 826, 33, 168);
            btnRefresh.Click += delegate { ReloadAll(true); };
            top.Controls.Add(btnRefresh);

            btnOpen = MakeButton("打开目录", 1002, 33, 90);
            btnOpen.Click += delegate { OpenDir(); };
            top.Controls.Add(btnOpen);

            lblDirState = new Label();
            lblDirState.AutoSize = false; lblDirState.Location = new Point(16, 63); lblDirState.Width = 1080; lblDirState.Height = 18;
            lblDirState.ForeColor = CDim; lblDirState.Font = new Font("Microsoft YaHei UI", 8.5f);
            top.Controls.Add(lblDirState);

            // ===== 顶部第三行：LocModel 版本选择（v2.3 新增）=====
            Label lVer = new Label();
            lVer.Text = "LocModel 版本"; lVer.ForeColor = CDim; lVer.AutoSize = true; lVer.Location = new Point(16, 90);
            top.Controls.Add(lVer);

            cboVer = new ComboBox();
            cboVer.DropDownStyle = ComboBoxStyle.DropDownList;
            cboVer.Bounds = new Rectangle(104, 86, 180, 24);
            cboVer.BackColor = CPanel2; cboVer.ForeColor = CText; cboVer.FlatStyle = FlatStyle.Flat;
            foreach (string v in LocModelProfile.Versions) cboVer.Items.Add(LocModelProfile.LabelOf(v));
            int vi = Array.IndexOf(LocModelProfile.Versions, _ver);
            if (vi < 0) vi = 0;
            cboVer.SelectedIndex = vi;
            cboVer.SelectedIndexChanged += delegate { OnVersionChanged(); };
            top.Controls.Add(cboVer);

            lblVerState = new Label();
            lblVerState.AutoSize = false; lblVerState.Bounds = new Rectangle(296, 90, 800, 18);
            lblVerState.ForeColor = CDim; lblVerState.Font = new Font("Microsoft YaHei UI", 8.5f);
            lblVerState.Text = "切换后按该版本重新加载模型清单与落盘规则；下载与校验内核不随版本变化";
            top.Controls.Add(lblVerState);

            _verEventOn = true;

            // ===== 顶部第二行：下载站点（v2.2：默认 hf-mirror.com，可自定义多个，下载前自动测速择优）=====
            Panel site = new Panel();
            site.Dock = DockStyle.Top; site.Height = 124; site.BackColor = CPanel; site.Padding = new Padding(16, 6, 16, 6);
            Controls.Add(site);

            Label l2 = new Label();
            l2.Text = "下载站点（可填多个：每行一个或用逗号分隔；下载前自动测速，最快的线路优先使用）";
            l2.ForeColor = CDim; l2.AutoSize = true; l2.Location = new Point(16, 6);
            l2.Font = new Font("Microsoft YaHei UI", 8.5f);
            site.Controls.Add(l2);

            txtSites = new TextBox();
            txtSites.Multiline = true; txtSites.ScrollBars = ScrollBars.Vertical;
            txtSites.Bounds = new Rectangle(16, 26, 720, 48);
            txtSites.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSites.BackColor = CPanel2; txtSites.ForeColor = CText; txtSites.BorderStyle = BorderStyle.FixedSingle;
            txtSites.Font = new Font("Consolas", 9f);
            site.Controls.Add(txtSites);

            Button btnSiteTest = MakeButton("测速预览", 744, 25, 96);
            btnSiteTest.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSiteTest.Click += delegate { PreviewSites(); };
            site.Controls.Add(btnSiteTest);

            Button btnSiteReset = MakeButton("恢复默认", 848, 25, 88);
            btnSiteReset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSiteReset.Click += delegate
            {
                txtSites.Text = SiteStore.DefaultSite;
                SiteStore.Save(txtSites.Text);
                lblSiteState.Text = "已恢复默认站点：" + SiteStore.DefaultSite;
            };
            site.Controls.Add(btnSiteReset);

            lblSiteState = new Label();
            lblSiteState.AutoSize = false;
            lblSiteState.Bounds = new Rectangle(16, 76, 1076, 16);
            lblSiteState.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblSiteState.ForeColor = CDim; lblSiteState.Font = new Font("Microsoft YaHei UI", 8f);
            lblSiteState.Text = "默认站点 " + SiteStore.DefaultSite + "（国内镜像）。可自行增删，修改后自动保存（官方 m1/m2 已从内置线路移除）。";
            site.Controls.Add(lblSiteState);

            // ===== v3.0.0：并发分片下载（Range 多连接）=====
            Label lSeg = new Label();
            lSeg.Text = "并发连接数";
            lSeg.ForeColor = CDim; lSeg.AutoSize = true; lSeg.Location = new Point(16, 101);
            lSeg.Font = new Font("Microsoft YaHei UI", 8.5f);
            site.Controls.Add(lSeg);

            cboSeg = new ComboBox();
            cboSeg.DropDownStyle = ComboBoxStyle.DropDownList;
            cboSeg.FlatStyle = FlatStyle.Flat;
            cboSeg.Bounds = new Rectangle(88, 97, 120, 24);
            cboSeg.BackColor = CPanel2; cboSeg.ForeColor = CText;
            foreach (string s in SegItems) cboSeg.Items.Add(s);
            cboSeg.SelectedIndex = SegIndex(DownloadConfig.Connections);
            cboSeg.SelectedIndexChanged += delegate { OnSegChanged(); };
            site.Controls.Add(cboSeg);

            lblSegState = new Label();
            lblSegState.AutoSize = false;
            lblSegState.Bounds = new Rectangle(220, 101, 972, 18);
            lblSegState.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblSegState.ForeColor = CDim; lblSegState.Font = new Font("Microsoft YaHei UI", 8f);
            site.Controls.Add(lblSegState);

            // ===== 左侧分类 =====
            Panel left = new Panel();
            left.Dock = DockStyle.Left; left.Width = 176; left.BackColor = CPanel; left.Padding = new Padding(10, 12, 10, 10);
            Controls.Add(left);

            string[] cats = new string[] { "图像模型", "视频模型", "语言 / 推理", "全部" };
            int y = 12;
            foreach (string c in cats)
            {
                Button b = MakeButton(c, 10, y, 156);
                b.Tag = c;
                b.Height = 38;
                b.Click += delegate(object s, EventArgs e)
                {
                    Button bb = (Button)s;
                    SetCategory((string)bb.Tag);
                };
                left.Controls.Add(b);
                y += 46;
            }

            Label tip = new Label();
            tip.Text = "下载规则与 LocModel\n自带下载页一致：\n· 语言模型 → {目录}\\{owner}__{repo}\\\n· 图像/视频 → {目录}\\image\\{模型名}\\\n· 共享依赖 → {目录}\\image\\common\\";
            tip.ForeColor = CDim; tip.AutoSize = false; tip.Bounds = new Rectangle(10, y + 16, 156, 190);
            tip.Font = new Font("Microsoft YaHei UI", 8f);
            left.Controls.Add(tip);

            // v2.4：在线搜索入口（等价 LocModel「搜索」选项卡）
            Button btnHub = MakeButton("在线搜索模型 …", 10, y + 214, 156);
            btnHub.Height = 40;
            btnHub.BackColor = CAccent; btnHub.ForeColor = Color.White;
            btnHub.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            btnHub.Click += delegate { OpenHubBrowser(); };
            left.Controls.Add(btnHub);

            // ===== 底部日志 =====
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom; bottom.Height = 150; bottom.BackColor = CPanel;
            Controls.Add(bottom);

            Label lt = new Label();
            lt.Text = "运行日志"; lt.ForeColor = CDim; lt.AutoSize = true; lt.Location = new Point(14, 6);
            bottom.Controls.Add(lt);

            txtLog = new TextBox();
            txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.ReadOnly = true;
            txtLog.Dock = DockStyle.Bottom; txtLog.Height = 120;
            txtLog.BackColor = Color.FromArgb(18, 19, 22); txtLog.ForeColor = Color.FromArgb(170, 200, 170);
            txtLog.BorderStyle = BorderStyle.None; txtLog.Font = new Font("Consolas", 8.5f);
            bottom.Controls.Add(txtLog);

            // ===== 中间模型列表 =====
            Panel mid = new Panel();
            mid.Dock = DockStyle.Left; mid.Width = 342; mid.BackColor = CBg; mid.Padding = new Padding(10, 10, 6, 8);
            Controls.Add(mid);

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Top; lblStatus.Height = 24; lblStatus.ForeColor = CDim;
            lblStatus.Font = new Font("Microsoft YaHei UI", 8.5f);
            lblStatus.Text = "正在从 LocModel 获取…";
            mid.Controls.Add(lblStatus);

            lstModels = new ListBox();
            lstModels.Dock = DockStyle.Fill;
            lstModels.BackColor = CBg; lstModels.ForeColor = CText; lstModels.BorderStyle = BorderStyle.None;
            lstModels.DrawMode = DrawMode.OwnerDrawFixed; lstModels.ItemHeight = 62;
            lstModels.IntegralHeight = false;
            lstModels.DrawItem += DrawModelItem;
            lstModels.SelectedIndexChanged += delegate { OnSelectModel(); };
            mid.Controls.Add(lstModels);

            // ===== 右侧详情 =====
            pnlDetail = new Panel();
            pnlDetail.Dock = DockStyle.Fill; pnlDetail.BackColor = CBg; pnlDetail.Padding = new Padding(18, 12, 18, 8);
            pnlDetail.AutoScroll = true;
            Controls.Add(pnlDetail);
            pnlDetail.BringToFront();

            lblTitle = new Label();
            lblTitle.AutoSize = false; lblTitle.Bounds = new Rectangle(18, 14, 700, 34);
            lblTitle.Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold); lblTitle.ForeColor = CText;
            pnlDetail.Controls.Add(lblTitle);

            lblOrg = new Label();
            lblOrg.AutoSize = false; lblOrg.Bounds = new Rectangle(20, 50, 700, 22);
            lblOrg.ForeColor = CDim; lblOrg.Font = new Font("Microsoft YaHei UI", 9f);
            pnlDetail.Controls.Add(lblOrg);

            lblDesc = new Label();
            // v2.4.2：LocModel 3.5 起详情面板多一行「模型规格：…」，描述区加高 22px，下方控件整体下移
            lblDesc.AutoSize = false; lblDesc.Bounds = new Rectangle(20, 80, 700, 88);
            lblDesc.ForeColor = CText; lblDesc.Font = new Font("Microsoft YaHei UI", 9.5f);
            pnlDetail.Controls.Add(lblDesc);

            lblRepo = new Label();
            lblRepo.AutoSize = false; lblRepo.Bounds = new Rectangle(20, 172, 700, 20);
            lblRepo.ForeColor = Color.FromArgb(120, 180, 255); lblRepo.Font = new Font("Consolas", 9f);
            pnlDetail.Controls.Add(lblRepo);

            Label lq = new Label();
            lq.Text = "量化档"; lq.AutoSize = true; lq.Location = new Point(20, 204); lq.ForeColor = CDim;
            pnlDetail.Controls.Add(lq);

            cboQuant = new ComboBox();
            cboQuant.DropDownStyle = ComboBoxStyle.DropDownList;
            cboQuant.Bounds = new Rectangle(80, 200, 220, 26);
            cboQuant.BackColor = CPanel2; cboQuant.ForeColor = CText; cboQuant.FlatStyle = FlatStyle.Flat;
            cboQuant.SelectedIndexChanged += delegate { OnQuantChanged(); };
            pnlDetail.Controls.Add(cboQuant);

            lblSize = new Label();
            lblSize.AutoSize = false; lblSize.Bounds = new Rectangle(316, 204, 460, 20);
            lblSize.ForeColor = CDim; lblSize.Font = new Font("Microsoft YaHei UI", 8.5f);
            pnlDetail.Controls.Add(lblSize);

            btnDownload = MakeButton("开始下载", 20, 236, 130);
            btnDownload.BackColor = CAccent; btnDownload.ForeColor = Color.White; btnDownload.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            btnDownload.Height = 36;
            btnDownload.Click += delegate { StartDownload(); };
            pnlDetail.Controls.Add(btnDownload);

            btnCancel = MakeButton("取消下载", 160, 236, 104);
            btnCancel.Height = 36; btnCancel.Enabled = false;
            btnCancel.Click += delegate { RequestCancel(); };
            pnlDetail.Controls.Add(btnCancel);

            pb = new ProgressBar();
            pb.Bounds = new Rectangle(20, 280, 700, 14);
            pb.Style = ProgressBarStyle.Continuous;
            pnlDetail.Controls.Add(pb);

            lblProgress = new Label();
            lblProgress.AutoSize = false; lblProgress.Bounds = new Rectangle(20, 298, 700, 20);
            lblProgress.ForeColor = CDim; lblProgress.Font = new Font("Consolas", 8.5f);
            pnlDetail.Controls.Add(lblProgress);

            lblDepTitle = new Label();
            lblDepTitle.AutoSize = false; lblDepTitle.Bounds = new Rectangle(20, 328, 700, 20);
            lblDepTitle.ForeColor = CDim; lblDepTitle.Text = "本次将下载的文件";
            pnlDetail.Controls.Add(lblDepTitle);

            lstDeps = new ListBox();
            lstDeps.Bounds = new Rectangle(20, 352, 700, 190);
            lstDeps.BackColor = CPanel; lstDeps.ForeColor = CText; lstDeps.BorderStyle = BorderStyle.None;
            lstDeps.Font = new Font("Consolas", 8.5f);
            lstDeps.HorizontalScrollbar = true;
            // v3.1.0：自绘每条，已复核存在的文件用绿色
            lstDeps.DrawMode = DrawMode.OwnerDrawFixed;
            lstDeps.ItemHeight = (int)Math.Ceiling(lstDeps.Font.GetHeight()) + 4;
            lstDeps.DrawItem += LstDeps_DrawItem;
            pnlDetail.Controls.Add(lstDeps);

            Resize += delegate { LayoutDetail(); };

            // 停靠顺序：top 最先、Fill 最后
            pnlDetail.BringToFront();
            mid.SendToBack();
            left.SendToBack();
            bottom.SendToBack();
            site.SendToBack();     // v2.2：站点栏紧随目录栏，位于各类列表之上
            top.SendToBack();
        }

        Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text; b.Bounds = new Rectangle(x, y, w, 26);
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = CLine;
            b.BackColor = CPanel2; b.ForeColor = CText;
            b.Cursor = Cursors.Hand;
            return b;
        }

        // ============ v2.4 在线搜索（等价 LocModel「搜索」选项卡）============
        public string ModelsDir { get { return txtDir == null ? "" : txtDir.Text.Trim(); } }
        public bool IsDownloading { get { return _busy; } }
        public void SetModelsDir(string d) { if (txtDir != null && !string.IsNullOrEmpty(d)) { txtDir.Text = d; try { Directory.CreateDirectory(d); } catch { } } }

        // v2.4.2：--uicheck 预览用，程序化选中模型（仅自检/截图，不影响正常交互）
        public void PreviewSelect(int index)
        {
            if (lstModels == null || index < 0 || index >= lstModels.Items.Count) return;
            lstModels.SelectedIndex = index;
            Application.DoEvents();
        }

        void OpenHubBrowser()
        {
            if (_busy) { Log("任务执行中，无法打开在线搜索"); MessageBox.Show(this, "正在下载中，请等待或取消后再打开在线搜索", "任务中"); return; }
            SaveSitesAndApply();
            HubBrowserForm f = new HubBrowserForm(this);
            f.ShowDialog(this);
        }

        // 在线搜索结果 → 复用本窗口的测速/进度/续传/校验内核
        public bool DownloadFromHub(string repo, string quantName, IList<string> files, IList<long> sizes)
        {
            if (_busy) { MessageBox.Show(this, "已有任务在执行，请等待结束或取消后再试", "任务中"); return false; }
            if (files == null || files.Count == 0) { MessageBox.Show(this, "没有可下载的文件", "清单为空"); return false; }
            string modelsDir = ModelsDir;
            if (string.IsNullOrEmpty(modelsDir)) { MessageBox.Show(this, "请先在主界面设置模型保存位置", "缺少目录"); return false; }
            try { if (!Directory.Exists(modelsDir)) Directory.CreateDirectory(modelsDir); } catch { }

            // 落盘规则与 LocModel 语言模型一致：{目录}\{owner}__{repo}\
            int sl = repo.IndexOf('/');
            string seg = sl > 0 ? repo.Substring(0, sl) + "__" + repo.Substring(sl + 1) : repo;
            string mainDir = Path.Combine(modelsDir, SafeSeg(seg));
            try { if (!Directory.Exists(mainDir)) Directory.CreateDirectory(mainDir); } catch { }

            List<DownloadItem> list = new List<DownloadItem>();
            for (int i = 0; i < files.Count; i++)
            {
                DownloadItem it = new DownloadItem();
                it.Repo = repo; it.Path = files[i];
                it.DestPath = Path.Combine(mainDir, BaseName(files[i]));
                it.ExpectedSize = (sizes != null && i < sizes.Count && sizes[i] > 0) ? sizes[i] : -1;
                it.Label = repo + " / " + quantName + " / " + BaseName(files[i]);
                list.Add(it);
            }

            SaveSitesAndApply();
            _busy = true; btnDownload.Enabled = false; btnCancel.Enabled = true;
            pb.Value = 0;
            _onFinished = null;
            Log("在线搜索下载：" + repo + " / " + quantName + "（" + list.Count + " 个文件）→ " + mainDir);
            lblDepsRefresh(list);
            RunDownload(list, modelsDir);
            return true;
        }

        // 没有 GGUF 的仓库：先下载 safetensors 源权重，下载完成后本地转换 + 量化
        public bool DownloadAndConvert(string repo, string quantName, IList<string> files, IList<long> sizes, string outType, long estBytes)
        {
            if (_busy) { MessageBox.Show(this, "已有任务在执行，请等待结束或取消后再试", "任务中"); return false; }
            if (files == null || files.Count == 0) { MessageBox.Show(this, "没有可下载的源文件", "清单为空"); return false; }
            string modelsDir = ModelsDir;
            if (string.IsNullOrEmpty(modelsDir)) { MessageBox.Show(this, "请先在主界面设置模型保存位置", "缺少目录"); return false; }
            try { if (!Directory.Exists(modelsDir)) Directory.CreateDirectory(modelsDir); } catch { }

            int sl = repo.IndexOf('/');
            string seg = sl > 0 ? repo.Substring(0, sl) + "__" + repo.Substring(sl + 1) : repo;
            string mainDir = Path.Combine(modelsDir, SafeSeg(seg));
            string srcDir = Path.Combine(mainDir, "_source");
            try { if (!Directory.Exists(mainDir)) Directory.CreateDirectory(mainDir); } catch { }
            try { if (!Directory.Exists(srcDir)) Directory.CreateDirectory(srcDir); } catch { }

            long srcBytes = 0;
            List<DownloadItem> list = new List<DownloadItem>();
            List<string> tiny = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                long sz = (sizes != null && i < sizes.Count && sizes[i] > 0) ? sizes[i] : -1;
                // 内核 Verify 把 <512B 当错误响应丢弃，元数据小文件改走轻量 GET（见 CatalogService.FetchSmallFile）
                if (sz > 0 && sz < 512) { tiny.Add(files[i]); continue; }
                DownloadItem it = new DownloadItem();
                it.Repo = repo; it.Path = files[i];
                it.DestPath = Path.Combine(srcDir, BaseName(files[i]));
                it.ExpectedSize = sz;
                it.Label = repo + " / 源权重 / " + BaseName(files[i]);
                if (it.ExpectedSize > 0) srcBytes += it.ExpectedSize;
                list.Add(it);
            }

            // 空间预检：源权重 + f16 中间产物（≈源大小）+ 目标 GGUF
            long need = srcBytes + srcBytes + (estBytes > 0 ? estBytes : srcBytes / 2);
            try
            {
                DriveInfo di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(modelsDir)));
                if (need > 0 && di.AvailableFreeSpace < need)
                {
                    DialogResult dr = MessageBox.Show(this,
                        "下载源权重、转换和量化一共需要约 " + GB(need) + "，\n模型目录所在盘只剩 " + GB(di.AvailableFreeSpace) + "。\n\n继续可能中途失败，是否仍要继续？",
                        "空间不足", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (dr != DialogResult.Yes) return false;
                }
                else if (need > 0) Log("空间预检：" + seg + " 需要约 " + GB(need) + "，目标盘剩余 " + GB(di.AvailableFreeSpace));
            }
            catch { }

            SaveSitesAndApply();
            _busy = true; btnDownload.Enabled = false; btnCancel.Enabled = true;
            pb.Value = 0;
            Log("下载并转换：" + repo + " / " + quantName + "（源 " + list.Count + " 个文件，" + GB(srcBytes) + "）→ " + srcDir);
            lblDepsRefresh(list);

            List<string> local = new List<string>();
            foreach (DownloadItem it in list) local.Add(it.DestPath);
            foreach (string p in tiny)
            {
                string dp = Path.Combine(srcDir, BaseName(p));
                string terr;
                if (CatalogService.FetchSmallFile(repo, p, dp, out terr)) { local.Add(dp); Log("小文件直取：" + BaseName(p)); }
                else Log("小文件获取失败：" + BaseName(p) + "（" + terr + "）");
            }
            string outDir = mainDir;
            if (list.Count == 0)
            {
                Log("源清单里只有元数据小文件，直接进入转换");
                _busy = false; btnCancel.Enabled = false;
                StartConvert(repo, quantName, outType, srcDir, outDir, local);
                return true;
            }
            _onFinished = delegate(bool ok)
            {
                if (!ok) { Log("源权重未全部下载成功，转换不启动；再次点击下载只会补下未完成的文件"); return; }
                StartConvert(repo, quantName, outType, srcDir, outDir, local);
            };
            RunDownload(list, modelsDir);
            return true;
        }

        void StartConvert(string repo, string quantName, string outType, string srcDir, string outDir, List<string> localFiles)
        {
            _busy = true; btnDownload.Enabled = false; btnCancel.Enabled = true;
            pb.Value = 0; pb.Style = ProgressBarStyle.Marquee;
            lblProgress.Text = "正在本地转换 " + quantName + " …";
            Log("=== 开始转换 " + repo + " → " + quantName + " ===");

            ConvertRequest req = new ConvertRequest();
            req.Repo = repo; req.QuantName = quantName; req.OutType = outType;
            req.OutDir = outDir; req.SourceFiles = new List<string>(localFiles);

            ThreadPool.QueueUserWorkItem(delegate
            {
                ConvertOutcome oc = ConvertService.Run(req, delegate(string s)
                {
                    try { BeginInvoke((MethodInvoker)delegate { Log(s); }); } catch { }
                });
                try
                {
                    Invoke((MethodInvoker)delegate
                    {
                        pb.Style = ProgressBarStyle.Continuous;
                        _busy = false; btnDownload.Enabled = true; btnCancel.Enabled = false;
                        if (oc.Ok)
                        {
                            pb.Value = 100; lblProgress.Text = "转换完成";
                            Log("=== 转换完成：" + oc.GgufPath + " ===");
                            long srcSize = 0;
                            foreach (string f in localFiles) { try { srcSize += new FileInfo(f).Length; } catch { } }
                            DialogResult dr = MessageBox.Show(this,
                                "转换完成：\n" + oc.GgufPath + "\n\n是否删除已下载的原始权重（约 " + GB(srcSize) + "）以释放空间？",
                                "转换完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                            if (dr == DialogResult.Yes)
                            {
                                int n = 0; long freed = 0;
                                foreach (string f in localFiles)
                                {
                                    try { long sz = new FileInfo(f).Length; File.Delete(f); freed += sz; n++; }
                                    catch { }
                                }
                                Log("已删除 " + n + " 个原始文件，释放 " + GB(freed));
                                try { if (Directory.Exists(srcDir) && Directory.GetFiles(srcDir).Length == 0) Directory.Delete(srcDir); } catch { }
                            }
                        }
                        else
                        {
                            pb.Value = 0; lblProgress.Text = "转换失败";
                            Log("=== 转换失败：" + oc.Err + " ===");
                            MessageBox.Show(this, "转换失败：\n" + oc.Err + "\n\n源权重已保留在：\n" + srcDir, "转换失败");
                        }
                    });
                }
                catch { }
            });
        }

        static string SafeSeg(string s)
        {
            StringBuilder sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in s) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string r = sb.ToString().Trim().TrimEnd('.');
            return r.Length == 0 ? "model" : r;
        }

        // ============ v2.2 下载站点 ============
        List<string> ParseSites()
        {
            List<string> ok = SiteStore.Normalize(SiteStore.Parse(txtSites == null ? null : txtSites.Text));
            if (ok.Count == 0) ok.Add(SiteStore.DefaultSite);
            return ok;
        }

        // 归一化后回写输入框、落盘，并同步进 HF 解析链
        List<string> SaveSitesAndApply()
        {
            List<string> ok = ParseSites();
            string body = string.Join("\r\n", ok.ToArray());
            if (txtSites.Text != body) txtSites.Text = body;
            SiteStore.Save(body);
            CatalogService.SetHfBases(ok);
            return ok;
        }

        static string HostOf(string baseUrl)
        {
            try { return new Uri(baseUrl).Host; } catch { return baseUrl; }
        }

        void ShowSiteState(List<SiteSpeed> ranked, string prefix)
        {
            StringBuilder sb = new StringBuilder();
            foreach (SiteSpeed s in ranked)
            {
                if (sb.Length > 0) sb.Append("   |   ");
                sb.Append(HostOf(s.Base)).Append(" ").Append(s.Ok ? s.Human() : "不可用");
            }
            string txt = prefix + sb.ToString();
            if (txt.Length > 190) txt = txt.Substring(0, 190) + " …";
            lblSiteState.Text = txt;
        }

        // 测速结果 → 线路顺序：可用站点按速度优先，用户未覆盖到的内置线路垫后兜底
        static List<string> RankToOrder(List<SiteSpeed> ranked)
        {
            List<string> order = new List<string>();
            foreach (SiteSpeed r in ranked) if (!order.Contains(r.Base)) order.Add(r.Base);
            foreach (string b in DownloadEngine.BuiltinFileMirrors) if (!order.Contains(b)) order.Add(b);
            DownloadEngine.SetMirrors(order);
            return order;
        }

        // ===== v3.0.0：并发分片下载（Range 多连接）=====
        static int SegIndex(int conns)
        {
            int best = 0;
            for (int i = 0; i < SegValues.Length; i++) if (SegValues[i] == conns) { best = i; break; }
            if (SegValues[best] != conns) best = 3;      // 配置里是 5、7 之类的值时，界面落到「4 条连接」
            return best;
        }

        void OnSegChanged()
        {
            int i = cboSeg == null ? -1 : cboSeg.SelectedIndex;
            if (i < 0 || i >= SegValues.Length) i = 3;
            DownloadConfig.SetConnections(SegValues[i]);
            UpdateSegState();
            Log("并发连接数已设为 " + DownloadConfig.Connections
                + (DownloadConfig.SegmentEnabled ? "（Range 分片下载，单文件 ≥ " + DownloadConfig.MinSegMB + " MB 才拆分）" : "（单连接下载）"));
        }

        void UpdateSegState()
        {
            if (lblSegState == null) return;
            if (!DownloadConfig.SegmentEnabled)
                lblSegState.Text = "已关闭分片：退回 v2.4.x 单连接内核；命令行可用 --setconn N 切换。";
            else
                lblSegState.Text = "Range 多连接分片下载：单文件 ≥ " + DownloadConfig.MinSegMB + " MB 才拆分，最多 "
                    + DownloadConfig.Connections + " 条连接；服务器不支持 Range 或文件过小时自动回退单连接。改动立即生效并保存到 "
                    + DownloadConfig.CfgPath;
        }

        // 无模型可下载时用公开的小文件路径做同构测速
        void PreviewSites()
        {
            if (_busy) { Log("任务执行中，无法测速预览"); return; }
            List<string> sites = SaveSitesAndApply();
            string probe = SiteProbe.PreviewProbePath;
            lblSiteState.Text = "正在测速 " + sites.Count + " 个站点…";
            Log("开始测速预览（" + sites.Count + " 个站点，探测文件 " + probe + "）");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<SiteSpeed> ranked = SiteProbe.Rank(sites, probe, 3, delegate(string s)
                {
                    try { BeginInvoke((MethodInvoker)delegate { Log(s); }); } catch { }
                });
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        ShowSiteState(ranked, "测速：");
                        List<string> order = RankToOrder(ranked);
                        Log("测速完成，线路顺序：" + string.Join(" → ", order.ToArray()));
                    });
                }
                catch { }
            });
        }

        // 用本批任务里最大的文件当测速目标，测速结果与真实下载同构
        static string PickProbePath(List<DownloadItem> list)
        {
            string best = null;
            long bestSize = long.MinValue;
            foreach (DownloadItem it in list)
            {
                if (it.ExpectedSize > bestSize) { bestSize = it.ExpectedSize; best = it.UrlPath; }
            }
            if (best == null && list.Count > 0) best = list[0].UrlPath;
            return best;
        }

        void LayoutDetail()
        {
            int w = pnlDetail.ClientSize.Width - 46;
            if (w < 400) w = 400;
            lblTitle.Width = w; lblOrg.Width = w; lblDesc.Width = w; lblRepo.Width = w;
            lblSize.Width = w - 300;
            pb.Width = w; lblProgress.Width = w; lblDepTitle.Width = w; lstDeps.Width = w;
        }

        // ============ 版本切换（v2.3）============
        void OnVersionChanged()
        {
            if (!_verEventOn) return;
            int i = cboVer.SelectedIndex;
            string v = (i >= 0 && i < LocModelProfile.Versions.Length) ? LocModelProfile.Versions[i] : LocModelProfile.DefaultVersion;
            if (v == _ver) return;
            _ver = v;
            _profile = LocModelProfile.Get(v);
            try { VersionStore.Save(v); } catch { }
            _sel = null;
            Log("已切换 LocModel 版本: " + v + " —— " + _profile.Note);
            ReloadAll(true);
        }

        // ============ 数据加载 ============
        void ReloadAll(bool showLog)
        {
            if (_busy) { Log("正在处理中，请稍候…"); return; }
            btnRefresh.Enabled = false;
            lblStatus.Text = "正在从 LocModel 获取目录与模型参数…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder sb = new StringBuilder();
                LocModelProfile prof = LocModelProfile.Get(_ver);
                LocModelInfo info = LocModelProbe.Probe();
                sb.AppendLine("LocModel 程序: " + (info.ExePath == "" ? "未找到" : info.ExePath));
                sb.AppendLine("配置文件: " + (info.SettingsPath == "" ? "未找到" : info.SettingsPath));
                sb.AppendLine("默认模型目录: " + (info.ModelsDir == "" ? "(未知)" : info.ModelsDir));
                sb.AppendLine("使用版本: LocModel " + prof.Version + "（探测: " + (info.Version == "" ? "未知" : info.Version + " / " + info.VersionDetail) + "）");
                sb.AppendLine("版本适配: " + prof.Note);

                // 3.3 的加速线路并入自有 CDN 链（仅内存生效，不改 LocModel 配置）
                if (info.HasAccelLines) CatalogService.SetCatalogBases(info.AccelLines);

                // v2.4.2：3.5 的 hf_token 仅在直连 huggingface.co 时使用，日志只打码显示，绝不回写 settings.json
                if (prof.HasHfToken)
                {
                    CatalogService.SetHfToken(info.HfToken);
                    sb.AppendLine("HF Token: " + (info.HasHfToken
                        ? "已识别 " + CatalogService.MaskToken(info.HfToken) + "（仅 huggingface.co 直连带 Authorization，镜像站不携带）"
                        : "未配置（settings.hf_token 为空，直连 huggingface.co 时按匿名访问）"));
                }
                else CatalogService.SetHfToken("");

                if (prof.HasGithubMirror)
                    sb.AppendLine("GitHub 镜像: " + (string.IsNullOrEmpty(info.GithubMirror) ? "未配置" : info.GithubMirror)
                        + "   ·   加速节点 " + info.AccelNodes.Count + " 个"
                        + (string.IsNullOrEmpty(info.PreferredLine) ? "" : "   ·   首选线路 " + info.PreferredLine));

                string log1;
                List<ModelEntry> text = CatalogService.LoadTextCatalog(info.ExePath, out log1);
                sb.AppendLine(log1.Trim());

                List<ModelEntry> image = EmbeddedImageCatalog.Build(prof.Version);
                string log2;
                CatalogService.SyncImageCatalogFromExe(image, info.ExePath, prof, out log2);
                sb.AppendLine(log2);

                List<ModelEntry> all = new List<ModelEntry>();
                all.AddRange(image);
                all.AddRange(text);

                Invoke((MethodInvoker)delegate
                {
                    _info = info;
                    _profile = prof;
                    _all = all;
                    _mismatchWarned = false;
                    string def = info.ModelsDir;
                    if (!string.IsNullOrEmpty(def)) txtDir.Text = def;
                    ApplyCategory();
                    UpdateDirState();
                    btnRefresh.Enabled = true;
                    if (showLog) Log(sb.ToString().Trim());
                    lblStatus.Text = "共 " + _all.Count + " 个可下载模型（图像/视频 " + image.Count + " · 语言/推理 " + text.Count + "）   ·   LocModel " + prof.Version;
                    lblVerState.Text = "当前按 LocModel " + prof.Version + " 加载"
                        + (info.Version == "" ? "" : "（本机探测: " + info.Version + "，" + info.VersionDetail + "）")
                        + (string.Equals(info.Version, prof.Version, StringComparison.Ordinal) ? "  版本一致" : "  与选择不一致，将按所选版本解析")
                        + (info.HasAccelLines ? "   ·   已识别加速线路 " + info.AccelLines.Count + " 条" : "");
                });
            });
        }

        void SetCategory(string c)
        {
            _category = c;
            ApplyCategory();
        }

        void ApplyCategory()
        {
            _view = new List<ModelEntry>();
            foreach (ModelEntry m in _all)
            {
                if (_category == "全部") _view.Add(m);
                else if (_category == "图像模型" && m.Category == "image") _view.Add(m);
                else if (_category == "视频模型" && m.Category == "video") _view.Add(m);
                else if (_category == "语言 / 推理" && m.Category == "text") _view.Add(m);
            }
            lstModels.BeginUpdate();
            lstModels.Items.Clear();
            foreach (ModelEntry m in _view) lstModels.Items.Add(m.Name);
            lstModels.EndUpdate();
            if (_view.Count > 0) lstModels.SelectedIndex = 0;
            else ClearDetail();
        }

        void DrawModelItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _view.Count) return;
            ModelEntry m = _view[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Color bg = sel ? Color.FromArgb(48, 62, 96) : CBg;
            using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);

            Rectangle r = e.Bounds;
            using (SolidBrush b = new SolidBrush(sel ? Color.White : CText))
                e.Graphics.DrawString(m.Name, new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold), b, r.X + 10, r.Y + 8);
            using (SolidBrush b = new SolidBrush(CDim))
            {
                string sub = m.Org + (string.IsNullOrEmpty(m.Tags) ? "" : "  ·  " + m.Tags);
                if (sub.Length > 34) sub = sub.Substring(0, 34) + "…";
                e.Graphics.DrawString(sub, new Font("Microsoft YaHei UI", 8f), b, r.X + 10, r.Y + 30);
                string q = m.Quants.Count + " 个量化档";
                if (m.SizeMap.Count > 0)
                {
                    double min = double.MaxValue;
                    foreach (KeyValuePair<string, double> kv in m.SizeMap) if (kv.Value > 0 && kv.Value < min) min = kv.Value;
                    if (min != double.MaxValue) q += "  ·  最小 " + min.ToString("0.#") + " GB";
                }
                e.Graphics.DrawString(q, new Font("Microsoft YaHei UI", 8f), b, r.X + 10, r.Y + 45);
            }
            using (Pen p = new Pen(CLine)) e.Graphics.DrawLine(p, r.Left + 8, r.Bottom - 1, r.Right - 8, r.Bottom - 1);
        }

        void OnSelectModel()
        {
            int i = lstModels.SelectedIndex;
            if (i < 0 || i >= _view.Count) return;
            _sel = _view[i];
            lblTitle.Text = _sel.Name;
            lblOrg.Text = _sel.Org + "   ·   " + _sel.Release;
            lblDesc.Text = _sel.HasSpec ? (_sel.SpecText + "\r\n" + _sel.Desc) : _sel.Desc;
            lblRepo.Text = "仓库: " + _sel.Repo;
            cboQuant.Items.Clear();
            foreach (string q in _sel.Quants) cboQuant.Items.Add(q);
            if (cboQuant.Items.Count > 0) cboQuant.SelectedIndex = 0;
            LayoutDetail();
            UpdateDirState();
            OnQuantChanged();
        }

        void ClearDetail()
        {
            _sel = null;
            lblTitle.Text = ""; lblOrg.Text = ""; lblDesc.Text = ""; lblRepo.Text = "";
            cboQuant.Items.Clear(); lstDeps.Items.Clear(); lblSize.Text = "";
        }

        void OnQuantChanged()
        {
            if (_sel == null) return;
            string q = cboQuant.SelectedItem as string;
            if (q == null) return;
            double sz;
            string szText = "大小未知";
            if (_sel.SizeMap.TryGetValue(q, out sz) && sz > 0) szText = sz.ToString("0.#") + " GB（catalog 声明）";
            lblSize.Text = "分辨率/精度: " + q + "   ·   " + szText;

            // v3.1.0：下载前复核 —— 逐条判定目标文件是否已下载完成，已存在的行标绿并加「（已存在）」
            lstDeps.Items.Clear();
            List<DepsRow> rows = new List<DepsRow>();
            string modelsDirNow = txtDir.Text.Trim();
            string mainDirNow = string.IsNullOrEmpty(modelsDirNow) ? "" : _sel.TargetDir(modelsDirNow);
            string commonDirNow = string.IsNullOrEmpty(modelsDirNow) ? "" : Path.Combine(modelsDirNow, "image", "common");
            int existsNow = 0;

            foreach (Quant qq in _sel.BuildQuants())
                if (qq.Name == q)
                    foreach (QuantFile f in qq.Files)
                    {
                        string dest = string.IsNullOrEmpty(mainDirNow) ? "" : Path.Combine(mainDirNow, f.BaseName);
                        bool ex = DepsPrecheck.IsReady(dest, -1);
                        if (ex) existsNow++;
                        rows.Add(new DepsRow("主文件  " + f.Repo + "  →  " + f.BaseName + DepsPrecheck.Suffix(ex), ex, dest));
                    }
            if (_sel.Common.Count > 0)
            {
                rows.Add(new DepsRow("—— 依赖组件（image\\common）——", false, ""));
                foreach (QuantFile f in _sel.Common)
                {
                    string dest = string.IsNullOrEmpty(commonDirNow) ? "" : Path.Combine(commonDirNow, f.BaseName);
                    bool ex = DepsPrecheck.IsReady(dest, -1);
                    if (ex) existsNow++;
                    rows.Add(new DepsRow("依赖    " + f.Repo + "  →  " + f.BaseName + DepsPrecheck.Suffix(ex), ex, dest));
                }
            }
            if (!_sel.IsImageLike && _sel.Vision)
            {
                bool ex = DepsPrecheck.HasMmproj(mainDirNow);
                if (ex) existsNow++;
                rows.Add(new DepsRow("依赖    " + _sel.Repo + "  →  mmproj（自动配对）" + DepsPrecheck.Suffix(ex), ex, mainDirNow));
            }
            foreach (DepsRow r in rows) lstDeps.Items.Add(r);
            lblDepTitle.Text = DepsPrecheck.CountText(lstDeps.Items.Count, existsNow);

            // 异步取仓库真实字节数，显示实际占用
            ModelEntry sel = _sel;
            string quant = q;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double total = 0;
                try
                {
                    Dictionary<string, long> szm = CatalogService.GetFileSizes(sel.Repo);
                    List<QuantFile> fs = sel.QuantsFiles(quant);
                    foreach (QuantFile f in fs)
                    {
                        long b;
                        if (szm.TryGetValue(f.Path, out b)) total += b / 1073741824.0;
                    }
                    foreach (QuantFile f in sel.Common)
                    {
                        Dictionary<string, long> dm = CatalogService.GetFileSizes(f.Repo);
                        long b;
                        if (dm.TryGetValue(f.Path, out b)) total += b / 1073741824.0;
                    }
                }
                catch { }
                if (total <= 0) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    if (_sel != sel) return;
                    lblSize.Text = lblSize.Text + "   ·   实占合计 " + total.ToString("0.#") + " GB（主文件+共享依赖）";
                });
            });
        }

        // ============ 目录 ============
        void BrowseDir()
        {
            FolderBrowserDialog fd = new FolderBrowserDialog();
            fd.Description = "选择模型保存位置（应与 LocModel 的模型目录一致，否则 LocModel 无法识别）";
            if (Directory.Exists(txtDir.Text)) fd.SelectedPath = txtDir.Text;
            if (fd.ShowDialog() == DialogResult.OK)
            {
                txtDir.Text = fd.SelectedPath;
                UpdateDirState();
                CheckMismatch(true);
            }
        }

        void OnDirChanged() { UpdateDirState(); }

        void UpdateDirState()
        {
            string def = _info.ModelsDir;
            string cur = txtDir.Text.Trim();
            if (string.IsNullOrEmpty(def)) { lblDirState.Text = "默认目录未知：请确认 LocModel 已安装并能读取其 settings.json"; lblDirState.ForeColor = Color.FromArgb(230, 170, 90); return; }
            lblDirState.Text = "LocModel 默认目录： " + def + "    " + (SameDir(cur, def) ? "（当前一致，LocModel 可直接识别）" : "（当前为自定义目录，与默认目录不一致）");
            lblDirState.ForeColor = SameDir(cur, def) ? Color.FromArgb(120, 190, 140) : Color.FromArgb(230, 170, 90);
        }

        static bool SameDir(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                a = Path.GetFullPath(a).TrimEnd('\\');
                b = Path.GetFullPath(b).TrimEnd('\\');
            }
            catch { }
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        bool CheckMismatch(bool force)
        {
            if (_mismatchWarned && !force) return false;
            string def = _info.ModelsDir, cur = txtDir.Text.Trim();
            if (string.IsNullOrEmpty(def) || string.IsNullOrEmpty(cur)) return false;
            if (SameDir(cur, def)) return false;
            _mismatchWarned = true;
            MessageBox.Show(this,
                "用户自建目录与默认目录不一致，需用户在locmodel内自行更改默认目录或把下载到自定义目录内容复制到默认目录使用",
                "目录不一致提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }

        void OpenDir()
        {
            string d = txtDir.Text.Trim();
            try
            {
                if (!Directory.Exists(d)) { MessageBox.Show(this, "目录不存在：\n" + d); return; }
                System.Diagnostics.Process.Start("explorer.exe", "\"" + d + "\"");
            }
            catch (Exception ex) { Log("打开目录失败: " + ex.Message); }
        }

        // ============ 下载 ============
        void StartDownload()
        {
            if (_busy) { Log("已有任务在执行"); return; }
            if (_sel == null) { MessageBox.Show(this, "请先选择模型"); return; }
            string q = cboQuant.SelectedItem as string;
            if (q == null) { MessageBox.Show(this, "请选择量化档"); return; }
            string modelsDir = txtDir.Text.Trim();
            if (string.IsNullOrEmpty(modelsDir)) { MessageBox.Show(this, "请先设置模型保存位置"); return; }

            // v2.2：站点设置先落地，清单解析阶段即可用用户站点作为 HF 解析链
            List<string> sites = SaveSitesAndApply();
            Log("下载站点 " + sites.Count + " 个：" + string.Join(" , ", sites.ToArray()));

            if (!SameDir(modelsDir, _info.ModelsDir) && CheckMismatch(false)) { /* 已提示，继续 */ }

            // 组装任务
            List<DownloadItem> items = new List<DownloadItem>();
            string mainDir = _sel.TargetDir(modelsDir);
            string commonDir = Path.Combine(modelsDir, "image", "common");

            List<Quant> qs = _sel.BuildQuants();
            Quant target = null;
            foreach (Quant x in qs) if (x.Name == q) { target = x; break; }
            if (target == null) { Log("量化档解析失败"); return; }

            lblProgress.Text = "正在解析下载清单…";
            btnDownload.Enabled = false; btnCancel.Enabled = true; _busy = true;

            ModelEntry sel = _sel;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DownloadItem> list = new List<DownloadItem>();
                StringBuilder sb = new StringBuilder();
                try
                {
                    double sizeGB;
                    long expected = 0;
                    if (sel.SizeMap.TryGetValue(q, out sizeGB) && sizeGB > 0) expected = (long)(sizeGB * 1024.0 * 1024.0 * 1024.0);

                    // 主文件
                    List<string> files;
                    string err;
                    if (sel.IsImageLike)
                    {
                        files = new List<string>();
                        string e1;
                        List<string> a1 = CatalogService.ResolveQuantFiles(sel.Repo, q, sel.MainFileTemplate, out e1);
                        if (a1.Count == 0) sb.AppendLine("主文件定位失败: " + e1);
                        for (int k = 0; k < a1.Count; k++) if (!files.Contains(a1[k])) files.Add(a1[k]);
                        if (!string.IsNullOrEmpty(sel.MainFile2Template))
                        {
                            string e2;
                            List<string> a2 = CatalogService.ResolveQuantFiles(sel.Repo, q, sel.MainFile2Template, out e2);
                            if (a2.Count == 0) sb.AppendLine("第二主文件定位失败: " + e2);
                            for (int k = 0; k < a2.Count; k++) if (!files.Contains(a2[k])) files.Add(a2[k]);
                        }
                    }
                    else
                    {
                        files = CatalogService.ResolveQuantFiles(sel.Repo, q, sel.MainFileTemplate, out err);
                        if (files.Count == 0) sb.AppendLine("主文件定位失败: " + err);
                    }
                    foreach (string f in files)
                    {
                        DownloadItem it = new DownloadItem();
                        it.Repo = sel.Repo; it.Path = f;
                        it.DestPath = Path.Combine(mainDir, BaseName(f));
                        it.ExpectedSize = (files.Count == 1) ? expected : 0;
                        it.Label = sel.Name + " / " + q + " / " + BaseName(f);
                        list.Add(it);
                    }

                    // 语言/多模态模型的 mmproj
                    if (!sel.IsImageLike && sel.Vision)
                    {
                        string mm = CatalogService.PickMmproj(sel.Repo);
                        if (mm != null)
                        {
                            DownloadItem it = new DownloadItem();
                            it.Repo = sel.Repo; it.Path = mm;
                            it.DestPath = Path.Combine(mainDir, BaseName(mm));
                            it.ExpectedSize = -1;
                            it.Label = sel.Name + " / mmproj / " + BaseName(mm);
                            list.Add(it);
                        }
                    }

                    // 共享依赖
                    foreach (QuantFile f in sel.Common)
                    {
                        DownloadItem it = new DownloadItem();
                        it.Repo = f.Repo; it.Path = f.Path;
                        it.DestPath = Path.Combine(commonDir, f.BaseName);
                        it.ExpectedSize = -1;
                        it.Label = "依赖 / " + f.BaseName;
                        list.Add(it);
                    }

                    // 用 HF tree 真实字节数补齐缺失容量（图像/视频模型无 catalog 尺寸）
                    foreach (DownloadItem it in list)
                    {
                        Dictionary<string, long> szm = CatalogService.GetFileSizes(it.Repo);
                        long real;
                        if (szm.TryGetValue(it.Path, out real) && real > 0) it.ExpectedSize = real;
                        if (it.ExpectedSize <= 0) it.ExpectedSize = -1;
                    }

                    // v2.4.2：识别 LocModel 主程序就地留下的 <file>.part 半成品（仅提示，不接管、不动文件）
                    if (_profile.ScanInlinePart)
                        foreach (string h in HalfPartScan.Scan(modelsDir, list)) sb.AppendLine(h);

                    // 磁盘空间
                    try
                    {
                        long need = 0; foreach (DownloadItem it in list) if (it.ExpectedSize > 0) need += it.ExpectedSize;
                        string root = Path.GetPathRoot(Path.GetFullPath(modelsDir));
                        DriveInfo di = new DriveInfo(root);
                        if (need > 0 && di.AvailableFreeSpace < need)
                            sb.AppendLine("警告: 目标盘剩余 " + GB(di.AvailableFreeSpace) + "，小于所需 " + GB(need));
                        else if (need > 0)
                            sb.AppendLine("目标盘剩余 " + GB(di.AvailableFreeSpace) + "，预计占用 " + GB(need));
                    }
                    catch { }
                }
                catch (Exception ex) { sb.AppendLine("清单解析异常: " + ex.Message); }

                Invoke((MethodInvoker)delegate
                {
                    Log(sb.ToString().Trim());
                    if (list.Count == 0)
                    {
                        _busy = false; btnDownload.Enabled = true; btnCancel.Enabled = false;
                        lblProgress.Text = "未能生成下载清单";
                        MessageBox.Show(this, "未能生成下载清单，请查看日志。\n可能是网络无法访问 HF 仓库接口。", "解析失败");
                        return;
                    }
                    lblDepsRefresh(list);
                    RunDownload(list, modelsDir);
                });
            });
        }

        // v3.1.0：listbox 自绘 —— 已复核存在的文件用绿色，其余正常色
        [DllImport("user32.dll")]
        static extern int GetScrollPos(IntPtr hWnd, int nBar);
        // v3.1.0：自检用 —— DrawToBitmap 抓不到自绘 ListBox，改用 PrintWindow(PW_RENDERFULLCONTENT) 抓真窗口像素
        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        // v3.1.0：自检用 —— 把整窗真实像素存 PNG，供核对绿色标注渲染
        public void SaveWindowPng(string path)
        {
            try
            {
                using (Bitmap b = new Bitmap(Width, Height))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        IntPtr dc = g.GetHdc();
                        PrintWindow(Handle, dc, 2);
                        g.ReleaseHdc(dc);
                    }
                    b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception ex) { Console.WriteLine("printwindow_fail=" + ex.Message); }
        }

        void LstDeps_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= lstDeps.Items.Count) return;

            DepsRow row = lstDeps.Items[e.Index] as DepsRow;
            string text = row != null ? row.Text : (lstDeps.Items[e.Index] == null ? "" : lstDeps.Items[e.Index].ToString());
            bool exists = row != null && row.Exists;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (SolidBrush back = new SolidBrush(sel ? CPanel2 : CPanel))
                e.Graphics.FillRectangle(back, e.Bounds);

            // 横向滚动偏移：OwnerDraw 下需要自己扣掉滚动量，否则长条目滚动后文字不跟随
            int pos = 0;
            try { pos = GetScrollPos(lstDeps.Handle, 0 /*SB_HORZ*/); } catch { }

            Color c = exists ? CExists : (sel ? CText : CText);
            using (SolidBrush b = new SolidBrush(c))
                e.Graphics.DrawString(text, e.Font, b, e.Bounds.X - pos, e.Bounds.Y + 2);

            if (sel) e.DrawFocusRectangle();
        }

        void lblDepsRefresh(List<DownloadItem> list)
        {
            // v3.1.0：清单落地后按真实目标路径再复核一次（此时 ExpectedSize 已补齐，判定最准）
            lstDeps.Items.Clear();
            int existsNow = 0;
            foreach (DownloadItem it in list)
            {
                bool ex = DepsPrecheck.IsReady(it.DestPath, it.ExpectedSize);
                if (ex) existsNow++;
                string line = (it.Path.StartsWith("mmproj") ? "mmproj  " : "") + it.Repo + "  →  " + it.DestPath;
                lstDeps.Items.Add(new DepsRow(line + DepsPrecheck.Suffix(ex), ex, it.DestPath));
            }
            lblDepTitle.Text = DepsPrecheck.CountText(list.Count, existsNow);
            Log("下载清单 " + list.Count + " 项，复核已存在 " + existsNow + " 项（绿色标注）");
        }

        void RunDownload(List<DownloadItem> list, string modelsDir)
        {
            string tempRoot = _profile.TempRoot(modelsDir);
            DownloadEngine eng = new DownloadEngine();
            _eng = eng;
            eng.Log += delegate(string s) { BeginInvoke((MethodInvoker)delegate { Log(s); }); };
            eng.Progress += delegate(long done, long total, double speed)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (total > 0)
                    {
                        int pct = (int)(done * 100 / total);
                        pb.Value = pct > 100 ? 100 : pct;
                        lblProgress.Text = GB(done) + " / " + GB(total) + "   " + SpeedText(speed) + "   " + pct + "%";
                    }
                    else lblProgress.Text = GB(done) + "   已下载   " + SpeedText(speed);
                });
            };

            btnCancel.Enabled = true;
            // 优化13：开始下载时进度归零，避免上次的 100% 残留到本次
            pb.Value = 0;
            List<string> siteList = ParseSites();
            string probePath = PickProbePath(list);
            lblProgress.Text = "正在测速 " + siteList.Count + " 个站点…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try
                {
                    // v2.2：下载前对界面里的站点自动测速，速度最快的线路优先
                    Log("站点测速开始：共 " + siteList.Count + " 个，探测文件 " + probePath);
                    List<SiteSpeed> ranked = SiteProbe.Rank(siteList, probePath, 3, delegate(string s)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { Log(s); }); } catch { }
                    });
                    List<string> order = RankToOrder(ranked);
                    Log("本次线路顺序：" + string.Join(" → ", order.ToArray()));
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            ShowSiteState(ranked, "测速：");
                            lblProgress.Text = "开始下载…";
                        });
                    }
                    catch { }
                    ok = eng.RunAll(list, tempRoot);
                }
                catch (Exception ex) { BeginInvoke((MethodInvoker)delegate { Log("异常: " + ex.Message); }); }
                Invoke((MethodInvoker)delegate
                {
                    _busy = false; btnDownload.Enabled = true; btnCancel.Enabled = false;
                    // v2.4.1：下载并转换流程由回调接管后续（源权重下载完成后自动进入转换）
                    System.Action<bool> cont = _onFinished;
                    _onFinished = null;
                    if (ok)
                    {
                        pb.Value = 100;
                        lblProgress.Text = "下载完成";
                        Log("=== 全部完成 ===");
                        try { if (Directory.Exists(tempRoot) && Directory.GetFiles(tempRoot).Length == 0) Directory.Delete(tempRoot); } catch { }
                        if (cont != null) { cont(true); }
                        else MessageBox.Show(this, "下载完成。\n\n文件已保存到：\n" + modelsDir + "\n\n在 LocModel 中刷新模型列表即可看到。", "完成");
                    }
                    else if (eng.Cancelled)
                    {
                        lblProgress.Text = "已取消";
                        Log("=== 已取消（已下载部分保留，下次可续传） ===");
                    }
                    else
                    {
                        // 缺陷4：单文件失败不再中断整批，这里提示部分失败并保留现场
                        lblProgress.Text = "部分文件失败";
                        Log("=== 任务结束（存在失败文件，详见上方日志） ===");
                        if (cont != null) { cont(false); }
                        else MessageBox.Show(this, "部分文件下载失败，其余文件已完成并可直接使用。\n\n失败明细见日志；再次点击下载只会补下失败或未完成的文件。", "部分失败");
                    }
                });
            });
        }

        void RequestCancel()
        {
            Log("正在取消…（已下载部分保留，下次可续传）");
            btnCancel.Enabled = false;
            DownloadEngine e = _eng;
            if (e != null) e.Cancelled = true;
        }

        static string GB(long bytes)
        {
            double g = bytes / 1024.0 / 1024.0 / 1024.0;
            if (g >= 1) return g.ToString("0.00") + " GB";
            double m = bytes / 1024.0 / 1024.0;
            return m.ToString("0.0") + " MB";
        }

        static string SpeedText(double bps)
        {
            if (bps <= 0) return "--";
            double m = bps / 1024.0 / 1024.0;
            if (m >= 1) return m.ToString("0.0") + " MB/s";
            return (bps / 1024.0).ToString("0") + " KB/s";
        }

        static string BaseName(string p) { int i = p.LastIndexOf('/'); return i < 0 ? p : p.Substring(i + 1); }

        void Log(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
        }

        // 自检：同步加载数据并转储界面状态，供自动化验证使用
        public void SelfTest()
        {
            StringBuilder sb = new StringBuilder();
            LocModelInfo info = LocModelProbe.Probe();
            _info = info;
            if (_profile.HasHfToken) CatalogService.SetHfToken(info.HfToken); else CatalogService.SetHfToken("");
            string log1;
            List<ModelEntry> text = CatalogService.LoadTextCatalog(info.ExePath, out log1);
            List<ModelEntry> image = EmbeddedImageCatalog.Build(_profile.Version);
            string log2;
            CatalogService.SyncImageCatalogFromExe(image, info.ExePath, _profile, out log2);
            _all = new List<ModelEntry>();
            _all.AddRange(image);
            _all.AddRange(text);
            txtDir.Text = info.ModelsDir;
            SetCategory("全部");
            sb.AppendLine("ModelsDir=" + info.ModelsDir);
            sb.AppendLine("AllCount=" + _all.Count + " image=" + image.Count + " text=" + text.Count);
            sb.AppendLine("ListBoxCount=" + lstModels.Items.Count);
            sb.AppendLine("StatusText=" + lblStatus.Text);
            sb.AppendLine("DirState=" + lblDirState.Text);
            sb.AppendLine("Sites=" + string.Join(" | ", ParseSites().ToArray()));
            sb.AppendLine("SiteCount=" + ParseSites().Count + " state=" + lblSiteState.Text);
            sb.AppendLine("SiteBox=" + txtSites.Bounds.Width + "x" + txtSites.Bounds.Height + " visible=" + txtSites.Visible);
            sb.AppendLine("Mirrors=" + string.Join(",", DownloadEngine.MirrorsSnapshot()));

            // ===== v3.0.0 自检：并发分片下载 =====
            sb.AppendLine("-- v3.0.0 并发分片下载 --");
            sb.AppendLine("BuiltinMirrors=" + string.Join(",", DownloadEngine.BuiltinFileMirrors)
                + " HasM1M2=" + (Array.IndexOf(DownloadEngine.BuiltinFileMirrors, "https://m1.locmodel.com") >= 0
                              || Array.IndexOf(DownloadEngine.BuiltinFileMirrors, "https://m2.locmodel.com") >= 0));
            sb.AppendLine("SegConnections=" + DownloadConfig.Connections + " MinSegMB=" + DownloadConfig.MinSegMB
                + " SegmentEnabled=" + DownloadConfig.SegmentEnabled + " Cfg=" + DownloadConfig.CfgPath
                + " CfgExists=" + File.Exists(DownloadConfig.CfgPath));
            sb.AppendLine("SegCombo=" + (cboSeg == null ? "null" : cboSeg.SelectedIndex + "/" + cboSeg.Text)
                + " SegState=" + (lblSegState == null ? "null" : lblSegState.Bounds.Width + "x" + lblSegState.Bounds.Height));
            foreach (long tot in new long[] { 3L * 1024 * 1024 * 1024, 5L * 1024 * 1024 * 1024 + 7, 1L << 40 })
            {
                foreach (int cn in new int[] { 2, 4, 8 })
                {
                    long[] st, ln;
                    SegmentedFetcher.Plan(tot, cn, out st, out ln);
                    bool ok = st[0] == 0;
                    long acc = 0;
                    for (int i = 0; i < st.Length; i++)
                    {
                        if (st[i] != acc || ln[i] <= 0) ok = false;
                        acc += ln[i];
                    }
                    if (acc != tot) ok = false;
                    sb.AppendLine(string.Format("  Plan({0}B,{1}片) ok={2} 每片≈{3:0.0}MB sum={4}",
                        tot, cn, ok, (double)tot / cn / 1048576.0, acc));
                }
            }
            // 半成品判读：分片状态文件存在 → 视为分片；不存在 → 单连接
            string tmpSeg = Path.Combine(Path.GetTempPath(), "locmodel_segcheck_" + Guid.NewGuid().ToString("N") + ".part");
            sb.AppendLine("SegDetect.None=" + SegmentedFetcher.IsSegmentedPart(tmpSeg));
            try
            {
                File.WriteAllText(tmpSeg, "0123456789");
                File.WriteAllText(tmpSeg + ".seg", "size=100\r\nseg0=10\r\nseg1=30\r\n");
                sb.AppendLine("SegDetect.HasSeg=" + SegmentedFetcher.IsSegmentedPart(tmpSeg)
                    + " Progress=" + SegmentedFetcher.ProgressOf(tmpSeg));
            }
            finally
            {
                try { if (File.Exists(tmpSeg)) File.Delete(tmpSeg); } catch { }
                try { if (File.Exists(tmpSeg + ".seg")) File.Delete(tmpSeg + ".seg"); } catch { }
            }
            sb.AppendLine("HfBases=" + string.Join(",", CatalogService.HfBasesSnapshot()));
            sb.AppendLine("QuantCount=" + cboQuant.Items.Count + " title=" + lblTitle.Text);
            sb.AppendLine("DepsShown=" + lstDeps.Items.Count + " size=" + lblSize.Text);
            sb.AppendLine("FormSize=" + Width + "x" + Height + " ListBox=" + lstModels.ClientSize.Width + "x" + lstModels.ClientSize.Height + " visible=" + lstModels.Visible);
            sb.AppendLine("DetailPanel=" + pnlDetail.ClientSize.Width + "x" + pnlDetail.ClientSize.Height + " DepsList=" + lstDeps.ClientSize.Width + "x" + lstDeps.ClientSize.Height);
            sb.AppendLine("-- 前 6 条列表项 --");
            for (int i = 0; i < lstModels.Items.Count && i < 6; i++)
                sb.AppendLine("  " + i + ": " + _view[i].Name + " | " + _view[i].Category + " | 量化 " + _view[i].Quants.Count);
            sb.AppendLine("-- 分类切换 --");
            foreach (string c in new string[] { "图像模型", "视频模型", "语言 / 推理" })
            {
                SetCategory(c);
                sb.AppendLine("  " + c + " -> " + lstModels.Items.Count + " 项");
            }
            SetCategory("全部");

            // ===== v2.4.2 自检：版本差异表 / 规格字段 / token 打码 / 就地半成品 =====
            sb.AppendLine("-- v2.4.2 版本适配 --");
            foreach (string v in LocModelProfile.Versions)
            {
                LocModelProfile p = LocModelProfile.Get(v);
                List<ModelEntry> im = EmbeddedImageCatalog.Build(v);
                int withSpec = 0;
                foreach (ModelEntry me in im) if (me.HasSpec) withSpec++;
                sb.AppendLine("  " + v + ": label=" + LocModelProfile.LabelOf(v)
                    + " temp=" + p.TempSubDir + " lora=" + p.HasLoraDir + " comfy=" + p.RecognizeComfyDir
                    + " token=" + p.HasHfToken + " accelNodes=" + p.HasAccelNodes + " ghMirror=" + p.HasGithubMirror
                    + " catalogSpec=" + p.CatalogHasSpec + " imgAuthority=" + p.ImageAuthority
                    + " scanPart=" + p.ScanInlinePart + " images=" + im.Count + " imgSpec=" + withSpec);
            }
            sb.AppendLine("DefaultVersion=" + LocModelProfile.DefaultVersion
                + " Normalize(3.6.0)=" + LocModelProfile.Normalize("3.6.0")
                + " Normalize(v3.6)=" + LocModelProfile.Normalize("v3.6")
                + " Normalize(3.6.0-beta)=" + LocModelProfile.Normalize("3.6.0-beta")
                + " Normalize(3.5.0)=" + LocModelProfile.Normalize("3.5.0")
                + " Normalize(v3.5)=" + LocModelProfile.Normalize("v3.5")
                + " Normalize(3.5.0-beta)=" + LocModelProfile.Normalize("3.5.0-beta")
                + " Normalize(9.9)=" + LocModelProfile.Normalize("9.9"));

            // ===== v3.1.0 自检：下载前复核（已存在判定 / 绿色标注 / 「（已存在）」后缀）=====
            sb.AppendLine("-- v3.1.0 下载前复核 --");
            {
                string dir = Path.Combine(Path.GetTempPath(), "locmodel_precheck_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    string pDone = Path.Combine(dir, "done.gguf");
                    string pPart = Path.Combine(dir, "half.gguf");
                    string pEmpty = Path.Combine(dir, "empty.gguf");
                    string pShort = Path.Combine(dir, "short.gguf");
                    string pMissing = Path.Combine(dir, "missing.gguf");

                    File.WriteAllBytes(pDone, new byte[2048]);              // 完整成品
                    File.WriteAllBytes(pPart, new byte[2048]);              // 成品 + 就地 .part 残留 → 未下完
                    File.WriteAllText(pPart + ".part", "stub");
                    File.WriteAllBytes(pEmpty, new byte[0]);                // 0 字节 → 未完成
                    File.WriteAllBytes(pShort, new byte[1024]);             // 小于声明大小 → 未完成
                    File.WriteAllBytes(Path.Combine(dir, "mmproj-x-F16.gguf"), new byte[16]); // mmproj 已存在
                    File.WriteAllText(Path.Combine(dir, "mmproj-y-F16.gguf.part"), "stub");   // mmproj 半成品不计数

                    sb.AppendLine("  IsReady.done(未知大小)=" + DepsPrecheck.IsReady(pDone, -1)
                        + " done(声明2048)=" + DepsPrecheck.IsReady(pDone, 2048)
                        + " half(有.part)=" + DepsPrecheck.IsReady(pPart, -1)
                        + " empty=" + DepsPrecheck.IsReady(pEmpty, -1)
                        + " short(声明4096)=" + DepsPrecheck.IsReady(pShort, 4096)
                        + " missing=" + DepsPrecheck.IsReady(pMissing, -1)
                        + " null=" + DepsPrecheck.IsReady(null, -1));
                    sb.AppendLine("  HasMmproj.dir=" + DepsPrecheck.HasMmproj(dir)
                        + " 空目录=" + DepsPrecheck.HasMmproj(Path.Combine(dir, "nope")));
                    sb.AppendLine("  Suffix(true)=" + DepsPrecheck.Suffix(true)
                        + " Suffix(false)=[" + DepsPrecheck.Suffix(false) + "]");
                    sb.AppendLine("  CountText(3,1)=" + DepsPrecheck.CountText(3, 1));
                    DepsRow dr = new DepsRow("主文件  x  →  y" + DepsPrecheck.Suffix(true), true, pDone);
                    sb.AppendLine("  RowText=" + dr.Text + " Exists=" + dr.Exists
                        + " DrawColor=" + (dr.Exists ? "CExists(Green)" : "CText"));
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            // v3.1.0：真实目录抽样 —— 用本机 models 目录实际情况核对绿色标注是否按复核结果生效
            sb.AppendLine("  -- 真实目录抽样（" + txtDir.Text + "）--");
            foreach (string id in new string[] { "z-image-turbo", "qwen-image-2.1" })
            {
                int idx = -1;
                for (int i = 0; i < _view.Count; i++) if (_view[i].Id == id) { idx = i; break; }
                if (idx < 0) { sb.AppendLine("    " + id + "：未在列表中"); continue; }
                lstModels.SelectedIndex = idx;
                Application.DoEvents();
                sb.AppendLine("    " + id + " quant=" + (cboQuant.SelectedItem == null ? "" : cboQuant.SelectedItem.ToString())
                    + " 标题=" + lblDepTitle.Text);
                int n = 0, ex2 = 0;
                foreach (object o in lstDeps.Items)
                {
                    DepsRow r = o as DepsRow;
                    if (r == null) continue;
                    n++;
                    if (r.Exists) ex2++;
                    if (n <= 6) sb.AppendLine("      [" + (r.Exists ? "绿色" : "常规") + "] " + r.Text);
                }
                sb.AppendLine("      合计 " + n + " 项，绿色 " + ex2 + " 项");
            }
            // v3.1.0：真实绘制路径验证 —— 直接调用自绘例程画到离屏位图，统计绿色像素，确认已存在行确实被画成绿色
            {
                List<object> keep = new List<object>();
                foreach (object o in lstDeps.Items) keep.Add(o);
                lstDeps.Items.Clear();
                lstDeps.Items.Add(new DepsRow("主文件  x  →  y" + DepsPrecheck.Suffix(true), true, null));
                lstDeps.Items.Add(new DepsRow("依赖    x  →  z", false, null));
                int green1 = 0, green2 = 0;
                using (Bitmap b = new Bitmap(640, 44))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.FromArgb(255, 0, 0, 0));
                        LstDeps_DrawItem(lstDeps, new DrawItemEventArgs(g, lstDeps.Font,
                            new Rectangle(0, 0, 640, 22), 0, DrawItemState.None));
                        LstDeps_DrawItem(lstDeps, new DrawItemEventArgs(g, lstDeps.Font,
                            new Rectangle(0, 22, 640, 22), 1, DrawItemState.None));
                    }
                    for (int y = 0; y < 44; y++)
                        for (int x = 0; x < 640; x++)
                        {
                            Color c = b.GetPixel(x, y);
                            bool isGreen = c.G > 150 && c.G > c.R + 30 && c.G > c.B + 20;
                            if (!isGreen) continue;
                            if (y < 22) green1++; else green2++;
                        }
                }
                sb.AppendLine("  绘制验证：已存在行绿色像素=" + green1 + "（>20 视为标绿成功），未存在行绿色像素=" + green2);
                lstDeps.Items.Clear();
                foreach (object o in keep) lstDeps.Items.Add(o);
            }
            {
                string det;
                string dv = LocModelProfile.Detect(info.ExePath, info.HasAccelLines, info.HasV35Settings, out det);
                sb.AppendLine("Detect=" + dv + " (" + det + ")");
            }
            int specAll = 0;
            foreach (ModelEntry me in _all) if (me.HasSpec) specAll++;
            sb.AppendLine("SpecFields=" + specAll + "/" + _all.Count);
            int shown = 0;
            foreach (ModelEntry me in _all)
            {
                if (!me.HasSpec) continue;
                if (shown++ >= 3) break;
                sb.AppendLine("  SpecSample=" + me.Id + " -> " + me.SpecText);
            }
            sb.AppendLine("MaskToken=" + CatalogService.MaskToken("hf_abcdefghijklmnc81")
                + " short=" + CatalogService.MaskToken("abc")
                + " empty=" + CatalogService.MaskToken(""));
            sb.AppendLine("-- 图像清单核对日志 (" + _profile.Version + ") --");
            foreach (string ln in log2.Replace("\r", "").Split('\n'))
                if (ln.Trim().Length > 0) sb.AppendLine("  " + ln.Trim());
            {
                // 用假 token 断言 Authorization 只在直连 huggingface.co 时携带
                string real = info.HfToken;
                CatalogService.SetHfToken("hf_FAKEfakeFAKEfakeFAKEc81");
                sb.AppendLine("AuthHF=" + CatalogService.DebugAuthHeader("https://huggingface.co/api/models/leejet/Qwen-Image-2.1-GGUF")
                    + " AuthMirror=" + CatalogService.DebugAuthHeader("https://hf-mirror.com/api/models/leejet/Qwen-Image-2.1-GGUF")
                    + " AuthOther=" + CatalogService.DebugAuthHeader("https://example.com/x"));
                CatalogService.SetHfToken(real);
            }
            {
                List<string> parts = HalfPartScan.ScanTree(info.ModelsDir, 2, 20);
                sb.AppendLine("InlineParts(scanTree)=" + parts.Count);
                foreach (string ps in parts) sb.AppendLine("  part" + ps);
            }

            // ===== v2.4.2 自检：详情面板布局与规格行（逐个模型点一遍，检查重叠/选中失败）=====
            sb.AppendLine("-- 详情面板布局 --");
            {
                int selOk = 0, selFail = 0, specRows = 0, overlap = 0;
                string lastDesc = "", lastRepo = "", lastSize = "";
                for (int i = 0; i < lstModels.Items.Count; i++)
                {
                    try
                    {
                        lstModels.SelectedIndex = i;
                        Application.DoEvents();
                        if (_sel == null) { selFail++; sb.AppendLine("  SEL-FAIL idx=" + i + " item=" + lstModels.Items[i]); continue; }
                        selOk++;
                        if (lblDesc.Text != null && lblDesc.Text.IndexOf("模型规格") >= 0) specRows++;
                        bool bad = lblDesc.Bottom > lblRepo.Top || lblRepo.Bottom > cboQuant.Top
                            || cboQuant.Bottom > btnDownload.Top || btnDownload.Bottom > pb.Top
                            || pb.Bottom > lblDepTitle.Top || lblDepTitle.Bottom > lstDeps.Top;
                        if (bad)
                        {
                            overlap++;
                            sb.AppendLine("  OVERLAP idx=" + i + " desc=" + lblDesc.Bounds + " repo=" + lblRepo.Bounds
                                + " quant=" + cboQuant.Bounds + " btn=" + btnDownload.Bounds + " pb=" + pb.Bounds
                                + " dep=" + lblDepTitle.Bounds + " list=" + lstDeps.Bounds);
                        }
                        lastDesc = (lblDesc.Text ?? "").Replace("\r\n", " | ");
                        lastRepo = lblRepo.Text; lastSize = lblSize.Text;
                    }
                    catch (Exception ex)
                    {
                        selFail++;
                        sb.AppendLine("  SEL-EX idx=" + i + " item=" + lstModels.Items[i] + " -> " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                sb.AppendLine("  selOk=" + selOk + " selFail=" + selFail + " specRows=" + specRows + " overlap=" + overlap);
                sb.AppendLine("  lastDesc=" + lastDesc);
                sb.AppendLine("  lastRepo=" + lastRepo + " lastSize=" + lastSize);
            }

            txtDir.Text = @"E:\TestModels";
            UpdateDirState();
            sb.AppendLine("MismatchState=" + lblDirState.Text);
            sb.AppendLine("MismatchSameDir=" + SameDir(txtDir.Text, _info.ModelsDir));
            txtDir.Text = info.ModelsDir;
            Console.WriteLine(sb.ToString());
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // v3.0.0：--setconn <N> 直接改写并发连接数（无界面，便于脚本验收）
            if (args.Length > 1 && args[0] == "--setconn")
            {
                int n;
                if (!int.TryParse(args[1], out n)) { Console.WriteLine("ERR bad N"); return; }
                DownloadConfig.Load();
                DownloadConfig.SetConnections(n);
                Console.WriteLine("Connections=" + DownloadConfig.Connections + " MinSegMB=" + DownloadConfig.MinSegMB
                                  + " Cfg=" + DownloadConfig.CfgPath);
                return;
            }

            // v3.0.0：--segtest <url> <dest> [conns] [minMB] 分片内核端到端验收（不经过界面、不碰 LocModel 目录）
            if (args.Length > 2 && args[0] == "--segtest")
            {
                DownloadConfig.Load();
                int conns = DownloadConfig.Connections;
                if (args.Length > 3) int.TryParse(args[3], out conns);
                int minmb = DownloadConfig.MinSegMB;
                if (args.Length > 4) int.TryParse(args[4], out minmb);
                if (conns < 2) conns = 4;
                string part = args[2] + ".part";
                SegmentedFetcher seg = new SegmentedFetcher();
                seg.Log += delegate(string s) { Console.WriteLine("SEG " + s); };
                DateTime t0 = DateTime.UtcNow;
                double peak = 0;
                SegResult r = seg.Run(args[1], part, -1, conns, (long)minmb * 1024L * 1024L,
                    delegate(long done, double spd) { if (spd > peak) peak = spd; },
                    delegate { return false; });
                double sec = (DateTime.UtcNow - t0).TotalSeconds;
                long len = -1;
                try { if (File.Exists(part)) len = new FileInfo(part).Length; } catch { }
                Console.WriteLine(string.Format("result={0} conns={1} bytes={2} sec={3:0.0} avgMBps={4:0.00} peakMBps={5:0.00}",
                    r, conns, len, sec, sec > 0 ? len / 1048576.0 / sec : 0, peak / 1048576.0));
                if (r == SegResult.Done)
                {
                    try { if (File.Exists(args[2])) File.Delete(args[2]); File.Move(part, args[2]); } catch (Exception ex) { Console.WriteLine("ERR move " + ex.Message); }
                    Console.WriteLine("ok=1");
                }
                else Console.WriteLine("ok=0");
                return;
            }

            if (args.Length > 0 && args[0] == "--selftest")
            {
                MainForm f = new MainForm();
                f.SelfTest();
                return;
            }
            if (args.Length > 1 && args[0] == "--uicheck")
            {
                MainForm f = new MainForm();
                f.Show();
                Application.DoEvents();
                // v2.4.2：--uicheck <out.png> [模型序号] 可指定选中项，便于核对详情面板的规格行布局
                if (args.Length > 2)
                {
                    int idx;
                    if (int.TryParse(args[2], out idx))
                    {
                        for (int k = 0; k < 30; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
                        f.PreviewSelect(idx);
                        for (int k = 0; k < 15; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
                    }
                }
                System.Threading.Thread.Sleep(800);
                Application.DoEvents();
                Bitmap bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                // v3.1.0：DrawToBitmap 抓不到 ListBox 内容，额外用 PrintWindow 抓一份真像素，便于核对绿色标注
                f.SaveWindowPng(Path.ChangeExtension(args[1], ".pw.png"));
                f.Close();
                return;
            }
            if (args.Length > 2 && args[0] == "--hubcheck")
            {
                MainForm f = new MainForm();
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(600);
                Application.DoEvents();
                HubBrowserForm h = new HubBrowserForm(f);
                h.Show();
                Application.DoEvents();
                h.UiCheck(args[1], args[2]);
                f.Close();
                return;
            }
            if (args.Length > 3 && args[0] == "--hubget")
            {
                // --hubget <repo> <file> <destDir>  ：走 DownloadFromHub 的端到端链路验证
                MainForm f = new MainForm();
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(500);
                Application.DoEvents();
                f.SetModelsDir(args[3]);
                Application.DoEvents();
                bool ok = f.DownloadFromHub(args[1], "E2E", new string[] { args[2] }, null);
                Console.WriteLine("start=" + ok);
                for (int i = 0; i < 600 && f.IsDownloading; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(200); }
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                string dir = args[3];
                if (Directory.Exists(dir))
                    foreach (string sub in Directory.GetDirectories(dir))
                        foreach (string fp in Directory.GetFiles(sub, "*", SearchOption.AllDirectories))
                            Console.WriteLine("FILE " + fp + " " + new FileInfo(fp).Length);
                f.Close();
                return;
            }
            if (args.Length > 3 && args[0] == "--conv")
            {
                // --conv <repo> <quant> <destRoot>：源权重下载 + 本地转换的端到端验证（控制台输出）
                MainForm f = new MainForm();
                f.Show(); Application.DoEvents();
                string repo = args[1], quant = args[2], root = args[3];
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                string err;
                List<QuantGroup> qs = RepoQuants.Parse(repo, out err);
                Console.WriteLine("quants=" + qs.Count + " err=" + (err == null ? "" : err));
                QuantGroup pick = null;
                foreach (QuantGroup g in qs)
                {
                    Console.WriteLine(string.Format("  kind={0} name={1} convert={2} files={3} bytes={4} est={5} outtype={6}",
                        g.Kind, g.Name, g.Convert, g.Files.Count, g.Bytes, g.EstBytes, g.OutType));
                    if (g.Convert && g.Name == quant) pick = g;
                }
                if (pick == null) { Console.WriteLine("NO_CONVERT_PLAN"); f.Close(); return; }
                string srcDir = Path.Combine(root, "_source");
                if (!Directory.Exists(srcDir)) Directory.CreateDirectory(srcDir);
                List<DownloadItem> list = new List<DownloadItem>();
                List<string> local = new List<string>();
                for (int i = 0; i < pick.Files.Count; i++)
                {
                    long sz = (pick.Sizes != null && i < pick.Sizes.Count) ? pick.Sizes[i] : -1;
                    string bn = pick.Files[i]; int sl2 = bn.LastIndexOf('/'); if (sl2 >= 0) bn = bn.Substring(sl2 + 1);
                    string dp = Path.Combine(srcDir, bn);
                    if (sz > 0 && sz < 512)
                    {
                        string terr;
                        if (CatalogService.FetchSmallFile(repo, pick.Files[i], dp, out terr)) { local.Add(dp); Console.WriteLine("SMALL ok " + bn); }
                        else Console.WriteLine("SMALL fail " + bn + " " + terr);
                        continue;
                    }
                    DownloadItem it = new DownloadItem();
                    it.Repo = repo; it.Path = pick.Files[i];
                    it.DestPath = dp;
                    it.ExpectedSize = sz;
                    it.Label = pick.Files[i];
                    list.Add(it);
                }
                DownloadEngine eng = new DownloadEngine();
                eng.Log += delegate(string s) { Console.WriteLine("DL " + s); };
                Console.WriteLine("download start, files=" + list.Count);
                bool ok = true;
                if (list.Count > 0) ok = eng.RunAll(list, Path.Combine(root, "_tmp"));
                Console.WriteLine("download=" + ok);
                if (!ok) { f.Close(); return; }
                foreach (DownloadItem it in list) local.Add(it.DestPath);
                ConvertRequest req = new ConvertRequest();
                req.Repo = repo; req.QuantName = pick.Name; req.OutType = pick.OutType;
                req.OutDir = root; req.SourceFiles = local; req.EstBytes = pick.EstBytes;
                ConvertOutcome oc = ConvertService.Run(req, delegate(string s) { Console.WriteLine("CV " + s); });
                Console.WriteLine("convert=" + oc.Ok + " path=" + oc.GgufPath + " err=" + oc.Err);
                f.Close();
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
