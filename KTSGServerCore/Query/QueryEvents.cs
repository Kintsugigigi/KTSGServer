using cfg;

namespace KTSG.Server;

public struct OnStackItemChangedEvent(int configId, int deltaCount, bool isAdd)
{
    private bool _recycled = false;

    public int ConfigId { get; private set; } = configId;
    public int DeltaCount { get; private set; } = deltaCount;
    public bool IsAdd { get; private set; } = isAdd;
}

public struct OnMonsterKilledEvent(int configId)
{
    public int ConfigId { get; private set; } = configId;
}

public struct OnDeathByMonsterEvent(int configId)
{
    public int ConfigId { get; private set; } = configId;
}

public struct OnInstanceItemChangedEvent(EItemType itemType, int configId, int level, long instanceId, bool isAdd)
{
    public EItemType ItemType { get; private set; } = itemType;
    public int ConfigId { get; private set; } = configId;
    public int Level { get; private set; } = level;
    public long InstanceId { get; private set; } = instanceId;
    public bool IsAdd { get; private set; } = isAdd;
}

public struct OnInstanceItemRuntimeChangedEvent(EItemType itemType, int configId, int level, long instanceId)
{
    public EItemType ItemType { get; private set; } = itemType;
    public int ConfigId { get; private set; } = configId;
    public int Level { get; private set; } = level;
    public long InstanceId { get; private set; } = instanceId;
}

public struct OnContractSuccessEvent(int configId, int score, List<int> contacts)
{
    public int ConfigId { get; private set; } = configId;
    public int Score { get; private set; } = score;
    public List<int> Contacts { get; private set; } = contacts;
}

public struct OnParentQuestUnlockableEvent(int parentQuestId)
{
    public int ParentQuestId { get; private set; } = parentQuestId;
}

public struct OnSubQuestAutoAdvanceEvent(int parentQuestId)
{
    public int ParentQuestId { get; private set; } = parentQuestId;
}

public struct OnSubQuestProgressChangedEvent(int subQuestId, int condIndex, int count)
{
    public int SubQuestId { get; private set; } = subQuestId;
    public int CondIndex { get; private set; } = condIndex;
    public int Count { get; private set; } = count;
}

public struct OnWayPointTriggeredEvent(int wayPointId)
{
    public int WayPointId { get; private set; } = wayPointId;
}
