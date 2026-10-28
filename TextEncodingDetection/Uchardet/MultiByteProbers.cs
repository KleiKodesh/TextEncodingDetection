// ***** BEGIN LICENSE BLOCK *****
// Version: MPL 1.1/GPL 2.0/LGPL 2.1
// C# port of CharDistribution.h/.cpp, JpCntx.h/.cpp, nsUTF8Prober.cpp, nsSJISProber.cpp,
// nsEUCJPProber.cpp, nsGB2312Prober.cpp (GB18030), nsEUCKRProber.cpp, nsBig5Prober.cpp,
// nsEUCTWProber.cpp, nsMBCSGroupProber.cpp and nsEscCharsetProber.cpp from the uchardet copy
// bundled in Notepad++ (PowerEditor/src/uchardet). The Original Code is Mozilla Universal
// charset detector code. The Initial Developer of the Original Code is Netscape Communications
// Corporation. Portions created by the Initial Developer are Copyright (C) 1998 the Initial
// Developer. All Rights Reserved. Used here under the Mozilla Public License 1.1; see NOTICE.
//
// Notepad++'s copy differs from Mozilla's: its multi-byte probers ignore eError (they never
// turn eNotMe) and its MBCS group never deactivates a prober. Ported as found.
// ***** END LICENSE BLOCK *****

namespace TextEncodingDetection.Uchardet;

/// <summary>CharDistributionAnalysis and its subclasses (GetOrder per encoding).</summary>
internal abstract class CharDistributionAnalysis
{
    const uint EnoughDataThreshold = 1024;
    const uint MinimumDataThreshold = 4;
    const float SureYes = 0.99f, SureNo = 0.01f;

    uint _freqChars, _totalChars, _dataThreshold;
    readonly short[] _charToFreqOrder;
    readonly float _typicalDistributionRatio;

    protected CharDistributionAnalysis(short[] charToFreqOrder, float typicalDistributionRatio)
    {
        _charToFreqOrder = charToFreqOrder;
        _typicalDistributionRatio = typicalDistributionRatio;
        Reset(false);
    }

    protected abstract int GetOrder(byte b0, byte b1);

    public void HandleOneChar(byte b0, byte b1, int charLen)
    {
        int order = charLen == 2 ? GetOrder(b0, b1) : -1;
        if (order >= 0)
        {
            _totalChars++;
            if ((uint)order < (uint)_charToFreqOrder.Length && 512 > _charToFreqOrder[order])
                _freqChars++;
        }
    }

    public float GetConfidence()
    {
        if (_totalChars <= 0 || _freqChars <= _dataThreshold) return SureNo;
        if (_totalChars != _freqChars)
        {
            float r = (float)((float)_freqChars / (float)((float)(_totalChars - _freqChars) * _typicalDistributionRatio));
            if (r < SureYes) return r;
        }
        return SureYes;
    }

    public void Reset(bool isPreferredLanguage)
    {
        _totalChars = 0;
        _freqChars = 0;
        _dataThreshold = isPreferredLanguage ? 0 : MinimumDataThreshold;
    }

    public bool GotEnoughData => _totalChars > EnoughDataThreshold;
}

internal sealed class EucTwDistributionAnalysis() : CharDistributionAnalysis(UchardetTables.EucTw_CharToFreqOrder, UchardetTables.EucTw_TypicalDistributionRatio)
{
    protected override int GetOrder(byte b0, byte b1) => b0 >= 0xc4 ? 94 * (b0 - 0xc4) + b1 - 0xa1 : -1;
}

internal sealed class EucKrDistributionAnalysis() : CharDistributionAnalysis(UchardetTables.EucKr_CharToFreqOrder, UchardetTables.EucKr_TypicalDistributionRatio)
{
    protected override int GetOrder(byte b0, byte b1) => b0 >= 0xb0 ? 94 * (b0 - 0xb0) + b1 - 0xa1 : -1;
}

internal sealed class Gb2312DistributionAnalysis() : CharDistributionAnalysis(UchardetTables.Gb2312_CharToFreqOrder, UchardetTables.Gb2312_TypicalDistributionRatio)
{
    protected override int GetOrder(byte b0, byte b1) => b0 >= 0xb0 && b1 >= 0xa1 ? 94 * (b0 - 0xb0) + b1 - 0xa1 : -1;
}

internal sealed class Big5DistributionAnalysis() : CharDistributionAnalysis(UchardetTables.Big5_CharToFreqOrder, UchardetTables.Big5_TypicalDistributionRatio)
{
    protected override int GetOrder(byte b0, byte b1)
    {
        if (b0 < 0xa4) return -1;
        return b1 >= 0xa1 ? 157 * (b0 - 0xa4) + b1 - 0xa1 + 63 : 157 * (b0 - 0xa4) + b1 - 0x40;
    }
}

internal sealed class SjisDistributionAnalysis() : CharDistributionAnalysis(UchardetTables.Jis_CharToFreqOrder, UchardetTables.Jis_TypicalDistributionRatio)
{
    protected override int GetOrder(byte b0, byte b1)
    {
        int order;
        if (b0 >= 0x81 && b0 <= 0x9f) order = 188 * (b0 - 0x81);
        else if (b0 >= 0xe0 && b0 <= 0xef) order = 188 * (b0 - 0xe0 + 31);
        else return -1;
        order += b1 - 0x40;
        if (b1 > 0x7f) order--;
        return order;
    }
}

internal sealed class EucJpDistributionAnalysis() : CharDistributionAnalysis(UchardetTables.Jis_CharToFreqOrder, UchardetTables.Jis_TypicalDistributionRatio)
{
    protected override int GetOrder(byte b0, byte b1) => b0 >= 0xa0 ? 94 * (b0 - 0xa1) + b1 - 0xa1 : -1;
}

/// <summary>JapaneseContextAnalysis (hiragana pair frequencies). Only HandleOneChar is reached
/// from the probers; the byte-buffer HandleData of JpCntx.cpp has no caller and is not ported.</summary>
internal abstract class JapaneseContextAnalysis
{
    const int NumOfCategory = 6;
    const uint EnoughRelThreshold = 100, MaxRelThreshold = 1000, MinimumDataThreshold = 4;
    const float DontKnow = -1f;

    readonly uint[] _relSample = new uint[NumOfCategory];
    uint _totalRel, _dataThreshold;
    int _lastCharOrder;
    bool _done;

    protected JapaneseContextAnalysis() => Reset(false);

    protected abstract int GetOrder(byte b0, byte b1);

    public void HandleOneChar(byte b0, byte b1, int charLen)
    {
        if (_totalRel > MaxRelThreshold) _done = true;
        if (_done) return;
        int order = charLen == 2 ? GetOrder(b0, b1) : -1;
        if (order != -1 && _lastCharOrder != -1)
        {
            _totalRel++;
            _relSample[UchardetTables.Jp2CharContext[_lastCharOrder * 83 + order]]++;
        }
        _lastCharOrder = order;
    }

    public float GetConfidence() =>
        _totalRel > _dataThreshold ? (float)((float)(_totalRel - _relSample[0]) / (float)_totalRel) : DontKnow;

    public void Reset(bool isPreferredLanguage)
    {
        _totalRel = 0;
        Array.Clear(_relSample, 0, _relSample.Length);
        _lastCharOrder = -1;
        _done = false;
        _dataThreshold = isPreferredLanguage ? 0 : MinimumDataThreshold;
    }

    public bool GotEnoughData => _totalRel > EnoughRelThreshold;
}

internal sealed class SjisContextAnalysis : JapaneseContextAnalysis
{
    protected override int GetOrder(byte b0, byte b1) => b0 == 0x82 && b1 >= 0x9f && b1 <= 0xf1 ? b1 - 0x9f : -1;
}

internal sealed class EucJpContextAnalysis : JapaneseContextAnalysis
{
    protected override int GetOrder(byte b0, byte b1) => b0 == 0xa4 && b1 >= 0xa1 && b1 <= 0xf3 ? b1 - 0xa1 : -1;
}

/// <summary>nsUTF8Prober.</summary>
internal sealed class Utf8Prober : CharSetProber
{
    const float OneCharProb = 0.50f;
    readonly CodingStateMachine _codingSm = new(UchardetTables.Utf8Sm);
    ProbingState _state;
    uint _numOfMbChar;

    public Utf8Prober() => Reset();

    public override void Reset() { _codingSm.Reset(); _numOfMbChar = 0; _state = ProbingState.Detecting; }

    public override ProbingState HandleData(byte[] buf, int start, int len)
    {
        for (int i = start, end = start + len; i < end; i++)
        {
            SmState s = _codingSm.NextState(buf[i]);
            if (s == SmState.ItsMe) { _state = ProbingState.FoundIt; break; }
            if (s == SmState.Start && _codingSm.CurrentCharLen >= 2) _numOfMbChar++;
        }
        if (_state == ProbingState.Detecting && GetConfidence() > ShortcutThreshold) _state = ProbingState.FoundIt;
        return _state;
    }

    public override float GetConfidence()
    {
        float unlike = 0.99f;
        if (_numOfMbChar < 6)
        {
            for (uint i = 0; i < _numOfMbChar; i++) unlike = (float)(unlike * OneCharProb);
            return (float)(1.0f - unlike);
        }
        return 0.99f;
    }

    public override ProbingState GetState() => _state;
    public override string GetCharSetName() => "UTF-8";
}

/// <summary>The common shape of nsSJISProber, nsEUCJPProber, nsGB18030Prober, nsEUCKRProber,
/// nsBig5Prober and nsEUCTWProber: a coding state machine feeding a distribution analyser and,
/// for the two Japanese ones, a context analyser.</summary>
internal sealed class MultiByteDistributionProber : CharSetProber
{
    readonly CodingStateMachine _codingSm;
    readonly CharDistributionAnalysis _distribution;
    readonly JapaneseContextAnalysis? _context;
    readonly string _name;
    ProbingState _state;
    byte _lastChar0, _lastChar1; // mLastChar[2]

    MultiByteDistributionProber(StateMachineModel sm, CharDistributionAnalysis distribution, JapaneseContextAnalysis? context, string name)
    {
        _codingSm = new CodingStateMachine(sm);
        _distribution = distribution; _context = context; _name = name;
        Reset();
    }

    public static MultiByteDistributionProber Sjis() => new(UchardetTables.SjisSm, new SjisDistributionAnalysis(), new SjisContextAnalysis(), "Shift_JIS");
    public static MultiByteDistributionProber EucJp() => new(UchardetTables.EucJpSm, new EucJpDistributionAnalysis(), new EucJpContextAnalysis(), "EUC-JP");
    public static MultiByteDistributionProber Gb18030() => new(UchardetTables.Gb18030Sm, new Gb2312DistributionAnalysis(), null, "gb18030");
    public static MultiByteDistributionProber EucKr() => new(UchardetTables.EucKrSm, new EucKrDistributionAnalysis(), null, "EUC-KR");
    public static MultiByteDistributionProber Big5() => new(UchardetTables.Big5Sm, new Big5DistributionAnalysis(), null, "Big5");
    public static MultiByteDistributionProber EucTw() => new(UchardetTables.EucTwSm, new EucTwDistributionAnalysis(), null, "x-euc-tw");

    public override void Reset()
    {
        _codingSm.Reset();
        _state = ProbingState.Detecting;
        _context?.Reset(false);
        _distribution.Reset(false);
    }

    public override ProbingState HandleData(byte[] buf, int start, int len)
    {
        for (int i = 0; i < len; i++)
        {
            SmState s = _codingSm.NextState(buf[start + i]);
            if (s == SmState.ItsMe) { _state = ProbingState.FoundIt; break; }
            if (s != SmState.Start) continue;
            int charLen = _codingSm.CurrentCharLen;
            // Both analysers only read the two bytes when charLen == 2, i.e. the pair ending at i.
            byte b0, b1;
            if (i == 0)
            {
                _lastChar1 = buf[start];
                b0 = _lastChar0; b1 = _lastChar1;
            }
            else
            {
                b0 = buf[start + i - 1]; b1 = buf[start + i];
            }
            if (_context != null)
            {
                // nsSJISProber: context gets (i==0 ? mLastChar+2-charLen : aBuf+i+1-charLen); for
                // charLen 2 that is the same pair. nsEUCJPProber passes the same pointer as the
                // distribution analyser. Both only dereference when charLen == 2.
                _context.HandleOneChar(b0, b1, charLen);
            }
            _distribution.HandleOneChar(b0, b1, charLen);
        }
        _lastChar0 = buf[start + len - 1];
        if (_state == ProbingState.Detecting)
        {
            bool enough = _context != null ? _context.GotEnoughData : _distribution.GotEnoughData;
            if (enough && GetConfidence() > ShortcutThreshold) _state = ProbingState.FoundIt;
        }
        return _state;
    }

    public override float GetConfidence()
    {
        float distribCf = _distribution.GetConfidence();
        if (_context == null) return distribCf;
        float contxtCf = _context.GetConfidence();
        return contxtCf > distribCf ? contxtCf : distribCf;
    }

    public override ProbingState GetState() => _state;
    public override string GetCharSetName() => _name;
}

/// <summary>nsMBCSGroupProber (NS_FILTER_ALL): UTF-8, Shift_JIS, EUC-JP, GB18030, EUC-KR, Big5,
/// EUC-TW. Feeds each prober only the high-byte runs plus the byte after each run.</summary>
internal sealed class MbcsGroupProber : CharSetProber
{
    readonly CharSetProber[] _probers =
    [
        new Utf8Prober(),
        MultiByteDistributionProber.Sjis(),
        MultiByteDistributionProber.EucJp(),
        MultiByteDistributionProber.Gb18030(),
        MultiByteDistributionProber.EucKr(),
        MultiByteDistributionProber.Big5(),
        MultiByteDistributionProber.EucTw(),
    ];
    int _bestGuess;
    ProbingState _state;
    uint _keepNext;

    public MbcsGroupProber() => Reset();

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
        foreach (var p in _probers) p.Reset();
        _bestGuess = -1;
        _state = ProbingState.Detecting;
        _keepNext = 0;
    }

    public override ProbingState HandleData(byte[] buf, int start, int len)
    {
        int segStart = 0;
        uint keepNext = _keepNext;
        for (int pos = 0; pos < len; ++pos)
        {
            if ((buf[start + pos] & 0x80) != 0)
            {
                if (keepNext == 0) segStart = pos;
                keepNext = 2;
            }
            else if (keepNext != 0 && --keepNext == 0)
            {
                for (int i = 0; i < _probers.Length; i++)
                {
                    if (_probers[i].HandleData(buf, start + segStart, pos + 1 - segStart) == ProbingState.FoundIt)
                    {
                        _bestGuess = i;
                        _state = ProbingState.FoundIt;
                        return _state;
                    }
                }
            }
        }
        if (keepNext != 0)
        {
            for (int i = 0; i < _probers.Length; i++)
            {
                if (_probers[i].HandleData(buf, start + segStart, len - segStart) == ProbingState.FoundIt)
                {
                    _bestGuess = i;
                    _state = ProbingState.FoundIt;
                    return _state;
                }
            }
        }
        _keepNext = keepNext;
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
            float cf = _probers[i].GetConfidence();
            if (bestConf < cf) { bestConf = cf; _bestGuess = i; }
        }
        return bestConf;
    }

    public override ProbingState GetState() => _state;
}

/// <summary>nsEscCharSetProber: HZ-GB-2312, ISO-2022-CN, ISO-2022-JP, ISO-2022-KR.</summary>
internal sealed class EscCharSetProber : CharSetProber
{
    readonly CodingStateMachine[] _codingSm =
    [
        new(UchardetTables.HzSm),
        new(UchardetTables.Iso2022CnSm),
        new(UchardetTables.Iso2022JpSm),
        new(UchardetTables.Iso2022KrSm),
    ];
    ProbingState _state;
    string? _detectedCharset;

    public EscCharSetProber() => Reset();

    public override void Reset()
    {
        _state = ProbingState.Detecting;
        foreach (var sm in _codingSm) sm.Reset();
        _detectedCharset = null;
    }

    public override ProbingState HandleData(byte[] buf, int start, int len)
    {
        for (int i = 0; i < len && _state == ProbingState.Detecting; i++)
        {
            for (int j = _codingSm.Length - 1; j >= 0; j--)
            {
                if (_codingSm[j].NextState(buf[start + i]) == SmState.ItsMe)
                {
                    _state = ProbingState.FoundIt;
                    _detectedCharset = _codingSm[j].Name;
                    return _state;
                }
            }
        }
        return _state;
    }

    public override string? GetCharSetName() => _detectedCharset;
    public override ProbingState GetState() => _state;
    public override float GetConfidence() => 0.99f;
}
