using cfg;
using KTSG.Server.Model;

namespace KTSG.Server;

public sealed class KillMonsterQuery : QuestQuery
{
    private int _configId;
    private int _targetCount;
    private ECompType _countCompType;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _configId = arg1;
        _targetCount = arg2;
        _countCompType = (ECompType)arg3;
        UseLiveOnlyOnInit = false;

        Listen(Events.Subscribe<OnMonsterKilledEvent>(OnMonsterKilled));
    }

    protected override int GetNow()
    {
        if (_configId <= 0)
        {
            return 0;
        }

        return Role.MonsterKills.GetValueOrDefault(_configId);
    }

    protected override bool CheckCompleted(int count)
    {
        return Compare(count, _targetCount, _countCompType);
    }

    private bool MatchConfig(int configId)
    {
        return _configId <= 0 || configId == _configId;
    }

    private void OnMonsterKilled(OnMonsterKilledEvent evt)
    {
        if (!MatchConfig(evt.ConfigId))
        {
            return;
        }

        ApplyCount(GetNow());
    }
}
