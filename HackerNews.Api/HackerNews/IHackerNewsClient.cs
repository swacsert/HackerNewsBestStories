namespace HackerNews.Api.HackerNews;

public interface IHackerNewsClient
{
    Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default);
    Task<HackerNewsItem?> GetStoryAsync(int id, CancellationToken cancellationToken = default);
}
