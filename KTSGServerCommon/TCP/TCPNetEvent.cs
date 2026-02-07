using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace KTSG.Network
{
    public sealed class TCPNetEvent
    {
        public TCPNetEvent Next;

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
        
        public readonly TCPNetPacketReader DataReader;

        public TCPNetEvent(TCPNetManager manager)
        {
            DataReader = new TCPNetPacketReader(manager, this);
        }
        
        internal void Clean()
        {
            Peer = null;
            RemoteEndPoint = null;
            Next = null;
            CmdId = 0;
            RpcSeq = 0; 
        }
    }
}