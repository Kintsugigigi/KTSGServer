using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqLoginHandler : ReqHandler<ReqLogin, RspLogin>
    {
        protected override void Run(INetConnection conn, ReqLogin req, RspLogin rsp, ushort rpcSeq)
        {
            var loginService = ServerRuntime.Instance.GetService<LoginService>();
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var auth = loginService.Login(req.Account, req.Password);

            if (auth.Code == ResultCode.Success &&
                !playerService.TryBeginPendingLogin(conn.ConnectionId, auth.UserUid, out var reason))
            {
                auth = new LoginResult(ResultCode.Fail, reason);
            }

            if (auth.Code == ResultCode.Success)
            {
                NetLogger.Info($"[Login] Success. ConnectionId={conn.ConnectionId}, Username={req.Account}, Uid={auth.UserUid}");
            }
            else
            {
                NetLogger.Warning($"[Login] Fail. ConnectionId={conn.ConnectionId}, Username={req.Account}, Reason={auth.Msg}");
            }

            rsp.Result = new Result
            {
                Code = auth.Code,
                Msg = auth.Msg
            };
            rsp.UserUid = auth.Code == ResultCode.Success ? auth.UserUid : string.Empty;
            Reply(conn, rsp, rpcSeq);
        }
    }
}
