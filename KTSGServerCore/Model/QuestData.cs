using MongoDB.Bson.Serialization.Attributes;
using cfg;
using KTSG.Server;

namespace KTSG.Server.Model;

[BsonIgnoreExtraElements]
public sealed class ParentQuestData
{
    public EParentQuestState State { get; set; } = EParentQuestState.Locking;

    public int CurrentSubQuestId { get; set; } = 0;

    [BsonIgnore]
    public List<UnlockCondRuntime> UnlockConds { get; set; } = new();

    public List<SubQuestCondData> CurrentSubQuestConds { get; set; } = new();

    [BsonIgnore]
    public List<SubQuestCondRuntime> RuntimeCurrentSubQuestConds { get; } = new();

    [BsonIgnore]
    public int RuntimeParentQuestId { get; internal set; }

    [BsonIgnore]
    public MainQuestConfig? RuntimeConfig { get; internal set; }

    [BsonIgnore]
    public QuestConfig? RuntimeCurrentSubQuestConfig { get; internal set; }

    [BsonIgnore]
    public TypeEventBus? RuntimeEvents { get; internal set; }

    [BsonIgnore]
    public bool UnlockReadyNotified { get; internal set; }

    [BsonIgnore]
    public bool CurrentSubQuestReadyNotified { get; internal set; }

    public bool AreUnlockQueriesCompleted()
    {
        if (State != EParentQuestState.Waiting)
        {
            return false;
        }

        for (int i = 0; i < UnlockConds.Count; i++)
        {
            UnlockCondRuntime? cond = UnlockConds[i];
            if (cond == null || cond.Query == null)
            {
                continue;
            }

            if (!cond.Query.IsCompleted.Value)
            {
                return false;
            }
        }

        return true;
    }

    public bool AreCurrentSubQuestQueriesCompleted()
    {
        if (State != EParentQuestState.Executing || CurrentSubQuestId <= 0)
        {
            return false;
        }

        for (int i = 0; i < RuntimeCurrentSubQuestConds.Count; i++)
        {
            SubQuestCondRuntime? cond = RuntimeCurrentSubQuestConds[i];
            if (cond == null || cond.Query == null)
            {
                continue;
            }

            if (!cond.Query.IsCompleted.Value)
            {
                return false;
            }
        }

        return true;
    }

    public void OnUnlockQueryStateChanged(bool _)
    {
        if (State != EParentQuestState.Waiting)
        {
            return;
        }

        if (!AreUnlockQueriesCompleted())
        {
            return;
        }

        if (UnlockReadyNotified)
        {
            return;
        }

        UnlockReadyNotified = true;
        RuntimeEvents?.Publish(new OnParentQuestUnlockableEvent(RuntimeParentQuestId));
    }

    public void OnCurrentSubQuestQueryStateChanged(bool _)
    {
        if (State != EParentQuestState.Executing ||
            CurrentSubQuestId <= 0 ||
            RuntimeCurrentSubQuestConfig == null ||
            !RuntimeCurrentSubQuestConfig.CanAutoSuccess)
        {
            CurrentSubQuestReadyNotified = false;
            return;
        }

        if (!AreCurrentSubQuestQueriesCompleted())
        {
            CurrentSubQuestReadyNotified = false;
            return;
        }

        if (CurrentSubQuestReadyNotified)
        {
            return;
        }

        CurrentSubQuestReadyNotified = true;
        RuntimeEvents?.Publish(new OnSubQuestAutoAdvanceEvent(RuntimeParentQuestId));
    }

    public void OnCurrentSubQuestProgressChanged(SubQuestCondRuntime runtime)
    {
        if (runtime == null ||
            State != EParentQuestState.Executing ||
            CurrentSubQuestId <= 0)
        {
            return;
        }

        if (runtime.SlotIndex < 0)
        {
            return;
        }

        RuntimeEvents?.Publish(new OnSubQuestProgressChangedEvent(CurrentSubQuestId, runtime.SlotIndex, runtime.Count));
    }
}
