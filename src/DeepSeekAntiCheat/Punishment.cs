// ============================================================================
//  AWA :: DeepSeekAntiCheat —— 递进处置
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  阶梯的每一项可以是「处死」也可以是「封禁 N 天」，形如：
//      kill / 3 / 7 / 90 / 365 / perm
//  第 N 次触犯取阶梯的第 N 项，超出长度就按最后一项算。
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>一次处置的描述。</summary>
    internal sealed class Punishment
    {
        /// <summary>是否只是处死（不封禁）。</summary>
        public bool IsKill { get; set; }

        /// <summary>封禁天数。0 = 永久。IsKill 时无意义。</summary>
        public int Days { get; set; }

        /// <summary>人类可读的档位名，例如「处死」「封禁 3 天」「永久封禁」。</summary>
        public string Label { get; set; }

        /// <summary>对应的时间跨度（IsKill 时无意义）。</summary>
        public TimeSpan Duration =>
            this.Days <= 0 ? TimeSpan.FromDays(365 * 100) : TimeSpan.FromDays(this.Days);

        /// <summary>
        /// 解析阶梯里的一项。
        /// 支持：kill（处死）、perm / permanent（永久）、纯数字（天数）。
        /// 解析不出来时按「处死」处理 —— 宁可轻，不可错封。
        /// </summary>
        public static Punishment Parse(string item)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                return new Punishment { IsKill = true, Label = "处死" };
            }

            string s = item.Trim().ToLowerInvariant();

            if (s == "kill" || s == "kick" || s == "处死")
            {
                return new Punishment { IsKill = true, Label = "处死" };
            }

            if (s == "perm" || s == "permanent" || s == "永久" || s == "0")
            {
                return new Punishment { IsKill = false, Days = 0, Label = "永久封禁" };
            }

            if (int.TryParse(s, out int days) && days > 0)
            {
                return new Punishment
                {
                    IsKill = false,
                    Days = days,
                    Label = "封禁 " + days.ToString(CultureInfo.InvariantCulture) + " 天",
                };
            }

            // 解析不出来 —— 用最轻的档
            return new Punishment { IsKill = true, Label = "处死" };
        }

        /// <summary>把整个阶梯渲染成一行，用于公告。</summary>
        public static string DescribeLadder(IList<string> ladder)
        {
            if (ladder == null || ladder.Count == 0)
            {
                return "处死";
            }

            var parts = new List<string>(ladder.Count);
            foreach (string item in ladder)
            {
                parts.Add(Parse(item).Label);
            }

            return string.Join(" → ", parts.ToArray());
        }
    }
}
