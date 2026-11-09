using System.Diagnostics;
using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host.Tests;

internal sealed class FakePlatform : IGamePlatform
{
    public int SessionId => 1;
    public List<GameProcess> Games { get; } = [];
    public List<string> Calls { get; } = [];
    public bool LaunchAppears = true;
    public bool CloseExits = true;
    public bool HasWindow = true;
    public bool LaunchThrows;
    public bool SteamAvailable = true;
    public bool InspectionThrows;
    private int nextId = 100;
    public IReadOnlyList<GameProcess> FindGames() => InspectionThrows ? throw new IOException("Inspection denied") : Games.ToArray();
    public Action PrepareLaunch(GameSettings settings)
    {
        if (!SteamAvailable) throw new HostOperationException("Start Steam in the agent's desktop session before launching the host.");
        return () =>
        {
            Calls.Add("launch");
            if (LaunchThrows) throw new IOException("Launch result is uncertain");
            if (LaunchAppears) Games.Add(new(nextId++, nextId, settings.GameExecutable, 1));
        };
    }
    public bool IsAlive(GameProcess process) => Games.Contains(process);
    public bool RequestClose(GameProcess process)
    {
        Calls.Add("close");
        if (!HasWindow) return false;
        if (CloseExits) Games.Remove(process);
        return true;
    }
    public void EmergencyKill(GameProcess process) { Calls.Add("kill"); Games.Remove(process); }
}

internal sealed class FakeBackup(FakePlatform platform) : IBackupStore
{
    public bool Throws;
    public GameSettings? LastSettings;
    public string Create(GameSettings settings, Action assertStopped)
    {
        assertStopped();
        platform.Calls.Add("backup");
        LastSettings = settings;
        if (Throws) throw new IOException("Disk full");
        return "test-backup.zip";
    }
}

internal sealed class Events : IEventSink
{
    public List<string> Names { get; } = [];
    public void Write(string eventName, string message, object? data = null) => Names.Add(eventName);
}

internal sealed class MemoryGuard : ILaunchGuard
{
    public bool Pending { get; private set; }
    public string? StartBlockedReason { get; private set; }
    public GameSettings? ActiveSettings { get; set; }
    public void MarkPending() => Pending = true;
    public void Clear() => Pending = false;
    public void BlockStart(string reason) => StartBlockedReason = reason;
    public void UnblockStart() => StartBlockedReason = null;
}

internal sealed class FastClock : TimeProvider
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly DateTimeOffset origin = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => origin + TimeSpan.FromTicks(watch.Elapsed.Ticks * 100);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new FastTimer(callback, state, dueTime, period);
    private sealed class FastTimer : ITimer
    {
        private readonly Timer timer;
        public FastTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => timer = new(callback, state, Scale(due), Scale(period));
        private static TimeSpan Scale(TimeSpan span) => span == Timeout.InfiniteTimeSpan ? span : TimeSpan.FromMilliseconds(Math.Max(1, span.TotalMilliseconds / 100));
        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(Scale(dueTime), Scale(period));
        public void Dispose() => timer.Dispose();
        public ValueTask DisposeAsync() => timer.DisposeAsync();
    }
}

internal sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "sm-test-" + Guid.NewGuid().ToString("N"));
    public FakePlatform Platform { get; } = new();
    public Events Log { get; } = new();
    public MemoryGuard Guard { get; } = new();
    public FakeBackup Backups { get; }
    public GameSettings Settings { get; set; }
    public HostController Controller { get; }
    public Fixture(bool emergency = false)
    {
        Directory.CreateDirectory(Root);
        Settings = new()
        {
            SteamDirectory = Root,
            GameExecutable = Path.Combine(Root, "ScrapMechanic.exe"),
            BackupDirectory = Path.Combine(Root, "backups"),
            SaveDirectory = Path.Combine(Root, "saves"),
            StartupStableSeconds = 1,
            StartupTimeoutSeconds = 5,
            ShutdownTimeoutSeconds = 1,
            StoppedSettleSeconds = 0
        };
        Directory.CreateDirectory(Settings.SaveDirectory);
        File.WriteAllText(Path.Combine(Settings.SaveDirectory, "world.db"), "synthetic world only");
        Backups = new(Platform);
        Controller = NewController(emergency);
    }
    public HostController NewController(bool emergency = false) => new(Platform, Backups, Log, Guard, () => Settings, () => emergency, new FastClock());
    public void Dispose() => Directory.Delete(Root, true);
}
