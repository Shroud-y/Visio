using System.IO;
using System.Text.Json;

namespace Visio;

/// <summary>Per-user preferences, stored as JSON in %LOCALAPPDATA%\Visio\settings.json.</summary>
internal sealed class Settings
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Visio");

    private static readonly string FilePath = Path.Combine(DataDir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // Window placement (restored size even when the window was closed maximized).
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Maximized { get; set; }

    /// <summary>False after the user picked "Don't ask again" on the default-app prompt.</summary>
    public bool AskDefaultApp { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public string? SkippedVersion { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken settings file must never stop the viewer from starting.
        }
        return new();
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the file's current contents, so several
    /// Visio windows (one process each) don't overwrite each other's settings.
    /// </summary>
    public static Settings Update(Action<Settings> change)
    {
        var s = Load();
        change(s);
        s.Save();
        return s;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
