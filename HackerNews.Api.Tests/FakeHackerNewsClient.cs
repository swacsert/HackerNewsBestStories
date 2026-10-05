using HackerNews.Api.HackerNews;

namespace HackerNews.Api.Tests;

public sealed class FakeHackerNewsClient : IHackerNewsClient
{
    private readonly Dictionary<int, HackerNewsItem> _items;

    public FakeHackerNewsClient(IEnumerable<HackerNewsItem> items)
    {
        _items = items.ToDictionary(item => item.Id);
    }

    public Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = _items.Keys
            .OrderByDescending(id => _items[id].Score)
            .ToArray();

        return Task.FromResult<IReadOnlyList<int>>(ids);
    }

    public Task<HackerNewsItem?> GetStoryAsync(int id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_items.TryGetValue(id, out var story) ? story : null);
    }
}
