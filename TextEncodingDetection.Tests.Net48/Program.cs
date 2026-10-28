using System.Text;
using TextEncodingDetection;

int failures = 0;
foreach (int cp in new[] { 65001, 1200, 1201, 1251, 1252, 1253, 1255, 28595, 28597, 28598, 10007, 866, 855, 932, 936, 51949, 950 })
{
    try { _ = TextEncodingDetector.CreateEncoding(cp).GetString(new byte[] { 0x41, 0x42, 0xE0, 0x80 }); }
    catch (Exception e) { Console.WriteLine($"FAIL cp {cp}: {e.Message}"); failures++; }
}

// Same inputs, same answers as on net10 (single-precision confidence math on both legs).
void Expect(string name, byte[] data, int cp)
{
    var r = TextEncodingDetector.Detect(data, data.Length);
    if (r.CodePage != cp) { Console.WriteLine($"FAIL {name}: {r.CodePage} ({r.Reason}), want {cp}"); failures++; }
}
string hebrew = "בראשית ברא אלהים את השמים ואת הארץ. ";
var sb = new StringBuilder();
while (sb.Length < 20000) sb.Append(hebrew);
Expect("hebrew 1255", Encoding.GetEncoding(1255).GetBytes(sb.ToString()), 1255);
Expect("hebrew utf-8", new UTF8Encoding(false).GetBytes(sb.ToString()), 65001);
Expect("ascii", Encoding.ASCII.GetBytes("plain text\r\n"), 65001);
Expect("empty", new byte[0], 65001);

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"FAILED: {failures}");
return failures == 0 ? 0 : 1;
