using HackerNews.Api.Configuration;
using HackerNews.Api.HackerNews;
using Microsoft.Extensions.Options;

namespace HackerNews.Api.Stories;

public sealed class BestStoriesRefresher : BackgroundService
{
    private readonly IHackerNewsClient _client;
    private readonly BestStoriesProvider _provider;
    private readonly HackerNewsOptions _options;
    private readonly ILogger<BestStoriesRefresher> _logger;

    // Guards the cold-start load only. Periodic refreshes always run unconditionally,
    // so they don't need this gate.
    private readonly SemaphoreSlim _coldStartGate = new(1, 1);

    public BestStoriesRefresher(
        IHackerNewsClient client,
        BestStoriesProvider provider,
        IOptions<HackerNewsOptions> options,
        ILogger<BestStoriesRefresher> logger)
    {
        _client = client;
        _provider = provider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureInitializedAsync(stoppingToken);

        // WaitForNextTickAsync returns false once stoppingToken is cancelled, so this exits
        // cleanly on shutdown. It also never queues up missed ticks while a refresh is slow.
        using var timer = new PeriodicTimer(_options.RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    // If several requests raced in during cold start, only the first one actually loads from
    // Hacker News. The rest find the snapshot already there once they get the gate and skip.
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_provider.HasSnapshot)
        {
            return;
        }

        await _coldStartGate.WaitAsync(cancellationToken);
        try
        {
            if (_provider.HasSnapshot)
            {
                return;
            }

            await RefreshAsync(cancellationToken);
        }
        finally
        {
            _coldStartGate.Release();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await BuildSnapshotAsync(cancellationToken);
            _provider.Replace(snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A bad cycle shouldn't take the previous good snapshot down with it.
            _logger.LogWarning(ex, "Failed to refresh best stories from Hacker News.");
        }
    }

    private async Task<StoriesSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        var ids = await _client.GetBestStoryIdsAsync(cancellationToken);
        var items = new HackerNewsItem?[ids.Count];

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        // Each iteration writes its own index, so no locking is needed to collect the results.
        await Parallel.ForEachAsync(Enumerable.Range(0, ids.Count), parallelOptions, async (index, ct) =>
        {
            items[index] = await _client.GetStoryAsync(ids[index], ct);
        });

        var stories = items
            .Where(item => item is not null)
            .Select(item => StoryMapper.Map(item!))
            .OrderByDescending(story => story.Score)
            .ToArray();

        return new StoriesSnapshot(stories, DateTimeOffset.UtcNow);
    }
}
