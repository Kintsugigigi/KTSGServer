using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace KTSG.Network
{
    public struct TCPNetEvent
    {
        public enum EType
        {
            Connect,
            Disconnect,
            Receive,
            Error
        }

        public EType Type;
        public TCPPeer Peer;
        public IPEndPoint RemoteEndPoint;
        public SocketError ErrorCode;
        public DisconnectReason DisconnectReason;

        public uint CmdId; 
        public ushort RpcSeq; 
        public TCPNetPacket Packet;
    }
}
