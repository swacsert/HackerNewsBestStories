using HackerNews.Api.Contracts;

namespace HackerNews.Api.Stories;

public sealed record StoriesSnapshot(IReadOnlyList<StoryDto> Stories, DateTimeOffset GeneratedAt);
