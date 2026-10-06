// ============================================================================
//  测试插件 权限逻辑 —— 离线验证
//  作者：AWA　　本插件全由 DSH 开发
//
//  做法：直接加载构建出来的 AwaCheatLab.dll，
//        反射调用里面的 CheatLabPermission.Decide（纯逻辑函数），
//        把「身份组合矩阵」全跑一遍，和期望值对比。
//
//  这样验的是【真实构建产物里的逻辑】，不是复制一份来测。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

class PermTest
{
    static int passed = 0;
    static int failed = 0;
    static readonly List<string> failures = new List<string>();

    static string dllPath;
    static Type cfgType;
    static Type permType;

    static int Main(string[] args)
    {
        dllPath = args.Length > 0
            ? args[0]
            : @"D:\scpsl-dev\AwaCheatLab\bin\Release\AwaCheatLab.dll";

        Console.WriteLine();
        Console.WriteLine("=".PadRight(72, '='));
        Console.WriteLine("  测试插件 权限逻辑 离线验证");
        Console.WriteLine("=".PadRight(72, '='));
        Console.WriteLine();

        if (!File.Exists(dllPath))
        {
            Console.WriteLine("  [x] 找不到 " + dllPath);
            return 1;
        }

        // Exiled.API.dll 在 LabAPI 依赖目录里，CheatLabConfig 实现 IConfig 需要它
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name;
            foreach (string dir in new[]
            {
                @"C:\Users\SRTY\AppData\Roaming\SCP Secret Laboratory\LabAPI\dependencies\global",
                @"C:\Users\SRTY\AppData\Roaming\EXILED\Plugins",
                Path.GetDirectoryName(dllPath),
            })
            {
                string p = Path.Combine(dir, name + ".dll");
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };

        Assembly asm = Assembly.LoadFrom(dllPath);
        cfgType = asm.GetType("AwaCheatLab.CheatLabConfig", true);
        permType = asm.GetType("AwaCheatLab.CheatLabPermission", true);

        Console.WriteLine("  程序集 : " + Path.GetFileName(dllPath));
        Console.WriteLine("  配置类 : " + cfgType.FullName);
        Console.WriteLine("  权限类 : " + permType.FullName);
        Console.WriteLine("  大小   : " + (new FileInfo(dllPath).Length / 1024) + " KB");
        Console.WriteLine();

        // ── 确认 Decide 真的存在（别测了个空壳） ──
        MethodInfo decide = permType.GetMethod(
            "Decide", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (decide == null)
        {
            Console.WriteLine("  [x] 找不到 Decide 方法 —— 测试没意义，先看构建");
            return 1;
        }

        Console.WriteLine("  找到 Decide(" + string.Join(", ",
            decide.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        Console.WriteLine();

        Console.WriteLine("-".PadRight(72, '-'));
        Console.WriteLine("  身份组合矩阵");
        Console.WriteLine("-".PadRight(72, '-'));

        // ── 逐个场景跑 ──
        // 参数：说明, isConsole, isHost, hasRa, userId, meetsExtra,
        //       allowOwner, allowRA, whitelist, require, 期望放行?
        var scenes = new List<object[]>
        {
            new object[] { "服务端控制台（所有者）",      true,  false, false, null,  true,  true,  true,  null,        null,      true  },
            new object[] { "服务器所有者（游戏内主机）",   false, true,  false, "uid1",true,  true,  true,  null,        null,      true  },
            new object[] { "管理员",                       false, false, true,  "uid2",true,  true,  true,  null,        null,      true  },
            new object[] { "普通玩家",                     false, false, false, "uid3",true,  true,  true,  null,        null,      false },
            new object[] { "白名单里的普通玩家",           false, false, false, "uid4",true,  true,  true,  new[]{"uid4"}, null,    true  },
            new object[] { "白名单大小写不同",             false, false, false, "UID5",true,  true,  true,  new[]{"uid5"}, null,    true  },
            new object[] { "白名单项带空格",               false, false, false, "uid6",true,  true,  true,  new[]{" uid6 "}, null,  true  },
            new object[] { "白名单里没这个人",             false, false, false, "uid7",true,  true,  true,  new[]{"other"}, null,   false },
            new object[] { "关掉所有者后，主机被拒",       false, true,  false, "uid1",true,  false, true,  null,        null,      false },
            new object[] { "关掉管理员后，管理员被拒",     false, false, true,  "uid2",true,  true,  false, null,        null,      false },
            new object[] { "管理员但缺少要求的权限",       false, false, true,  "uid2",false, true,  true,  null,        new[]{"X"},false },
            new object[] { "管理员且满足要求的权限",       false, false, true,  "uid2",true,  true,  true,  null,        new[]{"X"},true  },
            new object[] { "认不出 UserID",                false, false, true,  "",    true,  true,  true,  null,        null,      false },
            new object[] { "主机同时也是白名单",           false, true,  false, "uid1",true,  true,  true,  new[]{"uid1"}, null,    true  },
        };

        foreach (var s in scenes)
        {
            string desc    = (string)s[0];
            bool isConsole = (bool)s[1];
            bool isHost    = (bool)s[2];
            bool hasRa     = (bool)s[3];
            string userId  = (string)s[4];
            bool meets     = (bool)s[5];
            bool allowOwn  = (bool)s[6];
            bool allowRa   = (bool)s[7];
            string[] wl    = (string[])s[8];
            string[] req   = (string[])s[9];
            bool expected  = (bool)s[10];

            object cfg = MakeConfig(allowOwn, allowRa, wl, req);

            object[] args2 = { cfg, isConsole, isHost, hasRa, userId, meets, null };
            bool got = (bool)decide.Invoke(null, args2);
            string reason = args2[6] as string;

            string mark = got == expected ? "[v]" : "[x]";
            Console.WriteLine("  " + mark + " " + desc.PadRight(28)
                              + " 期望=" + (expected ? "放行" : "拒绝")
                              + " 实际=" + (got ? "放行" : "拒绝")
                              + "   (" + reason + ")");

            if (got == expected) passed++;
            else
            {
                failed++;
                failures.Add(desc + " —— 期望 " + (expected ? "放行" : "拒绝")
                             + "，实际 " + (got ? "放行" : "拒绝"));
            }
        }

        // ── 边界：配置为 null ──
        Console.WriteLine();
        Console.WriteLine("-".PadRight(72, '-'));
        Console.WriteLine("  边界情况");
        Console.WriteLine("-".PadRight(72, '-'));
        {
            object[] a = { null, false, true, true, "uid", true, null };
            bool got = (bool)decide.Invoke(null, a);
            Check("配置为 null 时拒绝", got == false, got ? "放行" : "拒绝");
        }
        {
            object cfg = MakeConfig(true, true, new string[0], new string[0]);
            object[] a = { cfg, false, true, true, null, true, null };
            bool got = (bool)decide.Invoke(null, a);
            Check("userId 为 null 时拒绝", got == false, got ? "放行" : "拒绝");
        }

        // ── 确认拒绝文案存在 ──
        MethodInfo deny = permType.GetMethod(
            "DenyMessage", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (deny != null)
        {
            object cfg = MakeConfig(true, true, new string[0], new string[0]);
            string msg = (string)deny.Invoke(null, new[] { cfg });
            Check("拒绝文案不为空", !string.IsNullOrWhiteSpace(msg), msg != null && msg.Length > 0 ? msg.Split('\n')[0] : "(空)");
        }

        // ── 结果 ──
        Console.WriteLine();
        Console.WriteLine("=".PadRight(72, '='));
        if (failed == 0)
        {
            Console.WriteLine("  权限逻辑验证通过 —— " + passed + " 项全过");
        }
        else
        {
            Console.WriteLine("  失败 " + failed + " 项（通过 " + passed + " 项）：");
            foreach (string f in failures) Console.WriteLine("    - " + f);
        }
        Console.WriteLine("=".PadRight(72, '='));
        Console.WriteLine();

        return failed == 0 ? 0 : 1;
    }

    static void Check(string desc, bool ok, string detail)
    {
        Console.WriteLine("  " + (ok ? "[v]" : "[x]") + " " + desc.PadRight(28) + "  " + detail);
        if (ok) passed++;
        else { failed++; failures.Add(desc); }
    }

    /// <summary>造一个 CheatLabConfig 实例并设好权限字段。</summary>
    static object MakeConfig(bool allowOwner, bool allowRa, string[] whitelist, string[] require)
    {
        object cfg = Activator.CreateInstance(cfgType);

        Set(cfg, "AllowServerOwner", allowOwner);
        Set(cfg, "AllowRemoteAdmin", allowRa);
        Set(cfg, "AllowAboutForEveryone", true);
        Set(cfg, "LogDenials", true);

        // List<string> 字段
        Type listType = typeof(List<string>);
        IList wl = (IList)Activator.CreateInstance(listType);
        if (whitelist != null) foreach (string x in whitelist) wl.Add(x);
        Set(cfg, "WhitelistedUserIds", wl);

        IList rq = (IList)Activator.CreateInstance(listType);
        if (require != null) foreach (string x in require) rq.Add(x);
        Set(cfg, "RequireRaPermissions", rq);

        return cfg;
    }

    static void Set(object obj, string prop, object val)
    {
        PropertyInfo p = cfgType.GetProperty(prop);
        if (p == null)
        {
            Console.WriteLine("  [x] 配置类里没有属性 " + prop + " —— 说明构建产物不对");
            failed++;
            return;
        }
        p.SetValue(obj, val, null);
    }
}
