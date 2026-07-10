using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqGetPlayerDataHandler : ReqHandler<ReqGetPlayerData, RspGetPlayerData>
    {
        protected override void Run(INetConnection conn, ReqGetPlayerData req, RspGetPlayerData rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();

            if (!playerService.TryCompletePendingLogin(conn.ConnectionId, out var player, out var reason))
            {
                NetLogger.Warning($"[GetPlayerData] Fail. ConnectionId={conn.ConnectionId}, Reason={reason}");
                rsp.Result = new Result
                {
                    Code = ResultCode.Fail,
                    Msg = reason
                };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result
            {
                Code = ResultCode.Success,
                Msg = "success"
            };
            rsp.Player = player.ToNPlayerProfile();
            NetLogger.Info($"[GetPlayerData] Success. ConnectionId={conn.ConnectionId}, Uid={player.Uid}, Username={player.Username}");
            Reply(conn, rsp, rpcSeq);
        }
    }
}
