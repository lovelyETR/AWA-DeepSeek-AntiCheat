// ============================================================================
//  AWA :: 反作弊测试插件 —— 权限管理
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  只有「服务器所有者」和「管理员」能用这个插件。
//  普通玩家、以及权限不够的人，一律拒绝。
//
//  三层认定（任何一层通过就放行）：
//    1. 服务器所有者  —— 服务端控制台，或者游戏里的主机（IsHost）
//    2. 白名单        —— 配置里点名允许的 UserID
//    3. 管理员        —— 有 RemoteAdmin 权限的人（可选再要求具体权限）
//
//  为什么拦在命令层：
//    这是唯一入口。所有注入都从 .cl 命令进，
//    拦在这里就等于拦住了全部能力。
//
//  【可测试性】
//    Decide() 是纯逻辑，只吃 bool 和 string，不碰 EXILED 类型。
//    所以可以用离线程序把「所有身份组合」跑一遍验证。
//    IsAllowed() 只负责从 EXILED 类型收集输入，然后交给 Decide()。
// ============================================================================

namespace AwaCheatLab
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    using CommandSystem;

    using Exiled.API.Features;

    using RemoteAdmin;

    /// <summary>测试插件的权限判定。</summary>
    internal static class CheatLabPermission
    {
        // ─────────────────────────────────────────────────────────────────────
        //  纯逻辑层（可离线测试）
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 权限判定的纯逻辑。
        /// </summary>
        /// <param name="cfg">配置。</param>
        /// <param name="isConsole">是不是服务端控制台。</param>
        /// <param name="isHost">是不是服务器所有者（游戏里的主机）。</param>
        /// <param name="hasRa">有没有 RemoteAdmin 权限。</param>
        /// <param name="userId">UserID（查白名单用）。</param>
        /// <param name="meetsExtra">是否满足额外的具体 RA 权限要求。</param>
        /// <param name="reason">输出：判定依据。</param>
        /// <returns>放行返回 true。</returns>
        internal static bool Decide(
            CheatLabConfig cfg,
            bool isConsole,
            bool isHost,
            bool hasRa,
            string userId,
            bool meetsExtra,
            out string reason)
        {
            reason = "未知";

            if (cfg == null)
            {
                reason = "没有配置";
                return false;
            }

            // ① 服务端控制台 = 服务器所有者，永远放行
            if (isConsole)
            {
                reason = "服务端控制台（服务器所有者）";
                return true;
            }

            if (string.IsNullOrEmpty(userId))
            {
                reason = "无法识别发起人";
                return false;
            }

            // ② 服务器所有者（游戏里的主机）
            if (cfg.AllowServerOwner && isHost)
            {
                reason = "服务器所有者（主机）";
                return true;
            }

            // ③ 白名单
            if (cfg.WhitelistedUserIds != null)
            {
                foreach (string id in cfg.WhitelistedUserIds)
                {
                    if (!string.IsNullOrWhiteSpace(id)
                        && string.Equals(id.Trim(), userId, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "白名单";
                        return true;
                    }
                }
            }

            // ④ 管理员
            if (cfg.AllowRemoteAdmin && hasRa)
            {
                if (meetsExtra)
                {
                    reason = "管理员";
                    return true;
                }

                reason = "是管理员，但缺少配置要求的 RA 权限";
                return false;
            }

            reason = hasRa ? "有 RA 但被配置禁止" : "普通玩家（无权限）";
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  收集层（从 EXILED 类型取输入）
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>判断发起者有没有资格使用测试插件。</summary>
        internal static bool IsAllowed(CheatLabConfig cfg, ICommandSender sender, out string who)
        {
            who = "未知";

            if (cfg == null || sender == null)
            {
                return false;
            }

            bool isConsole = !(sender is PlayerCommandSender);
            if (isConsole)
            {
                return Decide(cfg, true, false, false, null, true, out who);
            }

            Player p = CheatLabPlugin.Resolve(sender);
            if (p == null)
            {
                who = "无法识别发起人";
                return false;
            }

            if (!p.IsConnected)
            {
                who = p.Nickname + "（已离线）";
                return false;
            }

            bool isHost = false;
            bool hasRa = false;

            try
            {
                isHost = p.IsHost;
            }
            catch
            {
                // 忽略
            }

            if (!isHost && Server.Host != null && Server.Host.IsConnected)
            {
                isHost = string.Equals(Server.Host.UserId, p.UserId, StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                hasRa = p.RemoteAdminAccess;
            }
            catch
            {
                // 忽略
            }

            bool meetsExtra = MeetsRequiredPermissions(cfg, p, out string missing);

            bool allowed = Decide(cfg, false, isHost, hasRa, p.UserId, meetsExtra, out string reason);

            who = p.Nickname + " (" + p.UserId + ") —— " + reason;
            if (missing != null)
            {
                who += "（缺: " + missing + "）";
            }

            return allowed;
        }

        /// <summary>检查是否具备配置里要求的那些具体 RA 权限。</summary>
        private static bool MeetsRequiredPermissions(CheatLabConfig cfg, Player p, out string missing)
        {
            missing = null;

            if (cfg.RequireRaPermissions == null || cfg.RequireRaPermissions.Count == 0)
            {
                return true;
            }

            PlayerPermissions required = 0;
            var unknown = new List<string>();

            foreach (string name in cfg.RequireRaPermissions)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                try
                {
                    required |= (PlayerPermissions)Enum.Parse(
                        typeof(PlayerPermissions), name.Trim(), true);
                }
                catch
                {
                    unknown.Add(name.Trim());
                }
            }

            if (unknown.Count > 0)
            {
                // 名字写错只记日志，不因此拒绝 —— 否则一个错字会把管理员全挡在外面
                Log.Warn("[CheatLab] require_ra_permissions 里有认不出的权限名: "
                         + string.Join(", ", unknown));
            }

            if (required == 0)
            {
                return true;
            }

            try
            {
                PlayerPermissions have = p.RemoteAdminPermissions;
                PlayerPermissions lack = required & ~have;
                if (lack == 0)
                {
                    return true;
                }

                missing = lack.ToString();
                return false;
            }
            catch
            {
                return true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  展示
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>被拒绝时给玩家的提示。</summary>
        internal static string DenyMessage(CheatLabConfig cfg)
        {
            string msg = cfg != null ? cfg.DenyMessage : null;
            if (string.IsNullOrWhiteSpace(msg))
            {
                msg = "你没有权限使用这个插件。";
            }

            return msg
                 + "\n\n"
                 + "它只对服务器所有者和管理员开放。\n"
                 + "如果你确实需要用它来测试反作弊，请联系服务器管理员。";
        }

        /// <summary>把当前权限设置渲染成一张表。</summary>
        internal static string Describe(CheatLabConfig cfg)
        {
            var sb = new StringBuilder();

            sb.AppendLine("测试插件 权限设置");
            sb.AppendLine();
            sb.AppendLine("  能用的人（满足任一即可）：");
            sb.AppendLine("    " + Mark(cfg.AllowServerOwner) + " 服务器所有者（服务端控制台 / 主机）");
            sb.AppendLine("    " + Mark(cfg.AllowRemoteAdmin) + " 管理员（有 RemoteAdmin 权限的人）");

            var ids = cfg.WhitelistedUserIds ?? new List<string>();
            sb.AppendLine("    " + (ids.Count > 0 ? "[√]" : "[ ]")
                          + " 白名单玩家（" + ids.Count + " 人）");
            foreach (string id in ids)
            {
                sb.AppendLine("         " + id);
            }

            var need = cfg.RequireRaPermissions ?? new List<string>();
            if (need.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  额外要求（管理员还要具备这些具体 RA 权限）：");
                foreach (string n in need)
                {
                    sb.AppendLine("         " + n);
                }
            }

            sb.AppendLine();
            sb.AppendLine("  .cl about 是否对所有人开放：" + Mark(cfg.AllowAboutForEveryone));
            sb.AppendLine("  拒绝时是否记日志：" + Mark(cfg.LogDenials));
            sb.AppendLine();
            sb.AppendLine("  你当前的身份：" + DescribeSelf());

            return sb.ToString();
        }

        /// <summary>描述当前命令发起者的身份。</summary>
        internal static string DescribeSelf()
        {
            Player p = CheatLabPlugin.CurrentSender;
            if (p == null)
            {
                return "服务端控制台（服务器所有者）";
            }

            var parts = new List<string> { p.Nickname, p.UserId };

            try
            {
                if (p.IsHost)
                {
                    parts.Add("服务器所有者");
                }
                else if (p.RemoteAdminAccess)
                {
                    parts.Add("管理员");
                }
                else
                {
                    parts.Add("普通玩家（无权限）");
                }
            }
            catch
            {
                // 忽略
            }

            return string.Join("  ", parts);
        }

        private static string Mark(bool on)
        {
            return on ? "[√]" : "[ ]";
        }
    }
}
