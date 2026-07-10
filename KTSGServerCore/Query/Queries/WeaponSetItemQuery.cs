using cfg;
using KTSG.Server.Model;

namespace KTSG.Server;

public sealed class WeaponSetItemQuery : QuestQuery
{
    private int _configId;
    private int _level;
    private ECompType _levelCompType;
    private int _targetCount;
    private ECompType _countCompType;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _configId = arg1;
        _level = arg2;
        _levelCompType = (ECompType)arg3;
        _targetCount = arg4;
        _countCompType = (ECompType)arg5;
        UseLiveOnlyOnInit = false;

        if (Role.RuntimeWeaponSlots != null)
        {
            Listen(Role.RuntimeWeaponSlots.RegisterOnChanged(_ => ApplyCount(GetNow())));
        }

        Listen(Events.Subscribe<OnInstanceItemRuntimeChangedEvent>(OnInstanceItemRuntimeChanged));
    }

    protected override int GetNow()
    {
        if (Role.RuntimeWeaponSlots == null || Role.RuntimeBackpack == null)
        {
            return 0;
        }

        if (!Role.RuntimeBackpack.InstanceToSlot.TryGetValue(EItemType.Weapon, out Dictionary<long, int>? instanceToSlot))
        {
            return 0;
        }

        int matchedCount = 0;
        for (int i = 0; i < Role.RuntimeWeaponSlots.Count; i++)
        {
            long instanceId = Role.RuntimeWeaponSlots[i];
            if (instanceId <= 0 || !instanceToSlot.TryGetValue(instanceId, out int backpackSlot))
            {
                continue;
            }

            if (!Role.RuntimeBackpack.IsValidSlot(backpackSlot))
            {
                continue;
            }

            InventoryItemData? item = Role.RuntimeBackpack.GetItemOrNull(backpackSlot);
            if (item == null || item.Weapon == null)
            {
                continue;
            }

            if (!MatchConfig(item) || !MatchLevel(item.Weapon.Level))
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

    private bool MatchLevel(int level)
    {
        if (_level <= 0 || _levelCompType == ECompType.None)
        {
            return true;
        }

        return Compare(level, _level, _levelCompType);
    }

    private void OnInstanceItemRuntimeChanged(OnInstanceItemRuntimeChangedEvent evt)
    {
        if (evt.ItemType != EItemType.Weapon)
        {
            return;
        }

        if (!IsEquippedWeaponInstance(evt.InstanceId))
        {
            return;
        }

        ApplyCount(GetNow());
    }

    private bool IsEquippedWeaponInstance(long instanceId)
    {
        if (Role.RuntimeWeaponSlots == null || instanceId <= 0)
        {
            return false;
        }

        for (int i = 0; i < Role.RuntimeWeaponSlots.Count; i++)
        {
            if (Role.RuntimeWeaponSlots[i] == instanceId)
            {
                return true;
            }
        }

        return false;
    }
}
