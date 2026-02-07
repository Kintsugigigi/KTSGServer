using System.Buffers.Binary;
using LiteNetLib.Utils;

public static class UdpProtocol
{
    public const ushort MagicNum = 0x4517;
    public const int HeaderSize = 8; 
    
    public static void ReadHeader(ReadOnlySpan<byte> span, out ushort magic, out uint cmdId, out ushort seq)
    {
        magic = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(0, 2));
        cmdId = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(2, 4)); // [改为UInt32]
        seq   = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6, 2));
    }
    
    public static void WriteHeader(NetDataWriter writer, uint cmdId, ushort seq)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(0, 2), MagicNum);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(2, 4), cmdId); // [改为UInt32]
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(6, 2), seq);
        writer.Put(header);
    }
}