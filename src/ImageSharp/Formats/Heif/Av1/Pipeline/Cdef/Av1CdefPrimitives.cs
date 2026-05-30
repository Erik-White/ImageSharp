// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

/// <summary>
/// Per-block CDEF primitives. Implements the math from spec 7.15.2 (<c>cdef_filter</c>
/// process), 7.15.2.2 (direction search), and the <c>Constrain</c> helper used by the
/// filter inner loop. <see cref="Av1CdefUnitDriver"/> drives these primitives over the
/// 64×64 unit grid.
/// </summary>
internal static class Av1CdefPrimitives
{
    /// <summary>
    /// CDEF filter cell dimension in pixels. Spec 7.15.2 fixes the filtered block at 8×8.
    /// </summary>
    public const int CellSize = 8;

    /// <summary>
    /// Divisor proxies used by the direction-search cost normalisation. Below spec
    /// resolution — the spec only states the cost is the variance along a line; libaom
    /// replaces the per-direction division by <c>n</c> with a fixed-point multiplication
    /// by <c>3*5*7*8/n</c> to avoid the divide. See libaom
    /// <c>cdef_block.c:cdef_find_dir</c>'s <c>div_table</c>.
    /// </summary>
    private static readonly int[] DivTable = [0, 840, 420, 280, 210, 168, 140, 120, 105];

    /// <summary>
    /// Spec 7.15.2 <c>Constrain</c> (also written as a single-line definition inside the
    /// pseudocode): soft-thresholds <paramref name="diff"/> toward 0 using a magnitude
    /// shaped by <paramref name="threshold"/> and a damping-controlled falloff.
    /// </summary>
    public static int Constrain(int diff, int threshold, int damping)
    {
        if (threshold == 0)
        {
            return 0;
        }

        int shift = Math.Max(0, damping - (int)Av1Math.FloorLog2((uint)threshold));
        int abs = Math.Abs(diff);
        int sign = diff < 0 ? -1 : 1;
        int magnitude = Math.Clamp(threshold - (abs >> shift), 0, abs);
        return sign * magnitude;
    }

    /// <summary>
    /// Spec 7.15.2 strength-adjustment step: scales the primary strength up when the 8×8
    /// block's variance proxy indicates a high-contrast directional pattern (the spec's
    /// "if Var &gt; 0" branch, with the same <c>(strength * (4 + i) + 8) &gt;&gt; 4</c>
    /// formula and the <c>i = min(FloorLog2(Var &gt;&gt; 6), 12)</c> clamp).
    /// </summary>
    public static int AdjustStrength(int strength, int variance)
    {
        if (variance == 0)
        {
            return 0;
        }

        int v6 = variance >> 6;
        int i = v6 == 0 ? 0 : Math.Min((int)Av1Math.FloorLog2((uint)v6), 12);
        return ((strength * (4 + i)) + 8) >> 4;
    }

    /// <summary>
    /// Spec 7.15.2.2 direction search: returns the dominant direction (0..7) for the 8×8
    /// block at <paramref name="img"/> by maximising the squared partial-sum cost along
    /// each of the 8 candidate lines. Also returns a variance proxy
    /// (<c>best_cost − orthogonal_cost</c>) that downstream strength scaling consumes via
    /// <see cref="AdjustStrength"/>. The fixed-point cost normalisation via
    /// <see cref="DivTable"/> is below spec resolution.
    /// </summary>
    public static int FindDirection(ReadOnlySpan<ushort> img, int stride, out int variance, int coeffShift)
    {
        Span<int> partial = stackalloc int[8 * 15];
        AccumulatePartials(img, stride, coeffShift, partial);

        Span<int> cost = stackalloc int[8];
        ComputeCosts(partial, cost);

        int bestDir = SelectBestDirection(cost, out int bestCost);
        variance = (bestCost - cost[(bestDir + 4) & 7]) >> 10;
        return bestDir;
    }

    /// <summary>
    /// Spec 7.15.2.1 filter step (8-bit destination). Applies the primary filter (two taps
    /// along <paramref name="direction"/>) plus the secondary filter (two taps each at
    /// <paramref name="direction"/> ± 2) to the <see cref="CellSize"/>×<see cref="CellSize"/>
    /// block at <paramref name="inputOffset"/>, then clips to the per-pixel min/max envelope
    /// (the spec's <c>clip</c> step when both primary and secondary are active) and writes
    /// 8-bit output to <paramref name="dst"/>. The primary and secondary filters are enabled
    /// by their respective strengths being non-zero.
    /// </summary>
    public static void FilterBlock(
        Span<byte> dst,
        int dstStride,
        ReadOnlySpan<ushort> input,
        int inputOffset,
        int inputStride,
        int primaryStrength,
        int secondaryStrength,
        int direction,
        int primaryDamping,
        int secondaryDamping,
        int coeffShift)
    {
        FilterContext context = new(
            input,
            inputOffset,
            inputStride,
            Av1CdefConstants.PrimaryTaps[(primaryStrength >> coeffShift) & 1],
            Av1CdefConstants.SecondaryTaps,
            Av1CdefConstants.Directions[direction + 2],
            Av1CdefConstants.Directions[direction + 4],
            Av1CdefConstants.Directions[direction],
            primaryStrength,
            secondaryStrength,
            primaryDamping,
            secondaryDamping);

        for (int i = 0; i < CellSize; i++)
        {
            for (int j = 0; j < CellSize; j++)
            {
                dst[(i * dstStride) + j] = FilterPixel(in context, i, j);
            }
        }
    }

    private static void AccumulatePartials(ReadOnlySpan<ushort> img, int stride, int coeffShift, Span<int> partial)
    {
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                int x = (img[(i * stride) + j] >> coeffShift) - 128;
                partial[i + j] += x;                       // line 0
                partial[15 + i + (j / 2)] += x;            // line 1
                partial[(2 * 15) + i] += x;                // line 2
                partial[(3 * 15) + 3 + i - (j / 2)] += x;  // line 3
                partial[(4 * 15) + 7 + i - j] += x;        // line 4
                partial[(5 * 15) + 3 - (i / 2) + j] += x;  // line 5
                partial[(6 * 15) + j] += x;                // line 6
                partial[(7 * 15) + (i / 2) + j] += x;      // line 7
            }
        }
    }

    private static void ComputeCosts(ReadOnlySpan<int> partial, Span<int> cost)
    {
        // Lines 2 and 6 (perfectly horizontal/vertical): each partial contributes a single
        // squared sum scaled by the n=8 divisor.
        for (int i = 0; i < 8; i++)
        {
            int p2 = partial[(2 * 15) + i];
            cost[2] += p2 * p2;
            int p6 = partial[(6 * 15) + i];
            cost[6] += p6 * p6;
        }

        cost[2] *= DivTable[8];
        cost[6] *= DivTable[8];

        // Lines 0 and 4 (the two diagonals): 7 mirrored pairs scaled by their respective
        // line lengths plus a centre term scaled by 8.
        for (int i = 0; i < 7; i++)
        {
            cost[0] += SquareMirroredPair(partial, 0, i) * DivTable[i + 1];
            cost[4] += SquareMirroredPair(partial, 4, i) * DivTable[i + 1];
        }

        int p07 = partial[(0 * 15) + 7];
        cost[0] += p07 * p07 * DivTable[8];
        int p47 = partial[(4 * 15) + 7];
        cost[4] += p47 * p47 * DivTable[8];

        // Lines 1, 3, 5, 7 (off-diagonal): five centre terms scaled by 8, plus three
        // mirrored pairs scaled by even divisors.
        for (int line = 1; line < 8; line += 2)
        {
            cost[line] = ComputeOffDiagonalCost(partial, line);
        }
    }

    private static int SquareMirroredPair(ReadOnlySpan<int> partial, int line, int i)
    {
        int a = partial[(line * 15) + i];
        int b = partial[(line * 15) + 14 - i];
        return (a * a) + (b * b);
    }

    private static int ComputeOffDiagonalCost(ReadOnlySpan<int> partial, int line)
    {
        int sum = 0;
        for (int j = 0; j < 5; j++)
        {
            int p = partial[(line * 15) + 3 + j];
            sum += p * p;
        }

        sum *= DivTable[8];

        // Spec 7.15.2: off-diagonal lines pair partial[i][j] with partial[i][10 - j], unlike
        // the full-length diagonals (lines 0/4) whose pairs mirror about 14.
        for (int j = 0; j < 3; j++)
        {
            int a = partial[(line * 15) + j];
            int b = partial[(line * 15) + 10 - j];
            sum += ((a * a) + (b * b)) * DivTable[(2 * j) + 2];
        }

        return sum;
    }

    private static int SelectBestDirection(ReadOnlySpan<int> cost, out int bestCost)
    {
        bestCost = 0;
        int bestDir = 0;
        for (int i = 0; i < 8; i++)
        {
            if (cost[i] > bestCost)
            {
                bestCost = cost[i];
                bestDir = i;
            }
        }

        return bestDir;
    }

    private static byte FilterPixel(in FilterContext ctx, int i, int j)
    {
        int origin = ctx.InputOffset + (i * ctx.InputStride) + j;
        int x = ctx.Input[origin];
        int max = x;
        int min = x;
        int sum = 0;

        for (int k = 0; k < 2; k++)
        {
            if (ctx.EnablePrimary)
            {
                int p0 = ctx.Input[origin + (ctx.DirectionPrimary[k].Dy * ctx.InputStride) + ctx.DirectionPrimary[k].Dx];
                int p1 = ctx.Input[origin - (ctx.DirectionPrimary[k].Dy * ctx.InputStride) - ctx.DirectionPrimary[k].Dx];
                sum += ctx.PrimaryTaps[k] * (Constrain(p0 - x, ctx.PrimaryStrength, ctx.PrimaryDamping)
                                              + Constrain(p1 - x, ctx.PrimaryStrength, ctx.PrimaryDamping));
                if (ctx.Clip)
                {
                    UpdateEnvelope(ref max, ref min, p0);
                    UpdateEnvelope(ref max, ref min, p1);
                }
            }

            if (ctx.EnableSecondary)
            {
                int s0 = ctx.Input[origin + (ctx.DirectionSecondaryPlus[k].Dy * ctx.InputStride) + ctx.DirectionSecondaryPlus[k].Dx];
                int s1 = ctx.Input[origin - (ctx.DirectionSecondaryPlus[k].Dy * ctx.InputStride) - ctx.DirectionSecondaryPlus[k].Dx];
                int s2 = ctx.Input[origin + (ctx.DirectionSecondaryMinus[k].Dy * ctx.InputStride) + ctx.DirectionSecondaryMinus[k].Dx];
                int s3 = ctx.Input[origin - (ctx.DirectionSecondaryMinus[k].Dy * ctx.InputStride) - ctx.DirectionSecondaryMinus[k].Dx];
                if (ctx.Clip)
                {
                    UpdateEnvelope(ref max, ref min, s0);
                    UpdateEnvelope(ref max, ref min, s1);
                    UpdateEnvelope(ref max, ref min, s2);
                    UpdateEnvelope(ref max, ref min, s3);
                }

                sum += ctx.SecondaryTaps[k] * (Constrain(s0 - x, ctx.SecondaryStrength, ctx.SecondaryDamping)
                                                + Constrain(s1 - x, ctx.SecondaryStrength, ctx.SecondaryDamping)
                                                + Constrain(s2 - x, ctx.SecondaryStrength, ctx.SecondaryDamping)
                                                + Constrain(s3 - x, ctx.SecondaryStrength, ctx.SecondaryDamping));
            }
        }

        int y = x + ((8 + sum - (sum < 0 ? 1 : 0)) >> 4);
        if (ctx.Clip)
        {
            y = Math.Clamp(y, min, max);
        }

        return (byte)Math.Clamp(y, 0, 255);
    }

    private static void UpdateEnvelope(ref int max, ref int min, int sample)
    {
        // VeryLarge samples are off-frame stamps from the boundary padder; they must not
        // raise the maximum but they may legitimately stay below all on-frame samples
        // (and so participate in the minimum unchanged).
        if (sample != Av1CdefConstants.VeryLarge && sample > max)
        {
            max = sample;
        }

        if (sample < min)
        {
            min = sample;
        }
    }

    private readonly ref struct FilterContext
    {
        public FilterContext(
            ReadOnlySpan<ushort> input,
            int inputOffset,
            int inputStride,
            int[] primaryTaps,
            int[] secondaryTaps,
            Av1CdefDirectionOffset[] directionPrimary,
            Av1CdefDirectionOffset[] directionSecondaryPlus,
            Av1CdefDirectionOffset[] directionSecondaryMinus,
            int primaryStrength,
            int secondaryStrength,
            int primaryDamping,
            int secondaryDamping)
        {
            this.Input = input;
            this.InputOffset = inputOffset;
            this.InputStride = inputStride;
            this.PrimaryTaps = primaryTaps;
            this.SecondaryTaps = secondaryTaps;
            this.DirectionPrimary = directionPrimary;
            this.DirectionSecondaryPlus = directionSecondaryPlus;
            this.DirectionSecondaryMinus = directionSecondaryMinus;
            this.PrimaryStrength = primaryStrength;
            this.SecondaryStrength = secondaryStrength;
            this.PrimaryDamping = primaryDamping;
            this.SecondaryDamping = secondaryDamping;
            this.EnablePrimary = primaryStrength != 0;
            this.EnableSecondary = secondaryStrength != 0;
            this.Clip = this.EnablePrimary && this.EnableSecondary;
        }

        public ReadOnlySpan<ushort> Input { get; }

        public int InputOffset { get; }

        public int InputStride { get; }

        public int[] PrimaryTaps { get; }

        public int[] SecondaryTaps { get; }

        public Av1CdefDirectionOffset[] DirectionPrimary { get; }

        public Av1CdefDirectionOffset[] DirectionSecondaryPlus { get; }

        public Av1CdefDirectionOffset[] DirectionSecondaryMinus { get; }

        public int PrimaryStrength { get; }

        public int SecondaryStrength { get; }

        public int PrimaryDamping { get; }

        public int SecondaryDamping { get; }

        public bool EnablePrimary { get; }

        public bool EnableSecondary { get; }

        public bool Clip { get; }
    }
}
