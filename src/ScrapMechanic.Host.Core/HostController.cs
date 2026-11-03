namespace ScrapMechanic.Host.Core;

// Every state transition and backup is serialized, including periodic reconciliation.
public sealed class HostController(IGamePlatform platform, IBackupStore backups, IEventSink log,
    ILaunchGuard launchGuard, Func<GameSettings> loadSettings, Func<bool> allowEmergencyTermination,
    TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private GameProcess? managed;
    private GameSettings? activeSettings = launchGuard.ActiveSettings;
    private string? failure;
    private bool stopRequested;

    public async Task<Reply> ExecuteAsync(string command)
    {
        await gate.WaitAsync();
        try
        {
            Reconcile();
            switch (command)
            {
                case "status": return new(true, "Process observation only; multiplayer readiness is unverified.", Status());
                case "start":
                    await Start();
                    return new(true, "Game process observed; multiplayer readiness is unverified.", Status());
                case "stop":
                    var stoppedBackup = await Stop();
                    return new(true, "Game exited. A save flush is not proven by process exit.", Status(), stoppedBackup);
                case "restart":
                    var restartBackup = await Stop();
                    await Start();
                    return new(true, "Restart completed; multiplayer readiness is unverified.", Status(), restartBackup);
                case "backup":
                    var settings = activeSettings ?? Settings();
                    var backup = await Backup(settings);
                    launchGuard.UnblockStart();
                    return new(true, "Stopped backup completed.", Status(), backup);
                case "emergency-stop CONFIRM":
                    if (!allowEmergencyTermination()) throw new HostOperationException("Emergency termination is disabled in agent.json.");
                    if (managed is null) throw new HostOperationException("No verified game process to terminate.");
                    log.Write("emergency_termination", "Explicit emergency termination requested. Recent changes may be lost.", managed);
                    launchGuard.BlockStart("Emergency termination requested; inspect the world and run a stopped backup before starting.");
                    platform.EmergencyKill(managed);
                    if (!await WaitForExit(managed, TimeSpan.FromSeconds(10)))
                        throw new HostOperationException("Emergency termination did not complete.");
                    managed = null;
                    launchGuard.Clear();
                    stopRequested = false;
                    failure = "Emergency termination occurred; world consistency is unverified. No automatic backup taken.";
                    return new(true, failure, Status());
                case "acknowledge-launch CONFIRM":
                    AssertStoppedProcesses();
                    launchGuard.Clear();
                    failure = null;
                    log.Write("launch_acknowledged", "Operator confirmed Steam has no pending launch dialog or download.");
                    return new(true, "Pending-launch interlock cleared.", Status());
                default: throw new HostOperationException("Unknown command. Use start, stop, status, restart, backup, emergency-stop CONFIRM, or acknowledge-launch CONFIRM.");
            }
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            log.Write("operation_failed", ex.Message, new { command, exception = ex.GetType().Name });
            return new(false, ex.Message, Status());
        }
        finally { gate.Release(); }
    }

    public async Task PollAsync()
    {
        // A stop/start/backup already observes its own critical interval.
        if (!await gate.WaitAsync(0)) return;
        try { Reconcile(); }
        catch (Exception ex)
        {
            if (failure != ex.Message) log.Write("monitor_error", ex.Message);
            failure = ex.Message;
        }
        finally { gate.Release(); }
    }

    private GameSettings Settings()
    {
        var settings = loadSettings();
        settings.Validate();
        return settings;
    }

    private HostStatus Status() => new(managed is not null ? (stopRequested ? "StopFailed" : "Running") :
        launchGuard.Pending ? "LaunchUnresolved" : failure is null && launchGuard.StartBlockedReason is null ? "Stopped" : "Faulted",
        managed?.Id, platform.SessionId, false, failure ?? launchGuard.StartBlockedReason);

    private void Reconcile()
    {
        if (managed is not null && !platform.IsAlive(managed))
        {
            var expected = stopRequested;
            log.Write(expected ? "late_exit" : "unexpected_exit",
                expected ? "Game exited after an earlier stop request." : "Game exited unexpectedly; no automatic restart.", managed);
            managed = null;
            stopRequested = false;
            failure = expected ? "Late exit; requested backup was not run. Use backup before starting." : "Unexpected game exit; inspect the game and launcher logs.";
        }
        var candidates = platform.FindGames();
        if (candidates.Count == 0) return;
        if (candidates.Count != 1) throw new HostOperationException("Multiple Scrap Mechanic processes exist. Resolve them manually.");
        var candidate = candidates[0];
        var settings = activeSettings ?? Settings();
        if (candidate.SessionId != platform.SessionId || !SamePath(candidate.Executable, settings.GameExecutable))
            throw new HostOperationException("A conflicting Scrap Mechanic process exists in another session or executable location.");
        if (managed is not null && candidate != managed)
            throw new HostOperationException("Process identity changed unexpectedly.");
        if (managed is null)
        {
            managed = candidate;
            activeSettings = settings;
            launchGuard.ActiveSettings = settings;
            log.Write("process_observed", "Attached to the unique matching process in this user session. Readiness is unverified.", candidate);
        }
    }

    private async Task Start()
    {
        if (launchGuard.StartBlockedReason is not null) throw new HostOperationException(launchGuard.StartBlockedReason);
        if (managed is not null)
        {
            if (stopRequested) throw new HostOperationException("Previous stop failed; resolve it before restarting.");
            if (launchGuard.Pending) await ObserveStable(activeSettings!);
            return; // Idempotent reconnect, never launches a second copy.
        }
        if (launchGuard.Pending) throw new HostOperationException("An earlier launch is unresolved. Inspect Steam before acknowledge-launch CONFIRM.");
        var settings = Settings();
        activeSettings = settings;
        launchGuard.ActiveSettings = settings;
        AssertStoppedProcesses();
        launchGuard.MarkPending(); // Survives an agent crash or a delayed Steam launch.
        log.Write("start_requested", "Requesting Steam game startup.", new { settings.LaunchMode });
        platform.Launch(settings);
        await ObserveStable(settings);
    }

    private async Task ObserveStable(GameSettings settings)
    {
        var deadline = time.GetUtcNow().AddSeconds(settings.StartupTimeoutSeconds);
        DateTimeOffset? stableSince = null;
        GameProcess? last = null;
        while (time.GetUtcNow() < deadline)
        {
            Reconcile();
            if (managed is not null)
            {
                if (managed != last) { last = managed; stableSince = time.GetUtcNow(); }
                if (time.GetUtcNow() - stableSince >= TimeSpan.FromSeconds(settings.StartupStableSeconds))
                {
                    launchGuard.Clear();
                    failure = null;
                    log.Write("started", "Game process remained present for the configured observation interval. Multiplayer is unverified.", managed);
                    return;
                }
            }
            else { last = null; stableSince = null; }
            await Delay(250);
        }
        throw new HostOperationException("Startup timed out. Steam may still launch later; no automatic retry or termination.");
    }

    private async Task<string?> Stop()
    {
        var settings = activeSettings ?? Settings();
        if (managed is not null)
        {
            var target = managed;
            launchGuard.BlockStart("Stop or requested backup has not completed. Retry stop, or run backup once the game has exited.");
            log.Write("stop_requested", "Sending WM_CLOSE to the game's main window.", target);
            if (!platform.RequestClose(target)) throw new HostOperationException("No accessible main window accepted WM_CLOSE; game was not terminated.");
            stopRequested = true;
            if (!await WaitForExit(target, TimeSpan.FromSeconds(settings.ShutdownTimeoutSeconds)))
                throw new HostOperationException("Graceful shutdown timed out. Game is still running; no force termination was attempted.");
            managed = null;
            stopRequested = false;
            launchGuard.Clear();
            log.Write("stopped", "Observed game process exit. Save flush remains unverified.", target);
        }
        AssertStopped();
        failure = null;
        if (settings.BackupOnStop) launchGuard.BlockStart("Requested stopped backup has not completed. Retry stop or backup before starting.");
        var result = settings.BackupOnStop ? await Backup(settings) : null;
        launchGuard.UnblockStart();
        activeSettings = null;
        launchGuard.ActiveSettings = null;
        return result;
    }

    private async Task<string> Backup(GameSettings settings)
    {
        AssertStopped();
        await Delay(settings.StoppedSettleSeconds * 1000);
        AssertStopped();
        var result = backups.Create(settings, AssertStopped);
        log.Write("backup_completed", "Verified stopped backup created.", new { path = result });
        return result;
    }

    private async Task<bool> WaitForExit(GameProcess target, TimeSpan timeout)
    {
        var deadline = time.GetUtcNow() + timeout;
        while (platform.IsAlive(target) && time.GetUtcNow() < deadline) await Delay(250);
        return !platform.IsAlive(target);
    }

    private void AssertStopped()
    {
        if (launchGuard.Pending) throw new HostOperationException("A Steam launch is unresolved; stopped operations are blocked.");
        AssertStoppedProcesses();
    }

    private void AssertStoppedProcesses()
    {
        if (platform.FindGames().Count != 0) throw new HostOperationException("Scrap Mechanic is running. Operation requires all game processes to be stopped.");
    }

    private Task Delay(int milliseconds) => Task.Delay(TimeSpan.FromMilliseconds(milliseconds), time);
    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
