// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;

/// <summary>
/// 8-bit loop filter sample primitives. Implements section 7.14.6 of the AV1 specification:
/// the filter mask (7.14.6.2), narrow filter (7.14.6.3), and wide filter (7.14.6.4) processes.
/// </summary>
internal static class Av1LoopFilterPrimitives
{
    private static sbyte SignedCharClamp(int t) => (sbyte)Math.Clamp(t, -128, 127);

    private static int RoundPowerOfTwo(int value, int n) => (value + (1 << (n - 1))) >> n;

    private static sbyte FilterMask2(byte limit, byte blimit, byte p1, byte p0, byte q0, byte q1)
    {
        int mask = 0;
        mask |= (Math.Abs(p1 - p0) > limit) ? -1 : 0;
        mask |= (Math.Abs(q1 - q0) > limit) ? -1 : 0;
        mask |= ((Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) / 2) > blimit) ? -1 : 0;
        return (sbyte)~mask;
    }

    private static sbyte FilterMask(byte limit, byte blimit, byte p3, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2, byte q3)
    {
        int mask = 0;
        mask |= (Math.Abs(p3 - p2) > limit) ? -1 : 0;
        mask |= (Math.Abs(p2 - p1) > limit) ? -1 : 0;
        mask |= (Math.Abs(p1 - p0) > limit) ? -1 : 0;
        mask |= (Math.Abs(q1 - q0) > limit) ? -1 : 0;
        mask |= (Math.Abs(q2 - q1) > limit) ? -1 : 0;
        mask |= (Math.Abs(q3 - q2) > limit) ? -1 : 0;
        mask |= ((Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) / 2) > blimit) ? -1 : 0;
        return (sbyte)~mask;
    }

    private static sbyte FilterMask3Chroma(byte limit, byte blimit, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2)
    {
        int mask = 0;
        mask |= (Math.Abs(p2 - p1) > limit) ? -1 : 0;
        mask |= (Math.Abs(p1 - p0) > limit) ? -1 : 0;
        mask |= (Math.Abs(q1 - q0) > limit) ? -1 : 0;
        mask |= (Math.Abs(q2 - q1) > limit) ? -1 : 0;
        mask |= ((Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) / 2) > blimit) ? -1 : 0;
        return (sbyte)~mask;
    }

    private static sbyte FlatMask3Chroma(byte thresh, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2)
    {
        int mask = 0;
        mask |= (Math.Abs(p1 - p0) > thresh) ? -1 : 0;
        mask |= (Math.Abs(q1 - q0) > thresh) ? -1 : 0;
        mask |= (Math.Abs(p2 - p0) > thresh) ? -1 : 0;
        mask |= (Math.Abs(q2 - q0) > thresh) ? -1 : 0;
        return (sbyte)~mask;
    }

    private static sbyte FlatMask4(byte thresh, byte p3, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2, byte q3)
    {
        int mask = 0;
        mask |= (Math.Abs(p1 - p0) > thresh) ? -1 : 0;
        mask |= (Math.Abs(q1 - q0) > thresh) ? -1 : 0;
        mask |= (Math.Abs(p2 - p0) > thresh) ? -1 : 0;
        mask |= (Math.Abs(q2 - q0) > thresh) ? -1 : 0;
        mask |= (Math.Abs(p3 - p0) > thresh) ? -1 : 0;
        mask |= (Math.Abs(q3 - q0) > thresh) ? -1 : 0;
        return (sbyte)~mask;
    }

    private static sbyte HevMask(byte thresh, byte p1, byte p0, byte q0, byte q1)
    {
        int hev = 0;
        hev |= (Math.Abs(p1 - p0) > thresh) ? -1 : 0;
        hev |= (Math.Abs(q1 - q0) > thresh) ? -1 : 0;
        return (sbyte)hev;
    }

    private static void Filter4(sbyte mask, byte thresh, ref byte op1, ref byte op0, ref byte oq0, ref byte oq1)
    {
        sbyte ps1 = (sbyte)(op1 ^ 0x80);
        sbyte ps0 = (sbyte)(op0 ^ 0x80);
        sbyte qs0 = (sbyte)(oq0 ^ 0x80);
        sbyte qs1 = (sbyte)(oq1 ^ 0x80);
        sbyte hev = HevMask(thresh, op1, op0, oq0, oq1);

        sbyte filter = (sbyte)(SignedCharClamp(ps1 - qs1) & hev);
        filter = (sbyte)(SignedCharClamp(filter + (3 * (qs0 - ps0))) & mask);

        sbyte filter1 = (sbyte)(SignedCharClamp(filter + 4) >> 3);
        sbyte filter2 = (sbyte)(SignedCharClamp(filter + 3) >> 3);

        oq0 = (byte)(SignedCharClamp(qs0 - filter1) ^ 0x80);
        op0 = (byte)(SignedCharClamp(ps0 + filter2) ^ 0x80);

        // The C source rounds filter1 (a signed value) and masks with ~hev.
        sbyte rounded = (sbyte)RoundPowerOfTwo(filter1, 1);
        filter = (sbyte)(rounded & ~hev);

        oq1 = (byte)(SignedCharClamp(qs1 - filter) ^ 0x80);
        op1 = (byte)(SignedCharClamp(ps1 + filter) ^ 0x80);
    }

    private static void Filter6(sbyte mask, byte thresh, sbyte flat, ref byte op2, ref byte op1, ref byte op0, ref byte oq0, ref byte oq1, ref byte oq2)
    {
        if ((flat & mask) != 0)
        {
            byte p2 = op2, p1 = op1, p0 = op0;
            byte q0 = oq0, q1 = oq1, q2 = oq2;

            op1 = (byte)RoundPowerOfTwo((p2 * 3) + (p1 * 2) + (p0 * 2) + q0, 3);
            op0 = (byte)RoundPowerOfTwo(p2 + (p1 * 2) + (p0 * 2) + (q0 * 2) + q1, 3);
            oq0 = (byte)RoundPowerOfTwo(p1 + (p0 * 2) + (q0 * 2) + (q1 * 2) + q2, 3);
            oq1 = (byte)RoundPowerOfTwo(p0 + (q0 * 2) + (q1 * 2) + (q2 * 3), 3);
        }
        else
        {
            Filter4(mask, thresh, ref op1, ref op0, ref oq0, ref oq1);
        }
    }

    private static void Filter8(sbyte mask, byte thresh, sbyte flat, ref byte op3, ref byte op2, ref byte op1, ref byte op0, ref byte oq0, ref byte oq1, ref byte oq2, ref byte oq3)
    {
        if ((flat & mask) != 0)
        {
            byte p3 = op3, p2 = op2, p1 = op1, p0 = op0;
            byte q0 = oq0, q1 = oq1, q2 = oq2, q3 = oq3;

            op2 = (byte)RoundPowerOfTwo(p3 + p3 + p3 + (2 * p2) + p1 + p0 + q0, 3);
            op1 = (byte)RoundPowerOfTwo(p3 + p3 + p2 + (2 * p1) + p0 + q0 + q1, 3);
            op0 = (byte)RoundPowerOfTwo(p3 + p2 + p1 + (2 * p0) + q0 + q1 + q2, 3);
            oq0 = (byte)RoundPowerOfTwo(p2 + p1 + p0 + (2 * q0) + q1 + q2 + q3, 3);
            oq1 = (byte)RoundPowerOfTwo(p1 + p0 + q0 + (2 * q1) + q2 + q3 + q3, 3);
            oq2 = (byte)RoundPowerOfTwo(p0 + q0 + q1 + (2 * q2) + q3 + q3 + q3, 3);
        }
        else
        {
            Filter4(mask, thresh, ref op1, ref op0, ref oq0, ref oq1);
        }
    }

    private static void Filter14(sbyte mask, byte thresh, sbyte flat, sbyte flat2, ref byte op6, ref byte op5, ref byte op4, ref byte op3, ref byte op2, ref byte op1, ref byte op0, ref byte oq0, ref byte oq1, ref byte oq2, ref byte oq3, ref byte oq4, ref byte oq5, ref byte oq6)
    {
        if ((flat2 & flat & mask) != 0)
        {
            byte p6 = op6, p5 = op5, p4 = op4, p3 = op3, p2 = op2, p1 = op1, p0 = op0;
            byte q0 = oq0, q1 = oq1, q2 = oq2, q3 = oq3, q4 = oq4, q5 = oq5, q6 = oq6;

            op5 = (byte)RoundPowerOfTwo((p6 * 7) + (p5 * 2) + (p4 * 2) + p3 + p2 + p1 + p0 + q0, 4);
            op4 = (byte)RoundPowerOfTwo((p6 * 5) + (p5 * 2) + (p4 * 2) + (p3 * 2) + p2 + p1 + p0 + q0 + q1, 4);
            op3 = (byte)RoundPowerOfTwo((p6 * 4) + p5 + (p4 * 2) + (p3 * 2) + (p2 * 2) + p1 + p0 + q0 + q1 + q2, 4);
            op2 = (byte)RoundPowerOfTwo((p6 * 3) + p5 + p4 + (p3 * 2) + (p2 * 2) + (p1 * 2) + p0 + q0 + q1 + q2 + q3, 4);
            op1 = (byte)RoundPowerOfTwo((p6 * 2) + p5 + p4 + p3 + (p2 * 2) + (p1 * 2) + (p0 * 2) + q0 + q1 + q2 + q3 + q4, 4);
            op0 = (byte)RoundPowerOfTwo(p6 + p5 + p4 + p3 + p2 + (p1 * 2) + (p0 * 2) + (q0 * 2) + q1 + q2 + q3 + q4 + q5, 4);
            oq0 = (byte)RoundPowerOfTwo(p5 + p4 + p3 + p2 + p1 + (p0 * 2) + (q0 * 2) + (q1 * 2) + q2 + q3 + q4 + q5 + q6, 4);
            oq1 = (byte)RoundPowerOfTwo(p4 + p3 + p2 + p1 + p0 + (q0 * 2) + (q1 * 2) + (q2 * 2) + q3 + q4 + q5 + (q6 * 2), 4);
            oq2 = (byte)RoundPowerOfTwo(p3 + p2 + p1 + p0 + q0 + (q1 * 2) + (q2 * 2) + (q3 * 2) + q4 + q5 + (q6 * 3), 4);
            oq3 = (byte)RoundPowerOfTwo(p2 + p1 + p0 + q0 + q1 + (q2 * 2) + (q3 * 2) + (q4 * 2) + q5 + (q6 * 4), 4);
            oq4 = (byte)RoundPowerOfTwo(p1 + p0 + q0 + q1 + q2 + (q3 * 2) + (q4 * 2) + (q5 * 2) + (q6 * 5), 4);
            oq5 = (byte)RoundPowerOfTwo(p0 + q0 + q1 + q2 + q3 + (q4 * 2) + (q5 * 2) + (q6 * 7), 4);
        }
        else
        {
            Filter8(mask, thresh, flat, ref op3, ref op2, ref op1, ref op0, ref oq0, ref oq1, ref oq2, ref oq3);
        }
    }

    /// <summary>
    /// Applies a horizontal 4-tap filter at offset zero across 4 columns of pixels.
    /// </summary>
    public static void LpfHorizontal4(Span<byte> s, int offset, int p, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < 4; i++)
        {
            int idx = offset + i;
            byte p1 = s[idx - (2 * p)], p0 = s[idx - p];
            byte q0 = s[idx], q1 = s[idx + p];
            sbyte mask = FilterMask2(limit, blimit, p1, p0, q0, q1);
            ApplyFilter4(s, idx, p, mask, thresh);
        }
    }

    public static void LpfVertical4(Span<byte> s, int offset, int p, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < 4; i++)
        {
            int idx = offset + (i * p);
            byte p1 = s[idx - 2], p0 = s[idx - 1];
            byte q0 = s[idx], q1 = s[idx + 1];
            sbyte mask = FilterMask2(limit, blimit, p1, p0, q0, q1);
            ApplyFilter4(s, idx, 1, mask, thresh);
        }
    }

    public static void LpfHorizontal6(Span<byte> s, int offset, int p, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < 4; i++)
        {
            int idx = offset + i;
            byte p2 = s[idx - (3 * p)], p1 = s[idx - (2 * p)], p0 = s[idx - p];
            byte q0 = s[idx], q1 = s[idx + p], q2 = s[idx + (2 * p)];
            sbyte mask = FilterMask3Chroma(limit, blimit, p2, p1, p0, q0, q1, q2);
            sbyte flat = FlatMask3Chroma(1, p2, p1, p0, q0, q1, q2);
            ApplyFilter6(s, idx, p, mask, thresh, flat);
        }
    }

    public static void LpfVertical6(Span<byte> s, int offset, int p, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < 4; i++)
        {
            int idx = offset + (i * p);
            byte p2 = s[idx - 3], p1 = s[idx - 2], p0 = s[idx - 1];
            byte q0 = s[idx], q1 = s[idx + 1], q2 = s[idx + 2];
            sbyte mask = FilterMask3Chroma(limit, blimit, p2, p1, p0, q0, q1, q2);
            sbyte flat = FlatMask3Chroma(1, p2, p1, p0, q0, q1, q2);
            ApplyFilter6(s, idx, 1, mask, thresh, flat);
        }
    }

    public static void LpfHorizontal8(Span<byte> s, int offset, int p, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < 4; i++)
        {
            int idx = offset + i;
            byte p3 = s[idx - (4 * p)], p2 = s[idx - (3 * p)], p1 = s[idx - (2 * p)], p0 = s[idx - p];
            byte q0 = s[idx], q1 = s[idx + p], q2 = s[idx + (2 * p)], q3 = s[idx + (3 * p)];
            sbyte mask = FilterMask(limit, blimit, p3, p2, p1, p0, q0, q1, q2, q3);
            sbyte flat = FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3);
            ApplyFilter8(s, idx, p, mask, thresh, flat);
        }
    }

    public static void LpfVertical8(Span<byte> s, int offset, int p, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < 4; i++)
        {
            int idx = offset + (i * p);
            byte p3 = s[idx - 4], p2 = s[idx - 3], p1 = s[idx - 2], p0 = s[idx - 1];
            byte q0 = s[idx], q1 = s[idx + 1], q2 = s[idx + 2], q3 = s[idx + 3];
            sbyte mask = FilterMask(limit, blimit, p3, p2, p1, p0, q0, q1, q2, q3);
            sbyte flat = FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3);
            ApplyFilter8(s, idx, 1, mask, thresh, flat);
        }
    }

    public static void LpfHorizontal14(Span<byte> s, int offset, int p, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < 4; i++)
        {
            int idx = offset + i;
            byte p6 = s[idx - (7 * p)], p5 = s[idx - (6 * p)], p4 = s[idx - (5 * p)],
                 p3 = s[idx - (4 * p)], p2 = s[idx - (3 * p)], p1 = s[idx - (2 * p)], p0 = s[idx - p];
            byte q0 = s[idx], q1 = s[idx + p], q2 = s[idx + (2 * p)], q3 = s[idx + (3 * p)],
                 q4 = s[idx + (4 * p)], q5 = s[idx + (5 * p)], q6 = s[idx + (6 * p)];
            sbyte mask = FilterMask(limit, blimit, p3, p2, p1, p0, q0, q1, q2, q3);
            sbyte flat = FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3);
            sbyte flat2 = FlatMask4(1, p6, p5, p4, p0, q0, q4, q5, q6);
            ApplyFilter14(s, idx, p, mask, thresh, flat, flat2);
        }
    }

    public static void LpfVertical14(Span<byte> s, int offset, int p, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < 4; i++)
        {
            int idx = offset + (i * p);
            byte p6 = s[idx - 7], p5 = s[idx - 6], p4 = s[idx - 5], p3 = s[idx - 4], p2 = s[idx - 3], p1 = s[idx - 2], p0 = s[idx - 1];
            byte q0 = s[idx], q1 = s[idx + 1], q2 = s[idx + 2], q3 = s[idx + 3], q4 = s[idx + 4], q5 = s[idx + 5], q6 = s[idx + 6];
            sbyte mask = FilterMask(limit, blimit, p3, p2, p1, p0, q0, q1, q2, q3);
            sbyte flat = FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3);
            sbyte flat2 = FlatMask4(1, p6, p5, p4, p0, q0, q4, q5, q6);
            ApplyFilter14(s, idx, 1, mask, thresh, flat, flat2);
        }
    }

    private static void ApplyFilter4(Span<byte> s, int idx, int step, sbyte mask, byte thresh)
    {
        byte op1 = s[idx - (2 * step)], op0 = s[idx - step], oq0 = s[idx], oq1 = s[idx + step];
        Filter4(mask, thresh, ref op1, ref op0, ref oq0, ref oq1);
        s[idx - (2 * step)] = op1;
        s[idx - step] = op0;
        s[idx] = oq0;
        s[idx + step] = oq1;
    }

    private static void ApplyFilter6(Span<byte> s, int idx, int step, sbyte mask, byte thresh, sbyte flat)
    {
        byte op2 = s[idx - (3 * step)], op1 = s[idx - (2 * step)], op0 = s[idx - step];
        byte oq0 = s[idx], oq1 = s[idx + step], oq2 = s[idx + (2 * step)];
        Filter6(mask, thresh, flat, ref op2, ref op1, ref op0, ref oq0, ref oq1, ref oq2);
        s[idx - (3 * step)] = op2;
        s[idx - (2 * step)] = op1;
        s[idx - step] = op0;
        s[idx] = oq0;
        s[idx + step] = oq1;
        s[idx + (2 * step)] = oq2;
    }

    private static void ApplyFilter8(Span<byte> s, int idx, int step, sbyte mask, byte thresh, sbyte flat)
    {
        byte op3 = s[idx - (4 * step)], op2 = s[idx - (3 * step)], op1 = s[idx - (2 * step)], op0 = s[idx - step];
        byte oq0 = s[idx], oq1 = s[idx + step], oq2 = s[idx + (2 * step)], oq3 = s[idx + (3 * step)];
        Filter8(mask, thresh, flat, ref op3, ref op2, ref op1, ref op0, ref oq0, ref oq1, ref oq2, ref oq3);
        s[idx - (4 * step)] = op3;
        s[idx - (3 * step)] = op2;
        s[idx - (2 * step)] = op1;
        s[idx - step] = op0;
        s[idx] = oq0;
        s[idx + step] = oq1;
        s[idx + (2 * step)] = oq2;
        s[idx + (3 * step)] = oq3;
    }

    private static void ApplyFilter14(Span<byte> s, int idx, int step, sbyte mask, byte thresh, sbyte flat, sbyte flat2)
    {
        byte op6 = s[idx - (7 * step)], op5 = s[idx - (6 * step)], op4 = s[idx - (5 * step)],
             op3 = s[idx - (4 * step)], op2 = s[idx - (3 * step)], op1 = s[idx - (2 * step)], op0 = s[idx - step];
        byte oq0 = s[idx], oq1 = s[idx + step], oq2 = s[idx + (2 * step)], oq3 = s[idx + (3 * step)],
             oq4 = s[idx + (4 * step)], oq5 = s[idx + (5 * step)], oq6 = s[idx + (6 * step)];
        Filter14(mask, thresh, flat, flat2, ref op6, ref op5, ref op4, ref op3, ref op2, ref op1, ref op0, ref oq0, ref oq1, ref oq2, ref oq3, ref oq4, ref oq5, ref oq6);
        s[idx - (7 * step)] = op6;
        s[idx - (6 * step)] = op5;
        s[idx - (5 * step)] = op4;
        s[idx - (4 * step)] = op3;
        s[idx - (3 * step)] = op2;
        s[idx - (2 * step)] = op1;
        s[idx - step] = op0;
        s[idx] = oq0;
        s[idx + step] = oq1;
        s[idx + (2 * step)] = oq2;
        s[idx + (3 * step)] = oq3;
        s[idx + (4 * step)] = oq4;
        s[idx + (5 * step)] = oq5;
        s[idx + (6 * step)] = oq6;
    }
}
