using cfg;

namespace KTSG.Server;

public sealed class BoardConfigIDQuery : QuestQuery
{
    private int _targetConfigId;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _targetConfigId = arg1;
        UseLiveOnlyOnInit = false;
    }

    protected override int GetNow()
    {
        return Role?.RoleCid ?? 0;
    }

    protected override bool CheckCompleted(int count)
    {
        return count == _targetConfigId;
    }
}
