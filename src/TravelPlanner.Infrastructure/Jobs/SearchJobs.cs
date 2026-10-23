using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Infrastructure.Persistence;

namespace TravelPlanner.Infrastructure.Jobs;

public sealed class JobOptions
{
    public const string SectionName = "Jobs";
    public int MaxConcurrentSearches { get; set; } = 2;
}

public sealed class TripSearchQueue : ITripSearchQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid tripSearchId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(tripSearchId, cancellationToken);
}

public sealed class SearchCancellationRegistry : ISearchCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public CancellationToken Register(Guid tripSearchId, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[tripSearchId] = cts;
        return cts.Token;
    }

    public bool TryCancel(Guid tripSearchId)
    {
        if (!_running.TryGetValue(tripSearchId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    public void Unregister(Guid tripSearchId)
    {
        if (_running.TryRemove(tripSearchId, out var cts)) cts.Dispose();
    }
}

/// <summary>Writes progress through a short-lived context so it never interferes with the processor's unit of work.</summary>
public sealed class SearchProgressReporter(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : ISearchProgressReporter
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(400);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastWrite = new();

    public async Task ReportAsync(Guid tripSearchId, int percent, string message, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (_lastWrite.TryGetValue(tripSearchId, out var last) && now - last < MinInterval) return;
        _lastWrite[tripSearchId] = now;
        if (_lastWrite.Count > 1000) _lastWrite.Clear();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.TripSearches
            .Where(s => s.Id == tripSearchId && s.Status == TripSearchStatus.Running && s.ProgressPercent <= percent)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ProgressPercent, percent)
                .SetProperty(x => x.ProgressMessage, message.Length > 300 ? message[..300] : message)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
    }
}

/// <summary>Processes queued searches with bounded concurrency; recovers interrupted searches on startup.</summary>
public sealed class TripSearchWorker(
    TripSearchQueue queue,
    ISearchCancellationRegistry cancellationRegistry,
    IServiceScopeFactory scopeFactory,
    IOptions<JobOptions> options,
    TimeProvider timeProvider,
    ILogger<TripSearchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        try
        {
            await ProcessQueueAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }

    private Task ProcessQueueAsync(CancellationToken stoppingToken) =>
        Parallel.ForEachAsync(queue.Reader.ReadAllAsync(stoppingToken),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.MaxConcurrentSearches), CancellationToken = stoppingToken },
            async (searchId, ct) =>
            {
                var token = cancellationRegistry.Register(searchId, ct);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<TripSearchProcessor>().ProcessAsync(searchId, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error while processing trip search {SearchId}", searchId);
                }
                finally
                {
                    cancellationRegistry.Unregister(searchId);
                }
            });

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var interrupted = await db.TripSearches.Where(s => s.Status == TripSearchStatus.Running).ToListAsync(stoppingToken);
            foreach (var s in interrupted)
            {
                using var language = T.Use(s.Language);
                s.Fail(T.Get("error.interrupted"), timeProvider.GetUtcNow());
                s.ReportProgress(s.ProgressPercent, T.Get("progress.failed"), timeProvider.GetUtcNow());
            }
            await db.SaveChangesAsync(stoppingToken);

            var queued = await db.TripSearches.Where(s => s.Status == TripSearchStatus.Queued)
                .OrderBy(s => s.CreatedAtUtc).Select(s => s.Id).ToListAsync(stoppingToken);
            foreach (var id in queued) await queue.EnqueueAsync(id, stoppingToken);

            if (interrupted.Count + queued.Count > 0)
                logger.LogInformation("Recovered searches: {Interrupted} interrupted, {Queued} re-queued", interrupted.Count, queued.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to recover pending trip searches");
        }
    }
}
