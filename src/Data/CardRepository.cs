using System;
using System.Collections.Generic;
using MealCardSystem.Model;
using MealCardSystem.Util;

namespace MealCardSystem.Data
{
    /// <summary>饭卡档案的存取。</summary>
    public class CardRepository
    {
        private const string Columns =
            "id, epc, card_no, holder_name, holder_id, department, balance_cents, status, created_at, updated_at";

        private readonly SqliteConnection _conn;

        public CardRepository(SqliteConnection conn)
        {
            _conn = conn;
        }

        private static Card Map(object[] r)
        {
            return new Card
            {
                Id = Convert.ToInt64(r[0]),
                Epc = Convert.ToString(r[1]),
                CardNo = Convert.ToString(r[2]),
                HolderName = Convert.ToString(r[3]),
                HolderId = Convert.ToString(r[4]),
                Department = Convert.ToString(r[5]),
                BalanceCents = Convert.ToInt64(r[6]),
                Status = (CardStatus)Convert.ToInt32(r[7]),
                CreatedAt = Convert.ToDateTime(r[8]),
                UpdatedAt = Convert.ToDateTime(r[9])
            };
        }

        /// <summary>按 EPC 查卡（EPC 已做规整，大小写与分隔符不影响查询）。</summary>
        public Card GetByEpc(string epc)
        {
            string key = Hex.NormalizeEpc(epc);
            if (key.Length == 0)
            {
                return null;
            }
            List<object[]> rows = _conn.Query(
                "SELECT " + Columns + " FROM cards WHERE epc = ?1", key);
            return rows.Count == 0 ? null : Map(rows[0]);
        }

        public Card GetById(long id)
        {
            List<object[]> rows = _conn.Query(
                "SELECT " + Columns + " FROM cards WHERE id = ?1", id);
            return rows.Count == 0 ? null : Map(rows[0]);
        }

        public Card GetByCardNo(string cardNo)
        {
            List<object[]> rows = _conn.Query(
                "SELECT " + Columns + " FROM cards WHERE card_no = ?1", cardNo);
            return rows.Count == 0 ? null : Map(rows[0]);
        }

        /// <summary>
        /// 按关键字模糊查询卡号 / 姓名 / 工号 / EPC，可按状态过滤。
        /// keyword 为空则返回全部。
        /// </summary>
        public List<Card> Search(string keyword, CardStatus? status)
        {
            List<Card> result = new List<Card>();

            string sql = "SELECT " + Columns + " FROM cards WHERE 1 = 1";
            List<object> args = new List<object>();

            if (!string.IsNullOrEmpty(keyword) && keyword.Trim().Length > 0)
            {
                string like = "%" + keyword.Trim() + "%";
                sql += " AND (card_no LIKE ?1 OR holder_name LIKE ?1 OR holder_id LIKE ?1 OR epc LIKE ?1)";
                args.Add(like);
            }

            if (status.HasValue)
            {
                sql += " AND status = ?" + (args.Count + 1);
                args.Add((int)status.Value);
            }

            sql += " ORDER BY id DESC";

            List<object[]> rows = _conn.Query(sql, args.ToArray());
            foreach (object[] r in rows)
            {
                result.Add(Map(r));
            }
            return result;
        }

        public List<Card> GetAll()
        {
            return Search(null, null);
        }

        /// <summary>
        /// 生成下一个可用卡号。
        ///
        /// 同时看「现存卡片」和「历史流水里的卡号快照」两处：注销会把卡片档案删掉，
        /// 只看 cards 表的话，删掉 10003 之后下一个卡号又是 10003，同一个卡号先后
        /// 属于两个人，翻查历史时会很费解。
        /// </summary>
        public string NextCardNo()
        {
            // 只考虑纯数字卡号，避免历史数据里的非数字卡号影响递增
            List<object[]> rows = _conn.Query(
                "SELECT card_no FROM (" +
                "  SELECT card_no FROM cards        WHERE card_no GLOB '[0-9]*' " +
                "  UNION ALL " +
                "  SELECT card_no FROM transactions WHERE card_no GLOB '[0-9]*' " +
                ") ORDER BY LENGTH(card_no) DESC, card_no DESC LIMIT 1");

            long next = 10001; // 起始卡号
            if (rows.Count > 0)
            {
                long max;
                if (long.TryParse(Convert.ToString(rows[0][0]), out max))
                {
                    next = max + 1;
                }
            }
            return next.ToString();
        }

        // ---------- 写入 ----------

        public long Insert(Card card)
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            card.CreatedAt = DateTime.Now;
            card.UpdatedAt = DateTime.Now;

            return _conn.ExecuteInsert(
                "INSERT INTO cards (epc, card_no, holder_name, holder_id, department, balance_cents, status, created_at, updated_at) " +
                "VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)",
                Hex.NormalizeEpc(card.Epc),
                card.CardNo,
                card.HolderName,
                card.HolderId == null ? "" : card.HolderId,
                card.Department == null ? "" : card.Department,
                card.BalanceCents,
                (int)card.Status,
                now, now);
        }

        /// <summary>
        /// 删除卡片档案。注销退卡时调用：这张卡在本地被彻底抹掉，回到空白卡状态，
        /// 可以立刻重新发给别人。
        ///
        /// 已经发生的交易流水不受影响 —— 卡号与持卡人在记账那一刻就已快照进流水行，
        /// 不依赖这张卡片档案还在不在。
        /// </summary>
        public void Delete(long cardId)
        {
            _conn.ExecuteNonQuery("DELETE FROM cards WHERE id = ?1", cardId);
        }

        /// <summary>直接设定余额。业务层必须在事务中调用，并同步写入流水。</summary>
        public void SetBalance(long cardId, long balanceCents)
        {
            _conn.ExecuteNonQuery(
                "UPDATE cards SET balance_cents = ?1, updated_at = ?2 WHERE id = ?3",
                balanceCents,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                cardId);
        }

        public void SetStatus(long cardId, CardStatus status)
        {
            _conn.ExecuteNonQuery(
                "UPDATE cards SET status = ?1, updated_at = ?2 WHERE id = ?3",
                (int)status,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                cardId);
        }

        /// <summary>卡片统计：总数 / 各状态数量 / 余额合计（分）。</summary>
        public void GetStatistics(out int total, out int normal, out int lost, out int cancelled, out long totalBalanceCents)
        {
            total = (int)_conn.QueryInt64("SELECT COUNT(*) FROM cards");
            normal = (int)_conn.QueryInt64("SELECT COUNT(*) FROM cards WHERE status = 1");
            lost = (int)_conn.QueryInt64("SELECT COUNT(*) FROM cards WHERE status = 2");
            cancelled = (int)_conn.QueryInt64("SELECT COUNT(*) FROM cards WHERE status = 3");
            totalBalanceCents = _conn.QueryInt64("SELECT COALESCE(SUM(balance_cents), 0) FROM cards WHERE status <> 3");
        }
    }
}
