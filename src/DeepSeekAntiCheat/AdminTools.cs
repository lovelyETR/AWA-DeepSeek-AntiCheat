// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// 游戏内管理工具：HUD 面板渲染 + 证据落盘。
    /// </summary>
    internal static class AdminTools
    {
        /// <summary>证据文件存放目录。</summary>
        internal static string EvidenceDir
        {
            get
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EXILED", "Configs", "Plugins", "deepseek_anticheat", "evidence");

                try
                {
                    if (!Directory.Exists(baseDir))
                    {
                        Directory.CreateDirectory(baseDir);
                    }
                }
                catch
                {
                    // 建不出来就算了，导出时会再试
                }

                return baseDir;
            }
        }

        /// <summary>
        /// 渲染管理员 HUD 面板。
        ///
        /// 用 ShowHint 而不是 Broadcast —— 面板要常驻刷新，
        /// Broadcast 会挡住玩家视线中央，Hint 在右上角更合适。
        /// </summary>
        internal static string RenderPanel(IEnumerable<PlayerStats> top, Config cfg, int trackedCount, int apiCalls)
        {
            // ── 配色方案：黑 / 白 / 蓝 ──
            //   #4da6ff  主蓝（标题、高分）
            //   #ffffff  白（玩家名）
            //   #8fa8c4  灰蓝（中分）
            //   #7a7a7a  灰（低分、次要信息）
            //   #00d4ff  亮青蓝（硬违规标记）
            const string Blue = "#4da6ff";
            const string BlueDim = "#8fa8c4";
            const string White = "#ffffff";
            const string Gray = "#7a7a7a";
            const string Cyan = "#00d4ff";

            var sb = new StringBuilder();
            sb.Append("<size=19><color=").Append(Blue).Append("><b>◆ AWA 反作弊面板</b></color></size>\n");
            sb.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<size=12><color={0}>跟踪 {1} 人  ·  AI {2}/{3}  ·  每 {4:F0}s 刷新</color></size>\n",
                Gray, trackedCount, apiCalls, cfg.MaxApiCallsPerRound, cfg.PanelRefreshSeconds));
            sb.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<size=12><color={0}>────────────────────────────</color></size>\n",
                BlueDim));

            var list = top.ToList();
            if (list.Count == 0)
            {
                sb.Append(string.Format(
                    CultureInfo.InvariantCulture,
                    "<size=14><color={0}>暂无数据（本局还没人开枪）</color></size>",
                    Gray));
                return sb.ToString();
            }

            int rank = 1;
            foreach (PlayerStats s in list)
            {
                Heuristics.Result r = Heuristics.Evaluate(s, cfg);

                // 黑白蓝前提下用「蓝色的亮度」表达严重程度，不用红黄绿
                string scoreColor = r.Score >= 70f ? Blue
                                  : r.Score >= 35f ? BlueDim
                                  : Gray;

                // 高分加粗，让它在白字里跳出来
                string weightOpen = r.Score >= 70f ? "<b>" : string.Empty;
                string weightClose = r.Score >= 70f ? "</b>" : string.Empty;

                string marks = string.Empty;
                if (r.HasHardViolation)
                {
                    marks += string.Format(CultureInfo.InvariantCulture, "  <color={0}><b>[硬违规]</b></color>", Cyan);
                }

                if (s.Followed)
                {
                    marks += string.Format(CultureInfo.InvariantCulture, "  <color={0}>★</color>", Blue);
                }

                sb.Append(string.Format(
                    CultureInfo.InvariantCulture,
                    "<size=14><color={0}>{1}.</color> {2}<color={3}>{4}</color>{5}  {6}<color={7}>{8:F0} 分</color>{9}{10}</size>\n",
                    Blue, rank,
                    weightOpen, White, Trunc(s.Nickname, 14), weightClose,
                    weightOpen, scoreColor, r.Score, weightClose,
                    marks));

                // 只给前两名显示明细，避免面板太长挡视线
                if (rank <= 2 && r.Reasons.Count > 0)
                {
                    sb.Append(string.Format(
                        CultureInfo.InvariantCulture,
                        "<size=11><color={0}>     └ {1}</color></size>\n",
                        Gray, Trunc(r.Reasons[0], 44)));
                }

                rank++;
            }

            sb.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<size=12><color={0}>────────────────────────────</color></size>",
                BlueDim));

            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// 渲染纯文本版面板（剥掉富文本标记）—— 给 dsac panel preview 用，
        /// 让管理员在不依赖 HUD 的情况下确认面板内容和排版。
        /// </summary>
        internal static string RenderPanelPlain(IEnumerable<PlayerStats> top, Config cfg, int trackedCount, int apiCalls)
        {
            string rich = RenderPanel(top, cfg, trackedCount, apiCalls);
            var sb = new StringBuilder(rich.Length);

            int depth = 0;
            foreach (char ch in rich)
            {
                if (ch == '<')
                {
                    depth++;
                    continue;
                }

                if (ch == '>')
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }
                }

                if (depth == 0)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 把判定成立的证据写成 JSON。
        /// 返回写出的文件路径；失败返回 null。
        /// </summary>
        internal static string ExportEvidence(PlayerStats st, DeepSeekVerdict verdict, Heuristics.Result local, Config cfg)
        {
            try
            {
                string safeName = SafeFileName(st.Nickname);
                string file = Path.Combine(
                    EvidenceDir,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyyMMdd_HHmmss}_{1}.json",
                        DateTime.Now, safeName));

                var sb = new StringBuilder(4096);
                sb.Append('{');
                sb.Append("\"exported_at\":").Append(Heuristics.JsonString(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',');
                sb.Append("\"plugin\":").Append(Heuristics.JsonString("AWA :: DeepSeekAntiCheat")).Append(',');
                sb.Append("\"nickname\":").Append(Heuristics.JsonString(st.Nickname)).Append(',');
                sb.Append("\"user_id\":").Append(Heuristics.JsonString(st.UserId)).Append(',');
                sb.Append("\"round_minutes\":").Append(st.RoundMinutes.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"local_score\":").Append(local.Score.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"hard_violations\":[");
                for (int i = 0; i < local.HardViolations.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(Heuristics.JsonString(local.HardViolations[i]));
                }

                sb.Append("],");
                sb.Append("\"local_reasons\":[");
                for (int i = 0; i < local.Reasons.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(Heuristics.JsonString(local.Reasons[i]));
                }

                sb.Append("],");
                sb.Append("\"ai_suspicion\":").Append(verdict?.Suspicion ?? -1).Append(',');
                sb.Append("\"ai_reasoning\":").Append(Heuristics.JsonString(verdict?.Reasoning)).Append(',');
                sb.Append("\"ai_action\":").Append(Heuristics.JsonString(verdict?.RecommendedAction)).Append(',');
                sb.Append("\"config_action\":").Append(Heuristics.JsonString(cfg.Action.ToString())).Append(',');
                sb.Append("\"timeline\":[");
                for (int i = 0; i < st.Timeline.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(Heuristics.JsonString(st.Timeline[i].ToString()));
                }

                sb.Append("]}");

                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                return file;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把行为时间线渲染成可读文本（给 dsac replay 用）。</summary>
        internal static string RenderTimeline(PlayerStats st, int maxLines)
        {
            var sb = new StringBuilder();
            sb.Append(string.Format(
                CultureInfo.InvariantCulture,
                "{0}（{1}）行为时间线 —— 共 {2} 条\n",
                st.Nickname, Trunc(st.UserId, 20), st.Timeline.Count));

            if (st.Timeline.Count == 0)
            {
                sb.Append("  （本局还没有记录到事件）");
                return sb.ToString();
            }

            int skip = Math.Max(0, st.Timeline.Count - maxLines);
            if (skip > 0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "  （省略较早的 {0} 条）\n", skip));
            }

            for (int i = skip; i < st.Timeline.Count; i++)
            {
                sb.Append("  ").Append(st.Timeline[i]).Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>列出证据文件。</summary>
        internal static string ListEvidence(int max)
        {
            try
            {
                var dir = new DirectoryInfo(EvidenceDir);
                FileInfo[] files = dir.GetFiles("*.json");
                if (files.Length == 0)
                {
                    return "证据目录还是空的：" + EvidenceDir;
                }

                Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                var sb = new StringBuilder();
                sb.Append("证据目录: ").Append(EvidenceDir).Append('\n');
                sb.Append(string.Format(CultureInfo.InvariantCulture, "共 {0} 个文件，最近 {1} 个：\n", files.Length, Math.Min(max, files.Length)));

                for (int i = 0; i < Math.Min(max, files.Length); i++)
                {
                    sb.Append(string.Format(
                        CultureInfo.InvariantCulture,
                        "  {0:MM-dd HH:mm:ss}  {1}  ({2:N0} 字节)\n",
                        files[i].LastWriteTime, files[i].Name, files[i].Length));
                }

                return sb.ToString().TrimEnd('\n');
            }
            catch (Exception e)
            {
                return "读取证据目录失败: " + e.Message;
            }
        }

        private static string Trunc(string s, int n) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        /// <summary>把昵称清洗成安全文件名。</summary>
        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unknown";
            }

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }

            string s = sb.ToString().Trim();
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }
    }
}
