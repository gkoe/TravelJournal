using System.IO;
using System.Text.Json;

namespace TravelJournal.Wpf.Services;

public sealed class UserSettingsService
{
    private readonly string _path;

    public UserSettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TravelJournal");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "user-settings.json");
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return UserSettings.Default;
            var json     = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);
            return settings ?? UserSettings.Default;
        }
        catch
        {
            return UserSettings.Default;
        }
    }

    public void Save(UserSettings settings)
    {
        try
        {
            var tmp  = _path + ".tmp";
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch { }
    }
}

public sealed record UserSettings(
    string MapStyleId,
    string Language,
    int    BoundsPaddingPercent)
{
    public static readonly UserSettings Default = new("outdoor-v2", "de", 12);
}
