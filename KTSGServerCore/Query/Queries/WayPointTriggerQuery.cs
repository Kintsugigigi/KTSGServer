namespace KTSG.Server;

public sealed class WayPointTriggerQuery : QuestQuery
{
    private int _wayPointId;

    protected override void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8)
    {
        _wayPointId = arg1;
        UseLiveOnlyOnInit = true;

        Listen(Events.Subscribe<OnWayPointTriggeredEvent>(OnWayPointTriggered));
    }

    protected override int GetNow()
    {
        return 0;
    }

    protected override bool CheckCompleted(int count)
    {
        return count > 0;
    }

    private void OnWayPointTriggered(OnWayPointTriggeredEvent evt)
    {
        if (_wayPointId <= 0 || evt.WayPointId == _wayPointId)
        {
            ApplyCount(1);
        }
    }
}
