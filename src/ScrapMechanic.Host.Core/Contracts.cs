namespace ScrapMechanic.Host.Core;

public sealed record GameProcess(int Id, long StartTimeUtcTicks, string Executable, int SessionId);

public interface IGamePlatform
{
    int SessionId { get; }
    IReadOnlyList<GameProcess> FindGames(); // All sessions; inaccessible candidates must fail closed.
    void Launch(GameSettings settings);
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
    void MarkPending();
    void Clear();
    void BlockStart(string reason);
    void UnblockStart();
}

public interface IBackupStore
{
    string Create(GameSettings settings, Action assertStopped);
}

public sealed record HostStatus(string State, int? ProcessId, int SessionId, bool MultiplayerReady = false,
    string? Detail = null);
public sealed record Reply(bool Ok, string Message, HostStatus? Status = null, string? Backup = null);

public sealed class HostOperationException(string message) : Exception(message);
