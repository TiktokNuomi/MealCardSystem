using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MealCardSystem.Model;
using MealCardSystem.Service;
using MealCardSystem.Util;

namespace MealCardSystem.UI
{
    /// <summary>消费扣款面板。</summary>
    public class ConsumePanel : FunctionPanel
    {
        private CardScanBox _scan;
        private TextBox _txtAmount;
        private Button _btnConfirm;
        private Label _lblResult;
        private DataGridView _grid;

        public ConsumePanel(AppSession app, MainForm host) : base(app, host)
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

            // ---------- 金额输入区 ----------
            Panel amountCard = UiKit.Card();
            amountCard.Location = new Point(0, 184);
            amountCard.Height = 172;
            amountCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            amountCard.Width = Width - 8;
            Controls.Add(amountCard);

            Label caption = UiKit.Label("消费金额", UiKit.FontBold, UiKit.TextColor);
            caption.Location = new Point(20, 16);
            amountCard.Controls.Add(caption);

            _txtAmount = UiKit.TextBox();
            _txtAmount.Location = new Point(20, 44);
            _txtAmount.Width = 180;
            _txtAmount.Height = 40;
            _txtAmount.Font = UiKit.FontAmountMid;
            _txtAmount.TextAlign = HorizontalAlignment.Right;
            _txtAmount.KeyPress += OnAmountKeyPress;
            _txtAmount.TextChanged += delegate { UpdateConfirmState(); };
            amountCard.Controls.Add(_txtAmount);

            Label yuan = UiKit.Label("元", UiKit.FontNormal, UiKit.MutedColor);
            yuan.Location = new Point(208, 56);
            amountCard.Controls.Add(yuan);

            // 常用金额快捷键，食堂场景下比逐个敲数字快得多
            long[] presets = new long[] { 500, 800, 1000, 1200, 1500, 2000 };
            string[] presetLabels = new string[] { "5", "8", "10", "12", "15", "20" };

            Label presetCaption = UiKit.Label("常用金额", UiKit.FontSmall, UiKit.MutedColor);
            presetCaption.Location = new Point(20, 100);
            amountCard.Controls.Add(presetCaption);

            for (int i = 0; i < presets.Length; i++)
            {
                long cents = presets[i];
                Button b = UiKit.Button(presetLabels[i], "normal");
                b.Size = new Size(58, 32);
                b.Font = UiKit.FontNormal;
                b.Location = new Point(90 + i * 64, 96);
                b.Click += delegate { _txtAmount.Text = Money.Format(cents); _txtAmount.Focus(); };
                amountCard.Controls.Add(b);
            }

            _btnConfirm = UiKit.Button("确认扣款", "primary");
            _btnConfirm.Size = new Size(150, 40);
            _btnConfirm.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnConfirm.Location = new Point(amountCard.Width - 170, 44);
            _btnConfirm.Click += OnConfirm;
            amountCard.Controls.Add(_btnConfirm);

            _lblResult = UiKit.Label("", UiKit.FontNormal, UiKit.MutedColor);
            _lblResult.Location = new Point(20, 140);
            _lblResult.MaximumSize = new Size(700, 24);
            amountCard.Controls.Add(_lblResult);

            // ---------- 最近交易 ----------
            Label recentCaption = UiKit.Label("最近消费记录", UiKit.FontBold, UiKit.TextColor);
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
            _grid.Columns.Add("amount", "金额(元)");
            _grid.Columns.Add("balance", "消费后余额(元)");
            _grid.Columns.Add("op", "操作员");
            _grid.Columns["amount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns["balance"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            Controls.Add(_grid);

            Resize += delegate
            {
                _scan.Width = Width - 8;
                amountCard.Width = Width - 8;
                _btnConfirm.Left = amountCard.Width - 170;
                _grid.Width = Width - 8;
                _grid.Height = Math.Max(80, Height - 408);
            };
        }

        private void OnAmountKeyPress(object sender, KeyPressEventArgs e)
        {
            // 只允许数字、小数点和退格
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != '\b')
            {
                e.Handled = true;
                return;
            }
            // 只允许一个小数点，且小数部分最多两位
            if (e.KeyChar == '.')
            {
                string text = _txtAmount.Text;
                if (text.IndexOf('.') >= 0)
                {
                    e.Handled = true;
                    return;
                }
                if (text.Length == 0)
                {
                    _txtAmount.Text = "0";
                    _txtAmount.SelectionStart = 1;
                }
            }

            if (e.KeyChar == '\r')
            {
                e.Handled = true;
                OnConfirm(null, EventArgs.Empty);
            }
        }

        private void UpdateConfirmState()
        {
            bool ready = _scan.HasRegisteredCard && _scan.Card.CanConsume;
            _btnConfirm.Enabled = ready;
        }

        private void OnConfirm(object sender, EventArgs e)
        {
            if (!_scan.HasEpc)
            {
                UiKit.Warn(this, "请先刷卡，或在「当前卡片」区域手动输入 EPC。");
                return;
            }
            if (!_scan.HasRegisteredCard)
            {
                UiKit.Warn(this, "这张卡还没有登记，请先到「发卡」办理。");
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
                UiKit.Warn(this, "消费金额必须大于 0。");
                _txtAmount.Focus();
                return;
            }

            Card card = _scan.Card;

            try
            {
                long balanceAfter = App.AccountService.Consume(_scan.Epc, amount, App.CurrentOperatorId);

                _lblResult.ForeColor = UiKit.SuccessColor;
                _lblResult.Text = "扣款成功：" + card.HolderName + " 消费 " + Money.Format(amount) +
                                  " 元，余额 " + Money.Format(balanceAfter) + " 元。";

                Host.SetStatus("消费成功：" + card.HolderName + " -" + Money.Format(amount) + " 元");
                UiKit.Info(this,
                    "扣款成功\n\n" +
                    "持卡人：" + card.HolderName + "\n" +
                    "本次消费：" + Money.Format(amount) + " 元\n" +
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
                _lblResult.Text = "扣款失败：" + ex.Message;
                UiKit.Error(this, "扣款时发生错误：" + ex.Message);
            }
        }

        private void RefreshRecent()
        {
            _grid.Rows.Clear();
            List<Transaction> rows = App.ReportService.Query(null, null, TransactionType.Consume, null, 15);

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
            Host.SetStatus("消费：刷卡后输入金额，按回车即可扣款。");
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
