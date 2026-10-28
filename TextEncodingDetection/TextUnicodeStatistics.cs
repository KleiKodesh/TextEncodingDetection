namespace TextEncodingDetection;

/// <summary>
/// Managed stand-in for Win32 <c>IsTextUnicode(buf, len, &amp;flags)</c> called with
/// <c>flags = IS_TEXT_UNICODE_STATISTICS</c> only, as Notepad++'s Utf8_16_Read::determineEncoding does.
/// <para>Microsoft documents the test only as "the text is probably Unicode, with the determination
/// made by applying statistical analysis" (learn.microsoft.com, IsTextUnicode). The arithmetic here
/// follows ReactOS RtlIsTextUnicode (sdk/lib/rtl/unicode.c), re-written from its description:
/// drop one trailing zero byte, read at most the first 256 little-endian UTF-16 units, sum the
/// absolute differences between consecutive low bytes and between consecutive high bytes
/// (starting from 0), and pass when 3 × highDiff &lt; lowDiff. With only the STATISTICS flag
/// requested, every other test is masked out of the result.</para>
/// <para>Approximation: that is ReactOS's reconstruction, not Microsoft's code. The test harness
/// compares it with the real API on Windows over the whole test matrix and random inputs.</para>
/// </summary>
internal static class TextUnicodeStatistics
{
    public static bool Passes(ReadOnlySpan<byte> buf)
    {
        int len = buf.Length;
        if (len < 2) return false;
        if (buf[len - 1] == 0) len--;
        int units = Math.Min(len / 2, 256);
        byte lastLo = 0, lastHi = 0;
        ulong loDiff = 0, hiDiff = 0;
        for (int i = 0; i < units; i++)
        {
            byte lo = buf[2 * i], hi = buf[2 * i + 1];
            loDiff += (ulong)Math.Abs(lo - lastLo);
            hiDiff += (ulong)Math.Abs(hi - lastHi);
            lastLo = lo; lastHi = hi;
        }
        return 3 * hiDiff < loDiff;
    }
}
