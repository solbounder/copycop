using System.Buffers.Binary;

namespace CopyCop.Core;

public enum ProtocolType : byte
{
    Hello = 0x01,
    GetInfo = 0x02,
    BeginTransfer = 0x10,
    Data = 0x11,
    EndTransfer = 0x12,
    GetStatus = 0x20,
    Clear = 0x21,
    CopyEvent = 0x40,
}

public sealed record ProtocolFrame(
    byte Type,
    byte Status,
    uint Sequence,
    uint Argument0,
    uint Argument1,
    byte[] Payload)
{
    public const byte Magic = 0xC3;
    public const byte Version = 1;
    public const int Size = 64;
    public const int PayloadCapacity = 40;

    public byte[] Serialize()
    {
        if (Payload.Length > PayloadCapacity)
            throw new ArgumentOutOfRangeException(nameof(Payload));

        var bytes = new byte[Size];
        bytes[0] = Magic;
        bytes[1] = Version;
        bytes[2] = Type;
        bytes[3] = Status;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), Argument0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), Argument1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), (ushort)Payload.Length);
        Payload.CopyTo(bytes, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60),
            Crc32.Compute(bytes.AsSpan(0, 60)));
        return bytes;
    }

    public static ProtocolFrame Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size || bytes[0] != Magic)
            throw new InvalidDataException("Ungültiger CopyCop-HID-Frame.");
        if (bytes[1] != Version)
            throw new InvalidDataException($"Protokollversion {bytes[1]} wird nicht unterstützt.");
        if (Crc32.Compute(bytes[..60]) != BinaryPrimitives.ReadUInt32LittleEndian(bytes[60..]))
            throw new InvalidDataException("CRC-Fehler im CopyCop-HID-Frame.");

        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]);
        if (payloadLength > PayloadCapacity)
            throw new InvalidDataException("Ungültige HID-Nutzdatenlänge.");

        return new ProtocolFrame(
            bytes[2],
            bytes[3],
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
            bytes.Slice(20, payloadLength).ToArray());
    }
}
