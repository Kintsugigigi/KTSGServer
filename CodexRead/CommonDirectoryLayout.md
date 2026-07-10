# KTSGServerCommon 目录整理结果

## 目标

`KTSGServerCommon` 现在只保留双端可复用的基础设施，不再放服务端宿主和业务运行时。

当前边界是：

- `KTSGServerCommon`
  - 传输层抽象
  - TCP/UDP 协议与底层实现
  - 消息分发基础件
  - proto 生成产物与生成工具
  - 通用日志与对象池
- `KTSGServerCore`
  - `ServerRuntime`
  - `TcpServerService`
  - `UdpServerService`
  - `ConnectionLifecycleService`
  - `PlayerService / RoomService / BattleService`
  - Mongo / Config / Login / Runtime Session

## 当前目录结构

### Diagnostics

- `Diagnostics/NetLogger.cs`

职责：

- 双端通用日志适配层
- 给 TCP、UDP、消息层提供统一日志出口

### Messaging

- `Messaging/IMsgHandler.cs`
- `Messaging/MsgHandler.cs`
- `Messaging/NetMsgProcessor.cs`
- `Messaging/ReqHandler.cs`
- `Messaging/RPCRequest.cs`
- `Messaging/RspHandler.cs`

职责：

- 消息处理器抽象
- `cmdId -> handler` 分发
- 请求/响应/RPC 处理基类
- 双端共用的消息入口

### Pool

- `Pool/SimplePool.cs`

职责：

- 轻量对象池工具

### Protocol

- `Protocol/Generated/KtsgServer.cs`
- `Protocol/Generated/MsgMeta.cs`
- `Protocol/Handlers/CoinAddHandler.cs`
- `Protocol/Handlers/ReqLoginHandler.cs`
- `Protocol/Handlers/RspLoginHandler.cs`
- `Protocol/Schema/ktsg_server.proto`
- `Protocol/Tools/Proto2CsGen.bat`
- `Protocol/Tools/Proto2CsGen.exe`
- `Protocol/Tools/Proto2CsGen.pdb`

职责：

- 协议 schema
- 生成后的 proto 类型
- 自动生成的 handler 占位或公共 handler
- proto 代码生成工具

说明：

- `Proto2CsGen.bat` 已改成面向当前目录结构输出：
  - `Generated`
  - `Handlers`

### Transport

- `Transport/INetConnection.cs`

职责：

- 双端统一连接抽象

#### TCP

- `Transport/TCP/ITCPEventListener.cs`
- `Transport/TCP/NetBuffer.cs`
- `Transport/TCP/TcpConnectionAdapter.cs`
- `Transport/TCP/TCPNetEvent.cs`
- `Transport/TCP/TCPNetManager.cs`
- `Transport/TCP/TCPNetPacket.cs`
- `Transport/TCP/TCPNetPacketPool.cs`
- `Transport/TCP/TCPNetPacketReader.cs`
- `Transport/TCP/TCPPeer.cs`
- `Transport/TCP/TCPProtocol.cs`

职责：

- 自定义 TCP 底层
- TCP 包格式
- 事件队列与事件池
- peer 生命周期
- `Socket -> TCPNetManager -> TCPPeer -> INetConnection` 这条链

#### Udp

- `Transport/Udp/UdpChannels.cs`
- `Transport/Udp/UdpConnectionAdapter.cs`
- `Transport/Udp/UdpProtocol.cs`

职责：

- UDP 包头格式
- LiteNetLib peer 到 `INetConnection` 的适配
- UDP channel 常量

## 本次整理里完成的移动

### 1. 消息层改名收口

原来：

- `MsgProcessor/*`

现在：

- `Messaging/*`

目的：

- 名字更贴近职责
- 不再把目录名绑死在“处理器”概念上

### 2. proto 目录职责化

原来：

- `protos/*`

现在：

- `Protocol/Generated`
- `Protocol/Handlers`
- `Protocol/Schema`
- `Protocol/Tools`

目的：

- 生成物、schema、工具、handler 分开
- 后面客户端同步时更容易挑选需要的部分

### 3. TCP 适配器改名

原来：

- `TCPConnectionApdater.cs`

现在：

- `TcpConnectionAdapter.cs`

目的：

- 修正拼写
- 名称和职责一致

## 现在的 Common 是否已经“纯”

从代码职责上看，已经基本纯了。

目前 `KTSGServerCommon` 中没有再混入这些服务端专属内容：

- 服务端宿主
- 连接生命周期服务
- 玩家/房间/战斗业务状态
- 登录/Mongo/配置

所以现在它已经可以被视为“共享基础层”。

## 仍然存在的一个小尾巴

文件系统里还残留了两个旧空目录壳：

- `KTSGServerCommon/MsgProcessor`
- `KTSGServerCommon/protos`

其中代码文件已经全部迁走，构建也不再依赖它们。

当前环境对删除目录命令有限制，所以它们可能还暂时留在磁盘上，但这不影响项目结构和编译结果。

## 构建结果

已验证：

- `dotnet build D:\KTSGServer\KTSGSever.sln`

结果：

- `0 warning`
- `0 error`

## 下一步建议

如果要给 Unity 客户端复用，建议优先同步这些目录：

- `Diagnostics`
- `Messaging`
- `Protocol/Generated`
- `Protocol/Schema`
- `Transport`

不需要同步这些到客户端：

- `Protocol/Tools`

客户端如果只消费生成结果，不需要把生成工具也带过去。
