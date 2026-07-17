// Settings.cs — persisted app settings (%APPDATA%\RunCatNeo\settings.json).
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunCatNeo.Win;

public enum IconTheme { Auto, Light, Dark }

public sealed class Settings
{
    [JsonPropertyName("runnerId")] public string RunnerId { get; set; } = "cat";
    [JsonPropertyName("slowerUnderLoad")] public bool SlowerUnderLoad { get; set; }
    [JsonPropertyName("flippedHorizontally")] public bool FlippedHorizontally { get; set; }
    [JsonPropertyName("iconTheme")]
    [JsonConverter(typeof(JsonStringEnumConverter<IconTheme>))]
    public IconTheme IconTheme { get; set; } = IconTheme.Auto;
    [JsonPropertyName("updateIntervalSeconds")] public int UpdateIntervalSeconds { get; set; } = 5;

    public static readonly int[] AllowedIntervals = [3, 5, 10];

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RunCatNeo");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    if (!AllowedIntervals.Contains(settings.UpdateIntervalSeconds)) settings.UpdateIntervalSeconds = 5;
                    return settings;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
