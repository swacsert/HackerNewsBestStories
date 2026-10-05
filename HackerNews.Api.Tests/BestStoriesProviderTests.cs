using HackerNews.Api.Contracts;
using HackerNews.Api.Stories;

namespace HackerNews.Api.Tests;

public class BestStoriesProviderTests
{
    [Fact]
    public void GetTopStories_BeforeFirstRefresh_ReturnsNull()
    {
        var provider = new BestStoriesProvider();

        var stories = provider.GetTopStories(5);

        Assert.Null(stories);
    }

    [Fact]
    public void GetTopStories_ReturnsTopNFromSnapshot()
    {
        var provider = new BestStoriesProvider();
        provider.Replace(new StoriesSnapshot(
        [
            MakeStory("high", 50),
            MakeStory("mid", 25),
            MakeStory("low", 10)
        ], DateTimeOffset.UtcNow));

        var stories = provider.GetTopStories(2);

        Assert.NotNull(stories);
        Assert.Equal(2, stories!.Count);
        Assert.Equal("high", stories[0].Title);
        Assert.Equal("mid", stories[1].Title);
    }

    [Fact]
    public void GetTopStories_WhenRequestedMoreThanAvailable_ReturnsAvailableStories()
    {
        var provider = new BestStoriesProvider();
        provider.Replace(new StoriesSnapshot(
        [
            MakeStory("one", 15),
            MakeStory("two", 7)
        ], DateTimeOffset.UtcNow));

        var stories = provider.GetTopStories(10);

        Assert.NotNull(stories);
        Assert.Equal(2, stories!.Count);
    }

    private static StoryDto MakeStory(string title, int score) => new()
    {
        Title = title,
        Uri = $"https://example.com/{title}",
        PostedBy = "someone",
        Time = DateTimeOffset.UtcNow,
        Score = score,
        CommentCount = 0
    };
}
