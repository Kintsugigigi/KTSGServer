using KTSG.Proto;
using KTSG.Server.Config;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Attributes;

namespace KTSG.Server.Model;

[BsonIgnoreExtraElements]
public sealed class PlayerData 
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Uid { get; set; } = string.Empty;

    public long NextItemInstanceId { get; set; } = 1;

    public string Password { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public int AvatarCid { get; set; }

    public List<RoleData> Roles { get; set; } = new();

    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, List<ContractRecord>> ContractRecords { get; set; } = new();

    [BsonIgnore]
    public RoleData CurrentRole { get; set; }
    

    public NPlayerProfile ToNPlayerProfile()
    {
        var player = new NPlayerProfile
        {
            SocialProfile = new NSocialProfile
            {
                Uid = Uid,
                Username = Username,
                AvatarCid = AvatarCid
            }
        };

        player.RoleSums.AddRange(Roles.Select(role => role.ToNRoleSummary()));
        return player;
    }
}
