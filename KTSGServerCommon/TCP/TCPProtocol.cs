using System.Buffers.Binary;
using LiteNetLib.Utils;

public static class TcpProtocol
{
    public const ushort MagicNum = 0x4517;
    public const int LenSize   = sizeof(ushort); // 2
    public const int TCPHeaderSize = LenSize + 8; // 2(Magic)+4(CmdID)+2(RPCSeq)

    public static void WriteHeader(NetDataWriter writer, uint cmdId, ushort seq, int bodyLength)
    {
        ushort dataLen = (ushort)(8 + bodyLength);
        Span<byte> header = stackalloc byte[TCPHeaderSize];

        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(0, 2), dataLen);
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), MagicNum);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), cmdId); 
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(8, 2), seq);

        writer.Put(header);
    }
    
    public static bool ReadHeader(ReadOnlySpan<byte> input, out uint cmdId, out ushort seq, out ReadOnlySpan<byte> body)
    {
        cmdId = 0; seq = 0; body = ReadOnlySpan<byte>.Empty;
        if (input.Length < TCPHeaderSize) return false;

        ushort dataLen = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(0, 2));
        if (input.Length < LenSize + dataLen) return false;

        ushort magic = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(2, 2));
        if (magic != MagicNum) return false;
        
        cmdId = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(4, 4)); 
        seq = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(8, 2));
        body = input.Slice(TCPHeaderSize, dataLen - 8);
        return true;
    }
}