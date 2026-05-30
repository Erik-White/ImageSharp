// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

/// <summary>
/// 8×8 block coordinate within a CDEF unit, in 8×8 cells (not pixels). For a 64×64 unit
/// each axis ranges 0..7; for the 128×128 superblock case (when CDEF is invoked on the
/// full SB rather than per 64×64 sub-unit) each axis ranges 0..15.
/// </summary>
internal readonly record struct Av1CdefCellPosition(byte BlockX, byte BlockY)
{
    private const int CellSizeLog2 = 3;

    /// <summary>
    /// Gets the pixel x-offset of this cell's top-left corner within the CDEF unit.
    /// </summary>
    public int PixelX => this.BlockX << CellSizeLog2;

    /// <summary>
    /// Gets the pixel y-offset of this cell's top-left corner within the CDEF unit.
    /// </summary>
    public int PixelY => this.BlockY << CellSizeLog2;

    /// <summary>
    /// Computes this cell's offset into the bordered working buffer described by
    /// <paramref name="stride"/>, accounting for the (HBorder, VBorder) padding origin.
    /// </summary>
    public int WorkingBufferOffset(int stride)
        => ((Av1CdefConstants.VerticalBorder + this.PixelY) * stride) + Av1CdefConstants.HorizontalBorder + this.PixelX;
}
