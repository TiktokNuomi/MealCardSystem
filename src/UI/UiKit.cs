using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MealCardSystem.UI
{
    /// <summary>
    /// 界面主题与控件工厂。
    ///
    /// 本工程不使用 Visual Studio 设计器（本机没有 VS），所有界面均由代码构建，
    /// 因此把颜色、字体、常用控件的构造集中在这里，保证各界面观感一致。
    /// </summary>
    public static class UiKit
    {
        // ---------- 配色 ----------
        public static readonly Color BackColor = Color.FromArgb(0xF4, 0xF5, 0xF7);
        public static readonly Color SurfaceColor = Color.White;
        public static readonly Color BorderColor = Color.FromArgb(0xE3, 0xE6, 0xEB);

        public static readonly Color TextColor = Color.FromArgb(0x1F, 0x29, 0x37);
        public static readonly Color MutedColor = Color.FromArgb(0x6B, 0x72, 0x80);
        public static readonly Color FaintColor = Color.FromArgb(0x9C, 0xA3, 0xAF);

        public static readonly Color PrimaryColor = Color.FromArgb(0x2D, 0x7F, 0xF9);
        public static readonly Color PrimaryDarkColor = Color.FromArgb(0x1D, 0x63, 0xD0);
        public static readonly Color SuccessColor = Color.FromArgb(0x15, 0x9A, 0x5B);
        public static readonly Color DangerColor = Color.FromArgb(0xD9, 0x36, 0x36);
        public static readonly Color WarningColor = Color.FromArgb(0xD9, 0x7A, 0x06);
        public static readonly Color HeaderColor = Color.FromArgb(0x1E, 0x3A, 0x5F);

        // ---------- 字体 ----------
        private static readonly string Family = PickFamily();

        private static string PickFamily()
        {
            // 优先使用观感更好的中文字体，缺失时回退
            string[] preferred = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei" };
            foreach (string name in preferred)
            {
                try
                {
                    using (Font f = new Font(name, 9f))
                    {
                        if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)
                            || f.Name.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0
                            || f.Name.IndexOf("SimHei", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return name;
                        }
                    }
                }
                catch (Exception)
                {
                    // 该字体不存在，继续尝试下一个
                }
            }
            return FontFamily.GenericSansSerif.Name;
        }

        public static Font FontNormal { get { return new Font(Family, 9.5f); } }
        public static Font FontSmall { get { return new Font(Family, 8.5f); } }
        public static Font FontBold { get { return new Font(Family, 9.5f, FontStyle.Bold); } }
        public static Font FontSubtitle { get { return new Font(Family, 12f, FontStyle.Bold); } }
        public static Font FontTitle { get { return new Font(Family, 16f, FontStyle.Bold); } }
        public static Font FontAmount { get { return new Font(Family, 30f, FontStyle.Bold); } }
        public static Font FontAmountMid { get { return new Font(Family, 20f, FontStyle.Bold); } }

        // ---------- 控件工厂 ----------

        public static Label Label(string text)
        {
            return Label(text, FontNormal, TextColor);
        }

        public static Label Label(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Label MutedLabel(string text)
        {
            return Label(text, FontSmall, MutedColor);
        }

        public static TextBox TextBox()
        {
            TextBox t = new TextBox();
            t.Font = FontNormal;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.ForeColor = TextColor;
            return t;
        }

        public static ComboBox ComboBox()
        {
            ComboBox c = new ComboBox();
            c.Font = FontNormal;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Color.White;
            return c;
        }

        /// <summary>创建按钮。kind 取值：primary / success / danger / warning / normal。</summary>
        public static Button Button(string text, string kind)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = FontBold;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.Height = 36;

            Color back;
            switch (kind)
            {
                case "primary": back = PrimaryColor; break;
                case "success": back = SuccessColor; break;
                case "danger": back = DangerColor; break;
                case "warning": back = WarningColor; break;
                default: back = Color.White; break;
            }

            b.BackColor = back;
            b.ForeColor = (kind == "normal") ? TextColor : Color.White;
            if (kind == "normal")
            {
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = BorderColor;
            }

            // 悬停时略微加深，给出可点击的反馈
            b.FlatAppearance.MouseOverBackColor = Darken(back, 0.08);
            b.FlatAppearance.MouseDownBackColor = Darken(back, 0.16);

            return b;
        }

        /// <summary>左侧导航按钮。</summary>
        public static Button NavButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = FontNormal;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.White;
            b.ForeColor = TextColor;
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Padding = new Padding(18, 0, 0, 0);
            b.Height = 46;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xEE, 0xF4, 0xFE);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xDD, 0xEA, 0xFD);
            return b;
        }

        /// <summary>创建一块白色卡片面板，带 1 像素边框。</summary>
        public static Panel Card()
        {
            Panel p = new Panel();
            p.BackColor = SurfaceColor;
            p.Paint += PaintBorder;
            return p;
        }

        private static void PaintBorder(object sender, PaintEventArgs e)
        {
            Control c = (Control)sender;
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
            }
        }

        /// <summary>把 DataGridView 调成统一的清爽样式。</summary>
        public static void StyleGrid(DataGridView g)
        {
            g.Font = FontNormal;
            g.BackgroundColor = SurfaceColor;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = BorderColor;
            g.EnableHeadersVisualStyles = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.ReadOnly = true;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.RowHeadersVisible = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 34;

            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(0xF9, 0xFA, 0xFB);
            g.ColumnHeadersDefaultCellStyle.ForeColor = MutedColor;
            g.ColumnHeadersDefaultCellStyle.Font = FontBold;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(0xF9, 0xFA, 0xFB);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);

            g.DefaultCellStyle.BackColor = SurfaceColor;
            g.DefaultCellStyle.ForeColor = TextColor;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xE8, 0xF1, 0xFE);
            g.DefaultCellStyle.SelectionForeColor = TextColor;
            g.DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
            g.RowTemplate.Height = 30;

            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(0xFC, 0xFD, 0xFE);
        }

        public static Color Darken(Color c, double amount)
        {
            int r = (int)(c.R * (1 - amount));
            int g = (int)(c.G * (1 - amount));
            int b = (int)(c.B * (1 - amount));
            return Color.FromArgb(Math.Max(0, r), Math.Max(0, g), Math.Max(0, b));
        }

        /// <summary>提示框排版用的字体族（供 MessageDialog 拼各种字号）。</summary>
        public static string FontFamilyName
        {
            get { return Family; }
        }

        // ---------- 消息提示 ----------
        //
        // 这四个方法以前直接调系统的 MessageBox。MessageBox 固定用系统的消息框字体
        // （约 9pt）而且没有任何接口能改，收银台上要一眼看清「扣了多少、还剩多少」
        // 实在太小，所以改成自绘的 MessageDialog。调用方的签名保持不变。

        public static void Info(IWin32Window owner, string message)
        {
            MessageDialog.Show(owner, "提示", message, MessageBoxIcon.Information, false);
        }

        public static void Warn(IWin32Window owner, string message)
        {
            MessageDialog.Show(owner, "提示", message, MessageBoxIcon.Warning, false);
        }

        public static void Error(IWin32Window owner, string message)
        {
            MessageDialog.Show(owner, "出错了", message, MessageBoxIcon.Error, false);
        }

        public static bool Confirm(IWin32Window owner, string message)
        {
            return MessageDialog.Confirm(owner, "请确认", message, MessageBoxIcon.Question);
        }
    }

    /// <summary>
    /// 自绘的提示框，取代系统 MessageBox。
    ///
    /// 排版规则很朴素，正好对得上本工程所有提示文案的既有写法：
    /// 第一行当标题（大号加粗、按提示类型着色），空行作段落间距，
    /// 形如「标签：值」的行拆成两列、值加粗放大，其余整行按正文排。
    /// 于是「本次消费：8.00 元」「卡内余额：92.00 元」这类关键数字
    /// 会明显大过标签，收银时一眼就能读到。
    ///
    /// 字体全部登记在 _ownedFonts 里随窗体释放 —— 这个框每笔交易都要弹一次，
    /// 一天几百次，漏掉 GDI 句柄迟早会把进程耗尽。
    /// </summary>
    internal class MessageDialog : Form
    {
        private const int ContentWidth  = 560;   // 正文区宽度
        private const int MaxLabelWidth = 132;   // 「标签：」一列的封顶宽度
        private const int PadLeft       = 26;
        private const int PadTop        = 22;
        private const int PadBottom     = 20;

        private readonly List<Font> _ownedFonts = new List<Font>();

        private Font _headingFont;
        private Font _labelFont;
        private Font _valueFont;
        private Font _bodyFont;

        private bool _accepted;

        private MessageDialog(string caption, string message, Color accent, bool cancellable)
        {
            BuildUi(caption, message, accent, cancellable);
        }

        /// <summary>弹一个只有「确定」的提示框。</summary>
        public static void Show(IWin32Window owner, string caption, string message,
                                MessageBoxIcon icon, bool cancellable)
        {
            using (MessageDialog d = new MessageDialog(caption, message, AccentOf(icon), cancellable))
            {
                d.ShowDialog(owner);
            }
        }

        /// <summary>弹一个确认框：点「确定」返回 true，点「取消」或按 Esc 返回 false。</summary>
        public static bool Confirm(IWin32Window owner, string caption, string message, MessageBoxIcon icon)
        {
            using (MessageDialog d = new MessageDialog(caption, message, AccentOf(icon), true))
            {
                d.ShowDialog(owner);
                return d._accepted;
            }
        }

        /// <summary>
        /// 供 --uismoke 诊断模式使用：造一个不显示的对话框，用来检查排版。
        /// 正常代码请走 <see cref="Show"/> 或 <see cref="Confirm"/>。
        /// </summary>
        public static MessageDialog CreateForDiagnostics(string caption, string message,
                                                         MessageBoxIcon icon, bool cancellable)
        {
            return new MessageDialog(caption, message, AccentOf(icon), cancellable);
        }

        private static Color AccentOf(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Error: return UiKit.DangerColor;
                case MessageBoxIcon.Warning: return UiKit.WarningColor;
                case MessageBoxIcon.Question: return UiKit.PrimaryColor;
                default: return UiKit.SuccessColor;
            }
        }

        private Font Own(Font font)
        {
            _ownedFonts.Add(font);
            return font;
        }

        // ---------- 界面 ----------

        private void BuildUi(string caption, string message, Color accent, bool cancellable)
        {
            Text = caption;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = UiKit.SurfaceColor;
            DoubleBuffered = true;

            _headingFont = Own(new Font(UiKit.FontFamilyName, 16f, FontStyle.Bold));
            _labelFont = Own(new Font(UiKit.FontFamilyName, 11f));
            _valueFont = Own(new Font(UiKit.FontFamilyName, 15f, FontStyle.Bold));
            _bodyFont = Own(new Font(UiKit.FontFamilyName, 12f));
            Font buttonFont = Own(new Font(UiKit.FontFamilyName, 12f, FontStyle.Bold));

            string[] lines = (message == null ? "" : message)
                             .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            Panel body = new Panel();
            body.BackColor = Color.Transparent;
            body.Location = new Point(PadLeft, PadTop);
            body.Width = ContentWidth;
            Controls.Add(body);

            // 第一遍：建好这一行的控件，同时记下它属于哪种行
            List<Row> rows = new List<Row>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();

                Row row = new Row();
                if (line.Length == 0)
                {
                    row.GapBefore = 12;   // 空行 = 段落间距
                }
                else if (i == 0)
                {
                    row.Text = NewLabel(line, _headingFont, accent);
                    row.Text.MaximumSize = new Size(ContentWidth, 0);
                    row.IsHeading = true;
                }
                else
                {
                    string label, value;
                    SplitLabel(line, out label, out value);

                    if (label == null)
                    {
                        row.Text = NewLabel(value, _bodyFont, UiKit.TextColor);
                        row.Text.MaximumSize = new Size(ContentWidth, 0);
                    }
                    else
                    {
                        row.Label = NewLabel(label, _labelFont, UiKit.MutedColor);
                        row.Value = NewLabel(value, _valueFont, UiKit.TextColor);
                        row.Value.MaximumSize = new Size(ContentWidth, 0);
                    }
                }

                rows.Add(row);
                if (row.Text != null) body.Controls.Add(row.Text);
                if (row.Label != null) body.Controls.Add(row.Label);
                if (row.Value != null) body.Controls.Add(row.Value);
            }

            // 第二遍：标签列按最宽的那个标签对齐，值自然成一竖列
            int labelColumn = 0;
            foreach (Row row in rows)
            {
                if (row.Label != null && row.Label.PreferredWidth > labelColumn)
                {
                    labelColumn = row.Label.PreferredWidth;
                }
            }
            labelColumn = Math.Min(MaxLabelWidth, labelColumn + 12);

            // 值是从 labelColumn 处起画的，宽度上限必须把这一列减掉，
            // 否则很长的值（比如一长串路径）会从右边溢出，被面板裁掉半截。
            foreach (Row row in rows)
            {
                if (row.Value != null)
                {
                    row.Value.MaximumSize = new Size(ContentWidth - labelColumn, 0);
                }
            }

            // 第三遍：纵向依次摆放
            int y = 0;
            foreach (Row row in rows)
            {
                y += row.GapBefore;

                if (row.Label != null)
                {
                    // 标签小、值大，让值的基线看起来是齐的
                    row.Label.Location = new Point(0, y + (row.Value.PreferredHeight - row.Label.PreferredHeight) / 2);
                    row.Value.Location = new Point(labelColumn, y);
                    y += Math.Max(row.Value.PreferredHeight, row.Label.PreferredHeight) + 8;
                }
                else if (row.Text != null)
                {
                    row.Text.Location = new Point(0, y);
                    y += row.Text.PreferredHeight + (row.IsHeading ? 16 : 8);
                }
            }

            // 必须把内容面板撑到实际高度：子控件会被父容器的客户区裁掉，
            // 留着默认的 100 像素高，下面几行在界面上直接就不见了。
            body.Height = y;

            // ---------- 按钮 ----------
            int buttonTop = PadTop + y + 12;
            int buttonHeight = 42;
            int buttonWidth = 124;

            Button ok = UiKit.Button(cancellable ? "确定" : "知道了", "primary");
            ok.Font = buttonFont;
            ok.Size = new Size(buttonWidth, buttonHeight);
            ok.Click += delegate { _accepted = true; Close(); };
            Controls.Add(ok);
            AcceptButton = ok;

            int right = ContentWidth + PadLeft;

            if (cancellable)
            {
                Button cancel = UiKit.Button("取消", "normal");
                cancel.Font = buttonFont;
                cancel.Size = new Size(buttonWidth, buttonHeight);
                cancel.Click += delegate { _accepted = false; Close(); };
                Controls.Add(cancel);
                CancelButton = cancel;   // Esc 等同于点取消

                cancel.Location = new Point(right - buttonWidth, buttonTop);
                ok.Location = new Point(right - buttonWidth * 2 - 12, buttonTop);
            }
            else
            {
                ok.Location = new Point(right - buttonWidth, buttonTop);
            }

            ClientSize = new Size(ContentWidth + PadLeft * 2, buttonTop + buttonHeight + PadBottom);
        }

        private static Label NewLabel(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            return l;
        }

        /// <summary>
        /// 把「标签：值」拆成两半。只认中文全角冒号：半角冒号在路径（D:\...）里很常见，
        /// 拆错了会把一整行路径当成标签。
        /// </summary>
        private static void SplitLabel(string line, out string label, out string value)
        {
            int i = line.IndexOf('：');
            if (i <= 0 || i >= line.Length - 1)
            {
                label = null;
                value = line;
                return;
            }
            label = line.Substring(0, i + 1);
            value = line.Substring(i + 1);
        }

        private class Row
        {
            public Label Text;      // 整行文字（标题或正文）
            public Label Label;     // 「标签：值」行的标签
            public Label Value;     // 「标签：值」行的值
            public bool IsHeading;
            public int GapBefore;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                foreach (Font f in _ownedFonts)
                {
                    f.Dispose();
                }
                _ownedFonts.Clear();
            }
        }
    }

    /// <summary>提供窗体无闪烁的圆角/边框绘制所需的双缓冲基类。</summary>
    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint, true);
        }
    }
}
