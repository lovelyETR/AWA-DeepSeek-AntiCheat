// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  本文件由 AWA 编写。
// ============================================================================
namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Globalization;
    using System.Threading.Tasks;
    using RemoteAdmin;
    using Exiled.API.Features;
    using Exiled.Events.EventArgs.Player;
    using Exiled.Events.EventArgs.Server;
    using MEC;

    using PlayerEvents = Exiled.Events.Handlers.Player;
    using ServerEvents = Exiled.Events.Handlers.Server;

    /// <summary>
    /// DeepSeekAntiCheat —— SCP: Secret Laboratory 的 EXILED 反作弊插件。
    ///
    /// 工作方式：
    ///   1. 订阅服务端事件，累积每个玩家的行为统计（爆头率/命中率/击杀速率/伤害/射速）。
    ///   2. 用本地启发式打分（零成本），只有分数超过阈值的样本才会被送去复核。
    ///   3. 把统计打包成 JSON 交给 DeepSeek 判定，拿回「嫌疑度 + 中文理由 + 建议动作」。
    ///   4. 按配置决定：只通报管理员 / 踢出 / 封禁。
    ///
    /// 线程模型：
    ///   HTTP 请求在后台线程执行，结果只放进 ConcurrentQueue；
    ///   由一条 MEC 协程在主线程取出来处理 —— 绝不在非主线程碰 Unity 对象。
    /// </summary>
    public sealed class DeepSeekAntiCheatPlugin : Plugin<Config>
    {
        private readonly Dictionary<string, PlayerStats> statsByUserId = new Dictionary<string, PlayerStats>();
        private readonly ConcurrentQueue<ReviewOutcome> outcomes = new ConcurrentQueue<ReviewOutcome>();

        private DeepSeekClient client;
        private CoroutineHandle pump;

        /// <summary>移动采样协程（位置类检测的数据源）。</summary>
        private CoroutineHandle moveWatch;

        /// <summary>累犯封禁台账。</summary>
        private readonly BanLedger ledger = new BanLedger();

        /// <summary>移动异常检测器。</summary>
        private readonly MovementWatch movement = new MovementWatch();

        /// <summary>每个玩家最近一次进入房间的时刻（反应时间检测用）。</summary>
        private readonly Dictionary<string, DateTime> roomEnteredAt = new Dictionary<string, DateTime>();

        /// <summary>每个玩家最近一次所在房间名。</summary>
        private readonly Dictionary<string, string> roomName = new Dictionary<string, string>();

        /// <summary>打开了 HUD 面板的管理员（按 UserId）。</summary>
        private readonly HashSet<string> panelAdmins = new HashSet<string>();

        /// <summary>面板刷新协程。</summary>
        private CoroutineHandle panelWatch;
        private int apiCallsThisRound;

        /// <inheritdoc/>
        /// <remarks>AWA 水印：作者标识不可移除。</remarks>
        public override string Author => AwaWatermark.Owner;

        /// <inheritdoc/>
        public override string Name => "DeepSeekAntiCheat";

        /// <inheritdoc/>
        public override string Prefix => "deepseek_anticheat";

        /// <inheritdoc/>
        /// <remarks>AWA 。</remarks>
        public override Version Version => new Version(1, 0, 1);

        /// <summary>当前插件实例（给测试命令用）。</summary>
        public static DeepSeekAntiCheatPlugin Instance { get; private set; }

        /// <summary>AI 复核是否可用。</summary>
        public bool IsAiConfigured => this.client != null && this.client.IsConfigured;

        /// <summary>
        /// 命令注册交给 EXILED，我们不碰。
        ///
        /// 反例（踩过的坑）：曾经用 CommandProcessor.GetAllCommands() 去验证注册结果，
        /// 它列不出自定义命令 → 误判未注册 → 兜底注册时撞名 → 刷出一堆红字。
        /// 判断注册成功的正确依据是 EXILED 自己的日志：
        ///   "Command with same name has already registered! Command: dsac"
        /// </summary>
        private void RegisterCommandsExplicitly()
        {
            // 命令注册交给 EXILED 自己处理（见 [CommandHandler] 特性）。
            // 这里以前会再注册一次，导致 "Command with same name has already registered!"，
            // 现在什么都不做。
        }
        /// <summary>插件卸载时反注册命令。</summary>
        private void UnregisterCommandsExplicitly()
        {
            try
            {
                var cmd = new TestCommand();
                try
                {
                    CommandProcessor.RemoteAdminCommandHandler.UnregisterCommand(cmd);
                }
                catch
                {
                    // 没注册过就算了
                }

                this.OnUnregisteringCommands();
            }
            catch
            {
                // 卸载阶段的异常不该影响其他插件
            }
        }

        // ────────────────────────── 游戏内管理工具 ──────────────────────────

        /// <summary>切换某个管理员的 HUD 面板。返回切换后的状态。</summary>
        public bool TogglePanel(Player admin)
        {
            if (admin == null)
            {
                return false;
            }

            string id = admin.UserId ?? admin.Nickname;
            if (this.panelAdmins.Contains(id))
            {
                this.panelAdmins.Remove(id);
                return false;
            }

            this.panelAdmins.Add(id);
            return true;
        }

        /// <summary>面板是否对这个管理员开着。</summary>
        public bool IsPanelOn(Player admin)
        {
            if (admin == null)
            {
                return false;
            }

            string id = admin.UserId ?? admin.Nickname;
            return this.panelAdmins.Contains(id);
        }

        /// <summary>取嫌疑度最高的前 N 个玩家。</summary>
        public List<PlayerStats> GetTopSuspicious(int count)
        {
            var scored = new List<KeyValuePair<float, PlayerStats>>();
            foreach (KeyValuePair<string, PlayerStats> kv in this.statsByUserId)
            {
                PlayerStats s = kv.Value;
                if (s.ShotsFired == 0 && s.NoclipActivations == 0 && s.MoveViolationCount == 0)
                {
                    continue;
                }

                Heuristics.Result r = Heuristics.Evaluate(s, this.Config);
                scored.Add(new KeyValuePair<float, PlayerStats>(r.Score, s));
            }

            scored.Sort((a, b) => b.Key.CompareTo(a.Key));

            var result = new List<PlayerStats>();
            for (int i = 0; i < Math.Min(count, scored.Count); i++)
            {
                result.Add(scored[i].Value);
            }

            return result;
        }

        /// <summary>按昵称或 UserId 模糊查找一个被跟踪的玩家统计。</summary>
        public PlayerStats FindStats(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            // 精确匹配优先
            foreach (KeyValuePair<string, PlayerStats> kv in this.statsByUserId)
            {
                if (string.Equals(kv.Key, query, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kv.Value.Nickname, query, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }

            // 再做包含匹配
            foreach (KeyValuePair<string, PlayerStats> kv in this.statsByUserId)
            {
                if (kv.Value.Nickname != null && kv.Value.Nickname.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kv.Value;
                }
            }

            return null;
        }

        /// <summary>设置/取消跟踪。</summary>
        public bool SetFollow(string query, bool on, out PlayerStats found)
        {
            found = this.FindStats(query);
            if (found == null)
            {
                return false;
            }

            found.Followed = on;
            found.LogEvent(on ? "开始跟踪" : "停止跟踪", "由管理员操作");
            return true;
        }

        /// <summary>当前跟踪人数。</summary>
        public int TrackedCount => this.statsByUserId.Count;

        /// <summary>本局已用的 AI 调用数。</summary>
        public int ApiCallsThisRound => this.apiCallsThisRound;

        /// <summary>逐个检测器的当前战果（给 dsac report 用）。</summary>
        public string DescribeDetectors()
        {
            int noclip = 0, move = 0, inv = 0, ammo = 0, heal = 0, item = 0, react = 0, hard = 0, ai = 0;
            var names = new List<string>();

            foreach (KeyValuePair<string, PlayerStats> kv in this.statsByUserId)
            {
                PlayerStats s = kv.Value;
                if (s.NoclipActivations > 0) { noclip++; names.Add(s.Nickname + "(NoClip)"); }
                if (s.MoveViolationCount > 0) { move++; names.Add(s.Nickname + "(移动)"); }
                if (s.InvisibleTargetHits > 0) { inv++; }
                if (s.MaxShotsWithoutReload > this.Config.MaxReasonableShotsPerMagazine) { ammo++; }
                if (s.AbnormalHealCount > 0) { heal++; }
                if (s.SuspiciousItemCount > 0) { item++; }
                if (s.FastReactionCount > 0) { react++; }

                Heuristics.Result r = Heuristics.Evaluate(s, this.Config);
                if (r.HasHardViolation) { hard++; }
                if (r.Score >= this.Config.LocalScoreToReport) { ai++; }
            }

            var sb = new StringBuilder();
            sb.AppendLine("检测器战果（本局累计）");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "NoClip（状态位）", noclip > 0 ? "✅ " + noclip + " 人" : "— 未触发");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "移动异常（位置采样）", move > 0 ? "✅ " + move + " 人" : "— 未触发");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "命中隐身目标", inv > 0 ? "✅ " + inv + " 人" : "— 未触发");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "无限弹药", ammo > 0 ? "✅ " + ammo + " 人" : "— 未触发");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "异常回血", heal > 0 ? "✅ " + heal + " 人" : "— 未触发");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "可疑物品", item > 0 ? "✅ " + item + " 人" : "— 未触发");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "反应时间", react > 0 ? "✅ " + react + " 人" : "— 未触发");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "  硬违规玩家: {0}\n", hard);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  达到送审阈值: {0}\n", ai);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  本局 AI 调用: {0}/{1}\n", this.apiCallsThisRound, this.Config.MaxApiCallsPerRound);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  跟踪总人数: {0}\n", this.statsByUserId.Count);

            if (names.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("触发明细: " + string.Join("、", names));
            }

            if (noclip == 0 && move == 0)
            {
                sb.AppendLine();
                sb.AppendLine("提示：NoClip 和移动检测都没触发过。");
                sb.AppendLine("      可以用 dsac drill noclip / dsac drill teleport 真实制造条件验证。");
            }

            return sb.ToString();
        }

        /// <summary>累犯封禁台账（给 dsac bans 用）。</summary>
        internal BanLedger Ledger => this.ledger;

        /// <summary>向全服所有人公告这次处置。</summary>
        private void AnnouncePunishment(Player target, string action, int suspicion, string reason)
        {
            if (!this.Config.AnnouncePunishmentToAll)
            {
                return;
            }

            string text;
            try
            {
                text = this.Config.PunishmentAnnouncement
                    .Replace("{player}", target != null ? target.Nickname : "?")
                    .Replace("{action}", action ?? "处置")
                    .Replace("{score}", suspicion.ToString(CultureInfo.InvariantCulture))
                    .Replace("{reason}", Truncate(reason, 120))
                    .Replace("\\n", "\n");
            }
            catch
            {
                return;
            }

            ushort dur = this.Config.PunishmentAnnouncementDuration;
            if (dur < 3)
            {
                dur = 10;
            }

            foreach (Player p in Player.List)
            {
                try
                {
                    if (p != null && p.IsConnected)
                    {
                        p.Broadcast(dur, text, shouldClearPrevious: false);
                    }
                }
                catch
                {
                    // 单个失败不影响其他人
                }
            }

            Log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] 已全服公告处置: {1} / {2}",
                AwaWatermark.Owner, target != null ? target.Nickname : "?", action));
        }

        /// <summary>截断长文本，避免公告被刷屏。</summary>
        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        /// <summary>
        /// 当场处死 + 嘲讽。
        ///
        /// 只在高置信判定（AI 嫌疑度 ≥ 阈值）后执行 —— 不会误伤。
        /// 嘲讽文案在配置里，支持 {score} 和 {reason} 占位符。
        /// </summary>
        private void ApplyKill(Player target, string reason, DeepSeekVerdict verdict, string head)
        {
            try
            {
                Log.Warn(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} —— 执行处死: {1}", head, target.Nickname));

                // 先发嘲讽，再处死 —— 处死后 hint 会被清掉
                this.SendTaunt(target, verdict);

                // 全服公告
                this.AnnouncePunishment(
                    target, "处死",
                    verdict != null ? verdict.Suspicion : 0,
                    verdict != null ? verdict.Reasoning : reason);

                target.Kill(reason, null);
            }
            catch (Exception e)
            {
                Log.Error("[AWA] 执行处死时出错: " + e);
            }
        }

        /// <summary>给作弊者显示嘲讽（配置里可改，留空则不显示）。</summary>
        private void SendTaunt(Player target, DeepSeekVerdict verdict)
        {
            string tpl = this.Config.TauntMessage;
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return;
            }

            try
            {
                string text = tpl
                    .Replace("{score}", verdict != null ? verdict.Suspicion.ToString(CultureInfo.InvariantCulture) : "?")
                    .Replace("{reason}", verdict != null ? (verdict.Reasoning ?? string.Empty) : string.Empty)
                    .Replace("\\n", "\n");

                target.ShowHint(text, Math.Max(2f, this.Config.TauntDuration));

                // 同时发一条广播，保证他一定看得见（hint 有时会被别的东西顶掉）
                ushort dur = (ushort)Math.Min(65535, Math.Max(2, (int)this.Config.TauntDuration));
                target.Broadcast(dur, text, shouldClearPrevious: true);
            }
            catch (Exception e)
            {
                Log.Debug("[AWA] 发送嘲讽失败: " + e.Message);
            }
        }

        /// <summary>
        /// 执行阶梯处置。
        ///
        /// 阶梯的每一项可以是「处死」也可以是「封禁 N 天」，按这个人的历史触犯次数取档：
        ///   公共版默认 kill → kill → 3天 → 7天 → 90天 → 365天 → 永久
        /// 自用版默认直接 Action=Kill，不走这里。
        /// </summary>
        private void ApplyBan(Player target, string reason, string head, DeepSeekVerdict verdict)
        {
            try
            {
                if (!this.Config.UseProgressiveBans)
                {
                    int days = Math.Max(1, this.Config.BanDurationDays);
                    Log.Warn(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} —— 执行封禁 {1} 天（递进处置已关闭）: {2}", head, days, target.Nickname));
                    this.AnnouncePunishment(target, "封禁 " + days + " 天",
                        verdict != null ? verdict.Suspicion : 0, reason);
                    target.Ban(TimeSpan.FromDays(days), reason, Server.Host);
                    return;
                }

                Punishment p = this.ledger.NextPunishment(
                    target.UserId, target.Nickname, reason, this.Config.PunishmentLadder, out int offense);

                Log.Warn(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} —— 第 {1} 次触犯，执行 [{2}]: {3}",
                    head, offense, p.Label, target.Nickname));

                this.AnnouncePunishment(target, p.Label,
                    verdict != null ? verdict.Suspicion : 0, reason);

                if (p.IsKill)
                {
                    // 前几档只处死 —— 给他机会改正，也给管理员反应时间
                    this.SendTaunt(target, verdict);
                    target.Kill(reason, null);
                    return;
                }

                if (p.Days <= 0)
                {
                    target.Ban(TimeSpan.FromDays(365 * 100), reason, Server.Host);
                }
                else
                {
                    target.Ban(p.Duration, reason, Server.Host);
                }

                // 管理员通报里带上完整阶梯，方便核对
                string ladder = Punishment.DescribeLadder(this.Config.PunishmentLadder);
                foreach (Player admin in Player.List)
                {
                    try
                    {
                        if (admin != null && admin.IsConnected && admin.RemoteAdminAccess)
                        {
                            admin.Broadcast(6, string.Format(
                                CultureInfo.InvariantCulture,
                                "<color=#4da6ff>[AWA 反作弊]</color> 已处置 <b>{0}</b>（第 {1} 次 · {2}）\n<size=20><color=#888888>阶梯: {3}</color></size>",
                                target.Nickname, offense, p.Label, ladder), shouldClearPrevious: false);
                        }
                    }
                    catch
                    {
                        // 单个失败不影响处置
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[AWA] 执行处置时出错: " + e);
            }
        }
        /// <summary>把证据落盘（判定成立时调用）。</summary>
        public string ExportEvidence(PlayerStats st, DeepSeekVerdict verdict, Heuristics.Result local) =>
            AdminTools.ExportEvidence(st, verdict, local, this.Config);

        /// <summary>
        /// 面板刷新协程。只对打开了面板的管理员显示，
        /// 用 ShowHint 而不是 Broadcast —— 面板要常驻，Broadcast 会挡住视线中央。
        /// </summary>
        private IEnumerator<float> RefreshPanel()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(Math.Max(0.5f, this.Config.PanelRefreshSeconds));

                if (this.panelAdmins.Count == 0 || !this.Config.IsEnabled)
                {
                    continue;
                }

                string text;
                try
                {
                    List<PlayerStats> top = this.GetTopSuspicious(this.Config.PanelTopCount);
                    text = AdminTools.RenderPanel(top, this.Config, this.statsByUserId.Count, this.apiCallsThisRound);
                }
                catch (Exception e)
                {
                    Log.Debug("[DeepSeekAntiCheat] 渲染面板出错: " + e.Message);
                    continue;
                }

                foreach (Player p in Player.List)
                {
                    if (p == null || !p.IsConnected || p.IsNPC)
                    {
                        continue;
                    }

                    string id = p.UserId ?? p.Nickname;
                    if (!this.panelAdmins.Contains(id))
                    {
                        continue;
                    }

                    try
                    {
                        p.ShowHint(text, Math.Max(1f, this.Config.PanelRefreshSeconds + 1f));
                    }
                    catch
                    {
                        // 玩家刚断线时可能抛异常，忽略
                    }
                }
            }

            // ReSharper disable once IteratorNeverReturns
        }

        /// <summary>列出当前跟踪的玩家统计（给 dsac stats 用）。</summary>
        public string DescribeTrackedPlayers()
        {
            if (this.statsByUserId.Count == 0)
            {
                return "当前没有跟踪任何玩家（回合刚开始，或还没人开枪）。";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("当前跟踪的玩家：");
            sb.AppendLine();
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                " {0,-18} {1,6} {2,7} {3,7} {4,6} {5,6} {6,6}",
                "玩家", "命中", "命中率", "爆头率", "击杀", "杀/分", "评分"));
            sb.AppendLine(" " + new string('-', 74));

            foreach (KeyValuePair<string, PlayerStats> kv in this.statsByUserId)
            {
                PlayerStats s = kv.Value;
                Heuristics.Result local = Heuristics.Evaluate(s, this.Config);
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    " {0,-18} {1,6} {2,7:P0} {3,7:P0} {4,6} {5,6:F1} {6,6:F1}",
                    Trunc(s.Nickname, 18), s.ShotsHit, s.Accuracy, s.HeadshotRatio, s.Kills, s.KillsPerMinute, local.Score));
            }

            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "本局已调用 AI: {0} / {1}\n", this.apiCallsThisRound, this.Config.MaxApiCallsPerRound);
            return sb.ToString();
        }

        /// <summary>
        /// 把一个模拟场景送去 AI 复核。
        /// UserId 传 null —— 处置环节按 UserId 找不到玩家，
        /// 所以模拟永远不可能触发踢出/封禁。
        /// </summary>
        public void EnqueueSimulationReview(Scenarios.Scenario sc, PlayerStats stats, Heuristics.Result local, string requestedBy)
        {
            if (this.client == null || !this.client.IsConfigured)
            {
                return;
            }

            string report = Heuristics.BuildReportJson(stats, this.Config, local);
            string nickname = "[模拟] " + sc.Name;
            float score = local.Score;
            string[] hard = local.HardViolations.ToArray();

            Task.Run(async () =>
            {
                DeepSeekVerdict verdict = await this.client.ReviewAsync(report).ConfigureAwait(false);
                this.outcomes.Enqueue(new ReviewOutcome
                {
                    UserId = null,
                    Nickname = nickname,
                    LocalScore = score,
                    HardViolations = hard,
                    Verdict = verdict,
                    IsSimulation = true,
                    RequestedBy = requestedBy,
                });
            });
        }

        private static string Trunc(string s, int n) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        // ────────────────────────── 生命周期 ──────────────────────────

        /// <inheritdoc/>
        public override void OnEnabled()
        {
            Instance = this;

            // ── AWA 水印：启动横幅 + 完整性自检 ──
            Log.Info(AwaWatermark.Banner);

            // 启动时把「生效的处置方式」打出来 —— 免得改错配置自己不知道
            Log.Warn(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] {1}    生效处置: {2}    递进处置: {3}",
                AwaWatermark.Owner,
                AwaWatermark.Title,
                this.Config.Action,
                this.Config.UseProgressiveBans
                    ? Punishment.DescribeLadder(this.Config.PunishmentLadder)
                    : "关闭（固定封 " + this.Config.BanDurationDays + " 天）"));

            string tamper = AwaWatermark.Verify();
            if (tamper != null)
            {
                Log.Error("[" + AwaWatermark.Owner + "] 检测到水印被篡改：" + tamper);
                Log.Error("[" + AwaWatermark.Owner + "] 本插件的原始作者是 " +
                          AwaWatermark.ExpectedOwner + "。");
            }
            else
            {
                Log.Info(AwaWatermark.OneLine);

                // 顺便核对程序集里嵌入的元数据水印
                string meta = AwaWatermark.ReadAssemblyMetadata("AWA-Watermark");
                if (!string.IsNullOrEmpty(meta))
                {
                    Log.Info("[" + AwaWatermark.Owner + "] 程序集元数据水印: " + meta);
                }
            }

            this.ledger.Load();
            this.client = new DeepSeekClient(this.Config);

            PlayerEvents.Shot += this.OnShot;
            PlayerEvents.Died += this.OnDied;
            PlayerEvents.Left += this.OnLeft;
            PlayerEvents.Verified += this.OnVerified;
            ServerEvents.RoundEnded += this.OnRoundEnded;

            // 新增检测器的事件源
            PlayerEvents.TogglingNoClip += this.OnTogglingNoClip;
            PlayerEvents.Healing += this.OnHealing;
            PlayerEvents.ReloadedWeapon += this.OnReloadedWeapon;
            PlayerEvents.ItemAdded += this.OnItemAdded;
            PlayerEvents.Spawned += this.OnSpawned;
            PlayerEvents.RoomChanged += this.OnRoomChanged;

            // ── 命令注册验证 ──
            // 注册交给 EXILED，这里只做延迟验证 + 兜底（详见方法注释）。
            this.RegisterCommandsExplicitly();

            this.pump = Timing.RunCoroutine(this.PumpOutcomes(), Segment.Update);
            this.moveWatch = Timing.RunCoroutine(this.SampleMovement(), Segment.Update);
            this.panelWatch = Timing.RunCoroutine(this.RefreshPanel(), Segment.Update);

            if (!this.client.IsConfigured)
            {
                Log.Warn("[DeepSeekAntiCheat] 未配置 ApiKey —— 插件只做本地启发式评分，不会调用 DeepSeek。");
            }

            base.OnEnabled();
        }

        /// <inheritdoc/>
        public override void OnDisabled()
        {
            PlayerEvents.Shot -= this.OnShot;
            PlayerEvents.Died -= this.OnDied;
            PlayerEvents.Left -= this.OnLeft;
            PlayerEvents.Verified -= this.OnVerified;
            ServerEvents.RoundEnded -= this.OnRoundEnded;

            PlayerEvents.TogglingNoClip -= this.OnTogglingNoClip;
            PlayerEvents.Healing -= this.OnHealing;
            PlayerEvents.ReloadedWeapon -= this.OnReloadedWeapon;
            PlayerEvents.ItemAdded -= this.OnItemAdded;
            PlayerEvents.Spawned -= this.OnSpawned;
            PlayerEvents.RoomChanged -= this.OnRoomChanged;

            this.UnregisterCommandsExplicitly();

            Timing.KillCoroutines(this.pump);
            Timing.KillCoroutines(this.moveWatch);
            Timing.KillCoroutines(this.panelWatch);
            this.panelAdmins.Clear();

            this.client?.Dispose();
            this.client = null;

            this.statsByUserId.Clear();
            while (this.outcomes.TryDequeue(out _))
            {
            }

            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }

            base.OnDisabled();
        }

        /// <inheritdoc/>
        public override void OnReloaded()
        {
            // 配置重载后必须重建客户端。
            // Plugin<TConfig>.Config 只有 getter，EXILED 重载时是「更新已有实例」，
            // 所以不重建的话，改完 api_key 重载配置不会生效，必须重启服务端。
            this.client?.Dispose();
            this.ledger.Load();
            this.client = new DeepSeekClient(this.Config);

            if (!this.client.IsConfigured)
            {
                Log.Warn("[DeepSeekAntiCheat] 未配置 ApiKey —— 插件只做本地启发式评分，不会调用 DeepSeek。");
            }
            else
            {
                Log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "[DeepSeekAntiCheat] 配置已重载，AI 端点 {0}，模型 {1}。",
                    this.Config.ApiBaseUrl, this.Config.Model));
            }

            base.OnReloaded();
        }

        // ────────────────────────── 事件处理 ──────────────────────────

        private void OnVerified(VerifiedEventArgs ev)
        {
            if (ev?.Player == null)
            {
                return;
            }

            PlayerStats st = this.GetStats(ev.Player);
            st.FirstSeen = DateTime.UtcNow;
            st.LastApiCheck = DateTime.MinValue;
            st.ApiCallsThisRound = 0;
            st.AlreadyFlagged = false;
        }

        private void OnLeft(LeftEventArgs ev)
        {
            if (ev?.Player == null)
            {
                return;
            }

            // 保留统计（可能重连），但清冷却，避免重连后立刻又能刷 API
            if (this.statsByUserId.TryGetValue(ev.Player.UserId, out PlayerStats st))
            {
                st.LastApiCheck = DateTime.UtcNow;
            }
        }

        private void OnShot(ShotEventArgs ev)
        {
            if (ev?.Player == null || !this.Config.IsEnabled)
            {
                return;
            }

            // NPC / 本地玩家不算
            if (ev.Player.IsNPC || ev.Player.IsHost)
            {
                return;
            }

            PlayerStats st = this.GetStats(ev.Player);
            st.RecordShot();

            // ── 武器与投射物一致性 ──
            // 拿机枪却打出榴弹的伤害 —— 比的是「武器自己声明的伤害」，
            // 所以服务器插件改过武器也不会误报（见 WeaponCheck.cs）。
            if (this.Config.DetectAmmoMismatch && ev.Target != null)
            {
                try
                {
                    WeaponCheck.Result wc = WeaponCheck.Check(this.Config, ev.Player, ev.Damage);
                    if (wc.Suspicious)
                    {
                        st.AmmoMismatchCount++;
                        st.AmmoMismatchDetail = wc.Detail;

                        st.LogEvent("武器异常", wc.Detail);
                        Log.Warn(string.Format(
                            CultureInfo.InvariantCulture,
                            "[{0}] {1} —— {2}",
                            AwaWatermark.Owner, ev.Player.Nickname, wc.Detail));

                        this.MaybeReview(st);
                    }
                }
                catch (Exception e)
                {
                    Log.Debug("[AWA] 武器一致性检查失败: " + e.Message);
                }
            }

            // Target 为 null 表示没打中人
            if (ev.Target != null)
            {
                st.ShotsHit++;
                st.TotalDamageDealt += ev.Damage;

                if (ev.Damage > st.MaxSingleHitDamage)
                {
                    st.MaxSingleHitDamage = ev.Damage;
                }

                st.LogEvent("命中", string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}  伤害 {1:F0}  距离 {2:F1} 米{3}",
                    ev.Target.Nickname, ev.Damage, ev.Distance,
                    IsHeadshot(ev) ? "  爆头" : string.Empty));

                if (ev.Distance > st.LongestHitDistance)
                {
                    st.LongestHitDistance = ev.Distance;
                }

                this.CheckReactionTime(ev.Player, ev.Target, st);

                if (IsHeadshot(ev))
                {
                    st.Headshots++;
                    if (ev.Distance >= this.Config.LongRangeHeadshotDistance)
                    {
                        st.LongRangeHeadshots++;
                    }
                }
            }

            this.MaybeReview(st);
        }

        private void OnDied(DiedEventArgs ev)
        {
            if (ev?.Player == null || !this.Config.IsEnabled)
            {
                return;
            }

            // 死者记一笔
            PlayerStats victim = this.GetStats(ev.Player);
            victim.Deaths++;

            // 击杀归属：Attacker 可能为 null（自杀/环境伤害）
            Player attacker = ev.Attacker;
            if (attacker == null || attacker == ev.Player || attacker.IsNPC || attacker.IsHost)
            {
                return;
            }

            PlayerStats killer = this.GetStats(attacker);
            killer.Kills++;
            killer.LogEvent("击杀", string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}（{1}）",
                ev.Player.Nickname,
                ev.Player.Role != null ? ev.Player.Role.Type.ToString() : "未知角色"));
            if (!killer.FirstKillAt.HasValue)
            {
                killer.FirstKillAt = DateTime.UtcNow;
            }

            this.MaybeReview(killer);
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            foreach (KeyValuePair<string, PlayerStats> kv in this.statsByUserId)
            {
                kv.Value.ResetForNewRound();
            }

            this.apiCallsThisRound = 0;

            while (this.outcomes.TryDequeue(out _))
            {
            }

            if (this.Config.Debug)
            {
                Log.Debug("[DeepSeekAntiCheat] 回合结束，统计已重置。");
            }
        }

        // ────────────────────────── 复核调度 ──────────────────────────

        // AWA :: 触发 AI 复核的判定逻辑。
        // 这里的每一次调用都意味着一次真实的 API 计费，改动前请先读懂限流预算。
        private void MaybeReview(PlayerStats st)
        {
            // ── 白名单：这些玩家/角色完全不参与检测 ──
            if (st == null)
            {
                return;
            }

            ServerFeatures f = this.Config.Features;
            if (f != null)
            {
                if (f.IsWhitelisted(st.UserId))
                {
                    return;
                }

                try
                {
                    Player p0 = FindPlayer(st.UserId);
                    if (p0 != null && f.IsRoleWhitelisted(p0.Role.Type.ToString()))
                    {
                        return;
                    }
                }
                catch
                {
                    // 角色读不到就算了
                }
            }
            if (st == null || this.client == null || !this.client.IsConfigured || !this.Config.IsEnabled)
            {
                return;
            }

            // 样本门槛只约束「比率类」信号（爆头率/命中率）—— 样本少时比率不可信。
            // 但硬违规（NoClip 状态位、物理不可能的速度/伤害/射速）与打了多少发无关，
            // 必须绕过这个门槛，否则一个刚开挂的新号会被样本数挡下来。
            Heuristics.Result preCheck = Heuristics.Evaluate(st, this.Config);
            if (st.ShotsHit < this.Config.MinSamples && !preCheck.HasHardViolation)
            {
                return;
            }

            if (st.ApiCallsThisRound >= this.Config.MaxApiCallsPerPlayerPerRound)
            {
                return;
            }

            if (this.apiCallsThisRound >= this.Config.MaxApiCallsPerRound)
            {
                return;
            }

            if ((DateTime.UtcNow - st.LastApiCheck).TotalSeconds < this.Config.CooldownSeconds)
            {
                return;
            }

            // 累积分够高，或存在「物理上不可能」的硬违规 —— 后者单独一条就足以送审
            Heuristics.Result local = preCheck;
            if (!local.ShouldReport(this.Config.LocalScoreToReport))
            {
                return;
            }

            // 占用预算（先占再发，避免并发重复）
            st.LastApiCheck = DateTime.UtcNow;
            st.ApiCallsThisRound++;
            this.apiCallsThisRound++;

            string report = Heuristics.BuildReportJson(st, this.Config, local);
            string userId = st.UserId;
            string nickname = st.Nickname;
            float localScore = local.Score;
            string[] hardViolations = local.HardViolations.ToArray();

            if (this.Config.Debug)
            {
                Log.Debug(string.Format(
                    CultureInfo.InvariantCulture,
                    "[DeepSeekAntiCheat] {0} 本地评分 {1:F1}{2}，送去复核。理由: {3}",
                    nickname, localScore, hardViolations.Length > 0 ? "（含硬违规）" : string.Empty, local));
            }

            // 后台线程只做 HTTP，不碰任何 Unity 对象
            Task.Run(async () =>
            {
                DeepSeekVerdict verdict = await this.client.ReviewAsync(report).ConfigureAwait(false);
                this.outcomes.Enqueue(new ReviewOutcome
                {
                    UserId = userId,
                    Nickname = nickname,
                    LocalScore = localScore,
                    HardViolations = hardViolations,
                    Local = local,
                    Verdict = verdict,
                });
            });
        }

        /// <summary>主线程协程：把后台线程的判定结果取出来执行。</summary>
        private IEnumerator<float> PumpOutcomes()
        {
            while (true)
            {
                while (this.outcomes.TryDequeue(out ReviewOutcome outcome))
                {
                    try
                    {
                        this.HandleOutcome(outcome);
                    }
                    catch (Exception e)
                    {
                        Log.Error("[DeepSeekAntiCheat] 处理判定结果时出错: " + e);
                    }
                }

                yield return Timing.WaitForSeconds(1f);
            }

            // ReSharper disable once IteratorNeverReturns
        }

        // ────────────────────────── 新增检测器 ──────────────────────────

        /// <summary>
        /// 位置采样协程。移动类检测的数据源。
        ///
        /// 位置是服务端权威数据，所以速度/瞬移/飞天这几项可靠性高；
        /// 武器机制造成的位移（SCP-106 传送、SCP-096 冲刺）在 MovementWatch 里已排除。
        /// </summary>
        private IEnumerator<float> SampleMovement()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(Math.Max(0.1f, this.Config.MovementSampleInterval));

                if (!this.Config.IsEnabled || !this.Config.EnableMovementDetection)
                {
                    continue;
                }

                List<MovementWatch.Violation> found;
                try
                {
                    ServerFeatures fm = this.Config.Features;

                    // 特性倍率：开了加速/无限体力的服，把上限放宽而不是完全关掉
                    float speedMul = (fm != null && fm.SpeedToleranceMultiplier > 0f) ? fm.SpeedToleranceMultiplier : 1f;
                    float vertMul = (fm != null && fm.VerticalToleranceMultiplier > 0f) ? fm.VerticalToleranceMultiplier : 1f;

                    float maxSpeed = this.Config.MaxReasonableSpeed * speedMul;
                    float maxVert = this.Config.MaxReasonableVerticalSpeed * vertMul;

                    // 过场飞行：直接给一个「不可能达到」的上限，等于关掉垂直检测
                    if (fm != null && fm.CutsceneFlight)
                    {
                        maxVert = float.MaxValue;
                    }

                    // 无限体力：同样给一个极高的上限
                    if (fm != null && fm.InfiniteStamina)
                    {
                        maxSpeed = float.MaxValue;
                    }

                    // ── 透视预判采样 ──
                    // 统计「准星是否总对着他看不见的敌人」。
                    // 服务端看不到渲染，这是间接推断；靠次数门槛过滤运气与预瞄。
                    if (this.Config.DetectEspPrediction)
                    {
                        foreach (Player ap in Player.List)
                        {
                            if (ap == null || !ap.IsConnected || ap.IsNPC || ap.IsHost)
                            {
                                continue;
                            }

                            try
                            {
                                PlayerStats ast = this.GetStats(ap);

                                if (AimWatch.Sample(this.Config, ap, Player.List))
                                {
                                    int inWin = ast.RecordPrediction(this.Config.EspPredictionWindowSeconds);
                                    ast.PredictionStreak++;
                                    if (ast.PredictionStreak > ast.MaxPredictionStreak)
                                    {
                                        ast.MaxPredictionStreak = ast.PredictionStreak;
                                    }

                                    ast.PredictionDetail = string.Format(
                                        CultureInfo.InvariantCulture,
                                        "{0} 秒内 {1} 次（最长连续 {2} 次）",
                                        this.Config.EspPredictionWindowSeconds,
                                        inWin, ast.MaxPredictionStreak);

                                    if (inWin == this.Config.EspPredictionThreshold)
                                    {
                                        ast.LogEvent("透视预判", ast.PredictionDetail);
                                        Log.Warn(string.Format(
                                            CultureInfo.InvariantCulture,
                                            "[{0}] 预判异常: {1} —— {2}",
                                            AwaWatermark.Owner, ap.Nickname, ast.PredictionDetail));
                                    }

                                    if (inWin >= this.Config.EspPredictionHard)
                                    {
                                        this.MaybeReview(ast);
                                    }
                                }
                                else
                                {
                                    ast.BreakPredictionStreak();
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Debug("[AWA] 透视预判采样失败: " + e.Message);
                            }
                        }
                    }

                    float teleportDist = (fm != null && fm.CustomTeleport) ? float.MaxValue : this.Config.TeleportDistance;

                    found = this.movement.Tick(
                        this.Config,
                        maxSpeed,
                        teleportDist,
                        maxVert,
                        this.Config.MaxReasonableYawSpeed,
                        this.Config.SpinStreakNeeded,
                        this.Config.JitterYawSpeed,
                        this.Config.JitterStreakNeeded);
                }
                catch (Exception e)
                {
                    Log.Debug("[DeepSeekAntiCheat] 移动采样出错: " + e.Message);
                    continue;
                }

                if (found.Count == 0)
                {
                    continue;
                }

                // MovementWatch 直接把玩家带回来了，不用猜归属
                foreach (MovementWatch.Violation v in found)
                {
                    if (v.Player == null)
                    {
                        continue;
                    }

                    PlayerStats st = this.GetStats(v.Player);
                    st.AddMoveViolation(v.Detail);
                    st.LogEvent("移动异常", v.Detail);

                    if (v.Kind == "speed")
                    {
                        st.MaxHorizontalSpeed = Math.Max(st.MaxHorizontalSpeed, this.Config.MaxReasonableSpeed + 1f);
                    }
                    else if (v.Kind == "vertical")
                    {
                        st.MaxVerticalSpeed = Math.Max(st.MaxVerticalSpeed, this.Config.MaxReasonableVerticalSpeed + 1f);
                    }
                    else if (v.Kind == "spin")
                    {
                        st.SpinCount++;
                        st.MaxYawSpeed = Math.Max(st.MaxYawSpeed, this.Config.MaxReasonableYawSpeed + 1f);
                    }
                    else if (v.Kind == "jitter")
                    {
                        st.JitterCount++;
                        st.MaxYawSpeed = Math.Max(st.MaxYawSpeed, this.Config.JitterYawSpeed + 1f);
                    }

                    this.MaybeReview(st);
                }
            }

            // ReSharper disable once IteratorNeverReturns
        }

        /// <summary>NoClip —— 服务端直接可读的状态位。</summary>
        private void OnTogglingNoClip(TogglingNoClipEventArgs ev)
        {
            if (ev?.Player == null || !this.Config.DetectNoclip)
            {
                return;
            }

            PlayerStats st = this.GetStats(ev.Player);

            // 服务器特性：允许管理员用 NoClip
            ServerFeatures feat = this.Config.Features;
            if (feat != null && feat.NoclipForAdmins)
            {
                try
                {
                    if (ev.Player.RemoteAdminAccess)
                    {
                        return;
                    }
                }
                catch
                {
                    // 读不到权限就当普通玩家
                }
            }

            st.NoclipActivations++;
            st.NoclipContext = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "房间 {0}，位置 ({1:F0}, {2:F0}, {3:F0})",
                ev.Player.CurrentRoom?.Name ?? "未知",
                ev.Player.Position.x, ev.Player.Position.y, ev.Player.Position.z);

            st.LogEvent("NoClip", st.NoclipContext);

            Log.Warn(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[DeepSeekAntiCheat-AWA] 检测到 NoClip: {0} —— {1}",
                ev.Player.Nickname, st.NoclipContext));

            this.MaybeReview(st);
        }

        /// <summary>异常治疗。</summary>
        private void OnHealing(HealingEventArgs ev)
        {
            if (ev?.Player == null)
            {
                return;
            }

            float amount = ev.Amount;
            PlayerStats st = this.GetStats(ev.Player);
            if (amount > st.MaxSingleHeal)
            {
                st.MaxSingleHeal = amount;
            }

            // 服务器特性：异常治疗类技能/道具
            if (this.Config.Features != null && this.Config.Features.AbnormalHeal)
            {
                return;
            }

            if (amount > this.Config.MaxReasonableHeal)
            {
                st.AbnormalHealCount++;
                st.LogEvent("异常治疗", string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "单次治疗 {0:F0}（上限 {1:F0}）", amount, this.Config.MaxReasonableHeal));
                this.MaybeReview(st);
            }
        }

        /// <summary>换弹 —— 重置「连打不换弹」计数。</summary>
        private void OnReloadedWeapon(ReloadedWeaponEventArgs ev)
        {
            if (ev?.Player == null)
            {
                return;
            }

            PlayerStats st = this.GetStats(ev.Player);
            if (st.ShotsSinceReload > st.MaxShotsWithoutReload)
            {
                st.MaxShotsWithoutReload = st.ShotsSinceReload;
            }

            st.ShotsSinceReload = 0;
        }

        /// <summary>拿到物品 —— 同一瞬间拿到多个物品算可疑。</summary>
        private void OnItemAdded(ItemAddedEventArgs ev)
        {
            if (ev?.Player == null)
            {
                return;
            }

            // NPC / 本地玩家不算
            if (ev.Player.IsNPC || ev.Player.IsHost)
            {
                return;
            }

            PlayerStats st = this.GetStats(ev.Player);

            // ── 与服务器插件共存：这些物品不算刷物品，也不封堵 ──
            // 很多服务器插件会把钥匙卡/弹药/护甲/医疗品当补给发出去。
            string curItem = string.Empty;
            try
            {
                if (ev.Item != null)
                {
                    curItem = ev.Item.Type.ToString();
                }
            }
            catch
            {
                // 拿不到名字就当普通物品
            }

            bool ignored = this.IsItemSpamIgnored(curItem);

            // 出生后宽限期内不计（服务器插件常在出生瞬间发整套装备）
            bool inGrace = false;
            if (this.Config.ItemSpamSpawnGraceSeconds > 0f && st.SpawnedAt != DateTime.MinValue)
            {
                inGrace = (DateTime.UtcNow - st.SpawnedAt).TotalSeconds < this.Config.ItemSpamSpawnGraceSeconds;
            }

            // ── 封堵窗口：刚判定过刷物品，接下来一段时间里新拿到的物品直接收掉 ──
            // 否则「清空背包」只清那一瞬间，作弊者接着刷还是能拿到东西。
            if (this.Config.BlockItemsAfterSpam && DateTime.UtcNow < st.ItemSpamBlockedUntil)
            {
                // 豁免物品照常放行（服务器插件的补给不该被收）
                if (!ignored)
                {
                    this.BlockItem(ev.Player, ev.Item, st);
                    return;
                }
            }

            // 出生宽限期 / 豁免物品 —— 照常记录，但不参与刷物品判定
            if (ignored || inGrace)
            {
                st.RecordItem(this.Config.ItemSpamWindowSeconds);
                return;
            }

            // 按「滑动窗口内拿了多少件」判断，而不是累计总数 ——
            // 累计总数对正常玩家也会慢慢涨上去，窗口计数才能识别刷物品。
            int inWindow = st.RecordItem(this.Config.ItemSpamWindowSeconds);

            string itemName = "物品";
            try
            {
                if (ev.Item != null)
                {
                    itemName = ev.Item.Type.ToString();
                }
            }
            catch
            {
                // 拿不到名字就算了
            }

            st.LogEvent("获得物品", string.Format(
                CultureInfo.InvariantCulture,
                "{0}（{1} 秒内第 {2} 件）", itemName, this.Config.ItemSpamWindowSeconds, inWindow));

            // 服务器特性：服务器本身会发物品 —— 完全关掉这项检测
            ServerFeatures fi = this.Config.Features;
            if (fi != null && fi.GivesItems)
            {
                return;
            }

            // 阈值倍率：服务器偶尔发物品时可以调大
            float mult = (fi != null && fi.ItemSpamToleranceMultiplier > 0f) ? fi.ItemSpamToleranceMultiplier : 1f;
            if (inWindow < this.Config.ItemSpamThreshold * mult)
            {
                return;
            }

            // ── 判定为刷物品 ──
            st.ItemSpamDetected = true;
            st.ItemSpamDetail = string.Format(
                CultureInfo.InvariantCulture,
                "{0} 秒内获得 {1} 件物品（阈值 {2}）",
                this.Config.ItemSpamWindowSeconds, inWindow, this.Config.ItemSpamThreshold);

            Log.Warn(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] 检测到刷物品: {1} —— {2}",
                AwaWatermark.Owner, ev.Player.Nickname, st.ItemSpamDetail));

            // 设定封堵窗口：接下来这段时间里新拿到的物品会被逐件收掉
            if (this.Config.BlockItemsAfterSpam)
            {
                st.ItemSpamBlockedUntil = DateTime.UtcNow.AddSeconds(Math.Max(1, this.Config.ItemSpamBlockSeconds));
            }

            // 直接清空背包（可逆处置，不走 AI、不等阈值）
            if (this.Config.ClearInventoryOnItemSpam)
            {
                this.ClearInventory(ev.Player, st.ItemSpamDetail);
            }

            // 同时送去 AI 复核，走完整处置链路
            this.MaybeReview(st);
        }

        /// <summary>清空玩家背包，并告诉他为什么。</summary>
        private void ClearInventory(Player target, string why)
        {
            try
            {
                int before = target.Items != null ? target.Items.Count : 0;
                target.ClearInventory(true);
                int after = target.Items != null ? target.Items.Count : 0;

                Log.Warn(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] 已清空 {1} 的背包（{2} -> {3} 件）: {4}",
                    AwaWatermark.Owner, target.Nickname, before, after, why));

                try
                {
                    target.ShowHint(
                        "<size=24><color=#4da6ff><b>背包已被清空</b></color></size>\n" +
                        "<size=18><color=#aaaaaa>" + why + "</color></size>",
                        8f);
                }
                catch
                {
                    // 提示失败不影响清空
                }
            }
            catch (Exception e)
            {
                Log.Error("[AWA] 清空背包失败: " + e.Message);
            }
        }

        /// <summary>
        /// 这个物品类型是不是「服务器插件常发的补给」。
        ///
        /// 命中就不计入刷物品、也不会被封堵 —— 避免和发物品的服务器插件冲突。
        /// 名单在配置里（item_spam_ignore_types），可以按自己的服调整。
        /// </summary>
        private bool IsItemSpamIgnored(string itemTypeName)
        {
            if (string.IsNullOrEmpty(itemTypeName))
            {
                return false;
            }

            List<string> list = this.Config.ItemSpamIgnoreTypes;
            if (list == null)
            {
                return false;
            }

            foreach (string key in list)
            {
                if (!string.IsNullOrWhiteSpace(key)
                    && itemTypeName.IndexOf(key.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 收掉一件刚拿到的物品（封堵窗口内用）。
        ///
        /// 事件本身不能撤销，所以是「先让它进来，再立刻拿走」。
        /// </summary>
        private void BlockItem(Player target, Exiled.API.Features.Items.Item item, PlayerStats st)
        {
            try
            {
                if (target == null || item == null)
                {
                    return;
                }

                target.RemoveItem(item, true);
                st.BlockedItems++;

                if (st.BlockedItems <= 3 || st.BlockedItems % 10 == 0)
                {
                    Log.Warn(string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}] 封堵物品: {1} 的第 {2} 件（刷物品封堵窗口内）",
                        AwaWatermark.Owner, target.Nickname, st.BlockedItems));
                }
            }
            catch (Exception e)
            {
                Log.Debug("[AWA] 封堵物品失败: " + e.Message);
            }
        }
        /// <summary>记录玩家进入房间的时刻 —— 反应时间检测的基准。</summary>
        private void OnRoomChanged(RoomChangedEventArgs ev)
        {
            if (ev?.Player == null)
            {
                return;
            }

            string id = ev.Player.UserId;
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            this.roomEnteredAt[id] = DateTime.UtcNow;
            this.roomName[id] = ev.NewRoom?.Name ?? "未知";
        }

        /// <summary>
        /// 反应时间检测。
        ///
        /// 原理：目标刚刚进入射手所在房间，射手就命中了他 —— 人类反应时间下限约 150-200ms，
        /// 明显低于这个值说明是程序在瞄。
        ///
        /// 可靠性：★★ 间接推断。同一房间内可能本来就能看到对方（比如走廊尽头），
        /// 所以只靠「短时间内多次」来降低误报。
        /// </summary>
        private void CheckReactionTime(Player shooter, Player target, PlayerStats st)
        {
            if (shooter == null || target == null)
            {
                return;
            }

            string sid = shooter.UserId;
            string tid = target.UserId;
            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(tid))
            {
                return;
            }

            // 必须同房间才算
            if (!this.roomName.TryGetValue(sid, out string sRoom)
                || !this.roomName.TryGetValue(tid, out string tRoom)
                || sRoom != tRoom)
            {
                return;
            }

            if (!this.roomEnteredAt.TryGetValue(tid, out DateTime entered))
            {
                return;
            }

            double ms = (DateTime.UtcNow - entered).TotalMilliseconds;

            // 低于人类反应下限
            if (ms < this.Config.HumanReactionFloorMs)
            {
                st.FastReactionCount++;
                st.FastestReactionMs = st.FastestReactionMs <= 0 ? ms : Math.Min(st.FastestReactionMs, ms);
                st.LogEvent("反应时间", string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "目标 {0} 进入房间后 {1:F0} 毫秒即被命中（人类下限 {2:F0}ms）",
                    target.Nickname, ms, this.Config.HumanReactionFloorMs));
            }
        }

        /// <summary>重生 —— 清掉位置采样，避免跨出生点算成瞬移。</summary>
        private void OnSpawned(SpawnedEventArgs ev)
        {
            if (ev?.Player != null)
            {
                this.movement.Forget(ev.Player.UserId);

                // 记下出生时刻：服务器插件常在出生瞬间发整套装备，
                // 那段时间内不计刷物品（见 ItemSpamSpawnGraceSeconds）。
                try
                {
                    PlayerStats sst = this.GetStats(ev.Player);
                    sst.SpawnedAt = DateTime.UtcNow;
                    sst.ItemSpamBlockedUntil = DateTime.MinValue;
                }
                catch
                {
                    // 记录失败不影响主流程
                }
            }
        }

        // AWA :: 主线程处置入口。（处置段）。
        // 模拟测试会在这里提前返回 —— 这是「测试永远不会踢人」的保证点。
        private void HandleOutcome(ReviewOutcome o)
        {
            if (o?.Verdict == null)
            {
                return;
            }

            string head = string.Format(
                CultureInfo.InvariantCulture,
                "[DeepSeekAntiCheat] 玩家 {0} 本地评分 {1:F1}",
                o.Nickname, o.LocalScore);

            if (!o.Verdict.Ok)
            {
                Log.Warn(head + " —— DeepSeek 复核失败: " + o.Verdict.Error);
                return;
            }

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "{0}；AI 嫌疑度 {1}%，理由: {2}（建议 {3}）",
                head, o.Verdict.Suspicion, o.Verdict.Reasoning, o.Verdict.RecommendedAction);

            if (o.HardViolations != null && o.HardViolations.Length > 0)
            {
                summary += " ⚠ 硬违规: " + string.Join(" | ", o.HardViolations);
            }

            if (this.Config.LogFullReport)
            {
                Log.Warn(summary);
            }

            // 判定成立的，把完整时间线落盘存证
            if (o.UserId != null && this.statsByUserId.TryGetValue(o.UserId, out PlayerStats evidenceTarget))
            {
                string path = AdminTools.ExportEvidence(evidenceTarget, o.Verdict, o.Local, this.Config);
                if (path != null)
                {
                    Log.Warn("[DeepSeekAntiCheat] 证据已落盘: " + path);
                }
            }

            if (o.UserId != null && this.statsByUserId.TryGetValue(o.UserId, out PlayerStats logged))
            {
                logged.LogEvent("AI判定", string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "嫌疑度 {0}%  建议 {1}  {2}",
                    o.Verdict.Suspicion, o.Verdict.RecommendedAction, o.Verdict.Reasoning));
            }

            if (this.Config.AnnounceToAdmins)
            {
                string extra = o.HardViolations != null && o.HardViolations.Length > 0
                    ? "\n<color=#ffaa00>⚠ 物理上不可能的读数：" + string.Join("；", o.HardViolations) + "</color>"
                    : string.Empty;

                string prefix = o.IsSimulation
                    ? "<color=#55aaff>[反作弊·测试]</color>（由 " + (o.RequestedBy ?? "console") + " 发起）"
                    : "<color=#ff5555>[反作弊]</color>";

                this.AnnounceToAdmins(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1} 嫌疑度 <b>{2}%</b>\n{3}{4}",
                    prefix, o.Nickname, o.Verdict.Suspicion, o.Verdict.Reasoning, extra));
            }

            // ── 安全阀：模拟测试到此为止，绝不进入处置流程 ──
            if (o.IsSimulation)
            {
                Log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "[DeepSeekAntiCheat] 模拟测试结束（{0}），已按设计跳过处置环节。",
                    o.Nickname));
                return;
            }

            // 有硬违规时用更低的门槛（物理上不可能的读数基本等同铁证）
            int threshold = o.HardViolations != null && o.HardViolations.Length > 0
                ? this.Config.HardViolationThreshold
                : this.Config.VerdictThreshold;

            if (o.Verdict.Suspicion < threshold)
            {
                return;
            }

            PlayerStats st = null;
            if (o.UserId != null)
            {
                this.statsByUserId.TryGetValue(o.UserId, out st);
            }

            if (st != null)
            {
                st.AlreadyFlagged = true;
                st.VerdictHistory.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:HH:mm:ss} suspect={1}% {2}",
                    DateTime.Now, o.Verdict.Suspicion, o.Verdict.Reasoning));
            }

            Player target = FindPlayer(o.UserId);
            if (target == null || !target.IsConnected)
            {
                Log.Warn(head + " —— 判定超过阈值，但玩家已离线，无法执行动作。");
                return;
            }

            string reason = string.Format(
                CultureInfo.InvariantCulture,
                "[DeepSeekAntiCheat] AI 判定作弊（嫌疑度 {0}%）: {1}",
                o.Verdict.Suspicion, o.Verdict.Reasoning);

            switch (this.Config.Action)
            {
                case VerdictAction.Kick:
                    Log.Warn(string.Format(CultureInfo.InvariantCulture, "{0} —— 执行踢出: {1}", head, target.Nickname));
                    target.Kick(reason, Server.Host);
                    break;

                case VerdictAction.Kill:
                    this.ApplyKill(target, reason, o.Verdict, head);
                    break;

                case VerdictAction.Ban:
                    this.ApplyBan(target, reason, head, o.Verdict);
                    break;

                default:
                    Log.Warn(head + " —— 当前为 Alert 模式，仅通报不处置。");
                    break;
            }
        }

        private void AnnounceToAdmins(string message)
        {
            foreach (Player p in Player.List)
            {
                if (p == null || !p.IsConnected || p.IsNPC)
                {
                    continue;
                }

                if (p.RemoteAdminAccess)
                {
                    p.Broadcast(8, message);
                }
            }
        }

        // ────────────────────────── 工具 ──────────────────────────

        private PlayerStats GetStats(Player player)
        {
            string id = player.UserId ?? player.Nickname ?? "unknown";
            if (!this.statsByUserId.TryGetValue(id, out PlayerStats st))
            {
                st = new PlayerStats
                {
                    UserId = id,
                    Nickname = player.Nickname,
                    FirstSeen = DateTime.UtcNow,
                };
                this.statsByUserId[id] = st;
            }
            else
            {
                st.Nickname = player.Nickname;
            }

            return st;
        }

        private static Player FindPlayer(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            foreach (Player p in Player.List)
            {
                if (p != null && p.UserId == userId)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>
        /// 是否爆头。用名字比较而不是写死枚举值，
        /// 这样游戏更新改动枚举也不至于编译不过。
        /// </summary>
        private static bool IsHeadshot(ShotEventArgs ev)
        {
            try
            {
                if (ev.Hitbox == null)
                {
                    return false;
                }

                string name = ev.Hitbox.HitboxType.ToString();
                return name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>后台线程投递到主线程的判定结果。</summary>
        private sealed class ReviewOutcome
        {
            public string UserId { get; set; }

            public string Nickname { get; set; }

            public float LocalScore { get; set; }

            /// <summary>物理上不可能的读数（若有）。</summary>
            public string[] HardViolations { get; set; }

            /// <summary>本地评分明细，证据落盘要用。</summary>
            public Heuristics.Result Local { get; set; }

            /// <summary>这是不是模拟测试（模拟永远不会触发处置）。</summary>
            public bool IsSimulation { get; set; }

            /// <summary>谁发起的测试。</summary>
            public string RequestedBy { get; set; }

            public DeepSeekVerdict Verdict { get; set; }
        }
    }
}
