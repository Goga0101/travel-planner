using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TravelPlanner.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ApiValidationAndErrorTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Invalid_Search_Returns_400_With_Field_Errors()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", new
        {
            originAirportIata = "XXX",
            earliestDeparture = "2020-01-01",
            latestReturn = "2019-12-01",
            durationDays = 1,
            travelers = 0,
            budgetAmount = -1,
            budgetCurrency = "GEL",
            destinationMode = "Manual"
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var errors = problem.GetProperty("errors");
        foreach (var field in new[] { "originAirportIata", "earliestDeparture", "latestReturn", "durationDays", "travelers", "budgetAmount", "budgetCurrency", "destinationIds" })
            Assert.True(errors.TryGetProperty(field, out _), $"Missing validation error for {field}");
        Assert.DoesNotContain("   at ", problem.GetRawText()); // no stack traces
    }

    [Fact]
    public async Task Window_Shorter_Than_Duration_Is_Rejected()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var start = Today.AddDays(20);

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", new
        {
            originAirportIata = "TBS", earliestDeparture = start.ToString("yyyy-MM-dd"), latestReturn = start.AddDays(3).ToString("yyyy-MM-dd"),
            durationDays = 5, travelers = 1, budgetAmount = 1000, budgetCurrency = "EUR", destinationMode = "Automatic"
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains("shorter than the trip duration", problem.GetProperty("errors").GetProperty("latestReturn").GetRawText());
    }

    [Fact]
    public async Task Unknown_Manual_Destination_Is_Rejected()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", SearchBody("Manual", [Guid.NewGuid()]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_Json_Returns_400()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/trip-searches", new StringContent("{oops", Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/trip-searches/{0}")]
    [InlineData("/api/v1/trip-searches/{0}/plans")]
    [InlineData("/api/v1/trip-plans/{0}")]
    [InlineData("/api/v1/trip-plans/{0}/outbound-links")]
    [InlineData("/api/v1/destinations/{0}")]
    public async Task Unknown_Resources_Return_404_ProblemDetails(string template)
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(string.Format(template, Guid.NewGuid()), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Invalid_Paging_Is_Rejected()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/destinations?pageSize=1000", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_Creation_Is_Rate_Limited()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?> { ["RateLimiting:SearchesPerMinute"] = "2" });
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/v1/trip-searches", SearchBody(), Ct)).StatusCode);

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Health_And_Security_Headers()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live", Ct);
        var ready = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("nosniff", live.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", live.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Cors_Allows_Only_Configured_Origin()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        HttpRequestMessage Preflight(string origin)
        {
            var m = new HttpRequestMessage(HttpMethod.Options, "/api/v1/trip-searches");
            m.Headers.Add("Origin", origin);
            m.Headers.Add("Access-Control-Request-Method", "POST");
            return m;
        }

        var allowed = await client.SendAsync(Preflight("http://localhost:4200"), Ct);
        var denied = await client.SendAsync(Preflight("https://evil.example"), Ct);

        Assert.True(allowed.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
