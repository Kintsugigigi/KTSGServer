using KTSG.Proto;
using KTSG.Server.Config;
using MongoDB.Bson.Serialization.Attributes;

namespace KTSG.Server.Model;

[BsonIgnoreExtraElements]
public sealed class WeaponItemData 
{
    public int Level { get; set; }

    public float Exp { get; set; }
    

    public NWeaponItem ToNWeaponItem()
    {
        return new NWeaponItem
        {
            Level = Level,
            Exp = Exp
        };
    }
}
