using HackerNews.Api.HackerNews;
using HackerNews.Api.Stories;

namespace HackerNews.Api.Tests;

public class StoryMapperTests
{
    [Fact]
    public void Map_MapsHackerNewsFieldsToDto()
    {
        var item = new HackerNewsItem
        {
            Title = "A uBlock Origin update was rejected from the Chrome Web Store",
            Url = "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
            By = "ismaildonmez",
            Time = 1570443781,
            Score = 1716,
            Descendants = 572
        };

        var dto = StoryMapper.Map(item);

        Assert.Equal("A uBlock Origin update was rejected from the Chrome Web Store", dto.Title);
        Assert.Equal("https://github.com/uBlockOrigin/uBlock-issues/issues/745", dto.Uri);
        Assert.Equal("ismaildonmez", dto.PostedBy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1570443781), dto.Time);
        Assert.Equal(1716, dto.Score);
        Assert.Equal(572, dto.CommentCount);
    }

    [Fact]
    public void Map_AllowsNullUrl()
    {
        var item = new HackerNewsItem
        {
            Title = "Discussion",
            Url = null,
            By = "alice",
            Time = 1570443781,
            Score = 10,
            Descendants = 3
        };

        var dto = StoryMapper.Map(item);

        Assert.Equal("Discussion", dto.Title);
        Assert.Null(dto.Uri);
    }
}
