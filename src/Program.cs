using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MealCardSystem.Data;
using MealCardSystem.Model;
using MealCardSystem.Service;
using MealCardSystem.UI;
using MealCardSystem.Util;

namespace MealCardSystem
{
    /// <summary>
    /// 程序入口：打开数据库 → 操作员登录 → 进入主界面。
    /// </summary>
    internal static class Program
    {
        /// <summary>单实例互斥体。同一台收银机上开两个实例会让库存/余额看着不一致。</summary>
        private static Mutex _instanceLock;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 兜底：任何未捕获异常都弹窗说明，而不是让程序静默退出
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            string dbPath = ResolveDatabasePath(args);

            // 诊断开关：不弹任何窗口，把界面构造一遍，用于快速确认改动没有把界面写崩
            if (HasArgument(args, "--uismoke"))
            {
                RunUiSmokeTest();
                return;
            }

            if (!AcquireSingleInstance(dbPath))
            {
                MessageBox.Show(
                    "本系统已经有一个窗口在运行了。\n\n" +
                    "请切换到已打开的窗口继续操作，不要同时开两个。",
                    "食堂饭卡管理系统", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppSession app = null;
            try
            {
                app = AppSession.Start(dbPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "无法打开数据库，程序无法启动。\n\n" +
                    "数据库位置：" + dbPath + "\n" +
                    "错误信息：" + ex.Message + "\n\n" +
                    "如果是权限问题，请把程序放在可写的目录下再运行。",
                    "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                // 先登录，登录不通过就不进主界面
                using (LoginForm login = new LoginForm(app))
                {
                    if (login.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }
                }

                Application.Run(new MainForm(app));
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序运行时发生错误：\n\n" + ex.Message,
                                "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (app != null)
                {
                    app.Dispose();
                }
                if (_instanceLock != null)
                {
                    _instanceLock.ReleaseMutex();
                    _instanceLock.Close();
                    _instanceLock = null;
                }
            }
        }

        /// <summary>
        /// 数据库位置：默认在程序目录的 data\mealcard.db，
        /// 也支持命令行 --db=路径 指定（便于把数据放到共享盘）。
        /// </summary>
        private static string ResolveDatabasePath(string[] args)
        {
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a != null && a.StartsWith("--db=", StringComparison.OrdinalIgnoreCase))
                    {
                        string custom = a.Substring(5).Trim().Trim('"');
                        if (custom.Length > 0)
                        {
                            return Path.GetFullPath(custom);
                        }
                    }
                }
            }
            return Database.DefaultPath;
        }

        private static bool HasArgument(string[] args, string flag)
        {
            if (args == null)
            {
                return false;
            }
            foreach (string a in args)
            {
                if (a != null && string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 界面冒烟测试（MealCardSystem.exe --uismoke）。
        ///
        /// 在临时数据库上走一遍「发卡 → 充值 → 消费 → 挂失 → 注销」，
        /// 然后把登录窗口和主窗口连同全部六个功能面板都构造并激活一次。
        /// 界面代码里的空引用、越界这类问题会在这里立刻暴露，
        /// 不需要真机、也不需要人工点击。
        /// </summary>
        private static void RunUiSmokeTest()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), "mealcard_uismoke.db");

            // 本程序是窗口程序，没有控制台可输出，结果写进临时日志供构建脚本查看
            System.Text.StringBuilder log = new System.Text.StringBuilder();
            string error = null;

            try
            {
                DeleteIfExists(tempDb);

                using (AppSession app = AppSession.Start(tempDb))
                {
                    app.CurrentOperator = app.Operators.Authenticate("admin", "admin123");
                    if (app.CurrentOperator == null)
                    {
                        throw new Exception("默认管理员 admin/admin123 登录失败。");
                    }
                    long op = app.CurrentOperator.Id;

                    // 造点数据，让各个列表都有内容可渲染
                    const string epcA = "E2801160600002054E3A1B2C";
                    const string epcB = "E2801160600002054E3A1B2D";

                    app.CardService.IssueCard(epcA, "张三", "A001", "后厨", 10000, op);
                    app.AccountService.Recharge(epcA, 5000, op);
                    app.AccountService.Consume(epcA, 800, op);
                    log.AppendLine("  数据准备：发卡 / 充值 / 消费 通过");

                    Card b = app.CardService.IssueCard(epcB, "李四", "A002", "前厅", 0, op);
                    app.CardService.SetLost(b.Id, true, op);

                    string epcC = "E2801160600002054E3A1B2E";
                    Card c = app.CardService.IssueCard(epcC, "王五", "A003", "库房", 300, op);
                    app.CardService.CancelCard(c.Id, op);
                    log.AppendLine("  数据准备：挂失 / 注销 通过");

                    using (LoginForm login = new LoginForm(app))
                    {
                        login.CreateControl();
                        log.AppendLine("  登录窗口构造通过");
                    }

                    using (MainForm main = new MainForm(app))
                    {
                        // 挪到屏幕外真正显示一次：只有窗口真的显示了，
                        // WinForms 才会走完整的停靠与 Resize 布局，各面板里
                        // 算错尺寸这类问题才会暴露出来。
                        main.StartPosition = FormStartPosition.Manual;
                        main.Location = new Point(-6000, -6000);

                        // 诊断模式不碰真实串口：自动连接会去扫 COM 口，既慢又依赖硬件
                        main.AutoConnectOnStartup = false;
                        main.Show();
                        Application.DoEvents();

                        // 停靠顺序错了不会抛异常，只会让导航栏压住主界面，这里主动查
                        main.VerifyDocking();
                        log.AppendLine("  主窗口停靠布局通过（标题栏 / 导航栏 / 状态栏互不遮挡）");

                        // 标题栏和状态栏里的按钮/标签也要查：它们如果跑到容器外面，
                        // 界面上就是「这个按钮根本不存在」，不报任何错。
                        List<string> chromeProblems = new List<string>();
                        int chromeCount = CheckLayout(main, -1, chromeProblems);
                        if (chromeProblems.Count > 0)
                        {
                            throw new Exception("主窗口顶部/底部控件排版错误：\n  " +
                                                string.Join("\n  ", chromeProblems.ToArray()));
                        }
                        log.AppendLine("  标题栏与状态栏排版通过（检查了 " + chromeCount + " 个控件）");
                        log.AppendLine("    " + main.DescribeChrome());
                        // 逐个切换到六个面板，触发各自的 OnActivated / 列表刷新
                        for (int i = 0; i < main.PanelCount; i++)
                        {
                            main.ShowPanel(i);
                            Application.DoEvents();
                            // 面板必须真的被拉伸填满内容区，否则说明停靠失效
                            Control active = main.ActivePanel;
                            Size host = main.ContentAreaSize;
                            if (active == null || active.Width != host.Width || active.Height != host.Height)
                            {
                                throw new Exception("面板 " + i + " 没有填满内容区：面板 " +
                                    (active == null ? "(无)" : active.Width + " x " + active.Height) +
                                    "，内容区 " + host.Width + " x " + host.Height);
                            }

                            List<string> problems = new List<string>();
                            int visited = CheckLayout(main, i, problems);
                            if (problems.Count > 0)
                            {
                                throw new Exception("面板 " + i + " 排版错误：\n  " +
                                                    string.Join("\n  ", problems.ToArray()));
                            }

                            log.AppendLine("  面板 " + i + " 激活与排版通过（检查了 " + visited + " 个控件）");
                        }

                        // 再模拟读到卡片，触发面板的刷卡渲染分支
                        for (int i = 0; i < main.PanelCount; i++)
                        {
                            main.ShowPanel(i);
                            main.SimulateCardScan(epcA);
                            Application.DoEvents();

                            List<string> scanProblems = new List<string>();
                            CheckLayout(main, i, scanProblems);
                            if (scanProblems.Count > 0)
                            {
                                throw new Exception("面板 " + i + " 刷卡后排版错误：\n  " +
                                                    string.Join("\n  ", scanProblems.ToArray()));
                            }

                            log.AppendLine("  面板 " + i + " 刷卡渲染通过");
                        }

                        // ---------- 回归：切页面之后卡片状态必须是最新的 ----------
                        //
                        // 每个面板各有一个「当前卡片」显示区。余额往往是在别的页面上
                        // 被改掉的（在消费页扣款、在卡片管理页注销），切回来时必须
                        // 重新读一次数据库，否则显示的还是上次离开时的旧余额。
                        main.ShowPanel(0);
                        main.SimulateCardScan(epcA);
                        Application.DoEvents();

                        long beforeCents = app.Cards.GetByEpc(epcA).BalanceCents;
                        app.AccountService.Consume(epcA, 100, op);
                        string expectedBalance = Money.Format(beforeCents - 100);

                        main.ShowPanel(2); // 先切走
                        Application.DoEvents();
                        main.ShowPanel(0); // 再切回来
                        Application.DoEvents();

                        string shown = main.ActiveCardSummary;
                        if (shown.IndexOf(expectedBalance) < 0)
                        {
                            throw new Exception("切换页面后卡片余额没有刷新：期望显示 " +
                                expectedBalance + "，实际显示「" + shown + "」");
                        }
                        log.AppendLine("  切走再切回后余额刷新为最新值（" + expectedBalance + " 元）");

                        // ---------- 回归：注销后本地不再保留卡片信息 ----------
                        //
                        // epcC 在数据准备阶段已被注销，此时它应当是一张空白卡，
                        // 任何页面都不该再显示它的旧持卡人与余额。
                        main.ShowPanel(0);
                        main.SimulateCardScan(epcC);
                        Application.DoEvents();

                        string cancelled = main.ActiveCardSummary;
                        if (cancelled.IndexOf("未登记") < 0)
                        {
                            throw new Exception("注销后的卡仍然被当成已登记：显示内容「" +
                                                cancelled + "」");
                        }
                        log.AppendLine("  注销后的卡显示为未登记的空白卡「" + cancelled + "」");

                        // 而且立刻就能重新发卡
                        Card again = app.CardService.IssueCard(epcC, "孙七", "A009", "面点", 0, op);
                        if (again == null || again.CardNo == c.CardNo)
                        {
                            throw new Exception("注销后的卡重新发放失败，或复用了旧卡号 " +
                                                (again == null ? "(未发卡)" : again.CardNo));
                        }
                        log.AppendLine("  注销后的卡立刻可以重新发放，新卡号 " + again.CardNo);

                        // ---------- 提示框排版 ----------
                        //
                        // 提示框已经不用系统 MessageBox，改成自绘的了，所以排版得自己查。
                        // 用的是各面板真实的提示文案，不是编的样例。
                        CheckDialogs(log);

                        main.Hide();
                        main.Close();
                    }

                    log.AppendLine("界面冒烟测试通过：登录窗口与全部功能面板均正常构造。");
                }
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                log.AppendLine("界面冒烟测试失败：");
                log.AppendLine(ex.ToString());
            }
            finally
            {
                DeleteIfExists(tempDb);
            }

            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "mealcard_uismoke.log"),
                                  log.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
            }

            Environment.ExitCode = (error == null) ? 0 : 1;
        }

        /// <summary>
        /// 检查自绘提示框的排版。
        ///
        /// 这些文案就是各面板真正传进来的那几句，不是另编的样例 ——
        /// 拆行、拆「标签：值」都依赖文案的实际写法，用假数据测不出问题。
        /// 主要盯三件事：控件不许跑到框外、框子尺寸要合理、
        /// 字号确实比系统 MessageBox（约 9pt）大。
        /// </summary>
        private static void CheckDialogs(System.Text.StringBuilder log)
        {
            string[][] cases = new string[][]
            {
                new string[] { "扣款成功",
                    "扣款成功\n\n持卡人：张三\n本次消费：8.00 元\n卡内余额：92.00 元" },
                new string[] { "充值成功",
                    "充值成功\n\n持卡人：张三\n本次充值：100.00 元\n卡内余额：192.00 元" },
                new string[] { "注销确认",
                    "确认注销这张卡并退还余额？\n\n卡号：10001\n持卡人：张三\n应退余额：192.00 元\n\n" +
                    "注销后本地的卡片信息会被删除，这张卡就变回一张空白卡，\n" +
                    "可以立刻重新发给别人（会分配一个新卡号）。\n" +
                    "历史流水会保留，不会丢失。\n" +
                    "请确认已将余额退还给持卡人。" },
                new string[] { "发卡成功",
                    "发卡成功\n\n卡号：10001\n持卡人：张三\n工号：A001\n部门：后厨\n卡内余额：0.00 元" },
                new string[] { "余额不足",
                    "余额不足。\n\n持卡人：张三\n当前余额：5.00 元\n本次消费：8.00 元\n还差：3.00 元" },
                new string[] { "读卡器连不上",
                    "打开串口失败。\n\n排查建议：\n1. 确认读卡器已通电，USB 线插紧；\n" +
                    "2. 确认没有其它程序（例如官方 Demo）正在占用该串口；\n" +
                    "3. 若仍失败，可先用官方 Demo 测试读卡器是否正常。\n\n" +
                    "读卡器未连接时，本系统仍可使用「手动输入 EPC」方式操作。" },
                new string[] { "单行警告", "充值金额必须大于 0。" },
                new string[] { "导出确认",
                    "导出成功，共 128 条流水。\n\nD:\\Mino's code\\robot2\\报表.csv\n\n是否现在打开该文件？" }
            };

            float maxFont = 0f;

            foreach (string[] c in cases)
            {
                bool cancellable = c[0].EndsWith("确认", StringComparison.Ordinal)
                                   || c[0] == "导出确认";

                using (MessageDialog d = MessageDialog.CreateForDiagnostics(
                           "提示", c[1], MessageBoxIcon.Information, cancellable))
                {
                    Size size = d.ClientSize;

                    if (size.Width < 400 || size.Width > 900 || size.Height < 120 || size.Height > 700)
                    {
                        throw new Exception("提示框「" + c[0] + "」尺寸不合理：" +
                                            size.Width + " x " + size.Height);
                    }

                    // 逐层往下查：正文面板自己要在框内，面板里的每一行也要在面板内。
                    // 只看直接子控件的话，正文被面板裁掉这种事情查不出来，
                    // 字号也只会看到按钮那一层（正文都在面板里面）。
                    maxFont = CheckTree(d, c[0], size, maxFont);
                }

                log.AppendLine("  提示框「" + c[0] + "」排版通过");
            }

            // 旧版系统 MessageBox 是 9pt 左右，改完必须明显大过它
            if (maxFont < 15f)
            {
                throw new Exception("提示框字号没有变大，最大只有 " + maxFont + "pt");
            }
            log.AppendLine("  提示框最大字号 " + maxFont + "pt（系统 MessageBox 约 9pt）");
        }

        /// <summary>
        /// 递归检查提示框里的每一个控件：都要落在各自父容器的客户区里，
        /// 顺便把见到的最大字号带回来（正文在面板里，只看顶层是看不到的）。
        /// </summary>
        private static float CheckTree(Control parent, string caseName, Size box, float maxFont)
        {
            foreach (Control ctl in parent.Controls)
            {
                CheckInside(ctl, parent, caseName, box);

                if ((ctl is Label || ctl is Button) && ctl.Font.Size > maxFont)
                {
                    maxFont = ctl.Font.Size;
                }

                maxFont = CheckTree(ctl, caseName, box, maxFont);
            }
            return maxFont;
        }

        /// <summary>
        /// 控件必须完整落在父容器里。跑出去的部分会被父容器裁掉，
        /// 界面上就是「这段话莫名其妙少了一半」，不会报任何错。
        /// </summary>
        private static void CheckInside(Control c, Control parent, string caseName, Size box)
        {
            if (c.Left < 0 || c.Top < 0
                || c.Right > parent.ClientSize.Width || c.Bottom > parent.ClientSize.Height)
            {
                throw new Exception("提示框「" + caseName + "」里的 " + c.GetType().Name +
                                    "（" + (c.Text ?? "") + "）超出父容器：" + c.Bounds +
                                    "，父容器只有 " + parent.ClientSize +
                                    (parent is Form ? "" : "（整个框 " + box + "）"));
            }
        }

        /// <summary>
        /// 递归检查可见控件的尺寸。窗口被拉伸前算出来的尺寸可能是负数，
        /// WinForms 会悄悄吞掉而不报错，这里主动把它揪出来。
        /// </summary>
        private static int CheckLayout(Control root, int panelIndex, List<string> problems)
        {
            int visited = 0;

            // 用 DisplayRectangle 而不是 ClientRectangle：AutoScroll 面板的
            // 可滚动区域比可视区域大，用后者会把正常控件误判成越界。
            Rectangle area = root.DisplayRectangle;

            foreach (Control c in root.Controls)
            {
                if (!c.Visible)
                {
                    continue;
                }

                if (c.Width < 0 || c.Height < 0)
                {
                    problems.Add("面板 " + panelIndex + " 内的 " + c.GetType().Name +
                                 "「" + c.Text + "」尺寸为负数：" + c.Width + " x " + c.Height);
                }

                // 控件必须落在父容器里，跑到外面就等于从界面上消失了
                if (!area.Contains(c.Bounds))
                {
                    problems.Add("面板 " + panelIndex + " 内的 " + c.GetType().Name +
                                 "「" + c.Text + "」跑到父容器外面了：" + c.Bounds +
                                 "，父容器只有 " + area);
                }

                visited++;
                visited += CheckLayout(c, panelIndex, problems);
            }

            return visited;
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 临时文件删不掉不影响测试结论
            }
        }

        private static bool AcquireSingleInstance(string dbPath)
        {
            try
            {
                bool created;
                // 不同数据库文件允许各开一个实例
                string name = "MealCardSystem_" + dbPath.Replace('\\', '_').Replace(':', '_');
                _instanceLock = new Mutex(true, name, out created);
                return created;
            }
            catch (Exception)
            {
                // 拿不到互斥体不该阻止启动，放行
                return true;
            }
        }

        // ---------- 全局异常 ----------

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ReportCrash(e.Exception);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportCrash(e.ExceptionObject as Exception);
        }

        private static void ReportCrash(Exception ex)
        {
            string message = ex == null ? "未知错误。" : ex.Message;

            // 32 位 DLL 加载失败是最容易踩的坑，单独给一句人话提示
            if (ex is BadImageFormatException)
            {
                message = "读卡器驱动是 32 位的，程序必须以 32 位方式运行。\n\n" +
                          "请使用随附的 build.bat / run.bat 启动程序，不要手工用其它方式编译。";
            }

            try
            {
                MessageBox.Show("程序遇到一个未预期的问题：\n\n" + message,
                                "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // 连弹窗都失败时就只能放弃，避免死循环
            }
        }
    }
}
