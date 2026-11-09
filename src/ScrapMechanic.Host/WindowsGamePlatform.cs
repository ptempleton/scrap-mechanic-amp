using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host;

[SupportedOSPlatform("windows")]
public sealed class WindowsGamePlatform : IGamePlatform
{
    public int SessionId { get; } = Process.GetCurrentProcess().SessionId;

    public WindowsGamePlatform()
    {
        if (SessionId == 0 || !Environment.UserInteractive)
            throw new InvalidOperationException("Agent must run in a logged-in interactive desktop session, not as a Windows service.");
    }

    public IReadOnlyList<GameProcess> FindGames()
    {
        var result = new List<GameProcess>();
        foreach (var process in Process.GetProcessesByName("ScrapMechanic"))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited) result.Add(Identity(process));
                }
                catch (InvalidOperationException) { /* Exited while enumerating. */ }
                catch (Win32Exception ex)
                {
                    throw new HostOperationException($"Cannot inspect ScrapMechanic PID {process.Id}; refusing an unsafe operation: {ex.Message}");
                }
            }
        }
        return result;
    }

    public Action PrepareLaunch(GameSettings settings)
    {
        settings.Validate();
        if (!string.Equals(Path.GetFileName(settings.GameExecutable), "ScrapMechanic.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GameExecutable must name ScrapMechanic.exe.");
        if (!File.Exists(settings.GameExecutable)) throw new FileNotFoundException("Game executable does not exist.", settings.GameExecutable);
        var steamExe = Path.Combine(settings.SteamDirectory, "steam.exe");
        if (!File.Exists(steamExe)) throw new FileNotFoundException("Steam executable does not exist.", steamExe);
        if (!SteamInThisSession(steamExe))
            throw new HostOperationException("Start Steam and sign in in the agent's desktop session before launching the host.");
        if (FindGames().Count != 0) throw new HostOperationException("Scrap Mechanic is already running.");
        var start = BuildStartInfo(settings);
        return () =>
        {
            using var launched = Process.Start(start) ?? throw new InvalidOperationException("Process.Start returned no process.");
            // Steam's short-lived launcher PID is deliberately not used as the game identity.
        };
    }

    public static ProcessStartInfo BuildStartInfo(GameSettings settings)
    {
        var start = new ProcessStartInfo
        {
            FileName = settings.LaunchMode == "Executable" ? settings.GameExecutable : Path.Combine(settings.SteamDirectory, "steam.exe"),
            WorkingDirectory = settings.LaunchMode == "Executable" ? Path.GetDirectoryName(settings.GameExecutable)! : settings.SteamDirectory,
            UseShellExecute = false
        };
        switch (settings.LaunchMode)
        {
            case "Steam": start.Arguments = "-applaunch 387990 " + settings.Arguments; break;
            case "Executable": start.Arguments = settings.Arguments; break;
            case "SteamUri": start.ArgumentList.Add("steam://run/387990//" + Uri.EscapeDataString(settings.Arguments) + "/"); break;
            default: throw new InvalidDataException("Unsupported launch mode.");
        }
        return start;
    }

    private bool SteamInThisSession(string executable)
    {
        var found = false;
        foreach (var process in Process.GetProcessesByName("steam"))
        {
            using (process)
            {
                try { found |= process.SessionId == SessionId && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
        return found;
    }

    public bool IsAlive(GameProcess target)
    {
        using var process = OpenVerified(target);
        return process is not null;
    }

    public bool RequestClose(GameProcess target)
    {
        using var process = OpenVerified(target);
        if (process is null) return true;
        process.Refresh();
        var window = process.MainWindowHandle;
        if (window == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(window, out var owner);
        if (owner != target.Id) return false;
        if (!PostMessage(window, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WM_CLOSE failed");
        return true;
    }

    public void EmergencyKill(GameProcess target)
    {
        using var process = OpenVerified(target);
        process?.Kill(entireProcessTree: false);
    }

    private static Process? OpenVerified(GameProcess target)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(target.Id);
            _ = process.Handle; // Keep the kernel process object open during the operation.
            if (!process.HasExited && Identity(process) == target) return process;
            process.Dispose();
            return null;
        }
        catch (ArgumentException) { process?.Dispose(); return null; }
        catch (InvalidOperationException) { process?.Dispose(); return null; }
        catch { process?.Dispose(); throw; }
    }

    private static GameProcess Identity(Process process) => new(process.Id, process.StartTime.ToUniversalTime().Ticks,
        process.MainModule?.FileName ?? throw new Win32Exception("Cannot inspect executable path."), process.SessionId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
