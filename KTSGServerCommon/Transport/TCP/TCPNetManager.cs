using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;

namespace KTSG.Network
{
    public class TCPNetManager
    {

        private readonly TCPNetPacketPool _packetPool = new TCPNetPacketPool();
        private readonly ConcurrentDictionary<int, INetConnection> _connections = new ConcurrentDictionary<int, INetConnection>();
        public Action<INetConnection>? OnClientConnected { get; set; }
        public Action<INetConnection>? OnClientDisconnected { get; set; }
        
        private readonly ConcurrentDictionary<int, TCPPeer> _connectedPeers = new ConcurrentDictionary<int, TCPPeer>();
        private int _nextPeerId = 0; 
        private volatile bool _isRunning;
        private long _connectedPeersCount;
        
        private Socket _listenerSocket;
        private readonly ConcurrentQueue<TCPNetEvent> _pendingEvents = new ConcurrentQueue<TCPNetEvent>();

        public void SetClientCallbacks(
            Action<INetConnection>? onClientConnected,
            Action<INetConnection>? onClientDisconnected)
        {
            OnClientConnected = onClientConnected;
            OnClientDisconnected = onClientDisconnected;
        }
        

        public bool StartServer(string bindAddress, int port)
        {
            if (!IPAddress.TryParse(bindAddress, out var parsedAddress))
            {
                NetLogger.Error($"[TCP] StartServer failed: invalid bind address '{bindAddress}'");
                return false;
            }

            return StartServer(parsedAddress, port);
        }

        private bool StartServer(IPAddress bindAddress, int port)
        {
            if (_isRunning) return false;
            try
            {
                _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _listenerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenerSocket.Bind(new IPEndPoint(bindAddress, port));
                _listenerSocket.Listen(512); 
                
                _isRunning = true;
                _ = AcceptLoop(); 
                
                NetLogger.Debug($"[TCP] Server started on {bindAddress}:{port}");
                return true;
            }
            catch (Exception e)
            {
                NetLogger.Error($"[TCP] StartServer failed: {e.Message}");
                return false;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            
            try { _listenerSocket?.Close(); } catch { }
            
            foreach (var peer in _connectedPeers.Values)
            {
                peer.Disconnect(DisconnectReason.DisconnectPeerCalled);
            }

            PollEvents();
            ClearPendingEvents();
            _connections.Clear();
        }


        private async Task AcceptLoop()
        {
            while (_isRunning && _listenerSocket != null)
            {
                try
                {
                    // 等待客户端连接
                    Socket clientSocket = await _listenerSocket.AcceptAsync();
                    CreateAndAddPeer(clientSocket);
                }
                catch (ObjectDisposedException) { break; } 
                catch (Exception e)
                {
                    if (_isRunning) NetLogger.Error($"[TCP] Accept error: {e.Message}");
                }
            }
        }

        private void CreateAndAddPeer(Socket socket)
        {
            if (!_isRunning) return ;

            int id = Interlocked.Increment(ref _nextPeerId);
            var peer = new TCPPeer(id, socket, this);

            if (_connectedPeers.TryAdd(id, peer))
            {
                Interlocked.Increment(ref _connectedPeersCount);
                var conn = new TcpConnectionAdapter(peer);
                _connections.TryAdd(id, conn);
                CreateConnectEvent(peer);
                peer.StartReceiveLoop();
            }
            else
            {
                peer.Disconnect(DisconnectReason.ConnectionFailed);
            }
        }



        internal void OnTcpPacketReceived(TCPPeer peer, uint cmdId, ushort rpcSeq, TCPNetPacket packet)
        {
            EnqueueEvent(new TCPNetEvent
            {
                Type = TCPNetEvent.EType.Receive,
                Peer = peer,
                RemoteEndPoint = peer.RemoteEndPoint,
                CmdId = cmdId,
                RpcSeq = rpcSeq,
                Packet = packet
            });
        }

        internal void OnPeerDisconnected(TCPPeer peer, DisconnectReason reason, SocketError error)
        {
            if (_connectedPeers.TryRemove(peer.Id, out _))
            {
                Interlocked.Decrement(ref _connectedPeersCount);
                EnqueueEvent(new TCPNetEvent
                {
                    Type = TCPNetEvent.EType.Disconnect,
                    Peer = peer,
                    DisconnectReason = reason,
                    ErrorCode = error
                });
            }
        }

        private void CreateConnectEvent(TCPPeer peer)
        {
            EnqueueEvent(new TCPNetEvent
            {
                Type = TCPNetEvent.EType.Connect,
                Peer = peer,
                RemoteEndPoint = peer.RemoteEndPoint
            });
        }

        private void CreateErrorEvent(IPEndPoint endPoint, SocketError error)
        {
            EnqueueEvent(new TCPNetEvent
            {
                Type = TCPNetEvent.EType.Error,
                RemoteEndPoint = endPoint,
                ErrorCode = error
            });
        }
        

        public void PollEvents()
        {
            while (_pendingEvents.TryDequeue(out var evt))
            {
                switch (evt.Type)
                {
                    case TCPNetEvent.EType.Connect:
                        if (_connections.TryGetValue(evt.Peer.Id, out var connectedConn))
                        {
                            OnClientConnected?.Invoke(connectedConn);
                        }
                        break;

                    case TCPNetEvent.EType.Disconnect:
                        if (_connections.TryRemove(evt.Peer.Id, out var disconnectedConn))
                        {
                            OnClientDisconnected?.Invoke(disconnectedConn);
                        }
                        break;

                    case TCPNetEvent.EType.Receive:
                        try
                        {
                            if (evt.Packet != null && _connections.TryGetValue(evt.Peer.Id, out var receiveConn))
                            {
                                NetMsgProcessor.Instance.ProcessMessage(
                                    receiveConn,
                                    evt.CmdId,
                                    evt.RpcSeq,
                                    evt.Packet.RawData.AsSpan(0, evt.Packet.Size));
                            }
                        }
                        finally
                        {
                            if (evt.Packet != null)
                            {
                                PoolRecyclePacket(evt.Packet);
                            }
                        }
                        break;

                    case TCPNetEvent.EType.Error:
                        if (evt.RemoteEndPoint != null)
                        {
                            NetLogger.Error($"[TCP] Network error from {evt.RemoteEndPoint}: {evt.ErrorCode}");
                        }
                        break;
                }
            }
        }
        
        private void EnqueueEvent(TCPNetEvent evt)
        {
            _pendingEvents.Enqueue(evt);
        }

        private void ClearPendingEvents()
        {
            while (_pendingEvents.TryDequeue(out var evt))
            {
                if (evt.Packet != null)
                {
                    PoolRecyclePacket(evt.Packet);
                }
            }
        }
        
        internal void PoolRecyclePacket(TCPNetPacket packet) => _packetPool.Recycle(packet);
        internal TCPNetPacket PoolGetPacket(int size) => _packetPool.Get(size);
        
        
    }
}
