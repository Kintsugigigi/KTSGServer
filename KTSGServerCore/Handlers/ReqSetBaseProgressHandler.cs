using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqSetBaseProgressHandler : ReqHandler<ReqSetBaseProgress, RspRoleSignalChange>
    {
        protected override void Run(INetConnection conn, ReqSetBaseProgress req, RspRoleSignalChange rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var gamePlayService = ServerRuntime.Instance.GetService<GamePlayService>();
            var change = rsp.EnsureChange();

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason) ||
                !gamePlayService.TrySetInteractableBaseProgress(player, role, req.InteractableId, req.BaseProgress, change, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
            Reply(conn, rsp, rpcSeq);
        }
    }
}
