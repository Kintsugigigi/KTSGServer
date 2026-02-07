using System.Collections.Generic;

namespace KTSG.Network
{
    public class TCPNetPacketPool
    {

        private const int MaxPoolCount = 1000; 
        private const int MaxRecordSize = 4096; 

        private readonly Stack<TCPNetPacket> _pool = new Stack<TCPNetPacket>();
        private readonly object _lock = new object();
        

        internal TCPNetPacket Get(int requestSize)
        {
            TCPNetPacket packet = null;
            
            lock (_lock)
            {
                if (_pool.Count > 0)
                {
                    packet = _pool.Pop();
                }
            }
            
            if (packet == null)
            {
                packet = new TCPNetPacket(requestSize);
            }
            packet.Realloc(requestSize);
            
            return packet;
        }
        
        internal void Recycle(TCPNetPacket packet)
        {
            if (packet == null) return;
            
            if (packet.RawData.Length > MaxRecordSize)
            {
                return; 
            }

            lock (_lock)
            {
                if (_pool.Count >= MaxPoolCount)
                {
                    return;
                }
                
                _pool.Push(packet);
            }
        }
    }
}