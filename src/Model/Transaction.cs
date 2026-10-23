using System;
using MealCardSystem.Util;

namespace MealCardSystem.Model
{
    /// <summary>交易类型。金额一律以正数存储，方向由类型决定。</summary>
    public enum TransactionType
    {
        /// <summary>开卡（含初始存入金额）。</summary>
        Issue = 1,

        /// <summary>消费（扣款）。</summary>
        Consume = 2,

        /// <summary>充值（加款）。</summary>
        Recharge = 3,

        /// <summary>退卡退款（注销时结清余额）。</summary>
        Refund = 4
    }

    /// <summary>一条交易流水。所有余额变动都必须留下流水，便于对账。</summary>
    public class Transaction
    {
        public long Id { get; set; }

        public long CardId { get; set; }

        public string Epc { get; set; }

        public TransactionType Type { get; set; }

        /// <summary>交易金额（分），恒为正数。</summary>
        public long AmountCents { get; set; }

        /// <summary>交易完成后的余额（分）。</summary>
        public long BalanceAfterCents { get; set; }

        public long? OperatorId { get; set; }

        /// <summary>操作员姓名，列表展示用（来自联表查询，不入库）。</summary>
        public string OperatorName { get; set; }

        /// <summary>交易时点的持卡人姓名快照，随流水一起入库。</summary>
        public string HolderName { get; set; }

        /// <summary>交易时点的卡号快照，随流水一起入库。</summary>
        public string CardNo { get; set; }

        public string Remark { get; set; }

        public DateTime CreatedAt { get; set; }

        public string AmountText
        {
            get { return Money.Format(AmountCents); }
        }

        public string BalanceAfterText
        {
            get { return Money.Format(BalanceAfterCents); }
        }

        /// <summary>带符号的金额，用于流水列表直观区分收支。</summary>
        public string SignedAmountText
        {
            get
            {
                string sign = IsIncome ? "+" : "-";
                return sign + Money.Format(AmountCents);
            }
        }

        /// <summary>
        /// 该笔交易是否使卡内余额增加。
        ///
        /// 注意「退卡退款」虽然名字里有"退"，但它是把卡里剩余的钱退还给持卡人，
        /// <b>卡内余额是减少的</b>，因此算支出而不是收入。
        /// </summary>
        public bool IsIncome
        {
            get
            {
                return Type == TransactionType.Issue
                    || Type == TransactionType.Recharge;
            }
        }

        public string TypeText
        {
            get
            {
                switch (Type)
                {
                    case TransactionType.Issue: return "开卡";
                    case TransactionType.Consume: return "消费";
                    case TransactionType.Recharge: return "充值";
                    case TransactionType.Refund: return "退卡退款";
                    default: return "未知";
                }
            }
        }
    }
}
