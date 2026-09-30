using System.Collections.Generic;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace ModSource.Server;

public sealed record ModSourceMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.hj.modsource.server";
    public override string Name { get; init; } = "Hj's Mod Source";
    public override string Author { get; init; } = "HJ";
    public override List<string> Contributors { get; init; }
    public override Version Version { get; init; } = new("0.9.0");
    public override Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string> Incompatibilities { get; init; }
    public override Dictionary<string, Range> ModDependencies { get; init; }
    public override string Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
