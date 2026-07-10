using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace KTSG.Network
{
    public class UdpClientManager
    {
        public static UdpClientManager Instance { get; } = new UdpClientManager();
        private UdpClientManager() { }

        private NetManager _manager;

        public INetConnection Connection { get; private set; }

        public bool IsRunning => _manager != null && _manager.IsRunning;

        public void Connect(string ip, int port, string key)
        {
            if (IsRunning)
            {
                NetLogger.Warning("[UDP Client] Already running. Disconnecting previous session...");
                Disconnect();
            }

            var listener = new ClientUdpListener(this);
            _manager = new NetManager(listener);
            _manager.ChannelsCount = UdpChannels.ChannelCount;
            _manager.DisconnectTimeout = 10000;
            _manager.UnconnectedMessagesEnabled = true;

            if (_manager.Start())
            {
                NetLogger.Info($"[UDP Client] Local socket bound on port {_manager.LocalPort}");
                _manager.Connect(ip, port, key);
            }
            else
            {
                NetLogger.Error("[UDP Client] Failed to bind local socket.");
            }
        }

        public void Disconnect()
        {
            if (_manager != null)
            {
                _manager.Stop();
                _manager = null;
                Connection = null;
                NetLogger.Info("[UDP Client] Session Stopped.");
            }
        }

        public void Update()
        {
            _manager?.PollEvents();
        }

        private class ClientUdpListener : INetEventListener
        {
            private readonly UdpClientManager _owner;
            public ClientUdpListener(UdpClientManager owner) => _owner = owner;

            public void OnPeerConnected(NetPeer peer)
            {
                var conn = new UdpConnectionAdapter(peer);
                peer.Tag = conn;
                _owner.Connection = conn;

                NetLogger.Info($"[UDP Client] Connected to Battle Server: {peer}");
            }

            public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
            {
                NetLogger.Warning($"[UDP Client] Disconnected. Reason: {info.Reason}");
                _owner.Connection = null;
                peer.Tag = null;
            }

            public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
            {
                if (peer.Tag is INetConnection conn)
                {
                    var rawSpan = reader.GetRemainingBytesSpan();
                    if (UdpProtocol.ReadHeader(rawSpan, out uint cmdId, out ushort seq, out var body))
                    {
                        NetMsgProcessor.Instance.ProcessMessage(conn, cmdId, seq, body);
                    }
                    else
                    {
                        NetLogger.Warning($"[UDP Client] Received Invalid Packet from {peer}");
                    }
                }
            }

            public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
            {
                NetLogger.Error($"[UDP Client] Socket Error: {socketError}");
            }

            public void OnConnectionRequest(ConnectionRequest request) => request.Reject();
            public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
            public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
            public void OnMessageDelivered(NetPeer peer, object userData) { }
        }
    }
}
