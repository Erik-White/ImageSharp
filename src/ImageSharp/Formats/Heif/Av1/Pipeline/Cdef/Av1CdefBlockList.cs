// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

/// <summary>
/// Builds the list of 8×8 blocks within a 64×64 CDEF unit that need filtering. Skip-only blocks
/// (every mode-info inside has <c>skip == true</c>) contribute no residual to the reconstruction.
/// </summary>
internal static class Av1CdefBlockList
{
    /// <summary>
    /// Populates <paramref name="dlist"/> with the (bx, by) coordinates of every 8×8 cell
    /// inside the unit at <paramref name="miRow"/>/<paramref name="miColumn"/> whose
    /// mode-info has <c>skip == false</c> for at least one of its 4×4 sub-blocks. Returns
    /// the number of entries written. <paramref name="dlist"/> must have at least
    /// <c>(unitSize/8) * (unitSize/8)</c> slots.
    /// </summary>
    public static int Build(
        Av1FrameInfo frameInfo,
        int miRow,
        int miColumn,
        int frameMiRows,
        int frameMiColumns,
        Av1BlockSize unitSize,
        Span<Av1CdefCellPosition> dlist)
    {
        int wideMi = unitSize.Get4x4WideCount();
        int highMi = unitSize.Get4x4HighCount();
        int maxColumns = Math.Min(frameMiColumns - miColumn, wideMi);
        int maxRows = Math.Min(frameMiRows - miRow, highMi);

        int count = 0;
        for (int r = 0; r < maxRows; r += 2)
        {
            for (int c = 0; c < maxColumns; c += 2)
            {
                if (!Is8x8BlockSkip(frameInfo, miRow + r, miColumn + c))
                {
                    dlist[count] = new Av1CdefCellPosition((byte)(c >> 1), (byte)(r >> 1));
                    count++;
                }
            }
        }

        return count;
    }

    private static bool Is8x8BlockSkip(Av1FrameInfo frameInfo, int miRow, int miColumn)
    {
        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 2; c++)
            {
                Av1BlockModeInfo? info = frameInfo.GetModeInfoAtMiPosition(new Point(miColumn + c, miRow + r));
                if (info is not null && !info.Skip)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
