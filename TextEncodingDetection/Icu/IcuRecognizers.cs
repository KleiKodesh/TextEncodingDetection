// Copyright (C) 2016 and later: Unicode, Inc. and others.
// License & terms of use: http://www.unicode.org/copyright.html (Unicode License v3, see NOTICE)
// Copyright (C) 2005-2016, International Business Machines Corporation and others. All Rights Reserved.
//
// C# port of the ICU4C release-74-2 charset recognizers: csrutf8.cpp, csrucode.cpp, csrsbcs.cpp,
// csrmbcs.cpp, csr2022.cpp. Tables are in IcuTables.g.cs.

namespace TextEncodingDetection.Icu
{
    internal static class IcuRecognizers
    {
        /// <summary>
        /// Runs recognizer <paramref name="index"/> (the order of csdetect.cpp initRecognizers) and
        /// returns CharsetRecognizer::match's result, with the CharsetMatch fields it set.
        /// </summary>
        public static bool Match(int index, ref IcuInputText t, out int confidence, out string name, out string lang)
        {
            lang = "";
            switch (index)
            {
                case 0: name = "UTF-8"; confidence = MatchUtf8(t.RawInput); break;
                case 1: name = "UTF-16BE"; confidence = MatchUtf16(t.RawInput, bigEndian: true); break;
                case 2: name = "UTF-16LE"; confidence = MatchUtf16(t.RawInput, bigEndian: false); break;
                case 3: name = "UTF-32BE"; confidence = MatchUtf32(t.RawInput, bigEndian: true); break;
                case 4: name = "UTF-32LE"; confidence = MatchUtf32(t.RawInput, bigEndian: false); break;
                case 5: return Match8859Multi(ref t, IcuTables.ngrams_8859_1, IcuTables.ngrams_8859_1_lang,
                    IcuTables.charMap_8859_1, t.C1Bytes ? "windows-1252" : "ISO-8859-1", out confidence, out name, out lang);
                case 6: return Match8859Multi(ref t, IcuTables.ngrams_8859_2, IcuTables.ngrams_8859_2_lang,
                    IcuTables.charMap_8859_2, t.C1Bytes ? "windows-1250" : "ISO-8859-2", out confidence, out name, out lang);
                case 7: name = "ISO-8859-5"; lang = "ru"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_8859_5_ru, IcuTables.charMap_8859_5); break;
                case 8: name = "ISO-8859-6"; lang = "ar"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_8859_6_ar, IcuTables.charMap_8859_6); break;
                case 9: name = t.C1Bytes ? "windows-1253" : "ISO-8859-7"; lang = "el"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_8859_7_el, IcuTables.charMap_8859_7); break;
                case 10: name = t.C1Bytes ? "windows-1255" : "ISO-8859-8-I"; lang = "he"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_8859_8_I_he, IcuTables.charMap_8859_8); break;
                case 11: name = t.C1Bytes ? "windows-1255" : "ISO-8859-8"; lang = "he"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_8859_8_he, IcuTables.charMap_8859_8); break;
                case 12: name = "windows-1251"; lang = "ru"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_windows_1251, IcuTables.charMap_windows_1251); break;
                case 13: name = "windows-1256"; lang = "ar"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_windows_1256, IcuTables.charMap_windows_1256); break;
                case 14: name = "KOI8-R"; lang = "ru"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_KOI8_R, IcuTables.charMap_KOI8_R); break;
                case 15: name = t.C1Bytes ? "windows-1254" : "ISO-8859-9"; lang = "tr"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_8859_9_tr, IcuTables.charMap_8859_9); break;
                case 16: name = "Shift_JIS"; lang = "ja"; confidence = MatchMbcs(t.RawInput, Mbcs.Sjis, IcuTables.commonChars_sjis); break;
                case 17: name = "GB18030"; lang = "zh"; confidence = MatchMbcs(t.RawInput, Mbcs.Gb18030, IcuTables.commonChars_gb_18030); break;
                case 18: name = "EUC-JP"; lang = "ja"; confidence = MatchMbcs(t.RawInput, Mbcs.Euc, IcuTables.commonChars_euc_jp); break;
                case 19: name = "EUC-KR"; lang = "ko"; confidence = MatchMbcs(t.RawInput, Mbcs.Euc, IcuTables.commonChars_euc_kr); break;
                case 20: name = "Big5"; lang = "zh"; confidence = MatchMbcs(t.RawInput, Mbcs.Big5, IcuTables.commonChars_big5); break;
                case 21: name = "ISO-2022-JP"; confidence = Match2022(t.InputBytes, Escapes2022JP); break;
                case 22: name = "ISO-2022-KR"; confidence = Match2022(t.InputBytes, Escapes2022KR); break;
                case 23: name = "ISO-2022-CN"; confidence = Match2022(t.InputBytes, Escapes2022CN); break;
                case 24: name = "IBM424_rtl"; lang = "he"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_IBM424_he_rtl, IcuTables.charMap_IBM424_he); break;
                case 25: name = "IBM424_ltr"; lang = "he"; confidence = MatchSbcs(t.InputBytes, IcuTables.ngrams_IBM424_he_ltr, IcuTables.charMap_IBM424_he); break;
                case 26: name = "IBM420_rtl"; lang = "ar"; confidence = MatchIbm420(t.InputBytes, IcuTables.ngrams_IBM420_ar_rtl); break;
                case 27: name = "IBM420_ltr"; lang = "ar"; confidence = MatchIbm420(t.InputBytes, IcuTables.ngrams_IBM420_ar_ltr); break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
            return confidence > 0;
        }

        // ---------------------------------------------------------------- csrutf8.cpp

        private static int MatchUtf8(ReadOnlySpan<byte> input)
        {
            bool hasBOM = input.Length >= 3 && input[0] == 0xEF && input[1] == 0xBB && input[2] == 0xBF;
            int numValid = 0, numInvalid = 0, trailBytes;

            for (int i = 0; i < input.Length; i += 1)
            {
                int b = input[i];
                if ((b & 0x80) == 0)
                    continue;

                if ((b & 0x0E0) == 0x0C0) trailBytes = 1;
                else if ((b & 0x0F0) == 0x0E0) trailBytes = 2;
                else if ((b & 0x0F8) == 0xF0) trailBytes = 3;
                else
                {
                    numInvalid += 1;
                    continue;
                }

                for (; ; )
                {
                    i += 1;
                    if (i >= input.Length)
                        break;
                    b = input[i];
                    if ((b & 0xC0) != 0x080)
                    {
                        numInvalid += 1;
                        break;
                    }
                    if (--trailBytes == 0)
                    {
                        numValid += 1;
                        break;
                    }
                }
            }

            if (hasBOM && numInvalid == 0) return 100;
            if (hasBOM && numValid > numInvalid * 10) return 80;
            if (numValid > 3 && numInvalid == 0) return 100;
            if (numValid > 0 && numInvalid == 0) return 80;
            if (numValid == 0 && numInvalid == 0) return 15;
            if (numValid > numInvalid * 10) return 25;
            return 0;
        }

        // ---------------------------------------------------------------- csrucode.cpp

        private static int AdjustConfidence(int codeUnit, int confidence)
        {
            if (codeUnit == 0)
                confidence -= 10;
            else if ((codeUnit >= 0x20 && codeUnit <= 0xff) || codeUnit == 0x0a)
                confidence += 10;
            if (confidence < 0) confidence = 0;
            else if (confidence > 100) confidence = 100;
            return confidence;
        }

        private static int MatchUtf16(ReadOnlySpan<byte> input, bool bigEndian)
        {
            int confidence = 10;
            int length = input.Length;
            int bytesToCheck = length > 30 ? 30 : length;
            for (int charIndex = 0; charIndex < bytesToCheck - 1; charIndex += 2)
            {
                int codeUnit = bigEndian
                    ? (input[charIndex] << 8) | input[charIndex + 1]
                    : input[charIndex] | (input[charIndex + 1] << 8);
                if (charIndex == 0 && codeUnit == 0xFEFF)
                {
                    confidence = 100;
                    if (!bigEndian && length >= 4 && input[2] == 0 && input[3] == 0)
                        confidence = 0; // UTF-32 BOM
                    break;
                }
                confidence = AdjustConfidence(codeUnit, confidence);
                if (confidence == 0 || confidence == 100)
                    break;
            }
            if (bytesToCheck < 4 && confidence < 100)
                confidence = 0;
            return confidence;
        }

        private static int GetChar32(ReadOnlySpan<byte> input, int index, bool bigEndian) => bigEndian
            ? input[index] << 24 | input[index + 1] << 16 | input[index + 2] << 8 | input[index + 3]
            : input[index + 3] << 24 | input[index + 2] << 16 | input[index + 1] << 8 | input[index];

        private static int MatchUtf32(ReadOnlySpan<byte> input, bool bigEndian)
        {
            int limit = (input.Length / 4) * 4;
            int numValid = 0, numInvalid = 0;
            bool hasBOM = limit > 0 && GetChar32(input, 0, bigEndian) == 0x0000FEFF;

            for (int i = 0; i < limit; i += 4)
            {
                int ch = GetChar32(input, i, bigEndian);
                if (ch < 0 || ch >= 0x10FFFF || (ch >= 0xD800 && ch <= 0xDFFF))
                    numInvalid += 1;
                else
                    numValid += 1;
            }

            if (hasBOM && numInvalid == 0) return 100;
            if (hasBOM && numValid > numInvalid * 10) return 80;
            if (numValid > 3 && numInvalid == 0) return 100;
            if (numValid > 0 && numInvalid == 0) return 80;
            if (numValid > numInvalid * 10) return 25;
            return 0;
        }

        // ---------------------------------------------------------------- csrsbcs.cpp

        private static bool Match8859Multi(ref IcuInputText t, int[][] ngramSets, string[] langs, byte[] charMap,
            string csName, out int confidence, out string name, out string lang)
        {
            // CharsetRecog_8859_1/2::match: keep the first language with the best confidence.
            int best = -1;
            name = csName;
            lang = "";
            for (int i = 0; i < ngramSets.Length; i++)
            {
                int c = MatchSbcs(t.InputBytes, ngramSets[i], charMap);
                if (c > best)
                {
                    lang = langs[i];
                    best = c;
                }
            }
            confidence = best;
            return best > 0;
        }

        /// <summary>NGramParser state (csrsbcs.cpp).</summary>
        private struct NGramParser
        {
            private int _ngram;
            private int _ngramCount;
            private int _hitCount;
            private readonly int[] _ngramList;

            public NGramParser(int[] ngramList)
            {
                _ngram = 0;
                _ngramCount = 0;
                _hitCount = 0;
                _ngramList = ngramList;
            }

            // Binary search for value in table, which must have exactly 64 entries.
            private static int Search(int[] table, int value)
            {
                int index = 0;
                if (table[index + 32] <= value) index += 32;
                if (table[index + 16] <= value) index += 16;
                if (table[index + 8] <= value) index += 8;
                if (table[index + 4] <= value) index += 4;
                if (table[index + 2] <= value) index += 2;
                if (table[index + 1] <= value) index += 1;
                if (table[index] > value) index -= 1;
                if (index < 0 || table[index] != value) return -1;
                return index;
            }

            public void AddByte(int b)
            {
                _ngram = ((_ngram << 8) + b) & 0xFFFFFF; // N_GRAM_MASK
                _ngramCount += 1;
                if (Search(_ngramList, _ngram) >= 0)
                    _hitCount += 1;
            }

            /// <summary>NGramParser::parse after parseCharacters.</summary>
            public int Finish()
            {
                AddByte(0x20);
                double rawPercent = (double)_hitCount / (double)_ngramCount;
                if (rawPercent > 0.33)
                    return 98;
                return (int)(rawPercent * 300.0);
            }
        }

        private static int MatchSbcs(ReadOnlySpan<byte> input, int[] ngrams, byte[] charMap)
        {
            var parser = new NGramParser(ngrams);
            bool ignoreSpace = false;
            for (int i = 0; i < input.Length; i++)
            {
                byte mb = charMap[input[i]];
                if (mb != 0)
                {
                    if (!(mb == 0x20 && ignoreSpace))
                        parser.AddByte(mb);
                    ignoreSpace = mb == 0x20;
                }
            }
            return parser.Finish();
        }

        private static int IsLamAlef(int b)
        {
            if (b == 0xB2 || b == 0xB3) return 0x47;
            if (b == 0xB4 || b == 0xB5) return 0x49;
            if (b == 0xB8 || b == 0xB9) return 0x56;
            return 0x00;
        }

        /// <summary>CharsetRecog_IBM420_ar::match_sbcs with NGramParser_IBM420.</summary>
        private static int MatchIbm420(ReadOnlySpan<byte> input, int[] ngrams)
        {
            byte[] charMap = IcuTables.charMap_IBM420_ar;
            byte[] unshape = IcuTables.unshapeMap_IBM420;
            var parser = new NGramParser(ngrams);
            bool ignoreSpace = false;
            for (int byteIndex = 0; byteIndex < input.Length && input[byteIndex] != 0; byteIndex++)
            {
                int raw = input[byteIndex];
                int alef = IsLamAlef(raw);
                int b = alef != 0 ? 0xB1 : unshape[raw];

                byte mb = charMap[b];
                if (mb != 0)
                {
                    if (!(mb == 0x20 && ignoreSpace))
                        parser.AddByte(mb);
                    ignoreSpace = mb == 0x20;
                }
                if (alef != 0x00)
                {
                    mb = charMap[alef & 0xFF];
                    if (mb != 0)
                    {
                        if (!(mb == 0x20 && ignoreSpace))
                            parser.AddByte(mb);
                        ignoreSpace = mb == 0x20;
                    }
                }
            }
            return parser.Finish();
        }

        // ---------------------------------------------------------------- csrmbcs.cpp

        private enum Mbcs { Sjis, Euc, Big5, Gb18030 }

        /// <summary>IteratedChar (csrmbcs.h).</summary>
        private struct IteratedChar
        {
            public uint CharValue;
            public int NextIndex;
            public bool Error;

            public int NextByte(ReadOnlySpan<byte> input)
            {
                if (NextIndex >= input.Length)
                    return -1;
                return input[NextIndex++];
            }
        }

        private static int BinarySearch(ushort[] array, ushort value)
        {
            int start = 0, end = array.Length - 1;
            int mid = (start + end) / 2;
            while (start <= end)
            {
                if (array[mid] == value) return mid;
                if (array[mid] < value) start = mid + 1;
                else end = mid - 1;
                mid = (start + end) / 2;
            }
            return -1;
        }

        private static bool NextChar(Mbcs kind, ref IteratedChar it, ReadOnlySpan<byte> input)
        {
            it.Error = false;
            int firstByte = it.NextByte(input);
            it.CharValue = unchecked((uint)firstByte);
            if (firstByte < 0)
                return false;

            switch (kind)
            {
                case Mbcs.Sjis:
                {
                    if (firstByte <= 0x7F || (firstByte > 0xA0 && firstByte <= 0xDF))
                        return true;
                    int secondByte = it.NextByte(input);
                    if (secondByte >= 0)
                        it.CharValue = (uint)((firstByte << 8) | secondByte);
                    if (!((secondByte >= 0x40 && secondByte <= 0x7F) || (secondByte >= 0x80 && secondByte <= 0xFE)))
                        it.Error = true;
                    return true;
                }
                case Mbcs.Euc:
                {
                    if (firstByte <= 0x8D)
                        return true;
                    int secondByte = it.NextByte(input);
                    if (secondByte >= 0)
                        it.CharValue = (it.CharValue << 8) | (uint)secondByte;
                    if (firstByte >= 0xA1 && firstByte <= 0xFE)
                    {
                        if (secondByte < 0xA1)
                            it.Error = true;
                        return true;
                    }
                    if (firstByte == 0x8E)
                    {
                        if (secondByte < 0xA1)
                            it.Error = true;
                        return true;
                    }
                    if (firstByte == 0x8F)
                    {
                        int thirdByte = it.NextByte(input);
                        it.CharValue = (it.CharValue << 8) | unchecked((uint)thirdByte);
                        if (thirdByte < 0xa1)
                            it.Error = true;
                    }
                    return true;
                }
                case Mbcs.Big5:
                {
                    if (firstByte <= 0x7F || firstByte == 0xFF)
                        return true;
                    int secondByte = it.NextByte(input);
                    if (secondByte >= 0)
                        it.CharValue = (it.CharValue << 8) | (uint)secondByte;
                    if (secondByte < 0x40 || secondByte == 0x7F || secondByte == 0xFF)
                        it.Error = true;
                    return true;
                }
                default: // Gb18030
                {
                    if (firstByte <= 0x80)
                        return true;
                    int secondByte = it.NextByte(input);
                    if (secondByte >= 0)
                        it.CharValue = (it.CharValue << 8) | (uint)secondByte;
                    if (firstByte >= 0x81 && firstByte <= 0xFE)
                    {
                        // "secondByte >= 80" is decimal in ICU's source; kept as is.
                        if ((secondByte >= 0x40 && secondByte <= 0x7E) || (secondByte >= 80 && secondByte <= 0xFE))
                            return true;
                        if (secondByte >= 0x30 && secondByte <= 0x39)
                        {
                            int thirdByte = it.NextByte(input);
                            if (thirdByte >= 0x81 && thirdByte <= 0xFE)
                            {
                                int fourthByte = it.NextByte(input);
                                if (fourthByte >= 0x30 && fourthByte <= 0x39)
                                {
                                    it.CharValue = (it.CharValue << 16) | (uint)(thirdByte << 8) | (uint)fourthByte;
                                    return true;
                                }
                            }
                        }
                        it.Error = true;
                    }
                    return true;
                }
            }
        }

        private static int MatchMbcs(ReadOnlySpan<byte> input, Mbcs kind, ushort[] commonChars)
        {
            int doubleByteCharCount = 0, commonCharCount = 0, badCharCount = 0, totalCharCount = 0;
            int confidence = 0;
            var iter = new IteratedChar();

            while (NextChar(kind, ref iter, input))
            {
                totalCharCount++;
                if (iter.Error)
                {
                    badCharCount++;
                }
                else if (iter.CharValue > 0xFF)
                {
                    doubleByteCharCount++;
                    if (BinarySearch(commonChars, unchecked((ushort)iter.CharValue)) >= 0)
                        commonCharCount += 1;
                }
                if (badCharCount >= 2 && badCharCount * 5 >= doubleByteCharCount)
                    return confidence; // bail out early: 0
            }

            if (doubleByteCharCount <= 10 && badCharCount == 0)
                return doubleByteCharCount == 0 && totalCharCount < 10 ? 0 : 10;

            if (doubleByteCharCount < 20 * badCharCount)
                return 0;

            double maxVal = Math.Log((double)doubleByteCharCount / 4);
            double scaleFactor = 90.0 / maxVal;
            confidence = (int)(Math.Log((double)commonCharCount + 1) * scaleFactor + 10.0);
            confidence = Math.Min(confidence, 100);
            if (confidence < 0)
                confidence = 0;
            return confidence;
        }

        // ---------------------------------------------------------------- csr2022.cpp

        private static readonly byte[][] Escapes2022JP =
        {
            new byte[] { 0x1b, 0x24, 0x28, 0x43 }, // KS X 1001:1992
            new byte[] { 0x1b, 0x24, 0x28, 0x44 }, // JIS X 212-1990
            new byte[] { 0x1b, 0x24, 0x40 },       // JIS C 6226-1978
            new byte[] { 0x1b, 0x24, 0x41 },       // GB 2312-80
            new byte[] { 0x1b, 0x24, 0x42 },       // JIS X 208-1983
            new byte[] { 0x1b, 0x26, 0x40 },       // JIS X 208 1990, 1997
            new byte[] { 0x1b, 0x28, 0x42 },       // ASCII
            new byte[] { 0x1b, 0x28, 0x48 },       // JIS-Roman
            new byte[] { 0x1b, 0x28, 0x49 },       // Half-width katakana
            new byte[] { 0x1b, 0x28, 0x4a },       // JIS-Roman
            new byte[] { 0x1b, 0x2e, 0x41 },       // ISO 8859-1
            new byte[] { 0x1b, 0x2e, 0x46 },       // ISO 8859-7
        };

        private static readonly byte[][] Escapes2022KR =
        {
            new byte[] { 0x1b, 0x24, 0x29, 0x43 },
        };

        private static readonly byte[][] Escapes2022CN =
        {
            new byte[] { 0x1b, 0x24, 0x29, 0x41 }, // GB 2312-80
            new byte[] { 0x1b, 0x24, 0x29, 0x47 }, // CNS 11643-1992 Plane 1
            new byte[] { 0x1b, 0x24, 0x2A, 0x48 }, // CNS 11643-1992 Plane 2
            new byte[] { 0x1b, 0x24, 0x29, 0x45 }, // ISO-IR-165
            new byte[] { 0x1b, 0x24, 0x2B, 0x49 }, // CNS 11643-1992 Plane 3
            new byte[] { 0x1b, 0x24, 0x2B, 0x4A }, // CNS 11643-1992 Plane 4
            new byte[] { 0x1b, 0x24, 0x2B, 0x4B }, // CNS 11643-1992 Plane 5
            new byte[] { 0x1b, 0x24, 0x2B, 0x4C }, // CNS 11643-1992 Plane 6
            new byte[] { 0x1b, 0x24, 0x2B, 0x4D }, // CNS 11643-1992 Plane 7
            new byte[] { 0x1b, 0x4e },             // SS2
            new byte[] { 0x1b, 0x4f },             // SS3
        };

        /// <summary>CharsetRecog_2022::match_2022.</summary>
        private static int Match2022(ReadOnlySpan<byte> text, byte[][] escapeSequences)
        {
            int hits = 0, misses = 0, shifts = 0;
            int textLen = text.Length;
            int i = 0;
            while (i < textLen)
            {
                bool matched = false;
                if (text[i] == 0x1B)
                {
                    for (int escN = 0; escN < escapeSequences.Length; escN++)
                    {
                        byte[] seq = escapeSequences[escN];
                        int seqLength = seq.Length;
                        if (textLen - i >= seqLength)
                        {
                            int j = 1;
                            while (j < seqLength && seq[j] == text[i + j])
                                j += 1;
                            if (j == seqLength)
                            {
                                hits += 1;
                                i += seqLength - 1;
                                matched = true;
                                break;
                            }
                        }
                    }
                    if (!matched)
                        misses += 1;
                }
                if (!matched && (text[i] == 0x0e || text[i] == 0x0f))
                    shifts += 1;
                i += 1;
            }

            if (hits == 0)
                return 0;

            int quality = (100 * hits - 100 * misses) / (hits + misses);
            if (hits + shifts < 5)
                quality -= (5 - (hits + shifts)) * 10;
            if (quality < 0)
                quality = 0;
            return quality;
        }
    }
}
