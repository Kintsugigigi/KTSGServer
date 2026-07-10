using System;
using System.Collections.Generic;
using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Model;
using KTSG.Server.Runtime;
using MongoDB.Driver;

namespace KTSG.Server.Services;

public sealed class PlayerService : IService
{
    private DbService _db = null!;
    private QuestService _questService = null!;

    private readonly Dictionary<int, string> _pendingUidByConnectionId = new();
    private readonly Dictionary<string, int> _pendingConnectionIdByUid = new();
    private readonly Dictionary<int, string> _onlineUidByConnectionId = new();
    private readonly Dictionary<string, PlayerData> _onlinePlayerByUid = new();
    private readonly Dictionary<string, int> _onlineConnectionIdByUid = new();
    private readonly Dictionary<int, INetConnection> _connectionByConnectionId = new();

    public void Init()
    {
        _db = ServerRuntime.Instance.GetService<DbService>();
        _questService = ServerRuntime.Instance.GetService<QuestService>();
    }

    public void OnClientConnected(INetConnection conn)
    {
        if (conn == null)
        {
            return;
        }

        _connectionByConnectionId[conn.ConnectionId] = conn;
    }

    public bool TryBeginPendingLogin(int connectionId, string uid, out string reason)
    {
        reason = string.Empty;

        if (_onlineUidByConnectionId.ContainsKey(connectionId))
        {
            reason = "当前连接已登录";
            return false;
        }

        if (_onlinePlayerByUid.ContainsKey(uid))
        {
            reason = "用户已登录";
            return false;
        }

        if (_pendingConnectionIdByUid.TryGetValue(uid, out var pendingConnectionId) &&
            pendingConnectionId != connectionId)
        {
            reason = "用户正在登录中";
            return false;
        }

        if (_pendingUidByConnectionId.TryGetValue(connectionId, out var oldPendingUid))
        {
            if (oldPendingUid == uid)
            {
                return true;
            }

            _pendingConnectionIdByUid.Remove(oldPendingUid);
            _pendingUidByConnectionId.Remove(connectionId);
        }

        _pendingUidByConnectionId[connectionId] = uid;
        _pendingConnectionIdByUid[uid] = connectionId;
        return true;
    }

    public bool TryCompletePendingLogin(int connectionId, out PlayerData player, out string reason)
    {
        reason = string.Empty;
        player = null!;

        if (_onlineUidByConnectionId.TryGetValue(connectionId, out var onlineUid))
        {
            if (_onlinePlayerByUid.TryGetValue(onlineUid, out var onlinePlayer) && onlinePlayer != null)
            {
                player = onlinePlayer;
                return true;
            }

            _onlineUidByConnectionId.Remove(connectionId);
            _onlineConnectionIdByUid.Remove(onlineUid);
        }

        if (!_pendingUidByConnectionId.TryGetValue(connectionId, out var pendingUid))
        {
            reason = "登录会话不存在或已过期";
            return false;
        }

        if (_onlinePlayerByUid.ContainsKey(pendingUid))
        {
            reason = "用户已登录";
            return false;
        }

        var loadedPlayer = _db.Players.Find(playerData => playerData.Uid == pendingUid).FirstOrDefault();
        if (!IsValidPlayerData(loadedPlayer, out reason))
        {
            return false;
        }

        loadedPlayer.AvatarCid = loadedPlayer.Roles is { Count: > 0 }
            ? loadedPlayer.Roles[0].RoleCid
            : 1001;

        _pendingUidByConnectionId.Remove(connectionId);
        _pendingConnectionIdByUid.Remove(pendingUid);
        _onlineUidByConnectionId[connectionId] = pendingUid;
        _onlineConnectionIdByUid[pendingUid] = connectionId;
        _onlinePlayerByUid[pendingUid] = loadedPlayer;
        player = loadedPlayer;
        return true;
    }

    public bool TryGetOnlinePlayer(int connectionId, out PlayerData player, out string reason)
    {
        reason = string.Empty;
        player = null!;

        if (!_onlineUidByConnectionId.TryGetValue(connectionId, out var onlineUid))
        {
            reason = "玩家未登录";
            return false;
        }

        if (!_onlinePlayerByUid.TryGetValue(onlineUid, out var onlinePlayer) || onlinePlayer == null)
        {
            _onlineUidByConnectionId.Remove(connectionId);
            _onlineConnectionIdByUid.Remove(onlineUid);
            reason = "玩家数据不存在";
            return false;
        }

        player = onlinePlayer;
        return true;
    }

    public bool TryPushMessage(int connectionId, IMessage msg, out string reason)
    {
        reason = string.Empty;
        if (connectionId <= 0 || msg == null)
        {
            reason = "推送连接参数为空";
            return false;
        }

        if (!MsgMeta.TypeToItem.TryGetValue(msg.GetType(), out var metaItem))
        {
            reason = $"消息类型未注册: {msg.GetType().FullName}";
            return false;
        }

        if (!_connectionByConnectionId.TryGetValue(connectionId, out var conn) || conn == null)
        {
            reason = $"连接不存在或已断开: {connectionId}";
            return false;
        }

        if (!conn.TrySend(metaItem.CmdId, msg))
        {
            reason = $"推送失败: connection={connectionId}";
            return false;
        }

        return true;
    }

    public bool TryPushMessage(PlayerData player, IMessage msg, out string reason)
    {
        reason = string.Empty;
        if (player == null || msg == null)
        {
            reason = "玩家或消息数据为空";
            return false;
        }

        if (!_onlineConnectionIdByUid.TryGetValue(player.Uid, out int connectionId) || connectionId <= 0)
        {
            reason = "玩家不在线或连接不存在";
            return false;
        }

        return TryPushMessage(connectionId, msg, out reason);
    }

    public bool TrySavePlayer(PlayerData player, out string reason)
    {
        try
        {
            if (player.Roles != null)
            {
                for (int i = 0; i < player.Roles.Count; i++)
                {
                    RoleData? role = player.Roles[i];
                    if (role == null)
                    {
                        continue;
                    }
                    role.FlushRuntimeCollections();
                }
            }

            _db.Players.ReplaceOne(x => x.Uid == player.Uid, player, new ReplaceOptions { IsUpsert = true });
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public void OnClientDisconnected(int connectionId)
    {
        _connectionByConnectionId.Remove(connectionId);

        if (_pendingUidByConnectionId.Remove(connectionId, out var pendingUid))
        {
            _pendingConnectionIdByUid.Remove(pendingUid);
        }

        if (_onlineUidByConnectionId.Remove(connectionId, out var onlineUid))
        {
            _onlineConnectionIdByUid.Remove(onlineUid);
            if (_onlinePlayerByUid.Remove(onlineUid, out var player))
            {
                if (!TrySavePlayer(player, out var reason))
                {
                    NetLogger.Error($"[Player] Save failed on disconnect. Uid:{player.Uid}, Error:{reason}");
                }
                if (player.Roles != null)
                {
                    for (int i = 0; i < player.Roles.Count; i++)
                    {
                        _questService.ReleaseRoleQuestRuntime(player.Roles[i]);
                    }
                }
            }
        }
    }

    private static bool IsValidPlayerData(PlayerData? player, out string reason)
    {
        if (player == null)
        {
            reason = "玩家数据不存在";
            return false;
        }

        if (string.IsNullOrWhiteSpace(player.Uid))
        {
            reason = "玩家数据损坏: Uid 为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(player.Password))
        {
            reason = "玩家数据损坏: Password 为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(player.Username))
        {
            reason = "玩家数据损坏: Username 为空";
            return false;
        }

        if (player.Roles == null)
        {
            reason = "玩家数据损坏: 核心字段缺失";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
