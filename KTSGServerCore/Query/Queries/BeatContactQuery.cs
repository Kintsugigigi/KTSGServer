using cfg;
using KTSG.Server.Model;

namespace KTSG.Server;

public sealed class BeatContactQuery : QuestQuery
{
    private int _configId;
    private int _targetScore;
    private ECompType _scoreCompType;
    private int _targetCount;
    private ECompType _countCompType;
    private bool _recordHistory;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _configId = arg1;
        _targetScore = arg2;
        _scoreCompType = (ECompType)arg3;
        _targetCount = arg4;
        _countCompType = (ECompType)arg5;
        _recordHistory = arg8 == 1;
        UseLiveOnlyOnInit = !_recordHistory;

        Listen(Events.Subscribe<OnContractSuccessEvent>(OnContractSuccess));
    }

    protected override int GetNow()
    {
        if (!_recordHistory || _configId <= 0)
        {
            return 0;
        }

        if (!Player.ContractRecords.TryGetValue(_configId, out List<ContractRecord>? records) || records == null || records.Count == 0)
        {
            return 0;
        }

        int matchedCount = 0;
        for (int i = 0; i < records.Count; i++)
        {
            ContractRecord record = records[i];
            if (record != null && MatchScore(record.Score))
            {
                matchedCount++;
            }
        }

        return matchedCount;
    }

    protected override bool CheckCompleted(int count)
    {
        return Compare(count, _targetCount, _countCompType);
    }

    private bool MatchConfig(int configId)
    {
        return _configId <= 0 || configId == _configId;
    }

    private bool MatchScore(int score)
    {
        if (_targetScore <= 0 || _scoreCompType == ECompType.None)
        {
            return true;
        }

        return Compare(score, _targetScore, _scoreCompType);
    }

    private void OnContractSuccess(OnContractSuccessEvent evt)
    {
        if (!MatchConfig(evt.ConfigId) || !MatchScore(evt.Score))
        {
            return;
        }

        ApplyCount(CurrentCount + 1);
    }
}
