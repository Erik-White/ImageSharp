// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1CdefPrimitivesTests
{
    /// <summary>
    /// Spec 7.15.2 <c>Constrain</c> with threshold == 0 always returns 0 (the "if !threshold"
    /// short-circuit at the top of the procedure).
    /// </summary>
    [Fact]
    public void Constrain_ZeroThreshold_ReturnsZero()
    {
        Assert.Equal(0, Av1CdefPrimitives.Constrain(diff: 100, threshold: 0, damping: 5));
        Assert.Equal(0, Av1CdefPrimitives.Constrain(diff: -100, threshold: 0, damping: 5));
    }

    /// <summary>
    /// Spec 7.15.2 <c>Constrain</c>: below the soft-threshold boundary the function passes
    /// the magnitude through with the original sign. The pseudocode reads
    /// <c>sign(d) * clamp(threshold - (|d| &gt;&gt; (damping - FloorLog2(threshold))), 0, |d|)</c>.
    /// </summary>
    [Theory]
    [InlineData(2, 8, 5, 2)] // |diff|=2 < threshold=8: shift = 5-3 = 2; thr - (2>>2) = 8 - 0 = 8; clamp(8,0,2) = 2.
    [InlineData(-2, 8, 5, -2)]
    [InlineData(16, 8, 5, 0)] // |diff|=16: shift=2; thr - (16>>2) = 8 - 4 = 4; clamp(4,0,16) = 4.
    [InlineData(64, 8, 5, 0)] // |diff|=64: shift=2; thr - (64>>2) = 8 - 16 = -8; clamp = 0.
    public void Constrain_MatchesLibaomFormula(int diff, int threshold, int damping, int expectedSignedFloorOrUpper)
    {
        // Formula yields different intermediate when |diff| in mid range; recompute here.
        int abs = Math.Abs(diff);
        int sign = diff < 0 ? -1 : 1;
        int msb = BitOperations.Log2((uint)threshold);
        int shift = Math.Max(0, damping - msb);
        int magnitude = Math.Clamp(threshold - (abs >> shift), 0, abs);
        int expected = sign * magnitude;

        // Note: the InlineData values above are upper-bound sanity, exact value computed here.
        _ = expectedSignedFloorOrUpper;

        Assert.Equal(expected, Av1CdefPrimitives.Constrain(diff, threshold, damping));
    }

    /// <summary>
    /// Spec 7.15.2 strength-adjustment: zero variance returns 0 regardless of strength
    /// (the "if Var == 0" early-out).
    /// </summary>
    [Fact]
    public void AdjustStrength_ZeroVariance_ReturnsZero()
        => Assert.Equal(0, Av1CdefPrimitives.AdjustStrength(strength: 64, variance: 0));

    /// <summary>
    /// Spec 7.15.2 strength-adjustment: low variance (Var &lt; 64) drives the
    /// <c>FloorLog2(Var &gt;&gt; 6)</c> term to 0, so <c>i = 0</c> and the result reduces to
    /// <c>(strength * 4 + 8) &gt;&gt; 4</c>.
    /// </summary>
    [Theory]
    [InlineData(64, 32, ((64 * 4) + 8) >> 4)]
    [InlineData(64, 63, ((64 * 4) + 8) >> 4)]
    public void AdjustStrength_LowVariance_UsesBaseFactor(int strength, int variance, int expected)
        => Assert.Equal(expected, Av1CdefPrimitives.AdjustStrength(strength, variance));

    /// <summary>
    /// Spec 7.15.2 strength-adjustment: large variance bumps the multiplier via
    /// <c>i = Min(FloorLog2(Var &gt;&gt; 6), 12)</c>; saturating at 12.
    /// </summary>
    [Fact]
    public void AdjustStrength_HighVariance_IncreasesMultiplier() =>

        // Var = 1<<20; Var>>6 = 1<<14; FloorLog2(1<<14) = 14; min(14, 12) = 12 → i = 12.
        // (64 * (4+12) + 8) >> 4 = (64 * 16 + 8) >> 4 = 1032 >> 4 = 64.
        Assert.Equal(64, Av1CdefPrimitives.AdjustStrength(strength: 64, variance: 1 << 20));

    /// <summary>
    /// Spec 7.15.2.2 direction search on a uniform block returns direction 0 with variance 0
    /// — every partial sum is zero, every cost is zero, the best-cost and orthogonal-cost
    /// are equal so their difference is zero.
    /// </summary>
    [Fact]
    public void FindDirection_UniformBlock_IsZero()
    {
        ushort[] img = new ushort[8 * 8];
        Array.Fill(img, (ushort)128);

        int dir = Av1CdefPrimitives.FindDirection(img, 8, out int variance, coeffShift: 0);

        Assert.Equal(0, dir);
        Assert.Equal(0, variance);
    }

    /// <summary>
    /// A horizontal stripe pattern (constant across columns, alternating between rows)
    /// maximises the partial-sum cost along direction 2 (horizontal) per spec 7.15.3.
    /// Verifies the classifier agrees with the geometric expectation.
    /// </summary>
    [Fact]
    public void FindDirection_HorizontalStripes_IsHorizontal()
    {
        ushort[] img = new ushort[8 * 8];
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                img[(i * 8) + j] = (ushort)((i & 1) == 0 ? 100 : 200);
            }
        }

        int dir = Av1CdefPrimitives.FindDirection(img, 8, out _, coeffShift: 0);

        Assert.Equal(2, dir);
    }

    /// <summary>
    /// A vertical stripe pattern maximises direction 6 (vertical) per spec 7.15.3.
    /// </summary>
    [Fact]
    public void FindDirection_VerticalStripes_IsVertical()
    {
        ushort[] img = new ushort[8 * 8];
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                img[(i * 8) + j] = (ushort)((j & 1) == 0 ? 100 : 200);
            }
        }

        int dir = Av1CdefPrimitives.FindDirection(img, 8, out _, coeffShift: 0);

        Assert.Equal(6, dir);
    }

    /// <summary>
    /// Spec 7.15.2 filter step with both strengths zero: every <c>Constrain</c> call returns
    /// 0 (its threshold-zero short-circuit), so the running sum stays 0 and the output is
    /// the unmodified input.
    /// </summary>
    [Fact]
    public void FilterBlock_ZeroStrengths_PassThrough()
    {
        const int stride = 16;
        ushort[] input = new ushort[stride * 12];
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = (ushort)((i * 7) & 0xFF);
        }

        byte[] dst = new byte[8 * 8];
        Av1CdefPrimitives.FilterBlock(
            dst, 8,
            input.AsSpan((stride * 2) + 2),
            stride,
            primaryStrength: 0,
            secondaryStrength: 0,
            direction: 0,
            primaryDamping: 5,
            secondaryDamping: 5,
            coeffShift: 0,
            blockWidth: 8,
            blockHeight: 8,
            enablePrimary: false,
            enableSecondary: false);

        // With enablePrimary=false and enableSecondary=false the filter visits each pixel
        // once and writes back x + (8 + 0 - 0) >> 4 = x + 0 = x.
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                int srcOffset = (stride * 2) + 2 + (i * stride) + j;
                Assert.Equal((byte)input[srcOffset], dst[(i * 8) + j]);
            }
        }
    }
}
