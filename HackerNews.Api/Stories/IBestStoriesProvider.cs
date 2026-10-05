using HackerNews.Api.Contracts;

namespace HackerNews.Api.Stories;

public interface IBestStoriesProvider
{
    // Null means the first refresh hasn't completed yet (cold start) — not an empty result.
    IReadOnlyList<StoryDto>? GetTopStories(int count);
}
