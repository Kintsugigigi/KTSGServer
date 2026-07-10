using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Google.Protobuf;

namespace KTSG.Network
{
    public class UdpServerManager : INetEventListener
    {
        private NetManager _manager;
        private readonly ConcurrentDictionary<string, string> _acceptedTokensByEndPoint = new();
        public bool IsRunning => _manager != null && _manager.IsRunning;
        public int LocalPort => _manager?.LocalPort ?? 0;

        public Func<string, IPEndPoint, bool>? TokenValidator { get; set; }
        public Action<INetConnection, string>? OnAuthedPeerConnected { get; set; }
        public Action<INetConnection>? OnPeerDisconnectedAction { get; set; }
        
        public bool Start(int port)
        {
            if (IsRunning) return false;

            _manager = new NetManager(this);
            _manager.ChannelsCount = 3; 
            _manager.UnconnectedMessagesEnabled = false; 
            _manager.UpdateTime = 15; 

            if (_manager.Start(port))
            {
                NetLogger.Info($"[UDP Server] Started on port {LocalPort}");
                return true;
            }
            else
            {
                NetLogger.Error($"[UDP Server] Failed to start on port {port}");
                return false;
            }
        }

        /// <summary>
        /// 驱动网络心跳 (需要在主线程 Update 中调用)
        /// </summary>
        public void Update()
        {
            _manager?.PollEvents();
        }

        /// <summary>
        /// 停止会话并断开所有连接
        /// </summary>
        public void Stop()
        {
            if (_manager != null)
            {
                _manager.Stop(); // 这会自动断开所有 Peer
                _manager = null;
                _acceptedTokensByEndPoint.Clear();
                NetLogger.Info("[UDP Server] Stopped");
            }
        }

        /// <summary>
        /// 向房间内所有已连接玩家广播消息
        /// </summary>
        public void Broadcast(uint cmdId, IMessage msg, ushort seq = 0, byte channelId = 0, bool reliable = true)
        {
            if (!IsRunning) return;

            var deliveryMode = reliable ? NetDeliveryMode.ReliableOrdered : NetDeliveryMode.Unreliable;
            
            foreach (var peer in _manager)
            {
                if (peer.ConnectionState == ConnectionState.Connected && peer.Tag is INetConnection conn)
                {
                    conn.TrySend(cmdId, msg, new SendOptions(channelId, deliveryMode, seq));
                }
            }
        }

        public void Broadcast(uint cmdId, IMessage msg, byte channelId, NetDeliveryMode deliveryMode, ushort seq = 0)
        {
            if (!IsRunning) return;

            foreach (var peer in _manager)
            {
                if (peer.ConnectionState == ConnectionState.Connected && peer.Tag is INetConnection conn)
                {
                    conn.TrySend(cmdId, msg, new SendOptions(channelId, deliveryMode, seq));
                }
            }
        }


        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            var conn = new UdpConnectionAdapter(peer);
            peer.Tag = conn;
            
            NetLogger.Info($"[UDP Server] Player Connected: {peer}");

            if (_acceptedTokensByEndPoint.TryRemove(GetEndPointKey(peer), out var token))
            {
                OnAuthedPeerConnected?.Invoke(conn, token);
            }
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            NetLogger.Info($"[UDP Server] Player Disconnected: {peer}. Reason: {info.Reason}");
            if (peer.Tag is INetConnection conn)
            {
                OnPeerDisconnectedAction?.Invoke(conn);
            }
            peer.Tag = null; 
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
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
                    NetLogger.Warning($"[UDP Server] Invalid Packet (Magic/Size) from {peer}");
                }
            }
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            NetLogger.Error($"[UDP Server] Network Error {endPoint}: {socketError}");
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            if (TokenValidator != null)
            {
                if (TryReadToken(request, out var token) && TokenValidator(token, request.RemoteEndPoint))
                {
                    _acceptedTokensByEndPoint[GetEndPointKey(request.RemoteEndPoint)] = token;
                    request.Accept();
                }
                else
                {
                    request.Reject();
                }
                return;
            }

            request.AcceptIfKey("MyKey"); 
        }
        
        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        void INetEventListener.OnMessageDelivered(NetPeer peer, object userData) { }

        private static bool TryReadToken(ConnectionRequest request, out string token)
        {
            token = string.Empty;
            try
            {
                token = request.Data.GetString();
                return !string.IsNullOrWhiteSpace(token);
            }
            catch
            {
                return false;
            }
        }

        private static string GetEndPointKey(IPEndPoint endPoint)
        {
            return endPoint.ToString();
        }
    }
}
