namespace KTSG.Network
{
    public sealed class TCPNetPacket
    {

        public byte[] RawData;
        
        public int Size;

        public TCPNetPacket(int capacity)
        {
            RawData = new byte[capacity];
            Size = 0;
        }


        public void Realloc(int size)
        {
            if (RawData.Length < size)
            {
                RawData = new byte[size];
            }
            Size = size;
        }
    }
}