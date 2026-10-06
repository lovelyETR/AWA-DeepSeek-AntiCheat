<div align="center">

# AWA-DeepSeek-AntiCheat

SCP: Secret Laboratory 服务端反作弊插件（EXILED / LabAPI）

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
- [安装](#安装)
- [配置](#配置)
- [命令](#命令)
- [从源码构建](#从源码构建)
- [测试](#测试)
- [AI 接口](#ai-接口)
- [作者与许可](#作者与许可)

---

# 这个仓库里有两个插件

**两个独立的 DLL，装哪个不一样。**

### ① 反作弊插件 `DeepSeekAntiCheat.dll`

命令前缀 `.dsac`

作用是**抓作弊**：累积玩家的行为统计，用本地启发式打分，
超过阈值的样本交给 DeepSeek AI 复核，判定成立时按递进阶梯处置。

装在 `%APPDATA%\EXILED\Plugins\`。

### ② 测试插件 `AwaCheatLab.dll`

命令前缀 `.cl`

作用是**制造作弊状态**，用来验证①能不能抓到。

也装在 `%APPDATA%\EXILED\Plugins\`，但**只在测试服装**，
而且装好后默认拒绝运行，必须手动解锁。

**只想要反作弊的话，只装①就行，②完全不用碰。**

---

# 安装

## 前置条件

| 需要 | 版本 |
|---|---|
| SCP: Secret Laboratory 服务端 | 14.2.7 |
| EXILED | 9.14.2（LabAPI）|
| .NET Framework | 4.8 |

## 反作弊插件

```
把 DeepSeekAntiCheat.dll 放进  %APPDATA%\EXILED\Plugins\
重启服务端
```

## 测试插件（可选）

```
1. 把 AwaCheatLab.dll 也放进  %APPDATA%\EXILED\Plugins\
2. 重启服务端，让它生成配置
3. 关掉服务端，改配置：把 i_understand_this_is_a_test_tool 改成 true
4. 再重启服务端
```

测试插件的配置文件位置：

```
%APPDATA%\EXILED\Configs\Plugins\awa_cheatlab\<端口>.yml
```

不改成 `true` 并重启，这个插件**不会执行任何注入**。

傻瓜式安装教程：[`docs/安装教程-傻瓜版.txt`](docs/安装教程-傻瓜版.txt)

---

# 配置

## 配置文件位置

```
%APPDATA%\EXILED\Configs\Plugins\deepseek_anticheat\<端口>.yml
```

`<端口>` 是服务端端口（默认 `7777`）。**文件名就是端口号** ——
端口不对的话 EXILED 会重新生成一份默认的，你改的那个就不生效。

**改完必须重启服务端**，配置只在启动时读一次。

> 例外：白名单可以用 `.dsac whitelist` 命令改，那样立即生效并写回文件。

## 要填的两项

### ① API Key

```yaml
api_key: 'sk-你的Key'
```

到 https://platform.deepseek.com 申请。

**不填也能用** —— 只跑本地启发式检测，不联网、零成本。

### ② `features` 段

```yaml
features:
  infinite_stamina: false      # 你的服有无限体力 → 改 true
  infinite_ammo: false         # 有无限子弹 → 改 true
  cutscene_flight: false       # 过场/通话要上下飞 → 改 true
  custom_teleport: false       # 有自定义传送 → 改 true
  abnormal_heal: false         # 有异常治疗技能 → 改 true
  gives_items: false           # 服务器主动发物品 → 改 true

  noclip_for_admins: true      # 管理员用 NoClip 不算作弊
  god_mode_for_admins: true

  speed_tolerance_multiplier: 1        # 放宽移速上限的倍率
  vertical_tolerance_multiplier: 1
  item_spam_tolerance_multiplier: 1

  whitelisted_user_ids: []     # 完全不检测的玩家
  whitelisted_roles: []        # 完全不检测的角色
```

**你的服开了上面哪一项，就把对应项改成 `true`。** 没开就保持 `false`。

完整配置说明：[`docs/配置说明.md`](docs/配置说明.md)

---

# 命令

> **游戏内按 `~` 打开控制台，命令前必须加一个点。**
> 不加点会报 `Command dsac does not exist!` ——
> 那是游戏本身的分发规则（不加点会被当成客户端本地命令）。

## 反作弊插件 `.dsac`（别名 `deepseekanticheat`）

| 命令 | 作用 |
|---|---|
| `.dsac report` | 检测器战果汇总 |
| `.dsac stats` | 当前跟踪的玩家统计与评分 |
| `.dsac panel` | 开关右上角实时 HUD 面板 |
| `.dsac reliability` | 检测能力可靠性对照表 |
| `.dsac features` | 服务器特性白名单当前状态 |
| `.dsac whitelist` | 查看/管理白名单 |
| `.dsac bans` | 累犯封禁台账 |
| `.dsac bans reset <UserID>` | 撤销某人的封禁计数 |
| `.dsac follow <玩家>` | 开始跟踪 |
| `.dsac replay <玩家>` | 输出行为时间线 |
| `.dsac evidence` | 列出已落盘的证据 |
| `.dsac selftest` | 逐项自检 |
| `.dsac list` | 列出 18 个内置模拟场景 |
| `.dsac sim <场景>` | 跑本地评分（秒回，不联网）|
| `.dsac ai <场景>` | 走完整 AI 链路 |

### 白名单

```
.dsac whitelist                       查看
.dsac whitelist add <UserID或昵称>     加入（立即生效）
.dsac whitelist addrole <角色名>       加入角色白名单
.dsac whitelist remove <UserID>        移出
.dsac whitelist removerole <角色名>    移出
.dsac whitelist clear                 清空
```

白名单里的玩家**完全不参与检测**。可以填在线玩家昵称，会自动解析成 UserID。

## 测试插件 `.cl`（别名 `cheatlab` / `awalab`）

完整说明：[`docs/测试插件说明.txt`](docs/测试插件说明.txt)

```
.cl about      看这个插件是什么（唯一对所有人开放的命令）
.cl perms      看权限设置和你自己的身份
.cl on         开启
.cl off        关闭
.cl status     看当前状态
.cl list       列出全部命令
```

---

# 从源码构建

```bash
git clone https://github.com/lovelyETR/AWA-DeepSeek-AntiCheat.git
cd AWA-DeepSeek-AntiCheat

# 反作弊插件
dotnet build src/DeepSeekAntiCheat -c Release -p:AwaEdition=Public   # 公共版
dotnet build src/DeepSeekAntiCheat -c Release -p:AwaEdition=Private  # 自用版

# 测试插件
dotnet build src/AwaCheatLab -c Release
```

**反作弊插件的两个版本**（用编译开关分叉，逻辑代码相同）：

| | `Private` | `Public` |
|---|---|---|
| 署名 | `AWA 自用版` | `作者：AWA / 本插件全由 DSH 开发` |
| 默认处置 | `Kill`（只处死）| `Ban`（递进阶梯）|
| `.dsac drill` 命令 | 有 | 无（不含注入能力）|

**引用 EXILED**：把 `Exiled.API.dll` 等依赖放进 `lib/`，
或直接从你的 LabAPI 依赖目录引用。仓库里不含这些二进制。

---

# 测试

三个离线测试项目，不需要开服务器：

```bash
dotnet run --project tests/LogicTests        # 评分逻辑与边界
dotnet run --project tests/CheatSimulator    # 18 个模拟场景
dotnet run --project tests/PermTests         # 测试插件的权限判定
```

`CheatSimulator` 也可以把场景送到真实 AI 判定：

```bash
dotnet run --project tests/CheatSimulator -- --ai --key sk-xxx
```

---

# AI 接口

默认直连 DeepSeek 官方 API（Key 存在插件配置里）。

也可以把 Key 收在一处，只给插件发可吊销的令牌 ——
用配套的独立项目 [ai-proxy](https://github.com/lovelyETR/ai-proxy)：

```yaml
api_base_url: 'http://127.0.0.1:8787/v1'
api_key: 'my-token-1'          # 令牌，不是真 Key
```

见 [`docs/接入AI指南.md`](docs/接入AI指南.md)。

---

# 作者与许可

| | |
|---|---|
| **作者** | **AWA** |
| **AI** | **DSH**（DeepSeek Harness），基于 **DeepSeek** 模型 —— 全部代码的编写者 |

详见 [`AUTHORS.md`](AUTHORS.md)。

许可：[GPL-3.0](LICENSE)（因为链接了同样是 GPL-3.0 的 EXILED）

<div align="center">

**作者：AWA**　　本项目全部代码由 **DSH** 开发

</div>
