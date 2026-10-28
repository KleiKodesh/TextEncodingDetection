using System.Diagnostics;
using System.Text;
using TextEncodingDetection;
using TextEncodingDetection.Uchardet;

/// <summary>--bench: stopwatch timings (single thread). 128 KB samples built by repeating uchardet
/// test files, then whole-set throughput over the matrix, the uchardet test folder and, with
/// --real, the real-file list. --dump &lt;file&gt;: every case's full result (uchardet charset and
/// confidence bits, code page, reason, final confidence bits) for byte-exact before/after diffs.</summary>
static class Bench
{
    public static void Run(string upstream, List<Case> matrix, List<Case> upstreamCases, List<Case>? real)
    {
        Console.WriteLine("   128 KB samples (best of 7 rounds):");
        foreach (var (name, file) in new[]
        {
            ("ASCII", "en/ascii.txt"), ("Hebrew windows-1255", "he/windows-1255.txt"), ("Hebrew UTF-8", "he/utf-8.txt"),
            ("Russian windows-1251", "ru/windows-1251.txt"), ("French windows-1252", "fr/windows-1252.txt"),
            ("CJK gb18030", "zh/gb18030.txt"), ("CJK Shift_JIS", "ja/shift_jis.txt"), ("CJK Big5", "zh/big5.txt"),
        })
        {
            byte[] sample = Fill(File.ReadAllBytes(Path.Combine(upstream, file)), TextEncodingDetector.SampleSize);
            var cases = new List<Case> { new(name, sample, sample.Length) };
            double ms = Time(cases, Math.Max(20, 2_000_000 / sample.Length)) ;
            var r = TextEncodingDetector.Detect(sample, sample.Length);
            Console.WriteLine($"   {name,-22} {ms:0.000} ms  {sample.Length / 1048576.0 / (ms / 1000):0} MB/s  -> {r.Name} ({r.Reason})");
            if (Environment.GetEnvironmentVariable("TED_PROFILE") == "1")
                Console.WriteLine($"      mbcs {Part(() => new MbcsGroupProber().HandleData(sample)):0.000}  sbcs {Part(() => new SbcsGroupProber().HandleData(sample)):0.000}  latin1 {Part(() => new Latin1Prober().HandleData(sample)):0.000}  count {Part(() => BytePairCounts.ForCurrentThread().CountFiltered(sample)):0.000}  utf8struct {Part(() => TextEncodingDetector.Detect(sample, sample.Length)):0.000} ms");
        }
        Set("matrix", matrix);
        Set("uchardet test folder", upstreamCases);
        if (real != null) Set("real files", real);
    }

    static void Set(string name, List<Case> cases)
    {
        long bytes = cases.Sum(c => (long)c.Sample.Length);
        double ms = Time(cases, 3);
        Console.WriteLine($"   {name,-22} {cases.Count} cases, {bytes / 1048576.0:0.0} MB: {ms:0.0} ms  {bytes / 1048576.0 / (ms / 1000):0} MB/s");
    }

    /// <summary>Milliseconds for one pass over <paramref name="cases"/> divided by reps, best of 7 rounds.</summary>
    static double Time(List<Case> cases, int reps)
    {
        foreach (var c in cases) TextEncodingDetector.Detect(c.Sample, c.FileLength); // warm-up
        double best = double.MaxValue;
        for (int round = 0; round < 7; round++)
        {
            long t0 = Stopwatch.GetTimestamp();
            for (int k = 0; k < reps; k++)
                foreach (var c in cases) TextEncodingDetector.Detect(c.Sample, c.FileLength);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / reps;
            if (ms < best) best = ms;
        }
        return best;
    }

    static double Part(Action a)
    {
        a();
        double best = double.MaxValue;
        for (int r = 0; r < 7; r++)
        {
            long t0 = Stopwatch.GetTimestamp();
            for (int k = 0; k < 20; k++) a();
            best = Math.Min(best, (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / 20);
        }
        return best;
    }

    static byte[] Fill(byte[] src, int size)
    {
        var b = new byte[size];
        for (int i = 0; i < size; i += src.Length) Array.Copy(src, 0, b, i, Math.Min(src.Length, size - i));
        return b;
    }

    public static void Dump(string path, IEnumerable<Case> cases)
    {
        var sb = new StringBuilder();
        foreach (var c in cases)
        {
            var u = UniversalDetector.Detect(c.Sample, c.Sample.Length);
            var r = TextEncodingDetector.Detect(c.Sample, c.FileLength, mode: DetectionMode.NotepadPlusPlus);
            sb.Append(c.Id).Append('\t').Append(u.Charset).Append('\t').Append(Bits(u.Confidence)).Append('\t')
              .Append(r.CodePage).Append('\t').Append(r.Reason).Append('\t').Append(Bits(r.Confidence)).Append('\t')
              .Append(r.UchardetCharset).Append('\n');
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"   wrote {path}");
    }

    static string Bits(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

    public static List<Case> LoadReal(string listPath, int limit = 500)
    {
        var list = new List<Case>();
        foreach (var path in File.ReadLines(listPath).Where(p => p.Length > 0).Take(limit))
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var sample = new byte[(int)Math.Min(fs.Length, TextEncodingDetector.SampleSize)];
                int n = 0;
                while (n < sample.Length) { int r = fs.Read(sample, n, sample.Length - n); if (r == 0) break; n += r; }
                if (n < sample.Length) Array.Resize(ref sample, n);
                list.Add(new Case("real/" + list.Count + "\t" + path, sample, Math.Max(fs.Length, n)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }
}
