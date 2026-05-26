// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.Palette;

/// <summary>
/// 5.11.49 block extents for palette token decoding. <c>Width</c> and <c>Height</c>
/// are the block plane dimensions; <c>Rows</c> and <c>Cols</c> are the on-screen
/// dimensions clipped to the frame edge. For chroma planes whose dimensions fall
/// below 4 samples, the spec inflates them by 2 so the wavefront still has room
/// to traverse a usable diagonal.
/// </summary>
internal readonly record struct PaletteBlockExtents(int Width, int Height, int Rows, int Cols)
{
    public static PaletteBlockExtents For(
        Av1BlockSize blockSize, Av1PartitionInfo partitionInfo, int plane, bool subX, bool subY)
    {
        int blockHeight = blockSize.GetHeight();
        int blockWidth = blockSize.GetWidth();
        int onscreenRows = partitionInfo.ModeBlockToBottomEdge >= 0 ? blockHeight : (partitionInfo.ModeBlockToBottomEdge >> 3) + blockHeight;
        int onscreenCols = partitionInfo.ModeBlockToRightEdge >= 0 ? blockWidth : (partitionInfo.ModeBlockToRightEdge >> 3) + blockWidth;
        int subShiftX = subX ? 1 : 0;
        int subShiftY = subY ? 1 : 0;
        int planeBlockWidth = blockWidth >> subShiftX;
        int planeBlockHeight = blockHeight >> subShiftY;

        // 5.11.49: if the chroma block falls below 4 samples in either dimension,
        // pad both the block and onscreen extents by 2 in that dimension.
        int chromaPadX = plane > 0 && planeBlockWidth < 4 ? 2 : 0;
        int chromaPadY = plane > 0 && planeBlockHeight < 4 ? 2 : 0;

        return new PaletteBlockExtents(
            Width: planeBlockWidth + chromaPadX,
            Height: planeBlockHeight + chromaPadY,
            Rows: (onscreenRows >> subShiftY) + chromaPadY,
            Cols: (onscreenCols >> subShiftX) + chromaPadX);
    }
}
