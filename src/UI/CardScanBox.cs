using System;
using System.Drawing;
using System.Windows.Forms;
using MealCardSystem.Model;
using MealCardSystem.Service;
using MealCardSystem.Util;

namespace MealCardSystem.UI
{
    /// <summary>
    /// 各功能面板共用的「当前卡片」显示区。
    ///
    /// 卡片来源有两个：主窗口轮询读卡器后调用 <see cref="SetEpc"/>，
    /// 或操作员点「手动输入」直接敲 EPC —— 后者让系统在没有读卡器时也能使用与演示。
    /// </summary>
    public class CardScanBox : Panel
    {
        private readonly AppSession _app;

        private Label _lblStatusBadge;
        private Label _lblEpc;
        private Label _lblHolder;
        private Label _lblMeta;
        private Label _lblBalance;
        private Label _lblBalanceCaption;
        private Label _lblHint;

        private string _epc;
        private Card _card;

        /// <summary>当前卡片发生变化时触发（刷卡、手动输入、清除、余额变动后刷新）。</summary>
        public event EventHandler CardChanged;

        public CardScanBox(AppSession app)
        {
            _app = app;
            BuildUi();
            ShowEmpty();
        }

        /// <summary>当前卡片的 EPC，未读卡时为 null。</summary>
        public string Epc
        {
            get { return _epc; }
        }

        /// <summary>当前卡片的档案记录；卡片尚未登记时为 null。</summary>
        public Card Card
        {
            get { return _card; }
        }

        /// <summary>已读到卡（不论是否已登记）。</summary>
        public bool HasEpc
        {
            get { return !string.IsNullOrEmpty(_epc); }
        }

        /// <summary>已读到卡且该卡已经登记过。</summary>
        public bool HasRegisteredCard
        {
            get { return _card != null; }
        }

        /// <summary>诊断用：当前显示内容的摘要，用来核对界面是否与数据库一致。</summary>
        public string Summary
        {
            get
            {
                return "EPC=" + (_epc == null ? "(无)" : _epc) +
                       " 状态=" + (_lblStatusBadge.Visible ? _lblStatusBadge.Text : "(无)") +
                       " 持卡人=" + _lblHolder.Text +
                       " 余额=" + _lblBalance.Text;
            }
        }

        private void BuildUi()
        {
            BackColor = UiKit.SurfaceColor;
            Height = 168;

            // ---- 顶行：标题 + 状态徽标 ----
            Label title = UiKit.Label("当前卡片", UiKit.FontBold, UiKit.TextColor);
            title.Location = new Point(20, 12);
            Controls.Add(title);

            _lblStatusBadge = UiKit.Label("", UiKit.FontSmall, Color.White);
            _lblStatusBadge.AutoSize = false;
            _lblStatusBadge.Size = new Size(64, 22);
            _lblStatusBadge.TextAlign = ContentAlignment.MiddleCenter;
            _lblStatusBadge.Location = new Point(92, 11);
            _lblStatusBadge.Visible = false;
            Controls.Add(_lblStatusBadge);

            // ---- 左列：EPC 与持卡人信息 ----
            _lblEpc = UiKit.Label("", new Font("Consolas", 15f, FontStyle.Bold), UiKit.TextColor);
            _lblEpc.Location = new Point(20, 42);
            Controls.Add(_lblEpc);

            _lblHolder = UiKit.Label("", new Font(UiKit.FontSubtitle.FontFamily, 13f, FontStyle.Bold), UiKit.TextColor);
            _lblHolder.Location = new Point(20, 78);
            Controls.Add(_lblHolder);

            _lblMeta = UiKit.Label("", UiKit.FontSmall, UiKit.MutedColor);
            _lblMeta.Location = new Point(20, 108);
            Controls.Add(_lblMeta);

            _lblHint = UiKit.Label("", UiKit.FontSmall, UiKit.FaintColor);
            _lblHint.Location = new Point(20, 108);
            Controls.Add(_lblHint);

            // ---- 右列：余额 ----
            _lblBalanceCaption = UiKit.Label("余额（元）", UiKit.FontSmall, UiKit.MutedColor);
            _lblBalanceCaption.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblBalanceCaption.Location = new Point(560, 40);
            Controls.Add(_lblBalanceCaption);

            _lblBalance = UiKit.Label("", UiKit.FontAmount, UiKit.SuccessColor);
            _lblBalance.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblBalance.Location = new Point(556, 58);
            Controls.Add(_lblBalance);

            // ---- 底部：手动输入 ----
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 40;
            bottom.BackColor = Color.FromArgb(0xFA, 0xFB, 0xFC);

            Label lblManual = UiKit.Label("手动输入 EPC：", UiKit.FontSmall, UiKit.MutedColor);
            lblManual.Location = new Point(20, 12);
            bottom.Controls.Add(lblManual);

            TextBox txt = UiKit.TextBox();
            txt.Location = new Point(120, 9);
            txt.Width = 300;
            txt.Font = new Font("Consolas", 9.5f);
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ApplyManualInput(txt.Text);
                }
            };
            bottom.Controls.Add(txt);

            Button btnUse = UiKit.Button("读这张卡", "normal");
            btnUse.Location = new Point(428, 7);
            btnUse.Width = 90;
            btnUse.Height = 26;
            btnUse.Font = UiKit.FontSmall;
            btnUse.Click += delegate { ApplyManualInput(txt.Text); };
            bottom.Controls.Add(btnUse);

            Button btnClear = UiKit.Button("清除", "normal");
            btnClear.Location = new Point(524, 7);
            btnClear.Width = 70;
            btnClear.Height = 26;
            btnClear.Font = UiKit.FontSmall;
            btnClear.Click += delegate
            {
                txt.Clear();
                Clear();
            };
            bottom.Controls.Add(btnClear);

            Controls.Add(bottom);

            Resize += delegate { LayoutBalance(); };
            LayoutBalance();
        }

        private void LayoutBalance()
        {
            // 余额靠右显示，窗口变宽时跟着移动
            int right = Width - 30;
            _lblBalance.Left = right - _lblBalance.Width;
            _lblBalanceCaption.Left = right - _lblBalanceCaption.Width;
        }

        private void ApplyManualInput(string text)
        {
            string epc = Hex.NormalizeEpc(text);
            if (!Hex.IsValidEpc(epc))
            {
                UiKit.Warn(this, "请输入有效的卡片 EPC（十六进制，至少 2 个字节，例如 E2801160600002054E3A1B2C）。");
                return;
            }
            SetEpc(epc);
        }

        /// <summary>由主窗口在读卡器轮询到卡片时调用。EPC 相同则忽略，避免重复刷新。</summary>
        public void SetEpc(string epc)
        {
            string normalized = Hex.NormalizeEpc(epc);
            if (normalized.Length == 0)
            {
                return;
            }
            if (normalized == _epc)
            {
                return;
            }

            _epc = normalized;
            _card = _app.CardService.FindByEpc(normalized);
            Render();
            RaiseCardChanged();
        }

        /// <summary>余额变化或卡片状态变化后，从数据库重新读取一次。</summary>
        public void Reload()
        {
            if (!HasEpc)
            {
                return;
            }
            _card = _app.CardService.FindByEpc(_epc);
            Render();
            RaiseCardChanged();
        }

        public void Clear()
        {
            _epc = null;
            _card = null;
            ShowEmpty();
            RaiseCardChanged();
        }

        private void RaiseCardChanged()
        {
            EventHandler handler = CardChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        // ---------- 渲染 ----------

        private void ShowEmpty()
        {
            _lblEpc.Text = "等待刷卡…";
            _lblEpc.ForeColor = UiKit.FaintColor;
            _lblHolder.Text = "";
            _lblMeta.Text = "";
            _lblHint.Text = "请让持卡人把卡靠近读卡器；若读卡器未连接，可在下方手动输入 EPC。";
            _lblHint.Visible = true;
            _lblBalance.Text = "—";
            _lblBalance.ForeColor = UiKit.FaintColor;
            _lblStatusBadge.Visible = false;
            LayoutBalance();
        }

        private void Render()
        {
            _lblEpc.Text = FormatEpc(_epc);
            _lblEpc.ForeColor = UiKit.TextColor;

            if (_card == null)
            {
                // 读到了卡，但这张卡还没登记
                _lblHolder.Text = "该卡尚未登记";
                _lblHolder.ForeColor = UiKit.WarningColor;
                _lblMeta.Text = "";
                _lblHint.Text = "这是一张空白卡。若要作为饭卡使用，请到「发卡」办理。";
                _lblHint.Visible = true;
                _lblBalance.Text = "—";
                _lblBalance.ForeColor = UiKit.FaintColor;
                SetBadge("未登记", UiKit.WarningColor);
                LayoutBalance();
                return;
            }

            _lblHolder.Text = _card.HolderName;
            _lblHolder.ForeColor = UiKit.TextColor;

            string meta = "卡号 " + _card.CardNo;
            if (!string.IsNullOrEmpty(_card.HolderId))
            {
                meta += "　·　工号 " + _card.HolderId;
            }
            if (!string.IsNullOrEmpty(_card.Department))
            {
                meta += "　·　" + _card.Department;
            }
            _lblMeta.Text = meta;
            _lblHint.Visible = false;

            _lblBalance.Text = _card.BalanceText;
            _lblBalance.ForeColor = (_card.Status == CardStatus.Normal)
                ? UiKit.SuccessColor
                : UiKit.MutedColor;

            SetBadge(_card.StatusText, StatusColor(_card.Status));
            LayoutBalance();
        }

        private void SetBadge(string text, Color color)
        {
            _lblStatusBadge.Text = text;
            _lblStatusBadge.BackColor = color;
            _lblStatusBadge.Visible = true;
        }

        private static Color StatusColor(CardStatus status)
        {
            switch (status)
            {
                case CardStatus.Normal: return UiKit.SuccessColor;
                case CardStatus.Lost: return UiKit.WarningColor;
                default: return UiKit.FaintColor;
            }
        }

        /// <summary>把长 EPC 每 4 个字符加一个空格，便于人工核对。</summary>
        private static string FormatEpc(string epc)
        {
            if (string.IsNullOrEmpty(epc))
            {
                return "";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < epc.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    sb.Append(' ');
                }
                sb.Append(epc[i]);
            }
            return sb.ToString();
        }
    }
}
