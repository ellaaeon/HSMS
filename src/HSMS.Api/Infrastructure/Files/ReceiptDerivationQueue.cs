using System.Threading.Channels;

namespace HSMS.Api.Infrastructure.Files;

/// <summary>
/// In-process channel of receipt ids waiting for derivation. Upload endpoint enqueues, hosted service consumes.
/// On API restart the hosted service also sweeps any "Pending" rows so jobs survive a crash.
/// </summary>
public sealed class ReceiptDerivationQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(int receiptId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(receiptId, cancellationToken);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
