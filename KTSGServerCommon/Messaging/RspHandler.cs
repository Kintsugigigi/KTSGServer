using System;
using Google.Protobuf;
using KTSG.Proto; // 引用 MsgMeta

namespace KTSG.Network
{

    public abstract class RspHandler<TMsg> : IMsgHandler where TMsg : IMessage
    {
        public void Handle(INetConnection conn, IMessage msg, ushort rpcSeq)
        {
            if (rpcSeq > 0)
            {
                if (!NetMsgProcessor.Instance.TryGetCmdId(typeof(TMsg), out uint currentCmdId))
                {
                NetLogger.Error($"[RspHandler] 警告： 不存在的cmdID注册 {typeof(TMsg).Name}");
                    return;
                }
                
                if (NetMsgProcessor.Instance.InvokeRpcCallback(rpcSeq, currentCmdId, msg))
                {
                    try
                    {
                        Run(conn, (TMsg)msg);
                    }
                    catch (Exception e)
                    {
                NetLogger.Error($"[RspHandler] 业务异常: {typeof(TMsg).Name}\n{e}");
                    }
                }
            }
        }
        
        /// <summary>
        /// 业务逻辑入口 (仅在 RPC 校验通过且成功唤醒 await 后执行)
        /// </summary>
        protected virtual void Run(INetConnection conn, TMsg msg)
        {
        }
    }
}
