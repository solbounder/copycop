using HidSharp;

namespace CopyCop.Core;

public sealed class CopyCopDevice : IAsyncDisposable
{
    public const int VendorId = 0xCAFE;
    public const int LoadProductId = 0x4031;
    private const int PacketSize = ProtocolFrame.Size + 1;

    private readonly HidStream stream;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private bool disposed;

    private CopyCopDevice(HidStream stream)
    {
        this.stream = stream;
        stream.ReadTimeout = 250;
        stream.WriteTimeout = 2_000;
    }

    public static CopyCopDevice? TryOpen(out string? issue)
    {
        issue = null;
        HidDevice[] devices;
        try
        {
            devices = DeviceList.Local
                .GetHidDevices(VendorId, LoadProductId)
                .ToArray();
        }
        catch (Exception exception)
        {
            issue = $"HID-Suche fehlgeschlagen: {exception.Message}";
            return null;
        }

        if (devices.Length == 0) return null;
        foreach (var device in devices)
        {
            try
            {
                if (device.GetMaxInputReportLength() != PacketSize
                    || device.GetMaxOutputReportLength() != PacketSize)
                {
                    issue = "CopyCop wurde gefunden, verwendet aber unerwartete HID-Berichtslängen.";
                    continue;
                }

                if (device.TryOpen(out var opened)) return new CopyCopDevice(opened);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException)
            {
                issue = exception.Message;
            }
        }

        issue ??= OperatingSystem.IsLinux()
            ? "CopyCop wurde gefunden, aber /dev/hidraw darf nicht geöffnet werden. Bitte die udev-Regel installieren."
            : "CopyCop wurde gefunden, ist aber bereits in einem anderen Programm geöffnet.";
        return null;
    }

    public static async Task<CopyCopDevice> WaitForLoadDeviceAsync(
        CancellationToken cancellationToken,
        Action<string?>? waitStatus = null)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = TryOpen(out var issue);
            if (device is not null) return device;
            waitStatus?.Invoke(issue);
            await Task.Delay(500, cancellationToken);
        }
    }

    public async Task WriteFrameAsync(
        ProtocolFrame frame,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var packet = new byte[PacketSize];
        frame.Serialize().CopyTo(packet, 1);
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() => stream.Write(packet), cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public Task<ProtocolFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return Task.Run(() =>
        {
            var packet = new byte[PacketSize];
            var offset = 0;
            while (offset < packet.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var read = stream.Read(packet, offset, packet.Length - offset);
                    if (read == 0) throw new EndOfStreamException("CopyCop wurde getrennt.");
                    offset += read;
                }
                catch (TimeoutException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            if (packet[0] != 0)
                throw new InvalidDataException($"Unerwartete HID-Report-ID {packet[0]}.");
            return ProtocolFrame.Parse(packet.AsSpan(1));
        }, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            stream.Dispose();
            writeLock.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
