using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using KTSG.Network;
using LiteNetLib;
using LiteNetLib.Utils;

namespace KTSG.Network
{
    public class TCPNetManager
    {

        private readonly TCPNetPacketPool _packetPool = new TCPNetPacketPool();
        private readonly ITCPEventListener _eventListener;
        
        private readonly ConcurrentDictionary<int, TCPPeer> _connectedPeers = new ConcurrentDictionary<int, TCPPeer>();
        private int _nextPeerId = 0; 
        private volatile bool _isRunning;
        
        private long _connectedPeersCount;
        public int ConnectedPeersCount => (int)Interlocked.Read(ref _connectedPeersCount);
        
        private Socket _listenerSocket;
        
        private readonly object _eventLock = new object();
        private TCPNetEvent _netEventPoolHead; // 空闲池头
        private TCPNetEvent _pendingEventHead; // 待处理队列头
        private TCPNetEvent _pendingEventTail; // 待处理队列尾

        public TCPNetManager(ITCPEventListener listener)
        {
            _eventListener = listener;
        }
        

        /// <summary>
        /// 启动服务端监听
        /// </summary>
        public bool StartServer(int port)
        {
            if (_isRunning) return false;
            try
            {
                _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _listenerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenerSocket.Bind(new IPEndPoint(IPAddress.Any, port));
                _listenerSocket.Listen(512); 
                
                _isRunning = true;
                _ = AcceptLoop(); 
                
                MyLog.Debug($"[TCP] Server started on port {port}");
                return true;
            }
            catch (Exception e)
            {
                MyLog.Error($"[TCP] StartServer failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 客户端连接 (异步)
        /// </summary>
        public async Task<TCPPeer> Connect(string address, int port)
        {
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(address, port);

                _isRunning = true;
                return CreateAndAddPeer(socket);
            }
            catch (Exception e)
            {
                MyLog.Error($"[TCP] Connect failed: {e.Message}");
                
                if (socket != null)
                {
                    try { socket.Close(); } catch { }
                    try { socket.Dispose(); } catch { }
                }
                
                CreateErrorEvent(new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0), SocketError.ConnectionRefused);
                return null;
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
            _connectedPeers.Clear();
            Interlocked.Exchange(ref _connectedPeersCount, 0);

            ClearPendingEvents();
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
                    if (_isRunning) MyLog.Error($"[TCP] Accept error: {e.Message}");
                }
            }
        }

        private TCPPeer CreateAndAddPeer(Socket socket)
        {
            if (!_isRunning) return null;

            int id = Interlocked.Increment(ref _nextPeerId);
            var peer = new TCPPeer(id, socket, this);

            if (_connectedPeers.TryAdd(id, peer))
            {
                Interlocked.Increment(ref _connectedPeersCount);
                CreateConnectEvent(peer);
                return peer;
            }
            else
            {
                peer.Disconnect(DisconnectReason.ConnectionFailed);
                return null;
            }
        }



        internal void OnTcpPacketReceived(TCPPeer peer, uint cmdId, ushort rpcSeq, TCPNetPacket packet)
        {
            var evt = GetFreeEvent(TCPNetEvent.EType.Receive);
            evt.Peer = peer;
            evt.RemoteEndPoint = peer.RemoteEndPoint;
            evt.CmdId = cmdId;
            evt.RpcSeq = rpcSeq;
            evt.DataReader.SetSource(packet);
            EnqueueEvent(evt);
        }

        internal void OnPeerDisconnected(TCPPeer peer, DisconnectReason reason, SocketError error)
        {
            if (_connectedPeers.TryRemove(peer.Id, out _))
            {
                Interlocked.Decrement(ref _connectedPeersCount);
                var evt = GetFreeEvent(TCPNetEvent.EType.Disconnect);
                evt.Peer = peer;
                evt.DisconnectReason = reason;
                evt.ErrorCode = error;
                EnqueueEvent(evt);
            }
        }

        private void CreateConnectEvent(TCPPeer peer)
        {
            var evt = GetFreeEvent(TCPNetEvent.EType.Connect);
            evt.Peer = peer;
            evt.RemoteEndPoint = peer.RemoteEndPoint;
            EnqueueEvent(evt);
        }

        private void CreateErrorEvent(IPEndPoint endPoint, SocketError error)
        {
            var evt = GetFreeEvent(TCPNetEvent.EType.Error);
            evt.RemoteEndPoint = endPoint;
            evt.ErrorCode = error;
            EnqueueEvent(evt);
        }
        

        public void PollEvents()
        {
            if (_pendingEventHead == null) return;

            TCPNetEvent evt;
            
            lock (_eventLock)
            {
                evt = _pendingEventHead;
                _pendingEventHead = null;
                _pendingEventTail = null;
            }

            while (evt != null)
            {
                var next = evt.Next;
                ProcessEvent(evt);
                evt.DataReader.RecycleInternal();
                evt = next;
            }
        }
        
        private void ProcessEvent(TCPNetEvent evt)
        {
            switch (evt.Type)
            {
                case TCPNetEvent.EType.Connect:
                    _eventListener.OnPeerConnected(evt.Peer);
                    break;

                case TCPNetEvent.EType.Receive:
                    _eventListener.OnNetworkReceive(evt.Peer, evt.CmdId, evt.RpcSeq, evt.DataReader);
                    break;

                case TCPNetEvent.EType.Disconnect:
                    var info = new DisconnectInfo
                    {
                        Reason = evt.DisconnectReason,
                        SocketErrorCode = evt.ErrorCode,
                        AdditionalData = null 
                    };
                    _eventListener.OnPeerDisconnected(evt.Peer, info);
                    break;

                case TCPNetEvent.EType.Error:
                    _eventListener.OnNetworkError(evt.RemoteEndPoint, evt.ErrorCode);
                    break;
            }
        }
        

        private TCPNetEvent GetFreeEvent(TCPNetEvent.EType type)
        {
            TCPNetEvent evt;
            lock (_eventLock)
            {
                if (_netEventPoolHead != null)
                {
                    evt = _netEventPoolHead;
                    _netEventPoolHead = evt.Next;
                }
                else
                {
                    evt = new TCPNetEvent(this);
                }
            }
            evt.Next = null;
            evt.Type = type;
            evt.CmdId = 0; 
            return evt;
        }

        private void EnqueueEvent(TCPNetEvent evt)
        {
            lock (_eventLock)
            {
                if (_pendingEventTail == null)
                {
                    _pendingEventHead = evt;
                }
                else
                {
                    _pendingEventTail.Next = evt;
                }
                _pendingEventTail = evt;
            }
        }

        internal void RecycleEvent(TCPNetEvent evt)
        {
            evt.Clean(); 
            lock (_eventLock)
            {
                evt.Next = _netEventPoolHead;
                _netEventPoolHead = evt;
            }
        }
        
        private void ClearPendingEvents()
        {
            lock (_eventLock)
            {
                var evt = _pendingEventHead;
                while (evt != null)
                {
                    var next = evt.Next;
                    RecycleEvent(evt);
                    evt = next;
                }
                _pendingEventHead = null;
                _pendingEventTail = null;
            }
        }
        
        internal void PoolRecyclePacket(TCPNetPacket packet) => _packetPool.Recycle(packet);
        internal TCPNetPacket PoolGetPacket(int size) => _packetPool.Get(size);
    }
}