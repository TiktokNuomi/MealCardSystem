using System;
using MealCardSystem.Data;
using MealCardSystem.Model;
using MealCardSystem.Reader;

namespace MealCardSystem.Service
{
    /// <summary>
    /// 应用级上下文：持有数据库连接、各仓储/服务以及读卡器实例，
    /// 供各窗体共享，避免到处传递连接对象。
    /// </summary>
    public class AppSession : IDisposable
    {
        private bool _disposed;

        public SqliteConnection Connection { get; private set; }
        public CardRepository Cards { get; private set; }
        public TransactionRepository Transactions { get; private set; }
        public OperatorRepository Operators { get; private set; }

        public CardService CardService { get; private set; }
        public AccountService AccountService { get; private set; }
        public ReportService ReportService { get; private set; }

        public RfidReader Reader { get; private set; }

        /// <summary>当前登录的操作员。登录成功后由登录窗体赋值。</summary>
        public Operator CurrentOperator { get; set; }

        /// <summary>数据库文件路径，显示在标题栏便于排查。</summary>
        public string DatabasePath { get; private set; }

        /// <summary>本次启动是否新建了默认管理员（界面据此提示尽快改密）。</summary>
        public bool CreatedDefaultAdmin { get; private set; }

        private AppSession()
        {
        }

        /// <summary>打开数据库、建表、准备服务。</summary>
        public static AppSession Start(string databasePath)
        {
            AppSession s = new AppSession();
            s.DatabasePath = databasePath;
            s.Connection = Database.OpenAndInitialize(databasePath);

            s.Cards = new CardRepository(s.Connection);
            s.Transactions = new TransactionRepository(s.Connection);
            s.Operators = new OperatorRepository(s.Connection);

            s.CardService = new CardService(s.Connection, s.Cards, s.Transactions);
            s.AccountService = new AccountService(s.Connection, s.Cards, s.Transactions);
            s.ReportService = new ReportService(s.Transactions);

            s.Reader = new RfidReader();

            s.CreatedDefaultAdmin = s.Operators.EnsureDefaultAdmin();

            return s;
        }

        /// <summary>当前操作员 id，未登录时为 null。</summary>
        public long? CurrentOperatorId
        {
            get { return CurrentOperator == null ? (long?)null : CurrentOperator.Id; }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            if (Reader != null)
            {
                Reader.Dispose();
                Reader = null;
            }
            if (Connection != null)
            {
                Connection.Dispose();
                Connection = null;
            }
        }
    }
}
