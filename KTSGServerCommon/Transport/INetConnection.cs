using System.Net;
using Google.Protobuf;

namespace KTSG.Network
{
    public enum TransportKind : byte
    {
        Tcp = 0,
        Udp = 1,
    }

    public enum NetDeliveryMode : byte
    {
        ReliableUnordered = 0,
        Sequenced = 1,
        ReliableOrdered = 2,
        ReliableSequenced = 3,
        Unreliable = 4,
    }

    public readonly record struct SendOptions(
        byte ChannelId = 0,
        NetDeliveryMode DeliveryMode = NetDeliveryMode.ReliableOrdered,
        ushort RpcSeq = 0);

    public interface INetConnection
    {
        int ConnectionId { get; }
        TransportKind TransportKind { get; }
        IPEndPoint RemoteEndPoint { get; }
        bool IsConnected { get; }
        bool TrySend(uint cmdId, IMessage msg, in SendOptions options = default);
        void Disconnect();
    }
}
