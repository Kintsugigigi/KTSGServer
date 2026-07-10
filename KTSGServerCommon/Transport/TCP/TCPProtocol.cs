using System;
using System.Buffers.Binary; 
using LiteNetLib.Utils;      

namespace KTSG.Network
{
    public enum TcpHeaderReadResult
    {
        Success,
        Incomplete,
        Invalid,
    }

    public static class TcpProtocol
    {
        public const ushort MagicNum = 0x4517;
        // [修改] 长度字段占 4 字节
        public const int LenSize = sizeof(uint); 
        // [修改] 总头部大小为 12 字节
        // 结构: Length(4) + Magic(2) + CmdId(4) + Seq(2)
        public const int TCPHeaderSize = 12; 
        
        public static void WriteHeader(byte[] buffer, int offset, uint cmdId, ushort seq, int bodyLength)
        {
            // Magic(2) + CmdId(4) + Seq(2) + Body = 8 + bodyLength
            uint dataLen = (uint)(8 + bodyLength);
            FastBitConverter.GetBytes(buffer, offset + 0, dataLen);
            FastBitConverter.GetBytes(buffer, offset + 4, MagicNum);
            FastBitConverter.GetBytes(buffer, offset + 6, cmdId); 
            FastBitConverter.GetBytes(buffer, offset + 10, (short)seq);
        }
        
        public static TcpHeaderReadResult ReadHeader(ReadOnlySpan<byte> input, out uint cmdId, out ushort seq, out ReadOnlySpan<byte> body)
        {
            cmdId = 0; seq = 0; body = ReadOnlySpan<byte>.Empty;
            if (input.Length < TCPHeaderSize) return TcpHeaderReadResult.Incomplete;
            uint dataLen = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(0, 4));
            if (dataLen < 8) return TcpHeaderReadResult.Invalid;
            if (input.Length < LenSize + dataLen) return TcpHeaderReadResult.Incomplete;
            ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(4, 2));
            if (magic != MagicNum) return TcpHeaderReadResult.Invalid;
            cmdId = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(6, 4)); 
            seq = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(10, 2));
            body = input.Slice(TCPHeaderSize, (int)dataLen - 8);
            return TcpHeaderReadResult.Success;
        }
    }
}
