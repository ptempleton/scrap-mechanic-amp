using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScrapMechanic.Host.Core;

public sealed record BackupFile(string Path, long Length, string Sha256);
public sealed record BackupManifest(int Format, string SourceDirectory, DateTimeOffset CreatedUtc, List<BackupFile> Files);

public sealed class BackupStore(string userDataRoot, IEventSink log) : IBackupStore
{
    private const string ManifestEntry = "_scrapmechanic/manifest.json";
    private static readonly Regex OwnedName = new(@"^sm-\d{8}T\d{9}Z-[0-9a-f]{32}\.zip$", RegexOptions.CultureInvariant);

    public static string DiscoverSaveDirectory(string userDataRoot)
    {
        EnsureNoLinks(userDataRoot);
        if (!Directory.Exists(userDataRoot)) throw new DirectoryNotFoundException("No game user-data directory. Set SaveDirectory explicitly.");
        var candidates = Directory.GetDirectories(userDataRoot, "User_*")
            .Select(p => Path.Combine(p, "Save", "Survival")).Where(Directory.Exists).ToArray();
        if (candidates.Length != 1)
            throw new InvalidDataException($"Found {candidates.Length} Survival directories. Set SaveDirectory explicitly; newest-folder guessing is disabled.");
        EnsureNoLinks(candidates[0]);
        return Path.GetFullPath(candidates[0]);
    }

    public string Create(GameSettings settings, Action assertStopped, bool postCrash = false)
    {
        settings.Validate();
        assertStopped();
        var source = Path.GetFullPath(settings.SaveDirectory.Length == 0 ? DiscoverSaveDirectory(userDataRoot) : settings.SaveDirectory);
        var destination = Path.GetFullPath(settings.BackupDirectory);
        EnsureNoLinks(source);
        EnsureNoLinks(destination);
        if (Contains(source, destination) || Contains(destination, source))
            throw new InvalidDataException("Save and backup directories must not contain one another.");
        // Crash archives may contain inconsistent saves. Their retention must never prune normal backups.
        if (postCrash) destination = Path.Combine(destination, "crashes");
        var inventory = Inventory(source);
        if (inventory.Count == 0) throw new InvalidDataException("Refusing an empty save backup. Confirm the save directory.");
        var sourceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant())))[..16].ToLowerInvariant();
        var folder = Path.Combine(destination, sourceId);
        EnsureNoLinks(folder);
        Directory.CreateDirectory(folder);
        var created = DateTimeOffset.UtcNow;
        var final = Path.Combine(folder, $"sm-{created:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.zip");
        var partial = final + ".partial";
        try
        {
            var manifest = new BackupManifest(1, source, created, []);
            using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    foreach (var file in inventory)
                    {
                        assertStopped();
                        EnsureNoLinks(Path.Combine(source, file.Relative));
                        using var input = new FileStream(Path.Combine(source, file.Relative), FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var output = zip.CreateEntry("world/" + file.Relative, CompressionLevel.Optimal).Open();
                        var (length, hash) = CopyAndHash(input, output, assertStopped);
                        manifest.Files.Add(new(file.Relative, length, hash));
                    }
                    using var manifestStream = zip.CreateEntry(ManifestEntry).Open();
                    JsonSerializer.Serialize(manifestStream, manifest, ConfigJson.Options);
                }
                stream.Flush(true);
            }
            if (!inventory.SequenceEqual(Inventory(source)))
                throw new IOException("Save files changed during backup; archive was discarded.");
            assertStopped();
            Verify(partial);
            assertStopped();
            File.Move(partial, final, false);
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
        // Retention runs only after a verified archive has been published.
        try { Prune(folder, source, settings.BackupRetention, final); }
        catch (Exception ex) { log.Write("retention_failed", "Backup succeeded, but retention failed: " + ex.Message); }
        return final;
    }

    public static BackupManifest Verify(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
            throw new InvalidDataException("Archive contains duplicate entries.");
        var metadata = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("Missing backup manifest.");
        if (metadata.Length > 16 * 1024 * 1024) throw new InvalidDataException("Backup manifest is too large.");
        using var input = metadata.Open();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(input, ConfigJson.Options) ?? throw new InvalidDataException("Invalid manifest.");
        if (manifest.Format != 1 || manifest.Files.Count == 0 || string.IsNullOrWhiteSpace(manifest.SourceDirectory))
            throw new InvalidDataException("Unsupported or empty backup.");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestEntry };
        foreach (var file in manifest.Files)
        {
            if (!SafeRelative(file.Path) || !expected.Add("world/" + file.Path)) throw new InvalidDataException("Unsafe or duplicate backup path.");
            var entry = zip.GetEntry("world/" + file.Path) ?? throw new InvalidDataException("Backup entry is missing: " + file.Path);
            using var content = entry.Open();
            var (length, hash) = CopyAndHash(content, Stream.Null, () => { });
            if (length != file.Length || !string.Equals(hash, file.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Backup hash mismatch: " + file.Path);
        }
        if (zip.Entries.Any(e => !expected.Contains(e.FullName))) throw new InvalidDataException("Archive has unlisted entries.");
        return manifest;
    }

    private static void Prune(string folder, string source, int retention, string newest)
    {
        var owned = new List<string>();
        foreach (var path in Directory.GetFiles(folder, "sm-*.zip").Where(p => OwnedName.IsMatch(Path.GetFileName(p))))
        {
            EnsureNoLinks(path);
            try
            {
                // Corrupt or foreign archives are never removed by retention.
                var manifest = Verify(path);
                if (string.Equals(manifest.SourceDirectory, source, StringComparison.OrdinalIgnoreCase)) owned.Add(path);
            }
            catch (InvalidDataException) { }
        }
        var keep = owned.Where(p => p != newest).OrderDescending(StringComparer.Ordinal).Take(retention - 1).ToHashSet();
        keep.Add(newest); // Keep the new backup even if the machine clock moved backwards.
        foreach (var path in owned.Where(p => !keep.Contains(p))) File.Delete(path);
    }

    private sealed record FileStamp(string Relative, long Length, long Modified);
    private static List<FileStamp> Inventory(string root)
    {
        var files = new List<FileStamp>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            EnsureNoLinks(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                EnsureNoLinks(path);
                if (Directory.Exists(path)) pending.Push(path);
                else
                {
                    var info = new FileInfo(path);
                    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    if (!SafeRelative(relative)) throw new InvalidDataException("Unsafe save filename.");
                    files.Add(new(relative, info.Length, info.LastWriteTimeUtc.Ticks));
                }
            }
        }
        return files.OrderBy(f => f.Relative, StringComparer.Ordinal).ToList();
    }

    private static (long Length, string Hash) CopyAndHash(Stream input, Stream output, Action checkpoint)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072];
        long length = 0;
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            checkpoint();
            output.Write(buffer, 0, count);
            hash.AppendData(buffer, 0, count);
            length += count;
        }
        return (length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static bool SafeRelative(string path) => path.Length > 0 && !path.Contains('\\') && !path.Contains(':') &&
        !path.StartsWith('/') && path.Split('/').All(p => p is not ("" or "." or "..") &&
            !p.EndsWith(' ') && !p.EndsWith('.') && !p.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c)) &&
            !Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    private static bool Contains(string parent, string child) => string.Equals(parent.TrimEnd(Path.DirectorySeparatorChar), child.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        || child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void EnsureNoLinks(string path)
    {
        var full = Path.GetFullPath(path);
        // macOS /tmp is a system symlink; tests resolve it before calling here.
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symlinks/junctions are not allowed for save or backup paths: " + current);
    }
}
