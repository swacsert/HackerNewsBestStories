namespace HackerNews.Api.HackerNews;

public sealed class HackerNewsClient : IHackerNewsClient
{
    private readonly HttpClient _httpClient;

    public HackerNewsClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _httpClient.GetFromJsonAsync<int[]>("beststories.json", cancellationToken);
        return ids ?? [];
    }

    public Task<HackerNewsItem?> GetStoryAsync(int id, CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<HackerNewsItem?>($"item/{id}.json", cancellationToken);
    }
}
