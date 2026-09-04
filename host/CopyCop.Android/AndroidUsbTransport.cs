using Android.Hardware.Usb;
using CopyCop.Core;
using Java.Nio;

namespace CopyCop.AndroidApp;

internal sealed class AndroidUsbTransport : IProtocolTransport
{
    public const int VendorId = 0xCAFE;
    public const int LoadProductId = 0x4031;

    private const int HidSetReport = 0x09;
    private const int HidOutputReport = 0x02;
    private const int HidClassInterfaceOut = 0x21;
    private const int UsbTimeoutMilliseconds = 2_000;

    private readonly UsbDeviceConnection connection;
    private readonly UsbInterface usbInterface;
    private readonly UsbEndpoint inputEndpoint;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private bool disposed;

    private AndroidUsbTransport(
        UsbDevice device,
        UsbDeviceConnection connection,
        UsbInterface usbInterface,
        UsbEndpoint inputEndpoint)
    {
        DeviceId = device.DeviceId;
        this.connection = connection;
        this.usbInterface = usbInterface;
        this.inputEndpoint = inputEndpoint;
    }

    public int DeviceId { get; }

    public static bool IsCopyCop(UsbDevice device) =>
        device.VendorId == VendorId && device.ProductId == LoadProductId;

    public static AndroidUsbTransport Open(UsbManager manager, UsbDevice device)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(device);

        UsbInterface? matchingInterface = null;
        UsbEndpoint? inputEndpoint = null;

        for (var interfaceIndex = 0; interfaceIndex < device.InterfaceCount; interfaceIndex++)
        {
            var candidate = device.GetInterface(interfaceIndex);
            if (candidate.InterfaceClass != UsbClass.Hid) continue;

            for (var endpointIndex = 0; endpointIndex < candidate.EndpointCount; endpointIndex++)
            {
                var endpoint = candidate.GetEndpoint(endpointIndex);
                if (endpoint is not null
                    && endpoint.Direction == UsbAddressing.In
                    && endpoint.Type == UsbAddressing.XferInterrupt
                    && endpoint.MaxPacketSize == ProtocolFrame.Size)
                {
                    matchingInterface = candidate;
                    inputEndpoint = endpoint;
                    break;
                }
            }

            if (matchingInterface is not null) break;
        }

        if (matchingInterface is null || inputEndpoint is null)
            throw new IOException("CopyCop besitzt keinen passenden 64-Byte-HID-Endpunkt.");

        var connection = manager.OpenDevice(device)
            ?? throw new UnauthorizedAccessException("Android konnte CopyCop nicht öffnen.");

        try
        {
            if (!connection.ClaimInterface(matchingInterface, true))
                throw new IOException("Die CopyCop-HID-Schnittstelle konnte nicht übernommen werden.");

            return new AndroidUsbTransport(device, connection, matchingInterface, inputEndpoint);
        }
        catch
        {
            connection.Close();
            connection.Dispose();
            throw;
        }
    }

    public async Task WriteFrameAsync(
        ProtocolFrame frame,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var packet = frame.Serialize();

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            var written = await Task.Run(() => connection.ControlTransfer(
                (UsbAddressing)HidClassInterfaceOut,
                HidSetReport,
                HidOutputReport << 8,
                usbInterface.Id,
                packet,
                packet.Length,
                UsbTimeoutMilliseconds), cancellationToken);

            if (written != packet.Length)
                throw new IOException(
                    $"USB-Ausgabe unvollständig: {Math.Max(0, written)} von {packet.Length} Bytes.");
        }
        finally
        {
            writeLock.Release();
        }
    }

    public Task<ProtocolFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return Task.Run(() => ReadFrame(cancellationToken), cancellationToken);
    }

    private ProtocolFrame ReadFrame(CancellationToken cancellationToken)
    {
        var packet = new byte[ProtocolFrame.Size];
        using var buffer = ByteBuffer.AllocateDirect(ProtocolFrame.Size);
        using var request = new UsbRequest();
        if (!request.Initialize(connection, inputEndpoint))
            throw new IOException("Der USB-Leseendpunkt konnte nicht initialisiert werden.");
        if (!request.Queue(buffer))
            throw new IOException("Der USB-Leseauftrag konnte nicht gestartet werden.");

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UsbRequest? completed;
                try
                {
                    completed = connection.RequestWait(250);
                }
                catch (Java.Util.Concurrent.TimeoutException)
                {
                    continue;
                }

                if (completed is null) continue;
                if (!completed.Equals(request))
                    throw new IOException("Android hat einen unbekannten USB-Auftrag abgeschlossen.");

                var bytesRead = buffer.Position();
                if (bytesRead != ProtocolFrame.Size)
                    throw new IOException(
                        $"USB-Eingabe unvollständig: {bytesRead} von {ProtocolFrame.Size} Bytes.");

                buffer.Flip();
                buffer.Get(packet, 0, bytesRead);
                try
                {
                    return ProtocolFrame.Parse(packet);
                }
                catch (InvalidDataException exception)
                {
                    var prefix = Convert.ToHexString(packet.AsSpan(0, 8));
                    throw new InvalidDataException(
                        $"{exception.Message} USB-Anfang: {prefix}.", exception);
                }
            }
        }
        finally
        {
            request.Cancel();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            try { connection.ReleaseInterface(usbInterface); }
            catch (Java.Lang.Exception) { }
            connection.Close();
            connection.Dispose();
            writeLock.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
