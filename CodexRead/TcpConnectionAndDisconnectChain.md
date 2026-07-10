# TCP 连接与断连调用链

## 1. 文档目的

这份文档整理你当前项目中 TCP 侧的完整调用链，包含：

1. TCP 服务端如何启动。
2. 一个 TCP 连接是如何从底层 socket 变成 `PlayerSession` 的。
3. TCP 收包是如何一路进入 `NetMsgProcessor` 的。
4. TCP 断开有哪些入口。
5. TCP 断开后，是如何从底层一路回调到生命周期层和业务层的。

最终结论先写在前面：

- TCP 服务端“启动”是自上而下：
  - `ServerRuntime -> TcpServerService -> TCPNetManager -> Socket`
- TCP 的“连接事件 / 收包事件 / 断连事件”是自下而上：
  - `Socket/TCPPeer -> TCPNetManager -> TcpServerService -> ServerRuntime -> ConnectionLifecycleService -> Player/Room/Battle`

所以 TCP 和 UDP 一样，不是单向链，而是：

- 启动时：上层驱动底层
- 运行时：底层事件回调上层

## 2. 当前 TCP 相关层次

### 2.1 服务端宿主层

- `ServerRuntime`
- `TcpServerService`
- `ConnectionLifecycleService`

### 2.2 业务层

- `LoginService`
- `PlayerService`
- `RoomService`
- `BattleService`
- `PlayerSession`

### 2.3 底层 TCP 层

- `TCPNetManager`
- `TCPPeer`
- `TcpProtocol`
- `TCPNetEvent`
- `TCPNetPacketReader`

你这套 TCP 是自己实现的，不走 LiteNetLib。

## 3. TCP 启动调用链

## 3.1 启动入口

当前启动入口是：

1. `ServerRuntime.Start()`
2. 调 `TcpServer.Start(Options.TcpPort)`
3. `TcpServerService.Start(port)`
4. 创建 `new TCPNetManager(new TcpListenerImpl(this))`
5. `TCPNetManager.StartServer(port)`
6. 创建监听 socket
7. `Bind + Listen`
8. 启动 `AcceptLoop()`

所以 TCP 服务端启动方向是：

`ServerRuntime -> TcpServerService -> TCPNetManager -> Socket`

这是标准的上层驱动底层。

## 3.2 生命周期接线

在 `ServerRuntime` 初始化时，会接好：

- `TcpServer.OnClientConnected`
- `TcpServer.OnClientDisconnected`

因此后续所有 TCP 连接事件都会再从底层回调上来。

## 4. TCP 建连调用链

## 4.1 socket 接入入口

真正的建连起点在 `TCPNetManager.AcceptLoop()`：

1. 监听 socket 收到新连接
2. `AcceptAsync()` 返回 `clientSocket`
3. 调 `CreateAndAddPeer(clientSocket)`

## 4.2 peer 创建链

`CreateAndAddPeer(...)` 里做的事：

1. 分配新的 `peer.Id`
2. 创建 `new TCPPeer(id, socket, this)`
3. 加入 `_connectedPeers`
4. `CreateConnectEvent(peer)`

`TCPPeer` 在构造时会立即：

1. 记录 `RemoteEndPoint`
2. 创建接收缓冲区
3. 启动 `ReceiveLoop()`

所以 TCP 建连后，peer 接收循环会马上开始。

## 4.3 事件进入服务端宿主层

连接事件继续往上：

1. `TCPNetManager.CreateConnectEvent(peer)`
2. 事件进入 `_pendingEventHead/_pendingEventTail`
3. `ServerRuntime.Update()`
4. `TcpServer.Update()`
5. `TCPNetManager.PollEvents()`
6. `ProcessEvent(TCPNetEvent.EType.Connect)`
7. `ITCPEventListener.OnPeerConnected(peer)`
8. `TcpServerService.TcpListenerImpl.OnPeerConnected(...)`
9. 创建 `TcpConnectionAdapter`
10. 加入 `TcpServerService._connections`
11. 调 `TcpServerService.OnClientConnected(conn)`
12. `ServerRuntime` 收到回调
13. 转发给 `ConnectionLifecycleService.OnTcpConnected(conn)`

当前 `OnTcpConnected(...)` 还是轻量入口，它目前不直接建立玩家会话。

## 5. 登录绑定链

TCP 真正从“裸连接”变成“玩家会话”，是在登录成功之后。

调用链如下：

1. 客户端通过 TCP 发 `ReqLogin`
2. `TcpServerService.OnNetworkReceive(...)`
3. 调 `NetMsgProcessor.ProcessMessage(...)`
4. 进入 `ReqLoginHandler`
5. `ReqLoginHandler` 调 `LoginService.LoginOrCreateSession(...)`
6. `LoginService` 完成：
   - Mongo 查账号
   - 明文密码校验
   - 自动建号（如果不存在）
7. `LoginService` 调 `ConnectionLifecycleService.CreateOrReplaceTcpSession(...)`
8. 生命周期层：
   - 检查同账号旧 session
   - 检查该 TCP 是否已经绑定旧 session
   - 如有旧 session，先统一清理
   - 再创建新的 `PlayerSession`
   - 绑定 `tcpConnectionId -> PlayerSession`
9. `LoginService` 再调 `PlayerService.MarkAuthenticated(...)`
10. 最终得到：
   - `account -> PlayerSession`
   - `playerId -> PlayerSession`
   - `tcpConnectionId -> PlayerSession`

到这一步，TCP 才真正进入世界层在线态。

## 6. TCP 收包调用链

TCP 收包主链如下：

1. `TCPPeer.ReceiveLoop()` 在后台异步收 socket 数据
2. 数据写入 `_receiveBuffer`
3. `ProcessBuffer()`
4. 用 `TcpProtocol.ReadHeader(...)` 解析你的自定义包头
5. 拆出：
   - `cmdId`
   - `rpcSeq`
   - body
6. 创建 `TCPNetPacket`
7. `TCPNetManager.OnTcpPacketReceived(...)`
8. 生成 `TCPNetEvent.Receive`
9. `TCPNetManager.PollEvents()`
10. `ITCPEventListener.OnNetworkReceive(...)`
11. `TcpServerService.TcpListenerImpl.OnNetworkReceive(...)`
12. 通过 `peer.Id` 从 `_connections` 找到 `INetConnection`
13. 调 `NetMsgProcessor.ProcessMessage(conn, cmdId, rpcSeq, body)`

所以 TCP 收包方向也是：

`Socket/TCPPeer -> TCPNetManager -> TcpServerService -> NetMsgProcessor`

## 7. TCP 断连的主要入口

你这套 TCP 没有像 LiteNetLib 那样有很多协议层 reason，但仍然有多个实际入口。

## 7.1 入口 A：接收循环异常

入口在 `TCPPeer.ReceiveLoop()`。

链路：

1. `_socket.ReceiveAsync(...)` 抛异常
2. 或收到 `bytesReceived == 0`
3. 进入 `catch`
4. 调 `Disconnect(DisconnectReason.ConnectionFailed)`

这就是最典型的“远端断开”或“接收失败”入口。

## 7.2 入口 B：发送循环异常

入口在 `TCPPeer.TryStartSendLoop()`。

链路：

1. 发送队列中有包
2. `_socket.SendAsync(...)` 抛异常
3. 进入 `catch`
4. 调 `Disconnect(DisconnectReason.ConnectionFailed)`

这表示发送侧发现连接已经不可用。

## 7.3 入口 C：服务端主动断开单个连接

当前链路上没有大量单独的“踢线接口”，但你一旦调用：

- `PlayerSession.TcpConnection.Disconnect()`

最终会落到：

- `TcpConnectionAdapter.Disconnect()`
- `TCPPeer.Disconnect(...)`

这是 TCP 主动断开的入口。

## 7.4 入口 D：整体停服

入口在 `TCPNetManager.Stop()`。

链路：

1. `_isRunning = false`
2. 关闭监听 socket
3. 遍历 `_connectedPeers.Values`
4. 对每个 peer 调 `peer.Disconnect(DisconnectReason.DisconnectPeerCalled)`
5. `PollEvents()`
6. `ClearPendingEvents()`

和 LiteNetLib 的 `Stop()` 不同，你这套 TCP manager 会在 `Stop()` 里主动逐个 `Disconnect()`，并且马上 `PollEvents()`，所以断连事件是会沿调用链继续往上的。

## 7.5 入口 E：连接创建失败

这个主要发生在客户端侧 `TCPNetManager.Connect(...)`。

如果 `ConnectAsync(...)` 失败：

1. 关闭 socket
2. `CreateErrorEvent(...)`
3. 不会产生正常的 peer 断连链

这更像“连接失败”，不是“已连接后断开”。

## 8. TCP 断连统一主链

无论是接收失败、发送失败、主动断开还是停服，当前大多数入口最终都会收敛到：

1. `TCPPeer.Disconnect(reason, error)`
2. 使用 `_isDisconnected` 保证只执行一次
3. `Socket.Shutdown + Close`
4. 清空 `_sendQueue`
5. `TCPNetManager.OnPeerDisconnected(peer, reason, error)`
6. 从 `_connectedPeers` 删除
7. 创建 `TCPNetEvent.Disconnect`
8. `PollEvents()`
9. `ProcessEvent(Disconnect)`
10. `ITCPEventListener.OnPeerDisconnected(peer, info)`
11. `TcpServerService` 删除 `_connections` 中对应 `INetConnection`
12. 触发 `TcpServerService.OnClientDisconnected(conn)`
13. `ServerRuntime` 转发到 `ConnectionLifecycleService.OnTcpDisconnected(conn)`
14. 生命周期层通过 `tcpConnectionId -> PlayerSession` 找到 session
15. 执行统一清理：
   - 从房间移除
   - 从战斗移除
   - 删除玩家业务索引
   - 删除 TCP/UDP 连接反查索引
   - 必要时断开 UDP

所以你当前项目中的 TCP 断连最终是：

`TCPPeer -> TCPNetManager -> TcpServerService -> ServerRuntime -> ConnectionLifecycleService`

## 9. TCP 停服时的特殊补偿

除了 `TCPNetManager.Stop()` 自己会逐个触发断连事件外，`TcpServerService.Stop()` 还做了一层补偿：

1. 调 `_manager.Stop()`
2. 把 `_manager = null`
3. 遍历 `_connections.ToArray()`
4. 再尝试逐个删除并回调 `OnClientDisconnected`

这层补偿的意义是：

- 防止底层 stop 期间有残留连接没有完全从 `_connections` 删除
- 确保上层生命周期链尽量完整收尾

所以 TCP 这一侧比 UDP stop 更稳一些。

## 10. TCP 连接与 UDP 连接的本质差异

虽然两边都最终会进 `ConnectionLifecycleService`，但它们的业务意义不同。

### TCP

- 是世界层长期在线连接
- 登录、聊天、商店、背包、装备切换都依赖它
- 一旦 TCP 断开，当前玩家 session 整体就会被清理

### UDP

- 是战斗期附加连接
- 当前策略下，UDP 断开不等于离开 battle
- 只是标记 battle 内掉线并解绑 UDP

所以：

- TCP 断开 = 在线态生命周期结束
- UDP 断开 = 战斗内附加链路暂时中断

## 11. 当前项目中 TCP 调用链的方向

### 11.1 启动链

`ServerRuntime -> TcpServerService -> TCPNetManager -> Socket`

这是上层向下。

### 11.2 运行时事件链

`Socket/TCPPeer -> TCPNetManager -> TcpServerService -> ServerRuntime -> ConnectionLifecycleService -> Login/Player/Room/Battle`

这是底层向上。

所以正确理解是：

- 创建 TCP 网络能力时，是上层驱动底层
- 真正的建连、收包、断连事件，是底层逐层回调上来

## 12. 最后结论

你当前项目里，TCP 已经形成了比较完整的一条责任链：

- 底层 socket 建连、收包、断连
- TCP 管理器负责转成统一事件
- `TcpServerService` 负责连接适配器和消息入口
- `ServerRuntime` 负责把事件接入服务端运行时
- `ConnectionLifecycleService` 负责把连接事件翻译成玩家生命周期

这条链和你现在的 UDP 链是对称的，只是业务语义不同：

- TCP 更像“世界层主连接”
- UDP 更像“战斗层附加连接”
