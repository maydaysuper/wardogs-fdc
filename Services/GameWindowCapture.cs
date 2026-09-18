using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace WardogsNavigator.Services;

public sealed class GameWindowCapture
{
    public IntPtr FindWindow(string titleContains)
    {
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
        return found;
    }

    public Rectangle? GetClientScreenRect(string titleContains)
    {
        var hwnd = FindWindow(titleContains);
        if (hwnd == IntPtr.Zero) return null;
        if (!GetClientRect(hwnd, out var rc)) return null;
        var p = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref p)) return null;
        return new Rectangle(p.X, p.Y, Math.Max(1, rc.Right - rc.Left), Math.Max(1, rc.Bottom - rc.Top));
    }

    public Bitmap Capture(string titleContains, NormalizedRegion region)
    {
        var client = GetClientScreenRect(titleContains)
            ?? throw new InvalidOperationException("未找到游戏窗口。请使用无边框/窗口化并检查窗口标题。");
        if (!region.IsValid) throw new InvalidOperationException("尚未校准识别区域。");

        var x = client.Left + (int)Math.Round(region.X * client.Width);
        var y = client.Top + (int)Math.Round(region.Y * client.Height);
        var w = Math.Max(2, (int)Math.Round(region.Width * client.Width));
        var h = Math.Max(2, (int)Math.Round(region.Height * client.Height));
        var box = Rectangle.Intersect(client, new Rectangle(x, y, w, h));
        if (box.Width < 2 || box.Height < 2) throw new InvalidOperationException("识别区域超出游戏窗口。");

        var bmp = new Bitmap(box.Width, box.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(box.Location, Point.Empty, box.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static NormalizedRegion ToNormalized(Rectangle selectedScreenRect, Rectangle clientRect)
    {
        var clipped = Rectangle.Intersect(selectedScreenRect, clientRect);
        if (clipped.Width <= 0 || clipped.Height <= 0) return new NormalizedRegion();
        return new NormalizedRegion
        {
            X = (clipped.Left - clientRect.Left) / (double)clientRect.Width,
            Y = (clipped.Top - clientRect.Top) / (double)clientRect.Height,
            Width = clipped.Width / (double)clientRect.Width,
            Height = clipped.Height / (double)clientRect.Height
        };
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
}
