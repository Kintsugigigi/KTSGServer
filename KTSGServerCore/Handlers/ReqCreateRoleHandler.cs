using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqCreateRoleHandler : ReqHandler<ReqCreateRole, RspCreateRole>
    {
        protected override void Run(INetConnection conn, ReqCreateRole req, RspCreateRole rsp, ushort rpcSeq)
        {
            var roleService = ServerRuntime.Instance.GetService<RoleService>();

            if (!roleService.CreateRole(conn.ConnectionId, req.RoleCid, out var role, out var reason))
            {
                NetLogger.Warning($"[CreateRole] Fail. ConnectionId={conn.ConnectionId}, RoleCid={req.RoleCid}, Reason={reason}");
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
            rsp.Summary = role.ToNRoleSummary();
            NetLogger.Info($"[CreateRole] Success. ConnectionId={conn.ConnectionId}, RoleId={role.RoleId}, RoleCid={role.RoleCid}");
            Reply(conn, rsp, rpcSeq);
        }
    }
}