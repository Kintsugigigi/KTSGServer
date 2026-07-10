using KTSG.Server.Model;

namespace KTSG.Server;

public abstract class QuestQuery : Query
{
    private IQuestCondRuntime _runtime = null!;
    private Action<SubQuestCondRuntime>? _onVisibleCountChanged;
    public bool UseLiveOnlyOnInit { get; protected set; }
    protected int CurrentCount => _runtime.Count;

    public void BindRuntime(UnlockCondRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        runtime.Query = this;
        _onVisibleCountChanged = null;
        ApplyCount(UseLiveOnlyOnInit ? runtime.Count : GetNow(), false);
    }

    public void BindRuntime(SubQuestCondRuntime runtime, Action<SubQuestCondRuntime>? onVisibleCountChanged = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        runtime.Query = this;
        _onVisibleCountChanged = onVisibleCountChanged;
        ApplyCount(UseLiveOnlyOnInit ? runtime.Count : GetNow(), false);
    }

    protected void ApplyCount(int count, bool emitNotify = true)
    {
        if (_runtime == null)
        {
            throw new InvalidOperationException("Quest runtime is not bound.");
        }

        int nextCount = Math.Max(0, count);
        _runtime.Count = nextCount;
        IsCompleted.Value = CheckCompleted(nextCount);

        if (emitNotify && _runtime is SubQuestCondRuntime runtime)
        {
            _onVisibleCountChanged?.Invoke(runtime);
        }
    }

    protected override void OnReset()
    {
        if (_runtime is UnlockCondRuntime unlockRuntime && unlockRuntime.Query == this)
        {
            unlockRuntime.Query = null;
        }
        else if (_runtime is SubQuestCondRuntime subQuestRuntime && subQuestRuntime.Query == this)
        {
            subQuestRuntime.Query = null;
        }

        _runtime = null!;
        _onVisibleCountChanged = null;
        UseLiveOnlyOnInit = false;
    }
}
