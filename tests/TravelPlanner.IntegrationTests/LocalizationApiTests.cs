using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelPlanner.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class LocalizationApiTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private static HttpClient Georgian(TestAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ka");
        return client;
    }

    [Fact]
    public async Task Catalog_Is_Returned_In_Georgian_And_Searchable_By_Georgian_Name()
    {
        await using var factory = CreateFactory();
        var client = Georgian(factory);

        var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/destinations?search=ვენა", Ct);
        var vienna = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal("ვენა", vienna.GetProperty("city").GetString());
        Assert.Equal("ავსტრია", vienna.GetProperty("country").GetString());

        var airports = await client.GetFromJsonAsync<JsonElement>("/api/v1/airports?departureOnly=true", Ct);
        var tbs = airports.EnumerateArray().Single(a => a.GetProperty("iataCode").GetString() == "TBS");
        Assert.Equal("თბილისის საერთაშორისო აეროპორტი", tbs.GetProperty("name").GetString());
        Assert.Equal("თბილისი", tbs.GetProperty("city").GetString());

        var english = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/destinations?search=vienna", Ct);
        Assert.Equal("Vienna", english.GetProperty("items")[0].GetProperty("city").GetString());
    }

    [Fact]
    public async Task Validation_And_NotFound_Messages_Are_Georgian()
    {
        await using var factory = CreateFactory();
        var client = Georgian(factory);

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", SearchBody(budget: -1), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("მონაცემები შეიცავს შეცდომებს.", problem.GetProperty("title").GetString());
        Assert.Equal("ბიუჯეტი 0-ზე მეტი უნდა იყოს.", problem.GetProperty("errors").GetProperty("budgetAmount")[0].GetString());

        var missing = await client.GetAsync($"/api/v1/trip-plans/{Guid.NewGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var notFound = await missing.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("ვერ მოიძებნა", notFound.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Search_Created_In_Georgian_Generates_Georgian_Texts()
    {
        await using var factory = CreateFactory();
        var client = Georgian(factory);

        var response = await client.PostAsJsonAsync("/api/v1/trip-searches", SearchBody(), Ct);
        var searchId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        var search = await WaitForCompletionAsync(client, searchId);

        Assert.Equal("ძიება დასრულდა", search.GetProperty("progressMessage").GetString());
        var plans = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-searches/{searchId}/plans", Ct);
        var planId = plans.GetProperty("items")[0].GetProperty("id").GetString();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-plans/{planId}", Ct);
        var flight = detail.GetProperty("items").EnumerateArray().First(i => i.GetProperty("type").GetString() == "Flight");
        Assert.StartsWith("ორმხრივი ფრენა", flight.GetProperty("title").GetString());

        var config = await client.GetFromJsonAsync<JsonElement>("/api/v1/client-config", Ct);
        Assert.Equal("ფრენები", config.GetProperty("providers")[0].GetProperty("category").GetString());
    }
}
