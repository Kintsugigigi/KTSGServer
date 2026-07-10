using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqSaveRoleRevivePointHandler : ReqHandler<ReqSaveRoleRevivePoint, RspRoleRevivePoint>
    {
        protected override void Run(INetConnection conn, ReqSaveRoleRevivePoint req, RspRoleRevivePoint rsp, ushort rpcSeq)
        {
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            NRoleRevivePoint reqRevivePoint = req?.RevivePoint;
            if (reqRevivePoint == null)
            {
                rsp.Result = new Result
                {
                    Code = ResultCode.Fail,
                    Msg = "revive point is null"
                };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            if (!roleService.TrySaveCurrentRoleRevivePoint(
                    conn.ConnectionId,
                    reqRevivePoint.SceneCid,
                    reqRevivePoint.RevivePointCid,
                    out var savedRevivePoint,
                    out var reason))
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
            rsp.RevivePoint = savedRevivePoint;
            Reply(conn, rsp, rpcSeq);
        }
    }
}
