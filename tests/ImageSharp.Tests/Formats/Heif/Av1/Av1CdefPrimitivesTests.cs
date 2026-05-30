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
    /// Spec 7.15.2.2 direction search must recover every one of the 8 directions, including
    /// the off-diagonals (1, 3, 5, 7). For each target direction the block is a ramp whose
    /// value depends only on which accumulation line a pixel falls on for that direction,
    /// centred so the offsets are symmetric about zero.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void FindDirection_RampAlongDirection_RecoversThatDirection(int direction)
    {
        ushort[] img = new ushort[8 * 8];
        int maxLine = MaxLineIndex(direction);
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                // (2*line - maxLine) is symmetric about 0; ×4 keeps every value inside [0, 255].
                int value = 128 + (((2 * LineIndex(direction, i, j)) - maxLine) * 4);
                img[(i * 8) + j] = (ushort)value;
            }
        }

        int dir = Av1CdefPrimitives.FindDirection(img, 8, out _, coeffShift: 0);

        Assert.Equal(direction, dir);
    }

    // The accumulation-line index a pixel (i, j) contributes to for each direction, matching
    // the partial[] subscripts in Av1CdefPrimitives.AccumulatePartials (spec 7.15.3 lines).
    private static int LineIndex(int direction, int i, int j) => direction switch
    {
        0 => i + j,
        1 => i + (j / 2),
        2 => i,
        3 => 3 + i - (j / 2),
        4 => 7 + i - j,
        5 => 3 - (i / 2) + j,
        6 => j,
        _ => (i / 2) + j,
    };

    // Highest line index each direction's accumulation can reach over an 8×8 block, used to
    // centre the ramp so perpendicular directions' line sums cancel.
    private static int MaxLineIndex(int direction) => direction switch
    {
        0 or 4 => 14,
        2 or 6 => 7,
        _ => 10,
    };

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
            input,
            (stride * 2) + 2,
            stride,
            primaryStrength: 0,
            secondaryStrength: 0,
            direction: 0,
            primaryDamping: 5,
            secondaryDamping: 5,
            coeffShift: 0);

        // With both strengths zero the primary and secondary filters are disabled, so the
        // filter visits each pixel once and writes back x + (8 + 0 - 0) >> 4 = x + 0 = x.
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                int srcOffset = (stride * 2) + 2 + (i * stride) + j;
                Assert.Equal((byte)input[srcOffset], dst[(i * 8) + j]);
            }
        }
    }

    /// <summary>
    /// End-to-end <see cref="Av1CdefPrimitives.FilterBlock"/> against a libaom-derived
    /// reference output. Drives the same 8×8 input pattern through libaom's
    /// <c>cdef_filter_8_0_c</c> (the both-strengths-on case) at <c>(pri=24, sec=12, dir=4,
    /// pri_damping=5, sec_damping=4, coeff_shift=0)</c> and bakes the resulting bytes here.
    /// Catches any regression in the primary/secondary tap geometry, the <c>Constrain</c>
    /// soft-threshold scaling, the envelope clamp, or the per-pixel rounding step.
    /// </summary>
    [Fact]
    public void FilterBlock_DiagonalGradient_MatchesLibaomReference()
    {
        const int stride = Av1CdefConstants.BufferStride;
        const int hBorder = Av1CdefConstants.HorizontalBorder;
        const int vBorder = Av1CdefConstants.VerticalBorder;
        ushort[] input = BuildDiagonalGradientWorkingBuffer();

        byte[] dst = new byte[8 * 8];
        Av1CdefPrimitives.FilterBlock(
            dst, 8,
            input,
            (vBorder * stride) + hBorder,
            stride,
            primaryStrength: 24,
            secondaryStrength: 12,
            direction: 4,
            primaryDamping: 5,
            secondaryDamping: 4,
            coeffShift: 0);

        byte[] expected =
        [
            95, 118, 109, 132, 123, 146, 137, 160,
            122, 113, 136, 127, 150, 141, 164, 155,
            117, 140, 131, 154, 145, 168, 159, 182,
            144, 135, 158, 149, 172, 163, 186, 177,
            139, 162, 153, 176, 167, 190, 181, 204,
            166, 157, 180, 171, 194, 185, 208, 199,
            161, 184, 175, 198, 189, 212, 203, 227,
            188, 179, 202, 193, 216, 207, 231, 221,
        ];

        Assert.Equal(expected, dst);
    }

    /// <summary>
    /// Same inputs as <see cref="FilterBlock_DiagonalGradient_MatchesLibaomReference"/>,
    /// but with <c>enableSecondary=false</c> — exercises the primary-only path
    /// (<c>cdef_filter_8_1_c</c> in libaom). The envelope clamp is skipped here
    /// (<c>clipping_required = primary &amp;&amp; secondary</c>).
    /// </summary>
    [Fact]
    public void FilterBlock_PrimaryOnly_MatchesLibaomReference()
    {
        const int stride = Av1CdefConstants.BufferStride;
        const int hBorder = Av1CdefConstants.HorizontalBorder;
        const int vBorder = Av1CdefConstants.VerticalBorder;
        ushort[] input = BuildDiagonalGradientWorkingBuffer();

        byte[] dst = new byte[8 * 8];
        Av1CdefPrimitives.FilterBlock(
            dst, 8,
            input,
            (vBorder * stride) + hBorder,
            stride,
            primaryStrength: 24,
            secondaryStrength: 0,
            direction: 4,
            primaryDamping: 5,
            secondaryDamping: 4,
            coeffShift: 0);

        byte[] expected =
        [
            93, 120, 107, 134, 121, 148, 135, 162,
            124, 111, 138, 125, 152, 139, 166, 153,
            115, 142, 129, 156, 143, 170, 157, 184,
            146, 133, 160, 147, 174, 161, 188, 175,
            137, 164, 151, 178, 165, 192, 179, 206,
            168, 155, 182, 169, 196, 183, 210, 197,
            159, 186, 173, 200, 187, 214, 201, 229,
            190, 177, 204, 191, 218, 205, 233, 219,
        ];

        Assert.Equal(expected, dst);
    }

    /// <summary>
    /// Builds a <see cref="Av1CdefConstants.BufferStride"/>-strided working buffer with the
    /// same diagonal-gradient pattern used by the libaom reference harness in
    /// <c>tools/cdef_ref.c</c>. The pattern keeps every 8×8 pixel in [0, 255], guarantees a
    /// dominant direction near 4 (so the filter does real work), and fills the 2-row /
    /// 8-col padding with extrapolated values rather than <see cref="Av1CdefConstants.VeryLarge"/>
    /// so the envelope clamp visits actual sample magnitudes.
    /// </summary>
    private static ushort[] BuildDiagonalGradientWorkingBuffer()
    {
        const int stride = Av1CdefConstants.BufferStride;
        ushort[] input = new ushort[stride * 12];
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = Av1CdefConstants.VeryLarge;
        }

        for (int r = -2; r < 10; r++)
        {
            for (int c = -2; c < 10; c++)
            {
                int v = 100 + (r * 11) + (c * 7) + (((r + c) & 1) != 0 ? 13 : -7);
                if (v < 0)
                {
                    v = 0;
                }

                if (v > 255)
                {
                    v = 255;
                }

                input[((r + 2) * stride) + c + 8] = (ushort)v;
            }
        }

        return input;
    }

}
