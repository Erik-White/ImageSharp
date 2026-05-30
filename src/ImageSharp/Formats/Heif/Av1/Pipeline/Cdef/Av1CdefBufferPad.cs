// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

/// <summary>
/// Builds the padded 16-bit working buffer the CDEF inner filter reads from. The filter
/// taps reach up to ±2 rows / ±2 columns from each output pixel (spec 7.15.2.1), so the
/// driver assembles a (unitWidth + 2*HBorder) × (unitHeight + 2*VBorder) buffer that
/// includes the neighbouring frame samples and stamps off-frame regions with <see cref="Av1CdefConstants.VeryLarge"/>.
/// </summary>
internal static class Av1CdefBufferPad
{
    /// <summary>
    /// Copies the unitWidth × unitHeight pixels of the CDEF unit at
    /// (<paramref name="unitOriginX"/>, <paramref name="unitOriginY"/>) from
    /// <paramref name="source"/> into <paramref name="buffer"/>, surrounded by the spec's
    /// neighbour pixels (or the off-frame sentinel when on a frame edge).
    /// <paramref name="buffer"/> is laid out at <see cref="Av1CdefConstants.BufferStride"/>;
    /// the (HBorder, VBorder) origin within it is the unit's (0, 0). <paramref name="source"/>
    /// must be a frame-start snapshot of the plane — already-filtered units must not leak
    /// into later units' padding (spec 7.15).
    /// </summary>
    public static void Pad(
        ReadOnlySpan<byte> source,
        int sourceStride,
        int unitOriginX,
        int unitOriginY,
        int unitWidth,
        int unitHeight,
        int planeOriginX,
        int planeOriginY,
        int planeWidth,
        int planeHeight,
        Span<ushort> buffer)
    {
        int stride = Av1CdefConstants.BufferStride;
        int hBorder = Av1CdefConstants.HorizontalBorder;
        int vBorder = Av1CdefConstants.VerticalBorder;
        int rows = unitHeight + (2 * vBorder);
        int cols = unitWidth + (2 * hBorder);

        for (int r = 0; r < rows; r++)
        {
            int absoluteY = unitOriginY + r - vBorder;
            int rowBase = r * stride;
            bool rowOnFrame = absoluteY >= 0 && absoluteY < planeHeight;
            ReadOnlySpan<byte> sourceRow = rowOnFrame
                ? source.Slice((absoluteY + planeOriginY) * sourceStride, sourceStride)
                : default;
            for (int c = 0; c < cols; c++)
            {
                int absoluteX = unitOriginX + c - hBorder;
                if (rowOnFrame && absoluteX >= 0 && absoluteX < planeWidth)
                {
                    buffer[rowBase + c] = sourceRow[absoluteX + planeOriginX];
                }
                else
                {
                    buffer[rowBase + c] = Av1CdefConstants.VeryLarge;
                }
            }
        }
    }
}
