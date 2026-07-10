using MongoDB.Bson.Serialization.Attributes;
using KTSG.Server;

namespace KTSG.Server.Model;

public interface IQuestCondRuntime
{
    int Count { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class UnlockCondRuntime : IQuestCondRuntime
{
    public int Count { get; set; }

    [BsonIgnore]
    public Query? Query { get; set; }

    public void Reset()
    {
        Count = 0;
        Query = null;
    }
}

[BsonIgnoreExtraElements]
public sealed class SubQuestCondData
{
    public int Count { get; set; }

    public void Reset()
    {
        Count = 0;
    }
}

[BsonIgnoreExtraElements]
public sealed class SubQuestCondRuntime : IQuestCondRuntime
{
    [BsonIgnore]
    public SubQuestCondData Data { get; set; } = null!;

    public int Count
    {
        get => Data?.Count ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Count = value;
            }
        }
    }

    [BsonIgnore]
    public int SlotIndex { get; set; } = -1;

    [BsonIgnore]
    public Query? Query { get; set; }

    public void Reset()
    {
        Data = null!;
        SlotIndex = -1;
        Query = null;
    }
}
