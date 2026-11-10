namespace ScrapMechanic.Host.Core;

public sealed record GameProcess(int Id, long StartTimeUtcTicks, string Executable, int SessionId);

public interface IGamePlatform
{
    int SessionId { get; }
    IReadOnlyList<GameProcess> FindGames(); // All sessions; inaccessible candidates must fail closed.
    // Preparation must not issue a launch. Only the returned action may start
    // a process or hand a launch to Steam, including when that action throws.
    Action PrepareLaunch(GameSettings settings);
    bool IsAlive(GameProcess process);
    bool RequestClose(GameProcess process);
    void EmergencyKill(GameProcess process);
}

public interface IEventSink
{
    void Write(string eventName, string message, object? data = null);
}

public interface ILaunchGuard
{
    bool Pending { get; }
    string? StartBlockedReason { get; }
    GameSettings? ActiveSettings { get; set; }
    CrashRecoveryState Recovery { get; set; }
    void MarkPending();
    void Clear();
    void BlockStart(string reason);
    void UnblockStart();
}

public interface IBackupStore
{
    string Create(GameSettings settings, Action assertStopped, bool postCrash = false);
}

// Kept on disk so restarting the agent cannot reset the retry budget or lose a cancellation.
public sealed record CrashRecoveryState
{
    public DateTimeOffset[] Attempts { get; init; } = [];
    public DateTimeOffset? DueUtc { get; init; }
    public bool NeedsArchive { get; init; }
}

public sealed record HostStatus(string State, int? ProcessId, int SessionId, bool MultiplayerReady = false,
    string? Detail = null);
public sealed record Reply(bool Ok, string Message, HostStatus? Status = null, string? Backup = null);

public sealed class HostOperationException(string message) : Exception(message);
