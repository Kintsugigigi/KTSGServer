using cfg;
using KTSG.Proto;
using KTSG.Server.Config;
using MongoDB.Bson.Serialization.Attributes;

namespace KTSG.Server.Model;

[BsonIgnoreExtraElements]
public sealed class InventoryItemData
{
    public long InstanceId { get; set; } = -1;

    public int ConfigId { get; set; } = -1;

    public int Count { get; set; } = 0;

    public EItemType ItemType { get; set; } = EItemType.None;

    [BsonIgnoreIfNull]
    public WeaponItemData? Weapon { get; set; }
    

    public NItem ToNItem()
    {
        var item = new NItem
        {
            InstanceId = InstanceId,
            ConfigId = ConfigId,
            Count = Count,
            ItemType = (int)ItemType,
        };

        if (Weapon != null)
        {
            item.Weapon = Weapon.ToNWeaponItem();
        }

        return item;
    }
}
