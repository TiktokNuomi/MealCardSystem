using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MealCardSystem.Model;
using MealCardSystem.Service;
using MealCardSystem.Util;

namespace MealCardSystem.UI
{
    /// <summary>卡片管理：挂失、解挂、注销退卡。</summary>
    public class CardManagePanel : FunctionPanel
    {
        private CardScanBox _scan;
        private Button _btnLost;
        private Button _btnUnlost;
        private Button _btnCancel;
        private Label _lblResult;
        private DataGridView _grid;

        public CardManagePanel(AppSession app, MainForm host) : base(app, host)
        {
            BuildUi();
        }

        private void BuildUi()
        {
            _scan = new CardScanBox(App);
            _scan.Location = new Point(0, 0);
            _scan.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _scan.Width = Width - 8;
            _scan.CardChanged += delegate { UpdateButtons(); };
            Controls.Add(_scan);

            Panel card = UiKit.Card();
            card.Location = new Point(0, 184);
            card.Height = 126;
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Width = Width - 8;
            Controls.Add(card);

            Label caption = UiKit.Label("卡片操作", UiKit.FontBold, UiKit.TextColor);
            caption.Location = new Point(20, 16);
            card.Controls.Add(caption);

            _btnLost = UiKit.Button("挂失", "warning");
            _btnLost.Size = new Size(140, 40);
            _btnLost.Location = new Point(20, 48);
            _btnLost.Click += delegate { DoSetLost(true); };
            card.Controls.Add(_btnLost);

            _btnUnlost = UiKit.Button("解挂", "success");
            _btnUnlost.Size = new Size(140, 40);
            _btnUnlost.Location = new Point(172, 48);
            _btnUnlost.Click += delegate { DoSetLost(false); };
            card.Controls.Add(_btnUnlost);

            _btnCancel = UiKit.Button("注销退卡", "danger");
            _btnCancel.Size = new Size(140, 40);
            _btnCancel.Location = new Point(324, 48);
            _btnCancel.Click += OnCancel;
            card.Controls.Add(_btnCancel);

            _lblResult = UiKit.Label("", UiKit.FontSmall, UiKit.MutedColor);
            _lblResult.Location = new Point(484, 52);
            _lblResult.MaximumSize = new Size(400, 60);
            card.Controls.Add(_lblResult);

            Label listCaption = UiKit.Label("已登记的卡片", UiKit.FontBold, UiKit.TextColor);
            listCaption.Location = new Point(0, 322);
            Controls.Add(listCaption);

            TextBox txtFilter = UiKit.TextBox();
            txtFilter.Location = new Point(110, 318);
            txtFilter.Width = 200;
            txtFilter.TextChanged += delegate { RefreshList(txtFilter.Text); };
            Controls.Add(txtFilter);

            Label filterHint = UiKit.Label("按卡号 / 姓名 / 工号 / EPC 筛选", UiKit.FontSmall, UiKit.FaintColor);
            filterHint.Location = new Point(320, 322);
            Controls.Add(filterHint);

            _grid = new DataGridView();
            UiKit.StyleGrid(_grid);
            _grid.Location = new Point(0, 350);
            _grid.Height = 250;
            // 不含 Bottom：构造时面板高度还是默认的 100，按那时的高度记下的
            // 锚定基准会让表格在面板撑开后长到可视区外面去，高度改由 Resize 算。
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _grid.Width = Width - 8;
            _grid.Columns.Add("cardno", "卡号");
            _grid.Columns.Add("holder", "持卡人");
            _grid.Columns.Add("holderid", "工号");
            _grid.Columns.Add("dept", "部门");
            _grid.Columns.Add("balance", "余额(元)");
            _grid.Columns.Add("status", "状态");
            _grid.Columns.Add("epc", "EPC");
            _grid.Columns["balance"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.SelectionChanged += delegate { OnGridSelectionChanged(); };
            Controls.Add(_grid);

            Resize += delegate
            {
                _scan.Width = Width - 8;
                card.Width = Width - 8;
                _grid.Width = Width - 8;
                _grid.Height = Math.Max(80, Height - 358);
            };
        }

        private void UpdateButtons()
        {
            bool registered = _scan.HasRegisteredCard;

            if (!registered)
            {
                _btnLost.Enabled = false;
                _btnUnlost.Enabled = false;
                _btnCancel.Enabled = false;
                return;
            }

            Card c = _scan.Card;
            _btnLost.Enabled = (c.Status == CardStatus.Normal);
            _btnUnlost.Enabled = (c.Status == CardStatus.Lost);
            _btnCancel.Enabled = (c.Status != CardStatus.Cancelled);
        }

        private void DoSetLost(bool lost)
        {
            if (!_scan.HasRegisteredCard)
            {
                UiKit.Warn(this, "请先刷卡选择要操作的卡片。");
                return;
            }

            Card c = _scan.Card;

            if (lost)
            {
                if (!UiKit.Confirm(this,
                        "确认挂失这张卡？\n\n" +
                        "卡号：" + c.CardNo + "\n" +
                        "持卡人：" + c.HolderName + "\n" +
                        "卡内余额：" + c.BalanceText + " 元\n\n" +
                        "挂失后该卡不能消费和充值，余额保留。"))
                {
                    return;
                }
            }

            try
            {
                App.CardService.SetLost(c.Id, lost, App.CurrentOperatorId);
                _lblResult.ForeColor = UiKit.SuccessColor;
                _lblResult.Text = "操作成功：卡号 " + c.CardNo + " 已" + (lost ? "挂失" : "解挂") + "。";

                Host.SetStatus("卡号 " + c.CardNo + " 已" + (lost ? "挂失" : "解挂"));
                _scan.Reload();
                RefreshList(null);
                UpdateButtons();
            }
            catch (BusinessException ex)
            {
                _lblResult.ForeColor = UiKit.DangerColor;
                _lblResult.Text = ex.Message;
                UiKit.Warn(this, ex.Message);
            }
            catch (Exception ex)
            {
                _lblResult.ForeColor = UiKit.DangerColor;
                _lblResult.Text = "操作失败：" + ex.Message;
                UiKit.Error(this, "操作失败：" + ex.Message);
            }
        }

        private void OnCancel(object sender, EventArgs e)
        {
            if (!_scan.HasRegisteredCard)
            {
                UiKit.Warn(this, "请先刷卡选择要注销的卡片。");
                return;
            }

            Card c = _scan.Card;

            if (!UiKit.Confirm(this,
                    "确认注销这张卡并退还余额？\n\n" +
                    "卡号：" + c.CardNo + "\n" +
                    "持卡人：" + c.HolderName + "\n" +
                    "应退余额：" + c.BalanceText + " 元\n\n" +
                    "注销后本地的卡片信息会被删除，这张卡就变回一张空白卡，\n" +
                    "可以立刻重新发给别人（会分配一个新卡号）。\n" +
                    "历史流水会保留，不会丢失。\n" +
                    "请确认已将余额退还给持卡人。"))
            {
                return;
            }

            try
            {
                long refunded = App.CardService.CancelCard(c.Id, App.CurrentOperatorId);

                _lblResult.ForeColor = UiKit.SuccessColor;
                _lblResult.Text = "注销成功：卡号 " + c.CardNo + " 已注销并删除，退还 " +
                                  Money.Format(refunded) + " 元。";

                Host.SetStatus("卡号 " + c.CardNo + " 已注销，退还 " + Money.Format(refunded) + " 元");
                UiKit.Info(this,
                    "注销成功\n\n" +
                    "卡号：" + c.CardNo + "\n" +
                    "持卡人：" + c.HolderName + "\n" +
                    "退还余额：" + Money.Format(refunded) + " 元\n\n" +
                    "这张卡已变回空白卡，可到「发卡」重新登记。");

                _scan.Reload();
                RefreshList(null);
                UpdateButtons();
            }
            catch (BusinessException ex)
            {
                _lblResult.ForeColor = UiKit.DangerColor;
                _lblResult.Text = ex.Message;
                UiKit.Warn(this, ex.Message);
            }
            catch (Exception ex)
            {
                _lblResult.ForeColor = UiKit.DangerColor;
                _lblResult.Text = "注销失败：" + ex.Message;
                UiKit.Error(this, "注销时发生错误：" + ex.Message);
            }
        }

        private void RefreshList(string keyword)
        {
            _grid.Rows.Clear();
            List<Card> cards = App.Cards.Search(keyword, null);

            foreach (Card c in cards)
            {
                int idx = _grid.Rows.Add(
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
                _grid.Rows[idx].DefaultCellStyle.ForeColor = color;
            }
        }

        private void OnGridSelectionChanged()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                return;
            }
            object epc = _grid.SelectedRows[0].Cells["epc"].Value;
            if (epc != null)
            {
                // 从列表点选卡片，等同于刷了这张卡
                _scan.SetEpc(Convert.ToString(epc));
                UpdateButtons();
            }
        }

        public override void OnActivated()
        {
            RefreshList(null);
            UpdateButtons();
            Host.SetStatus("卡片管理：刷卡或从列表点选卡片，再进行挂失、解挂或注销。");
        }

        public override void OnCardScanned(string epc)
        {
            _scan.SetEpc(epc);
            UpdateButtons();
        }

        public override void ReloadCard()
        {
            _scan.Reload();
            UpdateButtons();
        }

        public override string CardSummary
        {
            get { return _scan.Summary; }
        }
    }
}
