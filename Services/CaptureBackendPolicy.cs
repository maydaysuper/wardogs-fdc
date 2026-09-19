namespace WardogsNavigator.Services;

public static class CaptureBackendPolicy
{
    private static readonly CaptureBackendMode[] AutoOrder =
    {
        CaptureBackendMode.NativeWindow,
        CaptureBackendMode.ScreenCopy,
        CaptureBackendMode.PrintWindow
    };

    private static readonly CaptureBackendMode[] NativeWindowOnly =
    {
        CaptureBackendMode.NativeWindow
    };

    private static readonly CaptureBackendMode[] ScreenCopyOnly =
    {
        CaptureBackendMode.ScreenCopy
    };

    private static readonly CaptureBackendMode[] PrintWindowOnly =
    {
        CaptureBackendMode.PrintWindow
    };

    public static IReadOnlyList<CaptureBackendMode> GetAttemptOrder(
        CaptureBackendMode requested) =>
        requested switch
        {
            CaptureBackendMode.NativeWindow => NativeWindowOnly,
            CaptureBackendMode.ScreenCopy => ScreenCopyOnly,
            CaptureBackendMode.PrintWindow => PrintWindowOnly,
            _ => AutoOrder
        };

    public static bool ShouldAcceptScreenCopy(
        CaptureBackendMode requested,
        bool captureSucceeded,
        bool probablyBlank)
    {
        if (!captureSucceeded)
            return false;

        return requested == CaptureBackendMode.ScreenCopy ||
               (requested == CaptureBackendMode.Auto && !probablyBlank);
    }

    public static bool ShouldFallbackToPrintWindow(
        CaptureBackendMode requested,
        bool screenCopySucceeded,
        bool screenCopyProbablyBlank) =>
        requested == CaptureBackendMode.Auto &&
        (!screenCopySucceeded || screenCopyProbablyBlank);
}
