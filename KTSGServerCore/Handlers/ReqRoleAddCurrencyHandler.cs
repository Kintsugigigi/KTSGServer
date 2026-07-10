using Google.Protobuf;
using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqRoleAddCurrencyHandler : ReqHandler<ReqRoleAddCurrency, RspRoleInventoryChange>
    {
        protected override void Run(INetConnection conn, ReqRoleAddCurrency req, RspRoleInventoryChange rsp, ushort rpcSeq)
        {
            var roleService = ServerRuntime.Instance.GetService<RoleService>();
            var itemService = ServerRuntime.Instance.GetService<ItemService>();
            var playerService = ServerRuntime.Instance.GetService<PlayerService>();

            if (!playerService.TryGetOnlinePlayer(conn.ConnectionId, out var player, out var reason) ||
                !roleService.TryGetCurrentRole(player, out var role, out reason) ||
                !itemService.TryAddCurrencyToRole(role, req.CurrencyCid, req.Count, out var delta, out reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };
            rsp.Delta = delta;
            Reply(conn, rsp, rpcSeq);
        }
    }
}
