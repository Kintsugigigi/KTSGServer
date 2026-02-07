using LiteNetLib;
using LiteNetLib.Utils;
using Google.Protobuf;
using System;

namespace KTSG.Network
{
    public static class LiteNetPeerExtensions
    {
        [ThreadStatic]
        private static NetDataWriter _udpWriter;
        
        public static void SendProtobuf(this NetPeer peer, ushort cmdId, IMessage msg, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
        {
            if (peer == null || peer.ConnectionState != ConnectionState.Connected) return;

            if (_udpWriter == null) _udpWriter = new NetDataWriter();
            _udpWriter.Reset();
            
            UdpProtocol.WriteHeader(_udpWriter, cmdId, 0);
            
            int bodySize = msg.CalculateSize();
            
            _udpWriter.ResizeIfNeed(_udpWriter.Length + bodySize);
            
            Span<byte> bodySpan = _udpWriter.Data.AsSpan(_udpWriter.Length, bodySize);
            
            msg.WriteTo(bodySpan);
            
            _udpWriter.SetPosition(_udpWriter.Length + bodySize);
            
            peer.Send(_udpWriter, method);
        }
    }
}