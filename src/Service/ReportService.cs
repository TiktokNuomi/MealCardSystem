using System;
using System.Collections.Generic;
using MealCardSystem.Model;
using MealCardSystem.Util;

namespace MealCardSystem.Service
{
    /// <summary>交易流水的查询与导出。</summary>
    public class ReportService
    {
        private readonly Data.TransactionRepository _transactions;

        public ReportService(Data.TransactionRepository transactions)
        {
            _transactions = transactions;
        }

        /// <summary>按条件查询流水。</summary>
        public List<Transaction> Query(DateTime? from, DateTime? to, TransactionType? type, string keyword, int limit)
        {
            return _transactions.Query(from, to, type, keyword, limit);
        }

        /// <summary>
        /// 把流水导出为 CSV 文件（UTF-8 带 BOM，Excel 直接双击即可正常显示中文）。
        /// </summary>
        public void ExportTransactionsCsv(string path, List<Transaction> rows)
        {
            string[] headers = new string[]
            {
                "流水号", "交易时间", "类型", "卡号", "持卡人", "EPC",
                "金额(元)", "收支", "交易后余额(元)", "操作员", "备注"
            };

            List<string[]> lines = new List<string[]>();
            foreach (Transaction t in rows)
            {
                lines.Add(new string[]
                {
                    t.Id.ToString(),
                    t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    t.TypeText,
                    t.CardNo,
                    t.HolderName,
                    t.Epc,
                    t.AmountText,
                    t.IsIncome ? "收入" : "支出",
                    t.BalanceAfterText,
                    t.OperatorName,
                    t.Remark
                });
            }

            Csv.WriteFile(path, headers, lines);
        }

        /// <summary>导出同时附一张汇总行，便于对账。</summary>
        public void ExportTransactionsCsvWithSummary(string path, List<Transaction> rows,
                                                     DateTime? from, DateTime? to)
        {
            string[] headers = new string[]
            {
                "流水号", "交易时间", "类型", "卡号", "持卡人", "EPC",
                "金额(元)", "收支", "交易后余额(元)", "操作员", "备注"
            };

            List<string[]> lines = new List<string[]>();

            long issue = 0, consume = 0, recharge = 0, refund = 0;
            foreach (Transaction t in rows)
            {
                switch (t.Type)
                {
                    case TransactionType.Issue: issue += t.AmountCents; break;
                    case TransactionType.Consume: consume += t.AmountCents; break;
                    case TransactionType.Recharge: recharge += t.AmountCents; break;
                    case TransactionType.Refund: refund += t.AmountCents; break;
                }

                lines.Add(new string[]
                {
                    t.Id.ToString(),
                    t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    t.TypeText,
                    t.CardNo,
                    t.HolderName,
                    t.Epc,
                    t.AmountText,
                    t.IsIncome ? "收入" : "支出",
                    t.BalanceAfterText,
                    t.OperatorName,
                    t.Remark
                });
            }

            // 空行 + 汇总
            lines.Add(new string[] { });
            lines.Add(new string[] { "统计范围", DateRangeText(from, to), "共 " + rows.Count + " 笔" });
            lines.Add(new string[] { "开卡金额合计", Money.Format(issue) });
            lines.Add(new string[] { "充值金额合计", Money.Format(recharge) });
            lines.Add(new string[] { "退卡退款合计", Money.Format(refund) });
            lines.Add(new string[] { "消费金额合计", Money.Format(consume) });

            Csv.WriteFile(path, headers, lines);
        }

        private static string DateRangeText(DateTime? from, DateTime? to)
        {
            string a = from.HasValue ? from.Value.ToString("yyyy-MM-dd") : "不限";
            string b = to.HasValue ? to.Value.ToString("yyyy-MM-dd") : "不限";
            return a + " 至 " + b;
        }
    }
}
