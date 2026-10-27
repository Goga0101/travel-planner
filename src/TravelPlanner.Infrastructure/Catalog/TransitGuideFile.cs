using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Catalog;

/// <summary>
/// catalog/transit-guides.json: per destination code, the airport-to-city options and tickets researched from the
/// operators' official pages (with sources and the date they were checked). Missing file or city = no guide.
/// </summary>
public sealed class TransitGuideFile(IOptions<CatalogOptions> options, IHostEnvironment environment, ILogger<TransitGuideFile> logger)
    : ITransitGuideSource
{
    private readonly Lazy<Dictionary<string, TransitGuide>> _guides = new(() => Load(options.Value, environment, logger));

    public TransitGuide? Get(string destinationCode) => _guides.Value.GetValueOrDefault(destinationCode);

    private static Dictionary<string, TransitGuide> Load(CatalogOptions options, IHostEnvironment environment, ILogger logger)
    {
        var path = Path.IsPathRooted(options.TransitGuidesPath) ? options.TransitGuidesPath : Path.Combine(environment.ContentRootPath, options.TransitGuidesPath);
        if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, TransitGuide>>(File.ReadAllText(path), AppJson.Options) ?? [];
            return raw
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => kv.Value with
                {
                    Options = kv.Value.Options ?? [],
                    Apps = kv.Value.Apps ?? [],
                    Tips = kv.Value.Tips ?? [],
                    Sources = kv.Value.Sources ?? []
                }, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Transit guides file {Path} is invalid: {Message}", path, ex.Message);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
