// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  本文件由 AWA 编写。
// ============================================================================
namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using CommandSystem;
    using Exiled.API.Features;
    using RemoteAdmin;

    /// <summary>
    /// 游戏内测试命令：在真实服务器上把作弊场景跑一遍检测链路。
    ///
    /// 安全保证：模拟场景的 UserId 为 null，处置环节按 UserId 找不到玩家，
    /// 因此**测试在代码层面不可能踢人封人**，与 action 配置无关。
    /// </summary>
    // 三个 handler 都要标 —— EXILED 的注册逻辑是 if/else-if 链，
    // 一条命令只会被注册进「标注里的那一个」处理器：
    //   游戏内 ~ 控制台 -> RemoteAdmin.QueryProcessor.DotCommandHandler (ClientCommandHandler)
    //   服务器黑窗口   -> GameCore.Console.ConsoleCommandHandler        (GameConsoleCommandHandler)
    //   RA 面板        -> CommandProcessor.RemoteAdminCommandHandler
    // 只标 RA 的话，游戏内控制台会报 "Command not found."
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    [CommandHandler(typeof(GameConsoleCommandHandler))]
        // AWA :: 游戏内测试命令。
        // 模拟场景的 UserId 恒为 null，处置环节找不到玩家 —— 测试不可能踢人。
    public sealed partial class TestCommand : ICommand
    {
        /// <inheritdoc/>
        public string Command => "dsac";

        /// <inheritdoc/>
        public string[] Aliases => new[] { "deepseekanticheat" };

        /// <inheritdoc/>
        public string Description => "DeepSeekAntiCheat 测试工具：模拟作弊场景，验证检测是否有效";

        /// <inheritdoc/>
        public string[] Usage => new[]
        {
            "list",
            "sim <场景id>",
            "ai <场景id>",
            "stats",
            "panel [玩家名]",
            "report",
            "follow <玩家>",
            "unfollow <玩家>",
            "replay <玩家> [条数]",
            "evidence",
            "selftest",
            "reliability",
            "bans [玩家]",
            "features",
            "whitelist [add|remove|addrole|removerole|clear]",
        };

        /// <inheritdoc/>
        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            bool ok = this.ExecuteInner(arguments, sender, out response);
            response = Clamp(response);
            return ok;
        }

        /// <summary>
        /// 命令回复有长度上限，超了会被游戏截断（看起来像「没显示全」）。
        /// 这里主动截断并告诉玩家怎么看到剩下的，比被静默截断好。
        /// </summary>
        private static string Clamp(string text)
        {
            const int Limit = 1100;
            if (string.IsNullOrEmpty(text) || text.Length <= Limit)
            {
                return text;
            }

            return text.Substring(0, Limit) +
                   "\n\n…（输出过长已截断）\n" +
                   "用更具体的子命令看剩下的，例如 .dsac list / .dsac report / .dsac reliability";
        }

        private bool ExecuteInner(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            DeepSeekAntiCheatPlugin plugin = DeepSeekAntiCheatPlugin.Instance;
            if (plugin == null)
            {
                response = "插件未加载。";
                return false;
            }

            string[] args = arguments.ToArray();
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

            switch (sub)
            {
                case "list":
                    response = ListScenarios();
                    return true;

                case "sim":
                    return this.RunScenario(plugin, args, withAi: false, sender: sender, response: out response);

                case "ai":
                    return this.RunScenario(plugin, args, withAi: true, sender: sender, response: out response);

                case "stats":
                    response = plugin.DescribeTrackedPlayers();
                    return true;

                case "panel":
                    return TogglePanel(plugin, args, sender, out response);


                case "report":
                    response = plugin.DescribeDetectors();
                    return true;

                case "follow":
                case "unfollow":
                    return SetFollow(plugin, args, sub == "follow", out response);

                case "replay":
                    return Replay(plugin, args, out response);

                case "evidence":
                    response = AdminTools.ListEvidence(10);
                    return true;

                case "selftest":
                    return SelfTest(plugin, out response);

                case "reliability":
                    response = ReliabilityTable();
                    return true;

                case "features":
                    response = FeaturesTable(plugin.Config);
                    return true;

                case "whitelist":
                    return Whitelist(plugin, args, out response);

                case "bans":
                    return Bans(plugin, args, out response);

                default:
                    response = PrefixHint() + "未知子命令。用法：\n" +
                       string.Join("\n", this.Usage.Select(u => "  dsac " + u));
                    return false;
            }
        }

        // ────────────────────────── 游戏内管理工具 ──────────────────────────

        /// <summary>切换 HUD 面板。可以显式指定玩家：dsac panel <玩家名></summary>
        private static bool TogglePanel(DeepSeekAntiCheatPlugin plugin, string[] args, ICommandSender sender, out string response)
        {
            Player admin = null;

            // preview 模式：不依赖 HUD，直接把面板内容返回给你看
            if (args.Length >= 2 && string.Equals(args[1], "preview", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine("面板预览（纯文本，实际显示在右上角 HUD）");
                sb.AppendLine();
                sb.Append(AdminTools.RenderPanelPlain(
                    plugin.GetTopSuspicious(plugin.Config.PanelTopCount),
                    plugin.Config,
                    plugin.TrackedCount,
                    plugin.ApiCallsThisRound));
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("配色：黑 / 白 / 蓝 —— 标题与高分用蓝，玩家名白色，次要信息灰色。");
                sb.AppendLine("如果上面有内容但游戏里看不到，说明是 HUD 显示的问题，不是渲染的问题。");
                response = sb.ToString();
                return true;
            }

            // 显式指定优先（万一自动识别失败，这是兜底）
            if (args.Length >= 2)
            {
                admin = ResolvePlayerByName(args[1]);
                if (admin == null)
                {
                    response = "找不到在线玩家：" + args[1];
                    return false;
                }
            }
            else
            {
                admin = ResolvePlayer(sender);
            }

            if (admin == null)
            {
                response =
                    "没法确定你是哪个玩家，面板开不了（控制台发起的命令没有 HUD）。\n\n" +
                    "两种解决办法：\n" +
                    "  1) 在游戏内按 ~ 打开控制台，输入：dsac panel\n" +
                    "  2) 显式指定自己：dsac panel <你的游戏昵称>";
                return false;
            }

            bool on = plugin.TogglePanel(admin);
            response = on
                ? "面板已开启 —— " + admin.Nickname + " 的右上角会每 " +
                  plugin.Config.PanelRefreshSeconds + " 秒刷新一次嫌疑排行。\n再执行一次 dsac panel 可关闭。"
                : "面板已关闭（" + admin.Nickname + "）。";
            return true;
        }

        /// <summary>按昵称或 UserId 找在线玩家。</summary>
        private static Player ResolvePlayerByName(string query)
        {
            foreach (Player p in Player.List)
            {
                if (p == null || !p.IsConnected)
                {
                    continue;
                }

                if (string.Equals(p.Nickname, query, StringComparison.OrdinalIgnoreCase)
                    || (p.UserId != null && string.Equals(p.UserId, query, StringComparison.OrdinalIgnoreCase)))
                {
                    return p;
                }
            }

            foreach (Player p in Player.List)
            {
                if (p != null && p.IsConnected && p.Nickname != null
                    && p.Nickname.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>开始/停止跟踪某个玩家。</summary>
        private static bool SetFollow(DeepSeekAntiCheatPlugin plugin, string[] args, bool on, out string response)
        {
            if (args.Length < 2)
            {
                response = "用法：dsac " + (on ? "follow" : "unfollow") + " <玩家昵称>";
                return false;
            }

            if (!plugin.SetFollow(args[1], on, out PlayerStats found))
            {
                response = "找不到玩家：" + args[1] + "\n用 dsac stats 看当前跟踪名单。";
                return false;
            }

            response = on
                ? "已开始跟踪 " + found.Nickname + "。他的所有事件会单独记录，用 dsac replay " + found.Nickname + " 查看时间线。"
                : "已停止跟踪 " + found.Nickname + "。";
            return true;
        }

        /// <summary>输出某个玩家的行为时间线。</summary>
        private static bool Replay(DeepSeekAntiCheatPlugin plugin, string[] args, out string response)
        {
            if (args.Length < 2)
            {
                response = "用法：dsac replay <玩家昵称> [显示条数，默认 40]";
                return false;
            }

            PlayerStats st = plugin.FindStats(args[1]);
            if (st == null)
            {
                response = "找不到玩家：" + args[1];
                return false;
            }

            int max = 40;
            if (args.Length >= 3 && int.TryParse(args[2], out int parsed) && parsed > 0)
            {
                max = Math.Min(parsed, PlayerStats.TimelineCapacity);
            }

            response = AdminTools.RenderTimeline(st, max);
            return true;
        }

        /// <summary>检测能力可靠性对照表。</summary>
        private static string ReliabilityTable()
        {
            var sb = new StringBuilder();
            sb.AppendLine("检测能力与可靠性对照（服务端视角）");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, " {0,-16} {1,-8} {2}\n", "检测项", "可靠性", "依据");
            sb.AppendLine(" " + new string('-', 74));

            var rows = new[]
            {
                new[] { "NoClip",     "★★★", "服务端状态位直接可读，权限为假却启用即为铁证" },
                new[] { "速度异常",    "★★★", "位置是服务端权威数据，瞬时速度直接算" },
                new[] { "瞬移",       "★★★", "同上；已排除 SCP-106/096/173 的机制位移" },
                new[] { "飞天/悬浮",   "★★",  "垂直速度；受地形与卡顿影响，偶有误报" },
                new[] { "隐身目标命中", "★★",  "正常玩家看不到隐身目标，但流弹可能误中" },
                new[] { "无限弹药",    "★★",  "按「未换弹连打数」推断，命中判定有误差" },
                new[] { "异常回血",    "★★",  "依赖治疗事件；部分角色技能本就是大治疗" },
                new[] { "可疑物品",    "★",   "仅统计获取次数，具体规则需按服务器玩法调" },
                new[] { "爆头率/命中率", "★★★", "开枪与命中事件直接统计" },
                new[] { "射速/伤害",   "★★★", "服务端收到的数值直接校验" },
                new[] { "击杀速率",    "★★",  "依赖回合时长，样本短时会归一化保护" },
                new[] { "透视/ESP",    "无法", "服务端看不到客户端渲染，只能靠命中率间接推测" },
                new[] { "无后坐力",    "无法", "纯客户端表现，服务端无数据" },
                new[] { "自瞄锁定",    "无法", "同上，只能靠爆头率与反应时间间接推测" },
            };

            foreach (string[] r in rows)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0,-16} {1,-8} {2}\n", r[0], r[1], r[2]);
            }

            sb.AppendLine();
            sb.AppendLine("★★★ = 服务端直接可见，误报极低");
            sb.AppendLine("★★  = 服务端可推断，需要配合 AI 复核");
            sb.AppendLine("★   = 只是弱信号，不单独作为依据");
            sb.AppendLine("无法 = 服务端原理上看不到，任何声称能检测的都是误报");
            return sb.ToString();
        }

        /// <summary>
        /// 游戏内自检：给每个检测器灌入合成数据，验证评分链路真的通。
        /// 这验证的是「评分与理由生成」，事件挂钩本身需要真实对局才能验证。
        /// </summary>
        private static bool SelfTest(DeepSeekAntiCheatPlugin plugin, out string response)
        {
            var sb = new StringBuilder();
            Config cfg = plugin.Config;
            int pass = 0;
            int fail = 0;

            sb.AppendLine("检测链路自检（合成数据 -> 评分 -> 理由）");
            sb.AppendLine();

            void Check(string name, PlayerStats s, string expectKeyword, bool expectHard = false)
            {
                Heuristics.Result r;
                try
                {
                    r = Heuristics.Evaluate(s, cfg);
                }
                catch (Exception e)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  [×] {0,-14} 评分抛异常: {1}\n", name, e.Message);
                    fail++;
                    return;
                }

                bool hit = r.Reasons.Exists(x => x.IndexOf(expectKeyword, StringComparison.Ordinal) >= 0);
                bool hardOk = !expectHard || r.HasHardViolation;
                bool ok = hit && hardOk;

                if (ok)
                {
                    pass++;
                }
                else
                {
                    fail++;
                }

                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  [{0}] {1,-14} 评分 {2,5:F1}  送审 {3,-2}  硬违规 {4,-2}  {5}\n",
                    ok ? "√" : "×", name, r.Score,
                    r.ShouldReport(cfg.LocalScoreToReport) ? "是" : "否",
                    r.HasHardViolation ? "是" : "否",
                    ok ? string.Empty : "（预期理由含「" + expectKeyword + "」）");
            }

            // 基线：正常玩家不该有分
            var clean = new PlayerStats { ShotsFired = 200, ShotsHit = 60, Headshots = 12, Kills = 4 };
            clean.FirstKillAt = DateTime.UtcNow.AddMinutes(-8);
            Check("正常玩家", clean, "绝不该出现的词");

            // NoClip
            var noclip = new PlayerStats { ShotsFired = 10, ShotsHit = 3, NoclipActivations = 1, NoclipContext = "自检" };
            Check("NoClip", noclip, "NoClip", expectHard: true);

            // 移动
            var move = new PlayerStats { ShotsFired = 10, ShotsHit = 3, MoveViolationCount = 3, MaxHorizontalSpeed = 40f };
            move.AddMoveViolation("自检合成数据");
            Check("移动异常", move, "移动异常", expectHard: true);

            // 隐身命中
            var inv = new PlayerStats { ShotsFired = 20, ShotsHit = 10, InvisibleTargetHits = 3 };
            Check("隐身目标命中", inv, "隐身");

            // 弹药
            var ammo = new PlayerStats { ShotsFired = 200, ShotsHit = 50, MaxShotsWithoutReload = 300 };
            Check("无限弹药", ammo, "未换弹");

            // 回血
            var heal = new PlayerStats { ShotsFired = 20, ShotsHit = 5, AbnormalHealCount = 2, MaxSingleHeal = 900f };
            Check("异常回血", heal, "异常治疗");

            // 物品
            var item = new PlayerStats { ShotsFired = 20, ShotsHit = 5, SuspiciousItemCount = 3 };
            Check("可疑物品", item, "可疑物品");

            // 原有维度
            var hs = new PlayerStats { ShotsFired = 120, ShotsHit = 110, Headshots = 105, Kills = 5 };
            hs.FirstKillAt = DateTime.UtcNow.AddMinutes(-5);
            Check("爆头率", hs, "爆头率");

            var dmg = new PlayerStats { ShotsFired = 60, ShotsHit = 40, MaxSingleHitDamage = 600f };
            Check("伤害异常", dmg, "单发最高伤害", expectHard: true);

            var sps = new PlayerStats { ShotsFired = 200, ShotsHit = 50, PeakShotsPerSecond = 55f };
            Check("射速异常", sps, "峰值射速", expectHard: true);

            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "结果：{0} 项通过，{1} 项失败\n", pass, fail);
            sb.AppendLine();
            sb.AppendLine("说明：这里验证的是「评分与理由生成」链路。");
            sb.AppendLine("      事件挂钩（Shot/NoClip/位置采样）需要真实对局才能验证 ——");
            sb.AppendLine("      开一局后执行 dsac stats 看有没有数据进来。");

            response = sb.ToString();
            return fail == 0;
        }

        /// <summary>
        /// 把命令发起者解析成 Player（HUD 面板需要）。
        ///
        /// 关键：不能用 LogName 比对昵称 —— PlayerCommandSender.LogName 的格式是
        /// "昵称 (steamid@steam)"，永远匹配不上 p.Nickname。
        /// 正确做法是用 CommandSender.SenderId —— 它就是 UserId。
        /// </summary>
        private static Player ResolvePlayer(ICommandSender sender)
        {
            if (sender == null)
            {
                return null;
            }

            // ① 最可靠：CommandSender.SenderId 就是 UserId
            var cs = sender as CommandSender;
            if (cs != null)
            {
                string uid = null;
                string nick = null;
                try
                {
                    uid = cs.SenderId;
                    nick = cs.Nickname;
                }
                catch
                {
                    // 某些 sender 实现可能抛异常
                }

                if (!string.IsNullOrEmpty(uid))
                {
                    foreach (Player p in Player.List)
                    {
                        if (p != null && p.IsConnected
                            && string.Equals(p.UserId, uid, StringComparison.OrdinalIgnoreCase))
                        {
                            return p;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(nick))
                {
                    foreach (Player p in Player.List)
                    {
                        if (p != null && p.IsConnected
                            && string.Equals(p.Nickname, nick, StringComparison.OrdinalIgnoreCase))
                        {
                            return p;
                        }
                    }
                }
            }

            // ② 兜底：从 LogName 里剥出昵称（"昵称 (userId)" -> "昵称"）
            string name = SenderName(sender);
            if (!string.IsNullOrEmpty(name))
            {
                int paren = name.LastIndexOf(" (", StringComparison.Ordinal);
                if (paren > 0)
                {
                    name = name.Substring(0, paren);
                }

                foreach (Player p in Player.List)
                {
                    if (p != null && p.IsConnected
                        && string.Equals(p.Nickname, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return p;
                    }
                }
            }

            return null;
        }
        /// <summary>执行真实性钻取。</summary>

        /// <summary>
        /// 前缀提示。游戏内控制台的命令必须加 '.' 才会发到服务端 ——
        /// 这是游戏本体 GameCore.Console.TypeCommand 的分发规则，和插件无关。
        /// </summary>
        internal static string PrefixHint() =>
            "【重要】游戏内按 ~ 输入时，命令前要加一个点：例如 .dsac list\n" +
            "        （服务器黑窗口里则不加点：dsac list）\n\n";

        // ──────────────────────────────────────────────

        /// <summary>取出命令发起者的可读名字。</summary>
        private static string SenderName(ICommandSender sender)
        {
            if (sender == null)
            {
                return "console";
            }

            try
            {
                // CommandSender.Nickname 是最干净的名字（不带 SteamID）
                if (sender is CommandSender cs && !string.IsNullOrEmpty(cs.Nickname))
                {
                    return cs.Nickname;
                }

                // ICommandSender 接口本身只有 LogName 和 Respond
                if (!string.IsNullOrEmpty(sender.LogName))
                {
                    return sender.LogName;
                }
            }
            catch
            {
                // 某些 sender 实现可能抛异常，忽略
            }

            return "unknown";
        }

        private static string ListScenarios()
        {
            var sb = new StringBuilder();
            sb.AppendLine("可用作弊模拟场景：");
            sb.AppendLine();

            foreach (Scenarios.Scenario sc in Scenarios.All())
            {
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  {0,-18} {1,-24} {2}\n",
                    sc.Id,
                    sc.Name,
                    sc.IsCheat ? (sc.ExpectReport ? "[作弊·预期上报]" : "[作弊·单轴弱信号]") : "[正常·不应上报]");
            }

            sb.AppendLine();
            sb.AppendLine("  dsac sim <场景id>   本地评分（秒回，不调用 AI）");
            sb.AppendLine("  dsac ai  <场景id>   本地评分 + DeepSeek 复核（异步，结果会广播）");
            return sb.ToString();
        }

        private bool RunScenario(
            DeepSeekAntiCheatPlugin plugin,
            string[] args,
            bool withAi,
            ICommandSender sender,
            out string response)
        {
            if (args.Length < 2)
            {
                response = "用法：dsac " + (withAi ? "ai" : "sim") + " <场景id>\n用 dsac list 查看全部场景。";
                return false;
            }

            string id = args[1];
            Scenarios.Scenario sc = Scenarios.All()
                .FirstOrDefault(s => s.Id.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0);

            if (sc == null)
            {
                response = "找不到场景: " + id + "\n用 dsac list 查看全部场景。";
                return false;
            }

            Config cfg = plugin.Config;
            PlayerStats stats = sc.Build(cfg);
            Heuristics.Result local = Heuristics.Evaluate(stats, cfg);
            bool report = local.ShouldReport(cfg.LocalScoreToReport) && stats.ShotsHit >= cfg.MinSamples;

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "场景 {0}（{1}）\n", sc.Id, sc.Name);
            sb.AppendFormat(CultureInfo.InvariantCulture, "说明: {0}\n", sc.Note);
            sb.AppendFormat(CultureInfo.InvariantCulture, "类型: {0}\n", sc.IsCheat ? "作弊" : "正常");
            sb.AppendLine();
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "本地评分: {0:F1}   送 AI 复核: {1}\n",
                local.Score,
                report ? "是" : "否");
            sb.AppendFormat(CultureInfo.InvariantCulture, "命中率 {0:P0}  爆头率 {1:P0}  击杀/分 {2:F1}\n",
                stats.Accuracy, stats.HeadshotRatio, stats.KillsPerMinute);
            sb.AppendLine();
            sb.AppendLine("命中项:");

            if (local.Reasons.Count == 0)
            {
                sb.AppendLine("  （无 —— 各项均在正常范围）");
            }
            else
            {
                foreach (string reason in local.Reasons)
                {
                    sb.AppendLine("  · " + reason);
                }
            }

            if (local.HardViolations.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("硬违规（物理上不可能）:");
                foreach (string v in local.HardViolations)
                {
                    sb.AppendLine("  !! " + v);
                }
            }

            // 期望核对
            bool expectOk = local.Score >= sc.ExpectScoreAtLeast && local.Score <= sc.ExpectScoreAtMost;
            sb.AppendLine();
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "期望区间 {0:F0}~{1:F0}  ->  {2}\n",
                sc.ExpectScoreAtLeast, sc.ExpectScoreAtMost, expectOk ? "符合" : "不符！");

            if (sc.IsCheat && sc.ExpectReport && !report)
            {
                sb.AppendLine("！这是预期能抓到的作弊，却没到送审阈值 —— 检测存在盲区。");
            }

            if (!sc.IsCheat && report)
            {
                sb.AppendLine("！这是正常玩家，却被送去复核 —— 可能造成误报。");
            }

            if (withAi)
            {
                if (!plugin.IsAiConfigured)
                {
                    sb.AppendLine();
                    sb.AppendLine("AI 未配置（enable_ai_review / api_key），本次只做了本地评分。");
                }
                else
                {
                    plugin.EnqueueSimulationReview(sc, stats, local, SenderName(sender));
                    sb.AppendLine();
                    sb.AppendLine("已把这个场景送 DeepSeek 复核，结果稍后会广播给管理员。");
                    sb.AppendLine("（模拟场景不可能触发踢出/封禁 —— 它没有对应的玩家。）");
                }
            }

            response = sb.ToString();
            return true;
        }
    }
}
