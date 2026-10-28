using System.Runtime.InteropServices;
using System.Text;
using TextEncodingDetection.Icu;

// Managed ICU ucsdet port (TextEncodingDetection/Icu) vs Windows' own icu.dll: the full ordered
// list of (name, language, confidence) from ucsdet_detectAll must be identical.
static class IcuCheck
{
    public static int Run(List<Case> matrix, List<Case> upstream)
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "icu.dll")))
        {
            Console.WriteLine("   icu.dll not found, skipped");
            return 0;
        }
        int f = 0;
        f += Compare("matrix", matrix.Select(c => (c.Id, c.Sample)));
        f += Compare("uchardet test folder", upstream.Select(c => (c.Id, c.Sample)));
        f += Compare("random/edge", Generated(matrix.Concat(upstream).ToList()));
        return f;
    }

    static int Compare(string label, IEnumerable<(string Id, byte[] Data)> cases)
    {
        int n = 0, same = 0, shown = 0;
        foreach (var (id, data) in cases)
        {
            n++;
            string managed = Format(IcuCharsetDetector.DetectAll(data));
            string native = Native.DetectAll(data);
            if (managed == native) { same++; continue; }
            if (shown++ < 10)
                Console.WriteLine($"   DIFF {id} (len {data.Length})\n     managed: {managed}\n     icu.dll: {native}");
        }
        Console.WriteLine($"   {label}: {same}/{n} identical");
        return n - same;
    }

    static string Format(IcuCharsetMatch[] m) => string.Join(" ", m.Select(x => $"{x.Name}/{x.Language}/{x.Confidence}"));

    static IEnumerable<(string, byte[])> Generated(List<Case> pool)
    {
        var rnd = new Random(12345);
        // Edge: empty, tiny, BOMs, escapes, boundary lengths around 8192.
        yield return ("empty", Array.Empty<byte>());
        foreach (var bom in new[] { new byte[] { 0xEF, 0xBB, 0xBF }, new byte[] { 0xFE, 0xFF }, new byte[] { 0xFF, 0xFE },
                     new byte[] { 0xFF, 0xFE, 0, 0 }, new byte[] { 0, 0, 0xFE, 0xFF } })
            for (int extra = 0; extra < 6; extra++)
                yield return ($"bom{bom.Length}+{extra}", bom.Concat(Enumerable.Repeat((byte)'a', extra)).ToArray());
        string[] esc = { "\x1b$B", "\x1b(B", "\x1b$)C", "\x1b$)A", "\x1bN", "\x1b$+I", "\x1b", "\x0e", "\x0f" };
        for (int i = 0; i < 300; i++)
        {
            var sb = new StringBuilder();
            int parts = rnd.Next(1, 12);
            for (int p = 0; p < parts; p++)
                sb.Append(rnd.Next(3) == 0 ? "text " : esc[rnd.Next(esc.Length)]);
            yield return ($"esc{i}", Encoding.Latin1.GetBytes(sb.ToString()));
        }
        foreach (var c in pool.Where(c => c.Sample.Length > 9000).Take(40))
            foreach (int len in new[] { 8190, 8191, 8192, 8193, 8200 })
                yield return ($"{c.Id}@{len}", c.Sample.AsSpan(0, len).ToArray());
        // Random byte soups of several flavours.
        for (int i = 0; i < 2500; i++)
        {
            int len = rnd.Next(4) switch { 0 => rnd.Next(0, 8), 1 => rnd.Next(0, 64), 2 => rnd.Next(0, 2000), _ => rnd.Next(0, 20000) };
            var b = new byte[len];
            int flavour = rnd.Next(5);
            for (int k = 0; k < len; k++)
                b[k] = flavour switch
                {
                    0 => (byte)rnd.Next(256),
                    1 => rnd.Next(4) == 0 ? (byte)rnd.Next(0x80, 256) : (byte)rnd.Next(0x20, 0x7F),
                    2 => rnd.Next(2) == 0 ? (byte)0 : (byte)rnd.Next(0x20, 0x7F),
                    3 => (byte)rnd.Next(0x80, 256),
                    _ => (byte)"<a> >b<<\x1b\x80\x9F\xA0\xE0\x8E\x8F\x30\x81 "[rnd.Next(17)],
                };
            yield return ($"rnd{i}/f{flavour}", b);
        }
        // Random slices of real samples (odd offsets break multibyte alignment).
        for (int i = 0; i < 1500 && pool.Count > 0; i++)
        {
            var s = pool[rnd.Next(pool.Count)].Sample;
            if (s.Length == 0) continue;
            int off = rnd.Next(s.Length);
            int len = Math.Min(s.Length - off, rnd.Next(2) == 0 ? rnd.Next(0, 200) : rnd.Next(0, 20000));
            yield return ($"slice{i}", s.AsSpan(off, len).ToArray());
        }
    }

    static class Native
    {
        [DllImport("icu.dll")] static extern IntPtr ucsdet_open(ref int status);
        [DllImport("icu.dll")] static extern void ucsdet_close(IntPtr d);
        [DllImport("icu.dll")] static extern void ucsdet_setText(IntPtr d, IntPtr text, int len, ref int status);
        [DllImport("icu.dll")] static extern IntPtr ucsdet_detectAll(IntPtr d, out int count, ref int status);
        [DllImport("icu.dll")] static extern IntPtr ucsdet_getName(IntPtr m, ref int status);
        [DllImport("icu.dll")] static extern IntPtr ucsdet_getLanguage(IntPtr m, ref int status);
        [DllImport("icu.dll")] static extern int ucsdet_getConfidence(IntPtr m, ref int status);

        static readonly byte[] One = new byte[1];

        public static string DetectAll(byte[] data)
        {
            byte[] buf = data.Length == 0 ? One : data; // non-null pointer for an empty text
            int st = 0;
            IntPtr d = ucsdet_open(ref st);
            if (st > 0) throw new InvalidOperationException("ucsdet_open " + st);
            var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                ucsdet_setText(d, h.AddrOfPinnedObject(), data.Length, ref st);
                IntPtr arr = ucsdet_detectAll(d, out int count, ref st);
                if (st > 0 || arr == IntPtr.Zero) return "";
                var parts = new string[count];
                for (int i = 0; i < count; i++)
                {
                    IntPtr m = Marshal.ReadIntPtr(arr, i * IntPtr.Size);
                    parts[i] = $"{Marshal.PtrToStringAnsi(ucsdet_getName(m, ref st))}/{Marshal.PtrToStringAnsi(ucsdet_getLanguage(m, ref st))}/{ucsdet_getConfidence(m, ref st)}";
                }
                return string.Join(" ", parts);
            }
            finally
            {
                h.Free();
                ucsdet_close(d);
            }
        }
    }
}
