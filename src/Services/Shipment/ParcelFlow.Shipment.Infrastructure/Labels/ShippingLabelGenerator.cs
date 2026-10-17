using System.Globalization;
using System.IO.Compression;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using ParcelFlow.Shipment.Application;
using ParcelFlow.Shipment.Domain;

namespace ParcelFlow.Shipment.Infrastructure.Labels;

public sealed class ShippingLabelGenerator : IShippingLabelGenerator
{
    private static readonly Lazy<LabelFont> Font = new(() => new LabelFont());
    private const double PageWidth = 297.638;
    private const double PageHeight = 419.528;

    public byte[] Generate(ParcelShipment shipment)
    {
        var font = Font.Value;
        var glyphs = new SortedDictionary<int, string>();
        var content = new StringBuilder();
        Text("ParcelFlow", 18, 18, 389);
        Text("ETIQUETTE D'EXPEDITION", 7, 18, 374);
        content.AppendLine("0.7 G 18 362 m 279 362 l S 0 g");
        Text("EXPEDITEUR", 7, 18, 349);
        Address(shipment.Sender, 335, 255, 9);
        content.AppendLine("0.7 G 18 248 m 279 248 l S 0 g");
        Text("DESTINATAIRE", 8, 18, 235);
        Address(shipment.Recipient, 219, 121, 12);
        var parcel = shipment.Parcel;
        Text(FormattableString.Invariant($"{parcel.WeightKg:0.###} kg | {parcel.LengthCm:0.##} x {parcel.WidthCm:0.##} x {parcel.HeightCm:0.##} cm | {shipment.Zone}"), 8, 18, 108);
        Code128Barcode.Draw(content, shipment.TrackingNumber, 18, 48, 261, 45);
        var numberWidth = font.Measure(shipment.TrackingNumber, 10);
        Text(shipment.TrackingNumber, 10, (PageWidth - numberWidth) / 2, 31);
        Text(shipment.Status == ShipmentStatus.AwaitingPayment ? "PRISE EN CHARGE APRES CONFIRMATION DU PAIEMENT" : "DOCUMENT LOGISTIQUE - CONSERVER AVEC LE COLIS", 6, 18, 14);

        var cmap = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /ParcelFlowUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var group in glyphs.Chunk(100))
        {
            cmap.Append(group.Length).AppendLine(" beginbfchar");
            foreach (var pair in group) cmap.Append('<').Append(pair.Key.ToString("X4", CultureInfo.InvariantCulture)).Append("> <").Append(pair.Value).AppendLine(">");
            cmap.AppendLine("endbfchar");
        }
        cmap.AppendLine("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
        var widths = string.Join(' ', Enumerable.Range(0, font.GlyphCount).Select(font.Width));
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>")),
            Stream(Ascii(content.ToString())),
            Ascii("<< /Type /Font /Subtype /Type0 /BaseFont /NotoSans /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 9 0 R >>"),
            Ascii($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /NotoSans /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 7 0 R /CIDToGIDMap /Identity /W [0 [{widths}]] >>"),
            Ascii("<< /Type /FontDescriptor /FontName /NotoSans /Flags 32 /FontBBox [-1000 -400 3000 1200] /ItalicAngle 0 /Ascent 1069 /Descent -293 /CapHeight 714 /StemV 80 /FontFile2 8 0 R >>"),
            Stream(font.Bytes, $"/Length1 {font.Bytes.Length}", compress: true),
            Stream(Ascii(cmap.ToString())),
            Ascii($"<< /Title (ParcelFlow {shipment.TrackingNumber}) /Producer (ParcelFlow .NET 8) >>")
        };
        using var pdf = new MemoryStream();
        pdf.Write(Encoding.Latin1.GetBytes("%PDF-1.7\n%âãÏÓ\n"));
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(pdf.Position);
            pdf.Write(Ascii($"{index + 1} 0 obj\n"));
            pdf.Write(objects[index]);
            pdf.Write(Ascii("\nendobj\n"));
        }
        var xref = pdf.Position;
        pdf.Write(Ascii($"xref\n0 {offsets.Count}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1)) pdf.Write(Ascii(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        pdf.Write(Ascii(FormattableString.Invariant($"trailer\n<< /Size {offsets.Count} /Root 1 0 R /Info 10 0 R >>\nstartxref\n{xref}\n%%EOF\n")));
        return pdf.ToArray();

        void Text(string value, double size, double x, double y)
        {
            var encoded = new StringBuilder();
            foreach (var rune in value.EnumerateRunes())
            {
                var glyph = font.Glyph(rune.Value);
                if (glyph == 0) throw new ValidationException([new ValidationFailure("Address", $"The label font cannot render U+{rune.Value:X4}. Use a postal transcription supported by the font.")]);
                encoded.Append(glyph.ToString("X4", CultureInfo.InvariantCulture));
                glyphs.TryAdd(glyph, Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(rune.ToString())));
            }
            content.AppendFormat(CultureInfo.InvariantCulture, "BT /F1 {0:0.##} Tf 1 0 0 1 {1:0.###} {2:0.###} Tm <{3}> Tj ET\n", size, x, y, encoded);
        }

        void Address(ShippingAddress address, double top, double bottom, double initialSize)
        {
            var parts = new[] { address.Name, address.Line1, address.Line2, $"{address.PostalCode} {address.City}", address.CountryCode, $"Tel. {address.Phone}" }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
            var size = initialSize;
            List<string> lines;
            while (true)
            {
                lines = parts.SelectMany(part => Wrap(part, size)).ToList();
                if (lines.Count * size * 1.2 <= top - bottom) break;
                size -= 0.5;
                if (size < 5.5) throw new ValidationException([new ValidationFailure("Address", "Address is too long for a legible A6 label. Use a compact postal transcription.")]);
            }
            for (var index = 0; index < lines.Count; index++) Text(lines[index], size, 18, top - index * size * 1.2);
        }

        IEnumerable<string> Wrap(string value, double size)
        {
            var line = new StringBuilder();
            foreach (var rune in value.EnumerateRunes())
            {
                var candidate = line.ToString() + rune;
                if (font.Measure(candidate, size) > 261 && line.Length > 0)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                line.Append(rune);
            }
            if (line.Length > 0) yield return line.ToString();
        }
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static byte[] Stream(byte[] bytes, string attributes = "", bool compress = false)
    {
        if (compress)
        {
            using var compressed = new MemoryStream();
            using (var compressor = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) compressor.Write(bytes);
            bytes = compressed.ToArray();
            attributes += " /Filter /FlateDecode";
        }
        using var result = new MemoryStream();
        result.Write(Ascii($"<< /Length {bytes.Length} {attributes} >>\nstream\n"));
        result.Write(bytes);
        result.Write(Ascii("\nendstream"));
        return result.ToArray();
    }
}
