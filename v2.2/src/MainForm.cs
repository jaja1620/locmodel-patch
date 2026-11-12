// LocModel Downloader v2.1 - UI
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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

        LocModelInfo _info = new LocModelInfo();
        List<ModelEntry> _all = new List<ModelEntry>();
        List<ModelEntry> _view = new List<ModelEntry>();
        ModelEntry _sel;
        string _category = "image";
        bool _mismatchWarned = false;
        volatile bool _busy = false;
        DownloadEngine _eng;

        TextBox txtDir;
        TextBox txtSites;
        Label lblDirState;
        Label lblSiteState;
        ListBox lstModels;
        Panel pnlDetail;
        Label lblTitle, lblOrg, lblDesc, lblRepo, lblSize, lblDepTitle, lblProgress;
        ComboBox cboQuant;
        ListBox lstDeps;
        Button btnDownload, btnCancel, btnOpen, btnRefresh, btnBrowse;
        ProgressBar pb;
        TextBox txtLog;
        Label lblStatus;

        public MainForm()
        {
            Text = "LocModel 模型下载器 v2.2";
            Width = 1320; Height = 900;
            MinimumSize = new Size(1120, 760);
            BackColor = CBg; ForeColor = CText;
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            BuildUI();
            txtSites.Text = SiteStore.Load();
            FormClosing += delegate { try { SiteStore.Save(txtSites.Text); } catch { } };
            Shown += delegate { ReloadAll(true); };
        }

        void BuildUI()
        {
            // ===== 顶部：目录栏 =====
            Panel top = new Panel();
            top.Dock = DockStyle.Top; top.Height = 86; top.BackColor = CPanel; top.Padding = new Padding(16, 10, 16, 10);
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

            // ===== 顶部第二行：下载站点（v2.2：默认 hf-mirror.com，可自定义多个，下载前自动测速择优）=====
            Panel site = new Panel();
            site.Dock = DockStyle.Top; site.Height = 96; site.BackColor = CPanel; site.Padding = new Padding(16, 6, 16, 6);
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
            lblSiteState.Text = "默认站点 " + SiteStore.DefaultSite + "（国内镜像）。可自行增删，修改后自动保存。";
            site.Controls.Add(lblSiteState);

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
            lblDesc.AutoSize = false; lblDesc.Bounds = new Rectangle(20, 80, 700, 66);
            lblDesc.ForeColor = CText; lblDesc.Font = new Font("Microsoft YaHei UI", 9.5f);
            pnlDetail.Controls.Add(lblDesc);

            lblRepo = new Label();
            lblRepo.AutoSize = false; lblRepo.Bounds = new Rectangle(20, 150, 700, 20);
            lblRepo.ForeColor = Color.FromArgb(120, 180, 255); lblRepo.Font = new Font("Consolas", 9f);
            pnlDetail.Controls.Add(lblRepo);

            Label lq = new Label();
            lq.Text = "量化档"; lq.AutoSize = true; lq.Location = new Point(20, 182); lq.ForeColor = CDim;
            pnlDetail.Controls.Add(lq);

            cboQuant = new ComboBox();
            cboQuant.DropDownStyle = ComboBoxStyle.DropDownList;
            cboQuant.Bounds = new Rectangle(80, 178, 220, 26);
            cboQuant.BackColor = CPanel2; cboQuant.ForeColor = CText; cboQuant.FlatStyle = FlatStyle.Flat;
            cboQuant.SelectedIndexChanged += delegate { OnQuantChanged(); };
            pnlDetail.Controls.Add(cboQuant);

            lblSize = new Label();
            lblSize.AutoSize = false; lblSize.Bounds = new Rectangle(316, 182, 460, 20);
            lblSize.ForeColor = CDim; lblSize.Font = new Font("Microsoft YaHei UI", 8.5f);
            pnlDetail.Controls.Add(lblSize);

            btnDownload = MakeButton("开始下载", 20, 214, 130);
            btnDownload.BackColor = CAccent; btnDownload.ForeColor = Color.White; btnDownload.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            btnDownload.Height = 36;
            btnDownload.Click += delegate { StartDownload(); };
            pnlDetail.Controls.Add(btnDownload);

            btnCancel = MakeButton("取消下载", 160, 214, 104);
            btnCancel.Height = 36; btnCancel.Enabled = false;
            btnCancel.Click += delegate { RequestCancel(); };
            pnlDetail.Controls.Add(btnCancel);

            pb = new ProgressBar();
            pb.Bounds = new Rectangle(20, 258, 700, 14);
            pb.Style = ProgressBarStyle.Continuous;
            pnlDetail.Controls.Add(pb);

            lblProgress = new Label();
            lblProgress.AutoSize = false; lblProgress.Bounds = new Rectangle(20, 276, 700, 20);
            lblProgress.ForeColor = CDim; lblProgress.Font = new Font("Consolas", 8.5f);
            pnlDetail.Controls.Add(lblProgress);

            lblDepTitle = new Label();
            lblDepTitle.AutoSize = false; lblDepTitle.Bounds = new Rectangle(20, 306, 700, 20);
            lblDepTitle.ForeColor = CDim; lblDepTitle.Text = "本次将下载的文件";
            pnlDetail.Controls.Add(lblDepTitle);

            lstDeps = new ListBox();
            lstDeps.Bounds = new Rectangle(20, 330, 700, 190);
            lstDeps.BackColor = CPanel; lstDeps.ForeColor = CText; lstDeps.BorderStyle = BorderStyle.None;
            lstDeps.Font = new Font("Consolas", 8.5f);
            lstDeps.HorizontalScrollbar = true;
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

        // ============ 数据加载 ============
        void ReloadAll(bool showLog)
        {
            if (_busy) { Log("正在处理中，请稍候…"); return; }
            btnRefresh.Enabled = false;
            lblStatus.Text = "正在从 LocModel 获取目录与模型参数…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder sb = new StringBuilder();
                LocModelInfo info = LocModelProbe.Probe();
                sb.AppendLine("LocModel 程序: " + (info.ExePath == "" ? "未找到" : info.ExePath));
                sb.AppendLine("配置文件: " + (info.SettingsPath == "" ? "未找到" : info.SettingsPath));
                sb.AppendLine("默认模型目录: " + (info.ModelsDir == "" ? "(未知)" : info.ModelsDir));

                string log1;
                List<ModelEntry> text = CatalogService.LoadTextCatalog(info.ExePath, out log1);
                sb.AppendLine(log1.Trim());

                List<ModelEntry> image = EmbeddedImageCatalog.Build();
                string log2;
                CatalogService.SyncImageCatalogFromExe(image, info.ExePath, out log2);
                sb.AppendLine(log2);

                List<ModelEntry> all = new List<ModelEntry>();
                all.AddRange(image);
                all.AddRange(text);

                Invoke((MethodInvoker)delegate
                {
                    _info = info;
                    _all = all;
                    _mismatchWarned = false;
                    string def = info.ModelsDir;
                    if (!string.IsNullOrEmpty(def)) txtDir.Text = def;
                    ApplyCategory();
                    UpdateDirState();
                    btnRefresh.Enabled = true;
                    if (showLog) Log(sb.ToString().Trim());
                    lblStatus.Text = "共 " + _all.Count + " 个可下载模型（图像/视频 " + image.Count + " · 语言/推理 " + text.Count + "）";
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
            lblDesc.Text = _sel.Desc;
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

            lstDeps.Items.Clear();
            foreach (Quant qq in _sel.BuildQuants())
                if (qq.Name == q)
                    foreach (QuantFile f in qq.Files)
                        lstDeps.Items.Add("主文件  " + f.Repo + "  →  " + f.BaseName);
            if (_sel.Common.Count > 0)
            {
                lstDeps.Items.Add("—— 依赖组件（image\\common）——");
                foreach (QuantFile f in _sel.Common)
                    lstDeps.Items.Add("依赖    " + f.Repo + "  →  " + f.BaseName);
            }
            if (!_sel.IsImageLike && _sel.Vision)
                lstDeps.Items.Add("依赖    " + _sel.Repo + "  →  mmproj（自动配对）");
            lblDepTitle.Text = "本次将下载的文件（共 " + lstDeps.Items.Count + " 项，已存在的自动跳过）";

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

        void lblDepsRefresh(List<DownloadItem> list)
        {
            lstDeps.Items.Clear();
            foreach (DownloadItem it in list)
                lstDeps.Items.Add((it.Path.StartsWith("mmproj") ? "mmproj  " : "") + it.Repo + "  →  " + it.DestPath);
            lblDepTitle.Text = "本次将下载的文件（共 " + list.Count + " 项，已存在的自动跳过）";
            Log("下载清单 " + list.Count + " 项");
        }

        void RunDownload(List<DownloadItem> list, string modelsDir)
        {
            string tempRoot = Path.Combine(modelsDir, "_locmodel_dl_tmp");
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
                    if (ok)
                    {
                        pb.Value = 100;
                        lblProgress.Text = "下载完成";
                        Log("=== 全部完成 ===");
                        try { if (Directory.Exists(tempRoot) && Directory.GetFiles(tempRoot).Length == 0) Directory.Delete(tempRoot); } catch { }
                        MessageBox.Show(this, "下载完成。\n\n文件已保存到：\n" + modelsDir + "\n\n在 LocModel 中刷新模型列表即可看到。", "完成");
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
                        MessageBox.Show(this, "部分文件下载失败，其余文件已完成并可直接使用。\n\n失败明细见日志；再次点击下载只会补下失败或未完成的文件。", "部分失败");
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
            string log1;
            List<ModelEntry> text = CatalogService.LoadTextCatalog(info.ExePath, out log1);
            List<ModelEntry> image = EmbeddedImageCatalog.Build();
            string log2;
            CatalogService.SyncImageCatalogFromExe(image, info.ExePath, out log2);
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
            // 目录不一致提示逻辑
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
                System.Threading.Thread.Sleep(800);
                Application.DoEvents();
                Bitmap bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                f.Close();
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
