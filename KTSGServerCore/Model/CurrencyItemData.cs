using KTSG.Proto;
using MongoDB.Bson.Serialization.Attributes;
namespace KTSG.Server.Model;

[BsonIgnoreExtraElements]
public sealed class CurrencyItemData 
{
    public int Cid { get; set; }

    public int Count { get; set; } = 0;
    

    public NCurrencyItem ToNCurrencyItem()
    {
        return new NCurrencyItem
        {
            Cid = Cid,
            Count = Count
        };
    }
}