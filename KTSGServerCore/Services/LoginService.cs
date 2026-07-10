using KTSG.Proto;
using KTSG.Server.Model;
using KTSG.Server.Runtime;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KTSG.Server.Services;

public readonly record struct LoginResult(ResultCode Code, string Msg, string UserUid = "");

public class LoginService : IService
{
    private DbService _db = null!;

    public void Init()
    {
        _db = ServerRuntime.Instance.GetService<DbService>();
    }

    public LoginResult Register(string username, string password)
    {
        if (!Validate(username, password, out var reason))
        {
            return Fail(reason);
        }

        try
        {
            if (FindPlayerByUsername(username) != null)
            {
                return Fail("用户名已存在");
            }

            var player = CreateNewPlayer(username, password);
            try
            {
                _db.Players.InsertOne(player);
            }
            catch (MongoWriteException ex) when (IsDuplicateKey(ex))
            {
                return Fail("用户名已存在");
            }

            return Success(player.Uid);
        }
        catch (Exception ex)
        {
            return Fail($"注册失败: {ex.Message}");
        }
    }

    public LoginResult Login(string username, string password)
    {
        if (!Validate(username, password, out var reason))
        {
            return Fail(reason);
        }

        try
        {
            var player = FindPlayerByUsernameAndPassword(username, password);
            if (player == null)
            {
                return Fail("用户名或密码错误");
            }

            if (!IsValidPlayerData(player, out reason))
            {
                return Fail(reason);
            }

            return Success(player.Uid);
        }
        catch (Exception ex)
        {
            return Fail($"登录失败: {ex.Message}");
        }
    }

    private PlayerData FindPlayerByUsername(string username)
    {
        return _db.Players.Find(player => player.Username == username).FirstOrDefault();
    }

    private PlayerData FindPlayerByUsernameAndPassword(string username, string password)
    {
        return _db.Players.Find(player => player.Username == username && player.Password == password).FirstOrDefault();
    }

    private PlayerData CreateNewPlayer(string username, string password)
    {
        return new PlayerData
        {
            Uid = ObjectId.GenerateNewId().ToString(),
            Password = password,
            Username = username,
            AvatarCid = 1001,
            Roles = new List<RoleData>()
        };
    }

    private static bool Validate(string username, string password, out string reason)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            reason = "用户名不能为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            reason = "密码不能为空";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsValidPlayerData(PlayerData player, out string reason)
    {
        if (player == null)
        {
            reason = "玩家数据不存在";
            return false;
        }

        if (string.IsNullOrWhiteSpace(player.Uid))
        {
            reason = "玩家数据损坏: Uid 为空";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsDuplicateKey(MongoWriteException ex)
    {
        return ex.WriteError?.Category == ServerErrorCategory.DuplicateKey;
    }

    private static LoginResult Success(string userUid)
    {
        return new LoginResult(ResultCode.Success, "success", userUid);
    }

    private static LoginResult Fail(string message)
    {
        return new LoginResult(ResultCode.Fail, message);
    }
}
