using System;
using System.Drawing;
using System.Windows.Forms;
using MealCardSystem.Model;
using MealCardSystem.Service;

namespace MealCardSystem.UI
{
    /// <summary>操作员登录窗口。程序启动后必须先登录才能使用各项功能。</summary>
    public class LoginForm : Form
    {
        private readonly AppSession _app;

        private TextBox _txtUser;
        private TextBox _txtPassword;
        private Label _lblMessage;
        private Button _btnLogin;

        public LoginForm(AppSession app)
        {
            _app = app;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "食堂饭卡管理系统 · 登录";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 340);
            BackColor = UiKit.SurfaceColor;
            Font = UiKit.FontNormal;

            // 顶部色块
            Panel header = new Panel();
            header.BackColor = UiKit.HeaderColor;
            header.Dock = DockStyle.Top;
            header.Height = 84;

            Label title = UiKit.Label("食堂饭卡管理系统", UiKit.FontTitle, Color.White);
            title.Location = new Point(32, 22);
            header.Controls.Add(title);

            Label sub = UiKit.Label("请登录后使用", UiKit.FontSmall, Color.FromArgb(0xB8, 0xC9, 0xE0));
            sub.Location = new Point(34, 54);
            header.Controls.Add(sub);

            Controls.Add(header);

            int y = 116;

            Label lblUser = UiKit.Label("操作员账号", UiKit.FontSmall, UiKit.MutedColor);
            lblUser.Location = new Point(32, y);
            Controls.Add(lblUser);

            _txtUser = UiKit.TextBox();
            _txtUser.Location = new Point(32, y + 22);
            _txtUser.Width = 356;
            _txtUser.Height = 28;
            _txtUser.Text = "admin";
            Controls.Add(_txtUser);

            y += 66;

            Label lblPwd = UiKit.Label("密码", UiKit.FontSmall, UiKit.MutedColor);
            lblPwd.Location = new Point(32, y);
            Controls.Add(lblPwd);

            _txtPassword = UiKit.TextBox();
            _txtPassword.Location = new Point(32, y + 22);
            _txtPassword.Width = 356;
            _txtPassword.Height = 28;
            _txtPassword.PasswordChar = '●';
            Controls.Add(_txtPassword);

            y += 66;

            _lblMessage = UiKit.Label("", UiKit.FontSmall, UiKit.DangerColor);
            _lblMessage.Location = new Point(32, y);
            _lblMessage.MaximumSize = new Size(356, 40);
            Controls.Add(_lblMessage);

            _btnLogin = UiKit.Button("登 录", "primary");
            _btnLogin.Location = new Point(32, y + 40);
            _btnLogin.Width = 356;
            _btnLogin.Height = 40;
            _btnLogin.Click += OnLogin;
            Controls.Add(_btnLogin);

            // 首次运行时提示默认口令，避免用户不知道用什么登录
            if (_app.CreatedDefaultAdmin)
            {
                Label hint = UiKit.Label("首次使用：默认账号 admin，密码 admin123，登录后请及时修改。",
                                         UiKit.FontSmall, UiKit.WarningColor);
                hint.Location = new Point(32, 292);
                hint.MaximumSize = new Size(356, 32);
                Controls.Add(hint);
            }

            AcceptButton = _btnLogin;
            ActiveControl = _txtPassword;
        }

        private void OnLogin(object sender, EventArgs e)
        {
            string user = _txtUser.Text.Trim();
            string password = _txtPassword.Text;

            if (user.Length == 0)
            {
                _lblMessage.Text = "请输入操作员账号。";
                _txtUser.Focus();
                return;
            }
            if (password.Length == 0)
            {
                _lblMessage.Text = "请输入密码。";
                _txtPassword.Focus();
                return;
            }

            Operator op;
            try
            {
                op = _app.Operators.Authenticate(user, password);
            }
            catch (Exception ex)
            {
                _lblMessage.Text = "登录失败：" + ex.Message;
                return;
            }

            if (op == null)
            {
                _lblMessage.Text = "账号或密码不正确。";
                _txtPassword.SelectAll();
                _txtPassword.Focus();
                return;
            }

            _app.CurrentOperator = op;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
