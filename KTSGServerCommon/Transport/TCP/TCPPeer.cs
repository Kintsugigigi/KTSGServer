using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using LiteNetLib;
using LiteNetLib.Utils;

namespace KTSG.Network
{
    public class TCPPeer
    {
        public readonly int Id;
        public IPEndPoint RemoteEndPoint { get; private set; }
        private int _isDisconnected;
        public bool IsConnected => Volatile.Read(ref _isDisconnected) == 0;
        
        private readonly Socket _socket;
        private readonly TCPNetManager _manager;
        private readonly NetBuffer _receiveBuffer;
        
        private readonly ConcurrentQueue<TCPNetPacket> _sendQueue = new ConcurrentQueue<TCPNetPacket>();
        private readonly SemaphoreSlim _sendSignal = new SemaphoreSlim(0);
        private readonly Task _sendLoopTask;
        public TCPPeer(int id, Socket socket, TCPNetManager manager)
        {
            Id = id;
            _socket = socket;
            _manager = manager;
            
            _receiveBuffer = new NetBuffer(NetConstants.SocketBufferSize, NetConstants.MaxPacketSize);
            
            try 
            {
                RemoteEndPoint = (IPEndPoint)socket.RemoteEndPoint; 
            } 
            catch { }

            _sendLoopTask = RunSendLoopAsync();
        }

        internal void StartReceiveLoop()
        {
            _ = ReceiveLoop();
        }

        private async Task ReceiveLoop()
        {
            try
            {
                while (IsConnected)
                {
                    var segment = _receiveBuffer.GetWriteSegment();
                    int bytesReceived = await _socket.ReceiveAsync(
                        new Memory<byte>(segment.Array, segment.Offset, segment.Count), 
                        SocketFlags.None);

                    if (bytesReceived == 0) throw new SocketException();
                    _receiveBuffer.CommitWrite(bytesReceived);
                    ProcessBuffer();
                }
            }
            catch
            {
                Disconnect(DisconnectReason.ConnectionFailed);
            }
        }

        private void ProcessBuffer()
        {
            while (true)
            {
                var bufferSpan = _receiveBuffer.PeekSpan();
                if (bufferSpan.Length < TcpProtocol.LenSize) 
                    break;
                
                var readResult = TcpProtocol.ReadHeader(bufferSpan, out uint cmdId, out ushort seq, out var bodySpan);
                if (readResult == TcpHeaderReadResult.Success)
                {
                    int totalPacketSize = TcpProtocol.TCPHeaderSize + bodySpan.Length;
                    TCPNetPacket packet = _manager.PoolGetPacket(bodySpan.Length);
                    bodySpan.CopyTo(packet.RawData);
                    packet.Size = bodySpan.Length;
                    
                    _receiveBuffer.Consume(totalPacketSize);
                    _manager.OnTcpPacketReceived(this, cmdId, seq, packet);
                }
                else if (readResult == TcpHeaderReadResult.Incomplete)
                {
                    break;
                }
                else
                {
                    Disconnect(DisconnectReason.InvalidProtocol, SocketError.NoData);
                    break;
                }
            }
        }
        
        

        internal bool TrySend(uint cmdId, IMessage msg, in SendOptions options = default)
        {
            if (!IsConnected)
            {
                return false;
            }

            int bodyLen = msg.CalculateSize();
            int totalPacketSize = TcpProtocol.TCPHeaderSize + bodyLen;

            TCPNetPacket packet = _manager.PoolGetPacket(totalPacketSize);
            packet.Size = totalPacketSize;

            TcpProtocol.WriteHeader(packet.RawData, 0, cmdId, options.RpcSeq, bodyLen);
            msg.WriteTo(packet.RawData.AsSpan(TcpProtocol.TCPHeaderSize, bodyLen));

            SendInternal(packet);
            return true;
        }
        

        public void SendInternal(TCPNetPacket packet)
        {
            if (!IsConnected)
            {
                _manager.PoolRecyclePacket(packet);
                return;
            }
            _sendQueue.Enqueue(packet);
            _sendSignal.Release();
        }
        
        private async Task RunSendLoopAsync()
        {
            try
            {
                while (true)
                {
                    if (!IsConnected && _sendQueue.IsEmpty)
                    {
                        break;
                    }

                    await _sendSignal.WaitAsync();

                    if (!IsConnected && _sendQueue.IsEmpty)
                    {
                        break;
                    }

                    while (_sendQueue.TryDequeue(out var packet))
                    {
                        if (!IsConnected)
                        {
                            _manager.PoolRecyclePacket(packet);
                            continue;
                        }

                        try
                        {
                            await _socket.SendAsync(new ArraySegment<byte>(packet.RawData, 0, packet.Size), SocketFlags.None);
                        }
                        finally
                        {
                            _manager.PoolRecyclePacket(packet);
                        }
                    }

                    if (!IsConnected && _sendQueue.IsEmpty)
                    {
                        break;
                    }
                }
            }
            catch
            {
                Disconnect(DisconnectReason.ConnectionFailed);
            }
        }

        public void Disconnect(DisconnectReason reason, SocketError error = SocketError.Success)
        {
            if (Interlocked.CompareExchange(ref _isDisconnected, 1, 0) != 0)
            {
                return;
            }

            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
            

            while (_sendQueue.TryDequeue(out var packet))
            {
                _manager.PoolRecyclePacket(packet);
            }

            _sendSignal.Release();
            
            _manager.OnPeerDisconnected(this, reason, error);
        }
    }
}
