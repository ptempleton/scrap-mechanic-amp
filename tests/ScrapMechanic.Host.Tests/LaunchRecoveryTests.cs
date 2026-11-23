using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host.Tests;

// Unresolved-launch recovery (2026-11-23 live incident: a game crash, then a scheduled start that
// Steam never launched, left the persistent interlock set until an operator acknowledged it).
internal static class LaunchRecoveryTests
{
    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new Exception(message);
    }

    private static Fixture Enabled()
    {
        var f = new Fixture();
        f.Settings = f.Settings with { AutoRestartOnCrash = true, CrashRestartDelaySeconds = 300, CrashRestartWindowSeconds = 3600 };
        return f;
    }

    private static int Launches(Fixture f) => f.Platform.Calls.Count(c => c == "launch");

    public static void Register(Action<string, Func<Task>> test)
    {
        test("launch interlock settings: defaults and bounds are validated", () =>
        {
            using var f = new Fixture();
            Check(f.Settings.LaunchUnresolvedClearSeconds == 600 && f.Settings.LaunchTimeoutMaxAttempts == 3);
            (f.Settings with { LaunchUnresolvedClearSeconds = 0 }).Validate();
            (f.Settings with { LaunchUnresolvedClearSeconds = 120 }).Validate();
            foreach (var invalid in new[] {
                f.Settings with { LaunchUnresolvedClearSeconds = 119 }, f.Settings with { LaunchUnresolvedClearSeconds = 86401 },
                f.Settings with { StartupTimeoutSeconds = 300, LaunchUnresolvedClearSeconds = 200 },
                f.Settings with { LaunchTimeoutMaxAttempts = 0 }, f.Settings with { LaunchTimeoutMaxAttempts = 11 } })
            {
                try { invalid.Validate(); throw new Exception("Invalid launch interlock setting accepted"); }
                catch (InvalidDataException) { }
            }
            return Task.CompletedTask;
        });
        test("a fresh unresolved launch is refused and says when it clears itself", async () =>
        {
            using var f = new Fixture();
            f.Platform.LaunchAppears = false;
            Check(!(await f.Controller.ExecuteAsync("start")).Ok);
            f.Clock.Advance(TimeSpan.FromSeconds(100));
            f.Platform.LaunchAppears = true;
            var reply = await f.Controller.ExecuteAsync("start");
            Check(!reply.Ok && reply.Message.Contains("clears itself") && f.Guard.Pending);
            Check(Launches(f) == 1 && !f.Log.Names.Contains("launch_auto_resolved"));
            // 0 = never: only acknowledge-launch CONFIRM clears it, however old.
            f.Settings = f.Settings with { LaunchUnresolvedClearSeconds = 0 };
            f.Clock.Advance(TimeSpan.FromDays(2));
            reply = await f.Controller.ExecuteAsync("start");
            Check(!reply.Ok && !reply.Message.Contains("clears itself") && f.Guard.Pending && Launches(f) == 1);
        });
        test("operator Start clears a stale unresolved launch and launches again", async () =>
        {
            using var f = new Fixture();
            f.Platform.LaunchAppears = false;
            Check(!(await f.Controller.ExecuteAsync("start")).Ok);
            Check(f.Guard.Recovery.LaunchTimeouts == 1);
            f.Clock.Advance(TimeSpan.FromSeconds(601));
            f.Platform.LaunchAppears = true;
            var reply = await f.Controller.ExecuteAsync("start");
            Check(reply.Ok && reply.Status?.State == "Running" && !f.Guard.Pending);
            Check(Launches(f) == 2 && f.Log.Names.Contains("launch_auto_resolved") && f.Guard.Recovery.LaunchTimeouts == 0);
        });
        test("a scheduled restart is not blocked by a stale unresolved launch", async () =>
        {
            using var f = new Fixture();
            f.Platform.LaunchAppears = false;
            Check(!(await f.Controller.ExecuteAsync("start")).Ok);
            f.Clock.Advance(TimeSpan.FromSeconds(601));
            f.Platform.LaunchAppears = true;
            Check((await f.Controller.ExecuteAsync("restart")).Ok);
            Check(f.Platform.Calls.SequenceEqual(new[] { "launch", "backup", "launch" }));
        });
        test("a Windows restart after an unresolved launch clears the interlock", async () =>
        {
            using var f = new Fixture();
            f.Platform.LaunchAppears = false;
            Check(!(await f.Controller.ExecuteAsync("start")).Ok);
            // Restarting only the agent (no reboot) keeps it.
            Check(!(await f.NewController().ExecuteAsync("start")).Ok && f.Guard.Pending);
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            f.BootTime = f.Clock.GetUtcNow();
            f.Platform.LaunchAppears = true;
            var controller = f.NewController();
            Check((await controller.ExecuteAsync("status")).Status?.State == "Stopped" && !f.Guard.Pending);
            Check(f.Log.Names.Contains("launch_auto_resolved"));
            Check((await controller.ExecuteAsync("start")).Ok && Launches(f) == 2);
        });
        test("old state files without a dispatch time use the file write time", () =>
        {
            using var f = new Fixture();
            var path = Path.Combine(f.Root, "state.json");
            File.WriteAllText(path, "{\"Pending\":true,\"StartBlockedReason\":null,\"ActiveSettings\":null}");
            var written = DateTime.UtcNow.AddHours(-3);
            File.SetLastWriteTimeUtc(path, written);
            var guard = new FileLaunchGuard(path);
            Check(guard.Pending && guard.PendingSinceUtc == new DateTimeOffset(written, TimeSpan.Zero));
            guard.Clear();
            Check(new FileLaunchGuard(path).PendingSinceUtc is null);
            var at = DateTimeOffset.UtcNow;
            guard.MarkPending(at);
            var reloaded = new FileLaunchGuard(path);
            Check(reloaded.Pending && reloaded.PendingSinceUtc == at);
            return Task.CompletedTask;
        });
        test("unattended: a start Steam ignored is retried through crash recovery once stale", async () =>
        {
            using var f = Enabled();
            f.Platform.LaunchAppears = false;
            Check(!(await f.Controller.ExecuteAsync("start")).Ok); // e.g. AMP's scheduled restart
            await f.Controller.PollAsync();
            Check(f.Guard.Pending && f.Guard.Recovery.DueUtc is null);
            Check((await f.Controller.ExecuteAsync("status")).Status?.State == "LaunchRetryWaiting");
            f.Platform.LaunchAppears = true;
            f.Clock.Advance(TimeSpan.FromSeconds(601));
            await f.Controller.PollAsync();
            Check(!f.Guard.Pending && f.Guard.Recovery.DueUtc is not null && f.Log.Names.Contains("launch_auto_resolved"));
            f.Clock.Advance(TimeSpan.FromSeconds(301));
            await f.Controller.PollAsync();
            Check((await f.Controller.ExecuteAsync("status")).Status?.State == "Running");
            Check(f.Platform.Calls.SequenceEqual(new[] { "launch", "backup", "launch" }) && f.Backups.LastPostCrash);
            Check(f.Guard.Recovery.LaunchTimeouts == 0 && f.Guard.Recovery.Attempts.Length == 1);
        });
        test("unattended retry needs crash recovery switched on", async () =>
        {
            using var f = new Fixture();
            f.Platform.LaunchAppears = false;
            Check(!(await f.Controller.ExecuteAsync("start")).Ok);
            f.Platform.LaunchAppears = true;
            f.Clock.Advance(TimeSpan.FromSeconds(3600));
            await f.Controller.PollAsync();
            Check(f.Guard.Pending && f.Guard.Recovery.DueUtc is null && Launches(f) == 1);
            Check((await f.Controller.ExecuteAsync("status")).Status?.State == "LaunchUnresolved");
        });
        test("a recovery launch Steam ignores is retried when stale, and the timeout cap blocks", async () =>
        {
            using var f = Enabled();
            f.Settings = f.Settings with { LaunchTimeoutMaxAttempts = 2, CrashRestartMaxAttempts = 10 };
            Check((await f.Controller.ExecuteAsync("start")).Ok);
            f.Platform.Games.Clear();
            await f.Controller.ExecuteAsync("status"); // crash observed, recovery scheduled
            f.Platform.LaunchAppears = false;
            f.Clock.Advance(TimeSpan.FromSeconds(301));
            await f.Controller.PollAsync(); // recovery launch 1 times out
            Check(f.Guard.Pending && f.Guard.StartBlockedReason is null && f.Guard.Recovery.LaunchTimeouts == 1);
            Check(f.Log.Names.Contains("crash_recovery_launch_timeout") && !f.Log.Names.Contains("crash_recovery_failed"));
            f.Clock.Advance(TimeSpan.FromSeconds(601));
            await f.Controller.PollAsync(); // stale: cleared and rescheduled
            Check(!f.Guard.Pending && f.Guard.Recovery.DueUtc is not null);
            f.Clock.Advance(TimeSpan.FromSeconds(301));
            await f.Controller.PollAsync(); // recovery launch 2 times out: cap reached
            Check(f.Guard.StartBlockedReason is not null && f.Guard.Recovery.LaunchTimeouts == 2);
            Check(f.Log.Names.Contains("crash_recovery_failed"));
            Check((await f.Controller.ExecuteAsync("status")).Status?.State == "LaunchUnresolved");
            f.Clock.Advance(TimeSpan.FromSeconds(3600));
            await f.Controller.PollAsync();
            Check(Launches(f) == 3 && !f.Platform.Calls.Contains("kill"));
        });
        test("bridge stays attached while the agent owns a launch retry", async () =>
        {
            var output = new StringWriter();
            var code = await Bridge.RunAsync(c => Task.FromResult(c == "start"
                ? new Reply(false, "Startup timed out.", new("LaunchRetryWaiting", null, 1))
                : new Reply(true, "ok", new("Stopped", null, 1))), new StringReader("stop\n"), output);
            Check(code == 0 && output.ToString().Contains("SM_CRASH_RECOVERY_WAITING") && !output.ToString().Contains("SM_PROCESS_RUNNING"));
            var failed = await Bridge.RunAsync(_ => Task.FromResult(new Reply(false, "Startup timed out.", new("LaunchUnresolved", null, 1))),
                new StringReader(""), new StringWriter());
            Check(failed == 2);
        });
        test("a lingering crash reporter is closed before the launch", async () =>
        {
            using var f = new Fixture();
            f.Platform.Reporters = 2;
            Check((await f.Controller.ExecuteAsync("start")).Ok);
            Check(f.Platform.Reporters == 0 && !f.Platform.LaunchedWithReporter && f.Log.Names.Contains("crash_reporter_closed"));
            using var clean = new Fixture();
            Check((await clean.Controller.ExecuteAsync("start")).Ok && !clean.Log.Names.Contains("crash_reporter_closed"));
        });
    }
}
