using System.Buffers.Binary;

namespace ParcelFlow.Delivery.Application;

// Checks bounded image containers; compressed pixels are never decoded by this service.
public static class ProofImages
{
    public const int MaxSignatureBytes = 2 * 1024 * 1024;
    public const int MaxPhotoBytes = 5 * 1024 * 1024;
    private const int MaxDimension = 8192;
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static bool IsValidSignature(byte[]? content) => content is null ||
        content.Length <= MaxSignatureBytes && IsPng(content);
    public static bool IsValidPhoto(byte[]? content) => content is null ||
        content.Length <= MaxPhotoBytes && (IsPng(content) || IsJpeg(content));
    public static string PhotoContentType(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length <= MaxPhotoBytes)
        {
            if (IsPng(content)) return "image/png";
            if (IsJpeg(content)) return "image/jpeg";
        }
        throw new ArgumentException("A bounded PNG or JPEG image is required.", nameof(content));
    }

    private static bool IsPng(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!content.StartsWith(signature)) return false;
        var position = 8;
        var header = false;
        var palette = false;
        var pixels = false;
        var pixelsEnded = false;
        byte color = 0;
        while (position <= content.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(content[position..]);
            if (length > (uint)(content.Length - position - 12)) return false;
            var type = content.Slice(position + 4, 4);
            foreach (var value in type)
                if (value is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z')) return false;
            var data = content.Slice(position + 8, (int)length);
            if (Checksum(content.Slice(position + 4, (int)length + 4)) !=
                BinaryPrimitives.ReadUInt32BigEndian(content[(position + 8 + (int)length)..])) return false;
            if (!header && !type.SequenceEqual("IHDR"u8)) return false;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || length != 13 || !Dimension(BinaryPrimitives.ReadUInt32BigEndian(data)) ||
                    !Dimension(BinaryPrimitives.ReadUInt32BigEndian(data[4..]))) return false;
                color = data[9];
                var validDepth = color switch
                {
                    0 => data[8] is 1 or 2 or 4 or 8 or 16,
                    2 or 4 or 6 => data[8] is 8 or 16,
                    3 => data[8] is 1 or 2 or 4 or 8,
                    _ => false
                };
                if (!validDepth || data[10] != 0 || data[11] != 0 || data[12] > 1) return false;
                header = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (palette || pixels || color is 0 or 4 || length is 0 or > 768 || length % 3 != 0) return false;
                palette = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (pixelsEnded || color == 3 && !palette) return false;
                if (length > 0) pixels = true;
            }
            else if (type.SequenceEqual("IEND"u8))
                return length == 0 && pixels && position + 12 == content.Length;
            else
            {
                if ((type[0] & 32) == 0) return false; // Unknown critical chunk.
                if (pixels) pixelsEnded = true;
            }
            position += (int)length + 12;
        }
        return false;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> content)
    {
        if (content.Length < 4 || content[0] != 255 || content[1] != 216) return false;
        var position = 2;
        var frame = false;
        var quantization = false;
        var huffman = false;
        var scan = false;
        var entropy = false;
        while (position < content.Length)
        {
            if (content[position++] != 255) return false;
            while (position < content.Length && content[position] == 255) position++;
            if (position >= content.Length) return false;
            var marker = content[position++];
            if (marker == 217) return frame && scan && entropy && position == content.Length;
            if (marker is 0 or 216 or >= 208 and <= 215 || position > content.Length - 2) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(content[position..]);
            if (length < 2 || length > content.Length - position) return false;
            var data = content.Slice(position + 2, length - 2);
            if (marker is 192 or 193 or 194)
            {
                if (frame || data.Length < 6 || data[0] != 8 ||
                    !Dimension(BinaryPrimitives.ReadUInt16BigEndian(data[1..])) ||
                    !Dimension(BinaryPrimitives.ReadUInt16BigEndian(data[3..])) ||
                    data[5] is not (1 or 3 or 4) || data.Length != 6 + 3 * data[5]) return false;
                frame = true;
            }
            else if (marker is >= 195 and <= 207 && marker is not (196 or 200 or 204)) return false;
            else if (marker == 219)
            {
                if (data.Length < 65) return false;
                quantization = true;
            }
            else if (marker == 196)
            {
                if (data.Length < 17) return false;
                huffman = true;
            }
            position += length;
            if (marker != 218) continue;
            if (!frame || !quantization || !huffman || data.Length < 4 || data[0] is not (1 or 2 or 3 or 4) ||
                data.Length != 4 + 2 * data[0]) return false;
            scan = true;
            while (position < content.Length)
            {
                if (content[position] != 255) { entropy = true; position++; continue; }
                if (position >= content.Length - 1) return false;
                var next = content[position + 1];
                if (next == 0) { entropy = true; position += 2; continue; }
                if (next is >= 208 and <= 215) { position += 2; continue; }
                if (next == 255) { position++; continue; }
                break;
            }
        }
        return false;
    }

    private static bool Dimension(uint value) => value is > 0 and <= MaxDimension;
    private static uint Checksum(ReadOnlySpan<byte> content)
    {
        var crc = uint.MaxValue;
        foreach (var value in content) crc = CrcTable[(crc ^ value) & 255] ^ crc >> 8;
        return ~crc;
    }
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? value >> 1 ^ 0xedb88320u : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
