# LiteNetLib 源码链路阅读记录

## 1. 阅读范围

本次阅读目录为 `D:\KTSGServer\LiteNetLib`，重点关注：

- `LiteNetManager*`
- `LiteNetPeer`
- `NetManager`
- `NetPeer`
- `NetPacket`
- `InternalPackets`
- `BaseChannel`
- `ReliableChannel`
- `SequencedChannel`
- `ConnectionRequest`
- `NetEvent`
- `NetPacketReader`
- `PooledPacket`

`Utils` 下的大部分工具类没有展开细读，只在必要处结合调用点理解用途。

## 2. 总体架构

LiteNetLib 在这个仓库里的核心可以理解为 4 层：

1. Socket / IO 层
   - `LiteNetManager.Socket.cs`
   - 负责 UDP Socket 创建、绑定、收发、收包线程

2. Manager 层
   - `LiteNetManager.cs`
   - 负责事件队列、Peer 生命周期、握手调度、主循环、事件派发

3. Peer / 协议层
   - `LiteNetPeer.cs`
   - 负责单连接状态机、Ping/Pong、MTU、分片、合包、断线、Channel 调度

4. Channel 层
   - `BaseChannel.cs`
   - `ReliableChannel.cs`
   - `SequencedChannel.cs`
   - 负责不同投递语义的发送窗口、ACK、重传、乱序缓存

这套库的关键点是：

- “Manager 负责管理 Peer 和事件”
- “Peer 负责协议状态机”
- “Channel 负责可靠性/顺序语义”
- “Socket 层只负责收发原始 UDP 字节”

## 3. 这个仓库里的分叉点

你当前仓库不是原版 LiteNetLib 的直接镜像，而是做过定制，最重要的是：

### 3.1 `NetManager`

`NetManager` 继承 `LiteNetManager`，主要增加：

- `ChannelsCount`
- 面向 `INetEventListener` 的事件接口
- 多通道 `SendToAll(...)`
- `Connect(...)` 返回 `NetPeer`
- 可选 NTP 处理

### 3.2 `NetPeer`

`NetPeer` 继承 `LiteNetPeer`，主要增加：

- `BaseChannel[] _channels`
- `ConcurrentQueue<BaseChannel> _channelSendQueue`
- 每个逻辑通道都能有 4 种 DeliveryMethod
- 支持 `channelNumber * ChannelTypeCount + deliveryMethod` 的索引模式

这意味着：

- `LiteNetPeer` 是“单组通道”的基础实现
- `NetPeer` 是“多逻辑通道”的增强实现

所以你项目里真正跑 UDP 的主类，实际应当以 `NetManager` / `NetPeer` 为准，而不是只看 `LiteNetManager` / `LiteNetPeer`。

## 4. 启动与线程模型

## 4.1 启动入口

`LiteNetManager.Start(...)` 在 [LiteNetManager.Socket.cs](D:\KTSGServer\LiteNetLib\LiteNetManager.Socket.cs) 里负责：

- 创建 IPv4 UDP Socket
- 绑定本地端口
- 可选创建 IPv6 Socket
- 设置各种 SocketOption
- 启动线程

非手动模式下会启动两个后台线程：

- `_receiveThread`
  - 执行 `ReceiveLogic()` 或 `NativeReceiveLogic()`
- `_logicThread`
  - 执行 `UpdateLogic()`

## 4.2 手动模式 vs 普通模式

### 普通模式

- 收包线程持续从 Socket 读取
- 逻辑线程持续驱动各个 Peer 的 `Update()`
- 主线程通常只需要 `PollEvents()`

### 手动模式

- `PollEvents()` 内部会顺便做 `ManualReceive(...)`
- 你还必须自己定时调 `ManualUpdate(elapsedMs)`

对游戏服务端来说，如果你想把网络和战斗 Tick 严格绑定，手动模式有价值；如果想先简单稳定，普通模式更直接。

## 5. Socket 到收包入口的链路

收包主链路如下：

1. `ReceiveLogic()` / `NativeReceiveLogic()`
2. `ReceiveFrom(...)`
3. `OnMessageReceived(packet, remoteEndPoint)`
4. `HandleMessageReceived(packet, remoteEndPoint)`
5. 根据包类型：
   - 握手类交给 `LiteNetManager`
   - 普通连接包交给 `LiteNetPeer.ProcessPacket(...)`
6. Peer 或 Channel 最终调用 `CreateReceiveEvent(...)`
7. `PollEvents()` 或立即回调触发业务层事件

## 5.1 Socket 层只做原始字节搬运

`ReceiveFrom(...)` 做的事情非常克制：

- 从 UDP Socket 读一包原始字节
- 填充到池化的 `NetPacket.RawData`
- 查找这个来源地址是否已有 Peer
- 然后进入 `OnMessageReceived(...)`

这一层并不理解业务包，也不理解可靠包、握手包或频道包。

## 5.2 `OnMessageReceived(...)`

这是“UDP 原始包进入协议栈”的第一站。

它会做：

- 空包过滤
- 调试用延迟模拟
- 调试用丢包模拟
- 然后调用 `HandleMessageReceived(...)`

所以这层主要是“收包入口 + 模拟网络环境”。

## 6. 收包后如何分类处理

`HandleMessageReceived(...)` 是 LiteNetLib 的中心分发点之一。

它的处理顺序是：

1. 更新统计
2. 经过 `CustomMessageHandle(...)`
3. 经过额外包处理层 `_extraPacketLayer`
4. `packet.Verify()` 校验基础报头合法性
5. 特判非连接包：
   - `ConnectRequest`
   - `Broadcast`
   - `UnconnectedMessage`
   - `NatMessage`
6. 查找来源地址对应的 Peer
7. 再按 `PacketProperty` 分发

## 6.1 非连接包

### `Broadcast`

- 如果启用 `BroadcastReceiveEnabled`
- 直接创建 `Broadcast` 事件

### `UnconnectedMessage`

- 如果启用 `UnconnectedMessagesEnabled`
- 直接创建 `ReceiveUnconnected` 事件

### `NatMessage`

- 交给 `NatPunchModule`
- 你当前服务端不用 NAT 穿透，这部分可以忽略

## 6.2 握手与连接控制包

### `ConnectRequest`

- 先验证协议号 `ProtocolId`
- 反序列化为 `NetConnectRequestPacket`
- 调 `ProcessConnectRequest(...)`

### `ConnectAccept`

- 仅对已存在的 outgoing peer 有效
- `LiteNetPeer.ProcessConnectAccept(...)` 成功后触发 `Connect` 事件

### `Disconnect`

- 交给 `LiteNetPeer.ProcessDisconnect(...)`
- 再由 `LiteNetManager.DisconnectPeerForce(...)` 触发断线事件
- 随后发送 `ShutdownOk`

### `PeerNotFound`

这条链是 LiteNetLib 里比较特别的一块。

用途是：

- 某个常规数据包到达时，接收端查不到对应 Peer
- 接收端回复 `PeerNotFound`
- 发送端据此判断：
  - 对方真的不认识我
  - 或者发生了地址变化漫游

配合 `AllowPeerAddressChange` 可以实现“IP/端口变化后的会话续接”。

## 7. 握手链路

## 7.1 客户端发起连接

`Connect(...)` 主链路：

1. `LiteNetManager.Connect(...)`
2. `CreateOutgoingPeer(...)`
3. `new LiteNetPeer(... connectData ...)`
4. 构造 `NetConnectRequestPacket`
5. 立即 `SendRaw(_connectRequestPacket, this)`

握手包里有：

- `ProtocolId`
- `ConnectionTime`
- `ConnectionNumber`
- `PeerId`
- `TargetAddress`
- 额外连接数据 `connectData`

## 7.2 服务端收到连接请求

`ProcessConnectRequest(...)` 会：

- 检查该地址是否已有 Peer
- 结合现有 Peer 状态判断：
  - 新连接
  - 重连
  - P2P 对撞谁赢谁输
- 将请求包装成 `ConnectionRequest`
- 放入 `_requestsDict`
- 触发 `OnConnectionRequest(...)`

真正的 Accept / Reject 并不是这里直接决定，而是交给上层监听器。

## 7.3 业务层 Accept / Reject

`ConnectionRequest` 提供：

- `Accept()`
- `AcceptIfKey(string key)`
- `Reject()`
- `RejectForce()`

一旦调用，会进入 `OnConnectionSolved(...)`：

- `Accept`
  - 创建 incoming peer
  - 发送 `ConnectAccept`
  - `AddPeer`
  - 触发 `Connect` 事件
- `Reject`
  - 创建 reject peer
  - 走 `Shutdown(...)`
- `RejectForce`
  - 直接构造 `Disconnect` 包回给对方

## 8. 发包链路总览

应用层对 `NetPeer.Send(...)` 或 `NetManager.SendToAll(...)` 的调用，会进入：

1. `LiteNetPeer.SendInternal(...)`
2. 选择 `PacketProperty` 和 `Channel`
3. 必要时做分片
4. 进入：
   - 不可靠队列 `_unreliableChannel`
   - 或 Channel 的 `OutgoingQueue`
5. 逻辑线程 `Peer.Update(...)`
6. `UpdateChannels()` / 不可靠包处理
7. `SendUserData(...)`
8. 必要时合并为 `Merged`
9. `LiteNetManager.SendRaw(...)`
10. Socket `SendTo(...)`

## 8.1 `SendInternal(...)`

`LiteNetPeer.SendInternal(...)` 是发送主入口。

它做 3 件事：

1. 判断投递方式
   - `Unreliable` -> `PacketProperty.Unreliable`
   - 其他 -> `PacketProperty.Channeled`

2. 选择 Channel
   - 基础 `LiteNetPeer` 只有几条固定通道
   - `NetPeer` 支持真正的多逻辑通道

3. 判断是否需要分片

## 8.2 分片逻辑

如果：

- 数据长度 + 报头 > 当前 MTU
- 且这是可靠有序或可靠无序包

则会进入分片发送。

每片会写入：

- `FragmentId`
- `FragmentPart`
- `FragmentsTotal`
- `MarkFragmented()`

分片之后并不是立刻发出，而是仍然交给可靠 Channel 管理。

### 不能分片的类型

以下超 MTU 会直接抛异常：

- `Unreliable`
- `Sequenced`
- `ReliableSequenced`

## 9. 发送时的数据聚合

LiteNetLib 有两层“合并”。

## 9.1 Peer 级合并：`Merged`

`LiteNetPeer.SendUserData(...)` 会尝试把多个小包塞进 `_mergeData`：

- 来自不同 Channel 的包都可能被合并
- 每个子包前面写 2 字节长度
- 如果只合并了 1 个包，则直接按原包发送
- 如果合并了多个，则作为 `PacketProperty.Merged` 发送

这层的目的是：

- 少发 UDP 包
- 减少头部开销

## 9.2 ReliableChannel 内部合并：`ReliableMerged`

`ReliableChannel.GetNextOutgoingPacket()` 会先把多个“小的可靠子包”合成一个 `ReliableMerged`。

这层只在可靠 Channel 内部发生，目的是：

- 在进入 Peer 级发送前，先减少可靠子包数量
- 同时保留每个子包自己的 UserData 送达语义

所以你可以把两层理解为：

- `ReliableMerged`：可靠通道内的小包打包
- `Merged`：Peer 级别跨类型的小包打包

## 10. `SendRaw(...)` 之后发生什么

`LiteNetManager.SendRaw(...)` 负责：

- 应用 `_extraPacketLayer`
- 可选模拟丢包/延迟
- 选 IPv4/IPv6 Socket
- 最终调用 `SendRawCore(...)`

`SendRawCore(...)` 才真正：

- `socket.SendTo(...)`
- 或 NativeSocket 的 `SendTo(...)`
- 统计发送字节和包数

这层并不知道可靠性逻辑，只负责“把已经组好的 UDP 数据报发出去”。

## 11. `LiteNetPeer.Update(...)` 的职责

每个 Peer 每个逻辑周期都会调用 `Update(deltaTime)`。

它负责：

- 连接超时检测
- Outgoing 连接重试
- Shutdown 请求重发
- Ping 定时发送
- RTT 与 RTO 更新
- MTU 探测
- 驱动各个 Channel 发包
- 驱动不可靠队列发包
- 最后把 `_mergeData` 冲刷发送

也就是说，发送不是“业务一调用就立刻全发出去”，而是：

- 业务把包放进队列
- `Update()` 在合适的时机统一推进

## 12. Channel 设计

## 12.1 `BaseChannel`

是所有 Channel 的基类。

公共职责：

- `OutgoingQueue`
- `AddToQueue(...)`
- 防止重复加入 Peer 的发送队列
- `SendAndCheckQueue()`

它本身不实现具体可靠性，只定义协议：

- `SendNextPackets()`
- `ProcessPacket(...)`

## 12.2 `ReliableChannel`

用于：

- `ReliableOrdered`
- `ReliableUnordered`

核心数据结构：

- `_pendingPackets`
  - 已发送但未确认的窗口
- `_outgoingAcks`
  - ACK 位图包
- `_receivedPackets`
  - 有序模式下的乱序缓存
- `_earlyReceived`
  - 无序模式下的“已提前收到”标记

### 发送侧逻辑

1. 从 `OutgoingQueue` 取包
2. 分配可靠序号 `Sequence`
3. 放进 `_pendingPackets`
4. 如果未发送过或已超 RTO，则 `TrySend(...)`
5. 收到 ACK 后清理对应 pending packet

### ACK 机制

ACK 不是“确认一个包”，而是：

- `Sequence` 记录接收窗口起点
- 后面跟一段位图
- 每一位代表窗口内某个序号是否已收到

这使得一份 ACK 就能确认一个窗口中的多包。

### 接收侧逻辑

收到可靠包后：

- 判断是否在接收窗口内
- 在 ACK 位图中标记已收到
- 对重复包再次触发 ACK 发送
- 对 ordered 模式：
  - 只有命中 `_remoteSequence` 的包立刻上交
  - 更大的序号先缓存在 `_receivedPackets`
- 对 unordered 模式：
  - 可以提前处理
  - 仅用 `_earlyReceived` 记录前面哪些位置已被跨过

## 12.3 `SequencedChannel`

用于：

- `Sequenced`
- `ReliableSequenced`

它的语义是“只关心最新包”。

### `Sequenced`

- 新包序号比旧的大才处理
- 中间丢的包直接算损失
- 旧包、重复包直接丢弃

### `ReliableSequenced`

- 仍然只保留“最后一个有效包”
- 但最后一个包会等待 ACK
- 如果 ACK 超时没来，会重发 `_lastPacket`

它适合那种：

- 不需要历史包
- 但最后一个最新状态必须送到

例如某些状态覆盖型同步。

## 12.4 `NetPeer` 的多通道实现

`NetPeer` 通过：

- `_channels = new BaseChannel[ChannelsCount * NetConstants.ChannelTypeCount]`

把“逻辑通道号”和“DeliveryMethod”组合成真实 Channel 索引。

索引方式是：

- `channelIndex = channelNumber * 4 + deliveryMethod`

其中 4 对应：

- `ReliableUnordered`
- `Sequenced`
- `ReliableOrdered`
- `ReliableSequenced`

因此：

- `channelNumber` 是业务逻辑通道
- `deliveryMethod` 是通道内的投递语义

这正是你现在 `ChannelsCount = 2` 时的真实行为基础。

## 13. 收到普通连接包后的链路

当 `HandleMessageReceived(...)` 判定这是某个已连接 Peer 的普通包后，会进入：

1. `LiteNetPeer.ProcessPacket(packet)`
2. 根据 `PacketProperty`：
   - `Merged`
   - `Ping`
   - `Pong`
   - `Ack`
   - `Channeled`
   - `ReliableMerged`
   - `Unreliable`
   - `MtuCheck`
   - `MtuOk`
3. 进入 `ProcessChanneled(...)` 或直接创建接收事件

## 13.1 `Merged`

如果是 `Merged`：

- 从包体里逐个拆出子包
- 每个子包重新构造成 `NetPacket`
- 再递归走 `ProcessPacket(...)`

这就是发送侧 Peer 级合并在接收侧的还原点。

## 13.2 `Unreliable`

最简单：

- 不走 Channel
- 直接 `CreateReceiveEvent(...)`
- header size 是 `NetConstants.HeaderSize`

## 13.3 `Channeled` / `ReliableMerged` / `Ack`

都会交给 `ProcessChanneled(...)`：

- 基础 `LiteNetPeer` 按单通道实现分发
- `NetPeer` 则按多通道数组分发

## 14. 分片重组链路

分片包最终会在 `AddReliablePacket(...)` 中重组。

流程：

1. 检查 `FragmentId`
2. 在 `_holdedFragments` 中找到或创建分片缓存
3. 校验：
   - 分片序号越界
   - 重复分片
   - Channel 不一致
4. 缓存当前分片
5. 当所有分片到齐：
   - 分配一个新的大 `NetPacket`
   - 依次拷贝每个分片的数据区
   - 回收原碎片包
   - 触发 `CreateReceiveEvent(...)`

注意：

- 重组后的大包直接进事件层
- 不会再次回到 Channel 层做排序
- 因为分片本身已经在可靠通道约束下传输了

## 15. 事件系统

## 15.1 `NetEvent`

`NetEvent` 是 Manager 层的统一事件对象。

类型包括：

- `Connect`
- `Disconnect`
- `Receive`
- `ReceiveUnconnected`
- `Error`
- `ConnectionLatencyUpdated`
- `Broadcast`
- `ConnectionRequest`
- `MessageDelivered`
- `PeerAddressChanged`

## 15.2 `CreateEvent(...)`

对于连接、断线、错误、连接请求等事件，会统一进入 `CreateEvent(...)`。

如果：

- `UnsyncedEvents`
- 或 `_manualMode`

则立即回调监听器；否则进入 `_pendingEventHead/_pendingEventTail` 链表，等待 `PollEvents()`。

## 15.3 `CreateReceiveEvent(...)`

接收事件有独立入口 `CreateReceiveEvent(...)`，和普通事件类似，但额外带上：

- `DeliveryMethod`
- `ChannelNumber`
- `NetPacketReader`

所以业务层 `OnNetworkReceive(...)` 收到的 reader，本质上是 `NetEvent` 持有的 `NetPacketReader`。

## 15.4 `NetPacketReader`

`NetPacketReader` 的关键作用是：

- 指向 `NetPacket.RawData`
- 记录头部偏移
- 在回收时把底层 `NetPacket` 放回池
- 再把 `NetEvent` 放回事件池

如果开启 `AutoRecycle`：

- 回调结束就自动回收

如果关闭：

- 需要业务代码自己 `reader.Recycle()`

## 16. 断线链路

## 16.1 主动断开

`LiteNetPeer.Disconnect()` 最终会走到：

- `LiteNetManager.DisconnectPeer(...)`
- `LiteNetPeer.Shutdown(...)`

`Shutdown(...)` 会：

- 构造 `Disconnect` 包
- 写入 `ConnectTime`
- 写入 `ConnectionNumber`
- 可附加断线数据
- 状态切为 `ShutdownRequested`
- 发送断线包

之后如果对方回应 `ShutdownOk`，状态才真正进入 `Disconnected`。

## 16.2 被动断开

收到 `Disconnect` 包后：

- `ProcessDisconnect(...)` 校验 `ConnectTime` 和 `ConnectionNumber`
- Manager 侧触发断线事件
- 然后回发 `ShutdownOk`

## 16.3 超时断开

`Peer.Update(...)` 会检查：

- `TimeSinceLastPacket > DisconnectTimeout`

成立后由 Manager 强制踢掉。

## 17. Ping / RTT / 重传超时

每个 Peer 周期性发送 `Ping`：

- `PingInterval` 默认 1000ms

收到 `Pong` 后：

- 计算 RTT
- 更新 `RemoteTimeDelta`
- 触发 `OnNetworkLatencyUpdate`

ReliableChannel 的重传间隔来自：

- `ResendDelay = ResendFixedDelay + avgRtt * ResendRttMultiplier`

所以这是一个基于 RTT 的动态 RTO，而不是固定值。

## 18. MTU 探测

如果启用 `MtuDiscovery`：

- Peer 会从较小 MTU 开始
- 周期性发送 `MtuCheck`
- 对端收到后改成 `MtuOk` 回发
- 本端确认成功后提升到更大的 MTU 档位

相关逻辑在：

- `UpdateMtuLogic(...)`
- `ProcessMtuPacket(...)`

这也是为什么库里很多“是否需要分片”的判断都依赖当前 `Peer.Mtu`，而不是固定常量。

## 19. Peer 集合维护

`LiteNetManager.HashSet.cs` 里自实现了一套轻量 HashSet，用于按远端地址查 Peer。

它同时维护：

- `_headPeer`
  - 双向链表头，用于遍历
- `_buckets/_slots`
  - 哈希查找结构，用于按 EndPoint 查 Peer
- `_peersArray`
  - 按 `Peer.Id` 直接索引

所以 Peer 查找有三种视角：

- 按链表遍历
- 按 EndPoint 哈希查找
- 按 PeerId 数组直取

## 20. 对你当前服务端最有价值的结论

### 20.1 业务层真正需要理解的是 `NetManager` / `NetPeer`

因为你现在用的是支持多逻辑通道的分叉版本，不应只按原版 `LiteNetPeer` 理解。

### 20.2 发送并不是“立刻发”

业务层 `Send(...)` 只是把数据放进：

- 不可靠暂存数组
- 或 Channel 的队列

真正发送发生在：

- `Peer.Update()`
- `Channel.SendNextPackets()`
- `SendUserData()`
- `SendRaw()`

### 20.3 接收真正进业务层前会经过 3 次分发

1. `HandleMessageReceived(...)`
2. `LiteNetPeer.ProcessPacket(...)`
3. `Channel.ProcessPacket(...)`

最后才进入 `CreateReceiveEvent(...)`。

### 20.4 可靠包的核心不是“每包一个 ACK”

而是：

- 固定窗口
- 位图 ACK
- RTO 重传
- ordered / unordered 两种接收侧缓存策略

### 20.5 `ReliableSequenced` 和 `Sequenced` 适合同步型状态

如果你的战斗同步里有：

- “最新状态覆盖旧状态”的消息
- 不必逐条回放历史状态

这两种语义会很有用。

### 20.6 分片只支持可靠包

这点对你后续做战斗快照很重要：

- 大型快照如果要跨 MTU，只能走可靠通道
- 高频位置同步如果想走不可靠，就必须自己把单帧包体控制在单包大小内

## 21. 对你服务端设计的直接启发

### TCP 常驻业务

这一块和 LiteNetLib 无关，仍然建议用你自己的 TCP。

### UDP 战斗同步

你在服务端接入 LiteNetLib 时，可以按消息类型分层：

- `Unreliable`
  - 高频位置、朝向、轻量状态
- `Sequenced`
  - 只关心最新值的状态同步
- `ReliableOrdered`
  - 战斗关键事件、战局开始/结束、技能确认
- `ReliableUnordered`
  - 需要可靠但不严格依赖顺序的事件

如果你准备使用多个逻辑通道，建议至少划分：

- `channel 0`
  - 核心战斗状态
- `channel 1`
  - 非关键扩展状态或大包

这样可以避免一个可靠大包把所有可靠消息都堵住。

## 22. 本次阅读的最终总结

LiteNetLib 的核心不是“一个 UDP Socket + 一个 Peer 列表”，而是一套完整的：

- 连接管理
- 握手与重连
- 多种投递语义
- 窗口 ACK
- 动态重传
- 分片与重组
- 合包与事件派发

在你的仓库里，这套基础又被 `NetManager` / `NetPeer` 扩成了真正可用的“多逻辑通道 UDP 传输层”。后续你如果要在服务端做战斗同步，最应该直接利用的不是 LiteNetLib 的表层 API，而是它已经为你准备好的三块能力：

- `DeliveryMethod`
- 多逻辑 `channel`
- 可靠窗口与分片机制

