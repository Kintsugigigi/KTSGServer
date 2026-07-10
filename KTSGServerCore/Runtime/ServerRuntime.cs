using System.Diagnostics;
using System.Net;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Config;
using KTSG.Server.Model;
using KTSG.Server.Services;

namespace KTSG.Server.Runtime
{
    public sealed class ServerRuntime
    {
        public static ServerRuntime Instance { get; } = new();

        private readonly Stopwatch _stopwatch = new();
        private readonly Dictionary<Type, IService> _services = new();
        private double _tickAccumulatorMs;
        private double _tickIntervalMs = 1000d / 30d;
        private long _logicalTickCount;
        private bool _isStarted;

        public TCPServerManager TcpServer { get; }
        public UdpServerManager UdpServer { get; }

        private ServerRuntime()
        {
            TcpServer = new TCPServerManager();
            UdpServer = new UdpServerManager();
        }

        public void Start()
        {
            if (_isStarted)
            {
                return;
            }

            Register();

            foreach (var service in _services.Values)
            {
                service.Init();
            }

            NetMsgProcessor.Instance.Init(new[] { typeof(ServerRuntime).Assembly, typeof(MsgMeta).Assembly }, includeReqHandlers: true);

            var serverConfig = AppConfig.Data.Server;
            if (!TcpServer.Start(serverConfig.BindAddress, serverConfig.TcpPort))
            {
                throw new InvalidOperationException(
                    $"Failed to start TCP server on {serverConfig.BindAddress}:{serverConfig.TcpPort}");
            }

            TcpServer.SetClientCallbacks(
                onClientConnected: conn =>
                {
                    NetLogger.Info($"[TCP] Client connected. ConnectionId={conn.ConnectionId}, Remote={conn.RemoteEndPoint}");
                    GetService<PlayerService>().OnClientConnected(conn);
                },
                onClientDisconnected: conn =>
                {
                    NetLogger.Info($"[TCP] Client disconnected. ConnectionId={conn.ConnectionId}, Remote={conn.RemoteEndPoint}");
                    GetService<PlayerService>().OnClientDisconnected(conn.ConnectionId);
                });

            NetLogger.Info($"[TCP] Server started on {serverConfig.BindAddress}:{serverConfig.TcpPort}");

            _tickIntervalMs = 1000d / Math.Max(1, AppConfig.Data.LogicalFrameRate);
            _tickAccumulatorMs = 0;
            _stopwatch.Restart();
            _isStarted = true;
        }

        public bool StartUdpGateway(int port, Func<string, IPEndPoint, bool>? tokenValidator = null)
        {
            UdpServer.TokenValidator = tokenValidator;
            return UdpServer.Start(port);
        }

        public void StopUdpGateway()
        {
            UdpServer.Stop();
        }

        public void Stop()
        {
            if (!_isStarted)
            {
                return;
            }

            TcpServer.Stop();
            UdpServer.Stop();
            PureClassPool.ClearAll();

            _stopwatch.Stop();
            _tickAccumulatorMs = 0;
            _isStarted = false;
        }

        public void Tick()
        {
            if (!_isStarted)
            {
                return;
            }

            var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
            _stopwatch.Restart();

            if (elapsedMs <= 0)
            {
                return;
            }

            _tickAccumulatorMs += elapsedMs;
            PureClassPool.Update((float)(elapsedMs / 1000d));
            while (_tickAccumulatorMs >= _tickIntervalMs)
            {
                _tickAccumulatorMs -= _tickIntervalMs;
                ExecuteTick();
            }
        }
        
        private void ExecuteTick()
        {
            _logicalTickCount++;
            if (_logicalTickCount % 30 == 0)
            {
                NetLogger.Info($"[Server] Logical tick {_logicalTickCount}");
            }

            TcpServer.Update();
            UdpServer.Update();
            NetMsgProcessor.Instance.Update();
        }

        public T GetService<T>() where T : class, IService
        {
            if (_services.TryGetValue(typeof(T), out var service))
            {
                return (T)service;
            }

            throw new KeyNotFoundException($"Service not found: {typeof(T).FullName}");
        }
        

        private void Register()
        {
            if (_services.Count > 0)
            {
                return;
            }
            RegisterService(new ConfigService());
            RegisterService(new DbService());
            RegisterService(new PlayerService());
            RegisterService(new ItemService());
            RegisterService(new RoleService());
            RegisterService(new GamePlayService());
            RegisterService(new QueryService());
            RegisterService(new QuestService());
            RegisterService(new LoginService());
            RegisterService(new SceneService());
        }

        private void RegisterService<T>(T service) where T : class, IService
        {
            var serviceType = typeof(T);
            if (_services.ContainsKey(serviceType))
            {
                throw new InvalidOperationException($"Service already registered: {serviceType.FullName}");
            }

            _services.Add(serviceType, service);
        }
    }
}
