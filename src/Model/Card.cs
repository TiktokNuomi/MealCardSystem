using System;
using MealCardSystem.Util;

namespace MealCardSystem.Model
{
    /// <summary>卡片状态。</summary>
    public enum CardStatus
    {
        /// <summary>正常，可消费可充值。</summary>
        Normal = 1,

        /// <summary>已挂失，禁止消费，可解挂。</summary>
        Lost = 2,

        /// <summary>已注销，禁止一切交易，该 EPC 可重新发卡。</summary>
        Cancelled = 3
    }

    /// <summary>一张饭卡。余额只在本地数据库中维护，不写入卡片芯片。</summary>
    public class Card
    {
        public long Id { get; set; }

        /// <summary>卡片 EPC（十六进制大写字符串），系统内的唯一标识。</summary>
        public string Epc { get; set; }

        /// <summary>人工卡号，便于口头报卡号查询。</summary>
        public string CardNo { get; set; }

        public string HolderName { get; set; }

        /// <summary>工号 / 学号。</summary>
        public string HolderId { get; set; }

        /// <summary>部门 / 班级。</summary>
        public string Department { get; set; }

        /// <summary>余额，单位为「分」。用整数避免浮点误差。</summary>
        public long BalanceCents { get; set; }

        public CardStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        /// <summary>余额的显示文本，如 "12.50"。</summary>
        public string BalanceText
        {
            get { return Money.Format(BalanceCents); }
        }

        /// <summary>状态的中文名称。</summary>
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case CardStatus.Normal: return "正常";
                    case CardStatus.Lost: return "已挂失";
                    case CardStatus.Cancelled: return "已注销";
                    default: return "未知";
                }
            }
        }

        /// <summary>该卡当前是否允许消费。</summary>
        public bool CanConsume
        {
            get { return Status == CardStatus.Normal; }
        }
    }
}
