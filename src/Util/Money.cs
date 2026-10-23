using System;
using System.Globalization;

namespace MealCardSystem.Util
{
    /// <summary>
    /// 金额与「分」之间的换算。
    ///
    /// 全系统金额一律以 long 存「分」并参与运算，只有在显示和用户输入时才做转换，
    /// 从根本上避免 double 累加出现 0.1 + 0.2 = 0.30000000000000004 这类误差。
    /// </summary>
    public static class Money
    {
        /// <summary>单笔金额上限：100 万元。防止误输入天文数字。</summary>
        public const long MaxCents = 1000000L * 100L;

        /// <summary>把「分」格式化为两位小数的显示文本。</summary>
        public static string Format(long cents)
        {
            // 用 decimal 保证负数与进位处理正确
            decimal yuan = (decimal)cents / 100m;
            return yuan.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>把「分」格式化为带货币符号的文本，如 "￥12.50"。</summary>
        public static string FormatWithSymbol(long cents)
        {
            return "￥" + Format(cents);
        }

        /// <summary>
        /// 解析用户输入的金额文本为「分」。
        /// 接受 "12"、"12.5"、"12.50" 等形式，不接受负数。
        /// </summary>
        public static bool TryParseCents(string text, out long cents, out string error)
        {
            cents = 0;
            error = null;

            if (text == null || text.Trim().Length == 0)
            {
                error = "请输入金额。";
                return false;
            }

            string s = text.Trim();
            // 容忍用户输入的货币符号与千分位
            s = s.Replace("￥", "").Replace("¥", "").Replace(",", "").Trim();

            decimal yuan;
            if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out yuan))
            {
                error = "金额格式不正确，请输入数字，例如 12.50。";
                return false;
            }

            if (yuan < 0m)
            {
                error = "金额不能为负数。";
                return false;
            }

            // 四舍五入到分，避免 12.505 这类输入
            decimal centValue = Math.Round(yuan * 100m, 0, MidpointRounding.AwayFromZero);
            if (centValue > (decimal)MaxCents)
            {
                error = "单笔金额不能超过 " + Format(MaxCents) + " 元。";
                return false;
            }

            cents = (long)centValue;
            return true;
        }

        /// <summary>解析金额，失败时抛出带中文说明的异常。</summary>
        public static long ParseCents(string text)
        {
            long cents;
            string error;
            if (!TryParseCents(text, out cents, out error))
            {
                throw new FormatException(error);
            }
            return cents;
        }
    }
}
