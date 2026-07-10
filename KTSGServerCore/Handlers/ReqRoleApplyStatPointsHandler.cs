using System;
using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqRoleApplyStatPointsHandler : ReqHandler<ReqRoleApplyStatPoints, RspRoleApplyStatPoints>
    {
        protected override void Run(INetConnection conn, ReqRoleApplyStatPoints req, RspRoleApplyStatPoints rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason) ||
                !roleService.TryApplyStatPoints(player, role, req.DetailStatPoints, out var delta, out var statPoints, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
            rsp.Delta = delta;
            rsp.StatPoints.AddRange(statPoints);
            Reply(conn, rsp, rpcSeq);
        }
    }
}
