using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TextEncodingDetection;
using TextEncodingDetection.Uchardet;

// Console harness (no test framework). dotnet run -c Release [-- options]
//   --oracle <oracle.exe>   regenerate Golden/*.tsv with the MSVC-built Notepad++ uchardet oracle first
//   --real <listfile>       real-file pass over the listed .txt files (report only)
//   --bench                 stopwatch timings (128 KB samples, matrix, test folder, --real list)
//   --dump <file>           write every case's full result (confidence bits too) for exact diffs
// Prints ALL PASSED when every check holds.

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string root = FindRoot();
string upstream = Path.Combine(root, "tools", "uchardet-upstream-test");
string goldenDir = Path.Combine(root, "TextEncodingDetection.Tests", "Golden");
string? oracle = Arg("--oracle");
string? realList = Arg("--real");
int failures = 0;

if (!Directory.Exists(upstream)) { Console.WriteLine($"missing {upstream}: run tools\\fetch-uchardet-tests.cmd"); return 2; }

Step("edge cases", EdgeCases);
var matrix = Matrix.Build(upstream);
Console.WriteLine($"matrix: {matrix.Count} cases");
if (oracle != null) Step("oracle -> Golden/matrix.tsv", () => Golden.Regenerate(oracle, matrix, Path.Combine(goldenDir, "matrix.tsv")));
Step("matrix vs Notepad++ oracle", () => CompareWithGolden(matrix, Path.Combine(goldenDir, "matrix.tsv")));
var upstreamCases = Directory.GetFiles(upstream, "*", SearchOption.AllDirectories)
    .Where(f => !f.EndsWith(".c") && !f.EndsWith("CMakeLists.txt"))
    .OrderBy(f => f, StringComparer.Ordinal)
    .Select(f => new Case("upstream/" + Path.GetRelativePath(upstream, f).Replace('\\', '/'), ReadFirstBlock(f), new FileInfo(f).Length))
    .ToList();
if (oracle != null) Step("oracle -> Golden/upstream.tsv", () => Golden.Regenerate(oracle, upstreamCases, Path.Combine(goldenDir, "upstream.tsv")));
Step("uchardet test folder vs Notepad++ oracle", () => CompareWithGolden(upstreamCases, Path.Combine(goldenDir, "upstream.tsv")));
Step("uchardet test folder vs upstream expected names (report)", () => { UpstreamReport(upstreamCases); return 0; });
Step("fused single-byte passes vs reference loops", FusedCheck.Run);
Step("IsTextUnicode managed vs Win32", IsTextUnicodeVsWin32);
Step("every returned code page decodes", DecodeAll);
Step("ICU ucsdet port vs Windows icu.dll", () => IcuCheck.Run(matrix, upstreamCases));
if (realList != null) Step("real files", () => RealFiles.Run(realList, oracle));
string? dump = Arg("--dump");
List<Case>? realCases = realList != null && (dump != null || args.Contains("--bench")) ? Bench.LoadReal(realList) : null;
if (dump != null) Step("dump", () => { Bench.Dump(dump, matrix.Concat(upstreamCases).Concat(realCases ?? [])); return 0; });
if (args.Contains("--bench")) Step("benchmark", () => { Bench.Run(upstream, matrix, upstreamCases, realCases); return 0; });

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"FAILED: {failures}");
return failures == 0 ? 0 : 1;

void Step(string name, Func<int> run)
{
    var sw = Stopwatch.StartNew();
    Console.WriteLine($"-- {name}");
    int f = run();
    failures += f;
    Console.WriteLine($"   {(f == 0 ? "ok" : f + " failure(s)")} ({sw.ElapsedMilliseconds} ms)");
}

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string FindRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !Directory.Exists(Path.Combine(d.FullName, "tools"))) d = d.Parent;
    return d?.FullName ?? throw new InvalidOperationException("repo root not found");
}

static byte[] ReadFirstBlock(string path)
{
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    var buf = new byte[(int)Math.Min(fs.Length, TextEncodingDetector.SampleSize)];
    int n = 0;
    while (n < buf.Length) { int r = fs.Read(buf, n, buf.Length - n); if (r == 0) break; n += r; }
    return buf;
}

// ---- edge cases: expectations derived by hand from Buffer.cpp / Utf8_16.cpp ----
int EdgeCases()
{
    int f = 0;
    void Expect(string name, byte[] data, long len, int cp, DetectionReason reason, int bom = 0)
    {
        var r = TextEncodingDetector.Detect(data, len);
        if (r.CodePage != cp || r.Reason != reason || r.BomLength != bom)
        {
            Console.WriteLine($"   FAIL {name}: got {r.CodePage}/{r.Reason}/bom{r.BomLength}, want {cp}/{reason}/bom{bom}");
            f++;
        }
    }
    byte[] B(params int[] v) => v.Select(x => (byte)x).ToArray();
    Expect("empty", [], 0, 65001, DetectionReason.EmptyFile);
    Expect("only UTF-8 BOM", B(0xEF, 0xBB, 0xBF), 3, 65001, DetectionReason.ByteOrderMark, 3);
    Expect("only UTF-16LE BOM", B(0xFF, 0xFE), 2, 1200, DetectionReason.ByteOrderMark, 2);
    Expect("only UTF-16BE BOM", B(0xFE, 0xFF), 2, 1201, DetectionReason.ByteOrderMark, 2);
    Expect("UTF-32LE BOM reads as UTF-16LE BOM", B(0xFF, 0xFE, 0, 0, 0x41, 0, 0, 0), 8, 1200, DetectionReason.ByteOrderMark, 2);
    Expect("half UTF-8 BOM", B(0xEF, 0xBB), 2, 1255, DetectionReason.AnsiFallback);
    Expect("single ASCII byte", B(0x41), 1, 65001, DetectionReason.SevenBitAscii);
    Expect("single NUL", B(0), 1, 1255, DetectionReason.AnsiFallback);
    Expect("ASCII with NUL", Encoding.ASCII.GetBytes("abc\0def"), 7, 1255, DetectionReason.AnsiFallback);
    Expect("ASCII CRLF", Encoding.ASCII.GetBytes("line one\r\nline two\r\n"), 20, 65001, DetectionReason.SevenBitAscii);
    Expect("ASCII LF", Encoding.ASCII.GetBytes("line one\nline two\n"), 18, 65001, DetectionReason.SevenBitAscii);
    Expect("UTF-16LE ASCII text, no BOM", Encoding.Unicode.GetBytes("Hello world, this is plain text.\r\n"), 68, 1200, DetectionReason.Utf16LittleEndianNoBom);
    Expect("UTF-16BE ASCII text, no BOM (not detected by Notepad++)", Encoding.BigEndianUnicode.GetBytes("Hello world\r\n"), 26, 1255, DetectionReason.AnsiFallback);
    Expect("odd-length UTF-16LE-looking", B(0x41, 0, 0x42, 0, 0x43), 5, 1255, DetectionReason.AnsiFallback);
    Expect("truncated 2-byte UTF-8 at sample end", B(0x61, 0x62, 0xD7), 3, 1255, DetectionReason.AnsiFallback);
    Expect("lone continuation byte", B(0x61, 0x80, 0x62), 3, 1255, DetectionReason.AnsiFallback);
    Expect("0xA0 only (NBSP is ascii to uchardet, 8-bit to the UTF-8 test)", B(0x61, 0xA0, 0x62), 3, 1255, DetectionReason.AnsiFallback);
    Expect("ESC without ISO-2022 sequence", B(0x1B, 0x41, 0x42), 3, 65001, DetectionReason.SevenBitAscii);
    // Sample-size behaviour: only the first block counts; a later high byte is not seen.
    var asciiThenHebrew = new byte[TextEncodingDetector.SampleSize + 10];
    asciiThenHebrew.AsSpan().Fill((byte)'a');
    asciiThenHebrew[^1] = 0xE0;
    Expect("ASCII first block, 8-bit later", asciiThenHebrew, asciiThenHebrew.Length, 65001, DetectionReason.SevenBitAscii);
    try { TextEncodingDetector.Detect(new byte[10], 100); Console.WriteLine("   FAIL short prefix accepted"); f++; }
    catch (ArgumentException) { }
    return f;
}

int CompareWithGolden(List<Case> cases, string goldenPath)
{
    var golden = Golden.Load(goldenPath);
    int f = 0, missing = 0;
    foreach (var c in cases)
    {
        if (!golden.TryGetValue(c.Id, out var g)) { missing++; continue; }
        var r = TextEncodingDetector.Detect(c.Sample, c.FileLength);
        var raw = UniversalDetector.Detect(c.Sample, c.Sample.Length).Charset;
        bool itu = c.Sample.Length > 0 && TextUnicodeStatistics.Passes(c.Sample);
        if (raw != g.Uchardet || r.CodePage != g.CodePage || itu != g.IsTextUnicode)
        {
            if (f < 30) Console.WriteLine($"   FAIL {c.Id}: ours {raw}/{r.CodePage}/itu{(itu ? 1 : 0)}  npp {g.Uchardet}/{g.CodePage}/itu{(g.IsTextUnicode ? 1 : 0)}");
            f++;
        }
    }
    if (missing > 0) { Console.WriteLine($"   {missing} case(s) missing from {Path.GetFileName(goldenPath)} (run with --oracle)"); f += missing; }
    Console.WriteLine($"   {cases.Count - f}/{cases.Count} match");
    return f;
}

void UpstreamReport(List<Case> cases)
{
    int agree = 0, total = 0;
    var lines = new List<string>();
    foreach (var c in cases)
    {
        string file = c.Id.Substring("upstream/".Length);
        string expected = Path.GetFileNameWithoutExtension(file.Split('/')[1]).Split('.')[0];
        var r = TextEncodingDetector.Detect(c.Sample, c.FileLength);
        string raw = UniversalDetector.Detect(c.Sample, c.Sample.Length).Charset;
        bool same = Norm(raw) == Norm(expected) || (r.Reason == DetectionReason.ByteOrderMark && expected.StartsWith("utf-"));
        total++; if (same) agree++;
        lines.Add($"   {(same ? "  " : "x ")}{file,-28} uchardet(npp)={(raw.Length == 0 ? "-" : raw),-14} final={r.CodePage} {r.Reason}");
    }
    foreach (var l in lines) Console.WriteLine(l);
    Console.WriteLine($"   Notepad++'s uchardet names the upstream-expected charset for {agree}/{total} files (modern uchardet knows many more charsets).");

    static string Norm(string s) => s.ToLowerInvariant().Replace("_", "-") switch
    {
        "mac-cyrillic" => "x-mac-cyrillic",
        "euc-tw" => "x-euc-tw",
        var x => x,
    };
}

int IsTextUnicodeVsWin32()
{
    if (!OperatingSystem.IsWindows()) { Console.WriteLine("   skipped: not Windows"); return 0; }
    var rng = new Random(12345);
    int f = 0, n = 0;
    void Check(byte[] b)
    {
        n++;
        int flags = 2; // IS_TEXT_UNICODE_STATISTICS
        bool win = Win32.IsTextUnicode(b, b.Length, ref flags);
        bool ours = TextUnicodeStatistics.Passes(b);
        if (win != ours) { if (f < 10) Console.WriteLine($"   FAIL len {b.Length} [{Convert.ToHexString(b.AsSpan(0, Math.Min(16, b.Length)))}]: win {win} ours {ours}"); f++; }
    }
    foreach (var c in matrix) if (c.Sample.Length > 0) Check(c.Sample);
    string[] alphabets = ["abc xyz\r\n", "אבג ד", "АБ В", "あいう", "aא 1."];
    for (int i = 0; i < 20000; i++)
    {
        int len = rng.Next(0, 700);
        var b = new byte[len];
        switch (i % 4)
        {
            case 0: rng.NextBytes(b); break;
            case 1:
            {
                string a = alphabets[rng.Next(alphabets.Length)];
                var sb = new StringBuilder();
                while (sb.Length * 2 < len) sb.Append(a[rng.Next(a.Length)]);
                var u = Encoding.Unicode.GetBytes(sb.ToString());
                Array.Copy(u, b, Math.Min(len, u.Length));
                break;
            }
            case 2: for (int k = 0; k < len; k++) b[k] = (byte)(k % 2 == 0 ? rng.Next(0x20, 0x7F) : (rng.Next(8) == 0 ? rng.Next(256) : 0)); break;
            default: for (int k = 0; k < len; k++) b[k] = (byte)rng.Next(0x20, 0x7F); break;
        }
        Check(b);
    }
    Console.WriteLine($"   {n - f}/{n} agree");
    return f;
}

static int DecodeAll()
{
    int f = 0;
    foreach (int cp in new[] { 65001, 1200, 1201, 1251, 1252, 1253, 1255, 28595, 28597, 28598, 10007, 866, 855, 932, 936, 51949, 950 })
    {
        try { _ = TextEncodingDetector.CreateEncoding(cp).GetString([0x41, 0x42, 0xE0, 0x80]); }
        catch (Exception e) { Console.WriteLine($"   FAIL cp {cp}: {e.Message}"); f++; }
    }
    foreach (string cs in new[] { "UTF-8", "windows-1251", "windows-1252", "windows-1253", "windows-1255", "ISO-8859-5", "ISO-8859-7", "ISO-8859-8", "x-mac-cyrillic", "IBM866", "IBM855", "Shift_JIS", "gb18030", "EUC-KR", "Big5" })
    {
        int cp = TextEncodingDetector.NotepadPlusPlusCodePage(cs);
        try { _ = TextEncodingDetector.CreateEncoding(cp); }
        catch (Exception e) { Console.WriteLine($"   FAIL {cs} -> {cp}: {e.Message}"); f++; }
    }
    return f;
}

record Case(string Id, byte[] Sample, long FileLength);

static partial class Win32
{
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsTextUnicode(byte[] buf, int len, ref int result);
}

static class Golden
{
    public record Entry(string Uchardet, bool IsTextUnicode, int CodePage);

    public static Dictionary<string, Entry> Load(string path)
    {
        var d = new Dictionary<string, Entry>();
        if (!File.Exists(path)) return d;
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split('\t');
            if (p.Length == 4) d[p[0]] = new Entry(p[1], p[2] == "1", int.Parse(p[3]));
        }
        return d;
    }

    /// <summary>Writes each case's first block to a temp file, runs the oracle once, and saves
    /// "id \t uchardet \t IsTextUnicode \t code page".</summary>
    public static int Regenerate(string oracle, List<Case> cases, string goldenPath)
    {
        string dir = Path.Combine(Path.GetTempPath(), "TextEncodingDetection-oracle");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var list = new StringBuilder();
        for (int i = 0; i < cases.Count; i++)
        {
            string p = Path.Combine(dir, i + ".bin");
            File.WriteAllBytes(p, cases[i].Sample);
            list.Append(p).Append('\n');
        }
        string listPath = Path.Combine(dir, "list.txt");
        File.WriteAllText(listPath, list.ToString(), new UTF8Encoding(false));
        var psi = new ProcessStartInfo(oracle, $"detect \"{listPath}\"") { RedirectStandardOutput = true, UseShellExecute = false };
        using var proc = Process.Start(psi)!;
        try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        var output = proc.StandardOutput.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        proc.WaitForExit();
        Directory.Delete(dir, true);
        if (output.Length != cases.Count) { Console.WriteLine($"   oracle returned {output.Length} lines for {cases.Count} cases"); return 1; }
        Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
        File.WriteAllLines(goldenPath, cases.Select((c, i) => c.Id + "\t" + output[i]), new UTF8Encoding(false));
        Console.WriteLine($"   wrote {cases.Count} lines");
        return 0;
    }
}
