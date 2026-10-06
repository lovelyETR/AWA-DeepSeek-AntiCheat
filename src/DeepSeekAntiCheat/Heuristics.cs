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
    using System.Text;

    /// <summary>
    /// 本地启发式评分。目的是用极低成本筛出「值得让 AI 复核」的样本，
    /// 而不是自己下结论 —— 所以它只做加法，并且每条都给出人类可读的理由。
    /// </summary>
    public static class Heuristics
    {
        /// <summary>一次评分的产物。</summary>
        public sealed class Result
        {
            public float Score { get; set; }

            public List<string> Reasons { get; } = new List<string>();

            /// <summary>
            /// 物理上不可能的读数（例如单发 600 伤害、55 发/秒）。
            /// 这类证据单独一条就足以送审，不需要靠累积分。
            /// </summary>
            public List<string> HardViolations { get; } = new List<string>();

            public bool HasHardViolation => this.HardViolations.Count > 0;

            /// <summary>是否应该送去 AI 复核：累积分够高，或存在硬违规。</summary>
            public bool ShouldReport(float scoreThreshold) => this.Score >= scoreThreshold || this.HasHardViolation;

            public override string ToString() =>
                string.Format(CultureInfo.InvariantCulture, "score={0:F1} reasons=[{1}]", this.Score, string.Join("; ", this.Reasons));
        }

        // ── 每条指标的「饱和点」：达到这个值该轴就拿满分 ──
        // 线性映射到 1.0 会让 92.7% 的爆头率只拿七成分，太软；
        // 真实世界里 95% 爆头率已经是铁证，所以在这里就顶格。
        private const float HeadshotSaturation = 0.95f;
        private const float AccuracySaturation = 0.95f;
        private const float KpmSaturation = 15f;
        private const float LongRangeHeadshotSaturation = 0.90f;
        private const float DamageSaturation = 300f;
        private const float SpsSaturation = 60f;

        // ── 硬违规倍数：超过配置上限的这个倍数 = 物理上不可能 ──
        // 伤害给 3 倍是因为 SCP-096 / MicroHID 之类确实能打出高伤害，
        // 但子弹不可能打到 3 倍；射速给 2 倍是因为没有任何枪能到 40 发/秒。
        private const float HardDamageMultiplier = 3f;
        private const float HardSpsMultiplier = 2f;
        private const float HardKpmMultiplier = 3f;

        /// <summary>
        /// 把「超出阈值的幅度」映射成 0-1 权重：
        /// 到阈值为止是 0，到饱和点是 1，中间线性。
        /// </summary>
        private static float Ramp(float value, float threshold, float saturation)
        {
            if (value <= threshold)
            {
                return 0f;
            }

            float span = saturation - threshold;
            if (span <= 0f)
            {
                return 1f;
            }

            float over = (value - threshold) / span;
            return over > 1f ? 1f : over;
        }

        /// <summary>按配置对玩家统计打分。</summary>
        // AWA :: 评分引擎入口。
        // 本实现由 AWA 编写，用于 DeepSeekAntiCheat。
        public static Result Evaluate(PlayerStats s, Config cfg)
        {
            var r = new Result();
            if (s == null || cfg == null)
            {
                return r;
            }

            int samples = s.ShotsHit;

            // ── 1. 爆头率 ──
            if (samples >= cfg.MinSamples && s.HeadshotRatio > cfg.HeadshotRatioThreshold)
            {
                float w = Ramp(s.HeadshotRatio, cfg.HeadshotRatioThreshold, HeadshotSaturation);
                float pts = w * 30f;
                r.Score += pts;
                r.Reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "爆头率 {0:P1}（阈值 {1:P1}，{2}/{3} 次命中），+{4:F1}",
                    s.HeadshotRatio, cfg.HeadshotRatioThreshold, s.Headshots, samples, pts));
            }

            // ── 2. 命中率 ──
            if (samples >= cfg.MinSamples && s.Accuracy > cfg.AccuracyThreshold)
            {
                float w = Ramp(s.Accuracy, cfg.AccuracyThreshold, AccuracySaturation);
                float pts = w * 25f;
                r.Score += pts;
                r.Reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "命中率 {0:P1}（阈值 {1:P1}，{2}/{3} 发），+{4:F1}",
                    s.Accuracy, cfg.AccuracyThreshold, s.ShotsHit, s.ShotsFired, pts));
            }

            // ── 3. 击杀速率 ──
            if (s.Kills >= 3 && s.KillsPerMinute > cfg.KillsPerMinuteThreshold)
            {
                float w = Ramp(s.KillsPerMinute, cfg.KillsPerMinuteThreshold, KpmSaturation);
                float pts = w * 25f;
                r.Score += pts;
                string msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "击杀速率 {0:F1}/分钟（阈值 {1:F1}，共 {2} 杀）",
                    s.KillsPerMinute, cfg.KillsPerMinuteThreshold, s.Kills);
                r.Reasons.Add(msg + string.Format(CultureInfo.InvariantCulture, "，+{0:F1}", pts));

                float hardAt = cfg.KillsPerMinuteThreshold * HardKpmMultiplier;
                if (s.KillsPerMinute > hardAt)
                {
                    // 注意：不能把表达式写进复合格式串的花括号里 —— 那是插值字符串的语法，
                    // string.Format 会直接抛 FormatException。
                    float secondsPerKill = 60f / Math.Max(0.01f, s.KillsPerMinute);
                    r.HardViolations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}，超过硬上限 {1:F1}（{2:F0} 倍）—— 相当于每 {3:F1} 秒一个击杀",
                        msg, hardAt, HardKpmMultiplier, secondsPerKill));
                }
            }

            // ── 4. 远距离爆头比例 ──
            if (s.Headshots >= 8 && s.LongRangeHeadshotRatio > cfg.LongRangeHeadshotRatioThreshold)
            {
                float w = Ramp(s.LongRangeHeadshotRatio, cfg.LongRangeHeadshotRatioThreshold, LongRangeHeadshotSaturation);
                float pts = w * 20f;
                r.Score += pts;
                r.Reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:F0} 米外爆头占 {1:P1}（{2}/{3}），+{4:F1}",
                    cfg.LongRangeHeadshotDistance, s.LongRangeHeadshotRatio, s.LongRangeHeadshots, s.Headshots, pts));
            }

            // ── 5. 单发伤害异常 ──
            if (s.MaxSingleHitDamage > cfg.MaxReasonableDamage)
            {
                float w = Ramp(s.MaxSingleHitDamage, cfg.MaxReasonableDamage, DamageSaturation);
                float pts = w * 25f;
                r.Score += pts;
                string msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "单发最高伤害 {0:F0}（上限 {1:F0}）",
                    s.MaxSingleHitDamage, cfg.MaxReasonableDamage);
                r.Reasons.Add(msg + string.Format(CultureInfo.InvariantCulture, "，+{0:F1}", pts));

                float hardAt = cfg.MaxReasonableDamage * HardDamageMultiplier;
                if (s.MaxSingleHitDamage > hardAt)
                {
                    r.HardViolations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}，超过硬上限 {1:F0}（{2:F0} 倍）—— 子弹不可能打出这个伤害",
                        msg, hardAt, HardDamageMultiplier));
                }
            }

            // ── 6. 射速异常 ──
            if (s.PeakShotsPerSecond > cfg.MaxReasonableShotsPerSecond)
            {
                float w = Ramp(s.PeakShotsPerSecond, cfg.MaxReasonableShotsPerSecond, SpsSaturation);
                float pts = w * 25f;
                r.Score += pts;
                string msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "峰值射速 {0:F1} 发/秒（上限 {1:F0}）",
                    s.PeakShotsPerSecond, cfg.MaxReasonableShotsPerSecond);
                r.Reasons.Add(msg + string.Format(CultureInfo.InvariantCulture, "，+{0:F1}", pts));

                float hardAt = cfg.MaxReasonableShotsPerSecond * HardSpsMultiplier;
                if (s.PeakShotsPerSecond > hardAt)
                {
                    r.HardViolations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}，超过硬上限 {1:F1}（{2:F0} 倍）—— 没有任何武器能做到",
                        msg, hardAt, HardSpsMultiplier));
                }
            }

            // ── 8. NoClip（状态直接可读，几乎零误报）──
            if (s.NoclipActivations > 0)
            {
                float pts = Math.Min(30f, 15f + ((s.NoclipActivations - 1) * 5f));
                r.Score += pts;
                string msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "启用 NoClip {0} 次", s.NoclipActivations);
                if (!string.IsNullOrEmpty(s.NoclipContext))
                {
                    msg += "（" + s.NoclipContext + "）";
                }

                r.Reasons.Add(msg + string.Format(CultureInfo.InvariantCulture, "，+{0:F1}", pts));

                // NoClip 是服务端直接可读的状态位，正常玩家永远为 false —— 视为硬违规
                r.HardViolations.Add(msg + "，而该玩家的 NoClip 权限为假 —— 服务端状态与权限矛盾");
            }

            // ── 9. 移动异常（服务端权威数据）──
            if (s.MoveViolationCount > 0)
            {
                float pts = Math.Min(30f, 12f + ((s.MoveViolationCount - 1) * 6f));
                r.Score += pts;
                string msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "移动异常 {0} 次（最高水平 {1:F1} 米/秒，最高垂直 {2:F1} 米/秒）",
                    s.MoveViolationCount, s.MaxHorizontalSpeed, s.MaxVerticalSpeed);
                r.Reasons.Add(msg + string.Format(CultureInfo.InvariantCulture, "，+{0:F1}", pts));

                float hardSpeed = cfg.MaxReasonableSpeed * 2f;
                if (s.MaxHorizontalSpeed > hardSpeed)
                {
                    r.HardViolations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "水平速度 {0:F1} 米/秒，超过硬上限 {1:F0}",
                        s.MaxHorizontalSpeed, hardSpeed));
                }

                float hardVert = cfg.MaxReasonableVerticalSpeed * 2f;
                if (s.MaxVerticalSpeed > hardVert)
                {
                    r.HardViolations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "垂直速度 {0:F1} 米/秒，超过硬上限 {1:F0}",
                        s.MaxVerticalSpeed, hardVert));
                }
            }

            // ── 9b. 原地旋转 / 抖动（朝向异常）──
            if (s.SpinCount > 0 || s.JitterCount > 0)
            {
                float pts = Math.Min(25f, 12f + ((s.SpinCount + s.JitterCount - 1) * 6f));
                r.Score += pts;
                string msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "朝向异常：原地旋转 {0} 次、抖动 {1} 次（最高 {2:F0} 度/秒）",
                    s.SpinCount, s.JitterCount, s.MaxYawSpeed);
                r.Reasons.Add(msg + string.Format(CultureInfo.InvariantCulture, "，+{0:F1}", pts));

                // 连续多次高速同向旋转 / 反向翻转，人的手腕做不到 —— 视为硬违规
                if (s.SpinCount >= 2 || s.JitterCount >= 2)
                {
                    r.HardViolations.Add(msg + " —— 超出人手能达到的转速与方向变化频率");
                }
            }

            foreach (string mv in s.MoveViolations)
            {
                r.Reasons.Add("移动明细: " + mv);
            }

            // ── 10. 命中隐身目标（间接推断）──
            if (s.InvisibleTargetHits > 0)
            {
                float pts = Math.Min(20f, 10f + ((s.InvisibleTargetHits - 1) * 5f));
                r.Score += pts;
                r.Reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "命中隐身（SCP-268）目标 {0} 次 —— 正常玩家看不到隐身目标，+{1:F1}",
                    s.InvisibleTargetHits, pts));

                // 偶尔一发流弹可能误中，但 3 次以上不可能是运气
                if (s.InvisibleTargetHits >= 3)
                {
                    r.HardViolations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "命中隐身目标 {0} 次 —— 该玩家看不到目标却反复命中",
                        s.InvisibleTargetHits));
                }
            }

            // ── 11. 弹药异常（间接）──
            bool infiniteAmmoServer = cfg.Features != null && cfg.Features.InfiniteAmmo;
            if (!infiniteAmmoServer && s.MaxShotsWithoutReload > cfg.MaxReasonableShotsPerMagazine)
            {
                float over = (s.MaxShotsWithoutReload - cfg.MaxReasonableShotsPerMagazine)
                             / Math.Max(1f, (float)cfg.MaxReasonableShotsPerMagazine);
                float pts = Math.Min(20f, over * 20f);
                r.Score += pts;
                r.Reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "连续开枪 {0} 发未换弹（弹匣上限约 {1}），+{2:F1}",
                    s.MaxShotsWithoutReload, cfg.MaxReasonableShotsPerMagazine, pts));

                int hardMag = cfg.MaxReasonableShotsPerMagazine * 3;
                if (s.MaxShotsWithoutReload >= hardMag)
                {
                    r.HardViolations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "连续开枪 {0} 发未换弹，超过硬上限 {1}（{2} 倍弹匣）",
                        s.MaxShotsWithoutReload, hardMag, 3));
                }
            }

            // ── 12. 异常回血（间接）──
            if (s.AbnormalHealCount > 0)
            {
                float pts = Math.Min(15f, 8f * s.AbnormalHealCount);
                r.Score += pts;
                r.Reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "异常治疗 {0} 次（单次最高 {1:F0}，上限 {2:F0}），+{3:F1}",
                    s.AbnormalHealCount, s.MaxSingleHeal, cfg.MaxReasonableHeal, pts));
            }

            // ── 13. 刷物品（滑动窗口判定）──
            // 判定在 Plugin.OnItemAdded 里做（那里有窗口计时），这里只负责计分。
            if (s.ItemSpamDetected)
            {
                r.Score += 40f;
                string msg = string.IsNullOrEmpty(s.ItemSpamDetail)
                    ? "短时间内刷取大量物品"
                    : "刷物品: " + s.ItemSpamDetail;
                r.Reasons.Add(msg + "，+40.0");

                // 窗口内刷物品是物理上不可能的行为 —— 升为硬违规，绕过样本门槛
                r.HardViolations.Add(msg + " —— 正常玩家不可能在这么短时间里拿到这么多物品");
            }
            else if (s.SuspiciousItemCount > 0)
            {
                float pts = Math.Min(15f, 8f * s.SuspiciousItemCount);
                r.Score += pts;
                r.Reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "可疑物品获取 {0} 次，+{1:F1}",
                    s.SuspiciousItemCount, pts));
            }

            // ── 14. 反应时间异常（间接推断）──
            if (s.FastReactionCount > 0)
            {
                float pts = Math.Min(25f, 12f + ((s.FastReactionCount - 1) * 6f));
                r.Score += pts;
                r.Reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "反应时间低于人类下限 {0} 次（最快 {1:F0} 毫秒，下限 {2} 毫秒），+{3:F1}",
                    s.FastReactionCount, s.FastestReactionMs, cfg.HumanReactionFloorMs, pts));

                // 连续 3 次以上低于人类极限，几乎不可能是人
                if (s.FastReactionCount >= 3)
                {
                    r.HardViolations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "连续 {0} 次在目标进入房间后 {1:F0} 毫秒内命中 —— 低于人类反应极限",
                        s.FastReactionCount, s.FastestReactionMs));
                }
            }

            // ── 7. 远距离命中本身也是信号 ──
            if (s.LongestHitDistance > 120f && s.Kills >= 2)
            {
                r.Score += 8f;
                r.Reasons.Add(string.Format(CultureInfo.InvariantCulture, "最远命中距离 {0:F0} 米，+8.0", s.LongestHitDistance));
            }

            if (r.Score > 100f)
            {
                r.Score = 100f;
            }

            return r;
        }

        /// <summary>把统计打包成给 AI 看的 JSON（不含身份信息，除非配置要求）。</summary>
        public static string BuildReportJson(PlayerStats s, Config cfg, Heuristics.Result local)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');

            if (cfg.IncludePlayerIdentity)
            {
                sb.Append("\"nickname\":").Append(JsonString(s.Nickname)).Append(',');
                sb.Append("\"user_id\":").Append(JsonString(s.UserId)).Append(',');
            }

            sb.Append("\"session_minutes\":").Append(F(s.RoundMinutes)).Append(',');
            sb.Append("\"shots_fired\":").Append(s.ShotsFired).Append(',');
            sb.Append("\"shots_hit\":").Append(s.ShotsHit).Append(',');
            sb.Append("\"headshots\":").Append(s.Headshots).Append(',');
            sb.Append("\"long_range_headshots\":").Append(s.LongRangeHeadshots).Append(',');
            sb.Append("\"accuracy\":").Append(F(s.Accuracy)).Append(',');
            sb.Append("\"headshot_ratio\":").Append(F(s.HeadshotRatio)).Append(',');
            sb.Append("\"long_range_headshot_ratio\":").Append(F(s.LongRangeHeadshotRatio)).Append(',');
            sb.Append("\"kills\":").Append(s.Kills).Append(',');
            sb.Append("\"deaths\":").Append(s.Deaths).Append(',');
            sb.Append("\"kills_per_minute\":").Append(F(s.KillsPerMinute)).Append(',');
            sb.Append("\"total_damage_dealt\":").Append(F(s.TotalDamageDealt)).Append(',');
            sb.Append("\"max_single_hit_damage\":").Append(F(s.MaxSingleHitDamage)).Append(',');
            sb.Append("\"longest_hit_distance_m\":").Append(F(s.LongestHitDistance)).Append(',');
            sb.Append("\"peak_shots_per_second\":").Append(F(s.PeakShotsPerSecond)).Append(',');

            // 新增检测器的数据（服务端可见的行为）
            sb.Append("\"max_horizontal_speed\":").Append(F(s.MaxHorizontalSpeed)).Append(',');
            sb.Append("\"max_vertical_speed\":").Append(F(s.MaxVerticalSpeed)).Append(',');
            sb.Append("\"movement_anomalies\":").Append(s.MoveViolationCount).Append(',');
            sb.Append("\"spin_count\":").Append(s.SpinCount).Append(',');
            sb.Append("\"jitter_count\":").Append(s.JitterCount).Append(',');
            sb.Append("\"max_yaw_speed\":").Append(F(s.MaxYawSpeed)).Append(',');
            sb.Append("\"noclip_activations\":").Append(s.NoclipActivations).Append(',');
            sb.Append("\"hits_on_invisible_targets\":").Append(s.InvisibleTargetHits).Append(',');
            sb.Append("\"max_shots_without_reload\":").Append(s.MaxShotsWithoutReload).Append(',');
            sb.Append("\"abnormal_heals\":").Append(s.AbnormalHealCount).Append(',');
            sb.Append("\"suspicious_items\":").Append(s.SuspiciousItemCount).Append(',');
            sb.Append("\"item_spam_detected\":").Append(s.ItemSpamDetected ? "true" : "false").Append(',');
            sb.Append("\"fast_reactions\":").Append(s.FastReactionCount).Append(',');
            sb.Append("\"fastest_reaction_ms\":").Append(F(s.FastestReactionMs)).Append(',');

            sb.Append("\"local_heuristic_score\":").Append(F(local.Score)).Append(',');
            sb.Append("\"local_flags\":[");
            for (int i = 0; i < local.Reasons.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(JsonString(local.Reasons[i]));
            }

            sb.Append("],");

            // 硬违规单独列出来，让 AI 知道「这些读数物理上不可能」
            sb.Append("\"physically_impossible\":[");
            for (int i = 0; i < local.HardViolations.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(JsonString(local.HardViolations[i]));
            }

            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>最小 JSON 字符串转义。</summary>
        internal static string JsonString(string raw)
        {
            if (raw == null)
            {
                return "null";
            }

            var sb = new StringBuilder(raw.Length + 8);
            sb.Append('"');
            foreach (char ch in raw)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
