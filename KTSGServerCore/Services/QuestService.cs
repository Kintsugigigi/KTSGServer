using cfg;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Model;
using KTSG.Server.Runtime;
using System.Text;

namespace KTSG.Server.Services;

public sealed class QuestService : IService
{
    private ConfigService _configService = null!;
    private ItemService _itemService = null!;
    private GamePlayService _gamePlayService = null!;
    private QueryService _queryService = null!;
    private PlayerService _playerService = null!;
    private SceneService _sceneService = null!;

    public void Init()
    {
        _configService = ServerRuntime.Instance.GetService<ConfigService>();
        _itemService = ServerRuntime.Instance.GetService<ItemService>();
        _gamePlayService = ServerRuntime.Instance.GetService<GamePlayService>();
        _queryService = ServerRuntime.Instance.GetService<QueryService>();
        _playerService = ServerRuntime.Instance.GetService<PlayerService>();
        _sceneService = ServerRuntime.Instance.GetService<SceneService>();

        PureClassPool.Register(
            static () => new UnlockCondRuntime(),
            onReturn: runtime => runtime.Reset(),
            preloadCount: 4,
            maxCount: 64,
            autoCleanInterval: 30f,
            canAutoClear: false);

        PureClassPool.Register(
            static () => new SubQuestCondRuntime(),
            onReturn: runtime => runtime.Reset(),
            preloadCount: 8,
            maxCount: 128,
            autoCleanInterval: 30f,
            canAutoClear: false);

    }

    public bool BuildRoleQuestRuntime(
        PlayerData player,
        RoleData role,
        out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null)
        {
            reason = "玩家或角色数据为空";
            return false;
        }

        ReleaseRoleQuestRuntime(role);
        role.InitQuestRuntime(player);
        foreach (InteractableProgressData? interactableData in role.InteractableProgresses.Values)
        {
            interactableData?.RecalculateResolvedProgress();
        }

        foreach (var kvp in role.ParentQuests)
        {
            int parentQuestId = kvp.Key;
            ParentQuestData data = kvp.Value;
            bool buildUnlockQueries = data.State == EParentQuestState.Waiting;
            bool buildCurrentQueries = data.State == EParentQuestState.Executing;
            if (!buildUnlockQueries && !buildCurrentQueries)
            {
                continue;
            }

            MainQuestConfig mainQuestConfig = _configService.Tables.TbMainQuestConfig.GetOrDefault(parentQuestId);
            if (mainQuestConfig == null)
            {
                reason = $"父任务配置不存在: {parentQuestId}";
                role.ReleaseQuestRuntime();
                return false;
            }

            ResetParentRuntime(data);
            data.RuntimeParentQuestId = parentQuestId;
            data.RuntimeConfig = mainQuestConfig;
            data.RuntimeEvents = role.RuntimeEvents!;

            if (buildUnlockQueries)
            {
                RecycleUnlockRuntime(data);
                if (mainQuestConfig.PreQuestCond != null)
                {
                    for (int i = 0; i < mainQuestConfig.PreQuestCond.Count; i++)
                    {
                        if (!TryCreateUnlockCond(
                                mainQuestConfig.PreQuestCond[i],
                                player,
                                data.OnUnlockQueryStateChanged,
                        out UnlockCondRuntime runtime,
                        out reason))
                        {
                            RecycleUnlockRuntime(data);
                            RecycleSubQuestRuntime(data);
                            ResetParentRuntime(data);
                            role.ReleaseQuestRuntime();
                            return false;
                        }

                        data.UnlockConds.Add(runtime);
                    }
                }
            }

            if (!buildCurrentQueries || data.CurrentSubQuestId <= 0)
            {
                continue;
            }

            QuestConfig currentSubQuestConfig = _configService.Tables.TbQuestConfig.GetOrDefault(data.CurrentSubQuestId);
            if (currentSubQuestConfig == null)
            {
                reason = $"子任务配置不存在: {data.CurrentSubQuestId}";
                RecycleUnlockRuntime(data);
                RecycleSubQuestRuntime(data);
                ResetParentRuntime(data);
                role.ReleaseQuestRuntime();
                return false;
            }

            data.RuntimeCurrentSubQuestConfig = currentSubQuestConfig;
            RecycleSubQuestRuntime(data);
            if (currentSubQuestConfig.CompleteQueryCond != null)
            {
                while (data.CurrentSubQuestConds.Count < currentSubQuestConfig.CompleteQueryCond.Count)
                {
                    data.CurrentSubQuestConds.Add(new SubQuestCondData { Count = 0 });
                }

                if (data.CurrentSubQuestConds.Count > currentSubQuestConfig.CompleteQueryCond.Count)
                {
                    data.CurrentSubQuestConds.RemoveRange(
                        currentSubQuestConfig.CompleteQueryCond.Count,
                        data.CurrentSubQuestConds.Count - currentSubQuestConfig.CompleteQueryCond.Count);
                }

                for (int i = 0; i < currentSubQuestConfig.CompleteQueryCond.Count; i++)
                {
                    if (!TryAttachSubQuestCondRuntime(
                            currentSubQuestConfig.CompleteQueryCond[i],
                            player,
                            i,
                            data.CurrentSubQuestConds[i],
                            currentSubQuestConfig.CanAutoSuccess ? data.OnCurrentSubQuestQueryStateChanged : null,
                            data.OnCurrentSubQuestProgressChanged,
                            out SubQuestCondRuntime runtime,
                            out reason))
                    {
                        RecycleUnlockRuntime(data);
                        RecycleSubQuestRuntime(data);
                        ResetParentRuntime(data);
                        role.ReleaseQuestRuntime();
                        return false;
                    }

                    data.RuntimeCurrentSubQuestConds.Add(runtime);
                }
            }
        }

        foreach (var kvp in role.ParentQuests)
        {
            ParentQuestData? data = kvp.Value;
            if (data == null)
            {
                continue;
            }

            QueueQuestIfReady(role, data);
        }

        return true;
    }
    
    public void ReleaseRoleQuestRuntime(RoleData role)
    {
        if (role == null || (role.RuntimePlayer == null && role.RuntimeEvents == null))
        {
            return;
        }

        foreach (ParentQuestData data in role.ParentQuests.Values)
        {
            if (data == null)
            {
                continue;
            }

            RecycleUnlockRuntime(data);
            RecycleSubQuestRuntime(data);
            ResetParentRuntime(data);
        }

        role.ReleaseQuestRuntime();
    }
    

    public ParentQuestData AddParentQuestData(RoleData role, int parentQuestId, EParentQuestState state = EParentQuestState.Locking)
    {
        ParentQuestData data = new()
        {
            State = state,
            CurrentSubQuestId = 0
        };
        role.ParentQuests[parentQuestId] = data;
        return data;
    }





    #region 任务信号处理

    public bool TryHandleSignal(PlayerData player, RoleData role, ServerConfigableSignal signal, NRoleSignalChange? change, int sourceParentQuestId, out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null || signal == null)
        {
            reason = "任务信号参数为空";
            return false;
        }

        switch (signal)
        {
            case TryUnlockParentQuestSignal unlockSignal:
                return TryHandleUnlockParentQuestSignal(player, role, unlockSignal.TargetParentQuestID, change, out reason, false);
            case ChangeStackItemSignal stackSignal:
                return TryHandleChangeStackItemSignal(player, role, stackSignal, change, sourceParentQuestId, out reason);
            case AddInstanceItemSignal addInstanceSignal:
                return TryHandleAddInstanceItemSignal(player, role, addInstanceSignal, change, sourceParentQuestId, out reason);
            case ChangeCurrencyItemSignal currencySignal:
                return TryHandleChangeCurrencyItemSignal(role, currencySignal, change, sourceParentQuestId, out reason);
            case TryRewardSignal rewardSignal:
                return TryHandleRewardSignal(player, role, rewardSignal, change, sourceParentQuestId, out reason);
            case TrySetInteractableProgSignal interactableSignal:
                if (sourceParentQuestId <= 0)
                {
                    reason = "TrySetInteractableProgSignal 只能由任务信号链内部触发";
                    return false;
                }

                return _gamePlayService.TrySetInteractableProgress(
                    role,
                    interactableSignal.InteractableID,
                    interactableSignal.Progress,
                    interactableSignal.Priority,
                    sourceParentQuestId,
                    change,
                    out reason);
            case TrySetActiveWayPointSignal wayPointSignal:
                if (sourceParentQuestId <= 0)
                {
                    reason = "TrySetActiveWayPointSignal 只能由任务信号链内部触发";
                    return false;
                }

                return _sceneService.SetWayPointActive(
                    role,
                    wayPointSignal.WayPointID,
                    wayPointSignal.IsActive,
                    change,
                    out reason);
            default:
                reason = $"暂不支持的任务信号类型: {signal.GetType().Name}";
                return false;
        }
    }
    
        private bool TryHandleChangeStackItemSignal(
        PlayerData player,
        RoleData role,
        ChangeStackItemSignal signal,
        NRoleSignalChange? change,
        int sourceParentQuestId,
        out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null || signal == null)
        {
            reason = "堆叠物品信号参数为空";
            return false;
        }

        if (sourceParentQuestId <= 0)
        {
            reason = "堆叠物品变更只能由任务信号链内部触发";
            return false;
        }

        if (signal.Count <= 0 || signal.ConfigID <= 0)
        {
            reason = "堆叠物品变更参数非法";
            return false;
        }

        bool ok;
        NRoleInventoryDelta delta;
        if (signal.IsAdd)
        {
            ok = _itemService.TryAddStackItemToRoleBackpack(role, signal.ConfigID, signal.Count, out delta, out reason);
        }
        else
        {
            ok = _itemService.TryConsumeStackItemFromRoleBackpack(role, signal.ConfigID, signal.Count, out delta, out reason);
        }
        if (!ok)
        {
            return false;
        }

        change?.MergeInventoryDelta(delta);
        return true;
    }

    private bool TryHandleAddInstanceItemSignal(
        PlayerData player,
        RoleData role,
        AddInstanceItemSignal signal,
        NRoleSignalChange? change,
        int sourceParentQuestId,
        out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null || signal == null)
        {
            reason = "实例物品信号参数为空";
            return false;
        }

        if (sourceParentQuestId <= 0)
        {
            reason = "实例物品变更只能由任务信号链内部触发";
            return false;
        }

        if (signal.ConfigID <= 0 || signal.Level < 0)
        {
            reason = "实例物品变更参数非法";
            return false;
        }

        if (!_itemService.TryAddInstanceItemByConfigToRole(player, role, signal.ConfigID, signal.Level, out NRoleInventoryDelta delta, out reason))
        {
            return false;
        }

        change?.MergeInventoryDelta(delta);
        return true;
    }
    
    private bool TryHandleChangeCurrencyItemSignal(
        RoleData role,
        ChangeCurrencyItemSignal signal,
        NRoleSignalChange? change,
        int sourceParentQuestId,
        out string reason)
    {
        reason = string.Empty;
        if (role == null || signal == null)
        {
            reason = "货币变更信号参数为空";
            return false;
        }

        if (sourceParentQuestId <= 0)
        {
            reason = "货币变更只能由任务信号链内部触发";
            return false;
        }

        if (signal.IndexID < 0 || signal.Count <= 0)
        {
            reason = "货币变更参数非法";
            return false;
        }

        if (!_itemService.TryChangeCurrencyToRole(role, signal, out NRoleInventoryDelta delta, out reason))
        {
            return false;
        }

        change?.MergeInventoryDelta(delta);
        return true;
    }

    private bool TryHandleRewardSignal(
        PlayerData player,
        RoleData role,
        TryRewardSignal signal,
        NRoleSignalChange? change,
        int sourceParentQuestId,
        out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null || signal == null)
        {
            reason = "奖励信号参数为空";
            return false;
        }

        int parentQuestId = signal.ParentQuestID > 0 ? signal.ParentQuestID : sourceParentQuestId;
        if (parentQuestId <= 0)
        {
            reason = "奖励信号缺少有效的父任务ID";
            return false;
        }

        if (!role.ParentQuests.TryGetValue(parentQuestId, out ParentQuestData? data) || data == null)
        {
            reason = $"父任务数据不存在: {parentQuestId}";
            return false;
        }

        if (data.State == EParentQuestState.Rewarded)
        {
            return true;
        }

        if (data.State != EParentQuestState.Finished)
        {
            reason = $"父任务当前不可领取奖励: parent={parentQuestId}, state={data.State}";
            return false;
        }

        MainQuestConfig? mainQuestConfig = data.RuntimeConfig ?? _configService.Tables.TbMainQuestConfig.GetOrDefault(parentQuestId);
        if (mainQuestConfig == null)
        {
            reason = $"父任务配置不存在: {parentQuestId}";
            return false;
        }

        if (!_itemService.TryGrantRewards(
                player,
                role,
                ERewardSourceType.RewardSourceQuest,
                parentQuestId,
                mainQuestConfig.QuestItemRewards,
                mainQuestConfig.QuestCurrencyRewards,
                out NRoleInventoryDelta rewardDelta,
                out NGrantedReward reward,
                out reason))
        {
            return false;
        }

        data.State = EParentQuestState.Rewarded;

        if (change != null)
        {
            change.MergeInventoryDelta(rewardDelta);
            change.SetReward(reward);
            change.AddQuestStateSnapshot(parentQuestId, data.State, data.CurrentSubQuestId, data.CurrentSubQuestConds);
        }

        return true;
    }
    
    public bool TryHandleUnlockParentQuestSignal(
        PlayerData player,
        RoleData role,
        int parentQuestId,
        NRoleSignalChange? change,
        out string reason,
        bool drainAfter = true)
    {
        reason = string.Empty;
        if (player == null || role == null || parentQuestId <= 0)
        {
            reason = "任务解锁参数非法";
            return false;
        }


        MainQuestConfig mainQuestConfig = _configService.Tables.TbMainQuestConfig.GetOrDefault(parentQuestId);
        if (mainQuestConfig == null)
        {
            reason = $"父任务配置不存在: {parentQuestId}";
            return false;
        }

        if (!role.ParentQuests.TryGetValue(parentQuestId, out ParentQuestData? data))
        {
            data = AddParentQuestData(role, parentQuestId, EParentQuestState.Waiting);
        }
        else if (data.State == EParentQuestState.Locking)
        {
            data.State = EParentQuestState.Waiting;
        }
        else if (data.State != EParentQuestState.Waiting)
        {
            return true;
        }

        if (data.RuntimeParentQuestId <= 0)
        {
            ResetParentRuntime(data);
            data.RuntimeParentQuestId = parentQuestId;
            data.RuntimeConfig = mainQuestConfig;
            data.RuntimeEvents = role.RuntimeEvents!;
            RecycleUnlockRuntime(data);
            if (mainQuestConfig.PreQuestCond != null)
            {
                for (int i = 0; i < mainQuestConfig.PreQuestCond.Count; i++)
                {
                    if (!TryCreateUnlockCond(
                            mainQuestConfig.PreQuestCond[i],
                            player,
                            data.OnUnlockQueryStateChanged,
                            out UnlockCondRuntime runtime,
                            out reason))
                    {
                        RecycleUnlockRuntime(data);
                        RecycleSubQuestRuntime(data);
                        ResetParentRuntime(data);
                        return false;
                    }

                    data.UnlockConds.Add(runtime);
                }
            }
        }

        QueueQuestIfReady(role, data);

        if (drainAfter)
        {
            return DrainPendingQuestQueue(player, role, change, out reason);
        }

        return true;
    }

    public bool TryProcessPendingParentUnlock(PlayerData player, RoleData role, int parentQuestId, NRoleSignalChange? change, out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null || parentQuestId <= 0)
        {
            reason = "父任务解锁检查参数非法";
            return false;
        }

        if (!role.ParentQuests.TryGetValue(parentQuestId, out ParentQuestData? data) ||
            data == null ||
            data.State != EParentQuestState.Waiting)
        {
            return true;
        }

        if (!data.AreUnlockQueriesCompleted())
        {
            return true;
        }

        if (data.RuntimeConfig.SubQuests == null || data.RuntimeConfig.SubQuests.Count == 0)
        {
            reason = $"父任务未配置子任务序列: {parentQuestId}";
            return false;
        }

        int firstSubQuestId = data.RuntimeConfig.SubQuests[0];
        if (firstSubQuestId <= 0)
        {
            reason = $"父任务首个子任务非法: parent={parentQuestId}, sub={firstSubQuestId}";
            return false;
        }

        QuestConfig firstSubQuestConfig = _configService.Tables.TbQuestConfig.GetOrDefault(firstSubQuestId);
        if (firstSubQuestConfig == null)
        {
            reason = $"首个子任务配置不存在: {firstSubQuestId}";
            return false;
        }

        data.State = EParentQuestState.Executing;
        data.CurrentSubQuestId = firstSubQuestId;
        RecycleUnlockRuntime(data);
        data.RuntimeCurrentSubQuestConfig = firstSubQuestConfig;
        data.CurrentSubQuestReadyNotified = false;

        RecycleSubQuestRuntime(data);
        ClearSubQuestData(data);
        if (firstSubQuestConfig.CompleteQueryCond != null)
        {
            for (int i = 0; i < firstSubQuestConfig.CompleteQueryCond.Count; i++)
            {
                SubQuestCondData condData = new() { Count = 0 };
                data.CurrentSubQuestConds.Add(condData);

                if (!TryAttachSubQuestCondRuntime(
                        firstSubQuestConfig.CompleteQueryCond[i],
                        player,
                        i,
                        condData,
                        firstSubQuestConfig.CanAutoSuccess ? data.OnCurrentSubQuestQueryStateChanged : null,
                        data.OnCurrentSubQuestProgressChanged,
                        out SubQuestCondRuntime runtime,
                        out reason))
                {
                    RecycleUnlockRuntime(data);
                    RecycleSubQuestRuntime(data);
                    ResetParentRuntime(data);
                    return false;
                }

                data.RuntimeCurrentSubQuestConds.Add(runtime);
            }
        }

        change?.AddQuestStateSnapshot(parentQuestId, data.State, data.CurrentSubQuestId, data.CurrentSubQuestConds);

        for (int i = 0; i < firstSubQuestConfig.EnterSignal.Count; i++)
        {
            if (!TryHandleSignal(player, role, firstSubQuestConfig.EnterSignal[i], change, parentQuestId, out reason))
            {
                return false;
            }
        }

        QueueQuestIfReady(role, data);
        return true;
    }

    #endregion
    

   
    
    public bool ApplySubQuestTransition(
        PlayerData player,
        RoleData role,
        int parentQuestId,
        ParentQuestData data,
        int targetSubQuestId,
        NRoleSignalChange? change,
        out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null || data == null || change == null)
        {
            reason = "子任务跳转参数为空";
            return false;
        }

        if (data.State != EParentQuestState.Executing || data.CurrentSubQuestId <= 0)
        {
            reason = $"父任务当前不可执行子任务跳转: parent={parentQuestId}, state={data.State}, sub={data.CurrentSubQuestId}";
            return false;
        }

        if (targetSubQuestId < 0)
        {
            NetLogger.Info(
                $"[QuestDebug] ApplySubQuestTransition finish. RoleId={role.RoleId}, ParentQuestId={parentQuestId}, " +
                $"FromSubQuestId={data.CurrentSubQuestId}, TargetSubQuestId={targetSubQuestId}, StateBefore={data.State}");
            RecycleSubQuestRuntime(data);
            ClearSubQuestData(data);
            data.RuntimeCurrentSubQuestConfig = null;
            data.State = EParentQuestState.Finished;
            change.AddQuestStateSnapshot(parentQuestId, data.State, data.CurrentSubQuestId, data.CurrentSubQuestConds);
            role.RemoveParentQuestInteractableOverrides(parentQuestId, data.RuntimeConfig.AffectedInteractableIDs, change);

            for (int i = 0; i < data.RuntimeConfig.OnSuccessSignalList.Count; i++)
            {
                if (!TryHandleSignal(player, role, data.RuntimeConfig.OnSuccessSignalList[i], change, parentQuestId, out reason))
                {
                    return false;
                }
            }

            return true;
        }

        if (targetSubQuestId <= 0)
        {
            reason = $"目标子任务ID非法: parent={parentQuestId}, target={targetSubQuestId}";
            return false;
        }

        QuestConfig nextSubQuestConfig = _configService.Tables.TbQuestConfig.GetOrDefault(targetSubQuestId);
        if (nextSubQuestConfig == null)
        {
            reason = $"目标子任务配置不存在: {targetSubQuestId}";
            return false;
        }

        if (nextSubQuestConfig.ParentID != parentQuestId)
        {
            reason = $"目标子任务不属于当前父任务: parent={parentQuestId}, target={targetSubQuestId}, targetParent={nextSubQuestConfig.ParentID}";
            return false;
        }

        RecycleSubQuestRuntime(data);
        ClearSubQuestData(data);
        int previousSubQuestId = data.CurrentSubQuestId;
        data.CurrentSubQuestId = targetSubQuestId;
        data.RuntimeCurrentSubQuestConfig = nextSubQuestConfig;
        data.CurrentSubQuestReadyNotified = false;

        if (nextSubQuestConfig.CompleteQueryCond != null)
        {
            for (int i = 0; i < nextSubQuestConfig.CompleteQueryCond.Count; i++)
            {
                SubQuestCondData condData = new() { Count = 0 };
                data.CurrentSubQuestConds.Add(condData);

                if (!TryAttachSubQuestCondRuntime(
                        nextSubQuestConfig.CompleteQueryCond[i],
                        player,
                        i,
                        condData,
                        nextSubQuestConfig.CanAutoSuccess ? data.OnCurrentSubQuestQueryStateChanged : null,
                        data.OnCurrentSubQuestProgressChanged,
                        out SubQuestCondRuntime runtime,
                        out reason))
                {
                    return false;
                }

                data.RuntimeCurrentSubQuestConds.Add(runtime);
            }
        }

        change.AddQuestStateSnapshot(parentQuestId, data.State, data.CurrentSubQuestId, data.CurrentSubQuestConds);
        NetLogger.Info(
            $"[QuestDebug] ApplySubQuestTransition switched. RoleId={role.RoleId}, ParentQuestId={parentQuestId}, " +
            $"FromSubQuestId={previousSubQuestId}, ToSubQuestId={data.CurrentSubQuestId}, State={data.State}, " +
            $"CanAutoSuccess={nextSubQuestConfig.CanAutoSuccess}, ProgressSlots={FormatSubQuestConds(data.CurrentSubQuestConds)}");

        for (int i = 0; i < nextSubQuestConfig.EnterSignal.Count; i++)
        {
            if (!TryHandleSignal(player, role, nextSubQuestConfig.EnterSignal[i], change, parentQuestId, out reason))
            {
                return false;
            }
        }

        QueueQuestIfReady(role, data);
        return true;
    }

    public bool TryBranchSubQuest(
        PlayerData player,
        RoleData role,
        int subQuestId,
        int branchId,
        NRoleSignalChange? change,
        out string reason,
        bool drainAfter = true)
    {
        reason = string.Empty;
        if (player == null || role == null || subQuestId <= 0)
        {
            reason = "子任务分支参数非法";
            return false;
        }
        
        QuestConfig currentSubQuestConfig = _configService.Tables.TbQuestConfig.GetOrDefault(subQuestId);
        if (currentSubQuestConfig == null)
        {
            reason = $"子任务配置不存在: {subQuestId}";
            return false;
        }

        int parentQuestId = currentSubQuestConfig.ParentID;
        if (parentQuestId <= 0)
        {
            reason = $"子任务父任务ID非法: sub={subQuestId}, parent={parentQuestId}";
            return false;
        }

        if (!role.ParentQuests.TryGetValue(parentQuestId, out ParentQuestData? data))
        {
            reason = $"父任务数据不存在: {parentQuestId}";
            return false;
        }

        if (data.State != EParentQuestState.Executing)
        {
            reason = $"父任务不在执行状态: parent={parentQuestId}, state={data.State}";
            return false;
        }

        if (data.CurrentSubQuestId != subQuestId)
        {
            reason = $"当前执行子任务与请求不匹配: current={data.CurrentSubQuestId}, req={subQuestId}";
            return false;
        }

        if (data.RuntimeCurrentSubQuestConfig == null ||
            data.RuntimeCurrentSubQuestConfig.ID != subQuestId)
        {
            reason = $"当前子任务运行时配置不存在或不匹配: current={data.RuntimeCurrentSubQuestConfig?.ID ?? 0}, req={subQuestId}";
            return false;
        }

        if (!data.AreCurrentSubQuestQueriesCompleted())
        {
            reason = $"当前子任务完成条件未满足: sub={subQuestId}";
            return false;
        }

        int targetSubQuestId = 0;
        bool matchedTransition = false;
        for (int i = 0; i < currentSubQuestConfig.TransitionsCond.Count; i++)
        {
            SubQuestTransistion transition = currentSubQuestConfig.TransitionsCond[i];
            if (transition == null)
            {
                continue;
            }

            if (transition.BranchID != branchId && transition.BranchID != -1)
            {
                continue;
            }

            targetSubQuestId = transition.TargetSubQuestID;
            matchedTransition = true;
            break;
        }

        if (!matchedTransition)
        {
            reason = $"未找到可用的子任务跳转分支: sub={subQuestId}, branch={branchId}";
            return false;
        }

        if (!ApplySubQuestTransition(
            player,
            role,
            parentQuestId,
            data,
            targetSubQuestId,
            change ?? new NRoleSignalChange(),
            out reason))
        {
            return false;
        }

        if (drainAfter)
        {
            return DrainPendingQuestQueue(player, role, change, out reason);
        }

        return true;
    }

    public bool TryProcessAutoSubQuestTransition(
        PlayerData player,
        RoleData role,
        int parentQuestId,
        NRoleSignalChange? change,
        out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null || parentQuestId <= 0 || change == null)
        {
            reason = "自动推进参数非法";
            return false;
        }
        

        if (!role.ParentQuests.TryGetValue(parentQuestId, out ParentQuestData? data))
        {
            reason = $"父任务数据不存在: {parentQuestId}";
            return false;
        }

        if (data.State != EParentQuestState.Executing || data.CurrentSubQuestId <= 0)
        {
            return true;
        }

        QuestConfig? currentSubQuestConfig = data.RuntimeCurrentSubQuestConfig;
        if (currentSubQuestConfig == null || !currentSubQuestConfig.CanAutoSuccess)
        {
            return true;
        }

        if (!data.AreCurrentSubQuestQueriesCompleted())
        {
            return true;
        }

        int targetSubQuestId = 0;
        bool matchedTransition = false;
        for (int i = 0; i < currentSubQuestConfig.TransitionsCond.Count; i++)
        {
            SubQuestTransistion transition = currentSubQuestConfig.TransitionsCond[i];
            if (transition == null || transition.BranchID != -1)
            {
                continue;
            }

            targetSubQuestId = transition.TargetSubQuestID;
            matchedTransition = true;
            break;
        }

        if (!matchedTransition)
        {
            return true;
        }

        NetLogger.Info(
            $"[QuestDebug] TryProcessAutoSubQuestTransition ready. RoleId={role.RoleId}, ParentQuestId={parentQuestId}, " +
            $"CurrentSubQuestId={data.CurrentSubQuestId}, TargetSubQuestId={targetSubQuestId}, " +
            $"ProgressSlots={FormatSubQuestConds(data.CurrentSubQuestConds)}");

        return ApplySubQuestTransition(
            player,
            role,
            parentQuestId,
            data,
            targetSubQuestId,
            change,
            out reason);
    }

    public bool DrainPendingQuestQueue(PlayerData player, RoleData role, NRoleSignalChange? change, out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null)
        {
            reason = "任务 drain 参数为空";
            return false;
        }

        if (role.IsQuestDraining)
        {
            return true;
        }

        bool shouldPushChange = change == null;
        NRoleSignalChange actualChange = change ?? new NRoleSignalChange();
        NetLogger.Info(
            $"[QuestDebug] DrainPendingQuestQueue begin. RoleId={role.RoleId}, ShouldPushChange={shouldPushChange}, " +
            $"InitialPendingCount={role.PendingParentQuestIds.Count}, ExistingSnapshots={FormatQuestSnapshots(actualChange.QuestStateSnapshot)}");

        role.IsQuestDraining = true;
        try
        {
            while (role.PendingParentQuestIds.Count > 0)
            {
                int parentQuestId = role.PendingParentQuestIds.Dequeue();

                if (!role.PendingParentQuestIdSet.Remove(parentQuestId))
                {
                    continue;
                }

                NetLogger.Info(
                    $"[QuestDebug] DrainPendingQuestQueue dequeue. RoleId={role.RoleId}, ParentQuestId={parentQuestId}, " +
                    $"RemainingPendingCount={role.PendingParentQuestIds.Count}, SnapshotsBeforeProcess={FormatQuestSnapshots(actualChange.QuestStateSnapshot)}");

                if (!role.ParentQuests.TryGetValue(parentQuestId, out ParentQuestData? data) || data == null)
                {
                    continue;
                }

                bool ok;
                if (data.State == EParentQuestState.Waiting)
                {
                    ok = TryProcessPendingParentUnlock(player, role, parentQuestId, actualChange, out reason);
                }
                else if (data.State == EParentQuestState.Executing)
                {
                    ok = TryProcessAutoSubQuestTransition(player, role, parentQuestId, actualChange, out reason);
                }
                else
                {
                    continue;
                }

                if (!ok)
                {
                    return false;
                }

                NetLogger.Info(
                    $"[QuestDebug] DrainPendingQuestQueue processed. RoleId={role.RoleId}, ParentQuestId={parentQuestId}, " +
                    $"StateAfter={data.State}, CurrentSubQuestId={data.CurrentSubQuestId}, " +
                    $"SnapshotsAfterProcess={FormatQuestSnapshots(actualChange.QuestStateSnapshot)}");
            }

            if (shouldPushChange && !IsEmpty(actualChange))
            {
                NetLogger.Info(
                    $"[QuestDebug] DrainPendingQuestQueue push change. RoleId={role.RoleId}, " +
                    $"QuestSnapshots={FormatQuestSnapshots(actualChange.QuestStateSnapshot)}");
                if (!_playerService.TryPushMessage(player, actualChange, out string pushReason))
                {
                    NetLogger.Warning($"[Quest] Auto change push fail. Uid={player.Uid}, RoleId={role.RoleId}, Reason={pushReason}");
                }
            }

            return true;
        }
        finally
        {
            role.IsQuestDraining = false;
        }
    }

    private void QueueQuestIfReady(RoleData role, ParentQuestData data)
    {
        if (role == null || data == null)
        {
            return;
        }

        if (data.State == EParentQuestState.Waiting)
        {
            if (data.AreUnlockQueriesCompleted())
            {
                role.EnqueueQuestPending(data.RuntimeParentQuestId);
            }

            return;
        }

        if (data.State == EParentQuestState.Executing &&
            data.CurrentSubQuestId > 0 &&
            data.RuntimeCurrentSubQuestConfig != null &&
            data.RuntimeCurrentSubQuestConfig.CanAutoSuccess &&
            data.AreCurrentSubQuestQueriesCompleted())
        {
            role.EnqueueQuestPending(data.RuntimeParentQuestId);
        }
    }

    private static bool IsEmpty(NRoleSignalChange? change)
    {
        if (change == null)
        {
            return true;
        }

        return change.InventoryDelta == null &&
               change.Reward == null &&
               change.QuestStateSnapshot.Count == 0 &&
               change.InteractableSnapshot.Count == 0 &&
               !change.ActiveWaypointIdsChanged &&
               change.ActiveWaypointIds.Count == 0;
    }

    private static string FormatQuestSnapshots(IList<NParentQuestSnapshot> snapshots)
    {
        if (snapshots == null || snapshots.Count == 0)
        {
            return "[]";
        }

        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < snapshots.Count; i++)
        {
            NParentQuestSnapshot snapshot = snapshots[i];
            if (i > 0)
            {
                sb.Append(" | ");
            }

            if (snapshot == null)
            {
                sb.Append("null");
                continue;
            }

            sb.Append("parent=").Append(snapshot.ParentQuestId)
                .Append(",state=").Append(snapshot.ParentState)
                .Append(",sub=").Append(snapshot.CurrentSubQuestId)
                .Append(",progress=");

            if (snapshot.CurrentProgress == null || snapshot.CurrentProgress.Count == 0)
            {
                sb.Append("[]");
                continue;
            }

            sb.Append("[");
            for (int j = 0; j < snapshot.CurrentProgress.Count; j++)
            {
                NQuestCondProgress progress = snapshot.CurrentProgress[j];
                if (j > 0)
                {
                    sb.Append(",");
                }

                if (progress == null)
                {
                    sb.Append("null");
                    continue;
                }

                sb.Append(progress.SlotIndex).Append(":").Append(progress.Count);
            }

            sb.Append("]");
        }

        sb.Append("]");
        return sb.ToString();
    }

    private static string FormatSubQuestConds(IList<SubQuestCondData> conds)
    {
        if (conds == null || conds.Count == 0)
        {
            return "[]";
        }

        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < conds.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(",");
            }

            SubQuestCondData cond = conds[i];
            sb.Append(i).Append(":").Append(cond?.Count ?? -1);
        }

        sb.Append("]");
        return sb.ToString();
    }

    public void RecycleUnlockRuntime(ParentQuestData data)
    {
        if (data == null)
        {
            return;
        }

        for (int i = 0; i < data.UnlockConds.Count; i++)
        {
            UnlockCondRuntime? runtime = data.UnlockConds[i];
            if (runtime == null)
            {
                continue;
            }

            if (runtime.Query != null)
            {
                _queryService.RecycleQuery(runtime.Query);
                runtime.Query = null;
            }

            PureClassPool.Return(runtime);
        }

        data.UnlockConds.Clear();
    }

    public void RecycleSubQuestRuntime(ParentQuestData data)
    {
        if (data == null)
        {
            return;
        }

        for (int i = 0; i < data.RuntimeCurrentSubQuestConds.Count; i++)
        {
            SubQuestCondRuntime? runtime = data.RuntimeCurrentSubQuestConds[i];
            if (runtime == null)
            {
                continue;
            }

            if (runtime.Query != null)
            {
                _queryService.RecycleQuery(runtime.Query);
                runtime.Query = null;
            }

            runtime.SlotIndex = -1;
            runtime.Data = null!;
            PureClassPool.Return(runtime);
        }

        data.RuntimeCurrentSubQuestConds.Clear();
    }

    public void ClearSubQuestData(ParentQuestData data)
    {
        if (data == null)
        {
            return;
        }

        for (int i = 0; i < data.CurrentSubQuestConds.Count; i++)
        {
            data.CurrentSubQuestConds[i]?.Reset();
        }

        data.CurrentSubQuestConds.Clear();
    }

    public void ResetParentRuntime(ParentQuestData data)
    {
        if (data == null)
        {
            return;
        }

        data.RuntimeParentQuestId = 0;
        data.RuntimeConfig = null;
        data.RuntimeCurrentSubQuestConfig = null;
        data.RuntimeEvents = null;
        data.UnlockReadyNotified = false;
        data.CurrentSubQuestReadyNotified = false;
    }
    
    
    #region 基础运行时Cond创建

    private bool TryCreateUnlockCond(
        ConfigableQuery? config,
        PlayerData player,
        Action<bool>? onCompletedChanged,
        out UnlockCondRuntime runtime,
        out string reason)
    {
        runtime = PureClassPool.Get<UnlockCondRuntime>();
        reason = string.Empty;
        if (config == null || (int)config.QueryType == 0)
        {
            return true;
        }

        if (!_queryService.TryCreateQueryRaw((int)config.QueryType, out Query? query) || query == null)
        {
            reason = $"任务 Query 创建失败: type={(int)config.QueryType}";
            PureClassPool.Return(runtime);
            return false;
        }

        if (query is not QuestQuery questQuery)
        {
            reason = $"任务 Query 类型未继承 QuestQuery: type={(int)config.QueryType}";
            _queryService.RecycleQuery(query);
            PureClassPool.Return(runtime);
            return false;
        }

        query.Init(player, config.Para1, config.Para2, config.Para3, config.Para4, config.Para5, config.Para6, config.Para7, config.Para8);

        questQuery.BindRuntime(runtime);
        runtime.Query = query;
        if (onCompletedChanged != null)
        {
            query.IsCompleted.RegisterOnValueChanged(onCompletedChanged);
        }

        return true;
    }

    private bool TryAttachSubQuestCondRuntime(
        SubQuestsCond? configItem,
        PlayerData player,
        int slotIndex,
        SubQuestCondData data,
        Action<bool>? onCompletedChanged,
        Action<SubQuestCondRuntime>? onProgressChanged,
        out SubQuestCondRuntime runtime,
        out string reason)
    {
        runtime = PureClassPool.Get<SubQuestCondRuntime>();
        runtime.Data = data ?? throw new ArgumentNullException(nameof(data));
        runtime.SlotIndex = slotIndex;
        reason = string.Empty;

        ConfigableQuery? config = configItem?.Query;
        if (config == null || (int)config.QueryType == 0)
        {
            return true;
        }

        if (!_queryService.TryCreateQueryRaw((int)config.QueryType, out Query? query) || query == null)
        {
            reason = $"任务 Query 创建失败: type={(int)config.QueryType}";
            PureClassPool.Return(runtime);
            return false;
        }

        if (query is not QuestQuery questQuery)
        {
            reason = $"任务 Query 类型未继承 QuestQuery: type={(int)config.QueryType}";
            _queryService.RecycleQuery(query);
            PureClassPool.Return(runtime);
            return false;
        }

        query.Init(player, config.Para1, config.Para2, config.Para3, config.Para4, config.Para5, config.Para6, config.Para7, config.Para8);

        questQuery.BindRuntime(runtime, configItem.CanNotify ? onProgressChanged : null);
        runtime.Query = query;
        if (onCompletedChanged != null)
        {
            query.IsCompleted.RegisterOnValueChanged(onCompletedChanged);
        }

        return true;
    }

    #endregion

    

}
