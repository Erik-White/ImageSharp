// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System;
using SixLabors.ImageSharp.Formats.Heif.Av1;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1YuvConverterTests
{
    [Theory]
    [InlineData(255, 255, 255, 255, 128, 128)]
    [InlineData(0, 0, 0, 0, 128, 128)]
    [InlineData(42, 42, 42, 42, 128, 128)]
    [InlineData(150, 100, 50, 107, 97, 155)]
    public void RgbToYuvSinglePixel(byte r, byte g, byte b, int y, int u, int v)
    {
        // Assign
        using Image<Rgb24> image = new(1, 1);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        memory.Span[0] = new Rgb24(r, g, b);
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = 1;
        sequenceHeader.MaxFrameHeight = 1;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, Av1ColorFormat.Yuv444, false);

        // Act
        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);

        // Assert
        byte actualY = frameBuffer.DeriveBlockPointer(Av1Plane.Y, new Point(0, 0), 0, 0, out _)[frameBuffer.BufferY!.Width];
        byte actualU = frameBuffer.DeriveBlockPointer(Av1Plane.U, new Point(0, 0), 0, 0, out _)[frameBuffer.BufferCb!.Width];
        byte actualV = frameBuffer.DeriveBlockPointer(Av1Plane.V, new Point(0, 0), 0, 0, out _)[frameBuffer.BufferCr!.Width];
        Assert.Equal(y, actualY);
        Assert.Equal(u, actualU);
        Assert.Equal(v, actualV);
    }

    [Theory]
    [InlineData(255, 255, 255, 255, 128, 128)]
    [InlineData(0, 0, 0, 0, 128, 128)]
    [InlineData(42, 42, 42, 42, 128, 128)]
    [InlineData(150, 100, 50, 107, 97, 155)]
    public void YuvToRgbSinglePixel(byte r, byte g, byte b, int y, int u, int v)
    {
        // Assign
        using Image<Rgb24> image = new(1, 1);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = 1;
        sequenceHeader.MaxFrameHeight = 1;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, Av1ColorFormat.Yuv444, false);
        frameBuffer.DeriveBlockPointer(Av1Plane.Y, new Point(0, 0), 0, 0, out int yStride)[yStride] = (byte)y;
        frameBuffer.DeriveBlockPointer(Av1Plane.U, new Point(0, 0), 0, 0, out int uStride)[uStride] = (byte)u;
        frameBuffer.DeriveBlockPointer(Av1Plane.V, new Point(0, 0), 0, 0, out int vStride)[vStride] = (byte)v;

        // Act
        Av1YuvConverter.ConvertToRgb(Configuration.Default, frameBuffer, frame);

        // Assert
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        Rgb24 actual = memory.Span[0];
        Assert.Equal(r, actual.R, 1d);
        Assert.Equal(g, actual.G, 1d);
        Assert.Equal(b, actual.B, 1d);
    }

    [Fact]
    public void RgbToYuvCompareToReferenceRandomPixels()
    {
        const int sampleCount = 1000;

        // Assign
        using Image<Rgb24> image = new(sampleCount, 1);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        Random rnd = new(42);
        Span<byte> input = new byte[sampleCount * 3];
        CreateTestData(rnd, input);
        PixelOperations<Rgb24>.Instance.FromBgr24Bytes(Configuration.Default, input, memory.Span, image.Width);
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = image.Width;
        sequenceHeader.MaxFrameHeight = image.Height;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, Av1ColorFormat.Yuv444, false);

        // Act
        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);
        Span<Rgb24> referenceOutput = Av1ReferenceYuvConverter.RgbToYuv(memory.Span, true);

        // Assert
        Span<Rgb24> actual = new Rgb24[frameBuffer.Width];
        Span<byte> yRow = frameBuffer.DeriveBlockPointer(Av1Plane.Y, new Point(0, 0), 0, 0, out int yStride);
        Span<byte> uRow = frameBuffer.DeriveBlockPointer(Av1Plane.U, new Point(0, 0), 0, 0, out int uStride);
        Span<byte> vRow = frameBuffer.DeriveBlockPointer(Av1Plane.V, new Point(0, 0), 0, 0, out int vStride);
        for (int i = 0; i < frameBuffer.Width; i++)
        {
            Rgb24 pixel = new();
            pixel.R = yRow[yStride + i];
            pixel.G = uRow[uStride + i];
            pixel.B = vRow[vStride + i];
            actual[i] = pixel;
        }

        Compare(referenceOutput, actual, 3);
    }

    [Fact]
    public void YuvToRgbCompareToReferenceRandomPixels()
    {
        const int sampleCount = 1000;

        // Assign
        using Image<Rgb24> image = new(sampleCount, 1);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = image.Width;
        sequenceHeader.MaxFrameHeight = image.Height;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, Av1ColorFormat.Yuv444, false);
        Random rnd = new(42);
        CreateTestData(rnd, frameBuffer, Av1Plane.Y);
        CreateTestData(rnd, frameBuffer, Av1Plane.U);
        CreateTestData(rnd, frameBuffer, Av1Plane.V);

        // Act
        Av1YuvConverter.ConvertToRgb(Configuration.Default, frameBuffer, frame);
        Span<Rgb24> referenceOutput = Av1ReferenceYuvConverter.YuvToRgb(frameBuffer, true);

        // Assert
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        Span<Rgb24> actual = memory.Span;
        Compare(referenceOutput, actual, 3);
    }

    private static void Compare(Span<Rgb24> referenceOutput, Span<Rgb24> actual, int allowedDifference)
    {
        for (int i = 0; i < actual.Length; i++)
        {
            if (Math.Abs(referenceOutput[i].R - actual[i].R) > allowedDifference ||
                Math.Abs(referenceOutput[i].G - actual[i].G) > allowedDifference ||
                Math.Abs(referenceOutput[i].B - actual[i].B) > allowedDifference)
            {
                Assert.Fail($"Difference at index {i}, expected: {referenceOutput[i]} but was {actual[i]}");
            }
        }
    }

    private static void CreateTestData(Random rnd, Av1FrameBuffer<byte> frameBuffer, Av1Plane plane)
    {
        const int bitCount = 8;
        Span<byte> span = frameBuffer.DeriveBlockPointer(plane, new Point(0, 0), 0, 0, out int stride);
        int max = (1 << bitCount) - 1;
        for (int i = 0; i < span.Length; i++)
        {
            byte current = (byte)rnd.Next(max);
            span[i] = current;
        }

    }

    private static void CreateTestData(Random rnd, Span<byte> span, int bitCount = 8)
    {
        int max = (1 << bitCount) - 1;
        for (int i = 0; i < span.Length; i++)
        {
            byte current = (byte)rnd.Next(max);
            span[i] = current;
        }
    }

    private static void CreateTestData(Random rnd, Span<ushort> span, int bitCount)
    {
        int max = (1 << bitCount) - 1;
        for (int i = 0; i < span.Length; i++)
        {
            ushort current = (ushort)rnd.Next(max);
            span[i] = current;
        }
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(42, 42, 42)]
    [InlineData(42, 0, 0)]
    [InlineData(42, 42, 0)]
    [InlineData(42, 0, 42)]
    [InlineData(0, 42, 42)]
    [InlineData(0, 0, 42)]
    [InlineData(150, 100, 50)]
    public void RoundTripSinglePixel(byte r, byte g, byte b)
    {
        // Assign
        using Image<Rgb24> image = new(1, 1);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        memory.Span[0] = new Rgb24(r, g, b);
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = 1;
        sequenceHeader.MaxFrameHeight = 1;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, Av1ColorFormat.Yuv444, false);
        using Image<Rgb24> actual = new(image.Width, image.Height);

        // Act
        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);
        Av1YuvConverter.ConvertToRgb(Configuration.Default, frameBuffer, actual.Frames.RootFrame);

        // Assert
        actual.Frames.RootFrame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> actualMemory);
        Rgb24 actualPixel = actualMemory.Span[0];
        Assert.Equal(r, actualPixel.R, 2d);
        Assert.Equal(g, actualPixel.G, 2d);
        Assert.Equal(b, actualPixel.B, 2d);
    }

    [Theory]
    [InlineData((int)Av1ColorFormat.Yuv444)]
    [InlineData((int)Av1ColorFormat.Yuv422)]
    [InlineData((int)Av1ColorFormat.Yuv420)]
    public void RoundTripUniformBlock(int colorFormatValue)
    {
        Av1ColorFormat colorFormat = (Av1ColorFormat)colorFormatValue;
        // 4x4 block of a uniform color survives chroma subsampling losslessly because
        // averaging identical values yields the same value.
        const int width = 4;
        const int height = 4;
        Rgb24 color = new(150, 100, 50);

        using Image<Rgb24> image = new(width, height);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        memory.Span.Fill(color);
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = width;
        sequenceHeader.MaxFrameHeight = height;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, colorFormat, false);
        using Image<Rgb24> actual = new(width, height);

        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);
        Av1YuvConverter.ConvertToRgb(Configuration.Default, frameBuffer, actual.Frames.RootFrame);

        actual.Frames.RootFrame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> actualMemory);
        for (int i = 0; i < actualMemory.Length; i++)
        {
            Rgb24 actualPixel = actualMemory.Span[i];
            Assert.Equal(color.R, actualPixel.R, 2d);
            Assert.Equal(color.G, actualPixel.G, 2d);
            Assert.Equal(color.B, actualPixel.B, 2d);
        }
    }

    [Theory]
    [InlineData(255, 255, 255, 235, 128, 128)]
    [InlineData(0, 0, 0, 16, 128, 128)]
    [InlineData(150, 100, 50, 108, 101, 152)]
    public void RgbToYuvSinglePixelLimitedRange(byte r, byte g, byte b, int y, int u, int v)
    {
        using Image<Rgb24> image = new(1, 1);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        memory.Span[0] = new Rgb24(r, g, b);
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.MaxFrameWidth = 1;
        sequenceHeader.MaxFrameHeight = 1;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, Av1ColorFormat.Yuv444, false);

        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);

        byte actualY = frameBuffer.DeriveBlockPointer(Av1Plane.Y, new Point(0, 0), 0, 0, out _)[frameBuffer.BufferY!.Width];
        byte actualU = frameBuffer.DeriveBlockPointer(Av1Plane.U, new Point(0, 0), 0, 0, out _)[frameBuffer.BufferCb!.Width];
        byte actualV = frameBuffer.DeriveBlockPointer(Av1Plane.V, new Point(0, 0), 0, 0, out _)[frameBuffer.BufferCr!.Width];
        Assert.Equal(y, actualY, 1d);
        Assert.Equal(u, actualU, 1d);
        Assert.Equal(v, actualV, 1d);
    }

    [Theory]
    [InlineData((int)Av1ColorFormat.Yuv444)]
    [InlineData((int)Av1ColorFormat.Yuv422)]
    [InlineData((int)Av1ColorFormat.Yuv420)]
    public void RoundTripUniformBlockLimitedRange(int colorFormatValue)
    {
        Av1ColorFormat colorFormat = (Av1ColorFormat)colorFormatValue;
        const int width = 4;
        const int height = 4;
        Rgb24 color = new(150, 100, 50);

        using Image<Rgb24> image = new(width, height);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        memory.Span.Fill(color);
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.MaxFrameWidth = width;
        sequenceHeader.MaxFrameHeight = height;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, colorFormat, false);
        using Image<Rgb24> actual = new(width, height);

        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);
        Av1YuvConverter.ConvertToRgb(Configuration.Default, frameBuffer, actual.Frames.RootFrame);

        actual.Frames.RootFrame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> actualMemory);
        for (int i = 0; i < actualMemory.Length; i++)
        {
            Rgb24 actualPixel = actualMemory.Span[i];
            Assert.Equal(color.R, actualPixel.R, 2d);
            Assert.Equal(color.G, actualPixel.G, 2d);
            Assert.Equal(color.B, actualPixel.B, 2d);
        }
    }

    [Theory]
    [InlineData((int)Av1ColorFormat.Yuv444)]
    [InlineData((int)Av1ColorFormat.Yuv422)]
    [InlineData((int)Av1ColorFormat.Yuv420)]
    public void RoundTripGreyscaleGradient(int colorFormatValue)
    {
        // A greyscale gradient has R==G==B so chroma is constant; subsampling loses no information
        // and Y reconstructs exactly (subject to the BT.709 round-trip's 2-LSB tolerance).
        Av1ColorFormat colorFormat = (Av1ColorFormat)colorFormatValue;
        const int width = 8;
        const int height = 8;
        using Image<Rgb24> image = new(width, height);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = (byte)(((x + y) * 255) / (width + height - 2));
                memory.Span[(y * width) + x] = new Rgb24(v, v, v);
            }
        }

        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = width;
        sequenceHeader.MaxFrameHeight = height;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, colorFormat, false);
        using Image<Rgb24> actual = new(width, height);

        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);
        Av1YuvConverter.ConvertToRgb(Configuration.Default, frameBuffer, actual.Frames.RootFrame);

        actual.Frames.RootFrame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> actualMemory);
        for (int i = 0; i < memory.Length; i++)
        {
            Rgb24 expected = memory.Span[i];
            Rgb24 got = actualMemory.Span[i];
            Assert.Equal(expected.R, got.R, 2d);
            Assert.Equal(expected.G, got.G, 2d);
            Assert.Equal(expected.B, got.B, 2d);
        }
    }

    [Theory]
    [InlineData((int)Av1ColorFormat.Yuv422)]
    [InlineData((int)Av1ColorFormat.Yuv420)]
    public void SubsampledChromaWritesCorrectPlaneSize(int colorFormatValue)
    {
        // Verify that ConvertFromRgb writes the correct number of chroma samples for a subsampled format
        // by checking that distinct horizontal/vertical bands appear in the chroma plane.
        Av1ColorFormat colorFormat = (Av1ColorFormat)colorFormatValue;
        const int width = 8;
        const int height = 8;
        int subX = 1;
        int subY = colorFormat == Av1ColorFormat.Yuv420 ? 1 : 0;

        using Image<Rgb24> image = new(width, height);
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        frame.DangerousTryGetSinglePixelMemory(out Memory<Rgb24> memory);

        // Two solid color regions: left half pure red, right half pure blue.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                memory.Span[(y * width) + x] = x < width / 2 ? new Rgb24(255, 0, 0) : new Rgb24(0, 0, 255);
            }
        }

        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = width;
        sequenceHeader.MaxFrameHeight = height;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, colorFormat, false);

        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);

        // V plane: red region has high V, blue region has low V — should still differ across the boundary.
        Span<byte> vSpan = frameBuffer.DeriveBlockPointer(Av1Plane.V, default, subX, subY, out int vStride);
        int chromaWidth = width >> subX;
        int chromaHeight = height >> subY;
        for (int cy = 0; cy < chromaHeight; cy++)
        {
            int rowOffset = vStride + (cy * vStride);
            byte leftHalf = vSpan[rowOffset + 0];
            byte rightHalf = vSpan[rowOffset + chromaWidth - 1];
            Assert.True(leftHalf > rightHalf + 50, $"Expected V plane to distinguish red/blue regions at row {cy}: left={leftHalf} right={rightHalf}");
        }
    }

    [Theory]
    [InlineData((int)Av1ColorFormat.Yuv444)]
    [InlineData((int)Av1ColorFormat.Yuv422)]
    [InlineData((int)Av1ColorFormat.Yuv420)]
    public void ChromaPlaneSizesForFormat(int colorFormatValue)
    {
        Av1ColorFormat colorFormat = (Av1ColorFormat)colorFormatValue;
        const int width = 16;
        const int height = 16;
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = width;
        sequenceHeader.MaxFrameHeight = height;
        using Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, colorFormat, false);

        int subX = colorFormat == Av1ColorFormat.Yuv444 ? 0 : 1;
        int subY = colorFormat == Av1ColorFormat.Yuv420 ? 1 : 0;

        Assert.NotNull(frameBuffer.BufferY);
        Assert.NotNull(frameBuffer.BufferCb);
        Assert.NotNull(frameBuffer.BufferCr);

        // Luma plane covers the full image, chroma planes are subsampled per-format.
        Span<byte> ySpan = frameBuffer.DeriveBlockPointer(Av1Plane.Y, default, 0, 0, out int yStride);
        Span<byte> uSpan = frameBuffer.DeriveBlockPointer(Av1Plane.U, default, subX, subY, out int uStride);
        Assert.True(yStride >= width);
        Assert.True(uStride >= (width + subX) >> subX);
        Assert.True(ySpan.Length >= yStride * height);
        Assert.True(uSpan.Length >= uStride * ((height + subY) >> subY));
    }

    // [Theory]
    // [WithFile(TestImages.Jpeg.Baseline.Winter444_Interleaved, PixelTypes.Rgb24)]
    public void RoundTrip(TestImageProvider<Rgb24> provider)
    {
        // Assign
        using Image<Rgb24> image = provider.GetImage();
        ImageFrame<Rgb24> frame = image.Frames.RootFrame;
        ObuSequenceHeader sequenceHeader = new();
        sequenceHeader.ColorConfig.ColorRange = true;
        sequenceHeader.MaxFrameWidth = image.Width;
        sequenceHeader.MaxFrameHeight = image.Height;
        Av1FrameBuffer<byte> frameBuffer = new(Configuration.Default, sequenceHeader, Av1ColorFormat.Yuv444, false);
        using Image<Rgb24> actual = new(image.Width, image.Height);

        // Act
        Av1YuvConverter.ConvertFromRgb(Configuration.Default, frame, frameBuffer);
        Av1YuvConverter.ConvertToRgb(Configuration.Default, frameBuffer, actual.Frames.RootFrame);

        // Assert
        ImageComparer.Tolerant(0.002F).VerifySimilarity(image, actual);
    }
}
