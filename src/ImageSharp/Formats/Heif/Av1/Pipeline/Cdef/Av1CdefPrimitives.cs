// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

/// <summary>
/// Per-block CDEF primitives. Implements the math from spec 7.15.2 (<c>cdef_filter</c>
/// process), 7.15.2.2 (direction search), and the <c>Constrain</c> helper used by the
/// filter inner loop. The frame-level driver (8x8 block-list + boundary padding + 64×64
/// loop) is not yet ported; this file only contains the math primitives.
/// </summary>
internal static class Av1CdefPrimitives
{
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

        int shift = damping - (int)Av1Math.FloorLog2((uint)threshold);
        if (shift < 0)
        {
            shift = 0;
        }

        int abs = diff < 0 ? -diff : diff;
        int sign = diff < 0 ? -1 : 1;
        int magnitude = threshold - (abs >> shift);
        if (magnitude < 0)
        {
            magnitude = 0;
        }
        else if (magnitude > abs)
        {
            magnitude = abs;
        }

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
        int i = 0;
        if (v6 != 0)
        {
            int msb = (int)Av1Math.FloorLog2((uint)v6);
            i = msb < 12 ? msb : 12;
        }

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
        Span<int> cost = stackalloc int[8];
        Span<int> partial = stackalloc int[8 * 15];

        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                int x = (img[(i * stride) + j] >> coeffShift) - 128;
                partial[(0 * 15) + i + j] += x;
                partial[(1 * 15) + i + (j / 2)] += x;
                partial[(2 * 15) + i] += x;
                partial[(3 * 15) + 3 + i - (j / 2)] += x;
                partial[(4 * 15) + 7 + i - j] += x;
                partial[(5 * 15) + 3 - (i / 2) + j] += x;
                partial[(6 * 15) + j] += x;
                partial[(7 * 15) + (i / 2) + j] += x;
            }
        }

        for (int i = 0; i < 8; i++)
        {
            int p2 = partial[(2 * 15) + i];
            cost[2] += p2 * p2;
            int p6 = partial[(6 * 15) + i];
            cost[6] += p6 * p6;
        }

        cost[2] *= DivTable[8];
        cost[6] *= DivTable[8];

        for (int i = 0; i < 7; i++)
        {
            int p0a = partial[(0 * 15) + i];
            int p0b = partial[(0 * 15) + 14 - i];
            cost[0] += ((p0a * p0a) + (p0b * p0b)) * DivTable[i + 1];
            int p4a = partial[(4 * 15) + i];
            int p4b = partial[(4 * 15) + 14 - i];
            cost[4] += ((p4a * p4a) + (p4b * p4b)) * DivTable[i + 1];
        }

        int p07 = partial[(0 * 15) + 7];
        cost[0] += p07 * p07 * DivTable[8];
        int p47 = partial[(4 * 15) + 7];
        cost[4] += p47 * p47 * DivTable[8];

        for (int i = 1; i < 8; i += 2)
        {
            for (int j = 0; j < 5; j++)
            {
                int p = partial[(i * 15) + 3 + j];
                cost[i] += p * p;
            }

            cost[i] *= DivTable[8];

            for (int j = 0; j < 3; j++)
            {
                int pa = partial[(i * 15) + j];
                int pb = partial[(i * 15) + 10 - j];
                cost[i] += ((pa * pa) + (pb * pb)) * DivTable[(2 * j) + 2];
            }
        }

        int bestCost = 0;
        int bestDir = 0;
        for (int i = 0; i < 8; i++)
        {
            if (cost[i] > bestCost)
            {
                bestCost = cost[i];
                bestDir = i;
            }
        }

        variance = (bestCost - cost[(bestDir + 4) & 7]) >> 10;
        return bestDir;
    }

    /// <summary>
    /// Spec 7.15.2.1 filter step (8-bit destination). Applies the primary filter (two taps
    /// along <paramref name="direction"/>) plus the secondary filter (two taps each at
    /// <paramref name="direction"/> ± 2) to <paramref name="input"/>, then clips to the
    /// per-pixel min/max envelope (per the spec's <c>clip</c> step when both primary and
    /// secondary are active) and writes 8-bit output to <paramref name="dst"/>.
    /// </summary>
    public static void FilterBlock(
        Span<byte> dst,
        int dstStride,
        ReadOnlySpan<ushort> input,
        int inputStride,
        int primaryStrength,
        int secondaryStrength,
        int direction,
        int primaryDamping,
        int secondaryDamping,
        int coeffShift,
        int blockWidth,
        int blockHeight,
        bool enablePrimary,
        bool enableSecondary)
    {
        bool clip = enablePrimary && enableSecondary;
        int[] primaryTaps = Av1CdefConstants.PrimaryTaps[(primaryStrength >> coeffShift) & 1];
        int[] secondaryTaps = Av1CdefConstants.SecondaryTaps;
        (int Dy, int Dx)[] dirPri = Av1CdefConstants.Directions[direction + 2];
        (int Dy, int Dx)[] dirSecPlus = Av1CdefConstants.Directions[direction + 4];
        (int Dy, int Dx)[] dirSecMinus = Av1CdefConstants.Directions[direction];

        for (int i = 0; i < blockHeight; i++)
        {
            for (int j = 0; j < blockWidth; j++)
            {
                int sum = 0;
                int x = input[(i * inputStride) + j];
                int max = x;
                int min = x;

                for (int k = 0; k < 2; k++)
                {
                    if (enablePrimary)
                    {
                        int p0 = input[((i + dirPri[k].Dy) * inputStride) + j + dirPri[k].Dx];
                        int p1 = input[((i - dirPri[k].Dy) * inputStride) + j - dirPri[k].Dx];
                        sum += primaryTaps[k] * Constrain(p0 - x, primaryStrength, primaryDamping);
                        sum += primaryTaps[k] * Constrain(p1 - x, primaryStrength, primaryDamping);
                        if (clip)
                        {
                            if (p0 != Av1CdefConstants.VeryLarge && p0 > max)
                            {
                                max = p0;
                            }

                            if (p1 != Av1CdefConstants.VeryLarge && p1 > max)
                            {
                                max = p1;
                            }

                            if (p0 < min)
                            {
                                min = p0;
                            }

                            if (p1 < min)
                            {
                                min = p1;
                            }
                        }
                    }

                    if (enableSecondary)
                    {
                        int s0 = input[((i + dirSecPlus[k].Dy) * inputStride) + j + dirSecPlus[k].Dx];
                        int s1 = input[((i - dirSecPlus[k].Dy) * inputStride) + j - dirSecPlus[k].Dx];
                        int s2 = input[((i + dirSecMinus[k].Dy) * inputStride) + j + dirSecMinus[k].Dx];
                        int s3 = input[((i - dirSecMinus[k].Dy) * inputStride) + j - dirSecMinus[k].Dx];
                        if (clip)
                        {
                            if (s0 != Av1CdefConstants.VeryLarge && s0 > max)
                            {
                                max = s0;
                            }

                            if (s1 != Av1CdefConstants.VeryLarge && s1 > max)
                            {
                                max = s1;
                            }

                            if (s2 != Av1CdefConstants.VeryLarge && s2 > max)
                            {
                                max = s2;
                            }

                            if (s3 != Av1CdefConstants.VeryLarge && s3 > max)
                            {
                                max = s3;
                            }

                            if (s0 < min)
                            {
                                min = s0;
                            }

                            if (s1 < min)
                            {
                                min = s1;
                            }

                            if (s2 < min)
                            {
                                min = s2;
                            }

                            if (s3 < min)
                            {
                                min = s3;
                            }
                        }

                        sum += secondaryTaps[k] * Constrain(s0 - x, secondaryStrength, secondaryDamping);
                        sum += secondaryTaps[k] * Constrain(s1 - x, secondaryStrength, secondaryDamping);
                        sum += secondaryTaps[k] * Constrain(s2 - x, secondaryStrength, secondaryDamping);
                        sum += secondaryTaps[k] * Constrain(s3 - x, secondaryStrength, secondaryDamping);
                    }
                }

                int y = x + ((8 + sum - (sum < 0 ? 1 : 0)) >> 4);
                if (clip)
                {
                    if (y < min)
                    {
                        y = min;
                    }
                    else if (y > max)
                    {
                        y = max;
                    }
                }

                if (y < 0)
                {
                    y = 0;
                }
                else if (y > 255)
                {
                    y = 255;
                }

                dst[(i * dstStride) + j] = (byte)y;
            }
        }
    }
}
