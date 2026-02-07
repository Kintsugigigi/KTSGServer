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
            if (!MsgMeta.TypeToItem.TryGetValue(typeof(TRsp), out MsgMeta.MsgMetaItem item))
            {
                MyLog.Error($"[RPC] 无法找到响应类型 {typeof(TRsp).Name} 的 CmdId，无法回包！");
                return;
            }

            if (rpcSeq <= 0)
            {
                MyLog.Error($"[RPC] RpcSeq 数值非法，无法回包！");
                return;
            }
            
            var rspCmdId = item.CmdId;
            
            var rsp = new TRsp();
            bool isReplied = false;
            
            void Reply()
            {
                if (isReplied) return;
                isReplied = true;
                conn.Send(rspCmdId, rsp, rpcSeq);
            }

            try
            {
                Run(conn, (TReq)msg, rsp, Reply);
            }
            catch (Exception e)
            {
                MyLog.Error($"[RPC] {typeof(TReq).Name} 业务异常: {e}");
            }
        }
        
        protected abstract void Run(INetConnection conn, TReq req, TRsp rsp, Action reply);
    }
}