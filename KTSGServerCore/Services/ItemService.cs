using cfg;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Model;
using KTSG.Server.Runtime;

namespace KTSG.Server.Services;

public sealed class ItemService : IService
{
    private ConfigService _configService = null!;
    private const int WeaponUpgradeMaxLevel = 5;
    private const int WeaponUpgradeMat3001 = 3001;
    private const int WeaponUpgradeMat3002 = 3002;
    private const int WeaponUpgradeMat3003 = 3003;
    private const int WeaponUpgradeMat3001Exp = 50;
    private const int WeaponUpgradeMat3002Exp = 100;
    private const int WeaponUpgradeMat3003Exp = 200;
    public void Init()
    {
        _configService = ServerRuntime.Instance.GetService<ConfigService>();
    }

    private static void PublishStackItemChanged(RoleData role, int configId, int deltaCount, bool isAdd)
    {
        if (role.RuntimeEvents == null)
        {
            return;
        }
        
        role.RuntimeEvents.Publish(new OnStackItemChangedEvent(configId, deltaCount, isAdd));
    }

    private static void PublishInstanceItemChanged(RoleData role, EItemType itemType, int configId, int level, long instanceId, bool isAdd)
    {
        if (role.RuntimeEvents == null)
        {
            return;
        }

        role.RuntimeEvents.Publish(new OnInstanceItemChangedEvent(itemType, configId, level, instanceId, isAdd));
    }

    public long AllocateInstanceId(PlayerData player)
    {
        if (player.NextItemInstanceId <= 0)
        {
            player.NextItemInstanceId = 1;
        }

        return player.NextItemInstanceId++;
    }

    public bool TryGrantDefaultRoleLoadout(PlayerData player, RoleData role, out string reason)
    {
        reason = string.Empty;

        if (!TryCreateWeaponItem(player, 1001, 1, 0f, out var weaponItem, out reason))
        {
            return false;
        }

        if (!TryAddInstanceItemToRoleBackpack(role, weaponItem, out _, out reason) ||
            !TryEquipToWeaponSlot(role, weaponItem, 0, out _, out reason))
        {
            return false;
        }

        if (!TryCreateEquipItem(player, 2001, out var headEquipItem, out reason))
        {
            return false;
        }

        if (!TryAddInstanceItemToRoleBackpack(role, headEquipItem, out _, out reason) ||
            !TryEquipToEquipSlot(role, headEquipItem, 0, out _, out reason))
        {
            return false;
        }

        if (!TryCreateEquipItem(player, 2002, out var bodyEquipItem, out reason))
        {
            return false;
        }

        if (!TryAddInstanceItemToRoleBackpack(role, bodyEquipItem, out _, out reason) ||
            !TryEquipToEquipSlot(role, bodyEquipItem, 1, out _, out reason))
        {
            return false;
        }

        return true;
    }

    public bool TryCreateWeaponItem(
        PlayerData player,
        int itemConfigId,
        int level,
        float exp,
        out InventoryItemData item,
        out string reason)
    {
        item = null!;
        reason = string.Empty;

        if (!TryGetItemConfig(itemConfigId, out var itemConfig, out reason))
        {
            return false;
        }

        if (itemConfig.ItemType != EItemType.Weapon)
        {
            reason = $"物品不是武器类型: {itemConfigId}";
            return false;
        }

        if (_configService.Tables.TbWeaponConfig.GetOrDefault(itemConfig.TypeID) == null)
        {
            reason = $"武器配置不存在: itemConfigId={itemConfigId}, weaponConfigId={itemConfig.TypeID}";
            return false;
        }

        item = new InventoryItemData
        {
            InstanceId = AllocateInstanceId(player),
            ConfigId = itemConfigId,
            Count = 1,
            ItemType = EItemType.Weapon,
            Weapon = new WeaponItemData
            {
                Level = level,
                Exp = exp
            }
        };
        return true;
    }

    public bool TryCreateEquipItem(
        PlayerData player,
        int itemConfigId,
        out InventoryItemData item,
        out string reason)
    {
        item = null!;
        reason = string.Empty;

        if (!TryGetItemConfig(itemConfigId, out var itemConfig, out reason))
        {
            return false;
        }

        if (!IsEquipItemType(itemConfig.ItemType))
        {
            reason = $"物品不是装备类型: {itemConfigId}";
            return false;
        }

        if (_configService.Tables.TbEquipablesConfig.GetOrDefault(itemConfig.TypeID) == null)
        {
            reason = $"装备配置不存在: itemConfigId={itemConfigId}, equipConfigId={itemConfig.TypeID}";
            return false;
        }

        item = new InventoryItemData
        {
            InstanceId = AllocateInstanceId(player),
            ConfigId = itemConfigId,
            Count = 1,
            ItemType = itemConfig.ItemType,
        };
        return true;
    }
    

    public bool TryAddStackItemToRoleBackpack(
        RoleData role,
        int configId,
        int count,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || configId <= 0 || count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        if (!TryGetItemConfig(configId, out var itemConfig, out reason))
        {
            return false;
        }

        if (itemConfig.ItemType == EItemType.Weapon || IsEquipItemType(itemConfig.ItemType))
        {
            reason = "该物品不是可堆叠物品";
            return false;
        }

        int maxStack = Math.Max(1, itemConfig.MaxStack);
        if (!role.RuntimeBackpack!.TryAddStackItem(configId, maxStack, count, out List<NInventorySlotDelta> slotDeltas, out reason))
        {
            return false;
        }

        for (int i = 0; i < slotDeltas.Count; i++)
        {
            delta.BackpackSlots.Add(slotDeltas[i]);
        }

        role.FillConsumableSlotDeltas(delta);
        PublishStackItemChanged(role, configId, count, true);
        return true;
    }

    public bool TryConsumeStackItemFromRoleBackpack(
        RoleData role,
        int configId,
        int count,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || configId <= 0 || count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        if (!role.RuntimeBackpack!.TryGetStackItemCount(configId, out int totalCount))
        {
            reason = "背包中不存在该物品";
            return false;
        }

        if (totalCount < count)
        {
            reason = "物品数量不足";
            return false;
        }

        if (!role.RuntimeBackpack.TryConsumeStackItem(configId, count, out List<NInventorySlotDelta> slotDeltas, out reason))
        {
            return false;
        }

        for (int i = 0; i < slotDeltas.Count; i++)
        {
            delta.BackpackSlots.Add(slotDeltas[i]);
        }

        if (!role.RuntimeBackpack.TryGetStackItemCount(configId, out int remainCount) || remainCount <= 0)
        {
            role.ClearConsumableConfigRefs(configId);
        }

        role.FillConsumableSlotDeltas(delta);
        PublishStackItemChanged(role, configId, count, false);
        return true;
    }

    public bool TryUpgradeWeapon(
        RoleData role,
        long instanceId,
        int mat3001Count,
        int mat3002Count,
        int mat3003Count,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || instanceId <= 0 || mat3001Count < 0 || mat3002Count < 0 || mat3003Count < 0)
        {
            reason = "参数非法";
            return false;
        }

        if (mat3001Count == 0 && mat3002Count == 0 && mat3003Count == 0)
        {
            reason = "未选择强化素材";
            return false;
        }

        if (role.RuntimeBackpack == null)
        {
            reason = "角色背包不存在";
            return false;
        }

        if (!role.RuntimeBackpack.TryGetInstanceSlot(EItemType.Weapon, instanceId, out int weaponBackpackSlot))
        {
            reason = $"武器不存在: {instanceId}";
            return false;
        }

        InventoryItemData? item = role.RuntimeBackpack.GetItemOrNull(weaponBackpackSlot);
        if (item == null || item.ItemType != EItemType.Weapon || item.Weapon == null)
        {
            reason = $"目标实例不是武器: {instanceId}";
            return false;
        }

        if (!TryGetItemConfig(item.ConfigId, out ItemConfig itemConfig, out reason))
        {
            return false;
        }

        WeaponConfig weaponConfig = _configService.Tables.TbWeaponConfig.GetOrDefault(itemConfig.TypeID);
        if (weaponConfig == null)
        {
            reason = $"武器配置不存在: itemConfigId={item.ConfigId}, weaponConfigId={itemConfig.TypeID}";
            return false;
        }

        int maxLevel = Math.Min(WeaponUpgradeMaxLevel, Math.Max(1, weaponConfig.MaxLevel));
        int currentLevel = item.Weapon.Level;
        float currentExp = Math.Max(0f, item.Weapon.Exp);
        if (currentLevel <= 0)
        {
            reason = $"武器等级非法: {currentLevel}";
            return false;
        }

        if (currentLevel >= maxLevel)
        {
            reason = "武器已满级";
            return false;
        }

        float gainExp = 0f;
        for (int i = 0; i < 3; i++)
        {
            int materialConfigId;
            int materialCount;
            int materialExpValue;
            switch (i)
            {
                case 0:
                    materialConfigId = WeaponUpgradeMat3001;
                    materialCount = mat3001Count;
                    materialExpValue = WeaponUpgradeMat3001Exp;
                    break;
                case 1:
                    materialConfigId = WeaponUpgradeMat3002;
                    materialCount = mat3002Count;
                    materialExpValue = WeaponUpgradeMat3002Exp;
                    break;
                default:
                    materialConfigId = WeaponUpgradeMat3003;
                    materialCount = mat3003Count;
                    materialExpValue = WeaponUpgradeMat3003Exp;
                    break;
            }

            if (materialCount <= 0)
            {
                continue;
            }

            if (!role.RuntimeBackpack.TryGetStackItemCount(materialConfigId, out int totalCount) || totalCount < materialCount)
            {
                reason = $"强化素材不足: {materialConfigId}";
                return false;
            }

            gainExp += materialCount * materialExpValue;
        }

        if (gainExp <= 0f)
        {
            reason = "强化经验非法";
            return false;
        }

        int previewLevel = currentLevel;
        float previewExp = currentExp;
        while (previewLevel < maxLevel)
        {
            int expIndex = previewLevel;
            if (expIndex < 0 || expIndex >= weaponConfig.UpdateExp.Count)
            {
                reason = $"武器升级配置非法: weaponConfigId={weaponConfig.ID}, level={previewLevel}";
                return false;
            }

            float needExp = weaponConfig.UpdateExp[expIndex];
            if (needExp <= 0f)
            {
                reason = $"武器升级经验配置非法: weaponConfigId={weaponConfig.ID}, level={previewLevel}";
                return false;
            }

            if (previewExp >= needExp)
            {
                previewExp -= needExp;
                previewLevel++;
                continue;
            }

            float remainExp = needExp - previewExp;
            if (gainExp >= remainExp)
            {
                gainExp -= remainExp;
                previewLevel++;
                previewExp = 0f;
                continue;
            }

            previewExp += gainExp;
            gainExp = 0f;
            break;
        }

        if (previewLevel >= maxLevel)
        {
            previewLevel = maxLevel;
            previewExp = 0f;
        }

        for (int i = 0; i < 3; i++)
        {
            int materialConfigId;
            int materialCount;
            switch (i)
            {
                case 0:
                    materialConfigId = WeaponUpgradeMat3001;
                    materialCount = mat3001Count;
                    break;
                case 1:
                    materialConfigId = WeaponUpgradeMat3002;
                    materialCount = mat3002Count;
                    break;
                default:
                    materialConfigId = WeaponUpgradeMat3003;
                    materialCount = mat3003Count;
                    break;
            }

            if (materialCount <= 0)
            {
                continue;
            }

            if (!TryConsumeStackItemFromRoleBackpack(role, materialConfigId, materialCount, out NRoleInventoryDelta materialDelta, out reason))
            {
                return false;
            }

            for (int j = 0; j < materialDelta.BackpackSlots.Count; j++)
            {
                delta.BackpackSlots.Add(materialDelta.BackpackSlots[j]);
            }
        }

        item.Weapon.Level = previewLevel;
        item.Weapon.Exp = previewExp;
        delta.BackpackSlots.Add(new NInventorySlotDelta
        {
            SlotIndex = weaponBackpackSlot,
            IsEmpty = false,
            Item = item.ToNItem()
        });

        role.RuntimeEvents?.Publish(new OnInstanceItemRuntimeChangedEvent(
            item.ItemType,
            item.ConfigId,
            item.Weapon.Level,
            item.InstanceId));
        return true;
    }

    public bool TryAddInstanceItemToRoleBackpack(
        RoleData role,
        InventoryItemData item,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || item == null || item.InstanceId <= 0 || item.Count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        if (item.ItemType == EItemType.Stack || item.ItemType == EItemType.Consumables)
        {
            reason = "不是实例物品";
            return false;
        }

        if (role.RuntimeBackpack == null)
        {
            reason = "角色背包不存在";
            return false;
        }

        if (item.ItemType == EItemType.None)
        {
            reason = "实例物品类型非法";
            return false;
        }

        if (!role.RuntimeBackpack.TryAddItemToFirstEmptySlot(item, out _, out NInventorySlotDelta slotDelta, out reason))
        {
            return false;
        }

        delta.BackpackSlots.Add(slotDelta);
        PublishInstanceItemChanged(role, item.ItemType, item.ConfigId, item.Weapon?.Level ?? 0, item.InstanceId, true);
        return true;
    }

    public bool TryAddInstanceItemByConfigToRole(
        PlayerData player,
        RoleData role,
        int configId,
        int level,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (player == null || role == null || configId <= 0 || level < 0)
        {
            reason = "实例物品变更参数非法";
            return false;
        }

        ItemConfig itemConfig = _configService.Tables.TbItemConfig.GetOrDefault(configId);
        if (itemConfig == null)
        {
            reason = $"物品配置不存在: {configId}";
            return false;
        }

        InventoryItemData item;
        if (itemConfig.ItemType == EItemType.Weapon)
        {
            if (!TryCreateWeaponItem(player, configId, level, 0f, out item, out reason))
            {
                return false;
            }
        }
        else if (IsEquipItemType(itemConfig.ItemType))
        {
            if (!TryCreateEquipItem(player, configId, out item, out reason))
            {
                return false;
            }
        }
        else
        {
            reason = $"该物品不是实例物品类型: {configId}, type={itemConfig.ItemType}";
            return false;
        }

        return TryAddInstanceItemToRoleBackpack(role, item, out delta, out reason);
    }

    public bool TryRemoveInstanceItemByIdFromRole(
        RoleData role,
        EItemType itemType,
        long instanceId,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || instanceId <= 0)
        {
            reason = "参数非法";
            return false;
        }

        if (role.RuntimeBackpack == null)
        {
            reason = "角色背包不存在";
            return false;
        }

        if (itemType == EItemType.Weapon &&
            role.RuntimeBackpack.TryRemoveInstanceItem(EItemType.Weapon, instanceId, out _, out InventoryItemData? removedWeapon, out NInventorySlotDelta slotDeltaWeapon, out reason))
        {
            if (removedWeapon == null)
            {
                reason = $"实例物品不存在: {instanceId}";
                return false;
            }

            int level = removedWeapon?.Weapon?.Level ?? 0;
            delta.BackpackSlots.Add(slotDeltaWeapon);
            role.ClearWeaponSlotRefs(instanceId, delta);
            PublishInstanceItemChanged(role, EItemType.Weapon, removedWeapon.ConfigId, level, instanceId, false);
            return true;
        }

        if (IsEquipItemType(itemType) &&
            role.RuntimeBackpack.TryRemoveInstanceItem(itemType, instanceId, out _, out InventoryItemData? removedEquip, out NInventorySlotDelta slotDeltaEquip, out reason))
        {
            if (removedEquip == null)
            {
                reason = $"实例物品不存在: {instanceId}";
                return false;
            }

            delta.BackpackSlots.Add(slotDeltaEquip);
            role.ClearEquipSlotRefs(instanceId, delta);
            PublishInstanceItemChanged(role, removedEquip.ItemType, removedEquip.ConfigId, 0, instanceId, false);
            return true;
        }

        reason = $"实例物品不存在: {instanceId}";
        return false;
    }

    public bool TrySwapBackpackSlot(
        RoleData role,
        int slotA,
        int slotB,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null)
        {
            reason = "角色数据为空";
            return false;
        }

        if (role == null || slotA < 0 || slotB < 0 || slotA == slotB ||
            role.RuntimeBackpack == null ||
            !role.RuntimeBackpack.IsValidSlot(slotA) ||
            !role.RuntimeBackpack.IsValidSlot(slotB))
        {
            reason = "槽位非法";
            return false;
        }

        InventoryItemData? itemA = role.RuntimeBackpack.GetItemOrNull(slotA);
        InventoryItemData? itemB = role.RuntimeBackpack.GetItemOrNull(slotB);

        if (IsEmptySlot(itemA) && IsEmptySlot(itemB))
        {
            reason = "两个槽位都为空";
            return false;
        }

        if (!IsEmptySlot(itemA) && !IsEmptySlot(itemB) &&
            itemA.InstanceId <= 0 && itemB.InstanceId <= 0 &&
            itemA.ConfigId == itemB.ConfigId)
        {
            reason = "相同配置的堆叠物品不需要交换";
            return false;
        }

        if (!role.RuntimeBackpack.TrySwapSlots(slotA, slotB, out List<NInventorySlotDelta> slotDeltas, out reason))
        {
            return false;
        }

        for (int i = 0; i < slotDeltas.Count; i++)
        {
            delta.BackpackSlots.Add(slotDeltas[i]);
        }
        return true;
    }

    public bool TryUseConsumable(
        RoleData role,
        int consumableSlotIndex,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || consumableSlotIndex < 0 || consumableSlotIndex >= role.RuntimeConsumableSlots!.Count)
        {
            reason = "消耗品槽位非法";
            return false;
        }

        int configId = role.RuntimeConsumableSlots[consumableSlotIndex];
        if (configId <= 0)
        {
            reason = "消耗品不存在";
            return false;
        }

        if (!TryGetItemConfig(configId, out var itemConfig, out reason))
        {
            return false;
        }

        if (itemConfig.ItemType != EItemType.Consumables)
        {
            reason = "该物品不是消耗品";
            return false;
        }

        if (role.RuntimeBackpack == null ||
            !role.RuntimeBackpack.TryGetStackItemCount(configId, out int totalCount) ||
            totalCount <= 0)
        {
            reason = "消耗品不存在";
            return false;
        }

        return TryConsumeStackItemFromRoleBackpack(role, configId, 1, out delta, out reason);
    }

    public bool TryBindConsumableToSlot(
        RoleData role,
        int configId,
        int slotIndex,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;
        NetLogger.Info($"[ItemService] TryBindConsumableToSlot start. Config={configId}, Slot={slotIndex}");

        if (role == null || role.RuntimeConsumableSlots == null)
        {
            reason = "角色消耗品槽不存在";
            NetLogger.Warning("[ItemService] TryBindConsumableToSlot failed: consumable slots missing.");
            return false;
        }

        if (slotIndex < 0 || slotIndex >= role.RuntimeConsumableSlots.Count)
        {
            reason = "消耗品槽位非法";
            NetLogger.Warning($"[ItemService] TryBindConsumableToSlot failed: invalid slot. Slot={slotIndex}, Count={role.RuntimeConsumableSlots.Count}");
            return false;
        }

        if (configId <= 0)
        {
            reason = "消耗品配置非法";
            NetLogger.Warning($"[ItemService] TryBindConsumableToSlot failed: invalid config. Config={configId}");
            return false;
        }

        if (!TryGetItemConfig(configId, out var itemConfig, out reason))
        {
            NetLogger.Warning($"[ItemService] TryBindConsumableToSlot failed: TryGetItemConfig. Config={configId}, Reason={reason}");
            return false;
        }

        if (itemConfig.ItemType != EItemType.Consumables)
        {
            reason = "该物品不是消耗品";
            NetLogger.Warning($"[ItemService] TryBindConsumableToSlot failed: item type mismatch. Config={configId}, Type={itemConfig.ItemType}");
            return false;
        }

        if (role.RuntimeBackpack == null ||
            !role.RuntimeBackpack.TryGetStackItemCount(configId, out int totalCount) ||
            totalCount <= 0)
        {
            reason = "背包中不存在该消耗品";
            NetLogger.Warning($"[ItemService] TryBindConsumableToSlot failed: stack count missing. Config={configId}");
            return false;
        }

        bool success = role.TryBindConsumableConfigToSlot(configId, slotIndex, out delta, out reason);
        if (!success)
        {
            NetLogger.Warning($"[ItemService] TryBindConsumableToSlot failed in role. Config={configId}, Slot={slotIndex}, Reason={reason}");
            return false;
        }

        NetLogger.Info(
            $"[ItemService] TryBindConsumableToSlot success. Config={configId}, Slot={slotIndex}, DeltaConsumable={delta?.ConsumableSlotDeltas?.Count ?? 0}");
        return true;
    }

    public bool TryAddCurrencyToRole(
        RoleData role,
        int currencyCid,
        int count,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || currencyCid < 0 || count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        var runtimeCurrencies = role.RuntimeCurrencies!;
        CurrencyItemData? currencyItem = null;
        for (int i = 0; i < runtimeCurrencies.Count; i++)
        {
            CurrencyItemData? current = runtimeCurrencies[i];
            if (current != null && current.Cid == currencyCid)
            {
                currencyItem = current;
                break;
            }
        }

        if (currencyItem == null)
        {
            currencyItem = new CurrencyItemData { Cid = currencyCid, Count = 0 };
            runtimeCurrencies.Add(currencyItem);
        }

        currencyItem.Count += count;
        AddCurrencyDelta(delta, currencyItem);
        return true;
    }

    public bool TryConsumeCurrencyFromRole(
        RoleData role,
        int currencyCid,
        int count,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || currencyCid < 0 || count <= 0)
        {
            reason = "参数非法";
            return false;
        }

        var runtimeCurrencies = role.RuntimeCurrencies!;
        CurrencyItemData? currencyItem = null;
        for (int i = 0; i < runtimeCurrencies.Count; i++)
        {
            CurrencyItemData? current = runtimeCurrencies[i];
            if (current != null && current.Cid == currencyCid)
            {
                currencyItem = current;
                break;
            }
        }

        if (currencyItem == null)
        {
            reason = "货币不存在";
            return false;
        }

        if (currencyItem.Count < count)
        {
            reason = "货币数量不足";
            return false;
        }

        currencyItem.Count -= count;
        AddCurrencyDelta(delta, currencyItem);
        return true;
    }

    public bool TryChangeCurrencyToRole(
        RoleData role,
        ChangeCurrencyItemSignal signal,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (role == null || signal == null)
        {
            reason = "货币变更信号参数为空";
            return false;
        }

        if (signal.IndexID < 0 || signal.Count <= 0)
        {
            reason = "货币变更参数非法";
            return false;
        }

        return signal.IsAdd
            ? TryAddCurrencyToRole(role, signal.IndexID, signal.Count, out delta, out reason)
            : TryConsumeCurrencyFromRole(role, signal.IndexID, signal.Count, out delta, out reason);
    }

    public bool TryGrantRewards(
        PlayerData player,
        RoleData role,
        ERewardSourceType sourceType,
        int sourceId,
        IReadOnlyList<Item>? itemRewards,
        IReadOnlyList<CurrencyItem>? currencyRewards,
        out NRoleInventoryDelta delta,
        out NGrantedReward reward,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reward = new NGrantedReward
        {
            SourceType = sourceType,
            SourceId = sourceId
        };
        reason = string.Empty;

        if (player == null || role == null)
        {
            reason = "奖励发放参数为空";
            return false;
        }

        int requiredInstanceSlots = 0;
        if (itemRewards != null)
        {
            for (int i = 0; i < itemRewards.Count; i++)
            {
                Item? itemReward = itemRewards[i];
                if (itemReward == null || itemReward.ConfigID <= 0)
                {
                    continue;
                }

                if (!TryGetItemConfig(itemReward.ConfigID, out ItemConfig itemConfig, out reason))
                {
                    return false;
                }

                if (itemReward is StackItem stackItem)
                {
                    if (stackItem.Count <= 0)
                    {
                        reason = $"堆叠奖励数量非法: {itemReward.ConfigID}";
                        return false;
                    }

                    int maxStack = Math.Max(1, itemConfig.MaxStack);
                    if (!role.RuntimeBackpack!.CanAcceptStackItem(itemReward.ConfigID, maxStack, stackItem.Count, out int requiredSlots, out reason))
                    {
                        return false;
                    }

                    requiredInstanceSlots += requiredSlots;
                    continue;
                }

                if (itemReward is InstanceItem instanceItem)
                {
                    if (instanceItem.Level < 0)
                    {
                        reason = $"实例奖励等级非法: {itemReward.ConfigID}";
                        return false;
                    }

                    requiredInstanceSlots++;
                    continue;
                }

                reason = $"暂不支持的奖励物品类型: {itemReward.GetType().Name}";
                return false;
            }
        }

        if (requiredInstanceSlots > 0 && !role.RuntimeBackpack!.CanAcceptInstanceItems(requiredInstanceSlots, out reason))
        {
            return false;
        }

        if (itemRewards != null)
        {
            for (int i = 0; i < itemRewards.Count; i++)
            {
                Item? itemReward = itemRewards[i];
                if (itemReward == null || itemReward.ConfigID <= 0)
                {
                    continue;
                }

                if (itemReward is StackItem stackItem)
                {
                    if (!TryAddStackItemToRoleBackpack(role, stackItem.ConfigID, stackItem.Count, out NRoleInventoryDelta rewardDelta, out reason))
                    {
                        return false;
                    }

                    delta.MergeFrom(rewardDelta);
                    reward.Entries.Add(new NGrantedRewardEntry
                    {
                        ConfigId = stackItem.ConfigID,
                        Count = stackItem.Count,
                        Level = 0,
                        IsCurrency = false,
                        CurrencyIndex = 0
                    });
                    continue;
                }

                if (itemReward is InstanceItem instanceItem)
                {
                    if (!TryAddInstanceItemByConfigToRole(player, role, instanceItem.ConfigID, instanceItem.Level, out NRoleInventoryDelta rewardDelta, out reason))
                    {
                        return false;
                    }

                    delta.MergeFrom(rewardDelta);
                    reward.Entries.Add(new NGrantedRewardEntry
                    {
                        ConfigId = instanceItem.ConfigID,
                        Count = 1,
                        Level = instanceItem.Level,
                        IsCurrency = false,
                        CurrencyIndex = 0
                    });
                    continue;
                }

                reason = $"暂不支持的奖励物品类型: {itemReward.GetType().Name}";
                return false;
            }
        }

        if (currencyRewards != null)
        {
            for (int i = 0; i < currencyRewards.Count; i++)
            {
                CurrencyItem? currencyReward = currencyRewards[i];
                if (currencyReward == null)
                {
                    continue;
                }

                if (currencyReward.Index < 0 || currencyReward.Count <= 0)
                {
                    reason = $"货币奖励参数非法: index={currencyReward.Index}, count={currencyReward.Count}";
                    return false;
                }

                if (!TryAddCurrencyToRole(role, currencyReward.Index, currencyReward.Count, out NRoleInventoryDelta rewardDelta, out reason))
                {
                    return false;
                }

                delta.MergeFrom(rewardDelta);
                reward.Entries.Add(new NGrantedRewardEntry
                {
                    ConfigId = 0,
                    Count = currencyReward.Count,
                    Level = 0,
                    IsCurrency = true,
                    CurrencyIndex = currencyReward.Index
                });
            }
        }

        return true;
    }

    public bool TryGrantEnemyDropRewards(
        PlayerData player,
        RoleData role,
        int sourceId,
        IReadOnlyList<RewardItem>? rewardItems,
        out NRoleInventoryDelta delta,
        out NGrantedReward reward,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reward = new NGrantedReward
        {
            SourceType = ERewardSourceType.RewardSourceEnemyDrop,
            SourceId = sourceId
        };
        reason = string.Empty;

        if (player == null || role == null)
        {
            reason = "敌人掉落奖励参数为空";
            return false;
        }

        if (rewardItems == null || rewardItems.Count == 0)
        {
            return true;
        }

        int requiredInstanceSlots = 0;
        for (int i = 0; i < rewardItems.Count; i++)
        {
            RewardItem? rewardItem = rewardItems[i];
            if (rewardItem == null || rewardItem.Count <= 0)
            {
                continue;
            }

            if (rewardItem.IsCurrency)
            {
                if (rewardItem.ConfigID < 0)
                {
                    continue;
                }
            }
            else if (rewardItem.ConfigID <= 0)
            {
                continue;
            }

            if (rewardItem.IsCurrency)
            {
                continue;
            }

            if (!TryGetItemConfig(rewardItem.ConfigID, out ItemConfig itemConfig, out reason))
            {
                return false;
            }

            switch (itemConfig.ItemType)
            {
                case EItemType.Stack:
                case EItemType.Consumables:
                    int maxStack = Math.Max(1, itemConfig.MaxStack);
                    if (!role.RuntimeBackpack!.CanAcceptStackItem(rewardItem.ConfigID, maxStack, rewardItem.Count, out int requiredSlots, out reason))
                    {
                        return false;
                    }

                    requiredInstanceSlots += requiredSlots;
                    break;
                case EItemType.Weapon:
                case EItemType.Helmet:
                case EItemType.Armor:
                case EItemType.Stone:
                    requiredInstanceSlots += rewardItem.Count;
                    break;
                default:
                    reason = $"敌人掉落暂不支持的物品类型: configId={rewardItem.ConfigID}, type={itemConfig.ItemType}";
                    return false;
            }
        }

        if (requiredInstanceSlots > 0 && !role.RuntimeBackpack!.CanAcceptInstanceItems(requiredInstanceSlots, out reason))
        {
            return false;
        }

        for (int i = 0; i < rewardItems.Count; i++)
        {
            RewardItem? rewardItem = rewardItems[i];
            if (rewardItem == null || rewardItem.Count <= 0)
            {
                continue;
            }

            if (rewardItem.IsCurrency)
            {
                if (rewardItem.ConfigID < 0)
                {
                    continue;
                }
            }
            else if (rewardItem.ConfigID <= 0)
            {
                continue;
            }

            if (rewardItem.IsCurrency)
            {
                if (!TryAddCurrencyToRole(role, rewardItem.ConfigID, rewardItem.Count, out NRoleInventoryDelta currencyDelta, out reason))
                {
                    return false;
                }

                delta.MergeFrom(currencyDelta);
                reward.Entries.Add(new NGrantedRewardEntry
                {
                    ConfigId = 0,
                    Count = rewardItem.Count,
                    Level = 0,
                    IsCurrency = true,
                    CurrencyIndex = rewardItem.ConfigID
                });
                continue;
            }

            if (!TryGetItemConfig(rewardItem.ConfigID, out ItemConfig itemConfig, out reason))
            {
                return false;
            }

            switch (itemConfig.ItemType)
            {
                case EItemType.Stack:
                case EItemType.Consumables:
                    if (!TryAddStackItemToRoleBackpack(role, rewardItem.ConfigID, rewardItem.Count, out NRoleInventoryDelta stackDelta, out reason))
                    {
                        return false;
                    }

                    delta.MergeFrom(stackDelta);
                    reward.Entries.Add(new NGrantedRewardEntry
                    {
                        ConfigId = rewardItem.ConfigID,
                        Count = rewardItem.Count,
                        Level = 0,
                        IsCurrency = false,
                        CurrencyIndex = 0
                    });
                    break;
                case EItemType.Weapon:
                    for (int j = 0; j < rewardItem.Count; j++)
                    {
                        if (!TryAddInstanceItemByConfigToRole(player, role, rewardItem.ConfigID, 1, out NRoleInventoryDelta weaponDelta, out reason))
                        {
                            return false;
                        }

                        delta.MergeFrom(weaponDelta);
                        reward.Entries.Add(new NGrantedRewardEntry
                        {
                            ConfigId = rewardItem.ConfigID,
                            Count = 1,
                            Level = 1,
                            IsCurrency = false,
                            CurrencyIndex = 0
                        });
                    }
                    break;
                case EItemType.Helmet:
                case EItemType.Armor:
                case EItemType.Stone:
                    for (int j = 0; j < rewardItem.Count; j++)
                    {
                        if (!TryAddInstanceItemByConfigToRole(player, role, rewardItem.ConfigID, 0, out NRoleInventoryDelta equipDelta, out reason))
                        {
                            return false;
                        }

                        delta.MergeFrom(equipDelta);
                        reward.Entries.Add(new NGrantedRewardEntry
                        {
                            ConfigId = rewardItem.ConfigID,
                            Count = 1,
                            Level = 0,
                            IsCurrency = false,
                            CurrencyIndex = 0
                        });
                    }
                    break;
                default:
                    reason = $"敌人掉落暂不支持的物品类型: configId={rewardItem.ConfigID}, type={itemConfig.ItemType}";
                    return false;
            }
        }

        return true;
    }

    public bool TryGetShopConfig(int shopId, out ShopConfig shopConfig, out string reason)
    {
        shopConfig = null!;
        reason = string.Empty;

        if (shopId <= 0)
        {
            reason = "商店ID非法";
            return false;
        }

        shopConfig = _configService.Tables.TbShopConfig.GetOrDefault(shopId);
        if (shopConfig == null)
        {
            reason = $"商店配置不存在: {shopId}";
            return false;
        }

        return true;
    }

    public bool TryShopTrade(
        PlayerData player,
        RoleData role,
        EShopTradeMode mode,
        int shopId,
        int itemConfigId,
        int currencyId,
        int unitPrice,
        int count,
        out NRoleInventoryDelta delta,
        out NGrantedReward reward,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reward = new NGrantedReward
        {
            SourceType = ERewardSourceType.RewardSourceShop,
            SourceId = shopId
        };
        reason = string.Empty;

        if (player == null || role == null)
        {
            reason = "商店交易参数为空";
            return false;
        }

        if (mode == EShopTradeMode.ShopTradeModeNone)
        {
            reason = "商店交易模式非法";
            return false;
        }

        if (shopId <= 0 || itemConfigId <= 0 || currencyId < 0 || unitPrice <= 0 || count <= 0)
        {
            reason = "商店交易参数非法";
            return false;
        }

        if (!TryGetShopConfig(shopId, out ShopConfig shopConfig, out reason))
        {
            return false;
        }

        if (!TryGetItemConfig(itemConfigId, out ItemConfig itemConfig, out reason))
        {
            return false;
        }

        long totalPriceLong = (long)unitPrice * count;
        if (totalPriceLong <= 0L || totalPriceLong > int.MaxValue)
        {
            reason = "总价非法";
            return false;
        }

        int totalPrice = (int)totalPriceLong;
        switch (mode)
        {
            case EShopTradeMode.ShopTradeModeBuy:
                return TryHandleShopBuyTrade(
                    player,
                    role,
                    shopConfig,
                    itemConfig,
                    shopId,
                    itemConfigId,
                    currencyId,
                    unitPrice,
                    count,
                    totalPrice,
                    delta,
                    reward,
                    out reason);
            case EShopTradeMode.ShopTradeModeSell:
                return TryHandleShopSellTrade(
                    role,
                    shopConfig,
                    itemConfig,
                    shopId,
                    itemConfigId,
                    currencyId,
                    unitPrice,
                    count,
                    totalPrice,
                    delta,
                    reward,
                    out reason);
            default:
                reason = $"暂不支持的商店交易模式: {mode}";
                return false;
        }
    }

    public bool TryBuyShopItem(
        PlayerData player,
        RoleData role,
        int shopId,
        int itemConfigId,
        int currencyId,
        int unitPrice,
        int count,
        out NRoleInventoryDelta delta,
        out NGrantedReward reward,
        out string reason)
    {
        return TryShopTrade(
            player,
            role,
            EShopTradeMode.ShopTradeModeBuy,
            shopId,
            itemConfigId,
            currencyId,
            unitPrice,
            count,
            out delta,
            out reward,
            out reason);
    }

    private bool TryHandleShopBuyTrade(
        PlayerData player,
        RoleData role,
        ShopConfig shopConfig,
        ItemConfig itemConfig,
        int shopId,
        int itemConfigId,
        int currencyId,
        int unitPrice,
        int count,
        int totalPrice,
        NRoleInventoryDelta delta,
        NGrantedReward reward,
        out string reason)
    {
        ShopItem? matchedShopItem = FindShopTradeItem(shopConfig?.BuyItems, itemConfigId, currencyId, unitPrice);
        if (matchedShopItem == null)
        {
            reason = $"商店购买条目不存在或价格不匹配: shopId={shopId}, itemConfigId={itemConfigId}, currencyId={currencyId}, unitPrice={unitPrice}";
            return false;
        }

        if (!TryGetCurrencyCount(role, currencyId, out int ownedCurrency))
        {
            reason = "货币不存在";
            return false;
        }

        if (ownedCurrency < totalPrice)
        {
            reason = "货币数量不足";
            return false;
        }

        if (!CanReceiveShopItem(role, itemConfig, count, out reason))
        {
            return false;
        }

        if (!TryConsumeCurrencyFromRole(role, currencyId, totalPrice, out NRoleInventoryDelta currencyDelta, out reason))
        {
            return false;
        }

        delta.MergeFrom(currencyDelta);

        switch (itemConfig.ItemType)
        {
            case EItemType.Stack:
            case EItemType.Consumables:
                if (!TryAddStackItemToRoleBackpack(role, itemConfigId, count, out NRoleInventoryDelta addStackDelta, out reason))
                {
                    return false;
                }

                delta.MergeFrom(addStackDelta);
                reward.Entries.Add(new NGrantedRewardEntry
                {
                    ConfigId = itemConfigId,
                    Count = count,
                    Level = 0,
                    IsCurrency = false,
                    CurrencyIndex = 0
                });
                return true;

            case EItemType.Weapon:
            case EItemType.Helmet:
            case EItemType.Armor:
            case EItemType.Stone:
                for (int i = 0; i < count; i++)
                {
                    if (!TryAddInstanceItemByConfigToRole(player, role, itemConfigId, 1, out NRoleInventoryDelta addInstanceDelta, out reason))
                    {
                        return false;
                    }

                    delta.MergeFrom(addInstanceDelta);
                    reward.Entries.Add(new NGrantedRewardEntry
                    {
                        ConfigId = itemConfigId,
                        Count = 1,
                        Level = 1,
                        IsCurrency = false,
                        CurrencyIndex = 0
                    });
                }

                return true;

            default:
                reason = $"商店暂不支持购买该物品类型: {itemConfig.ItemType}";
                return false;
        }
    }

    private bool TryHandleShopSellTrade(
        RoleData role,
        ShopConfig shopConfig,
        ItemConfig itemConfig,
        int shopId,
        int itemConfigId,
        int currencyId,
        int unitPrice,
        int count,
        int totalPrice,
        NRoleInventoryDelta delta,
        NGrantedReward reward,
        out string reason)
    {
        ShopItem? matchedShopItem = FindShopTradeItem(shopConfig?.SellItems, itemConfigId, currencyId, unitPrice);
        if (matchedShopItem == null)
        {
            reason = $"商店卖出条目不存在或价格不匹配: shopId={shopId}, itemConfigId={itemConfigId}, currencyId={currencyId}, unitPrice={unitPrice}";
            return false;
        }

        if (!CanSellShopItem(role, itemConfig, itemConfigId, count, out reason))
        {
            return false;
        }

        if (!TryConsumeStackItemFromRoleBackpack(role, itemConfigId, count, out NRoleInventoryDelta consumeDelta, out reason))
        {
            return false;
        }

        delta.MergeFrom(consumeDelta);

        if (!TryAddCurrencyToRole(role, currencyId, totalPrice, out NRoleInventoryDelta currencyDelta, out reason))
        {
            return false;
        }

        delta.MergeFrom(currencyDelta);
        reward.Entries.Add(new NGrantedRewardEntry
        {
            ConfigId = 0,
            Count = totalPrice,
            Level = 0,
            IsCurrency = true,
            CurrencyIndex = currencyId
        });
        return true;
    }

    public bool TryEquipToWeaponSlot(RoleData role, InventoryItemData item, int slotIndex, out string reason)
    {
        return TryEquipToWeaponSlot(role, item, slotIndex, out _, out reason);
    }

    public bool TryEquipToWeaponSlot(
        RoleData role,
        InventoryItemData item,
        int slotIndex,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (item.Weapon == null)
        {
            reason = "物品不是武器，无法放入武器槽";
            return false;
        }

        return role.TryAssignWeaponSlot(item.InstanceId, slotIndex, out delta, out reason);
    }

    public bool TryEquipToEquipSlot(RoleData role, InventoryItemData item, int slotIndex, out string reason)
    {
        return TryEquipToEquipSlot(role, item, slotIndex, out _, out reason);
    }

    public bool TryEquipToEquipSlot(
        RoleData role,
        InventoryItemData item,
        int slotIndex,
        out NRoleInventoryDelta delta,
        out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (item.ItemType != EItemType.Helmet && item.ItemType != EItemType.Armor && item.ItemType != EItemType.Stone)
        {
            reason = "物品不是装备，无法放入装备槽";
            return false;
        }

        if (!TryGetItemConfig(item.ConfigId, out var itemConfig, out reason))
        {
            return false;
        }

        if (_configService.Tables.TbEquipablesConfig.GetOrDefault(itemConfig.TypeID) == null)
        {
            reason = $"装备配置不存在: itemConfigId={item.ConfigId}, equipConfigId={itemConfig.TypeID}";
            return false;
        }

        if (itemConfig.ItemType != item.ItemType)
        {
            reason =
                $"装备实例类型与配置不一致: configId={item.ConfigId}, configType={itemConfig.ItemType}, itemType={item.ItemType}";
            return false;
        }

        return role.TryAssignEquipSlot(item.InstanceId, slotIndex, item.ItemType, out delta, out reason);
    }

    private bool TryGetItemConfig(int itemConfigId, out ItemConfig itemConfig, out string reason)
    {
        itemConfig = _configService.Tables.TbItemConfig.GetOrDefault(itemConfigId);
        if (itemConfig == null)
        {
            reason = $"物品配置不存在: {itemConfigId}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsEmptySlot(InventoryItemData? item)
    {
        return item == null || item.ConfigId <= 0 || item.Count <= 0;
    }

    private static void AddCurrencyDelta(NRoleInventoryDelta delta, CurrencyItemData currencyItem)
    {
        delta.CurrencyDeltas.Add(new NCurrencyDelta
        {
            CurrencyCid = currencyItem.Cid,
            Count = currencyItem.Count
        });
    }

    private static ShopItem? FindShopTradeItem(IList<ShopItem>? shopItems, int itemConfigId, int currencyId, int unitPrice)
    {
        if (shopItems == null)
        {
            return null;
        }

        for (int i = 0; i < shopItems.Count; i++)
        {
            ShopItem? item = shopItems[i];
            if (item == null)
            {
                continue;
            }

            if (item.ItemConfigID == itemConfigId &&
                item.CurrencyID == currencyId &&
                item.CurrencyCount == unitPrice)
            {
                return item;
            }
        }

        return null;
    }

    private static bool TryGetCurrencyCount(RoleData role, int currencyCid, out int count)
    {
        count = 0;
        if (role?.RuntimeCurrencies == null || currencyCid < 0)
        {
            return false;
        }

        for (int i = 0; i < role.RuntimeCurrencies.Count; i++)
        {
            CurrencyItemData? item = role.RuntimeCurrencies[i];
            if (item == null || item.Cid != currencyCid)
            {
                continue;
            }

            count = Math.Max(0, item.Count);
            return true;
        }

        return false;
    }

    private static bool TryGetStackItemCount(RoleData role, int configId, out int count)
    {
        count = 0;
        if (role?.RuntimeBackpack == null || configId <= 0)
        {
            return false;
        }

        return role.RuntimeBackpack.TryGetStackItemCount(configId, out count);
    }

    private static bool CanReceiveShopItem(RoleData role, ItemConfig itemConfig, int count, out string reason)
    {
        reason = string.Empty;
        if (role?.RuntimeBackpack == null || itemConfig == null || count <= 0)
        {
            reason = "背包检查参数非法";
            return false;
        }

        if (itemConfig.ItemType == EItemType.Stack || itemConfig.ItemType == EItemType.Consumables)
        {
            return role.RuntimeBackpack.CanAcceptStackItem(itemConfig.ID, Math.Max(1, itemConfig.MaxStack), count, out _, out reason);
        }

        if (itemConfig.ItemType == EItemType.Weapon || IsEquipItemType(itemConfig.ItemType))
        {
            return role.RuntimeBackpack.CanAcceptInstanceItems(count, out reason);
        }

        reason = $"商店暂不支持购买该物品类型: {itemConfig.ItemType}";
        return false;
    }

    private static bool CanSellShopItem(RoleData role, ItemConfig itemConfig, int configId, int count, out string reason)
    {
        reason = string.Empty;
        if (role?.RuntimeBackpack == null || itemConfig == null || configId <= 0 || count <= 0)
        {
            reason = "卖出检查参数非法";
            return false;
        }

        if (itemConfig.ItemType != EItemType.Stack && itemConfig.ItemType != EItemType.Consumables)
        {
            reason = $"商店卖出只支持堆叠物品: {itemConfig.ItemType}";
            return false;
        }

        if (!TryGetStackItemCount(role, configId, out int ownedCount) || ownedCount < count)
        {
            reason = "物品数量不足";
            return false;
        }

        return true;
    }

    private static bool IsEquipItemType(EItemType itemType)
    {
        return itemType is EItemType.Helmet or EItemType.Armor or EItemType.Stone;
    }

}
