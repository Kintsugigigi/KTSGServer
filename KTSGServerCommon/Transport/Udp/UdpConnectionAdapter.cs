using System.Net;
using Google.Protobuf;
using LiteNetLib;
using LiteNetLib.Utils;

namespace KTSG.Network
{
    public class UdpConnectionAdapter : INetConnection
    {
        private readonly NetPeer _peer;

        [ThreadStatic]
        private static NetDataWriter _cachedWriter;

        public UdpConnectionAdapter(NetPeer peer)
        {
            _peer = peer;
        }

        public int ConnectionId => _peer.Id;
        public TransportKind TransportKind => TransportKind.Udp;
        public IPEndPoint RemoteEndPoint => _peer;

        public bool IsConnected => _peer.ConnectionState == ConnectionState.Connected;

        public bool TrySend(uint cmdId, IMessage msg, in SendOptions options = default)
        {
            if (_peer.ConnectionState != ConnectionState.Connected)
            {
                return false;
            }

            var writer = _cachedWriter ??= new NetDataWriter();
            writer.Reset();

            int bodyLen = msg.CalculateSize();
            UdpProtocol.WriteHeader(writer, cmdId, options.RpcSeq);
            writer.ResizeIfNeed(writer.Length + bodyLen);
            msg.WriteTo(writer.Data.AsSpan(writer.Length, bodyLen));
            writer.SetPosition(writer.Length + bodyLen);

            _peer.Send(writer, options.ChannelId, ToLiteNetDeliveryMethod(options.DeliveryMode));
            return true;
        }

        public void Disconnect() => _peer.Disconnect();

        private static DeliveryMethod ToLiteNetDeliveryMethod(NetDeliveryMode deliveryMode)
        {
            return deliveryMode switch
            {
                NetDeliveryMode.ReliableUnordered => DeliveryMethod.ReliableUnordered,
                NetDeliveryMode.Sequenced => DeliveryMethod.Sequenced,
                NetDeliveryMode.ReliableOrdered => DeliveryMethod.ReliableOrdered,
                NetDeliveryMode.ReliableSequenced => DeliveryMethod.ReliableSequenced,
                NetDeliveryMode.Unreliable => DeliveryMethod.Unreliable,
                _ => DeliveryMethod.ReliableOrdered,
            };
        }
    }
}
