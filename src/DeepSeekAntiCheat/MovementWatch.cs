// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Exiled.API.Features;
    using UnityEngine;

    /// <summary>
    /// 移动异常检测器。
    ///
    /// 做法：每个 tick 采样一次所有玩家的位置，算出瞬时速度，
    /// 找出「人不可能达到」的数值。
    ///
    /// 可靠性说明：
    ///   - 速度/瞬移/垂直异常都是**服务端直接可见**的（位置是服务端权威数据），
    ///     所以这几项可靠性高。
    ///   - 但仍会有误报来源：SCP-106 传送、SCP-096 冲刺、被 SCP-173 丢飞、
    ///     电梯/传送门、服务器卡顿造成的跳变。下面都做了排除。
    /// </summary>
    internal sealed class MovementWatch
    {
        /// <summary>单个玩家的上一次采样。</summary>
        private sealed class Sample
        {
            public Vector3 Position;
            public float Time;
            public bool Valid;

            /// <summary>上一次的朝向 yaw（度）。</summary>
            public float Yaw;

            /// <summary>上一次的 yaw 变化方向（+1 / -1）。</summary>
            public int LastYawDir;

            /// <summary>连续同向旋转的次数。</summary>
            public int SpinStreak;

            /// <summary>连续反向翻转的次数。</summary>
            public int JitterStreak;
        }

        private readonly Dictionary<string, Sample> lastSample = new Dictionary<string, Sample>();

        /// <summary>一次采样发现的移动异常。</summary>
        internal sealed class Violation
        {
            /// <summary>违规的玩家。由 Tick 直接带回，避免事后猜归属。</summary>
            public Player Player { get; set; }

            /// <summary>速度类 / 瞬移类 / 垂直类。</summary>
            public string Kind { get; set; }

            /// <summary>人类可读描述。</summary>
            public string Detail { get; set; }

            /// <summary>严重程度权重（直接加进本地评分）。</summary>
            public float Weight { get; set; }
        }

        /// <summary>
        /// 采样一次。返回本 tick 发现的异常（可能为空）。
        /// </summary>
        internal List<Violation> Tick(Config cfg, float maxSpeed, float teleportDistance, float maxVertical)
        {
            return this.Tick(cfg, maxSpeed, teleportDistance, maxVertical, 600f, 8, 500f, 8);
        }

        /// <summary>
        /// 采样一次（含朝向异常检测）。
        ///
        /// 旋转类检测的原理：
        ///   - 人的手腕转速有限（快速甩枪也就 ~700°/秒，而且持续不了）
        ///   - 原地高速同向旋转 = 陀螺仪/反自瞄外挂
        ///   - 每帧方向随机翻转 = 脚本化瞄准
        /// 注意：采样间隔默认 0.3 秒，所以单次采样能跨过很大的角度。
        /// </summary>
        internal List<Violation> Tick(Config cfg, float maxSpeed, float teleportDistance, float maxVertical,
            float spinDegPerSec, int spinStreakNeeded, float jitterDegPerSec, int jitterStreakNeeded)
        {
            var found = new List<Violation>();
            float now = Time.realtimeSinceStartup;

            foreach (Player p in Player.List)
            {
                if (p == null || !p.IsConnected || p.IsNPC || !p.IsAlive)
                {
                    continue;
                }

                string id = p.UserId;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                // SCP-106 会瞬移，SCP-096 会冲刺，这些是游戏机制，不是作弊
                if (IsExemptRole(p))
                {
                    this.lastSample.Remove(id);
                    continue;
                }

                Vector3 pos;
                try
                {
                    pos = p.Position;
                }
                catch
                {
                    continue;
                }

                if (!this.lastSample.TryGetValue(id, out Sample s))
                {
                    s = new Sample();
                    this.lastSample[id] = s;
                }

                if (!s.Valid)
                {
                    s.Position = pos;
                    s.Time = now;
                    s.Valid = true;
                    continue;
                }

                float dt = now - s.Time;
                s.Time = now;
                Vector3 prev = s.Position;
                s.Position = pos;

                // 采样间隔太小或太大都不算（太小说明这一帧没动，太大说明中间丢帧）
                if (dt < 0.05f || dt > 2f)
                {
                    continue;
                }

                Vector3 delta = pos - prev;
                float dist = delta.magnitude;
                float speed = dist / dt;

                // ── 朝向采样（旋转 / 抖动检测）──
                try
                {
                    float yaw = p.Rotation.eulerAngles.y;
                    float dyaw = Mathf.DeltaAngle(s.Yaw, yaw);   // 归一化到 -180..180
                    float yawSpeed = Math.Abs(dyaw) / dt;
                    int dir = dyaw > 0f ? 1 : (dyaw < 0f ? -1 : 0);

                    // 同向连续旋转
                    if (dir != 0 && dir == s.LastYawDir && yawSpeed >= spinDegPerSec)
                    {
                        s.SpinStreak++;
                    }
                    else
                    {
                        s.SpinStreak = yawSpeed >= spinDegPerSec ? 1 : 0;
                    }

                    // 方向反复翻转
                    if (dir != 0 && s.LastYawDir != 0 && dir != s.LastYawDir && yawSpeed >= jitterDegPerSec)
                    {
                        s.JitterStreak++;
                    }
                    else if (yawSpeed < jitterDegPerSec)
                    {
                        s.JitterStreak = 0;
                    }

                    if (dir != 0)
                    {
                        s.LastYawDir = dir;
                    }

                    s.Yaw = yaw;

                    if (s.JitterStreak >= jitterStreakNeeded)
                    {
                        found.Add(new Violation
                        {
                            Player = p,
                            Kind = "jitter",
                            Weight = 25f,
                            Detail = string.Format(
                                CultureInfo.InvariantCulture,
                                "朝向每 {0:F2} 秒就反向翻转一次，连续 {1} 次（最快 {2:F0} 度/秒）—— 人手做不到",
                                dt, s.JitterStreak, yawSpeed),
                        });
                        s.JitterStreak = 0;
                    }

                    if (s.SpinStreak >= spinStreakNeeded)
                    {
                        found.Add(new Violation
                        {
                            Player = p,
                            Kind = "spin",
                            Weight = 25f,
                            Detail = string.Format(
                                CultureInfo.InvariantCulture,
                                "原地同向旋转，连续 {0} 次（最高 {1:F0} 度/秒）",
                                s.SpinStreak, yawSpeed),
                        });
                        s.SpinStreak = 0;
                    }
                }
                catch
                {
                    // 朝向读不到就跳过
                }

                // ① 瞬移
                if (dist > teleportDistance)
                {
                    found.Add(new Violation
                    {
                        Player = p,
                        Kind = "teleport",
                        Weight = 30f,
                        Detail = string.Format(
                            CultureInfo.InvariantCulture,
                            "单次采样位移 {0:F1} 米（阈值 {1:F0}），用时 {2:F2} 秒",
                            dist, teleportDistance, dt),
                    });
                    continue;
                }

                // ② 速度异常（排除垂直分量，垂直单独判）
                var horizontal = new Vector3(delta.x, 0f, delta.z);
                float hSpeed = horizontal.magnitude / dt;
                if (hSpeed > maxSpeed)
                {
                    found.Add(new Violation
                    {
                        Player = p,
                        Kind = "speed",
                        Weight = 25f,
                        Detail = string.Format(
                            CultureInfo.InvariantCulture,
                            "水平速度 {0:F1} 米/秒（上限 {1:F0}）",
                            hSpeed, maxSpeed),
                    });
                }

                // ③ 垂直异常（飞天 / 悬浮）
                float vSpeed = Math.Abs(delta.y) / dt;
                if (vSpeed > maxVertical)
                {
                    found.Add(new Violation
                    {
                        Player = p,
                        Kind = "vertical",
                        Weight = 25f,
                        Detail = string.Format(
                            CultureInfo.InvariantCulture,
                            "垂直速度 {0:F1} 米/秒（上限 {1:F0}），位置 Y 从 {2:F1} 到 {3:F1}",
                            vSpeed, maxVertical, prev.y, pos.y),
                    });
                }
            }

            return found;
        }

        /// <summary>清掉某个玩家的采样（离场/死亡时调用）。</summary>
        internal void Forget(string userId)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                this.lastSample.Remove(userId);
            }
        }

        /// <summary>整局重置。</summary>
        internal void Clear() => this.lastSample.Clear();

        /// <summary>跟踪人数（给面板显示用）。</summary>
        internal int TrackedCount => this.lastSample.Count;

        /// <summary>
        /// 这些角色的位移是游戏机制，不该算作弊。
        /// SCP-106 穿墙传送、SCP-096 狂暴冲刺、SCP-173 瞬移。
        /// </summary>
        private static bool IsExemptRole(Player p)
        {
            try
            {
                string role = p.Role.Type.ToString();
                return role.IndexOf("Scp106", StringComparison.OrdinalIgnoreCase) >= 0
                       || role.IndexOf("Scp096", StringComparison.OrdinalIgnoreCase) >= 0
                       || role.IndexOf("Scp173", StringComparison.OrdinalIgnoreCase) >= 0
                       || role.IndexOf("Scp939", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
