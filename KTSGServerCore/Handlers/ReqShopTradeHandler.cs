using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqShopTradeHandler : ReqHandler<ReqShopTrade, RspShopTrade>
    {
        protected override void Run(INetConnection conn, ReqShopTrade req, RspShopTrade rsp, ushort rpcSeq)
        {
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var itemService = ServerRuntime.Instance.GetService<ItemService>();

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason) ||
                !itemService.TryShopTrade(
                    player,
                    role,
                    req.Mode,
                    req.ShopId,
                    req.ItemConfigId,
                    req.CurrencyId,
                    req.UnitPrice,
                    req.Count,
                    out var delta,
                    out var reward,
                    out reason))
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
