using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqRoleBindConsumableSlotHandler : ReqHandler<ReqRoleBindConsumableSlot, RspRoleInventoryChange>
    {
        protected override void Run(INetConnection conn, ReqRoleBindConsumableSlot req, RspRoleInventoryChange rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var itemService = ServerRuntime.Instance.GetService<ItemService>();
            NetLogger.Info(
                $"[BindConsumableHandler] Run start. Conn={conn?.ConnectionId}, RpcSeq={rpcSeq}, Config={req.ConfigId}, Slot={req.SlotIndex}");

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                NetLogger.Warning($"[BindConsumableHandler] Resolve player/role failed. Reason={reason}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            NRoleInventoryDelta delta;
            if (req.ConfigId == -1)
            {
                if (!role.TryClearConsumableSlot(req.SlotIndex, out delta, out reason))
                {
                    rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                    NetLogger.Warning($"[BindConsumableHandler] Clear slot failed. Slot={req.SlotIndex}, Reason={reason}");
                    Reply(conn, rsp, rpcSeq);
                    return;
                }

                NetLogger.Info($"[BindConsumableHandler] Clear slot success. Slot={req.SlotIndex}");
            }
            else if (!itemService.TryBindConsumableToSlot(role, req.ConfigId, req.SlotIndex, out delta, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                NetLogger.Warning(
                    $"[BindConsumableHandler] TryBindConsumableToSlot failed. Config={req.ConfigId}, Slot={req.SlotIndex}, Reason={reason}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
            rsp.Delta = delta;
            NetLogger.Info(
                $"[BindConsumableHandler] Success. Config={req.ConfigId}, Slot={req.SlotIndex}, DeltaConsumable={delta?.ConsumableSlotDeltas?.Count ?? 0}");
            Reply(conn, rsp, rpcSeq);
        }
    }
}
