# Label assets and standard data

NotoSans-Regular.ttf is distributed under SIL Open Font License 1.1 (see Fonts/OFL.txt), from https://github.com/notofonts/noto-fonts/tree/main/hinted/ttf/NotoSans . The font is embedded unchanged in PDF labels.

The numeric Code128 run-width patterns in Code128Barcode.cs represent the ISO/IEC 15417 standard. They were cross-checked against ZXing's Code128Reader CODE_PATTERNS: https://github.com/zxing/zxing/blob/master/core/src/main/java/com/google/zxing/oned/Code128Reader.java . The C# encoding and PDF drawing implementations are native ParcelFlow code; no barcode or PDF runtime library is required.
