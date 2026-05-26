// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1LoopFilterTests
{
    private const byte StrongMbLimit = 200;
    private const byte StrongLimit = 200;
    private const byte HevThreshold = 7;

    [Fact]
    public void Lpf_FlatPixels_AreUnchanged()
    {
        // A flat plane has zero gradient on every checked pair, so the mask is all-zero
        // and pixels must round-trip untouched even with permissive thresholds.
        Span<byte> plane = AllocPlane(32, 32, fill: 128);
        Span<byte> expected = plane.ToArray();
        int stride = 32;
        int offset = (16 * stride) + 16;

        Av1LoopFilterPrimitives.LpfHorizontal4(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Assert.True(plane.SequenceEqual(expected));

        Av1LoopFilterPrimitives.LpfVertical4(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Assert.True(plane.SequenceEqual(expected));

        Av1LoopFilterPrimitives.LpfHorizontal8(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Assert.True(plane.SequenceEqual(expected));

        Av1LoopFilterPrimitives.LpfVertical14(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Assert.True(plane.SequenceEqual(expected));
    }

    [Fact]
    public void Lpf_BlimitZero_DisablesFilter()
    {
        // FilterMask requires |p0-q0|*2 + |p1-q1|/2 > blimit; with blimit=0 a sharp step
        // satisfies this and the mask becomes ~(-1)=0, so no pixels should change.
        Span<byte> plane = MakeStepPlane(32, 32, leftValue: 50, rightValue: 200);
        byte[] expected = plane.ToArray();
        int stride = 32;
        int offset = (16 * stride) + 16;

        Av1LoopFilterPrimitives.LpfVertical4(plane, offset, stride, blimit: 0, limit: 200, thresh: HevThreshold);

        Assert.True(plane.SequenceEqual(expected));
    }

    [Fact]
    public void Lpf_LimitZero_DisablesFilter()
    {
        // limit=0 fails |p1-p0|>limit on any non-flat input, masking the filter off.
        Span<byte> plane = MakeStepPlane(32, 32, leftValue: 50, rightValue: 200);
        byte[] expected = plane.ToArray();
        int stride = 32;
        int offset = (16 * stride) + 16;

        Av1LoopFilterPrimitives.LpfVertical4(plane, offset, stride, blimit: 200, limit: 0, thresh: HevThreshold);

        Assert.True(plane.SequenceEqual(expected));
    }

    [Fact]
    public void LpfVertical4_SoftStep_IsSmoothed()
    {
        // A small vertical step inside the limit thresholds must be filtered:
        // pixels strictly inside the 4-tap window should move toward each other.
        Span<byte> plane = MakeStepPlane(32, 32, leftValue: 120, rightValue: 130);
        int stride = 32;
        int col = 16;
        int row = 8;
        int offset = (row * stride) + col;

        // Snapshot values immediately straddling the edge before filtering.
        byte p0Before = plane[offset - 1];
        byte q0Before = plane[offset];

        Av1LoopFilterPrimitives.LpfVertical4(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);

        byte p0After = plane[offset - 1];
        byte q0After = plane[offset];

        // After filtering, the step must shrink: p0 should rise toward q0, q0 should fall toward p0.
        Assert.True(p0After >= p0Before, $"p0: {p0Before} -> {p0After} (expected non-decreasing)");
        Assert.True(q0After <= q0Before, $"q0: {q0Before} -> {q0After} (expected non-increasing)");
        Assert.True(q0After - p0After < q0Before - p0Before, "edge magnitude should shrink");
    }

    [Fact]
    public void LpfHorizontal4_SoftStep_IsSmoothed()
    {
        Span<byte> plane = MakeHorizontalStepPlane(32, 32, topValue: 120, bottomValue: 130);
        int stride = 32;
        int col = 8;
        int row = 16;
        int offset = (row * stride) + col;

        byte p0Before = plane[offset - stride];
        byte q0Before = plane[offset];

        Av1LoopFilterPrimitives.LpfHorizontal4(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);

        Assert.True(plane[offset - stride] >= p0Before);
        Assert.True(plane[offset] <= q0Before);
    }

    [Fact]
    public void Lpf_VerticalAndHorizontal_AreTranspositionallySymmetric()
    {
        // Filtering a vertical edge in a step image should produce the same column as
        // filtering a horizontal edge in the transposed image.
        const int size = 32;
        Span<byte> v = MakeStepPlane(size, size, leftValue: 110, rightValue: 140);
        byte[] h = TransposePlane(v, size);

        int stride = size;
        int row = 8;
        int offset = (row * stride) + 16;

        Av1LoopFilterPrimitives.LpfVertical4(v, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);

        // Transposed equivalent: edge at row=16 col=8.
        int hOffset = (16 * stride) + 8;
        Av1LoopFilterPrimitives.LpfHorizontal4(h, hOffset, stride, StrongMbLimit, StrongLimit, HevThreshold);

        // Verify the 4 filtered pixels along the edge match across the two orientations.
        for (int i = 0; i < 4; i++)
        {
            byte vCenterMinus1 = v[((row + i) * stride) + 15];
            byte vCenter = v[((row + i) * stride) + 16];
            byte hCenterMinus1 = h[(15 * stride) + (8 + i)];
            byte hCenter = h[(16 * stride) + (8 + i)];
            Assert.Equal(vCenterMinus1, hCenterMinus1);
            Assert.Equal(vCenter, hCenter);
        }
    }

    [Fact]
    public void Lpf_AllVariants_DoNotEscape6PixelGuard()
    {
        // Sanity: the longest filter touches 7 pixels on each side of the edge, so a 32-wide
        // plane with offset at column 16 must never read or write outside the array bounds.
        Span<byte> plane = MakeStepPlane(32, 32, leftValue: 100, rightValue: 150);
        int stride = 32;
        int offset = (16 * stride) + 16;

        Av1LoopFilterPrimitives.LpfVertical14(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Av1LoopFilterPrimitives.LpfHorizontal14(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Av1LoopFilterPrimitives.LpfVertical6(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Av1LoopFilterPrimitives.LpfHorizontal6(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Av1LoopFilterPrimitives.LpfVertical8(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
        Av1LoopFilterPrimitives.LpfHorizontal8(plane, offset, stride, StrongMbLimit, StrongLimit, HevThreshold);
    }

    [Fact]
    public void Context_Initialize_SetsThresholdsForAllLevels()
    {
        ObuLoopFilterParameters parameters = new();
        parameters.FilterLevel[0] = 32;
        parameters.SharpnessLevel = 0;
        Av1LoopFilterContext context = new(parameters);

        // Sharpness 0: blockInsideLimit = lvl >> 0 = lvl, clamped to >= 1.
        // mblim = 2*(lvl+2) + lim. hev_thr = lvl >> 4.
        for (int lvl = 0; lvl <= Av1LoopFilterContext.MaxLoopFilter; lvl++)
        {
            Av1LoopFilterThreshold t = context.GetThreshold(lvl);
            int expectedLim = Math.Max(1, lvl);
            int expectedMbLim = (2 * (lvl + 2)) + expectedLim;
            Assert.Equal((byte)expectedLim, t.Limit);
            Assert.Equal((byte)expectedMbLim, t.MbLimit);
            Assert.Equal((byte)(lvl >> 4), t.HevThreshold);
        }
    }

    [Fact]
    public void Context_Sharpness_TightensInnerLimit()
    {
        // Higher sharpness should tighten the inner threshold (at high levels) so the filter
        // smooths fewer pixels. Compare lvl=63 across two sharpness values.
        ObuLoopFilterParameters relaxedParameters = new();
        relaxedParameters.FilterLevel[0] = 63;
        relaxedParameters.SharpnessLevel = 0;
        Av1LoopFilterContext relaxed = new(relaxedParameters);

        ObuLoopFilterParameters sharpParameters = new();
        sharpParameters.FilterLevel[0] = 63;
        sharpParameters.SharpnessLevel = 7;
        Av1LoopFilterContext sharp = new(sharpParameters);

        byte relaxedLimit = relaxed.GetThreshold(63).Limit;
        byte sharpLimit = sharp.GetThreshold(63).Limit;
        Assert.True(sharpLimit < relaxedLimit, $"sharpness should tighten Limit: relaxed={relaxedLimit} sharp={sharpLimit}");
    }

    [Fact]
    public void Decoder_NoOpsWhenAllFilterLevelsAreZero()
    {
        // Given filter levels = 0 the spec says the LF stage MUST be a no-op. Even when
        // doLoopFilterFlag=true, the decoder should bail out without dereferencing state.
        ObuSequenceHeader sequenceHeader = MinimalSequenceHeader();
        ObuFrameHeader frameHeader = MinimalFrameHeader();
        frameHeader.LoopFilterParameters.FilterLevel[0] = 0;
        frameHeader.LoopFilterParameters.FilterLevel[1] = 0;
        frameHeader.LoopFilterParameters.FilterLevelU = 0;
        frameHeader.LoopFilterParameters.FilterLevelV = 0;

        // Passing nulls for frameInfo/frameBuffer asserts the early-return doesn't touch them:
        // any access would surface as NullReferenceException.
        Av1LoopFilterDecoder decoder = new(sequenceHeader, frameHeader, frameInfo: null!, frameBuffer: null!);
        Exception? thrown = Record.Exception(() => decoder.DecodeFrame());
        Assert.Null(thrown);
    }

    private static byte[] AllocPlane(int width, int height, byte fill)
    {
        byte[] plane = new byte[width * height];
        Array.Fill(plane, fill);
        return plane;
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

    private static byte[] TransposePlane(ReadOnlySpan<byte> plane, int size)
    {
        byte[] transposed = new byte[plane.Length];
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                transposed[(col * size) + row] = plane[(row * size) + col];
            }
        }

        return transposed;
    }

    private static ObuSequenceHeader MinimalSequenceHeader()
        => new()
        {
            ColorConfig = new ObuColorConfig
            {
                IsMonochrome = true,
            },
        };

    private static ObuFrameHeader MinimalFrameHeader()
    {
        ObuFrameHeader header = new()
        {
            LoopFilterParameters = new ObuLoopFilterParameters(),
        };
        return header;
    }
}
