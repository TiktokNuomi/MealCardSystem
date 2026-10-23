using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MealCardSystem.Model;
using MealCardSystem.Service;
using MealCardSystem.Util;

namespace MealCardSystem.UI
{
    /// <summary>充值面板。</summary>
    public class RechargePanel : FunctionPanel
    {
        private CardScanBox _scan;
        private TextBox _txtAmount;
        private Button _btnConfirm;
        private Label _lblResult;
        private DataGridView _grid;

        public RechargePanel(AppSession app, MainForm host) : base(app, host)
        {
            BuildUi();
        }

        private void BuildUi()
        {
            _scan = new CardScanBox(App);
            _scan.Location = new Point(0, 0);
            _scan.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _scan.Width = Width - 8;
            _scan.CardChanged += delegate { UpdateConfirmState(); };
            Controls.Add(_scan);

            Panel card = UiKit.Card();
            card.Location = new Point(0, 184);
            card.Height = 172;
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Width = Width - 8;
            Controls.Add(card);

            Label caption = UiKit.Label("充值金额", UiKit.FontBold, UiKit.TextColor);
            caption.Location = new Point(20, 16);
            card.Controls.Add(caption);

            _txtAmount = UiKit.TextBox();
            _txtAmount.Location = new Point(20, 44);
            _txtAmount.Width = 180;
            _txtAmount.Height = 40;
            _txtAmount.Font = UiKit.FontAmountMid;
            _txtAmount.TextAlign = HorizontalAlignment.Right;
            _txtAmount.KeyPress += OnAmountKeyPress;
            _txtAmount.TextChanged += delegate { UpdateConfirmState(); };
            card.Controls.Add(_txtAmount);

            Label yuan = UiKit.Label("元", UiKit.FontNormal, UiKit.MutedColor);
            yuan.Location = new Point(208, 56);
            card.Controls.Add(yuan);

            long[] presets = new long[] { 2000, 5000, 10000, 20000, 50000 };
            string[] labels = new string[] { "20", "50", "100", "200", "500" };

            Label presetCaption = UiKit.Label("常用金额", UiKit.FontSmall, UiKit.MutedColor);
            presetCaption.Location = new Point(20, 100);
            card.Controls.Add(presetCaption);

            for (int i = 0; i < presets.Length; i++)
            {
                long cents = presets[i];
                Button b = UiKit.Button(labels[i], "normal");
                b.Size = new Size(58, 32);
                b.Location = new Point(90 + i * 64, 96);
                b.Click += delegate { _txtAmount.Text = Money.Format(cents); _txtAmount.Focus(); };
                card.Controls.Add(b);
            }

            _btnConfirm = UiKit.Button("确认充值", "success");
            _btnConfirm.Size = new Size(150, 40);
            _btnConfirm.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnConfirm.Location = new Point(card.Width - 170, 44);
            _btnConfirm.Click += OnConfirm;
            card.Controls.Add(_btnConfirm);

            _lblResult = UiKit.Label("", UiKit.FontNormal, UiKit.MutedColor);
            _lblResult.Location = new Point(20, 140);
            _lblResult.MaximumSize = new Size(700, 24);
            card.Controls.Add(_lblResult);

            Label recentCaption = UiKit.Label("最近充值记录", UiKit.FontBold, UiKit.TextColor);
            recentCaption.Location = new Point(0, 372);
            Controls.Add(recentCaption);

            _grid = new DataGridView();
            UiKit.StyleGrid(_grid);
            _grid.Location = new Point(0, 400);
            _grid.Height = 200;
            // 不含 Bottom：构造时面板高度还是默认的 100，按那时的高度记下的
            // 锚定基准会让表格在面板撑开后长到可视区外面去，高度改由 Resize 算。
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _grid.Width = Width - 8;
            _grid.Columns.Add("time", "时间");
            _grid.Columns.Add("cardno", "卡号");
            _grid.Columns.Add("holder", "持卡人");
            _grid.Columns.Add("amount", "充值(元)");
            _grid.Columns.Add("balance", "充值后余额(元)");
            _grid.Columns.Add("op", "操作员");
            _grid.Columns["amount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns["balance"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            Controls.Add(_grid);

            Resize += delegate
            {
                _scan.Width = Width - 8;
                card.Width = Width - 8;
                _btnConfirm.Left = card.Width - 170;
                _grid.Width = Width - 8;
                _grid.Height = Math.Max(80, Height - 408);
            };
        }

        private void OnAmountKeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                e.Handled = true;
                OnConfirm(null, EventArgs.Empty);
                return;
            }
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != '\b')
            {
                e.Handled = true;
                return;
            }
            if (e.KeyChar == '.' && _txtAmount.Text.IndexOf('.') >= 0)
            {
                e.Handled = true;
            }
        }

        private void UpdateConfirmState()
        {
            _btnConfirm.Enabled = _scan.HasEpc;
        }

        private void OnConfirm(object sender, EventArgs e)
        {
            if (!_scan.HasEpc)
            {
                UiKit.Warn(this, "请先刷卡，或在「当前卡片」区域手动输入 EPC。");
                return;
            }

            long amount;
            string error;
            if (!Money.TryParseCents(_txtAmount.Text, out amount, out error))
            {
                UiKit.Warn(this, error);
                _txtAmount.Focus();
                return;
            }
            if (amount <= 0)
            {
                UiKit.Warn(this, "充值金额必须大于 0。");
                _txtAmount.Focus();
                return;
            }

            try
            {
                long balanceAfter = App.AccountService.Recharge(_scan.Epc, amount, App.CurrentOperatorId);
                Card card = App.CardService.FindByEpc(_scan.Epc);
                string holder = card == null ? "" : card.HolderName;

                _lblResult.ForeColor = UiKit.SuccessColor;
                _lblResult.Text = "充值成功：" + holder + " 充值 " + Money.Format(amount) +
                                  " 元，余额 " + Money.Format(balanceAfter) + " 元。";

                Host.SetStatus("充值成功：" + holder + " +" + Money.Format(amount) + " 元");
                UiKit.Info(this,
                    "充值成功\n\n" +
                    "持卡人：" + holder + "\n" +
                    "本次充值：" + Money.Format(amount) + " 元\n" +
                    "卡内余额：" + Money.Format(balanceAfter) + " 元");

                _txtAmount.Clear();
                _scan.Reload();
                RefreshRecent();
            }
            catch (BusinessException ex)
            {
                _lblResult.ForeColor = UiKit.DangerColor;
                _lblResult.Text = ex.Message.Replace("\n\n", "  ");
                UiKit.Warn(this, ex.Message);
            }
            catch (Exception ex)
            {
                _lblResult.ForeColor = UiKit.DangerColor;
                _lblResult.Text = "充值失败：" + ex.Message;
                UiKit.Error(this, "充值时发生错误：" + ex.Message);
            }
        }

        private void RefreshRecent()
        {
            _grid.Rows.Clear();
            List<Transaction> rows = App.ReportService.Query(null, null, TransactionType.Recharge, null, 15);

            foreach (Transaction t in rows)
            {
                _grid.Rows.Add(
                    t.CreatedAt.ToString("MM-dd HH:mm:ss"),
                    t.CardNo,
                    t.HolderName,
                    t.AmountText,
                    t.BalanceAfterText,
                    t.OperatorName);
            }
        }

        public override void OnActivated()
        {
            RefreshRecent();
            UpdateConfirmState();
            _txtAmount.Focus();
            Host.SetStatus("充值：刷卡后输入金额，按回车即可充值。");
        }

        public override void OnCardScanned(string epc)
        {
            _scan.SetEpc(epc);
            UpdateConfirmState();
            _txtAmount.Focus();
        }

        public override void ReloadCard()
        {
            _scan.Reload();
            UpdateConfirmState();
        }

        public override string CardSummary
        {
            get { return _scan.Summary; }
        }
    }
}
