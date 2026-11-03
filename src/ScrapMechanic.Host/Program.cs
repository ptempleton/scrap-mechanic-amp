using System.Runtime.Versioning;
using System.Text.Json;
using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "verify-backup")
            {
                var manifest = BackupStore.Verify(args[1]);
                Console.WriteLine(JsonSerializer.Serialize(new { verified = true, manifest.SourceDirectory, files = manifest.Files.Count }));
                return 0;
            }
            if (args.Length == 0 || args[0] is "help" or "--help")
            {
                Console.WriteLine("ScrapMechanic.Host agent --config <agent.json>\nScrapMechanic.Host bridge --config <client.json>\nScrapMechanic.Host <start|stop|status|restart|backup> --config <client.json>\nScrapMechanic.Host <emergency-stop|acknowledge-launch> --confirm --config <client.json>\nScrapMechanic.Host verify-backup <archive.zip>");
                return 0;
            }
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game management requires Windows. Portable tests and backup verification can run elsewhere.");
            return await RunWindows(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, @event = "launcher_error", error = ex.Message }));
            return 1;
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunWindows(string[] args)
    {
        var confirm = args.Length == 4 && args[1] == "--confirm";
        var offset = confirm ? 2 : 1;
        if (args.Length != offset + 2 || args[offset] != "--config") throw new ArgumentException("Expected --config <path>; use --help.");
        var configPath = Path.GetFullPath(args[offset + 1]);
        if (args[0] == "agent" && !confirm)
        {
            var config = ConfigJson.Read<AgentConfiguration>(configPath);
            config.Validate();
            // The named kernel object is held for the agent lifetime, with no thread-affine lock ownership.
            using var singleton = new Mutex(false, @"Global\ScrapMechanicHost-Agent", out var created);
            if (!created) throw new InvalidOperationException("Another Scrap Mechanic agent already owns this machine.");
            Directory.CreateDirectory(config.DataDirectory);
            var log = new JsonLog(Path.Combine(config.DataDirectory, "logs"));
            try
            {
                var platform = new WindowsGamePlatform();
                var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Axolot Games", "Scrap Mechanic", "User");
                var controller = new HostController(platform, new BackupStore(userData, log), log,
                    new FileLaunchGuard(Path.Combine(config.DataDirectory, "state.json")),
                    () => ConfigJson.Read<GameSettings>(config.SettingsFile),
                    () => ConfigJson.Read<AgentConfiguration>(configPath).AllowEmergencyTermination);
                using var cancel = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
                log.Write("agent_started", "Interactive agent online; game is not started automatically.", new { platform.SessionId });
                var monitor = Monitor(controller, cancel.Token);
                try { await PipeTransport.ServeAsync(config, controller, log, cancel.Token); }
                finally { cancel.Cancel(); await monitor; }
                return 0;
            }
            catch (OperationCanceledException) { log.Write("agent_stopped", "Agent stopped; game is not force-terminated."); return 0; }
            catch (Exception ex) { log.Write("agent_error", ex.Message); throw; }
        }
        var client = ConfigJson.Read<ClientConfiguration>(configPath);
        client.Validate();
        if (args[0] == "bridge" && !confirm)
            return await Bridge.RunAsync(command => PipeTransport.SendAsync(client, command), Console.In, Console.Out);
        var reply = await PipeTransport.SendAsync(client, args[0] + (confirm ? " CONFIRM" : ""));
        Console.WriteLine(JsonSerializer.Serialize(reply, ConfigJson.Options));
        return reply.Ok ? 0 : 2;
    }

    private static async Task Monitor(HostController controller, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await controller.PollAsync();
                await Task.Delay(1000, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
}
