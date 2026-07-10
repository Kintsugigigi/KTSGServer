# 连接生命周期责任链

## 1. 文档目的

这份文档记录当前服务端已经实现的连接生命周期责任链，用来回答三个问题：

1. TCP/UDP 的连接建立和断开事件，最终由谁接住。
2. 各层容器在新增和删除时，当前是如何同步的。
3. 之后继续扩展顶号、重连、战斗掉线时，应该往哪一层加逻辑。

## 2. 当前总体分层

当前链路分成三层：

### 2.1 传输层

职责：

- 维护底层 socket / peer / connection
- 发出连接建立、连接断开、收包事件
- 不直接处理玩家、房间、战斗业务

对应类：

- `TCPNetManager`
- `TcpServerService`
- `UdpServerService`

### 2.2 生命周期层

职责：

- 接住 TCP/UDP 的连接事件
- 把连接和 `PlayerSession`、`RoomSession`、`BattleSession` 串起来
- 负责统一清理顺序

对应类：

- `ConnectionLifecycleService`

### 2.3 业务层

职责：

- 保存玩家、房间、战斗这些业务对象
- 提供查找、加入、离开、创建、移除接口
- 不直接处理底层连接事件入口

对应类：

- `PlayerService`
- `RoomService`
- `BattleService`

## 3. 当前已实现的责任链

## 3.1 TCP 建连链

调用链：

1. `TCPNetManager` 接收到新 TCP 连接
2. `TcpServerService` 创建 `TcpConnectionAdapter`
3. `ServerRuntime` 通过 `TcpServer.OnClientConnected`
4. 转发到 `ConnectionLifecycleService.OnTcpConnected`

说明：

- 当前 `OnTcpConnected(...)` 还是轻量入口，暂时不做复杂业务。
- 真正建立玩家会话，是在登录成功之后完成。

## 3.2 登录成功链

调用链：

1. 客户端发 `ReqLogin`
2. `ReqLoginHandler` 调用 `LoginService.LoginOrCreateSession(...)`
3. `LoginService` 完成账号校验/建号
4. `LoginService` 调用 `ConnectionLifecycleService.CreateOrReplaceTcpSession(...)`
5. 生命周期层统一处理旧连接替换和新 session 建立
6. `PlayerService` 创建并登记新的 `PlayerSession`
7. `LoginService` 再调用 `PlayerService.MarkAuthenticated(...)`

关键点：

- 顶号替换不再直接散落在 `LoginService` 或 `PlayerService`。
- 登录阶段现在已经收口到生命周期服务。

## 3.3 TCP 断开链

调用链：

1. TCP 底层断开
2. `TCPNetManager.OnPeerDisconnected(...)`
3. `TcpServerService.OnClientDisconnected`
4. `ServerRuntime` 转发到 `ConnectionLifecycleService.OnTcpDisconnected(...)`
5. 生命周期层找到对应 `PlayerSession`
6. 按顺序执行清理：
   - 离开房间
   - 从战斗中移除
   - 删除玩家索引
   - 按需要断开 UDP

当前清理顺序在 `ConnectionLifecycleService.CleanupSession(...)` 中统一实现。

## 3.4 UDP 鉴权接入链

调用链：

1. 客户端通过 TCP 拿到 `battle token`
2. 客户端使用 token 发起 UDP 连接
3. `UdpServerService` 在 `OnConnectionRequest(...)` 中校验 token
4. LiteNetLib 建立 peer 后，`UdpServerService.OnAuthedPeerConnected`
5. `ServerRuntime` 转发到 `ConnectionLifecycleService.OnUdpAuthenticated(...)`
6. 生命周期层调用 `BattleService.TryBindUdp(...)`
7. 生命周期层绑定 `PlayerSession.UdpConnection`
8. 生命周期层登记 `udpConnectionId -> PlayerSession`
9. `BattleSession` 标记该玩家 `UdpConnected`

说明：

- 当前 UDP 不会自己决定连到谁。
- 绑定关系完全由 TCP 提前签发的 token 决定。

## 3.5 UDP 断开链

调用链：

1. UDP peer 断开
2. `UdpServerService.OnPeerDisconnected`
3. `ServerRuntime` 转发到 `ConnectionLifecycleService.OnUdpDisconnected(...)`
4. 生命周期层通过 `udpConnectionId` 反查 `PlayerSession`
5. 生命周期层调用 `BattleService.HandleUdpDisconnected(player)`
6. `BattleSession` 标记该玩家 `UdpDisconnected`
7. `PlayerSession` 解绑 `UdpConnection`
8. 生命周期层删除 `udpConnectionId -> PlayerSession`

说明：

- 当前 UDP 断开不再等于“离开战斗”。
- 现在的策略是：
  - 保留 `CurrentBattleId`
  - 保留 battle 成员身份
  - 只标记为战斗内 UDP 掉线
- 这为后续做短暂重连窗口提供了基础。

## 4. 当前各层容器如何同步

## 4.1 TCP 连接容器

当前涉及：

- `TCPNetManager._connectedPeers`
- `TcpServerService._connections`

同步关系：

- 底层新增 peer 后，`TcpServerService` 会创建对应 `INetConnection`
- 底层断开 peer 后，`TcpServerService` 会删除对应连接

这层主要是传输层容器，当前同步关系是成立的。

## 4.2 玩家会话容器

当前涉及：

- `PlayerService._sessionsByAccount`
- `PlayerService._sessionsByPlayerId`

同步关系：

- 登录成功后登记
- TCP 断开后统一删除
- 不再承担 TCP/UDP 连接反查索引

相较之前，关键改进是：

- `PlayerService` 已收敛为玩家业务主索引层
- 不再混合维护连接 ID 索引
- 生命周期层和业务层边界更清楚

## 4.3 房间容器

当前涉及：

- `RoomService._rooms`
- `RoomSession._playerIds`

同步关系：

- 玩家进房时，`RoomService.JoinRoom(...)` 同时改 `RoomSession` 和 `PlayerSession`
- 玩家离房或 TCP 断开时，生命周期层会调用 `RoomService.LeaveRoom(...)`
- 空房间现在会自动从 `RoomService._rooms` 中删除

## 4.4 战斗容器

当前涉及：

- `BattleService._battles`
- `BattleSession._players`
- `BattleSession._snapshots`

同步关系：

- 创建战斗时，登记 `BattleSession`
- UDP 鉴权成功后，绑定 `PlayerSession.UdpConnection`
- UDP 鉴权成功后，将 battle 内玩家标记为 `UdpConnected`
- UDP 断开时，将 battle 内玩家标记为 `UdpDisconnected`
- TCP 断开时，生命周期层会调用 `BattleService.RemovePlayer(...)`
- 空战斗现在会自动从 `BattleService._battles` 中删除

注意：

- 当前 `PlayerSession.CurrentBattleId` 仍然主要在 UDP 绑定时设置。
- TCP 断开时已经会从 `BattleSession._playerIds` 中移除该玩家。
- `BattleSession` 现在除了持有玩家 ID，还持有每个玩家的 UDP 连接状态、最近连接时间、最近断开时间、断线次数。

## 4.5 生命周期层连接索引

当前涉及：

- `ConnectionLifecycleService._sessionsByTcpConnectionId`
- `ConnectionLifecycleService._sessionsByUdpConnectionId`

同步关系：

- 新 TCP session 建立时登记 `tcpConnectionId -> PlayerSession`
- TCP 断开时通过该索引反查玩家 session
- UDP 鉴权成功时登记 `udpConnectionId -> PlayerSession`
- UDP 断开时通过该索引反查玩家 session
- 会话整体清理时，这两张表由生命周期层统一删除

这次调整之后，连接反查索引已经从 `PlayerService` 迁出，职责变为：

- `PlayerService` 负责“这个账号/玩家现在是哪一个 session”
- `ConnectionLifecycleService` 负责“这个连接现在属于哪一个 session”

## 5. 当前线程模型

当前业务容器修改是主线程约束模型。

已经实现的约束：

- `ServerRuntime.Start()` 绑定主线程
- `ServerRuntime.Update()` 校验主线程
- `PlayerService`、`RoomService`、`BattleService` 的写接口会做主线程断言

这意味着：

- socket 线程只负责投递事件
- 业务容器修改必须回到 `ServerRuntime.Update()` 驱动的主线程执行

当前这套约束适合现在的 demo 阶段，也和现有 `HashSet`、`PlayerSession` 的实现匹配。

## 6. 当前还没有完成的部分

虽然责任链已经形成，但还没有完全做完。

当前还缺：

1. 顶号策略细化
   - 现在已经会清旧 session
   - 但还没区分“温和挤下线”还是“立即踢线并通知客户端”

2. UDP 掉线策略细化
   - 现在已经能标记战斗内掉线
   - 但还没决定允许重连多久、超时后如何判定

3. 战斗状态对象细化
   - 现在 `BattleSession` 还是简单的 `playerId HashSet`
   - 后面需要扩展成真正的 battle player runtime

4. 战斗状态进一步细化
   - 现在 `BattlePlayerState` 只记录 UDP 在线状态和时间戳
   - 后面还需要接入重连窗口、超时策略、托管/判负策略

## 7. 当前推荐的后续方向

下一阶段建议按这个顺序做：

1. 把“顶号”做成明确业务策略
2. 把 UDP 断开从“解绑连接”升级成“战斗掉线策略”
3. 继续评估是否收敛 `PlayerService` 的连接索引

当前不要急着再新增更多容器。

这一轮的目标应该是：

- 让已有容器的责任链完整
- 而不是继续扩展新的索引表和新的状态缓存

## 8. 当前结论

现在服务端已经不再是“底层断了，上层各删各的”。

已经形成的完整思路是：

- 传输层负责发出连接事件
- 生命周期层负责统一编排连接和业务状态
- 业务层负责保存玩家、房间、战斗这些实际对象

这条责任链是后面做：

- 顶号
- 重连
- 房间离线清理
- 战斗掉线判定
- PVP 重连恢复

这些功能的基础。
