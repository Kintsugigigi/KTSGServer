using System;
using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqSceneEnemiesHandler : ReqHandler<ReqSceneEnemies, RspSceneEnemies>
    {
        protected override void Run(INetConnection conn, ReqSceneEnemies req, RspSceneEnemies rsp, ushort rpcSeq)
        {
            var sceneService = ServerRuntime.Instance.GetService<SceneService>();
            if (!sceneService.TryBuildSceneEnemies(req.SceneCid, out rsp, out var reason))
            {
                NetLogger.Warning($"[ReqSceneEnemies] Fail. ConnectionId={conn.ConnectionId}, SceneCid={req.SceneCid}, Reason={reason}");
                Reply(conn, rsp, rpcSeq);
                return;
            }

            NetLogger.Info($"[ReqSceneEnemies] Success. ConnectionId={conn.ConnectionId}, SceneCid={req.SceneCid}, EnemyCount={rsp.Enemies.Count}");
            Reply(conn, rsp, rpcSeq);
        }
    }
}
