using System.Threading.Tasks;
using SPTarkov.Server.Core.DI;

namespace ModSource.Server;

public sealed class TrackedLoadStep(IOnLoad inner) : IOnLoad
{
    public IOnLoad Inner => inner;

    public async Task OnLoad()
    {
        var owner = inner.GetType();
        ProvenanceTracker.Begin(owner);

        try
        {
            await inner.OnLoad();
        }
        finally
        {
            ProvenanceTracker.End(owner);
        }
    }
}
