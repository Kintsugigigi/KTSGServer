using System;
using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class NtfWayPointTriggerHandler : MsgHandler<NtfWayPointTrigger>
    {
        protected override void Run(INetConnection conn, NtfWayPointTrigger msg, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var gamePlayService = ServerRuntime.Instance.GetService<GamePlayService>();

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out _) ||
                !roleService.TryGetCurrentRole(player, out var role, out _))
            {
                return;
            }

            gamePlayService.NotifyWayPointTriggered(role, msg.WaypointId);
        }
    }
}
