using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Features.Journeys;

namespace TravelPlanner.Infrastructure.Journeys;

public sealed class LeaderboardOptions
{
    public const string SectionName = "Leaderboard";

    /// <summary>
    /// Show the illustrative sample travellers (labelled "sample" on the site) next to the real ones. Turn off once real
    /// travellers fill the board.
    /// </summary>
    public bool IncludeSamples { get; set; } = true;
    /// <summary>Relative to the content root unless absolute.</summary>
    public string SamplesPath { get; set; } = "catalog/leaderboard-samples.json";
}

/// <summary>Reads the sample travellers once; a missing or broken file simply means no samples.</summary>
public sealed class LeaderboardSamplesFile(IOptions<LeaderboardOptions> options, IHostEnvironment environment, ILogger<LeaderboardSamplesFile> logger)
    : ILeaderboardSamples
{
    private readonly Lazy<IReadOnlyList<LeaderboardSample>> samples = new(() => Load(options.Value, environment, logger));

    public IReadOnlyList<LeaderboardSample> Get() => options.Value.IncludeSamples ? samples.Value : [];

    private static IReadOnlyList<LeaderboardSample> Load(LeaderboardOptions o, IHostEnvironment env, ILogger logger)
    {
        var path = Path.IsPathRooted(o.SamplesPath) ? o.SamplesPath : Path.Combine(env.ContentRootPath, o.SamplesPath);
        try
        {
            if (!File.Exists(path)) return [];
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<SamplesFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return file?.Travellers ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            logger.LogWarning(e, "Leaderboard samples could not be read from {Path}", path);
            return [];
        }
    }

    private sealed record SamplesFile(List<LeaderboardSample> Travellers);
}
