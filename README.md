# TextEncodingDetection

Detects the encoding of a text file the way **Notepad++** does when it opens it — for .NET, fully
managed (net48, native AOT, WASM), no P/Invoke.

```csharp
DetectionResult r = TextEncodingDetector.Detect(firstBytes, fileLength); // firstBytes >= min(fileLength, 128 KB + 4)
Encoding enc = r.CreateEncoding();          // works for every code page Detect returns, net48 and net10
string text = enc.GetString(bytes, r.BomLength, bytes.Length - r.BomLength);
// r.CodePage, r.Name, r.HasBom, r.BomLength, r.Confidence, r.Reason, r.UchardetCharset
```

## The algorithm (verified in Notepad++ source, commit ee71355, 2026-10-28)

`PowerEditor/src/ScintillaComponent/Buffer.cpp` `FileManager::loadFileData` →
`copyFileContentToScintilla` (first block of `blockSize = 128 * 1024 + 4` bytes), `detectCodepage`,
`resolveLoadedEncoding`; `PowerEditor/src/Utf8_16.cpp` `Utf8_16_Read::determineEncodingFromBOM`,
`determineEncoding`, `utf8_7bits_8bits`; `PowerEditor/src/EncodingMapper.cpp` `getEncodingFromString`;
`PowerEditor/src/Parameters.h` defaults (`_detectEncoding = true`, `_openAnsiAsUtf8 = true`,
new-document `_unicodeMode = uniUTF8_NoBOM`).

1. **Empty file** → UTF-8 (new-document default UTF-8 + "apply to opened ANSI files").
2. **BOM**: `FE FF` → UTF-16 BE, `FF FE` → UTF-16 LE, `EF BB BF` → UTF-8. No UTF-32 check: a UTF-32 LE
   BOM (`FF FE 00 00`) is read as UTF-16 LE; a UTF-32 BE BOM is not a BOM at all.
3. **uchardet** (autodetect is on by default) on the first block, one `handle_data` + `data_end`.
   The charset name is mapped by `getEncodingFromString`; `TIS-620` is ignored on purpose. Names with
   no mapping (`KOI8-R` vs alias `koi8_r`, `EUC-JP`, `x-euc-tw`, `HZ-GB-2312`, `ISO-2022-*`) fall through.
   uchardet only reports when the best prober's confidence is > 0.60.
4. **UTF-16 LE without BOM**: sample length > 1 and even, byte 0 ≠ 0, byte 1 = 0, and
   `IsTextUnicode(..., IS_TEXT_UNICODE_STATISTICS)`. UTF-16 BE without BOM is commented out in Notepad++.
5. **UTF-8 structure** on the first block: any NUL → not UTF-8; a sequence cut by the block end fails.
   7-bit only → opened as UTF-8 (`_openAnsiAsUtf8`); valid UTF-8 → UTF-8.
6. Otherwise the **ANSI code page** (Notepad++: the system's; here `ansiCodePage`, default 1255).

Only the first block is ever examined, as in Notepad++.

## uchardet: which one

Notepad++ bundles an old Mozilla-derived uchardet (`PowerEditor/src/uchardet`; an update to
freedesktop uchardet 0.0.6 was reverted), with its own changes: multi-byte probers ignore `eError`
and the MBCS group never deactivates a prober. `Uchardet/` ports **that** copy: UTF-8, Shift_JIS,
EUC-JP, GB18030, EUC-KR, Big5, EUC-TW (MBCS group); Win1251/KOI8-R/ISO-8859-5/x-mac-cyrillic/
IBM866/IBM855 (Russian), ISO-8859-7/windows-1253 (Greek), ISO-8859-5/windows-1251 (Bulgarian),
TIS-620, Hebrew (windows-1255 logical vs ISO-8859-8 visual via `HebrewProber`) (SBCS group);
windows-1252 (`Latin1Prober`); HZ / ISO-2022-CN/JP/KR (escape prober). Same tables (dumped from the
compiled C++ by `tools/NppUchardetOracle`), same single-precision confidence math.

`IsTextUnicode` is re-implemented from ReactOS's `RtlIsTextUnicode` description (Microsoft only
documents "statistical analysis"): first 256 UTF-16 units, trailing zero byte dropped,
3 × (high-byte variation) < (low-byte variation). Checked against the real Win32 API on 22,628 inputs.

## Tests

```
tools\fetch-uchardet-tests.cmd
cd TextEncodingDetection.Tests && dotnet run -c Release            # ALL PASSED
cd TextEncodingDetection.Tests.Net48 && dotnet run -c Release      # ALL PASSED
```

- Edge cases with hand-derived expectations (empty, BOM only, UTF-32 BOMs, NULs, truncated UTF-8,
  0xA0, ESC, CRLF/LF, first-block-only).
- A generated matrix (2,628 cases): 14 texts (Hebrew, Hebrew+English, ASCII, French, German, Arabic,
  Russian, Bulgarian, Greek, Thai, Japanese, Chinese simplified/traditional, Korean — decoded from
  uchardet's own test files) × UTF-8±BOM, UTF-16 LE/BE ±BOM, UTF-32 LE/BE BOM and the language's
  legacy code pages (incl. windows-1255 logical, ISO-8859-8 visual and logical, IBM862) × sizes
  (1–10 bytes, one line, whole text LF/CRLF, 128 KB boundary −2…+7, 1 MB).
- uchardet's test folder (158 files).

Matrix and test-folder expectations come from `tools/NppUchardetOracle`: Notepad++'s uchardet C++
compiled with MSVC, the real Win32 `IsTextUnicode`, and an independent C++ write-up of the decision
(`Golden/*.tsv`). `--real <list>` adds a report-only pass over real files (`--oracle` checks them too).

Consumed by source: put this repo next to the consumer and add a `ProjectReference` (no NuGet).
License: see NOTICE (uchardet port under MPL 1.1).
