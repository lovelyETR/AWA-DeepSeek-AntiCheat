// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  作者：AWA　　本插件全由 DSH 开发
//
//  累犯封禁台账：记录每个玩家被封禁过几次，用来实现递进封禁。
//  数据落盘到 Configs\Plugins\deepseek_anticheat\bans.json，重启不丢。
// ============================================================================

namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    /// <summary>累犯封禁台账。</summary>
    internal sealed class BanLedger
    {
        private const string FileName = "bans.json";

        private readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private bool dirty;

        /// <summary>一条记录。</summary>
        private sealed class Entry
        {
            public int Count;

            public string Nickname;

            public string LastReason;

            public string LastAt;
        }

        /// <summary>台账文件路径。</summary>
        internal static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EXILED", "Configs", "Plugins", "deepseek_anticheat");
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                catch
                {
                    // 建不出来就算了
                }

                return Path.Combine(dir, FileName);
            }
        }

        /// <summary>某个玩家已经被封过几次。0 表示第一次。</summary>
        internal int GetCount(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return 0;
            }

            return this.entries.TryGetValue(userId, out Entry e) ? e.Count : 0;
        }

        /// <summary>
        /// 算出这次该封多久，并把次数 +1。
        /// ladder 里的 0 表示永久。
        /// </summary>
        internal Punishment NextPunishment(string userId, string nickname, string reason, IList<string> ladder, out int offense)
        {
            offense = this.GetCount(userId) + 1;

            string item;
            if (ladder == null || ladder.Count == 0)
            {
                item = "kill";
            }
            else
            {
                int idx = Math.Min(offense, ladder.Count) - 1;
                item = ladder[idx];
            }

            this.entries[userId] = new Entry
            {
                Count = offense,
                Nickname = nickname,
                LastReason = reason,
                LastAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };
            this.dirty = true;
            this.Save();

            return Punishment.Parse(item);
        }

        /// <summary>手动把某个玩家的次数清零（封错了要撤销时用）。</summary>
        internal bool Reset(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return false;
            }

            if (this.entries.Remove(userId))
            {
                this.dirty = true;
                this.Save();
                return true;
            }

            return false;
        }

        /// <summary>列出台账内容（给 dsac bans 用）。</summary>
        internal string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("累犯封禁台账");
            sb.AppendLine("  文件: " + FilePath);
            sb.AppendLine();

            if (this.entries.Count == 0)
            {
                sb.AppendLine("  （还没有人被封过）");
                return sb.ToString();
            }

            sb.AppendLine("  被封次数   玩家                          最近一次");
            sb.AppendLine("  " + new string('-', 70));
            foreach (KeyValuePair<string, Entry> kv in this.entries)
            {
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  {0,-10} {1,-28} {2}\n",
                    kv.Value.Count,
                    Trunc(kv.Value.Nickname ?? "?", 26),
                    kv.Value.LastAt ?? "?");
            }

            return sb.ToString();
        }

        /// <summary>读盘。</summary>
        internal void Load()
        {
            this.entries.Clear();
            this.dirty = false;

            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    return;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);

                // 手写极简 JSON 解析：格式由本类自己写，结构可控
                // {"counts":{"<userId>":{"count":N,"nick":"...","reason":"...","at":"..."}}}
                int i = 0;
                while (true)
                {
                    int keyStart = json.IndexOf("\"uid\":\"", i, StringComparison.Ordinal);
                    if (keyStart < 0)
                    {
                        break;
                    }

                    keyStart += 7;
                    int keyEnd = json.IndexOf('"', keyStart);
                    if (keyEnd < 0)
                    {
                        break;
                    }

                    string uid = Unescape(json.Substring(keyStart, keyEnd - keyStart));
                    int objEnd = json.IndexOf('}', keyEnd);
                    if (objEnd < 0)
                    {
                        break;
                    }

                    string body = json.Substring(keyEnd, objEnd - keyEnd);

                    var e = new Entry
                    {
                        Count = ReadInt(body, "count"),
                        Nickname = ReadStr(body, "nick"),
                        LastReason = ReadStr(body, "reason"),
                        LastAt = ReadStr(body, "at"),
                    };

                    if (!string.IsNullOrEmpty(uid))
                    {
                        this.entries[uid] = e;
                    }

                    i = objEnd + 1;
                }
            }
            catch
            {
                // 解析失败就当空台账，不影响主流程
            }
        }

        /// <summary>写盘。</summary>
        internal void Save()
        {
            if (!this.dirty)
            {
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.Append("{\"bans\":[");
                bool first = true;
                foreach (KeyValuePair<string, Entry> kv in this.entries)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    sb.Append("{\"uid\":\"").Append(Escape(kv.Key)).Append('"');
                    sb.Append(",\"count\":").Append(kv.Value.Count);
                    sb.Append(",\"nick\":\"").Append(Escape(kv.Value.Nickname ?? string.Empty)).Append('"');
                    sb.Append(",\"reason\":\"").Append(Escape(kv.Value.LastReason ?? string.Empty)).Append('"');
                    sb.Append(",\"at\":\"").Append(Escape(kv.Value.LastAt ?? string.Empty)).Append('"');
                    sb.Append('}');
                }

                sb.Append("]}");

                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
                this.dirty = false;
            }
            catch
            {
                // 写不进去也不该影响封禁动作本身
            }
        }

        private static int ReadInt(string body, string key)
        {
            int p = body.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (p < 0)
            {
                return 0;
            }

            p += key.Length + 3;
            int end = p;
            while (end < body.Length && (char.IsDigit(body[end]) || body[end] == '-'))
            {
                end++;
            }

            return int.TryParse(body.Substring(p, end - p), out int v) ? v : 0;
        }

        private static string ReadStr(string body, string key)
        {
            int p = body.IndexOf("\"" + key + "\":\"", StringComparison.Ordinal);
            if (p < 0)
            {
                return null;
            }

            p += key.Length + 4;
            int end = p;
            var sb = new StringBuilder();
            while (end < body.Length && body[end] != '"')
            {
                if (body[end] == '\\' && end + 1 < body.Length)
                {
                    end++;
                    sb.Append(body[end] == 'n' ? '\n' : body[end] == 't' ? '\t' : body[end]);
                }
                else
                {
                    sb.Append(body[end]);
                }

                end++;
            }

            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0)
            {
                return s;
            }

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] == 'n' ? '\n' : s[i] == 't' ? '\t' : s[i]);
                }
                else
                {
                    sb.Append(s[i]);
                }
            }

            return sb.ToString();
        }

        private static string Trunc(string s, int n) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }
}
