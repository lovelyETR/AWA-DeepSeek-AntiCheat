// ============================================================================
//  AWA :: 反作弊测试插件 (CheatLab) —— 命令扩展
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  不需要靶子、作用在自己身上的那几项注入的命令入口。
//  拆成独立文件是为了避免主文件被大段字符串拼接改坏。
// ============================================================================

namespace AwaCheatLab
{
    using System;
    using Exiled.API.Features;

    /// <summary>命令处理（扩展部分）。</summary>
    public sealed partial class CheatLabCommand
    {
        /// <summary>
        /// 处理不需要靶子的注入：god / door / spin / jitter。
        /// 目标默认是命令发起者自己。
        /// </summary>
        private static bool SimpleSelf(CheatLabPlugin plugin, string sub, string[] args, out string response)
        {
            if (!plugin.Armed)
            {
                response = "运行时开关是【关】的。先执行 .cl on 再试。";
                return false;
            }

            Player p = CheatLabPlugin.CurrentSender;

            if (args.Length >= 2)
            {
                Player other = CheatLabPlugin.Find(args[1]);
                if (other != null)
                {
                    p = other;
                }
            }

            if (p == null)
            {
                response = "没法确定目标玩家。用法：.cl " + sub + " [玩家昵称]";
                return false;
            }

            int rounds = 30;
            if (args.Length >= 2 && int.TryParse(args[1], out int parsed) && parsed > 0)
            {
                rounds = Math.Min(parsed, 200);
            }
            else if (args.Length >= 3 && int.TryParse(args[2], out int p2) && p2 > 0)
            {
                rounds = Math.Min(p2, 200);
            }

            string result;
            switch (sub)
            {
                case "god":
                    result = Inject.God(p);
                    break;
                case "door":
                    result = Inject.DoorOpen(p);
                    break;
                case "spin":
                    result = Inject.Spin(p, rounds);
                    break;
                default:
                    result = Inject.Jitter(p, rounds);
                    break;
            }

            response = "目标: " + p.Nickname + "\n" + result;
            return true;
        }
    }
}
