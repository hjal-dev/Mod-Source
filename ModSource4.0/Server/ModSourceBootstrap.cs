using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

namespace ModSource.Server;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Database + 1)]
public sealed class ModSourceBootstrap(
    IServiceProvider serviceProvider,
    IReadOnlyList<SptMod> loadedMods,
    DatabaseService databaseService
) : IOnLoad
{
    private static readonly List<string> Buffer = [];

    private static HashSet<Assembly> _modAssemblies = [];

    public static ModSourceConfig Config { get; private set; } = new();

    public static bool Tracking { get; private set; }

    public static int TrackedModAssemblyCount => _modAssemblies.Count;

    public static Dictionary<OriginKind, HashSet<string>> VanillaIds { get; private set; } = [];

    public static bool IsVanilla(OriginKind kind, string id)
    {
        return VanillaIds.TryGetValue(kind, out var ids) && ids.Contains(id);
    }

    public static IReadOnlyList<string> DrainLog()
    {
        lock (Buffer)
        {
            var copy = Buffer.ToArray();
            Buffer.Clear();
            return copy;
        }
    }

    internal static void Log(string message)
    {
        lock (Buffer)
        {
            Buffer.Add(message);
        }
    }

    public Task OnLoad()
    {
        try
        {
            var modDirectory = Path.GetDirectoryName(typeof(ModSourceBootstrap).Assembly.Location) ?? ".";
            Config = ModSourceConfig.Load(modDirectory);

            ProvenanceTracker.Initialize(BuildSources(), Log);

            VanillaIds = ProvenanceTracker.Capture();
            Log(
                "Captured vanilla baseline: "
                    + string.Join(", ", VanillaIds.Select(kv => $"{kv.Value.Count} {kv.Key.ToString().ToLowerInvariant()}s"))
                    + "."
            );

            if (!Config.TrackLoadOrder)
            {
                ProvenanceTracker.Stop();
                Log("Load-order tracking disabled by config; relying on CustomItemService and bundle attribution only.");
                return Task.CompletedTask;
            }

            var self = typeof(ModSourceBootstrap).Assembly;
            _modAssemblies = loadedMods.SelectMany(m => m.Assemblies).Where(a => !ReferenceEquals(a, self)).ToHashSet();
            WatchLoadSteps();
        }
        catch (Exception ex)
        {
            ProvenanceTracker.Stop();
            Log($"Bootstrap failed, continuing without load-order tracking: {ex}");
        }

        return Task.CompletedTask;
    }

    private Dictionary<OriginKind, Func<IEnumerable<string>>> BuildSources()
    {
        return new Dictionary<OriginKind, Func<IEnumerable<string>>>
        {
            [OriginKind.Item] = () => databaseService.GetItems().Keys.Select(id => id.ToString()),
            [OriginKind.Quest] = () => databaseService.GetQuests().Keys.Select(id => id.ToString()),
            [OriginKind.Achievement] = () =>
                (databaseService.GetAchievements() ?? [])
                    .Concat(databaseService.GetCustomAchievements() ?? [])
                    .Select(a => a.Id.ToString()),
            [OriginKind.Customization] = () => databaseService.GetCustomization().Keys.Select(id => id.ToString()),
            [OriginKind.Assort] = () =>
                databaseService.GetTraders().Values
                    .Where(t => t?.Assort?.Items is not null)
                    .SelectMany(t => t.Assort.Items)
                    .Where(i => i.ParentId == "hideout")
                    .Select(i => i.Id.ToString()),
        };
    }

    private void WatchLoadSteps()
    {
        var steps = FindLoadSteps();
        if (steps is null)
        {
            ProvenanceTracker.Stop();
            Log("Could not find SPT's load step list; load-order tracking unavailable.");
            return;
        }

        var start = Array.FindIndex(steps, s => ReferenceEquals(s, this));
        if (start < 0)
        {
            ProvenanceTracker.Stop();
            Log("Could not find Mod Source in SPT's load step list; load-order tracking unavailable.");
            return;
        }

        var wrapped = 0;
        for (var i = start + 1; i < steps.Length; i++)
        {
            var step = steps[i];
            if (step is null || step is TrackedLoadStep || !_modAssemblies.Contains(step.GetType().Assembly))
            {
                continue;
            }

            steps[i] = new TrackedLoadStep(step);
            wrapped++;
        }

        Tracking = true;
        Log($"Watching {wrapped} load steps from {_modAssemblies.Count} mod assemblies. No code is patched.");
    }

    private IOnLoad[] FindLoadSteps()
    {
        var app = serviceProvider.GetService(typeof(App));
        if (app is null)
        {
            return null;
        }

        for (var type = app.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!typeof(IEnumerable<IOnLoad>).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                if (field.GetValue(app) is IOnLoad[] steps && Array.IndexOf(steps, this) >= 0)
                {
                    return steps;
                }
            }
        }

        return null;
    }
}
