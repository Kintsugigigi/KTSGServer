using System;
using Google.Protobuf;

namespace KTSG.Network
{
    public abstract class MsgHandler<TMsg> : IMsgHandler 
        where TMsg : IMessage
    {
        public void Handle(INetConnection conn, IMessage msg, ushort rpcSeq)
        {
            try
            {
                Run(conn, (TMsg)msg, rpcSeq);
            }
            catch (Exception e)
            {
                MyLog.Error($"[MsgHandler] 业务异常: {typeof(TMsg).Name}\n{e}");
            }
        }
        
        protected abstract void Run(INetConnection conn, TMsg msg, ushort rpcSeq);
    }
}