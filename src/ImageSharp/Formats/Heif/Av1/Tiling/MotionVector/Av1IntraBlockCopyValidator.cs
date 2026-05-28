// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// Mirrors libaom's <c>av1_is_dv_valid</c> (mvref_common.h). Verifies that an
/// intra-block-copy displacement vector references pixels that are inside the
/// current tile, are integer-pel only, lie within an already-decoded
/// superblock, and respect the 256-pixel reconstruction-pipeline delay and
/// the wavefront constraint. Throws <see cref="InvalidImageContentException"/>
/// when any check fails — that is the same outcome libaom signals via its
/// "return 0" rejection of the bitstream.
/// </summary>
internal static class Av1IntraBlockCopyValidator
{
    private const int ScalePixelToMv = 8;
    private const int IntraBlockCopyDelaySuperblocks64 = Av1MotionVectorConstants.IntraBlockCopyDelaySuperblocks64;

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
        // Spec requires integer-pel for IBC; force_integer_mv masks fractional bits at decode,
        // but corrupt streams could still set them via a malformed predictor sum.
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

        // Sub-8x8 chroma blocks must keep at least 4 pixels of margin from the tile edge so
        // that the subsampled chroma read does not cross the tile boundary.
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

        // Wavefront + reconstruction-delay check, in 64-pixel units. The source's bottom-right
        // must lie inside an already-decoded SB64, accounting for a 4-SB64 reconstruction
        // delay and (for 128x128 SBs) an extra-row gradient offset.
        int superblockMib = 1 << superblockModeInfoSizeLog2;
        int superblockSizePixels = superblockMib << Av1Constants.ModeInfoSizeLog2;
        int activeSbRow = modeInfoRow >> superblockModeInfoSizeLog2;
        int activeSb64Col = (modeInfoColumn << Av1Constants.ModeInfoSizeLog2) >> 6;
        int srcSbRow = ((srcBottomEdge >> 3) - 1) / superblockSizePixels;
        int srcSb64Col = ((srcRightEdge >> 3) - 1) >> 6;
        int totalSb64PerRow = ((tileInfo.ModeInfoColumnEnd - tileInfo.ModeInfoColumnStart - 1) >> 4) + 1;
        int activeSb64 = (activeSbRow * totalSb64PerRow) + activeSb64Col;
        int srcSb64 = (srcSbRow * totalSb64PerRow) + srcSb64Col;
        if (srcSb64 >= activeSb64 - IntraBlockCopyDelaySuperblocks64)
        {
            throw new InvalidImageContentException("IBC source overlaps the in-flight reconstruction window.");
        }

        int gradient = 1 + IntraBlockCopyDelaySuperblocks64 + (superblockSizePixels > 64 ? 1 : 0);
        int wavefrontOffset = gradient * (activeSbRow - srcSbRow);
        if (srcSbRow > activeSbRow ||
            srcSb64Col >= activeSb64Col - IntraBlockCopyDelaySuperblocks64 + wavefrontOffset)
        {
            throw new InvalidImageContentException("IBC source violates the wavefront constraint.");
        }
    }
}
