// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;

/// <summary>
/// libaom <c>aom_dsp/recenter.h</c>: inverse-recenter helpers used by the loop
/// restoration filter parameter parsers to map a reference-relative subexp code back
/// to the absolute filter coefficient. Pulled into its own class so it's directly
/// unit-testable without standing up a full <see cref="Av1SymbolReader"/>.
/// </summary>
internal static class Av1RecenterMath
{
    /// <summary>
    /// libaom <c>inv_recenter_nonneg</c>: invert the recentering of a non-negative
    /// literal <paramref name="v"/> around reference <paramref name="reference"/>.
    /// </summary>
    public static int InverseRecenterNonNegative(int reference, int v)
    {
        if (v > (reference << 1))
        {
            return v;
        }

        if ((v & 1) == 0)
        {
            return (v >> 1) + reference;
        }

        return reference - ((v + 1) >> 1);
    }

    /// <summary>
    /// libaom <c>inv_recenter_finite_nonneg</c>: invert the recentering of a value in
    /// [0, n-1] around reference <paramref name="reference"/> also in [0, n-1].
    /// </summary>
    public static int InverseRecenterFiniteNonNegative(int n, int reference, int v)
    {
        if ((reference << 1) <= n)
        {
            return InverseRecenterNonNegative(reference, v);
        }

        return n - 1 - InverseRecenterNonNegative(n - 1 - reference, v);
    }
}
