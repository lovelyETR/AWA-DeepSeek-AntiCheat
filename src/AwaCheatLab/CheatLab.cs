// ============================================================================
//  AWA :: 反作弊测试插件 (CheatLab)
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  【这是什么】
//  一个**服务端**的作弊行为模拟器，用于在自己的测试服上验证反作弊的检测能力。
//
//  【它不是外挂】
//  它是一段服务端插件代码，没有客户端能力：
//    - 玩家装不了它 —— 只有服务器管理员能往服务端放 DLL
//    - 装到你自己电脑上没用 —— 客户端的 SCP:SL 根本不加载 EXILED 插件
//    - 拿到别人的服务器上没用 —— 除非对方管理员自己装、自己解锁
//    - 它不改客户端、不注入进程、不伪造网络包
//  它做的是「让服务端自己制造出作弊才会产生的状态」，好让检测器有东西可抓。
//
//  【安全锁】
//  配置 i_understand_this_is_a_test_tool 默认 false，必须手动改成 true 并重启，
//  插件才会执行任何注入。这是防止 DLL 被误装到正式服的第二道保险。
// ============================================================================

namespace AwaCheatLab
{
    using System;
using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Text;
    using CommandSystem;
    using Exiled.API.Features;
    using Exiled.API.Interfaces;
    using InventorySystem.Items;
    using InventorySystem.Items.Firearms.Modules;
    using InventorySystem.Items.Firearms.ShotEvents;
    using RemoteAdmin;
    using UnityEngine;

    /// <summary>CheatLab 的配置。</summary>
    public sealed class CheatLabConfig : IConfig
    {
        [Description("是否启用本插件。")]
        public bool IsEnabled { get; set; } = true;

        [Description("是否输出调试日志。")]
        public bool Debug { get; set; } = false;

        [Description(
            "【安全锁】必须改成 true 并重启，插件才会执行任何注入。\n" +
            "这是为了防止这个 DLL 被误装到正式服或别人的服务器上：\n" +
            "对方不手动改这一行并重启，它什么都不会做。")]
        public bool IUnderstandThisIsATestTool { get; set; } = false;

        [Description("解锁后初始是否处于开启状态。可以用 cl on / cl off 在游戏里随时切换，不用重启。")]
        public bool StartEnabled { get; set; } = false;

        [Description("注入后是否自动恢复现场（传送回去、血量还原、NoClip 关掉）。")]
        public bool AutoRestore { get; set; } = true;

        [Description("自动恢复前等待几秒（让反作弊的位置采样器有机会抓到）。")]
        public float RestoreDelaySeconds { get; set; } = 1.5f;

        [Description("瞬移注入的横向距离（米）。")]
        public float TeleportDistance { get; set; } = 60f;

        [Description("瞬移注入的垂直抬升（米）。")]
        public float TeleportLift { get; set; } = 25f;

        [Description("飞天注入的垂直抬升（米）。")]
        public float FlyLift { get; set; } = 40f;

        [Description("异常治疗注入的治疗量。")]
        public float HealAmount { get; set; } = 900f;

        [Description("连发注入默认打几发。")]
        public int RapidFireRounds { get; set; } = 40;

        // ───────────── 权限管理 ─────────────

        [Description(
            "是否允许【服务器所有者】使用（服务端控制台，以及游戏里的主机）。\n" +
            "建议保持 true —— 你自己就是所有者。")]
        public bool AllowServerOwner { get; set; } = true;

        [Description(
            "是否允许【管理员】使用（有 RemoteAdmin 权限的人）。\n" +
            "关掉的话就只有服务器所有者能用了。")]
        public bool AllowRemoteAdmin { get; set; } = true;

        [Description(
            "额外点名允许的玩家 UserID（形如 76561199570869496@steam）。\n" +
            "适合给特定的测试号开权限，而不用把 RA 权限给他。")]
        public List<string> WhitelistedUserIds { get; set; } = new List<string>();

        [Description(
            "进一步收紧：管理员还必须具备下面这些具体的 RA 权限。\n" +
            "留空 = 只要有 RemoteAdmin 权限就放行。\n" +
            "填了之后，权限不够的管理员也会被拒绝。\n" +
            "可填的值是游戏 PlayerPermissions 枚举的名字，例如：\n" +
            "  KickingAndShortTermBanning\n" +
            "  BanningOfflinePlayers\n" +
            "  ServerConsoleCommands")]
        public List<string> RequireRaPermissions { get; set; } = new List<string>();

        [Description(
            "是否允许所有人使用 .cl about（看这个插件是什么、为什么不是外挂）。\n" +
            "这只是说明文字，不执行任何注入，建议保持 true ——\n" +
            "让别人能确认它不是外挂。")]
        public bool AllowAboutForEveryone { get; set; } = true;

        [Description("被拒绝时提示什么。")]
        public string DenyMessage { get; set; } = "你没有权限使用这个测试插件。";

        [Description("被拒绝时是否在服务端日志里记一笔（能看到是谁想用）。")]
        public bool LogDenials { get; set; } = true;
    }

    /// <summary>插件主体。</summary>
    public sealed class CheatLabPlugin : Exiled.API.Features.Plugin<CheatLabConfig>
    {
        /// <summary>单例。</summary>
        public static CheatLabPlugin Instance { get; private set; }

        /// <summary>版本号。Alpha 用英文标注。</summary>
        public const string EditionVersion = "Alpha v1.0";

        /// <summary>运行时开关 —— cl on / cl off 切这个，不用重启。</summary>
        private bool sessionOn;

#if AWA_EDITION_PRIVATE
        /// <summary>版本标识。</summary>
        public const string Edition = "自用版";

        /// <summary>署名说明。</summary>
        public const string Notice = "AWA 自用版";
#else
        /// <summary>版本标识。</summary>
        public const string Edition = "公共版";

        /// <summary>署名说明。</summary>
        public const string Notice = "作者：AWA　　本插件全由 DSH 开发";
#endif

        /// <inheritdoc/>
        public override string Author => "AWA";

        /// <inheritdoc/>
        public override string Name => "AWA :: 反作弊测试插件";

        /// <inheritdoc/>
        public override string Prefix => "awa_cheatlab";

        /// <inheritdoc/>
        public override Version Version => new Version(1, 1, 0);

        /// <summary>最近一次执行命令的玩家（自瞄/透视测试要当射手）。</summary>
        public static Player CurrentSender { get; internal set; }

        /// <summary>运行时开关当前状态。</summary>
        public bool SessionOn => this.sessionOn;

        /// <summary>
        /// 安全锁是否已解除（配置层，需要重启才生效）。
        /// 这是防止 DLL 被别人拿去用的那道锁。
        /// </summary>
        public bool Unlocked =>
            this.Config != null && this.Config.IsEnabled && this.Config.IUnderstandThisIsATestTool;

        /// <summary>当前是否真的会执行注入（安全锁已解除 且 运行时开关是开的）。</summary>
        public bool Armed => this.Unlocked && this.sessionOn;

        /// <summary>这个插件在你服务器上的真实配置文件路径。</summary>
        private string ConfigPath()
        {
            try
            {
                return System.IO.Path.Combine(
                    Exiled.API.Features.Paths.Configs,
                    "Plugins",
                    this.Prefix,
                    Server.Port + ".yml");
            }
            catch
            {
                return "%APPDATA%\\EXILED\\Configs\\Plugins\\awa_cheatlab\\<端口>.yml";
            }
        }

        /// <summary>切换运行时开关（cl on / cl off 用，不需要重启）。</summary>
        public void SetSession(bool on)
        {
            this.sessionOn = on;
            Log.Warn("[CheatLab] 运行时开关 -> " + (on ? "开" : "关"));
        }

        /// <inheritdoc/>
        public override void OnEnabled()
        {
            Instance = this;
            this.sessionOn = this.Config.StartEnabled;

            Log.Warn("╔══════════════════════════════════════════════════════════╗");
            Log.Warn("║   AWA  ::  反作弊测试插件   [" + Edition + "  " + EditionVersion + "]");
            Log.Warn("║   " + Notice);
            Log.Warn("╠══════════════════════════════════════════════════════════╣");
            Log.Warn("║   服务端作弊行为模拟器 —— 仅用于测试反作弊检测能力");
#if !AWA_EDITION_PRIVATE
            Log.Warn("║   它不是外挂：没有客户端能力，玩家装不了，");
            Log.Warn("║   放到别人的服务器上也不会生效。");
#endif
            Log.Warn("╚══════════════════════════════════════════════════════════╝");

            if (!this.Unlocked)
            {
                Log.Error("[CheatLab] 安全锁未解除 —— 插件不会执行任何注入。");
                Log.Error("[CheatLab] 如果这确实是你的测试服，请在配置里把");
                Log.Error("[CheatLab]   i_understand_this_is_a_test_tool 改成 true 然后重启。");
                Log.Error("[CheatLab] 配置文件（把下面这个文件里的那一行改成 true）:");
                Log.Error("[CheatLab]   " + ConfigPath());
                Log.Error("[CheatLab] 输入 cl about 查看这个插件的完整说明。");
            }
            else
            {
                Log.Warn("[CheatLab] 安全锁已解除 —— 运行时开关当前为【"
                         + (this.sessionOn ? "开" : "关")
                         + "】，用 cl on / cl off 随时切换（不用重启）");
                Log.Info("[CheatLab] 输入 cl about 查看完整说明");
            }

            // 命令注册完全交给 EXILED，我们不碰。
            //
            // 注意：不要用 CommandProcessor.GetAllCommands() 去"验证"注册结果 ——
            // 实测它列不出已注册的自定义命令（会误报未注册），
            // 然后去兜底注册时又撞名，反而刷出一堆红字。
            // 判断注册是否成功，看 EXILED 自己打的那条日志：
            //   "Command with same name has already registered! Command: cheatlab"
            // 出现这条就说明注册成功了（它内部注册了两遍）。
            Log.Info("[CheatLab] 版本 " + EditionVersion + "    命令名: cheatlab    别名: cl / awalab");

            base.OnEnabled();
        }

        /// <inheritdoc/>
        public override void OnDisabled()
        {
            try
            {
                CommandProcessor.RemoteAdminCommandHandler.UnregisterCommand(new CheatLabCommand());
                this.OnUnregisteringCommands();
            }
            catch
            {
                // 忽略
            }

            Instance = null;
            base.OnDisabled();
        }

        /// <summary>把命令发起者解析成玩家。</summary>
        public static Player Resolve(ICommandSender sender)
        {
            if (sender is CommandSender cs)
            {
                try
                {
                    string uid = cs.SenderId;
                    if (!string.IsNullOrEmpty(uid))
                    {
                        foreach (Player p in Player.List)
                        {
                            if (p != null && p.IsConnected && string.Equals(p.UserId, uid, StringComparison.OrdinalIgnoreCase))
                            {
                                return p;
                            }
                        }
                    }

                    string nick = cs.Nickname;
                    if (!string.IsNullOrEmpty(nick))
                    {
                        foreach (Player p in Player.List)
                        {
                            if (p != null && p.IsConnected && string.Equals(p.Nickname, nick, StringComparison.OrdinalIgnoreCase))
                            {
                                return p;
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略
                }
            }

            return null;
        }

        /// <summary>按昵称找在线玩家。</summary>
        public static Player Find(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            foreach (Player p in Player.List)
            {
                if (p != null && p.IsConnected && string.Equals(p.Nickname, query, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            foreach (Player p in Player.List)
            {
                if (p != null && p.IsConnected && p.Nickname != null
                    && p.Nickname.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return p;
                }
            }

            return null;
        }
    }

    /// <summary>作弊行为注入。全部是服务端改状态，没有客户端能力。</summary>
    internal static partial class Inject
    {
        /// <summary>开启 NoClip。</summary>
        internal static string Noclip(Player p)
        {
            bool before = p.IsNoclipEnabled;
            p.IsNoclipEnabled = true;
            return string.Format(
                CultureInfo.InvariantCulture,
                "IsNoclipEnabled: {0} -> true   (IsNoclipPermitted={1})\n" +
                "反作弊的 NoClip 检测读的就是这两个状态位。",
                before, p.IsNoclipPermitted);
        }

        /// <summary>瞬移。</summary>
        internal static string Teleport(Player p, float distance, float lift)
        {
            Vector3 from = p.Position;
            var to = new Vector3(from.x + distance, from.y + lift, from.z + distance);
            p.Position = to;
            return string.Format(
                CultureInfo.InvariantCulture,
                "位置 ({0:F0},{1:F0},{2:F0}) -> ({3:F0},{4:F0},{5:F0})，位移约 {6:F0} 米\n" +
                "位置是服务端权威数据，采样器会算出「人不可能达到」的速度。",
                from.x, from.y, from.z, to.x, to.y, to.z, Vector3.Distance(from, to));
        }

        /// <summary>垂直飞天。</summary>
        internal static string Fly(Player p, float lift)
        {
            Vector3 from = p.Position;
            p.Position = new Vector3(from.x, from.y + lift, from.z);
            return string.Format(
                CultureInfo.InvariantCulture,
                "位置 Y {0:F1} -> {1:F1}（抬升 {2:F0} 米）\n" +
                "垂直速度会超过人类跳跃上限。",
                from.y, from.y + lift, lift);
        }

        /// <summary>刷物品。</summary>
        internal static string Items(Player p)
        {
            ItemType[] types =
            {
                ItemType.KeycardO5, ItemType.Medkit, ItemType.Painkillers,
                ItemType.Radio, ItemType.Flashlight, ItemType.Coin,
                ItemType.ArmorHeavy, ItemType.GrenadeFlash, ItemType.SCP268,
                ItemType.SCP500, ItemType.Adrenaline, ItemType.SCP207,
                ItemType.GunE11SR, ItemType.Ammo556x45,
            };

            int ok = 0;
            foreach (ItemType itemType in types)
            {
                try
                {
                    p.AddItem(itemType);
                    ok++;
                }
                catch
                {
                    // 某些物品在特定角色下不能给
                }
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "给了 {0}/{1} 件物品（含 E11 步枪，方便接着做射击测试）\n" +
                "用来触发「可疑物品获取」检测。",
                ok, types.Length);
        }

        /// <summary>
        /// 模拟「透视行为痕迹」。
        ///
        /// 透视本身是纯客户端渲染，服务端看不到、也没法让人"看见"。
        /// 但透视玩家会做出一种服务端可见的行为：**打他本来不该看见的目标**。
        /// 这里就把靶子套上 SCP-268 隐身，然后打他 ——
        /// 正常玩家做不到，反作弊的 "命中隐身目标" 检测会抓到。
        /// </summary>
        internal static string Esp(Player shooter, Player target)
        {
            if (shooter == null || target == null || target.ReferenceHub == shooter.ReferenceHub)
            {
                return "透视痕迹测试需要一个「别的玩家」当靶子：cl esp <对方昵称>";
            }

            bool invisible = false;
            try
            {
                // SCP-268 的隐身效果（EffectType.Invisible = 20，Fade 是屏幕变暗、不是隐身）
                target.EnableEffect(Exiled.API.Enums.EffectType.Invisible, 30f, false);
                invisible = true;
            }
            catch (Exception e)
            {
                return "给目标套隐身失败: " + e.Message;
            }

            string fired = Shoot(shooter, target, 8);

            var sb = new StringBuilder();
            sb.AppendLine("透视行为痕迹测试");
            sb.AppendLine("  靶子: " + target.Nickname + "（已套 SCP-268 隐身" + (invisible ? " ✓" : "") + "）");
            sb.AppendLine();
            sb.AppendLine("  说明：透视本身服务端看不到，模拟不了「看见」。");
            sb.AppendLine("        但可以模拟「打不该看见的人」这个行为痕迹 ——");
            sb.AppendLine("        反作弊的「命中隐身目标」检测读的就是这个。");
            sb.AppendLine();
            sb.AppendLine(fired.Replace("\n", "\n  "));
            return sb.ToString();
        }

        /// <summary>
        /// 模拟「自瞄行为」—— 真的把射手转向目标再开火。
        ///
        /// 原理（读游戏 IL 得到的）：
        ///   HitscanHitregModuleBase.ForwardRay
        ///     = new Ray(Owner.PlayerCameraReference.position,
        ///               Owner.PlayerCameraReference.forward)
        /// 也就是「子弹沿玩家摄像机的朝向飞」。所以只要把射手的 Rotation
        /// 设成「正对靶子」，他开出的每一枪就必然命中 —— 这正是自瞄的统计特征。
        ///
        /// 瞄头（aimHead=true）时对准头部高度，能造出爆头率异常。
        /// 注意：这不是客户端自瞄，改的是服务端权威的朝向数据。
        /// </summary>
        internal static string Aimbot(Player shooter, Player target, int rounds, bool aimHead)
        {
            if (shooter == null || target == null || target.ReferenceHub == shooter.ReferenceHub)
            {
                return "自瞄测试需要一个「别的玩家」当靶子：cl aimbot <对方昵称> [发数]";
            }

            var firearm = shooter.CurrentItem as Exiled.API.Features.Items.Firearm;
            if (firearm == null)
            {
                return "射手手上没有枪。\n先执行 cl items 给物品，然后在游戏里切到枪（E11 步枪）。";
            }

            HitscanHitregModuleBase module = firearm.HitscanHitregModule;
            if (module == null)
            {
                return "这把武器没有 hitscan 开火模块。";
            }

            var identifier = new ItemIdentifier(firearm.Type, firearm.Serial);
            int fired = 0;
            int aimed = 0;

            for (int i = 0; i < rounds; i++)
            {
                try
                {
                    // ① 每枪都重新对准（模拟自瞄的持续锁定）
                    Vector3 aimPoint = aimHead
                        ? target.Position + (Vector3.up * 1.65f)      // 头
                        : target.Position + (Vector3.up * 0.95f);     // 身体中线

                    Vector3 eye = shooter.CameraTransform.position;
                    Vector3 dir = aimPoint - eye;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        shooter.Rotation = Quaternion.LookRotation(dir.normalized);
                        aimed++;
                    }

                    // ② 真实开火（子弹沿摄像机朝向飞）
                    module.Fire(target.ReferenceHub, new BulletShotEvent(identifier));
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

            var sb = new StringBuilder();
            sb.AppendLine("自瞄行为模拟（服务端驱动真实瞄准 + 开火）");
            sb.AppendLine("  射手: " + shooter.Nickname);
            sb.AppendLine("  靶子: " + target.Nickname + (aimHead ? "（瞄头）" : "（瞄身体）"));
            sb.AppendLine("  命中: " + fired + "/" + rounds + " 发，其中 " + aimed + " 次重新瞄准");
            sb.AppendLine();
            sb.AppendLine("  原理：子弹沿「玩家摄像机朝向」飞，所以把 Rotation 对准靶子后必然命中。");
            sb.AppendLine("        这产生的是真实命中/爆头统计 —— 反作弊的命中率、爆头率、");
            sb.AppendLine("        射速、反应时间检测读的就是这组数字。");
            return sb.ToString();
        }

        /// <summary>异常治疗。</summary>
        internal static string Heal(Player p, float amount)
        {
            float before = p.Health;
            p.Heal(amount, true);
            return string.Format(
                CultureInfo.InvariantCulture,
                "血量 {0:F0} -> {1:F0}（注入治疗 {2:F0}）\n" +
                "用来触发「异常回血」检测。",
                before, p.Health, amount);
        }

        /// <summary>
        /// 服务端驱动真实开火。
        ///
        /// 调的是 HitscanHitregModuleBase.Fire(primaryTarget, shotEvent) ——
        /// 也就是 EXILED 的 Shot 事件挂钩的那个方法。
        /// 所以产生的是真实弹道、真实伤害、真实 ShotEventArgs。
        ///
        /// 这是整个工具里最接近「真作弊」的一项，但它依然是服务端调用游戏自己的开火函数：
        /// 没有客户端参与，拿到别人的服务器上什么都做不了。
        /// </summary>
        internal static string Shoot(Player shooter, Player target, int rounds)
        {
            if (shooter == null)
            {
                return "没有开火者。";
            }

            var firearm = shooter.CurrentItem as Exiled.API.Features.Items.Firearm;
            if (firearm == null)
            {
                return "开火者手上没有枪。\n" +
                       "先执行 cl items 给物品，然后让他在游戏里切到枪（E11 步枪）。";
            }

            HitscanHitregModuleBase module = firearm.HitscanHitregModule;
            if (module == null)
            {
                return "这把武器没有 hitscan 开火模块（不是枪械类武器）。";
            }

            // 游戏在 Fire(target, ...) 里会调 EnableSelfDamageProtection()，
            // 所以打自己一定没效果（不掉血、不算命中）—— 必须在代码层面拦住。
            Player victim = target ?? shooter;
            if (victim.ReferenceHub == shooter.ReferenceHub)
            {
                return "不能在只有你一个人的时候测射击类 —— 这把枪打自己会被游戏的「自伤保护」拦掉，\n" +
                       "不掉血、不算命中、反作弊也收不到数据。\n\n" +
                       "需要另一个玩家当靶子：\n" +
                       "  .cl shoot <对方昵称> [发数]\n\n" +
                       "或者让一个人先进服务器，你再执行。";
            }

            var identifier = new ItemIdentifier(firearm.Type, firearm.Serial);

            int fired = 0;
            string error = null;
            for (int i = 0; i < rounds; i++)
            {
                try
                {
                    module.Fire(victim.ReferenceHub, new BulletShotEvent(identifier));
                    fired++;
                }
                catch (Exception e)
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "第 {0} 发时出错: {1}: {2}",
                        i + 1, e.GetType().Name, e.Message);
                    break;
                }
            }

            var sb = new StringBuilder();
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "服务端驱动真实开火 {0}/{1} 发\n  武器: {2}\n  目标: {3}\n",
                fired, rounds, firearm.Type, victim.Nickname);
            if (error != null)
            {
                sb.AppendLine("  " + error);
            }

            sb.AppendLine("这会真实产生 Shot 事件、真实伤害、真实命中统计 ——");
            sb.AppendLine("反作弊的命中率 / 射速 / 伤害检测全都会收到数据。");
            return sb.ToString();
        }
    }

    /// <summary>命令处理。</summary>
    // 三个 handler 都要标 —— EXILED 是 if/else-if 链，只标 RA 的话游戏内控制台找不到。
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    [CommandHandler(typeof(GameConsoleCommandHandler))]
    public sealed partial class CheatLabCommand : ICommand
    {
        /// <inheritdoc/>
        public string Command => "cheatlab";

        /// <inheritdoc/>
        public string[] Aliases => new[] { "cl", "awalab" };

        /// <inheritdoc/>
        public string Description => "AWA 反作弊测试插件 —— 服务端作弊行为模拟（仅用于测试反作弊）";

        /// <inheritdoc/>
        public string[] Usage => new[]
        {
            "about",
            "perms",
            "on",
            "off",
            "status",
            "list",
            "shoot [目标] [发数]",
            "rapidfire [目标]",
            "noclip [玩家]",
            "teleport [玩家]",
            "fly [玩家]",
            "items [玩家]",
            "heal [玩家]",
            "god",
            "door",
            "spin [次数]",
            "jitter [次数]",
            "aimbot <靶子> [发数]",
            "esp <靶子>",
            "all [玩家]",
        };

        /// <inheritdoc/>
        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            bool ok = this.ExecuteInner(arguments, sender, out response);
            response = Clamp(response);
            return ok;
        }

        /// <summary>
        /// 命令回复有长度上限，超了会被游戏截断（看起来像「没显示全」）。
        /// 这里主动截断并给出提示。
        /// </summary>
        private static string Clamp(string text)
        {
            const int Limit = 1100;
            if (string.IsNullOrEmpty(text) || text.Length <= Limit)
            {
                return text;
            }

            return text.Substring(0, Limit) +
                   "\n\n…（输出过长已截断）\n" +
                   "用更具体的子命令看剩下的，例如 .cl list";
        }

        private bool ExecuteInner(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            CheatLabPlugin plugin = CheatLabPlugin.Instance;
            if (plugin == null)
            {
                response = "AWA 反作弊测试插件没有加载。";
                return false;
            }

            var args = new string[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
            {
                args[i] = arguments.Array[arguments.Offset + i];
            }

            CheatLabPlugin.CurrentSender = CheatLabPlugin.Resolve(sender);

            // ── 权限门：只有服务器所有者 / 管理员 / 白名单能用 ──
            // 这是唯一入口，拦在这里就等于拦住了全部注入能力。
            // about 只是说明文字（不执行任何注入），可以按配置对所有人开放。
            {
                string permSub = (arguments.Count > 0 && arguments.Array != null)
                    ? arguments.Array[arguments.Offset].ToLowerInvariant()
                    : string.Empty;

                bool isAbout = permSub == "about" || permSub == "help" || permSub == "?";

                if (!(isAbout && plugin.Config.AllowAboutForEveryone))
                {
                    if (!CheatLabPermission.IsAllowed(plugin.Config, sender, out string whoAllowed))
                    {
                        if (plugin.Config.LogDenials)
                        {
                            Log.Warn("[CheatLab] 拒绝访问（" + whoAllowed + "）"
                                     + "  发起人: " + whoAllowed);
                        }

                        response = CheatLabPermission.DenyMessage(plugin.Config);
                        return false;
                    }
                }
            }

            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

            // 安全锁没解除时，只允许看说明
            if (!plugin.Unlocked)
            {
                if (sub == "about")
                {
                    response = About();
                    return true;
                }

                response =
                    "AWA 反作弊测试插件 —— 安全锁未解除，拒绝执行。\n\n" +
                    "这道锁是防止这个 DLL 被拷到别人的服务器上乱用的。\n" +
                    "如果这确实是你的测试服：\n" +
                    "  1) 打开 %APPDATA%\\EXILED\\Configs\\Plugins\\awa_cheatlab\\<端口>.yml\n" +
                    "  2) 把 i_understand_this_is_a_test_tool 改成 true\n" +
                    "  3) 重启服务端（这一步必须重启）\n\n" +
                    "解锁之后就能用 cl on / cl off 随便开关，不用再重启。\n" +
                    "输入 cl about 看这个插件的完整说明。";
                return false;
            }

            switch (sub)
            {
                case "perms":
                    response = CheatLabPermission.Describe(plugin.Config);
                    return true;

                case "about":
                    response = About();
                    return true;

                case "on":
                    plugin.SetSession(true);
                    response =
                        "AWA 反作弊测试插件 —— 已【开启】\n\n" +
                        "现在可以用了：\n" +
                        "  .cl shoot       服务端驱动真实开火\n" +
                        "  .cl rapidfire   真实连发\n" +
                        "  .cl noclip      开启 NoClip\n" +
                        "  .cl teleport    瞬移\n" +
                        "  .cl fly         垂直飞天\n" +
                        "  .cl items       刷物品\n" +
                        "  .cl heal        异常治疗\n" +
                        "  .cl all         跑一遍全部\n\n" +
                        "做完用反作弊插件的 .dsac report 看有没有抓到。\n" +
                        "随时可以 cl off 关掉。";
                    return true;

                case "off":
                    plugin.SetSession(false);
                    response =
                        "AWA 反作弊测试插件 —— 已【关闭】\n\n" +
                        "所有注入命令都会拒绝执行，直到你再次 cl on。\n" +
                        "（反作弊插件本身不受影响，继续正常工作）";
                    return true;

                case "status":
                    response = string.Format(
                        CultureInfo.InvariantCulture,
                        "AWA 反作弊测试插件  [{0}]\n" +
                        "  安全锁    : 已解除\n" +
                        "  运行时开关: {1}\n" +
                        "  自动恢复  : {2}\n" +
                        "  恢复延迟  : {3:F1} 秒\n" +
                        "  连发默认  : {4} 发\n\n" +
                        "  用 cl on / cl off 切换，不需要重启。",
                        CheatLabPlugin.Edition,
                        plugin.SessionOn ? "开" : "关",
                        plugin.Config.AutoRestore,
                        plugin.Config.RestoreDelaySeconds,
                        plugin.Config.RapidFireRounds);
                    return true;

                case "list":
                    response = this.Help();
                    return true;

                case "shoot":
                    return Shoot(plugin, args, false, out response);

                case "rapidfire":
                    return Shoot(plugin, args, true, out response);

                case "noclip":
                case "teleport":
                case "fly":
                case "items":
                case "heal":
                    return Single(plugin, sub, args, out response);


                case "god":
                case "door":
                case "spin":
                case "jitter":
                    return SimpleSelf(plugin, sub, args, out response);

                case "aimbot":
                    return Special(plugin, sub, args, out response);

                case "esp":
                    return Special(plugin, sub, args, out response);

                case "all":
                    return RunAll(plugin, args, out response);

                default:
                    response = "未知子命令: " + sub + "\n\n" + this.Help();
                    return false;
            }
        }

        /// <summary>完整说明 —— 回答「这是什么」和「为什么它不是外挂」。</summary>
        private static string About()
        {
            var sb = new StringBuilder();
            sb.AppendLine("════════════════════════════════════════════════════════");
            sb.AppendLine("  AWA 反作弊测试插件  [" + CheatLabPlugin.Edition + "  " + CheatLabPlugin.EditionVersion + "]");
            sb.AppendLine("  " + CheatLabPlugin.Notice);
            sb.AppendLine("════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine("【它做什么】");
            sb.AppendLine("在服务端制造出「作弊才会产生的状态」，好让反作弊的检测器有东西可抓。");
            sb.AppendLine("比如：开启 NoClip、瞬移、刷物品，以及服务端驱动真实开火。");
            sb.AppendLine();
#if !AWA_EDITION_PRIVATE
            // 下面这一整段是给收件人看的 —— 自用版里不输出
            sb.AppendLine("【它不是外挂】");
            sb.AppendLine("  1. 它是服务端插件，不是客户端程序。");
            sb.AppendLine("     玩家装不了它 —— 只有服务器管理员能往服务端放 DLL。");
            sb.AppendLine();
            sb.AppendLine("  2. 它没有客户端能力。");
            sb.AppendLine("     不改客户端、不注入进程、不读写游戏内存、不碰网络包。");
            sb.AppendLine("     程序集引用里没有 Harmony、没有内存写入 API、没有远程线程。");
            sb.AppendLine();
            sb.AppendLine("  3. 它连「作弊」这件事都做不到。");
            sb.AppendLine("     它只是让服务端自己产生作弊状态，用来验证检测器灵不灵。");
            sb.AppendLine();
            sb.AppendLine("【为什么装在你自己的电脑上没用】");
            sb.AppendLine("  EXILED 插件只在服务端加载。");
            sb.AppendLine("  你把它放进自己电脑的游戏目录，客户端的 SCP:SL 根本不会读它 ——");
            sb.AppendLine("  客户端不加载 EXILED 插件，那个目录里的 DLL 它一个都不认。");
            sb.AppendLine("  就算硬塞进去也没用：这个插件做的全是「服务端改状态」的操作，");
            sb.AppendLine("  而客户端没有这个权限，代码一行都执行不到。");
            sb.AppendLine();
            sb.AppendLine("  换句话说：它是一个「让服务器以为自己被作弊了」的工具，");
            sb.AppendLine("  装在客户端上，它连启动的机会都没有。");
            sb.AppendLine();
            sb.AppendLine("【为什么装到别人的服务器上也没用】");
            sb.AppendLine("  服务器插件必须由服务器管理员放进服务端的插件目录，");
            sb.AppendLine("  普通玩家没有这个权限。所以这个 DLL 就算传出去，");
            sb.AppendLine("  对方管理员不主动装、不主动解锁安全锁，它永远不会运行。");
            sb.AppendLine();
#else
            sb.AppendLine("  没有客户端能力，改的是服务端状态。");
            sb.AppendLine();
#endif
            sb.AppendLine("【安全锁】");
            sb.AppendLine("  配置里 i_understand_this_is_a_test_tool 默认 false。");
            sb.AppendLine("  不改成 true 并重启，插件拒绝执行任何注入。");
#if !AWA_EDITION_PRIVATE
            sb.AppendLine("  这是防止误装到正式服的第二道保险。");
#endif
            sb.AppendLine();
            sb.AppendLine("【开关】");
            sb.AppendLine("  .cl on       开启（随时可用，不用重启）");
            sb.AppendLine("  .cl off      关闭");
            sb.AppendLine("  .cl status   看当前状态");
            sb.AppendLine("  .cl list     列出全部命令");
            sb.AppendLine();
            sb.AppendLine("════════════════════════════════════════════════════════");
            return sb.ToString();
        }

        private string Help()
        {
            var sb = new StringBuilder();
            sb.AppendLine("AWA 反作弊测试插件 —— 服务端作弊行为模拟（仅用于测试反作弊）");
            sb.AppendLine();
#if !AWA_EDITION_PRIVATE
            sb.AppendLine("  它不是外挂：没有客户端能力，玩家装不了，");
            sb.AppendLine("  放到你自己的电脑或别人的服务器上都不会生效。");
#endif
            sb.AppendLine("  输入 cl about 看说明。");
            sb.AppendLine();
            sb.AppendLine("开关：");
            sb.AppendLine("  .cl on / cl off         随时开关，不用重启");
            sb.AppendLine("  .cl status              看当前状态");
            sb.AppendLine();
            sb.AppendLine("注入（[玩家] 省略时以你自己为目标）：");
            sb.AppendLine();
            sb.AppendLine("  【射击类 —— 需要另一个玩家当靶子】");
            sb.AppendLine("  .cl aimbot <靶子> [发数|head|body]   自瞄：每枪转向瞄准（默认瞄头）");
            sb.AppendLine("  .cl esp <靶子>                       透视痕迹：给靶子套隐身再打他");
            sb.AppendLine("  .cl shoot <靶子> [发数]              普通开火（不转向瞄准）");
            sb.AppendLine("  .cl rapidfire <靶子>                 真实连发，触发射速异常");
            sb.AppendLine();
            sb.AppendLine("  【状态类 —— 可以对自己用】");
            sb.AppendLine("  .cl noclip [玩家]        开启 NoClip（状态位检测）");
            sb.AppendLine("  .cl teleport [玩家]      瞬移（位置采样检测）");
            sb.AppendLine("  .cl fly [玩家]           垂直飞天");
            sb.AppendLine("  .cl items [玩家]         刷物品（含 E11 步枪）");
            sb.AppendLine("  .cl heal [玩家]          异常治疗（回血检测）");
            sb.AppendLine("  .cl all [玩家]           依次跑一遍状态类");
            sb.AppendLine();
            sb.AppendLine("做完之后用反作弊插件的 .dsac report / .dsac stats 看有没有抓到。");
            return sb.ToString();
        }

        private static bool Shoot(CheatLabPlugin plugin, string[] args, bool rapid, out string response)
        {
            if (!plugin.Armed)
            {
                response = "运行时开关是【关】的。先执行 cl on 再试。";
                return false;
            }

            Player shooter = args.Length >= 2 ? CheatLabPlugin.Find(args[1]) : null;
            if (args.Length >= 2 && shooter == null)
            {
                response = "找不到玩家：" + args[1];
                return false;
            }

            if (shooter == null)
            {
                response = "没法确定开火者。用法：.cl " + (rapid ? "rapidfire" : "shoot") + " <靶子昵称> [发数]";
                return false;
            }

            if (shooter == null)
            {
                shooter = CheatLabPlugin.CurrentSender;
            }

            Player target = null;
            int rounds = rapid ? plugin.Config.RapidFireRounds : 6;

            if (args.Length >= 3)
            {
                Player maybe = CheatLabPlugin.Find(args[2]);
                if (maybe != null)
                {
                    target = maybe;
                    if (args.Length >= 4 && int.TryParse(args[3], out int parsed) && parsed > 0)
                    {
                        rounds = Math.Min(parsed, 200);
                    }
                }
                else if (int.TryParse(args[2], out int parsed2) && parsed2 > 0)
                {
                    rounds = Math.Min(parsed2, 200);
                }
            }

            response = Inject.Shoot(shooter, target, rounds);
            return true;
        }

        private static bool Single(CheatLabPlugin plugin, string sub, string[] args, out string response)
        {
            if (!plugin.Armed)
            {
                response = "运行时开关是【关】的。先执行 cl on 再试。";
                return false;
            }

            Player p = args.Length >= 2 ? CheatLabPlugin.Find(args[1]) : null;
            if (args.Length >= 2 && p == null)
            {
                response = "找不到玩家：" + args[1];
                return false;
            }

            if (p == null)
            {
                response = "用法：.cl " + sub + " <玩家昵称>";
                return false;
            }

            object snapshot = Snapshot(sub, p);
            string result;
            switch (sub)
            {
                case "noclip":
                    result = Inject.Noclip(p);
                    break;
                case "teleport":
                    result = Inject.Teleport(p, plugin.Config.TeleportDistance, plugin.Config.TeleportLift);
                    break;
                case "fly":
                    result = Inject.Fly(p, plugin.Config.FlyLift);
                    break;
                case "items":
                    result = Inject.Items(p);
                    break;
                case "heal":
                    result = Inject.Heal(p, plugin.Config.HealAmount);
                    break;
                default:
                    response = "未知子命令";
                    return false;
            }

            ScheduleRestore(plugin, sub, p, snapshot);
            response = "目标: " + p.Nickname + "\n" + result +
                       "\n\n等 2 秒后用 .dsac report 看检测器有没有抓到。";
            return true;
        }

        /// <summary>
        /// 自瞄 / 透视痕迹测试。
        ///
        /// 两者都需要**另一个玩家**当靶子 —— 游戏在 Fire(target,...) 里开了自伤保护，
        /// 打自己不掉血、不算命中、反作弊收不到任何数据。
        /// </summary>
        private static bool Special(CheatLabPlugin plugin, string sub, string[] args, out string response)
        {
            if (!plugin.Armed)
            {
                response = "运行时开关是【关】的。先执行 cl on 再试。";
                return false;
            }

            Player shooter = CheatLabPlugin.CurrentSender;
            if (shooter == null)
            {
                response = "这个命令要在游戏内执行 —— 服务端控制台发起的没有「你是谁」，也就没有射手。";
                return false;
            }

            if (args.Length < 2)
            {
                response = "用法：.cl " + sub + " <靶子昵称>" + (sub == "aimbot" ? " [发数|head|body]" : string.Empty) +
                           "\n\n靶子必须是另一个玩家 —— 打在你自己身上会被游戏的自伤保护拦掉。";
                return false;
            }

            Player target = CheatLabPlugin.Find(args[1]);
            if (target == null)
            {
                response = "找不到靶子：" + args[1];
                return false;
            }

            if (target.ReferenceHub == shooter.ReferenceHub)
            {
                response = "靶子不能是你自己 —— 游戏对「打自己」有自伤保护，不掉血也不算命中。\n" +
                           "需要另一个玩家在服务器里。";
                return false;
            }

            if (sub == "aimbot")
            {
                int rounds = 30;
                bool aimHead = true;   // 默认瞄头 —— 自瞄最典型的特征就是爆头率异常

                if (args.Length >= 3)
                {
                    if (string.Equals(args[2], "body", StringComparison.OrdinalIgnoreCase))
                    {
                        aimHead = false;
                    }
                    else if (string.Equals(args[2], "head", StringComparison.OrdinalIgnoreCase))
                    {
                        aimHead = true;
                    }
                    else if (int.TryParse(args[2], out int parsed) && parsed > 0)
                    {
                        rounds = Math.Min(parsed, 120);
                    }
                }

                if (args.Length >= 4)
                {
                    if (string.Equals(args[3], "body", StringComparison.OrdinalIgnoreCase)) aimHead = false;
                    else if (int.TryParse(args[3], out int p3) && p3 > 0) rounds = Math.Min(p3, 120);
                }

                response = Inject.Aimbot(shooter, target, rounds, aimHead);
            }
            else
            {
                response = Inject.Esp(shooter, target);
            }

            return true;
        }
        /// <summary>命令发起者（Special 里用）。</summary>
        internal static Player CurrentSender;
        private static bool RunAll(CheatLabPlugin plugin, string[] args, out string response)
        {
            if (!plugin.Armed)
            {
                response = "运行时开关是【关】的。先执行 cl on 再试。";
                return false;
            }

            Player p = args.Length >= 2 ? CheatLabPlugin.Find(args[1]) : null;
            if (p == null)
            {
                response = "用法：.cl all <玩家昵称>";
                return false;
            }

            if (!p.IsAlive)
            {
                response = p.Nickname + " 不是存活状态，位置/血量类注入需要有角色。";
                return false;
            }

            string teleport = Inject.Teleport(p, plugin.Config.TeleportDistance, plugin.Config.TeleportLift);

            var sb = new StringBuilder();
            sb.AppendLine("对 " + p.Nickname + " 依次执行：");
            sb.AppendLine();
            sb.AppendLine("[1] NoClip");
            sb.AppendLine("    " + Inject.Noclip(p).Replace("\n", "\n    "));
            sb.AppendLine();
            sb.AppendLine("[2] 瞬移");
            sb.AppendLine("    " + teleport.Replace("\n", "\n    "));
            sb.AppendLine();
            sb.AppendLine("[3] 异常治疗");
            sb.AppendLine("    " + Inject.Heal(p, plugin.Config.HealAmount).Replace("\n", "\n    "));
            sb.AppendLine();
            sb.AppendLine("[4] 刷物品");
            sb.AppendLine("    " + Inject.Items(p).Replace("\n", "\n    "));
            sb.AppendLine();
            sb.AppendLine("射击类（需要手里拿枪）用 cl shoot 或 cl rapidfire 单独做。");
            sb.AppendLine();

            if (plugin.Config.AutoRestore)
            {
                try
                {
                    MEC.Timing.CallDelayed(Math.Max(1f, plugin.Config.RestoreDelaySeconds), () =>
                    {
                        try
                        {
                            p.IsNoclipEnabled = false;
                            Log.Info("[CheatLab] 已关闭 " + p.Nickname + " 的 NoClip");
                        }
                        catch
                        {
                            // 玩家可能已离线
                        }
                    });
                }
                catch
                {
                    // MEC 不可用
                }
            }

            response = sb.ToString();
            return true;
        }

        private static object Snapshot(string sub, Player p)
        {
            try
            {
                switch (sub)
                {
                    case "noclip":
                        return p.IsNoclipEnabled;
                    case "teleport":
                    case "fly":
                        return p.Position;
                    case "heal":
                        return p.Health;
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void ScheduleRestore(CheatLabPlugin plugin, string sub, Player p, object snapshot)
        {
            if (!plugin.Config.AutoRestore || snapshot == null)
            {
                return;
            }

            try
            {
                MEC.Timing.CallDelayed(Math.Max(0.5f, plugin.Config.RestoreDelaySeconds), () =>
                {
                    try
                    {
                        switch (sub)
                        {
                            case "noclip":
                                p.IsNoclipEnabled = (bool)snapshot;
                                break;
                            case "teleport":
                            case "fly":
                                p.Position = (Vector3)snapshot;
                                break;
                            case "heal":
                                p.Health = (float)snapshot;
                                break;
                        }

                        Log.Info("[CheatLab] 已恢复现场（" + sub + "）");
                    }
                    catch
                    {
                        // 玩家可能已离线
                    }
                });
            }
            catch
            {
                // MEC 不可用
            }
        }
    }
}
