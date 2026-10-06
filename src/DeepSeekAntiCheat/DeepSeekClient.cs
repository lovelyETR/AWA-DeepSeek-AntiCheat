// ============================================================================
//  AWA :: DeepSeekAntiCheat
//  ---------------------------------------------------------------------------
//  本文件由 AWA 编写。
// ============================================================================
namespace DeepSeekAntiCheat
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;

    /// <summary>AI 的判定结论。</summary>
    public sealed class DeepSeekVerdict
    {
        /// <summary>嫌疑度 0-100。</summary>
        public int Suspicion { get; set; }

        /// <summary>人类可读的判定理由。</summary>
        public string Reasoning { get; set; } = string.Empty;

        /// <summary>建议动作：none / watch / kick / ban。</summary>
        public string RecommendedAction { get; set; } = "none";

        /// <summary>是否成功拿到有效结论。</summary>
        public bool Ok { get; set; }

        /// <summary>失败原因（Ok=false 时有意义）。</summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>原始返回，便于排查。</summary>
        public string Raw { get; set; } = string.Empty;

        public override string ToString() =>
            this.Ok
                ? string.Format(CultureInfo.InvariantCulture, "suspect={0}% action={1} reason={2}", this.Suspicion, this.RecommendedAction, this.Reasoning)
                : "FAILED: " + this.Error;
    }

    /// <summary>
    /// DeepSeek API 客户端。全部 IO 走异步，绝不阻塞主线程。
    /// </summary>
    public sealed class DeepSeekClient : IDisposable
    {
        private const string SystemPrompt =
            "你是 SCP: Secret Laboratory 服务器的反作弊分析员。" +
            "你会收到某个玩家本局的行为统计。请判断该玩家作弊的可能性，并给出简洁的中文理由。" +
            "注意：数据来自服务端，可能存在误报（高玩、运气、特殊角色、SCP 身份都会造成异常数值）。" +
            "宁可放过也不要冤枉人 —— 除非证据明确，否则不要给高分。" +
            "必须只输出 JSON，不要任何额外文字，格式：" +
            "{\"suspicion\": <0-100 整数>, \"reasoning\": \"<中文理由，60字以内>\", \"recommended_action\": \"<none|watch|kick|ban>\"}";

        private readonly Config config;
        private readonly HttpClient http;

        public DeepSeekClient(Config config)
        {
            this.config = config;
            this.http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds)),
            };
        }

        /// <summary>
        /// 是否具备调用条件。
        /// 允许空 Key（本地模型不需要），但如果端点还是官方 DeepSeek 又没填 Key，
        /// 那这个组合永远只会拿到 401，直接视为未配置，免得白烧调用预算。
        /// </summary>
        public bool IsConfigured
        {
            get
            {
                if (!this.config.EnableAiReview)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(this.config.ApiKey))
                {
                    return true;
                }

                string url = this.config.ApiBaseUrl ?? string.Empty;
                if (url.Length == 0)
                {
                    return false;
                }

                return url.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) < 0;
            }
        }

        /// <summary>让 DeepSeek 复核一份行为报告。异常一律吞掉并返回 Ok=false。</summary>
            // AWA :: 唯一发起网络请求的地方。
            // 真 API Key 只会出现在这条路径上，不会写进任何日志。
        public async Task<DeepSeekVerdict> ReviewAsync(string reportJson)
        {
            if (!this.IsConfigured)
            {
                return new DeepSeekVerdict { Ok = false, Error = "未配置 ApiKey" };
            }

            try
            {
                string url = this.config.ApiBaseUrl.TrimEnd('/') + "/chat/completions";
                string body = this.BuildRequestBody(reportJson);

                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    string key = this.config.ApiKey;
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key.Trim());
                    }

                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    using (HttpResponseMessage resp = await this.http.SendAsync(req).ConfigureAwait(false))
                    {
                        string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!resp.IsSuccessStatusCode)
                        {
                            return new DeepSeekVerdict
                            {
                                Ok = false,
                                Error = string.Format(CultureInfo.InvariantCulture, "HTTP {0}: {1}", (int)resp.StatusCode, Truncate(text, 300)),
                                Raw = text,
                            };
                        }

                        return this.ParseResponse(text);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return new DeepSeekVerdict { Ok = false, Error = "请求超时" };
            }
            catch (Exception e)
            {
                return new DeepSeekVerdict { Ok = false, Error = e.GetType().Name + ": " + e.Message };
            }
        }

        public void Dispose() => this.http?.Dispose();

        // ──────────────────────────────────────────────

        private string BuildRequestBody(string reportJson)
        {
            var sb = new StringBuilder(2048);
            sb.Append('{');
            sb.Append("\"model\":").Append(Heuristics.JsonString(this.config.Model)).Append(',');
            sb.Append("\"temperature\":0,");
            sb.Append("\"max_tokens\":").Append(Math.Max(64, this.config.MaxTokens)).Append(',');
            if (this.config.UseJsonResponseFormat)
            {
                sb.Append("\"response_format\":{\"type\":\"json_object\"},");
            }

            sb.Append("\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":").Append(Heuristics.JsonString(SystemPrompt)).Append("},");
            sb.Append("{\"role\":\"user\",\"content\":").Append(Heuristics.JsonString("玩家本局行为统计：\n" + reportJson)).Append('}');
            sb.Append("]}");
            return sb.ToString();
        }

        private DeepSeekVerdict ParseResponse(string text)
        {
            var verdict = new DeepSeekVerdict { Raw = text };

            var js = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            object root;
            try
            {
                root = js.DeserializeObject(text);
            }
            catch (Exception e)
            {
                verdict.Error = "外层 JSON 解析失败: " + e.Message;
                return verdict;
            }

            string content = DigString(root, "choices", 0, "message", "content");
            if (string.IsNullOrEmpty(content))
            {
                verdict.Error = "响应里没有 choices[0].message.content";
                return verdict;
            }

            verdict.Raw = content;

            object inner;
            try
            {
                inner = js.DeserializeObject(ExtractJsonObject(content));
            }
            catch (Exception e)
            {
                verdict.Error = "模型输出不是合法 JSON: " + e.Message;
                return verdict;
            }

            if (!(inner is Dictionary<string, object> map))
            {
                verdict.Error = "模型输出 JSON 顶层不是对象";
                return verdict;
            }

            verdict.Suspicion = Clamp(ToInt(Get(map, "suspicion")), 0, 100);
            verdict.Reasoning = ToStr(Get(map, "reasoning"));
            verdict.RecommendedAction = ToStr(Get(map, "recommended_action"));
            if (string.IsNullOrEmpty(verdict.RecommendedAction))
            {
                verdict.RecommendedAction = "none";
            }

            verdict.Ok = true;
            return verdict;
        }

        /// <summary>
        /// 从模型输出里抠出 JSON 对象。
        /// 商业 API 在 response_format 下会直接给纯 JSON；
        /// 本地小模型经常先写一段解释再给 JSON，或者包在 ```json 里，
        /// 所以这里做「剥代码围栏 + 字符串感知的括号配对」两级兜底。
        /// </summary>
        private static string ExtractJsonObject(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            s = StripCodeFence(s);

            if (s.StartsWith("{", StringComparison.Ordinal))
            {
                return s;
            }

            int start = s.IndexOf('{');
            if (start < 0)
            {
                return s;
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return s.Substring(start, i - start + 1);
                    }
                }
            }

            // 括号没配平，退回从第一个 { 到结尾
            return s.Substring(start);
        }

        /// <summary>模型有时会把 JSON 包在 ```json 里，这里剥掉。</summary>
        private static string StripCodeFence(string s)
        {
            s = s.Trim();
            if (!s.StartsWith("```", StringComparison.Ordinal))
            {
                return s;
            }

            int nl = s.IndexOf('\n');
            if (nl >= 0)
            {
                s = s.Substring(nl + 1);
            }

            int end = s.LastIndexOf("```", StringComparison.Ordinal);
            if (end >= 0)
            {
                s = s.Substring(0, end);
            }

            return s.Trim();
        }

        private static object Get(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object v) ? v : null;

        /// <summary>在嵌套的 Dictionary/Object[] 里按路径取值，取到字符串为止。</summary>
        private static string DigString(object root, params object[] path)
        {
            object cur = root;
            foreach (object step in path)
            {
                if (step is string key)
                {
                    if (!(cur is Dictionary<string, object> map) || !map.TryGetValue(key, out cur))
                    {
                        return null;
                    }
                }
                else if (step is int idx)
                {
                    if (!(cur is object[] arr) || idx < 0 || idx >= arr.Length)
                    {
                        return null;
                    }

                    cur = arr[idx];
                }
            }

            return cur as string;
        }

        private static int ToInt(object o)
        {
            if (o == null)
            {
                return 0;
            }

            if (o is int i)
            {
                return i;
            }

            if (o is double d)
            {
                return (int)Math.Round(d);
            }

            if (o is decimal m)
            {
                return (int)Math.Round(m);
            }

            return int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out int r) ? r : 0;
        }

        private static string ToStr(object o) => o == null ? string.Empty : Convert.ToString(o, CultureInfo.InvariantCulture);

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
