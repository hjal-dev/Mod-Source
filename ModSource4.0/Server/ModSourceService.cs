using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Loaders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;

namespace ModSource.Server;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostSptModLoader + 100)]
public sealed class ModSourceService(
    ISptLogger<ModSourceService> logger,
    IReadOnlyList<SptMod> loadedMods,
    DatabaseService databaseService,
    BundleLoader bundleLoader,
    ModItemCacheService modItemCacheService
) : IOnLoad
{
    private ModSourcePayload _payload = new();

    public ModSourcePayload Payload => _payload;

    public Task OnLoad()
    {
        try
        {
            Build();
        }
        catch (Exception ex)
        {
            logger.Error($"[ModSource] Failed to build provenance database: {ex}");
            _payload = new ModSourcePayload { Ready = false };
        }

        return Task.CompletedTask;
    }

    private void Build()
    {
        var config = ModSourceBootstrap.Config;
        var debug = config.DebugLogging;

        ProvenanceTracker.Stop();
        FlushBootstrapLog(debug);

        var modsByAssembly = BuildAssemblyIndex();
        var modsByGuid = loadedMods.ToDictionary(m => m.ModMetadata.ModGuid, m => m, StringComparer.OrdinalIgnoreCase);
        var modsByPath = BuildPathIndex();
        var bundleOwners = BuildBundleOwnerIndex(modsByPath, out var bundleCount);

        var creatorByCustomItemService = new Dictionary<MongoId, SptMod>();
        foreach (var (guid, itemIds) in modItemCacheService.GetAllCachedModItemIds())
        {
            if (!modsByGuid.TryGetValue(guid, out var mod))
            {
                continue;
            }

            foreach (var id in itemIds)
            {
                creatorByCustomItemService[id] = mod;
            }
        }

        var creatorsByKind = new Dictionary<OriginKind, Dictionary<string, SptMod>>();

        foreach (var (assembly, perKind) in ProvenanceTracker.Results)
        {
            if (!modsByAssembly.TryGetValue(assembly, out var mod))
            {
                continue;
            }

            foreach (var (kind, ids) in perKind)
            {
                if (!creatorsByKind.TryGetValue(kind, out var creators))
                {
                    creators = [];
                    creatorsByKind[kind] = creators;
                }

                foreach (var id in ids)
                {
                    creators.TryAdd(id, mod);
                }
            }
        }

        var creatorByLoadOrder = creatorsByKind.GetValueOrDefault(OriginKind.Item) ?? [];

        var displayNames = BuildDisplayNames();
        var libraryGuids = BuildLibraryGuidSet();
        var identifiers = BuildIdentifierIndex();

        var entries = new Dictionary<string, ModSourceEntry>();

        foreach (var (mongoId, template) in databaseService.GetItems())
        {
            var id = mongoId.ToString();
            if (ModSourceBootstrap.IsVanilla(OriginKind.Item, id))
            {
                continue;
            }

            var bundleMod = ResolveBundleOwner(template, bundleOwners);

            SptMod creator = null;
            var source = OriginSource.Unknown;

            if (creatorByCustomItemService.TryGetValue(mongoId, out var viaService))
            {
                creator = viaService;
                source = OriginSource.CustomItemService;
            }
            else if (creatorByLoadOrder.TryGetValue(id, out var viaDiff))
            {
                creator = viaDiff;
                source = OriginSource.LoadOrderDiff;
            }
            else if (bundleMod is not null && config.AllowBundleOnlyAttribution)
            {
                creator = bundleMod;
                source = OriginSource.BundleOwner;
            }
            else if (ResolveByName(template, identifiers) is { } viaName)
            {
                creator = viaName;
                source = OriginSource.NameMatch;
            }

            if (source == OriginSource.LoadOrderDiff && creator is not null && libraryGuids.Contains(creator.ModMetadata.ModGuid))
            {
                source = OriginSource.LoadOrderDiffLibrary;
            }

            if (creator is null)
            {
                entries[id] = new ModSourceEntry { Confidence = OriginConfidence.Low, Source = OriginSource.Unknown };
                continue;
            }

            var confidence = source switch
            {
                OriginSource.CustomItemService or OriginSource.LoadOrderDiff => OriginConfidence.High,
                OriginSource.BundleOwner or OriginSource.LoadOrderDiffLibrary => OriginConfidence.Medium,
                _ => OriginConfidence.Low,
            };

            var creatorGuid = creator.ModMetadata.ModGuid;

            entries[id] = new ModSourceEntry
            {
                ModName = DisplayName(displayNames, creator),
                ModAuthor = creator.ModMetadata.Author,
                ModGuid = creatorGuid,
                ModVersion = creator.ModMetadata.Version.ToString(),
                BundleModName = bundleMod is not null && !GuidEquals(bundleMod.ModMetadata.ModGuid, creatorGuid)
                    ? DisplayName(displayNames, bundleMod)
                    : null,
                Confidence = confidence,
                Source = source,
            };
        }

        var quests = BuildLoadOrderEntries(OriginKind.Quest, creatorsByKind, displayNames, libraryGuids);
        var achievements = BuildLoadOrderEntries(OriginKind.Achievement, creatorsByKind, displayNames, libraryGuids);
        var customization = BuildLoadOrderEntries(OriginKind.Customization, creatorsByKind, displayNames, libraryGuids);
        var assorts = BuildLoadOrderEntries(OriginKind.Assort, creatorsByKind, displayNames, libraryGuids);

        _payload = new ModSourcePayload
        {
            Items = entries,
            Quests = quests,
            Achievements = achievements,
            Customization = customization,
            Assorts = assorts,
            Ready = true,
        };

        var mods = entries.Values.Concat(quests.Values).Concat(achievements.Values).Concat(customization.Values).Concat(assorts.Values)
            .Where(e => e.ModGuid != null)
            .Select(e => e.ModGuid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        logger.Info(
            $"[ModSource] Detected {entries.Values.Count(e => e.ModName != null)} items, {quests.Count} quests, "
                + $"{achievements.Count} achievements, {customization.Count} clothing and {assorts.Count} trader offers from {mods} mods."
        );

        if (debug)
        {
            DebugLog($"[ModSource] Indexed {bundleCount} bundles from {bundleOwners.Values.Distinct().Count()} bundle mods.");
            DebugLog(
                $"[ModSource] Load-order tracking: {(ModSourceBootstrap.Tracking ? "on" : "off")}, "
                    + $"{ModSourceBootstrap.TrackedModAssemblyCount} mod assemblies tracked. No code is patched."
            );
        }
    }

    private Dictionary<string, ModSourceEntry> BuildLoadOrderEntries(
        OriginKind kind,
        Dictionary<OriginKind, Dictionary<string, SptMod>> creatorsByKind,
        Dictionary<string, string> displayNames,
        HashSet<string> libraryGuids)
    {
        var result = new Dictionary<string, ModSourceEntry>();
        if (!creatorsByKind.TryGetValue(kind, out var creators))
        {
            return result;
        }

        foreach (var (id, mod) in creators)
        {
            if (ModSourceBootstrap.IsVanilla(kind, id))
            {
                continue;
            }

            var isLibrary = libraryGuids.Contains(mod.ModMetadata.ModGuid);

            result[id] = new ModSourceEntry
            {
                ModName = DisplayName(displayNames, mod),
                ModAuthor = mod.ModMetadata.Author,
                ModGuid = mod.ModMetadata.ModGuid,
                ModVersion = mod.ModMetadata.Version.ToString(),
                Confidence = isLibrary ? OriginConfidence.Medium : OriginConfidence.High,
                Source = isLibrary ? OriginSource.LoadOrderDiffLibrary : OriginSource.LoadOrderDiff,
            };
        }

        return result;
    }

    private void DebugLog(string message)
    {
        if (ModSourceBootstrap.Config.DebugLogging)
        {
            logger.Info(message);
        }
    }

    private static bool GuidEquals(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayName(Dictionary<string, string> displayNames, SptMod mod)
    {
        return displayNames.TryGetValue(mod.ModMetadata.ModGuid, out var name) ? name : mod.ModMetadata.Name;
    }

    private Dictionary<string, string> BuildDisplayNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in loadedMods.GroupBy(m => m.ModMetadata.Name, StringComparer.OrdinalIgnoreCase))
        {
            var mods = group.ToList();
            if (mods.Count == 1)
            {
                result[mods[0].ModMetadata.ModGuid] = mods[0].ModMetadata.Name;
                continue;
            }

            foreach (var mod in mods)
            {
                var authorAlsoCollides =
                    mods.Count(m => string.Equals(m.ModMetadata.Author, mod.ModMetadata.Author, StringComparison.OrdinalIgnoreCase)) > 1;

                result[mod.ModMetadata.ModGuid] = authorAlsoCollides
                    ? $"{mod.ModMetadata.Name} ({mod.ModMetadata.ModGuid})"
                    : $"{mod.ModMetadata.Name} (by {mod.ModMetadata.Author})";
            }
        }

        return result;
    }

    private HashSet<string> BuildLibraryGuidSet()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in loadedMods)
        {
            var dependencies = mod.ModMetadata.ModDependencies;
            if (dependencies is null)
            {
                continue;
            }

            foreach (var guid in dependencies.Keys)
            {
                libraries.Add(guid);
            }
        }

        return libraries;
    }

    private Dictionary<string, SptMod> BuildIdentifierIndex()
    {
        const int minimumLength = 6;

        var index = new Dictionary<string, SptMod>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string identifier, SptMod mod)
        {
            if (string.IsNullOrWhiteSpace(identifier) || identifier.Length < minimumLength)
            {
                return;
            }

            if (index.TryGetValue(identifier, out var existing) && !GuidEquals(existing.ModMetadata.ModGuid, mod.ModMetadata.ModGuid))
            {
                ambiguous.Add(identifier);
                return;
            }

            index[identifier] = mod;
        }

        foreach (var mod in loadedMods)
        {
            Add(mod.ModMetadata.ModGuid, mod);

            foreach (var assembly in mod.Assemblies)
            {
                Add(assembly.GetName().Name, mod);
            }

            try
            {
                Add(new DirectoryInfo(mod.Directory).Name, mod);
            }
            catch
            {
            }
        }

        foreach (var key in ambiguous)
        {
            index.Remove(key);
        }

        return index;
    }

    private static SptMod ResolveByName(TemplateItem template, Dictionary<string, SptMod> identifiers)
    {
        var name = template.Name;
        if (string.IsNullOrWhiteSpace(name) || identifiers.Count == 0)
        {
            return null;
        }

        SptMod match = null;

        foreach (var (identifier, mod) in identifiers)
        {
            if (name.IndexOf(identifier, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (match is not null && !GuidEquals(match.ModMetadata.ModGuid, mod.ModMetadata.ModGuid))
            {
                return null;
            }

            match = mod;
        }

        return match;
    }

    private Dictionary<Assembly, SptMod> BuildAssemblyIndex()
    {
        var index = new Dictionary<Assembly, SptMod>();
        foreach (var mod in loadedMods)
        {
            foreach (var assembly in mod.Assemblies)
            {
                index[assembly] = mod;
            }
        }

        return index;
    }

    private Dictionary<string, SptMod> BuildPathIndex()
    {
        var index = new Dictionary<string, SptMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in loadedMods)
        {
            try
            {
                index[Normalise(mod.GetModPath())] = mod;
            }
            catch
            {
            }
        }

        return index;
    }

    private Dictionary<string, SptMod> BuildBundleOwnerIndex(Dictionary<string, SptMod> modsByPath, out int bundleCount)
    {
        var owners = new Dictionary<string, SptMod>(StringComparer.OrdinalIgnoreCase);
        var bundles = bundleLoader.GetBundles();
        bundleCount = bundles.Count;

        foreach (var bundle in bundles)
        {
            if (modsByPath.TryGetValue(Normalise(bundle.ModPath), out var mod))
            {
                owners[Normalise(bundle.Bundle.Key)] = mod;
            }
        }

        return owners;
    }

    private static SptMod ResolveBundleOwner(TemplateItem template, Dictionary<string, SptMod> bundleOwners)
    {
        foreach (var path in new[] { template.Properties?.Prefab?.Path, template.Properties?.UsePrefab?.Path })
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (bundleOwners.TryGetValue(Normalise(path), out var mod))
            {
                return mod;
            }
        }

        return null;
    }

    private static string Normalise(string value)
    {
        return value.Replace('\\', '/').TrimStart('.', '/').Trim();
    }

    private void FlushBootstrapLog(bool debug)
    {
        var lines = ModSourceBootstrap.DrainLog();
        if (!debug)
        {
            return;
        }

        foreach (var line in lines)
        {
            DebugLog($"[ModSource] {line}");
        }
    }
}
