using KTSG.Server.Model;
using KTSG.Server.Config;
using MongoDB.Driver;

namespace KTSG.Server.Services;

public sealed class DbService : IService
{
    public MongoClient Client { get; private set; } = null!;

    public IMongoDatabase Database { get; private set; } = null!;

    public IMongoCollection<PlayerData> Players => Database.GetCollection<PlayerData>("player");

    public IMongoCollection<ChatData> Chats => Database.GetCollection<ChatData>("chat");

    public void Init()
    {
        Client = new MongoClient(AppConfig.Data.Database.ConnectionString);
        Database = Client.GetDatabase(AppConfig.Data.Database.DatabaseName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var usernameIndex = new CreateIndexModel<PlayerData>(
            Builders<PlayerData>.IndexKeys.Ascending(player => player.Username),
            new CreateIndexOptions
            {
                Unique = true,
                Name = "ux_player_username"
            });

        Players.Indexes.CreateOne(usernameIndex);
    }
}
