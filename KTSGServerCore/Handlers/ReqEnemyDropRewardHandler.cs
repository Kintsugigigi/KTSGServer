using System;
using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqEnemyDropRewardHandler : ReqHandler<ReqEnemyDropReward, RspEnemyDropReward>
    {
        protected override void Run(INetConnection conn, ReqEnemyDropReward req, RspEnemyDropReward rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var sceneService = ServerRuntime.Instance.GetService<SceneService>();

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason) ||
                !sceneService.TryRecordEnemyKill(role, req.EnemyUid, req.ConfigId, out reason) ||
                !sceneService.TryGrantEnemyDropReward(player, role, req.EnemyUid, out var delta, out var reward, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
            rsp.Delta = delta;
            rsp.Reward = reward;
            Reply(conn, rsp, rpcSeq);
        }
    }
}
