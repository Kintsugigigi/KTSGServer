using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqRoleRevivePointHandler : ReqHandler<ReqRoleRevivePoint, RspRoleRevivePoint>
    {
        protected override void Run(INetConnection conn, ReqRoleRevivePoint req, RspRoleRevivePoint rsp, ushort rpcSeq)
        {
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            if (!roleService.TryGetCurrentRoleRevivePoint(conn.ConnectionId, out var revivePoint, out var reason))
            {
                rsp.Result = new Result
                {
                    Code = ResultCode.Fail,
                    Msg = reason
                };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result
            {
                Code = ResultCode.Success,
                Msg = "success"
            };
            rsp.RevivePoint = revivePoint;
            Reply(conn, rsp, rpcSeq);
        }
    }
}
