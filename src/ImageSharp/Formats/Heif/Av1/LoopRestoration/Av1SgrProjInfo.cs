// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.LoopRestoration;

/// <summary>
/// Self-guided projection filter parameters for one restoration unit. <c>Ep</c>
/// indexes <see cref="Av1RestorationConstants.SgrParams"/> and selects the (r0, r1,
/// e0, e1) tuple. <c>Xqd</c> holds the two recentered projection coefficients.
/// </summary>
internal sealed class Av1SgrProjInfo
{
    public int Ep { get; set; }

    public int[] Xqd { get; } = new int[2];

    public void CopyFrom(Av1SgrProjInfo other)
    {
        this.Ep = other.Ep;
        this.Xqd[0] = other.Xqd[0];
        this.Xqd[1] = other.Xqd[1];
    }
}
