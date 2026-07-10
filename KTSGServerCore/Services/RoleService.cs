using System.Collections.Generic;
using cfg;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Model;
using KTSG.Server.Runtime;
using MongoDB.Bson;

namespace KTSG.Server.Services;

public sealed class RoleService : IService
{
    private const int CurrencyCidForStatPointLevelUp = 0;
    private const int StatPointDetailCount = 5;
    private const int StatPointLevelIndex = 5;
    private const int InitialLevel = 1;
    private const int InitialLevelUpCost = 400;
    private const int LevelUpCostStep = 100;

    private PlayerService _playerService = null!;
    private ConfigService _configService = null!;
    private ItemService _itemService = null!;
    private QuestService _questService = null!;

    public void Init()
    {
        _playerService = ServerRuntime.Instance.GetService<PlayerService>();
        _configService = ServerRuntime.Instance.GetService<ConfigService>();
        _itemService = ServerRuntime.Instance.GetService<ItemService>();
        _questService = ServerRuntime.Instance.GetService<QuestService>();
    }

    public bool CreateRole(int connectionId, int cid, out RoleData role, out string reason)
    {
        role = null!;
        reason = string.Empty;

        if (!_playerService.TryGetOnlinePlayer(connectionId, out var player, out reason))
        {
            return false;
        }

        var baseConfig = _configService.Tables.TbCharacterBaseConfig.GetOrDefault(cid);
        if (baseConfig == null)
        {
            reason = $"角色配置不存在: {cid}";
            return false;
        }

        role = new RoleData
        {
            RoleId = ObjectId.GenerateNewId().ToString(),
            RoleCid = cid,
            SceneCid = 1003,
            RevivePointCid = 0,
            StatPoints = new List<int>(baseConfig.BasePoints),
            MonsterKills = new Dictionary<int, int>(),
            DeathByMonsterCounts = new Dictionary<int, int>(),
            InteractableProgresses = new Dictionary<int, InteractableProgressData>(),
            ContractReadies = new Dictionary<int, ContractReady>(),
            ParentQuests = new Dictionary<int, ParentQuestData>(),
            ActiveWayPointRequestCounts = new Dictionary<int, int>(),
            Currencies = CreateCurrencies(),
            Backpack = CreateBackpack(),
            WeaponSlots = CreateLongSlotList(RoleData.FixedWeaponSlotCount),
            EquipSlots = CreateLongSlotList(RoleData.FixedEquipSlotCount),
            ConsumableSlots = CreateIntSlotList(RoleData.FixedConsumableSlotCount),
        };

        role.EnsureRuntimeCollections();

        if (!_itemService.TryGrantDefaultRoleLoadout(player, role, out reason))
        {
            role = null!;
            return false;
        }

        if (!role.TryAddParentQuestData(1, EParentQuestState.Waiting))
        {
            reason = "初始化默认父任务失败";
            role = null!;
            return false;
        }

        player.Roles ??= new List<RoleData>();
        player.Roles.Add(role);

        if (!_playerService.TrySavePlayer(player, out reason))
        {
            player.Roles.Remove(role);
            return false;
        }

        return true;
    }

    private static List<CurrencyItemData> CreateCurrencies()
    {
        return new List<CurrencyItemData>
        {
            new CurrencyItemData { Cid = 0, Count = 1000 },
            new CurrencyItemData { Cid = 1, Count = 0 },
            new CurrencyItemData { Cid = 2, Count = 0 },
            new CurrencyItemData { Cid = 3, Count = 0 }
        };
    }

    private static Dictionary<int, InventoryItemData> CreateBackpack()
    {
        return new Dictionary<int, InventoryItemData>();
    }

    private static List<long> CreateLongSlotList(int count)
    {
        var slots = new List<long>(count);
        for (int i = 0; i < count; i++)
        {
            slots.Add(-1);
        }

        return slots;
    }

    private static List<int> CreateIntSlotList(int count)
    {
        var slots = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            slots.Add(-1);
        }

        return slots;
    }

    public bool TrySelectRole(
        int connectionId,
        string roleId,
        out RoleData role,
        out string reason)
    {
        role = null!;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(roleId))
        {
            reason = "角色ID不能为空";
            return false;
        }

        if (!_playerService.TryGetOnlinePlayer(connectionId, out var player, out reason))
        {
            return false;
        }

        if (player.Roles == null || player.Roles.Count <= 0)
        {
            reason = "玩家没有可用角色";
            return false;
        }

        RoleData? matchedRole = null;
        for (int i = 0; i < player.Roles.Count; i++)
        {
            RoleData? currentRole = player.Roles[i];
            if (currentRole != null && currentRole.RoleId == roleId)
            {
                matchedRole = currentRole;
                break;
            }
        }

        if (matchedRole == null)
        {
            reason = $"角色不存在或不属于当前玩家: {roleId}";
            return false;
        }

        if (player.CurrentRole != null && !ReferenceEquals(player.CurrentRole, matchedRole))
        {
            _questService.ReleaseRoleQuestRuntime(player.CurrentRole);
        }

        role = matchedRole;
        player.CurrentRole = role;
        if (!InitRoleRuntimeData(role, out reason))
        {
            role = null!;
            return false;
        }

        if (!_questService.BuildRoleQuestRuntime(player, role, out reason))
        {
            role = null!;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool TryGetCurrentRole(PlayerData player, out RoleData role, out string reason)
    {
        role = null!;
        reason = string.Empty;

        if (player == null)
        {
            reason = "玩家数据为空";
            return false;
        }

        if (player.Roles == null || player.Roles.Count <= 0)
        {
            reason = "玩家没有可用角色";
            return false;
        }

        if (player.CurrentRole == null)
        {
            reason = "当前角色未锁定";
            return false;
        }

        role = player.CurrentRole;
        return true;
    }

    public bool TrySaveCurrentRoleRevivePoint(
        int connectionId,
        int sceneCid,
        int revivePointCid,
        out NRoleRevivePoint revivePoint,
        out string reason)
    {
        revivePoint = null!;
        reason = string.Empty;

        if (sceneCid <= 0 || revivePointCid < 0)
        {
            reason = $"复活点参数非法: sceneCid={sceneCid}, revivePointCid={revivePointCid}";
            return false;
        }

        if (!_playerService.TryGetOnlinePlayer(connectionId, out var player, out reason) ||
            !TryGetCurrentRole(player, out var role, out reason))
        {
            return false;
        }

        int oldSceneCid = role.SceneCid;
        int oldRevivePointCid = role.RevivePointCid;
        role.SceneCid = sceneCid;
        role.RevivePointCid = revivePointCid;

        if (!_playerService.TrySavePlayer(player, out reason))
        {
            role.SceneCid = oldSceneCid;
            role.RevivePointCid = oldRevivePointCid;
            return false;
        }

        revivePoint = CreateRoleRevivePoint(role);
        reason = string.Empty;
        return true;
    }

    public bool TryGetCurrentRoleRevivePoint(
        int connectionId,
        out NRoleRevivePoint revivePoint,
        out string reason)
    {
        revivePoint = null!;
        reason = string.Empty;

        if (!_playerService.TryGetOnlinePlayer(connectionId, out var player, out reason) ||
            !TryGetCurrentRole(player, out var role, out reason))
        {
            return false;
        }

        revivePoint = CreateRoleRevivePoint(role);
        return true;
    }

    private static NRoleRevivePoint CreateRoleRevivePoint(RoleData role)
    {
        return new NRoleRevivePoint
        {
            SceneCid = role.SceneCid,
            RevivePointCid = role.RevivePointCid
        };
    }

    public bool InitRoleRuntimeData(RoleData role, out string reason)
    {
        reason = string.Empty;

        if (role == null)
        {
            reason = "角色数据为空";
            return false;
        }

        role.ReloadRuntimeCollectionsFromPersistent();

        if (role.RuntimeBackpack == null)
        {
            reason = "角色背包为空";
            return false;
        }

        foreach (KeyValuePair<int, InventoryItemData> entry in role.RuntimeBackpack.ItemsBySlot)
        {
            int configId = entry.Value.ConfigId;
            if (configId <= 0)
            {
                reason = $"物品配置非法: configId={configId}";
                return false;
            }

            ItemConfig itemConfig = _configService.Tables.TbItemConfig.GetOrDefault(configId);
            if (itemConfig == null)
            {
                reason = $"物品配置不存在: configId={configId}";
                return false;
            }
        }

        return true;
    }

    public bool TryApplyStatPoints(
        PlayerData player,
        RoleData role,
        IReadOnlyList<int> detailStatPoints,
        out NRoleInventoryDelta delta,
        out List<int> statPoints,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        statPoints = new List<int>();
        reason = string.Empty;

        if (player == null || role == null)
        {
            reason = "玩家或角色数据为空";
            return false;
        }

        if (detailStatPoints == null || detailStatPoints.Count != StatPointDetailCount)
        {
            reason = $"加点数量非法，期望 {StatPointDetailCount} 项";
            return false;
        }

        if (role.RuntimeStatPoints == null)
        {
            reason = "角色属性点未初始化";
            return false;
        }

        if (role.RuntimeStatPoints.Count <= StatPointLevelIndex)
        {
            reason = "角色属性点结构非法";
            return false;
        }

        int currentLevel = role.RuntimeStatPoints[StatPointLevelIndex];
        if (currentLevel < InitialLevel)
        {
            reason = $"角色等级点非法: {currentLevel}";
            return false;
        }

        int addedPointCount = 0;
        for (int i = 0; i < StatPointDetailCount; i++)
        {
            int currentValue = role.RuntimeStatPoints[i];
            int targetValue = detailStatPoints[i];
            if (targetValue < currentValue)
            {
                reason = $"属性点不允许回退: index={i}, current={currentValue}, target={targetValue}";
                return false;
            }

            addedPointCount += targetValue - currentValue;
        }

        if (addedPointCount <= 0)
        {
            reason = "没有可应用的属性点变化";
            return false;
        }

        int targetLevel = currentLevel + addedPointCount;
        int totalCost = GetTotalLevelUpCost(currentLevel, targetLevel);
        if (totalCost <= 0)
        {
            reason = $"加点花费非法: {totalCost}";
            return false;
        }

        if (!_itemService.TryConsumeCurrencyFromRole(
                role,
                CurrencyCidForStatPointLevelUp,
                totalCost,
                out delta,
                out reason))
        {
            return false;
        }

        for (int i = 0; i < StatPointDetailCount; i++)
        {
            role.RuntimeStatPoints[i] = detailStatPoints[i];
        }

        role.RuntimeStatPoints[StatPointLevelIndex] = targetLevel;
        statPoints.AddRange(role.RuntimeStatPoints);

        reason = string.Empty;
        return true;
    }

    private static int GetNextLevelCost(int currentLevel)
    {
        int safeLevel = Math.Max(InitialLevel, currentLevel);
        return InitialLevelUpCost + (safeLevel - InitialLevel) * LevelUpCostStep;
    }

    private static int GetTotalLevelUpCost(int fromLevel, int toLevel)
    {
        int safeFromLevel = Math.Max(InitialLevel, fromLevel);
        int safeToLevel = Math.Max(safeFromLevel, toLevel);
        int totalCost = 0;
        for (int currentLevel = safeFromLevel; currentLevel < safeToLevel; currentLevel++)
        {
            totalCost += GetNextLevelCost(currentLevel);
        }

        return totalCost;
    }
}
