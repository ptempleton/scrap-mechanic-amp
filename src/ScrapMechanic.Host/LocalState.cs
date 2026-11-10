using System.Text.Json;
using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host;

public sealed class JsonLog : IEventSink
{
    private readonly object gate = new();
    private readonly string directory;
    public JsonLog(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
    }

    public void Write(string eventName, string message, object? data = null)
    {
        var now = DateTimeOffset.UtcNow;
        var line = JsonSerializer.Serialize(new { time = now, @event = eventName, message, data });
        lock (gate)
        {
            // A full log disk or closed console must not interrupt a stop/backup transition.
            try { File.AppendAllText(Path.Combine(directory, $"host-{now:yyyyMMdd}.jsonl"), line + Environment.NewLine); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { Console.Error.WriteLine(JsonSerializer.Serialize(new { time = now, @event = "log_write_failed", error = ex.Message, original = line })); }
                catch (IOException) { }
            }
            try { Console.WriteLine(line); }
            catch (IOException) { }
        }
    }
}

// Writes are atomic within a volume. Malformed state fails closed; it is not reset.
public sealed class FileLaunchGuard : ILaunchGuard
{
    private sealed record State(bool Pending, string? StartBlockedReason, GameSettings? ActiveSettings,
        CrashRecoveryState? Recovery = null);
    private readonly string path;
    private State state;
    public FileLaunchGuard(string path)
    {
        this.path = path;
        state = File.Exists(path) ? ConfigJson.Read<State>(path) : new(false, null, null);
        if (state.Recovery is { Attempts: null } ||
            (state.Recovery?.DueUtc is not null && (state.ActiveSettings is null || !state.Recovery.NeedsArchive)))
            throw new InvalidDataException("Invalid persisted crash recovery state; inspect state.json.");
    }
    public bool Pending => state.Pending;
    public string? StartBlockedReason => state.StartBlockedReason;
    public CrashRecoveryState Recovery
    {
        get => state.Recovery ?? new();
        set => Save(state with { Recovery = value });
    }
    public GameSettings? ActiveSettings
    {
        get => state.ActiveSettings;
        set => Save(state with { ActiveSettings = value });
    }
    public void MarkPending() => Save(state with { Pending = true });
    public void Clear() => Save(state with { Pending = false });
    public void BlockStart(string reason) => Save(state with { StartBlockedReason = reason });
    public void UnblockStart() => Save(state with { StartBlockedReason = null });
    private void Save(State next)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, next, ConfigJson.Options);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
            state = next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record AgentConfiguration
{
    public string PipeName { get; init; } = "ScrapMechanicHost";
    public string AmpIdentitySid { get; init; } = "";
    public string SettingsFile { get; init; } = "";
    public string DataDirectory { get; init; } = "";
    public bool AllowEmergencyTermination { get; init; }
    public void Validate()
    {
        ClientConfiguration.ValidatePipeName(PipeName);
        if (!Path.IsPathFullyQualified(SettingsFile) || !Path.IsPathFullyQualified(DataDirectory))
            throw new InvalidDataException("Agent SettingsFile and DataDirectory must be absolute paths.");
        if (string.IsNullOrWhiteSpace(AmpIdentitySid)) throw new InvalidDataException("An explicit AMP Windows account SID is required.");
    }
}

public sealed record ClientConfiguration
{
    public string PipeName { get; init; } = "ScrapMechanicHost";
    public string AgentIdentitySid { get; init; } = "";
    public int RequestTimeoutSeconds { get; init; } = 1800;
    public static void ValidatePipeName(string name)
    {
        if (name.Length is < 1 or > 100 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidDataException("PipeName must contain 1..100 ASCII letters, digits, hyphens or underscores.");
    }
    public void Validate()
    {
        ValidatePipeName(PipeName);
        if (string.IsNullOrWhiteSpace(AgentIdentitySid)) throw new InvalidDataException("AgentIdentitySid is required to authenticate the pipe server.");
        if (RequestTimeoutSeconds is < 30 or > 86400) throw new InvalidDataException("RequestTimeoutSeconds must be 30..86400.");
    }
}
