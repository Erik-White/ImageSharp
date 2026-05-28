// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// Spec 6.10.25 (<c>is_mv_valid</c>) — the IBC subset. Verifies that an
/// intra-block-copy displacement vector is integer-pel, that the source
/// rectangle lies inside the current tile, and that the source respects the
/// reconstruction-pipeline delay and the wavefront constraint. Throws
/// <see cref="InvalidImageContentException"/> on any failure, mirroring the
/// spec's "return 0" rejection.
/// </summary>
internal static class Av1IntraBlockCopyValidator
{
    private const int ScalePixelToMv = 8;

    public static void Validate(
        Av1MotionVector dv,
        Av1TileInfo tileInfo,
        int superblockModeInfoSizeLog2,
        int modeInfoRow,
        int modeInfoColumn,
        Av1BlockSize blockSize,
        bool hasChroma,
        bool subsamplingX,
        bool subsamplingY)
    {
        // 6.10.25: (Mv[0][0] & 7) || (Mv[0][1] & 7) ⇒ return 0. force_integer_mv masks fractional
        // bits at decode, but a corrupt stream could still set them via a malformed predictor sum.
        if (((dv.Row | dv.Col) & (ScalePixelToMv - 1)) != 0)
        {
            throw new InvalidImageContentException("IBC displacement vector is not integer-pel.");
        }

        int blockWidth = blockSize.GetWidth();
        int blockHeight = blockSize.GetHeight();
        int blockTopEdge = (modeInfoRow << Av1Constants.ModeInfoSizeLog2) * ScalePixelToMv;
        int blockLeftEdge = (modeInfoColumn << Av1Constants.ModeInfoSizeLog2) * ScalePixelToMv;
        int srcTopEdge = blockTopEdge + dv.Row;
        int srcLeftEdge = blockLeftEdge + dv.Col;
        int srcBottomEdge = srcTopEdge + (blockHeight * ScalePixelToMv);
        int srcRightEdge = srcLeftEdge + (blockWidth * ScalePixelToMv);

        int tileTopEdge = (tileInfo.ModeInfoRowStart << Av1Constants.ModeInfoSizeLog2) * ScalePixelToMv;
        int tileLeftEdge = (tileInfo.ModeInfoColumnStart << Av1Constants.ModeInfoSizeLog2) * ScalePixelToMv;
        int tileBottomEdge = (tileInfo.ModeInfoRowEnd << Av1Constants.ModeInfoSizeLog2) * ScalePixelToMv;
        int tileRightEdge = (tileInfo.ModeInfoColumnEnd << Av1Constants.ModeInfoSizeLog2) * ScalePixelToMv;

        if (srcTopEdge < tileTopEdge || srcLeftEdge < tileLeftEdge ||
            srcBottomEdge > tileBottomEdge || srcRightEdge > tileRightEdge)
        {
            throw new InvalidImageContentException(
                $"IBC source region escapes the current tile. dv=({dv.Row},{dv.Col}) mi=({modeInfoRow},{modeInfoColumn}) bsize={blockSize} blockTL=({blockTopEdge},{blockLeftEdge}) src=({srcTopEdge}..{srcBottomEdge},{srcLeftEdge}..{srcRightEdge}) tile=({tileTopEdge}..{tileBottomEdge},{tileLeftEdge}..{tileRightEdge})");
        }

        // 6.10.25: under sub-8 chroma the spec subtracts 4 pixels from srcLeftEdge / srcTopEdge
        // before the tile-bounds test. Equivalently, require an extra 4-pixel margin from the
        // tile edge. (The +32 below is 4 pixels expressed in 1/8-pel units.)
        if (hasChroma)
        {
            if (blockWidth < 8 && subsamplingX && srcLeftEdge < tileLeftEdge + (4 * ScalePixelToMv))
            {
                throw new InvalidImageContentException("IBC sub-8 chroma source crosses tile left edge.");
            }

            if (blockHeight < 8 && subsamplingY && srcTopEdge < tileTopEdge + (4 * ScalePixelToMv))
            {
                throw new InvalidImageContentException("IBC sub-8 chroma source crosses tile top edge.");
            }
        }

        // 6.10.25: srcSb64 / activeSb64 reconstruction-delay test. The source's bottom-right
        // must lie at least INTRABC_DELAY_SB64 SB64s before the active block in raster order.
        int superblockMib = 1 << superblockModeInfoSizeLog2;
        int superblockSizePixels = superblockMib << Av1Constants.ModeInfoSizeLog2;
        int activeSbRow = modeInfoRow >> superblockModeInfoSizeLog2;
        int activeSb64Col = (modeInfoColumn << Av1Constants.ModeInfoSizeLog2) >> 6;
        int srcSbRow = ((srcBottomEdge >> 3) - 1) / superblockSizePixels;
        int srcSb64Col = ((srcRightEdge >> 3) - 1) >> 6;
        int totalSb64PerRow = ((tileInfo.ModeInfoColumnEnd - tileInfo.ModeInfoColumnStart - 1) >> 4) + 1;
        int activeSb64 = (activeSbRow * totalSb64PerRow) + activeSb64Col;
        int srcSb64 = (srcSbRow * totalSb64PerRow) + srcSb64Col;
        if (srcSb64 >= activeSb64 - Av1MotionVectorConstants.IntraBlockCopyDelaySuperblocks64)
        {
            throw new InvalidImageContentException("IBC source overlaps the in-flight reconstruction window.");
        }

        // 6.10.25: wavefront constraint. gradient = 1 + INTRABC_DELAY_SB64 + use_128x128_superblock.
        int gradient = 1 + Av1MotionVectorConstants.IntraBlockCopyDelaySuperblocks64 + (superblockSizePixels > 64 ? 1 : 0);
        int wavefrontOffset = gradient * (activeSbRow - srcSbRow);
        if (srcSbRow > activeSbRow ||
            srcSb64Col >= activeSb64Col - Av1MotionVectorConstants.IntraBlockCopyDelaySuperblocks64 + wavefrontOffset)
        {
            throw new InvalidImageContentException("IBC source violates the wavefront constraint.");
        }
    }
}
