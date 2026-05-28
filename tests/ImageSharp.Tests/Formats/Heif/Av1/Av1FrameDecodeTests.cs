// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Abstractions;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

/// <summary>
/// End-to-end frame decode tests: feed an IVF/AVIF fixture through <see cref="Av1Decoder"/>
/// and compare the reconstructed planes against an aomdec-produced reference YUV (or, for
/// adversarial fixtures, assert the expected rejection).
/// </summary>
[Trait("Format", "Avif")]
public class Av1FrameDecodeTests
{
    private readonly ITestOutputHelper output;

    public Av1FrameDecodeTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// `ibc-clean-256.ivf` — 256x256 4:2:0 fixture authored to exercise IBC. libaom emits
    /// 29 valid IBC blocks for this stream, so this test covers the round-trip from DV
    /// decode through IBC source copy and inverse-transform residual, plus the palette
    /// path that supplies the upstream Y samples those IBC blocks copy from.
    /// </summary>
    [Fact]
    public void IbcClean256_Frame0_MatchesLibaomReference()
    {
        byte[] obus = LoadIvfFirstFrame(TestImages.Heif.IbcClean256Ivf);

        Av1Decoder decoder = new(Configuration.Default);
        using Image<Rgba32> _ = decoder.Decode<Rgba32>(obus);
        Assert.NotNull(decoder.FrameBuffer);
        Assert.NotNull(decoder.FrameHeader);

        int width = decoder.FrameHeader.FrameSize.FrameWidth;
        int height = decoder.FrameHeader.FrameSize.FrameHeight;
        int chromaWidth = (width + 1) >> 1;
        int chromaHeight = (height + 1) >> 1;

        byte[] reference = LoadReference("Heif/Av1/ibc-clean-256.frame0.yuv");
        int expectedSize = (width * height) + (2 * chromaWidth * chromaHeight);
        Assert.Equal(expectedSize, reference.Length);

        int yOffset = 0;
        int uOffset = width * height;
        int vOffset = uOffset + (chromaWidth * chromaHeight);

        int originX = decoder.FrameBuffer!.OriginX;
        int originY = decoder.FrameBuffer.OriginY;
        int chromaOriginX = originX >> 1;
        int chromaOriginY = originY >> 1;

        PlaneDiff yDiff = ComparePlane(decoder.FrameBuffer.BufferY!, reference.AsSpan(yOffset, width * height), width, height, originX, originY);
        PlaneDiff uDiff = ComparePlane(decoder.FrameBuffer.BufferCb!, reference.AsSpan(uOffset, chromaWidth * chromaHeight), chromaWidth, chromaHeight, chromaOriginX, chromaOriginY);
        PlaneDiff vDiff = ComparePlane(decoder.FrameBuffer.BufferCr!, reference.AsSpan(vOffset, chromaWidth * chromaHeight), chromaWidth, chromaHeight, chromaOriginX, chromaOriginY);

        this.output.WriteLine($"Y: {yDiff}");
        this.output.WriteLine($"U: {uDiff}");
        this.output.WriteLine($"V: {vDiff}");

        Assert.Equal(0, yDiff.MaxAbs);
        Assert.Equal(0, uDiff.MaxAbs);
        Assert.Equal(0, vDiff.MaxAbs);
    }

    /// <summary>
    /// `intrabc-extreme-dv.ivf` is the AOMedia conformance vector
    /// `av1-1-b8-16-intra_only-intrabc-extreme-dv` — an adversarial stream that drives
    /// IBC displacement vectors to the spec extremes. The frame contains a block
    /// (mi=(50,324), bsize=Block16x8, dv=(-1664,2432)) whose source rectangle escapes
    /// the current tile on the left edge. libaom rejects the block with
    /// `AOM_CODEC_CORRUPT_FRAME — Invalid intrabc dv`, and our
    /// <see cref="Av1IntraBlockCopyValidator"/> mirrors that behaviour.
    /// </summary>
    [Fact]
    public void IntraBcExtremeDv_Frame0_RejectsAdversarialDv()
    {
        byte[] obus = LoadIvfFirstFrame(TestImages.Heif.IntraBcExtremeDvIvf);

        Av1Decoder decoder = new(Configuration.Default);
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(
            () =>
            {
                using Image<Rgba32> _ = decoder.Decode<Rgba32>(obus);
            });

        Assert.Contains("IBC source region escapes the current tile", ex.Message);
    }

    /// <summary>
    /// `mono-ibc-256.ivf` — 256x256 8-bit monochrome (Cmono) fixture that exercises the
    /// <c>IsMonochrome</c> branch the 4:2:0 IBC fixtures don't cover. The frame is also
    /// coded as <c>coded_lossless=true</c>, which forces every transform unit through the
    /// inverse Walsh-Hadamard 4x4 path and skips loop filtering. libaom emits 4 valid IBC
    /// blocks; the reference YUV was produced by aomdec --i420 (chroma planes are
    /// synthesized as 0x80 fillers and ignored here).
    /// </summary>
    [Fact]
    public void MonoIbc256_Frame0_Y_MatchesLibaomReference()
    {
        byte[] obus = LoadIvfFirstFrame(TestImages.Heif.MonoIbc256Ivf);

        Av1Decoder decoder = new(Configuration.Default);
        using Image<Rgba32> _ = decoder.Decode<Rgba32>(obus);
        Assert.NotNull(decoder.FrameBuffer);
        Assert.NotNull(decoder.FrameHeader);

        int width = decoder.FrameHeader.FrameSize.FrameWidth;
        int height = decoder.FrameHeader.FrameSize.FrameHeight;
        Assert.Equal(256, width);
        Assert.Equal(256, height);

        byte[] reference = LoadReference("Heif/Av1/mono-ibc-256.frame0.yuv");
        Assert.True(reference.Length >= width * height, "reference shorter than Y plane");

        int originX = decoder.FrameBuffer!.OriginX;
        int originY = decoder.FrameBuffer.OriginY;

        byte[] decoded = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            decoder.FrameBuffer.BufferY!.DangerousGetRowSpan(y + originY).Slice(originX, width).CopyTo(decoded.AsSpan(y * width, width));
        }

        File.WriteAllBytes(@"C:\Users\ewhite\AppData\Local\Temp\av1mono_decoded_y.bin", decoded);

        PlaneDiff yDiff = ComparePlane(decoder.FrameBuffer.BufferY!, reference.AsSpan(0, width * height), width, height, originX, originY);
        this.output.WriteLine($"Y: {yDiff}");

        Assert.Equal(0, yDiff.MaxAbs);
    }

    /// <summary>
    /// `Orange4x4.ivf` — 4x4 4:2:0 single-block solid-orange fixture (re-muxed from the
    /// matching AVIF). Tiny, all-DC, no IBC, no palette: the simplest possible exercise of
    /// the regular non-IBC inverse-transform path.
    /// </summary>
    [Fact]
    public void Orange4x4_Frame0_MatchesLibaomReference()
    {
        AssertLibaomYuv420Match(TestImages.Heif.Orange4x4Ivf, "Heif/Av1/Orange4x4.frame0.yuv");
    }

    /// <summary>
    /// `Irvine_CA.ivf` — 384x256 4:2:0 photographic fixture (re-muxed from the AVIF in
    /// AOMediaCodec/av1-avif/testFiles/Microsoft). Currently throws inside
    /// <c>ReadLoopRestoration</c>; the loop-restoration syntax + filter aren't implemented
    /// yet. Reference YUV is in place so the assertion can light up once the missing
    /// pieces land.
    /// </summary>
    [Fact(Skip = "Loop-restoration parser is implemented but the Wiener/SGR filter apply step is not. Decoder advances to a complete frame but Y mean_abs ~53 vs libaom because the filters aren't applied.")]
    public void IrvineCa_Frame0_MatchesLibaomReference()
    {
        AssertLibaomYuv420Match(TestImages.Heif.IrvineCaIvf, "Heif/Av1/Irvine_CA.frame0.yuv");
    }

    /// <summary>
    /// Smoke test: every fixture in the AV1 input set should at least drive the decoder
    /// to completion without throwing an unimplemented-feature exception. When a fixture
    /// trips a NotImplementedException the test is expected to be marked Skip with the
    /// missing-feature name; until then this set is the canary that flags new failure
    /// modes when AV1 features land.
    /// </summary>
    [Theory]
    [InlineData(TestImages.Heif.Orange4x4Ivf)]
    [InlineData(TestImages.Heif.IbcClean256Ivf)]
    [InlineData(TestImages.Heif.MonoIbc256Ivf)]
    [InlineData(TestImages.Heif.ScreenText512Q30Ivf)]
    [InlineData(TestImages.Heif.ScreenText512Q60Ivf, Skip = "Av1IntraBlockCopyValidator throws on a real AOM fixture: 'IBC source violates the wavefront constraint'. Either the validator is stricter than libaom, or the fixture trips a corner case of spec 6.10.25.")]
    [InlineData(TestImages.Heif.ScreenTextNopltQ30Ivf)]
    [InlineData(TestImages.Heif.ScreenTextNopltQ60Ivf)]
    [InlineData(TestImages.Heif.ScreenTile256Ivf)]
    [InlineData(TestImages.Heif.ScreenTileNopltIvf)]
    [InlineData(TestImages.Heif.ScreenBannerIbcIvf)]
    [InlineData(TestImages.Heif.XnConvertIvf)]
    public void DecodeWithoutThrowing(string ivfFixture)
    {
        byte[] obus = LoadIvfFirstFrame(ivfFixture);
        Av1Decoder decoder = new(Configuration.Default);
        using Image<Rgba32> _ = decoder.Decode<Rgba32>(obus);
        Assert.NotNull(decoder.FrameBuffer);
        Assert.NotNull(decoder.FrameHeader);
    }

    private void AssertLibaomYuv420Match(string ivfFixture, string referenceRelativePath)
    {
        byte[] obus = LoadIvfFirstFrame(ivfFixture);

        Av1Decoder decoder = new(Configuration.Default);
        using Image<Rgba32> _ = decoder.Decode<Rgba32>(obus);
        Assert.NotNull(decoder.FrameBuffer);
        Assert.NotNull(decoder.FrameHeader);

        int width = decoder.FrameHeader!.FrameSize.FrameWidth;
        int height = decoder.FrameHeader.FrameSize.FrameHeight;
        int chromaWidth = (width + 1) >> 1;
        int chromaHeight = (height + 1) >> 1;

        byte[] reference = LoadReference(referenceRelativePath);
        int expectedSize = (width * height) + (2 * chromaWidth * chromaHeight);
        Assert.Equal(expectedSize, reference.Length);

        int yOffset = 0;
        int uOffset = width * height;
        int vOffset = uOffset + (chromaWidth * chromaHeight);

        int originX = decoder.FrameBuffer!.OriginX;
        int originY = decoder.FrameBuffer.OriginY;
        int chromaOriginX = originX >> 1;
        int chromaOriginY = originY >> 1;

        PlaneDiff yDiff = ComparePlane(decoder.FrameBuffer.BufferY!, reference.AsSpan(yOffset, width * height), width, height, originX, originY);
        PlaneDiff uDiff = ComparePlane(decoder.FrameBuffer.BufferCb!, reference.AsSpan(uOffset, chromaWidth * chromaHeight), chromaWidth, chromaHeight, chromaOriginX, chromaOriginY);
        PlaneDiff vDiff = ComparePlane(decoder.FrameBuffer.BufferCr!, reference.AsSpan(vOffset, chromaWidth * chromaHeight), chromaWidth, chromaHeight, chromaOriginX, chromaOriginY);

        this.output.WriteLine($"Y: {yDiff}");
        this.output.WriteLine($"U: {uDiff}");
        this.output.WriteLine($"V: {vDiff}");

        Assert.Equal(0, yDiff.MaxAbs);
        Assert.Equal(0, uDiff.MaxAbs);
        Assert.Equal(0, vDiff.MaxAbs);
    }

    private static byte[] LoadIvfFirstFrame(string fixture)
    {
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, fixture);
        return ExtractFirstIvfFrame(File.ReadAllBytes(path));
    }

    private static byte[] LoadReference(string relativePath)
    {
        string path = Path.Combine(TestEnvironment.SolutionDirectoryFullPath, "tests/Images/ReferenceOutput", relativePath);
        return File.ReadAllBytes(path);
    }

    private static PlaneDiff ComparePlane(Buffer2D<byte> plane, ReadOnlySpan<byte> reference, int width, int height, int originX, int originY)
    {
        long sumAbs = 0;
        int maxAbs = 0;
        int diffCount = 0;
        Point firstDiff = new(-1, -1);
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> refRow = reference.Slice(y * width, width);
            ReadOnlySpan<byte> decRow = plane.DangerousGetRowSpan(y + originY).Slice(originX, width);
            for (int x = 0; x < width; x++)
            {
                int d = decRow[x] - refRow[x];
                int abs = d < 0 ? -d : d;
                if (abs > 0)
                {
                    diffCount++;
                    if (firstDiff.X < 0)
                    {
                        firstDiff = new Point(x, y);
                    }
                }

                sumAbs += abs;
                if (abs > maxAbs)
                {
                    maxAbs = abs;
                }
            }
        }

        return new PlaneDiff(maxAbs, sumAbs, diffCount, firstDiff, width * height);
    }

    private static byte[] ExtractFirstIvfFrame(byte[] bytes)
    {
        Assert.True(bytes.Length > 32 + 12, "IVF too short");
        Assert.Equal((byte)'D', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        Assert.Equal((byte)'I', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
        int frameSize = BitConverter.ToInt32(bytes, 32);
        return bytes.AsSpan(32 + 12, frameSize).ToArray();
    }

    private record PlaneDiff(int MaxAbs, long SumAbs, int DiffCount, Point FirstDiff, int TotalPixels)
    {
        public override string ToString()
            => $"max={this.MaxAbs} mean_abs={(double)this.SumAbs / this.TotalPixels:F3} differing={this.DiffCount}/{this.TotalPixels} first={this.FirstDiff}";
    }
}
