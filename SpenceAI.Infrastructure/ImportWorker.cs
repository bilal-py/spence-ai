using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpenceAI.Application;
using SpenceAI.Application.Imports;
using SpenceAI.Domain;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Infrastructure;

public class ImportQueue : IImportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid batchId, CancellationToken ct = default) => _channel.Writer.WriteAsync(batchId, ct);
}

/// <summary>Processes uploads one at a time so a local model isn't asked to run several jobs at once.</summary>
public class ImportWorker(ImportQueue queue, IServiceScopeFactory scopes, ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueInterruptedAsync(stoppingToken);

        await foreach (var batchId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ImportService>().ProcessAsync(batchId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unhandled error while processing import {BatchId}", batchId);
            }
        }
    }

    /// <summary>Imports that were queued or mid-processing when the app stopped are picked up again.</summary>
    private async Task RequeueInterruptedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.ImportBatches
            .Where(b => b.Status == ImportStatus.Queued || b.Status == ImportStatus.Processing)
            .ToListAsync(ct);

        foreach (var batch in pending)
        {
            batch.Status = ImportStatus.Queued;
            await queue.EnqueueAsync(batch.Id, ct);
        }

        await db.SaveChangesAsync(ct);
    }
}
