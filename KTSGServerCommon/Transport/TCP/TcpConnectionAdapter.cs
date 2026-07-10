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
        
        public int ConnectionId => _peer.Id;
        public TransportKind TransportKind => TransportKind.Tcp;
        public IPEndPoint RemoteEndPoint => _peer.RemoteEndPoint;
        public bool IsConnected => _peer.IsConnected;

        public bool TrySend(uint cmdId, IMessage msg, in SendOptions options = default)
        {
            if (!_peer.IsConnected)
            {
                return false;
            }

            return _peer.TrySend(cmdId, msg, options);
        }

        public void Disconnect()
        {
            _peer.Disconnect(DisconnectReason.DisconnectPeerCalled);
        }
    }
}
