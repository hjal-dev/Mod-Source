using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace ModSource.Server;

[Injectable]
public class ModSourceCallbacks(HttpResponseUtil httpResponseUtil, ModSourceService modSourceService)
{
    public ValueTask<string> GetDatabase(string url, EmptyRequestData _, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponseUtil.NoBody(modSourceService.Payload));
    }
}

[Injectable]
public class ModSourceStaticRouter(JsonUtil jsonUtil, ModSourceCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<EmptyRequestData>(
                "/modsource/db",
                async (url, info, sessionId, output) => await callbacks.GetDatabase(url, info, sessionId)
            ),
        ]
    ) { }
