// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Tests.Formats.Heif;

[Trait("Format", "Heif")]
public class HeifOrientationTests
{
    // Source 2x3 image, distinct value per pixel, encoded as (col,row).
    //   (0,0) (1,0)
    //   (0,1) (1,1)
    //   (0,2) (1,2)
    private static Image<Rgb24> MakeMarker()
    {
        Image<Rgb24> image = new(2, 3);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                image[x, y] = new Rgb24((byte)x, (byte)y, 0);
            }
        }

        return image;
    }

    private static Rgb24 At(Image<Rgb24> image, int x, int y) => image[x, y];

    [Fact]
    public void ApplyOrientationNoOpWhenIdentity()
    {
        using Image<Rgb24> image = MakeMarker();
        HeifItem item = new(Heif4CharCode.Av01, 1) { RotationCount = 0, MirrorMode = FlipMode.None };

        HeifDecoderCore.ApplyOrientation(image, item);

        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(new Rgb24(0, 0, 0), At(image, 0, 0));
        Assert.Equal(new Rgb24(1, 2, 0), At(image, 1, 2));
    }

    [Fact]
    public void ApplyOrientationRotates90Ccw()
    {
        using Image<Rgb24> image = MakeMarker();
        HeifItem item = new(Heif4CharCode.Av01, 1) { RotationCount = 1 };

        HeifDecoderCore.ApplyOrientation(image, item);

        // 90° CCW maps (x,y) in the 2x3 source to (y, W-1-x) in the 3x2 result.
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new Rgb24(0, 0, 0), At(image, 0, 1));
        Assert.Equal(new Rgb24(1, 0, 0), At(image, 0, 0));
        Assert.Equal(new Rgb24(0, 2, 0), At(image, 2, 1));
        Assert.Equal(new Rgb24(1, 2, 0), At(image, 2, 0));
    }

    [Fact]
    public void ApplyOrientationRotates180()
    {
        using Image<Rgb24> image = MakeMarker();
        HeifItem item = new(Heif4CharCode.Av01, 1) { RotationCount = 2 };

        HeifDecoderCore.ApplyOrientation(image, item);

        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(new Rgb24(1, 2, 0), At(image, 0, 0));
        Assert.Equal(new Rgb24(0, 0, 0), At(image, 1, 2));
    }

    [Fact]
    public void ApplyOrientationRotates270Ccw()
    {
        using Image<Rgb24> image = MakeMarker();
        HeifItem item = new(Heif4CharCode.Av01, 1) { RotationCount = 3 };

        HeifDecoderCore.ApplyOrientation(image, item);

        // 270° CCW (== 90° CW) maps (x,y) in 2x3 to (H-1-y, x) in 3x2.
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new Rgb24(0, 0, 0), At(image, 2, 0));
        Assert.Equal(new Rgb24(1, 2, 0), At(image, 0, 1));
    }

    [Fact]
    public void ApplyOrientationMirrorsHorizontally()
    {
        using Image<Rgb24> image = MakeMarker();
        HeifItem item = new(Heif4CharCode.Av01, 1) { MirrorMode = FlipMode.Horizontal };

        HeifDecoderCore.ApplyOrientation(image, item);

        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(new Rgb24(1, 0, 0), At(image, 0, 0));
        Assert.Equal(new Rgb24(0, 0, 0), At(image, 1, 0));
        Assert.Equal(new Rgb24(0, 2, 0), At(image, 1, 2));
    }

    [Fact]
    public void ApplyOrientationMirrorsVertically()
    {
        using Image<Rgb24> image = MakeMarker();
        HeifItem item = new(Heif4CharCode.Av01, 1) { MirrorMode = FlipMode.Vertical };

        HeifDecoderCore.ApplyOrientation(image, item);

        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(new Rgb24(0, 2, 0), At(image, 0, 0));
        Assert.Equal(new Rgb24(0, 0, 0), At(image, 0, 2));
    }

    [Fact]
    public void ApplyOrientationAppliesMirrorBeforeRotation()
    {
        using Image<Rgb24> mirrorOnly = MakeMarker();
        HeifItem mirrorItem = new(Heif4CharCode.Av01, 1) { MirrorMode = FlipMode.Horizontal };
        HeifDecoderCore.ApplyOrientation(mirrorOnly, mirrorItem);
        mirrorOnly.Mutate(c => c.Rotate(RotateMode.Rotate270));

        using Image<Rgb24> combined = MakeMarker();
        HeifItem combinedItem = new(Heif4CharCode.Av01, 1) { MirrorMode = FlipMode.Horizontal, RotationCount = 1 };
        HeifDecoderCore.ApplyOrientation(combined, combinedItem);

        Assert.Equal(mirrorOnly.Width, combined.Width);
        Assert.Equal(mirrorOnly.Height, combined.Height);
        for (int y = 0; y < combined.Height; y++)
        {
            for (int x = 0; x < combined.Width; x++)
            {
                Assert.Equal(At(mirrorOnly, x, y), At(combined, x, y));
            }
        }
    }
}
