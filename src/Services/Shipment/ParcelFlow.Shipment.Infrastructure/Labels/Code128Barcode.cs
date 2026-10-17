using System.Globalization;
using System.Text;

namespace ParcelFlow.Shipment.Infrastructure.Labels;

/// <summary>Code 128, character set B, ISO/IEC 15417. Pattern data follows the public barcode standard.</summary>
public static class Code128Barcode
{
    private static readonly string[] Patterns = "212222,222122,222221,121223,121322,131222,122213,122312,132212,221213,221312,231212,112232,122132,122231,113222,123122,123221,223211,221132,221231,213212,223112,312131,311222,321122,321221,312212,322112,322211,212123,212321,232121,111323,131123,131321,112313,132113,132311,211313,231113,231311,112133,112331,132131,113123,113321,133121,313121,211331,231131,213113,213311,213131,311123,311321,331121,312113,312311,332111,314111,221411,431111,111224,111422,121124,121421,141122,141221,112214,112412,122114,122411,142112,142211,241211,221114,413111,241112,134111,111242,121142,121241,114212,124112,124211,411212,421112,421211,212141,214121,412121,111143,111341,131141,114113,114311,411113,411311,113141,114131,311141,411131,211412,211214,211232,2331112".Split(',');

    public static IReadOnlyList<int> Encode(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(character => character is < ' ' or > '~'))
            throw new ArgumentException("Code128-B requires printable ASCII characters.", nameof(value));
        var symbols = new List<int> { 104 };
        var checksum = 104;
        for (var index = 0; index < value.Length; index++)
        {
            var symbol = value[index] - 32;
            symbols.Add(symbol);
            checksum += symbol * (index + 1);
        }
        symbols.Add(checksum % 103);
        symbols.Add(106);
        return symbols;
    }

    internal static void Draw(StringBuilder content, string value, double left, double bottom, double width, double height)
    {
        var symbols = Encode(value);
        var modules = symbols.Sum(symbol => Patterns[symbol].Sum(character => character - '0')) + 20;
        var unit = width / modules;
        var x = left + 10 * unit;
        content.AppendLine("0 g");
        foreach (var symbol in symbols)
        {
            var bar = true;
            foreach (var character in Patterns[symbol])
            {
                var barWidth = (character - '0') * unit;
                if (bar)
                    content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.####} {1:0.####} {2:0.####} {3:0.####} re f\n", x, bottom, barWidth, height);
                x += barWidth;
                bar = !bar;
            }
        }
    }
}
