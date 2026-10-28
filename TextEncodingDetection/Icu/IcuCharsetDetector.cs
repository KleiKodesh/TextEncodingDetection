// Copyright (C) 2016 and later: Unicode, Inc. and others.
// License & terms of use: http://www.unicode.org/copyright.html (Unicode License v3, see NOTICE)
// Copyright (C) 2005-2016, International Business Machines Corporation and others. All Rights Reserved.
//
// C# port of ICU4C release-74-2 i18n/csdetect.cpp (CharsetDetector::detectAll, initRecognizers,
// charsetMatchComparator), csmatch.cpp (CharsetMatch::set) and inputext.cpp (InputText).

namespace TextEncodingDetection.Icu
{
    /// <summary>One ICU charset match: ucsdet_getName / ucsdet_getLanguage / ucsdet_getConfidence.</summary>
    internal readonly struct IcuCharsetMatch
    {
        public IcuCharsetMatch(string name, string language, int confidence)
        {
            Name = name;
            Language = language;
            Confidence = confidence;
        }

        public string Name { get; }
        public string Language { get; }
        public int Confidence { get; }

        public override string ToString() => Name + "/" + Language + "/" + Confidence;
    }

    /// <summary>
    /// ICU's InputText (inputext.cpp) after setText + MungeInput(fStripTags = false):
    /// the first BUFFER_SIZE (8192) raw bytes, byte statistics over them, and the C1 flag.
    /// </summary>
    internal ref struct IcuInputText
    {
        public const int BufferSize = 8192; // inputext.cpp BUFFER_SIZE

        public ReadOnlySpan<byte> RawInput;   // fRawInput / fRawLength
        public ReadOnlySpan<byte> InputBytes; // fInputBytes / fInputLen
        public bool C1Bytes;                  // fC1Bytes

        public IcuInputText(ReadOnlySpan<byte> raw, Span<short> byteStats)
        {
            RawInput = raw;
            // MungeInput with fStripTags false: openTags == 0 < 5, so the unstripped input is used,
            // limited to BUFFER_SIZE bytes.
            InputBytes = raw.Length > BufferSize ? raw.Slice(0, BufferSize) : raw;
            byteStats.Clear();
            for (int i = 0; i < InputBytes.Length; i++)
                byteStats[InputBytes[i]] += 1;
            C1Bytes = false;
            for (int i = 0x80; i <= 0x9F; i++)
            {
                if (byteStats[i] != 0)
                {
                    C1Bytes = true;
                    break;
                }
            }
        }
    }

    /// <summary>Managed port of ICU's ucsdet charset detector (ICU4C release-74-2).</summary>
    internal static class IcuCharsetDetector
    {
        /// <summary>Number of recognizers (fCSRecognizers_size).</summary>
        public const int RecognizerCount = 28;

        /// <summary>
        /// ucsdet_open + ucsdet_setText(input) + ucsdet_detectAll with default settings (no tag
        /// stripping). Matches with confidence &gt; 0, sorted by confidence descending, ties in
        /// recognizer order (stable sort, as uprv_sortArray(..., sortStable = true)).
        /// An empty array is what ICU reports as U_INVALID_CHAR_FOUND.
        /// </summary>
        public static IcuCharsetMatch[] DetectAll(ReadOnlySpan<byte> input)
        {
            Span<short> stats = stackalloc short[256];
            var text = new IcuInputText(input, stats);

            Span<int> conf = stackalloc int[RecognizerCount];
            Span<int> order = stackalloc int[RecognizerCount];
            string[] names = new string[RecognizerCount];
            string[] langs = new string[RecognizerCount];
            int count = 0;

            // csdetect.cpp detectAll: every recognizer in fCSRecognizers is run (the 74-2 loop does
            // not consult isDefaultEnabled), keeping those whose match() returns true.
            for (int r = 0; r < RecognizerCount; r++)
            {
                if (IcuRecognizers.Match(r, ref text, out int c, out string name, out string lang))
                {
                    conf[count] = c;
                    names[count] = name;
                    langs[count] = lang;
                    order[count] = count;
                    count++;
                }
            }

            // Stable insertion sort, highest confidence first (charsetMatchComparator).
            for (int i = 1; i < count; i++)
            {
                int k = order[i];
                int j = i - 1;
                while (j >= 0 && conf[order[j]] < conf[k])
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = k;
            }

            var result = new IcuCharsetMatch[count];
            for (int i = 0; i < count; i++)
            {
                int k = order[i];
                result[i] = new IcuCharsetMatch(names[k], langs[k], conf[k]);
            }
            return result;
        }
    }
}
