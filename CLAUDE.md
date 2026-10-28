# TextEncodingDetection

Detects a .txt file's encoding exactly as Notepad++ does on open (the user's reference). Created
2026-10-28; consumed by FastDocView as a sibling-folder `ProjectReference`. No NuGet.

Rules:
- Fully managed: no P/Invoke, no reflection, `IsAotCompatible` on; net48 + net10.0 must both build.
- **Parity with Notepad++ is the spec.** `Uchardet/` is a faithful port of the uchardet copy
  bundled in Notepad++ (PowerEditor/src/uchardet, pinned commit in tools/NppUchardetOracle/build.cmd),
  NOT the modern freedesktop uchardet. Its quirks are intentional (multi-byte probers ignore
  eError; KOI8-R / EUC-JP / EUC-TW / ISO-2022 names map to no code page; TIS-620 is dropped).
  Do not "improve" detection here; a deviation from Notepad++ is a bug.
- Tables are generated: `tools/NppUchardetOracle/oracle.exe dump > TextEncodingDetection/Uchardet/UchardetTables.g.cs`.
- Verify facts in Notepad++ / uchardet source, never from memory; cite file + function.
- Notepad++ is GPL: re-implement its logic, never copy its code. uchardet files are MPL 1.1 (NOTICE).
- Speed: fix the implementation, never hide it — no threading.
- QA before each commit (all must pass):
  - `dotnet build TextEncodingDetection -c Release` (both TFMs)
  - `cd TextEncodingDetection.Tests && dotnet run -c Release` → ALL PASSED
    (needs `tools\fetch-uchardet-tests.cmd` once; compares with Golden/*.tsv)
  - `cd TextEncodingDetection.Tests.Net48 && dotnet run -c Release` → ALL PASSED
  - After changing the matrix or the port: rebuild the oracle (`tools\NppUchardetOracle\build.cmd`,
    MSVC) and run the harness with `-- --oracle ..\tools\NppUchardetOracle\oracle.exe` to regenerate
    the golden files; the oracle is real Notepad++ uchardet code + the real Win32 IsTextUnicode.
- Commit as you go; author KleiKodesh, no Co-Authored-By trailer; body ends with a
  `User-facing:` line (see the parent project's CLAUDE.md).
