using HackerNews.Api.Configuration;
using HackerNews.Api.Endpoints;
using HackerNews.Api.HackerNews;
using HackerNews.Api.Stories;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.Configure<HackerNewsOptions>(
    builder.Configuration.GetSection(HackerNewsOptions.SectionName));

builder.Services.AddHttpClient<IHackerNewsClient, HackerNewsClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<HackerNewsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = options.RequestTimeout;
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<HackerNewsOptions>>().Value;
    // Second barrier against ephemeral port exhaustion, on top of IHttpClientFactory's pooling.
    return new SocketsHttpHandler
    {
        MaxConnectionsPerServer = options.MaxConnectionsPerServer
    };
});

// Registered under both types: the refresher needs the concrete class to replace the
// snapshot, the endpoint only needs the read-only interface.
builder.Services.AddSingleton<BestStoriesProvider>();
builder.Services.AddSingleton<IBestStoriesProvider>(sp => sp.GetRequiredService<BestStoriesProvider>());
builder.Services.AddHostedService<BestStoriesRefresher>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapStoriesEndpoints();

app.Run();
