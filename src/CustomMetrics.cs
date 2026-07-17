// CustomMetrics.cs — model + watcher for user-provided metrics JSON files.
// Mirrors RunCatNeo's Custom Metrics schema (docs/CustomMetricsSchema.md).
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunCatNeo.Win;

public sealed class CustomMetric
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("formattedValue")] public string FormattedValue { get; set; } = "";
    [JsonPropertyName("normalizedValue")] public double? NormalizedValue { get; set; }
}

public sealed class CustomMetricsSnapshot
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("metricsBarValue")] public string? MetricsBarValue { get; set; }
    [JsonPropertyName("metrics")] public List<CustomMetric> Metrics { get; set; } = [];
    [JsonPropertyName("lastUpdatedDate")] public DateTimeOffset? LastUpdatedDate { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static CustomMetricsSnapshot Parse(string json) =>
        JsonSerializer.Deserialize<CustomMetricsSnapshot>(json, Options)
        ?? throw new JsonException("null snapshot");
}

/// <summary>A watched JSON source: last good snapshot plus whether the most recent read failed.</summary>
public sealed class MetricsSource(string filePath)
{
    public string FilePath { get; } = filePath;
    public CustomMetricsSnapshot? Snapshot { get; private set; }
    public bool Failed { get; private set; }

    public string DisplayName => Snapshot?.Title ?? Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Re-reads the file. Keeps the previous snapshot but sets Failed on error (matches macOS behavior).</summary>
    public void Reload()
    {
        try
        {
            var snapshot = CustomMetricsSnapshot.Parse(File.ReadAllText(FilePath));
            Snapshot = snapshot;
            Failed = false;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Failed = true;
        }
    }
}

/// <summary>
/// Watches %APPDATA%\RunCatNeo\Metrics\*.json (plus any files listed in sources.json) and raises
/// <see cref="Changed"/> — coalesced — whenever any of them appears, changes, or disappears.
/// </summary>
public sealed class MetricsWatcher : IDisposable
{
    public static string MetricsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RunCatNeo", "Metrics");
    private static string SourcesListPath => Path.Combine(MetricsDir, "sources.json");

    private readonly FileSystemWatcher _watcher;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 200 };
    private readonly Dictionary<string, MetricsSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizationContext _ui;

    /// <summary>Raised on the UI thread after sources are reloaded.</summary>
    public event Action? Changed;

    public IReadOnlyCollection<MetricsSource> Sources => _sources.Values;

    public MetricsWatcher()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        Directory.CreateDirectory(MetricsDir);

        _watcher = new FileSystemWatcher(MetricsDir, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Renamed += OnFsEvent;

        // Retry loop: files listed via sources.json may live elsewhere and appear/disappear (macOS parity).
        _debounce.Tick += (_, _) => { _debounce.Stop(); ReloadAll(); };

        ReloadAll();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // FS events fire off-thread and often in bursts; coalesce onto the UI timer.
        _ui.Post(_ => { _debounce.Stop(); _debounce.Start(); }, null);
    }

    /// <summary>Registers an external JSON file (outside the Metrics dir) as a source.</summary>
    public void AddExternalSource(string filePath)
    {
        var list = LoadExternalList();
        if (!list.Contains(filePath, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(filePath);
            SaveExternalList(list);
        }
        ReloadAll();
    }

    public void RemoveSource(string filePath)
    {
        var list = LoadExternalList();
        list.RemoveAll(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        SaveExternalList(list);
        // Files physically inside the Metrics dir are removed by deleting them; external ones just drop out.
        ReloadAll();
    }

    private void ReloadAll()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(MetricsDir))
        {
            foreach (var f in Directory.EnumerateFiles(MetricsDir, "*.json"))
            {
                if (!string.Equals(Path.GetFileName(f), "sources.json", StringComparison.OrdinalIgnoreCase))
                    paths.Add(f);
            }
        }
        foreach (var f in LoadExternalList()) paths.Add(f);

        // Drop sources whose files are gone.
        foreach (var gone in _sources.Keys.Where(k => !paths.Contains(k)).ToList())
            _sources.Remove(gone);

        foreach (var p in paths)
        {
            if (!_sources.TryGetValue(p, out var source))
            {
                source = new MetricsSource(p);
                _sources[p] = source;
            }
            source.Reload();
        }
        Changed?.Invoke();
    }

    private static List<string> LoadExternalList()
    {
        try
        {
            if (File.Exists(SourcesListPath))
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SourcesListPath)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return [];
    }

    private static void SaveExternalList(List<string> list)
    {
        try
        {
            Directory.CreateDirectory(MetricsDir);
            File.WriteAllText(SourcesListPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
