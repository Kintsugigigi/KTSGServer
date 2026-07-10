using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqRoleDataHandler : ReqHandler<ReqRoleData, RspRoleData>
    {
        protected override void Run(INetConnection conn, ReqRoleData req, RspRoleData rsp, ushort rpcSeq)
        {
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var questService = ServerRuntime.Instance.GetService<QuestService>();
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();

            if (!roleService.TrySelectRole(conn.ConnectionId, req.RoleId, out var role, out var reason))
            {
                NetLogger.Warning($"[RoleData] Fail. ConnectionId={conn.ConnectionId}, RoleId={req.RoleId}, Reason={reason}");
                rsp.Result = new Result
                {
                    Code = ResultCode.Fail,
                    Msg = reason
                };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            var roleProfile = role.ToNRoleProfile();
            var bootstrapChange = new NRoleSignalChange();
            var runtimePlayer = role.RuntimePlayer;
            if (runtimePlayer == null)
            {
                NetLogger.Warning($"[RoleData] Fail. ConnectionId={conn.ConnectionId}, RoleId={role.RoleId}, Reason=RuntimePlayer missing");
                rsp.Result = new Result
                {
                    Code = ResultCode.Fail,
                    Msg = "角色运行时玩家不存在"
                };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            if (!questService.DrainPendingQuestQueue(runtimePlayer, role, bootstrapChange, out reason))
            {
                NetLogger.Warning($"[RoleData] Bootstrap drain fail. ConnectionId={conn.ConnectionId}, RoleId={role.RoleId}, Reason={reason}");
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
            rsp.Role = roleProfile;
            NetLogger.Info($"[RoleData] Success. ConnectionId={conn.ConnectionId}, RoleId={role.RoleId}, RoleCid={role.RoleCid}");
            Reply(conn, rsp, rpcSeq);

            if (!IsEmpty(bootstrapChange))
            {
                if (!playerService.TryPushMessage(runtimePlayer, bootstrapChange, out var pushReason))
                {
                    NetLogger.Warning($"[RoleData] Bootstrap change push fail. ConnectionId={conn.ConnectionId}, RoleId={role.RoleId}, Reason={pushReason}");
                }
            }
        }

        private static bool IsEmpty(NRoleSignalChange change)
        {
            if (change == null)
            {
                return true;
            }

            return change.InventoryDelta == null &&
                   change.QuestStateSnapshot.Count == 0 &&
                   change.InteractableSnapshot.Count == 0 &&
                   !change.ActiveWaypointIdsChanged &&
                   change.ActiveWaypointIds.Count == 0;
        }
    }
}
