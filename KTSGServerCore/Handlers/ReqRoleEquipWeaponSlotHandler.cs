using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Model;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqRoleEquipWeaponSlotHandler : ReqHandler<ReqRoleEquipWeaponSlot, RspRoleInventoryChange>
    {
        protected override void Run(INetConnection conn, ReqRoleEquipWeaponSlot req, RspRoleInventoryChange rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var itemService = ServerRuntime.Instance.GetService<ItemService>();
            InventoryItemData? item = null;
            NetLogger.Info(
                $"[EquipWeaponHandler] Run start. Conn={conn?.ConnectionId}, RpcSeq={rpcSeq}, Inst={req.InstanceId}, Slot={req.SlotIndex}");

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                NetLogger.Warning($"[EquipWeaponHandler] Resolve player/role failed. Reason={reason}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            if (role.RuntimeBackpack == null)
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = "背包未初始化" };
                NetLogger.Warning("[EquipWeaponHandler] RuntimeBackpack is null.");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            if (req.InstanceId == -1)
            {
                if (!role.TryClearWeaponSlot(req.SlotIndex, out var clearDelta, out reason))
                {
                    rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                    NetLogger.Warning($"[EquipWeaponHandler] Clear slot failed. Slot={req.SlotIndex}, Reason={reason}");
                    Reply(conn, rsp, rpcSeq);
                    return;
                }

                rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
                rsp.Delta = clearDelta;
                NetLogger.Info($"[EquipWeaponHandler] Clear slot success. Slot={req.SlotIndex}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            if (!role.RuntimeBackpack.TryGetInstanceSlot(cfg.EItemType.Weapon, req.InstanceId, out int slotIndex))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = $"实例物品不存在: {req.InstanceId}" };
                NetLogger.Warning($"[EquipWeaponHandler] TryGetInstanceSlot failed. Inst={req.InstanceId}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            item = role.RuntimeBackpack.GetItemOrNull(slotIndex);
            if (item == null)
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = $"槽位物品不存在: {slotIndex}" };
                NetLogger.Warning($"[EquipWeaponHandler] GetItemOrNull failed. BackpackSlot={slotIndex}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            NetLogger.Info(
                $"[EquipWeaponHandler] Item resolved. BackpackSlot={slotIndex}, ItemType={item.ItemType}, ConfigId={item.ConfigId}, Inst={item.InstanceId}");

            if (!itemService.TryEquipToWeaponSlot(role, item, req.SlotIndex, out var delta, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                NetLogger.Warning(
                    $"[EquipWeaponHandler] TryEquipToWeaponSlot failed. Inst={item.InstanceId}, TargetSlot={req.SlotIndex}, Reason={reason}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
            rsp.Delta = delta;
            NetLogger.Info(
                $"[EquipWeaponHandler] Success. Inst={item.InstanceId}, TargetSlot={req.SlotIndex}, DeltaWeapon={delta?.WeaponSlotDeltas?.Count ?? 0}");
            Reply(conn, rsp, rpcSeq);
        }
    }
}
