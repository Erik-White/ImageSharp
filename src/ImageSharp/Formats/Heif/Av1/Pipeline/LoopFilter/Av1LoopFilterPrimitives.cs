// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;

/// <summary>
/// 8-bit loop filter sample primitives. Implements section 7.14.6 of the AV1 specification:
/// the filter mask (7.14.6.2), narrow filter (7.14.6.3), and wide filter (7.14.6.4) processes.
/// </summary>
internal static class Av1LoopFilterPrimitives
{
    private const int EdgeSamples = 4;

    public static void LpfHorizontal4(Span<byte> plane, int offset, int stride, byte blimit, byte limit, byte thresh)
        => FilterEdge4(plane, offset, sampleStride: 1, edgeStride: stride, blimit, limit, thresh);

    public static void LpfVertical4(Span<byte> plane, int offset, int stride, byte blimit, byte limit, byte thresh)
        => FilterEdge4(plane, offset, sampleStride: stride, edgeStride: 1, blimit, limit, thresh);

    public static void LpfHorizontal6(Span<byte> plane, int offset, int stride, byte blimit, byte limit, byte thresh)
        => FilterEdge6(plane, offset, sampleStride: 1, edgeStride: stride, blimit, limit, thresh);

    public static void LpfVertical6(Span<byte> plane, int offset, int stride, byte blimit, byte limit, byte thresh)
        => FilterEdge6(plane, offset, sampleStride: stride, edgeStride: 1, blimit, limit, thresh);

    public static void LpfHorizontal8(Span<byte> plane, int offset, int stride, byte blimit, byte limit, byte thresh)
        => FilterEdge8(plane, offset, sampleStride: 1, edgeStride: stride, blimit, limit, thresh);

    public static void LpfVertical8(Span<byte> plane, int offset, int stride, byte blimit, byte limit, byte thresh)
        => FilterEdge8(plane, offset, sampleStride: stride, edgeStride: 1, blimit, limit, thresh);

    public static void LpfHorizontal14(Span<byte> plane, int offset, int stride, byte blimit, byte limit, byte thresh)
        => FilterEdge14(plane, offset, sampleStride: 1, edgeStride: stride, blimit, limit, thresh);

    public static void LpfVertical14(Span<byte> plane, int offset, int stride, byte blimit, byte limit, byte thresh)
        => FilterEdge14(plane, offset, sampleStride: stride, edgeStride: 1, blimit, limit, thresh);

    private static void FilterEdge4(Span<byte> plane, int offset, int sampleStride, int edgeStride, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < EdgeSamples; i++)
        {
            int q0Index = offset + (i * sampleStride);
            byte p1 = plane[q0Index - (2 * edgeStride)];
            byte p0 = plane[q0Index - edgeStride];
            byte q0 = plane[q0Index];
            byte q1 = plane[q0Index + edgeStride];

            if (!FilterMask4(limit, blimit, p1, p0, q0, q1))
            {
                continue;
            }

            (p1, p0, q0, q1) = ApplyNarrowFilter(thresh, p1, p0, q0, q1);
            plane[q0Index - (2 * edgeStride)] = p1;
            plane[q0Index - edgeStride] = p0;
            plane[q0Index] = q0;
            plane[q0Index + edgeStride] = q1;
        }
    }

    private static void FilterEdge6(Span<byte> plane, int offset, int sampleStride, int edgeStride, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < EdgeSamples; i++)
        {
            int q0Index = offset + (i * sampleStride);
            byte p2 = plane[q0Index - (3 * edgeStride)];
            byte p1 = plane[q0Index - (2 * edgeStride)];
            byte p0 = plane[q0Index - edgeStride];
            byte q0 = plane[q0Index];
            byte q1 = plane[q0Index + edgeStride];
            byte q2 = plane[q0Index + (2 * edgeStride)];

            if (!FilterMask6Chroma(limit, blimit, p2, p1, p0, q0, q1, q2))
            {
                continue;
            }

            if (FlatMask3Chroma(1, p2, p1, p0, q0, q1, q2))
            {
                (p1, p0, q0, q1) = ApplyChromaWideFilter(p2, p1, p0, q0, q1, q2);
            }
            else
            {
                (p1, p0, q0, q1) = ApplyNarrowFilter(thresh, p1, p0, q0, q1);
            }

            plane[q0Index - (2 * edgeStride)] = p1;
            plane[q0Index - edgeStride] = p0;
            plane[q0Index] = q0;
            plane[q0Index + edgeStride] = q1;
        }
    }

    private static void FilterEdge8(Span<byte> plane, int offset, int sampleStride, int edgeStride, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < EdgeSamples; i++)
        {
            int q0Index = offset + (i * sampleStride);
            byte p3 = plane[q0Index - (4 * edgeStride)];
            byte p2 = plane[q0Index - (3 * edgeStride)];
            byte p1 = plane[q0Index - (2 * edgeStride)];
            byte p0 = plane[q0Index - edgeStride];
            byte q0 = plane[q0Index];
            byte q1 = plane[q0Index + edgeStride];
            byte q2 = plane[q0Index + (2 * edgeStride)];
            byte q3 = plane[q0Index + (3 * edgeStride)];

            if (!FilterMask8(limit, blimit, p3, p2, p1, p0, q0, q1, q2, q3))
            {
                continue;
            }

            if (FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3))
            {
                (p2, p1, p0, q0, q1, q2) = ApplyWideFilter8(p3, p2, p1, p0, q0, q1, q2, q3);
                plane[q0Index - (3 * edgeStride)] = p2;
                plane[q0Index + (2 * edgeStride)] = q2;
            }
            else
            {
                (p1, p0, q0, q1) = ApplyNarrowFilter(thresh, p1, p0, q0, q1);
            }

            plane[q0Index - (2 * edgeStride)] = p1;
            plane[q0Index - edgeStride] = p0;
            plane[q0Index] = q0;
            plane[q0Index + edgeStride] = q1;
        }
    }

    private static void FilterEdge14(Span<byte> plane, int offset, int sampleStride, int edgeStride, byte blimit, byte limit, byte thresh)
    {
        for (int i = 0; i < EdgeSamples; i++)
        {
            int q0Index = offset + (i * sampleStride);
            byte p6 = plane[q0Index - (7 * edgeStride)];
            byte p5 = plane[q0Index - (6 * edgeStride)];
            byte p4 = plane[q0Index - (5 * edgeStride)];
            byte p3 = plane[q0Index - (4 * edgeStride)];
            byte p2 = plane[q0Index - (3 * edgeStride)];
            byte p1 = plane[q0Index - (2 * edgeStride)];
            byte p0 = plane[q0Index - edgeStride];
            byte q0 = plane[q0Index];
            byte q1 = plane[q0Index + edgeStride];
            byte q2 = plane[q0Index + (2 * edgeStride)];
            byte q3 = plane[q0Index + (3 * edgeStride)];
            byte q4 = plane[q0Index + (4 * edgeStride)];
            byte q5 = plane[q0Index + (5 * edgeStride)];
            byte q6 = plane[q0Index + (6 * edgeStride)];

            if (!FilterMask8(limit, blimit, p3, p2, p1, p0, q0, q1, q2, q3))
            {
                continue;
            }

            bool flat = FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3);
            bool flat2 = FlatMask4(1, p6, p5, p4, p0, q0, q4, q5, q6);

            if (flat && flat2)
            {
                (p5, p4, p3, p2, p1, p0, q0, q1, q2, q3, q4, q5) = ApplyWideFilter14(p6, p5, p4, p3, p2, p1, p0, q0, q1, q2, q3, q4, q5, q6);
                plane[q0Index - (6 * edgeStride)] = p5;
                plane[q0Index - (5 * edgeStride)] = p4;
                plane[q0Index - (4 * edgeStride)] = p3;
                plane[q0Index - (3 * edgeStride)] = p2;
                plane[q0Index + (2 * edgeStride)] = q2;
                plane[q0Index + (3 * edgeStride)] = q3;
                plane[q0Index + (4 * edgeStride)] = q4;
                plane[q0Index + (5 * edgeStride)] = q5;
            }
            else if (flat)
            {
                (p2, p1, p0, q0, q1, q2) = ApplyWideFilter8(p3, p2, p1, p0, q0, q1, q2, q3);
                plane[q0Index - (3 * edgeStride)] = p2;
                plane[q0Index + (2 * edgeStride)] = q2;
            }
            else
            {
                (p1, p0, q0, q1) = ApplyNarrowFilter(thresh, p1, p0, q0, q1);
            }

            plane[q0Index - (2 * edgeStride)] = p1;
            plane[q0Index - edgeStride] = p0;
            plane[q0Index] = q0;
            plane[q0Index + edgeStride] = q1;
        }
    }

    private static (byte P1, byte P0, byte Q0, byte Q1) ApplyNarrowFilter(byte thresh, byte p1, byte p0, byte q0, byte q1)
    {
        bool hev = HighEdgeVariance(thresh, p1, p0, q0, q1);
        sbyte ps1 = (sbyte)(p1 ^ 0x80);
        sbyte ps0 = (sbyte)(p0 ^ 0x80);
        sbyte qs0 = (sbyte)(q0 ^ 0x80);
        sbyte qs1 = (sbyte)(q1 ^ 0x80);

        sbyte filter = hev ? Saturate8(ps1 - qs1) : (sbyte)0;
        filter = (sbyte)Saturate8(filter + (3 * (qs0 - ps0)));

        sbyte filter1 = (sbyte)(Saturate8(filter + 4) >> 3);
        sbyte filter2 = (sbyte)(Saturate8(filter + 3) >> 3);

        byte newQ0 = (byte)(Saturate8(qs0 - filter1) ^ 0x80);
        byte newP0 = (byte)(Saturate8(ps0 + filter2) ^ 0x80);

        sbyte outerFilter = hev ? (sbyte)0 : RoundShift(filter1, 1);
        byte newQ1 = (byte)(Saturate8(qs1 - outerFilter) ^ 0x80);
        byte newP1 = (byte)(Saturate8(ps1 + outerFilter) ^ 0x80);

        return (newP1, newP0, newQ0, newQ1);
    }

    private static (byte P1, byte P0, byte Q0, byte Q1) ApplyChromaWideFilter(byte p2, byte p1, byte p0, byte q0, byte q1, byte q2)
    {
        byte newP1 = RoundShift((p2 * 3) + (p1 * 2) + (p0 * 2) + q0, 3);
        byte newP0 = RoundShift(p2 + (p1 * 2) + (p0 * 2) + (q0 * 2) + q1, 3);
        byte newQ0 = RoundShift(p1 + (p0 * 2) + (q0 * 2) + (q1 * 2) + q2, 3);
        byte newQ1 = RoundShift(p0 + (q0 * 2) + (q1 * 2) + (q2 * 3), 3);
        return (newP1, newP0, newQ0, newQ1);
    }

    private static (byte P2, byte P1, byte P0, byte Q0, byte Q1, byte Q2) ApplyWideFilter8(byte p3, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2, byte q3)
    {
        byte newP2 = RoundShift((3 * p3) + (2 * p2) + p1 + p0 + q0, 3);
        byte newP1 = RoundShift((2 * p3) + p2 + (2 * p1) + p0 + q0 + q1, 3);
        byte newP0 = RoundShift(p3 + p2 + p1 + (2 * p0) + q0 + q1 + q2, 3);
        byte newQ0 = RoundShift(p2 + p1 + p0 + (2 * q0) + q1 + q2 + q3, 3);
        byte newQ1 = RoundShift(p1 + p0 + q0 + (2 * q1) + q2 + (2 * q3), 3);
        byte newQ2 = RoundShift(p0 + q0 + q1 + (2 * q2) + (3 * q3), 3);
        return (newP2, newP1, newP0, newQ0, newQ1, newQ2);
    }

    private static (byte P5, byte P4, byte P3, byte P2, byte P1, byte P0, byte Q0, byte Q1, byte Q2, byte Q3, byte Q4, byte Q5)
        ApplyWideFilter14(byte p6, byte p5, byte p4, byte p3, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2, byte q3, byte q4, byte q5, byte q6)
    {
        byte newP5 = RoundShift((7 * p6) + (2 * p5) + (2 * p4) + p3 + p2 + p1 + p0 + q0, 4);
        byte newP4 = RoundShift((5 * p6) + (2 * p5) + (2 * p4) + (2 * p3) + p2 + p1 + p0 + q0 + q1, 4);
        byte newP3 = RoundShift((4 * p6) + p5 + (2 * p4) + (2 * p3) + (2 * p2) + p1 + p0 + q0 + q1 + q2, 4);
        byte newP2 = RoundShift((3 * p6) + p5 + p4 + (2 * p3) + (2 * p2) + (2 * p1) + p0 + q0 + q1 + q2 + q3, 4);
        byte newP1 = RoundShift((2 * p6) + p5 + p4 + p3 + (2 * p2) + (2 * p1) + (2 * p0) + q0 + q1 + q2 + q3 + q4, 4);
        byte newP0 = RoundShift(p6 + p5 + p4 + p3 + p2 + (2 * p1) + (2 * p0) + (2 * q0) + q1 + q2 + q3 + q4 + q5, 4);
        byte newQ0 = RoundShift(p5 + p4 + p3 + p2 + p1 + (2 * p0) + (2 * q0) + (2 * q1) + q2 + q3 + q4 + q5 + q6, 4);
        byte newQ1 = RoundShift(p4 + p3 + p2 + p1 + p0 + (2 * q0) + (2 * q1) + (2 * q2) + q3 + q4 + q5 + (2 * q6), 4);
        byte newQ2 = RoundShift(p3 + p2 + p1 + p0 + q0 + (2 * q1) + (2 * q2) + (2 * q3) + q4 + q5 + (3 * q6), 4);
        byte newQ3 = RoundShift(p2 + p1 + p0 + q0 + q1 + (2 * q2) + (2 * q3) + (2 * q4) + q5 + (4 * q6), 4);
        byte newQ4 = RoundShift(p1 + p0 + q0 + q1 + q2 + (2 * q3) + (2 * q4) + (2 * q5) + (5 * q6), 4);
        byte newQ5 = RoundShift(p0 + q0 + q1 + q2 + q3 + (2 * q4) + (2 * q5) + (7 * q6), 4);
        return (newP5, newP4, newP3, newP2, newP1, newP0, newQ0, newQ1, newQ2, newQ3, newQ4, newQ5);
    }

    private static bool FilterMask4(byte limit, byte blimit, byte p1, byte p0, byte q0, byte q1)
        => Math.Abs(p1 - p0) <= limit
            && Math.Abs(q1 - q0) <= limit
            && (Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) / 2) <= blimit;

    private static bool FilterMask6Chroma(byte limit, byte blimit, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2)
        => Math.Abs(p2 - p1) <= limit
            && Math.Abs(p1 - p0) <= limit
            && Math.Abs(q1 - q0) <= limit
            && Math.Abs(q2 - q1) <= limit
            && (Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) / 2) <= blimit;

    private static bool FilterMask8(byte limit, byte blimit, byte p3, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2, byte q3)
        => Math.Abs(p3 - p2) <= limit
            && Math.Abs(p2 - p1) <= limit
            && Math.Abs(p1 - p0) <= limit
            && Math.Abs(q1 - q0) <= limit
            && Math.Abs(q2 - q1) <= limit
            && Math.Abs(q3 - q2) <= limit
            && (Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) / 2) <= blimit;

    private static bool FlatMask3Chroma(byte thresh, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2)
        => Math.Abs(p1 - p0) <= thresh
            && Math.Abs(q1 - q0) <= thresh
            && Math.Abs(p2 - p0) <= thresh
            && Math.Abs(q2 - q0) <= thresh;

    private static bool FlatMask4(byte thresh, byte p3, byte p2, byte p1, byte p0, byte q0, byte q1, byte q2, byte q3)
        => Math.Abs(p1 - p0) <= thresh
            && Math.Abs(q1 - q0) <= thresh
            && Math.Abs(p2 - p0) <= thresh
            && Math.Abs(q2 - q0) <= thresh
            && Math.Abs(p3 - p0) <= thresh
            && Math.Abs(q3 - q0) <= thresh;

    private static bool HighEdgeVariance(byte thresh, byte p1, byte p0, byte q0, byte q1)
        => Math.Abs(p1 - p0) > thresh || Math.Abs(q1 - q0) > thresh;

    private static sbyte Saturate8(int value) => (sbyte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue);

    private static sbyte RoundShift(sbyte value, int shift) => (sbyte)((value + (1 << (shift - 1))) >> shift);

    private static byte RoundShift(int value, int shift) => (byte)((value + (1 << (shift - 1))) >> shift);
}
