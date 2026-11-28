// LocModel Downloader v2.4 - 在线搜索 HuggingFace（等价 LocModel「搜索」选项卡）
// 流程：关键词 → api/models?filter=gguf → 选仓库 → repo_quants → 选量化档 → 交主窗口下载内核
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LocModelDL
{
    public class HubBrowserForm : Form
    {
        static readonly Color CBg = Color.FromArgb(24, 25, 29);
        static readonly Color CPanel = Color.FromArgb(32, 34, 39);
        static readonly Color CPanel2 = Color.FromArgb(42, 45, 52);
        static readonly Color CText = Color.FromArgb(226, 230, 236);
        static readonly Color CDim = Color.FromArgb(150, 158, 170);
        static readonly Color CLine = Color.FromArgb(58, 62, 70);
        static readonly Color CAccent = Color.FromArgb(56, 132, 255);
        // v3.1.2：按钮文案固定为「开始下载」，下载目标由新增的单选区决定
        const string NormalButtonText = "开始下载";
        const string ConvertButtonText = "下载原始权重并转换";

        readonly MainForm _owner;
        TextBox txtQuery;
        Button btnSearch;
        Label lblState;
        ListBox lstRepos;
        Panel pnlBody;
        Label lblRepo, lblMeta, lblQuantsTitle, lblHint;
        ListBox lstQuants;
        CheckBox chkMmproj;
        Button btnDownload;
        TextBox txtLog;

        List<HubModel> _repos = new List<HubModel>();
        List<QuantGroup> _quants = new List<QuantGroup>();
        string _repo = "";
        bool _busy;

        // v3.1.2：下载目标（二选一）——只作用于本窗口提交的任务，不改主界面设置
        RadioButton rdoMain, rdoCustom;
        TextBox txtDest;
        Button btnDestBrowse;
        Label lblDestTitle, lblDestState;
        // v3.1.3：队列状态行（显示主窗口队列当前执行/等待数）+ 定时刷新
        Label lblQueueState;
        System.Windows.Forms.Timer _queueTimer;
        // 目标设置尚未从配置文件读入时不回写，避免首次打开就造文件
        bool _destReady = false;

        public HubBrowserForm(MainForm owner)
        {
            _owner = owner;
            Text = "在线搜索 HuggingFace · LocModel 模型下载器 v3.1.3";
            Width = 1060; Height = 740;
            MinimumSize = new Size(880, 600);
            BackColor = CBg; ForeColor = CText;
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;
            BuildUI();
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

        void BuildUI()
        {
            // ===== 顶部搜索栏 =====
            Panel top = new Panel();
            top.Dock = DockStyle.Top; top.Height = 74; top.BackColor = CPanel; top.Padding = new Padding(14, 8, 14, 8);
            Controls.Add(top);

            Label l0 = new Label();
            l0.Text = "搜索 HuggingFace 上的 GGUF 仓库（按下载量排序，hf-mirror 不可用时自动切源）";
            l0.ForeColor = CDim; l0.AutoSize = true; l0.Location = new Point(14, 8);
            l0.Font = new Font("Microsoft YaHei UI", 8.5f);
            top.Controls.Add(l0);

            txtQuery = new TextBox();
            txtQuery.Bounds = new Rectangle(14, 30, 640, 26);
            txtQuery.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtQuery.BackColor = CPanel2; txtQuery.ForeColor = CText; txtQuery.BorderStyle = BorderStyle.FixedSingle;
            txtQuery.Font = new Font("Consolas", 9.5f);
            txtQuery.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); } };
            top.Controls.Add(txtQuery);

            btnSearch = MakeButton("搜索", 662, 30, 84);
            btnSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSearch.BackColor = CAccent; btnSearch.ForeColor = Color.White;
            btnSearch.Click += delegate { DoSearch(); };
            top.Controls.Add(btnSearch);

            Button btnHot = MakeButton("热门 GGUF", 754, 30, 100);
            btnHot.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnHot.Click += delegate { txtQuery.Text = ""; DoSearch(); };
            top.Controls.Add(btnHot);

            lblState = new Label();
            lblState.AutoSize = false; lblState.Bounds = new Rectangle(14, 56, 1000, 16);
            lblState.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblState.ForeColor = CDim; lblState.Font = new Font("Microsoft YaHei UI", 8f);
            lblState.Text = "输入模型名或链接后回车";
            top.Controls.Add(lblState);

            // ===== 左侧结果列表 =====
            lstRepos = new ListBox();
            lstRepos.Dock = DockStyle.Left; lstRepos.Width = 400;
            lstRepos.BackColor = CBg; lstRepos.ForeColor = CText; lstRepos.BorderStyle = BorderStyle.FixedSingle;
            lstRepos.Font = new Font("Consolas", 9f);
            lstRepos.IntegralHeight = false;
            lstRepos.HorizontalScrollbar = true;
            lstRepos.SelectedIndexChanged += delegate { OnSelectRepo(); };
            Controls.Add(lstRepos);

            // ===== 右侧详情 =====
            pnlBody = new Panel();
            pnlBody.Dock = DockStyle.Fill; pnlBody.BackColor = CBg; pnlBody.Padding = new Padding(16, 12, 16, 10);
            pnlBody.AutoScroll = true;
            Controls.Add(pnlBody);
            pnlBody.BringToFront();

            lblRepo = new Label();
            lblRepo.AutoSize = false; lblRepo.Bounds = new Rectangle(16, 12, 600, 28);
            lblRepo.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold); lblRepo.ForeColor = CText;
            pnlBody.Controls.Add(lblRepo);

            lblMeta = new Label();
            lblMeta.AutoSize = false; lblMeta.Bounds = new Rectangle(16, 44, 600, 40);
            lblMeta.ForeColor = CDim; lblMeta.Font = new Font("Microsoft YaHei UI", 8.5f);
            pnlBody.Controls.Add(lblMeta);

            lblQuantsTitle = new Label();
            lblQuantsTitle.AutoSize = false; lblQuantsTitle.Bounds = new Rectangle(16, 90, 600, 20);
            lblQuantsTitle.ForeColor = CDim; lblQuantsTitle.Text = "量化档";
            pnlBody.Controls.Add(lblQuantsTitle);

            lstQuants = new ListBox();
            lstQuants.Bounds = new Rectangle(16, 112, 600, 260);
            lstQuants.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lstQuants.BackColor = CPanel; lstQuants.ForeColor = CText; lstQuants.BorderStyle = BorderStyle.None;
            lstQuants.Font = new Font("Consolas", 9f);
            lstQuants.HorizontalScrollbar = true;
            lstQuants.SelectedIndexChanged += delegate { UpdateHint(); };
            pnlBody.Controls.Add(lstQuants);

            // ===== v3.1.2：下载目标（二选一）+ 自定义目录浏览 =====
            lblDestTitle = new Label();
            lblDestTitle.AutoSize = false; lblDestTitle.Bounds = new Rectangle(16, 380, 76, 20);
            lblDestTitle.ForeColor = CText; lblDestTitle.Font = new Font("Microsoft YaHei UI", 9f);
            lblDestTitle.Text = "下载到：";
            pnlBody.Controls.Add(lblDestTitle);

            rdoMain = new RadioButton();
            rdoMain.Text = "主界面目录";
            rdoMain.Bounds = new Rectangle(100, 378, 170, 22);
            rdoMain.ForeColor = CText; rdoMain.BackColor = CBg;
            rdoMain.Checked = true;
            rdoMain.CheckedChanged += delegate { UpdateDestState(); };
            pnlBody.Controls.Add(rdoMain);

            rdoCustom = new RadioButton();
            rdoCustom.Text = "自定义目录";
            rdoCustom.Bounds = new Rectangle(288, 378, 170, 22);
            rdoCustom.ForeColor = CText; rdoCustom.BackColor = CBg;
            rdoCustom.CheckedChanged += delegate { UpdateDestState(); };
            pnlBody.Controls.Add(rdoCustom);

            txtDest = new TextBox();
            txtDest.Bounds = new Rectangle(16, 406, 460, 24);
            txtDest.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtDest.ReadOnly = true;
            txtDest.BackColor = CPanel2; txtDest.ForeColor = CText; txtDest.BorderStyle = BorderStyle.FixedSingle;
            txtDest.Font = new Font("Microsoft YaHei UI", 8.5f);
            pnlBody.Controls.Add(txtDest);

            btnDestBrowse = MakeButton("浏览…", 482, 404, 62);
            btnDestBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnDestBrowse.Click += delegate { PickDestDir(); };
            pnlBody.Controls.Add(btnDestBrowse);

            lblDestState = new Label();
            lblDestState.AutoSize = false; lblDestState.Bounds = new Rectangle(552, 408, 320, 20);
            lblDestState.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblDestState.ForeColor = CDim; lblDestState.Font = new Font("Microsoft YaHei UI", 8f);
            pnlBody.Controls.Add(lblDestState);

            chkMmproj = new CheckBox();
            chkMmproj.Text = "附带 mmproj（视觉投影，多模态模型需要）";
            chkMmproj.Bounds = new Rectangle(16, 438, 380, 24);
            chkMmproj.ForeColor = CText; chkMmproj.BackColor = CBg;
            chkMmproj.Enabled = false;
            pnlBody.Controls.Add(chkMmproj);

            btnDownload = MakeButton(NormalButtonText, 16, 468, 130);
            btnDownload.BackColor = CAccent; btnDownload.ForeColor = Color.White;
            btnDownload.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            btnDownload.Height = 32;
            btnDownload.Enabled = false;
            btnDownload.Click += delegate { DoDownload(); };
            pnlBody.Controls.Add(btnDownload);

            lblHint = new Label();
            lblHint.AutoSize = false; lblHint.Bounds = new Rectangle(156, 470, 620, 18);
            lblHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblHint.ForeColor = CDim; lblHint.Font = new Font("Microsoft YaHei UI", 8f);
            pnlBody.Controls.Add(lblHint);

            // v3.1.3：队列状态行 —— 提交方直接看到主窗口队列「正在执行 / 等待 N 个」，满了也能提前知道
            lblQueueState = new Label();
            lblQueueState.AutoSize = false; lblQueueState.Bounds = new Rectangle(156, 490, 620, 18);
            lblQueueState.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblQueueState.ForeColor = CDim; lblQueueState.Font = new Font("Microsoft YaHei UI", 8f);
            lblQueueState.Text = "下载队列：—";
            pnlBody.Controls.Add(lblQueueState);

            txtLog = new TextBox();
            txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.ReadOnly = true;
            txtLog.Bounds = new Rectangle(16, 512, 600, 105);
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            txtLog.BackColor = Color.FromArgb(18, 19, 22); txtLog.ForeColor = Color.FromArgb(170, 200, 170);
            txtLog.BorderStyle = BorderStyle.None; txtLog.Font = new Font("Consolas", 8.5f);
            pnlBody.Controls.Add(txtLog);

            pnlBody.BringToFront();
            lstRepos.SendToBack();
            top.SendToBack();
            LoadDestFromStore();

            // v3.1.3：每秒刷新队列状态行（主窗口队列变化不会产生跨窗体事件）
            _queueTimer = new System.Windows.Forms.Timer();
            _queueTimer.Interval = 1000;
            _queueTimer.Tick += delegate { RefreshQueueState(); };
            _queueTimer.Start();
            RefreshQueueState();
        }

        // v3.1.3：队列状态行刷新 —— 读主窗口的队列状态文本
        void RefreshQueueState()
        {
            if (lblQueueState == null) return;
            try
            {
                string t = _owner.QueueStatusText();
                bool full = _owner.QueueFullNow;
                lblQueueState.ForeColor = full ? Color.FromArgb(255, 150, 120) : CDim;
                lblQueueState.Text = t + (full ? "   ·   队列已满，等完成或到主界面点「清空队列」" : "");
            }
            catch { }
        }

        // v3.1.3：提交被拒时给醒目提示（同一任务重复提交 / 队列已满）
        void WarnRejected(string note)
        {
            string m = Min(note).Length > 0 ? Min(note) : "任务未加入队列";
            Log("未提交：" + m);
            if (lblQueueState != null)
            {
                lblQueueState.ForeColor = Color.FromArgb(255, 120, 110);
                lblQueueState.Text = "提交失败：" + m;
            }
            if (!MainForm.AutoQuiet)
                MessageBox.Show(this,
                    m + "\n\n可以：\n· 等正在执行的任务跑完再点一次\n· 或到主界面点「清空队列」后重新提交",
                    "未提交", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // v3.1.3：停掉队列状态定时器
            try { if (_queueTimer != null) { _queueTimer.Stop(); _queueTimer.Dispose(); _queueTimer = null; } }
            catch { }
            base.OnFormClosed(e);
        }

        // ===== v3.1.2：下载目标持久化（记忆上次选择） =====
        void LoadDestFromStore()
        {
            int mode; string dir;
            HubDestStore.Load(out mode, out dir);
            txtDest.Text = dir;
            rdoCustom.Checked = (mode == HubDestStore.ModeCustom);
            rdoMain.Checked = !rdoCustom.Checked;
            _destReady = true;
            UpdateDestState();
        }

        void SaveDestToStore()
        {
            if (!_destReady) return;
            HubDestStore.Save(rdoCustom.Checked ? HubDestStore.ModeCustom : HubDestStore.ModeMain, txtDest.Text.Trim());
        }

        void PickDestDir()
        {
            using (FolderBrowserDialog fb = new FolderBrowserDialog())
            {
                fb.Description = "选择下载保存目录（本次任务落盘为：该目录\\{owner}__{仓库名}）";
                fb.ShowNewFolderButton = true;
                string cur = HubDestStore.Normalize(txtDest.Text);
                if (cur.Length > 0 && Directory.Exists(cur)) fb.SelectedPath = cur;
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                txtDest.Text = HubDestStore.Normalize(fb.SelectedPath);
                rdoCustom.Checked = true;
                UpdateDestState();
                SaveDestToStore();
                Log("下载目录改为：" + HubDestStore.ModelDirOf(txtDest.Text, _repo.Length > 0 ? _repo : "owner/repo"));
            }
        }

        void UpdateDestState()
        {
            bool custom = rdoCustom.Checked;
            txtDest.Enabled = custom;
            btnDestBrowse.Enabled = custom;
            string dir = txtDest.Text.Trim();
            if (!custom)
            {
                string baseDir = _owner.ModelsDir;
                string real = HubDestStore.ModelDirOf(baseDir, _repo.Length > 0 ? _repo : "owner/repo");
                lblDestState.ForeColor = CDim;
                lblDestState.Text = string.IsNullOrEmpty(baseDir) ? "主界面未设置模型目录" : "实际落盘：" + real;
            }
            else if (dir.Length == 0)
            {
                lblDestState.ForeColor = Color.FromArgb(255, 190, 90);
                lblDestState.Text = "请先点「浏览…」选择目录";
            }
            else
            {
                lblDestState.ForeColor = CDim;
                lblDestState.Text = Directory.Exists(dir) ? "实际落盘：" + HubDestStore.ModelDirOf(dir, _repo.Length > 0 ? _repo : "owner/repo") : "目录不存在，开始时将自动创建";
            }
            SaveDestToStore();
            UpdateHint();
        }

        // 返回要传给主窗口的目标基目录；null = 沿用主界面目录
        string ResolveDestDir()
        {
            if (!rdoCustom.Checked) return null;
            return HubDestStore.Normalize(txtDest.Text);
        }

        void Log(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
        }

        // ===== 搜索（v2.4.1：先识别链接/仓库标识，命中就直接进仓库页）=====
        void DoSearch()
        {
            if (_busy) { Log("上一次请求尚未结束"); return; }
            string raw = txtQuery.Text.Trim();

            // 链接 / 裸 repo id 命中时不再交给 /api/models?search=（那里只匹配仓库名，整条 URL 必 0 命中）
            HubRef hr = HubUrlMapper.Parse(raw);
            if (raw.Length > 0 && !hr.IsSearch && hr.Repo.Length > 0)
            {
                OpenRepoDirect(hr);
                return;
            }
            string q = raw;
            if (raw.Length > 0 && hr.IsSearch && hr.Query.Length > 0) q = hr.Query;
            if (raw.Length > 0 && !string.Equals(q, raw, StringComparison.Ordinal)) Log("链接已拆成搜索词：" + q);

            _busy = true; btnSearch.Enabled = false;
            lblState.Text = "正在搜索 " + (q.Length == 0 ? "热门 GGUF" : "“" + q + "”") + " …";
            lstRepos.Items.Clear(); lstQuants.Items.Clear();
            _repos.Clear(); _quants.Clear(); _repo = "";
            lblRepo.Text = ""; lblMeta.Text = ""; btnDownload.Enabled = false; chkMmproj.Enabled = false;
            Log("开始搜索：" + (q.Length == 0 ? "(热门)" : q));

            string repoLike = (q.Length > 0 && HubUrlMapper.IsRepoId(q)) ? q : "";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<HubModel> list = new List<HubModel>();
                string log = "";
                try { list = HubSearch.Search(q, 40, out log); }
                catch (Exception ex) { log = "搜索异常: " + ex.Message; }
                if (list.Count == 0 && repoLike.Length > 0)
                {
                    // owner/name 形态搜不到时，直接按仓库地址再试一次（等价 LocModel 的容错）
                    log += "按仓库地址直取：" + repoLike + "\r\n";
                    CatalogService.RepoFiles rf = CatalogService.GetRepoFiles(repoLike);
                    if (rf.Ok)
                    {
                        HubModel m = new HubModel();
                        m.Repo = repoLike;
                        int sl = repoLike.IndexOf('/');
                        if (sl > 0) m.Author = repoLike.Substring(0, sl);
                        list.Add(m);
                        log += "命中仓库 " + repoLike + "（" + rf.Paths.Count + " 个文件）\r\n";
                    }
                    else if (rf.Err.Length > 0) log += rf.Err + "\r\n";
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false; btnSearch.Enabled = true;
                        Log(log.Trim());
                        _repos = list;
                        lstRepos.BeginUpdate();
                        foreach (HubModel m in _repos) lstRepos.Items.Add(m.Line());
                        lstRepos.EndUpdate();
                        if (_repos.Count == 0)
                        {
                            lblState.Text = "没有搜到 GGUF 仓库，换个关键词试试（例如 qwen3 / llama / gemma / deepseek）；也可以直接粘贴 huggingface.co 链接";
                        }
                        else
                        {
                            lblState.Text = "共 " + _repos.Count + " 个结果，点击左侧任一条查看量化档";
                            lstRepos.SelectedIndex = 0;
                        }
                    });
                }
                catch { }
            });
        }

        // 链接识别命中：跳过搜索，直接把该仓库送进量化档解析
        void OpenRepoDirect(HubRef hr)
        {
            HubModel m = new HubModel();
            m.Repo = hr.Repo;
            int sl = hr.Repo.IndexOf('/');
            if (sl > 0) m.Author = hr.Repo.Substring(0, sl);

            _repos = new List<HubModel>();
            _repos.Add(m);
            _quants = new List<QuantGroup>();
            _repo = "";
            lstQuants.Items.Clear();
            lstRepos.BeginUpdate();
            lstRepos.Items.Clear();
            lstRepos.Items.Add(hr.Repo + "    链接识别");
            lstRepos.EndUpdate();

            StringBuilder st = new StringBuilder();
            st.Append("已识别：“").Append(hr.Raw).Append("” → ").Append(hr.Repo);
            if (hr.HasFile) st.Append("  ·  文件 ").Append(hr.FilePath);
            else if (hr.SubPath.Length > 0) st.Append("  ·  目录 ").Append(hr.SubPath);
            if (hr.Note.Length > 0) st.Append("  ·  ").Append(hr.Note);
            lblState.Text = st.ToString();
            Log("链接识别：" + hr.Describe() + (hr.Note.Length > 0 ? "（" + hr.Note + "）" : ""));

            lstRepos.SelectedIndex = 0;
            if (_repo.Length == 0) OnSelectRepo();
        }

        // ===== 选仓库 → 解析量化档 =====
        void OnSelectRepo()
        {
            int i = lstRepos.SelectedIndex;
            if (i < 0 || i >= _repos.Count) return;
            HubModel m = _repos[i];
            if (m.Repo == _repo) return;
            _repo = m.Repo;
            _quants = new List<QuantGroup>();
            lstQuants.Items.Clear(); btnDownload.Enabled = false; chkMmproj.Enabled = false; chkMmproj.Checked = false;

            lblRepo.Text = m.Repo;
            lblRepo.ForeColor = CText;
            btnDownload.Text = NormalButtonText;
            StringBuilder sb = new StringBuilder();
            sb.Append("下载 ").Append(m.Downloads.ToString("N0")).Append("   ·   收藏 ").Append(m.Likes.ToString("N0"));
            if (m.PipelineTag.Length > 0) sb.Append("   ·   ").Append(m.PipelineTag);
            if (m.Updated.Length >= 10) sb.Append("   ·   更新 ").Append(m.Updated.Substring(0, 10));
            lblMeta.Text = sb.ToString();
            lblQuantsTitle.Text = "量化档（正在解析文件清单…）";
            Log("解析仓库：" + m.Repo);

            string repo = m.Repo;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                List<QuantGroup> qs = RepoQuants.Parse(repo, out err);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (_repo != repo) return;
                        _quants = qs;
                        if (qs.Count == 0)
                        {
                            ShowQuantError(err);
                            return;
                        }
                        lstQuants.BeginUpdate();
                        int pick = -1, idx = 0;
                        foreach (QuantGroup g in _quants)
                        {
                            lstQuants.Items.Add(g.Line());
                            if (pick < 0 && g.Kind == "quant") pick = idx;
                            idx++;
                        }
                        lstQuants.EndUpdate();
                        bool hasMm = false, anyConvert = false;
                        foreach (QuantGroup g in _quants)
                        {
                            if (g.Kind == "mmproj") hasMm = true;
                            if (g.Convert) anyConvert = true;
                        }
                        chkMmproj.Enabled = hasMm;
                        if (hasMm) chkMmproj.Checked = true;
                        int nq = 0; foreach (QuantGroup g in _quants) if (g.Kind == "quant") nq++;
                        if (anyConvert)
                        {
                            lblQuantsTitle.ForeColor = Color.FromArgb(120, 190, 255);
                            lblQuantsTitle.Text = "这个仓库没有 GGUF 文件，需要先转换（" + nq + " 档，已按推荐顺序排列）";
                            btnDownload.Text = ConvertButtonText;
                            Log("仓库无 GGUF：给出 " + nq + " 个转换档，源权重 " + QuantGroup.Human(_quants[0].Bytes));
                        }
                        else
                        {
                            lblQuantsTitle.ForeColor = CDim;
                            lblQuantsTitle.Text = "量化档（共 " + nq + " 档" + (hasMm ? "，含 mmproj 视觉投影" : "") + "，已按质量/体积推荐顺序排列）";
                            btnDownload.Text = NormalButtonText;
                            Log("量化档 " + nq + " 档" + (hasMm ? " + mmproj" : ""));
                        }
                        if (pick >= 0) lstQuants.SelectedIndex = pick;
                        btnDownload.Enabled = pick >= 0;
                    });
                }
                catch { }
            });

            // v3.1.3：切换模型后立即刷新「实际落盘」路径（此前要等下一次点选目标目录才更新）
            UpdateDestState();
        }

        // 解析失败：按错误码给出可执行的提示，不再一律说"仓库内没有 .gguf 文件"
        void ShowQuantError(string err)
        {
            string code = "", msg = err == null ? "" : err;
            int bar = msg.IndexOf('|');
            if (bar > 0) { code = msg.Substring(0, bar); msg = msg.Substring(bar + 1); }
            string head, tip;
            switch (code)
            {
                case "NOTFOUND":
                    head = "仓库不存在";
                    tip = msg + "。粘贴链接时确认 owner/name 没有打错（例如 xtools-at 不是 xtols-at）";
                    break;
                case "AUTH":
                    head = "无权限读取";
                    tip = msg + "。私有仓库需要在浏览器登录后手动下载";
                    break;
                case "EMPTY":
                    head = "空仓库";
                    tip = msg;
                    break;
                case "NO_GGUF":
                    head = "没有可转换的权重";
                    tip = msg;
                    break;
                case "NET":
                    head = "网络失败";
                    tip = msg + "。可在主界面换站点后重试";
                    break;
                default:
                    head = "无法解析";
                    tip = msg.Length > 0 ? msg : "仓库内没有 .gguf 文件";
                    break;
            }
            lblQuantsTitle.ForeColor = Color.FromArgb(255, 138, 128);
            lblQuantsTitle.Text = "量化档（" + head + "）";
            lblRepo.ForeColor = Color.FromArgb(255, 138, 128);
            lblState.Text = tip;
            btnDownload.Enabled = false;
            Log(head + "：" + tip);
        }

        void UpdateHint()
        {
            int i = lstQuants.SelectedIndex;
            if (i < 0 || i >= _quants.Count) { lblHint.Text = ""; btnDownload.Enabled = false; return; }
            QuantGroup g = _quants[i];
            if (g.Kind == "mmproj")
            {
                lblHint.Text = "mmproj 是视觉投影组件，请选择具体量化档，勾选“附带 mmproj”一起下载";
                btnDownload.Enabled = false;
                return;
            }
            StringBuilder sb = new StringBuilder();
            if (g.Convert)
            {
                sb.Append("源权重 ").Append(g.Files.Count).Append(" 个文件 ").Append(QuantGroup.Human(g.Bytes));
                if (g.EstBytes > 0) sb.Append("  转换后约 ").Append(QuantGroup.Human(g.EstBytes));
                sb.Append("   先下载源权重，再本地转换 → ").Append(g.Name);
                lblHint.Text = sb.ToString();
                btnDownload.Enabled = true;
                return;
            }
            sb.Append(g.Files.Count).Append(" 个文件  ").Append(QuantGroup.Human(g.Bytes));
            if (g.Sharded) sb.Append("（分片）");
            sb.Append("   落盘：").Append(AskDestHint());
            lblHint.Text = sb.ToString();
            btnDownload.Enabled = true;
        }

        string AskDestHint()
        {
            // v3.1.2：落盘基目录跟随「下载到」选择，null 表示主界面目录
            string dir = ResolveDestDir();
            if (dir == null) dir = _owner.ModelsDir;
            if (string.IsNullOrEmpty(dir)) return "(未设置保存位置)";
            string repo = _repo.Length > 0 ? _repo : "owner/repo";
            return HubDestStore.ModelDirOf(dir, repo);
        }

        // ===== 自动化验图：搜索 → 选首个仓库 → 等量化档 → 截图 =====
        public void UiCheck(string png, string query)
        {
            if (!string.IsNullOrEmpty(query)) txtQuery.Text = query;
            DoSearch();
            WaitFor(delegate { return !_busy; }, 90);
            if (lstRepos.Items.Count > 0) lstRepos.SelectedIndex = 0;
            WaitFor(delegate { return lstQuants.Items.Count > 0; }, 90);
            WaitFor(delegate { return false; }, 2);
            Bitmap bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();

            // ===== v3.1.2：下载目标区布局 / 状态自检 =====
            Console.WriteLine("HubSize=" + Width + "x" + Height);
            Control[] cc = new Control[] { lblDestTitle, rdoMain, rdoCustom, txtDest, btnDestBrowse, lblDestState,
                                           chkMmproj, btnDownload, lblHint, lblQueueState, txtLog };
            int ov = 0;
            for (int a = 0; a < cc.Length; a++)
                for (int b = a + 1; b < cc.Length; b++)
                {
                    Rectangle rr = Rectangle.Intersect(cc[a].Bounds, cc[b].Bounds);
                    if (rr.Width > 0 && rr.Height > 0)
                    {
                        ov++;
                        Console.WriteLine("  OVERLAP " + cc[a].GetType().Name + "(" + cc[a].Bounds
                            + ") x " + cc[b].GetType().Name + "(" + cc[b].Bounds + ")");
                    }
                }
            bool inPanel = txtLog.Bottom <= pnlBody.ClientSize.Height;
            Console.WriteLine("  Layout=" + (ov == 0 && inPanel ? "OK" : "FAIL") + " overlap=" + ov
                + " txtLog.Bottom=" + txtLog.Bottom + " panelH=" + pnlBody.ClientSize.Height);

            bool keepReady = _destReady;
            _destReady = false;                       // 自检期间不写真实配置
            rdoMain.Checked = true; UpdateDestState();
            bool mainGray = !txtDest.Enabled && !btnDestBrowse.Enabled;
            Console.WriteLine("  MainMode=" + (mainGray ? "OK" : "FAIL") + " txtDest.Enabled=" + txtDest.Enabled
                + " browse.Enabled=" + btnDestBrowse.Enabled + " state=[" + lblDestState.Text + "]");
            rdoCustom.Checked = true; UpdateDestState();
            bool cusOn = txtDest.Enabled && btnDestBrowse.Enabled;
            Console.WriteLine("  CustomMode=" + (cusOn ? "OK" : "FAIL") + " txtDest.Enabled=" + txtDest.Enabled
                + " text=[" + txtDest.Text + "] state=[" + lblDestState.Text + "]");
            txtDest.Text = ""; UpdateDestState();
            Console.WriteLine("  CustomEmpty.state=[" + lblDestState.Text + "] hint=[" + lblHint.Text + "]");
            Bitmap bmp2 = new Bitmap(Width, Height);
            DrawToBitmap(bmp2, new Rectangle(0, 0, Width, Height));
            bmp2.Save(Path.ChangeExtension(png, ".custom.png"), System.Drawing.Imaging.ImageFormat.Png);
            bmp2.Dispose();
            _destReady = keepReady;
            LoadDestFromStore();                      // 恢复真实记忆状态
        }

        static void WaitFor(Func<bool> cond, int seconds)
        {
            for (int i = 0; i < seconds * 10; i++)
            {
                Application.DoEvents();
                try { if (cond()) return; } catch { }
                Thread.Sleep(100);
            }
        }

        // ===== 下载 =====
        void DoDownload()
        {
            if (_busy) { Log("请等当前请求结束"); return; }
            int i = lstQuants.SelectedIndex;
            if (i < 0 || i >= _quants.Count) { MessageBox.Show(this, "请先选择量化档", "未选择"); return; }
            QuantGroup g = _quants[i];
            if (g.Kind == "mmproj") { MessageBox.Show(this, "请选择具体量化档（mmproj 会作为附加项一起下载）", "选择有误"); return; }

            // v3.1.2：自定义目录为空时不提交，聚焦到浏览按钮
            string dest = ResolveDestDir();
            if (rdoCustom.Checked && string.IsNullOrEmpty(dest))
            {
                MessageBox.Show(this, "选择了「自定义目录」但目录为空，请先点「浏览…」选择，或改选「主界面目录」", "缺少下载目录");
                btnDestBrowse.Focus();
                return;
            }
            SaveDestToStore();
            string destShown = dest == null ? _owner.ModelsDir : dest;

            if (g.Convert)
            {
                Log("提交转换任务：" + _repo + " / " + g.Name + "（源 " + g.Files.Count + " 个文件，" + QuantGroup.Human(g.Bytes) + "，产物约 " + QuantGroup.Human(g.EstBytes) + "）");
                if (_owner.DownloadAndConvert(_repo, g.Name, g.Files, g.Sizes, g.OutType, g.EstBytes, dest))
                {
                    Log("已交给主窗口队列：" + Min(_owner.LastQueueNote));
                    Log("落盘基目录：" + destShown);
                    Log("可以继续选择下一个模型，任务会按顺序排队执行");
                    RefreshQueueState();                     // v3.1.3：提交成功即刻刷新队列状态行
                }
                else WarnRejected(_owner.LastQueueNote);     // v3.1.3：重复提交/队列满 → 醒目提示
                return;
            }

            List<string> files = new List<string>(g.Files);
            List<long> sizes = new List<long>(g.Sizes);
            if (chkMmproj.Checked && chkMmproj.Enabled)
            {
                foreach (QuantGroup x in _quants)
                    if (x.Kind == "mmproj") { files.AddRange(x.Files); sizes.AddRange(x.Sizes); break; }
            }
            Log("提交下载：" + _repo + " / " + g.Name + " 共 " + files.Count + " 个文件");
            if (_owner.DownloadFromHub(_repo, g.Name, files, sizes, dest))
            {
                Log("已交给主窗口队列：" + Min(_owner.LastQueueNote));
                Log("落盘基目录：" + destShown);
                Log("可以继续选择下一个模型，任务会按顺序排队执行");
                RefreshQueueState();                     // v3.1.3：提交成功即刻刷新队列状态行
            }
            else WarnRejected(_owner.LastQueueNote);      // v3.1.3：重复提交/队列满 → 醒目提示
        }

        static string Min(string s)
        {
            return s == null ? "" : s;
        }
    }
}
