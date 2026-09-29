using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Hosted;

namespace ModSource.Server;

public sealed class ModSourceBootstrap : IOnDIConstruct
{
    private static readonly List<string> Buffer = [];

    private static HashSet<Assembly> _modAssemblies = [];

    public static ModSourceConfig Config { get; private set; } = new();

    public static int DispatchSitesPatched { get; private set; }

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
                Log("Load-order tracking disabled by config; relying on CustomItemService and bundle attribution only.");
                return Task.CompletedTask;
            }

            _modAssemblies = ModAssemblies(typeof(ModSourceBootstrap).Assembly).ToHashSet();
            InstallDispatchPatches();
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

    private static void InstallDispatchPatches()
    {
        var harmony = new Harmony("com.hj.modsource.server");
        var transpiler = new HarmonyMethod(typeof(OnLoadDispatchPatch).GetMethod(nameof(OnLoadDispatchPatch.Transpiler)));

        (string Label, Type Host)[] hosts =
        [
            ("ProgramExtensions (pre-SPT load)", AccessTools.TypeByName("SPTarkov.Server.Extensions.ProgramExtensions")),
            ("SPTStartupHostedService (main load)", typeof(SPTStartupHostedService)),
        ];

        var patched = 0;
        foreach (var (label, host) in hosts)
        {
            if (host is null)
            {
                Log($"Could not locate {label}; mods loaded in that phase will not be tracked.");
                continue;
            }

            var sites = FindDispatchSites(host).ToList();
            if (sites.Count == 0)
            {
                Log($"No IOnLoad.OnLoadAsync call found in {label}; mods loaded in that phase will not be tracked.");
                continue;
            }

            foreach (var moveNext in sites)
            {
                patched += PatchDispatch(harmony, transpiler, $"{label} {moveNext.DeclaringType?.Name}", moveNext);
            }
        }

        DispatchSitesPatched = patched;
    }

    private static IEnumerable<MethodInfo> FindDispatchSites(Type host)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (var method in host.GetMethods(all))
        {
            var moveNext = method.GetCustomAttribute<AsyncStateMachineAttribute>()
                ?.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (moveNext is null)
            {
                continue;
            }

            List<CodeInstruction> body;
            try
            {
                body = PatchProcessor.GetOriginalInstructions(moveNext);
            }
            catch
            {
                continue;
            }

            if (body.Any(i => i.operand is MethodInfo called && called.Equals(OnLoadDispatchPatch.Target)))
            {
                yield return moveNext;
            }
        }
    }

    private static int PatchDispatch(Harmony harmony, HarmonyMethod transpiler, string label, MethodInfo moveNext)
    {
        try
        {
            OnLoadDispatchPatch.Replaced = 0;
            harmony.Patch(moveNext, transpiler: transpiler);

            if (OnLoadDispatchPatch.Replaced == 0)
            {
                Log($"No IOnLoad.OnLoadAsync call found in {label}; mods loaded in that phase will not be tracked.");
                return 0;
            }

            Log($"Hooked IOnLoad dispatch in {label}.");
            return 1;
        }
        catch (Exception ex)
        {
            Log($"Could not hook {label}: {ex.Message}");
            return 0;
        }
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

public static class OnLoadDispatchPatch
{
    internal static int Replaced;

    internal static readonly MethodInfo Target = AccessTools.Method(typeof(IOnLoad), nameof(IOnLoad.OnLoadAsync));
    private static readonly MethodInfo Wrapper = AccessTools.Method(typeof(OnLoadDispatchPatch), nameof(Invoke));

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call)
                && instruction.operand is MethodInfo method
                && method.Equals(Target))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = Wrapper;
                Replaced++;
            }

            yield return instruction;
        }
    }

    public static Task Invoke(IOnLoad onLoad, CancellationToken cancellationToken)
    {
        var type = onLoad.GetType();
        if (!ModSourceBootstrap.IsTrackedModType(type))
        {
            return onLoad.OnLoadAsync(cancellationToken);
        }

        ProvenanceTracker.Begin(type);

        Task task;
        try
        {
            task = onLoad.OnLoadAsync(cancellationToken);
        }
        catch
        {
            ProvenanceTracker.Complete(type, null);
            throw;
        }

        return ProvenanceTracker.Complete(type, task);
    }
}
