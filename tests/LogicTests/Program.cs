namespace LogicTests
{
    using System;
    using System.Globalization;
    using DeepSeekAntiCheat;

    /// <summary>离线验证评分与 JSON 生成逻辑（不需要游戏）。</summary>
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            TestCleanPlayerScoresZero();
            TestObviousAimbotScoresHigh();
            TestTooFewSamplesIsIgnored();
            TestKillsPerMinuteShortSampleGuard();
            TestReportJsonIsValidAndEscaped();
            TestCooldownBudgetDefaults();
            TestLocalModelAllowsEmptyKey();
            TestDeepSeekWithoutKeyIsSkipped();
            TestAiReviewCanBeDisabled();
            TestJsonResponseFormatToggle();
            TestExtractJsonFromProse();
            TestEndToEndAgainstMockServer();

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "全部通过 ✅"
                : string.Format(CultureInfo.InvariantCulture, "{0} 项失败 ❌", failures));
            return failures == 0 ? 0 : 1;
        }

        // ──────────────────────────────────────────────

        private static void TestCleanPlayerScoresZero()
        {
            var cfg = new Config { MinSamples = 25 };
            var s = new PlayerStats { ShotsFired = 200, ShotsHit = 60, Headshots = 12, Kills = 4 };
            s.FirstKillAt = DateTime.UtcNow.AddMinutes(-8);

            Heuristics.Result r = Heuristics.Evaluate(s, cfg);
            Check("正常玩家（30% 命中 / 20% 爆头）评分应很低", r.Score < 10f,
                string.Format(CultureInfo.InvariantCulture, "实际 {0:F1}", r.Score));
        }

        private static void TestObviousAimbotScoresHigh()
        {
            var cfg = new Config { MinSamples = 25 };
            var s = new PlayerStats
            {
                ShotsFired = 100,
                ShotsHit = 95,          // 95% 命中
                Headshots = 90,         // 95% 爆头
                LongRangeHeadshots = 70,
                Kills = 12,
                MaxSingleHitDamage = 250f,
                PeakShotsPerSecond = 40f,
                LongestHitDistance = 180f,
            };
            s.FirstKillAt = DateTime.UtcNow.AddMinutes(-2);

            Heuristics.Result r = Heuristics.Evaluate(s, cfg);
            Check("明显自瞄（95% 命中 / 95% 爆头 / 40 发秒 / 250 伤害）应接近满分", r.Score >= 95f,
                string.Format(CultureInfo.InvariantCulture, "实际 {0:F1}", r.Score));
            Check("应给出多条理由", r.Reasons.Count >= 5,
                string.Format(CultureInfo.InvariantCulture, "实际 {0} 条", r.Reasons.Count));

            Console.WriteLine("    理由明细:");
            foreach (string reason in r.Reasons)
            {
                Console.WriteLine("      - " + reason);
            }
        }

        private static void TestTooFewSamplesIsIgnored()
        {
            var cfg = new Config { MinSamples = 25 };
            var s = new PlayerStats { ShotsFired = 5, ShotsHit = 5, Headshots = 5, Kills = 3 };
            s.FirstKillAt = DateTime.UtcNow.AddSeconds(-10);

            Heuristics.Result r = Heuristics.Evaluate(s, cfg);
            Check("样本不足时不应因爆头率加分", r.Score < 40f,
                string.Format(CultureInfo.InvariantCulture, "实际 {0:F1}", r.Score));
        }

        private static void TestKillsPerMinuteShortSampleGuard()
        {
            var s = new PlayerStats { Kills = 3 };
            s.FirstKillAt = DateTime.UtcNow.AddSeconds(-3);   // 3 秒 3 杀
            float kpm = s.KillsPerMinute;

            // 15 秒归一化 => 3 / 0.25 = 12 每分，而不是 60
            Check("短样本击杀速率应被归一化保护（≤15/分）", kpm <= 15f,
                string.Format(CultureInfo.InvariantCulture, "实际 {0:F1}/分", kpm));
        }

        private static void TestReportJsonIsValidAndEscaped()
        {
            var cfg = new Config { IncludePlayerIdentity = true };
            var s = new PlayerStats
            {
                UserId = "76561198000000000@steam",
                Nickname = "坏\"人\"\\测试\n换行",
                ShotsFired = 50,
                ShotsHit = 30,
                Headshots = 20,
                Kills = 5,
            };
            s.FirstKillAt = DateTime.UtcNow.AddMinutes(-3);

            Heuristics.Result local = Heuristics.Evaluate(s, cfg);
            string json = Heuristics.BuildReportJson(s, cfg, local);

            // 用 net48 自带的解析器验证它真的是合法 JSON
            bool parsed = false;
            string err = null;
            try
            {
                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                object o = js.DeserializeObject(json);
                parsed = o is System.Collections.Generic.Dictionary<string, object>;
            }
            catch (Exception e)
            {
                err = e.Message;
            }

            Check("生成的报告必须是合法 JSON（含引号/反斜杠/换行转义）", parsed, err ?? "解析结果不是对象");
            Check("报告里应包含 local_heuristic_score", json.Contains("\"local_heuristic_score\""), "缺字段");

            // 身份开关
            var cfg2 = new Config { IncludePlayerIdentity = false };
            string json2 = Heuristics.BuildReportJson(s, cfg2, local);
            Check("关闭身份开关时不应包含昵称/UserID", !json2.Contains("nickname") && !json2.Contains("user_id"),
                "仍含身份字段");
        }

        private static void TestCooldownBudgetDefaults()
        {
            var cfg = new Config();

            // 默认处置按版本分叉（这是需求变更后的规格）：
            //   自用版 -> Kill（只处死，测试时不会封自己人）
            //   公共版 -> Ban（走递进阶梯，前几次也是处死）
            // 两种都不该是「一上来就封很久」。
#if AWA_EDITION_PRIVATE
            const VerdictAction ExpectedAction = VerdictAction.Kill;
            const string ExpectedName = "自用版";
#else
            const VerdictAction ExpectedAction = VerdictAction.Ban;
            const string ExpectedName = "公共版";
#endif
            Check("默认处置符合版本规格（" + ExpectedName + "）", cfg.Action == ExpectedAction,
                cfg.Action.ToString());

            // 递进阶梯：前面几档必须不是封禁，避免误判直接封人
            Check("递进阶梯默认开启", cfg.UseProgressiveBans, cfg.UseProgressiveBans.ToString());
            int firstBanIndex = -1;
            for (int i = 0; i < cfg.PunishmentLadder.Count; i++)
            {
                var step = Punishment.Parse(cfg.PunishmentLadder[i]);
                if (!step.IsKill) { firstBanIndex = i; break; }
            }
            Check("阶梯里第一次封禁不早于第 3 档（前面先处死）",
                firstBanIndex < 0 || firstBanIndex >= 2,
                firstBanIndex < 0 ? "全是处死" : ("第 " + (firstBanIndex + 1) + " 档开始封"));
            Check("阶梯最后应该是永久封禁",
                cfg.PunishmentLadder.Count > 0 && Punishment.Parse(cfg.PunishmentLadder[cfg.PunishmentLadder.Count - 1]).Label.Contains("永久"),
                cfg.PunishmentLadder.Count > 0 ? Punishment.Parse(cfg.PunishmentLadder[cfg.PunishmentLadder.Count - 1]).Label : "(空)");

            Check("默认每局全局 API 上限应存在且有限", cfg.MaxApiCallsPerRound > 0 && cfg.MaxApiCallsPerRound <= 50,
                cfg.MaxApiCallsPerRound.ToString(CultureInfo.InvariantCulture));
            Check("默认不含玩家身份", !cfg.IncludePlayerIdentity, "IncludePlayerIdentity=true");
            Check("默认 ApiKey 为空（不配就不联网）", string.IsNullOrEmpty(cfg.ApiKey), cfg.ApiKey);
        }

        // ──────────────────────────────────────────────

        private static void TestLocalModelAllowsEmptyKey()
        {
            var cfg = new Config
            {
                EnableAiReview = true,
                ApiKey = string.Empty,
                ApiBaseUrl = "http://127.0.0.1:11434/v1",
            };
            using (var c = new DeepSeekClient(cfg))
            {
                Check("本地模型（Ollama）空 Key 也必须能调用", c.IsConfigured, "IsConfigured=false");
            }
        }

        private static void TestDeepSeekWithoutKeyIsSkipped()
        {
            var cfg = new Config
            {
                EnableAiReview = true,
                ApiKey = string.Empty,
                ApiBaseUrl = "https://api.deepseek.com",
            };
            using (var c = new DeepSeekClient(cfg))
            {
                Check("官方端点空 Key 应直接跳过（否则只会拿到 401 白烧预算）", !c.IsConfigured, "IsConfigured=true");
            }
        }

        private static void TestAiReviewCanBeDisabled()
        {
            var cfg = new Config
            {
                EnableAiReview = false,
                ApiKey = "sk-should-be-ignored",
                ApiBaseUrl = "https://api.deepseek.com",
            };
            using (var c = new DeepSeekClient(cfg))
            {
                Check("EnableAiReview=false 时即使有 Key 也不该联网", !c.IsConfigured, "IsConfigured=true");
            }
        }

        private static void TestJsonResponseFormatToggle()
        {
            var s = new PlayerStats { ShotsFired = 30, ShotsHit = 20, Headshots = 15, Kills = 3 };
            s.FirstKillAt = DateTime.UtcNow.AddMinutes(-4);
            Heuristics.Result local = Heuristics.Evaluate(s, new Config());

            var on = new Config { UseJsonResponseFormat = true };
            var off = new Config { UseJsonResponseFormat = false };

            string bodyOn = InvokeBuildBody(on, s, local);
            string bodyOff = InvokeBuildBody(off, s, local);

            Check("开启时应带 response_format", bodyOn.Contains("response_format"), "缺失");
            Check("关闭时不应带 response_format（部分本地框架不支持会直接报错）",
                !bodyOff.Contains("response_format"), "仍在");
            Check("两种模式下都应带 messages", bodyOn.Contains("messages") && bodyOff.Contains("messages"), "缺失");
        }

        private static void TestExtractJsonFromProse()
        {
            var t = typeof(DeepSeekClient);
            var m = t.GetMethod("ExtractJsonObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Check("应能找到 ExtractJsonObject（反射）", m != null, "方法不存在");
            if (m == null)
            {
                return;
            }

            string bare = (string)m.Invoke(null, new object[] { "{\"a\":1}" });
            Check("纯 JSON 原样返回", bare == "{\"a\":1}", bare);

            string fenced = (string)m.Invoke(null, new object[] { "```json\n{\"a\":1}\n```" });
            Check("应剥掉 ```json 围栏", fenced == "{\"a\":1}", fenced);

            string prose = (string)m.Invoke(null, new object[] { "好的，我的判断是：\n{\"suspicion\": 88, \"reasoning\": \"爆头率异常\"}\n以上。" });
            Check("应能从散文里抠出 JSON 对象", prose == "{\"suspicion\": 88, \"reasoning\": \"爆头率异常\"}", prose);

            // 理由里含花括号和引号，不能把配对搞错
            string tricky = (string)m.Invoke(null, new object[] { "分析: {\"reasoning\":\"他用的是{奇怪}武器\",\"suspicion\":50} 完" });
            bool trickyOk;
            try
            {
                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                trickyOk = js.DeserializeObject(tricky) is System.Collections.Generic.Dictionary<string, object>;
            }
            catch
            {
                trickyOk = false;
            }

            Check("字符串内的花括号不应破坏配对", trickyOk, tricky);
        }

        private static string InvokeBuildBody(Config cfg, PlayerStats s, Heuristics.Result local)
        {
            var t = typeof(DeepSeekClient);
            var m = t.GetMethod("BuildRequestBody", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object inst = Activator.CreateInstance(t, cfg);
            try
            {
                return (string)m.Invoke(inst, new object[] { Heuristics.BuildReportJson(s, cfg, local) });
            }
            finally
            {
                (inst as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// 端到端：起一个最小 TCP mock 服务器冒充 OpenAI 兼容接口，
        /// 跑完整链路（拼请求 → 发 HTTP → 解析外层 JSON → 抠出内层 JSON → 得到结论）。
        /// 这样验证的是真正会上服务器的代码路径，而不是让反射绕过它。
        /// </summary>
        private static void TestEndToEndAgainstMockServer()
        {
            const int port = 18099;
            string capturedBody = null;
            Exception serverError = null;

            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            try
            {
                listener.Start();
            }
            catch (Exception e)
            {
                Check("端到端：mock 服务器应能监听本地端口", false, e.Message);
                return;
            }

            var server = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var sock = listener.AcceptTcpClient())
                    using (var ns = sock.GetStream())
                    {
                        // 读请求头，从中拿 Content-Length
                        var headerBytes = new System.Collections.Generic.List<byte>();
                        int prev3 = -1, prev2 = -1, prev1 = -1;
                        int cur;
                        while ((cur = ns.ReadByte()) >= 0)
                        {
                            headerBytes.Add((byte)cur);
                            if (prev3 == '\r' && prev2 == '\n' && prev1 == '\r' && cur == '\n')
                            {
                                break;
                            }

                            prev3 = prev2; prev2 = prev1; prev1 = cur;
                        }

                        string headers = System.Text.Encoding.ASCII.GetString(headerBytes.ToArray());
                        int len = 0;
                        foreach (string line in headers.Split('\n'))
                        {
                            if (line.ToLowerInvariant().StartsWith("content-length:"))
                            {
                                int.TryParse(line.Substring(15).Trim(), out len);
                            }
                        }

                        var bodyBuf = new byte[len];
                        int read = 0;
                        while (read < len)
                        {
                            int n = ns.Read(bodyBuf, read, len - read);
                            if (n <= 0)
                            {
                                break;
                            }

                            read += n;
                        }

                        capturedBody = System.Text.Encoding.UTF8.GetString(bodyBuf, 0, read);

                        // 冒充 DeepSeek 的响应：content 里是模型输出的 JSON 字符串
                        string inner = "{\\\"suspicion\\\": 91, \\\"reasoning\\\": \\\"爆头率 97% 且峰值射速 40 发/秒\\\", \\\"recommended_action\\\": \\\"ban\\\"}";
                        string payload = "{\"id\":\"x\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"" + inner + "\"}}]}";
                        byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);

                        string head = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                                      payloadBytes.Length + "\r\nConnection: close\r\n\r\n";
                        byte[] headBytes = System.Text.Encoding.ASCII.GetBytes(head);
                        ns.Write(headBytes, 0, headBytes.Length);
                        ns.Write(payloadBytes, 0, payloadBytes.Length);
                        ns.Flush();
                    }
                }
                catch (Exception e)
                {
                    serverError = e;
                }
                finally
                {
                    listener.Stop();
                }
            });

            try
            {
                var cfg = new Config
                {
                    EnableAiReview = true,
                    ApiKey = "sk-test",
                    ApiBaseUrl = "http://127.0.0.1:" + port + "/v1",
                    Model = "deepseek-chat",
                };

                using (var client = new DeepSeekClient(cfg))
                {
                    DeepSeekVerdict v = client.ReviewAsync("{\"shots_hit\":95}").GetAwaiter().GetResult();

                    Check("端到端：请求应成功", v.Ok, v.Error);
                    Check("端到端：应解析出嫌疑度 91", v.Suspicion == 91,
                        v.Suspicion.ToString(CultureInfo.InvariantCulture));
                    Check("端到端：应解析出中文理由", v.Reasoning != null && v.Reasoning.Contains("爆头率"), v.Reasoning ?? "null");
                    Check("端到端：应解析出建议动作 ban", v.RecommendedAction == "ban", v.RecommendedAction);
                }
            }
            catch (Exception e)
            {
                Check("端到端：调用过程不应抛异常", false, e.Message);
            }

            server.Wait(8000);

            if (serverError != null)
            {
                Check("端到端：mock 服务器不应报错", false, serverError.Message);
            }

            Check("端到端：请求体应含 model / messages / Authorization",
                capturedBody != null
                && capturedBody.Contains("deepseek-chat")
                && capturedBody.Contains("messages")
                && capturedBody.Contains("response_format"),
                capturedBody == null ? "没收到请求体" : "字段缺失");
        }

        // ──────────────────────────────────────────────

        private static void Check(string name, bool ok, string detail)
        {
            if (ok)
            {
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                failures++;
                Console.WriteLine("[FAIL] " + name + "  ->  " + detail);
            }
        }
    }
}
