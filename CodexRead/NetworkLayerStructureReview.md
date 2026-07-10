# KTSG 网络层结构说明与冗余评估

本文目标：

- 说明以下目录和文件中各个类在当前结构中的职责
- 解释它们之间的层级关系和调用方向
- 回答“为什么看起来还有很多中间层”这个问题
- 给出哪些层目前必要，哪些层后续还可以继续收敛

涉及范围：

- `D:\KTSGServer\KTSGClientCommon\Client`
- `D:\KTSGServer\KTSGServerCommon\BattleNet`
- `D:\KTSGServer\KTSGServerCommon\Gateway`
- `D:\KTSGServer\KTSGServerCommon\INetConnection`
- `D:\KTSGServer\KTSGServerCommon\LiteLibExtension`
- `D:\KTSGServer\KTSGServerCommon\TCP\TCPNetManager.cs`
- `D:\KTSGServer\KTSGServerCommon\TCP\TCPPeer.cs`

## 1. 先说结论

你现在的网络层已经比之前收敛了不少，但仍然存在两类“看起来像中间层很多”的来源：

- 一类是必要分层
  - 比如协议格式层、连接抽象层、传输宿主层、消息分发层
  - 这些不是冗余，而是职责边界
- 一类是自定义 TCP 底层带来的内部层
  - 比如 `TCPNetEvent / TCPNetPacket / TCPNetPacketReader / TCPNetPacketPool`
  - 这些是你自己实现事件队列和包复用时自然长出来的内部结构

当前真正偏冗余的，已经处理掉一部分：

- 旧的 `NetworkService` 已删除
- `TCPClientManager / UDPClientManager` 已从服务端公共层剥离
- `INetConnection` 已从胖接口收窄

但仍然还有一些“可以继续减”的点，后文会单独说。

## 2. 当前总层级

当前建议把整个结构理解成 6 层。

### 第 1 层：业务运行时层

服务端在 `KTSGServerCore`：

- `ServerRuntime`
- `TcpServerService`
- `BattleService`
- `PlayerService`

客户端在 Unity 或未来客户端项目：

- `NetworkSystem`
- 房间、登录、战斗流程系统

这一层关心的是：

- 登录
- 房间
- 战斗
- token
- 在线玩家

它不应该关心底层 socket 细节。

### 第 2 层：宿主入口层

这一层负责“真正把某种连接跑起来”。

客户端：

- `TcpClientManager`
- `UdpClientManager`

服务端：

- `TcpServerService`
- `BattleUdpGateway`

这一层关心的是：

- 启动/停止
- 建连/断连
- PollEvents
- 把收到的字节流交给消息处理器

### 第 3 层：连接抽象层

核心是：

- `INetConnection`
- `TcpConnectionAdapter`
- `UdpConnectionAdapter`

这一层关心的是：

- “这是一条 TCP 连接还是 UDP 连接”
- “我能不能发消息”
- “我要不要断开”

这一层不应该直接携带业务身份，比如 `PlayerId`、`BattleId`。

### 第 4 层：协议与发送策略层

核心是：

- `TcpProtocol`
- `UdpProtocol`
- `BattleUdpChannels`
- `BattleUdpSendPolicy`
- `BattleUdpSendExtensions`

这一层关心的是：

- 包头格式
- `cmdId`
- `seq`
- channel
- 可靠性语义

### 第 5 层：消息分发层

核心是：

- `NetMsgProcessor`
- `ReqHandler`
- `MsgHandler`

这一层关心的是：

- 收到某个 `cmdId` 后交给哪个 handler
- RPC 回包
- 多程序集扫描 handler

### 第 6 层：底层传输实现层

TCP 自研部分：

- `TCPNetManager`
- `TCPPeer`
- `TCPNetEvent`
- `TCPNetPacket`
- `TCPNetPacketPool`
- `TCPNetPacketReader`
- `NetBuffer`

UDP 第三方部分：

- LiteNetLib `NetManager`
- LiteNetLib `NetPeer`

这一层关心的是：

- socket
- accept/connect
- 收发循环
- 缓冲区
- 包对象复用

## 3. KTSGClientCommon/Client

这一层已经从服务端公共层剥离出来，现在属于“客户端共享宿主层”。

### 3.1 TcpClientManager

文件：

- `D:\KTSGServer\KTSGClientCommon\Client\TCPClientManager.cs`

职责：

- 创建 `TCPNetManager`
- 发起 TCP 客户端连接
- 持有当前唯一的 `INetConnection`
- 在 `Update()` 中驱动 `PollEvents()`
- 收到 TCP 消息后直接交给 `NetMsgProcessor`

调用方向：

- `TcpClientManager`
  - 依赖 `TCPNetManager`
  - 依赖 `TcpConnectionAdapter`
  - 依赖 `NetMsgProcessor`

它在客户端结构里是“宿主入口层”，不是协议层，也不是业务层。

### 3.2 UdpClientManager

文件：

- `D:\KTSGServer\KTSGClientCommon\Client\UDPClientManager.cs`

职责：

- 创建 LiteNetLib `NetManager`
- 连接战斗 UDP 服务器
- 持有当前唯一 UDP `INetConnection`
- 在 `Update()` 中驱动 LiteNetLib `PollEvents()`
- 收到 UDP 包后用 `UdpProtocol` 解头，再交给 `NetMsgProcessor`

调用方向：

- `UdpClientManager`
  - 依赖 LiteNetLib `NetManager`
  - 依赖 `UdpConnectionAdapter`
  - 依赖 `UdpProtocol`
  - 依赖 `NetMsgProcessor`

### 3.3 这层是否冗余

结论：

- 不冗余
- 但它们更适合作为“客户端宿主层”单独存在，不应继续放在 `KTSGServerCommon`

这轮已经完成了这一步收敛。

## 4. KTSGServerCommon/BattleNet

这一层不是连接管理层，而是“战斗 UDP 发送策略层”。

### 4.1 BattleUdpChannels

文件：

- `D:\KTSGServer\KTSGServerCommon\BattleNet\BattleUdpChannels.cs`

职责：

- 定义 UDP channel 约定
- `State = 0`
- `Event = 1`
- `Bulk = 2`
- `ChannelCount = 3`

这是协议约定常量层。

### 4.2 BattleUdpMessageKind

文件：

- `D:\KTSGServer\KTSGServerCommon\BattleNet\BattleUdpSendPolicy.cs`

职责：

- 定义“战斗消息语义类别”
- 例如：
  - `InputState`
  - `MotionState`
  - `CombatEvent`
  - `FullSnapshot`

这不是底层传输枚举，而是业务语义到传输策略之间的中间映射。

### 4.3 BattleUdpSendPolicy

职责：

- 表达“某种消息应该走哪个 channel，用什么投递语义”
- 即：
  - `ChannelId`
  - `DeliveryMode`

### 4.4 BattleUdpSendPolicies

职责：

- 把 `BattleUdpMessageKind` 映射成 `BattleUdpSendPolicy`

比如：

- 输入状态 -> `State + Sequenced`
- 战斗事件 -> `Event + ReliableOrdered`
- 快照 -> `Bulk + ReliableOrdered`

### 4.5 BattleUdpSendExtensions

文件：

- `D:\KTSGServer\KTSGServerCommon\BattleNet\BattleUdpSendExtensions.cs`

职责：

- 给 `INetConnection` 提供更高层的“按战斗语义发包”入口
- 上层不用每次都手填 `channel + delivery mode`

调用方向：

- `BattleService / Battle Runtime`
  - 调 `conn.SendBattle(...)`
  - 由扩展内部转成 `SendProto(...)`
  - 再落到 `INetConnection.TrySend(...)`

### 4.6 这层是否冗余

结论：

- 不冗余
- 它是“传输策略配置层”
- 它的价值在于把“业务消息类别”和“LiteNetLib DeliveryMethod/channel”解耦

如果删掉这一层，上层会反复直接写：

- `channelId = 1`
- `ReliableOrdered`
- `Sequenced`

这会让战斗代码到处散落网络细节。

## 5. KTSGServerCommon/Gateway

### 5.1 BattleUdpGateway

文件：

- `D:\KTSGServer\KTSGServerCommon\Gateway\BattleUdpGateway.cs`

职责：

- 启动和停止 UDP 服务端宿主
- 持有 LiteNetLib `NetManager`
- 在连接请求阶段做 token 校验
- 在连接建立后创建 `UdpConnectionAdapter`
- 在收包时解出 `cmdId/seq/body`
- 调用 `NetMsgProcessor`
- 提供 UDP 广播能力

它的真实角色是：

- 战斗 UDP 网关

不是：

- Session
- Service
- 业务房间对象

调用方向：

- `ServerRuntime`
  - 配置 `TokenValidator`
  - 配置 `OnAuthedPeerConnected`
  - 配置 `OnPeerDisconnectedAction`
- `BattleUdpGateway`
  - 持有 LiteNetLib `NetManager`
  - 收到包后调用 `NetMsgProcessor`

### 5.2 这层是否冗余

结论：

- 不冗余
- 但位置有争议

它的职责已经比较清楚了，不过它其实更偏服务端运行时宿主层，理论上也可以搬到 `KTSGServerCore`。

当前放在 `Common` 的理由大概是：

- 复用 `INetConnection`
- 复用 `UdpProtocol`
- 复用 LiteNetLib 封装

但从架构纯度上说，它比 `TcpProtocol` 更靠近服务端运行时。

## 6. KTSGServerCommon/INetConnection

### 6.1 INetConnection

文件：

- `D:\KTSGServer\KTSGServerCommon\INetConnection\INetConnection.cs`

职责：

- 提供统一的连接抽象

当前保留的核心信息：

- `ConnectionId`
- `TransportKind`
- `RemoteEndPoint`
- `IsConnected`
- `TrySend(...)`
- `Disconnect()`

### 6.2 SendOptions

职责：

- 表达单次发送的附加参数：
  - `ChannelId`
  - `DeliveryMode`
  - `RpcSeq`

### 6.3 NetConnectionProtoExtensions

职责：

- 把 protobuf `IMessage` 转成 `byte[]`
- 再调用 `TrySend(...)`

这样做的意义是：

- 连接接口不再直接依赖 protobuf 语义
- 传输层和消息格式层分开

### 6.4 TcpConnectionAdapter

文件：

- `D:\KTSGServer\KTSGServerCommon\INetConnection\TCPConnectionApdater.cs`

职责：

- 把 `TCPPeer` 适配成 `INetConnection`

也就是说：

- 上层只看到 `INetConnection`
- 底层真正发送时仍然走 `TCPPeer`

### 6.5 这层是否冗余

结论：

- 不冗余
- 这是当前 TCP/UDP 统一消息处理链的关键边界

如果没有这层：

- `NetMsgProcessor` 就要区分 TCP 和 UDP peer 类型
- `PlayerSession` 就不能用统一连接抽象
- `ReqHandler` 回包时也得分 TCP/UDP 两套逻辑

## 7. KTSGServerCommon/LiteLibExtension

这个目录现在实际上包含两类东西。

### 7.1 UdpConnectionAdapter

文件：

- `D:\KTSGServer\KTSGServerCommon\LiteLibExtension\LiteNetPeerExtension.cs`

职责：

- 把 LiteNetLib `NetPeer` 适配成 `INetConnection`
- 在发送时写入 `UdpProtocol` 头
- 把 `SendOptions` 映射成 LiteNetLib `DeliveryMethod`

这其实不是“Extension”概念，实际上更像：

- `UdpConnectionAdapter`

### 7.2 UdpProtocol

文件：

- `D:\KTSGServer\KTSGServerCommon\LiteLibExtension\UDPProtocol.cs`

职责：

- 定义 UDP 包头格式
- 写入和读取：
  - magic
  - `cmdId`
  - `seq`

### 7.3 这层是否冗余

结论：

- 功能不冗余
- 但目录命名不准确

`LiteLibExtension` 这个名字已经不太贴切了，因为现在里面最关键的不是扩展方法，而是：

- `UdpConnectionAdapter`
- `UdpProtocol`

更合理的目录名是：

- `Transport/Udp`
- 或 `Protocol/Udp`

## 8. TCP/TCPNetManager.cs

### 8.1 TCPNetManager

文件：

- `D:\KTSGServer\KTSGServerCommon\TCP\TCPNetManager.cs`

职责：

- 管理自定义 TCP 连接宿主
- 启动服务端监听
- 发起客户端连接
- 管理 `TCPPeer`
- 把 peer 收到的数据包装成事件
- 通过事件队列在 `PollEvents()` 中统一回调给监听器

它相当于自研 TCP 版的：

- “宿主 + peer 容器 + 主线程事件泵”

### 8.2 它依赖的内部结构

`TCPNetManager` 依赖：

- `ITCPEventListener`
- `TCPPeer`
- `TCPNetEvent`
- `TCPNetPacket`
- `TCPNetPacketPool`
- `TCPNetPacketReader`

它自己不做业务处理，只负责：

- 接入 socket
- 建连/断连
- 事件排队
- 派发回调

### 8.3 为什么会有这么多内部类型

因为你不是直接同步回调 socket，而是做了：

- peer 对象
- packet 对象
- event 对象
- reader 包装
- packet 池
- event 池

这种写法的优点：

- 生命周期清楚
- `PollEvents()` 统一驱动
- 便于复用包对象

缺点：

- 类型数明显偏多
- 阅读成本高

### 8.4 这层是否冗余

结论：

- `TCPNetManager` 本身不冗余
- 但它内部的辅助类型偏多

这里的“中间层感”主要来自事件池和 reader 封装，而不是来自业务层。

## 9. TCP/TCPPeer.cs

### 9.1 TCPPeer

文件：

- `D:\KTSGServer\KTSGServerCommon\TCP\TCPPeer.cs`

职责：

- 代表一条实际 TCP 连接
- 管理该连接的收包循环
- 管理该连接的发包队列
- 调用 `TcpProtocol` 拆包/封包
- 在收到完整包后回调给 `TCPNetManager`

### 9.2 内部流程

收包：

1. 从 socket 读到 `NetBuffer`
2. 用 `TcpProtocol.ReadHeader(...)` 检查是否已有完整包
3. 拿到完整 body
4. 包装成 `TCPNetPacket`
5. 交给 `TCPNetManager.OnTcpPacketReceived(...)`

发包：

1. `Send(...)`
2. 用 `TcpProtocol.WriteHeader(...)` 写头
3. 放进 `_sendQueue`
4. `TryStartSendLoop()` 异步发送

### 9.3 这层是否冗余

结论：

- 不冗余
- 它就是 TCP 连接对象本身

如果要继续简化 TCP 目录，`TCPPeer` 通常不会被删，它只会被保留或被重写。

## 10. 现在为什么还会觉得“中间层很多”

你的这种感觉是对的，主要来源有 3 个。

### 10.1 自定义 TCP 底层自己长出了一套内部对象模型

也就是：

- `TCPNetManager`
- `TCPPeer`
- `TCPNetEvent`
- `TCPNetPacket`
- `TCPNetPacketReader`
- `TCPNetPacketPool`

如果用成熟现成 TCP 框架，这些很多可以省。

### 10.2 UDP 同时存在两种“层级感”

一条是 LiteNetLib 自己的：

- `NetManager`
- `NetPeer`

一条是你为了统一结构又加的：

- `BattleUdpGateway`
- `UdpConnectionAdapter`
- `UdpProtocol`

这会带来明显的“包装层感”。

### 10.3 目录命名还没有完全跟职责对齐

比如：

- `LiteLibExtension`
- `TCP`
- `Gateway`

现在类职责已经比以前清楚，但目录名仍然容易让人误解。

## 11. 哪些层现在可以继续减

### 11.1 可以继续减的

- `TCPNetPacketReader`
  - 当前只是 `NetDataReader` 包装，存在感偏弱
- `TCPNetEvent`
  - 如果以后改成更直接的事件分发，这层可以合并
- `MyLog`
  - 目录位置不合理，应移出 `TCP`
- `LiteLibExtension` 目录
  - 应更名，不必继续叫 extension

### 11.2 目前不建议轻易删的

- `INetConnection`
- `TcpConnectionAdapter`
- `UdpConnectionAdapter`
- `BattleUdpSendPolicy`
- `BattleUdpGateway`
- `TCPNetManager`
- `TCPPeer`
- `TcpProtocol`
- `UdpProtocol`

这些现在都在承担明确边界。

## 12. 我对当前结构的最终判断

如果从“是否还能跑、是否职责基本成立”来看：

- 当前结构已经比一开始清晰很多

如果从“是否还足够精简”来看：

- 还没有到最简

最主要的问题已经不是业务层，而是自定义 TCP 目录内部类型偏多，以及目录命名和职责没有完全对齐。

## 13. 下一轮最值得继续收敛的方向

如果继续做，我建议按这个顺序：

1. 把 `LiteLibExtension` 改名成更准确的 `Transport/Udp`
2. 把 `MyLog` 从 `TCP` 目录移出去
3. 清理 `TCPNetPacketReader` 和 `TCPNetEvent` 的无效包装感
4. 再评估 `BattleUdpGateway` 是否应从 `Common` 挪到 `Core`

## 14. 一句话版

你现在感觉“中间层很多”，并不是错觉。

但当前真正多出来的，不再是业务层乱套，而主要是：

- 自定义 TCP 底层内部对象过多
- UDP 目录命名不准确
- 部分公共层里还混着偏宿主层的类

换句话说，现在剩下的问题更像“网络基础设施整理问题”，而不是“业务架构完全没边界”的问题。
