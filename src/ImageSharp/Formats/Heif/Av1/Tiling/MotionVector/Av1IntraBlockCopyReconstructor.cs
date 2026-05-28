// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// IBC reconstruction: copies a rectangle from already-decoded pixels of the
/// same plane in the current frame, replacing the intra-prediction step. The
/// residual-add (inverse transform) runs on top of this output unchanged.
/// </summary>
internal static class Av1IntraBlockCopyReconstructor
{
    /// <summary>
    /// Copies a <paramref name="width"/>×<paramref name="height"/> rectangle from
    /// <c>(srcX, srcY)</c> to <c>(dstX, dstY)</c> on a single plane buffer. The two
    /// regions must not overlap on rows that have already been written by this call:
    /// IBC's wavefront constraint guarantees the source is strictly above-left of
    /// the destination, so a top-down forward copy is safe.
    /// </summary>
    public static void Copy(
        Buffer2D<byte> plane,
        int srcX,
        int srcY,
        int dstX,
        int dstY,
        int width,
        int height)
    {
        for (int row = 0; row < height; row++)
        {
            Span<byte> srcRow = plane.DangerousGetRowSpan(srcY + row).Slice(srcX, width);
            Span<byte> dstRow = plane.DangerousGetRowSpan(dstY + row).Slice(dstX, width);
            srcRow.CopyTo(dstRow);
        }
    }
}
