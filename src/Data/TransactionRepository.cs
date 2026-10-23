using System;
using System.Collections.Generic;
using MealCardSystem.Model;

namespace MealCardSystem.Data
{
    /// <summary>交易流水的存取。流水只增不改，是所有余额变动的凭证。</summary>
    public class TransactionRepository
    {
        private readonly SqliteConnection _conn;

        public TransactionRepository(SqliteConnection conn)
        {
            _conn = conn;
        }

        /// <summary>
        /// 写入一条流水。必须在与余额更新同一个事务内调用，
        /// 以保证「余额」与「流水」永远一致。
        ///
        /// cardNo / holderName 是交易时点的快照，随流水一起存下来：注销会删掉卡片档案，
        /// 卡片也可能换人重发，只有快照能保证这条历史凭证始终显示当时的那个人。
        /// </summary>
        public long Insert(long cardId, string epc, string cardNo, string holderName,
                           TransactionType type, long amountCents,
                           long balanceAfterCents, long? operatorId, string remark)
        {
            return _conn.ExecuteInsert(
                "INSERT INTO transactions (card_id, epc, card_no, holder_name, type, amount_cents, balance_after_cents, operator_id, remark, created_at) " +
                "VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)",
                cardId,
                epc,
                cardNo == null ? "" : cardNo,
                holderName == null ? "" : holderName,
                (int)type,
                amountCents,
                balanceAfterCents,
                operatorId.HasValue ? (object)operatorId.Value : null,
                remark == null ? "" : remark,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private static Transaction Map(object[] r)
        {
            return new Transaction
            {
                Id = Convert.ToInt64(r[0]),
                CardId = Convert.ToInt64(r[1]),
                Epc = Convert.ToString(r[2]),
                Type = (TransactionType)Convert.ToInt32(r[3]),
                AmountCents = Convert.ToInt64(r[4]),
                BalanceAfterCents = Convert.ToInt64(r[5]),
                OperatorName = r[6] == null ? "" : Convert.ToString(r[6]),
                HolderName = r[7] == null ? "" : Convert.ToString(r[7]),
                CardNo = r[8] == null ? "" : Convert.ToString(r[8]),
                Remark = r[9] == null ? "" : Convert.ToString(r[9]),
                CreatedAt = Convert.ToDateTime(r[10])
            };
        }

        // 卡号与持卡人优先取流水里自带的快照；联表的兜底只为兼容极老的、
        // 迁移时卡片档案就已经不在库里的数据行。卡片被删掉之后联表只会得到 NULL。
        private const string SelectSql =
            "SELECT t.id, t.card_id, t.epc, t.type, t.amount_cents, t.balance_after_cents, " +
            "       o.display_name, " +
            "       COALESCE(t.holder_name, c.holder_name), COALESCE(t.card_no, c.card_no), " +
            "       t.remark, t.created_at " +
            "FROM transactions t " +
            "LEFT JOIN operators o ON o.id = t.operator_id " +
            "LEFT JOIN cards c ON c.id = t.card_id ";

        /// <summary>
        /// 按条件查询流水。from/to 为闭区间（含当天），type 为空表示不限类型，
        /// keyword 匹配卡号 / 持卡人姓名 / EPC。
        /// </summary>
        public List<Transaction> Query(DateTime? from, DateTime? to, TransactionType? type,
                                       string keyword, int limit)
        {
            List<Transaction> result = new List<Transaction>();

            string sql = SelectSql + " WHERE 1 = 1";
            List<object> args = new List<object>();

            if (from.HasValue)
            {
                args.Add(from.Value.ToString("yyyy-MM-dd 00:00:00"));
                sql += " AND t.created_at >= ?" + args.Count;
            }
            if (to.HasValue)
            {
                args.Add(to.Value.ToString("yyyy-MM-dd 23:59:59"));
                sql += " AND t.created_at <= ?" + args.Count;
            }
            if (type.HasValue)
            {
                args.Add((int)type.Value);
                sql += " AND t.type = ?" + args.Count;
            }
            if (!string.IsNullOrEmpty(keyword) && keyword.Trim().Length > 0)
            {
                args.Add("%" + keyword.Trim() + "%");
                int idx = args.Count;
                sql += " AND (COALESCE(t.card_no, c.card_no) LIKE ?" + idx +
                       " OR COALESCE(t.holder_name, c.holder_name) LIKE ?" + idx +
                       " OR t.epc LIKE ?" + idx + ")";
            }

            sql += " ORDER BY t.id DESC";
            if (limit > 0)
            {
                args.Add(limit);
                sql += " LIMIT ?" + args.Count;
            }

            foreach (object[] r in _conn.Query(sql, args.ToArray()))
            {
                result.Add(Map(r));
            }
            return result;
        }

        /// <summary>某张卡的全部流水（新到旧）。</summary>
        public List<Transaction> QueryByCard(long cardId, int limit)
        {
            List<Transaction> result = new List<Transaction>();
            string sql = SelectSql + " WHERE t.card_id = ?1 ORDER BY t.id DESC";
            if (limit > 0)
            {
                sql += " LIMIT " + limit;
            }
            foreach (object[] r in _conn.Query(sql, cardId))
            {
                result.Add(Map(r));
            }
            return result;
        }

        /// <summary>指定时间段内各类型的金额合计，用于日结对账。</summary>
        public void GetSummary(DateTime? from, DateTime? to, out long issueCents, out long consumeCents,
                               out long rechargeCents, out long refundCents, out int count)
        {
            string sql = "SELECT " +
                "COALESCE(SUM(CASE WHEN type = 1 THEN amount_cents ELSE 0 END), 0), " +
                "COALESCE(SUM(CASE WHEN type = 2 THEN amount_cents ELSE 0 END), 0), " +
                "COALESCE(SUM(CASE WHEN type = 3 THEN amount_cents ELSE 0 END), 0), " +
                "COALESCE(SUM(CASE WHEN type = 4 THEN amount_cents ELSE 0 END), 0), " +
                "COUNT(*) FROM transactions WHERE 1 = 1";

            List<object> args = new List<object>();
            if (from.HasValue)
            {
                args.Add(from.Value.ToString("yyyy-MM-dd 00:00:00"));
                sql += " AND created_at >= ?" + args.Count;
            }
            if (to.HasValue)
            {
                args.Add(to.Value.ToString("yyyy-MM-dd 23:59:59"));
                sql += " AND created_at <= ?" + args.Count;
            }

            List<object[]> rows = _conn.Query(sql, args.ToArray());
            if (rows.Count == 0)
            {
                issueCents = consumeCents = rechargeCents = refundCents = 0;
                count = 0;
                return;
            }

            object[] r = rows[0];
            issueCents = Convert.ToInt64(r[0]);
            consumeCents = Convert.ToInt64(r[1]);
            rechargeCents = Convert.ToInt64(r[2]);
            refundCents = Convert.ToInt64(r[3]);
            count = Convert.ToInt32(r[4]);
        }
    }
}
