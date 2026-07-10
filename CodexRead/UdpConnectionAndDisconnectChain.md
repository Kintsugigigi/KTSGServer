# UDP 连接与断连调用链

## 1. 文档目的

这份文档说明两件事：

1. LiteNetLib 内部一条完整的 UDP 断连链是怎样形成的。
2. 你当前项目里 UDP 侧“连接建立”和“连接断开”的调用链，究竟是从 `ServerRuntime` 往下驱动，还是从 LiteNetLib 往上回调。

最终结论先写在前面：

- UDP 服务端“启动入口”是自上而下：
  - `ServerRuntime -> UdpServerService -> LiteNetLib`
- UDP 的“实际接入事件”和“断连事件”是自下而上：
  - `LiteNetLib -> UdpServerService -> ServerRuntime -> ConnectionLifecycleService -> BattleService/PlayerSession`

所以它不是单向链，而是：

- 启动时：上层驱动底层
- 运行时事件：底层回调上层

## 2. 你项目里 UDP 相关的主要层

### 2.1 服务端宿主层

- `ServerRuntime`
- `UdpServerService`
- `ConnectionLifecycleService`

### 2.2 业务层

- `BattleService`
- `BattleSession`
- `PlayerSession`

### 2.3 底层网络层

- `LiteNetLib.NetManager`
- `LiteNetLib.LiteNetManager`
- `LiteNetLib.NetPeer / LiteNetPeer`

你项目里真正实例化的是 `NetManager`，但核心逻辑主要在 `LiteNetManager + LiteNetPeer`。

## 3. UDP 启动调用链

## 3.1 启动入口

当前启动入口是：

1. `ServerRuntime.Start()`
2. 如果配置要求启动 UDP，则调用 `StartUdpGateway()`
3. `StartUdpGateway()` 调用 `UdpServerService.Start(port)`
4. `UdpServerService` 内部创建 `new NetManager(this)`
5. 配置：
   - `ChannelsCount`
   - `UpdateTime`
   - 其他 LiteNetLib 参数
6. 调 `NetManager.Start(port)`

所以 UDP 服务端的创建方向是：

`ServerRuntime -> UdpServerService -> NetManager`

这是标准的“上层驱动底层”。

## 3.2 事件接线

在 `ServerRuntime` 构造时，会先把 UDP 生命周期事件接好：

- `TokenValidator`
- `OnAuthedPeerConnected`
- `OnPeerDisconnectedAction`

也就是说，虽然启动是上层往下调用，但 LiteNetLib 运行之后，所有连接事件都会反过来回调回上层。

## 4. UDP 接入调用链

UDP 接入不是“连上就算成功”，而是“token 校验成功并完成 battle 绑定”之后才算业务上真正接入。

## 4.1 客户端侧起点

客户端先通过 TCP 拿到：

- `battleId`
- `udp host`
- `udp port`
- `battle token`

然后客户端调用：

- `UdpClientManager.Connect(ip, port, token)`

LiteNetLib 会把这个 token 放进连接请求数据里。

## 4.2 服务端侧连接请求入口

服务端收到 UDP 连接请求后，LiteNetLib 先进入：

- `OnConnectionRequest(ConnectionRequest request)`

在你当前项目中，这个入口由 `UdpServerService` 实现。

当前链路是：

1. LiteNetLib 收到 UDP connect request
2. `UdpServerService.OnConnectionRequest(...)`
3. 调用 `TokenValidator(token, endPoint)`
4. 如果 token 合法：
   - 暂存 `endPoint -> token`
   - `request.Accept()`
5. 如果 token 不合法：
   - `request.Reject()`

注意：

- 这一步只是“允许建立 UDP peer”
- 还不是“业务层已经绑定到 battle”

## 4.3 UDP peer 真正建立后的链路

当 LiteNetLib 真正把 peer 建好后：

1. LiteNetLib 触发 `OnPeerConnected(NetPeer peer)`
2. `UdpServerService.OnPeerConnected(...)`
3. 创建 `UdpConnectionAdapter`
4. 设置 `peer.Tag = conn`
5. 根据 endpoint 找回之前暂存的 token
6. 调 `OnAuthedPeerConnected(conn, token)`
7. `ServerRuntime` 收到回调
8. 转发给 `ConnectionLifecycleService.OnUdpAuthenticated(...)`
9. 生命周期层调用 `BattleService.TryBindUdp(...)`
10. 生命周期层绑定：
   - `PlayerSession.UdpConnection`
   - `udpConnectionId -> PlayerSession`
11. `BattleSession` 把该玩家标记为 `UdpConnected`

所以“业务意义上的 UDP 接入完成”是发生在：

`LiteNetLib.OnPeerConnected -> UdpServerService -> ServerRuntime -> ConnectionLifecycleService`

这条链是**自下而上**的。

## 5. UDP 收包调用链

这条链有助于理解“断连为什么也是底层往上回调”。

1. LiteNetLib socket 线程收到 UDP 数据
2. `LiteNetManager` / `NetManager` 解析包
3. 对应 peer 的事件进入待处理队列
4. 你的 `UdpServerService.Update()` 调用 `NetManager.PollEvents()`
5. LiteNetLib 回调 `OnNetworkReceive(NetPeer peer, ...)`
6. `UdpServerService.OnNetworkReceive(...)`
7. 取出 `peer.Tag` 中的 `INetConnection`
8. 用 `UdpProtocol.ReadHeader(...)` 解析你的业务包头
9. 转发到 `NetMsgProcessor`

这说明运行时的 UDP 网络事件全都是：

`LiteNetLib -> UdpServerService -> 你的业务层`

## 6. LiteNetLib 里 UDP 断连的统一收敛点

先说核心结论：

LiteNetLib 中，无论断连来自哪个入口，绝大多数最终都会收敛到这条主链：

1. 某个入口决定 peer 应该断开
2. 调 `DisconnectPeer(...)` 或 `DisconnectPeerForce(...)`
3. 内部调用 `LiteNetPeer.Shutdown(...)`
4. 创建 `NetEvent.EType.Disconnect`
5. `PollEvents()` 或立即处理事件
6. `ProcessEvent(...)`
7. 调 `OnPeerDisconnected(...)`

也就是说，对你项目最重要的统一观察点不是 `ShutdownOk`，而是：

- `DisconnectPeer(...)`
- `DisconnectPeerForce(...)`
- `CreateEvent(NetEvent.EType.Disconnect)`
- `OnPeerDisconnected(...)`

## 7. LiteNetLib 中所有主要 UDP 断连入口

下面按来源分。

## 7.1 主动发起断开

### 入口 A：业务代码调用 `peer.Disconnect()`

链路：

1. `LiteNetPeer.Disconnect()`
2. `LiteNetManager.DisconnectPeer(peer)`
3. `LiteNetPeer.Shutdown(force:false)`
4. 发 `PacketProperty.Disconnect`
5. 立刻创建本地 `Disconnect` 事件

特点：

- 这是“主动优雅断开”
- 本地 `OnPeerDisconnected` 不需要等待对方确认才触发

### 入口 B：`DisconnectAll()`

链路：

1. `LiteNetManager.DisconnectAll()`
2. 遍历所有 peer
3. 对每个 peer 调 `DisconnectPeer(...)`

## 7.2 收到对端断开包

### 入口 C：收到 `PacketProperty.Disconnect`

链路：

1. LiteNetLib 收包
2. `LiteNetManager` 处理 `PacketProperty.Disconnect`
3. `netPeer.ProcessDisconnect(packet)`
4. 如果验证通过：
   - 已连接状态：`RemoteConnectionClose`
   - 连接中状态：`ConnectionRejected`
5. 调 `DisconnectPeerForce(...)`
6. 回发 `ShutdownOk`

这就是“对端主动断开”的标准入口。

### `ShutdownOk` 的作用

`ShutdownOk` 只表示：

- 对方已经确认收到你的断开包
- 本地可把状态从 `ShutdownRequested` 改成 `Disconnected`

它不是主要的断连事件入口。

## 7.3 超时断开

### 入口 D：`DisconnectTimeout`

链路：

1. `LiteNetPeer.Update(deltaTime)`
2. 如果 `TimeSinceLastPacket > DisconnectTimeout`
3. `DisconnectPeerForce(..., DisconnectReason.Timeout, ...)`

这是“长时间没收到任何包”的典型掉线入口。

## 7.4 连接建立失败

### 入口 E：连接尝试次数耗尽

链路：

1. peer 处于 `Outgoing`
2. 周期性重发连接请求
3. 超过 `MaxConnectAttempts`
4. `DisconnectPeerForce(..., DisconnectReason.ConnectionFailed, ...)`

这属于“根本没连上”的失败断开。

## 7.5 握手重连/冲突类断开

### 入口 F：Reconnection

在 `ProcessConnectRequest(...)` 中：

- 老 peer 被新连接顶替
- `DisconnectPeerForce(..., DisconnectReason.Reconnect, ...)`

### 入口 G：PeerToPeerConnection

在 `ProcessConnectRequest(...)` 中：

- P2P 冲突输掉
- `DisconnectPeerForce(..., DisconnectReason.PeerToPeerConnection, ...)`

## 7.6 协议或状态异常

### 入口 H：InvalidProtocol

- 收到 `PacketProperty.InvalidProtocol`
- 若当前处于 `Outgoing`
- `DisconnectPeerForce(..., DisconnectReason.InvalidProtocol, ...)`

### 入口 I：PeerNotFound

- 收到 `PeerNotFound` 的二次确认
- `DisconnectPeerForce(..., DisconnectReason.PeerNotFound, ...)`

## 7.7 Socket 层不可达

### 入口 J：HostUnreachable / NetworkUnreachable

发生在 socket 收发层。

如果 `DisconnectOnUnreachable = true`：

- `DisconnectPeerForce(...)`
- reason 为：
  - `HostUnreachable`
  - `NetworkUnreachable`

如果这个开关没开：

- 只发 `OnNetworkError`
- 不会自动断 peer

## 7.8 Stop 是特殊入口

### 入口 K：`NetManager.Stop()`

这里要特别注意。

`Stop()` 的行为更接近：

- 关闭整个 manager
- 尝试发最后的 shutdown
- 关 socket
- 停线程
- 清理 peer 和待处理事件

它不是“逐个 peer 做完整业务断连回调”的稳定入口。

所以结论是：

- 不应该依赖 `Stop()` 逐个触发你自己的完整 UDP 断线业务链
- 真正可靠的业务断线入口仍然是 `OnPeerDisconnected(...)`

## 8. 你当前项目中 UDP 断连的完整调用链

当前项目实际运行时的 UDP 断连链如下：

1. LiteNetLib 内部某个断连入口触发
   - 主动断开
   - 收到对端断开包
   - 超时
   - 握手失败
   - 协议错误
   - 不可达
2. LiteNetLib 调 `DisconnectPeer(...)` 或 `DisconnectPeerForce(...)`
3. LiteNetLib 创建 `Disconnect` 事件
4. `NetManager.ProcessEvent(...)`
5. 调用 `UdpServerService.OnPeerDisconnected(NetPeer peer, DisconnectInfo info)`
6. `UdpServerService` 从 `peer.Tag` 取出 `INetConnection`
7. 调用 `OnPeerDisconnectedAction(conn)`
8. `ServerRuntime` 把事件转给 `ConnectionLifecycleService.OnUdpDisconnected(...)`
9. 生命周期层通过 `udpConnectionId -> PlayerSession` 反查玩家
10. 生命周期层调用 `BattleService.HandleUdpDisconnected(player)`
11. `BattleService` 更新 `BattleSession` 中该玩家的状态：
   - `UdpDisconnected`
   - 最近断开时间
   - 断线次数
12. `PlayerSession` 清掉 `UdpConnection`
13. 生命周期层删除 `udpConnectionId -> PlayerSession`

所以你当前项目中的 UDP 掉线最终结果是：

- 玩家仍然属于这场 `BattleSession`
- 但其 UDP 链路已经断开
- battle 中记录该玩家当前处于掉线状态

## 9. 你当前项目中 UDP 接入的完整调用链

当前项目的 UDP 接入链如下：

1. `ServerRuntime` 启动 `UdpServerService`
2. `UdpServerService.Start(port)` 创建 `NetManager`
3. 客户端通过 TCP 获取 token
4. 客户端发起 UDP 连接
5. LiteNetLib 进入 `OnConnectionRequest(...)`
6. `UdpServerService` 调 token 校验
7. LiteNetLib 建立 peer
8. LiteNetLib 调 `UdpServerService.OnPeerConnected(...)`
9. `UdpServerService` 创建 `UdpConnectionAdapter`
10. `ServerRuntime` 收到 `OnAuthedPeerConnected`
11. `ConnectionLifecycleService.OnUdpAuthenticated(...)`
12. `BattleService.TryBindUdp(...)`
13. 生命周期层登记：
   - `PlayerSession.UdpConnection`
   - `udpConnectionId -> PlayerSession`
14. `BattleSession` 标记该玩家 `UdpConnected`

这里要特别记住：

- 启动 UDP 服务端是上层向下
- 某个具体玩家的 UDP 接入成功是底层向上

## 10. 最后结论

UDP 侧在你当前项目里有两条方向不同的链：

### 10.1 启动链

`ServerRuntime -> UdpServerService -> LiteNetLib`

这是“服务端启动网络能力”的上层驱动链。

### 10.2 事件链

`LiteNetLib -> UdpServerService -> ServerRuntime -> ConnectionLifecycleService -> BattleService`

这是“玩家接入、收包、掉线”的底层回调链。

所以正确理解应该是：

- 不是单纯从 `ServerRuntime` 到 LiteNetLib
- 也不是单纯从 LiteNetLib 到 `ServerRuntime`
- 而是：
  - 创建和启动时向下
  - 运行时连接/断连事件向上

这也是为什么你需要单独保留：

- `UdpServerService`
- `ConnectionLifecycleService`

这两层。

因为 LiteNetLib 只负责底层网络事件，不负责你的战斗业务状态同步。
