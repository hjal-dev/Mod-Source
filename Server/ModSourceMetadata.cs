using System.Collections.Generic;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace ModSource.Server;

public sealed class ModSourceMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.hj.modsource.server";
    public string Name { get; init; } = "Hj's Mod Source";
    public string Author { get; init; } = "HJ";
    public List<string> Contributors { get; init; }
    public Version Version { get; init; } = new("1.0.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string> Incompatibilities { get; init; }
    public Dictionary<string, Range> ModDependencies { get; init; }
    public string Url { get; init; }
    public string License { get; init; } = "MIT";
}
