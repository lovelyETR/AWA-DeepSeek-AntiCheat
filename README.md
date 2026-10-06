<div align="center">

# AWA-DeepSeek-AntiCheat

**用 DeepSeek AI 做二次判定的 SCP: Secret Laboratory 服务端反作弊插件**

[![Version](https://img.shields.io/badge/version-Alpha%20v1.0-blue?style=flat-square)]()
[![Platform](https://img.shields.io/badge/platform-EXILED%209.14%20%2F%20LabAPI-orange?style=flat-square)]()
[![Game](https://img.shields.io/badge/game-SCP%3ASL%2014.2.7-red?style=flat-square)]()
[![.NET](https://img.shields.io/badge/.NET-net48-512BD4?style=flat-square)]()
[![License](https://img.shields.io/badge/license-GPL--3.0-green?style=flat-square)]()

作者：**AWA**　　本插件全由 **DSH**（DeepSeek Harness）开发

</div>

---

> ## ⚠️ 水印声明
>
> **分发或转载时必须保留 AWA 水印。移除水印不代表获得授权。**
>
> 插件启动时会打印署名横幅，并自检水印完整性。
> 水印被篡改会在服务端日志里报错。

---

## 这是什么

一个跑在 SCP: Secret Laboratory **服务端**的反作弊插件。

它的判定分两段：

```
玩家行为数据
      ↓
① 本地启发式打分（14 个维度，全部在服务端算，零成本、零延迟）
      ↓  分数超过阈值才继续
② DeepSeek AI 复核（把行为摘要交给 AI，让它判断「这像不像人打的」）
      ↓  嫌疑度超过阈值才处置
③ 处置（按递进阶梯：处死 → 处死 → 封 3 天 → 7 天 → 90 天 → 365 天 → 永久）
```

**为什么要两段**：纯本地规则容易误判高玩，纯 AI 又不能每局都调用（太贵太慢）。
本地先筛掉 99% 的正常玩家，只把可疑的交给 AI 复核 —— 又快又准还省钱。

**AI 是可选的**：不填 API Key 就只跑本地检测，完全不联网、零成本，
只是少了一层复核。

---

## 特性

### 检测能力（14 个维度）

| 类别 | 检测项 |
|---|---|
| **射击** | 爆头率异常 · 命中率异常 · 远距离爆头 · 单发伤害超上限 · 射速超上限 · 连续开枪不换弹 |
| **反应** | 反应时间低于人类极限（< 150 ms）|
| **移动** | 移速异常 · 垂直速度异常 · 瞬移 · NoClip（穿墙）· 原地高速旋转 · 随机抖动 |
| **其他** | 异常治疗量 · 刷物品 · 命中隐身（SCP-268）目标 |

**硬违规**（物理上不可能的读数，例如移速 100 米/秒）会走更低的判定门槛。

### 诚实说明：哪些检测不了

服务端看不到客户端渲染，所以这些**做不到**：

- ❌ 透视（ESP）—— 只能通过「命中隐身目标」间接推断
- ❌ 无后坐力 —— 服务端收不到后坐力数据
- ❌ 自瞄锁定 —— 只能从爆头率/反应时间间接推断

游戏内 `.dsac reliability` 会打印完整的「检测能力可靠性对照表」，
明确标出哪些能直接抓、哪些只能间接推断、哪些根本做不到。

### 处置

- **递进封禁阶梯**，每一步可配置：`kill` / 天数 / `perm`
- 默认前两次只**处死**不封禁 —— 给误判留余地
- 封禁次数持久化在 `bans.json`，重启不丢
- `.dsac bans reset <UserID>` 可撤销

### 运维

- **白名单** —— 玩家 UserID / 角色，完全不参与检测（`.dsac whitelist`）
- **服务器特性白名单** —— 你的服开了无限体力/子弹/过场飞行/主动发物品，
  在 `features:` 里打开对应开关，否则会误报
- **实时 HUD 面板** —— 右上角显示当前最可疑的玩家（`.dsac panel`）
- **行为回放** —— 输出完整时间线（`.dsac replay <玩家>`）
- **证据落盘** —— 判定成立时自动存完整时间线到 `evidence/`
- **嘲讽 + 全服公告** —— 可自定义文案

---

## 快速开始

### 1. 前置条件

| 需要 | 版本 |
|---|---|
| SCP: Secret Laboratory 服务端 | 14.2.7 |
| EXILED | 9.14.2（LabAPI）|
| .NET Framework | 4.8 |

### 2. 安装

把 `DeepSeekAntiCheat.dll` 放进 `%APPDATA%\EXILED\Plugins\`，重启服务端。

详细的傻瓜式教程见 [`docs/安装教程-傻瓜版.txt`](docs/安装教程-傻瓜版.txt)。

### 3. 配置

配置文件自动生成在：

```
%APPDATA%\EXILED\Configs\Plugins\deepseek_anticheat\<端口>.yml
```

**必做两件事**：

```yaml
# ① 填 API Key（不填也能用，只跑本地检测）
api_key: 'sk-你的Key'          # https://platform.deepseek.com 申请

# ② 按自己服务器的情况改 features 段 —— 不改对会误报！
features:
  infinite_stamina: false      # 你的服有无限体力就改 true
  infinite_ammo: false         # 有无限子弹就改 true
  cutscene_flight: false       # 过场/通话要上下飞就改 true
  custom_teleport: false       # 有自定义传送就改 true
  abnormal_heal: false         # 有异常治疗技能就改 true
  gives_items: false           # 服务器主动发物品就改 true
```

完整配置说明见 [`docs/配置说明.md`](docs/配置说明.md)。

### 4. 验证

进游戏按 `~` 打开控制台，**命令前必须加一个点**：

```
.dsac list        列出全部命令
.dsac features    看服务器特性白名单当前状态
.dsac report      看检测器战果
```

> **不加点会报 `Command dsac does not exist!`**
> 那是游戏本身的分发规则（不加点会被当成客户端本地命令），不是插件坏了。

---

## 命令

### 反作弊插件 `.dsac`（别名 `deepseekanticheat`）

| 命令 | 作用 |
|---|---|
| `.dsac report` | 检测器战果汇总 |
| `.dsac stats` | 当前跟踪的玩家统计与评分 |
| `.dsac panel` | 开关右上角实时 HUD 面板 |
| `.dsac reliability` | 检测能力可靠性对照表 |
| `.dsac features` | 服务器特性白名单当前状态 |
| `.dsac whitelist` | 查看/管理白名单（玩家不参与检测）|
| `.dsac bans` | 累犯封禁台账 |
| `.dsac bans reset <UserID>` | 撤销某人的封禁计数 |
| `.dsac follow <玩家>` | 开始跟踪 |
| `.dsac replay <玩家>` | 输出行为时间线 |
| `.dsac evidence` | 列出已落盘的证据 |
| `.dsac selftest` | 逐项自检 |
| `.dsac list` / `.dsac sim <场景>` / `.dsac ai <场景>` | 18 个内置模拟场景 |

### 白名单

```
.dsac whitelist                       查看
.dsac whitelist add <UserID或昵称>     加入（立即生效）
.dsac whitelist addrole <角色名>       加入角色白名单
.dsac whitelist remove <UserID>        移出
.dsac whitelist clear                 清空
```

白名单里的玩家**完全不参与检测** —— 不是「检测到不处置」，而是压根不进检测流程。
可以填在线玩家昵称，会自动解析成 UserID。

### 测试插件 `.cl`（别名 `cheatlab` / `awalab`）

> **只在测试服装，正式服不要装。** 它是独立插件，不包含在本仓库的主插件里。

它能在服务端制造出「作弊才会产生的状态」，用来验证反作弊到底抓不抓得住。

```
.cl on / .cl off / .cl status     开关
.cl perms                         看权限设置
.cl about                         看它是什么、为什么不是外挂
.cl items / noclip / teleport / fly / god / door / heal / spin / jitter
.cl aimbot <靶子> / esp <靶子> / shoot <靶子> / rapidfire <靶子>
```

**三道安全设计**：

1. **默认拒绝运行** —— 必须手动把 `i_understand_this_is_a_test_tool` 改成 `true`
2. **权限管理** —— 只有服务器所有者、管理员、白名单玩家能用；普通玩家一律拒绝
3. **服务端插件** —— 玩家装不了；装到客户端完全无效；发给别人也必须在对方服务器上手动解锁

详细说明见 [`docs/测试插件说明.md`](docs/测试插件说明.md)。

---

## 关于 AI 接入

默认直连 DeepSeek 官方 API（Key 存在插件配置里）。

**想更安全的话**，可以把 Key 收在一处、只给插件发可吊销的令牌：

```
插件（配置里只有令牌） → ai-proxy（持有真 Key） → DeepSeek
```

`ai-proxy` 是一个独立的、引擎无关的开源项目：

> **https://github.com/AWA/ai-proxy**

它说的是 OpenAI 兼容格式，所以任何引擎、任何语言的插件都能接
（起源引擎 / Unity / 虚幻 / 寒霜 / GMod / FiveM / Minecraft / Godot ...）。

配置里改两行就能切过去：

```yaml
api_base_url: 'http://127.0.0.1:8787/v1'
api_key: 'my-token-1'          # 令牌，不是真 Key
```

见 [`docs/接入AI指南.md`](docs/接入AI指南.md)。

---

## 从源码构建

```bash
git clone https://github.com/AWA/AWA-DeepSeek-AntiCheat.git
cd AWA-DeepSeek-AntiCheat

# 自用版（默认处置 Kill，含测试钻取命令）
dotnet build src/DeepSeekAntiCheat -c Release -p:AwaEdition=Private

# 公共版（默认处置 Ban，走递进阶梯，不含任何注入能力）
dotnet build src/DeepSeekAntiCheat -c Release -p:AwaEdition=Public
```

**两个版本的区别**（用编译开关分叉，逻辑代码完全相同）：

| | 自用版 `Private` | 公共版 `Public` |
|---|---|---|
| 署名 | `AWA 自用版` | `作者：AWA / 本插件全由 DSH 开发` |
| 默认处置 | `Kill`（只处死不封禁）| `Ban`（递进阶梯）|
| `.dsac drill` 测试钻取 | 有 | **没有**（不含任何注入能力）|

引用 EXILED 的方式：把 `Exiled.API.dll` 等依赖放进 `lib/`，
或直接从你的 LabAPI 依赖目录引用。

---

## 测试

仓库带三个**离线**测试项目，不需要开服务器：

```bash
# 反作弊评分逻辑（含边界与回归）
dotnet run --project tests/LogicTests

# 18 个模拟场景（作弊 / 正常玩家 / 边界）
dotnet run --project tests/CheatSimulator

# 测试插件的权限判定矩阵
dotnet run --project tests/PermTests
```

`CheatSimulator` 还可以把场景真的送到 AI 判定：

```bash
dotnet run --project tests/CheatSimulator -- --ai --key sk-xxx
```

这个模式专门用来验证**「AI 会不会把高玩误判成作弊」**这类问题。

---

## 已知限制

- **检测不了透视、无后坐力、自瞄锁定** —— 服务端拿不到客户端渲染数据
- **AI 复核有误判概率** —— 内置的 `legit_veteran`（合法高玩）场景能打到 75 分，
  而默认阈值是 80。**所以默认前两次只处死不封禁。**
  想更保守就把 `action` 改成 `Alert`（只通报，什么都不做）
- **需要服务端能联网**（要用 AI 复核的话）
- **只支持 EXILED 9.14.2 / 游戏 14.2.7** —— 版本不匹配时插件不会加载

---

## 作者与 AI 署名

| | |
|---|---|
| **作者** | **AWA** |
| **AI 协作者** | **DSH**（DeepSeek Harness）—— 基于 **DeepSeek** 模型 |
| **说明** | 本插件的**全部代码由 DSH 编写**，AWA 提出需求、做决策并测试 |

详见 [`AUTHORS.md`](AUTHORS.md)。

**分发或转载时必须保留 AWA 署名与水印。**

---

## 许可

[GPL-3.0](LICENSE)

因为本插件链接了 [EXILED](https://github.com/ExMod-Team/EXILED)（GPL-3.0），
按通常理解属于衍生作品，所以采用相同的许可。

---

## 免责声明

本插件是**服务端防御工具**，用于检测和处置作弊行为。

- 它**不是外挂**，不含任何客户端注入能力
- 它**不能保证**抓住所有作弊者（见「已知限制」）
- **AI 判定可能出错**，请务必先用 `Alert` 模式跑一段时间观察，再开启处置
- 使用本插件造成的任何后果（包括误封）由使用者自行承担

<div align="center">

**作者：AWA**　　本插件全由 **DSH** 开发

</div>
