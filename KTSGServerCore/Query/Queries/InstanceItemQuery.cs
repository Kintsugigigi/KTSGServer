using cfg;
using KTSG.Server.Model;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.Server;

public sealed class InstanceItemQuery : QuestQuery
{
    private EItemType _itemType;
    private int _configId;
    private int _level;
    private ECompType _levelCompType;
    private int _targetCount;
    private ECompType _countCompType;
    private bool _subscribeOnly;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _itemType = (EItemType)arg1;
        _configId = arg2;
        _level = arg3;
        _levelCompType = (ECompType)arg4;
        _targetCount = arg5;
        _countCompType = (ECompType)arg6;
        _subscribeOnly = arg8 == 1;
        UseLiveOnlyOnInit = _subscribeOnly;

        Listen(Events.Subscribe<OnInstanceItemChangedEvent>(OnInstanceItemChanged));
    }

    protected override int GetNow()
    {
        if (_subscribeOnly || Role.RuntimeBackpack == null)
        {
            return 0;
        }

        int count = 0;
        foreach (InventoryItemData item in Role.RuntimeBackpack.OccupiedItems)
        {
            if (item.InstanceId <= 0)
            {
                continue;
            }

            if (!MatchItem(item))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    protected override bool CheckCompleted(int count)
    {
        return Compare(count, _targetCount, _countCompType);
    }

    private bool MatchItem(InventoryItemData item)
    {
        if (_configId > 0 && item.ConfigId != _configId)
        {
            return false;
        }

        if (_itemType != EItemType.None)
        {
            if (item.ItemType != _itemType)
            {
                return false;
            }
        }

        if (_levelCompType != ECompType.None)
        {
            if (item.Weapon == null)
            {
                return true;
            }

            int actualLevel = item.Weapon.Level;
            if (!Compare(actualLevel, _level, _levelCompType))
            {
                return false;
            }
        }

        return true;
    }

    private void OnInstanceItemChanged(OnInstanceItemChangedEvent evt)
    {
        if (_itemType != EItemType.None && evt.ItemType != _itemType)
        {
            return;
        }

        if (_configId > 0 && evt.ConfigId != _configId)
        {
            return;
        }

        if (_levelCompType != ECompType.None)
        {
            if (evt.ItemType == EItemType.Weapon && !Compare(evt.Level, _level, _levelCompType))
            {
                return;
            }
        }

        if (evt.IsAdd)
        {
            ApplyCount(CurrentCount + 1);
        }
        else
        {
            ApplyCount(Math.Max(0, CurrentCount - 1));
        }
    }

}
