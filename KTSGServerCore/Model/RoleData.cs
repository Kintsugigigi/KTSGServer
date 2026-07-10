using KTSG.Proto;
using KTSG.Network;
using KTSG.Server;
using KTSG.Server.Runtime;
using KTSG.Server.Services;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using cfg;

namespace KTSG.Server.Model;

[BsonIgnoreExtraElements]
public sealed class StackSlotRuntime
{
    public int ConfigId { get; set; }

    public int SlotIndex { get; set; }

    public int ConfigListIndex { get; set; }
}

[BsonIgnoreExtraElements]
public class RoleData
{
    public const int FixedWeaponSlotCount = 2;
    public const int FixedEquipSlotCount = 4;
    public const int FixedConsumableSlotCount = 5;
    public const int FixedBackpackItemCount = 80;
    private static readonly EItemType[] EquipSlotRules =
    {
        EItemType.Helmet,
        EItemType.Armor,
        EItemType.Stone,
        EItemType.Stone,
    };

    public string RoleId { get; set; } = string.Empty;

    public int RoleCid { get; set; } = 1001;
    
    public List<int> StatPoints { get; set; } = null!;

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, int> MonsterKills { get; set; } = null!;

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, int> DeathByMonsterCounts { get; set; } = null!;

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, InteractableProgressData> InteractableProgresses { get; set; } = null!;

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, ContractReady> ContractReadies { get; set; } = null!;

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, ParentQuestData> ParentQuests { get; set; } = null!;

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, int> ActiveWayPointRequestCounts { get; set; } = null!;

    [BsonElement("ActiveWayPointIds")]
    [BsonIgnoreIfNull]
    public HashSet<int>? LegacyActiveWayPointIds { get; set; }

    public List<CurrencyItemData> Currencies { get; set; } = null!;

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, InventoryItemData> Backpack { get; set; } = null!;

    public List<long> WeaponSlots { get; set; } = null!;

    public List<long> EquipSlots { get; set; } = null!;

    public List<int> ConsumableSlots { get; set; } = null!;

    public int SceneCid { get; set; } = 1003;

    public int RevivePointCid { get; set; } = 0;

    [BsonIgnore]
    public BindableList<int>? RuntimeStatPoints { get; private set; }

    [BsonIgnore]
    public BindableList<CurrencyItemData>? RuntimeCurrencies { get; private set; }

    [BsonIgnore]
    public BackpackRuntime? RuntimeBackpack { get; private set; }

    [BsonIgnore]
    public BindableList<long>? RuntimeWeaponSlots { get; private set; }

    [BsonIgnore]
    public BindableList<long>? RuntimeEquipSlots { get; private set; }

    [BsonIgnore]
    public BindableList<int>? RuntimeConsumableSlots { get; private set; }

    [BsonIgnore]
    public TypeEventBus? RuntimeEvents { get; private set; }

    [BsonIgnore]
    public PlayerData? RuntimePlayer { get; internal set; }

    [BsonIgnore]
    public IUnRegister? RuntimeQuestUnlockableUnregister { get; private set; }

    [BsonIgnore]
    public IUnRegister? RuntimeSubQuestAutoAdvanceUnregister { get; private set; }

    [BsonIgnore]
    public IUnRegister? RuntimeSubQuestProgressUnregister { get; private set; }

    [BsonIgnore]
    public Queue<int> PendingParentQuestIds { get; } = new();

    [BsonIgnore]
    public HashSet<int> PendingParentQuestIdSet { get; } = new();

    [BsonIgnore]
    public bool IsQuestDraining { get; set; }

    [BsonIgnore]
    public int CurrWeaponId
    {
        get
        {
            var weaponInstanceId = GetSlotValue(RuntimeWeaponSlots, 0);
            if (weaponInstanceId <= 0)
            {
                return 1000;
            }

            var backpack = RuntimeBackpack;
            if (backpack == null)
            {
                return 1000;
            }

            foreach (InventoryItemData item in backpack.OccupiedItems)
            {
                if (item.InstanceId == weaponInstanceId)
                {
                    return item.ConfigId;
                }
            }

            return 1000;
        }
    }

    public NRoleProfile ToNRoleProfile()
    {
        var profile = new NRoleProfile
        {
            RoleId = RoleId,
            RoleCid = RoleCid
        };

        var currencies = RuntimeCurrencies;
        if (currencies != null)
        {
            for (int i = 0; i < currencies.Count; i++)
            {
                CurrencyItemData? item = currencies[i];
                if (item == null)
                {
                    continue;
                }

                profile.Currencies.Add(item.ToNCurrencyItem());
            }
        }

        var backpack = RuntimeBackpack;
        if (backpack != null)
        {
            List<KeyValuePair<int, InventoryItemData>> backpackItems = new(backpack.ItemsBySlot);
            backpackItems.Sort((left, right) => left.Key.CompareTo(right.Key));
            for (int i = 0; i < backpackItems.Count; i++)
            {
                KeyValuePair<int, InventoryItemData> entry = backpackItems[i];
                InventoryItemData? item = entry.Value;
                if (item == null || item.ConfigId <= 0 || item.Count <= 0)
                {
                    continue;
                }

                profile.BackpackSlots.Add(new NInventorySlotSnapshot
                {
                    SlotIndex = entry.Key,
                    Item = item.ToNItem()
                });
            }
        }

        if (RuntimeStatPoints != null)
        {
            profile.StatPoints.AddRange(RuntimeStatPoints);
        }

        profile.WeaponSlots.AddRange(GetLongSlotView(RuntimeWeaponSlots, FixedWeaponSlotCount));
        profile.EquipSlots.AddRange(GetLongSlotView(RuntimeEquipSlots, FixedEquipSlotCount));
        profile.ConsumableSlots.AddRange(GetIntSlotView(RuntimeConsumableSlots, FixedConsumableSlotCount));
        foreach (var snapshot in BuildParentQuestSnapshots())
        {
            profile.ParentQuestSnapshots[snapshot.ParentQuestId] = snapshot;
        }

        foreach (var snapshot in BuildInteractableProgressSnapshots())
        {
            profile.InteractableProgresses[snapshot.InteractableId] = snapshot;
        }

        List<int> activeWayPointIds = GetActiveWayPointIds();
        if (activeWayPointIds.Count > 0)
        {
            profile.ActiveWaypointIds.Add(activeWayPointIds);
        }

        return profile;
    }

    public NRoleSummary ToNRoleSummary()
    {
        var summary = new NRoleSummary
        {
            RoleId = RoleId,
            RoleCid = RoleCid,
            WeaponId = ResolveSummaryWeaponId()
        };

        if (RuntimeStatPoints != null)
        {
            summary.StatPoints.AddRange(RuntimeStatPoints);
        }
        else if (StatPoints != null)
        {
            summary.StatPoints.AddRange(StatPoints);
        }

        return summary;
    }

    private int ResolveSummaryWeaponId()
    {
        if (RuntimeWeaponSlots != null && RuntimeBackpack != null)
        {
            return CurrWeaponId;
        }

        long weaponInstanceId = GetSlotValue(WeaponSlots, 0);
        if (weaponInstanceId <= 0 || Backpack == null || Backpack.Count <= 0)
        {
            return 1000;
        }

        foreach (InventoryItemData item in Backpack.Values)
        {
            if (item != null && item.InstanceId == weaponInstanceId)
            {
                return item.ConfigId > 0 ? item.ConfigId : 1000;
            }
        }

        return 1000;
    }

    public void EnsureRuntimeCollections()
    {
        RuntimeStatPoints ??= new BindableList<int>(StatPoints ?? new List<int>());
        RuntimeCurrencies ??= new BindableList<CurrencyItemData>(Currencies ?? new List<CurrencyItemData>());
        RuntimeBackpack ??= new BackpackRuntime(FixedBackpackItemCount);
        RuntimeWeaponSlots ??= new BindableList<long>(WeaponSlots ?? new List<long>());
        RuntimeEquipSlots ??= new BindableList<long>(EquipSlots ?? new List<long>());
        RuntimeConsumableSlots ??= new BindableList<int>(ConsumableSlots ?? new List<int>());
        RuntimeEvents ??= new TypeEventBus();
        NormalizeActiveWayPointRequests();
    }

    public void ReloadRuntimeCollectionsFromPersistent()
    {
        EnsureRuntimeCollections();

        RuntimeStatPoints!.Reset(StatPoints ?? new List<int>());
        RuntimeCurrencies!.Reset(Currencies ?? new List<CurrencyItemData>());
        RuntimeBackpack!.Reset(Backpack);
        RuntimeWeaponSlots!.Reset(WeaponSlots ?? new List<long>());
        RuntimeEquipSlots!.Reset(EquipSlots ?? new List<long>());
        RuntimeConsumableSlots!.Reset(ConsumableSlots ?? new List<int>());
        NormalizeActiveWayPointRequests();
    }

    public void InitQuestRuntime(PlayerData player)
    {
        RuntimePlayer = player;
        RuntimeQuestUnlockableUnregister?.UnRegister();
        RuntimeSubQuestAutoAdvanceUnregister?.UnRegister();
        RuntimeSubQuestProgressUnregister?.UnRegister();
        RuntimeQuestUnlockableUnregister = RuntimeEvents?.Subscribe<OnParentQuestUnlockableEvent>(OnParentQuestUnlockable);
        RuntimeSubQuestAutoAdvanceUnregister = RuntimeEvents?.Subscribe<OnSubQuestAutoAdvanceEvent>(OnSubQuestAutoAdvance);
        RuntimeSubQuestProgressUnregister = RuntimeEvents?.Subscribe<OnSubQuestProgressChangedEvent>(OnSubQuestProgressChanged);
    }

    public bool TryAddParentQuestData(int parentQuestId, EParentQuestState state = EParentQuestState.Waiting)
    {
        if (parentQuestId <= 0)
        {
            return false;
        }

        ParentQuests ??= new Dictionary<int, ParentQuestData>();
        if (ParentQuests.ContainsKey(parentQuestId))
        {
            return false;
        }

        ParentQuests[parentQuestId] = new ParentQuestData
        {
            State = state,
            CurrentSubQuestId = 0
        };
        return true;
    }

    public void RemoveParentQuestInteractableOverrides(
        int parentQuestId,
        IReadOnlyList<int>? affectedInteractableIds,
        NRoleSignalChange? change)
    {
        if (parentQuestId <= 0 || affectedInteractableIds == null || affectedInteractableIds.Count == 0)
        {
            return;
        }

        for (int i = 0; i < affectedInteractableIds.Count; i++)
        {
            int interactableId = affectedInteractableIds[i];
            if (interactableId <= 0 ||
                !InteractableProgresses.TryGetValue(interactableId, out InteractableProgressData? data) ||
                data == null)
            {
                continue;
            }

            if (!data.RemoveParentOverrides(parentQuestId))
            {
                continue;
            }

            change?.AddOrUpdateInteractableDelta(interactableId, data);
        }
    }

    public void ReleaseQuestRuntime()
    {
        RuntimeQuestUnlockableUnregister?.UnRegister();
        RuntimeQuestUnlockableUnregister = null;
        RuntimeSubQuestAutoAdvanceUnregister?.UnRegister();
        RuntimeSubQuestAutoAdvanceUnregister = null;
        RuntimeSubQuestProgressUnregister?.UnRegister();
        RuntimeSubQuestProgressUnregister = null;
        RuntimePlayer = null;
        IsQuestDraining = false;
        PendingParentQuestIds.Clear();
        PendingParentQuestIdSet.Clear();
    }

    private void OnParentQuestUnlockable(OnParentQuestUnlockableEvent evt)
    {
        if (evt.ParentQuestId <= 0 ||
            RuntimePlayer == null)
        {
            return;
        }

        EnqueueQuestPending(evt.ParentQuestId);
    }

    private void OnSubQuestAutoAdvance(OnSubQuestAutoAdvanceEvent evt)
    {
        if (evt.ParentQuestId <= 0 ||
            RuntimePlayer == null)
        {
            return;
        }

        EnqueueQuestPending(evt.ParentQuestId);
    }

    private void OnSubQuestProgressChanged(OnSubQuestProgressChangedEvent evt)
    {
        if (RuntimePlayer == null ||
            evt.SubQuestId <= 0 ||
            evt.CondIndex < 0 ||
            evt.Count < 0)
        {
            return;
        }

        var delta = new NSubQuestProgressDelta
        {
            SubQuestId = evt.SubQuestId,
            SlotIndex = evt.CondIndex,
            Count = evt.Count
        };

        NetLogger.Info(
            $"[QuestDebug] PushSubQuestProgressDelta. RoleId={RoleId}, ParentQuestStateCount={ParentQuests.Count}, " +
            $"SubQuestId={evt.SubQuestId}, SlotIndex={evt.CondIndex}, Count={evt.Count}");

        PlayerService playerService = ServerRuntime.Instance.GetService<PlayerService>();
        if (!playerService.TryPushMessage(RuntimePlayer, delta, out string reason))
        {
            NetLogger.Warning($"[RoleData] Push sub quest progress delta fail. RoleId={RoleId}, SubQuestId={evt.SubQuestId}, SlotIndex={evt.CondIndex}, Reason={reason}");
        }
    }

    public bool EnqueueQuestPending(int parentQuestId, NRoleSignalChange? change = null)
    {
        if (parentQuestId <= 0)
        {
            return false;
        }

        if (!PendingParentQuestIdSet.Add(parentQuestId))
        {
            NetLogger.Info(
                $"[QuestDebug] EnqueueQuestPending skipped duplicate. RoleId={RoleId}, ParentQuestId={parentQuestId}, " +
                $"IsQuestDraining={IsQuestDraining}, PendingCount={PendingParentQuestIds.Count}");
            return false;
        }

        PendingParentQuestIds.Enqueue(parentQuestId);
        NetLogger.Info(
            $"[QuestDebug] EnqueueQuestPending queued. RoleId={RoleId}, ParentQuestId={parentQuestId}, " +
            $"IsQuestDraining={IsQuestDraining}, PendingCount={PendingParentQuestIds.Count}, " +
            $"HasInlineChange={change != null}");

        if (!IsQuestDraining && RuntimePlayer != null)
        {
            QuestService questService = ServerRuntime.Instance.GetService<QuestService>();
            NetLogger.Info(
                $"[QuestDebug] EnqueueQuestPending trigger drain immediately. RoleId={RoleId}, ParentQuestId={parentQuestId}, " +
                $"PendingCount={PendingParentQuestIds.Count}");
            questService.DrainPendingQuestQueue(RuntimePlayer, this, change, out _);
        }

        return true;
    }

    public void FlushRuntimeCollections()
    {
        if (RuntimeStatPoints != null)
        {
            StatPoints = new List<int>(RuntimeStatPoints);
        }

        if (RuntimeCurrencies != null)
        {
            Currencies = new List<CurrencyItemData>(RuntimeCurrencies);
        }

        if (RuntimeBackpack != null)
        {
            Backpack = RuntimeBackpack.ExportPersistentItems();
        }

        if (RuntimeWeaponSlots != null)
        {
            WeaponSlots = new List<long>(RuntimeWeaponSlots);
        }

        if (RuntimeEquipSlots != null)
        {
            EquipSlots = new List<long>(RuntimeEquipSlots);
        }

        if (RuntimeConsumableSlots != null)
        {
            ConsumableSlots = new List<int>(RuntimeConsumableSlots);
        }

        NormalizeActiveWayPointRequests();
    }

    public List<int> GetActiveWayPointIds()
    {
        NormalizeActiveWayPointRequests();

        List<int> result = new(ActiveWayPointRequestCounts.Count);
        foreach (var kvp in ActiveWayPointRequestCounts)
        {
            if (kvp.Key > 0 && kvp.Value > 0)
            {
                result.Add(kvp.Key);
            }
        }

        result.Sort();
        return result;
    }

    public bool TryApplyActiveWayPointRequest(int wayPointId, bool isActive, out bool activeStateChanged, out string reason)
    {
        activeStateChanged = false;
        reason = string.Empty;

        if (wayPointId <= 0)
        {
            reason = "路点激活参数非法";
            return false;
        }

        NormalizeActiveWayPointRequests();

        if (isActive)
        {
            ActiveWayPointRequestCounts.TryGetValue(wayPointId, out int requestCount);
            if (requestCount == int.MaxValue)
            {
                reason = "路点激活申请次数超过上限";
                return false;
            }

            ActiveWayPointRequestCounts[wayPointId] = requestCount + 1;
            activeStateChanged = requestCount <= 0;
            return true;
        }

        if (!ActiveWayPointRequestCounts.TryGetValue(wayPointId, out int currentCount) || currentCount <= 0)
        {
            return true;
        }

        if (currentCount <= 1)
        {
            ActiveWayPointRequestCounts.Remove(wayPointId);
            activeStateChanged = true;
        }
        else
        {
            ActiveWayPointRequestCounts[wayPointId] = currentCount - 1;
        }

        return true;
    }

    private void NormalizeActiveWayPointRequests()
    {
        ActiveWayPointRequestCounts ??= new Dictionary<int, int>();

        if (LegacyActiveWayPointIds != null)
        {
            foreach (int wayPointId in LegacyActiveWayPointIds)
            {
                if (wayPointId > 0 && !ActiveWayPointRequestCounts.ContainsKey(wayPointId))
                {
                    ActiveWayPointRequestCounts[wayPointId] = 1;
                }
            }

            LegacyActiveWayPointIds = null;
        }

        List<int>? invalidWayPointIds = null;
        foreach (var kvp in ActiveWayPointRequestCounts)
        {
            if (kvp.Key <= 0 || kvp.Value <= 0)
            {
                invalidWayPointIds ??= new List<int>();
                invalidWayPointIds.Add(kvp.Key);
            }
        }

        if (invalidWayPointIds == null)
        {
            return;
        }

        for (int i = 0; i < invalidWayPointIds.Count; i++)
        {
            ActiveWayPointRequestCounts.Remove(invalidWayPointIds[i]);
        }
    }

    public void FillConsumableSlotDeltas(NRoleInventoryDelta delta)
    {
        if (delta == null || RuntimeConsumableSlots == null)
        {
            return;
        }

        delta.ConsumableSlotDeltas.Clear();
        for (int i = 0; i < RuntimeConsumableSlots.Count; i++)
        {
            delta.ConsumableSlotDeltas.Add(new NConsumableSlotDelta
            {
                SlotIndex = i,
                ConfigId = RuntimeConsumableSlots[i]
            });
        }
    }

    public void ClearConsumableConfigRefs(int configId)
    {
        if (RuntimeConsumableSlots == null || configId <= 0)
        {
            return;
        }

        for (int i = 0; i < RuntimeConsumableSlots.Count; i++)
        {
            if (RuntimeConsumableSlots[i] == configId)
            {
                RuntimeConsumableSlots[i] = -1;
            }
        }
    }

    public void ClearWeaponSlotRefs(long instanceId, NRoleInventoryDelta delta)
    {
        if (RuntimeWeaponSlots == null || instanceId <= 0 || delta == null)
        {
            return;
        }

        for (int i = 0; i < RuntimeWeaponSlots.Count; i++)
        {
            if (RuntimeWeaponSlots[i] != instanceId)
            {
                continue;
            }

            RuntimeWeaponSlots[i] = -1;
            delta.WeaponSlotDeltas.Add(new NWeaponSlotDelta
            {
                SlotIndex = i,
                InstanceId = -1
            });
        }
    }

    public void ClearEquipSlotRefs(long instanceId, NRoleInventoryDelta delta)
    {
        if (RuntimeEquipSlots == null || instanceId <= 0 || delta == null)
        {
            return;
        }

        for (int i = 0; i < RuntimeEquipSlots.Count; i++)
        {
            if (RuntimeEquipSlots[i] != instanceId)
            {
                continue;
            }

            RuntimeEquipSlots[i] = -1;
            delta.EquipSlotDeltas.Add(new NEquipSlotDelta
            {
                SlotIndex = i,
                InstanceId = -1
            });
        }
    }

    public bool TryClearWeaponSlot(int slotIndex, out NRoleInventoryDelta delta, out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (RuntimeWeaponSlots == null)
        {
            reason = "武器槽不存在";
            return false;
        }

        if (slotIndex < 0 || slotIndex >= RuntimeWeaponSlots.Count)
        {
            reason = "武器槽位非法";
            return false;
        }

        RuntimeWeaponSlots[slotIndex] = -1;
        delta.WeaponSlotDeltas.Add(new NWeaponSlotDelta
        {
            SlotIndex = slotIndex,
            InstanceId = -1
        });
        return true;
    }

    public bool TryClearEquipSlot(int slotIndex, out NRoleInventoryDelta delta, out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (RuntimeEquipSlots == null)
        {
            reason = "装备槽不存在";
            return false;
        }

        if (slotIndex < 0 || slotIndex >= RuntimeEquipSlots.Count)
        {
            reason = "装备槽位非法";
            return false;
        }

        RuntimeEquipSlots[slotIndex] = -1;
        delta.EquipSlotDeltas.Add(new NEquipSlotDelta
        {
            SlotIndex = slotIndex,
            InstanceId = -1
        });
        return true;
    }

    public bool TryClearConsumableSlot(int slotIndex, out NRoleInventoryDelta delta, out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;

        if (RuntimeConsumableSlots == null)
        {
            reason = "角色消耗品槽不存在";
            return false;
        }

        if (slotIndex < 0 || slotIndex >= RuntimeConsumableSlots.Count)
        {
            reason = "消耗品槽位非法";
            return false;
        }

        RuntimeConsumableSlots[slotIndex] = -1;
        FillConsumableSlotDeltas(delta);
        return true;
    }

    public bool TryBindConsumableConfigToSlot(int configId, int slotIndex, out NRoleInventoryDelta delta, out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;
        NetLogger.Info($"[RoleData] TryBindConsumableConfigToSlot start. Config={configId}, Slot={slotIndex}");

        if (RuntimeConsumableSlots == null)
        {
            reason = "角色消耗品槽不存在";
            NetLogger.Warning("[RoleData] TryBindConsumableConfigToSlot failed: RuntimeConsumableSlots is null.");
            return false;
        }

        if (configId <= 0)
        {
            reason = "消耗品配置非法";
            NetLogger.Warning($"[RoleData] TryBindConsumableConfigToSlot failed: invalid config. Config={configId}");
            return false;
        }

        if (slotIndex < 0 || slotIndex >= RuntimeConsumableSlots.Count)
        {
            reason = "消耗品槽位非法";
            NetLogger.Warning($"[RoleData] TryBindConsumableConfigToSlot failed: invalid slot. Slot={slotIndex}, Count={RuntimeConsumableSlots.Count}");
            return false;
        }

        int targetConfigId = RuntimeConsumableSlots[slotIndex];
        if (targetConfigId == configId)
        {
            for (int i = 0; i < RuntimeConsumableSlots.Count; i++)
            {
                if (i != slotIndex && RuntimeConsumableSlots[i] == configId)
                {
                    RuntimeConsumableSlots[i] = -1;
                }
            }

            FillConsumableSlotDeltas(delta);
            NetLogger.Info(
                $"[RoleData] TryBindConsumableConfigToSlot success (same target). Config={configId}, Slot={slotIndex}");
            return true;
        }

        int sourceSlotIndex = -1;
        for (int i = 0; i < RuntimeConsumableSlots.Count; i++)
        {
            if (i == slotIndex || RuntimeConsumableSlots[i] != configId)
            {
                continue;
            }

            if (sourceSlotIndex < 0)
            {
                sourceSlotIndex = i;
            }
            else
            {
                RuntimeConsumableSlots[i] = -1;
            }
        }

        if (sourceSlotIndex >= 0)
        {
            RuntimeConsumableSlots[sourceSlotIndex] = targetConfigId > 0 ? targetConfigId : -1;
        }

        RuntimeConsumableSlots[slotIndex] = configId;
        FillConsumableSlotDeltas(delta);
        NetLogger.Info(
            $"[RoleData] TryBindConsumableConfigToSlot success. Config={configId}, Slot={slotIndex}, TargetPrev={targetConfigId}, SourceSlot={sourceSlotIndex}");
        return true;
    }

    public bool TryAssignWeaponSlot(long instanceId, int slotIndex, out NRoleInventoryDelta delta, out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;
        NetLogger.Info($"[RoleData] TryAssignWeaponSlot start. Inst={instanceId}, Slot={slotIndex}");

        if (RuntimeWeaponSlots == null)
        {
            reason = "武器槽不存在";
            NetLogger.Warning("[RoleData] TryAssignWeaponSlot failed: RuntimeWeaponSlots is null.");
            return false;
        }

        if (instanceId <= 0 || slotIndex < 0 || slotIndex >= RuntimeWeaponSlots.Count)
        {
            reason = "武器槽位非法";
            NetLogger.Warning($"[RoleData] TryAssignWeaponSlot failed: invalid args. Inst={instanceId}, Slot={slotIndex}, Count={RuntimeWeaponSlots.Count}");
            return false;
        }

        long targetInstanceId = RuntimeWeaponSlots[slotIndex];
        if (targetInstanceId == instanceId)
        {
            for (int i = 0; i < RuntimeWeaponSlots.Count; i++)
            {
                if (i != slotIndex && RuntimeWeaponSlots[i] == instanceId)
                {
                    RuntimeWeaponSlots[i] = -1;
                    delta.WeaponSlotDeltas.Add(new NWeaponSlotDelta
                    {
                        SlotIndex = i,
                        InstanceId = -1
                    });
                }
            }

            delta.WeaponSlotDeltas.Add(new NWeaponSlotDelta
            {
                SlotIndex = slotIndex,
                InstanceId = instanceId
            });
            return true;
        }

        int sourceSlotIndex = -1;
        for (int i = 0; i < RuntimeWeaponSlots.Count; i++)
        {
            if (i == slotIndex || RuntimeWeaponSlots[i] != instanceId)
            {
                continue;
            }

            if (sourceSlotIndex < 0)
            {
                sourceSlotIndex = i;
            }
            else
            {
                RuntimeWeaponSlots[i] = -1;
                delta.WeaponSlotDeltas.Add(new NWeaponSlotDelta
                {
                    SlotIndex = i,
                    InstanceId = -1
                });
            }
        }

        if (sourceSlotIndex >= 0)
        {
            RuntimeWeaponSlots[sourceSlotIndex] = targetInstanceId > 0 ? targetInstanceId : -1;
            delta.WeaponSlotDeltas.Add(new NWeaponSlotDelta
            {
                SlotIndex = sourceSlotIndex,
                InstanceId = RuntimeWeaponSlots[sourceSlotIndex]
            });
        }

        RuntimeWeaponSlots[slotIndex] = instanceId;
        delta.WeaponSlotDeltas.Add(new NWeaponSlotDelta
        {
            SlotIndex = slotIndex,
            InstanceId = instanceId
        });
        NetLogger.Info(
            $"[RoleData] TryAssignWeaponSlot success. Inst={instanceId}, Slot={slotIndex}, TargetPrev={targetInstanceId}, SourceSlot={sourceSlotIndex}");
        return true;
    }

    public bool TryAssignEquipSlot(long instanceId, int slotIndex, EItemType itemType, out NRoleInventoryDelta delta, out string reason)
    {
        delta = new NRoleInventoryDelta();
        reason = string.Empty;
        NetLogger.Info($"[RoleData] TryAssignEquipSlot start. Inst={instanceId}, Slot={slotIndex}, ItemType={itemType}");

        if (RuntimeEquipSlots == null)
        {
            reason = "装备槽不存在";
            NetLogger.Warning("[RoleData] TryAssignEquipSlot failed: RuntimeEquipSlots is null.");
            return false;
        }

        if (instanceId <= 0 || slotIndex < 0 || slotIndex >= RuntimeEquipSlots.Count)
        {
            reason = "装备槽位非法";
            NetLogger.Warning($"[RoleData] TryAssignEquipSlot failed: invalid args. Inst={instanceId}, Slot={slotIndex}, Count={RuntimeEquipSlots.Count}");
            return false;
        }

        EItemType expectedSlotType = GetEquipSlotType(slotIndex);
        if (expectedSlotType == EItemType.None || itemType != expectedSlotType)
        {
            reason = $"装备类型与槽位不匹配: slot={slotIndex}, expected={expectedSlotType}, actual={itemType}";
            NetLogger.Warning($"[RoleData] TryAssignEquipSlot failed: type mismatch. Slot={slotIndex}, Expected={expectedSlotType}, Actual={itemType}");
            return false;
        }

        long targetInstanceId = RuntimeEquipSlots[slotIndex];
        if (targetInstanceId == instanceId)
        {
            for (int i = 0; i < RuntimeEquipSlots.Count; i++)
            {
                if (i != slotIndex && RuntimeEquipSlots[i] == instanceId)
                {
                    RuntimeEquipSlots[i] = -1;
                    delta.EquipSlotDeltas.Add(new NEquipSlotDelta
                    {
                        SlotIndex = i,
                        InstanceId = -1
                    });
                }
            }

            delta.EquipSlotDeltas.Add(new NEquipSlotDelta
            {
                SlotIndex = slotIndex,
                InstanceId = instanceId
            });
            return true;
        }

        int sourceSlotIndex = -1;
        for (int i = 0; i < RuntimeEquipSlots.Count; i++)
        {
            if (i == slotIndex || RuntimeEquipSlots[i] != instanceId)
            {
                continue;
            }

            if (sourceSlotIndex < 0)
            {
                sourceSlotIndex = i;
            }
            else
            {
                RuntimeEquipSlots[i] = -1;
                delta.EquipSlotDeltas.Add(new NEquipSlotDelta
                {
                    SlotIndex = i,
                    InstanceId = -1
                });
            }
        }

        if (sourceSlotIndex >= 0)
        {
            RuntimeEquipSlots[sourceSlotIndex] = targetInstanceId > 0 ? targetInstanceId : -1;
            delta.EquipSlotDeltas.Add(new NEquipSlotDelta
            {
                SlotIndex = sourceSlotIndex,
                InstanceId = RuntimeEquipSlots[sourceSlotIndex]
            });
        }

        RuntimeEquipSlots[slotIndex] = instanceId;
        delta.EquipSlotDeltas.Add(new NEquipSlotDelta
        {
            SlotIndex = slotIndex,
            InstanceId = instanceId
        });
        NetLogger.Info(
            $"[RoleData] TryAssignEquipSlot success. Inst={instanceId}, Slot={slotIndex}, ItemType={itemType}, TargetPrev={targetInstanceId}, SourceSlot={sourceSlotIndex}");
        return true;
    }

    private List<NParentQuestSnapshot> BuildParentQuestSnapshots()
    {
        List<NParentQuestSnapshot> result = new(ParentQuests.Count);
        foreach (var kvp in ParentQuests)
        {
            ParentQuestData? data = kvp.Value;
            if (data == null)
            {
                continue;
            }

            var snapshot = new NParentQuestSnapshot
            {
                ParentQuestId = kvp.Key,
                ParentState = (int)data.State,
                CurrentSubQuestId = data.CurrentSubQuestId
            };

            if (data.CurrentSubQuestConds.Count > 0)
            {
                for (int i = 0; i < data.CurrentSubQuestConds.Count; i++)
                {
                    SubQuestCondData? cond = data.CurrentSubQuestConds[i];
                    if (cond == null)
                    {
                        continue;
                    }

                    snapshot.CurrentProgress.Add(new NQuestCondProgress
                    {
                        SlotIndex = i,
                        Count = cond.Count
                    });
                }
            }

            result.Add(snapshot);
        }

        return result;
    }

    private List<NInteractableProgressSnapshot> BuildInteractableProgressSnapshots()
    {
        List<NInteractableProgressSnapshot> result = new(InteractableProgresses.Count);
        foreach (var kvp in InteractableProgresses)
        {
            InteractableProgressData? data = kvp.Value;
            if (data == null)
            {
                continue;
            }

            result.Add(data.ToDelta(kvp.Key));
        }

        return result;
    }

    private static List<long> GetLongSlotView(IReadOnlyList<long>? slots, int expectedCount)
    {
        List<long> result = new(expectedCount);
        for (var i = 0; i < expectedCount; i++)
        {
            result.Add(GetSlotValue(slots, i));
        }

        return result;
    }

    private static List<int> GetIntSlotView(IReadOnlyList<int>? slots, int expectedCount)
    {
        List<int> result = new(expectedCount);
        for (var i = 0; i < expectedCount; i++)
        {
            result.Add(GetSlotValue(slots, i));
        }

        return result;
    }

    private static long GetSlotValue(IReadOnlyList<long>? slots, int index)
    {
        if (slots == null || index < 0 || index >= slots.Count)
        {
            return -1;
        }

        return slots[index];
    }

    private static int GetSlotValue(IReadOnlyList<int>? slots, int index)
    {
        if (slots == null || index < 0 || index >= slots.Count)
        {
            return -1;
        }

        return slots[index];
    }

    private static EItemType GetEquipSlotType(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= EquipSlotRules.Length)
        {
            return EItemType.None;
        }

        return EquipSlotRules[slotIndex];
    }
}
