using System.Net;
using Google.Protobuf;
using LiteNetLib;   

namespace KTSG.Network
{
    public class TcpConnectionAdapter : INetConnection
    {
        private readonly TCPPeer _peer;

        public TcpConnectionAdapter(TCPPeer peer)
        {
            _peer = peer;
        }
        
        public int Id => _peer.Id;
        public IPEndPoint RemoteEndPoint => _peer.RemoteEndPoint;
        public bool IsConnected => _peer.IsConnected;
        public object RawPeer => _peer;
        
        public void Send(uint cmdId, IMessage msg, ushort seq = 0)
        {
            _peer.Send(cmdId, seq, msg);
        }

        public void Disconnect()
        {
            _peer.Disconnect(DisconnectReason.DisconnectPeerCalled);
        }
    }
}