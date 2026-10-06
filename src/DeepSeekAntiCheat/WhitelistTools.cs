// ============================================================================
//  AWA :: DeepSeekAntiCheat —— 白名单管理命令
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  白名单有两种用法：
//    1. 改配置文件（features.whitelisted_user_ids / whitelisted_roles）+ 重启
//    2. 用命令动态改（.dsac whitelist add/remove），立即生效并写回配置文件
//
//  这个文件实现第 2 种。
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using Exiled.API.Features;

    /// <summary>白名单管理。</summary>
    internal static class WhitelistTools
    {
        /// <summary>列出当前白名单。</summary>
        internal static string Describe(Config cfg)
        {
            ServerFeatures f = cfg.Features ?? new ServerFeatures();
            var sb = new StringBuilder();

            sb.AppendLine("AWA 反作弊  白名单");
            sb.AppendLine();
            sb.AppendLine("  白名单里的玩家 / 角色完全不参与检测 ——");
            sb.AppendLine("  不是「检测到但不处置」，而是压根不进检测流程。");
            sb.AppendLine();

            sb.AppendLine("  ── 玩家白名单（UserID）──");
            if (f.WhitelistedUserIds == null || f.WhitelistedUserIds.Count == 0)
            {
                sb.AppendLine("    （空）");
            }
            else
            {
                foreach (string id in f.WhitelistedUserIds)
                {
                    string nick = "?";
                    try
                    {
                        foreach (Player p in Player.List)
                        {
                            if (p != null && p.IsConnected
                                && string.Equals(p.UserId, id, StringComparison.OrdinalIgnoreCase))
                            {
                                nick = p.Nickname;
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略
                    }

                    sb.AppendFormat(CultureInfo.InvariantCulture, "    {0}   ({1})\n", id, nick);
                }
            }

            sb.AppendLine();
            sb.AppendLine("  ── 角色白名单（名字包含即匹配）──");
            if (f.WhitelistedRoles == null || f.WhitelistedRoles.Count == 0)
            {
                sb.AppendLine("    （空）");
            }
            else
            {
                foreach (string r in f.WhitelistedRoles)
                {
                    sb.AppendLine("    " + r);
                }
            }

            sb.AppendLine();
            sb.AppendLine("  管理命令（立即生效，并写回配置文件）：");
            sb.AppendLine("    .dsac whitelist add <UserID或玩家昵称>");
            sb.AppendLine("    .dsac whitelist remove <UserID>");
            sb.AppendLine("    .dsac whitelist addrole <角色名>");
            sb.AppendLine("    .dsac whitelist removerole <角色名>");
            sb.AppendLine("    .dsac whitelist clear");
            sb.AppendLine();
            sb.AppendLine("  UserID 形如 76561199570869496@steam");
            sb.AppendLine("  （游戏内 .dsac stats 能看到，RA 日志里括号里也是）");

            return sb.ToString();
        }

        /// <summary>增删玩家白名单。返回给玩家看的消息。</summary>
        internal static string EditUser(Config cfg, string query, bool add)
        {
            ServerFeatures f = cfg.Features;
            if (f == null)
            {
                return "配置里没有 features 段。";
            }

            if (f.WhitelistedUserIds == null)
            {
                f.WhitelistedUserIds = new List<string>();
            }

            string uid = ResolveUserId(query);
            if (string.IsNullOrEmpty(uid))
            {
                return "找不到这个玩家：" + query +
                       "\n（要填 UserID，形如 76561199570869496@steam，或在线玩家的完整昵称）";
            }

            if (add)
            {
                foreach (string x in f.WhitelistedUserIds)
                {
                    if (string.Equals(x, uid, StringComparison.OrdinalIgnoreCase))
                    {
                        return uid + " 已经在白名单里了。";
                    }
                }

                f.WhitelistedUserIds.Add(uid);
                Save(cfg);
                return "已加入白名单：" + uid + "（立即生效，不用重启）";
            }

            for (int i = 0; i < f.WhitelistedUserIds.Count; i++)
            {
                if (string.Equals(f.WhitelistedUserIds[i], uid, StringComparison.OrdinalIgnoreCase))
                {
                    f.WhitelistedUserIds.RemoveAt(i);
                    Save(cfg);
                    return "已移出白名单：" + uid + "（立即生效）";
                }
            }

            return uid + " 不在白名单里。";
        }

        /// <summary>增删角色白名单。</summary>
        internal static string EditRole(Config cfg, string role, bool add)
        {
            ServerFeatures f = cfg.Features;
            if (f == null)
            {
                return "配置里没有 features 段。";
            }

            if (string.IsNullOrWhiteSpace(role))
            {
                return "要填角色名，例如 Scp049、Scp096。";
            }

            role = role.Trim();

            if (f.WhitelistedRoles == null)
            {
                f.WhitelistedRoles = new List<string>();
            }

            if (add)
            {
                foreach (string x in f.WhitelistedRoles)
                {
                    if (string.Equals(x, role, StringComparison.OrdinalIgnoreCase))
                    {
                        return role + " 已经在角色白名单里了。";
                    }
                }

                f.WhitelistedRoles.Add(role);
                Save(cfg);
                return "已加入角色白名单：" + role + "（立即生效，不用重启）";
            }

            for (int i = 0; i < f.WhitelistedRoles.Count; i++)
            {
                if (string.Equals(f.WhitelistedRoles[i], role, StringComparison.OrdinalIgnoreCase))
                {
                    f.WhitelistedRoles.RemoveAt(i);
                    Save(cfg);
                    return "已移出角色白名单：" + role + "（立即生效）";
                }
            }

            return role + " 不在角色白名单里。";
        }

        /// <summary>清空白名单。</summary>
        internal static string Clear(Config cfg)
        {
            ServerFeatures f = cfg.Features;
            if (f == null)
            {
                return "配置里没有 features 段。";
            }

            int n = (f.WhitelistedUserIds != null ? f.WhitelistedUserIds.Count : 0)
                  + (f.WhitelistedRoles != null ? f.WhitelistedRoles.Count : 0);

            f.WhitelistedUserIds = new List<string>();
            f.WhitelistedRoles = new List<string>();
            Save(cfg);
            return "白名单已清空（原有 " + n + " 项）。";
        }

        /// <summary>
        /// 把 UserID 或在线玩家昵称解析成 UserID。
        /// </summary>
        private static string ResolveUserId(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            query = query.Trim();

            // 看着像 UserID 就直接用
            if (query.IndexOf('@') > 0)
            {
                return query;
            }

            // 否则按昵称找在线玩家
            try
            {
                foreach (Player p in Player.List)
                {
                    if (p != null && p.IsConnected
                        && string.Equals(p.Nickname, query, StringComparison.OrdinalIgnoreCase))
                    {
                        return p.UserId;
                    }
                }

                foreach (Player p in Player.List)
                {
                    if (p != null && p.IsConnected && p.Nickname != null
                        && p.Nickname.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return p.UserId;
                    }
                }
            }
            catch
            {
                // 忽略
            }

            return null;
        }

        /// <summary>
        /// 把白名单写回配置文件。
        ///
        /// 只改 features 段里的两行，其他内容一个字不动 ——
        /// 所以你的 API Key 和其他设置不会被碰。
        /// </summary>
        private static void Save(Config cfg)
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EXILED", "Configs", "Plugins", "deepseek_anticheat",
                    Server.Port + ".yml");

                if (!File.Exists(path))
                {
                    Log.Warn("[AWA] 找不到配置文件，白名单只改了内存里的（重启会丢）: " + path);
                    return;
                }

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                var outLines = new List<string>(lines.Length + 16);
                ServerFeatures f = cfg.Features;

                // 先把白名单块渲染出来
                var uidBlock = new List<string> { "  whitelisted_user_ids:" };
                if (f.WhitelistedUserIds != null && f.WhitelistedUserIds.Count > 0)
                {
                    foreach (string x in f.WhitelistedUserIds)
                    {
                        uidBlock.Add("  - " + x);
                    }
                }
                else
                {
                    uidBlock[0] = "  whitelisted_user_ids: []";
                }

                var roleBlock = new List<string> { "  whitelisted_roles:" };
                if (f.WhitelistedRoles != null && f.WhitelistedRoles.Count > 0)
                {
                    foreach (string x in f.WhitelistedRoles)
                    {
                        roleBlock.Add("  - " + x);
                    }
                }
                else
                {
                    roleBlock[0] = "  whitelisted_roles: []";
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line.StartsWith("  whitelisted_user_ids:", StringComparison.Ordinal))
                    {
                        outLines.AddRange(uidBlock);

                        // 跳过后面长的列表项
                        while (i + 1 < lines.Length && lines[i + 1].StartsWith("  - ", StringComparison.Ordinal))
                        {
                            i++;
                        }

                        continue;
                    }

                    if (line.StartsWith("  whitelisted_roles:", StringComparison.Ordinal))
                    {
                        outLines.AddRange(roleBlock);
                        while (i + 1 < lines.Length && lines[i + 1].StartsWith("  - ", StringComparison.Ordinal))
                        {
                            i++;
                        }

                        continue;
                    }

                    outLines.Add(line);
                }

                File.WriteAllLines(path, outLines.ToArray(), new UTF8Encoding(false));
                Log.Info("[AWA] 白名单已写回配置文件: " + path);
            }
            catch (Exception e)
            {
                Log.Warn("[AWA] 白名单写回配置失败（内存里的已生效）: " + e.Message);
            }
        }
    }
}
