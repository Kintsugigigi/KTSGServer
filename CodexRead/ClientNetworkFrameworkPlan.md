# TestField 客户端网络框架详细计划

本文目标：

- 基于当前 `TestField` 客户端框架，规划一套可落地的 `NetworkSystem`
- 对齐当前 `KTSGServer` 的世界层 TCP 与 PVP 战斗层 UDP 架构
- 明确客户端是否可以复用服务端现有的消息协议解析方案
- 给出一份分步可执行的实施需求，后续可直接按阶段开发

## 1. 当前服务端前提

当前服务端已经基本确定为两层网络：

- 世界层 TCP
  - 登录后长期保持
  - 负责非战斗事务
  - 包括登录、拉角色数据、背包、商店、装备切换、房间准备等
- 战斗层 UDP
  - 只在 PVP 战斗期间开启
  - 由 TCP 下发的 `battleToken` 驱动建立
  - 负责输入、移动、动作、战斗状态、快照同步

因此客户端不能设计成“TCP 和 UDP 二选一”，而应该设计成：

- 平时只开 TCP
- 进入 PVP 战斗时变成 `TCP + UDP`
- 战斗结束后只关闭 UDP，TCP 继续保留

## 2. 客户端网络层应放在哪里

结论：

- 网络主系统应该放在 `System`
- 协议读写辅助和少量纯工具函数可以放在 `Utility`

推荐目录：

- `D:\Unity Project\TestField\Assets\FrameWork\System\NetworkSystem`

原因：

- 网络层是有生命周期的
- 网络层要和 `ProcedureSystem`、UI、玩家控制、房间流程协作
- 网络层要维护连接状态、登录状态、房间状态、战斗状态
- 网络层不是单纯的静态工具集合

不建议把整个网络都放到 `Utility`，否则会出现：

- 登录态和在线态分散
- TCP/UDP 启停逻辑散落各业务脚本
- 战斗切换、断线、回退流程难以集中管理

## 3. 推荐目录结构

建议新增这些目录和脚本：

- `Assets/FrameWork/System/NetworkSystem/NetworkSystem.cs`
- `Assets/FrameWork/System/NetworkSystem/Model/NetworkStateModel.cs`
- `Assets/FrameWork/System/NetworkSystem/Model/BattleAccessInfo.cs`
- `Assets/FrameWork/System/NetworkSystem/Model/WorldPlayerDataModel.cs`
- `Assets/FrameWork/System/NetworkSystem/Client/WorldTcpClient.cs`
- `Assets/FrameWork/System/NetworkSystem/Client/BattleUdpClient.cs`
- `Assets/FrameWork/System/NetworkSystem/Dispatcher/ClientMsgDispatcher.cs`
- `Assets/FrameWork/System/NetworkSystem/Handler/Tcp/`
- `Assets/FrameWork/System/NetworkSystem/Handler/Udp/`
- `Assets/FrameWork/Utility/NetworkUtility/`

其中：

- `System/NetworkSystem`
  - 放网络生命周期、连接管理、消息分发、状态机
- `Utility/NetworkUtility`
  - 放协议工具、小型序列化辅助、错误码转文本等纯辅助逻辑

## 4. 核心对象职责

### 4.1 NetworkSystem

`NetworkSystem` 是客户端总入口。

职责：

- 初始化 TCP 客户端
- 根据流程决定是否初始化 UDP 客户端
- 对外提供登录、进入房间、准备、开始战斗、退出战斗等接口
- 每帧驱动 TCP/UDP 更新
- 统一保存并切换当前网络阶段
- 统一把底层消息转换成上层系统可消费的事件

不负责：

- 商店本身的业务规则
- 装备数值计算
- 战斗输入判定和动作逻辑
- UI 细节

### 4.2 WorldTcpClient

职责：

- 连接世界层 TCP 服务端
- 发送世界层请求
- 接收世界层响应和通知

负责消息范围：

- `ReqLogin / RspLogin`
- 玩家数据拉取
- 背包、商店、装备切换
- 匹配、创建房间、加入房间、Ready
- 战斗开始前的 `battleId / udpHost / udpPort / battleToken`

### 4.3 BattleUdpClient

职责：

- 根据 TCP 下发的 token 建立 UDP
- 发送战斗输入和高频状态
- 接收权威快照和战斗同步
- 战斗结束后断开

负责消息范围：

- 输入
- 移动
- 朝向
- `PlayAction`
- 战斗快照
- 战斗内权威状态同步

### 4.4 NetworkStateModel

建议单独维护一个状态模型，不要把所有字段散在 `NetworkSystem` 成员里。

建议至少包含：

- `CurrentPhase`
- `TcpConnected`
- `UdpConnected`
- `LoggedIn`
- `CurrentPlayerId`
- `CurrentSessionKey`
- `CurrentRoomId`
- `CurrentBattleId`
- `CurrentBattleToken`
- `CurrentTcpHost`
- `CurrentTcpPort`
- `CurrentUdpHost`
- `CurrentUdpPort`

### 4.5 ClientMsgDispatcher

职责：

- 统一注册 TCP/UDP 消息处理器
- 底层收到消息后按 `cmdId` 派发
- 把网络消息和业务脚本解耦

不建议：

- 让 UI、Procedure、玩家控制脚本直接和底层 socket 对接

## 5. 推荐网络状态机

建议明确为这些状态：

1. `Offline`
2. `TcpConnecting`
3. `LoginPending`
4. `WorldOnline`
5. `RoomPreparing`
6. `BattlePreparing`
7. `BattleUdpOnline`
8. `BattleLeaving`

状态含义：

- `Offline`
  - 没有连接
- `TcpConnecting`
  - 正在建立 TCP
- `LoginPending`
  - TCP 已连通，等待登录结果
- `WorldOnline`
  - 已登录，只有 TCP
- `RoomPreparing`
  - 已进入房间准备流程，仍然只有 TCP
- `BattlePreparing`
  - 已拿到战斗接入信息，准备启动 UDP
- `BattleUdpOnline`
  - 战斗中，TCP 与 UDP 并存
- `BattleLeaving`
  - 正在关闭 UDP，回退到世界层

## 6. 生命周期流程

### 6.1 游戏启动到登录成功

推荐流程：

1. `NetworkSystem` 初始化
2. 创建 `WorldTcpClient`
3. 连接 TCP
4. 切换到 `TcpConnecting`
5. 发送 `ReqLogin`
6. 切换到 `LoginPending`
7. 收到 `RspLogin`
8. 保存 `PlayerId` 与 `SessionKey`
9. 进入 `WorldOnline`

这一阶段只有 TCP。

### 6.2 世界层在线阶段

推荐行为：

- 玩家进入游戏世界
- 客户端本地移动和普通探索不做服务端同步
- 只有涉及持久化的数据变更才发给服务端

例如：

- 购买
- 领取奖励
- 背包变更
- 装备切换
- 武器切换
- 匹配申请

这就是你当前定义的“弱连接世界层”。

### 6.3 进入房间准备阶段

推荐流程：

1. 客户端通过 TCP 发起匹配或加入房间
2. 服务端通过 TCP 返回房间状态
3. 客户端保存 `RoomId` 和成员信息
4. 网络状态进入 `RoomPreparing`

这一阶段仍然不启用 UDP。

### 6.4 从房间进入战斗

推荐流程：

1. 服务端确认房间满足开战条件
2. 服务端通过 TCP 下发：
   - `battleId`
   - `udpHost`
   - `udpPort`
   - `battleToken`
3. 客户端保存这些参数到 `BattleAccessInfo`
4. 网络状态进入 `BattlePreparing`
5. `NetworkSystem` 调用 `BattleUdpClient.ConnectWithToken(...)`
6. UDP 连接成功
7. 状态切换到 `BattleUdpOnline`

关键原则：

- UDP 连接参数只能由 TCP 下发
- UDP 不自行决定连哪个地址、哪个房间

### 6.5 战斗结束

推荐流程：

1. 服务端通知战斗结束，或者本地收到战斗退场条件
2. 客户端停止发送战斗输入
3. `BattleUdpClient.Disconnect()`
4. 清空战斗态字段
5. 切回 `WorldOnline`

TCP 在整个过程中保持连接。

## 7. 客户端是否可以复用服务端当前 Msg 解析方案

结论：

- 可以
- 而且建议尽量复用

当前服务端公共层已经具备一套比较统一的消息收发结构：

- `D:\KTSGServer\KTSGServerCommon\TCP\TCPProtocol.cs`
- `D:\KTSGServer\KTSGServerCommon\LiteLibExtension\UDPProtocol.cs`
- `D:\KTSGServer\KTSGServerCommon\MsgProcessor\NetMsgProcessor.cs`
- `D:\KTSGServer\KTSGServerCommon\Client\TCPClientManager.cs`
- `D:\KTSGServer\KTSGServerCommon\Client\UDPClientManager.cs`

如果客户端直接沿用这一套，优点是：

- TCP 和 UDP 包格式一致
- `cmdId + seq + protobuf payload` 这一套不会分裂
- 客户端和服务端的 Handler 组织方式接近
- 以后排查协议错位、粘包、长度头错误会轻松很多
- 生成的 proto 类型可以直接共用

### 7.1 推荐复用的部分

建议直接或基本保持一致的部分：

- 包头格式
- `cmdId`
- `seq`
- protobuf 消息体
- TCP 拆包/封包逻辑
- UDP 协议打包逻辑
- `NetMsgProcessor` 的处理思路

### 7.2 推荐只“思路一致”而不是强行共用源码的部分

以下部分建议保持风格一致，但客户端可写自己的壳：

- `NetworkSystem`
- 消息事件桥接
- Unity 主线程回调派发
- 网络状态模型
- 连接生命周期管理

原因是：

- 服务端运行环境和 Unity 生命周期不同
- 客户端需要和 `ProcedureSystem`、UI、输入系统协作
- 客户端通常还要处理切后台、场景切换、主线程对象访问

所以更合适的方式是：

- 协议格式尽量统一
- 网络收发底层可以尽量复用
- Unity 上层包装自己写

### 7.3 是否连命名和目录也要完全一样

不建议完全镜像服务端目录。

建议：

- 协议层命名尽量一致
- 客户端业务组织按照 Unity 侧生命周期写

也就是说：

- 包格式、`Handler` 风格、`cmdId` 风格可以像服务端
- 但客户端不需要照搬服务端 `Services/Runtime` 的目录形态

## 8. 推荐的消息处理管线

客户端推荐管线：

1. 底层 socket 收到字节流
2. `TCPProtocol / UDPProtocol` 解包
3. 解出 `cmdId + seq + payload`
4. `ClientMsgDispatcher` 找到对应 handler
5. handler 把消息转换为客户端事件或更新 `NetworkStateModel`
6. `ProcedureSystem / UISystem / PlayerCtrlSystem` 消费这些事件

不要让流程变成：

- UI 直接读 socket
- 角色控制直接解析 protobuf
- 房间逻辑直接写网络字节处理

## 9. 与现有客户端系统的协作建议

结合当前 `Assets\FrameWork\System` 已有目录，推荐协作关系如下：

- `ProcedureSystem`
  - 决定当前阶段是登录、世界层、房间还是战斗
- `UISystem`
  - 负责登录界面、匹配界面、房间界面、网络错误提示
- `PlayerCtrlSystem`
  - 战斗中从输入或实体控制层取出战斗输入，再交给 `NetworkSystem`
- `EntitySystem`
  - 消费战斗快照，更新远端玩家表现
- `InputSystem`
  - 只产生输入，不直接碰网络

推荐原则：

- 上层系统不直接管理 socket
- 所有网络动作统一通过 `NetworkSystem`

## 10. 分步可执行需求

下面给出一份从易到难、可阶段验收的实施计划。

### 阶段 1. 建立 NetworkSystem 骨架

目标：

- 客户端有统一网络入口

需求：

- 新建 `NetworkSystem`
- 新建 `NetworkStateModel`
- 新建 `WorldTcpClient`
- 定义网络阶段枚举
- `ProcedureSystem` 可以拿到 `NetworkSystem`

完成标准：

- 游戏启动后 `NetworkSystem` 正常初始化
- 可以看到状态从 `Offline` 切到 `TcpConnecting`

### 阶段 2. 打通最小 TCP 登录

目标：

- 能够连接服务端并完成登录

需求：

- TCP 建连
- 发送 `ReqLogin`
- 接收 `RspLogin`
- 保存 `PlayerId`
- 保存 `SessionKey`
- 登录成功后进入 `WorldOnline`

完成标准：

- demo 账号可登录
- 登录成功后客户端进入世界层在线态

### 阶段 3. 打通世界层最小玩家数据拉取

目标：

- 登录后能获取最小玩家资料

需求：

- 设计 `ReqGetPlayerData / RspGetPlayerData`
- 客户端维护一个最小 `WorldPlayerDataModel`
- 登录成功后自动拉取玩家数据

完成标准：

- 登录后客户端能拿到基础角色数据

### 阶段 4. 完成世界层事务型消息入口

目标：

- 让 TCP 世界层真正可承载非战斗业务

需求：

- 装备切换请求入口
- 武器切换请求入口
- 背包、商店等事务预留统一发送接口
- 网络错误与失败结果回传 UI

完成标准：

- 非战斗事务消息都从 `NetworkSystem -> WorldTcpClient` 发出

### 阶段 5. 建立房间准备流程

目标：

- 支持世界层到 PVP 房间层的过渡

需求：

- TCP 匹配/创建房间/加入房间
- 房间成员同步
- Ready 同步
- 客户端本地保存 `RoomId`

完成标准：

- 客户端可以仅通过 TCP 看到完整房间准备过程

### 阶段 6. 设计并接入 BattleAccessInfo

目标：

- 为战斗层 UDP 接入做准备

需求：

- 定义 `BattleAccessInfo`
- TCP 接收 `battleId / udpHost / udpPort / battleToken`
- 状态从 `RoomPreparing` 切到 `BattlePreparing`

完成标准：

- 客户端收到开战信息后，能完整保存 UDP 接入参数

### 阶段 7. 拉起 BattleUdpClient

目标：

- 客户端根据 TCP 下发 token 建立 UDP

需求：

- 新建 `BattleUdpClient`
- 实现 `ConnectWithToken(...)`
- 实现断开和清理逻辑
- UDP 连通后切换到 `BattleUdpOnline`

完成标准：

- 客户端能按房间 token 连上正确的战斗 UDP

### 阶段 8. 接入最小战斗同步

目标：

- 让 UDP 真正承载战斗消息

需求：

- 发送输入
- 发送移动
- 发送 `PlayAction`
- 接收最小快照
- 更新远端实体表现

完成标准：

- PVP 中可通过 UDP 完成最小移动与动作同步

### 阶段 9. 完成战斗退出与回退

目标：

- 战斗结束后能安全回到世界层

需求：

- 停止发送战斗消息
- 断开 UDP
- 清理 `BattleAccessInfo`
- 状态回退到 `WorldOnline`

完成标准：

- 战斗结束后 TCP 仍保持在线
- 世界层事务消息仍可继续使用

## 11. 当前最推荐的优先级

结合你现在的服务端进度，最推荐的顺序是：

1. 阶段 1
2. 阶段 2
3. 阶段 3
4. 阶段 5
5. 阶段 6
6. 阶段 7

不要一开始就先做复杂 UDP 战斗细节，否则客户端会先失去主链路。

## 12. 一句话结论

客户端网络框架应该理解为：

- `NetworkSystem` 是总控
- `WorldTcpClient` 负责登录后长期在线的世界层事务
- `BattleUdpClient` 负责 PVP 战斗期的高频同步
- UDP 必须由 TCP 下发的 `battleToken` 来启动
- 底层 Msg 协议格式和解析流程可以尽量复用服务端当前这套实现
