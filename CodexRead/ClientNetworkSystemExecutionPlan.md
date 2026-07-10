# 客户端 NetworkSystem 详细框架计划

本文目标：

- 规划客户端网络系统在当前 `TestField` 架构中的落点
- 明确世界层 TCP 与 PVP 战斗层 UDP 的职责分离
- 说明客户端是否可以直接复用服务端当前的消息协议解析格式
- 给出一份可逐阶段执行的需求计划

## 1. 总目标

客户端网络系统最终应支持两条并存链路：

- 世界层 TCP
  - 登录后长期在线
  - 负责非战斗事务
- 战斗层 UDP
  - 只在 PVP 战斗期间启用
  - 由 TCP 下发的 `roomToken / battleToken` 驱动建立

这意味着客户端网络架构不是“TCP 切换成 UDP”，而是：

- 平时只有 TCP
- 战斗时变成 `TCP + UDP`
- 战斗结束后关闭 UDP，TCP 保持

## 2. 网络层应该放在 System 还是 Utility

结论：

- 网络主体放 `System`
- 协议辅助和纯函数工具放 `Utility`

推荐路径：

- `D:\Unity Project\TestField\Assets\FrameWork\System\NetworkSystem`

原因：

- 网络不是纯工具
- 网络有生命周期
- 网络要管理在线状态
- 网络要和 `ProcedureSystem`、UI、玩家控制、匹配流程强耦合

如果整个放 Utility，会导致：

- 登录态难管理
- TCP/UDP 切换分散
- 战斗开始/结束流程难收口
- 重连和退房逻辑难维护

所以推荐：

- `NetworkSystem` 作为总控系统
- `NetworkUtility` 只放包头/错误码/小工具

## 3. 推荐模块结构

推荐目录结构：

- `Assets/FrameWork/System/NetworkSystem/NetworkSystem.cs`
- `Assets/FrameWork/System/NetworkSystem/WorldTcpClient.cs`
- `Assets/FrameWork/System/NetworkSystem/BattleUdpClient.cs`
- `Assets/FrameWork/System/NetworkSystem/NetworkStateModel.cs`
- `Assets/FrameWork/System/NetworkSystem/NetworkEvent.cs`
- `Assets/FrameWork/System/NetworkSystem/Dispatcher/`
- `Assets/FrameWork/System/NetworkSystem/Handlers/Tcp/`
- `Assets/FrameWork/System/NetworkSystem/Handlers/Udp/`
- `Assets/FrameWork/Utility/NetworkUtility/`

## 4. 核心对象职责

## 4.1 NetworkSystem

`NetworkSystem` 是客户端网络总入口。

职责：

- 管理 TCP 客户端
- 管理 UDP 客户端
- 维护当前网络状态机
- 对外暴露登录、进房、准备、开战、退战等接口
- 统一驱动网络轮询

不负责：

- 真正的商店业务逻辑
- 真正的战斗输入计算
- UI 细节

它只负责“网络生命周期”和“网络消息到上层事件”的转换。

## 4.2 WorldTcpClient

职责：

- 建立 TCP 长连接
- 发送登录请求
- 发送世界层所有事务请求
- 接收登录响应、玩家数据、房间与战斗准备消息

世界层 TCP 负责的内容：

- 登录
- 获取玩家数据
- 背包
- 商店
- 装备/武器切换
- 匹配
- 创建房间
- 加入房间
- Ready
- 接收 UDP token

## 4.3 BattleUdpClient

职责：

- 按 TCP 下发的 token 建立 UDP
- 发送战斗输入与同步消息
- 接收战斗快照和权威同步数据
- 战斗结束后断开

战斗层 UDP 负责的内容：

- 输入
- 位置/朝向
- `PlayAction`
- 战斗状态同步
- 快照

## 4.4 NetworkStateModel

建议单独做一个状态模型，不要把所有状态散落在 `NetworkSystem` 里。

建议记录：

- `TcpConnected`
- `UdpConnected`
- `LoggedIn`
- `CurrentPlayerId`
- `CurrentSessionKey`
- `CurrentRoomId`
- `CurrentBattleId`
- `CurrentBattleToken`
- `CurrentServerIp`
- `CurrentTcpPort`
- `CurrentUdpPort`
- `CurrentNetworkPhase`

## 5. 推荐状态机

建议客户端网络阶段定义为：

1. `Offline`
2. `TcpConnecting`
3. `LoginPending`
4. `WorldOnline`
5. `RoomPreparing`
6. `BattlePreparing`
7. `BattleUdpOnline`
8. `BattleLeaving`

状态说明：

- `WorldOnline`
  - 只有 TCP
- `RoomPreparing`
  - 还是只有 TCP
- `BattlePreparing`
  - 已拿到 token，准备拉起 UDP
- `BattleUdpOnline`
  - TCP 与 UDP 并存
- `BattleLeaving`
  - 关闭 UDP，回到 `WorldOnline`

## 6. 连接与战斗切换流程

## 6.1 进入游戏时 TCP

流程建议：

1. 游戏启动
2. `NetworkSystem` 创建并初始化 `WorldTcpClient`
3. TCP 连接服务端
4. 发送 `ReqLogin`
5. 登录成功后保存：
   - `PlayerId`
   - `SessionKey`
6. 状态切换到 `WorldOnline`

此时客户端只使用 TCP。

## 6.2 进入 PVP 房间

流程建议：

1. 世界层通过 TCP 发起匹配或加入房间
2. 服务端通过 TCP 通知：
   - 当前房间信息
   - 成员变化
   - 准备状态
3. 客户端状态进入 `RoomPreparing`

此时仍然不启用 UDP。

## 6.3 从房间进入战斗

TCP 应该负责把进入战斗的前置参数送到客户端。

建议服务端通过 TCP 下发：

- `battleId`
- `udpHost`
- `udpPort`
- `battleToken`

客户端流程：

1. `NetworkSystem` 收到战斗准备消息
2. 记录 token 和目标地址
3. 状态切换到 `BattlePreparing`
4. 调用 `BattleUdpClient.ConnectWithToken(...)`
5. UDP 建连成功
6. 状态切换到 `BattleUdpOnline`

关键原则：

- UDP 从不自己决定连接目标
- UDP 连接信息全部来自 TCP

## 6.4 战斗结束

流程建议：

1. 服务端通过 TCP 或内部结束逻辑通知战斗结束
2. 客户端停止发送战斗输入
3. `BattleUdpClient.Disconnect()`
4. 清空 battle 相关 token 与状态
5. 回到 `WorldOnline`

TCP 在整个过程中保持连接。

## 7. 是否可以直接复用服务端那套消息解析

结论：

- 可以，而且建议尽量统一

当前服务端这几个基础件已经适合客户端沿用：

- `D:\KTSGServer\KTSGServerCommon\TCP\TCPProtocol.cs`
- `D:\KTSGServer\KTSGServerCommon\LiteLibExtension\UDPProtocol.cs`
- `D:\KTSGServer\KTSGServerCommon\MsgProcessor\NetMsgProcessor.cs`
- `D:\KTSGServer\KTSGServerCommon\Client\TCPClientManager.cs`
- `D:\KTSGServer\KTSGServerCommon\Client\UDPClientManager.cs`

统一的好处是：

- TCP/UDP 包格式一致
- `MagicNum` 一致
- `CmdId / Seq` 一致
- proto 类型与 `MsgMeta` 一致
- 双端联调成本更低

### 7.1 TCP 格式

当前 TCP 头：

- Length(4)
- Magic(2)
- CmdId(4)
- Seq(2)

也就是总头长 12 字节。

### 7.2 UDP 格式

当前 UDP 头：

- Magic(2)
- CmdId(4)
- Seq(2)

也就是总头长 8 字节。

### 7.3 建议复用的范围

客户端建议直接复用：

- TCP/UDP 包头格式
- proto 消息定义
- `MsgMeta`
- `CmdId` 体系
- RPC 的 `seq` 机制

### 7.4 不建议直接照搬的部分

服务端业务 handler 体系不要原封不动放到客户端。

原因：

- 服务端主要处理 `Req -> Rsp`
- 客户端主要处理 `Rsp` 和服务端 `Notify`

所以客户端更适合有自己的：

- `RspHandler<T>`
- `NotifyHandler<T>`

但底层的：

- 包格式
- `CmdId`
- proto
- 解析分发方式

是可以统一的。

## 8. 客户端 NetworkSystem 与当前服务端的对应关系

当前服务端已经形成的边界是：

- 世界层 TCP 常驻
- 房间层只负责准备
- 战斗层才启动 UDP

客户端网络系统应完全对齐这个边界。

推荐映射关系：

- 客户端 `WorldTcpClient`
  - 对应服务端 `TcpServerService + LoginService + 世界层 handler`
- 客户端 `BattleUdpClient`
  - 对应服务端 `UdpSessionModule + BattleService + BattleSession`
- 客户端 `NetworkStateModel`
  - 对应服务端 `PlayerSession + RoomSession + BattleSession` 的外部可见状态

## 9. 分步可执行需求

下面给一份建议的执行计划，每一步都应该是独立可落地的。

## 阶段 1. 建立客户端 NetworkSystem 骨架

目标：

- 有一个统一的 `NetworkSystem`
- 能驱动 TCP 客户端更新
- 有明确网络状态枚举

需求：

- 新建 `NetworkSystem`
- 新建 `NetworkStateModel`
- 新建 `WorldTcpClient`
- `ProcedureSystem` 能访问 `NetworkSystem`

完成标准：

- 游戏启动后 `NetworkSystem` 正常初始化
- 能看到 TCP 状态从 `Offline -> TcpConnecting`

## 阶段 2. 打通最小 TCP 登录

目标：

- 客户端能发 `ReqLogin`
- 能收 `RspLogin`
- 登录成功后保存 `PlayerId / SessionKey`

需求：

- TCP 建连
- 登录请求发送
- 登录回包分发
- 登录态保存

完成标准：

- 能用 demo 账号登录
- 登录成功后状态切到 `WorldOnline`

## 阶段 3. 打通世界层最小数据获取

目标：

- 登录后拉取最小玩家数据

需求：

- `ReqGetPlayerData`
- `RspGetPlayerData`
- 玩家数据缓存到客户端 Model

完成标准：

- 登录成功后可拿到角色基础资料

## 阶段 4. 房间准备流程

目标：

- 客户端能通过 TCP 完成 PVP 房间准备流程

需求：

- 匹配 / 创建房间
- 加入房间
- Ready
- 房间成员和状态同步

完成标准：

- 客户端能在 TCP 层看到房间状态变化
- 此时 UDP 仍未启动

## 阶段 5. 战斗 token 拉起 UDP

目标：

- 客户端根据 TCP 下发的 token 启动 UDP

需求：

- 定义 `BattleAccessInfo`
- TCP 接收：
  - `battleId`
  - `udpHost`
  - `udpPort`
  - `battleToken`
- `BattleUdpClient.ConnectWithToken(...)`

完成标准：

- 进入战斗前客户端能成功建立 UDP
- 状态从 `BattlePreparing -> BattleUdpOnline`

## 阶段 6. 战斗高频消息

目标：

- UDP 能发输入和接快照

需求：

- 发送输入
- 发送移动/动作
- 接收快照
- 接收权威战斗状态

完成标准：

- 战斗内角色能通过 UDP 正常同步

## 阶段 7. 战斗结束回退到世界层

目标：

- 战斗结束后干净地退回世界层

需求：

- 关闭 UDP
- 清空 battle token
- 状态回到 `WorldOnline`

完成标准：

- 战斗结束后 TCP 还在
- 世界层业务可继续使用

## 10. 当前最推荐的实现优先级

如果按你当前进度和服务端现状，建议优先做：

1. 阶段 1
2. 阶段 2
3. 阶段 3
4. 阶段 4
5. 阶段 5

不要一开始就冲 UDP 战斗消息细节，否则客户端会先失去主链路。

## 11. 一句话版结论

客户端网络系统应该这样理解：

- `NetworkSystem` 是总控系统
- `WorldTcpClient` 负责世界层长连接
- `BattleUdpClient` 负责战斗期短生命周期同步
- UDP 的启动权来自 TCP 下发的房间/战斗 token
- 底层消息格式和协议解析建议尽量复用服务端当前这一套
