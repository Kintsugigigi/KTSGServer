using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqRegisterHandler : ReqHandler<ReqRegister, RspRegister>
    {
        protected override void Run(INetConnection conn, ReqRegister req, RspRegister rsp, ushort rpcSeq)
        {
            var auth = ServerRuntime.Instance.GetService<LoginService>().Register(req.Account, req.Password);

            if (auth.Code == ResultCode.Success)
            {
                NetLogger.Info($"[Register] Success. ConnectionId={conn.ConnectionId}, Username={req.Account}, Uid={auth.UserUid}");
            }
            else
            {
                NetLogger.Warning($"[Register] Fail. ConnectionId={conn.ConnectionId}, Username={req.Account}, Reason={auth.Msg}");
            }

            rsp.Result = new Result
            {
                Code = auth.Code,
                Msg = auth.Msg
            };
            Reply(conn, rsp, rpcSeq);
        }
    }
}
