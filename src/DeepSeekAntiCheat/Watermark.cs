// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Globalization;
    using System.Reflection;
    using System.Text;

    /// <summary>
    /// AWA 水印。
    ///
    /// 存在的意义：本插件是托管程序集，任何人拿到 DLL 都能反编译成接近原始的 C#。
    /// 水印无法阻止这件事，但能让「这份代码从哪来」有据可查。
    ///
    /// 设计要点：自检用的期望值是用字符码拼出来的，不是字面量。
    /// 所以对 DLL 做一次全局查找替换 "AWA" 摘不干净水印 —— 自检依然会失败。
    /// </summary>
    internal static class AwaWatermark
    {
        /// <summary>水印标识。</summary>
        internal const string Owner = "AWA";

#if !AWA_EDITION_PRIVATE
        /// <summary>开发者。只有公共版的署名与自检需要它。</summary>
        internal const string Developer = "DSH";
#endif

        /// <summary>版本号。Alpha 用英文标注。</summary>
        internal const string EditionVersion = "Alpha v1.0";

        /// <summary>项目名。</summary>
        internal const string Project = "DeepSeekAntiCheat";

        /// <summary>版本标识（由编译开关 AwaEdition 决定）。</summary>
#if AWA_EDITION_PRIVATE
        internal const string Edition = "自用版";
#else
        internal const string Edition = "公共版";
#endif

        /// <summary>
        /// 署名说明。
        /// 两个版本共用同一份逻辑代码，只有这一行文案不同 ——
        /// 由 csproj 的 AwaEdition 开关控制，避免维护两套源码。
        /// </summary>
#if AWA_EDITION_PRIVATE
        internal const string Notice = "AWA 自用版";
#else
        internal const string Notice =
            "作者：AWA　　本插件全由 DSH 开发";
#endif

        /// <summary>
        /// 水印碎片。分散摆放，拼起来是 AWA。
        /// 目的是让「摘水印」变成一件需要通读代码的事，而不是一次替换。
        /// </summary>
        internal static readonly string[] Fragments = { "A", "W", "A" };

        /// <summary>
        /// 自检期望值 —— 用字符码拼出来，不以字面量形式出现在程序集里。
        /// 这样全局替换 "AWA" 不会连带改掉这个期望值，自检仍然能发现篡改。
        /// 65,87,65 == A W A
        /// </summary>
        private static readonly char[] ExpectedCodes = { (char)65, (char)87, (char)65 };

        /// <summary>期望的水印标识（运行时拼装，非字面量）。</summary>
        internal static string ExpectedOwner
        {
            get
            {
                var sb = new StringBuilder(ExpectedCodes.Length);
                foreach (char c in ExpectedCodes)
                {
                    sb.Append(c);
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// 水印自检。返回 null 表示完整；否则返回问题描述。
        /// </summary>
        internal static string Verify()
        {
            // 1) Owner 常量是否被改
            if (!string.Equals(Owner, ExpectedOwner, StringComparison.Ordinal))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "水印标识被改为 \"{0}\"，期望 \"{1}\"",
                    Owner,
                    ExpectedOwner);
            }

            // 2) 署名说明是否还在
            //    公共版要求带开发者署名；自用版文案里本来就没有 DSH，只要求非空。
            if (string.IsNullOrEmpty(Notice))
            {
                return "署名说明被清空";
            }

#if !AWA_EDITION_PRIVATE
            if (Notice.IndexOf(Developer, StringComparison.Ordinal) < 0)
            {
                return "署名说明被修改（少了开发者署名）";
            }
#endif

            // 3) 碎片拼装后是否仍能还原出水印标识
            string rebuilt = string.Concat(Fragments);
            if (!string.Equals(rebuilt, ExpectedOwner, StringComparison.Ordinal))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "水印碎片被修改（拼出 \"{0}\"，期望 \"{1}\"）",
                    rebuilt,
                    ExpectedOwner);
            }

            return null;
        }

        /// <summary>程序集里嵌入的元数据水印（AssemblyMetadata 特性）。</summary>
        internal static string ReadAssemblyMetadata(string key)
        {
            try
            {
                Assembly asm = typeof(AwaWatermark).Assembly;
                foreach (AssemblyMetadataAttribute attr in asm.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                {
                    if (string.Equals(attr.Key, key, StringComparison.Ordinal))
                    {
                        return attr.Value;
                    }
                }
            }
            catch
            {
                // 读不到就算了，不影响主流程
            }

            return null;
        }

        /// <summary>启动横幅（会写进服务端控制台）。</summary>
        internal static string Banner =>
            "\n" +
            "  ╔══════════════════════════════════════════════════════════╗\n" +
            "  ║   " + Owner + "  ::  " + Project + "  [" + Edition + "  " + EditionVersion + "]\n" +
            "  ║   " + Notice + "\n" +
            "  ╚══════════════════════════════════════════════════════════╝";

        /// <summary>一行式水印，便于日志检索。</summary>
        internal static string OneLine =>
            "[" + Owner + "] " + Notice;
    }
}
