// ***** BEGIN LICENSE BLOCK *****
// Version: MPL 1.1/GPL 2.0/LGPL 2.1
// C# port of nsUniversalDetector.cpp and uchardet.cpp from the uchardet copy bundled in
// Notepad++ (PowerEditor/src/uchardet). The Original Code is Mozilla Universal charset detector
// code. The Initial Developer of the Original Code is Netscape Communications Corporation.
// Portions created by the Initial Developer are Copyright (C) 2001 the Initial Developer.
// All Rights Reserved. Used here under the Mozilla Public License 1.1; see NOTICE.
// ***** END LICENSE BLOCK *****

namespace TextEncodingDetection.Uchardet;

/// <summary>The outcome of one uchardet run: the reported charset ("" when uchardet reports
/// nothing, exactly like uchardet_get_charset) and the winning confidence.</summary>
internal readonly record struct UchardetResult(string Charset, float Confidence);

/// <summary>nsUniversalDetector with NS_FILTER_ALL, driven like Notepad++'s detectCodepage:
/// one HandleData call with the whole sample, then DataEnd.</summary>
internal static class UniversalDetector
{
    const float MinimumThreshold = 0.60f; // MINIMUM_THRESHOLD

    enum InputState { PureAscii, EscAscii, Highbyte }

    public static UchardetResult Detect(byte[] buf, int len) => Detect(buf.AsSpan(0, len));

    public static UchardetResult Detect(ReadOnlySpan<byte> buf)
    {
        int len = buf.Length;
        if (len == 0) return new UchardetResult("", 0f); // DataEnd: !mGotData -> nothing reported

        // BOM shortcut (only when aLen > 2): UTF-8 BOM, or FE FF / FF FE -> "UTF-16".
        if (len > 2)
        {
            if (buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) return new UchardetResult("UTF-8", 1f);
            if ((buf[0] == 0xFE && buf[1] == 0xFF) || (buf[0] == 0xFF && buf[1] == 0xFE)) return new UchardetResult("UTF-16", 1f);
        }

        // The C++ input-state loop: a byte >= 0x80 other than 0xA0 makes it eHighbyte for good;
        // before that, ESC or "~{" makes it eEscAscii. So: any such high byte -> eHighbyte; else
        // any ESC / "~{" pair -> eEscAscii; else pure ASCII.
        var state = InputState.PureAscii;
        for (int i = Swar.SkipAscii(buf, 0); i < len; i = Swar.SkipAscii(buf, i + 1))
            if (buf[i] != 0xA0) { state = InputState.Highbyte; break; }
        if (state == InputState.PureAscii)
        {
            if (buf.IndexOf((byte)0x1B) >= 0) state = InputState.EscAscii;
            else
                for (int i = buf.IndexOf((byte)'{'); i >= 0; )
                {
                    if (i > 0 && buf[i - 1] == (byte)'~') { state = InputState.EscAscii; break; }
                    int next = buf.Slice(i + 1).IndexOf((byte)'{');
                    i = next < 0 ? -1 : i + 1 + next;
                }
        }

        switch (state)
        {
            case InputState.EscAscii:
            {
                var esc = new EscCharSetProber();
                // Found only during HandleData; DataEnd reports nothing for eEscAscii otherwise.
                return esc.HandleData(buf) == ProbingState.FoundIt
                    ? new UchardetResult(esc.GetCharSetName() ?? "", 0.99f)
                    : new UchardetResult("", 0f);
            }
            case InputState.Highbyte:
            {
                CharSetProber[] probers = [new MbcsGroupProber(), new SbcsGroupProber(), new Latin1Prober()];
                foreach (var p in probers)
                    if (p.HandleData(buf) == ProbingState.FoundIt)
                        return new UchardetResult(p.GetCharSetName() ?? "", p.GetConfidence());

                float max = 0.0f;
                int maxProber = 0;
                for (int i = 0; i < probers.Length; i++)
                {
                    float cf = probers[i].GetConfidence();
                    if (cf > max) { max = cf; maxProber = i; }
                }
                return max > MinimumThreshold
                    ? new UchardetResult(probers[maxProber].GetCharSetName() ?? "", max)
                    : new UchardetResult("", max);
            }
            default:
                return new UchardetResult("", 0f);
        }
    }
}
