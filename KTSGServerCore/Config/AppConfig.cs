using Tomlyn;

namespace KTSG.Server.Config
{
    public static class AppConfig
    {
        private static readonly TomlSerializerOptions TomlOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public const string DefaultFileName = "appconfig.toml";
        public static string ConfigPath { get; private set; } = Path.Combine(AppContext.BaseDirectory, DefaultFileName);
        
        public static AppConfigData Data;

        public static AppConfigData LoadOrCreate(string? configPath = null)
        {
            ConfigPath = string.IsNullOrWhiteSpace(configPath)
                ? Path.Combine(AppContext.BaseDirectory, DefaultFileName)
                : Path.GetFullPath(configPath);
            var toml = File.ReadAllText(ConfigPath);
            Data = TomlSerializer.Deserialize<AppConfigData>(toml, TomlOptions) ?? new AppConfigData();
            return Data;
        }
    }

    public sealed class AppConfigData
    {
        public int LogicalFrameRate { get; set; } = 30;
        public ServerConfig Server { get; set; } = new();
        public DatabaseConfig Database { get; set; } = new();
    }

    public sealed class ServerConfig
    {
        public string BindAddress { get; set; } = "0.0.0.0";
        public int TcpPort { get; set; } = 7000;
        public int UdpPort { get; set; } = 7001;
        public int MaxSnapshotFrames { get; set; } = 90;
    }

    public sealed class DatabaseConfig
    {
        public bool Enabled { get; set; } = true;
        public string ConnectionString { get; set; } = "mongodb://127.0.0.1:27017";
        public string DatabaseName { get; set; } = "ktsg_server";
    }
}
