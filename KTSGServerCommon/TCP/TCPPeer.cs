using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Google.Protobuf;
using LiteNetLib;
using LiteNetLib.Utils;

namespace KTSG.Network
{
    public class TCPPeer
    {
        [ThreadStatic]
        private static NetDataWriter _cachedWriter;
        
        public readonly int Id;
        public IPEndPoint RemoteEndPoint { get; private set; }
        public bool IsConnected => _socket != null && _socket.Connected;
        
        private readonly Socket _socket;
        private readonly TCPNetManager _manager;
        private readonly NetBuffer _receiveBuffer;

        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private volatile bool _isSending;

        public TCPPeer(int id, Socket socket, TCPNetManager manager)
        {
            Id = id;
            _socket = socket;
            _manager = manager;
            
            _receiveBuffer = new NetBuffer(NetConstants.SocketBufferSize,NetConstants.MaxPacketSize);
            
            try 
            { 
                RemoteEndPoint = (IPEndPoint)socket.RemoteEndPoint; 
            } 
            catch 
            {

            }
            
            _ = ReceiveLoop();
        }

       private async Task ReceiveLoop()
        {
            try
            {
                while (IsConnected)
                {
                    var segment = _receiveBuffer.GetWriteSegment();
                    int bytesReceived = await _socket.ReceiveAsync(segment, SocketFlags.None);
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
                
                if (TcpProtocol.ReadHeader(bufferSpan, out uint cmdId, out ushort seq, out var bodySpan))
                {

                    int totalPacketSize = TcpProtocol.TCPHeaderSize + bodySpan.Length;
                    TCPNetPacket packet = _manager.PoolGetPacket(bodySpan.Length);
                    bodySpan.CopyTo(packet.RawData);
                    packet.Size = bodySpan.Length;
                    _receiveBuffer.Consume(totalPacketSize);
                    _manager.OnTcpPacketReceived(this, cmdId, seq, packet);
                }
                else
                {
                    break; 
                }
            }
        }
        
        public void Send(uint cmdId, ushort seq, IMessage msg)
        {
            if (!IsConnected) return;
            
            if (_cachedWriter == null) _cachedWriter = new NetDataWriter();
            
            _cachedWriter.Reset();
            
            int bodyLen = msg.CalculateSize();
            
            TcpProtocol.WriteHeader(_cachedWriter, cmdId, seq, bodyLen);
            
            _cachedWriter.ResizeIfNeed(_cachedWriter.Length + bodyLen);

            var span = _cachedWriter.Data.AsSpan(_cachedWriter.Length, bodyLen);

            msg.WriteTo(span);
            _cachedWriter.SetPosition(_cachedWriter.Length + bodyLen);
            
            SendInternal(_cachedWriter.CopyData());
        }
        
        public void SendInternal(byte[] data)
        {
            if (!IsConnected) return;
            _sendQueue.Enqueue(data);
            TryStartSendLoop();
        }
        

        private async void TryStartSendLoop()
        {
            if (_isSending) return;
            _isSending = true;

            try
            {
                while (_sendQueue.TryDequeue(out var data))
                {
                    if (!IsConnected) break;
                    await _socket.SendAsync(new ArraySegment<byte>(data), SocketFlags.None);
                }
            }
            catch
            {
                Disconnect(DisconnectReason.ConnectionFailed);
            }
            finally
            {
                _isSending = false;
                if (!_sendQueue.IsEmpty && IsConnected) TryStartSendLoop();
            }
        }

        public void Disconnect(DisconnectReason reason, SocketError error = SocketError.Success)
        {
            if (_socket == null || !IsConnected) return;

            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
            
            _manager.OnPeerDisconnected(this, reason, error);
        }
    }
}