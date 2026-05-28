// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1IntraBlockCopyMotionVectorStackTests
{
    private const int SuperblockMib64 = 16;
    private const int SuperblockMib128 = 32;

    private static Av1TileInfo MakeTile(int rowEndMi = 1024, int colEndMi = 1024, int rowStartMi = 0, int colStartMi = 0)
        => new(rowStartMi, rowEndMi, colStartMi, colEndMi);

    [Fact]
    public void NoNeighborsAtTileTopFallsBackToLeftDirection()
    {
        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: _ => null,
            tileInfo: MakeTile(),
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: 0,
            modeInfoColumn: 0,
            blockWidthMi: 2,
            blockHeightMi: 2);

        const int superblockPixels = SuperblockMib64 << Av1Constants.ModeInfoSizeLog2;
        int expectedCol = -(superblockPixels + Av1MotionVectorConstants.IntraBlockCopyDelayPixels) << 3;
        Assert.Equal(0, predictor.Row);
        Assert.Equal((short)expectedCol, predictor.Col);
    }

    [Fact]
    public void NoNeighborsBelowTileTopFallsBackToUpDirection()
    {
        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: _ => null,
            tileInfo: MakeTile(),
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: SuperblockMib64,
            modeInfoColumn: 0,
            blockWidthMi: 2,
            blockHeightMi: 2);

        const int superblockPixels = SuperblockMib64 << Av1Constants.ModeInfoSizeLog2;
        int expectedRow = -superblockPixels << 3;
        Assert.Equal((short)expectedRow, predictor.Row);
        Assert.Equal(0, predictor.Col);
    }

    [Fact]
    public void Fallback128SbScalesWithSuperblockSize()
    {
        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: _ => null,
            tileInfo: MakeTile(),
            superblockModeInfoSize: SuperblockMib128,
            modeInfoRow: SuperblockMib128,
            modeInfoColumn: 0,
            blockWidthMi: 2,
            blockHeightMi: 2);

        const int superblockPixels = SuperblockMib128 << Av1Constants.ModeInfoSizeLog2;
        int expectedRow = -superblockPixels << 3;
        Assert.Equal((short)expectedRow, predictor.Row);
        Assert.Equal(0, predictor.Col);
    }

    [Fact]
    public void AboveIbcNeighborBeatsFallback()
    {
        // Place an IBC block immediately above the current 8x8 block.
        const int row = SuperblockMib64;
        const int col = 0;
        Av1BlockModeInfo above = MakeIbcNeighbor(new Av1MotionVector(-128, 256));
        Av1IntraBlockCopyMotionVectorStack.MiLookup lookup = pos =>
            (pos.Y == row - 1 && pos.X >= col && pos.X < col + 2) ? above : null;

        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: lookup,
            tileInfo: MakeTile(),
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: row,
            modeInfoColumn: col,
            blockWidthMi: 2,
            blockHeightMi: 2);

        Assert.Equal(new Av1MotionVector(-128, 256), predictor);
    }

    [Fact]
    public void LeftIbcNeighborBeatsFallbackWhenAboveIsAbsent()
    {
        const int row = SuperblockMib64;
        const int col = 4;
        Av1BlockModeInfo left = MakeIbcNeighbor(new Av1MotionVector(0, -64));
        Av1IntraBlockCopyMotionVectorStack.MiLookup lookup = pos =>
            (pos.X == col - 1 && pos.Y >= row && pos.Y < row + 2) ? left : null;

        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: lookup,
            tileInfo: MakeTile(),
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: row,
            modeInfoColumn: col,
            blockWidthMi: 2,
            blockHeightMi: 2);

        Assert.Equal(new Av1MotionVector(0, -64), predictor);
    }

    [Fact]
    public void NonIbcNeighborsAreIgnoredInFavorOfFallback()
    {
        const int row = SuperblockMib64;
        const int col = 0;
        Av1BlockModeInfo above = MakeNonIbcNeighbor();
        Av1BlockModeInfo left = MakeNonIbcNeighbor();
        Av1IntraBlockCopyMotionVectorStack.MiLookup lookup = pos =>
        {
            if (pos.Y == row - 1 && pos.X >= col && pos.X < col + 2)
            {
                return above;
            }

            if (pos.X == col - 1 && pos.Y >= row && pos.Y < row + 2)
            {
                return left;
            }

            return null;
        };

        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: lookup,
            tileInfo: MakeTile(),
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: row,
            modeInfoColumn: col,
            blockWidthMi: 2,
            blockHeightMi: 2);

        const int superblockPixels = SuperblockMib64 << Av1Constants.ModeInfoSizeLog2;
        int expectedRow = -superblockPixels << 3;
        Assert.Equal((short)expectedRow, predictor.Row);
        Assert.Equal(0, predictor.Col);
    }

    [Fact]
    public void TileTopOffsetMakesAtBoundaryGoLeftAndAboveBoundaryGoUp()
    {
        const int tileTop = SuperblockMib64 * 2;
        Av1TileInfo tile = MakeTile(rowStartMi: tileTop);

        Av1MotionVector atBoundary = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: _ => null,
            tileInfo: tile,
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: tileTop,
            modeInfoColumn: 0,
            blockWidthMi: 2,
            blockHeightMi: 2);

        Av1MotionVector aboveBoundary = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: _ => null,
            tileInfo: tile,
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: tileTop + SuperblockMib64,
            modeInfoColumn: 0,
            blockWidthMi: 2,
            blockHeightMi: 2);

        Assert.Equal(0, atBoundary.Row);
        Assert.NotEqual(0, atBoundary.Col);
        Assert.NotEqual(0, aboveBoundary.Row);
        Assert.Equal(0, aboveBoundary.Col);
    }

    [Fact]
    public void ZeroDvIbcNeighborsStillTriggerSynthesizedFallback()
    {
        // libaom's av1_find_ref_dv only fires when the chosen predictor (nearest, falling
        // back to near) is zero. Neighbors that *are* IBC blocks but carry a zero DV must
        // not suppress the synthesized fallback — exercises the `chosen.IsZero` branch in
        // BuildPredictor with stack entries actually present.
        const int row = SuperblockMib64;
        const int col = 0;
        Av1BlockModeInfo zeroDv = MakeIbcNeighbor(default);
        Av1IntraBlockCopyMotionVectorStack.MiLookup lookup = pos =>
            (pos.Y == row - 1 && pos.X >= col && pos.X < col + 2) ? zeroDv : null;

        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: lookup,
            tileInfo: MakeTile(),
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: row,
            modeInfoColumn: col,
            blockWidthMi: 2,
            blockHeightMi: 2);

        const int superblockPixels = SuperblockMib64 << Av1Constants.ModeInfoSizeLog2;
        int expectedRow = -superblockPixels << 3;
        Assert.Equal((short)expectedRow, predictor.Row);
        Assert.Equal(0, predictor.Col);
    }

    [Fact]
    public void NearMvPromotedWhenNearestIsZero()
    {
        // When nearestmv is zero but nearmv is non-zero, the predictor should pick near
        // and skip the synthesized fallback. Two IBC neighbors with distinct DVs (one
        // zero, one not) cause the stack to expose both candidates; the non-zero one
        // wins via the nearest.IsZero ? near : nearest selection.
        const int row = SuperblockMib64;
        const int col = 4;
        Av1BlockModeInfo aboveZero = MakeIbcNeighbor(default);
        Av1BlockModeInfo leftNonZero = MakeIbcNeighbor(new Av1MotionVector(-256, 128));
        Av1IntraBlockCopyMotionVectorStack.MiLookup lookup = pos =>
        {
            if (pos.Y == row - 1 && pos.X >= col && pos.X < col + 2)
            {
                return aboveZero;
            }

            if (pos.X == col - 1 && pos.Y >= row && pos.Y < row + 2)
            {
                return leftNonZero;
            }

            return null;
        };

        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi: lookup,
            tileInfo: MakeTile(),
            superblockModeInfoSize: SuperblockMib64,
            modeInfoRow: row,
            modeInfoColumn: col,
            blockWidthMi: 2,
            blockHeightMi: 2);

        Assert.Equal(new Av1MotionVector(-256, 128), predictor);
    }

    private static Av1BlockModeInfo MakeIbcNeighbor(Av1MotionVector dv)
        => new(Av1BlockSize.Block8x8, default)
        {
            UseIntraBlockCopy = true,
            DisplacementVector = dv,
        };

    private static Av1BlockModeInfo MakeNonIbcNeighbor()
        => new(Av1BlockSize.Block8x8, default)
        {
            UseIntraBlockCopy = false,
        };
}
