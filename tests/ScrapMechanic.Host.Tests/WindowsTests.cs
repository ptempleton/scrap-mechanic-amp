using System.IO.Pipes;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Principal;
using ScrapMechanic.Host;
using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host.Tests;

[SupportedOSPlatform("windows")]
internal static class WindowsTests
{
    public static async Task<int> Run()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            var name = "ScrapMechanicTest-" + Guid.NewGuid().ToString("N");
            using var server = PipeTransport.CreateServer(name, PipeTransport.Security(sid), true);
            var serve = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                var command = await Wire.ReadAsync<Request>(server, default);
                if (command.Command != "status") throw new Exception("Bad command");
                await Wire.WriteAsync(server, new Reply(true, "synthetic pipe test"), default);
            });
            var response = await PipeTransport.SendAsync(new() { PipeName = name, AgentIdentitySid = sid }, "status");
            await serve;
            if (!response.Ok) throw new Exception("Pipe response failed");
            Console.WriteLine("PASS Windows same-user pipe ACL and protocol round trip");
            server.Disconnect();
            var wrongOwnerClient = PipeTransport.SendAsync(new() { PipeName = name, AgentIdentitySid = sid == "S-1-5-18" ? "S-1-5-19" : "S-1-5-18" }, "status");
            await server.WaitForConnectionAsync();
            try { await wrongOwnerClient; throw new Exception("Wrong server owner accepted"); }
            catch (UnauthorizedAccessException) { Console.WriteLine("PASS Windows wrong-owner pipe rejected"); }
            var deniedBroadIdentity = false;
            try { PipeTransport.Security("S-1-1-0"); }
            catch (InvalidDataException) { deniedBroadIdentity = true; }
            if (!deniedBroadIdentity) throw new Exception("Broad SID accepted");
            Console.WriteLine("PASS Windows broad pipe access rejected");
            if (Process.GetCurrentProcess().SessionId != 0 && Environment.UserInteractive)
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add("--window-fixture");
                using var fixture = Process.Start(start)!;
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!fixture.HasExited && fixture.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
                { await Task.Delay(50); fixture.Refresh(); }
                if (fixture.HasExited || fixture.MainWindowHandle == IntPtr.Zero) throw new Exception("Window fixture failed to appear");
                var identity = new GameProcess(fixture.Id, fixture.StartTime.ToUniversalTime().Ticks, fixture.MainModule!.FileName, fixture.SessionId);
                var platform = new WindowsGamePlatform();
                if (!platform.IsAlive(identity) || platform.IsAlive(identity with { StartTimeUtcTicks = 1 })) throw new Exception("Process identity check failed");
                if (!platform.RequestClose(identity)) throw new Exception("WM_CLOSE not posted");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await fixture.WaitForExitAsync(timeout.Token);
                if (fixture.ExitCode != 0 || platform.IsAlive(identity)) throw new Exception("Fixture did not exit cleanly");
                Console.WriteLine("PASS Windows synthetic WM_CLOSE, process identity and exit detection");
            }
            else Console.WriteLine("SKIP synthetic WM_CLOSE: no interactive Windows session.");
            Console.WriteLine("SKIP Windows cross-account ACL and real game: use TESTING.md live plan.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL Windows pipe: " + ex); return 1; }
    }
}
