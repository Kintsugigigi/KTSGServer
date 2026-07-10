using System.Collections.Generic;
using cfg;
using KTSG.Proto;

namespace KTSG.Server.Model;

public sealed class BackpackRuntime
{
    private readonly Dictionary<int, StackSlotRuntime> _stackSlotRuntimes = new();

    public BackpackRuntime(int capacity)
    {
        Capacity = Math.Max(0, capacity);
        for (int i = 0; i < Capacity; i++)
        {
            EmptySlots.Add(i);
        }
    }

    public int Capacity { get; }

    public Dictionary<int, InventoryItemData> ItemsBySlot { get; } = new();

    public SortedSet<int> EmptySlots { get; } = new();

    public Dictionary<int, List<int>> ConfigToSlots { get; } = new();

    public Dictionary<EItemType, Dictionary<long, int>> InstanceToSlot { get; } = new();

    public int EmptySlotCount => EmptySlots.Count;

    public IEnumerable<InventoryItemData> OccupiedItems => ItemsBySlot.Values;

    public bool TryAddStackItem(int configId, int maxStack, int count, out List<NInventorySlotDelta> deltas, out string reason)
    {
        deltas = new List<NInventorySlotDelta>();
        if (configId <= 0 || maxStack <= 0 || count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        int availableOnTail = 0;
        if (TryGetTailStackItem(configId, out int tailSlot, out InventoryItemData tailItem))
        {
            availableOnTail = Math.Max(0, maxStack - tailItem.Count);
        }

        int remain = count - Math.Min(availableOnTail, count);
        int requiredNewStacks = remain > 0 ? (int)Math.Ceiling(remain / (float)maxStack) : 0;
        if (EmptySlotCount < requiredNewStacks)
        {
            reason = "背包已满";
            return false;
        }

        if (availableOnTail > 0 && TryGetTailStackItem(configId, out tailSlot, out tailItem))
        {
            int addAmount = Math.Min(availableOnTail, count);
            if (addAmount > 0)
            {
                tailItem.Count += addAmount;
                AddDelta(deltas, tailSlot, tailItem);
            }
        }

        while (remain > 0)
        {
            int stackCount = Math.Min(remain, maxStack);
            InventoryItemData newItem = new InventoryItemData
            {
                InstanceId = -1,
                ConfigId = configId,
                Count = stackCount,
            };

            if (!TryAddItemToFirstEmptySlot(newItem, out int slotIndex, out reason))
            {
                return false;
            }

            AddDelta(deltas, slotIndex, newItem);
            remain -= stackCount;
        }

        reason = string.Empty;
        return true;
    }

    public bool TryConsumeStackItem(int configId, int count, out List<NInventorySlotDelta> deltas, out string reason)
    {
        deltas = new List<NInventorySlotDelta>();
        if (configId <= 0 || count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        if (!TryGetSlotsByConfig(configId, out List<int> slots) || slots.Count <= 0)
        {
            reason = "背包中不存在该物品";
            return false;
        }

        int totalCount = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (ItemsBySlot.TryGetValue(slots[i], out InventoryItemData? stackItem))
            {
                totalCount += stackItem?.Count ?? 0;
            }
        }

        if (totalCount < count)
        {
            reason = "物品数量不足";
            return false;
        }

        int remain = count;
        while (remain > 0 && TryGetSlotsByConfig(configId, out slots) && slots.Count > 0)
        {
            int lastSlot = slots[^1];
            if (!ItemsBySlot.TryGetValue(lastSlot, out InventoryItemData? stackItem) || IsEmptyItem(stackItem))
            {
                ClearSlotInternal(lastSlot, out _);
                AddDelta(deltas, lastSlot, null, true);
                continue;
            }

            int removeAmount = Math.Min(stackItem.Count, remain);
            stackItem.Count -= removeAmount;
            remain -= removeAmount;

            if (stackItem.Count <= 0)
            {
                ClearSlotInternal(lastSlot, out _);
                AddDelta(deltas, lastSlot, null, true);
            }
            else
            {
                AddDelta(deltas, lastSlot, stackItem);
            }
        }

        reason = string.Empty;
        return true;
    }

    public void Reset(IEnumerable<KeyValuePair<int, InventoryItemData>>? items)
    {
        ItemsBySlot.Clear();
        ConfigToSlots.Clear();
        InstanceToSlot.Clear();
        _stackSlotRuntimes.Clear();
        EmptySlots.Clear();
        for (int i = 0; i < Capacity; i++)
        {
            EmptySlots.Add(i);
        }

        if (items == null)
        {
            return;
        }

        foreach (var entry in items)
        {
            if (!IsValidSlot(entry.Key) || IsEmptyItem(entry.Value))
            {
                continue;
            }

            SetItemInternal(entry.Key, entry.Value);
        }
    }

    public Dictionary<int, InventoryItemData> ExportPersistentItems()
    {
        return new Dictionary<int, InventoryItemData>(ItemsBySlot);
    }

    public bool IsValidSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < Capacity;
    }

    public bool TryGetItem(int slotIndex, out InventoryItemData? item)
    {
        if (!IsValidSlot(slotIndex))
        {
            item = null;
            return false;
        }

        ItemsBySlot.TryGetValue(slotIndex, out InventoryItemData? value);
        item = value;
        return true;
    }

    public InventoryItemData? GetItemOrNull(int slotIndex)
    {
        return TryGetItem(slotIndex, out InventoryItemData? item) ? item : null;
    }

    public bool IsEmptySlot(int slotIndex)
    {
        return !ItemsBySlot.ContainsKey(slotIndex);
    }

    public bool TryGetFirstEmptySlot(out int slotIndex)
    {
        slotIndex = -1;
        if (EmptySlots.Count <= 0)
        {
            return false;
        }

        slotIndex = EmptySlots.Min;
        return true;
    }

    public bool TryGetInstanceSlot(EItemType itemType, long instanceId, out int slotIndex)
    {
        slotIndex = -1;
        return instanceId > 0 &&
               InstanceToSlot.TryGetValue(itemType, out Dictionary<long, int>? instanceDict) &&
               instanceDict.TryGetValue(instanceId, out slotIndex);
    }

    public bool TryGetSlotsByConfig(int configId, out List<int> slots)
    {
        return ConfigToSlots.TryGetValue(configId, out slots!);
    }

    public bool TryGetStackItemCount(int configId, out int totalCount)
    {
        totalCount = 0;
        if (configId <= 0 || !ConfigToSlots.TryGetValue(configId, out List<int>? slots) || slots.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            if (ItemsBySlot.TryGetValue(slots[i], out InventoryItemData? stackItem))
            {
                totalCount += stackItem?.Count ?? 0;
            }
        }

        return totalCount > 0;
    }

    public bool TryGetTailStackItem(int configId, out int slotIndex, out InventoryItemData item)
    {
        slotIndex = -1;
        item = null!;
        if (!ConfigToSlots.TryGetValue(configId, out List<int>? slots) || slots.Count <= 0)
        {
            return false;
        }

        slotIndex = slots[^1];
        return ItemsBySlot.TryGetValue(slotIndex, out item!);
    }

    public bool TryAddItemToFirstEmptySlot(InventoryItemData item, out int slotIndex, out string reason)
    {
        slotIndex = -1;
        if (IsEmptyItem(item))
        {
            reason = "物品为空";
            return false;
        }

        if (!TryGetFirstEmptySlot(out slotIndex))
        {
            reason = "背包已满";
            return false;
        }

        SetItemInternal(slotIndex, item);
        reason = string.Empty;
        return true;
    }

    public bool TryAddItemToFirstEmptySlot(InventoryItemData item, out int slotIndex, out NInventorySlotDelta delta, out string reason)
    {
        delta = default;
        if (!TryAddItemToFirstEmptySlot(item, out slotIndex, out reason))
        {
            return false;
        }

        delta = CreateDelta(slotIndex, item);
        return true;
    }

    public bool CanAcceptStackItem(int configId, int maxStack, int count, out int requiredSlots, out string reason)
    {
        requiredSlots = 0;
        if (configId <= 0 || maxStack <= 0 || count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        int tailSpace = 0;
        if (TryGetTailStackItem(configId, out _, out InventoryItemData tailItem))
        {
            tailSpace = Math.Max(0, maxStack - Math.Max(0, tailItem.Count));
        }

        int remain = Math.Max(0, count - Math.Min(count, tailSpace));
        requiredSlots = remain > 0 ? (int)Math.Ceiling(remain / (float)maxStack) : 0;
        if (EmptySlotCount < requiredSlots)
        {
            reason = "背包空间不足";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool CanAcceptInstanceItems(int count, out string reason)
    {
        if (count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        if (EmptySlotCount < count)
        {
            reason = "背包空间不足";
            return false;
        }

        reason = string.Empty;
        return true;
    }
    

    public bool TryClearSlot(int slotIndex, out InventoryItemData? removedItem, out string reason)
    {
        if (!IsValidSlot(slotIndex))
        {
            removedItem = null;
            reason = $"槽位非法: {slotIndex}";
            return false;
        }

        ClearSlotInternal(slotIndex, out removedItem);
        reason = string.Empty;
        return true;
    }
    
    public bool TryRemoveInstanceItem(EItemType itemType, long instanceId, out int slotIndex, out InventoryItemData? removedItem, out NInventorySlotDelta delta, out string reason)
    {
        slotIndex = -1;
        removedItem = null;
        delta = default;
        if (!TryGetInstanceSlot(itemType, instanceId, out slotIndex))
        {
            reason = $"实例物品不存在: {instanceId}";
            return false;
        }

        if (!TryClearSlot(slotIndex, out removedItem, out reason))
        {
            return false;
        }

        delta = CreateDelta(slotIndex, null, true);
        return true;
    }

    public bool TrySwapSlots(int slotA, int slotB, out List<NInventorySlotDelta> deltas, out string reason)
    {
        deltas = new List<NInventorySlotDelta>();
        if (!IsValidSlot(slotA) || !IsValidSlot(slotB) || slotA == slotB)
        {
            reason = "槽位非法";
            return false;
        }

        InventoryItemData? itemA = GetItemOrNull(slotA);
        InventoryItemData? itemB = GetItemOrNull(slotB);
        if (itemA == null && itemB == null)
        {
            reason = "两个槽位都为空";
            return false;
        }

        ClearSlotInternal(slotA, out _);
        ClearSlotInternal(slotB, out _);

        if (itemA != null)
        {
            SetItemInternal(slotB, itemA);
        }

        if (itemB != null)
        {
            SetItemInternal(slotA, itemB);
        }

        AddDelta(deltas, slotA, GetItemOrNull(slotA), IsEmptySlot(slotA));
        AddDelta(deltas, slotB, GetItemOrNull(slotB), IsEmptySlot(slotB));
        reason = string.Empty;
        return true;
    }

    private void SetItemInternal(int slotIndex, InventoryItemData item)
    {
        ItemsBySlot[slotIndex] = item;
        EmptySlots.Remove(slotIndex);

        if (item.InstanceId > 0)
        {
            EItemType itemType = item.ItemType;
            if (itemType == EItemType.None)
            {
                throw new InvalidOperationException($"Instance item type is None. ConfigId={item.ConfigId}, InstanceId={item.InstanceId}");
            }

            if (!InstanceToSlot.TryGetValue(itemType, out Dictionary<long, int>? instanceDict))
            {
                instanceDict = new Dictionary<long, int>();
                InstanceToSlot[itemType] = instanceDict;
            }

            instanceDict[item.InstanceId] = slotIndex;
            return;
        }

        if (!ConfigToSlots.TryGetValue(item.ConfigId, out List<int>? slots))
        {
            slots = new List<int>();
            ConfigToSlots[item.ConfigId] = slots;
        }

        slots.Add(slotIndex);
        _stackSlotRuntimes[slotIndex] = new StackSlotRuntime
        {
            ConfigId = item.ConfigId,
            SlotIndex = slotIndex,
            ConfigListIndex = slots.Count - 1
        };
    }

    private void ClearSlotInternal(int slotIndex, out InventoryItemData? removedItem)
    {
        if (!ItemsBySlot.Remove(slotIndex, out removedItem))
        {
            removedItem = null;
            EmptySlots.Add(slotIndex);
            return;
        }

        EmptySlots.Add(slotIndex);
        if (removedItem.InstanceId > 0)
        {
            EItemType itemType = removedItem.ItemType;

            if (InstanceToSlot.TryGetValue(itemType, out Dictionary<long, int>? instanceDict))
            {
                instanceDict.Remove(removedItem.InstanceId);
                if (instanceDict.Count == 0)
                {
                    InstanceToSlot.Remove(itemType);
                }
            }

            return;
        }

        RemoveStackSlotRuntime(removedItem.ConfigId, slotIndex);
    }

    private void RemoveStackSlotRuntime(int configId, int slotIndex)
    {
        if (!_stackSlotRuntimes.TryGetValue(slotIndex, out StackSlotRuntime? slotRuntime))
        {
            return;
        }

        if (!ConfigToSlots.TryGetValue(configId, out List<int>? slots))
        {
            _stackSlotRuntimes.Remove(slotIndex);
            return;
        }

        int removeIndex = slotRuntime.ConfigListIndex;
        _stackSlotRuntimes.Remove(slotIndex);
        slots.RemoveAt(removeIndex);
        if (slots.Count == 0)
        {
            ConfigToSlots.Remove(configId);
            return;
        }

        for (int i = removeIndex; i < slots.Count; i++)
        {
            int currentSlot = slots[i];
            if (_stackSlotRuntimes.TryGetValue(currentSlot, out StackSlotRuntime? currentRuntime))
            {
                currentRuntime.ConfigListIndex = i;
            }
        }
    }

    private static bool IsEmptyItem(InventoryItemData? item)
    {
        return item == null || item.ConfigId <= 0 || item.Count <= 0;
    }

    private static void AddDelta(List<NInventorySlotDelta> deltas, int slotIndex, InventoryItemData? item, bool isEmpty = false)
    {
        deltas.Add(CreateDelta(slotIndex, item, isEmpty));
    }

    private static NInventorySlotDelta CreateDelta(int slotIndex, InventoryItemData? item, bool isEmpty = false)
    {
        return new NInventorySlotDelta
        {
            SlotIndex = slotIndex,
            IsEmpty = isEmpty || IsEmptyItem(item),
            Item = item?.ToNItem() ?? new NItem()
        };
    }
}
