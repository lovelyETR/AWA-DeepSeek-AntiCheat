namespace CheatSimulator
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using DeepSeekAntiCheat;

    /// <summary>
    /// DeepSeekAntiCheat 的作弊行为模拟器。
    ///
    /// 它只伪造行为统计数据，不碰任何游戏状态 —— 没有自瞄、没有内存写入、
    /// 没有网络包伪造，因此原理上无法被用来在服务器上作弊。
    /// 用途是把各种作弊的数值特征喂给真实检测链路，验证检测是否有效。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.WriteLine("!! 模拟器崩溃: " + e.GetType().FullName);
                Console.WriteLine("   " + e.Message);
                if (e.InnerException != null)
                {
                    Console.WriteLine("   内层: " + e.InnerException.GetType().FullName + " - " + e.InnerException.Message);
                }

                Console.WriteLine();
                Console.WriteLine(e.StackTrace);
                return 3;
            }
        }

        private static int Run(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string only = GetArg(args, "--scenario");
            bool withAi = HasFlag(args, "--ai");
            string key = GetArg(args, "--key") ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            string baseUrl = GetArg(args, "--url") ?? "https://api.deepseek.com";
            string model = GetArg(args, "--model") ?? "deepseek-chat";

            // --panel : 渲染管理员 HUD 面板（用真实渲染代码 + 样本数据）
            if (HasFlag(args, "--panel"))
            {
                var cfg0 = new Config();
                var sample = Scenarios.RunLocal(cfg0).Select(r => r.Stats).ToList();
                Console.WriteLine("管理员面板渲染预览（黑白蓝配色，实际显示在右上角 HUD）");
                Console.WriteLine();
                Console.WriteLine(AdminTools.RenderPanelPlain(sample, cfg0, sample.Count, 3));
                Console.WriteLine();
                Console.WriteLine("原始富文本标记（游戏里按这个渲染颜色）：");
                Console.WriteLine();
                Console.WriteLine(AdminTools.RenderPanel(sample, cfg0, sample.Count, 3));
                Console.WriteLine();
                return 0;
            }

            PrintBanner(withAi, key);

            var cfg = new Config
            {
                EnableAiReview = withAi,
                ApiKey = key ?? string.Empty,
                ApiBaseUrl = baseUrl,
                Model = model,
                UseJsonResponseFormat = !HasFlag(args, "--no-json-format"),
            };

            IList<Scenarios.Scenario> all = Scenarios.All();
            if (!string.IsNullOrEmpty(only))
            {
                all = all.Where(s => s.Id.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (all.Count == 0)
                {
                    Console.WriteLine("没有匹配的场景: " + only);
                    Console.WriteLine("可用场景: " + string.Join(", ", Scenarios.All().Select(s => s.Id)));
                    return 2;
                }
            }

            IList<Scenarios.ScenarioResult> results = Scenarios.RunLocal(cfg, all);

            // 需要 AI 的话逐个跑（串行，避免把免费额度打爆）
            if (withAi)
            {
                if (string.IsNullOrWhiteSpace(key) && baseUrl.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("!! 指定了 --ai 但没给 key，跳过 AI 判定。");
                    Console.WriteLine("   用法: CheatSimulator --ai --key sk-xxxx");
                    Console.WriteLine();
                }
                else
                {
                    using (var client = new DeepSeekClient(cfg))
                    {
                        if (!client.IsConfigured)
                        {
                            Console.WriteLine("!! AI 客户端未就绪（检查 enable_ai_review / api_base_url），跳过 AI 判定。");
                            Console.WriteLine();
                        }
                        else
                        {
                            Console.WriteLine("== 正在逐个调用 AI 复核（可能较慢）==");
                            Console.WriteLine();
                            foreach (Scenarios.ScenarioResult r in results)
                            {
                                string report = Heuristics.BuildReportJson(r.Stats, cfg, r.Local);
                                r.Verdict = client.ReviewAsync(report).GetAwaiter().GetResult();
                                Console.Write("   " + r.Scenario.Id.PadRight(20));
                                Console.WriteLine(r.Verdict.Ok
                                    ? string.Format(CultureInfo.InvariantCulture, "AI 嫌疑度 {0,3}%  [{1}]  {2}",
                                        r.Verdict.Suspicion, r.Verdict.RecommendedAction, r.Verdict.Reasoning)
                                    : "AI 失败: " + r.Verdict.Error);
                            }

                            Console.WriteLine();
                        }
                    }
                }
            }

            PrintTable(results);
            int failures = PrintVerdict(results, withAi);

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "结论：本地检测行为符合预期 ✅"
                : string.Format(CultureInfo.InvariantCulture, "结论：{0} 项不符合预期 ❌", failures));

            if (!withAi)
            {
                Console.WriteLine();
                Console.WriteLine("提示：加 --ai --key sk-xxx 可以把这些场景真的送到 DeepSeek 判定，");
                Console.WriteLine("      从而验证「AI 会不会把高玩误判成作弊」这类问题。");
            }

            return failures == 0 ? 0 : 1;
        }

        // ──────────────────────────────────────────────

        private static void PrintBanner(bool withAi, string key)
        {
            Console.WriteLine("================================================================");
            Console.WriteLine(" DeepSeekAntiCheat —— 作弊行为模拟器");
            Console.WriteLine("================================================================");
            Console.WriteLine();
            Console.WriteLine(" 本工具只生成伪造的行为统计数据，不接触游戏、不注入内存、不伪造网络包。");
            Console.WriteLine(" 它无法用于在服务器上作弊，只用于验证反作弊的检测能力。");
            Console.WriteLine();
            Console.WriteLine(" AI 判定: " + (withAi
                ? (string.IsNullOrWhiteSpace(key) ? "已请求（但未提供 key）" : "已启用")
                : "未启用（仅本地启发式）"));
            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine();
        }

        private static void PrintTable(IList<Scenarios.ScenarioResult> results)
        {
            Console.WriteLine("== 本地启发式评分 ==");
            Console.WriteLine();
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                " {0,-20} {1,-26} {2,7} {3,8} {4,8} {5,8} {6,8}",
                "场景", "说明", "评分", "命中率", "爆头率", "杀/分", "送审"));
            Console.WriteLine(" " + new string('-', 92));

            foreach (Scenarios.ScenarioResult r in results)
            {
                PlayerStats s = r.Stats;
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    " {0,-20} {1,-26} {2,7:F1} {3,8:P0} {4,8:P0} {5,8:F1} {6,8} {7,8}",
                    Trunc(r.Scenario.Id, 20),
                    Trunc(r.Scenario.Name, 24),
                    r.Local.Score,
                    s.Accuracy,
                    s.HeadshotRatio,
                    s.KillsPerMinute,
                    r.WouldReportToAi ? "是" : "否",
                    r.Local.HasHardViolation ? "是" : "否"));
            }

            Console.WriteLine();
        }

        private static int PrintVerdict(IList<Scenarios.ScenarioResult> results, bool withAi)
        {
            int failures = 0;

            Console.WriteLine("== 逐场景明细 ==");
            Console.WriteLine();

            foreach (Scenarios.ScenarioResult r in results)
            {
                bool localOk = r.LocalExpectationMet;
                if (!localOk)
                {
                    failures++;
                }

                string tag = r.Scenario.IsCheat ? "作弊" : "正常";
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] {1}  ({2})  本地评分 {3:F1}  期望 {4:F0}~{5:F0}  {6}",
                    tag, r.Scenario.Name, r.Scenario.Id, r.Local.Score,
                    r.Scenario.ExpectScoreAtLeast, r.Scenario.ExpectScoreAtMost,
                    localOk ? "OK" : "!! 不符"));

                Console.WriteLine("      说明: " + r.Scenario.Note);

                if (r.Scenario.IsCheat && r.Scenario.ExpectReport && !r.WouldReportToAi)
                {
                    Console.WriteLine("      !! 这是预期能抓到的作弊，但本地评分没到送审阈值 —— 检测存在盲区");
                    failures++;
                }

                if (r.Scenario.IsCheat && !r.Scenario.ExpectReport && r.WouldReportToAi)
                {
                    Console.WriteLine("      !! 单轴弱信号本不该单独送审，却送审了 —— 会浪费 AI 额度");
                    failures++;
                }

                if (!r.Scenario.IsCheat && r.WouldReportToAi)
                {
                    Console.WriteLine("      !! 这是正常玩家场景，却被送去 AI 复核 —— 可能造成误报，建议上调阈值");
                    failures++;
                }

                foreach (string reason in r.Local.Reasons)
                {
                    Console.WriteLine("      · " + reason);
                }

                if (r.Verdict != null)
                {
                    if (r.Verdict.Ok)
                    {
                        Console.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "      AI: 嫌疑度 {0}%，建议 {1} —— {2}",
                            r.Verdict.Suspicion, r.Verdict.RecommendedAction, r.Verdict.Reasoning));

                        // 关键检验：AI 会不会把正常玩家判成作弊
                        if (!r.Scenario.IsCheat && r.Verdict.Suspicion >= 80)
                        {
                            Console.WriteLine("      !! 误报：正常玩家被 AI 判为高度可疑");
                            failures++;
                        }

                        if (r.Scenario.IsCheat && r.Verdict.Suspicion < 30)
                        {
                            Console.WriteLine("      !! 漏报：明显作弊被 AI 判为低嫌疑");
                            failures++;
                        }
                    }
                    else
                    {
                        Console.WriteLine("      AI: 调用失败 —— " + r.Verdict.Error);
                        failures++;
                    }
                }

                Console.WriteLine();
            }

            return failures;
        }

        private static string Trunc(string s, int n) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        private static bool HasFlag(string[] args, string name) =>
            args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
