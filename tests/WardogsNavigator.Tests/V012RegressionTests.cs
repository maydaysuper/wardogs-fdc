using WardogsNavigator.Services;
using Xunit;

namespace WardogsNavigator.Tests;

public sealed class V012RegressionTests
{
    [Fact]
    public void AutoCapture_PrefersNativeGameWindow()
    {
        var order =
            CaptureBackendPolicy.GetAttemptOrder(
                CaptureBackendMode.Auto);

        Assert.Equal(
            CaptureBackendMode.NativeWindow,
            order[0]);

        Assert.Contains(
            CaptureBackendMode.ScreenCopy,
            order);

        Assert.Contains(
            CaptureBackendMode.PrintWindow,
            order);
    }

    [Fact]
    public void NativeCapture_CanBeForcedWithoutObsFallback()
    {
        var order =
            CaptureBackendPolicy.GetAttemptOrder(
                CaptureBackendMode.NativeWindow);

        Assert.Single(order);
        Assert.Equal(
            CaptureBackendMode.NativeWindow,
            order[0]);
    }

    [Fact]
    public void LegacySettings_RemainLoadableWithAutoCapture()
    {
        const string json = """
        {
          "GameWindowTitleContains": "WARDOGS",
          "CaptureWindowTitleContains": "OBS projector",
          "CaptureBackend": 0
        }
        """;

        var settings =
            AppSettings.FromJson(json);

        Assert.Equal(
            CaptureBackendMode.Auto,
            settings.CaptureBackend);

        Assert.Equal(
            "WARDOGS",
            settings.GameWindowTitleContains);

        // The legacy value is preserved only for settings compatibility.
        // MainForm no longer uses it as the visual/OCR capture source.
        Assert.Equal(
            "OBS projector",
            settings.CaptureWindowTitleContains);
    }
}
