using System.Net;
using Google.Protobuf;

namespace KTSG.Network
{
    public interface INetConnection
    {
        int Id { get; }
        IPEndPoint RemoteEndPoint { get; }
        bool IsConnected { get; }
        
        void Send(uint cmdId, IMessage msg, ushort seq = 0);
        
        void Disconnect();
        object RawPeer { get; }
    }
}