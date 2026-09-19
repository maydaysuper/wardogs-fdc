using System.Text.Json;

namespace WardogsNavigator;

public enum CaptureBackendMode
{
    Auto,
    NativeWindow,
    ScreenCopy,
    PrintWindow
}

public enum RuntimePerformanceMode
{
    LowPower,
    Balanced,
    Realtime
}

public sealed class NormalizedRegion
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public bool IsValid => Width > 0.002 && Height > 0.002;
}

public sealed class AppSettings
{
    public string GameWindowTitleContains { get; set; } = "WARDOGS";
    public string CaptureWindowTitleContains { get; set; } = "";
    public CaptureBackendMode CaptureBackend { get; set; } = CaptureBackendMode.Auto;
    public RuntimePerformanceMode PerformanceMode { get; set; } = RuntimePerformanceMode.Balanced;
    public string CurrentMap { get; set; } = "bakurani";
    public string DeepSeekModel { get; set; } = "deepseek-flash";
    public bool SpeakNavigation { get; set; } = true;
    public bool AutoReadTarget { get; set; } = false;
    public bool VisualTargetNavigationEnabled { get; set; }
    public bool VisualContinuousLocalizationEnabled { get; set; } = true;
    public int VisualHudSpeedScanMilliseconds { get; set; } = 1800;
    public double VisualMapMatchMaxMeters { get; set; } = 140;
    public NormalizedRegion DrivingViewRegion { get; set; } = new();
    public NormalizedRegion SpeedHudRegion { get; set; } = new();
    public double VisualMapMinRegistrationConfidence { get; set; } = 0.42;
    public double VisualTargetMinConfidence { get; set; } = 0.54;
    public int VisualTargetScanSeconds { get; set; } = 3;
    public TargetMarkerProfile TargetMarkerProfile { get; set; } = new();
    public bool AiAutoApplyNavigationLearning { get; set; }
    public double AiLearningMinConfidence { get; set; } = 0.82;
    public bool AiAutoVisionScan { get; set; }
    public double AiVisionMinConfidence { get; set; } = 0.82;
    public int AiVisionEvidenceMinutes { get; set; } = 8;
    public int AiVisionCacheSeconds { get; set; } = 90;
    public int AiVisionMaxImageDimension { get; set; } = 1280;
    public RoutePreference RoutePreference { get; set; } = RoutePreference.Fastest;
    public NormalizedRegion PlayerRegion { get; set; } = new();
    public NormalizedRegion TargetRegion { get; set; } = new();
    public NormalizedRegion VisionMapRegion { get; set; } = new();

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WardogsNavigator");
    private static string PathName => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(PathName))
                return FromJson(File.ReadAllText(PathName));
        }
        catch { }

        return new AppSettings();
    }

    public static AppSettings FromJson(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new AppSettings();

            Normalize(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static void Normalize(AppSettings s)
    {
        s.TargetMarkerProfile ??= new TargetMarkerProfile();
        s.PlayerRegion ??= new NormalizedRegion();
        s.TargetRegion ??= new NormalizedRegion();
        s.VisionMapRegion ??= new NormalizedRegion();
        s.DrivingViewRegion ??= new NormalizedRegion();
        s.SpeedHudRegion ??= new NormalizedRegion();

        s.VisualMapMinRegistrationConfidence = Math.Clamp(
            s.VisualMapMinRegistrationConfidence <= 0
                ? 0.42
                : s.VisualMapMinRegistrationConfidence,
            0.20,
            0.95);

        s.VisualTargetMinConfidence = Math.Clamp(
            s.VisualTargetMinConfidence <= 0
                ? 0.54
                : s.VisualTargetMinConfidence,
            0.25,
            0.95);

        s.VisualTargetScanSeconds = Math.Clamp(
            s.VisualTargetScanSeconds <= 0
                ? 3
                : s.VisualTargetScanSeconds,
            2,
            15);

        s.AiVisionCacheSeconds = Math.Clamp(
            s.AiVisionCacheSeconds <= 0
                ? 90
                : s.AiVisionCacheSeconds,
            15,
            600);

        s.AiVisionMaxImageDimension = Math.Clamp(
            s.AiVisionMaxImageDimension <= 0
                ? 1280
                : s.AiVisionMaxImageDimension,
            768,
            1920);

        s.VisualHudSpeedScanMilliseconds = Math.Clamp(
            s.VisualHudSpeedScanMilliseconds <= 0
                ? 1800
                : s.VisualHudSpeedScanMilliseconds,
            600,
            5000);

        s.VisualMapMatchMaxMeters = Math.Clamp(
            s.VisualMapMatchMaxMeters <= 0
                ? 140
                : s.VisualMapMatchMaxMeters,
            60,
            300);

        if (!Enum.IsDefined(s.CaptureBackend))
            s.CaptureBackend = CaptureBackendMode.Auto;

        if (!Enum.IsDefined(s.PerformanceMode))
            s.PerformanceMode = RuntimePerformanceMode.Balanced;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(PathName, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
