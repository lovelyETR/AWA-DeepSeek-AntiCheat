<div align="center">

# AWA-DeepSeek-AntiCheat

**用 DeepSeek AI 做二次判定的 SCP: Secret Laboratory 服务端反作弊插件**

[![Version](https://img.shields.io/badge/version-Alpha%20v1.0-blue?style=flat-square)]()
[![Platform](https://img.shields.io/badge/platform-EXILED%209.14%20%2F%20LabAPI-orange?style=flat-square)]()
[![Game](https://img.shields.io/badge/game-SCP%3ASL%2014.2.7-red?style=flat-square)]()
[![.NET](https://img.shields.io/badge/.NET-net48-512BD4?style=flat-square)]()
[![License](https://img.shields.io/badge/license-GPL--3.0-green?style=flat-square)]()

作者：**AWA**　　本项目全部代码由 **DSH**（DeepSeek Harness）编写

</div>

---

## 目录

- [这个仓库里有两个插件](#这个仓库里有两个插件)
- [①反作弊插件](#一反作弊插件-deepseekanticheat)
- [②测试插件](#二测试插件-awacheatlab)
- [检测能力：哪些能抓，哪些只能间接推断](#检测能力哪些能抓哪些只能间接推断)
- [快速开始](#快速开始)
- [命令](#命令)
- [从源码构建](#从源码构建)
- [测试](#测试)
- [AI 接入](#ai-接入)
- [已知限制](#已知限制)
- [作者与 AI 署名](#作者与-ai-署名)

---

# 这个仓库里有两个插件

**它们是两个独立的 DLL，装哪个、装在哪，不一样。**

| | **① 反作弊插件** | **② 测试插件** |
|---|---|---|
| 文件名 | `DeepSeekAntiCheat.dll` | `AwaCheatLab.dll` |
| 源码目录 | [`src/DeepSeekAntiCheat/`](src/DeepSeekAntiCheat) | [`src/AwaCheatLab/`](src/AwaCheatLab) |
| 游戏内命令 | `.dsac` | `.cl` |
| 干什么的 | **抓作弊**，并在判定成立时处置 | **制造作弊状态**，用来验证①抓不抓得住 |
| 装在哪 | **正式服和测试服都装** | **只在测试服装** |
| 装到正式服的后果 | 正常，这就是它该在的地方 | ⚠️ 你的管理员能在自己服务器上作弊 |
| 默认能不能用 | 装了就工作 | **默认拒绝运行**，必须手动解锁 |
| 谁能操作 | 管理员用命令看结果 | 只有服务器所有者 / 管理员 / 白名单 |
| 依赖关系 | 不需要②也能正常工作 | 不需要①也能装上，但没意义 |

**一句话**：①是产品，②是给①做体检的工具。

> **只想要反作弊？** 只装 `DeepSeekAntiCheat.dll` 就行，测试插件完全不用碰。

---

## ①反作弊插件 `DeepSeekAntiCheat`

### 它做什么

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

### 检测维度（14 项）

| 类别 | 检测项 |
|---|---|
| **射击** | 爆头率异常 · 命中率异常 · 远距离爆头 · 单发伤害超上限 · 射速超上限 · 连续开枪不换弹 |
| **反应** | 反应时间低于人类极限（< 150 ms）|
| **移动** | 移速异常 · 垂直速度异常 · 瞬移 · NoClip（穿墙）· 原地高速旋转 · 随机抖动 |
| **其他** | 异常治疗量 · 刷物品 · 命中隐身（SCP-268）目标 |

### 处置与运维

- **递进封禁阶梯**，每一步可配置为 `kill` / 天数 / `perm`
- 默认前两次只**处死**不封禁 —— 给误判留余地
- 封禁次数持久化，重启不丢，`.dsac bans reset <UserID>` 可撤销
- **白名单**：玩家 UserID / 角色，完全不参与检测
- **服务器特性白名单**：你的服开了无限体力/子弹/过场飞行等，打开对应开关避免误报
- **实时 HUD 面板**、**行为回放**、**证据自动落盘**
- **嘲讽 + 全服公告**，文案可自定义

---

## ②测试插件 `AwaCheatLab`

### 它做什么

反作弊插件负责抓作弊，但你怎么确认它真的抓得住？
平时没法验证 —— 总不能真去找个外挂来试。

这个插件能在**服务端**制造出「作弊才会产生的状态」，
然后你看反作弊能不能抓到。相当于给自己出一套模拟题。

### 它为什么不是外挂

1. **它是服务端插件** —— 玩家装不了，只有服务器管理员能往服务端放 DLL
2. **装到客户端完全没用** —— 游戏客户端根本不加载 EXILED 插件
3. **发给别人也没用** —— 服务器插件必须由对方管理员主动安装、主动解锁才会运行
4. **就算装上了，也只有管理员能用**（见下）

### 三道安全设计

| | 机制 | 作用 |
|---|---|---|
| **第一道** | 默认拒绝运行 | 配置文件里 `i_understand_this_is_a_test_tool` 默认 `false`，不改它并重启，插件什么都不做 |
| **第二道** | 权限管理 | 只有**服务器所有者 / 管理员 / 白名单玩家**能用，普通玩家一律拒绝 |
| **第三道** | 纯服务端 | 玩家碰不到；拷到别人服务器上也必须对方手动解锁 |

### 能造出什么状态

```
状态类（一个人就能测）：
    .cl items        刷一大堆物品（含 E11 步枪）
    .cl noclip       开 NoClip（穿墙）
    .cl teleport     瞬移 60 米
    .cl fly          垂直飞 40 米
    .cl god          无敌
    .cl door         免卡开门
    .cl heal         异常治疗
    .cl spin 60      原地高速旋转
    .cl jitter 60    随机乱转
    .cl all          以上全部跑一遍

射击类（需要两个玩家，一个开枪一个当靶子）：
    .cl aimbot <靶子>      自瞄（默认瞄头）
    .cl esp <靶子>         透视痕迹（给靶子套隐身再打他）
    .cl shoot <靶子>       普通开火
    .cl rapidfire <靶子>   真实连发
```

> **为什么射击类必须两个人？**
> 游戏对「打自己」有自伤保护，打自己不掉血也不算命中，反作弊一个数据都收不到。

---

# 检测能力：哪些能抓，哪些只能间接推断

**服务端看不到客户端渲染。** 这是原理限制，不是本插件的缺陷 ——
任何声称能**直接**检测这三项的插件，都是在误报。

但「不能直接检测」**不等于「完全没办法」**。本插件的处理方式：

### 透视 / ESP

| | |
|---|---|
| **能直接检测吗** | ❌ 不能。服务端不知道你屏幕上画了什么 |
| **本插件怎么做** | ✅ **间接推断** —— 正常玩家**看不见** SCP-268 隐身目标，如果他还能稳定命中，说明他有额外信息 |
| **对应检测项** | 「命中隐身目标」—— `.dsac reliability` 里标 ★★ 的那项 |
| **还会一起看** | 命中率、反应时间是否同步异常（透视往往伴随提前开枪）|

### 无后坐力

| | |
|---|---|
| **能直接检测吗** | ❌ 不能。后坐力是**纯客户端表现**，服务端收不到弹道 / 散布数据 |
| **本插件怎么做** | ⚠️ **目前没有专门检测**。只能从「射速 / 单发伤害 / 命中率」这些侧面数据看有没有一起异常 |
| **这是短板** | 是，如实说明。如果你的服务器有插件能上报弹道数据，可以接进来做 |
| **对应检测项** | 无专用项 |

### 自瞄锁定

| | |
|---|---|
| **能直接检测吗** | ❌ 不能。服务端看不到准星怎么移动的 |
| **本插件怎么做** | ✅ **间接推断**，用四条独立信号交叉验证 |
| **信号 1** | **爆头率**异常（阈值 75%）|
| **信号 2** | **远距离爆头**占比异常（45 米外，阈值 40%）|
| **信号 3** | **反应时间**低于人类极限（< 150 ms）|
| **信号 4** | **转身速度**异常（瞬时转速 600 度/秒以上，连续 8 次算一次违规）—— 自瞄锁定的典型特征就是「瞬间把准星甩到头上」，这条专门抓它 |

### 完整的可靠性对照表

游戏内输入 **`.dsac reliability`** 会打印完整表格，把每项分成四档：

```
★★★ = 服务端直接可见，误报极低
★★  = 服务端可推断，需要配合 AI 复核
★   = 只是弱信号，不单独作为依据
无法 = 服务端原理上看不到，任何声称能检测的都是误报
```

| 检测项 | 可靠性 | 依据 |
|---|---|---|
| NoClip | ★★★ | 服务端状态位直接可读，权限为假却启用即为铁证 |
| 速度异常 | ★★★ | 位置是服务端权威数据，瞬时速度直接算 |
| 瞬移 | ★★★ | 同上；已排除 SCP-106/096/173 的机制位移 |
| 飞天/悬浮 | ★★ | 垂直速度；受地形与卡顿影响，偶有误报 |
| 爆头率/命中率 | ★★★ | 开枪与命中事件直接统计 |
| 射速/伤害 | ★★★ | 服务端收到的数值直接校验 |
| 隐身目标命中 | ★★ | **透视的间接信号**；但流弹可能误中 |
| 无限弹药 | ★★ | 按「未换弹连打数」推断，命中判定有误差 |
| 异常回血 | ★★ | 依赖治疗事件；部分角色技能本就是大治疗 |
| 击杀速率 | ★★ | 依赖回合时长，样本短时会归一化保护 |
| 可疑物品 | ★ | 仅统计获取次数，具体规则需按服务器玩法调 |
| **透视 / ESP** | **无法直接检测** | **只能靠「命中隐身目标」间接推断** |
| **无后坐力** | **无法检测** | **纯客户端表现，服务端无数据（本插件暂无间接手段）** |
| **自瞄锁定** | **无法直接检测** | **靠爆头率 / 远距离爆头 / 反应时间 / 转身速度四条信号间接推断** |

---

# 快速开始

## 1. 前置条件

| 需要 | 版本 |
|---|---|
| SCP: Secret Laboratory 服务端 | 14.2.7 |
| EXILED | 9.14.2（LabAPI）|
| .NET Framework | 4.8 |

## 2. 安装

**只装反作弊插件**（推荐，大多数人只需要这个）：

```
把 DeepSeekAntiCheat.dll 放进  %APPDATA%\EXILED\Plugins\
重启服务端
```

**想验证检测能力的话**，再额外装测试插件：

```
把 AwaCheatLab.dll 也放进  %APPDATA%\EXILED\Plugins\
然后手动解锁（见 ②测试插件 那节的「三道安全设计」）
```

详细的傻瓜式教程见 [`docs/安装教程-傻瓜版.txt`](docs/安装教程-傻瓜版.txt)。

## 3. 配置

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

## 4. 验证

进游戏按 `~` 打开控制台，**命令前必须加一个点**：

```
.dsac list          列出全部命令
.dsac features      看服务器特性白名单当前状态
.dsac report        看检测器战果
.dsac reliability   看检测能力可靠性对照表
```

> **不加点会报 `Command dsac does not exist!`**
> 那是游戏本身的分发规则（不加点会被当成客户端本地命令），不是插件坏了。

---

# 命令

## 反作弊插件 `.dsac`（别名 `deepseekanticheat`）

| 命令 | 作用 |
|---|---|
| `.dsac report` | 检测器战果汇总 |
| `.dsac stats` | 当前跟踪的玩家统计与评分 |
| `.dsac panel` | 开关右上角实时 HUD 面板 |
| `.dsac reliability` | **检测能力可靠性对照表**（含哪些做不到）|
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

## 测试插件 `.cl`（别名 `cheatlab` / `awalab`）

> **只在测试服装。** 装好后默认拒绝运行，且只有管理员能用。

```
.cl about      它是什么、为什么不是外挂     ← 唯一对所有人开放的命令
.cl perms      看权限设置和你自己的身份
.cl on / .cl off / .cl status

.cl items / noclip / teleport / fly / god / door / heal / spin / jitter / all
.cl aimbot <靶子> / esp <靶子> / shoot <靶子> / rapidfire <靶子>
```

详细说明见 [`docs/测试插件说明.txt`](docs/测试插件说明.txt)。

---

# 从源码构建

```bash
git clone https://github.com/lovelyETR/AWA-DeepSeek-AntiCheat.git
cd AWA-DeepSeek-AntiCheat

# 反作弊插件
dotnet build src/DeepSeekAntiCheat -c Release -p:AwaEdition=Public   # 公共版（发给别人）
dotnet build src/DeepSeekAntiCheat -c Release -p:AwaEdition=Private  # 自用版（自己用）

# 测试插件
dotnet build src/AwaCheatLab -c Release
```

**反作弊插件两个版本的区别**（用编译开关分叉，逻辑代码完全相同）：

| | 自用版 `Private` | 公共版 `Public` |
|---|---|---|
| 署名 | `AWA 自用版` | `作者：AWA / 本插件全由 DSH 开发` |
| 默认处置 | `Kill`（只处死不封禁）| `Ban`（递进阶梯）|
| `.dsac drill` 测试钻取 | 有 | **没有**（不含任何注入能力）|

**测试插件**没有版本分叉，只有署名不同。

> **引用 EXILED**：把 `Exiled.API.dll` 等依赖放进 `lib/`，
> 或直接从你的 LabAPI 依赖目录引用。仓库里不含这些二进制。

---

# 测试

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

# AI 接入

默认直连 DeepSeek 官方 API（Key 存在插件配置里）。

**想更安全的话**，可以把 Key 收在一处、只给插件发可吊销的令牌：

```
插件（配置里只有令牌） → ai-proxy（持有真 Key） → DeepSeek
```

`ai-proxy` 是一个独立的、引擎无关的开源项目：

> **https://github.com/lovelyETR/ai-proxy**

它说的是 OpenAI 兼容格式，所以任何引擎、任何语言的插件都能接
（起源引擎 / Unity / 虚幻 / 寒霜 / GMod / FiveM / Minecraft / Godot ...）。

配置里改两行就能切过去：

```yaml
api_base_url: 'http://127.0.0.1:8787/v1'
api_key: 'my-token-1'          # 令牌，不是真 Key
```

见 [`docs/接入AI指南.md`](docs/接入AI指南.md)。

---

# 已知限制

- **透视、无后坐力、自瞄锁定无法直接检测** —— 服务端拿不到客户端渲染数据。
  其中**透视**和**自瞄**有间接手段（见[上面那节](#检测能力哪些能抓哪些只能间接推断)），
  **无后坐力**目前确实没有
- **AI 复核有误判概率** —— 内置的 `legit_veteran`（合法高玩）场景能打到 75 分，
  而默认阈值是 80。**所以默认前两次只处死不封禁。**
  想更保守就把 `action` 改成 `Alert`（只通报，什么都不做）
- **需要服务端能联网**（要用 AI 复核的话）
- **只支持 EXILED 9.14.2 / 游戏 14.2.7** —— 版本不匹配时插件不会加载

---

# 作者与 AI 署名

| | |
|---|---|
| **作者** | **AWA** —— 项目发起人、需求提出者、决策者、测试者 |
| **AI** | **DSH**（DeepSeek Harness），基于 **DeepSeek** 模型 —— **全部代码的编写者** |

本项目的**全部源代码由 DSH 编写**，AWA 提出需求、做决策并实测验证。

详见 [`AUTHORS.md`](AUTHORS.md)。

---

# 许可

[GPL-3.0](LICENSE)

因为本插件链接了 [EXILED](https://github.com/ExMod-Team/EXILED)（GPL-3.0），
按通常理解属于衍生作品，所以采用相同的许可。

---

# 免责声明

本插件是**服务端防御工具**，用于检测和处置作弊行为。

- 它**不是外挂**，不含任何客户端注入能力
- 它**不能保证**抓住所有作弊者（见「已知限制」）
- **AI 判定可能出错**，请务必先用 `Alert` 模式跑一段时间观察，再开启处置
- 使用本插件造成的任何后果（包括误封）由使用者自行承担

<div align="center">

**作者：AWA**　　本项目全部代码由 **DSH** 开发

</div>
