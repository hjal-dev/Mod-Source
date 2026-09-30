using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.DI;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ModSource.Server;

public sealed class ModSourceBootstrap : IOnDIConstruct
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

    internal static bool IsTrackedModType(Type type)
    {
        return _modAssemblies.Contains(type.Assembly);
    }

    public static Task OnDIConstructAsync(IServiceCollection serviceCollection, CancellationToken cancellationToken)
    {
        try
        {
            var modDirectory = Path.GetDirectoryName(typeof(ModSourceBootstrap).Assembly.Location) ?? ".";
            Config = ModSourceConfig.Load(modDirectory);

            var templates = serviceCollection.FirstOrDefault(d => d.ServiceType == typeof(TemplateTable))?.ImplementationInstance
                as TemplateTable;

            if (templates is null)
            {
                Log("Could not resolve TemplateTable during DI construction; attribution will be bundle-only.");
                return Task.CompletedTask;
            }

            var traders = serviceCollection.FirstOrDefault(d => d.ServiceType == typeof(TradersTable))?.ImplementationInstance
                as TradersTable;

            if (traders is null)
            {
                Log("Could not resolve TradersTable during DI construction; trader offers will not be attributed.");
            }

            ProvenanceTracker.Initialize(BuildSources(templates, traders), Log);

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

            _modAssemblies = ModAssemblies(typeof(ModSourceBootstrap).Assembly).ToHashSet();
            WatchLoadSteps(serviceCollection);
        }
        catch (Exception ex)
        {
            Log($"Bootstrap failed, continuing without load-order tracking: {ex}");
        }

        return Task.CompletedTask;
    }

    private static Dictionary<OriginKind, Func<IEnumerable<string>>> BuildSources(TemplateTable templates, TradersTable traders)
    {
        var sources = new Dictionary<OriginKind, Func<IEnumerable<string>>>
        {
            [OriginKind.Item] = () => templates.Items.Keys.Select(id => id.ToString()),
            [OriginKind.Quest] = () => templates.Quests.Keys.Select(id => id.ToString()),
            [OriginKind.Achievement] = () =>
                (templates.Achievements ?? []).Concat(templates.CustomAchievements ?? []).Select(a => a.Id.ToString()),
            [OriginKind.Customization] = () => templates.Customization.Keys.Select(id => id.ToString()),
        };

        if (traders is not null)
        {
            sources[OriginKind.Assort] = () =>
                traders.Values
                    .Where(t => t?.Assort?.Items is not null)
                    .SelectMany(t => t.Assort.Items)
                    .Where(i => i.ParentId == "hideout")
                    .Select(i => i.Id.ToString());
        }

        return sources;
    }

    private static void WatchLoadSteps(IServiceCollection serviceCollection)
    {
        var descriptor = serviceCollection.FirstOrDefault(d => d.ServiceType == typeof(IReadOnlyList<DependencyInjectionContainer>));
        if (descriptor?.ImplementationInstance is not IReadOnlyList<DependencyInjectionContainer> steps)
        {
            ProvenanceTracker.Stop();
            Log("Could not find SPT's load step list; load-order tracking unavailable.");
            return;
        }

        serviceCollection.Remove(descriptor);
        serviceCollection.AddSingleton<IReadOnlyList<DependencyInjectionContainer>>(new LoadStepList(steps));
        Tracking = true;
        Log($"Watching {steps.Count} load steps for {_modAssemblies.Count} mod assemblies. No code is patched.");
    }

    private static IEnumerable<Assembly> ModAssemblies(Assembly self)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, self) || assembly.IsDynamic)
            {
                continue;
            }

            string location;
            try
            {
                location = assembly.Location;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(location))
            {
                continue;
            }

            var normalised = location.Replace('\\', '/');
            if (normalised.Contains("/user/mods/", StringComparison.OrdinalIgnoreCase))
            {
                yield return assembly;
            }
        }
    }
}
