using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using ScrapMechanic.Host;
using ScrapMechanic.Host.Core;
using ScrapMechanic.Host.Tests;

if (args.Length == 1 && args[0] == "--window-fixture")
{
    if (!OperatingSystem.IsWindows()) return 1;
    return WindowFixture.Run();
}

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Task> run) => tests.Add((name, run));
void Sync(string name, Action run) => Test(name, () => { run(); return Task.CompletedTask; });
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

Test("start detects process, remains readiness-unverified and is idempotent", async () =>
{
    using var f = new Fixture();
    var result = await f.Controller.ExecuteAsync("start");
    Check(result.Ok && result.Status is { State: "Running", MultiplayerReady: false, ProcessId: not null });
    Check((await f.Controller.ExecuteAsync("start")).Ok);
    Check(f.Platform.Calls.Count(c => c == "launch") == 1);
});
Test("parallel starts are serialized", async () =>
{
    using var f = new Fixture();
    var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Controller.ExecuteAsync("start")));
    Check(replies.All(r => r.Ok));
    Check(f.Platform.Calls.Count(c => c == "launch") == 1);
});
Test("graceful stop closes once and backs up only after exit", async () =>
{
    using var f = new Fixture();
    await f.Controller.ExecuteAsync("start");
    var result = await f.Controller.ExecuteAsync("stop");
    Check(result.Ok && result.Status?.ProcessId is null && result.Backup is not null);
    Check(f.Platform.Calls.SequenceEqual(new[] { "launch", "close", "backup" }));
});
Test("restart orders close, backup, launch", async () =>
{
    using var f = new Fixture();
    await f.Controller.ExecuteAsync("start");
    Check((await f.Controller.ExecuteAsync("restart")).Ok);
    Check(f.Platform.Calls.SequenceEqual(new[] { "launch", "close", "backup", "launch" }));
});
Test("stop timeout never kills, backs up or restarts", async () =>
{
    using var f = new Fixture();
    f.Platform.CloseExits = false;
    await f.Controller.ExecuteAsync("start");
    Check(!(await f.Controller.ExecuteAsync("restart")).Ok);
    Check((await f.Controller.ExecuteAsync("status")).Status?.State == "StopFailed");
    Check(!(await f.Controller.ExecuteAsync("start")).Ok);
    Check(f.Platform.Calls.SequenceEqual(new[] { "launch", "close" }));
});
Test("missing window fails closed", async () =>
{
    using var f = new Fixture();
    f.Platform.HasWindow = false;
    await f.Controller.ExecuteAsync("start");
    Check(!(await f.Controller.ExecuteAsync("stop")).Ok);
    Check(f.Platform.Games.Count == 1 && !f.Platform.Calls.Contains("kill"));
});
Test("failed requested backup blocks restart across controller recreation", async () =>
{
    using var f = new Fixture();
    await f.Controller.ExecuteAsync("start");
    f.Backups.Throws = true;
    Check(!(await f.Controller.ExecuteAsync("restart")).Ok);
    Check(!(await f.NewController().ExecuteAsync("start")).Ok);
    f.Backups.Throws = false;
    Check((await f.Controller.ExecuteAsync("backup")).Ok);
    Check((await f.NewController().ExecuteAsync("start")).Ok);
});
Test("optional backup can be disabled", async () =>
{
    using var f = new Fixture();
    f.Settings = f.Settings with { BackupOnStop = false };
    await f.Controller.ExecuteAsync("start");
    Check((await f.Controller.ExecuteAsync("restart")).Ok);
    Check(!f.Platform.Calls.Contains("backup"));
});
Test("live backup refused", async () =>
{
    using var f = new Fixture();
    await f.Controller.ExecuteAsync("start");
    Check(!(await f.Controller.ExecuteAsync("backup")).Ok);
    Check(!f.Platform.Calls.Contains("backup"));
});
Test("launch exception is logged and unresolved launch survives restart", async () =>
{
    using var f = new Fixture();
    f.Platform.LaunchThrows = true;
    Check(!(await f.Controller.ExecuteAsync("start")).Ok && f.Log.Names.Contains("operation_failed"));
    Check(!(await f.NewController().ExecuteAsync("start")).Ok);
    Check(!(await f.Controller.ExecuteAsync("backup")).Ok);
});
Test("startup timeout does not retry; explicit acknowledgement clears interlock", async () =>
{
    using var f = new Fixture();
    f.Platform.LaunchAppears = false;
    Check(!(await f.Controller.ExecuteAsync("start")).Ok);
    Check(!(await f.Controller.ExecuteAsync("start")).Ok);
    Check(f.Platform.Calls.Count == 1);
    Check(!(await f.Controller.ExecuteAsync("acknowledge-launch")).Ok);
    Check((await f.Controller.ExecuteAsync("acknowledge-launch CONFIRM")).Ok);
    f.Platform.LaunchAppears = true;
    Check((await f.Controller.ExecuteAsync("start")).Ok);
});
Test("late Steam process can be observed without a second launch", async () =>
{
    using var f = new Fixture();
    f.Platform.LaunchAppears = false;
    await f.Controller.ExecuteAsync("start");
    f.Platform.Games.Add(new(300, 300, f.Settings.GameExecutable, 1));
    Check((await f.Controller.ExecuteAsync("start")).Ok && !f.Guard.Pending);
    Check(f.Platform.Calls.Count == 1);
});
foreach (var conflict in new[] { "multiple", "session", "path", "inaccessible" })
    Test("refuse conflicting process: " + conflict, async () =>
    {
        using var f = new Fixture();
        f.Platform.Games.Add(new(3, 3, conflict == "path" ? Path.Combine(f.Root, "other.exe") : f.Settings.GameExecutable, conflict == "session" ? 2 : 1));
        if (conflict == "multiple") f.Platform.Games.Add(new(4, 4, f.Settings.GameExecutable, 1));
        if (conflict == "inaccessible") f.Platform.InspectionThrows = true;
        Check(!(await f.Controller.ExecuteAsync("start")).Ok);
        Check(f.Platform.Calls.Count == 0);
    });
Test("unexpected exit logged with no automatic restart", async () =>
{
    using var f = new Fixture();
    await f.Controller.ExecuteAsync("start");
    f.Platform.Games.Clear();
    await f.Controller.PollAsync();
    Check(f.Log.Names.Contains("unexpected_exit"));
    Check((await f.Controller.ExecuteAsync("status")).Status?.State == "Faulted");
    Check(f.Platform.Calls.Count == 1);
});
Test("late exit after stop timeout still requires backup", async () =>
{
    using var f = new Fixture();
    f.Platform.CloseExits = false;
    await f.Controller.ExecuteAsync("start");
    await f.Controller.ExecuteAsync("stop");
    f.Platform.Games.Clear();
    await f.Controller.PollAsync();
    Check(f.Log.Names.Contains("late_exit"));
    Check(!(await f.Controller.ExecuteAsync("start")).Ok);
    Check((await f.Controller.ExecuteAsync("backup")).Ok);
});
Test("active settings remain attached to process across agent recreation", async () =>
{
    using var f = new Fixture();
    var original = f.Settings.SaveDirectory;
    await f.Controller.ExecuteAsync("start");
    f.Settings = f.Settings with { SaveDirectory = Path.Combine(f.Root, "other-save") };
    Check((await f.NewController().ExecuteAsync("stop")).Ok);
    Check(f.Backups.LastSettings?.SaveDirectory == original);
});
Test("emergency kill requires local enablement and explicit CONFIRM", async () =>
{
    using var f = new Fixture();
    await f.Controller.ExecuteAsync("start");
    Check(!(await f.Controller.ExecuteAsync("emergency-stop CONFIRM")).Ok);
    var enabled = f.NewController(true);
    Check(!(await enabled.ExecuteAsync("emergency-stop")).Ok);
    Check((await enabled.ExecuteAsync("emergency-stop CONFIRM")).Ok);
    Check(f.Platform.Calls.SequenceEqual(new[] { "launch", "kill" }));
    Check(!(await enabled.ExecuteAsync("start")).Ok);
});

Sync("configuration rejects unknown fields and invalid values", () =>
{
    using var f = new Fixture();
    Throws<JsonException>(() => JsonSerializer.Deserialize<GameSettings>("{\"InventedKey\":1}", ConfigJson.Options));
    Throws<InvalidDataException>(() => (f.Settings with { ShutdownTimeoutSeconds = 0 }).Validate());
    Throws<InvalidDataException>(() => (f.Settings with { BackupRetention = 0 }).Validate());
    Throws<InvalidDataException>(() => (f.Settings with { SaveDirectory = "relative" }).Validate());
    Throws<InvalidDataException>(() => (f.Settings with { Arguments = "x\ny" }).Validate());
    Throws<InvalidDataException>(() => (f.Settings with { LaunchMode = "Shell" }).Validate());
    var parsed = JsonSerializer.Deserialize<GameSettings>("{\"BackupOnStop\":\"False\",\"BackupRetention\":\"3\"}", ConfigJson.Options)!;
    Check(!parsed.BackupOnStop && parsed.BackupRetention == 3);
});
Sync("file state survives reload and malformed state fails closed", () =>
{
    using var f = new Fixture();
    var path = Path.Combine(f.Root, "state.json");
    var state = new FileLaunchGuard(path);
    state.MarkPending(); state.BlockStart("blocked"); state.ActiveSettings = f.Settings;
    var reloaded = new FileLaunchGuard(path);
    Check(reloaded.Pending && reloaded.StartBlockedReason == "blocked" && reloaded.ActiveSettings == f.Settings);
    reloaded.Clear(); reloaded.UnblockStart();
    Check(!new FileLaunchGuard(path).Pending);
    File.WriteAllText(path, "corrupt");
    Throws<JsonException>(() => new FileLaunchGuard(path));
});
Sync("backup round trip preserves nested files and sidecars", () =>
{
    using var f = new Fixture();
    Directory.CreateDirectory(Path.Combine(f.Settings.SaveDirectory, "nested"));
    File.WriteAllText(Path.Combine(f.Settings.SaveDirectory, "nested", "world.db-wal"), "sidecar");
    var path = new BackupStore(f.Root, f.Log).Create(f.Settings, () => { });
    var manifest = BackupStore.Verify(path);
    Check(manifest.Files.Count == 2);
    var restore = Path.Combine(f.Root, "restore");
    ZipFile.ExtractToDirectory(path, restore);
    Check(File.ReadAllText(Path.Combine(restore, "world", "world.db")) == "synthetic world only");
    Check(File.ReadAllText(Path.Combine(restore, "world", "nested", "world.db-wal")) == "sidecar");
});
Sync("backups never overwrite; retention leaves foreign and corrupt files", () =>
{
    using var f = new Fixture();
    var store = new BackupStore(f.Root, f.Log);
    var first = store.Create(f.Settings, () => { });
    var second = store.Create(f.Settings, () => { });
    Check(first != second && File.Exists(first) && File.Exists(second));
    var foreign = Path.Combine(Path.GetDirectoryName(first)!, "manual.zip");
    var corrupt = Path.Combine(Path.GetDirectoryName(first)!, "sm-20000101T000000000Z-00000000000000000000000000000000.zip");
    File.WriteAllText(foreign, "do not prune"); File.WriteAllText(corrupt, "corrupt");
    var final = store.Create(f.Settings with { BackupRetention = 1 }, () => { });
    Check(File.Exists(final) && !File.Exists(first) && !File.Exists(second) && File.Exists(foreign) && File.Exists(corrupt));
});
Sync("backup process-start race aborts partial and preserves prior backup", () =>
{
    using var f = new Fixture();
    var store = new BackupStore(f.Root, f.Log);
    var prior = store.Create(f.Settings, () => { });
    var checkpoints = 0;
    Throws<IOException>(() => store.Create(f.Settings with { BackupRetention = 1 }, () => { if (++checkpoints == 3) throw new IOException("Process appeared"); }));
    Check(File.Exists(prior));
    Check(Directory.GetFiles(f.Settings.BackupDirectory, "*.partial", SearchOption.AllDirectories).Length == 0);
});
Sync("changed save during backup is discarded", () =>
{
    using var f = new Fixture();
    var calls = 0;
    Throws<IOException>(() => new BackupStore(f.Root, f.Log).Create(f.Settings, () =>
    {
        if (++calls == 3) File.WriteAllText(Path.Combine(f.Settings.SaveDirectory, "new.db"), "changed");
    }));
    Check(Directory.GetFiles(f.Settings.BackupDirectory, "*.zip", SearchOption.AllDirectories).Length == 0);
});
Sync("nested backup and save paths rejected", () =>
{
    using var f = new Fixture();
    var store = new BackupStore(f.Root, f.Log);
    Throws<InvalidDataException>(() => store.Create(f.Settings with { BackupDirectory = Path.Combine(f.Settings.SaveDirectory, "backups") }, () => { }));
    Throws<InvalidDataException>(() => store.Create(f.Settings with { BackupDirectory = f.Root }, () => { }));
});
Sync("empty saves rejected", () =>
{
    using var f = new Fixture();
    File.Delete(Path.Combine(f.Settings.SaveDirectory, "world.db"));
    Throws<InvalidDataException>(() => new BackupStore(f.Root, f.Log).Create(f.Settings, () => { }));
});
Sync("save discovery rejects zero and multiple candidates", () =>
{
    using var f = new Fixture();
    Throws<InvalidDataException>(() => BackupStore.DiscoverSaveDirectory(f.Root));
    var one = Path.Combine(f.Root, "User_123", "Save", "Survival");
    Directory.CreateDirectory(one);
    Check(BackupStore.DiscoverSaveDirectory(f.Root) == one);
    Directory.CreateDirectory(Path.Combine(f.Root, "User_456", "Save", "Survival"));
    Throws<InvalidDataException>(() => BackupStore.DiscoverSaveDirectory(f.Root));
});
if (!OperatingSystem.IsWindows()) Sync("symlink saves rejected", () =>
{
    using var f = new Fixture();
    var link = Path.Combine(f.Root, "linked");
    Directory.CreateSymbolicLink(link, f.Settings.SaveDirectory);
    Throws<IOException>(() => new BackupStore(f.Root, f.Log).Create(f.Settings with { SaveDirectory = link }, () => { }));
});
Sync("tampered archive rejected", () =>
{
    using var f = new Fixture();
    var path = new BackupStore(f.Root, f.Log).Create(f.Settings, () => { });
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
    {
        zip.GetEntry("world/world.db")!.Delete();
        using var writer = new StreamWriter(zip.CreateEntry("world/world.db").Open());
        writer.Write("tampered");
    }
    Throws<InvalidDataException>(() => BackupStore.Verify(path));
});
Sync("unlisted traversal entry rejected", () =>
{
    using var f = new Fixture();
    var path = new BackupStore(f.Root, f.Log).Create(f.Settings, () => { });
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) zip.CreateEntry("../escape");
    Throws<InvalidDataException>(() => BackupStore.Verify(path));
});
Test("IPC round trip and oversize frame rejection", async () =>
{
    using var stream = new MemoryStream();
    await Wire.WriteAsync(stream, new Request("status"), default);
    stream.Position = 0;
    Check((await Wire.ReadAsync<Request>(stream, default)).Command == "status");
    using var oversized = new MemoryStream();
    var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, Wire.MaximumBytes + 1);
    oversized.Write(bytes); oversized.Position = 0;
    await ThrowsAsync<InvalidDataException>(async () => await Wire.ReadAsync<Request>(oversized, default));
});
Test("truncated IPC frame rejected", async () =>
{
    using var stream = new MemoryStream(new byte[] { 4, 0, 0, 0, 123 });
    await ThrowsAsync<EndOfStreamException>(async () => await Wire.ReadAsync<Request>(stream, default));
});
Test("bridge ready marker means process only; successful stop exits", async () =>
{
    var output = new StringWriter();
    var commands = new List<string>();
    var code = await Bridge.RunAsync(c => { commands.Add(c); return Task.FromResult(new Reply(true, "ok", new(c == "stop" ? "Stopped" : "Running", c == "stop" ? null : 12, 1))); }, new StringReader("status\nstop\n"), output);
    Check(code == 0 && commands.SequenceEqual(new[] { "start", "status", "stop" }));
    Check(output.ToString().Contains("SM_PROCESS_RUNNING multiplayer_readiness=unverified"));
});
Test("bridge failed stop does not exit successfully or issue kill", async () =>
{
    var calls = new List<string>();
    var code = await Bridge.RunAsync(c => { calls.Add(c); return Task.FromResult(new Reply(c != "stop", "result", new("Running", 1, 1))); }, new StringReader("stop\n"), new StringWriter());
    Check(code != 0 && calls.SequenceEqual(new[] { "start", "stop" }));
});
Test("bridge EOF leaves the game untouched", async () =>
{
    var calls = new List<string>();
    var code = await Bridge.RunAsync(c => { calls.Add(c); return Task.FromResult(new Reply(true, "ok", new("Running", 1, 1))); }, new StringReader(""), new StringWriter());
    Check(code == 3 && calls.SequenceEqual(new[] { "start" }));
});
Test("bridge detects unexpected exit without requesting a relaunch", async () =>
{
    var calls = new List<string>();
    var output = new StringWriter();
    var code = await Bridge.RunAsync(c =>
    {
        calls.Add(c);
        return Task.FromResult(new Reply(true, "observed", new(c == "start" ? "Running" : "Faulted", c == "start" ? 1 : null, 1)));
    }, new PendingReader(), output, TimeSpan.FromMilliseconds(1));
    Check(code == 5 && calls.SequenceEqual(new[] { "start", "status" }));
    Check(output.ToString().Contains("SM_PROCESS_EXITED"));
});
Test("bridge refuses readiness marker after failed startup", async () =>
{
    var output = new StringWriter();
    var code = await Bridge.RunAsync(_ => Task.FromResult(new Reply(false, "Steam unavailable")), new StringReader(""), output);
    Check(code == 2 && !output.ToString().Contains("SM_PROCESS_RUNNING"));
});
Sync("structured logger tolerates a failed disk write", () =>
{
    using var f = new Fixture();
    var folder = Path.Combine(f.Root, "logs");
    var log = new JsonLog(folder);
    Directory.Delete(folder);
    log.Write("test_event", "Synthetic missing log directory");
});

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
var portableFailures = failures;
if (OperatingSystem.IsWindows()) failures += await WindowsTests.Run();
else Console.WriteLine("SKIP Windows process/window/pipe ACL tests: this is not Windows.");
Console.WriteLine($"{tests.Count - portableFailures}/{tests.Count} portable tests passed.");
return failures == 0 ? 0 : 1;

sealed class PendingReader : TextReader
{
    private readonly TaskCompletionSource<string?> completion = new();
    public override Task<string?> ReadLineAsync() => completion.Task;
}
