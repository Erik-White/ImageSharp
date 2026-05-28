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

    /// <summary>
    /// Each per-superblock chroma transform-info slice must be 2x the mode-info count
    /// (one bank of slots for U interleaved with one bank for V; see UpdateTransformInfo's
    /// V-from-U copy step). Returning the luma-sized slice instead caused 4:4:4 fixtures
    /// to throw IndexOutOfRange the moment they walked past the U bank.
    /// </summary>
    [Theory]
    [InlineData(true, true)]   // 4:2:0
    [InlineData(false, false)] // 4:4:4
    public void GetSuperblockTransformUv_LengthIsTwiceModeInfoCount(bool subX, bool subY)
    {
        ObuSequenceHeader header = new()
        {
            MaxFrameWidth = 128,
            MaxFrameHeight = 128,
            Use128x128Superblock = false,
            ColorConfig = new ObuColorConfig
            {
                IsMonochrome = false,
                SubSamplingX = subX,
                SubSamplingY = subY,
            },
        };
        Av1FrameInfo frameInfo = new(header);

        // 64x64 SB with 4x4 mode-info units → 16x16 = 256 mi units per SB.
        Span<Av1TransformInfo> chromaSpan = frameInfo.GetSuperblockTransformUv(new Point(0, 0));
        Assert.Equal(512, chromaSpan.Length);
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
