// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.ComponentModel;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

internal class Av1Inverse2dTransformer
{
    private const int UnitQuantizationShift = 2;

    /// <summary>
    /// SVT: inv_txfm2d_add_c
    /// </summary>
    internal static void Transform2dAdd(
        Span<int> input,
        Span<short> outputForRead,
        int strideForRead,
        Span<short> outputForWrite,
        int strideForWrite,
        Av1Transform2dFlipConfiguration config,
        Span<int> transformFunctionBuffer,
        int bitDepth)
    {
        // Note when assigning txfm_size_col, we use the txfm_size from the
        // row configuration and vice versa. This is intentionally done to
        // accurately perform rectangular transforms. When the transform is
        // rectangular, the number of columns will be the same as the
        // txfm_size stored in the row cfg struct. It will make no difference
        // for square transforms.
        int transformWidth = config.TransformSize.GetWidth();
        int transformHeight = config.TransformSize.GetHeight();

        // Take the shift from the larger dimension in the rectangular case.
        Span<int> shift = config.Shift;
        int rectangleType = config.TransformSize.GetRectangleLogRatio();
        config.GenerateStageRange(bitDepth);

        int cosBitColumn = config.CosBitColumn;
        int cosBitRow = config.CosBitRow;
        IAv1Transformer1d? functionColumn = Av1InverseTransformerFactory.GetTransformer(config.TransformFunctionTypeColumn);
        IAv1Transformer1d? functionRow = Av1InverseTransformerFactory.GetTransformer(config.TransformFunctionTypeRow);
        Guard.NotNull(functionColumn);
        Guard.NotNull(functionRow);

        // txfm_buf's length is  txfm_size_row * txfm_size_col + 2 * MAX(txfm_size_row, txfm_size_col)
        // it is used for intermediate data buffering
        int bufferOffset = Math.Max(transformHeight, transformWidth);
        Guard.MustBeSizedAtLeast(transformFunctionBuffer, (transformHeight * transformWidth) + (2 * bufferOffset), nameof(transformFunctionBuffer));
        Span<int> tempIn = transformFunctionBuffer;
        Span<int> tempOut = tempIn.Slice(bufferOffset);
        Span<int> buf = tempOut.Slice(bufferOffset);
        Span<int> bufPtr = buf;
        int c, r;

        // Rows
        for (r = 0; r < transformHeight; ++r)
        {
            if (Math.Abs(rectangleType) == 1)
            {
                for (c = 0; c < transformWidth; ++c)
                {
                    tempIn[c] = Av1Math.RoundShift((long)input[c] * Av1InverseTransformMath.NewInverseSqrt2, Av1InverseTransformMath.NewSqrt2BitCount);
                }

                Av1InverseTransformMath.ClampBuffer(tempIn, transformWidth, (byte)(bitDepth + 8));
                functionRow.Transform(tempIn, bufPtr, cosBitRow, config.StageRangeRow);
            }
            else
            {
                for (c = 0; c < transformWidth; ++c)
                {
                    tempIn[c] = input[c];
                }

                Av1InverseTransformMath.ClampBuffer(tempIn, transformWidth, (byte)(bitDepth + 8));
                functionRow.Transform(tempIn, bufPtr, cosBitRow, config.StageRangeRow);
            }

            Av1InverseTransformMath.RoundShiftArray(bufPtr, transformWidth, -shift[0]);
            input = input[transformWidth..];
            bufPtr = bufPtr.Slice(transformWidth);
        }

        // Columns
        for (c = 0; c < transformWidth; ++c)
        {
            if (!config.FlipLeftToRight)
            {
                int t = c;
                for (r = 0; r < transformHeight; ++r)
                {
                    tempIn[r] = buf[t];
                    t += transformWidth;
                }
            }
            else
            {
                // flip left right
                int t = transformWidth - c - 1;
                for (r = 0; r < transformHeight; ++r)
                {
                    tempIn[r] = buf[t];
                    t += transformWidth;
                }
            }

            Av1InverseTransformMath.ClampBuffer(tempIn, transformHeight, (byte)Math.Max(bitDepth + 6, 16));
            functionColumn.Transform(tempIn, tempOut, cosBitColumn, config.StageRangeColumn);
            Av1InverseTransformMath.RoundShiftArray(tempOut, transformHeight, -shift[1]);
            if (!config.FlipUpsideDown)
            {
                int indexForWrite = c;
                int indexForRead = c;
                for (r = 0; r < transformHeight; ++r)
                {
                    outputForWrite[indexForWrite] =
                        Av1InverseTransformMath.ClipPixelAdd(outputForRead[indexForRead], tempOut[r], bitDepth);
                    indexForWrite += strideForWrite;
                    indexForRead += strideForRead;
                }
            }
            else
            {
                // flip upside down
                int indexForWrite = c;
                int indexForRead = c;
                int indexTemp = transformHeight - 1;
                for (r = 0; r < transformHeight; ++r)
                {
                    outputForWrite[indexForWrite] = Av1InverseTransformMath.ClipPixelAdd(
                        outputForRead[indexForRead], tempOut[indexTemp], bitDepth);
                    indexForWrite += strideForWrite;
                    indexForRead += strideForRead;
                    indexTemp--;
                }
            }
        }
    }

    /// <summary>
    /// SVT: inv_txfm2d_add_c
    /// </summary>
    internal static void Transform2dAdd(
        Span<int> input,
        Span<byte> outputForRead,
        int strideForRead,
        Span<byte> outputForWrite,
        int strideForWrite,
        Av1Transform2dFlipConfiguration config,
        Span<int> transformFunctionBuffer)
    {
        const int bitDepth = 8;

        // Note when assigning txfm_size_col, we use the txfm_size from the
        // row configuration and vice versa. This is intentionally done to
        // accurately perform rectangular transforms. When the transform is
        // rectangular, the number of columns will be the same as the
        // txfm_size stored in the row cfg struct. It will make no difference
        // for square transforms.
        int transformWidth = config.TransformSize.GetWidth();
        int transformHeight = config.TransformSize.GetHeight();

        // Take the shift from the larger dimension in the rectangular case.
        Span<int> shift = config.Shift;
        int rectangleType = config.TransformSize.GetRectangleLogRatio();
        config.GenerateStageRange(bitDepth);

        int cosBitColumn = config.CosBitColumn;
        int cosBitRow = config.CosBitRow;
        IAv1Transformer1d? functionColumn = Av1InverseTransformerFactory.GetTransformer(config.TransformFunctionTypeColumn);
        IAv1Transformer1d? functionRow = Av1InverseTransformerFactory.GetTransformer(config.TransformFunctionTypeRow);
        Guard.NotNull(functionColumn);
        Guard.NotNull(functionRow);

        // txfm_buf's length is  txfm_size_row * txfm_size_col + 2 * MAX(txfm_size_row, txfm_size_col)
        // it is used for intermediate data buffering
        int bufferOffset = Math.Max(transformHeight, transformWidth);
        Guard.MustBeSizedAtLeast(transformFunctionBuffer, (transformHeight * transformWidth) + (2 * bufferOffset), nameof(transformFunctionBuffer));
        Span<int> tempIn = transformFunctionBuffer;
        Span<int> tempOut = tempIn.Slice(bufferOffset);
        Span<int> buf = tempOut.Slice(bufferOffset);
        Span<int> bufPtr = buf;
        int c, r;

        // Rows
        for (r = 0; r < transformHeight; ++r)
        {
            if (Math.Abs(rectangleType) == 1)
            {
                for (c = 0; c < transformWidth; ++c)
                {
                    tempIn[c] = Av1Math.RoundShift((long)input[c] * Av1InverseTransformMath.NewInverseSqrt2, Av1InverseTransformMath.NewSqrt2BitCount);
                }

                Av1InverseTransformMath.ClampBuffer(tempIn, transformWidth, (byte)(bitDepth + 8));
                functionRow.Transform(tempIn, bufPtr, cosBitRow, config.StageRangeRow);
            }
            else
            {
                for (c = 0; c < transformWidth; ++c)
                {
                    tempIn[c] = input[c];
                }

                Av1InverseTransformMath.ClampBuffer(tempIn, transformWidth, (byte)(bitDepth + 8));
                functionRow.Transform(tempIn, bufPtr, cosBitRow, config.StageRangeRow);
            }

            Av1InverseTransformMath.RoundShiftArray(bufPtr, transformWidth, -shift[0]);
            input = input[transformWidth..];
            bufPtr = bufPtr[transformWidth..];
        }

        // Columns
        for (c = 0; c < transformWidth; ++c)
        {
            if (!config.FlipLeftToRight)
            {
                for (r = 0; r < transformHeight; ++r)
                {
                    tempIn[r] = buf[(r * transformWidth) + c];
                }
            }
            else
            {
                // flip left right
                for (r = 0; r < transformHeight; ++r)
                {
                    tempIn[r] = buf[(r * transformWidth) + (transformWidth - c - 1)];
                }
            }

            Av1InverseTransformMath.ClampBuffer(tempIn, transformHeight, (byte)Math.Max(bitDepth + 6, 16));
            functionColumn.Transform(tempIn, tempOut, cosBitColumn, config.StageRangeColumn);
            Av1InverseTransformMath.RoundShiftArray(tempOut, transformHeight, -shift[1]);
            if (!config.FlipUpsideDown)
            {
                for (r = 0; r < transformHeight; ++r)
                {
                    outputForWrite[(r * strideForWrite) + c] =
                        Av1InverseTransformMath.ClipPixelAdd(outputForRead[(r * strideForRead) + c], tempOut[r]);
                }
            }
            else
            {
                // flip upside down
                for (r = 0; r < transformHeight; ++r)
                {
                    outputForWrite[(r * strideForWrite) + c] = Av1InverseTransformMath.ClipPixelAdd(
                        outputForRead[(r * strideForRead) + c], tempOut[transformHeight - r - 1]);
                }
            }
        }
    }

    /// <summary>
    /// libaom <c>av1_highbd_iwht4x4_add</c>: lossless 4x4 inverse Walsh-Hadamard, dispatched
    /// by EOB. Input is signed transform coefficients; the 1-coeff variant covers the
    /// DC-only case the encoder emits when all but the DC coefficient are zero.
    /// </summary>
    internal static void InverseWalshHadamard4x4Add(Span<int> input, Span<byte> destination, int stride, int endOfBuffer)
    {
        if (endOfBuffer > 1)
        {
            InverseWalshHadamard4x4Add16(input, destination, stride);
        }
        else
        {
            InverseWalshHadamard4x4Add1(input, destination, stride);
        }
    }

    /// <summary>
    /// libaom <c>av1_highbd_iwht4x4_16_add_c</c>: 4-point reversible, orthonormal inverse
    /// Walsh-Hadamard in 3.5 adds, 0.5 shifts per pixel. Lossless transform used when
    /// <c>frame_header.coded_lossless</c> is set; pairs with the matching forward WHT in
    /// the encoder.
    /// </summary>
    private static void InverseWalshHadamard4x4Add16(Span<int> input, Span<byte> destination, int stride)
    {
        Span<int> output = stackalloc int[16];
        int a1, b1, c1, d1, e1;

        for (int i = 0; i < 4; i++)
        {
            a1 = input[i] >> UnitQuantizationShift;
            c1 = input[i + 4] >> UnitQuantizationShift;
            d1 = input[i + 8] >> UnitQuantizationShift;
            b1 = input[i + 12] >> UnitQuantizationShift;
            a1 += c1;
            d1 -= b1;
            e1 = (a1 - d1) >> 1;
            b1 = e1 - b1;
            c1 = e1 - c1;
            a1 -= b1;
            d1 += c1;

            output[i] = a1;
            output[i + 4] = b1;
            output[i + 8] = c1;
            output[i + 12] = d1;
        }

        for (int i = 0; i < 4; i++)
        {
            int rowOffset = i * 4;
            a1 = output[rowOffset];
            c1 = output[rowOffset + 1];
            d1 = output[rowOffset + 2];
            b1 = output[rowOffset + 3];
            a1 += c1;
            d1 -= b1;
            e1 = (a1 - d1) >> 1;
            b1 = e1 - b1;
            c1 = e1 - c1;
            a1 -= b1;
            d1 += c1;

            int rowBase = i * stride;
            destination[rowBase] = Av1InverseTransformMath.ClipPixelAdd(destination[rowBase], a1);
            destination[rowBase + 1] = Av1InverseTransformMath.ClipPixelAdd(destination[rowBase + 1], b1);
            destination[rowBase + 2] = Av1InverseTransformMath.ClipPixelAdd(destination[rowBase + 2], c1);
            destination[rowBase + 3] = Av1InverseTransformMath.ClipPixelAdd(destination[rowBase + 3], d1);
        }
    }

    /// <summary>
    /// libaom <c>av1_highbd_iwht4x4_1_add_c</c>: DC-only fast path (EOB == 1). Only the
    /// top-left coefficient is non-zero, so the row pass is a single butterfly and the
    /// column pass produces a checkerboard-of-two-values pattern.
    /// </summary>
    private static void InverseWalshHadamard4x4Add1(Span<int> input, Span<byte> destination, int stride)
    {
        Span<int> tmp = stackalloc int[4];
        int a1 = input[0] >> UnitQuantizationShift;
        int e1 = a1 >> 1;
        a1 -= e1;
        tmp[0] = a1;
        tmp[1] = tmp[2] = tmp[3] = e1;

        for (int i = 0; i < 4; i++)
        {
            int v = tmp[i];
            int eCol = v >> 1;
            int aCol = v - eCol;
            int destinationIndex = i;
            destination[destinationIndex] = Av1InverseTransformMath.ClipPixelAdd(destination[destinationIndex], aCol);
            destination[destinationIndex + stride] = Av1InverseTransformMath.ClipPixelAdd(destination[destinationIndex + stride], eCol);
            destination[destinationIndex + (2 * stride)] = Av1InverseTransformMath.ClipPixelAdd(destination[destinationIndex + (2 * stride)], eCol);
            destination[destinationIndex + (3 * stride)] = Av1InverseTransformMath.ClipPixelAdd(destination[destinationIndex + (3 * stride)], eCol);
        }
    }
}
