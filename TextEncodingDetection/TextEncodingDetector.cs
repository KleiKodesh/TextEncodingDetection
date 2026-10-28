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
    /// <summary>Improved mode: BOM-less UTF-16 / UTF-32 named by the ICU detector.</summary>
    UnicodeNoBom,
    /// <summary>Improved mode: uchardet was unsure (or named a charset Notepad++ cannot map) and
    /// the ICU detector named a non-Hebrew charset.</summary>
    SecondOpinion,
    /// <summary>Nothing else matched: the ANSI code page (Notepad++ uses the system code page; here
    /// it is the caller's <c>ansiCodePage</c>, windows-1255 by default).</summary>
    AnsiFallback,
}

/// <summary>Which decision to make.</summary>
public enum DetectionMode
{
    /// <summary>Notepad++ where it is right, plus what it misses: UTF-32 BOMs, BOM-less UTF-16 BE/LE
    /// and UTF-32, every charset uchardet names (KOI8-R, EUC-JP, ISO-2022-*, HZ, TIS-620), and a
    /// second opinion from ICU's detector for 8-bit text uchardet was unsure about, unless that
    /// opinion is Hebrew (then the ANSI fallback stands, as in Notepad++).</summary>
    Improved,
    /// <summary>Exactly what Notepad++ picks (the parity spec, checked against its uchardet).</summary>
    NotepadPlusPlus,
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
/// Fully managed, single-threaded; one call rents at most one pooled sample-sized buffer.
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
    /// <param name="mode">Improved (default) or exact Notepad++ parity.</param>
    public static DetectionResult Detect(ReadOnlySpan<byte> prefix, long fileLength, int ansiCodePage = DefaultAnsiCodePage,
        DetectionMode mode = DetectionMode.Improved)
    {
        bool improved = mode == DetectionMode.Improved;
        if (fileLength < 0) throw new ArgumentOutOfRangeException(nameof(fileLength));
        int len = (int)Math.Min(fileLength, SampleSize);
        if (prefix.Length < len)
            throw new ArgumentException($"Need the first {len} bytes of the file, got {prefix.Length}.", nameof(prefix));
        ReadOnlySpan<byte> sample = prefix.Slice(0, len);

        if (fileLength == 0)
            return Result(65001, false, 0, 1f, DetectionReason.EmptyFile, "");

        // Improved: UTF-32 BOMs (Notepad++ reads FF FE 00 00 as UTF-16 LE and misses 00 00 FE FF).
        if (improved && len > 3)
        {
            if (sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0 && sample[3] == 0) return Result(12000, true, 4, 1f, DetectionReason.ByteOrderMark, "");
            if (sample[0] == 0 && sample[1] == 0 && sample[2] == 0xFE && sample[3] == 0xFF) return Result(12001, true, 4, 1f, DetectionReason.ByteOrderMark, "");
        }

        // Utf8_16_Read::determineEncodingFromBOM.
        if (len > 1 && sample[0] == 0xFE && sample[1] == 0xFF) return Result(1201, true, 2, 1f, DetectionReason.ByteOrderMark, "");
        if (len > 1 && sample[0] == 0xFF && sample[1] == 0xFE) return Result(1200, true, 2, 1f, DetectionReason.ByteOrderMark, "");
        if (len > 2 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF) return Result(65001, true, 3, 1f, DetectionReason.ByteOrderMark, "");

        // FileManager::detectCodepage: uchardet on the first block.
        UchardetResult u = UniversalDetector.Detect(sample);
        int cp = improved ? UchardetCodePage(u.Charset)
            : u.Charset.Equals("TIS-620", StringComparison.OrdinalIgnoreCase) ? -1 : NotepadPlusPlusCodePage(u.Charset);
        Utf8Kind? utf8 = null;
        if (improved && cp != -1 && IsSingleByte(cp))
        {
            // uchardet's single-byte answer on text that is valid UTF-8 with non-ASCII in it
            // (short samples fool its models): UTF-8.
            utf8 = Utf8Structure.Classify(sample, allowCutEnd: true);
            if (utf8 == Utf8Kind.Utf8) return Result(65001, false, 0, 1f, DetectionReason.Utf8Structure, u.Charset);
            // A non-Hebrew single-byte answer in a charset ICU also knows, contradicted by a clear
            // ICU single-byte answer for another script (e.g. Greek read as ISO-8859-5): ICU's.
            if (!IsHebrew(cp) && IcuKnows(cp) && SingleByteSecondOpinion(sample) is { } other && Family(other.cp) != Family(cp))
                return Result(other.cp, false, 0, other.confidence / 100f, DetectionReason.SecondOpinion, u.Charset);
        }
        if (cp != -1)
            return Result(cp, false, 0, u.Confidence, DetectionReason.Uchardet, u.Charset);

        // Utf8_16_Read::determineEncoding (BOM cases were handled above).
        if (len > 1 && len % 2 == 0 && sample[0] != 0 && sample[1] == 0 && TextUnicodeStatistics.Passes(sample))
            return Result(1200, false, 0, 1f, DetectionReason.Utf16LittleEndianNoBom, u.Charset);

        switch (improved ? utf8 ?? Utf8Structure.Classify(sample, allowCutEnd: true) : Utf8Structure.Classify(sample))
        {
            case Utf8Kind.SevenBit: return Result(65001, false, 0, 1f, DetectionReason.SevenBitAscii, u.Charset);
            case Utf8Kind.Utf8: return Result(65001, false, 0, 1f, DetectionReason.Utf8Structure, u.Charset);
        }
        if (improved && SecondOpinion(sample) is { } icu)
            return Result(icu.cp, false, 0, icu.confidence / 100f, icu.cp is 1200 or 1201 or 12000 or 12001 ? DetectionReason.UnicodeNoBom : DetectionReason.SecondOpinion, u.Charset);
        return Result(ansiCodePage, false, 0, 1f, DetectionReason.AnsiFallback, u.Charset);
    }

    /// <summary>ICU's top match when it names a code page and is not Hebrew; null otherwise.</summary>
    static (int cp, int confidence)? SecondOpinion(ReadOnlySpan<byte> sample)
    {
        var matches = Icu.IcuCharsetDetector.DetectAll(sample);
        if (matches.Length == 0) return null;
        var top = matches[0];
        int cp = IcuCodePage(top.Name);
        if (cp == -1 || top.Confidence < SecondOpinionMinConfidence) return null;
        // A single-byte answer with Hebrew in close contention (at least half the top
        // confidence): the ANSI fallback stands.
        if (IsSingleByte(cp))
            foreach (var m in matches)
                if (IsIcuHebrew(m.Name) && 2 * m.Confidence >= top.Confidence) return null;
        return (cp, top.Confidence);
    }

    internal static int SecondOpinionMinConfidence = 10;
    internal static int OverrideMinConfidence = 30;

    /// <summary>ICU's best single-byte match, when it is not Hebrew and clears the override bar.</summary>
    static (int cp, int confidence)? SingleByteSecondOpinion(ReadOnlySpan<byte> sample)
    {
        foreach (var m in Icu.IcuCharsetDetector.DetectAll(sample))
        {
            if (m.Confidence < OverrideMinConfidence) return null;
            if (IsIcuHebrew(m.Name)) return null; // Hebrew: the uchardet answer stands
            int cp = IcuCodePage(m.Name);
            if (cp != -1 && IsSingleByte(cp)) return (cp, m.Confidence);
        }
        return null;
    }

    static bool IsIcuHebrew(string name) =>
        name.StartsWith("ISO-8859-8", StringComparison.Ordinal) || name == "windows-1255" || name.StartsWith("IBM424", StringComparison.Ordinal);

    static bool IsSingleByte(int cp) => cp is 1250 or 1251 or 1252 or 1253 or 1254 or 1255 or 1256 or 874 or 866 or 855 or 10007 or 20866
        or 28591 or 28592 or 28595 or 28596 or 28597 or 28598 or 28599;

    /// <summary>uchardet code pages ICU's detector has a recognizer for.</summary>
    static bool IcuKnows(int cp) => cp is 1251 or 1252 or 1253 or 28595 or 28597 or 20866;

    static bool IsHebrew(int cp) => cp is 1255 or 28598;

    /// <summary>Code pages that read the same letters of one script (a switch inside a family is no
    /// contradiction worth overriding uchardet for).</summary>
    static int Family(int cp) => cp switch
    {
        1252 or 28591 or 28605 or 1250 or 28592 or 1254 or 28599 => 1, // Latin
        1253 or 28597 => 2,                                            // Greek
        1256 or 28596 => 3,                                            // Arabic
        1251 or 28595 or 10007 or 866 or 855 or 20866 => 4,           // Cyrillic
        _ => cp,
    };

    /// <summary>Code pages for ICU's charset names; -1 for Hebrew (the ANSI fallback stands) and
    /// for names without a usable Windows code page (IBM420 Arabic EBCDIC).</summary>
    internal static int IcuCodePage(string name) => name switch
    {
        "UTF-8" => 65001,
        "UTF-16BE" => 1201,
        "UTF-16LE" => 1200,
        "UTF-32BE" => 12001,
        "UTF-32LE" => 12000,
        "Shift_JIS" => 932,
        "ISO-2022-JP" => 50220,
        "ISO-2022-CN" => 50227,
        "ISO-2022-KR" => 50225,
        "GB18030" => 54936,
        "Big5" => 950,
        "EUC-JP" => 51932,
        "EUC-KR" => 51949,
        "ISO-8859-1" => 28591,
        "ISO-8859-2" => 28592,
        "ISO-8859-5" => 28595,
        "ISO-8859-6" => 28596,
        "ISO-8859-7" => 28597,
        "ISO-8859-9" => 28599,
        "windows-1250" => 1250,
        "windows-1251" => 1251,
        "windows-1252" => 1252,
        "windows-1253" => 1253,
        "windows-1254" => 1254,
        "windows-1256" => 1256,
        "KOI8-R" => 20866,
        _ => -1, // ISO-8859-8(-I), windows-1255, IBM424_rtl/ltr (Hebrew), IBM420_rtl/ltr
    };

    /// <summary>Improved mode: Notepad++'s mapping plus the names it leaves unmapped.</summary>
    internal static int UchardetCodePage(string charset) => charset.ToLowerInvariant() switch
    {
        "koi8-r" => 20866,
        "euc-jp" => 51932,
        "iso-2022-jp" => 50220,
        "iso-2022-kr" => 50225,
        "iso-2022-cn" => 50227,
        "hz-gb-2312" => 52936,
        "tis-620" => 874,
        _ => NotepadPlusPlusCodePage(charset),
    };

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
        12000 => "utf-32",
        12001 => "utf-32BE",
        20866 => "koi8-r",
        51932 => "euc-jp",
        50220 => "iso-2022-jp",
        50225 => "iso-2022-kr",
        50227 => "x-cp50227",
        52936 => "hz-gb-2312",
        874 => "windows-874",
        54936 => "gb18030",
        28591 => "iso-8859-1",
        28592 => "iso-8859-2",
        28596 => "iso-8859-6",
        28599 => "iso-8859-9",
        1250 => "windows-1250",
        1254 => "windows-1254",
        1256 => "windows-1256",
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
            case 12000: return new UTF32Encoding(false, false);
            case 12001: return new UTF32Encoding(true, false);
        }
#if !NETFRAMEWORK
        if (Interlocked.Exchange(ref s_providerRegistered, 1) == 0)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
        return Encoding.GetEncoding(codePage);
    }
}
