using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ScrapMechanic.Host.Tests;

// A disposable native window, never a game process and never a game save writer.
[SupportedOSPlatform("windows")]
internal static class WindowFixture
{
    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    private static readonly WindowProcedure Procedure = OnMessage;
    public static int Run()
    {
        var name = "ScrapMechanicHostTest-" + Guid.NewGuid().ToString("N");
        var type = new WindowClass { Procedure = Procedure, Instance = GetModuleHandle(null), ClassName = name };
        if (RegisterClass(ref type) == 0) throw new Win32Exception();
        var window = CreateWindowEx(0, name, "Synthetic WM_CLOSE test (no game)", 0x10CF0000, 0, 0, 320, 120, IntPtr.Zero, IntPtr.Zero, type.Instance, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new Win32Exception();
        _ = SetTimer(window, 1, 20000, IntPtr.Zero); // Self-cleanup if the parent test fails.
        int result;
        while ((result = GetMessage(out var message, IntPtr.Zero, 0, 0)) > 0)
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
        return result < 0 ? 1 : 0;
    }
    private static IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message is 0x0010 or 0x0113) { _ = DestroyWindow(hwnd); return IntPtr.Zero; }
        if (message == 0x0002) { PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WindowProcedure Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")] private static extern nuint SetTimer(IntPtr hwnd, nuint id, uint milliseconds, IntPtr callback);
}
