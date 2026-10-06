# 接入 AI 指南

DeepSeekAntiCheat 的 AI 部分是**已经写好在插件里的**，你只需要填配置。这份文档讲清楚三条路线怎么选、怎么填、怎么验证。

---

## 先明确：插件发出去的是什么

每次复核，插件向你的 API 端点发一个标准 OpenAI 格式的请求：

```json
POST {ApiBaseUrl}/chat/completions
Authorization: Bearer {ApiKey}

{
  "model": "deepseek-chat",
  "temperature": 0,
  "max_tokens": 400,
  "response_format": { "type": "json_object" },
  "messages": [
    { "role": "system", "content": "你是 SCP: Secret Laboratory 服务器的反作弊分析员…" },
    { "role": "user",   "content": "玩家本局行为统计：\n{…JSON…}" }
  ]
}
```

**因为是标准 OpenAI 格式，任何兼容该格式的服务都能直接接** —— 这也是为什么你可以随便换供应商。

### 发出去的玩家数据（默认）

```json
{
  "session_minutes": 12.4,
  "shots_fired": 100,
  "shots_hit": 95,
  "headshots": 90,
  "long_range_headshots": 70,
  "accuracy": 0.95,
  "headshot_ratio": 0.947,
  "kills": 12,
  "deaths": 3,
  "kills_per_minute": 6.0,
  "max_single_hit_damage": 250,
  "longest_hit_distance_m": 180,
  "peak_shots_per_second": 40,
  "local_heuristic_score": 100,
  "local_flags": ["爆头率 94.7%…", "峰值射速 40 发/秒…"]
}
```

**默认不含昵称和 SteamID**（`include_player_identity: false`）。要打开的话，会增加 `nickname` 和 `user_id` 两个字段。

---

## 路线选择

| 路线 | 成本 | 隐私 | 判定质量 | 适合 |
|---|---|---|---|---|
| **A. DeepSeek 官方** | 极低（约 ¥1/百万 token） | 数据发往第三方 | 好 | 大多数服务器 |
| **B. 其他商业兼容 API** | 视供应商 | 数据发往第三方 | 好 | 已有其他家额度 |
| **C. 本地模型** | 电费 | **数据不出机器** | 取决于模型大小 | 注重隐私 / 想省钱 |

---

## 路线 A：DeepSeek 官方（推荐）

### 1. 拿 API Key

1. 打开 https://platform.deepseek.com
2. 注册 / 登录
3. 左侧 **API Keys** → **创建 API Key**
4. 复制那串 `sk-` 开头的字符串

> ⚠️ **Key 只显示一次**，关掉页面就看不到了。先存好。

### 2. 充值

DeepSeek 需要预充值。左侧 **充值**，充个 ¥10 就够跑很久（下面有成本估算）。

### 3. 填进配置

编辑 `%AppData%\EXILED\Configs\Plugins\deepseek_anticheat\<端口>.yml`：

```yaml
deepseek_anticheat:
  is_enabled: true
  enable_ai_review: true
  api_key: 'sk-你复制的那串'
  api_base_url: 'https://api.deepseek.com'
  model: 'deepseek-chat'
  use_json_response_format: true
  timeout_seconds: 20
```

### 4. 重载

游戏内 RemoteAdmin 控制台：
```
exiled reload deepseek_anticheat
```

---

## 路线 B：其他兼容 API

只要服务商兼容 OpenAI 的 `/chat/completions`，把 `api_base_url` 和 `model` 改掉就行。

| 服务商 | api_base_url | model 示例 |
|---|---|---|
| 硅基流动 SiliconFlow | `https://api.siliconflow.cn/v1` | `Qwen/Qwen2.5-7B-Instruct` |
| 阿里云百炼 | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `qwen-plus` |
| 智谱 GLM | `https://open.bigmodel.cn/api/paas/v4` | `glm-4-flash` |
| 月之暗面 | `https://api.moonshot.cn/v1` | `moonshot-v1-8k` |
| OpenAI | `https://api.openai.com/v1` | `gpt-4o-mini` |

> **注意**：填的是到 `/v1` 为止，**不要**自己加 `/chat/completions` —— 插件会自动拼。

---

## 路线 C：本地模型（免费 + 数据不出机器）

这是**隐私最好**的方案：玩家数据永远不离开你的服务器。

### 方案 1：Ollama（最简单）

**装 Ollama**：https://ollama.com/download

**拉一个模型**（反作弊这种结构化判定，7B 级别够用）：
```bash
ollama pull qwen2.5:7b-instruct
```

**确认服务在跑**：
```bash
curl http://127.0.0.1:11434/v1/models
```

**填配置**：
```yaml
deepseek_anticheat:
  is_enabled: true
  enable_ai_review: true
  api_key: ''                                  # ← 本地模型不需要 Key，留空
  api_base_url: 'http://127.0.0.1:11434/v1'
  model: 'qwen2.5:7b-instruct'
  use_json_response_format: false              # ← Ollama 对 response_format 支持不稳定，关掉
  timeout_seconds: 60                          # ← 本地推理慢，超时给大一点
```

### 方案 2：LM Studio（带图形界面）

1. 装 https://lmstudio.ai
2. 下载模型（推荐 Qwen2.5-7B-Instruct 或 Llama-3.1-8B）
3. 切到 **Local Server** 标签，点 **Start Server**（默认端口 1234）
4. 配置：

```yaml
deepseek_anticheat:
  api_key: ''
  api_base_url: 'http://127.0.0.1:1234/v1'
  model: 'qwen2.5-7b-instruct'       # 填 LM Studio 里显示的模型标识
  use_json_response_format: false
  timeout_seconds: 90
```

### 本地模型的两个注意点

**① 一定要关 `use_json_response_format`。** 很多本地推理框架不支持这个参数，带着它请求会直接报错。

**② 小模型输出会夹带废话。** 比如：

```
好的，我来分析这个玩家的数据。从爆头率来看……
{"suspicion": 85, "reasoning": "爆头率异常", "recommended_action": "watch"}
以上是我的判断。
```

插件**已经能处理这种情况** —— 解析器会剥掉 ```json 围栏，并做字符串感知的括号配对，从散文里把 JSON 对象抠出来。这个行为有测试覆盖（含"理由里带花括号"的边界情况）。

**③ 模型太小会瞎判。** 3B 以下基本不可用；7B 是及格线；14B+ 明显更稳。如果误报多，先换大模型，再考虑调阈值。

---

## 成本估算

插件有严格的三级限流，**正常服务器一天下来调用次数是个位数到几十次**：

```
每玩家每局最多 2 次  ×  全局每局最多 15 次  ×  同玩家冷却 120 秒
```

按每次请求 ≈ 800 token 输入 + 150 token 输出估算：

| 场景 | 每天调用 | 每月成本（deepseek-chat） |
|---|---|---|
| 小服（每局 8 人，每天 30 局） | 约 20 次 | **＜ ¥0.5** |
| 中服（每局 20 人，每天 80 局） | 约 60 次 | **＜ ¥1.5** |
| 大服（打满全局上限） | 约 300 次 | **约 ¥7** |

> deepseek-chat 定价约 ¥0.5/百万输入 token、¥8/百万输出 token。实际以官网为准。

**成本几乎可以忽略。** 真要担心的话，把 `max_api_calls_per_round` 调小即可。

---

## 怎么确认接上了

### 方法 1：看启动日志

重载插件后，服务端控制台应该**没有**这条警告：

```
[DeepSeekAntiCheat] 未配置 ApiKey —— 插件只做本地启发式评分，不会调用 DeepSeek。
```

**有这条 = 没接上；没有 = 接上了。**

### 方法 2：开 debug 看评分

```yaml
debug: true
```

然后故意把阈值调低，强制触发复核：

```yaml
local_score_to_report: 5      # 临时调低，随便打几枪就会触发
min_samples: 2
cooldown_seconds: 1
max_api_calls_per_player_per_round: 99
max_api_calls_per_round: 99
```

进游戏随便打几枪，控制台应该出现：

```
[DeepSeekAntiCheat] 某某 本地评分 12.3，送去复核。理由: score=12.3 reasons=[…]
[DeepSeekAntiCheat] 玩家 某某 本地评分 12.3；AI 嫌疑度 15%，理由: …（建议 none）
```

**看到第二行 = AI 真的回话了。**

> 验证完**记得把阈值改回去**，否则会疯狂烧 token。

### 方法 3：直接打 API

```bash
curl -X POST "https://api.deepseek.com/chat/completions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer sk-你的key" \
  -d '{"model":"deepseek-chat","messages":[{"role":"user","content":"1+1=?"}]}'
```

正常返回带 `"choices"` 的 JSON。返回 `401 Authentication Fails` 就是 key 不对。

---

## 排错

| 现象 | 原因 | 解决 |
|---|---|---|
| 日志说"未配置 ApiKey" | key 为空且端点是官方 | 填 key，或用本地模型改端点 |
| `HTTP 401` | key 错 / 没充值 / 有空格 | 检查 key，删掉首尾空格 |
| `HTTP 402` | 余额不足 | 充值 |
| `HTTP 404` | URL 拼错 | 只填到 `/v1`，别自己加 `/chat/completions` |
| `HTTP 429` | 触发限流 | 调小 `max_api_calls_per_round` |
| `请求超时` | 网络慢 / 本地模型太慢 | 调大 `timeout_seconds` |
| `模型输出不是合法 JSON` | 小模型不听话 | 换大模型；保留 `use_json_response_format: true` |
| `响应里没有 choices` | 端点不是 OpenAI 兼容格式 | 换供应商或自建代理 |
| 本地模型报 `response_format` 相关错 | 框架不支持 | `use_json_response_format: false` |
| 完全没反应 | 本地评分没到阈值 | 属正常；临时调低 `local_score_to_report` 验证 |

---

## 安全提醒

- **API Key 是明文存在 config.yml 里的。** 别把这个文件提交到 Git，别贴到群里。
- 如果 key 泄露了，立刻去平台后台**删除并重建**。
- 用本地模型的话，把 `api_base_url` 指向 `127.0.0.1` —— **不要**暴露到公网，否则等于给别人一个免费的推理端点。

---

## 一句话总结

**填个 key（或者指向本地 Ollama）就能用。** 插件已经处理好了请求构造、超时、重试预算、JSON 解析兜底和线程安全，你不需要改代码。

想换供应商就改两行配置；想省钱就用本地模型；想验证是否接上就开 debug 把阈值调低打几枪。
