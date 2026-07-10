using System;
using System.Collections.Generic;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Model;
using cfg;

namespace KTSG.Server.Services;

public sealed class SceneService : IService
{
    private ConfigService _configService = null!;
    private GamePlayService _gamePlayService = null!;
    private ItemService _itemService = null!;
    private readonly Random _random = new();

    public void Init()
    {
        _configService = ServerRuntime.Instance.GetService<ConfigService>();
        _gamePlayService = ServerRuntime.Instance.GetService<GamePlayService>();
        _itemService = ServerRuntime.Instance.GetService<ItemService>();
    }

    public bool TryBuildSceneEnemies(int sceneCid, out RspSceneEnemies rsp, out string reason)
    {
        rsp = new RspSceneEnemies();
        reason = string.Empty;

        var sceneConfig = _configService.Tables.TbSceneEnemyConfig.GetOrDefault(sceneCid);
        if (sceneConfig == null)
        {
            NetLogger.Warning($"[SceneService] TryBuildSceneEnemies config missing. SceneCid={sceneCid}");
            reason = $"场景敌人配置不存在: {sceneCid}";
            rsp.Result = new Result
            {
                Code = ResultCode.Fail,
                Msg = reason
            };
            return false;
        }

        rsp.Result = new Result
        {
            Code = ResultCode.Success,
            Msg = "success"
        };

        int configEnemyCount = sceneConfig.SceneEnemies?.Count ?? 0;
        NetLogger.Info(
            $"[SceneService] TryBuildSceneEnemies begin. " +
            $"SceneCid={sceneCid}, ConfigEnemyCount={configEnemyCount}");

        if (sceneConfig.SceneEnemies == null || sceneConfig.SceneEnemies.Count == 0)
        {
            NetLogger.Info($"[SceneService] TryBuildSceneEnemies no enemy entries. SceneCid={sceneCid}");
            return true;
        }

        for (int i = 0; i < sceneConfig.SceneEnemies.Count; i++)
        {
            SceneEnemy sceneEnemy = sceneConfig.SceneEnemies[i];
            if (sceneEnemy == null)
            {
                NetLogger.Warning(
                    $"[SceneService] TryBuildSceneEnemies skip null config entry. " +
                    $"SceneCid={sceneCid}, Index={i}");
                continue;
            }

            NetLogger.Info(
                $"[SceneService] TryBuildSceneEnemies config entry. " +
                $"SceneCid={sceneCid}, Index={i}, EnemyUid={sceneEnemy.UID}, ConfigId={sceneEnemy.ConfigID}, " +
                $"WeaponId={sceneEnemy.WeaponConfigID}, PatrolWayPoints={FormatIntList(sceneEnemy.PatrolWayPoints)}");

            NEnemyItem enemyItem = new NEnemyItem
            {
                Result = new Result
                {
                    Code = ResultCode.Success,
                    Msg = "success"
                },
                EnemyUid = sceneEnemy.UID,
                ConfigId = sceneEnemy.ConfigID,
                WeaponId = sceneEnemy.WeaponConfigID
            };

            if (sceneEnemy.PatrolWayPoints != null && sceneEnemy.PatrolWayPoints.Count > 0)
            {
                enemyItem.PatrolWayPoints.Add(sceneEnemy.PatrolWayPoints);
            }

            rsp.Enemies.Add(enemyItem);
            NetLogger.Info(
                $"[SceneService] TryBuildSceneEnemies rsp entry appended. " +
                $"SceneCid={sceneCid}, Index={i}, EnemyUid={enemyItem.EnemyUid}, ConfigId={enemyItem.ConfigId}, " +
                $"WeaponId={enemyItem.WeaponId}, PatrolWayPoints={FormatIntList(enemyItem.PatrolWayPoints)}");
        }

        NetLogger.Info(
            $"[SceneService] TryBuildSceneEnemies done. " +
            $"SceneCid={sceneCid}, ResponseEnemyCount={rsp.Enemies.Count}");

        return true;
    }

    public bool TryRecordEnemyKill(RoleData role, long enemyUid, int configId, out string reason)
    {
        reason = string.Empty;
        if (role == null || enemyUid <= 0 || configId <= 0)
        {
            reason = "敌人击杀统计参数非法";
            return false;
        }

        _gamePlayService.NotifyMonsterKill(role, configId, 1);
        return true;
    }

    public bool TryGrantEnemyDropReward(
        PlayerData player,
        RoleData role,
        long enemyUid,
        out NRoleInventoryDelta delta,
        out NGrantedReward reward,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reward = new NGrantedReward
        {
            SourceType = ERewardSourceType.RewardSourceEnemyDrop,
            SourceId = enemyUid > int.MaxValue ? int.MaxValue : (int)enemyUid
        };
        reason = string.Empty;

        if (player == null || role == null || enemyUid <= 0)
        {
            reason = "敌人掉落奖励参数非法";
            return false;
        }

        EnemyDropConfig dropConfig = _configService.Tables.TbEnemyDropConfig.GetOrDefault((int)enemyUid);
        if (dropConfig == null)
        {
            reason = $"敌人掉落配置不存在: enemyUid={enemyUid}";
            return false;
        }

        if (dropConfig.DropItem == null || dropConfig.DropItem.Count == 0)
        {
            return true;
        }

        if (!TryRollEnemyDropGroup(dropConfig.DropItem, out EnemyDropItem selectedGroup, out reason))
        {
            return false;
        }

        if (selectedGroup == null || selectedGroup.RewardItems == null || selectedGroup.RewardItems.Count == 0)
        {
            return true;
        }
        return _itemService.TryGrantEnemyDropRewards(
            player,
            role,
            reward.SourceId,
            selectedGroup.RewardItems,
            out delta,
            out reward,
            out reason);
    }

    public bool SetWayPointActive(RoleData role, int wayPointId, bool isActive, NRoleSignalChange? change, out string reason)
    {
        reason = string.Empty;
        if (role == null || wayPointId <= 0)
        {
            reason = "路点激活参数非法";
            return false;
        }

        if (!role.TryApplyActiveWayPointRequest(wayPointId, isActive, out bool activeStateChanged, out reason))
        {
            return false;
        }

        if (activeStateChanged)
        {
            change?.ReplaceActiveWaypointIds(role.GetActiveWayPointIds());
        }

        return true;
    }

    private bool TryRollEnemyDropGroup(
        IReadOnlyList<EnemyDropItem> dropItems,
        out EnemyDropItem selectedGroup,
        out string reason)
    {
        selectedGroup = null!;
        reason = string.Empty;

        if (dropItems == null || dropItems.Count == 0)
        {
            reason = "敌人掉落组为空";
            return false;
        }

        float totalRatio = 0f;
        for (int i = 0; i < dropItems.Count; i++)
        {
            EnemyDropItem dropItem = dropItems[i];
            if (dropItem == null || dropItem.Ratio <= 0f)
            {
                continue;
            }

            totalRatio += dropItem.Ratio;
        }

        if (totalRatio <= 0f)
        {
            reason = "敌人掉落概率总和非法";
            return false;
        }

        double roll = _random.NextDouble() * totalRatio;
        float cumulative = 0f;
        for (int i = 0; i < dropItems.Count; i++)
        {
            EnemyDropItem dropItem = dropItems[i];
            if (dropItem == null || dropItem.Ratio <= 0f)
            {
                continue;
            }

            cumulative += dropItem.Ratio;
            if (roll <= cumulative)
            {
                selectedGroup = dropItem;
                return true;
            }
        }

        for (int i = dropItems.Count - 1; i >= 0; i--)
        {
            EnemyDropItem dropItem = dropItems[i];
            if (dropItem != null && dropItem.Ratio > 0f)
            {
                selectedGroup = dropItem;
                return true;
            }
        }

        reason = "敌人掉落组选择失败";
        return false;
    }

    private static string FormatIntList(IReadOnlyList<int>? values)
    {
        if (values == null || values.Count == 0)
        {
            return "[]";
        }

        return "[" + string.Join(", ", values) + "]";
    }
}
