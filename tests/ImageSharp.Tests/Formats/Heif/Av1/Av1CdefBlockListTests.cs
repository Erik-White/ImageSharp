// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1CdefBlockListTests
{
    private const int CellsPerAxis = 8; // a 64x64 unit is 8x8 cells of 8x8 pixels

    /// <summary>
    /// Every 8×8 cell in the unit has at least one non-skip 4×4, so the list contains all
    /// 64 cells in raster order with cell coordinates 0..7 on each axis.
    /// </summary>
    [Fact]
    public void Build_AllCellsNonSkip_ListsEveryCellInRasterOrder()
    {
        Av1FrameInfo frameInfo = BuildFullUnit(skip: _ => false);
        Span<Av1CdefCellPosition> dlist = stackalloc Av1CdefCellPosition[64];

        int count = Av1CdefBlockList.Build(frameInfo, 0, 0, 16, 16, Av1BlockSize.Block64x64, dlist);

        Assert.Equal(64, count);
        int i = 0;
        for (byte by = 0; by < CellsPerAxis; by++)
        {
            for (byte bx = 0; bx < CellsPerAxis; bx++)
            {
                Assert.Equal(new Av1CdefCellPosition(bx, by), dlist[i++]);
            }
        }
    }

    /// <summary>
    /// When every 4×4 in the unit is skip, no cell needs filtering and the list is empty.
    /// </summary>
    [Fact]
    public void Build_AllCellsSkip_ListsNothing()
    {
        Av1FrameInfo frameInfo = BuildFullUnit(skip: _ => true);
        Span<Av1CdefCellPosition> dlist = stackalloc Av1CdefCellPosition[64];

        int count = Av1CdefBlockList.Build(frameInfo, 0, 0, 16, 16, Av1BlockSize.Block64x64, dlist);

        Assert.Equal(0, count);
    }

    /// <summary>
    /// A single non-skip 4×4 makes its enclosing 8×8 cell appear in the list; the other 63
    /// cells (all-skip) are omitted. Verifies the per-cell 2×2 skip reduction and the
    /// cell coordinate mapping (mi (4, 6) → cell (2, 3)).
    /// </summary>
    [Fact]
    public void Build_SingleNonSkipBlock_ListsOnlyThatCell()
    {
        const int nonSkipMiX = 4; // cell column 2
        const int nonSkipMiY = 6; // cell row 3
        Av1FrameInfo frameInfo = BuildFullUnit(skip: p => !(p.X == nonSkipMiX && p.Y == nonSkipMiY));
        Span<Av1CdefCellPosition> dlist = stackalloc Av1CdefCellPosition[64];

        int count = Av1CdefBlockList.Build(frameInfo, 0, 0, 16, 16, Av1BlockSize.Block64x64, dlist);

        Assert.Equal(1, count);
        Assert.Equal(new Av1CdefCellPosition(2, 3), dlist[0]);
    }

    /// <summary>
    /// A unit clipped by the frame edge (frame is only 12×8 mode-info units, i.e. 48×32 px)
    /// only walks the in-frame cells: <c>Min(frameMi - mi, 16)</c> bounds the loop, so the
    /// list covers a 6×4 grid of cells, not the full 8×8.
    /// </summary>
    [Fact]
    public void Build_ClippedUnit_StopsAtFrameExtent()
    {
        const int frameMiCols = 12; // 6 cells wide
        const int frameMiRows = 8;  // 4 cells tall
        Av1FrameInfo frameInfo = BuildFullUnit(skip: _ => false);
        Span<Av1CdefCellPosition> dlist = stackalloc Av1CdefCellPosition[64];

        int count = Av1CdefBlockList.Build(frameInfo, 0, 0, frameMiRows, frameMiCols, Av1BlockSize.Block64x64, dlist);

        Assert.Equal(6 * 4, count);
        Assert.All(dlist[..count].ToArray(), cell => Assert.True(cell.BlockX < 6 && cell.BlockY < 4));
    }

    // Builds an Av1FrameInfo whose first 64x64 superblock is fully tiled with 4x4 mode-info
    // blocks, each block's Skip flag chosen by the predicate keyed on its (miX, miY) position.
    private static Av1FrameInfo BuildFullUnit(Func<Point, bool> skip)
    {
        ObuSequenceHeader header = new()
        {
            MaxFrameWidth = 64,
            MaxFrameHeight = 64,
            Use128x128Superblock = false,
            ColorConfig = new ObuColorConfig { IsMonochrome = false, SubSamplingX = true, SubSamplingY = true },
        };

        Av1FrameInfo frameInfo = new(header);
        Av1SuperblockInfo superblock = frameInfo.GetSuperblock(new Point(0, 0));

        // 64x64 SB = 16x16 mode-info units of 4x4 pixels.
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                Av1BlockModeInfo modeInfo = new(Av1BlockSize.Block4x4, new Point(x, y))
                {
                    Skip = skip(new Point(x, y)),
                };
                frameInfo.UpdateModeInfo(modeInfo, superblock);
            }
        }

        return frameInfo;
    }
}
