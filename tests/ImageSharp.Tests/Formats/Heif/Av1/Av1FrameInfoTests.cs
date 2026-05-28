// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1FrameInfoTests
{
    /// <summary>
    /// Pins the negative-coordinate guard on <see cref="Av1FrameInfo.GetModeInfoAtMiPosition"/>.
    /// Without it, integer division toward zero would alias <c>(-1, y)</c> into
    /// the last column of the previous SB row and silently return a stale block.
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(-1, -1)]
    [InlineData(-8, 4)]
    public void GetModeInfoAtMiPosition_ReturnsNullForNegativeCoordinates(int miX, int miY)
    {
        Av1FrameInfo frameInfo = new(BuildSequenceHeader(width: 256, height: 256));

        Assert.Null(frameInfo.GetModeInfoAtMiPosition(new Point(miX, miY)));
    }

    private static ObuSequenceHeader BuildSequenceHeader(int width, int height)
        => new()
        {
            MaxFrameWidth = width,
            MaxFrameHeight = height,
            Use128x128Superblock = false,
            ColorConfig = new ObuColorConfig
            {
                IsMonochrome = false,
                SubSamplingX = true,
                SubSamplingY = true,
            },
        };
}
