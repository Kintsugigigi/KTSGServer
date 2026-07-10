using System;
using System.Buffers.Binary;
using LiteNetLib.Utils;

namespace KTSG.Network
{
    public static class UdpProtocol
    {
        public const ushort MagicNum = 0x4517;
        public const int HeaderSize = 8;

        public static bool ReadHeader(ReadOnlySpan<byte> input, out uint cmdId, out ushort seq, out ReadOnlySpan<byte> body)
        {
            cmdId = 0; seq = 0; body = ReadOnlySpan<byte>.Empty;
            if (input.Length < HeaderSize) return false;

            ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(0, 2));
            if (magic != MagicNum) return false;
            cmdId = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(2, 4));
            seq = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(6, 2));
            body = input.Slice(HeaderSize);
            return true;
        }

        public static void WriteHeader(NetDataWriter writer, uint cmdId, ushort seq)
        {
            writer.EnsureFit(HeaderSize);

            byte[] data = writer.Data;
            int pos = writer.Length;

            FastBitConverter.GetBytes(data, pos + 0, MagicNum);
            FastBitConverter.GetBytes(data, pos + 2, (int)cmdId);
            FastBitConverter.GetBytes(data, pos + 6, (short)seq);

            writer.SetPosition(pos + HeaderSize);
        }
    }
}
