using KTSG.Proto;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace KTSG.Server.Model;

[BsonIgnoreExtraElements]
public class InteractableProgressOverrideData
{
    public int Progress { get; set; }

    public int Priority { get; set; }

    public int UpdateSeq { get; set; }
}

[BsonIgnoreExtraElements]
public class InteractableProgressData
{
    public int BaseProgress { get; set; }

    public int ResolvedProgress { get; set; }

    public int UpdateSeq { get; set; }

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, InteractableProgressOverrideData> Overrides { get; set; } = new();

    public void SetBaseProgress(int progress)
    {
        BaseProgress = progress;
        RecalculateResolvedProgress();
    }

    public void SetOverride(int parentQuestId, int progress, int priority)
    {
        if (parentQuestId <= 0)
        {
            return;
        }

        UpdateSeq++;
        Overrides[parentQuestId] = new InteractableProgressOverrideData
        {
            Progress = progress,
            Priority = priority,
            UpdateSeq = UpdateSeq
        };

        RecalculateResolvedProgress();
    }

    public bool RemoveParentOverrides(int parentQuestId)
    {
        if (parentQuestId <= 0)
        {
            return false;
        }

        if (!Overrides.Remove(parentQuestId))
        {
            return false;
        }

        RecalculateResolvedProgress();
        return true;
    }

    public void RecalculateResolvedProgress()
    {
        int resolvedProgress = -1;
        int bestPriority = int.MinValue;
        int bestUpdateSeq = int.MinValue;

        foreach ((int _, InteractableProgressOverrideData? current) in Overrides)
        {
            if (current == null)
            {
                continue;
            }

            if (current.Priority > bestPriority ||
                (current.Priority == bestPriority && current.UpdateSeq > bestUpdateSeq))
            {
                bestPriority = current.Priority;
                bestUpdateSeq = current.UpdateSeq;
                resolvedProgress = current.Progress;
            }
        }

        ResolvedProgress = resolvedProgress;
    }

    public NInteractableProgressSnapshot ToDelta(int interactableId)
    {
        return new NInteractableProgressSnapshot
        {
            InteractableId = interactableId,
            BaseProgress = BaseProgress,
            ResolvedProgress = ResolvedProgress
        };
    }
}
