using System;
using Google.Protobuf;
using KTSG.Proto;


namespace KTSG.Network
{
    public abstract class ReqHandler<TReq, TRsp> : IMsgHandler
        where TReq : IMessage
        where TRsp : IMessage, new() 
    {

        public void Handle(INetConnection conn, IMessage msg, ushort rpcSeq)
        {
            if (!MsgMeta.TypeToItem.TryGetValue(typeof(TRsp), out _))
            {
                NetLogger.Error($"[RPC] 无法找到响应类型 {typeof(TRsp).Name} 的 CmdId，无法回包！");
                return;
            }

            if (rpcSeq <= 0)
            {
                NetLogger.Error($"[RPC] RpcSeq 数值非法，无法回包！");
                return;
            }
            
            var rsp = new TRsp();
            try
            {
                Run(conn, (TReq)msg, rsp, rpcSeq);
            }
            catch (Exception e)
            {
                NetLogger.Error($"[RPC] {typeof(TReq).Name} 业务异常: {e}");
            }
        }
        
        protected abstract void Run(INetConnection conn, TReq req, TRsp rsp, ushort rpcSeq);

        protected void Reply(INetConnection conn, TRsp rsp, ushort rpcSeq)
        {
            if (!MsgMeta.TypeToItem.TryGetValue(typeof(TRsp), out var item))
            {
                NetLogger.Error($"[RPC] 无法找到响应类型 {typeof(TRsp).Name} 的 CmdId，无法回包！");
                return;
            }

            if (rpcSeq == 0)
            {
                NetLogger.Error($"[RPC] RpcSeq 数值非法，无法回包！");
                return;
            }

            conn.TrySend(item.CmdId, rsp, new SendOptions(RpcSeq: rpcSeq));
        }
    }
}
