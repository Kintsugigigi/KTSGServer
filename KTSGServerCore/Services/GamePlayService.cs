using System.Collections.Generic;
using KTSG.Proto;
using KTSG.Server.Runtime;
using cfg;
using KTSG.Server.Model;

namespace KTSG.Server.Services;

public sealed class GamePlayService : IService
{
    public void Init()
    {
    }
    
    
    
    public void NotifyMonsterKill(RoleData role, int configId, int count = 1)
    {
        if (configId <= 0 || count <= 0)
        {
            return;
        }

        role.MonsterKills[configId] = role.MonsterKills.GetValueOrDefault(configId) + count;

        if (role.RuntimeEvents == null)
        {
            return;
        }
        
        role.RuntimeEvents.Publish(new OnMonsterKilledEvent(configId));
    }

    public void SetContractReady(RoleData role, int contractId, List<int> contacts)
    {
        if (contractId <= 0 || contacts == null)
        {
            return;
        }

        int score = CalculateContractScore(contacts);

        role.ContractReadies[contractId] = new ContractReady
        {
            Score = score,
            Contacts = contacts
        };
    }

    public void RecordContractSuccess(PlayerData player, RoleData role, int contractId)
    {
        if (contractId <= 0 || player == null || !role.ContractReadies.TryGetValue(contractId, out ContractReady? ready) || ready == null)
        {
            return;
        }

        var record = new ContractRecord
        {
            RoleId = role.RoleId,
            RoleCid = role.RoleCid,
            Score = ready.Score,
            Contacts = new List<int>(ready.Contacts),
            RecordTime = DateTime.UtcNow
        };

        if (!player.ContractRecords.TryGetValue(contractId, out List<ContractRecord>? records) || records == null)
        {
            records = new List<ContractRecord>();
            player.ContractRecords[contractId] = records;
        }

        records.Add(record);

        if (role.RuntimeEvents == null)
        {
            return;
        }

        role.RuntimeEvents.Publish(new OnContractSuccessEvent(contractId, record.Score, record.Contacts));
    }

    public bool TrySetInteractableBaseProgress(
        PlayerData player,
        RoleData role,
        int interactableId,
        int baseProgress,
        NRoleSignalChange? change,
        out string reason)
    {
        reason = string.Empty;
        if (player == null || role == null)
        {
            reason = "玩家或角色数据为空";
            return false;
        }

        return TrySetInteractableProgress(
            role,
            interactableId,
            baseProgress,
            -1,
            0,
            change,
            out reason);
    }

    public bool TrySetInteractableProgress(
        RoleData role,
        int interactableId,
        int progress,
        int priority,
        int sourceParentQuestId,
        NRoleSignalChange? change,
        out string reason)
    {
        reason = string.Empty;
        if (role == null || interactableId <= 0 || progress < 0)
        {
            reason = "交互物体进度参数非法";
            return false;
        }

        InteractableProgressData? data;
        if (priority < 0)
        {
            if (!role.InteractableProgresses.TryGetValue(interactableId, out data))
            {
                data = new InteractableProgressData();
                role.InteractableProgresses[interactableId] = data;
            }

            data.SetBaseProgress(progress);
        }
        else
        {
            if (sourceParentQuestId <= 0)
            {
                reason = $"交互物体覆盖缺少父任务来源: interactable={interactableId}, priority={priority}";
                return false;
            }

            if (!role.InteractableProgresses.TryGetValue(interactableId, out data))
            {
                data = new InteractableProgressData();
                role.InteractableProgresses[interactableId] = data;
            }

            data.SetOverride(sourceParentQuestId, progress, priority);
        }

        change?.AddOrUpdateInteractableDelta(interactableId, data);
        return true;
    }

    public void NotifyWayPointTriggered(RoleData role, int wayPointId)
    {
        if (role?.RuntimeEvents == null)
        {
            return;
        }

        role.RuntimeEvents.Publish(new OnWayPointTriggeredEvent(wayPointId));
    }
    

    public void NotifyDeath(RoleData role, int configId)
    {
        if (role == null || configId <= 0)
        {
            return;
        }

        role.DeathByMonsterCounts[configId] = role.DeathByMonsterCounts.GetValueOrDefault(configId) + 1;

        if (role.RuntimeEvents != null)
        {
            role.RuntimeEvents.Publish(new OnDeathByMonsterEvent(configId));
        }
    }

    private static int CalculateContractScore(List<int> contacts)
    {
        return contacts.Count * 5 + contacts.Sum();
    }
}
