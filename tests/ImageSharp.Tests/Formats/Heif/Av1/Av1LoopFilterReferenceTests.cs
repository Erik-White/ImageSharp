// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1LoopFilterReferenceTests
{
    private const byte StrongMbLimit = 200;
    private const byte StrongLimit = 200;
    private const byte HevThreshold = 7;

    public enum LibaomCase
    {
        VerticalStep4,
        VerticalStep6,
        VerticalStep8,
        VerticalStep14,
        VerticalRamp14,
        HorizontalStep4,
        HorizontalStep6,
        HorizontalStep8,
        HorizontalStep14,
    }

    // Reference outputs captured from libaom's aom_dsp/loopfilter.c built standalone:
    // 32x32 plane, edge at column or row 16, blimit=200 limit=200 thresh=7.
    // Vertical layouts: 4 rows (q0 rows 16..19) x 14 columns (cols 9..22).
    // Horizontal layouts: 14 rows (rows 9..22) x 4 columns (q0 cols 16..19).
    private static readonly byte[] VerticalStep4Ref =
    [
        120, 120, 120, 120, 120, 122, 124, 126, 128, 130, 130, 130, 130, 130,
        120, 120, 120, 120, 120, 122, 124, 126, 128, 130, 130, 130, 130, 130,
        120, 120, 120, 120, 120, 122, 124, 126, 128, 130, 130, 130, 130, 130,
        120, 120, 120, 120, 120, 122, 124, 126, 128, 130, 130, 130, 130, 130,
    ];

    private static readonly byte[] VerticalStep6Ref =
    [
        120, 120, 120, 120, 120, 121, 124, 126, 129, 130, 130, 130, 130, 130,
        120, 120, 120, 120, 120, 121, 124, 126, 129, 130, 130, 130, 130, 130,
        120, 120, 120, 120, 120, 121, 124, 126, 129, 130, 130, 130, 130, 130,
        120, 120, 120, 120, 120, 121, 124, 126, 129, 130, 130, 130, 130, 130,
    ];

    private static readonly byte[] VerticalStep8Ref =
    [
        120, 120, 120, 120, 121, 123, 124, 126, 128, 129, 130, 130, 130, 130,
        120, 120, 120, 120, 121, 123, 124, 126, 128, 129, 130, 130, 130, 130,
        120, 120, 120, 120, 121, 123, 124, 126, 128, 129, 130, 130, 130, 130,
        120, 120, 120, 120, 121, 123, 124, 126, 128, 129, 130, 130, 130, 130,
    ];

    private static readonly byte[] VerticalStep14Ref =
    [
        120, 121, 121, 122, 123, 123, 124, 126, 127, 128, 128, 129, 129, 130,
        120, 121, 121, 122, 123, 123, 124, 126, 127, 128, 128, 129, 129, 130,
        120, 121, 121, 122, 123, 123, 124, 126, 127, 128, 128, 129, 129, 130,
        120, 121, 121, 122, 123, 123, 124, 126, 127, 128, 128, 129, 129, 130,
    ];

    private static readonly byte[] VerticalRamp14Ref =
    [
        114, 115, 116, 117, 118, 121, 124, 126, 129, 132, 133, 134, 135, 136,
        114, 115, 116, 117, 118, 121, 124, 126, 129, 132, 133, 134, 135, 136,
        114, 115, 116, 117, 118, 121, 124, 126, 129, 132, 133, 134, 135, 136,
        114, 115, 116, 117, 118, 121, 124, 126, 129, 132, 133, 134, 135, 136,
    ];

    private static readonly byte[] HorizontalStep4Ref =
    [
        120, 120, 120, 120,
        120, 120, 120, 120,
        120, 120, 120, 120,
        120, 120, 120, 120,
        120, 120, 120, 120,
        122, 122, 122, 122,
        124, 124, 124, 124,
        126, 126, 126, 126,
        128, 128, 128, 128,
        130, 130, 130, 130,
        130, 130, 130, 130,
        130, 130, 130, 130,
        130, 130, 130, 130,
        130, 130, 130, 130,
    ];

    private static readonly byte[] HorizontalStep6Ref =
    [
        120, 120, 120, 120,
        120, 120, 120, 120,
        120, 120, 120, 120,
        120, 120, 120, 120,
        120, 120, 120, 120,
        121, 121, 121, 121,
        124, 124, 124, 124,
        126, 126, 126, 126,
        129, 129, 129, 129,
        130, 130, 130, 130,
        130, 130, 130, 130,
        130, 130, 130, 130,
        130, 130, 130, 130,
        130, 130, 130, 130,
    ];

    private static readonly byte[] HorizontalStep8Ref =
    [
        120, 120, 120, 120,
        120, 120, 120, 120,
        120, 120, 120, 120,
        120, 120, 120, 120,
        121, 121, 121, 121,
        123, 123, 123, 123,
        124, 124, 124, 124,
        126, 126, 126, 126,
        128, 128, 128, 128,
        129, 129, 129, 129,
        130, 130, 130, 130,
        130, 130, 130, 130,
        130, 130, 130, 130,
        130, 130, 130, 130,
    ];

    private static readonly byte[] HorizontalStep14Ref =
    [
        120, 120, 120, 120,
        121, 121, 121, 121,
        121, 121, 121, 121,
        122, 122, 122, 122,
        123, 123, 123, 123,
        123, 123, 123, 123,
        124, 124, 124, 124,
        126, 126, 126, 126,
        127, 127, 127, 127,
        128, 128, 128, 128,
        128, 128, 128, 128,
        129, 129, 129, 129,
        129, 129, 129, 129,
        130, 130, 130, 130,
    ];

    [Theory]
    [InlineData(LibaomCase.VerticalStep4)]
    [InlineData(LibaomCase.VerticalStep6)]
    [InlineData(LibaomCase.VerticalStep8)]
    [InlineData(LibaomCase.VerticalStep14)]
    [InlineData(LibaomCase.VerticalRamp14)]
    [InlineData(LibaomCase.HorizontalStep4)]
    [InlineData(LibaomCase.HorizontalStep6)]
    [InlineData(LibaomCase.HorizontalStep8)]
    [InlineData(LibaomCase.HorizontalStep14)]
    public void Lpf_MatchesLibaomReference(LibaomCase variant)
    {
        const int size = 32;
        const int stride = size;
        const int offset = (16 * stride) + 16;
        Span<byte> plane = variant == LibaomCase.VerticalRamp14
            ? MakeRampPlane(size, size)
            : variant.ToString().StartsWith("Vertical", StringComparison.Ordinal)
                ? (Span<byte>)MakeStepPlane(size, size, leftValue: 120, rightValue: 130)
                : MakeHorizontalStepPlane(size, size, topValue: 120, bottomValue: 130);

        switch (variant)
        {
            case LibaomCase.VerticalStep4:
                Av1LoopFilterPrimitives.LpfVertical4(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
                break;
            case LibaomCase.VerticalStep6:
                Av1LoopFilterPrimitives.LpfVertical6(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
                break;
            case LibaomCase.VerticalStep8:
                Av1LoopFilterPrimitives.LpfVertical8(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
                break;
            case LibaomCase.VerticalStep14:
            case LibaomCase.VerticalRamp14:
                Av1LoopFilterPrimitives.LpfVertical14(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
                break;
            case LibaomCase.HorizontalStep4:
                Av1LoopFilterPrimitives.LpfHorizontal4(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
                break;
            case LibaomCase.HorizontalStep6:
                Av1LoopFilterPrimitives.LpfHorizontal6(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
                break;
            case LibaomCase.HorizontalStep8:
                Av1LoopFilterPrimitives.LpfHorizontal8(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
                break;
            case LibaomCase.HorizontalStep14:
                Av1LoopFilterPrimitives.LpfHorizontal14(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
                break;
        }

        byte[] expected = variant switch
        {
            LibaomCase.VerticalStep4 => VerticalStep4Ref,
            LibaomCase.VerticalStep6 => VerticalStep6Ref,
            LibaomCase.VerticalStep8 => VerticalStep8Ref,
            LibaomCase.VerticalStep14 => VerticalStep14Ref,
            LibaomCase.VerticalRamp14 => VerticalRamp14Ref,
            LibaomCase.HorizontalStep4 => HorizontalStep4Ref,
            LibaomCase.HorizontalStep6 => HorizontalStep6Ref,
            LibaomCase.HorizontalStep8 => HorizontalStep8Ref,
            LibaomCase.HorizontalStep14 => HorizontalStep14Ref,
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        byte[] actual = ExtractWindow(plane, stride, variant);
        Assert.Equal(expected, actual);
    }

    private static byte[] ExtractWindow(ReadOnlySpan<byte> plane, int stride, LibaomCase variant)
    {
        bool isVertical = variant.ToString().StartsWith("Vertical", StringComparison.Ordinal);
        if (isVertical)
        {
            byte[] window = new byte[4 * 14];
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 14; col++)
                {
                    window[(row * 14) + col] = plane[((16 + row) * stride) + (16 - 7 + col)];
                }
            }

            return window;
        }
        else
        {
            byte[] window = new byte[14 * 4];
            for (int row = 0; row < 14; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    window[(row * 4) + col] = plane[((16 - 7 + row) * stride) + (16 + col)];
                }
            }

            return window;
        }
    }

    private static byte[] MakeStepPlane(int width, int height, byte leftValue, byte rightValue)
    {
        byte[] plane = new byte[width * height];
        int half = width / 2;
        for (int row = 0; row < height; row++)
        {
            int rowOffset = row * width;
            for (int col = 0; col < half; col++)
            {
                plane[rowOffset + col] = leftValue;
            }

            for (int col = half; col < width; col++)
            {
                plane[rowOffset + col] = rightValue;
            }
        }

        return plane;
    }

    private static byte[] MakeHorizontalStepPlane(int width, int height, byte topValue, byte bottomValue)
    {
        byte[] plane = new byte[width * height];
        int half = height / 2;
        for (int row = 0; row < half; row++)
        {
            for (int col = 0; col < width; col++)
            {
                plane[(row * width) + col] = topValue;
            }
        }

        for (int row = half; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                plane[(row * width) + col] = bottomValue;
            }
        }

        return plane;
    }

    private static byte[] MakeRampPlane(int width, int height)
    {
        // Smooth ramp on each side of the centre column so flat masks succeed and
        // the wide-filter paths get exercised. Mirrors make_ramp() in the libaom dumper.
        byte[] plane = new byte[width * height];
        int half = width / 2;
        for (int row = 0; row < height; row++)
        {
            int rowOffset = row * width;
            for (int col = 0; col < width; col++)
            {
                int fromEdge = col < half ? half - 1 - col : col - half;
                plane[rowOffset + col] = col < half ? (byte)(120 - fromEdge) : (byte)(130 + fromEdge);
            }
        }

        return plane;
    }
}
