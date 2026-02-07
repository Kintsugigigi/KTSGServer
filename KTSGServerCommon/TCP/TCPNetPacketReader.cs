using LiteNetLib.Utils;

namespace KTSG.Network
{
    public sealed class TCPNetPacketReader : NetDataReader
    {
        private TCPNetPacket _packet;
        private readonly TCPNetManager _manager;

        internal TCPNetPacketReader(TCPNetManager manager, TCPNetEvent evt)
        {
            _manager = manager;
        }
        
        internal void SetSource(TCPNetPacket packet)
        {
            if (packet == null) return;
            _packet = packet;
            SetSource(packet.RawData, 0, packet.Size);
        }
        
        internal void RecycleInternal()
        {
            Clear(); 
            if (_packet != null)
            {
                _manager.PoolRecyclePacket(_packet);
                _packet = null;
            }
        }
    }
}