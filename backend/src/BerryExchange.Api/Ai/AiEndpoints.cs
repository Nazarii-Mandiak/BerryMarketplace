using BerryExchange.AiCore;

namespace BerryExchange.Api.Ai;

public static class AiEndpoints
{
    public static void MapAiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ai");
        group.MapGet("/status", (IGenerativeAi ai) => Results.Ok(new { enabled = ai.IsEnabled }));
    }
}
