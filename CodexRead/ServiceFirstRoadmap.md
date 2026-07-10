# Service-First 服务端路线图

## 1. 当前实现状态

截至目前，服务端可以分成 4 个层次来看：

### 1.1 传输层

状态：已具备基础能力

- 自定义 TCP 已可常驻监听
- LiteNetLib UDP 已完成基础接入
- UDP 现在已经有统一的 `channel` 和 `DeliveryMode` 发送策略层

当前结论：

- 网络底座不是第一优先问题
- 它已经足够支撑下一阶段做业务服务层

### 1.2 业务服务层

状态：刚开始建立骨架

本轮已新增：

- `PlayerService`
- `LoginService`
- `RoomService`
- `BattleTokenService`
- `BattleService`
- `ServerServices`

以及运行时对象：

- `PlayerSession`
- `RoomSession`
- `BattleSession`
- `BattleSnapshotFrame`

当前结论：

- 服务层已经开始有形状
- 但还没有真正和启动入口、handler、TCP 网关串起来

### 1.3 TCP -> UDP Token 接入

状态：基础钩子已具备，尚未完成宿主接线

本轮已补：

- `UdpSessionModule` 支持自定义 `TokenValidator`
- `UdpSessionModule` 支持认证成功回调
- `UdpSessionModule` 支持断开回调
- `BattleTokenService` 支持发 token / 校验 / 消耗
- `BattleService` 支持 `TryBindUdp(...)`

当前结论：

- 架构上已经可以实现“TCP 下发 token，UDP 拿 token 建连”
- 但还没有实际的 `Program/Runtime` 把这条链串起来

### 1.4 快照层

状态：只有骨架，没有真正内容

本轮只补了：

- `BattleSnapshotFrame`
- `BattleSession.AddSnapshot(...)`
- `BattleService.AppendSnapshot(...)`

当前结论：

- 现在还不能叫“快照层已完成”
- 只能叫“已经有容器，但没有快照内容模型、采样策略、重放策略”

## 2. 你现在这条建议是对的

你建议先做：

- `PlayerService`
- `RoomService`
- `LoginService`

而不是直接先实现 proto，这个方向是对的。

原因很直接：

- proto 只是通信格式
- 业务服务层才决定“服务端到底在管理什么对象”
- 如果服务层模型没立住，proto 很容易来回返工

对你当前项目，更合理的顺序应该是：

1. 先建立服务层和运行时对象
2. 再定义这些服务层真正需要的消息
3. 最后补 handler 和落库

## 3. 这些 Service 要不要做单例

结论：

- 可以“单实例”
- 但不建议做“静态全局单例”

更推荐的是：

- 由 `ServerRuntime` 创建一次
- 存在于 `ServerServices` 容器中
- 整个服务端进程生命周期里只保留一个实例

也就是说，推荐：

- 进程级单实例

不推荐：

- `public static LoginService Instance`
- `public static RoomService Instance`

原因：

1. 后面更容易测试
2. 更容易做多实例服、分区服、战斗子进程
3. 更容易做启动顺序控制
4. 不会把状态散成全局变量

所以当前最合适的组织方式就是：

- `ServerServices`
  - `Players`
  - `Login`
  - `Rooms`
  - `Battles`
  - `BattleTokens`

这已经比纯静态单例更稳。

## 4. 从客户端 Entity 看，服务端至少要准备哪些模型

我看过：

- `GameEntity`
- `GameEntityBoard`
- `CtrlPlayer`
- `CtrlPlayerBoard`
- `PlayerStateManager`
- `CtrlPlayerStates`

可以很明确地看出，服务端以后至少要对应 3 套数据：

### 4.1 玩家静态资料

例如：

- 基础属性
- 角色模板
- 装备
- 技能配置
- 成长数据

这个更接近 `PlayerProfile` / `PlayerLoadout`

### 4.2 玩家战斗运行态

例如：

- 位置
- 朝向
- 速度
- 是否落地
- 当前房间
- 当前战局
- 当前生命值
- 当前 Buff

这个更接近 `PlayerCombatState`

### 4.3 玩家动作/状态机态

从客户端 `CtrlPlayerStates` 可以看出，你至少存在：

- Ground
- Air
- Jump
- Fall
- Hurt

后续还有可能恢复：

- Dash
- Spin
- Stomp
- BackFlip

这意味着服务端不能只存“位置和血量”，还需要一层：

- `PlayerActionState`
- 或 `PlayerLocomotionState`

否则战斗事件和状态同步很快会散掉。

## 5. 所以服务层应该怎么分

我建议你后面固定成下面几类服务：

### `LoginService`

负责：

- 登录校验
- 会话建立
- 账号到 `PlayerSession` 的绑定

### `PlayerService`

负责：

- 在线玩家会话
- 玩家基础资料读取
- TCP/UDP 连接绑定
- 玩家运行态缓存

### `RoomService`

负责：

- 房间创建
- 入房/离房
- 房间成员管理
- 战斗前准备状态

### `BattleService`

负责：

- 战局创建
- 战斗 tick
- UDP token 签发和绑定
- 快照缓存
- 战局结束清理

### 以后再加的服务

- `ChatService`
- `InventoryService`
- `ShopService`
- `MatchService`
- `SnapshotService`

## 6. UDP 现在能不能按 TCP Token 建连和断连

结论分两层：

### 架构能力上

可以了，已经具备基础能力。

因为现在已经有：

- `BattleTokenService.Issue(...)`
- `BattleTokenService.Validate(...)`
- `BattleTokenService.TryConsume(...)`
- `BattleService.TryBindUdp(...)`
- `UdpSessionModule.TokenValidator`
- `UdpSessionModule.OnAuthedPeerConnected`
- `UdpSessionModule.OnPeerDisconnectedAction`

### 整体运行上

还不算真正完成。

因为还缺：

1. TCP 登录/匹配成功后发 battle token
2. `ServerRuntime` 把 `BattleService.ValidateUdpToken` 挂到 `UdpSessionModule.TokenValidator`
3. `ServerRuntime` 把 `BattleService.TryBindUdp(...)` 挂到 `OnAuthedPeerConnected`
4. 断开时把 `BattleService.UnbindUdp(...)` 挂到 `OnPeerDisconnectedAction`

所以现在准确的说法是：

- “架构链路已打通”
- “运行时接线还没补完”

## 7. 快照层现在准备得怎么样

目前只能说是“刚起步”。

已经有的：

- 战局对象里有 snapshot queue
- 战斗服务里有 `AppendSnapshot(...)`

还没有的关键部分：

1. 快照内容定义
   - 玩家位置
   - 旋转
   - 速度
   - HP
   - 状态机状态
   - Buff
   - 技能事件

2. 快照采样时机
   - 每 tick 一次
   - 还是每 N tick 一次

3. 快照用途区分
   - 广播快照
   - 重连恢复快照
   - 落盘快照

4. 快照压缩与裁剪

5. 快照回放与补同步

所以接下来更合理的做法不是直接深挖快照，而是先：

- 先让 `BattleSession` 真正跑起来
- 先让 `PlayerCombatState` 稳定
- 再定义快照内容

## 8. 新的实施顺序

相比之前的计划，我建议现在切换为下面这条路线。

### 阶段 1：先把服务层和运行时模型立住

1. 建立 `ServerRuntime`
   - 统一持有 `ServerServices`
   - 统一持有 TCP/UDP 网关

2. 完成 `LoginService`
   - TCP 登录后创建 `PlayerSession`

3. 完成 `PlayerService`
   - 能按 playerId / tcpConn / udpConn 找到在线玩家

4. 完成 `RoomService`
   - 支持建房、入房、离房

5. 完成 `BattleService`
   - 支持创建战局
   - 支持发 token
   - 支持 UDP 绑定/解绑

### 阶段 2：把 TCP 常驻链路跑通

1. `ReqLogin -> RspLogin`
2. `CreateRoom / JoinRoom / LeaveRoom`
3. `StartBattlePrepare`
4. TCP 下发：
   - `battleId`
   - `udpPort`
   - `battleToken`

### 阶段 3：把 UDP Token 接入跑通

1. UDP 校验 token
2. UDP 建连后绑定到 `PlayerSession`
3. UDP 断线后解绑
4. TCP 保持在线返回大厅

### 阶段 4：先跑最小战斗闭环

1. 客户端上行 `MoveInput`
2. 服务端战斗态更新
3. 服务端下行 `PlayerTransformState`
4. 再补 `HpState`
5. 再补 `SkillCastConfirmed`

### 阶段 5：最后再补快照层

1. 定义 `PlayerCombatSnapshot`
2. 定义 `BattleSnapshot`
3. 战斗中按 tick 缓存
4. 重连时发送恢复快照
5. 之后再考虑落库和回放

## 9. 下一步最值得继续实现的内容

如果按这个新路线继续，我建议下一步优先做：

1. `ServerRuntime`
2. `PlayerSessionManager` 或直接用 `PlayerService`
3. `BattleService` 与 `UdpSessionModule` 的真正接线
4. 一个最小 `ReqLoginHandler`，只接 `LoginService`

此时 handler 不再承担业务，只负责把消息转给 service。

这才是更健康的架构走向。

