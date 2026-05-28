// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// Spec 5.11.31 (<c>read_mv</c>) and 5.11.32 (<c>read_mv_component</c>). Decodes
/// a motion-vector difference in 1/8-pel units. Under intra block copy,
/// <c>force_integer_mv</c> is on and <c>allow_high_precision_mv</c> is off, so
/// the result has its low 3 bits zero (integer-pel only).
/// </summary>
internal static class Av1MotionVectorReader
{
    /// <summary>
    /// Spec 5.11.26 (<c>assign_mv</c>) for intra-block-copy blocks. Builds the
    /// IBC predictor stack from already-decoded neighbors (falling back to the
    /// synthesized predictor when no IBC candidate is found), reads the
    /// motion-vector difference, and writes the resulting absolute DV onto the
    /// block's mode info.
    /// </summary>
    public static void AssignIntraBlockCopyMotionVector(
        ref Av1SymbolDecoder reader,
        Av1PartitionInfo partitionInfo,
        Av1TileInfo tileInfo,
        int superblockModeInfoSize,
        Av1IntraBlockCopyMotionVectorStack.MiLookup lookupMi)
    {
        Av1BlockSize blockSize = partitionInfo.ModeInfo.BlockSize;
        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi,
            tileInfo,
            superblockModeInfoSize,
            partitionInfo.RowIndex,
            partitionInfo.ColumnIndex,
            blockSize.Get4x4WideCount(),
            blockSize.Get4x4HighCount());
        Av1MotionVector difference = ReadMotionVectorDifference(
            ref reader, forceIntegerMv: true, allowHighPrecisionMv: false, useDisplacementVectorContext: true);
        Av1MotionVector finalDv = predictor + difference;
        partitionInfo.ModeInfo.DisplacementVector = finalDv;

        if (Av1SymbolTrace.IbcEnabled)
        {
            Av1SymbolTrace.WriteIbcDv(
                partitionInfo.RowIndex,
                partitionInfo.ColumnIndex,
                (int)blockSize,
                predictor.Row,
                predictor.Col,
                finalDv.Row,
                finalDv.Col);
        }
    }

    /// <summary>
    /// Reads the motion-vector difference relative to a predictor. The returned
    /// vector is the diff only; callers add it to <c>PredMv</c>.
    /// </summary>
    /// <remarks>
    /// When <paramref name="useDisplacementVectorContext"/> is true, the IBC `ndvc`
    /// CDFs are used; otherwise the standard inter-MV `nmvc` CDFs apply.
    /// </remarks>
    public static Av1MotionVector ReadMotionVectorDifference(
        ref Av1SymbolDecoder reader,
        bool forceIntegerMv,
        bool allowHighPrecisionMv,
        bool useDisplacementVectorContext = false)
    {
        Av1MotionVectorJoint joint = useDisplacementVectorContext
            ? reader.ReadDisplacementVectorJoint()
            : reader.ReadMotionVectorJoint();
        short row = 0;
        short col = 0;

        if (joint is Av1MotionVectorJoint.VerticalNonZero or Av1MotionVectorJoint.HorizontalAndVerticalNonZero)
        {
            row = (short)ReadComponent(ref reader, Av1MotionVectorComponent.Vertical, forceIntegerMv, allowHighPrecisionMv, useDisplacementVectorContext);
        }

        if (joint is Av1MotionVectorJoint.HorizontalNonZero or Av1MotionVectorJoint.HorizontalAndVerticalNonZero)
        {
            col = (short)ReadComponent(ref reader, Av1MotionVectorComponent.Horizontal, forceIntegerMv, allowHighPrecisionMv, useDisplacementVectorContext);
        }

        return new Av1MotionVector(row, col);
    }

    private static int ReadComponent(
        ref Av1SymbolDecoder reader,
        Av1MotionVectorComponent comp,
        bool forceIntegerMv,
        bool allowHighPrecisionMv,
        bool useDv)
    {
        bool sign = useDv ? reader.ReadDisplacementVectorSign(comp) : reader.ReadMotionVectorSign(comp);
        int mvClass = useDv ? reader.ReadDisplacementVectorClass(comp) : reader.ReadMotionVectorClass(comp);
        int magnitude;

        if (mvClass == 0)
        {
            int class0Bit = useDv ? reader.ReadDisplacementVectorClass0Bit(comp) : reader.ReadMotionVectorClass0Bit(comp);
            int fr = forceIntegerMv ? 3 : (useDv ? reader.ReadDisplacementVectorClass0Fraction(comp, class0Bit) : reader.ReadMotionVectorClass0Fraction(comp, class0Bit));
            int hp = allowHighPrecisionMv ? (useDv ? reader.ReadDisplacementVectorClass0HighPrecision(comp) : reader.ReadMotionVectorClass0HighPrecision(comp)) : 1;
            magnitude = ((class0Bit << 3) | (fr << 1) | hp) + 1;
        }
        else
        {
            int d = 0;
            for (int i = 0; i < mvClass; i++)
            {
                int bit = useDv ? reader.ReadDisplacementVectorBit(comp, i) : reader.ReadMotionVectorBit(comp, i);
                d |= bit << i;
            }

            magnitude = Av1MotionVectorConstants.Class0Size << (mvClass + 2);
            int fr = forceIntegerMv ? 3 : (useDv ? reader.ReadDisplacementVectorFraction(comp) : reader.ReadMotionVectorFraction(comp));
            int hp = allowHighPrecisionMv ? (useDv ? reader.ReadDisplacementVectorHighPrecision(comp) : reader.ReadMotionVectorHighPrecision(comp)) : 1;
            magnitude += ((d << 3) | (fr << 1) | hp) + 1;
        }

        return sign ? -magnitude : magnitude;
    }
}
