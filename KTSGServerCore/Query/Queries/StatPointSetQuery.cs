using cfg;

namespace KTSG.Server;

public sealed class StatPointSetQuery : QuestQuery
{
    private int _slotIndex;
    private int _targetCount;
    private ECompType _countCompType;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _slotIndex = arg1;
        _targetCount = arg2;
        _countCompType = (ECompType)arg3;
        UseLiveOnlyOnInit = false;

        if (Role.RuntimeStatPoints != null)
        {
            Listen(Role.RuntimeStatPoints.RegisterOnChanged(_ => ApplyCount(GetNow())));
        }
    }

    protected override int GetNow()
    {
        if (Role.RuntimeStatPoints == null || _slotIndex < 0 || _slotIndex >= Role.RuntimeStatPoints.Count)
        {
            return 0;
        }

        return Role.RuntimeStatPoints[_slotIndex];
    }

    protected override bool CheckCompleted(int count)
    {
        return Compare(count, _targetCount, _countCompType);
    }
}
