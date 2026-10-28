using System.Diagnostics;
using System.Text;
using TextEncodingDetection;

/// <summary>Real-file pass (report only): the first 500 paths of a list, read-only with
/// FileShare.ReadWrite|Delete, at below-normal priority. Reports the encoding mix, every file whose
/// result differs from the plain "BOM / valid UTF-8 / windows-1255" rule, and, given the oracle,
/// every disagreement with Notepad++'s uchardet.</summary>
static class RealFiles
{
    public static int Run(string listPath, string? oracle, int limit = 500)
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        var paths = File.ReadLines(listPath).Where(p => p.Length > 0).Take(limit).ToList();
        var mix = new SortedDictionary<string, int>();
        var changed = new List<string>();
        var samples = new List<(string path, byte[] sample, long length, DetectionResult r)>();
        var sw = Stopwatch.StartNew();
        long bytes = 0, detectTicks = 0;
        foreach (var path in paths)
        {
            byte[] sample; long length;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                length = fs.Length;
                sample = new byte[(int)Math.Min(length, TextEncodingDetector.SampleSize)];
                int n = 0;
                while (n < sample.Length) { int r = fs.Read(sample, n, sample.Length - n); if (r == 0) break; n += r; }
                if (n < sample.Length) { Array.Resize(ref sample, n); length = n; }
            }
            catch (Exception e) { Console.WriteLine($"   skip {path}: {e.GetType().Name}"); continue; }
            long t0 = Stopwatch.GetTimestamp();
            var res = TextEncodingDetector.Detect(sample, length);
            detectTicks += Stopwatch.GetTimestamp() - t0;
            bytes += sample.Length;
            samples.Add((path, sample, length, res));
            string key = $"{res.Name}{(res.HasBom ? " BOM" : "")} ({res.Reason})";
            mix[key] = mix.GetValueOrDefault(key) + 1;
            int plain = PlainRule(sample, length);
            if (plain != res.CodePage)
                changed.Add($"   {plain} -> {res.CodePage} {res.Reason} uchardet={res.UchardetCharset} conf={res.Confidence:0.00} {path}");
        }
        double ms = sw.Elapsed.TotalMilliseconds;
        double detectMs = detectTicks * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"   {samples.Count} files, {bytes / 1048576.0:0.0} MB sampled, {ms:0} ms total incl. reading; detection {detectMs:0} ms ({bytes / 1048576.0 / (detectMs / 1000):0} MB/s)");
        foreach (var kv in mix.OrderByDescending(k => k.Value)) Console.WriteLine($"   {kv.Value,5}  {kv.Key}");
        Console.WriteLine($"   {changed.Count} file(s) differ from the plain BOM/UTF-8/1255 rule:");
        foreach (var c in changed) Console.WriteLine(c);
        Console.WriteLine("   (the old rule's results by change: " + string.Join(", ", changed.GroupBy(c => c.Substring(0, c.IndexOf(" uchardet"))).Select(g => $"{g.Key.Trim()} x{g.Count()}")) + ")");

        int disagreements = 0;
        if (oracle != null)
        {
            var cases = samples.Select((s, i) => new Case("real/" + i, s.sample, s.length)).ToList();
            string golden = Path.Combine(Path.GetTempPath(), "TextEncodingDetection-real.tsv");
            Golden.Regenerate(oracle, cases, golden);
            var g = Golden.Load(golden);
            for (int i = 0; i < samples.Count; i++)
                if (g.TryGetValue("real/" + i, out var e) && e.CodePage != samples[i].r.CodePage)
                {
                    disagreements++;
                    Console.WriteLine($"   ORACLE MISMATCH {samples[i].path}: ours {samples[i].r.CodePage} npp {e.CodePage} ({e.Uchardet})");
                }
            File.Delete(golden);
            Console.WriteLine($"   Notepad++ oracle agrees on {samples.Count - disagreements}/{samples.Count}");
        }
        return disagreements;
    }

    /// <summary>The detection WordDocumentRandomAccess used before this library (its
    /// MarkupTextEncoding.DetectPlainText at master 31a4696): BOM, UTF-16 LE by zero-byte share,
    /// UTF-8 structure that tolerates a sequence cut by the sample end, else windows-1255.</summary>
    static int PlainRule(byte[] s, long fileLength)
    {
        if (s.Length >= 2 && s[0] == 0xFE && s[1] == 0xFF) return 1201;
        if (s.Length >= 2 && s[0] == 0xFF && s[1] == 0xFE) return 1200;
        if (s.Length >= 3 && s[0] == 0xEF && s[1] == 0xBB && s[2] == 0xBF) return 65001;
        if (s.Length == 0) return 65001;
        if ((fileLength & 1) == 0 && s.Length >= 2 && s[0] != 0 && s[1] == 0)
        {
            int pairs = s.Length / 2, zeroHigh = 0, zeroLow = 0;
            for (int i = 0; i + 1 < s.Length; i += 2) { if (s[i + 1] == 0) zeroHigh++; if (s[i] == 0) zeroLow++; }
            if (zeroHigh * 2 > pairs && zeroLow * 10 < pairs) return 1200;
        }
        int k = 0;
        while (k < s.Length)
        {
            byte b = s[k];
            if (b == 0) return 1255;
            if (b < 0x80) { k++; continue; }
            int c = b >= 0xC0 && b <= 0xDF ? 1 : b >= 0xE0 && b <= 0xEF ? 2 : b >= 0xF0 && b <= 0xF7 ? 3 : -1;
            if (c < 0) return 1255;
            for (int j = 1; j <= c; j++)
            {
                if (k + j >= s.Length) return 65001;
                if ((s[k + j] & 0xC0) != 0x80) return 1255;
            }
            k += c + 1;
        }
        return 65001;
    }
}
