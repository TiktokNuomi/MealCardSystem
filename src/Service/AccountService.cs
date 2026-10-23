using System;
using System.Collections.Generic;
using MealCardSystem.Data;
using MealCardSystem.Model;
using MealCardSystem.Util;

namespace MealCardSystem.Service
{
    /// <summary>消费与充值。</summary>
    public class AccountService
    {
        private readonly SqliteConnection _conn;
        private readonly CardRepository _cards;
        private readonly TransactionRepository _transactions;

        public AccountService(SqliteConnection conn, CardRepository cards, TransactionRepository transactions)
        {
            _conn = conn;
            _cards = cards;
            _transactions = transactions;
        }

        /// <summary>
        /// 消费扣款。
        ///
        /// 整个「读余额 → 校验 → 扣减 → 记流水」过程在 BEGIN IMMEDIATE 事务内完成，
        /// 事务一开始就取得写锁，因此不会出现两个操作同时读到同一余额而透支的情况。
        /// </summary>
        /// <returns>扣款后的余额（分）。</returns>
        public long Consume(string epc, long amountCents, long? operatorId)
        {
            ValidateAmount(amountCents, "消费金额");

            string normalized = Hex.NormalizeEpc(epc);
            long balanceAfter = 0;

            _conn.BeginTransaction();
            try
            {
                Card card = RequireConsumableCard(normalized);

                if (card.BalanceCents < amountCents)
                {
                    throw new BusinessException(
                        "余额不足。\n\n持卡人：" + card.HolderName +
                        "\n当前余额：" + card.BalanceText + " 元" +
                        "\n本次消费：" + Money.Format(amountCents) + " 元" +
                        "\n还差：" + Money.Format(amountCents - card.BalanceCents) + " 元");
                }

                balanceAfter = card.BalanceCents - amountCents;

                _cards.SetBalance(card.Id, balanceAfter);
                _transactions.Insert(card.Id, card.Epc, card.CardNo, card.HolderName,
                                     TransactionType.Consume,
                                     amountCents, balanceAfter, operatorId, "消费");

                _conn.Commit();
            }
            catch
            {
                _conn.Rollback();
                throw;
            }

            return balanceAfter;
        }

        /// <summary>充值加款。</summary>
        /// <returns>充值后的余额（分）。</returns>
        public long Recharge(string epc, long amountCents, long? operatorId)
        {
            ValidateAmount(amountCents, "充值金额");

            string normalized = Hex.NormalizeEpc(epc);
            long balanceAfter = 0;

            _conn.BeginTransaction();
            try
            {
                Card card = RequireExistingCard(normalized);

                if (card.Status == CardStatus.Cancelled)
                {
                    throw new BusinessException("该卡已注销，不能充值。请先重新发卡。");
                }
                if (card.Status == CardStatus.Lost)
                {
                    throw new BusinessException("该卡已挂失，不能充值。请先解挂。");
                }

                balanceAfter = card.BalanceCents + amountCents;

                if (balanceAfter > Money.MaxCents)
                {
                    throw new BusinessException(
                        "充值后余额将超过上限 " + Money.Format(Money.MaxCents) + " 元。\n" +
                        "当前余额：" + card.BalanceText + " 元，本次充值：" + Money.Format(amountCents) + " 元。");
                }

                _cards.SetBalance(card.Id, balanceAfter);
                _transactions.Insert(card.Id, card.Epc, card.CardNo, card.HolderName,
                                     TransactionType.Recharge,
                                     amountCents, balanceAfter, operatorId, "充值");

                _conn.Commit();
            }
            catch
            {
                _conn.Rollback();
                throw;
            }

            return balanceAfter;
        }

        /// <summary>
        /// 反查某张卡的余额是否与流水完全吻合，用于自检与对账。
        /// 返回 null 表示一致，否则返回差异说明。
        /// </summary>
        public string VerifyBalance(long cardId)
        {
            Card card = _cards.GetById(cardId);
            if (card == null)
            {
                return "卡片不存在。";
            }

            List<object[]> rows = _conn.Query(
                "SELECT " +
                "COALESCE(SUM(CASE WHEN type = 1 THEN amount_cents ELSE 0 END), 0), " +  // 开卡
                "COALESCE(SUM(CASE WHEN type = 2 THEN amount_cents ELSE 0 END), 0), " +  // 消费
                "COALESCE(SUM(CASE WHEN type = 3 THEN amount_cents ELSE 0 END), 0), " +  // 充值
                "COALESCE(SUM(CASE WHEN type = 4 THEN amount_cents ELSE 0 END), 0) " +   // 退款
                "FROM transactions WHERE card_id = ?1", cardId);

            object[] r = rows[0];
            long issue = Convert.ToInt64(r[0]);
            long consume = Convert.ToInt64(r[1]);
            long recharge = Convert.ToInt64(r[2]);
            long refund = Convert.ToInt64(r[3]);

            // 退卡退款是把余额退还给持卡人，卡内余额随之减少，故为减项
            long expected = issue + recharge - consume - refund;

            if (expected != card.BalanceCents)
            {
                return "卡号 " + card.CardNo + "（" + card.HolderName + "）余额与流水分不符：" +
                       "账面余额 " + Money.Format(card.BalanceCents) +
                       " 元，流水推算 " + Money.Format(expected) + " 元。";
            }
            return null;
        }

        // ---------- 内部校验 ----------

        private static void ValidateAmount(long amountCents, string label)
        {
            if (amountCents <= 0)
            {
                throw new BusinessException(label + "必须大于 0。");
            }
            if (amountCents > Money.MaxCents)
            {
                throw new BusinessException(label + "不能超过 " + Money.Format(Money.MaxCents) + " 元。");
            }
        }

        private Card RequireExistingCard(string normalizedEpc)
        {
            Card card = _cards.GetByEpc(normalizedEpc);
            if (card == null)
            {
                throw new BusinessException("这张卡还没有登记，请先到「发卡」办理。");
            }
            return card;
        }

        private Card RequireConsumableCard(string normalizedEpc)
        {
            Card card = RequireExistingCard(normalizedEpc);

            if (card.Status == CardStatus.Lost)
            {
                throw new BusinessException("该卡已挂失，不能消费。\n\n持卡人：" + card.HolderName);
            }
            if (card.Status == CardStatus.Cancelled)
            {
                throw new BusinessException("该卡已注销，不能消费。");
            }
            return card;
        }
    }
}
