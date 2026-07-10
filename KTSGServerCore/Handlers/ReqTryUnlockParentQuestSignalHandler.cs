using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqTryUnlockParentQuestSignalHandler : ReqHandler<ReqTryUnlockParentQuestSignal, RspRoleSignalChange>
    {
        protected override void Run(INetConnection conn, ReqTryUnlockParentQuestSignal req, RspRoleSignalChange rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var questService = ServerRuntime.Instance.GetService<QuestService>();
            var change = rsp.EnsureChange();

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason) ||
                !questService.TryHandleUnlockParentQuestSignal(player, role, req.ParentQuestId, change, out reason))
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
