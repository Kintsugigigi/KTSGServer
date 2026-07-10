# DemoServer 登录与房间架构梳理

本文整理自 `D:\Unity Project\DemoServer\ZZZServer.sln`，目标是回答这几个问题：

- 这个项目是如何完成登录验证的
- 它的整体架构流程是什么
- 从连接到登录到进入房间到断线的完整链路是什么
- 哪些地方适合迁移到 `KTSGServer`
- 哪些地方只适合参考，不建议直接照搬

## 1. 解决方案结构

`ZZZServer.sln` 里和主链路最相关的项目有 3 个：

- `KiraraNetworkCommon`
  - 自定义 TCP 协议、`Session`、消息分发、`MsgHandler/RpcHandler`
- `KiraraNetworkServer`
  - TCP 服务端监听、Accept、新建 `Session`
- `ZZZServer`
  - 真正的业务层：配置、Mongo、玩家模型、登录/注册、房间、战斗同步

整体分层其实很清楚：

1. `Server` 负责 TCP 接入
2. `Session` 负责单连接收发
3. `NetMsgProcessor` 负责把收到的包转成消息并分发
4. `Handler/*` 负责按消息类型处理业务
5. `Service/*` 负责玩家、房间、背包、武器等业务对象
6. `Model/*` 负责 Mongo 持久化模型和部分运行时方法

这套结构和你当前 `KTSGServerCommon + KTSGServerCore` 的分层思路是接近的，只是它把“运行时模型”和“Mongo 文档模型”混得更重一些。

## 2. 启动流程

入口在 `D:\Unity Project\DemoServer\ZZZServer\Program.cs`。

它的启动顺序是：

1. `AppConfigMgr.Init("Data/App.toml")`
2. 初始化 Serilog
3. `ConfigMgr.Init()`
4. `ActionMgr.Init()`
5. `NavigationMgr.Instance.Init()`
6. `DbMgr.Init()`
7. `KiraraNetwork.Init(new MsgMeta().Init(), typeof(Program).Assembly)`
8. 创建 `Server`
9. 注册 `RoomService.Update` 到网络 Tick
10. 注册关闭前保存玩家 `PlayerService.SaveAllPlayers`
11. `server.Run(127.0.0.1:23434)`

这里值得你参考的点：

- 启动入口集中，所有基础设施都在 `Program` 里串起来
- 网络消息扫描和运行时更新挂载在一起
- 关闭前统一做持久化

## 3. 网络骨架

### 3.1 TCP 服务端

`D:\Unity Project\DemoServer\KiraraNetworkServer\Server.cs`

它只做几件事：

- 监听 TCP
- `AcceptAsync` 创建 `Session`
- 把 `Session.ReceiveAsync()` 跑起来
- 开一个后台循环检查超时断线
- 按键 `C` 时触发关闭

说明：

- 这是一个纯 TCP 常驻架构，没有 UDP
- `Server` 本身不关心登录、房间、玩家，只管理连接

### 3.2 Session

`D:\Unity Project\DemoServer\KiraraNetworkCommon\Session.cs`

`Session` 是整个网络层最关键的承载对象，里面有：

- `Socket`
- `Data`
- `OnDisconnected`
- `_lastReceiveTime`
- `Send(...)`
- `ReceiveAsync()`

这个项目把“登录后的玩家对象”直接塞进 `session.Data`。

也就是说，登录前：

- `session.Data == null`

登录后：

- `session.Data == Player`

这是它后面业务层全部运行的前提。

### 3.3 消息处理线程

`D:\Unity Project\DemoServer\KiraraNetworkCommon\NetMsgProcessor.cs`

这个类做了三件事：

- 消费消息队列并调用 handler
- 消费任务队列，处理断线回调等延迟任务
- 每 20ms 驱动一次 `updates`

因此它实际上既是：

- 消息分发器
- 主线程替代品
- 简单逻辑帧驱动器

这也是它为什么能把 `RoomService.Update` 挂进去。

## 4. 登录验证是怎么做的

### 4.1 数据模型

登录和玩家数据都直接放在 `Player` 文档里：

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Model\Player.cs`

关键字段：

- `Uid`
- `Username`
- `Password`
- `Session` (`BsonIgnore`)
- `IsOnline` (`BsonIgnore`)
- `Room` (`BsonIgnore`)

这里有一个重要特征：

- Mongo 里存的是 `Player`
- 登录账号和角色数据没有拆开
- `Username` + `Password` 就是登录凭证

### 4.2 登录处理

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Handler\Account\ReqLogin_Handler.cs`

链路如下：

1. `PlayerService.GetPlayerByUsername(req.Username)`
2. 如果找不到玩家，或者 `player.Password != req.Password`
   - 返回“用户名或密码错误”
3. 如果 `player.IsOnline == true`
   - 返回“用户已登录”
4. 登录成功：
   - `session.Data = player`
   - `player.Session = session`
   - `player.IsOnline = true`
5. 注册断线回调
   - 清理 `session.Data`
   - 清理 `player.Session`
   - `player.IsOnline = false`
   - `PlayerService.SavePlayer(player)`

它的登录验证本质上就是：

- 用用户名查 Mongo
- 直接比较明文密码
- 登录成功后把 `Player` 绑定到 `Session`

### 4.3 玩家加载与缓存

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Service\PlayerService.cs`

`PlayerService` 用了一个内存缓存：

- `ConcurrentDictionary<string, Player> UidToPlayer`

行为是：

- 首次按用户名或 Uid 从 Mongo 加载
- 加载后放到内存
- 后续都复用同一个 `Player` 对象
- 断线或关服时再存回 Mongo

这个设计让业务层非常方便，因为所有 handler 操作的都是同一个活对象。

## 5. 注册流程

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Handler\Account\ReqRegister_Handler.cs`

流程是：

1. 校验用户名长度
2. 校验密码长度
3. `players.Find(x => x.Username == username).Any()`
4. 若不存在，`PlayerService.CreatePlayer(...)`
5. `players.InsertOne(player)`

也就是说：

- 账号注册和角色创建是同一步
- 注册成功后就已经有完整玩家文档了

## 6. 登录后如何进入房间

### 6.1 取玩家数据

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Handler\Account\ReqGetPlayerData_Handler.cs`

这个 handler 非常简单：

1. `var player = (Player)session.Data`
2. `rsp.PlayerData = player.Net`

说明它默认认为：

- 能调这个接口的连接已经登录
- `session.Data` 里一定是 `Player`

### 6.2 进入房间

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Handler\Room\MsgEnterRoom_Handler.cs`

流程：

1. 从 `session.Data` 拿到 `Player`
2. 固定取 `RoomService.GetRoom(1)`
3. 如果没有这个房间，就 `RoomService.NewRoom()`
4. `room.AddPlayer(player)`

这里有个明显特征：

- 它不是“按 matchmaking 分配房间”
- 也不是“按房间号加入”
- 它现在其实是“先把人放进一个固定示例房间”

这更像演示版，而不是可扩展的房间系统。

## 7. 房间如何运作

### 7.1 RoomService

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Service\RoomService.cs`

`RoomService` 是静态全局服务：

- `rooms: Dictionary<int, Room>`
- `NewRoom()`
- `GetRoom(id)`
- `Update(dt)`

### 7.2 Room

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Service\Room.cs`

`Room` 里管理：

- `Players`
- `Monsters`
- 房间内广播
- 玩家离开
- 每 Tick 的权威同步广播

`Update(dt)` 做的事情：

1. 更新怪物
2. 组装 `NotifyUpdateFromAuthority`
3. 把所有玩家的 `NSync` 广播出去
4. 组装 `NotifyUpdateMonster`
5. 广播怪物同步

这说明它的房间是：

- 以 `Room` 为战局/同步单元
- 通过 `RoomService.Update` 定期驱动
- 玩家上传 autonomous 状态，房间再向所有人广播 authority 状态

### 7.3 玩家状态上传

文件：

- `D:\Unity Project\DemoServer\ZZZServer\Handler\Room\MsgUpdateEntityFromAutonomous_Handler.cs`

流程：

1. 从 `session.Data` 拿到 `Player`
2. 调 `player.UpdateFromAutonomous(msg.Player)`
3. 房间下一帧 `Update` 时再统一广播

这是一种很典型的：

- 客户端上传自身状态
- 服务端落到运行时对象
- 服务端固定 Tick 广播房间快照

## 8. 断线链路

断线分两层。

### 8.1 Session 断线

`Session.Close()` 会：

1. `isClosed = true`
2. 关闭 socket
3. 调 `OnDisconnected?.Invoke()`

### 8.2 登录后的断线清理

在 `ReqLogin_Handler` 里，登录成功后注册了断线回调：

1. `session.Data = null`
2. `player.Session = null`
3. `player.IsOnline = false`
4. `PlayerService.SavePlayer(player)`

### 8.3 房间内断线清理

在 `Room.AddPlayer(player)` 里，又额外注册了一次 `session.OnDisconnected`：

1. 把 `RemovePlayer(player)` 投递到 `NetMsgProcessor` 的任务队列
2. 从房间移除玩家
3. 广播 `NotifyRemoveSimulatedPlayers`

所以这个项目的断线后果是：

- 网络断开
- 玩家在线态清理
- 玩家持久化
- 玩家从房间里移除
- 房间内其他人收到移除通知

## 9. 它的完整业务时序

### 9.1 从连接到登录

1. TCP 客户端连接 `Server`
2. `AcceptAsync()` 创建 `Session`
3. `Session.ReceiveAsync()` 开始读包
4. 收到 `ReqLogin`
5. `NetMsgProcessor` 调度到 `ReqLogin_Handler`
6. `PlayerService` 从 Mongo 加载 `Player`
7. 比较密码
8. 成功后把 `Player` 绑定到 `Session`

### 9.2 从登录到进入房间

1. 客户端请求 `ReqGetPlayerData`
2. 服务端把 `player.Net` 返回给客户端
3. 客户端发送 `MsgEnterRoom`
4. `MsgEnterRoom_Handler` 取或建房间
5. `Room.AddPlayer(player)`
6. 房间开始在 `RoomService.Update` 中持续广播权威同步

### 9.3 从房间到断线

1. 客户端断开，或超时断开
2. `Session.Close()` 触发 `OnDisconnected`
3. 登录回调清理在线状态并保存玩家
4. 房间回调把玩家移出房间
5. 其他房间玩家收到移除广播

## 10. 哪些架构你能直接借鉴

### 10.1 可以直接借鉴

- `Program` 作为统一宿主入口
  - 这个你现在已经在 `KTSGServerCore` 里开始做了，方向是对的
- “网络层只处理收发，业务层放 handler/service”
  - 这和你现在 `KTSGServerCommon + KTSGServerCore` 的切法很一致
- `Session`/连接对象上挂业务上下文
  - 但你更适合挂 `PlayerSession`，不要像它一样直接挂 `Player`
- `NetMsgProcessor` 统一串行处理消息 + 任务 + Tick
  - 对你做 TCP 常驻和战斗快照很有帮助
- 房间按服务统一驱动
  - `RoomService.Update -> Room.Update` 这条很适合参考
- 断线时把清理逻辑挂到连接生命周期
  - 你现在的 `ServerRuntime` 也应该保持这个模式

### 10.2 你可以改造后借鉴

- `PlayerService` 的内存缓存
  - 可以保留“Mongo -> 内存 runtime”的思路
  - 但建议缓存的是 `PlayerSession` / `PlayerRuntime`
  - 不建议直接把 Mongo 文档对象长期当运行时核心对象
- `Player` 里同时含 Mongo 字段和运行时字段
  - 小项目方便，大项目后面会越来越乱
  - 你更适合拆成 `PlayerDocument + PlayerRuntime`
- `Room` 里做权威同步广播
  - 这个思想可用
  - 但你未来 UDP 战斗房间建议单独变成 `BattleSession`

## 11. 哪些地方不建议你照搬

### 11.1 明文密码

它就是：

- Mongo 里存 `Password`
- 登录时直接 `player.Password != req.Password`

这适合演示，不适合作为长期设计。

### 11.2 账号和角色完全混在一个文档里

它只有 `Player`，没有 `Account`。

你的项目如果以后有：

- 多角色
- 改密码
- 邮箱/手机号绑定
- 第三方登录

就会比较难扩。

### 11.3 `session.Data` 直接塞强业务对象

这让 handler 很方便，但耦合很高。

你更适合：

- `INetConnection -> PlayerSession`
- `PlayerSession -> PlayerDocument/Runtime`

而不是：

- `session.Data -> Player`

### 11.4 静态全局服务过多

它的：

- `PlayerService`
- `RoomService`
- `DbMgr`
- `AppConfigMgr`

基本都是静态。

这对原型很快，但对你后面做：

- TCP 常驻 + UDP 按战局开关
- 多个 battle session
- 更复杂的运行时生命周期

会比较僵。

你现在的 `ServerRuntime + ServerServices` 方向其实比它更好。

### 11.5 房间加入逻辑过于演示化

`MsgEnterRoom_Handler` 直接硬编码：

- 只取房间 1
- 没有匹配
- 没有校验
- 没有房主/成员状态机

这部分只能参考最基础流程，不能直接搬。

## 12. 对 KTSGServer 最有价值的借鉴建议

如果映射到你当前 `KTSGServer`，我建议这样吸收它的优点：

### 12.1 可以直接采用的思路

- `Program -> Config -> Db -> MsgMeta -> ServerRuntime.Start()`
- TCP 层建立连接后先得到空 session
- `ReqLoginHandler -> LoginService`
- 登录成功后把 `PlayerSession` 绑定到 TCP 连接
- `RoomService/BattleService` 统一由 `ServerRuntime.Update()` 驱动
- 断线时由运行时统一：
  - 清理登录状态
  - 离开房间/战局
  - 保存玩家

### 12.2 你应该比它更进一步的地方

- 拆开 `AccountDocument` 和 `PlayerDocument`
- 业务 handler 放 `KTSGServerCore`，不要继续把业务逻辑塞在 `Common`
- 用 `ServerRuntime` 承担它 `Program + static service` 的那部分职责
- 房间和战斗分离：
  - `RoomSession` 负责组队/准备
  - `BattleSession` 负责 UDP 同步和快照

## 13. 对当前 KTSGServer 的结论

这个 DemoServer 最值得你借的，不是“实现细节”，而是“主链路组织方式”：

- 启动入口统一串接
- TCP Session 持有登录态
- Handler 很薄，Service 负责业务
- 房间统一驱动更新
- 断线统一做清理和保存

但它也有明显的原型化特征：

- 明文密码
- `Player` 文档和运行时状态混合
- 静态全局服务较多
- 房间逻辑写死

因此更适合你参考成下面这个方向：

1. `Program` 只负责组装运行时
2. `ServerRuntime` 负责生命周期
3. `LoginService` 完成 TCP 登录绑定
4. `PlayerService` 管理在线 `PlayerSession`
5. `RoomService` 管理组队房间
6. `BattleService` 管理 UDP token、战局和快照
7. Mongo 只保存持久化文档，不直接作为核心运行时对象

## 14. 一个简化的迁移映射

DemoServer 的关键对象和你项目的对应关系大致可以这样理解：

- `Server`
  - 对应你现在的 `TcpServerService + ServerRuntime`
- `Session`
  - 对应你现在的 `INetConnection + PlayerSession`
- `ReqLogin_Handler`
  - 对应你后面的 `ReqLoginHandler + LoginService`
- `PlayerService`
  - 对应你现在的 `PlayerService`
- `RoomService + Room`
  - 对应你现在的 `RoomService + RoomSession`
- `Room.Update()`
  - 对应你未来的 `BattleSession.Update()/SnapshotTick`

## 15. 建议你现在怎么用这份参考

如果你只取一条最实用的实现路线，可以直接是：

1. 在 `KTSGServerCore` 先做最小登录链路
   - `ReqLoginHandler -> LoginService -> Mongo`
2. 登录成功后绑定 `PlayerSession`
3. 再做一个最小 `ReqEnterRoom/MsgEnterRoom`
4. `RoomService` 先支持玩家加入/离开
5. 断线时统一清理并保存
6. 之后再把 UDP token 和 `BattleSession` 接进来

这条路线本质上就是吸收了 DemoServer 的主链路组织方式，但避开了它最原型化的那部分设计。
