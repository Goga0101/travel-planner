using System.Globalization;
using System.Text.RegularExpressions;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;
using static TravelPlanner.UnitTests.TestData;

namespace TravelPlanner.UnitTests;

public partial class LocalizationTests
{
    [Fact]
    public void English_And_Georgian_Have_The_Same_Keys()
    {
        var en = T.KeysFor("en").ToHashSet();
        var ka = T.KeysFor("ka").ToHashSet();

        Assert.Empty(en.Except(ka));
        Assert.Empty(ka.Except(en));
    }

    [Fact]
    public void Translations_Use_The_Same_Placeholders()
    {
        foreach (var key in T.KeysFor("en"))
        {
            using (T.Use("en")) Assert.Equal(Placeholders(Raw(key)), Placeholders(RawKa(key)));
        }

        static string Raw(string key) { using (T.Use("en")) return T.Get(key); }
        static string RawKa(string key) { using (T.Use("ka")) return T.Get(key); }
        static string Placeholders(string s) => string.Join(",", PlaceholderRegex().Matches(s).Select(m => m.Groups[1].Value).Distinct().Order());
    }

    [Fact]
    public void Use_Switches_And_Restores_Language()
    {
        Assert.Equal("Search completed", T.Get("progress.completed"));
        using (T.Use("ka"))
        {
            Assert.Equal("ka", T.CurrentLanguage);
            Assert.Equal("ძიება დასრულდა", T.Get("progress.completed"));
            Assert.Equal("1 გადაჯდომა", T.Stops(1));
        }
        Assert.Equal("en", T.CurrentLanguage);
    }

    [Theory]
    [InlineData("ka", "ka")]
    [InlineData("ka-GE", "ka")]
    [InlineData("en-US", "en")]
    [InlineData("de", "en")]
    [InlineData(null, "en")]
    public void Unsupported_Languages_Fall_Back_To_English(string? input, string expected) =>
        Assert.Equal(expected, T.Normalize(input));

    [Fact]
    public void Catalog_Translations_Fall_Back_To_English()
    {
        var vienna = new Destination("vienna", "Vienna", "AT", "Austria", 48.2, 16.37)
        {
            Translations = { ["ka"] = new DestinationTranslation { City = "ვენა", Country = "ავსტრია" } }
        };
        var airport = new Airport("VIE", "Vienna International Airport", "Vienna", "AT", 48.1, 16.5)
        {
            Translations = { ["ka"] = new AirportTranslation { Name = "ვენის საერთაშორისო აეროპორტი" } }
        };

        Assert.Equal("ვენა", vienna.CityIn("ka"));
        Assert.Equal("Vienna", vienna.CityIn("en"));
        Assert.Null(vienna.DescriptionIn("ka"));
        Assert.Equal("ვენის საერთაშორისო აეროპორტი", airport.NameIn("ka"));
        Assert.Equal("Vienna", airport.CityIn("ka")); // no translation -> English
    }

    [Fact]
    public async Task Planning_In_Georgian_Produces_Georgian_Texts()
    {
        var flights = new FakeFlightProvider(r => [Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 200)]);
        var engine = EngineFactory.Create([flights]);
        var progress = new List<string>();

        PlanningResult result;
        using (T.Use("ka"))
        {
            result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]),
                p => { lock (progress) progress.Add(p.Message); return Task.CompletedTask; }, CancellationToken.None);
        }

        var plan = result.Plans[0];
        Assert.StartsWith("ორმხრივი ფრენა", plan.Drafts.Single(d => d.Type == PlanItemType.Flight).Title);
        Assert.Equal("განთავსება მიუწვდომელია", plan.Drafts.Single(d => d.Type == PlanItemType.Accommodation).Title);
        Assert.Contains(result.Warnings, w => w.Contains("განთავსების პროვაიდერი"));
        Assert.Contains(progress, m => m.StartsWith("ვეძებთ ფრენებს"));
        Assert.Contains("ტაქსის შეთავაზება არ არის", plan.Drafts.First(d => d.Type == PlanItemType.Transfer).DetailsJson);
    }

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex PlaceholderRegex();
}
