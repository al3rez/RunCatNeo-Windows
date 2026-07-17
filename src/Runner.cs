// Runner.cs — runner model and repository.
// Port of RunCatNeo's Runner/RunnerKind/FrameOrder (Apache-2.0, Copyright 2026 Kyome22).
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunCatNeo.Win;

public sealed class Runner
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsTemplate { get; init; }
    public required int[] FrameOrder { get; init; }
    public required bool IsCustom { get; init; }
    /// <summary>Custom runners load frames from this directory; built-ins from embedded resources.</summary>
    public string? Directory { get; init; }

    // Frame order patterns from FrameOrder.swift.
    public static int[] Ascending(int n) => Enumerable.Range(0, n).ToArray();
    public static readonly int[] Swing = [0, 1, 2, 3, 4, 3, 2, 1];
    public static readonly int[] Pendulum = [0, 1, 2, 1, 0, 3, 4, 3];
    public static readonly int[] PartyHorn = [0, 1, 2, 3, 4, 4, 3, 2, 1];

    public static readonly Runner[] BuiltIns =
    [
        new() { Id = "cat", Name = "Cat", IsTemplate = true, FrameOrder = Ascending(5), IsCustom = false },
        new() { Id = "dog", Name = "Dog", IsTemplate = true, FrameOrder = Ascending(5), IsCustom = false },
        new() { Id = "slime", Name = "Slime", IsTemplate = true, FrameOrder = PartyHorn, IsCustom = false },
        new() { Id = "drop", Name = "Drop", IsTemplate = true, FrameOrder = Ascending(5), IsCustom = false },
        new() { Id = "coffee", Name = "Coffee", IsTemplate = true, FrameOrder = Ascending(10), IsCustom = false },
        new() { Id = "newton-cradle", Name = "Newton's Cradle", IsTemplate = true, FrameOrder = Pendulum, IsCustom = false },
        new() { Id = "engine", Name = "Engine", IsTemplate = true, FrameOrder = Ascending(10), IsCustom = false },
        new() { Id = "mochi", Name = "Mochi", IsTemplate = true, FrameOrder = Swing, IsCustom = false },
    ];

    /// <summary>Loads the distinct source bitmaps, indexed by frame number.</summary>
    public Dictionary<int, Bitmap> LoadFrameBitmaps()
    {
        var bitmaps = new Dictionary<int, Bitmap>();
        foreach (var n in FrameOrder.Distinct())
        {
            if (IsCustom)
            {
                var path = Path.Combine(Directory!, $"frame-{n}.png");
                if (!File.Exists(path)) continue;
                using var fs = File.OpenRead(path);
                bitmaps[n] = new Bitmap(fs);
            }
            else
            {
                var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"runners/{Id}-frame-{n}.png");
                if (stream is null) continue;
                using (stream) bitmaps[n] = new Bitmap(stream);
            }
        }
        return bitmaps;
    }
}

internal sealed class CustomRunnerManifest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("frameOrder")] public int[]? FrameOrder { get; set; }
    [JsonPropertyName("isTemplate")] public bool IsTemplate { get; set; }
}

public static class RunnerRepository
{
    public static string CustomRunnersRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RunCatNeo", "Runners");

    /// <summary>
    /// Custom runners live in %APPDATA%\RunCatNeo\Runners\&lt;name&gt;\frame-0.png, frame-1.png, …
    /// with an optional runner.json: { "name": "...", "frameOrder": [0,1,2,...], "isTemplate": false }.
    /// </summary>
    public static List<Runner> LoadCustomRunners()
    {
        var runners = new List<Runner>();
        if (!Directory.Exists(CustomRunnersRoot)) return runners;
        foreach (var dir in Directory.GetDirectories(CustomRunnersRoot))
        {
            var frameCount = 0;
            while (File.Exists(Path.Combine(dir, $"frame-{frameCount}.png"))) frameCount++;
            if (frameCount == 0) continue;

            var name = Path.GetFileName(dir);
            var order = Runner.Ascending(frameCount);
            var isTemplate = false;
            var manifestPath = Path.Combine(dir, "runner.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = JsonSerializer.Deserialize<CustomRunnerManifest>(File.ReadAllText(manifestPath));
                    if (manifest is not null)
                    {
                        if (!string.IsNullOrWhiteSpace(manifest.Name)) name = manifest.Name;
                        if (manifest.FrameOrder is { Length: > 0 } o && o.All(i => i >= 0 && i < frameCount)) order = o;
                        isTemplate = manifest.IsTemplate;
                    }
                }
                catch (JsonException) { /* malformed manifest → defaults */ }
            }

            runners.Add(new Runner
            {
                Id = $"custom:{Path.GetFileName(dir)}",
                Name = name,
                IsTemplate = isTemplate,
                FrameOrder = order,
                IsCustom = true,
                Directory = dir,
            });
        }
        return runners;
    }

    public static Runner Resolve(string id)
    {
        var all = Runner.BuiltIns.Concat(LoadCustomRunners());
        return all.FirstOrDefault(r => r.Id == id) ?? Runner.BuiltIns[0];
    }
}
