// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1CdefCellPositionTests
{
    /// <summary>
    /// Cells are addressed in 8×8 units, so the pixel offset within the CDEF unit is the
    /// cell coordinate times the 8-pixel cell size.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0, 8, 0)]
    [InlineData(0, 1, 0, 8)]
    [InlineData(3, 2, 24, 16)]
    [InlineData(7, 7, 56, 56)]
    public void PixelOffsets_AreCellTimesEight(byte blockX, byte blockY, int expectedPixelX, int expectedPixelY)
    {
        Av1CdefCellPosition cell = new(blockX, blockY);

        Assert.Equal(expectedPixelX, cell.PixelX);
        Assert.Equal(expectedPixelY, cell.PixelY);
    }

    /// <summary>
    /// The working-buffer offset places the cell at its pixel coordinate shifted past the
    /// (HorizontalBorder, VerticalBorder) padding origin:
    /// <c>(VBorder + pixelY) * stride + HBorder + pixelX</c>. This is the single formula
    /// <see cref="Av1CdefUnitDriver"/> uses for both the direction search and the filter input.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(3, 2)]
    [InlineData(7, 7)]
    public void WorkingBufferOffset_AccountsForBorderOrigin(byte blockX, byte blockY)
    {
        const int stride = Av1CdefConstants.BufferStride;
        Av1CdefCellPosition cell = new(blockX, blockY);

        int expected = ((Av1CdefConstants.VerticalBorder + cell.PixelY) * stride)
            + Av1CdefConstants.HorizontalBorder + cell.PixelX;

        Assert.Equal(expected, cell.WorkingBufferOffset(stride));
    }

    /// <summary>
    /// The cell at the unit origin still sits one full border in on each axis, so its offset is never zero.
    /// </summary>
    [Fact]
    public void WorkingBufferOffset_Origin_IncludesBorder()
    {
        Av1CdefCellPosition origin = new(0, 0);
        int expected = (Av1CdefConstants.VerticalBorder * Av1CdefConstants.BufferStride)
            + Av1CdefConstants.HorizontalBorder;

        Assert.Equal(expected, origin.WorkingBufferOffset(Av1CdefConstants.BufferStride));
    }
}
