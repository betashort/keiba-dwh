using System.Buffers.Binary;
using System.Security.Cryptography;

namespace JvDataCollector;

public static class RawFrameCodec
{
    public static ReadOnlySpan<byte> Magic => "JVRAW"u8;

    public const int HeaderSize = 17;
    public const int HashSize = 32;
    public const long MaxFileBytes = 256L * 1024 * 1024;

    public static int FrameSize(int payloadLength) => HeaderSize + payloadLength + HashSize;

    public static byte[] Write(Stream stream, long sequence, ReadOnlySpan<byte> payload)
    {
        var hash = SHA256.HashData(payload);
        Write(stream, sequence, payload, hash);
        return hash;
    }

    public static void Write(Stream stream, long sequence, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> hash)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt64LittleEndian(header[5..], (ulong)sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(header[13..], (uint)payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Write(hash);
    }

    public static bool TryRead(
        Stream stream,
        out long sequence,
        out long payloadOffset,
        out int length,
        out byte[] hash,
        out byte[] payload)
    {
        sequence = 0;
        payloadOffset = 0;
        length = 0;
        hash = [];
        payload = [];
        var start = stream.Position;
        Span<byte> header = stackalloc byte[HeaderSize];
        if (!ReadExact(stream, header))
        {
            stream.Position = start;
            return false;
        }

        if (!header[..Magic.Length].SequenceEqual(Magic))
        {
            stream.Position = start;
            return false;
        }

        var payloadLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[13..]));
        if (payloadLength < 0 || stream.Length - stream.Position < payloadLength + HashSize)
        {
            stream.Position = start;
            return false;
        }

        payload = new byte[payloadLength];
        if (!ReadExact(stream, payload))
        {
            stream.Position = start;
            return false;
        }

        hash = new byte[HashSize];
        if (!ReadExact(stream, hash))
        {
            stream.Position = start;
            return false;
        }

        if (!SHA256.HashData(payload).AsSpan().SequenceEqual(hash))
        {
            stream.Position = start;
            return false;
        }

        sequence = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header[5..]));
        payloadOffset = start + HeaderSize;
        length = payloadLength;
        return true;
    }

    public static bool StartsWithMagic(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < Magic.Length)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[Magic.Length];
        using var stream = File.OpenRead(path);
        return ReadExact(stream, buffer) && buffer.SequenceEqual(Magic);
    }

    private static bool ReadExact(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
