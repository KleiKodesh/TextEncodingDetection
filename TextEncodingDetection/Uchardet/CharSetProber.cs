// ***** BEGIN LICENSE BLOCK *****
// Version: MPL 1.1/GPL 2.0/LGPL 2.1
// C# port of nsCharSetProber.h/.cpp, nsCodingStateMachine.h, nsPkgInt.h and the SequenceModel
// struct of nsSBCharSetProber.h, from the uchardet copy bundled in Notepad++
// (PowerEditor/src/uchardet). The Original Code is Mozilla Universal charset detector code.
// The Initial Developer of the Original Code is Netscape Communications Corporation.
// Portions created by the Initial Developer are Copyright (C) 1998 the Initial Developer.
// All Rights Reserved. Used here under the Mozilla Public License 1.1; see NOTICE.
// ***** END LICENSE BLOCK *****

namespace TextEncodingDetection.Uchardet;

internal enum ProbingState { Detecting = 0, FoundIt = 1, NotMe = 2 }

internal abstract class CharSetProber
{
    internal const float ShortcutThreshold = 0.95f; // SHORTCUT_THRESHOLD

    public abstract string? GetCharSetName();
    public abstract ProbingState HandleData(byte[] buf, int start, int len);
    public abstract ProbingState GetState();
    public abstract void Reset();
    public abstract float GetConfidence();

    static bool IsAsciiLetter(byte b) => (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z');

    /// <summary>nsCharSetProber::FilterWithoutEnglishLetters: keeps only words that contain a
    /// high byte; every run of other bytes collapses to one space.</summary>
    internal static int FilterWithoutEnglishLetters(byte[] buf, int start, int len, byte[] output)
    {
        int n = 0, end = start + len, prev = start, cur = start;
        bool meetMsb = false;
        for (; cur < end; cur++)
        {
            byte c = buf[cur];
            if ((c & 0x80) != 0)
                meetMsb = true;
            else if (!IsAsciiLetter(c))
            {
                if (meetMsb && cur > prev)
                {
                    while (prev < cur) output[n++] = buf[prev++];
                    prev++;
                    output[n++] = (byte)' ';
                    meetMsb = false;
                }
                else
                    prev = cur + 1;
            }
        }
        if (meetMsb && cur > prev)
            while (prev < cur) output[n++] = buf[prev++];
        return n;
    }

    /// <summary>nsCharSetProber::FilterWithEnglishLetters: drops markup tags and symbol-only
    /// segments, keeping letter words (ASCII or high byte) separated by single spaces.</summary>
    internal static int FilterWithEnglishLetters(byte[] buf, int start, int len, byte[] output)
    {
        int n = 0, end = start + len, prev = start, cur = start;
        bool isInTag = false;
        for (; cur < end; cur++)
        {
            byte c = buf[cur];
            if (c == (byte)'>') isInTag = false;
            else if (c == (byte)'<') isInTag = true;
            if ((c & 0x80) == 0 && !IsAsciiLetter(c))
            {
                if (cur > prev && !isInTag)
                {
                    while (prev < cur) output[n++] = buf[prev++];
                    prev++;
                    output[n++] = (byte)' ';
                }
                else
                    prev = cur + 1;
            }
        }
        if (!isInTag)
            while (prev < cur) output[n++] = buf[prev++];
        return n;
    }
}

internal enum SmState { Start = 0, Error = 1, ItsMe = 2 }

/// <summary>SMModel with the packed 4-bit class/state tables stored unpacked (one byte per
/// entry); a lookup returns the same value GETFROMPCK returns on the packed table.</summary>
internal sealed class StateMachineModel(byte[] classTable, int classFactor, byte[] stateTable, byte[] charLenTable, string name)
{
    public readonly byte[] ClassTable = classTable;
    public readonly int ClassFactor = classFactor;
    public readonly byte[] StateTable = stateTable;
    public readonly byte[] CharLenTable = charLenTable;
    public readonly string Name = name;
}

/// <summary>nsCodingStateMachine.</summary>
internal sealed class CodingStateMachine(StateMachineModel model)
{
    readonly StateMachineModel _model = model;
    int _currentState; // eStart
    int _currentCharLen;

    public SmState NextState(byte c)
    {
        int byteCls = _model.ClassTable[c];
        if (_currentState == (int)SmState.Start)
            _currentCharLen = _model.CharLenTable[byteCls];
        _currentState = _model.StateTable[_currentState * _model.ClassFactor + byteCls];
        return (SmState)_currentState;
    }

    public int CurrentCharLen => _currentCharLen;
    public void Reset() => _currentState = (int)SmState.Start;
    public string Name => _model.Name;
}

/// <summary>SequenceModel (nsSBCharSetProber.h).</summary>
internal sealed class SequenceModel(byte[] charToOrderMap, byte[] precedenceMatrix, float typicalPositiveRatio, string charsetName)
{
    public readonly byte[] CharToOrderMap = charToOrderMap;
    public readonly byte[] PrecedenceMatrix = precedenceMatrix;
    public readonly float TypicalPositiveRatio = typicalPositiveRatio;
    public readonly string CharsetName = charsetName;
}
