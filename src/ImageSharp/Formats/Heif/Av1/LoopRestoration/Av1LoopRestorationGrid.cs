// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.LoopRestoration;

/// <summary>
/// Frame-global per-plane grid of parsed loop-restoration units. The parser fills one
/// <see cref="Av1RestorationUnitInfo"/> per (unitRow, unitCol) cell; the apply step
/// (spec 7.17) reads <c>LrType</c> / <c>LrWiener</c> / <c>LrSgrSet</c> back out by unit index.
/// </summary>
internal sealed class Av1LoopRestorationGrid
{
    private readonly Av1RestorationUnitInfo[][] units;
    private readonly int[] horizontalUnitCounts;

    public Av1LoopRestorationGrid(int[] horizontalUnitCounts, int[] verticalUnitCounts)
    {
        this.horizontalUnitCounts = horizontalUnitCounts;
        this.units = new Av1RestorationUnitInfo[3][];
        for (int plane = 0; plane < 3; plane++)
        {
            int count = horizontalUnitCounts[plane] * verticalUnitCounts[plane];
            this.units[plane] = new Av1RestorationUnitInfo[count];
            for (int i = 0; i < count; i++)
            {
                this.units[plane][i] = new Av1RestorationUnitInfo();
            }
        }
    }

    public Av1RestorationUnitInfo GetUnit(int plane, int unitRow, int unitColumn)
        => this.units[plane][(unitRow * this.horizontalUnitCounts[plane]) + unitColumn];
}
