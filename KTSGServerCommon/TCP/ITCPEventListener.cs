using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace KTSG.Network
{
    public interface ITCPEventListener
    {
        void OnPeerConnected(TCPPeer peer);
        void OnPeerDisconnected(TCPPeer peer, DisconnectInfo info);
        void OnNetworkReceive(TCPPeer peer, uint cmdId, ushort rpcSeq, TCPNetPacketReader reader);
        void OnNetworkError(IPEndPoint endPoint, SocketError socketError);
    }
}