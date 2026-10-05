using HackerNews.Api.Contracts;
using HackerNews.Api.HackerNews;

namespace HackerNews.Api.Stories;

public static class StoryMapper
{
    public static StoryDto Map(HackerNewsItem item) => new()
    {
        Title = item.Title,
        Uri = item.Url,
        PostedBy = item.By,
        Time = DateTimeOffset.FromUnixTimeSeconds(item.Time),
        Score = item.Score,
        CommentCount = item.Descendants
    };
}
