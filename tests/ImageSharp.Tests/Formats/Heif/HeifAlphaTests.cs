// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Tests.Formats.Heif;

[Trait("Format", "Heif")]
public class HeifAlphaTests
{
    [Fact]
    public void CompositeAlphaWritesAuxLumaIntoPrimaryAlpha()
    {
        using Image<Rgba32> primary = new(2, 2);
        primary[0, 0] = new Rgba32(10, 20, 30, 255);
        primary[1, 0] = new Rgba32(40, 50, 60, 255);
        primary[0, 1] = new Rgba32(70, 80, 90, 255);
        primary[1, 1] = new Rgba32(100, 110, 120, 255);

        using Image<L8> alpha = new(2, 2);
        alpha[0, 0] = new L8(0);
        alpha[1, 0] = new L8(64);
        alpha[0, 1] = new L8(192);
        alpha[1, 1] = new L8(255);

        HeifDecoderCore.CompositeAlpha(Configuration.Default, primary, alpha);

        Assert.Equal(0, primary[0, 0].A);
        Assert.Equal(64, primary[1, 0].A);
        Assert.Equal(192, primary[0, 1].A);
        Assert.Equal(255, primary[1, 1].A);

        // RGB channels should round-trip unchanged.
        Assert.Equal(10, primary[0, 0].R);
        Assert.Equal(110, primary[1, 1].G);
    }

    [Fact]
    public void CompositeAlphaIgnoresAuxOnPixelTypeWithoutAlpha()
    {
        // Rgb24 has no alpha component — verify the operation does not crash and leaves RGB intact.
        using Image<Rgb24> primary = new(2, 1);
        primary[0, 0] = new Rgb24(10, 20, 30);
        primary[1, 0] = new Rgb24(40, 50, 60);

        using Image<L8> alpha = new(2, 1);
        alpha[0, 0] = new L8(128);
        alpha[1, 0] = new L8(255);

        HeifDecoderCore.CompositeAlpha(Configuration.Default, primary, alpha);

        Assert.Equal(new Rgb24(10, 20, 30), primary[0, 0]);
        Assert.Equal(new Rgb24(40, 50, 60), primary[1, 0]);
    }
}
