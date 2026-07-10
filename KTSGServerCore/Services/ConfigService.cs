using Luban;
using cfg;
namespace KTSG.Server.Services;

public sealed class ConfigService : IService
{
    public string BinDirectory { get; }

    public Tables Tables { get; private set; } = null!;

    public ConfigService()
        : this(null)
    {
    }

    private ConfigService(string? binDirectory)
    {
        BinDirectory = string.IsNullOrWhiteSpace(binDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Data", "LubanBin")
            : Path.GetFullPath(binDirectory);
    }

    public void Init()
    {
        if (!Directory.Exists(BinDirectory))
        {
            throw new DirectoryNotFoundException($"Luban bin directory not found: {BinDirectory}");
        }

        Tables = new Tables(LoadTableBytes);
    }

    public ByteBuf LoadTableBytes(string tableName)
    {
        var filePath = Path.Combine(BinDirectory, $"{tableName}.bytes");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Luban config not found: {filePath}");
        }

        return ByteBuf.Wrap(File.ReadAllBytes(filePath));
    }
}
