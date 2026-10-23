using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MealCardSystem.Data;
using MealCardSystem.Model;
using MealCardSystem.Service;
using MealCardSystem.Util;

namespace MealCardSystem.Tests
{
    /// <summary>
    /// 不依赖读卡器的自检程序：在临时数据库上跑通发卡、充值、消费、挂失、注销、
    /// 流水导出等全部业务，用来验证数据库与业务层是否正确。
    ///
    /// 运行方式：build.bat selftest
    /// </summary>
    internal static class SelfTest
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                // 某些宿主环境不支持切换编码，不影响测试
            }

            string dbPath = Path.Combine(Path.GetTempPath(), "mealcard_selftest_" + Guid.NewGuid().ToString("N") + ".db");
            Console.WriteLine("食堂饭卡管理系统 · 业务自检");
            Console.WriteLine("临时数据库：" + dbPath);
            Console.WriteLine(new string('-', 62));

            try
            {
                using (AppSession app = AppSession.Start(dbPath))
                {
                    RunAll(app);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("!! 自检过程中出现未预期异常：" + ex);
                _failed++;
            }
            finally
            {
                TryDelete(dbPath);
            }

            RunLegacyUpgradeTest();

            Console.WriteLine(new string('-', 62));
            Console.WriteLine("通过 " + _passed + " 项，失败 " + _failed + " 项。");
            Console.WriteLine(_failed == 0 ? "全部通过。" : "存在失败项，请检查上面的输出。");
            return _failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// 旧版本数据库的升级测试。
        ///
        /// 老库的流水表没有 card_no / holder_name 两列，注销也只是把卡标记成「已注销」
        /// 而留在库里。用户手上已经跑起来的库就是这个样子，所以专门按旧结构造一个库，
        /// 验证新版本能把它平滑升上来：老流水的卡号与持卡人被回填成快照，
        /// 遗留的「已注销」卡片在重新发放时被清掉。
        /// </summary>
        private static void RunLegacyUpgradeTest()
        {
            Section("旧版本数据库升级");

            const string EPC_OLD = "E2801160600002054E3A1B2C";
            string dbPath = Path.Combine(Path.GetTempPath(),
                                         "mealcard_legacy_" + Guid.NewGuid().ToString("N") + ".db");

            try
            {
                // 按老版本的结构与数据造库：流水表没有快照列，卡片是软删除状态
                using (SqliteConnection raw = SqliteConnection.Open(dbPath))
                {
                    raw.ExecuteScript(@"
CREATE TABLE cards (
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
CREATE TABLE transactions (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    card_id             INTEGER NOT NULL,
    epc                 TEXT    NOT NULL,
    type                INTEGER NOT NULL,
    amount_cents        INTEGER NOT NULL,
    balance_after_cents INTEGER NOT NULL,
    operator_id         INTEGER,
    remark              TEXT,
    created_at          TEXT    NOT NULL
);
INSERT INTO cards (epc, card_no, holder_name, holder_id, department,
                   balance_cents, status, created_at, updated_at)
VALUES ('E2801160600002054E3A1B2C', '10001', '老张三', 'G001', '研发部', 0, 3,
        '2026-01-01 09:00:00', '2026-01-01 09:00:00');
INSERT INTO transactions (card_id, epc, type, amount_cents, balance_after_cents, remark, created_at)
VALUES (1, 'E2801160600002054E3A1B2C', 1, 5000, 5000, '开卡', '2026-01-01 09:00:00');
INSERT INTO transactions (card_id, epc, type, amount_cents, balance_after_cents, remark, created_at)
VALUES (1, 'E2801160600002054E3A1B2C', 2, 5000, 0, '消费', '2026-01-01 12:00:00');
");
                }

                using (AppSession app = AppSession.Start(dbPath))
                {
                    app.CurrentOperator = app.Operators.Authenticate("admin", "admin123");

                    List<Transaction> tx = app.ReportService.Query(null, null, null, null, 0);
                    CheckEqual("旧库流水条数不变", tx.Count, 2);

                    bool backfilled = tx.Count > 0;
                    foreach (Transaction t in tx)
                    {
                        if (t.CardNo != "10001" || t.HolderName != "老张三")
                        {
                            backfilled = false;
                        }
                    }
                    Check("旧库流水回填了卡号与持卡人快照", backfilled,
                          tx.Count == 0 ? "一条流水都没有"
                                        : ("第一条是 " + tx[0].CardNo + " / " + tx[0].HolderName));

                    // 旧库里遗留的「已注销」卡片必须在迁移时就被清掉。
                    // 留着的话「发卡」页面读到的仍是一张登记过的卡，
                    // 「确认发卡」按钮会一直是灰的，操作员没法把这张卡重新发出去。
                    Check("旧库遗留的已注销卡片在迁移时被清掉",
                          app.CardService.FindByEpc(EPC_OLD) == null,
                          "卡片仍留在库里，发卡页面会一直认为它已登记");
                    CheckEqual("迁移后已注销卡片不再占用卡片列表",
                               app.Cards.Search(null, null).Count, 0);
                    CheckEqual("迁移后按已注销状态筛选命中 0 张",
                               app.Cards.Search(null, CardStatus.Cancelled).Count, 0);

                    Card reissued = app.CardService.IssueCard(
                        EPC_OLD, "新李四", "G002", "财务部", 0, app.CurrentOperatorId);
                    Check("旧库卡片迁移后可以立刻重新发放", reissued != null);
                    CheckEqual("重新发放后持卡人已换人", reissued.HolderName, "新李四");
                    CheckEqual("重新发放后状态恢复正常", reissued.StatusText, "正常");
                    Check("重新发放没有复用已注销的旧卡号",
                          reissued.CardNo != "10001", "又发成了 10001");

                    List<Transaction> stillOld = app.ReportService.Query(null, null, null, "老张三", 0);
                    CheckEqual("旧库老流水仍能按原持卡人查到（没被新人覆盖）",
                               stillOld.Count, 2);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  FAIL  旧库升级测试出现异常：" + ex.Message);
                _failed++;
            }
            finally
            {
                TryDelete(dbPath);
            }
        }

        private static void RunAll(AppSession app)
        {
            // ---------- 操作员与登录 ----------
            Section("操作员与登录");

            Check("首次启动创建默认管理员",
                  app.CreatedDefaultAdmin,
                  "CreatedDefaultAdmin 应为 true");

            CheckEqual("admin 用正确密码可登录",
                       app.Operators.Authenticate("admin", "admin123") != null, true);

            CheckEqual("admin 用错误密码被拒绝",
                       app.Operators.Authenticate("admin", "wrong-password"), null);

            CheckEqual("不存在的账号被拒绝",
                       app.Operators.Authenticate("nobody", "admin123"), null);

            Check("重复创建同名账号会被拒绝",
                  Throws<InvalidOperationException>(delegate { app.Operators.Create("admin", "x1234", "重复", false); }));

            long cashierId = app.Operators.Create("cashier", "test1234", "收银员小王", false);
            CheckEqual("新建收银员后总数变为 2", app.Operators.Count(), 2);
            CheckEqual("收银员可用新密码登录",
                       app.Operators.Authenticate("cashier", "test1234") != null, true);

            app.Operators.ChangePassword(cashierId, "newpass99");
            CheckEqual("改密后旧密码失效",
                       app.Operators.Authenticate("cashier", "test1234"), null);
            CheckEqual("改密后新密码生效",
                       app.Operators.Authenticate("cashier", "newpass99") != null, true);

            app.CurrentOperator = app.Operators.Authenticate("admin", "admin123");
            long? opId = app.CurrentOperatorId;

            // ---------- 发卡 ----------
            Section("发卡");

            const string EPC_A = "E2801160600002054E3A1B2C";
            const string EPC_B = "E2801160600002054E3A1B2D";

            Card cardA = app.CardService.IssueCard(EPC_A, "张三", "G001", "研发部", 5000, opId);
            CheckEqual("发卡后卡号自动分配为 10001", cardA.CardNo, "10001");
            CheckEqual("发卡后余额为 50.00 元", cardA.BalanceText, "50.00");
            CheckEqual("发卡后状态为正常", cardA.StatusText, "正常");

            Card byEpc = app.CardService.FindByEpc(EPC_A);
            Check("按 EPC 能查回刚发的卡", byEpc != null && byEpc.Id == cardA.Id);

            Card byLowercase = app.CardService.FindByEpc(EPC_A.ToLowerInvariant());
            Check("EPC 大小写不敏感也能查到", byLowercase != null && byLowercase.Id == cardA.Id);

            Card bySpaced = app.CardService.FindByEpc("E2 80 11 60 60 00 02 05 4E 3A 1B 2C");
            Check("EPC 含空格也能查到", bySpaced != null && bySpaced.Id == cardA.Id);

            Check("同一张卡重复发卡被拒绝", Throws<BusinessException>(delegate
            {
                app.CardService.IssueCard(EPC_A, "李四", "G002", "财务部", 0, opId);
            }));

            Check("姓名为空时发卡被拒绝", Throws<BusinessException>(delegate
            {
                app.CardService.IssueCard(EPC_B, "  ", "G003", "财务部", 0, opId);
            }));

            Card cardB = app.CardService.IssueCard(EPC_B, "李四", "G002", "财务部", 0, opId);
            CheckEqual("第二张卡的卡号递增为 10002", cardB.CardNo, "10002");

            // ---------- 充值 ----------
            Section("充值");

            long bal = app.AccountService.Recharge(EPC_A, 10000, opId);
            CheckEqual("充值 100 元后余额为 150.00 元", Money.Format(bal), "150.00");

            bal = app.AccountService.Recharge(EPC_A, 1, opId);
            CheckEqual("再充值 0.01 元后余额为 150.01 元", Money.Format(bal), "150.01");

            Check("充值 0 元被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Recharge(EPC_A, 0, opId);
            }));

            Check("充值负数被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Recharge(EPC_A, -100, opId);
            }));

            Check("给未登记的卡充值被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Recharge("AABBCCDD", 100, opId);
            }));

            // ---------- 消费 ----------
            Section("消费");

            bal = app.AccountService.Consume(EPC_A, 1250, opId);
            CheckEqual("消费 12.50 元后余额为 137.51 元", Money.Format(bal), "137.51");

            CheckEqual("零余额卡消费被拒绝并给出余额不足提示",
                       Throws<BusinessException>(delegate
                       {
                           app.AccountService.Consume(EPC_B, 100, opId);
                       }), true);

            Check("未登记的卡消费被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Consume("DEADBEEF", 100, opId);
            }));

            // 边界：余额刚好等于消费额，应当成功且余额归零
            Card cardC = app.CardService.IssueCard("E2801160600002054E3A1B2E", "王五", "G003", "食堂", 500, opId);
            bal = app.AccountService.Consume(cardC.Epc, 500, opId);
            CheckEqual("余额刚好够时消费成功且余额归零", Money.Format(bal), "0.00");

            // 边界：差一分钱，应当被拒绝
            Check("余额差一分钱时消费被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Consume(cardC.Epc, 1, opId);
            }));

            // ---------- 余额与流水一致性 ----------
            Section("余额与流水一致性");

            Check("张三的余额与流水累加一致", app.AccountService.VerifyBalance(cardA.Id) == null,
                  app.AccountService.VerifyBalance(cardA.Id));
            Check("李四的余额与流水累加一致", app.AccountService.VerifyBalance(cardB.Id) == null,
                  app.AccountService.VerifyBalance(cardB.Id));
            Check("王五的余额与流水累加一致", app.AccountService.VerifyBalance(cardC.Id) == null,
                  app.AccountService.VerifyBalance(cardC.Id));

            List<Transaction> txA = app.Transactions.QueryByCard(cardA.Id, 0);
            CheckEqual("张三共有 4 笔流水（1 开卡 + 2 充值 + 1 消费）", txA.Count, 4);

            // ---------- 失败操作不留痕迹 ----------
            Section("失败操作不应留下任何痕迹");

            long balBefore = app.Cards.GetById(cardB.Id).BalanceCents;
            int txBefore = app.Transactions.QueryByCard(cardB.Id, 0).Count;

            try
            {
                app.AccountService.Consume(EPC_B, 999999, opId);
            }
            catch (BusinessException)
            {
                // 预期
            }

            CheckEqual("失败的消费没有改变余额",
                       app.Cards.GetById(cardB.Id).BalanceCents, balBefore);
            CheckEqual("失败的消费没有写入流水",
                       app.Transactions.QueryByCard(cardB.Id, 0).Count, txBefore);

            // ---------- 挂失与解挂 ----------
            Section("挂失与解挂");

            app.CardService.SetLost(cardA.Id, true, opId);
            CheckEqual("挂失后状态变为已挂失", app.Cards.GetById(cardA.Id).StatusText, "已挂失");

            Check("挂失卡消费被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Consume(EPC_A, 100, opId);
            }));

            Check("挂失卡充值被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Recharge(EPC_A, 100, opId);
            }));

            Check("重复挂失被拒绝", Throws<BusinessException>(delegate
            {
                app.CardService.SetLost(cardA.Id, true, opId);
            }));

            Check("挂失卡余额未受影响", app.Cards.GetById(cardA.Id).BalanceCents > 0);

            app.CardService.SetLost(cardA.Id, false, opId);
            CheckEqual("解挂后状态恢复正常", app.Cards.GetById(cardA.Id).StatusText, "正常");
            Check("解挂后可以正常消费",
                  app.AccountService.Consume(EPC_A, 100, opId) > 0);

            Check("未挂失的卡解挂被拒绝", Throws<BusinessException>(delegate
            {
                app.CardService.SetLost(cardA.Id, false, opId);
            }));

            // ---------- 注销与退卡 ----------
            Section("注销与退卡");

            int cardsBeforeCancel = app.Cards.GetAll().Count;
            long balanceBeforeCancel = app.Cards.GetById(cardA.Id).BalanceCents;
            long refunded = app.CardService.CancelCard(cardA.Id, opId);

            CheckEqual("退还金额等于注销前余额", refunded, balanceBeforeCancel);

            // 注销 = 把卡片信息从本地删掉，回到空白卡状态
            Check("注销后卡片档案已从本地删除",
                  app.Cards.GetById(cardA.Id) == null, "卡片记录仍然留在库里");
            Check("注销后按 EPC 查不到这张卡（变回空白卡）",
                  app.CardService.FindByEpc(EPC_A) == null);
            CheckEqual("注销后卡片总数减少 1 张",
                       app.Cards.GetAll().Count, cardsBeforeCancel - 1);

            Check("注销卡消费被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Consume(EPC_A, 100, opId);
            }));

            Check("注销卡充值被拒绝", Throws<BusinessException>(delegate
            {
                app.AccountService.Recharge(EPC_A, 100, opId);
            }));

            Check("重复注销被拒绝", Throws<BusinessException>(delegate
            {
                app.CardService.CancelCard(cardA.Id, opId);
            }));

            // 回归用例：退款是把卡里的钱退还给人，余额应当减少，不能被当成收入
            List<Transaction> refundTx = app.Transactions.QueryByCard(cardA.Id, 0);
            Transaction lastRefund = null;
            foreach (Transaction t in refundTx)
            {
                if (t.Type == TransactionType.Refund)
                {
                    lastRefund = t;
                    break;
                }
            }
            Check("注销产生了退款流水", lastRefund != null);
            if (lastRefund != null)
            {
                Check("退款流水被记为支出", !lastRefund.IsIncome);
                CheckEqual("退款流水的收支显示为支出", lastRefund.SignedAmountText.StartsWith("-"), true);
                CheckEqual("退款流水金额等于注销前余额", lastRefund.AmountCents, balanceBeforeCancel);
            }

            // 卡片档案删了，历史流水必须还在，而且仍然看得出是谁的卡 ——
            // 卡号与持卡人是在记账时快照进流水行的，不依赖卡片档案。
            // 张三共 6 笔：开卡 1 + 充值 2 + 消费 2 + 注销退款 1
            List<Transaction> txByCard = app.Transactions.QueryByCard(cardA.Id, 0);
            List<Transaction> txAfterDelete = app.ReportService.Query(null, null, null, "张三", 0);
            CheckEqual("注销后仍能按原持卡人姓名查到全部流水", txAfterDelete.Count, 6);
            CheckEqual("按已删除持卡人姓名搜索与按卡片查询结果一致",
                       txAfterDelete.Count, txByCard.Count);
            if (txAfterDelete.Count > 0)
            {
                CheckEqual("注销后历史流水仍带原卡号快照",
                           txAfterDelete[0].CardNo, cardA.CardNo);
                CheckEqual("注销后历史流水仍带原持卡人快照",
                           txAfterDelete[0].HolderName, "张三");
            }

            // ---------- 注销后重新发卡 ----------
            Section("注销后重新发卡");

            Check("注销后这张卡可以立刻重新发放", app.CardService.FindByEpc(EPC_A) == null);

            Card reissued = app.CardService.IssueCard(EPC_A, "赵六", "G100", "后勤部", 2000, opId);
            Check("重新发卡成功", reissued != null);
            Check("重新发卡分配了全新卡号，不复用已注销的 10001",
                  reissued.CardNo != cardA.CardNo,
                  "重新发卡的卡号是 " + reissued.CardNo + "，与已注销的 " + cardA.CardNo + " 相同");
            CheckEqual("重新发卡分配的是 10004（卡号没有回退）", reissued.CardNo, "10004");
            CheckEqual("重新发卡后持卡人已更新", reissued.HolderName, "赵六");
            CheckEqual("重新发卡后余额为初始金额", reissued.BalanceText, "20.00");
            CheckEqual("重新发卡后状态为正常", reissued.StatusText, "正常");
            Check("重新发卡后可以消费", app.AccountService.Consume(reissued.Epc, 100, opId) > 0);
            Check("跨注销再发卡后余额与流水仍然一致",
                  app.AccountService.VerifyBalance(reissued.Id) == null,
                  app.AccountService.VerifyBalance(reissued.Id));

            // 换人重发之后，老流水不能被新持卡人的名字覆盖
            List<Transaction> oldTx = app.Transactions.QueryByCard(cardA.Id, 0);
            bool anyOldHolder = false;
            foreach (Transaction t in oldTx)
            {
                if (t.HolderName == "张三")
                {
                    anyOldHolder = true;
                    break;
                }
            }
            Check("换人重发后老流水仍显示原持卡人张三", anyOldHolder);
            foreach (Transaction t in oldTx)
            {
                if (t.HolderName == "赵六")
                {
                    anyOldHolder = false;
                    break;
                }
            }
            Check("换人重发后老流水没有被新持卡人覆盖", anyOldHolder);

            // 零余额的卡注销时退款为 0，同样应当删除档案
            long refundedC = app.CardService.CancelCard(cardC.Id, opId);
            CheckEqual("零余额卡注销退还 0 元", refundedC, 0L);
            Check("零余额卡注销后档案同样被删除", app.Cards.GetById(cardC.Id) == null);

            // ---------- 查询与统计 ----------
            Section("查询与统计");

            List<Card> found = app.Cards.Search("李四", null);
            Check("按姓名查询能命中", found.Count == 1);

            found = app.Cards.Search("10002", null);
            Check("按卡号查询能命中", found.Count == 1 && found[0].CardNo == "10002");

            // 注销掉的人不该再出现在卡片档案里，但流水里必须留着
            CheckEqual("已注销的持卡人不再出现在卡片列表里",
                       app.Cards.Search("张三", null).Count, 0);
            CheckEqual("已注销的卡号不再占用卡片列表",
                       app.Cards.Search("10001", null).Count, 0);
            CheckEqual("按已注销状态筛选命中 0 张（新数据不再产生该状态）",
                       app.Cards.Search(null, CardStatus.Cancelled).Count, 0);
            CheckEqual("卡片列表只剩在场的 2 张卡", app.Cards.Search(null, null).Count, 2);

            int total, normal, lost, cancelled;
            long totalBalance;
            app.Cards.GetStatistics(out total, out normal, out lost, out cancelled, out totalBalance);
            CheckEqual("卡片总数统计正确", total, 2);
            CheckEqual("正常卡统计正确", normal, 2);
            CheckEqual("挂失卡统计正确", lost, 0);
            CheckEqual("已注销卡统计为 0（注销即删除）", cancelled, 0);
            Check("余额合计为正数", totalBalance > 0);

            List<Transaction> allTx = app.ReportService.Query(null, null, null, null, 0);
            Check("流水查询返回全部记录", allTx.Count >= 8);

            List<Transaction> onlyConsume = app.ReportService.Query(null, null, TransactionType.Consume, null, 0);
            Check("按类型筛选只返回消费流水",
                  onlyConsume.Count > 0 && AllOfType(onlyConsume, TransactionType.Consume));

            List<Transaction> keywordTx = app.ReportService.Query(null, null, null, "李四", 0);
            Check("按持卡人姓名查流水能命中", keywordTx.Count >= 1);

            List<Transaction> todayTx = app.ReportService.Query(DateTime.Today, DateTime.Today, null, null, 0);
            Check("按当天日期范围能查到今天的流水", todayTx.Count > 0);

            List<Transaction> pastTx = app.ReportService.Query(
                new DateTime(2000, 1, 1), new DateTime(2000, 1, 2), null, null, 0);
            Check("历史日期范围查不到记录", pastTx.Count == 0);

            // ---------- CSV 导出 ----------
            Section("CSV 导出");

            string csvPath = Path.Combine(Path.GetTempPath(), "mealcard_selftest_export.csv");
            TryDelete(csvPath);
            app.ReportService.ExportTransactionsCsvWithSummary(csvPath, allTx, null, null);

            Check("CSV 文件已生成", File.Exists(csvPath));
            if (File.Exists(csvPath))
            {
                byte[] raw = File.ReadAllBytes(csvPath);
                Check("CSV 带 UTF-8 BOM（Excel 中文不乱码）",
                      raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF);

                string text = File.ReadAllText(csvPath, Encoding.UTF8);
                Check("CSV 含中文表头", text.Contains("流水号") && text.Contains("持卡人"));
                Check("CSV 含中文内容", text.Contains("张三") || text.Contains("李四"));
                Check("CSV 含汇总行", text.Contains("消费金额合计"));
                Check("含逗号的字段被正确加引号",
                      Csv.Escape("研发部,财务部") == "\"研发部,财务部\"");
                Check("含引号的字段被正确转义",
                      Csv.Escape("他说\"你好\"") == "\"他说\"\"你好\"\"\"");
            }
            TryDelete(csvPath);

            // ---------- 金额换算 ----------
            Section("金额换算与边界");

            long cents;
            string err;
            Check("解析 12.50 得到 1250 分",
                  Money.TryParseCents("12.50", out cents, out err) && cents == 1250);
            Check("解析 12 得到 1200 分",
                  Money.TryParseCents("12", out cents, out err) && cents == 1200);
            Check("解析 0.01 得到 1 分",
                  Money.TryParseCents("0.01", out cents, out err) && cents == 1);
            Check("解析带货币符号的 ￥8.80 得到 880 分",
                  Money.TryParseCents("￥8.80", out cents, out err) && cents == 880);
            Check("解析带千分位的 1,234.56 得到 123456 分",
                  Money.TryParseCents("1,234.56", out cents, out err) && cents == 123456);
            Check("负数金额被拒绝", !Money.TryParseCents("-5", out cents, out err));
            Check("非数字金额被拒绝", !Money.TryParseCents("abc", out cents, out err));
            Check("空金额被拒绝", !Money.TryParseCents("", out cents, out err));

            // 浮点累加误差防护：0.1 + 0.2 必须精确等于 0.30
            long a, b;
            Money.TryParseCents("0.10", out a, out err);
            Money.TryParseCents("0.20", out b, out err);
            CheckEqual("0.10 + 0.20 精确等于 0.30（无浮点误差）", Money.Format(a + b), "0.30");

            // ---------- EPC 规整 ----------
            Section("EPC 处理");

            CheckEqual("EPC 小写转大写", Hex.NormalizeEpc("e2801160"), "E2801160");
            CheckEqual("EPC 去空格", Hex.NormalizeEpc("E2 80 11 60"), "E2801160");
            CheckEqual("EPC 去冒号", Hex.NormalizeEpc("E2:80:11:60"), "E2801160");
            CheckEqual("十六进制转文本", Hex.ToHexString(new byte[] { 0xE2, 0x80, 0x11, 0x60 }), "E2801160");
            Check("文本转十六进制", Hex.FromHexString("E2801160").Length == 4);
            Check("十六进制含非法字符时报错",
                  Throws<FormatException>(delegate { Hex.FromHexString("E280ZZ11"); }));
            Check("十六进制长度为奇数时报错",
                  Throws<FormatException>(delegate { Hex.FromHexString("E28"); }));
        }

        private static bool AllOfType(List<Transaction> list, TransactionType type)
        {
            foreach (Transaction t in list)
            {
                if (t.Type != type)
                {
                    return false;
                }
            }
            return true;
        }

        // ---------- 断言helpers ----------

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("【" + title + "】");
        }

        private static void Check(string name, bool ok)
        {
            Check(name, ok, null);
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (ok)
            {
                _passed++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : "  <- " + detail));
            }
        }

        private static void CheckEqual(string name, object actual, object expected)
        {
            bool ok = Equals(actual, expected);
            Check(name, ok, ok ? null : "期望 " + Describe(expected) + "，实际 " + Describe(actual));
        }

        private static string Describe(object o)
        {
            return o == null ? "(null)" : "[" + o + "]";
        }

        /// <summary>断言某段代码会抛出指定类型的异常；返回是否确实抛出。</summary>
        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
