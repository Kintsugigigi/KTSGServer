using KTSG.Server.Config;
using KTSG.Server.Runtime;
using KTSG.Network;
using Serilog;
using Serilog.Events;

namespace KTSG.Server
{
    internal static class Program
    {
        private const int LoopSleepMs = 1;
        private static volatile bool _shutdownRequested;

        private static int Main()
        {
            ServerRuntime? runtime = null;
            try
            {
                var config = AppConfig.LoadOrCreate();

                ConfigureLogging();
                PrintStartupSummary(config);

                runtime = ServerRuntime.Instance;
                Console.CancelKeyPress += OnCancelKeyPress;
                runtime.Start();

                while (!_shutdownRequested)
                {
                    runtime.Tick();
                    Thread.Sleep(LoopSleepMs);
                }

                runtime.Stop();
                return 0;
            }
            catch (Exception ex)
            {
                NetLogger.Error(ex, "[Server] Fatal startup/runtime exception.");
                return 1;
            }
            finally
            {
                runtime?.Stop();
                Console.CancelKeyPress -= OnCancelKeyPress;
                Log.CloseAndFlush();
            }
        }

        private static void ConfigureLogging()
        {
            // Initialize the global Serilog pipeline used by NetLogger on server builds.
            // This sets the minimum log levels and the console output format for all logs
            // forwarded through NetLogger.Info/Warning/Error/Debug.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        private static void PrintStartupSummary(AppConfigData config)
        {
            var server = config.Server;
            var database = config.Database;

            NetLogger.Info($"[Server] Config loaded. Bind={server.BindAddress}, Tcp={server.TcpPort}, LogicalFrameRate={config.LogicalFrameRate}");

            NetLogger.Info($"[Server] Database config. Enabled={database.Enabled}, Database={database.DatabaseName}, Connection={database.ConnectionString}");
        }

        private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _shutdownRequested = true;
            NetLogger.Info("[Server] Shutdown requested.");
        }
    }
}
