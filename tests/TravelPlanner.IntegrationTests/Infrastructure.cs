using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TravelPlanner.Infrastructure.Persistence;

namespace TravelPlanner.IntegrationTests;

/// <summary>
/// Starts a throw-away PostgreSQL container. Requires Docker; when Docker is unavailable every test is skipped
/// (never silently passed). No paid or external APIs are used: the app runs in Demo provider mode.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string? ConnectionString { get; private set; }
    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            SkipReason = $"Docker/Testcontainers is not available: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "api";
}

public sealed class TestAppFactory(string connectionString, IDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TravelPlanner", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Catalog:SeedOnStartup", "true");
        builder.UseSetting("Providers:Mode", "Demo");
        builder.UseSetting("RateLimiting:SearchesPerMinute", "1000");
        builder.UseSetting("RateLimiting:GlobalPerMinute", "10000");
        builder.UseSetting("RateLimiting:RefreshesPerMinute", "1000");
        builder.UseSetting("ExchangeRates:Enabled", "false");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);
    }

    public AppDbContext CreateDbContext() => Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
}

public abstract class ApiTestBase(PostgresFixture fixture)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected TestAppFactory CreateFactory(IDictionary<string, string?>? overrides = null)
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? "");
        return new TestAppFactory(fixture.ConnectionString!, overrides);
    }

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    protected static object SearchBody(string mode = "Automatic", Guid[]? destinationIds = null, decimal budget = 1000m, int duration = 5) => new
    {
        originAirportIata = "TBS",
        earliestDeparture = Today.AddDays(30).ToString("yyyy-MM-dd"),
        latestReturn = Today.AddDays(40).ToString("yyyy-MM-dd"),
        durationDays = duration,
        travelers = 1,
        budgetAmount = budget,
        budgetCurrency = "EUR",
        destinationMode = mode,
        destinationIds,
        preferences = new { includeCheckedBaggage = false, preferredDepartureTime = "Any" }
    };

    protected static async Task<JsonElement> WaitForCompletionAsync(HttpClient client, string searchId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var search = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-searches/{searchId}", Ct);
            if (search.GetProperty("isFinished").GetBoolean()) return search;
            await Task.Delay(250, Ct);
        }
        throw new TimeoutException($"Search {searchId} did not finish in time.");
    }
}
