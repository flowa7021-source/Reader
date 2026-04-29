using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Search;

public sealed class DocumentIndexingService : BackgroundService, IDocumentIndexer
{
    private readonly Channel<IndexRequest> _queue;
    private readonly IFtsIndex _fts;
    private readonly IFileFingerprint _fingerprint;
    private readonly ILogger<DocumentIndexingService> _log;

    public DocumentIndexingService(
        IFtsIndex fts,
        IFileFingerprint fingerprint,
        ILogger<DocumentIndexingService> log)
    {
        ArgumentNullException.ThrowIfNull(fts);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(log);

        _fts = fts;
        _fingerprint = fingerprint;
        _log = log;
        _queue = Channel.CreateBounded<IndexRequest>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public void Enqueue(IDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);
        _queue.Writer.TryWrite(new IndexRequest(document, path));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("DocumentIndexingService started");
        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await ProcessRequestAsync(request, stoppingToken).ConfigureAwait(false);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Background indexer must not crash on per-document failures.")]
    internal async Task ProcessRequestAsync(IndexRequest request, CancellationToken ct)
    {
        try
        {
            var fp = await _fingerprint.ComputeAsync(request.Path, ct).ConfigureAwait(false);
            await _fts.IndexDocumentAsync(fp, request.Path, StreamPages(request.Document, ct), ct)
                .ConfigureAwait(false);
            _log.LogDebug("Indexed {Path}", request.Path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to index {Path}", request.Path);
        }
    }

    private static async IAsyncEnumerable<TextLayer> StreamPages(
        IDocument document,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (int i = 0; i < document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var layer = await document.GetTextLayerAsync(i, ct).ConfigureAwait(false);
            if (layer is not null)
            {
                yield return layer;
            }
        }
    }

    internal sealed record IndexRequest(IDocument Document, string Path);
}
