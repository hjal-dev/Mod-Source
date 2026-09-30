using System;
using System.Collections.Generic;
using System.Reflection;

namespace ModSource.Server;

public enum OriginKind
{
    Item,
    Quest,
    Achievement,
    Customization,
    Assort,
}

public static class ProvenanceTracker
{
    private static Dictionary<OriginKind, Func<IEnumerable<string>>> _sources = [];
    private static Action<string> _debug;

    private static readonly object Gate = new();

    private static readonly Dictionary<Type, Dictionary<OriginKind, HashSet<string>>> Snapshots = [];

    private static readonly Dictionary<Assembly, Dictionary<OriginKind, HashSet<string>>> Created = [];

    public static bool Active { get; private set; }

    public static IReadOnlyDictionary<Assembly, Dictionary<OriginKind, HashSet<string>>> Results => Created;

    public static void Initialize(Dictionary<OriginKind, Func<IEnumerable<string>>> sources, Action<string> debug)
    {
        _sources = sources;
        _debug = debug;
        Active = true;
    }

    public static Dictionary<OriginKind, HashSet<string>> Capture()
    {
        var snapshot = new Dictionary<OriginKind, HashSet<string>>();
        foreach (var (kind, source) in _sources)
        {
            try
            {
                snapshot[kind] = [.. source()];
            }
            catch (Exception ex)
            {
                _debug?.Invoke($"Could not read {kind} ids: {ex.Message}");
                snapshot[kind] = [];
            }
        }

        return snapshot;
    }

    public static void Begin(Type owner)
    {
        if (!Active)
        {
            return;
        }

        var snapshot = Capture();
        lock (Gate)
        {
            Snapshots[owner] = snapshot;
        }
    }

    public static void End(Type owner)
    {
        if (Active)
        {
            Diff(owner);
        }
    }

    public static void Stop()
    {
        Active = false;
    }

    private static void Diff(Type owner)
    {
        try
        {
            Dictionary<OriginKind, HashSet<string>> before;
            lock (Gate)
            {
                if (!Snapshots.Remove(owner, out before))
                {
                    return;
                }
            }

            var after = Capture();

            lock (Gate)
            {
                if (!Created.TryGetValue(owner.Assembly, out var perKind))
                {
                    perKind = [];
                    Created[owner.Assembly] = perKind;
                }

                foreach (var (kind, ids) in after)
                {
                    var previous = before.GetValueOrDefault(kind) ?? [];
                    foreach (var id in ids)
                    {
                        if (previous.Contains(id))
                        {
                            continue;
                        }

                        if (!perKind.TryGetValue(kind, out var created))
                        {
                            created = [];
                            perKind[kind] = created;
                        }

                        created.Add(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _debug?.Invoke($"Failed to diff after {owner.FullName}: {ex.Message}");
        }
    }
}
