// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Prediction.ChromaFromLuma;

internal class Av1ChromaFromLumaContext
{
    private const int BufferLine = 32;

    private int bufferHeight;
    private int bufferWidth;
    private readonly bool subX;
    private readonly bool subY;

    public Av1ChromaFromLumaContext(Configuration configuration, ObuColorConfig colorConfig)
    {
        this.subX = colorConfig.SubSamplingX;
        this.subY = colorConfig.SubSamplingY;
        this.Q3Buffer = configuration.MemoryAllocator.Allocate2D<short>(new Size(32, 32), AllocationOptions.Clean);
    }

    public Buffer2D<short> Q3Buffer { get; private set; }

    public bool AreParametersComputed { get; private set; }

    public void Reset()
    {
        this.Q3Buffer.DangerousGetSingleSpan().Clear();
        this.bufferWidth = 0;
        this.bufferHeight = 0;
        this.AreParametersComputed = false;
    }

    public void InvalidateParameters() => this.AreParametersComputed = false;

    /// <summary>
    /// SVT/libaom: cfl_store_tx. Subsamples a luma transform block's reconstructed pixels
    /// into the Q3 buffer at the chroma offset corresponding to (row, col).
    /// </summary>
    public void StoreLuma(Span<byte> luma, int lumaStride, int row, int col, Av1TransformSize transformSize)
    {
        int width = transformSize.GetWidth();
        int height = transformSize.GetHeight();
        int subXShift = this.subX ? 1 : 0;
        int subYShift = this.subY ? 1 : 0;
        int storeRow = row << (Av1Constants.ModeInfoSizeLog2 - subYShift);
        int storeCol = col << (Av1Constants.ModeInfoSizeLog2 - subXShift);
        int storeHeight = height >> subYShift;
        int storeWidth = width >> subXShift;

        this.AreParametersComputed = false;

        if (col == 0 && row == 0)
        {
            this.bufferWidth = storeWidth;
            this.bufferHeight = storeHeight;
        }
        else
        {
            this.bufferWidth = Math.Max(storeCol + storeWidth, this.bufferWidth);
            this.bufferHeight = Math.Max(storeRow + storeHeight, this.bufferHeight);
        }

        DebugGuard.MustBeLessThanOrEqualTo(storeRow + storeHeight, BufferLine, nameof(storeHeight));
        DebugGuard.MustBeLessThanOrEqualTo(storeCol + storeWidth, BufferLine, nameof(storeWidth));

        ref short destinationStart = ref this.Q3Buffer[storeCol, storeRow];
        if (this.subX && this.subY)
        {
            SubsampleLuma420(luma, lumaStride, ref destinationStart, width, height);
        }
        else if (this.subX)
        {
            SubsampleLuma422(luma, lumaStride, ref destinationStart, width, height);
        }
        else
        {
            SubsampleLuma444(luma, lumaStride, ref destinationStart, width, height);
        }
    }

    private static void SubsampleLuma420(Span<byte> input, int inputStride, ref short output, int width, int height)
    {
        ref short writePtr = ref output;
        for (int j = 0; j < height; j += 2)
        {
            int rowOffset = j * inputStride;
            for (int i = 0; i < width; i += 2)
            {
                int top = rowOffset + i;
                int bottom = top + inputStride;
                int sum = input[top] + input[top + 1] + input[bottom] + input[bottom + 1];
                Unsafe.Add(ref writePtr, i >> 1) = (short)(sum << 1);
            }

            writePtr = ref Unsafe.Add(ref writePtr, BufferLine);
        }
    }

    private static void SubsampleLuma422(Span<byte> input, int inputStride, ref short output, int width, int height)
    {
        ref short writePtr = ref output;
        for (int j = 0; j < height; j++)
        {
            int rowOffset = j * inputStride;
            for (int i = 0; i < width; i += 2)
            {
                int sum = input[rowOffset + i] + input[rowOffset + i + 1];
                Unsafe.Add(ref writePtr, i >> 1) = (short)(sum << 2);
            }

            writePtr = ref Unsafe.Add(ref writePtr, BufferLine);
        }
    }

    private static void SubsampleLuma444(Span<byte> input, int inputStride, ref short output, int width, int height)
    {
        ref short writePtr = ref output;
        for (int j = 0; j < height; j++)
        {
            int rowOffset = j * inputStride;
            for (int i = 0; i < width; i++)
            {
                Unsafe.Add(ref writePtr, i) = (short)(input[rowOffset + i] << 3);
            }

            writePtr = ref Unsafe.Add(ref writePtr, BufferLine);
        }
    }

    public void ComputeParameters(Av1TransformSize transformSize)
    {
        Guard.IsFalse(this.AreParametersComputed, nameof(this.AreParametersComputed), "Do not call cfl_compute_parameters multiple time on the same values.");
        this.Pad(transformSize.GetWidth(), transformSize.GetHeight());
        SubtractAverage(ref this.Q3Buffer[0, 0], transformSize);
        this.AreParametersComputed = true;
    }

    private void Pad(int width, int height)
    {
        int diff_width = width - this.bufferWidth;
        int diff_height = height - this.bufferHeight;

        if (diff_width > 0)
        {
            int min_height = height - diff_height;
            ref short recon_buf_q3 = ref this.Q3Buffer[width - diff_width, 0];
            DebugGuard.MustBeLessThanOrEqualTo(width, BufferLine, nameof(width));
            for (int j = 0; j < min_height; j++)
            {
                short last_pixel = Unsafe.Subtract(ref recon_buf_q3, 1);
                for (int i = 0; i < diff_width; i++)
                {
                    Unsafe.Add(ref recon_buf_q3, i) = last_pixel;
                }

                recon_buf_q3 = ref Unsafe.Add(ref recon_buf_q3, BufferLine);
            }

            this.bufferWidth = width;
        }

        if (diff_height > 0)
        {
            ref short recon_buf_q3 = ref this.Q3Buffer[0, height - diff_height];
            DebugGuard.MustBeLessThanOrEqualTo(height, BufferLine, nameof(height));
            for (int j = 0; j < diff_height; j++)
            {
                ref short last_row_q3 = ref Unsafe.Subtract(ref recon_buf_q3, BufferLine);
                for (int i = 0; i < width; i++)
                {
                    Unsafe.Add(ref recon_buf_q3, i) = Unsafe.Add(ref last_row_q3, i);
                }

                recon_buf_q3 = ref Unsafe.Add(ref recon_buf_q3, BufferLine);
            }

            this.bufferHeight = height;
        }
    }

    /************************************************************************************************
    * svt_subtract_average_c
    * Calculate the DC value by averaging over all sample. Subtract DC value to get AC values In C
    ************************************************************************************************/
    private static void SubtractAverage(ref short pred_buf_q3, Av1TransformSize transformSize)
    {
        int width = transformSize.GetWidth();
        int height = transformSize.GetHeight();
        int roundOffset = (width * height) >> 1;
        int pelCountLog2 = transformSize.GetBlockWidthLog2() + transformSize.GetBlockHeightLog2();
        int sum_q3 = 0;
        ref short pred_buf = ref pred_buf_q3;
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                sum_q3 += Unsafe.Add(ref pred_buf, i);
            }

            pred_buf = ref Unsafe.Add(ref pred_buf, BufferLine);
        }

        int avg_q3 = (sum_q3 + roundOffset) >> pelCountLog2;
        ref short writePtr = ref pred_buf_q3;
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                Unsafe.Add(ref writePtr, i) -= (short)avg_q3;
            }

            writePtr = ref Unsafe.Add(ref writePtr, BufferLine);
        }
    }
}
