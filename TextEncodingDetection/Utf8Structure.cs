namespace TextEncodingDetection;

internal enum Utf8Kind { SevenBit, Utf8, EightBit }

/// <summary>
/// The structural UTF-8 test Notepad++ applies when nothing else decided
/// (Utf8_16_Read::utf8_7bits_8bits in Utf8_16.cpp), re-implemented from its behaviour:
/// any NUL byte means "not UTF-8" (8-bit); a lead byte must be followed by the right number of
/// 10xxxxxx bytes inside the sample (a sequence cut off by the sample end fails); lead bytes are
/// recognised by bit pattern only (110xxxxx, 1110xxxx, 11110xxx), so overlong forms and
/// surrogates pass and F8..FF fail. Only 7-bit bytes (and no NUL) → 7-bit.
/// </summary>
internal static class Utf8Structure
{
    public static Utf8Kind Classify(ReadOnlySpan<byte> s)
    {
        bool sevenBitOnly = true;
        int i = 0, n = s.Length;
        while (i < n)
        {
            // Eight bytes in 0x01..0x7F at once: the scalar loop would just step over them.
            if (i + 8 <= n)
            {
                ulong v = Uchardet.Swar.Load(s, i);
                if (((v & Uchardet.Swar.HighBits) | Uchardet.Swar.HasZeroByte(v)) == 0) { i += 8; continue; }
            }
            byte b = s[i];
            if (b == 0) return Utf8Kind.EightBit;
            if ((b & 0x80) == 0) { i++; continue; }
            sevenBitOnly = false;
            int follow;
            if ((b & 0xE0) == 0xC0) follow = 1;
            else if ((b & 0xF0) == 0xE0) follow = 2;
            else if ((b & 0xF8) == 0xF0) follow = 3;
            else return Utf8Kind.EightBit; // 10xxxxxx as a lead byte, or F8..FF
            if (n - i < follow + 1) return Utf8Kind.EightBit;
            for (int k = 1; k <= follow; k++)
                if ((s[i + k] & 0xC0) != 0x80) return Utf8Kind.EightBit;
            i += follow + 1;
        }
        return sevenBitOnly ? Utf8Kind.SevenBit : Utf8Kind.Utf8;
    }
}
