# Battle UDP 消息分类建议

## 1. 目标

这份文档专门回答两个问题：

1. 你的 RPG/PVP 服务端，UDP 到底该怎么选 `DeliveryMethod`
2. 你的多逻辑 `channel` 该怎么分工

基于当前项目，我建议把战斗 UDP 先固定成 3 个逻辑通道：

- `channel 0 = State`
- `channel 1 = Event`
- `channel 2 = Bulk`

对应代码已经开始落地在：

- `KTSGServerCommon\BattleNet\BattleUdpChannels.cs`
- `KTSGServerCommon\BattleNet\BattleUdpSendPolicy.cs`
- `KTSGServerCommon\BattleNet\BattleUdpSendExtensions.cs`

## 2. 先说结论

### `channel 0 : State`

用途：

- 高频、连续、会被新状态覆盖旧状态的同步

优先 DeliveryMode：

- `Sequenced`

可选：

- `Unreliable`

典型消息：

- 客户端输入状态
- 客户端移动方向
- 客户端瞄准方向
- 服务端角色位置
- 服务端速度
- 服务端朝向
- 服务端动画朝向

### `channel 1 : Event`

用途：

- 离散战斗事件
- 关键确认
- 不能丢的战斗结果

优先 DeliveryMode：

- `ReliableOrdered`

可选：

- `ReliableUnordered`
- `ReliableSequenced`

典型消息：

- 技能释放开始
- 技能命中确认
- 受击事件
- Buff 添加/移除
- 死亡
- 复活
- 子弹/召唤物生成销毁
- 战斗内 UI 的关键状态确认

### `channel 2 : Bulk`

用途：

- 大包
- 重同步
- 全量快照
- 断线恢复

优先 DeliveryMode：

- `ReliableOrdered`

典型消息：

- 全量战局快照
- 断线重连恢复包
- 进入战斗后的初始状态同步
- 大范围 AOI 补同步

## 3. 为什么是这 3 个 channel

如果你只有 2 个 channel：

- `State`
- `EverythingElse`

那很容易出现的问题是：

- 一个全量快照卡住技能确认
- 一个大包阻塞关键战斗事件

而 3 个 channel 的好处是：

- `State` 独立跑高频流
- `Event` 独立跑关键可靠事件
- `Bulk` 独立承载大包和重同步

这就是战斗服里最值得先固定下来的最小结构。

## 4. DeliveryMethod 具体怎么选

## 4.1 `Sequenced`

语义：

- 只要最新
- 旧包来了也丢
- 中间丢一些没关系

最适合：

- 输入流
- 位置流
- 朝向流
- 速度流

对你项目的建议：

- 客户端上行输入用 `Sequenced`
- 服务端下行位置状态也优先 `Sequenced`

原因：

- 这类消息天然是“新状态覆盖旧状态”
- 一旦改成 `ReliableOrdered`，丢一个旧包就可能拖住整条流

## 4.2 `Unreliable`

语义：

- 可能丢
- 可能乱序
- 也可能重复

适合：

- 调试辅助
- 非关键遥测
- 完全可丢弃的轻量状态

对你项目的建议：

- 第一版战斗核心同步不要优先用它
- 除非你明确知道某类消息即使乱序也没影响

更稳妥的第一版选择通常是：

- 状态流先用 `Sequenced`
- 不要一开始就大量使用 `Unreliable`

## 4.3 `ReliableOrdered`

语义：

- 必到
- 按顺序处理

适合：

- 技能释放确认
- 伤害结算
- Buff 生命周期
- 角色死亡/复活
- 战局开始/结束
- 全量快照

对你项目的建议：

- 所有“离散关键战斗事件”先统一放这类
- 第一版最省心

代价：

- 同 channel 内存在队头阻塞
- 所以不要把大快照和关键小事件放在同一个逻辑 channel

## 4.4 `ReliableUnordered`

语义：

- 必到
- 但不保证顺序

适合：

- 各自独立、彼此顺序无依赖的可靠消息

对你项目的建议：

- 第一版只少量使用
- 适合那种“漏了不行，但前后顺序不重要”的通知类消息

例如：

- 战斗内独立提示
- 多个彼此独立的对象状态确认

如果你不确定，就先别用，继续放 `ReliableOrdered`。

## 4.5 `ReliableSequenced`

语义：

- 只保最新
- 但最后那个最新值必须送到

适合：

- 最新状态必须最终到达
- 但历史中间态不重要

对你项目的建议：

- 用于“最新值型”的重要 UI/状态同步

例如：

- 当前血量绝对值
- 当前护盾绝对值
- 读条进度
- 目标点占领进度
- Boss 当前阶段状态

注意：

- 这里应该发送“绝对值”
- 不要发送“增量差值”

因为旧值会被覆盖。

## 5. 推荐的 RPG/PVP 消息分类表

## 5.1 客户端 -> 服务端

### 输入与移动

- `MoveInput`
  - `channel 0`
  - `Sequenced`
- `AimInput`
  - `channel 0`
  - `Sequenced`
- `LookYawPitch`
  - `channel 0`
  - `Sequenced`

### 技能请求

- `CastSkillRequest`
  - 如果是“按键意图 + 当前朝向”的连续状态，走 `channel 0 / Sequenced`
  - 如果是“离散技能释放命令”，走 `channel 1 / ReliableOrdered`

建议：

- 第一版把技能释放请求视为离散命令
- 走 `ReliableOrdered`

### 心跳和遥测

- `Ping`
  - 优先复用 LiteNetLib 自带
- `DebugTelemetry`
  - `channel 0 / Unreliable`

## 5.2 服务端 -> 客户端

### 高频状态流

- `PlayerTransformState`
  - `channel 0 / Sequenced`
- `VelocityState`
  - `channel 0 / Sequenced`
- `FacingState`
  - `channel 0 / Sequenced`
- `AnimatorMoveState`
  - `channel 0 / Sequenced`

### 最新值状态

- `HpState`
  - `channel 1 / ReliableSequenced`
- `ShieldState`
  - `channel 1 / ReliableSequenced`
- `CastBarState`
  - `channel 1 / ReliableSequenced`
- `ObjectiveProgressState`
  - `channel 1 / ReliableSequenced`

### 离散关键事件

- `SkillCastConfirmed`
  - `channel 1 / ReliableOrdered`
- `HitConfirmed`
  - `channel 1 / ReliableOrdered`
- `BuffAdded`
  - `channel 1 / ReliableOrdered`
- `BuffRemoved`
  - `channel 1 / ReliableOrdered`
- `Dead`
  - `channel 1 / ReliableOrdered`
- `Revive`
  - `channel 1 / ReliableOrdered`
- `ProjectileSpawn`
  - `channel 1 / ReliableOrdered`
- `ProjectileDespawn`
  - `channel 1 / ReliableOrdered`

### 大包与重同步

- `FullBattleSnapshot`
  - `channel 2 / ReliableOrdered`
- `ReconnectSnapshot`
  - `channel 2 / ReliableOrdered`
- `SceneResyncSnapshot`
  - `channel 2 / ReliableOrdered`

## 6. 几条特别重要的规则

### 规则 1

位置、朝向、速度这类消息，优先发“当前值”，不要发“必须逐条执行的轨迹增量”。

否则你会被可靠顺序和补发成本拖垮。

### 规则 2

血量这类消息如果走 `ReliableSequenced`，请发绝对值，不要发 delta。

正确：

- 当前 HP = 713

不推荐：

- HP -32

### 规则 3

大快照不要跟关键战斗事件共用一个逻辑 channel。

否则：

- 一个重连快照可能把死亡、复活、技能命中全堵住

### 规则 4

第一版先少用 `ReliableUnordered`。

因为它最容易在“看起来可以无序”但实际上业务有隐含前后关系时埋坑。

### 规则 5

如果一个消息“丢一帧没关系，但过时消息不能晚到”，就优先选 `Sequenced`。

这条对战斗状态同步非常重要。

## 7. 当前代码里的对应实现

目前已经开始落的部分：

- `BattleUdpChannels.ChannelCount = 3`
- `UDPClientManager` 已改为使用统一 `ChannelCount`
- `NetworkService` 已改为使用统一 `ChannelCount`
- `UdpSessionModule` 已改为使用统一 `ChannelCount`
- `INetConnection` 现在增加了完整 DeliveryMode 发送重载
- `UdpConnectionAdapter` 已能映射：
  - `ReliableUnordered`
  - `Sequenced`
  - `ReliableOrdered`
  - `ReliableSequenced`
  - `Unreliable`
- 新增 `BattleUdpSendPolicies`
- 新增 `SendBattle(...)` 扩展

这意味着后续 handler 或战斗广播逻辑，已经可以直接按“消息类别 -> 发送策略”来写，而不必继续硬编码 `reliable=true/false`。

## 8. 更新后的分步需求

相比之前的版本，我建议把战斗服实施步骤优化成下面这样：

1. 先固定 UDP 发送策略层
   - 固定 `channel 0/1/2`
   - 固定消息类别和 DeliveryMode
   - 避免后面协议越来越多后再返工

2. 先做 TCP 常驻和玩家会话
   - 登录
   - 大厅
   - 匹配

3. 再做 UDP 战斗接入
   - battle token
   - 会话绑定

4. 优先打通上行输入流和下行状态流
   - `InputState -> channel 0 / Sequenced`
   - `TransformState -> channel 0 / Sequenced`

5. 再补离散战斗事件
   - `channel 1 / ReliableOrdered`

6. 最后做全量快照和断线重连
   - `channel 2 / ReliableOrdered`

这样更符合实际开发路径，因为：

- 你先把传输语义定死，后面的 proto 和 handler 才不会反复返工
- 战斗同步第一版最先有价值的是“人能动 + 状态能看见”
- 快照恢复应放在基础战斗流稳定之后

## 9. 最后的建议

如果你现在马上要开始写第一批战斗 UDP 协议，我建议优先做这 6 类：

- 客户端 `MoveInput`
- 客户端 `AimInput`
- 服务端 `PlayerTransformState`
- 服务端 `HpState`
- 服务端 `SkillCastConfirmed`
- 服务端 `FullBattleSnapshot`

它们刚好覆盖：

- `Sequenced`
- `ReliableSequenced`
- `ReliableOrdered`
- `Bulk channel`

一旦这 6 类消息跑通，你整套 UDP 架构就不是纸面设计，而是真正开始形成闭环了。

