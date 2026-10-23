using System;
using System.Drawing;
using System.Windows.Forms;
using MealCardSystem.Model;
using MealCardSystem.Service;
using MealCardSystem.Util;

namespace MealCardSystem.UI
{
    /// <summary>发卡面板：把一张空白卡的 EPC 登记为饭卡。</summary>
    public class IssuePanel : FunctionPanel
    {
        private CardScanBox _scan;
        private TextBox _txtName;
        private TextBox _txtHolderId;
        private TextBox _txtDepartment;
        private TextBox _txtInitial;
        private Button _btnIssue;
        private Label _lblHint;
        private Label _lblResult;

        public IssuePanel(AppSession app, MainForm host) : base(app, host)
        {
            BuildUi();
        }

        private void BuildUi()
        {
            _scan = new CardScanBox(App);
            _scan.Location = new Point(0, 0);
            _scan.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _scan.Width = Width - 8;
            _scan.CardChanged += delegate { OnCardChanged(); };
            Controls.Add(_scan);

            Panel card = UiKit.Card();
            card.Location = new Point(0, 184);
            card.Height = 300;
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Width = Width - 8;
            Controls.Add(card);

            Label caption = UiKit.Label("持卡人信息", UiKit.FontBold, UiKit.TextColor);
            caption.Location = new Point(20, 16);
            card.Controls.Add(caption);

            // ---- 姓名 ----
            Label l1 = UiKit.Label("姓名 *", UiKit.FontSmall, UiKit.MutedColor);
            l1.Location = new Point(20, 50);
            card.Controls.Add(l1);

            _txtName = UiKit.TextBox();
            _txtName.Location = new Point(20, 72);
            _txtName.Width = 200;
            _txtName.Height = 28;
            card.Controls.Add(_txtName);

            // ---- 工号 ----
            Label l2 = UiKit.Label("工号 / 学号", UiKit.FontSmall, UiKit.MutedColor);
            l2.Location = new Point(240, 50);
            card.Controls.Add(l2);

            _txtHolderId = UiKit.TextBox();
            _txtHolderId.Location = new Point(240, 72);
            _txtHolderId.Width = 200;
            _txtHolderId.Height = 28;
            card.Controls.Add(_txtHolderId);

            // ---- 部门 ----
            Label l3 = UiKit.Label("部门 / 班级", UiKit.FontSmall, UiKit.MutedColor);
            l3.Location = new Point(460, 50);
            card.Controls.Add(l3);

            _txtDepartment = UiKit.TextBox();
            _txtDepartment.Location = new Point(460, 72);
            _txtDepartment.Width = 200;
            _txtDepartment.Height = 28;
            card.Controls.Add(_txtDepartment);

            // ---- 初始金额 ----
            Label l4 = UiKit.Label("开卡存入金额（元）", UiKit.FontSmall, UiKit.MutedColor);
            l4.Location = new Point(20, 118);
            card.Controls.Add(l4);

            _txtInitial = UiKit.TextBox();
            _txtInitial.Location = new Point(20, 140);
            _txtInitial.Width = 200;
            _txtInitial.Height = 28;
            _txtInitial.Text = "0";
            _txtInitial.TextAlign = HorizontalAlignment.Right;
            card.Controls.Add(_txtInitial);

            Button btnZero = UiKit.Button("清零", "normal");
            btnZero.Size = new Size(60, 28);
            btnZero.Location = new Point(228, 140);
            btnZero.Font = UiKit.FontSmall;
            btnZero.Click += delegate { _txtInitial.Text = "0"; };
            card.Controls.Add(btnZero);

            // ---- 发卡按钮 ----
            _btnIssue = UiKit.Button("确认发卡", "primary");
            _btnIssue.Size = new Size(150, 42);
            _btnIssue.Location = new Point(20, 190);
            _btnIssue.Click += OnIssue;
            card.Controls.Add(_btnIssue);

            _lblHint = UiKit.Label("", UiKit.FontSmall, UiKit.WarningColor);
            _lblHint.Location = new Point(190, 200);
            _lblHint.MaximumSize = new Size(520, 40);
            card.Controls.Add(_lblHint);

            _lblResult = UiKit.Label("", UiKit.FontNormal, UiKit.MutedColor);
            _lblResult.Location = new Point(20, 248);
            _lblResult.MaximumSize = new Size(700, 44);
            card.Controls.Add(_lblResult);

            Resize += delegate
            {
                _scan.Width = Width - 8;
                card.Width = Width - 8;
            };
        }

        private void OnCardChanged()
        {
            UpdateState();
        }

        private void UpdateState()
        {
            bool hasCard = _scan.HasEpc;
            bool registered = _scan.HasRegisteredCard;

            _btnIssue.Enabled = hasCard && !registered;

            if (!hasCard)
            {
                _lblHint.Text = "请先刷卡，或在「当前卡片」区域手动输入 EPC。";
                _lblHint.ForeColor = UiKit.FaintColor;
            }
            else if (registered)
            {
                _lblHint.Text = "这张卡已经登记过了，不能重复发卡。若要换人使用，请先到「卡片管理」注销原卡。";
                _lblHint.ForeColor = UiKit.WarningColor;
            }
            else
            {
                _lblHint.Text = "这是一张空白卡，填写持卡人信息后即可发卡。";
                _lblHint.ForeColor = UiKit.SuccessColor;
            }
        }

        private void OnIssue(object sender, EventArgs e)
        {
            if (!_scan.HasEpc)
            {
                UiKit.Warn(this, "请先刷卡，或在「当前卡片」区域手动输入 EPC。");
                return;
            }
            if (_scan.HasRegisteredCard)
            {
                UiKit.Warn(this, "这张卡已经登记过了，不能重复发卡。");
                return;
            }

            string name = _txtName.Text.Trim();
            if (name.Length == 0)
            {
                UiKit.Warn(this, "请填写持卡人姓名。");
                _txtName.Focus();
                return;
            }

            long initial;
            string error;
            if (!Money.TryParseCents(_txtInitial.Text, out initial, out error))
            {
                // 允许留空代表 0 元开卡
                if (_txtInitial.Text.Trim().Length == 0)
                {
                    initial = 0;
                }
                else
                {
                    UiKit.Warn(this, error);
                    _txtInitial.Focus();
                    return;
                }
            }

            if (initial > 0)
            {
                if (!UiKit.Confirm(this,
                        "确认发卡？\n\n" +
                        "持卡人：" + name + "\n" +
                        "开卡存入：" + Money.Format(initial) + " 元\n\n" +
                        "请确认已收取该笔现金。"))
                {
                    return;
                }
            }

            try
            {
                Card created = App.CardService.IssueCard(
                    _scan.Epc, name, _txtHolderId.Text, _txtDepartment.Text,
                    initial, App.CurrentOperatorId);

                _lblResult.ForeColor = UiKit.SuccessColor;
                _lblResult.Text = "发卡成功：卡号 " + created.CardNo + "，持卡人 " + created.HolderName +
                                  "，初始余额 " + created.BalanceText + " 元。";

                Host.SetStatus("发卡成功：卡号 " + created.CardNo + " · " + created.HolderName);
                UiKit.Info(this,
                    "发卡成功\n\n" +
                    "卡号：" + created.CardNo + "\n" +
                    "持卡人：" + created.HolderName + "\n" +
                    "工号：" + (created.HolderId.Length > 0 ? created.HolderId : "—") + "\n" +
                    "部门：" + (created.Department.Length > 0 ? created.Department : "—") + "\n" +
                    "卡内余额：" + created.BalanceText + " 元");

                ClearForm();
                _scan.Reload();
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
                _lblResult.Text = "发卡失败：" + ex.Message;
                UiKit.Error(this, "发卡时发生错误：" + ex.Message);
            }
        }

        private void ClearForm()
        {
            _txtName.Clear();
            _txtHolderId.Clear();
            _txtDepartment.Clear();
            _txtInitial.Text = "0";
        }

        public override void OnActivated()
        {
            UpdateState();
            _txtName.Focus();
            Host.SetStatus("发卡：刷一张空白卡，填写持卡人信息后确认。");
        }

        public override void OnCardScanned(string epc)
        {
            _scan.SetEpc(epc);
            UpdateState();
        }

        public override void ReloadCard()
        {
            _scan.Reload();
            UpdateState();
        }

        public override string CardSummary
        {
            get { return _scan.Summary; }
        }
    }
}
