using TextEncodingDetection.Uchardet;

/// <summary>The fused single-byte passes against straightforward re-statements of the C++ loops
/// (filter to a buffer, then walk it) on random byte soups rich in the bytes that matter:
/// Hebrew final / non-final letters, ASCII letters, spaces, punctuation, tags.</summary>
static class FusedCheck
{
    public static int Run()
    {
        var rng = new Random(4242);
        byte[] alphabet = [.. "aZ <>.,\n"u8.ToArray(), 0xe0, 0xe9, 0xea, 0xeb, 0xed, 0xee, 0xef, 0xf0, 0xf3, 0xf4, 0xf5, 0xf9, 0xa0, 0x80, 0xc3, 0xff];
        int f = 0;
        for (int iter = 0; iter < 20000; iter++)
        {
            var b = new byte[rng.Next(0, 300)];
            for (int i = 0; i < b.Length; i++)
                b[i] = rng.Next(4) == 0 ? (byte)rng.Next(256) : alphabet[rng.Next(alphabet.Length)];

            // SBCS filter + pair counts + Hebrew scores.
            var filtered = new byte[b.Length];
            int n = CharSetProber.FilterWithoutEnglishLetters(b, filtered);
            var reference = new BytePairCounts();
            reference.Count(filtered.AsSpan(0, n));
            var fused = new BytePairCounts();
            fused.CountFiltered(b);
            var (lg, vs) = HebrewScores(filtered.AsSpan(0, n));
            bool same = fused.FilteredLength == n && fused.FinalLogicalScore == lg && fused.FinalVisualScore == vs
                && fused.Single.AsSpan().SequenceEqual(reference.Single) && fused.Pair.AsSpan().SequenceEqual(reference.Pair);
            // Latin-1: state and confidence bits.
            var latin = new Latin1Prober();
            var st = latin.HandleData(b);
            var (refState, refConf) = Latin1Reference(b);
            same &= st == refState && BitConverter.SingleToInt32Bits(latin.GetConfidence()) == BitConverter.SingleToInt32Bits(refConf);
            if (!same) { if (f < 5) Console.WriteLine($"   FAIL [{Convert.ToHexString(b)}]"); f++; }
        }
        Console.WriteLine($"   {20000 - f}/20000 random buffers agree");
        return f;
    }

    static (int logical, int visual) HebrewScores(ReadOnlySpan<byte> s)
    {
        static bool Final(byte c) => c is 0xea or 0xed or 0xef or 0xf3 or 0xf5;
        static bool NonFinal(byte c) => c is 0xeb or 0xee or 0xf0 or 0xf4;
        int lg = 0, vs = 0; byte prev = 32, before = 32;
        foreach (byte cur in s)
        {
            if (cur == 32) { if (before != 32) { if (Final(prev)) lg++; else if (NonFinal(prev)) vs++; } }
            else if (before == 32 && Final(prev)) vs++;
            before = prev; prev = cur;
        }
        return (lg, vs);
    }

    static (ProbingState, float) Latin1Reference(byte[] buf)
    {
        static bool Letter(byte c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        var o = new List<byte>();
        int prev = 0, cur = 0; bool tag = false;
        for (; cur < buf.Length; cur++)
        {
            byte c = buf[cur];
            if (c == '>') tag = false; else if (c == '<') tag = true;
            if ((c & 0x80) == 0 && !Letter(c))
            {
                if (cur > prev && !tag) { while (prev < cur) o.Add(buf[prev++]); prev++; o.Add(32); }
                else prev = cur + 1;
            }
        }
        if (!tag) while (prev < cur) o.Add(buf[prev++]);
        var counter = new uint[4]; int last = 1;
        foreach (byte x in o)
        {
            int cls = UchardetTables.Latin1_CharToClass[x];
            byte freq = UchardetTables.Latin1ClassModel[last * 8 + cls];
            if (freq == 0) return (ProbingState.NotMe, 0.01f);
            counter[freq]++; last = cls;
        }
        uint total = counter[0] + counter[1] + counter[2] + counter[3];
        float conf;
        if (total == 0) conf = 0f;
        else { float ft = total; conf = (float)(counter[3] / ft); conf = (float)(conf - (float)((float)(counter[1] * 20.0f) / ft)); }
        if (conf < 0f) conf = 0f;
        return (ProbingState.Detecting, (float)(conf * 0.5f));
    }
}
