using System;
using System.Windows.Forms;
using MealCardSystem.Service;

namespace MealCardSystem.UI
{
    /// <summary>
    /// 各功能面板的基类。主窗口左侧导航切换的就是这些面板。
    /// </summary>
    public abstract class FunctionPanel : Panel
    {
        protected readonly AppSession App;
        protected readonly MainForm Host;

        protected FunctionPanel(AppSession app, MainForm host)
        {
            App = app;
            Host = host;
            Dock = DockStyle.Fill;
            BackColor = UiKit.BackColor;
            AutoScroll = true;
        }

        /// <summary>面板被切换到前台时调用，用于刷新列表、聚焦输入框。</summary>
        public virtual void OnActivated()
        {
        }

        /// <summary>读卡器读到一张新卡时调用。</summary>
        public virtual void OnCardScanned(string epc)
        {
        }

        /// <summary>
        /// 强制从数据库重新读一遍「当前卡片」。
        ///
        /// 每个面板各有一个自己的「当前卡片」显示区，卡片往往是在别的页面上被改掉的
        /// （在消费页扣了款、在卡片管理页注销了卡），所以每次切回本页都必须重新读库，
        /// 不能沿用上次渲染的结果，否则显示的余额和状态是旧的。
        /// </summary>
        public virtual void ReloadCard()
        {
        }

        /// <summary>诊断用：本面板「当前卡片」显示内容的摘要（没有该区域的面板返回空）。</summary>
        public virtual string CardSummary
        {
            get { return ""; }
        }

        /// <summary>读卡器连接状态变化时调用。</summary>
        public virtual void OnReaderStatusChanged()
        {
        }
    }
}
