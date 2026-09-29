using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModSource.Server;

public static class OriginConfidence
{
    public const string High = "High";

    public const string Medium = "Medium";

    public const string Low = "Low";
}

public static class OriginSource
{
    public const string CustomItemService = "CustomItemService";
    public const string LoadOrderDiff = "LoadOrderDiff";

    public const string LoadOrderDiffLibrary = "LoadOrderDiffLibrary";

    public const string BundleOwner = "BundleOwner";

    public const string NameMatch = "NameMatch";

    public const string Unknown = "Unknown";
}

public sealed record ModSourceEntry
{
    [JsonPropertyName("modName")]
    public string ModName { get; init; }

    [JsonPropertyName("modAuthor")]
    public string ModAuthor { get; init; }

    [JsonPropertyName("modGuid")]
    public string ModGuid { get; init; }

    [JsonPropertyName("modVersion")]
    public string ModVersion { get; init; }

    [JsonPropertyName("bundleModName")]
    public string BundleModName { get; init; }

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = OriginConfidence.Low;

    [JsonPropertyName("source")]
    public string Source { get; init; } = OriginSource.Unknown;
}

public sealed record ModSourcePayload
{
    [JsonPropertyName("items")]
    public Dictionary<string, ModSourceEntry> Items { get; init; } = [];

    [JsonPropertyName("quests")]
    public Dictionary<string, ModSourceEntry> Quests { get; init; } = [];

    [JsonPropertyName("achievements")]
    public Dictionary<string, ModSourceEntry> Achievements { get; init; } = [];

    [JsonPropertyName("customization")]
    public Dictionary<string, ModSourceEntry> Customization { get; init; } = [];

    [JsonPropertyName("assorts")]
    public Dictionary<string, ModSourceEntry> Assorts { get; init; } = [];

    [JsonPropertyName("ready")]
    public bool Ready { get; init; }
}
