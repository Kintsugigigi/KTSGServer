using cfg;
using KTSG.Server.Model;

namespace KTSG.Server;

public sealed class StackItemQuery : QuestQuery
{
    private int _configId;
    private int _targetCount;
    private ECompType _countCompType;
    private bool _subscribeOnly;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _configId = arg1;
        _targetCount = arg2;
        _countCompType = (ECompType)arg3;
        _subscribeOnly = arg8 == 1;
        UseLiveOnlyOnInit = _subscribeOnly;

        Listen(Events.Subscribe<OnStackItemChangedEvent>(OnStackItemChanged));
    }

    protected override int GetNow()
    {
        if (_subscribeOnly || _configId <= 0 || Role.RuntimeBackpack == null)
        {
            return 0;
        }

        if (!Role.RuntimeBackpack.TryGetSlotsByConfig(_configId, out List<int>? slots) || slots.Count == 0)
        {
            return 0;
        }

        int totalCount = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            int slotIndex = slots[i];
            if (!Role.RuntimeBackpack.IsValidSlot(slotIndex))
            {
                continue;
            }

            InventoryItemData? stackItem = Role.RuntimeBackpack.GetItemOrNull(slotIndex);
            if (stackItem != null && stackItem.ConfigId == _configId && stackItem.Count > 0)
            {
                totalCount += stackItem.Count;
            }
        }

        return totalCount;
    }

    protected override bool CheckCompleted(int count)
    {
        return Compare(count, _targetCount, _countCompType);
    }

    private void OnStackItemChanged(OnStackItemChangedEvent evt)
    {
        if (evt.ConfigId != _configId)
        {
            return;
        }

        int nextCount = evt.IsAdd ? CurrentCount + evt.DeltaCount : CurrentCount - evt.DeltaCount;
        ApplyCount(Math.Max(0, nextCount));
    }
}
