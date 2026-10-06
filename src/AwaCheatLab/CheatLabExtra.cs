// ============================================================================
//  AWA :: 反作弊测试插件 (CheatLab) —— 扩展注入
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  无敌 / 免卡开门 / 原地旋转 / 随机抖动。
//  全部是服务端改状态，没有客户端能力。
// ============================================================================

namespace AwaCheatLab
{
    using System;
    using System.Globalization;
    using System.Text;
    using Exiled.API.Features;
    using UnityEngine;

    /// <summary>注入集合（扩展部分）。</summary>
    internal static partial class Inject
    {
        /// <summary>无敌。</summary>
        internal static string God(Player p)
        {
            bool before = p.IsGodModeEnabled;
            p.IsGodModeEnabled = true;
            return string.Format(
                CultureInfo.InvariantCulture,
                "IsGodModeEnabled: {0} -> true\n" +
                "反作弊可以从「吃满伤害也不死」这个结果反推。",
                before);
        }

        /// <summary>免卡开门：把最近的一扇门直接解锁并打开，不刷卡、不看权限。</summary>
        internal static string DoorOpen(Player p)
        {
            Exiled.API.Features.Doors.Door nearest = null;
            float best = float.MaxValue;
            Vector3 me = p.Position;

            try
            {
                foreach (Exiled.API.Features.Doors.Door d in Exiled.API.Features.Doors.Door.List)
                {
                    if (d == null)
                    {
                        continue;
                    }

                    float dist = Vector3.Distance(me, d.Position);
                    if (dist < best)
                    {
                        best = dist;
                        nearest = d;
                    }
                }
            }
            catch (Exception e)
            {
                return "遍历门列表失败: " + e.Message;
            }

            if (nearest == null)
            {
                return "附近找不到门。";
            }

            var sb = new StringBuilder();
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "最近的门的距离 {0:F1} 米\n  原本: 锁={1}  开={2}\n",
                best,
                nearest.IsLocked ? "是" : "否",
                nearest.IsOpen ? "是" : "否");

            try
            {
                nearest.ChangeLock(Exiled.API.Enums.DoorLockType.None);
                nearest.IsOpen = true;
                sb.AppendLine("  现在: 锁=否  开=是    ← 没刷卡、没权限");
                sb.AppendLine();
                sb.AppendLine("这模拟的是「免卡开门」的作弊行为。");
                sb.AppendLine("反作弊可以通过 InteractingDoor 事件里「没刷卡却开了锁门」来抓。");
            }
            catch (Exception e)
            {
                sb.AppendLine("  开门失败: " + e.Message);
            }

            return sb.ToString();
        }

        /// <summary>原地高速旋转（反自瞄类外挂的典型动作）。</summary>
        internal static string Spin(Player p, int rounds)
        {
            int done = 0;
            string error = null;
            try
            {
                for (int i = 0; i < rounds; i++)
                {
                    // 每次转 47 度 —— 连续转起来就是每秒上千度
                    float yaw = (i * 47f) % 360f;
                    p.Rotation = Quaternion.Euler(0f, yaw, 0f);
                    done++;
                }
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
            }

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "原地旋转 {0} 次（每次 47 度）\n", done);
            if (error != null)
            {
                sb.AppendLine("  出错: " + error);
            }

            sb.AppendLine();
            sb.AppendLine("人的手腕转不了这么快 —— 反作弊靠采样 Rotation 的变化率来抓。");
            return sb.ToString();
        }

        /// <summary>无规律乱转（抖动 / jitter）。</summary>
        internal static string Jitter(Player p, int rounds)
        {
            var rng = new System.Random();
            int done = 0;
            string error = null;
            try
            {
                for (int i = 0; i < rounds; i++)
                {
                    // 每次都随机换方向 —— 人的手做不到每帧都换
                    float yaw = (float)(rng.NextDouble() * 360.0);
                    float pitch = (float)((rng.NextDouble() * 60.0) - 30.0);
                    p.Rotation = Quaternion.Euler(pitch, yaw, 0f);
                    done++;
                }
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
            }

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "随机抖动 {0} 次\n", done);
            if (error != null)
            {
                sb.AppendLine("  出错: " + error);
            }

            sb.AppendLine();
            sb.AppendLine("人的手不可能每帧都换方向 —— 反作弊靠统计相邻朝向的夹角来抓。");
            return sb.ToString();
        }
    }
}
