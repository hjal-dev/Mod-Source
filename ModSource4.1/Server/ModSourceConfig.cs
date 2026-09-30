using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModSource.Server;

public sealed class ModSourceConfig
{
    [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; }

    [JsonPropertyName("trackLoadOrder")]
    public bool TrackLoadOrder { get; set; } = true;

    [JsonPropertyName("allowBundleOnlyAttribution")]
    public bool AllowBundleOnlyAttribution { get; set; } = true;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ModSourceConfig Load(string modDirectory)
    {
        try
        {
            var path = Path.Combine(modDirectory, "config.json");
            if (!File.Exists(path))
            {
                return new ModSourceConfig();
            }

            return JsonSerializer.Deserialize<ModSourceConfig>(File.ReadAllText(path), ReadOptions) ?? new ModSourceConfig();
        }
        catch
        {
            return new ModSourceConfig();
        }
    }
}
