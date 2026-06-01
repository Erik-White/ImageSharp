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
    public void Orange4x4_Frame0_MatchesLibaomReference() => this.AssertLibaomYuv420Match(TestImages.Heif.Orange4x4Ivf, "Heif/Av1/Orange4x4.frame0.yuv");

    /// <summary>
    /// `cdef-only-128.ivf` — synthetic 128×128 4:2:0 frame authored with libaom-av1
    /// <c>-enable-cdef 1 -enable-restoration 0</c>. CDEF fires on every superblock; loop
    /// restoration is disabled at the sequence level. With LR off, the only post-loop-filter
    /// step is CDEF, so a byte-level match against the aomdec reference YUV verifies our
    /// CDEF apply path end-to-end.
    /// </summary>
    [Fact]
    public void CdefOnly128_Frame0_MatchesLibaomReference() => this.AssertLibaomYuv420Match(TestImages.Heif.CdefOnly128Ivf, "Heif/Av1/cdef-only-128.frame0.yuv");

    /// <summary>
    /// `cdef-edge-96x80.ivf` — 96×80 4:2:0 frame with CDEF on, LR off. The frame dimensions
    /// are not multiples of 64, so the CDEF unit grid produces clipped units at the right
    /// edge (32 wide) and bottom edge (16 tall). Validates the unit-clipping path
    /// <c>Math.Min(64, frame.Width - unitOriginX)</c> in <c>Av1CdefUnitDriver.ProcessUnit</c>
    /// that <see cref="CdefOnly128_Frame0_MatchesLibaomReference"/> can't reach.
    /// </summary>
    [Fact]
    public void CdefEdge96x80_Frame0_MatchesLibaomReference() => this.AssertLibaomYuv420Match(TestImages.Heif.CdefEdge96x80Ivf, "Heif/Av1/cdef-edge-96x80.frame0.yuv");

    /// <summary>
    /// `cdef-multi-256.ivf` — 256×256 4:2:0 frame encoded at CRF 50 so libaom picks
    /// <c>bitCount &gt; 0</c> and the per-SB <c>CdefStrength</c> varies. Validates the
    /// per-SB strength dispatch in <c>TryGetUnitStrengthIndex</c> + <c>YStrength[strengthIndex]</c>
    /// lookup which single-strength fixtures (cdef-only-128 with bitCount=0) bypass.
    /// </summary>
    [Fact]
    public void CdefMulti256_Frame0_MatchesLibaomReference() => this.AssertLibaomYuv420Match(TestImages.Heif.CdefMulti256Ivf, "Heif/Av1/cdef-multi-256.frame0.yuv");

    /// <summary>
    /// `cdef-422-128.ivf` — 128×128 4:2:2 CDEF-active fixture. The chroma direction remap
    /// (<c>Av1CdefConstants.ChromaConv422</c> in <c>Av1CdefUnitDriver.RemapChromaDirections</c>)
    /// only fires when <c>SubX != SubY</c>, which 4:2:0 and 4:4:4 fixtures can't reach. Chroma
    /// is half-width / full-height; the golden YUV is aomdec's standard I422 planar output
    /// (full-height, half-width U then V).
    /// </summary>
    [Fact]
    public void Cdef422_128_Frame0_MatchesLibaomReference()
        => this.AssertLibaomYuvMatch(TestImages.Heif.Cdef422_128Ivf, "Heif/Av1/cdef-422-128.frame0.yuv", chromaShiftX: 1, chromaShiftY: 0);

    /// <summary>
    /// `nopost-128.ivf` — synthetic 128×128 4:2:0 frame authored with libaom-av1
    /// <c>-enable-cdef 0 -enable-restoration 0</c>. With every post-loop-filter step off,
    /// only deblock + intra prediction + inverse transform run, so a YUV mismatch isolates
    /// the intra/transform pipeline from CDEF/LR.
    /// </summary>
    [Fact]
    public void NoPost128_Frame0_MatchesLibaomReference() => this.AssertLibaomYuv420Match(TestImages.Heif.NoPost128Ivf, "Heif/Av1/nopost-128.frame0.yuv");

    /// <summary>
    /// `lr-only-128.ivf` — 256x256 4:2:0, CDEF off, loop restoration on (one Y Wiener unit,
    /// chroma NONE). Validates the Wiener apply path end-to-end.
    /// </summary>
    [Fact]
    public void LrOnly128_Frame0_MatchesLibaomReference() => this.AssertLibaomYuv420Match(TestImages.Heif.LrOnly128Ivf, "Heif/Av1/lr-only-128.frame0.yuv");

    /// <summary>
    /// `lossless-256.ivf` — 256x256 4:2:0 coded-lossless frame. Every transform unit is a 4x4
    /// inverse Walsh-Hadamard; large (≥64x64) partitions exercise the chunked lossless
    /// transform-unit count + ordering in <c>Av1TileReader.Residual</c>.
    /// </summary>
    [Fact]
    public void Lossless256_Frame0_MatchesLibaomReference() => this.AssertLibaomYuv420Match(TestImages.Heif.Lossless256Ivf, "Heif/Av1/lossless-256.frame0.yuv");

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
    [InlineData(TestImages.Heif.CdefOnly128Ivf)]
    [InlineData(TestImages.Heif.NoPost128Ivf)]
    [InlineData(TestImages.Heif.CdefEdge96x80Ivf)]
    [InlineData(TestImages.Heif.Cdef422_128Ivf)]
    [InlineData(TestImages.Heif.CdefMulti256Ivf)]
    [InlineData(TestImages.Heif.ScreenText512Q30Ivf)]
    [InlineData(TestImages.Heif.ScreenText512Q60Ivf)]
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

    // 4:2:0 golden: chroma is subsampled on both axes.
    private void AssertLibaomYuv420Match(string ivfFixture, string referenceRelativePath)
        => this.AssertLibaomYuvMatch(ivfFixture, referenceRelativePath, chromaShiftX: 1, chromaShiftY: 1);

    private void AssertLibaomYuvMatch(string ivfFixture, string referenceRelativePath, int chromaShiftX, int chromaShiftY)
    {
        byte[] obus = LoadIvfFirstFrame(ivfFixture);

        Av1Decoder decoder = new(Configuration.Default);
        using Image<Rgba32> _ = decoder.Decode<Rgba32>(obus);
        Assert.NotNull(decoder.FrameBuffer);
        Assert.NotNull(decoder.FrameHeader);

        int width = decoder.FrameHeader!.FrameSize.FrameWidth;
        int height = decoder.FrameHeader.FrameSize.FrameHeight;
        int chromaWidth = (width + ((1 << chromaShiftX) - 1)) >> chromaShiftX;
        int chromaHeight = (height + ((1 << chromaShiftY) - 1)) >> chromaShiftY;

        byte[] reference = LoadReference(referenceRelativePath);
        int expectedSize = (width * height) + (2 * chromaWidth * chromaHeight);
        Assert.Equal(expectedSize, reference.Length);

        int yOffset = 0;
        int uOffset = width * height;
        int vOffset = uOffset + (chromaWidth * chromaHeight);

        int originX = decoder.FrameBuffer!.OriginX;
        int originY = decoder.FrameBuffer.OriginY;
        int chromaOriginX = originX >> chromaShiftX;
        int chromaOriginY = originY >> chromaShiftY;

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
        string path = Path.Combine(TestEnvironment.SolutionDirectoryFullPath, "tests/Images/External/ReferenceOutput", relativePath);
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
