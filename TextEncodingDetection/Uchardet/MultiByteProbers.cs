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

/// <summary>CharDistributionAnalysis and its subclasses (GetOrder per encoding, as a switch).</summary>
internal sealed class CharDistributionAnalysis
{
    internal enum Kind { EucTw, EucKr, Gb2312, Big5, Sjis, EucJp }

    const uint EnoughDataThreshold = 1024;
    const uint MinimumDataThreshold = 4;
    const float SureYes = 0.99f, SureNo = 0.01f;

    uint _freqChars, _totalChars, _dataThreshold;
    readonly Kind _kind;
    readonly short[] _charToFreqOrder;
    readonly float _typicalDistributionRatio;

    public CharDistributionAnalysis(Kind kind)
    {
        _kind = kind;
        (_charToFreqOrder, _typicalDistributionRatio) = kind switch
        {
            Kind.EucTw => (UchardetTables.EucTw_CharToFreqOrder, UchardetTables.EucTw_TypicalDistributionRatio),
            Kind.EucKr => (UchardetTables.EucKr_CharToFreqOrder, UchardetTables.EucKr_TypicalDistributionRatio),
            Kind.Gb2312 => (UchardetTables.Gb2312_CharToFreqOrder, UchardetTables.Gb2312_TypicalDistributionRatio),
            Kind.Big5 => (UchardetTables.Big5_CharToFreqOrder, UchardetTables.Big5_TypicalDistributionRatio),
            _ => (UchardetTables.Jis_CharToFreqOrder, UchardetTables.Jis_TypicalDistributionRatio),
        };
        _pairClass = Volatile.Read(ref s_pairClass[(int)kind]) ?? (s_pairClass[(int)kind] = BuildPairClass());
        Reset(false);
    }

    int GetOrder(byte b0, byte b1)
    {
        switch (_kind)
        {
            case Kind.EucTw: return b0 >= 0xc4 ? 94 * (b0 - 0xc4) + b1 - 0xa1 : -1;
            case Kind.EucKr: return b0 >= 0xb0 ? 94 * (b0 - 0xb0) + b1 - 0xa1 : -1;
            case Kind.Gb2312: return b0 >= 0xb0 && b1 >= 0xa1 ? 94 * (b0 - 0xb0) + b1 - 0xa1 : -1;
            case Kind.Big5:
                if (b0 < 0xa4) return -1;
                return b1 >= 0xa1 ? 157 * (b0 - 0xa4) + b1 - 0xa1 + 63 : 157 * (b0 - 0xa4) + b1 - 0x40;
            case Kind.Sjis:
            {
                int order;
                if (b0 >= 0x81 && b0 <= 0x9f) order = 188 * (b0 - 0x81);
                else if (b0 >= 0xe0 && b0 <= 0xef) order = 188 * (b0 - 0xe0 + 31);
                else return -1;
                order += b1 - 0x40;
                if (b1 > 0x7f) order--;
                return order;
            }
            default: return b0 >= 0xa0 ? 94 * (b0 - 0xa1) + b1 - 0xa1 : -1; // EUC-JP
        }
    }

    /// <summary>Per kind, for every byte pair: 0 = GetOrder gives no order, 1 = an order that is
    /// not frequent, 2 = a frequent order (order inside the table and its rank below 512). Built
    /// from <see cref="GetOrder"/> itself, so HandleOneChar counts exactly as before.</summary>
    static readonly byte[]?[] s_pairClass = new byte[]?[6];

    byte[] BuildPairClass()
    {
        var t = new byte[65536];
        for (int b0 = 0; b0 < 256; b0++)
            for (int b1 = 0; b1 < 256; b1++)
            {
                int order = GetOrder((byte)b0, (byte)b1);
                if (order < 0) continue;
                t[(b0 << 8) | b1] = (byte)((uint)order < (uint)_charToFreqOrder.Length && 512 > _charToFreqOrder[order] ? 2 : 1);
            }
        return t;
    }

    readonly byte[] _pairClass;

    public void HandleOneChar(byte b0, byte b1, int charLen)
    {
        if (charLen != 2) return;
        int v = _pairClass[(b0 << 8) | b1];
        if (v != 0)
        {
            _totalChars++;
            _freqChars += (uint)(v >> 1);
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

/// <summary>JapaneseContextAnalysis (hiragana pair frequencies), SJIS or EUC-JP flavour. Only
/// HandleOneChar is reached from the probers; the byte-buffer HandleData of JpCntx.cpp has no
/// caller and is not ported.</summary>
internal sealed class JapaneseContextAnalysis(bool sjis)
{
    const int NumOfCategory = 6;
    const uint EnoughRelThreshold = 100, MaxRelThreshold = 1000, MinimumDataThreshold = 4;
    const float DontKnow = -1f;

    readonly bool _sjis = sjis;
    readonly uint[] _relSample = new uint[NumOfCategory];
    uint _totalRel, _dataThreshold = MinimumDataThreshold;
    int _lastCharOrder = -1;
    bool _done;

    int GetOrder(byte b0, byte b1) => _sjis
        ? (b0 == 0x82 && b1 >= 0x9f && b1 <= 0xf1 ? b1 - 0x9f : -1)
        : (b0 == 0xa4 && b1 >= 0xa1 && b1 <= 0xf3 ? b1 - 0xa1 : -1);

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

/// <summary>A prober of the MBCS group. <see cref="IsDead"/>: its coding state machine sits in an
/// absorbing eError, so every later HandleData returns eDetecting and changes nothing that
/// GetConfidence reads (see <see cref="StateMachineModel.ErrorIsAbsorbing"/>).</summary>
internal abstract class MultiByteProber : CharSetProber
{
    public abstract bool IsDead { get; }
}

/// <summary>nsUTF8Prober.</summary>
internal sealed class Utf8Prober : MultiByteProber
{
    const float OneCharProb = 0.50f;
    readonly CodingStateMachine _codingSm = new(UchardetTables.Utf8Sm);
    ProbingState _state;
    uint _numOfMbChar;

    public Utf8Prober() => Reset();

    public override void Reset() { _codingSm.Reset(); _numOfMbChar = 0; _state = ProbingState.Detecting; }

    public override bool IsDead => _codingSm.IsDead;

    public override ProbingState HandleData(ReadOnlySpan<byte> buf)
    {
        StateMachineModel m = _codingSm.Model;
        byte[] next = m.Next, lenByByte = m.LenByByte;
        bool absorbing = m.ErrorIsAbsorbing;
        int state = _codingSm.CurrentState, charLen = _codingSm.CurrentCharLen;
        uint numOfMbChar = _numOfMbChar;
        for (int i = 0; i < buf.Length; i++)
        {
            byte b = buf[i];
            if (state == (int)SmState.Start) charLen = lenByByte[b];
            state = next[(state << 8) | b];
            if (state > (int)SmState.ItsMe) continue;
            if (state == (int)SmState.ItsMe) { _state = ProbingState.FoundIt; break; }
            if (state == (int)SmState.Error) { if (absorbing) break; continue; }
            if (charLen >= 2) numOfMbChar++;
        }
        _codingSm.CurrentState = state; _codingSm.CurrentCharLen = charLen;
        _numOfMbChar = numOfMbChar;
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
internal sealed class MultiByteDistributionProber : MultiByteProber
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

    public static MultiByteDistributionProber Sjis() => new(UchardetTables.SjisSm, new(CharDistributionAnalysis.Kind.Sjis), new(sjis: true), "Shift_JIS");
    public static MultiByteDistributionProber EucJp() => new(UchardetTables.EucJpSm, new(CharDistributionAnalysis.Kind.EucJp), new(sjis: false), "EUC-JP");
    public static MultiByteDistributionProber Gb18030() => new(UchardetTables.Gb18030Sm, new(CharDistributionAnalysis.Kind.Gb2312), null, "gb18030");
    public static MultiByteDistributionProber EucKr() => new(UchardetTables.EucKrSm, new(CharDistributionAnalysis.Kind.EucKr), null, "EUC-KR");
    public static MultiByteDistributionProber Big5() => new(UchardetTables.Big5Sm, new(CharDistributionAnalysis.Kind.Big5), null, "Big5");
    public static MultiByteDistributionProber EucTw() => new(UchardetTables.EucTwSm, new(CharDistributionAnalysis.Kind.EucTw), null, "x-euc-tw");

    public override void Reset()
    {
        _codingSm.Reset();
        _state = ProbingState.Detecting;
        _context?.Reset(false);
        _distribution.Reset(false);
    }

    public override bool IsDead => _codingSm.IsDead;

    public override ProbingState HandleData(ReadOnlySpan<byte> buf)
    {
        StateMachineModel m = _codingSm.Model;
        byte[] next = m.Next, lenByByte = m.LenByByte;
        bool absorbing = m.ErrorIsAbsorbing;
        int state = _codingSm.CurrentState, charLen = _codingSm.CurrentCharLen;
        JapaneseContextAnalysis? context = _context;
        CharDistributionAnalysis distribution = _distribution;
        for (int i = 0; i < buf.Length; i++)
        {
            byte b = buf[i];
            if (state == (int)SmState.Start) charLen = lenByByte[b];
            state = next[(state << 8) | b];
            if (state > (int)SmState.ItsMe) continue;
            if (state == (int)SmState.ItsMe) { _state = ProbingState.FoundIt; break; }
            if (state == (int)SmState.Error) { if (absorbing) break; continue; }
            // eStart: a character ended at i. Both analysers only read the two bytes when
            // charLen == 2, i.e. the pair ending at i.
            byte b0, b1;
            if (i == 0)
            {
                _lastChar1 = buf[0];
                b0 = _lastChar0; b1 = _lastChar1;
            }
            else
            {
                b0 = buf[i - 1]; b1 = b;
            }
            // nsSJISProber: context gets (i==0 ? mLastChar+2-charLen : aBuf+i+1-charLen); for
            // charLen 2 that is the same pair. nsEUCJPProber passes the same pointer as the
            // distribution analyser. Both only dereference when charLen == 2.
            context?.HandleOneChar(b0, b1, charLen);
            distribution.HandleOneChar(b0, b1, charLen);
        }
        _codingSm.CurrentState = state; _codingSm.CurrentCharLen = charLen;
        _lastChar0 = buf[buf.Length - 1];
        if (_state == ProbingState.Detecting)
        {
            bool enough = context != null ? context.GotEnoughData : distribution.GotEnoughData;
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
/// EUC-TW. Feeds each prober only the high-byte runs plus the byte after each run.
/// <para>Skips (both exact): ASCII between segments is jumped over word-at-a-time (bytes below
/// 0x80 with mKeepNext == 0 do nothing); a dead prober (<see cref="MultiByteProber.IsDead"/>) is no
/// longer called, and once all seven are dead the rest of the buffer cannot change any result,
/// so scanning stops.</para></summary>
internal sealed class MbcsGroupProber : CharSetProber
{
    readonly MultiByteProber[] _probers =
    [
        new Utf8Prober(),
        MultiByteDistributionProber.Sjis(),
        MultiByteDistributionProber.EucJp(),
        MultiByteDistributionProber.Gb18030(),
        MultiByteDistributionProber.EucKr(),
        MultiByteDistributionProber.Big5(),
        MultiByteDistributionProber.EucTw(),
    ];
    readonly bool[] _dead = new bool[7];
    int _aliveNum;
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
        Array.Clear(_dead, 0, _dead.Length);
        _aliveNum = _probers.Length;
        _bestGuess = -1;
        _state = ProbingState.Detecting;
        _keepNext = 0;
    }

    /// <summary>One segment to every live prober in order; true when one of them found it.</summary>
    bool Feed(ReadOnlySpan<byte> segment)
    {
        for (int i = 0; i < _probers.Length; i++)
        {
            if (_dead[i]) continue;
            var p = _probers[i];
            if (p.HandleData(segment) == ProbingState.FoundIt)
            {
                _bestGuess = i;
                _state = ProbingState.FoundIt;
                return true;
            }
            if (p.IsDead) { _dead[i] = true; _aliveNum--; }
        }
        return false;
    }

    public override ProbingState HandleData(ReadOnlySpan<byte> buf)
    {
        if (_aliveNum == 0) return _state;
        int len = buf.Length, segStart = 0;
        uint keepNext = _keepNext;
        for (int pos = 0; pos < len; ++pos)
        {
            if (keepNext == 0)
            {
                pos = Swar.SkipAscii(buf, pos);
                if (pos == len) break;
            }
            if ((buf[pos] & 0x80) != 0)
            {
                if (keepNext == 0) segStart = pos;
                keepNext = 2;
            }
            else if (keepNext != 0 && --keepNext == 0)
            {
                if (Feed(buf.Slice(segStart, pos + 1 - segStart))) return _state;
                if (_aliveNum == 0) return _state;
            }
        }
        if (keepNext != 0 && Feed(buf.Slice(segStart, len - segStart))) return _state;
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

/// <summary>nsEscCharSetProber: HZ-GB-2312, ISO-2022-CN, ISO-2022-JP, ISO-2022-KR. A machine in
/// an absorbing eError is skipped; with all four there, nothing can be found any more.</summary>
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

    public override ProbingState HandleData(ReadOnlySpan<byte> buf)
    {
        for (int i = 0; i < buf.Length && _state == ProbingState.Detecting; i++)
        {
            bool anyAlive = false;
            for (int j = _codingSm.Length - 1; j >= 0; j--)
            {
                var sm = _codingSm[j];
                if (sm.IsDead) continue;
                anyAlive = true;
                if (sm.NextState(buf[i]) == SmState.ItsMe)
                {
                    _state = ProbingState.FoundIt;
                    _detectedCharset = sm.Name;
                    return _state;
                }
            }
            if (!anyAlive) break;
        }
        return _state;
    }

    public override string? GetCharSetName() => _detectedCharset;
    public override ProbingState GetState() => _state;
    public override float GetConfidence() => 0.99f;
}
