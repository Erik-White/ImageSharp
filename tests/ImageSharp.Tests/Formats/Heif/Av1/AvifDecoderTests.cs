// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class AvifDecoderTests
{
    [Theory]
    [InlineData(TestImages.Heif.Orange4x4)]
    [InlineData(TestImages.Heif.IrvineAvif)]
    [InlineData(TestImages.Heif.XnConvert)]
    [InlineData(TestImages.Heif.ScreenTile256Avif)]
    [InlineData(TestImages.Heif.ScreenTileNopltAvif)]
    [InlineData(TestImages.Heif.ScreenText512Q30Avif)]
    [InlineData(TestImages.Heif.ScreenText512Q60Avif)]
    [InlineData(TestImages.Heif.ScreenTextNopltQ30Avif)]
    [InlineData(TestImages.Heif.ScreenTextNopltQ60Avif)]
    [InlineData(TestImages.Heif.ScreenBannerIbcAvif)]
    public void Identify_AvifFixturesAsAv1(string imagePath)
    {
        TestFile testFile = TestFile.Create(imagePath);
        using MemoryStream stream = new(testFile.Bytes, false);

        ImageInfo info = Image.Identify(stream);

        Assert.Equal(HeifFormat.Instance, info.Metadata.DecodedImageFormat);
        Assert.Equal(HeifCompressionMethod.Av1, info.Metadata.GetHeifMetadata().CompressionMethod);
    }

    [Theory]
    [WithFile(TestImages.Heif.Orange4x4, PixelTypes.Rgba32)]
    public void Decode_Orange4x4<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        HeifMetadata metadata = image.Metadata.GetHeifMetadata();
        Assert.Equal(HeifCompressionMethod.Av1, metadata.CompressionMethod);
        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
        image.DebugSave(provider);
        image.CompareToReferenceOutput(provider, ImageComparer.Exact);
    }
}
