# 当前连接容器问题与收敛路线

## 1. 当前问题概览

目前服务端和客户端都已经有“连接容器”和“业务会话容器”，但这些容器之间的生命周期还没有完全统一，主要问题不是单个容器线程不安全，而是不同层的新增、删除、替换没有形成一条完整闭环。

当前可观察到的主要问题：

1. TCP 断开后，只会清理 `TcpServerService` 和 `PlayerService` 的部分索引，不会自动同步 `RoomSession` 和 `BattleSession`。
2. 同账号重新登录时，`PlayerService` 会替换 session 索引，但不会主动关闭旧 TCP/UDP，也不会把旧玩家状态从房间和战斗中清掉。
3. `RoomSession` 和 `BattleSession` 内部仍然使用裸 `HashSet`，当前依赖“主线程 Update 驱动”才暂时可用，没有显式线程边界保护。
4. `PlayerSession` 自身同时持有 `TcpConnection`、`UdpConnection`、`CurrentRoomId`、`CurrentBattleId`，而 `PlayerService` 还额外维护按 TCP/UDP/账号/玩家 ID 的多个索引，维护成本较高。
5. UDP 断开时当前只会解绑 `PlayerSession.UdpConnection`，不会决定该玩家是否应该离开战斗，语义仍然偏弱。
6. 客户端 `TcpClientManager` / `UdpClientManager` 只适合主线程串行使用，没有“连接代次”保护，异步连接和主动断开交错时可能恢复旧状态。

## 2. 当前容器清单

### 2.1 传输层容器

- `TCPNetManager._connectedPeers`
- `TcpServerService._connections`
- `UdpServerService` 里的 LiteNetLib peer 集合
- `LiteNetManager` 内部 peer 链表/请求表

这些容器的职责是维护底层连接对象，基本不应直接承载业务状态。

### 2.2 业务层容器

- `PlayerService._sessionsByAccount`
- `PlayerService._sessionsByPlayerId`
- `PlayerService._sessionsByTcpConnectionId`
- `PlayerService._sessionsByUdpConnectionId`
- `RoomService._rooms`
- `RoomSession._playerIds`
- `BattleService._battles`
- `BattleSession._playerIds`
- `BattleSession._snapshots`

这些容器才是真正影响登录、房间、战斗生命周期的容器。

## 3. 现存漏洞与不足

### 3.1 生命周期不闭环

当前还没有一条从“连接创建”到“业务绑定”再到“连接断开”再到“业务清理”的统一调用链。

实际状态更像：

- TCP 建连
  - 底层加 peer
  - `TcpServerService` 加连接
  - 业务层等待登录
- 登录后
  - `PlayerService` 新建/替换 session
- TCP 断开
  - 底层删 peer
  - `TcpServerService` 删连接
  - `PlayerService` 删索引
  - 房间/战斗不一定删

这说明“连接存在”和“业务存在”之间还没有统一生命周期管理器。

### 3.2 同账号顶号处理不完整

当前替换 session 只会移除 `PlayerService` 自己的索引，不会：

- 断开旧 TCP
- 断开旧 UDP
- 从房间移除旧玩家
- 从战斗移除旧玩家
- 广播旧玩家离线/重登

这会导致旧连接、旧房间成员和旧战斗成员残留。

### 3.3 容器职责重叠

目前 `PlayerSession` 已经持有：

- `Account`
- `PlayerId`
- `TcpConnection`
- `UdpConnection`
- `CurrentRoomId`
- `CurrentBattleId`

同时 `PlayerService` 又维护四张索引表。索引本身不是错，但如果没有统一入口维护，就很容易漏改。

### 3.4 Room/Battle 成员容器线程边界不清晰

`RoomSession._playerIds` 和 `BattleSession._playerIds` 现在没有加锁，也不是并发容器。

这在“所有业务操作都在主线程 Update 中执行”的前提下可以接受，但当前代码没有明确这个约束。后续如果：

- 匹配改到后台线程
- DB 回调直接操作 session
- 战斗 Tick 改成独立线程

这些容器会立刻出现竞争问题。

### 3.5 UDP 断开语义不完整

现在 UDP 断开只等于“解绑 UDP 连接”，不等于：

- 离开战斗
- 暂时掉线等待重连
- 直接判负/托管

也就是说，容器有删除动作，但业务语义没有定下来。

## 4. 是否需要完整的连接/断连调用链

需要，而且这是下一阶段最重要的架构工作之一。

最终要实现的不是“底层断了，上层自己猜”，而是一条明确的调用链：

1. 底层连接创建
2. 连接适配器建立
3. 传输层容器登记
4. 业务层决定是否绑定到某个玩家
5. 进入世界层/房间层/战斗层
6. 底层断开
7. 生命周期管理器接收断开事件
8. 统一清理 TCP/UDP/房间/战斗/索引
9. 必要时广播业务事件

也就是说，目标应该是：

- 底层网络只负责“连接事件”
- 中间层负责“生命周期编排”
- 上层业务只负责“断线后的业务策略”

## 5. 是否存在冗余容器

存在，而且有些可以收敛。

### 5.1 可以保留的容器

这些容器保留是合理的：

- `TCPNetManager._connectedPeers`
  - 底层 TCP peer 表
- `TcpServerService._connections`
  - 服务端 TCP 连接适配器表
- `RoomService._rooms`
  - 房间总表
- `BattleService._battles`
  - 战斗总表
- `BattleSession._snapshots`
  - 战斗快照缓存

### 5.2 可收敛的容器

最值得收的是 `PlayerService` 这层索引维护方式。

调整前四张表里：

- `byPlayerId`
- `byAccount`

是主业务索引，建议保留。

- `byTcpConnectionId`
- `byUdpConnectionId`

是反查索引，是否保留取决于断线入口是否一定从连接对象出发。

如果未来统一做成：

- `ConnectionRegistry`
  - `connectionId -> session`

那么 `PlayerService` 自己就不必维护两张连接 ID 索引。

当前已经完成的收敛：

- `PlayerService` 只保留：
  - `account -> PlayerSession`
  - `playerId -> PlayerSession`
- `ConnectionLifecycleService` 现在维护：
  - `tcpConnectionId -> PlayerSession`
  - `udpConnectionId -> PlayerSession`

这意味着：

- 玩家业务索引和连接生命周期索引已经完成第一轮拆分
- `PlayerService` 的职责比之前更纯粹

### 5.3 不建议继续增加的新容器

当前不要再额外引入：

- 单独的 `SessionByRoomId`
- 单独的 `SessionByBattleId`
- 单独的 `TcpPlayers` / `UdpPlayers`

这些都会放大维护成本。

## 6. 建议的目标架构

建议把容器职责收成三层。

### 6.1 传输层

- `TCPNetManager`
- `TcpServerService`
- `UdpServerService`

职责：

- 维护底层连接
- 发出连接建立/断开事件
- 不直接处理玩家状态

### 6.2 生命周期层

新增一个统一的会话生命周期协调者，例如：

- `ConnectionLifecycleService`
- 或 `SessionLifecycleService`

职责：

- TCP 建连时登记临时连接
- 登录成功时绑定到 `PlayerSession`
- TCP 断开时统一触发玩家离线清理
- UDP 建连时绑定到 battle
- UDP 断开时根据策略做战斗内处理
- 处理顶号、踢人、重连

这一层应该成为容器同步新增/删除的唯一入口。

### 6.3 业务层

- `PlayerService`
- `RoomService`
- `BattleService`

职责：

- 保存业务对象
- 提供加入/离开/查找/创建接口
- 不直接处理底层连接生命周期

## 7. 建议的最终容器关系

建议尽量收敛成下面这组：

- `TcpServerService`
  - `connectionId -> INetConnection`
- `UdpServerService`
  - peer 内部自带连接，不额外做业务索引
- `PlayerService`
  - `account -> PlayerSession`
  - `playerId -> PlayerSession`
- `RoomService`
  - `roomId -> RoomSession`
- `BattleService`
  - `battleId -> BattleSession`
- `ConnectionLifecycleService`
  - `connectionId -> PlayerSession`
  - 或统一管理 TCP/UDP 到 session 的绑定关系

这样比现在把“连接反查索引”直接堆在 `PlayerService` 里更清楚。

## 8. 分步实现需求

### 阶段 1：明确线程边界

目标：

- 明确 `RoomSession`、`BattleSession`、`PlayerSession` 只允许在主线程访问和修改。

需求：

1. 在相关类和服务上加注释，声明线程使用约束。
2. 约束所有 `RoomService` / `BattleService` 修改入口只从 `ServerRuntime.Update()` 主线程触发。
3. 禁止后台线程直接修改 `RoomSession` 和 `BattleSession`。

完成标志：

- 当前所有业务容器修改路径可以口径一致地说明“在哪个线程上发生”。

### 阶段 2：补统一下线清理入口

目标：

- 不再由 `TcpServerService` 直接只删 `PlayerService` 索引。

需求：

1. 增加统一入口，例如 `SessionLifecycleService.OnTcpDisconnected(conn)`。
2. 在该入口中顺序处理：
   - 找到 `PlayerSession`
   - 如在房间中，先离房
   - 如绑定 UDP，先解绑或断开 UDP
   - 如在战斗中，按策略处理 battle 成员
   - 最后删除玩家索引
3. `ServerRuntime.WireTcpLifecycle()` 改为调用这个统一入口。

完成标志：

- TCP 断开时，房间、战斗、玩家索引三层都能同步清理。

### 阶段 3：补顶号/替换连接闭环

目标：

- 同账号重新登录时，旧连接和旧业务状态被统一收口。

需求：

1. 新登录时检查旧 `PlayerSession`。
2. 如存在旧连接：
   - 主动断开旧 TCP
   - 主动断开旧 UDP
   - 清理旧房间/战斗关系
3. 再为新连接建立新 session 或重绑 session。

完成标志：

- 同账号重复登录不会留下旧连接或旧业务成员残留。

### 阶段 4：收敛 PlayerService 索引

目标：

- 减少多表同步维护的复杂度。

需求：

1. 评估将 `byTcpConnectionId` / `byUdpConnectionId` 移到单独生命周期服务。
2. `PlayerService` 只保留业务主索引：
   - `account -> PlayerSession`
   - `playerId -> PlayerSession`
3. 连接反查改由生命周期服务负责。

完成标志：

- `PlayerService` 只负责玩家业务会话，不再承担全部连接反查职责。

### 阶段 5：明确 UDP 断开策略

目标：

- 让 UDP 断开不再只是“删一个引用”。

需求：

1. 先标记战斗掉线，允许后续接入短暂重连。
2. `BattleService` 改成更具体的战斗掉线接口。
3. `BattleSession` 增加 battle player 状态，而不只是一个 `playerId HashSet`。
4. 后续再补掉线超时、托管或判负策略。

完成标志：

- UDP 断开后，战斗层至少能区分“仍在 battle 中但 UDP 已掉线”。

### 阶段 6：再决定是否加强并发容器

目标：

- 在确认是否多线程战斗之前，不盲目复杂化容器。

需求：

1. 如果仍保持主线程业务模型，保留 `HashSet` 即可。
2. 如果战斗 Tick 要独立线程，再把：
   - `RoomSession`
   - `BattleSession`
   - `PlayerSession`
   的修改保护起来。

完成标志：

- 容器线程模型和运行时模型一致，不出现“看起来线程安全，实际并没有”的伪安全设计。

## 9. 当前最推荐的下一步

如果按收益排序，最优先做的是：

1. 增加统一的 TCP 断线清理入口
2. 补顶号/重复登录清理逻辑
3. 收敛 `PlayerService` 的连接反查索引

当前先不要急着把所有容器都改成并发容器，也不要先上复杂锁。

真正的问题首先是“生命周期没统一”，不是“容器类型选错了”。
