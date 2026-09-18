using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace WardogsNavigator.Services;

public sealed class GameWindowCapture
{
    private readonly object _sync = new();
    private IntPtr _cachedWindow;
    private string _cachedTitle = "";
    private DateTime _cachedAtUtc;

    public string LastBackendUsed { get; private set; } = "—";
    public double LastCaptureMilliseconds { get; private set; }

    public IntPtr FindWindow(string titleContains)
    {
        if (string.IsNullOrWhiteSpace(titleContains))
            return IntPtr.Zero;

        lock (_sync)
        {
            if (_cachedWindow != IntPtr.Zero &&
                IsWindow(_cachedWindow) &&
                _cachedTitle.Equals(titleContains, StringComparison.OrdinalIgnoreCase) &&
                DateTime.UtcNow - _cachedAtUtc < TimeSpan.FromSeconds(2))
            {
                return _cachedWindow;
            }
        }

        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var len = GetWindowTextLength(h);
            if (len <= 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.ToString().Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        lock (_sync)
        {
            _cachedWindow = found;
            _cachedTitle = titleContains;
            _cachedAtUtc = DateTime.UtcNow;
        }

        return found;
    }

    public Rectangle? GetClientScreenRect(string titleContains)
    {
        var hwnd = FindWindow(titleContains);
        if (hwnd == IntPtr.Zero) return null;
        if (!GetClientRect(hwnd, out var rc)) return null;
        var p = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref p)) return null;
        return new Rectangle(
            p.X,
            p.Y,
            Math.Max(1, rc.Right - rc.Left),
            Math.Max(1, rc.Bottom - rc.Top));
    }

    public Bitmap Capture(
        string titleContains,
        NormalizedRegion region,
        CaptureBackendMode backend = CaptureBackendMode.Auto)
    {
        if (!region.IsValid)
            throw new InvalidOperationException("尚未校准识别区域。");

        var started = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var hwnd = FindWindow(titleContains);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException(
                    "未找到采集窗口。请检查游戏/OBS窗口标题。");

            if (!GetClientRect(hwnd, out var rc))
                throw new InvalidOperationException("无法读取采集窗口客户区。");

            var clientSize = new Size(
                Math.Max(1, rc.Right - rc.Left),
                Math.Max(1, rc.Bottom - rc.Top));

            var crop = NormalizedToPixel(region, clientSize);

            if (backend == CaptureBackendMode.PrintWindow)
            {
                var printed = TryCaptureClientPrintWindow(hwnd, clientSize);
                if (printed != null)
                {
                    LastBackendUsed = "PrintWindow";
                    using (printed)
                        return Crop(printed, crop);
                }

                throw new InvalidOperationException(
                    "PrintWindow 采集失败。可切换到“自动”或“屏幕拷贝”。");
            }

            if (backend == CaptureBackendMode.ScreenCopy)
            {
                LastBackendUsed = "ScreenCopy";
                return CaptureFromScreen(hwnd, clientSize, crop);
            }

            // Auto: prefer screen copy for games because it has the lowest
            // latency. If the window is off-screen/covered or CopyFromScreen
            // fails, try PrintWindow (useful for OBS projector/preview windows).
            try
            {
                var screen = CaptureFromScreen(hwnd, clientSize, crop);
                LastBackendUsed = "ScreenCopy";
                return screen;
            }
            catch
            {
                var printed = TryCaptureClientPrintWindow(hwnd, clientSize);
                if (printed != null)
                {
                    LastBackendUsed = "PrintWindow";
                    using (printed)
                        return Crop(printed, crop);
                }

                throw;
            }
        }
        finally
        {
            started.Stop();
            LastCaptureMilliseconds = started.Elapsed.TotalMilliseconds;
        }
    }

    public Bitmap CaptureClient(
        string titleContains,
        CaptureBackendMode backend = CaptureBackendMode.Auto)
    {
        return Capture(
            titleContains,
            new NormalizedRegion
            {
                X = 0,
                Y = 0,
                Width = 1,
                Height = 1
            },
            backend);
    }

    public static NormalizedRegion ToNormalized(
        Rectangle selectedScreenRect,
        Rectangle clientRect)
    {
        var clipped = Rectangle.Intersect(selectedScreenRect, clientRect);
        if (clipped.Width <= 0 || clipped.Height <= 0)
            return new NormalizedRegion();

        return new NormalizedRegion
        {
            X = (clipped.Left - clientRect.Left) / (double)clientRect.Width,
            Y = (clipped.Top - clientRect.Top) / (double)clientRect.Height,
            Width = clipped.Width / (double)clientRect.Width,
            Height = clipped.Height / (double)clientRect.Height
        };
    }

    public void InvalidateWindowCache()
    {
        lock (_sync)
        {
            _cachedWindow = IntPtr.Zero;
            _cachedTitle = "";
            _cachedAtUtc = default;
        }
    }

    private static Rectangle NormalizedToPixel(
        NormalizedRegion region,
        Size clientSize)
    {
        var x = (int)Math.Round(region.X * clientSize.Width);
        var y = (int)Math.Round(region.Y * clientSize.Height);
        var w = Math.Max(2, (int)Math.Round(region.Width * clientSize.Width));
        var h = Math.Max(2, (int)Math.Round(region.Height * clientSize.Height));

        var crop = Rectangle.Intersect(
            new Rectangle(Point.Empty, clientSize),
            new Rectangle(x, y, w, h));

        if (crop.Width < 2 || crop.Height < 2)
            throw new InvalidOperationException("识别区域超出采集窗口。");

        return crop;
    }

    private static Bitmap CaptureFromScreen(
        IntPtr hwnd,
        Size clientSize,
        Rectangle crop)
    {
        var p = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref p))
            throw new InvalidOperationException("无法定位采集窗口。");

        var box = new Rectangle(
            p.X + crop.X,
            p.Y + crop.Y,
            crop.Width,
            crop.Height);

        var bmp = new Bitmap(
            box.Width,
            box.Height,
            PixelFormat.Format24bppRgb);

        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(
            box.Location,
            Point.Empty,
            box.Size,
            CopyPixelOperation.SourceCopy);

        return bmp;
    }

    private static Bitmap? TryCaptureClientPrintWindow(
        IntPtr hwnd,
        Size size)
    {
        try
        {
            var bmp = new Bitmap(
                size.Width,
                size.Height,
                PixelFormat.Format24bppRgb);

            using var g = Graphics.FromImage(bmp);
            var hdc = g.GetHdc();

            try
            {
                if (!PrintWindow(hwnd, hdc, PW_CLIENTONLY))
                {
                    bmp.Dispose();
                    return null;
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }

            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap Crop(Bitmap source, Rectangle crop)
    {
        var output = new Bitmap(
            crop.Width,
            crop.Height,
            PixelFormat.Format24bppRgb);

        using var g = Graphics.FromImage(output);
        g.DrawImage(
            source,
            new Rectangle(0, 0, output.Width, output.Height),
            crop,
            GraphicsUnit.Pixel);

        return output;
    }

    private const uint PW_CLIENTONLY = 0x00000001;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
}
