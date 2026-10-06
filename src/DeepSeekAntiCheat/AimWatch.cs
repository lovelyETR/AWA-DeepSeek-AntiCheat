// ============================================================================
//  AWA :: DeepSeekAntiCheat —— 透视预判检测
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  【为什么要有这个】
//  服务端看不到客户端渲染，所以「透视」没法直接检测。
//  但透视的人有一个藏不住的行为：**准星总是提前对着他看不见的敌人**。
//
//  正常玩家瞄的方向 = 他看得到的方向（或者随机扫视）。
//  透视玩家瞄的方向 = 敌人的真实位置，哪怕中间隔着墙、隔着好几个房间。
//
//  偶尔一两次可以说是运气 / 预判 / 听脚步。
//  但**次数多起来就不一样了** —— 这个检测就是数这个次数。
//
//  【怎么判】
//  每隔一个采样周期，对每个活着的玩家：
//    1. 取他的准星水平朝向
//    2. 遍历敌对阵营的玩家
//    3. 如果某个敌人的方向与他准星夹角很小（≤ 阈值）
//       且**那个敌人不在同一个房间**（近似「看不见」）
//       → 记一次「预判」
//
//  累计次数超过阈值就算可疑，超过更高的阈值算硬违规。
//
//  【可靠性】
//  这是**间接推断**，不是铁证：
//    - 玩家可能正在瞄门口，而敌人刚好要进来
//    - 隔墙听脚步后预瞄也是正常操作
//  所以门槛定得比较高，而且要配合 AI 复核一起用。
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System.Collections.Generic;

    using Exiled.API.Enums;
    using Exiled.API.Features;

    using UnityEngine;

    /// <summary>透视预判采样。</summary>
    internal static class AimWatch
    {
        /// <summary>
        /// 采样一次。返回 true 表示这一帧「瞄着一个看不见的敌人」。
        /// </summary>
        internal static bool Sample(Config cfg, Player p, IEnumerable<Player> others)
        {
            if (cfg == null || !cfg.DetectEspPrediction || p == null || others == null)
            {
                return false;
            }

            Transform cam = p.CameraTransform;
            if (cam == null)
            {
                return false;
            }

            Vector3 aim = cam.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            aim.Normalize();

            Room mine = p.CurrentRoom;
            float maxDist = cfg.EspPredictionMaxDistance;
            float maxAng = cfg.EspPredictionAngle;

            foreach (Player q in others)
            {
                if (q == null || q == p)
                {
                    continue;
                }

                // 只关心活着的敌对玩家
                if (!IsValidTarget(p, q))
                {
                    continue;
                }

                Vector3 d = q.Position - p.Position;
                d.y = 0f;

                float dist = d.magnitude;
                if (dist < 1f || dist > maxDist)
                {
                    continue;
                }

                d.Normalize();

                if (Vector3.Angle(aim, d) > maxAng)
                {
                    continue;
                }

                // 瞄着了 —— 他看得见吗？
                // 服务端拿不到视线遮挡，用「在不在同一个房间」近似。
                bool sameRoom = mine != null && ReferenceEquals(mine, q.CurrentRoom);
                if (!sameRoom)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是不是值得关注的敌对目标。</summary>
        private static bool IsValidTarget(Player p, Player q)
        {
            try
            {
                if (!q.IsAlive || q.IsNPC || q.IsHost)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            Side a;
            Side b;

            try
            {
                a = p.Role.Side;
                b = q.Role.Side;
            }
            catch
            {
                return false;
            }

            // 自己人之间互相瞄着很正常，不算
            if (a == b)
            {
                return false;
            }

            // 观察者不打人
            if (a == Side.None || a == Side.Tutorial || b == Side.None || b == Side.Tutorial)
            {
                return false;
            }

            return true;
        }
    }
}
