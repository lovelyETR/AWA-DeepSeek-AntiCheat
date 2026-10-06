// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  真实性钻取（drill）：在服务端真实地制造作弊条件，验证检测器是否生效。
//
//  它不注入进程、不碰内存、不伪造网络包 —— 只是改服务端状态。
//  因此它走的是完整真实链路：真实事件 -> 真实统计 -> 真实评分 -> 真实送 AI。
//  也正因为如此，它不可能被用来在服务器上作弊。
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Exiled.API.Features;
    using UnityEngine;

    /// <summary>钻取项的定义与执行。</summary>
    internal static class Drill
    {
        /// <summary>一个钻取项。</summary>
        internal sealed class Item
        {
            public string Id { get; set; }

            public string Name { get; set; }

            /// <summary>对应的检测器名（用于报告）。</summary>
            public string Detector { get; set; }

            /// <summary>执行钻取。返回描述；失败返回 null。</summary>
            public Func<Player, Player, Config, string> Run { get; set; }

            /// <summary>是否需要指定一个目标玩家（射击类钻取需要）。</summary>
            public bool NeedsTarget { get; set; }

            /// <summary>执行前记录的现场，用于恢复。</summary>
            public Func<Player, object> Snapshot { get; set; }

            /// <summary>恢复现场。</summary>
            public Action<Player, object> Restore { get; set; }
        }

        /// <summary>
        /// 服务端驱动一个玩家真实开火。
        ///
        /// 走的是 HitscanHitregModuleBase.Fire(primaryTarget, shotEvent) ——
        /// 也就是 EXILED 的 Shot 事件挂钩的那个方法，所以触发的是完整真实链路：
        /// 真实弹道、真实伤害、真实 ShotEventArgs。
        ///
        /// 它没有客户端能力：只是服务端调用游戏自己的开火函数，
        /// 拿到别的服务器上什么都做不了。
        /// </summary>
        private static string FireAt(Player shooter, Player target, Config cfg, int barrels, int rounds)
        {
            var firearm = shooter.CurrentItem as Exiled.API.Features.Items.Firearm;
            if (firearm == null)
            {
                return "目标当前拿的不是枪（需要先给一把枪并拿在手上）。";
            }

            var module = firearm.HitscanHitregModule;
            if (module == null)
            {
                return "这把武器没有 hitscan 开火模块。";
            }

            // 目标：默认打自己（能产生真实命中且不误伤别人），指定了就打指定的人
            Player victim = target ?? shooter;

            var identifier = new InventorySystem.Items.ItemIdentifier(firearm.Type, firearm.Serial);
            int fired = 0;
            for (int i = 0; i < rounds; i++)
            {
                try
                {
                    module.Fire(victim.ReferenceHub, new InventorySystem.Items.Firearms.ShotEvents.BulletShotEvent(identifier, barrels));
                    fired++;
                }
                catch (Exception e)
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "第 {0} 发失败: {1}: {2}（已成功 {3} 发）",
                        i + 1, e.GetType().Name, e.Message, fired);
                }
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "已通过 HitscanHitregModule.Fire() 真实开火 {0} 发（武器 {1}，目标 {2}）",
                fired, firearm.Type, victim.Nickname);
        }

        /// <summary>全部钻取项。</summary>
        internal static IList<Item> All() => new List<Item>
        {
            new Item
            {
                Id = "noclip",
                Name = "开启 NoClip",
                Detector = "NoClip（服务端状态位）",
                Snapshot = p => new object[] { p.IsNoclipEnabled, p.IsNoclipPermitted },
                Run = (p, t, cfg) =>
                {
                    p.IsNoclipEnabled = true;
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "IsNoclipEnabled 已设为 true（IsNoclipPermitted={0}）",
                        p.IsNoclipPermitted);
                },
                Restore = (p, s) =>
                {
                    var snap = (object[])s;
                    try { p.IsNoclipEnabled = (bool)snap[0]; } catch { }
                },
            },

            new Item
            {
                Id = "teleport",
                Name = "瞬移到远处",
                Detector = "瞬移 / 速度异常（位置采样）",
                Snapshot = p => p.Position,
                Run = (p, t, cfg) =>
                {
                    Vector3 from = p.Position;
                    // 抬到高空并横移，制造「人不可能到达」的位移
                    var to = new Vector3(from.x + 60f, from.y + 25f, from.z + 60f);
                    p.Position = to;
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "位置从 ({0:F0},{1:F0},{2:F0}) 改为 ({3:F0},{4:F0},{5:F0})，位移约 {6:F0} 米",
                        from.x, from.y, from.z, to.x, to.y, to.z, Vector3.Distance(from, to));
                },
                Restore = (p, s) =>
                {
                    try { p.Position = (Vector3)s; } catch { }
                },
            },

            new Item
            {
                Id = "vertical",
                Name = "垂直飞天",
                Detector = "飞天/悬浮（垂直速度）",
                Snapshot = p => p.Position,
                Run = (p, t, cfg) =>
                {
                    Vector3 from = p.Position;
                    p.Position = new Vector3(from.x, from.y + 40f, from.z);
                    return string.Format(CultureInfo.InvariantCulture, "位置 Y 从 {0:F1} 抬到 {1:F1}", from.y, from.y + 40f);
                },
                Restore = (p, s) =>
                {
                    try { p.Position = (Vector3)s; } catch { }
                },
            },

            new Item
            {
                Id = "shoot",
                Name = "服务端驱动真实开火",
                Detector = "命中率 / 射速 / 伤害（真实 Shot 事件）",
                NeedsTarget = true,
                Snapshot = p => null,
                Run = (p, t, cfg) => FireAt(p, t, cfg, 1, 6),
                Restore = (p, s) => { },
            },

            new Item
            {
                Id = "rapidfire",
                Name = "服务端驱动连发（射速异常）",
                Detector = "射速异常（峰值射速 / 硬违规）",
                NeedsTarget = true,
                Snapshot = p => null,
                Run = (p, t, cfg) => FireAt(p, t, cfg, 1, 40),
                Restore = (p, s) => { },
            },
            new Item
            {
                Id = "items",
                Name = "刷一堆物品",
                Detector = "可疑物品获取（ItemAdded）",
                Snapshot = p => null,
                Run = (p, t, cfg) =>
                {
                    int n = 0;
                    ItemType[] types =
                    {
                        ItemType.KeycardO5, ItemType.Medkit, ItemType.Painkillers,
                        ItemType.Radio, ItemType.Flashlight, ItemType.Coin,
                        ItemType.ArmorHeavy, ItemType.GrenadeFlash, ItemType.SCP268,
                        ItemType.SCP500, ItemType.Adrenaline, ItemType.SCP207,
                    };

                    foreach (ItemType itemType in types)
                    {
                        try
                        {
                            p.AddItem(itemType);
                            n++;
                        }
                        catch
                        {
                            // 某些物品在特定角色下不能给，跳过
                        }
                    }

                    return string.Format(CultureInfo.InvariantCulture, "给了 {0} 件物品", n);
                },
                Restore = (p, s) => { },
            },

            new Item
            {
                Id = "heal",
                Name = "异常治疗",
                Detector = "异常回血（Healed 事件）",
                Snapshot = p => p.Health,
                Run = (p, t, cfg) =>
                {
                    float before = p.Health;
                    // Heal 会触发 Healed 事件；给一个远超配置上限的量
                    p.Heal(cfg.MaxReasonableHeal * 3f, true);
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "血量从 {0:F0} 治疗到 {1:F0}（注入了 {2:F0}）",
                        before, p.Health, cfg.MaxReasonableHeal * 3f);
                },
                Restore = (p, s) =>
                {
                    try { p.Health = (float)s; } catch { }
                },
            },
        };

        /// <summary>按 id 找钻取项。</summary>
        internal static Item Find(string id)
        {
            foreach (Item i in All())
            {
                if (string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return null;
        }

        /// <summary>列出全部钻取项。</summary>
        internal static string List()
        {
            var sb = new StringBuilder();
            sb.AppendLine("真实性钻取 —— 在服务端真实制造作弊条件，验证检测器");
            sb.AppendLine("（游戏内输入记得加点：.dsac drill ...）");
            sb.AppendLine();
            sb.AppendLine("用法：dsac drill <id>     对你自己执行一次（会尝试恢复现场）");
            sb.AppendLine("      dsac drill <id> <玩家>  对指定玩家执行");
            sb.AppendLine();

            foreach (Item i in All())
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-10} {1,-14} -> {2}\n", i.Id, i.Name, i.Detector);
            }

            sb.AppendLine();
            sb.AppendLine("执行后按 ~ 打开控制台看服务端日志，或用 dsac stats 看统计有没有变化。");
            sb.AppendLine();
            sb.AppendLine("注意：这些钻取改的是服务端状态，不是注入客户端。");
            sb.AppendLine("      所以它无法被用来在服务器上作弊 —— 它只是让检测器有东西可抓。");
            return sb.ToString();
        }

        /// <summary>执行一个钻取项。</summary>
        internal static string Execute(Item item, Player subject, Player victim, Config cfg)
        {
            if (item == null || subject == null)
            {
                return null;
            }

            object snapshot = null;
            try
            {
                snapshot = item.Snapshot?.Invoke(subject);
            }
            catch
            {
                // 快照失败不影响执行
            }

            string result;
            try
            {
                result = item.Run(subject, victim, cfg);
            }
            catch (Exception e)
            {
                return "执行失败: " + e.GetType().Name + ": " + e.Message;
            }

            // 隔一拍再恢复，让检测器有机会采样到
            try
            {
                MEC.Timing.CallDelayed(1.5f, () =>
                {
                    try
                    {
                        item.Restore?.Invoke(subject, snapshot);
                        Log.Info(string.Format(
                            CultureInfo.InvariantCulture,
                            "[AWA drill] 已恢复现场（{0}）", item.Id));
                    }
                    catch
                    {
                        // 玩家可能已离线
                    }
                });
            }
            catch
            {
                // MEC 不可用时就不延迟恢复了
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}\n钻取项「{1}」已执行。等待约 2 秒让检测器采样，然后执行 dsac report 查看结果。",
                result, item.Name);
        }
    }
}
