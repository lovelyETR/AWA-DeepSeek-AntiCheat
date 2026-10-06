// ============================================================================
//  AWA :: DeepSeekAntiCheat —— 武器与投射物一致性检测
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  【要抓什么】
//  作弊者把武器改得「看起来是一把枪，打出来却是别的东西」——
//  例如拿一挺机枪，射出来的却是榴弹。
//
//  【怎么抓】
//  服务端拿不到「子弹模型」，但能拿到两样东西：
//    1. 玩家手上那把武器【自己声明的】伤害（Firearm.EffectiveDamage / Damage）
//    2. 这一次命中【实际造成的】伤害（ShotEventArgs.Damage）
//  两者应该在同一量级。如果实际伤害远超武器自身声明值，
//  说明打出来的不是这把枪该有的东西。
//
//  【为什么这样不会误伤服务器插件】
//  因为比的是「武器自己的当前数值」，不是「原版数值」。
//  服务器插件把机枪改成榴弹炮时，会同时改掉这把武器的伤害 ——
//  它自己的声明值也跟着变了，所以比值依然正常，不会误报。
//
//  另外还留了两道保险（见 Config）：
//    · custom_weapons 开关：你的服有改武器的插件，可以直接关掉这项
//    · ammo_mismatch_ignore_firearms：点名豁免某些武器
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Globalization;

    using Exiled.API.Features;
    using Exiled.API.Features.Items;

    /// <summary>武器与投射物一致性检测。</summary>
    internal static class WeaponCheck
    {
        /// <summary>一次射击的检查结果。</summary>
        internal struct Result
        {
            /// <summary>是否异常。</summary>
            public bool Suspicious;

            /// <summary>说明（写进日志与证据）。</summary>
            public string Detail;

            /// <summary>武器名。</summary>
            public string Weapon;

            /// <summary>武器声明伤害。</summary>
            public float DeclaredDamage;

            /// <summary>实际伤害。</summary>
            public float ActualDamage;
        }

        /// <summary>
        /// 检查这次射击的伤害是否与手上武器相符。
        /// </summary>
        internal static Result Check(Config cfg, Player shooter, float actualDamage)
        {
            var r = new Result();

            if (cfg == null || !cfg.DetectAmmoMismatch || shooter == null)
            {
                return r;
            }

            Firearm gun;
            try
            {
                gun = shooter.CurrentItem as Firearm;
            }
            catch
            {
                return r;
            }

            // 手上不是枪就不管（可能是投掷物、SCP 能力等）
            if (gun == null)
            {
                return r;
            }

            string name;
            float declared;

            try
            {
                name = gun.FirearmType.ToString();

                // 优先用「有效伤害」——它已经算上了附件的影响
                declared = gun.EffectiveDamage;

                if (declared <= 0f)
                {
                    declared = gun.Damage;
                }
            }
            catch
            {
                return r;
            }

            r.Weapon = name;
            r.DeclaredDamage = declared;
            r.ActualDamage = actualDamage;

            // 声明伤害读不到 —— 不判，免得瞎报
            if (declared <= 0.01f)
            {
                return r;
            }

            // 伤害太小不判（避免浮点误差与护甲减伤造成的小偏差）
            if (actualDamage < cfg.AmmoMismatchMinDamage)
            {
                return r;
            }

            // 豁免名单：你的服有自定义武器就点名放行
            if (cfg.AmmoMismatchIgnoreFirearms != null)
            {
                foreach (string ignore in cfg.AmmoMismatchIgnoreFirearms)
                {
                    if (!string.IsNullOrWhiteSpace(ignore)
                        && name.IndexOf(ignore.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return r;
                    }
                }
            }

            float ratio = cfg.AmmoMismatchDamageRatio > 1f ? cfg.AmmoMismatchDamageRatio : 2.5f;

            if (actualDamage > declared * ratio)
            {
                r.Suspicious = true;
                r.Detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "武器与伤害不符: {0} 声明伤害 {1:F0}，实际打出 {2:F0}（{3:F1} 倍，阈值 {4:F1} 倍）",
                    name, declared, actualDamage, actualDamage / declared, ratio);
            }

            return r;
        }
    }
}
