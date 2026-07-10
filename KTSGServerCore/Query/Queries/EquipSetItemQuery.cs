using cfg;
using KTSG.Server.Model;
using KTSG.Server.Runtime;

namespace KTSG.Server;

public sealed class EquipSetItemQuery : QuestQuery
{
    private EItemType _equipType;
    private int _configId;
    private int _targetCount;
    private ECompType _countCompType;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _equipType = (EItemType)arg1;
        _configId = arg2;
        _targetCount = arg3;
        _countCompType = (ECompType)arg4;
        UseLiveOnlyOnInit = false;

        if (Role.RuntimeEquipSlots != null)
        {
            Listen(Role.RuntimeEquipSlots.RegisterOnChanged(_ => ApplyCount(GetNow())));
        }

        Listen(Events.Subscribe<OnInstanceItemRuntimeChangedEvent>(OnInstanceItemRuntimeChanged));
    }

    protected override int GetNow()
    {
        if (Role.RuntimeEquipSlots == null || Role.RuntimeBackpack == null)
        {
            return 0;
        }

        int matchedCount = 0;
        for (int i = 0; i < Role.RuntimeEquipSlots.Count; i++)
        {
            long instanceId = Role.RuntimeEquipSlots[i];
            if (instanceId <= 0 || !TryGetEquipBackpackSlot(instanceId, out int backpackSlot))
            {
                continue;
            }

            if (!Role.RuntimeBackpack.IsValidSlot(backpackSlot))
            {
                continue;
            }

            InventoryItemData? item = Role.RuntimeBackpack.GetItemOrNull(backpackSlot);
            if (item == null)
            {
                continue;
            }

            if (!MatchConfig(item) || !MatchEquipType(item))
            {
                continue;
            }

            matchedCount++;
        }

        return matchedCount;
    }

    protected override bool CheckCompleted(int count)
    {
        return Compare(count, _targetCount, _countCompType);
    }

    private bool MatchConfig(InventoryItemData item)
    {
        return _configId <= 0 || item.ConfigId == _configId;
    }

    private bool MatchEquipType(InventoryItemData item)
    {
        if (_equipType == EItemType.None)
        {
            return true;
        }

        return item.ItemType == _equipType;
    }

    private bool TryGetEquipBackpackSlot(long instanceId, out int slotIndex)
    {
        slotIndex = -1;
        return Role.RuntimeBackpack!.TryGetInstanceSlot(EItemType.Helmet, instanceId, out slotIndex)
               || Role.RuntimeBackpack.TryGetInstanceSlot(EItemType.Armor, instanceId, out slotIndex)
               || Role.RuntimeBackpack.TryGetInstanceSlot(EItemType.Stone, instanceId, out slotIndex);
    }

    private void OnInstanceItemRuntimeChanged(OnInstanceItemRuntimeChangedEvent evt)
    {
        if (!IsEquipItemType(evt.ItemType) || !IsEquippedInstance(evt.InstanceId))
        {
            return;
        }

        ApplyCount(GetNow());
    }

    private bool IsEquippedInstance(long instanceId)
    {
        if (Role.RuntimeEquipSlots == null || instanceId <= 0)
        {
            return false;
        }

        for (int i = 0; i < Role.RuntimeEquipSlots.Count; i++)
        {
            if (Role.RuntimeEquipSlots[i] == instanceId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEquipItemType(EItemType itemType)
    {
        return itemType == EItemType.Helmet
               || itemType == EItemType.Armor
               || itemType == EItemType.Stone;
    }
}
