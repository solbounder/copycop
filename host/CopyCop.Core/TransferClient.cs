using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CopyCop.Core;

public sealed record DeviceInfo(
    uint MaxBytes,
    uint Generation,
    uint StoredCrc,
    uint StoredLength);

public sealed class TransferClient : IAsyncDisposable
{
    private readonly IProtocolTransport device;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<ProtocolFrame>> pending = new();
    private readonly Channel<ProtocolFrame> copyEvents = Channel.CreateUnbounded<ProtocolFrame>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly SemaphoreSlim exchangeLock = new(1, 1);
    private readonly Task readerTask;
    private uint sequence;

    public TransferClient(IProtocolTransport device)
    {
        this.device = device;
        readerTask = ReadLoopAsync();
    }

    public async Task HelloAsync(CancellationToken cancellationToken)
    {
        var response = await ExchangeAsync(ProtocolType.Hello, 0, 0, [], cancellationToken);
        EnsureSuccess(response, "HELLO");
        if (response.Argument0 != ProtocolFrame.Version)
            throw new InvalidDataException("Firmware und Host verwenden verschiedene Protokollversionen.");
    }

    public async Task<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        var response = await ExchangeAsync(ProtocolType.GetInfo, 0, 0, [], cancellationToken);
        EnsureSuccess(response, "GET_INFO");
        if (response.Payload.Length < 12) throw new InvalidDataException("GET_INFO ist unvollständig.");
        return new DeviceInfo(
            response.Argument0,
            BinaryPrimitives.ReadUInt32LittleEndian(response.Payload.AsSpan(0, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(response.Payload.AsSpan(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(response.Payload.AsSpan(8, 4)));
    }

    public async Task WaitForCopyAsync(CancellationToken cancellationToken) =>
        _ = await copyEvents.Reader.ReadAsync(cancellationToken);

    public async Task TransferAsync(
        byte[] utf8,
        Action<int, int>? progress,
        CancellationToken cancellationToken)
    {
        var crc = Crc32.Compute(utf8);
        var begin = await ExchangeAsync(
            ProtocolType.BeginTransfer, (uint)utf8.Length, crc, [], cancellationToken);
        EnsureSuccess(begin, "BEGIN_TRANSFER");

        var offset = 0;
        foreach (var chunk in Chunk(utf8, ProtocolFrame.PayloadCapacity))
        {
            var response = await ExchangeAsync(
                ProtocolType.Data, (uint)offset, 0, chunk, cancellationToken);
            EnsureSuccess(response, "DATA");
            offset += chunk.Length;
            progress?.Invoke(offset, utf8.Length);
        }

        var end = await ExchangeAsync(
            ProtocolType.EndTransfer, 0, 0, [], cancellationToken,
            TimeSpan.FromSeconds(60));
        EnsureSuccess(end, "END_TRANSFER");
        if (end.Argument1 != utf8.Length)
            throw new IOException("Das Gerät meldet nach dem Speichern eine falsche Länge.");
    }

    public static IEnumerable<byte[]> Chunk(byte[] bytes, int chunkSize)
    {
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, bytes.Length - offset);
            yield return bytes.AsSpan(offset, length).ToArray();
        }
    }

    private async Task<ProtocolFrame> ExchangeAsync(
        ProtocolType type,
        uint argument0,
        uint argument1,
        byte[] payload,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        await exchangeLock.WaitAsync(cancellationToken);
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetime.Token);
            operation.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));

            var requestSequence = unchecked(++sequence);
            var completion = new TaskCompletionSource<ProtocolFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(requestSequence, completion))
                throw new InvalidOperationException("Doppelte Protokoll-Sequenznummer.");

            try
            {
                var request = new ProtocolFrame(
                    (byte)type, 0, requestSequence, argument0, argument1, payload);
                await device.WriteFrameAsync(request, operation.Token);
                return await completion.Task.WaitAsync(operation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                     && !lifetime.IsCancellationRequested)
            {
                throw new TimeoutException($"Keine Antwort auf {type} vom CopyCop-Gerät.");
            }
            finally
            {
                pending.TryRemove(requestSequence, out _);
            }
        }
        finally
        {
            exchangeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var frame = await device.ReadFrameAsync(lifetime.Token);
                if (frame.Type == (byte)ProtocolType.CopyEvent)
                {
                    await copyEvents.Writer.WriteAsync(frame, lifetime.Token);
                }
                else if (pending.TryRemove(frame.Sequence, out var completion))
                {
                    completion.TrySetResult(frame);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            copyEvents.Writer.TryComplete(failure);
            foreach (var completion in pending.Values)
            {
                if (failure is null) completion.TrySetCanceled(lifetime.Token);
                else completion.TrySetException(failure);
            }
        }
    }

    private static void EnsureSuccess(ProtocolFrame response, string operation)
    {
        if (response.Status != 0)
            throw new IOException($"{operation} wurde vom Gerät abgelehnt (Status {response.Status}).");
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        await device.DisposeAsync();
        try { await readerTask; }
        catch (OperationCanceledException) { }
        exchangeLock.Dispose();
        lifetime.Dispose();
    }
}
