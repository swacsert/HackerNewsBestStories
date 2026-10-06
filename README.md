# Hacker News — Best Stories API

*[Versión en español](README.es.md)*

An ASP.NET Core API that returns the best `n` Hacker News stories, ordered by score descending.

The interesting part of this problem is not calling the Hacker News API — it is calling it *as
little as possible*. `beststories.json` returns around 200 story IDs, and each story detail is a
separate request. A naive implementation turns **one call to this API into ~201 calls to Hacker
News**; ten concurrent users turn it into ~2,000.

---

## Running it

**Prerequisites:** .NET 10 SDK.

```bash
git clone https://github.com/swacsert/HackerNewsBestStories.git
cd HackerNewsBestStories
dotnet run --project HackerNews.Api
```

The console prints the URL it is listening on (also in `HackerNews.Api/Properties/launchSettings.json`).

The default profile listens on http://localhost:5283.

```bash
curl "http://localhost:5283/stories?n=3"
```

```json
[
  {
    "title": "Tell HN: Bob Cringely has died",
    "uri": null,
    "postedBy": "paveworld",
    "time": "2026-10-04T00:50:52+00:00",
    "score": 926,
    "commentCount": 206
  }
]
```

**Tests:**

```bash
dotnet test
```

---

## How it works

The cache is refreshed **proactively**, not on demand.

A `BackgroundService` fetches the best stories on a fixed interval and replaces an in-memory
snapshot. Incoming requests only ever read that snapshot — they never call Hacker News.

The consequence is the point of the whole design: **the load on Hacker News is constant and does
not grow with traffic.** Whether the API serves ten requests or ten thousand, Hacker News sees the
same scheduled refresh. Request latency is a memory read.

A few decisions that follow from that:

- **The snapshot is immutable and swapped whole.** The refresher builds a new, already-sorted
  snapshot and replaces the reference; readers never lock and never see a half-written state. The
  field is `volatile` so every thread sees the new reference rather than a cached one.
- **Concurrency against Hacker News is capped** (`Parallel.ForEachAsync` with a configurable degree
  of parallelism). 200 details are fetched in controlled batches, never all at once.
- **A failed refresh keeps the previous snapshot.** If Hacker News is unreachable, the API keeps
  serving the last good data and logs the failure, rather than going blank. The `try/catch` is per
  cycle, so one bad refresh does not kill the service.
- **`PeriodicTimer` drives the loop**, so a refresh cannot start while the previous one is still
  running, and the loop exits cleanly on shutdown.
- **A typed `HttpClient` via `IHttpClientFactory`**, with a cap on connections per server — handlers
  are pooled and rotated instead of creating sockets per call.
- **Cold start is handled explicitly.** Before the first refresh completes, the API returns `503`
  with a `Retry-After` header rather than blocking or triggering a fetch from the request path. The
  initial load is guarded by a semaphore with a double check so it cannot be duplicated —
  defensive, since today the refresher is its only caller.

### Project layout

```
HackerNews.Api/
  Contracts/            the response shape returned by this API
  HackerNews/           the Hacker News client and its raw response model
  Stories/              snapshot, provider, mapper and the background refresher
  Endpoints/            the HTTP surface
  Configuration/        strongly typed options
HackerNews.Api.Tests/
```

`HackerNewsItem` (what Hacker News returns) and `StoryDto` (what this API returns) are deliberately
separate types with a mapper between them. Hacker News uses `by`, `url`, `descendants` and a Unix
epoch; the contract here uses `postedBy`, `uri`, `commentCount` and ISO 8601. Keeping the upstream
shape out of the rest of the code means a change on their side touches one file.

### Configuration

All of it lives under `HackerNews` in `appsettings.json`:

| Setting | Meaning |
|---|---|
| `BaseUrl`, `RequestTimeout` | upstream endpoint and per-request timeout |
| `RefreshInterval` | how often the background refresh runs |
| `MaxDegreeOfParallelism` | how many story details are fetched concurrently |
| `MaxConnectionsPerServer` | upper bound on outbound connections |
| `MaxStoriesPerRequest` | upper bound on `n` |

---

## Assumptions

- **Staleness is acceptable.** Best stories change slowly, so data up to one refresh interval old is
  fine. The interval is configurable; the trade-off is freshness against load on Hacker News.
- **`n` is clamped, not rejected, when it is too large.** Asking for more stories than exist is not
  an error — the API returns what it has. Asking for zero or a negative number is a client error and
  returns `400` with a `ProblemDetails` body. With no `n`, it returns 10.
- **Stories without an external link return `uri: null`.** "Ask HN" and "Tell HN" posts have no
  `url` field upstream. The alternative would be to substitute the Hacker News permalink, but I
  preferred the API to report what the source actually provides rather than synthesise a value.
- **Single instance.** The cache lives in process memory, which is enough for one node.
- **No authentication or rate limiting**, since neither was part of the brief.

---

## With more time

- **Per-story resilience on refresh.** Right now a single failed story detail aborts the whole
  refresh cycle and the previous snapshot is kept. Skipping the failed story and refreshing the rest
  would be more forgiving, at the cost of a snapshot that is briefly incomplete.
- **A shared cache outside the process** (Redis or similar) if this ran on more than one instance.
  Right now each instance would keep its own copy in memory and refresh on its own, so three nodes
  means three refreshes against Hacker News instead of one.
- **Load tests.** I have reasoned about the concurrency limits but not measured them. A load test
  would tell me what the right degree of parallelism actually is rather than what I guessed.
- **A richer cold-start answer.** `Retry-After` is currently a fixed value; deriving it from the
  refresh interval and the time of the last attempt would be more honest.
- **An incremental refresh.** Every cycle currently re-fetches all ~200 stories, whether or not they
  changed. Comparing the new ID list against the one already held and fetching only the new stories
  would cut a cycle from ~201 requests to a handful. The catch is that scores move constantly: a
  story kept from a previous cycle would carry a frozen score, and the ordering — which is the one
  thing this API must get right — would drift. A hybrid would be the real answer: always fetch new
  stories, and re-fetch known ones on a slower cadence or only near the top of the ranking.
- **Persisting the stories to a database**, treating them as a catalogue rather than a cache. The
  service would survive a restart without having to rebuild everything from Hacker News, it could
  keep serving if they were unreachable, and it would open the door to history — how a story's score
  moved over time, what was at the top last week. It is more infrastructure than this brief needs,
  but it is where I would take it if the data mattered beyond the current snapshot.

---

## A note on the cold-start lock

An alternative to the semaphore is to share the in-flight `Task` itself, so every caller awaits the
same load. It is a neat solution and I considered it, but a faulted `Task` stays faulted and has to
be explicitly discarded before the next attempt. The semaphore with a double check is slightly more
code and has no such edge case, so I went with it.
