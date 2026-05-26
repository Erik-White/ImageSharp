// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Memory;
using ImageSharpRgb24 = SixLabors.ImageSharp.PixelFormats.Rgb24;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1TilingTests
{
    [Fact]
    public void DecodeOrange4x4ProducesUniformOrange()
    {
        // Reference RGB from libavif/Magick.NET decode of Orange4x4.avif.
        const byte expectedR = 210;
        const byte expectedG = 101;
        const byte expectedB = 6;
        const int tolerance = 1;

        string filePath = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, TestImages.Heif.Orange4x4);
        byte[] content = File.ReadAllBytes(filePath);

        // Locate the AV1 OBU stream inside the AVIF (sequence header at 0x010E).
        const int dataOffset = 0x010E;
        const int dataSize = 0x001d;
        Span<byte> obuSpan = content.AsSpan(dataOffset, dataSize);

        Av1Decoder decoder = new(Configuration.Default);
        using Image<ImageSharpRgb24> image = decoder.Decode<ImageSharpRgb24>(obuSpan);

        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                ImageSharpRgb24 pixel = image[x, y];
                Assert.True(Math.Abs(pixel.R - expectedR) <= tolerance, $"R at ({x},{y}) was {pixel.R}, expected ~{expectedR}");
                Assert.True(Math.Abs(pixel.G - expectedG) <= tolerance, $"G at ({x},{y}) was {pixel.G}, expected ~{expectedG}");
                Assert.True(Math.Abs(pixel.B - expectedB) <= tolerance, $"B at ({x},{y}) was {pixel.B}, expected ~{expectedB}");
            }
        }
    }

    [Theory]
    [InlineData(TestImages.Heif.Orange4x4, 0x010E, 0x001d, 21, 1)]
    public void DecodePixelsFirstTile(string filename, int dataOffset, int dataSize, int tileOffset, int superblockCount)
    {
        // Assign
        string filePath = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, filename);
        byte[] content = File.ReadAllBytes(filePath);
        Span<byte> headerSpan = content.AsSpan(dataOffset, dataSize);
        Span<byte> tileSpan = content.AsSpan(tileOffset, dataSize - tileOffset);
        Av1BitStreamReader bitStreamReader = new(headerSpan);
        IAv1TileReader stub = new Av1TileDecoderStub();
        ObuReader obuReader = new();
        obuReader.ReadAll(ref bitStreamReader, dataSize, () => stub);
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, obuReader.SequenceHeader, Av1ColorFormat.Yuv444, false);
        Av1TileReader tileReader = new(Configuration.Default, obuReader.SequenceHeader, obuReader.FrameHeader);
        Av1FrameDecoder frameDecoder = new(Configuration.Default, obuReader.SequenceHeader, obuReader.FrameHeader, tileReader.FrameInfo, frameBuffer);

        // Act
        tileReader.ReadTile(tileSpan, 0);
        frameDecoder.DecodeFrame();

        // Assert
        Assert.Equal(dataSize * 8, bitStreamReader.BitPosition);
        Assert.False(frameBuffer.BufferY.Size().IsEmpty);
        Assert.True(frameBuffer.BufferY.DangerousGetSingleSpan().ContainsAnyExcept<byte>(0));
    }

    [Theory]
    [InlineData(TestImages.Heif.XnConvert, 0x010E, 0x03CC, 18, 16)]
    [InlineData(TestImages.Heif.Orange4x4, 0x010E, 0x001d, 21, 1)]
    public void DecodePartitionsFirstTile(string filename, int dataOffset, int dataSize, int tileOffset, int superblockCount)
    {
        // Assign
        string filePath = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, filename);
        byte[] content = File.ReadAllBytes(filePath);
        Span<byte> headerSpan = content.AsSpan(dataOffset, dataSize);
        Span<byte> tileSpan = content.AsSpan(tileOffset, dataSize - tileOffset);
        Av1BitStreamReader bitStreamReader = new(headerSpan);
        IAv1TileReader stub = new Av1TileDecoderStub();
        ObuReader obuReader = new();
        obuReader.ReadAll(ref bitStreamReader, dataSize, () => stub);
        Av1TileReader tileReader = new(Configuration.Default, obuReader.SequenceHeader, obuReader.FrameHeader);

        // Act
        tileReader.ReadTile(tileSpan, 0);

        // Assert
        Assert.Equal(dataSize * 8, bitStreamReader.BitPosition);
        Assert.Equal(superblockCount, CountPopulatedSuperblocks(tileReader.FrameInfo, obuReader.SequenceHeader));
    }

    [Theory]
    [InlineData(TestImages.Heif.XnConvert, 0x010E, 0x03CC, 18, 16)]
    [InlineData(TestImages.Heif.Orange4x4, 0x010E, 0x001d, 21, 1)]
    public void ParseHeaderForFirstTile(string filename, int dataOffset, int dataSize, int tileOffset, int superblockCount)
    {
        // Assign
        string filePath = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, filename);
        byte[] content = File.ReadAllBytes(filePath);
        Span<byte> headerSpan = content.AsSpan(dataOffset, dataSize);
        Av1BitStreamReader bitStreamReader = new(headerSpan);
        ObuReader obuReader = new();
        Av1TileReader? tileReader = null;

        // Act
        obuReader.ReadAll(ref bitStreamReader, dataSize, () =>
        {
            tileReader = new Av1TileReader(Configuration.Default, obuReader.SequenceHeader, obuReader.FrameHeader);
            return tileReader;
        });

        // Assert
        Assert.Equal(dataSize * 8, bitStreamReader.BitPosition);
        Assert.NotNull(tileReader);
        Assert.Equal(superblockCount, CountPopulatedSuperblocks(tileReader.FrameInfo, obuReader.SequenceHeader));
    }

    private static int CountPopulatedSuperblocks(Av1FrameInfo frameInfo, ObuSequenceHeader sequenceHeader)
    {
        int superblockSizeLog2 = sequenceHeader.SuperblockSizeLog2;
        int columns = Av1Math.AlignPowerOf2(sequenceHeader.MaxFrameWidth, superblockSizeLog2) >> superblockSizeLog2;
        int rows = Av1Math.AlignPowerOf2(sequenceHeader.MaxFrameHeight, superblockSizeLog2) >> superblockSizeLog2;
        int populated = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (frameInfo.GetSuperblock(new Point(x, y)).BlockCount > 0)
                {
                    populated++;
                }
            }
        }

        return populated;
    }
}
