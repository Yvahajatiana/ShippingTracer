using System.Buffers.Binary;
using System.Reflection;

namespace ParcelFlow.Shipment.Infrastructure.Labels;

internal sealed class LabelFont
{
    private readonly byte[] _bytes;
    private readonly int _cmap;
    private readonly int _hmtx;
    private readonly int _metrics;
    private readonly int _units;

    public LabelFont()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ParcelFlow.Shipment.Infrastructure.Labels.Fonts.NotoSans-Regular.ttf")
            ?? throw new InvalidOperationException("The label font resource is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        _bytes = memory.ToArray();
        var tables = new Dictionary<string, int>();
        for (var index = 0; index < U16(4); index++)
        {
            var entry = 12 + index * 16;
            tables.Add(System.Text.Encoding.ASCII.GetString(_bytes, entry, 4), checked((int)U32(entry + 8)));
        }
        _units = U16(tables["head"] + 18);
        _metrics = U16(tables["hhea"] + 34);
        _hmtx = tables["hmtx"];
        GlyphCount = U16(tables["maxp"] + 4);
        var cmap = tables["cmap"];
        for (var index = 0; index < U16(cmap + 2); index++)
        {
            var entry = cmap + 4 + index * 8;
            var candidate = cmap + checked((int)U32(entry + 4));
            if (U16(candidate) == 4 && (U16(entry) == 0 || U16(entry) == 3)) _cmap = candidate;
        }
        if (_cmap == 0) throw new InvalidOperationException("The label font has no Unicode BMP character map.");
    }

    public byte[] Bytes => _bytes;
    public int GlyphCount { get; }
    private ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(offset, 2));
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(offset, 4));

    public int Glyph(int codePoint)
    {
        if (codePoint > ushort.MaxValue) return 0;
        var count = U16(_cmap + 6) / 2;
        var ends = _cmap + 14;
        var starts = ends + count * 2 + 2;
        var deltas = starts + count * 2;
        var offsets = deltas + count * 2;
        for (var index = 0; index < count; index++)
        {
            if (codePoint > U16(ends + index * 2)) continue;
            if (codePoint < U16(starts + index * 2)) return 0;
            var delta = U16(deltas + index * 2);
            var range = U16(offsets + index * 2);
            if (range == 0) return (codePoint + delta) & 0xffff;
            var glyph = U16(offsets + index * 2 + range + (codePoint - U16(starts + index * 2)) * 2);
            return glyph == 0 ? 0 : (glyph + delta) & 0xffff;
        }
        return 0;
    }

    public int Width(int glyph) => (int)Math.Round(U16(_hmtx + Math.Min(glyph, _metrics - 1) * 4) * 1000d / _units);
    public double Measure(string value, double fontSize) => value.EnumerateRunes().Sum(rune => Width(Glyph(rune.Value))) * fontSize / 1000d;
}
