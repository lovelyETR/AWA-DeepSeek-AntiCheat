// ============================================================================
//  AWA :: DeepSeekAntiCheat —— 服务器特性白名单
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  【为什么需要这个】
//  每个服务器的玩法都不一样。有些开了无限体力、无限子弹，
//  有些有过场/通话需要玩家上下飞，有些会主动给玩家发补给。
//  这些「服务器特性」产生的数据，和作弊产生的数据长得一模一样 ——
//  不告诉插件的话，就会误报。
//
//  【怎么用】
//  在配置里把符合你服务器的那一项设成 true。
//  每一项只影响它对应的那个检测器，其他照常工作。
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;

    /// <summary>
    /// 服务器特性白名单。
    ///
    /// 这些开关是给「服主在开服时填一次」用的 —— 填对一次，之后就不用管。
    /// </summary>
    public sealed class ServerFeatures
    {
        // ───────────── 玩法特性 ─────────────

        [Description(
            "【无限体力 / 高移速插件】开启后放宽移速检测。\n" +
            "有些服务器开了无限体力或加速插件，玩家能长时间高速跑动 —— \n" +
            "不开这一项的话，正常玩家会被判成「加速外挂」。")]
        public bool InfiniteStamina { get; set; } = false;

        [Description(
            "【无限子弹】开启后关闭「无限弹药」检测。\n" +
            "开了无限子弹的服，玩家本来就不用换弹 —— 这项检测必然误报。")]
        public bool InfiniteAmmo { get; set; } = false;

        [Description(
            "【过场 / 通话需要上下飞】开启后关闭「飞天 / 垂直速度」检测。\n" +
            "有些服务器用过场动画或通话功能把玩家抬起来 —— 那是脚本行为，不是作弊。")]
        public bool CutsceneFlight { get; set; } = false;

        [Description(
            "【自定义传送 / 传送门】开启后放宽「瞬移」检测。\n" +
            "自定义传送会把玩家瞬间挪很远 —— 和被改位置长得一样。")]
        public bool CustomTeleport { get; set; } = false;

        [Description(
            "【异常治疗类技能 / 道具】开启后关闭「异常回血」检测。\n" +
            "有些自定义角色或道具本来就一次回满血。")]
        public bool AbnormalHeal { get; set; } = false;

        [Description(
            "【服务器会主动发物品】开启后放宽「刷物品」检测。\n" +
            "补给、奖励、开局发装备都会触发刷物品判定 —— 会误清玩家背包。")]
        public bool GivesItems { get; set; } = false;

        [Description(
            "【允许管理员用 NoClip】开启后对管理员跳过 NoClip 检测。\n" +
            "管理员用 NoClip 是正常操作，不该被判作弊。建议保持开启。")]
        public bool NoclipForAdmins { get; set; } = true;

        [Description(
            "【允许管理员用上帝模式】开启后对管理员跳过「无敌」相关推断。")]
        public bool GodModeForAdmins { get; set; } = true;

        // ───────────── 更宽松的阈值 ─────────────

        [Description(
            "移速上限倍率。服务器有加速插件但不想完全关掉移速检测时，把这个调大。\n" +
            "例如填 2.0 = 允许到正常上限的两倍。填 1.0 = 用默认阈值。")]
        public float SpeedToleranceMultiplier { get; set; } = 1f;

        [Description(
            "垂直速度上限倍率。和上面同理，用于「上下飞但不想完全关检测」的情况。")]
        public float VerticalToleranceMultiplier { get; set; } = 1f;

        [Description(
            "刷物品阈值倍率。服务器偶尔发物品但不想完全关检测时调大这个。")]
        public float ItemSpamToleranceMultiplier { get; set; } = 1f;

        // ───────────── 白名单 ─────────────

        [Description(
            "完全跳过检测的玩家 UserID 白名单（形如 76561199570869496@steam）。\n" +
            "适合：服主自己的号、测试号、已知的特殊玩家。")]
        public List<string> WhitelistedUserIds { get; set; } = new List<string>();

        [Description(
            "完全跳过检测的角色名白名单（例如 Scp049、Scp096）。\n" +
            "这些角色天生就「数据异常」，不该按普通玩家标准判。")]
        public List<string> WhitelistedRoles { get; set; } = new List<string>();

        /// <summary>这个玩家是否在白名单里（按 UserID）。</summary>
        public bool IsWhitelisted(string userId)
        {
            if (string.IsNullOrEmpty(userId) || this.WhitelistedUserIds == null)
            {
                return false;
            }

            foreach (string id in this.WhitelistedUserIds)
            {
                if (!string.IsNullOrEmpty(id)
                    && string.Equals(id.Trim(), userId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>这个角色是否在白名单里。</summary>
        public bool IsRoleWhitelisted(string roleName)
        {
            if (string.IsNullOrEmpty(roleName) || this.WhitelistedRoles == null)
            {
                return false;
            }

            foreach (string r in this.WhitelistedRoles)
            {
                if (!string.IsNullOrEmpty(r)
                    && roleName.IndexOf(r.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
