// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1CdefBufferPadTests
{
    /// <summary>
    /// Inside a frame with no boundary touches, the working buffer is a verbatim copy of
    /// the unit pixels plus the neighbouring rows/columns. Tests a 64×64 unit at frame
    /// position (64, 64) inside a synthetic 256×256 plane filled with row*16 + col so each
    /// pixel has a unique value the test can verify.
    /// </summary>
    [Fact]
    public void Pad_InteriorUnit_CopiesFrameWithBorders()
    {
        const int frameWidth = 256;
        const int frameHeight = 256;
        const int unitOriginX = 64;
        const int unitOriginY = 64;
        const int unitWidth = 64;
        const int unitHeight = 64;

        byte[] plane = new byte[frameWidth * frameHeight];
        for (int y = 0; y < frameHeight; y++)
        {
            for (int x = 0; x < frameWidth; x++)
            {
                plane[(y * frameWidth) + x] = (byte)(((y * 16) + x) & 0xFF);
            }
        }

        ushort[] buffer = new ushort[Av1CdefConstants.BufferStride * (unitHeight + (2 * Av1CdefConstants.VerticalBorder))];
        Av1CdefBufferPad.Pad(plane, frameWidth, unitOriginX, unitOriginY, unitWidth, unitHeight, 0, 0, frameWidth, frameHeight, buffer);

        int hBorder = Av1CdefConstants.HorizontalBorder;
        int vBorder = Av1CdefConstants.VerticalBorder;
        int stride = Av1CdefConstants.BufferStride;

        for (int dy = -2; dy < unitHeight + 2; dy++)
        {
            for (int dx = -2; dx < unitWidth + 2; dx++)
            {
                ushort actual = buffer[((vBorder + dy) * stride) + hBorder + dx];
                byte expected = plane[((unitOriginY + dy) * frameWidth) + unitOriginX + dx];
                Assert.Equal(expected, actual);
            }
        }
    }

    /// <summary>
    /// On the top-left corner of the frame the rows and columns above/left of the unit are
    /// off-frame and must be stamped with <see cref="Av1CdefConstants.VeryLarge"/> so the
    /// filter's max/min envelope check skips them.
    /// </summary>
    [Fact]
    public void Pad_TopLeftCorner_FillsOffFrameWithVeryLarge()
    {
        const int frameWidth = 64;
        const int frameHeight = 64;
        const int unitWidth = 64;
        const int unitHeight = 64;

        byte[] plane = new byte[frameWidth * frameHeight];
        Array.Fill(plane, (byte)0x77);

        ushort[] buffer = new ushort[Av1CdefConstants.BufferStride * (unitHeight + (2 * Av1CdefConstants.VerticalBorder))];
        Av1CdefBufferPad.Pad(plane, frameWidth, 0, 0, unitWidth, unitHeight, 0, 0, frameWidth, frameHeight, buffer);

        int hBorder = Av1CdefConstants.HorizontalBorder;
        int vBorder = Av1CdefConstants.VerticalBorder;
        int stride = Av1CdefConstants.BufferStride;

        for (int r = 0; r < vBorder; r++)
        {
            for (int c = 0; c < hBorder + unitWidth + hBorder; c++)
            {
                Assert.Equal(Av1CdefConstants.VeryLarge, buffer[(r * stride) + c]);
            }
        }

        for (int r = vBorder; r < vBorder + unitHeight; r++)
        {
            for (int c = 0; c < hBorder; c++)
            {
                Assert.Equal(Av1CdefConstants.VeryLarge, buffer[(r * stride) + c]);
            }
        }

        for (int r = vBorder; r < vBorder + unitHeight; r++)
        {
            for (int c = hBorder; c < hBorder + unitWidth; c++)
            {
                Assert.Equal(0x77, buffer[(r * stride) + c]);
            }
        }
    }

    /// <summary>
    /// On the right edge, the columns past the frame width are off-frame; the rows still
    /// available (above/below the unit, but inside the frame) are real pixel values.
    /// </summary>
    [Fact]
    public void Pad_RightEdge_FillsOffFrameOnlyOnRight()
    {
        const int frameWidth = 64;
        const int frameHeight = 128;
        const int unitOriginX = 0;
        const int unitOriginY = 32;
        const int unitWidth = 64;
        const int unitHeight = 64;

        byte[] plane = new byte[frameWidth * frameHeight];
        Array.Fill(plane, (byte)0x55);

        ushort[] buffer = new ushort[Av1CdefConstants.BufferStride * (unitHeight + (2 * Av1CdefConstants.VerticalBorder))];
        Av1CdefBufferPad.Pad(plane, frameWidth, unitOriginX, unitOriginY, unitWidth, unitHeight, 0, 0, frameWidth, frameHeight, buffer);

        int hBorder = Av1CdefConstants.HorizontalBorder;
        int vBorder = Av1CdefConstants.VerticalBorder;
        int stride = Av1CdefConstants.BufferStride;

        for (int r = 0; r < unitHeight + (2 * vBorder); r++)
        {
            for (int c = hBorder + unitWidth; c < hBorder + unitWidth + hBorder; c++)
            {
                Assert.Equal(Av1CdefConstants.VeryLarge, buffer[(r * stride) + c]);
            }
        }

        for (int r = 0; r < unitHeight + (2 * vBorder); r++)
        {
            int absoluteY = unitOriginY + r - vBorder;
            if (absoluteY is < 0 or >= frameHeight)
            {
                continue;
            }

            for (int c = hBorder; c < hBorder + unitWidth; c++)
            {
                Assert.Equal(0x55, buffer[(r * stride) + c]);
            }
        }
    }
}
