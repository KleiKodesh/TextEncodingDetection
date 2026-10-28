using System.Text;
using TextEncodingDetection;

/// <summary>
/// The generated test matrix: source texts (decoded from uchardet's own test files, so no text is
/// typed here) × byte encodings × sizes (× line endings for the medium size). Each case keeps only
/// the first block (what Notepad++ and the detector read) plus the full file length.
/// </summary>
static class Matrix
{
    record Source(string Lang, string Text, string[] Encodings);

    static readonly string[] Unicode = ["utf8bom", "utf8", "utf16le-bom", "utf16le", "utf16be-bom", "utf16be", "utf32le-bom", "utf32be-bom"];

    public static List<Case> Build(string upstream)
    {
        string Read(string rel, int cp) => Encoding.GetEncoding(cp).GetString(File.ReadAllBytes(Path.Combine(upstream, rel)));
        string he = Read("he/utf-8.txt", 65001), en = Read("en/ascii.txt", 65001);
        var sources = new List<Source>
        {
            new("he", he, [.. Unicode, "cp1255", "iso8859-8-visual", "iso8859-8-logical", "cp862"]),
            new("he-en", Interleave(he, en), [.. Unicode, "cp1255", "iso8859-8-visual"]),
            new("ascii", en, [.. Unicode, "cp1252"]),
            new("fr", Read("fr/utf-8.txt", 65001), [.. Unicode, "cp1252", "iso8859-1", "iso8859-15", "cp850"]),
            new("de", Read("de/utf-8.txt", 65001), [.. Unicode, "cp1252", "iso8859-1"]),
            new("ar", Read("ar/utf-8.txt", 65001), [.. Unicode, "cp1256", "iso8859-6"]),
            new("ru", Read("ru/utf-8.txt", 65001), [.. Unicode, "cp1251", "koi8-r", "iso8859-5", "cp866", "cp855", "x-mac-cyrillic"]),
            new("bg", Read("bg/utf-8.txt", 65001), [.. Unicode, "cp1251", "iso8859-5"]),
            new("el", Read("el/utf-8.txt", 65001), [.. Unicode, "cp1253", "iso8859-7"]),
            new("th", Read("th/utf-8.txt", 65001), [.. Unicode, "cp874"]),
            new("ja", Read("ja/utf-8.txt", 65001), [.. Unicode, "shift_jis", "euc-jp", "iso-2022-jp"]),
            new("zh-hans", Read("zh/gb18030.txt", 54936), [.. Unicode, "gb2312", "gb18030"]),
            new("zh-hant", Read("zh/big5.txt", 950), [.. Unicode, "big5"]),
            new("ko", Read("ko/utf-8.txt", 65001), [.. Unicode, "euc-kr", "iso-2022-kr"]),
        };

        var cases = new List<Case>();
        foreach (var s in sources)
            foreach (var enc in s.Encodings)
            {
                string text = enc == "iso8859-8-visual" ? Visual(s.Text) : s.Text;
                // medium: the whole text, LF and CRLF
                string lf = text.Replace("\r\n", "\n");
                Add(cases, $"{s.Lang}/{enc}/medium/lf", Encode(enc, lf));
                Add(cases, $"{s.Lang}/{enc}/medium/crlf", Encode(enc, lf.Replace("\n", "\r\n")));
                // short line: the first line
                string first = lf.Split('\n').First(l => l.Trim().Length > 0);
                Add(cases, $"{s.Lang}/{enc}/line", Encode(enc, first + "\r\n"));
                // tiny: the first 1..10 bytes of the encoded text (BOM included)
                byte[] full = Encode(enc, lf);
                foreach (int n in new[] { 1, 2, 3, 4, 5, 7, 10 })
                    if (n <= full.Length) Add(cases, $"{s.Lang}/{enc}/tiny{n}", full.AsSpan(0, n).ToArray());
                // 128 KB boundary and large: repeat the text, cut the bytes at exact lengths
                byte[] big = Repeat(enc, lf, TextEncodingDetector.SampleSize + 64);
                foreach (int d in new[] { -2, -1, 0, 1, 2, 3, 7 })
                {
                    int len = TextEncodingDetector.SampleSize + d;
                    cases.Add(new Case($"{s.Lang}/{enc}/boundary{d:+0;-0;0}", big.AsSpan(0, Math.Min(len, TextEncodingDetector.SampleSize)).ToArray(), len));
                }
                cases.Add(new Case($"{s.Lang}/{enc}/large", big.AsSpan(0, TextEncodingDetector.SampleSize).ToArray(), 1L << 20));
            }
        return cases;
    }

    static void Add(List<Case> cases, string id, byte[] data) => cases.Add(new Case(id, data, data.Length));

    static string Interleave(string a, string b)
    {
        var la = a.Replace("\r\n", "\n").Split('\n');
        var lb = b.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            if (i < la.Length) sb.Append(la[i]);
            if (i < lb.Length) sb.Append(' ').Append(lb[i]);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Visual Hebrew: every line stored in display order (reversed).</summary>
    static string Visual(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => new string(l.Reverse().ToArray())));

    static byte[] Repeat(string enc, string text, int minBytes)
    {
        var sb = new StringBuilder(text);
        byte[] b;
        while ((b = Encode(enc, sb.ToString())).Length < minBytes) sb.Append(text);
        return b;
    }

    public static byte[] Encode(string enc, string text)
    {
        static byte[] Cat(byte[] a, byte[] b) => [.. a, .. b];
        return enc switch
        {
            "utf8bom" => Cat([0xEF, 0xBB, 0xBF], Encoding.UTF8.GetBytes(text)),
            "utf8" => Encoding.UTF8.GetBytes(text),
            "utf16le-bom" => Cat([0xFF, 0xFE], Encoding.Unicode.GetBytes(text)),
            "utf16le" => Encoding.Unicode.GetBytes(text),
            "utf16be-bom" => Cat([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes(text)),
            "utf16be" => Encoding.BigEndianUnicode.GetBytes(text),
            "utf32le-bom" => Cat([0xFF, 0xFE, 0, 0], new UTF32Encoding(false, false).GetBytes(text)),
            "utf32be-bom" => Cat([0, 0, 0xFE, 0xFF], new UTF32Encoding(true, false).GetBytes(text)),
            "iso8859-8-visual" or "iso8859-8-logical" => Encoding.GetEncoding(28598).GetBytes(text),
            _ => Encoding.GetEncoding(CodePageOf(enc)).GetBytes(text),
        };
    }

    static int CodePageOf(string enc) => enc switch
    {
        "cp1255" => 1255, "cp862" => 862, "cp1252" => 1252, "iso8859-1" => 28591, "iso8859-15" => 28605,
        "cp850" => 850, "cp1256" => 1256, "iso8859-6" => 28596, "cp1251" => 1251, "koi8-r" => 20866,
        "iso8859-5" => 28595, "cp866" => 866, "cp855" => 855, "x-mac-cyrillic" => 10007, "cp1253" => 1253,
        "iso8859-7" => 28597, "cp874" => 874, "shift_jis" => 932, "euc-jp" => 51932, "iso-2022-jp" => 50220,
        "gb2312" => 936, "gb18030" => 54936, "big5" => 950, "euc-kr" => 51949, "iso-2022-kr" => 50225,
        _ => throw new ArgumentException(enc),
    };
}
