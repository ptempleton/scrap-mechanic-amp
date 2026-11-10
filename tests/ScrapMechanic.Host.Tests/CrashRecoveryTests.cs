using System.Text.Json;
using System.Threading.Channels;
using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host.Tests;

internal static class CrashRecoveryTests
{
    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new Exception(message);
    }

    private static Fixture Enabled(bool emergency = false)
    {
        var f = new Fixture(emergency);
        // Long countdown avoids depending on real scheduler speed. Tests advance the clock explicitly.
        f.Settings = f.Settings with { AutoRestartOnCrash = true, CrashRestartDelaySeconds = 300, CrashRestartWindowSeconds = 3600 };
        return f;
    }

    private static async Task Crash(Fixture f, HostController? controller = null)
    {
        f.Platform.Games.Clear();
        await (controller ?? f.Controller).ExecuteAsync("status");
    }

    private static async Task Recover(Fixture f, HostController? controller = null)
    {
        f.Clock.Advance(TimeSpan.FromSeconds(301));
        await (controller ?? f.Controller).PollAsync();
    }

    public static void Register(Action<string, Func<Task>> test)
    {
        test("crash recovery defaults and bounds are validated", () =>
        {
            using var f = new Fixture();
            Check(!f.Settings.AutoRestartOnCrash && f.Settings.CrashRestartDelaySeconds == 30 &&
                f.Settings.CrashRestartMaxAttempts == 3 && f.Settings.CrashRestartWindowSeconds == 900);
            foreach (var invalid in new[] {
                f.Settings with { CrashRestartDelaySeconds = 0 }, f.Settings with { CrashRestartDelaySeconds = 601 },
                f.Settings with { CrashRestartMaxAttempts = 0 }, f.Settings with { CrashRestartMaxAttempts = 11 },
                f.Settings with { CrashRestartWindowSeconds = 59 }, f.Settings with { CrashRestartWindowSeconds = 86401 },
                f.Settings with { CrashRestartDelaySeconds = 300, CrashRestartWindowSeconds = 60 } })
            {
                try { invalid.Validate(); throw new Exception("Invalid recovery setting accepted"); }
                catch (InvalidDataException) { }
            }
            return Task.CompletedTask;
        });
        test("crash waits then archives before exactly one relaunch", async () =>
        {
            using var f = Enabled();
            f.Settings = f.Settings with { BackupOnStop = false };
            Check((await f.Controller.ExecuteAsync("start")).Ok);
            await Crash(f);
            var status = (await f.Controller.ExecuteAsync("status")).Status;
            Check(status is { State: "RecoveryWaiting", ProcessId: null, MultiplayerReady: false });
            Check(f.Platform.Calls.SequenceEqual(new[] { "launch" }));
            await f.Controller.PollAsync();
            Check(f.Platform.Calls.Count == 1);
            // An AMP bridge reconnect must not skip the archive/countdown.
            Check((await f.Controller.ExecuteAsync("start")).Status?.State == "RecoveryWaiting");
            await Recover(f);
            Check(f.Platform.Calls.SequenceEqual(new[] { "launch", "backup", "launch" }));
            Check(f.Backups.LastPostCrash && f.Guard.Recovery.Attempts.Length == 1);
            Check((await f.Controller.ExecuteAsync("status")).Status is { State: "Running", MultiplayerReady: false });
            Check(f.Log.Names.Contains("unexpected_exit") && f.Log.Names.Contains("crash_recovery_succeeded"));
        });
        test("concurrent monitor ticks cannot dispatch duplicate recovery launches", async () =>
        {
            using var f = Enabled();
            await f.Controller.ExecuteAsync("start"); await Crash(f);
            f.Clock.Advance(TimeSpan.FromSeconds(301));
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Controller.PollAsync()));
            Check(f.Platform.Calls.Count(c => c == "launch") == 2 && f.Guard.Recovery.Attempts.Length == 1);
        });
        test("Stop cancels countdown durably and uses a crash archive", async () =>
        {
            using var f = Enabled();
            await f.Controller.ExecuteAsync("start"); await Crash(f);
            Check((await f.Controller.ExecuteAsync("stop")).Ok);
            Check(f.Guard.Recovery.DueUtc is null && f.Backups.LastPostCrash);
            await Recover(f, f.NewController());
            Check(f.Platform.Calls.Count(c => c == "launch") == 1);
            Check(f.Log.Names.Contains("crash_recovery_cancelled"));
        });
        test("Stop wins when it is the first command to discover an exit", async () =>
        {
            using var f = Enabled();
            await f.Controller.ExecuteAsync("start");
            f.Platform.Games.Clear();
            Check((await f.Controller.ExecuteAsync("stop")).Ok);
            await Recover(f);
            Check(f.Platform.Calls.Count(c => c == "launch") == 1 && f.Guard.Recovery.DueUtc is null);
        });
        test("intentional stop, restart, failed close and emergency stop never schedule crash recovery", async () =>
        {
            foreach (var command in new[] { "stop", "restart", "failed-close", "emergency-stop CONFIRM" })
            {
                using var f = Enabled(emergency: true);
                await f.Controller.ExecuteAsync("start");
                if (command == "failed-close") f.Platform.HasWindow = false;
                await f.Controller.ExecuteAsync(command == "failed-close" ? "stop" : command);
                if (command == "failed-close") f.Platform.Games.Clear();
                await f.Controller.PollAsync();
                Check(f.Guard.Recovery.DueUtc is null && !f.Log.Names.Contains("crash_recovery_scheduled"), command);
            }
        });
        test("three attempts exhaust the persistent budget and never force kill", async () =>
        {
            using var f = Enabled();
            var controller = f.Controller;
            await controller.ExecuteAsync("start");
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await Crash(f, controller);
                // Recreate the agent while waiting: retry count and pending intent survive.
                controller = f.NewController();
                await Recover(f, controller);
                Check(f.Guard.Recovery.Attempts.Length == attempt);
                Check((await controller.ExecuteAsync("status")).Status?.State == "Running");
            }
            await Crash(f, controller); await Recover(f, controller);
            Check(f.Platform.Calls.Count(c => c == "launch") == 4);
            Check(f.Guard.StartBlockedReason?.Contains("limit") == true && f.Guard.Recovery.DueUtc is null);
            Check(!(await f.NewController().ExecuteAsync("start")).Ok && !f.Platform.Calls.Contains("kill"));
        });
        test("retry window expires old attempts but not future timestamps", async () =>
        {
            using var f = Enabled();
            f.Settings = f.Settings with { CrashRestartMaxAttempts = 1 };
            await f.Controller.ExecuteAsync("start"); await Crash(f); await Recover(f);
            f.Clock.Advance(TimeSpan.FromSeconds(3601));
            await Crash(f); await Recover(f);
            Check(f.Guard.Recovery.Attempts.Length == 1 && f.Platform.Calls.Count(c => c == "launch") == 3);
            f.Clock.Advance(TimeSpan.FromSeconds(-7200));
            await Crash(f);
            Check(f.Guard.StartBlockedReason?.Contains("limit") == true);
        });
        foreach (var fault in new[] { "backup", "steam", "dispatch", "timeout", "conflict", "inspection" })
            test("crash recovery fails closed: " + fault, async () =>
            {
                using var f = Enabled();
                await f.Controller.ExecuteAsync("start"); await Crash(f);
                switch (fault)
                {
                    case "backup": f.Backups.Throws = true; break;
                    case "steam": f.Platform.SteamAvailable = false; break;
                    case "dispatch": f.Platform.LaunchThrows = true; break;
                    case "timeout": f.Platform.LaunchAppears = false; break;
                    case "conflict": f.Platform.Games.Add(new(999, 999, f.Settings.GameExecutable, 1)); break;
                    case "inspection": f.Platform.InspectionThrows = true; break;
                }
                await Recover(f);
                var launches = f.Platform.Calls.Count(c => c == "launch");
                Check(launches == (fault is "dispatch" or "timeout" ? 2 : 1));
                Check(f.Guard.StartBlockedReason is not null && f.Guard.Recovery.DueUtc is null);
                Check(f.Guard.Pending == (fault is "dispatch" or "timeout"));
                Check(!(await f.NewController().ExecuteAsync("start")).Ok);
                await Recover(f, f.NewController());
                Check(f.Platform.Calls.Count(c => c == "launch") == launches && !f.Platform.Calls.Contains("kill"));
            });
        test("initial unresolved launch never enters crash recovery", async () =>
        {
            using var f = Enabled();
            f.Platform.LaunchAppears = false;
            Check(!(await f.Controller.ExecuteAsync("start")).Ok);
            await Recover(f);
            Check(f.Guard.Pending && f.Guard.Recovery.DueUtc is null && f.Guard.Recovery.Attempts.Length == 0);
            Check(f.Platform.Calls.SequenceEqual(new[] { "launch" }));
        });
        test("failed manual backup during countdown cancels automatic recovery", async () =>
        {
            using var f = Enabled();
            await f.Controller.ExecuteAsync("start"); await Crash(f);
            f.Backups.Throws = true;
            Check(!(await f.Controller.ExecuteAsync("backup")).Ok);
            Check(f.Guard.Recovery.DueUtc is null && f.Guard.StartBlockedReason is not null);
            f.Backups.Throws = false;
            await Recover(f, f.NewController());
            Check(f.Platform.Calls.Count(c => c == "launch") == 1);
        });
        test("captured world settings are used for crash archive and relaunch", async () =>
        {
            using var f = Enabled();
            var original = f.Settings;
            await f.Controller.ExecuteAsync("start"); await Crash(f);
            f.Settings = f.Settings with { SaveDirectory = Path.Combine(f.Root, "other-world"), GameExecutable = Path.Combine(f.Root, "other-game.exe") };
            await Recover(f);
            Check(f.Backups.LastSettings == original && f.Platform.Games.Single().Executable == original.GameExecutable);
        });
        test("recovery state upgrades old files and survives disk reload", async () =>
        {
            using var f = Enabled();
            var path = Path.Combine(f.Root, "state.json");
            File.WriteAllText(path, "{\"Pending\":false,\"StartBlockedReason\":null,\"ActiveSettings\":null}");
            var guard = new FileLaunchGuard(path);
            Check(guard.Recovery.Attempts.Length == 0 && guard.Recovery.DueUtc is null);
            HostController Controller(FileLaunchGuard state) => new(f.Platform, f.Backups, f.Log, state, () => f.Settings, () => false, f.Clock);
            var controller = Controller(guard);
            await controller.ExecuteAsync("start"); await Crash(f, controller);
            var reloaded = new FileLaunchGuard(path);
            Check(reloaded.Recovery.NeedsArchive && reloaded.Recovery.DueUtc == guard.Recovery.DueUtc);
            controller = Controller(reloaded); await Recover(f, controller);
            Check(new FileLaunchGuard(path).Recovery.Attempts.Length == 1);
            await Crash(f, controller); await controller.ExecuteAsync("stop");
            await Recover(f, Controller(new FileLaunchGuard(path)));
            Check(f.Platform.Calls.Count(c => c == "launch") == 2);
        });
        test("interrupted recovery requires operator inspection and backup", async () =>
        {
            using var f = Enabled();
            await f.Controller.ExecuteAsync("start"); await Crash(f);
            f.Guard.BlockStart("Crash recovery was interrupted");
            await Recover(f, f.NewController());
            Check(f.Platform.Calls.SequenceEqual(new[] { "launch" }));
            Check(f.Guard.Recovery.DueUtc is null && f.Guard.StartBlockedReason is not null);
        });
        test("crash retention cannot remove normal backups", () =>
        {
            using var f = new Fixture();
            var store = new BackupStore(f.Root, f.Log);
            var settings = f.Settings with { BackupRetention = 2 };
            var good1 = store.Create(settings, () => { });
            var good2 = store.Create(settings, () => { });
            var crash1 = store.Create(settings with { BackupRetention = 1 }, () => { }, postCrash: true);
            var crash2 = store.Create(settings with { BackupRetention = 1 }, () => { }, postCrash: true);
            Check(File.Exists(good1) && File.Exists(good2) && !File.Exists(crash1) && File.Exists(crash2));
            Check(crash2.StartsWith(Path.Combine(settings.BackupDirectory, "crashes") + Path.DirectorySeparatorChar));
            Check(BackupStore.Verify(crash2).Files.Count > 0);
            return Task.CompletedTask;
        });
        test("bridge stays connected through recovery and accepts Stop during countdown", async () =>
        {
            var reader = new CommandReader();
            var output = new StringWriter();
            var commands = new List<string>();
            var polls = 0;
            var code = await Bridge.RunAsync(c =>
            {
                commands.Add(c);
                if (c == "stop") return Task.FromResult(new Reply(true, "stopped", new("Stopped", null, 1)));
                if (c == "start") return Task.FromResult(new Reply(true, "started", new("Running", 1, 1)));
                polls++;
                if (polls == 4) reader.Send("stop");
                return Task.FromResult(new Reply(true, "observed", polls == 3 ? new("Running", 2, 1) : new("RecoveryWaiting", null, 1)));
            }, reader, output, TimeSpan.FromMilliseconds(1)).WaitAsync(TimeSpan.FromSeconds(5));
            Check(code == 0 && commands.Count(c => c == "start") == 1 && commands.Last() == "stop");
            Check(output.ToString().Contains("SM_CRASH_RECOVERY_WAITING") && !output.ToString().Contains("SM_PROCESS_EXITED"));
        });
        test("bridge reconnect during recovery never emits premature ready marker", async () =>
        {
            var output = new StringWriter();
            var code = await Bridge.RunAsync(c => Task.FromResult(new Reply(true, "ok",
                c == "stop" ? new("Stopped", null, 1) : new("RecoveryWaiting", null, 1))), new StringReader("stop\n"), output);
            Check(code == 0 && !output.ToString().Contains("SM_PROCESS_RUNNING"));
        });
    }

    private sealed class CommandReader : TextReader
    {
        private readonly Channel<string?> commands = Channel.CreateUnbounded<string?>();
        public void Send(string command) => commands.Writer.TryWrite(command);
        public override Task<string?> ReadLineAsync() => commands.Reader.ReadAsync().AsTask();
    }
}
