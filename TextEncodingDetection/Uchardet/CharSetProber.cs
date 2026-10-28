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
    public abstract ProbingState HandleData(ReadOnlySpan<byte> buf);
    public abstract ProbingState GetState();
    public abstract void Reset();
    public abstract float GetConfidence();

    internal static bool IsAsciiLetter(byte b) => (uint)((b | 0x20) - 'a') <= 'z' - 'a';

    /// <summary>nsCharSetProber::FilterWithoutEnglishLetters: keeps only words that contain a
    /// high byte; every run of other bytes collapses to one space.</summary>
    internal static int FilterWithoutEnglishLetters(ReadOnlySpan<byte> buf, Span<byte> output)
    {
        int n = 0, end = buf.Length, prev = 0, cur = 0;
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
                    buf.Slice(prev, cur - prev).CopyTo(output.Slice(n));
                    n += cur - prev;
                    prev = cur + 1;
                    output[n++] = (byte)' ';
                    meetMsb = false;
                }
                else
                    prev = cur + 1;
            }
        }
        if (meetMsb && cur > prev)
        {
            buf.Slice(prev, cur - prev).CopyTo(output.Slice(n));
            n += cur - prev;
        }
        return n;
    }
}

internal enum SmState { Start = 0, Error = 1, ItsMe = 2 }

/// <summary>SMModel with the packed 4-bit class/state tables stored unpacked (one byte per
/// entry); a lookup returns the same value GETFROMPCK returns on the packed table.
/// <para>Derived tables (computed here from the generated ones, so they cannot drift):
/// <see cref="Next"/>[state * 256 + byte] = StateTable[state * ClassFactor + ClassTable[byte]] (one
/// lookup per byte instead of two), <see cref="LenByByte"/>[byte] = CharLenTable[ClassTable[byte]],
/// and <see cref="ErrorIsAbsorbing"/>: every transition out of eError leads back to eError. When it
/// holds, a machine that reached eError never changes state again, never reports eStart or eItsMe,
/// and so its prober's counters and answers are frozen from then on.</para></summary>
internal sealed class StateMachineModel
{
    public readonly byte[] ClassTable;
    public readonly int ClassFactor;
    public readonly byte[] StateTable;
    public readonly byte[] CharLenTable;
    public readonly string Name;
    public readonly byte[] Next;
    public readonly byte[] LenByByte = new byte[256];
    public readonly bool ErrorIsAbsorbing;

    public StateMachineModel(byte[] classTable, int classFactor, byte[] stateTable, byte[] charLenTable, string name)
    {
        ClassTable = classTable; ClassFactor = classFactor; StateTable = stateTable; CharLenTable = charLenTable; Name = name;
        int rows = stateTable.Length / classFactor;
        Next = new byte[rows * 256];
        for (int s = 0; s < rows; s++)
            for (int b = 0; b < 256; b++)
                Next[s * 256 + b] = stateTable[s * classFactor + classTable[b]];
        for (int b = 0; b < 256; b++) LenByByte[b] = charLenTable[classTable[b]];
        bool absorbing = true;
        for (int c = 0; c < classFactor; c++)
            if (stateTable[(int)SmState.Error * classFactor + c] != (byte)SmState.Error) absorbing = false;
        ErrorIsAbsorbing = absorbing;
    }
}

/// <summary>nsCodingStateMachine (the per-byte step is inlined in the probers' loops, on
/// <see cref="StateMachineModel.Next"/>).</summary>
internal sealed class CodingStateMachine(StateMachineModel model)
{
    public readonly StateMachineModel Model = model;
    public int CurrentState; // eStart
    public int CurrentCharLen;

    public SmState NextState(byte c)
    {
        if (CurrentState == (int)SmState.Start)
            CurrentCharLen = Model.LenByByte[c];
        CurrentState = Model.Next[(CurrentState << 8) | c];
        return (SmState)CurrentState;
    }

    /// <summary>In eError and eError is absorbing: no further input can change anything.</summary>
    public bool IsDead => CurrentState == (int)SmState.Error && Model.ErrorIsAbsorbing;
    public void Reset() => CurrentState = (int)SmState.Start;
    public string Name => Model.Name;
}

/// <summary>SequenceModel (nsSBCharSetProber.h). <see cref="FrequentBytes"/> lists the bytes whose
/// order is below SAMPLE_SIZE (the only ones that take part in sequence counting).</summary>
internal sealed class SequenceModel
{
    public readonly byte[] CharToOrderMap;
    public readonly byte[] PrecedenceMatrix;
    public readonly float TypicalPositiveRatio;
    public readonly string CharsetName;
    public readonly byte[] FrequentBytes;

    public SequenceModel(byte[] charToOrderMap, byte[] precedenceMatrix, float typicalPositiveRatio, string charsetName)
    {
        CharToOrderMap = charToOrderMap; PrecedenceMatrix = precedenceMatrix;
        TypicalPositiveRatio = typicalPositiveRatio; CharsetName = charsetName;
        var frequent = new List<byte>();
        for (int b = 0; b < 256; b++) if (charToOrderMap[b] < 64) frequent.Add((byte)b);
        FrequentBytes = frequent.ToArray();
    }
}

/// <summary>Word-at-a-time (SWAR) scans over 8 bytes; exact predicates, any endianness.</summary>
internal static class Swar
{
    public const ulong Ones = 0x0101010101010101UL, HighBits = 0x8080808080808080UL;

    public static ulong Load(ReadOnlySpan<byte> s, int i) => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(s.Slice(i));

    /// <summary>Nonzero iff some byte of v is zero.</summary>
    public static ulong HasZeroByte(ulong v) => (v - Ones) & ~v & HighBits;

    /// <summary>First index at or after <paramref name="i"/> holding a byte &gt;= 0x80, or s.Length.</summary>
    public static int SkipAscii(ReadOnlySpan<byte> s, int i)
    {
        int n = s.Length;
        while (i + 8 <= n && (Load(s, i) & HighBits) == 0) i += 8;
        while (i < n && s[i] < 0x80) i++;
        return i;
    }
}
