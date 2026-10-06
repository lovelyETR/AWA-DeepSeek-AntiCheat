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

    /// <summary>
    /// 作弊行为模拟器 —— 只伪造「行为统计数据」，不碰任何游戏状态。
    ///
    /// 它没有自瞄、没有内存写入、没有网络包伪造，
    /// 因此原理上无法被用来在服务器上作弊；它的唯一作用是
    /// 把各种作弊的数值特征喂给真实检测链路，验证检测是否有效。
    /// </summary>
    public static class Scenarios
    {
        /// <summary>一个场景的元信息。</summary>
        public sealed class Scenario
        {
            public string Id { get; set; }

            public string Name { get; set; }

            /// <summary>这是不是作弊行为（用于判断检测是否正确）。</summary>
            public bool IsCheat { get; set; }

            /// <summary>
            /// 预期本地评分是否应该达到送审阈值。
            /// 单轴弱信号（例如只有命中率略高）刻意设为 false —— 那属于设计意图，不是漏报。
            /// </summary>
            public bool ExpectReport { get; set; }

            /// <summary>期望本地评分落在哪个区间。</summary>
            public float ExpectScoreAtLeast { get; set; }

            public float ExpectScoreAtMost { get; set; } = 100f;

            /// <summary>构造该场景的统计数据。</summary>
            public Func<Config, PlayerStats> Build { get; set; }

            public string Note { get; set; }
        }

        /// <summary>全部场景。</summary>
            // AWA :: 作弊行为模拟器场景表。
            // 只伪造统计数据，不接触游戏 —— 这些场景无法被用来作弊。
        public static IList<Scenario> All()
        {
            return new List<Scenario>
            {
                // ─────────── 正常玩家（不该被报） ───────────
                new Scenario
                {
                    Id = "legit_newbie",
                    Name = "新手玩家",
                    IsCheat = false,
                    ExpectReport = false,
                    ExpectScoreAtLeast = 0f,
                    ExpectScoreAtMost = 10f,
                    Note = "低命中低爆头，应该完全无感",
                    Build = cfg => Make(cfg, shots: 80, hits: 18, head: 3, kills: 1, maxDmg: 40f, peakSps: 8f, longest: 30f),
                },
                new Scenario
                {
                    Id = "legit_average",
                    Name = "普通玩家",
                    IsCheat = false,
                    ExpectReport = false,
                    ExpectScoreAtLeast = 0f,
                    ExpectScoreAtMost = 20f,
                    Note = "30% 命中 / 20% 爆头",
                    Build = cfg => Make(cfg, shots: 200, hits: 60, head: 12, kills: 5, maxDmg: 55f, peakSps: 10f, longest: 60f),
                },
                new Scenario
                {
                    Id = "legit_veteran",
                    Name = "高玩（高爆头但正常）",
                    IsCheat = false,
                    ExpectReport = false,
                    ExpectScoreAtLeast = 0f,
                    ExpectScoreAtMost = 45f,
                    Note = "硬核玩家真的能打出 60% 爆头 —— 这是最重要的一条误报防线",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 300, hits: 195, head: 117, kills: 22, maxDmg: 68f, peakSps: 12f, longest: 90f);
                        s.FirstKillAt = DateTime.UtcNow.AddMinutes(-18);
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "legit_scp096",
                    Name = "SCP-096（身份天然异常）",
                    IsCheat = false,
                    ExpectReport = false,
                    ExpectScoreAtLeast = 0f,
                    ExpectScoreAtMost = 60f,
                    Note = "特殊角色造成的天然高数据，不应被当成作弊",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 90, hits: 78, head: 40, kills: 14, maxDmg: 250f, peakSps: 14f, longest: 70f);
                        s.FirstKillAt = DateTime.UtcNow.AddMinutes(-6);
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "edge_low_sample",
                    Name = "样本不足（4 杀秒退）",
                    IsCheat = false,
                    ExpectReport = false,
                    ExpectScoreAtLeast = 0f,
                    ExpectScoreAtMost = 30f,
                    Note = "只有 5 次命中，任何比例都不足以下结论",
                    Build = cfg => Make(cfg, shots: 6, hits: 5, head: 5, kills: 3, maxDmg: 90f, peakSps: 20f, longest: 50f),
                },

                // ─────────── 作弊行为（该被报） ───────────
                new Scenario
                {
                    Id = "cheat_aimbot",
                    Name = "自瞄（aimbot）",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 70f,
                    Note = "命中率和爆头率同时逼近上限，且大量远距离爆头",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 120, hits: 110, head: 102, kills: 18, maxDmg: 75f, peakSps: 10f, longest: 150f);
                        s.LongRangeHeadshots = 85;
                        s.FirstKillAt = DateTime.UtcNow.AddMinutes(-3);
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_triggerbot",
                    Name = "扳机机器人（triggerbot）",
                    IsCheat = true,

                    // 刻意设 false：只有命中率一个弱信号，不该单独惊动 AI。
                    // 这类玩家和「近距离霰弹枪打得准」在数值上无法区分。
                    ExpectReport = false,
                    ExpectScoreAtLeast = 18f,
                    ExpectScoreAtMost = 40f,
                    Note = "只有命中率异常（95%），爆头率正常 —— 单轴弱信号，设计上不单独上报",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 150, hits: 143, head: 43, kills: 10, maxDmg: 60f, peakSps: 11f, longest: 80f);
                        s.FirstKillAt = DateTime.UtcNow.AddMinutes(-5);
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_rapidfire",
                    Name = "射速修改（rapid fire）",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 20f,
                    Note = "峰值 55 发/秒，物理上不可能（硬违规）",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 400, hits: 120, head: 30, kills: 8, maxDmg: 50f, peakSps: 55f, longest: 60f);
                        s.FirstKillAt = DateTime.UtcNow.AddMinutes(-5);
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_damage",
                    Name = "伤害修改（damage hack）",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 20f,
                    Note = "单发 600 伤害，正常武器不可能（硬违规）",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 60, hits: 40, head: 10, kills: 12, maxDmg: 600f, peakSps: 9f, longest: 70f);
                        s.FirstKillAt = DateTime.UtcNow.AddMinutes(-4);
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_killfarm",
                    Name = "击杀速率异常",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 15f,
                    Note = "2 分钟内 40 杀（20/分钟）—— 触发击杀速率硬违规",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 250, hits: 170, head: 62, kills: 40, maxDmg: 70f, peakSps: 13f, longest: 85f);
                        s.FirstKillAt = DateTime.UtcNow.AddMinutes(-2);
                        return s;
                    },
                },
                // ─────────── 新增检测器（阶段 1-2）───────────
                new Scenario
                {
                    Id = "cheat_noclip",
                    Name = "NoClip 穿墙",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 15f,
                    Note = "服务端状态位直接可读：权限为假却启用 NoClip —— 几乎零误报",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 40, hits: 15, head: 4, kills: 2, maxDmg: 50f, peakSps: 8f, longest: 40f);
                        s.NoclipActivations = 1;
                        s.NoclipContext = "房间 914，位置 (12, -1, 34)";
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_speed",
                    Name = "加速外挂",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 12f,
                    Note = "位置采样算出水平速度 45 米/秒，人类上限约 7",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 60, hits: 20, head: 6, kills: 4, maxDmg: 55f, peakSps: 9f, longest: 50f);
                        s.MoveViolationCount = 2;
                        s.MaxHorizontalSpeed = 45f;
                        s.AddMoveViolation("水平速度 45.0 米/秒（上限 12）");
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_teleport",
                    Name = "瞬移",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 12f,
                    Note = "单次采样位移 80 米；已排除 SCP-106/096/173 的机制位移",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 30, hits: 12, head: 3, kills: 3, maxDmg: 50f, peakSps: 7f, longest: 30f);
                        s.MoveViolationCount = 1;
                        // 真实的瞬移必然算出巨大瞬时速度（80 米 / 0.3 秒 ≈ 266 米/秒）
                        s.MaxHorizontalSpeed = 266f;
                        s.AddMoveViolation("单次采样位移 80.0 米（阈值 25），用时 0.30 秒");
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_fly",
                    Name = "飞天",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 12f,
                    Note = "垂直速度 30 米/秒，正常跳跃约 5",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 50, hits: 18, head: 5, kills: 3, maxDmg: 52f, peakSps: 8f, longest: 45f);
                        s.MoveViolationCount = 3;
                        s.MaxVerticalSpeed = 30f;
                        s.AddMoveViolation("垂直速度 30.0 米/秒（上限 10），位置 Y 从 2.0 到 40.0");
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_invisible_hit",
                    Name = "命中隐身目标",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 10f,
                    Note = "间接推断：正常玩家看不到 SCP-268 隐身目标",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 60, hits: 30, head: 10, kills: 5, maxDmg: 60f, peakSps: 9f, longest: 50f);
                        s.InvisibleTargetHits = 4;
                        return s;
                    },
                },
                new Scenario
                {
                    Id = "cheat_infinite_ammo",
                    Name = "无限弹药",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 10f,
                    Note = "间接推断：连打 400 发从不换弹",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 400, hits: 130, head: 40, kills: 8, maxDmg: 55f, peakSps: 11f, longest: 60f);
                        s.MaxShotsWithoutReload = 400;
                        return s;
                    },
                },                new Scenario
                {
                    Id = "cheat_reaction",
                    Name = "反应时间异常",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 12f,
                    Note = "间接推断：目标进房后 40 毫秒内命中，人类极限约 150ms",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 80, hits: 40, head: 15, kills: 6, maxDmg: 60f, peakSps: 10f, longest: 50f);
                        s.FastReactionCount = 4;
                        s.FastestReactionMs = 40;
                        return s;
                    },
                },                new Scenario
                {
                    Id = "cheat_full",
                    Name = "全开（自瞄+射速+伤害）",
                    IsCheat = true,
                    ExpectReport = true,
                    ExpectScoreAtLeast = 95f,
                    Note = "所有指标一起爆，应该拿满分",
                    Build = cfg =>
                    {
                        var s = Make(cfg, shots: 200, hits: 194, head: 188, kills: 30, maxDmg: 420f, peakSps: 48f, longest: 200f);
                        s.LongRangeHeadshots = 170;
                        s.FirstKillAt = DateTime.UtcNow.AddMinutes(-2);
                        return s;
                    },
                },
            };
        }

        /// <summary>把一批场景跑过本地评分，返回结果。</summary>
        public static IList<ScenarioResult> RunLocal(Config cfg, IList<Scenario> scenarios = null)
        {
            var results = new List<ScenarioResult>();
            foreach (Scenario sc in scenarios ?? All())
            {
                PlayerStats stats = sc.Build(cfg);
                Heuristics.Result local = Heuristics.Evaluate(stats, cfg);
                results.Add(new ScenarioResult
                {
                    Scenario = sc,
                    Stats = stats,
                    Local = local,

                    // 必须用 ShouldReport —— 它会认「硬违规」，与插件里的判断保持一致。
                    // 只比分数会漏掉「单轴但物理上不可能」的情况。
                    // 样本门槛只约束比率类信号；硬违规必须绕过它（与插件里的判断保持一致）
                    WouldReportToAi = local.ShouldReport(cfg.LocalScoreToReport)
                                      && (stats.ShotsHit >= cfg.MinSamples || local.HasHardViolation),
                });
            }

            return results;
        }

        /// <summary>场景跑分结果。</summary>
        public sealed class ScenarioResult
        {
            public Scenario Scenario { get; set; }

            public PlayerStats Stats { get; set; }

            public Heuristics.Result Local { get; set; }

            /// <summary>本地评分是否达到送去 AI 复核的阈值。</summary>
            public bool WouldReportToAi { get; set; }

            /// <summary>AI 复核结论（没跑就是 null）。</summary>
            public DeepSeekVerdict Verdict { get; set; }

            /// <summary>本地期望是否满足。</summary>
            public bool LocalExpectationMet =>
                this.Local.Score >= this.Scenario.ExpectScoreAtLeast
                && this.Local.Score <= this.Scenario.ExpectScoreAtMost;

            public override string ToString() => string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] {1} score={2:F1} report={3}",
                this.Scenario.Id, this.Scenario.Name, this.Local.Score, this.WouldReportToAi);
        }

        // ──────────────────────────────────────────────

        /// <summary>
        /// 生成一份伪造的行为统计。
        /// 注意：这里产生的只是数字，没有任何游戏交互。
        /// </summary>
        private static PlayerStats Make(
            Config cfg,
            int shots,
            int hits,
            int head,
            int kills,
            float maxDmg,
            float peakSps,
            float longest)
        {
            var s = new PlayerStats
            {
                UserId = "SIMULATED@test",
                Nickname = "[模拟]",
                ShotsFired = shots,
                ShotsHit = hits,
                Headshots = head,
                Kills = kills,
                MaxSingleHitDamage = maxDmg,
                PeakShotsPerSecond = peakSps,
                LongestHitDistance = longest,
                TotalDamageDealt = maxDmg * 0.6f * hits,
                FirstSeen = DateTime.UtcNow.AddMinutes(-10),
            };

            // 远距离爆头按比例给一部分，除非场景自己覆盖
            s.LongRangeHeadshots = (int)(head * 0.3f);

            if (kills > 0)
            {
                s.FirstKillAt = DateTime.UtcNow.AddMinutes(-5);
            }

            return s;
        }
    }
}
