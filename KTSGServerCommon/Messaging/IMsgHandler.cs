using Google.Protobuf;

namespace KTSG.Network
{
    public interface IMsgHandler
    {
        void Handle(INetConnection conn, IMessage msg, ushort rpcSeq = 0);
    }
}