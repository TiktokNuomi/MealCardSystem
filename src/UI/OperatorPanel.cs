using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MealCardSystem.Model;
using MealCardSystem.Service;

namespace MealCardSystem.UI
{
    /// <summary>
    /// 操作员管理：新建 / 删除操作员、修改登录密码。
    ///
    /// 权限约定：管理员可以管理所有人；普通操作员只能修改自己的密码，
    /// 以免收银员之间互相改口令或删除账号。
    /// </summary>
    public class OperatorPanel : FunctionPanel
    {
        private DataGridView _grid;
        private Button _btnNew;
        private Button _btnDelete;
        private Label _lblTarget;
        private TextBox _txtNewPassword;
        private TextBox _txtConfirm;
        private Button _btnSavePassword;
        private Label _lblMessage;
        private Label _lblRoleHint;

        public OperatorPanel(AppSession app, MainForm host) : base(app, host)
        {
            AutoScroll = false;
            BuildUi();
        }

        private bool IsAdmin
        {
            get { return App.CurrentOperator != null && App.CurrentOperator.IsAdmin; }
        }

        // ================= 界面 =================

        private void BuildUi()
        {
            Label caption = UiKit.Label("操作员列表", UiKit.FontBold, UiKit.TextColor);
            caption.Location = new Point(0, 8);
            Controls.Add(caption);

            _btnNew = UiKit.Button("新建操作员", "primary");
            _btnNew.Size = new Size(120, 28);
            _btnNew.Font = UiKit.FontNormal;
            _btnNew.Location = new Point(140, 4);
            _btnNew.Click += OnNew;
            Controls.Add(_btnNew);

            _btnDelete = UiKit.Button("删除所选", "danger");
            _btnDelete.Size = new Size(110, 28);
            _btnDelete.Font = UiKit.FontNormal;
            _btnDelete.Location = new Point(268, 4);
            _btnDelete.Click += OnDelete;
            Controls.Add(_btnDelete);

            _lblRoleHint = UiKit.Label("", UiKit.FontSmall, UiKit.FaintColor);
            _lblRoleHint.Location = new Point(390, 10);
            Controls.Add(_lblRoleHint);

            _grid = new DataGridView();
            UiKit.StyleGrid(_grid);
            _grid.Location = new Point(0, 40);
            _grid.Size = new Size(Width, 220);
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _grid.Columns.Add("username", "登录账号");
            _grid.Columns.Add("display", "姓名");
            _grid.Columns.Add("role", "角色");
            _grid.SelectionChanged += delegate { OnRowSelected(); };
            Controls.Add(_grid);

            // ---------- 修改密码 ----------
            Panel card = UiKit.Card();
            card.Location = new Point(0, 276);
            card.Height = 190;
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Width = Width - 8;
            Controls.Add(card);

            Label pwdCaption = UiKit.Label("修改登录密码", UiKit.FontBold, UiKit.TextColor);
            pwdCaption.Location = new Point(20, 14);
            card.Controls.Add(pwdCaption);

            _lblTarget = UiKit.Label("请先在上方列表中选择一个账号。", UiKit.FontSmall, UiKit.MutedColor);
            _lblTarget.Location = new Point(140, 18);
            card.Controls.Add(_lblTarget);

            Label l1 = UiKit.Label("新密码", UiKit.FontSmall, UiKit.MutedColor);
            l1.Location = new Point(20, 50);
            card.Controls.Add(l1);

            _txtNewPassword = UiKit.TextBox();
            _txtNewPassword.Location = new Point(20, 72);
            _txtNewPassword.Width = 220;
            _txtNewPassword.Height = 28;
            _txtNewPassword.UseSystemPasswordChar = true;
            card.Controls.Add(_txtNewPassword);

            Label l2 = UiKit.Label("确认新密码", UiKit.FontSmall, UiKit.MutedColor);
            l2.Location = new Point(264, 50);
            card.Controls.Add(l2);

            _txtConfirm = UiKit.TextBox();
            _txtConfirm.Location = new Point(264, 72);
            _txtConfirm.Width = 220;
            _txtConfirm.Height = 28;
            _txtConfirm.UseSystemPasswordChar = true;
            card.Controls.Add(_txtConfirm);

            _btnSavePassword = UiKit.Button("保存新密码", "primary");
            _btnSavePassword.Size = new Size(130, 34);
            _btnSavePassword.Location = new Point(508, 70);
            _btnSavePassword.Click += OnSavePassword;
            card.Controls.Add(_btnSavePassword);

            _lblMessage = UiKit.Label("密码至少 4 位。修改后请牢记，遗忘需由管理员重置。",
                                      UiKit.FontSmall, UiKit.FaintColor);
            _lblMessage.Location = new Point(20, 118);
            _lblMessage.MaximumSize = new Size(700, 60);
            card.Controls.Add(_lblMessage);

            Resize += delegate
            {
                _grid.Width = Width - 8;
                card.Width = Width - 8;
            };
        }

        // ================= 数据 =================

        private void RefreshList()
        {
            _grid.Rows.Clear();

            List<Operator> ops = App.Operators.List();
            foreach (Operator op in ops)
            {
                int idx = _grid.Rows.Add(op.Username, op.DisplayName, op.IsAdmin ? "管理员" : "操作员");

                if (App.CurrentOperator != null && op.Id == App.CurrentOperator.Id)
                {
                    _grid.Rows[idx].DefaultCellStyle.Font = UiKit.FontBold;
                }
            }

            _btnNew.Enabled = IsAdmin;
            _btnDelete.Enabled = IsAdmin;

            _lblRoleHint.Text = IsAdmin
                ? "当前以管理员身份登录，可新建与删除账号。"
                : "当前是普通操作员，只能修改自己的密码。";

            if (!IsAdmin)
            {
                // 普通操作员只看得见自己，避免列出他人账号
                _grid.Rows.Clear();
                if (App.CurrentOperator != null)
                {
                    _grid.Rows.Add(App.CurrentOperator.Username,
                                   App.CurrentOperator.DisplayName, "操作员");
                }
                _grid.Rows[0].Selected = true;
            }
        }

        /// <summary>当前选中的操作员，未选中为 null。</summary>
        private Operator SelectedOperator
        {
            get
            {
                if (_grid.SelectedRows.Count == 0)
                {
                    return null;
                }
                object name = _grid.SelectedRows[0].Cells["username"].Value;
                if (name == null)
                {
                    return null;
                }

                string username = Convert.ToString(name);
                foreach (Operator op in App.Operators.List())
                {
                    if (op.Username == username)
                    {
                        return op;
                    }
                }
                return null;
            }
        }

        private void OnRowSelected()
        {
            Operator op = SelectedOperator;
            if (op == null)
            {
                _lblTarget.Text = "请先在上方列表中选择一个账号。";
                return;
            }

            string self = (App.CurrentOperator != null && op.Id == App.CurrentOperator.Id) ? "（当前登录）" : "";
            _lblTarget.Text = "正在修改：" + op.Caption + "（" + op.Username + "）" + self;
        }

        // ================= 操作 =================

        private void OnNew(object sender, EventArgs e)
        {
            if (!IsAdmin)
            {
                UiKit.Warn(this, "只有管理员可以新建操作员。");
                return;
            }

            using (NewOperatorDialog dlg = new NewOperatorDialog(App))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshList();
                    Host.SetStatus("已新建操作员：" + dlg.CreatedUsername);
                }
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            if (!IsAdmin)
            {
                UiKit.Warn(this, "只有管理员可以删除操作员。");
                return;
            }

            Operator op = SelectedOperator;
            if (op == null)
            {
                UiKit.Warn(this, "请先选择要删除的账号。");
                return;
            }
            if (App.CurrentOperator != null && op.Id == App.CurrentOperator.Id)
            {
                UiKit.Warn(this, "不能删除当前登录的账号。");
                return;
            }
            if (op.IsAdmin && App.Operators.CountAdmins() <= 1)
            {
                UiKit.Warn(this, "这是最后一个管理员账号，不能删除。\n\n" +
                                 "请先新建另一个管理员，再删除此账号。");
                return;
            }

            if (!UiKit.Confirm(this,
                    "确认删除该操作员？\n\n" +
                    "账号：" + op.Username + "\n" +
                    "姓名：" + op.Caption + "\n\n" +
                    "删除后该账号无法再登录。此操作不可撤销。"))
            {
                return;
            }

            try
            {
                App.Operators.Delete(op.Id);
                RefreshList();
                Host.SetStatus("已删除操作员：" + op.Username);
            }
            catch (Exception ex)
            {
                UiKit.Error(this, "删除失败：" + ex.Message);
            }
        }

        private void OnSavePassword(object sender, EventArgs e)
        {
            Operator op = SelectedOperator;
            if (op == null)
            {
                UiKit.Warn(this, "请先在上方列表中选择要修改密码的账号。");
                return;
            }
            if (!IsAdmin && (App.CurrentOperator == null || op.Id != App.CurrentOperator.Id))
            {
                UiKit.Warn(this, "只有管理员可以修改他人的密码。");
                return;
            }

            string pwd = _txtNewPassword.Text;
            string confirm = _txtConfirm.Text;

            if (pwd.Length < 4)
            {
                _lblMessage.ForeColor = UiKit.DangerColor;
                _lblMessage.Text = "新密码至少 4 位。";
                _txtNewPassword.Focus();
                return;
            }
            if (pwd != confirm)
            {
                _lblMessage.ForeColor = UiKit.DangerColor;
                _lblMessage.Text = "两次输入的密码不一致，请重新输入。";
                _txtConfirm.SelectAll();
                _txtConfirm.Focus();
                return;
            }
            if (App.CurrentOperator != null && op.Id == App.CurrentOperator.Id && pwd == "admin123")
            {
                _lblMessage.ForeColor = UiKit.DangerColor;
                _lblMessage.Text = "新密码不能与默认密码相同，请另设一个。";
                _txtNewPassword.Focus();
                return;
            }

            try
            {
                App.Operators.ChangePassword(op.Id, pwd);

                _lblMessage.ForeColor = UiKit.SuccessColor;
                _lblMessage.Text = "已修改 " + op.Username + " 的登录密码，下次登录请使用新密码。";

                _txtNewPassword.Clear();
                _txtConfirm.Clear();

                Host.SetStatus("已修改 " + op.Username + " 的密码。");
            }
            catch (Exception ex)
            {
                _lblMessage.ForeColor = UiKit.DangerColor;
                _lblMessage.Text = "修改失败：" + ex.Message;
            }
        }

        public override void OnActivated()
        {
            RefreshList();
            _txtNewPassword.Clear();
            _txtConfirm.Clear();
            Host.SetStatus("操作员管理：可新建/删除账号，或为选中账号设置新密码。");
        }
    }

    /// <summary>新建操作员的模态对话框。</summary>
    internal class NewOperatorDialog : Form
    {
        private readonly AppSession _app;

        private TextBox _txtUsername;
        private TextBox _txtDisplayName;
        private TextBox _txtPassword;
        private TextBox _txtConfirm;
        private CheckBox _chkAdmin;
        private Label _lblMessage;

        /// <summary>创建成功后的账号名，供调用方提示。</summary>
        public string CreatedUsername;

        public NewOperatorDialog(AppSession app)
        {
            _app = app;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "新建操作员";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 316);
            BackColor = UiKit.SurfaceColor;
            Font = UiKit.FontNormal;

            Label caption = UiKit.Label("新建操作员", UiKit.FontSubtitle, UiKit.TextColor);
            caption.Location = new Point(24, 18);
            Controls.Add(caption);

            int y = 62;

            Controls.Add(MakeLabel("登录账号 *", y));
            _txtUsername = UiKit.TextBox();
            _txtUsername.Location = new Point(24, y + 22);
            _txtUsername.Width = 340;
            _txtUsername.Height = 28;
            Controls.Add(_txtUsername);

            y += 62;
            Controls.Add(MakeLabel("姓名", y));
            _txtDisplayName = UiKit.TextBox();
            _txtDisplayName.Location = new Point(24, y + 22);
            _txtDisplayName.Width = 340;
            _txtDisplayName.Height = 28;
            Controls.Add(_txtDisplayName);

            y += 62;
            Controls.Add(MakeLabel("密码 *（至少 4 位）", y));
            _txtPassword = UiKit.TextBox();
            _txtPassword.Location = new Point(24, y + 22);
            _txtPassword.Width = 160;
            _txtPassword.Height = 28;
            _txtPassword.UseSystemPasswordChar = true;
            Controls.Add(_txtPassword);

            Label l = UiKit.Label("确认密码 *", UiKit.FontSmall, UiKit.MutedColor);
            l.Location = new Point(204, y);
            Controls.Add(l);

            _txtConfirm = UiKit.TextBox();
            _txtConfirm.Location = new Point(204, y + 22);
            _txtConfirm.Width = 160;
            _txtConfirm.Height = 28;
            _txtConfirm.UseSystemPasswordChar = true;
            Controls.Add(_txtConfirm);

            y += 62;
            _chkAdmin = new CheckBox();
            _chkAdmin.Text = "设为管理员（可以管理其它操作员）";
            _chkAdmin.Font = UiKit.FontNormal;
            _chkAdmin.ForeColor = UiKit.TextColor;
            _chkAdmin.Location = new Point(24, y);
            _chkAdmin.AutoSize = true;
            Controls.Add(_chkAdmin);

            y += 34;
            _lblMessage = UiKit.Label("", UiKit.FontSmall, UiKit.DangerColor);
            _lblMessage.Location = new Point(24, y);
            _lblMessage.MaximumSize = new Size(340, 40);
            Controls.Add(_lblMessage);

            Button btnOk = UiKit.Button("创建", "primary");
            btnOk.Size = new Size(160, 36);
            btnOk.Location = new Point(24, y + 34);
            btnOk.Click += OnCreate;
            Controls.Add(btnOk);

            Button btnCancel = UiKit.Button("取消", "normal");
            btnCancel.Size = new Size(160, 36);
            btnCancel.Location = new Point(204, y + 34);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private static Label MakeLabel(string text, int y)
        {
            Label l = UiKit.Label(text, UiKit.FontSmall, UiKit.MutedColor);
            l.Location = new Point(24, y);
            return l;
        }

        private void OnCreate(object sender, EventArgs e)
        {
            string username = _txtUsername.Text.Trim();

            if (username.Length == 0)
            {
                _lblMessage.Text = "请填写登录账号。";
                _txtUsername.Focus();
                return;
            }
            if (username.IndexOf(' ') >= 0)
            {
                _lblMessage.Text = "登录账号不能包含空格。";
                _txtUsername.Focus();
                return;
            }
            if (_txtPassword.Text.Length < 4)
            {
                _lblMessage.Text = "密码至少 4 位。";
                _txtPassword.Focus();
                return;
            }
            if (_txtPassword.Text != _txtConfirm.Text)
            {
                _lblMessage.Text = "两次输入的密码不一致。";
                _txtConfirm.SelectAll();
                _txtConfirm.Focus();
                return;
            }

            try
            {
                _app.Operators.Create(username, _txtPassword.Text, _txtDisplayName.Text, _chkAdmin.Checked);
                CreatedUsername = username;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _lblMessage.Text = ex.Message;
            }
        }
    }
}
