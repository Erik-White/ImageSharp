// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.LoopRestoration;

/// <summary>
/// Wiener filter coefficients for one restoration unit. The filters are 7-tap
/// separable; only taps 0..2 are signalled, the centre tap (index 3) is implicit
/// and the right/bottom halves mirror the left/top.
/// </summary>
internal sealed class Av1WienerInfo
{
    public Av1WienerInfo()
    {
        this.VerticalFilter = new int[Av1RestorationConstants.WienerWin];
        this.HorizontalFilter = new int[Av1RestorationConstants.WienerWin];
    }

    public int[] VerticalFilter { get; }

    public int[] HorizontalFilter { get; }

    public void CopyFrom(Av1WienerInfo other)
    {
        Array.Copy(other.VerticalFilter, this.VerticalFilter, this.VerticalFilter.Length);
        Array.Copy(other.HorizontalFilter, this.HorizontalFilter, this.HorizontalFilter.Length);
    }
}
