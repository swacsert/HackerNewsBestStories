using HackerNews.Api.Contracts;

namespace HackerNews.Api.Stories;

public sealed class BestStoriesProvider : IBestStoriesProvider
{
    // Swapped wholesale by the refresher; volatile so a read on any thread sees the new
    // reference immediately instead of a stale cached one.
    private volatile StoriesSnapshot? _snapshot;

    public bool HasSnapshot => _snapshot is not null;

    public void Replace(StoriesSnapshot snapshot) => _snapshot = snapshot;

    public IReadOnlyList<StoryDto>? GetTopStories(int count)
    {
        var snapshot = _snapshot;
        return snapshot?.Stories.Take(count).ToArray();
    }
}
