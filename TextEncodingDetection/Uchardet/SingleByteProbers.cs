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

/// <summary>nsSingleByteCharSetProber (POSITIVE_APPROACH build).</summary>
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
    byte _lastOrder;
    uint _totalSeqs, _totalChar, _freqChar;
    readonly uint[] _seqCounters = new uint[4];

    public SingleByteCharSetProber(SequenceModel model, bool reversed = false, CharSetProber? nameProber = null)
    {
        _model = model; _reversed = reversed; _nameProber = nameProber;
        Reset();
    }

    public override ProbingState HandleData(byte[] buf, int start, int len)
    {
        // Same loop as the C++ one; the counters live in locals for the duration of the loop.
        byte[] map = _model.CharToOrderMap, matrix = _model.PrecedenceMatrix;
        uint[] seq = _seqCounters;
        byte lastOrder = _lastOrder;
        uint totalChar = _totalChar, freqChar = _freqChar, totalSeqs = _totalSeqs;
        bool reversed = _reversed;
        for (int i = start, end = start + len; i < end; i++)
        {
            byte order = map[buf[i]];
            if (order < SymbolCatOrder) totalChar++;
            if (order < SampleSize)
            {
                freqChar++;
                if (lastOrder < SampleSize)
                {
                    totalSeqs++;
                    seq[reversed ? matrix[order * SampleSize + lastOrder] : matrix[lastOrder * SampleSize + order]]++;
                }
            }
            lastOrder = order;
        }
        _lastOrder = lastOrder; _totalChar = totalChar; _freqChar = freqChar; _totalSeqs = totalSeqs;
        if (_state == ProbingState.Detecting && _totalSeqs > SbEnoughRelThreshold)
        {
            float cf = GetConfidence();
            if (cf > PositiveShortcutThreshold) _state = ProbingState.FoundIt;
            else if (cf < NegativeShortcutThreshold) _state = ProbingState.NotMe;
        }
        return _state;
    }

    public override void Reset()
    {
        _state = ProbingState.Detecting;
        _lastOrder = 255;
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

    public override ProbingState HandleData(byte[] buf, int start, int len)
    {
        if (GetState() == ProbingState.NotMe) return ProbingState.NotMe;
        for (int i = start, end = start + len; i < end; i++)
        {
            byte cur = buf[i];
            if (cur == (byte)' ')
            {
                if (_beforePrev != (byte)' ')
                {
                    if (IsFinal(_prev)) ++_finalCharLogicalScore;
                    else if (IsNonFinal(_prev)) ++_finalCharVisualScore;
                }
            }
            else if (_beforePrev == (byte)' ' && IsFinal(_prev) && cur != (byte)' ')
                ++_finalCharVisualScore;
            _beforePrev = _prev;
            _prev = cur;
        }
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

/// <summary>nsLatin1Prober: windows-1252 by character-class pair plausibility.</summary>
internal sealed class Latin1Prober : CharSetProber
{
    const int ClassNum = 8; // CLASS_NUM
    const byte Oth = 1;
    ProbingState _state;
    byte _lastCharClass;
    readonly uint[] _freqCounter = new uint[4]; // FREQ_CAT_NUM
    byte[] _scratch = [];

    public Latin1Prober() => Reset();

    public override void Reset()
    {
        _state = ProbingState.Detecting;
        _lastCharClass = Oth;
        Array.Clear(_freqCounter, 0, _freqCounter.Length);
    }

    public override ProbingState HandleData(byte[] buf, int start, int len)
    {
        if (_scratch.Length < len) _scratch = new byte[len];
        int n = FilterWithEnglishLetters(buf, start, len, _scratch);
        byte[] toClass = UchardetTables.Latin1_CharToClass, model = UchardetTables.Latin1ClassModel;
        for (int i = 0; i < n; i++)
        {
            byte charClass = toClass[_scratch[i]];
            byte freq = model[_lastCharClass * ClassNum + charClass];
            if (freq == 0) { _state = ProbingState.NotMe; break; }
            _freqCounter[freq]++;
            _lastCharClass = charClass;
        }
        return _state;
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
/// models are disabled in the source, as there).</summary>
internal sealed class SbcsGroupProber : CharSetProber
{
    readonly CharSetProber[] _probers;
    readonly bool[] _isActive;
    int _activeNum, _bestGuess;
    ProbingState _state;
    byte[] _scratch = [];

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

    public override ProbingState HandleData(byte[] buf, int start, int len)
    {
        if (_scratch.Length < len) _scratch = new byte[len];
        int newLen = FilterWithoutEnglishLetters(buf, start, len, _scratch);
        if (newLen == 0) return _state;
        for (int i = 0; i < _probers.Length; i++)
        {
            if (!_isActive[i]) continue;
            ProbingState st = _probers[i].HandleData(_scratch, 0, newLen);
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
