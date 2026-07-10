using cfg;
using KTSG.Proto;
using KTSG.Server.Model;

namespace KTSG.Server.Services;

public static class QuestSignalChangeExtensions
{
    public static NRoleSignalChange EnsureChange(this RspRoleSignalChange rsp)
    {
        rsp.Change ??= new NRoleSignalChange();
        return rsp.Change;
    }

    public static NRoleInventoryDelta EnsureInventoryDelta(this NRoleSignalChange change)
    {
        change.InventoryDelta ??= new NRoleInventoryDelta();
        return change.InventoryDelta;
    }
    
    public static void MergeInventoryDelta(this NRoleSignalChange change, NRoleInventoryDelta? delta)
    {
        if (delta == null)
        {
            return;
        }

        change.EnsureInventoryDelta().MergeFrom(delta);
    }

    public static void MergeFrom(this NRoleInventoryDelta target, NRoleInventoryDelta? source)
    {
        if (target == null || source == null)
        {
            return;
        }

        target.BackpackSlots.AddRange(source.BackpackSlots);
        target.WeaponSlotDeltas.AddRange(source.WeaponSlotDeltas);
        target.EquipSlotDeltas.AddRange(source.EquipSlotDeltas);
        target.ConsumableSlotDeltas.AddRange(source.ConsumableSlotDeltas);
        target.CurrencyDeltas.AddRange(source.CurrencyDeltas);
    }


    public static void AddQuestStateSnapshot(
        this NRoleSignalChange change,
        int parentQuestId,
        EParentQuestState parentState,
        int currentSubQuestId,
        IReadOnlyList<SubQuestCondData>? currentProgress = null)
    {
        var snapshot = new NParentQuestSnapshot
        {
            ParentQuestId = parentQuestId,
            ParentState = (int)parentState,
            CurrentSubQuestId = currentSubQuestId
        };

        if (currentProgress != null)
        {
            for (int i = 0; i < currentProgress.Count; i++)
            {
                SubQuestCondData? cond = currentProgress[i];
                if (cond == null)
                {
                    continue;
                }

                snapshot.CurrentProgress.Add(new NQuestCondProgress
                {
                    SlotIndex = i,
                    Count = cond.Count
                });
            }
        }

        change.QuestStateSnapshot.Add(snapshot);
    }

    public static void AddOrUpdateInteractableDelta(this NRoleSignalChange change, int interactableId, InteractableProgressData data)
    {
        if (interactableId <= 0 || data == null)
        {
            return;
        }

        NInteractableProgressSnapshot? delta = null;
        for (int i = 0; i < change.InteractableSnapshot.Count; i++)
        {
            if (change.InteractableSnapshot[i].InteractableId == interactableId)
            {
                delta = change.InteractableSnapshot[i];
                break;
            }
        }

        if (delta == null)
        {
            delta = new NInteractableProgressSnapshot();
            change.InteractableSnapshot.Add(delta);
        }

        delta.InteractableId = interactableId;
        delta.BaseProgress = data.BaseProgress;
        delta.ResolvedProgress = data.ResolvedProgress;
    }

    public static void ReplaceActiveWaypointIds(this NRoleSignalChange change, IEnumerable<int>? activeWaypointIds)
    {
        if (change == null)
        {
            return;
        }

        change.ActiveWaypointIdsChanged = true;
        change.ActiveWaypointIds.Clear();
        if (activeWaypointIds == null)
        {
            return;
        }

        change.ActiveWaypointIds.Add(activeWaypointIds);
    }

    public static void SetReward(this NRoleSignalChange change, NGrantedReward? reward)
    {
        if (change == null)
        {
            return;
        }

        change.Reward = reward;
    }
}
