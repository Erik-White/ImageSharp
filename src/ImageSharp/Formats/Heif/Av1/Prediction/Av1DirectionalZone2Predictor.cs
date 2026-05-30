// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Prediction;

internal class Av1DirectionalZone2Predictor
{
    private readonly nuint blockWidth;
    private readonly nuint blockHeight;

    public Av1DirectionalZone2Predictor(Size blockSize)
    {
        this.blockWidth = (nuint)blockSize.Width;
        this.blockHeight = (nuint)blockSize.Height;
    }

    public Av1DirectionalZone2Predictor(Av1TransformSize transformSize)
    {
        this.blockWidth = (nuint)transformSize.GetWidth();
        this.blockHeight = (nuint)transformSize.GetHeight();
    }

    public static void PredictScalar(Av1TransformSize transformSize, Span<byte> destination, nuint stride, Span<byte> above, Span<byte> left, bool upsampleAbove, bool upsampleLeft, int dx, int dy)
        => new Av1DirectionalZone2Predictor(transformSize).PredictScalar(destination, stride, above, left, upsampleAbove, upsampleLeft, dx, dy);

    /// <summary>
    /// For each pixel the predictor first projects toward the above row; if that projection
    /// lands left of the available samples it re-projects toward the left column instead.
    /// The per-row projection is recomputed per pixel — there is no row-loop running offset like z1/z3.
    /// </summary>
    public void PredictScalar(Span<byte> destination, nuint stride, Span<byte> above, Span<byte> left, bool doUpsampleAbove, bool doUpsampleLeft, int dx, int dy)
    {
        Guard.MustBeGreaterThanOrEqualTo(stride, this.blockWidth, nameof(stride));
        Guard.MustBeSizedAtLeast(left, (int)this.blockHeight, nameof(left));
        Guard.MustBeSizedAtLeast(above, (int)this.blockWidth, nameof(above));
        Guard.MustBeSizedAtLeast(destination, (int)this.blockHeight * (int)stride, nameof(destination));
        int upsampleAbove = doUpsampleAbove ? 1 : 0;
        int upsampleLeft = doUpsampleLeft ? 1 : 0;
        ref byte aboveRef = ref above[0];
        ref byte leftRef = ref left[0];
        ref byte destinationRef = ref destination[0];
        int minBasisX = -(1 << upsampleAbove);
        int fractionBitCountX = 6 - upsampleAbove;
        int fractionBitCountY = 6 - upsampleLeft;

        for (int r = 0; r < (int)this.blockHeight; r++)
        {
            for (int c = 0; c < (int)this.blockWidth; c++)
            {
                int val;
                int y = r + 1;
                int x = (c << 6) - (y * dx);
                int baseX = x >> fractionBitCountX;
                if (baseX >= minBasisX)
                {
                    int shift = ((x * (1 << upsampleAbove)) & 0x3F) >> 1;
                    val = (Unsafe.Add(ref aboveRef, baseX) * (32 - shift)) + (Unsafe.Add(ref aboveRef, baseX + 1) * shift);
                    val = Av1Math.RoundPowerOf2(val, 5);
                }
                else
                {
                    int xn = c + 1;
                    int yn = (r << 6) - (xn * dy);
                    int baseY = yn >> fractionBitCountY;
                    int shift = ((yn * (1 << upsampleLeft)) & 0x3F) >> 1;
                    val = (Unsafe.Add(ref leftRef, baseY) * (32 - shift)) + (Unsafe.Add(ref leftRef, baseY + 1) * shift);
                    val = Av1Math.RoundPowerOf2(val, 5);
                }

                Unsafe.Add(ref destinationRef, c) = (byte)Av1Math.Clamp(val, 0, 255);
            }

            destinationRef = ref Unsafe.Add(ref destinationRef, stride);
        }
    }
}
