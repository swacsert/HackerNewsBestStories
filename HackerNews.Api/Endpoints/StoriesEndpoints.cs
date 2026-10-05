using HackerNews.Api.Configuration;
using HackerNews.Api.Stories;
using Microsoft.Extensions.Options;

namespace HackerNews.Api.Endpoints;

public static class StoriesEndpoints
{
    public static void MapStoriesEndpoints(this WebApplication app)
    {
        app.MapGet("/stories", GetBestStories);
    }

    private static IResult GetBestStories(
        int? n,
        HttpContext context,
        IBestStoriesProvider provider,
        IOptions<HackerNewsOptions> options)
    {
        var requested = n ?? 10;
        if (requested <= 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid value for 'n'",
                detail: "'n' must be greater than zero.");
        }

        var count = Math.Min(requested, options.Value.MaxStoriesPerRequest);
        var stories = provider.GetTopStories(count);

        if (stories is null)
        {
            // Cold start: the first background refresh hasn't completed yet. Honest 503
            // instead of blocking the request or triggering a fetch from here.
            context.Response.Headers["Retry-After"] = "5";
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Not ready",
                detail: "The best stories cache has not completed its first refresh yet.");
        }

        return Results.Ok(stories);
    }
}
