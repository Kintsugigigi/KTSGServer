using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;

namespace KTSG.Network
{
    public class TcpClientManager
    {
        public static TcpClientManager Instance { get; } = new TcpClientManager();
        private TcpClientManager() { }

        private TCPNetManager _manager;

        public INetConnection Connection { get; private set; }

        public bool IsRunning => _manager != null && Connection != null && Connection.IsConnected;

        public async Task<INetConnection> ConnectAsync(string ip, int port)
        {
            if (IsRunning) return Connection;

            _manager = new TCPNetManager(new ClientTcpListener(this));

            if (!_manager.StartServer(0))
            {
                NetLogger.Error("[TCP Client] Failed to init socket environment.");
                return null;
            }

            NetLogger.Info($"[TCP Client] Connecting to {ip}:{port}...");
            var peer = await _manager.Connect(ip, port);

            if (peer != null)
            {
                return Connection;
            }

            NetLogger.Error("[TCP Client] Connection failed.");
            return null;
        }

        public void Update()
        {
            _manager?.PollEvents();
        }

        public void Disconnect()
        {
            if (_manager != null)
            {
                _manager.Stop();
                _manager = null;
                Connection = null;
                NetLogger.Info("[TCP Client] Disconnected by self.");
            }
        }

        private class ClientTcpListener : ITCPEventListener
        {
            private readonly TcpClientManager _owner;
            public ClientTcpListener(TcpClientManager owner) => _owner = owner;

            public void OnPeerConnected(TCPPeer peer)
            {
                _owner.Connection = new TcpConnectionAdapter(peer);
                NetLogger.Info($"[TCP Client] Connected to Server: {peer.RemoteEndPoint}");
            }

            public void OnPeerDisconnected(TCPPeer peer, DisconnectInfo info)
            {
                NetLogger.Warning($"[TCP Client] Disconnected from Server. Reason: {info.Reason}");
                _owner.Connection = null;
            }

            public void OnNetworkReceive(TCPPeer peer, uint cmdId, ushort rpcSeq, TCPNetPacketReader reader)
            {
                if (_owner.Connection != null)
                {
                    NetMsgProcessor.Instance.ProcessMessage(_owner.Connection, cmdId, rpcSeq, reader.GetRemainingBytesSpan());
                }
            }

            public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
            {
                NetLogger.Error($"[TCP Client] Network Error: {socketError}");
            }
        }
    }
}
