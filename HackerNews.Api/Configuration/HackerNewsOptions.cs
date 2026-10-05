namespace HackerNews.Api.Configuration;

public sealed class HackerNewsOptions
{
    public const string SectionName = "HackerNews";

    public required string BaseUrl { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    // Second barrier against ephemeral port exhaustion, on top of IHttpClientFactory pooling.
    public int MaxConnectionsPerServer { get; init; } = 20;

    // Used by Parallel.ForEachAsync when fetching story details. Never the full ~200 at once.
    public int MaxDegreeOfParallelism { get; init; } = 10;

    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMinutes(5);

    public int MaxStoriesPerRequest { get; init; } = 200;
}
