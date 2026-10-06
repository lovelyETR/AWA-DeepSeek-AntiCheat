// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  本文件由 AWA 编写。
// ============================================================================
namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// 单个玩家在一局内的行为统计。只在主线程读写。
    /// </summary>
        // AWA :: 行为统计。
        // 只在主线程读写，不要跨线程碰它。
    public sealed class PlayerStats
    {
        /// <summary>玩家的 UserID（含 @steam / @discord 后缀）。</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>玩家昵称。</summary>
        public string Nickname { get; set; } = string.Empty;

        /// <summary>本局首次出现时间。</summary>
        public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

        /// <summary>最后一次开枪时间。</summary>
        public DateTime LastShotAt { get; set; } = DateTime.MinValue;

        // ── 射击 ──
        public int ShotsFired { get; set; }
        public int ShotsHit { get; set; }
        public int Headshots { get; set; }
        public int LongRangeHeadshots { get; set; }

        // ── 伤害 ──
        public float TotalDamageDealt { get; set; }
        public float MaxSingleHitDamage { get; set; }
        public float LongestHitDistance { get; set; }

        // ── 击杀 ──
        public int Kills { get; set; }
        public int Deaths { get; set; }

        /// <summary>本局首次击杀时间，用于算击杀速率。</summary>
        public DateTime? FirstKillAt { get; set; }

        /// <summary>最近的开枪时间戳，用于推算峰值射速。</summary>
        public List<DateTime> RecentShotTimes { get; } = new List<DateTime>();

        /// <summary>观测到的最高瞬时射速（发/秒）。</summary>
        public float PeakShotsPerSecond { get; set; }

        // ── 移动类（服务端权威数据，可靠性高） ──

        /// <summary>观测到的最高水平速度（米/秒）。</summary>
        public float MaxHorizontalSpeed { get; set; }

        /// <summary>观测到的最高垂直速度（米/秒）。</summary>
        public float MaxVerticalSpeed { get; set; }

        /// <summary>触发过的移动异常次数。</summary>
        public int MoveViolationCount { get; set; }

        /// <summary>移动异常的具体描述（最多留 5 条）。</summary>
        public List<string> MoveViolations { get; } = new List<string>();

        /// <summary>原地高速旋转的次数。</summary>
        public int SpinCount { get; set; }

        /// <summary>朝向反复翻转的次数。</summary>
        public int JitterCount { get; set; }

        /// <summary>观测到的最高转速（度/秒）。</summary>
        public float MaxYawSpeed { get; set; }

        // ── NoClip（状态直接可读，几乎零误报） ──

        /// <summary>检测到启用 NoClip 的次数。</summary>
        public int NoclipActivations { get; set; }

        /// <summary>最近一次 NoClip 的上下文。</summary>
        public string NoclipContext { get; set; }

        // ── 隐身目标命中（间接推断：正常玩家看不到隐身目标） ──

        /// <summary>命中隐身目标的次数。</summary>
        public int InvisibleTargetHits { get; set; }

        // ── 弹药异常（间接） ──

        /// <summary>本次换弹前打出的最多发数。</summary>
        public int MaxShotsWithoutReload { get; set; }

        /// <summary>当前这一轮没换弹打出的发数。</summary>
        public int ShotsSinceReload { get; set; }

        // ── 治疗 / 物品异常（间接） ──

        /// <summary>单次治疗量超过上限的次数。</summary>
        public int AbnormalHealCount { get; set; }

        /// <summary>最大单次治疗量。</summary>
        public float MaxSingleHeal { get; set; }

        /// <summary>可疑物品获取次数。</summary>
        public int SuspiciousItemCount { get; set; }

        /// <summary>最近的物品获取时间戳（用于算「短时间刷多少件」）。</summary>
        public List<DateTime> RecentItemTimes { get; } = new List<DateTime>();

        /// <summary>当前滑动窗口内的物品数。</summary>
        /// <summary>检测到刷物品后，在这个时刻之前新获得的物品全部收掉。</summary>
        public DateTime ItemSpamBlockedUntil { get; set; } = DateTime.MinValue;

        /// <summary>被封堵收掉的物品件数。</summary>
        public int BlockedItems { get; set; }

        // ───────────── 透视预判 ─────────────

        /// <summary>最近一次「瞄着看不见的敌人」的时刻（滑动窗口用）。</summary>
        public List<DateTime> RecentPredictionTimes { get; } = new List<DateTime>();

        /// <summary>窗口内的预判次数。</summary>
        public int PredictionsInWindow { get; set; }

        /// <summary>预判总次数。</summary>
        public int PredictionCount { get; set; }

        /// <summary>当前连续预判次数。</summary>
        public int PredictionStreak { get; set; }

        /// <summary>最长连续预判次数。</summary>
        public int MaxPredictionStreak { get; set; }

        /// <summary>预判详情（用于日志与证据）。</summary>
        public string PredictionDetail { get; set; }
        public int ItemsInWindow { get; set; }

        /// <summary>是否已判定为刷物品。</summary>
        public bool ItemSpamDetected { get; set; }

        /// <summary>刷物品的详细描述。</summary>
        public string ItemSpamDetail { get; set; }

        // ── 反应时间（间接推断） ──

        /// <summary>低于人类反应下限的命中次数。</summary>
        public int FastReactionCount { get; set; }

        /// <summary>观测到的最快反应时间（毫秒）。</summary>
        public double FastestReactionMs { get; set; }

        /// <summary>
        /// 记录一次物品获取，返回「当前窗口内拿了多少件」。
        /// 窗口窗口秒数由调用方给。
        /// </summary>
        public int RecordItem(int windowSeconds)
        {
            DateTime now = DateTime.UtcNow;
            this.SuspiciousItemCount++;
            this.RecentItemTimes.Add(now);

            // 丢掉窗口外的
            DateTime cutoff = now.AddSeconds(-Math.Max(1, windowSeconds));
            while (this.RecentItemTimes.Count > 0 && this.RecentItemTimes[0] < cutoff)
            {
                this.RecentItemTimes.RemoveAt(0);
            }

            // 防止无限增长
            while (this.RecentItemTimes.Count > 200)
            {
                this.RecentItemTimes.RemoveAt(0);
            }

            this.ItemsInWindow = this.RecentItemTimes.Count;
            return this.ItemsInWindow;
        }

        /// <summary>
        /// 记录一次「瞄着看不见的敌人」。返回窗口内累计次数。
        /// </summary>
        public int RecordPrediction(int windowSeconds)
        {
            DateTime now = DateTime.UtcNow;
            this.PredictionCount++;
            this.RecentPredictionTimes.Add(now);

            DateTime cutoff = now.AddSeconds(-Math.Max(1, windowSeconds));
            while (this.RecentPredictionTimes.Count > 0 && this.RecentPredictionTimes[0] < cutoff)
            {
                this.RecentPredictionTimes.RemoveAt(0);
            }

            while (this.RecentPredictionTimes.Count > 500)
            {
                this.RecentPredictionTimes.RemoveAt(0);
            }

            this.PredictionsInWindow = this.RecentPredictionTimes.Count;
            return this.PredictionsInWindow;
        }

        /// <summary>重置连续预判计数。</summary>
        public void BreakPredictionStreak()
        {
            this.PredictionStreak = 0;
        }
        /// <summary>记录一条移动异常（最多留 5 条）。</summary>
        public void AddMoveViolation(string detail)
        {
            this.MoveViolationCount++;
            if (this.MoveViolations.Count < 5)
            {
                this.MoveViolations.Add(detail);
            }
        }

        // ── 行为时间线（面板、replay、证据落盘都用它） ──

        /// <summary>时间线最多保留多少条。</summary>
        public const int TimelineCapacity = 300;

        /// <summary>行为时间线，按时间顺序，超过容量会丢弃最早的。</summary>
        public List<TimelineEntry> Timeline { get; } = new List<TimelineEntry>();

        /// <summary>被管理员 `dsac follow` 跟踪的标记。</summary>
        public bool Followed { get; set; }

        /// <summary>记一条时间线事件。</summary>
        public void LogEvent(string kind, string detail)
        {
            this.Timeline.Add(new TimelineEntry
            {
                At = DateTime.Now,
                Kind = kind,
                Detail = detail,
            });

            while (this.Timeline.Count > TimelineCapacity)
            {
                this.Timeline.RemoveAt(0);
            }
        }

        /// <summary>时间线里的一条。</summary>
        public sealed class TimelineEntry
        {
            public DateTime At { get; set; }

            public string Kind { get; set; }

            public string Detail { get; set; }

            /// <summary>一行式输出，给 replay / 证据文件用。</summary>
            public override string ToString() => string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:HH:mm:ss}  [{1}] {2}",
                this.At, this.Kind, this.Detail);
        }

        // ── 复核预算与冷却 ──
        public DateTime LastApiCheck { get; set; } = DateTime.MinValue;
        public int ApiCallsThisRound { get; set; }

        /// <summary>本局是否已经报过，避免同一个玩家反复刷 API。</summary>
        public bool AlreadyFlagged { get; set; }

        /// <summary>历次 AI 判定结论摘要。</summary>
        public List<string> VerdictHistory { get; } = new List<string>();

        /// <summary>命中率（无样本时返回 0）。</summary>
        public float Accuracy => ShotsFired > 0 ? (float)ShotsHit / ShotsFired : 0f;

        /// <summary>爆头占命中的比例（无样本时返回 0）。</summary>
        public float HeadshotRatio => ShotsHit > 0 ? (float)Headshots / ShotsHit : 0f;

        /// <summary>远距离爆头占爆头的比例（无样本时返回 0）。</summary>
        public float LongRangeHeadshotRatio => Headshots > 0 ? (float)LongRangeHeadshots / Headshots : 0f;

        /// <summary>每分钟击杀数，按首次击杀到现在的时长计算。</summary>
        public float KillsPerMinute
        {
            get
            {
                if (Kills <= 0 || !FirstKillAt.HasValue)
                {
                    return 0f;
                }

                double minutes = (DateTime.UtcNow - FirstKillAt.Value).TotalMinutes;
                if (minutes < 0.25)
                {
                    // 样本太短，用 15 秒归一化，避免开局秒杀被算成天文数字
                    minutes = 0.25;
                }

                return (float)(Kills / minutes);
            }
        }

        /// <summary>本局存活时长（分钟）。</summary>
        public double RoundMinutes => Math.Max(0.1, (DateTime.UtcNow - FirstSeen).TotalMinutes);

        /// <summary>重置本局数据。</summary>
        public void ResetForNewRound()
        {
            ShotsFired = 0;
            ShotsHit = 0;
            Headshots = 0;
            LongRangeHeadshots = 0;
            TotalDamageDealt = 0f;
            MaxSingleHitDamage = 0f;
            LongestHitDistance = 0f;
            Kills = 0;
            Deaths = 0;
            FirstKillAt = null;
            RecentShotTimes.Clear();
            PeakShotsPerSecond = 0f;
            ApiCallsThisRound = 0;
            AlreadyFlagged = false;
            FirstSeen = DateTime.UtcNow;

            // 新增检测器的状态
            MaxHorizontalSpeed = 0f;
            MaxVerticalSpeed = 0f;
            MoveViolationCount = 0;
            MoveViolations.Clear();
            SpinCount = 0;
            JitterCount = 0;
            MaxYawSpeed = 0f;
            NoclipActivations = 0;
            NoclipContext = null;
            InvisibleTargetHits = 0;
            MaxShotsWithoutReload = 0;
            ShotsSinceReload = 0;
            AbnormalHealCount = 0;
            MaxSingleHeal = 0f;
            SuspiciousItemCount = 0;
            ItemSpamDetected = false;
            ItemSpamDetail = null;
            RecentItemTimes.Clear();
            ItemsInWindow = 0;
            FastReactionCount = 0;
            FastestReactionMs = 0;
        }

        /// <summary>记录一次开枪，并更新峰值射速。</summary>
        public void RecordShot()
        {
            DateTime now = DateTime.UtcNow;
            ShotsFired++;
            LastShotAt = now;
            RecentShotTimes.Add(now);

            // 只保留最近 3 秒的样本
            DateTime cutoff = now.AddSeconds(-3);
            RecentShotTimes.RemoveAll(t => t < cutoff);

            if (RecentShotTimes.Count >= 2)
            {
                double span = (RecentShotTimes[RecentShotTimes.Count - 1] - RecentShotTimes[0]).TotalSeconds;
                if (span > 0.05)
                {
                    float rate = (float)((RecentShotTimes.Count - 1) / span);
                    if (rate > PeakShotsPerSecond)
                    {
                        PeakShotsPerSecond = rate;
                    }
                }
            }
        }
    }
}
