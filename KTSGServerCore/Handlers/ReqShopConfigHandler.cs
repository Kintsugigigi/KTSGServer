using KTSG.Network;
using KTSG.Proto;
using KTSG.Server.Runtime;
using KTSG.Server.Services;

namespace KTSG.NetWork
{
    public class ReqShopConfigHandler : ReqHandler<ReqShopConfig, RspShopConfig>
    {
        protected override void Run(INetConnection conn, ReqShopConfig req, RspShopConfig rsp, ushort rpcSeq)
        {
            var itemService = ServerRuntime.Instance.GetService<ItemService>();
            if (!itemService.TryGetShopConfig(req.ShopId, out var shopConfig, out var reason))
            {
                rsp.Result = new Result { Code = ResultCode.Fail, Msg = reason };
                Reply(conn, rsp, rpcSeq);
                return;
            }

            rsp.Result = new Result { Code = ResultCode.Success, Msg = "success" };

            if (shopConfig.BuyItems != null)
            {
                for (int i = 0; i < shopConfig.BuyItems.Count; i++)
                {
                    var source = shopConfig.BuyItems[i];
                    if (source == null)
                    {
                        continue;
                    }

                    rsp.BuyItems.Add(new NShopItem
                    {
                        ItemConfigId = source.ItemConfigID,
                        CurrencyId = source.CurrencyID,
                        CurrencyCount = source.CurrencyCount
                    });
                }
            }

            if (shopConfig.SellItems != null)
            {
                for (int i = 0; i < shopConfig.SellItems.Count; i++)
                {
                    var source = shopConfig.SellItems[i];
                    if (source == null)
                    {
                        continue;
                    }

                    rsp.SellItems.Add(new NShopItem
                    {
                        ItemConfigId = source.ItemConfigID,
                        CurrencyId = source.CurrencyID,
                        CurrencyCount = source.CurrencyCount
                    });
                }
            }

            Reply(conn, rsp, rpcSeq);
        }
    }
}
