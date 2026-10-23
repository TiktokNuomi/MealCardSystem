using System;
using MealCardSystem.Data;
using MealCardSystem.Model;
using MealCardSystem.Util;

namespace MealCardSystem.Service
{
    /// <summary>
    /// 发卡、挂失/解挂、注销/退卡。
    ///
    /// 所有会改变余额或状态的操作都在数据库事务内完成，并同步写入流水，
    /// 保证「卡片余额」与「流水累加」永远对得上。
    /// </summary>
    public class CardService
    {
        private readonly SqliteConnection _conn;
        private readonly CardRepository _cards;
        private readonly TransactionRepository _transactions;

        public CardService(SqliteConnection conn, CardRepository cards, TransactionRepository transactions)
        {
            _conn = conn;
            _cards = cards;
            _transactions = transactions;
        }

        /// <summary>按 EPC 取卡，未登记返回 null。</summary>
        public Card FindByEpc(string epc)
        {
            return _cards.GetByEpc(epc);
        }

        /// <summary>
        /// 发卡：把一张空白卡的 EPC 登记为饭卡。
        ///
        /// 注销会把卡片档案从本地删掉，所以注销过的卡在这里就是一张普通的空白卡，
        /// 直接按新卡发放即可，不需要任何「重新启用」的特殊路径。
        /// </summary>
        public Card IssueCard(string epc, string holderName, string holderId, string department,
                              long initialCents, long? operatorId)
        {
            string normalized = Hex.NormalizeEpc(epc);

            if (!Hex.IsValidEpc(normalized))
            {
                throw new BusinessException("卡片 EPC 无效，无法发卡。请重新读卡。");
            }
            if (string.IsNullOrEmpty(holderName) || holderName.Trim().Length == 0)
            {
                throw new BusinessException("请填写持卡人姓名。");
            }
            if (initialCents < 0)
            {
                throw new BusinessException("初始金额不能为负数。");
            }
            if (initialCents > Money.MaxCents)
            {
                throw new BusinessException("初始金额不能超过 " + Money.Format(Money.MaxCents) + " 元。");
            }

            Card existing = _cards.GetByEpc(normalized);

            if (existing != null && existing.Status == CardStatus.Cancelled)
            {
                // 旧版本把注销的卡留在库里、只标记状态，新规则是直接删除。
                // 碰到这种遗留记录顺手清掉，让这张卡回到空白卡状态。
                _cards.Delete(existing.Id);
                existing = null;
            }

            if (existing != null)
            {
                throw new BusinessException(
                    "这张卡已经登记过了。\n\n卡号：" + existing.CardNo +
                    "\n持卡人：" + existing.HolderName +
                    "\n状态：" + existing.StatusText +
                    "\n\n如需换人使用，请先到「卡片管理」注销原卡。");
            }

            Card result;

            _conn.BeginTransaction();
            try
            {
                Card card = new Card();
                card.Epc = normalized;
                card.CardNo = _cards.NextCardNo();
                card.HolderName = holderName.Trim();
                card.HolderId = holderId == null ? "" : holderId.Trim();
                card.Department = department == null ? "" : department.Trim();
                card.BalanceCents = initialCents;
                card.Status = CardStatus.Normal;

                long id = _cards.Insert(card);
                result = _cards.GetById(id);

                _transactions.Insert(id, normalized, result.CardNo, result.HolderName,
                                     TransactionType.Issue,
                                     initialCents, initialCents, operatorId, "开卡");

                _conn.Commit();
            }
            catch
            {
                _conn.Rollback();
                throw;
            }

            return result;
        }

        /// <summary>挂失或解挂。注销的卡不能改挂失状态。</summary>
        public void SetLost(long cardId, bool lost, long? operatorId)
        {
            Card card = _cards.GetById(cardId);
            if (card == null)
            {
                throw new BusinessException("未找到该卡片记录。");
            }
            if (card.Status == CardStatus.Cancelled)
            {
                throw new BusinessException("该卡已注销，无法再变更挂失状态。");
            }
            if (lost && card.Status == CardStatus.Lost)
            {
                throw new BusinessException("该卡已经处于挂失状态。");
            }
            if (!lost && card.Status != CardStatus.Lost)
            {
                throw new BusinessException("该卡当前不是挂失状态，无需解挂。");
            }

            _conn.BeginTransaction();
            try
            {
                _cards.SetStatus(cardId, lost ? CardStatus.Lost : CardStatus.Normal);
                _conn.Commit();
            }
            catch
            {
                _conn.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 注销/退卡：结清余额、留下退款流水，然后把这张卡从本地档案里删掉。
        /// 返回退还的金额（分）。
        ///
        /// 是「删除」而不是「标记为已注销」：注销完这张卡就是一张空白卡，
        /// 立刻可以重新发给别人，不必先解挂、也没有任何残留信息挡着发卡。
        /// 已经发生的流水不受影响，卡号与持卡人在记账时就已快照进流水行。
        /// </summary>
        public long CancelCard(long cardId, long? operatorId)
        {
            Card card = _cards.GetById(cardId);
            if (card == null)
            {
                throw new BusinessException("未找到该卡片记录。");
            }

            long refund = card.BalanceCents;

            _conn.BeginTransaction();
            try
            {
                // 退款流水先写：流水自带卡号与持卡人快照，
                // 卡片档案删掉之后这条历史记录依然完整可读。
                _transactions.Insert(cardId, card.Epc, card.CardNo, card.HolderName,
                                     TransactionType.Refund, refund, 0, operatorId,
                                     refund > 0 ? "注销退卡，退还余额" : "注销退卡，无余额可退");

                _cards.Delete(cardId);

                _conn.Commit();
            }
            catch
            {
                _conn.Rollback();
                throw;
            }

            return refund;
        }
    }
}
