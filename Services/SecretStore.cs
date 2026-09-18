using System.Security.Cryptography;
using System.Text;

namespace WardogsNavigator.Services;

public static class SecretStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WardogsNavigator");
    private static readonly string KeyPath = Path.Combine(Dir, "deepseek.key");

    public static void SaveDeepSeekKey(string key)
    {
        Directory.CreateDirectory(Dir);
        if (string.IsNullOrWhiteSpace(key))
        {
            if (File.Exists(KeyPath)) File.Delete(KeyPath);
            return;
        }

        var raw = Encoding.UTF8.GetBytes(key.Trim());
        var encrypted = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
        File.WriteAllText(KeyPath, Convert.ToBase64String(encrypted));
    }

    public static string LoadDeepSeekKey()
    {
        try
        {
            if (!File.Exists(KeyPath)) return "";
            var encrypted = Convert.FromBase64String(File.ReadAllText(KeyPath));
            var raw = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        }
        catch
        {
            return "";
        }
    }
}
