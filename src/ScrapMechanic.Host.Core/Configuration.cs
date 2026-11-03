using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrapMechanic.Host.Core;

public sealed record GameSettings
{
    public string SteamDirectory { get; init; } = @"C:\Program Files (x86)\Steam";
    public string GameExecutable { get; init; } = @"C:\Program Files (x86)\Steam\steamapps\common\Scrap Mechanic\Release\ScrapMechanic.exe";
    public string LaunchMode { get; init; } = "Steam";
    public string Arguments { get; init; } = "-dedicated_server -last_save";
    public int StartupTimeoutSeconds { get; init; } = 120;
    public int StartupStableSeconds { get; init; } = 3;
    public int ShutdownTimeoutSeconds { get; init; } = 120;
    public int StoppedSettleSeconds { get; init; } = 3;
    public string SaveDirectory { get; init; } = "";
    public string BackupDirectory { get; init; } = @"C:\ScrapMechanicHost\Backups";
    public int BackupRetention { get; init; } = 10;
    public bool BackupOnStop { get; init; } = true;

    public void Validate()
    {
        foreach (var path in new[] { SteamDirectory, GameExecutable, BackupDirectory })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new InvalidDataException("Steam, game and backup paths must be absolute.");
        if (SaveDirectory.Length > 0 && !Path.IsPathFullyQualified(SaveDirectory))
            throw new InvalidDataException("SaveDirectory must be absolute or empty for discovery.");
        if (LaunchMode is not ("Steam" or "Executable" or "SteamUri"))
            throw new InvalidDataException("LaunchMode must be Steam, Executable or SteamUri.");
        if (Arguments.Length > 4096 || Arguments.Any(char.IsControl))
            throw new InvalidDataException("Arguments contain control characters or exceed 4096 characters.");
        Range(StartupTimeoutSeconds, 5, 600, nameof(StartupTimeoutSeconds));
        Range(StartupStableSeconds, 1, 60, nameof(StartupStableSeconds));
        if (StartupStableSeconds >= StartupTimeoutSeconds)
            throw new InvalidDataException("StartupStableSeconds must be less than StartupTimeoutSeconds.");
        Range(ShutdownTimeoutSeconds, 1, 600, nameof(ShutdownTimeoutSeconds));
        Range(StoppedSettleSeconds, 0, 60, nameof(StoppedSettleSeconds));
        Range(BackupRetention, 1, 10000, nameof(BackupRetention));
    }

    private static void Range(int value, int min, int max, string name)
    {
        if (value < min || value > max) throw new InvalidDataException($"{name} must be {min}..{max}.");
    }
}

public static class ConfigJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new FlexibleBooleanConverter() },
        WriteIndented = false
    };

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Empty configuration: {path}");
}

public sealed class FlexibleBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
            _ => throw new JsonException("Expected a boolean or true/false string.")
        };
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
