// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

/// <summary>
/// libaom <c>aom_dsp/recenter.h</c> defines an encoder-side <c>recenter_nonneg</c> /
/// <c>recenter_finite_nonneg</c> pair that the inverse functions in
/// <see cref="Av1RecenterMath"/> must perfectly invert. These tests do the round-trip
/// for every value in the relevant ranges, which catches off-by-one / sign-flip
/// regressions in the inverse functions used by the loop-restoration parser.
/// </summary>
[Trait("Format", "Avif")]
public class Av1RecenterMathTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(63)]
    public void InverseRecenterNonNegative_RoundTrips(int reference)
    {
        for (int original = 0; original <= 128; original++)
        {
            int recentered = RecenterNonNegative(reference, original);
            int recovered = Av1RecenterMath.InverseRecenterNonNegative(reference, recentered);
            Assert.Equal(original, recovered);
        }
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(8, 3)]
    [InlineData(8, 7)]
    [InlineData(64, 0)]
    [InlineData(64, 32)]
    [InlineData(64, 63)]
    [InlineData(128, 47)]
    public void InverseRecenterFiniteNonNegative_RoundTrips(int n, int reference)
    {
        for (int original = 0; original < n; original++)
        {
            int recentered = RecenterFiniteNonNegative(n, reference, original);
            int recovered = Av1RecenterMath.InverseRecenterFiniteNonNegative(n, reference, recentered);
            Assert.Equal(original, recovered);
        }
    }

    // libaom recenter.h:recenter_nonneg.
    private static int RecenterNonNegative(int r, int v)
    {
        if (v > (r << 1))
        {
            return v;
        }

        if (v >= r)
        {
            return (v - r) << 1;
        }

        return ((r - v) << 1) - 1;
    }

    // libaom recenter.h:recenter_finite_nonneg.
    private static int RecenterFiniteNonNegative(int n, int r, int v)
    {
        if ((r << 1) <= n)
        {
            return RecenterNonNegative(r, v);
        }

        return RecenterNonNegative(n - 1 - r, n - 1 - v);
    }
}
