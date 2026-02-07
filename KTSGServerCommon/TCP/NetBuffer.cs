using System;
using LiteNetLib.Utils;

namespace KTSG.Network
{
    public class NetBuffer
    {
        public byte[] RawData { get; private set; }
        public int WritePosition { get; private set; }
        public int ReadPosition { get; private set; }
        
        private int _reallocThreshold;

        public NetBuffer(int capacity,int threshold)
        {
            RawData = new byte[capacity];
            _reallocThreshold = threshold;
        }
        
        public int AvailableBytes => WritePosition - ReadPosition;
        
        public ArraySegment<byte> GetWriteSegment()
        {
            if (RawData.Length - WritePosition < _reallocThreshold)
            {
                ResizeOrCompact();
            }
            return new ArraySegment<byte>(RawData, WritePosition, RawData.Length - WritePosition);
        }
        
        public void CommitWrite(int bytesWritten)
        {
            WritePosition += bytesWritten;
        }


        public ReadOnlySpan<byte> PeekSpan()
        {
            return new ReadOnlySpan<byte>(RawData, ReadPosition, AvailableBytes);
        }
        
        
        public void Consume(int bytesConsumed)
        {
            ReadPosition += bytesConsumed;
            
            if (ReadPosition == WritePosition)
            {
                ReadPosition = 0;
                WritePosition = 0;
            }
        }

        /// <summary>
        /// 内存整理
        /// </summary>
        private void ResizeOrCompact()
        {
            int validCount = AvailableBytes;
            
            // 如果有效数据太多（超过一半），装不下，则扩容
            if (validCount > RawData.Length / 2 && RawData.Length - validCount < _reallocThreshold)
            {
                int newSize = RawData.Length * 2;
                if (newSize > 10 * 1024 * 1024) throw new Exception("NetBuffer Overflow");
                
                byte[] newArr = new byte[newSize];
                Buffer.BlockCopy(RawData, ReadPosition, newArr, 0, validCount);
                RawData = newArr;
            }
            else
            {
                if (validCount > 0)
                {
                    Buffer.BlockCopy(RawData, ReadPosition, RawData, 0, validCount);
                }
            }
            
            ReadPosition = 0;
            WritePosition = validCount;
        }
    }
}