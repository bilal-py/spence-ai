using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpenceAI.WebApi.Desktop;

public sealed record TrayActions(
    string Version,
    Action Open,
    Action OpenDataFolder,
    Func<bool> IsAutostart,
    Action<bool> SetAutostart,
    Func<(string Version, string Url)?> Update,
    Action<string> OpenUrl,
    Action Quit,
    Action<string> Log);

/// <summary>
/// The Windows tray icon, straight on Win32 (no WinForms/WPF, so the app stays one cross-platform project).
/// A hidden window on its own thread owns the icon and receives its clicks. Click opens Spence; right-click shows the menu.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTray
{
    const uint WM_APP_TRAY = 0x8001, WM_APP_BALLOON = 0x8002;
    const uint WM_CLOSE = 0x0010, WM_DESTROY = 0x0002, WM_COMMAND = 0x0111, WM_NULL = 0x0000, WM_ENDSESSION = 0x0016;
    const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B, NIN_BALLOONUSERCLICK = 0x0405;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20;
    const int CmdOpen = 1, CmdFolder = 2, CmdAutostart = 3, CmdUpdate = 4, CmdQuit = 5;

    readonly TrayActions actions;
    readonly WndProc proc; // Kept in a field so the GC never collects the callback Windows holds.
    readonly ManualResetEventSlim ready = new();
    readonly object gate = new();
    IntPtr hwnd, icon;
    uint taskbarCreated;
    (string Title, string Text, string? Url)? pendingBalloon;
    string? balloonUrl;

    WindowsTray(TrayActions actions)
    {
        this.actions = actions;
        proc = WindowProc;
    }

    public static WindowsTray Start(TrayActions actions)
    {
        var tray = new WindowsTray(actions);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                tray.Run();
            }
            catch (Exception ex)
            {
                failure = ex;
                tray.ready.Set();
            }
        }) { IsBackground = true, Name = "Spence tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        tray.ready.Wait(TimeSpan.FromSeconds(10));
        if (failure != null)
        {
            throw new InvalidOperationException("Tray icon failed", failure);
        }

        return tray;
    }

    public void ShowBalloon(string title, string text, string? url)
    {
        lock (gate)
        {
            pendingBalloon = (title, text, url);
        }

        PostMessageW(hwnd, WM_APP_BALLOON, IntPtr.Zero, IntPtr.Zero);
    }

    public void Close() => PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    public static void MessageBox(string title, string text) => MessageBoxW(IntPtr.Zero, text, title, 0x10 /* MB_ICONERROR */ | 0x40000 /* MB_TOPMOST */);

    void Run()
    {
        var hInstance = GetModuleHandleW(null);
        var cls = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = hInstance,
            lpszClassName = "SpenceTrayWindow",
        };
        if (RegisterClassExW(ref cls) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()})");
        }

        // A hidden top-level window (not message-only), so it hears "TaskbarCreated" when Explorer restarts.
        hwnd = CreateWindowExW(0, cls.lpszClassName, "Spence", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");
        }

        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        icon = LoadAppIcon(hInstance);
        AddIcon();
        ready.Set();

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    static IntPtr LoadAppIcon(IntPtr hInstance)
    {
        // The SDK embeds ApplicationIcon (spence.ico) in the exe as icon group 32512; load it at the tray's size.
        var cx = GetSystemMetrics(49 /* SM_CXSMICON */);
        var cy = GetSystemMetrics(50 /* SM_CYSMICON */);
        var h = LoadImageW(hInstance, 32512, 1 /* IMAGE_ICON */, cx, cy, 0);
        if (h != IntPtr.Zero)
        {
            return h;
        }

        try
        {
            if (Environment.ProcessPath is { } exe && ExtractIconExW(exe, 0, IntPtr.Zero, out var small, 1) > 0 && small != IntPtr.Zero)
            {
                return small;
            }
        }
        catch
        {
        }

        return LoadIconW(IntPtr.Zero, 32512 /* IDI_APPLICATION */);
    }

    NOTIFYICONDATAW IconData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = icon,
        szTip = $"Spence {actions.Version}",
        szInfo = "",
        szInfoTitle = "",
    };

    void AddIcon()
    {
        var data = IconData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        if (!Shell_NotifyIconW(NIM_ADD, ref data))
        {
            actions.Log("Windows didn't accept the tray icon (is Explorer running?). It's added again when the taskbar comes back.");
        }
    }

    IntPtr WindowProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_APP_TRAY)
            {
                switch ((uint)(lParam.ToInt64() & 0xFFFF))
                {
                    case WM_LBUTTONUP:
                        actions.Open();
                        break;
                    case WM_RBUTTONUP:
                    case WM_CONTEXTMENU:
                        ShowMenu();
                        break;
                    case NIN_BALLOONUSERCLICK:
                        if (balloonUrl != null)
                        {
                            actions.OpenUrl(balloonUrl);
                        }
                        else
                        {
                            actions.Open();
                        }

                        break;
                }

                return IntPtr.Zero;
            }

            if (msg == WM_APP_BALLOON)
            {
                (string Title, string Text, string? Url)? next;
                lock (gate)
                {
                    next = pendingBalloon;
                    pendingBalloon = null;
                }

                if (next is { } b)
                {
                    balloonUrl = b.Url;
                    var data = IconData(NIF_INFO);
                    data.szInfoTitle = b.Title;
                    data.szInfo = b.Text;
                    Shell_NotifyIconW(NIM_MODIFY, ref data);
                }

                return IntPtr.Zero;
            }

            if (msg == taskbarCreated && taskbarCreated != 0)
            {
                AddIcon(); // Explorer restarted and forgot our icon.
                return IntPtr.Zero;
            }

            switch (msg)
            {
                case WM_ENDSESSION when wParam != IntPtr.Zero:
                    actions.Quit(); // Windows is signing out or shutting down.
                    return IntPtr.Zero;
                case WM_CLOSE:
                    var data = IconData(0);
                    Shell_NotifyIconW(NIM_DELETE, ref data);
                    DestroyWindow(h);
                    return IntPtr.Zero;
                case WM_DESTROY:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch
        {
            // Never let an exception cross back into Windows.
        }

        return DefWindowProcW(h, msg, wParam, lParam);
    }

    void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MF_STRING, CmdOpen, "Open Spence");
            SetMenuDefaultItem(menu, CmdOpen, 0);
            var update = actions.Update();
            if (update is { } u)
            {
                AppendMenuW(menu, MF_STRING, CmdUpdate, $"Download update ({u.Version})…");
            }

            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, CmdFolder, "Open data folder");
            AppendMenuW(menu, MF_STRING | (actions.IsAutostart() ? MF_CHECKED : 0), CmdAutostart, "Start with Windows");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING | MF_GRAYED, 0, $"Spence {actions.Version}");
            AppendMenuW(menu, MF_STRING, CmdQuit, "Quit Spence");

            GetCursorPos(out var pt);
            SetForegroundWindow(hwnd); // Without this the menu doesn't close when you click elsewhere.
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.X, pt.Y, hwnd, IntPtr.Zero);
            PostMessageW(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (cmd)
            {
                case CmdOpen: actions.Open(); break;
                case CmdFolder: actions.OpenDataFolder(); break;
                case CmdAutostart: actions.SetAutostart(!actions.IsAutostart()); break;
                case CmdUpdate when update is { } up: actions.OpenUrl(up.Url); break;
                case CmdQuit: actions.Quit(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    // ---------- Win32 ----------

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEXW cls);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr menu, uint flags, nint id, string? text);
    [DllImport("user32.dll")] static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPos);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr LoadIconW(IntPtr instance, nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    [DllImport("user32.dll")] static extern IntPtr LoadImageW(IntPtr instance, nint name, uint type, int cx, int cy, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconExW(string file, int index, IntPtr large, out IntPtr small, uint count);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string? name);
}
