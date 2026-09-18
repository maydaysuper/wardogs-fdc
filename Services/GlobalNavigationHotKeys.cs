using System.Runtime.InteropServices;

namespace WardogsNavigator.Services;

public enum GlobalNavigationHotKeyAction
{
    CaptureCurrent,
    CaptureTarget,
    ToggleNavigation
}

/// <summary>
/// Registers non-invasive Windows global hotkeys so the user can trigger
/// screen-only navigation actions while the game remains foreground.
/// It does not synthesize keyboard/mouse input and does not hook the game.
/// </summary>
public sealed class GlobalNavigationHotKeys : IDisposable
{
    public const int MessageId = 0x0312; // WM_HOTKEY

    public const int CaptureCurrentId = 0x5711;
    public const int CaptureTargetId = 0x5712;
    public const int ToggleNavigationId = 0x5713;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;

    private const uint VkF8 = 0x77;
    private const uint VkF9 = 0x78;
    private const uint VkF10 = 0x79;

    private IntPtr _windowHandle;

    public bool CurrentRegistered { get; private set; }
    public bool TargetRegistered { get; private set; }
    public bool ToggleRegistered { get; private set; }

    public string Register(IntPtr windowHandle)
    {
        Unregister();

        if (windowHandle == IntPtr.Zero)
            return "全局快捷键：窗口句柄无效";

        _windowHandle = windowHandle;
        const uint modifiers =
            ModControl |
            ModAlt |
            ModNoRepeat;

        CurrentRegistered =
            RegisterHotKey(
                windowHandle,
                CaptureCurrentId,
                modifiers,
                VkF8);

        TargetRegistered =
            RegisterHotKey(
                windowHandle,
                CaptureTargetId,
                modifiers,
                VkF9);

        ToggleRegistered =
            RegisterHotKey(
                windowHandle,
                ToggleNavigationId,
                modifiers,
                VkF10);

        var registered =
            new List<string>();

        var failed =
            new List<string>();

        AddStatus(
            CurrentRegistered,
            "Ctrl+Alt+F8 当前",
            registered,
            failed);

        AddStatus(
            TargetRegistered,
            "Ctrl+Alt+F9 目的地",
            registered,
            failed);

        AddStatus(
            ToggleRegistered,
            "Ctrl+Alt+F10 导航",
            registered,
            failed);

        if (failed.Count == 0)
        {
            return
                "全局快捷键：" +
                string.Join(" · ", registered);
        }

        return
            "全局快捷键：" +
            (registered.Count == 0
                ? "注册失败"
                : string.Join(" · ", registered)) +
            "；占用/失败：" +
            string.Join("、", failed);
    }

    public void Unregister()
    {
        if (_windowHandle == IntPtr.Zero)
            return;

        if (CurrentRegistered)
        {
            UnregisterHotKey(
                _windowHandle,
                CaptureCurrentId);
        }

        if (TargetRegistered)
        {
            UnregisterHotKey(
                _windowHandle,
                CaptureTargetId);
        }

        if (ToggleRegistered)
        {
            UnregisterHotKey(
                _windowHandle,
                ToggleNavigationId);
        }

        CurrentRegistered = false;
        TargetRegistered = false;
        ToggleRegistered = false;
        _windowHandle = IntPtr.Zero;
    }

    public static bool TryGetAction(
        int hotKeyId,
        out GlobalNavigationHotKeyAction action)
    {
        switch (hotKeyId)
        {
            case CaptureCurrentId:
                action =
                    GlobalNavigationHotKeyAction.CaptureCurrent;
                return true;

            case CaptureTargetId:
                action =
                    GlobalNavigationHotKeyAction.CaptureTarget;
                return true;

            case ToggleNavigationId:
                action =
                    GlobalNavigationHotKeyAction.ToggleNavigation;
                return true;

            default:
                action = default;
                return false;
        }
    }

    public void Dispose()
    {
        Unregister();
        GC.SuppressFinalize(this);
    }

    private static void AddStatus(
        bool success,
        string label,
        ICollection<string> registered,
        ICollection<string> failed)
    {
        if (success)
            registered.Add(label);
        else
            failed.Add(label);
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(
        IntPtr hWnd,
        int id);
}
