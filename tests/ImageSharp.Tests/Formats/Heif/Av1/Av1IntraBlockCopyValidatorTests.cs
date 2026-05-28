// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1IntraBlockCopyValidatorTests
{
    private const int SuperblockMib64Log2 = 4;
    private const int SuperblockMib128Log2 = 5;
    private const int SuperblockMib64 = 1 << SuperblockMib64Log2;
    private const int SuperblockMib128 = 1 << SuperblockMib128Log2;

    private const string SubpelMessage = "not integer-pel";
    private const string TileEscapeMessage = "escapes the current tile";
    private const string SubChromaLeftMessage = "sub-8 chroma source crosses tile left edge";
    private const string SubChromaTopMessage = "sub-8 chroma source crosses tile top edge";
    private const string ReconstructionWindowMessage = "in-flight reconstruction window";
    private const string WavefrontMessage = "wavefront constraint";

    private static Av1TileInfo MakeTile(int rowEndMi = 1024, int colEndMi = 1024, int rowStartMi = 0, int colStartMi = 0)
        => new(rowStartMi, rowEndMi, colStartMi, colEndMi);

    private static InvalidImageContentException AssertThrows(Action validate)
        => Assert.Throws<InvalidImageContentException>(validate);

    [Fact]
    public void SubpelRowThrows()
    {
        Av1MotionVector dv = new(1, 0);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block8x8,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false));
        Assert.Contains(SubpelMessage, ex.Message);
    }

    [Fact]
    public void SubpelColThrows()
    {
        Av1MotionVector dv = new(0, 1);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block8x8,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false));
        Assert.Contains(SubpelMessage, ex.Message);
    }

    [Fact]
    public void SourceAboveTileTopThrows()
    {
        Av1MotionVector dv = new(-2568, 0);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block8x8,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false));
        Assert.Contains(TileEscapeMessage, ex.Message);
    }

    [Fact]
    public void SourceLeftOfTileLeftThrows()
    {
        Av1MotionVector dv = new(0, -2568);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block8x8,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false));
        Assert.Contains(TileEscapeMessage, ex.Message);
    }

    [Fact]
    public void SourceBelowTileBottomThrows()
    {
        Av1MotionVector dv = new(1224, 0);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(rowEndMi: SuperblockMib64 * 5),
            SuperblockMib64Log2,
            modeInfoRow: (SuperblockMib64 * 2) + 8,
            modeInfoColumn: 0,
            Av1BlockSize.Block8x8,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false));
        Assert.Contains(TileEscapeMessage, ex.Message);
    }

    [Fact]
    public void SourceRightOfTileRightThrows()
    {
        Av1MotionVector dv = new(0, 1224);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(colEndMi: SuperblockMib64 * 5),
            SuperblockMib64Log2,
            modeInfoRow: 0,
            modeInfoColumn: (SuperblockMib64 * 2) + 8,
            Av1BlockSize.Block8x8,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false));
        Assert.Contains(TileEscapeMessage, ex.Message);
    }

    [Fact]
    public void SubEightChromaLeftMarginThrows()
    {
        Av1MotionVector dv = new(0, -2536);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block4x4,
            hasChroma: true,
            subsamplingX: true,
            subsamplingY: false));
        Assert.Contains(SubChromaLeftMessage, ex.Message);
    }

    [Fact]
    public void SubEightChromaTopMarginThrows()
    {
        Av1MotionVector dv = new(-2536, 0);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block4x4,
            hasChroma: true,
            subsamplingX: false,
            subsamplingY: true));
        Assert.Contains(SubChromaTopMessage, ex.Message);
    }

    [Fact]
    public void SourceInsideReconstructionDelayWindowThrows()
    {
        Av1MotionVector dv = new(0, -32);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block4x4,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false));
        Assert.Contains(ReconstructionWindowMessage, ex.Message);
    }

    /// <summary>
    /// Active block at SB(5,5) referencing a source one SB64 row up but in column 6
    /// of that row — the wavefront's gradient permits at most column 6 from row 4
    /// (= activeSbCol - delay + 5 * 1 row). This DV lands exactly on the boundary
    /// and is rejected. Chosen carefully because the recon-window branch fires first
    /// for any source with srcSbRow > activeSbRow.
    /// </summary>
    [Fact]
    public void SourceViolatesWavefrontGradientThrows()
    {
        Av1MotionVector dv = new(-32, 992);
        InvalidImageContentException ex = AssertThrows(() => Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block4x4,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false));
        Assert.Contains(WavefrontMessage, ex.Message);
    }

    [Fact]
    public void SourceFiveSuperblock64ColumnsBackPasses()
    {
        Av1MotionVector dv = new(0, -2080);
        Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib64Log2,
            modeInfoRow: SuperblockMib64 * 5,
            modeInfoColumn: SuperblockMib64 * 5,
            Av1BlockSize.Block4x4,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false);
    }

    [Fact]
    public void Sb128WavefrontWithExtraRowGradientPasses()
    {
        Av1MotionVector dv = new(-32, 520);
        Av1IntraBlockCopyValidator.Validate(
            dv,
            MakeTile(),
            SuperblockMib128Log2,
            modeInfoRow: SuperblockMib128 * 5,
            modeInfoColumn: SuperblockMib128 * 5,
            Av1BlockSize.Block4x4,
            hasChroma: false,
            subsamplingX: false,
            subsamplingY: false);
    }
}
