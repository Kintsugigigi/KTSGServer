# KTSGServer 架构阅读记录

> 说明：这份文档偏“总览”。如果要按当前最新方向推进实施顺序，请以 `CodexRead\ServiceFirstRoadmap.md` 为主。

## 1. 当前代码结构概览

当前解决方案 `D:\KTSGServer\KTSGSever.sln` 里有 3 个项目：

- `KTSGServerCommon`
  - 当前最核心的项目。
  - 里面同时放了：
    - 自定义 TCP 实现
    - LiteNetLib UDP 封装
    - `INetConnection` 抽象
    - Proto 生成产物
    - 消息分发器 `NetMsgProcessor`
    - 客户端侧的 `TCPClientManager` / `UdpClientManager`
    - 服务端侧的 `NetworkService` / `UdpSessionModule`
- `KTSGServerCore`
  - 目前内容很少，只有一个 `TCPServerService.cs`。
  - 现阶段还不是“服务端启动入口”，更像是一个未成型的宿主层。
- `LiteNetLib`
  - 本地源码形式引入的 UDP 库。
  - 这次阅读仅关注主流程：`NetManager`、`NetPeer`、连接/收包/事件派发。

结论：目前 `Common` 承担了“协议层 + 传输层 + 客户端 + 服务端 + 部分基础设施”的混合职责，`Core` 反而还没有真正成为服务端主工程。

## 2. 当前网络与协议实现

### 2.1 TCP

你已经在 `KTSGServerCommon\TCP` 下实现了一套自定义 TCP 网络层，核心链路如下：

- `TCPNetManager`
  - 负责监听、连接、接收连接、维护 `TCPPeer`
  - 通过事件队列把底层 Socket 事件转发到 `ITCPEventListener`
- `TCPPeer`
  - 每个 TCP 连接一个 Peer
  - 负责异步收包、发包队列、断线
- `TcpProtocol`
  - 自定义 TCP 包头
  - 结构是：
    - `Length(4)`
    - `Magic(2)`
    - `CmdId(4)`
    - `Seq(2)`
    - `Body`
- `TcpConnectionAdapter`
  - 把 `TCPPeer` 适配成统一接口 `INetConnection`

这条链路已经具备作为常驻 TCP 服务端的基础能力。

### 2.2 UDP

UDP 侧复用了 LiteNetLib，外层做了统一协议封装：

- `UdpConnectionAdapter`
  - 把 `NetPeer` 适配成 `INetConnection`
- `UdpProtocol`
  - 自定义 UDP 包头
  - 结构是：
    - `Magic(2)`
    - `CmdId(4)`
    - `Seq(2)`
    - `Body`
- `NetworkService` / `UdpSessionModule`
  - 都能驱动 UDP 服务
  - `UdpSessionModule` 更像“战局会话级 UDP 实例”

LiteNetLib 本次只看了主流程，关键点是：

- `NetManager` 负责轮询 `PollEvents()`
- `NetPeer` 负责多通道发送
- 你现在设置了 `ChannelsCount = 2`
- UDP 是否可靠由 `DeliveryMethod` 控制

### 2.3 Proto 与消息处理

你当前的消息处理链路已经比较清楚：

1. `ktsg_server.proto` 定义协议
2. `Proto2CsGen.bat` 生成 `MsgMeta.cs` 和 `GenHandlers`
3. 网络层读出 `cmdId / seq / body`
4. `NetMsgProcessor.Instance.ProcessMessage(...)`
5. 通过 `MsgMeta.CmdIdToItem` 找到 `Parser`
6. 反序列化成 protobuf 消息
7. 按 handler 类型分发到：
   - `MsgHandler<T>`
   - `ReqHandler<TReq, TRsp>`
   - `RspHandler<TMsg>`

这套模式适合作为 TCP/UDP 共用的上层消息入口。

## 3. 当前代码里的主要问题

### 3.1 还没有真正的“服务端启动入口”

`KTSGServerCore.csproj` 目前没有明显的可执行宿主定位，也没有 `Program.cs`。

这意味着以下事情还没有统一启动：

- 日志初始化
- 配置加载
- MongoDB 初始化
- `NetMsgProcessor.Init(...)`
- TCP 常驻服务启动
- 主循环或后台轮询
- 关闭时资源释放

### 3.2 `NetworkService` 混合了客户端和服务端职责

`KTSGServerCommon\NetWorkMgr\NetWorkService.cs` 里同时有：

- 服务端能力：
  - `StartTcpServer`
  - `StartUdpServer`
- 客户端能力：
  - `ConnectTcpAsync`
  - `ConnectUdp`

这会让后续架构很容易继续混乱。服务端启动入口如果直接依赖这个类，短期能跑，但中期维护成本会明显上升。

### 3.3 `TCPServerService` 与 `NetworkService` 的 TCP 服务能力重复

`KTSGServerCore\TCPServerService.cs` 和 `NetworkService` 里都在做 TCP 服务端包装，职责重叠。

建议二选一：

- 要么删掉 `TCPServerService`，统一收口到一个 Host 层的 `TcpGateway`
- 要么保留 `TCPServerService`，那就不要再让 `NetworkService` 同时承担 TCP 服务端职责

### 3.4 `Common` 项目承担了过多职责

当前 `KTSGServerCommon` 包含：

- 传输实现
- 协议元数据
- 消息处理器
- 客户端网络管理器
- 服务端网络管理器
- 对象池

这不是“通用层”，而是“所有东西都先放这里”。

短期可以继续用，但建议后续拆分。

### 3.5 生成 Handler 的命名空间拼写不一致

`Proto2CsGen.bat` 里 `HandlerNS="KTSG.NetWork"`，而你大部分网络代码命名空间是 `KTSG.Network`。

这不会立刻导致功能失效，因为处理器扫描基于接口和程序集，不只看命名空间；但这会长期增加阅读和维护成本，建议统一。

### 3.6 UDP 接入校验仍是硬编码

当前 UDP 接入使用：

- `request.AcceptIfKey("MyKey")`

这只能作为临时测试。正式战局里应该改成：

- TCP 登录后签发 battle token
- 客户端进入战局时拿 token 连接 UDP
- 服务端校验 token 是否属于该玩家、该房间、该时间窗口

### 3.7 业务会话关联还没建立

当前 `INetConnection` 只是底层连接抽象，没有看到显式的：

- `PlayerId`
- `AccountId`
- `SessionId`
- `BattleId`
- `TransportType`

后面你要做“TCP 常驻大厅 + UDP 战局按需开启”，就必须把“玩家业务会话”和“底层连接对象”拆开。

## 4. 推荐目标架构

## 4.1 总体原则

建议把未来服务端分成 4 层理解：

- Host 层
  - 进程启动、配置、日志、数据库、主循环
- Gateway 层
  - TCP 网关
  - UDP 战局网关
- Session / Domain 层
  - 登录态
  - 大厅态
  - 房间态
  - 战局态
- Protocol 层
  - proto
  - 消息元数据
  - handler 分发

## 4.2 针对你当前目标的推荐形态

### TCP 常驻

TCP 作为全局常驻网关，负责：

- 登录
- 角色/装备切换
- 商店
- 邮件
- 好友
- 聊天
- 匹配请求
- 战局分配结果下发

特点：

- 进程启动时就启动
- 生命周期跟服务端进程一致
- 适合可靠请求/响应
- 不需要固定 tick 驱动业务，可事件驱动

### UDP 按战局按需启停

UDP 不建议做成“整个服务端唯一常驻全局实例”，更适合以下两种之一：

- 方案 A：单进程内常驻一个 UDP 端口，但只有玩家进入战局时才为其建立 battle session
- 方案 B：单进程内可创建多个 `UdpSessionModule`，按房间/战场动态启停

基于你当前代码，建议先走方案 A，再视压力演进到方案 B。

原因：

- 启动入口更简单
- NAT 不考虑时，客户端接入逻辑更直接
- 先把“TCP 常驻 + UDP 可选加入/离开”跑通最重要

也就是说，第一阶段的“UDP 可中途开启关闭”更建议理解为：

- 对玩家和房间来说可选、可加入、可离开
- 对服务端进程来说先不一定要频繁开关底层 UDP Socket

这样风险更低。

## 5. 建议的项目职责调整

### 5.1 短期最小改动方案

不要求你现在立刻大拆分，可以先这样收敛：

- `KTSGServerCommon`
  - 保留：
    - proto 生成结果
    - `MsgMeta`
    - `NetMsgProcessor`
    - `INetConnection`
    - TCP/UDP 底层传输适配
  - 暂时也可保留现有 TCP/UDP 实现
- `KTSGServerCore`
  - 变成真正的服务端宿主
  - 新增：
    - `Program.cs`
    - `ServerBootstrap`
    - `ServerRuntime`
    - `TcpGateway`
    - `BattleUdpGateway`
    - `PlayerSessionManager`
    - `BattleSessionManager`

### 5.2 中期更合理的拆分方案

后续可以考虑拆成：

- `KTSGServer.Protocol`
  - proto、`MsgMeta`、handler 基类
- `KTSGServer.Transport`
  - 自定义 TCP、LiteNetLib UDP 封装、连接适配器
- `KTSGServer.Core`
  - 业务会话、房间、战斗、快照、数据库服务
- `KTSGServer.Host`
  - 启动入口、配置、日志、DI、进程宿主

这个不是当前必须做，但应作为 TODO。

## 6. 第一阶段实现目标

你现在最值得先做的，不是马上把战斗逻辑写完，而是先把“宿主 + 生命周期 + 会话模型”立住。

### 阶段 1：先做 TCP 常驻，UDP 作为可选战局通道

在当前版本基础上，我建议把这个阶段再细化成“先固定 UDP 发送策略，再接战斗逻辑”，避免后续 proto 一多就返工。

建议拆成下面几个步骤：

1. 固定 UDP 传输策略
   - 固定逻辑通道：
     - `channel 0 = State`
     - `channel 1 = Event`
     - `channel 2 = Bulk`
   - 固定核心投递语义：
     - 高频状态流优先 `Sequenced`
     - 离散关键事件优先 `ReliableOrdered`
     - 全量快照和重连恢复优先 `ReliableOrdered + Bulk`
   - 这一层应先于战斗业务协议扩张完成

2. 建立服务端宿主入口
   - 在 `KTSGServerCore` 增加 `Program.cs`
   - 初始化日志、配置、MongoDB、`NetMsgProcessor`
   - 启动 TCP 服务
   - 启动主循环

3. 建立 TCP 常驻网关
   - 统一管理 TCP 连接
   - 在连接建立时创建 `PlayerSession`
   - 在断开时回收会话

4. 建立登录态和大厅态
   - `ReqLogin -> RspLogin`
   - 登录成功后将 `PlayerSession` 标记为已认证
   - 后续装备/商店/聊天全部先走 TCP

5. 建立战局分配模型
   - 玩家通过 TCP 进入匹配/房间
   - 房间准备完成后，服务端生成：
     - `battleId`
     - `udpPort`
     - `battleToken`
   - 再通过 TCP 下发给客户端

6. 建立 UDP 战局接入
   - 客户端拿 `battleToken` 连接 UDP
   - 服务端校验 token
   - 校验通过后，将该 UDP 连接绑定到已有 `PlayerSession`

7. 优先打通两条最小战斗流
   - 客户端上行输入流：
     - `InputState -> channel 0 / Sequenced`
   - 服务端下行状态流：
     - `TransformState -> channel 0 / Sequenced`
   - 这是第一版最有价值的 UDP 闭环

8. 再补离散关键战斗事件
   - `SkillCastConfirmed`
   - `HitConfirmed`
   - `BuffAdded/Removed`
   - `Dead/Revive`
   - 优先 `channel 1 / ReliableOrdered`

9. 最后接入全量快照和断线恢复
   - `FullBattleSnapshot`
   - `ReconnectSnapshot`
   - 优先 `channel 2 / ReliableOrdered`

10. 建立 UDP 离场与回收
   - 战局结束或掉线后，移除 UDP 绑定
   - TCP 可继续保留在线，返回大厅

这条顺序的关键在于：

- 先把“玩家身份和生命周期”统一在 TCP
- 再把 UDP 作为临时战斗通道挂接到同一个玩家会话上
- 并且先固定 UDP 消息语义和通道分工，再让战斗协议增殖

## 7. 推荐的运行时对象模型

建议引入两个核心对象：

### 7.1 PlayerSession

表示“玩家业务会话”，而不是单纯 socket。

建议至少包含：

- `PlayerId`
- `Account`
- `TcpConnection`
- `UdpConnection` 可空
- `AuthState`
- `CurrentRoomId`
- `CurrentBattleId`
- `LastActiveTime`

### 7.2 BattleSession

表示一个战局实例。

建议至少包含：

- `BattleId`
- `Players`
- `TickRate`
- `SnapshotBuffer`
- `UdpTokenMap`
- `State`

关系建议：

- 一个 `PlayerSession` 常驻 TCP
- 一个 `PlayerSession` 在战斗中可临时挂一个 UDP 连接
- 一个 `BattleSession` 管理多个玩家的 UDP 状态同步

## 8. 关于服务器 tick 与快照

你的客户端逻辑帧是 30 tick，这不意味着“整个服务端所有模块都必须 30 tick”，但战斗模拟层建议优先与客户端保持一致。

推荐做法：

- TCP 大厅/登录/商店/聊天
  - 事件驱动即可
  - 不需要固定 30 tick
- 战斗模拟服务器
  - 建议先用 30 tick
- UDP 快照广播
  - 不一定要 30 次/秒
  - 可以先 10~20Hz 广播快照，再根据表现调整

原因：

- 如果你未来做的是强同步/半确定性战斗，服务端与客户端都用 30 tick 最容易对齐
- 快照发送频率和模拟频率不是同一个概念
- 模拟可以 30 tick，广播可以低一些，客户端用插值/预测补平滑

第一版建议：

- 战斗逻辑 tick：30
- 快照保存频率：30 或每 N tick 1 次
- UDP 广播频率：10 或 15 起步
- 逻辑通道数量：3
  - `State`
  - `Event`
  - `Bulk`

如果后面你发现带宽和 CPU 仍然充裕，再往上调。

## 9. MongoDB 建议

## 9.1 环境配置建议

如果你准备在 Windows 本地开发，最直接的方式是：

1. 安装 MongoDB Community Server
2. 安装 MongoDB Shell 或 Compass
3. 将服务注册为 Windows Service
4. 本地开发默认连接：
   - `mongodb://127.0.0.1:27017`

建议准备一个配置文件，例如：

```json
{
  "Database": {
    "ConnectionString": "mongodb://127.0.0.1:27017",
    "DatabaseName": "ktsg_server"
  }
}
```

如果你之后要部署正式环境，再区分：

- 本地开发
- 测试环境
- 正式环境

不要把连接串硬编码进代码。

### 9.2 集合建议

初期可先准备：

- `player`
- `player_inventory`
- `player_mail`
- `chat_msg`
- `battle_record`
- `battle_snapshot`

并尽早建索引，例如：

- `player.uid`
- `player.account`
- `chat_msg.channel_id + send_time`
- `battle_record.battle_id`

## 10. 你给的 `DbMgr` 能不能用

结论：可以作为第一版使用，但还需要做几处修正，否则后面容易踩坑。

你给的版本：

```csharp
public static class DbMgr
{
    public static MongoClient Client { get; private set; }
    public static IMongoDatabase Database { get; private set; }
    public static IMongoCollection<Player> Players => Database.GetCollection<Player>("player");
    public static IMongoCollection<ChatMsg> ChatMsgs => Database.GetCollection<ChatMsg>("chat_msg");

    public static void Init()
    {
        string connectionString = AppConfigMgr.Config.Database.ConnectionString;
        string databaseName = AppConfigMgr.Config.Database.DatabaseName;

        BsonSerializer.RegisterSerializer(new Vector3dSerializer());

        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase(databaseName);
    }
}
```

### 10.1 这个版本的优点

- `MongoClient` 做成单例方向是对的
- `IMongoDatabase` 缓存下来也是对的
- 集合访问写成属性，第一版开发效率高

### 10.2 需要修正的点

1. `Init()` 应该做成幂等
   - 避免重复初始化

2. `BsonSerializer.RegisterSerializer(...)` 不能无保护重复注册
   - 多次启动流程或测试环境下可能抛异常

3. 应该在初始化时做参数校验
   - 连接串为空
   - 数据库名为空

4. 最好加一次数据库健康检查
   - 例如启动时 ping 一次

5. 静态全局类适合第一版，但中后期最好切成服务接口
   - 否则单元测试和多环境切换会比较难

### 10.3 第一版可接受写法

```csharp
public static class DbMgr
{
    private static int _inited;

    public static MongoClient Client { get; private set; }
    public static IMongoDatabase Database { get; private set; }

    public static IMongoCollection<Player> Players =>
        Database.GetCollection<Player>("player");

    public static IMongoCollection<ChatMsg> ChatMsgs =>
        Database.GetCollection<ChatMsg>("chat_msg");

    public static void Init()
    {
        if (Interlocked.Exchange(ref _inited, 1) == 1)
            return;

        string connectionString = AppConfigMgr.Config.Database.ConnectionString;
        string databaseName = AppConfigMgr.Config.Database.DatabaseName;

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database.ConnectionString is empty");

        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Database.DatabaseName is empty");

        if (!BsonSerializer.SerializerRegistry.GetSerializer<Vector3d>().GetType()
            .IsAssignableTo(typeof(Vector3dSerializer)))
        {
            BsonSerializer.RegisterSerializer(new Vector3dSerializer());
        }

        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase(databaseName);
    }
}
```

上面这段只是方向示例。真正落地时，序列化器注册建议再写得更稳一点，避免依赖“取不到默认 serializer”这个行为差异。

## 11. 我对 `KTSGServerCommon.csproj` 和 `KTSGServerCore.csproj` 的 TODO 建议

### `KTSGServerCommon.csproj`

当前问题：

- 名字叫 `Common`，但实际不是纯通用层
- 直接依赖 `LiteNetLib`
- 同时放客户端与服务端网络代码

TODO：

- 把客户端网络管理器和服务端网关拆开
- 把 proto / 元数据 / handler 基类与传输层逐步拆开
- 尽量减少“宿主逻辑”进入 Common

### `KTSGServerCore.csproj`

当前问题：

- 还没有成为真正的启动宿主
- 没有统一管理配置、日志、DB、生命周期

TODO：

- 让它成为可执行项目
- 增加 `Program.cs`
- 增加 `ServerBootstrap`
- 增加运行主循环
- 作为 TCP 常驻入口和未来 UDP 战局入口的统一宿主

## 12. 建议的启动入口蓝图

第一版可以按这个顺序组织：

1. `Program`
   - 创建日志
   - 读取配置
   - `DbMgr.Init()`
   - `NetMsgProcessor.Instance.Init(...)`
   - 创建 `ServerRuntime`
   - `ServerRuntime.Start()`

2. `ServerRuntime`
   - 持有：
     - `TcpGateway`
     - `BattleSessionManager`
     - 可选 `UdpGateway`
   - 提供：
     - `Start()`
     - `Stop()`
     - `Update()`

3. `TcpGateway`
   - 常驻监听 TCP
   - 管理 TCP 连接到 `PlayerSession` 的映射

4. `BattleSessionManager`
   - 管理战局创建、销毁、tick、快照

5. `UdpGateway` 或 `BattleUdpSession`
   - 负责战局期间的 UDP 接入和广播

## 13. 首阶段最小可交付目标

在你下一步开始写服务端入口时，我建议目标先收敛成下面这个版本：

- 进程启动后 TCP 自动常驻监听
- 可以处理 `ReqLogin -> RspLogin`
- 登录成功后创建 `PlayerSession`
- 通过 TCP 下发“可进入战局”的指令
- 玩家进入战局时再绑定 UDP
- 离开战局时释放 UDP 绑定，但 TCP 保持在线

这版跑通后，再继续做：

- 战局 tick
- 快照保存
- 位置同步
- 战斗事件
- 掉线重连

## 14. 本次阅读后最重要的结论

1. 你当前的 TCP、自定义协议、proto 分发、LiteNetLib UDP 封装，已经具备搭出第一版服务端的基础。
2. 最大缺口不是“底层收发”，而是“宿主入口、生命周期和会话模型”。
3. 第一阶段不要急着做“频繁开关 UDP Socket 的复杂设计”，先做“TCP 常驻 + 玩家按需加入/离开 UDP 战局”。
4. 战斗模拟 tick 建议先定为 30；TCP 业务不用强行套 30 tick。
5. `DbMgr` 可以用，但建议先补幂等初始化、配置校验和序列化器注册保护。
