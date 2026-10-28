using System.Buffers;
using System.Text;
using TextEncodingDetection.Uchardet;

namespace TextEncodingDetection;

/// <summary>Why <see cref="TextEncodingDetector"/> chose its encoding — one value per step of
/// Notepad++'s decision (see README.md for the source references).</summary>
public enum DetectionReason
{
    /// <summary>Empty file: Notepad++'s defaults ("new document = UTF-8 without BOM" and "apply to
    /// opened ANSI files") make it UTF-8.</summary>
    EmptyFile,
    /// <summary>A byte-order mark: FF FE (UTF-16 LE), FE FF (UTF-16 BE) or EF BB BF (UTF-8).</summary>
    ByteOrderMark,
    /// <summary>uchardet named a charset Notepad++ maps to a code page.</summary>
    Uchardet,
    /// <summary>No BOM, uchardet gave nothing usable, and the sample looks like UTF-16 LE
    /// (byte 0 non-zero, byte 1 zero, even length, IsTextUnicode statistics test).</summary>
    Utf16LittleEndianNoBom,
    /// <summary>Only 7-bit bytes, no NUL: Notepad++ opens it as UTF-8 (its "open ANSI as UTF-8" default).</summary>
    SevenBitAscii,
    /// <summary>The sample is structurally valid UTF-8 (no NUL bytes).</summary>
    Utf8Structure,
    /// <summary>Nothing else matched: the ANSI code page (Notepad++ uses the system code page; here
    /// it is the caller's <c>ansiCodePage</c>, windows-1255 by default).</summary>
    AnsiFallback,
}

/// <summary>The chosen encoding of a text file.</summary>
public sealed record DetectionResult(
    int CodePage,
    string Name,
    bool HasBom,
    int BomLength,
    float Confidence,
    DetectionReason Reason,
    string UchardetCharset)
{
    /// <summary>A decoder for <see cref="CodePage"/>. BOM bytes are not skipped by the returned
    /// encoding's GetString; start decoding at <see cref="BomLength"/>.</summary>
    public Encoding CreateEncoding() => TextEncodingDetector.CreateEncoding(CodePage);
}

/// <summary>
/// Detects a text file's encoding the way Notepad++ does when it opens a file
/// (FileManager::loadFileData / detectCodepage in Buffer.cpp and Utf8_16_Read::determineEncoding
/// in Utf8_16.cpp, with "Autodetect character encoding" at its default, on):
/// <list type="number">
/// <item>empty file → UTF-8;</item>
/// <item>BOM: FE FF → UTF-16 BE, FF FE → UTF-16 LE (also the start of a UTF-32 LE BOM), EF BB BF → UTF-8;</item>
/// <item>uchardet (the Mozilla detector copy Notepad++ bundles) on the first 128 KB + 4 bytes;
/// its answer is used when Notepad++ maps the name to a code page (TIS-620 is ignored);</item>
/// <item>UTF-16 LE without BOM: byte 0 ≠ 0, byte 1 = 0, even sample length, IsTextUnicode(STATISTICS);</item>
/// <item>7-bit only → UTF-8; valid UTF-8 without NUL → UTF-8;</item>
/// <item>otherwise the ANSI code page.</item>
/// </list>
/// Fully managed, single-threaded; one call allocates at most one pooled sample-sized buffer.
/// </summary>
public static class TextEncodingDetector
{
    /// <summary>Notepad++ reads files in blocks of 128 KB + 4 bytes and detects on the first one
    /// (Buffer.cpp: <c>blockSize = 128 * 1024 + 4</c>).</summary>
    public const int SampleSize = 128 * 1024 + 4;

    /// <summary>The fallback for 8-bit text that is neither UTF-8 nor recognised by uchardet.</summary>
    public const int DefaultAnsiCodePage = 1255;

    /// <summary>Detects the encoding from the start of a file.</summary>
    /// <param name="prefix">The first bytes of the file: at least <c>min(fileLength, SampleSize)</c>
    /// of them (more are ignored).</param>
    /// <param name="fileLength">The file's full length in bytes.</param>
    /// <param name="ansiCodePage">The code page Notepad++ would take from the system ANSI setting.</param>
    public static DetectionResult Detect(ReadOnlySpan<byte> prefix, long fileLength, int ansiCodePage = DefaultAnsiCodePage)
    {
        if (fileLength < 0) throw new ArgumentOutOfRangeException(nameof(fileLength));
        int len = (int)Math.Min(fileLength, SampleSize);
        if (prefix.Length < len)
            throw new ArgumentException($"Need the first {len} bytes of the file, got {prefix.Length}.", nameof(prefix));
        ReadOnlySpan<byte> sample = prefix.Slice(0, len);

        if (fileLength == 0)
            return Result(65001, false, 0, 1f, DetectionReason.EmptyFile, "");

        // Utf8_16_Read::determineEncodingFromBOM.
        if (len > 1 && sample[0] == 0xFE && sample[1] == 0xFF) return Result(1201, true, 2, 1f, DetectionReason.ByteOrderMark, "");
        if (len > 1 && sample[0] == 0xFF && sample[1] == 0xFE) return Result(1200, true, 2, 1f, DetectionReason.ByteOrderMark, "");
        if (len > 2 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF) return Result(65001, true, 3, 1f, DetectionReason.ByteOrderMark, "");

        // FileManager::detectCodepage: uchardet on the first block.
        byte[] buf = ArrayPool<byte>.Shared.Rent(len);
        UchardetResult u;
        try
        {
            sample.CopyTo(buf);
            u = UniversalDetector.Detect(buf, len);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
        int cp = u.Charset.Equals("TIS-620", StringComparison.OrdinalIgnoreCase) ? -1 : NotepadPlusPlusCodePage(u.Charset);
        if (cp != -1)
            return Result(cp, false, 0, u.Confidence, DetectionReason.Uchardet, u.Charset);

        // Utf8_16_Read::determineEncoding (BOM cases were handled above).
        if (len > 1 && len % 2 == 0 && sample[0] != 0 && sample[1] == 0 && TextUnicodeStatistics.Passes(sample))
            return Result(1200, false, 0, 1f, DetectionReason.Utf16LittleEndianNoBom, u.Charset);

        return Utf8Structure.Classify(sample) switch
        {
            Utf8Kind.SevenBit => Result(65001, false, 0, 1f, DetectionReason.SevenBitAscii, u.Charset),
            Utf8Kind.Utf8 => Result(65001, false, 0, 1f, DetectionReason.Utf8Structure, u.Charset),
            _ => Result(ansiCodePage, false, 0, 1f, DetectionReason.AnsiFallback, u.Charset),
        };
    }

    static DetectionResult Result(int codePage, bool bom, int bomLength, float confidence, DetectionReason reason, string uchardet) =>
        new(codePage, CodePageName(codePage), bom, bomLength, confidence, reason, uchardet);

    /// <summary>The code page Notepad++'s EncodingMapper::getEncodingFromString gives for each
    /// name its uchardet copy can report (case-insensitive alias match); -1 = no mapping, in which
    /// case Notepad++ falls through to its own UTF-16/UTF-8/ANSI checks. Names checked against
    /// EncodingMapper.cpp: "KOI8-R" does not match the alias "koi8_r", and EUC-JP, x-euc-tw, HZ and
    /// the ISO-2022 names have no entry.</summary>
    internal static int NotepadPlusPlusCodePage(string charset) => charset.ToLowerInvariant() switch
    {
        "utf-8" => 65001,
        "windows-1251" => 1251,
        "windows-1252" => 1252,
        "windows-1253" => 1253,
        "windows-1255" => 1255,
        "iso-8859-5" => 28595,
        "iso-8859-7" => 28597,
        "iso-8859-8" => 28598,
        "x-mac-cyrillic" => 10007,
        "ibm866" => 866,
        "ibm855" => 855,
        "shift_jis" => 932,
        "gb18030" => 936,
        "euc-kr" => 51949,
        "big5" => 950,
        "tis-620" => 874, // never used: detectCodepage drops TIS-620 before mapping
        _ => -1,
    };

    static string CodePageName(int cp) => cp switch
    {
        65001 => "utf-8",
        1200 => "utf-16",
        1201 => "utf-16BE",
        1251 => "windows-1251",
        1252 => "windows-1252",
        1253 => "windows-1253",
        1255 => "windows-1255",
        28595 => "iso-8859-5",
        28597 => "iso-8859-7",
        28598 => "iso-8859-8",
        10007 => "x-mac-cyrillic",
        866 => "cp866",
        855 => "IBM855",
        932 => "shift_jis",
        936 => "gb2312",
        51949 => "euc-kr",
        950 => "big5",
        _ => "cp" + cp,
    };

#if !NETFRAMEWORK
    static int s_providerRegistered;
#endif

    /// <summary>An <see cref="Encoding"/> for any code page <see cref="Detect"/> returns, on net48
    /// and net10 alike (registers the in-box code-pages provider on .NET Core). UTF-8 and UTF-16
    /// encodings are created without emitting a BOM.</summary>
    public static Encoding CreateEncoding(int codePage)
    {
        switch (codePage)
        {
            case 65001: return new UTF8Encoding(false);
            case 1200: return new UnicodeEncoding(false, false);
            case 1201: return new UnicodeEncoding(true, false);
        }
#if !NETFRAMEWORK
        if (Interlocked.Exchange(ref s_providerRegistered, 1) == 0)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
        return Encoding.GetEncoding(codePage);
    }
}
