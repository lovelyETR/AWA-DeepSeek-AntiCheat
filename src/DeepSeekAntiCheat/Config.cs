// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  本文件由 AWA 编写。
// ============================================================================
namespace DeepSeekAntiCheat
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using Exiled.API.Interfaces;

    /// <summary>
    /// 插件配置。EXILED 会把它序列化成
    /// %AppData%\EXILED\Configs\&lt;端口&gt;-config.yml
    /// </summary>
        // AWA :: 配置定义。
        // 配置里出现的一切都会写进用户的 yml，注意措辞。
    public sealed class Config : IConfig
    {
        [Description(
            "【服务器特性白名单】—— 开服时按你的玩法填一次。\\n" +
            "这些开关默认全关，插件按「标准无特性服务器」判断；\\n" +
            "如果你的服开了无限体力 / 无限子弹 / 过场飞行 / 主动发物品等，\\n" +
            "把对应项打开 —— 否则那些特性产生的数据会被当成作弊。")]
        public ServerFeatures Features { get; set; } = new ServerFeatures();

        [Description("是否启用本插件。")]
        public bool IsEnabled { get; set; } = true;

        [Description("是否在服务端控制台输出调试信息。")]
        public bool Debug { get; set; } = false;

        // ───────────── DeepSeek API ─────────────

        [Description("是否启用 AI 复核。关掉则只做本地启发式检测，完全不联网、零成本。")]
        public bool EnableAiReview { get; set; } = true;

        [Description("API Key。到 https://platform.deepseek.com 申请。用本地模型（Ollama/LM Studio）或自建服务时可以留空。")]
        public string ApiKey { get; set; } = string.Empty;

        [Description("API 地址。填到 /v1 之前即可，插件会自动拼 /chat/completions。官方= https://api.deepseek.com ；Ollama= http://127.0.0.1:11434/v1 ；LM Studio= http://127.0.0.1:1234/v1")]
        public string ApiBaseUrl { get; set; } = "https://api.deepseek.com";

        [Description("模型名。官方推荐 deepseek-chat（便宜快）；本地模型填你在 Ollama/LM Studio 里的模型名，例如 qwen2.5:7b-instruct")]
        public string Model { get; set; } = "deepseek-chat";

        [Description("是否要求接口返回严格 JSON（response_format）。官方和多数商业 API 支持；部分本地推理框架不支持，报错就把它设为 false。")]
        public bool UseJsonResponseFormat { get; set; } = true;

        [Description("HTTP 超时（秒）。")]
        public int TimeoutSeconds { get; set; } = 20;

        [Description("单次判定的最大输出 token 数。")]
        public int MaxTokens { get; set; } = 400;

        // ───────────── 本地启发式阈值 ─────────────

        [Description("低于这个采样数（命中次数）不做判定，避免误伤新手。")]
        public int MinSamples { get; set; } = 25;

        [Description("本地评分达到这个值才送 DeepSeek 复核（0-100）。默认 35：偏低是故意的，因为 AI 的存在就是为了裁决「有点可疑但不确定」的情况；每局调用上限会兜住成本。")]
        public float LocalScoreToReport { get; set; } = 35f;

        [Description("爆头率阈值（0-1）。超过则加分。")]
        public float HeadshotRatioThreshold { get; set; } = 0.75f;

        [Description("命中率阈值（0-1）。超过则加分。")]
        public float AccuracyThreshold { get; set; } = 0.80f;

        [Description("每分钟击杀数阈值。超过则加分。")]
        public float KillsPerMinuteThreshold { get; set; } = 5f;

        [Description("远距离爆头判定距离（米）。超过该距离的爆头额外加分。")]
        public float LongRangeHeadshotDistance { get; set; } = 45f;

        [Description("远距离爆头占爆头总数的比例阈值（0-1）。")]
        public float LongRangeHeadshotRatioThreshold { get; set; } = 0.4f;

        [Description("单发伤害上限。超过视为异常（正常武器不会超过这个值）。")]
        public float MaxReasonableDamage { get; set; } = 120f;

        [Description("每秒射速上限。超过物理上不可能的射速则加分。")]
        public float MaxReasonableShotsPerSecond { get; set; } = 20f;

        // ───────────── 移动类检测（服务端权威数据，可靠性高）─────────────

        [Description("是否启用移动类检测（速度/瞬移/飞天）。位置是服务端权威数据，可靠性高，建议开启。")]
        public bool EnableMovementDetection { get; set; } = true;

        [Description("允许的最高水平移动速度（米/秒）。人类角色正常约 5-7，留余量到 12。")]
        public float MaxReasonableSpeed { get; set; } = 12f;

        [Description("允许的最高垂直移动速度（米/秒）。超过就是飞天/悬浮。正常跳跃约 5。")]
        public float MaxReasonableVerticalSpeed { get; set; } = 10f;

        [Description("单次采样位移超过多少米算瞬移。")]
        public float TeleportDistance { get; set; } = 25f;

        [Description("位置采样间隔（秒）。越小越灵敏但越费性能，建议 0.2-0.5。")]
        public float MovementSampleInterval { get; set; } = 0.3f;

        // ───────────── 旋转 / 抖动检测 ─────────────

        [Description("原地旋转检测：转速超过多少度/秒算异常。人的手腕快速甩枪约 700 度/秒，但持续不了。")]
        public float MaxReasonableYawSpeed { get; set; } = 600f;

        [Description("原地旋转检测：连续多少次同向高速旋转才判定。")]
        public int SpinStreakNeeded { get; set; } = 8;

        [Description("抖动检测：朝向反向翻转时，转速超过多少度/秒才算。")]
        public float JitterYawSpeed { get; set; } = 500f;

        [Description("抖动检测：连续多少次反向翻转才判定。")]
        public int JitterStreakNeeded { get; set; } = 8;

        // ───────────── 其他检测 ─────────────

        [Description("不换弹最多能连打多少发。超过视为无限弹药。普通步枪弹匣约 30-40。")]
        public int MaxReasonableShotsPerMagazine { get; set; } = 60;

        [Description("单次治疗量上限。超过视为异常治疗。")]
        public float MaxReasonableHeal { get; set; } = 200f;

        [Description("是否检测命中隐身（SCP-268）目标。可靠性中等：正常玩家看不到隐身目标，但流弹可能误中。")]
        public bool DetectInvisibleTargetHits { get; set; } = true;

        // ───────────── 透视预判检测 ─────────────

        [Description(
            "是否启用【透视预判】检测。\n" +
            "原理：透视玩家的准星总是提前对着他看不见的敌人。\n" +
            "服务端看不到渲染，但能看到「准星朝向」和「敌人在哪个房间」，\n" +
            "所以可以统计「瞄着一个不同房间里的敌人」的次数。\n" +
            "偶尔几次是运气或预瞄，次数多了就不正常。")]
        public bool DetectEspPrediction { get; set; } = true;

        [Description("准星方向与敌人方向夹角小于这个值，算「瞄着」。单位：度。")]
        public float EspPredictionAngle { get; set; } = 8f;

        [Description("超过这个距离的敌人不计入（太远了瞄着也没意义）。单位：米。")]
        public float EspPredictionMaxDistance { get; set; } = 60f;

        [Description("统计窗口。单位：秒。")]
        public int EspPredictionWindowSeconds { get; set; } = 30;

        [Description("窗口内「瞄着看不见的敌人」达到这个次数 → 记为可疑。")]
        public int EspPredictionThreshold { get; set; } = 12;

        [Description(
            "窗口内达到这个次数 → 升为硬违规（送 AI 复核的门槛会降低）。\n" +
            "建议设得比上面高一些，避免误伤习惯预瞄的玩家。")]
        public int EspPredictionHard { get; set; } = 25;

        // ───────────── 刷物品封堵 ─────────────

        [Description(
            "检测到刷物品后，是否在接下来一段时间里【持续收掉】新获得的物品。\n" +
            "关掉的话只清一次背包，但作弊者继续刷还是会拿到东西。")]
        public bool BlockItemsAfterSpam { get; set; } = true;

        [Description("封堵持续多少秒。")]
        public int ItemSpamBlockSeconds { get; set; } = 15;

        [Description("是否检测 NoClip。这是服务端直接可读的状态位，几乎零误报，建议保持开启。")]
        public bool DetectNoclip { get; set; } = true;

        // ───────────── 刷物品检测与处置 ─────────────

        [Description("刷物品检测的时间窗口（秒）。在这个窗口内拿到超过阈值的物品就算刷物品。")]
        public int ItemSpamWindowSeconds { get; set; } = 10;

        [Description("窗口内拿到多少件物品算刷物品。正常玩家 10 秒内拿不到这么多。")]
        public int ItemSpamThreshold { get; set; } = 8;

        [Description("检测到刷物品时，直接清空该玩家的背包。这是可逆处置，伤害远小于封禁。")]
        public bool ClearInventoryOnItemSpam { get; set; } = true;

        // ───────────── 处死与嘲讽 ─────────────

        [Description("处死后给作弊者显示的话。留空则不显示。支持 {score} 和 {reason} 占位符。")]
        public string TauntMessage { get; set; } = "叮检测到宿主开挂已开启超级大肥鱼反外挂系统";

        [Description("嘲讽显示几秒。")]
        public float TauntDuration { get; set; } = 12f;

        [Description("处置作弊者时，是否向全服所有人公告。")]
        public bool AnnouncePunishmentToAll { get; set; } = true;

        [Description("全服公告的文案。支持 {player} {action} {score} {reason} 占位符。")]
        public string PunishmentAnnouncement { get; set; } = "检测到作弊者{player}";

        [Description("全服公告显示几秒。")]
        public ushort PunishmentAnnouncementDuration { get; set; } = 10;

        [Description("人类反应时间下限（毫秒）。目标进入同房间后低于这个时间就被命中，算可疑。人类极限约 150-200ms。")]
        public int HumanReactionFloorMs { get; set; } = 150;

        [Description("管理员面板刷新间隔（秒）。")]
        public float PanelRefreshSeconds { get; set; } = 2f;

        [Description("管理员面板最多显示几个玩家。")]
        public int PanelTopCount { get; set; } = 5;

        // ───────────── 成本控制 ─────────────

        [Description("每个玩家每局最多调用几次 DeepSeek。")]
        public int MaxApiCallsPerPlayerPerRound { get; set; } = 2;

        [Description("整局最多调用几次 DeepSeek（全局上限，防止账单失控）。")]
        public int MaxApiCallsPerRound { get; set; } = 15;

        [Description("同一个玩家两次 API 复核的最小间隔（秒）。")]
        public int CooldownSeconds { get; set; } = 120;

        // ───────────── 处置方式 ─────────────

        [Description("AI 判定嫌疑度达到多少才执行动作（0-100）。")]
        public int VerdictThreshold { get; set; } = 80;

        [Description("存在「物理上不可能」的硬违规（如单发伤害超过配置上限 3 倍、射速超过 2 倍）时，改用这个更低的门槛。默认 50，因为这类读数基本等同于铁证。")]
        public int HardViolationThreshold { get; set; } = 50;

        [Description("处置方式：Alert=只通报管理员 / Kick=踢出 / Ban=封禁。强烈建议先用 Alert 观察一段时间。")]
        // 默认走递进处置 —— 前几次处死，后面才开始封
        public VerdictAction Action { get; set; } = VerdictAction.Ban;

        [Description("封禁时长（天）。仅当 Action=Ban 且「递进封禁」关闭时使用。")]
        public int BanDurationDays { get; set; } = 7;

        [Description(
            "是否启用递进封禁（累犯加重）。开启后按下面的阶梯，按每个人的历史封禁次数决定时长。\n" +
            "封禁次数记录在 Configs\\Plugins\\deepseek_anticheat\\bans.json，重启不丢。")]
        public bool UseProgressiveBans { get; set; } = true;

        [Description(
            "递进封禁阶梯（天），按次序对应第 1、2、3... 次封禁。\n" +
            "0 表示永久封禁。超出列表长度就按最后一项算。\n" +
            "默认：第1次3天 / 第2次7天 / 第3次90天 / 第4次365天 / 第5次起永久。")]
        public List<string> PunishmentLadder { get; set; } = new List<string> { "kill", "kill", "3", "7", "90", "365", "perm" };

        [Description("是否把判定结果广播给在线管理员（RemoteAdmin 广播）。")]
        public bool AnnounceToAdmins { get; set; } = true;

        [Description("是否把完整报告写到服务端日志。")]
        public bool LogFullReport { get; set; } = true;

        [Description("发给 AI 时是否包含玩家昵称与 UserID。关掉可减少隐私外泄（但会降低判定质量）。")]
        public bool IncludePlayerIdentity { get; set; } = false;
    }

    /// <summary>判定后的处置方式。</summary>
    public enum VerdictAction
    {
        /// <summary>只通报，不动玩家。</summary>
        Alert,

        /// <summary>踢出。</summary>
        Kick,

        /// <summary>封禁。</summary>
        Ban,

        /// <summary>当场处死（并显示嘲讽）。最轻的即时处置，适合测试与轻度处置。</summary>
        Kill,
    }
}
