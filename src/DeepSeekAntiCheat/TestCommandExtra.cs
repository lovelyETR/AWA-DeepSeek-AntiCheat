// ============================================================================
//  AWA :: DeepSeekAntiCheat —— 命令扩展
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  服务器特性白名单查询 + 累犯封禁台账查询。
//  拆成独立文件，避免主命令文件被大段字符串拼接改坏。
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>命令处理（扩展部分）。</summary>
    public sealed partial class TestCommand
    {
        /// <summary>白名单管理。</summary>
        private static bool Whitelist(DeepSeekAntiCheatPlugin plugin, string[] args, out string response)
        {
            Config cfg = plugin.Config;

            if (args.Length < 2)
            {
                response = WhitelistTools.Describe(cfg);
                return true;
            }

            string op = args[1].ToLowerInvariant();

            if (op == "clear")
            {
                response = WhitelistTools.Clear(cfg);
                return true;
            }

            bool add = op == "add" || op == "addrole";
            bool isRole = op == "addrole" || op == "removerole";

            if (op != "add" && op != "remove" && op != "addrole" && op != "removerole")
            {
                response = "未知操作: " + op + "\n\n" + WhitelistTools.Describe(cfg);
                return false;
            }

            if (args.Length < 3)
            {
                response = "用法：.dsac whitelist " + op + (isRole ? " <角色名>" : " <UserID或在线玩家昵称>");
                return false;
            }

            response = isRole
                ? WhitelistTools.EditRole(cfg, args[2], add)
                : WhitelistTools.EditUser(cfg, args[2], add);
            return true;
        }

        /// <summary>显示当前服务器特性白名单的配置状态。</summary>
        private static string FeaturesTable(Config cfg)
        {
            ServerFeatures f = cfg.Features ?? new ServerFeatures();
            var sb = new StringBuilder();

            sb.AppendLine("AWA 反作弊插件  " + AwaWatermark.EditionVersion + "  [" + AwaWatermark.Edition + "]");
            sb.AppendLine();
            sb.AppendLine("服务器特性白名单（当前生效的配置）");
            sb.AppendLine();
            sb.AppendLine("  开服时按你的玩法填一次。填错会误报，改配置后要重启。");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "无限体力/加速", f.InfiniteStamina ? "开 —— 移速检测已关闭" : "关 —— 移速正常检测");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "无限子弹", f.InfiniteAmmo ? "开 —— 弹药检测已关闭" : "关 —— 弹药正常检测");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "过场/通话上下飞", f.CutsceneFlight ? "开 —— 垂直检测已关闭" : "关 —— 垂直正常检测");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "自定义传送", f.CustomTeleport ? "开 —— 瞬移检测已关闭" : "关 —— 瞬移正常检测");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "异常治疗类技能", f.AbnormalHeal ? "开 —— 回血检测已关闭" : "关 —— 回血正常检测");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "服务器主动发物品", f.GivesItems ? "开 —— 刷物品检测已关闭" : "关 —— 刷物品正常检测");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-22} {1}\n", "管理员可用 NoClip", f.NoclipForAdmins ? "开 —— 管理员豁免" : "关 —— 管理员也检");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "  容差倍率  移速 {0:F2} / 垂直 {1:F2} / 刷物品 {2:F2}\n",
                f.SpeedToleranceMultiplier, f.VerticalToleranceMultiplier, f.ItemSpamToleranceMultiplier);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  白名单    UserID {0} 人 / 角色 {1} 个\n",
                f.WhitelistedUserIds != null ? f.WhitelistedUserIds.Count : 0,
                f.WhitelistedRoles != null ? f.WhitelistedRoles.Count : 0);

            if (!f.InfiniteStamina && !f.InfiniteAmmo && !f.CutsceneFlight
                && !f.CustomTeleport && !f.AbnormalHeal && !f.GivesItems)
            {
                sb.AppendLine();
                sb.AppendLine("  以上全是默认值。如果你的服有上面任何一项特性，");
                sb.AppendLine("  请到配置文件最上面的 features: 段里打开对应项。");
            }

            return sb.ToString();
        }

        /// <summary>查看 / 重置累犯封禁台账。</summary>
        private static bool Bans(DeepSeekAntiCheatPlugin plugin, string[] args, out string response)
        {
            if (args.Length >= 2 && string.Equals(args[1], "reset", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3)
                {
                    response = "用法：.dsac bans reset <玩家UserID>\n（UserID 形如 76561199570869496@steam）";
                    return false;
                }

                bool ok = plugin.Ledger.Reset(args[2]);
                response = ok
                    ? "已把 " + args[2] + " 的封禁次数清零。他下次再被判定，按第 1 次算。"
                    : "台账里没有 " + args[2] + "。";
                return ok;
            }

            response = plugin.Ledger.Describe() +
                       "\n封错了要撤销：.dsac bans reset <玩家UserID>\n" +
                       "（清零后他下次再犯按第 1 次算）";
            return true;
        }
    }
}
