# 客户端网络层放置建议 与 当前服务端登录管线记录

这份文档记录两件事：

- 客户端网络架构应该怎么放进 `D:\Unity Project\TestField\Assets\FrameWork`
- 当前 `KTSGServer` 已实现出来的整体架构，尤其是登录管线

## 1. 客户端网络层应该放在 System 还是 Utility

先说结论：

- 网络层主体应该放在 `System`
- 协议编解码、少量纯工具函数可以放在 `Utility`
- 不建议把整个网络模块放成 `Utility`

原因和你现在的框架边界有关。

### 1.1 你当前框架里的边界

从这几个文件可以看出：

- `D:\Unity Project\TestField\Assets\FrameWork\Architecture\ISystem.cs`
- `D:\Unity Project\TestField\Assets\FrameWork\Architecture\IUtility.cs`

`System` 的特点是：

- 属于架构主业务层
- 可拿 `Model / Utility / System`
- 可注册事件、发命令
- 适合承载长期生命周期对象

`Utility` 更适合：

- 无状态工具
- 资源加载封装
- 配置读写
- Tick 小工具
- 纯算法或纯辅助方法

而网络层显然不是“纯工具”。

网络层通常要负责：

- 建连
- 重连
- 心跳
- 在线状态
- 登录态
- 消息分发
- TCP/UDP 生命周期切换
- 和 `ProcedureSystem` 协作

所以它更像一个运行时系统，而不是工具类。

## 1.2 建议的客户端放置方式

建议在 `Assets/FrameWork/System` 下新增一个独立模块：

- `NetworkSystem`

建议结构：

- `Assets/FrameWork/System/NetworkSystem/NetworkSystem.cs`
- `Assets/FrameWork/System/NetworkSystem/WorldTcpClient.cs`
- `Assets/FrameWork/System/NetworkSystem/BattleUdpClient.cs`
- `Assets/FrameWork/System/NetworkSystem/NetSessionState.cs`
- `Assets/FrameWork/System/NetworkSystem/MsgDispatcher.cs`
- `Assets/FrameWork/System/NetworkSystem/Handlers/*`

同时把纯辅助逻辑放在 `Utility`：

- `Assets/FrameWork/Utility/NetworkUtility/`

建议放进去的东西：

- 包头组装/拆包辅助
- token 字符串处理
- 简单序列号工具
- 网络错误码转文本

## 1.3 客户端网络层的推荐职责拆分

建议客户端网络层拆成两部分。

### A. WorldTcpClient

负责世界层 TCP 常驻连接：

- 登录
- 拉取玩家数据
- 商店、背包、装备切换
- 聊天
- 匹配 / 创建房间 / Ready
- 下发战斗 token

### B. BattleUdpClient

负责战斗期 UDP：

- 进入战斗后用 token 建立 UDP
- 输入同步
- 移动同步
- 动作同步
- 战斗快照接收
- 战斗结束后断开 UDP

### C. NetworkSystem

`NetworkSystem` 作为总控：

- 管理 `WorldTcpClient`
- 管理 `BattleUdpClient`
- 对上层 Procedure/UI 暴露统一接口
- 根据当前阶段切换世界层 / 房间层 / 战斗层状态

## 1.4 为什么不建议整个网络都放 Utility

如果你把整个网络层都放 Utility，会有几个问题：

- 生命周期不清晰
- 登录态和连接态难管理
- TCP/UDP 切换不自然
- 和 `ProcedureSystem`、`PlayerCtrlSystem`、UI 的交互会越来越乱
- 断线重连、战斗进入退出都容易散落在各处

所以：

- `NetworkSystem` 放 `System`
- 纯函数辅助放 `Utility`

这是最稳的。

## 2. 当前服务端整体架构

当前 `KTSGServer` 已经形成了一个比较清晰的分层。

## 2.1 项目分层

### `KTSGServerCommon`

职责：

- 自定义 TCP 实现
- LiteNetLib UDP 封装
- `INetConnection`
- `NetMsgProcessor`
- proto 消息分发
- 公共网络基础设施

它更像：

- 传输层
- 协议层
- 基础消息分发层

### `KTSGServerCore`

职责：

- 运行时宿主
- 在线玩家会话
- 登录服务
- 房间服务
- 战斗服务
- Mongo 配置和数据库入口
- 业务 handler

它更像：

- 业务层
- 生命周期层

## 2.2 当前核心对象

### Program

文件：

- `D:\KTSGServer\KTSGServerCore\Program.cs`

职责：

- 加载 `appconfig.json`
- 初始化 Mongo
- 创建 `ServerRuntime`
- 启动 TCP
- 主循环调用 `runtime.Update()`

### ServerRuntime

文件：

- `D:\KTSGServer\KTSGServerCore\Runtime\ServerRuntime.cs`

职责：

- 持有 `ServerServices`
- 持有 `TcpServerService`
- 持有 `UdpSessionModule`
- 驱动 TCP/UDP 生命周期
- 驱动战斗 Tick
- 负责 UDP token 验证和绑定接线

### ServerServices

文件：

- `D:\KTSGServer\KTSGServerCore\Services\ServerServices.cs`

职责：

- 统一持有业务服务单例

当前包含：

- `PlayerService`
- `LoginService`
- `RoomService`
- `BattleTokenService`
- `BattleService`

### PlayerSession

文件：

- `D:\KTSGServer\KTSGServerCore\Runtime\PlayerSession.cs`

职责：

- 表示一个在线玩家会话
- 绑定 TCP 连接
- 可绑定 UDP 连接
- 保存 `PlayerId`
- 保存当前房间 / 战斗 Id

### DbService

文件：

- `D:\KTSGServer\KTSGServerCore\Data\DbService.cs`

职责：

- 初始化 MongoClient / Database
- 暴露集合入口

### AccountDocument

文件：

- `D:\KTSGServer\KTSGServerCore\Data\Documents\AccountDocument.cs`

职责：

- Mongo 中的登录账号文档

当前字段：

- `AccountId`
- `Account`
- `AccountNormalized`
- `Password`
- `PlayerId`
- `CreateUtcTicks`
- `LastLoginUtcTicks`

## 3. 当前登录管线

当前登录功能已经实现为“最小 demo 版”。

特点：

- 密码明文存储
- 密码明文比较
- 如果账号不存在，会自动创建账号

## 3.1 登录消息

当前使用的 proto：

- `D:\KTSGServer\KTSGServerCommon\protos\ktsg_server.proto`

登录请求：

- `ReqLogin`
  - `account`
  - `password`
  - `client_version`

登录响应：

- `RspLogin`
  - `result`
  - `user_uid`
  - `session_key`

## 3.2 登录业务入口

业务 handler 在：

- `D:\KTSGServer\KTSGServerCore\Handler\Account\ReqLoginHandler.cs`

它的处理流程是：

1. 取 `ServerRuntime.Current`
2. 调 `runtime.Services.Login.LoginOrCreateSession(...)`
3. 把结果填进 `RspLogin`

所以现在的登录 handler 已经不再放在 `Common` 里，而是放到 `Core` 里。

这是对的。

## 3.3 LoginService 当前行为

文件：

- `D:\KTSGServer\KTSGServerCore\Services\LoginService.cs`

当前逻辑如下：

1. 对客户端传来的 `account` 做标准化
   - `Trim()`
   - `ToLowerInvariant()`
2. 按 `AccountNormalized` 查询 Mongo `account` 集合
3. 如果账号不存在：
   - 自动创建 `AccountDocument`
   - 自动生成：
     - `AccountId`
     - `PlayerId`
4. 如果账号存在但密码不一致：
   - 返回 `AUTH_FAILED`
5. 如果成功：
   - 更新 `LastLoginUtcTicks`
   - 创建或替换 `PlayerSession`
   - 把 `PlayerSession` 标记成已认证
   - 返回 `SUCCESS`

## 3.4 登录成功后的返回值

当前 `RspLogin` 填充为：

- `result = SUCCESS`
- `user_uid = PlayerId`
- `session_key = PlayerSession.SessionId`

这里的 `session_key` 当前只是 demo 阶段的会话字符串，不是长期认证票据体系。

## 3.5 登录成功后的服务端状态变化

一旦登录成功，服务端内部完成这些事情：

1. TCP 连接已存在
2. `LoginService` 读取或创建账号
3. `PlayerService` 创建 `PlayerSession`
4. `PlayerSession` 绑定 TCP 连接
5. `PlayerSession` 写入 `PlayerId`
6. 在线态建立完成

此时玩家进入的是：

- 世界层在线态

而不是：

- 房间态
- 战斗态

## 4. 当前世界层 / 房间层 / 战斗层理解

当前你已经明确的服务端路线是：

- 登录后先进入世界层
- 世界层只用 TCP
- 房间只有在玩家进入 PVP 流程时才会创建
- UDP 只有在真正开战后才启用

也就是说：

### 世界层

- 登录
- 玩家数据
- 背包
- 装备切换
- 武器切换
- 商店
- 聊天
- 匹配

### 房间层

- PVP 准备
- 加入房间
- Ready
- 开战前条件确认

### 战斗层

- UDP token
- UDP 绑定
- 战斗快照
- 移动 / 动作 / 属性 / 伤害

## 5. 当前服务端最小登录时序

可以把现在的链路理解成：

1. 客户端启动
2. 连接 TCP
3. 服务端 `TcpServerService` 创建连接对象
4. 客户端发送 `ReqLogin`
5. `NetMsgProcessor` 分发给 `Core` 里的 `ReqLoginHandler`
6. `ReqLoginHandler` 调 `LoginService`
7. `LoginService` 查询或创建 `AccountDocument`
8. `LoginService` 建立 `PlayerSession`
9. `RspLogin` 返回给客户端
10. 客户端进入世界层

## 6. 下一步最合理的服务端实现顺序

基于现在这条主线，最适合继续做的是：

1. `ReqGetPlayerData`
   - 登录后拉取世界层最小玩家数据
2. 装备 / 武器 / 背包 相关 TCP 接口
3. `RoomSession` 与 `RoomService`
   - 只做 PVP 准备房间
4. `BattleSession`
   - 只做真正战斗期 UDP 逻辑

不要在当前阶段先去做完整战斗 Set/Board，再回来补登录世界层。

## 7. 对客户端实现的直接建议

结合现在服务端这条主线，客户端网络层推荐这样落：

- `System/NetworkSystem`
  - `NetworkSystem`
  - `WorldTcpClient`
  - `BattleUdpClient`

其中：

- 世界层 UI、登录流程、商店、装备切换都只依赖 `WorldTcpClient`
- 只有进入 `RoomSession -> BattleSession` 后，才激活 `BattleUdpClient`

这和服务端现在的方向是一一对应的。
