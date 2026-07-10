using cfg;
using KTSG.Server;
using KTSG.Server.Model;
using KTSG.Server.Services;

namespace KTSG.Server;

public sealed class QueryService : IService
{
    private const int QueryArgCount = 8;

    private readonly Dictionary<int, Func<Query>> _factories = new();

    public void Init()
    {
        RegisterQueryPool<BoardConfigIDQuery>((int)EConfigableQueryType.BoardConfigIDQuery);
        RegisterQueryPool<StatPointSetQuery>((int)EConfigableQueryType.StatPointSetQuery);
        RegisterQueryPool<StackItemQuery>((int)EConfigableQueryType.StackItemQuery);
        RegisterQueryPool<WeaponSetItemQuery>((int)EConfigableQueryType.WeaponSetItemQuery);
        RegisterQueryPool<EquipSetItemQuery>((int)EConfigableQueryType.EquipSetItemQuery);
        RegisterQueryPool<KillMonsterQuery>((int)EConfigableQueryType.KillMonsterQuery);
        RegisterQueryPool<BeatContactQuery>((int)EConfigableQueryType.BeatContactQuery);
        RegisterQueryPool<DeathCountQuery>((int)EConfigableQueryType.DeathCountQuery);
        RegisterQueryPool<InstanceItemQuery>((int)EConfigableQueryType.InstanceItemQuery);
        RegisterQueryPool<WayPointTriggerQuery>((int)EConfigableQueryType.WayPointTriggerQuery);
    }

    private void RegisterQueryPool<TQuery>(int queryTypeId)
        where TQuery : Query, new()
    {
        PureClassPool.Register(
            static () => new TQuery(),
            onReturn: query => query.Reset(),
            preloadCount: 2,
            maxCount: 16,
            autoCleanInterval: 30f,
            canAutoClear: false);

        _factories[queryTypeId] = static () => PureClassPool.Get<TQuery>();
    }

    public bool TryCreateQueryRaw(int queryTypeId, out Query? query)
    {
        query = null;
        if (!_factories.TryGetValue(queryTypeId, out Func<Query>? factory))
        {
            return false;
        }

        query = factory();
        return true;
    }

    public void RecycleQuery(Query? query)
    {
        query?.Recycle();
    }
}
