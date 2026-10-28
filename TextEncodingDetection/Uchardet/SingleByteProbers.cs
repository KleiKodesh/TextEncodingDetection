// ***** BEGIN LICENSE BLOCK *****
// Version: MPL 1.1/GPL 2.0/LGPL 2.1
// C# port of nsSBCharSetProber.cpp, nsHebrewProber.cpp, nsLatin1Prober.cpp and
// nsSBCSGroupProber.cpp from the uchardet copy bundled in Notepad++ (PowerEditor/src/uchardet).
// The Original Code is Mozilla Universal charset detector code.
// The Initial Developer of the Original Code is Netscape Communications Corporation
// (nsHebrewProber: Shy Shalom <shooshX@gmail.com>, Copyright (C) 2005).
// Portions created by the Initial Developer are Copyright (C) 1998/2001 the Initial Developer.
// All Rights Reserved. Used here under the Mozilla Public License 1.1; see NOTICE.
// ***** END LICENSE BLOCK *****

namespace TextEncodingDetection.Uchardet;

/// <summary>
/// Byte-pair and byte counts of one filtered stream, shared by every single-byte model.
/// <para>Why this is exact: nsSBCSGroupProber feeds all its probers the same filtered buffer in one
/// HandleData call (Notepad++ calls uchardet_handle_data once), and a model's counters are plain
/// sums over that buffer: mTotalChar / mFreqChar count bytes by their order, and mTotalSeqs /
/// mSeqCounters count each adjacent byte pair (a, b) whose orders are both below SAMPLE_SIZE
/// (mLastOrder starts at 255, so the first byte opens no pair). Adding each distinct pair's count
/// gives the same integers; the state test and the float confidence then run on identical inputs.</para>
/// </summary>
internal sealed class BytePairCounts
{
    [ThreadStatic] static BytePairCounts? t_instance;
    public static BytePairCounts ForCurrentThread() => t_instance ??= new BytePairCounts();

    /// <summary>[first &lt;&lt; 8 | second]; row 256 collects the "pair" opened by the stream's first
    /// byte, which no model reads (mLastOrder 255 there), so counting needs no branch.</summary>
    public readonly int[] Pair = new int[257 * 256];
    public readonly int[] Single = new int[256];
    const int NoPrevious = 256;

    /// <summary>Zeroes what the previous call wrote: a row can only be nonzero when its first
    /// byte occurred.</summary>
    void Clear()
    {
        for (int b = 0; b < 256; b++)
            if (Single[b] != 0) Array.Clear(Pair, b << 8, 256);
        Array.Clear(Pair, NoPrevious << 8, 256);
        Array.Clear(Single, 0, 256);
    }

    /// <summary>Length of the filtered stream last counted by <see cref="CountFiltered"/>.</summary>
    public int FilteredLength;
    /// <summary>nsHebrewProber's two final-letter scores over that stream.</summary>
    public int FinalLogicalScore, FinalVisualScore;

    const byte KindOther = 0, KindLetter = 1, KindHigh = 2;
    static readonly byte[] s_kind = BuildKind();
    static byte[] BuildKind()
    {
        var t = new byte[256];
        for (int b = 0; b < 256; b++)
            t[b] = b >= 0x80 ? KindHigh : CharSetProber.IsAsciiLetter((byte)b) ? KindLetter : KindOther;
        return t;
    }

    static bool IsFinal(byte c) => c == 0xea || c == 0xed || c == 0xef || c == 0xf3 || c == 0xf5;
    static bool IsNonFinal(byte c) => c == 0xeb || c == 0xee || c == 0xf0 || c == 0xf4;

    /// <summary>
    /// FilterWithoutEnglishLetters(<paramref name="raw"/>) counted without materialising it: the
    /// filtered stream is the kept words (runs of letters / high bytes that contain a high byte),
    /// each followed by one space when a separator ended it (the tail word gets none). Pairs and
    /// bytes are counted as <see cref="Count"/> would count that stream.
    /// <para>The Hebrew final-letter scores are taken per word. In that stream a space is always
    /// preceded by a non-space and words hold no spaces, so nsHebrewProber::HandleData (starting
    /// from mPrev = mBeforePrev = ' ') scores, for each word w of length L &gt;= 2 only: +visual when
    /// w[0] is a final letter (cur = w[1], beforePrev = ' '); and when a space follows w, +logical
    /// when w[L-1] is final, else +visual when it is a non-final Kaf/Mem/Nun/Pe (cur = ' ',
    /// beforePrev = w[L-2]). A one-letter word sees beforePrev = ' ' at its space and scores
    /// nothing; no other position matches a rule.</para>
    /// </summary>
    public void CountFiltered(ReadOnlySpan<byte> raw)
    {
        Clear();
        int[] pair = Pair, single = Single;
        byte[] kind = s_kind;
        int outLen = 0, logical = 0, visual = 0;
        int p = NoPrevious; // last byte of the stream so far
        int prev = 0, end = raw.Length;
        bool meetMsb = false; // a high byte since prev: the current word will be kept
        for (int cur = 0; cur < end; cur++)
        {
            int b = raw[cur];
            int k = kind[b];
            if (k != KindOther)
            {
                if (!meetMsb)
                {
                    if (k == KindLetter) continue; // pending: kept only if a high byte follows
                    meetMsb = true;
                    for (int j = prev; j < cur; j++) // the pending letters
                    {
                        int q = raw[j];
                        single[q]++;
                        pair[(p << 8) | q]++;
                        p = q;
                    }
                }
                single[b]++;
                pair[(p << 8) | b]++;
                p = b;
                continue;
            }
            // A separator.
            if (meetMsb)
            {
                outLen += cur - prev + 1;
                Score(raw.Slice(prev, cur - prev), true, ref logical, ref visual);
                single[' ']++;
                pair[(p << 8) | ' ']++;
                p = ' ';
                meetMsb = false;
                prev = cur + 1;
                continue;
            }
            prev = cur + 1;
            // Pure-ASCII stretch up to the next high byte: its words are all dropped, so the
            // only effect is prev = (last separator in it) + 1.
            int h = Swar.SkipAscii(raw, cur + 1);
            if (h - cur > 8)
            {
                int j = h - 1;
                while (j > cur && kind[raw[j]] == KindLetter) j--;
                prev = j + 1;
                cur = h - 1;
            }
        }
        if (meetMsb)
        {
            outLen += end - prev;
            Score(raw.Slice(prev, end - prev), false, ref logical, ref visual);
        }

        static void Score(ReadOnlySpan<byte> word, bool spaceFollows, ref int logical, ref int visual)
        {
            if (word.Length < 2) return;
            if (IsFinal(word[0])) visual++;
            if (spaceFollows)
            {
                byte lastLetter = word[word.Length - 1];
                if (IsFinal(lastLetter)) logical++;
                else if (IsNonFinal(lastLetter)) visual++;
            }
        }
        FilteredLength = outLen;
        FinalLogicalScore = logical; FinalVisualScore = visual;
    }

    public void Count(ReadOnlySpan<byte> s)
    {
        Clear();
        int[] pair = Pair, single = Single;
        int prev = NoPrevious;
        foreach (byte b in s)
        {
            single[b]++;
            pair[(prev << 8) | b]++;
            prev = b;
        }
    }
}

/// <summary>nsSingleByteCharSetProber (POSITIVE_APPROACH build), fed from <see cref="BytePairCounts"/>.</summary>
internal sealed class SingleByteCharSetProber : CharSetProber
{
    const int SampleSize = 64;               // SAMPLE_SIZE
    const int SbEnoughRelThreshold = 1024;   // SB_ENOUGH_REL_THRESHOLD
    const float PositiveShortcutThreshold = 0.95f;
    const float NegativeShortcutThreshold = 0.05f;
    const int SymbolCatOrder = 250;          // SYMBOL_CAT_ORDER
    const int PositiveCat = 3;               // NUMBER_OF_SEQ_CAT - 1

    readonly SequenceModel _model;
    readonly bool _reversed;
    readonly CharSetProber? _nameProber;
    ProbingState _state;
    uint _totalSeqs, _totalChar, _freqChar;
    readonly uint[] _seqCounters = new uint[4];

    public SingleByteCharSetProber(SequenceModel model, bool reversed = false, CharSetProber? nameProber = null)
    {
        _model = model; _reversed = reversed; _nameProber = nameProber;
        Reset();
    }

    /// <summary>The C++ HandleData, as the first and only call after Reset, over the stream that
    /// <paramref name="counts"/> describes.</summary>
    public ProbingState HandlePairCounts(BytePairCounts counts)
    {
        // Totals are set, not added: only valid as the single call after Reset (as Notepad++ makes).
        if (_fed) throw new InvalidOperationException("one HandleData per Reset");
        _fed = true;
        byte[] map = _model.CharToOrderMap, matrix = _model.PrecedenceMatrix;
        int[] single = counts.Single, pair = counts.Pair;
        uint[] seq = _seqCounters;
        uint totalChar = 0, freqChar = 0, totalSeqs = 0;
        for (int b = 0; b < 256; b++)
        {
            uint c = (uint)single[b];
            if (c == 0) continue;
            byte order = map[b];
            if (order < SymbolCatOrder) totalChar += c;
            if (order < SampleSize) freqChar += c;
        }
        // Every pair of frequent bytes (order < SAMPLE_SIZE): the pairs that count.
        bool reversed = _reversed;
        foreach (byte a in _model.FrequentBytes)
        {
            int rowA = a << 8, oa = map[a];
            foreach (byte b in _model.FrequentBytes)
            {
                uint c = (uint)pair[rowA | b];
                if (c == 0) continue;
                int ob = map[b];
                totalSeqs += c;
                seq[reversed ? matrix[ob * SampleSize + oa] : matrix[oa * SampleSize + ob]] += c;
            }
        }
        _totalChar = totalChar; _freqChar = freqChar; _totalSeqs = totalSeqs;
        if (_state == ProbingState.Detecting && _totalSeqs > SbEnoughRelThreshold)
        {
            float cf = GetConfidence();
            if (cf > PositiveShortcutThreshold) _state = ProbingState.FoundIt;
            else if (cf < NegativeShortcutThreshold) _state = ProbingState.NotMe;
        }
        return _state;
    }

    public override ProbingState HandleData(ReadOnlySpan<byte> buf)
    {
        var counts = BytePairCounts.ForCurrentThread();
        counts.Count(buf);
        return HandlePairCounts(counts);
    }

    bool _fed;

    public override void Reset()
    {
        _fed = false;
        _state = ProbingState.Detecting;
        Array.Clear(_seqCounters, 0, _seqCounters.Length);
        _totalSeqs = 0; _totalChar = 0; _freqChar = 0;
    }

    public override float GetConfidence()
    {
        if (_totalSeqs > 0)
        {
            // Explicit float casts keep every step in single precision, as in the C++ code.
            float r = (float)((float)_seqCounters[PositiveCat] / (float)_totalSeqs);
            r = (float)(r / _model.TypicalPositiveRatio);
            r = (float)(r * (float)_freqChar);
            r = (float)(r / (float)_totalChar);
            if (r >= 1.00f) r = 0.99f;
            return r;
        }
        return 0.01f;
    }

    public override ProbingState GetState() => _state;
    public override string? GetCharSetName() => _nameProber == null ? _model.CharsetName : _nameProber.GetCharSetName();
}

/// <summary>nsHebrewProber: final-letter evidence for logical (windows-1255) vs visual (ISO-8859-8).</summary>
internal sealed class HebrewProber : CharSetProber
{
    const byte FinalKaf = 0xea, NormalKaf = 0xeb, FinalMem = 0xed, NormalMem = 0xee, FinalNun = 0xef,
        NormalNun = 0xf0, FinalPe = 0xf3, NormalPe = 0xf4, FinalTsadi = 0xf5;
    const int MinFinalCharDistance = 5;
    const double MinModelDistance = 0.01; // a double literal in the C++ source
    internal const string VisualHebrewName = "ISO-8859-8";
    internal const string LogicalHebrewName = "windows-1255";

    int _finalCharLogicalScore, _finalCharVisualScore;
    byte _prev, _beforePrev;
    CharSetProber? _logicalProb, _visualProb;

    public HebrewProber() => Reset();

    public void SetModelProbers(CharSetProber logical, CharSetProber visual) { _logicalProb = logical; _visualProb = visual; }

    static bool IsFinal(byte c) => c == FinalKaf || c == FinalMem || c == FinalNun || c == FinalPe || c == FinalTsadi;
    // Tsadi is deliberately absent (see the original comments in nsHebrewProber.cpp).
    static bool IsNonFinal(byte c) => c == NormalKaf || c == NormalMem || c == NormalNun || c == NormalPe;

    public override ProbingState HandleData(ReadOnlySpan<byte> buf)
    {
        if (GetState() == ProbingState.NotMe) return ProbingState.NotMe;
        byte prev = _prev, beforePrev = _beforePrev;
        int logical = _finalCharLogicalScore, visual = _finalCharVisualScore;
        for (int i = 0; i < buf.Length; i++)
        {
            byte cur = buf[i];
            // Every rule needs prev to be a final or non-final letter, all of which are >= 0xea.
            if (prev >= FinalKaf)
            {
                if (cur == (byte)' ')
                {
                    if (beforePrev != (byte)' ')
                    {
                        if (IsFinal(prev)) ++logical;
                        else if (IsNonFinal(prev)) ++visual;
                    }
                }
                else if (beforePrev == (byte)' ' && IsFinal(prev))
                    ++visual;
            }
            beforePrev = prev;
            prev = cur;
        }
        _prev = prev; _beforePrev = beforePrev;
        _finalCharLogicalScore = logical; _finalCharVisualScore = visual;
        return ProbingState.Detecting;
    }

    /// <summary>HandleData over the filtered stream, as the first call after Reset: adds the
    /// scores <see cref="BytePairCounts.CountFiltered"/> derived for that stream.</summary>
    public ProbingState HandleCounts(BytePairCounts counts)
    {
        if (GetState() == ProbingState.NotMe) return ProbingState.NotMe;
        // Per-word scores of one whole stream: only valid as the single call after Reset.
        if (_finalCharLogicalScore != 0 || _finalCharVisualScore != 0 || _prev != (byte)' ')
            throw new InvalidOperationException("one HandleData per Reset");
        _prev = 0; // marks it fed
        _finalCharLogicalScore += counts.FinalLogicalScore;
        _finalCharVisualScore += counts.FinalVisualScore;
        return ProbingState.Detecting;
    }

    public override string GetCharSetName()
    {
        int finalsub = _finalCharLogicalScore - _finalCharVisualScore;
        if (finalsub >= MinFinalCharDistance) return LogicalHebrewName;
        if (finalsub <= -MinFinalCharDistance) return VisualHebrewName;
        float modelsub = (float)(_logicalProb!.GetConfidence() - _visualProb!.GetConfidence());
        if (modelsub > MinModelDistance) return LogicalHebrewName;
        if (modelsub < -MinModelDistance) return VisualHebrewName;
        if (finalsub < 0) return VisualHebrewName;
        return LogicalHebrewName;
    }

    public override void Reset()
    {
        _finalCharLogicalScore = 0; _finalCharVisualScore = 0;
        _prev = (byte)' '; _beforePrev = (byte)' ';
    }

    public override ProbingState GetState() =>
        _logicalProb!.GetState() == ProbingState.NotMe && _visualProb!.GetState() == ProbingState.NotMe
            ? ProbingState.NotMe : ProbingState.Detecting;

    public override float GetConfidence() => 0.0f;
}

/// <summary>nsLatin1Prober: windows-1252 by character-class pair plausibility.
/// <para>The C++ loop walks the FilterWithEnglishLetters output pair by pair, adds each pair's
/// frequency category to mFreqCounter and stops with eNotMe at the first category-0 pair. Here the
/// filter feeds the class pairs straight into a pair histogram (no output buffer) that is applied
/// afterwards: any category-0 pair gives eNotMe (after which GetConfidence ignores the counters),
/// otherwise mFreqCounter gets the same sums. One HandleData call, as Notepad++ makes.</para></summary>
internal sealed class Latin1Prober : CharSetProber
{
    const int ClassNum = 8; // CLASS_NUM
    const byte Oth = 1;
    ProbingState _state;
    byte _lastCharClass;
    readonly uint[] _freqCounter = new uint[4]; // FREQ_CAT_NUM

    public Latin1Prober() => Reset();

    public override void Reset()
    {
        _state = ProbingState.Detecting;
        _lastCharClass = Oth;
        Array.Clear(_freqCounter, 0, _freqCounter.Length);
    }

    const int Separator = 8, TagOpen = 9, TagClose = 10;

    /// <summary>Per byte: its Latin-1 class (0..7) when FilterWithEnglishLetters keeps it in a
    /// word (ASCII letter or high byte), else Separator / TagOpen ('&lt;') / TagClose ('&gt;').</summary>
    static readonly byte[] s_kind = BuildKind();

    static byte[] BuildKind()
    {
        var t = new byte[256];
        for (int b = 0; b < 256; b++)
            t[b] = b >= 0x80 || IsAsciiLetter((byte)b) ? UchardetTables.Latin1_CharToClass[b]
                : b == '<' ? (byte)TagOpen : b == '>' ? (byte)TagClose : (byte)Separator;
        return t;
    }

    public override ProbingState HandleData(ReadOnlySpan<byte> buf)
    {
        // FilterWithEnglishLetters fused with the class-pair count. A word's pairs are counted as
        // its bytes go by; the separator that ends it decides whether the filter keeps it
        // (cur > prev && !isInTag), and a dropped word's pairs are taken back out again.
        // Four interleaved histograms keep back-to-back increments of one cell independent; only
        // their sum is used.
        Span<int> hist = stackalloc int[4 * 64];
        hist.Clear();
        byte[] kind = s_kind;
        int spaceClass = UchardetTables.Latin1_CharToClass[(byte)' '];
        int last = _lastCharClass, lastBeforeWord = last, wordStart = 0, h = 0;
        bool isInTag = false;
        int cur = 0, end = buf.Length;
        for (; cur < end; cur++)
        {
            int c = kind[buf[cur]];
            if (c < Separator)
            {
                hist[h | (last << 3) | c]++;
                h = (h + 64) & 255;
                last = c;
                continue;
            }
            if (c == TagClose) isInTag = false;
            else if (c == TagOpen) isInTag = true;
            if (cur > wordStart)
            {
                if (!isInTag)
                {
                    hist[h | (last << 3) | spaceClass]++;
                    h = (h + 64) & 255;
                    last = spaceClass;
                }
                else
                    last = TakeBack(buf.Slice(wordStart, cur - wordStart), kind, hist, lastBeforeWord);
            }
            wordStart = cur + 1;
            lastBeforeWord = last;
        }
        if (isInTag && cur > wordStart)
            last = TakeBack(buf.Slice(wordStart, cur - wordStart), kind, hist, lastBeforeWord);
        _lastCharClass = (byte)last;

        byte[] model = UchardetTables.Latin1ClassModel;
        for (int pairIndex = 0; pairIndex < ClassNum * ClassNum; pairIndex++)
        {
            uint n = (uint)(hist[pairIndex] + hist[64 + pairIndex] + hist[128 + pairIndex] + hist[192 + pairIndex]);
            if (n == 0) continue;
            byte freq = model[pairIndex];
            if (freq == 0) { _state = ProbingState.NotMe; break; }
            _freqCounter[freq] += n;
        }
        return _state;
    }

    /// <summary>Removes a dropped word's pairs; returns the class before it.</summary>
    static int TakeBack(ReadOnlySpan<byte> word, byte[] kind, Span<int> hist, int lastBeforeWord)
    {
        int l = lastBeforeWord;
        foreach (byte b in word)
        {
            int cls = kind[b];
            hist[(l << 3) | cls]--;
            l = cls;
        }
        return lastBeforeWord;
    }

    public override float GetConfidence()
    {
        if (_state == ProbingState.NotMe) return 0.01f;
        float confidence;
        uint total = 0;
        for (int i = 0; i < _freqCounter.Length; i++) total += _freqCounter[i];
        if (total == 0)
            confidence = 0.0f;
        else
        {
            float floatTotal = total;
            confidence = (float)((float)_freqCounter[3] / floatTotal);
            confidence = (float)(confidence - (float)((float)((float)_freqCounter[1] * 20.0f) / floatTotal));
        }
        if (confidence < 0.0f) confidence = 0.0f;
        confidence = (float)(confidence * 0.50f);
        return confidence;
    }

    public override ProbingState GetState() => _state;
    public override string GetCharSetName() => "windows-1252";
}

/// <summary>nsSBCSGroupProber: the 14 single-byte probers in Notepad++'s order (Hungarian
/// models are disabled in the source, as there). The filtered buffer is counted once into
/// <see cref="BytePairCounts"/>, which every sequence model then reads.</summary>
internal sealed class SbcsGroupProber : CharSetProber
{
    readonly CharSetProber[] _probers;
    readonly bool[] _isActive;
    int _activeNum, _bestGuess;
    ProbingState _state;

    public SbcsGroupProber()
    {
        var hebrew = new HebrewProber();
        var logical = new SingleByteCharSetProber(UchardetTables.Win1255, false, hebrew);
        var visual = new SingleByteCharSetProber(UchardetTables.Win1255, true, hebrew);
        hebrew.SetModelProbers(logical, visual);
        _probers =
        [
            new SingleByteCharSetProber(UchardetTables.Win1251),
            new SingleByteCharSetProber(UchardetTables.Koi8r),
            new SingleByteCharSetProber(UchardetTables.Latin5),
            new SingleByteCharSetProber(UchardetTables.MacCyrillic),
            new SingleByteCharSetProber(UchardetTables.Ibm866),
            new SingleByteCharSetProber(UchardetTables.Ibm855),
            new SingleByteCharSetProber(UchardetTables.Latin7),
            new SingleByteCharSetProber(UchardetTables.Win1253),
            new SingleByteCharSetProber(UchardetTables.Latin5Bulgarian),
            new SingleByteCharSetProber(UchardetTables.Win1251Bulgarian),
            new SingleByteCharSetProber(UchardetTables.TIS620Thai),
            hebrew,
            logical,
            visual,
        ];
        _isActive = new bool[_probers.Length];
        Reset();
    }

    public override string? GetCharSetName()
    {
        if (_bestGuess == -1)
        {
            GetConfidence();
            if (_bestGuess == -1) _bestGuess = 0;
        }
        return _probers[_bestGuess].GetCharSetName();
    }

    public override void Reset()
    {
        _activeNum = 0;
        for (int i = 0; i < _probers.Length; i++)
        {
            _probers[i].Reset();
            _isActive[i] = true;
            ++_activeNum;
        }
        _bestGuess = -1;
        _state = ProbingState.Detecting;
    }

    public override ProbingState HandleData(ReadOnlySpan<byte> buf)
    {
        {
            var counts = BytePairCounts.ForCurrentThread();
            counts.CountFiltered(buf);
            if (counts.FilteredLength == 0) return _state;
            for (int i = 0; i < _probers.Length; i++)
            {
                if (!_isActive[i]) continue;
                ProbingState st = _probers[i] switch
                {
                    SingleByteCharSetProber sb => sb.HandlePairCounts(counts),
                    HebrewProber hebrew => hebrew.HandleCounts(counts),
                    var other => throw new InvalidOperationException(other.GetType().Name),
                };
                if (st == ProbingState.FoundIt)
                {
                    _bestGuess = i;
                    _state = ProbingState.FoundIt;
                    break;
                }
                if (st == ProbingState.NotMe)
                {
                    _isActive[i] = false;
                    if (--_activeNum <= 0)
                    {
                        _state = ProbingState.NotMe;
                        break;
                    }
                }
            }
            return _state;
        }
    }

    public override float GetConfidence()
    {
        switch (_state)
        {
            case ProbingState.FoundIt: return 0.99f;
            case ProbingState.NotMe: return 0.01f;
        }
        float bestConf = 0.0f;
        for (int i = 0; i < _probers.Length; i++)
        {
            if (!_isActive[i]) continue;
            float cf = _probers[i].GetConfidence();
            if (bestConf < cf) { bestConf = cf; _bestGuess = i; }
        }
        return bestConf;
    }

    public override ProbingState GetState() => _state;
}
