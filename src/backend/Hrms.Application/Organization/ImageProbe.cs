using System.Buffers.Binary;

namespace Hrms.Application.Organization;

/// <summary>Reads pixel dimensions from PNG / JPEG / WebP headers without decoding the image.</summary>
public static class ImageProbe
{
    public static (int Width, int Height)? TryGetSize(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 24 && d[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) && d.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return ((int)BinaryPrimitives.ReadUInt32BigEndian(d.Slice(16, 4)), (int)BinaryPrimitives.ReadUInt32BigEndian(d.Slice(20, 4)));
        }

        if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8)
        {
            return Jpeg(d);
        }

        if (d.Length >= 30 && d[..4].SequenceEqual("RIFF"u8) && d.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return WebP(d);
        }

        return null;
    }

    private static (int, int)? Jpeg(ReadOnlySpan<byte> d)
    {
        var i = 2;
        while (i + 9 < d.Length)
        {
            if (d[i] != 0xFF)
            {
                return null;
            }

            var marker = d[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                i += 2;
                continue;
            }

            var len = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 2, 2));
            if (len < 2)
            {
                return null;
            }

            // SOF0..SOF15 excluding DHT (C4), JPG (C8), DAC (CC)
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var h = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 5, 2));
                var w = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 7, 2));
                return (w, h);
            }

            i += 2 + len;
        }

        return null;
    }

    private static (int, int)? WebP(ReadOnlySpan<byte> d)
    {
        var chunk = d.Slice(12, 4);
        if (chunk.SequenceEqual("VP8 "u8) && d.Length >= 30)
        {
            return (BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(26, 2)) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(28, 2)) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8) && d.Length >= 25 && d[20] == 0x2F)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(21, 4));
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8) && d.Length >= 30)
        {
            var w = 1 + (d[24] | (d[25] << 8) | (d[26] << 16));
            var h = 1 + (d[27] | (d[28] << 8) | (d[29] << 16));
            return (w, h);
        }

        return null;
    }
}
