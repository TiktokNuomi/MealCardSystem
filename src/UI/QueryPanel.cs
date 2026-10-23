using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MealCardSystem.Model;
using MealCardSystem.Service;
using MealCardSystem.Util;

namespace MealCardSystem.UI
{
    /// <summary>
    /// 查询面板：上半部分是「卡片查询」，下半部分是「交易流水查询与导出」。
    /// 两块内容用顶部的分段按钮切换，避免用 TabControl 破坏整体配色。
    /// </summary>
    public class QueryPanel : FunctionPanel
    {
        /// <summary>流水查询的类型下拉项。第一项为 null，表示不限类型。</summary>
        private static readonly TransactionType?[] TypeFilter = new TransactionType?[]
        {
            null,
            TransactionType.Issue,
            TransactionType.Consume,
            TransactionType.Recharge,
            TransactionType.Refund
        };

        private Button _tabCards;
        private Button _tabTx;
        private Panel _pageCards;
        private Panel _pageTx;

        // ---- 卡片查询 ----
        private TextBox _txtCardKeyword;
        private Label _lblCardStats;
        private DataGridView _gridCards;
        private Label _lblCardDetail;
        private DataGridView _gridCardTx;
        private Card _selectedCard;

        // ---- 流水查询 ----
        private DateTimePicker _dpFrom;
        private DateTimePicker _dpTo;
        private ComboBox _cboType;
        private TextBox _txtTxKeyword;
        private Label _lblTxSummary;
        private DataGridView _gridTx;
        private List<Transaction> _txRows = new List<Transaction>();

        public QueryPanel(AppSession app, MainForm host) : base(app, host)
        {
            // 本面板自行控制子控件尺寸，不需要滚动条
            AutoScroll = false;

            // 构造阶段面板还没被父容器拉伸，Height 还是控件的默认值（100）。
            // 按那个高度算出来的子控件尺寸会是负数——虽然停靠后的 Resize 会把
            // 它们纠正回来，但没必要让代码先经过一个非法状态，这里先给一个
            // 与主窗口相符的尺寸。
            Size = new Size(900, 600);

            BuildUi();
            SwitchPage(0);
        }

        // ================= 界面构建 =================

        private void BuildUi()
        {
            _tabCards = MakeTab("卡片查询", 0);
            _tabTx = MakeTab("交易流水 / 报表", 1);

            _pageCards = new Panel();
            _pageCards.BackColor = UiKit.BackColor;
            _pageCards.Location = new Point(0, 44);
            _pageCards.Size = new Size(Width, H - 44);
            _pageCards.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_pageCards);

            _pageTx = new Panel();
            _pageTx.BackColor = UiKit.BackColor;
            _pageTx.Location = new Point(0, 44);
            _pageTx.Size = new Size(Width, H - 44);
            _pageTx.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_pageTx);

            BuildCardsPage();
            BuildTxPage();

            Resize += delegate
            {
                _pageCards.Size = new Size(Width, H - 44);
                _pageTx.Size = new Size(Width, H - 44);
            };
        }

        /// <summary>面板可用高度。给窗口留出余量，避免算出负数的控件尺寸。</summary>
        private int H
        {
            get { return Math.Max(Height, 300); }
        }

        private Button MakeTab(string caption, int index)
        {
            Button b = UiKit.Button(caption, "normal");
            b.Size = new Size(150, 34);
            b.Location = new Point(index * 156, 0);
            b.Click += delegate { SwitchPage(index); };
            Controls.Add(b);
            return b;
        }

        private void SwitchPage(int index)
        {
            _pageCards.Visible = (index == 0);
            _pageTx.Visible = (index == 1);

            StyleTab(_tabCards, index == 0);
            StyleTab(_tabTx, index == 1);

            if (index == 0)
            {
                RefreshCardList();
            }
            else
            {
                RefreshTxList();
            }
        }

        private static void StyleTab(Button b, bool active)
        {
            b.BackColor = active ? UiKit.PrimaryColor : Color.White;
            b.ForeColor = active ? Color.White : UiKit.TextColor;
            b.FlatAppearance.BorderSize = active ? 0 : 1;
            b.FlatAppearance.BorderColor = UiKit.BorderColor;
            b.Cursor = Cursors.Hand;
        }

        // ---------------- 卡片查询页 ----------------

        private void BuildCardsPage()
        {
            Label l = UiKit.Label("卡号 / 姓名 / 工号 / EPC", UiKit.FontSmall, UiKit.MutedColor);
            l.Location = new Point(0, 8);
            _pageCards.Controls.Add(l);

            _txtCardKeyword = UiKit.TextBox();
            _txtCardKeyword.Location = new Point(170, 5);
            _txtCardKeyword.Width = 220;
            _txtCardKeyword.Height = 26;
            _txtCardKeyword.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    RefreshCardList();
                }
            };
            _txtCardKeyword.TextChanged += delegate { RefreshCardList(); };
            _pageCards.Controls.Add(_txtCardKeyword);

            Button btnAll = UiKit.Button("显示全部", "normal");
            btnAll.Size = new Size(90, 26);
            btnAll.Font = UiKit.FontSmall;
            btnAll.Location = new Point(400, 4);
            btnAll.Click += delegate
            {
                _txtCardKeyword.Clear();
                RefreshCardList();
            };
            _pageCards.Controls.Add(btnAll);

            _lblCardStats = UiKit.Label("", UiKit.FontSmall, UiKit.MutedColor);
            _lblCardStats.Location = new Point(506, 8);
            _pageCards.Controls.Add(_lblCardStats);

            _gridCards = new DataGridView();
            UiKit.StyleGrid(_gridCards);
            _gridCards.Location = new Point(0, 38);
            _gridCards.Size = new Size(Width, 200);
            _gridCards.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _gridCards.Columns.Add("cardno", "卡号");
            _gridCards.Columns.Add("holder", "持卡人");
            _gridCards.Columns.Add("holderid", "工号");
            _gridCards.Columns.Add("dept", "部门");
            _gridCards.Columns.Add("balance", "余额(元)");
            _gridCards.Columns.Add("status", "状态");
            _gridCards.Columns.Add("epc", "EPC");
            _gridCards.Columns["balance"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _gridCards.SelectionChanged += delegate { OnCardRowSelected(); };
            _pageCards.Controls.Add(_gridCards);

            Label cap = UiKit.Label("该卡的交易流水（最近 50 笔）", UiKit.FontBold, UiKit.TextColor);
            cap.Location = new Point(0, 248);
            _pageCards.Controls.Add(cap);

            _lblCardDetail = UiKit.Label("请在上方列表中选择一张卡片。", UiKit.FontSmall, UiKit.MutedColor);
            _lblCardDetail.Location = new Point(240, 250);
            _pageCards.Controls.Add(_lblCardDetail);

            _gridCardTx = new DataGridView();
            UiKit.StyleGrid(_gridCardTx);
            _gridCardTx.Location = new Point(0, 274);
            _gridCardTx.Size = new Size(Width, H - 44 - 274 - 4);
            _gridCardTx.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _gridCardTx.Columns.Add("time", "时间");
            _gridCardTx.Columns.Add("type", "类型");
            _gridCardTx.Columns.Add("amount", "金额(元)");
            _gridCardTx.Columns.Add("sign", "收支");
            _gridCardTx.Columns.Add("balance", "交易后余额(元)");
            _gridCardTx.Columns.Add("op", "操作员");
            _gridCardTx.Columns.Add("remark", "备注");
            _gridCardTx.Columns["amount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _gridCardTx.Columns["balance"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _pageCards.Controls.Add(_gridCardTx);

            _pageCards.Resize += delegate
            {
                _gridCards.Width = _pageCards.Width;
                _gridCardTx.Width = _pageCards.Width;
                _gridCardTx.Height = _pageCards.Height - 278;
            };
        }

        private void RefreshCardList()
        {
            _gridCards.Rows.Clear();

            List<Card> cards = App.Cards.Search(_txtCardKeyword.Text, null);

            foreach (Card c in cards)
            {
                int idx = _gridCards.Rows.Add(
                    c.CardNo, c.HolderName, c.HolderId, c.Department,
                    c.BalanceText, c.StatusText, c.Epc);

                Color color = UiKit.TextColor;
                if (c.Status == CardStatus.Lost)
                {
                    color = UiKit.WarningColor;
                }
                else if (c.Status == CardStatus.Cancelled)
                {
                    color = UiKit.FaintColor;
                }
                _gridCards.Rows[idx].DefaultCellStyle.ForeColor = color;
            }

            int total, normal, lost, cancelled;
            long totalBalance;
            App.Cards.GetStatistics(out total, out normal, out lost, out cancelled, out totalBalance);

            _lblCardStats.Text = "共 " + total + " 张　正常 " + normal + "　挂失 " + lost +
                                 "　注销 " + cancelled + "　在用卡余额合计 " + Money.Format(totalBalance) + " 元";

            _selectedCard = null;
            _gridCardTx.Rows.Clear();
            _lblCardDetail.Text = cards.Count == 0
                ? "没有符合条件的卡片。"
                : "请在上方列表中选择一张卡片。";
        }

        private void OnCardRowSelected()
        {
            if (_gridCards.SelectedRows.Count == 0)
            {
                return;
            }

            object epcValue = _gridCards.SelectedRows[0].Cells["epc"].Value;
            if (epcValue == null)
            {
                return;
            }

            Card card = App.CardService.FindByEpc(Convert.ToString(epcValue));
            if (card == null)
            {
                return;
            }

            _selectedCard = card;
            _lblCardDetail.Text =
                "持卡人 " + card.HolderName +
                "　卡号 " + card.CardNo +
                "　余额 " + card.BalanceText + " 元" +
                "　状态 " + card.StatusText +
                "　登记于 " + card.CreatedAt.ToString("yyyy-MM-dd HH:mm");

            RefreshCardTransactions(card);
        }

        private void RefreshCardTransactions(Card card)
        {
            _gridCardTx.Rows.Clear();
            List<Transaction> rows = App.Transactions.QueryByCard(card.Id, 50);

            foreach (Transaction t in rows)
            {
                int idx = _gridCardTx.Rows.Add(
                    t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    t.TypeText,
                    t.AmountText,
                    t.IsIncome ? "收入" : "支出",
                    t.BalanceAfterText,
                    t.OperatorName,
                    t.Remark);

                _gridCardTx.Rows[idx].Cells["sign"].Style.ForeColor =
                    t.IsIncome ? UiKit.SuccessColor : UiKit.DangerColor;
            }

            if (rows.Count == 0)
            {
                _lblCardDetail.Text += "　（该卡暂无流水）";
            }
        }

        // ---------------- 流水查询页 ----------------

        private void BuildTxPage()
        {
            // ---- 第一行：日期区间与类型 ----
            Label l1 = UiKit.Label("起始日期", UiKit.FontSmall, UiKit.MutedColor);
            l1.Location = new Point(0, 10);
            _pageTx.Controls.Add(l1);

            _dpFrom = MakePicker(new Point(70, 6));
            _dpFrom.Value = DateTime.Today.AddDays(-30);

            Label l2 = UiKit.Label("结束日期", UiKit.FontSmall, UiKit.MutedColor);
            l2.Location = new Point(216, 10);
            _pageTx.Controls.Add(l2);

            _dpTo = MakePicker(new Point(286, 6));
            _dpTo.Value = DateTime.Today;

            Label l3 = UiKit.Label("类型", UiKit.FontSmall, UiKit.MutedColor);
            l3.Location = new Point(432, 10);
            _pageTx.Controls.Add(l3);

            _cboType = UiKit.ComboBox();
            _cboType.Location = new Point(472, 6);
            _cboType.Width = 130;
            _cboType.Items.AddRange(new object[]
            {
                "全部类型", "开卡", "消费", "充值", "退卡退款"
            });
            _cboType.SelectedIndex = 0;
            _pageTx.Controls.Add(_cboType);

            // ---- 第二行：关键字与操作按钮 ----
            Label l4 = UiKit.Label("关键字", UiKit.FontSmall, UiKit.MutedColor);
            l4.Location = new Point(0, 50);
            _pageTx.Controls.Add(l4);

            _txtTxKeyword = UiKit.TextBox();
            _txtTxKeyword.Location = new Point(70, 46);
            _txtTxKeyword.Width = 200;
            _txtTxKeyword.Height = 26;
            _txtTxKeyword.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    RefreshTxList();
                }
            };
            _pageTx.Controls.Add(_txtTxKeyword);

            Button btnQuery = UiKit.Button("查询", "primary");
            btnQuery.Size = new Size(90, 28);
            btnQuery.Font = UiKit.FontNormal;
            btnQuery.Location = new Point(282, 45);
            btnQuery.Click += delegate { RefreshTxList(); };
            _pageTx.Controls.Add(btnQuery);

            Button btnExport = UiKit.Button("导出 CSV 报表", "success");
            btnExport.Size = new Size(140, 28);
            btnExport.Font = UiKit.FontNormal;
            btnExport.Location = new Point(382, 45);
            btnExport.Click += OnExport;
            _pageTx.Controls.Add(btnExport);

            Label hint = UiKit.Label("提示：取消勾选日期前的方框即表示「不限日期」。",
                                     UiKit.FontSmall, UiKit.FaintColor);
            hint.Location = new Point(536, 50);
            _pageTx.Controls.Add(hint);

            _lblTxSummary = UiKit.Label("", UiKit.FontSmall, UiKit.TextColor);
            _lblTxSummary.Location = new Point(0, 86);
            _pageTx.Controls.Add(_lblTxSummary);

            _gridTx = new DataGridView();
            UiKit.StyleGrid(_gridTx);
            _gridTx.Location = new Point(0, 110);
            _gridTx.Size = new Size(Width, H - 44 - 110 - 4);
            _gridTx.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _gridTx.Columns.Add("time", "交易时间");
            _gridTx.Columns.Add("type", "类型");
            _gridTx.Columns.Add("cardno", "卡号");
            _gridTx.Columns.Add("holder", "持卡人");
            _gridTx.Columns.Add("amount", "金额(元)");
            _gridTx.Columns.Add("sign", "收支");
            _gridTx.Columns.Add("balance", "交易后余额(元)");
            _gridTx.Columns.Add("op", "操作员");
            _gridTx.Columns.Add("remark", "备注");
            _gridTx.Columns["amount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _gridTx.Columns["balance"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _pageTx.Controls.Add(_gridTx);

            _pageTx.Resize += delegate
            {
                _gridTx.Width = _pageTx.Width;
                _gridTx.Height = _pageTx.Height - 114;
            };
        }

        private DateTimePicker MakePicker(Point location)
        {
            DateTimePicker dp = new DateTimePicker();
            dp.Format = DateTimePickerFormat.Short;
            dp.ShowCheckBox = true;
            dp.Checked = true;
            dp.Font = UiKit.FontNormal;
            dp.Width = 132;
            dp.Location = location;
            _pageTx.Controls.Add(dp);
            return dp;
        }

        private DateTime? FromDate
        {
            get { return _dpFrom.Checked ? (DateTime?)_dpFrom.Value.Date : null; }
        }

        private DateTime? ToDate
        {
            get { return _dpTo.Checked ? (DateTime?)_dpTo.Value.Date : null; }
        }

        private void RefreshTxList()
        {
            DateTime? from = FromDate;
            DateTime? to = ToDate;

            if (from.HasValue && to.HasValue && from.Value > to.Value)
            {
                UiKit.Warn(this, "起始日期不能晚于结束日期。");
                return;
            }

            int typeIndex = _cboType.SelectedIndex;
            if (typeIndex < 0 || typeIndex >= TypeFilter.Length)
            {
                typeIndex = 0;
            }

            _txRows = App.ReportService.Query(from, to, TypeFilter[typeIndex], _txtTxKeyword.Text, 1000);

            _gridTx.Rows.Clear();
            foreach (Transaction t in _txRows)
            {
                int idx = _gridTx.Rows.Add(
                    t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    t.TypeText,
                    t.CardNo,
                    t.HolderName,
                    t.AmountText,
                    t.IsIncome ? "收入" : "支出",
                    t.BalanceAfterText,
                    t.OperatorName,
                    t.Remark);

                _gridTx.Rows[idx].Cells["sign"].Style.ForeColor =
                    t.IsIncome ? UiKit.SuccessColor : UiKit.DangerColor;
            }

            long issue, consume, recharge, refund;
            int count;
            App.Transactions.GetSummary(from, to, out issue, out consume, out recharge, out refund, out count);

            // 净流入 = 收到现金（开卡 + 充值）− 退还现金（退款）；消费不涉及现金进出
            long net = issue + recharge - refund;

            _lblTxSummary.Text =
                "共 " + count + " 笔流水（下表显示 " + _txRows.Count + " 条）　" +
                "开卡 " + Money.Format(issue) + " 元　" +
                "充值 " + Money.Format(recharge) + " 元　" +
                "消费 " + Money.Format(consume) + " 元　" +
                "退卡退款 " + Money.Format(refund) + " 元　" +
                "现金净收入 " + Money.Format(net) + " 元";

            Host.SetStatus(_lblTxSummary.Text);
        }

        private void OnExport(object sender, EventArgs e)
        {
            DateTime? from = FromDate;
            DateTime? to = ToDate;

            if (from.HasValue && to.HasValue && from.Value > to.Value)
            {
                UiKit.Warn(this, "起始日期不能晚于结束日期。");
                return;
            }

            int typeIndex = _cboType.SelectedIndex;
            if (typeIndex < 0 || typeIndex >= TypeFilter.Length)
            {
                typeIndex = 0;
            }

            List<Transaction> rows = App.ReportService.Query(from, to, TypeFilter[typeIndex], _txtTxKeyword.Text, 0);

            if (rows.Count == 0)
            {
                UiKit.Warn(this, "当前条件下没有可导出的流水。");
                return;
            }

            string path;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "导出交易流水报表";
                dlg.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                dlg.DefaultExt = "csv";
                dlg.FileName = "交易流水_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                path = dlg.FileName;
            }

            try
            {
                App.ReportService.ExportTransactionsCsvWithSummary(path, rows, from, to);

                Host.SetStatus("报表已导出：" + path);

                if (UiKit.Confirm(this,
                        "导出成功，共 " + rows.Count + " 条流水。\n\n" + path + "\n\n是否现在打开该文件？"))
                {
                    try
                    {
                        Process.Start(path);
                    }
                    catch (Exception ex)
                    {
                        UiKit.Warn(this, "无法自动打开文件，请手动到上述路径查看。\n\n" + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                UiKit.Error(this, "导出失败：" + ex.Message);
            }
        }

        // ---------------- 生命周期 ----------------

        public override void OnActivated()
        {
            if (_pageTx.Visible)
            {
                RefreshTxList();
            }
            else
            {
                RefreshCardList();
            }
        }

        public override void OnReaderStatusChanged()
        {
            // 查询面板不依赖读卡器，无需处理
        }
    }
}
