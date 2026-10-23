using System;
using System.IO;

namespace MealCardSystem.Data
{
    /// <summary>数据库的建立与初始化。</summary>
    public static class Database
    {
        /// <summary>默认数据库文件位置：程序目录下的 data\mealcard.db。</summary>
        public static string DefaultPath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(Path.Combine(baseDir, "data"), "mealcard.db");
            }
        }

        /// <summary>打开（必要时创建）数据库并确保表结构就绪。</summary>
        public static SqliteConnection OpenAndInitialize(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            SqliteConnection conn = SqliteConnection.Open(path);
            try
            {
                Initialize(conn);
                return conn;
            }
            catch
            {
                conn.Dispose();
                throw;
            }
        }

        /// <summary>建表（幂等，可重复执行）。</summary>
        public static void Initialize(SqliteConnection conn)
        {
            conn.ExecuteScript(@"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS operators (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    username      TEXT    NOT NULL UNIQUE,
    password_hash TEXT    NOT NULL,
    salt          TEXT    NOT NULL,
    display_name  TEXT,
    is_admin      INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS cards (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    epc           TEXT    NOT NULL UNIQUE,
    card_no       TEXT    NOT NULL UNIQUE,
    holder_name   TEXT    NOT NULL,
    holder_id     TEXT,
    department    TEXT,
    balance_cents INTEGER NOT NULL DEFAULT 0,
    status        INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT    NOT NULL,
    updated_at    TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS transactions (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    card_id             INTEGER NOT NULL,
    epc                 TEXT    NOT NULL,
    card_no             TEXT,
    holder_name         TEXT,
    type                INTEGER NOT NULL,
    amount_cents        INTEGER NOT NULL,
    balance_after_cents INTEGER NOT NULL,
    operator_id         INTEGER,
    remark              TEXT,
    created_at          TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_tx_card    ON transactions(card_id, created_at);
CREATE INDEX IF NOT EXISTS idx_tx_time    ON transactions(created_at);
CREATE INDEX IF NOT EXISTS idx_tx_type    ON transactions(type);
CREATE INDEX IF NOT EXISTS idx_cards_name ON cards(holder_name);
");

                Migrate(conn);
        }

        /// <summary>
        /// 老版本的流水表只有 card_id，「卡号」和「持卡人」是查询时联 cards 表取出来的。
        /// 现在注销会真的删掉卡片档案，联表就查不到人了，所以把这两项按「交易时点的快照」
        /// 直接存进流水行 —— 顺带也修掉了另一个老问题：卡片换人重发之后，
        /// 历史流水会显示成新持卡人的名字，等于把凭证改掉了。
        /// </summary>
        private static void Migrate(SqliteConnection conn)
        {
            EnsureColumn(conn, "transactions", "card_no", "TEXT");
            EnsureColumn(conn, "transactions", "holder_name", "TEXT");

            // 已有流水按当前卡片档案回填一次；卡片早被删掉的就只能留空，
            // 但 EPC 仍在流水里，依然查得到。
            conn.ExecuteNonQuery(
                "UPDATE transactions SET " +
                "  card_no     = COALESCE(card_no,     (SELECT c.card_no     FROM cards c WHERE c.id = transactions.card_id)), " +
                "  holder_name = COALESCE(holder_name, (SELECT c.holder_name FROM cards c WHERE c.id = transactions.card_id)) " +
                "WHERE card_no IS NULL OR holder_name IS NULL");

            // 老版本注销是把卡留在库里标记成「已注销」，新版本是注销即删除。
            // 上面已经把这些卡的历史快照回填进流水，记录不会再丢，所以这里直接清掉，
            // 让它们真正回到空白卡状态。
            //
            // 必须清：留着的话「发卡」页面读到的仍是一张已登记过的卡，
            // 「确认发卡」按钮会一直是灰的，操作员根本没法把这张卡重新发出去。
            conn.ExecuteNonQuery("DELETE FROM cards WHERE status = 3");
        }

        /// <summary>表里没有这一列就补上（SQLite 的 ALTER TABLE 只支持加列）。</summary>
        private static void EnsureColumn(SqliteConnection conn, string table, string column, string type)
        {
            foreach (object[] r in conn.Query("PRAGMA table_info(" + table + ")"))
            {
                if (string.Equals(Convert.ToString(r[1]), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            conn.ExecuteNonQuery("ALTER TABLE " + table + " ADD COLUMN " + column + " " + type);
        }
    }
}
