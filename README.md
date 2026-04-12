# MajTataru (塔塔露麻将)

----
#### MajTataru (塔塔露麻将) 是一个可以在多玛方城战中记录牌局，并且可以实时提示，让塔塔露也能学会打麻将的ACT插件

----
## 使用说明

* 在ACT插件管理中加载MajTataru.dll
* 在OverlayPlugin中新建一个自定义数据统计悬浮窗，将悬浮窗地址设置为`Overlay/MajTataru.html`
* 将悬浮窗调整至适合你的位置，点击插件设置界面中`测试悬浮窗`按钮,可以观察效果
* 勾选启用解析之后, 进行多玛方城战对局（暂不支持人机，因为不在副本里）会在悬浮窗和TTS输出AI推荐策略
* 可以选择内置AI或外置MJAI模型，内置AI算法和系统推荐差不多,仅供参考；外置MJAI模型下面会单独说明。

----

## 注意事项

* 本项目仍处于测试阶段，尚不稳定，可以在插件设置界面启用DEBUG日志，如果遇到bug，可以将Debug日志提供给我。
* 该插件和ACT_Plugin/Overlay Plugin等插件类似，在游戏版本更新后需要更新opcode才可正常使用。
* 该插件仅供学习使用，请勿用于扰乱游戏环境的行为，也暂时不会提供明显强于系统提示的模型

----
## 如何使用MJAI模型

### MJAI和Mortal
关于`mjai`可以自行参考相关文档，如：
* [mjai.app](https://mjai.app/docs/mjai-protocol)
* [Gimite's original Mjai protocol](https://gimite.net/pukiwiki/index.php?Mjai%20%E9%BA%BB%E9%9B%80AI%E5%AF%BE%E6%88%A6%E3%82%B5%E3%83%BC%E3%83%90)

[Mortal](https://github.com/Equim-chan/Mortal)是一个基于mjai协议的开源麻将AI，原项目没有提供已经训练好的模型，可以自行训练或在网上寻找其他人训练好的模型
> Mortal作者没有提供详细的文档, [这里](https://blog.homura.cc/articles/2026/04/12/post_07.html)有我学习Mortal时的一些笔记，仅供参考。

> 如果你使用的是由Mortal训练出来的模型，那么在`MortalServer`目录下实现了一个外部AI Server的样例,直接将训练好的mortal.pth放入`MortalServer`目录下，然后在该目录下执行`python server.py`即可

>如果是其他的mjai模型，那么可以参考下面的接口说明实现AI Server。

<details>

<summary>MajTataru 外部 AI Server 接口说明</summary>

MajTataru 插件支持通过 HTTP 与外部 AI 模型通信。本文档定义了插件与服务端之间的交互接口，供自行实现服务端时参考。

---

## 基本信息

| 项目   | 说明                      |
| ---- | ----------------------- |
| 传输协议 | HTTP                    |
| 数据格式 | JSON（UTF-8）             |
| 默认地址 | `http://127.0.0.1:7331` |
| 插件超时 | 决策请求 5 秒，重置请求 3 秒       |

---

## 接口一览

| 方法   | 路径       | 用途              |
| ---- | -------- | --------------- |
| POST | `/`      | 发送事件列表，获取 AI 决策 |
| POST | `/reset` | 重置服务端状态         |

---

## POST `/reset` — 重置状态

插件在以下时机调用此接口：

- 新游戏开始时
- 切换到外部 AI 模式时
- 插件启动加载设置时
- 点击"测试MJAI"按钮前

### 请求

```
POST /reset HTTP/1.1
Content-Type: application/json; charset=utf-8

{}
```

### 响应

```json
{"status": "ok"}
```

### 服务端行为

清除所有游戏状态，包括：

- 已创建的 Bot 实例
- 已处理的事件计数
- 缓存的上次响应

---

## POST `/` — 请求 AI 决策

插件在需要 AI 决策时调用此接口。每次请求发送从游戏开始至今的**完整事件列表**。

### 请求

```
POST / HTTP/1.1
Content-Type: application/json; charset=utf-8

[事件1, 事件2, ..., 事件N]
```

Body 是一个 JSON 数组，包含 MJAI 格式的事件对象。事件列表从 `start_game` 开始，按时间顺序排列，每次请求都包含完整历史。

### 响应

返回一个 JSON 对象，表示 AI 的决策动作。

```json
{"type": "dahai", "actor": 3, "pai": "3s", "tsumogiri": false}
```

如果 AI 不需要动作（如轮到其他玩家），返回：

```json
{"type": "none"}
```

---

## 增量处理机制

虽然插件每次发送完整事件列表，但服务端应实现增量处理以避免重复计算：

1. 记录上次已处理的事件数量 `last_len`
2. 收到请求时，只取 `events[last_len:]` 作为新事件喂给模型
3. 更新 `last_len = len(events)`
4. 若 `len(events) < last_len`，说明开始了新游戏，重置状态后从头处理

```
请求1: [start_game, start_kyoku, tsumo]          → 处理 3 个事件, last_len=3
请求2: [start_game, start_kyoku, tsumo, dahai]    → 处理 1 个新事件(dahai), last_len=4
请求3: [start_game, start_kyoku, tsumo, dahai, tsumo] → 处理 1 个新事件(tsumo), last_len=5
```

---

## 决策时机

插件在两种场景下请求决策：

### 1. 自家摸牌后

事件列表末尾为自家的 `tsumo` 事件。

期望返回以下之一：

- `dahai` — 打牌
- `reach` — 立直（服务端需在后续接收到 reach 的 dahai）
- `ankan` — 暗杠
- `kakan` — 加杠
- `hora` — 自摸和了
- `ryukyoku` — 九种九牌流局
- `none` — 无动作

### 2. 对手打牌后

事件列表末尾为其他玩家的 `dahai` 事件。

期望返回以下之一：

- `chi` — 吃
- `pon` — 碰
- `daiminkan` — 大明杠
- `hora` — 荣和
- `none` — 跳过（不鸣牌）

---

## 响应字段说明

| 字段          | 类型       | 说明                                    |
| ----------- | -------- | ------------------------------------- |
| `type`      | string   | 动作类型（必须）                              |
| `actor`     | int      | 执行动作的玩家 ID                            |
| `pai`       | string   | 相关牌（MJAI 格式，如 `"3s"`, `"5mr"`, `"E"`） |
| `tsumogiri` | bool     | 是否摸切（仅 `dahai`）                       |
| `consumed`  | string[] | 使用的手牌（用于 `chi`/`pon`/`ankan` 等）       |
| `target`    | int      | 目标玩家 ID（用于 `hora`）                    |

插件读取 `type` 和 `pai` 来显示决策建议；`consumed` 用于显示鸣牌组合。

---

## 玩家 ID

- 玩家 ID 为 0-3 的固定物理座位号（非风位）
- `start_game` 事件中的 `id` 字段标识自家座位
- 所有事件中的 `actor`/`target` 均使用物理座位号

---

## 插件显示行为

| AI 返回           | 悬浮窗显示     | TTS 播报     |
| --------------- | --------- | ---------- |
| `dahai` + `pai` | 切 {牌名}    | "切{牌名}"    |
| `reach` + `pai` | 立直 切 {牌名} | "切{牌名} 立直" |
| `hora`（自摸）      | 自摸和了      | "自摸"       |
| `hora`（荣和）      | 荣和        | "荣和"       |
| `pon`           | 碰         | "碰"        |
| `chi`           | 吃         | "吃"        |
| `daiminkan`     | 大明杠       | "大明杠"      |
| `ankan`         | 暗杠        | "暗杠"       |
| `kakan`         | 加杠        | "加杠"       |
| `none`          | 跳过     	| 跳过      |

---

## 错误处理

- 服务端返回非 200 状态码 → 插件在日志中输出错误
- 服务端超时（>5秒） → 插件显示连接失败
- JSON 解析失败 → 视为 `{"type": "none"}`

服务端应尽量避免返回 4xx/5xx，遇到无法处理的情况返回 `{"type": "none"}` 即可。

---

## 完整交互示例

```
插件 → 服务端: POST /reset
服务端 → 插件: {"status":"ok"}

插件 → 服务端: POST /
[
  {"type":"start_game","id":3},
  {"type":"start_kyoku","bakaze":"E","dora_marker":"2s","kyoku":1,
   "honba":0,"kyotaku":0,"oya":0,"scores":[25000,25000,25000,25000],
   "tehais":[["?","?","?","?","?","?","?","?","?","?","?","?","?"],
             ["?","?","?","?","?","?","?","?","?","?","?","?","?"],
             ["?","?","?","?","?","?","?","?","?","?","?","?","?"],
             ["1m","3m","5m","7m","2p","4p","6p","8p","1s","3s","5sr","7s","9s"]]},
  {"type":"tsumo","actor":0,"pai":"?"},
  {"type":"dahai","actor":0,"pai":"E","tsumogiri":false},
  {"type":"tsumo","actor":1,"pai":"?"},
  {"type":"dahai","actor":1,"pai":"9m","tsumogiri":true},
  {"type":"tsumo","actor":2,"pai":"?"},
  {"type":"dahai","actor":2,"pai":"1p","tsumogiri":true},
  {"type":"tsumo","actor":3,"pai":"6s"}
]
服务端 → 插件: {"type":"dahai","actor":3,"pai":"9s","tsumogiri":false}
```

</details>

----
## 如何编译

### 安装依赖：

1. 请从 <https://github.com/EQAditu/AdvancedCombatTracker/releases/> 下载最新的 Zip 文件。
1. 解压 `Advanced Combat Tracker.exe` 到 `MajTataru/ThirdParty/ACT/` 下
1. 请从 <https://github.com/ravahn/FFXIV_ACT_Plugin/> 下载最新的 SDK Zip 文件（确保文件名称中包含 SDK 字样）
1. 解压 `SDK文件夹` 和 `FFXIV_ACT_Plugin.dll` 到 `MajTataru/ThirdParty/FFXIV_ACT_Plugin/` 下
1. 下转 **构建步骤**

该文件夹应如下所示（请注意，将来文件结构可能会随着更新而更改）：

```plaintext
ThirdParty
|- ACT
|  |- Advanced Combat Tracker.exe
|- FFXIV_ACT_Plugin
|  |- SDK
|  |  |- FFXIV_ACT_Plugin.Common.dll
|  |  |- FFXIV_ACT_Plugin.Config.dll
|  |  |- FFXIV_ACT_Plugin.LogFile.dll
|  |  |- FFXIV_ACT_Plugin.Memory.dll
|  |  |- FFXIV_ACT_Plugin.Network.dll
|  |  |- FFXIV_ACT_Plugin.Overlay.dll
|  |  |- FFXIV_ACT_Plugin.Parse.dll
|  |  |- FFXIV_ACT_Plugin.Resource.dll
|  |- FFXIV_ACT_Plugin.dll
```

### 构建插件的步骤

1. 在 Visual Studio 中打开解决方案（已在 Visual Studio 2022 测试通过）。
1. 采用 “Release” 和 “any CPU” 的配置开始构建。
1. 该插件将构建到 **bin/Release/MajTataru.dll**。