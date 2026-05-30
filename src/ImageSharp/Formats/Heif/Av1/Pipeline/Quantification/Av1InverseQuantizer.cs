// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Quantification;

internal class Av1InverseQuantizer
{
    private readonly ObuSequenceHeader sequenceHeader;
    private readonly ObuFrameHeader frameHeader;
    private Av1DeQuantizationContext deQuantsDeltaQ;

    public Av1InverseQuantizer(ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader)
    {
        this.sequenceHeader = sequenceHeader;
        this.frameHeader = frameHeader;
        this.deQuantsDeltaQ = new(sequenceHeader, frameHeader);
    }

    public void UpdateDequant(Av1DeQuantizationContext deQuants, Av1SuperblockInfo superblockInfo)
    {
        Av1BitDepth bitDepth = this.sequenceHeader.ColorConfig.BitDepth;
        Guard.NotNull(deQuants, nameof(deQuants));
        this.deQuantsDeltaQ = deQuants;
        if (this.frameHeader.DeltaQParameters.IsPresent)
        {
            for (int i = 0; i < Av1Constants.MaxSegmentCount; i++)
            {
                int currentQIndex = Av1QuantizationLookup.GetQIndex(this.frameHeader.SegmentationParameters, i, superblockInfo.SuperblockDeltaQ);

                for (Av1Plane plane = 0; (int)plane < Av1Constants.MaxPlanes; plane++)
                {
                    int dcDeltaQ = this.frameHeader.QuantizationParameters.DeltaQDc[(int)plane];
                    int acDeltaQ = this.frameHeader.QuantizationParameters.DeltaQAc[(int)plane];

                    this.deQuantsDeltaQ.SetDc(i, plane, Av1QuantizationLookup.GetDcQuant(currentQIndex, dcDeltaQ, bitDepth));
                    this.deQuantsDeltaQ.SetAc(i, plane, Av1QuantizationLookup.GetAcQuant(currentQIndex, acDeltaQ, bitDepth));
                }
            }
        }
    }

    /// <summary>
    /// SVT: svt_aom_inverse_quantize
    /// </summary>
    public int InverseQuantize(Av1BlockModeInfo mode, Span<int> level, Span<int> qCoefficients, Av1TransformType transformType, Av1TransformSize transformSize, Av1Plane plane)
    {
        Guard.NotNull(this.deQuantsDeltaQ);
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(transformSize, transformType);
        ReadOnlySpan<short> scanIndices = scanOrder.Scan;
        int maxValue = (1 << (7 + this.sequenceHeader.ColorConfig.BitDepth.GetBitCount())) - 1;
        int minValue = -(1 << (7 + this.sequenceHeader.ColorConfig.BitDepth.GetBitCount()));
        Av1TransformSize qmTransformSize = transformSize.GetAdjusted();
        bool usingQuantizationMatrix = this.frameHeader.QuantizationParameters.IsUsingQMatrix;
        bool lossless = this.frameHeader.LosslessArray[mode.SegmentId];
        short dequantDc = this.deQuantsDeltaQ.GetDc(mode.SegmentId, plane);
        short dequantAc = this.deQuantsDeltaQ.GetAc(mode.SegmentId, plane);
        int qmLevel = lossless || !usingQuantizationMatrix ? Av1ScanOrderConstants.QuantizationMatrixLevelCount - 1 : this.frameHeader.QuantizationParameters.QMatrix[(int)plane];
        ReadOnlySpan<int> iqMatrix = (transformType.ToClass() == Av1TransformClass.Class2D) ?
            Av1InverseQuantizationLookup.GetQuantizationMatrix(qmLevel, plane, qmTransformSize)
            : Av1InverseQuantizationLookup.GetQuantizationMatrix(Av1Constants.QuantificationMatrixLevelCount - 1, Av1Plane.Y, qmTransformSize);
        int shift = transformSize.GetScale();

        // The 2D inverse transform reads its input column-major at the destination height
        // stride (input[(col * destinationHeight) + row]; spec 7.13.3 row pass). The dequant
        // therefore has to place each scanned coefficient at that column-major slot. Two
        // conventions feed in: the scan grid is the qm-adjusted size (64-dim sizes use the
        // 32-bounded scan, so the height is expanded from adjustedHeight to destinationHeight),
        // and our non-square scan tables are stored transposed vs libaom (commit "Transpose
        // non-square scan tables..."), so the (row, col) recovery differs for non-square.
        int destinationHeight = transformSize.GetHeight();
        int adjustedWidth = qmTransformSize.GetWidth();
        int adjustedHeight = qmTransformSize.GetHeight();
        int adjustedHeightLog2 = Av1Math.Log2(adjustedHeight);
        bool squareScan = adjustedWidth == adjustedHeight;

        int coefficientCount = level[0];
        level = level[1..];
        int lev = level[0];
        int qCoefficient;
        if (lev != 0)
        {
            int pos = scanIndices[0];
            qCoefficient = (int)(((long)Math.Abs(lev) * GetDeQuantizedValue(dequantDc, pos, iqMatrix)) & 0xffffff);
            qCoefficient >>= shift;

            if (lev < 0)
            {
                qCoefficient = -qCoefficient;
            }

            qCoefficients[0] = Av1Math.Clamp(qCoefficient, minValue, maxValue);
        }

        for (int i = 1; i < coefficientCount; i++)
        {
            lev = level[i];
            if (lev != 0)
            {
                int pos = scanIndices[i];
                qCoefficient = (int)(((long)Math.Abs(lev) * GetDeQuantizedValue(dequantAc, pos, iqMatrix)) & 0xffffff);
                qCoefficient >>= shift;

                if (lev < 0)
                {
                    qCoefficient = -qCoefficient;
                }

                // Recover the coefficient's (row, col) in the adjusted grid, then place it
                // column-major at the destination height stride.
                int row;
                int col;
                if (squareScan)
                {
                    col = pos >> adjustedHeightLog2;
                    row = pos & (adjustedHeight - 1);
                }
                else
                {
                    col = pos % adjustedWidth;
                    row = pos / adjustedWidth;
                }

                int destPos = (col * destinationHeight) + row;
                qCoefficients[destPos] = Av1Math.Clamp(qCoefficient, minValue, maxValue);
            }
        }

        return coefficientCount;
    }

    /// <summary>
    /// SVT: get_dqv
    /// </summary>
    private static int GetDeQuantizedValue(short dequant, int coefficientIndex, ReadOnlySpan<int> iqMatrix)
    {
        const int bias = 1 << (Av1ScanOrderConstants.QuantizationMatrixWeightBits - 1);
        int deQuantifiedValue = dequant;

        deQuantifiedValue = ((iqMatrix[coefficientIndex] * deQuantifiedValue) + bias) >> Av1ScanOrderConstants.QuantizationMatrixWeightBits;
        return deQuantifiedValue;
    }
}
