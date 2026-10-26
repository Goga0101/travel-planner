using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TripSearchWorkflowTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Catalog_Is_Seeded_And_Searchable()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/destinations?pageSize=100", Ct);
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 15);

        var search = await client.GetFromJsonAsync<JsonElement>("/api/v1/destinations?search=vien", Ct);
        var vienna = Assert.Single(search.GetProperty("items").EnumerateArray());
        Assert.Equal("vienna", vienna.GetProperty("code").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/destinations/{vienna.GetProperty("id").GetString()}", Ct);
        Assert.Equal("VIE", detail.GetProperty("airports")[0].GetProperty("iataCode").GetString());
        Assert.All(detail.GetProperty("transportLinks").EnumerateArray(), l => Assert.StartsWith("https://", l.GetProperty("url").GetString()));

        var airports = await client.GetFromJsonAsync<JsonElement>("/api/v1/airports?departureOnly=true", Ct);
        Assert.Contains(airports.EnumerateArray(), a => a.GetProperty("iataCode").GetString() == "TBS");
    }

    [Fact]
    public async Task Full_Search_Workflow_Returns_Labelled_Demo_Plans_With_Budget_And_Links()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", SearchBody(), Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Demo", created.GetProperty("dataMode").GetString());
        var searchId = created.GetProperty("id").GetString()!;

        var search = await WaitForCompletionAsync(client, searchId);
        Assert.Equal("Completed", search.GetProperty("status").GetString());
        Assert.Equal(100, search.GetProperty("progressPercent").GetInt32());

        var plans = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-searches/{searchId}/plans", Ct);
        var items = plans.GetProperty("items").EnumerateArray().ToList();
        Assert.InRange(items.Count, 1, 5);
        Assert.Equal(Enumerable.Range(1, items.Count), items.Select(p => p.GetProperty("rank").GetInt32()));
        Assert.All(items, p =>
        {
            Assert.True(p.GetProperty("isDemo").GetBoolean());
            Assert.Equal(4, p.GetProperty("nights").GetInt32());
            Assert.Contains(p.GetProperty("budgetFit").GetString(), new[] { "WithinBudget", "WithinBudgetIncomplete" });
            Assert.True(p.GetProperty("totalEstimatedCost").GetDecimal() <= 1000m);
            Assert.Equal(1000m - p.GetProperty("totalEstimatedCost").GetDecimal(), p.GetProperty("remainingBudget").GetDecimal());
            Assert.All(p.GetProperty("sources").EnumerateArray(), s => Assert.Equal("Demo", s.GetProperty("verification").GetString()));
        });

        var sorted = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-searches/{searchId}/plans?sort=TotalCost", Ct);
        var costs = sorted.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("totalEstimatedCost").GetDecimal()).ToList();
        Assert.Equal(costs.Order(), costs);

        var planId = items[0].GetProperty("id").GetString();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-plans/{planId}", Ct);
        var lines = detail.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(lines, i => i.GetProperty("type").GetString() == "Flight");
        Assert.Contains(lines, i => i.GetProperty("type").GetString() == "Accommodation");
        Assert.Equal(2, lines.Count(i => i.GetProperty("type").GetString() == "Transfer"));
        var included = lines.Where(i => i.GetProperty("includedInTotal").GetBoolean() && i.TryGetProperty("convertedPrice", out _))
            .Sum(i => i.GetProperty("convertedPrice").GetDecimal());
        Assert.Equal(detail.GetProperty("summary").GetProperty("totalEstimatedCost").GetDecimal(), included);
        Assert.Equal(included, detail.GetProperty("costBreakdown").EnumerateArray().Sum(c => c.GetProperty("amount").GetDecimal()));

        var links = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-plans/{planId}/outbound-links", Ct);
        Assert.NotEmpty(links.EnumerateArray());
        Assert.All(links.EnumerateArray(), l => Assert.StartsWith("https://", l.GetProperty("url").GetString()));

        var refresh = await client.PostAsync($"/api/v1/trip-plans/{planId}/refresh-prices", null, Ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = await refresh.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(refreshed.GetProperty("summary").TryGetProperty("pricesRefreshedAtUtc", out _));
        Assert.Equal(lines.Count, refreshed.GetProperty("items").GetArrayLength());

        var cancelFinished = await client.PostAsync($"/api/v1/trip-searches/{searchId}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cancelFinished.StatusCode);
    }

    [Fact]
    public async Task Manual_Mode_Plans_Only_Selected_Destinations_And_Persists_Correctly()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var destinations = await client.GetFromJsonAsync<JsonElement>("/api/v1/destinations?search=prague", Ct);
        var pragueId = Guid.Parse(destinations.GetProperty("items")[0].GetProperty("id").GetString()!);

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", SearchBody("Manual", [pragueId]), Ct);
        var searchId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        var search = await WaitForCompletionAsync(client, searchId);

        Assert.Equal("Manual", search.GetProperty("criteria").GetProperty("destinationMode").GetString());
        Assert.Equal("Prague", search.GetProperty("criteria").GetProperty("destinations")[0].GetProperty("city").GetString());

        await using var db = factory.CreateDbContext();
        var plans = await db.TripPlans.Include(p => p.Items).Where(p => p.TripSearchId == Guid.Parse(searchId)).ToListAsync(Ct);
        Assert.NotEmpty(plans);
        Assert.All(plans, p =>
        {
            Assert.Equal(pragueId, p.DestinationId);
            Assert.Equal(4, p.Nights);
            Assert.All(p.Items, i =>
            {
                Assert.Equal(TimeSpan.Zero, i.RetrievedAtUtc.Offset);
                Assert.False(string.IsNullOrEmpty(i.DetailsJson));
            });
        });
        var stored = await db.TripSearches.AsNoTracking().SingleAsync(s => s.Id == Guid.Parse(searchId), Ct);
        Assert.Equal(DepartureTimePreference.Any, stored.Preferences.PreferredDepartureTime);
        Assert.Equal(1000m, stored.BudgetAmount);
    }

    [Fact]
    public async Task Tiny_Budget_Completes_As_No_Match_With_Over_Budget_Options()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", SearchBody(budget: 40m), Ct);
        var searchId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        var search = await WaitForCompletionAsync(client, searchId);

        Assert.Equal("CompletedNoMatch", search.GetProperty("status").GetString());
        var plans = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-searches/{searchId}/plans", Ct);
        Assert.All(plans.GetProperty("items").EnumerateArray(), p => Assert.Equal("OverBudget", p.GetProperty("budgetFit").GetString()));
    }

    [Fact]
    public async Task Live_Mode_Without_Providers_Fails_Clearly_Instead_Of_Inventing_Results()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?> { ["Providers:Mode"] = "Live" });
        var client = factory.CreateClient();

        var config = await client.GetFromJsonAsync<JsonElement>("/api/v1/client-config", Ct);
        Assert.Equal("Live", config.GetProperty("dataMode").GetString());
        // Every provider that needs credentials is inactive. The keyless hotel-price estimate feed (Xotelo) stays on: it never
        // produces offers or plans, only indicative prices next to existing plans.
        Assert.All(config.GetProperty("providers").EnumerateArray().Where(p => !p.GetProperty("name").GetString()!.StartsWith("Xotelo")),
            p => Assert.False(p.GetProperty("active").GetBoolean()));

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", SearchBody(), Ct);
        var searchId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        var search = await WaitForCompletionAsync(client, searchId);

        Assert.Equal("Failed", search.GetProperty("status").GetString());
        Assert.Contains("No flight data provider", search.GetProperty("errorMessage").GetString());
        var plans = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-searches/{searchId}/plans", Ct);
        Assert.Equal(0, plans.GetProperty("totalCount").GetInt32());
    }
}
