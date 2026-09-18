using System.Text.Json;

namespace WardogsNavigator;

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
    public string CurrentMap { get; set; } = "bakurani";
    public string DeepSeekModel { get; set; } = "deepseek-flash";
    public bool SpeakNavigation { get; set; } = true;
    public bool AutoReadTarget { get; set; } = true;
    public bool AiAutoApplyNavigationLearning { get; set; }
    public double AiLearningMinConfidence { get; set; } = 0.82;
    public RoutePreference RoutePreference { get; set; } = RoutePreference.Fastest;
    public NormalizedRegion PlayerRegion { get; set; } = new();
    public NormalizedRegion TargetRegion { get; set; } = new();

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WardogsNavigator");
    private static string PathName => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(PathName))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathName));
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
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
