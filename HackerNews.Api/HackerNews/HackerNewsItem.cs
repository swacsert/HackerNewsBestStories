using System.Text.Json.Serialization;

namespace HackerNews.Api.HackerNews;

// Raw shape of a Hacker News item, as the Firebase API names its fields.
// Explicit JsonPropertyName so this keeps deserializing correctly regardless
// of whatever JsonSerializerOptions the HttpClient ends up using.
public sealed record HackerNewsItem
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("by")]
    public string By { get; init; } = string.Empty;

    [JsonPropertyName("time")]
    public long Time { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("descendants")]
    public int Descendants { get; init; }
}
