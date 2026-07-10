using cfg;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Model;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqRoleEquipEquipSlotHandler : ReqHandler<ReqRoleEquipEquipSlot, RspRoleInventoryChange>
    {
        protected override void Run(INetConnection conn, ReqRoleEquipEquipSlot req, RspRoleInventoryChange rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var itemService = ServerRuntime.Instance.GetService<ItemService>();
            EItemType itemType = (EItemType)req.ItemType;
            InventoryItemData? item = null;
            NetLogger.Info(
                $"[EquipEquipHandler] Run start. Conn={conn?.ConnectionId}, RpcSeq={rpcSeq}, Type={itemType}, Inst={req.InstanceId}, Slot={req.SlotIndex}");

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                NetLogger.Warning($"[EquipEquipHandler] Resolve player/role failed. Reason={reason}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            if (role.RuntimeBackpack == null)
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = "背包未初始化" };
                NetLogger.Warning("[EquipEquipHandler] RuntimeBackpack is null.");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            if (req.InstanceId == -1)
            {
                if (!role.TryClearEquipSlot(req.SlotIndex, out var clearDelta, out reason))
                {
                    rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                    NetLogger.Warning($"[EquipEquipHandler] Clear slot failed. Slot={req.SlotIndex}, Reason={reason}");
                    Reply(conn, rsp, rpcSeq);
                    return;
                }

                rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
                rsp.Delta = clearDelta;
                NetLogger.Info($"[EquipEquipHandler] Clear slot success. Slot={req.SlotIndex}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            if (!role.RuntimeBackpack.TryGetInstanceSlot(itemType, req.InstanceId, out int slotIndex))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = $"实例物品不存在: {req.InstanceId}" };
                NetLogger.Warning($"[EquipEquipHandler] TryGetInstanceSlot failed. Type={itemType}, Inst={req.InstanceId}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            item = role.RuntimeBackpack.GetItemOrNull(slotIndex);
            if (item == null)
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = $"槽位物品不存在: {slotIndex}" };
                NetLogger.Warning($"[EquipEquipHandler] GetItemOrNull failed. BackpackSlot={slotIndex}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            NetLogger.Info(
                $"[EquipEquipHandler] Item resolved. BackpackSlot={slotIndex}, ItemType={item.ItemType}, ConfigId={item.ConfigId}, Inst={item.InstanceId}");

            if (!itemService.TryEquipToEquipSlot(role, item, req.SlotIndex, out var delta, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                NetLogger.Warning(
                    $"[EquipEquipHandler] TryEquipToEquipSlot failed. Type={item.ItemType}, Inst={item.InstanceId}, TargetSlot={req.SlotIndex}, Reason={reason}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
            rsp.Delta = delta;
            NetLogger.Info(
                $"[EquipEquipHandler] Success. Type={item.ItemType}, Inst={item.InstanceId}, TargetSlot={req.SlotIndex}, DeltaEquip={delta?.EquipSlotDeltas?.Count ?? 0}");
            Reply(conn, rsp, rpcSeq);
        }
    }
}
