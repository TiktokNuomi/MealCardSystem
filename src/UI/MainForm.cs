using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MealCardSystem.Reader;
using MealCardSystem.Service;

namespace MealCardSystem.UI
{
    /// <summary>
    /// 主窗口：顶部状态栏 + 左侧导航 + 内容区。
    ///
    /// 读卡器轮询跑在后台线程上。Inventory_G2 是阻塞式串口调用，
    /// 若放在 UI 线程上每几百毫秒执行一次，界面会明显卡顿。
    /// </summary>
    public class MainForm : Form
    {
        private readonly AppSession _app;

        private Panel _contentHost;
        private Panel _navHost;
        private Panel _header;
        private Panel _statusBar;
        private Label _lblDbPath;
        private Label _lblReaderDot;
        private Label _lblReaderText;
        private Button _btnReader;
        private Label _lblOperator;
        private Label _lblStatus;

        private readonly List<FunctionPanel> _panels = new List<FunctionPanel>();
        private readonly List<Button> _navButtons = new List<Button>();
        private FunctionPanel _active;

        private Thread _pollThread;
        private volatile bool _polling;

        /// <summary>最近一次读到的卡片 EPC。切换面板时用来恢复「当前卡片」。</summary>
        private string _lastEpc;

        /// <summary>
        /// 启动后是否自动尝试连接读卡器。收银台开机就该能用，
        /// 默认自动连；诊断模式会关掉它，免得测试去碰真实串口。
        /// </summary>
        public bool AutoConnectOnStartup = true;

        public MainForm(AppSession app)
        {
            _app = app;
            BuildUi();
            CreatePanels();
            SwitchTo(0);
            StartPolling();

            // 窗口显示出来之后再连，这样连上了状态栏能立刻更新
            Shown += delegate { TryAutoConnect(); };
        }

        // ---------- 界面构建 ----------

        private void BuildUi()
        {
            Text = "食堂饭卡管理系统";
            ClientSize = new Size(1080, 720);
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiKit.BackColor;
            Font = UiKit.FontNormal;
            AutoScaleMode = AutoScaleMode.None;

            // ---------- 顶部 ----------
            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = 62;
            _header.BackColor = UiKit.HeaderColor;

            Label title = UiKit.Label("食堂饭卡管理系统", UiKit.FontSubtitle, Color.White);
            title.Location = new Point(22, 18);
            _header.Controls.Add(title);

            _btnReader = UiKit.Button("连接读卡器", "normal");
            _btnReader.Size = new Size(110, 32);
            _btnReader.Font = UiKit.FontSmall;
            _btnReader.Click += OnToggleReader;
            _header.Controls.Add(_btnReader);

            _lblReaderDot = new Label();
            _lblReaderDot.AutoSize = false;
            _lblReaderDot.Size = new Size(10, 10);
            _lblReaderDot.BackColor = UiKit.FaintColor;
            _header.Controls.Add(_lblReaderDot);

            _lblReaderText = UiKit.Label("读卡器未连接", UiKit.FontSmall, Color.FromArgb(0xC8, 0xD6, 0xE8));
            _header.Controls.Add(_lblReaderText);

            _lblOperator = UiKit.Label("", UiKit.FontSmall, Color.FromArgb(0xC8, 0xD6, 0xE8));
            _header.Controls.Add(_lblOperator);

            // 这些控件靠右摆放，位置由 LayoutHeader 统一算，不用 Anchor。
            // 原因见 LayoutHeader 的注释。
            _header.Resize += delegate { LayoutHeader(); };

            // ---------- 左侧导航 ----------
            _navHost = new Panel();
            _navHost.Dock = DockStyle.Left;
            _navHost.Width = 150;
            _navHost.BackColor = UiKit.SurfaceColor;
            _navHost.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(UiKit.BorderColor))
                {
                    e.Graphics.DrawLine(pen, _navHost.Width - 1, 0, _navHost.Width - 1, _navHost.Height);
                }
            };

            // ---------- 底部状态栏 ----------
            _statusBar = new Panel();
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = 30;
            _statusBar.BackColor = Color.FromArgb(0xEC, 0xEF, 0xF3);

            _lblStatus = UiKit.Label("就绪", UiKit.FontSmall, UiKit.MutedColor);
            _lblStatus.AutoSize = false;
            _lblStatus.Location = new Point(12, 7);
            _lblStatus.Height = 17;
            _statusBar.Controls.Add(_lblStatus);

            _lblDbPath = UiKit.Label("数据文件：" + _app.DatabasePath, UiKit.FontSmall, UiKit.FaintColor);
            _statusBar.Controls.Add(_lblDbPath);

            _statusBar.Resize += delegate { LayoutStatusBar(); };

            // ---------- 内容区 ----------
            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = UiKit.BackColor;
            _contentHost.Padding = new Padding(16);

            // ---------- 加入窗体 ----------
            //
            // 顺序很关键：WinForms 按 z 序从后往前停靠，也就是「最后 Add 的最先占位置」。
            // 所以「填充」的内容区必须第一个加进去，让它在其余控件各占一条边之后再吃掉
            // 剩下的中间区域；反过来写的话 Fill 会先抢走整块客户区，标题栏和左侧导航
            // 就只能叠在内容上面——看起来就是导航栏把主界面盖住了。
            //
            // 停靠的先后决定了各控件拿到哪条边，期望的布局是：
            //   标题栏（整宽·上）→ 状态栏（整宽·下）→ 导航栏（左侧）→ 内容区（填满中间）
            // 因此 Add 的先后正好相反。
            Controls.Add(_contentHost);
            Controls.Add(_navHost);
            Controls.Add(_statusBar);
            Controls.Add(_header);

            if (_app.CurrentOperator != null)
            {
                _lblOperator.Text = "操作员：" + _app.CurrentOperator.Caption;
            }

            UpdateReaderUi();
            LayoutHeader();
            LayoutStatusBar();

            FormClosing += OnFormClosing;
        }

        private void CreatePanels()
        {
            AddPanel("消费", new ConsumePanel(_app, this));
            AddPanel("充值", new RechargePanel(_app, this));
            AddPanel("发卡", new IssuePanel(_app, this));
            AddPanel("查询", new QueryPanel(_app, this));
            AddPanel("卡片管理", new CardManagePanel(_app, this));
            AddPanel("操作员", new OperatorPanel(_app, this));
        }

        private void AddPanel(string caption, FunctionPanel panel)
        {
            int index = _panels.Count;
            _panels.Add(panel);
            panel.Visible = false;
            _contentHost.Controls.Add(panel);

            Button nav = UiKit.NavButton(caption);
            nav.Width = _navHost.Width;
            nav.Location = new Point(0, index * 46);
            nav.Click += delegate { SwitchTo(index); };
            _navHost.Controls.Add(nav);
            _navButtons.Add(nav);
        }

        private void SwitchTo(int index)
        {
            if (index < 0 || index >= _panels.Count)
            {
                return;
            }

            for (int i = 0; i < _panels.Count; i++)
            {
                bool active = (i == index);
                _panels[i].Visible = active;

                Button nav = _navButtons[i];
                nav.BackColor = active ? Color.FromArgb(0xE8, 0xF1, 0xFE) : UiKit.SurfaceColor;
                nav.ForeColor = active ? UiKit.PrimaryColor : UiKit.TextColor;
                nav.Font = active ? UiKit.FontBold : UiKit.FontNormal;
            }

            _active = _panels[index];
            _active.BringToFront();

            // 切回来时把「当前卡片」恢复成最近读到的那张，避免操作员重新刷一次
            if (!string.IsNullOrEmpty(_lastEpc))
            {
                _active.OnCardScanned(_lastEpc);
            }

            // 光恢复 EPC 不够：同一个 EPC 会被 OnCardScanned 里的去重逻辑挡掉，
            // 于是本页显示的还是上次离开时的余额。卡片很可能是在别的页面上被改掉的
            // （扣了款、注销了卡），这里再强制重读一次数据库。
            _active.ReloadCard();

            _active.OnActivated();
        }

        /// <summary>供各面板在状态栏显示提示。</summary>
        public void SetStatus(string message)
        {
            _lblStatus.Text = message;
        }

        // ---------- 顶部 / 底部条内控件的摆放 ----------

        /// <summary>
        /// 摆放标题栏右侧的控件（读卡器状态、连接按钮、操作员）。
        ///
        /// 这里刻意不用 Anchor：这些控件是在标题栏还没被拉伸到实际宽度时加进去的，
        /// 那时记录下来的锚定距离是按控件的默认宽度 200 算的。标题栏真正撑开到
        /// 1080 之后，Anchor=Right 会照着这个错误基准把控件推到窗口外面
        /// （实测按钮跑到 X=1720），界面上看起来就是「这个按钮根本不存在」。
        /// 改成每次尺寸变化时显式重排，就没这个隐患了。
        /// </summary>
        private void LayoutHeader()
        {
            int right = _header.ClientSize.Width - 22;
            int mid = _header.Height / 2;

            _lblOperator.Left = right - _lblOperator.Width;
            _lblOperator.Top = mid - _lblOperator.Height / 2;

            _btnReader.Left = _lblOperator.Left - 18 - _btnReader.Width;
            _btnReader.Top = mid - _btnReader.Height / 2;

            _lblReaderText.Left = _btnReader.Left - _lblReaderText.Width - 26;
            _lblReaderText.Top = mid - _lblReaderText.Height / 2;

            _lblReaderDot.Left = _lblReaderText.Left - 16;
            _lblReaderDot.Top = mid - _lblReaderDot.Height / 2;
        }

        /// <summary>摆放状态栏：数据文件路径贴右，状态文字占满左侧剩余空间。</summary>
        private void LayoutStatusBar()
        {
            _lblDbPath.Left = _statusBar.ClientSize.Width - _lblDbPath.Width - 22;
            _lblDbPath.Top = (_statusBar.Height - _lblDbPath.Height) / 2;

            // 状态文字不能压到右边的路径上去
            _lblStatus.Width = Math.Max(0, _lblDbPath.Left - _lblStatus.Left - 12);
        }

        // ---------- 供 --uismoke 诊断模式使用 ----------

        /// <summary>功能面板数量。</summary>
        public int PanelCount
        {
            get { return _panels.Count; }
        }

        /// <summary>切换到指定面板（等同于点击左侧导航）。</summary>
        public void ShowPanel(int index)
        {
            SwitchTo(index);
        }

        /// <summary>当前显示的功能面板。</summary>
        public Control ActivePanel
        {
            get { return _active; }
        }

        /// <summary>当前面板「当前卡片」显示区的摘要，供诊断模式核对是否与数据库一致。</summary>
        public string ActiveCardSummary
        {
            get { return _active == null ? "" : _active.CardSummary; }
        }

        /// <summary>内容区可供面板使用的尺寸（已扣除内边距，停靠的子面板就是这么大）。</summary>
        public Size ContentAreaSize
        {
            get { return _contentHost.DisplayRectangle.Size; }
        }

        /// <summary>
        /// 检查停靠布局是否正确。
        ///
        /// 停靠顺序写错时，Fill 的内容区会先把整块客户区吃掉，标题栏和导航栏
        /// 只能叠在内容上面。这种错误不会抛异常，只会让界面变得不能用，
        /// 所以在这里主动检查：内容区必须与其它三条边互不重叠。
        /// </summary>
        public void VerifyDocking()
        {
            Rectangle content = _contentHost.Bounds;

            if (content.Width <= 0 || content.Height <= 0)
            {
                throw new Exception("内容区尺寸无效：" + content.Width + " x " + content.Height);
            }

            Panel[] others = new Panel[] { _header, _navHost, _statusBar };
            string[] names = new string[] { "标题栏", "左侧导航栏", "状态栏" };

            for (int i = 0; i < others.Length; i++)
            {
                Rectangle r = others[i].Bounds;

                if (r.Width <= 0 || r.Height <= 0)
                {
                    throw new Exception(names[i] + "尺寸无效：" + r.Width + " x " + r.Height);
                }
                if (r.IntersectsWith(content))
                {
                    throw new Exception(names[i] + "与内容区重叠，会遮挡主界面。" +
                        names[i] + " 位于 " + r + "，内容区位于 " + content);
                }
            }

            // 四块拼起来应当铺满整个客户区，不留空隙
            int covered = _header.Height + _statusBar.Height;
            if (content.Height + covered != ClientSize.Height)
            {
                throw new Exception("纵向没有铺满客户区：内容区 " + content.Height +
                    " + 标题栏 " + _header.Height + " + 状态栏 " + _statusBar.Height +
                    " ≠ 客户区 " + ClientSize.Height);
            }
            if (content.Width + _navHost.Width != ClientSize.Width)
            {
                throw new Exception("横向没有铺满客户区：内容区 " + content.Width +
                    " + 导航栏 " + _navHost.Width + " ≠ 客户区 " + ClientSize.Width);
            }
        }

        /// <summary>顶部与底部关键控件的位置，供诊断日志核对是否摆在了该在的地方。</summary>
        public string DescribeChrome()
        {
            return "连接按钮 " + _btnReader.Bounds +
                   " ｜ 读卡器状态 " + _lblReaderText.Bounds +
                   " ｜ 操作员 " + _lblOperator.Bounds +
                   " ｜ 数据文件 " + _lblDbPath.Bounds;
        }

        /// <summary>模拟读卡器读到一张卡，走与真实刷卡完全相同的处理路径。</summary>
        public void SimulateCardScan(string epc)
        {
            OnCardDetected(epc);
        }

        // ---------- 读卡器 ----------

        private void OnToggleReader(object sender, EventArgs e)
        {
            if (_app.Reader.IsConnected)
            {
                DisconnectReader();
                return;
            }

            SetStatus("正在连接读卡器，请稍候…");
            _btnReader.Enabled = false;
            Application.DoEvents();

            try
            {
                string error;
                bool ok = _app.Reader.ConnectAuto(ReaderBaud.Bps57600, out error);

                if (ok)
                {
                    SetStatus("读卡器已连接：" + _app.Reader.Status.Summary);
                    _app.Reader.ResetSwipeLatch();
                }
                else
                {
                    SetStatus("读卡器连接失败。");
                    UiKit.Warn(this,
                        error + "\n\n" +
                        "排查建议：\n" +
                        "1. 确认读卡器已通电，USB 线插紧；\n" +
                        "2. 确认没有其它程序（例如官方 Demo）正在占用该串口；\n" +
                        "3. 若仍失败，可先用官方 Demo 测试读卡器是否正常。\n\n" +
                        "读卡器未连接时，本系统仍可使用「手动输入 EPC」方式操作。");
                }
            }
            catch (Exception ex)
            {
                SetStatus("读卡器连接出错。");
                UiKit.Error(this, "连接读卡器时出错：" + ex.Message);
            }
            finally
            {
                _btnReader.Enabled = true;
                UpdateReaderUi();
                NotifyPanelsReaderChanged();
            }
        }

        private void DisconnectReader()
        {
            try
            {
                _app.Reader.Disconnect();
            }
            catch (Exception)
            {
            }
            SetStatus("读卡器已断开。");
            UpdateReaderUi();
            NotifyPanelsReaderChanged();
        }

        private void NotifyPanelsReaderChanged()
        {
            foreach (FunctionPanel p in _panels)
            {
                p.OnReaderStatusChanged();
            }
        }

        private void UpdateReaderUi()
        {
            bool connected = _app.Reader.IsConnected;

            _lblReaderDot.BackColor = connected ? UiKit.SuccessColor : UiKit.FaintColor;
            _lblReaderText.Text = connected ? _app.Reader.Status.Summary : "读卡器未连接";
            _btnReader.Text = connected ? "断开读卡器" : "连接读卡器";

            // 文字长度变了宽度就变了，右侧这一串要整体重排
            LayoutHeader();
        }

        /// <summary>
        /// 启动后自动连一次读卡器，省得操作员还得先找到按钮点一下。
        /// 放在后台线程做，因为扫描串口要花时间，不能让界面卡住；
        /// 连不上是常态（没插设备），只更新状态栏，不弹窗打断。
        /// </summary>
        private void TryAutoConnect()
        {
            if (!AutoConnectOnStartup || _app.Reader.IsConnected)
            {
                return;
            }

            SetStatus("正在自动连接读卡器…");
            _btnReader.Enabled = false;

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try
                {
                    string error;
                    ok = _app.Reader.ConnectAuto(ReaderBaud.Bps57600, out error);
                }
                catch (Exception)
                {
                    ok = false;
                }

                SafeInvoke(delegate
                {
                    _btnReader.Enabled = true;

                    if (ok)
                    {
                        _app.Reader.ResetSwipeLatch();
                        SetStatus("读卡器已连接：" + _app.Reader.Status.Summary);
                    }
                    else
                    {
                        SetStatus("未检测到读卡器，可点右上角「连接读卡器」重试；不接读卡器也能手动输入 EPC 操作。");
                    }

                    UpdateReaderUi();
                    NotifyPanelsReaderChanged();
                });
            });
        }

        private void StartPolling()
        {
            _polling = true;
            _pollThread = new Thread(PollLoop);
            _pollThread.IsBackground = true;
            _pollThread.Name = "RFID-Poll";
            _pollThread.Start();
        }

        private void PollLoop()
        {
            while (_polling)
            {
                try
                {
                    if (_app.Reader.IsConnected)
                    {
                        List<string> appeared = _app.Reader.Poll();
                        if (appeared.Count > 0)
                        {
                            string epc = appeared[0];
                            SafeInvoke(delegate { OnCardDetected(epc); });
                        }
                    }
                }
                catch (Exception)
                {
                    // 轮询异常不应终止线程；设备抖动下一轮会自行恢复
                }

                Thread.Sleep(200);
            }
        }

        private void SafeInvoke(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                BeginInvoke(action);
            }
            catch (Exception)
            {
                // 窗口正在关闭，忽略
            }
        }

        private void OnCardDetected(string epc)
        {
            _lastEpc = epc;

            if (_active != null)
            {
                _active.OnCardScanned(epc);
            }

            // 读卡器连接状态下，把读到的卡提示出来
            SetStatus("读到卡片：" + epc);
        }

        // ---------- 关闭 ----------

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _polling = false;

            if (_pollThread != null && _pollThread.IsAlive)
            {
                // 轮询线程是后台线程，稍等一下让它自己退出即可，不必强杀
                _pollThread.Join(400);
                _pollThread = null;
            }

            try
            {
                _app.Reader.Disconnect();
            }
            catch (Exception)
            {
            }

            if (_app.CurrentOperator != null)
            {
                e.Cancel = false;
            }
        }
    }
}
