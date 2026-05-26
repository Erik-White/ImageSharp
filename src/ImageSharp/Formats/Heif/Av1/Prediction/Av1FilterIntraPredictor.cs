// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Prediction;

internal static class Av1FilterIntraPredictor
{
    private const int FilterIntraScaleBits = 4;
    private const int RoundOffset = 1 << (FilterIntraScaleBits - 1);

    // SVT/libaom: av1_filter_intra_taps[FILTER_INTRA_MODES][8][7]
    // The reference table is declared as [8][8] but the trailing column is always 0; we drop it.
    private static readonly sbyte[][][] FilterIntraTaps =
    [
        [
            [-6, 10, 0, 0, 0, 12, 0],
            [-5, 2, 10, 0, 0, 9, 0],
            [-3, 1, 1, 10, 0, 7, 0],
            [-3, 1, 1, 2, 10, 5, 0],
            [-4, 6, 0, 0, 0, 2, 12],
            [-3, 2, 6, 0, 0, 2, 9],
            [-3, 2, 2, 6, 0, 2, 7],
            [-3, 1, 2, 2, 6, 3, 5],
        ],
        [
            [-10, 16, 0, 0, 0, 10, 0],
            [-6, 0, 16, 0, 0, 6, 0],
            [-4, 0, 0, 16, 0, 4, 0],
            [-2, 0, 0, 0, 16, 2, 0],
            [-10, 16, 0, 0, 0, 0, 10],
            [-6, 0, 16, 0, 0, 0, 6],
            [-4, 0, 0, 16, 0, 0, 4],
            [-2, 0, 0, 0, 16, 0, 2],
        ],
        [
            [-8, 8, 0, 0, 0, 16, 0],
            [-8, 0, 8, 0, 0, 16, 0],
            [-8, 0, 0, 8, 0, 16, 0],
            [-8, 0, 0, 0, 8, 16, 0],
            [-4, 4, 0, 0, 0, 0, 16],
            [-4, 0, 4, 0, 0, 0, 16],
            [-4, 0, 0, 4, 0, 0, 16],
            [-4, 0, 0, 0, 4, 0, 16],
        ],
        [
            [-2, 8, 0, 0, 0, 10, 0],
            [-1, 3, 8, 0, 0, 6, 0],
            [-1, 2, 3, 8, 0, 4, 0],
            [0, 1, 2, 3, 8, 2, 0],
            [-1, 4, 0, 0, 0, 3, 10],
            [-1, 3, 4, 0, 0, 4, 6],
            [-1, 2, 3, 4, 0, 4, 4],
            [-1, 2, 2, 3, 4, 3, 3],
        ],
        [
            [-12, 14, 0, 0, 0, 14, 0],
            [-10, 0, 14, 0, 0, 12, 0],
            [-9, 0, 0, 14, 0, 11, 0],
            [-8, 0, 0, 0, 14, 10, 0],
            [-10, 12, 0, 0, 0, 0, 14],
            [-9, 1, 12, 0, 0, 0, 12],
            [-8, 0, 0, 12, 0, 1, 11],
            [-7, 0, 0, 1, 12, 1, 9],
        ],
    ];

    /// <summary>
    /// SVT: svt_av1_filter_intra_predictor_c
    /// </summary>
    public static void PredictScalar(Av1TransformSize transformSize, Span<byte> destination, nuint destinationStride, Span<byte> aboveRow, Span<byte> leftColumn, Av1FilterIntraMode mode)
    {
        int blockWidth = transformSize.GetWidth();
        int blockHeight = transformSize.GetHeight();
        Guard.MustBeLessThanOrEqualTo(blockWidth, 32, nameof(blockWidth));
        Guard.MustBeLessThanOrEqualTo(blockHeight, 32, nameof(blockHeight));

        const int stride = 33;
        Span<byte> buffer = stackalloc byte[stride * stride];

        for (int r = 0; r < blockHeight; r++)
        {
            buffer[((r + 1) * stride) + 0] = leftColumn[r];
        }

        buffer[0] = Unsafe.Subtract(ref aboveRow[0], 1);
        for (int c = 0; c < blockWidth; c++)
        {
            buffer[c + 1] = aboveRow[c];
        }

        sbyte[][] modeTaps = FilterIntraTaps[(int)mode];
        for (int r = 1; r < blockHeight + 1; r += 2)
        {
            for (int c = 1; c < blockWidth + 1; c += 4)
            {
                int aboveBase = ((r - 1) * stride) + c;
                byte p0 = buffer[aboveBase - 1];
                byte p1 = buffer[aboveBase];
                byte p2 = buffer[aboveBase + 1];
                byte p3 = buffer[aboveBase + 2];
                byte p4 = buffer[aboveBase + 3];
                byte p5 = buffer[(r * stride) + c - 1];
                byte p6 = buffer[((r + 1) * stride) + c - 1];

                for (int k = 0; k < 8; k++)
                {
                    sbyte[] taps = modeTaps[k];
                    int pr = (taps[0] * p0)
                        + (taps[1] * p1)
                        + (taps[2] * p2)
                        + (taps[3] * p3)
                        + (taps[4] * p4)
                        + (taps[5] * p5)
                        + (taps[6] * p6);

                    int rounded = (pr + RoundOffset) >> FilterIntraScaleBits;
                    int clipped = Math.Clamp(rounded, 0, 255);
                    int rOffset = k >> 2;
                    int cOffset = k & 0x03;
                    buffer[((r + rOffset) * stride) + c + cOffset] = (byte)clipped;
                }
            }
        }

        ref byte destinationRef = ref destination[0];
        for (int r = 0; r < blockHeight; r++)
        {
            ref byte source = ref buffer[((r + 1) * stride) + 1];
            Unsafe.CopyBlock(ref destinationRef, ref source, (uint)blockWidth);
            destinationRef = ref Unsafe.Add(ref destinationRef, destinationStride);
        }
    }
}
