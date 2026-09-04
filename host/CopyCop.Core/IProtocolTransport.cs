namespace CopyCop.Core;

public interface IProtocolTransport : IAsyncDisposable
{
    Task WriteFrameAsync(ProtocolFrame frame, CancellationToken cancellationToken);

    Task<ProtocolFrame> ReadFrameAsync(CancellationToken cancellationToken);
}
